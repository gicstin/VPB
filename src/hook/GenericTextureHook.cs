using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using SimpleJSON;
using StbImageSharp;
using Hebron.Runtime;

namespace VPB
{
    public static class GenericTextureHook
    {
        [ThreadStatic]
        private static string _lastReadImagePath;
        
        static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public static void PatchAll(Harmony harmony)
        {
            try
            {
                // Patch File.ReadAllBytes to capture context
                var mReadAllBytes = AccessTools.Method(typeof(System.IO.File), "ReadAllBytes", new Type[] { typeof(string) });
                if (mReadAllBytes != null)
                {
                    harmony.Patch(mReadAllBytes, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(File_ReadAllBytes_Postfix)));
                }

                // Patch Texture2D.LoadImage
                // Use GetMethod to avoid HarmonyX warnings on missing methods
                var mLoadImage = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]), typeof(bool) });
                if (mLoadImage != null)
                {
                    harmony.Patch(mLoadImage, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Prefix)));
                }
                else
                {
                    // Fallback for Unity 2018 (VaM 1.22)
                    mLoadImage = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]) });
                    if (mLoadImage != null)
                    {
                        harmony.Patch(mLoadImage, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Prefix_Simple)));
                    }
                    else
                    {
                        // Check for ImageConversion.LoadImage (Unity 2017+)
                        var imgConvType = AccessTools.TypeByName("UnityEngine.ImageConversion");
                        if (imgConvType != null)
                        {
                            mLoadImage = imgConvType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                            if (mLoadImage != null)
                            {
                                harmony.Patch(mLoadImage, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(ImageConversion_LoadImage_Prefix)));
                            }
                        }
                    }
                }

                // Patch Resources.Load
                var mResourcesLoad = AccessTools.Method(typeof(Resources), "Load", new Type[] { typeof(string) });
                if (mResourcesLoad != null)
                {
                    harmony.Patch(mResourcesLoad, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Resources_Load_Prefix)));
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("GenericTextureHook PatchAll failed: " + ex);
            }
        }

        public static void File_ReadAllBytes_Postfix(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                    path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    _lastReadImagePath = path;
                }
                else
                {
                    _lastReadImagePath = null;
                }
            }
            catch { }
        }

        public static void Resources_Load_Prefix(string path)
        {
             // Placeholder for monitoring
        }

        public static bool Texture2D_LoadImage_Prefix(Texture2D __instance, byte[] data, bool markNonReadable, ref bool __result)
        {
            string path = _lastReadImagePath;
            _lastReadImagePath = null; // Consume context

            if (ImageLoadingMgr.singleton == null || Settings.Instance == null || !Settings.Instance.ReduceTextureSize.Value)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(path))
            {
                if (ProcessWithCache(__instance, path, data, markNonReadable))
                {
                    __result = true;
                    return false; // Skip original
                }
            }
            return true;
        }

        public static bool Texture2D_LoadImage_Prefix_Simple(Texture2D __instance, byte[] data, ref bool __result)
        {
            // Unity 2018 LoadImage(byte[]) equivalent
            return Texture2D_LoadImage_Prefix(__instance, data, false, ref __result);
        }

        public static bool ImageConversion_LoadImage_Prefix(Texture2D tex, byte[] data, bool markNonReadable, ref bool __result)
        {
            return Texture2D_LoadImage_Prefix(tex, data, markNonReadable, ref __result);
        }

        private static bool ProcessWithCache(Texture2D tex, string path, byte[] originalData, bool markNonReadable)
        {
            try
            {
                // Use default settings for generic loads
                var qi = new ImageLoaderThreaded.QueuedImage();
                qi.imgPath = path;
                qi.tex = tex;
                qi.compress = true;
                qi.linear = false; 

                // 1. Check Disk Cache
                string cachePath = GetDiskCachePath(qi, false, 0, 0);
                if (!string.IsNullOrEmpty(cachePath))
                {
                    // Check full cache with resize meta
                    string metaPath = cachePath + ".meta";
                    if (File.Exists(metaPath))
                    {
                         try
                         {
                             var json = JSON.Parse(File.ReadAllText(metaPath));
                             int w = json["resizedWidth"].AsInt;
                             int h = json["resizedHeight"].AsInt;
                             
                             if (w > 0 && h > 0)
                             {
                                 string realCachePath = GetDiskCachePath(qi, true, w, h);
                                 string realCacheFile = realCachePath + ".cache";
                                 
                                 if (!File.Exists(realCacheFile)) realCacheFile = realCachePath; // Compat

                                 if (File.Exists(realCacheFile))
                                 {
                                     byte[] cacheBytes = File.ReadAllBytes(realCacheFile);
                                     if (tex.width != w || tex.height != h)
                                     {
                                         tex.Resize(w, h);
                                     }
                                     tex.LoadRawTextureData(cacheBytes);
                                     tex.Apply(false, !markNonReadable);
                                     return true;
                                 }
                             }
                         }
                         catch {}
                    }
                }

                // 2. Cache Miss: Resize Synchronously
                using (var ms = new MemoryStream(originalData))
                {
                    StbImage.stbi_set_flip_vertically_on_load(1);
                    int x, y, comp;
                    unsafe
                    {
                        var context = new StbImage.stbi__context(ms);
                        byte* result = StbImage.stbi__load_and_postprocess_8bit(context, &x, &y, &comp, 4);
                        
                        if (result != null)
                        {
                             try
                             {
                                 int w = x;
                                 int h = y;
                                 int newW = w;
                                 int newH = h;
                                 
                                 GetResizedSize(ref newW, ref newH, path);
                                 
                                 byte[] resizedRaw = new byte[newW * newH * 4];
                                 
                                 ImageProcessingOptimization.FastBitmapCopy(result, w, h, w*4, 4,
                                    resizedRaw, newW, newH, newW*4, 4, false, false);
                                    
                                 if (tex.width != newW || tex.height != newH)
                                     tex.Resize(newW, newH);
                                     
                                 tex.LoadRawTextureData(resizedRaw);
                                 tex.Apply(false, !markNonReadable);
                                 
                                 // Async Write
                                 qi.width = w; // Original dims for meta
                                 qi.height = h;
                                 WriteCache(qi, resizedRaw, newW, newH, tex.format);
                                 
                                 return true;
                             }
                             finally
                             {
                                 CRuntime.free(result);
                             }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("GenericTextureHook Error: " + ex);
            }
            return false;
        }

        private static void WriteCache(ImageLoaderThreaded.QueuedImage qi, byte[] bytes, int w, int h, TextureFormat fmt)
        {
             // Calculate paths
             string baseCachePath = GetDiskCachePath(qi, false, 0, 0);
             if (string.IsNullOrEmpty(baseCachePath)) return;

             string realCachePath = GetDiskCachePath(qi, true, w, h);
             string realCacheFile = realCachePath + ".cache";
             string metaFile = baseCachePath + ".meta"; // Meta goes to base path usually? No, check ImageLoadingMgr.
             // ImageLoadingMgr writes meta to: diskCachePath + ".meta" (where diskCachePath is the base one).
             
             System.Threading.ThreadPool.QueueUserWorkItem((s) => {
                 try
                 {
                     File.WriteAllBytes(realCacheFile, bytes);
                     
                     var json = new JSONClass();
                     json["type"] = "image";
                     json["width"].AsInt = qi.width;
                     json["height"].AsInt = qi.height;
                     json["resizedWidth"].AsInt = w;
                     json["resizedHeight"].AsInt = h;
                     json["format"] = fmt.ToString();
                     
                     File.WriteAllText(metaFile, json.ToString(""));
                 }
                 catch {}
             });
        }
        
        // --- Shared Logic from ImageLoadingMgr ---

        static bool Has(string source, string value)
        {
            if (source == null || value == null) return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsLikelyTorsoPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p = path;
            if (Has(p, "torso") || Has(p, "body")) return true;
            return false;
        }

        static void GetResizedSize(ref int width, ref int height, string path = null)
        {
            if (Settings.Instance == null) return;

            int originalWidth = width;
            int originalHeight = height;

            int minSize = Settings.Instance.MinTextureSize != null ? Settings.Instance.MinTextureSize.Value : 2048;
            minSize = Mathf.Clamp(minSize, 2048, 8192);

            bool forceToMin = Settings.Instance.ForceTextureToMinSize != null && Settings.Instance.ForceTextureToMinSize.Value;
            int maxSize = Settings.Instance.MaxTextureSize.Value;
            if (maxSize < minSize) maxSize = minSize;

            // Exception for Torso textures to support Genital blending
            if (originalWidth >= 4096 && IsLikelyTorsoPath(path))
            {
                if (maxSize < 4096) maxSize = 4096;
                if (minSize < 4096) minSize = 4096;
            }

            if (originalWidth != originalHeight)
            {
                int minDim = Mathf.Min(originalWidth, originalHeight);
                int maxDim = Mathf.Max(originalWidth, originalHeight);

                if (minDim <= minSize)
                {
                    width = originalWidth;
                    height = originalHeight;
                    return;
                }

                float scale = forceToMin ? ((float)minSize / maxDim) : 0.5f;

                if (!forceToMin)
                {
                    float minScale = (float)minSize / minDim;
                    if (scale < minScale) scale = minScale;
                }

                float maxScale = (float)maxSize / maxDim;
                if (scale > maxScale) scale = maxScale;

                if (scale >= 0.9999f)
                {
                    width = originalWidth;
                    height = originalHeight;
                    return;
                }

                int newWidth = Mathf.RoundToInt(originalWidth * scale);
                int newHeight = Mathf.RoundToInt(originalHeight * scale);

                newWidth = Mathf.Max(4, ((newWidth + 3) / 4) * 4);
                newHeight = Mathf.Max(4, ((newHeight + 3) / 4) * 4);

                if (newWidth > maxSize || newHeight > maxSize)
                {
                    float scale2 = Mathf.Min((float)maxSize / newWidth, (float)maxSize / newHeight);
                    newWidth = Mathf.FloorToInt(newWidth * scale2);
                    newHeight = Mathf.FloorToInt(newHeight * scale2);
                    newWidth = Mathf.Max(4, ((newWidth + 3) / 4) * 4);
                    newHeight = Mathf.Max(4, ((newHeight + 3) / 4) * 4);
                }

                width = newWidth;
                height = newHeight;
            }
            else
            {
                if (originalWidth <= minSize && originalHeight <= minSize)
                {
                    width = originalWidth;
                    height = originalHeight;
                    return;
                }

                if (forceToMin)
                {
                    width = originalWidth > minSize ? minSize : originalWidth;
                    height = originalHeight > minSize ? minSize : originalHeight;
                    width = ClosestPowerOfTwo(width);
                    height = ClosestPowerOfTwo(height);
                }
                else
                {
                    width = ClosestPowerOfTwo(width / 2);
                    height = ClosestPowerOfTwo(height / 2);
                }
                
                if (originalWidth >= minSize) width = Mathf.Max(width, minSize);
                if (originalHeight >= minSize) height = Mathf.Max(height, minSize);
                
                while (width > maxSize || height > maxSize)
                {
                    width /= 2;
                    height /= 2;
                }
            }
        }

        static int ClosestPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value) power <<= 1;
            return power;
        }

        static string GetDiskCachePath(ImageLoaderThreaded.QueuedImage qi, bool useSize, int width, int height)
        {
            string textureCacheDir = VamHookPlugin.GetCacheDir();
            if (string.IsNullOrEmpty(textureCacheDir)) return null;

            string basePath = textureCacheDir + "/";
            string fileName = Path.GetFileName(qi.imgPath);
            fileName = SanitizeFileName(fileName).Replace('.', '_');
            
            var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(qi.imgPath);
            string sizeStr = (fileEntry != null) ? fileEntry.Size.ToString() : "0";
            string timeStr = (fileEntry != null) ? fileEntry.LastWriteTime.ToFileTime().ToString() : "0";
            
            string sig = GetDiskCacheSignature(qi, useSize, width, height);
            return basePath + fileName + "_" + sizeStr + "_" + timeStr + "_" + sig;
        }

        static string GetDiskCacheSignature(ImageLoaderThreaded.QueuedImage qi, bool useSize, int width, int height)
        {
            string text = useSize ? (width + "_" + height) : "";
            if (qi.compress) text += "_C";
            if (qi.linear) text += "_L";
            if (qi.isNormalMap) text += "_N";
            if (qi.createAlphaFromGrayscale) text += "_A";
            if (qi.createNormalFromBump) text += "_BN" + qi.bumpStrength;
            if (qi.invert) text += "_I";
            if (qi.isThumbnail) text += "_T";
            return text;
        }

        static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "img";
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                sb.Append(Array.IndexOf(s_InvalidFileNameChars, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }
    }
}
