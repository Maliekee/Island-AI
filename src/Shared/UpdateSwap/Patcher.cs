using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace Shared
{
    // The step that runs before the chainloader, while no mod DLL is loaded and so none is
    // locked. BepInEx calls Initialize once per start; this patcher patches nothing, the
    // preloader is only where it can stand. An Island mod writes it into BepInEx/patchers when
    // the player first takes an update. Without a plan on disk it reads one small file and
    // is done. It opens no connection, and neither does anything it loads.
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get { yield break; } }

        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("IslandUpdate");
            new UpdateDisk(Paths.GameRootPath).AtStart(log.LogInfo);
            Logger.Sources.Remove(log);
        }

        public static void Patch(AssemblyDefinition assembly)
        {
        }
    }
}
