using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VPB
{
    /// <summary>
    /// Plugins float catalog — same SQL hot path as gallery grid (cat_mem JOIN pkg).
    /// Never DeepMaxDirMtime / SafeGetFiles / PackagesByUid on open.
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        internal const string PluginsCategoryName = "Plugins";
        internal const string PluginsFloatLooseCacheKey = "plugins:float_loose|root=Custom/Scripts";
        internal const string CslistChildrenKeyPrefix = "plugins:cslist_children|parent=";

        /// <summary>
        /// Grid-parity catalog: TryQueryGalleryCategoryRows + warm sys_file loose rows (stored sig only).
        /// ThreadPool-safe. Builds lightweight FileEntry like RefreshFilesRoutine bulk path.
        /// </summary>
        internal static bool TryBuildPluginsFloatCatalog(List<FileEntry> outEntries)
        {
            if (outEntries == null) return false;
            outEntries.Clear();
            if (!VpbSqlite3.IsAvailable)
            {
                try { LogUtil.Log("[VPB.PluginsFloat] catalog FAIL sqlite_unavailable"); } catch { }
                return false;
            }

            var sw = Stopwatch.StartNew();
            string reject = null;
            int sqlRows = 0;
            int kept = 0;
            int looseAdded = 0;
            bool usedDirectFallback = false;

            try
            {
                // Must pass category extension — null fails ExtensionSetsEqual → empty float.
                string catExt = null;
                try
                {
                    Gallery g = Gallery.singleton;
                    if (g != null)
                    {
                        Gallery.Category catDef = g.FindCategoryByName(PluginsCategoryName);
                        catExt = catDef.extension;
                    }
                }
                catch { catExt = null; }

                var rows = new List<Row>(512);
                GalleryCategoryQueryStats stats;
                bool ok = TryQueryGalleryCategoryRows(
                    PluginsCategoryName,
                    catExt,
                    null,
                    rows,
                    out stats,
                    0,
                    -1,
                    (string[])null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false,
                    false,
                    null);

                if (!ok)
                {
                    reject = stats.RejectReason ?? "query_false";
                    // Direct cat_mem read — same data grid uses once index ready; skip extension gate.
                    usedDirectFallback = TryReadPluginsCatMemDirect(rows, out reject);
                    if (!usedDirectFallback)
                    {
                        try
                        {
                            LogUtil.Log("[VPB.PluginsFloat] catalog FAIL reject=" + reject
                                + " catExt='" + (catExt ?? "") + "'"
                                + " ms=" + sw.ElapsedMilliseconds);
                        }
                        catch { }
                        return false;
                    }
                }

                sqlRows = rows.Count;
                if (outEntries.Capacity < rows.Count)
                    outEntries.Capacity = rows.Count;

                for (int i = 0; i < rows.Count; i++)
                {
                    Row r = rows[i];
                    string internalPath = r.InternalPath ?? "";
                    if (internalPath.Length == 0) continue;
                    if (!IsPluginScriptPath(internalPath) && !IsPluginScriptPath(r.ListPath))
                        continue;

                    string varHint = r.VarPath ?? "";
                    string listPath = r.ListPath ?? "";
                    if (string.IsNullOrEmpty(listPath))
                    {
                        if (!string.IsNullOrEmpty(varHint))
                            listPath = varHint + ":/" + internalPath;
                    }
                    if (string.IsNullOrEmpty(listPath)) continue;

                    DateTime entryTime = DateTime.MinValue;
                    if (r.LastWriteTicksOrInvalid != long.MinValue)
                    {
                        try { entryTime = DateTime.FromBinary(r.LastWriteTicksOrInvalid); }
                        catch { entryTime = DateTime.MinValue; }
                    }
                    long entrySize = r.PackageSizeOrInvalid != long.MinValue ? r.PackageSizeOrInvalid : 0;

                    outEntries.Add(new VarFileEntry(
                        r.PackageUid,
                        internalPath,
                        entryTime,
                        entrySize,
                        listPath,
                        varHint,
                        r.PackageCreationTicksOrInvalid,
                        r.FirstScannedTicksOrInvalid,
                        null));
                    kept++;
                }

                int beforeLoose = outEntries.Count;
                AppendWarmLoosePluginScripts(outEntries);
                looseAdded = outEntries.Count - beforeLoose;

                sw.Stop();
                try
                {
                    LogUtil.Log("[VPB.PluginsFloat] catalog OK"
                        + " sqlRows=" + sqlRows
                        + " kept=" + kept
                        + " loose=" + looseAdded
                        + " total=" + outEntries.Count
                        + " catExt='" + (catExt ?? "") + "'"
                        + " fallback=" + (usedDirectFallback ? "1" : "0")
                        + (string.IsNullOrEmpty(reject) ? "" : " firstReject=" + reject)
                        + " ms=" + sw.ElapsedMilliseconds);
                }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                outEntries.Clear();
                try
                {
                    LogUtil.LogWarning("[VPB.PluginsFloat] catalog EX: " + (ex != null ? ex.Message : "?")
                        + " ms=" + sw.ElapsedMilliseconds);
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Bypass ExtensionSetsEqual / ready-gate for float when grid query rejects.
        /// Still JOIN pkg for timestamps like bulk path.
        /// </summary>
        private static bool TryReadPluginsCatMemDirect(List<Row> outRows, out string detail)
        {
            detail = null;
            if (outRows == null) return false;
            outRows.Clear();
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    string loadedSel = "0";
                    try { if (PkgHasLoadedColumn(conn)) loadedSel = "ifnull(p.loaded,0)"; } catch { }
                    string sql =
                        "SELECT m.pkg_uid, m.internal_path, m.list_path, p.var_path, p.wtime, p.psize, ifnull(p.ictime, p.pctime), "
                        + loadedSel + ", ifnull(p.first_scanned, 0) "
                        + "FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid WHERE m.category = ?";
                    using (var st = conn.Prepare(sql))
                    {
                        st.BindText(1, PluginsCategoryName);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            Row r;
                            r.ItemUsageKey = null;
                            r.PackageUid = st.ColumnText(0) ?? "";
                            r.InternalPath = st.ColumnText(1) ?? "";
                            r.ListPath = st.ColumnText(2) ?? "";
                            r.VarPath = st.ColumnText(3) ?? "";
                            r.LastWriteTicksOrInvalid = st.ColumnInt64(4);
                            r.PackageSizeOrInvalid = st.ColumnInt64(5);
                            r.PackageCreationTicksOrInvalid = st.ColumnInt64(6);
                            r.ClothingAttrPacked = 0;
                            r.PackageIsLoaded = st.ColumnInt64(7) != 0;
                            r.FirstScannedTicksOrInvalid = st.ColumnInt64(8);
                            r.ItemUsageCount = 0;
                            r.ItemLastUsedBinary = 0;
                            if (r.PackageUid.Length > 0 && r.InternalPath.Length > 0)
                                outRows.Add(r);
                        }
                    }
                }
                detail = "direct_cat_mem rows=" + outRows.Count;
                return outRows.Count > 0;
            }
            catch (Exception ex)
            {
                detail = "direct_ex:" + (ex != null ? ex.Message : "?");
                outRows.Clear();
                return false;
            }
        }

        /// <summary>
        /// Read plugins:custom_scripts (or float_loose) using meta-stored sig only.
        /// No DeepMaxDirMtimeBinary — that walk is why float felt slower than grid.
        /// </summary>
        private static void AppendWarmLoosePluginScripts(List<FileEntry> outEntries)
        {
            if (outEntries == null) return;
            const string root = "Custom/Scripts";
            string fullRoot;
            try { fullRoot = Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/'); }
            catch { fullRoot = root; }

            string primaryKey = "plugins:custom_scripts|root=" + fullRoot + "|exts=cs,cslist,dll";
            if (!TryAppendLooseFromStoredSysCache(primaryKey, outEntries))
                TryAppendLooseFromStoredSysCache(PluginsFloatLooseCacheKey, outEntries);
        }

        private static bool TryAppendLooseFromStoredSysCache(string cacheKey, List<FileEntry> outEntries)
        {
            if (string.IsNullOrEmpty(cacheKey) || outEntries == null) return false;
            try
            {
                string storedSig = null;
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    storedSig = MetaGet(conn, SysSigMetaKey(cacheKey));
                }
                if (string.IsNullOrEmpty(storedSig)) return false;

                var rows = new List<SystemFileRow>(128);
                if (!TryReadSystemFilesForCacheKey(cacheKey, storedSig, rows) || rows.Count == 0)
                    return false;

                for (int i = 0; i < rows.Count; i++)
                {
                    SystemFileRow r = rows[i];
                    if (string.IsNullOrEmpty(r.Path)) continue;
                    if (!IsPluginScriptPath(r.Path)) continue;
                    DateTime wt = DateTime.MinValue;
                    if (r.LastWriteBinaryOrInvalid != long.MinValue)
                    {
                        try { wt = DateTime.FromBinary(r.LastWriteBinaryOrInvalid); }
                        catch { wt = DateTime.MinValue; }
                    }
                    long sz = r.SizeOrInvalid != long.MinValue ? r.SizeOrInvalid : 0;
                    string norm;
                    try { norm = FileManager.NormalizePath(r.Path); }
                    catch { norm = r.Path.Replace('\\', '/'); }
                    outEntries.Add(new SystemFileEntry(norm, wt, sz, exists: true));
                }
                return true;
            }
            catch { return false; }
        }

        private static bool IsPluginScriptPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path.Replace('\\', '/');
            int sep = p.IndexOf(":/", StringComparison.Ordinal);
            string check = p;
            if (sep >= 0 && !(sep == 1 && char.IsLetter(p[0])))
                check = p.Substring(sep + 2);
            string l = check.ToLowerInvariant();
            if (!(l.EndsWith(".cs") || l.EndsWith(".cslist") || l.EndsWith(".dll")))
                return false;
            if (check.IndexOf("Custom/Scripts/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (p.IndexOf("/custom/scripts/", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (p.IndexOf("\\custom\\scripts\\", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (l.StartsWith("custom/scripts/", StringComparison.Ordinal))
                return true;
            return sep >= 0;
        }

        /// <summary>
        /// Rebuild loose Custom/Scripts rows. Disk IO — never call from float open.
        /// </summary>
        internal static void EnsurePluginsFloatLooseCache()
        {
            if (!VpbSqlite3.IsAvailable) return;
            const string root = "Custom/Scripts";
            string sig = "0";
            try
            {
                if (!Directory.Exists(root))
                {
                    TryWriteSystemFilesForCacheKey(PluginsFloatLooseCacheKey, "0", new List<SystemFileRow>());
                    return;
                }
            }
            catch { return; }

            try { sig = DeepMaxDirMtimeBinary(root).ToString(); } catch { sig = "0"; }

            var probe = new List<SystemFileRow>(1);
            if (TryReadSystemFilesForCacheKey(PluginsFloatLooseCacheKey, sig, probe))
                return;

            var rows = new List<SystemFileRow>(256);
            string[] exts = { "cslist", "cs", "dll" };
            var buf = new List<string>(128);
            for (int ei = 0; ei < exts.Length; ei++)
            {
                buf.Clear();
                try { FileManager.SafeGetFiles(root, "*." + exts[ei], buf); } catch { continue; }
                for (int i = 0; i < buf.Count; i++)
                {
                    string p = buf[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    var r = new SystemFileRow();
                    try { r.Path = p.Replace('\\', '/'); } catch { r.Path = p; }
                    r.LastWriteBinaryOrInvalid = long.MinValue;
                    r.SizeOrInvalid = long.MinValue;
                    rows.Add(r);
                }
            }
            try { TryWriteSystemFilesForCacheKey(PluginsFloatLooseCacheKey, sig, rows); } catch { }
        }

        internal static string MakeCslistChildrenCacheKey(string parentKey)
        {
            if (string.IsNullOrEmpty(parentKey)) return CslistChildrenKeyPrefix;
            return CslistChildrenKeyPrefix + parentKey.Replace('\\', '/').ToLowerInvariant();
        }

        internal static string NormalizeCslistParentKey(string pkgUid, string cslistInternalOrPath)
        {
            string ip = (cslistInternalOrPath ?? "").Replace('\\', '/');
            if (!string.IsNullOrEmpty(pkgUid))
                return (pkgUid + ":/" + ip).ToLowerInvariant();
            return ip.ToLowerInvariant();
        }

        internal static bool TryReadAllCslistChildren(Dictionary<string, List<string>> outParentToChildren)
        {
            if (outParentToChildren == null) return false;
            outParentToChildren.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare(
                        "SELECT cache_key, path FROM sys_file WHERE cache_key LIKE 'plugins:cslist_children|parent=%'"))
                    {
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string ck = st.ColumnText(0) ?? "";
                            string child = (st.ColumnText(1) ?? "").Replace('\\', '/').ToLowerInvariant();
                            if (ck.Length <= CslistChildrenKeyPrefix.Length || child.Length == 0) continue;
                            string parent = ck.Substring(CslistChildrenKeyPrefix.Length);
                            List<string> list;
                            if (!outParentToChildren.TryGetValue(parent, out list) || list == null)
                            {
                                list = new List<string>(8);
                                outParentToChildren[parent] = list;
                            }
                            list.Add(child);
                        }
                    }
                }
                return true;
            }
            catch
            {
                outParentToChildren.Clear();
                return false;
            }
        }

        internal static bool TryReadCslistChildrenForParent(string parentKey, List<string> outChildren)
        {
            if (outChildren == null) return false;
            outChildren.Clear();
            if (string.IsNullOrEmpty(parentKey) || !VpbSqlite3.IsAvailable) return false;
            string cacheKey = MakeCslistChildrenCacheKey(parentKey);
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT path FROM sys_file WHERE cache_key=?"))
                    {
                        st.BindText(1, cacheKey);
                        while (st.Step() == VpbSqlite3.SqliteRow)
                        {
                            string p = (st.ColumnText(0) ?? "").Replace('\\', '/').ToLowerInvariant();
                            if (p.Length > 0) outChildren.Add(p);
                        }
                    }
                }
                return true;
            }
            catch
            {
                outChildren.Clear();
                return false;
            }
        }

        internal static void WriteCslistChildrenForParent(string parentKey, List<string> children)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(parentKey)) return;
            string ck = MakeCslistChildrenCacheKey(parentKey);
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var del = conn.Prepare("DELETE FROM sys_file WHERE cache_key = ?"))
                    {
                        del.BindText(1, ck);
                        del.Step();
                    }
                    if (children == null || children.Count == 0) return;
                    using (var ins = conn.Prepare(
                        "INSERT OR REPLACE INTO sys_file(cache_key,path,wtime,size) VALUES(?,?,?,?)"))
                    {
                        for (int i = 0; i < children.Count; i++)
                        {
                            string child = children[i];
                            if (string.IsNullOrEmpty(child)) continue;
                            ins.BindText(1, ck);
                            ins.BindText(2, child.Replace('\\', '/').ToLowerInvariant());
                            ins.BindText(3, long.MinValue.ToString());
                            ins.BindText(4, long.MinValue.ToString());
                            ins.Step();
                            ins.Reset();
                        }
                    }
                }
            }
            catch { }
        }
    }
}
