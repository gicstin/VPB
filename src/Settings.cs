using BepInEx.Configuration;
using System;
using UnityEngine;
namespace VPB
{
    class Settings
    {
        private static Settings instance;
        public static Settings Instance
        {
            get
            {
                if (instance == null) instance = new Settings();
                return instance;
            }
        }

        public ConfigEntry<string> UIKey;
        public ConfigEntry<string> GalleryKey;
        public ConfigEntry<string> CreateGalleryKey;
        public ConfigEntry<string> HubKey;
        public ConfigEntry<float> UIScale;
        public ConfigEntry<Vector2> UIPosition;
        public ConfigEntry<bool> MiniMode;
        public ConfigEntry<bool> PluginsAlwaysEnabled;
        public ConfigEntry<bool> ReduceTextureSize;
        public ConfigEntry<int> MinTextureSize;
        public ConfigEntry<bool> ForceTextureToMinSize;
        public ConfigEntry<bool> CacheAssetBundle;
        public ConfigEntry<int> MaxTextureSize;
        public ConfigEntry<bool> InflightDedupEnabled;
        public ConfigEntry<bool> PrioritizeFaceTextures;
        public ConfigEntry<bool> PrioritizeHairTextures;
        public ConfigEntry<bool> OptimizeGameObjectFind;
        public ConfigEntry<bool> OptimizePhysicsRaycast;
        public ConfigEntry<bool> OptimizeMeshNormals;
        public ConfigEntry<bool> OptimizeMeshBounds;
        public ConfigEntry<bool> OptimizeMeshTangents;

        public ConfigEntry<string> LastGalleryPage;
        public ConfigEntry<int> TextureLogLevel;

        public ConfigEntry<bool> LogImageQueueEvents;
        public ConfigEntry<bool> LogVerboseUi;
        public ConfigEntry<bool> CleanLogEnabled;
        public ConfigEntry<string> CleanLogPath;
        public ConfigEntry<bool> ScenePrewarmEnabled;
        public ConfigEntry<bool> EnableUiTransparency;
        public ConfigEntry<float> UiTransparencyValue;
        public ConfigEntry<bool> EnableGalleryFade;
        public ConfigEntry<bool> DragDropReplaceMode;
        public ConfigEntry<bool> AutoPageEnabled;
        
        internal static void Init(ConfigFile config)
        {
            Instance.Load(config);
        }
        private void Load(ConfigFile config)
        {
            UIKey = config.Bind<string>("UI", "UIKey", "Ctrl+V", "Shortcut key for Show/Hide Var Browser.");
            GalleryKey = config.Bind<string>("UI", "GalleryKey", "Ctrl+G", "Shortcut key for Show/Hide Gallery Panes.");
            CreateGalleryKey = config.Bind<string>("UI", "CreateGalleryKey", "Ctrl+N", "Shortcut key for Create Gallery Pane.");
            HubKey = config.Bind<string>("UI", "HubKey", "Ctrl+H", "Shortcut key for Open Hub Browser.");
            UIScale = config.Bind<float>("UI", "Scale", 1, "Set UI Scale.");
            UIPosition = config.Bind<Vector2>("UI", "Position", Vector2.zero, "Set UI Position.");
            MiniMode = config.Bind<bool>("UI", "MiniMode", false, "Set Mini Mode.");
            PluginsAlwaysEnabled = config.Bind<bool>("Settings", "PluginsAlwaysEnabled", false, "Plugins will always enabled.");
            
            ReduceTextureSize = config.Bind<bool>("Optimze", "ReduceTextureSize", false, "reduce texture size.");
            MinTextureSize = config.Bind<int>("Optimze", "MinTextureSize", 2048, "min size for resized texture.");
            ForceTextureToMinSize = config.Bind<bool>("Optimze", "ForceTextureToMinSize", false, "force resized textures to minimum size.");
            MaxTextureSize = config.Bind<int>("Optimze", "MaxTextureSize", 4096, "max size for texture.");
            CacheAssetBundle = config.Bind<bool>("Optimze", "CacheAssetBundle", true, "cache assetbundle.");
            InflightDedupEnabled = config.Bind<bool>("Optimze", "InflightDedupEnabled", false, "coalesce duplicate image requests while the first is still loading.");
            PrioritizeFaceTextures = config.Bind<bool>("Optimze", "PrioritizeFaceTextures", true, "prioritize face/makeup/overlay textures in VaM image load queue.");
            PrioritizeHairTextures = config.Bind<bool>("Optimze", "PrioritizeHairTextures", true, "prioritize hair in VaM image load queue.");
            ScenePrewarmEnabled = config.Bind<bool>("Optimze", "ScenePrewarmEnabled", true, "prewarm VPB_cache entries for scene textures during scene load.");

            EnableUiTransparency = config.Bind<bool>("UI", "EnableUiTransparency", true, "Enable dynamic UI transparency (fade when idle).");
            UiTransparencyValue = config.Bind<float>("UI", "UiTransparencyValue", 0.5f, "Transparency level when idle (0.0 = Opaque, 1.0 = Invisible).");
            EnableGalleryFade = config.Bind<bool>("UI", "EnableGalleryFade", true, "Enable Gallery Side Buttons Fade.");
            DragDropReplaceMode = config.Bind<bool>("UI", "DragDropReplaceMode", false, "Enable Replace mode for Drag and Drop in Gallery.");
            AutoPageEnabled = config.Bind<bool>("UI", "AutoPageEnabled", false, "Enable Auto Paging in Gallery on scroll.");
            
            OptimizeGameObjectFind = config.Bind<bool>("Unity Patches", "OptimizeGameObjectFind", true, "Cache GameObject.Find results.");
            OptimizePhysicsRaycast = config.Bind<bool>("Unity Patches", "OptimizePhysicsRaycast", true, "Cache Physics.Raycast results per frame.");
            OptimizeMeshNormals = config.Bind<bool>("Unity Patches", "OptimizeMeshNormals", true, "Debounce Mesh.RecalculateNormals calls.");
            OptimizeMeshBounds = config.Bind<bool>("Unity Patches", "OptimizeMeshBounds", true, "Debounce Mesh.RecalculateBounds calls.");
            OptimizeMeshTangents = config.Bind<bool>("Unity Patches", "OptimizeMeshTangents", true, "Debounce Mesh.RecalculateTangents calls.");

            TextureLogLevel = config.Bind<int>("Logging", "TextureLogLevel", 1, "0=off, 1=summary only, 2=verbose per-texture trace.");
            LogImageQueueEvents = config.Bind<bool>("Logging", "LogImageQueueEvents", false, "Log IMGQ enqueue/dequeue events (very verbose).");
            LogVerboseUi = config.Bind<bool>("Logging", "LogVerboseUi", false, "Log verbose UI lifecycle messages (can be noisy).");
            CleanLogEnabled = config.Bind<bool>("Logging", "CleanLogEnabled", true, "Write a separate clean VPB log file (no Unity Filename footer).");
            CleanLogPath = config.Bind<string>("Logging", "CleanLogPath", "BepInEx/LogOutput/VPB_clean.log", "Path for the clean VPB log file (relative to VaM folder).");

            LastGalleryPage = config.Bind<string>("UI", "LastGalleryPage", "CategoryHair", "Last opened Gallery page.");
        }
    }
}
