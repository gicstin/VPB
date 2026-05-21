using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MVR.FileManagement;
using Prime31.MessageKit;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Local SQLite database for VPB: gallery VAR index (category membership, package rows, rebuild metadata).
    /// Rebuilt after package scans; optional fast path in <see cref="GalleryPanel"/> when signatures match.
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        /// <summary>Set on main thread by <see cref="GalleryPanel"/> before creator-filtered DB queries for the active browse.</summary>
        internal static bool GalleryCreatorFilterCaseInsensitive;

        private static List<string> SplitCreatorFilterList(string creatorFilter)
        {
            var list = new List<string>();
            string src = creatorFilter ?? "";
            if (src.Length == 0) return list;
            string[] parts = src.Split('|');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i] != null ? parts[i].Trim() : "";
                if (p.Length == 0) continue;
                list.Add(p);
            }
            return list;
        }

        private static void AppendCreatorFilterSql(StringBuilder sb, string columnSql, List<string> creators)
        {
            if (sb == null || creators == null || creators.Count == 0) return;
            bool nocase = GalleryCreatorFilterCaseInsensitive;
            if (nocase)
            {
                sb.Append(" AND (");
                for (int i = 0; i < creators.Count; i++)
                {
                    if (i > 0) sb.Append(" OR ");
                    sb.Append("lower(").Append(columnSql).Append(") = lower(?)");
                }
                sb.Append(')');
                return;
            }
            if (creators.Count == 1)
            {
                sb.Append(" AND ").Append(columnSql).Append(" = ?");
                return;
            }
            sb.Append(" AND ").Append(columnSql).Append(" IN (");
            for (int i = 0; i < creators.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('?');
            }
            sb.Append(')');
        }

        private static void BindCreatorFilterSql(VpbSqlite3.Statement stmt, ref int bind, List<string> creators)
        {
            if (stmt == null || creators == null) return;
            for (int i = 0; i < creators.Count; i++)
                stmt.BindText(bind++, creators[i] ?? "");
        }

        /// <summary>Optional <c>[VPB.History]</c> trace logs (default off).</summary>
        internal static bool LogHistoryUsageDebug = false;
        /// <summary>Logs every <see cref="TryRecordItemUse"/> (very chatty).</summary>
        internal static bool LogHistoryRecordWrites = false;

        internal struct Row
        {
            public string PackageUid;
            public string InternalPath;
            /// <summary>Precomputed <see cref="VarFileEntry.Path"/> at index rebuild (empty for legacy DB rows until rebuild).</summary>
            public string ListPath;
            /// <summary>Last known .var path (<c>pkg.var_path</c>) for lazy package resolution if the file moved.</summary>
            public string VarPath;
            public long LastWriteTicksOrInvalid;
            public long PackageSizeOrInvalid;
            public long PackageCreationTicksOrInvalid;
            /// <summary>First time VPB indexed this VAR uid (<c>pkg.first_scanned</c>, <see cref="DateTime.ToBinary"/>); 0 when unknown.</summary>
            public long FirstScannedTicksOrInvalid;
            /// <summary>
            /// Packed gender / preset / decal / kind for Clothing gallery subfilters (see <see cref="ClothingPackedAttrMatchesSubfilter"/>).
            /// Bit 31 set when populated at index rebuild; 0 or unset column means caller may fall back to path classification.
            /// </summary>
            public int ClothingAttrPacked;
            /// <summary>
            /// From <c>pkg.loaded</c>: package is &quot;loaded&quot; when its .var lives under <c>AddonPackages/</c>, or under <c>Custom/</c> / <c>Saves/</c> (always loaded).
            /// </summary>
            public bool PackageIsLoaded;
            /// <summary><c>item_usage.item_key</c> when this row is from History SQL; empty for category index rows.</summary>
            public string ItemUsageKey;
            /// <summary><c>item_usage.use_count</c> when <see cref="ItemUsageKey"/> is set.</summary>
            public int ItemUsageCount;
            /// <summary><c>item_usage.last_used</c> (<see cref="DateTime.ToBinary"/>).</summary>
            public long ItemLastUsedBinary;
        }

        internal struct PackageRow
        {
            public string PackageUid;
            public string VarPath;
            public long LastWriteTicksOrInvalid;
            public long PackageSizeOrInvalid;
            public long PackageCreationTicksOrInvalid;
            /// <summary>First time VPB indexed this VAR uid (<c>pkg.first_scanned</c>, <see cref="DateTime.ToBinary"/>); 0 or <see cref="long.MinValue"/> when unknown.</summary>
            public long FirstScannedTicksOrInvalid;
            public bool PackageIsLoaded;
        }

        /// <summary>High bit: row has <see cref="ClothingAttrPacked"/> from index rebuild (fast clothing subfilter path).</summary>
        internal const int ClothingAttrPresentFlag = unchecked((int)0x80000000);

        private const int SchemaVersion = 12;

        private static readonly object s_Sync = new object();
        private static volatile bool s_RebuildScheduled;
        private static volatile bool s_RebuildRunning;
        private static long s_ReadyScanBinary = long.MinValue;
        private static string s_ReadyCategoriesSig;
        // Package-inventory signature for the currently "ready" index. Lets us bump ready scan clock
        // without rebuilding when FileManager.lastPackageRefreshTime advances but package set is unchanged.
        private static string s_ReadyPkgInvSig;
        private static string s_LastError;
        private static bool s_LoggedSqliteUnavailable;
        private static bool s_LoggedEmptyCategoriesDb;
        private static long s_CachedInvScanBinary = long.MinValue;
        private static int s_CachedInvPkgCount;
        private static string s_CachedInvSig;
        private static long s_LastAutoScheduleScanBinary = long.MinValue;

        private const string DatabaseFileName = "VpbLocalDatabase.sqlite3";
        private const string LegacyDatabaseFileName = "GalleryVarFileIndex.sqlite3";

        /// <summary>Prefer <see cref="DatabaseFileName"/>; one-time rename from <see cref="LegacyDatabaseFileName"/> when the new file is absent.</summary>
        private static string ResolveDatabaseFilePath(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                directory = Path.GetFullPath(Path.Combine("Cache", "VPB"));
            try
            {
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
            }
            catch { }

            string current = Path.Combine(directory, DatabaseFileName);
            string legacy = Path.Combine(directory, LegacyDatabaseFileName);
            try
            {
                if (!File.Exists(current) && File.Exists(legacy))
                    File.Move(legacy, current);
            }
            catch { }
            return current;
        }

        private static string DbPath
        {
            get
            {
                try
                {
                    string dir = VpbSqlite3.GetCacheVpbDirectoryOrFallback();
                    if (string.IsNullOrEmpty(dir))
                        dir = Path.GetFullPath(Path.Combine("Cache", "VPB"));
                    return ResolveDatabaseFilePath(dir);
                }
                catch
                {
                    return ResolveDatabaseFilePath(Application.temporaryCachePath);
                }
            }
        }

        internal static string GetLocalDatabasePathForDiagnostics()
        {
            try { return Path.GetFullPath(DbPath); }
            catch { return DbPath; }
        }

        private static string HistoryDebugTruncate(string s, int maxLen = 220)
        {
            if (string.IsNullOrEmpty(s)) return "(empty)";
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "…(" + s.Length + " chars)";
        }

        /// <summary>How many of the given keys currently exist in <c>item_usage</c> (same batching as delete).</summary>
        private static long CountItemUsageKeysPresent(VpbSqlite3.Connection conn, IList<string> itemKeys, int start, int n)
        {
            if (n <= 0) return 0;
            var sb = new StringBuilder(56 + n * 2);
            sb.Append("SELECT COUNT(*) FROM item_usage WHERE item_key IN (");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('?');
            }
            sb.Append(')');
            using (var st = conn.Prepare(sb.ToString()))
            {
                for (int i = 0; i < n; i++)
                    st.BindText(i + 1, itemKeys[start + i] ?? "");
                if (st.Step() != VpbSqlite3.SqliteRow) return -1;
                return st.ColumnInt64(0);
            }
        }

        private static long ScalarInt64(VpbSqlite3.Connection conn, string sqlUtf8)
        {
            using (var st = conn.Prepare(sqlUtf8))
            {
                int rc = st.Step();
                if (rc != VpbSqlite3.SqliteRow) return -1;
                string t = st.ColumnText(0);
                long n;
                if (long.TryParse(t, out n)) return n;
                return -1;
            }
        }

        private static string MetaGet(VpbSqlite3.Connection conn, string key)
        {
            try
            {
                using (var st = conn.Prepare("SELECT v FROM meta WHERE k=?"))
                {
                    st.BindText(1, key);
                    if (st.Step() != VpbSqlite3.SqliteRow) return "";
                    return st.ColumnText(0) ?? "";
                }
            }
            catch
            {
                return "";
            }
        }

        private static string FormatByteSize(long bytes)
        {
            if (bytes < 0) return "?";
            if (bytes < 1024L) return bytes + " B";
            if (bytes < 1024L * 1024L) return (bytes / 1024.0).ToString("0.0") + " KiB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MiB";
        }

        private static void LogLocalDatabaseReadyDetails(
            VpbSqlite3.Connection conn,
            string dbPath,
            long rebuildElapsedMs,
            int categoryDefCount,
            int snapshotPackageCount)
        {
            try
            {
                long nPkg = ScalarInt64(conn, "SELECT COUNT(*) FROM pkg;");
                long nMem = ScalarInt64(conn, "SELECT COUNT(*) FROM cat_mem;");
                long nCatDistinct = ScalarInt64(conn, "SELECT COUNT(DISTINCT category) FROM cat_mem;");

                var topSb = new StringBuilder(384);
                try
                {
                    using (var st = conn.Prepare("SELECT category, COUNT(*) FROM cat_mem GROUP BY category ORDER BY COUNT(*) DESC LIMIT 10"))
                    {
                        for (;;)
                        {
                            int rc = st.Step();
                            if (rc == VpbSqlite3.SqliteDone) break;
                            if (rc != VpbSqlite3.SqliteRow) break;
                            if (topSb.Length > 0) topSb.Append("; ");
                            topSb.Append(st.ColumnText(0)).Append("=").Append(st.ColumnText(1));
                        }
                    }
                }
                catch { }

                string journal = "";
                try
                {
                    using (var st = conn.Prepare("PRAGMA journal_mode;"))
                    {
                        if (st.Step() == VpbSqlite3.SqliteRow)
                            journal = st.ColumnText(0) ?? "";
                    }
                }
                catch { }

                long fileLen = -1;
                try
                {
                    if (File.Exists(dbPath))
                        fileLen = new FileInfo(dbPath).Length;
                }
                catch { }

                string ver = MetaGet(conn, "schema_version");
                string scanBin = MetaGet(conn, "scan_binary");
                string catSigStored = MetaGet(conn, "categories_sig");
                int sigLen = catSigStored != null ? catSigStored.Length : 0;

                LogUtil.Log(
                    "[VPB] VpbLocalDatabase: SQLite ready | path=" + dbPath
                    + " | file=" + FormatByteSize(fileLen)
                    + " | journal_mode=" + journal
                    + " | rebuild_ms=" + rebuildElapsedMs
                    + " | snapshot_var_pkgs=" + snapshotPackageCount
                    + " | category_defs=" + categoryDefCount
                    + " | rows_pkg=" + nPkg
                    + " | rows_cat_mem=" + nMem
                    + " | distinct_categories_indexed=" + nCatDistinct
                    + " | meta_schema_version=" + ver
                    + " | meta_scan_binary=" + scanBin
                    + " | meta_categories_sig_len=" + sigLen
                    + " | top_cat_mem_counts=" + (topSb.Length > 0 ? topSb.ToString() : "(none)"));
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] VpbLocalDatabase: SQLite stats query failed: " + ex.Message); } catch { }
            }
        }

        private static void TryAddColumnIgnoreFailure(VpbSqlite3.Connection conn, string alterSql)
        {
            try
            {
                conn.ExecUtf8(alterSql);
            }
            catch
            {
            }
        }

        private static void EnsureSchema(VpbSqlite3.Connection conn)
        {
            conn.ExecUtf8("PRAGMA journal_mode=WAL;");
            conn.ExecUtf8("PRAGMA synchronous=NORMAL;");
            conn.ExecUtf8("PRAGMA foreign_keys=ON;");
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT);" +
                "CREATE TABLE IF NOT EXISTS pkg (uid TEXT PRIMARY KEY, creator TEXT, wtime INTEGER, psize INTEGER);" +
                "CREATE TABLE IF NOT EXISTS cat_mem (category TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, PRIMARY KEY(category, pkg_uid, internal_path));" +
                "CREATE TABLE IF NOT EXISTS pkg_dep (src_uid TEXT NOT NULL, dep_uid TEXT NOT NULL, PRIMARY KEY(src_uid, dep_uid));" +
                "CREATE TABLE IF NOT EXISTS sys_file (cache_key TEXT NOT NULL, path TEXT NOT NULL, wtime INTEGER, size INTEGER, PRIMARY KEY(cache_key, path));" +
                "CREATE TABLE IF NOT EXISTS loose_deps (path TEXT PRIMARY KEY, wtime INTEGER, size INTEGER, deps TEXT);" +
                "CREATE TABLE IF NOT EXISTS loose_vap_gender (path TEXT PRIMARY KEY, wtime INTEGER, size INTEGER, gender INTEGER NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS cleanup_exclude (uid TEXT PRIMARY KEY, added_utc_binary INTEGER NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS cat_filter_state (panel_id TEXT NOT NULL, cat_key TEXT NOT NULL, state_json TEXT NOT NULL, PRIMARY KEY(panel_id, cat_key));" +
                "CREATE INDEX IF NOT EXISTS idx_cm_cat ON cat_mem(category);" +
                "CREATE INDEX IF NOT EXISTS idx_cm_pkg ON cat_mem(pkg_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_pd_src ON pkg_dep(src_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_pd_dep ON pkg_dep(dep_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_sf_key ON sys_file(cache_key);" +
                "CREATE TABLE IF NOT EXISTS cache_usage (cache_path TEXT PRIMARY KEY, hit_count INTEGER NOT NULL DEFAULT 0, last_accessed INTEGER NOT NULL);" +
                "CREATE TABLE IF NOT EXISTS cache_usage_pkg (cache_path TEXT NOT NULL, pkg_uid TEXT NOT NULL, PRIMARY KEY(cache_path, pkg_uid));" +
                "CREATE INDEX IF NOT EXISTS idx_cu_last ON cache_usage(last_accessed);" +
                "CREATE INDEX IF NOT EXISTS idx_cup_pkg ON cache_usage_pkg(pkg_uid);" +
                "CREATE TABLE IF NOT EXISTS item_usage (item_key TEXT PRIMARY KEY, kind TEXT, use_count INTEGER NOT NULL DEFAULT 0, last_used INTEGER NOT NULL);" +
                "CREATE INDEX IF NOT EXISTS idx_iu_count ON item_usage(use_count);" +
                "CREATE INDEX IF NOT EXISTS idx_iu_last ON item_usage(last_used);" +
                "CREATE INDEX IF NOT EXISTS idx_cfs_panel ON cat_filter_state(panel_id);");
            // Once-per-build wipe of pre-deep-sig sys_sig:* rows, gated by sentinel so it survives
            // EnsureSchema's per-connection invocation. Old shallow-mtime sigs could otherwise
            // coincidentally match a new deep-mtime sig and return stale cache rows.
            const string sysSigDeepMigKey = "schema_migration:sys_sig_deep_v1";
            if (string.IsNullOrEmpty(MetaGet(conn, sysSigDeepMigKey)))
            {
                try { conn.ExecUtf8("DELETE FROM meta WHERE k LIKE 'sys_sig:%';"); } catch { }
                try
                {
                    using (var mset = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                    {
                        mset.BindText(1, sysSigDeepMigKey);
                        mset.BindText(2, "1");
                        mset.Step();
                    }
                }
                catch { }
            }

            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN var_path TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE cat_mem ADD COLUMN list_path TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN pctime TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN ictime INTEGER;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE cat_mem ADD COLUMN cloth_attr TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN loaded INTEGER;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN first_scanned INTEGER;");
            try { conn.ExecUtf8("UPDATE pkg SET first_scanned = ifnull(ictime, wtime) WHERE first_scanned IS NULL;"); } catch { }
            EnsureGalleryUserTagTables(conn);
        }


        /// <summary>All indexed gallery rows (category + internal_path) for given package UID.</summary>
        internal static bool TryReadCatMemRowsForPackage(string pkgUid, List<KeyValuePair<string, string>> categoryAndInternalPathOut)
        {
            if (!VpbSqlite3.IsAvailable || categoryAndInternalPathOut == null) return false;
            categoryAndInternalPathOut.Clear();
            if (string.IsNullOrEmpty(pkgUid)) return true;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT category, internal_path FROM cat_mem WHERE pkg_uid=?"))
                    {
                        st.BindText(1, pkgUid);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string cat = st.ColumnText(0) ?? "";
                            string ip = (st.ColumnText(1) ?? "").Replace('\\', '/');
                            if (cat.Length == 0 || ip.Length == 0) continue;
                            categoryAndInternalPathOut.Add(new KeyValuePair<string, string>(cat, ip));
                        }
                    }
                }
                return true;
            }
            catch
            {
                categoryAndInternalPathOut.Clear();
                return false;
            }
        }

        internal static string BuildUsageKey(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                string k = file.Uid;
                if (string.IsNullOrEmpty(k)) k = file.Path;
                if (string.IsNullOrEmpty(k)) return "";
                return (k.Replace('\\', '/').Trim()).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>Upserts <c>item_usage</c> for <paramref name="itemKey"/> (<see cref="BuildUsageKey"/>). Use for explicit user applies only, not scans/thumbnails/deps.</summary>
        internal static void TryRecordItemUse(string itemKey, string kind)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (string.IsNullOrEmpty(itemKey)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare(
                        "INSERT INTO item_usage(item_key, kind, use_count, last_used) VALUES(?, ?, 1, ?) " +
                        "ON CONFLICT(item_key) DO UPDATE SET use_count = use_count + 1, last_used = excluded.last_used, kind = excluded.kind"))
                    {
                        st.BindText(1, itemKey);
                        st.BindText(2, kind ?? "");
                        st.BindInt64(3, DateTime.UtcNow.ToBinary());
                        st.Step();
                    }
                }
                try { MessageKit.post(MessageDef.GalleryItemUsageRecorded); } catch { }
                InvalidateGalleryHistoryModeCountsCache();
                if (LogHistoryRecordWrites)
                {
                    try
                    {
                        LogUtil.Log("[VPB.History] TryRecordItemUse kind=" + (kind ?? "") + " key=" + HistoryDebugTruncate(itemKey));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                if (LogHistoryUsageDebug || LogHistoryRecordWrites)
                {
                    try { LogUtil.LogWarning("[VPB.History] TryRecordItemUse failed: " + ex.Message); } catch { }
                }
            }
        }

        /// <summary>Removes usage rows for the given keys (same strings as <see cref="BuildUsageKey"/>).</summary>
        internal static void TryDeleteItemUsageForKeys(IList<string> itemKeys)
        {
            if (!VpbSqlite3.IsAvailable || itemKeys == null || itemKeys.Count == 0)
            {
                if (LogHistoryUsageDebug)
                {
                    try
                    {
                        LogUtil.Log("[VPB.History] TryDeleteItemUsageForKeys skipped (sqlite_unavailable or empty keys). sqlite=" +
                                    VpbSqlite3.IsAvailable + " count=" + (itemKeys != null ? itemKeys.Count : -1));
                    }
                    catch { }
                }
                return;
            }
            const int MaxVars = 900;
            try
            {
                string dbLabel = "";
                try { dbLabel = Path.GetFileName(GetLocalDatabasePathForDiagnostics()); } catch { dbLabel = "?"; }

                if (LogHistoryUsageDebug)
                {
                    try
                    {
                        var sbKeys = new StringBuilder(itemKeys.Count * 48);
                        int maxKeys = Math.Min(itemKeys.Count, 12);
                        for (int i = 0; i < maxKeys; i++)
                        {
                            if (i > 0) sbKeys.Append(" | ");
                            sbKeys.Append(HistoryDebugTruncate(itemKeys[i] ?? ""));
                        }
                        if (itemKeys.Count > maxKeys) sbKeys.Append(" …(+" + (itemKeys.Count - maxKeys) + " more)");
                        LogUtil.Log("[VPB.History] TryDeleteItemUsageForKeys db=" + dbLabel + " batch_keys=" + itemKeys.Count + " sample=" + sbKeys);
                    }
                    catch { }
                }

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    long totalBefore = 0;
                    long totalAfter = 0;
                    for (int start = 0; start < itemKeys.Count; start += MaxVars)
                    {
                        int n = Math.Min(MaxVars, itemKeys.Count - start);
                        long before = CountItemUsageKeysPresent(conn, itemKeys, start, n);
                        if (before >= 0) totalBefore += before;

                        var sb = new StringBuilder(48 + n * 2);
                        sb.Append("DELETE FROM item_usage WHERE item_key IN (");
                        for (int i = 0; i < n; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append('?');
                        }
                        sb.Append(')');
                        using (var st = conn.Prepare(sb.ToString()))
                        {
                            for (int i = 0; i < n; i++)
                                st.BindText(i + 1, itemKeys[start + i] ?? "");
                            st.Step();
                        }

                        long after = CountItemUsageKeysPresent(conn, itemKeys, start, n);
                        if (after >= 0) totalAfter += after;

                        if (LogHistoryUsageDebug)
                        {
                            try
                            {
                                LogUtil.Log("[VPB.History] TryDeleteItemUsageForKeys batch start=" + start + " n=" + n +
                                            " matched_before_delete=" + before + " matched_after_delete=" + after);
                            }
                            catch { }
                        }
                    }

                    if (LogHistoryUsageDebug)
                    {
                        try
                        {
                            LogUtil.Log("[VPB.History] TryDeleteItemUsageForKeys done db=" + dbLabel +
                                        " total_keys_requested=" + itemKeys.Count +
                                        " sum_matched_before=" + totalBefore + " sum_still_present_after=" + totalAfter +
                                        " (if matched_before=0 keys do not match DB; if after>0 DELETE missed)");
                        }
                        catch { }
                    }
                }
                InvalidateGalleryHistoryModeCountsCache();
            }
            catch (Exception ex)
            {
                if (LogHistoryUsageDebug)
                {
                    try { LogUtil.LogWarning("[VPB.History] TryDeleteItemUsageForKeys exception: " + ex.Message); } catch { }
                }
            }
        }

        internal static bool TryReadItemUseCountsForKeys(IList<string> keys, Dictionary<string, int> outCounts)
        {
            if (outCounts == null) return false;
            outCounts.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            if (keys == null || keys.Count == 0) return true;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    const int MaxVars = 900;
                    for (int start = 0; start < keys.Count; start += MaxVars)
                    {
                        int n = Math.Min(MaxVars, keys.Count - start);
                        var sb = new StringBuilder(64 + n * 3);
                        sb.Append("SELECT item_key, use_count FROM item_usage WHERE item_key IN (");
                        for (int i = 0; i < n; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append("?");
                        }
                        sb.Append(")");

                        using (var st = conn.Prepare(sb.ToString()))
                        {
                            for (int i = 0; i < n; i++)
                            {
                                string k = keys[start + i] ?? "";
                                st.BindText(i + 1, k);
                            }

                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string k = st.ColumnText(0) ?? "";
                                int c = 0;
                                try { c = (int)st.ColumnInt64(1); } catch { c = 0; }
                                if (!string.IsNullOrEmpty(k)) outCounts[k] = c;
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                outCounts.Clear();
                return false;
            }
        }

        private static int _galleryHistoryModeCountsCacheGen;
        private static int _galleryHistoryModeCountsCacheValidGen = -1;
        private static Dictionary<GalleryHistoryFilterMode, int> _galleryHistoryModeCountsCache;

        internal static void InvalidateGalleryHistoryModeCountsCache()
        {
            Interlocked.Increment(ref _galleryHistoryModeCountsCacheGen);
        }

        /// <summary>Fast path for History side tabs: returns last aggregated counts without hitting SQLite.</summary>
        internal static bool TryGetGalleryHistoryModeCountsCached(Dictionary<GalleryHistoryFilterMode, int> outCounts)
        {
            if (outCounts == null) return false;
            outCounts.Clear();
            if (!VpbSqlite3.IsAvailable) return false;

            int gen = Interlocked.CompareExchange(ref _galleryHistoryModeCountsCacheGen, 0, 0);
            var cache = _galleryHistoryModeCountsCache;
            if (cache == null || _galleryHistoryModeCountsCacheValidGen != gen)
                return false;

            foreach (var kv in cache)
                outCounts[kv.Key] = kv.Value;
            return true;
        }

        internal static bool TryReadGalleryHistoryModeCounts(Dictionary<GalleryHistoryFilterMode, int> outCounts)
        {
            if (outCounts == null) return false;
            outCounts.Clear();
            if (!VpbSqlite3.IsAvailable) return false;

            if (TryGetGalleryHistoryModeCountsCached(outCounts))
                return true;

            int genAtStart = Interlocked.CompareExchange(ref _galleryHistoryModeCountsCacheGen, 0, 0);
            if (!TryReadGalleryHistoryModeCountsUncached(outCounts))
                return false;

            if (genAtStart == Interlocked.CompareExchange(ref _galleryHistoryModeCountsCacheGen, 0, 0))
            {
                _galleryHistoryModeCountsCache = new Dictionary<GalleryHistoryFilterMode, int>(outCounts);
                _galleryHistoryModeCountsCacheValidGen = genAtStart;
            }
            return true;
        }

        private static bool TryReadGalleryHistoryModeCountsUncached(Dictionary<GalleryHistoryFilterMode, int> outCounts)
        {
            if (outCounts == null) return false;
            outCounts.Clear();
            if (!VpbSqlite3.IsAvailable) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    string internalPathExpr = GalleryHistoryResolvedInternalPathSql();
                    const string miscKindList = "'scene','appearance','clothing','hair','plugins','pose','skin','morphs'";
                    var sb = new StringBuilder(1024);
                    sb.Append("SELECT ");
                    sb.Append("COUNT(DISTINCT i.item_key), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind = 'scene' THEN i.item_key END), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind = 'appearance' THEN i.item_key END), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind = 'clothing' THEN i.item_key END), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind = 'hair' THEN i.item_key END), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind = 'plugins' THEN i.item_key END), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind = 'pose' THEN i.item_key END), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind IN ('skin','morphs') THEN i.item_key END), ");
                    sb.Append("COUNT(DISTINCT CASE WHEN i.kind NOT IN (").Append(miscKindList).Append(") THEN i.item_key END) ");
                    AppendGalleryHistoryJoinFromWhere(sb);
                    sb.Append(" AND length(trim(ifnull(p.uid,''))) > 0");
                    sb.Append(" AND length(trim(").Append(internalPathExpr).Append(")) > 0");

                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        if (st.Step() != VpbSqlite3.SqliteRow)
                            return false;

                        int all = (int)Math.Min(Math.Max(st.ColumnInt64(0), 0), int.MaxValue);
                        int scenes = (int)Math.Min(Math.Max(st.ColumnInt64(1), 0), int.MaxValue);
                        int appearance = (int)Math.Min(Math.Max(st.ColumnInt64(2), 0), int.MaxValue);
                        int clothing = (int)Math.Min(Math.Max(st.ColumnInt64(3), 0), int.MaxValue);
                        int hair = (int)Math.Min(Math.Max(st.ColumnInt64(4), 0), int.MaxValue);
                        int plugins = (int)Math.Min(Math.Max(st.ColumnInt64(5), 0), int.MaxValue);
                        int pose = (int)Math.Min(Math.Max(st.ColumnInt64(6), 0), int.MaxValue);
                        int body = (int)Math.Min(Math.Max(st.ColumnInt64(7), 0), int.MaxValue);
                        int misc = (int)Math.Min(Math.Max(st.ColumnInt64(8), 0), int.MaxValue);

                        outCounts[GalleryHistoryFilterMode.Recent] = all;
                        outCounts[GalleryHistoryFilterMode.MostUsed] = all;
                        outCounts[GalleryHistoryFilterMode.Scenes] = scenes;
                        outCounts[GalleryHistoryFilterMode.Appearance] = appearance;
                        outCounts[GalleryHistoryFilterMode.Clothing] = clothing;
                        outCounts[GalleryHistoryFilterMode.Hair] = hair;
                        outCounts[GalleryHistoryFilterMode.Plugins] = plugins;
                        outCounts[GalleryHistoryFilterMode.Pose] = pose;
                        outCounts[GalleryHistoryFilterMode.Body] = body;
                        outCounts[GalleryHistoryFilterMode.Misc] = misc;
                    }

                    return true;
                }
            }
            catch
            {
                outCounts.Clear();
                return false;
            }
        }

        internal struct ItemUsageSnapshot
        {
            public string ItemKey;
            public string Kind;
            public int UseCount;
            public long LastUsed;
        }

        internal static bool TryReadItemUsageSnapshotsForKeys(IList<string> keys, Dictionary<string, ItemUsageSnapshot> outSnapshots)
        {
            if (outSnapshots == null) return false;
            outSnapshots.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            if (keys == null || keys.Count == 0) return true;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    const int MaxVars = 900;
                    for (int start = 0; start < keys.Count; start += MaxVars)
                    {
                        int n = Math.Min(MaxVars, keys.Count - start);
                        var sb = new StringBuilder(96 + n * 3);
                        sb.Append("SELECT item_key, ifnull(kind,''), use_count, last_used FROM item_usage WHERE item_key IN (");
                        for (int i = 0; i < n; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append("?");
                        }
                        sb.Append(")");

                        using (var st = conn.Prepare(sb.ToString()))
                        {
                            for (int i = 0; i < n; i++)
                                st.BindText(i + 1, keys[start + i] ?? "");

                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string itemKey = st.ColumnText(0) ?? "";
                                if (string.IsNullOrEmpty(itemKey)) continue;

                                string kind = st.ColumnText(1) ?? "";
                                int useCount = 0;
                                long lastUsed = 0;
                                try { useCount = (int)st.ColumnInt64(2); } catch { useCount = 0; }
                                try { lastUsed = st.ColumnInt64(3); } catch { lastUsed = 0; }
                                if (useCount < 1) useCount = 1;

                                outSnapshots[itemKey] = new ItemUsageSnapshot
                                {
                                    ItemKey = itemKey,
                                    Kind = kind,
                                    UseCount = useCount,
                                    LastUsed = lastUsed,
                                };
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                outSnapshots.Clear();
                return false;
            }
        }

        internal static bool TryRestoreItemUsageSnapshots(IList<ItemUsageSnapshot> snapshots)
        {
            if (!VpbSqlite3.IsAvailable) return false;
            if (snapshots == null || snapshots.Count == 0) return true;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var st = conn.Prepare("INSERT OR REPLACE INTO item_usage(item_key, kind, use_count, last_used) VALUES(?, ?, ?, ?)"))
                        {
                            for (int i = 0; i < snapshots.Count; i++)
                            {
                                ItemUsageSnapshot snap = snapshots[i];
                                if (string.IsNullOrEmpty(snap.ItemKey)) continue;

                                st.Reset();
                                st.BindText(1, snap.ItemKey);
                                st.BindText(2, snap.Kind ?? "");
                                st.BindInt64(3, snap.UseCount > 0 ? snap.UseCount : 1);
                                st.BindInt64(4, snap.LastUsed);
                                st.Step();
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                        InvalidateGalleryHistoryModeCountsCache();
                        return true;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void TrySaveCategoryFilterState(string panelId, string catKey, string stateJson)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (string.IsNullOrEmpty(panelId) || string.IsNullOrEmpty(catKey) || stateJson == null) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("INSERT OR REPLACE INTO cat_filter_state(panel_id,cat_key,state_json) VALUES(?,?,?)"))
                    {
                        st.BindText(1, panelId);
                        st.BindText(2, catKey);
                        st.BindText(3, stateJson);
                        st.Step();
                    }
                }
            }
            catch { }
        }

        internal static bool TryLoadCategoryFilterState(string panelId, string catKey, out string stateJson)
        {
            stateJson = null;
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(panelId) || string.IsNullOrEmpty(catKey)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT state_json FROM cat_filter_state WHERE panel_id=? AND cat_key=?"))
                    {
                        st.BindText(1, panelId);
                        st.BindText(2, catKey);
                        if (st.Step() != VpbSqlite3.SqliteRow) return false;
                        stateJson = st.ColumnText(0);
                        return stateJson != null;
                    }
                }
            }
            catch { return false; }
        }

        internal static void TryDeleteAllCategoryFilterStates(string panelId)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(panelId)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("DELETE FROM cat_filter_state WHERE panel_id=?"))
                    {
                        st.BindText(1, panelId);
                        st.Step();
                    }
                }
            }
            catch { }
        }

        internal static bool TryIsCleanupExcluded(string uid)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(uid)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT 1 FROM cleanup_exclude WHERE uid=? LIMIT 1"))
                    {
                        st.BindText(1, uid);
                        return st.Step() == VpbSqlite3.SqliteRow;
                    }
                }
            }
            catch { return false; }
        }

        internal static void TryReadCleanupExcludedUids(HashSet<string> outUids)
        {
            if (outUids == null) return;
            outUids.Clear();
            if (!VpbSqlite3.IsAvailable) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT uid FROM cleanup_exclude"))
                    {
                        for (;;)
                        {
                            int rc = st.Step();
                            if (rc == VpbSqlite3.SqliteDone) break;
                            if (rc != VpbSqlite3.SqliteRow) break;
                            string uid = st.ColumnText(0);
                            if (!string.IsNullOrEmpty(uid))
                                outUids.Add(uid);
                        }
                    }
                }
            }
            catch { }
        }

        internal static void TryAddCleanupExcludes(IList<string> uids)
        {
            if (!VpbSqlite3.IsAvailable || uids == null || uids.Count == 0) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var st = conn.Prepare("INSERT OR REPLACE INTO cleanup_exclude(uid,added_utc_binary) VALUES(?,?)"))
                        {
                            long now = DateTime.UtcNow.ToBinary();
                            for (int i = 0; i < uids.Count; i++)
                            {
                                string uid = uids[i];
                                if (string.IsNullOrEmpty(uid)) continue;
                                st.Reset();
                                st.BindText(1, uid);
                                st.BindInt64(2, now);
                                st.Step();
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                    }
                }
            }
            catch { }
        }

        internal static void TryRemoveCleanupExcludes(IList<string> uids)
        {
            if (!VpbSqlite3.IsAvailable || uids == null || uids.Count == 0) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var st = conn.Prepare("DELETE FROM cleanup_exclude WHERE uid=?"))
                        {
                            for (int i = 0; i < uids.Count; i++)
                            {
                                string uid = uids[i];
                                if (string.IsNullOrEmpty(uid)) continue;
                                st.Reset();
                                st.BindText(1, uid);
                                st.Step();
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                    }
                }
            }
            catch { }
        }

        internal static void TryRecordCacheHit(string cachePath)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(cachePath)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("INSERT INTO cache_usage(cache_path, hit_count, last_accessed) VALUES(?, 1, ?) ON CONFLICT(cache_path) DO UPDATE SET hit_count = hit_count + 1, last_accessed = excluded.last_accessed"))
                    {
                        st.BindText(1, cachePath);
                        st.BindInt64(2, DateTime.UtcNow.ToBinary());
                        st.Step();
                    }
                }
            }
            catch { }
        }

        internal static void TryRecordCacheHitsBatch(IEnumerable<string> cachePaths)
        {
            if (!VpbSqlite3.IsAvailable || cachePaths == null) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var st = conn.Prepare("INSERT INTO cache_usage(cache_path, hit_count, last_accessed) VALUES(?, 1, ?) ON CONFLICT(cache_path) DO UPDATE SET hit_count = hit_count + 1, last_accessed = excluded.last_accessed"))
                        {
                            long now = DateTime.UtcNow.ToBinary();
                            foreach (var path in cachePaths)
                            {
                                if (string.IsNullOrEmpty(path)) continue;
                                st.Reset();
                                st.BindText(1, path);
                                st.BindInt64(2, now);
                                st.Step();
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch { }
        }

        internal struct CacheUsagePkgRow
        {
            public string CachePath;
            public string PackageUid;
        }

        internal static void TryRecordCacheUsagePackagesBatch(IEnumerable<CacheUsagePkgRow> rows)
        {
            if (!VpbSqlite3.IsAvailable || rows == null) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var st = conn.Prepare("INSERT OR IGNORE INTO cache_usage_pkg(cache_path, pkg_uid) VALUES(?, ?)"))
                        {
                            foreach (var r in rows)
                            {
                                if (string.IsNullOrEmpty(r.CachePath) || string.IsNullOrEmpty(r.PackageUid)) continue;
                                st.Reset();
                                st.BindText(1, r.CachePath);
                                st.BindText(2, r.PackageUid);
                                st.Step();
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch { }
        }

        internal static void TryGetCacheUsagePackages(string cachePath, List<string> outUids)
        {
            if (outUids == null) return;
            outUids.Clear();
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(cachePath)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT pkg_uid FROM cache_usage_pkg WHERE cache_path = ?"))
                    {
                        st.BindText(1, cachePath);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string uid = st.ColumnText(0);
                            if (!string.IsNullOrEmpty(uid)) outUids.Add(uid);
                        }
                    }
                }
            }
            catch { outUids.Clear(); }
        }

        internal struct CacheUsageRow
        {
            public string CachePath;
            public int HitCount;
            public long LastAccessedBinary;
        }

        internal static void TryGetStaleCacheItems(long olderThanBinary, int maxHits, List<CacheUsageRow> outRows)
        {
            if (outRows == null) return;
            outRows.Clear();
            if (!VpbSqlite3.IsAvailable) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    // NOTE: stale-cache selection is date-based only; hit_count is tracked but not used for staleness.
                    using (var st = conn.Prepare("SELECT cache_path, hit_count, last_accessed FROM cache_usage WHERE last_accessed < ?"))
                    {
                        st.BindInt64(1, olderThanBinary);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            outRows.Add(new CacheUsageRow
                            {
                                CachePath = st.ColumnText(0),
                                HitCount = (int)st.ColumnInt64(1),
                                LastAccessedBinary = st.ColumnInt64(2)
                            });
                        }
                    }
                }
            }
            catch { }
        }

        internal static void TryDeleteCacheUsage(string cachePath)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(cachePath)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    try
                    {
                        using (var st2 = conn.Prepare("DELETE FROM cache_usage_pkg WHERE cache_path = ?"))
                        {
                            st2.BindText(1, cachePath);
                            st2.Step();
                        }
                    }
                    catch { }
                    using (var st = conn.Prepare("DELETE FROM cache_usage WHERE cache_path = ?"))
                    {
                        st.BindText(1, cachePath);
                        st.Step();
                    }
                }
            }
            catch { }
        }

        internal struct SystemFileRow
        {
            public string Path;
            public long LastWriteBinaryOrInvalid;
            public long SizeOrInvalid;
        }

        private static string SysSigMetaKey(string cacheKey)
        {
            // meta.k is a primary key; keep it short and stable even if cacheKey is long.
            return "sys_sig:" + HashFnv1a64Hex(cacheKey ?? "");
        }

        private static string HashFnv1a64Hex(string s)
        {
            unchecked
            {
                const ulong Offset = 14695981039346656037UL;
                const ulong Prime = 1099511628211UL;
                ulong h = Offset;
                if (!string.IsNullOrEmpty(s))
                {
                    for (int i = 0; i < s.Length; i++)
                    {
                        h ^= (ushort)s[i];
                        h *= Prime;
                    }
                }
                return h.ToString("x16");
            }
        }

        /// <summary>
        /// Recursive max(mtime) across <paramref name="root"/> and all subdirectories. Used by sys_file cache
        /// signature builders: callers walk file trees with SearchOption.AllDirectories, but the top-level
        /// dir's mtime does not update when files are added to deep subfolders, so a shallow sig would let
        /// the cache return stale rows. Walking GetDirectories(AllDirectories) costs one directory enumeration
        /// per scan but is far cheaper than the file enumeration the cache is protecting against.
        /// </summary>
        internal static long DeepMaxDirMtimeBinary(string root)
        {
            long max = 0;
            if (string.IsNullOrEmpty(root)) return max;
            try
            {
                if (!Directory.Exists(root)) return max;
                try
                {
                    long rootM = Directory.GetLastWriteTimeUtc(root).ToBinary();
                    if (rootM > max) max = rootM;
                }
                catch { }

                string[] dirs;
                try { dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
                catch { dirs = null; }

                if (dirs != null)
                {
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        try
                        {
                            long m = Directory.GetLastWriteTimeUtc(dirs[i]).ToBinary();
                            if (m > max) max = m;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return max;
        }

        internal static bool TryReadSystemFilesForCacheKey(string cacheKey, string expectedSig, List<SystemFileRow> outRows)
        {
            outRows.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(cacheKey)) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    // sys_file caches are independent of the gallery VAR index "ready" state.
                    EnsureSchema(conn);
                    string storedSig = MetaGet(conn, SysSigMetaKey(cacheKey));
                    if (!string.Equals(storedSig ?? "", expectedSig ?? "", StringComparison.Ordinal))
                        return false;

                    using (var st = conn.Prepare("SELECT path, wtime, size FROM sys_file WHERE cache_key = ?"))
                    {
                        st.BindText(1, cacheKey);
                        int step;
                        while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                        {
                            SystemFileRow r;
                            r.Path = st.ColumnText(0) ?? "";
                            r.LastWriteBinaryOrInvalid = long.MinValue;
                            r.SizeOrInvalid = long.MinValue;
                            long wt, sz;
                            string wtxt = st.ColumnText(1);
                            string sztxt = st.ColumnText(2);
                            if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wt)) r.LastWriteBinaryOrInvalid = wt;
                            if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out sz)) r.SizeOrInvalid = sz;
                            if (!string.IsNullOrEmpty(r.Path)) outRows.Add(r);
                        }
                    }
                }
                return true;
            }
            catch
            {
                outRows.Clear();
                return false;
            }
        }

        internal static void TryWriteSystemFilesForCacheKey(string cacheKey, string sig, List<SystemFileRow> rows)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (string.IsNullOrEmpty(cacheKey)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var del = conn.Prepare("DELETE FROM sys_file WHERE cache_key = ?"))
                        {
                            del.BindText(1, cacheKey);
                            del.Step();
                        }
                        if (rows != null && rows.Count > 0)
                        {
                            using (var ins = conn.Prepare("INSERT OR REPLACE INTO sys_file(cache_key,path,wtime,size) VALUES(?,?,?,?)"))
                            {
                                for (int i = 0; i < rows.Count; i++)
                                {
                                    var r = rows[i];
                                    if (string.IsNullOrEmpty(r.Path)) continue;
                                    ins.BindText(1, cacheKey);
                                    ins.BindText(2, r.Path);
                                    ins.BindText(3, r.LastWriteBinaryOrInvalid.ToString());
                                    ins.BindText(4, r.SizeOrInvalid.ToString());
                                    ins.Step();
                                    ins.Reset();
                                }
                            }
                        }
                        using (var up = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                        {
                            up.BindText(1, SysSigMetaKey(cacheKey));
                            up.BindText(2, sig ?? "");
                            up.Step();
                        }
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Persistent cache for dependencies extracted from loose .json scene/preset files. Survives VaM restarts so the
        /// 1500ms per-file stream scan only runs once per (path, mtime, size). Returns true when a fresh row matches.
        /// </summary>
        internal static bool TryReadLooseSceneDeps(string filePath, long expectedWtimeBinary, long expectedSize, HashSet<string> outDeps)
        {
            if (outDeps == null) return false;
            outDeps.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT wtime, size, deps FROM loose_deps WHERE path = ?"))
                    {
                        st.BindText(1, filePath);
                        if (st.Step() != VpbSqlite3.SqliteRow) return false;

                        long wt = long.MinValue, sz = long.MinValue;
                        string wtxt = st.ColumnText(0);
                        string sztxt = st.ColumnText(1);
                        if (string.IsNullOrEmpty(wtxt) || !long.TryParse(wtxt, out wt)) return false;
                        if (string.IsNullOrEmpty(sztxt) || !long.TryParse(sztxt, out sz)) return false;
                        if (wt != expectedWtimeBinary || sz != expectedSize) return false;

                        string deps = st.ColumnText(2) ?? "";
                        if (deps.Length == 0) return true;
                        string[] parts = deps.Split('|');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            string p = parts[i];
                            if (!string.IsNullOrEmpty(p)) outDeps.Add(p);
                        }
                        return true;
                    }
                }
            }
            catch
            {
                outDeps.Clear();
                return false;
            }
        }

        internal static void WriteLooseSceneDeps(string filePath, long wtimeBinary, long size, HashSet<string> deps)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                string depsCsv = "";
                if (deps != null && deps.Count > 0)
                {
                    var sb = new StringBuilder(deps.Count * 24);
                    bool first = true;
                    foreach (var d in deps)
                    {
                        if (string.IsNullOrEmpty(d)) continue;
                        // Defensive: drop anything containing the delimiter so round-trip can't corrupt the set.
                        if (d.IndexOf('|') >= 0) continue;
                        if (!first) sb.Append('|');
                        sb.Append(d);
                        first = false;
                    }
                    depsCsv = sb.ToString();
                }
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var ins = conn.Prepare("INSERT OR REPLACE INTO loose_deps(path,wtime,size,deps) VALUES(?,?,?,?)"))
                    {
                        ins.BindText(1, filePath);
                        ins.BindText(2, wtimeBinary.ToString());
                        ins.BindText(3, size.ToString());
                        ins.BindText(4, depsCsv);
                        ins.Step();
                    }
                }
            }
            catch { }
        }

        /// <summary>Per-file gender classification cache for loose .vap appearance presets. Avoids re-streaming the same JSON every refresh.</summary>
        internal static bool TryReadLooseVapGender(string filePath, long expectedWtimeBinary, long expectedSize, out int gender)
        {
            gender = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT wtime, size, gender FROM loose_vap_gender WHERE path = ?"))
                    {
                        st.BindText(1, filePath);
                        if (st.Step() != VpbSqlite3.SqliteRow) return false;
                        long wt, sz; int g;
                        string wtxt = st.ColumnText(0);
                        string sztxt = st.ColumnText(1);
                        string gtxt = st.ColumnText(2);
                        if (string.IsNullOrEmpty(wtxt) || !long.TryParse(wtxt, out wt)) return false;
                        if (string.IsNullOrEmpty(sztxt) || !long.TryParse(sztxt, out sz)) return false;
                        if (string.IsNullOrEmpty(gtxt) || !int.TryParse(gtxt, out g)) return false;
                        if (wt != expectedWtimeBinary || sz != expectedSize) return false;
                        gender = g;
                        return true;
                    }
                }
            }
            catch { gender = 0; return false; }
        }

        internal static void WriteLooseVapGender(string filePath, long wtimeBinary, long size, int gender)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (string.IsNullOrEmpty(filePath)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var ins = conn.Prepare("INSERT OR REPLACE INTO loose_vap_gender(path,wtime,size,gender) VALUES(?,?,?,?)"))
                    {
                        ins.BindText(1, filePath);
                        ins.BindText(2, wtimeBinary.ToString());
                        ins.BindText(3, size.ToString());
                        ins.BindText(4, gender.ToString());
                        ins.Step();
                    }
                }
            }
            catch { }
        }

        /// <summary>DAZCharacter displayName -> isMale map, built from the live DAZCharacterSelector and persisted in meta. Used by the on-disk vap classifier so non-conforming displayName strings still resolve correctly when the user has the pack installed.</summary>
        internal static bool TryReadCharacterGenderDict(out Dictionary<string, bool> outMap)
        {
            outMap = null;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    string raw = MetaGet(conn, "daz_char_dict_v1");
                    if (string.IsNullOrEmpty(raw)) return false;
                    char rs = '\x1E';
                    char us = '\x1F';
                    var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    string[] entries = raw.Split(rs);
                    for (int i = 0; i < entries.Length; i++)
                    {
                        string e = entries[i];
                        if (string.IsNullOrEmpty(e)) continue;
                        int sep = e.IndexOf(us);
                        if (sep <= 0 || sep >= e.Length - 1) continue;
                        map[e.Substring(0, sep)] = (e.Substring(sep + 1) == "1");
                    }
                    outMap = map;
                    return map.Count > 0;
                }
            }
            catch { outMap = null; return false; }
        }

        internal static void WriteCharacterGenderDict(Dictionary<string, bool> map)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (map == null) return;
            try
            {
                char rs = '\x1E';
                char us = '\x1F';
                var sb = new StringBuilder(map.Count * 24);
                bool first = true;
                foreach (var kv in map)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    if (kv.Key.IndexOf(rs) >= 0 || kv.Key.IndexOf(us) >= 0) continue;
                    if (!first) sb.Append(rs);
                    sb.Append(kv.Key).Append(us).Append(kv.Value ? '1' : '0');
                    first = false;
                }
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var ins = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                    {
                        ins.BindText(1, "daz_char_dict_v1");
                        ins.BindText(2, sb.ToString());
                        ins.Step();
                    }
                }
            }
            catch { }
        }

        private static string NormalizeDependencyUidOrPath(string depUidOrPath)
        {
            if (string.IsNullOrEmpty(depUidOrPath)) return "";
            try
            {
                string d = depUidOrPath.Replace('\\', '/');
                int lastSlash = d.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < d.Length) d = d.Substring(lastSlash + 1);
                if (d.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    d = d.Substring(0, d.Length - 4);
                return d ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 1 = loaded: .var under <c>AddonPackages/</c>, or under <c>Custom/</c> / <c>Saves/</c> (treated as always loaded / not AllPackages-style repo).
        /// 0 = unloaded (e.g. <c>AllPackages/</c>, <c>InvalidPackages/</c>, or other roots).
        /// </summary>
        internal static int ComputePackageLoadedFlagFromVarPath(string varPath)
        {
            if (string.IsNullOrEmpty(varPath)) return 0;
            string p = varPath.Replace('\\', '/');
            if (p.Length >= 7 && p.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase)) return 1;
            if (p.Length >= 6 && p.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase)) return 1;
            if (p.Length >= 15 && p.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        /// <summary>
        /// After AllPackages ↔ AddonPackages moves, update <c>pkg.var_path</c> and <c>pkg.loaded</c> for the given UIDs (no full index rebuild).
        /// </summary>
        internal static void TryUpdatePkgPathAndLoadedForUids(ICollection<string> packageUids)
        {
            if (!VpbSqlite3.IsAvailable || packageUids == null || packageUids.Count == 0) return;
            VarPackage[] snapshot;
            try
            {
                lock (FileManager.packagesLock)
                {
                    if (FileManager.PackagesByUid == null) return;
                    var list = new List<VarPackage>(packageUids.Count);
                    foreach (string uid in packageUids)
                    {
                        if (string.IsNullOrEmpty(uid)) continue;
                        VarPackage p;
                        if (FileManager.PackagesByUid.TryGetValue(uid, out p) && p != null)
                            list.Add(p);
                    }
                    snapshot = list.ToArray();
                }
            }
            catch
            {
                return;
            }
            if (snapshot == null || snapshot.Length == 0) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("UPDATE pkg SET var_path=?, loaded=? WHERE uid=?"))
                    {
                        for (int i = 0; i < snapshot.Length; i++)
                        {
                            VarPackage pkg = snapshot[i];
                            if (pkg == null) continue;
                            string uid = pkg.Uid ?? "";
                            if (uid.Length == 0) continue;
                            string vp = pkg.Path ?? "";
                            int ld = ComputePackageLoadedFlagFromVarPath(vp);
                            st.BindText(1, vp);
                            st.BindText(2, ld.ToString());
                            st.BindText(3, uid);
                            st.Step();
                            st.Reset();
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>Precomputes packed clothing metadata once per VAR row at index rebuild (mirrors VAR branch of <see cref="GalleryPanel.PassesClothingGalleryFiltersForPath"/>).</summary>
        internal static int PackClothingGalleryAttrForVarListPath(string listPath)
        {
            if (string.IsNullOrEmpty(listPath)) return 0;
            string p = listPath;
            int sep = p.IndexOf(":/", StringComparison.Ordinal);
            if (sep >= 0 && sep + 2 < p.Length)
                p = p.Substring(sep + 2);
            int lastDot = p.LastIndexOf('.');
            string ext = (lastDot >= 0 && lastDot < p.Length - 1) ? p.Substring(lastDot + 1) : "";
            bool isPreset = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);

            ClothingLoadingUtils.ResourceKind k;
            ClothingLoadingUtils.ResourceGender g;
            ClothingLoadingUtils.ClassifyClothingHairPath(p, out k, out g);
            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(p);

            return ClothingAttrPresentFlag
                | ((int)k & 0xF)
                | (((int)g & 0xF) << 4)
                | (isPreset ? 0x100 : 0)
                | (isDecal ? 0x200 : 0);
        }

        /// <summary>Fast clothing subfilter test using <see cref="PackClothingGalleryAttrForVarListPath"/> output (no path parsing).</summary>
        internal static bool ClothingPackedAttrMatchesSubfilter(int packed, GalleryPanel.ClothingSubfilter clothingSubfilter)
        {
            int kind = packed & 0xF;
            int gender = (packed >> 4) & 0xF;
            bool isPreset = (packed & 0x100) != 0;
            bool isDecal = (packed & 0x200) != 0;

            if (kind != (int)ClothingLoadingUtils.ResourceKind.Clothing) return false;

            // Issue #101: default behavior (no flags set) hides .vap presets so the grid does not
            // show duplicate base + preset pairs. Mirrors GalleryPanel.PassesClothingGalleryFiltersForPath.
            if (clothingSubfilter == 0) return !isPreset;

            const GalleryPanel.ClothingSubfilter Real = GalleryPanel.ClothingSubfilter.RealClothing;
            const GalleryPanel.ClothingSubfilter Dec = GalleryPanel.ClothingSubfilter.Decals;
            const GalleryPanel.ClothingSubfilter Pre = GalleryPanel.ClothingSubfilter.Presets;
            const GalleryPanel.ClothingSubfilter Cus = GalleryPanel.ClothingSubfilter.Custom;
            const GalleryPanel.ClothingSubfilter Itm = GalleryPanel.ClothingSubfilter.Items;
            const GalleryPanel.ClothingSubfilter Mal = GalleryPanel.ClothingSubfilter.Male;
            const GalleryPanel.ClothingSubfilter Fem = GalleryPanel.ClothingSubfilter.Female;

            bool wantsRealType = ((clothingSubfilter & (Real | Pre | Cus | Itm | Mal | Fem)) != 0);
            bool wantsDecalType = ((clothingSubfilter & Dec) != 0);

            bool typeExplicit = ((clothingSubfilter & (Real | Dec)) != 0);
            if (typeExplicit)
            {
                bool okType = (!isDecal && (clothingSubfilter & Real) != 0)
                    || (isDecal && (clothingSubfilter & Dec) != 0);
                if (!okType) return false;
            }
            else
            {
                if (wantsRealType && isDecal && !wantsDecalType) return false;
            }

            bool wantsPresets = (clothingSubfilter & Pre) != 0;
            bool wantsCustom = (clothingSubfilter & Cus) != 0;
            if (wantsPresets) { if (!isPreset) return false; }
            if (wantsCustom) return false; // VAR rows: isCustomLoose is always false
            // Default-hide presets unless Presets/Custom toggle is on.
            if (!wantsPresets && !wantsCustom) { if (isPreset) return false; }
            if ((clothingSubfilter & Itm) != 0) { if (isPreset) return false; }
            if ((clothingSubfilter & Mal) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Male && gender != (int)ClothingLoadingUtils.ResourceGender.Unknown) return false; }
            if ((clothingSubfilter & Fem) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Female && gender != (int)ClothingLoadingUtils.ResourceGender.Unknown) return false; }

            return true;
        }

        /// <summary>Fast hair subfilter test using <see cref="PackClothingGalleryAttrForVarListPath"/> output (no path parsing).</summary>
        internal static bool HairPackedAttrMatchesSubfilter(int packed, GalleryPanel.HairSubfilter hairSubfilter)
        {
            int kind = packed & 0xF;
            int gender = (packed >> 4) & 0xF;
            bool isPreset = (packed & 0x100) != 0;

            if (kind != (int)ClothingLoadingUtils.ResourceKind.Hair) return false;

            // Issue #101 parity: default behavior (no flags set) hides .vap presets.
            if (hairSubfilter == 0) return !isPreset;

            const GalleryPanel.HairSubfilter Pre = GalleryPanel.HairSubfilter.Presets;
            const GalleryPanel.HairSubfilter Cus = GalleryPanel.HairSubfilter.Custom;
            const GalleryPanel.HairSubfilter Itm = GalleryPanel.HairSubfilter.Items;
            const GalleryPanel.HairSubfilter Mal = GalleryPanel.HairSubfilter.Male;
            const GalleryPanel.HairSubfilter Fem = GalleryPanel.HairSubfilter.Female;

            bool wantsPresets = (hairSubfilter & Pre) != 0;
            bool wantsCustom = (hairSubfilter & Cus) != 0;
            if (wantsPresets) { if (!isPreset) return false; }
            if (wantsCustom) return false; // VAR rows: isCustomLoose is always false
            if (!wantsPresets && !wantsCustom) { if (isPreset) return false; }
            if ((hairSubfilter & Itm) != 0) { if (isPreset) return false; }
            if ((hairSubfilter & Mal) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Male && gender != (int)ClothingLoadingUtils.ResourceGender.Unknown) return false; }
            if ((hairSubfilter & Fem) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Female && gender != (int)ClothingLoadingUtils.ResourceGender.Unknown) return false; }

            return true;
        }

        /// <summary>
        /// Extra <c>AND ...</c> fragment for <see cref="TryQueryGalleryCategoryRows"/> when Clothing + active subfilter + schema has <c>cloth_attr</c>.
        /// Uses the same packing as <see cref="PackClothingGalleryAttrForVarListPath"/> (bit 31 present, kind, gender nibble, 0x100 preset, 0x200 decal).
        /// </summary>
        private static string BuildClothingSubfilterSqlAnd(
            VpbSqlite3.Connection conn,
            string categoryTitle,
            GalleryPanel.ClothingSubfilter f)
        {
            if (!string.Equals(categoryTitle, "Clothing", StringComparison.OrdinalIgnoreCase)) return "";

            string metaVer = MetaGet(conn, "schema_version");
            int mv;
            if (string.IsNullOrEmpty(metaVer) || !int.TryParse(metaVer, out mv) || mv < SchemaVersion) return "";

            const GalleryPanel.ClothingSubfilter Real = GalleryPanel.ClothingSubfilter.RealClothing;
            const GalleryPanel.ClothingSubfilter Dec = GalleryPanel.ClothingSubfilter.Decals;
            const GalleryPanel.ClothingSubfilter Pre = GalleryPanel.ClothingSubfilter.Presets;
            const GalleryPanel.ClothingSubfilter Cus = GalleryPanel.ClothingSubfilter.Custom;
            const GalleryPanel.ClothingSubfilter Itm = GalleryPanel.ClothingSubfilter.Items;
            const GalleryPanel.ClothingSubfilter Mal = GalleryPanel.ClothingSubfilter.Male;
            const GalleryPanel.ClothingSubfilter Fem = GalleryPanel.ClothingSubfilter.Female;

            const string c = "CAST(ifnull(m.cloth_attr,'0') AS INTEGER)";

            // Issue #101: with no subfilter active, default-hide .vap presets so the grid does not
            // show duplicate base + preset pairs. Mirrors GalleryPanel.PassesClothingGalleryFiltersForPath.
            if (f == 0)
            {
                var sb0 = new StringBuilder(96);
                sb0.Append(" AND (").Append(c).Append(" & 2147483648) <> 0");
                sb0.Append(" AND (").Append(c).Append(" & 15) = 1");
                sb0.Append(" AND (").Append(c).Append(" & 256) = 0");
                return sb0.ToString();
            }

            // VAR rows never satisfy Custom alone (or with Presets, etc.); SQL path is VAR-only here.
            if ((f & Cus) != 0) return " AND (1=0)";
            if (((f & Pre) != 0) && ((f & Itm) != 0)) return " AND (1=0)";
            if (((f & Mal) != 0) && ((f & Fem) != 0)) return " AND (1=0)";

            var sb = new StringBuilder(384);
            sb.Append(" AND (").Append(c).Append(" & 2147483648) <> 0");
            sb.Append(" AND (").Append(c).Append(" & 15) = 1");

            bool wantsRealType = ((f & (Real | Pre | Cus | Itm | Mal | Fem)) != 0);
            bool wantsDecalType = ((f & Dec) != 0);
            bool typeExplicit = ((f & (Real | Dec)) != 0);
            if (typeExplicit)
            {
                if (((f & Real) != 0) && ((f & Dec) == 0))
                    sb.Append(" AND (").Append(c).Append(" & 512) = 0");
                else if (((f & Dec) != 0) && ((f & Real) == 0))
                    sb.Append(" AND (").Append(c).Append(" & 512) <> 0");
            }
            else if (wantsRealType && !wantsDecalType)
                sb.Append(" AND (").Append(c).Append(" & 512) = 0");

            if ((f & Pre) != 0)
                sb.Append(" AND (").Append(c).Append(" & 256) <> 0");
            else if ((f & Cus) == 0)
                // Default-hide presets unless Presets/Custom toggle is on.
                sb.Append(" AND (").Append(c).Append(" & 256) = 0");
            if ((f & Itm) != 0)
                sb.Append(" AND (").Append(c).Append(" & 256) = 0");
            if ((f & Mal) != 0)
                sb.Append(" AND ( (((").Append(c).Append(") & 240) / 16) = 2 OR (((").Append(c).Append(") & 240) / 16) = 0 )");
            if ((f & Fem) != 0)
                sb.Append(" AND ( (((").Append(c).Append(") & 240) / 16) = 1 OR (((").Append(c).Append(") & 240) / 16) = 0 )");

            return sb.ToString();
        }

        /// <summary>Extra <c>AND ...</c> fragment for <see cref="TryReadTagCounts"/> when Hair + subfilter + schema has <c>cloth_attr</c>.</summary>
        private static string BuildHairSubfilterSqlAnd(
            VpbSqlite3.Connection conn,
            string categoryTitle,
            GalleryPanel.HairSubfilter f)
        {
            if (!string.Equals(categoryTitle, "Hair", StringComparison.OrdinalIgnoreCase)) return "";

            string metaVer = MetaGet(conn, "schema_version");
            int mv;
            if (string.IsNullOrEmpty(metaVer) || !int.TryParse(metaVer, out mv) || mv < SchemaVersion) return "";

            const GalleryPanel.HairSubfilter Pre = GalleryPanel.HairSubfilter.Presets;
            const GalleryPanel.HairSubfilter Cus = GalleryPanel.HairSubfilter.Custom;
            const GalleryPanel.HairSubfilter Itm = GalleryPanel.HairSubfilter.Items;
            const GalleryPanel.HairSubfilter Mal = GalleryPanel.HairSubfilter.Male;
            const GalleryPanel.HairSubfilter Fem = GalleryPanel.HairSubfilter.Female;

            const string c = "CAST(ifnull(m.cloth_attr,'0') AS INTEGER)";

            // Note: This SQL fragment is used by TryReadTagCounts (facet counters).
            // Do NOT default-hide presets when f==0; UI needs correct Presets facet count even when
            // presets are hidden in file grid by default.
            if (f == 0)
            {
                var sb0 = new StringBuilder(96);
                sb0.Append(" AND (").Append(c).Append(" & 2147483648) <> 0");
                sb0.Append(" AND (").Append(c).Append(" & 15) = 2");
                return sb0.ToString();
            }

            // VAR rows never satisfy Custom.
            if ((f & Cus) != 0) return " AND (1=0)";
            if (((f & Pre) != 0) && ((f & Itm) != 0)) return " AND (1=0)";
            if (((f & Mal) != 0) && ((f & Fem) != 0)) return " AND (1=0)";

            var sb = new StringBuilder(256);
            sb.Append(" AND (").Append(c).Append(" & 2147483648) <> 0");
            sb.Append(" AND (").Append(c).Append(" & 15) = 2");

            if ((f & Pre) != 0)
                sb.Append(" AND (").Append(c).Append(" & 256) <> 0");
            else if ((f & Cus) == 0)
                sb.Append(" AND (").Append(c).Append(" & 256) = 0");
            if ((f & Itm) != 0)
                sb.Append(" AND (").Append(c).Append(" & 256) = 0");
            if ((f & Mal) != 0)
                sb.Append(" AND ( (((").Append(c).Append(") & 240) / 16) = 2 OR (((").Append(c).Append(") & 240) / 16) = 0 )");
            if ((f & Fem) != 0)
                sb.Append(" AND ( (((").Append(c).Append(") & 240) / 16) = 1 OR (((").Append(c).Append(") & 240) / 16) = 0 )");

            return sb.ToString();
        }

        internal static string LastErrorForDiagnostics { get { return s_LastError; } }

        internal static void InvalidateReadyStateOnCategoriesChanged()
        {
            Gallery g = Gallery.singleton;
            lock (s_Sync)
            {
                if (g == null)
                {
                    s_ReadyCategoriesSig = null;
                    s_ReadyScanBinary = long.MinValue;
                    s_ReadyPkgInvSig = null;
                    return;
                }
                string newSig = BuildCategoriesSignature(g.CloneCategoriesForIndex());
                long scanBin = 0;
                try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }
                if (!string.IsNullOrEmpty(s_ReadyCategoriesSig)
                    && string.Equals(s_ReadyCategoriesSig, newSig, StringComparison.Ordinal)
                    && scanBin != 0
                    && s_ReadyScanBinary == scanBin)
                {
                    return;
                }
                s_ReadyCategoriesSig = null;
                s_ReadyScanBinary = long.MinValue;
                s_ReadyPkgInvSig = null;
            }
        }

        /// <summary>
        /// Fingerprint of installed VAR UIDs (order-independent). Used to reuse an on-disk index across
        /// launches even though <see cref="FileManager.lastPackageRefreshTime"/> is a wall clock, not content-derived.
        /// </summary>
        internal static string ComputePackageInventorySignature(Dictionary<string, VarPackage> packagesByUid)
        {
            if (packagesByUid == null || packagesByUid.Count == 0) return "0";
            var uids = new List<string>(packagesByUid.Count);
            foreach (KeyValuePair<string, VarPackage> kv in packagesByUid)
            {
                VarPackage p = kv.Value;
                if (p == null) continue;
                string u = p.Uid;
                if (!string.IsNullOrEmpty(u)) uids.Add(u);
            }
            uids.Sort(StringComparer.Ordinal);
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < uids.Count; i++)
                {
                    string s = uids[i];
                    for (int j = 0; j < s.Length; j++)
                    {
                        h ^= s[j];
                        h *= 1099511628211UL;
                    }
                    h ^= 0xFFUL;
                }
                return uids.Count.ToString() + ":" + h.ToString();
            }
        }

        private static string GetCachedPackageInventorySignature(long scanBin, Dictionary<string, VarPackage> packagesByUid)
        {
            int count = packagesByUid != null ? packagesByUid.Count : 0;
            lock (s_Sync)
            {
                if (scanBin != 0
                    && scanBin == s_CachedInvScanBinary
                    && count == s_CachedInvPkgCount
                    && !string.IsNullOrEmpty(s_CachedInvSig))
                {
                    return s_CachedInvSig;
                }
            }

            string sig = ComputePackageInventorySignature(packagesByUid);
            lock (s_Sync)
            {
                s_CachedInvScanBinary = scanBin;
                s_CachedInvPkgCount = count;
                s_CachedInvSig = sig;
            }
            return sig;
        }

        private static string ComputePackageInventorySignatureFromUids(List<string> uids)
        {
            if (uids == null || uids.Count == 0) return "0";
            uids.Sort(StringComparer.Ordinal);
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < uids.Count; i++)
                {
                    string s = uids[i] ?? "";
                    for (int j = 0; j < s.Length; j++)
                    {
                        h ^= s[j];
                        h *= 1099511628211UL;
                    }
                    h ^= 0xFFUL;
                }
                return uids.Count.ToString() + ":" + h.ToString();
            }
        }

        private static string GetCachedPackageInventorySignatureFromLivePackages(long scanBin, out long invComputeMs)
        {
            invComputeMs = 0;

            Dictionary<string, VarPackage> byUid = null;
            int count = 0;
            lock (FileManager.packagesLock)
            {
                byUid = FileManager.PackagesByUid;
                if (byUid == null) return null;
                count = byUid.Count;
            }

            lock (s_Sync)
            {
                if (scanBin != 0
                    && scanBin == s_CachedInvScanBinary
                    && count == s_CachedInvPkgCount
                    && !string.IsNullOrEmpty(s_CachedInvSig))
                {
                    return s_CachedInvSig;
                }
            }

            // Snapshot only the UIDs under lock (avoid copying the whole dictionary).
            List<string> uids = new List<string>(count);
            lock (FileManager.packagesLock)
            {
                byUid = FileManager.PackagesByUid;
                if (byUid == null) return null;
                foreach (KeyValuePair<string, VarPackage> kv in byUid)
                {
                    VarPackage p = kv.Value;
                    if (p == null) continue;
                    string u = p.Uid;
                    if (!string.IsNullOrEmpty(u)) uids.Add(u);
                }
                count = byUid.Count;
            }

            Stopwatch sw = Stopwatch.StartNew();
            string sig = ComputePackageInventorySignatureFromUids(uids);
            sw.Stop();
            invComputeMs = sw.ElapsedMilliseconds;

            lock (s_Sync)
            {
                s_CachedInvScanBinary = scanBin;
                s_CachedInvPkgCount = count;
                s_CachedInvSig = sig;
            }
            return sig;
        }

        /// <summary>
        /// If the SQLite DB was built for the same category list and the same package set, republish the
        /// in-memory gate using the <em>current</em> package scan stamp so <see cref="TryQueryGalleryCategoryRows"/>
        /// works without a ~20s rebuild (e.g. first Clothing open right after startup).
        /// </summary>
        internal static bool TryRestoreReadyStateIfMetaMatchesInventory()
        {
            if (!VpbSqlite3.IsAvailable || s_RebuildRunning) return false;
            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }
            if (scanBin == 0) return false;

            Gallery g = Gallery.singleton;
            if (g == null) return false;
            List<Gallery.Category> catSnap = g.CloneCategoriesForIndex();
            if (catSnap == null || catSnap.Count == 0) return false;
            string expectSig = BuildCategoriesSignature(catSnap);
            if (string.IsNullOrEmpty(expectSig)) return false;

            Stopwatch sw = Stopwatch.StartNew();
            long metaMs = 0;
            long invMs = 0;
            bool ok = false;
            try
            {
                string metaInv;
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    string metaVer = MetaGet(conn, "schema_version");
                    int mv;
                    if (string.IsNullOrEmpty(metaVer) || !int.TryParse(metaVer, out mv) || mv != SchemaVersion)
                        return false;

                    string metaSig = MetaGet(conn, "categories_sig");
                    if (!string.Equals(metaSig ?? "", expectSig, StringComparison.Ordinal))
                        return false;

                    metaInv = MetaGet(conn, "pkg_inv_sig");
                    if (string.IsNullOrEmpty(metaInv))
                        return false;
                }
                metaMs = sw.ElapsedMilliseconds;
                long invStart = sw.ElapsedMilliseconds;
                long invComputeMs;
                string liveInv = GetCachedPackageInventorySignatureFromLivePackages(scanBin, out invComputeMs);
                invMs = sw.ElapsedMilliseconds - invStart;
                if (string.IsNullOrEmpty(liveInv))
                    return false;
                if (!string.Equals(metaInv, liveInv, StringComparison.Ordinal))
                    return false;

                lock (s_Sync)
                {
                    s_ReadyScanBinary = scanBin;
                    s_ReadyCategoriesSig = expectSig;
                    s_ReadyPkgInvSig = liveInv;
                }
                ok = true;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    sw.Stop();
                    long total = sw.ElapsedMilliseconds;
                    if (total >= 10)
                        LogUtil.Log("[VPB.Gallery.Timing] sqlRestore total=" + total + "ms meta_ms=" + metaMs + " inv_ms=" + invMs + " ok=" + (ok ? "1" : "0"));
                }
                catch { }
            }
        }

        /// <summary>Call after <see cref="FileManager"/> finishes a package scan (same moment gallery caches clear).</summary>
        internal static void ScheduleGalleryIndexRebuildAfterScan()
        {
            if (s_RebuildScheduled || s_RebuildRunning) return;
            s_RebuildScheduled = true;
            ThreadPool.QueueUserWorkItem(_ => RebuildWorker());
        }

        private static void AutoScheduleRebuildIfStale(long scanBin, long readyScan, string catSig)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (s_RebuildScheduled || s_RebuildRunning) return;
            if (scanBin == 0 || scanBin == long.MinValue) return;
            if (readyScan == scanBin && !string.IsNullOrEmpty(catSig)) return;

            // Avoid expensive rebuild when only scan clock advanced.
            // If we already have a ready index for same category signature and same package inventory,
            // bump readyScanBinary to current scanBin and continue without rebuild.
            if (!string.IsNullOrEmpty(catSig))
            {
                string readyInv;
                lock (s_Sync) { readyInv = s_ReadyPkgInvSig; }
                if (!string.IsNullOrEmpty(readyInv))
                {
                    long invComputeMs;
                    string liveInv = GetCachedPackageInventorySignatureFromLivePackages(scanBin, out invComputeMs);
                    if (!string.IsNullOrEmpty(liveInv) && string.Equals(liveInv, readyInv, StringComparison.Ordinal))
                    {
                        lock (s_Sync)
                        {
                            // Ensure we are still talking about same category signature.
                            if (!string.IsNullOrEmpty(s_ReadyCategoriesSig) && string.Equals(s_ReadyCategoriesSig, catSig, StringComparison.Ordinal))
                            {
                                s_ReadyScanBinary = scanBin;
                                // keep s_ReadyPkgInvSig as-is
                            }
                        }
                        try
                        {
                            if (invComputeMs >= 5)
                                LogUtil.Log("[VPB.Gallery.Timing] sqlBumpReadyScan inv_ms=" + invComputeMs + " ok=1");
                        }
                        catch { }
                        return;
                    }
                }
            }
            lock (s_Sync)
            {
                if (s_LastAutoScheduleScanBinary == scanBin) return;
                s_LastAutoScheduleScanBinary = scanBin;
            }
            try { ScheduleGalleryIndexRebuildAfterScan(); } catch { }
        }

        private static void RebuildWorker()
        {
            try
            {
                s_RebuildRunning = true;
                RebuildCore();
            }
            catch (Exception ex)
            {
                s_LastError = ex.Message;
                try { LogUtil.LogError("[VPB] VpbLocalDatabase: gallery index rebuild failed: " + ex); } catch { }
                lock (s_Sync)
                {
                    s_ReadyScanBinary = long.MinValue;
                    s_ReadyCategoriesSig = null;
                    s_ReadyPkgInvSig = null;
                }
            }
            finally
            {
                s_RebuildRunning = false;
                s_RebuildScheduled = false;
            }
        }

        private static string BuildCategoriesSignature(List<Gallery.Category> cats)
        {
            if (cats == null || cats.Count == 0) return "";
            var sb = new StringBuilder(cats.Count * 64);
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i];
                sb.Append(c.name ?? "").Append('\u001E');
                sb.Append(c.extension ?? "").Append('\u001E');
                sb.Append(c.path ?? "").Append('\u001E');
                if (c.paths != null)
                {
                    for (int j = 0; j < c.paths.Count; j++)
                    {
                        sb.Append(c.paths[j] ?? "");
                        sb.Append('\u001F');
                    }
                }
                sb.Append('\u001E');
            }
            return sb.ToString();
        }

        private static bool ExtensionSetsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return true;
            var sa = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(a))
            {
                string[] pa = a.Split('|');
                for (int i = 0; i < pa.Length; i++)
                {
                    string t = pa[i] != null ? pa[i].Trim() : "";
                    if (t.Length > 0) sa.Add(t);
                }
            }
            var sb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(b))
            {
                string[] pb = b.Split('|');
                for (int i = 0; i < pb.Length; i++)
                {
                    string t = pb[i] != null ? pb[i].Trim() : "";
                    if (t.Length > 0) sb.Add(t);
                }
            }
            if (sa.Count != sb.Count) return false;
            foreach (string x in sa)
            {
                if (!sb.Contains(x)) return false;
            }
            return true;
        }

        private static string NormalizeSlashes(string p)
        {
            return string.IsNullOrEmpty(p) ? p : p.Replace('\\', '/');
        }

        private static bool InternalPathStartsWithPrefix(string internalPath, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return true;
            return NormalizeSlashes(internalPath).StartsWith(NormalizeSlashes(prefix), StringComparison.OrdinalIgnoreCase);
        }

        private static bool FileMatchesCategoryExtensions(string internalPath, Gallery.Category cat)
        {
            if (string.IsNullOrEmpty(cat.extension)) return false;
            string entryExt = Path.GetExtension(internalPath);
            if (string.IsNullOrEmpty(entryExt) || entryExt.Length < 2) return false;
            entryExt = entryExt.Substring(1);
            string[] exts = cat.extension.Split('|');
            for (int e = 0; e < exts.Length; e++)
            {
                string ce = exts[e];
                if (string.IsNullOrEmpty(ce)) continue;
                if (string.Equals(entryExt, ce.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool FileMatchesCategoryPath(string internalPath, Gallery.Category cat)
        {
            if (cat.paths != null && cat.paths.Count > 0)
            {
                for (int k = 0; k < cat.paths.Count; k++)
                {
                    string pref = cat.paths[k];
                    if (!string.IsNullOrEmpty(pref) && InternalPathStartsWithPrefix(internalPath, pref))
                        return true;
                }
                return false;
            }
            if (!string.IsNullOrEmpty(cat.path))
                return InternalPathStartsWithPrefix(internalPath, cat.path);
            return true;
        }

        /// <summary>First matching category wins (same spirit as <see cref="GalleryPanel.CacheCategoryCounts"/>).</summary>
        private static string ClassifyFile(List<Gallery.Category> orderedCats, string internalPath)
        {
            if (orderedCats == null) return null;
            for (int ci = 0; ci < orderedCats.Count; ci++)
            {
                Gallery.Category cat = orderedCats[ci];
                if (string.IsNullOrEmpty(cat.name) || string.IsNullOrEmpty(cat.extension)) continue;
                if (!FileMatchesCategoryExtensions(internalPath, cat)) continue;
                if (!FileMatchesCategoryPath(internalPath, cat)) continue;
                return cat.name;
            }
            return null;
        }

        private sealed class CategoryClassifier
        {
            private struct Rule
            {
                public string Name;
                public string SinglePathPrefixNorm; // empty means "any"
                public string[] PathPrefixesNorm;    // null means use SinglePathPrefixNorm
            }

            private readonly Rule[] _rules;
            private readonly Dictionary<string, List<int>> _extToRuleIndices;

            internal CategoryClassifier(List<Gallery.Category> orderedCats)
            {
                if (orderedCats == null) orderedCats = new List<Gallery.Category>();

                _rules = new Rule[orderedCats.Count];
                _extToRuleIndices = new Dictionary<string, List<int>>(64, StringComparer.OrdinalIgnoreCase);

                for (int ci = 0; ci < orderedCats.Count; ci++)
                {
                    Gallery.Category cat = orderedCats[ci];
                    string name = cat.name ?? "";
                    string extPipe = cat.extension ?? "";

                    Rule r = new Rule();
                    r.Name = name;

                    // Normalize path prefixes once (slashes + null handling).
                    if (cat.paths != null && cat.paths.Count > 0)
                    {
                        var pref = new List<string>(cat.paths.Count);
                        for (int j = 0; j < cat.paths.Count; j++)
                        {
                            string p = cat.paths[j];
                            if (string.IsNullOrEmpty(p)) continue;
                            pref.Add(NormalizeSlashes(p));
                        }
                        r.PathPrefixesNorm = pref.Count > 0 ? pref.ToArray() : null;
                        r.SinglePathPrefixNorm = "";
                    }
                    else
                    {
                        r.PathPrefixesNorm = null;
                        r.SinglePathPrefixNorm = string.IsNullOrEmpty(cat.path) ? "" : NormalizeSlashes(cat.path);
                    }
                    _rules[ci] = r;

                    // Map each extension -> ordered list of rule indices.
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(extPipe))
                        continue;
                    string[] exts = extPipe.Split('|');
                    for (int ei = 0; ei < exts.Length; ei++)
                    {
                        string e = exts[ei] != null ? exts[ei].Trim() : "";
                        if (e.Length == 0) continue;
                        List<int> list;
                        if (!_extToRuleIndices.TryGetValue(e, out list) || list == null)
                        {
                            list = new List<int>(8);
                            _extToRuleIndices[e] = list;
                        }
                        // Preserve "first matching category wins" by keeping order.
                        list.Add(ci);
                    }
                }
            }

            internal string Classify(string internalPath)
            {
                if (string.IsNullOrEmpty(internalPath)) return null;

                int dot = internalPath.LastIndexOf('.');
                if (dot < 0 || dot >= internalPath.Length - 1) return null;
                string ext = internalPath.Substring(dot + 1);
                if (string.IsNullOrEmpty(ext)) return null;

                List<int> ruleIdx;
                if (!_extToRuleIndices.TryGetValue(ext, out ruleIdx) || ruleIdx == null || ruleIdx.Count == 0)
                    return null;

                // Normalize the internal path once per file.
                string ipNorm = NormalizeSlashes(internalPath);

                for (int k = 0; k < ruleIdx.Count; k++)
                {
                    int ci = ruleIdx[k];
                    if (ci < 0 || ci >= _rules.Length) continue;
                    Rule r = _rules[ci];
                    if (string.IsNullOrEmpty(r.Name)) continue;

                    // Path rules: if no prefixes are configured, accept any path.
                    if (r.PathPrefixesNorm != null && r.PathPrefixesNorm.Length > 0)
                    {
                        bool ok = false;
                        for (int j = 0; j < r.PathPrefixesNorm.Length; j++)
                        {
                            string pref = r.PathPrefixesNorm[j];
                            if (string.IsNullOrEmpty(pref)) continue;
                            if (ipNorm.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                            {
                                ok = true;
                                break;
                            }
                        }
                        if (!ok) continue;
                    }
                    else if (!string.IsNullOrEmpty(r.SinglePathPrefixNorm))
                    {
                        if (!ipNorm.StartsWith(r.SinglePathPrefixNorm, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    return r.Name;
                }

                return null;
            }
        }

        private static void RebuildCore()
        {
            if (!VpbSqlite3.IsAvailable)
            {
                if (!s_LoggedSqliteUnavailable)
                {
                    s_LoggedSqliteUnavailable = true;
                    try
                    {
                        LogUtil.LogWarning(
                            "[VPB] VpbLocalDatabase: sqlite3.dll could not be loaded (no local DB index). " +
                            "Deploy 64-bit sqlite3.dll to BepInEx\\plugins under the VaM folder (Cache\\VPB is a legacy fallback). " +
                            "Expected DB path (when working): " + GetLocalDatabasePathForDiagnostics());
                    }
                    catch { }
                }
                return;
            }

            Gallery g = Gallery.singleton;
            List<Gallery.Category> catSnap = g != null ? g.CloneCategoriesForIndex() : new List<Gallery.Category>();
            if (catSnap == null) catSnap = new List<Gallery.Category>();

            if (catSnap.Count == 0)
            {
                try
                {
                    using (var conn = new VpbSqlite3.Connection(DbPath))
                    {
                        EnsureSchema(conn);
                    }
                    if (!s_LoggedEmptyCategoriesDb)
                    {
                        s_LoggedEmptyCategoriesDb = true;
                        try
                        {
                            string p = GetLocalDatabasePathForDiagnostics();
                            long sz = -1;
                            try { if (File.Exists(p)) sz = new FileInfo(p).Length; } catch { }
                            LogUtil.Log("[VPB] VpbLocalDatabase: database created (empty categories) at " + p + " | file=" + FormatByteSize(sz));
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    s_LastError = ex.Message;
                    try { LogUtil.LogError("[VPB] VpbLocalDatabase: could not create database: " + ex.Message); } catch { }
                }
                return;
            }

            // Never rebuild against an unstamped clock: DateTime.MinValue.ToBinary() is 0 and would publish
            // s_ReadyScanBinary=0 while real scans use a non-zero stamp — SQL fast path stays disabled forever.
            DateTime refreshClock = DateTime.MinValue;
            try { refreshClock = FileManager.lastPackageRefreshTime; } catch { }
            if (refreshClock == DateTime.MinValue)
                return;

            long scanAtStart = 0;
            try { scanAtStart = refreshClock.ToBinary(); } catch { }
            if (scanAtStart == 0)
                return;

            string catSig = BuildCategoriesSignature(catSnap);
            var classifier = new CategoryClassifier(catSnap);

            Dictionary<string, VarPackage> pkgSnap;
            lock (FileManager.packagesLock)
            {
                if (FileManager.PackagesByUid == null) return;
                pkgSnap = new Dictionary<string, VarPackage>(FileManager.PackagesByUid, StringComparer.OrdinalIgnoreCase);
            }

            string invSigForMeta = ComputePackageInventorySignature(pkgSnap);

            long tsFreq = Stopwatch.Frequency;
            Func<long, long> ticksToMs = t => (t <= 0 || tsFreq <= 0) ? 0 : (long)((t * 1000.0) / tsFreq);

            Stopwatch rebuildSw = Stopwatch.StartNew();
            string rebuildAbortReason = null;
            long tOpenConn = 0;
            long tEnsureSchema = 0;
            long tBulkPragmas = 0;
            long tBegin = 0;
            long tDelete = 0;
            long tPkgRow = 0;
            long tDeps = 0;
            long tEntryData = 0;
            long tCatMem = 0;
            long tCatMemClassify = 0;
            long tCatMemSql = 0;
            long tMeta = 0;
            long tCommit = 0;
            long tDropIdx = 0;
            long tCreateIdx = 0;

            int nPkgInserted = 0;
            int nDepInserted = 0;
            int nCatMemInserted = 0;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    long t0 = Stopwatch.GetTimestamp();
                    tOpenConn += Stopwatch.GetTimestamp() - t0;

                    t0 = Stopwatch.GetTimestamp();
                    EnsureSchema(conn);
                    tEnsureSchema += Stopwatch.GetTimestamp() - t0;

                    // Rebuild-only perf pragmas (connection-local).
                    // Avoids extra temp-file work and increases cache for bulk insert/index build.
                    t0 = Stopwatch.GetTimestamp();
                    try
                    {
                        conn.ExecUtf8(
                            "PRAGMA temp_store=MEMORY;" +
                            "PRAGMA cache_size=-65536;" +          // ~64MiB page cache (negative = KiB)
                            "PRAGMA mmap_size=268435456;");        // 256MiB mmap when supported
                    }
                    catch { }
                    tBulkPragmas += Stopwatch.GetTimestamp() - t0;

                    t0 = Stopwatch.GetTimestamp();
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    tBegin += Stopwatch.GetTimestamp() - t0;
                    try
                    {
                        // Bulk-load optimization: avoid maintaining cat_mem secondary indexes per inserted row.
                        t0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8("DROP INDEX IF EXISTS idx_cm_cat; DROP INDEX IF EXISTS idx_cm_pkg;");
                        tDropIdx += Stopwatch.GetTimestamp() - t0;

                        // Preserve per-uid first_scanned across the DELETE+INSERT rebuild so "Date Added" history survives.
                        var existingFirstScanned = new Dictionary<string, long>(StringComparer.Ordinal);
                        try
                        {
                            using (var sel = conn.Prepare("SELECT uid, first_scanned FROM pkg"))
                            {
                                while (sel.Step() == VpbSqlite3.SqliteRow)
                                {
                                    string uidRow = sel.ColumnText(0);
                                    if (string.IsNullOrEmpty(uidRow)) continue;
                                    long fs = sel.ColumnInt64(1);
                                    existingFirstScanned[uidRow] = fs;
                                }
                            }
                        }
                        catch { existingFirstScanned.Clear(); }

                        t0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8("DELETE FROM cat_mem; DELETE FROM pkg_dep; DELETE FROM pkg;");
                        tDelete += Stopwatch.GetTimestamp() - t0;
                        using (var insPkg = conn.Prepare("INSERT OR REPLACE INTO pkg(uid,creator,wtime,psize,var_path,pctime,ictime,loaded,first_scanned) VALUES(?,?,?,?,?,?,?,?,?)"))
                        using (var insMem = conn.Prepare("INSERT OR IGNORE INTO cat_mem(category,pkg_uid,internal_path,list_path,cloth_attr) VALUES(?,?,?,?,?)"))
                        using (var insDep = conn.Prepare("INSERT OR IGNORE INTO pkg_dep(src_uid,dep_uid) VALUES(?,?)"))
                        {
                            foreach (KeyValuePair<string, VarPackage> kv in pkgSnap)
                            {
                                VarPackage pkg = kv.Value;
                                if (pkg == null) continue;
                                string uid = pkg.Uid ?? "";
                                if (uid.Length == 0) continue;

                                string cr = pkg.Creator ?? "";
                                long wt = DateTime.MinValue.Ticks;
                                try { wt = pkg.LastWriteTime.ToBinary(); } catch { }
                                long sz = 0;
                                try { sz = pkg.Size; } catch { }
                                string varPath = pkg.Path ?? "";
                                string varListPrefix = varPath.Length > 0 ? (varPath + ":/") : ":/";
                                long ct = DateTime.MinValue.Ticks;
                                try { ct = pkg.CreationTime.ToBinary(); } catch { }
                                long ict = long.MinValue;
                                try { ict = pkg.InternalCreationTimeBinary; } catch { ict = long.MinValue; }
                                int loaded = ComputePackageLoadedFlagFromVarPath(varPath);

                                long firstScannedBin;
                                if (!existingFirstScanned.TryGetValue(uid, out firstScannedBin) || firstScannedBin == 0L || firstScannedBin == long.MinValue)
                                {
                                    firstScannedBin = DateTime.UtcNow.ToBinary();
                                }

                                long tPkg0 = Stopwatch.GetTimestamp();
                                insPkg.BindText(1, uid);
                                insPkg.BindText(2, cr);
                                insPkg.BindInt64(3, wt);
                                insPkg.BindInt64(4, sz);
                                insPkg.BindText(5, varPath);
                                insPkg.BindInt64(6, ct);
                                insPkg.BindInt64(7, ict);
                                insPkg.BindInt64(8, loaded);
                                insPkg.BindInt64(9, firstScannedBin);
                                insPkg.Step();
                                insPkg.Reset();
                                tPkgRow += Stopwatch.GetTimestamp() - tPkg0;
                                nPkgInserted++;

                                // Store transitive dependency edges (matches existing RecursivePackageDependencies-based behavior).
                                try
                                {
                                    var deps = pkg.RecursivePackageDependencies;
                                    if (deps != null)
                                    {
                                        long tDep0 = Stopwatch.GetTimestamp();
                                        for (int di = 0; di < deps.Count; di++)
                                        {
                                            string dep = NormalizeDependencyUidOrPath(deps[di]);
                                            if (string.IsNullOrEmpty(dep)) continue;
                                            insDep.BindText(1, uid);
                                            insDep.BindText(2, dep);
                                            insDep.Step();
                                            insDep.Reset();
                                            nDepInserted++;
                                        }
                                        tDeps += Stopwatch.GetTimestamp() - tDep0;
                                    }
                                }
                                catch { }

                                List<string> names;
                                List<long> ticks;
                                List<long> sizes;
                                long tEntry0 = Stopwatch.GetTimestamp();
                                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                                {
                                    tEntryData += Stopwatch.GetTimestamp() - tEntry0;
                                    continue;
                                }
                                tEntryData += Stopwatch.GetTimestamp() - tEntry0;

                                long tMem0 = Stopwatch.GetTimestamp();
                                for (int i = 0; i < names.Count; i++)
                                {
                                    string ip = names[i];
                                    if (string.IsNullOrEmpty(ip)) continue;
                                    long tCls0 = Stopwatch.GetTimestamp();
                                    string cname = classifier.Classify(ip);
                                    tCatMemClassify += Stopwatch.GetTimestamp() - tCls0;

                                    string listPath;
                                    if (string.Equals(ip, "meta.json", StringComparison.OrdinalIgnoreCase))
                                        listPath = varPath;
                                    else
                                        listPath = varListPrefix + ip;

                                    if (!string.IsNullOrEmpty(cname))
                                    {
                                        long clothAttr = 0;
                                        if (string.Equals(cname, "Clothing", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(cname, "Hair", StringComparison.OrdinalIgnoreCase))
                                            clothAttr = PackClothingGalleryAttrForVarListPath(listPath);

                                        long tMemSql0 = Stopwatch.GetTimestamp();
                                        insMem.BindText(1, cname);
                                        insMem.BindText(2, uid);
                                        insMem.BindText(3, ip);
                                        insMem.BindText(4, listPath);
                                        insMem.BindInt64(5, clothAttr);
                                        insMem.Step();
                                        insMem.Reset();
                                        tCatMemSql += Stopwatch.GetTimestamp() - tMemSql0;
                                        nCatMemInserted++;
                                    }

                                    int lastDot = ip.LastIndexOf('.');
                                    if (lastDot > 0 && lastDot < ip.Length - 1)
                                    {
                                        string evExt = ip.Substring(lastDot + 1);
                                        if (Gallery.IsEverythingExcludedPreviewExtension(evExt)) continue;

                                        long tEv0 = Stopwatch.GetTimestamp();
                                        insMem.BindText(1, Gallery.EverythingCategoryName);
                                        insMem.BindText(2, uid);
                                        insMem.BindText(3, ip);
                                        insMem.BindText(4, listPath);
                                        insMem.BindInt64(5, 0);
                                        insMem.Step();
                                        insMem.Reset();
                                        tCatMemSql += Stopwatch.GetTimestamp() - tEv0;
                                        nCatMemInserted++;
                                    }
                                }
                                tCatMem += Stopwatch.GetTimestamp() - tMem0;
                            }
                        }

                        using (var upMeta = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                        {
                            long tMeta0 = Stopwatch.GetTimestamp();
                            upMeta.BindText(1, "schema_version");
                            upMeta.BindText(2, SchemaVersion.ToString());
                            upMeta.Step();
                            upMeta.Reset();

                            upMeta.BindText(1, "categories_sig");
                            upMeta.BindText(2, catSig);
                            upMeta.Step();
                            upMeta.Reset();

                            upMeta.BindText(1, "scan_binary");
                            upMeta.BindText(2, scanAtStart.ToString());
                            upMeta.Step();
                            upMeta.Reset();

                            upMeta.BindText(1, "pkg_inv_sig");
                            upMeta.BindText(2, invSigForMeta);
                            upMeta.Step();
                            upMeta.Reset();
                            tMeta += Stopwatch.GetTimestamp() - tMeta0;
                        }

                        // Recreate indexes after load (much faster than updating incrementally).
                        t0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8(
                            "CREATE INDEX IF NOT EXISTS idx_cm_cat ON cat_mem(category);" +
                            "CREATE INDEX IF NOT EXISTS idx_cm_pkg ON cat_mem(pkg_uid);");
                        tCreateIdx += Stopwatch.GetTimestamp() - t0;

                        long tCommit0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8("COMMIT;");
                        tCommit += Stopwatch.GetTimestamp() - tCommit0;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            finally
            {
                rebuildSw.Stop();
            }

            long scanNow = 0;
            try { scanNow = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }
            if (scanNow != scanAtStart || scanAtStart == 0)
            {
                rebuildAbortReason = "scan_changed";
            }

            string catSigNow = BuildCategoriesSignature(g.CloneCategoriesForIndex());
            if (catSigNow != catSig)
            {
                if (rebuildAbortReason == null) rebuildAbortReason = "categories_changed";
            }

            try
            {
                long totalMs = rebuildSw.ElapsedMilliseconds;
                var sb = new StringBuilder(512);
                sb.Append("[VPB.Gallery.Timing] sqlRebuild ");
                sb.Append(rebuildAbortReason == null ? "DONE" : ("ABORT " + rebuildAbortReason));
                sb.Append(" total=").Append(totalMs).Append("ms");
                sb.Append(" | phases_ms=");
                sb.Append("open=").Append(ticksToMs(tOpenConn)).Append(',');
                sb.Append("schema=").Append(ticksToMs(tEnsureSchema)).Append(',');
                sb.Append("bulkPragmas=").Append(ticksToMs(tBulkPragmas)).Append(',');
                sb.Append("begin=").Append(ticksToMs(tBegin)).Append(',');
                sb.Append("delete=").Append(ticksToMs(tDelete)).Append(',');
                sb.Append("pkg=").Append(ticksToMs(tPkgRow)).Append(',');
                sb.Append("deps=").Append(ticksToMs(tDeps)).Append(',');
                sb.Append("entryData=").Append(ticksToMs(tEntryData)).Append(',');
                sb.Append("catMem=").Append(ticksToMs(tCatMem)).Append(',');
                sb.Append("catMemClassify=").Append(ticksToMs(tCatMemClassify)).Append(',');
                sb.Append("catMemSql=").Append(ticksToMs(tCatMemSql)).Append(',');
                sb.Append("meta=").Append(ticksToMs(tMeta)).Append(',');
                sb.Append("dropIdx=").Append(ticksToMs(tDropIdx)).Append(',');
                sb.Append("createIdx=").Append(ticksToMs(tCreateIdx)).Append(',');
                sb.Append("commit=").Append(ticksToMs(tCommit));
                sb.Append(" | counts=");
                sb.Append("pkg=").Append(nPkgInserted).Append(',');
                sb.Append("dep=").Append(nDepInserted).Append(',');
                sb.Append("catMem=").Append(nCatMemInserted);
                sb.Append(" | snap=");
                sb.Append("cats=").Append(catSnap != null ? catSnap.Count : 0).Append(',');
                sb.Append("pkgs=").Append(pkgSnap != null ? pkgSnap.Count : 0);
                LogUtil.Log(sb.ToString());
            }
            catch { }

            if (rebuildAbortReason != null) return;

            lock (s_Sync)
            {
                s_ReadyScanBinary = scanAtStart;
                s_ReadyCategoriesSig = catSig;
                s_ReadyPkgInvSig = invSigForMeta;
            }

            string dbPathForLog = GetLocalDatabasePathForDiagnostics();
            try
            {
                using (var logConn = new VpbSqlite3.Connection(dbPathForLog))
                    LogLocalDatabaseReadyDetails(logConn, dbPathForLog, rebuildSw.ElapsedMilliseconds, catSnap.Count, pkgSnap.Count);
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] VpbLocalDatabase: SQLite ready but stats log failed: " + ex.Message); } catch { }
            }
        }

        /// <summary>
        /// Fills side-tab category totals from <c>cat_mem</c> when the index matches the current package scan (avoids scanning every VAR on disk).
        /// Only keys already present in <paramref name="countsByCategoryName"/> are updated.
        /// </summary>
        internal static bool TryReadCategoryMemberCounts(
            Dictionary<string, int> countsByCategoryName,
            string creatorFilter = "",
            HashSet<string> activeTags = null,
            string packagePathFilter = "",
            HashSet<string> activeUserTags = null)
        {
            if (!VpbSqlite3.IsAvailable || countsByCategoryName == null || countsByCategoryName.Count == 0) return false;

            long scanBin = DateTime.MinValue.Ticks;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (conn == null) return false;
                    
                    var creatorList = SplitCreatorFilterList(creatorFilter);
                    bool hasCreator = creatorList.Count > 0;
                    string normalizedPackagePathFilter = "";
                    bool hasPackagePathFilter = false;
                    if (!string.IsNullOrEmpty(packagePathFilter))
                    {
                        normalizedPackagePathFilter = packagePathFilter.Replace('\\', '/').Trim().Trim('/');
                        hasPackagePathFilter = normalizedPackagePathFilter.Length > 0;
                    }
                    bool hasTags = activeTags != null && activeTags.Count > 0;
                    var userTagBindNames = new List<string>();
                    
                    var sb = new StringBuilder();
                    sb.Append("SELECT m.category, COUNT(*) FROM cat_mem m");
                    if (hasCreator || hasPackagePathFilter) sb.Append(" INNER JOIN pkg p ON p.uid = m.pkg_uid");
                    
                    sb.Append(" WHERE 1=1");
                    if (hasCreator) AppendCreatorFilterSql(sb, "p.creator", creatorList);
                    if (hasPackagePathFilter)
                        sb.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    
                    List<string> tagsList = null;
                    if (hasTags)
                    {
                        tagsList = new List<string>(activeTags);
                        foreach (var tag in tagsList)
                        {
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                    }
                    AppendSqlActiveUserTagExists(sb, userTagBindNames, activeUserTags, "m");
                    
                    // Issue #101: top-level Clothing/Hair counts represent BASE items (.vam) only,
                    // excluding .vap presets (which are also indexed under those categories).
                    // EVERYTHING must count all indexed rows for that category (including .vap under Clothing/Hair).
                    sb.Append(" AND ((m.category NOT IN ('Clothing','Hair') OR lower(m.internal_path) LIKE '%.vam') OR m.category = ?)");

                    sb.Append(" GROUP BY m.category");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (hasCreator) BindCreatorFilterSql(stmt, ref bind, creatorList);
                        if (hasPackagePathFilter)
                            stmt.BindText(bind++, EscapeLike(normalizedPackagePathFilter.ToLowerInvariant()) + "/%");
                        if (hasTags)
                        {
                            foreach (var tag in tagsList)
                            {
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                            }
                        }
                        for (int ui = 0; ui < userTagBindNames.Count; ui++)
                            stmt.BindText(bind++, userTagBindNames[ui]);
                        stmt.BindText(bind++, Gallery.EverythingCategoryName);

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string cname = stmt.ColumnText(0);
                            int n;
                            if (!int.TryParse(stmt.ColumnText(1), out n)) n = 0;
                            if (countsByCategoryName.ContainsKey(cname))
                                countsByCategoryName[cname] = n;
                        }
                    }

                    // Package-level pseudo-category: ALL VAR (varpkg) is not represented in cat_mem.
                    // Count directly from pkg table so the side-tab count stays correct even when
                    // FileManager.PackagesByUid snapshot is not ready.
                    if (countsByCategoryName.ContainsKey("ALL VAR"))
                    {
                        try
                        {
                            // Mirror GalleryPanel rule: apply package-path filter only when creator filter is active.
                            bool applyPath = hasCreator && hasPackagePathFilter;
                            var sbPkg = new StringBuilder(96);
                            sbPkg.Append("SELECT COUNT(*) FROM pkg p WHERE 1=1");
                            if (hasCreator) AppendCreatorFilterSql(sbPkg, "p.creator", creatorList);
                            if (applyPath)
                                sbPkg.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");

                            using (var stPkg = conn.Prepare(sbPkg.ToString()))
                            {
                                int b2 = 1;
                                if (hasCreator) BindCreatorFilterSql(stPkg, ref b2, creatorList);
                                if (applyPath) stPkg.BindText(b2++, EscapeLike(normalizedPackagePathFilter.ToLowerInvariant()) + "/%");
                                if (stPkg.Step() == VpbSqlite3.SqliteRow)
                                {
                                    int nPkg = 0;
                                    if (!int.TryParse(stPkg.ColumnText(0), out nPkg)) nPkg = (int)stPkg.ColumnInt64(0);
                                    countsByCategoryName["ALL VAR"] = nPkg;
                                }
                            }
                        }
                        catch { /* keep previous value */ }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Side-tab creator file counts from <c>cat_mem</c> + <c>pkg</c> (distinct VAR file rows), using the same extension + path rules as the legacy package scan.
        /// Only files that appear in the index (assigned to at least one category) are counted; unclassified VAR files are excluded.
        /// </summary>
        internal static bool TryReadCreatorFileCounts(
            Dictionary<string, int> countsOut,
            string extensionPipeSeparated,
            List<string> pathPrefixes,
            string singlePathPrefix,
            HashSet<string> activeTags = null,
            string categoryTitle = null,
            string packagePathFilter = null,
            HashSet<string> activeUserTags = null)
        {
            if (!VpbSqlite3.IsAvailable || countsOut == null) return false;
            countsOut.Clear();

            long scanBin = DateTime.MinValue.Ticks;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(extensionPipeSeparated))
            {
                string[] exts2 = extensionPipeSeparated.Split('|');
                for (int i = 0; i < exts2.Length; i++)
                {
                    string e = exts2[i] != null ? exts2[i].Trim() : "";
                    if (e.Length > 0) extSet.Add(e);
                }
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool hasTags = activeTags != null && activeTags.Count > 0;
                    bool hasCat = !string.IsNullOrEmpty(categoryTitle);
                    string normalizedPackagePathFilter = "";
                    bool hasPackagePathFilter = false;
                    if (!string.IsNullOrEmpty(packagePathFilter))
                    {
                        normalizedPackagePathFilter = packagePathFilter.Replace('\\', '/').Trim().Trim('/');
                        hasPackagePathFilter = normalizedPackagePathFilter.Length > 0;
                    }
                    bool hasPathPrefix = (pathPrefixes != null && pathPrefixes.Count > 0) || !string.IsNullOrEmpty(singlePathPrefix);
                    var sb = new StringBuilder();
                    string countExpr = hasCat ? "COUNT(*)" : "COUNT(DISTINCT m.pkg_uid || char(0) || m.internal_path)";
                    sb.Append("SELECT p.creator, ").Append(countExpr).Append(" ");
                    sb.Append("FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid ");
                    sb.Append("WHERE length(trim(coalesce(p.creator,''))) > 0");
                    if (hasCat) sb.Append(" AND m.category = ?");
                    if (hasPackagePathFilter)
                        sb.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");

                    if (extSet.Count > 0)
                    {
                        sb.Append(" AND (");
                        int ei = 0;
                        foreach (var ext in extSet)
                        {
                            if (ei++ > 0) sb.Append(" OR ");
                            sb.Append("lower(m.internal_path) LIKE ? ESCAPE '\\'");
                        }
                        sb.Append(")");
                    }

                    if (hasPathPrefix)
                    {
                        sb.Append(" AND (");
                        bool firstPath = true;
                        if (pathPrefixes != null && pathPrefixes.Count > 0)
                        {
                            for (int pi = 0; pi < pathPrefixes.Count; pi++)
                            {
                                if (string.IsNullOrEmpty(pathPrefixes[pi])) continue;
                                if (!firstPath) sb.Append(" OR ");
                                sb.Append("m.internal_path LIKE ? ESCAPE '\\'");
                                firstPath = false;
                            }
                        }
                        if (!string.IsNullOrEmpty(singlePathPrefix))
                        {
                            if (!firstPath) sb.Append(" OR ");
                            sb.Append("m.internal_path LIKE ? ESCAPE '\\'");
                            firstPath = false;
                        }
                        if (firstPath) sb.Append("1");
                        sb.Append(")");
                    }
                    
                    List<string> tagsList = null;
                    if (hasTags)
                    {
                        tagsList = new List<string>(activeTags);
                        foreach (var tag in tagsList)
                        {
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                    }
                    var utBind = new List<string>();
                    AppendSqlActiveUserTagExists(sb, utBind, activeUserTags, "m");
                    
                    sb.Append(" GROUP BY p.creator");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (hasCat) stmt.BindText(bind++, categoryTitle);
                        if (hasPackagePathFilter)
                            stmt.BindText(bind++, EscapeLike(normalizedPackagePathFilter.ToLowerInvariant()) + "/%");

                        if (extSet.Count > 0)
                        {
                            foreach (var ext in extSet)
                                stmt.BindText(bind++, "%." + EscapeLike(ext));
                        }

                        if (hasPathPrefix)
                        {
                            if (pathPrefixes != null && pathPrefixes.Count > 0)
                            {
                                for (int pi = 0; pi < pathPrefixes.Count; pi++)
                                {
                                    string pref = pathPrefixes[pi];
                                    if (string.IsNullOrEmpty(pref)) continue;
                                    stmt.BindText(bind++, EscapeLike(pref.Replace('\\', '/')) + "%");
                                }
                            }
                            if (!string.IsNullOrEmpty(singlePathPrefix))
                                stmt.BindText(bind++, EscapeLike(singlePathPrefix.Replace('\\', '/')) + "%");
                        }

                        if (hasTags)
                        {
                            foreach (var tag in tagsList)
                            {
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                            }
                        }
                        for (int ui = 0; ui < utBind.Count; ui++)
                            stmt.BindText(bind++, utBind[ui]);

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string creator = stmt.ColumnText(0);
                            int n;
                            if (!int.TryParse(stmt.ColumnText(1), out n)) n = 0;
                            if (!string.IsNullOrEmpty(creator))
                                countsOut[creator] = n;
                        }
                    }
                }
                return true;
            }
            catch
            {
                countsOut.Clear();
                return false;
            }
        }

        /// <summary>
        /// Side-tab package-folder file counts (VAR rows only) grouped by package directory path under
        /// AddonPackages/ and AllPackages/, including parent folders.
        /// </summary>
        internal static bool TryReadPackageFolderCounts(
            Dictionary<string, int> countsOut,
            string extensionPipeSeparated,
            List<string> pathPrefixes,
            string singlePathPrefix,
            HashSet<string> activeTags,
            string categoryTitle,
            string creatorFilter,
            HashSet<string> activeUserTags = null)
        {
            if (!VpbSqlite3.IsAvailable || countsOut == null) return false;
            countsOut.Clear();

            if (string.IsNullOrEmpty(categoryTitle)) return false;

            long scanBin = DateTime.MinValue.Ticks;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(extensionPipeSeparated))
            {
                string[] exts = extensionPipeSeparated.Split('|');
                for (int i = 0; i < exts.Length; i++)
                {
                    string e = exts[i] != null ? exts[i].Trim() : "";
                    if (e.Length > 0) extSet.Add(e.ToLowerInvariant());
                }
            }

            void AddHierarchyCount(string folderPath, int count)
            {
                if (string.IsNullOrEmpty(folderPath) || count <= 0) return;
                string p = folderPath.Replace('\\', '/').Trim('/');
                if (p.Length == 0) return;

                string[] seg = p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (seg.Length == 0) return;
                string running = seg[0];
                for (int si = 1; si <= seg.Length; si++)
                {
                    int cur;
                    countsOut.TryGetValue(running, out cur);
                    countsOut[running] = cur + count;
                    if (si < seg.Length) running += "/" + seg[si];
                }
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool hasTags = activeTags != null && activeTags.Count > 0;
                    var creatorList = SplitCreatorFilterList(creatorFilter);
                    bool hasCreator = creatorList.Count > 0;
                    bool hasPathPrefix = (pathPrefixes != null && pathPrefixes.Count > 0) || !string.IsNullOrEmpty(singlePathPrefix);

                    var sb = new StringBuilder();
                    sb.Append("SELECT ifnull(p.var_path,''), COUNT(*) ");
                    sb.Append("FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid ");
                    sb.Append("WHERE m.category = ?");
                    if (hasCreator) AppendCreatorFilterSql(sb, "p.creator", creatorList);

                    if (extSet.Count > 0)
                    {
                        sb.Append(" AND (");
                        int ei = 0;
                        foreach (var ext in extSet)
                        {
                            if (ei++ > 0) sb.Append(" OR ");
                            sb.Append("lower(m.internal_path) LIKE ? ESCAPE '\\'");
                        }
                        sb.Append(")");
                    }

                    if (hasPathPrefix)
                    {
                        sb.Append(" AND (");
                        bool first = true;
                        if (pathPrefixes != null && pathPrefixes.Count > 0)
                        {
                            for (int i = 0; i < pathPrefixes.Count; i++)
                            {
                                if (string.IsNullOrEmpty(pathPrefixes[i])) continue;
                                if (!first) sb.Append(" OR ");
                                sb.Append("m.internal_path LIKE ? ESCAPE '\\'");
                                first = false;
                            }
                        }
                        if (!string.IsNullOrEmpty(singlePathPrefix))
                        {
                            if (!first) sb.Append(" OR ");
                            sb.Append("m.internal_path LIKE ? ESCAPE '\\'");
                            first = false;
                        }
                        if (first) sb.Append("1");
                        sb.Append(")");
                    }

                    if (hasTags)
                    {
                        foreach (var _ in activeTags)
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                    }
                    var utBindPf = new List<string>();
                    AppendSqlActiveUserTagExists(sb, utBindPf, activeUserTags, "m");

                    sb.Append(" GROUP BY p.var_path");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        if (hasCreator) BindCreatorFilterSql(stmt, ref bind, creatorList);

                        if (extSet.Count > 0)
                        {
                            foreach (var ext in extSet)
                                stmt.BindText(bind++, "%." + EscapeLike(ext));
                        }

                        if (hasPathPrefix)
                        {
                            if (pathPrefixes != null && pathPrefixes.Count > 0)
                            {
                                for (int i = 0; i < pathPrefixes.Count; i++)
                                {
                                    string pref = pathPrefixes[i];
                                    if (string.IsNullOrEmpty(pref)) continue;
                                    stmt.BindText(bind++, EscapeLike(pref.Replace('\\', '/')) + "%");
                                }
                            }
                            if (!string.IsNullOrEmpty(singlePathPrefix))
                            {
                                stmt.BindText(bind++, EscapeLike(singlePathPrefix.Replace('\\', '/')) + "%");
                            }
                        }

                        if (hasTags)
                        {
                            foreach (var tag in activeTags)
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                        }
                        for (int ui = 0; ui < utBindPf.Count; ui++)
                            stmt.BindText(bind++, utBindPf[ui]);

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string varPath = stmt.ColumnText(0) ?? "";
                            int n;
                            if (!int.TryParse(stmt.ColumnText(1), out n)) n = 0;
                            if (n <= 0 || string.IsNullOrEmpty(varPath)) continue;

                            string normalized;
                            if (!GalleryPanel.TryNormalizeGalleryPathUnderKnownRoots(varPath, out normalized)) continue;
                            string folder = "";
                            try { folder = Path.GetDirectoryName(normalized); } catch { folder = ""; }
                            if (string.IsNullOrEmpty(folder)) continue;
                            folder = folder.Replace('\\', '/').Trim('/');
                            if (folder.Length == 0) continue;
                            AddHierarchyCount(folder, n);
                        }
                    }
                }
                return true;
            }
            catch
            {
                countsOut.Clear();
                return false;
            }
        }

        /// <summary>Populated by <see cref="TryQueryGalleryCategoryRows"/> for perf diagnostics (gated logging).</summary>
        internal static bool TryReadTagCounts(
            string categoryTitle,
            string currentExtension,
            string creatorFilter,
            HashSet<string> tagsToCount,
            Dictionary<string, int> outTagCounts,
            out TagScanTotals outFacets,
            GalleryPanel.ClothingSubfilter clothingSubfilter = 0,
            GalleryPanel.HairSubfilter hairSubfilter = 0,
            GalleryPanel.AppearanceSubfilter appearanceSubfilter = 0,
            HashSet<string> activeTags = null,
            Dictionary<string, HashSet<string>> appearanceTagsByRowKey = null)
        {
            outFacets = new TagScanTotals();
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool pkgHasLoadedCol = false;
                    try { pkgHasLoadedCol = PkgHasLoadedColumn(conn); } catch { pkgHasLoadedCol = false; }

                    string clothSqlAnd = BuildClothingSubfilterSqlAnd(conn, categoryTitle, clothingSubfilter);
                    string hairSqlAnd = BuildHairSubfilterSqlAnd(conn, categoryTitle, hairSubfilter);
                    
                    string tagSqlAnd = "";
                    List<string> activeTagsList = null;
                    if (activeTags != null && activeTags.Count > 0)
                    {
                        activeTagsList = new List<string>(activeTags);
                        var sb = new StringBuilder();
                        for (int i = 0; i < activeTagsList.Count; i++)
                        {
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                        tagSqlAnd = sb.ToString();
                    }

                    var creatorList = SplitCreatorFilterList(creatorFilter);
                    bool hasCreator = creatorList.Count > 0;
                    var sbSql = new StringBuilder(256);
                    sbSql.Append("SELECT m.internal_path, m.pkg_uid, ifnull(m.cloth_attr,'0'), m.list_path FROM cat_mem m ");
                    sbSql.Append("INNER JOIN pkg p ON p.uid = m.pkg_uid ");
                    sbSql.Append("WHERE m.category = ?");
                    if (hasCreator) AppendCreatorFilterSql(sbSql, "p.creator", creatorList);
                    sbSql.Append(clothSqlAnd).Append(hairSqlAnd).Append(tagSqlAnd);
                    string sql = sbSql.ToString();

                    using (var stmt = conn.Prepare(sql))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        if (hasCreator) BindCreatorFilterSql(stmt, ref bind, creatorList);

                        if (activeTagsList != null)
                        {
                            foreach (var tag in activeTagsList)
                            {
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                            }
                        }

                        bool isClothing = string.Equals(categoryTitle, "Clothing", StringComparison.OrdinalIgnoreCase);
                        bool isHair = string.Equals(categoryTitle, "Hair", StringComparison.OrdinalIgnoreCase);
                        bool isAppearance = string.Equals(categoryTitle, "Appearance", StringComparison.OrdinalIgnoreCase);
                        if (isAppearance && appearanceTagsByRowKey == null)
                        {
                            appearanceTagsByRowKey = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                            try { TryLoadGalleryUserTagsForCategory(categoryTitle, appearanceTagsByRowKey); } catch { }
                        }

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string internalPath = stmt.ColumnText(0);
                            string pkgUid = stmt.ColumnText(1);
                            int clothAttr = (int)stmt.ColumnInt64(2);
                            string listPath = stmt.ColumnText(3) ?? "";

                            if (tagsToCount != null && tagsToCount.Count > 0)
                            {
                                foreach (var tag in tagsToCount)
                                {
                                    if (listPath.IndexOf("[" + tag + "]", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        int cur;
                                        outTagCounts.TryGetValue(tag, out cur);
                                        outTagCounts[tag] = cur + 1;
                                    }
                                }
                            }

                            if (isClothing)
                            {
                                outFacets.ClothingSubfilterCountAll++;
                                // Issue #101: subfilter facet counts dropped to 0 when creator filter active.
                                // Root cause: some indexed rows have clothAttr stored as 0/NULL (missing
                                // ClothingAttrPresentFlag), so the packed-attr branch is skipped. Fall back
                                // to per-row classification from internal_path/list_path so facet counts
                                // are populated regardless of whether cloth_attr was packed at index time.
                                if ((clothAttr & ClothingAttrPresentFlag) == 0)
                                    clothAttr = PackClothingGalleryAttrForVarListPath(string.IsNullOrEmpty(listPath) ? internalPath : listPath);

                                if ((clothAttr & ClothingAttrPresentFlag) != 0)
                                {
                                    int kind = clothAttr & 0xF;
                                    int gender = (clothAttr >> 4) & 0xF;
                                    bool isPreset = (clothAttr & 0x100) != 0;
                                    bool isDecal = (clothAttr & 0x200) != 0;

                                    if (kind == (int)ClothingLoadingUtils.ResourceKind.Clothing)
                                    {
                                        // Facet counts
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.RealClothing)) outFacets.ClothingSubfilterFacetCountReal++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Presets)) outFacets.ClothingSubfilterFacetCountPresets++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Custom)) outFacets.ClothingSubfilterFacetCountCustom++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Items)) outFacets.ClothingSubfilterFacetCountItems++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Male)) outFacets.ClothingSubfilterFacetCountMale++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Female)) outFacets.ClothingSubfilterFacetCountFemale++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Decals)) outFacets.ClothingSubfilterFacetCountDecals++;

                                        if (isDecal) outFacets.ClothingSubfilterCountDecals++;
                                        else
                                        {
                                            outFacets.ClothingSubfilterCountReal++;
                                            if (isPreset) outFacets.ClothingSubfilterCountPresets++;
                                            // Note: VAR rows are never "Custom" (loose files only), so ClothingSubfilterCountCustom remains 0.
                                            // We explicitly initialize it here to clear the compiler warning.
                                            outFacets.ClothingSubfilterCountCustom = 0; 
                                            if (!isPreset) outFacets.ClothingSubfilterCountItems++;
                                            if (gender == (int)ClothingLoadingUtils.ResourceGender.Male) outFacets.ClothingSubfilterCountMale++;
                                            else if (gender == (int)ClothingLoadingUtils.ResourceGender.Female) outFacets.ClothingSubfilterCountFemale++;
                                        }
                                    }
                                }
                            }
                            else if (isHair)
                            {
                                outFacets.HairSubfilterCountAll++;
                                if ((clothAttr & ClothingAttrPresentFlag) == 0)
                                    clothAttr = PackClothingGalleryAttrForVarListPath(string.IsNullOrEmpty(listPath) ? internalPath : listPath);

                                if ((clothAttr & ClothingAttrPresentFlag) != 0)
                                {
                                    int kind = clothAttr & 0xF;
                                    int gender = (clothAttr >> 4) & 0xF;
                                    bool isPreset = (clothAttr & 0x100) != 0;

                                    if (kind == (int)ClothingLoadingUtils.ResourceKind.Hair)
                                    {
                                        if (HairPackedAttrMatchesSubfilter(clothAttr, hairSubfilter ^ GalleryPanel.HairSubfilter.Presets)) outFacets.HairSubfilterFacetCountPresets++;
                                        if (HairPackedAttrMatchesSubfilter(clothAttr, hairSubfilter ^ GalleryPanel.HairSubfilter.Custom)) outFacets.HairSubfilterFacetCountCustom++;
                                        if (HairPackedAttrMatchesSubfilter(clothAttr, hairSubfilter ^ GalleryPanel.HairSubfilter.Items)) outFacets.HairSubfilterFacetCountItems++;
                                        if (HairPackedAttrMatchesSubfilter(clothAttr, hairSubfilter ^ GalleryPanel.HairSubfilter.Male)) outFacets.HairSubfilterFacetCountMale++;
                                        if (HairPackedAttrMatchesSubfilter(clothAttr, hairSubfilter ^ GalleryPanel.HairSubfilter.Female)) outFacets.HairSubfilterFacetCountFemale++;

                                        if (isPreset) outFacets.HairSubfilterCountPresets++;
                                        outFacets.HairSubfilterCountCustom = 0;
                                        if (!isPreset) outFacets.HairSubfilterCountItems++;
                                        if (gender == (int)ClothingLoadingUtils.ResourceGender.Male) outFacets.HairSubfilterCountMale++;
                                        else if (gender == (int)ClothingLoadingUtils.ResourceGender.Female) outFacets.HairSubfilterCountFemale++;
                                    }
                                }
                            }
                            else if (isAppearance)
                            {
                                // Appearance subfilters are not yet packed into clothAttr in the current schema.
                                // Fall back to path heuristics for now to avoid warnings and provide correct counts.
                                string p = internalPath.Replace('\\', '/');
                                if (p.IndexOf("/appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    bool isCustomAppearance = p.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                                    bool isPresetAppearance = p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
                                    
                                    string fileName = internalPath;
                                    int ls = internalPath.LastIndexOfAny(new char[] { '/', '\\' });
                                    if (ls >= 0 && ls + 1 < internalPath.Length) fileName = internalPath.Substring(ls + 1);
                                    int g = AppearanceGenderClassifier.ToGenderCode(
                                        AppearanceGenderClassifier.ClassifyVarAppearance(categoryTitle ?? "", p, fileName, pkgUid ?? "", appearanceTagsByRowKey));

                                    outFacets.AppearanceSubfilterCountAll++;
                                    if (isPresetAppearance) outFacets.AppearanceSubfilterCountPresets++;
                                    if (isCustomAppearance) outFacets.AppearanceSubfilterCountCustom++;
                                    if (g == 2) outFacets.AppearanceSubfilterCountMale++;
                                    if (g == 1) outFacets.AppearanceSubfilterCountFemale++;
                                    if (g == 3) outFacets.AppearanceSubfilterCountFuta++;
                                    if (g == 0) outFacets.AppearanceSubfilterCountUnknown++;

                                    // Simplified PassesAppearanceSubfilters check
                                    bool PassesApp(GalleryPanel.AppearanceSubfilter f, bool isPre, bool isCus, int gen)
                                    {
                                        if (f == 0) return true;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Presets) != 0 && !isPre) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Custom) != 0 && !isCus) return false;
                                        if (!AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(gen, f)) return false;
                                        return true;
                                    }

                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Presets, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountPresets++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Custom, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountCustom++;
                                    if (PassesApp(AppearanceGenderClassifier.HypotheticalGenderFacet(appearanceSubfilter, GalleryPanel.AppearanceSubfilter.Male), isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountMale++;
                                    if (PassesApp(AppearanceGenderClassifier.HypotheticalGenderFacet(appearanceSubfilter, GalleryPanel.AppearanceSubfilter.Female), isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountFemale++;
                                    if (PassesApp(AppearanceGenderClassifier.HypotheticalGenderFacet(appearanceSubfilter, GalleryPanel.AppearanceSubfilter.Unknown), isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountUnknown++;
                                    if (PassesApp(AppearanceGenderClassifier.HypotheticalGenderFacet(appearanceSubfilter, GalleryPanel.AppearanceSubfilter.Futa), isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountFuta++;

                                    if (PassesApp(appearanceSubfilter, isPresetAppearance, isCustomAppearance, g))
                                    {
                                        outFacets.AppearanceSubfilterCurrentCountAll++;
                                        if (g == 2) outFacets.AppearanceSubfilterCurrentCountMale++;
                                        if (g == 1) outFacets.AppearanceSubfilterCurrentCountFemale++;
                                        if (g == 3) outFacets.AppearanceSubfilterCurrentCountFuta++;
                                        if (g == 0) outFacets.AppearanceSubfilterCurrentCountUnknown++;
                                    }
                                    
                                    if (isPresetAppearance)
                                    {
                                        outFacets.AppearanceSourceCountPresets++;
                                        outFacets.AppearanceSourceCountAll++;
                                        outFacets.AppearanceSourceCountCustom = 0; // Explicitly initialize to clear warning
                                    }
                                    else if (isCustomAppearance)
                                    {
                                        outFacets.AppearanceSourceCountCustom++;
                                        outFacets.AppearanceSourceCountAll++;
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal struct GalleryCategoryQueryStats
        {
            public bool ExecutedQuery;
            public string RejectReason;
            public long SqlElapsedMs;
            public int RowsRead;
        }

        internal sealed class TagScanTotals
        {
            public int AppearanceSourceCountAll;
            public int AppearanceSourceCountPresets;
            public int AppearanceSourceCountCustom;
            public int ClothingSubfilterCountAll;
            public int ClothingSubfilterCountReal;
            public int ClothingSubfilterCountPresets;
            public int ClothingSubfilterCountCustom;
            public int ClothingSubfilterCountItems;
            public int ClothingSubfilterCountMale;
            public int ClothingSubfilterCountFemale;
            public int ClothingSubfilterCountDecals;
            public int HairSubfilterCountAll;
            public int HairSubfilterCountPresets;
            public int HairSubfilterCountCustom;
            public int HairSubfilterCountItems;
            public int HairSubfilterCountMale;
            public int HairSubfilterCountFemale;
            public int AppearanceSubfilterCountAll;
            public int AppearanceSubfilterCountPresets;
            public int AppearanceSubfilterCountCustom;
            public int AppearanceSubfilterCountMale;
            public int AppearanceSubfilterCountFemale;
            public int AppearanceSubfilterCountFuta;
            public int AppearanceSubfilterCountUnknown;
            public int ClothingSubfilterFacetCountReal;
            public int ClothingSubfilterFacetCountPresets;
            public int ClothingSubfilterFacetCountCustom;
            public int ClothingSubfilterFacetCountItems;
            public int ClothingSubfilterFacetCountMale;
            public int ClothingSubfilterFacetCountFemale;
            public int ClothingSubfilterFacetCountDecals;
            public int HairSubfilterFacetCountPresets;
            public int HairSubfilterFacetCountCustom;
            public int HairSubfilterFacetCountItems;
            public int HairSubfilterFacetCountMale;
            public int HairSubfilterFacetCountFemale;
            public int AppearanceSubfilterFacetCountPresets;
            public int AppearanceSubfilterFacetCountCustom;
            public int AppearanceSubfilterFacetCountMale;
            public int AppearanceSubfilterFacetCountFemale;
            public int AppearanceSubfilterFacetCountFuta;
            public int AppearanceSubfilterFacetCountUnknown;
            public int AppearanceSubfilterCurrentCountAll;
            public int AppearanceSubfilterCurrentCountMale;
            public int AppearanceSubfilterCurrentCountFemale;
            public int AppearanceSubfilterCurrentCountFuta;
            public int AppearanceSubfilterCurrentCountUnknown;
        }

        /// <summary>
        /// Returns true if rows were read from SQLite (caller still applies path quirks e.g. Saves/Person vs appearance, name filter, PassesFilters).
        /// </summary>
        /// <param name="clothingSubfilterForSql">When non-zero and category is Clothing, narrows the query using indexed <c>cloth_attr</c> (schema 4+).</param>
        internal static bool TryQueryGalleryCategoryRows(
            string categoryTitle,
            string currentExtension,
            string creatorFilter,
            List<Row> outRows,
            out GalleryCategoryQueryStats stats,
            GalleryPanel.ClothingSubfilter clothingSubfilterForSql = 0,
            int loadedState = -1,
            string[] nameTerms = null,
            List<string> pathExclusions = null,
            List<string> pathInclusions = null,
            HashSet<string> activeTags = null,
            HashSet<string> activeUserTags = null,
            SortState sortState = null)
        {
            stats = new GalleryCategoryQueryStats();
            outRows.Clear();
            if (!VpbSqlite3.IsAvailable)
            {
                stats.RejectReason = "sqlite_unavailable";
                return false;
            }
            if (string.IsNullOrEmpty(categoryTitle))
            {
                stats.RejectReason = "empty_category_title";
                return false;
            }
            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig))
            {
                stats.RejectReason = "index_stale_or_empty_sig readyScan=" + readyScan + " scanBin=" + scanBin + " sigEmpty=" + (string.IsNullOrEmpty(catSig) ? "1" : "0");
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }
            if (s_RebuildRunning)
            {
                stats.RejectReason = "rebuild_running";
                return false;
            }

            Gallery g = Gallery.singleton;
            if (g == null)
            {
                stats.RejectReason = "gallery_singleton_null";
                return false;
            }
            Gallery.Category catDef = g.FindCategoryByName(categoryTitle);
            if (string.IsNullOrEmpty(catDef.name))
            {
                stats.RejectReason = "category_not_found";
                return false;
            }
            if (!ExtensionSetsEqual(catDef.extension ?? "", currentExtension ?? ""))
            {
                stats.RejectReason = "extension_mismatch";
                return false;
            }

            try
            {
                var swSql = Stopwatch.StartNew();
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool pkgHasLoadedCol = false;
                    try { pkgHasLoadedCol = PkgHasLoadedColumn(conn); } catch { pkgHasLoadedCol = false; }

                    string clothSqlAnd = BuildClothingSubfilterSqlAnd(conn, categoryTitle, clothingSubfilterForSql);
                    string loadedSqlAnd = "";
                    if (loadedState == 1)
                    {
                        // If the column doesn't exist, nothing can be "loaded".
                        if (!pkgHasLoadedCol) loadedSqlAnd = " AND 0";
                        else loadedSqlAnd = " AND ifnull(p.loaded,0) != 0";
                    }
                    else if (loadedState == 0)
                    {
                        // If the column doesn't exist, treat everything as unloaded (no extra filter needed).
                        if (pkgHasLoadedCol) loadedSqlAnd = " AND ifnull(p.loaded,0) = 0";
                    }

                    string nameSqlAnd = "";
                    if (nameTerms != null && nameTerms.Length > 0)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < nameTerms.Length; i++)
                        {
                            if (string.IsNullOrEmpty(nameTerms[i])) continue;
                            // Match against list_path (which includes package name) or internal_path.
                            sb.Append(" AND (m.list_path LIKE ? ESCAPE '\\' OR m.internal_path LIKE ? ESCAPE '\\')");
                        }
                        nameSqlAnd = sb.ToString();
                    }

                    string exclusionSqlAnd = "";
                    if (pathExclusions != null && pathExclusions.Count > 0)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < pathExclusions.Count; i++)
                        {
                            if (string.IsNullOrEmpty(pathExclusions[i])) continue;
                            sb.Append(" AND m.internal_path NOT LIKE ? ESCAPE '\\'");
                        }
                        exclusionSqlAnd = sb.ToString();
                    }

                    string inclusionSqlAnd = "";
                    if (pathInclusions != null && pathInclusions.Count > 0)
                    {
                        var sb = new StringBuilder();
                        sb.Append(" AND (");
                        bool firstInc = true;
                        for (int i = 0; i < pathInclusions.Count; i++)
                        {
                            if (string.IsNullOrEmpty(pathInclusions[i])) continue;
                            if (!firstInc) sb.Append(" OR ");
                            firstInc = false;
                            sb.Append("m.internal_path LIKE ? ESCAPE '\\'");
                        }
                        if (!firstInc) sb.Append(")");
                        else inclusionSqlAnd = "";
                        if (!firstInc) inclusionSqlAnd = sb.ToString();
                    }

                    string tagSqlAnd = "";
                    List<string> activeTagsList = null;
                    if (activeTags != null && activeTags.Count > 0)
                    {
                        activeTagsList = new List<string>(activeTags);
                        var sb = new StringBuilder();
                        for (int i = 0; i < activeTagsList.Count; i++)
                        {
                            // Tags are stored in list_path as [tag] or similar.
                            // This is a heuristic; real tag filtering is complex.
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                        tagSqlAnd = sb.ToString();
                    }
                    var userTagBindNames = new List<string>();
                    var sbUt = new StringBuilder();
                    AppendSqlActiveUserTagExists(sbUt, userTagBindNames, activeUserTags, "m");
                    string userTagSqlAnd = sbUt.ToString();

                    string loadedSelect = pkgHasLoadedCol ? "ifnull(p.loaded,'')" : "0";
                    string orderBy = "";
                    if (sortState != null)
                    {
                        string dir = sortState.Direction == SortDirection.Descending ? " DESC" : " ASC";
                        switch (sortState.Type)
                        {
                            case SortType.Date: orderBy = " ORDER BY p.wtime" + dir + ", m.list_path ASC"; break;
                            case SortType.Size: orderBy = " ORDER BY p.psize" + dir + ", m.list_path ASC"; break;
                            case SortType.DateCreated: orderBy = " ORDER BY ifnull(p.ictime, p.pctime)" + dir + ", m.list_path ASC"; break;
                            // SortType.DateAdded / DateUpdated are family-level (creator.packageName aggregates), computed in-process by GallerySortManager.BuildFamilyScanTimes.
                        }
                    }

                    var creatorList = SplitCreatorFilterList(creatorFilter);
                    bool hasCreator = creatorList.Count > 0;
                    var sbSql = new StringBuilder(512);
                    sbSql.Append("SELECT m.pkg_uid, m.internal_path, m.list_path, p.var_path, p.wtime, p.psize, ifnull(p.ictime, p.pctime), ifnull(m.cloth_attr,''), ");
                    sbSql.Append(loadedSelect);
                    sbSql.Append(", ifnull(p.first_scanned, 0)");
                    sbSql.Append(" FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid WHERE m.category = ?");
                    if (hasCreator) AppendCreatorFilterSql(sbSql, "p.creator", creatorList);
                    sbSql.Append(clothSqlAnd).Append(loadedSqlAnd).Append(nameSqlAnd).Append(exclusionSqlAnd).Append(inclusionSqlAnd).Append(tagSqlAnd).Append(userTagSqlAnd).Append(orderBy);
                    string sql = sbSql.ToString();
                    
                    using (var stmt = conn.Prepare(sql))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        if (hasCreator) BindCreatorFilterSql(stmt, ref bind, creatorList);

                        if (nameTerms != null && nameTerms.Length > 0)
                        {
                            for (int i = 0; i < nameTerms.Length; i++)
                            {
                                if (string.IsNullOrEmpty(nameTerms[i])) continue;
                                string esc = "%" + EscapeLike(nameTerms[i]) + "%";
                                stmt.BindText(bind++, esc);
                                stmt.BindText(bind++, esc);
                            }
                        }

                        if (pathExclusions != null && pathExclusions.Count > 0)
                        {
                            for (int i = 0; i < pathExclusions.Count; i++)
                            {
                                if (string.IsNullOrEmpty(pathExclusions[i])) continue;
                                string esc = EscapeLike(pathExclusions[i]) + "%";
                                stmt.BindText(bind++, esc);
                            }
                        }

                        if (pathInclusions != null && pathInclusions.Count > 0)
                        {
                            for (int i = 0; i < pathInclusions.Count; i++)
                            {
                                if (string.IsNullOrEmpty(pathInclusions[i])) continue;
                                string esc = EscapeLike(pathInclusions[i].Replace('\\', '/').TrimEnd('/')) + "/%";
                                stmt.BindText(bind++, esc);
                            }
                        }

                        if (activeTagsList != null)
                        {
                            for (int i = 0; i < activeTagsList.Count; i++)
                            {
                                string esc = "%" + EscapeLike(activeTagsList[i]) + "%";
                                stmt.BindText(bind++, esc);
                            }
                        }
                        for (int ui = 0; ui < userTagBindNames.Count; ui++)
                            stmt.BindText(bind++, userTagBindNames[ui]);

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            Row r;
                            r.ItemUsageKey = null;
                            r.PackageUid = stmt.ColumnText(0);
                            r.InternalPath = stmt.ColumnText(1);
                            r.ListPath = stmt.ColumnText(2) ?? "";
                            r.VarPath = stmt.ColumnText(3) ?? "";
                            r.LastWriteTicksOrInvalid = stmt.ColumnInt64(4);
                            r.PackageSizeOrInvalid = stmt.ColumnInt64(5);
                            r.PackageCreationTicksOrInvalid = stmt.ColumnInt64(6);
                            r.ClothingAttrPacked = (int)stmt.ColumnInt64(7);
                            r.PackageIsLoaded = stmt.ColumnInt64(8) != 0;
                            r.FirstScannedTicksOrInvalid = stmt.ColumnInt64(9);
                            r.ItemUsageCount = 0;
                            r.ItemLastUsedBinary = 0;
                            if (r.PackageUid.Length > 0 && r.InternalPath.Length > 0)
                                outRows.Add(r);
                        }
                    }
                }
                swSql.Stop();
                stats.ExecutedQuery = true;
                stats.SqlElapsedMs = swSql.ElapsedMilliseconds;
                stats.RowsRead = outRows.Count;
                return true;
            }
            catch (Exception ex)
            {
                s_LastError = ex.Message;
                stats.RejectReason = "exception:" + ex.Message;
                return false;
            }
        }

        private static string BuildGalleryHistoryKindSqlAnd(GalleryHistoryFilterMode mode)
        {
            switch (mode)
            {
                case GalleryHistoryFilterMode.Recent:
                case GalleryHistoryFilterMode.MostUsed:
                    return "";
                case GalleryHistoryFilterMode.Scenes:
                    return " AND i.kind = 'scene'";
                case GalleryHistoryFilterMode.Appearance:
                    return " AND i.kind = 'appearance'";
                case GalleryHistoryFilterMode.Clothing:
                    return " AND i.kind = 'clothing'";
                case GalleryHistoryFilterMode.Hair:
                    return " AND i.kind = 'hair'";
                case GalleryHistoryFilterMode.Plugins:
                    return " AND i.kind = 'plugins'";
                case GalleryHistoryFilterMode.Pose:
                    return " AND i.kind = 'pose'";
                case GalleryHistoryFilterMode.Body:
                    return " AND i.kind IN ('skin','morphs')";
                case GalleryHistoryFilterMode.Misc:
                    return " AND i.kind NOT IN ('scene','appearance','clothing','hair','plugins','pose','skin','morphs')";
                default:
                    return "";
            }
        }

        private static string BuildGalleryHistoryOrderSql(GalleryHistoryFilterMode mode)
        {
            if (mode == GalleryHistoryFilterMode.MostUsed)
                return " ORDER BY i.use_count DESC, i.last_used DESC, i.item_key ASC";
            return " ORDER BY i.last_used DESC, i.item_key ASC";
        }

        // item_usage.item_key uid/path split (keep in sync with TryQueryGalleryHistoryRows + mode counts).
        private const string GalleryHistoryUsagePkgKeySql =
            "(CASE WHEN instr(i.item_key,':/')>0 THEN substr(i.item_key,1,instr(i.item_key,':/')-1) ELSE i.item_key END)";

        private const string GalleryHistoryUsageInternalKeySql =
            "(CASE WHEN instr(i.item_key,':/')>0 THEN substr(i.item_key,instr(i.item_key,':/')+2) ELSE '' END)";

        private static string GalleryHistoryResolvedInternalPathSql()
        {
            return "COALESCE(NULLIF(mx.internal_path,''), NULLIF(mr.internal_path,''), NULLIF(" + GalleryHistoryUsageInternalKeySql + ",''), 'meta.json')";
        }

        private static void AppendGalleryHistoryJoinFromWhere(StringBuilder sb)
        {
            sb.Append("FROM item_usage i ");
            sb.Append("INNER JOIN pkg p ON lower(p.uid) = ").Append(GalleryHistoryUsagePkgKeySql).Append(" ");
            sb.Append("LEFT JOIN cat_mem mx ON mx.pkg_uid = p.uid AND (");
            sb.Append("lower(TRIM(ifnull(mx.list_path,''))) = lower(TRIM(i.item_key)) OR ");
            sb.Append("lower(TRIM(ifnull(mx.internal_path,''))) = lower(TRIM(").Append(GalleryHistoryUsageInternalKeySql).Append("))) ");
            sb.Append("AND mx.rowid = (SELECT MIN(cm.rowid) FROM cat_mem cm WHERE cm.pkg_uid = p.uid AND (");
            sb.Append("lower(TRIM(ifnull(cm.list_path,''))) = lower(TRIM(i.item_key)) OR ");
            sb.Append("lower(TRIM(ifnull(cm.internal_path,''))) = lower(TRIM(").Append(GalleryHistoryUsageInternalKeySql).Append(")))) ");
            sb.Append("LEFT JOIN cat_mem mr ON mr.pkg_uid = p.uid AND mr.rowid = (");
            sb.Append("SELECT MIN(cm.rowid) FROM cat_mem cm WHERE cm.pkg_uid = p.uid) ");
            sb.Append("WHERE 1=1");
        }

        /// <summary>History browse SQL (<c>item_usage</c>, <c>pkg</c>, <c>cat_mem</c>).</summary>
        internal static bool TryQueryGalleryHistoryRows(
            GalleryHistoryFilterMode mode,
            string[] nameTerms,
            List<Row> outRows,
            out GalleryCategoryQueryStats stats)
        {
            stats = new GalleryCategoryQueryStats();
            outRows.Clear();
            if (!VpbSqlite3.IsAvailable)
            {
                stats.RejectReason = "sqlite_unavailable";
                if (LogHistoryUsageDebug)
                {
                    try { LogUtil.Log("[VPB.History] TryQueryGalleryHistoryRows skipped (sqlite_unavailable) mode=" + mode); } catch { }
                }
                return false;
            }

            try
            {
                var swSql = Stopwatch.StartNew();
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    bool pkgHasLoadedCol = false;
                    try { pkgHasLoadedCol = PkgHasLoadedColumn(conn); } catch { pkgHasLoadedCol = false; }

                    string loadedSelect = pkgHasLoadedCol ? "ifnull(p.loaded,'')" : "0";
                    string kindSql = BuildGalleryHistoryKindSqlAnd(mode);
                    string orderSql = BuildGalleryHistoryOrderSql(mode);

                    var sb = new StringBuilder(768);
                    sb.Append(
                        "SELECT i.item_key, p.uid, " +
                        GalleryHistoryResolvedInternalPathSql() + ", " +
                        "TRIM(COALESCE(mx.list_path, mr.list_path,'')), " +
                        "ifnull(p.var_path,''), " +
                        "p.wtime, p.psize, ifnull(p.ictime, p.pctime), " +
                        "ifnull(COALESCE(mx.cloth_attr, mr.cloth_attr),''), " +
                        loadedSelect +
                        ", i.use_count, i.last_used, ifnull(p.first_scanned, 0) ");
                    AppendGalleryHistoryJoinFromWhere(sb);
                    sb.Append(kindSql);

                    if (nameTerms != null && nameTerms.Length > 0)
                    {
                        for (int t = 0; t < nameTerms.Length; t++)
                        {
                            if (string.IsNullOrEmpty(nameTerms[t])) continue;
                            sb.Append(" AND (ifnull(COALESCE(mx.list_path, mr.list_path),'') LIKE ? ESCAPE '\\' OR ifnull(COALESCE(mx.internal_path, mr.internal_path),'') LIKE ? ESCAPE '\\' OR ifnull(p.var_path,'') LIKE ? ESCAPE '\\')");
                        }
                    }

                    sb.Append(orderSql);

                    List<string> dbgSampleKeys = LogHistoryUsageDebug ? new List<string>(18) : null;

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (nameTerms != null && nameTerms.Length > 0)
                        {
                            for (int t = 0; t < nameTerms.Length; t++)
                            {
                                if (string.IsNullOrEmpty(nameTerms[t])) continue;
                                string esc = "%" + EscapeLike(nameTerms[t]) + "%";
                                stmt.BindText(bind++, esc);
                                stmt.BindText(bind++, esc);
                                stmt.BindText(bind++, esc);
                            }
                        }

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            Row r;
                            r.ItemUsageKey = stmt.ColumnText(0) ?? "";
                            r.PackageUid = stmt.ColumnText(1);
                            r.InternalPath = stmt.ColumnText(2);
                            r.ListPath = stmt.ColumnText(3) ?? "";
                            r.VarPath = stmt.ColumnText(4) ?? "";
                            r.LastWriteTicksOrInvalid = stmt.ColumnInt64(5);
                            r.PackageSizeOrInvalid = stmt.ColumnInt64(6);
                            r.PackageCreationTicksOrInvalid = stmt.ColumnInt64(7);
                            r.ClothingAttrPacked = (int)stmt.ColumnInt64(8);
                            r.PackageIsLoaded = stmt.ColumnInt64(9) != 0;
                            r.ItemUsageCount = (int)Math.Min(Math.Max(stmt.ColumnInt64(10), 0), int.MaxValue);
                            r.ItemLastUsedBinary = stmt.ColumnInt64(11);
                            r.FirstScannedTicksOrInvalid = stmt.ColumnInt64(12);
                            if (dbgSampleKeys != null && dbgSampleKeys.Count < 18 && !string.IsNullOrEmpty(r.ItemUsageKey))
                                dbgSampleKeys.Add(r.ItemUsageKey);
                            if (r.PackageUid.Length > 0 && r.InternalPath.Length > 0)
                                outRows.Add(r);
                        }
                    }

                    if (LogHistoryUsageDebug)
                    {
                        try
                        {
                            long totalUsageRows = -1;
                            try
                            {
                                using (var stc = conn.Prepare("SELECT COUNT(*) FROM item_usage"))
                                {
                                    if (stc.Step() == VpbSqlite3.SqliteRow)
                                        totalUsageRows = stc.ColumnInt64(0);
                                }
                            }
                            catch { }

                            var sj = new StringBuilder(dbgSampleKeys != null ? dbgSampleKeys.Count * 64 : 8);
                            if (dbgSampleKeys != null)
                            {
                                for (int i = 0; i < dbgSampleKeys.Count; i++)
                                {
                                    if (i > 0) sj.Append(" | ");
                                    sj.Append(HistoryDebugTruncate(dbgSampleKeys[i]));
                                }
                            }

                            string dbLabel = "";
                            try { dbLabel = Path.GetFileName(GetLocalDatabasePathForDiagnostics()); } catch { dbLabel = "?"; }

                            LogUtil.Log("[VPB.History] TryQueryGalleryHistoryRows mode=" + mode + " db=" + dbLabel +
                                        " joined_rows=" + outRows.Count + " item_usage_row_count=" + totalUsageRows +
                                        " sql_ms=" + swSql.ElapsedMilliseconds +
                                        " sample_item_keys=" + (sj.Length > 0 ? sj.ToString() : "(none)"));
                        }
                        catch { }
                    }
                }
                swSql.Stop();
                stats.ExecutedQuery = true;
                stats.SqlElapsedMs = swSql.ElapsedMilliseconds;
                stats.RowsRead = outRows.Count;
                return true;
            }
            catch (Exception ex)
            {
                s_LastError = ex.Message;
                stats.RejectReason = "exception:" + ex.Message;
                if (LogHistoryUsageDebug)
                {
                    try { LogUtil.LogWarning("[VPB.History] TryQueryGalleryHistoryRows failed: " + ex.Message); } catch { }
                }
                return false;
            }
        }

        private static bool PkgHasLoadedColumn(VpbSqlite3.Connection conn)
        {
            if (conn == null) return false;
            using (var st = conn.Prepare("SELECT 1 FROM pragma_table_info('pkg') WHERE name='loaded' LIMIT 1;"))
            {
                int step = st.Step();
                return step == VpbSqlite3.SqliteRow;
            }
        }

        /// <summary>
        /// Reads package rows for a scoped set of UIDs from the local SQLite index (no full scan / no package resolution).
        /// Returns false if the index is unavailable or stale.
        /// </summary>
        internal static bool TryQueryPackageRowsForUids(HashSet<string> uids, List<PackageRow> outRows)
        {
            outRows.Clear();
            if (uids == null || uids.Count == 0) return true;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }

            // Require a published index for the current package inventory.
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                // SQLite default max variables is typically 999. Stay well under to allow future expansion.
                const int chunkSize = 400;
                var chunk = new List<string>(chunkSize);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    foreach (var uid in uids)
                    {
                        if (string.IsNullOrEmpty(uid)) continue;
                        chunk.Add(uid);
                        if (chunk.Count < chunkSize) continue;
                        if (!TryQueryPackageRowsChunk(conn, chunk, outRows)) return false;
                        chunk.Clear();
                    }
                    if (chunk.Count > 0)
                    {
                        if (!TryQueryPackageRowsChunk(conn, chunk, outRows)) return false;
                    }
                }
                return true;
            }
            catch
            {
                outRows.Clear();
                return false;
            }
        }

        private static string EscapeLike(string term)
        {
            if (string.IsNullOrEmpty(term)) return "";
            // Escape LIKE wildcards and the escape character itself.
            return term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        internal static bool TryReadVarPackageCreatorCounts(Dictionary<string, int> countsOut, string packagePathFilter)
        {
            if (!VpbSqlite3.IsAvailable || countsOut == null) return false;
            countsOut.Clear();
            try
            {
                string normalized = "";
                bool hasPath = false;
                if (!string.IsNullOrEmpty(packagePathFilter))
                {
                    normalized = packagePathFilter.Replace('\\', '/').Trim().Trim('/');
                    hasPath = normalized.Length > 0;
                }
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    var sb = new StringBuilder(160);
                    sb.Append("SELECT p.creator, COUNT(*) FROM pkg p ");
                    sb.Append("WHERE length(trim(coalesce(p.creator,''))) > 0");
                    if (hasPath)
                        sb.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    sb.Append(" GROUP BY p.creator");
                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        if (hasPath) st.BindText(1, EscapeLike(normalized.ToLowerInvariant()) + "/%");
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string creator = st.ColumnText(0) ?? "";
                            int n;
                            if (!int.TryParse(st.ColumnText(1), out n)) n = (int)st.ColumnInt64(1);
                            if (creator.Length > 0) countsOut[creator] = n;
                        }
                    }
                }
                return true;
            }
            catch
            {
                countsOut.Clear();
                return false;
            }
        }

        internal static bool TryQueryVarPackageRowsForList(
            string creatorFilter,
            string packagePathFilter,
            int loadedState,
            string[] nameTerms,
            SortState sortState,
            List<PackageRow> outRows)
        {
            if (outRows == null) return false;
            outRows.Clear();
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool pkgHasLoadedCol = false;
                    try { pkgHasLoadedCol = PkgHasLoadedColumn(conn); } catch { pkgHasLoadedCol = false; }

                    var creatorList = SplitCreatorFilterList(creatorFilter);
                    bool hasCreator = creatorList.Count > 0;
                    string normalizedPath = "";
                    bool hasPath = false;
                    if (!string.IsNullOrEmpty(packagePathFilter))
                    {
                        normalizedPath = packagePathFilter.Replace('\\', '/').Trim().Trim('/');
                        hasPath = normalizedPath.Length > 0;
                    }

                    string loadedSqlAnd = "";
                    if (loadedState == 1)
                    {
                        if (!pkgHasLoadedCol) loadedSqlAnd = " AND 0";
                        else loadedSqlAnd = " AND ifnull(p.loaded,0) != 0";
                    }
                    else if (loadedState == 0)
                    {
                        if (pkgHasLoadedCol) loadedSqlAnd = " AND ifnull(p.loaded,0) = 0";
                    }

                    string nameSqlAnd = "";
                    if (nameTerms != null && nameTerms.Length > 0)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < nameTerms.Length; i++)
                        {
                            if (string.IsNullOrEmpty(nameTerms[i])) continue;
                            sb.Append(" AND (p.uid LIKE ? ESCAPE '\\' OR ifnull(p.var_path,'') LIKE ? ESCAPE '\\')");
                        }
                        nameSqlAnd = sb.ToString();
                    }

                    string loadedSelect = pkgHasLoadedCol ? "ifnull(p.loaded,'')" : "0";
                    string orderBy = " ORDER BY p.uid ASC";
                    if (sortState != null)
                    {
                        string dir = sortState.Direction == SortDirection.Descending ? " DESC" : " ASC";
                        switch (sortState.Type)
                        {
                            case SortType.Date: orderBy = " ORDER BY p.wtime" + dir + ", p.uid ASC"; break;
                            case SortType.Size: orderBy = " ORDER BY p.psize" + dir + ", p.uid ASC"; break;
                            case SortType.DateCreated: orderBy = " ORDER BY ifnull(p.ictime, p.pctime)" + dir + ", p.uid ASC"; break;
                        }
                    }

                    var sbSql = new StringBuilder(512);
                    sbSql.Append("SELECT p.uid, ifnull(p.var_path,''), p.wtime, p.psize, ifnull(p.ictime, p.pctime), ").Append(loadedSelect).Append(", ifnull(p.first_scanned, 0) FROM pkg p WHERE 1=1");
                    if (hasCreator) AppendCreatorFilterSql(sbSql, "p.creator", creatorList);
                    if (hasPath) sbSql.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    sbSql.Append(loadedSqlAnd).Append(nameSqlAnd).Append(orderBy);

                    using (var st = conn.Prepare(sbSql.ToString()))
                    {
                        int bind = 1;
                        if (hasCreator) BindCreatorFilterSql(st, ref bind, creatorList);
                        if (hasPath) st.BindText(bind++, EscapeLike(normalizedPath.ToLowerInvariant()) + "/%");
                        if (nameTerms != null && nameTerms.Length > 0)
                        {
                            for (int i = 0; i < nameTerms.Length; i++)
                            {
                                if (string.IsNullOrEmpty(nameTerms[i])) continue;
                                string esc = "%" + EscapeLike(nameTerms[i]) + "%";
                                st.BindText(bind++, esc);
                                st.BindText(bind++, esc);
                            }
                        }

                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            PackageRow r;
                            r.PackageUid = st.ColumnText(0) ?? "";
                            r.VarPath = st.ColumnText(1) ?? "";
                            r.LastWriteTicksOrInvalid = st.ColumnInt64(2);
                            r.PackageSizeOrInvalid = st.ColumnInt64(3);
                            r.PackageCreationTicksOrInvalid = st.ColumnInt64(4);
                            r.PackageIsLoaded = false;
                            string loadedTxt = st.ColumnText(5) ?? "";
                            int loadedInt = 0;
                            if (!string.IsNullOrEmpty(loadedTxt) && int.TryParse(loadedTxt, out loadedInt))
                                r.PackageIsLoaded = loadedInt != 0;
                            else
                                r.PackageIsLoaded = ComputePackageLoadedFlagFromVarPath(r.VarPath) != 0;
                            r.FirstScannedTicksOrInvalid = st.ColumnInt64(6);
                            if (!string.IsNullOrEmpty(r.PackageUid)) outRows.Add(r);
                        }
                    }
                }
                return true;
            }
            catch
            {
                outRows.Clear();
                return false;
            }
        }

        internal static bool TryCountVarPackages(string creatorFilter, string packagePathFilter, bool applyPathOnlyWhenCreator, out int count)
        {
            count = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                var creatorList = SplitCreatorFilterList(creatorFilter);
                bool hasCreator = creatorList.Count > 0;
                string normalized = "";
                bool hasPath = false;
                if (!string.IsNullOrEmpty(packagePathFilter))
                {
                    normalized = packagePathFilter.Replace('\\', '/').Trim().Trim('/');
                    hasPath = normalized.Length > 0;
                }
                bool applyPath = hasPath && (!applyPathOnlyWhenCreator || hasCreator);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    var sb = new StringBuilder(160);
                    sb.Append("SELECT COUNT(*) FROM pkg p WHERE 1=1");
                    if (hasCreator) AppendCreatorFilterSql(sb, "p.creator", creatorList);
                    if (applyPath)
                        sb.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        int b = 1;
                        if (hasCreator) BindCreatorFilterSql(st, ref b, creatorList);
                        if (applyPath) st.BindText(b++, EscapeLike(normalized.ToLowerInvariant()) + "/%");
                        if (st.Step() != VpbSqlite3.SqliteRow) return false;
                        if (!int.TryParse(st.ColumnText(0), out count)) count = (int)st.ColumnInt64(0);
                        return true;
                    }
                }
            }
            catch
            {
                count = 0;
                return false;
            }
        }

        /// <summary>
        /// Reads package rows for a scoped ordered list of UIDs, applying an AND-of-terms filter on <c>pkg.uid</c> using SQL LIKE.
        /// Returns false if the index is unavailable or stale.
        /// </summary>
        internal static bool TryQueryPackageRowsForUidsWithAllTerms(List<string> orderedUids, string[] termsLower, List<PackageRow> outRows)
        {
            outRows.Clear();
            if (orderedUids == null || orderedUids.Count == 0) return true;
            if (termsLower == null || termsLower.Length == 0) return TryQueryPackageRowsForUids(new HashSet<string>(orderedUids, StringComparer.OrdinalIgnoreCase), outRows);
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }

            // Require a published index for the current package inventory.
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                const int chunkSize = 350; // leave headroom for term binds
                var chunk = new List<string>(chunkSize);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    for (int i = 0; i < orderedUids.Count; i++)
                    {
                        string uid = orderedUids[i];
                        if (string.IsNullOrEmpty(uid)) continue;
                        chunk.Add(uid);
                        if (chunk.Count < chunkSize) continue;
                        if (!TryQueryPackageRowsChunkWithTerms(conn, chunk, termsLower, outRows)) return false;
                        chunk.Clear();
                    }
                    if (chunk.Count > 0)
                    {
                        if (!TryQueryPackageRowsChunkWithTerms(conn, chunk, termsLower, outRows)) return false;
                    }
                }
                return true;
            }
            catch
            {
                outRows.Clear();
                return false;
            }
        }

        private static bool TryQueryPackageRowsChunkWithTerms(VpbSqlite3.Connection conn, List<string> chunkUids, string[] termsLower, List<PackageRow> outRows)
        {
            if (conn == null || chunkUids == null || chunkUids.Count == 0) return true;

            var sb = new StringBuilder(128 + chunkUids.Count * 2 + (termsLower != null ? termsLower.Length * 32 : 0));
            sb.Append("SELECT uid, ifnull(var_path,''), wtime, psize, ifnull(ictime, pctime), ifnull(loaded,''), ifnull(first_scanned, 0) FROM pkg WHERE uid IN (");
            for (int i = 0; i < chunkUids.Count; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append('?');
            }
            sb.Append(')');
            if (termsLower != null && termsLower.Length > 0)
            {
                for (int t = 0; t < termsLower.Length; t++)
                {
                    if (string.IsNullOrEmpty(termsLower[t])) continue;
                    sb.Append(" AND uid LIKE ? ESCAPE '\\'");
                }
            }
            sb.Append(';');

            using (var st = conn.Prepare(sb.ToString()))
            {
                int bind = 1;
                for (int i = 0; i < chunkUids.Count; i++)
                    st.BindText(bind++, chunkUids[i]);

                if (termsLower != null && termsLower.Length > 0)
                {
                    for (int t = 0; t < termsLower.Length; t++)
                    {
                        string term = termsLower[t];
                        if (string.IsNullOrEmpty(term)) continue;
                        string pat = "%" + EscapeLike(term) + "%";
                        st.BindText(bind++, pat);
                    }
                }

                int step;
                while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                {
                    PackageRow r;
                    r.PackageUid = st.ColumnText(0) ?? "";
                    r.VarPath = st.ColumnText(1) ?? "";
                    r.LastWriteTicksOrInvalid = long.MinValue;
                    r.PackageSizeOrInvalid = long.MinValue;
                    r.PackageCreationTicksOrInvalid = long.MinValue;
                    r.FirstScannedTicksOrInvalid = 0L;
                    r.PackageIsLoaded = false;

                    string wtxt = st.ColumnText(2);
                    string sztxt = st.ColumnText(3);
                    string ctxt = st.ColumnText(4);
                    string loadedTxt = st.ColumnText(5) ?? "";
                    string fstxt = st.ColumnText(6);

                    long wtL, szL, ctL, fsL;
                    if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wtL))
                        r.LastWriteTicksOrInvalid = wtL;
                    if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out szL))
                        r.PackageSizeOrInvalid = szL;
                    if (!string.IsNullOrEmpty(ctxt) && long.TryParse(ctxt, out ctL))
                        r.PackageCreationTicksOrInvalid = ctL;
                    if (!string.IsNullOrEmpty(fstxt) && long.TryParse(fstxt, out fsL))
                        r.FirstScannedTicksOrInvalid = fsL;
                    int loadedInt = 0;
                    if (!string.IsNullOrEmpty(loadedTxt) && int.TryParse(loadedTxt, out loadedInt))
                        r.PackageIsLoaded = loadedInt != 0;
                    else
                        r.PackageIsLoaded = ComputePackageLoadedFlagFromVarPath(r.VarPath) != 0;

                    if (!string.IsNullOrEmpty(r.PackageUid))
                        outRows.Add(r);
                }
            }
            return true;
        }

        private static bool TryQueryPackageRowsChunk(VpbSqlite3.Connection conn, List<string> chunkUids, List<PackageRow> outRows)
        {
            if (conn == null || chunkUids == null || chunkUids.Count == 0) return true;

            var sb = new StringBuilder(96 + chunkUids.Count * 2);
            sb.Append("SELECT uid, ifnull(var_path,''), wtime, psize, ifnull(ictime, pctime), ifnull(loaded,''), ifnull(first_scanned, 0) FROM pkg WHERE uid IN (");
            for (int i = 0; i < chunkUids.Count; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append('?');
            }
            sb.Append(");");

            using (var st = conn.Prepare(sb.ToString()))
            {
                for (int i = 0; i < chunkUids.Count; i++)
                    st.BindText(i + 1, chunkUids[i]);

                int step;
                while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                {
                    PackageRow r;
                    r.PackageUid = st.ColumnText(0) ?? "";
                    r.VarPath = st.ColumnText(1) ?? "";
                    r.LastWriteTicksOrInvalid = long.MinValue;
                    r.PackageSizeOrInvalid = long.MinValue;
                    r.PackageCreationTicksOrInvalid = long.MinValue;
                    r.FirstScannedTicksOrInvalid = 0L;
                    r.PackageIsLoaded = false;

                    string wtxt = st.ColumnText(2);
                    string sztxt = st.ColumnText(3);
                    string ctxt = st.ColumnText(4);
                    string loadedTxt = st.ColumnText(5) ?? "";
                    string fstxt = st.ColumnText(6);

                    long wtL, szL, ctL, fsL;
                    if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wtL))
                        r.LastWriteTicksOrInvalid = wtL;
                    if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out szL))
                        r.PackageSizeOrInvalid = szL;
                    if (!string.IsNullOrEmpty(ctxt) && long.TryParse(ctxt, out ctL))
                        r.PackageCreationTicksOrInvalid = ctL;
                    if (!string.IsNullOrEmpty(fstxt) && long.TryParse(fstxt, out fsL))
                        r.FirstScannedTicksOrInvalid = fsL;
                    int loadedInt = 0;
                    if (!string.IsNullOrEmpty(loadedTxt) && int.TryParse(loadedTxt, out loadedInt))
                        r.PackageIsLoaded = loadedInt != 0;
                    else
                        r.PackageIsLoaded = ComputePackageLoadedFlagFromVarPath(r.VarPath) != 0;

                    if (!string.IsNullOrEmpty(r.PackageUid))
                        outRows.Add(r);
                }
            }
            return true;
        }

        internal static bool TryReadRecursiveDependencyUids(string srcUid, HashSet<string> outUids)
        {
            if (outUids == null) return false;
            outUids.Clear();
            if (string.IsNullOrEmpty(srcUid)) return true;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                using (var st = conn.Prepare("SELECT dep_uid FROM pkg_dep WHERE src_uid = ?"))
                {
                    st.BindText(1, srcUid);
                    int step;
                    while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                    {
                        string d = st.ColumnText(0) ?? "";
                        if (!string.IsNullOrEmpty(d))
                            outUids.Add(d);
                    }
                }
                return true;
            }
            catch
            {
                outUids.Clear();
                return false;
            }
        }

        internal static bool TryReadDependentUids(string targetUid, string targetShort, HashSet<string> outUids)
        {
            if (outUids == null) return false;
            outUids.Clear();
            if (string.IsNullOrEmpty(targetUid)) return true;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    // Exact UID match always.
                    using (var st = conn.Prepare("SELECT DISTINCT src_uid FROM pkg_dep WHERE dep_uid = ?"))
                    {
                        st.BindText(1, targetUid);
                        int step;
                        while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string u = st.ColumnText(0) ?? "";
                            if (!string.IsNullOrEmpty(u))
                                outUids.Add(u);
                        }
                    }

                    // Group match (Author.Name.*): includes .latest, .minX, numeric versions, etc.
                    if (!string.IsNullOrEmpty(targetShort))
                    {
                        using (var st2 = conn.Prepare("SELECT DISTINCT src_uid FROM pkg_dep WHERE dep_uid LIKE ?"))
                        {
                            st2.BindText(1, targetShort + ".%");
                            int step2;
                            while ((step2 = st2.Step()) == VpbSqlite3.SqliteRow)
                            {
                                string u = st2.ColumnText(0) ?? "";
                                if (!string.IsNullOrEmpty(u))
                                    outUids.Add(u);
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                outUids.Clear();
                return false;
            }
        }

        internal static bool TryReadIndexedPackageGroups(List<string> outGroups)
        {
            if (outGroups == null) return false;
            outGroups.Clear();
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                using (var st = conn.Prepare("SELECT uid FROM pkg"))
                {
                    for (;;)
                    {
                        int rc = st.Step();
                        if (rc == VpbSqlite3.SqliteDone) break;
                        if (rc != VpbSqlite3.SqliteRow) break;
                        string uid = st.ColumnText(0) ?? "";
                        if (TryGetPackageGroupFromUid(uid, out string group))
                            groups.Add(group);
                    }
                }
                outGroups.AddRange(groups);
                outGroups.Sort(StringComparer.OrdinalIgnoreCase);
                return true;
            }
            catch
            {
                outGroups.Clear();
                return false;
            }
        }

        internal static bool TryResolveIndexedVarPathForUid(string uid, out string varPath)
        {
            varPath = null;
            if (string.IsNullOrEmpty(uid)) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                using (var st = conn.Prepare("SELECT ifnull(var_path,'') FROM pkg WHERE uid = ? LIMIT 1"))
                {
                    st.BindText(1, uid);
                    if (st.Step() != VpbSqlite3.SqliteRow) return false;
                    string p = st.ColumnText(0) ?? "";
                    if (string.IsNullOrEmpty(p)) return false;
                    varPath = p;
                    return true;
                }
            }
            catch
            {
                varPath = null;
                return false;
            }
        }

        internal static bool TryResolveLatestUidFromIndex(string packageGroup, out string resolvedUid)
        {
            resolvedUid = null;
            if (string.IsNullOrEmpty(packageGroup)) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
            {
                AutoScheduleRebuildIfStale(scanBin, readyScan, catSig);
                return false;
            }

            try
            {
                int bestVersion = -1;
                string bestUid = null;
                using (var conn = new VpbSqlite3.Connection(DbPath))
                using (var st = conn.Prepare("SELECT uid FROM pkg WHERE uid LIKE ?"))
                {
                    st.BindText(1, packageGroup + ".%");
                    for (;;)
                    {
                        int rc = st.Step();
                        if (rc == VpbSqlite3.SqliteDone) break;
                        if (rc != VpbSqlite3.SqliteRow) break;
                        string uid = st.ColumnText(0) ?? "";
                        if (!TryParseUidVersion(uid, packageGroup, out int version)) continue;
                        if (version > bestVersion)
                        {
                            bestVersion = version;
                            bestUid = uid;
                        }
                    }
                }

                if (string.IsNullOrEmpty(bestUid)) return false;
                resolvedUid = bestUid;
                return true;
            }
            catch
            {
                resolvedUid = null;
                return false;
            }
        }

        private static bool TryGetPackageGroupFromUid(string uid, out string packageGroup)
        {
            packageGroup = null;
            if (string.IsNullOrEmpty(uid)) return false;
            int lastDot = uid.LastIndexOf('.');
            if (lastDot <= 0) return false;
            string maybeGroup = uid.Substring(0, lastDot);
            if (string.IsNullOrEmpty(maybeGroup)) return false;
            int firstDot = maybeGroup.IndexOf('.');
            if (firstDot <= 0) return false;
            if (maybeGroup.IndexOf('.', firstDot + 1) != -1) return false;
            packageGroup = maybeGroup;
            return true;
        }

        private static bool TryParseUidVersion(string uid, string packageGroup, out int version)
        {
            version = -1;
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(packageGroup)) return false;
            string prefix = packageGroup + ".";
            if (!uid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string suffix = uid.Substring(prefix.Length);
            if (string.IsNullOrEmpty(suffix)) return false;
            return int.TryParse(suffix, out version);
        }
    }
}
