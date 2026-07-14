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
        const string MetaVarPathInventoryAddonRootMtimeKey = "var_path_inventory_addon_root_mtime_v1";
        const string MetaVarPathInventoryAllRootMtimeKey = "var_path_inventory_all_root_mtime_v1";
        const string MetaRegistryWarmInvSigKey = "registry_warm_inv_sig_v1";

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

        static bool TryGetScanRootMtimeUtcTicks(string root, out long ticks)
        {
            ticks = 0;
            try
            {
                // Nonexistent root is a valid "0" cache key (no files to track).
                if (!Directory.Exists(root))
                    return true;
                // NTFS only bumps a directory's own mtime on immediate-child changes; deep walk is
                // required to detect files added inside subfolders.
                ticks = DeepMaxDirMtimeBinary(root);
                // 0 on an existing dir means the probe failed; refuse so a 0/0 cached pair can't pass.
                return ticks > 0;
            }
            catch
            {
                return false;
            }
        }

        static bool TryLoadVarPathInventoryRootMeta(
            VpbSqlite3.Connection conn,
            out long addonRootMtimeTicks,
            out long allRootMtimeTicks,
            out int savedCount)
        {
            addonRootMtimeTicks = 0;
            allRootMtimeTicks = 0;
            savedCount = 0;
            if (conn == null) return false;
            try
            {
                using (var st = conn.Prepare(
                    "SELECT k, v FROM meta WHERE k IN (?, ?, ?)"))
                {
                    st.BindText(1, MetaVarPathInventoryCountKey);
                    st.BindText(2, MetaVarPathInventoryAddonRootMtimeKey);
                    st.BindText(3, MetaVarPathInventoryAllRootMtimeKey);
                    while (st.Step() == VpbSqlite3.SqliteRow)
                    {
                        string key = st.ColumnText(0) ?? string.Empty;
                        string val = st.ColumnText(1) ?? string.Empty;
                        if (key == MetaVarPathInventoryCountKey)
                        {
                            int c;
                            if (int.TryParse(val, out c))
                                savedCount = c;
                        }
                        else if (key == MetaVarPathInventoryAddonRootMtimeKey)
                        {
                            long t;
                            if (long.TryParse(val, out t))
                                addonRootMtimeTicks = t;
                        }
                        else if (key == MetaVarPathInventoryAllRootMtimeKey)
                        {
                            long t;
                            if (long.TryParse(val, out t))
                                allRootMtimeTicks = t;
                        }
                    }
                }
                return savedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        static void SaveVarPathInventoryRootMeta(VpbSqlite3.Connection conn, int pathCount)
        {
            if (conn == null || pathCount <= 0) return;
            long addonTicks;
            long allTicks;
            if (!TryGetScanRootMtimeUtcTicks("AddonPackages", out addonTicks)) return;
            if (!TryGetScanRootMtimeUtcTicks("AllPackages", out allTicks)) return;

            using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
            {
                st.BindText(1, MetaVarPathInventoryCountKey);
                st.BindText(2, pathCount.ToString());
                st.Step();
                st.Reset();
                st.BindText(1, MetaVarPathInventoryAddonRootMtimeKey);
                st.BindText(2, addonTicks.ToString());
                st.Step();
                st.Reset();
                st.BindText(1, MetaVarPathInventoryAllRootMtimeKey);
                st.BindText(2, allTicks.ToString());
                st.Step();
            }
        }

        static bool TryFastRejectVarPathInventory(
            int rowCount,
            long cachedAddonRootMtimeTicks,
            long cachedAllRootMtimeTicks,
            int cachedCount)
        {
            if (rowCount <= 0 || cachedCount != rowCount) return false;
            long addonNow;
            long allNow;
            if (!TryGetScanRootMtimeUtcTicks("AddonPackages", out addonNow)) return false;
            if (!TryGetScanRootMtimeUtcTicks("AllPackages", out allNow)) return false;
            return addonNow == cachedAddonRootMtimeTicks && allNow == cachedAllRootMtimeTicks;
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

                long cachedAddonRootMtimeTicks = 0;
                long cachedAllRootMtimeTicks = 0;
                int cachedCount = 0;
                using (var connMeta = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(connMeta);
                    TryLoadVarPathInventoryRootMeta(connMeta, out cachedAddonRootMtimeTicks, out cachedAllRootMtimeTicks, out cachedCount);
                }

                if (TryFastRejectVarPathInventory(rows.Count, cachedAddonRootMtimeTicks, cachedAllRootMtimeTicks, cachedCount))
                {
                    paths = new List<string>(rows.Count);
                    for (int i = 0; i < rows.Count; i++)
                        paths.Add(rows[i].Path);
                    sw.Stop();
                    try
                    {
                        LogUtil.Log("Var path inventory cache HIT paths=" + paths.Count
                            + " validate_ms=" + sw.ElapsedMilliseconds + " mode=root_mtime_fast_reject");
                    }
                    catch { }
                    return true;
                }

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

                // Reached here only because TryFastRejectVarPathInventory returned false (root mtime
                // changed) AND no existing rows failed validation. The only remaining cause is file
                // additions, which the cached row list cannot reflect. Force disk enum to pick them up.
                try
                {
                    LogUtil.Log("Var path inventory cache MISS additions_likely rows=" + rows.Count
                        + " validate_ms=" + sw.ElapsedMilliseconds);
                }
                catch { }
                return false;
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
                        SaveVarPathInventoryRootMeta(conn, rows.Count);
                        ClearRegistryWarmInventorySignature(conn);
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

        /// <summary>Fast root-mtime check — true when cached inventory likely still valid (no recursive walk).</summary>
        internal static bool IsVarPathInventoryUnchangedFast()
        {
            if (!VpbSqlite3.IsAvailable || !VamStartupOptimizations.UseCachedVarPathInventory)
                return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureVarPathInventorySchema(conn);
                    int rowCount = 0;
                    using (var st = conn.Prepare("SELECT COUNT(*) FROM pkg_var_path"))
                    {
                        if (st.Step() == VpbSqlite3.SqliteRow)
                            rowCount = (int)Math.Min(Math.Max(st.ColumnInt64(0), 0), int.MaxValue);
                    }
                    if (rowCount <= 0) return false;
                    long addonCached;
                    long allCached;
                    int cachedCount;
                    if (!TryLoadVarPathInventoryRootMeta(conn, out addonCached, out allCached, out cachedCount))
                        return false;
                    return TryFastRejectVarPathInventory(rowCount, addonCached, allCached, cachedCount);
                }
            }
            catch
            {
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

        internal static bool TryLoadRegistryWarmInventorySignature(out string signature)
        {
            signature = null;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    signature = MetaGet(conn, MetaRegistryWarmInvSigKey);
                }
                return !string.IsNullOrEmpty(signature);
            }
            catch
            {
                signature = null;
                return false;
            }
        }

        /// <summary>Warm-restore sig: dedicated meta first, else last gallery rebuild fingerprint.</summary>
        internal static bool TryLoadPackageInventorySignatureForWarmRestore(out string signature)
        {
            if (TryLoadRegistryWarmInventorySignature(out signature)) return true;
            signature = null;
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    signature = MetaGet(conn, "pkg_inv_sig");
                    if (string.IsNullOrEmpty(signature)) return false;
                    string galleryReady = MetaGet(conn, MetaGalleryReadyKey);
                    if (!string.Equals(galleryReady ?? "", "1", StringComparison.Ordinal)) return false;
                }
                return true;
            }
            catch
            {
                signature = null;
                return false;
            }
        }

        internal static void TrySaveRegistryWarmInventorySignature(string signature)
        {
            if (string.IsNullOrEmpty(signature) || !VpbSqlite3.IsAvailable) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                    {
                        st.BindText(1, MetaRegistryWarmInvSigKey);
                        st.BindText(2, signature);
                        st.Step();
                    }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TrySaveRegistryWarmInventorySignature failed: " + ex.Message); } catch { }
            }
        }

        static void ClearRegistryWarmInventorySignature(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            try
            {
                using (var st = conn.Prepare("DELETE FROM meta WHERE k = ?"))
                {
                    st.BindText(1, MetaRegistryWarmInvSigKey);
                    st.Step();
                }
            }
            catch { }
        }
    }
}
