using System;
using System.Collections.Generic;

namespace VPB
{
    internal static class VpbUidWhitespaceIdentityRepair
    {
        internal struct Candidate
        {
            internal readonly string StoredUid;
            internal readonly string ExactUid;
            internal readonly long FirstScanned;

            internal Candidate(string storedUid, string exactUid, long firstScanned)
            {
                StoredUid = storedUid;
                ExactUid = exactUid;
                FirstScanned = firstScanned;
            }
        }

        internal static void EnsureSchema(VpbSqlite3.Connection conn)
        {
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS pkg_uid_first_scanned_repair (" +
                "uid TEXT PRIMARY KEY," +
                "first_scanned INTEGER NOT NULL);");
        }

        internal static int Apply(VpbSqlite3.Connection conn, IList<Candidate> candidates)
        {
            if (conn == null || candidates == null || candidates.Count == 0) return 0;
            EnsureSchema(conn);

            int repaired = 0;
            conn.ExecUtf8("BEGIN IMMEDIATE;");
            try
            {
                using (var saveDate = conn.Prepare("INSERT OR REPLACE INTO pkg_uid_first_scanned_repair(uid,first_scanned) VALUES(?,?)"))
                using (var delMem = conn.Prepare("DELETE FROM cat_mem WHERE pkg_uid=?"))
                using (var delDepSrc = conn.Prepare("DELETE FROM pkg_dep WHERE src_uid=?"))
                using (var delDepRef = conn.Prepare("DELETE FROM pkg_dep WHERE dep_uid=?"))
                using (var delPkg = conn.Prepare("DELETE FROM pkg WHERE uid=?"))
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Candidate candidate = candidates[i];
                        if (string.IsNullOrEmpty(candidate.StoredUid) || string.IsNullOrEmpty(candidate.ExactUid)) continue;
                        if (string.Equals(candidate.StoredUid, candidate.ExactUid, StringComparison.OrdinalIgnoreCase)) continue;

                        if (candidate.FirstScanned != 0L && candidate.FirstScanned != long.MinValue)
                        {
                            saveDate.BindText(1, candidate.ExactUid);
                            saveDate.BindInt64(2, candidate.FirstScanned);
                            StepDone(saveDate);
                            saveDate.Reset();
                        }

                        delMem.BindText(1, candidate.StoredUid); StepDone(delMem); delMem.Reset();
                        delDepSrc.BindText(1, candidate.StoredUid); StepDone(delDepSrc); delDepSrc.Reset();
                        delDepRef.BindText(1, candidate.StoredUid); StepDone(delDepRef); delDepRef.Reset();
                        delPkg.BindText(1, candidate.StoredUid); StepDone(delPkg); delPkg.Reset();
                        repaired++;
                    }
                }
                conn.ExecUtf8("COMMIT;");
                return repaired;
            }
            catch
            {
                try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                throw;
            }
        }

        private static void StepDone(VpbSqlite3.Statement statement)
        {
            int rc = statement.Step();
            if (rc != VpbSqlite3.SqliteDone)
                throw new InvalidOperationException("SQLite repair statement failed: " + rc);
        }

        internal static void MergePreservedFirstScanned(
            VpbSqlite3.Connection conn,
            Dictionary<string, long> firstScannedByUid)
        {
            if (conn == null || firstScannedByUid == null) return;
            EnsureSchema(conn);
            using (var sel = conn.Prepare("SELECT uid, first_scanned FROM pkg_uid_first_scanned_repair"))
            {
                int rc;
                while ((rc = sel.Step()) == VpbSqlite3.SqliteRow)
                {
                    string uid = sel.ColumnText(0);
                    long preserved = sel.ColumnInt64(1);
                    if (string.IsNullOrEmpty(uid) || preserved == 0L || preserved == long.MinValue) continue;

                    long existing;
                    if (!firstScannedByUid.TryGetValue(uid, out existing)
                        || existing == 0L
                        || existing == long.MinValue
                        || preserved < existing)
                        firstScannedByUid[uid] = preserved;
                }
                RequireReadCompleted(rc);
            }
        }

        internal static void RequireReadCompleted(int rc)
        {
            if (rc != VpbSqlite3.SqliteDone)
                throw new InvalidOperationException("SQLite repair query failed: " + rc);
        }

        internal static void ConsumeApplied(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            EnsureSchema(conn);
            conn.ExecUtf8(
                "DELETE FROM pkg_uid_first_scanned_repair " +
                "WHERE EXISTS (SELECT 1 FROM pkg WHERE pkg.uid=pkg_uid_first_scanned_repair.uid);");
        }
    }
}
