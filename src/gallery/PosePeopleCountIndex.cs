using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;

namespace VPB
{
    public class PosePeopleCountIndex
    {
        private static PosePeopleCountIndex _instance;
        public static PosePeopleCountIndex Instance => _instance ?? (_instance = new PosePeopleCountIndex());

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool _loaded;
        private bool _dirty;
        private int _dirtyWrites;

        private string IndexPath => Path.Combine(GlobalInfo.PluginInfoDirectory, "PosePeopleCountIndex.json");

        private PosePeopleCountIndex()
        {
        }

        private void EnsureLoaded()
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
            EnsureLoaded();
            return _counts.TryGetValue(uid, out peopleCount) && peopleCount > 0;
        }

        public void Set(string uid, int peopleCount)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (peopleCount <= 0) return;
            EnsureLoaded();

            int existing;
            if (_counts.TryGetValue(uid, out existing) && existing == peopleCount) return;

            _counts[uid] = peopleCount;
            _dirty = true;
            _dirtyWrites++;

            // Avoid writing to disk on every single pose; batch a bit.
            if (_dirtyWrites >= 200)
            {
                Save();
            }
        }

        public void Save()
        {
            if (!_dirty) return;

            try
            {
                JSONClass root = new JSONClass();
                foreach (var kvp in _counts)
                {
                    root[kvp.Key] = kvp.Value.ToString();
                }

                if (!Directory.Exists(GlobalInfo.PluginInfoDirectory))
                    Directory.CreateDirectory(GlobalInfo.PluginInfoDirectory);

                File.WriteAllText(IndexPath, root.ToString());
                _dirty = false;
                _dirtyWrites = 0;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Failed to save PosePeopleCountIndex: " + ex);
            }
        }
    }
}
