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
using VPB.Shared;

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

    public enum VpbCatalogState
    {
        Unknown,
        Fetching,
        Ready,
        Unavailable
    }

    public class VpbUpdaterService
    {
        private const string RepoOwner = "gicstin";
        private const string RepoName = "VPB";
        private const string PatchRoot = "vam_patch/";
        private const int TimeoutSeconds = 30;

        private readonly string _gameRoot;
        private readonly MonoBehaviour _host;
        private VpbUpdateConfig _config;
        private volatile string[] _cachedBranches;
        private volatile VpbReleaseCatalog _catalog;
        private volatile VpbCatalogState _catalogState = VpbCatalogState.Unknown;
        private volatile string _catalogBranch = "";
        private Coroutine _activeCoroutine;
        private bool _lastCheckUsedApi;

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
            HasPendingUpdate = HasAnyPending();
        }

        public VpbUpdateConfig Config => _config;

        public void SetBranch(string branch)
        {
            string next = branch ?? VpbUpdateConfig.DefaultBranch;
            bool changed = !string.Equals(next, _config.Branch, StringComparison.Ordinal);

            _config.Branch = next;
            _config.Pinned = false;
            _config.PinnedTag = "";
            _config.PinnedVersion = "";
            _config.PinnedSchema = 0;
            _config.Save();
            PinnedRefMissing = false;

            // Each branch publishes its own releases/index.json, so the list belongs to the branch
            // that was selected when it was fetched. Dropping it first means the picker hides
            // rather than offering builds that do not exist on the newly chosen branch.
            if (changed)
            {
                _catalog = null;
                _catalogState = VpbCatalogState.Unknown;
                FetchReleasesAsync();
            }
        }

        public bool IsPinned { get { return _config.Pinned; } }

        public string PinnedVersion { get { return _config.PinnedVersion; } }

        public bool PinnedRefMissing { get; private set; }

        public bool PinnedReleaseIsListed()
        {
            if (!_config.Pinned) return true;
            var catalog = _catalog;
            if (catalog == null || catalog.IsEmpty) return true;
            return catalog.Find(_config.PinnedVersion) != null;
        }

        public VpbReleaseCatalog ReleaseCatalog { get { return _catalog; } }

        public VpbCatalogState ReleaseCatalogState { get { return _catalogState; } }

        public string ReleaseCatalogBranch { get { return _catalogBranch; } }

        public bool LastCheckUsedGitHubApi { get { return _lastCheckUsedApi; } }

        public string PinToRelease(VpbRelease release)
        {
            if (release == null) return "No release selected.";

            var catalog = _catalog;
            if (catalog != null && catalog.IsBelowFloor(release.Version))
            {
                return "VPB " + release.Version + " predates the single-folder plugin layout ("
                    + catalog.MinRollbackVersion + "). Rolling back across it is not supported.";
            }

            _config.Pinned = true;
            _config.PinnedTag = release.Tag;
            _config.PinnedVersion = release.Version;
            _config.PinnedSchema = release.Schema;
            _config.Save();
            PinnedRefMissing = false;

            string warning = DescribeSchemaRisk(release.Schema);
            Status = VpbUpdateStatus.Idle;
            StatusMessage = "Pinned to " + release.Version + (warning == null ? "" : "  -  " + warning);
            try { OnStatusChanged?.Invoke(); } catch { }
            return null;
        }

        public void UnpinToLatest()
        {
            _config.Pinned = false;
            _config.PinnedTag = "";
            _config.PinnedVersion = "";
            _config.PinnedSchema = 0;
            _config.Save();

            PinnedRefMissing = false;
            Status = VpbUpdateStatus.Idle;
            StatusMessage = "Following " + _config.Branch + " again.";
            try { OnStatusChanged?.Invoke(); } catch { }
        }

        public static string DescribeSchemaRisk(int releaseSchema)
        {
            if (releaseSchema <= 0) return "database schema unknown for this build";
            int local = VpbLocalDatabase.CurrentSchemaVersion;
            if (releaseSchema >= local) return null;
            return "that build expects database schema " + releaseSchema + ", yours is " + local
                + "; it may rebuild the index";
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

        private string GetPluginsDir()
        {
            return VpbUpdateManifest.PluginsDir(_gameRoot);
        }

        private string GetStagingDir()
        {
            return VpbUpdateManifest.NewStagingDir(GetPluginsDir());
        }

        private string GetLegacyStagingDir()
        {
            return VpbUpdateManifest.LegacyStagingDir(GetPluginsDir());
        }

        private string GetPendingPath()
        {
            return VpbUpdateManifest.PendingPath(GetStagingDir());
        }

        private bool HasAnyPending()
        {
            return VpbUpdateManifest.StagingHasPending(GetStagingDir())
                || VpbUpdateManifest.StagingHasPending(GetLegacyStagingDir());
        }

        // ── Coroutine-based update flow using UnityWebRequest ──

        private IEnumerator CheckAndStageCoroutine()
        {
            string branch = _config.EffectiveRef;

            List<ManifestItem> manifestItems = null;
            Dictionary<string, string> remoteShas = null;
            string remoteVersion = null;
            int remoteSchema = 0;
            _lastCheckUsedApi = false;

            string manifest2Url = RawUrl(branch, PatchRoot + "patch_manifest2.json");
            string manifest2Json = null;
            yield return DownloadText(manifest2Url, false, r => manifest2Json = r, true);

            bool fastPath = !string.IsNullOrEmpty(manifest2Json)
                && TryParseManifest2(manifest2Json, out manifestItems, out remoteShas, out remoteVersion, out remoteSchema);

            if (!fastPath)
            {
                string versionUrl = RawUrl(branch, "plugin_version.txt");
                string versionText = null;
                yield return DownloadText(versionUrl, false, r => versionText = r);

                if (string.IsNullOrEmpty(versionText))
                {
                    if (_config.Pinned)
                    {
                        PinnedRefMissing = true;
                        SetError("Pinned build " + (_config.PinnedVersion ?? _config.PinnedTag)
                            + " is not available on GitHub (tag '" + _config.PinnedTag
                            + "' is missing). Return to latest to resume updates.");
                    }
                    else
                    {
                        SetError("Could not fetch remote version");
                    }
                    yield break;
                }

                string[] versionLines = versionText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (versionLines.Length < 2)
                {
                    SetError("Invalid remote version format");
                    yield break;
                }

                remoteVersion = versionLines[0].Trim() + "." + versionLines[1].Trim();
            }

            string localVersion = PluginVersionInfo.Version;
            if (remoteVersion == localVersion)
            {
                Status = VpbUpdateStatus.UpToDate;
                StatusMessage = _config.Pinned
                    ? "Pinned to " + localVersion
                    : "Up to date (" + localVersion + ")";
                AvailableVersion = null;
                _config.LastCheckUtc = DateTime.UtcNow.ToString("o");
                _config.Save();
                FinishCoroutine();
                yield break;
            }

            AvailableVersion = remoteVersion;
            bool goingBack = VpbReleaseCatalog.IsOlderThan(_catalog, remoteVersion, localVersion);
            StatusMessage = (goingBack ? "Rolling back to " : "Update available: ") + remoteVersion;

            if (!fastPath)
            {
                string manifestUrl = RawUrl(branch, PatchRoot + "patch_manifest.json");
                string manifestJson = null;
                yield return DownloadText(manifestUrl, false, r => manifestJson = r);

                if (string.IsNullOrEmpty(manifestJson))
                {
                    SetError("Could not fetch patch manifest");
                    yield break;
                }

                manifestItems = ParseManifest(manifestJson);
                if (manifestItems == null || manifestItems.Count == 0)
                {
                    SetError("Empty or invalid manifest");
                    yield break;
                }

                string treeUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/git/trees/" + branch + "?recursive=1";
                string treeJson = null;
                _lastCheckUsedApi = true;
                yield return DownloadText(treeUrl, true, r => treeJson = r);

                remoteShas = ParseTreeShas(treeJson);
                if (remoteShas == null || remoteShas.Count == 0)
                {
                    SetError("Could not verify files for '" + branch + "' (GitHub rate limit or outage). "
                        + "Nothing was changed - try again later.");
                    yield break;
                }
            }

            if (goingBack)
            {
                string risk = DescribeSchemaRisk(remoteSchema);
                if (risk != null)
                    LogUtil.LogWarning("[VpbUpdater] Rolling back to " + remoteVersion + ": " + risk + ".");
            }

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

                string rawUrl = RawUrl(branch, PatchRoot + VpbUpdateManifest.EncodeGitHubRawPath(item.RelativePath));
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

            // 7. Write pending.json (and mirror for pre-subfolder VPB.Patcher.dll)
            Progress = 1f;
            WritePendingJson(stagingDir, remoteVersion, branch, pendingEntries);
            if (!VpbUpdateManifest.CopyStaging(stagingDir, GetLegacyStagingDir()))
                LogUtil.LogWarning("[VpbUpdater] Could not mirror staging to plugins/vpb_update_staging; old patcher may miss this update.");

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

        private IEnumerator DownloadText(string url, bool githubApi, Action<string> callback, bool expectMissing = false)
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
                    // Some of these are ordinary: a branch need not publish a release index, and
                    // patch_manifest2.json is absent from every ref older than it. Logging those
                    // as errors trains people to ignore the log.
                    if (expectMissing)
                        LogUtil.LogWarning("[VpbUpdater] GET " + url + " unavailable: " + req.error);
                    else
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

            string path = VpbUpdateManifest.PendingPath(stagingDir);
            File.WriteAllText(path, VPB.src.util.JsonSerializationUtil.Serialize(root, 1024));
        }

        private static string RawUrl(string gitRef, string repoRelativePath)
        {
            return "https://raw.githubusercontent.com/" + RepoOwner + "/" + RepoName + "/" + gitRef + "/" + repoRelativePath;
        }

        private static bool TryParseManifest2(
            string json,
            out List<ManifestItem> items,
            out Dictionary<string, string> shas,
            out string version,
            out int schema)
        {
            items = null;
            shas = null;
            version = null;
            schema = 0;

            try
            {
                var root = JSON.Parse(json);
                if (root == null) return false;
                if (root["ManifestVersion"].AsInt != 2) return false;

                version = root["Version"].Value;
                if (string.IsNullOrEmpty(version)) return false;
                schema = root["Schema"].AsInt;

                var arr = root["Files"] as JSONArray;
                if (arr == null || arr.Count == 0) return false;

                var parsedItems = new List<ManifestItem>(arr.Count);
                var parsedShas = new Dictionary<string, string>(arr.Count, StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < arr.Count; i++)
                {
                    var node = arr[i];
                    string rel = node["RelativePath"].Value;
                    if (string.IsNullOrEmpty(rel)) continue;

                    bool isDir = node["IsDirectory"].AsBool;
                    parsedItems.Add(new ManifestItem { RelativePath = rel, IsDirectory = isDir });
                    if (isDir) continue;

                    string sha = node["Sha1"].Value;
                    if (string.IsNullOrEmpty(sha))
                    {
                        LogUtil.LogWarning("[VpbUpdater] patch_manifest2.json has no Sha1 for '" + rel
                            + "'; refusing the fast path rather than fetching it unverified.");
                        return false;
                    }
                    parsedShas[(PatchRoot + rel).Replace('\\', '/')] = sha;
                }

                if (parsedItems.Count == 0) return false;

                items = parsedItems;
                shas = parsedShas;
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[VpbUpdater] Could not parse patch_manifest2.json: " + ex.Message);
                return false;
            }
        }

        public void FetchReleasesAsync()
        {
            if (_catalogState == VpbCatalogState.Fetching) return;
            _catalogState = VpbCatalogState.Fetching;
            _catalogBranch = string.IsNullOrEmpty(_config.Branch) ? VpbUpdateConfig.DefaultBranch : _config.Branch;
            try { OnStatusChanged?.Invoke(); } catch { }
            _host.StartCoroutine(FetchReleasesCoroutine(_catalogBranch));
        }

        private IEnumerator FetchReleasesCoroutine(string channel)
        {
            string url = RawUrl(channel, "releases/index.json");
            string json = null;
            yield return DownloadText(url, false, r => json = r, true);

            // A branch that has never published a release index is a normal state, not an error:
            // every branch looked like this before the index existed, and side branches may never
            // carry one. Updating still works there - only the rollback list is missing.
            if (string.IsNullOrEmpty(json))
            {
                FinishCatalogFetch(channel, null);
                yield break;
            }

            var catalog = VpbReleaseCatalog.Parse(json);
            FinishCatalogFetch(channel, catalog.IsEmpty ? null : catalog);
        }

        private void FinishCatalogFetch(string channel, VpbReleaseCatalog catalog)
        {
            // A branch switch during the request wins; this result describes the old branch.
            string current = string.IsNullOrEmpty(_config.Branch) ? VpbUpdateConfig.DefaultBranch : _config.Branch;
            if (!string.Equals(channel, current, StringComparison.Ordinal)) return;

            _catalog = catalog;
            _catalogState = catalog == null ? VpbCatalogState.Unavailable : VpbCatalogState.Ready;
            if (catalog == null)
                LogUtil.LogWarning("[VpbUpdater] No release index published on '" + channel
                    + "'; version rollback is unavailable there. Updates are unaffected.");
            try { OnStatusChanged?.Invoke(); } catch { }
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
            string stagingDir = GetStagingDir();
            string legacyStagingDir = GetLegacyStagingDir();
            string pendingPath = GetPendingPath();
            bool cleared = true;

            try
            {
                if (File.Exists(pendingPath))
                {
                    try
                    {
                        File.SetAttributes(pendingPath, FileAttributes.Normal);
                        File.Delete(pendingPath);
                    }
                    catch (Exception ex)
                    {
                        cleared = false;
                        LogUtil.LogWarning("[VpbUpdater] Could not delete pending.json: " + ex.Message);
                    }
                }

                if (Directory.Exists(stagingDir))
                {
                    if (!TryDeleteDirectoryRecursive(stagingDir))
                        cleared = false;
                }

                if (Directory.Exists(legacyStagingDir))
                {
                    if (!TryDeleteDirectoryRecursive(legacyStagingDir))
                        cleared = false;
                }
            }
            catch (Exception ex)
            {
                cleared = false;
                LogUtil.LogError("[VpbUpdater] ClearStagedUpdate failed: " + ex.Message);
            }

            HasPendingUpdate = HasAnyPending();
            _config.LastStagedVersion = "";
            _config.Save();

            if (HasPendingUpdate || !cleared)
            {
                Status = VpbUpdateStatus.Error;
                StatusMessage = "Could not clear staged update (files may be in use). Close other apps and retry.";
                try { OnStatusChanged?.Invoke(); } catch { }
                return;
            }

            Status = VpbUpdateStatus.Idle;
            StatusMessage = "";
            AvailableVersion = null;
            Progress = 0f;
            try { OnStatusChanged?.Invoke(); } catch { }
        }

        private static bool TryDeleteDirectoryRecursive(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return true;

            bool ok = true;
            try
            {
                foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch
                    {
                        ok = false;
                    }
                }

                string[] subDirs = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories);
                Array.Sort(subDirs, (a, b) => b.Length.CompareTo(a.Length));
                for (int i = 0; i < subDirs.Length; i++)
                {
                    try { Directory.Delete(subDirs[i], false); }
                    catch { ok = false; }
                }

                Directory.Delete(dir, false);
            }
            catch
            {
                ok = false;
            }

            return ok && !Directory.Exists(dir);
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
