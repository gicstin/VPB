using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace VPB
{
    /// <summary>
    /// Persists last-known .var paths so startup can skip recursive directory walks when files unchanged.
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        const string MetaVarPathInventoryCountKey = "var_path_inventory_count_v1";

        struct VarPathRow
        {
            internal string Path;
            internal long Size;
            internal long MtimeTicks;
        }

        internal static void EnsureVarPathInventorySchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS pkg_var_path (" +
                "path TEXT PRIMARY KEY," +
                "file_size INTEGER NOT NULL," +
                "mtime_ticks INTEGER NOT NULL);");
        }

        /// <summary>Load cached paths and validate size/mtime in parallel (no recursive directory walk).</summary>
        internal static bool TryRestoreVarPathInventory(out List<string> paths)
        {
            paths = null;
            if (!VpbSqlite3.IsAvailable) return false;

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                var rows = new List<VarPathRow>(16384);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureVarPathInventorySchema(conn);
                    using (var st = conn.Prepare("SELECT path, file_size, mtime_ticks FROM pkg_var_path ORDER BY path"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string p = st.ColumnText(0);
                            if (string.IsNullOrEmpty(p)) continue;
                            rows.Add(new VarPathRow
                            {
                                Path = p,
                                Size = st.ColumnInt64(1),
                                MtimeTicks = st.ColumnInt64(2)
                            });
                        }
                    }
                }

                if (rows.Count == 0) return false;

                int failCount = 0;
                int chunkSize = 512;
                int chunkCount = (rows.Count + chunkSize - 1) / chunkSize;
                int pending = chunkCount;
                var pendingLock = new object();

                for (int c = 0; c < chunkCount; c++)
                {
                    int start = c * chunkSize;
                    int end = Math.Min(start + chunkSize, rows.Count);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            for (int i = start; i < end; i++)
                            {
                                VarPathRow row = rows[i];
                                try
                                {
                                    if (!File.Exists(row.Path))
                                    {
                                        Interlocked.Increment(ref failCount);
                                        continue;
                                    }
                                    var fi = new FileInfo(row.Path);
                                    if (fi.Length != row.Size || fi.LastWriteTimeUtc.Ticks != row.MtimeTicks)
                                        Interlocked.Increment(ref failCount);
                                }
                                catch
                                {
                                    Interlocked.Increment(ref failCount);
                                }
                            }
                        }
                        finally
                        {
                            lock (pendingLock)
                            {
                                pending--;
                                if (pending == 0)
                                    Monitor.PulseAll(pendingLock);
                            }
                        }
                    });
                }

                lock (pendingLock)
                {
                    while (pending > 0)
                        Monitor.Wait(pendingLock, 100);
                }

                sw.Stop();
                if (failCount > 0)
                {
                    try
                    {
                        LogUtil.Log("Var path inventory cache MISS validate_fail=" + failCount
                            + " total=" + rows.Count + " ms=" + sw.ElapsedMilliseconds);
                    }
                    catch { }
                    return false;
                }

                paths = new List<string>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                    paths.Add(rows[i].Path);

                try
                {
                    LogUtil.Log("Var path inventory cache HIT paths=" + paths.Count
                        + " validate_ms=" + sw.ElapsedMilliseconds);
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryRestoreVarPathInventory failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TrySaveVarPathInventory(IList<string> paths)
        {
            if (paths == null || paths.Count == 0) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                var rows = new List<VarPathRow>(paths.Count);
                for (int i = 0; i < paths.Count; i++)
                {
                    string p = paths[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    try
                    {
                        if (!File.Exists(p)) continue;
                        var fi = new FileInfo(p);
                        rows.Add(new VarPathRow
                        {
                            Path = p,
                            Size = fi.Length,
                            MtimeTicks = fi.LastWriteTimeUtc.Ticks
                        });
                    }
                    catch { }
                }
                if (rows.Count == 0) return false;

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureVarPathInventorySchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        conn.ExecUtf8("DELETE FROM pkg_var_path;");
                        using (var ins = conn.Prepare(
                            "INSERT INTO pkg_var_path(path,file_size,mtime_ticks) VALUES(?,?,?)"))
                        {
                            for (int i = 0; i < rows.Count; i++)
                            {
                                VarPathRow row = rows[i];
                                ins.BindText(1, row.Path);
                                ins.BindInt64(2, row.Size);
                                ins.BindInt64(3, row.MtimeTicks);
                                ins.Step();
                                ins.Reset();
                            }
                        }
                        using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                        {
                            st.BindText(1, MetaVarPathInventoryCountKey);
                            st.BindText(2, rows.Count.ToString());
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

                sw.Stop();
                try
                {
                    LogUtil.Log("Var path inventory saved paths=" + rows.Count + " ms=" + sw.ElapsedMilliseconds);
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TrySaveVarPathInventory failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TryAppendVarPathInventory(string path)
        {
            if (string.IsNullOrEmpty(path) || !VpbSqlite3.IsAvailable) return false;
            try
            {
                if (!File.Exists(path)) return false;
                var fi = new FileInfo(path);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureVarPathInventorySchema(conn);
                    using (var st = conn.Prepare(
                        "INSERT OR REPLACE INTO pkg_var_path(path,file_size,mtime_ticks) VALUES(?,?,?)"))
                    {
                        st.BindText(1, path);
                        st.BindInt64(2, fi.Length);
                        st.BindInt64(3, fi.LastWriteTimeUtc.Ticks);
                        st.Step();
                    }
                    using (var st = conn.Prepare("SELECT COUNT(*) FROM pkg_var_path"))
                    {
                        if (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            int count = (int)Math.Min(Math.Max(st.ColumnInt64(0), 0), int.MaxValue);
                            using (var upd = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                            {
                                upd.BindText(1, MetaVarPathInventoryCountKey);
                                upd.BindText(2, count.ToString());
                                upd.Step();
                            }
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryAppendVarPathInventory failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TryRemoveVarPathInventory(string path)
        {
            if (string.IsNullOrEmpty(path) || !VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureVarPathInventorySchema(conn);
                    using (var st = conn.Prepare("DELETE FROM pkg_var_path WHERE path = ?"))
                    {
                        st.BindText(1, path);
                        st.Step();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryRemoveVarPathInventory failed: " + ex.Message); } catch { }
                return false;
            }
        }
    }
}
