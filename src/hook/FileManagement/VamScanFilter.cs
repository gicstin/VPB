using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Intercepts VaM's native FileManager to block non-whitelisted packages from being
    /// registered during VaM's startup scan. Uses a Harmony PREFIX on RegisterPackage so
    /// VaM never opens the .var zip or reads the manifest for excluded packages.
    /// On-demand registration (for plugin requests) is handled by VamOnDemandLoader.
    /// </summary>
    internal static class VamScanFilter
    {
        // VaM's method to register a single .var file by path — discovered via reflection.
        private static MethodInfo s_VamRegisterPackageMethod;

        private static bool s_Discovered = false;

        /// <summary>
        /// Must be called once during plugin startup (VamHookPlugin.Start).
        /// Discovers VaM's RegisterPackage method and logs the result.
        /// </summary>
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

        /// <summary>
        /// Registers a single .var file path in VaM's FileManager. Used by VamOnDemandLoader
        /// for on-demand loading of scan-excluded packages. Sets s_AllowRegistration so the
        /// PREFIX filter lets this call through.
        /// </summary>
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

        // VaM's FileManager exposes RegisterPackage even before its first Refresh has populated
        // internal AssetBundle/file dictionaries. Calling RegisterPackage too early throws
        // NullReferenceException inside VaM (e.g. AssetBundleManager.RemapVariantName).
        // Mark this true after the first MVR.FileManagement.FileManager.Refresh has run so
        // on-demand registration can avoid those doomed startup invocations.
        private static int s_HasVamRefreshedAtLeastOnce;

        public static bool HasVamRefreshedAtLeastOnce => s_HasVamRefreshedAtLeastOnce != 0;

        public static void MarkVamRefreshed()
        {
            int prev = System.Threading.Interlocked.Exchange(ref s_HasVamRefreshedAtLeastOnce, 1);
            if (prev == 0)
            {
                VamOnDemandLoader.NotifyVamFileManagerRefreshed();
            }
        }

        // Counters for the current scan cycle, reset at the start of each Refresh
        private static int s_ScanAllowed;
        private static int s_ScanBlocked;

        public static void ResetScanCounters() { s_ScanAllowed = 0; s_ScanBlocked = 0; }
        public static void RecordScanAllowed() { System.Threading.Interlocked.Increment(ref s_ScanAllowed); }
        public static void RecordScanBlocked() { System.Threading.Interlocked.Increment(ref s_ScanBlocked); }
        public static void LogScanResult()
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return;
            LogUtil.Log(string.Format(
                "[VPB ScanWhitelist] VaM scan filter: {0} allowed, {1} blocked by prefix patch",
                s_ScanAllowed, s_ScanBlocked));
        }
    }
}
