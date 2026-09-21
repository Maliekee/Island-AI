// Copyright (c) 2026 Maliekee
// SPDX-License-Identifier: GPL-3.0-only

using HarmonyLib;
using UnityEngine;

namespace IslandAI
{
    /// <summary>
    /// Obstacle avoidance for NPCs that are chasing or following.
    ///
    /// The game has no pathfinding: NPCMove.Chase and NPCMove.Follow are coroutines that write
    /// rb.velocity straight at the target every frame, and nothing looks ahead. This component
    /// runs in LateUpdate — after the coroutines, before the next physics step — and turns that
    /// velocity to the nearest free heading when something is in the way. The coroutines stay
    /// untouched, so every other rule they carry (give-up distance, water, attacks) still holds.
    ///
    /// It is local steering, not a path: it walks around trees, rocks, corners and along walls,
    /// and it does NOT solve a concave trap (a U of walls, a closed base).
    /// </summary>
    internal class Steering : MonoBehaviour
    {
        // Headings tried, nearest the target first, when the straight line is blocked.
        private static readonly float[] Angles = { 30f, 60f, 90f, 120f, 150f };

        private const float EvalInterval = 0.1f;
        // How long a chosen side outlives the last blocked probe, so an NPC rounding a wall
        // does not flip sides each time the straight line looks free for a moment.
        private const float CommitSeconds = 1f;
        // Probes ride this far above the feet, so kerbs and low steps are not obstacles.
        private const float StepHeight = 0.3f;
        // A hit whose normal points this far up is a slope to walk on, not a wall.
        private const float WalkableNormalY = 0.6f;
        // The unstick sidestep (NPCMove.Interval) travels at most this far.
        private const float SidestepRoom = 4f;

        private static GameManager _gameMN;

        private NPCMove _nm;
        private int _mask;
        private bool _maskSet;
        private float _offset;      // degrees the heading is turned by, 0 = straight at the target
        private int _side;          // committed side, -1 left / +1 right, 0 = none
        private float _blockedAt;
        private float _nextEval;

        /// <summary>The flat heading to the target, last time this NPC was chasing or following.</summary>
        internal Vector3 LastDesired { get; private set; }

        private void Awake()
        {
            _nm = GetComponent<NPCMove>();
        }

        private void LateUpdate()
        {
            if (!Wanted() || !Target(out Vector3 target))
            {
                _offset = 0f;
                _side = 0;
                return;
            }

            Vector3 to = target - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.01f)
            {
                return;
            }
            Vector3 desired = to / dist;
            LastDesired = desired;

            if (Time.time >= _nextEval)
            {
                _nextEval = Time.time + EvalInterval;
                Evaluate(desired, dist);
            }
            if (_offset == 0f)
            {
                return;
            }

            // Rebuilt from the target each frame, never from the velocity already turned.
            Rigidbody rb = _nm.rb;
            Vector3 v = rb.velocity;
            float speed = new Vector3(v.x, 0f, v.z).magnitude;
            Vector3 dir = Quaternion.AngleAxis(_offset, Vector3.up) * desired;
            rb.velocity = dir * speed + new Vector3(0f, v.y, 0f);
        }

        private bool Wanted()
        {
            if (_nm == null || _nm.rb == null || _nm.rb.isKinematic || _nm.capColl == null)
            {
                return false;
            }
            if (_nm.actType != NPCMove.ActType.Chase && _nm.actType != NPCMove.ActType.Follow)
            {
                return false;
            }
            // Swimmers and fliers move in 3D through their own branches of Chase.
            if (_nm.subType == NPCMove.SubType.Air || _nm.subType == NPCMove.SubType.Water || _nm.inWater == 2)
            {
                return false;
            }
            return Enabled(_nm);
        }

        internal static bool Enabled(NPCMove nm)
        {
            bool friend = nm.npcType == NPCMove.NPCType.Friend || nm.npcType == NPCMove.NPCType.Follow;
            return friend ? Plugin.SteerFriends.Value : Plugin.SteerEnemies.Value;
        }

        private bool Target(out Vector3 pos)
        {
            GameObject t;
            if (_nm.actType == NPCMove.ActType.Chase)
            {
                t = _nm.tmpEnemy;
            }
            else
            {
                if (_gameMN == null)
                {
                    _gameMN = GameObject.Find("GameManager")?.GetComponent<GameManager>();
                }
                t = _gameMN != null ? _gameMN.player : null;
            }
            pos = t != null ? t.transform.position : Vector3.zero;
            return t != null;
        }

        private void Evaluate(Vector3 desired, float targetDist)
        {
            // Never probe past the target: a wall BEHIND the player is not in the way.
            float probe = Mathf.Min(Plugin.ProbeDistance.Value, targetDist);
            if (Room(desired, probe) >= probe)
            {
                _offset = 0f;
                if (_side != 0 && Time.time - _blockedAt > CommitSeconds)
                {
                    _side = 0;
                }
                return;
            }

            _blockedAt = Time.time;
            if (_side == 0)
            {
                foreach (float a in Angles)
                {
                    bool left = Room(Turn(desired, -a), probe) >= probe;
                    bool right = Room(Turn(desired, a), probe) >= probe;
                    if (left || right)
                    {
                        _side = left && right ? (Random.value < 0.5f ? -1 : 1) : (right ? 1 : -1);
                        _offset = _side * a;
                        return;
                    }
                }
                _offset = 0f;
                return;
            }

            if (TrySide(desired, probe, _side))
            {
                return;
            }
            if (TrySide(desired, probe, -_side))
            {
                _side = -_side;
                return;
            }
            // Boxed in: leave the heading alone and let the game's own unstick move fire.
            _offset = 0f;
        }

        private bool TrySide(Vector3 desired, float probe, int side)
        {
            foreach (float a in Angles)
            {
                if (Room(Turn(desired, side * a), probe) >= probe)
                {
                    _offset = side * a;
                    return true;
                }
            }
            return false;
        }

        private static Vector3 Turn(Vector3 dir, float degrees)
        {
            return Quaternion.AngleAxis(degrees, Vector3.up) * dir;
        }

        /// <summary>Free distance along <paramref name="dir"/>, capped at <paramref name="dist"/>.</summary>
        private float Room(Vector3 dir, float dist)
        {
            if (!_maskSet)
            {
                _mask = ObstacleMask(gameObject.layer);
                _maskSet = true;
            }

            CapsuleCollider c = _nm.capColl;
            Vector3 s = transform.lossyScale;
            float radius = c.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            float half = c.direction == 1 ? Mathf.Max(c.height * 0.5f * Mathf.Abs(s.y), radius) : radius;
            Vector3 center = transform.TransformPoint(c.center);
            // A little wider than the body, so a corner it clears is a corner the body clears.
            float probeRadius = radius * 1.1f;
            Vector3 origin = new Vector3(center.x, center.y - half + StepHeight + probeRadius, center.z);

            // A sphere cast never reports what it already overlaps, and the wide probe overlaps a
            // wall the body is pressed against (a knockback, a chase begun at the wall). A thin
            // one from the body's middle starts clear of it.
            float wide = Cast(origin, probeRadius, dir, dist);
            return wide < dist ? wide : Cast(origin, radius * 0.5f, dir, dist);
        }

        private float Cast(Vector3 origin, float radius, Vector3 dir, float dist)
        {
            if (!Physics.SphereCast(origin, radius, dir, out RaycastHit hit, dist, _mask, QueryTriggerInteraction.Ignore))
            {
                return dist;
            }
            return hit.normal.y > WalkableNormalY ? dist : hit.distance;
        }

        // What this NPC's body physically collides with, minus characters: the same exclusions
        // as the game's own NPCMove.collLayer.
        private static int ObstacleMask(int layer)
        {
            int mask = 0;
            for (int l = 0; l < 32; l++)
            {
                if (!Physics.GetIgnoreLayerCollision(layer, l))
                {
                    mask |= 1 << l;
                }
            }
            return mask & ~LayerMask.GetMask("NPC", "Player", "Chara", "Bullet", "Water", "FX", "Light");
        }

        /// <summary>A true perpendicular to the last heading, on the side with more room.</summary>
        internal Vector3 Sidestep()
        {
            Vector3 right = Vector3.Cross(Vector3.up, LastDesired);
            float roomRight = Room(right, SidestepRoom);
            float roomLeft = Room(-right, SidestepRoom);
            if (Mathf.Approximately(roomRight, roomLeft))
            {
                return Random.value < 0.5f ? right : -right;
            }
            return roomRight > roomLeft ? right : -right;
        }
    }

    [HarmonyPatch(typeof(NPCMove), "Awake")]
    internal static class AttachSteering
    {
        private static void Postfix(NPCMove __instance)
        {
            if (__instance.GetComponent<Steering>() == null)
            {
                __instance.gameObject.AddComponent<Steering>();
            }
        }
    }

    // The game's unstick move: every 3 s a chasing or following NPC that has barely moved
    // sidesteps for ~2 s. It picks the direction with `Quaternion.Euler(dir) * Vector3.right`,
    // which reads a unit vector as Euler DEGREES — a rotation of under 1° — so the sidestep is
    // always world ±X whatever the heading, on a coin-flipped side. Interval is a coroutine, so
    // this prefix runs when it is created, before its body reads avoidDir.
    [HarmonyPatch(typeof(NPCMove), nameof(NPCMove.Interval))]
    internal static class SidestepFix
    {
        private static void Prefix(NPCMove __instance)
        {
            if (__instance.avoidDir == Vector3.zero || !Steering.Enabled(__instance))
            {
                return;
            }
            Steering s = __instance.GetComponent<Steering>();
            if (s == null || s.LastDesired == Vector3.zero || __instance.capColl == null)
            {
                return;
            }
            __instance.avoidDir = s.Sidestep();
        }
    }
}
