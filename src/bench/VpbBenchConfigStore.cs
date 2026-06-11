using System;
using System.IO;

namespace VPB
{
    internal static class VpbBenchConfigStore
    {
        static VpbBenchConfig s_Working;
        static bool s_Dirty;

        internal static VpbBenchConfig Working
        {
            get
            {
                EnsureLoaded();
                return s_Working;
            }
        }

        internal static bool IsDirty
        {
            get { return s_Dirty; }
        }

        internal static void EnsureLoaded()
        {
            if (s_Working != null) return;
            ReloadFromDisk();
        }

        internal static void ReloadFromDisk()
        {
            string path = VpbBenchPaths.ActiveConfigPath;
            VpbBenchConfig cfg;
            string err;
            if (VpbBenchConfigParser.TryParseFile(path, out cfg, out err))
            {
                s_Working = cfg;
                s_Dirty = false;
                return;
            }

            if (File.Exists(path))
            {
                s_Working = VpbBenchConfigParser.CreateDefault();
                s_Working.SourceConfigPath = path;
                s_Dirty = true;
                return;
            }

            s_Working = VpbBenchConfigParser.CreateDefault();
            s_Working.SourceConfigPath = path;
            s_Dirty = false;
        }

        internal static void MarkDirty()
        {
            s_Dirty = true;
            TryAutoSave();
        }

        static void TryAutoSave()
        {
            if (s_Working == null) return;
            try
            {
                VpbBenchUiNormalize.PrepareForSave(s_Working);
                string err;
                if (!TrySave(out err) && !string.IsNullOrEmpty(err))
                    LogUtil.LogWarning("[VPB.Bench] Auto-save failed: " + err);
            }
            catch (System.Exception ex)
            {
                try { LogUtil.LogWarning("[VPB.Bench] Auto-save failed: " + ex.Message); } catch { }
            }
        }

        internal static bool TrySave(out string error)
        {
            error = null;
            if (s_Working == null)
            {
                error = "No config loaded";
                return false;
            }

            string path = VpbBenchPaths.ActiveConfigPath;
            if (!VpbBenchConfigWriter.TryWrite(s_Working, path, out error))
                return false;

            s_Dirty = false;
            VpbBenchRunner.ReloadFromDisk();
            return true;
        }

        internal static bool TryValidateWorking(out string error)
        {
            error = null;
            if (s_Working == null)
            {
                error = "No config loaded";
                return false;
            }
            if (!s_Working.Enabled)
            {
                error = "Bench runner is disabled";
                return false;
            }
            return VpbBenchConfigParser.TryValidate(s_Working, out error);
        }

        internal static bool TryValidateForUiRun(out string error)
        {
            error = null;
            if (s_Working == null)
            {
                error = "No config loaded";
                return false;
            }
            return VpbBenchConfigParser.TryValidateForUiRun(s_Working, out error);
        }
    }
}
