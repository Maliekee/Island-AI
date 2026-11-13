using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shared
{
    // FREE MODE SWITCHES THE ISLAND MODS OFF (beta 26642668). The game's beta added a free
    // mode with co-op (docs/beta-systems.md section 2), and the mods do not support it yet:
    // Island UI performs game actions itself (pickup, chests, crafting, NPC orders), which
    // co-op's sync never sees, and its state is not kept in free mode's saves. Until the
    // refactoring makes that right, every Island mod steps out of free mode entirely, so a
    // free-mode game is the unmodded game.
    //
    // The free-mode scene is found as the game finds it (FreeModeScene.IsFreeMode compares
    // the scene's path). On its load each mod runs its own `goDormant` - Island UI its reload
    // teardown, the others unpatch and stop - and stays off until the game restarts: a mod
    // cannot safely patch itself back in inside the same process (Island UI's teardown is
    // built for a fresh assembly to follow). The settings are left as they were, so after a
    // restart every mod is on again by itself.
    //
    // The player is told twice: one line in the game's own message log as free mode starts,
    // naming the Island mods that are installed (whichever mod gets there first says it for
    // all, through an AppDomain flag - the copies of this file share no type), and on the
    // title screen afterwards, where each mod's icon shows it is off and why (TitleBadge reads
    // Note) and Island UI's title notice says what to do.
    //
    // Shared SOURCE: each mod compiles its own copy, so Dormant is per mod.
    internal static class FreeModeGate
    {
        internal const string FreeModeScenePath = "Assets/Resources/AI_workspace/FreeMode/Scenes/FreeMode_Original.unity";
        private const string AnnouncedKey = "IslandMods.FreeModeAnnounced";

        // every Island mod that switches off in free mode, by GUID, with the name the player knows
        private static readonly string[][] Mods =
        {
            new[] { "madisland.islandui", "Island UI" },
            new[] { "madisland.islandex", "Island Ex" },
            new[] { "madisland.islandai", "Island AI" },
        };

        private static string _mod;
        private static Action _goDormant;

        // this mod switched itself off for free mode, until the game restarts
        internal static bool Dormant { get; private set; }

        // Before the mod's own sceneLoaded handlers, so free mode is caught first.
        internal static void Install(string modName, Action goDormant)
        {
            _mod = modName;
            _goDormant = goDormant;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        internal static bool IsFreeMode(Scene scene)
        {
            return scene.IsValid() && scene.path == FreeModeScenePath;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Dormant && IsFreeMode(scene))
            {
                Enter();
            }
        }

        // also the harness's way to see it without starting free mode
        internal static void Enter()
        {
            if (Dormant)
            {
                return;
            }
            Dormant = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Debug.Log("[" + _mod + "] Free mode: switching off until the game restarts (free mode is not supported yet).");
            try { _goDormant?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[" + _mod + "] Free mode: switching off threw: " + e); }
            Announce();
        }

        // the badge's line while dormant (TitleBadge.Words)
        internal static string Note()
        {
            return Dormant
                ? Loc.Get("freemode.badge", "Off since free mode, which it does not support yet. Restart the game to turn it back on.")
                : "";
        }

        // the installed Island mods, by name, for the message
        internal static List<string> InstalledNames()
        {
            var names = new List<string>();
            foreach (string[] mod in Mods)
            {
                if (Chainloader.PluginInfos.ContainsKey(mod[0]))
                {
                    names.Add(mod[1]);
                }
            }
            return names;
        }

        private static void Announce()
        {
            if (AppDomain.CurrentDomain.GetData(AnnouncedKey) != null)
            {
                return;
            }
            AppDomain.CurrentDomain.SetData(AnnouncedKey, true);
            var runner = new GameObject("IslandFreeModeNotice");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<Runner>().StartCoroutine(Say(runner));
        }

        private sealed class Runner : MonoBehaviour
        {
        }

        // once the free-mode scene has its managers; the game's own message log, nothing of ours
        private static IEnumerator Say(GameObject runner)
        {
            // a free-mode world loads slowly (it captures the island map first): minutes, not seconds
            float until = Time.realtimeSinceStartup + 600f;
            ManagersScript mn = null;
            while (Time.realtimeSinceStartup < until)
            {
                GameObject managers = GameObject.Find("Managers");
                mn = managers != null ? managers.GetComponent<ManagersScript>() : null;
                if (mn != null && mn.allSet && mn.uiMN != null)
                {
                    break;
                }
                mn = null;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yield return new WaitForSecondsRealtime(2f);    // after the scene's own opening lines
            try
            {
                if (mn != null && mn.uiMN != null)
                {
                    mn.uiMN.GoLogText(Loc.Get("freemode.log", "{0}: off in free mode for now. Restart the game to use them again.",
                        string.Join(", ", InstalledNames().ToArray())));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Free mode notice: " + e.Message);
            }
            UnityEngine.Object.Destroy(runner);
        }
    }
}
