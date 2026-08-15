using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using MVR.FileManagement;

namespace VPB
{
    public static class VpbRandomHistory
    {
        public const string ViewScope = "view";

        private const int MaxScopes = 48;
        private const int RingCapacity = 320;
        private const int PersistPerScope = 128;
        private const int WindowCap = 256;
        private const int RejectionAttempts = 24;
        private const int SaveIntervalMs = 45000;
        private const int PersistVersion = 1;

        private sealed class Scope
        {
            public readonly Dictionary<long, int> Seq = new Dictionary<long, int>(RingCapacity);
            public readonly long[] RingHash = new long[RingCapacity];
            public readonly int[] RingSeq = new int[RingCapacity];
            public int Head;
            public int Count;
            public int Counter;
            public int TouchedTick;

            public bool IsRecent(long h, int window)
            {
                if (window <= 0 || h == 0L) return false;
                int seq;
                if (!Seq.TryGetValue(h, out seq)) return false;
                return (Counter - seq) < window;
            }

            public void Note(long h)
            {
                if (h == 0L) return;
                int seq = ++Counter;

                if (Count >= RingCapacity)
                {
                    long oldHash = RingHash[Head];
                    int oldSeq = RingSeq[Head];
                    int cur;
                    if (oldHash != 0L && Seq.TryGetValue(oldHash, out cur) && cur == oldSeq)
                        Seq.Remove(oldHash);
                }
                else
                {
                    Count++;
                }

                RingHash[Head] = h;
                RingSeq[Head] = seq;
                Head++;
                if (Head >= RingCapacity) Head = 0;
                Seq[h] = seq;
            }

            public void CopyRecent(List<long> dest, int max)
            {
                if (dest == null) return;
                int take = Count < max ? Count : max;
                if (take <= 0) return;
                int start = Head - take;
                if (start < 0) start += RingCapacity;
                for (int i = 0; i < take; i++)
                {
                    int idx = start + i;
                    if (idx >= RingCapacity) idx -= RingCapacity;
                    long h = RingHash[idx];
                    if (h == 0L) continue;
                    int cur;
                    if (!Seq.TryGetValue(h, out cur) || cur != RingSeq[idx]) continue;
                    dest.Add(h);
                }
            }
        }

        private static readonly object s_Lock = new object();
        private static readonly Dictionary<string, Scope> s_Scopes =
            new Dictionary<string, Scope>(MaxScopes, StringComparer.OrdinalIgnoreCase);
        private static readonly List<int> s_Allowed = new List<int>(512);
        private static readonly List<long> s_PersistScratch = new List<long>(PersistPerScope);

        private static bool s_Loaded;
        private static bool s_Dirty;
        private static int s_LastSaveTick;

        public static long HashKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0L;
            unchecked
            {
                ulong h = 14695981039346656037UL;
                for (int i = 0; i < key.Length; i++)
                {
                    char c = key[i];
                    if (c == '\\') c = '/';
                    else if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                    h ^= c;
                    h *= 1099511628211UL;
                }
                long v = (long)h;
                return v == 0L ? 1L : v;
            }
        }

        public static string IdentityOf(FileEntry file)
        {
            if (file == null) return null;
            string s = null;
            try { s = file.Path; } catch { s = null; }
            if (string.IsNullOrEmpty(s))
            {
                try { s = file.Uid; } catch { s = null; }
            }
            return s;
        }

        public static long HashOf(FileEntry file)
        {
            return HashKey(IdentityOf(file));
        }

        public static int ComputeWindow(int poolCount)
        {
            if (poolCount <= 1) return 0;
            int w = (poolCount + 1) / 2;
            int ceiling = poolCount - 1;
            if (ceiling > WindowCap) ceiling = WindowCap;
            if (w > ceiling) w = ceiling;
            if (w < 1) w = 1;
            return w;
        }

        public static bool IsRecent(string scopeKey, FileEntry file, int window)
        {
            if (window <= 0 || file == null) return false;
            long h = HashOf(file);
            if (h == 0L) return false;
            lock (s_Lock)
            {
                EnsureLoaded();
                Scope sc;
                if (!s_Scopes.TryGetValue(NormalizeScope(scopeKey), out sc)) return false;
                return sc.IsRecent(h, window);
            }
        }

        public static void Note(string scopeKey, FileEntry file)
        {
            Note(scopeKey, IdentityOf(file));
        }

        public static void Note(string scopeKey, string identityKey)
        {
            long h = HashKey(identityKey);
            if (h == 0L) return;
            lock (s_Lock)
            {
                EnsureLoaded();
                GetOrCreateScope(NormalizeScope(scopeKey)).Note(h);
                s_Dirty = true;
                MaybeSaveLocked();
            }
        }

        public static int PickIndex(string scopeKey, List<FileEntry> pool, string extraExcludeKey)
        {
            if (pool == null || pool.Count == 0) return -1;
            if (pool.Count == 1) return pool[0] != null ? 0 : -1;

            long extra = HashKey(extraExcludeKey);
            int n = pool.Count;

            lock (s_Lock)
            {
                EnsureLoaded();
                Scope sc = GetOrCreateScope(NormalizeScope(scopeKey));
                int window = ComputeWindow(n);

                for (int a = 0; a < RejectionAttempts; a++)
                {
                    int i = VpbRandom.Next(n);
                    FileEntry f = pool[i];
                    if (f == null) continue;
                    long h = HashOf(f);
                    if (h == 0L) continue;
                    if (h == extra) continue;
                    if (sc.IsRecent(h, window)) continue;
                    return i;
                }

                int w = window;
                bool useExtra = extra != 0L;
                while (true)
                {
                    s_Allowed.Clear();
                    for (int i = 0; i < n; i++)
                    {
                        FileEntry f = pool[i];
                        if (f == null) continue;
                        long h = HashOf(f);
                        if (useExtra && h == extra) continue;
                        if (sc.IsRecent(h, w)) continue;
                        s_Allowed.Add(i);
                    }
                    if (s_Allowed.Count > 0)
                    {
                        int idx = s_Allowed[VpbRandom.Next(s_Allowed.Count)];
                        s_Allowed.Clear();
                        return idx;
                    }
                    if (w > 0) { w >>= 1; continue; }
                    if (useExtra) { useExtra = false; continue; }
                    break;
                }

                for (int i = 0; i < n; i++)
                {
                    if (pool[i] != null) return i;
                }
                return -1;
            }
        }

        public static FileEntry Pick(string scopeKey, List<FileEntry> pool, string extraExcludeKey, bool commit)
        {
            int i = PickIndex(scopeKey, pool, extraExcludeKey);
            if (i < 0) return null;
            FileEntry f = pool[i];
            if (commit) Note(scopeKey, f);
            return f;
        }

        public static void ClearScope(string scopeKey)
        {
            lock (s_Lock)
            {
                if (s_Scopes.Remove(NormalizeScope(scopeKey)))
                {
                    s_Dirty = true;
                    MaybeSaveLocked();
                }
            }
        }

        public static void ClearAll()
        {
            lock (s_Lock)
            {
                s_Scopes.Clear();
                s_Dirty = true;
                s_LastSaveTick = 0;
                MaybeSaveLocked();
            }
        }

        public static void Flush()
        {
            lock (s_Lock)
            {
                if (!s_Dirty) return;
                SaveLocked();
            }
        }

        private static string NormalizeScope(string scopeKey)
        {
            return string.IsNullOrEmpty(scopeKey) ? ViewScope : scopeKey;
        }

        private static Scope GetOrCreateScope(string key)
        {
            Scope sc;
            if (s_Scopes.TryGetValue(key, out sc) && sc != null)
            {
                sc.TouchedTick = Environment.TickCount;
                return sc;
            }

            if (s_Scopes.Count >= MaxScopes) EvictOldestScopeLocked();

            sc = new Scope();
            sc.TouchedTick = Environment.TickCount;
            s_Scopes[key] = sc;
            return sc;
        }

        private static void EvictOldestScopeLocked()
        {
            string oldestKey = null;
            int oldestAge = int.MinValue;
            int now = Environment.TickCount;
            foreach (var kv in s_Scopes)
            {
                int age = now - kv.Value.TouchedTick;
                if (age > oldestAge)
                {
                    oldestAge = age;
                    oldestKey = kv.Key;
                }
            }
            if (oldestKey != null) s_Scopes.Remove(oldestKey);
        }

        private static string StorePath
        {
            get
            {
                string dir = Path.Combine(
                    Path.Combine(Path.Combine(Directory.GetCurrentDirectory(), "Saves"), "PluginData"), "VPB");
                return Path.Combine(dir, "VPB.random.json");
            }
        }

        private static void EnsureLoaded()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            s_LastSaveTick = Environment.TickCount;
            try
            {
                string path = StorePath;
                if (!File.Exists(path)) return;

                JSONClass root = JSON.Parse(File.ReadAllText(path)) as JSONClass;
                if (root == null) return;
                if (root["v"].AsInt > PersistVersion) return;

                JSONClass scopes = root["scopes"] as JSONClass;
                if (scopes == null) return;

                foreach (string key in scopes.Keys)
                {
                    JSONArray arr = scopes[key] as JSONArray;
                    if (arr == null || arr.Count == 0) continue;
                    if (s_Scopes.Count >= MaxScopes) break;

                    Scope sc = new Scope();
                    sc.TouchedTick = Environment.TickCount;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        long h;
                        if (!long.TryParse(arr[i].Value, out h)) continue;
                        sc.Note(h);
                    }
                    if (sc.Count > 0) s_Scopes[key] = sc;
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] Random history load failed: " + ex.Message); } catch { }
            }
        }

        private static void MaybeSaveLocked()
        {
            if (!s_Dirty) return;
            int now = Environment.TickCount;
            if (unchecked(now - s_LastSaveTick) < SaveIntervalMs) return;
            SaveLocked();
        }

        private static void SaveLocked()
        {
            s_Dirty = false;
            s_LastSaveTick = Environment.TickCount;
            try
            {
                string path = StorePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                JSONClass root = new JSONClass();
                root["v"].AsInt = PersistVersion;
                JSONClass scopes = new JSONClass();

                foreach (var kv in s_Scopes)
                {
                    Scope sc = kv.Value;
                    if (sc == null || sc.Count == 0) continue;
                    s_PersistScratch.Clear();
                    sc.CopyRecent(s_PersistScratch, PersistPerScope);
                    if (s_PersistScratch.Count == 0) continue;

                    JSONArray arr = new JSONArray();
                    for (int i = 0; i < s_PersistScratch.Count; i++)
                        arr.Add(s_PersistScratch[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                    scopes[kv.Key] = arr;
                }
                s_PersistScratch.Clear();

                root["scopes"] = scopes;
                File.WriteAllText(path, root.ToString());
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] Random history save failed: " + ex.Message); } catch { }
            }
        }
    }
}
