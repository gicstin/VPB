using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    /// <summary>Title-bar search SQL fragments (broad OR + explicit tag:/time/creator:).</summary>
    internal static partial class VpbLocalDatabase
    {
        /// <summary>Row key for in-memory tag joins: pkg_uid + NUL + internal_path (ordinal).</summary>
        internal static string MakeGalleryRowKey(string pkgUid, string internalPath)
        {
            return (pkgUid ?? "") + "\0" + (internalPath ?? "");
        }

        /// <summary>
        /// Collects <c>pkg_uid\0internal_path</c> keys whose user-tag name matches any substring in
        /// <paramref name="tagSubstrings"/> (OR across substrings). Category-scoped like grid filters.
        /// </summary>
        internal static bool TryCollectRowKeysWithUserTagSubstrings(
            string categoryTitle,
            IList<string> tagSubstrings,
            HashSet<string> keysOut)
        {
            if (keysOut == null) return false;
            keysOut.Clear();
            if (tagSubstrings == null || tagSubstrings.Count == 0) return true;
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (!TryCollectRowKeysWithUserTagSubstringsPerTerm(categoryTitle, tagSubstrings, map))
                return false;
            foreach (var kv in map)
            {
                if (kv.Value == null) continue;
                foreach (string k in kv.Value)
                    keysOut.Add(k);
            }
            return true;
        }

        /// <summary>
        /// One SQLite connection: for each substring, fill <paramref name="keysByTermOut"/>[term] with matching row keys.
        /// Skips terms shorter than 2 chars (leading-wildcard LIKE on 1 char is too expensive for keystroke search).
        /// </summary>
        internal static bool TryCollectRowKeysWithUserTagSubstringsPerTerm(
            string categoryTitle,
            IList<string> tagSubstrings,
            Dictionary<string, HashSet<string>> keysByTermOut)
        {
            if (keysByTermOut == null) return false;
            keysByTermOut.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            if (tagSubstrings == null || tagSubstrings.Count == 0) return true;

            var terms = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tagSubstrings.Count; i++)
            {
                string t = tagSubstrings[i];
                if (string.IsNullOrEmpty(t) || t.Length < 2) continue;
                if (!seen.Add(t)) continue;
                terms.Add(t);
                keysByTermOut[t] = new HashSet<string>(StringComparer.Ordinal);
            }
            if (terms.Count == 0) return true;

            bool everything = Gallery.IsEverythingCategoryName(categoryTitle);
            bool allVar = IsGalleryAllVarPseudoCategory(categoryTitle);
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    // Vocab is small — filter tag ids first, then expand rows via idx_giut_tag.
                    const string sql =
                        "SELECT gut.pkg_uid, gut.internal_path FROM gallery_item_user_tag gut " +
                        "WHERE gut.tag_id IN (SELECT tag_id FROM gallery_user_tag WHERE lower(name) LIKE ? ESCAPE '\\')";
                    string sqlCat = sql + " AND gut.category=?";

                    for (int ti = 0; ti < terms.Count; ti++)
                    {
                        string term = terms[ti];
                        HashSet<string> keys = keysByTermOut[term];
                        string pat = "%" + EscapeLike(term.ToLowerInvariant()) + "%";
                        string useSql = (!everything && !allVar && !string.IsNullOrEmpty(categoryTitle)) ? sqlCat : sql;
                        using (var st = conn.Prepare(useSql))
                        {
                            st.BindText(1, pat);
                            if (!everything && !allVar && !string.IsNullOrEmpty(categoryTitle))
                                st.BindText(2, categoryTitle);
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string pkg = st.ColumnText(0) ?? "";
                                string ip = st.ColumnText(1) ?? "";
                                if (pkg.Length == 0) continue;
                                keys.Add(MakeGalleryRowKey(pkg, ip));
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                keysByTermOut.Clear();
                return false;
            }
        }

        /// <summary>Append EXISTS user-tag name LIKE for cat_mem alias <paramref name="mAlias"/>.</summary>
        /// <remarks>
        /// Tag-id subquery first (small vocab / <c>idx</c>), then match row — avoids JOIN+LIKE
        /// per outer <c>cat_mem</c> row (was making category loads with search very slow).
        /// </remarks>
        private static void AppendSqlUserTagNameLikeExists(
            StringBuilder sb,
            List<string> bindOut,
            string mAlias,
            string termLower,
            bool negate,
            bool everythingView,
            string categoryTitle)
        {
            if (sb == null || bindOut == null || string.IsNullOrEmpty(mAlias) || string.IsNullOrEmpty(termLower))
                return;
            bool allVar = IsGalleryAllVarPseudoCategory(categoryTitle);
            string catExpr;
            if (allVar || everythingView)
                catExpr = null; // match any category row for that pkg/path
            else
                catExpr = mAlias + ".category";

            sb.Append(negate ? " AND NOT EXISTS (" : " AND EXISTS (");
            sb.Append("SELECT 1 FROM gallery_item_user_tag gut");
            sb.Append(" WHERE gut.pkg_uid=").Append(mAlias).Append(".pkg_uid");
            sb.Append(" AND gut.internal_path=").Append(mAlias).Append(".internal_path");
            if (catExpr != null)
                sb.Append(" AND gut.category=").Append(catExpr);
            sb.Append(" AND gut.tag_id IN (SELECT tag_id FROM gallery_user_tag WHERE lower(name) LIKE ? ESCAPE '\\')");
            sb.Append(')');
            bindOut.Add("%" + EscapeLike(termLower) + "%");
        }

        /// <summary>
        /// Builds broad-term / tag: / creator: / status fragments into <paramref name="ctx"/>.
        /// OR-branches become <c>AND ( (branch1) OR (branch2) … )</c>.
        /// </summary>
        private static void AppendGallerySearchQueryToWhere(
            GalleryCategoryWhereContext ctx,
            GallerySearchQuery query,
            string categoryTitle,
            bool everythingView)
        {
            if (ctx.NameBindValues == null) ctx.NameBindValues = new List<string>();
            if (ctx.SearchInt64BindValues == null) ctx.SearchInt64BindValues = new List<long>();

            if (query == null || query.IsEmpty || query.Branches == null || query.Branches.Count == 0)
            {
                ctx.NameAndFragment = "";
                ctx.SearchTimeAndFragment = "";
                return;
            }

            var sb = new StringBuilder();
            int n = 0;
            for (int bi = 0; bi < query.Branches.Count; bi++)
            {
                GallerySearchBranch br = query.Branches[bi];
                if (br == null || br.IsEmpty) continue;
                n++;
            }
            if (n == 0)
            {
                ctx.NameAndFragment = "";
                ctx.SearchTimeAndFragment = "";
                return;
            }

            if (n == 1)
            {
                for (int bi = 0; bi < query.Branches.Count; bi++)
                {
                    GallerySearchBranch br = query.Branches[bi];
                    if (br == null || br.IsEmpty) continue;
                    AppendGallerySearchBranchToSql(sb, ctx.NameBindValues, br, categoryTitle, everythingView, ctx.PkgHasLoadedCol);
                    break;
                }
                ctx.NameAndFragment = sb.ToString();
                ctx.SearchTimeAndFragment = "";
                return;
            }

            sb.Append(" AND (");
            bool first = true;
            for (int bi = 0; bi < query.Branches.Count; bi++)
            {
                GallerySearchBranch br = query.Branches[bi];
                if (br == null || br.IsEmpty) continue;
                if (!first) sb.Append(" OR ");
                first = false;
                sb.Append("(1=1");
                AppendGallerySearchBranchToSql(sb, ctx.NameBindValues, br, categoryTitle, everythingView, ctx.PkgHasLoadedCol);
                sb.Append(')');
            }
            sb.Append(')');
            ctx.NameAndFragment = sb.ToString();
            ctx.SearchTimeAndFragment = "";
        }

        private static void AppendGallerySearchBranchToSql(
            StringBuilder sb,
            List<string> binds,
            GallerySearchBranch br,
            string categoryTitle,
            bool everythingView,
            bool pkgHasLoadedCol)
        {
            if (sb == null || binds == null || br == null) return;

            // Broad include/exclude: match GalleryPanel.FileEntryMatchesBroadTerm PathAndName surface
            // (path + creator + uid + user-tag substring). NameOnly / NameStartsWith are omitted from SQL
            // by the panel and applied in PassesFilters.
            if (br.BroadTerms != null)
            {
                for (int i = 0; i < br.BroadTerms.Count; i++)
                {
                    string t = br.BroadTerms[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append(" AND (");
                    AppendGalleryBroadTermMatchOrGroup(sb, binds, t, categoryTitle, everythingView);
                    sb.Append(')');
                }
            }
            if (br.BroadExclude != null)
            {
                for (int i = 0; i < br.BroadExclude.Count; i++)
                {
                    string t = br.BroadExclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append(" AND NOT (");
                    AppendGalleryBroadTermMatchOrGroup(sb, binds, t, categoryTitle, everythingView);
                    sb.Append(')');
                }
            }

            if (br.TagInclude != null)
            {
                for (int i = 0; i < br.TagInclude.Count; i++)
                {
                    string t = br.TagInclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    AppendSqlUserTagNameLikeExists(sb, binds, "m", t, false, everythingView, categoryTitle);
                }
            }
            if (br.TagExclude != null)
            {
                for (int i = 0; i < br.TagExclude.Count; i++)
                {
                    string t = br.TagExclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    AppendSqlUserTagNameLikeExists(sb, binds, "m", t, true, everythingView, categoryTitle);
                }
            }

            AppendDataPackTerms(sb, binds, br.PackSubjectInclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.Subject, false);
            AppendDataPackTerms(sb, binds, br.PackSubjectExclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.Subject, true);
            AppendDataPackTerms(sb, binds, br.PackHubTagInclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.HubTag, false);
            AppendDataPackTerms(sb, binds, br.PackHubTagExclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.HubTag, true);
            AppendDataPackTerms(sb, binds, br.PackHubCatInclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.HubCategory, false);
            AppendDataPackTerms(sb, binds, br.PackHubCatExclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.HubCategory, true);
            AppendDataPackTerms(sb, binds, br.PackAnyInclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.Any, false);
            AppendDataPackTerms(sb, binds, br.PackAnyExclude, "m.pkg_uid", GallerySearchQuery.DataPackAtomKind.Any, true);
            if (br.CreatorTerms != null)
            {
                for (int i = 0; i < br.CreatorTerms.Count; i++)
                {
                    string t = br.CreatorTerms[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append(" AND ifnull(p.creator,'') LIKE ? ESCAPE '\\'");
                    binds.Add("%" + EscapeLike(t) + "%");
                }
            }

            if (br.HasFlag(GallerySearchQuery.StatusFlags.Loaded))
            {
                if (pkgHasLoadedCol) sb.Append(" AND ifnull(p.loaded,0) != 0");
                else sb.Append(" AND 0");
            }
            else if (br.HasFlag(GallerySearchQuery.StatusFlags.Unloaded))
            {
                if (pkgHasLoadedCol) sb.Append(" AND ifnull(p.loaded,0) = 0");
            }

            if (br.HasFlag(GallerySearchQuery.StatusFlags.Tagged))
            {
                sb.Append(" AND EXISTS (SELECT 1 FROM gallery_item_user_tag gut");
                sb.Append(" WHERE gut.pkg_uid=m.pkg_uid AND gut.internal_path=m.internal_path");
                if (!everythingView && !IsGalleryAllVarPseudoCategory(categoryTitle))
                    sb.Append(" AND gut.category=m.category");
                sb.Append(')');
            }
            else if (br.HasFlag(GallerySearchQuery.StatusFlags.Untagged))
            {
                AppendSqlNoUserTagExists(sb, "m", categoryTitle, everythingView);
            }
        }

        /// <summary>
        /// OR-group body for one broad search term (no leading AND/NOT).
        /// Surfaces: list_path, internal_path, creator, uid, user-tag name LIKE.
        /// </summary>
        static void AppendDataPackTerms(
            StringBuilder sb,
            List<string> binds,
            List<string> terms,
            string pkgUidSql,
            GallerySearchQuery.DataPackAtomKind kind,
            bool negate)
        {
            if (sb == null || binds == null || terms == null || terms.Count == 0) return;

            if (negate)
            {
                for (int i = 0; i < terms.Count; i++)
                {
                    string t = terms[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    AppendSqlDataPackExists(sb, binds, pkgUidSql, t, kind, true, " AND ");
                }
                return;
            }

            int mark = sb.Length;
            int bindMark = binds.Count;
            sb.Append(" AND (");
            int emitted = 0;
            for (int i = 0; i < terms.Count; i++)
            {
                string t = terms[i];
                if (string.IsNullOrEmpty(t)) continue;
                int textAt = sb.Length;
                int bindAt = binds.Count;
                if (!AppendSqlDataPackExists(sb, binds, pkgUidSql, t, kind, false, emitted == 0 ? "" : " OR "))
                {
                    sb.Length = textAt;
                    if (binds.Count > bindAt) binds.RemoveRange(bindAt, binds.Count - bindAt);
                    continue;
                }
                emitted++;
            }
            if (emitted == 0)
            {
                sb.Length = mark;
                if (binds.Count > bindMark) binds.RemoveRange(bindMark, binds.Count - bindMark);
                return;
            }
            sb.Append(')');
        }

        internal static bool AppendSqlDataPackExists(
            StringBuilder sb,
            List<string> binds,
            string pkgUidSql,
            string termLower,
            GallerySearchQuery.DataPackAtomKind kind,
            bool negate,
            string connector)
        {
            if (sb == null || binds == null || string.IsNullOrEmpty(termLower)) return false;
            string uidSql = string.IsNullOrEmpty(pkgUidSql) ? "m.pkg_uid" : pkgUidSql;
            string lead = connector ?? "";

            bool exact;
            string body;
            DataPackTermSplitExact(termLower, out exact, out body);
            if (string.IsNullOrEmpty(body)) return false;

            if (kind == GallerySearchQuery.DataPackAtomKind.Subject
                && TryAppendDataPackSubjectUidSet(sb, uidSql, body, exact, negate, lead, false))
                return true;

            string pat = exact ? body : ("%" + EscapeLike(body) + "%");

            sb.Append(lead).Append(negate ? "NOT EXISTS (" : "EXISTS (");
            sb.Append("SELECT 1 FROM datapack_link dl WHERE dl.pkg_uid=").Append(uidSql);
            if (kind != GallerySearchQuery.DataPackAtomKind.HubCategory)
                AppendDataPackLinkIdentityOnlySql(sb, "dl");
            sb.Append(" AND ");

            if (kind == GallerySearchQuery.DataPackAtomKind.HubTag)
            {
                sb.Append("EXISTS (SELECT 1 FROM datapack_tag dt WHERE dt.pack_id=dl.pack_id");
                sb.Append(" AND dt.entry_id=dl.entry_id AND dt.ns='hub' AND ");
                if (exact) sb.Append("lower(trim(ifnull(dt.tag,''))) = ?");
                else sb.Append("dt.tag LIKE ? ESCAPE '\\'");
                AppendDataPackTagNotHiddenSql(sb, "dt.tag", "dl.pkg_uid");
                sb.Append(')');
                binds.Add(pat);
            }
            else if (kind == GallerySearchQuery.DataPackAtomKind.Subject)
            {
                sb.Append("EXISTS (SELECT 1 FROM datapack_entry de WHERE de.pack_id=dl.pack_id");
                sb.Append(" AND de.entry_id=dl.entry_id AND ");
                if (exact) sb.Append("lower(trim(ifnull(de.subject,''))) = ?");
                else sb.Append("lower(ifnull(de.subject,'')) LIKE ? ESCAPE '\\'");
                sb.Append(')');
                AppendDataPackSubjectPackScopeSql(sb);
                binds.Add(pat);
                AddDataPackSubjectPackScopeBinds(binds);
            }
            else if (kind == GallerySearchQuery.DataPackAtomKind.HubCategory)
            {
                sb.Append("EXISTS (SELECT 1 FROM datapack_entry de WHERE de.pack_id=dl.pack_id");
                sb.Append(" AND de.entry_id=dl.entry_id");
                AppendDataPackHubTypePackScopeSql(sb);
                sb.Append(" AND ");
                if (exact) sb.Append("lower(trim(ifnull(de.category,''))) = ?");
                else sb.Append("lower(ifnull(de.category,'')) LIKE ? ESCAPE '\\'");
                sb.Append(')');
                binds.Add(pat);
            }
            else
            {
                // lap: stays substring even when quoted — any-field expert search.
                string likePat = "%" + EscapeLike(body) + "%";
                sb.Append("(EXISTS (SELECT 1 FROM datapack_entry de WHERE de.pack_id=dl.pack_id");
                sb.Append(" AND de.entry_id=dl.entry_id AND (");
                sb.Append("lower(ifnull(de.subject,'')) LIKE ? ESCAPE '\\'");
                sb.Append(" OR lower(ifnull(de.title,'')) LIKE ? ESCAPE '\\'");
                sb.Append(" OR lower(ifnull(de.creator,'')) LIKE ? ESCAPE '\\'");
                sb.Append(" OR lower(ifnull(de.category,'')) LIKE ? ESCAPE '\\'");
                sb.Append(" OR lower(ifnull(de.tag_line,'')) LIKE ? ESCAPE '\\'))");
                sb.Append(" OR EXISTS (SELECT 1 FROM datapack_tag dt WHERE dt.pack_id=dl.pack_id");
                sb.Append(" AND dt.entry_id=dl.entry_id AND dt.tag LIKE ? ESCAPE '\\'");
                if (DataPackTagPrefsActive())
                {
                    sb.Append(" AND (dt.ns<>'hub' OR ");
                    AppendDataPackTagNotHiddenPredicate(sb, "dt.tag", "dl.pkg_uid");
                    sb.Append(')');
                }
                sb.Append("))");
                for (int i = 0; i < 6; i++) binds.Add(likePat);
            }

            sb.Append(')');
            return true;
        }

        internal static void DataPackTermSplitExact(string termLower, out bool exact, out string body)
        {
            exact = false;
            body = termLower ?? "";
            if (body.Length > 1 && body[0] == '=')
            {
                exact = true;
                body = body.Substring(1);
            }
        }

        internal static bool TryCollectPackageUidsForDataPackTerms(
            IList<string> terms,
            GallerySearchQuery.DataPackAtomKind kind,
            Dictionary<string, HashSet<string>> uidsByTermOut)
        {
            if (uidsByTermOut == null) return false;
            if (terms == null || terms.Count == 0) return true;
            if (!VpbSqlite3.IsAvailable) return false;

            var pending = new List<string>();
            for (int i = 0; i < terms.Count; i++)
            {
                string t = terms[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (uidsByTermOut.ContainsKey(t)) continue;
                uidsByTermOut[t] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                pending.Add(t);
            }
            if (pending.Count == 0) return true;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    var sb = new StringBuilder(384);
                    var binds = new List<string>(8);
                    for (int i = 0; i < pending.Count; i++)
                    {
                        string term = pending[i];
                        sb.Length = 0;
                        binds.Clear();
                        sb.Append("SELECT DISTINCT m.pkg_uid FROM datapack_link m WHERE 1=1");
                        AppendSqlDataPackExists(sb, binds, "m.pkg_uid", term, kind, false, " AND ");

                        HashSet<string> uids = uidsByTermOut[term];
                        using (var st = conn.Prepare(sb.ToString()))
                        {
                            for (int b = 0; b < binds.Count; b++) st.BindText(b + 1, binds[b]);
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                if (!string.IsNullOrEmpty(uid)) uids.Add(uid);
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

        private static void AppendGalleryBroadTermMatchOrGroup(
            StringBuilder sb,
            List<string> binds,
            string termLower,
            string categoryTitle,
            bool everythingView)
        {
            if (sb == null || binds == null || string.IsNullOrEmpty(termLower)) return;
            string esc = "%" + EscapeLike(termLower) + "%";
            sb.Append("m.list_path LIKE ? ESCAPE '\\'");
            sb.Append(" OR m.internal_path LIKE ? ESCAPE '\\'");
            sb.Append(" OR ifnull(p.creator,'') LIKE ? ESCAPE '\\'");
            sb.Append(" OR ifnull(p.uid,'') LIKE ? ESCAPE '\\'");
            binds.Add(esc);
            binds.Add(esc);
            binds.Add(esc);
            binds.Add(esc);

            // User-tag substring (same as in-memory broad OR). Skip 1-char — too expensive / noisy.
            if (termLower.Length < 2) return;

            bool allVar = IsGalleryAllVarPseudoCategory(categoryTitle);
            sb.Append(" OR EXISTS (");
            sb.Append("SELECT 1 FROM gallery_item_user_tag gut");
            sb.Append(" WHERE gut.pkg_uid=m.pkg_uid");
            sb.Append(" AND gut.internal_path=m.internal_path");
            if (!everythingView && !allVar && !string.IsNullOrEmpty(categoryTitle))
                sb.Append(" AND gut.category=m.category");
            sb.Append(" AND gut.tag_id IN (SELECT tag_id FROM gallery_user_tag WHERE lower(name) LIKE ? ESCAPE '\\')");
            sb.Append(')');
            binds.Add(esc);

            // Look-A-Pedia subject ("this looks like") — same OR as in-memory FileEntryMatchesBroadTerm.
            if (DataPackSubjectSearchEnabled()
                && !TryAppendDataPackSubjectUidSet(sb, "m.pkg_uid", termLower, false, false, " OR ", true))
            {
                sb.Append(" OR EXISTS (SELECT 1 FROM datapack_link dl WHERE dl.pkg_uid=m.pkg_uid");
                AppendDataPackLinkIdentityOnlySql(sb, "dl");
                AppendDataPackSubjectPackScopeSql(sb);
                sb.Append(" AND EXISTS (SELECT 1 FROM datapack_entry de WHERE de.pack_id=dl.pack_id");
                sb.Append(" AND de.entry_id=dl.entry_id AND lower(ifnull(de.subject,'')) LIKE ? ESCAPE '\\'))");
                AddDataPackSubjectPackScopeBinds(binds);
                binds.Add(esc);
            }
        }

        /// <summary>History browse: append search AST predicates (paths + creator + uid + user tags + status).</summary>
        private static void AppendGalleryHistorySearchSql(
            StringBuilder sb,
            List<string> textBinds,
            List<long> int64Binds,
            GallerySearchQuery query)
        {
            if (sb == null || query == null || query.IsEmpty) return;
            if (textBinds == null) return;
            if (query.Branches == null || query.Branches.Count == 0) return;

            string listPath = "ifnull(COALESCE(mx.list_path, mr.list_path),'')";
            string intPath = "ifnull(COALESCE(mx.internal_path, mr.internal_path),'')";

            int n = 0;
            for (int bi = 0; bi < query.Branches.Count; bi++)
            {
                if (query.Branches[bi] != null && !query.Branches[bi].IsEmpty) n++;
            }
            if (n == 0) return;

            if (n == 1)
            {
                for (int bi = 0; bi < query.Branches.Count; bi++)
                {
                    GallerySearchBranch br = query.Branches[bi];
                    if (br == null || br.IsEmpty) continue;
                    AppendGalleryHistorySearchBranchSql(sb, textBinds, br, listPath, intPath);
                    break;
                }
                return;
            }

            sb.Append(" AND (");
            bool first = true;
            for (int bi = 0; bi < query.Branches.Count; bi++)
            {
                GallerySearchBranch br = query.Branches[bi];
                if (br == null || br.IsEmpty) continue;
                if (!first) sb.Append(" OR ");
                first = false;
                sb.Append("(1=1");
                AppendGalleryHistorySearchBranchSql(sb, textBinds, br, listPath, intPath);
                sb.Append(')');
            }
            sb.Append(')');
        }

        private static void AppendGalleryHistorySearchBranchSql(
            StringBuilder sb,
            List<string> textBinds,
            GallerySearchBranch br,
            string listPath,
            string intPath)
        {
            if (sb == null || textBinds == null || br == null) return;

            if (br.BroadTerms != null)
            {
                for (int i = 0; i < br.BroadTerms.Count; i++)
                {
                    string t = br.BroadTerms[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    string esc = "%" + EscapeLike(t) + "%";
                    sb.Append(" AND (");
                    sb.Append(listPath).Append(" LIKE ? ESCAPE '\\'");
                    sb.Append(" OR ").Append(intPath).Append(" LIKE ? ESCAPE '\\'");
                    sb.Append(" OR ifnull(p.var_path,'') LIKE ? ESCAPE '\\'");
                    sb.Append(" OR ifnull(p.creator,'') LIKE ? ESCAPE '\\'");
                    sb.Append(" OR p.uid LIKE ? ESCAPE '\\'");
                    if (t.Length >= 2)
                    {
                        sb.Append(" OR EXISTS (SELECT 1 FROM gallery_item_user_tag gut");
                        sb.Append(" WHERE gut.pkg_uid=COALESCE(mx.pkg_uid, mr.pkg_uid)");
                        sb.Append(" AND gut.internal_path=COALESCE(mx.internal_path, mr.internal_path)");
                        sb.Append(" AND gut.tag_id IN (SELECT tag_id FROM gallery_user_tag WHERE lower(name) LIKE ? ESCAPE '\\'))");
                    }
                    bool packSubjectNeedsBind = false;
                    if (t.Length >= 2 && DataPackSubjectSearchEnabled()
                        && !TryAppendDataPackSubjectUidSet(
                            sb, "COALESCE(mx.pkg_uid, mr.pkg_uid)", t.ToLowerInvariant(), false, false, " OR ", true))
                    {
                        packSubjectNeedsBind = true;
                        sb.Append(" OR EXISTS (SELECT 1 FROM datapack_link dl WHERE dl.pkg_uid=COALESCE(mx.pkg_uid, mr.pkg_uid)");
                        AppendDataPackLinkIdentityOnlySql(sb, "dl");
                        AppendDataPackSubjectPackScopeSql(sb);
                        sb.Append(" AND EXISTS (SELECT 1 FROM datapack_entry de WHERE de.pack_id=dl.pack_id");
                        sb.Append(" AND de.entry_id=dl.entry_id AND lower(ifnull(de.subject,'')) LIKE ? ESCAPE '\\'))");
                    }
                    sb.Append(')');
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    if (t.Length >= 2) textBinds.Add(esc);
                    if (packSubjectNeedsBind)
                    {
                        AddDataPackSubjectPackScopeBinds(textBinds);
                        textBinds.Add(esc);
                    }
                }
            }
            if (br.BroadExclude != null)
            {
                for (int i = 0; i < br.BroadExclude.Count; i++)
                {
                    string t = br.BroadExclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    string esc = "%" + EscapeLike(t) + "%";
                    sb.Append(" AND NOT (");
                    sb.Append(listPath).Append(" LIKE ? ESCAPE '\\'");
                    sb.Append(" OR ").Append(intPath).Append(" LIKE ? ESCAPE '\\'");
                    sb.Append(" OR ifnull(p.var_path,'') LIKE ? ESCAPE '\\'");
                    sb.Append(" OR ifnull(p.creator,'') LIKE ? ESCAPE '\\'");
                    sb.Append(" OR p.uid LIKE ? ESCAPE '\\'");
                    if (t.Length >= 2)
                    {
                        sb.Append(" OR EXISTS (SELECT 1 FROM gallery_item_user_tag gut");
                        sb.Append(" WHERE gut.pkg_uid=COALESCE(mx.pkg_uid, mr.pkg_uid)");
                        sb.Append(" AND gut.internal_path=COALESCE(mx.internal_path, mr.internal_path)");
                        sb.Append(" AND gut.tag_id IN (SELECT tag_id FROM gallery_user_tag WHERE lower(name) LIKE ? ESCAPE '\\'))");
                    }
                    bool packSubjectNeedsBind = false;
                    if (t.Length >= 2 && DataPackSubjectSearchEnabled()
                        && !TryAppendDataPackSubjectUidSet(
                            sb, "COALESCE(mx.pkg_uid, mr.pkg_uid)", t.ToLowerInvariant(), false, false, " OR ", true))
                    {
                        packSubjectNeedsBind = true;
                        sb.Append(" OR EXISTS (SELECT 1 FROM datapack_link dl WHERE dl.pkg_uid=COALESCE(mx.pkg_uid, mr.pkg_uid)");
                        AppendDataPackLinkIdentityOnlySql(sb, "dl");
                        AppendDataPackSubjectPackScopeSql(sb);
                        sb.Append(" AND EXISTS (SELECT 1 FROM datapack_entry de WHERE de.pack_id=dl.pack_id");
                        sb.Append(" AND de.entry_id=dl.entry_id AND lower(ifnull(de.subject,'')) LIKE ? ESCAPE '\\'))");
                    }
                    sb.Append(')');
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    textBinds.Add(esc);
                    if (t.Length >= 2) textBinds.Add(esc);
                    if (packSubjectNeedsBind)
                    {
                        AddDataPackSubjectPackScopeBinds(textBinds);
                        textBinds.Add(esc);
                    }
                }
            }

            if (br.TagInclude != null)
            {
                for (int i = 0; i < br.TagInclude.Count; i++)
                {
                    string t = br.TagInclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append(" AND EXISTS (SELECT 1 FROM gallery_item_user_tag gut");
                    sb.Append(" WHERE gut.pkg_uid=p.uid");
                    sb.Append(" AND gut.internal_path=").Append(GalleryHistoryResolvedInternalPathSql());
                    sb.Append(" AND gut.tag_id IN (SELECT tag_id FROM gallery_user_tag WHERE lower(name) LIKE ? ESCAPE '\\')");
                    sb.Append(')');
                    textBinds.Add("%" + EscapeLike(t) + "%");
                }
            }
            if (br.TagExclude != null)
            {
                for (int i = 0; i < br.TagExclude.Count; i++)
                {
                    string t = br.TagExclude[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append(" AND NOT EXISTS (SELECT 1 FROM gallery_item_user_tag gut");
                    sb.Append(" WHERE gut.pkg_uid=p.uid");
                    sb.Append(" AND gut.internal_path=").Append(GalleryHistoryResolvedInternalPathSql());
                    sb.Append(" AND gut.tag_id IN (SELECT tag_id FROM gallery_user_tag WHERE lower(name) LIKE ? ESCAPE '\\')");
                    sb.Append(')');
                    textBinds.Add("%" + EscapeLike(t) + "%");
                }
            }

            AppendDataPackTerms(sb, textBinds, br.PackSubjectInclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.Subject, false);
            AppendDataPackTerms(sb, textBinds, br.PackSubjectExclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.Subject, true);
            AppendDataPackTerms(sb, textBinds, br.PackHubTagInclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.HubTag, false);
            AppendDataPackTerms(sb, textBinds, br.PackHubTagExclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.HubTag, true);
            AppendDataPackTerms(sb, textBinds, br.PackHubCatInclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.HubCategory, false);
            AppendDataPackTerms(sb, textBinds, br.PackHubCatExclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.HubCategory, true);
            AppendDataPackTerms(sb, textBinds, br.PackAnyInclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.Any, false);
            AppendDataPackTerms(sb, textBinds, br.PackAnyExclude, "COALESCE(mx.pkg_uid, mr.pkg_uid)", GallerySearchQuery.DataPackAtomKind.Any, true);
            if (br.CreatorTerms != null)
            {
                for (int i = 0; i < br.CreatorTerms.Count; i++)
                {
                    string t = br.CreatorTerms[i];
                    if (string.IsNullOrEmpty(t)) continue;
                    sb.Append(" AND ifnull(p.creator,'') LIKE ? ESCAPE '\\'");
                    textBinds.Add("%" + EscapeLike(t) + "%");
                }
            }

            if (br.HasFlag(GallerySearchQuery.StatusFlags.Loaded))
                sb.Append(" AND ifnull(p.loaded,0) != 0");
            else if (br.HasFlag(GallerySearchQuery.StatusFlags.Unloaded))
                sb.Append(" AND ifnull(p.loaded,0) = 0");

            if (br.HasFlag(GallerySearchQuery.StatusFlags.Tagged))
            {
                sb.Append(" AND EXISTS (SELECT 1 FROM gallery_item_user_tag gut WHERE gut.pkg_uid=p.uid");
                sb.Append(" AND gut.internal_path=").Append(GalleryHistoryResolvedInternalPathSql()).Append(')');
            }
            else if (br.HasFlag(GallerySearchQuery.StatusFlags.Untagged))
            {
                sb.Append(" AND NOT EXISTS (SELECT 1 FROM gallery_item_user_tag gut WHERE gut.pkg_uid=p.uid");
                sb.Append(" AND gut.internal_path=").Append(GalleryHistoryResolvedInternalPathSql()).Append(')');
            }
        }
    }
}
