using System;
using System.Collections.Generic;
using System.Text;

namespace VPB
{
    internal struct ContentFileHit
    {
        internal string PackageUid;
        internal string InternalPath;
        internal long Size;
    }

    internal struct DuplicateAssetRow
    {
        internal string InternalPath;
        internal int PackageCount;
        internal long Size;
        internal long RedundantBytes;
    }

    internal static partial class VpbLocalDatabase
    {
        private const string MetaContentPathIndexKey = "pkg_file_path_index_v1";

        private static readonly string[] DuplicateScanIgnoredPaths =
        {
            "meta.json"
        };

        private static void EnsureContentPathIndex(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            try
            {
                if (!string.IsNullOrEmpty(MetaGet(conn, MetaContentPathIndexKey))) return;
                conn.ExecUtf8("CREATE INDEX IF NOT EXISTS idx_pkg_file_path ON pkg_file(internal_path);");
                MetaSet(conn, MetaContentPathIndexKey, "1");
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] content path index failed: " + ex.Message); } catch { }
            }
        }

        internal static bool TryGetContentIndexFileCount(out long fileCount, out long packageCount)
        {
            fileCount = 0;
            packageCount = 0;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageManifestSchema(conn);
                    using (var st = conn.Prepare("SELECT COUNT(*), COUNT(DISTINCT pkg_uid) FROM pkg_file"))
                    {
                        if (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            fileCount = st.ColumnInt64(0);
                            packageCount = st.ColumnInt64(1);
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

        internal static bool TrySearchPackageFileOwners(string term, HashSet<string> into)
        {
            if (into == null || string.IsNullOrEmpty(term) || !VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageManifestSchema(conn);
                    using (var st = conn.Prepare(
                        "SELECT DISTINCT pkg_uid FROM pkg_file WHERE internal_path LIKE ? ESCAPE '\\'"))
                    {
                        st.BindText(1, "%" + EscapeLikeTerm(term) + "%");
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string uid = st.ColumnText(0);
                            if (!string.IsNullOrEmpty(uid)) into.Add(uid);
                        }
                    }
                }
                return true;
            }
            catch
            {
                into.Clear();
                return false;
            }
        }

        internal static bool TrySearchPackageFiles(string term, int limit, List<ContentFileHit> into, bool allowIndexBuild = false)
        {
            if (into == null) return false;
            if (string.IsNullOrEmpty(term)) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            if (limit <= 0) limit = 200;

            string like = "%" + EscapeLikeTerm(term) + "%";
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageManifestSchema(conn);
                    if (allowIndexBuild) EnsureContentPathIndex(conn);

                    using (var st = conn.Prepare(
                        "SELECT pkg_uid, internal_path, size FROM pkg_file " +
                        "WHERE internal_path LIKE ? ESCAPE '\\' " +
                        "ORDER BY internal_path LIMIT ?"))
                    {
                        st.BindText(1, like);
                        st.BindInt64(2, limit);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            var hit = new ContentFileHit
                            {
                                PackageUid = st.ColumnText(0) ?? "",
                                InternalPath = st.ColumnText(1) ?? "",
                                Size = st.ColumnInt64(2)
                            };
                            if (hit.InternalPath.Length == 0) continue;
                            into.Add(hit);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] content search failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TryGetPackagesForInternalPath(string internalPath, int limit, List<string> into)
        {
            if (into == null || string.IsNullOrEmpty(internalPath)) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            if (limit <= 0) limit = 200;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageManifestSchema(conn);
                    EnsureContentPathIndex(conn);
                    using (var st = conn.Prepare(
                        "SELECT DISTINCT pkg_uid FROM pkg_file WHERE internal_path = ? ORDER BY pkg_uid LIMIT ?"))
                    {
                        st.BindText(1, internalPath);
                        st.BindInt64(2, limit);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string uid = st.ColumnText(0);
                            if (!string.IsNullOrEmpty(uid)) into.Add(uid);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] path package lookup failed: " + ex.Message); } catch { }
                return false;
            }
        }
        
        internal static bool TryFindDuplicateAssets(int minPackages, long minBytes, int limit, List<DuplicateAssetRow> into)
        {
            if (into == null) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            if (minPackages < 2) minPackages = 2;
            if (limit <= 0) limit = 200;
            if (minBytes < 0) minBytes = 0;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageManifestSchema(conn);
                    EnsureContentPathIndex(conn);

                    var sb = new StringBuilder(320);
                    sb.Append("SELECT internal_path, COUNT(DISTINCT pkg_uid) AS n, MAX(size) AS sz FROM pkg_file ");
                    sb.Append("WHERE size >= ? ");
                    for (int i = 0; i < DuplicateScanIgnoredPaths.Length; i++)
                        sb.Append("AND internal_path <> ? ");
                    sb.Append("GROUP BY internal_path HAVING n >= ? ");
                    sb.Append("ORDER BY (n - 1) * sz DESC LIMIT ?");

                    using (var st = conn.Prepare(sb.ToString()))
                    {
                        int p = 1;
                        st.BindInt64(p++, minBytes);
                        for (int i = 0; i < DuplicateScanIgnoredPaths.Length; i++)
                            st.BindText(p++, DuplicateScanIgnoredPaths[i]);
                        st.BindInt64(p++, minPackages);
                        st.BindInt64(p, limit);

                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string path = st.ColumnText(0) ?? "";
                            if (path.Length == 0) continue;
                            int n = (int)st.ColumnInt64(1);
                            long sz = st.ColumnInt64(2);
                            into.Add(new DuplicateAssetRow
                            {
                                InternalPath = path,
                                PackageCount = n,
                                Size = sz,
                                RedundantBytes = sz * (n - 1)
                            });
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.DB] duplicate asset scan failed: " + ex.Message); } catch { }
                return false;
            }
        }
        private static string EscapeLikeTerm(string term)
        {
            var sb = new StringBuilder(term.Length + 8);
            for (int i = 0; i < term.Length; i++)
            {
                char c = term[i];
                if (c == '%' || c == '_' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
