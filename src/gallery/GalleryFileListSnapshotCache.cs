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

        private static int s_Epoch;

        /// <summary>
        /// Bumped whenever the cached lists are dropped (package library changed). Consumers that keep
        /// their own derived snapshots — quick-menu random preview reels — compare it to detect staleness.
        /// </summary>
        public static int Epoch { get { return s_Epoch; } }

        public static void Clear()
        {
            lock (s_Lock)
            {
                s_ByKey.Clear();
                unchecked { s_Epoch++; }
            }
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

        /// <summary>
        /// Copy cached snapshot into <paramref name="dest"/> (Clear + AddRange). Prefer over <see cref="TryGet"/>
        /// when caller reuses a scratch list across refreshes.
        /// </summary>
        public static bool TryCopyInto(string key, List<FileEntry> dest)
        {
            if (dest == null || string.IsNullOrEmpty(key)) return false;
            lock (s_Lock)
            {
                List<FileEntry> src;
                if (!s_ByKey.TryGetValue(key, out src) || src == null) return false;
                dest.Clear();
                if (dest.Capacity < src.Count) dest.Capacity = src.Count;
                dest.AddRange(src);
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
