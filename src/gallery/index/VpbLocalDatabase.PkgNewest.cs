using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    /// <summary>
    /// Library-global newest-version flags on <c>pkg</c> for gallery SQL filters.
    /// Family = uid prefix before last <c>.</c> (Creator.Package, package name may contain dots);
    /// <c>is_newest=1</c> for max <c>ver</c> per family and for unparseable uids (always keep when hiding old).
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        /// <summary>No package-version SQL filter.</summary>
        internal const int PkgVersionFilterOff = 0;
        /// <summary>Keep only library-newest package per family (<c>is_newest!=0</c>).</summary>
        internal const int PkgVersionFilterNewestOnly = 1;
        /// <summary>Keep only non-newest parseable packages (<c>is_newest=0</c>).</summary>
        internal const int PkgVersionFilterOldOnly = 2;

        const string PkgNewestBackfillMetaKey = "pkg_newest_flags_v1";

        /// <summary>
        /// Parse <c>Creator.Package.N</c> (N integer). Family lowercased for SQL GROUP BY stability.
        /// </summary>
        internal static bool TryParsePkgUidFamilyVer(string uid, out string familyLower, out int ver)
        {
            familyLower = "";
            ver = -1;
            if (string.IsNullOrEmpty(uid)) return false;
            int lastDot = uid.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= uid.Length - 1) return false;
            if (!int.TryParse(uid.Substring(lastDot + 1), out ver) || ver < 0) return false;
            string fam = uid.Substring(0, lastDot);
            if (fam.Length == 0) return false;
            familyLower = fam.ToLowerInvariant();
            return true;
        }

        static void ResolvePkgFamilyVerForInsert(VarPackage pkg, string uid, out string familyLower, out int ver)
        {
            familyLower = "";
            ver = -1;
            if (pkg != null)
            {
                try
                {
                    string cr = pkg.Creator ?? "";
                    string name = pkg.Name ?? "";
                    int pv = pkg.Version;
                    if (cr.Length > 0 && name.Length > 0 && pv >= 0)
                    {
                        familyLower = (cr + "." + name).ToLowerInvariant();
                        ver = pv;
                        return;
                    }
                }
                catch { }
            }
            TryParsePkgUidFamilyVer(uid, out familyLower, out ver);
        }

        /// <summary>Appends AND clause; no bind placeholders.</summary>
        internal static void AppendPkgVersionFilterSql(StringBuilder sb, string pkgAlias, int filterMode)
        {
            if (sb == null || filterMode == PkgVersionFilterOff) return;
            string a = string.IsNullOrEmpty(pkgAlias) ? "p" : pkgAlias;
            if (filterMode == PkgVersionFilterNewestOnly)
                sb.Append(" AND ifnull(").Append(a).Append(".is_newest,1) != 0");
            else if (filterMode == PkgVersionFilterOldOnly)
                sb.Append(" AND ifnull(").Append(a).Append(".is_newest,1) = 0");
        }

        internal static string BuildPkgVersionFilterFragment(string pkgAlias, int filterMode)
        {
            if (filterMode == PkgVersionFilterOff) return "";
            var sb = new StringBuilder(48);
            AppendPkgVersionFilterSql(sb, pkgAlias, filterMode);
            return sb.ToString();
        }

        static void EnsurePkgNewestSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN family TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN ver INTEGER;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN is_newest INTEGER;");
            try
            {
                conn.ExecUtf8(
                    "CREATE INDEX IF NOT EXISTS idx_pkg_family_ver ON pkg(family, ver);" +
                    "CREATE INDEX IF NOT EXISTS idx_pkg_is_newest ON pkg(is_newest);");
            }
            catch { }

            if (!string.IsNullOrEmpty(MetaGet(conn, PkgNewestBackfillMetaKey))) return;

            try
            {
                conn.ExecUtf8("BEGIN IMMEDIATE;");
                BackfillPkgFamilyVerRows(conn);
                RefreshAllPkgNewestFlags(conn);
                MetaSet(conn, PkgNewestBackfillMetaKey, "1");
                conn.ExecUtf8("COMMIT;");
                try { LogUtil.Log("[VPB.DB] pkg family/ver/is_newest backfill OK"); } catch { }
            }
            catch (Exception ex)
            {
                try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                try { LogUtil.LogWarning("[VPB.DB] pkg newest backfill failed (retry next open): " + ex.Message); } catch { }
            }
        }

        static void BackfillPkgFamilyVerRows(VpbSqlite3.Connection conn)
        {
            var updates = new List<KeyValuePair<string, KeyValuePair<string, int>>>(256);
            using (var sel = conn.Prepare("SELECT uid FROM pkg"))
            {
                while (sel.Step() == VpbSqlite3.SqliteRow)
                {
                    string uid = sel.ColumnText(0) ?? "";
                    if (uid.Length == 0) continue;
                    string fam;
                    int ver;
                    if (!TryParsePkgUidFamilyVer(uid, out fam, out ver))
                    {
                        fam = "";
                        ver = -1;
                    }
                    updates.Add(new KeyValuePair<string, KeyValuePair<string, int>>(
                        uid, new KeyValuePair<string, int>(fam, ver)));
                }
            }

            using (var up = conn.Prepare("UPDATE pkg SET family=?, ver=? WHERE uid=?"))
            {
                for (int i = 0; i < updates.Count; i++)
                {
                    var row = updates[i];
                    up.BindText(1, row.Value.Key ?? "");
                    up.BindInt64(2, row.Value.Value);
                    up.BindText(3, row.Key);
                    up.Step();
                    up.Reset();
                }
            }
        }

        /// <summary>
        /// Recompute <c>is_newest</c> for entire <c>pkg</c> table. Call after full rebuild / incremental / backfill.
        /// Unparseable rows (empty family or ver&lt;0) stay newest so Hide-old never drops them.
        /// </summary>
        static void RefreshAllPkgNewestFlags(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8("UPDATE pkg SET is_newest=0;");
            conn.ExecUtf8(
                "UPDATE pkg SET is_newest=1 WHERE ifnull(family,'')='' OR ifnull(ver,-1)<0;");
            conn.ExecUtf8(
                "UPDATE pkg SET is_newest=1 WHERE rowid IN (" +
                "SELECT p.rowid FROM pkg p INNER JOIN (" +
                "SELECT family AS f, MAX(ver) AS mv FROM pkg " +
                "WHERE ifnull(family,'')!='' AND ifnull(ver,-1)>=0 GROUP BY family" +
                ") t ON p.family=t.f AND p.ver=t.mv)");
        }

        static bool PkgHasIsNewestColumn(VpbSqlite3.Connection conn)
        {
            if (conn == null) return false;
            using (var st = conn.Prepare("SELECT 1 FROM pragma_table_info('pkg') WHERE name='is_newest' LIMIT 1;"))
            {
                return st.Step() == VpbSqlite3.SqliteRow;
            }
        }
    }
}
