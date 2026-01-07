using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;

namespace VPB
{
    public class VPBConfig
    {
        private static VPBConfig _instance;
        public static VPBConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new VPBConfig();
                    _instance.Load();
                }
                return _instance;
            }
        }

        private string ConfigPath
        {
            get
            {
                // Try to find the plugin directory
                string path = Path.Combine(Application.dataPath, "../Custom/Scripts/VPB/VPB.cfg");
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return path;
            }
        }

        // Settings
        public bool EnableButtonGaps = true;
        public string ShowSideButtons = "Both"; // "Both", "Left", "Right"
        public bool FollowAngle = true;
        public bool FollowDistance = false;
        public float FollowDistanceMeters = 2.0f;

        public delegate void OnConfigChanged();
        public event OnConfigChanged ConfigChanged;

        public void Load()
        {
            // Reset to defaults before loading
            EnableButtonGaps = true;
            ShowSideButtons = "Both";
            FollowAngle = true;
            FollowDistance = false;
            FollowDistanceMeters = 2.0f;

            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    JSONNode node = JSON.Parse(json);
                    if (node != null)
                    {
                        if (node["EnableButtonGaps"] != null) EnableButtonGaps = node["EnableButtonGaps"].AsBool;
                        if (node["ShowSideButtons"] != null) ShowSideButtons = node["ShowSideButtons"].Value;
                        if (node["FollowAngle"] != null) FollowAngle = node["FollowAngle"].AsBool;
                        if (node["FollowDistance"] != null) FollowDistance = node["FollowDistance"].AsBool;
                        if (node["FollowDistanceMeters"] != null) FollowDistanceMeters = node["FollowDistanceMeters"].AsFloat;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Error loading config: " + ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                JSONClass node = new JSONClass();
                node["EnableButtonGaps"].AsBool = EnableButtonGaps;
                node["ShowSideButtons"].Value = ShowSideButtons;
                node["FollowAngle"].AsBool = FollowAngle;
                node["FollowDistance"].AsBool = FollowDistance;
                node["FollowDistanceMeters"].AsFloat = FollowDistanceMeters;
                File.WriteAllText(ConfigPath, node.ToString());
                // No need to Invoke ConfigChanged here if we want to control it from the UI or if Save is the final action.
                // Actually, Invoke is good if other components listen to file saves.
                ConfigChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] Error saving config: " + ex.Message);
            }
        }

        public void TriggerChange()
        {
            ConfigChanged?.Invoke();
        }
    }
}
