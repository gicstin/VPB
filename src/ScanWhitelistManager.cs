using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valve.Newtonsoft.Json;

namespace VPB
{
    /// <summary>
    /// Manages the VaM scan whitelist: a set of AddonPackages/ subfolders and per-UID overrides
    /// that VaM's native scanner is restricted to on startup. Packages outside the whitelist
    /// remain physically in AddonPackages/ and are accessible via on-demand loading but are
    /// not proactively scanned by VaM, improving startup performance.
    /// </summary>
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
        }

        private string jsonPath;
        private readonly object lockObj = new object();
        private bool hasLoadedSuccessfully = false;

        private bool _enabled = false;
        // Normalized folder paths (forward slashes, no trailing slash), e.g. "AddonPackages/FavoriteCreator"
        private readonly List<string> _whitelistedFolders = new List<string>();
        // Per-package UID overrides: packages included even if their folder is not whitelisted
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
                string baseDir = Directory.GetCurrentDirectory();
                string saveDir = Path.Combine(Path.Combine(Path.Combine(baseDir, "Saves"), "PluginData"), "VPB");

                if (!Directory.Exists(saveDir))
                    Directory.CreateDirectory(saveDir);

                jsonPath = Path.Combine(saveDir, "scan_whitelist.json");
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

            lock (lockObj)
            {
                _whitelistedFolders.Clear();
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

                hasLoadedSuccessfully = true; // fresh start
                LogUtil.Log("[VPB ScanWhitelist] No config found — disabled, passthrough mode");
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
                LogUtil.LogWarning("[VPB ScanWhitelist] WARNING: enabled but empty — all AddonPackages will be excluded from VaM startup scan!");
        }

        private bool TryLoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                string json;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                    json = sr.ReadToEnd();

                if (string.IsNullOrEmpty(json) || json.Trim().Length < 2) return false;

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
                Debug.LogError("[VPB] ScanWhitelistManager: Error parsing " + Path.GetFileName(path) + ": " + ex.Message);
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
                        Enabled = _enabled,
                        WhitelistedFolders = new List<string>(_whitelistedFolders),
                        IncludedPackageUids = new List<string>(_includedPackageUids)
                    };

                    string json;
                    lock (LogUtil.JsonLock)
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);

                    if (string.IsNullOrEmpty(json)) return;

                    string tmpPath = jsonPath + ".tmp";
                    File.WriteAllText(tmpPath, json);

                    if (!File.Exists(tmpPath) || new FileInfo(tmpPath).Length < 2) return;

                    string backupPath = jsonPath + ".bak";
                    if (File.Exists(jsonPath))
                    {
                        if (new FileInfo(jsonPath).Length > 2)
                        {
                            try
                            {
                                if (File.Exists(backupPath)) File.Delete(backupPath);
                                File.Move(jsonPath, backupPath);
                            }
                            catch
                            {
                                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                            }
                        }
                        else
                        {
                            File.Delete(jsonPath);
                        }
                    }

                    File.Move(tmpPath, jsonPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VPB] ScanWhitelistManager: Error saving scan_whitelist.json: " + ex.Message);
                }
            }
        }

        // --- Query API ---

        /// <summary>
        /// Returns true if the given .var file path should be scanned by VaM.
        /// When the feature is disabled, always returns true (passthrough).
        /// </summary>
        public bool IsPathWhitelisted(string varFilePath)
        {
            if (string.IsNullOrEmpty(varFilePath)) return true;
            lock (lockObj)
            {
                if (!_enabled) return true;
                string norm = varFilePath.Replace('\\', '/');
                // AllPackages is always passed through (separate system)
                if (norm.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var folder in _whitelistedFolders)
                {
                    // folder = "AddonPackages/Creator", norm must start with "AddonPackages/Creator/"
                    // or exactly equal "AddonPackages/Creator/pkg.var" (file directly in folder)
                    if (norm.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                        || norm.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Returns true if the package UID is whitelisted via a per-UID override
        /// (included even though its folder is not whitelisted).
        /// </summary>
        public bool IsUidOverrideIncluded(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                return _includedPackageUids.Contains(uid) || _temporaryIncludedPackageUids.Contains(uid);
            }
        }

        /// <summary>
        /// Returns true if the package is effectively excluded from VaM's scan
        /// (feature enabled, path not whitelisted, no UID override).
        /// </summary>
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
            foreach (var folder in _whitelistedFolders)
            {
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

        // --- Mutation API ---

        public void SetEnabled(bool enabled)
        {
            lock (lockObj) { _enabled = enabled; }
        }

        public bool AddFolder(string folderPath)
        {
            string normalized = NormalizeFolder(folderPath);
            if (string.IsNullOrEmpty(normalized)) return false;
            lock (lockObj)
            {
                if (_whitelistedFolders.Contains(normalized)) return false;
                _whitelistedFolders.Add(normalized);
                return true;
            }
        }

        public bool RemoveFolder(string folderPath)
        {
            string normalized = NormalizeFolder(folderPath);
            if (string.IsNullOrEmpty(normalized)) return false;
            lock (lockObj)
            {
                return _whitelistedFolders.Remove(normalized);
            }
        }

        public bool AddUidOverride(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                return _includedPackageUids.Add(uid.Trim());
            }
        }

        public bool RemoveUidOverride(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            lock (lockObj)
            {
                return _includedPackageUids.Remove(uid.Trim());
            }
        }

        /// <summary>
        /// Adds runtime-only UID overrides for this session and returns only newly-added UIDs.
        /// Permanent overrides are left untouched and not included in the returned list.
        /// </summary>
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
                    _temporaryIncludedPackageUids.Remove(uid);
                }
            }
        }

        /// <summary>Whether the whitelist is enabled but has no folders or UID overrides.</summary>
        public bool IsEnabledButEmpty()
        {
            lock (lockObj)
            {
                return _enabled && _whitelistedFolders.Count == 0
                    && _includedPackageUids.Count == 0
                    && _temporaryIncludedPackageUids.Count == 0;
            }
        }

        // --- Gallery helpers ---

        /// <summary>
        /// Returns true if this file entry should show the gallery "W" badge.
        /// Current gallery behavior shows this only for packages effectively included by VaM's scan whitelist
        /// (whitelisted folder or UID override), and hides it for VPB-only scan-excluded packages.
        /// </summary>
        public static bool IsScanExcludedBadgeVisible(FileEntry entry)
        {
            if (entry == null) return false;
            try
            {
                if (!Instance.IsEnabled) return false;

                VarPackage pkg = null;
                if (entry is VarFileEntry vfe && vfe.Package != null) pkg = vfe.Package;
                else if (entry is SystemFileEntry sfe && sfe.isVar && sfe.package != null) pkg = sfe.package;
                else if (entry is PackageListEntry ple && ple.Package != null) pkg = ple.Package;

                if (pkg == null) return false;
                return !Instance.IsPackageScanExcluded(pkg.Uid, pkg.Path ?? "");
            }
            catch { return false; }
        }

        // --- Helpers ---

        public static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            return folder.Replace('\\', '/').TrimEnd('/').Trim();
        }

        /// <summary>
        /// Given a .var file path, returns the normalized parent folder path suitable for use as a whitelist entry.
        /// e.g. "AddonPackages\Creator\Sub\pkg.var" → "AddonPackages/Creator/Sub"
        /// </summary>
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
            [JsonProperty("enabled")]
            public bool Enabled;
            [JsonProperty("whitelistedFolders")]
            public List<string> WhitelistedFolders;
            [JsonProperty("includedPackageUids")]
            public List<string> IncludedPackageUids;
        }
    }
}
