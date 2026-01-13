using System.Drawing;
using System.Drawing.Imaging;
using SimpleJSON;
using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace VPB
{
    public static class GenericTextureHook
    {
        [ThreadStatic]
        private static string _lastReadImagePath;

        private class BufferPathMap
        {
            private readonly Dictionary<byte[], string> _map = new Dictionary<byte[], string>();
            private readonly List<byte[]> _keys = new List<byte[]>();
            private const int MaxCapacity = 500;

            public void Add(byte[] data, string path)
            {
                lock (_map)
                {
                    if (_map.ContainsKey(data)) return;
                    
                    if (_keys.Count >= MaxCapacity)
                    {
                        var oldest = _keys[0];
                        _keys.RemoveAt(0);
                        _map.Remove(oldest);
                    }
                    
                    _map[data] = path;
                    _keys.Add(data);
                }
            }

            public bool TryGetValue(byte[] data, out string path)
            {
                lock (_map)
                {
                    return _map.TryGetValue(data, out path);
                }
            }

            public void Remove(byte[] data)
            {
                lock (_map)
                {
                    if (_map.Remove(data))
                    {
                        _keys.Remove(data);
                    }
                }
            }
        }

        private static BufferPathMap _dataToPath = new BufferPathMap();
        
        static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();
        private static NativeKtx _nativeKtx = new NativeKtx();

        public static void PatchAll(Harmony harmony)
        {
            try
            {
                // Patch File.ReadAllBytes to capture context
                var mReadAllBytes = AccessTools.Method(typeof(System.IO.File), "ReadAllBytes", new Type[] { typeof(string) });
                if (mReadAllBytes != null)
                {
                    harmony.Patch(mReadAllBytes, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(File_ReadAllBytes_Postfix)));
                    LogUtil.Log("Patched File.ReadAllBytes");
                }

                // Patch Texture2D.LoadImage (2 args)
                var mLoadImage = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]), typeof(bool) });
                if (mLoadImage != null)
                {
                    harmony.Patch(mLoadImage, 
                        prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Prefix)),
                        postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Postfix)));
                    LogUtil.Log("Patched Texture2D.LoadImage(byte[], bool)");
                }

                // Patch Texture2D.LoadImage (1 arg)
                var mLoadImageSimple = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]) });
                if (mLoadImageSimple != null)
                {
                    harmony.Patch(mLoadImageSimple, 
                        prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Prefix_Simple)),
                        postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Postfix_Simple)));
                    LogUtil.Log("Patched Texture2D.LoadImage(byte[])");
                }

                // Patch ImageConversion.LoadImage (Unity 2017+)
                var imgConvType = AccessTools.TypeByName("UnityEngine.ImageConversion");
                if (imgConvType != null)
                {
                    var mLoadImageIC = imgConvType.GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                    if (mLoadImageIC != null)
                    {
                        harmony.Patch(mLoadImageIC, 
                            prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(ImageConversion_LoadImage_Prefix)),
                            postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(ImageConversion_LoadImage_Postfix)));
                        LogUtil.Log("Patched ImageConversion.LoadImage");
                    }
                }
                
                // Patch Resources.Load
                var mResourcesLoad = AccessTools.Method(typeof(Resources), "Load", new Type[] { typeof(string) });
                if (mResourcesLoad != null)
                {
                    harmony.Patch(mResourcesLoad, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Resources_Load_Prefix)));
                }

                // Patch WWW constructor and texture property
                try 
                {
                    var wwwType = typeof(WWW);
                    var mWWWCtor = wwwType.GetConstructor(new Type[] { typeof(string) });
                    if (mWWWCtor != null)
                    {
                        harmony.Patch(mWWWCtor, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(WWW_Ctor_Postfix)));
                        LogUtil.Log("Patched WWW(string)");
                    }
                    var mWWWTexture = AccessTools.Property(wwwType, "texture").GetGetMethod();
                    if (mWWWTexture != null)
                    {
                        harmony.Patch(mWWWTexture, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(WWW_texture_Postfix)));
                        LogUtil.Log("Patched WWW.texture");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Failed to patch WWW: " + ex);
                }

                // Patch UnityWebRequest (if available)
                try
                {
                    var uwrType = AccessTools.TypeByName("UnityEngine.Networking.UnityWebRequest");
                    if (uwrType != null)
                    {
                         // Patch UnityWebRequest.Get(string)
                         var mGet = uwrType.GetMethod("Get", new Type[] { typeof(string) });
                         if (mGet != null)
                         {
                             harmony.Patch(mGet, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(UnityWebRequest_Get_Postfix)));
                             LogUtil.Log("Patched UnityWebRequest.Get");
                         }
                    }

                    var dhtType = AccessTools.TypeByName("UnityEngine.Networking.DownloadHandlerTexture");
                    if (dhtType != null)
                    {
                        // Patch DownloadHandlerTexture.texture property
                        var mTex = AccessTools.Property(dhtType, "texture").GetGetMethod();
                        if (mTex != null)
                        {
                            harmony.Patch(mTex, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(DownloadHandlerTexture_texture_Postfix)));
                            LogUtil.Log("Patched DownloadHandlerTexture.texture");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Failed to patch UnityWebRequest: " + ex);
                }

                // Patch FileManager.ReadAllBytes
                try
                {
                    var fmType = typeof(MVR.FileManagement.FileManager);
                    var mFmReadAllBytes = AccessTools.Method(fmType, "ReadAllBytes", new Type[] { typeof(string), typeof(bool) });
                    if (mFmReadAllBytes != null)
                    {
                        harmony.Patch(mFmReadAllBytes, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(File_ReadAllBytes_Postfix)));
                    }

                    var mFmReadAllBytesFe = AccessTools.Method(fmType, "ReadAllBytes", new Type[] { AccessTools.TypeByName("MVR.FileManagement.FileEntry") });
                    if (mFmReadAllBytesFe != null)
                    {
                        harmony.Patch(mFmReadAllBytesFe, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(File_ReadAllBytes_FileEntry_Postfix)));
                    }

                    var mFmReadAllBytesCoroutine = AccessTools.Method(fmType, "ReadAllBytesCoroutine", new Type[] { AccessTools.TypeByName("MVR.FileManagement.FileEntry"), typeof(byte[]) });
                    if (mFmReadAllBytesCoroutine != null)
                    {
                        harmony.Patch(mFmReadAllBytesCoroutine, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(File_ReadAllBytesCoroutine_Prefix)));
                    }
                }
                catch { }

                // Patch Texture2D.Apply
                var mApply = typeof(Texture2D).GetMethod("Apply", new Type[] { typeof(bool), typeof(bool) });
                if (mApply != null)
                {
                    harmony.Patch(mApply, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_Apply_Prefix)));
                }

                // Patch Texture2D.Compress
                var mCompress = typeof(Texture2D).GetMethod("Compress", new Type[] { typeof(bool) });
                if (mCompress != null)
                {
                    harmony.Patch(mCompress, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_Compress_Prefix)));
                }

                // Patch Texture2D.LoadRawTextureData
                var mLoadRaw = typeof(Texture2D).GetMethod("LoadRawTextureData", new Type[] { typeof(byte[]) });
                if (mLoadRaw != null)
                {
                    harmony.Patch(mLoadRaw, prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadRawTextureData_Prefix)));
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("GenericTextureHook PatchAll failed: " + ex);
            }
        }

        public static void Texture2D_LoadRawTextureData_Prefix(Texture2D __instance, byte[] data)
        {
            LogUtil.Log("Texture2D_LoadRawTextureData_Prefix");
            if (ImageLoadingMgr.singleton == null) return;

            string path = null;
            if (data != null)
            {
                _dataToPath.TryGetValue(data, out path);
            }

            if (!string.IsNullOrEmpty(path))
            {
                var candidate = ImageLoadingMgr.singleton.FindCandidateByPath(path);
                if (candidate != null)
                {
                    if (candidate.tex == null) candidate.tex = __instance;
                    ImageLoadingMgr.singleton.TryEnqueueResizeCache(candidate);
                }
            }

            CaptureCandidateTexture(__instance, "LoadRawTextureData");
        }

        public static void Texture2D_Apply_Prefix(Texture2D __instance, bool updateMipmaps, bool makeNoLongerReadable)
        {
            if (!makeNoLongerReadable) return;
            CaptureCandidateTexture(__instance, "Apply(unreadable)");
        }

        public static void Texture2D_Compress_Prefix(Texture2D __instance, bool highQuality)
        {
            CaptureCandidateTexture(__instance, "Compress");
        }

        private static void CaptureCandidateTexture(Texture2D tex, string reason)
        {
            if (ImageLoadingMgr.singleton == null || Settings.Instance == null || !Settings.Instance.EnableTextureOptimizations.Value) return;

            var qi = ImageLoadingMgr.singleton.FindCandidateByTexture(tex);
            if (qi == null && !string.IsNullOrEmpty(tex.name))
            {
                qi = ImageLoadingMgr.singleton.FindCandidateByPath(tex.name);
                if (qi != null && qi.tex == null) qi.tex = tex;
            }

            if (qi != null)
            {
                ImageLoadingMgr.singleton.TryEnqueueResizeCache(qi);
            }
        }

        public static void WWW_Ctor_Postfix(WWW __instance, string url)
        {
            LogUtil.Log("WWW Ctor: " + url);
        }

        public static void WWW_texture_Postfix(WWW __instance, Texture2D __result)
        {
            if (__instance == null || __result == null) return;
            string url = __instance.url;
            LogUtil.Log("WWW.texture accessed: " + url);
            
            if (string.IsNullOrEmpty(url)) return;
            
            // Clean up file:// prefix if present
            string path = url;
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(7);
            }
            else if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(5);
            }

            if (ImageLoadingMgr.singleton != null)
            {
                 var qi = new ImageLoaderThreaded.QueuedImage();
                 qi.imgPath = path;
                 qi.tex = __result;
                 qi.compress = true;
                 if (ImageLoadingMgr.singleton.TryEnqueueResizeCache(qi))
                 {
                     LogUtil.Log("Captured WWW texture: " + path);
                 }
            }
        }

        public static void File_ReadAllBytes_Postfix(string __0, byte[] __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__0)) return;
                
                LogUtil.Log("File_ReadAllBytes: " + __0);

                if (__0.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                    __0.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    __0.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    _lastReadImagePath = __0;
                    if (__result != null)
                    {
                        _dataToPath.Remove(__result);
                        _dataToPath.Add(__result, __0);
                    }
                }
                else
                {
                    _lastReadImagePath = null;
                }
            }
            catch { }
        }

        public static void File_ReadAllBytes_FileEntry_Postfix(object __0, byte[] __result)
        {
            try
            {
                if (__0 == null) return;
                var path = Traverse.Create(__0).Property("Path").GetValue() as string;
                if (string.IsNullOrEmpty(path))
                    path = Traverse.Create(__0).Field("Path").GetValue() as string;
                
                if (!string.IsNullOrEmpty(path))
                {
                    File_ReadAllBytes_Postfix(path, __result);
                }
            }
            catch { }
        }

        public static void File_ReadAllBytesCoroutine_Prefix(object __0, byte[] __1)
        {
            try
            {
                if (__0 == null || __1 == null) return;
                var path = Traverse.Create(__0).Property("Path").GetValue() as string;
                if (string.IsNullOrEmpty(path))
                    path = Traverse.Create(__0).Field("Path").GetValue() as string;
                
                if (!string.IsNullOrEmpty(path))
                {
                    _dataToPath.Remove(__1);
                    _dataToPath.Add(__1, path);
                }
            }
            catch { }
        }

        public static void Resources_Load_Prefix(string path)
        {
             // Placeholder for monitoring
        }

        public static bool Texture2D_LoadImage_Prefix(Texture2D __instance, byte[] data, bool markNonReadable, ref bool __result, out string __state)
        {
            LogUtil.Log("Texture2D_LoadImage_Prefix " + (__instance != null ? __instance.name : "null"));
            
            string path = _lastReadImagePath;
            _lastReadImagePath = null; // Consume context

            if (string.IsNullOrEmpty(path))
            {
                path = ImageLoadingMgr.currentProcessingPath;
            }

            if (string.IsNullOrEmpty(path) && data != null)
            {
                _dataToPath.TryGetValue(data, out path);
            }

            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(__instance.name))
            {
                // VaM often sets texture name to path
                if (__instance.name.Contains("/") || __instance.name.Contains("\\") || __instance.name.Contains(":"))
                {
                    path = __instance.name;
                }
            }

            if (!string.IsNullOrEmpty(path) && ImageLoadingMgr.singleton != null)
            {
                var candidate = ImageLoadingMgr.singleton.FindCandidateByPath(path);
                if (candidate != null && candidate.tex == null)
                {
                    candidate.tex = __instance;
                }
            }
            
            __state = path;

            if (ImageLoadingMgr.singleton == null || Settings.Instance == null || !Settings.Instance.EnableTextureOptimizations.Value)
            {
                return true;
            }

            // Only proceed if either Resize or KTX is enabled
            if (!Settings.Instance.ReduceTextureSize.Value && !Settings.Instance.EnableKtxCompression.Value)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(path))
            {
                if (TryLoadFromCache(__instance, path, markNonReadable))
                {
                    __result = true;
                    __state = null; // Don't re-cache
                    return false; // Skip original
                }
            }
            return true;
        }

        public static void Texture2D_LoadImage_Postfix(Texture2D __instance, string __state)
        {
            if (!string.IsNullOrEmpty(__state)) LogUtil.Log("Texture2D_LoadImage_Postfix " + __state);
            // Unity 2018 LoadImage can return void in some versions/overloads, or bool. 
            // Harmony handles void by not providing __result, or we check if it succeeded.
            // But if the signature returns bool, we should respect it.
            // However, most importantly, if the texture is loaded, we want to capture it.
            
            if (string.IsNullOrEmpty(__state)) return;
            
            if (ImageLoadingMgr.singleton != null)
            {
                var qi = new ImageLoaderThreaded.QueuedImage();
                qi.imgPath = __state;
                qi.tex = __instance;
                qi.compress = true;
                
                // Directly tracking it as a new candidate if it wasn't one already
                if (ImageLoadingMgr.singleton.TryEnqueueResizeCache(qi))
                {
                     LogUtil.Log("Texture2D_LoadImage_Postfix Enqueued: " + __state);
                }
                else
                {
                     // This might happen if it's already cached or settings disabled
                }
            }
        }

        public static bool Texture2D_LoadImage_Prefix_Simple(Texture2D __instance, byte[] data, out string __state)
        {
            LogUtil.Log("Texture2D_LoadImage_Prefix_Simple");
            // In simple version (no bool return ref), we just pass dummy ref
            bool dummy = false;
            return Texture2D_LoadImage_Prefix(__instance, data, false, ref dummy, out __state);
        }

        public static void Texture2D_LoadImage_Postfix_Simple(Texture2D __instance, string __state)
        {
            Texture2D_LoadImage_Postfix(__instance, __state);
        }

        public static bool ImageConversion_LoadImage_Prefix(Texture2D tex, byte[] data, bool markNonReadable, ref bool __result, out string __state)
        {
            LogUtil.Log("ImageConversion_LoadImage_Prefix");
            return Texture2D_LoadImage_Prefix(tex, data, markNonReadable, ref __result, out __state);
        }

        public static void ImageConversion_LoadImage_Postfix(Texture2D tex, bool __result, string __state)
        {
            LogUtil.Log("ImageConversion_LoadImage_Postfix success=" + __result);
            Texture2D_LoadImage_Postfix(tex, __state);
        }

        private static bool TryLoadFromCache(Texture2D tex, string path, bool markNonReadable)
        {
            if (Settings.Instance == null || !Settings.Instance.EnableTextureOptimizations.Value) return false;
            
            bool enableKtx = Settings.Instance.EnableKtxCompression.Value;
            bool enableResize = Settings.Instance.ReduceTextureSize.Value;

            try
            {
                var qi = new ImageLoaderThreaded.QueuedImage();
                qi.imgPath = path;
                qi.tex = tex;
                qi.compress = true;
                qi.linear = false; 

                // 1. Check Disk Cache (KTX Priority)
                if (enableKtx)
                {
                    string ktxBase = GetDiskCachePath(qi, false, 0, 0);
                    if (!string.IsNullOrEmpty(ktxBase))
                    {
                        string metaPath = ktxBase + ".meta";
                        if (File.Exists(metaPath))
                        {
                            try
                            {
                                var json = JSON.Parse(File.ReadAllText(metaPath));
                                int w = json["width"].AsInt;
                                int h = json["height"].AsInt;
                                int targetW = w;
                                int targetH = h;
                                
                                if (Settings.Instance.ReduceTextureSize.Value)
                                {
                                    GetResizedSize(ref targetW, ref targetH, path);
                                }
                                
                                string type = json["type"];

                                if (type == "ktx" && targetW > 0 && targetH > 0)
                                {
                                    string realCachePath = GetDiskCachePath(qi, true, targetW, targetH);
                                    string ktxFile = realCachePath + ".ktx2";

                                    if (File.Exists(ktxFile))
                                    {
                                        _nativeKtx.Initialize();
                                        var chain = _nativeKtx.ReadDxtFromKtx(ktxFile);
                                        if (chain != null)
                                        {
                                            TextureFormat tf = TextureFormat.DXT1;
                                            if (chain.format == KtxTestFormat.Dxt1) tf = TextureFormat.DXT1;
                                            else if (chain.format == KtxTestFormat.Dxt5) tf = TextureFormat.DXT5;
                                            else if (chain.format == KtxTestFormat.Rgb24) tf = TextureFormat.RGB24;
                                            else if (chain.format == KtxTestFormat.Rgba32) tf = TextureFormat.RGBA32;

                                            if (tex.width != chain.width || tex.height != chain.height)
                                                tex.Resize(chain.width, chain.height, tf, false);
                                            
                                            tex.LoadRawTextureData(chain.data);
                                            tex.Apply(false, !markNonReadable);
                                            return true;
                                        }
                                    }
                                }
                            }
                            catch {}
                        }
                    }
                }

                // Check Standard Cache (Fallback)
                string cachePath = GetDiskCachePath(qi, false, 0, 0);
                if (!string.IsNullOrEmpty(cachePath))
                {
                    string metaPath = cachePath + ".meta";
                    if (File.Exists(metaPath))
                    {
                         try
                         {
                             var json = JSON.Parse(File.ReadAllText(metaPath));
                             int w = json["width"].AsInt;
                             int h = json["height"].AsInt;
                             int targetW = w;
                             int targetH = h;
                             
                             if (Settings.Instance.ReduceTextureSize.Value)
                             {
                                 GetResizedSize(ref targetW, ref targetH, path);
                             }
                             
                             if (targetW > 0 && targetH > 0)
                             {
                                 string realCachePath = GetDiskCachePath(qi, true, targetW, targetH);
                                 string realCacheFile = realCachePath + ".cache";
                                 if (!File.Exists(realCacheFile)) realCacheFile = realCachePath; 

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
            }
            catch (Exception ex)
            {
                LogUtil.LogError("GenericTextureHook Error: " + ex);
            }
            return false;
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
                    width = originalWidth;
                    height = originalHeight;

                    if (originalWidth > minSize)
                        width = minSize;
                    if (originalHeight > minSize)
                        height = minSize;

                    width = ClosestPowerOfTwo(width);
                    height = ClosestPowerOfTwo(height);
                }
                else
                {
                    width = ClosestPowerOfTwo(width / 2);
                    height = ClosestPowerOfTwo(height / 2);

                    if (originalWidth >= minSize) width = Mathf.Max(width, minSize);
                    if (originalHeight >= minSize) height = Mathf.Max(height, minSize);
                }
                
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
            string text = (fileEntry != null) ? fileEntry.Size.ToString() : "0";
            string token = (fileEntry != null) ? fileEntry.LastWriteTime.ToFileTime().ToString() : "0";

            string signature = useSize ? (width + "_" + height) : "";
            if (qi.compress) signature += "_C";

            return basePath + fileName + "_" + text + "_" + token + "_" + signature;
        }
        
        static string SanitizeFileName(string value)
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
        public static void UnityWebRequest_Get_Postfix(object __result, string uri)
        {
             LogUtil.Log("UnityWebRequest.Get: " + uri);
        }

        public static void DownloadHandlerTexture_texture_Postfix(object __instance, Texture2D __result)
        {
             // We can't easily get the URL here without extra tracking.
             // Just log for now to see if it's used.
             if (__result != null)
             {
                LogUtil.Log("DownloadHandlerTexture.texture accessed: " + __result.name);
             }
        }
    }
}