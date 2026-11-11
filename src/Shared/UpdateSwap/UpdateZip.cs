using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Shared
{
    // Unpacks a release zip into the staging folder. Every entry is judged before the first
    // byte is written, so a zip with one path out of the mod's folders writes nothing, and the
    // bytes written are counted rather than trusted from the zip's own header.
    internal static class UpdateZip
    {
        internal sealed class Refused : Exception
        {
            internal Refused(string entry) : base(entry) { }
        }

        internal sealed class Unpacked
        {
            internal string Path;
            internal string Hash;
        }

        internal static List<Unpacked> Unpack(string zipPath, UpdateRules.Mod mod, string stagedDir)
        {
            var files = new List<Unpacked>();
            using (ZipArchive zip = ZipFile(zipPath))
            {
                if (zip.Entries.Count > UpdateRules.MaxEntries)
                {
                    throw new Refused(zip.Entries.Count + " entries");
                }
                long declared = 0;
                bool hasDll = false;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (UpdateRules.Classify(entry.FullName, mod.Roots) == UpdateRules.Entry.Refused)
                    {
                        throw new Refused(entry.FullName);
                    }
                    declared += entry.Length;
                    hasDll |= entry.FullName == UpdateRules.CanonicalDll(mod);
                }
                if (!hasDll)
                {
                    throw new Refused("no " + UpdateRules.CanonicalDll(mod));
                }
                if (declared > UpdateRules.MaxUnpacked)
                {
                    throw new Refused(declared + " bytes unpacked");
                }

                long budget = UpdateRules.MaxUnpacked;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (UpdateRules.Classify(entry.FullName, mod.Roots) != UpdateRules.Entry.File)
                    {
                        continue;
                    }
                    string target = Path.Combine(stagedDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (SHA256 sha = SHA256.Create())
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            budget -= read;
                            if (budget < 0)
                            {
                                throw new Refused("more bytes than it declared");
                            }
                            sha.TransformBlock(buffer, 0, read, null, 0);
                            output.Write(buffer, 0, read);
                        }
                        sha.TransformFinalBlock(buffer, 0, 0);
                        files.Add(new Unpacked { Path = entry.FullName, Hash = UpdateDisk.Hex(sha.Hash) });
                    }
                }
            }
            return files;
        }

        private static ZipArchive ZipFile(string path)
        {
            return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read), ZipArchiveMode.Read);
        }
    }
}
