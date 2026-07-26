using System;
using System.Collections.Generic;
using SimpleJSON;
using MVR.FileManagement;

namespace VPB
{
    // Persistent cache of a scene's Person-atom JSON, keyed by scene + file signature, so the
    // Import sidebar populates its picker (and slices one atom at Apply) without re-parsing the scene.
    internal static partial class VpbLocalDatabase
    {
        const int SceneAtomCacheSchemaVersion = 1;
        const string MetaSceneAtomCacheSchemaKey = "scene_atom_cache_schema_v";

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
                "PRIMARY KEY(scene_key, atom_id));");
            try
            {
                using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                {
                    st.BindText(1, MetaSceneAtomCacheSchemaKey);
                    st.BindText(2, SceneAtomCacheSchemaVersion.ToString());
                    st.Step();
                }
            }
            catch { }
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
                    using (var st = conn.Prepare(
                        "SELECT atom_id FROM scene_person_atom WHERE scene_key=? AND sig=? ORDER BY seq"))
                    {
                        st.BindText(1, sceneKey);
                        st.BindText(2, sig);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string id = st.ColumnText(0);
                            if (string.IsNullOrEmpty(id)) continue;
                            found.Add(id);
                        }
                    }
                    if (found.Count == 0) return false;
                    outIds.AddRange(found);
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
                        using (var ins = conn.Prepare(
                            "INSERT OR REPLACE INTO scene_person_atom(scene_key,atom_id,seq,atom_json,sig) VALUES(?,?,?,?,?)"))
                        {
                            for (int i = 0; i < ids.Count; i++)
                            {
                                string id = ids[i];
                                JSONClass node = nodes[i];
                                if (string.IsNullOrEmpty(id) || node == null) continue;
                                ins.BindText(1, sceneKey);
                                ins.BindText(2, id);
                                ins.BindInt64(3, i);
                                ins.BindText(4, VPB.src.util.JsonSerializationUtil.Serialize(node, 1 << 20));
                                ins.BindText(5, sig);
                                ins.Step();
                                ins.Reset();
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
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryWriteSceneAtoms failed: " + ex.Message); } catch { }
            }
        }
    }
}
