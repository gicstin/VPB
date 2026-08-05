using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;

namespace VPB
{
    /// <summary>SQLite-backed gallery filter presets (formerly Cache/VPB/quick_filters.json).</summary>
    internal static partial class VpbLocalDatabase
    {
        private const string FilterPresetsMigratedMetaKey = "gallery_filter_preset_migrated_v1";

        private static void EnsureFilterPresetTables(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS gallery_filter_preset (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "name TEXT NOT NULL," +
                "sort_order INTEGER NOT NULL DEFAULT 0," +
                "pinned INTEGER NOT NULL DEFAULT 0," +
                "state_json TEXT NOT NULL," +
                "updated_utc INTEGER NOT NULL);" +
                "CREATE INDEX IF NOT EXISTS idx_gfp_sort ON gallery_filter_preset(sort_order);" +
                "CREATE INDEX IF NOT EXISTS idx_gfp_pinned ON gallery_filter_preset(pinned);");
        }

        /// <summary>Load all presets ordered by sort_order. Returns false if SQLite unavailable or on failure.</summary>
        internal static bool TryLoadFilterPresets(List<QuickFilterEntry> into)
        {
            if (into == null) return false;
            into.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare(
                        "SELECT id, name, sort_order, pinned, state_json FROM gallery_filter_preset ORDER BY sort_order ASC, id ASC"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            long id = st.ColumnInt64(0);
                            string nameCol = st.ColumnText(1) ?? "";
                            long pinned = st.ColumnInt64(3);
                            string json = st.ColumnText(4);
                            QuickFilterEntry entry = null;
                            try
                            {
                                if (!string.IsNullOrEmpty(json))
                                {
                                    JSONNode node = JSON.Parse(json);
                                    if (node != null)
                                        entry = QuickFilterEntry.FromJSON(node);
                                }
                            }
                            catch { entry = null; }

                            if (entry == null)
                            {
                                entry = new QuickFilterEntry();
                                entry.Name = nameCol;
                            }
                            entry.Id = (int)id;
                            if (string.IsNullOrEmpty(entry.Name)) entry.Name = nameCol;
                            entry.Pinned = pinned != 0;
                            into.Add(entry);
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryLoadFilterPresets failed: " + ex.Message); } catch { }
                into.Clear();
                return false;
            }
        }

        /// <summary>Replace-all write of preset list. Assigns Ids for new rows. Returns false if SQLite unavailable.</summary>
        internal static bool TrySaveFilterPresets(IList<QuickFilterEntry> filters)
        {
            if (filters == null) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        conn.ExecUtf8("DELETE FROM gallery_filter_preset;");
                        long now = DateTime.UtcNow.ToBinary();

                        int maxId = 0;
                        for (int i = 0; i < filters.Count; i++)
                        {
                            QuickFilterEntry e = filters[i];
                            if (e != null && e.Id > maxId) maxId = e.Id;
                        }
                        int nextId = maxId + 1;

                        using (var st = conn.Prepare(
                            "INSERT INTO gallery_filter_preset(id, name, sort_order, pinned, state_json, updated_utc) VALUES(?,?,?,?,?,?)"))
                        {
                            for (int i = 0; i < filters.Count; i++)
                            {
                                QuickFilterEntry entry = filters[i];
                                if (entry == null) continue;
                                if (entry.Id <= 0)
                                {
                                    entry.Id = nextId;
                                    nextId++;
                                }

                                string name = entry.Name ?? "";
                                string json = "{}";
                                try
                                {
                                    JSONNode node = entry.ToJSON();
                                    json = VPB.src.util.JsonSerializationUtil.Serialize(node, 32_768);
                                }
                                catch
                                {
                                    try { json = entry.ToJSON().ToString(); } catch { json = "{}"; }
                                }

                                st.BindInt64(1, entry.Id);
                                st.BindText(2, name);
                                st.BindInt64(3, i);
                                st.BindInt64(4, entry.Pinned ? 1 : 0);
                                st.BindText(5, json);
                                st.BindInt64(6, now);
                                st.Step();
                                st.Reset();
                            }
                        }

                        try
                        {
                            long maxStored = ScalarInt64(conn, "SELECT MAX(id) FROM gallery_filter_preset;");
                            if (maxStored > 0)
                            {
                                long seqExists = ScalarInt64(conn,
                                    "SELECT COUNT(*) FROM sqlite_sequence WHERE name='gallery_filter_preset';");
                                if (seqExists > 0)
                                {
                                    using (var seq = conn.Prepare(
                                        "UPDATE sqlite_sequence SET seq=? WHERE name='gallery_filter_preset'"))
                                    {
                                        seq.BindInt64(1, maxStored);
                                        seq.Step();
                                    }
                                }
                                else
                                {
                                    using (var ins = conn.Prepare(
                                        "INSERT INTO sqlite_sequence(name,seq) VALUES('gallery_filter_preset',?)"))
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
                try { LogUtil.LogWarning("[VPB] TrySaveFilterPresets failed: " + ex.Message); } catch { }
                return false;
            }
        }

        /// <summary>
        /// One-shot: if SQL table empty and legacy JSON exists, import rows and mark migrated.
        /// </summary>
        internal static bool TryMigrateFilterPresetsFromJsonFile(string jsonFilePath)
        {
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(jsonFilePath) || !File.Exists(jsonFilePath)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    long count = ScalarInt64(conn, "SELECT COUNT(*) FROM gallery_filter_preset;");
                    if (count > 0)
                    {
                        if (string.IsNullOrEmpty(MetaGet(conn, FilterPresetsMigratedMetaKey)))
                            MetaSet(conn, FilterPresetsMigratedMetaKey, "1");
                        return false;
                    }
                }

                var migrateList = new List<QuickFilterEntry>();
                string json = File.ReadAllText(jsonFilePath);
                if (string.IsNullOrEmpty(json)) return false;
                JSONNode root = JSON.Parse(json);
                JSONArray arr = root != null ? root.AsArray : null;
                if (arr == null || arr.Count == 0) return false;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] == null) continue;
                    try
                    {
                        QuickFilterEntry e = QuickFilterEntry.FromJSON(arr[i]);
                        if (e != null) migrateList.Add(e);
                    }
                    catch { }
                }
                if (migrateList.Count == 0) return false;
                if (!TrySaveFilterPresets(migrateList)) return false;

                try
                {
                    using (var conn = new VpbSqlite3.Connection(DbPath))
                    {
                        EnsureSchema(conn);
                        MetaSet(conn, FilterPresetsMigratedMetaKey, "1");
                    }
                }
                catch { }

                try { LogUtil.Log("[VPB] Migrated " + migrateList.Count + " filter presets from JSON to SQLite."); } catch { }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] Filter preset JSON migrate failed: " + ex.Message); } catch { }
                return false;
            }
        }
    }
}
