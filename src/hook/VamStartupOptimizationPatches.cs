using System;
using HarmonyLib;

namespace VPB
{
    /// <summary>VaM cold-start Harmony hooks (SyncVamX skips live in <see cref="VamStartupProfilerPatches"/>).</summary>
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
        }
    }
}
