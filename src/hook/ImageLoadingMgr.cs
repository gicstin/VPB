using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using Valve.Newtonsoft.Json.Linq;

namespace VPB
{
    /// <summary>
    /// Textures may need to be read later, so we cannot discard the CPU-side memory.
    /// </summary>
    public class ImageLoadingMgr : MonoBehaviour
    {
        static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();

        [System.Serializable]
        public class ImageRequest
        {
            public string path;
            public Texture2D texture;
        }
        public static ImageLoadingMgr singleton;
        private readonly object _diskCacheWriteLock = new object();
        private readonly Dictionary<string, object> _perPathWriteLocks = new Dictionary<string, object>();
        
        private void Awake()
        {
            singleton = this;
        }
        private void Start()
        {
            StartBulkZstdCompression();
        }

        private float _lastDumpTime;
        private void Update()
        {
            if (Time.realtimeSinceStartup - _lastDumpTime > 1.0f)
            {
                _lastDumpTime = Time.realtimeSinceStartup;
                DumpTrackedImagesToCache();
            }
        }

        Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        class ResizeJob
        {
            public ImageLoaderThreaded.QueuedImage qi;
            public string key;
        }

        readonly Queue<ResizeJob> resizeQueue = new Queue<ResizeJob>();
        readonly HashSet<string> resizeQueuedKeys = new HashSet<string>();
        readonly List<ResizeJob> _trackedImages = new List<ResizeJob>();
        readonly HashSet<ImageLoaderThreaded.QueuedImage> _trackedQiSet = new HashSet<ImageLoaderThreaded.QueuedImage>();
        readonly List<ImageLoaderThreaded.QueuedImage> _candidateImages = new List<ImageLoaderThreaded.QueuedImage>();
        Coroutine resizeCoroutine;

        Dictionary<string, List<ImageLoaderThreaded.QueuedImage>> inflightWaiters = new Dictionary<string, List<ImageLoaderThreaded.QueuedImage>>();
        HashSet<string> inflightKeys = new HashSet<string>();
        HashSet<string> diskWriteInFlight = new HashSet<string>();

        void EnqueueInflightWaiter(string key, ImageLoaderThreaded.QueuedImage qi)
        {
            if (string.IsNullOrEmpty(key) || qi == null) return;
            List<ImageLoaderThreaded.QueuedImage> list;
            if (!inflightWaiters.TryGetValue(key, out list) || list == null)
            {
                list = new List<ImageLoaderThreaded.QueuedImage>(4);
                inflightWaiters[key] = list;
            }
            list.Add(qi);
        }

        void ResolveInflightWaiters(string key, Texture2D tex)
        {
            if (string.IsNullOrEmpty(key)) return;
            inflightKeys.Remove(key);

            List<ImageLoaderThreaded.QueuedImage> list;
            if (!inflightWaiters.TryGetValue(key, out list) || list == null || list.Count == 0)
            {
                inflightWaiters.Remove(key);
                return;
            }

            inflightWaiters.Remove(key);
            if (tex == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                if (w == null) continue;
                try
                {
                    w.tex = tex;
                    Messager.singleton.StartCoroutine(DelayDoCallback(w));
                }
                catch { }
            }
        }

        public void ResolveInflightForQueuedImage(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return;
            string key = GetDiskCachePath(qi, false, 0, 0);
            if (string.IsNullOrEmpty(key)) return;
            ResolveInflightWaiters(key, qi.tex);
        }
        public void ClearCache()
        {
            cache.Clear();
        }
        public Texture2D GetTextureFromCache(string path)
        {
            if (cache.ContainsKey(path))
            {
                if (cache[path] != null)
                    return cache[path];
                cache.Remove(path);
            }
            return null;
        }
        void RegisterTexture(string path, Texture2D tex)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (cache.ContainsKey(path) && cache[path] != null)
                return;
            if (tex == null)
                return;
            //LogUtil.Log("RegisterTexture:" + path);
            cache.Remove(path);
            cache.Add(path, tex);
        }
        public List<ImageRequest> requests = new List<ImageRequest>();
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
        // Cannot call immediately; delay one frame.
        // e.g. MacGruber.PostMagic can finish immediately while initialization isn't complete yet.
        WaitForEndOfFrame waitForEndOfFrame= new WaitForEndOfFrame();
        IEnumerator DelayDoCallback(ImageLoaderThreaded.QueuedImage qi)
        {
            yield return waitForEndOfFrame;
            // Delay 2 frames; delaying only 1 frame can break decalmaker timing.
            yield return waitForEndOfFrame;
            DoCallback(qi);
        }

        private object GetPathWriteLock(string path)
        {
            lock (_diskCacheWriteLock)
            {
                if (!_perPathWriteLocks.ContainsKey(path))
                {
                    _perPathWriteLocks[path] = new object();
                }
                return _perPathWriteLocks[path];
            }
        }

        void EnsureResizeCoroutine()
        {
            if (resizeCoroutine != null) return;
            try
            {
                resizeCoroutine = StartCoroutine(ResizeWorker());
            }
            catch (Exception ex)
            {
                LogUtil.LogError("Resize start coroutine failed: " + ex.ToString());
            }
        }

        public bool TryEnqueueResizeCache(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) 
            {
                 LogUtil.Log("TryEnqueueResizeCache: qi is null");
                 return false;
            }

            lock (_trackedQiSet)
            {
                if (_trackedQiSet.Contains(qi)) 
                {
                    // LogUtil.Log("TryEnqueueResizeCache: already tracked (set) " + qi.imgPath);
                    return false;
                }
            }

            if (qi.tex == null)
            {
                LogUtil.Log("TryEnqueueResizeCache skipped: tex is null for " + qi.imgPath);
                return false;
            }

            try
            {
                if (Settings.Instance == null) { LogUtil.Log("TryEnqueueResizeCache: Settings null"); return false; }
                if (!Settings.Instance.EnableTextureOptimizations.Value) { LogUtil.Log("TryEnqueueResizeCache: Opts disabled"); return false; }
                if (!Settings.Instance.ReduceTextureSize.Value && !Settings.Instance.EnableZstdCompression.Value) { LogUtil.Log("TryEnqueueResizeCache: Resize/Zstd disabled"); return false; }
            }
            catch (Exception ex) { LogUtil.LogError("TryEnqueueResizeCache Settings Error: " + ex); return false; }

            string key = GetDiskCachePath(qi, false, 0, 0);
            if (string.IsNullOrEmpty(key)) 
            {
                 LogUtil.Log("TryEnqueueResizeCache skipped: key is null for " + qi.imgPath);
                 return false;
            }

            // If no resize would occur (and resize is enabled), don't generate cache unless Zstd is on.
            int w = qi.tex.width;
            int h = qi.tex.height;
            if (Settings.Instance.ReduceTextureSize.Value)
            {
                GetResizedSize(ref w, ref h, qi.imgPath);
            }

            // If disk cache already exists, skip.
            try
            {
                var real = GetDiskCachePath(qi, true, w, h);
                if (!string.IsNullOrEmpty(real))
                {
                    bool inFlight = false;
                    lock(diskWriteInFlight) { inFlight = diskWriteInFlight.Contains(key + ".meta"); }

                    if (inFlight || ((File.Exists(real + ".cache") || File.Exists(real)) && File.Exists(key + ".meta")))
                    {
                        LogUtil.Log("TryEnqueueResizeCache skipped: exists or in-flight " + real);
                        return false;
                    }
                }
            }
            catch { }

            if (resizeQueuedKeys.Contains(key)) 
            {
                LogUtil.Log("TryEnqueueResizeCache: key in resizeQueuedKeys " + key);
                return false;
            }
            
            lock (_trackedQiSet)
            {
                if (_trackedQiSet.Contains(qi)) return false;
                _trackedQiSet.Add(qi);
            }

            lock (_trackedImages)
            {
                _trackedImages.Add(new ResizeJob { qi = qi, key = key });
            }
            LogUtil.Log("Tracked image for later cache: " + qi.imgPath + " (format: " + qi.tex.format + " " + qi.tex.width + "x" + qi.tex.height + ")");
            return true;
        }

        public ImageLoaderThreaded.QueuedImage FindCandidateByTexture(Texture2D tex)
        {
            if (tex == null) return null;
            lock (_candidateImages)
            {
                foreach (var qi in _candidateImages)
                {
                    if (qi != null && qi.tex == tex) return qi;
                }
            }
            return null;
        }

        public ImageLoaderThreaded.QueuedImage FindCandidateByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (_candidateImages)
            {
                foreach (var qi in _candidateImages)
                {
                    if (qi != null && string.Equals(qi.imgPath, path, StringComparison.OrdinalIgnoreCase)) return qi;
                }
            }
            return null;
        }

        public void ClearCandidates()
        {
            lock (_candidateImages) { _candidateImages.Clear(); }
            lock (_trackedQiSet) { _trackedQiSet.Clear(); }
            lock (_trackedImages) { _trackedImages.Clear(); }
            lock (resizeQueuedKeys) { resizeQueuedKeys.Clear(); }
            lock (resizeQueue) { resizeQueue.Clear(); }
        }

        [ThreadStatic]
        public static string currentProcessingPath;
        public void TrackCandidate(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return;

            // Wrap callback to capture texture immediately upon completion.
            // This is more reliable than waiting for scene end, as textures may be cleared from memory.
            var originalCallback = qi.callback;
            qi.callback = (newQi) =>
            {
                try
                {
                    LogUtil.Log("TrackCandidate callback for " + (newQi != null ? newQi.imgPath : "null") + " tex=" + (newQi != null && newQi.tex != null ? newQi.tex.name : "null"));
                    if (newQi != null && newQi.tex != null)
                    {
                        TryEnqueueResizeCache(newQi);
                    }
                    else
                    {
                        LogUtil.Log("TrackCandidate callback texture missing for " + (newQi != null ? newQi.imgPath : "null"));
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error in wrapped image callback: " + ex.Message);
                }
                finally
                {
                    if (originalCallback != null) originalCallback(newQi);
                }
            };

            lock (_candidateImages)
            {
                if (!_candidateImages.Contains(qi))
                {
                    _candidateImages.Add(qi);
                }
            }
        }

        public void DumpTrackedImagesToCache()
        {
            int candidateCount = 0;
            int withTextureCount = 0;
            // First, promote candidates that now have textures
            lock (_candidateImages)
            {
                candidateCount = _candidateImages.Count;
                for (int i = _candidateImages.Count - 1; i >= 0; i--)
                {
                    var qi = _candidateImages[i];
                    if (qi != null && qi.tex != null)
                    {
                        withTextureCount++;
                        TryEnqueueResizeCache(qi);
                        _candidateImages.RemoveAt(i);
                    }
                    else if (qi == null)
                    {
                        _candidateImages.RemoveAt(i);
                    }
                }
            }



            int count = 0;
            lock (_trackedImages)
            {
                count = _trackedImages.Count;
                if (count == 0) return;

                foreach (var job in _trackedImages)
                {
                    if (job != null && !string.IsNullOrEmpty(job.key))
                    {
                        if (!resizeQueuedKeys.Contains(job.key))
                        {
                            resizeQueuedKeys.Add(job.key);
                            resizeQueue.Enqueue(job);
                        }
                    }
                }
                _trackedImages.Clear();
            }

            LogUtil.Log("Dumped " + count + " tracked images to cache worker");
            lock (_trackedQiSet) { _trackedQiSet.Clear(); }
            EnsureResizeCoroutine();
        }

        IEnumerator ResizeWorker()
        {
            WaitForEndOfFrame wait = new WaitForEndOfFrame();
            WaitForSeconds waitASecond = new WaitForSeconds(1f);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            while (true)
            {
                if (resizeQueue.Count == 0)
                {
                    resizeCoroutine = null;
                    yield break;
                }

                if (LogUtil.IsSceneLoading())
                {
                    yield return waitASecond;
                    continue;
                }

                sw.Reset();
                sw.Start();

                while (resizeQueue.Count > 0)
                {
                    var job = resizeQueue.Dequeue();
                    if (job != null && job.qi != null)
                    {
                        try
                        {
                            GenerateResizedDiskCache(job.qi);
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                LogUtil.LogError("ResizeWorker error path=" + (job.qi != null ? job.qi.imgPath : "?") + " " + ex.ToString());
                            }
                            catch { }
                        }
                        finally
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(job.key)) resizeQueuedKeys.Remove(job.key);
                            }
                            catch { }
                        }
                    }

                    // Process more per frame if we are not loading a scene.
                    float msLimit = LogUtil.IsSceneLoading() ? 5.0f : 30.0f;
                    if (sw.Elapsed.TotalMilliseconds > msLimit)
                    {
                        break;
                    }
                }

                yield return wait;
            }
        }

        void GenerateResizedDiskCache(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return;
            if (qi.tex == null) return;

            var diskCachePath = GetDiskCachePath(qi, false, 0, 0);
            if (string.IsNullOrEmpty(diskCachePath)) return;

            int width = qi.tex.width;
            int height = qi.tex.height;
            if (Settings.Instance.ReduceTextureSize.Value)
            {
                GetResizedSize(ref width, ref height, qi.imgPath);
            }

            bool enableZstd = Settings.Instance.EnableZstdCompression.Value;
            var realDiskCachePath = GetDiskCachePath(qi, true, width, height);
            if (string.IsNullOrEmpty(realDiskCachePath)) return;
            var realDiskCachePathCache = realDiskCachePath + ".cache";

            if ((File.Exists(realDiskCachePathCache) || File.Exists(realDiskCachePath)) && File.Exists(diskCachePath + ".meta")) return;

            LogUtil.Log("Generating cache for " + qi.imgPath + " -> " + realDiskCachePathCache);

            // OPTIMIZATION: If no resize needed and already DXT, just grab the bytes.
            if (width == qi.tex.width && height == qi.tex.height && (qi.tex.format == TextureFormat.DXT1 || qi.tex.format == TextureFormat.DXT5))
            {
                try
                {
                    var bytes = qi.tex.GetRawTextureData();
                    if (bytes != null && bytes.Length > 0)
                    {
                        byte[] pooledBytes = ByteArrayPool.Rent(bytes.Length);
                        Array.Copy(bytes, pooledBytes, bytes.Length);

                        JSONClass jSON = new JSONClass();
                        jSON["type"] = enableZstd ? "compressed" : "dxt";
                        jSON["width"].AsInt = qi.tex.width;
                        jSON["height"].AsInt = qi.tex.height;
                        jSON["resizedWidth"].AsInt = width;
                        jSON["resizedHeight"].AsInt = height;
                        jSON["format"] = qi.tex.format.ToString();

                        WriteDiskCacheAsync(realDiskCachePathCache, pooledBytes, bytes.Length, diskCachePath + ".meta", jSON.ToString(string.Empty), enableZstd, width, height, qi.tex.format, qi.linear);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("Fast-path GetRawTextureData failed, falling back to blit: " + ex.Message);
                }
            }

            // Generate the resized/compressed texture and write to disk cache.
            Texture2D tmp = null;
            RenderTexture tempTexture = null;
            try
            {
                tempTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                    qi.linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

                Graphics.SetRenderTarget(tempTexture);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, width, height, 0);
                GL.Clear(true, true, Color.clear);
                Graphics.Blit(qi.tex, tempTexture);
                GL.PopMatrix();
                Graphics.SetRenderTarget(null);

                var format = qi.tex.format == TextureFormat.DXT1 ? TextureFormat.RGB24 : TextureFormat.RGBA32;
                tmp = new Texture2D(width, height, format, false, qi.linear);
                var previous = RenderTexture.active;
                RenderTexture.active = tempTexture;
                tmp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tmp.Apply();
                RenderTexture.active = previous;

                if (width > 0 && height > 0 && (width & (width - 1)) == 0 && (height & (height - 1)) == 0)
                {
                    try { tmp.Compress(true); } catch (Exception ex) { LogUtil.LogError("Img cache compress failed " + ex + " path=" + qi.imgPath); }
                }

                var bytes = tmp.GetRawTextureData();
                // Copy to pooled buffer for async write to avoid GC alloc
                byte[] pooledBytes = ByteArrayPool.Rent(bytes.Length);
                Array.Copy(bytes, pooledBytes, bytes.Length);
                int realLength = bytes.Length;
                
                JSONClass jSONClass = new JSONClass();
                jSONClass["type"] = enableZstd ? "compressed" : "dxt";
                jSONClass["width"].AsInt = qi.tex.width;
                jSONClass["height"].AsInt = qi.tex.height;
                jSONClass["resizedWidth"].AsInt = width;
                jSONClass["resizedHeight"].AsInt = height;
                jSONClass["format"] = tmp.format.ToString();
                
                WriteDiskCacheAsync(realDiskCachePathCache, pooledBytes, realLength, diskCachePath + ".meta", jSONClass.ToString(string.Empty), enableZstd, width, height, tmp.format, qi.linear);

                // Note: we intentionally do NOT RegisterTexture or swap qi.tex in this async cache path.
                // This prevents unexpected texture replacement mid-frame; future loads will hit disk/mem cache.
            }
            finally
            {
                if (tmp != null)
                {
                    UnityEngine.Object.Destroy(tmp);
                }

                if (tempTexture != null)
                {
            RenderTexture.ReleaseTemporary(tempTexture);
                }
            }
        }

        void WriteDiskCacheAsync(string pathCache, byte[] bytes, int length, string pathMeta, string metaContent, bool isZstd = false, int w = 0, int h = 0, TextureFormat fmt = TextureFormat.RGBA32, bool linear = false)
        {
            if (!string.IsNullOrEmpty(pathMeta))
            {
                lock(diskWriteInFlight) { diskWriteInFlight.Add(pathMeta); }
            }

            ThreadPool.QueueUserWorkItem((state) =>
            {
                object pathLock = GetPathWriteLock(pathCache);
                lock (pathLock)
                {
                    try
                    {
                        if (bytes != null && !string.IsNullOrEmpty(pathCache))
                        {
                            int compressionLevel = Settings.Instance.ZstdCompressionLevel.Value;
                            
                            if (isZstd && compressionLevel > 0)
                            {
                                try
                                {
                                    byte[] compressed = ZstdCompressor.Compress(bytes, compressionLevel);
                                    using (FileStream fs = new FileStream(pathCache, FileMode.Create, FileAccess.Write))
                                    {
                                        fs.Write(compressed, 0, compressed.Length);
                                    }
                                    LogUtil.Log("[VPB] WriteDiskCacheAsync: Wrote Zstd-compressed cache (level " + compressionLevel + ") for " + pathCache);
                                }
                                catch (Exception zstdEx)
                                {
                                    LogUtil.LogWarning("[VPB] WriteDiskCacheAsync: Zstd compression failed, writing uncompressed. Error: " + zstdEx.ToString());
                                    using (FileStream fs = new FileStream(pathCache, FileMode.Create, FileAccess.Write))
                                    {
                                        fs.Write(bytes, 0, length);
                                    }
                                }
                            }
                            else
                            {
                                using (FileStream fs = new FileStream(pathCache, FileMode.Create, FileAccess.Write))
                                {
                                    fs.Write(bytes, 0, length);
                                }
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(metaContent) && !string.IsNullOrEmpty(pathMeta))
                        {
                            string tempMetaPath = pathMeta + ".tmp";
                            try
                            {
                                var metaJson = SimpleJSON.JSON.Parse(metaContent);
                                // The meta content already has correct 'type' set in GenerateResizedDiskCache
                                File.WriteAllText(tempMetaPath, metaJson.ToString(string.Empty));
                                if (File.Exists(pathMeta)) File.Delete(pathMeta);
                                File.Move(tempMetaPath, pathMeta);
                            }
                            catch
                            {
                                try { if (File.Exists(tempMetaPath)) File.Delete(tempMetaPath); } catch { }
                                throw;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("WriteDiskCacheAsync failed: " + pathCache + " " + ex.ToString());
                    }
                    finally
                    {
                        if (!string.IsNullOrEmpty(pathMeta))
                        {
                            lock(diskWriteInFlight) { diskWriteInFlight.Remove(pathMeta); }
                        }
                        if (bytes != null) ByteArrayPool.Return(bytes);
                    }
                }
            });
        }

        public bool Request(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return false;
            if (!Settings.Instance.EnableTextureOptimizations.Value) return false;
            if (string.IsNullOrEmpty(qi.imgPath)) return false;

            bool originalLinear = qi.linear;
            if (RequestImpl(qi)) return true;

            qi.linear = !originalLinear;
            if (RequestImpl(qi)) return true;

            qi.linear = originalLinear;
            return false;
        }

        private bool RequestImpl(ImageLoaderThreaded.QueuedImage qi)
        {
            LogUtil.MarkImageActivity();
            var swRequest = System.Diagnostics.Stopwatch.StartNew();
            var diskCachePath = GetDiskCachePath(qi,false,0,0);
            if (string.IsNullOrEmpty(diskCachePath)) return false;

            LogUtil.Log("Request checking: " + diskCachePath + " linear=" + qi.linear);

            var cacheTexture = GetTextureFromCache(diskCachePath);
            if (cacheTexture!=null)
            {
                LogUtil.PerfAdd("Img.Cache.MemHit", 0, 0);
                LogUtil.LogTextureTrace("Img.Request.MemHit:" + diskCachePath, "request use mem cache:" + diskCachePath);
                qi.tex = cacheTexture;
                Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                LogUtil.PerfAdd("Mgr.Request", swRequest.Elapsed.TotalMilliseconds, 0);
                return true;
            }

            bool inflightEnabled = Settings.Instance != null && Settings.Instance.InflightDedupEnabled != null && Settings.Instance.InflightDedupEnabled.Value;

            if (inflightEnabled && inflightKeys.Contains(diskCachePath))
            {
                EnqueueInflightWaiter(diskCachePath, qi);
                qi.skipCache = true;
                qi.processed = true;
                qi.finished = true;
                LogUtil.PerfAdd("Mgr.Request", swRequest.Elapsed.TotalMilliseconds, 0);
                return true;
            }

            var metaPath = diskCachePath + ".meta";
            var legacyDiskCachePath = diskCachePath + ".cache";
            var legacyMetaPath = legacyDiskCachePath + ".meta";
            int width = 0;
            int height = 0;
            TextureFormat textureFormat=TextureFormat.DXT1;
            string metaToUse = null;
            if (File.Exists(metaPath))
            {
                metaToUse = metaPath;
            }
            else if (File.Exists(legacyMetaPath))
            {
                metaToUse = legacyMetaPath;
            }
            else
            {
                 LogUtil.LogTextureTrace("Img.Request.MetaMissing:" + diskCachePath, "Request meta missing: " + metaPath + " (legacy: " + legacyMetaPath + ")");
            }
            
            if (metaToUse != null)
            {
                var jsonString = File.ReadAllText(metaToUse);
                JSONNode jSONNode = JSON.Parse(jsonString);
                JSONClass asObject = (jSONNode != null) ? jSONNode.AsObject : null;
                if (asObject == null)
                {
                    LogUtil.LogError("Invalid meta JSON in " + metaToUse + ": " + jsonString);
                    return false;
                }

                if (asObject != null)
                {
                    if (asObject["width"] != null) width = asObject["width"].AsInt;
                    if (asObject["height"] != null) height = asObject["height"].AsInt;
                    if (asObject["format"] != null) textureFormat = (TextureFormat)System.Enum.Parse(typeof(TextureFormat), asObject["format"]);
                }

                if (Settings.Instance.ReduceTextureSize.Value)
                {
                    GetResizedSize(ref width, ref height, qi.imgPath);
                }

                var realDiskCachePath = GetDiskCachePath(qi, true, width, height);
                var diskPathToUse = realDiskCachePath + ".cache";
                bool isCompressed = asObject != null && asObject["type"].Value.ToLower() == "compressed";

                LogUtil.LogTextureTrace("Img.Request.MetaHit:" + diskCachePath, string.Format("Meta hit: {0}, format={1}, size={2}x{3}, type={4}", metaToUse, textureFormat, width, height, (asObject != null ? asObject["type"].Value : "null")));

                if (!File.Exists(diskPathToUse))
                {
                    LogUtil.Log(string.Format("Request cache missing: {0} (type={1}, meta={2})", diskPathToUse, (asObject != null ? asObject["type"].Value : "null"), metaToUse));

                    // Backward-compat for caches previously written without extension.
                    if (File.Exists(realDiskCachePath))
                    {
                        diskPathToUse = realDiskCachePath;
                    }
                }

                if (File.Exists(diskPathToUse))
                {
                    LogUtil.PerfAdd("Img.Cache.DiskHit", 0, 0);
                    LogUtil.LogTextureTrace("Img.Request.DiskHit:" + diskPathToUse, "request use disk cache:" + diskPathToUse);
                    var swRead = System.Diagnostics.Stopwatch.StartNew();
                    byte[] bytes = null;
                    long bytesLen = 0;
                    bool success = false;
                    Texture2D tex = null;

                    try
                    {
                        using (var fs = new FileStream(diskPathToUse, FileMode.Open, FileAccess.Read))
                        {
                            int len = (int)fs.Length;
                            byte[] fileBytes = new byte[len];
                            int offset = 0;
                            int remaining = len;
                            while (remaining > 0)
                            {
                                int read = fs.Read(fileBytes, offset, remaining);
                                if (read <= 0) break;
                                offset += read;
                                remaining -= read;
                            }

                            if (isCompressed)
                            {
                                try
                                {
                                    byte[] decompressed = ZstdCompressor.Decompress(fileBytes);
                                    bytes = ByteArrayPool.Rent(decompressed.Length);
                                    Array.Copy(decompressed, bytes, decompressed.Length);
                                    bytesLen = decompressed.Length;
                                    success = true;
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogError("Zstd decompression failed for " + diskPathToUse + ": " + ex.Message);
                                    success = false;
                                }
                            }
                            else
                            {
                                bytes = ByteArrayPool.Rent(len);
                                Array.Copy(fileBytes, bytes, len);
                                bytesLen = len;
                                success = true;
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        LogUtil.LogError("request load disk cache fail:" + diskPathToUse + " " + ex.ToString());
                        try { File.Delete(diskPathToUse); } catch { }
                    }

                    if (success)
                    {
                        LogUtil.PerfAdd("Img.Disk.Read", swRead.Elapsed.TotalMilliseconds, bytesLen);
                        LogUtil.LogTextureSlowDisk("read", diskPathToUse, swRead.Elapsed.TotalMilliseconds, bytesLen);

                        tex = new Texture2D(width, height, textureFormat, false, qi.linear);
                        tex.LoadRawTextureData(bytes);
                        tex.Apply();
                        qi.tex = tex;

                        RegisterTexture(diskCachePath, tex);

                        if (bytes != null) ByteArrayPool.Return(bytes);

                        Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                        LogUtil.PerfAdd("Mgr.Request", swRequest.Elapsed.TotalMilliseconds, 0);
                        return true;
                    }
                    else
                    {
                        LogUtil.Log("Request cache load failed (success=false): " + diskPathToUse);
                        if (bytes != null) ByteArrayPool.Return(bytes);
                    }
                }
                else
                {
                    LogUtil.Log("Request cache file missing (meta exists): " + diskPathToUse);
                }
            }

            LogUtil.PerfAdd("Img.Cache.Miss", 0, 0);
            LogUtil.LogTextureTrace("Img.Request.Miss:" + diskCachePath, "request not use cache:" + diskCachePath);

            if (inflightEnabled && !inflightKeys.Contains(diskCachePath))
            {
                inflightKeys.Add(diskCachePath);
            }

            LogUtil.PerfAdd("Mgr.Request", swRequest.Elapsed.TotalMilliseconds, 0);
            return false;
        }

        public void StartBulkZstdCompression()
        {
            if (Settings.Instance == null || !Settings.Instance.EnableTextureOptimizations.Value || !Settings.Instance.EnableZstdCompression.Value)
                return;

            string cacheDir = VamHookPlugin.GetCacheDir();
            if (!Directory.Exists(cacheDir)) return;

            LogUtil.Log("Starting bulk Zstd compression...");
            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    BulkZstdWorker(cacheDir);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Bulk Zstd compression failed: " + ex.ToString());
                }
            });
        }

        private void BulkZstdWorker(string cacheDir)
        {
            string[] files = Directory.GetFiles(cacheDir, "*.cache", SearchOption.TopDirectoryOnly);
            LogUtil.Log(string.Format("Bulk compression: Found {0} .cache files", files.Length));

            int compressed = 0;
            int skipped = 0;
            int failed = 0;
            int compressionLevel = Settings.Instance.ZstdCompressionLevel.Value;

            foreach (var file in files)
            {
                try
                {
                    string metaPath = file.Substring(0, file.Length - 6) + ".meta";
                    if (!File.Exists(metaPath))
                    {
                        if (File.Exists(file + ".meta")) metaPath = file + ".meta";
                        else { skipped++; continue; }
                    }

                    var metaJson = JSON.Parse(File.ReadAllText(metaPath));
                    var type = metaJson["type"].Value;
                    if (type == "compressed") { skipped++; continue; }

                    if (compressionLevel <= 0) { skipped++; continue; }

                    byte[] dxtData = File.ReadAllBytes(file);
                    byte[] compressedData = ZstdCompressor.Compress(dxtData, compressionLevel);

                    string tempPath = file + ".tmp";
                    File.WriteAllBytes(tempPath, compressedData);
                    File.Delete(file);
                    File.Move(tempPath, file);

                    metaJson["type"] = "compressed";
                    File.WriteAllText(metaPath, metaJson.ToString(string.Empty));

                    compressed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    LogUtil.LogError("Bulk compression: Failed to convert " + file + ": " + ex.Message);
                }
            }

            LogUtil.Log(string.Format("Bulk compression completed: {0} compressed, {1} skipped, {2} failed", compressed, skipped, failed));
        }

        static bool IsLikelyUrlFieldName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                return name.EndsWith("Url", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        static bool IsLikelyDataTextureSlot(string slotName)
        {
            if (string.IsNullOrEmpty(slotName)) return false;
            string n;
            try { n = slotName.ToLowerInvariant(); } catch { n = slotName; }
            if (n.Contains("normal")) return true;
            if (n.Contains("specular")) return true;
            if (n.Contains("gloss")) return true;
            if (n.Contains("roughness")) return true;
            if (n.Contains("metallic")) return true;
            if (n.Contains("ao")) return true;
            if (n.Contains("mask")) return true;
            if (n.Contains("alpha")) return true;
            if (n.Contains("opacity")) return true;
            return false;
        }

        static int ClosestPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value)
            {
                power <<= 1;
            }
            return power;
        }
        /// <summary>
        /// Resize and compress the loaded texture, then store it locally.
        /// </summary>
        /// <param name="qi"></param>
        /// <returns></returns>
        public Texture2D GetResizedTextureFromBytes(ImageLoaderThreaded.QueuedImage qi)
        {
            if (!Settings.Instance.EnableTextureOptimizations.Value) return null;
            var path = qi.imgPath;

            if (LogUtil.IsSceneLoadActive() && qi.isThumbnail)
            {
                return null;
            }

            // Must be a power of two, otherwise mipmaps cannot be generated.
            // Start by dividing the size by 2.
            var localFormat = qi.tex.format;
            if (qi.tex.format == TextureFormat.RGBA32 || qi.tex.format == TextureFormat.ARGB32 || qi.tex.format == TextureFormat.BGRA32 || qi.tex.format == TextureFormat.DXT5)
            {
                localFormat = TextureFormat.DXT5;
            }
            else if (qi.tex.format == TextureFormat.RGB24 || qi.tex.format == TextureFormat.DXT1)
            {
                localFormat = TextureFormat.DXT1;
            }
            else
            {
                localFormat = TextureFormat.DXT5;
            }
            //string ext = localFormat == TextureFormat.DXT1 ? ".DXT1" : ".DXT5";

            int width = qi.tex.width;
            int height = qi.tex.height;

            GetResizedSize(ref width, ref height, qi.imgPath);

            var diskCachePath = GetDiskCachePath(qi,false,0,0);
            var realDiskCachePath = GetDiskCachePath(qi,true,width,height);
            var realDiskCachePathCache = realDiskCachePath + ".cache";

            LogUtil.BeginImageWork();
            try
            {

            Texture2D resultTexture = GetTextureFromCache(diskCachePath);
            // Not only the path is needed
            if (resultTexture!=null)
            {
                LogUtil.PerfAdd("Img.Cache.MemHit", 0, 0);
                LogUtil.LogTextureTrace("Img.Resize.MemHit:" + diskCachePath, "resize use mem cache:" + diskCachePath);
                UnityEngine.Object.Destroy(qi.tex);
                qi.tex = resultTexture;
                ResolveInflightWaiters(diskCachePath, resultTexture);
                return resultTexture;
            }

            //var thumbnailPath = diskCachePath + ext
            string diskPathToUse = File.Exists(realDiskCachePathCache) ? realDiskCachePathCache : realDiskCachePath;
            if (File.Exists(diskPathToUse))
            {
                LogUtil.PerfAdd("Img.Resize.FromDisk", 0, 0);
                LogUtil.LogTextureTrace("Img.Resize.DiskHit:" + diskPathToUse, "resize use disk cache:" + diskPathToUse);
                var swRead = System.Diagnostics.Stopwatch.StartNew();
                byte[] bytes = null;
                long bytesLen = 0;

                try
                {
                    using (var fs = new FileStream(diskPathToUse, FileMode.Open, FileAccess.Read))
                    {
                        int len = (int)fs.Length;
                        byte[] fileBytes = new byte[len];
                        int offset = 0;
                        int remaining = len;
                        while (remaining > 0)
                        {
                            int read = fs.Read(fileBytes, offset, remaining);
                            if (read <= 0) break;
                            offset += read;
                            remaining -= read;
                        }

                        // Check meta for compression
                        bool isCompressed = false;
                        string metaPath = diskCachePath + ".meta";
                        if (File.Exists(metaPath))
                        {
                            try
                            {
                                var metaJson = JSON.Parse(File.ReadAllText(metaPath));
                                if (metaJson["type"].Value == "compressed") isCompressed = true;
                            }
                            catch { }
                        }

                        if (isCompressed)
                        {
                            byte[] decompressed = ZstdCompressor.Decompress(fileBytes);
                            bytes = ByteArrayPool.Rent(decompressed.Length);
                            Array.Copy(decompressed, bytes, decompressed.Length);
                            bytesLen = decompressed.Length;
                        }
                        else
                        {
                            bytes = ByteArrayPool.Rent(len);
                            Array.Copy(fileBytes, bytes, len);
                            bytesLen = len;
                        }
                    }

                    LogUtil.PerfAdd("Img.Disk.Read", swRead.Elapsed.TotalMilliseconds, bytesLen);
                    LogUtil.LogTextureSlowDisk("read", diskPathToUse, swRead.Elapsed.TotalMilliseconds, bytesLen);

                    resultTexture = new Texture2D(width, height, localFormat, false, qi.linear);
                    //resultTexture.name = qi.cacheSignature;
                    resultTexture.LoadRawTextureData(bytes);
                    resultTexture.Apply();
                    RegisterTexture(diskCachePath, resultTexture);
                    ResolveInflightWaiters(diskCachePath, resultTexture);
                    return resultTexture;
                }
                finally
                {
                    if (bytes != null) ByteArrayPool.Return(bytes);
                }
            }


            LogUtil.LogTextureTrace("Img.Resize.Generate:" + realDiskCachePath, "resize generate cache:" + realDiskCachePath);

            // OPTIMIZATION: If no resize needed and already DXT, just grab the bytes.
            if (width == qi.tex.width && height == qi.tex.height && (qi.tex.format == TextureFormat.DXT1 || qi.tex.format == TextureFormat.DXT5))
            {
                try
                {
                    var bytes = qi.tex.GetRawTextureData();
                    if (bytes != null && bytes.Length > 0)
                    {
                        byte[] pooledBytes = ByteArrayPool.Rent(bytes.Length);
                        Array.Copy(bytes, pooledBytes, bytes.Length);

                        bool zstd = Settings.Instance.EnableZstdCompression.Value;
                        string targetPath = realDiskCachePath + ".cache";

                        JSONClass jSON = new JSONClass();
                        jSON["type"] = zstd ? "compressed" : "dxt";
                        jSON["width"].AsInt = qi.tex.width;
                        jSON["height"].AsInt = qi.tex.height;
                        jSON["resizedWidth"].AsInt = width;
                        jSON["resizedHeight"].AsInt = height;
                        jSON["format"] = qi.tex.format.ToString();

                        WriteDiskCacheAsync(targetPath, pooledBytes, bytes.Length, diskCachePath + ".meta", jSON.ToString(string.Empty), zstd, width, height, qi.tex.format, qi.linear);
                        
                        // We still need to return a result texture or update qi.tex
                        // but since we didn't resize, we can just use qi.tex as is.
                        RegisterTexture(diskCachePath, qi.tex);
                        ResolveInflightWaiters(diskCachePath, qi.tex);
                        return qi.tex;
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("Fast-path GetRawTextureData failed (sync), falling back to blit: " + ex.Message);
                }
            }

            // Whether an image is linear affects how qi.tex is displayed.
            var tempTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                qi.linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

            Graphics.SetRenderTarget(tempTexture);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, width, height, 0);
            // RenderTextures are reused, so it must be cleared first.
            GL.Clear(true, true, Color.clear);
            Graphics.Blit(qi.tex, tempTexture);
            //Graphics.DrawTexture(new Rect(0, 0, width, height), qi.tex);
            GL.PopMatrix();
            Graphics.SetRenderTarget(null);

            TextureFormat format= qi.tex.format;
            if (format == TextureFormat.DXT1)
                format = TextureFormat.RGB24;
            else
                format = TextureFormat.RGBA32;

            resultTexture = new Texture2D(width, height, format, false, qi.linear);
            //resultTexture.name = qi.cacheSignature;
            var previous = RenderTexture.active;
            RenderTexture.active = tempTexture;
            resultTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resultTexture.Apply();
            RenderTexture.active = previous;


            if (width > 0 && height > 0 && (width & (width - 1)) == 0 && (height & (height - 1)) == 0)
            {
                try { resultTexture.Compress(true); } catch (Exception ex) { LogUtil.LogError("Img convert compress failed " + ex + " path=" + qi.imgPath); }
            }
            RenderTexture.ReleaseTemporary(tempTexture);

            LogUtil.LogTextureTrace(
                "Img.Convert:" + qi.imgPath,
                string.Format("convert {0}:{1}({2},{3})mip:{4} isLinear:{5} -> {6}({7},{8})mip:{9}",
                    qi.imgPath,
                    qi.tex.format, qi.tex.width, qi.tex.height, qi.tex.mipmapCount, qi.linear,
                    resultTexture.format, width, height, resultTexture.mipmapCount)
            );

            byte[] texBytes = resultTexture.GetRawTextureData();
            
            bool enableZstd = Settings.Instance.EnableZstdCompression.Value;
            string targetCachePath = realDiskCachePath + ".cache";

            JSONClass jSONClass = new JSONClass();
            jSONClass["type"] = enableZstd ? "compressed" : "dxt";
            // Record the original texture size here.
            jSONClass["width"].AsInt = qi.tex.width;
            jSONClass["height"].AsInt = qi.tex.height;
            jSONClass["resizedWidth"].AsInt = width;
            jSONClass["resizedHeight"].AsInt = height;
            jSONClass["format"] = resultTexture.format.ToString();
            string contents = jSONClass.ToString(string.Empty);
            
            WriteDiskCacheAsync(targetCachePath, texBytes, texBytes.Length, diskCachePath + ".meta", contents, enableZstd, width, height, resultTexture.format, qi.linear);


            RegisterTexture(diskCachePath, resultTexture);

            ResolveInflightWaiters(diskCachePath, resultTexture);

            UnityEngine.Object.Destroy(qi.tex);
            qi.tex = resultTexture;
            return resultTexture;
            }
            catch
            {
                ResolveInflightWaiters(diskCachePath, null);
                throw;
            }
            finally
            {
                LogUtil.EndImageWork();
            }
        }

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

        void GetResizedSize(ref int width, ref int height, string path = null)
        {
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

            // If the source texture is already smaller than the minimum threshold,
            // do not downscale it further.
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

                if (originalWidth >= minSize)
                    width = Mathf.Max(width, minSize);
                if (originalHeight >= minSize)
                    height = Mathf.Max(height, minSize);
            }
            while (width > maxSize || height > maxSize)
            {
                width /= 2;
                height /= 2;
            }

            }

            if (originalWidth != width || originalHeight != height)
            {
                LogUtil.LogTextureTrace(
                    "Img.GetResizedSize:" + originalWidth + "x" + originalHeight + ":" + minSize + ":" + maxSize + ":" + width + "x" + height,
                    string.Format("GetResizedSize {0}x{1} min:{2} max:{3} -> {4}x{5}", originalWidth, originalHeight, minSize, maxSize, width, height)
                );
            }
        }

        protected string GetDiskCachePath(ImageLoaderThreaded.QueuedImage qi, bool useSize, int width, int height)
        {
            var textureCacheDir = VamHookPlugin.GetCacheDir();

            var imgPath = qi.imgPath;
            if (!string.IsNullOrEmpty(imgPath))
            {
                imgPath = imgPath.Replace('\\', '/');
            }

            string result = null;
            var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(imgPath);

            if (textureCacheDir != null)
            {
                string fileName = Path.GetFileName(imgPath);
                fileName = SanitizeFileName(fileName);
                if (fileName.Length > 100) fileName = fileName.Substring(0, 100);
                fileName = fileName.Replace('.', '_');
                string text = (fileEntry != null) ? fileEntry.Size.ToString() : "0";
                string token = (fileEntry != null) ? fileEntry.LastWriteTime.ToFileTime().ToString() : "0";
                var diskCacheSignature = GetDiskCacheSignature(qi, useSize, width, height);
                string finalName = fileName + "_" + text + "_" + token + "_" + diskCacheSignature;
                result = Path.Combine(textureCacheDir, finalName);
            }
            if (result == null) LogUtil.Log("GetDiskCachePath null. CacheDir=" + (textureCacheDir??"null") + " ImgPath=" + (imgPath??"null"));
            return result;
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

        static string BuildNonVarCacheToken(MVR.FileManagement.FileEntry fileEntry)
        {
            return fileEntry.LastWriteTime.ToFileTime().ToString();
        }

        protected string GetDiskCacheSignature(ImageLoaderThreaded.QueuedImage qi, bool useSize, int width,int height)
        {
            string text = useSize ? (width + "_" + height) : "";
            if (qi.compress && Settings.Instance != null && Settings.Instance.EnableZstdCompression.Value)
            {
                text += "_C";
            }
            if (qi.linear)
            {
                text += "_L";
            }
            if (qi.isNormalMap)
            {
                text += "_N";
            }
            if (qi.createAlphaFromGrayscale)
            {
                text += "_A";
            }
            if (qi.createNormalFromBump)
            {
                text += "_B";
            }
            if (qi.invert)
            {
                text += "_I";
            }
            return text;
        }
    }
}
