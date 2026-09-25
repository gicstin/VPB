using System;
using HarmonyLib;

namespace VPB
{
    internal static class VamStartupOptimizationPatches
    {
        public static void Apply(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                harmony.PatchAll(typeof(VamStartupOptimizationPatches));
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning(VamStartupOptimizations.LogTag + " optimization PatchAll partial: " + ex.Message); }
                catch { }
            }
            VamNativePackageListing.Apply(harmony);
            VamPathFastPaths.Apply(harmony);
        }
    }
}
