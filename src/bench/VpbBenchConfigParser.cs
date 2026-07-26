using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
    internal static class VpbBenchConfigParser
    {
        internal static bool TryParseFile(string path, out VpbBenchConfig config, out string error)
        {
            config = null;
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "Config file not found: " + path;
                return false;
            }

            try
            {
                var cfg = new VpbBenchConfig();
                cfg.SourceConfigPath = path;
                bool sawPresetKey = false;
                string section = "";
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string raw = lines[i];
                    if (raw == null) continue;
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key.Length == 0) continue;

                    if (string.IsNullOrEmpty(section) || section.Equals("global", StringComparison.OrdinalIgnoreCase))
                    {
                        if (key.Equals("preset", StringComparison.OrdinalIgnoreCase))
                            sawPresetKey = true;
                        ApplyGlobal(cfg, key, val);
                    }
                    else if (section.Equals("scenes", StringComparison.OrdinalIgnoreCase))
                    {
                        if (key.Equals("scene", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(val))
                            cfg.Scenes.Add(val);
                    }
                    else if (section.StartsWith("candidate.", StringComparison.OrdinalIgnoreCase))
                    {
                        string id = section.Substring("candidate.".Length).Trim();
                        if (id.Length == 0) continue;
                        VpbBenchCandidateConfig cand;
                        if (!cfg.Candidates.TryGetValue(id, out cand))
                        {
                            cand = new VpbBenchCandidateConfig { Id = id, Label = id };
                            cfg.Candidates[id] = cand;
                        }
                        ApplyCandidate(cand, key, val);
                    }
                    else if (section.Equals("settings", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(key))
                            cfg.SettingOverrides[key] = val ?? "";
                    }
                }

                if (!sawPresetKey)
                    cfg.Preset = VpbBenchPresetUtil.Detect(cfg);
                else if (cfg.Preset != VpbBenchPresetKind.Custom)
                    VpbBenchPresetUtil.Apply(cfg, cfg.Preset);

                config = cfg;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryLoad(string path, out VpbBenchConfig config, out string error)
        {
            if (!TryParseFile(path, out config, out error)) return false;
            return Validate(config, out error);
        }

        internal static bool TryValidate(VpbBenchConfig cfg, out string error)
        {
            return Validate(cfg, out error);
        }

        internal static bool TryValidateForUiRun(VpbBenchConfig cfg, out string error)
        {
            error = null;
            if (cfg == null)
            {
                error = "Null config";
                return false;
            }
            if (cfg.Scenes.Count == 0)
            {
                error = "No scenes configured";
                return false;
            }
            if (cfg.Candidates.Count == 0)
            {
                error = "No candidate configured";
                return false;
            }
            return true;
        }

        internal static VpbBenchConfig CreateDefault()
        {
            var cfg = new VpbBenchConfig();
            cfg.Enabled = false;
            cfg.BaselineId = "main";
            VpbBenchPresetUtil.Apply(cfg, VpbBenchPresetKind.Standard);
            cfg.SelectedCandidates.Add("stock");
            var stock = new VpbBenchCandidateConfig { Id = "stock", Label = "Stock whitelist" };
            cfg.Candidates["stock"] = stock;
            return cfg;
        }

        static void ApplyGlobal(VpbBenchConfig cfg, string key, string val)
        {
            if (key.Equals("enabled", StringComparison.OrdinalIgnoreCase))
            {
                cfg.Enabled = ParseBool(val);
                return;
            }
            if (key.Equals("mode", StringComparison.OrdinalIgnoreCase))
            {
                if (val != null && val.Equals("compare", StringComparison.OrdinalIgnoreCase))
                    cfg.Mode = VpbBenchMode.Compare;
                else
                    cfg.Mode = VpbBenchMode.Baseline;
                return;
            }
            if (key.Equals("baselineId", StringComparison.OrdinalIgnoreCase))
            {
                cfg.BaselineId = val;
                return;
            }
            if (key.Equals("trigger", StringComparison.OrdinalIgnoreCase))
            {
                if (val != null && val.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    cfg.Trigger = VpbBenchTrigger.Auto;
                else if (val != null && val.Equals("manual", StringComparison.OrdinalIgnoreCase))
                    cfg.Trigger = VpbBenchTrigger.Manual;
                else
                    cfg.Trigger = VpbBenchTrigger.Hotkey;
                return;
            }
            if (key.Equals("hotkey", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(val)) cfg.Hotkey = val;
                return;
            }
            if (key.Equals("autoDelaySeconds", StringComparison.OrdinalIgnoreCase))
            {
                float f;
                if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out f) && f >= 0f)
                    cfg.AutoDelaySeconds = f;
                return;
            }
            if (key.Equals("trackStartup", StringComparison.OrdinalIgnoreCase))
            {
                cfg.TrackStartup = ParseBool(val, true);
                return;
            }
            if (key.Equals("trackSceneLoad", StringComparison.OrdinalIgnoreCase))
            {
                cfg.TrackSceneLoad = ParseBool(val, true);
                return;
            }
            if (key.Equals("maxCandidates", StringComparison.OrdinalIgnoreCase))
            {
                int n;
                if (int.TryParse(val, out n) && n > 0) cfg.MaxCandidates = n;
                return;
            }
            if (key.Equals("selectedCandidates", StringComparison.OrdinalIgnoreCase))
            {
                cfg.SelectedCandidates.Clear();
                SplitCsv(val, cfg.SelectedCandidates);
                return;
            }
            if (key.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                cfg.Preset = VpbBenchPresetUtil.Parse(val);
                return;
            }
            if (key.Equals("clearTextureCache", StringComparison.OrdinalIgnoreCase))
            {
                cfg.ClearTextureCache = ParseBool(val);
                return;
            }
            if (key.Equals("clearAbCache", StringComparison.OrdinalIgnoreCase))
            {
                cfg.ClearAbCache = ParseBool(val);
                return;
            }
            if (key.Equals("waitBetweenScenesSeconds", StringComparison.OrdinalIgnoreCase))
            {
                float f;
                if (float.TryParse(val, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out f) && f >= 0f)
                    cfg.WaitBetweenScenesSeconds = f;
                return;
            }
            if (key.Equals("captureSceneScreenshots", StringComparison.OrdinalIgnoreCase))
            {
                cfg.CaptureSceneScreenshots = ParseBool(val, true);
                return;
            }
            if (key.Equals("reloadSceneBetweenRuns", StringComparison.OrdinalIgnoreCase))
            {
                cfg.ReloadSceneBetweenRuns = ParseBool(val, true);
                return;
            }
            if (key.Equals("outputDir", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(val)) cfg.OutputDir = val;
                return;
            }
            if (key.Equals("scene", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(val)) cfg.Scenes.Add(val);
            }
        }

        static void ApplyCandidate(VpbBenchCandidateConfig cand, string key, string val)
        {
            if (key.Equals("label", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(val)) cand.Label = val;
                return;
            }
            if (key.Equals("packageUids", StringComparison.OrdinalIgnoreCase))
            {
                cand.PackageUids.Clear();
                SplitCsv(val, cand.PackageUids);
            }
        }

        static void SplitCsv(string val, List<string> dest)
        {
            if (dest == null || string.IsNullOrEmpty(val)) return;
            string[] parts = val.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i] != null ? parts[i].Trim() : "";
                if (p.Length > 0) dest.Add(p);
            }
        }

        static bool ParseBool(string val, bool defaultValue)
        {
            if (string.IsNullOrEmpty(val)) return defaultValue;
            return ParseBool(val);
        }

        static bool ParseBool(string val)
        {
            if (string.IsNullOrEmpty(val)) return false;
            string s = val.Trim();
            if (s.Equals("1") || s.Equals("true", StringComparison.OrdinalIgnoreCase)
                || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || s.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (s.Equals("0") || s.Equals("false", StringComparison.OrdinalIgnoreCase)
                || s.Equals("no", StringComparison.OrdinalIgnoreCase)
                || s.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
            bool b;
            return bool.TryParse(s, out b) && b;
        }

        static bool Validate(VpbBenchConfig cfg, out string error)
        {
            error = null;
            if (cfg == null)
            {
                error = "Null config";
                return false;
            }
            if (!cfg.Enabled)
            {
                error = "Config disabled";
                return false;
            }
            if (cfg.Scenes.Count == 0)
            {
                error = "No scenes configured";
                return false;
            }
            if (cfg.Candidates.Count == 0)
            {
                error = "No [candidate.*] sections configured";
                return false;
            }

            List<string> selected = ResolveSelectedCandidates(cfg);
            if (selected.Count == 0)
            {
                error = "No candidates selected";
                return false;
            }
            if (selected.Count > cfg.MaxCandidates)
            {
                error = "Selected candidates (" + selected.Count + ") exceed maxCandidates (" + cfg.MaxCandidates + ")";
                return false;
            }
            for (int i = 0; i < selected.Count; i++)
            {
                if (!cfg.Candidates.ContainsKey(selected[i]))
                {
                    error = "Unknown candidate: " + selected[i];
                    return false;
                }
            }
            return true;
        }

        internal static List<string> ResolveSelectedCandidates(VpbBenchConfig cfg)
        {
            var result = new List<string>();
            if (cfg == null) return result;
            if (cfg.SelectedCandidates.Count > 0)
            {
                for (int i = 0; i < cfg.SelectedCandidates.Count; i++)
                {
                    string id = cfg.SelectedCandidates[i];
                    if (!string.IsNullOrEmpty(id) && cfg.Candidates.ContainsKey(id) && !result.Contains(id))
                        result.Add(id);
                }
                return result;
            }
            foreach (var kv in cfg.Candidates)
            {
                if (!string.IsNullOrEmpty(kv.Key))
                    result.Add(kv.Key);
            }
            return result;
        }
    }
}
