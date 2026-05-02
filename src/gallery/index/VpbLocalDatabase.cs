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
    internal static class VpbLocalDatabase
    {
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
            public bool PackageIsLoaded;
        }

        /// <summary>High bit: row has <see cref="ClothingAttrPacked"/> from index rebuild (fast clothing subfilter path).</summary>
        internal const int ClothingAttrPresentFlag = unchecked((int)0x80000000);

        private const int SchemaVersion = 11;

        private static readonly object s_Sync = new object();
        private static volatile bool s_RebuildScheduled;
        private static volatile bool s_RebuildRunning;
        private static long s_ReadyScanBinary = long.MinValue;
        private static string s_ReadyCategoriesSig;
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
                "CREATE INDEX IF NOT EXISTS idx_cfs_panel ON cat_filter_state(panel_id);" +
                "CREATE TABLE IF NOT EXISTS gallery_user_tag (tag_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);" +
                "CREATE TABLE IF NOT EXISTS gallery_item_user_tag (category TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, tag_id INTEGER NOT NULL, PRIMARY KEY(category, pkg_uid, internal_path, tag_id), FOREIGN KEY(tag_id) REFERENCES gallery_user_tag(tag_id) ON DELETE CASCADE);" +
                "CREATE INDEX IF NOT EXISTS idx_giut_tag ON gallery_item_user_tag(tag_id);" +
                "CREATE INDEX IF NOT EXISTS idx_giut_pkg_path ON gallery_item_user_tag(pkg_uid, internal_path);");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN var_path TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE cat_mem ADD COLUMN list_path TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN pctime TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE cat_mem ADD COLUMN cloth_attr TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN loaded INTEGER;");
            TryEnsureGalleryItemUserTagSchemaV11(conn);
            BumpMetaSchemaVersionAfterUserTagTables(conn);
        }

        /// <summary>
        /// v11: FK on <c>gallery_item_user_tag.tag_id</c>, index for ALL‑VAR <c>(pkg_uid, internal_path)</c>, drop redundant <c>idx_giut_lookup</c>.
        /// Rebuilds link table when existing DB predates FK (SQLite cannot ALTER ADD FK).
        /// </summary>
        private static void TryEnsureGalleryItemUserTagSchemaV11(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            try
            {
                string tblSql;
                using (var st = conn.Prepare("SELECT sql FROM sqlite_master WHERE type='table' AND name='gallery_item_user_tag'"))
                {
                    if (st.Step() != VpbSqlite3.SqliteRow) return;
                    tblSql = st.ColumnText(0) ?? "";
                }
                if (string.IsNullOrEmpty(tblSql)) return;

                bool hasFk = tblSql.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) >= 0
                    && tblSql.IndexOf("gallery_user_tag", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hasFk)
                {
                    conn.ExecUtf8("PRAGMA foreign_keys=OFF;");
                    conn.ExecUtf8("BEGIN;");
                    try
                    {
                        conn.ExecUtf8("DROP TABLE IF EXISTS gallery_item_user_tag__v11;");
                        conn.ExecUtf8(
                            "CREATE TABLE gallery_item_user_tag__v11 (" +
                            "category TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, tag_id INTEGER NOT NULL, " +
                            "PRIMARY KEY(category, pkg_uid, internal_path, tag_id), " +
                            "FOREIGN KEY(tag_id) REFERENCES gallery_user_tag(tag_id) ON DELETE CASCADE);");
                        conn.ExecUtf8(
                            "INSERT INTO gallery_item_user_tag__v11(category, pkg_uid, internal_path, tag_id) " +
                            "SELECT category, pkg_uid, internal_path, tag_id FROM gallery_item_user_tag " +
                            "WHERE tag_id IN (SELECT tag_id FROM gallery_user_tag);");
                        conn.ExecUtf8("DROP TABLE gallery_item_user_tag;");
                        conn.ExecUtf8("ALTER TABLE gallery_item_user_tag__v11 RENAME TO gallery_item_user_tag;");
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                    finally
                    {
                        try { conn.ExecUtf8("PRAGMA foreign_keys=ON;"); } catch { }
                    }
                }

                conn.ExecUtf8("DROP INDEX IF EXISTS idx_giut_lookup;");
                conn.ExecUtf8("CREATE INDEX IF NOT EXISTS idx_giut_tag ON gallery_item_user_tag(tag_id);");
                conn.ExecUtf8("CREATE INDEX IF NOT EXISTS idx_giut_pkg_path ON gallery_item_user_tag(pkg_uid, internal_path);");
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] VpbLocalDatabase: gallery_item_user_tag v11 migration failed: " + ex.Message); } catch { }
            }
        }

        internal const int GalleryUserTagNameMaxLength = 30;
        internal const int GalleryUserTagVocabularyMaxCount = 10000;
        internal const int GalleryUserTagMaxPerItem = 100;
        internal const int GalleryUserTagPasteMaxUniqueNames = 10000;

        /// <summary>pkg_uid for on-disk files outside a .var (Custom/, Saves/, etc.) in <c>gallery_item_user_tag</c>.</summary>
        internal const string GalleryUserTagLoosePkgUid = "__local__";

        /// <summary>Stable relative path (forward slashes) from first VAM root segment for loose file user-tag rows.</summary>
        internal static string NormalizeLoosePathForGalleryUserTag(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string p = path.Replace('\\', '/');
            string[] anchors = { "Custom/", "Saves/", "AddonPackages/", "AllPackages/" };
            for (int i = 0; i < anchors.Length; i++)
            {
                int idx = p.IndexOf(anchors[i], StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) return p.Substring(idx);
            }
            return p;
        }

        /// <summary>
        /// Normalize gallery user tag: trim, lowercase, collapse whitespace to single spaces (tabs/newlines → space, never stored as tab),
        /// allow letters, digits, <c>-</c>/<c>_</c>, single internal spaces; reject other characters; length 1–<see cref="GalleryUserTagNameMaxLength"/>.
        /// </summary>
        internal static string NormalizeGalleryUserTagName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Trim().ToLowerInvariant();
            if (s.Length == 0) return "";
            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!prevSpace && sb.Length > 0) { sb.Append(' '); prevSpace = true; }
                    continue;
                }
                if (!IsGalleryUserTagAllowedNonSpaceChar(c))
                    return "";
                sb.Append(c);
                prevSpace = false;
            }
            string r = sb.ToString().Trim();
            if (r.Length == 0 || r.Length > GalleryUserTagNameMaxLength) return "";
            return r;
        }

        private static bool IsGalleryUserTagAllowedNonSpaceChar(char c)
        {
            if (c == '-' || c == '_') return true;
            return char.IsLetter(c) || char.IsDigit(c);
        }

        private static void BumpMetaSchemaVersionAfterUserTagTables(VpbSqlite3.Connection conn)
        {
            try
            {
                string v = MetaGet(conn, "schema_version");
                int mv;
                if (int.TryParse(v, out mv) && mv >= SchemaVersion) return;
                using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                {
                    st.BindText(1, "schema_version");
                    st.BindText(2, SchemaVersion.ToString());
                    st.Step();
                }
            }
            catch { }
        }

        private static void AppendSqlActiveUserTagExists(StringBuilder sb, List<string> bindNamesOut, HashSet<string> activeUserTags, string mAlias)
        {
            if (activeUserTags == null || activeUserTags.Count == 0 || bindNamesOut == null) return;
            foreach (var raw in activeUserTags)
            {
                string n = NormalizeGalleryUserTagName(raw);
                if (string.IsNullOrEmpty(n)) continue;
                bindNamesOut.Add(n);
                sb.Append(" AND EXISTS (SELECT 1 FROM gallery_item_user_tag gut");
                sb.Append(" INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id");
                sb.Append(" WHERE gut.category=").Append(mAlias).Append(".category");
                sb.Append(" AND gut.pkg_uid=").Append(mAlias).Append(".pkg_uid");
                sb.Append(" AND gut.internal_path=").Append(mAlias).Append(".internal_path");
                sb.Append(" AND gt.name=?)");
            }
        }

        /// <summary>Side tab: distinct user tag names with counts for current category (+ creator/path filters).</summary>
        internal static bool TryReadGalleryUserTagSideTabCounts(
            string categoryTitle,
            string creatorFilter,
            string packagePathFilter,
            Dictionary<string, int> countsOut)
        {
            countsOut?.Clear();
            if (!VpbSqlite3.IsAvailable || countsOut == null) return false;
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

            string normalizedPackagePathFilter = "";
            bool hasPackagePathFilter = false;
            if (!string.IsNullOrEmpty(packagePathFilter))
            {
                normalizedPackagePathFilter = packagePathFilter.Replace('\\', '/').Trim().Trim('/');
                hasPackagePathFilter = normalizedPackagePathFilter.Length > 0;
            }
            bool hasCreator = !string.IsNullOrEmpty(creatorFilter);

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    var sb = new StringBuilder(512);
                    sb.Append("SELECT gt.name, COUNT(*) FROM gallery_item_user_tag gut");
                    sb.Append(" INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id");
                    sb.Append(" INNER JOIN cat_mem m ON m.category=gut.category AND m.pkg_uid=gut.pkg_uid AND m.internal_path=gut.internal_path");
                    sb.Append(" INNER JOIN pkg p ON p.uid=m.pkg_uid");
                    sb.Append(" WHERE gut.category=?");
                    if (hasCreator) sb.Append(" AND p.creator=?");
                    if (hasPackagePathFilter)
                        sb.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    sb.Append(" GROUP BY gt.name");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        if (hasCreator) stmt.BindText(bind++, creatorFilter);
                        if (hasPackagePathFilter)
                            stmt.BindText(bind++, EscapeLike(normalizedPackagePathFilter.ToLowerInvariant()) + "/%");

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string name = stmt.ColumnText(0) ?? "";
                            int n;
                            if (!int.TryParse(stmt.ColumnText(1), out n)) n = (int)stmt.ColumnInt64(1);
                            if (!string.IsNullOrEmpty(name)) countsOut[name] = n;
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

        /// <summary>All distinct gallery user tag names (pick list vocabulary). Order: case-insensitive.</summary>
        internal static bool TryReadAllGalleryUserTagNames(List<string> namesOut)
        {
            namesOut?.Clear();
            if (!VpbSqlite3.IsAvailable || namesOut == null) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var stmt = conn.Prepare("SELECT name FROM gallery_user_tag ORDER BY name"))
                    {
                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string name = stmt.ColumnText(0) ?? "";
                            if (!string.IsNullOrEmpty(name)) namesOut.Add(name);
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

        /// <summary>One gallery row ↔ user tag link (for YAML export).</summary>
        internal struct GalleryUserTagAssignmentRow
        {
            public string TagName;
            public string Category;
            public string PkgUid;
            public string InternalPath;
        }

        /// <summary>All <c>gallery_item_user_tag</c> rows with tag names (full assignment table).</summary>
        internal static bool TryReadAllGalleryUserTagAssignments(List<GalleryUserTagAssignmentRow> rowsOut)
        {
            rowsOut?.Clear();
            if (!VpbSqlite3.IsAvailable || rowsOut == null) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var stmt = conn.Prepare(
                        "SELECT gt.name, gut.category, gut.pkg_uid, gut.internal_path FROM gallery_item_user_tag gut " +
                        "INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id " +
                        "ORDER BY gt.name, gut.category, gut.pkg_uid, gut.internal_path"))
                    {
                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string tag = stmt.ColumnText(0) ?? "";
                            string cat = stmt.ColumnText(1) ?? "";
                            string pkg = stmt.ColumnText(2) ?? "";
                            string ip = stmt.ColumnText(3) ?? "";
                            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(cat) || string.IsNullOrEmpty(pkg) || string.IsNullOrEmpty(ip))
                                continue;
                            rowsOut.Add(new GalleryUserTagAssignmentRow
                            {
                                TagName = tag,
                                Category = cat,
                                PkgUid = pkg,
                                InternalPath = ip
                            });
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

        internal static long TryGetOrCreateGalleryUserTagId(VpbSqlite3.Connection conn, string normalizedName)
        {
            if (conn == null || string.IsNullOrEmpty(normalizedName)) return -1;
            try
            {
                using (var sel0 = conn.Prepare("SELECT tag_id FROM gallery_user_tag WHERE name=?"))
                {
                    sel0.BindText(1, normalizedName);
                    if (sel0.Step() == VpbSqlite3.SqliteRow)
                        return sel0.ColumnInt64(0);
                }
                using (var cnt = conn.Prepare("SELECT COUNT(*) FROM gallery_user_tag"))
                {
                    if (cnt.Step() == VpbSqlite3.SqliteRow && cnt.ColumnInt64(0) >= GalleryUserTagVocabularyMaxCount)
                        return -1;
                }
                using (var ins = conn.Prepare("INSERT OR IGNORE INTO gallery_user_tag(name) VALUES(?)"))
                {
                    ins.BindText(1, normalizedName);
                    ins.Step();
                }
                using (var sel = conn.Prepare("SELECT tag_id FROM gallery_user_tag WHERE name=?"))
                {
                    sel.BindText(1, normalizedName);
                    if (sel.Step() == VpbSqlite3.SqliteRow)
                        return sel.ColumnInt64(0);
                }
            }
            catch { }
            return -1;
        }

        /// <summary>Assign normalized tags to one indexed row. Creates tag rows as needed.</summary>
        internal static bool TryAssignGalleryUserTagsToRow(string categoryTitle, string pkgUid, string internalPath, IEnumerable<string> normalizedTagNames, out int inserted)
        {
            inserted = 0;
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(categoryTitle) || string.IsNullOrEmpty(pkgUid) || string.IsNullOrEmpty(internalPath))
                return false;
            if (normalizedTagNames == null) return true;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    var existingOnRow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var ex = conn.Prepare(
                        "SELECT gt.name FROM gallery_item_user_tag gut INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id WHERE gut.category=? AND gut.pkg_uid=? AND gut.internal_path=?"))
                    {
                        ex.BindText(1, categoryTitle);
                        ex.BindText(2, pkgUid);
                        ex.BindText(3, internalPath);
                        while (ex.Step() == VpbSqlite3.SqliteRow)
                        {
                            string n = ex.ColumnText(0);
                            if (!string.IsNullOrEmpty(n)) existingOnRow.Add(n);
                        }
                    }
                    int rowTagCount = existingOnRow.Count;
                    conn.ExecUtf8("BEGIN;");
                    try
                    {
                        using (var insIt = conn.Prepare(
                            "INSERT OR IGNORE INTO gallery_item_user_tag(category, pkg_uid, internal_path, tag_id) VALUES(?,?,?,?)"))
                        {
                            foreach (var rawName in normalizedTagNames)
                            {
                                string name = NormalizeGalleryUserTagName(rawName);
                                if (string.IsNullOrEmpty(name)) continue;
                                if (existingOnRow.Contains(name)) continue;
                                if (rowTagCount >= GalleryUserTagMaxPerItem) break;
                                long tid = TryGetOrCreateGalleryUserTagId(conn, name);
                                if (tid < 0) continue;
                                insIt.BindText(1, categoryTitle);
                                insIt.BindText(2, pkgUid);
                                insIt.BindText(3, internalPath);
                                insIt.BindInt64(4, tid);
                                insIt.Step();
                                insIt.Reset();
                                inserted++;
                                existingOnRow.Add(name);
                                rowTagCount++;
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                        return true;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch
            {
                inserted = 0;
                return false;
            }
        }

        internal struct GalleryUserTagRowKey
        {
            public string Category;
            public string PkgUid;
            public string InternalPath;
        }

        private static long QueryTotalChanges(VpbSqlite3.Connection conn, VpbSqlite3.Statement stmtTotalChanges)
        {
            if (conn == null || stmtTotalChanges == null) return -1;
            try
            {
                stmtTotalChanges.Reset();
                if (stmtTotalChanges.Step() != VpbSqlite3.SqliteRow) return -1;
                return stmtTotalChanges.ColumnInt64(0);
            }
            catch { return -1; }
        }

        internal static bool TryAssignGalleryUserTagsToManyRows(List<GalleryUserTagRowKey> rows, IEnumerable<string> normalizedTagNames, out int rowsTouched)
        {
            rowsTouched = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            if (rows == null || rows.Count == 0) return true;
            if (normalizedTagNames == null) return true;

            var tagIds = new List<long>(32);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in normalizedTagNames)
            {
                string n = NormalizeGalleryUserTagName(raw);
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
                tagIds.Add(-1);
            }
            if (seen.Count == 0) return true;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    // Resolve tag IDs once.
                    tagIds.Clear();
                    foreach (var raw in normalizedTagNames)
                    {
                        string n = NormalizeGalleryUserTagName(raw);
                        if (string.IsNullOrEmpty(n) || !seen.Contains(n)) continue;
                        long tid = TryGetOrCreateGalleryUserTagId(conn, n);
                        if (tid >= 0) tagIds.Add(tid);
                    }
                    if (tagIds.Count == 0) return true;

                    using (var stCount = conn.Prepare("SELECT COUNT(*) FROM gallery_item_user_tag WHERE category=? AND pkg_uid=? AND internal_path=?"))
                    using (var stIns = conn.Prepare("INSERT OR IGNORE INTO gallery_item_user_tag(category, pkg_uid, internal_path, tag_id) VALUES(?,?,?,?)"))
                    using (var stTotalChanges = conn.Prepare("SELECT total_changes()"))
                    {
                        conn.ExecUtf8("BEGIN;");
                        try
                        {
                            for (int i = 0; i < rows.Count; i++)
                            {
                                var rk = rows[i];
                                if (string.IsNullOrEmpty(rk.Category) || string.IsNullOrEmpty(rk.PkgUid) || string.IsNullOrEmpty(rk.InternalPath))
                                    continue;

                                stCount.Reset();
                                stCount.BindText(1, rk.Category);
                                stCount.BindText(2, rk.PkgUid);
                                stCount.BindText(3, rk.InternalPath);
                                long cur = 0;
                                if (stCount.Step() == VpbSqlite3.SqliteRow)
                                    cur = stCount.ColumnInt64(0);
                                if (cur >= GalleryUserTagMaxPerItem) continue;

                                long before = QueryTotalChanges(conn, stTotalChanges);

                                for (int ti = 0; ti < tagIds.Count; ti++)
                                {
                                    if (cur >= GalleryUserTagMaxPerItem) break;
                                    long tid = tagIds[ti];
                                    if (tid < 0) continue;
                                    stIns.BindText(1, rk.Category);
                                    stIns.BindText(2, rk.PkgUid);
                                    stIns.BindText(3, rk.InternalPath);
                                    stIns.BindInt64(4, tid);
                                    stIns.Step();
                                    stIns.Reset();
                                    cur++;
                                }

                                long after = QueryTotalChanges(conn, stTotalChanges);
                                if (before >= 0 && after >= 0 && after > before)
                                    rowsTouched++;
                            }

                            conn.ExecUtf8("COMMIT;");
                            return true;
                        }
                        catch
                        {
                            try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                            throw;
                        }
                    }
                }
            }
            catch
            {
                rowsTouched = 0;
                return false;
            }
        }

        internal static bool TryRemoveGalleryUserTagsFromManyRows(List<GalleryUserTagRowKey> rows, IEnumerable<string> normalizedTagNames, out int rowsTouched)
        {
            rowsTouched = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            if (rows == null || rows.Count == 0) return true;
            if (normalizedTagNames == null) return true;

            var tagIds = new List<long>(32);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in normalizedTagNames)
            {
                string n = NormalizeGalleryUserTagName(raw);
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
            }
            if (seen.Count == 0) return true;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    // Resolve tag IDs once; missing tags => no-op.
                    using (var stSel = conn.Prepare("SELECT tag_id FROM gallery_user_tag WHERE name=?"))
                    {
                        foreach (var n in seen)
                        {
                            stSel.Reset();
                            stSel.BindText(1, n);
                            if (stSel.Step() == VpbSqlite3.SqliteRow)
                            {
                                long tid = stSel.ColumnInt64(0);
                                if (tid >= 0) tagIds.Add(tid);
                            }
                        }
                    }
                    if (tagIds.Count == 0) return true;

                    using (var stDel = conn.Prepare("DELETE FROM gallery_item_user_tag WHERE category=? AND pkg_uid=? AND internal_path=? AND tag_id=?"))
                    using (var stTotalChanges = conn.Prepare("SELECT total_changes()"))
                    {
                        conn.ExecUtf8("BEGIN;");
                        try
                        {
                            for (int i = 0; i < rows.Count; i++)
                            {
                                var rk = rows[i];
                                if (string.IsNullOrEmpty(rk.Category) || string.IsNullOrEmpty(rk.PkgUid) || string.IsNullOrEmpty(rk.InternalPath))
                                    continue;

                                long before = QueryTotalChanges(conn, stTotalChanges);
                                for (int ti = 0; ti < tagIds.Count; ti++)
                                {
                                    long tid = tagIds[ti];
                                    if (tid < 0) continue;
                                    stDel.BindText(1, rk.Category);
                                    stDel.BindText(2, rk.PkgUid);
                                    stDel.BindText(3, rk.InternalPath);
                                    stDel.BindInt64(4, tid);
                                    stDel.Step();
                                    stDel.Reset();
                                }
                                long after = QueryTotalChanges(conn, stTotalChanges);
                                if (before >= 0 && after >= 0 && after > before)
                                    rowsTouched++;
                            }

                            conn.ExecUtf8("COMMIT;");
                            return true;
                        }
                        catch
                        {
                            try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                            throw;
                        }
                    }
                }
            }
            catch
            {
                rowsTouched = 0;
                return false;
            }
        }

        internal static bool TryRemoveGalleryUserTagsFromRow(string categoryTitle, string pkgUid, string internalPath, IEnumerable<string> normalizedTagNames, out int deleted)
        {
            deleted = 0;
            if (!VpbSqlite3.IsAvailable || normalizedTagNames == null) return true;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    foreach (var rawName in normalizedTagNames)
                    {
                        string name = NormalizeGalleryUserTagName(rawName);
                        if (string.IsNullOrEmpty(name)) continue;
                        using (var del = conn.Prepare(
                            "DELETE FROM gallery_item_user_tag WHERE category=? AND pkg_uid=? AND internal_path=? AND tag_id=(SELECT tag_id FROM gallery_user_tag WHERE name=?)"))
                        {
                            del.BindText(1, categoryTitle);
                            del.BindText(2, pkgUid);
                            del.BindText(3, internalPath);
                            del.BindText(4, name);
                            del.Step();
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

        /// <summary>Package-level pseudo-category: not represented as <c>cat_mem.category</c>; tags use real browse categories (or this name when applied here).</summary>
        internal static bool IsGalleryAllVarPseudoCategory(string categoryTitle)
        {
            return !string.IsNullOrEmpty(categoryTitle)
                && string.Equals(categoryTitle.Trim(), "ALL VAR", StringComparison.OrdinalIgnoreCase);
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

        /// <summary>Format for <see cref="TryBuildCatMemRowKeysMatchingAllUserTags"/> / package enumeration lookup (internal path uses /).</summary>
        internal static string FormatCatMemRowLookupKey(string pkgUid, string internalPath)
        {
            string ip = string.IsNullOrEmpty(internalPath) ? "" : internalPath.Replace('\\', '/');
            return string.Concat(pkgUid ?? "", "\x1F", ip);
        }

        /// <summary>
        /// One query: all cat_mem rows in <paramref name="categoryTitle"/> that satisfy AND user-tag EXISTS clauses.
        /// Used when category SQLite bulk query falls back to package scan — avoids per-row <see cref="TryGalleryRowMatchesAllUserTags"/> on UI thread.
        /// </summary>
        internal static bool TryBuildCatMemRowKeysMatchingAllUserTags(
            string categoryTitle,
            HashSet<string> activeUserTags,
            HashSet<string> keysOut)
        {
            keysOut?.Clear();
            if (keysOut == null) return false;
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(categoryTitle)) return false;
            if (activeUserTags == null || activeUserTags.Count == 0) return true;
            bool anyNormTag = false;
            foreach (var raw in activeUserTags)
            {
                if (!string.IsNullOrEmpty(NormalizeGalleryUserTagName(raw)))
                {
                    anyNormTag = true;
                    break;
                }
            }
            if (!anyNormTag) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    // ALL VAR: no cat_mem rows use category "ALL VAR"; tags live under real categories (Appearance, …).
                    // Match (pkg_uid, internal_path) that have every selected tag on any gut.category row.
                    if (IsGalleryAllVarPseudoCategory(categoryTitle))
                        return TryBuildAllVarPkgInternalPathKeysMatchingAllUserTags(conn, activeUserTags, keysOut);

                    var bindNames = new List<string>();
                    var sb = new StringBuilder();
                    sb.Append("SELECT m.pkg_uid, m.internal_path FROM cat_mem m WHERE m.category=?");
                    AppendSqlActiveUserTagExists(sb, bindNames, activeUserTags, "m");
                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        for (int i = 0; i < bindNames.Count; i++)
                            stmt.BindText(bind++, bindNames[i]);
                        while (stmt.Step() == VpbSqlite3.SqliteRow)
                        {
                            string pu = stmt.ColumnText(0) ?? "";
                            string ip = stmt.ColumnText(1) ?? "";
                            keysOut.Add(FormatCatMemRowLookupKey(pu, ip));
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

        /// <summary>
        /// Distinct (pkg_uid, internal_path) that have every requested tag (AND), ignoring <c>gallery_item_user_tag.category</c>.
        /// </summary>
        private static bool TryBuildAllVarPkgInternalPathKeysMatchingAllUserTags(
            VpbSqlite3.Connection conn,
            HashSet<string> activeUserTags,
            HashSet<string> keysOut)
        {
            keysOut?.Clear();
            if (keysOut == null || conn == null) return false;
            var distinctNeed = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in activeUserTags)
            {
                string n = NormalizeGalleryUserTagName(raw);
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
                distinctNeed.Add(n);
            }
            if (distinctNeed.Count == 0) return false;

            try
            {
                var sb = new StringBuilder();
                sb.Append("SELECT gut.pkg_uid, gut.internal_path FROM gallery_item_user_tag gut INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id WHERE gt.name IN (");
                for (int i = 0; i < distinctNeed.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('?');
                }
                sb.Append(") GROUP BY gut.pkg_uid, gut.internal_path HAVING COUNT(DISTINCT gt.name)=");
                sb.Append(distinctNeed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using (var stmt = conn.Prepare(sb.ToString()))
                {
                    int bind = 1;
                    for (int i = 0; i < distinctNeed.Count; i++)
                        stmt.BindText(bind++, distinctNeed[i]);
                    while (stmt.Step() == VpbSqlite3.SqliteRow)
                    {
                        string pu = stmt.ColumnText(0) ?? "";
                        string ip = stmt.ColumnText(1) ?? "";
                        keysOut.Add(FormatCatMemRowLookupKey(pu, ip));
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when row has every listed user tag (AND). Names must already be normalized.</summary>
        internal static bool TryGalleryRowMatchesAllUserTags(string categoryTitle, string pkgUid, string internalPath, HashSet<string> normalizedUserTags)
        {
            if (normalizedUserTags == null || normalizedUserTags.Count == 0) return true;
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(categoryTitle)) return false;
            var distinctNeed = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in normalizedUserTags)
            {
                string n = NormalizeGalleryUserTagName(t);
                if (string.IsNullOrEmpty(n) || !seenNames.Add(n)) continue;
                distinctNeed.Add(n);
            }
            if (distinctNeed.Count == 0) return true;
            bool allVarPseudo = IsGalleryAllVarPseudoCategory(categoryTitle);
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    var sb = new StringBuilder(120 + distinctNeed.Count * 4);
                    sb.Append("SELECT COUNT(DISTINCT gt.name) FROM gallery_item_user_tag gut");
                    sb.Append(" INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id");
                    if (allVarPseudo)
                    {
                        // Tags are stored under the browse category where Apply ran, not "ALL VAR".
                        sb.Append(" WHERE gut.pkg_uid=? AND gut.internal_path=? AND gt.name IN (");
                    }
                    else
                    {
                        sb.Append(" WHERE gut.category=? AND gut.pkg_uid=? AND gut.internal_path=? AND gt.name IN (");
                    }
                    for (int i = 0; i < distinctNeed.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('?');
                    }
                    sb.Append(')');
                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        int b = 1;
                        if (!allVarPseudo)
                            st.BindText(b++, categoryTitle);
                        st.BindText(b++, pkgUid ?? "");
                        st.BindText(b++, internalPath ?? "");
                        for (int i = 0; i < distinctNeed.Count; i++)
                            st.BindText(b++, distinctNeed[i]);
                        if (st.Step() != VpbSqlite3.SqliteRow) return false;
                        long cnt = st.ColumnInt64(0);
                        return cnt >= distinctNeed.Count;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Tags on one indexed row; reuses <paramref name="conn"/> (one connection for many rows — selection pane, batch export).</summary>
        internal static bool TryGetGalleryUserTagsForRow(VpbSqlite3.Connection conn, string categoryTitle, string pkgUid, string internalPath, HashSet<string> outNames)
        {
            outNames?.Clear();
            if (conn == null || outNames == null || string.IsNullOrEmpty(categoryTitle)) return false;
            try
            {
                bool allVarPseudo = IsGalleryAllVarPseudoCategory(categoryTitle);
                // ALL VAR browse: tags live under real categories; union names for this pkg/path (Applied pane, exports).
                string sql = allVarPseudo
                    ? "SELECT DISTINCT gt.name FROM gallery_item_user_tag gut INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id WHERE gut.pkg_uid=? AND gut.internal_path=?"
                    : "SELECT gt.name FROM gallery_item_user_tag gut INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id WHERE gut.category=? AND gut.pkg_uid=? AND gut.internal_path=?";
                using (var st = conn.Prepare(sql))
                {
                    if (allVarPseudo)
                    {
                        st.BindText(1, pkgUid ?? "");
                        st.BindText(2, internalPath ?? "");
                    }
                    else
                    {
                        st.BindText(1, categoryTitle);
                        st.BindText(2, pkgUid ?? "");
                        st.BindText(3, internalPath ?? "");
                    }
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string n = st.ColumnText(0);
                        if (!string.IsNullOrEmpty(n)) outNames.Add(n);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryGetGalleryUserTagsForRow(string categoryTitle, string pkgUid, string internalPath, HashSet<string> outNames)
        {
            if (!VpbSqlite3.IsAvailable || outNames == null || string.IsNullOrEmpty(categoryTitle)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    return TryGetGalleryUserTagsForRow(conn, categoryTitle, pkgUid, internalPath, outNames);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True if <c>gallery_item_user_tag</c> has at least one row for this item (lightweight for grid badge).</summary>
        internal static bool TryHasAnyGalleryUserTagsForRow(string categoryTitle, string pkgUid, string internalPath)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(categoryTitle)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    return TryHasAnyGalleryUserTagsForRow(conn, categoryTitle, pkgUid, internalPath);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// True if any row in <c>gallery_item_user_tag</c> exists for this package (any internal_path).
        /// Used for ALL VAR package rows when tags were applied to child items (inherit mode).
        /// </summary>
        internal static bool TryHasAnyGalleryUserTagsForPackageAnyPath(string pkgUid)
        {
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    return TryHasAnyGalleryUserTagsForPackageAnyPath(conn, pkgUid);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryHasAnyGalleryUserTagsForRow(VpbSqlite3.Connection conn, string categoryTitle, string pkgUid, string internalPath)
        {
            if (conn == null || string.IsNullOrEmpty(categoryTitle)) return false;
            try
            {
                bool allVarPseudo = IsGalleryAllVarPseudoCategory(categoryTitle);
                string sql = allVarPseudo
                    ? "SELECT 1 FROM gallery_item_user_tag WHERE pkg_uid=? AND internal_path=? LIMIT 1"
                    : "SELECT 1 FROM gallery_item_user_tag WHERE category=? AND pkg_uid=? AND internal_path=? LIMIT 1";
                using (var st = conn.Prepare(sql))
                {
                    if (allVarPseudo)
                    {
                        st.BindText(1, pkgUid ?? "");
                        st.BindText(2, internalPath ?? "");
                    }
                    else
                    {
                        st.BindText(1, categoryTitle);
                        st.BindText(2, pkgUid ?? "");
                        st.BindText(3, internalPath ?? "");
                    }
                    return st.Step() == VpbSqlite3.SqliteRow;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryHasAnyGalleryUserTagsForPackageAnyPath(VpbSqlite3.Connection conn, string pkgUid)
        {
            if (conn == null) return false;
            try
            {
                using (var st = conn.Prepare("SELECT 1 FROM gallery_item_user_tag WHERE pkg_uid=? LIMIT 1"))
                {
                    st.BindText(1, pkgUid ?? "");
                    return st.Step() == VpbSqlite3.SqliteRow;
                }
            }
            catch
            {
                return false;
            }
        }


        /// <summary>Aggregates tag→how many selected rows have it, single DB connection (vs N opens per row).</summary>
        internal static bool TryAccumulateGalleryUserTagSelectionCounts(
            string categoryTitle,
            List<KeyValuePair<string, string>> uniquePkgInternalPaths,
            Dictionary<string, int> countsOut)
        {
            countsOut?.Clear();
            if (!VpbSqlite3.IsAvailable || countsOut == null || string.IsNullOrEmpty(categoryTitle)) return false;
            if (uniquePkgInternalPaths == null || uniquePkgInternalPaths.Count == 0) return true;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    var rowTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < uniquePkgInternalPaths.Count; i++)
                    {
                        KeyValuePair<string, string> kv = uniquePkgInternalPaths[i];
                        rowTags.Clear();
                        if (!TryGetGalleryUserTagsForRow(conn, categoryTitle, kv.Key, kv.Value, rowTags)) continue;
                        foreach (string t in rowTags)
                        {
                            if (string.IsNullOrEmpty(t)) continue;
                            if (countsOut.TryGetValue(t, out int c)) countsOut[t] = c + 1;
                            else countsOut[t] = 1;
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

        /// <summary>Ensure a row exists in <c>gallery_user_tag</c> (tag vocabulary only).</summary>
        internal static bool TryEnsureGalleryUserTagInVocabulary(string rawName, out string normalizedOut)
        {
            normalizedOut = NormalizeGalleryUserTagName(rawName);
            if (string.IsNullOrEmpty(normalizedOut)) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    long id = TryGetOrCreateGalleryUserTagId(conn, normalizedOut);
                    return id >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Remove all gallery_item_user_tag rows for tag and delete its gallery_user_tag row.</summary>
        internal static bool TryPurgeGalleryUserTagGlobally(string normalizedName, out int itemLinksRemoved)
        {
            itemLinksRemoved = 0;
            string n = NormalizeGalleryUserTagName(normalizedName);
            if (string.IsNullOrEmpty(n)) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN;");
                    try
                    {
                        long tid = -1;
                        using (var sel = conn.Prepare("SELECT tag_id FROM gallery_user_tag WHERE name=?"))
                        {
                            sel.BindText(1, n);
                            if (sel.Step() == VpbSqlite3.SqliteRow)
                                tid = sel.ColumnInt64(0);
                        }
                        if (tid < 0)
                        {
                            conn.ExecUtf8("COMMIT;");
                            return true;
                        }
                        using (var cnt = conn.Prepare("SELECT COUNT(*) FROM gallery_item_user_tag WHERE tag_id=?"))
                        {
                            cnt.BindInt64(1, tid);
                            if (cnt.Step() == VpbSqlite3.SqliteRow)
                                itemLinksRemoved = (int)cnt.ColumnInt64(0);
                        }
                        using (var delIt = conn.Prepare("DELETE FROM gallery_item_user_tag WHERE tag_id=?"))
                        {
                            delIt.BindInt64(1, tid);
                            delIt.Step();
                        }
                        using (var delTag = conn.Prepare("DELETE FROM gallery_user_tag WHERE tag_id=?"))
                        {
                            delTag.BindInt64(1, tid);
                            delTag.Step();
                        }
                        conn.ExecUtf8("COMMIT;");
                        return true;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch
            {
                itemLinksRemoved = 0;
                return false;
            }
        }

        /// <summary>Move all item assignments from source tags into <paramref name="rawTargetName"/>; delete emptied source tag rows. Target row is created if missing.</summary>
        internal static bool TryMergeGalleryUserTagsInto(IEnumerable<string> sourceDisplayNames, string rawTargetName, out string normalizedTargetOut, out int itemAssignmentsUpdated)
        {
            normalizedTargetOut = "";
            itemAssignmentsUpdated = 0;
            if (!VpbSqlite3.IsAvailable || sourceDisplayNames == null) return false;
            normalizedTargetOut = NormalizeGalleryUserTagName(rawTargetName);
            if (string.IsNullOrEmpty(normalizedTargetOut)) return false;

            var sourceTids = new HashSet<long>();
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    foreach (var raw in sourceDisplayNames)
                    {
                        string n = NormalizeGalleryUserTagName(raw);
                        if (string.IsNullOrEmpty(n)) continue;
                        using (var sel = conn.Prepare("SELECT tag_id FROM gallery_user_tag WHERE name=?"))
                        {
                            sel.BindText(1, n);
                            if (sel.Step() == VpbSqlite3.SqliteRow)
                                sourceTids.Add(sel.ColumnInt64(0));
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            if (sourceTids.Count == 0) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN;");
                    try
                    {
                        long targetTid = TryGetOrCreateGalleryUserTagId(conn, normalizedTargetOut);
                        if (targetTid < 0)
                        {
                            conn.ExecUtf8("ROLLBACK;");
                            return false;
                        }

                        foreach (long sourceTid in sourceTids)
                        {
                            if (sourceTid == targetTid) continue;

                            using (var cnt = conn.Prepare("SELECT COUNT(*) FROM gallery_item_user_tag WHERE tag_id=?"))
                            {
                                cnt.BindInt64(1, sourceTid);
                                if (cnt.Step() == VpbSqlite3.SqliteRow)
                                    itemAssignmentsUpdated += (int)cnt.ColumnInt64(0);
                            }

                            using (var ins = conn.Prepare(
                                "INSERT OR IGNORE INTO gallery_item_user_tag(category, pkg_uid, internal_path, tag_id) " +
                                "SELECT category, pkg_uid, internal_path, ? FROM gallery_item_user_tag WHERE tag_id=?"))
                            {
                                ins.BindInt64(1, targetTid);
                                ins.BindInt64(2, sourceTid);
                                ins.Step();
                            }

                            using (var delIt = conn.Prepare("DELETE FROM gallery_item_user_tag WHERE tag_id=?"))
                            {
                                delIt.BindInt64(1, sourceTid);
                                delIt.Step();
                            }

                            using (var delTag = conn.Prepare("DELETE FROM gallery_user_tag WHERE tag_id=?"))
                            {
                                delTag.BindInt64(1, sourceTid);
                                delTag.Step();
                            }
                        }

                        conn.ExecUtf8("COMMIT;");
                        return true;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch
            {
                itemAssignmentsUpdated = 0;
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

        internal static bool TryReadGalleryHistoryModeCounts(Dictionary<GalleryHistoryFilterMode, int> outCounts)
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
                    foreach (GalleryHistoryFilterMode mode in Enum.GetValues(typeof(GalleryHistoryFilterMode)))
                    {
                        string kindSql = BuildGalleryHistoryKindSqlAnd(mode);
                        var sb = new StringBuilder(768);
                        sb.Append("SELECT COUNT(DISTINCT i.item_key) ");
                        AppendGalleryHistoryJoinFromWhere(sb);
                        sb.Append(kindSql);
                        sb.Append(" AND length(trim(ifnull(p.uid,''))) > 0");
                        sb.Append(" AND length(trim(").Append(internalPathExpr).Append(")) > 0");

                        using (var st = conn.Prepare(sb.ToString()))
                        {
                            int n = 0;
                            if (st.Step() == VpbSqlite3.SqliteRow)
                                n = (int)Math.Min(Math.Max(st.ColumnInt64(0), 0), int.MaxValue);
                            outCounts[mode] = n;
                        }
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
                        // UTF-16 code units; stable within this codebase.
                        h ^= (ushort)s[i];
                        h *= Prime;
                    }
                }
                return h.ToString("x16");
            }
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
            if (clothingSubfilter == 0) return true;

            int kind = packed & 0xF;
            int gender = (packed >> 4) & 0xF;
            bool isPreset = (packed & 0x100) != 0;
            bool isDecal = (packed & 0x200) != 0;

            if (kind != (int)ClothingLoadingUtils.ResourceKind.Clothing) return false;

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
            if ((clothingSubfilter & Itm) != 0) { if (isPreset) return false; }
            if ((clothingSubfilter & Mal) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Male) return false; }
            if ((clothingSubfilter & Fem) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Female) return false; }

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
            if (f == 0) return "";
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

            // VAR rows never satisfy Custom alone (or with Presets, etc.); SQL path is VAR-only here.
            if ((f & Cus) != 0) return " AND (1=0)";
            if (((f & Pre) != 0) && ((f & Itm) != 0)) return " AND (1=0)";
            if (((f & Mal) != 0) && ((f & Fem) != 0)) return " AND (1=0)";

            const string c = "CAST(ifnull(m.cloth_attr,'0') AS INTEGER)";
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
            if ((f & Itm) != 0)
                sb.Append(" AND (").Append(c).Append(" & 256) = 0");
            if ((f & Mal) != 0)
                sb.Append(" AND (((").Append(c).Append(") & 240) / 16) = 2");
            if ((f & Fem) != 0)
                sb.Append(" AND (((").Append(c).Append(") & 240) / 16) = 1");

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

                        t0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8("DELETE FROM cat_mem; DELETE FROM pkg_dep; DELETE FROM pkg;");
                        tDelete += Stopwatch.GetTimestamp() - t0;
                        using (var insPkg = conn.Prepare("INSERT OR REPLACE INTO pkg(uid,creator,wtime,psize,var_path,pctime,loaded) VALUES(?,?,?,?,?,?,?)"))
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
                                int loaded = ComputePackageLoadedFlagFromVarPath(varPath);

                                long tPkg0 = Stopwatch.GetTimestamp();
                                insPkg.BindText(1, uid);
                                insPkg.BindText(2, cr);
                                insPkg.BindInt64(3, wt);
                                insPkg.BindInt64(4, sz);
                                insPkg.BindText(5, varPath);
                                insPkg.BindInt64(6, ct);
                                insPkg.BindInt64(7, loaded);
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
                                    if (string.IsNullOrEmpty(cname)) continue;

                                    string listPath;
                                    if (string.Equals(ip, "meta.json", StringComparison.OrdinalIgnoreCase))
                                        listPath = varPath;
                                    else
                                        listPath = varListPrefix + ip;

                                    long clothAttr = 0;
                                    if (string.Equals(cname, "Clothing", StringComparison.OrdinalIgnoreCase))
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
                    
                    bool hasCreator = !string.IsNullOrEmpty(creatorFilter);
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
                    if (hasCreator) sb.Append(" AND p.creator = ?");
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
                    
                    sb.Append(" GROUP BY m.category");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (hasCreator) stmt.BindText(bind++, creatorFilter);
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
                            if (hasCreator) sbPkg.Append(" AND p.creator = ?");
                            if (applyPath)
                                sbPkg.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");

                            using (var stPkg = conn.Prepare(sbPkg.ToString()))
                            {
                                int b2 = 1;
                                if (hasCreator) stPkg.BindText(b2++, creatorFilter);
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
                    var sb = new StringBuilder();
                    string countExpr = hasCat ? "COUNT(*)" : "COUNT(DISTINCT m.pkg_uid || char(0) || m.internal_path)";
                    sb.Append("SELECT p.creator, ").Append(countExpr).Append(" ");
                    sb.Append("FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid ");
                    sb.Append("WHERE length(trim(coalesce(p.creator,''))) > 0");
                    if (hasCat) sb.Append(" AND m.category = ?");
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
                    var utBind = new List<string>();
                    AppendSqlActiveUserTagExists(sb, utBind, activeUserTags, "m");
                    
                    sb.Append(" GROUP BY p.creator");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (hasCat) stmt.BindText(bind++, categoryTitle);
                        if (hasPackagePathFilter)
                            stmt.BindText(bind++, EscapeLike(normalizedPackagePathFilter.ToLowerInvariant()) + "/%");
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
                    bool hasCreator = !string.IsNullOrEmpty(creatorFilter);
                    bool hasPathPrefix = (pathPrefixes != null && pathPrefixes.Count > 0) || !string.IsNullOrEmpty(singlePathPrefix);

                    var sb = new StringBuilder();
                    sb.Append("SELECT ifnull(p.var_path,''), COUNT(*) ");
                    sb.Append("FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid ");
                    sb.Append("WHERE m.category = ?");
                    if (hasCreator) sb.Append(" AND p.creator = ?");

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
                        if (hasCreator) stmt.BindText(bind++, creatorFilter);

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
            GalleryPanel.AppearanceSubfilter appearanceSubfilter = 0,
            HashSet<string> activeTags = null)
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

                    string sql =
                        "SELECT m.internal_path, m.pkg_uid, ifnull(m.cloth_attr,'0'), m.list_path FROM cat_mem m " +
                        "INNER JOIN pkg p ON p.uid = m.pkg_uid " +
                        "WHERE m.category = ? AND ((length(trim(?)) = 0) OR (p.creator = ?))" + clothSqlAnd + tagSqlAnd;

                    using (var stmt = conn.Prepare(sql))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        string cf = creatorFilter ?? "";
                        stmt.BindText(bind++, cf);
                        stmt.BindText(bind++, cf);

                        if (activeTagsList != null)
                        {
                            foreach (var tag in activeTagsList)
                            {
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                            }
                        }

                        bool isClothing = string.Equals(categoryTitle, "Clothing", StringComparison.OrdinalIgnoreCase);
                        bool isAppearance = string.Equals(categoryTitle, "Appearance", StringComparison.OrdinalIgnoreCase);

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
                            else if (isAppearance)
                            {
                                // Appearance subfilters are not yet packed into clothAttr in the current schema.
                                // Fall back to path heuristics for now to avoid warnings and provide correct counts.
                                string p = internalPath.Replace('\\', '/');
                                if (p.IndexOf("/appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    bool isCustomAppearance = p.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                                    bool isPresetAppearance = p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
                                    
                                    // Heuristic gender (simplified version of the one in GalleryTagCountBackgroundScan)
                                    int g = 0; // Unknown
                                    string combined = (p + " " + (pkgUid ?? "")).ToLowerInvariant();
                                    if (combined.Contains("female") || combined.Contains("woman") || combined.Contains("girl")) g = 1;
                                    else if (combined.Contains("male") || combined.Contains("man") || combined.Contains("boy")) g = 2;
                                    else if (combined.Contains("futa") || combined.Contains("herm")) g = 3;

                                    outFacets.AppearanceSubfilterCountAll++;
                                    if (isPresetAppearance) outFacets.AppearanceSubfilterCountPresets++;
                                    if (isCustomAppearance) outFacets.AppearanceSubfilterCountCustom++;
                                    if (g == 2) outFacets.AppearanceSubfilterCountMale++;
                                    if (g == 1) outFacets.AppearanceSubfilterCountFemale++;
                                    if (g == 3) outFacets.AppearanceSubfilterCountFuta++;

                                    // Simplified PassesAppearanceSubfilters check
                                    bool PassesApp(GalleryPanel.AppearanceSubfilter f, bool isPre, bool isCus, int gen)
                                    {
                                        if (f == 0) return true;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Presets) != 0 && !isPre) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Custom) != 0 && !isCus) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Male) != 0 && gen != 2) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Female) != 0 && gen != 1) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Futa) != 0 && gen != 3) return false;
                                        return true;
                                    }

                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Presets, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountPresets++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Custom, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountCustom++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Male, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountMale++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Female, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountFemale++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Futa, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountFuta++;

                                    if (PassesApp(appearanceSubfilter, isPresetAppearance, isCustomAppearance, g))
                                    {
                                        outFacets.AppearanceSubfilterCurrentCountAll++;
                                        if (g == 2) outFacets.AppearanceSubfilterCurrentCountMale++;
                                        if (g == 1) outFacets.AppearanceSubfilterCurrentCountFemale++;
                                        if (g == 3) outFacets.AppearanceSubfilterCurrentCountFuta++;
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
            public int AppearanceSubfilterCountAll;
            public int AppearanceSubfilterCountPresets;
            public int AppearanceSubfilterCountCustom;
            public int AppearanceSubfilterCountMale;
            public int AppearanceSubfilterCountFemale;
            public int AppearanceSubfilterCountFuta;
            public int ClothingSubfilterFacetCountReal;
            public int ClothingSubfilterFacetCountPresets;
            public int ClothingSubfilterFacetCountCustom;
            public int ClothingSubfilterFacetCountItems;
            public int ClothingSubfilterFacetCountMale;
            public int ClothingSubfilterFacetCountFemale;
            public int ClothingSubfilterFacetCountDecals;
            public int AppearanceSubfilterFacetCountPresets;
            public int AppearanceSubfilterFacetCountCustom;
            public int AppearanceSubfilterFacetCountMale;
            public int AppearanceSubfilterFacetCountFemale;
            public int AppearanceSubfilterFacetCountFuta;
            public int AppearanceSubfilterCurrentCountAll;
            public int AppearanceSubfilterCurrentCountMale;
            public int AppearanceSubfilterCurrentCountFemale;
            public int AppearanceSubfilterCurrentCountFuta;
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
                            case SortType.DateCreated: orderBy = " ORDER BY p.pctime" + dir + ", m.list_path ASC"; break;
                        }
                    }

                    string sql =
                        "SELECT m.pkg_uid, m.internal_path, m.list_path, p.var_path, p.wtime, p.psize, p.pctime, ifnull(m.cloth_attr,''), " + loadedSelect + " FROM cat_mem m " +
                        "INNER JOIN pkg p ON p.uid = m.pkg_uid " +
                        "WHERE m.category = ? AND ((length(trim(?)) = 0) OR (p.creator = ?))" + clothSqlAnd + loadedSqlAnd + nameSqlAnd + exclusionSqlAnd + tagSqlAnd + userTagSqlAnd + orderBy;
                    
                    using (var stmt = conn.Prepare(sql))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        string cf = creatorFilter ?? "";
                        stmt.BindText(bind++, cf);
                        stmt.BindText(bind++, cf);

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
                        "p.wtime, p.psize, p.pctime, " +
                        "ifnull(COALESCE(mx.cloth_attr, mr.cloth_attr),''), " +
                        loadedSelect +
                        ", i.use_count, i.last_used ");
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

                    bool hasCreator = !string.IsNullOrEmpty(creatorFilter);
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
                            case SortType.DateCreated: orderBy = " ORDER BY p.pctime" + dir + ", p.uid ASC"; break;
                        }
                    }

                    var sbSql = new StringBuilder(512);
                    sbSql.Append("SELECT p.uid, ifnull(p.var_path,''), p.wtime, p.psize, p.pctime, ").Append(loadedSelect).Append(" FROM pkg p WHERE 1=1");
                    if (hasCreator) sbSql.Append(" AND p.creator = ?");
                    if (hasPath) sbSql.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    sbSql.Append(loadedSqlAnd).Append(nameSqlAnd).Append(orderBy);

                    using (var st = conn.Prepare(sbSql.ToString()))
                    {
                        int bind = 1;
                        if (hasCreator) st.BindText(bind++, creatorFilter);
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
                bool hasCreator = !string.IsNullOrEmpty(creatorFilter);
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
                    if (hasCreator) sb.Append(" AND p.creator = ?");
                    if (applyPath)
                        sb.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        int b = 1;
                        if (hasCreator) st.BindText(b++, creatorFilter);
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
            sb.Append("SELECT uid, ifnull(var_path,''), wtime, psize, pctime, ifnull(loaded,'') FROM pkg WHERE uid IN (");
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
                    r.PackageIsLoaded = false;

                    string wtxt = st.ColumnText(2);
                    string sztxt = st.ColumnText(3);
                    string ctxt = st.ColumnText(4);
                    string loadedTxt = st.ColumnText(5) ?? "";

                    long wtL, szL, ctL;
                    if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wtL))
                        r.LastWriteTicksOrInvalid = wtL;
                    if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out szL))
                        r.PackageSizeOrInvalid = szL;
                    if (!string.IsNullOrEmpty(ctxt) && long.TryParse(ctxt, out ctL))
                        r.PackageCreationTicksOrInvalid = ctL;
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
            sb.Append("SELECT uid, ifnull(var_path,''), wtime, psize, pctime, ifnull(loaded,'') FROM pkg WHERE uid IN (");
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
                    r.PackageIsLoaded = false;

                    string wtxt = st.ColumnText(2);
                    string sztxt = st.ColumnText(3);
                    string ctxt = st.ColumnText(4);
                    string loadedTxt = st.ColumnText(5) ?? "";

                    long wtL, szL, ctL;
                    if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wtL))
                        r.LastWriteTicksOrInvalid = wtL;
                    if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out szL))
                        r.PackageSizeOrInvalid = szL;
                    if (!string.IsNullOrEmpty(ctxt) && long.TryParse(ctxt, out ctL))
                        r.PackageCreationTicksOrInvalid = ctL;
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
