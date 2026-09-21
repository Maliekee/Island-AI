// Copyright (c) 2026 Maliekee
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace IslandAI
{
    // Changes how NPCs and enemies move. Its own mod because it alters gameplay: walls
    // stop less once enemies can walk around them, so it must be removable on its own.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "madisland.islandai";
        public const string PluginName = "Mad Island AI";
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SteerEnemies;
        internal static ConfigEntry<bool> SteerFriends;
        internal static ConfigEntry<float> ProbeDistance;
        internal static ConfigEntry<SteerMethod> Method;
        internal static ConfigEntry<float> Clearance;

        // How a blocked NPC picks its way. There is no best one, so they stay side by side and
        // the choice is a setting.
        internal enum SteerMethod
        {
            AlongWalls,
            Fan,
        }

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true,
                "Master switch, live: off = NPCs and enemies move exactly as in the unmodded game. Clicking the mod's icon on the title screen flips this.");
            SteerEnemies = Config.Bind("Steering", "Enemies", true,
                "Enemies chasing a target walk around obstacles instead of pushing into them. Makes walls less of a defence.");
            SteerFriends = Config.Bind("Steering", "Friends", true,
                "Followers and villagers walk around obstacles when following you or chasing an enemy.");
            ProbeDistance = Config.Bind("Steering", "Probe Distance", 2f,
                new ConfigDescription("How far ahead, in metres, an NPC looks for something in its way.",
                    new AcceptableValueRange<float>(0.5f, 6f)));
            Method = Config.Bind("Steering", "Method", SteerMethod.AlongWalls,
                "Live. AlongWalls: a blocked NPC tries the direction of the wall it met as well as a fan of headings, and takes the one with the most room nearest its target; it finds narrow passages. Fan: the first cut; the nearest of eleven fixed headings that is completely clear, which walks past most narrow passages.");
            Clearance = Config.Bind("Steering", "Clearance", 1.1f,
                new ConfigDescription("Live. How wide the look-ahead is, as a share of the NPC's own body. Above 1 rounds corners with room to spare and refuses tight gaps; below 1 squeezes into gaps barely wider than the body and brushes corners.",
                    new AcceptableValueRange<float>(0.8f, 1.3f)));

            var harmony = new Harmony(PluginGuid);
            Shared.PatchCensus.ApplyAndReport(harmony, Assembly.GetExecutingAssembly(), Log);
            // live: Steering asks Enabled every time, so running and wanted are the same answer
            Shared.TitleBadge.Install(Assembly.GetExecutingAssembly(), "IslandAI.icon.png", "Island AI", PluginVersion,
                () => Enabled.Value, () => Enabled.Value, on =>
                {
                    Enabled.Value = on;
                    Log.LogInfo(PluginName + " switched " + (on ? "on" : "off") + " from the title screen.");
                });
        }
    }
}
