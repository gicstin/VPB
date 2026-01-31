using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Valve.Newtonsoft.Json;

namespace VPB
{
    public class RatingsManager
    {
        [Serializable]
        public class SerializableRating
        {
            public string uid;
            public int rating;
        }

        [Serializable]
        public class SerializableRatings
        {
            public List<SerializableRating> ratings = new List<SerializableRating>();
        }

        private static RatingsManager _instance;
        public static RatingsManager Instance
        {
            get
            {
                if (_instance == null) _instance = new RatingsManager();
                return _instance;
            }
        }

        private string jsonPath;
        private Dictionary<string, int> ratings = new Dictionary<string, int>();
        private readonly object lockObj = new object();
        private bool hasLoadedSuccessfully = false;
        private bool legacyFavImportComplete = false;

        public RatingsManager()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(baseDir, "Saves");
                saveDir = Path.Combine(saveDir, "PluginData");
                saveDir = Path.Combine(saveDir, "VPB");

                if (!Directory.Exists(saveDir))
                {
                    Directory.CreateDirectory(saveDir);
                }
                
                jsonPath = Path.Combine(saveDir, "ratings.json");
                Debug.Log("[VPB] RatingsManager: Using JSON path: " + jsonPath);

                // Load existing JSON if it exists
                Load();

                try
                {
                    FileManager.RegisterRefreshHandler(OnFileManagerRefresh);
                }
                catch { }

                // Legacy migration check (can merge into existing)
                string oldPath = Path.Combine(saveDir, "ratings.bin");
                if (File.Exists(oldPath))
                {
                    Debug.Log("[VPB] RatingsManager: Found legacy ratings.bin, migrating...");
                    int countBefore = ratings.Count;
                    LoadLegacyBinary(oldPath);
                    int countAfter = ratings.Count;
                    
                    if (countAfter > countBefore)
                    {
                        Debug.Log("[VPB] RatingsManager: Migrated " + (countAfter - countBefore) + " new ratings from legacy binary.");
                        hasLoadedSuccessfully = true;
                        Save();
                    }
                    
                    try 
                    { 
                        string migratedPath = oldPath + ".migrated";
                        if (File.Exists(migratedPath)) File.Delete(migratedPath);
                        File.Move(oldPath, migratedPath);
                        Debug.Log("[VPB] RatingsManager: Renamed legacy binary to .migrated");
                    } 
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[VPB] RatingsManager: Failed to rename legacy binary: " + ex.Message);
                    }
                }

                TryImportLegacyFavRatings();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] RatingsManager initialization failed: " + ex.Message);
            }
        }

        private void OnFileManagerRefresh()
        {
            TryImportLegacyFavRatings();
        }

        private void TryImportLegacyFavRatings()
        {
            if (legacyFavImportComplete) return;
            if (!hasLoadedSuccessfully) return;
            if (FileManager.PackagesByUid == null || FileManager.PackagesByUid.Count == 0) return;

            bool anyAdded = false;
            try
            {
                // Native VaM favorites live in AddonPackagesFilePrefs\<packageUid>\...\<resource>.<ext>.fav
                // and are typically empty marker files.
                string prefsDir = GetAddonPackagesFilePrefsDir();
                if (!string.IsNullOrEmpty(prefsDir) && Directory.Exists(prefsDir))
                {
                    string[] favFiles;
                    try { favFiles = Directory.GetFiles(prefsDir, "*.fav", SearchOption.AllDirectories); }
                    catch { favFiles = new string[0]; }

                    foreach (var favFile in favFiles)
                    {
                        int rating = TryReadLegacyFavRating(favFile);
                        if (rating <= 0) continue;

                        if (TryParseLegacyFavToRatingKey(prefsDir, favFile, out string key))
                        {
                            if (TryAddRatingIfMissing(key, rating)) anyAdded = true;
                        }
                    }
                }
            }
            catch { }

            if (anyAdded) Save();
            legacyFavImportComplete = true;
        }

        private int TryReadLegacyFavRating(string favPath)
        {
            try
            {
                string txt = File.ReadAllText(favPath);
                if (IsNullOrWhiteSpace(txt)) return 1;
                if (int.TryParse(txt.Trim(), out int r)) return Mathf.Clamp(r, 1, 5);
            }
            catch { }
            return 1;
        }

        private bool IsNullOrWhiteSpace(string s)
        {
            if (s == null) return true;
            for (int i = 0; i < s.Length; i++)
            {
                if (!char.IsWhiteSpace(s[i])) return false;
            }
            return true;
        }

        private bool TryParseLegacyFavToRatingKey(string prefsDir, string favFile, out string key)
        {
            key = null;
            try
            {
                if (string.IsNullOrEmpty(prefsDir) || string.IsNullOrEmpty(favFile)) return false;

                string rel = MakeRelativePath(prefsDir, favFile);
                if (string.IsNullOrEmpty(rel)) return false;

                rel = rel.Replace('\\', '/');
                if (rel.StartsWith("./")) rel = rel.Substring(2);
                if (rel.StartsWith("../")) return false;

                int slash = rel.IndexOf('/');
                if (slash <= 0) return false;

                string pkgUid = rel.Substring(0, slash);
                string rest = rel.Substring(slash + 1);
                if (string.IsNullOrEmpty(pkgUid) || string.IsNullOrEmpty(rest)) return false;
                if (!rest.EndsWith(".fav", StringComparison.OrdinalIgnoreCase)) return false;

                // remove .fav -> get original relative file reference used by VaM
                string originalRef = rest.Substring(0, rest.Length - 4);
                originalRef = originalRef.Replace('\\', '/');

                // Case 1: package-level .var marker (e.g. AddonPackages/Foo.Bar.1.var)
                if (originalRef.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) ||
                    originalRef.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase))
                {
                    key = originalRef;
                    return true;
                }

                // Case 2: a file inside a var package: key is the VarFileEntry uid "<pkgUid>:/<internalPath>"
                key = pkgUid + ":/" + originalRef;
                return true;
            }
            catch { }

            return false;
        }

        private string MakeRelativePath(string basePath, string fullPath)
        {
            try
            {
                if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(fullPath)) return null;

                string b = Path.GetFullPath(basePath).TrimEnd('\\', '/');
                string f = Path.GetFullPath(fullPath);

                if (!f.StartsWith(b, StringComparison.OrdinalIgnoreCase)) return null;
                if (f.Length == b.Length) return "";

                string rel = f.Substring(b.Length);
                if (rel.StartsWith("\\") || rel.StartsWith("/")) rel = rel.Substring(1);
                return rel;
            }
            catch { }
            return null;
        }

        private string GetAddonPackagesFilePrefsDir()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                return Path.Combine(baseDir, "AddonPackagesFilePrefs");
            }
            catch { }
            return null;
        }

        private bool TryAddRatingIfMissing(string uid, int rating)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            rating = Mathf.Clamp(rating, 0, 5);
            if (rating <= 0) return false;

            lock (lockObj)
            {
                if (ratings.TryGetValue(uid, out int existing) && existing > 0) return false;
                ratings[uid] = rating;
                return true;
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            lock (lockObj)
            {
                ratings.Clear(); // Always clear before loading fresh from JSON
                string backupPath = jsonPath + ".bak";
                bool mainExists = File.Exists(jsonPath);
                bool backupExists = File.Exists(backupPath);

                if (mainExists)
                {
                    if (TryLoadFile(jsonPath))
                    {
                        Debug.Log("[VPB] RatingsManager: Loaded " + ratings.Count + " ratings from ratings.json");
                        hasLoadedSuccessfully = true;
                        return;
                    }
                    Debug.LogWarning("[VPB] ratings.json appears corrupt or empty, trying backup...");
                }

                if (backupExists)
                {
                    if (TryLoadFile(backupPath))
                    {
                        Debug.Log("[VPB] Successfully restored " + ratings.Count + " ratings from backup.");
                        hasLoadedSuccessfully = true;
                        // Restore main file from backup immediately
                        try { File.Copy(backupPath, jsonPath, true); } catch {}
                        return;
                    }
                }

                // If we get here and main file existed but failed, it's a real failure
                if (mainExists || backupExists)
                {
                    Debug.LogError("[VPB] RatingsManager: Failed to load existing ratings from JSON or backup.");
                }
                else
                {
                    Debug.Log("[VPB] RatingsManager: No existing ratings found, starting fresh.");
                }

                ratings.Clear();
                hasLoadedSuccessfully = true; // Even if empty, we start fresh
            }
        }

        private bool TryLoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                
                string json;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }

                if (string.IsNullOrEmpty(json) || json.Trim().Length < 2) return false;

                SerializableRatings data;
                lock (LogUtil.JsonLock)
                {
                    data = JsonConvert.DeserializeObject<SerializableRatings>(json);
                }
                if (data != null && data.ratings != null)
                {
                    foreach (var item in data.ratings)
                    {
                        if (!string.IsNullOrEmpty(item.uid))
                            ratings[item.uid] = item.rating;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Error parsing " + Path.GetFileName(path) + ": " + ex.Message);
            }
            return false;
        }

        private void LoadLegacyBinary(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    while (fs.Position < fs.Length)
                    {
                        try 
                        {
                            if (fs.Position + 5 > fs.Length) break;
                            byte rating = reader.ReadByte();
                            int length = reader.ReadInt32();
                            if (length < 0 || fs.Position + length > fs.Length) break;
                            byte[] bytes = reader.ReadBytes(length);
                            string uid = Encoding.UTF8.GetString(bytes);
                            if (rating > 0) ratings[uid] = rating;
                        }
                        catch { break; }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Legacy binary migration failed: " + ex.Message);
            }
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;
            if (!hasLoadedSuccessfully)
            {
                Debug.LogWarning("[VPB] RatingsManager: Skipping save because load failed or hasn't completed.");
                return;
            }

            lock (lockObj)
            {
                try
                {
                    var data = new SerializableRatings();
                    foreach (var kvp in ratings)
                    {
                        data.ratings.Add(new SerializableRating { uid = kvp.Key, rating = kvp.Value });
                    }

                    string json;
                    lock (LogUtil.JsonLock)
                    {
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    }
                    if (string.IsNullOrEmpty(json))
                    {
                        Debug.LogError("[VPB] Serialization returned empty string, aborting save.");
                        return;
                    }

                    string tmpPath = jsonPath + ".tmp";
                    File.WriteAllText(tmpPath, json);

                    // Verify tmp file was written correctly
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length < 2)
                    {
                        Debug.LogError("[VPB] Failed to write temporary ratings file, aborting save.");
                        return;
                    }

                    string backupPath = jsonPath + ".bak";
                    
                    // Rotate files
                    if (File.Exists(jsonPath))
                    {
                        // Only overwrite backup if main file is not empty
                        if (new FileInfo(jsonPath).Length > 2)
                        {
                            try 
                            { 
                                if (File.Exists(backupPath)) File.Delete(backupPath);
                                File.Move(jsonPath, backupPath);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning("[VPB] Failed to create backup: " + ex.Message);
                                // Continue anyway, we have the .tmp
                                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                            }
                        }
                        else
                        {
                            // If main is somehow empty, just delete it and keep the old backup
                            File.Delete(jsonPath);
                        }
                    }
                    
                    File.Move(tmpPath, jsonPath);
                    Debug.Log("[VPB] RatingsManager: Successfully saved " + ratings.Count + " ratings to " + jsonPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VPB] RatingsManager: Error saving ratings.json: " + ex.Message);
                }
            }
        }

        public int GetRating(FileEntry entry)
        {
            if (entry == null) return 0;
            lock (lockObj)
            {
                if (ratings.TryGetValue(entry.Uid, out int r)) return r;
            }
            return 0;
        }

        public void SetRating(FileEntry entry, int rating)
        {
            if (entry == null) return;
            rating = Mathf.Clamp(rating, 0, 5);

            lock (lockObj)
            {
                int current = 0;
                ratings.TryGetValue(entry.Uid, out current);
                if (current == rating) return;

                if (rating > 0) ratings[entry.Uid] = rating;
                else ratings.Remove(entry.Uid);
            }

            Save();

            TrySyncLegacyFavFile(entry, rating);
        }

        private void TrySyncLegacyFavFile(FileEntry entry, int rating)
        {
            try
            {
                if (entry == null) return;
                if (rating < 0) return;

                string prefsDir = GetAddonPackagesFilePrefsDir();
                if (string.IsNullOrEmpty(prefsDir)) return;
                if (!Directory.Exists(prefsDir)) Directory.CreateDirectory(prefsDir);

                string favPath = null;
                if (!TryBuildLegacyFavPath(prefsDir, entry, out favPath)) return;
                string markerPath = favPath + ".vpb";

                bool favExists = false;
                try { favExists = File.Exists(favPath); } catch { favExists = false; }
                bool ownedByVPB = false;
                try { ownedByVPB = File.Exists(markerPath); } catch { ownedByVPB = false; }

                if (rating <= 0)
                {
                    // Safety: never delete user-managed legacy .fav files.
                    // Only delete if VPB previously created the file (marker exists).
                    if (ownedByVPB)
                    {
                        try { if (favExists) File.Delete(favPath); } catch { }
                        try { File.Delete(markerPath); } catch { }
                    }
                    return;
                }

                // Safety: don't overwrite existing .fav files unless VPB owns them.
                // If a user already has a legacy .fav marker, leave it alone.
                if (favExists && !ownedByVPB) return;

                string dir = Path.GetDirectoryName(favPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(favPath, Mathf.Clamp(rating, 1, 5).ToString());
                try { if (!ownedByVPB) File.WriteAllBytes(markerPath, new byte[] { 0 }); } catch { }
            }
            catch { }
        }

        private bool TryBuildLegacyFavPath(string prefsDir, FileEntry entry, out string favPath)
        {
            favPath = null;
            try
            {
                if (string.IsNullOrEmpty(prefsDir) || entry == null) return false;

                // Var-internal file
                if (entry is VarFileEntry vfe && vfe.Package != null)
                {
                    string pkgUid = vfe.Package.Uid;
                    string internalPath = vfe.InternalPath ?? "";
                    internalPath = internalPath.Replace('\\', '/');

                    string folder = Path.GetDirectoryName(internalPath)?.Replace('\\', '/');
                    string fileName = Path.GetFileName(internalPath);
                    if (string.IsNullOrEmpty(fileName)) return false;

                    string rel = string.IsNullOrEmpty(folder) ? fileName : (folder + "/" + fileName);
                    rel = rel.Replace('/', '\\');

                    favPath = Path.Combine(Path.Combine(prefsDir, pkgUid), rel + ".fav");
                    return true;
                }

                // Package (.var) file on disk
                if (entry is SystemFileEntry sfe && sfe.package != null)
                {
                    string pkgUid = sfe.package.Uid;
                    string sysPath = (sfe.Path ?? "").Replace('\\', '/');
                    if (string.IsNullOrEmpty(sysPath)) return false;

                    // Store under AddonPackagesFilePrefs\<pkgUid>\<sysPath>.fav
                    favPath = Path.Combine(Path.Combine(prefsDir, pkgUid), sysPath.Replace('/', '\\') + ".fav");
                    return true;
                }
            }
            catch { }

            return false;
        }
    }
}
