using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Prime31.MessageKit;

namespace VPB
{
    /// <summary>
    /// Persistent VAR zip manifest cache in SQLite (<c>pkg_manifest</c> blob payload).
    /// </summary>
    internal static partial class VpbLocalDatabase
    {
        const int PackageManifestSchemaVersion = 1;
        const string MetaManifestSchemaKey = "pkg_manifest_schema_v";

        internal static void EnsurePackageManifestSchema(VpbSqlite3.Connection conn)
        {
            if (conn == null) return;
            conn.ExecUtf8(
                "CREATE TABLE IF NOT EXISTS pkg_manifest (" +
                "uid TEXT PRIMARY KEY," +
                "var_size INTEGER NOT NULL," +
                "var_mtime INTEGER NOT NULL," +
                "is_invalid INTEGER NOT NULL DEFAULT 0," +
                "ictime INTEGER NOT NULL DEFAULT 0);" +
                "CREATE TABLE IF NOT EXISTS pkg_file (" +
                "pkg_uid TEXT NOT NULL," +
                "seq INTEGER NOT NULL," +
                "internal_path TEXT NOT NULL," +
                "wtime INTEGER NOT NULL," +
                "size INTEGER NOT NULL," +
                "PRIMARY KEY(pkg_uid, seq));" +
                "CREATE INDEX IF NOT EXISTS idx_pkg_file_uid ON pkg_file(pkg_uid);" +
                "CREATE INDEX IF NOT EXISTS idx_pkg_file_uid_path ON pkg_file(pkg_uid, internal_path);" +
                "CREATE TABLE IF NOT EXISTS pkg_manifest_dep (" +
                "pkg_uid TEXT NOT NULL," +
                "dep_uid TEXT NOT NULL," +
                "PRIMARY KEY(pkg_uid, dep_uid));" +
                "CREATE INDEX IF NOT EXISTS idx_pkg_manifest_dep_uid ON pkg_manifest_dep(pkg_uid);" +
                "CREATE TABLE IF NOT EXISTS pkg_manifest_cloth (" +
                "pkg_uid TEXT NOT NULL," +
                "internal_path TEXT NOT NULL," +
                "tags TEXT NOT NULL," +
                "PRIMARY KEY(pkg_uid, internal_path));" +
                "CREATE TABLE IF NOT EXISTS pkg_manifest_hair (" +
                "pkg_uid TEXT NOT NULL," +
                "internal_path TEXT NOT NULL," +
                "tags TEXT NOT NULL," +
                "PRIMARY KEY(pkg_uid, internal_path));");
            TryAddColumnIgnoreFailure(conn, "ALTER TABLE pkg_manifest ADD COLUMN payload BLOB;");
            try
            {
                using (var st = conn.Prepare("INSERT OR REPLACE INTO meta(k,v) VALUES(?,?)"))
                {
                    st.BindText(1, MetaManifestSchemaKey);
                    st.BindText(2, PackageManifestSchemaVersion.ToString());
                    st.Step();
                }
            }
            catch { }
        }

        internal static bool TryLoadPackageManifestsIntoLookup(
            Dictionary<string, SerializableVarPackage> lookup,
            out int loadedCount,
            out bool needsBlobUpgrade)
        {
            loadedCount = 0;
            needsBlobUpgrade = false;
            if (lookup == null) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                LogUtil.Log("VarPackageMgr SQL manifest load START");
            }
            catch { }

            try
            {
                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageManifestSchema(conn);

                    long manifestCount = ScalarInt64(conn, "SELECT COUNT(*) FROM pkg_manifest;");
                    if (manifestCount <= 0) return false;

                    long blobCount = ScalarInt64(conn,
                        "SELECT COUNT(*) FROM pkg_manifest WHERE payload IS NOT NULL AND length(payload) > 0;");
                    bool useBlobFastPath = blobCount > 0 && blobCount == manifestCount;
                    string loadMode = useBlobFastPath ? "blob" : "normalized";
                    needsBlobUpgrade = !useBlobFastPath;

                    var byUid = new Dictionary<string, SerializableVarPackage>((int)Math.Min(manifestCount, int.MaxValue),
                        StringComparer.OrdinalIgnoreCase);

                    if (useBlobFastPath)
                    {
                        using (var st = conn.Prepare(
                            "SELECT uid, var_size, var_mtime, is_invalid, ictime, payload FROM pkg_manifest ORDER BY uid"))
                        {
                            int progress = 0;
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                if (string.IsNullOrEmpty(uid)) continue;
                                var svp = new SerializableVarPackage
                                {
                                    VarFileSize = st.ColumnInt64(1),
                                    VarLastWriteTimeUtcTicks = st.ColumnInt64(2),
                                    IsInvalid = st.ColumnInt64(3) != 0,
                                    VarInternalCreationTimeBinary = st.ColumnInt64(4)
                                };
                                byte[] payload = st.ColumnBlob(5);
                                svp.DeserializePayloadBody(payload);
                                byUid[uid] = svp;
                                progress++;
                                if (progress % 2000 == 0)
                                {
                                    try
                                    {
                                        LogUtil.Log("VarPackageMgr SQL manifest load progress " + progress + "/" + manifestCount);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    else
                    {
                        try
                        {
                            LogUtil.Log("VarPackageMgr SQL manifest load using normalized tables (will upgrade to blob on next save)");
                        }
                        catch { }

                        using (var st = conn.Prepare(
                            "SELECT uid, var_size, var_mtime, is_invalid, ictime FROM pkg_manifest ORDER BY uid"))
                        {
                            int progress = 0;
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                if (string.IsNullOrEmpty(uid)) continue;
                                var svp = new SerializableVarPackage
                                {
                                    VarFileSize = st.ColumnInt64(1),
                                    VarLastWriteTimeUtcTicks = st.ColumnInt64(2),
                                    IsInvalid = st.ColumnInt64(3) != 0,
                                    VarInternalCreationTimeBinary = st.ColumnInt64(4),
                                    FileEntryNames = new List<string>(),
                                    FileEntryLastWriteTimeUtcTicks = new List<long>(),
                                    FileEntrySizes = new List<long>()
                                };
                                byUid[uid] = svp;
                                progress++;
                                if (progress % 2000 == 0)
                                {
                                    try
                                    {
                                        LogUtil.Log("VarPackageMgr SQL manifest load progress " + progress + "/" + manifestCount);
                                    }
                                    catch { }
                                }
                            }
                        }

                        using (var st = conn.Prepare(
                            "SELECT pkg_uid, internal_path, wtime, size FROM pkg_file ORDER BY pkg_uid, seq"))
                        {
                            int fileRows = 0;
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                if (string.IsNullOrEmpty(uid)) continue;
                                SerializableVarPackage svp;
                                if (!byUid.TryGetValue(uid, out svp) || svp == null) continue;
                                string path = (st.ColumnText(1) ?? "").Replace('\\', '/');
                                if (path.Length == 0) continue;
                                svp.FileEntryNames.Add(path);
                                svp.FileEntryLastWriteTimeUtcTicks.Add(st.ColumnInt64(2));
                                svp.FileEntrySizes.Add(st.ColumnInt64(3));
                                fileRows++;
                                if (fileRows % 500000 == 0)
                                {
                                    try
                                    {
                                        LogUtil.Log("VarPackageMgr SQL manifest load pkg_file rows=" + fileRows);
                                    }
                                    catch { }
                                }
                            }
                        }

                        using (var st = conn.Prepare("SELECT pkg_uid, dep_uid FROM pkg_manifest_dep ORDER BY pkg_uid"))
                        {
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                string dep = st.ColumnText(1);
                                if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(dep)) continue;
                                SerializableVarPackage svp;
                                if (!byUid.TryGetValue(uid, out svp) || svp == null) continue;
                                if (svp.RecursivePackageDependencies == null)
                                    svp.RecursivePackageDependencies = new List<string>();
                                svp.RecursivePackageDependencies.Add(dep);
                            }
                        }

                        using (var st = conn.Prepare(
                            "SELECT pkg_uid, internal_path, tags FROM pkg_manifest_cloth ORDER BY pkg_uid"))
                        {
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                if (string.IsNullOrEmpty(uid)) continue;
                                SerializableVarPackage svp;
                                if (!byUid.TryGetValue(uid, out svp) || svp == null) continue;
                                if (svp.ClothingFileEntryNames == null) svp.ClothingFileEntryNames = new List<string>();
                                if (svp.ClothingTags == null) svp.ClothingTags = new List<string>();
                                svp.ClothingFileEntryNames.Add((st.ColumnText(1) ?? "").Replace('\\', '/'));
                                svp.ClothingTags.Add(st.ColumnText(2) ?? "");
                            }
                        }

                        using (var st = conn.Prepare(
                            "SELECT pkg_uid, internal_path, tags FROM pkg_manifest_hair ORDER BY pkg_uid"))
                        {
                            while (st.Step() == VpbSqlite3.SqliteRow)
                            {
                                string uid = st.ColumnText(0);
                                if (string.IsNullOrEmpty(uid)) continue;
                                SerializableVarPackage svp;
                                if (!byUid.TryGetValue(uid, out svp) || svp == null) continue;
                                if (svp.HairFileEntryNames == null) svp.HairFileEntryNames = new List<string>();
                                if (svp.HairTags == null) svp.HairTags = new List<string>();
                                svp.HairFileEntryNames.Add((st.ColumnText(1) ?? "").Replace('\\', '/'));
                                svp.HairTags.Add(st.ColumnText(2) ?? "");
                            }
                        }
                    }

                    lookup.Clear();
                    foreach (var kv in byUid)
                    {
                        if (kv.Value == null) continue;
                        lookup[kv.Key] = kv.Value;
                        loadedCount++;
                    }

                    sw.Stop();
                    try
                    {
                        LogUtil.Log("VarPackageMgr SQL manifest load DONE pkgs=" + loadedCount
                            + " mode=" + loadMode + " ms=" + sw.ElapsedMilliseconds);
                    }
                    catch { }
                }
                return loadedCount > 0;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] TryLoadPackageManifestsIntoLookup failed: " + ex.Message); } catch { }
                return false;
            }
        }

        internal static bool TrySavePackageManifestSnapshot(Dictionary<string, SerializableVarPackage> snapshot)
        {
            if (snapshot == null || snapshot.Count == 0) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                try
                {
                    string dir = Path.GetDirectoryName(DbPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                }
                catch { }

                using (var conn = new VpbSqlite3.Connection(DbPath))
                {
                    EnsureSchema(conn);
                    EnsurePackageManifestSchema(conn);
                    conn.ExecUtf8("BEGIN IMMEDIATE;");
                    try
                    {
                        WriteManifestSnapshotToConnection(conn, snapshot);
                        conn.ExecUtf8("COMMIT;");
                    }
                    catch
                    {
                        try { conn.ExecUtf8("ROLLBACK;"); } catch { }
                        throw;
                    }
                }

                TryDeleteLegacyBytes2ManifestFiles();
                sw.Stop();
                long bytes = 0;
                try { if (File.Exists(DbPath)) bytes = new FileInfo(DbPath).Length; } catch { }
                LogUtil.Log("VarPackageMgr SQL manifest write " + snapshot.Count + " in " + sw.ElapsedMilliseconds
                    + "ms db_bytes=" + bytes + " mode=blob");
                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogError("Failed to write package manifest SQL: " + ex.Message); } catch { }
                return false;
            }
        }

        static void WriteManifestSnapshotToConnection(
            VpbSqlite3.Connection conn,
            Dictionary<string, SerializableVarPackage> snapshot)
        {
            conn.ExecUtf8(
                "DELETE FROM pkg_manifest_hair;" +
                "DELETE FROM pkg_manifest_cloth;" +
                "DELETE FROM pkg_manifest_dep;" +
                "DELETE FROM pkg_file;" +
                "DELETE FROM pkg_manifest;");

            using (var insManifest = conn.Prepare(
                "INSERT INTO pkg_manifest(uid,var_size,var_mtime,is_invalid,ictime,payload) VALUES(?,?,?,?,?,?)"))
            {
                int progress = 0;
                foreach (var item in snapshot)
                {
                    WriteManifestPackageBlobRow(item.Key, item.Value, insManifest);
                    progress++;
                    if (progress % 2000 == 0)
                    {
                        try
                        {
                            LogUtil.Log("VarPackageMgr SQL manifest write progress " + progress + "/" + snapshot.Count);
                        }
                        catch { }
                    }
                }
            }
        }

        static void WriteManifestPackageBlobRow(string uid, SerializableVarPackage svp, VpbSqlite3.Statement insManifest)
        {
            if (string.IsNullOrEmpty(uid) || svp == null) return;
            byte[] payload = svp.SerializePayloadBody();
            insManifest.BindText(1, uid);
            insManifest.BindInt64(2, svp.VarFileSize);
            insManifest.BindInt64(3, svp.VarLastWriteTimeUtcTicks);
            insManifest.BindInt64(4, svp.IsInvalid ? 1 : 0);
            insManifest.BindInt64(5, svp.VarInternalCreationTimeBinary);
            insManifest.BindBlob(6, payload);
            insManifest.Step();
            insManifest.Reset();
        }

        static void TryDeleteLegacyBytes2ManifestFiles()
        {
            foreach (string rel in new[] { "Cache/VPB/AllPackages.bytes2", "Cache/VPB/AllPackages.bytes2.migrated" })
            {
                try
                {
                    string path = rel;
                    try { path = Path.GetFullPath(path); } catch { }
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
        }
    }
}
