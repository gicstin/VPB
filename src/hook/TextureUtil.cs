using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SimpleJSON;
using UnityEngine;
using System.Runtime.InteropServices;

namespace VPB
{
    public static class TextureUtil
    {
        private static readonly object s_DownscaledActiveLock = new object();
        private static readonly HashSet<string> s_DownscaledActiveKeys = new HashSet<string>();

        private static readonly object s_SimPurgeLock = new object();
        private static readonly HashSet<string> s_SimPurgedThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static int GetDownscaledActiveCount()
        {
            lock (s_DownscaledActiveLock)
            {
                return s_DownscaledActiveKeys.Count;
            }
        }

        public static void MarkDownscaledActive(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (s_DownscaledActiveLock)
            {
                s_DownscaledActiveKeys.Add(key);
            }
        }

        public static void UnmarkDownscaledActive(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (s_DownscaledActiveLock)
            {
                s_DownscaledActiveKeys.Remove(key);
            }
        }

        public static void UnmarkDownscaledActiveByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return;
            lock (s_DownscaledActiveLock)
            {
                if (s_DownscaledActiveKeys.Count == 0) return;
                var remove = new List<string>();
                foreach (var k in s_DownscaledActiveKeys)
                {
                    if (k != null && k.StartsWith(prefix))
                    {
                        remove.Add(k);
                    }
                }
                for (int i = 0; i < remove.Count; i++)
                {
                    s_DownscaledActiveKeys.Remove(remove[i]);
                }
            }
        }

        public static int GetExpectedRawDataSize(int w, int h, TextureFormat fmt)
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

        public static bool IsBlockCompressedFormat(TextureFormat fmt)
        {
            return fmt == TextureFormat.DXT1 || fmt == TextureFormat.DXT5;
        }

        public static bool CanGenerateMipsFromBaseLevel(int w, int h, TextureFormat fmt)
        {
            if (IsBlockCompressedFormat(fmt)) return false;
            return GetExpectedFullMipChainSize(w, h, fmt) > 0;
        }

        public static bool ShouldAllocateMipChain(bool createMipMaps, int rawLength, int w, int h, TextureFormat fmt)
        {
            if (!createMipMaps) return false;
            if (ShouldAllocateMipChainForRawLoad(createMipMaps, rawLength, w, h, fmt)) return true;
            return CanGenerateMipsFromBaseLevel(w, h, fmt);
        }

        public static bool ShouldGenerateMipsOnApply(bool createMipMaps, int rawLength, int w, int h, TextureFormat fmt)
        {
            if (!createMipMaps) return false;
            if (ShouldAllocateMipChainForRawLoad(createMipMaps, rawLength, w, h, fmt)) return false;
            return CanGenerateMipsFromBaseLevel(w, h, fmt);
        }

        /// <summary>Match CustomImageLoader: full mip chain bytes in <paramref name="rawLength"/> require mipChain on Texture2D ctor.</summary>
        public static bool ShouldAllocateMipChainForRawLoad(bool createMipMaps, int rawLength, int w, int h, TextureFormat fmt)
        {
            if (!createMipMaps) return false;
            if (rawLength <= 0) return false;
            int baseSize = GetExpectedRawDataSize(w, h, fmt);
            if (baseSize <= 0) return true;
            return rawLength > baseSize;
        }

        public static void InferCreateMipMapsFromRawSize(ref bool createMipMaps, int rawLength, int w, int h, TextureFormat fmt)
        {
            if (createMipMaps) return;
            int baseSize = GetExpectedRawDataSize(w, h, fmt);
            if (baseSize > 0 && rawLength > baseSize) createMipMaps = true;
        }

        public const string MipStorageBase = "base";
        public const string MipStorageFull = "full";

        public const int TextureCacheVersion = 4;

        public static void WriteCacheVersionToMeta(JSONNode meta)
        {
            if (meta == null) return;
            meta["vpbVer"].AsInt = TextureCacheVersion;
        }


        public static bool IsMipMetaRepairableOnServe(bool createMipMaps, string mipStorage, int w, int h, TextureFormat fmt)
        {
            if (!createMipMaps) return true;
            if (string.Equals(mipStorage, MipStorageFull, StringComparison.OrdinalIgnoreCase)) return true;
            return CanGenerateMipsFromBaseLevel(w, h, fmt);
        }

        public static bool IsMipPayloadConsistent(bool createMipMaps, string mipStorage, int rawLength, int w, int h, TextureFormat fmt)
        {
            if (!createMipMaps) return true;
            int fullSize = GetExpectedFullMipChainSize(w, h, fmt);
            if (fullSize > 0 && rawLength == fullSize) return true;
            return IsMipMetaRepairableOnServe(createMipMaps, mipStorage, w, h, fmt);
        }

        /// <summary>Unity-style mip count from full size down to 1×1.</summary>
        public static int CountMipLevels(int w, int h)
        {
            if (w <= 0 || h <= 0) return 0;
            int count = 0;
            int bw = w;
            int bh = h;
            while (true)
            {
                count++;
                if (bw == 1 && bh == 1) break;
                bw = Math.Max(1, bw / 2);
                bh = Math.Max(1, bh / 2);
            }
            return count;
        }

        /// <summary>Sum of <see cref="GetExpectedRawDataSize"/> for every mip level (DXT + uncompressed).</summary>
        public static int GetExpectedFullMipChainSize(int w, int h, TextureFormat fmt)
        {
            if (w <= 0 || h <= 0) return 0;
            int total = 0;
            int bw = w;
            int bh = h;
            while (true)
            {
                int levelSize = GetExpectedRawDataSize(bw, bh, fmt);
                if (levelSize <= 0) return 0;
                total += levelSize;
                if (bw == 1 && bh == 1) break;
                bw = Math.Max(1, bw / 2);
                bh = Math.Max(1, bh / 2);
            }
            return total;
        }

        /// <summary>Whether cached bytes are base mip only or full mip chain in raw layout.</summary>
        public static string ClassifyMipStorage(int rawLength, int w, int h, TextureFormat fmt)
        {
            int baseSize = GetExpectedRawDataSize(w, h, fmt);
            int fullSize = GetExpectedFullMipChainSize(w, h, fmt);
            if (baseSize <= 0) return MipStorageBase;
            if (rawLength == baseSize) return MipStorageBase;
            if (fullSize > 0 && rawLength == fullSize) return MipStorageFull;
            if (rawLength > baseSize) return MipStorageFull;
            return MipStorageBase;
        }

        /// <summary>Queue intent OR meta OR full-chain storage OR oversized raw vs base.</summary>
        public static bool ResolveCreateMipMaps(bool queueCreateMipMaps, bool metaCreateMipMaps, string mipStorage, int rawLength, int w, int h, TextureFormat fmt)
        {
            if (string.Equals(mipStorage, MipStorageFull, StringComparison.OrdinalIgnoreCase)) return true;
            bool create = queueCreateMipMaps || metaCreateMipMaps;
            InferCreateMipMapsFromRawSize(ref create, rawLength, w, h, fmt);
            return create;
        }

        /// <summary>Validate raw payload size against base/full mip expectations (<see cref="MipStorageBase"/> / <see cref="MipStorageFull"/>).</summary>
        public static bool ValidateRawLengthForTextureMeta(int w, int h, TextureFormat fmt, int rawLength, string mipStorage)
        {
            if (rawLength <= 0 || w <= 0 || h <= 0) return false;

            int baseSize = GetExpectedRawDataSize(w, h, fmt);
            if (baseSize <= 0) return true;

            if (rawLength < baseSize) return false;

            int fullSize = GetExpectedFullMipChainSize(w, h, fmt);

            if (string.Equals(mipStorage, MipStorageFull, StringComparison.OrdinalIgnoreCase))
                return fullSize > 0 && rawLength == fullSize;

            if (string.Equals(mipStorage, MipStorageBase, StringComparison.OrdinalIgnoreCase))
                return rawLength == baseSize || (fullSize > 0 && rawLength == fullSize);

            if (rawLength == baseSize) return true;
            if (fullSize > 0 && rawLength == fullSize) return true;
            if (rawLength > baseSize && (fullSize <= 0 || rawLength < fullSize)) return false;
            if (fullSize > 0 && rawLength > fullSize) return false;
            return true;
        }

        /// <summary>True when on-disk meta predates mipStorage fields (pre-refactor zstd caches).</summary>
        public static bool DiskCacheMetaLacksMipFields(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath)) return false;
            try
            {
                string metaPath = cachePath + "meta";
                if (!File.Exists(metaPath)) return true;
                var metaJson = SimpleJSON.JSON.Parse(File.ReadAllText(metaPath));
                if (metaJson == null) return true;
                return metaJson["mipStorage"] == null || string.IsNullOrEmpty(metaJson["mipStorage"].Value);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Write createMipMaps, mipCount, mipStorage into cache meta JSON.</summary>
        public static void WriteMipFieldsToMeta(JSONNode meta, int w, int h, TextureFormat fmt, int rawLength, bool queueCreateMipMaps)
        {
            if (meta == null || w <= 0 || h <= 0 || rawLength <= 0) return;

            string storage = ClassifyMipStorage(rawLength, w, h, fmt);
            bool createMipMaps = ResolveCreateMipMaps(queueCreateMipMaps, false, storage, rawLength, w, h, fmt);

            meta["createMipMaps"].AsBool = createMipMaps;
            meta["mipCount"].AsInt = CountMipLevels(w, h);
            meta["mipStorage"] = storage;
        }


        private static void LoadRawPinned(Texture2D t, byte[] data, int size)
        {
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                t.LoadRawTextureData(pin.AddrOfPinnedObject(), size);
            }
            finally
            {
                pin.Free();
            }
        }

        public static void SafeLoadRawTextureData(Texture2D t, byte[] data, int length, int w, int h, TextureFormat fmt)
        {
            if (t == null || data == null) return;

            int expected = GetExpectedRawDataSize(w, h, fmt);
            if (expected <= 0)
            {
                t.LoadRawTextureData(data);
                return;
            }

            if (length <= 0) length = data.Length;

            if (length < expected)
            {
                LogUtil.LogWarning($"[VPB] SafeLoadRawTextureData: data length ({length}) is smaller than expected ({expected}) for {w}x{h} {fmt}");
                t.LoadRawTextureData(data);
                return;
            }

            int required = expected;
            if (t.mipmapCount > 1)
            {
                int full = GetExpectedFullMipChainSize(w, h, fmt);
                if (full > 0) required = full;
            }

            if (length >= required || data.Length >= required)
            {
                LoadRawPinned(t, data, required);
                return;
            }

            byte[] padded = ByteArrayPool.Rent(required);
            try
            {
                Buffer.BlockCopy(data, 0, padded, 0, expected);
                LoadRawPinned(t, padded, required);
            }
            finally
            {
                ByteArrayPool.Return(padded);
            }
        }

        /// <summary>
        /// Overload that uses data.Length as the valid data length.
        /// </summary>
        public static void SafeLoadRawTextureData(Texture2D t, byte[] data, int w, int h, TextureFormat fmt)
        {
            SafeLoadRawTextureData(t, data, data != null ? data.Length : 0, w, h, fmt);
        }

        /// <summary>Apply cached raw (+ optional mip chain) to an existing or new target texture.</summary>
        public static bool ApplyCachedRawToTexture(Texture2D tex, byte[] data, int w, int h, TextureFormat fmt, bool createMipMaps, bool linear, bool markNonReadable, bool forceReadable)
        {
            if (tex == null || data == null || data.Length == 0 || w <= 0 || h <= 0) return false;

            int rawLen = data.Length;
            int expected = GetExpectedRawDataSize(w, h, fmt);
            if (expected > 0 && rawLen < expected) return false;

            InferCreateMipMapsFromRawSize(ref createMipMaps, rawLen, w, h, fmt);
            bool mipAlloc = ShouldAllocateMipChainForRawLoad(createMipMaps, rawLen, w, h, fmt);
            bool genMipsOnApply = ShouldGenerateMipsOnApply(createMipMaps, rawLen, w, h, fmt);
            bool wantMipChain = mipAlloc || genMipsOnApply;
            bool isSimTexture = forceReadable;
            bool isCompressedTarget = IsBlockCompressedFormat(fmt);
            bool needsReinit = tex.width != w || tex.height != h || tex.format != fmt || (tex.mipmapCount > 1) != wantMipChain;

            try
            {
                if (!needsReinit || tex.Resize(w, h, fmt, wantMipChain))
                {
                    if (TextureMatchesCachedLayout(tex, w, h, fmt))
                    {
                        SafeLoadRawTextureData(tex, data, w, h, fmt);
                        tex.Apply(genMipsOnApply, markNonReadable && !isSimTexture);
                        return TextureMatchesCachedLayout(tex, w, h, fmt);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!isCompressedTarget)
                {
                    LogUtil.LogError("[VPB] ApplyCachedRawToTexture failed: " + ex.Message);
                    return false;
                }
                LogUtil.LogWarning("[VPB] ApplyCachedRawToTexture raw load failed (" + ex.Message + "); retrying via GPU copy");
            }

            if (!isCompressedTarget || isSimTexture) return false;

            try
            {
                if (needsReinit && !tex.Resize(w, h, fmt, wantMipChain)) return false;
                if (!TextureMatchesCachedLayout(tex, w, h, fmt)) return false;

                Texture2D tmp = new Texture2D(w, h, fmt, wantMipChain, linear);
                try
                {
                    SafeLoadRawTextureData(tmp, data, w, h, fmt);
                    tmp.Apply(genMipsOnApply, false);
                    Graphics.CopyTexture(tmp, tex);
                }
                finally
                {
                    UnityEngine.Object.Destroy(tmp);
                }
                return TextureMatchesCachedLayout(tex, w, h, fmt);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] ApplyCachedRawToTexture GPU copy failed: " + ex.Message);
                return false;
            }
        }

        private static bool TextureMatchesCachedLayout(Texture2D tex, int w, int h, TextureFormat fmt)
        {
            if (tex == null) return false;
            if (tex.width != w || tex.height != h) return false;
            return tex.format == fmt;
        }

        public static Texture2D CreateTextureFromCachedRaw(byte[] data, int w, int h, TextureFormat fmt, bool createMipMaps, bool linear, bool markNonReadable, bool forceReadable)
        {
            if (data == null || data.Length == 0 || w <= 0 || h <= 0) return null;
            InferCreateMipMapsFromRawSize(ref createMipMaps, data.Length, w, h, fmt);
            bool mipAlloc = ShouldAllocateMipChainForRawLoad(createMipMaps, data.Length, w, h, fmt);
            bool genMipsOnApply = ShouldGenerateMipsOnApply(createMipMaps, data.Length, w, h, fmt);
            bool isSimTexture = forceReadable;
            var tex = new Texture2D(w, h, fmt, mipAlloc || genMipsOnApply, linear);
            SafeLoadRawTextureData(tex, data, w, h, fmt);
            tex.Apply(genMipsOnApply, markNonReadable && !isSimTexture);
            return tex;
        }

        private static readonly char[] s_InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();

        public static string SanitizeFileName(string value)
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

        public static bool IsTiffTexturePath(string imgPath)
        {
            if (string.IsNullOrEmpty(imgPath)) return false;
            string lower = imgPath.Trim().ToLowerInvariant();
            return lower.EndsWith(".tif") || lower.EndsWith(".tiff");
        }

        /// <summary>Per-entry size for .var paths; MVR <see cref="MVR.FileManagement.FileEntry.Size"/> can be package size after resolve.</summary>
        public static long GetFileEntryCacheSizeToken(MVR.FileManagement.FileEntry fileEntry, string imgPath = null)
        {
            if (fileEntry == null) return 0;
            if (!string.IsNullOrEmpty(imgPath))
            {
                try
                {
                    FileEntry vpbEntry = FileManager.GetFileEntry(imgPath);
                    if (vpbEntry != null) return GetFileEntryCacheSizeToken(vpbEntry);
                }
                catch { }
            }
            return fileEntry.Size;
        }

        public static long GetFileEntryCacheSizeToken(FileEntry fileEntry)
        {
            if (fileEntry == null) return 0;
            VarFileEntry vfe = fileEntry as VarFileEntry;
            if (vfe != null) return vfe.EntrySize;
            return fileEntry.Size;
        }

        /// <summary>In-memory / inflight dedupe key; must match disk lookup dimensions and flags.</summary>
        public static string BuildTextureLookupKey(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, float bumpStrength, bool setSize, int width, int height, bool isReadableVariant)
        {
            return imgPath
                + "|" + compress
                + "|" + linear
                + "|" + isNormalMap
                + "|" + createAlphaFromGrayscale
                + "|" + createNormalFromBump
                + "|" + invert
                + "|" + (setSize ? width : 0)
                + "|" + (setSize ? height : 0)
                + "|" + bumpStrength
                + "|" + isReadableVariant;
        }

        /// <summary>Runtime zstd serve: exact FileEntry match first, then sig variant + size-agnostic fallback for legacy caches.</summary>
        public static string ResolveServeZstdCachePath(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth, int targetHeight, float bumpStrength, bool isReadable)
        {
            string path = FindZstdCacheFileOnDisk(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength, isReadable);
            if (path != null) return path;

            if (targetWidth > 0 && targetHeight > 0)
            {
                path = FindZstdCacheFileOnDisk(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, 0, 0, bumpStrength, isReadable);
                if (path != null) return path;
            }

            bool isSimPath = isReadable || SuperControllerHook.IsSimulationTexturePath(imgPath);
            if (isSimPath && isReadable)
            {
                path = FindZstdCacheFileOnDisk(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength, false);
                if (path != null) return path;
                if (targetWidth > 0 && targetHeight > 0)
                {
                    path = FindZstdCacheFileOnDisk(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, 0, 0, bumpStrength, false);
                }
            }

            return path;
        }

        /// <summary>
        /// On-demand may write richer sigs (_C_L, _C_A) than VaM requests at runtime (_C).
        /// Alpha (_C_A) and normal (_L_N) must never fall back to a weaker sig (e.g. _C for _C_A).
        /// </summary>
        private static string TryResolveExistingZstdVariant(string cacheDir, string fileName, string sizeStr, string timeStr, string requestedSig, string primaryPath)
        {
            if (File.Exists(primaryPath)) return primaryPath;

            string prefix = fileName + "_" + sizeStr + "_" + timeStr + "_";
            string[] trySigs;
            if (!string.IsNullOrEmpty(requestedSig) && requestedSig.IndexOf("_A", StringComparison.Ordinal) >= 0)
            {
                trySigs = new[] { "_C_A", "_C_L_A" };
            }
            else if (!string.IsNullOrEmpty(requestedSig) && requestedSig.IndexOf("_N", StringComparison.Ordinal) >= 0)
            {
                trySigs = new[] { requestedSig, "_C_L_N", "_L_N", "_N" };
            }
            else if (string.IsNullOrEmpty(requestedSig) || requestedSig == "_C")
            {
                trySigs = new[] { "_C_L", "_C_A", "_C_L_N", "_L_N", "_L", "_N", "_C" };
            }
            else if (requestedSig.StartsWith("_C", StringComparison.Ordinal))
            {
                trySigs = new[] { requestedSig, "_C_L", "_C_A", "_C_L_N" };
            }
            else
            {
                trySigs = new[] { requestedSig, "_C", "_C_L" };
            }

            for (int i = 0; i < trySigs.Length; i++)
            {
                string altSig = trySigs[i];
                if (altSig == requestedSig) continue;
                string altPath = System.IO.Path.Combine(cacheDir, prefix + altSig + ".zvamcache");
                if (File.Exists(altPath)) return altPath;
            }

            if (!string.IsNullOrEmpty(requestedSig)
                && requestedSig.IndexOf("_A", StringComparison.Ordinal) < 0
                && requestedSig.IndexOf("_N", StringComparison.Ordinal) < 0)
            {
                string bulkPath = System.IO.Path.Combine(cacheDir, prefix + "_C.zvamcache");
                if (!requestedSig.Equals("_C", StringComparison.Ordinal))
                {
                    bulkPath = System.IO.Path.Combine(cacheDir, fileName + "_" + sizeStr + "_" + timeStr + "__C.zvamcache");
                }
                if (File.Exists(bulkPath)) return bulkPath;
            }

            string sizeAgnostic = TryFindZstdCacheSizeAgnostic(cacheDir, fileName, timeStr, requestedSig);
            if (!string.IsNullOrEmpty(sizeAgnostic)) return sizeAgnostic;

            return primaryPath;
        }

        private static string NormalizeZstdFlagSig(string flagSig)
        {
            if (string.IsNullOrEmpty(flagSig)) return "_C";
            string s = flagSig;
            if (!s.StartsWith("_", StringComparison.Ordinal) && s.IndexOf('_') < 0)
            {
                s = "_" + s;
            }
            return s;
        }

        private static string[] BuildZstdSigVariants(string requestedSig)
        {
            string sig = NormalizeZstdFlagSig(requestedSig);
            if (sig.IndexOf("_A", StringComparison.Ordinal) >= 0)
            {
                return new[] { sig, "_C_A", "_C_L_A" };
            }
            if (sig.IndexOf("_N", StringComparison.Ordinal) >= 0)
            {
                return new[] { sig, "_C_L_N", "_L_N", "_N" };
            }
            if (sig == "_C")
            {
                return new[] { "_C", "__C", "_C_L", "_C_A", "_C_L_N", "_L_N", "_L" };
            }
            if (sig.StartsWith("_C", StringComparison.Ordinal))
            {
                return new[] { sig, "_C", "__C", "_C_L", "_C_A", "_C_L_N" };
            }
            return new[] { sig, "_C", "__C", "_C_L" };
        }

        /// <summary>Find .zvamcache when size token differs (on-demand/bulk use payload or native file size, not always FileEntry.Size).</summary>
        private static string TryFindZstdCacheSizeAgnostic(string cacheDir, string fileName, string timeStr, string requestedSig)
        {
            if (string.IsNullOrEmpty(cacheDir) || string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(timeStr))
            {
                return null;
            }

            string[] matches;
            try
            {
                matches = Directory.GetFiles(cacheDir, fileName + "_*_" + timeStr + "_*.zvamcache", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                return null;
            }
            if (matches == null || matches.Length == 0) return null;

            string[] trySigs = BuildZstdSigVariants(requestedSig);
            for (int i = 0; i < matches.Length; i++)
            {
                string p = matches[i];
                if (string.IsNullOrEmpty(p) || !File.Exists(p + "meta")) continue;

                string baseName = Path.GetFileNameWithoutExtension(p);
                if (!TryParseNativeVamCacheFileName(baseName, out _, out _, out _, out string flagSig))
                {
                    continue;
                }

                string diskSig = NormalizeZstdFlagSig(flagSig);
                for (int s = 0; s < trySigs.Length; s++)
                {
                    if (diskSig == NormalizeZstdFlagSig(trySigs[s]))
                    {
                        return p;
                    }
                }
            }

            return null;
        }

        /// <summary>Returns true when zstd meta exists and width*height is at least minPixels.</summary>
        public static bool IsZstdCachePayloadSane(string zstdPath, int minPixels)
        {
            if (string.IsNullOrEmpty(zstdPath) || !File.Exists(zstdPath)) return false;
            try
            {
                string metaPath = zstdPath + "meta";
                if (!File.Exists(metaPath)) return false;
                var mz = SimpleJSON.JSON.Parse(File.ReadAllText(metaPath));
                if (mz == null) return false;
                int w = mz["width"].AsInt;
                int h = mz["height"].AsInt;
                if (w <= 0 || h <= 0) return false;
                return (long)w * h >= minPixels;
            }
            catch { return false; }
        }

        public static bool CacheEntryNeedsSourceRebuild(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath)) return false;
            try
            {
                string metaPath = cachePath + "meta";
                if (!File.Exists(metaPath)) return false;
                var mz = SimpleJSON.JSON.Parse(File.ReadAllText(metaPath));
                if (mz == null) return false;

                string storage = mz["mipStorage"] != null ? mz["mipStorage"].Value : null;
                if (string.IsNullOrEmpty(storage)) return false;
                if (mz["vpbVer"].AsInt >= TextureCacheVersion) return false;

                int w = mz["width"].AsInt;
                int h = mz["height"].AsInt;
                if (w <= 0 || h <= 0) return false;

                TextureFormat fmt = TextureFormat.RGBA32;
                try { fmt = (TextureFormat)Enum.Parse(typeof(TextureFormat), mz["format"].Value); } catch { }

                return !IsMipMetaRepairableOnServe(mz["createMipMaps"].AsBool, storage, w, h, fmt);
            }
            catch { return false; }
        }

        public static void TryDeleteZstdCacheFile(string zstdPath)
        {
            if (string.IsNullOrEmpty(zstdPath)) return;
            try { if (File.Exists(zstdPath)) File.Delete(zstdPath); } catch { }
            try { string meta = zstdPath + "meta"; if (File.Exists(meta)) File.Delete(meta); } catch { }
        }

        /// <summary>Delete all VaM native .vamcache rows for a loose thumbnail (any size/time token).</summary>
        public static void TryDeleteNativeThumbnailDiskCachesForSource(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return;

            string fileName = System.IO.Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(fileName)) return;
            try { fileName = SanitizeFileName(fileName).Replace('.', '_'); }
            catch { fileName = fileName.Replace('.', '_'); }

            string cacheDir = ResolveTextureCacheDirFullPath();
            if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir)) return;

            string[] matches = null;
            try { matches = Directory.GetFiles(cacheDir, fileName + "_*.vamcache", SearchOption.TopDirectoryOnly); }
            catch { matches = null; }
            if (matches == null) return;

            for (int i = 0; i < matches.Length; i++)
                TryDeleteZstdCacheFile(matches[i]);
        }

        /// <summary>Delete all VPB .zvamcache rows for a loose thumbnail (any size/time token).</summary>
        public static void TryDeleteZstdThumbnailDiskCachesForSource(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return;

            string fileName = System.IO.Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(fileName)) return;
            try { fileName = SanitizeFileName(fileName).Replace('.', '_'); }
            catch { fileName = fileName.Replace('.', '_'); }

            string cacheDir = null;
            try { cacheDir = VamHookPlugin.GetCacheDir(); } catch { cacheDir = null; }
            if (string.IsNullOrEmpty(cacheDir) || !Directory.Exists(cacheDir)) return;

            string[] matches = null;
            try { matches = Directory.GetFiles(cacheDir, fileName + "_*.zvamcache", SearchOption.TopDirectoryOnly); }
            catch { matches = null; }
            if (matches == null) return;

            for (int i = 0; i < matches.Length; i++)
                TryDeleteZstdCacheFile(matches[i]);
        }

        private static readonly Regex s_NativeCacheFlagSuffix = new Regex(@"_(_?[CLNAIR]|BN\d+(?:\.\d+)?)$", RegexOptions.Compiled);
        private static readonly Regex s_NativeCacheTimeSuffix = new Regex(@"_(\d{17,18})$", RegexOptions.Compiled);
        private static readonly Regex s_NativeCacheSizeSuffix = new Regex(@"_(\d{1,12})$", RegexOptions.Compiled);
        private static readonly Regex s_NativeCacheExtSuffix = new Regex(@"_([a-zA-Z]{2,5})$", RegexOptions.Compiled);

        /// <summary>Resolve VaM texture cache directory to an absolute path for filesystem scans.</summary>
        public static string ResolveTextureCacheDirFullPath()
        {
            string dir = null;
            try { dir = MVR.FileManagement.CacheManager.GetTextureCacheDir(); } catch { dir = null; }
            if (string.IsNullOrEmpty(dir))
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, "../Cache/Textures"));
            }
            try { return Path.GetFullPath(dir); }
            catch { return dir; }
        }

        /// <summary>
        /// Parse VaM native <c>.vamcache</c> filename into source path hint, size/time tokens, and disk flag sig (_C, _L, _C_A, …).
        /// Format: <c>{name}_{size}_{time}_{sig}</c> where <c>sig</c> may be <c>_C</c>, <c>__L_N</c>, <c>1024_768_C</c>, or placeholder <c>_1</c>.
        /// </summary>
        public static bool TryParseNativeVamCacheFileName(string cacheFileBase, out string reconstructedPath, out string sizeStr, out string timeStr, out string flagSig)
        {
            reconstructedPath = null;
            sizeStr = string.Empty;
            timeStr = string.Empty;
            flagSig = string.Empty;
            if (string.IsNullOrEmpty(cacheFileBase)) return false;

            string s = cacheFileBase;
            bool sigFromPlaceholder = false;
            if (s.EndsWith("_1", StringComparison.Ordinal))
            {
                flagSig = "_C";
                sigFromPlaceholder = true;
                s = s.Substring(0, s.Length - 2);
            }

            if (!sigFromPlaceholder)
            {
                string builtSig = string.Empty;
                bool stripped;
                do
                {
                    stripped = false;
                    Match fm = s_NativeCacheFlagSuffix.Match(s);
                    if (fm.Success)
                    {
                        builtSig = "_" + fm.Groups[1].Value + builtSig;
                        s = s.Substring(0, fm.Index);
                        stripped = true;
                    }
                }
                while (stripped);

                Match dm = Regex.Match(s, @"_(\d+)_(\d+)$");
                if (dm.Success)
                {
                    builtSig = dm.Groups[1].Value + "_" + dm.Groups[2].Value + builtSig;
                    s = s.Substring(0, dm.Index);
                }

                flagSig = builtSig;
                if (string.IsNullOrEmpty(flagSig))
                {
                    flagSig = "_C";
                }
            }

            Match tm = s_NativeCacheTimeSuffix.Match(s);
            if (tm.Success)
            {
                timeStr = tm.Groups[1].Value;
                s = s.Substring(0, tm.Index);
            }

            Match sm = s_NativeCacheSizeSuffix.Match(s);
            if (sm.Success)
            {
                sizeStr = sm.Groups[1].Value;
                s = s.Substring(0, sm.Index);
            }

            Match em = s_NativeCacheExtSuffix.Match(s);
            if (em.Success)
            {
                string ext = em.Groups[1].Value.ToLowerInvariant();
                if (ext == "png" || ext == "jpg" || ext == "jpeg" || ext == "dds"
                    || ext == "tga" || ext == "bmp" || ext == "exr" || ext == "tiff" || ext == "tif")
                {
                    s = s.Substring(0, em.Index) + "." + ext;
                }
            }

            reconstructedPath = s.Replace('_', '.');
            if (reconstructedPath.IndexOf('.') < 0)
            {
                reconstructedPath = s;
            }
            return !string.IsNullOrEmpty(sizeStr) && !string.IsNullOrEmpty(timeStr);
        }

        /// <summary>Target .zvamcache path for bulk compress from a native <c>.vamcache</c> basename (parse + mirror fallback).</summary>
        public static string ResolveBulkZstdTargetPath(string vpbCacheDir, string nativeCacheFileBase, out bool parsed)
        {
            parsed = false;
            if (string.IsNullOrEmpty(vpbCacheDir) || string.IsNullOrEmpty(nativeCacheFileBase))
            {
                return null;
            }

            if (TryParseNativeVamCacheFileName(nativeCacheFileBase, out string reconstructed, out string bulkSizeStr, out string bulkTimeStr, out string flagSig))
            {
                string sourceFileName = Path.GetFileName(reconstructed);
                string vpbFileName = SanitizeFileName(sourceFileName).Replace('.', '_');
                string path = BuildZstdCachePathFromNativeTokens(vpbCacheDir, vpbFileName, bulkSizeStr, bulkTimeStr, flagSig);
                if (!string.IsNullOrEmpty(path))
                {
                    parsed = true;
                    return path;
                }
            }

            return Path.Combine(vpbCacheDir, nativeCacheFileBase + ".zvamcache");
        }

        /// <summary>Build zstd cache path matching <see cref="GetZstdCachePath"/> layout from parsed native cache tokens.</summary>
        public static string BuildZstdCachePathFromNativeTokens(string vpbCacheDir, string sanitizedSourceFileName, string sizeStr, string timeStr, string flagSig)
        {
            if (string.IsNullOrEmpty(vpbCacheDir) || string.IsNullOrEmpty(sanitizedSourceFileName)
                || string.IsNullOrEmpty(sizeStr) || string.IsNullOrEmpty(timeStr))
            {
                return null;
            }

            string sig = string.IsNullOrEmpty(flagSig) ? "_C" : flagSig;
            if (!sig.StartsWith("_", StringComparison.Ordinal))
            {
                sig = "_" + sig;
            }

            string name = sanitizedSourceFileName;
            if (name.Length > 100) name = name.Substring(0, 100);
            return Path.Combine(vpbCacheDir, name + "_" + sizeStr + "_" + timeStr + sig + ".zvamcache");
        }

        private static string BuildVaMNativeDiskCacheSignature(bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth, int targetHeight, float bumpStrength)
        {
            string sig = (targetWidth > 0 && targetHeight > 0) ? (targetWidth + "_" + targetHeight) : string.Empty;
            if (compress) sig += "_C";
            if (linear) sig += "_L";
            if (isNormalMap) sig += "_N";
            if (createAlphaFromGrayscale) sig += "_A";
            if (createNormalFromBump) sig += "_BN" + bumpStrength;
            if (invert) sig += "_I";
            return sig;
        }

        /// <summary>VaM native .vamcache path for the given load flags (not the diagnostic _1 placeholder).</summary>
        public static string GetVaMNativeDiskCachePath(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f)
        {
            if (string.IsNullOrEmpty(imgPath) || imgPath == "NULL") return null;

            var fileEntry = FileManager.GetFileEntry(imgPath);
            string textureCacheDir = MVR.FileManagement.CacheManager.GetTextureCacheDir();
            if (fileEntry == null || string.IsNullOrEmpty(textureCacheDir)) return null;

            string fileName = System.IO.Path.GetFileName(imgPath);
            if (string.IsNullOrEmpty(fileName)) return null;
            fileName = fileName.Replace('.', '_');

            string sig = BuildVaMNativeDiskCacheSignature(compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength);
            return textureCacheDir + "/" + fileName + "_" + fileEntry.Size + "_" + fileEntry.LastWriteTime.ToFileTime() + "_" + sig + ".vamcache";
        }

        public static bool VaMNativeDiskCacheExists(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f)
        {
            return !string.IsNullOrEmpty(FindVaMNativeDiskCachePath(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength));
        }

        private static bool VaMNativeCacheFilePairExists(string cachePath)
        {
            if (string.IsNullOrEmpty(cachePath)) return false;
            try
            {
                if (FileManager.FileExists(cachePath)
                    && FileManager.FileExists(cachePath + "meta"))
                {
                    return true;
                }
            }
            catch { }
            return File.Exists(cachePath) && File.Exists(cachePath + "meta");
        }

        /// <summary>Resolve VaM native .vamcache with flag fallbacks (LUT linear, alpha _A, sim compress).</summary>
        public static string FindVaMNativeDiskCachePath(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f)
        {
            string path = GetVaMNativeDiskCachePath(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength);
            if (!string.IsNullOrEmpty(path) && VaMNativeCacheFilePairExists(path)) return path;

            if (SuperControllerHook.IsLutTexturePath(imgPath) && !linear)
            {
                path = GetVaMNativeDiskCachePath(imgPath, false, true, false, false, false, false, targetWidth, targetHeight, bumpStrength);
                if (!string.IsNullOrEmpty(path) && VaMNativeCacheFilePairExists(path)) return path;
            }

            if (createAlphaFromGrayscale)
            {
                path = GetVaMNativeDiskCachePath(imgPath, compress, linear, isNormalMap, true, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength);
                if (!string.IsNullOrEmpty(path) && VaMNativeCacheFilePairExists(path)) return path;
            }

            if (SuperControllerHook.IsSimulationTexturePath(imgPath))
            {
                path = GetVaMNativeDiskCachePath(imgPath, true, false, false, false, false, false, targetWidth, targetHeight, bumpStrength);
                if (!string.IsNullOrEmpty(path) && VaMNativeCacheFilePairExists(path)) return path;
            }

            return null;
        }

        private static bool TryGetZstdCachePathTokens(
            string imgPath,
            bool compress,
            bool linear,
            bool isNormalMap,
            bool createAlphaFromGrayscale,
            bool createNormalFromBump,
            bool invert,
            int targetWidth,
            int targetHeight,
            float bumpStrength,
            bool isReadable,
            out string cacheDir,
            out string fileName,
            out string sizeStr,
            out string timeStr,
            out string sig)
        {
            cacheDir = null;
            fileName = null;
            sizeStr = null;
            timeStr = null;
            sig = null;

            if (string.IsNullOrEmpty(imgPath) || imgPath == "NULL") return false;

            var fileEntry = FileManager.GetFileEntry(imgPath);
            if (fileEntry == null) return false;

            cacheDir = VamHookPlugin.GetCacheDir();
            fileName = SanitizeFileName(System.IO.Path.GetFileName(imgPath)).Replace('.', '_');
            if (fileName.Length > 100) fileName = fileName.Substring(0, 100);

            sizeStr = GetFileEntryCacheSizeToken(fileEntry).ToString();
            timeStr = fileEntry.LastWriteTime.ToFileTime().ToString();

            sig = "";
            if (targetWidth > 0 && targetHeight > 0) sig += $"{targetWidth}_{targetHeight}";
            if (isReadable) sig += "_R";
            if (compress) sig += "_C";
            if (linear) sig += "_L";
            if (isNormalMap) sig += "_N";
            if (createAlphaFromGrayscale) sig += "_A";
            if (createNormalFromBump) sig += "_BN" + bumpStrength;
            if (invert) sig += "_I";
            return true;
        }

        private static string CombineZstdCachePath(string cacheDir, string fileName, string sizeStr, string timeStr, string sig)
        {
            return System.IO.Path.Combine(cacheDir, fileName + "_" + sizeStr + "_" + timeStr + sig + ".zvamcache");
        }

        /// <summary>Canonical zstd path for current FileEntry; no variant/size-agnostic aliasing (on-demand writes).</summary>
        public static string BuildExactZstdCachePath(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f, bool isReadable = false)
        {
            if (!TryGetZstdCachePathTokens(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength, isReadable,
                out string cacheDir, out string fileName, out string sizeStr, out string timeStr, out string sig))
            {
                return null;
            }

            return CombineZstdCachePath(cacheDir, fileName, sizeStr, timeStr, sig);
        }

        /// <summary>On-disk zstd only when exact FileEntry size/time/sig matches (on-demand cache-hit test).</summary>
        public static string FindExactZstdCacheFileOnDisk(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f, bool isReadable = false)
        {
            if (!TryGetZstdCachePathTokens(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength, isReadable,
                out string cacheDir, out string fileName, out string sizeStr, out string timeStr, out string sig))
            {
                return null;
            }

            string exactPath = CombineZstdCachePath(cacheDir, fileName, sizeStr, timeStr, sig);
            if (File.Exists(exactPath) && File.Exists(exactPath + "meta")) return exactPath;

            bool isSimReq = isReadable || SuperControllerHook.IsSimulationTexturePath(imgPath);
            if (isSimReq && isReadable)
            {
                string sigNoR = sig.Replace("_R", string.Empty);
                string altPath = CombineZstdCachePath(cacheDir, fileName, sizeStr, timeStr, sigNoR);
                if (File.Exists(altPath) && File.Exists(altPath + "meta")) return altPath;
            }

            return null;
        }

        private static string TryResolveZstdCacheOnDisk(string cacheDir, string fileName, string sizeStr, string timeStr, string sig, bool isSimReq, bool isReadable)
        {
            string finalPath = CombineZstdCachePath(cacheDir, fileName, sizeStr, timeStr, sig);
            finalPath = TryResolveExistingZstdVariant(cacheDir, fileName, sizeStr, timeStr, sig, finalPath);
            if (File.Exists(finalPath) && File.Exists(finalPath + "meta")) return finalPath;

            if (isSimReq && isReadable)
            {
                string sigNoR = sig.Replace("_R", string.Empty);
                string altPath = CombineZstdCachePath(cacheDir, fileName, sizeStr, timeStr, sigNoR);
                altPath = TryResolveExistingZstdVariant(cacheDir, fileName, sizeStr, timeStr, sigNoR, altPath);
                if (File.Exists(altPath) && File.Exists(altPath + "meta")) return altPath;
            }

            return null;
        }

        /// <summary>Locate an on-disk .zvamcache without applying serve-time sim/readable blocking rules.</summary>
        public static string FindZstdCacheFileOnDisk(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f, bool isReadable = false)
        {
            if (string.IsNullOrEmpty(imgPath) || imgPath == "NULL") return null;

            bool isSimReq = (isReadable || SuperControllerHook.IsSimulationTexturePath(imgPath));

            if (!TryGetZstdCachePathTokens(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength, isReadable,
                out string cacheDir, out string fileName, out string sizeStr, out string timeStr, out string sig))
            {
                return null;
            }

            string found = TryResolveZstdCacheOnDisk(cacheDir, fileName, sizeStr, timeStr, sig, isSimReq, isReadable);
            if (!string.IsNullOrEmpty(found)) return found;

            // Pre-refactor caches may have used package Size instead of EntrySize; VPB index resolves both.
            try
            {
                var fileEntry = FileManager.GetFileEntry(imgPath);
                if (fileEntry != null)
                {
                    string legacySizeStr = fileEntry.Size.ToString();
                    if (!string.Equals(legacySizeStr, sizeStr, StringComparison.Ordinal))
                    {
                        found = TryResolveZstdCacheOnDisk(cacheDir, fileName, legacySizeStr, timeStr, sig, isSimReq, isReadable);
                        if (!string.IsNullOrEmpty(found)) return found;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>Exact zstd path for runtime serve or canonical write target on miss (no fuzzy sig aliasing).</summary>
        public static string GetZstdCachePath(string imgPath, bool compress, bool linear, bool isNormalMap, bool createAlphaFromGrayscale, bool createNormalFromBump, bool invert, int targetWidth = 0, int targetHeight = 0, float bumpStrength = 1f, bool isReadable = false)
        {
            if (string.IsNullOrEmpty(imgPath) || imgPath == "NULL") return null;

            string finalPath = ResolveServeZstdCachePath(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength, isReadable);
            if (!string.IsNullOrEmpty(finalPath)) return finalPath;

            finalPath = BuildExactZstdCachePath(imgPath, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, invert, targetWidth, targetHeight, bumpStrength, isReadable);

            if (Settings.Instance != null && Settings.Instance.TextureLogLevel != null && Settings.Instance.TextureLogLevel.Value >= 2
                && !string.IsNullOrEmpty(finalPath) && !File.Exists(finalPath)
                && !NativeTextureOnDemandCache.SuppressZstdMissLookupLog)
            {
                LogUtil.LogTextureTrace("ZstdCacheMiss:" + finalPath, "[VPB] Cache MISS lookup: " + System.IO.Path.GetFileName(finalPath) + " for " + System.IO.Path.GetFileName(imgPath));
            }

            return finalPath;
        }

        public static string GetNativeCachePath(string imgPath)
        {
            return GetVaMNativeDiskCachePath(imgPath, true, false, false, false, false, false, 0, 0, 1f);
        }
    }
}
