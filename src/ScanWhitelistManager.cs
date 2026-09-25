using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valve.Newtonsoft.Json;

namespace VPB
{
    /// <summary>Manages VaM scan whitelist (folders + per-UID overrides); others load on demand only.</summary>
    public class ScanWhitelistManager
    {
        private static ScanWhitelistManager _instance;
        public static ScanWhitelistManager Instance
        {
            get
            {
                if (_instance == null) _instance = new ScanWhitelistManager();
                return _instance;
            }
        }

        public static void Reload()
        {
            _instance = null;
            BumpStateVersion();
        }

        private static int s_StateVersion;

        public static int StateVersion
        {
            get { return System.Threading.Interlocked.CompareExchange(ref s_StateVersion, 0, 0); }
        }

        private static void BumpStateVersion()
        {
            System.Threading.Interlocked.Increment(ref s_StateVersion);
        }

        private string jsonPath;
        private readonly object lockObj = new object();
        private bool hasLoadedSuccessfully = false;

        private bool _enabled = false;
        private readonly List<string> _whitelistedFolders = new List<string>();
        private readonly Dictionary<string, List<string>> _whitelistedFoldersByBucket =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _includedPackageUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Runtime-only UID overrides for temporary scene-load allow-listing (not persisted).
        private readonly HashSet<string> _temporaryIncludedPackageUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsEnabled
        {
            get { lock (lockObj) { return _enabled; } }
        }

        public List<string> GetWhitelistedFolders()
        {
            lock (lockObj) { return new List<string>(_whitelistedFolders); }
        }

        public HashSet<string> GetIncludedPackageUids()
        {
            lock (lockObj) { return new HashSet<string>(_includedPackageUids, StringComparer.OrdinalIgnoreCase); }
        }

        public ScanWhitelistManager()
        {
            try
            {
                jsonPath = GlobalInfo.PluginFile("scan_whitelist.json");
                Load();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] ScanWhitelistManager initialization failed: " + ex.Message);
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;

            bool createBlankConfig = false;
            BumpStateVersion();
            lock (lockObj)
            {
                _whitelistedFolders.Clear();
                _whitelistedFoldersByBucket.Clear();
                _includedPackageUids.Clear();
                _temporaryIncludedPackageUids.Clear();
                _enabled = false;

                string backupPath = jsonPath + ".bak";
                bool mainExists = File.Exists(jsonPath);
                bool backupExists = File.Exists(backupPath);

                if (mainExists && TryLoadFile(jsonPath))
                {
                    hasLoadedSuccessfully = true;
                    LogLoadedState("main");
                    return;
                }

                if (backupExists && TryLoadFile(backupPath))
                {
                    hasLoadedSuccessfully = true;
                    try { File.Copy(backupPath, jsonPath, true); } catch { }
                    LogLoadedState("backup");
                    return;
                }

                hasLoadedSuccessfully = true;
                if (!mainExists && !backupExists)
                {
                    // First run: fail-closed empty whitelist (on-demand still loads packages).
                    _enabled = true;
                    createBlankConfig = true;
                    LogUtil.Log("[VPB ScanWhitelist] No config found — creating blank enabled whitelist (fail-closed)");
                }
                else
                {
                    _enabled = false;
                    createBlankConfig = true;
                    LogUtil.Log("[VPB ScanWhitelist] Config unreadable — rewriting disabled whitelist");
                }
            }

            if (createBlankConfig)
            {
                try { Save(); } catch { }
                LogUtil.Log("[VPB ScanWhitelist] Wrote scan_whitelist.json enabled=" + _enabled
                    + " folders=0 uid_overrides=0");
            }
        }

        private void LogLoadedState(string source)
        {
            string folderList = _whitelistedFolders.Count == 0 ? "(none)"
                : string.Join(", ", _whitelistedFolders.ToArray());
            LogUtil.Log(string.Format(
                "[VPB ScanWhitelist] Loaded ({0}): enabled={1} | folders={2} [{3}] | uid_overrides={4}",
                source, _enabled, _whitelistedFolders.Count, folderList, _includedPackageUids.Count));
            if (_enabled && _whitelistedFolders.Count == 0 && _includedPackageUids.Count == 0)
                LogUtil.Log("[VPB ScanWhitelist] enabled but empty — AddonPackages excluded from VaM startup scan (settings shows the warning)");
        }

        private bool TryLoadFile(string path)
        {
            try
            {
                string json;
                if (!VpbAtomicTextFile.TryReadSharedText(path, out json)) return false;

                ScanWhitelistData data;
                lock (LogUtil.JsonLock)
                    data = JsonConvert.DeserializeObject<ScanWhitelistData>(json);

                if (data == null) return false;

                _enabled = data.Enabled;

                if (data.WhitelistedFolders != null)
                {
                    foreach (var f in data.WhitelistedFolders)
                    {
                        string normalized = NormalizeFolder(f);
                        if (!string.IsNullOrEmpty(normalized) && !_whitelistedFolders.Contains(normalized))
                            _whitelistedFolders.Add(normalized);
                    }
                }
                RebuildWhitelistBucketsLocked();

                if (data.IncludedPackageUids != null)
                {
                    foreach (var uid in data.IncludedPackageUids)
                        if (!string.IsNullOrEmpty(uid))
                            _includedPackageUids.Add(uid.Trim());
                }

                return true;
            }
            catch (Exception ex)
            {
                LogUtil.Log("[VPB] ScanWhitelistManager: skip unreadable " + Path.GetFileName(path) + ": " + ex.Message);
            }
            return false;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(jsonPath)) return;
            if (!hasLoadedSuccessfully) return;

            lock (lockObj)
            {
                try
                {
                    var data = new ScanWhitelistData
                    {
                        SchemaVersion = 1,
                        Enabled = _enabled,
                        WhitelistedFolders = new List<string>(_whitelistedFolders),
                        IncludedPackageUids = new List<string>(_includedPackageUids)
                    };

                    string json;
                    lock (LogUtil.JsonLock)
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);

                    if (string.IsNullOrEmpty(json)) return;
                    VpbAtomicTextFile.TryWriteWithBackup(jsonPath, json);
                }
                catch (Exception ex)
                {
                    VPB.src.util.VPBLogger.Files.LogError("[VPB] ScanWhitelistManager: Error saving scan_whitelist.json: " + ex.Message, false);
                }
            }
        }

        public bool IsPathWhitelisted(string varFilePath)
        {
            if (string.IsNullOrEmpty(varFilePath)) return true;
            lock (lockObj)
            {
                if (!_enabled) return true;
                string norm = varFilePath.Replace('\\', '/');
                if (norm.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) return true;
                List<string> candidates = GetWhitelistBucketCandidatesLocked(norm);
                for (int i = 0; i < candidates.Count; i++)
                {
                    string folder = candidates[i];
                    // folder = "AddonPackages/Creator", norm must start with "AddonPackages/Creator/" or exactly equal "AddonPackages/Creator/pkg.var"
                    if (norm.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                        || norm.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }

        public bool IsUidOverrideIncluded(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                return _includedPackageUids.Contains(uid) || _temporaryIncludedPackageUids.Contains(uid);
            }
        }

        /// <summary>Returns true only for persistent UID overrides stored in scan_whitelist.json.</summary>
        public bool IsUidOverridePersisted(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                return _includedPackageUids.Contains(uid);
            }
        }

        public bool IsPackageScanExcluded(string uid, string varFilePath)
        {
            lock (lockObj)
            {
                if (!_enabled) return false;
                if (IsUidOverrideIncludedLocked(uid)) return false;
                return !IsPathWhitelistedLocked(varFilePath);
            }
        }

        // Called with lockObj already held
        private bool IsPathWhitelistedLocked(string varFilePath)
        {
            if (string.IsNullOrEmpty(varFilePath)) return true;
            string norm = varFilePath.Replace('\\', '/');
            if (norm.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) return true;
            List<string> candidates = GetWhitelistBucketCandidatesLocked(norm);
            for (int i = 0; i < candidates.Count; i++)
            {
                string folder = candidates[i];
                if (norm.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                    || norm.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // Called with lockObj already held
        private bool IsUidOverrideIncludedLocked(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            return _includedPackageUids.Contains(uid) || _temporaryIncludedPackageUids.Contains(uid);
        }

        public void SetEnabled(bool enabled)
        {
            lock (lockObj)
            {
                if (_enabled != enabled) BumpStateVersion();
                _enabled = enabled;
            }
        }

        public bool AddFolder(string folderPath)
        {
            string normalized = NormalizeFolder(folderPath);
            if (string.IsNullOrEmpty(normalized)) return false;
            lock (lockObj)
            {
                if (_whitelistedFolders.Contains(normalized)) return false;
                _whitelistedFolders.Add(normalized);
                AddFolderToBucketsLocked(normalized);
                BumpStateVersion();
                return true;
            }
        }

        public bool RemoveFolder(string folderPath)
        {
            string normalized = NormalizeFolder(folderPath);
            if (string.IsNullOrEmpty(normalized)) return false;
            lock (lockObj)
            {
                bool removed = _whitelistedFolders.Remove(normalized);
                if (removed)
                {
                    RemoveFolderFromBucketsLocked(normalized);
                    BumpStateVersion();
                }
                return removed;
            }
        }

        public bool AddUidOverride(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                bool added = _includedPackageUids.Add(uid.Trim());
                if (added) BumpStateVersion();
                return added;
            }
        }

        public bool RemoveUidOverride(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                bool removed = _includedPackageUids.Remove(uid.Trim());
                if (removed) BumpStateVersion();
                return removed;
            }
        }

        /// <summary>Adds runtime-only UID overrides for this session and returns only newly-added UIDs.</summary>
        public List<string> AddTemporaryUidOverrides(IEnumerable<string> uids)
        {
            var added = new List<string>();
            if (uids == null) return added;

            lock (lockObj)
            {
                foreach (var uidRaw in uids)
                {
                    string uid = string.IsNullOrEmpty(uidRaw) ? null : uidRaw.Trim();
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (_includedPackageUids.Contains(uid)) continue;
                    if (_temporaryIncludedPackageUids.Add(uid))
                        added.Add(uid);
                }
                if (added.Count > 0) BumpStateVersion();
            }

            return added;
        }

        /// <summary>Removes runtime-only UID overrides previously added at runtime.</summary>
        public void RemoveTemporaryUidOverrides(IEnumerable<string> uids)
        {
            if (uids == null) return;

            lock (lockObj)
            {
                foreach (var uidRaw in uids)
                {
                    string uid = string.IsNullOrEmpty(uidRaw) ? null : uidRaw.Trim();
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (_temporaryIncludedPackageUids.Remove(uid)) BumpStateVersion();
                }
            }
        }

        public bool IsEnabledButEmpty()
        {
            lock (lockObj)
            {
                return _enabled && _whitelistedFolders.Count == 0
                    && _includedPackageUids.Count == 0
                    && _temporaryIncludedPackageUids.Count == 0;
            }
        }

        /// <summary>True when UID is included only via runtime temporary override (not persisted JSON).</summary>
        public bool IsUidTemporaryOverrideOnly(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                if (!_enabled) return false;
                string key = uid.Trim();
                return _temporaryIncludedPackageUids.Contains(key) && !_includedPackageUids.Contains(key);
            }
        }

        private static VarPackage TryResolveGalleryVarPackage(FileEntry entry)
        {
            if (entry == null) return null;
            if (entry is VarFileEntry vfe && vfe.Package != null) return vfe.Package;
            if (entry is SystemFileEntry sfe && sfe.isVar && sfe.package != null) return sfe.package;
            if (entry is PackageListEntry ple && ple.Package != null) return ple.Package;
            return null;
        }

        public enum GalleryScanWlBadgeKind : byte
        {
            None = 0,
            Persistent = 1,
            /// <summary>Session-only temporary UID override (not folder / not persisted).</summary>
            Temporary = 2
        }

        public static GalleryScanWlBadgeKind GetGalleryScanWhitelistBadgeKind(FileEntry entry)
        {
            if (entry == null) return GalleryScanWlBadgeKind.None;
            try
            {
                if (!Instance.IsEnabled) return GalleryScanWlBadgeKind.None;
                VarPackage pkg = TryResolveGalleryVarPackage(entry);
                if (pkg == null) return GalleryScanWlBadgeKind.None;

                if (Instance.IsPathWhitelisted(pkg.Path ?? "") || Instance.IsUidOverridePersisted(pkg.Uid))
                    return GalleryScanWlBadgeKind.Persistent;
                if (Instance.IsUidTemporaryOverrideOnly(pkg.Uid))
                    return GalleryScanWlBadgeKind.Temporary;
                return GalleryScanWlBadgeKind.None;
            }
            catch { return GalleryScanWlBadgeKind.None; }
        }

        /// <summary>Folder-whitelisted or persisted UID override (not session-only temporary).</summary>
        public static bool IsGalleryPersistentScanWhitelistBorderVisible(FileEntry entry)
        {
            return GetGalleryScanWhitelistBadgeKind(entry) == GalleryScanWlBadgeKind.Persistent;
        }

        /// <summary>Session-only temporary UID override (excludes folder/persisted inclusion).</summary>
        public static bool IsGalleryTemporaryScanWhitelistBorderVisible(FileEntry entry)
        {
            return GetGalleryScanWhitelistBadgeKind(entry) == GalleryScanWlBadgeKind.Temporary;
        }

        public static bool IsScanExcludedBadgeVisible(FileEntry entry)
        {
            return GetGalleryScanWhitelistBadgeKind(entry) != GalleryScanWlBadgeKind.None;
        }

        public static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            return folder.Replace('\\', '/').TrimEnd('/').Trim();
        }

        private static string GetWhitelistBucketKey(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath)) return "";
            string p = normalizedPath.Replace('\\', '/').Trim('/');
            if (p.Length == 0) return "";

            const string addonPrefix = "AddonPackages/";
            if (p.StartsWith(addonPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string rest = p.Substring(addonPrefix.Length);
                if (rest.Length == 0) return "AddonPackages";
                int slash = rest.IndexOf('/');
                if (slash <= 0) return addonPrefix + rest;
                return addonPrefix + rest.Substring(0, slash);
            }

            int firstSlash = p.IndexOf('/');
            if (firstSlash <= 0) return p;
            return p.Substring(0, firstSlash);
        }

        private void RebuildWhitelistBucketsLocked()
        {
            _whitelistedFoldersByBucket.Clear();
            for (int i = 0; i < _whitelistedFolders.Count; i++)
                AddFolderToBucketsLocked(_whitelistedFolders[i]);
        }

        private void AddFolderToBucketsLocked(string normalizedFolder)
        {
            if (string.IsNullOrEmpty(normalizedFolder)) return;
            string key = GetWhitelistBucketKey(normalizedFolder);
            if (!_whitelistedFoldersByBucket.TryGetValue(key, out var list))
            {
                list = new List<string>();
                _whitelistedFoldersByBucket[key] = list;
            }
            if (!list.Contains(normalizedFolder))
                list.Add(normalizedFolder);
        }

        private void RemoveFolderFromBucketsLocked(string normalizedFolder)
        {
            if (string.IsNullOrEmpty(normalizedFolder)) return;
            string key = GetWhitelistBucketKey(normalizedFolder);
            if (!_whitelistedFoldersByBucket.TryGetValue(key, out var list)) return;
            list.Remove(normalizedFolder);
            if (list.Count == 0)
                _whitelistedFoldersByBucket.Remove(key);
        }

        private List<string> GetWhitelistBucketCandidatesLocked(string normalizedPath)
        {
            if (_whitelistedFolders.Count <= 1) return _whitelistedFolders;
            string key = GetWhitelistBucketKey(normalizedPath);
            if (_whitelistedFoldersByBucket.TryGetValue(key, out var list) && list != null && list.Count > 0)
                return list;
            return _whitelistedFolders;
        }

        public static string FolderFromVarPath(string varFilePath)
        {
            if (string.IsNullOrEmpty(varFilePath)) return null;
            string dir = Path.GetDirectoryName(varFilePath);
            if (string.IsNullOrEmpty(dir)) return null;
            return NormalizeFolder(dir);
        }

        [System.Serializable]
        private class ScanWhitelistData
        {
            /// <summary>Written by multiplayer. Ignored here; extra JSON fields must not fail deserialize.</summary>
            [JsonProperty("schemaVersion")]
            public int SchemaVersion;
            [JsonProperty("enabled")]
            public bool Enabled;
            [JsonProperty("whitelistedFolders")]
            public List<string> WhitelistedFolders;
            [JsonProperty("includedPackageUids")]
            public List<string> IncludedPackageUids;
        }
    }
}
