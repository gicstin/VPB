using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public static class ThirdPartyFixHook
    {
        private const float CheesyFxInGameNreTailSeconds = 0.5f;

        private static bool _parentHoldLinkPatched = false;
        private static bool _unityLogSourcePatched = false;
        private static bool _loggerInternalLogEventPatched = false;
        private static bool _unityLogListenerPatched = false;
        private static bool _superControllerInGameLogPatched = false;
        private static bool _superControllerLogUiFontPatched = false;

        private static int _ingameBurstLinesRemaining;
        private static float _ingameBurstUntilRealtime;
        private static float _lastCheesyFxErrorRealtime = -999f;

        private static int _lastPatchScanAssemblyCount = -1;

        private static bool AllPatchesApplied
        {
            get
            {
                return _parentHoldLinkPatched
                    && _unityLogSourcePatched
                    && _loggerInternalLogEventPatched
                    && _unityLogListenerPatched
                    && _superControllerInGameLogPatched
                    && _superControllerLogUiFontPatched;
            }
        }

        /// <summary>
        /// True when a still-unpatched third-party type could plausibly have appeared since the last scan.
        /// <see cref="PatchAll"/> resolves types via <c>AccessTools.TypeByName</c>, which walks every type in
        /// every loaded assembly — far too expensive to repeat on each Unity scene load. CustomUnityAsset
        /// atoms load their .assetbundle scene additively on every skybox/environment pick, so that path fires
        /// constantly. New third-party types can only arrive with a new assembly, so gate on the assembly count.
        /// </summary>
        internal static bool ShouldRetryPendingPatches()
        {
            if (AllPatchesApplied) return false;
            int count;
            try { count = AppDomain.CurrentDomain.GetAssemblies().Length; }
            catch { return true; }
            if (count == _lastPatchScanAssemblyCount) return false;
            _lastPatchScanAssemblyCount = count;
            return true;
        }

        public static void PatchAll(Harmony harmony)
        {
            try { _lastPatchScanAssemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length; } catch { }
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

                if (!_unityLogSourcePatched)
                {
                    Type unityLogSourceType = AccessTools.TypeByName("BepInEx.Logging.UnityLogSource");
                    MethodInfo onUnityLog = unityLogSourceType != null
                        ? AccessTools.Method(unityLogSourceType, "OnUnityLogMessageReceived")
                        : null;
                    if (onUnityLog != null)
                    {
                        MethodInfo prefix = AccessTools.Method(typeof(ThirdPartyFixHook), nameof(UnityLogSource_OnUnityLogMessageReceived_Prefix));
                        harmony.Patch(onUnityLog, prefix: new HarmonyMethod(prefix));
                        _unityLogSourcePatched = true;
                        LogUtil.Log("[VPB] Patched BepInEx.Logging.UnityLogSource.OnUnityLogMessageReceived for CheesyFX log suppression");
                    }
                }

                if (!_loggerInternalLogEventPatched)
                {
                    Type loggerType = AccessTools.TypeByName("BepInEx.Logging.Logger");
                    MethodInfo internalLogEvent = loggerType != null
                        ? AccessTools.Method(loggerType, "InternalLogEvent")
                        : null;
                    if (internalLogEvent != null)
                    {
                        MethodInfo prefix = AccessTools.Method(typeof(ThirdPartyFixHook), nameof(Logger_InternalLogEvent_Prefix));
                        harmony.Patch(internalLogEvent, prefix: new HarmonyMethod(prefix));
                        _loggerInternalLogEventPatched = true;
                        LogUtil.Log("[VPB] Patched BepInEx.Logging.Logger.InternalLogEvent for CheesyFX log suppression");
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
                            LogUtil.Log("[VPB] Patched BepInEx.Logging.UnityLogListener." + m.Name + " (legacy) for third-party Unity log suppression");
                            break;
                        }
                    }
                }

                if (!_superControllerInGameLogPatched)
                {
                    PatchSuperControllerInGameLogs(harmony);
                }

                PatchSuperControllerLogUiFont(harmony);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Failed to patch third-party scripts: " + ex.Message);
            }
        }

        private static void PatchSuperControllerInGameLogs(Harmony harmony)
        {
            MethodInfo errorPrefix = AccessTools.Method(typeof(ThirdPartyFixHook), nameof(SuperController_ErrorOrLogError_Prefix));
            MethodInfo messagePrefix = AccessTools.Method(typeof(ThirdPartyFixHook), nameof(SuperController_MessageOrLogMessage_Prefix));
            int patched = 0;

            foreach (MethodInfo method in typeof(SuperController).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null) continue;
                if (method.ReturnType != typeof(void)) continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters == null || parameters.Length == 0 || parameters[0].ParameterType != typeof(string))
                    continue;

                if (method.Name == "Error" || method.Name == "LogError")
                {
                    try { harmony.Patch(method, prefix: new HarmonyMethod(errorPrefix)); patched++; } catch { }
                }
                else if (method.Name == "Message" || method.Name == "LogMessage")
                {
                    try { harmony.Patch(method, prefix: new HarmonyMethod(messagePrefix)); patched++; } catch { }
                }
            }

            if (patched > 0)
            {
                _superControllerInGameLogPatched = true;
                LogUtil.Log("[VPB] Patched SuperController.Error/LogError/Message/LogMessage x" + patched + " for in-game log suppression");
            }
        }

        private static void PatchSuperControllerLogUiFont(Harmony harmony)
        {
            if (_superControllerLogUiFontPatched) return;

            MethodInfo postfix = AccessTools.Method(typeof(ThirdPartyFixHook), nameof(SuperController_SyncMessageLogFont_Postfix));
            if (postfix == null) return;

            int patched = 0;
            foreach (string methodName in new[] {
                "Start",
                "SyncAllMessagesInputFields",
                "SyncAllErrorsInputFields",
                "OpenMessageLogPanel",
                "OpenErrorLogPanel"
            })
            {
                MethodInfo method = AccessTools.Method(typeof(SuperController), methodName);
                if (method == null) continue;
                try
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    patched++;
                }
                catch { }
            }

            if (patched > 0)
            {
                _superControllerLogUiFontPatched = true;
                LogUtil.Log("[VPB] Patched SuperController message log font sync x" + patched);
            }
        }

        private static void SuperController_SyncMessageLogFont_Postfix(SuperController __instance)
        {
            try { SyncMessageLogFontToErrorLog(__instance); } catch { }
        }

        private static void SyncMessageLogFontToErrorLog(SuperController sc)
        {
            if (sc == null) return;
            ApplyMessageLogInputFieldStyle(sc.allErrorsInputField, sc.allMessagesInputField);
            ApplyMessageLogInputFieldStyle(sc.allErrorsInputField2, sc.allMessagesInputField2);
        }

        private static void ApplyMessageLogInputFieldStyle(InputField reference, InputField messageField)
        {
            if (messageField == null) return;

            Text msgText = messageField.textComponent;
            if (msgText == null) return;

            Text refText = reference != null ? reference.textComponent : null;

            if (refText != null)
            {
                if (refText.fontSize > 0)
                    msgText.fontSize = refText.fontSize;
                if (refText.font != null)
                    msgText.font = refText.font;
                msgText.lineSpacing = refText.lineSpacing;
                msgText.alignment = refText.alignment;
                msgText.supportRichText = refText.supportRichText;
                msgText.horizontalOverflow = refText.horizontalOverflow;
                msgText.verticalOverflow = refText.verticalOverflow;

                if (reference.lineType != InputField.LineType.SingleLine)
                    messageField.lineType = reference.lineType;
            }
            else
            {
                if (messageField.lineType == InputField.LineType.SingleLine)
                    messageField.lineType = InputField.LineType.MultiLineNewline;
                msgText.horizontalOverflow = HorizontalWrapMode.Wrap;
                msgText.verticalOverflow = VerticalWrapMode.Overflow;
                if (msgText.fontSize < 12)
                    msgText.fontSize = 14;
            }

            // Message log prefab often uses best-fit/overflow; long lines shrink to fit width.
            msgText.resizeTextForBestFit = false;
            if (msgText.horizontalOverflow == HorizontalWrapMode.Overflow)
                msgText.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (messageField.lineType == InputField.LineType.SingleLine)
                messageField.lineType = InputField.LineType.MultiLineNewline;

            Graphic placeholder = messageField.placeholder;
            Text phText = placeholder as Text;
            if (phText != null)
            {
                phText.resizeTextForBestFit = false;
                if (msgText.fontSize > 0)
                    phText.fontSize = msgText.fontSize;
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

        internal static void TryClearInGameLogsOnSceneLaunch(SuperController sc, bool loadMerge)
        {
            if (sc == null || loadMerge) return;
            try
            {
                var c = VPBConfig.Instance;
                if (c == null || !c.ClearInGameLogsOnSceneLaunch) return;
                sc.ClearErrors();
                sc.ClearMessages();
            }
            catch { }
        }

        internal static bool ShouldSuppressCheesyFxUnityLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Assert && type != LogType.Exception)
                return false;

            if (ShouldSuppressCheesyFxNullReferenceLog(CombineUnityLogParts(message, stackTrace)))
                return true;
            if (ShouldSuppressCheesyFxNullReferenceLog(message))
                return true;
            if (ShouldSuppressCheesyFxNullReferenceLog(stackTrace))
                return true;

            return IsNullReferenceMessage(message) && ContainsCheesyFx(stackTrace);
        }

        private static bool IsCheesyFxSuppressionEnabled()
        {
            try
            {
                return VPBConfig.Instance != null && VPBConfig.Instance.SuppressCheesyFxNullReferenceLogs;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsMissingAddonDependencyMessage(string msg)
        {
            return !string.IsNullOrEmpty(msg)
                && msg.IndexOf("Missing addon package", StringComparison.OrdinalIgnoreCase) >= 0
                && msg.IndexOf("depends on", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool ShouldSuppressMissingDependencyLog(string msg)
        {
            if (string.IsNullOrEmpty(msg) || !IsMissingAddonDependencyMessage(msg))
                return false;
            try
            {
                return VPBConfig.Instance != null && VPBConfig.Instance.HideMissingDependencyLogs;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldSuppressAllInGameMessages()
        {
            try
            {
                var c = VPBConfig.Instance;
                if (c == null) return false;
                string mode = c.BlockInGameMessages ?? "Off";
                if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase)) return false;
                if (string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase)) return true;
                bool isVR = c.IsVR;
                if (string.Equals(mode, "VR Only", StringComparison.OrdinalIgnoreCase)) return isVR;
                if (string.Equals(mode, "Desktop Only", StringComparison.OrdinalIgnoreCase)) return !isVR;
                return false;
            }
            catch { return false; }
        }

        private static void NoteCheesyFxErrorActivity()
        {
            _lastCheesyFxErrorRealtime = Time.realtimeSinceStartup;
            _ingameBurstLinesRemaining = 8;
            _ingameBurstUntilRealtime = Time.realtimeSinceStartup + 0.25f;
        }

        private static bool IsInCheesyFxInGameBurst()
        {
            return _ingameBurstLinesRemaining > 0 && Time.realtimeSinceStartup <= _ingameBurstUntilRealtime;
        }

        internal static bool ShouldSuppressCheesyFxInGameText(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsCheesyFxSuppressionEnabled())
                return false;

            if (ShouldSuppressCheesyFxNullReferenceLog(text))
            {
                NoteCheesyFxErrorActivity();
                return true;
            }

            if (IsInCheesyFxInGameBurst())
            {
                if (IsNullReferenceMessage(text)
                    || ContainsCheesyFx(text)
                    || text.IndexOf("Stack trace", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _ingameBurstLinesRemaining--;
                    return true;
                }
            }

            if (IsNullReferenceMessage(text)
                && Time.realtimeSinceStartup - _lastCheesyFxErrorRealtime < CheesyFxInGameNreTailSeconds)
            {
                return true;
            }

            return false;
        }

        internal static bool ShouldSuppressCheesyFxNullReferenceLog(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return false;
            if (!IsCheesyFxSuppressionEnabled())
                return false;

            if (!ContainsCheesyFx(msg))
                return false;

            if (IsNullReferenceMessage(msg))
                return true;

            if (msg.IndexOf(".Update", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (msg.IndexOf("UIManager", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (msg.IndexOf("Stack trace", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static bool ContainsCheesyFx(string s)
        {
            return !string.IsNullOrEmpty(s)
                && s.IndexOf("CheesyFX", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsNullReferenceMessage(string s)
        {
            return !string.IsNullOrEmpty(s)
                && (s.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0
                    || s.IndexOf("Object reference not set to an instance of an object", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string CombineUnityLogParts(string part0, string part1)
        {
            string msg = !string.IsNullOrEmpty(part0) ? part0 : part1;
            if (string.IsNullOrEmpty(msg)) msg = part1 ?? "";
            if (!string.IsNullOrEmpty(part0) && !string.IsNullOrEmpty(part1)
                && !string.Equals(part0, part1, StringComparison.Ordinal))
            {
                msg = part0 + "\n" + part1;
            }
            return msg;
        }

        private static bool SuperController_ErrorOrLogError_Prefix(string __0)
        {
            try
            {
                if (ShouldSuppressAllInGameMessages()) return false;
                if (ShouldSuppressMissingDependencyLog(__0)) return false;
                if (ShouldSuppressCheesyFxInGameText(__0)) return false;
            }
            catch
            {
            }
            return true;
        }

        private static bool SuperController_MessageOrLogMessage_Prefix()
        {
            try
            {
                if (ShouldSuppressAllInGameMessages()) return false;
            }
            catch
            {
            }
            return true;
        }

        private static bool UnityLogSource_OnUnityLogMessageReceived_Prefix(string message, string stackTrace, LogType type)
        {
            try
            {
                VamStartupProfiler.OnUnityStartupLog(message);
            }
            catch { }
            try
            {
                if (ShouldSuppressCheesyFxUnityLog(message, stackTrace, type))
                {
                    NoteCheesyFxErrorActivity();
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }

        private static bool Logger_InternalLogEvent_Prefix(object sender, LogEventArgs eventArgs)
        {
            try
            {
                if (eventArgs?.Source == null || eventArgs.Source.SourceName != "Unity Log")
                    return true;

                string data = eventArgs.Data as string;
                if (!string.IsNullOrEmpty(data))
                {
                    if (ShouldSuppressMissingDependencyLog(data))
                        return false;
                    if (ShouldSuppressCheesyFxNullReferenceLog(data))
                        return false;
                }
            }
            catch
            {
            }
            return true;
        }

        private static bool UnityLogListener_Log_Prefix(string __0, string __1, LogType __2)
        {
            try
            {
                if (__2 == LogType.Error || __2 == LogType.Exception || __2 == LogType.Assert)
                {
                    string msg = CombineUnityLogParts(__0, __1);

                    if (ShouldSuppressMissingDependencyLog(msg))
                        return false;

                    if (ShouldSuppressCheesyFxUnityLog(__0, __1, __2))
                        return false;
                }
            }
            catch
            {
            }
            return true;
        }
    }
}
