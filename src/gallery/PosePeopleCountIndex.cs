using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;

namespace VPB
{
    public class PosePeopleCountIndex
    {
        // Static-init guarantees a single instance; lazy-on-first-access would race off-thread callers.
        private static readonly PosePeopleCountIndex _instance = new PosePeopleCountIndex();
        public static PosePeopleCountIndex Instance => _instance;

        // Dictionary<,> is not safe for concurrent read+write, and foreach in Save throws on concurrent mutation. Lock guards all access in case any Set() caller runs off-thread.
        private readonly object _lock = new object();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool _loaded;
        private bool _dirty;
        private int _dirtyWrites;

        private string IndexPath => Path.Combine(GlobalInfo.PluginInfoDirectory, "PosePeopleCountIndex.json");

        private PosePeopleCountIndex()
        {
        }

        private void EnsureLoadedLocked()
        {
            if (_loaded) return;
            _loaded = true;
            _counts.Clear();

            if (!File.Exists(IndexPath)) return;

            try
            {
                string json = File.ReadAllText(IndexPath);
                JSONClass root = JSON.Parse(json).AsObject;
                if (root == null) return;

                foreach (string key in root.Keys)
                {
                    JSONNode node = root[key];
                    if (node == null) continue;

                    int parsed;
                    if (int.TryParse(node.Value, out parsed) && parsed > 0)
                    {
                        _counts[key] = parsed;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Failed to load PosePeopleCountIndex: " + ex);
            }
        }

        public bool TryGet(string uid, out int peopleCount)
        {
            peopleCount = 0;
            if (string.IsNullOrEmpty(uid)) return false;
            lock (_lock)
            {
                EnsureLoadedLocked();
                return _counts.TryGetValue(uid, out peopleCount) && peopleCount > 0;
            }
        }

        public void Set(string uid, int peopleCount)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (peopleCount <= 0) return;
            bool needsFlush = false;
            lock (_lock)
            {
                EnsureLoadedLocked();

                int existing;
                if (_counts.TryGetValue(uid, out existing) && existing == peopleCount) return;

                _counts[uid] = peopleCount;
                _dirty = true;
                _dirtyWrites++;

                // Avoid writing to disk on every single pose; batch a bit.
                if (_dirtyWrites >= 200) needsFlush = true;
            }
            if (needsFlush) Save();
        }

        public void Save()
        {
            string serialized;
            int snapshotDirtyWrites;
            lock (_lock)
            {
                if (!_dirty) return;
                try
                {
                    JSONClass root = new JSONClass();
                    foreach (var kvp in _counts)
                    {
                        root[kvp.Key] = kvp.Value.ToString();
                    }
                    serialized = VPB.src.util.JsonSerializationUtil.Serialize(root, 8192);
                    snapshotDirtyWrites = _dirtyWrites;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Failed to serialize PosePeopleCountIndex: " + ex);
                    return;
                }
            }

            try
            {
                GlobalInfo.EnsurePluginDataInitialized();
                File.WriteAllText(IndexPath, serialized);
                // Clear dirty flags only if the write succeeded AND no new Set() arrived since the snapshot.
                lock (_lock)
                {
                    if (_dirty && _dirtyWrites == snapshotDirtyWrites)
                    {
                        _dirty = false;
                        _dirtyWrites = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Failed to write PosePeopleCountIndex: " + ex);
            }
        }
    }
}
