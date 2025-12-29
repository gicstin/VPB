using BepInEx.Configuration;
using System;
using UnityEngine;
namespace var_browser
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
        public ConfigEntry<string> CustomSceneKey;
        public ConfigEntry<string> CategorySceneKey;
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

        public ConfigEntry<string> LastGalleryPage;
        public ConfigEntry<int> TextureLogLevel;
        public ConfigEntry<int> TextureLogSlowConvertMs;
        public ConfigEntry<int> TextureLogSlowDiskMs;
        public ConfigEntry<bool> LogImageQueueEvents;
        public ConfigEntry<bool> LogVerboseUi;
        public ConfigEntry<bool> CleanLogEnabled;
        public ConfigEntry<string> CleanLogPath;
        public ConfigEntry<bool> ScenePrewarmEnabled;
        
        internal static void Init(ConfigFile config)
        {
            Instance.Load(config);
        }
        private void Load(ConfigFile config)
        {
            UIKey = config.Bind<string>("UI", "UIKey", "Ctrl+V", "Shortcut key for Show/Hide Var Browser.");
            CustomSceneKey = config.Bind<string>("UI", "CustomSceneKey", "Ctrl+Shift+Alpha1", "Shortcut key for open Custom Scene.");
            CategorySceneKey = config.Bind<string>("UI", "CategorySceneKey", "Ctrl+Shift+Alpha2", "Shortcut key for open Category Scene.");
            UIScale = config.Bind<float>("UI", "Scale", 1, "Set UI Scale.");
            UIPosition = config.Bind<Vector2>("UI", "Position", Vector2.zero, "Set UI Position.");
            MiniMode = config.Bind<bool>("UI", "MiniMode", false, "Set Mini Mode.");
            PluginsAlwaysEnabled = config.Bind<bool>("Settings", "PluginsAlwaysEnabled", false, "Plugins will always enabled.");
            
            ReduceTextureSize = config.Bind<bool>("Optimze", "ReduceTextureSize", false, "reduce texture size.");
            MinTextureSize = config.Bind<int>("Optimze", "MinTextureSize", 1024, "min size for resized texture.");
            ForceTextureToMinSize = config.Bind<bool>("Optimze", "ForceTextureToMinSize", false, "force resized textures to minimum size.");
            MaxTextureSize = config.Bind<int>("Optimze", "MaxTextureSize", 4096, "max size for texture.");
            CacheAssetBundle = config.Bind<bool>("Optimze", "CacheAssetBundle", true, "cache assetbundle.");
            InflightDedupEnabled = config.Bind<bool>("Optimze", "InflightDedupEnabled", false, "coalesce duplicate image requests while the first is still loading.");
            PrioritizeFaceTextures = config.Bind<bool>("Optimze", "PrioritizeFaceTextures", true, "prioritize face/makeup/overlay textures in VaM image load queue.");
            PrioritizeHairTextures = config.Bind<bool>("Optimze", "PrioritizeHairTextures", true, "prioritize hair in VaM image load queue.");
            ScenePrewarmEnabled = config.Bind<bool>("Optimze", "ScenePrewarmEnabled", true, "prewarm var_browser_cache entries for scene textures during scene load.");

            TextureLogLevel = config.Bind<int>("Logging", "TextureLogLevel", 1, "0=off, 1=summary only, 2=verbose per-texture trace.");
            TextureLogSlowConvertMs = config.Bind<int>("Logging", "TextureLogSlowConvertMs", 50, "Warn when texture compress/convert exceeds this duration (ms).");
            TextureLogSlowDiskMs = config.Bind<int>("Logging", "TextureLogSlowDiskMs", 20, "Warn when texture disk read/write exceeds this duration (ms).");
            LogImageQueueEvents = config.Bind<bool>("Logging", "LogImageQueueEvents", false, "Log IMGQ enqueue/dequeue events (very verbose).");
            LogVerboseUi = config.Bind<bool>("Logging", "LogVerboseUi", false, "Log verbose UI lifecycle messages (can be noisy).");
            CleanLogEnabled = config.Bind<bool>("Logging", "CleanLogEnabled", true, "Write a separate clean var_browser log file (no Unity Filename footer).");
            CleanLogPath = config.Bind<string>("Logging", "CleanLogPath", "BepInEx/LogOutput/var_browser_clean.log", "Path for the clean var_browser log file (relative to VaM folder).");

            LastGalleryPage = config.Bind<string>("UI", "LastGalleryPage", "CategoryHair", "Last opened Gallery page.");
        }
    }
}
