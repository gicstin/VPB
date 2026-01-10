using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using StbImageSharp; 
using Hebron.Runtime;

namespace VPB
{
    public class HubImageLoaderThreaded : MonoBehaviour
    {
        public delegate void ImageLoaderCallback(QueuedImage qi);

        public class QueuedImage
        {
            public void Reset()
            {
                isThumbnail = false;
                imgPath = null;
                skipCache = false;
                forceReload = false;
                createMipMaps = false;
                compress = true;
                linear = false;
                processed = false;
                preprocessed = false;
                loadedFromCache = false;
                cancel = false;
                finished = false;
                isNormalMap = false;
                createAlphaFromGrayscale = false;
                createNormalFromBump = false;
                bumpStrength = 1f;
                invert = false;
                setSize = false;
                fillBackground = false;
                width = 0;
                height = 0;
                if (raw != null)
                {
                    ByteArrayPool.Return(raw);
                }
                raw = null;
                hadError = false;
                errorText = null;
                textureFormat = TextureFormat.RGBA32;
                tex = null;
                rawImageToLoad = null;
                useWebCache = false;
                webRequest = null;
                webRequestDone = false;
                webRequestHadError = false;
                webRequestData = null;
                callback = null;
                priority = 1000;
                insertionIndex = 0;
                groupId = null;
            }

            public int priority;
            public long insertionIndex;
            public string groupId;
            public bool isThumbnail;
            public string imgPath;
            public bool skipCache;
            public bool forceReload;
            public bool createMipMaps;
            public bool compress = true;
            public bool linear;
            public bool processed;
            public bool preprocessed;
            public bool loadedFromCache;
            public bool cancel;
            public bool finished;
            public bool isNormalMap;
            public bool createAlphaFromGrayscale;
            public bool createNormalFromBump;
            public float bumpStrength = 1f;
            public bool invert;
            public bool setSize;
            public bool fillBackground;
            public int width;
            public int height;
            public byte[] raw;
            public bool hadError;
            public string errorText;
            public TextureFormat textureFormat;
            public Texture2D tex;
            public RawImage rawImageToLoad;
            public bool useWebCache;
            public UnityWebRequest webRequest;
            public bool webRequestDone;
            public bool webRequestHadError;
            public byte[] webRequestData;
            public ImageLoaderCallback callback;

            public string cacheSignature
            {
                get
                {
                    string text = imgPath;
                    if (compress) text += ":C";
                    if (linear) text += ":L";
                    if (isNormalMap) text += ":N";
                    if (createAlphaFromGrayscale) text += ":A";
                    if (createNormalFromBump) text = text + ":BN" + bumpStrength;
                    if (invert) text += ":I";
                    return text;
                }
            }

            protected string diskCacheSignature
            {
                get
                {
                    string text = ((!setSize) ? string.Empty : (width + "_" + height));
                    if (compress) text += "_C";
                    if (linear) text += "_L";
                    if (isNormalMap) text += "_N";
                    if (createAlphaFromGrayscale) text += "_A";
                    if (createNormalFromBump) text = text + "_BN" + bumpStrength;
                    if (invert) text += "_I";
                    return text;
                }
            }

            protected string GetDiskCachePath()
            {
                string result = null;
                FileEntry fileEntry = FileManager.GetFileEntry(imgPath);
                string textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
                if (fileEntry != null && textureCacheDir != null)
                {
                    string text = fileEntry.Size.ToString();
                    string text2 = fileEntry.LastWriteTime.ToFileTime().ToString();
                    string text3 = textureCacheDir + "/";
                    string fileName = Path.GetFileName(imgPath);
                    fileName = fileName.Replace('.', '_');
                    result = text3 + fileName + "_" + text + "_" + text2 + "_" + diskCacheSignature + ".vamcache";
                }
                return result;
            }

            protected string GetWebCachePath()
            {
                string result = null;
                string textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
                if (textureCacheDir != null)
                {
                    string text = imgPath.Replace("https://", string.Empty);
                    text = text.Replace("http://", string.Empty);
                    text = text.Replace("/", "__");
                    text = text.Replace("?", "_");
                    string text2 = textureCacheDir + "/";
                    result = text2 + text + "_" + diskCacheSignature + ".vamcache";
                }
                return result;
            }

            public bool WebCachePathExists()
            {
                string webCachePath = GetWebCachePath();
                return webCachePath != null && FileManager.FileExists(webCachePath);
            }

            public void CreateTexture()
            {
                if (tex == null)
                {
                    if (width <= 0 || height <= 0) return;
                    try
                    {
                        tex = new Texture2D(width, height, textureFormat, createMipMaps, linear);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError(imgPath + " " + ex);
                    }
                    if (tex != null) tex.name = cacheSignature;
                }
            }

            protected void ReadMetaJson(string jsonString)
            {
                JSONNode jSONNode = JSON.Parse(jsonString);
                JSONClass asObject = jSONNode.AsObject;
                if (asObject != null)
                {
                    if (asObject["width"] != null) width = asObject["width"].AsInt;
                    if (asObject["height"] != null) height = asObject["height"].AsInt;
                    if (asObject["format"] != null) textureFormat = (TextureFormat)Enum.Parse(typeof(TextureFormat), asObject["format"]);
                }
            }

            protected void ProcessFromStream(Stream st)
            {
                StbImage.stbi_set_flip_vertically_on_load(1);
                Stream streamToUse = st;
                MemoryStream ms = null;
                int num3 = 3;
                int num8 = 0;

                unsafe {
                    byte* result = null;
                    int x = 0, y = 0, comp = 0;
                    try 
                    {
                        if (!st.CanSeek)
                        {
                            ms = new MemoryStream();
                            byte[] buffer = ByteArrayPool.Rent(4096);
                            try
                            {
                                int read;
                                while ((read = st.Read(buffer, 0, buffer.Length)) > 0) ms.Write(buffer, 0, read);
                            }
                            finally { ByteArrayPool.Return(buffer); }
                            ms.Position = 0;
                            streamToUse = ms;
                        }
                        var context = new StbImage.stbi__context(streamToUse);
                        result = StbImage.stbi__load_and_postprocess_8bit(context, &x, &y, &comp, 4);
                    }
                    catch (Exception ex) { LogUtil.LogError("StbImage FromStream Error: " + ex); }
                    
                    if (result == null) 
                    {
                        LogUtil.LogError("StbImage returned null for stream. Reason: " + StbImage.stbi__g_failure_reason);
                        if (ms != null) ms.Dispose();
                        hadError = true;
                        return;
                    }

                    try
                    {
                        int srcWidth = x;
                        int srcHeight = y;
                        int srcStride = srcWidth * 4;
                        int srcBpp = 4;

                        if (!setSize)
                        {
                            width = srcWidth;
                            height = srcHeight;
                            if (compress)
                            {
                                int num = width / 4;
                                if (num == 0) num = 1;
                                width = num * 4;
                                int num2 = height / 4;
                                if (num2 == 0) num2 = 1;
                                height = num2 * 4;
                            }
                        }
                        
                        num3 = 3;
                        textureFormat = TextureFormat.RGB24;
                        if (createAlphaFromGrayscale || isNormalMap || createNormalFromBump || comp == 4)
                        {
                            textureFormat = TextureFormat.RGBA32;
                            num3 = 4;
                        }
                        
                        int dstStride = width * num3;
                        int num7 = width * height;
                        num8 = num7 * num3;
                        int num9 = Mathf.CeilToInt((float)num8 * 1.5f);
                        raw = ByteArrayPool.Rent(num9);
                        
                        ImageProcessingOptimization.FastBitmapCopy(result, srcWidth, srcHeight, srcStride, srcBpp,
                            raw, width, height, dstStride, num3, setSize, fillBackground);
                    }
                    finally
                    {
                        CRuntime.free(result);
                        if (ms != null) ms.Dispose();
                    }
                }

                bool flag = isNormalMap && num3 == 4;
                if (flag)
                {
                    for (int i = 0; i < num8; i += 4) raw[i + 3] = 255;
                }

                if (invert)
                {
                    for (int j = 0; j < num8; j++)
                    {
                        int num10 = 255 - raw[j];
                        raw[j] = (byte)num10;
                    }
                }
                if (createAlphaFromGrayscale)
                {
                    for (int k = 0; k < num8; k += 4)
                    {
                        int num11 = raw[k];
                        int num12 = raw[k + 1];
                        int num13 = raw[k + 2];
                        int num14 = (num11 + num12 + num13) / 3;
                        raw[k + 3] = (byte)num14;
                    }
                }
                if (createNormalFromBump)
                {
                    ImageProcessingOptimization.OptimizedNormalMapGeneration(raw, width, height, bumpStrength);
                }
            }

            public void Process()
            {
                if (processed || cancel)
                {
                    processed = true;
                    return;
                }
                
                if (imgPath != null && imgPath != "NULL")
                {
                    if (useWebCache)
                    {
                        string webCachePath = GetWebCachePath();
                        try
                        {
                            string text = webCachePath + "meta";
                            if (FileManager.FileExists(text))
                            {
                                string jsonString = FileManager.ReadAllText(text);
                                ReadMetaJson(jsonString);
                                using (Stream fs = new FileStream(webCachePath, FileMode.Open, FileAccess.Read))
                                {
                                    int len = (int)fs.Length;
                                    raw = ByteArrayPool.Rent(len);
                                    int read = 0;
                                    while (read < len)
                                    {
                                        int r = fs.Read(raw, read, len - read);
                                        if (r == 0) break;
                                        read += r;
                                    }
                                }
                                preprocessed = true;
                            }
                            else { hadError = true; errorText = "Missing cache meta file " + text; }
                        }
                        catch (Exception ex) { LogUtil.LogError("Exception during cache file read " + ex); hadError = true; errorText = ex.ToString(); }
                    }
                    else if (webRequest != null)
                    {
                        if (!webRequestDone) return;
                        try
                        {
                            if (!webRequestHadError && webRequestData != null)
                            {
                                using (MemoryStream st = new MemoryStream(webRequestData)) ProcessFromStream(st);
                            }
                        }
                        catch (Exception ex2) { hadError = true; LogUtil.LogError("Exception " + ex2); errorText = ex2.ToString(); }
                    }
                    else if (FileManager.FileExists(imgPath))
                    {
                        string diskCachePath = GetDiskCachePath();
                        if (MVR.FileManagement.CacheManager.CachingEnabled && diskCachePath != null && FileManager.FileExists(diskCachePath))
                        {
                            try
                            {
                                string text2 = diskCachePath + "meta";
                                if (FileManager.FileExists(text2))
                                {
                                    string jsonString2 = FileManager.ReadAllText(text2);
                                    ReadMetaJson(jsonString2);
                                    using (Stream fs = new FileStream(diskCachePath, FileMode.Open, FileAccess.Read))
                                    {
                                        int len = (int)fs.Length;
                                        raw = ByteArrayPool.Rent(len);
                                        int read = 0;
                                        while (read < len)
                                        {
                                            int r = fs.Read(raw, read, len - read);
                                            if (r == 0) break;
                                            read += r;
                                        }
                                    }
                                    preprocessed = true;
                                    loadedFromCache = true;
                                }
                                else { hadError = true; errorText = "Missing cache meta file " + text2; }
                            }
                            catch (Exception ex3) { LogUtil.LogError("Exception during cache file read " + ex3); hadError = true; errorText = ex3.ToString(); }
                        }
                        else
                        {
                            try
                            {
                                using (FileEntryStream fileEntryStream = FileManager.OpenStream(imgPath))
                                {
                                    Stream stream = fileEntryStream.Stream;
                                    ProcessFromStream(stream);
                                }
                            }
                            catch (Exception ex4) { hadError = true; LogUtil.LogError("Exception " + ex4 + " " + imgPath); errorText = ex4.ToString(); }
                        }
                    }
                    else
                    {
                        hadError = true;
                        errorText = "Path " + imgPath + " not found via FileManager";
                        if (!imgPath.Contains(":/")) LogUtil.LogWarning("[VPB] Hub Image not found: " + imgPath);
                    }
                }
                else finished = true;
                processed = true;
            }

            private int GetExpectedRawDataSize(int w, int h, TextureFormat fmt)
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

            private void SafeLoadRawTextureData(Texture2D t, byte[] data, int w, int h, TextureFormat fmt)
            {
                if (t == null || data == null) return;
                int expected = GetExpectedRawDataSize(w, h, fmt);
                if (expected > 0 && data.Length >= expected)
                {
                    if (data.Length > expected)
                    {
                        byte[] exact = new byte[expected];
                        Buffer.BlockCopy(data, 0, exact, 0, expected);
                        t.LoadRawTextureData(exact);
                    }
                    else t.LoadRawTextureData(data);
                }
                else t.LoadRawTextureData(data);
            }

            public void Finish()
            {
                if (webRequest != null) { webRequest.Dispose(); webRequestData = null; webRequest = null; }
                if (hadError || finished) return;

                bool canCompress = compress && width > 0 && height > 0 && (width != 0 && (width & (width - 1)) == 0) && (height != 0 && (height & (height - 1)) == 0);
                CreateTexture();
                if (tex == null) { hadError = true; return; }

                if (preprocessed)
                {
                    try { SafeLoadRawTextureData(tex, raw, width, height, textureFormat); }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"[VPB] Hub LoadRawTextureData failed (preprocessed) for {imgPath}: {ex.Message}");
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                        CreateTexture();
                        if (tex != null) try { SafeLoadRawTextureData(tex, raw, width, height, textureFormat); } catch { }
                    }
                    tex.Apply(false);
                    if (canCompress && textureFormat != TextureFormat.DXT1 && textureFormat != TextureFormat.DXT5)
                    {
                        try { tex.Compress(true); } catch { canCompress = false; }
                    }
                }
                else
                {
                    try { SafeLoadRawTextureData(tex, raw, width, height, textureFormat); tex.Apply(); if (canCompress) tex.Compress(true); }
                    catch (Exception ex) { LogUtil.LogError($"[VPB] Hub LoadRawTextureData failed for {imgPath}: {ex.Message}"); }

                    if (MVR.FileManagement.CacheManager.CachingEnabled && !loadedFromCache)
                    {
                        string text = ((!Regex.IsMatch(imgPath, "^http")) ? GetDiskCachePath() : GetWebCachePath());
                        if (text != null && !FileManager.FileExists(text))
                        {
                            try
                            {
                                JSONClass jSONClass = new JSONClass();
                                jSONClass["type"] = "image";
                                jSONClass["width"].AsInt = tex.width;
                                jSONClass["height"].AsInt = tex.height;
                                jSONClass["format"] = tex.format.ToString();
                                byte[] rawTextureData2 = tex.GetRawTextureData();
                                File.WriteAllText(text + "meta", jSONClass.ToString(string.Empty));
                                File.WriteAllBytes(text, rawTextureData2);
                            }
                            catch (Exception ex) { LogUtil.LogError("Exception during Hub caching " + ex); }
                        }
                    }
                }
                if (raw != null) { ByteArrayPool.Return(raw); raw = null; }
                finished = true;
            }

            public void DoCallback()
            {
                if (rawImageToLoad != null) rawImageToLoad.texture = tex;
                if (callback != null) { callback(this); callback = null; }
            }
        }

        public class QueuedImagePool
        {
            Stack<QueuedImage> stack = new Stack<QueuedImage>();
            object lockObj = new object();
            public QueuedImage Get() { lock(lockObj) { if (stack.Count > 0) return stack.Pop(); } return new QueuedImage(); }
            public void Return(QueuedImage qi) { if (qi == null) return; qi.Reset(); lock(lockObj) { stack.Push(qi); } }
        }

        protected QueuedImagePool pool = new QueuedImagePool();
        public QueuedImage GetQI() { return pool.Get(); }

        protected class ImageLoaderTaskInfo
        {
            public string name;
            public AutoResetEvent resetEvent;
            public Thread thread;
            public volatile bool working;
            public volatile bool kill;
        }

        public static HubImageLoaderThreaded singleton;
        protected ImageLoaderTaskInfo imageLoaderTask;
        protected bool _threadsRunning;
        protected Dictionary<string, Texture2D> thumbnailCache;
        protected Dictionary<string, Texture2D> textureCache;
        protected Dictionary<string, Texture2D> immediateTextureCache;
        protected Dictionary<Texture2D, bool> textureTrackedCache;
        protected Dictionary<Texture2D, int> textureUseCount;
        protected PriorityQueue<QueuedImage> queuedImages;
        protected int numRealQueuedImages;
        protected int progress;
        protected int progressMax;
        protected AsyncFlag loadFlag;

        protected void MTTask(object info)
        {
            ImageLoaderTaskInfo imageLoaderTaskInfo = (ImageLoaderTaskInfo)info;
            while (_threadsRunning)
            {
                try
                {
                    imageLoaderTaskInfo.resetEvent.WaitOne(-1, true);
                    if (imageLoaderTaskInfo.kill) break;
                    ProcessImageQueueThreaded();
                }
                catch (Exception ex) { LogUtil.LogError("HubImageLoaderThread Error: " + ex); }
                finally { imageLoaderTaskInfo.working = false; }
            }
        }

        protected void StopThreads()
        {
            _threadsRunning = false;
            if (imageLoaderTask != null)
            {
                imageLoaderTask.kill = true;
                imageLoaderTask.resetEvent.Set();
                while (imageLoaderTask.thread.IsAlive) { }
                imageLoaderTask = null;
            }
        }

        protected void StartThreads()
        {
            if (!_threadsRunning)
            {
                _threadsRunning = true;
                imageLoaderTask = new ImageLoaderTaskInfo();
                imageLoaderTask.name = "HubImageLoaderTask";
                imageLoaderTask.resetEvent = new AutoResetEvent(false);
                imageLoaderTask.thread = new Thread(MTTask);
                imageLoaderTask.thread.Priority = System.Threading.ThreadPriority.Normal;
                imageLoaderTask.thread.Start(imageLoaderTask);
            }
        }

        public void PurgeAllTextures()
        {
            if (textureCache == null) return;
            foreach (Texture2D value in textureCache.Values) UnityEngine.Object.Destroy(value);
            textureUseCount.Clear();
            textureCache.Clear();
            textureTrackedCache.Clear();
        }

        protected void ProcessImageQueueThreaded()
        {
            if (queuedImages != null && queuedImages.Count > 0)
            {
                QueuedImage value = queuedImages.Peek();
                value.Process();
            }
        }

        public void CancelGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return;
            if (queuedImages != null && queuedImages.data != null)
            {
                foreach(var qi in queuedImages.data) { if (qi.groupId == groupId) qi.cancel = true; }
            }
        }

        public void QueueThumbnail(QueuedImage qi)
        {
            qi.isThumbnail = true;
            qi.cancel = false;
            if (queuedImages != null) { qi.insertionIndex = ++_insertionOrderCounter; queuedImages.Enqueue(qi); }
        }

        public void QueueThumbnailImmediate(QueuedImage qi)
        {
            qi.isThumbnail = true;
            qi.cancel = false;
            if (queuedImages != null) { qi.priority = -1; qi.insertionIndex = ++_insertionOrderCounter; queuedImages.Enqueue(qi); }
        }

        protected void PostProcessImageQueue()
        {
            int maxPerFrame = 20;
            while (queuedImages != null && queuedImages.Count > 0 && maxPerFrame > 0)
            {
                maxPerFrame--;
                QueuedImage value = queuedImages.Peek();
                if (value.processed || value.cancel)
                {
                    queuedImages.Dequeue();
                    if (value.cancel) { pool.Return(value); continue; }
                    value.Finish();
                    if (!value.skipCache && value.imgPath != null && value.imgPath != "NULL")
                    {
                        if (value.isThumbnail)
                        {
                            if (!thumbnailCache.ContainsKey(value.imgPath) && value.tex != null) thumbnailCache.Add(value.imgPath, value.tex);
                        }
                        else if (!textureCache.ContainsKey(value.cacheSignature) && value.tex != null)
                        {
                            textureCache.Add(value.cacheSignature, value.tex);
                            textureTrackedCache.Add(value.tex, true);
                        }
                    }
                    value.DoCallback();
                    pool.Return(value);
                }
                else break;
            }
        }

        protected void PreprocessImageQueue()
        {
            if (queuedImages == null || queuedImages.Count <= 0) return;
            while (queuedImages.Count > 0 && queuedImages.Peek().cancel) pool.Return(queuedImages.Dequeue());
            if (queuedImages.Count == 0) return;
            QueuedImage value = queuedImages.Peek();
            if (value == null) return;
            if (!value.skipCache && value.imgPath != null && value.imgPath != "NULL")
            {
                Texture2D value2;
                if (value.isThumbnail)
                {
                    if (thumbnailCache != null && thumbnailCache.TryGetValue(value.imgPath, out value2))
                    {
                        if (value2 == null) thumbnailCache.Remove(value.imgPath);
                        else { value.tex = value2; value.processed = true; value.finished = true; }
                    }
                }
                else if (textureCache != null && textureCache.TryGetValue(value.cacheSignature, out value2))
                {
                    if (value2 == null) { textureCache.Remove(value.cacheSignature); textureTrackedCache.Remove(value2); }
                    else { value.tex = value2; value.processed = true; value.finished = true; }
                }
            }
            if (!value.processed && value.imgPath != null && Regex.IsMatch(value.imgPath, "^http"))
            {
                if (MVR.FileManagement.CacheManager.CachingEnabled && value.WebCachePathExists()) value.useWebCache = true;
                else
                {
                    if (value.webRequest == null)
                    {
                        value.webRequest = UnityWebRequest.Get(value.imgPath);
                        value.webRequest.timeout = 30;
                        value.webRequest.SendWebRequest();
                    }
                    if (value.webRequest.isDone)
                    {
                        if (!value.webRequest.isNetworkError && value.webRequest.responseCode == 200)
                        {
                            value.webRequestData = value.webRequest.downloadHandler.data;
                            value.webRequestDone = true;
                        }
                        else
                        {
                            value.webRequestHadError = true;
                            value.webRequestDone = true;
                            value.hadError = true;
                            value.errorText = "Error " + value.webRequest.responseCode;
                        }
                    }
                }
            }
        }

        protected void Update()
        {
            StartThreads();
            if (!imageLoaderTask.working)
            {
                PostProcessImageQueue();
                if (queuedImages != null && queuedImages.Count > 0)
                {
                    PreprocessImageQueue();
                    QueuedImage head = queuedImages.Peek();
                    if (head != null && !head.processed && !head.cancel && (head.webRequestDone || head.webRequest == null))
                    {
                        imageLoaderTask.working = true;
                        imageLoaderTask.resetEvent.Set();
                    }
                }
            }
        }

        public void OnDestroy() { if (Application.isPlaying) StopThreads(); PurgeAllTextures(); }
        protected void OnApplicationQuit() { if (Application.isPlaying) StopThreads(); }

        protected void Awake()
        {
            singleton = this;
            immediateTextureCache = new Dictionary<string, Texture2D>();
            textureCache = new Dictionary<string, Texture2D>();
            textureTrackedCache = new Dictionary<Texture2D, bool>();
            thumbnailCache = new Dictionary<string, Texture2D>();
            textureUseCount = new Dictionary<Texture2D, int>();
            queuedImages = new PriorityQueue<QueuedImage>((a, b) => {
                int p = a.priority - b.priority;
                if (p != 0) return p;
                return a.insertionIndex.CompareTo(b.insertionIndex);
            });
        }

        private static long _insertionOrderCounter = 0;

        public class PriorityQueue<T>
        {
            public List<T> data;
            private Comparison<T> comparison;
            public PriorityQueue(Comparison<T> comparison) { this.data = new List<T>(); this.comparison = comparison; }
            public void Enqueue(T item)
            {
                data.Add(item);
                int ci = data.Count - 1; 
                while (ci > 0) { int pi = (ci - 1) / 2; if (comparison(data[ci], data[pi]) >= 0) break; T tmp = data[ci]; data[ci] = data[pi]; data[pi] = tmp; ci = pi; }
            }
            public T Dequeue()
            {
                int li = data.Count - 1; T frontItem = data[0]; data[0] = data[li]; data.RemoveAt(li); --li; int pi = 0;
                while (true) { int ci = pi * 2 + 1; if (ci > li) break; int rc = ci + 1; if (rc <= li && comparison(data[rc], data[ci]) < 0) ci = rc; if (comparison(data[pi], data[ci]) <= 0) break; T tmp = data[pi]; data[pi] = data[ci]; data[ci] = tmp; pi = ci; }
                return frontItem;
            }
            public T Peek() { if (data.Count == 0) return default(T); return data[0]; }
            public int Count { get { return data.Count; } }
            public void Clear() { data.Clear(); }
        }
    }
}
