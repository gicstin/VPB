using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VPB
{
    public enum SortType
    {
        Name,
        Date,
        Size,
        Count,
        Score // Placeholder for future
    }

    public enum SortDirection
    {
        Ascending,
        Descending
    }

    [Serializable]
    public class SortState
    {
        public SortType Type = SortType.Name;
        public SortDirection Direction = SortDirection.Ascending;

        public SortState() { }
        public SortState(SortType type, SortDirection direction)
        {
            Type = type;
            Direction = direction;
        }

        public SortState Clone()
        {
            return new SortState(Type, Direction);
        }
    }

    public class GallerySortManager
    {
        private static GallerySortManager _instance;
        public static GallerySortManager Instance
        {
            get
            {
                if (_instance == null) _instance = new GallerySortManager();
                return _instance;
            }
        }

        private GallerySortCache cache;

        public GallerySortManager()
        {
            cache = new GallerySortCache();
        }

        public void SortFiles(List<FileEntry> files, SortState state)
        {
            if (files == null || state == null) return;

            switch (state.Type)
            {
                case SortType.Name:
                    if (state.Direction == SortDirection.Ascending)
                        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    else
                        files.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.Date:
                    if (state.Direction == SortDirection.Ascending)
                        files.Sort((a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));
                    else
                        files.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                    break;
                case SortType.Size:
                    if (state.Direction == SortDirection.Ascending)
                        files.Sort((a, b) => a.Size.CompareTo(b.Size));
                    else
                        files.Sort((a, b) => b.Size.CompareTo(a.Size));
                    break;
                // Count not applicable for files
            }
        }

        public void SortCategories(List<Gallery.Category> categories, SortState state, Dictionary<string, int> counts = null)
        {
            if (categories == null || state == null) return;

            switch (state.Type)
            {
                case SortType.Name:
                    if (state.Direction == SortDirection.Ascending)
                        categories.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                    else
                        categories.Sort((a, b) => string.Compare(b.name, a.name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.Count:
                    if (counts != null)
                    {
                        if (state.Direction == SortDirection.Ascending)
                            categories.Sort((a, b) => GetCount(a.name, counts).CompareTo(GetCount(b.name, counts)));
                        else
                            categories.Sort((a, b) => GetCount(b.name, counts).CompareTo(GetCount(a.name, counts)));
                    }
                    break;
            }
        }

        public void SortCreators(List<CreatorCacheEntry> creators, SortState state)
        {
            if (creators == null || state == null) return;

            switch (state.Type)
            {
                case SortType.Name:
                    if (state.Direction == SortDirection.Ascending)
                        creators.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    else
                        creators.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case SortType.Count:
                    if (state.Direction == SortDirection.Ascending)
                        creators.Sort((a, b) => a.Count.CompareTo(b.Count));
                    else
                        creators.Sort((a, b) => b.Count.CompareTo(a.Count));
                    break;
            }
        }

        private int GetCount(string key, Dictionary<string, int> counts)
        {
            if (counts.TryGetValue(key, out int count)) return count;
            return 0;
        }

        public SortState GetDefaultSortState(string context)
        {
            return cache.GetSortState(context) ?? new SortState(SortType.Name, SortDirection.Ascending);
        }

        public void SaveSortState(string context, SortState state)
        {
            cache.SaveSortState(context, state);
        }

        public void SaveCache()
        {
            cache.Save();
        }
    }

    public class GallerySortCache
    {
        private Dictionary<string, SortState> sortStates = new Dictionary<string, SortState>();
        private string cachePath;

        public GallerySortCache()
        {
            string cacheDir = Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Cache"), "VPB");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            cachePath = Path.Combine(cacheDir, "gallery_sort_cache.bin");
            Load();
        }

        public SortState GetSortState(string context)
        {
            if (sortStates.TryGetValue(context, out SortState state))
                return state.Clone();
            return null;
        }

        public void SaveSortState(string context, SortState state)
        {
            sortStates[context] = state.Clone();
            Save();
        }

        private void Load()
        {
            if (!File.Exists(cachePath)) return;

            try
            {
                using (var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read))
                using (var reader = new BinaryReader(fs))
                {
                    int version = reader.ReadInt32();
                    if (version != 1) return;

                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        string key = reader.ReadString();
                        SortType type = (SortType)reader.ReadInt32();
                        SortDirection dir = (SortDirection)reader.ReadInt32();
                        sortStates[key] = new SortState(type, dir);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load sort cache: " + ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(fs))
                {
                    writer.Write(1); // Version
                    writer.Write(sortStates.Count);
                    foreach (var kvp in sortStates)
                    {
                        writer.Write(kvp.Key);
                        writer.Write((int)kvp.Value.Type);
                        writer.Write((int)kvp.Value.Direction);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to save sort cache: " + ex.Message);
            }
        }
    }
}
