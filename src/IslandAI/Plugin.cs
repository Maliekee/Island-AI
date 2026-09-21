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
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> SteerEnemies;
        internal static ConfigEntry<bool> SteerFriends;
        internal static ConfigEntry<float> ProbeDistance;

        private void Awake()
        {
            Log = Logger;

            SteerEnemies = Config.Bind("Steering", "Enemies", true,
                "Enemies chasing a target walk around obstacles instead of pushing into them. Makes walls less of a defence.");
            SteerFriends = Config.Bind("Steering", "Friends", true,
                "Followers and villagers walk around obstacles when following you or chasing an enemy.");
            ProbeDistance = Config.Bind("Steering", "Probe Distance", 2f,
                new ConfigDescription("How far ahead, in metres, an NPC looks for something in its way.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            var harmony = new Harmony(PluginGuid);
            Shared.PatchCensus.ApplyAndReport(harmony, Assembly.GetExecutingAssembly(), Log);
        }
    }
}
