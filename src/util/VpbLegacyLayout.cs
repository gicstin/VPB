using System;
using System.Diagnostics;
using System.IO;

namespace VPB.Shared
{
    internal static class VpbLegacyLayout
    {
        internal const string OldFileSuffix = ".vpb_old";
        internal const string StagingDirName = "vpb_update_staging";
        internal const string PendingFileName = "pending.json";
        internal const string PluginSubDirName = "VPB";

        /// <summary>MP-only dirs under <c>BepInEx/plugins/VPB</c>. Experiments does not ship them; leftover copies survive a branch switch if the stale MP manifest still lists them.</summary>
        internal static readonly string[] MpOnlyPluginSubDirs =
        {
            "net"
        };

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

        /// <summary>Stop leftover <c>VpbNet.exe</c> then delete MP-only plugin subdirs. Failures go through <paramref name="info"/> so they do not splash an orange warning every later experiments launch.</summary>
        internal static int SweepMpOnlyUnderVpbDir(string vpbDir, Action<string> info)
        {
            if (string.IsNullOrEmpty(vpbDir)) return 0;
            StopLeftoverMpBroker();

            int removed = 0;
            for (int i = 0; i < MpOnlyPluginSubDirs.Length; i++)
            {
                string path;
                try { path = Path.Combine(vpbDir, MpOnlyPluginSubDirs[i]); }
                catch { continue; }
                if (!SafeDirExists(path)) continue;
                if (RetireDirectory(path, info))
                {
                    if (info != null) info("Retired MP-only " + MpOnlyPluginSubDirs[i] + "/");
                    removed++;
                }
            }
            return removed;
        }

        internal static void StopLeftoverMpBroker()
        {
            bool killed = false;
            try
            {
                Process[] procs = Process.GetProcessesByName("VpbNet");
                if (procs == null) return;
                for (int i = 0; i < procs.Length; i++)
                {
                    try { procs[i].Kill(); killed = true; } catch { }
                    try { procs[i].Dispose(); } catch { }
                }
            }
            catch { }
            if (!killed) return;
            try { System.Threading.Thread.Sleep(200); } catch { }
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
