using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;

namespace Shared
{
    // How a mod joins the in-game update: two calls from its Plugin, and the title badge's menu
    // does the rest. Island UI and Island Ex each compile this (Shared SOURCE, as TitleBadge),
    // and either badge starts the same update of whichever of the two are installed; what they
    // share is the folder on disk, so it does not matter which one the player clicked.
    //
    // A request happens only inside UpdateFlow, and UpdateFlow runs from the window's Check for
    // updates and Update buttons, and from StartupCheck below when the player turned that on.
    internal static class ModUpdate
    {
        // each copy of this class is its own type, so "I got all the way up" is left in the
        // AppDomain, the way Loc publishes its language: BepInEx alone cannot say, since a
        // plugin whose Awake threw still counts as loaded there
        private const string LoadedKey = "IslandMods.Update.Loaded.";
        // the same for the check at startup: the first running mod decides for this start, and
        // what it finds is read by both badges. Written on the main thread only.
        private const string StartupKey = "IslandMods.Update.StartupDecided";
        private const string NewerKey = "IslandMods.Update.Newer.";

        private static Assembly _assembly;
        private static string _name;
        private static string _version;
        private static UpdateDisk _disk;
        private static bool _noticed;
        private static bool _up;
        private static bool _decided;
        private static UpdateFlow _startupFlow;

        // first thing in Awake, with the badge
        internal static void Install(Assembly assembly, string name, string version)
        {
            _assembly = assembly;
            _name = name;
            _version = version;
            _disk = new UpdateDisk(Paths.GameRootPath);
            TitleBadge.Extras = Items;
            TitleBadge.Note = Note;
            TitleBadge.AtTitle = AtTitle;
            TitleBadge.Sign = Sign;
            TitleBadge.Tick = Tick;
        }

        // last thing in Awake, on every path that ends well: the mod got all the way up, which
        // is what lets a freshly swapped version stop being on probation
        internal static void Loaded()
        {
            AppDomain.CurrentDomain.SetData(LoadedKey + _name, _version);
            _up = true;
            TitleBadge.TitleCheck();
        }

        private static List<TitleBadge.Item> Items()
        {
            var items = new List<TitleBadge.Item>();
            string phase = _disk.Phase;
            if (phase == UpdateDisk.Staged)
            {
                items.Add(new TitleBadge.Item(Loc.Get("update.cancel_pending", "Cancel the update"), CancelPending));
            }
            else if (phase == UpdateDisk.Undo)
            {
                items.Add(new TitleBadge.Item(Loc.Get("update.keep_version", "Keep this version"), CancelPending));
            }
            else if (phase.Length == 0)
            {
                string found = Newer();
                string check = found.Length > 0 ? Loc.Get("update.check_found", "Check for updates: {0} available", found)
                    : Loc.Get("update.check", "Check for updates");
                items.Add(new TitleBadge.Item(check, OpenCheck));
            }
            string startup = _disk.CheckAtStartup ? Loc.Get("update.startup_on", "Check at startup: on")
                : Loc.Get("update.startup_off", "Check at startup: off");
            items.Add(new TitleBadge.Item(startup, ToggleStartupCheck, true));
            if (phase.Length == 0)
            {
                UpdateDisk.Plan previous = _disk.PreviousPlan;
                string back = previous != null ? Summary(previous, true) : "";
                if (back.Length > 0)
                {
                    items.Add(new TitleBadge.Item(Loc.Get("update.go_back", "Go back to {0}", back), () =>
                        UpdatePanel.ConfirmBack(_disk, back)));
                }
            }
            return items;
        }

        // The menu's Check for updates line on its own, for a notice that stands beside the
        // badges and points at the update. Null while an update is staged or a way back chosen,
        // when the menu has no such line either. It is built as a menu line, so a press on it is
        // the press tests/update/network-callers.ps1 holds every request to.
        internal static TitleBadge.Item CheckLine()
        {
            if (_disk == null || _disk.Phase.Length != 0)
            {
                return null;
            }
            string found = Newer();
            string check = found.Length > 0 ? Loc.Get("update.check_found", "Check for updates: {0} available", found)
                : Loc.Get("update.check", "Check for updates");
            return new TitleBadge.Item(check, OpenCheck);
        }

        // what the check at startup found for this mod, while nothing else is under way. Empty:
        // nothing to tell.
        private static string Newer()
        {
            return _disk.Phase.Length == 0 ? AppDomain.CurrentDomain.GetData(NewerKey + _name) as string ?? "" : "";
        }

        // the update sign's words, in the badge's tip and menu
        private static string Sign()
        {
            string found = Newer();
            return found.Length > 0 ? Loc.Get("update.sign", "version {0} is available", found) : "";
        }

        // the coral pip's words. Empty: this mod is not about to change.
        private static string Note()
        {
            string phase = _disk.Phase;
            if (phase == UpdateDisk.Staged)
            {
                UpdateDisk.ModChange own = Own(_disk.PendingPlan);
                return own == null ? "" : Loc.Get("update.note_pending", "updates to {0} at the next start", own.To);
            }
            if (phase == UpdateDisk.Undo)
            {
                UpdateDisk.ModChange own = Own(_disk.PreviousPlan);
                return own == null ? "" : Loc.Get("update.note_back", "goes back to {0} at the next start", own.From);
            }
            return "";
        }

        private static UpdateDisk.ModChange Own(UpdateDisk.Plan plan)
        {
            if (plan != null)
            {
                foreach (UpdateDisk.ModChange mod in plan.Mods)
                {
                    if (mod.Name == _name)
                    {
                        return mod;
                    }
                }
            }
            return null;
        }

        internal static string Summary(UpdateDisk.Plan plan, bool from)
        {
            var parts = new List<string>();
            foreach (UpdateDisk.ModChange mod in plan.Mods)
            {
                parts.Add(mod.Name + " " + (from ? mod.From : mod.To));
            }
            return string.Join(", ", parts.ToArray());
        }

        private static void OpenCheck()
        {
            UpdatePanel.OpenCheck(new UpdateFlow(_disk, _assembly, _name, _version), _disk);
        }

        // reaches for nothing: it counts from the next start
        private static void ToggleStartupCheck()
        {
            try
            {
                _disk.CheckAtStartup = !_disk.CheckAtStartup;
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning("Island update: could not save Check at startup: " + e.Message);
                UpdatePanel.ShowOptionNotSaved();
            }
        }

        private static void CancelPending()
        {
            _disk.CancelPending();
            TitleBadge.RefreshAll();
        }

        // Runs when the title screen is up, from whichever mod gets here first and again from
        // the other. The only way to the network from here is StartupCheck.
        private static void AtTitle()
        {
            if (_disk.Phase == UpdateDisk.Applied)
            {
                TryConfirm();
            }
            if (_up && _disk.Phase != UpdateDisk.Applied)
            {
                StartupCheck();
            }
            if (_noticed)
            {
                return;
            }
            _noticed = true;
            string notice = _disk.TakeNotice();
            if (notice != null)
            {
                UpdatePanel.ShowNotice(notice);
            }
        }

        // THE ONE REQUEST NOT STARTED BY A PRESS: the player's press was turning the option on.
        // At the title screen, once in a game's life, however many mods are here, and only by a
        // mod that is running. Decided once per mod, with the option read at that moment, so
        // turning it on during a session does nothing until the next start. It asks and it
        // tells: this class never calls Download. tests/update/network-callers.ps1 holds this
        // method to that.
        private static void StartupCheck()
        {
            if (_decided)
            {
                return;
            }
            _decided = true;
            if (!TitleBadge.Running || AppDomain.CurrentDomain.GetData(StartupKey) != null)
            {
                return;
            }
            AppDomain.CurrentDomain.SetData(StartupKey, _name);
            if (!_disk.CheckAtStartup)
            {
                return;
            }
            if (_disk.Phase.Length != 0)
            {
                return;     // something is already under way; the menu has no Check line to tell on
            }
            _startupFlow = new UpdateFlow(_disk, _assembly, _name, _version);
            _startupFlow.Check();
        }

        // Every frame a badge exists, so an answer that comes while the player is in a save waits
        // for the title screen. A failure leaves the screen as it was; UpdateFlow has logged it.
        private static void Tick()
        {
            UpdateFlow flow = _startupFlow;
            if (flow == null || flow.Current == UpdateFlow.Step.Checking)
            {
                return;
            }
            _startupFlow = null;
            if (flow.Current == UpdateFlow.Step.UpToDate)
            {
                UnityEngine.Debug.Log("Island update: the check at startup found nothing newer.");
            }
            if (flow.Current != UpdateFlow.Step.Offer)
            {
                return;
            }
            foreach (UpdateFlow.Part part in flow.Parts)
            {
                AppDomain.CurrentDomain.SetData(NewerKey + part.Mod.Name, part.Version);
                UnityEngine.Debug.Log("Island update: the check at startup found " + part.Mod.Name + " " + part.Version + ".");
            }
            TitleBadge.RefreshAll();
        }

        private static void TryConfirm()
        {
            UpdateDisk.Plan plan = _disk.PendingPlan;
            if (plan == null)
            {
                return;
            }
            foreach (UpdateDisk.ModChange mod in plan.Mods)
            {
                if (AppDomain.CurrentDomain.GetData(LoadedKey + mod.Name) as string != mod.To)
                {
                    return;
                }
            }
            try
            {
                _disk.Confirm();
                UnityEngine.Debug.Log("Island update: " + Summary(plan, false) + " loaded, keeping the previous version.");
                TitleBadge.RefreshAll();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("Island update: could not finish: " + e.Message);
            }
        }

    }
}
