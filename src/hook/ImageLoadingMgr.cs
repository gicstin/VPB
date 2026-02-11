using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SimpleJSON;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Manages Zstd compression and loading from VPB_Cache.
    /// </summary>
    public class ImageLoadingMgr : MonoBehaviour
    {
        public static ImageLoadingMgr singleton;
        public static string currentProcessingPath;
        public static bool currentProcessingIsThumbnail;
        
        private void Awake()
        {
            singleton = this;
        }

        Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
        Dictionary<string, List<ImageLoaderThreaded.QueuedImage>> inflightWaiters = new Dictionary<string, List<ImageLoaderThreaded.QueuedImage>>();
        HashSet<string> inflightKeys = new HashSet<string>();
        private readonly object textureCacheLock = new object();
        private readonly object inflightLock = new object();
        
        private class DecompressedData
        {
            public string CacheKey;
            public byte[] Data;
            public MetadataEntry Meta;
            public ImageLoaderThreaded.QueuedImage OriginalQI;
        }
        
        private class MetadataEntry
        {
            public int Width;
            public int Height;
            public TextureFormat Format;
            public bool IsDownscaled;
        }
        
        private class CachedDecompressed
        {
            public byte[] Data;
            public LinkedListNode<string> LRUNode;
        }
        
        private Dictionary<string, MetadataEntry> metadataCache = new Dictionary<string, MetadataEntry>();
        private readonly object metadataCacheLock = new object();
        
        private Dictionary<string, CachedDecompressed> decompressedCache = new Dictionary<string, CachedDecompressed>();
        private LinkedList<string> lruOrder = new LinkedList<string>();
        private readonly object decompressedCacheLock = new object();
        
        private Dictionary<string, string> cachePathMap = new Dictionary<string, string>();
        private readonly object cachePathMapLock = new object();
        
        private const int MaxMemoryCacheBytes = 512 * 1024 * 1024;
        private long currentMemoryUsage = 0;

        private static string GetDownscaledKey(string cacheKey)
        {
            return "ILM:" + (cacheKey ?? string.Empty);
        }

        private string GetCachePath(ImageLoaderThreaded.QueuedImage qi)
        {
            string pathKey = qi.imgPath + "|" + qi.compress + "|" + qi.linear + "|" + qi.isNormalMap + "|" + qi.createAlphaFromGrayscale + "|" + qi.createNormalFromBump + "|" + qi.invert + "|" + (qi.setSize ? qi.width : 0) + "|" + (qi.setSize ? qi.height : 0) + "|" + qi.bumpStrength;

            lock (cachePathMapLock)
            {
                if (cachePathMap.TryGetValue(pathKey, out var cached))
                {
                    if (cached != null && File.Exists(cached)) return cached;
                    cachePathMap.Remove(pathKey);
                }
            }

            string vpbCachePath = TextureUtil.GetZstdCachePath(qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert, qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength);

            if (vpbCachePath != null && !File.Exists(vpbCachePath) && qi.setSize)
            {
                string vpbCachePathDefault = TextureUtil.GetZstdCachePath(qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert, 0, 0, qi.bumpStrength);
                if (File.Exists(vpbCachePathDefault))
                {
                    vpbCachePath = vpbCachePathDefault;
                }
            }

            if (vpbCachePath != null && File.Exists(vpbCachePath))
            {
                lock (cachePathMapLock)
                {
                    cachePathMap[pathKey] = vpbCachePath;
                }
                return vpbCachePath;
            }

            return null;
        }

        public void ClearCache()
        {
            TextureUtil.UnmarkDownscaledActiveByPrefix("ILM:");
            lock (metadataCacheLock)
            {
                metadataCache.Clear();
            }
            lock (decompressedCacheLock)
            {
                decompressedCache.Clear();
                lruOrder.Clear();
                currentMemoryUsage = 0;
            }
            lock (cachePathMapLock)
            {
                cachePathMap.Clear();
            }
            lock (textureCacheLock)
            {
                textureCache.Clear();
            }
            lock (inflightLock)
            {
                inflightKeys.Clear();
                inflightWaiters.Clear();
            }
        }
        
        private MetadataEntry FastLoadMetadata(string cachePath)
        {
            lock (metadataCacheLock)
            {
                if (metadataCache.TryGetValue(cachePath, out var cached))
                {
                    return cached;
                }
            }

            try
            {
                string metaPath = cachePath + "meta";
                
                byte[] metaBytes;
                try { metaBytes = File.ReadAllBytes(metaPath); }
                catch { return null; }
                
                var metaJson = JSON.Parse(System.Text.Encoding.UTF8.GetString(metaBytes));
                
                int w = metaJson["width"].AsInt;
                int h = metaJson["height"].AsInt;
                TextureFormat fmt = TextureFormat.RGBA32;
                try { fmt = (TextureFormat)Enum.Parse(typeof(TextureFormat), metaJson["format"].Value); } catch { }
                
                bool isDown = false;
                try { isDown = metaJson["downscaled"].AsBool; } catch { }

                var entry = new MetadataEntry
                {
                    Width = w,
                    Height = h,
                    Format = fmt,
                    IsDownscaled = isDown
                };

                lock (metadataCacheLock)
                {
                    metadataCache[cachePath] = entry;
                }

                return entry;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("FastLoadMetadata failed: " + ex.Message);
                return null;
            }
        }

        private byte[] FastGetDecompressed(string cachePath)
        {
            lock (decompressedCacheLock)
            {
                if (decompressedCache.TryGetValue(cachePath, out var cached))
                {
                    if (cached.LRUNode != null)
                    {
                        lruOrder.Remove(cached.LRUNode);
                        cached.LRUNode = lruOrder.AddFirst(cachePath);
                    }
                    return cached.Data;
                }
            }

            try
            {
                byte[] compressedData;
                try { compressedData = File.ReadAllBytes(cachePath); }
                catch { return null; }
                
                byte[] decompressedData = ZstdCompressor.Decompress(compressedData);
                
                if (decompressedData == null || decompressedData.Length == 0)
                    return null;

                lock (decompressedCacheLock)
                {
                    if (decompressedCache.ContainsKey(cachePath))
                    {
                        return decompressedCache[cachePath].Data;
                    }

                    while (currentMemoryUsage + decompressedData.Length > MaxMemoryCacheBytes)
                    {
                        EvictLRU();
                    }

                    var node = lruOrder.AddFirst(cachePath);
                    decompressedCache[cachePath] = new CachedDecompressed { Data = decompressedData, LRUNode = node };
                    currentMemoryUsage += decompressedData.Length;
                }

                return decompressedData;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("FastGetDecompressed failed: " + ex.Message);
                return null;
            }
        }

        private void EvictLRU()
        {
            if (lruOrder.Count == 0) return;

            var lruKey = lruOrder.Last.Value;
            lruOrder.RemoveLast();
            
            if (decompressedCache.TryGetValue(lruKey, out var entry))
            {
                currentMemoryUsage -= entry.Data?.Length ?? 0;
                decompressedCache.Remove(lruKey);
            }
        }

        public Texture2D GetTextureFromCache(string path)
        {
            lock (textureCacheLock)
            {
                if (textureCache.TryGetValue(path, out var tex))
                {
                    if (tex != null) return tex;
                    TextureUtil.UnmarkDownscaledActive(GetDownscaledKey(path));
                    textureCache.Remove(path);
                }
                return null;
            }
        }

        void RegisterTexture(string path, Texture2D tex)
        {
            if (string.IsNullOrEmpty(path) || tex == null) return;
            TextureUtil.UnmarkDownscaledActive(GetDownscaledKey(path));
            lock (textureCacheLock)
            {
                textureCache[path] = tex;
            }
        }

        public void DoCallback(ImageLoaderThreaded.QueuedImage qi)
        {
            try
            {
                if (qi.rawImageToLoad != null)
                {
                    qi.rawImageToLoad.texture = qi.tex;
                }

                if (qi.callback != null)
                {
                    qi.callback(qi);
                    qi.callback = null;
                }
            }
            catch(System.Exception ex)
            {
                LogUtil.LogError("DoCallback "+qi.imgPath+" "+ex.ToString());
            }
        }

        WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();
        IEnumerator DelayDoCallback(ImageLoaderThreaded.QueuedImage qi)
        {
            yield return waitForEndOfFrame;
            yield return waitForEndOfFrame;
            DoCallback(qi);
        }

        public bool Request(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return false;
            
            LogUtil.MarkImageActivity();

            int threshold = Settings.Instance.ThumbnailThreshold.Value;
            if (qi.setSize && qi.width > 0 && qi.width <= threshold && qi.height > 0 && qi.height <= threshold)
                return false;

            string cacheKey = qi.imgPath + (qi.linear ? "_L" : "");
            
            var cacheTexture = GetTextureFromCache(cacheKey);
            if (cacheTexture != null)
            {
                qi.tex = cacheTexture;
                if (Messager.singleton != null)
                {
                    Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                }
                else
                {
                    DoCallback(qi);
                }
                return true;
            }

            lock (inflightLock)
            {
                if (inflightKeys.Contains(cacheKey))
                {
                    if (!inflightWaiters.TryGetValue(cacheKey, out var waiters))
                    {
                        waiters = new List<ImageLoaderThreaded.QueuedImage>();
                        inflightWaiters[cacheKey] = waiters;
                    }
                    waiters.Add(qi);
                    return true;
                }

                string vpbCachePath = GetCachePath(qi);
                if (vpbCachePath != null)
                {
                    inflightKeys.Add(cacheKey);
                    ThreadPool.QueueUserWorkItem((state) => LoadAndDecompressBackground(cacheKey, vpbCachePath, qi));
                    return true;
                }

                return false;
            }
        }

        private void LoadAndDecompressBackground(string cacheKey, string cachePath, ImageLoaderThreaded.QueuedImage qi)
        {
            try
            {
                var meta = FastLoadMetadata(cachePath);
                if (meta == null)
                {
                    lock (inflightLock)
                    {
                        inflightKeys.Remove(cacheKey);
                    }
                    return;
                }

                byte[] decompressedData = FastGetDecompressed(cachePath);
                if (decompressedData == null)
                {
                    lock (inflightLock)
                    {
                        inflightKeys.Remove(cacheKey);
                    }
                    return;
                }

                if (meta.Width <= 0 || meta.Height <= 0)
                {
                    LogUtil.LogError($"LoadAndDecompress: Invalid texture dimensions {meta.Width}x{meta.Height} for {cachePath}");
                    lock (inflightLock)
                    {
                        inflightKeys.Remove(cacheKey);
                    }
                    return;
                }

                int expectedSize = TextureUtil.GetExpectedRawDataSize(meta.Width, meta.Height, meta.Format);
                if (expectedSize > 0 && decompressedData.Length < expectedSize)
                {
                    LogUtil.LogError($"LoadAndDecompress: Decompressed data size mismatch. Expected {expectedSize}, got {decompressedData.Length} for {cachePath}");
                    lock (inflightLock)
                    {
                        inflightKeys.Remove(cacheKey);
                    }
                    return;
                }

                var decompressed = new DecompressedData
                {
                    CacheKey = cacheKey,
                    Data = decompressedData,
                    Meta = meta,
                    OriginalQI = qi
                };

                if (Messager.singleton != null)
                {
                    Messager.singleton.StartCoroutine(CreateTextureOnMainThread(decompressed));
                }
                else
                {
                    CreateTexture(decompressed);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("LoadAndDecompressBackground failed for " + cachePath + ": " + ex.Message);
                lock (inflightLock)
                {
                    inflightKeys.Remove(cacheKey);
                }
            }
        }

        private IEnumerator CreateTextureOnMainThread(DecompressedData data)
        {
            yield return waitForEndOfFrame;
            CreateTexture(data);
        }

        private void CreateTexture(DecompressedData data)
        {
            try
            {
                Texture2D tex = new Texture2D(data.Meta.Width, data.Meta.Height, data.Meta.Format, false, data.OriginalQI.linear);
                TextureUtil.SafeLoadRawTextureData(tex, data.Data, data.Meta.Width, data.Meta.Height, data.Meta.Format);
                tex.Apply(false, true);
                data.OriginalQI.tex = tex;

                RegisterTexture(data.CacheKey, tex);
                if (data.Meta.IsDownscaled) 
                    TextureUtil.MarkDownscaledActive(GetDownscaledKey(data.CacheKey));
                
                ResolveInflight(data.CacheKey, tex);
                if (Messager.singleton != null)
                {
                    Messager.singleton.StartCoroutine(DelayDoCallback(data.OriginalQI));
                }
                else
                {
                    DoCallback(data.OriginalQI);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("CreateTexture failed for " + data.CacheKey + ": " + ex.Message);
            }
            finally
            {
                lock (inflightLock)
                {
                    inflightKeys.Remove(data.CacheKey);
                }
            }
        }

        public void ResolveInflightForQueuedImage(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            string cacheKey = qi.imgPath + (qi.linear ? "_L" : "");
            
            if (qi.tex != null)
            {
                RegisterTexture(cacheKey, qi.tex);
            }

            ResolveInflight(cacheKey, qi.tex);
            lock (inflightLock)
            {
                inflightKeys.Remove(cacheKey);
            }
        }

        private void ResolveInflight(string cacheKey, Texture2D tex)
        {
            List<ImageLoaderThreaded.QueuedImage> waiters = null;
            lock (inflightLock)
            {
                if (inflightWaiters.TryGetValue(cacheKey, out waiters))
                {
                    inflightWaiters.Remove(cacheKey);
                }
            }

            if (waiters != null)
            {
                foreach (var w in waiters)
                {
                    w.tex = tex;
                    if (Messager.singleton != null)
                    {
                        Messager.singleton.StartCoroutine(DelayDoCallback(w));
                    }
                    else
                    {
                        DoCallback(w);
                    }
                }
            }
        }

        // --- Bulk Compression Logic ---

        public class ZstdStats
        {
            public long TotalFiles;
            public long ProcessedFiles;
            public long SkippedCount;
            public long TotalOriginalSize;
            public long TotalCompressedSize;
            public bool IsRunning;
            public string CurrentFile;
            public int FailedCount;
            public bool Completed;
            public bool CancelRequested;
            public bool IsDecompression;
            public float Duration;
            public DateTime StartTime;
        }
        public ZstdStats CurrentZstdStats = new ZstdStats();

        public void CancelBulkOperation()
        {
            if (CurrentZstdStats.IsRunning)
            {
                CurrentZstdStats.CancelRequested = true;
                LogUtil.Log("[VPB] Bulk operation cancellation requested.");
            }
        }

        public void StartBulkZstdCompression()
        {
            if (CurrentZstdStats.IsRunning) return;

            string nativeCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
            if (string.IsNullOrEmpty(nativeCacheDir))
            {
                nativeCacheDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Cache/Textures"));
            }
            string vpbCacheDir = VamHookPlugin.GetCacheDir();

            if (!Directory.Exists(nativeCacheDir))
            {
                LogUtil.LogError("[VPB] Native cache directory not found: " + nativeCacheDir);
                return;
            }

            LogUtil.Log("Starting bulk Zstd compression from " + nativeCacheDir + " to " + vpbCacheDir);
            CurrentZstdStats = new ZstdStats { IsRunning = true, IsDecompression = false, StartTime = DateTime.Now };

            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    BulkZstdWorker(nativeCacheDir, vpbCacheDir);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Bulk Zstd compression failed: " + ex.ToString());
                    CurrentZstdStats.IsRunning = false;
                }
            });
        }

        private void BulkZstdWorker(string nativeCacheDir, string vpbCacheDir)
        {
            string[] files = Directory.GetFiles(nativeCacheDir, "*.vamcache", SearchOption.TopDirectoryOnly);
            CurrentZstdStats.TotalFiles = files.Length;

            int compressionLevel = Settings.Instance.ZstdCompressionLevel.Value;
            bool deleteOriginal = Settings.Instance.DeleteOriginalCacheAfterCompression.Value;
            int threshold = Settings.Instance.ThumbnailThreshold.Value;

            foreach (var file in files)
            {
                if (CurrentZstdStats.CancelRequested)
                {
                    CurrentZstdStats.CurrentFile = "Cancelled";
                    break;
                }
                try
                {
                    string fileName = Path.GetFileName(file);
                    CurrentZstdStats.CurrentFile = fileName;

                    string metaPath = file + "meta";
                    if (!File.Exists(metaPath))
                    {
                        CurrentZstdStats.SkippedCount++;
                        continue;
                    }

                    // Check metadata for resolution and isThumbnail flag
                    JSONNode metaJson = null;
                    try
                    {
                        metaJson = JSON.Parse(File.ReadAllText(metaPath));
                        if (metaJson != null)
                        {
                            // Skip if marked as thumbnail or if resolution is <= threshold
                            bool isThumb = metaJson["isThumbnail"].AsBool;
                            int width = metaJson["width"].AsInt;
                            int height = metaJson["height"].AsInt;

                            if (isThumb || (width > 0 && width <= threshold && height > 0 && height <= threshold))
                            {
                                CurrentZstdStats.SkippedCount++;
                                continue;
                            }
                        }
                    }
                    catch { }

                    string targetName = Path.GetFileNameWithoutExtension(fileName);
                    targetName += ".zvamcache";
                    string targetPath = Path.Combine(vpbCacheDir, targetName);

                    long originalSize = new FileInfo(file).Length;
                    CurrentZstdStats.TotalOriginalSize += originalSize;

                    bool needsCompression = !File.Exists(targetPath) || File.GetLastWriteTime(file) > File.GetLastWriteTime(targetPath);
                    
                    // If file exists and timestamps match, check if compression level changed
                    if (!needsCompression && File.Exists(targetPath + "meta"))
                    {
                        try
                        {
                            var existingMeta = JSON.Parse(File.ReadAllText(targetPath + "meta"));
                            if (existingMeta["zstdLevel"] == null || existingMeta["zstdLevel"].AsInt != compressionLevel)
                            {
                                needsCompression = true;
                            }
                        }
                        catch { needsCompression = true; }
                    }

                    if (needsCompression)
                    {
                        ZstdCompressor.SaveCacheFromFile(targetPath, file, compressionLevel);
                        
                        // Update meta and update type to "compressed" and store level
                        if (metaJson != null)
                        {
                            try
                            {
                                metaJson["type"] = "compressed";
                                metaJson["width"] = metaJson["width"].Value; // Ensure it's a string
                                metaJson["height"] = metaJson["height"].Value; // Ensure it's a string
                                metaJson["zstdLevel"].AsInt = compressionLevel;
                                File.WriteAllText(targetPath + "meta", metaJson.ToString());
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError("Failed to update meta for " + targetPath + ": " + ex.Message);
                                File.Copy(metaPath, targetPath + "meta", true);
                            }
                        }
                    }

                    long compressedSize = new FileInfo(targetPath).Length;
                    CurrentZstdStats.TotalCompressedSize += compressedSize;

                    if (deleteOriginal)
                    {
                        File.Delete(file);
                        File.Delete(metaPath);
                    }

                    CurrentZstdStats.ProcessedFiles++;
                }
                catch (Exception ex)
                {
                    CurrentZstdStats.FailedCount++;
                    LogUtil.LogError("Bulk compression: Failed to convert " + file + ": " + ex.Message);
                    CurrentZstdStats.ProcessedFiles++;
                }
            }

            CurrentZstdStats.Duration = (float)(DateTime.Now - CurrentZstdStats.StartTime).TotalSeconds;
            CurrentZstdStats.IsRunning = false;
            CurrentZstdStats.Completed = true;
            CurrentZstdStats.CurrentFile = "Completed";
            LogUtil.Log(string.Format("Bulk compression completed: {0} processed, {1} failed", CurrentZstdStats.ProcessedFiles, CurrentZstdStats.FailedCount));
        }

        public void StartBulkZstdDecompression()
        {
            if (CurrentZstdStats.IsRunning) return;

            string nativeCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
            if (string.IsNullOrEmpty(nativeCacheDir))
            {
                nativeCacheDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Cache/Textures"));
            }
            string vpbCacheDir = VamHookPlugin.GetCacheDir();

            if (!Directory.Exists(vpbCacheDir))
            {
                LogUtil.LogError("[VPB] VPB cache directory not found: " + vpbCacheDir);
                return;
            }

            LogUtil.Log("Starting bulk Zstd decompression from " + vpbCacheDir + " to " + nativeCacheDir);
            CurrentZstdStats = new ZstdStats { IsRunning = true, IsDecompression = true, StartTime = DateTime.Now };

            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    BulkZstdDecompressWorker(nativeCacheDir, vpbCacheDir);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Bulk Zstd decompression failed: " + ex.ToString());
                    CurrentZstdStats.IsRunning = false;
                }
            });
        }

        private void BulkZstdDecompressWorker(string nativeCacheDir, string vpbCacheDir)
        {
            string[] files = Directory.GetFiles(vpbCacheDir, "*.zvamcache", SearchOption.TopDirectoryOnly);
            CurrentZstdStats.TotalFiles = files.Length;

            if (!Directory.Exists(nativeCacheDir)) Directory.CreateDirectory(nativeCacheDir);

            foreach (var file in files)
            {
                if (CurrentZstdStats.CancelRequested)
                {
                    CurrentZstdStats.CurrentFile = "Cancelled";
                    break;
                }
                try
                {
                    CurrentZstdStats.CurrentFile = Path.GetFileName(file);
                    string metaPath = file + "meta";
                    if (!File.Exists(metaPath))
                    {
                        CurrentZstdStats.SkippedCount++;
                        continue;
                    }

                    string fileName = Path.GetFileName(file);
                    string targetName = Path.GetFileNameWithoutExtension(fileName);
                    targetName += ".vamcache";
                    
                    string targetPath = Path.Combine(nativeCacheDir, targetName);

                    long compressedSize = new FileInfo(file).Length;
                    CurrentZstdStats.TotalCompressedSize += compressedSize;

                    if (!File.Exists(targetPath))
                    {
                        byte[] compressed = File.ReadAllBytes(file);
                        byte[] decompressed = ZstdCompressor.Decompress(compressed);
                        File.WriteAllBytes(targetPath, decompressed);
                        
                        // Restore native meta format
                        try
                        {
                            var metaJson = JSON.Parse(File.ReadAllText(metaPath));
                            metaJson["type"] = "image";
                            metaJson["width"] = metaJson["width"].Value; // Ensure it's a string
                            metaJson["height"] = metaJson["height"].Value; // Ensure it's a string
                            metaJson.Remove("zstdLevel");
                            File.WriteAllText(targetPath + "meta", metaJson.ToString());
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("Failed to restore native meta for " + targetPath + ": " + ex.Message);
                            File.Copy(metaPath, targetPath + "meta", true);
                        }
                    }

                    long originalSize = new FileInfo(targetPath).Length;
                    CurrentZstdStats.TotalOriginalSize += originalSize;

                    // Decompressed successfully, remove the compressed versions
                    try
                    {
                        File.Delete(file);
                        File.Delete(metaPath);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("Failed to delete source file during decompression: " + ex.Message);
                    }

                    CurrentZstdStats.ProcessedFiles++;
                }
                catch (Exception ex)
                {
                    CurrentZstdStats.FailedCount++;
                    LogUtil.LogError("Bulk decompression: Failed to revert " + file + ": " + ex.Message);
                    CurrentZstdStats.ProcessedFiles++;
                }
            }

            CurrentZstdStats.Duration = (float)(DateTime.Now - CurrentZstdStats.StartTime).TotalSeconds;
            CurrentZstdStats.IsRunning = false;
            CurrentZstdStats.Completed = true;
            CurrentZstdStats.CurrentFile = "Restored";
            LogUtil.Log(string.Format("Bulk decompression completed: {0} processed, {1} failed", CurrentZstdStats.ProcessedFiles, CurrentZstdStats.FailedCount));
        }

        // --- Compatibility Stubs (Removed from Settings but kept here as no-ops to avoid immediate breaking of other hooks) ---

        public void ClearCandidates() { }
        public void ProcessCandidates() { }
        public void TrackCandidate(ImageLoaderThreaded.QueuedImage qi) { }
        public bool TryEnqueueResizeCache(ImageLoaderThreaded.QueuedImage qi) { return false; }
        public ImageLoaderThreaded.QueuedImage FindCandidateByTexture(Texture2D tex) { return null; }
        public ImageLoaderThreaded.QueuedImage FindCandidateByPath(string path) { return null; }
    }
}
