using System;

namespace VPB
{
    /// <summary>Scene uid → loadable .json path for MP invite. LoadedSceneName has no extension.</summary>
    internal static partial class VpbLocalDatabase
    {
        /// <summary>Leaf → full .json path. packageUid narrows; false on zero or ambiguous matches.</summary>
        internal static bool TryResolveSceneJsonPath(string packageUid, string leafName, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(leafName)) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            string suffix = "%/" + EscapeLike(leafName) + ".json";

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    if (!string.IsNullOrEmpty(packageUid))
                        return TryResolvePackageScene(conn, packageUid, leafName, suffix, out path);

                    return TryResolveLooseScene(conn, suffix, out path);
                }
            }
            catch
            {
                path = null;
                return false;
            }
        }

        private static bool TryResolvePackageScene(VpbSqlite3.Connection conn, string packageUid,
            string leafName, string suffix, out string path)
        {
            path = null;

            using (var st = conn.Prepare(
                "SELECT internal_path FROM cat_mem WHERE category = 'Scenes' AND pkg_uid = ? " +
                "AND internal_path LIKE ? ESCAPE '\\' LIMIT 2"))
            {
                st.BindText(1, packageUid);
                st.BindText(2, suffix);

                string first = null;
                int hits = 0;
                while (st.Step() == VpbSqlite3.SqliteRow)
                {
                    string ip = st.ColumnText(0);
                    if (string.IsNullOrEmpty(ip)) continue;
                    if (first == null) first = ip;
                    hits++;
                }

                // Same leaf twice in one package: uid has no subfolder, so refuse rather than guess.
                if (hits != 1 || first == null) return false;

                path = Qualify(packageUid, first);
                return path != null;
            }
        }

        private static bool TryResolveLooseScene(VpbSqlite3.Connection conn, string suffix, out string path)
        {
            path = null;

            using (var st = conn.Prepare(
                "SELECT DISTINCT path FROM sys_file WHERE path LIKE ? ESCAPE '\\' LIMIT 16"))
            {
                st.BindText(1, suffix);

                string first = null;
                int hits = 0;
                while (st.Step() == VpbSqlite3.SqliteRow)
                {
                    string p = st.ColumnText(0);
                    if (string.IsNullOrEmpty(p)) continue;

                    // sys_file caches all loose files; only Saves/scene .json rows are launchable.
                    if (!LocalSceneGallerySupport.IsVaMLocalSceneListingCandidate(p)) continue;

                    string norm;
                    try { norm = FileManager.NormalizePath(p); }
                    catch { norm = p.Replace('\\', '/'); }
                    if (string.IsNullOrEmpty(norm)) continue;

                    if (first == null) { first = norm; hits = 1; continue; }
                    if (!string.Equals(first, norm, StringComparison.OrdinalIgnoreCase)) hits++;
                }

                if (hits != 1 || first == null) return false;

                path = first;
                return true;
            }
        }

        private static string Qualify(string packageUid, string internalPath)
        {
            string ip = internalPath.Replace('\\', '/');
            if (ip.IndexOf(":/", StringComparison.Ordinal) > 0) return ip;
            if (ip.Length > 0 && ip[0] == '/') ip = ip.Substring(1);
            return ip.Length > 0 ? packageUid + ":/" + ip : null;
        }
    }
}
