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
    /// velocity to the best free heading when something is in the way: along what it hit, or a
    /// fan around the target line, scored by room. The coroutines stay untouched, so every
    /// other rule they carry (give-up distance, water, attacks) still holds.
    ///
    /// It is local steering, not a path: it walks around trees, rocks, corners and along walls,
    /// and it does NOT solve a concave trap (a U of walls, a closed base).
    /// </summary>
    internal class Steering : MonoBehaviour
    {
        // The fan of headings tried, either side of the target line, when it is blocked.
        private static readonly float[] Angles = { 30f, 60f, 90f, 120f, 150f };

        private const float EvalInterval = 0.1f;
        // How long a chosen heading outlives the last blocked probe, so an NPC rounding a wall
        // does not turn back each time the straight line looks free for a moment.
        private const float CommitSeconds = 1f;
        // A heading with less free room than this is not a way out.
        private const float MinRoom = 0.5f;
        // A heading scores its free room as a share of the probe, less these: a half turn away
        // from the target costs TurnCost, a half turn away from the committed heading SwerveCost.
        // The second outweighs the first, so a wall is followed to its end, not back and forth.
        private const float TurnCost = 0.5f;
        private const float SwerveCost = 1f;
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
        private Vector3 _steer;     // the flat world heading walked, zero = straight at the target
        private Vector3 _commit;    // the last heading chosen, kept past the block; zero = none
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
                _steer = Vector3.zero;
                _commit = Vector3.zero;
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
            if (_steer == Vector3.zero)
            {
                return;
            }

            // The heading is a world one, as a wall is: it does not swing with the target between
            // evaluations, and it is never built from the velocity already turned.
            Rigidbody rb = _nm.rb;
            Vector3 v = rb.velocity;
            float speed = new Vector3(v.x, 0f, v.z).magnitude;
            rb.velocity = _steer * speed + new Vector3(0f, v.y, 0f);
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
            if (!Plugin.Enabled.Value)
            {
                return false;
            }
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
            if (Room(desired, probe, out Vector3 normal) >= probe)
            {
                _steer = Vector3.zero;
                if (Time.time - _blockedAt > CommitSeconds)
                {
                    _commit = Vector3.zero;
                }
                return;
            }

            _blockedAt = Time.time;
            // Either method leaves zero when boxed in: the heading is left alone and the game's
            // own unstick move fires.
            _steer = Plugin.Method.Value == Plugin.SteerMethod.Fan
                ? Fan(desired, probe)
                : AlongWalls(desired, probe, normal);
            _commit = _steer;
        }

        // The first cut: the nearest heading of the fan that is clear for the whole probe, on the
        // side already taken if there is one.
        private Vector3 Fan(Vector3 desired, float probe)
        {
            if (_commit == Vector3.zero)
            {
                foreach (float a in Angles)
                {
                    bool left = Room(Turn(desired, -a), probe) >= probe;
                    bool right = Room(Turn(desired, a), probe) >= probe;
                    if (left || right)
                    {
                        int pick = left && right ? (Random.value < 0.5f ? -1 : 1) : (right ? 1 : -1);
                        return Turn(desired, pick * a);
                    }
                }
                return Vector3.zero;
            }

            int side = Vector3.SignedAngle(desired, _commit, Vector3.up) < 0f ? -1 : 1;
            foreach (int s in new[] { side, -side })
            {
                foreach (float a in Angles)
                {
                    if (Room(Turn(desired, s * a), probe) >= probe)
                    {
                        return Turn(desired, s * a);
                    }
                }
            }
            return Vector3.zero;
        }

        private Vector3 AlongWalls(Vector3 desired, float probe, Vector3 normal)
        {
            Vector3 best = Vector3.zero;
            float bestScore = float.MinValue;
            // Equal scores go to the first asked, so which side is asked first is the coin.
            int first = Random.value < 0.5f ? -1 : 1;

            // Along what is in the way, both ways. The fan cannot stand in for this: a probe down
            // a narrow corridor clears only within a few degrees of its axis, and the fan steps
            // by thirty from a line that has nothing to do with the corridor.
            Vector3 flat = new Vector3(normal.x, 0f, normal.z);
            if (flat.sqrMagnitude > 0.01f)
            {
                Vector3 along = Vector3.Cross(Vector3.up, flat).normalized * first;
                Consider(along, desired, probe, ref best, ref bestScore);
                Consider(-along, desired, probe, ref best, ref bestScore);
            }
            foreach (float a in Angles)
            {
                Consider(Turn(desired, first * a), desired, probe, ref best, ref bestScore);
                Consider(Turn(desired, -first * a), desired, probe, ref best, ref bestScore);
            }

            return best;
        }

        private void Consider(Vector3 dir, Vector3 desired, float probe, ref Vector3 best, ref float bestScore)
        {
            float room = Room(dir, probe);
            if (room < Mathf.Min(MinRoom, probe))
            {
                return;
            }
            float score = room / probe - TurnCost * Vector3.Angle(desired, dir) / 180f;
            if (_commit != Vector3.zero)
            {
                score -= SwerveCost * (1f - Vector3.Dot(_commit, dir)) * 0.5f;
            }
            if (score > bestScore)
            {
                best = dir;
                bestScore = score;
            }
        }

        private static Vector3 Turn(Vector3 dir, float degrees)
        {
            return Quaternion.AngleAxis(degrees, Vector3.up) * dir;
        }

        private float Room(Vector3 dir, float dist)
        {
            return Room(dir, dist, out _);
        }

        /// <summary>
        /// Free distance along <paramref name="dir"/>, capped at <paramref name="dist"/>, and the
        /// normal of what ends it (zero when nothing does).
        /// </summary>
        private float Room(Vector3 dir, float dist, out Vector3 normal)
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
            // Wider than the body by default, so a corner it clears is a corner the body clears;
            // narrower squeezes into tighter gaps (the Clearance setting).
            float probeRadius = radius * Plugin.Clearance.Value;
            Vector3 origin = new Vector3(center.x, center.y - half + StepHeight + probeRadius, center.z);

            // A sphere cast never reports what it already overlaps, and the wide probe overlaps a
            // wall the body is pressed against (a knockback, a chase begun at the wall). A thin
            // one from the body's middle starts clear of it.
            float wide = Cast(origin, probeRadius, dir, dist, out normal);
            return wide < dist ? wide : Cast(origin, radius * 0.5f, dir, dist, out normal);
        }

        private float Cast(Vector3 origin, float radius, Vector3 dir, float dist, out Vector3 normal)
        {
            normal = Vector3.zero;
            if (!Physics.SphereCast(origin, radius, dir, out RaycastHit hit, dist, _mask, QueryTriggerInteraction.Ignore)
                || hit.normal.y > WalkableNormalY)
            {
                return dist;
            }
            normal = hit.normal;
            return hit.distance;
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
