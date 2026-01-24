using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Valve.Newtonsoft.Json;

namespace VPB
{
    public class AutoLoadPackagesManager
    {
        private static AutoLoadPackagesManager _instance;
        public static AutoLoadPackagesManager Instance
        {
            get
            {
                if (_instance == null) _instance = new AutoLoadPackagesManager();
                return _instance;
            }
        }

        private string jsonPath;
        private HashSet<string> autoLoadPackages = new HashSet<string>();
        private readonly object lockObj = new object();
        private bool hasLoadedSuccessfully = false;

        public AutoLoadPackagesManager()
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
                
                jsonPath = Path.Combine(saveDir, "autoload_packages.json");
                Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] AutoLoadPackagesManager initialization failed: " + ex.Message);
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            lock (lockObj)
            {
                autoLoadPackages.Clear();
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
                        try { File.Copy(backupPath, jsonPath, true); } catch {}
                        return;
                    }
                }

                autoLoadPackages.Clear();
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
                            autoLoadPackages.Add(item);
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
                    var data = new List<string>(autoLoadPackages);

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
                    Debug.LogError("[VPB] AutoLoadPackagesManager: Error saving autoload_packages.json: " + ex.Message);
                }
            }
        }

        public bool IsAutoLoad(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                return autoLoadPackages.Contains(uid);
            }
        }

        public void SetAutoLoad(string uid, bool autoLoad, bool save = true)
        {
            if (string.IsNullOrEmpty(uid)) return;

            lock (lockObj)
            {
                bool current = autoLoadPackages.Contains(uid);
                if (current == autoLoad) return;

                if (autoLoad) autoLoadPackages.Add(uid);
                else autoLoadPackages.Remove(uid);
            }

            if (save) Save();
        }

        public HashSet<string> GetAutoLoadPackages()
        {
            lock (lockObj)
            {
                return new HashSet<string>(autoLoadPackages);
            }
        }
    }
}
