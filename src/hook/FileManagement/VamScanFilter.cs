using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace VPB
{
    /// <summary>Intercepts VaM's native FileManager to block non-whitelisted packages from being registered during VaM's startup scan.</summary>
    internal static class VamScanFilter
    {
        private static MethodInfo s_VamRegisterPackageMethod;

        private static bool s_Discovered = false;

        /// <summary>Must be called once during plugin startup (VamHookPlugin.Start).</summary>
        public static void DiscoverVamInternals()
        {
            if (s_Discovered) return;
            s_Discovered = true;

            try
            {
                var fmType = typeof(MVR.FileManagement.FileManager);

                string[] methodCandidates = new[] {
                    "RegisterVarPackage", "AddVarPackage",
                    "RegisterPackage", "AddPackage",
                    "LoadVarPackage", "LoadPackage"
                };

                foreach (var name in methodCandidates)
                {
                    var m = fmType.GetMethod(name,
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                        null, new[] { typeof(string) }, null);
                    if (m != null)
                    {
                        s_VamRegisterPackageMethod = m;
                        break;
                    }
                }

                LogUtil.Log("[VPB ScanFilter] VaM register package method: " +
                    (s_VamRegisterPackageMethod?.Name ?? "NOT FOUND (on-demand registration disabled)"));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB ScanFilter] Discovery failed: " + ex.Message);
            }
        }

        public static bool TryRegisterVarInVam(string varPath)
        {
            if (s_VamRegisterPackageMethod == null) return false;
            if (string.IsNullOrEmpty(varPath)) return false;

            VamOnDemandLoader.s_AllowRegistration = true;
            try
            {
                s_VamRegisterPackageMethod.Invoke(null, new object[] { varPath });
                return true;
            }
            catch (Exception ex)
            {
                Exception root = ex;
                while (root.InnerException != null) root = root.InnerException;
                LogUtil.LogWarning("[VPB ScanFilter] TryRegisterVarInVam failed for " + varPath + ": "
                    + root.GetType().Name + ": " + root.Message);
                return false;
            }
            finally
            {
                VamOnDemandLoader.s_AllowRegistration = false;
            }
        }

        public static bool HasRegisterMethodAccess => s_VamRegisterPackageMethod != null;

        internal enum NativeRegistrationGate
        {
            Unfiltered,
            Allowed,
            Blocked
        }

        internal static NativeRegistrationGate ClassifyNativeRegistration(string vpath)
        {
            if (VamOnDemandLoader.s_AllowRegistration) return NativeRegistrationGate.Unfiltered;
            if (string.IsNullOrEmpty(vpath)) return NativeRegistrationGate.Unfiltered;

            string norm = vpath.Replace('\\', '/');
            if (!norm.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) return NativeRegistrationGate.Unfiltered;

            ScanWhitelistManager whitelist = ScanWhitelistManager.Instance;
            if (!whitelist.IsEnabled) return NativeRegistrationGate.Unfiltered;

            string uid = Path.GetFileNameWithoutExtension(norm);
            if (whitelist.IsUidOverrideIncluded(uid)) return NativeRegistrationGate.Allowed;
            return whitelist.IsPathWhitelisted(norm) ? NativeRegistrationGate.Allowed : NativeRegistrationGate.Blocked;
        }

        // VaM's FileManager exposes RegisterPackage even before its first Refresh has populated internal AssetBundle/file dictionaries.
        private static int s_HasVamRefreshedAtLeastOnce;

        public static bool HasVamRefreshedAtLeastOnce => s_HasVamRefreshedAtLeastOnce != 0;

        public static void MarkVamRefreshed()
        {
            int prev = System.Threading.Interlocked.Exchange(ref s_HasVamRefreshedAtLeastOnce, 1);
            if (prev == 0)
            {
                try { VamStartupProfiler.Milestone("native_FileManager.first_refresh_complete"); } catch { }
                VamOnDemandLoader.NotifyVamFileManagerRefreshed();
            }
        }

        private static int s_ScanAllowed;
        private static int s_ScanBlocked;
        // Tracks in-flight VaM refresh so on-demand registration avoids mutating dictionaries mid-enumeration.
        private static int s_VamRefreshInProgressCount;

        public static void ResetScanCounters() { s_ScanAllowed = 0; s_ScanBlocked = 0; }
        public static void RecordScanAllowed() { System.Threading.Interlocked.Increment(ref s_ScanAllowed); }
        public static void RecordScanBlocked() { System.Threading.Interlocked.Increment(ref s_ScanBlocked); }
        public static void RecordScanBlocked(int count) { if (count > 0) System.Threading.Interlocked.Add(ref s_ScanBlocked, count); }
        public static int ScanAllowedCount => System.Threading.Interlocked.CompareExchange(ref s_ScanAllowed, 0, 0);
        public static int ScanBlockedCount => System.Threading.Interlocked.CompareExchange(ref s_ScanBlocked, 0, 0);
        public static void LogScanResult()
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return;
            LogUtil.Log(string.Format(
                "[VPB ScanWhitelist] VaM scan filter: {0} allowed, {1} blocked by prefix patch",
                s_ScanAllowed, s_ScanBlocked));
        }

        public static bool IsVamRefreshInProgress => System.Threading.Interlocked.CompareExchange(ref s_VamRefreshInProgressCount, 0, 0) > 0;

        public static void MarkVamRefreshBegin()
        {
            System.Threading.Interlocked.Increment(ref s_VamRefreshInProgressCount);
        }

        public static void MarkVamRefreshEnd()
        {
            int next = System.Threading.Interlocked.Decrement(ref s_VamRefreshInProgressCount);
            if (next <= 0)
            {
                if (next < 0) System.Threading.Interlocked.Exchange(ref s_VamRefreshInProgressCount, 0);
                VamOnDemandLoader.NotifyVamRefreshCompleted();
            }
        }
    }
}
