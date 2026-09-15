using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public class AutoLoadPackagesManager
    {
        private static AutoLoadPackagesManager _instance;
        public static AutoLoadPackagesManager Instance
        {
            get
            {
                if (_instance == null) _instance = new AutoLoadPackagesManager();
                return _instance;
            }
        }

        public static void Reload()
        {
            _instance = null;
        }

        private readonly JsonUidSetStore _store;

        public AutoLoadPackagesManager()
        {
            try
            {
                _store = new JsonUidSetStore("autoload_packages.json", "AutoLoadPackagesManager");
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] AutoLoadPackagesManager initialization failed: " + ex.Message);
            }
        }

        public void Save()
        {
            if (_store != null) _store.Save();
        }

        public bool IsAutoLoad(string uid)
        {
            return _store != null && _store.Contains(uid);
        }

        public void SetAutoLoad(string uid, bool autoLoad, bool save = true)
        {
            if (_store != null) _store.Set(uid, autoLoad, save);
        }

        public HashSet<string> GetAutoLoadPackages()
        {
            if (_store == null) return new HashSet<string>();
            return _store.Copy();
        }
    }
}
