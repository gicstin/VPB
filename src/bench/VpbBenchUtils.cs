using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VPB
{
    internal enum VpbBenchPresetKind
    {
        Quick,
        Standard,
        Deep,
        Custom
    }

    internal static class VpbBenchPresetUtil
    {
        internal static void Apply(VpbBenchConfig cfg, VpbBenchPresetKind kind)
        {
            if (cfg == null) return;
            cfg.Preset = kind;
            switch (kind)
            {
                case VpbBenchPresetKind.Quick:
                    cfg.WaitBetweenScenesSeconds = 2f;
                    cfg.CaptureSceneScreenshots = false;
                    cfg.ClearTextureCache = false;
                    cfg.ClearAbCache = false;
                    break;
                case VpbBenchPresetKind.Standard:
                    cfg.WaitBetweenScenesSeconds = 5f;
                    cfg.CaptureSceneScreenshots = true;
                    cfg.ClearTextureCache = false;
                    cfg.ClearAbCache = false;
                    break;
                case VpbBenchPresetKind.Deep:
                    cfg.WaitBetweenScenesSeconds = 10f;
                    cfg.CaptureSceneScreenshots = true;
                    cfg.ClearTextureCache = true;
                    cfg.ClearAbCache = true;
                    break;
            }
        }

        internal static VpbBenchPresetKind Detect(VpbBenchConfig cfg)
        {
            if (cfg == null) return VpbBenchPresetKind.Standard;
            if (Matches(cfg, VpbBenchPresetKind.Quick)) return VpbBenchPresetKind.Quick;
            if (Matches(cfg, VpbBenchPresetKind.Standard)) return VpbBenchPresetKind.Standard;
            if (Matches(cfg, VpbBenchPresetKind.Deep)) return VpbBenchPresetKind.Deep;
            return VpbBenchPresetKind.Custom;
        }

        static bool Matches(VpbBenchConfig cfg, VpbBenchPresetKind kind)
        {
            VpbBenchConfig probe = new VpbBenchConfig();
            Apply(probe, kind);
            return Nearly(cfg.WaitBetweenScenesSeconds, probe.WaitBetweenScenesSeconds)
                && cfg.CaptureSceneScreenshots == probe.CaptureSceneScreenshots
                && cfg.ClearTextureCache == probe.ClearTextureCache
                && cfg.ClearAbCache == probe.ClearAbCache;
        }

        static bool Nearly(float a, float b)
        {
            return Math.Abs(a - b) < 0.05f;
        }

        internal static string ToConfigValue(VpbBenchPresetKind kind)
        {
            switch (kind)
            {
                case VpbBenchPresetKind.Quick: return "quick";
                case VpbBenchPresetKind.Deep: return "deep";
                case VpbBenchPresetKind.Custom: return "custom";
                default: return "standard";
            }
        }

        internal static VpbBenchPresetKind Parse(string val)
        {
            if (string.IsNullOrEmpty(val)) return VpbBenchPresetKind.Standard;
            string s = val.Trim();
            if (s.Equals("quick", StringComparison.OrdinalIgnoreCase)) return VpbBenchPresetKind.Quick;
            if (s.Equals("deep", StringComparison.OrdinalIgnoreCase)) return VpbBenchPresetKind.Deep;
            if (s.Equals("custom", StringComparison.OrdinalIgnoreCase)) return VpbBenchPresetKind.Custom;
            return VpbBenchPresetKind.Standard;
        }

        internal static string Label(VpbBenchPresetKind kind)
        {
            switch (kind)
            {
                case VpbBenchPresetKind.Quick: return "Quick";
                case VpbBenchPresetKind.Deep: return "Deep";
                case VpbBenchPresetKind.Custom: return "Custom";
                default: return "Standard";
            }
        }
    }

    internal static class VpbBenchUiNormalize
    {
        internal const string DefaultCandidateId = "default";

        internal static void PrepareForSave(VpbBenchConfig cfg)
        {
            if (cfg == null) return;
            VpbBenchCandidateConfig cand = GetDefaultCandidate(cfg);
            cfg.SelectedCandidates.Clear();
            cfg.SelectedCandidates.Add(DefaultCandidateId);
            if (!cfg.Candidates.ContainsKey(DefaultCandidateId))
            {
                cfg.Candidates.Clear();
                cfg.Candidates[DefaultCandidateId] = cand;
            }
            else
            {
                var keep = cfg.Candidates[DefaultCandidateId];
                cfg.Candidates.Clear();
                cfg.Candidates[DefaultCandidateId] = keep;
            }
            if (string.IsNullOrEmpty(cfg.BaselineId)) cfg.BaselineId = "main";
            cfg.Mode = VpbBenchMode.Compare;
            if (cfg.Preset == VpbBenchPresetKind.Custom)
                cfg.Preset = VpbBenchPresetUtil.Detect(cfg);
            if (string.IsNullOrEmpty(cfg.Hotkey)) cfg.Hotkey = "Ctrl+F10";
            cfg.Trigger = VpbBenchTrigger.Hotkey;
            cfg.MaxCandidates = 1;
        }

        internal static VpbBenchCandidateConfig GetDefaultCandidate(VpbBenchConfig cfg)
        {
            VpbBenchCandidateConfig cand;
            if (cfg.Candidates.TryGetValue(DefaultCandidateId, out cand) && cand != null)
                return cand;

            cand = new VpbBenchCandidateConfig { Id = DefaultCandidateId, Label = "Default" };
            for (int i = 0; i < cfg.SelectedCandidates.Count; i++)
            {
                VpbBenchCandidateConfig src;
                if (cfg.Candidates.TryGetValue(cfg.SelectedCandidates[i], out src) && src != null)
                {
                    cand.Label = src.Label ?? cand.Label;
                    for (int p = 0; p < src.PackageUids.Count; p++)
                    {
                        string uid = src.PackageUids[p];
                        if (!string.IsNullOrEmpty(uid) && !cand.PackageUids.Contains(uid))
                            cand.PackageUids.Add(uid);
                    }
                }
            }
            if (cand.PackageUids.Count == 0)
            {
                foreach (var kv in cfg.Candidates)
                {
                    if (kv.Value == null) continue;
                    for (int p = 0; p < kv.Value.PackageUids.Count; p++)
                    {
                        string uid = kv.Value.PackageUids[p];
                        if (!string.IsNullOrEmpty(uid) && !cand.PackageUids.Contains(uid))
                            cand.PackageUids.Add(uid);
                    }
                }
            }
            cfg.Candidates[DefaultCandidateId] = cand;
            return cand;
        }
    }

    internal static class VpbBenchPaths
    {
        internal const string ActiveConfigFileName = "bench_run.cfg";
        internal const string ExampleConfigFileName = "bench_run.example.cfg";
        internal const string DefaultOutputSubDir = "bench";

        static string s_PluginDir;
        static string s_OutputSubDir = DefaultOutputSubDir;

        internal static string PluginDir
        {
            get
            {
                if (!string.IsNullOrEmpty(s_PluginDir)) return s_PluginDir;
                try
                {
                    string loc = typeof(VpbBenchPaths).Assembly.Location;
                    if (!string.IsNullOrEmpty(loc))
                        s_PluginDir = Path.GetDirectoryName(loc);
                }
                catch { }
                if (string.IsNullOrEmpty(s_PluginDir))
                    s_PluginDir = ".";
                return s_PluginDir;
            }
        }

        internal static string ActiveConfigPath
        {
            get { return Path.Combine(PluginDir, ActiveConfigFileName); }
        }

        internal static string ExampleConfigPath
        {
            get { return Path.Combine(PluginDir, ExampleConfigFileName); }
        }

        internal static void SetOutputSubDir(string subDir)
        {
            if (string.IsNullOrEmpty(subDir))
            {
                s_OutputSubDir = DefaultOutputSubDir;
                return;
            }
            string trimmed = subDir.Trim().TrimStart('/', '\\');
            s_OutputSubDir = string.IsNullOrEmpty(trimmed) ? DefaultOutputSubDir : trimmed;
        }

        internal static string BenchOutputRoot
        {
            get { return Path.Combine(PluginDir, s_OutputSubDir); }
        }

        internal static string RunDir(string runId)
        {
            return Path.Combine(Path.Combine(BenchOutputRoot, "runs"), runId ?? "unknown");
        }

        internal static string BaselineDir(string baselineId)
        {
            return Path.Combine(Path.Combine(BenchOutputRoot, "baselines"), baselineId ?? "unknown");
        }

        internal static string CompareDir(string runId)
        {
            return Path.Combine(BenchOutputRoot, "compare");
        }

        internal static string CompareCsvPath(string runId)
        {
            return Path.Combine(CompareDir(runId), (runId ?? "unknown") + ".csv");
        }

        internal static string RunJsonPath(string runDir)
        {
            return Path.Combine(runDir ?? "", "run.json");
        }

        internal static void EnsureDir(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch { }
        }

        internal static List<string> ListBaselineIds()
        {
            var ids = new List<string>();
            string root = Path.Combine(BenchOutputRoot, "baselines");
            if (!Directory.Exists(root)) return ids;
            try
            {
                string[] dirs = Directory.GetDirectories(root);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string id = Path.GetFileName(dirs[i]);
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
                }
                ids.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return ids;
        }

        internal static void TryOpenInShell(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string normalized = path.Replace('/', '\\');
                if (Directory.Exists(normalized))
                {
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + normalized + "\"");
                    return;
                }
                if (File.Exists(normalized))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + normalized + "\"");
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VPB.Bench] Open path failed: " + ex.Message);
            }
        }

        internal static string SanitizeFileKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unnamed";
            char[] bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ':' || c == '/' || c == '\\') c = '_';
                bool invalid = false;
                for (int j = 0; j < bad.Length; j++)
                {
                    if (bad[j] == c) { invalid = true; break; }
                }
                sb.Append(invalid ? '_' : c);
            }
            string s = sb.ToString().Trim();
            if (s.Length > 120) s = s.Substring(0, 120);
            return string.IsNullOrEmpty(s) ? "unnamed" : s;
        }
    }

    internal static class VpbBenchScreenshot
    {
        internal static string CaptureAfterFrame(string runDir, int sceneIndex, string sceneKey)
        {
            if (string.IsNullOrEmpty(runDir)) return null;
            string shotsDir = Path.Combine(runDir, "screenshots");
            VpbBenchPaths.EnsureDir(shotsDir);

            string key = VpbBenchPaths.SanitizeFileKey(sceneKey);
            string fileName = string.Format("{0:D3}_{1}.png", sceneIndex + 1, key);
            string fullPath = Path.Combine(shotsDir, fileName);

            Camera cam = Camera.main;
            if (cam == null)
            {
                try
                {
                    if (SuperController.singleton != null && SuperController.singleton.centerCameraTarget != null)
                        cam = SuperController.singleton.centerCameraTarget.GetComponentInChildren<Camera>(true);
                }
                catch { }
            }
            if (cam == null) return null;

            int w = Screen.width;
            int h = Screen.height;
            if (w <= 0 || h <= 0) return null;

            RenderTexture prevTarget = null;
            RenderTexture rt = null;
            try
            {
                prevTarget = cam.targetTexture;
                rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                tex.Apply(false, false);
                RenderTexture.active = prevActive;
                cam.targetTexture = prevTarget;
                prevTarget = null;

                byte[] bytes = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                File.WriteAllBytes(fullPath, bytes);
                return "screenshots/" + fileName;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogError("[VPB.Bench] Screenshot failed: " + ex.Message); } catch { }
                return null;
            }
            finally
            {
                try
                {
                    if (cam != null && prevTarget != null)
                        cam.targetTexture = prevTarget;
                }
                catch { }
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
