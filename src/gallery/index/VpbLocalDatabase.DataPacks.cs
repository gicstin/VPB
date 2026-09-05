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
            public string HubTypeCategory;
            public string HubTagsFmt;
        }

        internal const int DataPackMatchKindIdent = 2;
        internal const int DataPackMatchKindNamePrefix = 3;
        internal const int DataPackMatchKindCategoryOnly = 4;
        const int DataPackContainmentMinLen = 4;

        internal static void AppendDataPackLinkIdentityOnlySql(StringBuilder sb, string alias)
        {
            if (sb == null) return;
            sb.Append(" AND ").Append(string.IsNullOrEmpty(alias) ? "dl" : alias)
              .Append(".match_kind<>").Append(DataPackMatchKindCategoryOnly);
        }

        internal const string DataPackLinkIdentityOnlyAnd = " AND dl.match_kind<>4";
        const int PkgIdentPrefixMinLen = 5;
        const int PkgIdentPrefixMaxTokens = 5;
        const int DataPackIdentAmbiguityCap = 8;
        const string PkgIdentPrefixVersion = "1";
        const string PkgIdentPrefixMetaKey = "pkg_ident_prefix_ver";
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
                            AppendDataPackLinkIdentityOnlySql(sb, "dl");
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
                        "WHERE 1=1" + DataPackLinkIdentityOnlyAnd + " " +
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

        internal static string DataPackHubCategorySearchToken(string category)
        {
            return DataPackFacetValueToken(category);
        }

        internal static void AppendDataPackHubTypePackScopeSql(StringBuilder sb)
        {
            if (sb == null) return;
            sb.Append(" AND (de.pack_id='").Append(VpbDataPackService.HubTagsPackId)
                .Append("' OR de.pack_id='").Append(VpbDataPackService.HubLivePackId).Append("')");
        }

        internal enum SceneHubBucket
        {
            Unclassified = 0,
            HubScenes = 1,
            HubLooks = 2,
            Other = 3,
        }

        internal static bool SceneHubSubfilterIsNarrowing(GalleryPanel.SceneHubSubfilter f)
        {
            return f != 0 && f != GalleryPanel.SceneHubSubfilter.All;
        }

        internal static SceneHubBucket ClassifySceneHubBucket(string hubTypeCategory)
        {
            if (string.IsNullOrEmpty(hubTypeCategory)) return SceneHubBucket.Unclassified;
            if (string.Equals(hubTypeCategory, "Looks", StringComparison.OrdinalIgnoreCase))
                return SceneHubBucket.HubLooks;
            if (string.Equals(hubTypeCategory, "Scenes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(hubTypeCategory, "Demo + Lite", StringComparison.OrdinalIgnoreCase))
                return SceneHubBucket.HubScenes;
            return SceneHubBucket.Other;
        }

        static Dictionary<string, int> s_SceneHubMaskByUid;
        static int s_SceneHubMaskRev = -1;
        static bool s_SceneHubMaskFailed;

        internal static bool TryGetSceneHubBucketMasks(out Dictionary<string, int> map)
        {
            map = null;
            if (!DataPackLookSearchEnabled() || !VpbSqlite3.IsAvailable) return false;

            int rev = VpbDataPackService.StatusRevision;
            if (s_SceneHubMaskRev != rev)
            {
                s_SceneHubMaskByUid = null;
                s_SceneHubMaskFailed = false;
                s_SceneHubMaskRev = rev;
            }
            if (s_SceneHubMaskFailed) return false;
            if (s_SceneHubMaskByUid != null)
            {
                map = s_SceneHubMaskByUid;
                return true;
            }

            long tStart = Stopwatch.GetTimestamp();
            var built = new Dictionary<string, int>(4096, StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn))
                    {
                        s_SceneHubMaskByUid = built;
                        map = built;
                        return true;
                    }
                    var sb = new StringBuilder(320);
                    sb.Append("SELECT DISTINCT dl.pkg_uid, de.category FROM datapack_link dl ");
                    sb.Append("CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id ");
                    sb.Append("WHERE length(trim(ifnull(de.category,'')))>0");
                    AppendDataPackHubTypePackScopeSql(sb);
                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string uid = st.ColumnText(0);
                            if (string.IsNullOrEmpty(uid)) continue;
                            int bit = (int)SceneHubBucketToFlag(ClassifySceneHubBucket(st.ColumnText(1)));
                            int cur;
                            built[uid] = built.TryGetValue(uid, out cur) ? (cur | bit) : bit;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] scene hub bucket map failed: " + ex.Message); }
                catch { }
                s_SceneHubMaskFailed = true;
                return false;
            }
            s_SceneHubMaskByUid = built;
            map = built;
            ReportDataPackStepTiming("scene hub bucket map (" + built.Count + ")", tStart, SceneHubMaskBudgetMs);
            return true;
        }

        const int SceneHubMaskBudgetMs = 1500;

        static GalleryPanel.SceneHubSubfilter SceneHubBucketToFlag(SceneHubBucket b)
        {
            if (b == SceneHubBucket.HubScenes) return GalleryPanel.SceneHubSubfilter.HubScenes;
            if (b == SceneHubBucket.HubLooks) return GalleryPanel.SceneHubSubfilter.HubLooks;
            if (b == SceneHubBucket.Other) return GalleryPanel.SceneHubSubfilter.Other;
            return GalleryPanel.SceneHubSubfilter.Unclassified;
        }

        internal static bool PassesSceneHubSubfilterMask(
            GalleryPanel.SceneHubSubfilter f, Dictionary<string, int> map, string pkgUid)
        {
            if (!SceneHubSubfilterIsNarrowing(f)) return true;
            int mask;
            if (map == null || string.IsNullOrEmpty(pkgUid)
                || !map.TryGetValue(pkgUid, out mask) || mask == 0)
                return (f & GalleryPanel.SceneHubSubfilter.Unclassified) != 0;
            return (((int)f) & mask) != 0;
        }

        internal static string BuildSceneHubSubfilterSqlAnd(
            VpbSqlite3.Connection conn,
            string categoryTitle,
            GalleryPanel.SceneHubSubfilter f)
        {
            if (!GalleryPanel.IsGalleryScenesCategory(categoryTitle)) return "";
            if (!SceneHubSubfilterIsNarrowing(f)) return "";
            if (conn == null || !DataPackTablesPresent(conn)) return "";

            bool wantU = (f & GalleryPanel.SceneHubSubfilter.Unclassified) != 0;
            bool wantS = (f & GalleryPanel.SceneHubSubfilter.HubScenes) != 0;
            bool wantL = (f & GalleryPanel.SceneHubSubfilter.HubLooks) != 0;
            bool wantO = (f & GalleryPanel.SceneHubSubfilter.Other) != 0;

            var sb = new StringBuilder(640);
            sb.Append(" AND (");
            bool first = true;
            if (wantU)
            {
                first = false;
                sb.Append("NOT EXISTS (SELECT 1 FROM datapack_link dl ");
                sb.Append("CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id ");
                sb.Append("WHERE dl.pkg_uid=m.pkg_uid AND length(trim(ifnull(de.category,'')))>0");
                AppendDataPackHubTypePackScopeSql(sb);
                sb.Append(')');
            }
            if (wantS)
            {
                if (!first) sb.Append(" OR ");
                first = false;
                AppendSceneHubCategoryExistsSql(sb, true, false, false);
            }
            if (wantL)
            {
                if (!first) sb.Append(" OR ");
                first = false;
                AppendSceneHubCategoryExistsSql(sb, false, true, false);
            }
            if (wantO)
            {
                if (!first) sb.Append(" OR ");
                AppendSceneHubCategoryExistsSql(sb, false, false, true);
            }
            sb.Append(')');
            return sb.ToString();
        }

        static void AppendSceneHubCategoryExistsSql(StringBuilder sb, bool scenes, bool looks, bool other)
        {
            sb.Append("EXISTS (SELECT 1 FROM datapack_link dl ");
            sb.Append("CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id ");
            sb.Append("WHERE dl.pkg_uid=m.pkg_uid AND length(trim(ifnull(de.category,'')))>0");
            AppendDataPackHubTypePackScopeSql(sb);
            sb.Append(" AND ");
            if (looks)
                sb.Append("lower(trim(ifnull(de.category,'')))='looks'");
            else if (scenes)
                sb.Append("(lower(trim(ifnull(de.category,'')))='scenes' OR lower(trim(ifnull(de.category,'')))='demo + lite')");
            else if (other)
            {
                sb.Append("lower(trim(ifnull(de.category,''))) NOT IN ('looks','scenes','demo + lite')");
            }
            else
                sb.Append("1=0");
            sb.Append(')');
        }

        internal static string DataPackHubCategoryDisplayName(string value)
        {
            return GalleryHubCategoryNames.Display(value);
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

        internal static bool TryCollectHubCategoryFacetRows(
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

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool ok = TryReadGroupedFileCounts(
                counts,
                FileCountGroupMode.DataPackHubCategory,
                extensionPipeSeparated, pathPrefixes, singlePathPrefix,
                activeTags, categoryTitle, packagePathFilter, activeUserTags);

            if (!ok)
                return TryCollectHubCategoryPackageFacetRows(dest);

            dest.Clear();
            foreach (var kv in counts)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                dest.Add(new CreatorCacheEntry
                {
                    Name = DataPackHubCategoryDisplayName(kv.Key),
                    Count = kv.Value
                });
            }
            return true;
        }

        internal static bool TryCollectHubCategoryPackageFacetRows(List<CreatorCacheEntry> dest)
        {
            return FillHubCategoryPackageCounts(dest);
        }

        internal static bool TryCollectHubCategoryItemFacetRows(
            List<CreatorCacheEntry> dest,
            Func<string, List<string>> scopeResolver)
        {
            if (dest == null) return false;
            dest.Clear();
            if (!VpbSqlite3.IsAvailable) return false;

            var byHubThenCategory = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            long tStart = Stopwatch.GetTimestamp();
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return true;
                    var sb = new StringBuilder(560);
                    sb.Append("SELECT t.c, m.category, COUNT(*) FROM (");
                    sb.Append("SELECT DISTINCT dl.pkg_uid AS u, de.category AS c FROM datapack_link dl ");
                    sb.Append("CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id ");
                    sb.Append("WHERE length(trim(ifnull(de.category,'')))>0");
                    AppendDataPackHubTypePackScopeSql(sb);
                    sb.Append(") t CROSS JOIN cat_mem m ON m.pkg_uid = t.u WHERE 1=1");
                    sb.Append(BuildEverythingNonPreviewAnd("m.internal_path"));
                    sb.Append(" GROUP BY t.c, m.category");
                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string hub = st.ColumnText(0);
                            string cat = st.ColumnText(1);
                            if (string.IsNullOrEmpty(hub) || string.IsNullOrEmpty(cat)) continue;
                            string display = DataPackHubCategoryDisplayName(hub);
                            if (string.IsNullOrEmpty(display)) continue;
                            Dictionary<string, int> perCat;
                            if (!byHubThenCategory.TryGetValue(display, out perCat) || perCat == null)
                            {
                                perCat = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                byHubThenCategory[display] = perCat;
                            }
                            int cur;
                            perCat.TryGetValue(cat, out cur);
                            perCat[cat] = cur + (int)st.ColumnInt64(2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] hub category item facet collect failed: " + ex.Message); } catch { }
                dest.Clear();
                return FillHubCategoryPackageCounts(dest);
            }

            foreach (var kv in byHubThenCategory)
            {
                List<string> scope = scopeResolver != null ? scopeResolver(kv.Key) : null;
                int total = 0;
                if (scope == null || scope.Count == 0)
                {
                    foreach (var c in kv.Value) total += c.Value;
                }
                else
                {
                    for (int i = 0; i < scope.Count; i++)
                    {
                        int n;
                        if (kv.Value.TryGetValue(scope[i], out n)) total += n;
                    }
                }
                if (total <= 0) continue;
                dest.Add(new CreatorCacheEntry { Name = kv.Key, Count = total });
            }
            dest.Sort(CompareHubCategoryItemRows);
            ReportDataPackStepTiming("hub category item counts (" + dest.Count + ")", tStart, HubCategoryItemCountBudgetMs);
            return true;
        }

        const int HubCategoryItemCountBudgetMs = 2500;

        static int CompareHubCategoryItemRows(CreatorCacheEntry a, CreatorCacheEntry b)
        {
            if (a.Count != b.Count) return b.Count.CompareTo(a.Count);
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        static bool FillHubCategoryPackageCounts(List<CreatorCacheEntry> dest)
        {
            if (dest == null) return false;
            dest.Clear();
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTablesPresent(conn)) return true;
                    var sb = new StringBuilder(320);
                    sb.Append("SELECT de.category, COUNT(DISTINCT dl.pkg_uid) FROM datapack_link dl ");
                    sb.Append("CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id ");
                    sb.Append("WHERE length(trim(ifnull(de.category,'')))>0");
                    AppendDataPackHubTypePackScopeSql(sb);
                    sb.Append(" GROUP BY de.category ORDER BY COUNT(DISTINCT dl.pkg_uid) DESC, de.category ASC");
                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string name = st.ColumnText(0);
                            if (string.IsNullOrEmpty(name)) continue;
                            dest.Add(new CreatorCacheEntry
                            {
                                Name = DataPackHubCategoryDisplayName(name),
                                Count = (int)st.ColumnInt64(1)
                            });
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] hub category facet collect failed: " + ex.Message); } catch { }
                dest.Clear();
                return false;
            }
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
                        AppendDataPackLinkIdentityOnlySql(hsb, "dl");
                        AppendDataPackTagNotHiddenSql(hsb, "dt.tag", "dl.pkg_uid");
                        hsb.Append(" GROUP BY dt.tag");
                        sql = hsb.ToString();
                    }
                    else
                    {
                        sql = "SELECT de.subject, COUNT(DISTINCT dl.pkg_uid) FROM datapack_link dl " +
                              "CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id " +
                              "WHERE length(trim(ifnull(de.subject,'')))>0" + DataPackLinkIdentityOnlyAnd +
                              " GROUP BY de.subject";
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
                "CREATE INDEX IF NOT EXISTS idx_dp_entry_resource ON datapack_entry(resource_id);" +
                "CREATE INDEX IF NOT EXISTS idx_dp_entry_pack_cat ON datapack_entry(pack_id, category);");

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
            try
            {
                conn.ExecUtf8(
                    "CREATE TABLE IF NOT EXISTS pkg_ident (" +
                    "pkg_uid TEXT NOT NULL, ident_key TEXT NOT NULL, klen INTEGER NOT NULL," +
                    "PRIMARY KEY(pkg_uid, ident_key));" +
                    "CREATE INDEX IF NOT EXISTS idx_pkg_ident_prefix ON pkg_ident(ident_key);");
            }
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

        static void CollectPkgIdentPrefixKeys(string uid, List<string> destKeys, List<int> destLens)
        {
            if (destKeys == null || destLens == null) return;
            destKeys.Clear();
            destLens.Clear();
            if (string.IsNullOrEmpty(uid)) return;

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
            if (split <= 0 || split >= s.Length - 1) return;
            string creator = NormalizeIdentPart(s.Substring(0, split));
            if (creator.Length == 0) return;
            string name = s.Substring(split + 1);

            var acc = new System.Text.StringBuilder(name.Length);
            var whole = new System.Text.StringBuilder(name.Length);
            int tokens = 0;
            int i2 = 0;
            while (i2 < name.Length)
            {
                if (!IsIdentWordChar(name[i2])) { i2++; continue; }
                int start = i2;
                i2++;
                while (i2 < name.Length && IsIdentWordChar(name[i2]))
                {
                    char prev = name[i2 - 1];
                    char cur = name[i2];
                    if (cur >= 'A' && cur <= 'Z'
                        && ((prev >= 'a' && prev <= 'z') || (prev >= '0' && prev <= '9')))
                        break;
                    i2++;
                }
                for (int k = start; k < i2; k++)
                {
                    char c = name[k];
                    if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                    whole.Append(c);
                    if (tokens < PkgIdentPrefixMaxTokens) acc.Append(c);
                }
                tokens++;
                if (tokens <= PkgIdentPrefixMaxTokens && acc.Length >= PkgIdentPrefixMinLen)
                    AddPkgIdentKey(destKeys, destLens, creator, acc.ToString());
            }

            if (whole.Length >= 2)
                AddPkgIdentKey(destKeys, destLens, creator, whole.ToString());
        }

        static bool IsIdentWordChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        static void AddPkgIdentKey(List<string> destKeys, List<int> destLens, string creator, string name)
        {
            for (int i = 0; i < destLens.Count; i++)
            {
                if (destLens[i] == name.Length) return;
            }
            destKeys.Add(creator + "|" + name);
            destLens.Add(name.Length);
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

            int written = 0;
            if (uids.Count > 0)
            {
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
            }

            RefreshPkgIdentPrefixKeys(conn);
            return written;
        }

        static void RefreshPkgIdentPrefixKeys(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            long tStart = Stopwatch.GetTimestamp();
            int inserted = 0;
            try
            {
                EnsurePkgIdentTable(conn);
                bool rebuildAll = !string.Equals(
                    MetaGet(conn, PkgIdentPrefixMetaKey), PkgIdentPrefixVersion, StringComparison.Ordinal);
                conn.ExecUtf8(rebuildAll
                    ? "DELETE FROM pkg_ident;"
                    : "DELETE FROM pkg_ident WHERE pkg_uid NOT IN (SELECT uid FROM pkg);");

                var todo = new List<string>(1024);
                using (var st = conn.Prepare(
                    "SELECT p.uid FROM pkg p LEFT JOIN pkg_ident pi ON pi.pkg_uid = p.uid " +
                    "WHERE pi.pkg_uid IS NULL"))
                {
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string uid = st.ColumnText(0);
                        if (!string.IsNullOrEmpty(uid)) todo.Add(uid);
                    }
                }

                if (todo.Count > 0)
                {
                    var keys = new List<string>(8);
                    var lens = new List<int>(8);
                    using (var ins = conn.Prepare(
                        "INSERT OR IGNORE INTO pkg_ident(pkg_uid,ident_key,klen) VALUES(?,?,?)"))
                    {
                        for (int i = 0; i < todo.Count; i++)
                        {
                            CollectPkgIdentPrefixKeys(todo[i], keys, lens);
                            for (int k = 0; k < keys.Count; k++)
                            {
                                ins.BindText(1, todo[i]);
                                ins.BindText(2, keys[k]);
                                ins.BindInt64(3, lens[k]);
                                ins.Step();
                                ins.Reset();
                                inserted++;
                            }
                        }
                    }
                }

                if (rebuildAll) MetaSet(conn, PkgIdentPrefixMetaKey, PkgIdentPrefixVersion);
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] pkg_ident prefix refresh failed: " + ex.Message); }
                catch { }
            }
            if (inserted > 0)
                ReportDataPackStepTiming("pkg_ident keys (+" + inserted + ")", tStart, PkgIdentRefreshBudgetMs);
        }

        const int PkgIdentRefreshBudgetMs = 4000;

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

            AppendDataPackNamePrefixLinks(conn, packId);
            AppendDataPackCategoryOnlyLinks(conn, packId);

            BackfillDataPackResourceIds(conn);
            int inherited = InheritDataPackLinksByResourceId(conn);
            if (inherited > 0)
            {
                try { LogUtil.Log("[VPB.DB] data pack resource_id inherit: +" + inherited + " links"); }
                catch { }
            }

            return (int)ScalarInt64(conn, "SELECT COUNT(*) FROM datapack_link;");
        }

        static void AppendDataPackNamePrefixLinks(VpbSqlite3.Connection conn, string packId)
        {
            if (conn == null) return;
            bool scoped = !string.IsNullOrEmpty(packId);
            long tStart = Stopwatch.GetTimestamp();
            try
            {
                EnsurePkgIdentTable(conn);

                const string hitSelect =
                    "SELECT di.pack_id, pi.pkg_uid, di.entry_id, pi.klen " +
                    "FROM pkg_ident pi " +
                    "CROSS JOIN datapack_ident di ON di.ident_key = pi.ident_key " +
                    "WHERE di.ident_key <> '' " +
                    "AND NOT EXISTS (SELECT 1 FROM temp.dp_ident_amb a " +
                    "WHERE a.pack_id = di.pack_id AND a.ident_key = di.ident_key)";

                conn.ExecUtf8(
                    "DROP TABLE IF EXISTS temp.dp_ident_amb;" +
                    "CREATE TEMP TABLE dp_ident_amb(pack_id TEXT NOT NULL, ident_key TEXT NOT NULL," +
                    "PRIMARY KEY(pack_id, ident_key));" +
                    "INSERT OR IGNORE INTO temp.dp_ident_amb(pack_id, ident_key) " +
                    "SELECT pack_id, ident_key FROM datapack_ident " +
                    "GROUP BY pack_id, ident_key HAVING COUNT(*) > " + DataPackIdentAmbiguityCap + ";");

                if (!DataPackPrefixJoinPlanIsSane(conn, hitSelect))
                {
                    LogUtil.LogWarning(
                        "[VPB.DB] name-prefix link tier SKIPPED: planner is not seeking datapack_ident by " +
                        "idx_dp_ident_key, which is the 570 s plan. Links keep the var and whole-name tiers.");
                    return;
                }

                conn.ExecUtf8(
                    "DROP TABLE IF EXISTS temp.dp_prefix_hit;" +
                    "CREATE TEMP TABLE dp_prefix_hit(pack_id TEXT NOT NULL, pkg_uid TEXT NOT NULL," +
                    "entry_id INTEGER NOT NULL, klen INTEGER NOT NULL);" +
                    "INSERT INTO temp.dp_prefix_hit(pack_id, pkg_uid, entry_id, klen) " + hitSelect + ";" +
                    "CREATE INDEX temp.idx_dp_prefix_hit ON dp_prefix_hit(pack_id, pkg_uid, klen);" +

                    "DROP TABLE IF EXISTS temp.dp_prefix_pick;" +
                    "CREATE TEMP TABLE dp_prefix_pick(pack_id TEXT NOT NULL, pkg_uid TEXT NOT NULL," +
                    "klen INTEGER NOT NULL, PRIMARY KEY(pack_id, pkg_uid));" +
                    "INSERT OR IGNORE INTO temp.dp_prefix_pick(pack_id, pkg_uid, klen) " +
                    "SELECT pack_id, pkg_uid, MAX(klen) FROM temp.dp_prefix_hit " +
                    "GROUP BY pack_id, pkg_uid;");

                string linkSql =
                    "INSERT OR IGNORE INTO datapack_link(pack_id,pkg_uid,entry_id,match_kind) " +
                    "SELECT h.pack_id, h.pkg_uid, h.entry_id, " + DataPackMatchKindNamePrefix + " " +
                    "FROM temp.dp_prefix_hit h " +
                    "CROSS JOIN temp.dp_prefix_pick pk ON pk.pack_id = h.pack_id " +
                    "AND pk.pkg_uid = h.pkg_uid AND pk.klen = h.klen";

                if (scoped)
                {
                    using (var st = conn.Prepare(linkSql + " WHERE h.pack_id=?"))
                    {
                        st.BindText(1, packId);
                        st.Step();
                    }
                }
                else
                    conn.ExecUtf8(linkSql + ";");
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] data pack name-prefix link pass failed: " + ex.Message); }
                catch { }
            }
            finally
            {
                try
                {
                    conn.ExecUtf8(
                        "DROP TABLE IF EXISTS temp.dp_prefix_pick;" +
                        "DROP TABLE IF EXISTS temp.dp_prefix_hit;" +
                        "DROP TABLE IF EXISTS temp.dp_ident_amb;");
                }
                catch { }
                ReportDataPackStepTiming("name-prefix links", tStart, DataPackPrefixPassBudgetMs);
            }
        }

        const int DataPackPrefixPassBudgetMs = 3000;

        struct CategoryOnlyCandidate
        {
            public long EntryId;
            public string NameKey;
            public string Category;
        }

        static void AppendDataPackCategoryOnlyLinks(VpbSqlite3.Connection conn, string packId)
        {
            if (conn == null) return;
            long tStart = Stopwatch.GetTimestamp();
            int inserted = 0;
            try
            {
                var packIds = new List<string>(2);
                if (!string.IsNullOrEmpty(packId))
                {
                    if (IsDataPackHubListingPack(packId)) packIds.Add(packId);
                }
                else
                {
                    using (var st = conn.Prepare("SELECT pack_id FROM datapack"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string p = st.ColumnText(0);
                            if (!string.IsNullOrEmpty(p) && IsDataPackHubListingPack(p)) packIds.Add(p);
                        }
                    }
                }
                if (packIds.Count == 0) return;

                var alreadyLinked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var st = conn.Prepare(
                    "SELECT DISTINCT pkg_uid FROM datapack_link WHERE pack_id='"
                    + VpbDataPackService.HubTagsPackId + "' OR pack_id='"
                    + VpbDataPackService.HubLivePackId + "'"))
                {
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string u = st.ColumnText(0);
                        if (!string.IsNullOrEmpty(u)) alreadyLinked.Add(u);
                    }
                }

                var pkgCreators = new List<string>(4096);
                var pkgNames = new List<string>(4096);
                var pkgUids = new List<string>(4096);
                using (var st = conn.Prepare("SELECT uid, ifnull(ident_key,'') FROM pkg WHERE ifnull(ident_key,'')<>''"))
                {
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string uid = st.ColumnText(0);
                        string key = st.ColumnText(1);
                        if (string.IsNullOrEmpty(uid) || alreadyLinked.Contains(uid)) continue;
                        string cr, nm;
                        if (!TrySplitIdentKey(key, out cr, out nm)) continue;
                        if (nm.Length < DataPackContainmentMinLen) continue;
                        pkgUids.Add(uid);
                        pkgCreators.Add(cr);
                        pkgNames.Add(nm);
                    }
                }
                if (pkgUids.Count == 0) return;

                for (int pi = 0; pi < packIds.Count; pi++)
                    inserted += AppendCategoryOnlyLinksForPack(conn, packIds[pi], pkgUids, pkgCreators, pkgNames);
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] data pack category-only link pass failed: " + ex.Message); }
                catch { }
            }
            finally
            {
                if (inserted > 0)
                    ReportDataPackStepTiming("category-only links (+" + inserted + ")", tStart, DataPackCategoryOnlyBudgetMs);
            }
        }

        static int AppendCategoryOnlyLinksForPack(
            VpbSqlite3.Connection conn,
            string packId,
            List<string> pkgUids,
            List<string> pkgCreators,
            List<string> pkgNames)
        {
            var categories = new Dictionary<long, string>(4096);
            using (var st = conn.Prepare(
                "SELECT entry_id, category FROM datapack_entry WHERE pack_id=? " +
                "AND length(trim(ifnull(category,'')))>0"))
            {
                st.BindText(1, packId);
                while (st.Step() == VpbSqlite3.SqliteRow)
                    categories[st.ColumnInt64(0)] = st.ColumnText(1);
            }
            if (categories.Count == 0) return 0;

            var byCreator = new Dictionary<string, List<CategoryOnlyCandidate>>(2048, StringComparer.Ordinal);
            using (var st = conn.Prepare("SELECT entry_id, ident_key FROM datapack_ident WHERE pack_id=?"))
            {
                st.BindText(1, packId);
                while (st.Step() == VpbSqlite3.SqliteRow)
                {
                    long eid = st.ColumnInt64(0);
                    string cat;
                    if (!categories.TryGetValue(eid, out cat)) continue;
                    string cr, nm;
                    if (!TrySplitIdentKey(st.ColumnText(1), out cr, out nm)) continue;
                    if (nm.Length < DataPackContainmentMinLen) continue;
                    List<CategoryOnlyCandidate> bucket;
                    if (!byCreator.TryGetValue(cr, out bucket) || bucket == null)
                    {
                        bucket = new List<CategoryOnlyCandidate>(8);
                        byCreator[cr] = bucket;
                    }
                    CategoryOnlyCandidate cc;
                    cc.EntryId = eid;
                    cc.NameKey = nm;
                    cc.Category = cat;
                    bucket.Add(cc);
                }
            }
            if (byCreator.Count == 0) return 0;

            int inserted = 0;
            using (var ins = conn.Prepare(
                "INSERT OR IGNORE INTO datapack_link(pack_id,pkg_uid,entry_id,match_kind) VALUES(?,?,?,"
                + DataPackMatchKindCategoryOnly + ")"))
            {
                for (int i = 0; i < pkgUids.Count; i++)
                {
                    List<CategoryOnlyCandidate> bucket;
                    if (!byCreator.TryGetValue(pkgCreators[i], out bucket) || bucket == null) continue;

                    string name = pkgNames[i];
                    string wonCategory = null;
                    long wonEntry = 0;
                    int wonLen = -1;
                    bool ambiguous = false;
                    for (int b = 0; b < bucket.Count; b++)
                    {
                        CategoryOnlyCandidate cc = bucket[b];
                        if (name.IndexOf(cc.NameKey, StringComparison.Ordinal) < 0
                            && cc.NameKey.IndexOf(name, StringComparison.Ordinal) < 0)
                            continue;
                        if (wonCategory == null)
                        {
                            wonCategory = cc.Category;
                            wonEntry = cc.EntryId;
                            wonLen = cc.NameKey.Length;
                            continue;
                        }
                        if (!string.Equals(wonCategory, cc.Category, StringComparison.OrdinalIgnoreCase))
                        {
                            ambiguous = true;
                            break;
                        }
                        if (cc.NameKey.Length > wonLen)
                        {
                            wonEntry = cc.EntryId;
                            wonLen = cc.NameKey.Length;
                        }
                    }
                    if (ambiguous || wonCategory == null) continue;

                    ins.BindText(1, packId);
                    ins.BindText(2, pkgUids[i]);
                    ins.BindInt64(3, wonEntry);
                    ins.Step();
                    ins.Reset();
                    inserted++;
                }
            }
            return inserted;
        }

        static bool IsDataPackHubListingPack(string packId)
        {
            return string.Equals(packId, VpbDataPackService.HubTagsPackId, StringComparison.Ordinal)
                || string.Equals(packId, VpbDataPackService.HubLivePackId, StringComparison.Ordinal);
        }

        static bool TrySplitIdentKey(string identKey, out string creator, out string name)
        {
            creator = null;
            name = null;
            if (string.IsNullOrEmpty(identKey)) return false;
            int bar = identKey.IndexOf('|');
            if (bar <= 0 || bar >= identKey.Length - 1) return false;
            creator = identKey.Substring(0, bar);
            name = identKey.Substring(bar + 1);
            return true;
        }

        const int DataPackCategoryOnlyBudgetMs = 4000;

        static void ReportDataPackStepTiming(string label, long startTicks, int budgetMs)
        {
            try
            {
                double ms = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
                if (ms >= budgetMs)
                    LogUtil.LogWarning("[VPB.DB] data pack " + label + " took " + ms.ToString("F0")
                        + " ms (budget " + budgetMs + " ms) — check the query plan before shipping.");
                else
                    LogUtil.Log("[VPB.DB] data pack " + label + " " + ms.ToString("F0") + " ms");
            }
            catch { }
        }

        static bool DataPackPrefixJoinPlanIsSane(VpbSqlite3.Connection conn, string joinSql)
        {
            bool sawIdentTable = false;
            try
            {
                using (var st = conn.Prepare("EXPLAIN QUERY PLAN " + joinSql))
                {
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string detail = st.ColumnText(3);
                        if (string.IsNullOrEmpty(detail)) continue;
                        if (detail.IndexOf("datapack_ident", StringComparison.Ordinal) < 0) continue;
                        sawIdentTable = true;
                        if (detail.IndexOf("idx_dp_ident_key", StringComparison.Ordinal) >= 0)
                            return true;
                    }
                }
            }
            catch
            {
                return true;
            }
            return !sawIdentTable;
        }

        static void EnsurePkgIdentTable(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            try
            {
                conn.ExecUtf8(
                    "CREATE TABLE IF NOT EXISTS pkg_ident (" +
                    "pkg_uid TEXT NOT NULL, ident_key TEXT NOT NULL, klen INTEGER NOT NULL," +
                    "PRIMARY KEY(pkg_uid, ident_key));" +
                    "CREATE INDEX IF NOT EXISTS idx_pkg_ident_prefix ON pkg_ident(ident_key);");
            }
            catch { }
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
                        "WHERE dl.pkg_uid=? AND length(trim(ifnull(de.subject,'')))>0" +
                        DataPackLinkIdentityOnlyAnd + " " +
                        "ORDER BY dl.match_kind ASC LIMIT 1"))
                    {
                        st.BindText(1, pkgUid);
                        if (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            overlay.Subject = st.ColumnText(0) ?? "";
                            overlay.Category = st.ColumnText(1) ?? "";
                        }
                    }

                    using (var st = conn.Prepare(
                        "SELECT de.category, MIN(dl.match_kind) mk FROM datapack_link dl " +
                        "CROSS JOIN datapack_entry de ON de.pack_id=dl.pack_id AND de.entry_id=dl.entry_id " +
                        "WHERE dl.pkg_uid=? AND length(trim(ifnull(de.category,'')))>0 " +
                        "AND (de.pack_id='" + VpbDataPackService.HubTagsPackId + "' OR de.pack_id='" + VpbDataPackService.HubLivePackId + "') " +
                        "GROUP BY lower(trim(de.category)) ORDER BY mk ASC"))
                    {
                        st.BindText(1, pkgUid);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string shown = DataPackHubCategoryDisplayName(st.ColumnText(0) ?? "");
                            if (shown.Length == 0) continue;
                            overlay.HubTypeCategory = shown;
                            break;
                        }
                    }

                    overlay.Found = !string.IsNullOrEmpty(overlay.Subject)
                        || !string.IsNullOrEmpty(overlay.Category)
                        || !string.IsNullOrEmpty(overlay.HubTypeCategory);

                    var qsb = new StringBuilder(320);
                    qsb.Append("SELECT DISTINCT dt.tag FROM datapack_link dl ");
                    qsb.Append("CROSS JOIN datapack_tag dt ON dt.pack_id=dl.pack_id AND dt.entry_id=dl.entry_id ");
                    qsb.Append("AND dt.ns='hub' WHERE dl.pkg_uid=?");
                    AppendDataPackLinkIdentityOnlySql(qsb, "dl");
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
                        "AND dt.ns='hub' WHERE dl.pkg_uid=?" + DataPackLinkIdentityOnlyAnd +
                        " ORDER BY dt.tag LIMIT " + cap))
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
