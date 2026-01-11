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
        public string _followAngle = "Both"; // "Off", "Desktop", "VR", "Both"
        public string FollowAngle
        {
            get { return _followAngle; }
            set { _followAngle = value; }
        }
        public string _followDistance = "VR"; // "Off", "Desktop", "VR", "Both"
        public string FollowDistance
        {
            get { return _followDistance; }
            set { _followDistance = value; }
        }
        public string _followEyeHeight = "VR"; // "Off", "Desktop", "VR", "Both"
        public string FollowEyeHeight
        {
            get { return _followEyeHeight; }
            set { _followEyeHeight = value; }
        }
        public float ReorientStartAngle = 20f;
        public float MovementThreshold = 0.1f;
        public bool EnableCurvature = false;
        public float CurvatureIntensity = 1.0f;
        public bool EnableGalleryFade = true;
        public bool EnableGalleryTranslucency = false;
        public float GalleryOpacity = 1.0f;
        public bool DragDropReplaceMode = false;
        public bool DesktopFixedMode = false;
        public bool DesktopFixedAutoCollapse = false;
        public bool IsLoadingScene { get; private set; }

        private bool? _isDevMode;
        public bool IsDevMode
        {
            get
            {
                if (!_isDevMode.HasValue)
                {
                    try
                    {
                        string assemblyLocation = typeof(VPBConfig).Assembly.Location;
                        if (!string.IsNullOrEmpty(assemblyLocation))
                        {
                            string devModeFile = Path.Combine(Path.GetDirectoryName(assemblyLocation), ".DevMode");
                            if (File.Exists(devModeFile))
                            {
                                _isDevMode = true;
                                return true;
                            }
                        }
                    }
                    catch
                    {
                    }

                    // Fallback to config-based dev mode
                    _isDevMode = _isDevModeFromConfig;
                }
                return _isDevMode.Value;
            }
            set
            {
                _isDevMode = value;
                _isDevModeFromConfig = value;
            }
        }
        private bool _isDevModeFromConfig = false;

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
            _followAngle = "Both";
            _followDistance = "VR";
            _followEyeHeight = "VR";
            ReorientStartAngle = 20f;
            MovementThreshold = 0.1f;
            EnableCurvature = false;
            CurvatureIntensity = 1.0f;
            EnableGalleryFade = true;
            EnableGalleryTranslucency = false;
            GalleryOpacity = 1.0f;
            DragDropReplaceMode = false;
            DesktopFixedMode = false;
            DesktopFixedAutoCollapse = false;

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
                        
                        // Handle legacy bools if they exist, or just use string
                        if (node["FollowAngle"] != null) {
                            string val = node["FollowAngle"].Value;
                            if (val == "true" || val == "True") 
                                _followAngle = "Both";
                            else if (val == "false" || val == "False") 
                                _followAngle = "Off";
                            else 
                                _followAngle = val;
                        }

                        if (node["FollowDistance"] != null) {
                            string val = node["FollowDistance"].Value;
                            if (val == "true" || val == "True") 
                                _followDistance = "Both";
                            else if (val == "false" || val == "False") 
                                _followDistance = "Off";
                            else 
                                _followDistance = val;
                        }

                        if (node["FollowEyeHeight"] != null) {
                            string val = node["FollowEyeHeight"].Value;
                            if (val == "true" || val == "True") 
                                _followEyeHeight = "Both";
                            else if (val == "false" || val == "False") 
                                _followEyeHeight = "Off";
                            else 
                                _followEyeHeight = val;
                        }
                        
                        if (node["ReorientStartAngle"] != null) ReorientStartAngle = node["ReorientStartAngle"].AsFloat;
                        if (node["MovementThreshold"] != null) MovementThreshold = node["MovementThreshold"].AsFloat;
                        // if (node["EnableCurvature"] != null) EnableCurvature = node["EnableCurvature"].AsBool;
                        EnableCurvature = false; // Force disabled for now
                        if (node["CurvatureIntensity"] != null) CurvatureIntensity = node["CurvatureIntensity"].AsFloat;
                        if (node["EnableGalleryFade"] != null) EnableGalleryFade = node["EnableGalleryFade"].AsBool;
                        if (node["EnableGalleryTranslucency"] != null) EnableGalleryTranslucency = node["EnableGalleryTranslucency"].AsBool;
                        if (node["GalleryOpacity"] != null) GalleryOpacity = node["GalleryOpacity"].AsFloat;
                        if (node["DragDropReplaceMode"] != null) DragDropReplaceMode = node["DragDropReplaceMode"].AsBool;
                        if (node["DesktopFixedMode"] != null) DesktopFixedMode = node["DesktopFixedMode"].AsBool;
                        if (node["DesktopFixedAutoCollapse"] != null) DesktopFixedAutoCollapse = node["DesktopFixedAutoCollapse"].AsBool;
                        if (node["IsDevMode"] != null) _isDevModeFromConfig = node["IsDevMode"].AsBool;
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
                node["FollowAngle"].Value = _followAngle;
                node["FollowDistance"].Value = _followDistance;
                node["FollowEyeHeight"].Value = _followEyeHeight;
                node["ReorientStartAngle"].AsFloat = ReorientStartAngle;
                node["MovementThreshold"].AsFloat = MovementThreshold;
                node["EnableCurvature"].AsBool = EnableCurvature;
                node["CurvatureIntensity"].AsFloat = CurvatureIntensity;
                node["EnableGalleryFade"].AsBool = EnableGalleryFade;
                node["EnableGalleryTranslucency"].AsBool = EnableGalleryTranslucency;
                node["GalleryOpacity"].AsFloat = GalleryOpacity;
                node["DragDropReplaceMode"].AsBool = DragDropReplaceMode;
                node["DesktopFixedMode"].AsBool = DesktopFixedMode;
                node["DesktopFixedAutoCollapse"].AsBool = DesktopFixedAutoCollapse;
                node["IsDevMode"].AsBool = IsDevMode;
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

        public bool IsFollowEnabled(string setting)
        {
            if (IsLoadingScene) return true;
            if (setting == "Off") return false;
            if (setting == "Both") return true;

            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }

            if (setting == "VR") return isVR;
            if (setting == "Desktop") return !isVR;

            return false;
        }
    }
}
