using System;
using System.Collections.Generic;
using System.IO;

namespace VPB.Shared
{
    internal static class VpbUpdateManifest
    {
        internal const string ManifestOwnedPrefix = "BepInEx/plugins/VPB/";
        internal const string MpNetPrefix = "net/";
        internal const string MpNetDir = "net";

        internal static string PluginsDir(string gameRoot)
        {
            return Path.Combine(Path.Combine(gameRoot, "BepInEx"), "plugins");
        }

        internal static string PluginDir(string pluginsDir)
        {
            return Path.Combine(pluginsDir, VpbLegacyLayout.PluginSubDirName);
        }

        internal static string NewStagingDir(string pluginsDir)
        {
            return Path.Combine(PluginDir(pluginsDir), VpbLegacyLayout.StagingDirName);
        }

        internal static string LegacyStagingDir(string pluginsDir)
        {
            return Path.Combine(pluginsDir, VpbLegacyLayout.StagingDirName);
        }

        internal static string PendingPath(string stagingDir)
        {
            return Path.Combine(stagingDir, VpbLegacyLayout.PendingFileName);
        }

        internal static bool StagingHasPending(string stagingDir)
        {
            try { return File.Exists(PendingPath(stagingDir)); }
            catch { return false; }
        }

        internal static string GameRelative(string gameRoot, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return gameRoot;
            return Path.Combine(gameRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string EncodeGitHubRawPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return "";
            string p = relativePath.Replace('\\', '/');
            string[] parts = p.Split('/');
            for (int i = 0; i < parts.Length; i++)
                parts[i] = Uri.EscapeDataString(parts[i] ?? "");
            return string.Join("/", parts);
        }

        internal static bool ListsMpNet(IList<string> ownedFiles, IList<string> ownedDirs)
        {
            if (ownedFiles != null)
            {
                for (int i = 0; i < ownedFiles.Count; i++)
                {
                    if (IsMpNetRel(ownedFiles[i])) return true;
                }
            }
            if (ownedDirs != null)
            {
                for (int i = 0; i < ownedDirs.Count; i++)
                {
                    if (IsMpNetRel(ownedDirs[i])) return true;
                }
            }
            return false;
        }

        internal static bool ShouldSweepMpOnly(IList<string> ownedFiles, IList<string> ownedDirs)
        {
            return !ListsMpNet(ownedFiles, ownedDirs);
        }

        internal static bool TryReadOwnedManifest(string json, List<string> files, List<string> dirs)
        {
            if (files == null || dirs == null) return false;
            if (string.IsNullOrEmpty(json) || !json.TrimEnd().EndsWith("]", StringComparison.Ordinal))
                return false;

            List<ManifestRow> rows = ParseManifestRows(json);
            if (rows == null || rows.Count == 0) return false;

            for (int i = 0; i < rows.Count; i++)
            {
                string rel = rows[i].RelativePath;
                if (string.IsNullOrEmpty(rel)) continue;
                rel = rel.Replace('\\', '/');
                if (!rel.StartsWith(ManifestOwnedPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                string owned = rel.Substring(ManifestOwnedPrefix.Length);
                if (owned.Length == 0) continue;

                if (rows[i].IsDirectory) dirs.Add(owned);
                else files.Add(owned.ToLowerInvariant());
            }

            return true;
        }

        internal static List<ManifestRow> ParseManifestRows(string json)
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

                string objStr = json.Substring(objStart, objEnd - objStart + 1);
                string rel = ExtractStringValue(objStr, "RelativePath");
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

        internal static PendingUpdate ParsePending(string json)
        {
            var result = new PendingUpdate();
            result.Files = new List<PendingFileEntry>();
            if (string.IsNullOrEmpty(json)) return result;

            result.Version = ExtractStringValue(json, "version");
            result.Branch = ExtractStringValue(json, "branch");

            int filesStart = json.IndexOf("\"files\"");
            if (filesStart < 0) return result;

            int arrayStart = json.IndexOf('[', filesStart);
            if (arrayStart < 0) return result;

            int arrayEnd = FindMatchingBracket(json, arrayStart, '[', ']');
            if (arrayEnd < 0) return result;

            string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);

            int objStart = 0;
            while (true)
            {
                objStart = arrayContent.IndexOf('{', objStart);
                if (objStart < 0) break;
                int objEnd = arrayContent.IndexOf('}', objStart);
                if (objEnd < 0) break;

                string objStr = arrayContent.Substring(objStart, objEnd - objStart + 1);
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

        internal static ApplyResult ApplyStagedUpdates(
            string gameRoot,
            Action<string> info,
            Action<string> warn,
            Action<string> error)
        {
            string pluginsDir = PluginsDir(gameRoot);
            string newStaging = NewStagingDir(pluginsDir);
            string oldStaging = LegacyStagingDir(pluginsDir);

            ApplyResult fromNew = ApplyPendingUpdate(gameRoot, newStaging, info, warn, error);
            if (fromNew.HadPending && StagingUpdateWasReal(fromNew, newStaging))
            {
                if (!StagingHasPending(newStaging))
                    ClearStaging(oldStaging);
                return fromNew;
            }

            ApplyResult fromOld = ApplyPendingUpdate(gameRoot, oldStaging, info, warn, error);
            if (fromOld.HadPending && !StagingHasPending(oldStaging))
                ClearStaging(newStaging);
            return fromOld.HadPending ? fromOld : fromNew;
        }

        private static bool StagingUpdateWasReal(ApplyResult result, string stagingDir)
        {
            if (result.Applied > 0 || result.Blocked > 0 || result.Abandoned)
                return true;
            return StagingHasPending(stagingDir);
        }

        internal static ApplyResult ApplyPendingUpdate(
            string gameRoot,
            string stagingDir,
            Action<string> info,
            Action<string> warn,
            Action<string> error)
        {
            var result = new ApplyResult();
            try
            {
                string pendingPath = PendingPath(stagingDir);
                if (!File.Exists(pendingPath))
                    return result;

                result.HadPending = true;
                if (info != null) info("Found pending update, applying...");

                PendingUpdate pending = ParsePending(File.ReadAllText(pendingPath));
                if (pending == null || pending.Files == null || pending.Files.Count == 0)
                {
                    if (warn != null) warn("pending.json empty or malformed, removing");
                    TryDelete(pendingPath);
                    TryDelete(Path.Combine(stagingDir, "retry.count"));
                    return result;
                }

                string filesDir = Path.Combine(stagingDir, "files");
                int applied = 0;
                int failed = 0;
                int blocked = 0;

                for (int i = 0; i < pending.Files.Count; i++)
                {
                    PendingFileEntry entry = pending.Files[i];
                    try
                    {
                        string target = GameRelative(gameRoot, entry.RelativePath);
                        string staged = Path.Combine(filesDir, entry.StagedFileName);

                        if (!File.Exists(staged))
                        {
                            if (warn != null) warn("Staged file missing: " + entry.StagedFileName);
                            failed++;
                            continue;
                        }

                        string targetDir = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                            Directory.CreateDirectory(targetDir);

                        if (File.Exists(target))
                        {
                            string oldPath = target + VpbLegacyLayout.OldFileSuffix;
                            TryDelete(oldPath);
                            try
                            {
                                File.Move(target, oldPath);
                            }
                            catch (Exception ex)
                            {
                                if (warn != null) warn("Cannot rename " + entry.RelativePath + ": " + ex.Message);
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
                        if (error != null) error("Failed to apply " + entry.RelativePath + ": " + ex.Message);
                        failed++;
                        blocked++;
                    }
                }

                result.Applied = applied;
                result.Failed = failed;
                result.Blocked = blocked;
                if (info != null) info("Update applied: " + applied + " files updated, " + failed + " failed");

                string retryPath = Path.Combine(stagingDir, "retry.count");
                int retries = ReadRetryCount(retryPath);

                if (blocked > 0 && retries < 3)
                {
                    WriteRetryCount(retryPath, retries + 1);
                    if (warn != null)
                        warn("Update incomplete: " + blocked + " file(s) locked; keeping pending for retry "
                            + (retries + 1) + "/3 at next launch");
                    return result;
                }

                if (blocked > 0)
                {
                    result.Abandoned = true;
                    if (error != null)
                        error("Update abandoned after " + retries + " retries; " + blocked
                            + " file(s) could not be replaced. Reinstall VPB manually.");
                }

                TryDelete(pendingPath);
                TryDelete(retryPath);

                if (Directory.Exists(filesDir))
                {
                    try
                    {
                        string[] remaining = Directory.GetFiles(filesDir);
                        if (remaining.Length == 0)
                            Directory.Delete(filesDir, false);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                if (error != null) error("pending-update error: " + ex);
            }
            return result;
        }

        internal static void ClearStaging(string stagingDir)
        {
            if (string.IsNullOrEmpty(stagingDir) || !Directory.Exists(stagingDir)) return;
            try
            {
                string[] files = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        File.SetAttributes(files[i], FileAttributes.Normal);
                        File.Delete(files[i]);
                    }
                    catch { }
                }
                Directory.Delete(stagingDir, true);
            }
            catch { }
        }

        internal static bool CopyStaging(string src, string dst)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dst) || !Directory.Exists(src))
                return false;
            try
            {
                CopyDir(src, dst);
                return StagingHasPending(dst);
            }
            catch
            {
                return false;
            }
        }

        private static void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(dst)) Directory.CreateDirectory(dst);
            string[] files = Directory.GetFiles(src);
            for (int i = 0; i < files.Length; i++)
            {
                string name = Path.GetFileName(files[i]);
                File.Copy(files[i], Path.Combine(dst, name), true);
            }
            string[] dirs = Directory.GetDirectories(src);
            for (int i = 0; i < dirs.Length; i++)
                CopyDir(dirs[i], Path.Combine(dst, Path.GetFileName(dirs[i])));
        }

        private static bool IsMpNetRel(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            string n = rel.Replace('\\', '/');
            if (n.StartsWith("/", StringComparison.Ordinal)) n = n.Substring(1);
            return string.Equals(n, MpNetDir, StringComparison.OrdinalIgnoreCase)
                || n.StartsWith(MpNetPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static int FindMatchingBracket(string json, int openIdx, char open, char close)
        {
            int depth = 0;
            for (int i = openIdx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static bool ExtractBoolValue(string json, string key)
        {
            string search = "\"" + key + "\"";
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
            string search = "\"" + key + "\"";
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
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, value.ToString());
            }
            catch { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        internal struct ApplyResult
        {
            public bool HadPending;
            public int Applied;
            public int Failed;
            public int Blocked;
            public bool Abandoned;
        }

        internal class ManifestRow
        {
            public string RelativePath;
            public bool IsDirectory;
        }

        internal class PendingUpdate
        {
            public string Version;
            public string Branch;
            public List<PendingFileEntry> Files;
        }

        internal class PendingFileEntry
        {
            public string RelativePath;
            public string StagedFileName;
            public string Sha;
        }
    }
}
