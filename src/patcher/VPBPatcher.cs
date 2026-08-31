using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using Mono.Cecil;
using VPB.Shared;

namespace VPB.Patcher
{
    public static class VPBPatcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        private static ManualLogSource Log;

        private const string StagingDirName = VpbLegacyLayout.StagingDirName;
        private const string PendingFileName = VpbLegacyLayout.PendingFileName;
        private const string RetryFileName = "retry.count";
        private const string OldFileSuffix = VpbLegacyLayout.OldFileSuffix;
        private const string PluginSubDirName = VpbLegacyLayout.PluginSubDirName;
        private const string ManifestFileName = "patch_manifest.json";
        private const string ManifestOwnedPrefix = "BepInEx/plugins/VPB/";
        private const int MinManifestRowsToPrune = 4;
        private const int MaxPendingRetries = 3;

        private static readonly string[] PruneKeepNames =
        {
            "VPB.pdb"
        };

        private static readonly string[] PruneProtectedDirs =
        {
            "icons_override",
            "vpb_icons_override",
            "clips",
            StagingDirName
        };

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

                var pluginsDir = Path.Combine(Path.Combine(gameRoot, "BepInEx"), "plugins");

                CleanupOldFiles(gameRoot);
                ApplyPendingUpdate(gameRoot, Path.Combine(Path.Combine(pluginsDir, PluginSubDirName), StagingDirName));
                ApplyPendingUpdate(gameRoot, Path.Combine(pluginsDir, StagingDirName));
                PruneLegacyLayout(gameRoot);
            }
            catch (Exception ex)
            {
                Log.LogError("VPB.Patcher error: " + ex);
            }
        }

        private static void ApplyPendingUpdate(string gameRoot, string stagingDir)
        {
            try
            {
                var pendingPath = Path.Combine(stagingDir, PendingFileName);

                if (!File.Exists(pendingPath))
                    return;

                Log.LogInfo("Found pending update, applying...");

                var pending = ParsePendingJson(pendingPath);
                if (pending == null || pending.Files == null || pending.Files.Count == 0)
                {
                    Log.LogWarning("pending.json empty or malformed, removing");
                    TryDelete(pendingPath);
                    TryDelete(Path.Combine(stagingDir, RetryFileName));
                    return;
                }

                var filesDir = Path.Combine(stagingDir, "files");
                int applied = 0;
                int failed = 0;
                int blocked = 0;

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
                                blocked++;
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
                        blocked++;
                    }
                }

                Log.LogInfo("Update applied: " + applied + " files updated, " + failed + " failed");

                var retryPath = Path.Combine(stagingDir, RetryFileName);
                int retries = ReadRetryCount(retryPath);

                if (blocked > 0 && retries < MaxPendingRetries)
                {
                    WriteRetryCount(retryPath, retries + 1);
                    Log.LogWarning("Update incomplete: " + blocked + " file(s) locked; keeping pending for retry "
                        + (retries + 1) + "/" + MaxPendingRetries + " at next launch");
                    return;
                }

                if (blocked > 0)
                {
                    Log.LogError("Update abandoned after " + retries + " retries; " + blocked
                        + " file(s) could not be replaced. Reinstall VPB manually.");
                }

                TryDelete(pendingPath);
                TryDelete(retryPath);

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
                Log.LogError("VPB.Patcher pending-update error: " + ex);
            }
        }

        private static void PruneLegacyLayout(string gameRoot)
        {
            try
            {
                var pluginsDir = Path.Combine(Path.Combine(gameRoot, "BepInEx"), "plugins");
                var vpbDir = Path.Combine(pluginsDir, PluginSubDirName);
                if (!File.Exists(Path.Combine(vpbDir, "VPB.dll")))
                    return;

                int removed = VpbLegacyLayout.SweepPluginsRoot(pluginsDir, null, LogPruneWarning);

                removed += PruneUnshippedFiles(vpbDir);

                if (removed > 0)
                    Log.LogInfo("Removed " + removed + " stale VPB item(s); the shipped tree is BepInEx/plugins/" + PluginSubDirName);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Legacy layout prune failed: " + ex.Message);
            }
        }

        private static int PruneUnshippedFiles(string vpbDir)
        {
            var manifestPath = Path.Combine(vpbDir, ManifestFileName);
            if (!File.Exists(manifestPath))
                return 0;

            var files = new List<string>();
            var dirs = new List<string>();
            if (!TryReadOwnedManifest(manifestPath, files, dirs))
                return 0;

            if (files.Count < MinManifestRowsToPrune || !files.Contains("vpb.dll"))
            {
                Log.LogWarning("Shipped manifest looks incomplete (" + files.Count + " owned files); skipping prune");
                return 0;
            }

            var shippedDirs = new List<string>(dirs.Count);
            for (int i = 0; i < dirs.Count; i++)
                shippedDirs.Add(dirs[i].ToLowerInvariant());

            int removed = 0;
            PruneDirectory(vpbDir, "", files, shippedDirs, ref removed);
            return removed;
        }

        private static void PruneDirectory(string absDir, string relDir, List<string> files, List<string> shippedDirs, ref int removed)
        {
            string[] present;
            try { present = Directory.GetFiles(absDir); }
            catch { return; }

            for (int i = 0; i < present.Length; i++)
            {
                var name = Path.GetFileName(present[i]);
                if (IsProtectedName(name, PruneKeepNames)) continue;
                if (name.EndsWith(OldFileSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                var rel = relDir.Length == 0 ? name : relDir + "/" + name;
                if (files.Contains(rel.ToLowerInvariant())) continue;

                if (RetireFile(present[i]))
                {
                    Log.LogInfo("Removed unshipped file: " + rel);
                    removed++;
                }
            }

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(absDir); }
            catch { return; }

            for (int i = 0; i < subDirs.Length; i++)
            {
                var name = Path.GetFileName(subDirs[i]);
                if (IsProtectedName(name, PruneProtectedDirs)) continue;

                var rel = relDir.Length == 0 ? name : relDir + "/" + name;
                PruneDirectory(subDirs[i], rel, files, shippedDirs, ref removed);

                if (shippedDirs.Contains(rel.ToLowerInvariant())) continue;

                try
                {
                    if (Directory.GetFiles(subDirs[i]).Length == 0 && Directory.GetDirectories(subDirs[i]).Length == 0)
                    {
                        Directory.Delete(subDirs[i], false);
                        Log.LogInfo("Removed superseded directory: " + rel);
                        removed++;
                    }
                }
                catch { }
            }
        }

        private static bool IsProtectedName(string name, string[] list)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (string.Equals(name, list[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool TryReadOwnedManifest(string path, List<string> files, List<string> dirs)
        {
            try
            {
                var json = File.ReadAllText(path);
                if (json == null || !json.TrimEnd().EndsWith("]", StringComparison.Ordinal))
                {
                    Log.LogWarning("Shipped manifest is truncated; skipping prune");
                    return false;
                }

                var rows = SimpleJsonParser.ParseManifestRows(json);
                if (rows == null || rows.Count == 0) return false;

                for (int i = 0; i < rows.Count; i++)
                {
                    var rel = rows[i].RelativePath;
                    if (string.IsNullOrEmpty(rel)) continue;
                    rel = rel.Replace('\\', '/');
                    if (!rel.StartsWith(ManifestOwnedPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    var owned = rel.Substring(ManifestOwnedPrefix.Length);
                    if (owned.Length == 0) continue;

                    if (rows[i].IsDirectory) dirs.Add(owned);
                    else files.Add(owned.ToLowerInvariant());
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not read shipped manifest: " + ex.Message);
                return false;
            }
        }

        private static void LogPruneWarning(string message)
        {
            Log.LogWarning(message);
        }

        private static bool RetireFile(string path)
        {
            return VpbLegacyLayout.RetireFile(path, LogPruneWarning);
        }

        private static int ReadRetryCount(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                int value;
                if (int.TryParse(File.ReadAllText(path).Trim(), out value) && value > 0) return value;
            }
            catch { }
            return 0;
        }

        private static void WriteRetryCount(string path, int value)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, value.ToString());
            }
            catch { }
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

            public static List<ManifestRow> ParseManifestRows(string json)
            {
                var rows = new List<ManifestRow>();
                if (string.IsNullOrEmpty(json)) return rows;

                int objStart = 0;
                while (true)
                {
                    objStart = json.IndexOf('{', objStart);
                    if (objStart < 0) break;
                    int objEnd = json.IndexOf('}', objStart);
                    if (objEnd < 0) break;

                    var objStr = json.Substring(objStart, objEnd - objStart + 1);
                    var rel = ExtractStringValue(objStr, "RelativePath");
                    if (!string.IsNullOrEmpty(rel))
                    {
                        rows.Add(new ManifestRow
                        {
                            RelativePath = rel,
                            IsDirectory = ExtractBoolValue(objStr, "IsDirectory")
                        });
                    }

                    objStart = objEnd + 1;
                }

                return rows;
            }

            private static bool ExtractBoolValue(string json, string key)
            {
                var search = "\"" + key + "\"";
                int keyIdx = json.IndexOf(search);
                if (keyIdx < 0) return false;

                int colonIdx = json.IndexOf(':', keyIdx + search.Length);
                if (colonIdx < 0) return false;

                int trueIdx = json.IndexOf("true", colonIdx + 1, StringComparison.OrdinalIgnoreCase);
                int falseIdx = json.IndexOf("false", colonIdx + 1, StringComparison.OrdinalIgnoreCase);
                if (trueIdx < 0) return false;
                return falseIdx < 0 || trueIdx < falseIdx;
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

        private class ManifestRow
        {
            public string RelativePath;
            public bool IsDirectory;
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
