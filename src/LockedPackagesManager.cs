using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public class LockedPackagesManager
    {
        private static LockedPackagesManager _instance;
        public static LockedPackagesManager Instance
        {
            get
            {
                if (_instance == null) _instance = new LockedPackagesManager();
                return _instance;
            }
        }

        public static void Reload()
        {
            _instance = null;
        }

        private readonly JsonUidSetStore _store;

        public LockedPackagesManager()
        {
            try
            {
                _store = new JsonUidSetStore("locked_packages.json", "LockedPackagesManager");
            }
            catch (Exception ex)
            {
                Debug.LogError("[VPB] LockedPackagesManager initialization failed: " + ex.Message);
            }
        }

        public void Save()
        {
            if (_store != null) _store.Save();
        }

        public bool IsLocked(string uid)
        {
            return _store != null && _store.Contains(uid);
        }

        public void SetLocked(string uid, bool locked, bool save = true)
        {
            if (_store != null) _store.Set(uid, locked, save);
        }

        public HashSet<string> GetLockedPackages()
        {
            if (_store == null) return new HashSet<string>();
            return _store.Copy();
        }
    }
}
