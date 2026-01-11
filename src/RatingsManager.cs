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
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] RatingsManager initialization failed: " + ex.Message);
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

                var data = JsonConvert.DeserializeObject<SerializableRatings>(json);
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

                    string json = JsonConvert.SerializeObject(data, Formatting.Indented);
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
        }
    }
}
