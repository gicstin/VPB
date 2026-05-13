using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using Mono.Cecil;

namespace VPB.Patcher
{
    public static class VPBPatcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        private static ManualLogSource Log;

        private const string StagingDirName = "vpb_update_staging";
        private const string PendingFileName = "pending.json";
        private const string OldFileSuffix = ".vpb_old";

        public static void Patch(AssemblyDefinition assembly) { }

        public static void Initialize()
        {
            Log = Logger.CreateLogSource("VPB.Patcher");

            try
            {
                var gameRoot = ResolveGameRoot();
                if (gameRoot == null)
                {
                    Log.LogWarning("Could not resolve game root directory");
                    return;
                }

                CleanupOldFiles(gameRoot);

                var stagingDir = Path.Combine(Path.Combine(Path.Combine(gameRoot, "BepInEx"), "plugins"), StagingDirName);
                var pendingPath = Path.Combine(stagingDir, PendingFileName);

                if (!File.Exists(pendingPath))
                    return;

                Log.LogInfo("Found pending update, applying...");

                var pending = ParsePendingJson(pendingPath);
                if (pending == null || pending.Files == null || pending.Files.Count == 0)
                {
                    Log.LogWarning("pending.json empty or malformed, removing");
                    TryDelete(pendingPath);
                    return;
                }

                var filesDir = Path.Combine(stagingDir, "files");
                int applied = 0;
                int failed = 0;

                foreach (var entry in pending.Files)
                {
                    try
                    {
                        var target = Path.Combine(gameRoot, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        var staged = Path.Combine(filesDir, entry.StagedFileName);

                        if (!File.Exists(staged))
                        {
                            Log.LogWarning("Staged file missing: " + entry.StagedFileName);
                            failed++;
                            continue;
                        }

                        var targetDir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        if (File.Exists(target))
                        {
                            var oldPath = target + OldFileSuffix;
                            TryDelete(oldPath);
                            try
                            {
                                File.Move(target, oldPath);
                            }
                            catch (Exception ex)
                            {
                                Log.LogWarning("Cannot rename " + entry.RelativePath + ": " + ex.Message);
                                failed++;
                                continue;
                            }
                        }

                        File.Move(staged, target);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        Log.LogError("Failed to apply " + entry.RelativePath + ": " + ex.Message);
                        failed++;
                    }
                }

                TryDelete(pendingPath);

                Log.LogInfo("Update applied: " + applied + " files updated, " + failed + " failed");

                if (Directory.Exists(filesDir))
                {
                    try
                    {
                        var remaining = Directory.GetFiles(filesDir);
                        if (remaining.Length == 0)
                            Directory.Delete(filesDir, false);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Log.LogError("VPB.Patcher error: " + ex);
            }
        }

        private static void CleanupOldFiles(string gameRoot)
        {
            try
            {
                CleanupOldInDir(gameRoot);
                var bepinPlugins = Path.Combine(Path.Combine(gameRoot, "BepInEx"), "plugins");
                if (Directory.Exists(bepinPlugins))
                {
                    CleanupOldInDir(bepinPlugins);
                    foreach (var sub in Directory.GetDirectories(bepinPlugins))
                        CleanupOldInDir(sub);
                }
                var bepinCore = Path.Combine(Path.Combine(gameRoot, "BepInEx"), "core");
                if (Directory.Exists(bepinCore))
                    CleanupOldInDir(bepinCore);
            }
            catch { }
        }

        private static void CleanupOldInDir(string dir)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*" + OldFileSuffix))
                    TryDelete(f);
            }
            catch { }
        }

        private static string ResolveGameRoot()
        {
            try
            {
                var asmPath = Assembly.GetExecutingAssembly().Location;
                // VPB.Patcher.dll lives in <gameRoot>/BepInEx/patchers/
                var patchersDir = Path.GetDirectoryName(asmPath);
                var bepinDir = Path.GetDirectoryName(patchersDir);
                var gameRoot = Path.GetDirectoryName(bepinDir);

                if (gameRoot != null && File.Exists(Path.Combine(gameRoot, "VaM.exe")))
                    return gameRoot;

                // Fallback: walk up from current directory
                var cur = Directory.GetCurrentDirectory();
                if (File.Exists(Path.Combine(cur, "VaM.exe")))
                    return cur;
            }
            catch { }
            return null;
        }

        private static PendingUpdate ParsePendingJson(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                return SimpleJsonParser.ParsePending(json);
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to parse pending.json: " + ex.Message);
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // Minimal JSON parser — no external deps allowed in patcher
        private static class SimpleJsonParser
        {
            public static PendingUpdate ParsePending(string json)
            {
                var result = new PendingUpdate();
                result.Files = new List<PendingFileEntry>();

                // Parse "version"
                result.Version = ExtractStringValue(json, "version");
                result.Branch = ExtractStringValue(json, "branch");

                // Parse "files" array entries
                int filesStart = json.IndexOf("\"files\"");
                if (filesStart < 0) return result;

                int arrayStart = json.IndexOf('[', filesStart);
                if (arrayStart < 0) return result;

                int arrayEnd = json.IndexOf(']', arrayStart);
                if (arrayEnd < 0) return result;

                var arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

                int objStart = 0;
                while (true)
                {
                    objStart = arrayContent.IndexOf('{', objStart);
                    if (objStart < 0) break;
                    int objEnd = arrayContent.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    var objStr = arrayContent.Substring(objStart, objEnd - objStart + 1);
                    var entry = new PendingFileEntry
                    {
                        RelativePath = ExtractStringValue(objStr, "relativePath"),
                        StagedFileName = ExtractStringValue(objStr, "stagedFileName"),
                        Sha = ExtractStringValue(objStr, "sha")
                    };

                    if (!string.IsNullOrEmpty(entry.RelativePath) && !string.IsNullOrEmpty(entry.StagedFileName))
                        result.Files.Add(entry);

                    objStart = objEnd + 1;
                }

                return result;
            }

            private static string ExtractStringValue(string json, string key)
            {
                var search = "\"" + key + "\"";
                int keyIdx = json.IndexOf(search);
                if (keyIdx < 0) return null;

                int colonIdx = json.IndexOf(':', keyIdx + search.Length);
                if (colonIdx < 0) return null;

                int quoteStart = json.IndexOf('"', colonIdx + 1);
                if (quoteStart < 0) return null;

                int quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) return null;

                return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
            }
        }

        private class PendingUpdate
        {
            public string Version;
            public string Branch;
            public List<PendingFileEntry> Files;
        }

        private class PendingFileEntry
        {
            public string RelativePath;
            public string StagedFileName;
            public string Sha;
        }
    }
}
