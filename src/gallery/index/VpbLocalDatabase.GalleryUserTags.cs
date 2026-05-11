using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MVR.FileManagement;

namespace VPB
{
    /// <summary>SQLite user-tag tables (<c>gallery_user_tag</c> / <c>gallery_item_user_tag</c>), normalization, and queries. Split from <see cref="VpbLocalDatabase"/> for clarity; same static partial type — no call-site or perf change.</summary>
    internal static partial class VpbLocalDatabase
    {
        private static void EnsureGalleryUserTagTables(VpbSqlite3.Connection conn)
        {
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS gallery_user_tag (tag_id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE);" +
                "CREATE TABLE IF NOT EXISTS gallery_item_user_tag (category TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, tag_id INTEGER NOT NULL, PRIMARY KEY(category, pkg_uid, internal_path, tag_id), FOREIGN KEY(tag_id) REFERENCES gallery_user_tag(tag_id) ON DELETE CASCADE);" +
                "CREATE INDEX IF NOT EXISTS idx_giut_tag ON gallery_item_user_tag(tag_id);" +
                "CREATE INDEX IF NOT EXISTS idx_giut_pkg_path ON gallery_item_user_tag(pkg_uid, internal_path);");
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

        internal const int GalleryUserTagNameMaxLength = 512;
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
        /// Normalize gallery user tag: trim ends, lowercase for stable dedupe, allow unicode/emoji/punctuation/spaces/slashes;
        /// reject null, line breaks, most control chars (tab allowed); length 1–<see cref="GalleryUserTagNameMaxLength"/>.
        /// </summary>
        internal static string NormalizeGalleryUserTagName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Trim().ToLowerInvariant();
            if (s.Length == 0 || s.Length > GalleryUserTagNameMaxLength) return "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\0') return "";
                if (c == '\n' || c == '\r') return "";
                if (c == '\t') continue;
                if (char.IsControl(c)) return "";
            }
            return s;
        }

        /// <summary>Cave say: some chars bad for Windows file names; DB still store, cave warn honest.</summary>
        internal static bool GalleryUserTagNameHasFilesystemRisk(string normalizedName, out string distinctBadCharsHuman)
        {
            distinctBadCharsHuman = "";
            if (string.IsNullOrEmpty(normalizedName)) return false;
            char[] invalid = Path.GetInvalidFileNameChars();
            var distinct = new List<char>(8);
            for (int i = 0; i < normalizedName.Length; i++)
            {
                char c = normalizedName[i];
                if (Array.IndexOf(invalid, c) < 0) continue;
                bool already = false;
                for (int d = 0; d < distinct.Count; d++)
                {
                    if (distinct[d] == c) { already = true; break; }
                }
                if (!already) distinct.Add(c);
            }
            if (distinct.Count == 0) return false;
            var sb = new StringBuilder(distinct.Count * 3);
            for (int i = 0; i < distinct.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                char c = distinct[i];
                if (c == ' ') sb.Append("(space)");
                else if (c < 32) sb.Append("(ctrl 0x").Append(((int)c).ToString("X2")).Append(")");
                else { sb.Append("'").Append(c).Append("'"); }
            }
            distinctBadCharsHuman = sb.ToString();
            return true;
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

            // Do not gate on s_ReadyScanBinary / category rebuild: vocabulary query has no such gate; mismatch left every count at 0 while names still loaded.

            string normalizedPackagePathFilter = "";
            bool hasPackagePathFilter = false;
            if (!string.IsNullOrEmpty(packagePathFilter))
            {
                normalizedPackagePathFilter = packagePathFilter.Replace('\\', '/').Trim().Trim('/');
                hasPackagePathFilter = normalizedPackagePathFilter.Length > 0;
            }
            bool hasCreator = !string.IsNullOrEmpty(creatorFilter);
            bool allVarPseudo = IsGalleryAllVarPseudoCategory(categoryTitle);
            // ALL VAR + pkg path: same rule as TryReadCategoryMemberCounts — path filter only with creator.
            bool pathFilterBind = allVarPseudo ? (hasCreator && hasPackagePathFilter) : hasPackagePathFilter;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    var sb = new StringBuilder(800);
                    // Path normalize: cat_mem / gut rows may differ by '\' vs '/' only.
                    const string PathNorm = "lower(replace(ifnull(__COL__,''),char(92),'/'))";
                    string pathEqMemGut = PathNorm.Replace("__COL__", "m.internal_path") + "=" + PathNorm.Replace("__COL__", "gut.internal_path");
                    // ALL VAR: cat_mem has no "ALL VAR" rows; gut.category holds real browse category (Appearance, …).
                    // Join cat_mem on pkg+path only; DISTINCT avoids double count when same item appears under multiple categories.
                    if (allVarPseudo)
                    {
                        sb.Append("SELECT gt.name, COUNT(DISTINCT gut.pkg_uid || char(31) || lower(replace(ifnull(gut.internal_path,''),char(92),'/')) ) FROM gallery_item_user_tag gut");
                        sb.Append(" INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id");
                        sb.Append(" INNER JOIN cat_mem m ON m.pkg_uid=gut.pkg_uid AND ").Append(pathEqMemGut);
                    }
                    else
                    {
                        sb.Append("SELECT gt.name, COUNT(*) FROM gallery_item_user_tag gut");
                        sb.Append(" INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id");
                        sb.Append(" INNER JOIN cat_mem m ON m.category=gut.category AND m.pkg_uid=gut.pkg_uid AND ").Append(pathEqMemGut);
                    }
                    sb.Append(" INNER JOIN pkg p ON p.uid=m.pkg_uid");
                    sb.Append(" WHERE 1=1");
                    if (!allVarPseudo)
                        sb.Append(" AND gut.category=?");
                    if (hasCreator) sb.Append(" AND p.creator=?");
                    if (pathFilterBind)
                        sb.Append(" AND lower(replace(ifnull(p.var_path,''),'\\','/')) LIKE ? ESCAPE '\\'");
                    sb.Append(" GROUP BY gt.name");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (!allVarPseudo)
                            stmt.BindText(bind++, categoryTitle);
                        if (hasCreator) stmt.BindText(bind++, creatorFilter);
                        if (pathFilterBind)
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

                    // Loose Custom/Saves use pkg_uid __local__; cat_mem is VAR-only so INNER JOIN above drops them.
                    // Skip when browsing ALL VAR pseudo-category (grid is VAR-centric; loose rows use real browse categories).
                    if (!allVarPseudo)
                    {
                        var sbLoose = new StringBuilder(220);
                        sbLoose.Append("SELECT gt.name, COUNT(*) FROM gallery_item_user_tag gut INNER JOIN gallery_user_tag gt ON gt.tag_id=gut.tag_id WHERE gut.pkg_uid=? AND gut.category=?");
                        sbLoose.Append(" GROUP BY gt.name");
                        using (var stLoose = conn.Prepare(sbLoose.ToString()))
                        {
                            int b = 1;
                            stLoose.BindText(b++, GalleryUserTagLoosePkgUid);
                            stLoose.BindText(b++, categoryTitle);
                            int stepL;
                            while ((stepL = stLoose.Step()) == VpbSqlite3.SqliteRow)
                            {
                                string name = stLoose.ColumnText(0) ?? "";
                                int n;
                                if (!int.TryParse(stLoose.ColumnText(1), out n)) n = (int)stLoose.ColumnInt64(1);
                                if (string.IsNullOrEmpty(name)) continue;
                                int prev;
                                countsOut.TryGetValue(name, out prev);
                                countsOut[name] = prev + n;
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

        /// <summary>Shared prefix-rename target list: exact <paramref name="normPrefix"/> row plus rows named <c>normPrefix + " " + …</c>.</summary>
        private static bool TryBuildGalleryUserTagRenameTargets(string normPrefix, string normalizedNewOut, out List<KeyValuePair<long, string>> targetsOut)
        {
            targetsOut = null;
            if (string.IsNullOrEmpty(normPrefix) || string.IsNullOrEmpty(normalizedNewOut)) return false;

            var rows = new List<KeyValuePair<long, string>>(64);
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    // Cave avoid SQL LIKE: % and _ inside tag name must not act wildcard.
                    using (var sel = conn.Prepare("SELECT tag_id, name FROM gallery_user_tag"))
                    {
                        while (sel.Step() == VpbSqlite3.SqliteRow)
                        {
                            long tid = sel.ColumnInt64(0);
                            string nm = sel.ColumnText(1) ?? "";
                            if (string.IsNullOrEmpty(nm)) continue;
                            if (string.Equals(nm, normPrefix, StringComparison.OrdinalIgnoreCase))
                                rows.Add(new KeyValuePair<long, string>(tid, nm));
                            else if (nm.Length > normPrefix.Length
                                && nm.StartsWith(normPrefix, StringComparison.OrdinalIgnoreCase)
                                && nm[normPrefix.Length] == ' ')
                                rows.Add(new KeyValuePair<long, string>(tid, nm));
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            if (rows.Count == 0) return false;

            var targets = new List<KeyValuePair<long, string>>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                string nm = rows[i].Value;
                string newName;
                if (string.Equals(nm, normPrefix, StringComparison.OrdinalIgnoreCase))
                    newName = normalizedNewOut;
                else if (nm.Length > normPrefix.Length
                    && nm.StartsWith(normPrefix, StringComparison.OrdinalIgnoreCase)
                    && nm[normPrefix.Length] == ' ')
                    newName = normalizedNewOut + nm.Substring(normPrefix.Length);
                else
                    continue;

                string check = NormalizeGalleryUserTagName(newName);
                if (string.IsNullOrEmpty(check))
                    return false;
                targets.Add(new KeyValuePair<long, string>(rows[i].Key, check));
            }

            if (targets.Count == 0) return false;
            targetsOut = targets;
            return true;
        }

        /// <summary>
        /// True if some rename target name already exists on another <c>tag_id</c> (assignments would merge into that row).
        /// </summary>
        internal static bool TryPreviewGalleryUserTagRenameMergeConflict(string rawPrefixName, string rawNewName, out string normalizedNewOut, out bool wouldMergeIntoExistingTag)
        {
            normalizedNewOut = "";
            wouldMergeIntoExistingTag = false;
            if (!VpbSqlite3.IsAvailable) return false;
            string normPrefix = NormalizeGalleryUserTagName(rawPrefixName);
            normalizedNewOut = NormalizeGalleryUserTagName(rawNewName);
            if (string.IsNullOrEmpty(normPrefix) || string.IsNullOrEmpty(normalizedNewOut)) return false;
            if (string.Equals(normPrefix, normalizedNewOut, StringComparison.OrdinalIgnoreCase)) return false;

            if (!TryBuildGalleryUserTagRenameTargets(normPrefix, normalizedNewOut, out List<KeyValuePair<long, string>> targets))
                return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var selName = conn.Prepare("SELECT tag_id FROM gallery_user_tag WHERE name=?"))
                    {
                        for (int i = 0; i < targets.Count; i++)
                        {
                            long sourceTid = targets[i].Key;
                            string destName = targets[i].Value;
                            selName.BindText(1, destName);
                            if (selName.Step() != VpbSqlite3.SqliteRow)
                                continue;
                            long existingTid = selName.ColumnInt64(0);
                            if (existingTid != sourceTid)
                                wouldMergeIntoExistingTag = true;
                            selName.Reset();
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Renames <paramref name="rawPrefixName"/> to <paramref name="rawNewName"/> and renames every tag whose name is
        /// <c>prefix + " " + …</c> so the same prefix is replaced (space-separated “child” tag names in vocabulary).
        /// </summary>
        internal static bool TryRenameGalleryUserTagPrefixWithChildren(string rawPrefixName, string rawNewName, out string normalizedNewOut, out int itemAssignmentsUpdated)
        {
            normalizedNewOut = "";
            itemAssignmentsUpdated = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            string normPrefix = NormalizeGalleryUserTagName(rawPrefixName);
            normalizedNewOut = NormalizeGalleryUserTagName(rawNewName);
            if (string.IsNullOrEmpty(normPrefix) || string.IsNullOrEmpty(normalizedNewOut)) return false;
            if (string.Equals(normPrefix, normalizedNewOut, StringComparison.OrdinalIgnoreCase)) return false;

            if (!TryBuildGalleryUserTagRenameTargets(normPrefix, normalizedNewOut, out List<KeyValuePair<long, string>> targets))
                return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN;");
                    try
                    {
                        for (int i = 0; i < targets.Count; i++)
                        {
                            long sourceTid = targets[i].Key;
                            string destName = targets[i].Value;
                            long targetTid = TryGetOrCreateGalleryUserTagId(conn, destName);
                            if (targetTid < 0)
                            {
                                conn.ExecUtf8("ROLLBACK;");
                                itemAssignmentsUpdated = 0;
                                return false;
                            }
                            if (sourceTid == targetTid)
                                continue;

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

        // --- BA migration helpers ---

        internal struct GalleryUserTagImportRow
        {
            public string Category;
            public string PkgUid;
            public string InternalPath;
            public string[] Tags;
        }

        /// <summary>Retrieves category membership for a single gallery item from cat_mem.</summary>
        internal static bool TryGetCategoryForItem(string pkgUid, string internalPath, out string category)
        {
            category = null;
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(pkgUid) || string.IsNullOrEmpty(internalPath))
                return false;
            string ip = internalPath.Replace('\\', '/');
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT category FROM cat_mem WHERE pkg_uid=? AND internal_path=? LIMIT 1"))
                    {
                        st.BindText(1, pkgUid);
                        st.BindText(2, ip);
                        if (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            category = st.ColumnText(0);
                            return !string.IsNullOrEmpty(category);
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>Bulk-insert gallery user tag assignments; ignores duplicates. Respects <see cref="GalleryUserTagMaxPerItem"/> cap.</summary>
        internal static bool BulkMergeGalleryUserTags(IList<GalleryUserTagImportRow> rows)
        {
            if (rows == null || rows.Count == 0) return true;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN;");
                    try
                    {
                        using (var insIt = conn.Prepare(
                            "INSERT OR IGNORE INTO gallery_item_user_tag(category, pkg_uid, internal_path, tag_id) VALUES(?,?,?,?)"))
                        using (var cntRow = conn.Prepare(
                            "SELECT COUNT(*) FROM gallery_item_user_tag WHERE category=? AND pkg_uid=? AND internal_path=?"))
                        {
                            for (int ri = 0; ri < rows.Count; ri++)
                            {
                                var row = rows[ri];
                                if (string.IsNullOrEmpty(row.Category) || string.IsNullOrEmpty(row.PkgUid) ||
                                    string.IsNullOrEmpty(row.InternalPath) || row.Tags == null || row.Tags.Length == 0)
                                    continue;
                                string ip = row.InternalPath.Replace('\\', '/');

                                cntRow.Reset();
                                cntRow.BindText(1, row.Category);
                                cntRow.BindText(2, row.PkgUid);
                                cntRow.BindText(3, ip);
                                int rowTagCount = 0;
                                if (cntRow.Step() == VpbSqlite3.SqliteRow)
                                    rowTagCount = (int)cntRow.ColumnInt64(0);

                                for (int ti = 0; ti < row.Tags.Length; ti++)
                                {
                                    string name = NormalizeGalleryUserTagName(row.Tags[ti]);
                                    if (string.IsNullOrEmpty(name)) continue;
                                    if (rowTagCount >= GalleryUserTagMaxPerItem) break;
                                    long tid = TryGetOrCreateGalleryUserTagId(conn, name);
                                    if (tid < 0) continue;
                                    insIt.Reset();
                                    insIt.BindText(1, row.Category);
                                    insIt.BindText(2, row.PkgUid);
                                    insIt.BindText(3, ip);
                                    insIt.BindInt64(4, tid);
                                    insIt.Step();
                                    rowTagCount++;
                                }
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
                return false;
            }
        }

        /// <summary>Remove specific user tags from a gallery item (BA migration reset).</summary>
        internal static bool RemoveGalleryUserTagsForItem(string category, string pkgUid, string internalPath, IEnumerable<string> tags)
        {
            return TryRemoveGalleryUserTagsFromRow(category, pkgUid, internalPath.Replace('\\', '/'), tags, out _);
        }
    }
}
