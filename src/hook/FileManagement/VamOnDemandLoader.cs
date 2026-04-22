using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Handles on-demand registration of scan-excluded packages in VaM's FileManager.
    /// When a MVRScript plugin or scene requests a package that was excluded from VaM's
    /// startup scan (because its folder is not whitelisted), this loader registers it
    /// in VaM's FileManager on demand so the request succeeds.
    ///
    /// Thread safety: registration must happen on the Unity main thread.
    /// Calls from background threads are queued and drained each frame via DrainMainThreadQueue().
    /// </summary>
    internal static class VamOnDemandLoader
    {
        // Re-entry guard: prevents infinite recursion when our postfix calls GetVarFileEntry
        [ThreadStatic]
        public static bool s_InOnDemand;

        // Set to true while VPB is deliberately calling VaM's RegisterPackage for on-demand
        // loading, so the PREFIX scan filter knows to allow it through.
        [ThreadStatic]
        public static bool s_AllowRegistration;

        // Set of UIDs we've already registered on-demand this session (avoid re-registering)
        private static readonly HashSet<string> s_RegisteredOnDemand =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_RegisteredLock = new object();

        // Queue for off-main-thread registration requests
        private static readonly Queue<string> s_PendingPaths = new Queue<string>();
        private static readonly object s_QueueLock = new object();

        private const int MaxDrainPerFrame = 10;

        // Unity main thread ID, set during plugin initialization
        private static int s_MainThreadId = -1;

        public static void SetMainThread()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private static bool IsMainThread()
        {
            return s_MainThreadId < 0 || Thread.CurrentThread.ManagedThreadId == s_MainThreadId;
        }

        /// <summary>
        /// Clears the on-demand registration cache (call after a full VaM/VPB scan refresh).
        /// </summary>
        public static void ClearCache()
        {
            lock (s_RegisteredLock)
                s_RegisteredOnDemand.Clear();
        }

        /// <summary>
        /// Called from the Harmony postfix on MVR.FileManagement.FileManager.GetPackage.
        /// Attempts to register the requested package in VaM's FileManager if it exists
        /// in VPB's registry but was excluded from VaM's scan.
        /// Returns the VPB VarPackage path found, or null.
        /// </summary>
        public static string TryRegisterPackageOnDemand(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            if (!ScanWhitelistManager.Instance.IsEnabled) return null;
            if (!VamScanFilter.HasRegisterMethodAccess) return null;

            // Already registered this session?
            lock (s_RegisteredLock)
            {
                if (s_RegisteredOnDemand.Contains(uid)) return null;
            }

            if (!TryResolveVarPathForUid(uid, out string resolvedUid, out string varPath))
                return null;
            if (string.IsNullOrEmpty(varPath)) return null;

            // Check file exists
            if (!File.Exists(varPath)) return null;

            string normPath = NormalizePath(varPath);
            if (normPath.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase))
            {
                // Ensure prefix whitelist patch allows this package registration.
                if (!ScanWhitelistManager.Instance.IsPathWhitelisted(normPath)
                    && !ScanWhitelistManager.Instance.IsUidOverrideIncluded(resolvedUid))
                {
                    var added = ScanWhitelistManager.Instance.AddTemporaryUidOverrides(new[] { resolvedUid });
                    if (added != null && added.Count > 0)
                    {
                        LogUtil.Log("[VPB OnDemand] Temporary allow-list +"
                            + string.Join(", ", added.ToArray()) + " for runtime request '" + uid + "'");
                    }
                }
            }

            LogUtil.Log("[VPB OnDemand] Registering package on demand: req=" + uid
                + " resolved=" + resolvedUid + " path=" + normPath);

            if (IsMainThread())
            {
                RegisterNow(resolvedUid, varPath);
            }
            else
            {
                lock (s_QueueLock)
                    s_PendingPaths.Enqueue(varPath);
                // Return null — caller will get null this frame, retry next frame
                return null;
            }

            return normPath;
        }

        /// <summary>
        /// For entry paths like "Author.Pkg.latest:/Custom/...", resolves ".latest" to the
        /// concrete installed UID and returns a rewritten path. Optionally triggers on-demand
        /// registration for the resolved UID first.
        /// </summary>
        public static string TryRewriteLatestEntryPath(string entryPath, bool attemptRegister)
        {
            if (string.IsNullOrEmpty(entryPath)) return null;
            int colonIdx = entryPath.IndexOf(':');
            if (colonIdx <= 0) return null;

            string uid = entryPath.Substring(0, colonIdx);
            if (!uid.EndsWith(".latest", StringComparison.OrdinalIgnoreCase)) return null;

            string resolvedUid = ResolveLatestUid(uid);
            if (string.IsNullOrEmpty(resolvedUid)) return null;
            if (string.Equals(resolvedUid, uid, StringComparison.OrdinalIgnoreCase)) return null;

            if (attemptRegister)
                TryRegisterPackageOnDemand(resolvedUid);

            return resolvedUid + entryPath.Substring(colonIdx);
        }

        private static void RegisterNow(string uid, string varPath)
        {
            bool ok = VamScanFilter.TryRegisterVarInVam(varPath);
            if (ok)
            {
                lock (s_RegisteredLock)
                    s_RegisteredOnDemand.Add(uid);
            }
        }

        private static bool TryResolveVarPathForUid(string requestUid, out string resolvedUid, out string varPath)
        {
            resolvedUid = null;
            varPath = null;
            if (string.IsNullOrEmpty(requestUid)) return false;

            // 1) Fast path: VPB live registry (works for already-indexed packages, including ".latest")
            try
            {
                VarPackage vpbPkg = FileManager.GetPackage(requestUid, ensureInstalled: false);
                if (vpbPkg != null && !string.IsNullOrEmpty(vpbPkg.Path))
                {
                    resolvedUid = !string.IsNullOrEmpty(vpbPkg.Uid) ? vpbPkg.Uid : UidFromVarPath(vpbPkg.Path);
                    varPath = NormalizePath(vpbPkg.Path);
                    if (!string.IsNullOrEmpty(resolvedUid) && !string.IsNullOrEmpty(varPath)) return true;
                }
            }
            catch { }

            string req = requestUid.Trim();
            string candidateUid = req;
            if (req.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
            {
                candidateUid = ResolveLatestUid(req);
                if (string.IsNullOrEmpty(candidateUid)) return false;
            }

            // 2) Fallback: resolve file directly from disk/cache using UID.
            string candidatePath = TryFindVarPathForUid(candidateUid);
            if (string.IsNullOrEmpty(candidatePath)) return false;

            resolvedUid = UidFromVarPath(candidatePath);
            if (string.IsNullOrEmpty(resolvedUid)) resolvedUid = candidateUid;
            varPath = NormalizePath(candidatePath);
            return !string.IsNullOrEmpty(varPath);
        }

        private static string ResolveLatestUid(string requestUid)
        {
            if (string.IsNullOrEmpty(requestUid)) return null;
            Match m = Regex.Match(requestUid, "^([^\\.]+\\.[^\\.]+)\\.latest$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string group = m.Groups[1].Value;
            if (string.IsNullOrEmpty(group)) return null;

            int bestVersion = -1;
            string bestUid = null;

            // Final fallback: scan filesystem for the newest installed version.
            bestVersion = -1;
            bestUid = null;
            foreach (string root in new[] { "AddonPackages", "AllPackages" })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string file in Directory.GetFiles(root, "*.var", SearchOption.AllDirectories))
                    {
                        string uid = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrEmpty(uid)) continue;
                        if (!uid.StartsWith(group + ".", StringComparison.OrdinalIgnoreCase)) continue;
                        int lastDot = uid.LastIndexOf('.');
                        if (lastDot <= 0 || lastDot >= uid.Length - 1) continue;
                        if (!int.TryParse(uid.Substring(lastDot + 1), out int v)) continue;
                        if (v > bestVersion)
                        {
                            bestVersion = v;
                            bestUid = uid;
                        }
                    }
                }
                catch { }
            }
            return bestUid;
        }

        private static string TryFindVarPathForUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;

            string filename = uid + ".var";
            string addon = NormalizePath(Path.Combine("AddonPackages", filename));
            if (File.Exists(addon)) return addon;
            string all = NormalizePath(Path.Combine("AllPackages", filename));
            if (File.Exists(all)) return all;

            foreach (string root in new[] { "AddonPackages", "AllPackages" })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    string[] matches = Directory.GetFiles(root, filename, SearchOption.AllDirectories);
                    if (matches != null && matches.Length > 0)
                        return NormalizePath(matches[0]);
                }
                catch { }
            }

            return null;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string p = path.Replace('\\', '/');
            if (Path.IsPathRooted(path))
            {
                try
                {
                    string cwd = Directory.GetCurrentDirectory().Replace('\\', '/').TrimEnd('/');
                    if (p.StartsWith(cwd + "/", StringComparison.OrdinalIgnoreCase))
                        p = p.Substring(cwd.Length + 1);
                }
                catch { }
            }
            return p;
        }

        /// <summary>
        /// Called from VamHookPlugin.Update() on the main thread. Drains the pending
        /// registration queue, max MaxDrainPerFrame entries per frame to avoid hitches.
        /// </summary>
        public static void DrainMainThreadQueue()
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return;

            int drained = 0;
            while (drained < MaxDrainPerFrame)
            {
                string path;
                lock (s_QueueLock)
                {
                    if (s_PendingPaths.Count == 0) break;
                    path = s_PendingPaths.Dequeue();
                }

                if (!string.IsNullOrEmpty(path))
                {
                    // Derive UID from path
                    string uid = UidFromVarPath(path);
                    if (!string.IsNullOrEmpty(uid))
                        RegisterNow(uid, path);
                }
                drained++;
            }
        }

        /// <summary>
        /// Extracts a package UID from a .var file path.
        /// e.g. "AddonPackages/Creator/Author.Package.1.var" → "Author.Package.1"
        /// </summary>
        public static string UidFromVarPath(string varPath)
        {
            if (string.IsNullOrEmpty(varPath)) return null;
            string filename = Path.GetFileNameWithoutExtension(varPath);
            return filename;
        }

        /// <summary>
        /// Extracts a package UID from a VaM file entry path.
        /// e.g. "Author.Package.1:/Custom/Hair/whatever.vam" → "Author.Package.1"
        /// </summary>
        public static string UidFromEntryPath(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return null;
            int colonIdx = entryPath.IndexOf(':');
            if (colonIdx > 0)
                return entryPath.Substring(0, colonIdx);
            return null;
        }
    }
}
