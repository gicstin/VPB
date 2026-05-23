using System;
using System.Reflection;
using UnityEngine;

namespace VPB
{
    /// <summary>Cold-start optimizations gated by Settings and logged under [VPB.Startup.Opt].</summary>
    internal static class VamStartupOptimizations
    {
        public const string LogTag = "[VPB.Startup.Opt]";

        static bool s_VamXKnownAbsent;
        static int s_VamXSkipCount;
        static FieldInfo s_VamXInstalledField;
        static FieldInfo s_VamXVersionField;
        static bool s_VamXFastAbsentLogged;

        static bool GetVamXInstalled(SuperController instance)
        {
            if (instance == null) return false;
            try
            {
                if (s_VamXInstalledField == null)
                {
                    s_VamXInstalledField = typeof(SuperController).GetField("_vamXInstalled",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
                if (s_VamXInstalledField == null) return false;
                return (bool)s_VamXInstalledField.GetValue(instance);
            }
            catch { return false; }
        }

        public static bool DeferGallerySqlRebuildUntilReady
        {
            get
            {
                try
                {
                    var inst = Settings.Instance;
                    if (inst == null || inst.StartupDeferGallerySqlRebuild == null) return true;
                    return inst.StartupDeferGallerySqlRebuild.Value;
                }
                catch { return true; }
            }
        }

        public static bool SkipRedundantSyncVamX
        {
            get
            {
                try
                {
                    var inst = Settings.Instance;
                    if (inst == null || inst.StartupSkipRedundantSyncVamX == null) return true;
                    return inst.StartupSkipRedundantSyncVamX.Value;
                }
                catch { return true; }
            }
        }

        public static void InvalidateVamXAbsentCache()
        {
            if (!s_VamXKnownAbsent) return;
            s_VamXKnownAbsent = false;
            try { LogUtil.Log(LogTag + " SyncVamX cache invalidated"); } catch { }
        }

        public static void InvalidateVamXAbsentCacheIfVamXPackageTouched(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (uid.StartsWith("vamX", StringComparison.OrdinalIgnoreCase))
                InvalidateVamXAbsentCache();
        }

        public static float DeferredGallerySqlRebuildDelaySec
        {
            get
            {
                try
                {
                    var inst = Settings.Instance;
                    if (inst != null && inst.StartupDeferGallerySqlRebuildDelaySec != null)
                        return Mathf.Max(0f, inst.StartupDeferGallerySqlRebuildDelaySec.Value);
                }
                catch { }
                return 2f;
            }
        }

        public static bool DeferStartupDependencyGraphRebuild
        {
            get
            {
                try
                {
                    var inst = Settings.Instance;
                    if (inst == null || inst.StartupDeferDependencyGraphRebuild == null) return true;
                    return inst.StartupDeferDependencyGraphRebuild.Value;
                }
                catch { return true; }
            }
        }

        public static bool SkipGallerySqlRebuildIfValid
        {
            get
            {
                try
                {
                    var inst = Settings.Instance;
                    if (inst == null || inst.StartupSkipGallerySqlRebuildIfValid == null) return true;
                    return inst.StartupSkipGallerySqlRebuildIfValid.Value;
                }
                catch { return true; }
            }
        }

        public static bool SqlBatchCatMemInserts
        {
            get
            {
                try
                {
                    var inst = Settings.Instance;
                    if (inst == null || inst.StartupSqlBatchCatMem == null) return false;
                    return inst.StartupSqlBatchCatMem.Value;
                }
                catch { return false; }
            }
        }

        public static int SqlBatchCatMemRowCount
        {
            get
            {
                try
                {
                    var inst = Settings.Instance;
                    if (inst != null && inst.StartupSqlBatchCatMemRows != null)
                        return Math.Max(20, Math.Min(1000, inst.StartupSqlBatchCatMemRows.Value));
                }
                catch { }
                return 150;
            }
        }

        /// <summary>After first SyncVamX found vamX missing, skip later calls until refresh invalidates cache.</summary>
        public static bool TrySkipSyncVamX(SuperController instance)
        {
            if (!SkipRedundantSyncVamX) return false;
            if (!s_VamXKnownAbsent) return false;
            if (instance == null) return false;

            // If vamX later appears in whitelist/native registry, do not skip.
            try
            {
                if (MVR.FileManagement.FileManager.GetPackage("vamX.1.latest") != null)
                {
                    s_VamXKnownAbsent = false;
                    return false;
                }
            }
            catch { }

            int n = System.Threading.Interlocked.Increment(ref s_VamXSkipCount);
            if (n <= 3 || (n % 10) == 0)
            {
                try { LogUtil.Log(LogTag + " SyncVamX skipped (vamX known absent) count=" + n); } catch { }
            }
            VamStartupProfiler.RecordSyncVamXSkipped();
            return true;
        }

        public static bool GetVamXInstalledForProfiler(SuperController instance)
        {
            return GetVamXInstalled(instance);
        }

        public static void NoteSyncVamXCompleted(bool vamXInstalled)
        {
            if (vamXInstalled)
            {
                s_VamXKnownAbsent = false;
                return;
            }
            s_VamXKnownAbsent = true;
        }

        public static bool IsVamXBootstrapPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.IndexOf("vamX", StringComparison.OrdinalIgnoreCase) >= 0
                && path.IndexOf("vamXBootstrap", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Skip expensive on-demand rewrite/register for vamX bootstrap FileExists during startup.</summary>
        public static bool ShouldSkipVamXFileExistsWork(string path)
        {
            if (!SkipRedundantSyncVamX || !IsVamXBootstrapPath(path)) return false;
            return !IsVamXPackageRegisteredInVam();
        }

        static bool IsVamXPackageRegisteredInVam()
        {
            try
            {
                return MVR.FileManagement.FileManager.GetPackage("vamX.1.latest") != null;
            }
            catch { return false; }
        }

        static bool IsVamXPackageInVpbInventory()
        {
            try
            {
                return FileManager.GetPackage("vamX.1.latest", ensureInstalled: false) != null;
            }
            catch { return false; }
        }

        /// <summary>Fast absent path for SyncVamX when vamX is not registered in VaM (ref SuperController.SyncVamX).</summary>
        public static bool TryApplyFastSyncVamXAbsent(SuperController instance)
        {
            if (!SkipRedundantSyncVamX || instance == null) return false;
            if (IsVamXPackageRegisteredInVam()) return false;

            if (IsVamXPackageInVpbInventory())
            {
                try
                {
                    VarPackage pkg = FileManager.GetPackage("vamX.1.latest", ensureInstalled: false);
                    if (pkg != null && !ScanWhitelistManager.Instance.IsPackageScanExcluded(pkg.Uid, pkg.Path ?? ""))
                        return false;
                }
                catch { return false; }
            }

            if (!ApplySyncVamXAbsentState(instance)) return false;

            s_VamXKnownAbsent = true;
            if (!s_VamXFastAbsentLogged)
            {
                s_VamXFastAbsentLogged = true;
                try { LogUtil.Log(LogTag + " SyncVamX fast absent (skip FileExists/on-demand)"); } catch { }
            }
            VamStartupProfiler.RecordSyncVamXSkipped();
            return true;
        }

        static bool ApplySyncVamXAbsentState(SuperController instance)
        {
            try
            {
                if (s_VamXInstalledField == null)
                {
                    s_VamXInstalledField = typeof(SuperController).GetField("_vamXInstalled",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
                if (s_VamXVersionField == null)
                {
                    s_VamXVersionField = typeof(SuperController).GetField("vamXVersion",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (s_VamXInstalledField != null)
                    s_VamXInstalledField.SetValue(instance, false);
                if (s_VamXVersionField != null)
                    s_VamXVersionField.SetValue(instance, 0);

                ApplyVamXUiAbsent(instance);
                return true;
            }
            catch { return false; }
        }

        static void ApplyVamXUiAbsent(SuperController instance)
        {
            SetActiveOnField(instance, "vamXPanel", false);
            SetActiveOnArray(instance, "vamXEnabledGameObjects", false);
            SetActiveOnArray(instance, "vamXEnabledAndAdvancedSceneEditGameObjects", false);
            // VaM shows one "Create" main-menu tile via vamXDisabledGameObjects when vamX is absent.
            // Enabling the advanced-scene-edit disabled arrays too duplicates that tile (see startup menu).
            SetActiveOnArray(instance, "vamXDisabledGameObjects", true);
            SetActiveOnArray(instance, "vamXDisabledAndAdvancedSceneEditGameObjects", false);
            SetActiveOnArray(instance, "vamXDisabledAndAdvancedSceneEditDisabledGameObjects", false);

            MethodInfo syncVersionText = typeof(SuperController).GetMethod("SyncVersionText",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (syncVersionText != null)
                syncVersionText.Invoke(instance, null);
        }

        static void SetActiveOnField(SuperController instance, string fieldName, bool active)
        {
            try
            {
                FieldInfo f = typeof(SuperController).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return;
                object val = f.GetValue(instance);
                if (val == null) return;
                Component c = val as Component;
                if (c != null && c.gameObject != null)
                    c.gameObject.SetActive(active);
            }
            catch { }
        }

        static void SetActiveOnArray(SuperController instance, string fieldName, bool active)
        {
            try
            {
                FieldInfo f = typeof(SuperController).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return;
                GameObject[] arr = f.GetValue(instance) as GameObject[];
                if (arr == null) return;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null)
                        arr[i].SetActive(active);
                }
            }
            catch { }
        }
    }
}

