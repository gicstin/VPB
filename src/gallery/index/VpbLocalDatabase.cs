using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Local SQLite database for VPB: gallery VAR index (category membership, package rows, rebuild metadata).
    /// Rebuilt after package scans; optional fast path in <see cref="GalleryPanel"/> when signatures match.
    /// </summary>
    internal static class VpbLocalDatabase
    {
        internal struct Row
        {
            public string PackageUid;
            public string InternalPath;
            /// <summary>Precomputed <see cref="VarFileEntry.Path"/> at index rebuild (empty for legacy DB rows until rebuild).</summary>
            public string ListPath;
            /// <summary>Last known .var path (<c>pkg.var_path</c>) for lazy package resolution if the file moved.</summary>
            public string VarPath;
            public long LastWriteTicksOrInvalid;
            public long PackageSizeOrInvalid;
            public long PackageCreationTicksOrInvalid;
            /// <summary>
            /// Packed gender / preset / decal / kind for Clothing gallery subfilters (see <see cref="ClothingPackedAttrMatchesSubfilter"/>).
            /// Bit 31 set when populated at index rebuild; 0 or unset column means caller may fall back to path classification.
            /// </summary>
            public int ClothingAttrPacked;
            /// <summary>
            /// From <c>pkg.loaded</c>: package is &quot;loaded&quot; when its .var lives under <c>AddonPackages/</c>, or under <c>Custom/</c> / <c>Saves/</c> (always loaded).
            /// </summary>
            public bool PackageIsLoaded;
        }

        internal struct PackageRow
        {
            public string PackageUid;
            public string VarPath;
            public long LastWriteTicksOrInvalid;
            public long PackageSizeOrInvalid;
            public long PackageCreationTicksOrInvalid;
            public bool PackageIsLoaded;
        }

        /// <summary>High bit: row has <see cref="ClothingAttrPacked"/> from index rebuild (fast clothing subfilter path).</summary>
        internal const int ClothingAttrPresentFlag = unchecked((int)0x80000000);

        private const int SchemaVersion = 7;

        private static readonly object s_Sync = new object();
        private static volatile bool s_RebuildScheduled;
        private static volatile bool s_RebuildRunning;
        private static long s_ReadyScanBinary = long.MinValue;
        private static string s_ReadyCategoriesSig;
        private static string s_LastError;
        private static bool s_LoggedSqliteUnavailable;
        private static bool s_LoggedEmptyCategoriesDb;
        private static long s_CachedInvScanBinary = long.MinValue;
        private static int s_CachedInvPkgCount;
        private static string s_CachedInvSig;

        private const string DatabaseFileName = "VpbLocalDatabase.sqlite3";
        private const string LegacyDatabaseFileName = "GalleryVarFileIndex.sqlite3";

        /// <summary>Prefer <see cref="DatabaseFileName"/>; one-time rename from <see cref="LegacyDatabaseFileName"/> when the new file is absent.</summary>
        private static string ResolveDatabaseFilePath(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                directory = Path.GetFullPath(Path.Combine("Cache", "VPB"));
            try
            {
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
            }
            catch { }

            string current = Path.Combine(directory, DatabaseFileName);
            string legacy = Path.Combine(directory, LegacyDatabaseFileName);
            try
            {
                if (!File.Exists(current) && File.Exists(legacy))
                    File.Move(legacy, current);
            }
            catch { }
            return current;
        }

        private static string DbPath
        {
            get
            {
                try
                {
                    string dir = VpbSqlite3.GetCacheVpbDirectoryOrFallback();
                    if (string.IsNullOrEmpty(dir))
                        dir = Path.GetFullPath(Path.Combine("Cache", "VPB"));
                    return ResolveDatabaseFilePath(dir);
                }
                catch
                {
                    return ResolveDatabaseFilePath(Application.temporaryCachePath);
                }
            }
        }

        internal static string GetLocalDatabasePathForDiagnostics()
        {
            try { return Path.GetFullPath(DbPath); }
            catch { return DbPath; }
        }

        private static long ScalarInt64(VpbSqlite3.Connection conn, string sqlUtf8)
        {
            using (var st = conn.Prepare(sqlUtf8))
            {
                int rc = st.Step();
                if (rc != VpbSqlite3.SqliteRow) return -1;
                string t = st.ColumnText(0);
                long n;
                if (long.TryParse(t, out n)) return n;
                return -1;
            }
        }

        private static string MetaGet(VpbSqlite3.Connection conn, string key)
        {
            try
            {
                using (var st = conn.Prepare("SELECT v FROM meta WHERE k=?"))
                {
                    st.BindText(1, key);
                    if (st.Step() != VpbSqlite3.SqliteRow) return "";
                    return st.ColumnText(0) ?? "";
                }
            }
            catch
            {
                return "";
            }
        }

        private static string FormatByteSize(long bytes)
        {
            if (bytes < 0) return "?";
            if (bytes < 1024L) return bytes + " B";
            if (bytes < 1024L * 1024L) return (bytes / 1024.0).ToString("0.0") + " KiB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.00") + " MiB";
        }

        private static void LogLocalDatabaseReadyDetails(
            VpbSqlite3.Connection conn,
            string dbPath,
            long rebuildElapsedMs,
            int categoryDefCount,
            int snapshotPackageCount)
        {
            try
            {
                long nPkg = ScalarInt64(conn, "SELECT COUNT(*) FROM pkg;");
                long nMem = ScalarInt64(conn, "SELECT COUNT(*) FROM cat_mem;");
                long nCatDistinct = ScalarInt64(conn, "SELECT COUNT(DISTINCT category) FROM cat_mem;");

                var topSb = new StringBuilder(384);
                try
                {
                    using (var st = conn.Prepare("SELECT category, COUNT(*) FROM cat_mem GROUP BY category ORDER BY COUNT(*) DESC LIMIT 10"))
                    {
                        for (;;)
                        {
                            int rc = st.Step();
                            if (rc == VpbSqlite3.SqliteDone) break;
                            if (rc != VpbSqlite3.SqliteRow) break;
                            if (topSb.Length > 0) topSb.Append("; ");
                            topSb.Append(st.ColumnText(0)).Append("=").Append(st.ColumnText(1));
                        }
                    }
                }
                catch { }

                string journal = "";
                try
                {
                    using (var st = conn.Prepare("PRAGMA journal_mode;"))
                    {
                        if (st.Step() == VpbSqlite3.SqliteRow)
                            journal = st.ColumnText(0) ?? "";
                    }
                }
                catch { }

                long fileLen = -1;
                try
                {
                    if (File.Exists(dbPath))
                        fileLen = new FileInfo(dbPath).Length;
                }
                catch { }

                string ver = MetaGet(conn, "schema_version");
                string scanBin = MetaGet(conn, "scan_binary");
                string catSigStored = MetaGet(conn, "categories_sig");
                int sigLen = catSigStored != null ? catSigStored.Length : 0;

                LogUtil.Log(
                    "[VPB] VpbLocalDatabase: SQLite ready | path=" + dbPath
                    + " | file=" + FormatByteSize(fileLen)
                    + " | journal_mode=" + journal
                    + " | rebuild_ms=" + rebuildElapsedMs
                    + " | snapshot_var_pkgs=" + snapshotPackageCount
                    + " | category_defs=" + categoryDefCount
                    + " | rows_pkg=" + nPkg
                    + " | rows_cat_mem=" + nMem
                    + " | distinct_categories_indexed=" + nCatDistinct
                    + " | meta_schema_version=" + ver
                    + " | meta_scan_binary=" + scanBin
                    + " | meta_categories_sig_len=" + sigLen
                    + " | top_cat_mem_counts=" + (topSb.Length > 0 ? topSb.ToString() : "(none)"));
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] VpbLocalDatabase: SQLite stats query failed: " + ex.Message); } catch { }
            }
        }

        private static void TryAddColumnIgnoreFailure(VpbSqlite3.Connection conn, string alterSql)
        {
            try
            {
                conn.ExecUtf8(alterSql);
            }
            catch
            {
            }
        }

        private static void EnsureSchema(VpbSqlite3.Connection conn)
        {
            conn.ExecUtf8("PRAGMA journal_mode=WAL;");
            conn.ExecUtf8("PRAGMA synchronous=NORMAL;");
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT);" +
                "CREATE TABLE IF NOT EXISTS pkg (uid TEXT PRIMARY KEY, creator TEXT, wtime INTEGER, psize INTEGER);" +
                "CREATE TABLE IF NOT EXISTS cat_mem (category TEXT NOT NULL, pkg_uid TEXT NOT NULL, internal_path TEXT NOT NULL, PRIMARY KEY(category, pkg_uid, internal_path));" +
                "CREATE TABLE IF NOT EXISTS pkg_dep (src_uid TEXT NOT NULL, dep_uid TEXT NOT NULL, PRIMARY KEY(src_uid, dep_uid));" +
                "CREATE TABLE IF NOT EXISTS sys_file (cache_key TEXT NOT NULL, path TEXT NOT NULL, wtime INTEGER, size INTEGER, PRIMARY KEY(cache_key, path));" +
                "CREATE TABLE IF NOT EXISTS cat_filter_state (panel_id TEXT NOT NULL, cat_key TEXT NOT NULL, state_json TEXT NOT NULL, PRIMARY KEY(panel_id, cat_key));" +
                "CREATE INDEX IF NOT EXISTS idx_cm_cat ON cat_mem(category);" +
                "CREATE INDEX IF NOT EXISTS idx_cm_pkg ON cat_mem(pkg_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_pd_src ON pkg_dep(src_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_pd_dep ON pkg_dep(dep_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_sf_key ON sys_file(cache_key);" +
                "CREATE INDEX IF NOT EXISTS idx_cfs_panel ON cat_filter_state(panel_id);");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN var_path TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE cat_mem ADD COLUMN list_path TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN pctime TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE cat_mem ADD COLUMN cloth_attr TEXT;");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg ADD COLUMN loaded INTEGER;");
        }

        internal static void TrySaveCategoryFilterState(string panelId, string catKey, string stateJson)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (string.IsNullOrEmpty(panelId) || string.IsNullOrEmpty(catKey) || stateJson == null) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("INSERT OR REPLACE INTO cat_filter_state(panel_id,cat_key,state_json) VALUES(?,?,?)"))
                    {
                        st.BindText(1, panelId);
                        st.BindText(2, catKey);
                        st.BindText(3, stateJson);
                        st.Step();
                    }
                }
            }
            catch { }
        }

        internal static bool TryLoadCategoryFilterState(string panelId, string catKey, out string stateJson)
        {
            stateJson = null;
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(panelId) || string.IsNullOrEmpty(catKey)) return false;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("SELECT state_json FROM cat_filter_state WHERE panel_id=? AND cat_key=?"))
                    {
                        st.BindText(1, panelId);
                        st.BindText(2, catKey);
                        if (st.Step() != VpbSqlite3.SqliteRow) return false;
                        stateJson = st.ColumnText(0);
                        return stateJson != null;
                    }
                }
            }
            catch { return false; }
        }

        internal static void TryDeleteAllCategoryFilterStates(string panelId)
        {
            if (!VpbSqlite3.IsAvailable || string.IsNullOrEmpty(panelId)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("DELETE FROM cat_filter_state WHERE panel_id=?"))
                    {
                        st.BindText(1, panelId);
                        st.Step();
                    }
                }
            }
            catch { }
        }

        internal struct SystemFileRow
        {
            public string Path;
            public long LastWriteBinaryOrInvalid;
            public long SizeOrInvalid;
        }

        private static string SysSigMetaKey(string cacheKey)
        {
            // meta.k is a primary key; keep it short and stable even if cacheKey is long.
            return "sys_sig:" + HashFnv1a64Hex(cacheKey ?? "");
        }

        private static string HashFnv1a64Hex(string s)
        {
            unchecked
            {
                const ulong Offset = 14695981039346656037UL;
                const ulong Prime = 1099511628211UL;
                ulong h = Offset;
                if (!string.IsNullOrEmpty(s))
                {
                    for (int i = 0; i < s.Length; i++)
                    {
                        // UTF-16 code units; stable within this codebase.
                        h ^= (ushort)s[i];
                        h *= Prime;
                    }
                }
                return h.ToString("x16");
            }
        }

        internal static bool TryReadSystemFilesForCacheKey(string cacheKey, string expectedSig, List<SystemFileRow> outRows)
        {
            outRows.Clear();
            if (!VpbSqlite3.IsAvailable) return false;
            if (string.IsNullOrEmpty(cacheKey)) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    // sys_file caches are independent of the gallery VAR index "ready" state.
                    EnsureSchema(conn);
                    string storedSig = MetaGet(conn, SysSigMetaKey(cacheKey));
                    if (!string.Equals(storedSig ?? "", expectedSig ?? "", StringComparison.Ordinal))
                        return false;

                    using (var st = conn.Prepare("SELECT path, wtime, size FROM sys_file WHERE cache_key = ?"))
                    {
                        st.BindText(1, cacheKey);
                        int step;
                        while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                        {
                            SystemFileRow r;
                            r.Path = st.ColumnText(0) ?? "";
                            r.LastWriteBinaryOrInvalid = long.MinValue;
                            r.SizeOrInvalid = long.MinValue;
                            long wt, sz;
                            string wtxt = st.ColumnText(1);
                            string sztxt = st.ColumnText(2);
                            if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wt)) r.LastWriteBinaryOrInvalid = wt;
                            if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out sz)) r.SizeOrInvalid = sz;
                            if (!string.IsNullOrEmpty(r.Path)) outRows.Add(r);
                        }
                    }
                }
                return true;
            }
            catch
            {
                outRows.Clear();
                return false;
            }
        }

        internal static void TryWriteSystemFilesForCacheKey(string cacheKey, string sig, List<SystemFileRow> rows)
        {
            if (!VpbSqlite3.IsAvailable) return;
            if (string.IsNullOrEmpty(cacheKey)) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        using (var del = conn.Prepare("DELETE FROM sys_file WHERE cache_key = ?"))
                        {
                            del.BindText(1, cacheKey);
                            del.Step();
                        }
                        if (rows != null && rows.Count > 0)
                        {
                            using (var ins = conn.Prepare("INSERT OR REPLACE INTO sys_file(cache_key,path,wtime,size) VALUES(?,?,?,?)"))
                            {
                                for (int i = 0; i < rows.Count; i++)
                                {
                                    var r = rows[i];
                                    if (string.IsNullOrEmpty(r.Path)) continue;
                                    ins.BindText(1, cacheKey);
                                    ins.BindText(2, r.Path);
                                    ins.BindText(3, r.LastWriteBinaryOrInvalid.ToString());
                                    ins.BindText(4, r.SizeOrInvalid.ToString());
                                    ins.Step();
                                    ins.Reset();
                                }
                            }
                        }
                        using (var up = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                        {
                            up.BindText(1, SysSigMetaKey(cacheKey));
                            up.BindText(2, sig ?? "");
                            up.Step();
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
            catch { }
        }

        private static string NormalizeDependencyUidOrPath(string depUidOrPath)
        {
            if (string.IsNullOrEmpty(depUidOrPath)) return "";
            try
            {
                string d = depUidOrPath.Replace('\\', '/');
                int lastSlash = d.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < d.Length) d = d.Substring(lastSlash + 1);
                if (d.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    d = d.Substring(0, d.Length - 4);
                return d ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 1 = loaded: .var under <c>AddonPackages/</c>, or under <c>Custom/</c> / <c>Saves/</c> (treated as always loaded / not AllPackages-style repo).
        /// 0 = unloaded (e.g. <c>AllPackages/</c>, <c>InvalidPackages/</c>, or other roots).
        /// </summary>
        internal static int ComputePackageLoadedFlagFromVarPath(string varPath)
        {
            if (string.IsNullOrEmpty(varPath)) return 0;
            string p = varPath.Replace('\\', '/');
            if (p.Length >= 7 && p.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase)) return 1;
            if (p.Length >= 6 && p.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase)) return 1;
            if (p.Length >= 15 && p.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        /// <summary>
        /// After AllPackages ↔ AddonPackages moves, update <c>pkg.var_path</c> and <c>pkg.loaded</c> for the given UIDs (no full index rebuild).
        /// </summary>
        internal static void TryUpdatePkgPathAndLoadedForUids(ICollection<string> packageUids)
        {
            if (!VpbSqlite3.IsAvailable || packageUids == null || packageUids.Count == 0) return;
            VarPackage[] snapshot;
            try
            {
                lock (FileManager.packagesLock)
                {
                    if (FileManager.PackagesByUid == null) return;
                    var list = new List<VarPackage>(packageUids.Count);
                    foreach (string uid in packageUids)
                    {
                        if (string.IsNullOrEmpty(uid)) continue;
                        VarPackage p;
                        if (FileManager.PackagesByUid.TryGetValue(uid, out p) && p != null)
                            list.Add(p);
                    }
                    snapshot = list.ToArray();
                }
            }
            catch
            {
                return;
            }
            if (snapshot == null || snapshot.Length == 0) return;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    using (var st = conn.Prepare("UPDATE pkg SET var_path=?, loaded=? WHERE uid=?"))
                    {
                        for (int i = 0; i < snapshot.Length; i++)
                        {
                            VarPackage pkg = snapshot[i];
                            if (pkg == null) continue;
                            string uid = pkg.Uid ?? "";
                            if (uid.Length == 0) continue;
                            string vp = pkg.Path ?? "";
                            int ld = ComputePackageLoadedFlagFromVarPath(vp);
                            st.BindText(1, vp);
                            st.BindText(2, ld.ToString());
                            st.BindText(3, uid);
                            st.Step();
                            st.Reset();
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>Precomputes packed clothing metadata once per VAR row at index rebuild (mirrors VAR branch of <see cref="GalleryPanel.PassesClothingGalleryFiltersForPath"/>).</summary>
        internal static int PackClothingGalleryAttrForVarListPath(string listPath)
        {
            if (string.IsNullOrEmpty(listPath)) return 0;
            string p = listPath;
            int lastDot = p.LastIndexOf('.');
            string ext = (lastDot >= 0 && lastDot < p.Length - 1) ? p.Substring(lastDot + 1) : "";
            bool isPreset = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);

            ClothingLoadingUtils.ResourceKind k;
            ClothingLoadingUtils.ResourceGender g;
            ClothingLoadingUtils.ClassifyClothingHairPath(p, out k, out g);
            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(p);

            return ClothingAttrPresentFlag
                | ((int)k & 0xF)
                | (((int)g & 0xF) << 4)
                | (isPreset ? 0x100 : 0)
                | (isDecal ? 0x200 : 0);
        }

        /// <summary>Fast clothing subfilter test using <see cref="PackClothingGalleryAttrForVarListPath"/> output (no path parsing).</summary>
        internal static bool ClothingPackedAttrMatchesSubfilter(int packed, GalleryPanel.ClothingSubfilter clothingSubfilter)
        {
            if (clothingSubfilter == 0) return true;

            int kind = packed & 0xF;
            int gender = (packed >> 4) & 0xF;
            bool isPreset = (packed & 0x100) != 0;
            bool isDecal = (packed & 0x200) != 0;

            if (kind != (int)ClothingLoadingUtils.ResourceKind.Clothing) return false;

            const GalleryPanel.ClothingSubfilter Real = GalleryPanel.ClothingSubfilter.RealClothing;
            const GalleryPanel.ClothingSubfilter Dec = GalleryPanel.ClothingSubfilter.Decals;
            const GalleryPanel.ClothingSubfilter Pre = GalleryPanel.ClothingSubfilter.Presets;
            const GalleryPanel.ClothingSubfilter Cus = GalleryPanel.ClothingSubfilter.Custom;
            const GalleryPanel.ClothingSubfilter Itm = GalleryPanel.ClothingSubfilter.Items;
            const GalleryPanel.ClothingSubfilter Mal = GalleryPanel.ClothingSubfilter.Male;
            const GalleryPanel.ClothingSubfilter Fem = GalleryPanel.ClothingSubfilter.Female;

            bool wantsRealType = ((clothingSubfilter & (Real | Pre | Cus | Itm | Mal | Fem)) != 0);
            bool wantsDecalType = ((clothingSubfilter & Dec) != 0);

            bool typeExplicit = ((clothingSubfilter & (Real | Dec)) != 0);
            if (typeExplicit)
            {
                bool okType = (!isDecal && (clothingSubfilter & Real) != 0)
                    || (isDecal && (clothingSubfilter & Dec) != 0);
                if (!okType) return false;
            }
            else
            {
                if (wantsRealType && isDecal && !wantsDecalType) return false;
            }

            bool wantsPresets = (clothingSubfilter & Pre) != 0;
            bool wantsCustom = (clothingSubfilter & Cus) != 0;
            if (wantsPresets) { if (!isPreset) return false; }
            if (wantsCustom) return false; // VAR rows: isCustomLoose is always false
            if ((clothingSubfilter & Itm) != 0) { if (isPreset) return false; }
            if ((clothingSubfilter & Mal) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Male) return false; }
            if ((clothingSubfilter & Fem) != 0) { if (gender != (int)ClothingLoadingUtils.ResourceGender.Female) return false; }

            return true;
        }

        /// <summary>
        /// Extra <c>AND ...</c> fragment for <see cref="TryQueryGalleryCategoryRows"/> when Clothing + active subfilter + schema has <c>cloth_attr</c>.
        /// Uses the same packing as <see cref="PackClothingGalleryAttrForVarListPath"/> (bit 31 present, kind, gender nibble, 0x100 preset, 0x200 decal).
        /// </summary>
        private static string BuildClothingSubfilterSqlAnd(
            VpbSqlite3.Connection conn,
            string categoryTitle,
            GalleryPanel.ClothingSubfilter f)
        {
            if (f == 0) return "";
            if (!string.Equals(categoryTitle, "Clothing", StringComparison.OrdinalIgnoreCase)) return "";

            string metaVer = MetaGet(conn, "schema_version");
            int mv;
            if (string.IsNullOrEmpty(metaVer) || !int.TryParse(metaVer, out mv) || mv < SchemaVersion) return "";

            const GalleryPanel.ClothingSubfilter Real = GalleryPanel.ClothingSubfilter.RealClothing;
            const GalleryPanel.ClothingSubfilter Dec = GalleryPanel.ClothingSubfilter.Decals;
            const GalleryPanel.ClothingSubfilter Pre = GalleryPanel.ClothingSubfilter.Presets;
            const GalleryPanel.ClothingSubfilter Cus = GalleryPanel.ClothingSubfilter.Custom;
            const GalleryPanel.ClothingSubfilter Itm = GalleryPanel.ClothingSubfilter.Items;
            const GalleryPanel.ClothingSubfilter Mal = GalleryPanel.ClothingSubfilter.Male;
            const GalleryPanel.ClothingSubfilter Fem = GalleryPanel.ClothingSubfilter.Female;

            // VAR rows never satisfy Custom alone (or with Presets, etc.); SQL path is VAR-only here.
            if ((f & Cus) != 0) return " AND (1=0)";
            if (((f & Pre) != 0) && ((f & Itm) != 0)) return " AND (1=0)";
            if (((f & Mal) != 0) && ((f & Fem) != 0)) return " AND (1=0)";

            const string c = "CAST(ifnull(m.cloth_attr,'0') AS INTEGER)";
            var sb = new StringBuilder(384);
            sb.Append(" AND (").Append(c).Append(" & 2147483648) <> 0");
            sb.Append(" AND (").Append(c).Append(" & 15) = 1");

            bool wantsRealType = ((f & (Real | Pre | Cus | Itm | Mal | Fem)) != 0);
            bool wantsDecalType = ((f & Dec) != 0);
            bool typeExplicit = ((f & (Real | Dec)) != 0);
            if (typeExplicit)
            {
                if (((f & Real) != 0) && ((f & Dec) == 0))
                    sb.Append(" AND (").Append(c).Append(" & 512) = 0");
                else if (((f & Dec) != 0) && ((f & Real) == 0))
                    sb.Append(" AND (").Append(c).Append(" & 512) <> 0");
            }
            else if (wantsRealType && !wantsDecalType)
                sb.Append(" AND (").Append(c).Append(" & 512) = 0");

            if ((f & Pre) != 0)
                sb.Append(" AND (").Append(c).Append(" & 256) <> 0");
            if ((f & Itm) != 0)
                sb.Append(" AND (").Append(c).Append(" & 256) = 0");
            if ((f & Mal) != 0)
                sb.Append(" AND (((").Append(c).Append(") & 240) / 16) = 2");
            if ((f & Fem) != 0)
                sb.Append(" AND (((").Append(c).Append(") & 240) / 16) = 1");

            return sb.ToString();
        }

        internal static string LastErrorForDiagnostics { get { return s_LastError; } }

        internal static void InvalidateReadyStateOnCategoriesChanged()
        {
            Gallery g = Gallery.singleton;
            lock (s_Sync)
            {
                if (g == null)
                {
                    s_ReadyCategoriesSig = null;
                    s_ReadyScanBinary = long.MinValue;
                    return;
                }
                string newSig = BuildCategoriesSignature(g.CloneCategoriesForIndex());
                long scanBin = 0;
                try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }
                if (!string.IsNullOrEmpty(s_ReadyCategoriesSig)
                    && string.Equals(s_ReadyCategoriesSig, newSig, StringComparison.Ordinal)
                    && scanBin != 0
                    && s_ReadyScanBinary == scanBin)
                {
                    return;
                }
                s_ReadyCategoriesSig = null;
                s_ReadyScanBinary = long.MinValue;
            }
        }

        /// <summary>
        /// Fingerprint of installed VAR UIDs (order-independent). Used to reuse an on-disk index across
        /// launches even though <see cref="FileManager.lastPackageRefreshTime"/> is a wall clock, not content-derived.
        /// </summary>
        internal static string ComputePackageInventorySignature(Dictionary<string, VarPackage> packagesByUid)
        {
            if (packagesByUid == null || packagesByUid.Count == 0) return "0";
            var uids = new List<string>(packagesByUid.Count);
            foreach (KeyValuePair<string, VarPackage> kv in packagesByUid)
            {
                VarPackage p = kv.Value;
                if (p == null) continue;
                string u = p.Uid;
                if (!string.IsNullOrEmpty(u)) uids.Add(u);
            }
            uids.Sort(StringComparer.Ordinal);
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < uids.Count; i++)
                {
                    string s = uids[i];
                    for (int j = 0; j < s.Length; j++)
                    {
                        h ^= s[j];
                        h *= 1099511628211UL;
                    }
                    h ^= 0xFFUL;
                }
                return uids.Count.ToString() + ":" + h.ToString();
            }
        }

        private static string GetCachedPackageInventorySignature(long scanBin, Dictionary<string, VarPackage> packagesByUid)
        {
            int count = packagesByUid != null ? packagesByUid.Count : 0;
            lock (s_Sync)
            {
                if (scanBin != 0
                    && scanBin == s_CachedInvScanBinary
                    && count == s_CachedInvPkgCount
                    && !string.IsNullOrEmpty(s_CachedInvSig))
                {
                    return s_CachedInvSig;
                }
            }

            string sig = ComputePackageInventorySignature(packagesByUid);
            lock (s_Sync)
            {
                s_CachedInvScanBinary = scanBin;
                s_CachedInvPkgCount = count;
                s_CachedInvSig = sig;
            }
            return sig;
        }

        private static string ComputePackageInventorySignatureFromUids(List<string> uids)
        {
            if (uids == null || uids.Count == 0) return "0";
            uids.Sort(StringComparer.Ordinal);
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < uids.Count; i++)
                {
                    string s = uids[i] ?? "";
                    for (int j = 0; j < s.Length; j++)
                    {
                        h ^= s[j];
                        h *= 1099511628211UL;
                    }
                    h ^= 0xFFUL;
                }
                return uids.Count.ToString() + ":" + h.ToString();
            }
        }

        private static string GetCachedPackageInventorySignatureFromLivePackages(long scanBin, out long invComputeMs)
        {
            invComputeMs = 0;

            Dictionary<string, VarPackage> byUid = null;
            int count = 0;
            lock (FileManager.packagesLock)
            {
                byUid = FileManager.PackagesByUid;
                if (byUid == null) return null;
                count = byUid.Count;
            }

            lock (s_Sync)
            {
                if (scanBin != 0
                    && scanBin == s_CachedInvScanBinary
                    && count == s_CachedInvPkgCount
                    && !string.IsNullOrEmpty(s_CachedInvSig))
                {
                    return s_CachedInvSig;
                }
            }

            // Snapshot only the UIDs under lock (avoid copying the whole dictionary).
            List<string> uids = new List<string>(count);
            lock (FileManager.packagesLock)
            {
                byUid = FileManager.PackagesByUid;
                if (byUid == null) return null;
                foreach (KeyValuePair<string, VarPackage> kv in byUid)
                {
                    VarPackage p = kv.Value;
                    if (p == null) continue;
                    string u = p.Uid;
                    if (!string.IsNullOrEmpty(u)) uids.Add(u);
                }
                count = byUid.Count;
            }

            Stopwatch sw = Stopwatch.StartNew();
            string sig = ComputePackageInventorySignatureFromUids(uids);
            sw.Stop();
            invComputeMs = sw.ElapsedMilliseconds;

            lock (s_Sync)
            {
                s_CachedInvScanBinary = scanBin;
                s_CachedInvPkgCount = count;
                s_CachedInvSig = sig;
            }
            return sig;
        }

        /// <summary>
        /// If the SQLite DB was built for the same category list and the same package set, republish the
        /// in-memory gate using the <em>current</em> package scan stamp so <see cref="TryQueryGalleryCategoryRows"/>
        /// works without a ~20s rebuild (e.g. first Clothing open right after startup).
        /// </summary>
        internal static bool TryRestoreReadyStateIfMetaMatchesInventory()
        {
            if (!VpbSqlite3.IsAvailable || s_RebuildRunning) return false;
            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }
            if (scanBin == 0) return false;

            Gallery g = Gallery.singleton;
            if (g == null) return false;
            List<Gallery.Category> catSnap = g.CloneCategoriesForIndex();
            if (catSnap == null || catSnap.Count == 0) return false;
            string expectSig = BuildCategoriesSignature(catSnap);
            if (string.IsNullOrEmpty(expectSig)) return false;

            Stopwatch sw = Stopwatch.StartNew();
            long metaMs = 0;
            long invMs = 0;
            bool ok = false;
            try
            {
                string metaInv;
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    string metaVer = MetaGet(conn, "schema_version");
                    int mv;
                    if (string.IsNullOrEmpty(metaVer) || !int.TryParse(metaVer, out mv) || mv != SchemaVersion)
                        return false;

                    string metaSig = MetaGet(conn, "categories_sig");
                    if (!string.Equals(metaSig ?? "", expectSig, StringComparison.Ordinal))
                        return false;

                    metaInv = MetaGet(conn, "pkg_inv_sig");
                    if (string.IsNullOrEmpty(metaInv))
                        return false;
                }
                metaMs = sw.ElapsedMilliseconds;
                long invStart = sw.ElapsedMilliseconds;
                long invComputeMs;
                string liveInv = GetCachedPackageInventorySignatureFromLivePackages(scanBin, out invComputeMs);
                invMs = sw.ElapsedMilliseconds - invStart;
                if (string.IsNullOrEmpty(liveInv))
                    return false;
                if (!string.Equals(metaInv, liveInv, StringComparison.Ordinal))
                    return false;

                lock (s_Sync)
                {
                    s_ReadyScanBinary = scanBin;
                    s_ReadyCategoriesSig = expectSig;
                }
                ok = true;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    sw.Stop();
                    long total = sw.ElapsedMilliseconds;
                    if (total >= 10)
                        LogUtil.Log("[VPB.Gallery.Timing] sqlRestore total=" + total + "ms meta_ms=" + metaMs + " inv_ms=" + invMs + " ok=" + (ok ? "1" : "0"));
                }
                catch { }
            }
        }

        /// <summary>Call after <see cref="FileManager"/> finishes a package scan (same moment gallery caches clear).</summary>
        internal static void ScheduleGalleryIndexRebuildAfterScan()
        {
            if (s_RebuildScheduled || s_RebuildRunning) return;
            s_RebuildScheduled = true;
            ThreadPool.QueueUserWorkItem(_ => RebuildWorker());
        }

        private static void RebuildWorker()
        {
            try
            {
                s_RebuildRunning = true;
                RebuildCore();
            }
            catch (Exception ex)
            {
                s_LastError = ex.Message;
                try { LogUtil.LogError("[VPB] VpbLocalDatabase: gallery index rebuild failed: " + ex); } catch { }
                lock (s_Sync)
                {
                    s_ReadyScanBinary = long.MinValue;
                    s_ReadyCategoriesSig = null;
                }
            }
            finally
            {
                s_RebuildRunning = false;
                s_RebuildScheduled = false;
            }
        }

        private static string BuildCategoriesSignature(List<Gallery.Category> cats)
        {
            if (cats == null || cats.Count == 0) return "";
            var sb = new StringBuilder(cats.Count * 64);
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i];
                sb.Append(c.name ?? "").Append('\u001E');
                sb.Append(c.extension ?? "").Append('\u001E');
                sb.Append(c.path ?? "").Append('\u001E');
                if (c.paths != null)
                {
                    for (int j = 0; j < c.paths.Count; j++)
                    {
                        sb.Append(c.paths[j] ?? "");
                        sb.Append('\u001F');
                    }
                }
                sb.Append('\u001E');
            }
            return sb.ToString();
        }

        private static bool ExtensionSetsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return true;
            var sa = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(a))
            {
                string[] pa = a.Split('|');
                for (int i = 0; i < pa.Length; i++)
                {
                    string t = pa[i] != null ? pa[i].Trim() : "";
                    if (t.Length > 0) sa.Add(t);
                }
            }
            var sb = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(b))
            {
                string[] pb = b.Split('|');
                for (int i = 0; i < pb.Length; i++)
                {
                    string t = pb[i] != null ? pb[i].Trim() : "";
                    if (t.Length > 0) sb.Add(t);
                }
            }
            if (sa.Count != sb.Count) return false;
            foreach (string x in sa)
            {
                if (!sb.Contains(x)) return false;
            }
            return true;
        }

        private static string NormalizeSlashes(string p)
        {
            return string.IsNullOrEmpty(p) ? p : p.Replace('\\', '/');
        }

        private static bool InternalPathStartsWithPrefix(string internalPath, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return true;
            return NormalizeSlashes(internalPath).StartsWith(NormalizeSlashes(prefix), StringComparison.OrdinalIgnoreCase);
        }

        private static bool FileMatchesCategoryExtensions(string internalPath, Gallery.Category cat)
        {
            if (string.IsNullOrEmpty(cat.extension)) return false;
            string entryExt = Path.GetExtension(internalPath);
            if (string.IsNullOrEmpty(entryExt) || entryExt.Length < 2) return false;
            entryExt = entryExt.Substring(1);
            string[] exts = cat.extension.Split('|');
            for (int e = 0; e < exts.Length; e++)
            {
                string ce = exts[e];
                if (string.IsNullOrEmpty(ce)) continue;
                if (string.Equals(entryExt, ce.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool FileMatchesCategoryPath(string internalPath, Gallery.Category cat)
        {
            if (cat.paths != null && cat.paths.Count > 0)
            {
                for (int k = 0; k < cat.paths.Count; k++)
                {
                    string pref = cat.paths[k];
                    if (!string.IsNullOrEmpty(pref) && InternalPathStartsWithPrefix(internalPath, pref))
                        return true;
                }
                return false;
            }
            if (!string.IsNullOrEmpty(cat.path))
                return InternalPathStartsWithPrefix(internalPath, cat.path);
            return true;
        }

        /// <summary>First matching category wins (same spirit as <see cref="GalleryPanel.CacheCategoryCounts"/>).</summary>
        private static string ClassifyFile(List<Gallery.Category> orderedCats, string internalPath)
        {
            if (orderedCats == null) return null;
            for (int ci = 0; ci < orderedCats.Count; ci++)
            {
                Gallery.Category cat = orderedCats[ci];
                if (string.IsNullOrEmpty(cat.name) || string.IsNullOrEmpty(cat.extension)) continue;
                if (!FileMatchesCategoryExtensions(internalPath, cat)) continue;
                if (!FileMatchesCategoryPath(internalPath, cat)) continue;
                return cat.name;
            }
            return null;
        }

        private sealed class CategoryClassifier
        {
            private struct Rule
            {
                public string Name;
                public string SinglePathPrefixNorm; // empty means "any"
                public string[] PathPrefixesNorm;    // null means use SinglePathPrefixNorm
            }

            private readonly Rule[] _rules;
            private readonly Dictionary<string, List<int>> _extToRuleIndices;

            internal CategoryClassifier(List<Gallery.Category> orderedCats)
            {
                if (orderedCats == null) orderedCats = new List<Gallery.Category>();

                _rules = new Rule[orderedCats.Count];
                _extToRuleIndices = new Dictionary<string, List<int>>(64, StringComparer.OrdinalIgnoreCase);

                for (int ci = 0; ci < orderedCats.Count; ci++)
                {
                    Gallery.Category cat = orderedCats[ci];
                    string name = cat.name ?? "";
                    string extPipe = cat.extension ?? "";

                    Rule r = new Rule();
                    r.Name = name;

                    // Normalize path prefixes once (slashes + null handling).
                    if (cat.paths != null && cat.paths.Count > 0)
                    {
                        var pref = new List<string>(cat.paths.Count);
                        for (int j = 0; j < cat.paths.Count; j++)
                        {
                            string p = cat.paths[j];
                            if (string.IsNullOrEmpty(p)) continue;
                            pref.Add(NormalizeSlashes(p));
                        }
                        r.PathPrefixesNorm = pref.Count > 0 ? pref.ToArray() : null;
                        r.SinglePathPrefixNorm = "";
                    }
                    else
                    {
                        r.PathPrefixesNorm = null;
                        r.SinglePathPrefixNorm = string.IsNullOrEmpty(cat.path) ? "" : NormalizeSlashes(cat.path);
                    }
                    _rules[ci] = r;

                    // Map each extension -> ordered list of rule indices.
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(extPipe))
                        continue;
                    string[] exts = extPipe.Split('|');
                    for (int ei = 0; ei < exts.Length; ei++)
                    {
                        string e = exts[ei] != null ? exts[ei].Trim() : "";
                        if (e.Length == 0) continue;
                        List<int> list;
                        if (!_extToRuleIndices.TryGetValue(e, out list) || list == null)
                        {
                            list = new List<int>(8);
                            _extToRuleIndices[e] = list;
                        }
                        // Preserve "first matching category wins" by keeping order.
                        list.Add(ci);
                    }
                }
            }

            internal string Classify(string internalPath)
            {
                if (string.IsNullOrEmpty(internalPath)) return null;

                int dot = internalPath.LastIndexOf('.');
                if (dot < 0 || dot >= internalPath.Length - 1) return null;
                string ext = internalPath.Substring(dot + 1);
                if (string.IsNullOrEmpty(ext)) return null;

                List<int> ruleIdx;
                if (!_extToRuleIndices.TryGetValue(ext, out ruleIdx) || ruleIdx == null || ruleIdx.Count == 0)
                    return null;

                // Normalize the internal path once per file.
                string ipNorm = NormalizeSlashes(internalPath);

                for (int k = 0; k < ruleIdx.Count; k++)
                {
                    int ci = ruleIdx[k];
                    if (ci < 0 || ci >= _rules.Length) continue;
                    Rule r = _rules[ci];
                    if (string.IsNullOrEmpty(r.Name)) continue;

                    // Path rules: if no prefixes are configured, accept any path.
                    if (r.PathPrefixesNorm != null && r.PathPrefixesNorm.Length > 0)
                    {
                        bool ok = false;
                        for (int j = 0; j < r.PathPrefixesNorm.Length; j++)
                        {
                            string pref = r.PathPrefixesNorm[j];
                            if (string.IsNullOrEmpty(pref)) continue;
                            if (ipNorm.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                            {
                                ok = true;
                                break;
                            }
                        }
                        if (!ok) continue;
                    }
                    else if (!string.IsNullOrEmpty(r.SinglePathPrefixNorm))
                    {
                        if (!ipNorm.StartsWith(r.SinglePathPrefixNorm, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    return r.Name;
                }

                return null;
            }
        }

        private static void RebuildCore()
        {
            if (!VpbSqlite3.IsAvailable)
            {
                if (!s_LoggedSqliteUnavailable)
                {
                    s_LoggedSqliteUnavailable = true;
                    try
                    {
                        LogUtil.LogWarning(
                            "[VPB] VpbLocalDatabase: sqlite3.dll could not be loaded (no local DB index). " +
                            "Deploy 64-bit sqlite3.dll to BepInEx\\plugins under the VaM folder (Cache\\VPB is a legacy fallback). " +
                            "Expected DB path (when working): " + GetLocalDatabasePathForDiagnostics());
                    }
                    catch { }
                }
                return;
            }

            Gallery g = Gallery.singleton;
            List<Gallery.Category> catSnap = g != null ? g.CloneCategoriesForIndex() : new List<Gallery.Category>();
            if (catSnap == null) catSnap = new List<Gallery.Category>();

            if (catSnap.Count == 0)
            {
                try
                {
                    using (var conn = new VpbSqlite3.Connection(DbPath))
                    {
                        EnsureSchema(conn);
                    }
                    if (!s_LoggedEmptyCategoriesDb)
                    {
                        s_LoggedEmptyCategoriesDb = true;
                        try
                        {
                            string p = GetLocalDatabasePathForDiagnostics();
                            long sz = -1;
                            try { if (File.Exists(p)) sz = new FileInfo(p).Length; } catch { }
                            LogUtil.Log("[VPB] VpbLocalDatabase: database created (empty categories) at " + p + " | file=" + FormatByteSize(sz));
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    s_LastError = ex.Message;
                    try { LogUtil.LogError("[VPB] VpbLocalDatabase: could not create database: " + ex.Message); } catch { }
                }
                return;
            }

            // Never rebuild against an unstamped clock: DateTime.MinValue.ToBinary() is 0 and would publish
            // s_ReadyScanBinary=0 while real scans use a non-zero stamp — SQL fast path stays disabled forever.
            DateTime refreshClock = DateTime.MinValue;
            try { refreshClock = FileManager.lastPackageRefreshTime; } catch { }
            if (refreshClock == DateTime.MinValue)
                return;

            long scanAtStart = 0;
            try { scanAtStart = refreshClock.ToBinary(); } catch { }
            if (scanAtStart == 0)
                return;

            string catSig = BuildCategoriesSignature(catSnap);
            var classifier = new CategoryClassifier(catSnap);

            Dictionary<string, VarPackage> pkgSnap;
            lock (FileManager.packagesLock)
            {
                if (FileManager.PackagesByUid == null) return;
                pkgSnap = new Dictionary<string, VarPackage>(FileManager.PackagesByUid, StringComparer.OrdinalIgnoreCase);
            }

            string invSigForMeta = ComputePackageInventorySignature(pkgSnap);

            long tsFreq = Stopwatch.Frequency;
            Func<long, long> ticksToMs = t => (t <= 0 || tsFreq <= 0) ? 0 : (long)((t * 1000.0) / tsFreq);

            Stopwatch rebuildSw = Stopwatch.StartNew();
            string rebuildAbortReason = null;
            long tOpenConn = 0;
            long tEnsureSchema = 0;
            long tBulkPragmas = 0;
            long tBegin = 0;
            long tDelete = 0;
            long tPkgRow = 0;
            long tDeps = 0;
            long tEntryData = 0;
            long tCatMem = 0;
            long tCatMemClassify = 0;
            long tCatMemSql = 0;
            long tMeta = 0;
            long tCommit = 0;
            long tDropIdx = 0;
            long tCreateIdx = 0;

            int nPkgInserted = 0;
            int nDepInserted = 0;
            int nCatMemInserted = 0;
            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    long t0 = Stopwatch.GetTimestamp();
                    tOpenConn += Stopwatch.GetTimestamp() - t0;

                    t0 = Stopwatch.GetTimestamp();
                    EnsureSchema(conn);
                    tEnsureSchema += Stopwatch.GetTimestamp() - t0;

                    // Rebuild-only perf pragmas (connection-local).
                    // Avoids extra temp-file work and increases cache for bulk insert/index build.
                    t0 = Stopwatch.GetTimestamp();
                    try
                    {
                        conn.ExecUtf8(
                            "PRAGMA temp_store=MEMORY;" +
                            "PRAGMA cache_size=-65536;" +          // ~64MiB page cache (negative = KiB)
                            "PRAGMA mmap_size=268435456;");        // 256MiB mmap when supported
                    }
                    catch { }
                    tBulkPragmas += Stopwatch.GetTimestamp() - t0;

                    t0 = Stopwatch.GetTimestamp();
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    tBegin += Stopwatch.GetTimestamp() - t0;
                    try
                    {
                        // Bulk-load optimization: avoid maintaining cat_mem secondary indexes per inserted row.
                        t0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8("DROP INDEX IF EXISTS idx_cm_cat; DROP INDEX IF EXISTS idx_cm_pkg;");
                        tDropIdx += Stopwatch.GetTimestamp() - t0;

                        t0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8("DELETE FROM cat_mem; DELETE FROM pkg_dep; DELETE FROM pkg;");
                        tDelete += Stopwatch.GetTimestamp() - t0;
                        using (var insPkg = conn.Prepare("INSERT OR REPLACE INTO pkg(uid,creator,wtime,psize,var_path,pctime,loaded) VALUES(?,?,?,?,?,?,?)"))
                        using (var insMem = conn.Prepare("INSERT OR IGNORE INTO cat_mem(category,pkg_uid,internal_path,list_path,cloth_attr) VALUES(?,?,?,?,?)"))
                        using (var insDep = conn.Prepare("INSERT OR IGNORE INTO pkg_dep(src_uid,dep_uid) VALUES(?,?)"))
                        {
                            foreach (KeyValuePair<string, VarPackage> kv in pkgSnap)
                            {
                                VarPackage pkg = kv.Value;
                                if (pkg == null) continue;
                                string uid = pkg.Uid ?? "";
                                if (uid.Length == 0) continue;

                                string cr = pkg.Creator ?? "";
                                long wt = DateTime.MinValue.Ticks;
                                try { wt = pkg.LastWriteTime.ToBinary(); } catch { }
                                long sz = 0;
                                try { sz = pkg.Size; } catch { }
                                string varPath = pkg.Path ?? "";
                                string varListPrefix = varPath.Length > 0 ? (varPath + ":/") : ":/";
                                long ct = DateTime.MinValue.Ticks;
                                try { ct = pkg.CreationTime.ToBinary(); } catch { }
                                int loaded = ComputePackageLoadedFlagFromVarPath(varPath);

                                long tPkg0 = Stopwatch.GetTimestamp();
                                insPkg.BindText(1, uid);
                                insPkg.BindText(2, cr);
                                insPkg.BindInt64(3, wt);
                                insPkg.BindInt64(4, sz);
                                insPkg.BindText(5, varPath);
                                insPkg.BindInt64(6, ct);
                                insPkg.BindInt64(7, loaded);
                                insPkg.Step();
                                insPkg.Reset();
                                tPkgRow += Stopwatch.GetTimestamp() - tPkg0;
                                nPkgInserted++;

                                // Store transitive dependency edges (matches existing RecursivePackageDependencies-based behavior).
                                try
                                {
                                    var deps = pkg.RecursivePackageDependencies;
                                    if (deps != null)
                                    {
                                        long tDep0 = Stopwatch.GetTimestamp();
                                        for (int di = 0; di < deps.Count; di++)
                                        {
                                            string dep = NormalizeDependencyUidOrPath(deps[di]);
                                            if (string.IsNullOrEmpty(dep)) continue;
                                            insDep.BindText(1, uid);
                                            insDep.BindText(2, dep);
                                            insDep.Step();
                                            insDep.Reset();
                                            nDepInserted++;
                                        }
                                        tDeps += Stopwatch.GetTimestamp() - tDep0;
                                    }
                                }
                                catch { }

                                List<string> names;
                                List<long> ticks;
                                List<long> sizes;
                                long tEntry0 = Stopwatch.GetTimestamp();
                                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                                {
                                    tEntryData += Stopwatch.GetTimestamp() - tEntry0;
                                    continue;
                                }
                                tEntryData += Stopwatch.GetTimestamp() - tEntry0;

                                long tMem0 = Stopwatch.GetTimestamp();
                                for (int i = 0; i < names.Count; i++)
                                {
                                    string ip = names[i];
                                    if (string.IsNullOrEmpty(ip)) continue;
                                    long tCls0 = Stopwatch.GetTimestamp();
                                    string cname = classifier.Classify(ip);
                                    tCatMemClassify += Stopwatch.GetTimestamp() - tCls0;
                                    if (string.IsNullOrEmpty(cname)) continue;

                                    string listPath;
                                    if (string.Equals(ip, "meta.json", StringComparison.OrdinalIgnoreCase))
                                        listPath = varPath;
                                    else
                                        listPath = varListPrefix + ip;

                                    long clothAttr = 0;
                                    if (string.Equals(cname, "Clothing", StringComparison.OrdinalIgnoreCase))
                                        clothAttr = PackClothingGalleryAttrForVarListPath(listPath);

                                    long tMemSql0 = Stopwatch.GetTimestamp();
                                    insMem.BindText(1, cname);
                                    insMem.BindText(2, uid);
                                    insMem.BindText(3, ip);
                                    insMem.BindText(4, listPath);
                                    insMem.BindInt64(5, clothAttr);
                                    insMem.Step();
                                    insMem.Reset();
                                    tCatMemSql += Stopwatch.GetTimestamp() - tMemSql0;
                                    nCatMemInserted++;
                                }
                                tCatMem += Stopwatch.GetTimestamp() - tMem0;
                            }
                        }

                        using (var upMeta = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                        {
                            long tMeta0 = Stopwatch.GetTimestamp();
                            upMeta.BindText(1, "schema_version");
                            upMeta.BindText(2, SchemaVersion.ToString());
                            upMeta.Step();
                            upMeta.Reset();

                            upMeta.BindText(1, "categories_sig");
                            upMeta.BindText(2, catSig);
                            upMeta.Step();
                            upMeta.Reset();

                            upMeta.BindText(1, "scan_binary");
                            upMeta.BindText(2, scanAtStart.ToString());
                            upMeta.Step();
                            upMeta.Reset();

                            upMeta.BindText(1, "pkg_inv_sig");
                            upMeta.BindText(2, invSigForMeta);
                            upMeta.Step();
                            upMeta.Reset();
                            tMeta += Stopwatch.GetTimestamp() - tMeta0;
                        }

                        // Recreate indexes after load (much faster than updating incrementally).
                        t0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8(
                            "CREATE INDEX IF NOT EXISTS idx_cm_cat ON cat_mem(category);" +
                            "CREATE INDEX IF NOT EXISTS idx_cm_pkg ON cat_mem(pkg_uid);");
                        tCreateIdx += Stopwatch.GetTimestamp() - t0;

                        long tCommit0 = Stopwatch.GetTimestamp();
                        conn.ExecUtf8("COMMIT;");
                        tCommit += Stopwatch.GetTimestamp() - tCommit0;
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
            finally
            {
                rebuildSw.Stop();
            }

            long scanNow = 0;
            try { scanNow = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }
            if (scanNow != scanAtStart || scanAtStart == 0)
            {
                rebuildAbortReason = "scan_changed";
            }

            string catSigNow = BuildCategoriesSignature(g.CloneCategoriesForIndex());
            if (catSigNow != catSig)
            {
                if (rebuildAbortReason == null) rebuildAbortReason = "categories_changed";
            }

            try
            {
                long totalMs = rebuildSw.ElapsedMilliseconds;
                var sb = new StringBuilder(512);
                sb.Append("[VPB.Gallery.Timing] sqlRebuild ");
                sb.Append(rebuildAbortReason == null ? "DONE" : ("ABORT " + rebuildAbortReason));
                sb.Append(" total=").Append(totalMs).Append("ms");
                sb.Append(" | phases_ms=");
                sb.Append("open=").Append(ticksToMs(tOpenConn)).Append(',');
                sb.Append("schema=").Append(ticksToMs(tEnsureSchema)).Append(',');
                sb.Append("bulkPragmas=").Append(ticksToMs(tBulkPragmas)).Append(',');
                sb.Append("begin=").Append(ticksToMs(tBegin)).Append(',');
                sb.Append("delete=").Append(ticksToMs(tDelete)).Append(',');
                sb.Append("pkg=").Append(ticksToMs(tPkgRow)).Append(',');
                sb.Append("deps=").Append(ticksToMs(tDeps)).Append(',');
                sb.Append("entryData=").Append(ticksToMs(tEntryData)).Append(',');
                sb.Append("catMem=").Append(ticksToMs(tCatMem)).Append(',');
                sb.Append("catMemClassify=").Append(ticksToMs(tCatMemClassify)).Append(',');
                sb.Append("catMemSql=").Append(ticksToMs(tCatMemSql)).Append(',');
                sb.Append("meta=").Append(ticksToMs(tMeta)).Append(',');
                sb.Append("dropIdx=").Append(ticksToMs(tDropIdx)).Append(',');
                sb.Append("createIdx=").Append(ticksToMs(tCreateIdx)).Append(',');
                sb.Append("commit=").Append(ticksToMs(tCommit));
                sb.Append(" | counts=");
                sb.Append("pkg=").Append(nPkgInserted).Append(',');
                sb.Append("dep=").Append(nDepInserted).Append(',');
                sb.Append("catMem=").Append(nCatMemInserted);
                sb.Append(" | snap=");
                sb.Append("cats=").Append(catSnap != null ? catSnap.Count : 0).Append(',');
                sb.Append("pkgs=").Append(pkgSnap != null ? pkgSnap.Count : 0);
                LogUtil.Log(sb.ToString());
            }
            catch { }

            if (rebuildAbortReason != null) return;

            lock (s_Sync)
            {
                s_ReadyScanBinary = scanAtStart;
                s_ReadyCategoriesSig = catSig;
            }

            string dbPathForLog = GetLocalDatabasePathForDiagnostics();
            try
            {
                using (var logConn = new VpbSqlite3.Connection(dbPathForLog))
                    LogLocalDatabaseReadyDetails(logConn, dbPathForLog, rebuildSw.ElapsedMilliseconds, catSnap.Count, pkgSnap.Count);
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] VpbLocalDatabase: SQLite ready but stats log failed: " + ex.Message); } catch { }
            }
        }

        /// <summary>
        /// Fills side-tab category totals from <c>cat_mem</c> when the index matches the current package scan (avoids scanning every VAR on disk).
        /// Only keys already present in <paramref name="countsByCategoryName"/> are updated.
        /// </summary>
        internal static bool TryReadCategoryMemberCounts(Dictionary<string, int> countsByCategoryName, string creatorFilter = "", HashSet<string> activeTags = null)
        {
            if (!VpbSqlite3.IsAvailable || countsByCategoryName == null || countsByCategoryName.Count == 0) return false;

            long scanBin = DateTime.MinValue.Ticks;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig)) return false;
            if (s_RebuildRunning) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    if (conn == null) return false;
                    
                    bool hasCreator = !string.IsNullOrEmpty(creatorFilter);
                    bool hasTags = activeTags != null && activeTags.Count > 0;
                    
                    var sb = new StringBuilder();
                    sb.Append("SELECT m.category, COUNT(*) FROM cat_mem m");
                    if (hasCreator) sb.Append(" INNER JOIN pkg p ON p.uid = m.pkg_uid");
                    
                    sb.Append(" WHERE 1=1");
                    if (hasCreator) sb.Append(" AND p.creator = ?");
                    
                    List<string> tagsList = null;
                    if (hasTags)
                    {
                        tagsList = new List<string>(activeTags);
                        foreach (var tag in tagsList)
                        {
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                    }
                    
                    sb.Append(" GROUP BY m.category");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (hasCreator) stmt.BindText(bind++, creatorFilter);
                        if (hasTags)
                        {
                            foreach (var tag in tagsList)
                            {
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                            }
                        }

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string cname = stmt.ColumnText(0);
                            int n;
                            if (!int.TryParse(stmt.ColumnText(1), out n)) n = 0;
                            if (countsByCategoryName.ContainsKey(cname))
                                countsByCategoryName[cname] = n;
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Side-tab creator file counts from <c>cat_mem</c> + <c>pkg</c> (distinct VAR file rows), using the same extension + path rules as the legacy package scan.
        /// Only files that appear in the index (assigned to at least one category) are counted; unclassified VAR files are excluded.
        /// </summary>
        internal static bool TryReadCreatorFileCounts(
            Dictionary<string, int> countsOut,
            string extensionPipeSeparated,
            List<string> pathPrefixes,
            string singlePathPrefix,
            HashSet<string> activeTags = null,
            string categoryTitle = null)
        {
            if (!VpbSqlite3.IsAvailable || countsOut == null) return false;
            countsOut.Clear();

            long scanBin = DateTime.MinValue.Ticks;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig)) return false;
            if (s_RebuildRunning) return false;

            var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(extensionPipeSeparated))
            {
                string[] exts2 = extensionPipeSeparated.Split('|');
                for (int i = 0; i < exts2.Length; i++)
                {
                    string e = exts2[i] != null ? exts2[i].Trim() : "";
                    if (e.Length > 0) extSet.Add(e);
                }
            }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool hasTags = activeTags != null && activeTags.Count > 0;
                    bool hasCat = !string.IsNullOrEmpty(categoryTitle);
                    var sb = new StringBuilder();
                    string countExpr = hasCat ? "COUNT(*)" : "COUNT(DISTINCT m.pkg_uid || char(0) || m.internal_path)";
                    sb.Append("SELECT p.creator, ").Append(countExpr).Append(" ");
                    sb.Append("FROM cat_mem m INNER JOIN pkg p ON p.uid = m.pkg_uid ");
                    sb.Append("WHERE length(trim(coalesce(p.creator,''))) > 0");
                    if (hasCat) sb.Append(" AND m.category = ?");
                    
                    List<string> tagsList = null;
                    if (hasTags)
                    {
                        tagsList = new List<string>(activeTags);
                        foreach (var tag in tagsList)
                        {
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                    }
                    
                    sb.Append(" GROUP BY p.creator");

                    using (var stmt = conn.Prepare(sb.ToString()))
                    {
                        int bind = 1;
                        if (hasCat) stmt.BindText(bind++, categoryTitle);
                        if (hasTags)
                        {
                            foreach (var tag in tagsList)
                            {
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                            }
                        }

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string creator = stmt.ColumnText(0);
                            int n;
                            if (!int.TryParse(stmt.ColumnText(1), out n)) n = 0;
                            if (!string.IsNullOrEmpty(creator))
                                countsOut[creator] = n;
                        }
                    }
                }
                return true;
            }
            catch
            {
                countsOut.Clear();
                return false;
            }
        }

        /// <summary>Populated by <see cref="TryQueryGalleryCategoryRows"/> for perf diagnostics (gated logging).</summary>
        internal static bool TryReadTagCounts(
            string categoryTitle,
            string currentExtension,
            string creatorFilter,
            HashSet<string> tagsToCount,
            Dictionary<string, int> outTagCounts,
            out TagScanTotals outFacets,
            GalleryPanel.ClothingSubfilter clothingSubfilter = 0,
            GalleryPanel.AppearanceSubfilter appearanceSubfilter = 0,
            HashSet<string> activeTags = null)
        {
            outFacets = new TagScanTotals();
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning) return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool pkgHasLoadedCol = false;
                    try { pkgHasLoadedCol = PkgHasLoadedColumn(conn); } catch { pkgHasLoadedCol = false; }

                    string clothSqlAnd = BuildClothingSubfilterSqlAnd(conn, categoryTitle, clothingSubfilter);
                    
                    string tagSqlAnd = "";
                    List<string> activeTagsList = null;
                    if (activeTags != null && activeTags.Count > 0)
                    {
                        activeTagsList = new List<string>(activeTags);
                        var sb = new StringBuilder();
                        for (int i = 0; i < activeTagsList.Count; i++)
                        {
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                        tagSqlAnd = sb.ToString();
                    }

                    string sql =
                        "SELECT m.internal_path, m.pkg_uid, ifnull(m.cloth_attr,'0'), m.list_path FROM cat_mem m " +
                        "INNER JOIN pkg p ON p.uid = m.pkg_uid " +
                        "WHERE m.category = ? AND ((length(trim(?)) = 0) OR (p.creator = ?))" + clothSqlAnd + tagSqlAnd;

                    using (var stmt = conn.Prepare(sql))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        string cf = creatorFilter ?? "";
                        stmt.BindText(bind++, cf);
                        stmt.BindText(bind++, cf);

                        if (activeTagsList != null)
                        {
                            foreach (var tag in activeTagsList)
                            {
                                stmt.BindText(bind++, "%[" + EscapeLike(tag) + "]%");
                            }
                        }

                        bool isClothing = string.Equals(categoryTitle, "Clothing", StringComparison.OrdinalIgnoreCase);
                        bool isAppearance = string.Equals(categoryTitle, "Appearance", StringComparison.OrdinalIgnoreCase);

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string internalPath = stmt.ColumnText(0);
                            string pkgUid = stmt.ColumnText(1);
                            int clothAttr = (int)stmt.ColumnInt64(2);
                            string listPath = stmt.ColumnText(3) ?? "";

                            if (tagsToCount != null && tagsToCount.Count > 0)
                            {
                                foreach (var tag in tagsToCount)
                                {
                                    if (listPath.IndexOf("[" + tag + "]", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        int cur;
                                        outTagCounts.TryGetValue(tag, out cur);
                                        outTagCounts[tag] = cur + 1;
                                    }
                                }
                            }

                            if (isClothing)
                            {
                                outFacets.ClothingSubfilterCountAll++;
                                if ((clothAttr & ClothingAttrPresentFlag) != 0)
                                {
                                    int kind = clothAttr & 0xF;
                                    int gender = (clothAttr >> 4) & 0xF;
                                    bool isPreset = (clothAttr & 0x100) != 0;
                                    bool isDecal = (clothAttr & 0x200) != 0;

                                    if (kind == (int)ClothingLoadingUtils.ResourceKind.Clothing)
                                    {
                                        // Facet counts
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.RealClothing)) outFacets.ClothingSubfilterFacetCountReal++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Presets)) outFacets.ClothingSubfilterFacetCountPresets++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Custom)) outFacets.ClothingSubfilterFacetCountCustom++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Items)) outFacets.ClothingSubfilterFacetCountItems++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Male)) outFacets.ClothingSubfilterFacetCountMale++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Female)) outFacets.ClothingSubfilterFacetCountFemale++;
                                        if (ClothingPackedAttrMatchesSubfilter(clothAttr, clothingSubfilter ^ GalleryPanel.ClothingSubfilter.Decals)) outFacets.ClothingSubfilterFacetCountDecals++;

                                        if (isDecal) outFacets.ClothingSubfilterCountDecals++;
                                        else
                                        {
                                            outFacets.ClothingSubfilterCountReal++;
                                            if (isPreset) outFacets.ClothingSubfilterCountPresets++;
                                            // Note: VAR rows are never "Custom" (loose files only), so ClothingSubfilterCountCustom remains 0.
                                            // We explicitly initialize it here to clear the compiler warning.
                                            outFacets.ClothingSubfilterCountCustom = 0; 
                                            if (!isPreset) outFacets.ClothingSubfilterCountItems++;
                                            if (gender == (int)ClothingLoadingUtils.ResourceGender.Male) outFacets.ClothingSubfilterCountMale++;
                                            else if (gender == (int)ClothingLoadingUtils.ResourceGender.Female) outFacets.ClothingSubfilterCountFemale++;
                                        }
                                    }
                                }
                            }
                            else if (isAppearance)
                            {
                                // Appearance subfilters are not yet packed into clothAttr in the current schema.
                                // Fall back to path heuristics for now to avoid warnings and provide correct counts.
                                string p = internalPath.Replace('\\', '/');
                                if (p.IndexOf("/appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    bool isCustomAppearance = p.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                                    bool isPresetAppearance = p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
                                    
                                    // Heuristic gender (simplified version of the one in GalleryTagCountBackgroundScan)
                                    int g = 0; // Unknown
                                    string combined = (p + " " + (pkgUid ?? "")).ToLowerInvariant();
                                    if (combined.Contains("female") || combined.Contains("woman") || combined.Contains("girl")) g = 1;
                                    else if (combined.Contains("male") || combined.Contains("man") || combined.Contains("boy")) g = 2;
                                    else if (combined.Contains("futa") || combined.Contains("herm")) g = 3;

                                    outFacets.AppearanceSubfilterCountAll++;
                                    if (isPresetAppearance) outFacets.AppearanceSubfilterCountPresets++;
                                    if (isCustomAppearance) outFacets.AppearanceSubfilterCountCustom++;
                                    if (g == 2) outFacets.AppearanceSubfilterCountMale++;
                                    if (g == 1) outFacets.AppearanceSubfilterCountFemale++;
                                    if (g == 3) outFacets.AppearanceSubfilterCountFuta++;

                                    // Simplified PassesAppearanceSubfilters check
                                    bool PassesApp(GalleryPanel.AppearanceSubfilter f, bool isPre, bool isCus, int gen)
                                    {
                                        if (f == 0) return true;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Presets) != 0 && !isPre) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Custom) != 0 && !isCus) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Male) != 0 && gen != 2) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Female) != 0 && gen != 1) return false;
                                        if ((f & GalleryPanel.AppearanceSubfilter.Futa) != 0 && gen != 3) return false;
                                        return true;
                                    }

                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Presets, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountPresets++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Custom, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountCustom++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Male, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountMale++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Female, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountFemale++;
                                    if (PassesApp(appearanceSubfilter ^ GalleryPanel.AppearanceSubfilter.Futa, isPresetAppearance, isCustomAppearance, g)) outFacets.AppearanceSubfilterFacetCountFuta++;

                                    if (PassesApp(appearanceSubfilter, isPresetAppearance, isCustomAppearance, g))
                                    {
                                        outFacets.AppearanceSubfilterCurrentCountAll++;
                                        if (g == 2) outFacets.AppearanceSubfilterCurrentCountMale++;
                                        if (g == 1) outFacets.AppearanceSubfilterCurrentCountFemale++;
                                        if (g == 3) outFacets.AppearanceSubfilterCurrentCountFuta++;
                                    }
                                    
                                    if (isPresetAppearance)
                                    {
                                        outFacets.AppearanceSourceCountPresets++;
                                        outFacets.AppearanceSourceCountAll++;
                                        outFacets.AppearanceSourceCountCustom = 0; // Explicitly initialize to clear warning
                                    }
                                    else if (isCustomAppearance)
                                    {
                                        outFacets.AppearanceSourceCountCustom++;
                                        outFacets.AppearanceSourceCountAll++;
                                    }
                                }
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal struct GalleryCategoryQueryStats
        {
            public bool ExecutedQuery;
            public string RejectReason;
            public long SqlElapsedMs;
            public int RowsRead;
        }

        internal sealed class TagScanTotals
        {
            public int AppearanceSourceCountAll;
            public int AppearanceSourceCountPresets;
            public int AppearanceSourceCountCustom;
            public int ClothingSubfilterCountAll;
            public int ClothingSubfilterCountReal;
            public int ClothingSubfilterCountPresets;
            public int ClothingSubfilterCountCustom;
            public int ClothingSubfilterCountItems;
            public int ClothingSubfilterCountMale;
            public int ClothingSubfilterCountFemale;
            public int ClothingSubfilterCountDecals;
            public int AppearanceSubfilterCountAll;
            public int AppearanceSubfilterCountPresets;
            public int AppearanceSubfilterCountCustom;
            public int AppearanceSubfilterCountMale;
            public int AppearanceSubfilterCountFemale;
            public int AppearanceSubfilterCountFuta;
            public int ClothingSubfilterFacetCountReal;
            public int ClothingSubfilterFacetCountPresets;
            public int ClothingSubfilterFacetCountCustom;
            public int ClothingSubfilterFacetCountItems;
            public int ClothingSubfilterFacetCountMale;
            public int ClothingSubfilterFacetCountFemale;
            public int ClothingSubfilterFacetCountDecals;
            public int AppearanceSubfilterFacetCountPresets;
            public int AppearanceSubfilterFacetCountCustom;
            public int AppearanceSubfilterFacetCountMale;
            public int AppearanceSubfilterFacetCountFemale;
            public int AppearanceSubfilterFacetCountFuta;
            public int AppearanceSubfilterCurrentCountAll;
            public int AppearanceSubfilterCurrentCountMale;
            public int AppearanceSubfilterCurrentCountFemale;
            public int AppearanceSubfilterCurrentCountFuta;
        }

        /// <summary>
        /// Returns true if rows were read from SQLite (caller still applies path quirks e.g. Saves/Person vs appearance, name filter, PassesFilters).
        /// </summary>
        /// <param name="clothingSubfilterForSql">When non-zero and category is Clothing, narrows the query using indexed <c>cloth_attr</c> (schema 4+).</param>
        internal static bool TryQueryGalleryCategoryRows(
            string categoryTitle,
            string currentExtension,
            string creatorFilter,
            List<Row> outRows,
            out GalleryCategoryQueryStats stats,
            GalleryPanel.ClothingSubfilter clothingSubfilterForSql = 0,
            int loadedState = -1,
            string[] nameTerms = null,
            List<string> pathExclusions = null,
            HashSet<string> activeTags = null,
            SortState sortState = null)
        {
            stats = new GalleryCategoryQueryStats();
            outRows.Clear();
            if (!VpbSqlite3.IsAvailable)
            {
                stats.RejectReason = "sqlite_unavailable";
                return false;
            }
            if (string.IsNullOrEmpty(categoryTitle))
            {
                stats.RejectReason = "empty_category_title";
                return false;
            }
            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            string catSig = null;
            long readyScan = long.MinValue;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig))
            {
                stats.RejectReason = "index_stale_or_empty_sig readyScan=" + readyScan + " scanBin=" + scanBin + " sigEmpty=" + (string.IsNullOrEmpty(catSig) ? "1" : "0");
                // Self-heal: stamp 0 / MinValue means an early rebuild ran before FileManager stamped refresh time,
                // or the index was never published — queue a rebuild when the live scan clock is real.
                if (scanBin != 0 && (readyScan == 0 || readyScan == long.MinValue))
                {
                    try { ScheduleGalleryIndexRebuildAfterScan(); } catch { }
                }
                return false;
            }
            if (s_RebuildRunning)
            {
                stats.RejectReason = "rebuild_running";
                return false;
            }

            Gallery g = Gallery.singleton;
            if (g == null)
            {
                stats.RejectReason = "gallery_singleton_null";
                return false;
            }
            Gallery.Category catDef = g.FindCategoryByName(categoryTitle);
            if (string.IsNullOrEmpty(catDef.name))
            {
                stats.RejectReason = "category_not_found";
                return false;
            }
            if (!ExtensionSetsEqual(catDef.extension ?? "", currentExtension ?? ""))
            {
                stats.RejectReason = "extension_mismatch";
                return false;
            }

            try
            {
                var swSql = Stopwatch.StartNew();
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    bool pkgHasLoadedCol = false;
                    try { pkgHasLoadedCol = PkgHasLoadedColumn(conn); } catch { pkgHasLoadedCol = false; }

                    string clothSqlAnd = BuildClothingSubfilterSqlAnd(conn, categoryTitle, clothingSubfilterForSql);
                    string loadedSqlAnd = "";
                    if (loadedState == 1)
                    {
                        // If the column doesn't exist, nothing can be "loaded".
                        if (!pkgHasLoadedCol) loadedSqlAnd = " AND 0";
                        else loadedSqlAnd = " AND ifnull(p.loaded,0) != 0";
                    }
                    else if (loadedState == 0)
                    {
                        // If the column doesn't exist, treat everything as unloaded (no extra filter needed).
                        if (pkgHasLoadedCol) loadedSqlAnd = " AND ifnull(p.loaded,0) = 0";
                    }

                    string nameSqlAnd = "";
                    if (nameTerms != null && nameTerms.Length > 0)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < nameTerms.Length; i++)
                        {
                            if (string.IsNullOrEmpty(nameTerms[i])) continue;
                            // Match against list_path (which includes package name) or internal_path.
                            sb.Append(" AND (m.list_path LIKE ? ESCAPE '\\' OR m.internal_path LIKE ? ESCAPE '\\')");
                        }
                        nameSqlAnd = sb.ToString();
                    }

                    string exclusionSqlAnd = "";
                    if (pathExclusions != null && pathExclusions.Count > 0)
                    {
                        var sb = new StringBuilder();
                        for (int i = 0; i < pathExclusions.Count; i++)
                        {
                            if (string.IsNullOrEmpty(pathExclusions[i])) continue;
                            sb.Append(" AND m.internal_path NOT LIKE ? ESCAPE '\\'");
                        }
                        exclusionSqlAnd = sb.ToString();
                    }

                    string tagSqlAnd = "";
                    List<string> activeTagsList = null;
                    if (activeTags != null && activeTags.Count > 0)
                    {
                        activeTagsList = new List<string>(activeTags);
                        var sb = new StringBuilder();
                        for (int i = 0; i < activeTagsList.Count; i++)
                        {
                            // Tags are stored in list_path as [tag] or similar.
                            // This is a heuristic; real tag filtering is complex.
                            sb.Append(" AND m.list_path LIKE ? ESCAPE '\\'");
                        }
                        tagSqlAnd = sb.ToString();
                    }

                    string loadedSelect = pkgHasLoadedCol ? "ifnull(p.loaded,'')" : "0";
                    string orderBy = "";
                    if (sortState != null)
                    {
                        string dir = sortState.Direction == SortDirection.Descending ? " DESC" : " ASC";
                        switch (sortState.Type)
                        {
                            case SortType.Date: orderBy = " ORDER BY p.wtime" + dir + ", m.list_path ASC"; break;
                            case SortType.Size: orderBy = " ORDER BY p.psize" + dir + ", m.list_path ASC"; break;
                            case SortType.DateCreated: orderBy = " ORDER BY p.pctime" + dir + ", m.list_path ASC"; break;
                        }
                    }

                    string sql =
                        "SELECT m.pkg_uid, m.internal_path, m.list_path, p.var_path, p.wtime, p.psize, p.pctime, ifnull(m.cloth_attr,''), " + loadedSelect + " FROM cat_mem m " +
                        "INNER JOIN pkg p ON p.uid = m.pkg_uid " +
                        "WHERE m.category = ? AND ((length(trim(?)) = 0) OR (p.creator = ?))" + clothSqlAnd + loadedSqlAnd + nameSqlAnd + exclusionSqlAnd + tagSqlAnd + orderBy;
                    
                    using (var stmt = conn.Prepare(sql))
                    {
                        int bind = 1;
                        stmt.BindText(bind++, categoryTitle);
                        string cf = creatorFilter ?? "";
                        stmt.BindText(bind++, cf);
                        stmt.BindText(bind++, cf);

                        if (nameTerms != null && nameTerms.Length > 0)
                        {
                            for (int i = 0; i < nameTerms.Length; i++)
                            {
                                if (string.IsNullOrEmpty(nameTerms[i])) continue;
                                string esc = "%" + EscapeLike(nameTerms[i]) + "%";
                                stmt.BindText(bind++, esc);
                                stmt.BindText(bind++, esc);
                            }
                        }

                        if (pathExclusions != null && pathExclusions.Count > 0)
                        {
                            for (int i = 0; i < pathExclusions.Count; i++)
                            {
                                if (string.IsNullOrEmpty(pathExclusions[i])) continue;
                                string esc = EscapeLike(pathExclusions[i]) + "%";
                                stmt.BindText(bind++, esc);
                            }
                        }

                        if (activeTagsList != null)
                        {
                            for (int i = 0; i < activeTagsList.Count; i++)
                            {
                                string esc = "%" + EscapeLike(activeTagsList[i]) + "%";
                                stmt.BindText(bind++, esc);
                            }
                        }

                        int step;
                        while ((step = stmt.Step()) == VpbSqlite3.SqliteRow)
                        {
                            Row r;
                            r.PackageUid = stmt.ColumnText(0);
                            r.InternalPath = stmt.ColumnText(1);
                            r.ListPath = stmt.ColumnText(2) ?? "";
                            r.VarPath = stmt.ColumnText(3) ?? "";
                            r.LastWriteTicksOrInvalid = stmt.ColumnInt64(4);
                            r.PackageSizeOrInvalid = stmt.ColumnInt64(5);
                            r.PackageCreationTicksOrInvalid = stmt.ColumnInt64(6);
                            r.ClothingAttrPacked = (int)stmt.ColumnInt64(7);
                            r.PackageIsLoaded = stmt.ColumnInt64(8) != 0;
                            if (r.PackageUid.Length > 0 && r.InternalPath.Length > 0)
                                outRows.Add(r);
                        }
                    }
                }
                swSql.Stop();
                stats.ExecutedQuery = true;
                stats.SqlElapsedMs = swSql.ElapsedMilliseconds;
                stats.RowsRead = outRows.Count;
                return true;
            }
            catch (Exception ex)
            {
                s_LastError = ex.Message;
                stats.RejectReason = "exception:" + ex.Message;
                return false;
            }
        }

        private static bool PkgHasLoadedColumn(VpbSqlite3.Connection conn)
        {
            if (conn == null) return false;
            using (var st = conn.Prepare("SELECT 1 FROM pragma_table_info('pkg') WHERE name='loaded' LIMIT 1;"))
            {
                int step = st.Step();
                return step == VpbSqlite3.SqliteRow;
            }
        }

        /// <summary>
        /// Reads package rows for a scoped set of UIDs from the local SQLite index (no full scan / no package resolution).
        /// Returns false if the index is unavailable or stale.
        /// </summary>
        internal static bool TryQueryPackageRowsForUids(HashSet<string> uids, List<PackageRow> outRows)
        {
            outRows.Clear();
            if (uids == null || uids.Count == 0) return true;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }

            // Require a published index for the current package inventory.
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
                return false;

            try
            {
                // SQLite default max variables is typically 999. Stay well under to allow future expansion.
                const int chunkSize = 400;
                var chunk = new List<string>(chunkSize);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    foreach (var uid in uids)
                    {
                        if (string.IsNullOrEmpty(uid)) continue;
                        chunk.Add(uid);
                        if (chunk.Count < chunkSize) continue;
                        if (!TryQueryPackageRowsChunk(conn, chunk, outRows)) return false;
                        chunk.Clear();
                    }
                    if (chunk.Count > 0)
                    {
                        if (!TryQueryPackageRowsChunk(conn, chunk, outRows)) return false;
                    }
                }
                return true;
            }
            catch
            {
                outRows.Clear();
                return false;
            }
        }

        private static string EscapeLike(string term)
        {
            if (string.IsNullOrEmpty(term)) return "";
            // Escape LIKE wildcards and the escape character itself.
            return term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        /// <summary>
        /// Reads package rows for a scoped ordered list of UIDs, applying an AND-of-terms filter on <c>pkg.uid</c> using SQL LIKE.
        /// Returns false if the index is unavailable or stale.
        /// </summary>
        internal static bool TryQueryPackageRowsForUidsWithAllTerms(List<string> orderedUids, string[] termsLower, List<PackageRow> outRows)
        {
            outRows.Clear();
            if (orderedUids == null || orderedUids.Count == 0) return true;
            if (termsLower == null || termsLower.Length == 0) return TryQueryPackageRowsForUids(new HashSet<string>(orderedUids, StringComparer.OrdinalIgnoreCase), outRows);
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }

            // Require a published index for the current package inventory.
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
                return false;

            try
            {
                const int chunkSize = 350; // leave headroom for term binds
                var chunk = new List<string>(chunkSize);
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    for (int i = 0; i < orderedUids.Count; i++)
                    {
                        string uid = orderedUids[i];
                        if (string.IsNullOrEmpty(uid)) continue;
                        chunk.Add(uid);
                        if (chunk.Count < chunkSize) continue;
                        if (!TryQueryPackageRowsChunkWithTerms(conn, chunk, termsLower, outRows)) return false;
                        chunk.Clear();
                    }
                    if (chunk.Count > 0)
                    {
                        if (!TryQueryPackageRowsChunkWithTerms(conn, chunk, termsLower, outRows)) return false;
                    }
                }
                return true;
            }
            catch
            {
                outRows.Clear();
                return false;
            }
        }

        private static bool TryQueryPackageRowsChunkWithTerms(VpbSqlite3.Connection conn, List<string> chunkUids, string[] termsLower, List<PackageRow> outRows)
        {
            if (conn == null || chunkUids == null || chunkUids.Count == 0) return true;

            var sb = new StringBuilder(128 + chunkUids.Count * 2 + (termsLower != null ? termsLower.Length * 32 : 0));
            sb.Append("SELECT uid, ifnull(var_path,''), wtime, psize, pctime, ifnull(loaded,'') FROM pkg WHERE uid IN (");
            for (int i = 0; i < chunkUids.Count; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append('?');
            }
            sb.Append(')');
            if (termsLower != null && termsLower.Length > 0)
            {
                for (int t = 0; t < termsLower.Length; t++)
                {
                    if (string.IsNullOrEmpty(termsLower[t])) continue;
                    sb.Append(" AND uid LIKE ? ESCAPE '\\'");
                }
            }
            sb.Append(';');

            using (var st = conn.Prepare(sb.ToString()))
            {
                int bind = 1;
                for (int i = 0; i < chunkUids.Count; i++)
                    st.BindText(bind++, chunkUids[i]);

                if (termsLower != null && termsLower.Length > 0)
                {
                    for (int t = 0; t < termsLower.Length; t++)
                    {
                        string term = termsLower[t];
                        if (string.IsNullOrEmpty(term)) continue;
                        string pat = "%" + EscapeLike(term) + "%";
                        st.BindText(bind++, pat);
                    }
                }

                int step;
                while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                {
                    PackageRow r;
                    r.PackageUid = st.ColumnText(0) ?? "";
                    r.VarPath = st.ColumnText(1) ?? "";
                    r.LastWriteTicksOrInvalid = long.MinValue;
                    r.PackageSizeOrInvalid = long.MinValue;
                    r.PackageCreationTicksOrInvalid = long.MinValue;
                    r.PackageIsLoaded = false;

                    string wtxt = st.ColumnText(2);
                    string sztxt = st.ColumnText(3);
                    string ctxt = st.ColumnText(4);
                    string loadedTxt = st.ColumnText(5) ?? "";

                    long wtL, szL, ctL;
                    if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wtL))
                        r.LastWriteTicksOrInvalid = wtL;
                    if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out szL))
                        r.PackageSizeOrInvalid = szL;
                    if (!string.IsNullOrEmpty(ctxt) && long.TryParse(ctxt, out ctL))
                        r.PackageCreationTicksOrInvalid = ctL;
                    int loadedInt = 0;
                    if (!string.IsNullOrEmpty(loadedTxt) && int.TryParse(loadedTxt, out loadedInt))
                        r.PackageIsLoaded = loadedInt != 0;
                    else
                        r.PackageIsLoaded = ComputePackageLoadedFlagFromVarPath(r.VarPath) != 0;

                    if (!string.IsNullOrEmpty(r.PackageUid))
                        outRows.Add(r);
                }
            }
            return true;
        }

        private static bool TryQueryPackageRowsChunk(VpbSqlite3.Connection conn, List<string> chunkUids, List<PackageRow> outRows)
        {
            if (conn == null || chunkUids == null || chunkUids.Count == 0) return true;

            var sb = new StringBuilder(96 + chunkUids.Count * 2);
            sb.Append("SELECT uid, ifnull(var_path,''), wtime, psize, pctime, ifnull(loaded,'') FROM pkg WHERE uid IN (");
            for (int i = 0; i < chunkUids.Count; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append('?');
            }
            sb.Append(");");

            using (var st = conn.Prepare(sb.ToString()))
            {
                for (int i = 0; i < chunkUids.Count; i++)
                    st.BindText(i + 1, chunkUids[i]);

                int step;
                while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                {
                    PackageRow r;
                    r.PackageUid = st.ColumnText(0) ?? "";
                    r.VarPath = st.ColumnText(1) ?? "";
                    r.LastWriteTicksOrInvalid = long.MinValue;
                    r.PackageSizeOrInvalid = long.MinValue;
                    r.PackageCreationTicksOrInvalid = long.MinValue;
                    r.PackageIsLoaded = false;

                    string wtxt = st.ColumnText(2);
                    string sztxt = st.ColumnText(3);
                    string ctxt = st.ColumnText(4);
                    string loadedTxt = st.ColumnText(5) ?? "";

                    long wtL, szL, ctL;
                    if (!string.IsNullOrEmpty(wtxt) && long.TryParse(wtxt, out wtL))
                        r.LastWriteTicksOrInvalid = wtL;
                    if (!string.IsNullOrEmpty(sztxt) && long.TryParse(sztxt, out szL))
                        r.PackageSizeOrInvalid = szL;
                    if (!string.IsNullOrEmpty(ctxt) && long.TryParse(ctxt, out ctL))
                        r.PackageCreationTicksOrInvalid = ctL;
                    int loadedInt = 0;
                    if (!string.IsNullOrEmpty(loadedTxt) && int.TryParse(loadedTxt, out loadedInt))
                        r.PackageIsLoaded = loadedInt != 0;
                    else
                        r.PackageIsLoaded = ComputePackageLoadedFlagFromVarPath(r.VarPath) != 0;

                    if (!string.IsNullOrEmpty(r.PackageUid))
                        outRows.Add(r);
                }
            }
            return true;
        }

        internal static bool TryReadRecursiveDependencyUids(string srcUid, HashSet<string> outUids)
        {
            if (outUids == null) return false;
            outUids.Clear();
            if (string.IsNullOrEmpty(srcUid)) return true;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
                return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                using (var st = conn.Prepare("SELECT dep_uid FROM pkg_dep WHERE src_uid = ?"))
                {
                    st.BindText(1, srcUid);
                    int step;
                    while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                    {
                        string d = st.ColumnText(0) ?? "";
                        if (!string.IsNullOrEmpty(d))
                            outUids.Add(d);
                    }
                }
                return true;
            }
            catch
            {
                outUids.Clear();
                return false;
            }
        }

        internal static bool TryReadDependentUids(string targetUid, string targetShort, HashSet<string> outUids)
        {
            if (outUids == null) return false;
            outUids.Clear();
            if (string.IsNullOrEmpty(targetUid)) return true;
            if (!VpbSqlite3.IsAvailable) return false;

            long scanBin = 0;
            try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }

            long readyScan = long.MinValue;
            string catSig = null;
            lock (s_Sync)
            {
                readyScan = s_ReadyScanBinary;
                catSig = s_ReadyCategoriesSig;
            }
            if (readyScan != scanBin || string.IsNullOrEmpty(catSig) || s_RebuildRunning)
                return false;

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    // Exact UID match always.
                    using (var st = conn.Prepare("SELECT DISTINCT src_uid FROM pkg_dep WHERE dep_uid = ?"))
                    {
                        st.BindText(1, targetUid);
                        int step;
                        while ((step = st.Step()) == VpbSqlite3.SqliteRow)
                        {
                            string u = st.ColumnText(0) ?? "";
                            if (!string.IsNullOrEmpty(u))
                                outUids.Add(u);
                        }
                    }

                    // Group match (Author.Name.*): includes .latest, .minX, numeric versions, etc.
                    if (!string.IsNullOrEmpty(targetShort))
                    {
                        using (var st2 = conn.Prepare("SELECT DISTINCT src_uid FROM pkg_dep WHERE dep_uid LIKE ?"))
                        {
                            st2.BindText(1, targetShort + ".%");
                            int step2;
                            while ((step2 = st2.Step()) == VpbSqlite3.SqliteRow)
                            {
                                string u = st2.ColumnText(0) ?? "";
                                if (!string.IsNullOrEmpty(u))
                                    outUids.Add(u);
                            }
                        }
                    }
                }
                return true;
            }
            catch
            {
                outUids.Clear();
                return false;
            }
        }
    }
}
