// Copyright (c) 2026 Maliekee
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

// Shared SOURCE, not a shared assembly: linked into every plugin csproj
// (<Compile Include="..\Shared\PatchCensus.cs">), so each mod carries its own
// private copy and none depends on another at runtime. It replaced the IslandEvents
// core mod on 2026-09-01, whose only shared piece with a second caller was this.
namespace Shared
{
    /// <summary>
    /// Applies a plugin's Harmony patches one class at a time and reports what bound.
    ///
    /// WHY THIS EXISTS, and why it replaces <c>harmony.PatchAll()</c>:
    ///
    /// 1. **PatchAll is all-or-nothing.** It walks the assembly's types and calls Patch() on
    ///    each; the first one that throws aborts the walk, so a single stale target silently
    ///    takes every LATER patch class down with it. The symptom is "half the mod stopped
    ///    working" with one stack trace that explains only the first casualty. Patching per
    ///    class contains the blast radius: one dead target costs one feature.
    ///
    /// 2. **PatchAll skips a class with no CLASS-LEVEL Harmony attribute — silently.** No
    ///    exception, no log. A class whose methods carry [HarmonyPostfix] but whose type
    ///    carries no [HarmonyPatch] is simply never visited, and the symptom is
    ///    indistinguishable from the feature never having been written. (VanillaContainerUI
    ///    and FavouriteRules both carry a bare [HarmonyPatch] for exactly this reason, with a
    ///    comment calling it load-bearing.) That case is detected here explicitly, because
    ///    Harmony itself cannot report it — to Harmony the class is not a patch class at all.
    ///
    /// 3. **A game update is the moment both failures arrive at once.** The game ships no
    ///    version of its own and its methods are bound by name and signature, so a renamed or
    ///    re-signatured method fails at PatchAll time with nothing tying it back to the update.
    ///    The census turns that into a boot-time list: run the game once after an update and
    ///    the log says which patches no longer bind. That list IS the triage.
    ///    See README "Tracking the game version" and tools/sync-decompiled.ps1, which reports
    ///    the same question from the other side (which decompiled files changed).
    /// </summary>
    internal static class PatchCensus
    {
        /// <summary>What a census run found. Returned so a caller can refuse to run degraded.</summary>
        public sealed class Result
        {
            /// <summary>Patch classes Harmony accepted and applied.</summary>
            public int Classes;
            /// <summary>Individual methods patched across those classes.</summary>
            public int Methods;
            /// <summary>Classes that threw while patching, with the reason.</summary>
            public readonly List<string> Failed = new List<string>();
            /// <summary>Classes that look like patch classes but lack the class-level attribute.</summary>
            public readonly List<string> Unattributed = new List<string>();

            public bool Clean { get { return Failed.Count == 0 && Unattributed.Count == 0; } }
        }

        // The member-level attributes that mean "this class intends to patch something".
        // A type carrying any of these but no type-level [HarmonyPatch] is case 2 above.
        private static readonly Type[] PatchKindAttributes =
        {
            typeof(HarmonyPrefix), typeof(HarmonyPostfix), typeof(HarmonyTranspiler),
            typeof(HarmonyFinalizer), typeof(HarmonyTargetMethod), typeof(HarmonyTargetMethods),
        };

        /// <summary>
        /// Apply every patch class in <paramref name="assembly"/>, isolated from each other, and
        /// log a census. Call this INSTEAD of <c>harmony.PatchAll()</c> — doing both double-patches.
        /// </summary>
        public static Result ApplyAndReport(Harmony harmony, Assembly assembly, ManualLogSource log)
        {
            if (harmony == null) throw new ArgumentNullException("harmony");
            if (assembly == null) throw new ArgumentNullException("assembly");

            var result = new Result();

            foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
            {
                // Harmony's own resolution decides what a patch class is and what it targets;
                // re-implementing that here would be a second source of truth that drifts.
                List<MethodInfo> patched = null;
                try
                {
                    patched = harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    // Unwrap: HarmonyException's own message is a generic wrapper, and the
                    // inner exception is the one that names the missing method.
                    Exception cause = e.InnerException ?? e;
                    result.Failed.Add(type.FullName + " — " + cause.Message);
                    if (log != null)
                    {
                        log.LogError("PATCH FAILED: " + type.FullName + " — " + cause.Message);
                    }
                    continue;
                }

                if (patched != null && patched.Count > 0)
                {
                    result.Classes++;
                    result.Methods += patched.Count;
                    continue;
                }

                // Patch() returned nothing. Either the type is not a patch class (the common,
                // uninteresting case) or it MEANT to be one and lost its class attribute.
                if (LooksLikeAPatchClass(type))
                {
                    result.Unattributed.Add(type.FullName);
                    if (log != null)
                    {
                        log.LogError("PATCH SKIPPED: " + type.FullName + " declares Harmony patch "
                            + "methods but the TYPE carries no [HarmonyPatch] attribute, so PatchAll "
                            + "never visits it. Add a bare [HarmonyPatch] to the class.");
                    }
                }
            }

            Report(result, assembly, log);
            return result;
        }

        private static bool LooksLikeAPatchClass(Type type)
        {
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods(AccessTools.all);
            }
            catch (Exception)
            {
                return false;   // reflection-hostile type; not our problem to diagnose
            }

            foreach (MethodInfo m in methods)
            {
                foreach (Type attr in PatchKindAttributes)
                {
                    if (m.GetCustomAttributes(attr, false).Length > 0) return true;
                }
                // A method-level [HarmonyPatch] counts too: it is the other half of the
                // bare-class-attribute idiom, and on its own it is just as invisible.
                if (m.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0) return true;
            }
            return false;
        }

        private static void Report(Result r, Assembly assembly, ManualLogSource log)
        {
            if (log == null) return;

            string name = assembly.GetName().Name;
            if (r.Clean)
            {
                log.LogInfo(string.Format(
                    "Patch census [{0}]: {1} methods across {2} classes, all bound.",
                    name, r.Methods, r.Classes));
                return;
            }

            log.LogWarning(string.Format(
                "Patch census [{0}]: {1} methods across {2} classes bound, "
                + "{3} class(es) FAILED, {4} class(es) SKIPPED (no class attribute).",
                name, r.Methods, r.Classes, r.Failed.Count, r.Unattributed.Count));
            log.LogWarning("A game update is the usual cause. Re-check the failing targets against "
                + "decompiled/ (see tools/sync-decompiled.ps1) and game-build.json for which build "
                + "this is.");
        }
    }
}
