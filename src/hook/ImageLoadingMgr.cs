using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Profiling;
using ZstdNet;

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
        public static ImageLoaderThreaded.QueuedImage currentProcessingQI;
        
        private const int AlphaCacheVersion = 3;
        /// <summary>Max decompressed bytes for zstd serve on calling thread (LoadImage immediate, etc.).</summary>
        private const int MaxSyncDecompressedPayloadBytes = 8 * 1024 * 1024;
        /// <summary>Sized for an 8K RGBA full mip chain (8192² × 4 × 4/3 ≈ 341 MiB); 512 MiB is the next round bucket.</summary>
        private const int MaxServeCachePayloadBytes = 512 * 1024 * 1024;
        private const int MaxCacheDimension = 8192;

        private readonly Queue<DecompressedData> pendingMainThreadTextureCreates = new Queue<DecompressedData>();
        private readonly object pendingMainThreadLock = new object();
        private const int MaxPendingMainThreadTextureCreates = 16;
        private const long MaxPendingMainThreadTextureCreateBytes = 512L * 1024 * 1024;
        private int pendingMainThreadTextureCreateCount;
        private long pendingMainThreadTextureCreateBytes;
        private int loadGeneration;
        private const int MaxActiveTexturePayloadWorkers = 2;
        private int activeTexturePayloadWorkers;
        private Coroutine mainThreadTextureCreatePump;

        private int CurrentLoadGeneration
        {
            get { return Thread.VolatileRead(ref loadGeneration); }
        }

        private bool IsLoadGenerationCurrent(int generation)
        {
            return generation == CurrentLoadGeneration;
        }

        private bool TryAcquireTexturePayloadWorker(int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return false;
            while (true)
            {
                int current = Thread.VolatileRead(ref activeTexturePayloadWorkers);
                if (current >= MaxActiveTexturePayloadWorkers) return false;
                if (Interlocked.CompareExchange(ref activeTexturePayloadWorkers, current + 1, current) == current) return true;
            }
        }

        private static bool TryQueueBackground(WaitCallback callback)
        {
            if (callback == null) return false;
            try { return ThreadPool.QueueUserWorkItem(callback); }
            catch { return false; }
        }

        private void Awake()
        {
            singleton = this;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => CleanStaleAlphaCaches());
        }

        private static void CleanStaleAlphaCaches()
        {
            try
            {
                string zstdDir = VamHookPlugin.GetCacheDir();
                if (string.IsNullOrEmpty(zstdDir) || !Directory.Exists(zstdDir)) return;

                string sentinel = Path.Combine(zstdDir, ".vpb_alpha_v" + AlphaCacheVersion);
                if (File.Exists(sentinel)) return;

                string[] metaFiles;
                try { metaFiles = Directory.GetFiles(zstdDir, "*__C_A.zvamcachemeta"); }
                catch { return; }

                int deleted = 0;
                foreach (var metaFile in metaFiles)
                {
                    try
                    {
                        var meta = SimpleJSON.JSON.Parse(File.ReadAllText(metaFile));
                        if (meta == null || meta["vpbVer"].AsInt < AlphaCacheVersion)
                        {
                            string dataFile = metaFile.Substring(0, metaFile.Length - 4);
                            try { if (File.Exists(dataFile)) File.Delete(dataFile); } catch { }
                            try { File.Delete(metaFile); } catch { }
                            deleted++;
                        }
                    }
                    catch { }
                }

                if (deleted > 0)
                    LogUtil.Log("[VPB] Cleaned " + deleted + " stale alpha cache file(s) from previous version.");

                File.WriteAllText(sentinel, AlphaCacheVersion.ToString());
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] CleanStaleAlphaCaches failed: " + ex.Message);
            }
        }

        private sealed class CachedTexture
        {
            public Texture2D Texture;
            public long EstimatedBytes;
            public LinkedListNode<string> LruNode;
        }

        private const long MaxTextureCacheBytes = 512L * 1024 * 1024;
        private readonly Dictionary<string, CachedTexture> textureCache = new Dictionary<string, CachedTexture>();
        private readonly LinkedList<string> textureLruOrder = new LinkedList<string>();
        private long currentTextureCacheBytes;
        Dictionary<string, List<ImageLoaderThreaded.QueuedImage>> inflightWaiters = new Dictionary<string, List<ImageLoaderThreaded.QueuedImage>>();
        HashSet<string> inflightKeys = new HashSet<string>();
        private readonly object textureCacheLock = new object();
        private readonly object inflightLock = new object();
        private readonly HashSet<string> quarantinedZstdPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object quarantineLock = new object();

        private bool IsZstdPathQuarantined(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath)) return false;
            lock (quarantineLock)
                return quarantinedZstdPaths.Contains(cachePath);
        }

        private bool QuarantineZstdPath(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath)) return false;
            lock (quarantineLock)
                return quarantinedZstdPaths.Add(cachePath);
        }
        
        private class DecompressedData
        {
            public string CacheKey;
            public byte[] Data;
            public MetadataEntry Meta;
            public ImageLoaderThreaded.QueuedImage OriginalQI;
            public int Generation;
        }
        
        private class MetadataEntry
        {
            public int Width;
            public int Height;
            public TextureFormat Format;
            public bool IsDownscaled;
            public bool IsReadable;
            public bool CreateMipMaps;
            public int MipCount;
            /// <summary><see cref="TextureUtil.MipStorageBase"/> or <see cref="TextureUtil.MipStorageFull"/>.</summary>
            public string MipStorage;
        }

        private class DiskCachePayload
        {
            public byte[] Data;
            public MetadataEntry Meta;
            public string CachePath;
            public bool FromZstd;
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

        private static string GetTextureCacheKey(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return string.Empty;

            bool isReadableVariant = SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
            return TextureUtil.BuildTextureLookupKey(
                qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                qi.bumpStrength, qi.setSize, qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, isReadableVariant);
        }

        private string GetCachePath(ImageLoaderThreaded.QueuedImage qi)
        {
            string pathKey = GetTextureCacheKey(qi);

            lock (cachePathMapLock)
            {
                if (cachePathMap.TryGetValue(pathKey, out var cached))
                {
                    if (cached != null && File.Exists(cached) && !IsZstdCachePathBusy(cached)) return cached;
                    cachePathMap.Remove(pathKey);
                }
            }

            string vpbCachePath = ResolveZstdServePath(qi);

            if (vpbCachePath != null && File.Exists(vpbCachePath))
            {
                if (IsZstdCachePathBusy(vpbCachePath))
                    return null;

                CacheCleanupManager.QueueHit(vpbCachePath, qi.imgPath);
                lock (cachePathMapLock)
                {
                    cachePathMap[pathKey] = vpbCachePath;
                }
                return vpbCachePath;
            }

            return null;
        }

        private static byte[] ReadCacheFileBytes(string path, int maxBytes)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    if (len <= 0 || len > maxBytes) return null;
                    var buf = new byte[(int)len];
                    int read = fs.Read(buf, 0, buf.Length);
                    if (read != len) return null;
                    return buf;
                }
            }
            catch
            {
                return null;
            }
        }

        private bool IsZstdCachePathBusy(string zstdPath)
        {
            return IsZstdWritePathBusy(zstdPath);
        }

        /// <summary>True while runtime or on-demand zstd write owns path (incl. .tmp on disk).</summary>
        public static bool IsZstdWritePathBusy(string zstdPath)
        {
            if (string.IsNullOrEmpty(zstdPath)) return false;

            var mgr = singleton;
            if (mgr != null)
            {
                lock (mgr.pendingZstdWriteLock)
                {
                    if (mgr.pendingZstdWrites.Contains(zstdPath)) return true;
                }

                lock (mgr.runtimeZstdWriteQueueLock)
                {
                    foreach (var item in mgr.runtimeZstdWriteQueue)
                    {
                        if (item != null && string.Equals(item.ZstdPath, zstdPath, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            else
            {
                lock (s_FallbackPendingZstdLock)
                {
                    if (s_FallbackPendingZstdWrites.Contains(zstdPath)) return true;
                }
            }

            if (File.Exists(zstdPath + ".tmp")) return true;
            if (File.Exists(zstdPath + ".meta.tmp")) return true;
            return false;
        }

        /// <summary>Claim path for exclusive zstd write. False if already claimed.</summary>
        public static bool TryAcquireZstdWritePath(string zstdPath)
        {
            if (string.IsNullOrEmpty(zstdPath)) return false;
            var mgr = singleton;
            if (mgr == null)
            {
                lock (s_FallbackPendingZstdLock)
                {
                    if (s_FallbackPendingZstdWrites.Contains(zstdPath)) return false;
                    s_FallbackPendingZstdWrites.Add(zstdPath);
                    return true;
                }
            }

            lock (mgr.pendingZstdWriteLock)
            {
                if (mgr.pendingZstdWrites.Contains(zstdPath)) return false;
                mgr.pendingZstdWrites.Add(zstdPath);
                return true;
            }
        }

        public static void ReleaseZstdWritePath(string zstdPath)
        {
            if (string.IsNullOrEmpty(zstdPath)) return;
            var mgr = singleton;
            if (mgr == null)
            {
                lock (s_FallbackPendingZstdLock)
                {
                    s_FallbackPendingZstdWrites.Remove(zstdPath);
                }
                return;
            }
            ReleasePendingZstdWriteSlot(zstdPath);
        }

        private bool TryGetZstdPayloadFromMemoryOnly(string zstdPath, bool queueCreateMipMaps, out DiskCachePayload payload)
        {
            payload = null;
            if (string.IsNullOrEmpty(zstdPath)) return false;

            MetadataEntry meta = null;
            lock (metadataCacheLock)
            {
                metadataCache.TryGetValue(zstdPath, out meta);
            }

            byte[] memData = null;
            lock (decompressedCacheLock)
            {
                CachedDecompressed cached;
                if (decompressedCache.TryGetValue(zstdPath, out cached))
                    memData = cached.Data;
            }

            if (meta == null || memData == null || !IsRawDataValidForMeta(meta, memData, queueCreateMipMaps, zstdPath))
                return false;

            payload = new DiskCachePayload { Data = memData, Meta = meta, CachePath = zstdPath, FromZstd = true };
            return true;
        }

        private bool BeginAsyncMemoryServe(string cacheKey, ImageLoaderThreaded.QueuedImage qi, DiskCachePayload payload, int generation)
        {
            if (qi == null || payload == null || payload.Data == null || payload.Meta == null || !IsLoadGenerationCurrent(generation)) return false;

            lock (inflightLock)
            {
                if (inflightKeys.Contains(cacheKey)) return false;
                inflightKeys.Add(cacheKey);
            }

            var decompressed = new DecompressedData
            {
                CacheKey = cacheKey,
                Data = payload.Data,
                Meta = payload.Meta,
                OriginalQI = qi,
                Generation = generation
            };
            ScheduleCreateTextureOnMainThread(decompressed);
            return true;
        }

        private bool TryApplyMemoryCacheImmediate(ImageLoaderThreaded.QueuedImage qi, string cacheKey)
        {
            if (qi == null) return false;

            string zstdPath = ResolveZstdServePath(qi);
            bool queueMip = ResolveQueueCreateMipMaps(qi);
            DiskCachePayload payload;
            if (!string.IsNullOrEmpty(zstdPath) && TryGetZstdPayloadFromMemoryOnly(zstdPath, queueMip, out payload))
            {
                try
                {
                    if (payload.Data != null && payload.Data.Length > MaxSyncDecompressedPayloadBytes)
                        return false;

                    bool forceReadable = ShouldForceCpuReadable(qi, payload.Meta.IsReadable);
                    Texture2D tex = qi.tex;
                    if (tex == null)
                    {
                        tex = TextureUtil.CreateTextureFromCachedRaw(payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                            payload.Meta.CreateMipMaps, qi.linear, !forceReadable, forceReadable);
                        if (tex == null) return false;
                    }
                    else if (!TextureUtil.ApplyCachedRawToTexture(tex, payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                        payload.Meta.CreateMipMaps, qi.linear, !forceReadable, forceReadable))
                    {
                        return false;
                    }

                    qi.tex = tex;
                    tex.name = qi.cacheSignature;
                    RegisterTexture(cacheKey, tex);
                    if (payload.Meta.IsDownscaled)
                        TextureUtil.MarkDownscaledActive(GetDownscaledKey(cacheKey));
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            ImageLoaderThreaded.QueuedImage cand = FindCandidateByPath(qi.imgPath);
            if (cand != null && cand.tex != null && GetTextureCacheKey(cand) == cacheKey
                && TextureMeetsCpuReadableRequirement(qi, cand.tex))
            {
                qi.tex = cand.tex;
                RegisterTexture(cacheKey, cand.tex);
                return true;
            }

            return false;
        }

        private bool TryServeFromMemoryCache(ImageLoaderThreaded.QueuedImage qi, string cacheKey, bool allowCandidateTexture, int generation)
        {
            if (qi == null || string.IsNullOrEmpty(cacheKey) || !IsLoadGenerationCurrent(generation)) return false;

            string zstdPath = ResolveZstdServePath(qi);
            bool queueMip = ResolveQueueCreateMipMaps(qi);
            DiskCachePayload payload;
            if (!string.IsNullOrEmpty(zstdPath) && TryGetZstdPayloadFromMemoryOnly(zstdPath, queueMip, out payload))
                return BeginAsyncMemoryServe(cacheKey, qi, payload, generation);

            if (allowCandidateTexture)
            {
                ImageLoaderThreaded.QueuedImage cand = FindCandidateByPath(qi.imgPath);
                if (cand != null && cand.tex != null && GetTextureCacheKey(cand) == cacheKey
                    && TextureMeetsCpuReadableRequirement(qi, cand.tex))
                {
                    qi.tex = cand.tex;
                    RegisterTexture(cacheKey, cand.tex);
                    if (Messager.singleton != null)
                        Messager.singleton.StartCoroutine(DelayDoCallback(qi, generation));
                    else if (IsLoadGenerationCurrent(generation))
                        DoCallback(qi);
                    return true;
                }
            }

            return false;
        }

        private void ScheduleRetryDiskCacheLoad(string cacheKey, string cachePath, ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
            if (Messager.singleton != null)
                Messager.singleton.StartCoroutine(RetryDiskCacheLoadCoroutine(cacheKey, cachePath, qi, generation));
            else if (!TryQueueBackground(_ => LoadAndDecompressBackground(cacheKey, cachePath, qi, generation)))
                FallbackInflightToNative(cacheKey, qi, generation);
        }

        private IEnumerator RetryDiskCacheLoadCoroutine(string cacheKey, string cachePath, ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            for (int i = 0; i < 12 && IsZstdCachePathBusy(cachePath); i++)
            {
                if (!IsLoadGenerationCurrent(generation)) yield break;
                yield return null;
            }

            if (!IsLoadGenerationCurrent(generation)) yield break;
            if (TryServeFromMemoryCache(qi, cacheKey, true, generation))
                yield break;

            lock (inflightLock)
            {
                if (inflightKeys.Contains(cacheKey)) yield break;
                inflightKeys.Add(cacheKey);
            }
            LoadAndDecompressBackground(cacheKey, cachePath, qi, generation);
        }

        private void HandleBusyOrTransientDiskCacheFailure(string cacheKey, string diskPath, ImageLoaderThreaded.QueuedImage qi, DiskCacheFailureKind kind, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
            lock (inflightLock)
            {
                inflightKeys.Remove(cacheKey);
            }

            if (TryServeFromMemoryCache(qi, cacheKey, true, generation))
                return;

            InvalidateMetadataCacheForDiskPath(diskPath);

            if (kind == DiskCacheFailureKind.VpbZstd && IsZstdCachePathBusy(diskPath))
            {
                ScheduleRetryDiskCacheLoad(cacheKey, diskPath, qi, generation);
                return;
            }

            FailDiskCacheLoadAndFallback(cacheKey, diskPath, qi, kind, generation);
        }

        private static void FinalizeMetaFromRaw(MetadataEntry meta, byte[] data, bool queueCreateMipMaps)
        {
            if (meta == null || data == null || data.Length == 0) return;

            if (string.IsNullOrEmpty(meta.MipStorage))
                meta.MipStorage = TextureUtil.ClassifyMipStorage(data.Length, meta.Width, meta.Height, meta.Format);

            if (meta.MipCount <= 0)
                meta.MipCount = TextureUtil.CountMipLevels(meta.Width, meta.Height);

            meta.CreateMipMaps = TextureUtil.ResolveCreateMipMaps(
                queueCreateMipMaps, meta.CreateMipMaps, meta.MipStorage, data.Length, meta.Width, meta.Height, meta.Format);
        }

        private static bool IsCacheMetaAndPayloadSane(MetadataEntry meta, byte[] data)
        {
            if (meta == null || data == null || data.Length == 0) return false;
            if (data.Length > MaxServeCachePayloadBytes) return false;
            if (meta.Width <= 0 || meta.Height <= 0) return false;
            if (meta.Width > MaxCacheDimension || meta.Height > MaxCacheDimension) return false;
            long pixels = (long)meta.Width * meta.Height;
            if (pixels > (long)MaxCacheDimension * MaxCacheDimension) return false;
            return true;
        }

        private static void TryDeleteCorruptNativeCache(string nativePath)
        {
            if (string.IsNullOrEmpty(nativePath)) return;
            try
            {
                if (File.Exists(nativePath)) File.Delete(nativePath);
            }
            catch { }
            try
            {
                string meta = nativePath + "meta";
                if (File.Exists(meta)) File.Delete(meta);
            }
            catch { }
        }

        private void InvalidateCachePathMapForDiskPath(string diskPath)
        {
            if (string.IsNullOrEmpty(diskPath)) return;
            lock (cachePathMapLock)
            {
                var staleKeys = new List<string>();
                foreach (var kvp in cachePathMap)
                {
                    if (string.Equals(kvp.Value, diskPath, StringComparison.OrdinalIgnoreCase))
                        staleKeys.Add(kvp.Key);
                }
                for (int i = 0; i < staleKeys.Count; i++)
                    cachePathMap.Remove(staleKeys[i]);
            }
        }

        private void InvalidateMetadataCacheForDiskPath(string diskPath)
        {
            if (string.IsNullOrEmpty(diskPath)) return;
            lock (metadataCacheLock)
                metadataCache.Remove(diskPath);
        }

        /// <summary>Drop VPB zstd / native preprocess entries after a file was overwritten on disk (e.g. scene/preset thumb .jpg).</summary>
        public void InvalidateProcessedCachesForSourcePath(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return;
            try
            {
                TextureUtil.TryDeleteNativeThumbnailDiskCachesForSource(sourcePath);
                TextureUtil.TryDeleteZstdThumbnailDiskCachesForSource(sourcePath);

                string full = FileManager.GetFullPath(sourcePath.Replace('\\', '/'));
                InvalidateCachePathMapForDiskPath(full);
                InvalidateMetadataCacheForDiskPath(full);

                string native = FindNativeDiskCachePathForThumbnailSource(sourcePath);
                if (!string.IsNullOrEmpty(native))
                {
                    TryDeleteCorruptNativeCache(native);
                    InvalidateCachePathMapForDiskPath(native);
                    InvalidateMetadataCacheForDiskPath(native);
                }

                string zstd = TextureUtil.ResolveServeZstdCachePath(
                    sourcePath, false, false, false, false, false, false, 0, 0, 0f, false);
                if (!string.IsNullOrEmpty(zstd))
                {
                    TextureUtil.TryDeleteZstdCacheFile(zstd);
                    InvalidateCachePathMapForDiskPath(zstd);
                    InvalidateMetadataCacheForDiskPath(zstd);
                }
            }
            catch { }
        }

        private static string FindNativeDiskCachePathForThumbnailSource(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath)) return null;
            return TextureUtil.FindVaMNativeDiskCachePath(
                imgPath, false, false, false, false, false, false, 0, 0, 0f);
        }

        private enum DiskCacheFailureKind
        {
            VpbZstd,
            VaMNative
        }

        private static string FindNativeDiskCachePath(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return null;
            return TextureUtil.FindVaMNativeDiskCachePath(
                qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength);
        }

        private void CollectInflightTargets(string cacheKey, ImageLoaderThreaded.QueuedImage primaryQi, List<ImageLoaderThreaded.QueuedImage> targets)
        {
            if (targets == null) return;
            if (primaryQi != null) targets.Add(primaryQi);
            lock (inflightLock)
            {
                inflightKeys.Remove(cacheKey);
                if (inflightWaiters.TryGetValue(cacheKey, out var waiters))
                {
                    for (int i = 0; i < waiters.Count; i++)
                        targets.Add(waiters[i]);
                    inflightWaiters.Remove(cacheKey);
                }
            }
        }

        private void FailDiskCacheLoadAndFallback(string cacheKey, string diskPath, ImageLoaderThreaded.QueuedImage qi, DiskCacheFailureKind kind, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
            if (kind == DiskCacheFailureKind.VaMNative)
                TryDeleteCorruptNativeCache(diskPath);

            InvalidateCachePathMapForDiskPath(diskPath);
            InvalidateMetadataCacheForDiskPath(diskPath);

            var targets = new List<ImageLoaderThreaded.QueuedImage>();
            CollectInflightTargets(cacheKey, qi, targets);
            for (int i = 0; i < targets.Count; i++)
                ScheduleVaMLoadFallback(targets[i], kind, generation);
        }

        private void ScheduleVaMLoadFallback(ImageLoaderThreaded.QueuedImage qi, DiskCacheFailureKind failedKind, int generation)
        {
            if (qi == null || !IsLoadGenerationCurrent(generation)) return;
            qi.tex = null;

            string cacheKey = GetTextureCacheKey(qi);
            if (failedKind == DiskCacheFailureKind.VpbZstd)
            {
                string nativeDiskPath = FindNativeDiskCachePath(qi);
                if (!string.IsNullOrEmpty(nativeDiskPath))
                {
                    lock (inflightLock)
                    {
                        if (inflightKeys.Contains(cacheKey)) return;
                        inflightKeys.Add(cacheKey);
                    }
                    if (TryQueueBackground(_ => LoadVaMNativeDiskCacheBackground(cacheKey, nativeDiskPath, qi, generation))) return;
                    lock (inflightLock) inflightKeys.Remove(cacheKey);
                }
            }

            EnqueueVaMImageLoadOnMainThread(qi, generation);
        }

        private void EnqueueVaMImageLoadOnMainThread(ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
            if (Messager.singleton != null)
                Messager.singleton.StartCoroutine(RequeueVaMImageLoadCoroutine(qi, generation));
            else
                SuperControllerHook.RequeueVaMImageLoad(qi);
        }

        private IEnumerator RequeueVaMImageLoadCoroutine(ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            while (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive())
            {
                if (!IsLoadGenerationCurrent(generation)) yield break;
                yield return null;
            }
            yield return waitForEndOfFrame;
            if (!IsLoadGenerationCurrent(generation)) yield break;
            SuperControllerHook.RequeueVaMImageLoad(qi);
        }

        private static bool IsRawDataValidForMeta(MetadataEntry meta, byte[] data, bool queueCreateMipMaps = false, string diskCachePath = null)
        {
            if (meta == null || data == null || data.Length == 0) return false;
            if (!IsCacheMetaAndPayloadSane(meta, data)) return false;

            bool legacyMipMeta = TextureUtil.DiskCacheMetaLacksMipFields(diskCachePath);

            FinalizeMetaFromRaw(meta, data, queueCreateMipMaps);

            if (legacyMipMeta)
            {
                int baseSize = TextureUtil.GetExpectedRawDataSize(meta.Width, meta.Height, meta.Format);
                return baseSize <= 0 || data.Length >= baseSize;
            }

            if (!TextureUtil.ValidateRawLengthForTextureMeta(meta.Width, meta.Height, meta.Format, data.Length, meta.MipStorage))
            {
                string repairedStorage = TextureUtil.ClassifyMipStorage(data.Length, meta.Width, meta.Height, meta.Format);
                if (!string.Equals(repairedStorage, meta.MipStorage, StringComparison.OrdinalIgnoreCase))
                {
                    meta.MipStorage = repairedStorage;
                    meta.CreateMipMaps = TextureUtil.ResolveCreateMipMaps(
                        queueCreateMipMaps, meta.CreateMipMaps, meta.MipStorage, data.Length, meta.Width, meta.Height, meta.Format);
                    if (TextureUtil.ValidateRawLengthForTextureMeta(meta.Width, meta.Height, meta.Format, data.Length, meta.MipStorage))
                        return true;
                }

                try
                {
                    int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0;
                    if (lvl >= 1)
                    {
                        LogUtil.LogWarning("[VPB] Cache raw mip size mismatch "
                            + meta.Width + "x" + meta.Height + " " + meta.Format
                            + " storage=" + (meta.MipStorage ?? "?")
                            + " got=" + data.Length
                            + " base=" + TextureUtil.GetExpectedRawDataSize(meta.Width, meta.Height, meta.Format)
                            + " full=" + TextureUtil.GetExpectedFullMipChainSize(meta.Width, meta.Height, meta.Format));
                    }
                }
                catch { }
                return false;
            }

            return true;
        }

        /// <summary>Queue mip intent from <see cref="ImageLoaderThreaded.QueuedImage"/> plus path hints from PreQueue/Process.</summary>
        private bool ResolveQueueCreateMipMaps(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return false;
            bool mips = qi.createMipMaps;
            lock (candidateLock)
            {
                bool hint;
                if (!string.IsNullOrEmpty(qi.imgPath) && pathCreateMipMapsHints.TryGetValue(qi.imgPath, out hint))
                    mips = mips || hint;
            }
            return mips;
        }

        private static bool DiskMetaNeedsMipUpgrade(string metaPath, MetadataEntry resolved)
        {
            if (string.IsNullOrEmpty(metaPath) || !File.Exists(metaPath) || resolved == null) return false;
            try
            {
                var metaJson = JSON.Parse(File.ReadAllText(metaPath));
                bool hadStorage = metaJson["mipStorage"] != null && !string.IsNullOrEmpty(metaJson["mipStorage"].Value);
                if (!hadStorage) return true;

                if (metaJson["mipCount"] == null || metaJson["mipCount"].AsInt <= 0) return true;

                bool fileCreate = false;
                bool hadCreate = metaJson["createMipMaps"] != null;
                try { fileCreate = metaJson["createMipMaps"].AsBool; } catch { }
                if (!hadCreate || fileCreate != resolved.CreateMipMaps) return true;

                string fileStorage = null;
                try { fileStorage = metaJson["mipStorage"].Value; } catch { }
                if (!string.Equals(fileStorage, resolved.MipStorage, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                return true;
            }

            return false;
        }

        private static void TryUpgradeDiskCacheMetaIfStale(string cachePath, MetadataEntry resolvedMeta, int rawLength, bool queueCreateMipMaps)
        {
            if (string.IsNullOrEmpty(cachePath) || resolvedMeta == null || rawLength <= 0) return;
            string metaPath = cachePath + "meta";
            if (!DiskMetaNeedsMipUpgrade(metaPath, resolvedMeta)) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!File.Exists(metaPath)) return;
                    var metaJson = JSON.Parse(File.ReadAllText(metaPath));
                    TextureUtil.WriteMipFieldsToMeta(metaJson, resolvedMeta.Width, resolvedMeta.Height, resolvedMeta.Format, rawLength, queueCreateMipMaps);
                    string temp = metaPath + ".tmp";
                    File.WriteAllText(temp, VPB.src.util.JsonSerializationUtil.Serialize(metaJson, 1024));
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                    File.Move(temp, metaPath);
                }
                catch { }
            });
        }

        private void TryUpgradeExistingZstdMeta(string zstdPath, bool queueCreateMipMaps)
        {
            if (string.IsNullOrEmpty(zstdPath) || !File.Exists(zstdPath) || !File.Exists(zstdPath + "meta")) return;
            try
            {
                var meta = FastLoadMetadata(zstdPath);
                if (meta == null) return;
                byte[] data = FastGetDecompressed(zstdPath);
                if (data == null || data.Length == 0) return;
                if (!IsRawDataValidForMeta(meta, data, queueCreateMipMaps, zstdPath)) return;
                TryUpgradeDiskCacheMetaIfStale(zstdPath, meta, data.Length, queueCreateMipMaps);
                lock (metadataCacheLock)
                {
                    metadataCache[zstdPath] = meta;
                }
            }
            catch { }
        }

        private static void RejectUnsaneServeZstdPath(ImageLoaderThreaded.QueuedImage qi, ref string vpbCachePath)
        {
            if (string.IsNullOrEmpty(vpbCachePath) || qi == null) return;

            if (TextureUtil.IsTiffTexturePath(qi.imgPath)
                && !TextureUtil.IsZstdCachePayloadSane(vpbCachePath, 64 * 64))
            {
                TextureUtil.TryDeleteZstdCacheFile(vpbCachePath);
                vpbCachePath = null;
                return;
            }

            if (qi.createAlphaFromGrayscale
                && !TextureUtil.IsZstdCachePayloadSane(vpbCachePath, 32 * 32))
            {
                TextureUtil.TryDeleteZstdCacheFile(vpbCachePath);
                vpbCachePath = null;
            }
        }

        private string ResolveZstdServePath(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return null;
            bool isSimPath = SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
            string vpbCachePath = TextureUtil.ResolveServeZstdCachePath(
                qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength, isSimPath);

            RejectUnsaneServeZstdPath(qi, ref vpbCachePath);
            return vpbCachePath;
        }

        private static int EstimateMaxDecompressedPayloadBytes(MetadataEntry meta)
        {
            if (meta == null || meta.Width <= 0 || meta.Height <= 0) return 0;
            int full = TextureUtil.GetExpectedFullMipChainSize(meta.Width, meta.Height, meta.Format);
            if (full > 0 && (meta.CreateMipMaps || string.Equals(meta.MipStorage, TextureUtil.MipStorageFull, StringComparison.OrdinalIgnoreCase)))
                return full;
            int bas = TextureUtil.GetExpectedRawDataSize(meta.Width, meta.Height, meta.Format);
            return bas > 0 ? bas : 0;
        }

        private static bool IsPayloadSmallEnoughForSyncServe(MetadataEntry meta)
        {
            int est = EstimateMaxDecompressedPayloadBytes(meta);
            return est > 0 && est <= MaxSyncDecompressedPayloadBytes;
        }

        private bool TryGetZstdPayloadFromMemory(string zstdPath, bool queueCreateMipMaps, out DiskCachePayload payload)
        {
            payload = null;
            if (string.IsNullOrEmpty(zstdPath)) return false;

            if (TryGetZstdPayloadFromMemoryOnly(zstdPath, queueCreateMipMaps, out payload))
                return true;

            var meta = FastLoadMetadata(zstdPath);
            if (meta == null) return false;

            byte[] memData = null;
            lock (decompressedCacheLock)
            {
                CachedDecompressed cached;
                if (decompressedCache.TryGetValue(zstdPath, out cached))
                    memData = cached.Data;
            }

            if (memData == null || !IsRawDataValidForMeta(meta, memData, queueCreateMipMaps, zstdPath)) return false;

            payload = new DiskCachePayload { Data = memData, Meta = meta, CachePath = zstdPath, FromZstd = true };
            return true;
        }

        /// <summary>Disk-cache load (zstd + native). <paramref name="allowSyncZstdDecompress"/> false on main thread for large payloads.</summary>
        private bool TryLoadDiskCachePayload(ImageLoaderThreaded.QueuedImage qi, bool allowZstdFromMemory, bool allowSyncZstdDecompress, bool allowNativeRead, out DiskCachePayload payload)
        {
            payload = null;
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return false;

            bool queueCreateMipMaps = ResolveQueueCreateMipMaps(qi);

            if (allowZstdFromMemory || allowSyncZstdDecompress)
            {
                string zstdPath = ResolveZstdServePath(qi);
                if (!string.IsNullOrEmpty(zstdPath))
                {
                    if (allowZstdFromMemory && TryGetZstdPayloadFromMemory(zstdPath, queueCreateMipMaps, out payload))
                        return true;

                    if (IsZstdCachePathBusy(zstdPath))
                        zstdPath = null;
                }

                if (!string.IsNullOrEmpty(zstdPath) && File.Exists(zstdPath) && File.Exists(zstdPath + "meta"))
                {
                    if (allowSyncZstdDecompress)
                    {
                        var meta = FastLoadMetadata(zstdPath);
                        if (meta != null && IsPayloadSmallEnoughForSyncServe(meta))
                        {
                            byte[] data = FastGetDecompressed(zstdPath);
                            if (IsRawDataValidForMeta(meta, data, queueCreateMipMaps, zstdPath))
                            {
                                TryUpgradeDiskCacheMetaIfStale(zstdPath, meta, data.Length, queueCreateMipMaps);
                                payload = new DiskCachePayload { Data = data, Meta = meta, CachePath = zstdPath, FromZstd = true };
                                return true;
                            }
                        }
                    }
                }
            }

            if (allowNativeRead)
            {
                string nativePath = TextureUtil.FindVaMNativeDiskCachePath(
                    qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                    qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength);
                if (!string.IsNullOrEmpty(nativePath) && File.Exists(nativePath) && File.Exists(nativePath + "meta"))
                {
                    var meta = FastLoadMetadata(nativePath);
                    if (meta != null)
                    {
                        byte[] data;
                        try { data = File.ReadAllBytes(nativePath); }
                        catch { data = null; }
                        if (IsRawDataValidForMeta(meta, data, queueCreateMipMaps, nativePath))
                        {
                            payload = new DiskCachePayload { Data = data, Meta = meta, CachePath = nativePath, FromZstd = false };
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Worker Process: zstd only if already in ImageLoadingMgr decompressed LRU (no sync decompress).</summary>
        public bool TryFillPreprocessedRawForProcess(
            string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, float bumpStrength,
            bool setSize, int requestWidth, int requestHeight, bool queueCreateMipMaps,
            out byte[] raw, out int rawLength, out int width, out int height, out TextureFormat textureFormat, out bool createMipMaps)
        {
            raw = null;
            rawLength = 0;
            width = 0;
            height = 0;
            textureFormat = TextureFormat.RGBA32;
            createMipMaps = queueCreateMipMaps;

            var qi = new ImageLoaderThreaded.QueuedImage();
            qi.imgPath = imgPath;
            qi.compress = compress;
            qi.linear = linear;
            qi.isNormalMap = isNormalMap;
            qi.createAlphaFromGrayscale = createAlphaFromGrayscale;
            qi.createNormalFromBump = createNormalFromBump;
            qi.invert = invert;
            qi.bumpStrength = bumpStrength;
            qi.setSize = setSize;
            qi.width = requestWidth;
            qi.height = requestHeight;
            qi.createMipMaps = queueCreateMipMaps;

            lock (candidateLock)
            {
                if (!string.IsNullOrEmpty(imgPath))
                    pathCreateMipMapsHints[imgPath] = queueCreateMipMaps;
            }

            string zstdPath = ResolveZstdServePath(qi);
            if (!string.IsNullOrEmpty(zstdPath))
            {
                var meta = FastLoadMetadata(zstdPath);
                byte[] memData = null;
                lock (decompressedCacheLock)
                {
                    CachedDecompressed cached;
                    if (decompressedCache.TryGetValue(zstdPath, out cached))
                    {
                        memData = cached.Data;
                    }
                }

                if (meta != null && memData != null && IsRawDataValidForMeta(meta, memData, queueCreateMipMaps, zstdPath))
                {
                    TryUpgradeDiskCacheMetaIfStale(zstdPath, meta, memData.Length, queueCreateMipMaps);
                    rawLength = memData.Length;
                    raw = ByteArrayPool.Rent(rawLength);
                    Buffer.BlockCopy(memData, 0, raw, 0, rawLength);
                    width = meta.Width;
                    height = meta.Height;
                    textureFormat = meta.Format;
                    createMipMaps = meta.CreateMipMaps;
                    return true;
                }
            }

            DiskCachePayload nativePayload;
            if (TryLoadDiskCachePayload(qi, false, false, true, out nativePayload))
            {
                rawLength = nativePayload.Data.Length;
                raw = ByteArrayPool.Rent(rawLength);
                Buffer.BlockCopy(nativePayload.Data, 0, raw, 0, rawLength);
                width = nativePayload.Meta.Width;
                height = nativePayload.Meta.Height;
                textureFormat = nativePayload.Meta.Format;
                createMipMaps = nativePayload.Meta.CreateMipMaps;
                return true;
            }

            return false;
        }

        /// <summary>Main-thread / hook serve: apply zstd or native cache to an existing texture.</summary>
        public bool TryApplyDiskCacheToTexture(ImageLoaderThreaded.QueuedImage qi, Texture2D tex, bool markNonReadable)
        {
            if (qi == null || tex == null) return false;

            DiskCachePayload payload;
            if (!TryLoadDiskCachePayload(qi, true, false, true, out payload)) return false;

            bool forceReadable = ShouldForceCpuReadable(qi, payload.Meta.IsReadable);
            // Caller may request non-readable GPU texture; never override character/sim CPU-read needs.
            bool markNonReadableEffective = markNonReadable && !forceReadable;
            bool createMipMaps = payload.Meta.CreateMipMaps;
            if (!TextureUtil.ApplyCachedRawToTexture(tex, payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                createMipMaps, qi.linear, markNonReadableEffective, forceReadable))
            {
                return false;
            }

            if (forceReadable)
            {
                LogUtil.Log("[VPB] ImageLoadingMgr applied CPU-readable texture from cache: " + qi.imgPath);
            }

            return true;
        }

        public void ClearCache()
        {
            CancelPendingTextureLoads();
            ClearCandidates();
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
            ClearTextureCacheReferences();
        }

        private void CancelPendingTextureLoads()
        {
            Interlocked.Increment(ref loadGeneration);
            lock (pendingMainThreadLock)
            {
                pendingMainThreadTextureCreates.Clear();
                pendingMainThreadTextureCreateCount = 0;
                pendingMainThreadTextureCreateBytes = 0;
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

            if (IsZstdCachePathBusy(cachePath))
                return null;

            try
            {
                string metaPath = cachePath + "meta";
                
                byte[] metaBytes = ReadCacheFileBytes(metaPath, 64 * 1024);
                if (metaBytes == null) return null;
                
                var metaJson = JSON.Parse(System.Text.Encoding.UTF8.GetString(metaBytes));
                
                int w = metaJson["width"].AsInt;
                int h = metaJson["height"].AsInt;
                if (w <= 0 || h <= 0 || w > MaxCacheDimension || h > MaxCacheDimension)
                    return null;
                TextureFormat fmt = TextureFormat.RGBA32;
                try { fmt = (TextureFormat)Enum.Parse(typeof(TextureFormat), metaJson["format"].Value); } catch { }
                
                bool isDown = false;
                try { isDown = metaJson["downscaled"].AsBool; } catch { }

                bool isRead = false;
                try
                {
                    if (metaJson["isReadable"] != null)
                    {
                        isRead = metaJson["isReadable"].AsBool;
                    }
                }
                catch { }

                bool createMipMaps = false;
                int mipCount = 0;
                string mipStorage = null;
                try { createMipMaps = metaJson["createMipMaps"].AsBool; } catch { }
                try { mipCount = metaJson["mipCount"].AsInt; } catch { }
                try { mipStorage = metaJson["mipStorage"].Value; } catch { }

                var entry = new MetadataEntry
                {
                    Width = w,
                    Height = h,
                    Format = fmt,
                    IsDownscaled = isDown,
                    IsReadable = isRead,
                    CreateMipMaps = createMipMaps,
                    MipCount = mipCount > 0 ? mipCount : TextureUtil.CountMipLevels(w, h),
                    MipStorage = mipStorage
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
            return FastGetDecompressed(cachePath, MaxMemoryCacheBytes, -1);
        }

        private byte[] FastGetDecompressed(string cachePath, int maxDecompressedBytes, int generation)
        {
            if (generation >= 0 && !IsLoadGenerationCurrent(generation)) return null;
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

            if (IsZstdCachePathBusy(cachePath))
                return null;

            try
            {
                byte[] compressedData = ReadCacheFileBytes(cachePath, MaxServeCachePayloadBytes);
                if (compressedData == null)
                    return null;

                byte[] decompressedData = ZstdCompressor.Decompress(compressedData, maxDecompressedBytes);
                
                if (decompressedData == null || decompressedData.Length == 0)
                    return null;
                if (decompressedData.Length > MaxMemoryCacheBytes)
                    return null;
                if (generation >= 0 && !IsLoadGenerationCurrent(generation)) return null;

                lock (decompressedCacheLock)
                {
                    if (generation >= 0 && !IsLoadGenerationCurrent(generation)) return null;
                    if (decompressedCache.ContainsKey(cachePath))
                    {
                        return decompressedCache[cachePath].Data;
                    }

                    while (currentMemoryUsage + decompressedData.Length > MaxMemoryCacheBytes && lruOrder.Count > 0)
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

        private static long EstimateTextureCacheBytes(Texture2D tex)
        {
            if (tex == null) return 0;

            try
            {
                long runtimeBytes = Profiler.GetRuntimeMemorySizeLong(tex);
                if (runtimeBytes > 0) return runtimeBytes;
            }
            catch { }

            try
            {
                int width = Math.Max(1, tex.width);
                int height = Math.Max(1, tex.height);
                long bytes = TextureUtil.GetExpectedRawDataSize(width, height, tex.format);
                if (bytes <= 0) bytes = (long)width * height * 4L;
                if (tex.mipmapCount > 1) bytes += bytes / 3L;
                return Math.Max(1L, bytes);
            }
            catch
            {
                return 1L;
            }
        }

        private void TouchTextureCacheEntryLocked(string path, CachedTexture entry)
        {
            if (entry.LruNode == null)
            {
                entry.LruNode = textureLruOrder.AddFirst(path);
                return;
            }

            textureLruOrder.Remove(entry.LruNode);
            textureLruOrder.AddFirst(entry.LruNode);
        }

        private bool RemoveTextureFromCacheLocked(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            CachedTexture entry;
            if (!textureCache.TryGetValue(path, out entry)) return false;

            textureCache.Remove(path);
            if (entry != null)
            {
                if (entry.LruNode != null) textureLruOrder.Remove(entry.LruNode);
                currentTextureCacheBytes -= entry.EstimatedBytes;
                if (currentTextureCacheBytes < 0) currentTextureCacheBytes = 0;
            }
            return true;
        }

        private void RemoveTextureFromCache(string path)
        {
            bool removed;
            lock (textureCacheLock)
            {
                removed = RemoveTextureFromCacheLocked(path);
            }
            if (removed) TextureUtil.UnmarkDownscaledActive(GetDownscaledKey(path));
        }

        internal bool TryGetTextureCachePath(Texture2D tex, out string path)
        {
            path = null;
            if (tex == null) return false;
            lock (textureCacheLock)
            {
                foreach (var item in textureCache)
                {
                    if (item.Value != null && object.ReferenceEquals(item.Value.Texture, tex))
                    {
                        path = item.Key;
                        return true;
                    }
                }
            }
            return false;
        }

        internal void ReleaseTextureCacheReference(string path)
        {
            RemoveTextureFromCache(path);
        }

        private void EvictTextureCacheIfNeededLocked()
        {
            while (currentTextureCacheBytes > MaxTextureCacheBytes && textureCache.Count > 0)
            {
                LinkedListNode<string> lru = textureLruOrder.Last;
                if (lru == null) break;

                string path = lru.Value;
                if (!RemoveTextureFromCacheLocked(path))
                {
                    textureLruOrder.Remove(lru);
                    continue;
                }
                TextureUtil.UnmarkDownscaledActive(GetDownscaledKey(path));
            }
        }

        public void ClearTextureCacheReferences()
        {
            TextureUtil.UnmarkDownscaledActiveByPrefix("ILM:");
            lock (textureCacheLock)
            {
                textureCache.Clear();
                textureLruOrder.Clear();
                currentTextureCacheBytes = 0;
            }
        }

        public Texture2D GetTextureFromCache(string path)
        {
            bool removed = false;
            lock (textureCacheLock)
            {
                CachedTexture entry;
                if (textureCache.TryGetValue(path, out entry))
                {
                    if (entry != null && entry.Texture != null)
                    {
                        TouchTextureCacheEntryLocked(path, entry);
                        return entry.Texture;
                    }
                    removed = RemoveTextureFromCacheLocked(path);
                }
            }
            if (removed) TextureUtil.UnmarkDownscaledActive(GetDownscaledKey(path));
            return null;
        }

        void RegisterTexture(string path, Texture2D tex)
        {
            if (string.IsNullOrEmpty(path) || tex == null) return;
            long estimatedBytes = EstimateTextureCacheBytes(tex);
            TextureUtil.UnmarkDownscaledActive(GetDownscaledKey(path));
            lock (textureCacheLock)
            {
                RemoveTextureFromCacheLocked(path);
                if (estimatedBytes > MaxTextureCacheBytes) return;

                var entry = new CachedTexture
                {
                    Texture = tex,
                    EstimatedBytes = estimatedBytes
                };
                textureCache[path] = entry;
                currentTextureCacheBytes += estimatedBytes;
                TouchTextureCacheEntryLocked(path, entry);
                EvictTextureCacheIfNeededLocked();
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
                    // Scan-whitelist + on-demand registration: the image callback is deferred
                    // until scene load finishes (DelayDoCallback) and the texture is often served
                    // from VPB's own cache, so the source package may never have been registered in
                    // VaM's FileManager. Plugins like MacGruber PostMagic enumerate sibling files in
                    // their package directory (FileManagerSecure.GetFiles) inside this callback; if
                    // the package isn't registered, VaM's GetFiles throws "non-existent path" and
                    // aborts the plugin. Ensure the source package is live before invoking.
                    if (!qi.isThumbnail)
                    {
                        try
                        {
                            if (ScanWhitelistManager.Instance != null && ScanWhitelistManager.Instance.IsEnabled)
                            {
                                string uid = VamOnDemandLoader.UidFromEntryPath(qi.imgPath);
                                if (!string.IsNullOrEmpty(uid))
                                    VamOnDemandLoader.TryRegisterPackageOnDemand(uid);
                            }
                        }
                        catch { }
                    }

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

        /// <summary>
        /// MaterialOptions custom slots need OnTexture*Loaded ASAP after URL sync so GPU bind
        /// races less with skin-wrap/cloth material reconnect (issue #80). Still wait out real scene loads.
        /// </summary>
        static bool IsMaterialOptionsCustomCallback(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || qi.callback == null) return false;
            try
            {
                var m = qi.callback.Method;
                if (m == null) return false;
                string mName = m.Name ?? "";
                if (!mName.StartsWith("OnTexture", StringComparison.Ordinal)) return false;
                Type t = m.DeclaringType;
                if (t == null) return false;
                return typeof(MaterialOptions).IsAssignableFrom(t);
            }
            catch
            {
                return false;
            }
        }

        IEnumerator DelayDoCallback(ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            while (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive())
            {
                if (!IsLoadGenerationCurrent(generation)) yield break;
                yield return null;
            }
            yield return waitForEndOfFrame;
            if (!IsLoadGenerationCurrent(generation)) yield break;
            // MaterialOptions: one EOF is enough outside scene load; second EOF widens the
            // window where late material reconnect can overwrite the custom slot with defaults.
            if (!IsMaterialOptionsCustomCallback(qi))
                yield return waitForEndOfFrame;
            if (!IsLoadGenerationCurrent(generation)) yield break;
            DoCallback(qi);
        }

        public bool RequestImmediate(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return false;
            if (qi.skipCache) return false;
            // Match VaM ImageLoaderThreaded: forceReload must reprocess, not serve VPB RAM/disk hit.
            if (qi.forceReload) return false;
            if (Settings.Instance == null || Settings.Instance.ThumbnailThreshold == null) return false;

            int threshold = Settings.Instance.ThumbnailThreshold.Value;
            if (qi.setSize && qi.width > 0 && qi.width <= threshold && qi.height > 0 && qi.height <= threshold)
                return false;

            string cacheKey = GetTextureCacheKey(qi);

            if (qi.tex == null)
            {
                var cached = GetTextureFromCache(cacheKey);
                if (cached != null && TextureMeetsCpuReadableRequirement(qi, cached))
                {
                    qi.tex = cached;
                    return true;
                }
            }

            if (TryApplyMemoryCacheImmediate(qi, cacheKey))
                return true;

            DiskCachePayload payload;
            if (TryLoadDiskCachePayload(qi, true, true, true, out payload))
            {
                try
                {
                    if (payload.Data != null && payload.Data.Length > MaxSyncDecompressedPayloadBytes)
                        return false;

                    bool forceReadable = ShouldForceCpuReadable(qi, payload.Meta.IsReadable);
                    Texture2D tex = qi.tex;
                    if (tex == null)
                    {
                        tex = TextureUtil.CreateTextureFromCachedRaw(payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                            payload.Meta.CreateMipMaps, qi.linear, !forceReadable, forceReadable);
                        if (tex == null) return false;
                    }
                    else if (!TextureUtil.ApplyCachedRawToTexture(tex, payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                        payload.Meta.CreateMipMaps, qi.linear, !forceReadable, forceReadable))
                    {
                        return false;
                    }

                    qi.tex = tex;
                    tex.name = qi.cacheSignature;
                    RegisterTexture(cacheKey, tex);
                    if (payload.Meta.IsDownscaled)
                        TextureUtil.MarkDownscaledActive(GetDownscaledKey(cacheKey));
                    return true;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("RequestImmediate failed for " + payload.CachePath + ": " + ex.Message);
                    return false;
                }
            }

            return false;
        }

        public bool Request(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return false;
            LogUtil.MarkImageActivity();
            if (qi.skipCache) return false;

            // VaM JSONStorableUrl.Reload / browse sets forceReload. Native UseCachedTex still
            // reprocesses; VPB must not short-circuit to a silent RAM/disk hit (issue #80).
            if (qi.forceReload)
            {
                try
                {
                    string evictKey = GetTextureCacheKey(qi);
                    if (!string.IsNullOrEmpty(evictKey))
                    {
                        RemoveTextureFromCache(evictKey);
                    }
                }
                catch { }
                return false;
            }

            // VaM's single-worker loader binds during scene load; VPB cannot retain decompressed payloads until scene end.
            if (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive()) return false;
            int generation = CurrentLoadGeneration;

            if (Settings.Instance == null || Settings.Instance.ThumbnailThreshold == null) return false;

            int threshold = Settings.Instance.ThumbnailThreshold.Value;
            if (qi.setSize && qi.width > 0 && qi.width <= threshold && qi.height > 0 && qi.height <= threshold)
                return false;

            string cacheKey = GetTextureCacheKey(qi);
            
            var cacheTexture = GetTextureFromCache(cacheKey);
            if (cacheTexture != null && TextureMeetsCpuReadableRequirement(qi, cacheTexture))
            {
                qi.tex = cacheTexture;
                if (Messager.singleton != null)
                {
                    Messager.singleton.StartCoroutine(DelayDoCallback(qi, generation));
                }
                else if (IsLoadGenerationCurrent(generation))
                {
                    DoCallback(qi);
                }
                return true;
            }
            // Stale non-readable RAM entry (pre-fix character/sim serve): drop and rebuild readable.
            if (cacheTexture != null)
            {
                try
                {
                    RemoveTextureFromCache(cacheKey);
                }
                catch { }
            }

            if (TryServeFromMemoryCache(qi, cacheKey, true, generation))
                return true;

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
                    if (TryQueueBackground((state) => LoadAndDecompressBackground(cacheKey, vpbCachePath, qi, generation))) return true;
                    inflightKeys.Remove(cacheKey);
                    return false;
                }

                string nativeDiskPath = TextureUtil.FindVaMNativeDiskCachePath(
                    qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                    qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength);
                if (!string.IsNullOrEmpty(nativeDiskPath))
                {
                    inflightKeys.Add(cacheKey);
                    if (TryQueueBackground((state) => LoadVaMNativeDiskCacheBackground(cacheKey, nativeDiskPath, qi, generation))) return true;
                    inflightKeys.Remove(cacheKey);
                    return false;
                }

                try
                {
                    int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0;
                    if (lvl >= 1)
                    {
                        LogUtil.Log("[VPB Req] MISS path=" + qi.imgPath + " C=" + qi.compress + " L=" + qi.linear + " A=" + qi.createAlphaFromGrayscale + " N=" + qi.isNormalMap + " key=" + cacheKey);
                    }
                }
                catch { }
                return false;
            }
        }

        private void LoadVaMNativeDiskCacheBackground(string cacheKey, string nativePath, ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
            if (!TryAcquireTexturePayloadWorker(generation))
            {
                FallbackInflightToNative(cacheKey, qi, generation);
                return;
            }
            try
            {
                var meta = FastLoadMetadata(nativePath);
                if (meta == null)
                {
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative, generation);
                    return;
                }

                byte[] data = ReadCacheFileBytes(nativePath, MaxServeCachePayloadBytes);
                if (data == null)
                {
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative, generation);
                    return;
                }

                if (data.Length > MaxServeCachePayloadBytes)
                {
                    try
                    {
                        LogUtil.LogWarning("[VPB] Rejecting oversize native cache (" + data.Length + " bytes): " + nativePath);
                    }
                    catch { }
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative, generation);
                    return;
                }

                bool queueMip = ResolveQueueCreateMipMaps(qi);
                if (!IsRawDataValidForMeta(meta, data, queueMip, nativePath))
                {
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative, generation);
                    return;
                }

                if (!IsLoadGenerationCurrent(generation)) return;

                var decompressed = new DecompressedData
                {
                    CacheKey = cacheKey,
                    Data = data,
                    Meta = meta,
                    OriginalQI = qi,
                    Generation = generation
                };

                ScheduleCreateTextureOnMainThread(decompressed);
            }
            catch (Exception ex)
            {
                if (!IsLoadGenerationCurrent(generation)) return;
                LogUtil.LogError("LoadVaMNativeDiskCacheBackground failed for " + nativePath + ": " + ex.Message);
                FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative, generation);
            }
            finally
            {
                Interlocked.Decrement(ref activeTexturePayloadWorkers);
            }
        }

        private void LoadAndDecompressBackground(string cacheKey, string cachePath, ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
            if (!TryAcquireTexturePayloadWorker(generation))
            {
                FallbackInflightToNative(cacheKey, qi, generation);
                return;
            }
            try
            {
                if (IsZstdPathQuarantined(cachePath))
                {
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd, generation);
                    return;
                }

                var meta = FastLoadMetadata(cachePath);
                if (meta == null)
                {
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd, generation);
                    return;
                }

                if (meta.Width <= 0 || meta.Height <= 0 || meta.Width > MaxCacheDimension || meta.Height > MaxCacheDimension)
                {
                    if (QuarantineZstdPath(cachePath))
                        LogUtil.LogError("LoadAndDecompress: Invalid texture dimensions " + meta.Width + "x" + meta.Height + " for " + cachePath + " (quarantined for session)");
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd, generation);
                    return;
                }

                int maxDecompressedBytes = TextureUtil.GetExpectedFullMipChainSize(meta.Width, meta.Height, meta.Format);
                if (maxDecompressedBytes <= 0 || maxDecompressedBytes > MaxServeCachePayloadBytes)
                {
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd, generation);
                    return;
                }

                byte[] decompressedData = FastGetDecompressed(cachePath, maxDecompressedBytes, generation);
                if (decompressedData == null)
                {
                    if (!IsLoadGenerationCurrent(generation)) return;
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd, generation);
                    return;
                }

                bool queueMip = ResolveQueueCreateMipMaps(qi);
                if (!IsRawDataValidForMeta(meta, decompressedData, queueMip, cachePath))
                {
                    if (QuarantineZstdPath(cachePath))
                    {
                        if (decompressedData.Length > MaxServeCachePayloadBytes)
                            LogUtil.LogError("LoadAndDecompress: Decompressed payload too large (" + decompressedData.Length + ") for " + cachePath + " (quarantined for session)");
                        else
                            LogUtil.LogError("LoadAndDecompress: Decompressed data size mismatch for " + cachePath + " (quarantined for session)");
                    }
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd, generation);
                    return;
                }

                TryUpgradeDiskCacheMetaIfStale(cachePath, meta, decompressedData.Length, queueMip);

                try {
                    int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0;
                    if (lvl >= 1) LogUtil.Log("[VPB Load] path=" + cachePath + " fmt=" + meta.Format + " " + meta.Width + "x" + meta.Height
                        + " mips=" + meta.CreateMipMaps + " storage=" + (meta.MipStorage ?? "?") + " mipCount=" + meta.MipCount + " got=" + decompressedData.Length);
                } catch { }

                var decompressed = new DecompressedData
                {
                    CacheKey = cacheKey,
                    Data = decompressedData,
                    Meta = meta,
                    OriginalQI = qi,
                    Generation = generation
                };

                ScheduleCreateTextureOnMainThread(decompressed);
            }
            catch (Exception ex)
            {
                if (!IsLoadGenerationCurrent(generation)) return;
                LogUtil.LogError("LoadAndDecompressBackground failed for " + cachePath + ": " + ex.Message);
                HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd, generation);
            }
            finally
            {
                Interlocked.Decrement(ref activeTexturePayloadWorkers);
            }
        }

        private void ScheduleCreateTextureOnMainThread(DecompressedData data)
        {
            if (data == null || data.Data == null || !IsLoadGenerationCurrent(data.Generation)) return;
            if (Messager.singleton == null)
            {
                CreateTexture(data);
                return;
            }

            var dropped = new List<DecompressedData>();
            lock (pendingMainThreadLock)
            {
                if (data.Data.LongLength > MaxPendingMainThreadTextureCreateBytes)
                {
                    dropped.Add(data);
                }
                else
                {
                    while (pendingMainThreadTextureCreates.Count > 0
                        && (pendingMainThreadTextureCreateCount >= MaxPendingMainThreadTextureCreates
                            || pendingMainThreadTextureCreateBytes + data.Data.LongLength > MaxPendingMainThreadTextureCreateBytes))
                    {
                        DecompressedData oldest = pendingMainThreadTextureCreates.Dequeue();
                        pendingMainThreadTextureCreateCount--;
                        pendingMainThreadTextureCreateBytes -= oldest != null && oldest.Data != null ? oldest.Data.LongLength : 0;
                        dropped.Add(oldest);
                    }

                    if (pendingMainThreadTextureCreateCount >= MaxPendingMainThreadTextureCreates
                        || pendingMainThreadTextureCreateBytes + data.Data.LongLength > MaxPendingMainThreadTextureCreateBytes)
                    {
                        dropped.Add(data);
                    }
                    else
                    {
                        pendingMainThreadTextureCreates.Enqueue(data);
                        pendingMainThreadTextureCreateCount++;
                        pendingMainThreadTextureCreateBytes += data.Data.LongLength;
                    }
                }
            }

            for (int i = 0; i < dropped.Count; i++)
                FallbackInflightToNative(dropped[i]);

            if (dropped.Count > 0 && object.ReferenceEquals(dropped[dropped.Count - 1], data)) return;

            if (mainThreadTextureCreatePump == null)
                mainThreadTextureCreatePump = Messager.singleton.StartCoroutine(PumpCreateTextureOnMainThread());
        }

        private void FallbackInflightToNative(DecompressedData data)
        {
            if (data == null || !IsLoadGenerationCurrent(data.Generation)) return;
            FallbackInflightToNative(data.CacheKey, data.OriginalQI, data.Generation);
        }

        private void FallbackInflightToNative(string cacheKey, ImageLoaderThreaded.QueuedImage qi, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
            var targets = new List<ImageLoaderThreaded.QueuedImage>();
            CollectInflightTargets(cacheKey, qi, targets);
            for (int i = 0; i < targets.Count; i++)
                EnqueueVaMImageLoadOnMainThread(targets[i], generation);
        }

        private IEnumerator PumpCreateTextureOnMainThread()
        {
            while (true)
            {
                DecompressedData data = null;
                lock (pendingMainThreadLock)
                {
                    if (pendingMainThreadTextureCreates.Count == 0)
                    {
                        mainThreadTextureCreatePump = null;
                        yield break;
                    }
                    data = pendingMainThreadTextureCreates.Dequeue();
                    pendingMainThreadTextureCreateCount--;
                    pendingMainThreadTextureCreateBytes -= data != null && data.Data != null ? data.Data.LongLength : 0;
                    if (pendingMainThreadTextureCreateBytes < 0) pendingMainThreadTextureCreateBytes = 0;
                }

                yield return waitForEndOfFrame;

                while (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive())
                {
                    if (!IsLoadGenerationCurrent(data.Generation)) break;
                    yield return null;
                }

                if (IsLoadGenerationCurrent(data.Generation)) CreateTexture(data);
            }
        }

        private void CreateTexture(DecompressedData data)
        {
            if (data == null || data.OriginalQI == null || !IsLoadGenerationCurrent(data.Generation)) return;
            var sw = Stopwatch.StartNew();
            try
            {
                string path = data.OriginalQI.imgPath ?? "";
                bool isSimTexturePath = SuperControllerHook.IsSimulationTexturePath(path);
                bool isCharacterTex = SuperControllerHook.IsCharacterTextureQueuedImage(data.OriginalQI);
                bool forceReadable = ShouldForceCpuReadable(data.OriginalQI, data.Meta.IsReadable);
                
                if (isSimTexturePath || isCharacterTex || data.Meta.IsReadable)
                {
                    LogUtil.Log("[VPB] CreateTexture readable: path='" + path + "', sim=" + isSimTexturePath
                        + ", char=" + isCharacterTex + ", metaIsReadable=" + data.Meta.IsReadable
                        + ", forceReadable=" + forceReadable);
                }

                if (!IsCacheMetaAndPayloadSane(data.Meta, data.Data))
                {
                    LogUtil.LogWarning("[VPB] CreateTexture rejected insane cache payload for " + path);
                    string diskPath = data.OriginalQI != null ? GetCachePath(data.OriginalQI) : null;
                    if (!string.IsNullOrEmpty(diskPath))
                        HandleBusyOrTransientDiskCacheFailure(data.CacheKey, diskPath, data.OriginalQI, DiskCacheFailureKind.VpbZstd, data.Generation);
                    else
                        EnqueueVaMImageLoadOnMainThread(data.OriginalQI, data.Generation);
                    return;
                }

                bool createMipMaps = data.Meta.CreateMipMaps;
                if (!createMipMaps && data.OriginalQI != null)
                    createMipMaps = ResolveQueueCreateMipMaps(data.OriginalQI);

                Texture2D tex = TextureUtil.CreateTextureFromCachedRaw(data.Data, data.Meta.Width, data.Meta.Height, data.Meta.Format,
                    createMipMaps, data.OriginalQI.linear, !forceReadable, forceReadable);
                if (tex == null)
                {
                    EnqueueVaMImageLoadOnMainThread(data.OriginalQI, data.Generation);
                    return;
                }

                if (forceReadable)
                {
                    LogUtil.Log("[VPB] Created CPU-readable texture from cache: " + path);
                }

                data.OriginalQI.tex = tex;
                tex.name = data.OriginalQI.cacheSignature;

                RegisterTexture(data.CacheKey, tex);
                if (data.Meta.IsDownscaled)
                    TextureUtil.MarkDownscaledActive(GetDownscaledKey(data.CacheKey));
                
                ResolveInflight(data.CacheKey, tex, data.Generation);
                if (Messager.singleton != null)
                {
                    Messager.singleton.StartCoroutine(DelayDoCallback(data.OriginalQI, data.Generation));
                }
                else if (IsLoadGenerationCurrent(data.Generation))
                {
                    DoCallback(data.OriginalQI);
                }
            }
            catch (Exception ex)
            {
                if (IsLoadGenerationCurrent(data.Generation))
                {
                    LogUtil.LogError("CreateTexture failed for " + data.CacheKey + ": " + ex.Message);
                    EnqueueVaMImageLoadOnMainThread(data.OriginalQI, data.Generation);
                }
            }
            finally
            {
                sw.Stop();
                try
                {
                    int lvl = Settings.Instance != null && Settings.Instance.TextureLogLevel != null ? Settings.Instance.TextureLogLevel.Value : 0;
                    if (lvl >= 1 && sw.ElapsedMilliseconds >= 50)
                    {
                        string imgPath = data.OriginalQI != null ? data.OriginalQI.imgPath : "?";
                        int rawLen = data.Data != null ? data.Data.Length : 0;
                        LogUtil.Log("[VPB Load] CreateTexture ms=" + sw.ElapsedMilliseconds + " raw=" + rawLen + " path=" + imgPath);
                    }
                }
                catch { }

                if (IsLoadGenerationCurrent(data.Generation)) lock (inflightLock)
                {
                    inflightKeys.Remove(data.CacheKey);
                }
            }
        }

        public void ResolveInflightForQueuedImage(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            int generation;
            if (!TryGetCurrentCandidateGeneration(qi, out generation)) return;
            string cacheKey = GetTextureCacheKey(qi);

            if (qi.tex != null)
            {
                RegisterTexture(cacheKey, qi.tex);
            }

            ResolveInflight(cacheKey, qi.tex, generation);
            lock (inflightLock)
            {
                if (IsLoadGenerationCurrent(generation)) inflightKeys.Remove(cacheKey);
            }
        }

        private void ResolveInflight(string cacheKey, Texture2D tex, int generation)
        {
            if (!IsLoadGenerationCurrent(generation)) return;
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
                        Messager.singleton.StartCoroutine(DelayDoCallback(w, generation));
                    }
                    else if (IsLoadGenerationCurrent(generation))
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

            string nativeCacheDir = TextureUtil.ResolveTextureCacheDirFullPath();
            string vpbCacheDir = VamHookPlugin.GetCacheDir();

            if (!Directory.Exists(nativeCacheDir))
            {
                LogUtil.LogError("[VPB] Native cache directory not found: " + nativeCacheDir);
                return;
            }

            LogUtil.Log("Starting bulk Zstd compression from " + nativeCacheDir + " to " + vpbCacheDir);
            CurrentZstdStats = new ZstdStats { IsRunning = true, IsDecompression = false, StartTime = DateTime.Now };
            try { VpbProgressService.BeginBulkZstd(decompress: false); } catch { }

            // Dedicated thread — never WaitOne on ThreadPool (classic pool starvation deadlock).
            var t = new Thread(() =>
            {
                try
                {
                    BulkZstdWorker(nativeCacheDir, vpbCacheDir);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Bulk Zstd compression failed: " + ex.ToString());
                    CurrentZstdStats.IsRunning = false;
                    try { VpbProgressService.EndBulkZstd(); } catch { }
                }
            });
            t.IsBackground = true;
            t.Name = "VPB-BulkZstd-Compress";
            t.Start();
        }

        private static bool BulkZstdIsThumbnail(JSONNode metaJson, int threshold)
        {
            if (metaJson == null) return false;
            bool isThumb = metaJson["isThumbnail"].AsBool;
            int width = metaJson["width"].AsInt;
            int height = metaJson["height"].AsInt;
            return isThumb || (width > 0 && width <= threshold && height > 0 && height <= threshold);
        }

        private static bool BulkZstdNeedsCompression(string sourcePath, string targetPath, int compressionLevel)
        {
            if (!File.Exists(targetPath)) return true;
            try
            {
                if (File.Exists(sourcePath) && File.GetLastWriteTime(sourcePath) > File.GetLastWriteTime(targetPath))
                {
                    return true;
                }
            }
            catch { return true; }

            string metaPath = targetPath + "meta";
            if (!File.Exists(metaPath)) return true;
            try
            {
                var existingMeta = JSON.Parse(File.ReadAllText(metaPath));
                if (existingMeta == null || existingMeta["zstdLevel"] == null || existingMeta["zstdLevel"].AsInt != compressionLevel)
                {
                    return true;
                }
            }
            catch { return true; }

            return false;
        }

        private static void BulkZstdWriteMeta(string targetPath, string sourceMetaPath, JSONNode metaJson, int compressionLevel, long rawByteLength = -1)
        {
            if (metaJson != null)
            {
                try
                {
                    metaJson["type"] = "compressed";
                    metaJson["width"] = metaJson["width"].Value;
                    metaJson["height"] = metaJson["height"].Value;
                    metaJson["zstdLevel"].AsInt = compressionLevel;
                    if (rawByteLength > 0)
                    {
                        int w = metaJson["width"].AsInt;
                        int h = metaJson["height"].AsInt;
                        TextureFormat fmt = TextureFormat.RGBA32;
                        try
                        {
                            if (metaJson["format"] != null)
                                fmt = (TextureFormat)Enum.Parse(typeof(TextureFormat), metaJson["format"].Value);
                        }
                        catch { }
                        bool queueMip = false;
                        bool hadMipFields = false;
                        try
                        {
                            hadMipFields = metaJson["mipStorage"] != null && !string.IsNullOrEmpty(metaJson["mipStorage"].Value);
                            queueMip = metaJson["createMipMaps"].AsBool;
                        }
                        catch { }
                        if (!hadMipFields && !queueMip && (fmt == TextureFormat.DXT1 || fmt == TextureFormat.DXT5))
                            queueMip = true;
                        TextureUtil.WriteMipFieldsToMeta(metaJson, w, h, fmt, (int)rawByteLength, queueMip);
                    }
                    File.WriteAllText(targetPath + "meta", VPB.src.util.JsonSerializationUtil.Serialize(metaJson, 1024));
                    return;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Failed to update meta for " + targetPath + ": " + ex.Message);
                }
            }

            if (!string.IsNullOrEmpty(sourceMetaPath) && File.Exists(sourceMetaPath))
            {
                try { File.Copy(sourceMetaPath, targetPath + "meta", true); }
                catch { }
            }
        }

        private static void BulkZstdCompressNativeToZstd(string nativePath, string targetPath, int compressionLevel, JSONNode metaJson)
        {
            // mip-meta needs only the raw .vamcache byte length, not the bytes; SaveCacheFromFile streams via
            // external zstd. Reading the whole 64MB+ file into managed memory per file churned the Boehm heap.
            long rawByteLength = -1;
            try { rawByteLength = new FileInfo(nativePath).Length; } catch { }
            ZstdCompressor.SaveCacheFromFile(targetPath, nativePath, compressionLevel);
            BulkZstdWriteMeta(targetPath, nativePath + "meta", metaJson, compressionLevel, rawByteLength);
        }

        private static void BulkZstdRecompressZstdInPlace(string zstdPath, int compressionLevel, JSONNode metaJson)
        {
            byte[] compressed = File.ReadAllBytes(zstdPath);
            byte[] raw = ZstdCompressor.Decompress(compressed);
            if (raw == null || raw.Length == 0)
            {
                throw new InvalidOperationException("decompress returned empty");
            }

            ZstdCompressor.SaveCache(zstdPath, raw, compressionLevel);
            BulkZstdWriteMeta(zstdPath, zstdPath + "meta", metaJson, compressionLevel, raw.LongLength);
        }

        private static void LogBulkZstdMemSample(int fileIndex, long fileBytes, bool recompressPath)
        {
            // recompress decompresses in-memory, so its real pending alloc is larger than the on-disk fileBytes.
            long liveBytes = GC.GetTotalMemory(false);
            int gcCount = GC.CollectionCount(GC.MaxGeneration);
            LogUtil.Log(string.Format(
                "[VPB][zstd-mem] file#{0} liveBytes={1} fileBytes={2} gcCount={3} path={4}",
                fileIndex, liveBytes, fileBytes, gcCount, recompressPath ? "recompress" : "native"));
        }

        private enum BulkZstdWorkKind
        {
            NativeToZstd = 0,
            RecompressZstd = 1
        }

        private sealed class BulkZstdWorkItem
        {
            public BulkZstdWorkKind Kind;
            public string SourcePath;
            public string TargetPath;
            public string MetaPath;
            public string DisplayName;
            public bool DeleteNativeAfter;
            public JSONNode MetaJson;
        }

        private readonly object bulkZstdStatsLock = new object();

        private static int ResolveBulkZstdParallelism()
        {
            try
            {
                int p = Environment.ProcessorCount;
                if (p < 1) p = 1;
                int n = p;
                if (n < 2) n = 2;
                if (n > 12) n = 12;
                return n;
            }
            catch { return 4; }
        }

        private static void TryBoostThreadPoolForBulkZstd(int workers)
        {
            try
            {
                int minW, minIo, maxW, maxIo;
                ThreadPool.GetMinThreads(out minW, out minIo);
                ThreadPool.GetMaxThreads(out maxW, out maxIo);
                int want = workers + 2;
                if (want < 8) want = 8;
                if (want > 32) want = 32;
                if (want > minW) ThreadPool.SetMinThreads(want, minIo);
                if (want > maxW) ThreadPool.SetMaxThreads(want, maxIo);
            }
            catch { }
        }

        private void BulkZstdNoteProgress(string currentFile, long originalAdd, long compressedAdd, bool processed, bool skipped, bool failed)
        {
            lock (bulkZstdStatsLock)
            {
                // Only phase labels (Scanning… / Completed / Cancelled) — no per-texture names in UI.
                if (!string.IsNullOrEmpty(currentFile)
                    && (currentFile == "Scanning..."
                        || currentFile == "Completed"
                        || currentFile == "Cancelled"
                        || currentFile == "Restored"))
                {
                    CurrentZstdStats.CurrentFile = currentFile;
                }
                if (originalAdd > 0) CurrentZstdStats.TotalOriginalSize += originalAdd;
                if (compressedAdd > 0) CurrentZstdStats.TotalCompressedSize += compressedAdd;
                if (processed) CurrentZstdStats.ProcessedFiles++;
                if (skipped) CurrentZstdStats.SkippedCount++;
                if (failed) CurrentZstdStats.FailedCount++;
            }
        }

        private void BulkZstdProcessWorkItem(BulkZstdWorkItem item, int compressionLevel, ref int memSampleCounter)
        {
            if (item == null) return;
            if (CurrentZstdStats.CancelRequested) return;

            try
            {
                if (item.Kind == BulkZstdWorkKind.NativeToZstd)
                {
                    long originalSize = new FileInfo(item.SourcePath).Length;
                    int sample = System.Threading.Interlocked.Increment(ref memSampleCounter);
                    if ((sample - 1) % 10 == 0)
                        LogBulkZstdMemSample(sample - 1, originalSize, false);

                    BulkZstdCompressNativeToZstd(item.SourcePath, item.TargetPath, compressionLevel, item.MetaJson);

                    long compressed = 0;
                    if (File.Exists(item.TargetPath))
                        compressed = new FileInfo(item.TargetPath).Length;
                    BulkZstdNoteProgress(null, originalSize, compressed, true, false, false);

                    if (item.DeleteNativeAfter)
                    {
                        try { File.Delete(item.SourcePath); } catch { }
                        try { if (!string.IsNullOrEmpty(item.MetaPath)) File.Delete(item.MetaPath); } catch { }
                    }
                }
                else
                {
                    long originalSize = new FileInfo(item.SourcePath).Length;
                    int sample = System.Threading.Interlocked.Increment(ref memSampleCounter);
                    if ((sample - 1) % 10 == 0)
                        LogBulkZstdMemSample(sample - 1, originalSize, true);

                    BulkZstdRecompressZstdInPlace(item.SourcePath, compressionLevel, item.MetaJson);

                    long compressed = 0;
                    if (File.Exists(item.SourcePath))
                        compressed = new FileInfo(item.SourcePath).Length;
                    BulkZstdNoteProgress(null, originalSize, compressed, true, false, false);
                }
            }
            catch (Exception ex)
            {
                BulkZstdNoteProgress(null, 0, 0, true, false, true);
                LogUtil.LogError("Bulk compression: Failed " + item.SourcePath + ": " + ex.Message);
            }
        }

        private void BulkZstdRunParallel(List<BulkZstdWorkItem> work, int compressionLevel)
        {
            if (work == null || work.Count == 0) return;

            int parallel = ResolveBulkZstdParallelism();
            if (parallel > work.Count) parallel = work.Count;
            TryBoostThreadPoolForBulkZstd(parallel);

            var queue = new Queue<BulkZstdWorkItem>(work.Count);
            for (int i = 0; i < work.Count; i++)
            {
                if (work[i] != null) queue.Enqueue(work[i]);
            }

            int memSampleCounter = 0;
            int remainingWorkers = parallel;
            var done = new ManualResetEvent(false);
            var queueLock = new object();

            for (int w = 0; w < parallel; w++)
            {
                int workerIndex = w;
                var worker = new Thread(() =>
                {
                    try
                    {
                        while (!CurrentZstdStats.CancelRequested)
                        {
                            BulkZstdWorkItem item = null;
                            lock (queueLock)
                            {
                                if (queue.Count == 0) break;
                                item = queue.Dequeue();
                            }
                            BulkZstdProcessWorkItem(item, compressionLevel, ref memSampleCounter);
                        }
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref remainingWorkers) == 0)
                        {
                            try { done.Set(); } catch { }
                        }
                    }
                });
                worker.IsBackground = true;
                worker.Name = "VPB-BulkZstd-" + workerIndex;
                worker.Start();
            }

            done.WaitOne();
            done.Close();
        }

        private void BulkZstdWorker(string nativeCacheDir, string vpbCacheDir)
        {
            string[] files;
            try
            {
                string sig = "0";
                try { sig = Directory.GetLastWriteTimeUtc(nativeCacheDir).ToBinary().ToString(); } catch { sig = "0"; }
                string cacheKey = "cache:list|dir=" + (Path.GetFullPath(nativeCacheDir).Replace('\\', '/').TrimEnd('/')) + "|pat=*.vamcache";
                var cached = new List<VpbLocalDatabase.SystemFileRow>();
                if (VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey, sig, cached) && cached.Count > 0)
                {
                    files = new string[cached.Count];
                    for (int i = 0; i < cached.Count; i++) files[i] = cached[i].Path;
                }
                else
                {
                    files = Directory.GetFiles(nativeCacheDir, "*.vamcache", SearchOption.TopDirectoryOnly);
                    try
                    {
                        var rows = new List<VpbLocalDatabase.SystemFileRow>(files.Length);
                        for (int i = 0; i < files.Length; i++)
                        {
                            string p = files[i];
                            if (string.IsNullOrEmpty(p)) continue;
                            var r = new VpbLocalDatabase.SystemFileRow();
                            try { r.Path = Path.GetFullPath(p); } catch { r.Path = p; }
                            r.LastWriteBinaryOrInvalid = long.MinValue;
                            r.SizeOrInvalid = long.MinValue;
                            rows.Add(r);
                        }
                        if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey, sig, rows);
                    }
                    catch { }
                }
            }
            catch
            {
                files = Directory.GetFiles(nativeCacheDir, "*.vamcache", SearchOption.TopDirectoryOnly);
            }
            int compressionLevel = Settings.Instance.ZstdCompressionLevel.Value;
            bool deleteOriginal = Settings.Instance.DeleteOriginalCacheAfterCompression.Value;
            int threshold = Settings.Instance.ThumbnailThreshold.Value;

            string[] zstdFiles = new string[0];
            try
            {
                if (Directory.Exists(vpbCacheDir))
                {
                    zstdFiles = Directory.GetFiles(vpbCacheDir, "*.zvamcache", SearchOption.TopDirectoryOnly);
                }
            }
            catch { zstdFiles = new string[0]; }

            var touchedZstdTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var work = new List<BulkZstdWorkItem>(256);
            CurrentZstdStats.CurrentFile = "Scanning...";
            foreach (var f in files)
            {
                try
                {
                    if (!File.Exists(f + "meta")) continue;
                    var mj = JSON.Parse(File.ReadAllText(f + "meta"));
                    if (BulkZstdIsThumbnail(mj, threshold)) continue;
                    string cacheFileBase = Path.GetFileNameWithoutExtension(Path.GetFileName(f));
                    string targetPath = TextureUtil.ResolveBulkZstdTargetPath(vpbCacheDir, cacheFileBase, out _);
                    if (string.IsNullOrEmpty(targetPath)) continue;
                    touchedZstdTargets.Add(targetPath);
                    if (!BulkZstdNeedsCompression(f, targetPath, compressionLevel))
                    {
                        CurrentZstdStats.SkippedCount++;
                        if (deleteOriginal)
                        {
                            try { File.Delete(f); } catch { }
                            try { File.Delete(f + "meta"); } catch { }
                        }
                        continue;
                    }
                    work.Add(new BulkZstdWorkItem
                    {
                        Kind = BulkZstdWorkKind.NativeToZstd,
                        SourcePath = f,
                        TargetPath = targetPath,
                        MetaPath = f + "meta",
                        DisplayName = Path.GetFileName(f),
                        DeleteNativeAfter = deleteOriginal,
                        MetaJson = mj
                    });
                }
                catch { }
            }
            for (int zi = 0; zi < zstdFiles.Length; zi++)
            {
                string zf = zstdFiles[zi];
                if (string.IsNullOrEmpty(zf)) continue;
                try
                {
                    if (!File.Exists(zf + "meta")) continue;
                    var mj = JSON.Parse(File.ReadAllText(zf + "meta"));
                    if (BulkZstdIsThumbnail(mj, threshold)) continue;
                    if (touchedZstdTargets.Contains(zf)) continue;
                    string cacheFileBase = Path.GetFileNameWithoutExtension(Path.GetFileName(zf));
                    string nativeTarget = TextureUtil.ResolveBulkZstdTargetPath(vpbCacheDir, cacheFileBase, out _);
                    if (!string.IsNullOrEmpty(nativeTarget))
                    {
                        string nativePath = Path.Combine(nativeCacheDir, cacheFileBase + ".vamcache");
                        if (File.Exists(nativePath)) continue;
                    }
                    if (!BulkZstdNeedsCompression(zf, zf, compressionLevel))
                    {
                        CurrentZstdStats.SkippedCount++;
                        continue;
                    }
                    work.Add(new BulkZstdWorkItem
                    {
                        Kind = BulkZstdWorkKind.RecompressZstd,
                        SourcePath = zf,
                        TargetPath = zf,
                        MetaPath = zf + "meta",
                        DisplayName = Path.GetFileName(zf),
                        DeleteNativeAfter = false,
                        MetaJson = mj
                    });
                }
                catch { }
            }

            CurrentZstdStats.TotalFiles = work.Count;
            CurrentZstdStats.CurrentFile = null;
            if (work.Count == 0)
            {
                LogUtil.Log("[VPB] Bulk compress: nothing to compress (native=" + files.Length + " zstd=" + zstdFiles.Length + ") in " + nativeCacheDir);
            }
            else
            {
                LogUtil.Log("[VPB] Bulk compress: " + work.Count + " files, parallel=" + ResolveBulkZstdParallelism());
                BulkZstdRunParallel(work, compressionLevel);
                if (CurrentZstdStats.CancelRequested)
                    CurrentZstdStats.CurrentFile = "Cancelled";
            }

            CurrentZstdStats.Duration = (float)(DateTime.Now - CurrentZstdStats.StartTime).TotalSeconds;
            CurrentZstdStats.IsRunning = false;
            CurrentZstdStats.Completed = true;
            if (!CurrentZstdStats.CancelRequested)
                CurrentZstdStats.CurrentFile = "Completed";
            LogUtil.Log(string.Format(
                "Bulk compression completed: {0} compressed, {1} already up to date, {2} failed",
                CurrentZstdStats.ProcessedFiles, CurrentZstdStats.SkippedCount, CurrentZstdStats.FailedCount));
        }

        public void StartBulkZstdDecompression()
        {
            if (CurrentZstdStats.IsRunning) return;

            string nativeCacheDir = TextureUtil.ResolveTextureCacheDirFullPath();
            string vpbCacheDir = VamHookPlugin.GetCacheDir();

            if (!Directory.Exists(vpbCacheDir))
            {
                LogUtil.LogError("[VPB] VPB cache directory not found: " + vpbCacheDir);
                return;
            }

            LogUtil.Log("Starting bulk Zstd decompression from " + vpbCacheDir + " to " + nativeCacheDir);
            CurrentZstdStats = new ZstdStats { IsRunning = true, IsDecompression = true, StartTime = DateTime.Now };
            try { VpbProgressService.BeginBulkZstd(decompress: true); } catch { }

            var t = new Thread(() =>
            {
                try
                {
                    BulkZstdDecompressWorker(nativeCacheDir, vpbCacheDir);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Bulk Zstd decompression failed: " + ex.ToString());
                    CurrentZstdStats.IsRunning = false;
                    try { VpbProgressService.EndBulkZstd(); } catch { }
                }
            });
            t.IsBackground = true;
            t.Name = "VPB-BulkZstd-Decompress";
            t.Start();
        }

        private void BulkZstdDecompressWorker(string nativeCacheDir, string vpbCacheDir)
        {
            string[] files;
            try
            {
                string sig = "0";
                try { sig = Directory.GetLastWriteTimeUtc(vpbCacheDir).ToBinary().ToString(); } catch { sig = "0"; }
                string cacheKey = "cache:list|dir=" + (Path.GetFullPath(vpbCacheDir).Replace('\\', '/').TrimEnd('/')) + "|pat=*.zvamcache";
                var cached = new List<VpbLocalDatabase.SystemFileRow>();
                if (VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey, sig, cached) && cached.Count > 0)
                {
                    files = new string[cached.Count];
                    for (int i = 0; i < cached.Count; i++) files[i] = cached[i].Path;
                }
                else
                {
                    files = Directory.GetFiles(vpbCacheDir, "*.zvamcache", SearchOption.TopDirectoryOnly);
                    try
                    {
                        var rows = new List<VpbLocalDatabase.SystemFileRow>(files.Length);
                        for (int i = 0; i < files.Length; i++)
                        {
                            string p = files[i];
                            if (string.IsNullOrEmpty(p)) continue;
                            var r = new VpbLocalDatabase.SystemFileRow();
                            try { r.Path = Path.GetFullPath(p); } catch { r.Path = p; }
                            r.LastWriteBinaryOrInvalid = long.MinValue;
                            r.SizeOrInvalid = long.MinValue;
                            rows.Add(r);
                        }
                        if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey, sig, rows);
                    }
                    catch { }
                }
            }
            catch
            {
                files = Directory.GetFiles(vpbCacheDir, "*.zvamcache", SearchOption.TopDirectoryOnly);
            }
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
                            File.WriteAllText(targetPath + "meta", VPB.src.util.JsonSerializationUtil.Serialize(metaJson, 1024));
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

        public static void WriteAlphaTextureToZstdCache(ImageLoaderThreaded.QueuedImage qi)
        {
            if (ImageLoadingMgr.singleton == null) return;
            ImageLoadingMgr.singleton.TryEnqueueResizeCache(qi);
        }

        // --- Runtime zstd write-after-load (lazy cache) ---

        private struct CandidateStamp
        {
            internal WeakReference QueuedImage;
            internal int Generation;
        }

        private readonly Dictionary<string, ImageLoaderThreaded.QueuedImage> pathCandidates = new Dictionary<string, ImageLoaderThreaded.QueuedImage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> pathCreateMipMapsHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, ImageLoaderThreaded.QueuedImage> texIdCandidates = new Dictionary<int, ImageLoaderThreaded.QueuedImage>();
        private readonly Dictionary<string, CandidateStamp> candidateStamps = new Dictionary<string, CandidateStamp>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingZstdWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_FallbackPendingZstdWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object s_FallbackPendingZstdLock = new object();
        private readonly object candidateLock = new object();
        private readonly object pendingZstdWriteLock = new object();

        private sealed class PendingRuntimeZstdWrite
        {
            internal Texture2D Texture;
            internal bool Linear;
            internal string ZstdPath;
            internal bool IsSimPath;
            internal bool IsAlphaFromGrayscale;
            internal bool QueueCreateMipMaps;
        }

        private readonly Queue<PendingRuntimeZstdWrite> runtimeZstdWriteQueue = new Queue<PendingRuntimeZstdWrite>();
        private readonly object runtimeZstdWriteQueueLock = new object();
        private const float RuntimeZstdWriteFrameBudgetSec = 0.006f;
        private const int MaxRuntimeZstdWriteQueue = 8;
        private const int MaxRuntimeZstdWritesPerFrame = 1;
        private const int MaxActiveRuntimeZstdWrites = 1;
        private const int MaxRuntimeZstdWritePayloadBytes = 512 * 1024 * 1024;
        private int activeRuntimeZstdWrites;

        /// <summary>
        /// Spread runtime zstd cache writes across frames. VaM native Finish already writes .vamcache
        /// synchronously; VPB zstd writes were duplicated in PostFinish and caused periodic hitches
        /// (soft-body physics jitter when frame time spikes).
        /// </summary>
        public void DrainPendingRuntimeZstdWrites()
        {
            if (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive()) return;
            if (Thread.VolatileRead(ref activeRuntimeZstdWrites) >= MaxActiveRuntimeZstdWrites) return;

            float deadline = Time.realtimeSinceStartup + RuntimeZstdWriteFrameBudgetSec;
            int processed = 0;
            while (processed < MaxRuntimeZstdWritesPerFrame && Time.realtimeSinceStartup < deadline)
            {
                PendingRuntimeZstdWrite item;
                lock (runtimeZstdWriteQueueLock)
                {
                    if (runtimeZstdWriteQueue.Count == 0) return;
                    item = runtimeZstdWriteQueue.Dequeue();
                }

                ProcessPendingRuntimeZstdWrite(item);
                processed++;
            }
        }

        private void EnqueueRuntimeZstdWrite(
            ImageLoaderThreaded.QueuedImage qi,
            string zstdPath,
            bool isSimPath,
            bool isAlphaFromGrayscale,
            bool queueCreateMipMaps)
        {
            if (qi == null) return;

            lock (runtimeZstdWriteQueueLock)
            {
                while (runtimeZstdWriteQueue.Count >= MaxRuntimeZstdWriteQueue)
                {
                    var dropped = runtimeZstdWriteQueue.Dequeue();
                    ReleasePendingZstdWriteSlot(dropped != null ? dropped.ZstdPath : null);
                }

                runtimeZstdWriteQueue.Enqueue(new PendingRuntimeZstdWrite
                {
                    Texture = qi.tex,
                    Linear = qi.linear,
                    ZstdPath = zstdPath,
                    IsSimPath = isSimPath,
                    IsAlphaFromGrayscale = isAlphaFromGrayscale,
                    QueueCreateMipMaps = queueCreateMipMaps
                });
            }
        }

        private static void ReleasePendingZstdWriteSlot(string zstdPath)
        {
            if (string.IsNullOrEmpty(zstdPath) || ImageLoadingMgr.singleton == null) return;
            lock (ImageLoadingMgr.singleton.pendingZstdWriteLock)
            {
                ImageLoadingMgr.singleton.pendingZstdWrites.Remove(zstdPath);
            }
        }

        private void ProcessPendingRuntimeZstdWrite(PendingRuntimeZstdWrite item)
        {
            if (item == null) return;

            Texture2D texture = item.Texture;
            string zstdPath = item.ZstdPath;
            bool queuedToWorker = false;
            try
            {
                if (texture == null) return;

                if (item.IsAlphaFromGrayscale)
                {
                    if (string.IsNullOrEmpty(zstdPath)) return;
                    if (File.Exists(zstdPath) && File.Exists(zstdPath + "meta")) return;

                    byte[] raw;
                    TextureFormat format;
                    int w, h;
                    if (!TryCaptureAlphaTextureBytesForZstdWrite(texture, item.Linear, out raw, out format, out w, out h)) return;
                    if (raw == null || raw.Length == 0 || raw.Length > MaxRuntimeZstdWritePayloadBytes) return;

                    int level = 3;
                    try { if (Settings.Instance != null) level = Settings.Instance.ZstdCompressionLevel.Value; } catch { }
                    string pathCopy = zstdPath;
                    Interlocked.Increment(ref activeRuntimeZstdWrites);
                    try
                    {
                        queuedToWorker = ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try { WriteCapturedBytesToZstdCache(pathCopy, raw, w, h, format, false, false, level, AlphaCacheVersion); }
                            finally { Interlocked.Decrement(ref activeRuntimeZstdWrites); }
                        });
                    }
                    catch { queuedToWorker = false; }
                    if (!queuedToWorker) Interlocked.Decrement(ref activeRuntimeZstdWrites);
                    return;
                }

                if (string.IsNullOrEmpty(zstdPath)) return;
                if (File.Exists(zstdPath) && File.Exists(zstdPath + "meta")) return;

                byte[] rawBytes;
                TextureFormat fmt;
                int width, height;
                if (!TryGetTextureBytesForZstdWrite(texture, item.Linear, item.IsSimPath, out rawBytes, out fmt, out width, out height))
                    return;
                if (rawBytes == null || rawBytes.Length == 0 || rawBytes.Length > MaxRuntimeZstdWritePayloadBytes) return;

                int zlevel = 3;
                try { if (Settings.Instance != null) zlevel = Settings.Instance.ZstdCompressionLevel.Value; } catch { }
                bool isSim = item.IsSimPath;
                bool queueMip = item.QueueCreateMipMaps;
                string pathForWorker = zstdPath;
                Interlocked.Increment(ref activeRuntimeZstdWrites);
                try
                {
                    queuedToWorker = ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { WriteCapturedBytesToZstdCache(pathForWorker, rawBytes, width, height, fmt, isSim, queueMip, zlevel, 0); }
                        finally { Interlocked.Decrement(ref activeRuntimeZstdWrites); }
                    });
                }
                catch { queuedToWorker = false; }
                if (!queuedToWorker) Interlocked.Decrement(ref activeRuntimeZstdWrites);
            }
            finally
            {
                if (!queuedToWorker)
                    ReleasePendingZstdWriteSlot(zstdPath);
            }
        }

        private static bool TryCaptureAlphaTextureBytesForZstdWrite(
            Texture2D src,
            bool linear,
            out byte[] raw,
            out TextureFormat format,
            out int w,
            out int h)
        {
            raw = null;
            format = TextureFormat.RGBA32;
            w = 0;
            h = 0;
            if (src == null) return false;

            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
            Texture2D rgba = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                rgba = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, linear);
                rgba.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
                rgba.Apply(false, false);
                w = src.width;
                h = src.height;
                raw = rgba.GetRawTextureData();
                return raw != null && raw.Length > 0;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (rgba != null) UnityEngine.Object.Destroy(rgba);
            }
        }

        private static void WriteCapturedBytesToZstdCache(
            string zstdPath,
            byte[] raw,
            int w,
            int h,
            TextureFormat format,
            bool isSimPath,
            bool queueCreateMipMaps,
            int zstdLevel,
            int alphaCacheVersion)
        {
            if (string.IsNullOrEmpty(zstdPath) || raw == null || raw.Length == 0 || w <= 0 || h <= 0) return;

            try
            {
                byte[] compressed = ZstdCompressor.Compress(raw, zstdLevel);
                if (compressed == null || compressed.Length == 0) return;

                string zdir = Path.GetDirectoryName(zstdPath);
                if (!string.IsNullOrEmpty(zdir) && !Directory.Exists(zdir)) Directory.CreateDirectory(zdir);

                string dataTmp = zstdPath + ".tmp";
                string metaTmp = zstdPath + ".meta.tmp";
                File.WriteAllBytes(dataTmp, compressed);

                var zmeta = new SimpleJSON.JSONClass();
                zmeta["type"] = "compressed";
                zmeta["width"] = w.ToString();
                zmeta["height"] = h.ToString();
                zmeta["format"] = format.ToString();
                if (isSimPath) zmeta["isReadable"] = "true";
                if (alphaCacheVersion > 0) zmeta["vpbVer"].AsInt = alphaCacheVersion;
                zmeta["zstdLevel"].AsInt = zstdLevel;
                TextureUtil.WriteMipFieldsToMeta(zmeta, w, h, format, raw.Length, queueCreateMipMaps);
                File.WriteAllText(metaTmp, VPB.src.util.JsonSerializationUtil.Serialize(zmeta, 1024));

                try { if (File.Exists(zstdPath + "meta")) File.Delete(zstdPath + "meta"); } catch { }
                try { if (File.Exists(zstdPath)) File.Delete(zstdPath); } catch { }

                File.Move(metaTmp, zstdPath + "meta");
                File.Move(dataTmp, zstdPath);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB ZSTD WRITE] Failed for " + zstdPath + ": " + ex.Message);
            }
            finally
            {
                ReleasePendingZstdWriteSlot(zstdPath);
            }
        }

        public void ClearCandidates()
        {
            lock (candidateLock)
            {
                pathCandidates.Clear();
                texIdCandidates.Clear();
                pathCreateMipMapsHints.Clear();
                candidateStamps.Clear();
            }
        }

        /// <summary>Full replacements release VPB texture refs before VaM's unload pass; merges keep refs still used by live scene.</summary>
        public void PrepareForSceneLoad(bool loadMerge)
        {
            CancelPendingTextureLoads();

            if (!loadMerge)
            {
                ClearTextureCacheReferences();
                lock (decompressedCacheLock)
                {
                    decompressedCache.Clear();
                    lruOrder.Clear();
                    currentMemoryUsage = 0;
                }
            }
            ClearCandidates();
            lock (runtimeZstdWriteQueueLock)
            {
                while (runtimeZstdWriteQueue.Count > 0)
                {
                    var dropped = runtimeZstdWriteQueue.Dequeue();
                    ReleasePendingZstdWriteSlot(dropped != null ? dropped.ZstdPath : null);
                }
            }
        }

        public void PrepareForAppearanceLoad()
        {
            CancelPendingTextureLoads();
            ClearCandidates();
        }

        internal void GetMemoryTelemetry(
            out int textureCacheCount,
            out long textureCacheBytes,
            out int decompressedCacheCount,
            out long decompressedCacheBytes,
            out int pendingCreateCount,
            out long pendingCreateBytes,
            out int inflightCount,
            out int activePayloadWorkersCount,
            out int pendingWritePathCount,
            out int runtimeWriteQueueCount,
            out int runtimeWriteActiveCount)
        {
            lock (textureCacheLock)
            {
                textureCacheCount = textureCache.Count;
                textureCacheBytes = currentTextureCacheBytes;
            }
            lock (decompressedCacheLock)
            {
                decompressedCacheCount = decompressedCache.Count;
                decompressedCacheBytes = currentMemoryUsage;
            }
            lock (pendingMainThreadLock)
            {
                pendingCreateCount = pendingMainThreadTextureCreateCount;
                pendingCreateBytes = pendingMainThreadTextureCreateBytes;
            }
            lock (inflightLock)
            {
                inflightCount = inflightKeys.Count;
            }
            activePayloadWorkersCount = Thread.VolatileRead(ref activeTexturePayloadWorkers);
            lock (pendingZstdWriteLock)
            {
                pendingWritePathCount = pendingZstdWrites.Count;
            }
            lock (runtimeZstdWriteQueueLock)
            {
                runtimeWriteQueueCount = runtimeZstdWriteQueue.Count;
            }
            runtimeWriteActiveCount = Thread.VolatileRead(ref activeRuntimeZstdWrites);
        }

        public void TrackCandidate(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            lock (candidateLock)
            {
                pathCandidates[qi.imgPath] = qi;
                pathCreateMipMapsHints[qi.imgPath] = qi.createMipMaps;
                if (qi.tex != null) texIdCandidates[qi.tex.GetInstanceID()] = qi;
                candidateStamps[GetTextureCacheKey(qi)] = new CandidateStamp { QueuedImage = new WeakReference(qi), Generation = CurrentLoadGeneration };
            }
        }

        private bool TryGetCurrentCandidateGeneration(ImageLoaderThreaded.QueuedImage qi, out int generation)
        {
            generation = -1;
            if (qi == null) return false;
            lock (candidateLock)
            {
                CandidateStamp stamp;
                if (!candidateStamps.TryGetValue(GetTextureCacheKey(qi), out stamp)
                    || stamp.QueuedImage == null
                    || !object.ReferenceEquals(stamp.QueuedImage.Target, qi)) return false;
                generation = stamp.Generation;
            }
            return IsLoadGenerationCurrent(generation);
        }

        public void ReleaseCandidate(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath)) return;
            lock (candidateLock)
            {
                ImageLoaderThreaded.QueuedImage current;
                if (pathCandidates.TryGetValue(qi.imgPath, out current) && object.ReferenceEquals(current, qi))
                {
                    pathCandidates.Remove(qi.imgPath);
                    pathCreateMipMapsHints.Remove(qi.imgPath);
                }

                if (qi.tex != null)
                {
                    int textureId = qi.tex.GetInstanceID();
                    if (texIdCandidates.TryGetValue(textureId, out current) && object.ReferenceEquals(current, qi))
                        texIdCandidates.Remove(textureId);
                }
                string cacheKey = GetTextureCacheKey(qi);
                CandidateStamp stamp;
                if (candidateStamps.TryGetValue(cacheKey, out stamp) && stamp.QueuedImage != null
                    && object.ReferenceEquals(stamp.QueuedImage.Target, qi))
                    candidateStamps.Remove(cacheKey);
            }
        }

        public ImageLoaderThreaded.QueuedImage FindCandidateByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (candidateLock)
            {
                ImageLoaderThreaded.QueuedImage qi;
                return pathCandidates.TryGetValue(path, out qi) ? qi : null;
            }
        }

        public ImageLoaderThreaded.QueuedImage FindCandidateByTexture(Texture2D tex)
        {
            if (tex == null) return null;
            lock (candidateLock)
            {
                ImageLoaderThreaded.QueuedImage qi;
                return texIdCandidates.TryGetValue(tex.GetInstanceID(), out qi) ? qi : null;
            }
        }

        public bool TryEnqueueResizeCacheForCurrentCandidate(ImageLoaderThreaded.QueuedImage qi)
        {
            int generation;
            if (!TryGetCurrentCandidateGeneration(qi, out generation)) return false;
            if (!IsLoadGenerationCurrent(generation)) return false;
            return TryEnqueueResizeCache(qi);
        }

        public bool TryEnqueueResizeCache(ImageLoaderThreaded.QueuedImage qi)
        {
            if (!ShouldWriteRuntimeZstdCache(qi)) return false;

            if (qi.createAlphaFromGrayscale)
            {
                string alphaPath = TextureUtil.BuildExactZstdCachePath(
                    qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                    0, 0, qi.bumpStrength, false);
                if (string.IsNullOrEmpty(alphaPath)) return false;
                lock (pendingZstdWriteLock)
                {
                    if (pendingZstdWrites.Contains(alphaPath)) return true;
                    pendingZstdWrites.Add(alphaPath);
                }
                EnqueueRuntimeZstdWrite(qi, alphaPath, false, true, false);
                return true;
            }

            bool isSimPath = SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
            string zstdPath = TextureUtil.BuildExactZstdCachePath(
                qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength, isSimPath);
            if (string.IsNullOrEmpty(zstdPath)) return false;
            if (File.Exists(zstdPath) && File.Exists(zstdPath + "meta"))
            {
                bool queueMip = ResolveQueueCreateMipMaps(qi);
                ThreadPool.QueueUserWorkItem(_ => TryUpgradeExistingZstdMeta(zstdPath, queueMip));
                return false;
            }

            lock (pendingZstdWriteLock)
            {
                if (pendingZstdWrites.Contains(zstdPath)) return true;
                pendingZstdWrites.Add(zstdPath);
            }

            EnqueueRuntimeZstdWrite(qi, zstdPath, isSimPath, false, ResolveQueueCreateMipMaps(qi));
            return true;
        }

        private static bool ShouldWriteRuntimeZstdCache(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || qi.tex == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return false;
            if (Settings.Instance == null || Settings.Instance.EnableZstdCompression == null || !Settings.Instance.EnableZstdCompression.Value) return false;

            try
            {
                int threshold = Settings.Instance.ThumbnailThreshold != null ? Settings.Instance.ThumbnailThreshold.Value : 256;
                if (qi.isThumbnail && qi.setSize && qi.width > 0 && qi.width <= threshold && qi.height > 0 && qi.height <= threshold)
                    return false;
            }
            catch { }

            return true;
        }

        private static bool IsTextureReadableForCacheWrite(Texture2D tex)
        {
            if (tex == null) return false;
            try
            {
                tex.GetPixel(0, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Match native ImageLoaderThreaded: character skin stays CPU-readable so
        /// DAZCharacterTextureControl.BlendGenitalTexture GetPixels can run after torso load.
        /// </summary>
        private static bool ShouldForceCpuReadable(ImageLoaderThreaded.QueuedImage qi, bool metaIsReadable)
        {
            if (metaIsReadable) return true;
            return SuperControllerHook.NeedsCpuReadableTexture(qi);
        }

        private static bool TextureMeetsCpuReadableRequirement(ImageLoaderThreaded.QueuedImage qi, Texture2D tex)
        {
            if (tex == null) return false;
            if (!SuperControllerHook.NeedsCpuReadableTexture(qi)) return true;
            return IsTextureReadableForCacheWrite(tex);
        }

        private static bool TryGetTextureBytesForZstdWrite(Texture2D src, bool linear, bool forceReadable, out byte[] raw, out TextureFormat format, out int w, out int h)
        {
            raw = null;
            format = src.format;
            w = src.width;
            h = src.height;

            if (forceReadable || IsTextureReadableForCacheWrite(src))
            {
                try
                {
                    raw = src.GetRawTextureData();
                    return raw != null && raw.Length > 0;
                }
                catch { }
            }

            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
            Texture2D rgba = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                rgba = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
                rgba.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                rgba.Apply(false, false);
                raw = rgba.GetRawTextureData();
                format = TextureFormat.RGBA32;
                return raw != null && raw.Length > 0;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (rgba != null) UnityEngine.Object.Destroy(rgba);
            }
        }
    }
}
