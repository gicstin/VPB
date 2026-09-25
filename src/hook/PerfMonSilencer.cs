using HarmonyLib;
using System;
using UnityEngine;

namespace VPB
{
    public static class PerfMonSilencer
    {
        public static void Patch(Harmony harmony)
        {
            try
            {
                Silence(harmony, "MeshVR.PerfMonCamera", "OnPreCull");
                Silence(harmony, "MeshVR.PerfMonPre", "Update");
                Silence(harmony, "MeshVR.PerfMonPre", "FixedUpdate");
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] PerfMonSilencer.Patch failed: {ex.Message}");
            }
        }

        private static void Silence(Harmony harmony, string typeName, string methodName)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                if (type == null) return;

                var method = AccessTools.Method(type, methodName);
                if (method == null) return;

                var prefix = new HarmonyMethod(typeof(PerfMonSilencer), nameof(Prefix));
                harmony.Patch(method, prefix);
                LogUtil.Log($"[VPB] Silenced {typeName}.{methodName}");
            }
            catch (Exception)
            {
                // No error log here to avoid spam from per-method patch failures.
            }
        }

        private static bool Prefix()
        {
            return false;
        }
    }
}
