using System;
using System.IO;

namespace VPB
{

    // 1. Package names cannot be corrected.
    class GlobalInfo
    {
        static readonly object InitLock = new object();
        static bool pathsInitialized;
        static string pluginInfoDirectory;

        /// <summary>VPB persistence root under Saves (not Custom/PluginData/sfishere).</summary>
        public static string PluginInfoDirectory
        {
            get
            {
                EnsurePluginDataInitialized();
                return pluginInfoDirectory;
            }
        }

        public static string AutoInstallPath => Path.Combine(PluginInfoDirectory, "AutoInstall.txt");

        /// <summary>Resolves paths, migrates known legacy files once, ensures VPB folder exists.</summary>
        public static void EnsurePluginDataInitialized()
        {
            if (pathsInitialized) return;
            lock (InitLock)
            {
                if (pathsInitialized) return;

                string baseDir = Directory.GetCurrentDirectory();
                pluginInfoDirectory = Path.Combine(
                    Path.Combine(Path.Combine(baseDir, "Saves"), "PluginData"),
                    "VPB");

                MigrateLegacyPluginFilesOnce(baseDir);

                if (!Directory.Exists(pluginInfoDirectory))
                    Directory.CreateDirectory(pluginInfoDirectory);

                pathsInitialized = true;
            }
        }

        static void MigrateLegacyPluginFilesOnce(string baseDir)
        {
            string legacyDir = Path.Combine(Path.Combine(Path.Combine(baseDir, "Custom"), "PluginData"), "sfishere");

            TryMigrateLegacyFile(legacyDir, "AutoInstall.txt");
            TryMigrateLegacyFile(legacyDir, "UserTags.json");
            TryMigrateLegacyFile(legacyDir, "PosePeopleCountIndex.json");
        }

        /// <summary>Copy a single legacy file if present; never enumerates the old sfishere folder.</summary>
        static void TryMigrateLegacyFile(string legacyDir, string fileName)
        {
            string dest = Path.Combine(pluginInfoDirectory, fileName);
            if (File.Exists(dest)) return;

            string src = Path.Combine(legacyDir, fileName);
            if (!File.Exists(src)) return;

            try
            {
                if (!Directory.Exists(pluginInfoDirectory))
                    Directory.CreateDirectory(pluginInfoDirectory);
                File.Copy(src, dest, false);
                LogUtil.Log("[VPB] Migrated " + fileName + " from legacy var_browser path to Saves/PluginData/VPB");
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB] Failed to migrate legacy " + fileName + ": " + ex.Message);
            }
        }
    }
}
