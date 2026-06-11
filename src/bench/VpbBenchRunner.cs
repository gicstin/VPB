using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    internal static class VpbBenchRunner
    {
        const string LogTag = "[VPB.Bench]";

        static bool s_Initialized;
        static bool s_ConfigLoaded;
        static VpbBenchConfig s_Config;
        static KeyUtil s_Hotkey;
        static bool s_Running;
        static bool s_AutoScheduled;
        static float s_AutoStartAt = -1f;
        static string s_CurrentRunId;
        static string s_CurrentRunDir;
        static VamHookPlugin s_Host;
        static SceneLoadBenchSnapshot? s_PendingSnapshot;
        static readonly List<string> s_ActiveTempUids = new List<string>();
        static int s_GlobalSceneIndex;
        static JSONClass s_RunDocument;

        internal static bool IsEnabled
        {
            get { return s_ConfigLoaded && s_Config != null && s_Config.Enabled; }
        }

        internal static bool IsRunning
        {
            get { return s_Running; }
        }

        internal static void InitializeOnce()
        {
            if (s_Initialized) return;
            s_Initialized = true;

            LogUtil.SceneLoadBenchCompleted += OnSceneLoadBenchCompleted;

            string path = VpbBenchPaths.ActiveConfigPath;
            if (!File.Exists(path))
            {
                LogUtil.Log(LogTag + " No config (optional): " + path);
                return;
            }

            string err;
            if (TryLoadConfig(out err))
            {
                LogUtil.LogWarning(LogTag + " Armed | mode=" + s_Config.Mode
                    + " | trigger=" + s_Config.Trigger
                    + " | hotkey=" + (s_Config.Hotkey ?? "")
                    + " | scenes=" + s_Config.Scenes.Count
                    + " | cfg=" + path);
            }
            else
            {
                LogUtil.LogWarning(LogTag + " Config present but not armed: " + err + " | cfg=" + path);
                EnsureHotkeyFromFile();
            }
        }

        internal static void Tick(VamHookPlugin host, bool fileManagerReady)
        {
            if (host == null) return;
            s_Host = host;
            if (s_Running) return;

            EnsureHotkeyFromFile();
            if (s_Hotkey != null && s_Hotkey.TestKeyDown())
            {
                if (VamHookPlugin.ShouldSuppressBenchHotkey(s_Hotkey))
                    return;
                if (!fileManagerReady)
                {
                    LogUtil.LogWarning(LogTag + " Hotkey pressed — waiting for FileManager");
                    return;
                }
                HandleHotkeyPressed(host);
                return;
            }

            if (!s_ConfigLoaded || s_Config == null || !s_Config.Enabled) return;

            if (s_Config.Trigger == VpbBenchTrigger.Auto)
            {
                if (!fileManagerReady) return;
                if (!s_AutoScheduled)
                {
                    s_AutoScheduled = true;
                    s_AutoStartAt = Time.realtimeSinceStartup + Mathf.Max(0f, s_Config.AutoDelaySeconds);
                    LogUtil.Log(LogTag + " Auto run scheduled in " + s_Config.AutoDelaySeconds.ToString("0") + "s");
                }
                if (s_AutoStartAt >= 0f && Time.realtimeSinceStartup >= s_AutoStartAt)
                {
                    s_AutoStartAt = -1f;
                    host.StartCoroutine(RunSuiteCo());
                }
            }
        }

        static void HandleHotkeyPressed(VamHookPlugin host)
        {
            string err;
            if (!TryLoadConfig(out err))
            {
                LogUtil.LogError(LogTag + " Hotkey — cannot run: " + err
                    + " | fix bench_run.cfg and press hotkey again (no restart needed)");
                return;
            }

            if (s_Config.Trigger != VpbBenchTrigger.Hotkey && s_Config.Trigger != VpbBenchTrigger.Manual)
            {
                LogUtil.LogWarning(LogTag + " Hotkey pressed but trigger=" + s_Config.Trigger + " (use trigger = hotkey)");
                return;
            }

            LogUtil.LogWarning(LogTag + " Hotkey — starting run");
            host.StartCoroutine(RunSuiteCo());
        }

        static bool TryLoadConfig(out string error)
        {
            error = null;
            string path = VpbBenchPaths.ActiveConfigPath;
            if (!File.Exists(path))
            {
                error = "bench_run.cfg not found at " + path;
                s_ConfigLoaded = false;
                s_Config = null;
                return false;
            }

            VpbBenchConfig cfg;
            if (!VpbBenchConfigParser.TryLoad(path, out cfg, out error))
            {
                s_ConfigLoaded = false;
                s_Config = null;
                ParseHotkeyFromValue(ReadHotkeyLineFromFile(path));
                return false;
            }

            s_Config = cfg;
            s_ConfigLoaded = true;
            VpbBenchPaths.SetOutputSubDir(s_Config.OutputDir);
            ApplyStartupTrackingIfNeeded();
            ParseHotkeyFromValue(s_Config.Hotkey);
            return true;
        }

        static void ApplyStartupTrackingIfNeeded()
        {
            if (s_Config == null || !s_Config.TrackStartup) return;
            try
            {
                if (Settings.Instance != null && Settings.Instance.LogStartupTiming != null)
                    Settings.Instance.LogStartupTiming.Value = true;
                if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null)
                    Settings.Instance.LogStartupDetails.Value = true;
                VamStartupProfiler.RefreshCache();
            }
            catch { }
        }

        static void EnsureHotkeyFromFile()
        {
            if (s_Hotkey != null) return;
            string path = VpbBenchPaths.ActiveConfigPath;
            if (!File.Exists(path)) return;
            ParseHotkeyFromValue(ReadHotkeyLineFromFile(path));
        }

        static string ReadHotkeyLineFromFile(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == null) continue;
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) continue;
                    if (!line.StartsWith("hotkey", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return null;
        }

        static void ParseHotkeyFromValue(string hotkeyPattern)
        {
            if (string.IsNullOrEmpty(hotkeyPattern)) return;
            try { s_Hotkey = KeyUtil.Parse(hotkeyPattern); }
            catch (Exception ex) { LogUtil.LogError(LogTag + " Invalid hotkey '" + hotkeyPattern + "': " + ex.Message); }
        }

        static void OnSceneLoadBenchCompleted(SceneLoadBenchSnapshot snap)
        {
            if (!s_Running) return;
            s_PendingSnapshot = snap;
        }

        static IEnumerator RunSuiteCo()
        {
            if (s_Running || s_Config == null) yield break;
            s_Running = true;
            s_PendingSnapshot = null;
            s_GlobalSceneIndex = 0;

            s_CurrentRunId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            s_CurrentRunDir = VpbBenchPaths.RunDir(s_CurrentRunId);
            VpbBenchPaths.EnsureDir(s_CurrentRunDir);

            LogUtil.LogWarning(LogTag + " Run started | id=" + s_CurrentRunId);

            try
            {
                s_RunDocument = BuildRunDocumentHeader();
                WriteConfigSnapshot();

                ApplySettingOverrides();
                if (s_Config.ClearTextureCache) ClearCacheDir(VamHookPlugin.GetCacheDir());
                if (s_Config.ClearAbCache) ClearCacheDir(VamHookPlugin.GetAssetBundleCacheDir());

                List<string> candidates = VpbBenchConfigParser.ResolveSelectedCandidates(s_Config);
                var candSummary = s_RunDocument["candidates"] as JSONArray;
                if (candSummary == null)
                {
                    candSummary = new JSONArray();
                    s_RunDocument["candidates"] = candSummary;
                }

                for (int ci = 0; ci < candidates.Count; ci++)
                {
                    string candId = candidates[ci];
                    VpbBenchCandidateConfig cand;
                    if (!s_Config.Candidates.TryGetValue(candId, out cand))
                        continue;

                    LogUtil.Log(LogTag + " Candidate " + candId + " (" + cand.Label + ")");
                    ApplyCandidatePackages(cand);

                    var candNode = new JSONClass();
                    candNode["id"] = candId;
                    candNode["label"] = cand.Label ?? candId;
                    var scenesArr = new JSONArray();
                    candNode["scenes"] = scenesArr;

                    var baselineLookup = VpbBenchComparer.BuildBaselineLookup(s_Config.BaselineId, candId);
                    if (baselineLookup.Count > 0)
                        LogUtil.LogWarning(LogTag + " Baseline '" + s_Config.BaselineId + "' loaded — per-scene delta enabled");

                    for (int si = 0; si < s_Config.Scenes.Count; si++)
                    {
                        string sceneSpec = s_Config.Scenes[si];
                        yield return RunSingleSceneCo(sceneSpec, scenesArr, baselineLookup);

                        if (s_Config.WaitBetweenScenesSeconds > 0f)
                        {
                            float wait = s_Config.WaitBetweenScenesSeconds;
                            float end = Time.realtimeSinceStartup + wait;
                            while (Time.realtimeSinceStartup < end)
                                yield return null;
                        }

                        if (s_Config.CaptureSceneScreenshots && scenesArr.Count > 0)
                        {
                            var lastNode = scenesArr[scenesArr.Count - 1] as JSONClass;
                            if (lastNode != null)
                                yield return CaptureSceneScreenshotCo(lastNode);
                        }
                    }

                    ClearCandidatePackages();
                    candSummary.Add(candNode);
                }

                VpbBenchRunSummary.AttachUserResults(s_RunDocument, s_Config.BaselineId, s_CurrentRunDir);
                WriteRunJson();

                if (VpbBenchComparer.BaselineExists(s_Config.BaselineId))
                    RunCompareAndMergeReport();
            }
            finally
            {
                ClearCandidatePackages();
                s_Running = false;
                s_PendingSnapshot = null;
                s_RunDocument = null;
                LogUtil.LogWarning(LogTag + " Run finished | id=" + s_CurrentRunId + " | dir=" + s_CurrentRunDir);
            }
        }

        static IEnumerator RunSingleSceneCo(string sceneSpec, JSONArray scenesArr,
            Dictionary<string, VpbBenchComparer.SceneMetrics> baselineLookup)
        {
            int sceneIndex = s_GlobalSceneIndex++;
            string sceneKey = VpbBenchPaths.SanitizeFileKey(sceneSpec);

            string resolved = VdsLauncher.BenchResolveScene(sceneSpec);
            if (string.IsNullOrEmpty(resolved))
            {
                LogUtil.LogError(LogTag + " Scene resolve failed: " + sceneSpec);
                AddSceneNode(scenesArr, BuildSceneNode(sceneSpec, null, null, false, "resolve_failed", sceneIndex, sceneKey), baselineLookup);
                yield break;
            }

            s_PendingSnapshot = null;
            LogUtil.Log(LogTag + " Loading scene: " + resolved);

            try
            {
                LogUtil.BeginSceneClick(resolved);
                if (!LogUtil.IsSceneLoadActive())
                    LogUtil.BeginSceneLoad(resolved);
            }
            catch { }

            bool loadOk = VdsLauncher.BenchLoadScene(resolved);
            if (!loadOk)
            {
                AddSceneNode(scenesArr, BuildSceneNode(sceneSpec, resolved, null, false, "load_invoke_failed", sceneIndex, sceneKey), baselineLookup);
                yield break;
            }

            float timeout = Time.realtimeSinceStartup + 600f;
            while (Time.realtimeSinceStartup < timeout)
            {
                if (s_PendingSnapshot.HasValue) break;
                if (!LogUtil.IsSceneLoading() && s_PendingSnapshot.HasValue) break;
                yield return null;
            }

            SceneLoadBenchSnapshot? snapOpt = null;
            SceneLoadBenchSnapshot snap;
            if (s_PendingSnapshot.HasValue)
            {
                snap = s_PendingSnapshot.Value;
                s_PendingSnapshot = null;
                snapOpt = snap;
            }
            else
            {
                snap = default(SceneLoadBenchSnapshot);
                snap.SceneName = resolved;
                snap.Success = false;
                snap.TotalMs = 0;
            }

            AddSceneNode(scenesArr,
                BuildSceneNode(sceneSpec, resolved, snapOpt, snap.Success, null, sceneIndex, sceneKey),
                baselineLookup);
        }

        static void AddSceneNode(JSONArray scenesArr, JSONClass node,
            Dictionary<string, VpbBenchComparer.SceneMetrics> baselineLookup)
        {
            if (node == null) return;
            VpbBenchComparer.EnrichSceneWithBaseline(node, baselineLookup);
            scenesArr.Add(node);

            string line = VpbBenchComparer.FormatSceneBaselineLogLine(node);
            if (!string.IsNullOrEmpty(line))
                LogUtil.LogWarning(LogTag + " " + line);
            else if (node["totalMs"] != null)
            {
                string key = node["sceneKey"] != null ? node["sceneKey"].Value : "?";
                LogUtil.LogWarning(LogTag + " Scene " + key + " | run=" + node["totalMs"].Value + "ms");
            }
        }

        static IEnumerator CaptureSceneScreenshotCo(JSONClass sceneNode)
        {
            if (sceneNode == null) yield break;
            if (sceneNode["success"] == null || !sceneNode["success"].AsBool) yield break;

            yield return new WaitForEndOfFrame();

            int sceneIndex = 0;
            try { sceneIndex = sceneNode["index"].AsInt; } catch { }
            string captureKey = sceneNode["resolvedPath"] != null ? sceneNode["resolvedPath"].Value : null;
            if (string.IsNullOrEmpty(captureKey) && sceneNode["sceneKey"] != null)
                captureKey = sceneNode["sceneKey"].Value;

            string rel = VpbBenchScreenshot.CaptureAfterFrame(s_CurrentRunDir, sceneIndex, captureKey);
            if (!string.IsNullOrEmpty(rel))
                LogUtil.Log(LogTag + " Screenshot saved (not in run.json): " + rel);
        }

        static JSONClass BuildSceneNode(string sceneSpec, string resolved, SceneLoadBenchSnapshot? snap,
            bool success, string error, int sceneIndex, string sceneKey)
        {
            var root = new JSONClass();
            root["index"] = sceneIndex.ToString();
            root["sceneKey"] = sceneKey ?? "";
            root["timestamp"] = DateTime.Now.ToString("o");
            root["sceneSpec"] = sceneSpec ?? "";
            root["resolvedPath"] = resolved ?? "";
            root["success"] = success ? "true" : "false";
            if (!string.IsNullOrEmpty(error)) root["error"] = error;

            if (snap.HasValue)
            {
                SceneLoadBenchSnapshot s = snap.Value;
                root["sceneName"] = s.SceneName ?? "";
                root["packageUid"] = s.PackageUid ?? "";
                root["totalMs"] = s.TotalMs.ToString("0.00");
                root["totalSeconds"] = s.TotalSeconds.ToString("0.000");
                root["serial"] = s.Serial.ToString();
                root["context"] = s.Context ?? "";

                var lifecycle = new JSONClass();
                lifecycle["preLoadInternalSec"] = s.PreLoadInternalSec.ToString("0.000");
                lifecycle["loadInternalWindowSec"] = s.LoadInternalWindowSec.ToString("0.000");
                lifecycle["postLoadInternalToNotLoadingSec"] = s.PostLoadInternalToNotLoadingSec.ToString("0.000");
                lifecycle["firstImageActivitySec"] = s.FirstImageActivitySec.ToString("0.000");
                lifecycle["worldUiSec"] = s.WorldUiSec.ToString("0.000");
                lifecycle["preNotLoadingSec"] = s.PreNotLoadingSec.ToString("0.000");
                lifecycle["tailSec"] = s.TailSec.ToString("0.000");
                root["lifecycle"] = lifecycle;

                var frames = new JSONClass();
                frames["samples"] = s.FrameSamples.ToString();
                frames["fpsAvg"] = s.FpsAvg.ToString("0.0");
                frames["frameMsAvg"] = s.FrameMsAvg.ToString("0.0");
                frames["frameMsP95"] = s.FrameMsP95.ToString("0.0");
                frames["frameMsMax"] = s.FrameMsMax.ToString("0.0");
                frames["st33"] = s.St33.ToString();
                frames["st50"] = s.St50.ToString();
                frames["st100"] = s.St100.ToString();
                root["frames"] = frames;

                var mem = new JSONClass();
                mem["allocEnd"] = s.MemAllocEnd.ToString();
                mem["allocDelta"] = s.MemAllocDelta.ToString();
                mem["reservedEnd"] = s.MemReservedEnd.ToString();
                mem["reservedDelta"] = s.MemReservedDelta.ToString();
                mem["managedEnd"] = s.MemManagedEnd.ToString();
                mem["managedDelta"] = s.MemManagedDelta.ToString();
                root["memory"] = mem;

                var settle = new JSONClass();
                settle["fileExistsMisses"] = s.SettleFileExistsMisses.ToString();
                settle["openStreamMisses"] = s.SettleOpenStreamMisses.ToString();
                settle["varEntryMisses"] = s.SettleVarEntryMisses.ToString();
                settle["onDemandRetries"] = s.SettleOnDemandRetries.ToString();
                settle["sampleCount"] = s.SettleSampleCount.ToString();
                settle["busySampleCount"] = s.SettleBusySampleCount.ToString();
                settle["queueMax"] = s.SettleQueueMax.ToString();
                root["settle"] = settle;

                if (s.Perf != null && s.Perf.Count > 0)
                {
                    var perfArr = new JSONArray();
                    foreach (var kv in s.Perf)
                    {
                        var pe = new JSONClass();
                        pe["key"] = kv.Key;
                        pe["totalMs"] = kv.Value.TotalMs.ToString("0.00");
                        pe["count"] = kv.Value.Count.ToString();
                        pe["totalBytes"] = kv.Value.TotalBytes.ToString();
                        perfArr.Add(pe);
                    }
                    root["perf"] = perfArr;
                }
            }

            return root;
        }

        static JSONClass BuildRunDocumentHeader()
        {
            var doc = new JSONClass();
            doc["runId"] = s_CurrentRunId;
            doc["generatedAt"] = DateTime.Now.ToString("o");
            doc["mode"] = s_Config.Mode.ToString();
            doc["trigger"] = s_Config.Trigger.ToString();
            doc["baselineId"] = s_Config.BaselineId ?? "";
            doc["configPath"] = s_Config.SourceConfigPath ?? "";
            doc["pluginDir"] = VpbBenchPaths.PluginDir;
            doc["outputRoot"] = VpbBenchPaths.BenchOutputRoot;
            doc["waitBetweenScenesSeconds"] = s_Config.WaitBetweenScenesSeconds.ToString("0.##");
            doc["captureSceneScreenshots"] = s_Config.CaptureSceneScreenshots ? "true" : "false";
            try
            {
                var asm = typeof(VpbBenchRunner).Assembly;
                doc["vpbAssemblyVersion"] = asm.GetName().Version.ToString();
            }
            catch { }

            if (s_Config.TrackStartup)
            {
                try
                {
                    JSONClass snap = VamStartupProfiler.ExportBenchSnapshot("bench_run");
                    doc["startup"] = snap;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError(LogTag + " startup snapshot: " + ex.Message);
                }
            }

            doc["candidates"] = new JSONArray();
            return doc;
        }

        static void WriteRunJson()
        {
            if (s_RunDocument == null || string.IsNullOrEmpty(s_CurrentRunDir)) return;
            try
            {
                File.WriteAllText(VpbBenchPaths.RunJsonPath(s_CurrentRunDir), s_RunDocument.ToString(""));
            }
            catch (Exception ex)
            {
                LogUtil.LogError(LogTag + " run.json write failed: " + ex.Message);
            }
        }

        static void WriteConfigSnapshot()
        {
            try
            {
                if (!string.IsNullOrEmpty(s_Config.SourceConfigPath) && File.Exists(s_Config.SourceConfigPath))
                {
                    string dest = Path.Combine(s_CurrentRunDir, "config.cfg");
                    File.Copy(s_Config.SourceConfigPath, dest, true);
                }
            }
            catch { }
        }

        internal static bool TryPromoteLastRunToBaseline(string baselineId, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(baselineId))
            {
                error = "Baseline name is empty";
                return false;
            }

            string runDir = VpbBenchRunSummary.LastRunDir;
            if (string.IsNullOrEmpty(runDir) || !Directory.Exists(runDir))
            {
                error = "No completed run to save as baseline — run a test first";
                return false;
            }

            return TryPromoteRunDirToBaseline(runDir, baselineId, out error);
        }

        static bool TryPromoteRunDirToBaseline(string runDir, string baselineId, out string error)
        {
            error = null;
            string destRoot = VpbBenchPaths.BaselineDir(baselineId);
            try
            {
                if (Directory.Exists(destRoot))
                {
                    string[] existing = Directory.GetFiles(destRoot);
                    for (int i = 0; i < existing.Length; i++)
                    {
                        try { File.Delete(existing[i]); } catch { }
                    }
                    string[] subdirs = Directory.GetDirectories(destRoot);
                    for (int i = 0; i < subdirs.Length; i++)
                    {
                        try { Directory.Delete(subdirs[i], true); } catch { }
                    }
                }
                VpbBenchPaths.EnsureDir(destRoot);
                CopyDirRecursive(runDir, destRoot);
                LogUtil.LogWarning(LogTag + " Baseline saved: " + destRoot);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LogUtil.LogError(LogTag + " Baseline promote failed: " + ex.Message);
                return false;
            }
        }

        static void RunCompareAndMergeReport()
        {
            string summary;
            if (!VpbBenchComparer.TryCompare(s_Config.BaselineId, s_CurrentRunDir, s_CurrentRunId, out summary))
            {
                LogUtil.LogError(LogTag + " Compare failed: " + summary);
                return;
            }

            LogUtil.LogWarning(LogTag + " " + summary);
            if (!string.IsNullOrEmpty(VpbBenchRunSummary.LastRunHeadline))
                LogUtil.LogWarning(LogTag + " " + VpbBenchRunSummary.LastRunHeadline);
        }

        static void ApplyCandidatePackages(VpbBenchCandidateConfig cand)
        {
            ClearCandidatePackages();
            if (cand == null || cand.PackageUids == null || cand.PackageUids.Count == 0) return;
            try
            {
                var added = ScanWhitelistManager.Instance.AddTemporaryUidOverrides(cand.PackageUids);
                s_ActiveTempUids.Clear();
                s_ActiveTempUids.AddRange(added);
            }
            catch { }
        }

        static void ClearCandidatePackages()
        {
            if (s_ActiveTempUids.Count == 0) return;
            try { ScanWhitelistManager.Instance.RemoveTemporaryUidOverrides(s_ActiveTempUids); } catch { }
            s_ActiveTempUids.Clear();
        }

        static void ApplySettingOverrides()
        {
            if (s_Config.SettingOverrides == null || s_Config.SettingOverrides.Count == 0) return;
            foreach (var kv in s_Config.SettingOverrides)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                VdsLauncher.BenchApplySetting(kv.Key, kv.Value);
            }
        }

        static void ClearCacheDir(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                var files = new List<string>();
                FileManager.SafeGetFiles(path, "*", files);
                for (int i = 0; i < files.Count; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
                var dirs = new List<string>();
                FileManager.SafeGetDirectories(path, "*", dirs);
                for (int i = dirs.Count - 1; i >= 0; i--)
                {
                    try { Directory.Delete(dirs[i], false); } catch { }
                }
                LogUtil.Log(LogTag + " Cache cleared: " + path);
            }
            catch (Exception ex)
            {
                LogUtil.LogError(LogTag + " Cache clear failed: " + ex.Message);
            }
        }

        static void CopyDirRecursive(string src, string dst)
        {
            VpbBenchPaths.EnsureDir(dst);
            string[] files = Directory.GetFiles(src);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                File.Copy(files[i], Path.Combine(dst, name), true);
            }
            string[] dirs = Directory.GetDirectories(src);
            for (int i = 0; i < dirs.Length; i++)
            {
                string name = Path.GetFileName(dirs[i]);
                CopyDirRecursive(dirs[i], Path.Combine(dst, name));
            }
        }

        internal static void ReloadFromDisk()
        {
            string err;
            TryLoadConfig(out err);
        }

        internal static void RequestRunNow(VamHookPlugin host)
        {
            if (host == null) return;
            if (s_Running)
            {
                LogUtil.LogWarning(LogTag + " Run already in progress");
                return;
            }
            string err;
            if (!TryLoadConfig(out err))
            {
                LogUtil.LogError(LogTag + " Run Now failed: " + err);
                return;
            }
            LogUtil.LogWarning(LogTag + " Run Now — starting");
            host.StartCoroutine(RunSuiteCo());
        }
    }
}
