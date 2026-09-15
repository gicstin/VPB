using System;
using System.Collections.Generic;

namespace VPB
{
    internal static class GalleryDepStatus
    {
        internal const byte Unknown = 0;
        internal const byte Ready = 1;
        internal const byte Missing = 2;
        internal const byte Broken = 3;

        private const int CacheCap = 32768;
        private const int PendingCap = 384;
        private const int CountCap = 0xFFFFF;

        private static readonly Dictionary<string, int> _byKey =
            new Dictionary<string, int>(4096, StringComparer.OrdinalIgnoreCase);
        private static readonly Queue<FileEntry> _pendingFiles = new Queue<FileEntry>();
        private static readonly Queue<string> _pendingKeys = new Queue<string>();
        private static readonly HashSet<string> _pendingSet =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static int PendingCount { get { return _pendingKeys.Count; } }

        internal static bool TryGet(string key, out byte status, out int missingCount)
        {
            status = Unknown;
            missingCount = 0;
            if (string.IsNullOrEmpty(key)) return false;
            int packed;
            if (!_byKey.TryGetValue(key, out packed)) return false;
            status = (byte)(packed & 0x7);
            missingCount = packed >> 3;
            return true;
        }

        internal static void Enqueue(string key, FileEntry file)
        {
            if (string.IsNullOrEmpty(key) || file == null) return;
            if (_byKey.ContainsKey(key)) return;
            if (_pendingKeys.Count >= PendingCap) return;
            if (!_pendingSet.Add(key)) return;
            _pendingKeys.Enqueue(key);
            _pendingFiles.Enqueue(file);
        }

        internal static bool TryDequeue(out string key, out FileEntry file)
        {
            key = null;
            file = null;
            if (_pendingKeys.Count == 0) return false;
            key = _pendingKeys.Dequeue();
            file = _pendingFiles.Dequeue();
            _pendingSet.Remove(key);
            return true;
        }

        internal static void Store(string key, byte status, int missingCount)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_byKey.Count >= CacheCap) _byKey.Clear();
            if (missingCount < 0) missingCount = 0;
            if (missingCount > CountCap) missingCount = CountCap;
            _byKey[key] = (missingCount << 3) | (status & 0x7);
        }

        internal static byte Compute(FileEntry file, out int missingCount)
        {
            missingCount = 0;
            if (file == null) return Unknown;
            if (file is VirtualFileEntry) return Broken;
            try { missingCount = GallerySortManager.GetMissingDepsCount(file); }
            catch { missingCount = 0; }
            return missingCount > 0 ? Missing : Ready;
        }

        internal static byte Resolve(string key, FileEntry file, out int missingCount)
        {
            byte cached;
            if (TryGet(key, out cached, out missingCount)) return cached;
            byte status = Compute(file, out missingCount);
            Store(key, status, missingCount);
            return status;
        }

        internal static void Invalidate(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _byKey.Remove(key);
        }

        internal static void InvalidateAll()
        {
            _byKey.Clear();
            _pendingKeys.Clear();
            _pendingFiles.Clear();
            _pendingSet.Clear();
        }
    }
}
