using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SimpleJSON;
using MVR.FileManagement;

namespace VPB
{
    // Persistent cache of a scene's Person-atom JSON, keyed by scene + file signature, so the
    // Import sidebar populates its picker (and slices one atom at Apply) without re-parsing the scene.
    internal static partial class VpbLocalDatabase
    {
        const int SceneAtomCacheSchemaVersion = 4;
        internal const int SceneAtomGenderNotComputed = -1;
        const string MetaSceneAtomCacheSchemaKey = "scene_atom_cache_schema_v";
        private static readonly object s_SceneAtomCacheMaintenanceLock = new object();
        private static readonly object s_SceneAtomCacheTrimLock = new object();
        private static bool s_SceneAtomCacheTrimRunning;
        private static bool s_SceneAtomCacheTrimRequested;

        internal static long GetDatabaseFileLength()
        {
            try { return new FileInfo(DbPath).Length; }
            catch { return 0L; }
        }

        internal static bool TryClearSceneAtomCacheAndVacuum(out bool cacheCleared, out Exception error)
        {
            cacheCleared = false;
            error = null;
            try
            {
                if (!VpbSqlite3.IsAvailable)
                    throw new InvalidOperationException("SQLite is unavailable");
                lock (s_SceneAtomCacheMaintenanceLock)
                {
                    using (var conn = new VpbSqlite3.Connection(DbPath))
                    {
                        EnsureSchema(conn);
                        EnsureSceneAtomCacheSchema(conn);
                        conn.ExecUtf8("DELETE FROM scene_person_atom;");
                        conn.ExecUtf8("DELETE FROM scene_person_atom_usage;");
                        cacheCleared = true;
                        conn.ExecUtf8("VACUUM;");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        internal static void EnsureSceneAtomCacheSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            // PK leftmost column is scene_key, so WHERE scene_key=? range scans use the PK index;
            // no separate index needed. A schema/format change self-heals: sig mismatch -> cache miss -> overwrite.
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS scene_person_atom (" +
                "scene_key TEXT NOT NULL," +
                "atom_id TEXT NOT NULL," +
                "seq INTEGER NOT NULL," +
                "atom_json TEXT NOT NULL," +
                "sig TEXT NOT NULL," +
                "gender INTEGER NOT NULL DEFAULT -1," +
                "PRIMARY KEY(scene_key, atom_id));");
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS scene_person_atom_usage (" +
                "scene_key TEXT PRIMARY KEY," +
                "cached_at INTEGER NOT NULL," +
                "bytes INTEGER NOT NULL);");

            long schemaVersion = 0;
            try
            {
                using (var st = conn.Prepare("SELECT v FROM meta WHERE k=?"))
                {
                    st.BindText(1, MetaSceneAtomCacheSchemaKey);
                    if (st.Step() == VpbSqlite3.SqliteRow)
                        long.TryParse(st.ColumnText(0), out schemaVersion);
                }
            }
            catch { }

            if (schemaVersion >= SceneAtomCacheSchemaVersion) return;
            conn.ExecUtf8("BEGIN IMMEDIATE;");
            try
            {
                // Preserve pre-migration eviction order: older rowids represent earlier cache writes.
                conn.ExecUtf8(
                    "INSERT OR IGNORE INTO scene_person_atom_usage(scene_key,cached_at,bytes) " +
                    "SELECT scene_key,MIN(rowid),SUM(length(CAST(atom_json AS BLOB))) " +
                    "FROM scene_person_atom GROUP BY scene_key;");
                if (schemaVersion < 4)
                {
                    try { conn.ExecUtf8("ALTER TABLE scene_person_atom ADD COLUMN gender INTEGER NOT NULL DEFAULT -1;"); }
                    catch { }
                    conn.ExecUtf8("UPDATE scene_person_atom SET gender=" + SceneAtomGenderNotComputed + ";");
                }
                using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                {
                    st.BindText(1, MetaSceneAtomCacheSchemaKey);
                    st.BindText(2, SceneAtomCacheSchemaVersion.ToString());
                    st.Step();
                }
                conn.ExecUtf8("COMMIT;");
            }
            catch
            {
                try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                throw;
            }
        }

        internal static void RequestSceneAtomCacheTrim()
        {
            bool startWorker = false;
            lock (s_SceneAtomCacheTrimLock)
            {
                s_SceneAtomCacheTrimRequested = true;
                if (!s_SceneAtomCacheTrimRunning)
                {
                    s_SceneAtomCacheTrimRunning = true;
                    startWorker = true;
                }
            }
            if (!startWorker) return;

            bool queued = false;
            try { queued = ThreadPool.QueueUserWorkItem(_ => SceneAtomCacheTrimWorker()); }
            catch { }
            if (queued) return;

            lock (s_SceneAtomCacheTrimLock)
            {
                s_SceneAtomCacheTrimRunning = false;
                s_SceneAtomCacheTrimRequested = false;
            }
            try { LogUtil.LogWarning("[VPB] Could not start scene import cache trim worker"); } catch { }
        }

        private static void SceneAtomCacheTrimWorker()
        {
            while (true)
            {
                lock (s_SceneAtomCacheTrimLock)
                {
                    if (!s_SceneAtomCacheTrimRequested)
                    {
                        s_SceneAtomCacheTrimRunning = false;
                        return;
                    }
                    s_SceneAtomCacheTrimRequested = false;
                }

                try { TrimSceneAtomCacheToConfiguredLimit(); }
                catch (Exception ex)
                {
                    try { LogUtil.LogWarning("[VPB] Scene import cache trim failed: " + ex.Message); } catch { }
                }
            }
        }

        private static void TrimSceneAtomCacheToConfiguredLimit()
        {
            if (!VpbSqlite3.IsAvailable) return;
            int limitMb = VPBConfig.SceneImportCacheLimitMbDefault;
            try
            {
                if (VPBConfig.Instance != null)
                    limitMb = VPBConfig.ClampSceneImportCacheLimitMb(VPBConfig.Instance.SceneImportCacheLimitMb);
            }
            catch { }
            long limitBytes = limitMb * 1024L * 1024L;

            lock (s_SceneAtomCacheMaintenanceLock)
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureSceneAtomCacheSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        var oldest = new List<KeyValuePair<string, long>>();
                        long totalBytes = 0;
                        using (var st = conn.Prepare(
                            "SELECT scene_key,bytes FROM scene_person_atom_usage ORDER BY cached_at,scene_key"))
                        {
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string key = st.ColumnText(0);
                                long bytes = st.ColumnInt64(1);
                                if (string.IsNullOrEmpty(key)) continue;
                                oldest.Add(new KeyValuePair<string, long>(key, bytes));
                                totalBytes += bytes;
                            }
                        }

                        if (totalBytes > limitBytes)
                        {
                            using (var delAtoms = conn.Prepare("DELETE FROM scene_person_atom WHERE scene_key=?"))
                            using (var delUsage = conn.Prepare("DELETE FROM scene_person_atom_usage WHERE scene_key=?"))
                            {
                                for (int i = 0; i < oldest.Count && totalBytes > limitBytes; i++)
                                {
                                    string key = oldest[i].Key;
                                    delAtoms.BindText(1, key);
                                    delAtoms.Step();
                                    delAtoms.Reset();
                                    delUsage.BindText(1, key);
                                    delUsage.Step();
                                    delUsage.Reset();
                                    totalBytes -= oldest[i].Value;
                                }
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
            }
        }

        // Flips when the scene's on-disk bytes change. For a VAR scene the .var file mtime+size move on
        // re-install but the zip entry's own mtime is static, so read from the host package not the FileEntry.
        private static string ComputeSceneSig(FileEntry entry)
        {
            long mtime, size;
            VarFileEntry vfe = entry as VarFileEntry;
            if (vfe != null && vfe.Package != null)
            {
                mtime = vfe.Package.LastWriteTime.ToBinary();
                size = vfe.Package.Size;
            }
            else
            {
                mtime = entry.LastWriteTime.ToBinary();
                size = entry.Size;
            }
            return mtime.ToString() + ":" + size.ToString();
        }

        // Cache HIT only when scene_key AND current sig both match. Returns false (miss/stale) so the
        // caller re-parses and overwrites. Populates outIds in scene order.
        internal static bool TryReadSceneAtomIds(FileEntry entry, List<string> outIds)
        {
            return TryReadSceneAtomIds(entry, outIds, null);
        }

        internal static bool TryReadSceneAtomIds(FileEntry entry, List<string> outIds, List<int> outGenders)
        {
            if (entry == null || outIds == null) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            string sceneKey = entry.Uid;
            if (string.IsNullOrEmpty(sceneKey)) return false;
            string sig = ComputeSceneSig(entry);

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureSceneAtomCacheSchema(conn);
                    // Accumulate locally and only publish on full success: a throw mid-step must not leave the
                    // caller's list half-filled, or its miss branch re-appends and duplicates picker rows.
                    List<string> found = new List<string>(4);
                    List<int> genders = new List<int>(4);
                    using (var st = conn.Prepare(
                        "SELECT atom_id,gender FROM scene_person_atom WHERE scene_key=? AND sig=? ORDER BY seq"))
                    {
                        st.BindText(1, sceneKey);
                        st.BindText(2, sig);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string id = st.ColumnText(0);
                            if (string.IsNullOrEmpty(id)) continue;
                            found.Add(id);
                            genders.Add((int)st.ColumnInt64(1));
                        }
                    }
                    if (found.Count == 0) return false;
                    outIds.AddRange(found);
                    if (outGenders != null) outGenders.AddRange(genders);
                    return true;
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryReadSceneAtomIds failed: " + ex.Message); } catch { }
                return false;
            }
        }

        // Returns the cached atom JSON only if the sig still matches (scene unchanged since the cache write).
        // null on miss/stale -> caller must re-resolve the atom from the live scene file.
        internal static string TryReadSceneAtomJson(FileEntry entry, string atomId)
        {
            if (entry == null || string.IsNullOrEmpty(atomId)) return null;
            if (!VpbSqlite3.IsAvailable) return null;
            string sceneKey = entry.Uid;
            if (string.IsNullOrEmpty(sceneKey)) return null;
            string sig = ComputeSceneSig(entry);

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureSceneAtomCacheSchema(conn);
                    using (var st = conn.Prepare(
                        "SELECT atom_json FROM scene_person_atom WHERE scene_key=? AND atom_id=? AND sig=?"))
                    {
                        st.BindText(1, sceneKey);
                        st.BindText(2, atomId);
                        st.BindText(3, sig);
                        if (st.Step() == VpbSqlite3.SqliteRow) return st.ColumnText(0);
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryReadSceneAtomJson failed: " + ex.Message); } catch { }
                return null;
            }
        }

        internal static int SceneAtomGenderToPersist(JSONClass node)
        {
            int gender = (int)VPB.src.util.LooseVapGenderProbe.ClassifyStorables(node);
            if (gender != (int)VPB.src.util.LooseVapGenderProbe.Gender.Unknown) return gender;
            bool mapReady;
            try { mapReady = VPB.src.util.JSONExtensions.IsCharacterGenderMapInitComplete(); }
            catch { mapReady = false; }
            return mapReady ? gender : SceneAtomGenderNotComputed;
        }

        // ids[i] is the SAME pid the picker derives (incl. "Person_"+i fallback) so Apply reads by pid with no re-match.
        // INSERT OR REPLACE tolerates duplicate pids; serialize via JsonSerializationUtil (SimpleJSON .ToString() is O(N^2)).
        internal static void TryWriteSceneAtoms(FileEntry entry, IList<string> ids, IList<JSONClass> nodes)
        {
            if (entry == null || ids == null || nodes == null) return;
            if (ids.Count == 0 || ids.Count != nodes.Count) return;
            if (!VpbSqlite3.IsAvailable) return;
            string sceneKey = entry.Uid;
            if (string.IsNullOrEmpty(sceneKey)) return;
            string sig = ComputeSceneSig(entry);
            bool wrote = false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureSceneAtomCacheSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var del = conn.Prepare("DELETE FROM scene_person_atom WHERE scene_key=?"))
                        {
                            del.BindText(1, sceneKey);
                            del.Step();
                        }
                        using (var del = conn.Prepare("DELETE FROM scene_person_atom_usage WHERE scene_key=?"))
                        {
                            del.BindText(1, sceneKey);
                            del.Step();
                        }
                        long cachedBytes = 0;
                        using (var ins = conn.Prepare(
                            "INSERT OR REPLACE INTO scene_person_atom(scene_key,atom_id,seq,atom_json,sig,gender) VALUES(?,?,?,?,?,?)"))
                        {
                            for (int i = 0; i < ids.Count; i++)
                            {
                                string id = ids[i];
                                JSONClass node = nodes[i];
                                if (string.IsNullOrEmpty(id) || node == null) continue;
                                string atomJson = VPB.src.util.JsonSerializationUtil.Serialize(node, 1 << 20);
                                ins.BindText(1, sceneKey);
                                ins.BindText(2, id);
                                ins.BindInt64(3, i);
                                ins.BindText(4, atomJson);
                                ins.BindText(5, sig);
                                ins.BindInt64(6, SceneAtomGenderToPersist(node));
                                ins.Step();
                                ins.Reset();
                                cachedBytes += Encoding.UTF8.GetByteCount(atomJson);
                            }
                        }
                        using (var usage = conn.Prepare(
                            "INSERT OR REPLACE INTO scene_person_atom_usage(scene_key,cached_at,bytes) VALUES(?,?,?)"))
                        {
                            usage.BindText(1, sceneKey);
                            usage.BindInt64(2, DateTime.UtcNow.Ticks);
                            usage.BindInt64(3, cachedBytes);
                            usage.Step();
                        }
                        conn.ExecUtf8("COMMIT;");
                        wrote = true;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryWriteSceneAtoms failed: " + ex.Message); } catch { }
            }
            if (wrote) RequestSceneAtomCacheTrim();
        }
    }
}
