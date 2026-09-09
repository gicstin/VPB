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
        private const string OldFileSuffix = VpbLegacyLayout.OldFileSuffix;
        private const string PluginSubDirName = VpbLegacyLayout.PluginSubDirName;
        private const string ManifestFileName = "patch_manifest.json";
        private const int MinManifestRowsToPrune = 4;

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

                CleanupOldFiles(gameRoot);
                VpbUpdateManifest.ApplyStagedUpdates(gameRoot, LogPruneInfo, LogPruneWarning, LogPruneError);
                PruneLegacyLayout(gameRoot);
            }
            catch (Exception ex)
            {
                Log.LogError("VPB.Patcher error: " + ex);
            }
        }

        private static void PruneLegacyLayout(string gameRoot)
        {
            try
            {
                var pluginsDir = VpbUpdateManifest.PluginsDir(gameRoot);
                var vpbDir = VpbUpdateManifest.PluginDir(pluginsDir);
                if (!File.Exists(Path.Combine(vpbDir, "VPB.dll")))
                    return;

                var files = new List<string>();
                var dirs = new List<string>();
                bool haveManifest = TryReadOwnedManifest(Path.Combine(vpbDir, ManifestFileName), files, dirs);

                int removed = VpbLegacyLayout.SweepPluginsRoot(pluginsDir, LogPruneInfo, LogPruneWarning);
                if (haveManifest && VpbUpdateManifest.ShouldSweepMpOnly(files, dirs))
                    removed += VpbLegacyLayout.SweepMpOnlyUnderVpbDir(vpbDir, LogPruneInfo);
                else if (haveManifest)
                    Log.LogInfo("Keeping net/; shipped manifest lists the multiplayer companion");

                if (haveManifest)
                    removed += PruneUnshippedFiles(vpbDir, files, dirs);

                if (removed > 0)
                    Log.LogInfo("Removed " + removed + " stale VPB item(s); the shipped tree is BepInEx/plugins/" + PluginSubDirName);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Legacy layout prune failed: " + ex.Message);
            }
        }

        private static int PruneUnshippedFiles(string vpbDir, List<string> files, List<string> dirs)
        {
            if (files == null || dirs == null) return 0;

            if (files.Count < MinManifestRowsToPrune || !files.Contains("vpb.dll"))
            {
                Log.LogInfo("Shipped manifest looks incomplete (" + files.Count + " owned files); skipping prune");
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
                if (!File.Exists(path)) return false;
                var json = File.ReadAllText(path);
                if (json == null || !json.TrimEnd().EndsWith("]", StringComparison.Ordinal))
                {
                    Log.LogInfo("Shipped manifest is truncated; skipping prune");
                    return false;
                }

                return VpbUpdateManifest.TryReadOwnedManifest(json, files, dirs);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not read shipped manifest: " + ex.Message);
                return false;
            }
        }

        private static void LogPruneInfo(string message)
        {
            Log.LogInfo(message);
        }

        private static void LogPruneWarning(string message)
        {
            Log.LogWarning(message);
        }

        private static void LogPruneError(string message)
        {
            Log.LogError(message);
        }

        private static bool RetireFile(string path)
        {
            return VpbLegacyLayout.RetireFile(path, LogPruneWarning);
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
