using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VPB
{
    public static class ThirdPartyFixHook
    {
        private static bool _parentHoldLinkPatched = false;
        private static bool _unityLogListenerPatched = false;

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

                if (!_unityLogListenerPatched)
                {
                    Type unityLogListenerType = AccessTools.TypeByName("BepInEx.Logging.UnityLogListener");
                    if (unityLogListenerType != null)
                    {
                        MethodInfo prefix = AccessTools.Method(typeof(ThirdPartyFixHook), nameof(UnityLogListener_Log_Prefix));
                        var methods = unityLogListenerType.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        foreach (var m in methods)
                        {
                            if (m == null) continue;
                            var p = m.GetParameters();
                            if (p == null || p.Length != 3) continue;
                            if (p[0].ParameterType != typeof(string)) continue;
                            if (p[1].ParameterType != typeof(string)) continue;
                            if (p[2].ParameterType != typeof(LogType)) continue;

                            harmony.Patch(m, prefix: new HarmonyMethod(prefix));
                            _unityLogListenerPatched = true;
                            LogUtil.Log("[VPB] Patched BepInEx.Logging.UnityLogListener." + m.Name + " to suppress missing addon dependency spam");
                            break;
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

        private static bool UnityLogListener_Log_Prefix(string __0, string __1, LogType __2)
        {
            try
            {
                if (__2 == LogType.Error)
                {
                    string msg = !string.IsNullOrEmpty(__0) ? __0 : __1;
                    if (string.IsNullOrEmpty(msg)) msg = __1;

                    if (!string.IsNullOrEmpty(msg)
                        && msg.IndexOf("Missing addon package", StringComparison.OrdinalIgnoreCase) >= 0
                        && msg.IndexOf("depends on", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return false;
                    }
                }
            }
            catch
            {
            }
            return true;
        }
    }
}
