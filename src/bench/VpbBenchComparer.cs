using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SimpleJSON;

namespace VPB
{
    internal static class VpbBenchComparer
    {
        const double WarnMsAbs = 500.0;
        const double WarnMsPct = 0.05;

        internal sealed class SceneMetrics
        {
            public string CandidateId;
            public string SceneKey;
            public string SceneSpec;
            public double TotalMs;
            public bool Success;
            public double FpsAvg;
            public double FrameMsAvg;
            public double FrameMsP95;
            public double FrameMsMax;
            public double WorldUiSec;
            public double TailSec;
            public double FirstImageActivitySec;
            public double MemAllocDeltaMb;
            public double MemManagedDeltaMb;
            public int BusySampleCount;
            public int SettleQueueMax;
        }

        internal static bool BaselineExists(string baselineId)
        {
            if (string.IsNullOrEmpty(baselineId)) return false;
            string root = VpbBenchPaths.BaselineDir(baselineId);
            if (!Directory.Exists(root)) return false;
            if (File.Exists(VpbBenchPaths.RunJsonPath(root))) return true;
            return Directory.Exists(Path.Combine(root, "candidates"));
        }

        internal static Dictionary<string, SceneMetrics> BuildBaselineLookup(string baselineId, string candidateId)
        {
            var dict = new Dictionary<string, SceneMetrics>(StringComparer.OrdinalIgnoreCase);
            if (!BaselineExists(baselineId)) return dict;
            string root = VpbBenchPaths.BaselineDir(baselineId);
            var rows = LoadSceneMetrics(root);
            for (int i = 0; i < rows.Count; i++)
            {
                SceneMetrics r = rows[i];
                if (!string.IsNullOrEmpty(candidateId)
                    && !string.IsNullOrEmpty(r.CandidateId)
                    && !string.Equals(r.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase))
                    continue;
                string k = MakeMatchKey(r);
                if (!dict.ContainsKey(k))
                    dict[k] = r;
            }
            return dict;
        }

        internal static void EnrichSceneWithBaseline(JSONClass node, Dictionary<string, SceneMetrics> lookup)
        {
            if (node == null || lookup == null || lookup.Count == 0) return;
            string key = MakeMatchKeyFromNode(node);
            SceneMetrics baseRow;
            if (!lookup.TryGetValue(key, out baseRow)) return;

            double runMs = node["totalMs"] != null ? node["totalMs"].AsDouble : 0;
            double baseMs = baseRow.TotalMs;
            double delta = runMs - baseMs;
            double pct = baseMs > 0.0001 ? (delta / baseMs) : 0;
            bool runOk = node["success"] == null || node["success"].AsBool;

            string status = "OK";
            if (!runOk) status = "FAIL";
            else if (!baseRow.Success) status = "WARN";
            else if (Math.Abs(delta) >= WarnMsAbs || Math.Abs(pct) >= WarnMsPct) status = "WARN";

            node["baselineMs"] = baseMs.ToString("0.00");
            node["deltaMs"] = delta.ToString("0.00");
            node["deltaPct"] = pct.ToString("0.0000");
            node["compareStatus"] = status;

            WriteCompareMetricPair(node, "fpsAvg", ReadFpsAvg(node), baseRow.FpsAvg);
            WriteCompareMetricPair(node, "frameMsP95", ReadFrameMsP95(node), baseRow.FrameMsP95);
            WriteCompareMetricPair(node, "frameMsMax", ReadFrameMsMax(node), baseRow.FrameMsMax);
            WriteCompareMetricPair(node, "worldUiSec", ReadWorldUiSec(node), baseRow.WorldUiSec);
            WriteCompareMetricPair(node, "memAllocDeltaMb", ReadMemAllocDeltaMb(node), baseRow.MemAllocDeltaMb);
        }

        static void WriteCompareMetricPair(JSONClass node, string name, double runVal, double baseVal)
        {
            double delta = runVal - baseVal;
            node["baseline" + Capitalize(name)] = baseVal.ToString("0.##");
            node["run" + Capitalize(name)] = runVal.ToString("0.##");
            node["delta" + Capitalize(name)] = delta.ToString("0.##");
        }

        static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        internal static string FormatSceneBaselineLogLine(JSONClass node)
        {
            if (node == null) return null;
            if (node["baselineMs"] == null) return null;
            string key = MakeMatchKeyFromNode(node);
            double runMs = node["totalMs"] != null ? node["totalMs"].AsDouble : 0;
            double delta = node["deltaMs"] != null ? node["deltaMs"].AsDouble : 0;
            string deltaStr = FormatSignedMs(delta);
            string status = node["compareStatus"] != null ? node["compareStatus"].Value : "";
            string fps = "";
            if (node["frames"] != null && node["frames"]["fpsAvg"] != null)
                fps = " " + node["frames"]["fpsAvg"].Value + "fps";
            return "Scene " + key + " | run=" + runMs.ToString("0") + "ms" + fps + " delta baseline: " + deltaStr
                + (string.IsNullOrEmpty(status) ? "" : " [" + status + "]");
        }

        internal static string FormatSignedMs(double deltaMs)
        {
            if (deltaMs >= 0) return "+" + deltaMs.ToString("0") + "ms";
            return deltaMs.ToString("0") + "ms";
        }

        static string MakeMatchKeyFromNode(JSONNode node)
        {
            if (node == null) return "unnamed";
            if (node["sceneKey"] != null && !string.IsNullOrEmpty(node["sceneKey"].Value))
                return node["sceneKey"].Value;
            if (node["sceneSpec"] != null && !string.IsNullOrEmpty(node["sceneSpec"].Value))
                return node["sceneSpec"].Value;
            return "unnamed";
        }

        internal static bool TryCompare(string baselineId, string runDir, string runId, out string summary)
        {
            summary = null;
            if (string.IsNullOrEmpty(baselineId) || string.IsNullOrEmpty(runDir))
            {
                summary = "Missing baselineId or runDir";
                return false;
            }

            string baselineRoot = VpbBenchPaths.BaselineDir(baselineId);
            if (!Directory.Exists(baselineRoot))
            {
                summary = "Baseline not found: " + baselineRoot;
                return false;
            }

            VpbBenchPaths.EnsureDir(VpbBenchPaths.CompareDir(runId));

            var baseScenes = LoadSceneMetrics(baselineRoot);
            var runScenes = LoadSceneMetrics(runDir);
            if (baseScenes.Count == 0)
            {
                summary = "Baseline has no scene data: " + baselineRoot;
                return false;
            }
            if (runScenes.Count == 0)
            {
                summary = "Run has no scene data: " + runDir;
                return false;
            }

            var runByKey = new Dictionary<string, SceneMetrics>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < runScenes.Count; i++)
            {
                SceneMetrics r = runScenes[i];
                string k = MakeMatchKey(r);
                if (!runByKey.ContainsKey(k))
                    runByKey[k] = r;
            }

            int warnCount = 0;
            int failCount = 0;
            var csvRows = new List<string>();
            csvRows.Add(BuildCsvHeader());

            for (int i = 0; i < baseScenes.Count; i++)
            {
                SceneMetrics b = baseScenes[i];
                string key = MakeMatchKey(b);
                SceneMetrics r;
                if (!runByKey.TryGetValue(key, out r))
                {
                    failCount++;
                    csvRows.Add(CsvRowMissing(b, key));
                    continue;
                }

                string status = ClassifyStatus(b, r, out bool isWarn, out bool isFail);
                if (isFail) failCount++;
                else if (isWarn) warnCount++;

                csvRows.Add(BuildCsvRow(b, r, status));
            }

            string csvPath = VpbBenchPaths.CompareCsvPath(runId);
            try
            {
                File.WriteAllText(csvPath, string.Join(Environment.NewLine, csvRows.ToArray()), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                summary = "CSV write failed: " + ex.Message;
                return false;
            }

            summary = "Compare CSV: " + csvPath + " | fail=" + failCount + " warn=" + warnCount;
            return true;
        }

        static string ClassifyStatus(SceneMetrics b, SceneMetrics r, out bool isWarn, out bool isFail)
        {
            isWarn = false;
            isFail = false;
            if (!r.Success) { isFail = true; return "FAIL"; }
            if (!b.Success) { isWarn = true; return "WARN"; }
            double delta = r.TotalMs - b.TotalMs;
            double pct = b.TotalMs > 0.0001 ? (delta / b.TotalMs) : 0;
            if (Math.Abs(delta) >= WarnMsAbs || Math.Abs(pct) >= WarnMsPct)
            {
                isWarn = true;
                return "WARN";
            }
            return "OK";
        }

        static string BuildCsvHeader()
        {
            return string.Join(",", new string[]
            {
                "candidate_id", "scene_key", "status",
                "baseline_load_sec", "run_load_sec", "delta_load_sec", "delta_load_pct",
                "baseline_fps_avg", "run_fps_avg", "delta_fps_avg",
                "baseline_frame_ms_avg", "run_frame_ms_avg", "delta_frame_ms_avg",
                "baseline_frame_ms_p95", "run_frame_ms_p95", "delta_frame_ms_p95",
                "baseline_frame_ms_max", "run_frame_ms_max", "delta_frame_ms_max",
                "baseline_world_ui_sec", "run_world_ui_sec", "delta_world_ui_sec",
                "baseline_tail_sec", "run_tail_sec", "delta_tail_sec",
                "baseline_first_image_sec", "run_first_image_sec", "delta_first_image_sec",
                "baseline_mem_alloc_delta_mb", "run_mem_alloc_delta_mb", "delta_mem_alloc_delta_mb",
                "baseline_mem_managed_delta_mb", "run_mem_managed_delta_mb", "delta_mem_managed_delta_mb",
                "baseline_busy_samples", "run_busy_samples", "delta_busy_samples",
                "baseline_settle_queue_max", "run_settle_queue_max", "delta_settle_queue_max",
                "baseline_ok", "run_ok"
            });
        }

        static string BuildCsvRow(SceneMetrics b, SceneMetrics r, string status)
        {
            return string.Join(",", new string[]
            {
                Csv(b.CandidateId), Csv(b.SceneKey), Csv(status),
                F(b.TotalMs / 1000.0), F(r.TotalMs / 1000.0), F((r.TotalMs - b.TotalMs) / 1000.0), Pct(r.TotalMs, b.TotalMs),
                F(b.FpsAvg), F(r.FpsAvg), F(r.FpsAvg - b.FpsAvg),
                F(b.FrameMsAvg), F(r.FrameMsAvg), F(r.FrameMsAvg - b.FrameMsAvg),
                F(b.FrameMsP95), F(r.FrameMsP95), F(r.FrameMsP95 - b.FrameMsP95),
                F(b.FrameMsMax), F(r.FrameMsMax), F(r.FrameMsMax - b.FrameMsMax),
                F(b.WorldUiSec), F(r.WorldUiSec), F(r.WorldUiSec - b.WorldUiSec),
                F(b.TailSec), F(r.TailSec), F(r.TailSec - b.TailSec),
                F(b.FirstImageActivitySec), F(r.FirstImageActivitySec), F(r.FirstImageActivitySec - b.FirstImageActivitySec),
                F(b.MemAllocDeltaMb), F(r.MemAllocDeltaMb), F(r.MemAllocDeltaMb - b.MemAllocDeltaMb),
                F(b.MemManagedDeltaMb), F(r.MemManagedDeltaMb), F(r.MemManagedDeltaMb - b.MemManagedDeltaMb),
                I(b.BusySampleCount), I(r.BusySampleCount), I(r.BusySampleCount - b.BusySampleCount),
                I(b.SettleQueueMax), I(r.SettleQueueMax), I(r.SettleQueueMax - b.SettleQueueMax),
                B(b.Success), B(r.Success)
            });
        }

        static string CsvRowMissing(SceneMetrics b, string key)
        {
            return string.Join(",", new string[]
            {
                Csv(b.CandidateId), Csv(key), Csv("MISSING"),
                F(b.TotalMs / 1000.0), "", "", "",
                F(b.FpsAvg), "", "",
                F(b.FrameMsAvg), "", "",
                F(b.FrameMsP95), "", "",
                F(b.FrameMsMax), "", "",
                F(b.WorldUiSec), "", "",
                F(b.TailSec), "", "",
                F(b.FirstImageActivitySec), "", "",
                F(b.MemAllocDeltaMb), "", "",
                F(b.MemManagedDeltaMb), "", "",
                I(b.BusySampleCount), "", "",
                I(b.SettleQueueMax), "", "",
                B(b.Success), "false"
            });
        }

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static string F(double v) { return v.ToString("0.###"); }
        static string I(int v) { return v.ToString(); }
        static string B(bool v) { return v ? "true" : "false"; }

        static string Pct(double runMs, double baseMs)
        {
            if (baseMs <= 0.0001) return "";
            return ((runMs - baseMs) / baseMs).ToString("0.####");
        }

        static string MakeMatchKey(SceneMetrics row)
        {
            if (!string.IsNullOrEmpty(row.SceneKey)) return row.SceneKey;
            if (!string.IsNullOrEmpty(row.SceneSpec)) return row.SceneSpec;
            return "unnamed";
        }

        static List<SceneMetrics> LoadSceneMetrics(string runRoot)
        {
            string runJson = VpbBenchPaths.RunJsonPath(runRoot);
            if (File.Exists(runJson))
                return LoadSceneMetricsFromRunJson(runJson);
            return LoadSceneMetricsLegacy(runRoot);
        }

        static List<SceneMetrics> LoadSceneMetricsFromRunJson(string runJsonPath)
        {
            var rows = new List<SceneMetrics>();
            JSONNode root;
            try { root = JSON.Parse(File.ReadAllText(runJsonPath)); } catch { return rows; }
            if (root == null) return rows;

            JSONNode cands = root["candidates"];
            if (cands == null) return rows;
            for (int c = 0; c < cands.Count; c++)
            {
                JSONNode cand = cands[c];
                string candId = cand["id"] != null ? cand["id"].Value : "";
                JSONNode scenes = cand["scenes"];
                if (scenes == null) continue;
                for (int s = 0; s < scenes.Count; s++)
                    rows.Add(ParseSceneMetrics(scenes[s], candId));
            }
            return rows;
        }

        static SceneMetrics ParseSceneMetrics(JSONNode node, string candId)
        {
            var row = new SceneMetrics();
            row.CandidateId = candId;
            if (node == null) return row;
            row.SceneKey = node["sceneKey"] != null ? node["sceneKey"].Value : null;
            row.SceneSpec = node["sceneSpec"] != null ? node["sceneSpec"].Value : null;
            row.TotalMs = node["totalMs"] != null ? node["totalMs"].AsDouble : 0;
            row.Success = node["success"] == null || node["success"].AsBool;
            row.FpsAvg = ReadFpsAvg(node);
            row.FrameMsAvg = ReadFrameMsAvg(node);
            row.FrameMsP95 = ReadFrameMsP95(node);
            row.FrameMsMax = ReadFrameMsMax(node);
            row.WorldUiSec = ReadWorldUiSec(node);
            row.TailSec = ReadLifecycleDouble(node, "tailSec");
            row.FirstImageActivitySec = ReadLifecycleDouble(node, "firstImageActivitySec");
            row.MemAllocDeltaMb = ReadMemAllocDeltaMb(node);
            row.MemManagedDeltaMb = ReadMemManagedDeltaMb(node);
            row.BusySampleCount = ReadSettleInt(node, "busySampleCount");
            row.SettleQueueMax = ReadSettleInt(node, "queueMax");
            if (string.IsNullOrEmpty(row.SceneKey))
            {
                string resolved = node["resolvedPath"] != null ? node["resolvedPath"].Value : null;
                row.SceneKey = VpbBenchPaths.SanitizeFileKey(
                    string.IsNullOrEmpty(resolved) ? row.SceneSpec : resolved);
            }
            return row;
        }

        static double ReadFpsAvg(JSONNode node)
        {
            if (node["frames"] != null && node["frames"]["fpsAvg"] != null)
                return node["frames"]["fpsAvg"].AsDouble;
            return 0;
        }

        static double ReadFrameMsAvg(JSONNode node)
        {
            if (node["frames"] != null && node["frames"]["frameMsAvg"] != null)
                return node["frames"]["frameMsAvg"].AsDouble;
            return 0;
        }

        static double ReadFrameMsP95(JSONNode node)
        {
            if (node["frames"] != null && node["frames"]["frameMsP95"] != null)
                return node["frames"]["frameMsP95"].AsDouble;
            return 0;
        }

        static double ReadFrameMsMax(JSONNode node)
        {
            if (node["frames"] != null && node["frames"]["frameMsMax"] != null)
                return node["frames"]["frameMsMax"].AsDouble;
            return 0;
        }

        static double ReadWorldUiSec(JSONNode node) { return ReadLifecycleDouble(node, "worldUiSec"); }

        static double ReadLifecycleDouble(JSONNode node, string key)
        {
            if (node["lifecycle"] != null && node["lifecycle"][key] != null)
                return node["lifecycle"][key].AsDouble;
            return 0;
        }

        static double ReadMemAllocDeltaMb(JSONNode node)
        {
            return ReadMemDeltaMb(node, "allocDelta");
        }

        static double ReadMemManagedDeltaMb(JSONNode node)
        {
            return ReadMemDeltaMb(node, "managedDelta");
        }

        static double ReadMemDeltaMb(JSONNode node, string key)
        {
            if (node["memory"] == null || node["memory"][key] == null) return 0;
            JSONNode memNode = node["memory"][key];
            long bytes;
            if (long.TryParse(memNode.Value, out bytes))
                return bytes / (1024.0 * 1024.0);
            return memNode.AsDouble / (1024.0 * 1024.0);
        }

        static int ReadSettleInt(JSONNode node, string key)
        {
            if (node["settle"] != null && node["settle"][key] != null)
                return node["settle"][key].AsInt;
            return 0;
        }

        static List<SceneMetrics> LoadSceneMetricsLegacy(string runRoot)
        {
            var rows = new List<SceneMetrics>();
            string candidatesDir = Path.Combine(runRoot, "candidates");
            if (!Directory.Exists(candidatesDir)) return rows;

            string[] candDirs = Directory.GetDirectories(candidatesDir);
            for (int c = 0; c < candDirs.Length; c++)
            {
                string candDir = candDirs[c];
                string candId = Path.GetFileName(candDir);
                string scenesDir = Path.Combine(candDir, "scenes");
                if (!Directory.Exists(scenesDir)) continue;

                string[] files = Directory.GetFiles(scenesDir, "*.json");
                for (int s = 0; s < files.Length; s++)
                {
                    JSONNode node = null;
                    try { node = JSON.Parse(File.ReadAllText(files[s])); } catch { }
                    if (node == null) continue;
                    SceneMetrics row = ParseSceneMetrics(node, candId);
                    if (string.IsNullOrEmpty(row.SceneKey))
                        row.SceneKey = Path.GetFileNameWithoutExtension(files[s]);
                    rows.Add(row);
                }
            }
            return rows;
        }
    }
}
