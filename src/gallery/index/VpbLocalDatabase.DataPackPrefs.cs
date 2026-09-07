using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        internal sealed class DataPackTagPrefSnapshot
        {
            internal HashSet<string> Global;
            internal Dictionary<string, HashSet<string>> ByPackage;
            internal int GlobalCount;
            internal int PackageRuleCount;

            internal bool Any { get { return GlobalCount > 0 || PackageRuleCount > 0; } }
        }

        const int DataPackTagPrefInlineMax = 64;

        static readonly object s_TagPrefSync = new object();
        static volatile DataPackTagPrefSnapshot s_TagPrefs;

        static readonly DataPackTagPrefSnapshot EmptyTagPrefs = new DataPackTagPrefSnapshot
        {
            Global = new HashSet<string>(StringComparer.Ordinal),
            ByPackage = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
        };

        internal static string NormalizeDataPackTagPref(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            return tag.Trim().ToLowerInvariant();
        }

        internal static void EnsureDataPackTagPrefSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS datapack_tag_pref (" +
                "scope_uid TEXT NOT NULL, tag TEXT NOT NULL, PRIMARY KEY(scope_uid, tag));" +
                "CREATE INDEX IF NOT EXISTS idx_dp_tag_pref_tag ON datapack_tag_pref(tag);");
        }

        internal static DataPackTagPrefSnapshot GetDataPackTagPrefs()
        {
            DataPackTagPrefSnapshot snap = s_TagPrefs;
            if (snap != null) return snap;
            lock (s_TagPrefSync)
            {
                snap = s_TagPrefs;
                if (snap != null) return snap;
                snap = LoadDataPackTagPrefs();
                s_TagPrefs = snap;
                return snap;
            }
        }

        static DataPackTagPrefSnapshot LoadDataPackTagPrefs()
        {
            if (!VpbSqlite3.IsAvailable) return EmptyTagPrefs;

            var global = new HashSet<string>(StringComparer.Ordinal);
            var byPkg = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            int pkgRules = 0;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTagPrefTablePresent(conn)) return EmptyTagPrefs;
                    using (var st = conn.Prepare("SELECT scope_uid, tag FROM datapack_tag_pref"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string scope = st.ColumnText(0) ?? "";
                            string tag = st.ColumnText(1) ?? "";
                            if (tag.Length == 0) continue;
                            if (scope.Length == 0)
                            {
                                global.Add(tag);
                                continue;
                            }
                            HashSet<string> set;
                            if (!byPkg.TryGetValue(scope, out set))
                            {
                                set = new HashSet<string>(StringComparer.Ordinal);
                                byPkg[scope] = set;
                            }
                            if (set.Add(tag)) pkgRules++;
                        }
                    }
                }
            }
            catch
            {
                return EmptyTagPrefs;
            }

            if (global.Count == 0 && pkgRules == 0) return EmptyTagPrefs;
            return new DataPackTagPrefSnapshot
            {
                Global = global,
                ByPackage = byPkg,
                GlobalCount = global.Count,
                PackageRuleCount = pkgRules,
            };
        }

        static bool DataPackTagPrefTablePresent(VpbSqlite3.Connection conn)
        {
            if (conn == null) return false;
            try
            {
                using (var st = conn.Prepare(
                    "SELECT 1 FROM sqlite_master WHERE type='table' AND name='datapack_tag_pref' LIMIT 1"))
                {
                    return st.Step() == VpbSqlite3.SqliteRow;
                }
            }
            catch { return false; }
        }

        internal static bool DataPackTagPrefsActive()
        {
            return GetDataPackTagPrefs().Any;
        }

        internal static void DataPackHiddenTagCounts(out int globalCount, out int packageRuleCount)
        {
            DataPackTagPrefSnapshot snap = GetDataPackTagPrefs();
            globalCount = snap.GlobalCount;
            packageRuleCount = snap.PackageRuleCount;
        }

        internal static void InvalidateDataPackTagPrefs()
        {
            lock (s_TagPrefSync) { s_TagPrefs = null; }
            InvalidateDataPackLookOverlayCache();
            try { VpbDataPackService.BumpStatusRevision(); } catch { }
        }

        internal static bool IsDataPackTagHidden(string pkgUid, string tag)
        {
            string t = NormalizeDataPackTagPref(tag);
            if (t.Length == 0) return false;
            DataPackTagPrefSnapshot snap = GetDataPackTagPrefs();
            if (!snap.Any) return false;
            if (snap.Global.Contains(t)) return true;
            if (string.IsNullOrEmpty(pkgUid)) return false;
            HashSet<string> set;
            return snap.ByPackage.TryGetValue(pkgUid, out set) && set.Contains(t);
        }

        internal static bool IsDataPackTagHiddenForPackage(string pkgUid, string tag)
        {
            if (string.IsNullOrEmpty(pkgUid)) return false;
            string t = NormalizeDataPackTagPref(tag);
            if (t.Length == 0) return false;
            DataPackTagPrefSnapshot snap = GetDataPackTagPrefs();
            if (snap.PackageRuleCount == 0) return false;
            HashSet<string> set;
            return snap.ByPackage.TryGetValue(pkgUid, out set) && set.Contains(t);
        }

        internal static bool IsDataPackTagHiddenGlobally(string tag)
        {
            string t = NormalizeDataPackTagPref(tag);
            if (t.Length == 0) return false;
            DataPackTagPrefSnapshot snap = GetDataPackTagPrefs();
            return snap.GlobalCount > 0 && snap.Global.Contains(t);
        }

        internal static bool SetDataPackTagHidden(string pkgUid, string tag, bool hidden)
        {
            string t = NormalizeDataPackTagPref(tag);
            if (t.Length == 0) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            string scope = pkgUid ?? "";

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureDataPackTagPrefSchema(conn);
                    using (var st = conn.Prepare(hidden
                        ? "INSERT OR IGNORE INTO datapack_tag_pref(scope_uid,tag) VALUES(?,?)"
                        : "DELETE FROM datapack_tag_pref WHERE scope_uid=? AND tag=?"))
                    {
                        st.BindText(1, scope);
                        st.BindText(2, t);
                        st.Step();
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] hub tag pref write failed: " + ex.Message); } catch { }
                return false;
            }

            InvalidateDataPackTagPrefs();
            return true;
        }

        internal static bool ClearDataPackTagPrefs(bool globalOnly)
        {
            if (!VpbSqlite3.IsAvailable) return false;
            if (!GetDataPackTagPrefs().Any) return true;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (!DataPackTagPrefTablePresent(conn)) return true;
                    conn.ExecUtf8(globalOnly
                        ? "DELETE FROM datapack_tag_pref WHERE scope_uid='';"
                        : "DELETE FROM datapack_tag_pref;");
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] hub tag pref clear failed: " + ex.Message); } catch { }
                return false;
            }

            InvalidateDataPackTagPrefs();
            return true;
        }

        internal static void AppendDataPackTagNotHiddenSql(StringBuilder sb, string tagSql, string uidSql)
        {
            if (sb == null) return;
            int mark = sb.Length;
            sb.Append(" AND ");
            if (!AppendDataPackTagNotHiddenPredicate(sb, tagSql, uidSql)) sb.Length = mark;
        }

        internal static bool AppendDataPackTagNotHiddenPredicate(StringBuilder sb, string tagSql, string uidSql)
        {
            if (sb == null || string.IsNullOrEmpty(tagSql)) return false;
            DataPackTagPrefSnapshot snap = GetDataPackTagPrefs();
            if (!snap.Any) return false;

            if (snap.PackageRuleCount == 0 && snap.GlobalCount <= DataPackTagPrefInlineMax)
            {
                sb.Append("lower(trim(ifnull(").Append(tagSql).Append(",''))) NOT IN (");
                bool first = true;
                foreach (string t in snap.Global)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('\'').Append(t.Replace("'", "''")).Append('\'');
                }
                sb.Append(')');
                return true;
            }

            sb.Append("NOT EXISTS (SELECT 1 FROM datapack_tag_pref tp WHERE tp.tag=lower(trim(ifnull(")
              .Append(tagSql).Append(",''))) AND (tp.scope_uid=''");
            if (!string.IsNullOrEmpty(uidSql))
                sb.Append(" OR tp.scope_uid=").Append(uidSql);
            sb.Append("))");
            return true;
        }
    }
}
