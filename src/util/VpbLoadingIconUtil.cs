using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace VPB.src.util
{
    /// <summary>
    /// Recover VaM hourglass / frozen sim when AsyncFlags were orphaned
    /// (e.g. atom destroyed mid JSONStorableDynamic load, or catalog-refresh pause).
    /// Cold/warm path only — never call from Update.
    /// </summary>
    public static class VpbLoadingIconUtil
    {
        private static FieldInfo s_LoadingIconFlagsField;
        private static FieldInfo s_HoldLoadCompleteFlagsField;
        private static FieldInfo s_WaitResumeSimulationFlagsField;
        private static FieldInfo s_SceneLoadFlagField;
        private static bool s_FieldsResolved;

        private static void EnsureFields()
        {
            if (s_FieldsResolved) return;
            s_FieldsResolved = true;
            try
            {
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
                Type t = typeof(SuperController);
                s_LoadingIconFlagsField = t.GetField("loadingIconFlags", bf);
                s_HoldLoadCompleteFlagsField = t.GetField("holdLoadCompleteFlags", bf);
                s_WaitResumeSimulationFlagsField = t.GetField("waitResumeSimulationFlags", bf);
                s_SceneLoadFlagField = t.GetField("loadFlag", bf);
            }
            catch { }
        }

        private static int RaiseAndPruneFlagList(IList list, StringBuilder names)
        {
            if (list == null || list.Count == 0) return 0;
            int raised = 0;
            for (int i = 0; i < list.Count; i++)
            {
                AsyncFlag af = list[i] as AsyncFlag;
                if (af == null) continue;
                try
                {
                    if (names != null)
                    {
                        if (names.Length > 0) names.Append(", ");
                        names.Append(af.Name ?? "?");
                        names.Append(af.Raised ? "(R)" : "(pending)");
                    }
                }
                catch { }
                if (af.Raised) continue;
                try
                {
                    af.Raise();
                    raised++;
                }
                catch { }
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                AsyncFlag af = list[i] as AsyncFlag;
                if (af == null || af.Raised)
                {
                    try { list.RemoveAt(i); } catch { }
                }
            }
            return raised;
        }

        public static int RaiseStuckLoadingIconFlags(string reason = null)
        {
            EnsureFields();
            SuperController sc = SuperController.singleton;
            if (sc == null || s_LoadingIconFlagsField == null) return 0;

            int raised = 0;
            var names = new StringBuilder(128);
            try
            {
                var list = s_LoadingIconFlagsField.GetValue(sc) as IList;
                raised = RaiseAndPruneFlagList(list, names);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] RaiseStuckLoadingIconFlags failed: " + ex.Message);
            }

            if (raised > 0 || (names.Length > 0))
            {
                LogUtil.LogWarning("[VPB] loadingIconFlags raise=" + raised
                    + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")")
                    + (names.Length > 0 ? " | " + names : ""));
            }
            return raised;
        }

        public static int RaiseStuckHoldLoadFlags(string reason = null)
        {
            EnsureFields();
            SuperController sc = SuperController.singleton;
            if (sc == null || s_HoldLoadCompleteFlagsField == null) return 0;

            int raised = 0;
            var names = new StringBuilder(64);
            try
            {
                var list = s_HoldLoadCompleteFlagsField.GetValue(sc) as IList;
                raised = RaiseAndPruneFlagList(list, names);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] RaiseStuckHoldLoadFlags failed: " + ex.Message);
            }

            if (raised > 0)
            {
                LogUtil.LogWarning("[VPB] holdLoadCompleteFlags raise=" + raised
                    + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")")
                    + (names.Length > 0 ? " | " + names : ""));
            }
            return raised;
        }

        public static int RaiseStuckSimulationFlags(string reason = null)
        {
            EnsureFields();
            SuperController sc = SuperController.singleton;
            if (sc == null || s_WaitResumeSimulationFlagsField == null) return 0;

            int raised = 0;
            var names = new StringBuilder(64);
            try
            {
                var list = s_WaitResumeSimulationFlagsField.GetValue(sc) as IList;
                raised = RaiseAndPruneFlagList(list, names);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] RaiseStuckSimulationFlags failed: " + ex.Message);
            }

            if (raised > 0)
            {
                LogUtil.LogWarning("[VPB] waitResumeSimulationFlags raise=" + raised
                    + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")")
                    + (names.Length > 0 ? " | " + names : ""));
            }
            return raised;
        }

        public static bool RaiseSceneLoadFlag(string reason = null)
        {
            EnsureFields();
            SuperController sc = SuperController.singleton;
            if (sc == null || s_SceneLoadFlagField == null) return false;
            try
            {
                AsyncFlag af = s_SceneLoadFlagField.GetValue(sc) as AsyncFlag;
                if (af == null || af.Raised) return false;
                af.Raise();
                LogUtil.LogWarning("[VPB] Raised SuperController.loadFlag"
                    + (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")")
                    + " name=" + (af.Name ?? "?"));
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] RaiseSceneLoadFlag failed: " + ex.Message);
                return false;
            }
        }

        public static void RecoverAfterBulkAtomTeardown(string reason)
        {
            LogUtil.LogWarning("[VPB] RecoverAfterBulkAtomTeardown begin: " + (reason ?? ""));
            RaiseStuckLoadingIconFlags(reason);
            RaiseStuckHoldLoadFlags(reason);
            RaiseStuckSimulationFlags(reason);
            RaiseSceneLoadFlag(reason);

            try
            {
                SuperController sc = SuperController.singleton;
                if (sc == null) return;
                if (sc.loadingIcon != null)
                    sc.loadingIcon.gameObject.SetActive(false);
                // Only hide full loading UI when VaM itself reports not loading.
                if (!sc.isLoading && sc.loadingUI != null && sc.loadingUI.gameObject.activeSelf)
                {
                    sc.loadingUI.gameObject.SetActive(false);
                    LogUtil.LogWarning("[VPB] Forced loadingUI off (isLoading=false) (" + reason + ")");
                }
            }
            catch { }

            LogUtil.LogWarning("[VPB] RecoverAfterBulkAtomTeardown end: " + (reason ?? ""));
        }

        /// <summary>Diagnose current flag state without mutating (unless raise=true).</summary>
        public static void LogFlagSnapshot(string reason, bool raise = false)
        {
            EnsureFields();
            SuperController sc = SuperController.singleton;
            if (sc == null) return;
            try
            {
                bool loading = false;
                try { loading = sc.isLoading; } catch { }
                int iconN = 0, holdN = 0, simN = 0;
                try
                {
                    var a = s_LoadingIconFlagsField != null ? s_LoadingIconFlagsField.GetValue(sc) as IList : null;
                    iconN = a != null ? a.Count : 0;
                }
                catch { }
                try
                {
                    var a = s_HoldLoadCompleteFlagsField != null ? s_HoldLoadCompleteFlagsField.GetValue(sc) as IList : null;
                    holdN = a != null ? a.Count : 0;
                }
                catch { }
                try
                {
                    var a = s_WaitResumeSimulationFlagsField != null ? s_WaitResumeSimulationFlagsField.GetValue(sc) as IList : null;
                    simN = a != null ? a.Count : 0;
                }
                catch { }

                LogUtil.LogWarning("[VPB] LoadFlagSnapshot (" + (reason ?? "") + "): isLoading=" + loading
                    + " loadingIconFlags=" + iconN
                    + " holdLoad=" + holdN
                    + " waitSim=" + simN);

                if (raise)
                    RecoverAfterBulkAtomTeardown(reason + "-raise");
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] LogFlagSnapshot failed: " + ex.Message);
            }
        }
    }
}
