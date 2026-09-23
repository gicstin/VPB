using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VPB
{
    internal static class LocalDiskEntryCache
    {
        sealed class DirListing
        {
            public Dictionary<string, string> Names;
            public string ActualPath;
            public long StampTicks;
        }

        static readonly object s_Lock = new object();
        static readonly Dictionary<string, DirListing> s_Dirs = new Dictionary<string, DirListing>(StringComparer.OrdinalIgnoreCase);
        static readonly Stopwatch s_Clock = Stopwatch.StartNew();

        internal static double TtlSeconds = 5.0;
        internal static int Listings;

        public static bool Exists(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            string p = Normalize(relativePath);
            if (string.IsNullOrEmpty(p)) return DirectDiskExists(relativePath);
            int slash = p.LastIndexOf('/');
            string dir = slash > 0 ? p.Substring(0, slash) : string.Empty;
            string name = slash >= 0 ? p.Substring(slash + 1) : p;
            if (name.Length == 0) return false;
            lock (s_Lock)
            {
                DirListing listing = GetListing(dir);
                return listing.Names != null && listing.Names.ContainsKey(name);
            }
        }

        public static void Invalidate()
        {
            lock (s_Lock)
                s_Dirs.Clear();
        }

        static bool DirectDiskExists(string path)
        {
            try { return File.Exists(path) || Directory.Exists(path); }
            catch { return false; }
        }

        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string p = path.IndexOf('\\') >= 0 ? path.Replace('\\', '/') : path;
            p = p.Trim();
            while (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            p = p.TrimStart('/').TrimEnd('/');
            if (p.Length == 0 || p.IndexOf(':') >= 0 || p.IndexOf("..", StringComparison.Ordinal) >= 0) return null;
            return p;
        }

        static DirListing GetListing(string dir)
        {
            DirListing cached;
            long now = s_Clock.ElapsedTicks;
            long ttlTicks = (long)(TtlSeconds * Stopwatch.Frequency);
            if (s_Dirs.TryGetValue(dir, out cached) && now - cached.StampTicks <= ttlTicks)
                return cached;

            string actualPath = null;
            if (dir.Length == 0)
            {
                actualPath = ".";
            }
            else
            {
                int slash = dir.LastIndexOf('/');
                string parent = slash > 0 ? dir.Substring(0, slash) : string.Empty;
                string leaf = slash >= 0 ? dir.Substring(slash + 1) : dir;
                DirListing parentListing = GetListing(parent);
                string actualLeaf;
                if (parentListing.Names != null && parentListing.Names.TryGetValue(leaf, out actualLeaf))
                    actualPath = parentListing.ActualPath == "." ? actualLeaf : parentListing.ActualPath + "/" + actualLeaf;
            }

            Dictionary<string, string> names = null;
            if (actualPath != null)
            {
                try
                {
                    string[] entries = Directory.GetFileSystemEntries(actualPath);
                    names = new Dictionary<string, string>(entries.Length, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < entries.Length; i++)
                    {
                        string n = Path.GetFileName(entries[i]);
                        if (!string.IsNullOrEmpty(n) && !names.ContainsKey(n)) names[n] = n;
                    }
                    Listings++;
                }
                catch
                {
                    names = null;
                }
            }

            var listing = new DirListing { Names = names, ActualPath = actualPath, StampTicks = now };
            s_Dirs[dir] = listing;
            return listing;
        }
    }
}
