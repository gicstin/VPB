using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

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

                // Legacy migration check
                string oldPath = Path.Combine(saveDir, "ratings.bin");
                if (File.Exists(oldPath) && !File.Exists(jsonPath))
                {
                    LoadLegacyBinary(oldPath);
                    Save();
                    try { File.Delete(oldPath); } catch {}
                }
                else
                {
                    Load();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("RatingsManager initialization failed: " + ex.Message);
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            lock (lockObj)
            {
                ratings.Clear();
                string backupPath = jsonPath + ".bak";

                if (!File.Exists(jsonPath) && File.Exists(backupPath))
                {
                    try { File.Copy(backupPath, jsonPath); } catch {}
                }

                if (!File.Exists(jsonPath)) return;

                try
                {
                    string json = File.ReadAllText(jsonPath);
                    var data = JsonUtility.FromJson<SerializableRatings>(json);
                    if (data != null && data.ratings != null)
                    {
                        foreach (var item in data.ratings)
                        {
                            if (!string.IsNullOrEmpty(item.uid))
                                ratings[item.uid] = item.rating;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("RatingsManager: Error loading ratings.json: " + ex.Message);
                }
            }
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
                            else ratings.Remove(uid);
                        }
                        catch { break; }
                    }
                }
            }
            catch {}
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            lock (lockObj)
            {
                try
                {
                    var data = new SerializableRatings();
                    foreach (var kvp in ratings)
                    {
                        data.ratings.Add(new SerializableRating { uid = kvp.Key, rating = kvp.Value });
                    }

                    string json = JsonUtility.ToJson(data, true); // pretty print for easy editing
                    string tmpPath = jsonPath + ".tmp";
                    File.WriteAllText(tmpPath, json);

                    if (File.Exists(jsonPath))
                    {
                        string backupPath = jsonPath + ".bak";
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        File.Move(jsonPath, backupPath);
                    }
                    
                    File.Move(tmpPath, jsonPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("RatingsManager: Error saving ratings.json: " + ex.Message);
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
                int current = GetRating(entry);
                if (current == rating) return;

                if (rating > 0) ratings[entry.Uid] = rating;
                else ratings.Remove(entry.Uid);
            }

            Save();
        }
    }
}
