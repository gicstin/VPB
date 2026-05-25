using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;

namespace VPB
{
    public partial class VamHookPlugin
    {
        public static string FormatDataSize(long bytes)
        {
            var inst = singleton;
            if (inst != null) return inst.FormatBytes(bytes);
            if (bytes < 0) return "…";
            return bytes + " B";
        }

        public int GetVamCacheCompressibleCount()
        {
            try { return GetVamCacheFileCount(); }
            catch { return 0; }
        }

        public long GetNativeTextureCacheBytes()
        {
            try { return GetTexturesFolderSize(); }
            catch { return 0; }
        }

        public long GetVpbZstdCacheBytes()
        {
            try { return GetVpbCacheFolderSize(); }
            catch { return 0; }
        }

        public string FormatBytes(long bytes)
        {
            if (bytes < 0) return "Calculating...";
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dbl = bytes;
            for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
                dbl = bytes / 1024.0;
            return string.Format("{0:0.##} {1}", dbl, suffix[i]);
        }

        private int GetVamCacheFileCount()
        {
            try
            {
                return GetVamCacheFileCount(
                    TextureUtil.ResolveTextureCacheDirFullPath(),
                    Settings.Instance.ThumbnailThreshold.Value);
            }
            catch { return 0; }
        }

        private int GetVamCacheFileCount(string path, int threshold)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;

                string[] files = Directory.GetFiles(path, "*.vamcache", SearchOption.TopDirectoryOnly);
                int count = 0;
                for (int i = 0; i < files.Length; i++)
                {
                    string metaPath = files[i] + "meta";
                    if (!File.Exists(metaPath)) continue;
                    try
                    {
                        var meta = JSON.Parse(File.ReadAllText(metaPath));
                        if (meta != null)
                        {
                            int w = meta["width"].AsInt;
                            int h = meta["height"].AsInt;
                            if (meta["isThumbnail"].AsBool || (w > 0 && w <= threshold && h > 0 && h <= threshold))
                                continue;
                        }
                    }
                    catch { continue; }

                    count++;
                }

                return count;
            }
            catch { return 0; }
        }

        private long GetVpbCacheFolderSize()
        {
            try
            {
                string path = GetCacheDir();
                if (!Directory.Exists(path)) return 0;

                long size = 0;
                var fileList = new List<string>();
                FileManager.SafeGetFiles(path, "*.*", fileList);
                for (int i = 0; i < fileList.Count; i++)
                    size += new FileInfo(fileList[i]).Length;
                return size;
            }
            catch { return 0; }
        }

        private long GetTexturesFolderSize()
        {
            try
            {
                string path = MVR.FileManagement.CacheManager.GetTextureCacheDir();
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;

                long size = 0;
                string[] files;
                try
                {
                    string sig = "0";
                    try { sig = Directory.GetLastWriteTimeUtc(path).ToBinary().ToString(); } catch { sig = "0"; }
                    string cacheKey = "cache:list|dir=" + (Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/')) + "|pat=*.vamcache";
                    var cached = new List<VpbLocalDatabase.SystemFileRow>();
                    if (VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey, sig, cached) && cached.Count > 0)
                    {
                        files = new string[cached.Count];
                        for (int i = 0; i < cached.Count; i++) files[i] = cached[i].Path;
                    }
                    else
                    {
                        files = Directory.GetFiles(path, "*.vamcache", SearchOption.TopDirectoryOnly);
                        try
                        {
                            var rows = new List<VpbLocalDatabase.SystemFileRow>(files.Length);
                            for (int i = 0; i < files.Length; i++)
                            {
                                string p = files[i];
                                if (string.IsNullOrEmpty(p)) continue;
                                var r = new VpbLocalDatabase.SystemFileRow();
                                try { r.Path = Path.GetFullPath(p); } catch { r.Path = p; }
                                r.LastWriteBinaryOrInvalid = long.MinValue;
                                r.SizeOrInvalid = long.MinValue;
                                rows.Add(r);
                            }
                            if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey, sig, rows);
                        }
                        catch { }
                    }
                }
                catch
                {
                    files = Directory.GetFiles(path, "*.vamcache", SearchOption.TopDirectoryOnly);
                }

                int threshold = Settings.Instance.ThumbnailThreshold.Value;
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    string metaPath = file + "meta";
                    bool isThumb = false;
                    if (File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = JSON.Parse(File.ReadAllText(metaPath));
                            if (meta != null)
                            {
                                int w = meta["width"].AsInt;
                                int h = meta["height"].AsInt;
                                if (meta["isThumbnail"].AsBool || (w > 0 && w <= threshold && h > 0 && h <= threshold))
                                    isThumb = true;
                            }
                        }
                        catch { }
                    }

                    if (!isThumb)
                    {
                        size += new FileInfo(file).Length;
                        if (File.Exists(metaPath))
                            size += new FileInfo(metaPath).Length;
                    }
                }
                return size;
            }
            catch { return 0; }
        }
    }
}
