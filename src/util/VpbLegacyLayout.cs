using System;
using System.IO;

namespace VPB.Shared
{
    internal static class VpbLegacyLayout
    {
        internal const string OldFileSuffix = ".vpb_old";
        internal const string StagingDirName = "vpb_update_staging";
        internal const string PendingFileName = "pending.json";
        internal const string PluginSubDirName = "VPB";

        internal static readonly string[] RootFiles =
        {
            "VPB.dll",
            "VPB.pdb",
            "sqlite3.dll",
            "turbojpeg.dll",
            "vpb_icons.pack",
            "VPB_THIRD_PARTY_NOTICES.txt",
            "bench_run.cfg",
            "bench_run.example.cfg"
        };

        internal static readonly string[] RootDirs =
        {
            "vpb_fonts",
            "vpb_help",
            "vpb_translations",
            "vpb_themes",
            "vpb_ccm_clips",
            "vpb_icons",
            "VpbNet",
            "zstd",
            "bench",
            StagingDirName
        };

        internal static int SweepPluginsRoot(string pluginsDir, Action<string> info, Action<string> warn)
        {
            if (string.IsNullOrEmpty(pluginsDir)) return 0;

            int removed = 0;

            for (int i = 0; i < RootFiles.Length; i++)
            {
                string path;
                try { path = Path.Combine(pluginsDir, RootFiles[i]); }
                catch { continue; }
                if (!SafeFileExists(path)) continue;
                if (RetireFile(path, warn))
                {
                    if (info != null) info("Retired legacy " + RootFiles[i]);
                    removed++;
                }
            }

            for (int i = 0; i < RootDirs.Length; i++)
            {
                string path;
                try { path = Path.Combine(pluginsDir, RootDirs[i]); }
                catch { continue; }
                if (!SafeDirExists(path)) continue;
                if (RootDirs[i] == StagingDirName && HasPendingUpdate(path)) continue;
                if (RetireDirectory(path, warn))
                {
                    if (info != null) info("Retired legacy " + RootDirs[i] + "/");
                    removed++;
                }
            }

            return removed;
        }

        internal static bool RetireFile(string path, Action<string> warn)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch { }

            try
            {
                var retired = path + OldFileSuffix;
                try { if (File.Exists(retired)) File.Delete(retired); } catch { }
                File.Move(path, retired);
                return true;
            }
            catch (Exception ex)
            {
                if (warn != null) warn("Could not remove legacy " + path + ": " + ex.Message);
                return false;
            }
        }

        internal static bool RetireDirectory(string path, Action<string> warn)
        {
            try
            {
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                if (warn != null) warn("Could not remove legacy " + path + ": " + ex.Message);
                return false;
            }
        }

        private static bool HasPendingUpdate(string stagingDir)
        {
            try { return File.Exists(Path.Combine(stagingDir, PendingFileName)); }
            catch { return false; }
        }

        private static bool SafeFileExists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        private static bool SafeDirExists(string path)
        {
            try { return Directory.Exists(path); }
            catch { return false; }
        }
    }
}
