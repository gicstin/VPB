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
        
        public static void PatchAll(Harmony harmony)
        {
            try
            {
                // Patch File.ReadAllBytes to capture context
                var mReadAllBytes = AccessTools.Method(typeof(System.IO.File), "ReadAllBytes", new Type[] { typeof(string) });
                if (mReadAllBytes != null)
                {
                    harmony.Patch(mReadAllBytes, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(File_ReadAllBytes_Postfix)));
                    if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
                        LogUtil.Log("Patched File.ReadAllBytes");
                }

                // Patch Texture2D.LoadImage (2 args)
                var mLoadImage = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]), typeof(bool) });
                if (mLoadImage != null)
                {
                    harmony.Patch(mLoadImage, 
                        prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Prefix)),
                        postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Postfix)));
                    if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
                        LogUtil.Log("Patched Texture2D.LoadImage(byte[], bool)");
                }

                // Patch Texture2D.LoadImage (1 arg)
                var mLoadImageSimple = typeof(Texture2D).GetMethod("LoadImage", new Type[] { typeof(byte[]) });
                if (mLoadImageSimple != null)
                {
                    harmony.Patch(mLoadImageSimple, 
                        prefix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Prefix_Simple)),
                        postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(Texture2D_LoadImage_Postfix_Simple)));
                    if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
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
                        if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
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
                        if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
                            LogUtil.Log("Patched WWW(string)");
                    }
                    var mWWWTexture = AccessTools.Property(wwwType, "texture").GetGetMethod();
                    if (mWWWTexture != null)
                    {
                        harmony.Patch(mWWWTexture, postfix: new HarmonyMethod(typeof(GenericTextureHook), nameof(WWW_texture_Postfix)));
                        if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
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
                             if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
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
                            if (Settings.Instance != null && Settings.Instance.LogStartupDetails != null && Settings.Instance.LogStartupDetails.Value)
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

        private static long ExpectedRawDataSize(int width, int height, TextureFormat format)
        {
            try
            {
                if (width <= 0 || height <= 0) return -1;

                long w = width;
                long h = height;

                switch (format)
                {
                    case TextureFormat.Alpha8: return w * h;
                    case TextureFormat.RGB24: return w * h * 3;
                    case TextureFormat.RGBA32: return w * h * 4;
                    case TextureFormat.ARGB32: return w * h * 4;

                    case TextureFormat.RGB565: return w * h * 2;
                    case TextureFormat.RGBA4444: return w * h * 2;

                    case TextureFormat.DXT1:
                        {
                            long bw = (w + 3) / 4;
                            long bh = (h + 3) / 4;
                            return bw * bh * 8;
                        }
                    case TextureFormat.DXT5:
                        {
                            long bw = (w + 3) / 4;
                            long bh = (h + 3) / 4;
                            return bw * bh * 16;
                        }

                    default:
                        return -1;
                }
            }
            catch
            {
                return -1;
            }
        }

        public static void Texture2D_LoadRawTextureData_Prefix(Texture2D __instance, byte[] data)
        {
            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
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
            if (ImageLoadingMgr.singleton == null || Settings.Instance == null || !Settings.Instance.EnableZstdCompression.Value) return;

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
            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                LogUtil.Log("WWW Ctor: " + url);
        }

        public static void WWW_texture_Postfix(WWW __instance, Texture2D __result)
        {
            if (__instance == null || __result == null) return;
            string url = __instance.url;
            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
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
                     if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                         LogUtil.Log("Captured WWW texture: " + path);
                 }
            }
        }

        public static void File_ReadAllBytes_Postfix(string __0, byte[] __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__0)) return;
                
                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
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
            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
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

            if (ImageLoadingMgr.singleton == null || Settings.Instance == null || !Settings.Instance.EnableZstdCompression.Value)
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
            if (!string.IsNullOrEmpty(__state) && Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                LogUtil.Log("Texture2D_LoadImage_Postfix " + __state);
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
                     if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
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
            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
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
            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                LogUtil.Log("ImageConversion_LoadImage_Prefix");
            return Texture2D_LoadImage_Prefix(tex, data, markNonReadable, ref __result, out __state);
        }

        public static void ImageConversion_LoadImage_Postfix(Texture2D tex, bool __result, string __state)
        {
            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                LogUtil.Log("ImageConversion_LoadImage_Postfix success=" + __result);
            Texture2D_LoadImage_Postfix(tex, __state);
        }

        private static bool TryLoadFromCache(Texture2D tex, string path, bool markNonReadable)
        {
            if (Settings.Instance == null || !Settings.Instance.EnableZstdCompression.Value) return false;

            try
            {
                var qi = new ImageLoaderThreaded.QueuedImage();
                qi.imgPath = path;
                qi.tex = tex;
                qi.compress = true;
                qi.linear = false; 

                string baseZstdPath = TextureUtil.GetZstdCachePath(qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert, 0, 0, qi.bumpStrength);
                if (string.IsNullOrEmpty(baseZstdPath)) return false;

                string metaPath = baseZstdPath + "meta";
                if (!File.Exists(metaPath))
                {
                    return false;
                }

                try
                {
                    var json = JSON.Parse(File.ReadAllText(metaPath));
                    int w = json["width"].AsInt;
                    int h = json["height"].AsInt;
                    int targetW = w;
                    int targetH = h;
                    string type = json["type"];
                    string realCacheFile = baseZstdPath;

                    if (File.Exists(realCacheFile))
                    {
                        if (Settings.Instance.TextureLogLevel.Value >= 2) LogUtil.Log("Cache HIT: " + realCacheFile + " for " + path);
                        byte[] fileBytes = File.ReadAllBytes(realCacheFile);
                        byte[] bytes = null;
                        
                        // Zstd files should always be decompressed regardless of 'type' in meta, 
                        // but we check for .zvamcache extension or 'compressed' type
                        if (realCacheFile.EndsWith(".zvamcache") || type == "compressed")
                        {
                            try 
                            { 
                                bytes = ZstdCompressor.Decompress(fileBytes); 
                                if (bytes == null) {
                                    LogUtil.LogError("Zstd decompression returned null for: " + realCacheFile);
                                    return false;
                                }
                            }
                            catch (Exception ex) { LogUtil.LogError("Zstd decompress fail in hook: " + ex.Message); return false; }
                        }
                        else bytes = fileBytes;

                        if (bytes != null)
                        {
                            try
                            {
                                TextureFormat tf = TextureFormat.DXT5;
                                if (json["format"] != null)
                                {
                                    try { tf = (TextureFormat)Enum.Parse(typeof(TextureFormat), json["format"]); } catch { }
                                }

                                long expected = ExpectedRawDataSize(targetW, targetH, tf);
                                if (expected > 0 && bytes.Length != (int)expected)
                                {
                                    LogUtil.LogWarning("Cache raw data size mismatch for ", path);
                                    return false;
                                }

                                if (tex.width != targetW || tex.height != targetH)
                                {
                                    tex.Resize(targetW, targetH, tf, false);
                                }

                                tex.LoadRawTextureData(bytes);
                                tex.Apply(false, !markNonReadable);

                                if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2)
                                    LogUtil.Log("Successfully loaded from cache: " + path);

                                return true;
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError("TryLoadFromCache failed to apply cached texture: " + ex.Message);
                                return false;
                            }
                        }
                        }
                    else
                    {
                        // LogUtil.Log("Cache file missing: " + realCacheFile);
                    }
                }
                catch (Exception ex) { LogUtil.LogError("TryLoadFromCache error parsing meta: " + ex.Message); }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("GenericTextureHook TryLoadFromCache Error: " + ex);
            }
            return false;
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