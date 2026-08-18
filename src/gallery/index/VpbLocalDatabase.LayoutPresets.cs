using System;
using System.Collections.Generic;
using SimpleJSON;

namespace VPB
{
    /// <summary>SQLite-backed gallery layout presets. Sibling of <c>gallery_filter_preset</c>.</summary>
    internal static partial class VpbLocalDatabase
    {
        private static void EnsureLayoutPresetTables(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS gallery_layout_preset (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "name TEXT NOT NULL," +
                "mode INTEGER NOT NULL DEFAULT 0," +
                "sort_order INTEGER NOT NULL DEFAULT 0," +
                "pinned INTEGER NOT NULL DEFAULT 0," +
                "state_json TEXT NOT NULL," +
                "updated_utc INTEGER NOT NULL);" +
                "CREATE INDEX IF NOT EXISTS idx_glp_mode ON gallery_layout_preset(mode, sort_order);");
        }

        /// <summary>
        /// Loads presets. With <paramref name="withPayload"/> false only the list-view columns are read
        /// and <see cref="GalleryLayoutPreset.Panes"/> stays empty — so opening the manager with a large
        /// collection costs one query and no JSON parsing.
        /// </summary>
        internal static bool TryLoadLayoutPresets(List<GalleryLayoutPreset> into, bool withPayload)
        {
            if (into == null) return false;
            into.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    string sql = withPayload
                        ? "SELECT id, name, mode, sort_order, pinned, state_json FROM gallery_layout_preset ORDER BY sort_order ASC, id ASC"
                        : "SELECT id, name, mode, sort_order, pinned FROM gallery_layout_preset ORDER BY sort_order ASC, id ASC";

                    using (var st = conn.Prepare(sql))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            long id = st.ColumnInt64(0);
                            string nameCol = st.ColumnText(1) ?? "";
                            long mode = st.ColumnInt64(2);
                            long pinned = st.ColumnInt64(4);

                            GalleryLayoutPreset entry = null;
                            if (withPayload)
                            {
                                string json = st.ColumnText(5);
                                try
                                {
                                    if (!string.IsNullOrEmpty(json))
                                        entry = GalleryLayoutPreset.FromJsonString(json);
                                }
                                catch { entry = null; }
                            }

                            if (entry == null)
                            {
                                entry = new GalleryLayoutPreset();
                                entry.PayloadLoaded = withPayload;
                            }
                            else
                            {
                                entry.PayloadLoaded = true;
                            }

                            entry.Id = (int)id;
                            if (string.IsNullOrEmpty(entry.Name)) entry.Name = nameCol;
                            entry.Mode = (int)mode;
                            entry.Pinned = pinned != 0;
                            into.Add(entry);
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryLoadLayoutPresets failed: " + ex.Message); } catch { }
                into.Clear();
                return false;
            }
        }

        /// <summary>Reads one preset's full payload. Used when a lazily listed row is applied or edited.</summary>
        internal static GalleryLayoutPreset TryLoadLayoutPresetPayload(int id)
        {
            if (id <= 0 || !VpbSqlite3.IsAvailable) return null;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare(
                        "SELECT name, mode, pinned, state_json FROM gallery_layout_preset WHERE id=? LIMIT 1"))
                    {
                        st.BindInt64(1, id);
                        if (st.Step() != VpbSqlite3.SqliteRow) return null;

                        string nameCol = st.ColumnText(0) ?? "";
                        long mode = st.ColumnInt64(1);
                        long pinned = st.ColumnInt64(2);
                        string json = st.ColumnText(3);

                        GalleryLayoutPreset e = GalleryLayoutPreset.FromJsonString(json);
                        if (e == null) return null;
                        e.Id = id;
                        if (string.IsNullOrEmpty(e.Name)) e.Name = nameCol;
                        e.Mode = (int)mode;
                        e.Pinned = pinned != 0;
                        e.PayloadLoaded = true;
                        return e;
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryLoadLayoutPresetPayload failed: " + ex.Message); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Replace-all write. Rows whose payload was never loaded keep their stored JSON, so a lazy
        /// list can be reordered or renamed without first parsing every preset.
        /// </summary>
        internal static bool TrySaveLayoutPresets(IList<GalleryLayoutPreset> presets)
        {
            if (presets == null) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);

                    var keepJson = new Dictionary<int, string>();
                    for (int i = 0; i < presets.Count; i++)
                    {
                        GalleryLayoutPreset e = presets[i];
                        if (e == null || e.PayloadLoaded || e.Id <= 0) continue;
                        keepJson[e.Id] = null;
                    }
                    if (keepJson.Count > 0)
                    {
                        using (var sel = conn.Prepare("SELECT id, state_json FROM gallery_layout_preset"))
                        {
                            while (sel.Step() == VpbSqlite3.SqliteRow)
                            {
                                int rid = (int)sel.ColumnInt64(0);
                                if (keepJson.ContainsKey(rid))
                                    keepJson[rid] = sel.ColumnText(1);
                            }
                        }
                    }

                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        conn.ExecUtf8("DELETE FROM gallery_layout_preset;");
                        long now = DateTime.UtcNow.ToBinary();

                        int maxId = 0;
                        for (int i = 0; i < presets.Count; i++)
                        {
                            GalleryLayoutPreset e = presets[i];
                            // Built-in ids sit in a reserved high band — seeding nextId from one would
                            // hand every new user preset an id inside that band.
                            if (e != null && !e.IsBuiltIn && e.Id > maxId) maxId = e.Id;
                        }
                        int nextId = maxId + 1;

                        using (var st = conn.Prepare(
                            "INSERT INTO gallery_layout_preset(id, name, mode, sort_order, pinned, state_json, updated_utc) " +
                            "VALUES(?,?,?,?,?,?,?)"))
                        {
                            for (int i = 0; i < presets.Count; i++)
                            {
                                GalleryLayoutPreset entry = presets[i];
                                if (entry == null) continue;
                                // Built-ins ship with the build and live in memory only.
                                if (entry.IsBuiltIn) continue;
                                if (entry.Id <= 0)
                                {
                                    entry.Id = nextId;
                                    nextId++;
                                }

                                string json;
                                if (entry.PayloadLoaded)
                                {
                                    json = entry.ToJsonString();
                                }
                                else
                                {
                                    // Never rewrite a lazily listed row from an empty in-memory payload.
                                    string stored;
                                    if (!keepJson.TryGetValue(entry.Id, out stored) || string.IsNullOrEmpty(stored))
                                        continue;
                                    json = stored;
                                }
                                if (string.IsNullOrEmpty(json)) json = "{}";

                                st.BindInt64(1, entry.Id);
                                st.BindText(2, entry.Name ?? "");
                                st.BindInt64(3, entry.Mode);
                                st.BindInt64(4, i);
                                st.BindInt64(5, entry.Pinned ? 1 : 0);
                                st.BindText(6, json);
                                st.BindInt64(7, entry.UpdatedUtc != 0 ? entry.UpdatedUtc : now);
                                st.Step();
                                st.Reset();
                            }
                        }

                        try
                        {
                            long maxStored = ScalarInt64(conn, "SELECT MAX(id) FROM gallery_layout_preset;");
                            if (maxStored > 0)
                            {
                                long seqExists = ScalarInt64(conn,
                                    "SELECT COUNT(*) FROM sqlite_sequence WHERE name='gallery_layout_preset';");
                                if (seqExists > 0)
                                {
                                    using (var seq = conn.Prepare(
                                        "UPDATE sqlite_sequence SET seq=? WHERE name='gallery_layout_preset'"))
                                    {
                                        seq.BindInt64(1, maxStored);
                                        seq.Step();
                                    }
                                }
                                else
                                {
                                    using (var ins = conn.Prepare(
                                        "INSERT INTO sqlite_sequence(name,seq) VALUES('gallery_layout_preset',?)"))
                                    {
                                        ins.BindInt64(1, maxStored);
                                        ins.Step();
                                    }
                                }
                            }
                        }
                        catch { }

                        conn.ExecUtf8("COMMIT;");
                        return true;
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
                try { LogUtil.LogWarning("[VPB] TrySaveLayoutPresets failed: " + ex.Message); } catch { }
                return false;
            }
        }
    }
}
