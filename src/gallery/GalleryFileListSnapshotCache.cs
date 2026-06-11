using System;
using System.Collections.Generic;

namespace VPB
{
    /// <summary>
    /// Reuses the sorted file list for a gallery category view across panels when filters/sort match,
    /// avoiding a full package + disk rescan (often multiple seconds).
    /// Cleared when the package library changes.
    /// </summary>
    internal static class GalleryFileListSnapshotCache
    {
        private static readonly object s_Lock = new object();
        private static readonly Dictionary<string, List<FileEntry>> s_ByKey = new Dictionary<string, List<FileEntry>>(StringComparer.Ordinal);

        private const int MaxEntries = 48;

        public static void Clear()
        {
            lock (s_Lock) { s_ByKey.Clear(); }
        }

        public static bool TryGet(string key, out List<FileEntry> list)
        {
            list = null;
            if (string.IsNullOrEmpty(key)) return false;
            lock (s_Lock)
            {
                List<FileEntry> src;
                if (!s_ByKey.TryGetValue(key, out src) || src == null) return false;
                list = new List<FileEntry>(src);
                return true;
            }
        }

        public static void Put(string key, List<FileEntry> files)
        {
            if (string.IsNullOrEmpty(key) || files == null) return;
            lock (s_Lock)
            {
                if (s_ByKey.Count >= MaxEntries)
                    s_ByKey.Clear();
                s_ByKey[key] = new List<FileEntry>(files);
            }
        }

        internal static void InvalidateAll() { Clear(); GalleryTagCountSnapshotCache.Clear(); }
    }
}
