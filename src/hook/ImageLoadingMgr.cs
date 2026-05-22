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
            bool isSimPath = SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
            string pathKey = GetTextureCacheKey(qi);

            lock (cachePathMapLock)
            {
                if (cachePathMap.TryGetValue(pathKey, out var cached))
                {
                    if (cached != null && File.Exists(cached)) return cached;
                    cachePathMap.Remove(pathKey);
                }
            }

            string vpbCachePath = TextureUtil.ResolveServeZstdCachePath(
                qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength, isSimPath);

            if (vpbCachePath != null && TextureUtil.IsTiffTexturePath(qi.imgPath)
                && !TextureUtil.IsZstdCachePayloadSane(vpbCachePath, 64 * 64))
            {
                vpbCachePath = null;
            }

            if (vpbCachePath != null && qi.createAlphaFromGrayscale
                && !TextureUtil.IsZstdCachePayloadSane(vpbCachePath, 32 * 32))
            {
                vpbCachePath = null;
            }

            if (vpbCachePath != null && File.Exists(vpbCachePath))
            {
                CacheCleanupManager.QueueHit(vpbCachePath, qi.imgPath);
                lock (cachePathMapLock)
                {
                    cachePathMap[pathKey] = vpbCachePath;
                }
                return vpbCachePath;
            }

            return null;
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

        private static bool IsRawDataValidForMeta(MetadataEntry meta, byte[] data, bool queueCreateMipMaps = false)
        {
            if (meta == null || data == null || data.Length == 0) return false;

            FinalizeMetaFromRaw(meta, data, queueCreateMipMaps);

            if (!TextureUtil.ValidateRawLengthForTextureMeta(meta.Width, meta.Height, meta.Format, data.Length, meta.MipStorage))
            {
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
                if (!IsRawDataValidForMeta(meta, data, queueCreateMipMaps)) return;
                TryUpgradeDiskCacheMetaIfStale(zstdPath, meta, data.Length, queueCreateMipMaps);
                lock (metadataCacheLock)
                {
                    metadataCache[zstdPath] = meta;
                }
            }
            catch { }
        }

        private string ResolveZstdServePath(ImageLoaderThreaded.QueuedImage qi)
        {
            if (qi == null) return null;
            bool isSimPath = SuperControllerHook.IsSimulationTexturePath(qi.imgPath);
            string vpbCachePath = TextureUtil.ResolveServeZstdCachePath(
                qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert,
                qi.setSize ? qi.width : 0, qi.setSize ? qi.height : 0, qi.bumpStrength, isSimPath);

            if (vpbCachePath != null && TextureUtil.IsTiffTexturePath(qi.imgPath)
                && !TextureUtil.IsZstdCachePayloadSane(vpbCachePath, 64 * 64))
            {
                vpbCachePath = null;
            }

            if (vpbCachePath != null && qi.createAlphaFromGrayscale
                && !TextureUtil.IsZstdCachePayloadSane(vpbCachePath, 32 * 32))
            {
                vpbCachePath = null;
            }

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

            var meta = FastLoadMetadata(zstdPath);
            if (meta == null) return false;

            byte[] memData = null;
            lock (decompressedCacheLock)
            {
                CachedDecompressed cached;
                if (decompressedCache.TryGetValue(zstdPath, out cached))
                    memData = cached.Data;
            }

            if (memData == null || !IsRawDataValidForMeta(meta, memData, queueCreateMipMaps)) return false;

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
                if (!string.IsNullOrEmpty(zstdPath) && File.Exists(zstdPath) && File.Exists(zstdPath + "meta"))
                {
                    if (allowZstdFromMemory && TryGetZstdPayloadFromMemory(zstdPath, queueCreateMipMaps, out payload))
                        return true;

                    if (allowSyncZstdDecompress)
                    {
                        var meta = FastLoadMetadata(zstdPath);
                        if (meta != null && IsPayloadSmallEnoughForSyncServe(meta))
                        {
                            byte[] data = FastGetDecompressed(zstdPath);
                            if (IsRawDataValidForMeta(meta, data, queueCreateMipMaps))
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
                        if (IsRawDataValidForMeta(meta, data, queueCreateMipMaps))
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

                if (meta != null && memData != null && IsRawDataValidForMeta(meta, memData, queueCreateMipMaps))
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
                    lock (inflightLock) { inflightKeys.Remove(cacheKey); }
                    return;
                }

                byte[] data;
                try { data = File.ReadAllBytes(nativePath); }
                catch
                {
                    lock (inflightLock) { inflightKeys.Remove(cacheKey); }
                    return;
                }

                bool queueMip = ResolveQueueCreateMipMaps(qi);
                if (!IsRawDataValidForMeta(meta, data, queueMip))
                {
                    lock (inflightLock) { inflightKeys.Remove(cacheKey); }
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
                lock (inflightLock) { inflightKeys.Remove(cacheKey); }
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

                bool queueMip = ResolveQueueCreateMipMaps(qi);
                if (!IsRawDataValidForMeta(meta, decompressedData, queueMip))
                {
                    LogUtil.LogError($"LoadAndDecompress: Decompressed data size mismatch for {cachePath}");
                    lock (inflightLock)
                    {
                        inflightKeys.Remove(cacheKey);
                    }
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
                lock (inflightLock)
                {
                    inflightKeys.Remove(cacheKey);
                }
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

                bool createMipMaps = data.Meta.CreateMipMaps;
                if (!createMipMaps && data.OriginalQI != null)
                    createMipMaps = ResolveQueueCreateMipMaps(data.OriginalQI);

                Texture2D tex = TextureUtil.CreateTextureFromCachedRaw(data.Data, data.Meta.Width, data.Meta.Height, data.Meta.Format,
                    createMipMaps, data.OriginalQI.linear, !isSimTexture, isSimTexture);
                if (tex == null) return;

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

        private static void BulkZstdWriteMeta(string targetPath, string sourceMetaPath, JSONNode metaJson, int compressionLevel, byte[] rawForMipMeta = null)
        {
            if (metaJson != null)
            {
                try
                {
                    metaJson["type"] = "compressed";
                    metaJson["width"] = metaJson["width"].Value;
                    metaJson["height"] = metaJson["height"].Value;
                    metaJson["zstdLevel"].AsInt = compressionLevel;
                    if (rawForMipMeta != null && rawForMipMeta.Length > 0)
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
                        TextureUtil.WriteMipFieldsToMeta(metaJson, w, h, fmt, rawForMipMeta.Length, queueMip);
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
            byte[] nativeRaw = null;
            try { nativeRaw = File.ReadAllBytes(nativePath); } catch { }
            ZstdCompressor.SaveCacheFromFile(targetPath, nativePath, compressionLevel);
            BulkZstdWriteMeta(targetPath, nativePath + "meta", metaJson, compressionLevel, nativeRaw);
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
            BulkZstdWriteMeta(zstdPath, zstdPath + "meta", metaJson, compressionLevel, raw);
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
            int eligibleCount = 0;
            foreach (var f in files)
            {
                try
                {
                    if (!File.Exists(f + "meta")) continue;
                    var mj = JSON.Parse(File.ReadAllText(f + "meta"));
                    if (BulkZstdIsThumbnail(mj, threshold)) continue;
                    eligibleCount++;
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
                    eligibleCount++;
                }
                catch { }
            }
            CurrentZstdStats.TotalFiles = eligibleCount;
            if (eligibleCount == 0)
            {
                LogUtil.Log("[VPB] Bulk compress: no eligible caches (native=" + files.Length + " zstd=" + zstdFiles.Length + ") in " + nativeCacheDir);
            }

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

                    long originalSize = new FileInfo(file).Length;
                    CurrentZstdStats.TotalOriginalSize += originalSize;

                    if (BulkZstdNeedsCompression(file, targetPath, compressionLevel))
                    {
                        BulkZstdCompressNativeToZstd(file, targetPath, compressionLevel, metaJson);
                    }

                    if (File.Exists(targetPath))
                    {
                        CurrentZstdStats.TotalCompressedSize += new FileInfo(targetPath).Length;
                    }

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
                    CurrentZstdStats.CurrentFile = Path.GetFileName(zstdPath);
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

                    long originalSize = new FileInfo(zstdPath).Length;
                    CurrentZstdStats.TotalOriginalSize += originalSize;

                    if (BulkZstdNeedsCompression(zstdPath, zstdPath, compressionLevel))
                    {
                        BulkZstdRecompressZstdInPlace(zstdPath, compressionLevel, metaJson);
                    }

                    if (File.Exists(zstdPath))
                    {
                        CurrentZstdStats.TotalCompressedSize += new FileInfo(zstdPath).Length;
                    }

                    CurrentZstdStats.ProcessedFiles++;
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
            LogUtil.Log(string.Format("Bulk compression completed: {0} processed, {1} failed", CurrentZstdStats.ProcessedFiles, CurrentZstdStats.FailedCount));
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
            if (qi == null || qi.tex == null || string.IsNullOrEmpty(qi.imgPath)) return;
            try
            {
                string zstdPath = TextureUtil.BuildExactZstdCachePath(qi.imgPath, qi.compress, qi.linear, qi.isNormalMap, qi.createAlphaFromGrayscale, qi.createNormalFromBump, qi.invert, 0, 0, qi.bumpStrength, false);
                if (string.IsNullOrEmpty(zstdPath)) return;
                if (File.Exists(zstdPath) && File.Exists(zstdPath + "meta")) return;

                var src = qi.tex;
                RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, qi.linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
                Graphics.Blit(src, rt);
                RenderTexture prev = RenderTexture.active;
                Texture2D rgba = null;
                try
                {
                    RenderTexture.active = rt;
                    rgba = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, qi.linear);
                    rgba.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
                    rgba.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }

                byte[] raw = rgba.GetRawTextureData();
                UnityEngine.Object.Destroy(rgba);
                if (raw == null || raw.Length == 0) return;

                int level = 3;
                try { if (Settings.Instance != null) level = Settings.Instance.ZstdCompressionLevel.Value; } catch { }

                byte[] compressed;
                NativeTextureOnDemandCache.EnsureZstdInitialized();
                using (var compressor = new Compressor(new CompressionOptions(level)))
                    compressed = compressor.Wrap(raw, 0, raw.Length);

                string zdir = Path.GetDirectoryName(zstdPath);
                if (!string.IsNullOrEmpty(zdir) && !Directory.Exists(zdir)) Directory.CreateDirectory(zdir);

                File.WriteAllBytes(zstdPath, compressed);

                var zmeta = new SimpleJSON.JSONClass();
                zmeta["type"] = "compressed";
                zmeta["width"] = src.width.ToString();
                zmeta["height"] = src.height.ToString();
                zmeta["format"] = TextureFormat.RGBA32.ToString();
                zmeta["vpbVer"].AsInt = AlphaCacheVersion;
                TextureUtil.WriteMipFieldsToMeta(zmeta, src.width, src.height, TextureFormat.RGBA32, raw.Length, false);
                File.WriteAllText(zstdPath + "meta", VPB.src.util.JsonSerializationUtil.Serialize(zmeta, 1024));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB ALPHA WRITE] Failed for " + qi.imgPath + ": " + ex.Message);
            }
        }

        // --- Runtime zstd write-after-load (lazy cache) ---

        private readonly Dictionary<string, ImageLoaderThreaded.QueuedImage> pathCandidates = new Dictionary<string, ImageLoaderThreaded.QueuedImage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> pathCreateMipMapsHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, ImageLoaderThreaded.QueuedImage> texIdCandidates = new Dictionary<int, ImageLoaderThreaded.QueuedImage>();
        private readonly HashSet<string> pendingZstdWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object candidateLock = new object();
        private readonly object pendingZstdWriteLock = new object();

        public void ClearCandidates()
        {
            lock (candidateLock)
            {
                pathCandidates.Clear();
                texIdCandidates.Clear();
                pathCreateMipMapsHints.Clear();
            }
        }

        public void ProcessCandidates() { }

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
                ThreadPool.QueueUserWorkItem(_ => WriteAlphaTextureToZstdCache(qi));
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

            bool queueCreateMipMaps = ResolveQueueCreateMipMaps(qi);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { WriteQueuedImageToZstdCache(qi, zstdPath, isSimPath, queueCreateMipMaps); }
                finally
                {
                    lock (pendingZstdWriteLock) { pendingZstdWrites.Remove(zstdPath); }
                }
            });
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

        private static void WriteQueuedImageToZstdCache(ImageLoaderThreaded.QueuedImage qi, string zstdPath, bool isSimPath, bool queueCreateMipMaps)
        {
            if (qi == null || qi.tex == null || string.IsNullOrEmpty(zstdPath)) return;
            if (File.Exists(zstdPath) && File.Exists(zstdPath + "meta")) return;

            try
            {
                var src = qi.tex as Texture2D;
                if (src == null) return;

                byte[] raw;
                TextureFormat format;
                int w, h;
                if (!TryGetTextureBytesForZstdWrite(src, qi.linear, isSimPath, out raw, out format, out w, out h))
                    return;
                if (raw == null || raw.Length == 0) return;

                int level = 3;
                try { if (Settings.Instance != null) level = Settings.Instance.ZstdCompressionLevel.Value; } catch { }

                byte[] compressed = ZstdCompressor.Compress(raw, level);
                if (compressed == null || compressed.Length == 0) return;

                string zdir = Path.GetDirectoryName(zstdPath);
                if (!string.IsNullOrEmpty(zdir) && !Directory.Exists(zdir)) Directory.CreateDirectory(zdir);

                string temp = zstdPath + ".tmp";
                File.WriteAllBytes(temp, compressed);
                if (File.Exists(zstdPath)) try { File.Delete(zstdPath); } catch { }
                File.Move(temp, zstdPath);

                var zmeta = new SimpleJSON.JSONClass();
                zmeta["type"] = "compressed";
                zmeta["width"] = w.ToString();
                zmeta["height"] = h.ToString();
                zmeta["format"] = format.ToString();
                if (isSimPath) zmeta["isReadable"] = "true";
                zmeta["zstdLevel"].AsInt = level;
                TextureUtil.WriteMipFieldsToMeta(zmeta, w, h, format, raw.Length, queueCreateMipMaps);
                File.WriteAllText(zstdPath + "meta", VPB.src.util.JsonSerializationUtil.Serialize(zmeta, 1024));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB ZSTD WRITE] Failed for " + (qi != null ? qi.imgPath : "?") + ": " + ex.Message);
            }
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
