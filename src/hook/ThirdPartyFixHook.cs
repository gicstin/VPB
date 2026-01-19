using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VPB
{
    public static class ThirdPartyFixHook
    {
        private static bool _parentHoldLinkPatched = false;

        public static void PatchAll(Harmony harmony)
        {
            try
            {
                // Patch MacGruber.ParentHoldLink.OnEnable to prevent NRE during LateRestore
                if (!_parentHoldLinkPatched)
                {
                    Type parentHoldLinkType = AccessTools.TypeByName("MacGruber.ParentHoldLink");
                    if (parentHoldLinkType != null)
                    {
                        MethodInfo onEnableMethod = AccessTools.Method(parentHoldLinkType, "OnEnable");
                        if (onEnableMethod != null)
                        {
                            MethodInfo finalizer = AccessTools.Method(typeof(ThirdPartyFixHook), nameof(ParentHoldLink_OnEnable_Finalizer));
                            harmony.Patch(onEnableMethod, finalizer: new HarmonyMethod(finalizer));
                            _parentHoldLinkPatched = true;
                            LogUtil.Log("[VPB] Successfully patched MacGruber.ParentHoldLink.OnEnable with Finalizer");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Failed to patch third-party scripts: " + ex.Message);
            }
        }

        // Finalizer for MacGruber.ParentHoldLink.OnEnable to catch and suppress exceptions
        private static Exception ParentHoldLink_OnEnable_Finalizer(MonoBehaviour __instance, Exception __exception)
        {
            if (__exception != null)
            {
                LogUtil.LogWarning($"[VPB] Suppressed exception in {__instance.GetType().Name}.OnEnable: {__exception.Message}\n{__exception.StackTrace}");
                return null; // Suppress the exception
            }
            return null;
        }
    }
}
