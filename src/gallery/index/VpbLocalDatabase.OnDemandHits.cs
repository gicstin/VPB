using System;
using System.Collections.Generic;

namespace VPB
{
    internal static partial class VpbLocalDatabase
    {
        private static void EnsureOnDemandHitTable(VpbSqlite3.Connection conn)
        {
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS ondemand_hit (" +
                "uid TEXT PRIMARY KEY, count INTEGER NOT NULL DEFAULT 0, last_utc INTEGER NOT NULL);");
        }

        internal static void RecordOnDemandHit(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (!VpbSqlite3.IsAvailable) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    long now = DateTime.UtcNow.ToBinary();
                    using (var st = conn.Prepare(
                        "INSERT INTO ondemand_hit(uid, count, last_utc) VALUES(?, 1, ?) " +
                        "ON CONFLICT(uid) DO UPDATE SET count = count + 1, last_utc = excluded.last_utc"))
                    {
                        st.BindText(1, uid);
                        st.BindInt64(2, now);
                        st.Step();
                    }
                }
            }
            catch { }
        }

        internal static bool TryReadOnDemandHits(Dictionary<string, int> counts)
        {
            if (counts == null) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT uid, count FROM ondemand_hit"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string uid = st.ColumnText(0);
                            int c = (int)st.ColumnInt64(1);
                            if (!string.IsNullOrEmpty(uid))
                                counts[uid] = c;
                        }
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }
}
