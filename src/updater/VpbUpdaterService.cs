using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

namespace VPB
{
    public enum VpbUpdateStatus
    {
        Idle,
        Checking,
        Downloading,
        Staged,
        UpToDate,
        Error
    }

    public class VpbUpdaterService
    {
        private const string RepoOwner = "gicstin";
        private const string RepoName = "VPB";
        private const string PatchRoot = "vam_patch/";
        private const string StagingDirName = "vpb_update_staging";
        private const string PendingFileName = "pending.json";
        private const int TimeoutSeconds = 30;

        private readonly string _gameRoot;
        private readonly MonoBehaviour _host;
        private VpbUpdateConfig _config;
        private volatile string[] _cachedBranches;
        private Coroutine _activeCoroutine;

        public VpbUpdateStatus Status { get; private set; } = VpbUpdateStatus.Idle;
        public string StatusMessage { get; private set; } = "";
        public string AvailableVersion { get; private set; }
        public float Progress { get; private set; }
        public bool HasPendingUpdate { get; private set; }

        public Action OnStatusChanged;

        public VpbUpdaterService(string gameRoot, MonoBehaviour host)
        {
            _gameRoot = gameRoot;
            _host = host;
            _config = VpbUpdateConfig.Load(gameRoot);
            HasPendingUpdate = File.Exists(GetPendingPath());
        }

        public VpbUpdateConfig Config => _config;

        public void SetBranch(string branch)
        {
            _config.Branch = branch ?? "main";
            _config.Save();
        }

        public void CheckForUpdateAsync()
        {
            if (_activeCoroutine != null) return;
            Status = VpbUpdateStatus.Checking;
            StatusMessage = "Checking for updates...";
            Progress = 0f;
            _activeCoroutine = _host.StartCoroutine(CheckAndStageCoroutine());
        }

        public void Cancel()
        {
            if (_activeCoroutine != null)
            {
                _host.StopCoroutine(_activeCoroutine);
                _activeCoroutine = null;
            }
            Status = VpbUpdateStatus.Idle;
            StatusMessage = "";
        }

        public bool IsBusy => _activeCoroutine != null;

        private string GetStagingDir()
        {
            return Path.Combine(Path.Combine(Path.Combine(_gameRoot, "BepInEx"), "plugins"), StagingDirName);
        }

        private string GetPendingPath()
        {
            return Path.Combine(GetStagingDir(), PendingFileName);
        }

        // ── Coroutine-based update flow using UnityWebRequest ──

        private IEnumerator CheckAndStageCoroutine()
        {
            string branch = _config.Branch ?? "main";

            // 1. Fetch remote version
            string versionUrl = "https://raw.githubusercontent.com/" + RepoOwner + "/" + RepoName + "/" + branch + "/plugin_version.txt";
            string versionText = null;
            yield return DownloadText(versionUrl, false, r => versionText = r);

            if (string.IsNullOrEmpty(versionText))
            {
                SetError("Could not fetch remote version");
                yield break;
            }

            string[] versionLines = versionText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (versionLines.Length < 2)
            {
                SetError("Invalid remote version format");
                yield break;
            }

            string remoteVersion = versionLines[0].Trim() + "." + versionLines[1].Trim();

            // 2. Compare with local
            string localVersion = PluginVersionInfo.Version;
            if (remoteVersion == localVersion)
            {
                Status = VpbUpdateStatus.UpToDate;
                StatusMessage = "Up to date (" + localVersion + ")";
                AvailableVersion = null;
                _config.LastCheckUtc = DateTime.UtcNow.ToString("o");
                _config.Save();
                FinishCoroutine();
                yield break;
            }

            AvailableVersion = remoteVersion;
            StatusMessage = "Update available: " + remoteVersion;

            // 3. Fetch manifest
            string manifestUrl = "https://raw.githubusercontent.com/" + RepoOwner + "/" + RepoName + "/" + branch + "/" + PatchRoot + "patch_manifest.json";
            string manifestJson = null;
            yield return DownloadText(manifestUrl, false, r => manifestJson = r);

            if (string.IsNullOrEmpty(manifestJson))
            {
                SetError("Could not fetch patch manifest");
                yield break;
            }

            var manifestItems = ParseManifest(manifestJson);
            if (manifestItems == null || manifestItems.Count == 0)
            {
                SetError("Empty or invalid manifest");
                yield break;
            }

            // 4. Fetch tree for SHA comparison
            string treeUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/git/trees/" + branch + "?recursive=1";
            string treeJson = null;
            yield return DownloadText(treeUrl, true, r => treeJson = r);

            Dictionary<string, string> remoteShas = ParseTreeShas(treeJson);

            // 5. Diff against local files (offload SHA computation to threadpool)
            StatusMessage = "Comparing files...";
            List<ManifestItem> filesToUpdate = null;
            bool diffDone = false;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    filesToUpdate = DiffFiles(manifestItems, remoteShas);
                }
                catch { filesToUpdate = new List<ManifestItem>(); }
                diffDone = true;
            });

            while (!diffDone) yield return null;

            if (filesToUpdate.Count == 0)
            {
                Status = VpbUpdateStatus.UpToDate;
                StatusMessage = "All files match remote (" + remoteVersion + ")";
                _config.LastCheckUtc = DateTime.UtcNow.ToString("o");
                _config.Save();
                FinishCoroutine();
                yield break;
            }

            // 6. Download to staging
            Status = VpbUpdateStatus.Downloading;
            string stagingDir = GetStagingDir();
            string filesDir = Path.Combine(stagingDir, "files");
            if (!Directory.Exists(filesDir))
                Directory.CreateDirectory(filesDir);

            var pendingEntries = new List<PendingStagedFile>();

            for (int i = 0; i < filesToUpdate.Count; i++)
            {
                var item = filesToUpdate[i];
                Progress = (float)i / filesToUpdate.Count;
                StatusMessage = "Downloading " + (i + 1) + "/" + filesToUpdate.Count + ": " + item.RelativePath;

                string rawUrl = "https://raw.githubusercontent.com/" + RepoOwner + "/" + RepoName + "/" + branch + "/" + PatchRoot + item.RelativePath.Replace('\\', '/');
                string stagedName = Guid.NewGuid().ToString("N") + ".tmp";
                string stagedPath = Path.Combine(filesDir, stagedName);

                bool dlOk = false;
                yield return DownloadFileCoroutine(rawUrl, stagedPath, ok => dlOk = ok);

                if (!dlOk)
                {
                    SetError("Failed to download: " + item.RelativePath);
                    yield break;
                }

                // Verify SHA (on threadpool)
                string fullManifestPath = (PatchRoot + item.RelativePath).Replace('\\', '/');
                string expectedSha = null;
                if (remoteShas != null)
                    remoteShas.TryGetValue(fullManifestPath, out expectedSha);

                if (!string.IsNullOrEmpty(expectedSha))
                {
                    string computedSha = null;
                    bool shaDone = false;
                    string capturedStagedPath = stagedPath;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { computedSha = ComputeGitBlobSha1(capturedStagedPath); } catch { }
                        shaDone = true;
                    });
                    while (!shaDone) yield return null;

                    if (!string.Equals(computedSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(stagedPath); } catch { }
                        SetError("Checksum mismatch: " + item.RelativePath);
                        yield break;
                    }
                }

                pendingEntries.Add(new PendingStagedFile
                {
                    RelativePath = item.RelativePath,
                    StagedFileName = stagedName,
                    Sha = expectedSha ?? ""
                });
            }

            // 7. Write pending.json
            Progress = 1f;
            WritePendingJson(stagingDir, remoteVersion, branch, pendingEntries);

            _config.LastCheckUtc = DateTime.UtcNow.ToString("o");
            _config.LastStagedVersion = remoteVersion;
            _config.Save();

            HasPendingUpdate = true;
            Status = VpbUpdateStatus.Staged;
            StatusMessage = "Update " + remoteVersion + " staged. Restart VaM to apply. (" + pendingEntries.Count + " files)";
            FinishCoroutine();
        }

        private void FinishCoroutine()
        {
            _activeCoroutine = null;
            try { OnStatusChanged?.Invoke(); } catch { }
        }

        private void SetError(string msg)
        {
            Status = VpbUpdateStatus.Error;
            StatusMessage = msg;
            LogUtil.LogError("[VpbUpdater] " + msg);
            _activeCoroutine = null;
            try { OnStatusChanged?.Invoke(); } catch { }
        }

        // ── UnityWebRequest helpers ──

        private IEnumerator DownloadText(string url, bool githubApi, Action<string> callback)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = TimeoutSeconds;
                if (githubApi)
                    req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.SetRequestHeader("User-Agent", "VPB-Updater/1.0");

                yield return req.SendWebRequest();

                if (req.isNetworkError || req.isHttpError)
                {
                    LogUtil.LogError("[VpbUpdater] GET " + url + " failed: " + req.error);
                    callback(null);
                }
                else
                {
                    callback(req.downloadHandler.text);
                }
            }
        }

        private IEnumerator DownloadFileCoroutine(string url, string destPath, Action<bool> callback)
        {
            string dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 120;
                req.SetRequestHeader("User-Agent", "VPB-Updater/1.0");

                yield return req.SendWebRequest();

                if (req.isNetworkError || req.isHttpError)
                {
                    LogUtil.LogError("[VpbUpdater] Download " + url + " failed: " + req.error);
                    callback(false);
                }
                else
                {
                    try
                    {
                        File.WriteAllBytes(destPath, req.downloadHandler.data);
                        callback(true);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VpbUpdater] Write " + destPath + " failed: " + ex.Message);
                        callback(false);
                    }
                }
            }
        }

        // ── File diff (runs on threadpool) ──

        private List<ManifestItem> DiffFiles(List<ManifestItem> manifestItems, Dictionary<string, string> remoteShas)
        {
            var result = new List<ManifestItem>();
            foreach (var item in manifestItems)
            {
                if (item.IsDirectory) continue;

                string localPath = Path.Combine(_gameRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                string fullManifestPath = (PatchRoot + item.RelativePath).Replace('\\', '/');

                string remoteSha = null;
                if (remoteShas != null)
                    remoteShas.TryGetValue(fullManifestPath, out remoteSha);

                if (!File.Exists(localPath))
                {
                    result.Add(item);
                    continue;
                }

                if (!string.IsNullOrEmpty(remoteSha))
                {
                    string localSha = ComputeGitBlobSha1(localPath);
                    if (!string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                        result.Add(item);
                }
                else
                {
                    result.Add(item);
                }
            }
            return result;
        }

        // ── JSON helpers ──

        private void WritePendingJson(string stagingDir, string version, string branch, List<PendingStagedFile> entries)
        {
            var root = new JSONClass();
            root["version"] = version;
            root["branch"] = branch;
            root["timestamp"] = DateTime.UtcNow.ToString("o");

            var arr = new JSONArray();
            foreach (var e in entries)
            {
                var obj = new JSONClass();
                obj["relativePath"] = e.RelativePath;
                obj["stagedFileName"] = e.StagedFileName;
                obj["sha"] = e.Sha;
                arr.Add(obj);
            }
            root["files"] = arr;

            string path = Path.Combine(stagingDir, PendingFileName);
            File.WriteAllText(path, VPB.src.util.JsonSerializationUtil.Serialize(root, 1024));
        }

        private static List<ManifestItem> ParseManifest(string json)
        {
            var result = new List<ManifestItem>();
            try
            {
                var arr = JSON.Parse(json) as JSONArray;
                if (arr == null) return result;

                for (int i = 0; i < arr.Count; i++)
                {
                    var node = arr[i];
                    result.Add(new ManifestItem
                    {
                        RelativePath = node["RelativePath"].Value,
                        IsDirectory = node["IsDirectory"].AsBool
                    });
                }
            }
            catch { }
            return result;
        }

        private static Dictionary<string, string> ParseTreeShas(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var root = JSON.Parse(json);
                if (root == null) return null;

                var truncated = root["truncated"];
                if (truncated != null && truncated.AsBool)
                    return null;

                var tree = root["tree"] as JSONArray;
                if (tree == null) return null;

                for (int i = 0; i < tree.Count; i++)
                {
                    var item = tree[i];
                    string type = item["type"].Value;
                    if (type != "blob") continue;

                    string path = item["path"].Value;
                    string sha = item["sha"].Value;
                    if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(sha))
                        result[path] = sha;
                }
            }
            catch { }
            return result;
        }

        private static string ComputeGitBlobSha1(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            long length = fileInfo.Length;

            using (var sha1 = SHA1.Create())
            {
                byte[] header = Encoding.UTF8.GetBytes("blob " + length + "\0");
                sha1.TransformBlock(header, 0, header.Length, null, 0);

                byte[] buffer = new byte[81920];
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int read;
                    while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        sha1.TransformBlock(buffer, 0, read, null, 0);
                    }
                }

                sha1.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha1.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // ── Branch fetching ──

        public string[] GetAvailableBranches()
        {
            return _cachedBranches ?? new[] { _config.Branch ?? "main" };
        }

        public void FetchBranchesAsync()
        {
            _host.StartCoroutine(FetchBranchesCoroutine());
        }

        private IEnumerator FetchBranchesCoroutine()
        {
            string url = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/branches";
            string json = null;
            yield return DownloadText(url, true, r => json = r);

            if (string.IsNullOrEmpty(json)) yield break;

            try
            {
                var arr = JSON.Parse(json) as JSONArray;
                if (arr == null) yield break;

                var names = new List<string>();
                for (int i = 0; i < arr.Count; i++)
                {
                    string name = arr[i]["name"].Value;
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }

                names.Sort((a, b) =>
                {
                    if (a == "main") return -1;
                    if (b == "main") return 1;
                    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });

                if (names.Count > 0)
                    _cachedBranches = names.ToArray();
            }
            catch { }
        }

        public void ClearStagedUpdate()
        {
            try
            {
                string stagingDir = GetStagingDir();
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, true);
                HasPendingUpdate = false;
                _config.LastStagedVersion = "";
                _config.Save();
            }
            catch { }
        }

        private class ManifestItem
        {
            public string RelativePath;
            public bool IsDirectory;
        }

        private class PendingStagedFile
        {
            public string RelativePath;
            public string StagedFileName;
            public string Sha;
        }
    }
}
