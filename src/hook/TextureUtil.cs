using System;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

namespace VPB
{
    public static class TextureUtil
    {
        private static readonly object s_DownscaledActiveLock = new object();
        private static readonly HashSet<string> s_DownscaledActiveKeys = new HashSet<string>();

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

        public static string GetZstdCachePath(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f)
        {
            string cacheDir = VamHookPlugin.GetCacheDir();
            var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(imgPath);
            if (fileEntry == null)
            {
                if (Settings.Instance.TextureLogLevel.Value >= 2)
                {
                    LogUtil.LogTextureTrace("GetZstdCachePath_NoFileEntry:" + imgPath, "[VPB] GetZstdCachePath: No FileEntry for " + imgPath);
                }
                return null;
            }

            string fileName = System.IO.Path.GetFileName(imgPath);
            fileName = SanitizeFileName(fileName).Replace('.', '_');
            if (fileName.Length > 100) fileName = fileName.Substring(0, 100);

            string sizeStr = fileEntry.Size.ToString();
            string timeStr = fileEntry.LastWriteTime.ToFileTime().ToString();

            string sig = "";
            if (targetWidth > 0 && targetHeight > 0) sig += $"{targetWidth}_{targetHeight}";
            if (compress) sig += "_C";
            if (linear) sig += "_L";
            if (isNormalMap) sig += "_N";
            if (createAlphaFromGrayscale) sig += "_A";
            if (createNormalFromBump) sig += "_BN" + bumpStrength;
            if (invert) sig += "_I";
            
            string finalPath = System.IO.Path.Combine(cacheDir, $"{fileName}_{sizeStr}_{timeStr}_{sig}.zvamcache");
            
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
