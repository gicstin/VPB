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
        private static string s_LastLoggedSavedGalleryCategory;
        private static string s_LastLoggedLoadedGalleryCategory;

        public static void ReloadFromDisk()
        {
            _instance = null;
        }

        public static string ReadLastGalleryCategoryFromDisk()
        {
            try
            {
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(baseDir, "Saves");
                saveDir = Path.Combine(saveDir, "PluginData");
                saveDir = Path.Combine(saveDir, "VPB");
                string path = Path.Combine(saveDir, "VPB.cfg");
                if (!File.Exists(path)) return "";

                string json = File.ReadAllText(path);
                JSONNode node = JSON.Parse(json);
                if (node == null) return "";
                if (node["LastGalleryCategory"] == null) return "";
                return node["LastGalleryCategory"].Value;
            }
            catch
            {
                return "";
            }
        }
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

        public string ConfigPathForDebug => ConfigPath;

        private string ConfigPath
        {
            get
            {
                // Use PluginData for persistence (works reliably even with hot reloads / read-only Custom folders).
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(baseDir, "Saves");
                saveDir = Path.Combine(saveDir, "PluginData");
                saveDir = Path.Combine(saveDir, "VPB");
                if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
                return Path.Combine(saveDir, "VPB.cfg");
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
        public float BringToFrontDistance = 1.5f;
        public float ReorientStartAngle = 20f;
        public float MovementThreshold = 0.1f;
        public bool EnableCurvature = false;
        public float CurvatureIntensity = 1.0f;
        public bool EnableGalleryFade = true;
        public bool EnableGalleryTranslucency = false;
        public float GalleryOpacity = 1.0f;
        public bool DragDropReplaceMode = false;
        public string ApplyMode = "DoubleClick";
        public string LastGalleryCategory = "";
        public bool DesktopFixedMode = false;
        public bool DesktopFixedAutoCollapse = true;
        public int DesktopFixedHeightMode = 0; // 0: Full, 1: Custom
        public float DesktopCustomHeight = 0.5f;
        public float DesktopCustomWidth = 1.618f / 2.618f;
        public bool EnableAutoFixedGallery = true;
        public float ListRowHeight = 100f;
        public int GridColumnCount = 4;
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

                    // Only .DevMode file enables dev mode
                    _isDevMode = false;
                }
                return _isDevMode.Value;
            }
            set
            {
                _isDevMode = value;
            }
        }

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
            LogUtil.Log("[VPBConfig.Load] Starting Load() from: " + ConfigPath);
            // Reset to defaults before loading
            EnableButtonGaps = true;
            ShowSideButtons = "Both";
            _followAngle = "Both";
            _followDistance = "VR";
            _followEyeHeight = "VR";
            BringToFrontDistance = 1.5f;
            ReorientStartAngle = 20f;
            MovementThreshold = 0.1f;
            EnableCurvature = false;
            CurvatureIntensity = 1.0f;
            EnableGalleryFade = true;
            EnableGalleryTranslucency = false;
            GalleryOpacity = 1.0f;
            DragDropReplaceMode = false;
            ApplyMode = "DoubleClick";
            LastGalleryCategory = "";
            DesktopFixedMode = false;
            DesktopFixedAutoCollapse = true;
            DesktopFixedHeightMode = 0;
            DesktopCustomHeight = 0.5f;
            DesktopCustomWidth = 1.618f / 2.618f;
            EnableAutoFixedGallery = true;
            ListRowHeight = 100f;
            GridColumnCount = 4;

            try
            {
                if (File.Exists(ConfigPath))
                {
                    string prevLastGalleryCategory = LastGalleryCategory;
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
                        
                        if (node["BringToFrontDistance"] != null) BringToFrontDistance = node["BringToFrontDistance"].AsFloat;
                        if (node["ReorientStartAngle"] != null) ReorientStartAngle = node["ReorientStartAngle"].AsFloat;
                        if (node["MovementThreshold"] != null) MovementThreshold = node["MovementThreshold"].AsFloat;
                        // if (node["EnableCurvature"] != null) EnableCurvature = node["EnableCurvature"].AsBool;
                        EnableCurvature = false; // Force disabled for now
                        if (node["CurvatureIntensity"] != null) CurvatureIntensity = node["CurvatureIntensity"].AsFloat;
                        if (node["EnableGalleryFade"] != null) EnableGalleryFade = node["EnableGalleryFade"].AsBool;
                        if (node["EnableGalleryTranslucency"] != null) EnableGalleryTranslucency = node["EnableGalleryTranslucency"].AsBool;
                        if (node["GalleryOpacity"] != null) GalleryOpacity = node["GalleryOpacity"].AsFloat;
                        if (node["DragDropReplaceMode"] != null) DragDropReplaceMode = node["DragDropReplaceMode"].AsBool;
                        if (node["ApplyMode"] != null) ApplyMode = node["ApplyMode"].Value;
                        if (node["LastGalleryCategory"] != null) LastGalleryCategory = node["LastGalleryCategory"].Value;
                        if (node["DesktopFixedMode"] != null) DesktopFixedMode = node["DesktopFixedMode"].AsBool;
                        if (node["DesktopFixedAutoCollapse"] != null) DesktopFixedAutoCollapse = node["DesktopFixedAutoCollapse"].AsBool;
                        if (node["DesktopFixedHeightMode"] != null) DesktopFixedHeightMode = node["DesktopFixedHeightMode"].AsInt;
                        if (node["DesktopCustomHeight"] != null) DesktopCustomHeight = node["DesktopCustomHeight"].AsFloat;
                        if (node["DesktopCustomWidth"] != null) DesktopCustomWidth = node["DesktopCustomWidth"].AsFloat;
                        if (node["EnableAutoFixedGallery"] != null) EnableAutoFixedGallery = node["EnableAutoFixedGallery"].AsBool;
                        if (node["ListRowHeight"] != null) ListRowHeight = node["ListRowHeight"].AsFloat;
                        if (node["GridColumnCount"] != null) GridColumnCount = node["GridColumnCount"].AsInt;
                    }

                    try
                    {
                        if (Settings.Instance != null && Settings.Instance.LogVerboseUi != null && Settings.Instance.LogVerboseUi.Value)
                            LogUtil.Log("[VPBConfig] Loaded cfg path=" + ConfigPath + " | LastGalleryCategory=" + LastGalleryCategory + " | DragDropReplaceMode=" + DragDropReplaceMode + " | ApplyMode=" + ApplyMode);
                    }
                    catch { }

                    try
                    {
                        if (!string.Equals(prevLastGalleryCategory, LastGalleryCategory, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(s_LastLoggedLoadedGalleryCategory, LastGalleryCategory, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(LastGalleryCategory))
                        {
                            s_LastLoggedLoadedGalleryCategory = LastGalleryCategory;
                            LogUtil.Log("[VPBConfig] Loaded LastGalleryCategory='" + LastGalleryCategory + "' from " + ConfigPath);
                        }
                    }
                    catch { }
                }
                else
                {
                    LogUtil.LogWarning("[VPBConfig.Load] Config file DOES NOT EXIST at: " + ConfigPath);
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
                string prevLogged = s_LastLoggedSavedGalleryCategory;
                JSONClass node = new JSONClass();
                node["EnableButtonGaps"].AsBool = EnableButtonGaps;
                node["ShowSideButtons"] = ShowSideButtons;
                node["FollowAngle"] = _followAngle;
                node["FollowDistance"] = _followDistance;
                node["FollowEyeHeight"] = _followEyeHeight;
                node["BringToFrontDistance"].AsFloat = BringToFrontDistance;
                node["ReorientStartAngle"].AsFloat = ReorientStartAngle;
                node["MovementThreshold"].AsFloat = MovementThreshold;
                node["EnableCurvature"].AsBool = EnableCurvature;
                node["CurvatureIntensity"].AsFloat = CurvatureIntensity;
                node["EnableGalleryFade"].AsBool = EnableGalleryFade;
                node["EnableGalleryTranslucency"].AsBool = EnableGalleryTranslucency;
                node["GalleryOpacity"].AsFloat = GalleryOpacity;
                node["DragDropReplaceMode"].AsBool = DragDropReplaceMode;
                node["ApplyMode"] = ApplyMode;
                node["LastGalleryCategory"] = LastGalleryCategory;
                node["DesktopFixedMode"].AsBool = DesktopFixedMode;
                node["DesktopFixedAutoCollapse"].AsBool = DesktopFixedAutoCollapse;
                node["DesktopFixedHeightMode"].AsInt = DesktopFixedHeightMode;
                node["DesktopCustomHeight"].AsFloat = DesktopCustomHeight;
                node["DesktopCustomWidth"].AsFloat = DesktopCustomWidth;
                node["EnableAutoFixedGallery"].AsBool = EnableAutoFixedGallery;
                node["ListRowHeight"].AsFloat = ListRowHeight;
                node["GridColumnCount"].AsInt = GridColumnCount;
                string jsonOutput = node.ToString();
                File.WriteAllText(ConfigPath, jsonOutput);

                try
                {
                    if (Settings.Instance != null && Settings.Instance.LogVerboseUi != null && Settings.Instance.LogVerboseUi.Value)
                        LogUtil.Log("[VPBConfig] Saved cfg path=" + ConfigPath + " | LastGalleryCategory=" + LastGalleryCategory + " | DragDropReplaceMode=" + DragDropReplaceMode + " | ApplyMode=" + ApplyMode);
                }
                catch { }

                try
                {
                    if (!string.Equals(prevLogged, LastGalleryCategory, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(LastGalleryCategory))
                    {
                        s_LastLoggedSavedGalleryCategory = LastGalleryCategory;
                    }
                }
                catch { }

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
