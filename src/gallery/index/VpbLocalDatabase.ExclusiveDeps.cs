using System;
using System.Collections.Generic;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        /// <summary>
        /// Full <c>pkg_dep</c> edge map for exclusive-dep scan. No gallery-index freshness gate —
        /// user clicked; answer from whatever SQL currently has. Bulk cache first when ready.
        /// </summary>
        internal static bool TryLoadPackageDepEdgesUngated(Dictionary<string, List<string>> dest)
        {
            if (dest == null) return false;
            dest.Clear();
            if (!VpbSqlite3.IsAvailable) return false;

            if (TryCopyBulkPackageDepEdges(dest))
                return dest.Count > 0;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                using (var st = conn.Prepare("SELECT src_uid, dep_uid FROM pkg_dep"))
                {
                    int step;
                    while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                    {
                        string src = st.ColumnText(0);
                        string dep = st.ColumnText(1);
                        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dep)) continue;
                        List<string> list;
                        if (!dest.TryGetValue(src, out list) || list == null)
                        {
                            list = new List<string>(4);
                            dest[src] = list;
                        }
                        list.Add(dep);
                    }
                }
                return dest.Count > 0;
            }
            catch
            {
                dest.Clear();
                return false;
            }
        }

        static bool TryCopyBulkPackageDepEdges(Dictionary<string, List<string>> dest)
        {
            if (!TryEnsurePackageDependencyBulkCache()) return false;
            lock (s_BulkDepLock)
            {
                if (s_BulkDepBySrc == null || s_BulkDepBySrc.Count == 0) return false;
                foreach (KeyValuePair<string, List<string>> kv in s_BulkDepBySrc)
                {
                    List<string> srcList = kv.Value;
                    if (srcList == null || srcList.Count == 0) continue;
                    var copy = new List<string>(srcList.Count);
                    for (int i = 0; i < srcList.Count; i++)
                    {
                        string d = srcList[i];
                        if (!string.IsNullOrEmpty(d)) copy.Add(d);
                    }
                    if (copy.Count > 0)
                        dest[kv.Key] = copy;
                }
            }
            return dest.Count > 0;
        }
    }
}
