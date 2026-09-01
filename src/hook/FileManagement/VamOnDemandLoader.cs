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
    // Unity objects require main-thread registration, so background requests enter bounded queue.
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
                // Skip Harmony GetPackage on-demand postfix — otherwise register probes recurse.
                bool prev = s_InOnDemand;
                s_InOnDemand = true;
                try
                {
                    object r = s_VamGetPackageMethod.Invoke(null, new object[] { uid });
                    return r != null;
                }
                finally { s_InOnDemand = prev; }
            }
            catch { }
            return false;
        }

        static bool NativePackageEntryExists(string qualifiedPath)
        {
            if (string.IsNullOrEmpty(qualifiedPath)) return false;
            bool previous = s_InOnDemand;
            s_InOnDemand = true;
            try
            {
                return MVR.FileManagement.FileManager.FileExists(qualifiedPath, false, false);
            }
            catch { return false; }
            finally { s_InOnDemand = previous; }
        }

        public static bool EnsurePackageRegisteredForMorph(string packageUid, string internalPath)
        {
            if (!IsMainThread() || string.IsNullOrEmpty(packageUid) || string.IsNullOrEmpty(internalPath))
                return false;
            string qualifiedPath = packageUid + ":/" + internalPath.Replace('\\', '/');
            if (IsUidAlreadyRegisteredInVam(packageUid) && NativePackageEntryExists(qualifiedPath))
                return true;
            if (VamScanFilter.IsVamRefreshInProgress) return false;
            TryRegisterPackageOnDemand(packageUid);
            return IsUidAlreadyRegisteredInVam(packageUid) && NativePackageEntryExists(qualifiedPath);
        }

        // Catalog refresh is allowed only after string-ID miss because object path already owns live item.
        public static void EnsurePackageReadyForDynamicItemActivation(
            string packageUid,
            string reason,
            bool allowCatalogForceRefresh)
        {
            if (string.IsNullOrEmpty(packageUid)) return;
            if (!ScanWhitelistManager.Instance.IsEnabled) return;
            if (!IsMainThread()) return;

            // Fast path: native already has package and clothing/hair catalog is fresh.
            try
            {
                if (IsUidAlreadyRegisteredInVam(packageUid)
                    && !IsPromotedPackageCatalogStale(packageUid))
                    return;
            }
            catch { }

            string od = null;
            try { od = TryRegisterPackageOnDemand(packageUid); }
            catch { }

            if (!allowCatalogForceRefresh) return;
            // Never nest Force Refresh inside an in-flight native Refresh (dictionary enum).
            if (VamScanFilter.IsVamRefreshInProgress) return;

            bool needCatalog = false;
            try
            {
                if (!string.IsNullOrEmpty(od)
                    && PackageRegistrationNeedsNativeCatalogRefresh(packageUid, null))
                    needCatalog = true;
                else if (IsPromotedPackageCatalogStale(packageUid))
                    needCatalog = true;
            }
            catch { }

            if (!needCatalog) return;

            string r = string.IsNullOrEmpty(reason) ? "dynamic_item_activation" : reason;
            try
            {
                if (!HasPendingCoalescedVamRefresh())
                    RequestCoalescedVamRefresh(r);
                ForceRunPendingCoalescedVamRefresh(r);
            }
            catch { }
        }

        public static string ResolvePackageUidForDynamicItem(string packageUid, string itemUid, string backupId)
        {
            if (!string.IsNullOrEmpty(packageUid)) return packageUid;
            string fromUid = UidFromEntryPath(itemUid);
            if (!string.IsNullOrEmpty(fromUid)) return fromUid;
            return UidFromEntryPath(backupId);
        }

        // Nested postfixes skip ownership so only outermost scope exits shared guard.
        public static bool TryEnterOnDemandGuard(out bool previous)
        {
            previous = s_InOnDemand;
            if (previous) return false;
            s_InOnDemand = true;
            return true;
        }

        public static void ExitOnDemandGuard(bool previous)
        {
            s_InOnDemand = previous;
        }

        // Resolve and registration wait until native refresh managers are initialized and idle.
        public static bool ShouldDeferHeavyOnDemandProbe()
        {
            if (VamScanFilter.IsVamRefreshInProgress) return true;
            if (!VamScanFilter.HasVamRefreshedAtLeastOnce && !SafeIsStartupReadyLogged()) return true;
            return false;
        }

        // Refresh-time probes stay out of runtime queue because they are not real package requests.
        public static void EnqueueDeferredOnDemandFromProbe(string packageUidOrPath)
        {
            if (string.IsNullOrEmpty(packageUidOrPath)) return;
            if (IsRawVarFilesystemPath(packageUidOrPath)) return;
            if (!ScanWhitelistManager.Instance.IsEnabled) return;
            // Refresh-time probes must not queue — see #12 hooks (log 22 register storm / crash).
            if (VamScanFilter.IsVamRefreshInProgress) return;

            EnqueueVamNotReadyDefer(packageUidOrPath, null);
        }

        private static string NormalizeOnDemandRequestUid(string uidOrPath)
        {
            if (string.IsNullOrEmpty(uidOrPath)) return null;
            string s = uidOrPath.Trim();
            if (s.Length == 0) return null;
            if (s.StartsWith(UidOnlyPathPrefix, StringComparison.Ordinal))
                s = s.Substring(UidOnlyPathPrefix.Length);
            if (s.IndexOf(":/", StringComparison.Ordinal) >= 0)
            {
                string fromEntry = UidFromEntryPath(s);
                if (!string.IsNullOrEmpty(fromEntry)) s = fromEntry;
            }
            if (s.EndsWith(".var", StringComparison.OrdinalIgnoreCase)
                || s.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string fromPath = UidFromVarPath(s);
                if (!string.IsNullOrEmpty(fromPath)) s = fromPath;
            }
            // Collapse Author.Pkg.latest.latest → Author.Pkg.latest (GetPackageGroup bug / double append).
            const string latestSuffix = ".latest";
            while (s.Length > latestSuffix.Length * 2
                && s.EndsWith(".latest.latest", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - latestSuffix.Length);
            }
            return s;
        }

        /// <summary>Returns true when this UID was newly queued.</summary>
        private static bool EnqueueRefreshInProgressDefer(string uidOrPath)
        {
            string deferUid = NormalizeOnDemandRequestUid(uidOrPath);
            if (string.IsNullOrEmpty(deferUid)) return false;
            // Nonsense UIDs from probe bugs — never queue.
            if (deferUid.EndsWith(".latest.latest", StringComparison.OrdinalIgnoreCase)) return false;
            bool added;
            lock (s_RefreshInProgressLock)
            {
                added = s_RefreshInProgressDeferredUids.Add(deferUid);
                if (added)
                    s_RefreshInProgressDeferredPaths.Enqueue(UidOnlyPathPrefix + deferUid);
            }
            // No per-UID log — Refresh can touch hundreds of legitimate entry-path defers;
            // summary is logged on promote.
            return added;
        }

        // UID sentinel postpones path resolution until package inventory is ready.
        private static bool EnqueueVamNotReadyDefer(string uidOrPath, string varPathOrNull)
        {
            string deferUid = NormalizeOnDemandRequestUid(uidOrPath);
            if (string.IsNullOrEmpty(deferUid)) return false;
            bool added;
            lock (s_VamNotReadyLock)
            {
                added = s_VamNotReadyDeferredUids.Add(deferUid);
                if (added)
                {
                    if (!string.IsNullOrEmpty(varPathOrNull)
                        && !varPathOrNull.StartsWith(UidOnlyPathPrefix, StringComparison.Ordinal)
                        && varPathOrNull.IndexOf(":/", StringComparison.Ordinal) < 0)
                        s_VamNotReadyDeferredPaths.Enqueue(varPathOrNull);
                    else
                        s_VamNotReadyDeferredPaths.Enqueue(UidOnlyPathPrefix + deferUid);
                }
            }
            if (added)
                Interlocked.Increment(ref s_StartupVamNotReadyDeferredCount);
            return added;
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

        // Filesystem paths masquerading as UIDs are catalog probes and cannot register as packages.
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

            // Trailing control characters make Path.GetDirectoryName throw, so invalid URLs stay unchanged.
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

        // Native refresh drops non-whitelisted registrations, so completed UID cache must invalidate with it.
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
        // These are promoted once MarkVamRefreshed() fires (and again at STARTUP READY).
        private static readonly Queue<string> s_VamNotReadyDeferredPaths = new Queue<string>();
        private static readonly HashSet<string> s_VamNotReadyDeferredUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_VamNotReadyLock = new object();
        // Requests that arrive while VaM FileManager.Refresh is actively running.
        // RegisterPackage during this window can race with VaM dictionary enumeration.
        private static readonly Queue<string> s_RefreshInProgressDeferredPaths = new Queue<string>();
        private static readonly HashSet<string> s_RefreshInProgressDeferredUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_RefreshInProgressLock = new object();
        private const string UidOnlyPathPrefix = "uid:";
        private static int s_UidOnlyResolveFailLogged;
        private static readonly object s_RefreshRequestLock = new object();
        private static bool s_PendingVamRefresh;
        private static float s_PendingVamRefreshRequestedAt;
        private static float s_PendingVamRefreshFirstRequestedAt;
        private static int s_PendingVamRefreshRequestCount;
        private static string s_PendingVamRefreshReason;
        // UIDs registered on-demand this session whose clothing/hair/morph catalogs may still be stale.
        private static readonly HashSet<string> s_CatalogStaleUids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Pending morph UIDs survive skip-guard refreshes because no morph bank was rebuilt.
        private static readonly HashSet<string> s_MorphIngestPendingUids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Each Person owns separate DAZMorphBank instances. Weak selector state prevents one Person's
        // completed ingest from suppressing another Person while allowing dead atoms to be pruned.
        private sealed class MorphIngestSelectorCompletion
        {
            public WeakReference Selector;
            public readonly HashSet<string> Uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        private static readonly Dictionary<int, MorphIngestSelectorCompletion> s_MorphIngestCompletedUidsBySelector =
            new Dictionary<int, MorphIngestSelectorCompletion>();
        private static readonly object s_CatalogStaleLock = new object();
        private const string MorphCatalogPathNeedle = "Custom/Atom/Person/Morphs/";
        // DAZCharacterSelector.ResetMorphsToDefault(bool physical, bool appearance) — protected.
        static MethodInfo s_ResetMorphsToDefaultMi;
        static FieldInfo s_CharacterRunFi;
        static MethodInfo s_SmoothApplyMorphsLiteMi;
        static MethodInfo s_CharacterRunResetMorphsMi;
        // physical=true, appearance=true — Appearance replace must clear pose morphs too
        // (Yuna hand-straighten + Life breathing stick otherwise and poison later looks).
        static readonly object[] s_ResetAllMorphArgs = new object[] { true, true };

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
            lock (s_VamNotReadyLock)
            {
                s_VamNotReadyDeferredPaths.Clear();
                s_VamNotReadyDeferredUids.Clear();
            }
            Interlocked.Exchange(ref s_UidOnlyResolveFailLogged, 0);
            lock (s_CatalogStaleLock)
            {
                s_CatalogStaleUids.Clear();
                s_MorphIngestPendingUids.Clear();
                s_MorphIngestCompletedUidsBySelector.Clear();
            }
        }

        /// <summary>True when UID was on-demand registered this session and native catalogs may still be stale.</summary>
        public static bool IsPromotedPackageCatalogStale(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (s_CatalogStaleLock)
                return s_CatalogStaleUids.Contains(uid);
        }

        public static void NotifyNativeCatalogRefreshed()
        {
            bool skipMorphs = VpbCatalogRefreshGuard.SkipPackageMorphRefresh;
            lock (s_CatalogStaleLock)
            {
                s_CatalogStaleUids.Clear();
                if (!skipMorphs)
                {
                    // Global pending UIDs are satisfied because full refresh updates every existing Person's morph bank.
                    s_MorphIngestPendingUids.Clear();
                }
            }
        }

        // Native refresh drops non-whitelisted packages, so registration and failure caches become stale.
        private static void InvalidateOnDemandSessionCachesAfterNativeRefresh()
        {
            lock (s_RegisteredLock)
                s_RegisteredOnDemand.Clear();
            lock (s_FailedLock)
                s_LastFailedAttemptTicksByUid.Clear();
        }

        private static bool ShouldSkipAlreadyRegisteredOnDemand(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            bool tracked;
            lock (s_RegisteredLock)
                tracked = s_RegisteredOnDemand.Contains(uid);
            if (!tracked) return false;
            if (IsUidAlreadyRegisteredInVam(uid)) return true;
            lock (s_RegisteredLock)
                s_RegisteredOnDemand.Remove(uid);
            return false;
        }

        // Morph-only pending work uses targeted bank ingest because full native refresh is more expensive.
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

        public static bool PackageRegistrationNeedsNativeCatalogRefresh(string uid, string entryPath)
        {
            if (IsPluginEntryPath(entryPath)) return false;
            if (!string.IsNullOrEmpty(entryPath) && IsCatalogDependentEntryPath(entryPath)) return true;
            return IsCatalogDependentUid(uid);
        }

        static void MarkPromotedPackageCatalogStale(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            CatalogContentKind kind = GetCatalogContentKindForUid(uid);
            lock (s_CatalogStaleLock)
            {
                s_CatalogStaleUids.Add(uid);
                if ((kind & CatalogContentKind.Morphs) != 0)
                {
                    s_MorphIngestPendingUids.Add(uid);
                    RemoveMorphIngestCompletionForUidUnlocked(uid);
                }
            }
        }

        static DAZCharacterSelector GetMorphSelector(Atom atom)
        {
            if (atom == null || !string.Equals(atom.type, "Person", StringComparison.Ordinal)) return null;
            return atom.GetStorableByID("geometry") as DAZCharacterSelector;
        }

        static MorphIngestSelectorCompletion GetMorphIngestSelectorCompletionUnlocked(
            DAZCharacterSelector selector, bool create)
        {
            if (selector == null) return null;
            PruneDeadMorphIngestSelectorCompletionsUnlocked();
            int id = selector.GetInstanceID();
            MorphIngestSelectorCompletion state;
            if (s_MorphIngestCompletedUidsBySelector.TryGetValue(id, out state))
            {
                DAZCharacterSelector existing = state.Selector != null
                    ? state.Selector.Target as DAZCharacterSelector
                    : null;
                if (existing != null && existing == selector) return state;
                s_MorphIngestCompletedUidsBySelector.Remove(id);
            }
            if (!create) return null;
            state = new MorphIngestSelectorCompletion { Selector = new WeakReference(selector) };
            s_MorphIngestCompletedUidsBySelector[id] = state;
            return state;
        }

        static void PruneDeadMorphIngestSelectorCompletionsUnlocked()
        {
            List<int> dead = null;
            foreach (KeyValuePair<int, MorphIngestSelectorCompletion> kv in s_MorphIngestCompletedUidsBySelector)
            {
                MorphIngestSelectorCompletion state = kv.Value;
                DAZCharacterSelector selector = state != null && state.Selector != null
                    ? state.Selector.Target as DAZCharacterSelector
                    : null;
                if (selector != null) continue;
                if (dead == null) dead = new List<int>();
                dead.Add(kv.Key);
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++)
                s_MorphIngestCompletedUidsBySelector.Remove(dead[i]);
        }

        static void RemoveCompletedMorphIngestForSelector(DAZCharacterSelector selector)
        {
            if (selector == null) return;
            lock (s_CatalogStaleLock)
            {
                MorphIngestSelectorCompletion state =
                    GetMorphIngestSelectorCompletionUnlocked(selector, false);
                if (state == null || state.Uids.Count == 0) return;
                foreach (string uid in state.Uids)
                    s_MorphIngestPendingUids.Remove(uid);
            }
        }

        static void MarkMorphIngestCompletedForSelector(
            DAZCharacterSelector selector, ICollection<string> uids)
        {
            if (selector == null || uids == null || uids.Count == 0) return;
            lock (s_CatalogStaleLock)
            {
                MorphIngestSelectorCompletion state =
                    GetMorphIngestSelectorCompletionUnlocked(selector, true);
                foreach (string uid in uids)
                {
                    if (!string.IsNullOrEmpty(uid)) state.Uids.Add(uid);
                }
            }
        }

        static void RemoveMorphIngestCompletionForUidUnlocked(string uid)
        {
            if (string.IsNullOrEmpty(uid) || s_MorphIngestCompletedUidsBySelector.Count == 0) return;
            PruneDeadMorphIngestSelectorCompletionsUnlocked();
            foreach (MorphIngestSelectorCompletion state in s_MorphIngestCompletedUidsBySelector.Values)
                state.Uids.Remove(uid);
        }

        [Flags]
        enum CatalogContentKind
        {
            None = 0,
            Clothing = 1,
            Hair = 2,
            Morphs = 4,
        }

        static bool IsCatalogDependentEntryPath(string entryPath)
        {
            return GetCatalogContentKindForEntryPath(entryPath) != CatalogContentKind.None;
        }

        static CatalogContentKind GetCatalogContentKindForEntryPath(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath)) return CatalogContentKind.None;
            string p = entryPath.Replace('\\', '/');
            CatalogContentKind kind = CatalogContentKind.None;
            if (p.IndexOf(":/Custom/Clothing/", StringComparison.OrdinalIgnoreCase) >= 0)
                kind |= CatalogContentKind.Clothing;
            if (p.IndexOf(":/Custom/Hair/", StringComparison.OrdinalIgnoreCase) >= 0)
                kind |= CatalogContentKind.Hair;
            if (p.IndexOf(":/Custom/Atom/Person/Morphs/", StringComparison.OrdinalIgnoreCase) >= 0)
                kind |= CatalogContentKind.Morphs;
            return kind;
        }

        static CatalogContentKind GetCatalogContentKindForUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return CatalogContentKind.None;
            try
            {
                VarPackage pkg = FileManager.GetPackage(uid, false);
                if (pkg == null)
                {
                    // Unknown package: treat as full catalog-dependent so we never skip morphs incorrectly.
                    return CatalogContentKind.Clothing | CatalogContentKind.Hair | CatalogContentKind.Morphs;
                }

                bool hasClothing;
                bool hasHair;
                bool hasMorphs;
                if (pkg.TryGetCompleteCatalogContent(out hasClothing, out hasHair, out hasMorphs))
                {
                    CatalogContentKind kind = CatalogContentKind.None;
                    if (hasClothing) kind |= CatalogContentKind.Clothing;
                    if (hasHair) kind |= CatalogContentKind.Hair;
                    if (hasMorphs) kind |= CatalogContentKind.Morphs;
                    return kind;
                }
            }
            catch { }
            return CatalogContentKind.Clothing | CatalogContentKind.Hair | CatalogContentKind.Morphs;
        }

        static bool IsCatalogDependentUid(string uid)
        {
            return GetCatalogContentKindForUid(uid) != CatalogContentKind.None;
        }

        /// <summary>True when any morph package still needs DAZ bank ingest.</summary>
        public static bool HasPendingMorphIngest()
        {
            lock (s_CatalogStaleLock)
                return s_MorphIngestPendingUids.Count > 0;
        }

        // Only referenced morph UIDs trigger bank ingest, avoiding unrelated dependency rebuilds.
        public static void NoteMorphIngestPendingForSlice(ICollection<string> uids, string sliceJson)
        {
            if (!JsonReferencesPackageMorphContent(sliceJson)) return;

            int marked = 0;
            lock (s_CatalogStaleLock)
            {
                marked += NoteMorphPackageUidsFromJsonUnlocked(sliceJson);
            }

            if (marked > 0)
            {
                try
                {
                    int depCount = uids != null ? uids.Count : 0;
                    LogUtil.Log("[VPB OnDemand] NoteMorphIngestPendingForSlice marked=" + marked
                        + " deps=" + depCount);
                }
                catch { }
            }
        }

        // Lock spans traversal because discovered UIDs mutate shared pending state.
        static int NoteMorphPackageUidsFromJsonUnlocked(string json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            const string needle = ":/Custom/Atom/Person/Morphs/";
            int marked = 0;
            int searchFrom = 0;
            while (searchFrom < json.Length)
            {
                int idx = json.IndexOf(needle, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                int end = idx;
                int start = end - 1;
                while (start >= 0)
                {
                    char c = json[start];
                    if (c == '"' || c == '\'' || c == '[' || c == ',' || c == '{' || c == ':'
                        || c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    {
                        start++;
                        break;
                    }
                    start--;
                }
                if (start < 0) start = 0;

                if (end > start)
                {
                    string uid = json.Substring(start, end - start);
                    if (!string.IsNullOrEmpty(uid)
                        && !string.Equals(uid, "SELF", StringComparison.OrdinalIgnoreCase)
                        && s_MorphIngestPendingUids.Add(uid))
                    {
                        marked++;
                    }
                }

                searchFrom = idx + needle.Length;
            }
            return marked;
        }

        /// <summary>True when any of the given UIDs still needs morph-bank ingest.</summary>
        public static bool HasMorphIngestPendingForUids(ICollection<string> uids)
        {
            if (uids == null || uids.Count == 0) return false;
            lock (s_CatalogStaleLock)
            {
                if (s_MorphIngestPendingUids.Count == 0) return false;
                foreach (string uid in uids)
                {
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (s_MorphIngestPendingUids.Contains(uid)) return true;
                }
                return false;
            }
        }

        public static bool JsonReferencesPackageMorphContent(string json)
        {
            if (string.IsNullOrEmpty(json)) return false;
            return json.IndexOf(MorphCatalogPathNeedle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Clothing-only stale sets skip morph rebuild because full package ingest is expensive.
        public static bool PendingCatalogNeedsMorphRefresh()
        {
            lock (s_CatalogStaleLock)
            {
                if (s_MorphIngestPendingUids.Count > 0) return true;
                if (s_CatalogStaleUids.Count == 0) return false;
                foreach (string uid in s_CatalogStaleUids)
                {
                    if ((GetCatalogContentKindForUid(uid) & CatalogContentKind.Morphs) != 0)
                        return true;
                }
                return false;
            }
        }

        // Unknown refresh intent keeps full morph refresh; known pending UIDs use targeted ingest.
        public static bool ShouldSkipPackageMorphRefreshForCatalogUpdate()
        {
            try
            {
                if (Settings.Instance == null
                    || Settings.Instance.SkipPackageMorphRefreshOnClothingHairCatalog == null
                    || !Settings.Instance.SkipPackageMorphRefreshOnClothingHairCatalog.Value)
                    return false;
            }
            catch { return false; }

            lock (s_CatalogStaleLock)
            {
                if (s_CatalogStaleUids.Count == 0) return false;
                foreach (string uid in s_CatalogStaleUids)
                {
                    if ((GetCatalogContentKindForUid(uid) & CatalogContentKind.Morphs) != 0)
                        return false;
                }
                return true;
            }
        }

        // Force zero before bank rebuild because VaM restores non-default snapshots and pose formulas can retain old look state.
        public static void ResetAppearanceMorphValues(Atom targetAtom, string reason = null)
        {
            if (!IsMainThread()) return;
            if (targetAtom == null || !string.Equals(targetAtom.type, "Person", StringComparison.Ordinal))
                return;

            try
            {
                var selector = targetAtom.GetStorableByID("geometry") as DAZCharacterSelector;
                if (selector == null) return;

                // Force zero on every morph in the UI lists (includes demand-activated).
                ZeroMorphList(selector.morphsControlUI);
                ZeroMorphList(selector.morphsControlUIAlt);
                ZeroMorphList(selector.morphsControlUIOtherGender);

                if (s_ResetMorphsToDefaultMi == null)
                {
                    s_ResetMorphsToDefaultMi = typeof(DAZCharacterSelector).GetMethod(
                        "ResetMorphsToDefault",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new Type[] { typeof(bool), typeof(bool) },
                        null);
                }
                if (s_ResetMorphsToDefaultMi != null)
                    s_ResetMorphsToDefaultMi.Invoke(selector, s_ResetAllMorphArgs);
                else
                    selector.ResetMorphsOtherGender(true, true);

                // Flush bone formulas (Yuna Body targets carpals/neck/hip) so zeroed values take effect.
                FlushCharacterRunMorphs(selector);

                try
                {
                    LogUtil.Log("[VPB OnDemand] ResetAppearanceMorphValues"
                        + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason))
                        + " atom=" + targetAtom.uid);
                }
                catch { }
            }
            catch (Exception ex)
            {
                try
                {
                    LogUtil.LogWarning("[VPB OnDemand] ResetAppearanceMorphValues failed: " + ex.Message
                        + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason)));
                }
                catch { }
            }
        }

        static void ZeroMorphList(GenerateDAZMorphsControlUI ui)
        {
            if (ui == null) return;
            List<DAZMorph> morphs = ui.GetMorphs();
            if (morphs == null) return;
            for (int i = 0; i < morphs.Count; i++)
            {
                DAZMorph m = morphs[i];
                if (m == null) continue;
                try { m.morphValue = 0f; }
                catch
                {
                    try { m.Reset(); } catch { }
                }
            }
        }

        static void FlushCharacterRunMorphs(DAZCharacterSelector selector)
        {
            if (selector == null) return;
            try
            {
                if (s_CharacterRunFi == null)
                    s_CharacterRunFi = typeof(DAZCharacterSelector).GetField(
                        "_characterRun", BindingFlags.Instance | BindingFlags.NonPublic);
                object run = s_CharacterRunFi != null ? s_CharacterRunFi.GetValue(selector) : null;
                if (run == null) return;

                if (s_CharacterRunResetMorphsMi == null)
                    s_CharacterRunResetMorphsMi = run.GetType().GetMethod(
                        "ResetMorphs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                if (s_CharacterRunResetMorphsMi != null)
                    s_CharacterRunResetMorphsMi.Invoke(run, null);

                if (s_SmoothApplyMorphsLiteMi == null)
                    s_SmoothApplyMorphsLiteMi = run.GetType().GetMethod(
                        "SmoothApplyMorphsLite", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null, Type.EmptyTypes, null);
                if (s_SmoothApplyMorphsLiteMi != null)
                    s_SmoothApplyMorphsLiteMi.Invoke(run, null);
            }
            catch { }
        }

        // Inactive demand-loaded morphs unload after replace so old formulas cannot affect later looks.
        public static void UnloadInactiveDemandMorphs(Atom targetAtom, string reason = null)
        {
            if (!IsMainThread()) return;
            if (targetAtom == null || !string.Equals(targetAtom.type, "Person", StringComparison.Ordinal))
                return;
            try
            {
                var selector = targetAtom.GetStorableByID("geometry") as DAZCharacterSelector;
                if (selector == null) return;
                selector.UnloadDemandActivatedMorphs();
                selector.CleanDemandActivatedMorphs();
                try
                {
                    LogUtil.Log("[VPB OnDemand] UnloadInactiveDemandMorphs"
                        + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason))
                        + " atom=" + targetAtom.uid);
                }
                catch { }
            }
            catch (Exception ex)
            {
                try
                {
                    LogUtil.LogWarning("[VPB OnDemand] UnloadInactiveDemandMorphs failed: " + ex.Message);
                }
                catch { }
            }
        }

        // Bank rebuild stays on Unity main thread because DAZ selectors are Unity objects.
        public static bool EnsurePackageMorphsIngested(Atom targetAtom, string reason = null)
        {
            if (!IsMainThread()) return false;
            if (VpbCatalogRefreshGuard.SkipPackageMorphRefresh)
            {
                try
                {
                    LogUtil.LogWarning("[VPB OnDemand] EnsurePackageMorphsIngested skipped (skip guard active)"
                        + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason)));
                }
                catch { }
                return false;
            }

            bool hadPending;
            int pendingCount;
            List<string> pendingUids;
            lock (s_CatalogStaleLock)
            {
                pendingCount = s_MorphIngestPendingUids.Count;
                hadPending = pendingCount > 0;
                pendingUids = new List<string>(s_MorphIngestPendingUids);
            }

            // Import paths clear old values because RefreshPackageMorphs restores non-default snapshots.
            bool resetForImport = !string.IsNullOrEmpty(reason)
                && (reason.IndexOf("vpb_import", StringComparison.OrdinalIgnoreCase) >= 0
                    || reason.IndexOf("VpbImport", StringComparison.OrdinalIgnoreCase) >= 0);
            if (resetForImport && targetAtom != null)
                ResetAppearanceMorphValues(targetAtom, "pre_ingest:" + reason);

            bool changed = false;
            bool refreshedAny = false;
            List<DAZCharacterSelector> refreshedSelectors = new List<DAZCharacterSelector>();
            try
            {
                if (targetAtom != null && string.Equals(targetAtom.type, "Person", StringComparison.Ordinal))
                {
                    DAZCharacterSelector selector;
                    changed = RefreshPackageMorphsOnAtom(targetAtom, out selector);
                    if (selector != null)
                    {
                        refreshedAny = true;
                        refreshedSelectors.Add(selector);
                    }
                }
                else
                {
                    var sc = SuperController.singleton;
                    if (sc != null)
                    {
                        foreach (Atom atom in sc.GetAtoms())
                        {
                            if (atom == null || atom.type != "Person") continue;
                            DAZCharacterSelector selector;
                            if (RefreshPackageMorphsOnAtom(atom, out selector)) changed = true;
                            if (selector != null)
                            {
                                refreshedAny = true;
                                refreshedSelectors.Add(selector);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    LogUtil.LogWarning("[VPB OnDemand] EnsurePackageMorphsIngested failed: " + ex.Message
                        + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason)));
                }
                catch { }
                return false;
            }

            // No bank reached means ingest did not happen. Keep pending UIDs for a later initialized Person.
            if (!refreshedAny)
                return false;

            for (int i = 0; i < refreshedSelectors.Count; i++)
                MarkMorphIngestCompletedForSelector(refreshedSelectors[i], pendingUids);

            lock (s_CatalogStaleLock)
            {
                // Registrations can arrive while VaM refreshes. Remove only this completed snapshot.
                for (int i = 0; i < pendingUids.Count; i++)
                    s_MorphIngestPendingUids.Remove(pendingUids[i]);
            }

            try
            {
                LogUtil.Log("[VPB OnDemand] EnsurePackageMorphsIngested changed=" + (changed ? 1 : 0)
                    + " pendingWas=" + pendingCount
                    + " hadPending=" + (hadPending ? 1 : 0)
                    + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason))
                    + (targetAtom != null ? (" atom=" + targetAtom.uid) : " atom=all"));
            }
            catch { }

            return changed;
        }

        // JSON refs mark pending UIDs before gate because clothing-only refresh can skip morph ingest.
        public static bool EnsurePackageMorphsIngestedIfNeeded(Atom targetAtom, string jsonOrNull, string reason = null)
        {
            if (!string.IsNullOrEmpty(jsonOrNull))
                NoteMorphIngestPendingForSlice(null, jsonOrNull);
            RemoveCompletedMorphIngestForSelector(GetMorphSelector(targetAtom));
            if (!HasPendingMorphIngest())
                return false;
            return EnsurePackageMorphsIngested(targetAtom, reason);
        }

        static bool RefreshPackageMorphsOnAtom(Atom atom, out DAZCharacterSelector selector)
        {
            selector = GetMorphSelector(atom);
            if (selector == null) return false;
            return selector.RefreshPackageMorphs();
        }

        /// <summary>Drop coalesced native refresh without running it (light clothing catalog path succeeded).</summary>
        public static bool CancelPendingCoalescedVamRefresh(string reason = null)
        {
            lock (s_RefreshRequestLock)
            {
                if (!s_PendingVamRefresh) return false;
                s_PendingVamRefresh = false;
                s_PendingVamRefreshRequestedAt = 0f;
                s_PendingVamRefreshFirstRequestedAt = 0f;
                s_PendingVamRefreshRequestCount = 0;
                s_PendingVamRefreshReason = null;
            }
            try
            {
                LogUtil.Log("[VPB OnDemand] Cancelled pending FileManager.Refresh"
                    + (string.IsNullOrEmpty(reason) ? "" : (" reason=" + reason)));
            }
            catch { }
            return true;
        }

        public static void RefreshPersonClothingHairCatalogs(Atom targetAtom)
        {
            try
            {
                if (targetAtom != null && string.Equals(targetAtom.type, "Person", StringComparison.Ordinal))
                {
                    RefreshOnePersonClothingHairCatalog(targetAtom);
                    return;
                }

                var sc = SuperController.singleton;
                if (sc == null) return;
                foreach (Atom atom in sc.GetAtoms())
                {
                    if (atom == null || atom.type != "Person") continue;
                    RefreshOnePersonClothingHairCatalog(atom);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] RefreshPersonClothingHairCatalogs failed: " + ex.Message);
            }
        }

        static void RefreshOnePersonClothingHairCatalog(Atom atom)
        {
            if (atom == null) return;
            try
            {
                var clothing = atom.GetStorableByID("Clothing") as DAZClothingItemControl;
                if (clothing != null) clothing.RefreshClothingItems();
            }
            catch { }
            try
            {
                var hair = atom.GetStorableByID("Hair") as DAZHairGroupControl;
                if (hair != null) hair.RefreshHairItems();
            }
            catch { }
            try
            {
                var selector = atom.GetStorableByID("geometry") as DAZCharacterSelector;
                if (selector != null) selector.RefreshDynamicClothes();
            }
            catch { }
        }

        /// <summary>Public entry for delayed MVR refresh coroutine (same morph-skip policy).</summary>
        public static void InvokeNativeFileManagerRefreshForDelayedMvr(string reason)
        {
            InvokeNativeFileManagerRefresh("Running delayed FileManager.Refresh", reason);
        }

        static void InvokeNativeFileManagerRefresh(string logLabel, string reason)
        {
            bool skipMorphs = ShouldSkipPackageMorphRefreshForCatalogUpdate();
            Action run = delegate
            {
                PausePhysicsForCatalogRefresh();
                MVR.FileManagement.FileManager.Refresh();
            };

            if (skipMorphs)
            {
                try
                {
                    LogUtil.Log("[VPB OnDemand] " + logLabel + " (skipPackageMorphs=1 reason="
                        + (string.IsNullOrEmpty(reason) ? "unknown" : reason) + ")");
                }
                catch { }
                VpbCatalogRefreshGuard.RunSkippingPackageMorphRefresh(run);
            }
            else
            {
                try
                {
                    LogUtil.Log("[VPB OnDemand] " + logLabel + " (skipPackageMorphs=0 reason="
                        + (string.IsNullOrEmpty(reason) ? "unknown" : reason) + ")");
                }
                catch { }
                run();
            }
        }

        // Bare .var paths are catalog probes, so on-demand registration ignores them.
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

        public static string TryRegisterPackageOnDemand(string uid, bool persistUidOverride = false)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            if (!ScanWhitelistManager.Instance.IsEnabled) return null;
            if (!VamScanFilter.HasRegisterMethodAccess) return null;
            if (!persistUidOverride && IsRawVarFilesystemPath(uid)) return null;

            string normalized = NormalizeOnDemandRequestUid(uid);
            if (!string.IsNullOrEmpty(normalized))
                uid = normalized;

            // Already registered this session — but only skip if VaM still has the package.
            // Native Refresh under scan whitelist can drop it while this set still contains the UID.
            if (ShouldSkipAlreadyRegisteredOnDemand(uid)) return null;

            // Cooldown repeated failures per UID to prevent startup stalls from repeated reflection/invoke exceptions.
            if (WasRecentFailure(uid))
            {
                lock (s_StartupStatsLock)
                    s_StartupSkippedRecentFailCount++;
                return null;
            }

            // Refresh-time misses defer before recursive path resolution to avoid repeated AddonPackages walks.
            if (VamScanFilter.IsVamRefreshInProgress)
            {
                EnqueueRefreshInProgressDefer(uid);
                return null;
            }
            if (!VamScanFilter.HasVamRefreshedAtLeastOnce && !SafeIsStartupReadyLogged())
            {
                if (EnqueueVamNotReadyDefer(uid, null))
                {
                    string deferUidEarly = NormalizeOnDemandRequestUid(uid);
                    if (!string.IsNullOrEmpty(deferUidEarly))
                        LogUtil.Log("[VPB OnDemand] Defer before VaM FileManager ready: " + deferUidEarly);
                }
                return null;
            }

            // Non-script / heavy-script policy until READY — queue UID only (resolve on promote).
            {
                string deferUidPolicy = NormalizeOnDemandRequestUid(uid);
                if (!string.IsNullOrEmpty(deferUidPolicy)
                    && ShouldDeferStartupOnDemandForPath(deferUidPolicy + ":/", deferUidPolicy))
                {
                    EnqueueVamNotReadyDefer(deferUidPolicy, null);
                    return null;
                }
            }

            if (!TryResolveVarPathForUid(uid, out string resolvedUid, out string varPath))
            {
                // Do not poison during native Refresh / pre-ready windows — resolve can fail transiently
                // while VaM dictionaries rebuild or VPB inventory is mid-scan.
                if (!VamScanFilter.IsVamRefreshInProgress && VamScanFilter.HasVamRefreshedAtLeastOnce)
                {
                    // Genuinely unresolvable: arm the failure cooldown so repeated probes for the same uid
                    // short-circuit instead of re-running the recursive AddonPackages walk on every hook call.
                    MarkFailure(uid);
                }
                return null;
            }
            if (string.IsNullOrEmpty(varPath)) return null;

            if (!string.IsNullOrEmpty(resolvedUid) && ShouldSkipAlreadyRegisteredOnDemand(resolvedUid)) return null;
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

            // RegisterPackage can run before native managers initialize, so replay requests after refresh readiness.
            if (!VamScanFilter.HasVamRefreshedAtLeastOnce && !SafeIsStartupReadyLogged())
            {
                EnqueueVamNotReadyDefer(
                    !string.IsNullOrEmpty(resolvedUid) ? resolvedUid : uid,
                    varPath);
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
                if (added)
                    LogUtil.Log("[VPB OnDemand] Defer during VaM Refresh: " + deferUid);
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

        // Exact installed pins stay unchanged unless explicit policy forces latest.
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
            // Script requests remain available during startup, while non-script requests wait for READY to avoid late bootstrap stalls.
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

        public static void NotifyVamFileManagerRefreshed()
        {
            PromoteVamNotReadyDeferred("FileManager_ready");
        }

        public static void NotifyVamRefreshCompleted()
        {
            int promoted = PromoteDeferredQueueToPending(
                s_RefreshInProgressLock,
                s_RefreshInProgressDeferredPaths,
                s_RefreshInProgressDeferredUids,
                "refresh_completed");

            if (promoted > 0)
                LogUtil.Log("[VPB OnDemand] VaM refresh completed - promoted " + promoted + " deferred registrations");

            // Drop session skip caches before catalog-stale clear so the next miss can re-register
            // packages that native Refresh just excluded under the scan whitelist (#77).
            InvalidateOnDemandSessionCachesAfterNativeRefresh();
            NotifyNativeCatalogRefreshed();
        }

        private static void PromoteVamNotReadyDeferred(string reason)
        {
            int promoted = PromoteDeferredQueueToPending(
                s_VamNotReadyLock,
                s_VamNotReadyDeferredPaths,
                s_VamNotReadyDeferredUids,
                reason);
            if (promoted > 0)
            {
                LogUtil.Log("[VPB OnDemand] VaM FileManager ready - promoted " + promoted
                    + " deferred registrations (" + reason + ")");
                try { VamStartupProfiler.Milestone("VamOnDemand.FileManager_ready promoted=" + promoted + " reason=" + reason); } catch { }
            }
        }

        // UID sentinels postpone disk resolution until inventory is warm enough to avoid refresh-postfix spikes.
        private static int PromoteDeferredQueueToPending(
            object lockObj,
            Queue<string> paths,
            HashSet<string> uids,
            string reason)
        {
            int promoted = 0;
            lock (lockObj)
            {
                while (paths.Count > 0)
                {
                    string entry = paths.Dequeue();
                    if (string.IsNullOrEmpty(entry)) continue;
                    lock (s_QueueLock)
                        s_PendingPaths.Enqueue(entry);
                    promoted++;
                }
                uids.Clear();
            }
            return promoted;
        }

        private static bool TryResolveQueueEntry(string entry, out string uid, out string varPath)
        {
            uid = null;
            varPath = null;
            if (string.IsNullOrEmpty(entry)) return false;

            if (entry.StartsWith(UidOnlyPathPrefix, StringComparison.Ordinal))
            {
                string req = entry.Substring(UidOnlyPathPrefix.Length);
                if (string.IsNullOrEmpty(req)) return false;
                if (!TryResolveVarPathForUid(req, out uid, out varPath))
                {
                    int n = Interlocked.Increment(ref s_UidOnlyResolveFailLogged);
                    if (n <= 8)
                        LogUtil.LogWarning("[VPB OnDemand] Deferred UID resolve failed: " + req);
                    return false;
                }
                return !string.IsNullOrEmpty(varPath);
            }

            uid = UidFromVarPath(entry);
            varPath = NormalizePath(entry);
            if (string.IsNullOrEmpty(uid)) uid = NormalizeOnDemandRequestUid(entry);
            return !string.IsNullOrEmpty(varPath);
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
                try { VpbLocalDatabase.RecordOnDemandHit(uid); } catch { }
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
                    string livePath = NormalizePath(vpbPkg.Path);
                    // Registry can lag after package moves; require file on disk before trusting path.
                    if ((File.Exists(livePath) || File.Exists(vpbPkg.Path))
                        && !string.IsNullOrEmpty(livePath))
                    {
                        resolvedUid = !string.IsNullOrEmpty(vpbPkg.Uid) ? vpbPkg.Uid : UidFromVarPath(vpbPkg.Path);
                        varPath = livePath;
                        if (!string.IsNullOrEmpty(resolvedUid)) return true;
                    }
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
            // Skip during native Refresh / pre-ready — recursive *.var walks stall Init (#12).
            if (VamScanFilter.IsVamRefreshInProgress
                || (!VamScanFilter.HasVamRefreshedAtLeastOnce && !SafeIsStartupReadyLogged()))
                return null;

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

            // Indexed paths can go stale after package moves, so missing files fall through to recursive lookup.
            try
            {
                if (VpbLocalDatabase.TryResolveIndexedVarPathForUid(uid, out string sqlPath) && !string.IsNullOrEmpty(sqlPath))
                {
                    string normSql = NormalizePath(sqlPath);
                    if (File.Exists(normSql) || (!string.Equals(normSql, sqlPath, StringComparison.Ordinal) && File.Exists(sqlPath)))
                        return normSql;
                }
            }
            catch { }

            string filename = uid + ".var";
            string addon = NormalizePath(Path.Combine("AddonPackages", filename));
            if (File.Exists(addon)) return addon;
            string all = NormalizePath(Path.Combine("AllPackages", filename));
            if (File.Exists(all)) return all;

            // Recursive walk is expensive on large libraries — never during Refresh / pre-ready.
            if (VamScanFilter.IsVamRefreshInProgress
                || (!VamScanFilter.HasVamRefreshedAtLeastOnce && !SafeIsStartupReadyLogged()))
                return null;

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

        // Per-frame drain cap prevents registration bursts from hitching Unity main thread.
        public static void DrainMainThreadQueue()
        {
            if (!ScanWhitelistManager.Instance.IsEnabled) return;
            MaybeLogStartupSummary();
            if (VamScanFilter.IsVamRefreshInProgress) return;

            // Non-script requests deferred after first Refresh but before READY were stuck in
            // s_VamNotReady* (NotifyVamFileManagerRefreshed only runs once). Flush at READY.
            if (SafeIsStartupReadyLogged())
            {
                bool hasLeftover;
                lock (s_VamNotReadyLock)
                    hasLeftover = s_VamNotReadyDeferredPaths.Count > 0;
                if (hasLeftover)
                    PromoteVamNotReadyDeferred("startup_ready");
            }

            int drained = 0;
            while (drained < MaxDrainPerFrame)
            {
                string entry;
                lock (s_QueueLock)
                {
                    if (s_PendingPaths.Count == 0) break;
                    entry = s_PendingPaths.Dequeue();
                }

                if (!string.IsNullOrEmpty(entry))
                {
                    string uid;
                    string path;
                    if (TryResolveQueueEntry(entry, out uid, out path))
                    {
                        if (!string.IsNullOrEmpty(uid))
                            RegisterNow(uid, path);
                    }
                    else if (entry.StartsWith(UidOnlyPathPrefix, StringComparison.Ordinal))
                    {
                        // Unresolvable now (missing .var / index lag). Arm cooldown; do not spin.
                        string req = entry.Substring(UidOnlyPathPrefix.Length);
                        if (!string.IsNullOrEmpty(req))
                            MarkFailure(req);
                    }
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
                InvokeNativeFileManagerRefresh("Running immediate FileManager.Refresh", reason);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB OnDemand] Immediate FileManager.Refresh failed: " + ex.Message);
            }
        }

        // Burst requests share one delayed refresh to avoid repeated startup stalls.
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

                // Early refresh stalls startup, so only long-waiting requests bypass READY gate.
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
                InvokeNativeFileManagerRefresh(
                    "Running coalesced FileManager.Refresh requests=" + requestCount,
                    reason);
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

        /// <summary>Warm-path probe string: pending refresh + why morph-skip is on/off.</summary>
        public static string DescribePendingCatalogRefreshForProbe()
        {
            int pendingCount = 0;
            string pendingReason = null;
            bool pending;
            lock (s_RefreshRequestLock)
            {
                pending = s_PendingVamRefresh;
                pendingCount = s_PendingVamRefreshRequestCount;
                pendingReason = s_PendingVamRefreshReason;
            }

            bool skipMorphs = false;
            int staleCount = 0;
            int morphStale = 0;
            int morphIngestPending = 0;
            string morphSample = "";
            try
            {
                skipMorphs = ShouldSkipPackageMorphRefreshForCatalogUpdate();
                lock (s_CatalogStaleLock)
                {
                    staleCount = s_CatalogStaleUids.Count;
                    morphIngestPending = s_MorphIngestPendingUids.Count;
                    int shown = 0;
                    foreach (string uid in s_CatalogStaleUids)
                    {
                        if ((GetCatalogContentKindForUid(uid) & CatalogContentKind.Morphs) != 0)
                        {
                            morphStale++;
                            if (shown < 4)
                            {
                                if (shown > 0) morphSample += ",";
                                morphSample += uid;
                                shown++;
                            }
                        }
                    }
                    if (shown < 4)
                    {
                        foreach (string uid in s_MorphIngestPendingUids)
                        {
                            if (string.IsNullOrEmpty(uid)) continue;
                            if (shown > 0) morphSample += ",";
                            morphSample += uid;
                            shown++;
                            if (shown >= 4) break;
                        }
                    }
                }
            }
            catch { }

            return "pending=" + (pending ? 1 : 0)
                + " reqs=" + pendingCount
                + " reason=" + (pendingReason ?? "-")
                + " skipMorphs=" + (skipMorphs ? 1 : 0)
                + " stale=" + staleCount
                + " morphStale=" + morphStale
                + " morphIngestPending=" + morphIngestPending
                + " morphSample=" + (string.IsNullOrEmpty(morphSample) ? "-" : morphSample);
        }

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
                InvokeNativeFileManagerRefresh(
                    "Running forced FileManager.Refresh pending_requests=" + requestCount,
                    reason);
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

        public static string UidFromVarPath(string varPath)
        {
            if (string.IsNullOrEmpty(varPath)) return null;
            string filename = Path.GetFileNameWithoutExtension(varPath);
            return filename;
        }

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
                // SELF is in-package syntax, so treating it as package UID would trigger pointless recursive searches.
                if (uid.IndexOf('.') < 0) return null;
                return uid;
            }
            return null;
        }
    }
}
