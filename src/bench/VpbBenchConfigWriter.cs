using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VPB
{
    internal static class VpbBenchConfigWriter
    {
        internal static bool TryWrite(VpbBenchConfig cfg, string path, out string error)
        {
            error = null;
            if (cfg == null)
            {
                error = "Null config";
                return false;
            }
            if (string.IsNullOrEmpty(path))
            {
                error = "Null path";
                return false;
            }

            try
            {
                var sb = new StringBuilder(2048);
                sb.AppendLine("# VPB benchmark / validation — written by gallery UI");
                sb.AppendLine("# Config path: " + path);
                sb.AppendLine();
                sb.AppendLine("enabled = " + (cfg.Enabled ? "true" : "false"));
                sb.AppendLine("mode = " + (cfg.Mode == VpbBenchMode.Compare ? "compare" : "baseline"));
                if (!string.IsNullOrEmpty(cfg.BaselineId))
                    sb.AppendLine("baselineId = " + cfg.BaselineId);
                sb.AppendLine("trigger = " + TriggerToString(cfg.Trigger));
                if (!string.IsNullOrEmpty(cfg.Hotkey))
                    sb.AppendLine("hotkey = " + cfg.Hotkey);
                sb.AppendLine("autoDelaySeconds = " + cfg.AutoDelaySeconds.ToString("0.##"));
                sb.AppendLine("trackStartup = " + (cfg.TrackStartup ? "true" : "false"));
                sb.AppendLine("trackSceneLoad = " + (cfg.TrackSceneLoad ? "true" : "false"));
                sb.AppendLine("maxCandidates = " + cfg.MaxCandidates);
                if (cfg.SelectedCandidates.Count > 0)
                    sb.AppendLine("selectedCandidates = " + string.Join(", ", cfg.SelectedCandidates.ToArray()));
                sb.AppendLine("preset = " + VpbBenchPresetUtil.ToConfigValue(cfg.Preset));
                sb.AppendLine("clearTextureCache = " + (cfg.ClearTextureCache ? "true" : "false"));
                sb.AppendLine("clearAbCache = " + (cfg.ClearAbCache ? "true" : "false"));
                sb.AppendLine("waitBetweenScenesSeconds = " + cfg.WaitBetweenScenesSeconds.ToString("0.##"));
                sb.AppendLine("captureSceneScreenshots = " + (cfg.CaptureSceneScreenshots ? "true" : "false"));
                sb.AppendLine("reloadSceneBetweenRuns = " + (cfg.ReloadSceneBetweenRuns ? "true" : "false"));
                if (!string.IsNullOrEmpty(cfg.OutputDir))
                    sb.AppendLine("outputDir = " + cfg.OutputDir);
                sb.AppendLine();

                foreach (var kv in cfg.Candidates)
                {
                    if (kv.Value == null || string.IsNullOrEmpty(kv.Key)) continue;
                    sb.AppendLine("[candidate." + kv.Key + "]");
                    if (!string.IsNullOrEmpty(kv.Value.Label))
                        sb.AppendLine("label = " + kv.Value.Label);
                    if (kv.Value.PackageUids != null && kv.Value.PackageUids.Count > 0)
                        sb.AppendLine("packageUids = " + string.Join(", ", kv.Value.PackageUids.ToArray()));
                    sb.AppendLine();
                }

                sb.AppendLine("[scenes]");
                for (int i = 0; i < cfg.Scenes.Count; i++)
                {
                    string scene = cfg.Scenes[i];
                    if (!string.IsNullOrEmpty(scene))
                        sb.AppendLine("scene = " + scene);
                }
                sb.AppendLine();

                if (cfg.SettingOverrides != null && cfg.SettingOverrides.Count > 0)
                {
                    sb.AppendLine("[settings]");
                    foreach (var kv in cfg.SettingOverrides)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        sb.AppendLine(kv.Key + " = " + (kv.Value ?? ""));
                    }
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString());
                cfg.SourceConfigPath = path;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static string TriggerToString(VpbBenchTrigger trigger)
        {
            switch (trigger)
            {
                case VpbBenchTrigger.Auto: return "auto";
                case VpbBenchTrigger.Manual: return "manual";
                default: return "hotkey";
            }
        }
    }
}
