using System;
using System.IO;
using SimpleJSON;

namespace VPB
{
    /// <summary>Flat, user-facing result summary embedded in run.json.</summary>
    internal static class VpbBenchRunSummary
    {
        internal static string LastRunId;
        internal static string LastRunDir;
        internal static string LastRunHeadline;

        internal static void AttachUserResults(JSONClass runDoc, string baselineId, string runDir)
        {
            if (runDoc == null) return;

            bool compared = VpbBenchComparer.BaselineExists(baselineId);
            int ok = 0, warn = 0, fail = 0, total = 0;
            var results = new JSONArray();

            JSONNode cands = runDoc["candidates"];
            if (cands != null)
            {
                for (int c = 0; c < cands.Count; c++)
                {
                    JSONNode scenes = cands[c]["scenes"];
                    if (scenes == null) continue;
                    for (int s = 0; s < scenes.Count; s++)
                    {
                        JSONClass scene = scenes[s] as JSONClass;
                        if (scene == null) continue;
                        scene.Remove("screenshot");
                        scene.Remove("baselineScreenshot");
                        total++;
                        FinishSceneUserFields(scene, compared);
                        string status = scene["compareStatus"] != null ? scene["compareStatus"].Value : "OK";
                        if (!scene["success"].AsBool) { fail++; status = "FAIL"; scene["compareStatus"] = "FAIL"; }
                        else if (status == "WARN") warn++;
                        else if (status == "FAIL") fail++;
                        else ok++;

                        results.Add(BuildFlatResultRow(scene));
                    }
                }
            }

            var summary = new JSONClass();
            summary["runDir"] = runDir ?? "";
            summary["runId"] = runDoc["runId"] != null ? runDoc["runId"].Value : "";
            summary["baselineId"] = baselineId ?? "";
            summary["comparedToBaseline"] = compared ? "true" : "false";
            summary["sceneCount"] = total.ToString();
            summary["ok"] = ok.ToString();
            summary["warn"] = warn.ToString();
            summary["fail"] = fail.ToString();
            summary["headline"] = BuildHeadline(total, ok, warn, fail, compared, baselineId);
            if (compared && summary["runId"] != null)
                summary["compareCsv"] = "bench/compare/" + summary["runId"].Value + ".csv";

            runDoc["resultsSummary"] = summary;
            runDoc["results"] = results;

            LastRunId = summary["runId"].Value;
            LastRunDir = runDir;
            LastRunHeadline = summary["headline"].Value;
        }

        internal static void FinishSceneUserFields(JSONClass scene, bool comparedToBaseline)
        {
            if (scene == null) return;
            string key = scene["sceneKey"] != null ? scene["sceneKey"].Value : "scene";
            double runMs = scene["totalMs"] != null ? scene["totalMs"].AsDouble : 0;
            double runSec = runMs / 1000.0;
            scene["runSeconds"] = runSec.ToString("0.00");

            string fpsPart = "";
            if (scene["frames"] != null && scene["frames"]["fpsAvg"] != null)
                fpsPart = " " + scene["frames"]["fpsAvg"].Value + "fps";

            if (comparedToBaseline && scene["baselineMs"] != null)
            {
                double baseMs = scene["baselineMs"].AsDouble;
                double delta = scene["deltaMs"] != null ? scene["deltaMs"].AsDouble : (runMs - baseMs);
                scene["baselineSeconds"] = (baseMs / 1000.0).ToString("0.00");
                scene["deltaSeconds"] = (delta / 1000.0).ToString("0.00");
                scene["deltaLabel"] = VpbBenchComparer.FormatSignedMs(delta);
                scene["summaryLine"] = key + ": " + runSec.ToString("0.0") + "s" + fpsPart + " ("
                    + VpbBenchComparer.FormatSignedMs(delta) + " vs baseline)";
            }
            else
            {
                scene["summaryLine"] = key + ": " + runSec.ToString("0.0") + "s" + fpsPart;
            }
        }

        static JSONClass BuildFlatResultRow(JSONClass scene)
        {
            var row = new JSONClass();
            row["sceneKey"] = scene["sceneKey"] != null ? scene["sceneKey"].Value : "";
            row["sceneSpec"] = scene["sceneSpec"] != null ? scene["sceneSpec"].Value : "";
            row["summaryLine"] = scene["summaryLine"] != null ? scene["summaryLine"].Value : "";
            row["runSeconds"] = scene["runSeconds"] != null ? scene["runSeconds"].Value : "0";
            if (scene["baselineSeconds"] != null) row["baselineSeconds"] = scene["baselineSeconds"].Value;
            if (scene["deltaLabel"] != null) row["deltaLabel"] = scene["deltaLabel"].Value;
            if (scene["deltaSeconds"] != null) row["deltaSeconds"] = scene["deltaSeconds"].Value;
            if (scene["compareStatus"] != null) row["status"] = scene["compareStatus"].Value;
            else row["status"] = scene["success"].AsBool ? "OK" : "FAIL";
            if (scene["frames"] != null && scene["frames"]["fpsAvg"] != null)
                row["runFpsAvg"] = scene["frames"]["fpsAvg"].Value;
            if (scene["baselineFpsAvg"] != null) row["baselineFpsAvg"] = scene["baselineFpsAvg"].Value;
            if (scene["deltaFpsAvg"] != null) row["deltaFpsAvg"] = scene["deltaFpsAvg"].Value;
            if (scene["runFrameMsP95"] != null) row["runFrameMsP95"] = scene["runFrameMsP95"].Value;
            if (scene["baselineFrameMsP95"] != null) row["baselineFrameMsP95"] = scene["baselineFrameMsP95"].Value;
            if (scene["deltaFrameMsP95"] != null) row["deltaFrameMsP95"] = scene["deltaFrameMsP95"].Value;
            return row;
        }

        internal static string BuildHeadline(int total, int ok, int warn, int fail, bool compared, string baselineId)
        {
            if (total == 0) return "No scenes measured";
            if (!compared)
                return total + " scene(s) measured — no baseline yet (Set as baseline after a run)";
            if (fail > 0)
                return fail + " failed, " + warn + " slower, " + ok + " OK vs '" + baselineId + "'";
            if (warn > 0)
                return ok + " OK, " + warn + " slower vs '" + baselineId + "'";
            return "All " + ok + " scene(s) OK vs '" + baselineId + "'";
        }

        internal static bool IsRegressionStatus(string status)
        {
            return string.Equals(status, "WARN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "FAIL", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryLoadLatestRun(out JSONClass runDoc, out string runDir)
        {
            runDoc = null;
            runDir = null;
            try
            {
                string runsRoot = Path.Combine(VpbBenchPaths.BenchOutputRoot, "runs");
                if (!Directory.Exists(runsRoot)) return false;
                string[] dirs = Directory.GetDirectories(runsRoot);
                if (dirs == null || dirs.Length == 0) return false;

                string bestDir = null;
                string bestId = null;
                for (int i = 0; i < dirs.Length; i++)
                {
                    string id = Path.GetFileName(dirs[i]);
                    if (string.IsNullOrEmpty(id)) continue;
                    if (bestId == null || string.Compare(id, bestId, StringComparison.Ordinal) > 0)
                    {
                        bestId = id;
                        bestDir = dirs[i];
                    }
                }
                if (bestDir == null) return false;
                string jsonPath = VpbBenchPaths.RunJsonPath(bestDir);
                if (!File.Exists(jsonPath)) return false;
                runDoc = JSON.Parse(File.ReadAllText(jsonPath)) as JSONClass;
                runDir = bestDir;
                return runDoc != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
