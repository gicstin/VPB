using System;
using HarmonyLib;

namespace VPB
{
    /// <summary>
    /// Skip DAZ package-morph re-ingest during clothing/hair-only FileManager.Refresh.
    /// Measured: Refresh package morphs ~18–20s per person when TittyMagic/Naturalis banks reload.
    /// </summary>
    internal static class VpbCatalogRefreshHook
    {
        public static void PatchAll(Harmony harmony)
        {
            if (harmony == null) return;
            try
            {
                var sel = AccessTools.Method(typeof(DAZCharacterSelector), "RefreshPackageMorphs", Type.EmptyTypes);
                if (sel != null)
                {
                    harmony.Patch(sel, prefix: new HarmonyMethod(typeof(VpbCatalogRefreshHook), nameof(PreRefreshPackageMorphsBool)));
                }
                else
                {
                    LogUtil.LogWarning("VpbCatalogRefreshHook: DAZCharacterSelector.RefreshPackageMorphs not found.");
                }

                var bank = AccessTools.Method(typeof(DAZMorphBank), "RefreshPackageMorphs", Type.EmptyTypes);
                if (bank != null)
                {
                    harmony.Patch(bank, prefix: new HarmonyMethod(typeof(VpbCatalogRefreshHook), nameof(PreRefreshPackageMorphsBool)));
                }
                else
                {
                    LogUtil.LogWarning("VpbCatalogRefreshHook: DAZMorphBank.RefreshPackageMorphs not found.");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("VpbCatalogRefreshHook PatchAll failed: " + ex);
            }
        }

        // Both RefreshPackageMorphs overloads return bool (changed).
        static bool PreRefreshPackageMorphsBool(ref bool __result)
        {
            if (!VpbCatalogRefreshGuard.SkipPackageMorphRefresh)
                return true;
            __result = false;
            return false;
        }
    }
}
