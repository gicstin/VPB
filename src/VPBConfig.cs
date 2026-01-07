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
        public bool _followDistance = false;
        public bool FollowDistance
        {
            get { return _followDistance || IsLoadingScene; }
            set { _followDistance = value; }
        }
        public float FollowDistanceMeters = 2.0f;
        public bool _followEyeHeight = false;
        public bool FollowEyeHeight
        {
            get { return _followEyeHeight || IsLoadingScene; }
            set { _followEyeHeight = value; }
        }
        public float ReorientStartAngle = 20f;
        public float MovementThreshold = 0.1f;
        public bool EnableGalleryFade = true;
        public bool EnableGalleryTranslucency = false;
        public float GalleryOpacity = 1.0f;
        public bool DragDropReplaceMode = false;
        public bool IsLoadingScene { get; private set; }

        public void StartSceneLoad()
        {
            IsLoadingScene = true;
            TriggerChange();
        }

        public void EndSceneLoad()
        {
            IsLoadingScene = false;
            TriggerChange();
        }

        public delegate void OnConfigChanged();
        public event OnConfigChanged ConfigChanged;

        public void Load()
        {
            // Reset to defaults before loading
            EnableButtonGaps = true;
            ShowSideButtons = "Both";
            FollowAngle = true;
            _followDistance = false;
            FollowDistanceMeters = 2.0f;
            _followEyeHeight = false;
            ReorientStartAngle = 20f;
            MovementThreshold = 0.1f;
            EnableGalleryFade = true;
            EnableGalleryTranslucency = false;
            GalleryOpacity = 1.0f;
            DragDropReplaceMode = false;

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
                        if (node["FollowDistance"] != null) _followDistance = node["FollowDistance"].AsBool;
                        if (node["FollowDistanceMeters"] != null) FollowDistanceMeters = node["FollowDistanceMeters"].AsFloat;
                        if (node["FollowEyeHeight"] != null) _followEyeHeight = node["FollowEyeHeight"].AsBool;
                        if (node["ReorientStartAngle"] != null) ReorientStartAngle = node["ReorientStartAngle"].AsFloat;
                        if (node["MovementThreshold"] != null) MovementThreshold = node["MovementThreshold"].AsFloat;
                        if (node["EnableGalleryFade"] != null) EnableGalleryFade = node["EnableGalleryFade"].AsBool;
                        if (node["EnableGalleryTranslucency"] != null) EnableGalleryTranslucency = node["EnableGalleryTranslucency"].AsBool;
                        if (node["GalleryOpacity"] != null) GalleryOpacity = node["GalleryOpacity"].AsFloat;
                        if (node["DragDropReplaceMode"] != null) DragDropReplaceMode = node["DragDropReplaceMode"].AsBool;
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
                node["FollowDistance"].AsBool = _followDistance;
                node["FollowDistanceMeters"].AsFloat = FollowDistanceMeters;
                node["FollowEyeHeight"].AsBool = _followEyeHeight;
                node["ReorientStartAngle"].AsFloat = ReorientStartAngle;
                node["MovementThreshold"].AsFloat = MovementThreshold;
                node["EnableGalleryFade"].AsBool = EnableGalleryFade;
                node["EnableGalleryTranslucency"].AsBool = EnableGalleryTranslucency;
                node["GalleryOpacity"].AsFloat = GalleryOpacity;
                node["DragDropReplaceMode"].AsBool = DragDropReplaceMode;
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
