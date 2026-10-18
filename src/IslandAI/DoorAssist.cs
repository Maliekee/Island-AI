using UnityEngine;

namespace IslandAI
{
    /// <summary>
    /// Lets a friend out of a house when they have walked to its door and cannot touch it.
    ///
    /// Leaving a house is a trigger: NPCMove.Travel walks an NPC inside a house to its door
    /// (NPCManager.FindDoor), and the door's OnTriggerEnter calls NPCManager.HouseOut. In the
    /// Native House the exit trigger is 0.62 by 0.5 and sits mostly inside the front wall; the
    /// sliver that reaches into the room is about 0.15 deep. Travel aims at the door's position,
    /// which is behind the wall, drops to a crawl within a metre of it, and wall friction keeps
    /// the NPC from sliding sideways onto the sliver - so they stand at the wall, beside the
    /// door, for good (found 2026-09-24: three friends at the same z, 0.3 to 0.6 left of the
    /// trigger).
    ///
    /// So a friend travelling inside their house, within Reach of its exit, gets the call the
    /// trigger would have made - the game's own HouseOut, with the game's own door and its own
    /// refusal (sexInfo[7]) kept. Nothing else about how they move changes.
    /// </summary>
    internal class DoorAssist : MonoBehaviour
    {
        // The stuck NPCs stood 0.73 to 1.0 from the door's position (the wall between).
        private const float Reach = 1.2f;
        private const float Interval = 0.25f;

        private static NPCManager _npcMN;
        private NPCMove _nm;
        private PlayerMove _pm;
        private float _next;

        private void Awake()
        {
            _nm = GetComponent<NPCMove>();
            _pm = GetComponent<PlayerMove>();
        }

        private void Update()
        {
            if (Time.time < _next)
            {
                return;
            }
            _next = Time.time + Interval;
            if (!Plugin.Enabled.Value || !Plugin.DoorAssist.Value || _nm == null
                || _nm.actType != NPCMove.ActType.Travel || Steering.PlayerDriven(_pm))
            {
                return;
            }
            CommonStates c = _nm.common;
            if (c == null || c.houseID == 0 || c.sexInfo == null || c.sexInfo.Length < 8 || c.sexInfo[7] != 0)
            {
                return;
            }
            if (_npcMN == null)
            {
                _npcMN = GameObject.Find("NPCManager")?.GetComponent<NPCManager>();
                if (_npcMN == null)
                {
                    return;
                }
            }
            // the door Travel is walking to, and only a house's own exit
            DoorScript door = _npcMN.FindDoor(c, frontOnly: true);
            if (door == null || door.gameObject.name != "in_door" || door.houseInfo == null
                || door.houseInfo.houseID != c.houseID)
            {
                return;
            }
            Vector3 to = door.transform.position - transform.position;
            to.y = 0f;
            if (to.magnitude > Reach)
            {
                return;
            }
            Plugin.Log.LogInfo("Door assist: " + c.charaName + " let out of house " + c.houseID
                + " (" + to.magnitude.ToString("0.00") + " from the door)");
            _npcMN.HouseOut(c, door);
        }
    }
}
