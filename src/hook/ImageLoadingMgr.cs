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
        
        private void Awake()
        {
            singleton = this;
        }

        Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        Dictionary<string, List<ImageLoaderThreaded.QueuedImage>> inflightWaiters = new Dictionary<string, List<ImageLoaderThreaded.QueuedImage>>();
        HashSet<string> inflightKeys = new HashSet<string>();

        public void ClearCache()
        {
            cache.Clear();
        }

        public Texture2D GetTextureFromCache(string path)
        {
            if (cache.TryGetValue(path, out var tex))
            {
                if (tex != null) return tex;
                cache.Remove(path);
            }
            return null;
        }

        void RegisterTexture(string path, Texture2D tex)
        {
            if (string.IsNullOrEmpty(path) || tex == null) return;
            cache[path] = tex;
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
            if (qi == null || string.IsNullOrEmpty(qi.imgPath)) return false;
            
            LogUtil.MarkImageActivity();

            // Skip textures that are considered thumbnails (<= threshold)
            int threshold = Settings.Instance.ThumbnailThreshold.Value;
            if (qi.setSize && qi.width > 0 && qi.width <= threshold && qi.height > 0 && qi.height <= threshold)
            {
                return false;
            }

            // Check native cache meta if resolution is not already known to skip thumbnails
            try
            {
                string nativePath = TextureUtil.GetNativeCachePath(qi.imgPath);
                if (nativePath != null)
                {
                    string nativeMetaPath = nativePath + "meta";
                    if (File.Exists(nativeMetaPath))
                    {
                        var meta = JSON.Parse(File.ReadAllText(nativeMetaPath));
                        if (meta != null)
                        {
                            int w = meta["width"].AsInt;
                            int h = meta["height"].AsInt;
                            bool isThumb = meta["isThumbnail"].AsBool;
                            if (isThumb || (w > 0 && w <= threshold && h > 0 && h <= threshold))
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            catch { }

            // Check memory cache first
            string cacheKey = qi.imgPath + (qi.linear ? "_L" : "");
            var cacheTexture = GetTextureFromCache(cacheKey);
            if (cacheTexture != null)
            {
                qi.tex = cacheTexture;
                Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                return true;
            }

            // Check if already inflight
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

            // Check for .zvamcache in VPB_Cache
            string vpbCachePath = TextureUtil.GetZstdCachePath(qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert, qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength);
            if (vpbCachePath != null && !File.Exists(vpbCachePath) && qi.setSize)
            {
                // Fallback to full size Zstd cache
                vpbCachePath = TextureUtil.GetZstdCachePath(qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert, 0, 0, qi.bumpStrength);
            }

            if (vpbCachePath != null && File.Exists(vpbCachePath))
            {
                inflightKeys.Add(cacheKey);
                try
                {
                    string metaPath = vpbCachePath + "meta";
                    if (File.Exists(metaPath))
                    {
                        var metaJson = JSON.Parse(File.ReadAllText(metaPath));
                        int width = metaJson["width"].AsInt;
                        int height = metaJson["height"].AsInt;
                        TextureFormat format = (TextureFormat)Enum.Parse(typeof(TextureFormat), metaJson["format"].Value);

                        byte[] compressed = File.ReadAllBytes(vpbCachePath);
                        byte[] decompressed = ZstdCompressor.Decompress(compressed);

                        Texture2D tex = new Texture2D(width, height, format, false, qi.linear);
                        tex.LoadRawTextureData(decompressed);
                        tex.Apply();
                        qi.tex = tex;

                        RegisterTexture(cacheKey, tex);
                        
                        // Resolve others waiting for this same key
                        ResolveInflight(cacheKey, tex);
                        
                        Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Failed to load from Zstd cache " + vpbCachePath + ": " + ex.Message);
                }
                finally
                {
                    inflightKeys.Remove(cacheKey);
                }
            }

            // Not in Zstd cache, let VaM handle it but track it so we can suppress duplicates
            inflightKeys.Add(cacheKey);
            return false;
        }

        public void ResolveInflightForQueuedImage(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath)) return;
            string cacheKey = qi.imgPath + (qi.linear ? "_L" : "");
            
            if (qi.tex != null)
            {
                RegisterTexture(cacheKey, qi.tex);
            }

            ResolveInflight(cacheKey, qi.tex);
            inflightKeys.Remove(cacheKey);
        }

        private void ResolveInflight(string cacheKey, Texture2D tex)
        {
            if (inflightWaiters.TryGetValue(cacheKey, out var waiters))
            {
                foreach (var w in waiters)
                {
                    w.tex = tex;
                    Messager.singleton.StartCoroutine(DelayDoCallback(w));
                }
                inflightWaiters.Remove(cacheKey);
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
