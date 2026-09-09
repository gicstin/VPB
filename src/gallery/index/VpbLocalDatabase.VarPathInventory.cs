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
        const string MetaVarPathInventoryAddonRootSigKey = "var_path_inventory_addon_root_sig_v2";
        const string MetaVarPathInventoryAllRootSigKey = "var_path_inventory_all_root_sig_v2";
        const string MetaRegistryWarmInvSigKey = "registry_warm_inv_sig_v1";

        const string ScanRootAddon = "AddonPackages";
        const string ScanRootAll = "AllPackages";

        struct ScanRootSignature
        {
            internal long MaxMtimeBinary;
            internal int VarCount;
            internal bool SawUnresolvableLink;
            internal bool RootIsLink;

            internal string ToMetaValue()
            {
                return MaxMtimeBinary.ToString() + ":" + VarCount.ToString();
            }
        }

        static readonly object s_ScanRootSigLock = new object();
        static readonly Dictionary<string, KeyValuePair<long, ScanRootSignature>> s_ScanRootSigCache =
            new Dictionary<string, KeyValuePair<long, ScanRootSignature>>(StringComparer.OrdinalIgnoreCase);
        static readonly long ScanRootSigCacheTtlTicks = TimeSpan.FromSeconds(3).Ticks;

        /// <summary>
        /// Drops the in-process scan-root signature and deep-mtime TTL caches so a user-forced rescan
        /// re-probes the filesystem instead of replaying a value sampled seconds before their file drop.
        /// </summary>
        internal static void InvalidateScanRootSignatureCaches()
        {
            lock (s_ScanRootSigLock) { s_ScanRootSigCache.Clear(); }
            try { ClearDeepDirMtimeCache(); } catch { }
        }

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

        /// <summary>
        /// Deep max directory mtime PLUS live .var count for a scan root. The mtime alone cannot be trusted
        /// as an addition detector: a junctioned/symlinked root reports the link node's frozen timestamp, and
        /// timestamp-preserving copy tools, network shares and exFAT granularity all defeat it. The count is
        /// what actually catches "a file appeared"; the walk enumerates directories only, so it costs far less
        /// than the per-file stat pass the cache exists to avoid.
        /// </summary>
        static bool TryComputeScanRootSignature(string root, out ScanRootSignature sig)
        {
            sig = new ScanRootSignature();
            if (string.IsNullOrEmpty(root)) return false;

            long nowTicks = DateTime.UtcNow.Ticks;
            lock (s_ScanRootSigLock)
            {
                KeyValuePair<long, ScanRootSignature> cached;
                if (s_ScanRootSigCache.TryGetValue(root, out cached)
                    && (nowTicks - cached.Key) < ScanRootSigCacheTtlTicks)
                {
                    sig = cached.Value;
                    return true;
                }
            }

            try
            {
                if (!Directory.Exists(root))
                {
                    lock (s_ScanRootSigLock)
                        s_ScanRootSigCache[root] = new KeyValuePair<long, ScanRootSignature>(nowTicks, sig);
                    return true;
                }

                long rootMtime;
                bool rootLink;
                if (FileManager.TryGetDirectoryLastWriteBinaryFollowingLinks(root, out rootMtime, out rootLink))
                {
                    sig.RootIsLink = rootLink;
                    if (rootMtime > sig.MaxMtimeBinary) sig.MaxMtimeBinary = rootMtime;
                }
                else
                {
                    sig.RootIsLink = true;
                    sig.SawUnresolvableLink = true;
                }

                sig.VarCount += CountVarFilesInDirectory(root);

                var dirs = new List<string>(256);
                try { FileManager.SafeGetDirectories(root, "*", dirs); }
                catch { }

                for (int i = 0; i < dirs.Count; i++)
                {
                    string dir = dirs[i];
                    long m;
                    bool link;
                    if (FileManager.TryGetDirectoryLastWriteBinaryFollowingLinks(dir, out m, out link))
                    {
                        if (m > sig.MaxMtimeBinary) sig.MaxMtimeBinary = m;
                    }
                    else
                    {
                        sig.SawUnresolvableLink = true;
                    }
                    sig.VarCount += CountVarFilesInDirectory(dir);
                }
            }
            catch
            {
                return false;
            }

            lock (s_ScanRootSigLock)
            {
                if (s_ScanRootSigCache.Count >= 32 && !s_ScanRootSigCache.ContainsKey(root))
                    s_ScanRootSigCache.Clear();
                s_ScanRootSigCache[root] = new KeyValuePair<long, ScanRootSignature>(nowTicks, sig);
            }
            return true;
        }

        static int CountVarFilesInDirectory(string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "*.var");
                return files != null ? files.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        static bool TryLoadVarPathInventoryRootMeta(
            VpbSqlite3.Connection conn,
            out string addonRootSig,
            out string allRootSig,
            out int savedCount)
        {
            addonRootSig = null;
            allRootSig = null;
            savedCount = 0;
            if (conn == null) return false;
            try
            {
                using (var st = conn.Prepare(
                    "SELECT k, v FROM meta WHERE k IN (?, ?, ?)"))
                {
                    st.BindText(1, MetaVarPathInventoryCountKey);
                    st.BindText(2, MetaVarPathInventoryAddonRootSigKey);
                    st.BindText(3, MetaVarPathInventoryAllRootSigKey);
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
                        else if (key == MetaVarPathInventoryAddonRootSigKey)
                            addonRootSig = val;
                        else if (key == MetaVarPathInventoryAllRootSigKey)
                            allRootSig = val;
                    }
                }
                return savedCount > 0
                    && !string.IsNullOrEmpty(addonRootSig)
                    && !string.IsNullOrEmpty(allRootSig);
            }
            catch
            {
                return false;
            }
        }

        static void SaveVarPathInventoryRootMeta(VpbSqlite3.Connection conn, int pathCount)
        {
            if (conn == null || pathCount <= 0) return;
            ScanRootSignature addonSig;
            ScanRootSignature allSig;
            if (!TryComputeScanRootSignature(ScanRootAddon, out addonSig)) return;
            if (!TryComputeScanRootSignature(ScanRootAll, out allSig)) return;
            if (addonSig.SawUnresolvableLink || allSig.SawUnresolvableLink) return;

            using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
            {
                st.BindText(1, MetaVarPathInventoryCountKey);
                st.BindText(2, pathCount.ToString());
                st.Step();
                st.Reset();
                st.BindText(1, MetaVarPathInventoryAddonRootSigKey);
                st.BindText(2, addonSig.ToMetaValue());
                st.Step();
                st.Reset();
                st.BindText(1, MetaVarPathInventoryAllRootSigKey);
                st.BindText(2, allSig.ToMetaValue());
                st.Step();
            }
        }

        static bool TryFastRejectVarPathInventory(
            int rowCount,
            string cachedAddonRootSig,
            string cachedAllRootSig,
            int cachedCount,
            out string rejectDetail)
        {
            rejectDetail = "";
            if (rowCount <= 0) return false;
            if (cachedCount != rowCount)
            {
                rejectDetail = "meta_count=" + cachedCount + " rows=" + rowCount;
                return false;
            }
            if (string.IsNullOrEmpty(cachedAddonRootSig) || string.IsNullOrEmpty(cachedAllRootSig))
            {
                rejectDetail = "no_v2_root_sig";
                return false;
            }

            ScanRootSignature addonNow;
            ScanRootSignature allNow;
            if (!TryComputeScanRootSignature(ScanRootAddon, out addonNow)
                || !TryComputeScanRootSignature(ScanRootAll, out allNow))
            {
                rejectDetail = "root_sig_probe_failed";
                return false;
            }

            if (addonNow.SawUnresolvableLink || allNow.SawUnresolvableLink)
            {
                rejectDetail = "unresolvable_reparse_point addonLink=" + (addonNow.RootIsLink ? "1" : "0")
                    + " allLink=" + (allNow.RootIsLink ? "1" : "0");
                return false;
            }

            string addonNowSig = addonNow.ToMetaValue();
            string allNowSig = allNow.ToMetaValue();
            if (!string.Equals(addonNowSig, cachedAddonRootSig, StringComparison.Ordinal))
            {
                rejectDetail = "addon sig now=" + addonNowSig + " cached=" + cachedAddonRootSig
                    + " link=" + (addonNow.RootIsLink ? "1" : "0");
                return false;
            }
            if (!string.Equals(allNowSig, cachedAllRootSig, StringComparison.Ordinal))
            {
                rejectDetail = "all sig now=" + allNowSig + " cached=" + cachedAllRootSig
                    + " link=" + (allNow.RootIsLink ? "1" : "0");
                return false;
            }

            rejectDetail = "addon=" + addonNowSig + " link=" + (addonNow.RootIsLink ? "1" : "0")
                + " all=" + allNowSig + " link=" + (allNow.RootIsLink ? "1" : "0");
            return true;
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

                string cachedAddonRootSig = null;
                string cachedAllRootSig = null;
                int cachedCount = 0;
                using (var connMeta = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(connMeta);
                    TryLoadVarPathInventoryRootMeta(connMeta, out cachedAddonRootSig, out cachedAllRootSig, out cachedCount);
                }

                string fastRejectDetail;
                bool fastAccept = TryFastRejectVarPathInventory(
                    rows.Count, cachedAddonRootSig, cachedAllRootSig, cachedCount, out fastRejectDetail);
                if (fastAccept)
                {
                    paths = new List<string>(rows.Count);
                    for (int i = 0; i < rows.Count; i++)
                        paths.Add(rows[i].Path);
                    sw.Stop();
                    try
                    {
                        LogUtil.Log("Var path inventory cache HIT paths=" + paths.Count
                            + " validate_ms=" + sw.ElapsedMilliseconds
                            + " mode=root_sig_fast_reject " + fastRejectDetail);
                    }
                    catch { }
                    return true;
                }

                try
                {
                    LogUtil.Log("Var path inventory root sig mismatch -> full validate | " + fastRejectDetail);
                }
                catch { }

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
                    string addonCached;
                    string allCached;
                    int cachedCount;
                    if (!TryLoadVarPathInventoryRootMeta(conn, out addonCached, out allCached, out cachedCount))
                        return false;
                    string detail;
                    return TryFastRejectVarPathInventory(rowCount, addonCached, allCached, cachedCount, out detail);
                }
            }
            catch
            {
                return false;
            }
        }

        static readonly object s_MissingVarPathLock = new object();
        static List<string> s_MissingVarPaths;

        /// <summary>
        /// Records a cached inventory path whose .var file is gone (moved to InvalidPackages / deleted
        /// after the inventory was saved). Callable from scan worker threads; SQLite work happens in
        /// <see cref="FlushMissingVarPathPrune"/>. Without this the stale row is restored on every launch,
        /// registers a ghost package that can never be classified, and keeps the gallery index incomplete.
        /// </summary>
        internal static void NoteMissingVarPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (s_MissingVarPathLock)
            {
                if (s_MissingVarPaths == null) s_MissingVarPaths = new List<string>(16);
                s_MissingVarPaths.Add(path);
            }
        }

        /// <summary>Deletes noted dead paths from <c>pkg_var_path</c> and resyncs the cached row count. Returns rows removed.</summary>
        internal static int FlushMissingVarPathPrune()
        {
            List<string> pending;
            lock (s_MissingVarPathLock)
            {
                if (s_MissingVarPaths == null || s_MissingVarPaths.Count == 0) return 0;
                pending = s_MissingVarPaths;
                s_MissingVarPaths = null;
            }
            if (!VpbSqlite3.IsAvailable) return 0;

            int removed = 0;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsureVarPathInventorySchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        // Inventory rows keep the raw enumeration path (backslashes on Windows) while
                        // VarPackage.Path is cleaned to forward slashes — match both spellings.
                        using (var del = conn.Prepare("DELETE FROM pkg_var_path WHERE path = ? OR path = ?"))
                        {
                            for (int i = 0; i < pending.Count; i++)
                            {
                                string p = pending[i];
                                if (string.IsNullOrEmpty(p)) continue;
                                del.BindText(1, p.Replace('\\', '/'));
                                del.BindText(2, p.Replace('/', '\\'));
                                del.Step();
                                del.Reset();
                                removed++;
                            }
                        }
                        int rowCount = (int)Math.Max(ScalarInt64(conn, "SELECT COUNT(*) FROM pkg_var_path;"), 0);
                        if (rowCount > 0)
                        {
                            using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                            {
                                st.BindText(1, MetaVarPathInventoryCountKey);
                                st.BindText(2, rowCount.ToString());
                                st.Step();
                            }
                        }
                        conn.ExecUtf8("COMMIT;");
                        try
                        {
                            LogUtil.Log("Var path inventory pruned dead paths=" + removed + " rows_left=" + rowCount);
                        }
                        catch { }
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
                try { LogUtil.LogWarning("[VPB] FlushMissingVarPathPrune failed: " + ex.Message); } catch { }
                return 0;
            }
            return removed;
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
