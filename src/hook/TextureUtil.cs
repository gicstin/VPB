using System;
using System.IO;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using System.Runtime.InteropServices;

namespace VPB
{
    public static class TextureUtil
    {
        private static readonly object s_DownscaledActiveLock = new object();
        private static readonly HashSet<string> s_DownscaledActiveKeys = new HashSet<string>();

        private static readonly object s_SimPurgeLock = new object();
        private static readonly HashSet<string> s_SimPurgedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static int GetDownscaledActiveCount()
        {
            lock (s_DownscaledActiveLock)
            {
                return s_DownscaledActiveKeys.Count;
            }
        }

        public static void MarkDownscaledActive(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (s_DownscaledActiveLock)
            {
                s_DownscaledActiveKeys.Add(key);
            }
        }

        public static void UnmarkDownscaledActive(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (s_DownscaledActiveLock)
            {
                s_DownscaledActiveKeys.Remove(key);
            }
        }

        public static void UnmarkDownscaledActiveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            lock (s_DownscaledActiveLock)
            {
                if (s_DownscaledActiveKeys.Count == 0) return;
                var remove = new List<string>();
                foreach (var k in s_DownscaledActiveKeys)
                {
                    if (k != null && k.StartsWith(prefix))
                    {
                        remove.Add(k);
                    }
                }
                for (int i = 0; i < remove.Count; i++)
                {
                    s_DownscaledActiveKeys.Remove(remove[i]);
                }
            }
        }

        public static int GetExpectedRawDataSize(int w, int h, TextureFormat fmt)
        {
            switch (fmt)
            {
                case TextureFormat.Alpha8: return w * h;
                case TextureFormat.RGB24: return w * h * 3;
                case TextureFormat.RGBA32: return w * h * 4;
                case TextureFormat.ARGB32: return w * h * 4;
                case TextureFormat.DXT1: return (Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4)) * 8;
                case TextureFormat.DXT5: return (Mathf.Max(1, (w + 3) / 4) * Mathf.Max(1, (h + 3) / 4)) * 16;
                default: return 0;
            }
        }

        /// <summary>
        /// Loads raw texture data from a byte array using zero-copy IntPtr if the buffer is oversized.
        /// </summary>
        public static void SafeLoadRawTextureData(Texture2D t, byte[] data, int length, int w, int h, TextureFormat fmt)
        {
            if (t == null || data == null) return;
            
            int expected = GetExpectedRawDataSize(w, h, fmt);
            if (expected <= 0)
            {
                // Fallback for formats we don't have expected size for
                t.LoadRawTextureData(data);
                return;
            }

            if (length < expected)
            {
                LogUtil.LogWarning($"[VPB] SafeLoadRawTextureData: data length ({length}) is smaller than expected ({expected}) for {w}x{h} {fmt}");
                // We still try it, Unity might throw or it might work if Mips are involved but we don't handle that here.
                t.LoadRawTextureData(data);
                return;
            }

            if (data.Length == expected && length == expected)
            {
                t.LoadRawTextureData(data);
            }
            else
            {
                // Use IntPtr overload to avoid copying if the array is too large (pooled buffer)
                GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    t.LoadRawTextureData(pin.AddrOfPinnedObject(), expected);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"[VPB] SafeLoadRawTextureData (IntPtr) failed: {ex.Message}");
                    // Last resort fallback
                    t.LoadRawTextureData(data);
                }
                finally
                {
                    pin.Free();
                }
            }
        }

        /// <summary>
        /// Overload that uses data.Length as the valid data length.
        /// </summary>
        public static void SafeLoadRawTextureData(Texture2D t, byte[] data, int w, int h, TextureFormat fmt)
        {
            SafeLoadRawTextureData(t, data, data != null ? data.Length : 0, w, h, fmt);
        }

        private static readonly char[] s_InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();

        private static void TryPurgeSimZstdCacheVariants(string imgPath, MVR.FileManagement.FileEntry fileEntry, string cacheDir)
        {
            if (string.IsNullOrEmpty(imgPath) || fileEntry == null || string.IsNullOrEmpty(cacheDir)) return;
            try
            {
                string fileName = System.IO.Path.GetFileName(imgPath);
                fileName = SanitizeFileName(fileName).Replace('.', '_');
                if (fileName.Length > 100) fileName = fileName.Substring(0, 100);

                string sizeStr = fileEntry.Size.ToString();
                string timeStr = fileEntry.LastWriteTime.ToFileTime().ToString();
                string prefix = fileName + "_" + sizeStr + "_" + timeStr + "_";

                string[] matches;
                try { matches = Directory.GetFiles(cacheDir, prefix + "*.zvamcache", SearchOption.TopDirectoryOnly); }
                catch { matches = null; }
                if (matches == null || matches.Length == 0) return;

                int deleted = 0;
                for (int i = 0; i < matches.Length; i++)
                {
                    string p = matches[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    try
                    {
                        if (File.Exists(p))
                        {
                            File.Delete(p);
                            deleted++;
                        }
                    }
                    catch { }

                    try
                    {
                        string meta = p + "meta";
                        if (File.Exists(meta)) File.Delete(meta);
                    }
                    catch { }
                }

                try
                {
                    if (deleted > 0 && Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 1)
                        LogUtil.Log($"[VPB SIM] Purged {deleted} corrupted .zvamcache file(s) for sim texture: {imgPath}");
                }
                catch { }
            }
            catch { }
        }

        private static bool IsZstdMetaReadable(string zvamcachePath)
        {
            if (string.IsNullOrEmpty(zvamcachePath)) return false;
            try
            {
                string metaPath = zvamcachePath + "meta";
                if (!File.Exists(metaPath)) return false;
                string json = File.ReadAllText(metaPath);
                if (string.IsNullOrEmpty(json)) return false;
                var n = SimpleJSON.JSON.Parse(json);
                if (n == null) return false;
                return n["isReadable"].AsBool;
            }
            catch
            {
                return false;
            }
        }

        private static bool AnyMatchingZstdVariantHasReadableMeta(string imgPath, MVR.FileManagement.FileEntry fileEntry, string cacheDir)
        {
            if (string.IsNullOrEmpty(imgPath) || fileEntry == null || string.IsNullOrEmpty(cacheDir)) return false;
            try
            {
                string fileName = System.IO.Path.GetFileName(imgPath);
                fileName = SanitizeFileName(fileName).Replace('.', '_');
                if (fileName.Length > 100) fileName = fileName.Substring(0, 100);

                string sizeStr = fileEntry.Size.ToString();
                string timeStr = fileEntry.LastWriteTime.ToFileTime().ToString();
                string prefix = fileName + "_" + sizeStr + "_" + timeStr + "_";

                string[] matches;
                try { matches = Directory.GetFiles(cacheDir, prefix + "*.zvamcache", SearchOption.TopDirectoryOnly); }
                catch { matches = null; }
                if (matches == null || matches.Length == 0) return false;

                for (int i = 0; i < matches.Length; i++)
                {
                    string p = matches[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (IsZstdMetaReadable(p)) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void TryPurgeSimZstdByFilenameFallback(string imgPath, string cacheDir)
        {
            if (string.IsNullOrEmpty(imgPath) || string.IsNullOrEmpty(cacheDir)) return;
            try
            {
                string fileName = System.IO.Path.GetFileName(imgPath);
                fileName = SanitizeFileName(fileName).Replace('.', '_');
                if (string.IsNullOrEmpty(fileName)) return;
                if (fileName.Length > 100) fileName = fileName.Substring(0, 100);

                // Fallback for early scene-load where FileEntry is not available yet.
                // Cache filenames are: {fileName}_{size}_{time}_{sig}.zvamcache
                // We match by sanitized filename only, then delete the variants.
                string[] matches;
                try { matches = Directory.GetFiles(cacheDir, fileName + "_*_*.zvamcache", SearchOption.TopDirectoryOnly); }
                catch { matches = null; }
                if (matches == null || matches.Length == 0) return;

                for (int i = 0; i < matches.Length; i++)
                {
                    string p = matches[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                    try { string meta = p + "meta"; if (File.Exists(meta)) File.Delete(meta); } catch { }
                }
            }
            catch { }
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "img";
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                sb.Append(Array.IndexOf(s_InvalidFileNameChars, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        public static string GetZstdCachePath(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f, bool isReadable = false)
        {
            if (string.IsNullOrEmpty(imgPath) || imgPath == "NULL") return null;
            
            string cacheDir = VamHookPlugin.GetCacheDir();
            bool isSimReq = (isReadable || SuperControllerHook.IsSimulationTexturePath(imgPath));
            if (isSimReq)
            {
                // Must never serve SIM from .zvamcache. Purge aggressively, but avoid repeated directory scans.
                bool shouldPurge = false;
                lock (s_SimPurgeLock)
                {
                    if (!s_SimPurgedThisSession.Contains(imgPath))
                    {
                        s_SimPurgedThisSession.Add(imgPath);
                        shouldPurge = true;
                    }
                }
                if (shouldPurge)
                {
                    // Even if FileEntry isn't available yet (early scene-load), remove likely corrupted variants.
                    TryPurgeSimZstdByFilenameFallback(imgPath, cacheDir);
                }
            }

            var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(imgPath);
            if (fileEntry == null)
            {
                if (Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogUtil.LogTextureTrace("GetZstdCachePath_NoFileEntry:" + imgPath, "[VPB] GetZstdCachePath: No FileEntry for " + imgPath);
                }
                // If SIM request, we already purged by filename fallback above; always return null.
                if (isSimReq) return null;
                return null;
            }

            // SIM (simulation/physics) textures must never be served from VPB's .zvamcache.
            // If an old/corrupted .zvamcache exists for a SIM request, delete it now so VaM can rebuild/cache normally.
            if (isSimReq)
            {
                // FileEntry is now available; do the precise purge once.
                // (This is still guarded by the session set above; if we already purged by filename fallback,
                // it's still safe to call again, but we avoid extra IO.)
                TryPurgeSimZstdCacheVariants(imgPath, fileEntry, cacheDir);
                return null;
            }

            string fileName = System.IO.Path.GetFileName(imgPath);
            fileName = SanitizeFileName(fileName).Replace('.', '_');
            if (fileName.Length > 100) fileName = fileName.Substring(0, 100);

            string sizeStr = fileEntry.Size.ToString();
            string timeStr = fileEntry.LastWriteTime.ToFileTime().ToString();

            string sig = "";
            if (targetWidth > 0 && targetHeight > 0) sig += $"{targetWidth}_{targetHeight}";
            if (isReadable) sig += "_R";
            if (compress) sig += "_C";
            if (linear) sig += "_L";
            if (isNormalMap) sig += "_N";
            if (createAlphaFromGrayscale) sig += "_A";
            if (createNormalFromBump) sig += "_BN" + bumpStrength;
            if (invert) sig += "_I";
            
            string finalPath = System.IO.Path.Combine(cacheDir, $"{fileName}_{sizeStr}_{timeStr}_{sig}.zvamcache");

            // Safety net: some SIM textures have neutral filenames and may not be detected by heuristics at request time.
            // If an existing cache entry indicates a readable (SIM) texture via meta, purge it and fall back to VaM.
            try
            {
                if (File.Exists(finalPath) && IsZstdMetaReadable(finalPath))
                {
                    TryPurgeSimZstdCacheVariants(imgPath, fileEntry, cacheDir);
                    return null;
                }
            }
            catch { }
            try
            {
                if (AnyMatchingZstdVariantHasReadableMeta(imgPath, fileEntry, cacheDir))
                {
                    TryPurgeSimZstdCacheVariants(imgPath, fileEntry, cacheDir);
                    return null;
                }
            }
            catch { }
            
            if (Settings.Instance.TextureLogLevel.Value >= 2)
            {
                // Only log if it doesn't exist to avoid spamming successful hits (which are logged by the caller)
                if (!System.IO.File.Exists(finalPath))
                    LogUtil.LogTextureTrace("ZstdCacheMiss:" + finalPath, "[VPB] Cache MISS lookup: " + System.IO.Path.GetFileName(finalPath) + " for " + System.IO.Path.GetFileName(imgPath));
            }

            return finalPath;
        }

        public static string GetNativeCachePath(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath) || imgPath == "NULL") return null;
            
            var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(imgPath);
            string textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
            if (fileEntry != null && textureCacheDir != null)
            {
                string text = fileEntry.Size.ToString();
                string text2 = fileEntry.LastWriteTime.ToFileTime().ToString();
                string fileName = System.IO.Path.GetFileName(imgPath);
                fileName = fileName.Replace('.', '_');
                // Signature "1" is hardcoded in the loaders
                return System.IO.Path.Combine(textureCacheDir, fileName + "_" + text + "_" + text2 + "_1.vamcache");
            }
            return null;
        }
    }
}
