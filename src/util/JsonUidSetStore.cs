using System;
using System.Collections.Generic;
using System.IO;
using Valve.Newtonsoft.Json;

namespace VPB
{
    internal sealed class JsonUidSetStore
    {
        private readonly string _fileName;
        private readonly string _logTag;
        private readonly IEqualityComparer<string> _comparer;
        private readonly bool _trim;
        private readonly HashSet<string> _items;
        private readonly object _lockObj = new object();
        private string _jsonPath;
        private bool _hasLoadedSuccessfully;

        internal JsonUidSetStore(string fileName, string logTag)
            : this(fileName, logTag, null, false)
        {
        }

        internal JsonUidSetStore(string fileName, string logTag, IEqualityComparer<string> comparer)
            : this(fileName, logTag, comparer, false)
        {
        }

        internal JsonUidSetStore(string fileName, string logTag, IEqualityComparer<string> comparer, bool trim)
        {
            _fileName = fileName;
            _logTag = logTag;
            _comparer = comparer;
            _trim = trim;
            _items = comparer != null ? new HashSet<string>(comparer) : new HashSet<string>();
            try
            {
                _jsonPath = GlobalInfo.PluginFile(fileName);
                Load();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] " + _logTag + " initialization failed: " + ex.Message);
            }
        }

        internal bool Contains(string uid)
        {
            uid = Normalize(uid);
            if (string.IsNullOrEmpty(uid)) return false;
            lock (_lockObj)
            {
                return _items.Contains(uid);
            }
        }

        internal bool Set(string uid, bool on, bool save)
        {
            uid = Normalize(uid);
            if (string.IsNullOrEmpty(uid)) return false;

            lock (_lockObj)
            {
                bool current = _items.Contains(uid);
                if (current == on) return false;
                if (on) _items.Add(uid);
                else _items.Remove(uid);
            }

            if (save) Save();
            return true;
        }

        internal HashSet<string> Copy()
        {
            lock (_lockObj)
            {
                if (_comparer != null) return new HashSet<string>(_items, _comparer);
                return new HashSet<string>(_items);
            }
        }

        internal void Save()
        {
            if (string.IsNullOrEmpty(_jsonPath) || !_hasLoadedSuccessfully) return;

            lock (_lockObj)
            {
                try
                {
                    var data = new List<string>(_items);
                    string json;
                    lock (LogUtil.JsonLock)
                    {
                        json = JsonConvert.SerializeObject(data, Formatting.Indented);
                    }
                    if (string.IsNullOrEmpty(json)) return;
                    VpbAtomicTextFile.TryWriteWithBackup(_jsonPath, json);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] " + _logTag + ": Error saving " + _fileName + ": " + ex.Message);
                }
            }
        }

        private string Normalize(string uid)
        {
            if (string.IsNullOrEmpty(uid) || !_trim) return uid;
            return uid.Trim();
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(_jsonPath)) return;

            lock (_lockObj)
            {
                _items.Clear();
                string backupPath = _jsonPath + ".bak";
                bool mainExists = File.Exists(_jsonPath);
                bool backupExists = File.Exists(backupPath);

                if (mainExists && TryLoadFile(_jsonPath))
                {
                    _hasLoadedSuccessfully = true;
                    return;
                }

                if (backupExists && TryLoadFile(backupPath))
                {
                    _hasLoadedSuccessfully = true;
                    try { File.Copy(backupPath, _jsonPath, true); }
                    catch { }
                    return;
                }

                _items.Clear();
                _hasLoadedSuccessfully = true;
            }
        }

        private bool TryLoadFile(string path)
        {
            try
            {
                string json;
                if (!VpbAtomicTextFile.TryReadSharedText(path, out json)) return false;

                List<string> data;
                lock (LogUtil.JsonLock)
                {
                    data = JsonConvert.DeserializeObject<List<string>>(json);
                }
                if (data == null) return false;

                foreach (var item in data)
                {
                    string key = Normalize(item);
                    if (!string.IsNullOrEmpty(key))
                        _items.Add(key);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Error parsing " + Path.GetFileName(path) + ": " + ex.Message);
            }
            return false;
        }
    }
}
