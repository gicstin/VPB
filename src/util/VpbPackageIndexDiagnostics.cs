using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VPB
{
    /// <summary>
    /// Optional tracing for VAR packages that fail to appear in the gallery index.
    /// Enable via BepInEx config <c>Logging.IndexDiagUidSubstring</c> (e.g. <c>RunRudolf.AlternativeFuta</c>).
    /// </summary>
    internal static class VpbPackageIndexDiagnostics
    {
        const string LogTag = "[VPB.IndexDiag]";

        internal static bool ShouldTrace(string uidOrPath)
        {
            if (string.IsNullOrEmpty(uidOrPath)) return false;
            string needle = GetTraceSubstring();
            if (string.IsNullOrEmpty(needle)) return false;
            return uidOrPath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string GetTraceSubstring()
        {
            try
            {
                if (Settings.Instance != null && Settings.Instance.IndexDiagUidSubstring != null)
                    return Settings.Instance.IndexDiagUidSubstring.Value ?? "";
            }
            catch { }
            return "";
        }

        internal static void Log(string uidOrPath, string phase, string detail)
        {
            if (!ShouldTrace(uidOrPath)) return;
            try
            {
                LogUtil.Log(LogTag + " " + phase + " uid/path='" + uidOrPath + "' | " + (detail ?? ""));
            }
            catch { }
        }

        internal static void LogPathEvent(string path, string phase, string detail)
        {
            if (string.IsNullOrEmpty(path)) return;
            string uid = TryUidFromVarPath(path);
            if (!ShouldTrace(path) && (uid == null || !ShouldTrace(uid))) return;
            try
            {
                LogUtil.Log(LogTag + " " + phase + " path='" + path + "'"
                    + (string.IsNullOrEmpty(uid) ? "" : (" uid='" + uid + "'"))
                    + " | " + (detail ?? ""));
            }
            catch { }
        }

        static string TryUidFromVarPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string fn = Path.GetFileName(path.TrimEnd('/', '\\'));
                if (string.IsNullOrEmpty(fn)) return null;
                if (fn.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    return fn.Substring(0, fn.Length - 4);
                if (fn.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return fn.Substring(0, fn.Length - 4);
            }
            catch { }
            return null;
        }

        /// <summary>After refresh / rebuild: report disk vs registry vs SQLite for traced families.</summary>
        internal static void AuditTracedPackages(string reason)
        {
            string needle = GetTraceSubstring();
            if (string.IsNullOrEmpty(needle)) return;

            try
            {
                var diskPaths = FindVarPathsOnDiskMatching(needle);
                var sb = new StringBuilder(512);
                sb.Append("audit reason=").Append(reason ?? "?");
                sb.Append(" needle='").Append(needle).Append('\'');
                sb.Append(" disk=").Append(diskPaths.Count);

                foreach (string path in diskPaths)
                {
                    string uid = TryUidFromVarPath(path) ?? "";
                    sb.Append("\n  disk path='").Append(path).Append('\'');

                    VarPackage live = null;
                    try { live = FileManager.GetPackage(uid, ensureInstalled: false); } catch { }
                    if (live != null)
                    {
                        sb.Append(" registry=YES path='").Append(live.Path ?? "").Append('\'');
                        sb.Append(" invalid=").Append(live.invalid ? "1" : "0");
                        sb.Append(" scaned=").Append(live.Scaned ? "1" : "0");
                        sb.Append(" enabled=").Append(live.Enabled ? "1" : "0");
                        List<string> names; List<long> t; List<long> s;
                        bool cache = live.TryGetCachedFileEntryData(out names, out t, out s) && names != null;
                        sb.Append(" zipCache=").Append(cache ? names.Count.ToString() : "0");
                    }
                    else
                    {
                        sb.Append(" registry=NO");
                        try
                        {
                            VarPackage reg = FileManager.GetPackage(path, ensureInstalled: false);
                            if (reg != null)
                                sb.Append(" pathResolve=YES uid='").Append(reg.Uid ?? "").Append('\'');
                        }
                        catch { }
                    }

                    string sqlPath = null;
                    try
                    {
                        if (VpbLocalDatabase.TryResolveIndexedVarPathForUid(uid, out sqlPath) && !string.IsNullOrEmpty(sqlPath))
                            sb.Append(" sqlPkg=YES var_path='").Append(sqlPath).Append('\'');
                        else
                            sb.Append(" sqlPkg=NO");
                    }
                    catch { sb.Append(" sqlPkg=ERR"); }

                    try
                    {
                        SerializableVarPackage man = VarPackageMgr.singleton.TryGetCache(uid);
                        if (man != null)
                            sb.Append(" manifestInvalid=").Append(man.IsInvalid ? "1" : "0");
                    }
                    catch { }

                    if (live == null && File.Exists(path))
                    {
                        try
                        {
                            VarPackage repaired = FileManager.RegisterHubDownloadedPackage(path);
                            sb.Append(" repairRegister=").Append(repaired != null ? ("OK uid='" + (repaired.Uid ?? "") + "'") : "FAIL");
                        }
                        catch (Exception rex)
                        {
                            sb.Append(" repairRegister=EX:").Append(rex.Message);
                        }
                    }
                }

                // Registry entries matching needle but missing on expected disk paths
                try
                {
                    lock (FileManager.packagesLock)
                    {
                        if (FileManager.PackagesByUid != null)
                        {
                            foreach (KeyValuePair<string, VarPackage> kv in FileManager.PackagesByUid)
                            {
                                if (!ShouldTrace(kv.Key)) continue;
                                bool onDisk = diskPaths.Exists(p =>
                                    string.Equals(p, kv.Value?.Path, StringComparison.OrdinalIgnoreCase));
                                if (!onDisk)
                                {
                                    sb.Append("\n  registryOnly uid='").Append(kv.Key).Append("' path='")
                                        .Append(kv.Value?.Path ?? "").Append('\'');
                                }
                            }
                        }
                    }
                }
                catch { }

                LogUtil.Log(LogTag + " " + sb.ToString());
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning(LogTag + " audit failed: " + ex.Message); } catch { }
            }
        }

        static List<string> FindVarPathsOnDiskMatching(string needle)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(needle)) return results;
            foreach (string root in new[] { "AddonPackages", "AllPackages" })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string file in Directory.GetFiles(root, "*.var", SearchOption.AllDirectories))
                    {
                        if (file.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                            results.Add(file.Replace('\\', '/'));
                    }
                }
                catch { }
            }
            return results;
        }
    }
}
