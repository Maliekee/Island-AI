using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Shared
{
    // The steps of an update, each started by a button and ending where it says: Check ends at
    // what it found, and only Download goes on, to staged files and a plan. It runs on a thread
    // of its own and touches no Unity object; the window reads Step and Done every frame.
    // Check has one other caller, ModUpdate's check at startup, which reads the answer and never
    // calls Download.
    //
    // WHAT IS CHECKED, before anything is staged: the release names a version; the asset's
    // address is github.com, this repository, this tag, that file; GitHub lists a sha256 for
    // the file, which the bytes must match; the zip stays inside the mod's folders; the DLL
    // inside says the version the release promised.
    internal sealed class UpdateFlow
    {
        internal enum Step { Checking, UpToDate, Offer, Downloading, Unpacking, Ready, Failed, Closed }

        // the few words of GitHub's answer the update reads. Newtonsoft, which the game ships:
        // JsonUtility drops an array of a type from an assembly Unity did not load itself.
        private sealed class ReleaseJson
        {
            internal string tag_name;
            internal string body;
            internal bool draft;
            internal bool prerelease;
            internal List<AssetJson> assets = new List<AssetJson>();
        }

        private sealed class AssetJson
        {
            internal string name;
            internal long size;
            internal string digest;
            internal string browser_download_url;
        }

        private static ReleaseJson ParseRelease(string text)
        {
            JObject root;
            try
            {
                root = JObject.Parse(text);
            }
            catch (JsonException e)
            {
                throw new UpdateFailure("release-bad", e.Message);
            }
            var release = new ReleaseJson
            {
                tag_name = Value<string>(root, "tag_name"),
                body = Value<string>(root, "body"),
                draft = Value<bool>(root, "draft"),
                prerelease = Value<bool>(root, "prerelease"),
            };
            var assets = root["assets"] as JArray;
            if (assets != null)
            {
                foreach (JToken token in assets)
                {
                    var asset = token as JObject;
                    if (asset != null)
                    {
                        release.assets.Add(new AssetJson
                        {
                            name = Value<string>(asset, "name"),
                            size = Value<long>(asset, "size"),
                            digest = Value<string>(asset, "digest"),
                            browser_download_url = Value<string>(asset, "browser_download_url"),
                        });
                    }
                }
            }
            return release;
        }

        // the value when it is one of that type, or nothing: a field of the wrong shape reads
        // as missing, and the checks after this refuse a release with one missing
        private static T Value<T>(JObject source, string key)
        {
            var value = source[key] as JValue;
            return value != null && value.Value is T ? (T)value.Value : default(T);
        }

        internal sealed class Part
        {
            internal UpdateRules.Mod Mod;
            internal string Installed;
            internal string Version;
            internal string Asset;
            internal string Url;
            internal long Size;
            internal string Sha;
            internal List<string> Strays;       // copies of the DLL outside its proper folder
        }

        private readonly UpdateDisk _disk;
        private readonly Assembly _assembly;
        private readonly string _name;
        private readonly string _version;
        private readonly UpdateNet _net = new UpdateNet();

        private volatile Step _step = Step.Closed;
        private volatile bool _downloading;
        private long _done;

        internal UpdateFlow(UpdateDisk disk, Assembly assembly, string name, string version)
        {
            _disk = disk;
            _assembly = assembly;
            _name = name;
            _version = version;
#if DEBUG
            _armed = HarnessUp();
#endif
        }

        internal Step Current { get { return _step; } }
        internal long Done { get { return Interlocked.Read(ref _done); } }
        internal long Total { get; private set; }
        internal string Tag { get; private set; }
        internal string Body { get; private set; }
        internal List<Part> Parts { get; private set; }
        internal string FailKind { get; private set; }
        internal string FailDetail { get; private set; }

        internal void Check()
        {
            Run(Step.Checking, Look);
        }

        internal void Download()
        {
            _downloading = true;
            Run(Step.Downloading, Fetch);
        }

        internal void Cancel()
        {
            bool running = _step == Step.Checking || _step == Step.Downloading || _step == Step.Unpacking;
            _step = Step.Closed;
            if (running)
            {
                _net.Cancel();
            }
        }

        private void Run(Step first, Action work)
        {
            _step = first;
            var thread = new Thread(() =>
            {
                try
                {
                    work();
                }
                catch (Exception e)
                {
                    Fail(e);
                }
            });
            thread.IsBackground = true;
            thread.Name = "IslandUpdate";
            thread.Start();
        }

        private void Advance(Step step)
        {
            if (_step != Step.Closed)
            {
                _step = step;
            }
        }

        private void Fail(Exception e)
        {
            // only a download has anything to clear. A check's failure must not: the check at
            // startup can fail while the player's own download is running.
            if (_downloading)
            {
                Discard();
            }
            var failure = e as UpdateFailure;
            if (failure != null && failure.Kind == "cancelled" || _step == Step.Closed)
            {
                _step = Step.Closed;
                return;
            }
            FailKind = failure != null ? failure.Kind
                : e is UpdateZip.Refused ? "zip-unsafe"
                : e is InvalidDataException ? "zip-bad"
                : e is UnauthorizedAccessException ? "denied"
                : IsDiskFull(e) ? "disk"
                : "unknown";
            FailDetail = e.Message;
            Debug.LogWarning("Island update: " + FailKind + " " + e.Message);
            _step = Step.Failed;
        }

        private void Discard()
        {
            try
            {
                if (_disk.Phase.Length == 0)
                {
                    _disk.Discard();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Island update: leftovers stay: " + e.Message);
            }
        }

        private static bool IsDiskFull(Exception e)
        {
            int code = e.HResult & 0xFFFF;
            return e is IOException && (code == 112 || code == 39);    // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL
        }

        // --- Check: one request for the latest release, and what it means for this game
        private void Look()
        {
            var json = new MemoryStream();
            _net.Get(LatestAddress(), ApiAllowed, json, UpdateRules.MaxJson, null);
            ReleaseJson release = ParseRelease(Encoding.UTF8.GetString(json.ToArray()));
            if (release.draft || release.prerelease || UpdateRules.ParseVersion(release.tag_name) == null)
            {
                throw new UpdateFailure("release-bad", release.tag_name ?? "");
            }

            var parts = new List<Part>();
            foreach (UpdateRules.Mod mod in UpdateRules.Mods)
            {
                List<string> copies = Copies(mod);
                string installed = mod.Name == _name ? RunningVersion() : DiskVersion(copies);
                if (installed == null)
                {
                    continue;   // not in this game: an update never adds a mod
                }
                AssetJson asset = FindAsset(release, mod, installed);
                if (asset == null)
                {
                    continue;
                }
                parts.Add(MakePart(release, mod, asset, installed, copies));
            }
            if (parts.Count == 0)
            {
                Advance(Step.UpToDate);
                return;
            }
            Tag = release.tag_name;
            Body = release.body;
            Parts = parts;
            long total = 0;
            foreach (Part part in parts)
            {
                total += part.Size;
            }
            Total = total;
            Advance(Step.Offer);
        }

        // the mod's own asset in this release, when it is newer than what is installed. Island
        // UI's version is the tag; Island Ex has its own, in its file name.
        private static AssetJson FindAsset(ReleaseJson release, UpdateRules.Mod mod, string installed)
        {
            foreach (AssetJson asset in release.assets)
            {
                string name = asset.name ?? "";
                if (!name.StartsWith(mod.Asset, StringComparison.Ordinal) || !name.EndsWith(".zip", StringComparison.Ordinal))
                {
                    continue;
                }
                string version = name.Substring(mod.Asset.Length, name.Length - mod.Asset.Length - 4);
                if (UpdateRules.ParseVersion(version) != null && UpdateRules.IsNewer(version, installed))
                {
                    return asset;
                }
            }
            return null;
        }

        private Part MakePart(ReleaseJson release, UpdateRules.Mod mod, AssetJson asset, string installed, List<string> copies)
        {
            string url = AssetAddress(release.tag_name, asset);
            string sha = UpdateRules.ParseDigest(asset.digest);
            if (asset.browser_download_url != url)
            {
                throw new UpdateFailure("host", asset.browser_download_url);
            }
            if (sha == null)
            {
                throw new UpdateFailure("no-digest", asset.name);
            }
            if (asset.size <= 0 || asset.size > UpdateRules.MaxAsset)
            {
                throw new UpdateFailure("too-big", asset.size.ToString());
            }
            var strays = new List<string>(copies);
            strays.Remove(UpdateRules.CanonicalDll(mod));
            return new Part
            {
                Mod = mod,
                Installed = installed,
                Version = asset.name.Substring(mod.Asset.Length, asset.name.Length - mod.Asset.Length - 4),
                Asset = asset.name,
                Url = url,
                Size = asset.size,
                Sha = sha,
                Strays = strays,
            };
        }

        // every copy of the mod's DLL under plugins, as a path from the game folder
        private List<string> Copies(UpdateRules.Mod mod)
        {
            var copies = new List<string>();
            string plugins = Path.Combine(_disk.Root, "BepInEx", "plugins");
            foreach (string path in Directory.GetFiles(plugins, mod.Dll, SearchOption.AllDirectories))
            {
                copies.Add(path.Substring(_disk.Root.TrimEnd('\\', '/').Length + 1).Replace('\\', '/'));
            }
            return copies;
        }

        private string DiskVersion(List<string> copies)
        {
            foreach (string copy in copies)
            {
                string version = FileVersion(Path.Combine(_disk.Root, copy.Replace('/', '\\')));
                if (version != null)
                {
                    return version;
                }
            }
            return null;
        }

        private static string FileVersion(string path)
        {
            try { return AssemblyName.GetAssemblyName(path).Version.ToString(3); }
            catch (Exception) { return null; }
        }

        // --- Update: fetch, verify, unpack, and leave a plan for the next start
        private void Fetch()
        {
            _disk.Discard();
            Directory.CreateDirectory(_disk.DownloadDir);
            RequireSpace();
            Interlocked.Exchange(ref _done, 0);

            var plan = new UpdateDisk.Plan();
            foreach (Part part in Parts)
            {
                string zip = Path.Combine(_disk.DownloadDir, part.Asset + ".part");
                using (var file = new FileStream(zip, FileMode.Create, FileAccess.Write))
                {
                    _net.Get(part.Url, AssetAllowed, file, part.Size, n => Interlocked.Add(ref _done, n));
                }
                // the file is the one the release lists, or it goes no further
                if (new FileInfo(zip).Length != part.Size)
                {
                    throw new UpdateFailure("interrupted", part.Asset);
                }
                if (UpdateDisk.Hash(zip) != part.Sha)
                {
                    throw new UpdateFailure("digest", part.Asset);
                }

                Advance(Step.Unpacking);
                List<UpdateZip.Unpacked> files = UpdateZip.Unpack(zip, part.Mod, _disk.StagedDir);
                string dll = Path.Combine(_disk.StagedDir, UpdateRules.CanonicalDll(part.Mod).Replace('/', '\\'));
                if (FileVersion(dll) != part.Version)
                {
                    throw new UpdateFailure("wrong-version", FileVersion(dll) ?? "");
                }
                plan.Mods.Add(new UpdateDisk.ModChange { Name = part.Mod.Name, From = part.Installed, To = part.Version });
                foreach (UpdateZip.Unpacked file in files)
                {
                    string now = UpdateDisk.Hash(Path.Combine(_disk.Root, file.Path.Replace('/', '\\')));
                    if (now != file.Hash)
                    {
                        plan.Files.Add(new UpdateDisk.FileChange { Path = file.Path, Now = file.Hash, Was = now });
                    }
                }
                foreach (string stray in part.Strays)
                {
                    plan.Files.Add(new UpdateDisk.FileChange
                    {
                        Path = stray,
                        Now = "",
                        Was = UpdateDisk.Hash(Path.Combine(_disk.Root, stray.Replace('/', '\\'))),
                    });
                }
                Advance(Step.Downloading);
            }

            if (_step == Step.Closed)
            {
                throw new UpdateFailure("cancelled");
            }
            WritePatcher();
            _disk.WritePlan(plan);
            UpdateDisk.DeleteTree(_disk.DownloadDir);
            _disk.MarkStaged();     // last: until here, the next start finds nothing to do
            Advance(Step.Ready);
        }

        private void RequireSpace()
        {
            // the zip, its contents, and the old files kept for the way back
            long need = Total * 4;
            long free;
            try { free = new DriveInfo(Path.GetPathRoot(_disk.Dir)).AvailableFreeSpace; }
            catch (Exception) { return; }   // unknown: the writes will say
            if (free < need)
            {
                throw new UpdateFailure("disk", UpdateRules.Size(need));
            }
        }

        // the preloader step travels inside the mod and is put in place the first time it is
        // wanted, so a game that never updates never has it
        private void WritePatcher()
        {
            string target = Path.Combine(_disk.Root, "BepInEx", "patchers", "IslandUpdate", "IslandUpdateSwap1.dll");
            using (Stream resource = _assembly.GetManifestResourceStream("IslandUpdateSwap1.dll"))
            {
                if (resource == null)
                {
                    throw new UpdateFailure("unknown", "the update step is missing from this build");
                }
                var bytes = new byte[resource.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    read += resource.Read(bytes, read, bytes.Length - read);
                }
                // already there from an earlier update, it was loaded at this start and is locked
                if (File.Exists(target) && File.ReadAllBytes(target).Length == bytes.Length)
                {
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, bytes);
            }
        }

        // --- where requests go: the official release, in every build a player can have. A
        // DEBUG build in an armed harness session can be pointed at a local fixture; a Release
        // build, which is every build that ships, has none of this code (the publish gate
        // refuses a DLL with the variable's name in it).
#if DEBUG
        internal static string TestSource { get { return Armed("ISLAND_UPDATE_TEST_SOURCE"); } }

        private static bool _armed;

        // the harness unloads itself unless it was armed for this launch, so it being up is the
        // proof. Asked on the main thread: a Unity object cannot be judged from the worker.
        private static bool HarnessUp()
        {
            foreach (BepInEx.PluginInfo info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                if (info.Metadata.GUID.EndsWith(".islandtest", StringComparison.Ordinal) && info.Instance != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static string Armed(string variable)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            return _armed && !string.IsNullOrEmpty(value) ? value : null;
        }

        private static bool LocalTest(Uri uri)
        {
            return TestSource != null && uri.Host == new Uri(TestSource).Host;
        }

        private string RunningVersion()
        {
            return Armed("ISLAND_UPDATE_TEST_PRETEND") ?? _version;
        }

        private static string LatestAddress()
        {
            return TestSource != null ? TestSource + "/repos/" + UpdateRules.Repo + "/releases/latest" : UpdateRules.LatestApi;
        }

        private static string AssetAddress(string tag, AssetJson asset)
        {
            return TestSource != null ? TestSource + "/download/" + tag + "/" + asset.name : UpdateRules.AssetUrl(tag, asset.name);
        }

        private static bool ApiAllowed(Uri uri)
        {
            return LocalTest(uri) || UpdateRules.ApiAllowed(uri);
        }

        private static bool AssetAllowed(Uri uri)
        {
            return LocalTest(uri) || UpdateRules.AssetAllowed(uri);
        }
#else
        private string RunningVersion()
        {
            return _version;
        }

        private static string LatestAddress()
        {
            return UpdateRules.LatestApi;
        }

        private static string AssetAddress(string tag, AssetJson asset)
        {
            return UpdateRules.AssetUrl(tag, asset.name);
        }

        private static bool ApiAllowed(Uri uri)
        {
            return UpdateRules.ApiAllowed(uri);
        }

        private static bool AssetAllowed(Uri uri)
        {
            return UpdateRules.AssetAllowed(uri);
        }
#endif
    }
}
