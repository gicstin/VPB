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
        private Coroutine mainThreadTextureCreatePump;

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

        Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();
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
            if (string.IsNullOrEmpty(zstdPath)) return false;

            lock (pendingZstdWriteLock)
            {
                if (pendingZstdWrites.Contains(zstdPath)) return true;
            }

            lock (runtimeZstdWriteQueueLock)
            {
                foreach (var item in runtimeZstdWriteQueue)
                {
                    if (item != null && string.Equals(item.ZstdPath, zstdPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            if (File.Exists(zstdPath + ".tmp")) return true;
            if (File.Exists(zstdPath + ".meta.tmp")) return true;
            return false;
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

        private bool BeginAsyncMemoryServe(string cacheKey, ImageLoaderThreaded.QueuedImage qi, DiskCachePayload payload)
        {
            if (qi == null || payload == null || payload.Data == null || payload.Meta == null) return false;

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
                OriginalQI = qi
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

                    bool isSimTexture = payload.Meta.IsReadable || SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
                    Texture2D tex = qi.tex;
                    if (tex == null)
                    {
                        tex = TextureUtil.CreateTextureFromCachedRaw(payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                            payload.Meta.CreateMipMaps, qi.linear, !isSimTexture, isSimTexture);
                        if (tex == null) return false;
                    }
                    else if (!TextureUtil.ApplyCachedRawToTexture(tex, payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                        payload.Meta.CreateMipMaps, qi.linear, !isSimTexture, isSimTexture))
                    {
                        return false;
                    }

                    qi.tex = tex;
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
            if (cand != null && cand.tex != null && GetTextureCacheKey(cand) == cacheKey)
            {
                qi.tex = cand.tex;
                RegisterTexture(cacheKey, cand.tex);
                return true;
            }

            return false;
        }

        private bool TryServeFromMemoryCache(ImageLoaderThreaded.QueuedImage qi, string cacheKey, bool allowCandidateTexture)
        {
            if (qi == null || string.IsNullOrEmpty(cacheKey)) return false;

            string zstdPath = ResolveZstdServePath(qi);
            bool queueMip = ResolveQueueCreateMipMaps(qi);
            DiskCachePayload payload;
            if (!string.IsNullOrEmpty(zstdPath) && TryGetZstdPayloadFromMemoryOnly(zstdPath, queueMip, out payload))
                return BeginAsyncMemoryServe(cacheKey, qi, payload);

            if (allowCandidateTexture)
            {
                ImageLoaderThreaded.QueuedImage cand = FindCandidateByPath(qi.imgPath);
                if (cand != null && cand.tex != null && GetTextureCacheKey(cand) == cacheKey)
                {
                    qi.tex = cand.tex;
                    RegisterTexture(cacheKey, cand.tex);
                    if (Messager.singleton != null)
                        Messager.singleton.StartCoroutine(DelayDoCallback(qi));
                    else
                        DoCallback(qi);
                    return true;
                }
            }

            return false;
        }

        private void ScheduleRetryDiskCacheLoad(string cacheKey, string cachePath, ImageLoaderThreaded.QueuedImage qi)
        {
            if (Messager.singleton != null)
                Messager.singleton.StartCoroutine(RetryDiskCacheLoadCoroutine(cacheKey, cachePath, qi));
            else
                ThreadPool.QueueUserWorkItem(_ => LoadAndDecompressBackground(cacheKey, cachePath, qi));
        }

        private IEnumerator RetryDiskCacheLoadCoroutine(string cacheKey, string cachePath, ImageLoaderThreaded.QueuedImage qi)
        {
            for (int i = 0; i < 12 && IsZstdCachePathBusy(cachePath); i++)
                yield return null;

            if (TryServeFromMemoryCache(qi, cacheKey, true))
                yield break;

            lock (inflightLock)
            {
                if (inflightKeys.Contains(cacheKey)) yield break;
                inflightKeys.Add(cacheKey);
            }
            LoadAndDecompressBackground(cacheKey, cachePath, qi);
        }

        private void HandleBusyOrTransientDiskCacheFailure(string cacheKey, string diskPath, ImageLoaderThreaded.QueuedImage qi, DiskCacheFailureKind kind)
        {
            lock (inflightLock)
            {
                inflightKeys.Remove(cacheKey);
            }

            if (TryServeFromMemoryCache(qi, cacheKey, true))
                return;

            InvalidateMetadataCacheForDiskPath(diskPath);

            if (kind == DiskCacheFailureKind.VpbZstd && IsZstdCachePathBusy(diskPath))
            {
                ScheduleRetryDiskCacheLoad(cacheKey, diskPath, qi);
                return;
            }

            FailDiskCacheLoadAndFallback(cacheKey, diskPath, qi, kind);
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

        private void FailDiskCacheLoadAndFallback(string cacheKey, string diskPath, ImageLoaderThreaded.QueuedImage qi, DiskCacheFailureKind kind)
        {
            if (kind == DiskCacheFailureKind.VaMNative)
                TryDeleteCorruptNativeCache(diskPath);

            InvalidateCachePathMapForDiskPath(diskPath);
            InvalidateMetadataCacheForDiskPath(diskPath);

            var targets = new List<ImageLoaderThreaded.QueuedImage>();
            CollectInflightTargets(cacheKey, qi, targets);
            for (int i = 0; i < targets.Count; i++)
                ScheduleVaMLoadFallback(targets[i], kind);
        }

        private void ScheduleVaMLoadFallback(ImageLoaderThreaded.QueuedImage qi, DiskCacheFailureKind failedKind)
        {
            if (qi == null) return;
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
                    ThreadPool.QueueUserWorkItem(_ => LoadVaMNativeDiskCacheBackground(cacheKey, nativeDiskPath, qi));
                    return;
                }
            }

            EnqueueVaMImageLoadOnMainThread(qi);
        }

        private void EnqueueVaMImageLoadOnMainThread(ImageLoaderThreaded.QueuedImage qi)
        {
            if (Messager.singleton != null)
                Messager.singleton.StartCoroutine(RequeueVaMImageLoadCoroutine(qi));
            else
                SuperControllerHook.RequeueVaMImageLoad(qi);
        }

        private IEnumerator RequeueVaMImageLoadCoroutine(ImageLoaderThreaded.QueuedImage qi)
        {
            while (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive())
                yield return null;
            yield return waitForEndOfFrame;
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

        private void TryUpgradeExistingZstdMeta(ImageLoaderThreaded.QueuedImage qi, string zstdPath, bool queueCreateMipMaps)
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

            bool isSim = payload.Meta.IsReadable || SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
            bool createMipMaps = payload.Meta.CreateMipMaps;
            if (!TextureUtil.ApplyCachedRawToTexture(tex, payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                createMipMaps, qi.linear, markNonReadable, isSim))
            {
                return false;
            }

            if (SuperControllerHook.IsSimulationTexturePath(qi.imgPath))
            {
                LogUtil.Log("[VPB SIM] ImageLoadingMgr applied READABLE sim texture from cache: " + qi.imgPath);
            }

            return true;
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
        IEnumerator DelayDoCallback(ImageLoaderThreaded.QueuedImage qi)
        {
            while (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive())
                yield return null;
            yield return waitForEndOfFrame;
            yield return waitForEndOfFrame;
            DoCallback(qi);
        }

        public bool RequestImmediate(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return false;
            if (Settings.Instance == null || Settings.Instance.ThumbnailThreshold == null) return false;

            int threshold = Settings.Instance.ThumbnailThreshold.Value;
            if (qi.setSize && qi.width > 0 && qi.width <= threshold && qi.height > 0 && qi.height <= threshold)
                return false;

            string cacheKey = GetTextureCacheKey(qi);

            if (qi.tex == null)
            {
                var cached = GetTextureFromCache(cacheKey);
                if (cached != null)
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

                    bool isSimTexture = payload.Meta.IsReadable || SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
                    Texture2D tex = qi.tex;
                    if (tex == null)
                    {
                        tex = TextureUtil.CreateTextureFromCachedRaw(payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                            payload.Meta.CreateMipMaps, qi.linear, !isSimTexture, isSimTexture);
                        if (tex == null) return false;
                    }
                    else if (!TextureUtil.ApplyCachedRawToTexture(tex, payload.Data, payload.Meta.Width, payload.Meta.Height, payload.Meta.Format,
                        payload.Meta.CreateMipMaps, qi.linear, !isSimTexture, isSimTexture))
                    {
                        return false;
                    }

                    qi.tex = tex;
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

            if (Settings.Instance == null || Settings.Instance.ThumbnailThreshold == null) return false;

            int threshold = Settings.Instance.ThumbnailThreshold.Value;
            if (qi.setSize && qi.width > 0 && qi.width <= threshold && qi.height > 0 && qi.height <= threshold)
                return false;

            string cacheKey = GetTextureCacheKey(qi);
            
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

            if (TryServeFromMemoryCache(qi, cacheKey, true))
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
                    ThreadPool.QueueUserWorkItem((state) => LoadAndDecompressBackground(cacheKey, vpbCachePath, qi));
                    return true;
                }

                string nativeDiskPath = TextureUtil.FindVaMNativeDiskCachePath(
                    qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                    qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength);
                if (!string.IsNullOrEmpty(nativeDiskPath))
                {
                    inflightKeys.Add(cacheKey);
                    ThreadPool.QueueUserWorkItem((state) => LoadVaMNativeDiskCacheBackground(cacheKey, nativeDiskPath, qi));
                    return true;
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

        private void LoadVaMNativeDiskCacheBackground(string cacheKey, string nativePath, ImageLoaderThreaded.QueuedImage qi)
        {
            try
            {
                var meta = FastLoadMetadata(nativePath);
                if (meta == null)
                {
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative);
                    return;
                }

                byte[] data = ReadCacheFileBytes(nativePath, MaxServeCachePayloadBytes);
                if (data == null)
                {
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative);
                    return;
                }

                if (data.Length > MaxServeCachePayloadBytes)
                {
                    try
                    {
                        LogUtil.LogWarning("[VPB] Rejecting oversize native cache (" + data.Length + " bytes): " + nativePath);
                    }
                    catch { }
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative);
                    return;
                }

                bool queueMip = ResolveQueueCreateMipMaps(qi);
                if (!IsRawDataValidForMeta(meta, data, queueMip, nativePath))
                {
                    FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative);
                    return;
                }

                var decompressed = new DecompressedData
                {
                    CacheKey = cacheKey,
                    Data = data,
                    Meta = meta,
                    OriginalQI = qi
                };

                ScheduleCreateTextureOnMainThread(decompressed);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("LoadVaMNativeDiskCacheBackground failed for " + nativePath + ": " + ex.Message);
                FailDiskCacheLoadAndFallback(cacheKey, nativePath, qi, DiskCacheFailureKind.VaMNative);
            }
        }

        private void LoadAndDecompressBackground(string cacheKey, string cachePath, ImageLoaderThreaded.QueuedImage qi)
        {
            if (IsZstdPathQuarantined(cachePath))
            {
                HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd);
                return;
            }
            try
            {
                var meta = FastLoadMetadata(cachePath);
                if (meta == null)
                {
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd);
                    return;
                }

                byte[] decompressedData = FastGetDecompressed(cachePath);
                if (decompressedData == null)
                {
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd);
                    return;
                }

                if (meta.Width <= 0 || meta.Height <= 0)
                {
                    if (QuarantineZstdPath(cachePath))
                        LogUtil.LogError($"LoadAndDecompress: Invalid texture dimensions {meta.Width}x{meta.Height} for {cachePath} (quarantined for session)");
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd);
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
                    HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd);
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
                    OriginalQI = qi
                };

                ScheduleCreateTextureOnMainThread(decompressed);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("LoadAndDecompressBackground failed for " + cachePath + ": " + ex.Message);
                HandleBusyOrTransientDiskCacheFailure(cacheKey, cachePath, qi, DiskCacheFailureKind.VpbZstd);
            }
        }

        private void ScheduleCreateTextureOnMainThread(DecompressedData data)
        {
            if (data == null) return;
            if (Messager.singleton == null)
            {
                CreateTexture(data);
                return;
            }

            lock (pendingMainThreadLock)
            {
                pendingMainThreadTextureCreates.Enqueue(data);
            }

            if (mainThreadTextureCreatePump == null)
                mainThreadTextureCreatePump = Messager.singleton.StartCoroutine(PumpCreateTextureOnMainThread());
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
                }

                yield return waitForEndOfFrame;

                while (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive())
                    yield return null;

                CreateTexture(data);
            }
        }

        private void CreateTexture(DecompressedData data)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                string path = data.OriginalQI.imgPath ?? "";
                bool isSimTexturePath = SuperControllerHook.IsSimulationTexturePath(path);
                bool isSimTexture = data.Meta.IsReadable || isSimTexturePath;
                
                if (isSimTexturePath || data.Meta.IsReadable)
                {
                    LogUtil.Log($"[VPB SIM] CreateTexture: path='{path}', isSimPath={isSimTexturePath}, metaIsReadable={data.Meta.IsReadable}, finalIsSim={isSimTexture}");
                }

                if (!IsCacheMetaAndPayloadSane(data.Meta, data.Data))
                {
                    LogUtil.LogWarning("[VPB] CreateTexture rejected insane cache payload for " + path);
                    string diskPath = data.OriginalQI != null ? GetCachePath(data.OriginalQI) : null;
                    if (!string.IsNullOrEmpty(diskPath))
                        HandleBusyOrTransientDiskCacheFailure(data.CacheKey, diskPath, data.OriginalQI, DiskCacheFailureKind.VpbZstd);
                    else
                        EnqueueVaMImageLoadOnMainThread(data.OriginalQI);
                    return;
                }

                bool createMipMaps = data.Meta.CreateMipMaps;
                if (!createMipMaps && data.OriginalQI != null)
                    createMipMaps = ResolveQueueCreateMipMaps(data.OriginalQI);

                Texture2D tex = TextureUtil.CreateTextureFromCachedRaw(data.Data, data.Meta.Width, data.Meta.Height, data.Meta.Format,
                    createMipMaps, data.OriginalQI.linear, !isSimTexture, isSimTexture);
                if (tex == null)
                {
                    EnqueueVaMImageLoadOnMainThread(data.OriginalQI);
                    return;
                }

                if (isSimTexture)
                {
                    LogUtil.Log($"[VPB SIM] Created READABLE sim texture from cache: {path}");
                }

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
                EnqueueVaMImageLoadOnMainThread(data.OriginalQI);
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

                lock (inflightLock)
                {
                    inflightKeys.Remove(data.CacheKey);
                }
            }
        }

        public void ResolveInflightForQueuedImage(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            string cacheKey = GetTextureCacheKey(qi);
            
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
                    try { VpbProgressService.EndBulkZstd(); } catch { }
                }
            });
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
            int workCount = 0;
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
                    if (BulkZstdNeedsCompression(f, targetPath, compressionLevel))
                        workCount++;
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
                    string cacheFileBase = Path.GetFileNameWithoutExtension(Path.GetFileName(zf));
                    string nativeTarget = TextureUtil.ResolveBulkZstdTargetPath(vpbCacheDir, cacheFileBase, out _);
                    if (!string.IsNullOrEmpty(nativeTarget))
                    {
                        string nativePath = Path.Combine(nativeCacheDir, cacheFileBase + ".vamcache");
                        if (File.Exists(nativePath)) continue;
                    }
                    if (BulkZstdNeedsCompression(zf, zf, compressionLevel))
                        workCount++;
                }
                catch { }
            }
            CurrentZstdStats.TotalFiles = workCount;
            CurrentZstdStats.CurrentFile = null;
            if (workCount == 0)
            {
                LogUtil.Log("[VPB] Bulk compress: nothing to compress (native=" + files.Length + " zstd=" + zstdFiles.Length + ") in " + nativeCacheDir);
            }

            // Diagnostic: every N processed files sample heap live-bytes, this file's on-disk size, and GC count.
            // Rising live = leak; flat live but death on a big file + high GC count = Boehm fragmentation; clean sawtooth = peak.
            int zstdMemSampleCounter = 0;
            const int ZstdMemSampleEveryN = 10;

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
                    string metaPath = file + "meta";
                    if (!File.Exists(metaPath)) continue;

                    JSONNode metaJson = null;
                    try
                    {
                        metaJson = JSON.Parse(File.ReadAllText(metaPath));
                        if (BulkZstdIsThumbnail(metaJson, threshold))
                            continue;
                    }
                    catch { continue; }

                    string cacheFileBase = Path.GetFileNameWithoutExtension(fileName);
                    string targetPath = TextureUtil.ResolveBulkZstdTargetPath(vpbCacheDir, cacheFileBase, out _);
                    if (string.IsNullOrEmpty(targetPath))
                        continue;

                    touchedZstdTargets.Add(targetPath);

                    bool needsWork = BulkZstdNeedsCompression(file, targetPath, compressionLevel);
                    if (needsWork)
                    {
                        CurrentZstdStats.CurrentFile = fileName;
                        long originalSize = new FileInfo(file).Length;
                        CurrentZstdStats.TotalOriginalSize += originalSize;
                        if (zstdMemSampleCounter % ZstdMemSampleEveryN == 0)
                            LogBulkZstdMemSample(zstdMemSampleCounter, originalSize, false);
                        zstdMemSampleCounter++;
                        BulkZstdCompressNativeToZstd(file, targetPath, compressionLevel, metaJson);
                        if (File.Exists(targetPath))
                            CurrentZstdStats.TotalCompressedSize += new FileInfo(targetPath).Length;
                        CurrentZstdStats.ProcessedFiles++;
                    }
                    else
                    {
                        CurrentZstdStats.SkippedCount++;
                    }

                    if (deleteOriginal)
                    {
                        File.Delete(file);
                        File.Delete(metaPath);
                    }
                }
                catch (Exception ex)
                {
                    CurrentZstdStats.FailedCount++;
                    LogUtil.LogError("Bulk compression: Failed to convert " + file + ": " + ex.Message);
                    CurrentZstdStats.ProcessedFiles++;
                }
            }

            for (int zi = 0; zi < zstdFiles.Length; zi++)
            {
                if (CurrentZstdStats.CancelRequested)
                {
                    CurrentZstdStats.CurrentFile = "Cancelled";
                    break;
                }

                string zstdPath = zstdFiles[zi];
                if (string.IsNullOrEmpty(zstdPath) || touchedZstdTargets.Contains(zstdPath))
                {
                    continue;
                }

                try
                {
                    string zmetaPath = zstdPath + "meta";
                    if (!File.Exists(zmetaPath)) continue;

                    JSONNode metaJson = null;
                    try
                    {
                        metaJson = JSON.Parse(File.ReadAllText(zmetaPath));
                        if (BulkZstdIsThumbnail(metaJson, threshold))
                            continue;
                    }
                    catch { continue; }

                    bool needsWork = BulkZstdNeedsCompression(zstdPath, zstdPath, compressionLevel);
                    if (needsWork)
                    {
                        CurrentZstdStats.CurrentFile = Path.GetFileName(zstdPath);
                        long originalSize = new FileInfo(zstdPath).Length;
                        CurrentZstdStats.TotalOriginalSize += originalSize;
                        if (zstdMemSampleCounter % ZstdMemSampleEveryN == 0)
                            LogBulkZstdMemSample(zstdMemSampleCounter, originalSize, true);
                        zstdMemSampleCounter++;
                        BulkZstdRecompressZstdInPlace(zstdPath, compressionLevel, metaJson);
                        if (File.Exists(zstdPath))
                            CurrentZstdStats.TotalCompressedSize += new FileInfo(zstdPath).Length;
                        CurrentZstdStats.ProcessedFiles++;
                    }
                    else
                    {
                        CurrentZstdStats.SkippedCount++;
                    }
                }
                catch (Exception ex)
                {
                    CurrentZstdStats.FailedCount++;
                    LogUtil.LogError("Bulk compression: Failed to relevel " + zstdPath + ": " + ex.Message);
                    CurrentZstdStats.ProcessedFiles++;
                }
            }

            CurrentZstdStats.Duration = (float)(DateTime.Now - CurrentZstdStats.StartTime).TotalSeconds;
            CurrentZstdStats.IsRunning = false;
            CurrentZstdStats.Completed = true;
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
                    try { VpbProgressService.EndBulkZstd(); } catch { }
                }
            });
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

        private readonly Dictionary<string, ImageLoaderThreaded.QueuedImage> pathCandidates = new Dictionary<string, ImageLoaderThreaded.QueuedImage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> pathCreateMipMapsHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, ImageLoaderThreaded.QueuedImage> texIdCandidates = new Dictionary<int, ImageLoaderThreaded.QueuedImage>();
        private readonly HashSet<string> pendingZstdWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object candidateLock = new object();
        private readonly object pendingZstdWriteLock = new object();

        private sealed class PendingRuntimeZstdWrite
        {
            internal ImageLoaderThreaded.QueuedImage Qi;
            internal string ZstdPath;
            internal bool IsSimPath;
            internal bool IsAlphaFromGrayscale;
            internal bool QueueCreateMipMaps;
        }

        private readonly Queue<PendingRuntimeZstdWrite> runtimeZstdWriteQueue = new Queue<PendingRuntimeZstdWrite>();
        private readonly object runtimeZstdWriteQueueLock = new object();
        private const float RuntimeZstdWriteFrameBudgetSec = 0.006f;
        private const int MaxRuntimeZstdWriteQueue = 96;
        private const int MaxRuntimeZstdWritesPerFrame = 1;

        /// <summary>
        /// Spread runtime zstd cache writes across frames. VaM native Finish already writes .vamcache
        /// synchronously; VPB zstd writes were duplicated in PostFinish and caused periodic hitches
        /// (soft-body physics jitter when frame time spikes).
        /// </summary>
        public void DrainPendingRuntimeZstdWrites()
        {
            if (LogUtil.IsSceneLoading() || LogUtil.IsSceneLoadActive()) return;

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
                    Qi = qi,
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

            var qi = item.Qi;
            string zstdPath = item.ZstdPath;
            bool queuedToWorker = false;
            try
            {
                if (qi == null || qi.tex == null) return;

                if (item.IsAlphaFromGrayscale)
                {
                    if (string.IsNullOrEmpty(zstdPath))
                    {
                        zstdPath = TextureUtil.BuildExactZstdCachePath(
                            qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                            0, 0, qi.bumpStrength, false);
                    }
                    if (string.IsNullOrEmpty(zstdPath)) return;
                    if (File.Exists(zstdPath) && File.Exists(zstdPath + "meta")) return;

                    byte[] raw;
                    TextureFormat format;
                    int w, h;
                    if (!TryCaptureAlphaTextureBytesForZstdWrite(qi, out raw, out format, out w, out h)) return;
                    if (raw == null || raw.Length == 0) return;

                    int level = 3;
                    try { if (Settings.Instance != null) level = Settings.Instance.ZstdCompressionLevel.Value; } catch { }
                    string pathCopy = zstdPath;
                    queuedToWorker = true;
                    ThreadPool.QueueUserWorkItem(_ => WriteCapturedBytesToZstdCache(pathCopy, raw, w, h, format, false, false, level, AlphaCacheVersion));
                    return;
                }

                if (string.IsNullOrEmpty(zstdPath)) return;
                if (File.Exists(zstdPath) && File.Exists(zstdPath + "meta")) return;

                byte[] rawBytes;
                TextureFormat fmt;
                int width, height;
                if (!TryGetTextureBytesForZstdWrite(qi.tex as Texture2D, qi.linear, item.IsSimPath, out rawBytes, out fmt, out width, out height))
                    return;
                if (rawBytes == null || rawBytes.Length == 0) return;

                int zlevel = 3;
                try { if (Settings.Instance != null) zlevel = Settings.Instance.ZstdCompressionLevel.Value; } catch { }
                bool isSim = item.IsSimPath;
                bool queueMip = item.QueueCreateMipMaps;
                string pathForWorker = zstdPath;
                queuedToWorker = true;
                ThreadPool.QueueUserWorkItem(_ => WriteCapturedBytesToZstdCache(pathForWorker, rawBytes, width, height, fmt, isSim, queueMip, zlevel, 0));
            }
            finally
            {
                if (!queuedToWorker)
                    ReleasePendingZstdWriteSlot(zstdPath);
            }
        }

        private static bool TryCaptureAlphaTextureBytesForZstdWrite(
            ImageLoaderThreaded.QueuedImage qi,
            out byte[] raw,
            out TextureFormat format,
            out int w,
            out int h)
        {
            raw = null;
            format = TextureFormat.RGBA32;
            w = 0;
            h = 0;
            if (qi == null || qi.tex == null) return false;

            var src = qi.tex;
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, qi.linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
            Texture2D rgba = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                rgba = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, qi.linear);
                rgba.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
                rgba.Apply(false, false);
                RenderTexture.active = prev;
                w = src.width;
                h = src.height;
                raw = rgba.GetRawTextureData();
                return raw != null && raw.Length > 0;
            }
            finally
            {
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
            }
        }

        /// <summary>Scene load prep: drop stale candidate refs, cancel queued zstd writes; keep RAM decompressed + texture caches.</summary>
        public void PrepareForSceneLoad()
        {
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

        public void TrackCandidate(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null || string.IsNullOrEmpty(qi.imgPath) || qi.imgPath == "NULL") return;
            lock (candidateLock)
            {
                pathCandidates[qi.imgPath] = qi;
                pathCreateMipMapsHints[qi.imgPath] = qi.createMipMaps;
                if (qi.tex != null) texIdCandidates[qi.tex.GetInstanceID()] = qi;
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

        public bool TryEnqueueResizeCache(ImageLoaderThreaded.QueuedImage qi)
        {
            if (!ShouldWriteRuntimeZstdCache(qi)) return false;

            if (qi.createAlphaFromGrayscale)
            {
                string alphaPath = TextureUtil.BuildExactZstdCachePath(
                    qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                    0, 0, qi.bumpStrength, false);
                if (!string.IsNullOrEmpty(alphaPath))
                {
                    lock (pendingZstdWriteLock)
                    {
                        if (pendingZstdWrites.Contains(alphaPath)) return true;
                        pendingZstdWrites.Add(alphaPath);
                    }
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
                ThreadPool.QueueUserWorkItem(_ => TryUpgradeExistingZstdMeta(qi, zstdPath, queueMip));
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
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                rgba = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
                rgba.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                rgba.Apply(false, false);
                RenderTexture.active = prev;
                raw = rgba.GetRawTextureData();
                format = TextureFormat.RGBA32;
                return raw != null && raw.Length > 0;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                if (rgba != null) UnityEngine.Object.Destroy(rgba);
            }
        }
    }
}
