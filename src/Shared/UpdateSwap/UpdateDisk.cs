using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Shared
{
    // THE SWAP. Everything an update leaves on disk, and the one place that changes the mod's
    // files. It runs at the very start of the game, from a BepInEx preloader patcher, because
    // Windows locks a DLL the chainloader has loaded; the mods compile this same file to write
    // a plan and to read how one went. No Unity, no network.
    //
    //   BepInEx/island-update/
    //     state.txt      one word, the phase; no file = nothing under way
    //     plan.txt       what the next start does (written complete, before the phase)
    //     staged/        the new files, laid out as from the game folder
    //     rollback/      what the swap replaced, until the new version has loaded
    //     previous/      the same once it has: the player's way back
    //     notice.txt     how a start went wrong, for the title screen to say once
    //     check-at-startup.txt   the player's choice, "on"; no file = off. Not part of any
    //                    update: nothing here that clears, swaps or goes back names it
    //
    // THE PHASES, each written only after the work it promises is on disk:
    //   staged    a plan is ready. The next start swaps.
    //   swapping  the swap began. Found at a start, the last one died in the middle: put back.
    //   applied   the swap is done, the new version has not said it loaded. Found at a start,
    //             it never did: put back. The title screen ends this phase (Confirm).
    //   undo      the player chose to go back. The next start restores previous/.
    //
    // Every file a plan names carries the hash it must have before and after, so a plan that
    // no longer fits the folder (the player ran the installer in between) does nothing, and a
    // swap that goes wrong halfway is put back from rollback/. A path in a plan is checked here
    // too, not only when it was written: this code never leaves the mod's own folders.
    internal sealed class UpdateDisk
    {
        // what the plan's words mean; a patcher ignores a plan of any other format, so a later
        // mod that changes them ships a patcher of its own beside this one
        internal const int Format = 1;
        internal const string SubFolder = "BepInEx/island-update";

        internal const string Staged = "staged";
        internal const string Swapping = "swapping";
        internal const string Applied = "applied";
        internal const string Undo = "undo";

        internal sealed class ModChange
        {
            internal string Name, From, To;
        }

        internal sealed class FileChange
        {
            internal string Path;       // from the game folder, forward slashes
            internal string Now;        // its hash after the swap; "" = the swap removes it
            internal string Was;        // its hash before; "" = it was not there
        }

        internal sealed class Plan
        {
            internal readonly List<ModChange> Mods = new List<ModChange>();
            internal readonly List<FileChange> Files = new List<FileChange>();
        }

        internal readonly string Root;
        private readonly string _dir;

        internal UpdateDisk(string gameRoot)
        {
            Root = gameRoot;
            _dir = Path.Combine(gameRoot, SubFolder.Replace('/', Path.DirectorySeparatorChar));
        }

        internal string Dir { get { return _dir; } }
        internal string StagedDir { get { return Path.Combine(_dir, "staged"); } }
        internal string DownloadDir { get { return Path.Combine(_dir, "download"); } }
        private string RollbackDir { get { return Path.Combine(_dir, "rollback"); } }
        private string PreviousDir { get { return Path.Combine(_dir, "previous"); } }
        private string PlanPath { get { return Path.Combine(_dir, "plan.txt"); } }
        private string StatePath { get { return Path.Combine(_dir, "state.txt"); } }
        private string NoticePath { get { return Path.Combine(_dir, "notice.txt"); } }
        private string StartupCheckPath { get { return Path.Combine(_dir, "check-at-startup.txt"); } }

        private string Full(string relative)
        {
            return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string Under(string dir, string relative)
        {
            return Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        // --- reading, for the title screen and the menu
        internal string Phase
        {
            get
            {
                try { return File.Exists(StatePath) ? File.ReadAllText(StatePath).Trim() : ""; }
                catch (IOException) { return ""; }
                catch (UnauthorizedAccessException) { return ""; }
            }
        }

        internal Plan PendingPlan
        {
            get { return ReadPlan(PlanPath); }
        }

        // the way back on offer: what the last finished update changed
        internal Plan PreviousPlan
        {
            get { return ReadPlan(Path.Combine(PreviousDir, "undo.txt")); }
        }

        // Off unless the file says exactly "on", so a game that never chose, or a damaged file,
        // contacts nothing. Turning it off leaves no trace. Shared by the mods as the rest of the
        // update is: the state is the folder.
        internal bool CheckAtStartup
        {
            get
            {
                try { return File.Exists(StartupCheckPath) && File.ReadAllText(StartupCheckPath).Trim() == "on"; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
            set
            {
                if (value)
                {
                    WriteAtomic(StartupCheckPath, "on");
                }
                else
                {
                    Remove(StartupCheckPath);
                    DropIfEmpty();
                }
            }
        }

        internal string TakeNotice()
        {
            try
            {
                if (!File.Exists(NoticePath))
                {
                    return null;
                }
                string text = File.ReadAllText(NoticePath);
                Remove(NoticePath);
                DropIfEmpty();
                return text;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // --- what the mods do between starts
        internal void WritePlan(Plan plan)
        {
            WriteAtomic(PlanPath, Render(plan));
        }

        internal void MarkStaged()
        {
            WriteAtomic(StatePath, Staged);
        }

        internal void MarkUndo()
        {
            WriteAtomic(StatePath, Undo);
        }

        // cancels a staged update, or a chosen way back; the mod's files were never touched
        internal void CancelPending()
        {
            string phase = Phase;
            if (phase == Staged)
            {
                Clear();
            }
            else if (phase == Undo)
            {
                Remove(StatePath);
            }
        }

        // what a stopped download left: never the mod's files, never a phase in progress
        internal void Discard()
        {
            Clear();
        }

        // the new version reached the title screen: its rollback becomes the player's way back
        internal void Confirm()
        {
            if (Directory.Exists(RollbackDir))
            {
                DeleteTree(PreviousDir);
                Directory.Move(RollbackDir, PreviousDir);
            }
            Remove(PlanPath);
            Remove(StatePath);
            DeleteTree(StagedDir);
        }

        // --- the start of the game. Never throws: whatever happens, the game goes on starting.
        internal void AtStart(Action<string> log)
        {
            string phase = Phase;
            try
            {
                switch (phase)
                {
                    case Staged: Apply(log); break;
                    case Swapping: Recover(log, "interrupted"); break;
                    case Applied: Recover(log, "not-loaded"); break;
                    case Undo: GoBack(log); break;
                }
            }
            catch (Exception e)
            {
                log("update: " + phase + " failed: " + e.Message);
                try
                {
                    Notify("failed", e.Message);
                    // a swap that could not be put back stays on record: the next start tries again
                    if (File.Exists(Path.Combine(RollbackDir, "undo.txt")))
                    {
                        WriteAtomic(StatePath, Swapping);
                    }
                    else
                    {
                        Remove(StatePath);
                    }
                }
                catch (Exception)
                {
                    // the disk refuses even this; the log has it
                }
            }
        }

        private void Apply(Action<string> log)
        {
            Plan plan = ReadPlan(PlanPath);
            if (plan == null)
            {
                Abandon(log, "plan-unreadable", "");
                return;
            }
            // first, nothing but looking: every path, every staged file, every file it replaces
            foreach (FileChange file in plan.Files)
            {
                if (!Allowed(file.Path))
                {
                    Abandon(log, "plan-unsafe", file.Path);
                    return;
                }
                if (file.Now.Length > 0 && Hash(Under(StagedDir, file.Path)) != file.Now)
                {
                    Abandon(log, "staged-damaged", file.Path);
                    return;
                }
                if (Hash(Full(file.Path)) != file.Was)
                {
                    Abandon(log, "changed", file.Path);
                    return;
                }
            }

            WriteAtomic(StatePath, Swapping);
            try
            {
                DeleteTree(RollbackDir);
                foreach (FileChange file in plan.Files)
                {
                    if (file.Was.Length > 0)
                    {
                        CopyInto(Full(file.Path), Under(RollbackDir, file.Path));
                    }
                }
                Directory.CreateDirectory(RollbackDir);
                // from here the folder may differ: undo.txt is what Recover needs to put it back
                WriteAtomic(Path.Combine(RollbackDir, "undo.txt"), Render(plan));
                foreach (FileChange file in plan.Files)
                {
                    if (file.Now.Length > 0)
                    {
                        CopyInto(Under(StagedDir, file.Path), Full(file.Path));
                    }
                    else
                    {
                        Remove(Full(file.Path));
                    }
                }
                foreach (FileChange file in plan.Files)
                {
                    if (Hash(Full(file.Path)) != file.Now)
                    {
                        throw new IOException("the copy of " + file.Path + " does not match");
                    }
                }
            }
            catch (Exception e)
            {
                log("update: the swap failed, putting back: " + e.Message);
                Recover(log, "swap-failed");
                return;
            }
            WriteAtomic(StatePath, Applied);
            DeleteTree(StagedDir);
            log("update: swapped in " + Describe(plan));
        }

        // Puts back what a swap changed. rollback/ when it holds one; after a Confirm that died
        // between its two moves, previous/ holds the same undo.txt. No undo.txt at all means
        // the swap never reached the mod's files, and there is nothing to put back.
        private void Recover(Action<string> log, string why)
        {
            string from = File.Exists(Path.Combine(RollbackDir, "undo.txt")) ? RollbackDir : null;
            if (from == null && why == "not-loaded" && File.Exists(Path.Combine(PreviousDir, "undo.txt")))
            {
                from = PreviousDir;
            }
            Plan undo = from == null ? null : ReadPlan(Path.Combine(from, "undo.txt"));
            if (undo != null)
            {
                Restore(undo, from);
                DeleteTree(from);
            }
            log("update: put the previous version back (" + why + ")");
            Abandon(log, why, undo != null ? Describe(undo) : "");
        }

        private void GoBack(Action<string> log)
        {
            Plan undo = ReadPlan(Path.Combine(PreviousDir, "undo.txt"));
            if (undo == null)
            {
                Remove(StatePath);
                Notify("no-previous", "");
                return;
            }
            foreach (FileChange file in undo.Files)
            {
                if (!Allowed(file.Path) || Hash(Full(file.Path)) != file.Now)
                {
                    // installed over by hand since: previous/ no longer describes this folder
                    Remove(StatePath);
                    Notify("changed", file.Path);
                    return;
                }
            }
            Restore(undo, PreviousDir);
            DeleteTree(PreviousDir);
            Remove(StatePath);
            Notify("went-back", Describe(undo));
            log("update: went back from " + Describe(undo));
        }

        private void Restore(Plan undo, string from)
        {
            foreach (FileChange file in undo.Files)
            {
                if (!Allowed(file.Path))
                {
                    throw new IOException("unsafe path in the undo list: " + file.Path);
                }
                if (Hash(Full(file.Path)) == file.Was)
                {
                    continue;       // never changed: a swap that stopped short of this file
                }
                if (file.Was.Length > 0)
                {
                    CopyInto(Under(from, file.Path), Full(file.Path));
                }
                else
                {
                    Remove(Full(file.Path));
                    Prune(Path.GetDirectoryName(Full(file.Path)));
                }
            }
            foreach (FileChange file in undo.Files)
            {
                if (Hash(Full(file.Path)) != file.Was)
                {
                    throw new IOException("the restored " + file.Path + " does not match");
                }
            }
        }

        // a folder the undone update created goes with its last file; plugins itself never does
        private void Prune(string dir)
        {
            string stop = Full("BepInEx/plugins");
            while (dir != null && dir.Length > stop.Length && dir.StartsWith(stop, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }

        private void Abandon(Action<string> log, string why, string detail)
        {
            log("update: nothing swapped (" + why + ") " + detail);
            Notify(why, detail);
            Clear();
        }

        private void Clear()
        {
            Remove(StatePath);
            Remove(PlanPath);
            DeleteTree(StagedDir);
            DeleteTree(DownloadDir);
            DropIfEmpty();
        }

        // nothing under way and nothing to go back to: no trace of this folder
        private void DropIfEmpty()
        {
            if (Directory.Exists(_dir) && Directory.GetFileSystemEntries(_dir).Length == 0)
            {
                Directory.Delete(_dir);
            }
        }

        // File.Delete shrugs at a missing file and throws at a missing folder
        private static void Remove(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private void Notify(string kind, string detail)
        {
            WriteAtomic(NoticePath, kind + "\n" + detail);
        }

        private static bool Allowed(string path)
        {
            return UpdateRules.InRoots(path, UpdateRules.AllRoots()) || UpdateRules.IsStrayDll(path);
        }

        private static string Describe(Plan plan)
        {
            var parts = new List<string>();
            foreach (ModChange mod in plan.Mods)
            {
                parts.Add(mod.Name + " " + mod.From + " to " + mod.To);
            }
            return string.Join(", ", parts.ToArray());
        }

        // --- the files
        internal static string Render(Plan plan)
        {
            var text = new StringBuilder();
            text.Append("format=").Append(Format).Append('\n');
            foreach (ModChange mod in plan.Mods)
            {
                text.Append("mod=").Append(mod.Name).Append('|').Append(mod.From).Append('|').Append(mod.To).Append('\n');
            }
            foreach (FileChange file in plan.Files)
            {
                text.Append("file=").Append(file.Path).Append('|').Append(file.Now).Append('|').Append(file.Was).Append('\n');
            }
            return text.ToString();
        }

        internal static Plan ParsePlan(string text)
        {
            var plan = new Plan();
            bool known = false;
            foreach (string line in text.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }
                string key = line.Substring(0, eq);
                string[] parts = line.Substring(eq + 1).Trim().Split('|');
                if (key == "format")
                {
                    known = parts[0] == Format.ToString();
                }
                else if (key == "mod" && parts.Length == 3)
                {
                    plan.Mods.Add(new ModChange { Name = parts[0], From = parts[1], To = parts[2] });
                }
                else if (key == "file" && parts.Length == 3)
                {
                    plan.Files.Add(new FileChange { Path = parts[0], Now = parts[1], Was = parts[2] });
                }
            }
            return known ? plan : null;
        }

        private static Plan ReadPlan(string path)
        {
            try { return File.Exists(path) ? ParsePlan(File.ReadAllText(path)) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        // "" for no file: the same word a plan uses for "not there"
        internal static string Hash(string path)
        {
            if (!File.Exists(path))
            {
                return "";
            }
            using (SHA256 sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                return Hex(sha.ComputeHash(stream));
            }
        }

        internal static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                text.Append(b.ToString("x2"));
            }
            return text.ToString();
        }

        private static void CopyInto(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }

        internal static void DeleteTree(string dir)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        // whole or not at all: a reader never sees half a state, a crash never leaves one
        private static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            Remove(path);
            File.Move(temp, path);
        }
    }
}
