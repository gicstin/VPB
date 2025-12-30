using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using Valve.Newtonsoft.Json.Linq;

namespace var_browser
{
    /// <summary>
    /// Textures may need to be read later, so we cannot discard the CPU-side memory.
    /// </summary>
    public class ImageLoadingMgr : MonoBehaviour
    {
        static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();

        class PrewarmRequest
        {
            public string imgPath;
            public bool isThumbnail;
            public bool compress;
            public bool linear;
            public bool isNormalMap;
            public bool createAlphaFromGrayscale;
            public bool createNormalFromBump;
            public float bumpStrength;
            public bool invert;
            public string source;
        }

        [System.Serializable]
        public class ImageRequest
        {
            public string path;
            public Texture2D texture;
        }
        public static ImageLoadingMgr singleton;
        private void Awake()
        {
            singleton = this;
        }

        Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        readonly Queue<PrewarmRequest> prewarmQueue = new Queue<PrewarmRequest>();
        readonly HashSet<string> prewarmQueuedKeys = new HashSet<string>();
        Coroutine prewarmCoroutine;

        class ResizeJob
        {
            public ImageLoaderThreaded.QueuedImage qi;
            public string key;
        }

        readonly Queue<ResizeJob> resizeQueue = new Queue<ResizeJob>();
        readonly HashSet<string> resizeQueuedKeys = new HashSet<string>();
        Coroutine resizeCoroutine;

        double prewarmEwmaMs = 50.0;
        bool prewarmEwmaInit;

        Dictionary<string, List<ImageLoaderThreaded.QueuedImage>> inflightWaiters = new Dictionary<string, List<ImageLoaderThreaded.QueuedImage>>();
        HashSet<string> inflightKeys = new HashSet<string>();

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
            if (qi == null) return false;
            if (qi.tex == null) return false;

            try
            {
                if (Settings.Instance == null) return false;
                if (Settings.Instance.ReduceTextureSize == null || !Settings.Instance.ReduceTextureSize.Value) return false;
            }
            catch { return false; }

            string key = GetDiskCachePath(qi, false, 0, 0);
            if (string.IsNullOrEmpty(key)) return false;

            // If no resize would occur, don't generate cache.
            int w = qi.tex.width;
            int h = qi.tex.height;
            GetResizedSize(ref w, ref h);
            if (w == qi.tex.width && h == qi.tex.height) return false;

            // If disk cache already exists, skip.
            try
            {
                var real = GetDiskCachePath(qi, true, w, h);
                if (!string.IsNullOrEmpty(real))
                {
                    if (File.Exists(real + ".cache") || File.Exists(real)) return false;
                }
            }
            catch { }

            if (resizeQueuedKeys.Contains(key)) return false;

            resizeQueuedKeys.Add(key);
            resizeQueue.Enqueue(new ResizeJob { qi = qi, key = key });
            EnsureResizeCoroutine();
            return true;
        }

        IEnumerator ResizeWorker()
        {
            WaitForEndOfFrame wait = new WaitForEndOfFrame();
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            while (true)
            {
                if (resizeQueue.Count == 0)
                {
                    resizeCoroutine = null;
                    yield break;
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

                    if (sw.Elapsed.TotalMilliseconds > 5.0)
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
            GetResizedSize(ref width, ref height);

            var realDiskCachePath = GetDiskCachePath(qi, true, width, height);
            if (string.IsNullOrEmpty(realDiskCachePath)) return;
            var realDiskCachePathCache = realDiskCachePath + ".cache";

            if (File.Exists(realDiskCachePathCache) || File.Exists(realDiskCachePath)) return;

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

                tmp.Compress(true);

                var bytes = tmp.GetRawTextureData();
                // Copy to pooled buffer for async write to avoid GC alloc
                byte[] pooledBytes = ByteArrayPool.Rent(bytes.Length);
                Array.Copy(bytes, pooledBytes, bytes.Length);
                int realLength = bytes.Length;
                
                JSONClass jSONClass = new JSONClass();
                jSONClass["type"] = "image";
                jSONClass["width"].AsInt = qi.tex.width;
                jSONClass["height"].AsInt = qi.tex.height;
                jSONClass["resizedWidth"].AsInt = width;
                jSONClass["resizedHeight"].AsInt = height;
                jSONClass["format"] = tmp.format.ToString();
                
                WriteDiskCacheAsync(realDiskCachePathCache, pooledBytes, realLength, diskCachePath + ".meta", jSONClass.ToString(string.Empty));

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

        void WriteDiskCacheAsync(string pathCache, byte[] bytes, int length, string pathMeta, string metaContent)
        {
            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    if (bytes != null && !string.IsNullOrEmpty(pathCache))
                    {
                        using (FileStream fs = new FileStream(pathCache, FileMode.Create, FileAccess.Write))
                        {
                            fs.Write(bytes, 0, length);
                        }
                    }
                    if (!string.IsNullOrEmpty(metaContent) && !string.IsNullOrEmpty(pathMeta))
                        File.WriteAllText(pathMeta, metaContent);
                }
                catch { }
                finally
                {
                    if (bytes != null) ByteArrayPool.Return(bytes);
                }
            });
        }

        public bool Request(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return false;
            var imgPath = qi.imgPath;
            if (string.IsNullOrEmpty(imgPath)) return false;

            LogUtil.MarkImageActivity();
            var swRequest = System.Diagnostics.Stopwatch.StartNew();
            var diskCachePath = GetDiskCachePath(qi,false,0,0);
            if (string.IsNullOrEmpty(diskCachePath)) return false;

            //LogUtil.Log("request img:"+ diskCachePath);

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
            if (metaToUse != null)
            {
                var jsonString = File.ReadAllText(metaToUse);
                JSONNode jSONNode = JSON.Parse(jsonString);
                JSONClass asObject = jSONNode.AsObject;
                if (asObject != null)
                {
                    if (asObject["width"] != null) width = asObject["width"].AsInt;
                    if (asObject["height"] != null) height = asObject["height"].AsInt;
                    if (asObject["format"] != null) textureFormat = (TextureFormat)System.Enum.Parse(typeof(TextureFormat), asObject["format"]);
                }

                GetResizedSize(ref width, ref height);

                var realDiskCachePath = GetDiskCachePath(qi, true,width, height);
                var diskPathToUse = realDiskCachePath + ".cache";
                if (!File.Exists(diskPathToUse))
                {
                    // Backward-compat for caches previously written without extension.
                    diskPathToUse = realDiskCachePath;
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
                            bytes = ByteArrayPool.Rent(len);
                            bytesLen = len;
                            int offset = 0;
                            int remaining = len;
                            while (remaining > 0)
                            {
                                int read = fs.Read(bytes, offset, remaining);
                                if (read <= 0) break;
                                offset += read;
                                remaining -= read;
                            }
                        }

                        LogUtil.PerfAdd("Img.Disk.Read", swRead.Elapsed.TotalMilliseconds, bytesLen);
                        LogUtil.LogTextureSlowDisk("read", diskPathToUse, swRead.Elapsed.TotalMilliseconds, bytesLen);

                        tex = new Texture2D(width, height, textureFormat, false, qi.linear);
                        tex.LoadRawTextureData(bytes);
                        success = true;
                    }
                    catch (System.Exception ex)
                    {
                        LogUtil.LogError("request load disk cache fail:" + diskPathToUse + " " + ex.ToString());
                        try { File.Delete(diskPathToUse); } catch { }
                    }
                    finally
                    {
                        if (bytes != null) ByteArrayPool.Return(bytes);
                    }

                    if (success)
                    {
                        tex.Apply();
                        qi.tex = tex;

                        RegisterTexture(diskCachePath, tex);

                        Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                        LogUtil.PerfAdd("Mgr.Request", swRequest.Elapsed.TotalMilliseconds, 0);
                        return true;
                    }
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

        public void StartScenePrewarm(string sceneSaveName, string sceneJsonText)
        {
            if (Settings.Instance == null || Settings.Instance.ScenePrewarmEnabled == null || !Settings.Instance.ScenePrewarmEnabled.Value)
            {
                return;
            }
            if (Settings.Instance.ReduceTextureSize == null || !Settings.Instance.ReduceTextureSize.Value)
            {
                return;
            }
            if (string.IsNullOrEmpty(sceneJsonText))
            {
                return;
            }

            JObject root;
            try
            {
                root = JObject.Parse(sceneJsonText);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("PREWARM parse scene json failed: " + sceneSaveName + " " + ex.ToString());
                return;
            }

            var roots = new List<KeyValuePair<string, JObject>>();
            roots.Add(new KeyValuePair<string, JObject>(sceneSaveName, root));

            try
            {
                CollectReferencedJsonRoots(root, sceneSaveName, roots);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("PREWARM deep json scan failed: " + sceneSaveName + " " + ex.ToString());
            }

            var requests = new List<PrewarmRequest>(64);
            for (int i = 0; i < roots.Count; i++)
            {
                var kv = roots[i];
                var src = kv.Key;
                var r = kv.Value;
                if (r == null) continue;
                var sub = BuildPrewarmRequestsFromScene(r, src);
                if (sub == null || sub.Count == 0) continue;
                requests.AddRange(sub);
            }
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            int added = 0;
            for (int i = 0; i < requests.Count; i++)
            {
                var req = requests[i];
                if (req == null || string.IsNullOrEmpty(req.imgPath)) continue;
                if (TryEnqueuePrewarm(req))
                {
                    added++;
                }
            }

            if (added > 0)
            {
                LogUtil.Log("PREWARM start scene=" + sceneSaveName + " requests=" + added);
                EnsurePrewarmCoroutine();
            }
        }

        static bool IsLikelyJsonPathValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            string v;
            try { v = value.Trim(); } catch { v = value; }
            if (string.IsNullOrEmpty(v)) return false;

            try
            {
                if (v.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
            }
            catch { }

            string lower;
            try { lower = v.ToLowerInvariant(); } catch { lower = v; }
            return lower.EndsWith(".json");
        }

        static void CollectJsonValues(JToken token, List<string> found)
        {
            if (token == null) return;

            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop == null) continue;
                    var v = prop.Value;
                    if (v != null && v.Type == JTokenType.String)
                    {
                        string s = null;
                        try { s = v.Value<string>(); } catch { }
                        if (!string.IsNullOrEmpty(s) && IsLikelyJsonPathValue(s))
                        {
                            found.Add(s);
                        }
                    }
                    CollectJsonValues(prop.Value, found);
                }
                return;
            }

            var arr = token as JArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item != null && item.Type == JTokenType.String)
                    {
                        string s = null;
                        try { s = item.Value<string>(); } catch { }
                        if (!string.IsNullOrEmpty(s) && IsLikelyJsonPathValue(s))
                        {
                            found.Add(s);
                        }
                        continue;
                    }
                    CollectJsonValues(item, found);
                }
                return;
            }
        }

        static string TryReadAllTextPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch { }

            try
            {
                using (var fileEntryStream = MVR.FileManagement.FileManager.OpenStream(path, true))
                {
                    using (var sr = new StreamReader(fileEntryStream.Stream))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        void CollectReferencedJsonRoots(JObject sceneRoot, string sceneSaveName, List<KeyValuePair<string, JObject>> roots)
        {
            if (sceneRoot == null) return;
            if (roots == null) return;

            int maxDepth = 3;
            int maxFiles = 64;
            var visited = new HashSet<string>();
            var queue = new Queue<KeyValuePair<string, int>>();

            var initial = new List<string>(32);
            CollectJsonValues(sceneRoot, initial);
            for (int i = 0; i < initial.Count; i++)
            {
                var p = initial[i];
                if (string.IsNullOrEmpty(p)) continue;
                queue.Enqueue(new KeyValuePair<string, int>(p, 1));
            }

            while (queue.Count > 0)
            {
                var kv = queue.Dequeue();
                var jsonPath = kv.Key;
                var depth = kv.Value;

                if (string.IsNullOrEmpty(jsonPath)) continue;
                if (depth > maxDepth) continue;
                if (visited.Count >= maxFiles) break;

                string canonical = ResolveCanonicalImagePath(jsonPath);
                if (string.IsNullOrEmpty(canonical)) continue;
                if (visited.Contains(canonical)) continue;
                visited.Add(canonical);

                var text = TryReadAllTextPath(canonical);
                if (string.IsNullOrEmpty(text)) continue;

                JObject root;
                try
                {
                    root = JObject.Parse(text);
                }
                catch
                {
                    continue;
                }

                roots.Add(new KeyValuePair<string, JObject>(sceneSaveName + ":" + canonical, root));

                var next = new List<string>(16);
                CollectJsonValues(root, next);
                for (int i = 0; i < next.Count; i++)
                {
                    var p = next[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    queue.Enqueue(new KeyValuePair<string, int>(p, depth + 1));
                }
            }

            LogUtil.Log("PREWARM deepjson scene=" + sceneSaveName + " roots=" + roots.Count + " jsonFiles=" + visited.Count);
        }

        void EnsurePrewarmCoroutine()
        {
            if (prewarmCoroutine != null) return;
            try
            {
                prewarmCoroutine = StartCoroutine(PrewarmWorker());
            }
            catch (Exception ex)
            {
                LogUtil.LogError("PREWARM start coroutine failed: " + ex.ToString());
            }
        }

        bool TryEnqueuePrewarm(PrewarmRequest req)
        {
            string canonicalPath = ResolveCanonicalImagePath(req.imgPath);
            if (string.IsNullOrEmpty(canonicalPath))
            {
                return false;
            }

            var qi = new ImageLoaderThreaded.QueuedImage();
            qi.imgPath = canonicalPath;
            qi.isThumbnail = req.isThumbnail;
            qi.compress = req.compress;
            qi.linear = req.linear;
            qi.isNormalMap = req.isNormalMap;
            qi.createAlphaFromGrayscale = req.createAlphaFromGrayscale;
            qi.createNormalFromBump = req.createNormalFromBump;
            qi.bumpStrength = req.bumpStrength;
            qi.invert = req.invert;

            string key = GetDiskCachePath(qi, false, 0, 0);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            if (prewarmQueuedKeys.Contains(key))
            {
                return false;
            }

            req.imgPath = canonicalPath;
            prewarmQueuedKeys.Add(key);
            prewarmQueue.Enqueue(req);
            LogUtil.Log("PREWARM enqueue src=" + (req.source ?? "?") + " path=" + canonicalPath + " flags=" + GetDiskCacheSignature(qi, true, 0, 0));

            return true;
        }

        IEnumerator PrewarmWorker()
        {
            WaitForEndOfFrame wait = new WaitForEndOfFrame();
            System.Diagnostics.Stopwatch swFrame = new System.Diagnostics.Stopwatch();
            while (true)
            {
                if (prewarmQueue.Count == 0)
                {
                    prewarmCoroutine = null;
                    yield break;
                }

                swFrame.Reset();
                swFrame.Start();

                while (prewarmQueue.Count > 0)
                {
                    var req = prewarmQueue.Dequeue();
                    if (req != null && !string.IsNullOrEmpty(req.imgPath))
                    {
                        try
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            PrewarmImage(req);
                            sw.Stop();
                            UpdatePrewarmCost(sw.Elapsed.TotalMilliseconds);
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("PREWARM error path=" + req.imgPath + " " + ex.ToString());
                        }
                    }

                    // Budget of 5ms per frame to match ResizeWorker
                    if (swFrame.Elapsed.TotalMilliseconds > 5.0)
                    {
                        break;
                    }
                }

                yield return wait;
            }
        }

        void PrewarmImage(PrewarmRequest req)
        {
            var qi = new ImageLoaderThreaded.QueuedImage();
            qi.imgPath = req.imgPath;
            qi.isThumbnail = req.isThumbnail;
            qi.compress = req.compress;
            qi.linear = req.linear;
            qi.isNormalMap = req.isNormalMap;
            qi.createAlphaFromGrayscale = req.createAlphaFromGrayscale;
            qi.createNormalFromBump = req.createNormalFromBump;
            qi.bumpStrength = req.bumpStrength;
            qi.invert = req.invert;

            string diskCachePath = GetDiskCachePath(qi, false, 0, 0);
            if (string.IsNullOrEmpty(diskCachePath))
            {
                return;
            }

            var cacheTexture = GetTextureFromCache(diskCachePath);
            if (cacheTexture != null)
            {
                LogUtil.Log("PREWARM hit.mem path=" + req.imgPath);
                return;
            }

            var metaPath = diskCachePath + ".meta";
            int width = 0;
            int height = 0;
            if (File.Exists(metaPath))
            {
                try
                {
                    var jsonString = File.ReadAllText(metaPath);
                    JSONNode jSONNode = JSON.Parse(jsonString);
                    JSONClass asObject = jSONNode.AsObject;
                    if (asObject != null)
                    {
                        if (asObject["width"] != null) width = asObject["width"].AsInt;
                        if (asObject["height"] != null) height = asObject["height"].AsInt;
                    }

                    GetResizedSize(ref width, ref height);
                    var realDiskCachePath = GetDiskCachePath(qi, true, width, height);
                    var realDiskCachePathCache = realDiskCachePath + ".cache";
                    if (File.Exists(realDiskCachePathCache) || File.Exists(realDiskCachePath))
                    {
                        LogUtil.Log("PREWARM hit.disk path=" + req.imgPath + " cache=" + (File.Exists(realDiskCachePathCache) ? realDiskCachePathCache : realDiskCachePath));
                        return;
                    }
                }
                catch { }

                if (width > 0 && height > 0)
                {
                    GetResizedSize(ref width, ref height);
                    var realDiskCachePath = GetDiskCachePath(qi, true, width, height);
                    var realDiskCachePathCache = realDiskCachePath + ".cache";
                    if (File.Exists(realDiskCachePathCache) || File.Exists(realDiskCachePath))
                    {
                        LogUtil.Log("PREWARM hit.disk path=" + req.imgPath + " cache=" + (File.Exists(realDiskCachePathCache) ? realDiskCachePathCache : realDiskCachePath));
                        return;
                    }
                }
            }

            byte[] bytes = null;
            try
            {
                using (var fileEntryStream = MVR.FileManagement.FileManager.OpenStream(req.imgPath, true))
                {
                    using (var ms = new MemoryStream())
                    {
                        var s = fileEntryStream.Stream;
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ms.Write(buffer, 0, read);
                        }
                        bytes = ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("PREWARM read fail path=" + req.imgPath + " " + ex.ToString());
                return;
            }

            if (bytes == null || bytes.Length == 0)
            {
                return;
            }

            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, qi.linear);
                if (!tex.LoadImage(bytes, false))
                {
                    UnityEngine.Object.Destroy(tex);
                    return;
                }
                qi.tex = tex;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                
                Texture2D resized = GetResizedTextureFromBytes(qi);
                
                if (resized != null)
                {
                    LogUtil.Log("PREWARM generate ok path=" + req.imgPath + " ms=" + sw.Elapsed.TotalMilliseconds.ToString("0") + " resized=" + resized.width + "x" + resized.height);

                    string k = GetDiskCachePath(qi, false, 0, 0);
                    if (!string.IsNullOrEmpty(k))
                    {
                        cache.Remove(k);
                    }
                    UnityEngine.Object.Destroy(resized);
                    qi.tex = null;
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("PREWARM generate fail path=" + req.imgPath + " " + ex.ToString());
            }
            finally
            {
                if (qi != null && qi.tex != null)
                {
                    UnityEngine.Object.Destroy(qi.tex);
                    qi.tex = null;
                }
            }
        }

        static bool IsUrlFieldName(string name)
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

        static bool IsLikelyNormalMapSlot(string slotName)
        {
            if (string.IsNullOrEmpty(slotName)) return false;
            string n;
            try { n = slotName.ToLowerInvariant(); } catch { n = slotName; }
            return n.Contains("normal");
        }

        static bool IsLikelyThumbnailSlot(string slotName)
        {
            if (string.IsNullOrEmpty(slotName)) return false;
            string n;
            try { n = slotName.ToLowerInvariant(); } catch { n = slotName; }
            return n.Contains("thumb");
        }

        static bool IsLikelyAlphaPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p;
            try { p = path.ToLowerInvariant(); } catch { p = path; }
            return p.Contains("alpha") || p.Contains("opacity");
        }

        static bool IsLikelyNormalMapPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string p;
            try { p = path.ToLowerInvariant(); } catch { p = path; }
            if (p.Contains("normal")) return true;
            if (p.Contains("_nrm")) return true;
            if (p.EndsWith("_n.png") || p.EndsWith("_n.jpg") || p.EndsWith("_n.jpeg")) return true;
            return false;
        }

        static bool IsLikelyFilePathValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            string v;
            try { v = value.Trim(); } catch { v = value; }
            if (string.IsNullOrEmpty(v)) return false;

            bool looksLikePath = false;
            try
            {
                looksLikePath = v.Contains(":/") || v.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) || v.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                looksLikePath = v.Contains(":/");
            }
            if (!looksLikePath) return false;

            string lower;
            try { lower = v.ToLowerInvariant(); } catch { lower = v; }

            if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".tga") || lower.EndsWith(".bmp") || lower.EndsWith(".tif") || lower.EndsWith(".tiff")) return true;
            if (lower.EndsWith(".assetbundle") || lower.EndsWith(".shaderbundle") || lower.EndsWith(".audiobundle")) return true;

            return false;
        }

        List<PrewarmRequest> BuildPrewarmRequestsFromScene(JObject root, string sceneSaveName)
        {
            var list = new List<PrewarmRequest>(64);
            if (root == null) return list;

            var found = new List<KeyValuePair<string, string>>(64);
            CollectUrlValues(root, found);

            string contextPackageUid = null;
            try
            {
                int idx = sceneSaveName != null ? sceneSaveName.IndexOf(":/", StringComparison.Ordinal) : -1;
                if (idx > 0)
                {
                    contextPackageUid = sceneSaveName.Substring(0, idx);
                }
            }
            catch { }

            for (int i = 0; i < found.Count; i++)
            {
                var kv = found[i];
                var slotName = kv.Key;
                var path = kv.Value;
                if (string.IsNullOrEmpty(path)) continue;
                if (!IsLikelyFilePathValue(path)) continue;

                if (path.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(contextPackageUid))
                {
                    try
                    {
                        path = contextPackageUid + ":/" + path.Substring("SELF:/".Length);
                    }
                    catch { }
                }

                bool createAlphaFromGrayscale = IsLikelyAlphaPath(slotName) || IsLikelyAlphaPath(path);
                bool isNormalMap = IsLikelyNormalMapSlot(slotName) || IsLikelyNormalMapPath(path);
                bool linear = IsLikelyDataTextureSlot(slotName) || isNormalMap;
                bool isThumb = IsLikelyThumbnailSlot(slotName);

                var req = new PrewarmRequest();
                req.imgPath = path;
                req.isThumbnail = isThumb;
                req.compress = true;
                req.linear = linear;
                req.isNormalMap = isNormalMap;
                req.createAlphaFromGrayscale = createAlphaFromGrayscale;
                req.createNormalFromBump = false;
                req.bumpStrength = 1f;
                req.invert = false;
                req.source = sceneSaveName + ":" + slotName;
                list.Add(req);

                if (!isThumb)
                {
                    var req2 = new PrewarmRequest();
                    req2.imgPath = path;
                    req2.isThumbnail = true;
                    req2.compress = true;
                    req2.linear = linear;
                    req2.isNormalMap = isNormalMap;
                    req2.createAlphaFromGrayscale = createAlphaFromGrayscale;
                    req2.createNormalFromBump = false;
                    req2.bumpStrength = 1f;
                    req2.invert = false;
                    req2.source = sceneSaveName + ":" + slotName + ":thumb";
                    list.Add(req2);
                }
            }

            return list;
        }

        static void CollectUrlValues(JToken token, List<KeyValuePair<string, string>> found)
        {
            if (token == null) return;

            var obj = token as JObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    if (prop == null) continue;
                    var name = prop.Name;
                    if (IsUrlFieldName(name))
                    {
                        var v = prop.Value;
                        if (v != null && v.Type == JTokenType.String)
                        {
                            string s = null;
                            try { s = v.Value<string>(); } catch { }
                            if (!string.IsNullOrEmpty(s))
                            {
                                found.Add(new KeyValuePair<string, string>(name, s));
                            }
                        }
                    }
                    else
                    {
                        var v = prop.Value;
                        if (v != null && v.Type == JTokenType.String)
                        {
                            string s = null;
                            try { s = v.Value<string>(); } catch { }
                            if (!string.IsNullOrEmpty(s) && IsLikelyFilePathValue(s))
                            {
                                found.Add(new KeyValuePair<string, string>(name, s));
                            }
                        }
                    }

                    CollectUrlValues(prop.Value, found);
                }
                return;
            }

            var arr = token as JArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    var item = arr[i];
                    if (item != null && item.Type == JTokenType.String)
                    {
                        string s = null;
                        try { s = item.Value<string>(); } catch { }
                        if (!string.IsNullOrEmpty(s) && IsLikelyFilePathValue(s))
                        {
                            found.Add(new KeyValuePair<string, string>("array", s));
                        }
                        continue;
                    }

                    CollectUrlValues(item, found);
                }
                return;
            }
        }

        static string ResolveCanonicalImagePath(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath)) return imgPath;

            try
            {
                if (imgPath.IndexOf(".latest", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(imgPath);
                    if (fileEntry != null)
                    {
                        try
                        {
                            var uid = fileEntry.Uid;
                            if (!string.IsNullOrEmpty(uid) && uid.IndexOf(".latest", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                return uid;
                            }
                        }
                        catch { }
                        try
                        {
                            var p = fileEntry.Path;
                            if (!string.IsNullOrEmpty(p) && p.IndexOf(".latest", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                return p;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            string packageUid;
            string internalPath;
            if (!TrySplitVarPath(imgPath, out packageUid, out internalPath))
            {
                return imgPath;
            }

            if (!packageUid.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
            {
                return imgPath;
            }

            VarPackage resolved = ResolveLatestInstalledPackage(packageUid);
            if (resolved == null)
            {
                return imgPath;
            }

            return resolved.Uid + ":/" + internalPath;
        }

        static bool TrySplitVarPath(string path, out string packageUidOrRef, out string internalPath)
        {
            packageUidOrRef = null;
            internalPath = null;
            if (string.IsNullOrEmpty(path)) return false;

            int idx = path.IndexOf(":/", StringComparison.Ordinal);
            if (idx <= 0) return false;

            packageUidOrRef = path.Substring(0, idx);
            internalPath = path.Substring(idx + 2);
            return !string.IsNullOrEmpty(packageUidOrRef) && !string.IsNullOrEmpty(internalPath);
        }

        static VarPackage ResolveLatestInstalledPackage(string packageLatestRef)
        {
            if (string.IsNullOrEmpty(packageLatestRef)) return null;
            if (!packageLatestRef.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var pkg = var_browser.FileManager.GetPackage(packageLatestRef);
                if (pkg == null) return null;

                VarPackageGroup group = pkg.Group;
                if (group == null) return pkg;

                VarPackage bestInstalled = null;
                if (group.Packages != null)
                {
                    for (int i = 0; i < group.Packages.Count; i++)
                    {
                        var p = group.Packages[i];
                        if (p == null) continue;
                        if (!p.IsInstalled()) continue;
                        if (bestInstalled == null || p.Version > bestInstalled.Version)
                        {
                            bestInstalled = p;
                        }
                    }
                }
                return bestInstalled ?? pkg;
            }
            catch
            {
                return null;
            }
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

            GetResizedSize(ref width, ref height);

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
                        bytes = ByteArrayPool.Rent(len);
                        bytesLen = len;
                        int offset = 0;
                        int remaining = len;
                        while (remaining > 0)
                        {
                            int read = fs.Read(bytes, offset, remaining);
                            if (read <= 0) break;
                            offset += read;
                            remaining -= read;
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


            resultTexture.Compress(true);
            RenderTexture.ReleaseTemporary(tempTexture);

            LogUtil.LogTextureTrace(
                "Img.Convert:" + qi.imgPath,
                string.Format("convert {0}:{1}({2},{3})mip:{4} isLinear:{5} -> {6}({7},{8})mip:{9}",
                    qi.imgPath,
                    qi.tex.format, qi.tex.width, qi.tex.height, qi.tex.mipmapCount, qi.linear,
                    resultTexture.format, width, height, resultTexture.mipmapCount)
            );

            byte[] texBytes = resultTexture.GetRawTextureData();
            
            JSONClass jSONClass = new JSONClass();
            jSONClass["type"] = "image";
            // Record the original texture size here.
            jSONClass["width"].AsInt = qi.tex.width;
            jSONClass["height"].AsInt = qi.tex.height;
            jSONClass["resizedWidth"].AsInt = width;
            jSONClass["resizedHeight"].AsInt = height;
            jSONClass["format"] = resultTexture.format.ToString();
            string contents = jSONClass.ToString(string.Empty);
            
            WriteDiskCacheAsync(realDiskCachePathCache, texBytes, texBytes.Length, diskCachePath + ".meta", contents);


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

        void GetResizedSize(ref int width,ref int height)
        {
            int originalWidth = width;
            int originalHeight = height;

            int minSize = Settings.Instance.MinTextureSize != null ? Settings.Instance.MinTextureSize.Value : 2048;
            minSize = Mathf.Clamp(minSize, 2048, 8192);

            bool forceToMin = Settings.Instance.ForceTextureToMinSize != null && Settings.Instance.ForceTextureToMinSize.Value;

            int maxSize = Settings.Instance.MaxTextureSize.Value;
            if (maxSize < minSize) maxSize = minSize;

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

            string result = null;
            var fileEntry = MVR.FileManagement.FileManager.GetFileEntry(imgPath);

            if (textureCacheDir != null)
            {
                string basePath = textureCacheDir + "/";
                string fileName = Path.GetFileName(imgPath);
                fileName = SanitizeFileName(fileName);
                fileName = fileName.Replace('.', '_');
                string text = (fileEntry != null) ? fileEntry.Size.ToString() : "0";
                string token = (fileEntry != null) ? fileEntry.LastWriteTime.ToFileTime().ToString() : "0";
                var diskCacheSignature = GetDiskCacheSignature(qi, useSize, width, height);
                result = basePath + fileName + "_" + text + "_" + token + "_" + diskCacheSignature;
            }
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
            if (qi.compress)
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
                text = text + "_BN" + qi.bumpStrength;
            }
            if (qi.invert)
            {
                text += "_I";
            }
            if (qi.isThumbnail)
            {
                text += "_T";
            }
            return text;
        }

        void UpdatePrewarmCost(double ms)
        {
            if (ms <= 0) return;
            if (!prewarmEwmaInit)
            {
                prewarmEwmaMs = ms;
                prewarmEwmaInit = true;
                return;
            }
            double a = 0.15;
            prewarmEwmaMs = (prewarmEwmaMs * (1.0 - a)) + (ms * a);
            if (prewarmEwmaMs < 0.1) prewarmEwmaMs = 0.1;
            if (prewarmEwmaMs > 5000.0) prewarmEwmaMs = 5000.0;
        }

        int GetAdaptivePrewarmPerFrame(int maxPerFrame)
        {
            if (maxPerFrame <= 1) return 1;
            double frameMs;
            try { frameMs = Time.unscaledDeltaTime * 1000.0; } catch { frameMs = 0.0; }
            if (frameMs <= 0.0) return 1;

            double targetFrameMs = 33.33;
            double spare = targetFrameMs - frameMs;
            if (spare < 0.0) spare = 0.0;

            double budgetMs = 6.0 + (spare * 0.75);
            int n = 1;
            if (prewarmEwmaMs > 0.1)
            {
                n = 1 + (int)System.Math.Floor(budgetMs / prewarmEwmaMs);
            }
            if (n < 1) n = 1;
            if (n > maxPerFrame) n = maxPerFrame;
            return n;
        }
    }
}
