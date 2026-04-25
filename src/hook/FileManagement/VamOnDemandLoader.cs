using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
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
        // Failed registration cache to avoid repeatedly hammering the same package UID during startup/plugin bootstrap.
        private static readonly Dictionary<string, long> s_LastFailedAttemptTicksByUid =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_FailedLock = new object();
        private const long FailedRetryCooldownMs = 30000; // 30s
        private static readonly HashSet<string> s_StartupDeferredScriptUidsLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_StartupDeferredLock = new object();
        private static long s_StartupDeferredScriptCount;
        private static long s_StartupDeferredNonScriptCount;
        private static long s_StartupAllowedScriptCount;
        private static readonly HashSet<string> s_StartupDeferredAnyUidsLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Script/plugin paths must be registered synchronously when VaM asks for them.
        // VaM treats a false existence check as a failed plugin load and does not retry later.
        private static readonly HashSet<string> s_StartupDeferredScriptUids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
            };

        // Startup diagnostics: quantify how much time on-demand registration consumes.
        private static long s_StartupAttemptCount;
        private static long s_StartupSuccessCount;
        private static long s_StartupFailCount;
        private static long s_StartupSkippedRecentFailCount;
        private static long s_StartupAttemptTotalMs;
        private static bool s_StartupSummaryLogged;
        private static bool s_StartupFinalSummaryLogged;
        private static readonly object s_StartupStatsLock = new object();
        private static readonly Dictionary<string, int> s_StartupAttemptsByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> s_StartupFailsByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

        private static void SafeRecordStartupOnDemandActivity()
        {
            try
            {
                MethodInfo m = typeof(LogUtil).GetMethod("RecordStartupOnDemandActivity",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (m != null) m.Invoke(null, null);
            }
            catch { }
        }

        private static bool SafeIsStartupReadyLogged()
        {
            try
            {
                MethodInfo m = typeof(LogUtil).GetMethod("IsStartupReadyLogged",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (m != null)
                {
                    object r = m.Invoke(null, null);
                    if (r is bool b) return b;
                }
            }
            catch { }
            return false;
        }

        private static bool SafeIsStartupPresetBootstrapActive()
        {
            try
            {
                MethodInfo m = typeof(LogUtil).GetMethod("IsStartupPresetBootstrapActive",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (m != null)
                {
                    object r = m.Invoke(null, null);
                    if (r is bool b) return b;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Clears the on-demand registration cache (call after a full VaM/VPB scan refresh).
        /// </summary>
        public static void ClearCache()
        {
            lock (s_RegisteredLock)
                s_RegisteredOnDemand.Clear();
            lock (s_FailedLock)
                s_LastFailedAttemptTicksByUid.Clear();
            lock (s_StartupStatsLock)
            {
                s_StartupAttemptCount = 0;
                s_StartupSuccessCount = 0;
                s_StartupFailCount = 0;
                s_StartupSkippedRecentFailCount = 0;
                s_StartupAttemptTotalMs = 0;
                s_StartupSummaryLogged = false;
                s_StartupFinalSummaryLogged = false;
                s_StartupAttemptsByUid.Clear();
                s_StartupFailsByUid.Clear();
                s_StartupDeferredScriptCount = 0;
                s_StartupDeferredNonScriptCount = 0;
                s_StartupAllowedScriptCount = 0;
            }
            lock (s_StartupDeferredLock)
            {
                s_StartupDeferredScriptUidsLogged.Clear();
                s_StartupDeferredAnyUidsLogged.Clear();
            }
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

            // Cooldown repeated failures per UID to prevent startup stalls from repeated reflection/invoke exceptions.
            if (WasRecentFailure(uid))
            {
                lock (s_StartupStatsLock)
                    s_StartupSkippedRecentFailCount++;
                return null;
            }

            if (!TryResolveVarPathForUid(uid, out string resolvedUid, out string varPath))
                return null;
            if (string.IsNullOrEmpty(varPath)) return null;

            lock (s_RegisteredLock)
            {
                if (!string.IsNullOrEmpty(resolvedUid) && s_RegisteredOnDemand.Contains(resolvedUid)) return null;
            }
            if (!string.IsNullOrEmpty(resolvedUid) && WasRecentFailure(resolvedUid)) return null;

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
            SafeRecordStartupOnDemandActivity();

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

        public static bool ShouldDeferStartupOnDemandForPath(string entryPath, string uid)
        {
            bool startupReady = SafeIsStartupReadyLogged();
            bool presetBootstrapActive = SafeIsStartupPresetBootstrapActive();
            if (startupReady && !presetBootstrapActive) return false;
            if (string.IsNullOrEmpty(entryPath)) return false;
            string p = entryPath.Replace('\\', '/');
            // Balanced mode:
            // - allow startup on-demand for script/plugin paths so plugin init remains functional
            // - defer non-script on-demand requests until READY
            // During preset bootstrap we keep startup policy active a bit longer because
            // heavy script controllers can still create long main-thread stalls post-READY.
            bool isScriptPath = p.IndexOf(":/Custom/Scripts/", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isScriptPath)
            {
                if (!string.IsNullOrEmpty(uid) && s_StartupDeferredScriptUids.Contains(uid))
                {
                    lock (s_StartupStatsLock) s_StartupDeferredScriptCount++;
                    lock (s_StartupDeferredLock)
                    {
                        if (s_StartupDeferredScriptUidsLogged.Add(uid))
                            LogUtil.Log("[VPB OnDemand] Startup defer heavy script package: " + uid + " entry=" + p);
                    }
                    return true;
                }
                lock (s_StartupStatsLock) s_StartupAllowedScriptCount++;
                return false;
            }

            lock (s_StartupStatsLock) s_StartupDeferredNonScriptCount++;
            if (!string.IsNullOrEmpty(uid))
            {
                lock (s_StartupDeferredLock)
                {
                    if (s_StartupDeferredAnyUidsLogged.Add(uid))
                        LogUtil.Log("[VPB OnDemand] Startup defer non-script package: " + uid + " entry=" + p);
                }
            }
            return true;
        }

        private static void RegisterNow(string uid, string varPath)
        {
            var sw = Stopwatch.StartNew();
            bool ok = VamScanFilter.TryRegisterVarInVam(varPath);
            sw.Stop();
            long elapsedMs = sw.ElapsedMilliseconds;
            lock (s_StartupStatsLock)
            {
                s_StartupAttemptCount++;
                s_StartupAttemptTotalMs += elapsedMs;
                int a = 0;
                s_StartupAttemptsByUid.TryGetValue(uid, out a);
                s_StartupAttemptsByUid[uid] = a + 1;
                if (ok) s_StartupSuccessCount++;
                else
                {
                    s_StartupFailCount++;
                    int f = 0;
                    s_StartupFailsByUid.TryGetValue(uid, out f);
                    s_StartupFailsByUid[uid] = f + 1;
                }
            }

            if (ok)
            {
                lock (s_RegisteredLock)
                    s_RegisteredOnDemand.Add(uid);
                lock (s_FailedLock)
                    s_LastFailedAttemptTicksByUid.Remove(uid);
            }
            else
            {
                MarkFailure(uid);
            }
        }

        private static bool WasRecentFailure(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastTicks;
            lock (s_FailedLock)
            {
                if (!s_LastFailedAttemptTicksByUid.TryGetValue(uid, out lastTicks))
                    return false;
            }
            long dtMs = (nowTicks - lastTicks) / TimeSpan.TicksPerMillisecond;
            return dtMs >= 0 && dtMs < FailedRetryCooldownMs;
        }

        private static void MarkFailure(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (s_FailedLock)
            {
                s_LastFailedAttemptTicksByUid[uid] = DateTime.UtcNow.Ticks;
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

            // Prefer the local package index when available.
            try
            {
                if (VpbLocalDatabase.TryResolveLatestUidFromIndex(group, out string latestFromSql) && !string.IsNullOrEmpty(latestFromSql))
                    return latestFromSql;
            }
            catch { }

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

            // Prefer indexed UID->path lookup when available.
            try
            {
                if (VpbLocalDatabase.TryResolveIndexedVarPathForUid(uid, out string sqlPath) && !string.IsNullOrEmpty(sqlPath))
                    return NormalizePath(sqlPath);
            }
            catch { }

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
            MaybeLogStartupSummary();

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

        private static void MaybeLogStartupSummary()
        {
            bool ready = SafeIsStartupReadyLogged();
            if (!ready && s_StartupSummaryLogged) return;
            if (ready && s_StartupFinalSummaryLogged) return;
            if (!ready && LogUtil.GetStartupSecondsForDisplay() < 12.0) return;

            long a, s, f, sk, ms, ds, dn, ascr;
            string topFail = "";
            lock (s_StartupStatsLock)
            {
                if (!ready && s_StartupSummaryLogged) return;
                if (ready && s_StartupFinalSummaryLogged) return;
                a = s_StartupAttemptCount;
                s = s_StartupSuccessCount;
                f = s_StartupFailCount;
                sk = s_StartupSkippedRecentFailCount;
                ms = s_StartupAttemptTotalMs;
                ds = s_StartupDeferredScriptCount;
                dn = s_StartupDeferredNonScriptCount;
                ascr = s_StartupAllowedScriptCount;
                int shown = 0;
                foreach (var kv in System.Linq.Enumerable.OrderByDescending(s_StartupFailsByUid, x => x.Value))
                {
                    if (shown >= 5) break;
                    if (shown > 0) topFail += ";";
                    topFail += kv.Key + ":" + kv.Value;
                    shown++;
                }
                if (!ready) s_StartupSummaryLogged = true;
                else s_StartupFinalSummaryLogged = true;
            }

            LogUtil.Log("[VPB OnDemand][Startup" + (ready ? ":final" : ":checkpoint") + "] attempts=" + a
                + " success=" + s
                + " fail=" + f
                + " skipped_recent_fail=" + sk
                + " deferred_non_script=" + dn
                + " allowed_script=" + ascr
                + " deferred_script=" + ds
                + " invoke_ms_total=" + ms
                + " cooldown_ms=" + FailedRetryCooldownMs
                + " top_fail_uids=" + (string.IsNullOrEmpty(topFail) ? "(none)" : topFail));
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
            string p = entryPath.Replace('\\', '/');
            int colonIdx = p.IndexOf(":/");
            if (colonIdx > 0)
            {
                // Do not treat absolute Windows paths (E:/...) as package UIDs.
                if (colonIdx == 1 && char.IsLetter(p[0])) return null;
                return p.Substring(0, colonIdx);
            }
            return null;
        }
    }
}
