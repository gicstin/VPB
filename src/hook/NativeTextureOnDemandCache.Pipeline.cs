using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VPB
{
    public static partial class NativeTextureOnDemandCache
    {
        /// <summary>In-flight image coroutines (decode + Finish). Sliding window; RAM-bounded.</summary>
        private const int OnDemandMaxParallelImages = 6;
        /// <summary>Main-thread Finish budget while progress UI open.</summary>
        private const float OnDemandBulkFrameBudgetSec = 0.070f;
        /// <summary>Pause starting new images when zstd queue is deep.</summary>
        private const int OnDemandZstdBackpressureQueued = 24;

        private static long EstimateManagedPass(int width, int height, bool bump, bool zstd)
        {
            checked
            {
                if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width");
                long pixels = (long)width * height;
                long mipBytes = 0;
                int w = width, h = height;
                while (true)
                {
                    mipBytes += (long)w * h * 4;
                    if (w == 1 && h == 1) break;
                    w = Math.Max(1, w / 2); h = Math.Max(1, h / 2);
                }
                // Includes decoder intermediates, fallback pooled copy and both Finish snapshots.
                long raw = pixels * (bump ? 8 : 4);
                if (raw > int.MaxValue || mipBytes > int.MaxValue) throw new OverflowException();
                long bytes = pixels * (bump ? 18 : 6) + height * 32L + 4096;
                bytes += ByteArrayPool.GetRentalSize((int)raw) + 2 * mipBytes;
                if (zstd)
                {
                    ulong bound = ZstdNet.Compressor.GetCompressBoundLong((ulong)mipBytes);
                    if (bound > int.MaxValue) throw new OverflowException();
                    bytes += ByteArrayPool.GetRentalSize((int)bound) + (long)bound;
                }
                return bytes;
            }
        }

        private static long EstimateManagedRoute(string path, string cachePath, TextureFlags flags,
            int width, int height, int secondWidth, int secondHeight, bool zstd, out bool exclusive)
        {
            exclusive = true;
            try
            {
                long sourceBytes = TryGetOnDemandFileEntrySize(path);
                int sw, sh;
                if (sourceBytes <= 0 || !TryReadImageDimensionsPartial(path, out sw, out sh)) return 512L * 1024 * 1024;
                if (width <= 0 || height <= 0)
                {
                    width = flags.compress ? Math.Max(4, sw / 4 * 4) : sw;
                    height = flags.compress ? Math.Max(4, sh / 4 * 4) : sh;
                }
                checked
                {
                    // Sum both passes: original payload remains fallback until downscaled decode succeeds.
                    long bytes = sourceBytes + EstimateManagedPass(width, height, flags.createNormalFromBump, zstd);
                    if (secondWidth > 0 && secondHeight > 0)
                        bytes += sourceBytes + EstimateManagedPass(secondWidth, secondHeight, flags.createNormalFromBump, zstd);
                    if (!string.IsNullOrEmpty(cachePath) && System.IO.File.Exists(cachePath))
                    {
                        long cacheBytes = new System.IO.FileInfo(cachePath).Length;
                        if (cacheBytes <= 0 || cacheBytes > int.MaxValue) return 512L * 1024 * 1024;
                        bytes += 3L * ByteArrayPool.GetRentalSize((int)cacheBytes);
                        if (zstd)
                        {
                            ulong bound = ZstdNet.Compressor.GetCompressBoundLong((ulong)cacheBytes);
                            if (bound > int.MaxValue) return 512L * 1024 * 1024;
                            bytes += ByteArrayPool.GetRentalSize((int)bound) + (long)bound;
                        }
                    }
                    exclusive = false;
                    return bytes;
                }
            }
            catch (Exception)
            {
                // Preserve formats with unknown headers and oversized routes, but run them alone.
                return 512L * 1024 * 1024;
            }
        }

        private static float GetOnDemandFrameBudgetSec()
        {
            return s_UiVisible && !s_UiShowSummary ? OnDemandBulkFrameBudgetSec : OnDemandFrameBudgetSec;
        }

        private static int GetOnDemandParallelImageLimit()
        {
            try
            {
                int loader = CustomImageLoaderThreaded.GetEffectiveMaxLoaderThreads();
                // Match decode worker pool — keep several ThreadPool decodes busy while main Finishes.
                int n = loader;
                if (n < 4) n = 4;
                if (n > OnDemandMaxParallelImages) n = OnDemandMaxParallelImages;
                return n;
            }
            catch
            {
                return 6;
            }
        }

        private sealed class NestedPump
        {
            public readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>(4);

            public NestedPump(IEnumerator root)
            {
                if (root != null) Stack.Push(root);
            }

            public bool IsEmpty { get { return Stack.Count == 0; } }

            public void DisposeAll()
            {
                while (Stack.Count > 0)
                {
                    IEnumerator e = Stack.Pop();
                    try
                    {
                        IDisposable d = e as IDisposable;
                        if (d != null) d.Dispose();
                    }
                    catch { }
                }
            }

            /// <summary>
            /// Advance until nested work yields a frame wait (null / non-enumerator) or completes.
            /// Unity coroutine runner auto-pumps nested IEnumerators; manual MoveNext must do the same.
            /// </summary>
            public bool AdvanceUntilYieldOrDone()
            {
                while (Stack.Count > 0)
                {
                    IEnumerator top = Stack.Peek();
                    bool moved;
                    try
                    {
                        moved = top.MoveNext();
                    }
                    catch
                    {
                        moved = false;
                    }

                    if (!moved)
                    {
                        Stack.Pop();
                        try
                        {
                            IDisposable d = top as IDisposable;
                            if (d != null) d.Dispose();
                        }
                        catch { }
                        continue;
                    }

                    object cur = top.Current;
                    IEnumerator nested = cur as IEnumerator;
                    if (nested != null)
                    {
                        Stack.Push(nested);
                        continue;
                    }

                    // null / WaitForSeconds / etc. — caller yields a frame
                    return true;
                }

                return false;
            }
        }

        private static bool TryStartImageWorker(
            Queue<KeyValuePair<string, string>> pending,
            Dictionary<string, List<TextureFlags>> internalLowerToFlags,
            System.Func<string, string, string> makeImgUidPath,
            List<NestedPump> active)
        {
            while (pending.Count > 0)
            {
                var kv = pending.Dequeue();
                string internalLower = kv.Key;
                string internalPath = kv.Value;
                if (string.IsNullOrEmpty(internalPath)) continue;

                try
                {
                    List<TextureFlags> variants;
                    if (internalLowerToFlags == null || !internalLowerToFlags.TryGetValue(internalLower, out variants) || variants == null || variants.Count == 0)
                    {
                        variants = new List<TextureFlags>
                        {
                            new TextureFlags
                            {
                                compress = true,
                                linear = false,
                                isNormalMap = false,
                                createAlphaFromGrayscale = false,
                                createNormalFromBump = false,
                                invert = false,
                                isReadable = false,
                                bumpStrength = 1f
                            }
                        };
                    }

                    string imgUidPath = makeImgUidPath(internalLower, internalPath);
                    if (!string.IsNullOrEmpty(imgUidPath) && TryFileEntryExists(imgUidPath))
                    {
                        active.Add(new NestedPump(WriteNativeCacheForImageVariantsCoroutine(imgUidPath, internalPath, variants, 0, default(DateTime))));
                        return true;
                    }

                    s_TexturesProcessed++;
                    if (!s_BatchMode) s_ProcessedWork++;
                }
                catch
                {
                    s_TexturesProcessed++;
                    if (!s_BatchMode) s_ProcessedWork++;
                }
            }

            return false;
        }

        private static IEnumerator WorkerBuildSelectivePipelineCoroutine(
            IEnumerable<KeyValuePair<string, string>> internalLowerToOriginal,
            Dictionary<string, List<TextureFlags>> internalLowerToFlags,
            System.Func<string, string, string> makeImgUidPath)
        {
            if (internalLowerToOriginal == null || makeImgUidPath == null) yield break;

            int parallel = GetOnDemandParallelImageLimit();
            var pending = new Queue<KeyValuePair<string, string>>(64);
            foreach (var kv in internalLowerToOriginal)
            {
                if (kv.Value != null) pending.Enqueue(kv);
            }

            if (pending.Count == 0) yield break;

            var active = new List<NestedPump>(parallel);

            int session = OnDemandZstdWriteQueue.CurrentSession;
            try
            {
            // Sliding window: refill as soon as a slot frees — no batch barrier stall.
            while ((pending.Count > 0 || active.Count > 0) && !s_CancelRequested && OnDemandZstdWriteQueue.IsSessionCurrent(session))
            {
                bool zstdBackpressured = OnDemandZstdWriteQueue.PendingCount >= OnDemandZstdBackpressureQueued;
                while (!zstdBackpressured && active.Count < parallel && pending.Count > 0)
                {
                    if (!TryStartImageWorker(pending, internalLowerToFlags, makeImgUidPath, active))
                        break;
                    zstdBackpressured = OnDemandZstdWriteQueue.PendingCount >= OnDemandZstdBackpressureQueued;
                }

                if (active.Count == 0)
                {
                    if (pending.Count == 0) yield break;
                    // Waiting for zstd queue to drain before holding more decode/Finish RAM.
                    yield return null;
                    continue;
                }

                float frameStart = Time.realtimeSinceStartup;
                float budget = GetOnDemandFrameBudgetSec();

                // Multi-pass: Finish + refill in same frame until everyone waits on decode or budget ends.
                bool keepPumping = true;
                while (keepPumping && active.Count > 0 && !s_CancelRequested)
                {
                    keepPumping = false;
                    for (int i = active.Count - 1; i >= 0; i--)
                    {
                        NestedPump pump = active[i];
                        bool waiting;
                        try
                        {
                            waiting = pump.AdvanceUntilYieldOrDone();
                        }
                        catch
                        {
                            waiting = false;
                            try { pump.DisposeAll(); } catch { }
                        }

                        if (!waiting || pump.IsEmpty)
                        {
                            try { pump.DisposeAll(); } catch { }
                            active.RemoveAt(i);
                            s_TexturesProcessed++;
                            if (!s_BatchMode) s_ProcessedWork++;
                            UpdateUiStatusThrottled();
                            keepPumping = true;

                            if (!s_CancelRequested
                                && OnDemandZstdWriteQueue.PendingCount < OnDemandZstdBackpressureQueued
                                && active.Count < parallel
                                && pending.Count > 0)
                            {
                                if (TryStartImageWorker(pending, internalLowerToFlags, makeImgUidPath, active))
                                    keepPumping = true;
                            }
                        }

                        if (Time.realtimeSinceStartup - frameStart >= budget)
                        {
                            keepPumping = false;
                            break;
                        }
                    }
                }

                yield return null;
            }

            }
            finally
            {
                for (int i = 0; i < active.Count; i++)
                {
                    try { active[i].DisposeAll(); } catch { }
                }
                active.Clear();
            }
        }

        private static void EnterSuppressZstdMissLookupLog()
        {
            System.Threading.Interlocked.Increment(ref s_SuppressZstdMissLookupLogCount);
        }

        private static void ExitSuppressZstdMissLookupLog()
        {
            System.Threading.Interlocked.Decrement(ref s_SuppressZstdMissLookupLogCount);
        }

        private static readonly object s_ZstdStatsLock = new object();

        internal static void NotifyZstdWriteFailed()
        {
            lock (s_ZstdStatsLock)
            {
                s_ZstdFails++;
            }
        }

        internal static void NotifyZstdWriteCompleted(
            int originalBytes,
            long compressedBytes,
            long diskBytes,
            string zstdPath,
            bool rewrite,
            bool didDownscale,
            int sourceWidth,
            int sourceHeight,
            TextureFormat format,
            string deleteNativePathAfterSuccess)
        {
            lock (s_ZstdStatsLock)
            {
                s_ZstdWrites++;
                if (rewrite) s_ZstdRewrites++;
                if (originalBytes > 0) s_ZstdOriginalBytes += originalBytes;
                if (compressedBytes > 0) s_ZstdCompressedBytes += compressedBytes;
                if (diskBytes > 0)
                {
                    s_ZstdDiskBytes += diskBytes;
                    s_ZstdDiskBytesWritten += diskBytes;
                }

                if (didDownscale && sourceWidth > 0 && sourceHeight > 0 && originalBytes > 0)
                {
                    try
                    {
                        int expected = TextureUtil.GetExpectedRawDataSize(sourceWidth, sourceHeight, format);
                        if (expected > 0)
                        {
                            long saved = (long)expected - (long)originalBytes;
                            s_ZstdDownscaleWrites++;
                            if (saved > 0) s_ZstdDownscaleSavedBytes += saved;
                            s_ZstdDownscaleOriginalBytes += expected;
                            s_ZstdDownscaleFinalBytes += originalBytes;
                        }
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrEmpty(deleteNativePathAfterSuccess))
            {
                try
                {
                    if (System.IO.File.Exists(deleteNativePathAfterSuccess))
                    {
                        System.IO.File.Delete(deleteNativePathAfterSuccess);
                        lock (s_ZstdStatsLock) { s_NativeDeletes++; }
                    }
                }
                catch { }
                try
                {
                    string meta = deleteNativePathAfterSuccess + "meta";
                    if (System.IO.File.Exists(meta)) System.IO.File.Delete(meta);
                }
                catch { }
            }

            try
            {
                Trace((rewrite ? "ZstdRewriteAsync: path='" : "ZstdWriteAsync: path='")
                    + (zstdPath ?? string.Empty) + "' orig=" + originalBytes + " comp=" + compressedBytes);
            }
            catch { }
        }
    }
}
