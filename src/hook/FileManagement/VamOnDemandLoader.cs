using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using UnityEngine;
using System.Linq;

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
        private static bool IsPluginEntryPath(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return false;
            string p = entryPath.Replace('\\', '/');
            return p.IndexOf(":/Custom/Scripts/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParseUidGroupAndVersion(string uid, out string group, out int version)
        {
            group = null;
            version = -1;
            if (string.IsNullOrEmpty(uid)) return false;

            // UID format: "Author.Package.14" (version is final dot-segment)
            int lastDot = uid.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= uid.Length - 1) return false;

            string vStr = uid.Substring(lastDot + 1);
            if (!int.TryParse(vStr, out version)) return false;

            group = uid.Substring(0, lastDot);
            return !string.IsNullOrEmpty(group) && version >= 0;
        }

        private static string ResolveBestAvailableUid(string requestUid)
        {
            return ResolveBestAvailableUid(requestUid, null);
        }

        /// <summary>
        /// Resolve an alternate UID when the requested versioned UID should be rewritten.
        /// Keeps exact UID when that package is installed (native VaM behavior).
        /// When exact is missing, applies meta ReferenceVersionOption / user settings.
        /// </summary>
        private static string ResolveBestAvailableUid(string requestUid, string entryPath)
        {
            if (string.IsNullOrEmpty(requestUid)) return null;

            // Explicit ".latest" already handled by existing logic.
            if (requestUid.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
                return ResolveLatestUid(requestUid);

            if (!TryParseUidGroupAndVersion(requestUid, out string group, out int requestedVer))
                return null;

            // Force-latest list: upgrade even when exact exists.
            if (FileManager.ShouldForceLatestForPackageGroup(group))
            {
                string forcedLatest = ResolveLatestUid(group + ".latest");
                if (!string.IsNullOrEmpty(forcedLatest)
                    && !string.Equals(forcedLatest, requestUid, StringComparison.OrdinalIgnoreCase))
                    return forcedLatest;
                return null;
            }

            // Exact version present → never rewrite (matches native NormalizeCommon).
            if (IsExactUidAvailable(requestUid))
                return null;

            VarPackage.ReferenceVersionOption option =
                PackageReferenceVersionResolver.GetEffectiveOption(entryPath);
            return PackageReferenceVersionResolver.ResolveMissingVersionUid(group, requestedVer, option);
        }

        private static bool IsExactUidAvailable(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            try
            {
                VarPackage pkg = FileManager.GetPackage(uid, ensureInstalled: false);
                if (pkg != null) return true;
            }
            catch { }

            // Cheap existence only — no recursive Directory.GetFiles (warm NormalizeLoadPath).
            try
            {
                if (VpbLocalDatabase.TryResolveIndexedVarPathForUid(uid, out string sqlPath)
                    && !string.IsNullOrEmpty(sqlPath))
                    return true;
            }
            catch { }

            try
            {
                string filename = uid + ".var";
                if (File.Exists(Path.Combine("AddonPackages", filename))) return true;
                if (File.Exists(Path.Combine("AllPackages", filename))) return true;
            }
            catch { }

            return IsUidAlreadyRegisteredInVam(uid);
        }

        private static MethodInfo s_VamGetPackageMethod;
        private static bool s_VamGetPackageMethodResolved;

        private static bool IsUidAlreadyRegisteredInVam(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            try
            {
                if (!s_VamGetPackageMethodResolved)
                {
                    s_VamGetPackageMethodResolved = true;
                    var fmType = typeof(MVR.FileManagement.FileManager);
                    s_VamGetPackageMethod = fmType.GetMethod("GetPackage",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                        null, new[] { typeof(string) }, null);
                }
                if (s_VamGetPackageMethod == null) return false;
                object r = s_VamGetPackageMethod.Invoke(null, new object[] { uid });
                return r != null;
            }
            catch { }
            return false;
        }

        private static readonly HashSet<string> s_RewriteLogOnceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_RewriteLogLock = new object();
        private const int PathRewriteProbeLogMax = 10;
        private static int s_PathRewriteProbeLogged;
        private static int s_PathRewriteProbeSilenced;
        private static bool s_PathRewriteProbeSummaryLogged;
        private static int s_CatalogMetaJsonProbeSuppressed;
        private static bool s_CatalogMetaJsonProbeNoticeLogged;

        private static void LogRewriteOnce(string key, string message)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(message)) return;
            lock (s_RewriteLogLock)
            {
                if (!s_RewriteLogOnceKeys.Add(key)) return;
            }
            LogUtil.Log(message);
        }

        /// <summary>
        /// PluginAssist (and similar) probe meta.json using filesystem paths as fake UIDs
        /// (AddonPackages/Foo.1.var:/meta.json). VPB manifest lookup cannot register these in VaM.
        /// </summary>
        private static bool IsCatalogMetaJsonFilesystemProbe(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return false;
            string p = entryPath.Replace('\\', '/');
            int colonIdx = p.IndexOf(":/", StringComparison.Ordinal);
            if (colonIdx <= 0 || colonIdx + 2 >= p.Length) return false;
            string uid = p.Substring(0, colonIdx);
            string internalPath = p.Substring(colonIdx + 2);
            if (internalPath.StartsWith("/")) internalPath = internalPath.Substring(1);
            if (!IsRawVarFilesystemPath(uid)) return false;
            return string.Equals(internalPath, "meta.json", StringComparison.OrdinalIgnoreCase);
        }

        private static void SuppressCatalogMetaJsonProbe()
        {
            int n = Interlocked.Increment(ref s_CatalogMetaJsonProbeSuppressed);
            if (n != 1 || s_CatalogMetaJsonProbeNoticeLogged) return;
            s_CatalogMetaJsonProbeNoticeLogged = true;
            LogUtil.Log("[VPB OnDemand] PluginAssist catalog meta.json probes detected — suppressing path rewrite logs");
        }

        /// <summary>
        /// Caps noisy path rewrite probe logs (PluginAssist catalog scans, identity rewrites).
        /// </summary>
        private static void LogPathRewriteProbeLimited(string reqPath, string rewrittenPath, string detailMessage)
        {
            if (string.IsNullOrEmpty(detailMessage)) return;
            if (!string.IsNullOrEmpty(reqPath) && !string.IsNullOrEmpty(rewrittenPath)
                && string.Equals(reqPath.Replace('\\', '/'), rewrittenPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref s_PathRewriteProbeSilenced);
                return;
            }

            lock (s_RewriteLogLock)
            {
                if (s_PathRewriteProbeLogged < PathRewriteProbeLogMax)
                {
                    s_PathRewriteProbeLogged++;
                    LogUtil.Log(detailMessage);
                    return;
                }
                s_PathRewriteProbeSilenced++;
                if (s_PathRewriteProbeSummaryLogged) return;
                s_PathRewriteProbeSummaryLogged = true;
                LogUtil.Log("[VPB OnDemand] Silenced further path rewrite probe logs (first "
                    + PathRewriteProbeLogMax + " shown; "
                    + s_PathRewriteProbeSilenced + " additional probe(s) skipped)");
            }
        }

        private static void AppendPathRewriteProbeSummaryIfNeeded(StringBuilder sb)
        {
            if (sb == null) return;
            int catalog = Interlocked.CompareExchange(ref s_CatalogMetaJsonProbeSuppressed, 0, 0);
            int silenced = s_PathRewriteProbeSilenced;
            lock (s_RewriteLogLock) { silenced = s_PathRewriteProbeSilenced; }
            if (catalog <= 0 && silenced <= 0) return;
            sb.Append(" path_rewrite_catalog_probes=").Append(catalog);
            if (silenced > 0) sb.Append(" path_rewrite_probes_silenced=").Append(silenced);
        }

        private static string TryRewritePluginCslistPathByFilename(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return null;
            string p = entryPath.Replace('\\', '/');
            if (p.IndexOf(":/Custom/Scripts/", StringComparison.OrdinalIgnoreCase) < 0) return null;
            if (!p.EndsWith(".cslist", StringComparison.OrdinalIgnoreCase)) return null;

            int colonIdx = p.IndexOf(":/", StringComparison.Ordinal);
            if (colonIdx <= 0 || colonIdx + 2 >= p.Length) return null;

            string uid = p.Substring(0, colonIdx);
            string internalPath = p.Substring(colonIdx + 2);
            if (internalPath.StartsWith("/")) internalPath = internalPath.Substring(1);
            string filename = Path.GetFileName(internalPath);
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(filename)) return null;

            // If the exact entry exists, no rewrite needed.
            try
            {
                if (MVR.FileManagement.FileManager.GetVarFileEntry(p) != null) return null;
            }
            catch { }

            VarPackage pkg = null;
            try { pkg = FileManager.GetPackage(uid, ensureInstalled: false); } catch { pkg = null; }
            if (pkg == null) return null;

            // Use cached file list (fast) to locate the actual cslist path within the VAR.
            if (!pkg.TryGetCachedFileEntryData(out List<string> names, out _, out _)) return null;
            if (names == null || names.Count == 0) return null;

            string best = null;
            int matchCount = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                string nn = n.Replace('\\', '/');
                if (!nn.StartsWith("Custom/Scripts/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!nn.EndsWith("/" + filename, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(nn, "Custom/Scripts/" + filename, StringComparison.OrdinalIgnoreCase))
                    continue;
                matchCount++;
                // Prefer the shortest matching path (closest to root), tends to be the intended entry point.
                if (best == null || nn.Length < best.Length)
                    best = nn;
            }

            if (string.IsNullOrEmpty(best)) return null;

            string rewritten = uid + ":/" + best;
            LogRewriteOnce("cslistloc|" + uid + "|" + filename,
                "[VPB OnDemand] Rewrote missing plugin cslist by filename: req=" + p
                + " -> " + rewritten + " (matches=" + matchCount + ")");
            return rewritten;
        }

        // Cached once; Path.GetInvalidPathChars allocates a fresh array per call on Mono.
        private static readonly char[] s_InvalidPathChars = Path.GetInvalidPathChars();

        private static string TryRewriteMissingEntryPathWithinSamePackage(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return null;

            // Trigger action URLs in some VaM scenes contain trailing CR/LF (saved from
            // dirty clipboard data). Path.GetDirectoryName throws ArgumentException on those,
            // which would unwind out of MacGruber's per-state loop and drop every state after
            // the bad one. Bail to "no rewrite" so VaM's caller falls back to the original path.
            if (entryPath.IndexOfAny(s_InvalidPathChars) >= 0) return null;

            string p = entryPath.Replace('\\', '/');

            int colonIdx = p.IndexOf(":/", StringComparison.Ordinal);
            if (colonIdx <= 0 || colonIdx + 2 >= p.Length) return null;

            string uid = p.Substring(0, colonIdx);
            string internalPath = p.Substring(colonIdx + 2);
            if (internalPath.StartsWith("/")) internalPath = internalPath.Substring(1);
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(internalPath)) return null;

            if (IsCatalogMetaJsonFilesystemProbe(p))
            {
                SuppressCatalogMetaJsonProbe();
                return null;
            }

            // If exact entry exists, no rewrite needed.
            try
            {
                if (MVR.FileManagement.FileManager.GetVarFileEntry(p) != null) return null;
            }
            catch { }

            VarPackage pkg = null;
            try { pkg = FileManager.GetPackage(uid, ensureInstalled: false); } catch { pkg = null; }
            if (pkg == null) return null;

            if (!pkg.TryGetCachedFileEntryData(out List<string> names, out _, out _)) return null;
            if (names == null || names.Count == 0) return null;

            string reqNorm = internalPath.Replace('\\', '/');
            string reqDir = Path.GetDirectoryName(reqNorm)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(reqDir)) reqDir = "";
            string reqFile = Path.GetFileName(reqNorm);
            if (string.IsNullOrEmpty(reqFile)) return null;

            // 1) Case-insensitive full path match (fixes zip case-sensitivity issues).
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                string nn = n.Replace('\\', '/');
                if (string.Equals(nn, reqNorm, StringComparison.OrdinalIgnoreCase))
                {
                    string rewrittenExact = uid + ":/" + nn;
                    LogPathRewriteProbeLimited(p, rewrittenExact,
                        "[VPB OnDemand] Rewrote missing entry by case-insensitive path match: req=" + p + " -> " + rewrittenExact);
                    return rewrittenExact;
                }
            }

            // 2) Filename match within same package (case-insensitive), prefer closest directory match.
            string best = null;
            int bestScore = int.MinValue;
            int matchCount = 0;
            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                string nn = n.Replace('\\', '/');
                if (!string.Equals(Path.GetFileName(nn), reqFile, StringComparison.OrdinalIgnoreCase)) continue;

                matchCount++;

                string candDir = Path.GetDirectoryName(nn)?.Replace('\\', '/') ?? "";
                int score = 0;
                if (string.Equals(candDir, reqDir, StringComparison.OrdinalIgnoreCase)) score += 200;
                else if (!string.IsNullOrEmpty(reqDir) && candDir.EndsWith(reqDir, StringComparison.OrdinalIgnoreCase)) score += 120;
                // Prefer shallower paths when ambiguous (often the "main" file).
                score -= nn.Length;

                if (best == null || score > bestScore)
                {
                    best = nn;
                    bestScore = score;
                }
            }

            if (string.IsNullOrEmpty(best)) return null;

            string rewritten = uid + ":/" + best;
            LogPathRewriteProbeLimited(p, rewritten,
                "[VPB OnDemand] Rewrote missing entry by filename within same package: req=" + p
                + " -> " + rewritten + " (matches=" + matchCount + ")");
            return rewritten;
        }

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
        private static long s_StartupVamNotReadyDeferredCount;
        private static bool s_StartupSummaryLogged;
        private static bool s_StartupFinalSummaryLogged;
        private static readonly object s_StartupStatsLock = new object();
        private static readonly Dictionary<string, int> s_StartupAttemptsByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> s_StartupFailsByUid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Queue for off-main-thread registration requests
        private static readonly Queue<string> s_PendingPaths = new Queue<string>();
        private static readonly object s_QueueLock = new object();
        // Requests that arrive before VaM's first Refresh has completed.
        // These are promoted once MarkVamRefreshed() fires.
        private static readonly Queue<string> s_VamNotReadyDeferredPaths = new Queue<string>();
        private static readonly HashSet<string> s_VamNotReadyDeferredUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_VamNotReadyLock = new object();
        // Requests that arrive while VaM FileManager.Refresh is actively running.
        // RegisterPackage during this window can race with VaM dictionary enumeration.
        private static readonly Queue<string> s_RefreshInProgressDeferredPaths = new Queue<string>();
        private static readonly HashSet<string> s_RefreshInProgressDeferredUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_RefreshInProgressLock = new object();
        private static readonly object s_RefreshRequestLock = new object();
        private static bool s_PendingVamRefresh;
        private static float s_PendingVamRefreshRequestedAt;
        private static float s_PendingVamRefreshFirstRequestedAt;
        private static int s_PendingVamRefreshRequestCount;
        private static string s_PendingVamRefreshReason;
        // UIDs registered on-demand this session whose clothing/hair/morph catalogs may still be stale.
        private static readonly HashSet<string> s_CatalogStaleUids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_CatalogStaleLock = new object();

        private const int MaxDrainPerFrame = 10;
        private const float CoalescedVamRefreshDelayStartupSeconds = 1.0f;
        private const float CoalescedVamRefreshDelayReadySeconds = 0.25f;

        // Unity main thread ID, set during plugin initialization
        private static int s_MainThreadId = -1;

        public static void SetMainThread()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static bool IsMainThread()
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

        private static bool SafeIsReadyLogged()
        {
            try
            {
                MethodInfo m = typeof(LogUtil).GetMethod("IsReadyLogged",
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
            Interlocked.Exchange(ref s_StartupVamNotReadyDeferredCount, 0);
            lock (s_RefreshRequestLock)
            {
                s_PendingVamRefresh = false;
                s_PendingVamRefreshRequestedAt = 0f;
                s_PendingVamRefreshFirstRequestedAt = 0f;
                s_PendingVamRefreshRequestCount = 0;
                s_PendingVamRefreshReason = null;
            }
            lock (s_RefreshInProgressLock)
            {
                s_RefreshInProgressDeferredPaths.Clear();
                s_RefreshInProgressDeferredUids.Clear();
            }
            lock (s_CatalogStaleLock)
                s_CatalogStaleUids.Clear();
        }

        /// <summary>True when UID was on-demand registered this session and native catalogs may still be stale.</summary>
        public static bool IsPromotedPackageCatalogStale(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (s_CatalogStaleLock)
                return s_CatalogStaleUids.Contains(uid);
        }

        /// <summary>Called when native FileManager.Refresh completes — DAZ catalogs are fresh again.</summary>
        public static void NotifyNativeCatalogRefreshed()
        {
            lock (s_CatalogStaleLock)
                s_CatalogStaleUids.Clear();
        }

        /// <summary>
        /// Whether a coalesced native refresh is needed after on-demand registration for these UIDs.
        /// Skips refresh when every UID was already registered and no catalog is stale.
        /// </summary>
        public static bool ShouldRequestCoalescedNativeRefreshForUids(ICollection<string> uids, int newlyRegisteredCount)
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return false;
            if (newlyRegisteredCount <= 0) return false;
            if (uids == null || uids.Count == 0) return false;

            foreach (string uid in uids)
            {
                if (string.IsNullOrEmpty(uid)) continue;
                if (IsCatalogDependentUid(uid)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether native clothing/hair/morph catalogs need rebuilding after registering this package.
        /// </summary>
        public static bool PackageRegistrationNeedsNativeCatalogRefresh(string uid, string entryPath)
        {
            if (IsPluginEntryPath(entryPath)) return false;
            if (!string.IsNullOrEmpty(entryPath) && IsCatalogDependentEntryPath(entryPath)) return true;
            return IsCatalogDependentUid(uid);
        }

        static void MarkPromotedPackageCatalogStale(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            lock (s_CatalogStaleLock)
                s_CatalogStaleUids.Add(uid);
        }

        static bool IsCatalogDependentEntryPath(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return false;
            string p = entryPath.Replace('\\', '/');
            return p.IndexOf(":/Custom/Clothing/", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf(":/Custom/Hair/", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf(":/Custom/Atom/Person/Morphs/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsCatalogDependentUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            try
            {
                SerializableVarPackage cached = VarPackageMgr.singleton.TryGetCache(uid);
                if (cached != null && cached.FileEntryNames != null)
                {
                    List<string> names = cached.FileEntryNames;
                    for (int i = 0; i < names.Count; i++)
                    {
                        string internalPath = names[i];
                        if (string.IsNullOrEmpty(internalPath)) continue;
                        if (internalPath.StartsWith("Custom/Clothing/", StringComparison.OrdinalIgnoreCase)) return true;
                        if (internalPath.StartsWith("Custom/Hair/", StringComparison.OrdinalIgnoreCase)) return true;
                        if (internalPath.StartsWith("Custom/Atom/Person/Morphs/", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                }

                VarPackage pkg = FileManager.GetPackage(uid, false);
                if (pkg == null) return true;

                List<string> manifestNames;
                List<long> ticks;
                List<long> sizes;
                if (pkg.TryGetCachedFileEntryData(out manifestNames, out ticks, out sizes) && manifestNames != null)
                {
                    for (int i = 0; i < manifestNames.Count; i++)
                    {
                        string internalPath = manifestNames[i];
                        if (string.IsNullOrEmpty(internalPath)) continue;
                        if (internalPath.StartsWith("Custom/Clothing/", StringComparison.OrdinalIgnoreCase)) return true;
                        if (internalPath.StartsWith("Custom/Hair/", StringComparison.OrdinalIgnoreCase)) return true;
                        if (internalPath.StartsWith("Custom/Atom/Person/Morphs/", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    return false;
                }
            }
            catch { }
            return true;
        }

        /// <summary>
        /// True for bare filesystem paths like "AddonPackages/Creator.Pkg.1.var".
        /// These are catalog/index probes, not real package-entry requests — skip on-demand.
        /// </summary>
        internal static bool IsRawVarFilesystemPath(string request)
        {
            if (string.IsNullOrEmpty(request)) return false;
            string p = request.Replace('\\', '/').Trim();
            if (p.IndexOf(":/", StringComparison.Ordinal) >= 0) return false;
            if (!p.EndsWith(".var", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return false;
            return p.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Register a scan-excluded package in VaM's FileManager when a real runtime request
        /// needs it (entry-path hooks, preset deps, script load). Returns the .var path or null.
        /// </summary>
        public static string TryRegisterPackageOnDemand(string uid, bool persistUidOverride = false)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            if (!ScanWhitelistManager.Instance.IsEnabled) return null;
            if (!VamScanFilter.HasRegisterMethodAccess) return null;
            if (!persistUidOverride && IsRawVarFilesystemPath(uid)) return null;

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
            {
                // Genuinely unresolvable: arm the failure cooldown so repeated probes for the same uid
                // short-circuit instead of re-running the recursive AddonPackages walk on every hook call.
                MarkFailure(uid);
                return null;
            }
            if (string.IsNullOrEmpty(varPath)) return null;

            string deferUidCheck = !string.IsNullOrEmpty(resolvedUid) ? resolvedUid : uid;
            string deferPathCheck = deferUidCheck + ":/";
            if (ShouldDeferStartupOnDemandForPath(deferPathCheck, deferUidCheck))
            {
                lock (s_VamNotReadyLock)
                {
                    if (s_VamNotReadyDeferredUids.Add(deferUidCheck))
                        s_VamNotReadyDeferredPaths.Enqueue(varPath);
                }
                return null;
            }

            lock (s_RegisteredLock)
            {
                if (!string.IsNullOrEmpty(resolvedUid) && s_RegisteredOnDemand.Contains(resolvedUid)) return null;
            }
            if (!string.IsNullOrEmpty(resolvedUid) && WasRecentFailure(resolvedUid)) return null;

            // Check file exists
            if (!File.Exists(varPath)) return null;

            string normPath = NormalizePath(varPath);
            if (normPath.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)
                && ScanWhitelistManager.Instance.IsEnabled)
            {
                // Legacy block-at-register: temporarily allow so RegisterPackage can run.
                if (!ScanWhitelistManager.Instance.IsPathWhitelisted(normPath)
                    && !ScanWhitelistManager.Instance.IsUidOverrideIncluded(resolvedUid))
                {
                    if (persistUidOverride && !string.IsNullOrEmpty(resolvedUid))
                    {
                        try
                        {
                            if (ScanWhitelistManager.Instance.AddUidOverride(resolvedUid))
                            {
                                ScanWhitelistManager.Instance.Save();
                                LogUtil.Log("[VPB OnDemand] Persisted plugin whitelist UID override: +" + resolvedUid);
                            }
                        }
                        catch { }
                    }

                    var added = ScanWhitelistManager.Instance.AddTemporaryUidOverrides(new[] { resolvedUid });
                    if (added != null && added.Count > 0)
                    {
                        LogUtil.Log("[VPB OnDemand] Temporary allow-list +"
                            + string.Join(", ", added.ToArray()) + " for runtime request '" + uid + "'");
                    }
                }
            }

            // If VaM already has this UID registered, skip duplicate register.
            if (!string.IsNullOrEmpty(resolvedUid) && IsUidAlreadyRegisteredInVam(resolvedUid))
            {
                lock (s_RegisteredLock)
                    s_RegisteredOnDemand.Add(resolvedUid);
                return null;
            }

            // VaM can throw NREs in RegisterPackage before its first Refresh finishes
            // initializing internal managers. Defer these on-demand requests and replay
            // them once VamScanFilter.MarkVamRefreshed() signals readiness.
            if (!VamScanFilter.HasVamRefreshedAtLeastOnce && !SafeIsStartupReadyLogged())
            {
                bool added;
                string deferUid = !string.IsNullOrEmpty(resolvedUid) ? resolvedUid : uid;
                lock (s_VamNotReadyLock)
                {
                    added = s_VamNotReadyDeferredUids.Add(deferUid);
                    if (added)
                        s_VamNotReadyDeferredPaths.Enqueue(varPath);
                }
                if (added)
                    Interlocked.Increment(ref s_StartupVamNotReadyDeferredCount);
                return null;
            }

            // VaM can enumerate package dictionaries during Refresh. Registering during this
            // window can trigger "InvalidOperationException: out of sync" in VaM.
            if (VamScanFilter.IsVamRefreshInProgress)
            {
                string deferUid = !string.IsNullOrEmpty(resolvedUid) ? resolvedUid : uid;
                bool added;
                lock (s_RefreshInProgressLock)
                {
                    added = s_RefreshInProgressDeferredUids.Add(deferUid);
                    if (added)
                        s_RefreshInProgressDeferredPaths.Enqueue(varPath);
                }
                return null;
            }

            try { VamStartupOptimizations.InvalidateVamXAbsentCacheIfVamXPackageTouched(resolvedUid ?? uid); } catch { }

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

        /// <summary>
        /// For entry paths like "Author.Pkg.12:/Custom/...", rewrites only when the request
        /// version is not installed (or ForceLatest / Latest policy applies). Exact pins stay
        /// when that version exists on disk.
        /// </summary>
        public static string TryRewriteBestAvailableEntryPath(string entryPath, bool attemptRegister)
        {
            if (string.IsNullOrEmpty(entryPath)) return null;
            int colonIdx = entryPath.IndexOf(':');
            if (colonIdx <= 0) return null;

            string uid = entryPath.Substring(0, colonIdx);
            if (string.IsNullOrEmpty(uid)) return null;

            string bestUid = ResolveBestAvailableUid(uid, entryPath);
            if (string.IsNullOrEmpty(bestUid)) return null;
            if (string.Equals(bestUid, uid, StringComparison.OrdinalIgnoreCase)) return null;

            if (attemptRegister)
                TryRegisterPackageOnDemand(bestUid);

            return bestUid + entryPath.Substring(colonIdx);
        }

        private static string TryRewriteEntryPathUidByCaseInsensitiveLookup(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return null;
            string p = entryPath.Replace('\\', '/');
            int colonIdx = p.IndexOf(":/", StringComparison.Ordinal);
            if (colonIdx <= 0) return null;

            string uid = p.Substring(0, colonIdx);
            if (string.IsNullOrEmpty(uid)) return null;

            // Only rewrite casing when we can resolve the same UID/version.
            try
            {
                VarPackage pkg = FileManager.GetPackage(uid, ensureInstalled: false);
                if (pkg == null || string.IsNullOrEmpty(pkg.Uid)) return null;

                if (string.Equals(pkg.Uid, uid, StringComparison.Ordinal)) return null;
                if (!string.Equals(pkg.Uid, uid, StringComparison.OrdinalIgnoreCase)) return null;

                string rewritten = pkg.Uid + p.Substring(colonIdx);
                LogPathRewriteProbeLimited(p, rewritten,
                    "[VPB OnDemand] Rewrote entry UID by case-insensitive package lookup: req=" + p + " -> " + rewritten);
                return rewritten;
            }
            catch { return null; }
        }

        /// <summary>
        /// Rewrites an entry path to a concrete UID when policy allows.
        /// Handles "*.latest:/..." always, and versioned UIDs only when exact is missing
        /// (or ForceLatest / meta Latest fallback applies).
        /// </summary>
        public static string RewriteEntryPathToBestAvailable(string entryPath, bool attemptRegister)
        {
            if (string.IsNullOrEmpty(entryPath)) return entryPath;

            if (IsCatalogMetaJsonFilesystemProbe(entryPath))
            {
                SuppressCatalogMetaJsonProbe();
                return entryPath;
            }

            // First, normalize UID casing (VaM sometimes treats UID segment as case-sensitive).
            string uidCase = TryRewriteEntryPathUidByCaseInsensitiveLookup(entryPath);
            if (!string.IsNullOrEmpty(uidCase) && !string.Equals(uidCase, entryPath, StringComparison.Ordinal))
                entryPath = uidCase;

            // Prefer explicit .latest rewrite first.
            string rewritten = TryRewriteLatestEntryPath(entryPath, attemptRegister);
            if (!string.IsNullOrEmpty(rewritten) && !string.Equals(rewritten, entryPath, StringComparison.OrdinalIgnoreCase))
            {
                string pluginRewrite = TryRewritePluginCslistPathByFilename(rewritten);
                string baseRewritten = !string.IsNullOrEmpty(pluginRewrite) ? pluginRewrite : rewritten;
                string caseUid2 = TryRewriteEntryPathUidByCaseInsensitiveLookup(baseRewritten);
                if (!string.IsNullOrEmpty(caseUid2)) baseRewritten = caseUid2;
                string missingRewrite = TryRewriteMissingEntryPathWithinSamePackage(baseRewritten);
                return !string.IsNullOrEmpty(missingRewrite) ? missingRewrite : baseRewritten;
            }

            // Then try versioned best-available rewrite.
            string rewrittenBest = TryRewriteBestAvailableEntryPath(entryPath, attemptRegister);
            if (!string.IsNullOrEmpty(rewrittenBest) && !string.Equals(rewrittenBest, entryPath, StringComparison.OrdinalIgnoreCase))
            {
                string pluginRewrite = TryRewritePluginCslistPathByFilename(rewrittenBest);
                string baseRewritten = !string.IsNullOrEmpty(pluginRewrite) ? pluginRewrite : rewrittenBest;
                string caseUid2 = TryRewriteEntryPathUidByCaseInsensitiveLookup(baseRewritten);
                if (!string.IsNullOrEmpty(caseUid2)) baseRewritten = caseUid2;
                string missingRewrite = TryRewriteMissingEntryPathWithinSamePackage(baseRewritten);
                return !string.IsNullOrEmpty(missingRewrite) ? missingRewrite : baseRewritten;
            }

            // Finally, if UID is already concrete but the path is wrong, try locating within the same package.
            string pluginOnly = TryRewritePluginCslistPathByFilename(entryPath);
            string baseOnly = !string.IsNullOrEmpty(pluginOnly) ? pluginOnly : entryPath;
            string caseUidOnly = TryRewriteEntryPathUidByCaseInsensitiveLookup(baseOnly);
            if (!string.IsNullOrEmpty(caseUidOnly)) baseOnly = caseUidOnly;
            string missingOnly = TryRewriteMissingEntryPathWithinSamePackage(baseOnly);
            return !string.IsNullOrEmpty(missingOnly) ? missingOnly : baseOnly;
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

            // VDS startup should prioritize dependency availability over startup deferral
            // so hair/morph/asset dependencies resolve before scene bootstrap continues.
            if (VdsLauncher.IsVdsEnabled())
            {
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

        public static string TryRegisterPackageOnDemandForEntryPath(string entryPath)
        {
            string uid = UidFromEntryPath(entryPath);
            if (string.IsNullOrEmpty(uid)) return null;
            return TryRegisterPackageOnDemand(uid, persistUidOverride: IsPluginEntryPath(entryPath));
        }

        // Plugins resolve dependency morphs by display name (not by file path), so the reactive
        // file-request hook never fires; register the parent's declared deps up front instead.
        public static bool EnsureDeclaredDependenciesActivatedForParent(string parentUid)
        {
            if (string.IsNullOrEmpty(parentUid)) return false;
            if (!ScanWhitelistManager.Instance.IsEnabled) return false;

            // pkg_dep is keyed by concrete version; a plugin URL may carry ".latest".
            string resolved = parentUid;
            if (parentUid.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
            {
                string r = ResolveLatestUid(parentUid);
                if (!string.IsNullOrEmpty(r)) resolved = r;
            }

            // Gated read is freshest when the index is ready; during scene/plugin load it bails on a
            // stale scan, so only then fall back to a direct read (declared deps are immutable).
            var deps = new HashSet<string>();
            if (!VpbLocalDatabase.TryReadRecursiveDependencyUids(resolved, deps))
                VpbLocalDatabase.TryReadDeclaredDependencyUidsDirect(resolved, deps);
            if (deps.Count == 0) return false;

            int registered = 0;
            foreach (string dep in deps)
            {
                if (string.IsNullOrEmpty(dep)) continue;
                // Resolves ".latest", dedupes, skips already-registered, defers when VaM not ready;
                // non-null return means it registered the package this call.
                if (!string.IsNullOrEmpty(TryRegisterPackageOnDemand(dep))) registered++;
            }
            if (registered > 0)
                LogUtil.Log("[VPB PluginDep] " + resolved + ": registered " + registered + "/" + deps.Count + " declared dep(s)");
            return registered > 0;
        }

        /// <summary>
        /// Called from VamScanFilter.MarkVamRefreshed once VaM's first Refresh completes.
        /// Promotes deferred requests into the normal main-thread drain queue.
        /// </summary>
        public static void NotifyVamFileManagerRefreshed()
        {
            int promoted = 0;
            lock (s_VamNotReadyLock)
            {
                while (s_VamNotReadyDeferredPaths.Count > 0)
                {
                    string path = s_VamNotReadyDeferredPaths.Dequeue();
                    if (string.IsNullOrEmpty(path)) continue;
                    lock (s_QueueLock)
                        s_PendingPaths.Enqueue(path);
                    promoted++;
                }
                s_VamNotReadyDeferredUids.Clear();
            }

            if (promoted > 0)
                LogUtil.Log("[VPB OnDemand] VaM FileManager ready - promoted " + promoted + " deferred registrations");
                try { VamStartupProfiler.Milestone("VamOnDemand.FileManager_ready promoted=" + promoted); } catch { }
        }

        /// <summary>
        /// Called whenever VaM's Refresh lifecycle fully exits.
        /// Promotes registrations deferred due to refresh-in-progress back to the normal queue.
        /// </summary>
        public static void NotifyVamRefreshCompleted()
        {
            int promoted = 0;
            lock (s_RefreshInProgressLock)
            {
                while (s_RefreshInProgressDeferredPaths.Count > 0)
                {
                    string path = s_RefreshInProgressDeferredPaths.Dequeue();
                    if (string.IsNullOrEmpty(path)) continue;
                    lock (s_QueueLock)
                        s_PendingPaths.Enqueue(path);
                    promoted++;
                }
                s_RefreshInProgressDeferredUids.Clear();
            }

            if (promoted > 0)
                LogUtil.Log("[VPB OnDemand] VaM refresh completed - promoted " + promoted + " deferred registrations");

            NotifyNativeCatalogRefreshed();
        }

        private static void RegisterNow(string uid, string varPath)
        {
            if (string.IsNullOrEmpty(uid))
            {
                uid = UidFromVarPath(varPath);
                if (string.IsNullOrEmpty(uid)) return;
            }

            // Deferred startup requests can become "already registered" by the time they drain
            // (e.g. VaM's first Refresh scanned the temporary allow-list). Skip duplicate invokes.
            if (IsUidAlreadyRegisteredInVam(uid))
            {
                lock (s_RegisteredLock)
                    s_RegisteredOnDemand.Add(uid);
                lock (s_FailedLock)
                    s_LastFailedAttemptTicksByUid.Remove(uid);
                return;
            }

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
                try { DependencyGraph.EnsureForPackage(uid); } catch { }
                MarkPromotedPackageCatalogStale(uid);
                if (PackageRegistrationNeedsNativeCatalogRefresh(uid, null))
                {
                    try { RequestCoalescedVamRefresh("ondemand_register_catalog"); } catch { }
                }
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
            if (string.IsNullOrEmpty(candidatePath))
            {
                // Versioned request missing on disk: serve the latest available version.
                string bestUid = ResolveBestAvailableUid(candidateUid);
                if (!string.IsNullOrEmpty(bestUid))
                {
                    candidateUid = bestUid;
                    candidatePath = TryFindVarPathForUid(candidateUid);
                }
            }
            if (string.IsNullOrEmpty(candidatePath)) return false;

            resolvedUid = UidFromVarPath(candidatePath);
            if (string.IsNullOrEmpty(resolvedUid)) resolvedUid = candidateUid;
            varPath = NormalizePath(candidatePath);
            return !string.IsNullOrEmpty(varPath);
        }

        /// <summary>Newest installed UID for package group (e.g. MacGruber.PostMagic.3 -> .4 when .4 is installed).</summary>
        internal static string TryGetNewestInstalledUid(string requestUid)
        {
            if (string.IsNullOrEmpty(requestUid)) return null;

            string uid = requestUid.Trim();
            if (uid.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
                return ResolveLatestUid(uid);

            if (TryParseUidGroupAndVersion(uid, out string group, out _))
            {
                string latest = ResolveLatestUid(group + ".latest");
                if (!string.IsNullOrEmpty(latest)) return latest;
            }

            string latestFromBase = ResolveLatestUid(uid + ".latest");
            if (!string.IsNullOrEmpty(latestFromBase)) return latestFromBase;

            return uid;
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
            if (VamScanFilter.IsVamRefreshInProgress) return;

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

            DrainCoalescedVamRefresh();
        }

        // Interactive FileManager.Refresh rebuilds every live Person's clothing/hair; on a female soft-body
        // atom that NaNs the pelvic/genital sim and freezes the skin. Hold VaM's sim reset across the rebuild.
        private static AsyncFlag s_RefreshSimFlag;
        private static float s_RefreshSimHeldSince;
        private static float s_LastDynamicItemLoad;
        private static FieldInfo s_LoadingIconFlagsField;
        private static bool s_LoadingIconFieldResolved;
        private const float RefreshSimMinHoldSeconds = 0.3f;
        private const float RefreshSimSettleSeconds = 0.5f;
        private const float RefreshSimMaxHoldSeconds = 12f;

        // Freeze via VaM's own onCharacterLoadedFlag mechanism: PauseSimulation(flag) holds the reset until
        // the flag is raised, so the freeze spans the whole rebuild instead of a guessed frame count.
        public static void PausePhysicsForCatalogRefresh()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc == null) return;
                if (s_RefreshSimFlag != null) return;            // already holding for an in-flight rebuild
                if (sc.freezeAnimation) return;                  // already frozen (scene load / user freeze)
                s_RefreshSimFlag = new AsyncFlag("vpb_catalog_refresh");
                float now = Time.realtimeSinceStartup;
                s_RefreshSimHeldSince = now;
                s_LastDynamicItemLoad = now;
                sc.PauseSimulation(s_RefreshSimFlag);
            }
            catch { s_RefreshSimFlag = null; }
        }

        // Called from a JSONStorableDynamic.OnLoadComplete postfix: extends the hold while items keep arriving.
        public static void NotifyDynamicItemLoaded()
        {
            if (s_RefreshSimFlag != null)
                try { s_LastDynamicItemLoad = Time.realtimeSinceStartup; } catch { }
        }

        // Polled every frame: release the hold once the rebuild's item loads settle (or the backstop elapses).
        public static void TickRefreshSimHold()
        {
            var flag = s_RefreshSimFlag;
            if (flag == null) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                float held = now - s_RefreshSimHeldSince;
                bool settled = held >= RefreshSimMinHoldSeconds
                    && (now - s_LastDynamicItemLoad) >= RefreshSimSettleSeconds
                    && !IsLoadingIconBusy();
                if (settled || held >= RefreshSimMaxHoldSeconds)
                {
                    flag.Raise();
                    s_RefreshSimFlag = null;
                }
            }
            catch
            {
                try { flag.Raise(); } catch { }
                s_RefreshSimFlag = null;
            }
        }

        private static bool IsLoadingIconBusy()
        {
            try
            {
                var sc = SuperController.singleton;
                if (sc == null) return false;
                if (sc.isLoading) return true;
                if (!s_LoadingIconFieldResolved)
                {
                    s_LoadingIconFlagsField = typeof(SuperController).GetField(
                        "loadingIconFlags", BindingFlags.Instance | BindingFlags.NonPublic);
                    s_LoadingIconFieldResolved = true;
                }
                var list = s_LoadingIconFlagsField != null
                    ? s_LoadingIconFlagsField.GetValue(sc) as System.Collections.IList : null;
                if (list == null) return false;
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i] as AsyncFlag;
                    if (f != null && !f.Raised) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// Runs MVR.FileManagement.FileManager.Refresh immediately and clears any pending coalesced request.
        /// </summary>
        public static void RunVamFileManagerRefreshNow(string reason = null)
        {
            lock (s_RefreshRequestLock)
            {
                s_PendingVamRefresh = false;
                s_PendingVamRefreshRequestedAt = 0f;
                s_PendingVamRefreshFirstRequestedAt = 0f;
                s_PendingVamRefreshRequestCount = 0;
                s_PendingVamRefreshReason = null;
            }

            try
            {
                LogUtil.Log("[VPB OnDemand] Running immediate FileManager.Refresh (reason="
                    + (string.IsNullOrEmpty(reason) ? "immediate" : reason) + ")");
                PausePhysicsForCatalogRefresh();
                MVR.FileManagement.FileManager.Refresh();
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] Immediate FileManager.Refresh failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Request a single delayed VaM FileManager.Refresh. Multiple requests in a short
        /// burst are coalesced into one refresh to avoid repeated startup stalls.
        /// </summary>
        public static void RequestCoalescedVamRefresh(string reason)
        {
            lock (s_RefreshRequestLock)
            {
                bool wasPending = s_PendingVamRefresh;
                s_PendingVamRefresh = true;
                float now = Time.realtimeSinceStartup;
                if (!wasPending)
                    s_PendingVamRefreshFirstRequestedAt = now;
                s_PendingVamRefreshRequestedAt = now;
                s_PendingVamRefreshRequestCount++;
                if (!string.IsNullOrEmpty(reason))
                    s_PendingVamRefreshReason = reason;
            }
        }

        private static void DrainCoalescedVamRefresh()
        {
            bool shouldRun = false;
            int requestCount = 0;
            string reason = null;

            lock (s_RefreshRequestLock)
            {
                if (!s_PendingVamRefresh) return;

                float now = Time.realtimeSinceStartup;
                float firstRequestedAt = s_PendingVamRefreshFirstRequestedAt > 0f
                    ? s_PendingVamRefreshFirstRequestedAt
                    : s_PendingVamRefreshRequestedAt;
                float pendingAge = now - firstRequestedAt;
                bool startupReady = SafeIsStartupReadyLogged();
                bool startupSettled = SafeIsReadyLogged();

                // Startup fast-path:
                // avoid triggering expensive VaM.Refresh during early bootstrap unless the request has
                // been waiting a long time. Gate on full READY (startup settled), not UI_READY.
                // This prevents "preset_json_catalog" refreshes from injecting 1-3s stalls into
                // the tail of startup while keeping a safety escape hatch on very long sessions.
                const float MaxPreReadyDeferralSeconds = 12f;
                if ((!startupReady || !startupSettled) && pendingAge < MaxPreReadyDeferralSeconds) return;

                float delay = SafeIsStartupReadyLogged()
                    ? CoalescedVamRefreshDelayReadySeconds
                    : CoalescedVamRefreshDelayStartupSeconds;
                if (now - s_PendingVamRefreshRequestedAt < delay) return;

                shouldRun = true;
                requestCount = s_PendingVamRefreshRequestCount;
                reason = s_PendingVamRefreshReason;

                s_PendingVamRefresh = false;
                s_PendingVamRefreshRequestedAt = 0f;
                s_PendingVamRefreshFirstRequestedAt = 0f;
                s_PendingVamRefreshRequestCount = 0;
                s_PendingVamRefreshReason = null;
            }

            if (!shouldRun) return;

            try
            {
                LogUtil.Log("[VPB OnDemand] Running coalesced FileManager.Refresh (requests="
                    + requestCount + ", reason=" + (string.IsNullOrEmpty(reason) ? "unknown" : reason) + ")");
                PausePhysicsForCatalogRefresh();
                MVR.FileManagement.FileManager.Refresh();
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] Coalesced FileManager.Refresh failed: " + ex.Message);
            }
        }

        public static bool HasPendingCoalescedVamRefresh()
        {
            lock (s_RefreshRequestLock)
                return s_PendingVamRefresh;
        }

        /// <summary>
        /// Forces a pending coalesced VaM FileManager.Refresh to run immediately.
        /// Returns true when a pending refresh existed and was executed.
        /// </summary>
        public static bool ForceRunPendingCoalescedVamRefresh(string reasonOverride = null)
        {
            int requestCount = 0;
            string reason = null;
            lock (s_RefreshRequestLock)
            {
                if (!s_PendingVamRefresh) return false;
                requestCount = s_PendingVamRefreshRequestCount;
                reason = !string.IsNullOrEmpty(reasonOverride) ? reasonOverride : s_PendingVamRefreshReason;

                s_PendingVamRefresh = false;
                s_PendingVamRefreshRequestedAt = 0f;
                s_PendingVamRefreshFirstRequestedAt = 0f;
                s_PendingVamRefreshRequestCount = 0;
                s_PendingVamRefreshReason = null;
            }

            try
            {
                LogUtil.Log("[VPB OnDemand] Running forced FileManager.Refresh (pending_requests="
                    + requestCount + ", reason=" + (string.IsNullOrEmpty(reason) ? "forced" : reason) + ")");
                PausePhysicsForCatalogRefresh();
                MVR.FileManagement.FileManager.Refresh();
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] Forced FileManager.Refresh failed: " + ex.Message);
            }

            return true;
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

            long vamNotReady = Interlocked.Read(ref s_StartupVamNotReadyDeferredCount);
            var summary = new StringBuilder();
            summary.Append("[VPB OnDemand][Startup").Append(ready ? ":final" : ":checkpoint").Append("] attempts=").Append(a)
                .Append(" success=").Append(s)
                .Append(" fail=").Append(f)
                .Append(" skipped_recent_fail=").Append(sk)
                .Append(" deferred_non_script=").Append(dn)
                .Append(" allowed_script=").Append(ascr)
                .Append(" deferred_script=").Append(ds)
                .Append(" deferred_vam_not_ready=").Append(vamNotReady)
                .Append(" invoke_ms_total=").Append(ms)
                .Append(" cooldown_ms=").Append(FailedRetryCooldownMs)
                .Append(" top_fail_uids=").Append(string.IsNullOrEmpty(topFail) ? "(none)" : topFail);
            AppendPathRewriteProbeSummaryIfNeeded(summary);
            int catalogProbes = Interlocked.CompareExchange(ref s_CatalogMetaJsonProbeSuppressed, 0, 0);
            if (ready && catalogProbes > 0 && s_CatalogMetaJsonProbeNoticeLogged)
            {
                summary.Append(" catalog_meta_json_probes_suppressed=").Append(catalogProbes);
            }
            LogUtil.Log(summary.ToString());
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
                string uid = p.Substring(0, colonIdx);
                // "SELF:/" is VaM's in-package self-reference, not a package UID, and resolves internally
                // against the current package. Real UIDs are Author.Name[.ver] (always a dot); a bare token
                // like SELF would otherwise drive a full recursive AddonPackages walk for "<token>.var" on
                // every probe.
                if (uid.IndexOf('.') < 0) return null;
                return uid;
            }
            return null;
        }
    }
}
