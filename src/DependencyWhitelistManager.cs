using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valve.Newtonsoft.Json;

namespace VPB
{
    public class DependencyWhitelistManager
    {
        private static DependencyWhitelistManager _instance;
        public static DependencyWhitelistManager Instance
        {
            get
            {
                if (_instance == null) _instance = new DependencyWhitelistManager();
                return _instance;
            }
        }

        private string jsonPath;
        private readonly HashSet<string> whitelistedPackageGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object lockObj = new object();
        private bool hasLoadedSuccessfully = false;

        public DependencyWhitelistManager()
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

                jsonPath = Path.Combine(saveDir, "dependency_whitelist.json");
                Load();
                SyncToConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] DependencyWhitelistManager initialization failed: " + ex.Message);
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            lock (lockObj)
            {
                whitelistedPackageGroups.Clear();
                string backupPath = jsonPath + ".bak";
                bool mainExists = File.Exists(jsonPath);
                bool backupExists = File.Exists(backupPath);

                if (mainExists)
                {
                    if (TryLoadFile(jsonPath))
                    {
                        hasLoadedSuccessfully = true;
                        return;
                    }
                }

                if (backupExists)
                {
                    if (TryLoadFile(backupPath))
                    {
                        hasLoadedSuccessfully = true;
                        try { File.Copy(backupPath, jsonPath, true); } catch { }
                        return;
                    }
                }

                whitelistedPackageGroups.Clear();
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

                List<string> data;
                lock (LogUtil.JsonLock)
                {
                    data = JsonConvert.DeserializeObject<List<string>>(json);
                }

                if (data != null)
                {
                    foreach (var item in data)
                    {
                        if (!string.IsNullOrEmpty(item))
                            whitelistedPackageGroups.Add(item.Trim());
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

        public void Save()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;
            if (!hasLoadedSuccessfully) return;

            lock (lockObj)
            {
                try
                {
                    var data = new List<string>(whitelistedPackageGroups);

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
                        else
                        {
                            File.Delete(jsonPath);
                        }
                    }

                    File.Move(tmpPath, jsonPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VPB] DependencyWhitelistManager: Error saving dependency_whitelist.json: " + ex.Message);
                }
            }
        }

        public bool IsWhitelisted(string packageGroup)
        {
            if (string.IsNullOrEmpty(packageGroup)) return false;
            lock (lockObj)
            {
                return whitelistedPackageGroups.Contains(packageGroup);
            }
        }

        public void SetWhitelisted(string packageGroup, bool whitelisted, bool save = true)
        {
            if (string.IsNullOrEmpty(packageGroup)) return;

            lock (lockObj)
            {
                bool current = whitelistedPackageGroups.Contains(packageGroup);
                if (current == whitelisted) return;

                if (whitelisted) whitelistedPackageGroups.Add(packageGroup);
                else whitelistedPackageGroups.Remove(packageGroup);
            }

            SyncToConfig();
            if (save) Save();
        }

        public HashSet<string> GetWhitelistedPackageGroups()
        {
            lock (lockObj)
            {
                return new HashSet<string>(whitelistedPackageGroups, StringComparer.OrdinalIgnoreCase);
            }
        }

        private void SyncToConfig()
        {
            try
            {
                if (Settings.Instance == null || Settings.Instance.ForceLatestDependencyIgnorePackageGroups == null) return;

                HashSet<string> copy;
                lock (lockObj)
                {
                    copy = new HashSet<string>(whitelistedPackageGroups, StringComparer.OrdinalIgnoreCase);
                }

                string joined = string.Join(", ", new List<string>(copy).ToArray());
                Settings.Instance.ForceLatestDependencyIgnorePackageGroups.Value = joined;
            }
            catch
            {
            }
        }
    }
}
