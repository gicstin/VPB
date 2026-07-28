using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valve.Newtonsoft.Json;

namespace VPB
{
    /// <summary>
    /// Local 0–5 star ratings keyed by creator name (case-insensitive).
    /// Separate from <see cref="RatingsManager"/> so creator keys never collide with file UIDs.
    /// Warm/cold path: lock + dict; Save on change (same pattern as package ratings).
    /// </summary>
    public class CreatorRatingsManager
    {
        [Serializable]
        public class SerializableRating
        {
            public string name;
            public int rating;
        }

        [Serializable]
        public class SerializableRatings
        {
            public List<SerializableRating> ratings = new List<SerializableRating>();
        }

        private static CreatorRatingsManager _instance;
        public static CreatorRatingsManager Instance
        {
            get
            {
                if (_instance == null) _instance = new CreatorRatingsManager();
                return _instance;
            }
        }

        private string jsonPath;
        private readonly Dictionary<string, int> ratings =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly object lockObj = new object();
        private bool hasLoadedSuccessfully;

        /// <summary>Bumped on every successful SetRating. Gallery virt signatures include this.</summary>
        public int DataRevision { get; private set; }

        public CreatorRatingsManager()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(baseDir, "Saves");
                saveDir = Path.Combine(saveDir, "PluginData");
                saveDir = Path.Combine(saveDir, "VPB");
                if (!Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                jsonPath = Path.Combine(saveDir, "creator_ratings.json");
                Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] CreatorRatingsManager init failed: " + ex.Message);
                hasLoadedSuccessfully = true;
            }
        }

        public int GetRating(string creatorName)
        {
            if (string.IsNullOrEmpty(creatorName)) return 0;
            lock (lockObj)
            {
                int r;
                if (ratings.TryGetValue(creatorName, out r)) return r;
            }
            return 0;
        }

        public void SetRating(string creatorName, int rating)
        {
            if (string.IsNullOrEmpty(creatorName)) return;
            rating = Mathf.Clamp(rating, 0, 5);

            bool changed = false;
            lock (lockObj)
            {
                int current = 0;
                ratings.TryGetValue(creatorName, out current);
                if (current == rating) return;

                if (rating > 0) ratings[creatorName] = rating;
                else ratings.Remove(creatorName);
                changed = true;
                unchecked { DataRevision++; }
            }

            if (changed) Save();
        }

        /// <summary>Empty → 5 (favorite shortcut); else step down; 1 → clear.</summary>
        public int CycleRating(string creatorName)
        {
            int cur = GetRating(creatorName);
            int next;
            if (cur <= 0) next = 5;
            else if (cur == 1) next = 0;
            else next = cur - 1;
            SetRating(creatorName, next);
            return next;
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            lock (lockObj)
            {
                ratings.Clear();
                string backupPath = jsonPath + ".bak";
                bool mainExists = File.Exists(jsonPath);
                bool backupExists = File.Exists(backupPath);

                if (mainExists && TryLoadFile(jsonPath))
                {
                    hasLoadedSuccessfully = true;
                    return;
                }

                if (mainExists)
                    Debug.LogWarning("[VPB] creator_ratings.json corrupt/empty, trying backup...");

                if (backupExists && TryLoadFile(backupPath))
                {
                    hasLoadedSuccessfully = true;
                    try { File.Copy(backupPath, jsonPath, true); } catch { }
                    return;
                }

                if (mainExists || backupExists)
                    Debug.LogError("[VPB] CreatorRatingsManager: failed to load ratings.");

                ratings.Clear();
                hasLoadedSuccessfully = true;
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
                if (data == null || data.ratings == null) return false;

                for (int i = 0; i < data.ratings.Count; i++)
                {
                    SerializableRating item = data.ratings[i];
                    if (item == null || string.IsNullOrEmpty(item.name)) continue;
                    int r = Mathf.Clamp(item.rating, 0, 5);
                    if (r > 0) ratings[item.name] = r;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Error parsing " + Path.GetFileName(path) + ": " + ex.Message);
            }
            return false;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;
            if (!hasLoadedSuccessfully)
            {
                Debug.LogWarning("[VPB] CreatorRatingsManager: skip save — load incomplete.");
                return;
            }

            lock (lockObj)
            {
                try
                {
                    var data = new SerializableRatings();
                    foreach (var kvp in ratings)
                    {
                        if (kvp.Value <= 0) continue;
                        data.ratings.Add(new SerializableRating { name = kvp.Key, rating = kvp.Value });
                    }

                    string json;
                    lock (LogUtil.JsonLock)
                    {
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    }
                    if (string.IsNullOrEmpty(json)) return;

                    string tmpPath = jsonPath + ".tmp";
                    File.WriteAllText(tmpPath, json);
                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length < 2) return;

                    string backupPath = jsonPath + ".bak";
                    if (File.Exists(jsonPath))
                    {
                        if (new FileInfo(jsonPath).Length > 2)
                        {
                            try
                            {
                                if (File.Exists(backupPath)) File.Delete(backupPath);
                                File.Move(jsonPath, backupPath);
                            }
                            catch
                            {
                                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                            }
                        }
                        else File.Delete(jsonPath);
                    }

                    File.Move(tmpPath, jsonPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VPB] CreatorRatingsManager save failed: " + ex.Message);
                }
            }
        }
    }
}
