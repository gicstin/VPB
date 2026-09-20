using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        private const char InsightListSeparator = '\n';

        internal static void EnsurePackageInsightSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS pkg_insight (" +
                "uid TEXT PRIMARY KEY," +
                "var_size INTEGER NOT NULL," +
                "var_mtime INTEGER NOT NULL," +
                "scan_ver INTEGER NOT NULL," +
                "issue_flags INTEGER NOT NULL DEFAULT 0," +
                "risk_flags INTEGER NOT NULL DEFAULT 0," +
                "morph_count INTEGER NOT NULL DEFAULT 0," +
                "script_count INTEGER NOT NULL DEFAULT 0," +
                "dll_count INTEGER NOT NULL DEFAULT 0," +
                "bundle_count INTEGER NOT NULL DEFAULT 0," +
                "undeclared TEXT," +
                "detail TEXT);" +
                "CREATE TABLE IF NOT EXISTS pkg_insight_review (" +
                "uid TEXT PRIMARY KEY," +
                "sig TEXT NOT NULL," +
                "ts INTEGER NOT NULL);");
        }

        internal static bool TryLoadPackageInsights(
            Dictionary<string, PackageInsightRecord> into,
            Dictionary<string, string> reviewsInto)
        {
            if (into == null) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageInsightSchema(conn);

                    using (var st = conn.Prepare(
                        "SELECT uid, var_size, var_mtime, scan_ver, issue_flags, risk_flags, " +
                        "morph_count, script_count, dll_count, bundle_count, undeclared, detail FROM pkg_insight"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string uid = st.ColumnText(0);
                            if (string.IsNullOrEmpty(uid)) continue;
                            var rec = new PackageInsightRecord
                            {
                                Uid = uid,
                                VarSize = st.ColumnInt64(1),
                                VarMtimeTicks = st.ColumnInt64(2),
                                ScanVersion = (int)st.ColumnInt64(3),
                                Issues = (PkgIssueFlags)(int)st.ColumnInt64(4),
                                Risk = (PkgRiskFlags)(int)st.ColumnInt64(5),
                                MorphCount = (int)st.ColumnInt64(6),
                                ScriptCount = (int)st.ColumnInt64(7),
                                DllCount = (int)st.ColumnInt64(8),
                                AssetBundleCount = (int)st.ColumnInt64(9),
                                UndeclaredDeps = SplitInsightList(st.ColumnText(10)),
                                Details = SplitInsightList(st.ColumnText(11))
                            };
                            into[uid] = rec;
                        }
                    }

                    if (reviewsInto != null)
                    {
                        using (var st = conn.Prepare("SELECT uid, sig FROM pkg_insight_review"))
                        {
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                if (string.IsNullOrEmpty(uid)) continue;
                                reviewsInto[uid] = st.ColumnText(1) ?? "";
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] insight load failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TrySavePackageInsights(List<PackageInsightRecord> batch)
        {
            if (batch == null || batch.Count == 0) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageInsightSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var st = conn.Prepare(
                            "INSERT OR REPLACE INTO pkg_insight(uid,var_size,var_mtime,scan_ver,issue_flags,risk_flags," +
                            "morph_count,script_count,dll_count,bundle_count,undeclared,detail) " +
                            "VALUES(?,?,?,?,?,?,?,?,?,?,?,?)"))
                        {
                            for (int i = 0; i < batch.Count; i++)
                            {
                                PackageInsightRecord r = batch[i];
                                if (r == null || string.IsNullOrEmpty(r.Uid)) continue;
                                st.BindText(1, r.Uid);
                                st.BindInt64(2, r.VarSize);
                                st.BindInt64(3, r.VarMtimeTicks);
                                st.BindInt64(4, r.ScanVersion);
                                st.BindInt64(5, (int)r.Issues);
                                st.BindInt64(6, (int)r.Risk);
                                st.BindInt64(7, r.MorphCount);
                                st.BindInt64(8, r.ScriptCount);
                                st.BindInt64(9, r.DllCount);
                                st.BindInt64(10, r.AssetBundleCount);
                                st.BindText(11, JoinInsightList(r.UndeclaredDeps));
                                st.BindText(12, JoinInsightList(r.Details));
                                st.Step();
                                st.Reset();
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
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] insight save failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TrySaveInsightReview(string uid, string signature)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageInsightSchema(conn);
                    if (signature == null)
                    {
                        using (var st = conn.Prepare("DELETE FROM pkg_insight_review WHERE uid = ?"))
                        {
                            st.BindText(1, uid);
                            st.Step();
                        }
                    }
                    else
                    {
                        using (var st = conn.Prepare("INSERT OR REPLACE INTO pkg_insight_review(uid,sig,ts) VALUES(?,?,?)"))
                        {
                            st.BindText(1, uid);
                            st.BindText(2, signature);
                            st.BindInt64(3, DateTime.UtcNow.ToBinary());
                            st.Step();
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] insight review save failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TryClearPackageInsights()
        {
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageInsightSchema(conn);
                    conn.ExecUtf8("DELETE FROM pkg_insight;");
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] insight clear failed: " + ex.Message); } catch { }
                return false;
            }
        }

        private static string JoinInsightList(string[] values)
        {
            if (values == null || values.Length == 0) return "";
            var sb = new StringBuilder(values.Length * 32);
            for (int i = 0; i < values.Length; i++)
            {
                string v = values[i];
                if (string.IsNullOrEmpty(v)) continue;
                if (sb.Length > 0) sb.Append(InsightListSeparator);
                sb.Append(v);
            }
            return sb.ToString();
        }

        private static string[] SplitInsightList(string packed)
        {
            if (string.IsNullOrEmpty(packed)) return new string[0];
            return packed.Split(new[] { InsightListSeparator }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
