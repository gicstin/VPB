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
        public ConfigEntry<Vector2> QuickMenuCreateGalleryPosDesktop;
        public ConfigEntry<Vector2> QuickMenuCreateGalleryPosVR;
        public ConfigEntry<Vector2> QuickMenuShowHidePosDesktop;
        public ConfigEntry<Vector2> QuickMenuShowHidePosVR;
        public ConfigEntry<bool> QuickMenuCreateGalleryUseSameInVR;
        public ConfigEntry<bool> QuickMenuShowHideUseSameInVR;
        public ConfigEntry<bool> QuickMenuCreateGalleryEnabled;
        public ConfigEntry<bool> QuickMenuShowHideEnabled;
        public ConfigEntry<bool> PluginsAlwaysEnabled;
        public ConfigEntry<bool> EnableTextureOptimizations;
        public ConfigEntry<bool> ReduceTextureSize;
        public ConfigEntry<int> MinTextureSize;
        public ConfigEntry<bool> ForceTextureToMinSize;
        public ConfigEntry<bool> CacheAssetBundle;
        public ConfigEntry<int> MaxTextureSize;
        public ConfigEntry<bool> InflightDedupEnabled;
        public ConfigEntry<bool> PrioritizeFaceTextures;
        public ConfigEntry<bool> PrioritizeHairTextures;

        public ConfigEntry<string> LastGalleryPage;
        public ConfigEntry<int> TextureLogLevel;

        public ConfigEntry<bool> LogImageQueueEvents;
        public ConfigEntry<bool> LogVerboseUi;
        public ConfigEntry<bool> ScenePrewarmEnabled;
        public ConfigEntry<bool> EnableUiTransparency;
        public ConfigEntry<float> UiTransparencyValue;
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
            UIScale = config.Bind<float>("UI", "Scale", 1.5f, "Set UI Scale.");
            UIPosition = config.Bind<Vector2>("UI", "Position", Vector2.zero, "Set UI Position.");
            MiniMode = config.Bind<bool>("UI", "MiniMode", false, "Set Mini Mode.");
            QuickMenuCreateGalleryPosDesktop = config.Bind<Vector2>("UI", "QuickMenuCreateGalleryPosDesktop", new Vector2(-470f, -66f), "Anchored position for Quick Menu Create Gallery button in Desktop mode.");
            QuickMenuCreateGalleryPosVR = config.Bind<Vector2>("UI", "QuickMenuCreateGalleryPosVR", new Vector2(-470f, -66f), "Anchored position for Quick Menu Create Gallery button in VR mode.");
            QuickMenuShowHidePosDesktop = config.Bind<Vector2>("UI", "QuickMenuShowHidePosDesktop", new Vector2(-470f, -216f), "Anchored position for Quick Menu Show/Hide button in Desktop mode.");
            QuickMenuShowHidePosVR = config.Bind<Vector2>("UI", "QuickMenuShowHidePosVR", new Vector2(-470f, -216f), "Anchored position for Quick Menu Show/Hide button in VR mode.");
            QuickMenuCreateGalleryUseSameInVR = config.Bind<bool>("UI", "QuickMenuCreateGalleryUseSameInVR", true, "Use the same Quick Menu Create Gallery position in VR as Desktop.");
            QuickMenuShowHideUseSameInVR = config.Bind<bool>("UI", "QuickMenuShowHideUseSameInVR", true, "Use the same Quick Menu Show/Hide position in VR as Desktop.");
            QuickMenuCreateGalleryEnabled = config.Bind<bool>("UI", "QuickMenuCreateGalleryEnabled", true, "Show the Quick Menu Create Gallery button.");
            QuickMenuShowHideEnabled = config.Bind<bool>("UI", "QuickMenuShowHideEnabled", true, "Show the Quick Menu Show/Hide button.");
            PluginsAlwaysEnabled = config.Bind<bool>("Settings", "PluginsAlwaysEnabled", false, "Plugins will always enabled.");
            
            EnableTextureOptimizations = config.Bind<bool>("Optimze", "EnableTextureOptimizations", false, "Master toggle for all texture optimizations (caching, resizing, compression, prewarm, etc.).");
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
            AutoPageEnabled = config.Bind<bool>("UI", "AutoPageEnabled", false, "Enable Auto Paging in Gallery on scroll.");

            TextureLogLevel = config.Bind<int>("Logging", "TextureLogLevel", 1, "0=off, 1=summary only, 2=verbose per-texture trace.");
            LogImageQueueEvents = config.Bind<bool>("Logging", "LogImageQueueEvents", false, "Log IMGQ enqueue/dequeue events (very verbose).");
            LogVerboseUi = config.Bind<bool>("Logging", "LogVerboseUi", false, "Log verbose UI lifecycle messages (can be noisy).");


            LastGalleryPage = config.Bind<string>("UI", "LastGalleryPage", "CategoryHair", "Last opened Gallery page.");
        }
    }
}
