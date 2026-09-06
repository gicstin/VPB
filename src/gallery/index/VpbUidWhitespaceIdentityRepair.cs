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
                using (var existsPkg = conn.Prepare("SELECT 1 FROM pkg WHERE uid=? LIMIT 1"))
                using (var saveDate = conn.Prepare("INSERT OR REPLACE INTO pkg_uid_first_scanned_repair(uid,first_scanned) VALUES(?,?)"))
                using (var selExactDate = conn.Prepare("SELECT ifnull(first_scanned,0) FROM pkg WHERE uid=? LIMIT 1"))
                using (var upExactDate = conn.Prepare("UPDATE pkg SET first_scanned=? WHERE uid=?"))
                using (var renamePkg = conn.Prepare("UPDATE pkg SET uid=? WHERE uid=?"))
                using (var delPkg = conn.Prepare("DELETE FROM pkg WHERE uid=?"))
                using (var upAlias = TryPrepare(conn, "UPDATE pkg SET uid_alias=?, ident_key=? WHERE uid=?"))
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        Candidate candidate = candidates[i];
                        if (string.IsNullOrEmpty(candidate.StoredUid) || string.IsNullOrEmpty(candidate.ExactUid)) continue;
                        if (string.Equals(candidate.StoredUid, candidate.ExactUid, StringComparison.OrdinalIgnoreCase)) continue;

                        bool exactExists = PkgExists(existsPkg, candidate.ExactUid);
                        RemapDependentUidRows(conn, candidate.StoredUid, candidate.ExactUid);
                        RemapPrefixedUserKeys(conn, candidate.StoredUid, candidate.ExactUid);

                        if (candidate.FirstScanned != 0L && candidate.FirstScanned != long.MinValue)
                        {
                            saveDate.BindText(1, candidate.ExactUid);
                            saveDate.BindInt64(2, candidate.FirstScanned);
                            StepDone(saveDate);
                            saveDate.Reset();
                        }

                        if (exactExists)
                        {
                            MergeFirstScannedOntoExisting(selExactDate, upExactDate, candidate);
                            delPkg.BindText(1, candidate.StoredUid);
                            StepDone(delPkg);
                            delPkg.Reset();
                        }
                        else
                        {
                            renamePkg.BindText(1, candidate.ExactUid);
                            renamePkg.BindText(2, candidate.StoredUid);
                            StepDone(renamePkg);
                            renamePkg.Reset();
                            UpdateAliasIdent(upAlias, candidate.ExactUid);
                        }

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

        static bool PkgExists(VpbSqlite3.Statement existsPkg, string uid)
        {
            existsPkg.BindText(1, uid);
            int rc = existsPkg.Step();
            existsPkg.Reset();
            return rc == VpbSqlite3.SqliteRow;
        }

        static void MergeFirstScannedOntoExisting(
            VpbSqlite3.Statement selExactDate,
            VpbSqlite3.Statement upExactDate,
            Candidate candidate)
        {
            long preserved = candidate.FirstScanned;
            if (preserved == 0L || preserved == long.MinValue) return;

            selExactDate.BindText(1, candidate.ExactUid);
            int rc = selExactDate.Step();
            long existing = 0L;
            if (rc == VpbSqlite3.SqliteRow)
                existing = selExactDate.ColumnInt64(0);
            selExactDate.Reset();
            if (rc != VpbSqlite3.SqliteRow && rc != VpbSqlite3.SqliteDone)
                throw new InvalidOperationException("SQLite repair statement failed: " + rc);

            if (existing != 0L && existing != long.MinValue && existing <= preserved) return;

            upExactDate.BindInt64(1, preserved);
            upExactDate.BindText(2, candidate.ExactUid);
            StepDone(upExactDate);
            upExactDate.Reset();
        }

        static void UpdateAliasIdent(VpbSqlite3.Statement upAlias, string exactUid)
        {
            if (upAlias == null) return;
            string alias = "";
            string ident = "";
            try { alias = VpbLocalDatabase.ComputePkgUidAlias(exactUid) ?? ""; } catch { }
            try { ident = VpbLocalDatabase.ComputePkgIdentKey(exactUid) ?? ""; } catch { }
            upAlias.BindText(1, alias);
            upAlias.BindText(2, ident);
            upAlias.BindText(3, exactUid);
            int rc = upAlias.Step();
            upAlias.Reset();
            if (rc != VpbSqlite3.SqliteDone && rc != VpbSqlite3.SqliteRow)
                throw new InvalidOperationException("SQLite repair statement failed: " + rc);
        }

        static void RemapDependentUidRows(VpbSqlite3.Connection conn, string storedUid, string exactUid)
        {
            RemapUidColumn(conn, "cat_mem", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "loose_cat_mem", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_dep", "src_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_dep", "dep_uid", storedUid, exactUid);
            RemapUidColumn(conn, "hide_marker", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "gallery_item_user_tag", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "cache_usage_pkg", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "datapack_link", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_file", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_manifest_dep", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_manifest_cloth", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_manifest_hair", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_ident", "pkg_uid", storedUid, exactUid);
            RemapUidColumn(conn, "pkg_manifest", "uid", storedUid, exactUid);
            RemapUidColumn(conn, "cleanup_exclude", "uid", storedUid, exactUid);
        }

        static void RemapUidColumn(
            VpbSqlite3.Connection conn,
            string table,
            string column,
            string storedUid,
            string exactUid)
        {
            if (!TableExists(conn, table)) return;
            string sqlUp = "UPDATE OR IGNORE " + table + " SET " + column + "=? WHERE " + column + "=?";
            string sqlDel = "DELETE FROM " + table + " WHERE " + column + "=?";
            using (var up = conn.Prepare(sqlUp))
            using (var del = conn.Prepare(sqlDel))
            {
                up.BindText(1, exactUid);
                up.BindText(2, storedUid);
                StepDone(up);
                del.BindText(1, storedUid);
                StepDone(del);
            }
        }

        static void RemapPrefixedUserKeys(VpbSqlite3.Connection conn, string storedUid, string exactUid)
        {
            RemapPrefixedPrimaryKey(conn, "item_usage", "item_key", storedUid.ToLowerInvariant(), exactUid.ToLowerInvariant());
            RemapPrefixedPrimaryKey(conn, "scene_person_atom", "scene_key", storedUid, exactUid);
            RemapPrefixedPrimaryKey(conn, "scene_person_atom_usage", "scene_key", storedUid, exactUid);
        }

        static void RemapPrefixedPrimaryKey(
            VpbSqlite3.Connection conn,
            string table,
            string column,
            string storedPrefix,
            string exactPrefix)
        {
            if (!TableExists(conn, table)) return;
            if (string.IsNullOrEmpty(storedPrefix) || string.IsNullOrEmpty(exactPrefix)) return;

            var oldKeys = new List<string>();
            using (var sel = conn.Prepare("SELECT " + column + " FROM " + table + " WHERE " + column + "=? OR instr(" + column + ",?)=1"))
            {
                sel.BindText(1, storedPrefix);
                sel.BindText(2, storedPrefix + ":/");
                int rc;
                while ((rc = sel.Step()) == VpbSqlite3.SqliteRow)
                {
                    string key = sel.ColumnText(0);
                    if (!string.IsNullOrEmpty(key)) oldKeys.Add(key);
                }
                RequireReadCompleted(rc);
            }
            if (oldKeys.Count == 0) return;

            using (var up = conn.Prepare("UPDATE OR IGNORE " + table + " SET " + column + "=? WHERE " + column + "=?"))
            using (var del = conn.Prepare("DELETE FROM " + table + " WHERE " + column + "=?"))
            {
                for (int i = 0; i < oldKeys.Count; i++)
                {
                    string oldKey = oldKeys[i];
                    string newKey = RewritePrefixedKey(oldKey, storedPrefix, exactPrefix);
                    if (string.IsNullOrEmpty(newKey) || string.Equals(newKey, oldKey, StringComparison.Ordinal)) continue;

                    up.BindText(1, newKey);
                    up.BindText(2, oldKey);
                    StepDone(up);
                    up.Reset();

                    del.BindText(1, oldKey);
                    StepDone(del);
                    del.Reset();
                }
            }
        }

        static string RewritePrefixedKey(string oldKey, string storedPrefix, string exactPrefix)
        {
            if (string.Equals(oldKey, storedPrefix, StringComparison.Ordinal)) return exactPrefix;
            string storedEntry = storedPrefix + ":/";
            if (oldKey.Length > storedEntry.Length && oldKey.StartsWith(storedEntry, StringComparison.Ordinal))
                return exactPrefix + ":/" + oldKey.Substring(storedEntry.Length);
            return oldKey;
        }

        static bool TableExists(VpbSqlite3.Connection conn, string name)
        {
            using (var st = conn.Prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=? LIMIT 1"))
            {
                st.BindText(1, name);
                return st.Step() == VpbSqlite3.SqliteRow;
            }
        }

        static VpbSqlite3.Statement TryPrepare(VpbSqlite3.Connection conn, string sql)
        {
            try { return conn.Prepare(sql); }
            catch { return null; }
        }

        static void StepDone(VpbSqlite3.Statement statement)
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
