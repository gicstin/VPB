using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public class DependencyWhitelistManager
    {
        private static DependencyWhitelistManager _instance;
        public static DependencyWhitelistManager Instance
        {
            get
            {
                if (_instance == null) _instance = new DependencyWhitelistManager();
                return _instance;
            }
        }

        public static void Reload()
        {
            _instance = null;
        }

        private readonly JsonUidSetStore _store;

        public DependencyWhitelistManager()
        {
            try
            {
                _store = new JsonUidSetStore(
                    "dependency_whitelist.json",
                    "DependencyWhitelistManager",
                    StringComparer.OrdinalIgnoreCase,
                    true);
                SyncToConfig();
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] DependencyWhitelistManager initialization failed: " + ex.Message);
            }
        }

        public void Save()
        {
            if (_store != null) _store.Save();
        }

        public bool IsWhitelisted(string packageGroup)
        {
            return _store != null && _store.Contains(packageGroup);
        }

        public void SetWhitelisted(string packageGroup, bool whitelisted, bool save = true)
        {
            if (_store == null) return;
            if (!_store.Set(packageGroup, whitelisted, save)) return;
            SyncToConfig();
        }

        public HashSet<string> GetWhitelistedPackageGroups()
        {
            if (_store == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return _store.Copy();
        }

        private void SyncToConfig()
        {
            try
            {
                if (Settings.Instance == null || Settings.Instance.ForceLatestDependencyIgnorePackageGroups == null) return;
                if (_store == null) return;

                HashSet<string> copy = _store.Copy();
                string joined = string.Join(", ", new List<string>(copy).ToArray());
                Settings.Instance.ForceLatestDependencyIgnorePackageGroups.Value = joined;
            }
            catch
            {
            }
        }
    }
}
