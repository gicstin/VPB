using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        internal struct DataPackStatus
        {
            public bool Installed;
            public string PackVersion;
            public string BuiltDate;
            public string ContentHash;
            public string Attribution;
            public int EntryCount;
            public int LinkedEntries;
            public int LinkedPackages;
        }

        /// <summary>Look-A-Pedia row linked to a library package (subject = "this looks like").</summary>
        internal struct DataPackLookOverlay
        {
            public bool Found;
            public string Subject;
            public string Category;
            public string HubTagsFmt;
        }

        internal const int DataPackMatchKindIdent = 2;
        const int DataPackOverlayHubTagCap = 8;
        internal const int DataPackMenuHubTagCap = 64;

        static volatile int s_DataPackIndexReady;
        static Dictionary<string, DataPackLookOverlay> s_OverlayByUid;
        static int s_OverlayMapRev = -1;
        const int OverlayCacheMax = 48;

        internal static bool DataPackIndexReady { get { return s_DataPackIndexReady != 0; } }

        internal static bool DataPackPacksConfigured()
        {
            try
            {
                var cfg = VPBConfig.Instance;
                return cfg != null && (cfg.DataPackLookapediaEnabled || cfg.DataPackHubTagsEnabled);
            }
            catch { return false; }
        }

        internal static bool DataPackLookSearchEnabled()
        {
            try
            {
                var cfg = VPBConfig.Instance;
                if (cfg == null) return false;
                if (!cfg.DataPackLookapediaEnabled && !cfg.DataPackHubTagsEnabled) return false;
            }
            catch { return false; }
            return s_DataPackIndexReady != 0;
        }

        static string[] s_SubjectPackIds;
        static int s_SubjectPackRev = -1;

        /// <summary>
        /// Packs that actually carry a subject. Only Look-A-Pedia does; the Hub pack has none,
        /// so scoping subject probes by pack id keeps them off its links entirely.
        /// Null means "unknown" — callers must then fall back to probing every pack.
        /// </summary>
        internal static string[] DataPackSubjectPackIds()
        {
            int rev = VpbDataPackService.StatusRevision;
            if (s_SubjectPackRev == rev) return s_SubjectPackIds;

            string[] ids = null;
            try
            {
                if (VpbSqlite3.IsAvailable)
                {
                    using (var conn = new VpbSqlite3.Connection(DbPath))
                    {
                        if (DataPackTablesPresent(conn))
                        {
                            var list = new List<string>(2);
                            using (var st = conn.Prepare(
                                "SELECT DISTINCT pack_id FROM datapack_entry WHERE ifnull(subject,'')<>''"))
                            {
                                while (st.Step() == VpbSqlite3.SqliteRow)
                                {
                                    string p = st.ColumnText(0);
                                    if (!string.IsNullOrEmpty(p)) list.Add(p);
                                }
                            }
                            ids = list.ToArray();
                        }
                    }
                }
            }
            catch { ids = null; }

            s_SubjectPackIds = ids;
            s_SubjectPackRev = rev;
            return ids;
        }

        /// <summary>False when no installed pack carries a subject — the probe can be skipped outright.</summary>
        internal static bool DataPackSubjectSearchEnabled()
        {
            if (!DataPackLookSearchEnabled()) return false;
            string[] ids = DataPackSubjectPackIds();
            return ids == null || ids.Length > 0;
        }

        internal static void AppendDataPackSubjectPackScopeSql(StringBuilder sb)
        {
            if (sb == null) return;
            string[] ids = DataPackSubjectPackIds();
            if (ids == null || ids.Length == 0) return;
            if (ids.Length == 1)
            {
                sb.Append(" AND dl.pack_id=?");
                return;
            }
            sb.Append(" AND dl.pack_id IN (");
            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('?');
            }
            sb.Append(')');
        }

        internal static void AddDataPackSubjectPackScopeBinds(List<string> binds)
        {
            if (binds == null) return;
            string[] ids = DataPackSubjectPackIds();
            if (ids == null) return;
            for (int i = 0; i < ids.Length; i++) binds.Add(ids[i]);
        }

        const int DataPackSubjectUidInlineMax = 400;
        const int DataPackSubjectUidCacheMax = 64;
        static Dictionary<string, List<string>> s_SubjectUidsByTerm;
        static int s_SubjectUidsRev = -1;

        static List<string> DataPackSubjectUidsForTerm(string body, bool exact)
        {
            if (string.IsNullOrEmpty(body)) return null;
            string[] packs = DataPackSubjectPackIds();
            if (packs != null && packs.Length == 0) return EmptyStringList;

            int rev = VpbDataPackService.StatusRevision;
            if (s_SubjectUidsRev != rev)
            {
                if (s_SubjectUidsByTerm != null) s_SubjectUidsByTerm.Clear();
                s_SubjectUidsRev = rev;
            }
            if (s_SubjectUidsByTerm == null)
                s_SubjectUidsByTerm = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            string key = (exact ? "=" : "~") + body;
            List<string> hit;
            if (s_SubjectUidsByTerm.TryGetValue(key, out hit)) return hit;
            if (s_SubjectUidsByTerm.Count >= DataPackSubjectUidCacheMax) s_SubjectUidsByTerm.Clear();

            List<string> uids = null;
            try
            {
                if (VpbSqlite3.IsAvailable)
                {
                    using (var conn = new VpbSqlite3.Connection(DbPath))
                    {
                        if (DataPackTablesPresent(conn))
                        {
                            var sb = new StringBuilder(256);
                            sb.Append("SELECT DISTINCT dl.pkg_uid FROM datapack_link dl ");
                            sb.Append("CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id ");
                            sb.Append("AND de.entry_id=dl.entry_id WHERE ");
                            sb.Append(exact
                                ? "lower(trim(ifnull(de.subject,''))) = ?"
                                : "lower(ifnull(de.subject,'')) LIKE ? ESCAPE '\\'");
                            AppendDataPackSubjectPackScopeSql(sb);

                            var list = new List<string>(32);
                            using (var st = conn.Prepare(sb.ToString()))
                            {
                                st.BindText(1, exact ? body : ("%" + EscapeLike(body) + "%"));
                                string[] ids = DataPackSubjectPackIds();
                                if (ids != null)
                                {
                                    for (int i = 0; i < ids.Length; i++) st.BindText(i + 2, ids[i]);
                                }
                                while (st.Step() == VpbSqlite3.SqliteRow)
                                {
                                    string u = st.ColumnText(0);
                                    if (string.IsNullOrEmpty(u)) continue;
                                    if (list.Count >= DataPackSubjectUidInlineMax) { list = null; break; }
                                    list.Add(u);
                                }
                            }
                            uids = list;
                        }
                    }
                }
            }
            catch { uids = null; }

            s_SubjectUidsByTerm[key] = uids;
            return uids;
        }

        static readonly List<string> EmptyStringList = new List<string>(0);

        static void AppendDataPackUidLiteralList(StringBuilder sb, string uidSql, List<string> uids, bool negate)
        {
            sb.Append(uidSql).Append(negate ? " NOT IN (" : " IN (");
            for (int i = 0; i < uids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\'').Append(uids[i].Replace("'", "''")).Append('\'');
            }
            sb.Append(')');
        }

        internal static bool TryAppendDataPackSubjectUidSet(
            StringBuilder sb, string uidSql, string body, bool exact, bool negate,
            string connector, bool dropWhenEmpty)
        {
            if (sb == null || string.IsNullOrEmpty(uidSql)) return false;
            List<string> uids = DataPackSubjectUidsForTerm(body, exact);
            if (uids == null) return false;

            string lead = connector ?? "";
            if (uids.Count == 0)
            {
                if (dropWhenEmpty) return true;
                sb.Append(lead).Append(negate ? "1" : "0");
                return true;
            }

            sb.Append(lead);
            AppendDataPackUidLiteralList(sb, uidSql, uids, negate);
            return true;
        }

        internal struct DataPackPackageMetrics
        {
            public int Downloads;
            public int RatingX100;
            public int ReleasedYmd;
            public int UpdatedYmd;
        }

        static Dictionary<string, DataPackPackageMetrics> s_PackMetrics;
        static int s_PackMetricsRev = -1;

        static int ParseYmd(string iso)
        {
            if (string.IsNullOrEmpty(iso) || iso.Length < 10) return 0;
            int y = 0, m = 0, d = 0;
            for (int i = 0; i < 4; i++)
            {
                char c = iso[i];
                if (c < '0' || c > '9') return 0;
                y = y * 10 + (c - '0');
            }
            for (int i = 5; i < 7; i++)
            {
                char c = iso[i];
                if (c < '0' || c > '9') return 0;
                m = m * 10 + (c - '0');
            }
            for (int i = 8; i < 10; i++)
            {
                char c = iso[i];
                if (c < '0' || c > '9') return 0;
                d = d * 10 + (c - '0');
            }
            if (y < 1000 || m < 1 || m > 12 || d < 1 || d > 31) return 0;
            return y * 10000 + m * 100 + d;
        }

        internal static Dictionary<string, DataPackPackageMetrics> GetDataPackPackageMetrics()
        {
            if (!DataPackLookSearchEnabled()) return null;
            int rev = VpbDataPackService.StatusRevision;
            if (s_PackMetricsRev == rev && s_PackMetrics != null) return s_PackMetrics;
            if (!VpbSqlite3.IsAvailable) return null;

            var map = new Dictionary<string, DataPackPackageMetrics>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return null;
                    using (var st = conn.Prepare(
                        "SELECT dl.pkg_uid, MAX(de.downloads), " +
                        "MAX(CAST(ROUND(CAST(ifnull(de.rating_avg,'0') AS REAL) * 100) AS INTEGER)), " +
                        "MAX(ifnull(de.first_release,'')), MAX(ifnull(de.last_update,'')) " +
                        "FROM datapack_link dl " +
                        "CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id " +
                        "GROUP BY dl.pkg_uid"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string uid = st.ColumnText(0);
                            if (string.IsNullOrEmpty(uid)) continue;
                            var m = new DataPackPackageMetrics();
                            long dl = st.ColumnInt64(1);
                            m.Downloads = dl > int.MaxValue ? int.MaxValue : (dl < 0 ? 0 : (int)dl);
                            m.RatingX100 = (int)st.ColumnInt64(2);
                            m.ReleasedYmd = ParseYmd(st.ColumnText(3));
                            m.UpdatedYmd = ParseYmd(st.ColumnText(4));
                            map[uid] = m;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] hub metrics read failed: " + ex.Message); } catch { }
                return null;
            }

            s_PackMetrics = map;
            s_PackMetricsRev = rev;
            return map;
        }

        internal static void InvalidateDataPackLookOverlayCache()
        {
            s_SubjectUidsRev = -1;
            s_SubjectPackRev = -1;
            s_OverlayMapRev = -1;
            s_PackMetricsRev = -1;
        }

        internal static void SetDataPackIndexReady(bool ready)
        {
            s_DataPackIndexReady = ready ? 1 : 0;
            InvalidateDataPackLookOverlayCache();
        }

        /// <summary>Ready means at least one pack is installed, whichever it is.</summary>
        internal static void RefreshDataPackIndexReady()
        {
            if (!VpbSqlite3.IsAvailable)
            {
                SetDataPackIndexReady(false);
                return;
            }
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn))
                    {
                        SetDataPackIndexReady(false);
                        return;
                    }
                    SetDataPackIndexReady(ScalarInt64(conn, "SELECT COUNT(*) FROM datapack;") > 0);
                }
            }
            catch
            {
                SetDataPackIndexReady(false);
            }
        }

        internal static string DataPackSubjectSearchToken(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return "";
            string s = subject.Trim();
            int br = s.IndexOf('[');
            if (br > 0) s = s.Substring(0, br).Trim();
            if (s.Length == 0) return "";

            string[] parts = s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (string.IsNullOrEmpty(p)) continue;
                string lower = p.ToLowerInvariant();
                if (lower == "the" || lower == "a" || lower == "an") continue;
                return lower;
            }
            return s.ToLowerInvariant();
        }

        /// <summary>Full subject/tag string for facet chips (quoted exact match). Not first-word LIKE.</summary>
        internal static string DataPackFacetValueToken(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Trim().ToLowerInvariant();
        }

        internal static string DataPackHubTagSearchToken(string tag)
        {
            return DataPackFacetValueToken(tag);
        }

        internal static bool TryCollectLookFacetRows(
            bool hubTags,
            string extensionPipeSeparated,
            List<string> pathPrefixes,
            string singlePathPrefix,
            HashSet<string> activeTags,
            string categoryTitle,
            string packagePathFilter,
            HashSet<string> activeUserTags,
            List<CreatorCacheEntry> dest)
        {
            if (dest == null) return false;
            if (!DataPackLookSearchEnabled()) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            bool ok = TryReadGroupedFileCounts(
                counts,
                hubTags ? FileCountGroupMode.DataPackHubTag : FileCountGroupMode.DataPackSubject,
                extensionPipeSeparated, pathPrefixes, singlePathPrefix,
                activeTags, categoryTitle, packagePathFilter, activeUserTags);

            if (!ok)
            {
                // Package-level fallback: the item-level query needs a ready category index, and an
                // "ALL VAR" style category has no cat_mem rows at all.
                return FillLookFacetPackageCounts(hubTags, dest);
            }

            dest.Clear();
            foreach (var kv in counts)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                dest.Add(new CreatorCacheEntry { Name = kv.Key, Count = kv.Value });
            }
            return true;
        }

        static bool FillLookFacetPackageCounts(bool hubTags, List<CreatorCacheEntry> dest)
        {
            var rows = new List<CreatorCacheEntry>(512);
            if (!FillLookFacetCache(hubTags, rows)) return false;
            dest.Clear();
            for (int i = 0; i < rows.Count; i++) dest.Add(rows[i]);
            return true;
        }

        static bool FillLookFacetCache(bool hubTags, List<CreatorCacheEntry> dest)
        {
            if (dest == null) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return true;
                    string sql;
                    if (hubTags)
                    {
                        var hsb = new StringBuilder(320);
                        hsb.Append("SELECT dt.tag, COUNT(DISTINCT dl.pkg_uid) FROM datapack_link dl ");
                        hsb.Append("CROSS JOIN datapack_tag dt ON dt.pack_id=dl.pack_id AND dt.entry_id=dl.entry_id AND dt.ns='hub' ");
                        hsb.Append("WHERE length(trim(ifnull(dt.tag,'')))>0");
                        AppendDataPackTagNotHiddenSql(hsb, "dt.tag", "dl.pkg_uid");
                        hsb.Append(" GROUP BY dt.tag");
                        sql = hsb.ToString();
                    }
                    else
                    {
                        sql = "SELECT de.subject, COUNT(DISTINCT dl.pkg_uid) FROM datapack_link dl " +
                              "CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id " +
                              "WHERE length(trim(ifnull(de.subject,'')))>0 GROUP BY de.subject";
                    }
                    using (var st = conn.Prepare(sql))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string name = st.ColumnText(0);
                            if (string.IsNullOrEmpty(name)) continue;
                            dest.Add(new CreatorCacheEntry
                            {
                                Name = name,
                                Count = (int)st.ColumnInt64(1)
                            });
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] look facet collect failed: " + ex.Message); } catch { }
                dest.Clear();
                return false;
            }
        }

        internal static void EnsureDataPackSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS datapack (" +
                "pack_id TEXT PRIMARY KEY," +
                "pack_version TEXT NOT NULL DEFAULT ''," +
                "built_date TEXT NOT NULL DEFAULT ''," +
                "content_hash TEXT NOT NULL DEFAULT ''," +
                "source_url TEXT NOT NULL DEFAULT ''," +
                "attribution TEXT NOT NULL DEFAULT ''," +
                "entry_count INTEGER NOT NULL DEFAULT 0," +
                "applied_utc INTEGER NOT NULL DEFAULT 0," +
                "sync_watermark TEXT," +
                "sync_utc INTEGER NOT NULL DEFAULT 0);" +
                "CREATE TABLE IF NOT EXISTS datapack_entry (" +
                "pack_id TEXT NOT NULL, entry_id INTEGER NOT NULL, src_id TEXT, subject TEXT, title TEXT," +
                "creator TEXT, category TEXT, pay_type TEXT, resource_id TEXT, link TEXT," +
                "first_release TEXT, last_update TEXT, tag_line TEXT, version TEXT, license TEXT," +
                "min_vam TEXT, rating_avg TEXT, downloads INTEGER NOT NULL DEFAULT 0," +
                "rating_count INTEGER NOT NULL DEFAULT 0, dep_count INTEGER NOT NULL DEFAULT 0," +
                "size_kb INTEGER NOT NULL DEFAULT 0, flags INTEGER NOT NULL DEFAULT 0," +
                "PRIMARY KEY(pack_id, entry_id));" +
                "CREATE TABLE IF NOT EXISTS datapack_tag (" +
                "pack_id TEXT NOT NULL, entry_id INTEGER NOT NULL, ns TEXT NOT NULL, tag TEXT NOT NULL," +
                "PRIMARY KEY(pack_id, entry_id, ns, tag));" +
                "CREATE TABLE IF NOT EXISTS datapack_var (" +
                "pack_id TEXT NOT NULL, entry_id INTEGER NOT NULL, var_key TEXT NOT NULL," +
                "alias_key TEXT NOT NULL, var_version TEXT, exact INTEGER NOT NULL DEFAULT 0," +
                "PRIMARY KEY(pack_id, entry_id, var_key));" +
                "CREATE TABLE IF NOT EXISTS datapack_link (" +
                "pack_id TEXT NOT NULL, pkg_uid TEXT NOT NULL, entry_id INTEGER NOT NULL," +
                "match_kind INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(pack_id, pkg_uid, entry_id));" +
                "CREATE TABLE IF NOT EXISTS datapack_ident (" +
                "pack_id TEXT NOT NULL, entry_id INTEGER NOT NULL, ident_key TEXT NOT NULL," +
                "PRIMARY KEY(pack_id, entry_id, ident_key));" +
                "CREATE INDEX IF NOT EXISTS idx_dp_ident_key ON datapack_ident(ident_key);" +
                "CREATE INDEX IF NOT EXISTS idx_dp_var_alias ON datapack_var(alias_key);" +
                "CREATE INDEX IF NOT EXISTS idx_dp_tag_tag ON datapack_tag(tag COLLATE NOCASE);" +
                "CREATE INDEX IF NOT EXISTS idx_dp_link_pkg ON datapack_link(pkg_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_dp_link_entry ON datapack_link(pack_id, entry_id);" +
                "CREATE INDEX IF NOT EXISTS idx_dp_entry_resource ON datapack_entry(resource_id);");

            EnsureDataPackTagPrefSchema(conn);

            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN tag_line TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN version TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN license TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN min_vam TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN rating_avg TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN downloads INTEGER NOT NULL DEFAULT 0;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN rating_count INTEGER NOT NULL DEFAULT 0;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN dep_count INTEGER NOT NULL DEFAULT 0;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN size_kb INTEGER NOT NULL DEFAULT 0;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack_entry ADD COLUMN flags INTEGER NOT NULL DEFAULT 0;");

            // Live-sync bookkeeping for the hublive pack (see VpbLocalDatabase.DataPackLive.cs).
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack ADD COLUMN sync_watermark TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE datapack ADD COLUMN sync_utc INTEGER NOT NULL DEFAULT 0;");

            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN uid_alias TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN ident_key TEXT;");
            try { conn.ExecUtf8("CREATE INDEX IF NOT EXISTS idx_pkg_uid_alias ON pkg(uid_alias);"); }
            catch { }
            try { conn.ExecUtf8("CREATE INDEX IF NOT EXISTS idx_pkg_ident_key ON pkg(ident_key);"); }
            catch { }
        }

        internal static string ComputePkgUidAlias(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";

            string s = uid;
            int dot = s.LastIndexOf('.');
            if (dot > 0 && dot < s.Length - 1)
            {
                bool allDigits = true;
                for (int i = dot + 1; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c < '0' || c > '9') { allDigits = false; break; }
                }
                if (allDigits)
                    s = s.Substring(0, dot);
                else if (string.Equals(s.Substring(dot + 1), "latest", StringComparison.OrdinalIgnoreCase))
                    s = s.Substring(0, dot);
            }

            if (s.IndexOf('.') < 0) return "";
            if (s.IndexOf(' ') >= 0) s = s.Replace(" ", "");
            return s.ToLowerInvariant();
        }

        internal static string ComputePkgIdentKey(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";

            string s = uid;
            int dot = s.LastIndexOf('.');
            if (dot > 0 && dot < s.Length - 1)
            {
                bool allDigits = true;
                for (int i = dot + 1; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c < '0' || c > '9') { allDigits = false; break; }
                }
                if (allDigits) s = s.Substring(0, dot);
            }

            int split = s.IndexOf('.');
            if (split <= 0 || split >= s.Length - 1) return "";
            string creator = NormalizeIdentPart(s.Substring(0, split));
            string name = NormalizeIdentPart(s.Substring(split + 1));
            if (creator.Length == 0 || name.Length < 2) return "";
            return creator + "|" + name;
        }

        static string NormalizeIdentPart(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new System.Text.StringBuilder(value.Length);
            string lower = value.ToLowerInvariant();
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            }
            return sb.ToString();
        }

        static bool DataPackTablesPresent(VpbSqlite3.Connection conn)
        {
            if (conn == null) return false;
            try
            {
                using (var st = conn.Prepare(
                    "SELECT 1 FROM sqlite_master WHERE type='table' AND name='datapack_var' LIMIT 1"))
                {
                    return st.Step() == VpbSqlite3.SqliteRow;
                }
            }
            catch
            {
                return false;
            }
        }

        static int RefreshPkgUidAliases(VpbSqlite3.Connection conn, bool onlyMissing)
        {
            if (conn == null) return 0;

            var uids = new List<string>(4096);
            string sel = onlyMissing
                ? "SELECT uid FROM pkg WHERE uid_alias IS NULL OR uid_alias='' OR ident_key IS NULL"
                : "SELECT uid FROM pkg";
            using (var st = conn.Prepare(sel))
            {
                while (st.Step() == VpbSqlite3.SqliteRow)
                {
                    string uid = st.ColumnText(0);
                    if (!string.IsNullOrEmpty(uid)) uids.Add(uid);
                }
            }
            if (uids.Count == 0) return 0;

            int written = 0;
            using (var up = conn.Prepare("UPDATE pkg SET uid_alias=?, ident_key=? WHERE uid=?"))
            {
                for (int i = 0; i < uids.Count; i++)
                {
                    up.BindText(1, ComputePkgUidAlias(uids[i]));
                    up.BindText(2, ComputePkgIdentKey(uids[i]));
                    up.BindText(3, uids[i]);
                    up.Step();
                    up.Reset();
                    written++;
                }
            }
            return written;
        }

        static int RebuildDataPackLinks(VpbSqlite3.Connection conn, string packId)
        {
            if (conn == null) return 0;

            bool scoped = !string.IsNullOrEmpty(packId);
            if (scoped)
            {
                using (var del = conn.Prepare("DELETE FROM datapack_link WHERE pack_id=?"))
                {
                    del.BindText(1, packId);
                    del.Step();
                }
            }
            else
            {
                conn.ExecUtf8("DELETE FROM datapack_link;");
            }

            const string insertVar =
                "INSERT OR IGNORE INTO datapack_link(pack_id,pkg_uid,entry_id,match_kind) " +
                "SELECT dv.pack_id, p.uid, dv.entry_id, dv.exact " +
                "FROM datapack_var dv JOIN pkg p ON p.uid_alias = dv.alias_key " +
                "WHERE dv.alias_key <> ''";
            string insertIdent =
                "INSERT OR IGNORE INTO datapack_link(pack_id,pkg_uid,entry_id,match_kind) " +
                "SELECT di.pack_id, p.uid, di.entry_id, " + DataPackMatchKindIdent + " " +
                "FROM datapack_ident di JOIN pkg p ON p.ident_key = di.ident_key " +
                "WHERE di.ident_key <> ''";

            if (scoped)
            {
                using (var ins = conn.Prepare(insertVar + " AND dv.pack_id=?"))
                {
                    ins.BindText(1, packId);
                    ins.Step();
                }
                using (var ins = conn.Prepare(insertIdent + " AND di.pack_id=?"))
                {
                    ins.BindText(1, packId);
                    ins.Step();
                }
            }
            else
            {
                conn.ExecUtf8(insertVar + ";");
                conn.ExecUtf8(insertIdent + ";");
            }

            BackfillDataPackResourceIds(conn);
            int inherited = InheritDataPackLinksByResourceId(conn);
            if (inherited > 0)
            {
                try { LogUtil.Log("[VPB.DB] data pack resource_id inherit: +" + inherited + " links"); }
                catch { }
            }

            return (int)ScalarInt64(conn, "SELECT COUNT(*) FROM datapack_link;");
        }

        static int BackfillDataPackResourceIds(VpbSqlite3.Connection conn)
        {
            if (conn == null) return 0;

            var packIds = new List<string>(4096);
            var entryIds = new List<long>(4096);
            var rids = new List<string>(4096);
            try
            {
                using (var st = conn.Prepare(
                    "SELECT pack_id, entry_id, ifnull(resource_id,''), ifnull(link,'') FROM datapack_entry " +
                    "WHERE length(trim(ifnull(resource_id,'')))=0"))
                {
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string packId = st.ColumnText(0) ?? "";
                        long entryId = st.ColumnInt64(1);
                        string have = st.ColumnText(2) ?? "";
                        string link = st.ColumnText(3) ?? "";
                        string want = VpbDataPackReader.NormalizeHubResourceId(have, link);
                        if (want.Length == 0
                            && string.Equals(packId, VpbDataPackService.HubTagsPackId, StringComparison.Ordinal))
                            want = entryId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if (want.Length == 0) continue;
                        if (string.Equals(want, have, StringComparison.Ordinal)) continue;
                        packIds.Add(packId);
                        entryIds.Add(entryId);
                        rids.Add(want);
                    }
                }
            }
            catch
            {
                return 0;
            }

            if (packIds.Count == 0) return 0;

            using (var up = conn.Prepare(
                "UPDATE datapack_entry SET resource_id=? WHERE pack_id=? AND entry_id=?"))
            {
                for (int i = 0; i < packIds.Count; i++)
                {
                    up.BindText(1, rids[i]);
                    up.BindText(2, packIds[i]);
                    up.BindInt64(3, entryIds[i]);
                    up.Step();
                    up.Reset();
                }
            }
            return packIds.Count;
        }

        static int InheritDataPackLinksByResourceId(VpbSqlite3.Connection conn)
        {
            if (conn == null) return 0;
            int before = (int)ScalarInt64(conn, "SELECT COUNT(*) FROM datapack_link;");
            conn.ExecUtf8(
                "INSERT OR IGNORE INTO datapack_link(pack_id,pkg_uid,entry_id,match_kind) " +
                "SELECT a.pack_id, l.pkg_uid, a.entry_id, l.match_kind " +
                "FROM datapack_entry a " +
                "JOIN datapack_entry b ON b.pack_id<>a.pack_id " +
                "AND b.resource_id=a.resource_id " +
                "AND length(a.resource_id)>0 AND a.resource_id NOT GLOB '*[^0-9]*' " +
                "AND length(b.resource_id)>0 AND b.resource_id NOT GLOB '*[^0-9]*' " +
                "JOIN datapack_link l ON l.pack_id=b.pack_id AND l.entry_id=b.entry_id;");
            int after = (int)ScalarInt64(conn, "SELECT COUNT(*) FROM datapack_link;");
            int delta = after - before;
            return delta > 0 ? delta : 0;
        }

        internal static void RefreshDataPackLinksAfterIndexChange(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            try
            {
                if (!DataPackTablesPresent(conn)) return;
                if (ScalarInt64(conn, "SELECT COUNT(*) FROM datapack;") <= 0) return;

                RefreshPkgUidAliases(conn, false);
                int links = RebuildDataPackLinks(conn, null);
                try { LogUtil.Log("[VPB.DB] data pack relink after index change: links=" + links); } catch { }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] data pack relink skipped: " + ex.Message); } catch { }
            }
        }

        internal static bool TryApplyDataPackFile(
            string packId, string path, out int entries, out int links, out string error)
        {
            entries = 0;
            links = 0;
            error = null;

            if (string.IsNullOrEmpty(packId) || string.IsNullOrEmpty(path))
            {
                error = "missing pack id or path";
                return false;
            }
            if (!VpbSqlite3.IsAvailable)
            {
                error = "sqlite unavailable";
                return false;
            }

            Stopwatch sw = Stopwatch.StartNew();
            var reader = new VpbDataPackReader();
            try
            {
                if (!reader.Open(path, out error)) return false;

                if (!string.IsNullOrEmpty(reader.Header.PackId) &&
                    !string.Equals(reader.Header.PackId, packId, StringComparison.OrdinalIgnoreCase))
                {
                    error = "pack id mismatch: file declares '" + reader.Header.PackId + "'";
                    return false;
                }

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureDataPackSchema(conn);

                    try
                    {
                        conn.ExecUtf8("BEGIN IMMEDIATE;");
                        DeleteDataPackRows(conn, packId);

                        int tagRows = 0;
                        int varRows = 0;
                        int identRows = 0;
                        using (var insEntry = conn.Prepare(
                            "INSERT OR REPLACE INTO datapack_entry(pack_id,entry_id,src_id,subject,title,creator," +
                            "category,pay_type,resource_id,link,first_release,last_update," +
                            "tag_line,version,license,min_vam,rating_avg,downloads,rating_count,dep_count,size_kb,flags) " +
                            "VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)"))
                        using (var insTag = conn.Prepare(
                            "INSERT OR IGNORE INTO datapack_tag(pack_id,entry_id,ns,tag) VALUES(?,?,?,?)"))
                        using (var insVar = conn.Prepare(
                            "INSERT OR IGNORE INTO datapack_var(pack_id,entry_id,var_key,alias_key,var_version,exact) " +
                            "VALUES(?,?,?,?,?,?)"))
                        using (var insIdent = conn.Prepare(
                            "INSERT OR IGNORE INTO datapack_ident(pack_id,entry_id,ident_key) VALUES(?,?,?)"))
                        {
                            while (reader.ReadEntry())
                            {
                                insEntry.BindText(1, packId);
                                insEntry.BindInt64(2, reader.EntryId);
                                insEntry.BindText(3, reader.SrcId);
                                insEntry.BindText(4, reader.Subject);
                                insEntry.BindText(5, reader.Title);
                                insEntry.BindText(6, reader.Creator);
                                insEntry.BindText(7, reader.Category);
                                insEntry.BindText(8, reader.PayType);
                                insEntry.BindText(9, reader.ResourceId);
                                insEntry.BindText(10, reader.Link);
                                insEntry.BindText(11, reader.FirstRelease);
                                insEntry.BindText(12, reader.LastUpdate);
                                insEntry.BindText(13, reader.TagLine);
                                insEntry.BindText(14, reader.Version);
                                insEntry.BindText(15, reader.License);
                                insEntry.BindText(16, reader.MinVam);
                                insEntry.BindText(17, reader.RatingAvg);
                                insEntry.BindInt64(18, reader.Downloads);
                                insEntry.BindInt64(19, reader.RatingCount);
                                insEntry.BindInt64(20, reader.DepCount);
                                insEntry.BindInt64(21, reader.SizeKb);
                                insEntry.BindInt64(22, reader.Flags);
                                insEntry.Step();
                                insEntry.Reset();
                                entries++;

                                for (int i = 0; i < reader.TagTexts.Count; i++)
                                {
                                    insTag.BindText(1, packId);
                                    insTag.BindInt64(2, reader.EntryId);
                                    insTag.BindText(3, reader.TagNamespaces[i]);
                                    insTag.BindText(4, reader.TagTexts[i]);
                                    insTag.Step();
                                    insTag.Reset();
                                    tagRows++;
                                }

                                for (int i = 0; i < reader.VarKeys.Count; i++)
                                {
                                    insVar.BindText(1, packId);
                                    insVar.BindInt64(2, reader.EntryId);
                                    insVar.BindText(3, reader.VarKeys[i]);
                                    insVar.BindText(4, reader.VarAliases[i]);
                                    insVar.BindText(5, reader.VarVersions[i]);
                                    insVar.BindInt64(6, reader.VarExact[i]);
                                    insVar.Step();
                                    insVar.Reset();
                                    varRows++;
                                }

                                for (int i = 0; i < reader.IdentKeys.Count; i++)
                                {
                                    insIdent.BindText(1, packId);
                                    insIdent.BindInt64(2, reader.EntryId);
                                    insIdent.BindText(3, reader.IdentKeys[i]);
                                    insIdent.Step();
                                    insIdent.Reset();
                                    identRows++;
                                }
                            }
                        }

                        if (entries == 0)
                        {
                            conn.ExecUtf8("ROLLBACK;");
                            error = "data pack contained no usable entries";
                            return false;
                        }

                        WriteDataPackRow(conn, packId, reader.Header, entries);
                        RefreshPkgUidAliases(conn, false);
                        links = RebuildDataPackLinks(conn, packId);
                        conn.ExecUtf8("COMMIT;");
                        SetDataPackIndexReady(true);

                        try
                        {
                            LogUtil.Log("[VPB.DB] data pack '" + packId + "' applied"
                                + " | version=" + (reader.Header.PackVersion ?? "")
                                + " | entries=" + entries
                                + " | tags=" + tagRows
                                + " | var_keys=" + varRows
                                + " | ident_keys=" + identRows
                                + " | linked_rows=" + links
                                + " | malformed_lines=" + reader.MalformedLines
                                + " | ms=" + sw.ElapsedMilliseconds);
                        }
                        catch { }
                        return true;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { LogUtil.LogWarning("[VPB.DB] data pack '" + packId + "' apply failed: " + ex.Message); } catch { }
                return false;
            }
            finally
            {
                reader.Dispose();
            }
        }

        static void DeleteDataPackRows(VpbSqlite3.Connection conn, string packId)
        {
            string[] tables = { "datapack_link", "datapack_var", "datapack_ident", "datapack_tag", "datapack_entry", "datapack" };
            for (int i = 0; i < tables.Length; i++)
            {
                using (var del = conn.Prepare("DELETE FROM " + tables[i] + " WHERE pack_id=?"))
                {
                    del.BindText(1, packId);
                    del.Step();
                }
            }
        }

        static void WriteDataPackRow(
            VpbSqlite3.Connection conn, string packId, VpbDataPackHeader header, int entries)
        {
            using (var ins = conn.Prepare(
                "INSERT OR REPLACE INTO datapack(pack_id,pack_version,built_date,content_hash,source_url," +
                "attribution,entry_count,applied_utc) VALUES(?,?,?,?,?,?,?,?)"))
            {
                ins.BindText(1, packId);
                ins.BindText(2, header.PackVersion ?? "");
                ins.BindText(3, header.BuiltDate ?? "");
                ins.BindText(4, header.ContentHash ?? "");
                ins.BindText(5, header.SourceUrl ?? "");
                ins.BindText(6, header.Attribution ?? "");
                ins.BindInt64(7, entries);
                ins.BindInt64(8, DateTime.UtcNow.ToBinary());
                ins.Step();
            }
        }

        internal static bool TryRemoveDataPack(string packId)
        {
            if (string.IsNullOrEmpty(packId)) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    if (!DataPackTablesPresent(conn)) return true;
                    try
                    {
                        conn.ExecUtf8("BEGIN IMMEDIATE;");
                        DeleteDataPackRows(conn, packId);
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
                RefreshDataPackIndexReady();
                try { LogUtil.Log("[VPB.DB] data pack '" + packId + "' removed"); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] data pack '" + packId + "' remove failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TryRelinkDataPack(string packId, out int links)
        {
            links = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureDataPackSchema(conn);
                    try
                    {
                        conn.ExecUtf8("BEGIN IMMEDIATE;");
                        RefreshPkgUidAliases(conn, false);
                        links = RebuildDataPackLinks(conn, packId);
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
                SetDataPackIndexReady(true);
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] data pack '" + packId + "' relink failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TryGetDataPackStatus(string packId, out DataPackStatus status)
        {
            status = new DataPackStatus();
            if (string.IsNullOrEmpty(packId)) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return true;

                    using (var st = conn.Prepare(
                        "SELECT pack_version, built_date, content_hash, attribution, entry_count " +
                        "FROM datapack WHERE pack_id=?"))
                    {
                        st.BindText(1, packId);
                        if (st.Step() != VpbSqlite3.SqliteRow) return true;
                        status.Installed = true;
                        status.PackVersion = st.ColumnText(0) ?? "";
                        status.BuiltDate = st.ColumnText(1) ?? "";
                        status.ContentHash = st.ColumnText(2) ?? "";
                        status.Attribution = st.ColumnText(3) ?? "";
                        status.EntryCount = (int)st.ColumnInt64(4);
                    }

                    using (var st = conn.Prepare(
                        "SELECT COUNT(DISTINCT entry_id), COUNT(DISTINCT pkg_uid) " +
                        "FROM datapack_link WHERE pack_id=?"))
                    {
                        st.BindText(1, packId);
                        if (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            status.LinkedEntries = (int)st.ColumnInt64(0);
                            status.LinkedPackages = (int)st.ColumnInt64(1);
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

        internal static bool TryGetDataPackLookOverlay(string pkgUid, out DataPackLookOverlay overlay)
        {
            overlay = new DataPackLookOverlay();
            if (string.IsNullOrEmpty(pkgUid) || !DataPackLookSearchEnabled()) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            int rev = VpbDataPackService.StatusRevision;
            if (s_OverlayByUid == null)
                s_OverlayByUid = new Dictionary<string, DataPackLookOverlay>(StringComparer.OrdinalIgnoreCase);
            if (s_OverlayMapRev != rev)
            {
                s_OverlayByUid.Clear();
                s_OverlayMapRev = rev;
            }
            else if (s_OverlayByUid.TryGetValue(pkgUid, out overlay))
                return overlay.Found;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn))
                    {
                        RememberLookOverlay(pkgUid, rev, overlay);
                        return false;
                    }

                    using (var st = conn.Prepare(
                        "SELECT de.subject, de.category " +
                        "FROM datapack_link dl " +
                        "CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id " +
                        "WHERE dl.pkg_uid=? AND length(trim(ifnull(de.subject,'')))>0 " +
                        "ORDER BY dl.match_kind ASC LIMIT 1"))
                    {
                        st.BindText(1, pkgUid);
                        if (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            overlay.Subject = st.ColumnText(0) ?? "";
                            overlay.Category = st.ColumnText(1) ?? "";
                        }
                    }

                    overlay.Found = !string.IsNullOrEmpty(overlay.Subject)
                        || !string.IsNullOrEmpty(overlay.Category);

                    var qsb = new StringBuilder(320);
                    qsb.Append("SELECT DISTINCT dt.tag FROM datapack_link dl ");
                    qsb.Append("CROSS JOIN datapack_tag dt ON dt.pack_id=dl.pack_id AND dt.entry_id=dl.entry_id ");
                    qsb.Append("AND dt.ns='hub' WHERE dl.pkg_uid=?");
                    AppendDataPackTagNotHiddenSql(qsb, "dt.tag", "dl.pkg_uid");
                    qsb.Append(" ORDER BY dt.tag LIMIT ").Append(DataPackOverlayHubTagCap);

                    var sb = new StringBuilder(48);
                    int n = 0;
                    using (var st = conn.Prepare(qsb.ToString()))
                    {
                        st.BindText(1, pkgUid);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string tag = st.ColumnText(0);
                            if (string.IsNullOrEmpty(tag)) continue;
                            if (n > 0) sb.Append(", ");
                            sb.Append(tag);
                            n++;
                        }
                    }
                    overlay.HubTagsFmt = n > 0 ? sb.ToString() : "";
                    if (n > 0) overlay.Found = true;
                }
            }
            catch
            {
                overlay = new DataPackLookOverlay();
            }

            RememberLookOverlay(pkgUid, rev, overlay);
            return overlay.Found;
        }

        internal static bool TryReadDataPackHubTagsForPackage(string pkgUid, List<string> dest, int cap)
        {
            if (dest == null) return false;
            dest.Clear();
            if (string.IsNullOrEmpty(pkgUid) || !DataPackLookSearchEnabled()) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            if (cap <= 0) cap = DataPackMenuHubTagCap;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return false;
                    using (var st = conn.Prepare(
                        "SELECT DISTINCT dt.tag FROM datapack_link dl " +
                        "CROSS JOIN datapack_tag dt ON dt.pack_id=dl.pack_id AND dt.entry_id=dl.entry_id " +
                        "AND dt.ns='hub' WHERE dl.pkg_uid=? ORDER BY dt.tag LIMIT " + cap))
                    {
                        st.BindText(1, pkgUid);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string tag = st.ColumnText(0);
                            if (!string.IsNullOrEmpty(tag)) dest.Add(tag);
                        }
                    }
                }
                return true;
            }
            catch
            {
                dest.Clear();
                return false;
            }
        }

        static void RememberLookOverlay(string pkgUid, int rev, DataPackLookOverlay overlay)
        {
            if (s_OverlayByUid == null)
                s_OverlayByUid = new Dictionary<string, DataPackLookOverlay>(StringComparer.OrdinalIgnoreCase);
            if (s_OverlayMapRev != rev)
            {
                s_OverlayByUid.Clear();
                s_OverlayMapRev = rev;
            }
            if (s_OverlayByUid.Count >= OverlayCacheMax)
                s_OverlayByUid.Clear();
            s_OverlayByUid[pkgUid] = overlay;
        }
    }
}
