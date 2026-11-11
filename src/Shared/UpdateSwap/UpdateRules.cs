using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Shared
{
    // WHAT THE IN-GAME UPDATE MAY DO, as pure functions: no file, no network, no Unity, so a
    // plain console test can hold every rule to account (tests/update). The rest of the
    // update asks here and has no opinions of its own. See docs/in-game-update.md.
    //
    // The official source is one repository and it is written here, once. Nothing a player
    // or a settings file can reach changes it.
    internal static class UpdateRules
    {
        internal const string Repo = "Maliekee/Island-UI";
        internal const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        internal const string UserAgent = "Island-UI-update";

        internal const long MaxJson = 2L * 1024 * 1024;
        internal const long MaxAsset = 96L * 1024 * 1024;
        internal const long MaxUnpacked = 256L * 1024 * 1024;
        internal const int MaxEntries = 4000;

        // the mods this repository releases. A mod is only ever updated where it is already
        // installed: an update never adds Island Ex to a game that chose not to have it.
        internal sealed class Mod
        {
            internal string Name;       // as a player reads it, and the key in a plan
            internal string Guid;       // the BepInPlugin id
            internal string Dll;
            internal string Asset;      // the release zip's name up to the version
            internal string[] Roots;    // the only folders its zip may write to
        }

        internal static readonly Mod[] Mods =
        {
            new Mod
            {
                Name = "Island UI", Guid = "madisland.islandui", Dll = "IslandUI.dll", Asset = "Island-UI-v",
                Roots = new[] { "BepInEx/plugins/IslandUI/", "BepInEx/plugins/islandui-art/" },
            },
            new Mod
            {
                Name = "Island Ex", Guid = "madisland.islandex", Dll = "IslandEx.dll", Asset = "Island-Ex-v",
                Roots = new[] { "BepInEx/plugins/IslandEx/" },
            },
        };

        internal static string CanonicalDll(Mod mod)
        {
            return mod.Roots[0] + mod.Dll;
        }

        // --- versions: three numbers, optionally behind a v. Anything else is no version, and
        // a release without one is refused rather than guessed at.
        internal static int[] ParseVersion(string text)
        {
            Match m = Regex.Match(text ?? "", @"^v?(\d{1,6})\.(\d{1,6})\.(\d{1,6})$");
            if (!m.Success)
            {
                return null;
            }
            return new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value) };
        }

        internal static bool IsNewer(string candidate, string installed)
        {
            int[] a = ParseVersion(candidate), b = ParseVersion(installed);
            if (a == null || b == null)
            {
                return false;
            }
            for (int i = 0; i < 3; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] > b[i];
                }
            }
            return false;
        }

        // --- where a request may go. HTTPS on the default port, no credentials in the URL, and
        // these hosts exactly: a suffix match would let any *.githubusercontent.com in, and
        // raw.githubusercontent.com serves anyone's files. Should GitHub move its asset host the
        // download fails plainly and the player falls back on the release page.
        private static readonly string[] AssetHosts =
        {
            "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
        };

        private static bool Secure(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
                && uri.IsDefaultPort && uri.UserInfo.Length == 0;
        }

        internal static bool ApiAllowed(Uri uri)
        {
            return Secure(uri) && string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool AssetAllowed(Uri uri)
        {
            if (!Secure(uri))
            {
                return false;
            }
            foreach (string host in AssetHosts)
            {
                if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // the address a release lists for its own file, to the letter: github.com, this
        // repository, this tag, this file name
        internal static string AssetUrl(string tag, string name)
        {
            return "https://github.com/" + Repo + "/releases/download/" + tag + "/" + name;
        }

        // GitHub's own record of the file, "sha256:<64 hex>". Null when there is none.
        internal static string ParseDigest(string digest)
        {
            Match m = Regex.Match(digest ?? "", "^sha256:([0-9a-fA-F]{64})$");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
        }

        // --- what a zip may contain. A release zip is refused whole for one bad entry: an entry
        // that points out of the mod's folders is not a mistake to work around.
        internal enum Entry { Refused, Folder, File }

        private static readonly Regex Device = new Regex(@"^(con|prn|aux|nul|com\d|lpt\d)(\..*)?$", RegexOptions.IgnoreCase);

        // one path as it may be written: forward slashes, relative, each segment an ordinary
        // name. Windows drops trailing dots and spaces and reads a colon as a drive or a
        // stream, so all of those could land somewhere other than they say.
        internal static bool PlainPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOfAny(new[] { '\\', ':', '|', '*', '?', '"', '<', '>' }) >= 0)
            {
                return false;
            }
            foreach (char c in path)
            {
                if (c < 0x20)
                {
                    return false;
                }
            }
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.EndsWith(".", StringComparison.Ordinal)
                    || segment.EndsWith(" ", StringComparison.Ordinal) || Device.IsMatch(segment))
                {
                    return false;
                }
            }
            return true;
        }

        // the file is inside one of the roots, with a name of its own
        internal static bool InRoots(string path, IEnumerable<string> roots)
        {
            if (!PlainPath(path))
            {
                return false;
            }
            foreach (string root in roots)
            {
                if (path.Length > root.Length && path.StartsWith(root, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // every root of every mod: what a plan on disk may name, whoever wrote it
        internal static IEnumerable<string> AllRoots()
        {
            foreach (Mod mod in Mods)
            {
                foreach (string root in mod.Roots)
                {
                    yield return root;
                }
            }
        }

        // a duplicate of the mod's own DLL somewhere else under plugins (a hand install):
        // the one other kind of path an update touches, moved aside so it cannot load twice
        internal static bool IsStrayDll(string path)
        {
            if (!PlainPath(path) || !path.StartsWith("BepInEx/plugins/", StringComparison.Ordinal))
            {
                return false;
            }
            string name = path.Substring(path.LastIndexOf('/') + 1);
            foreach (Mod mod in Mods)
            {
                if (name == mod.Dll)
                {
                    return true;
                }
            }
            return false;
        }

        internal static Entry Classify(string name, string[] roots)
        {
            bool folder = name.EndsWith("/", StringComparison.Ordinal);
            string path = folder ? name.Substring(0, name.Length - 1) : name;
            if (!PlainPath(path))
            {
                return Entry.Refused;
            }
            if (folder)
            {
                foreach (string root in roots)
                {
                    // on the way to a root (BepInEx/, BepInEx/plugins/), the root, or under it
                    if ((root).StartsWith(path + "/", StringComparison.Ordinal)
                        || (path + "/").StartsWith(root, StringComparison.Ordinal))
                    {
                        return Entry.Folder;
                    }
                }
                return Entry.Refused;
            }
            return InRoots(path, roots) ? Entry.File : Entry.Refused;
        }

        // --- the release's notes, from the Markdown a release body is, into plain blocks. Text
        // from the network reaches the screen as text: rich text is off wherever it is shown.
        internal struct Block
        {
            internal bool Heading;
            internal string Text;
        }

        internal static List<Block> Notes(string body, int maxChars)
        {
            var blocks = new List<Block>();
            int used = 0;
            foreach (string raw in (body ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                bool heading = line.StartsWith("#", StringComparison.Ordinal);
                line = heading ? line.TrimStart('#').Trim()
                    : line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)
                        ? "• " + line.Substring(2) : line;
                line = Regex.Replace(line, @"\[([^\]]*)\]\([^)]*\)", "$1").Replace("**", "").Replace("`", "");
                if (used + line.Length > maxChars)
                {
                    blocks.Add(new Block { Text = "…" });
                    break;
                }
                used += line.Length;
                blocks.Add(new Block { Heading = heading, Text = line });
            }
            return blocks;
        }

        internal static string Size(long bytes)
        {
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }
    }
}
