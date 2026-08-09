using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SimpleJSON;
using UnityEngine;
using ZstdNet;

namespace VPB
{
    /// <summary>
    /// Background zstd compress + disk write for on-demand cache builds.
    /// Keeps Unity Finish on main thread; overlaps CPU compress/IO with next decode.
    /// Shares busy-path registration with <see cref="ImageLoadingMgr"/> so runtime loads wait for .tmp.
    /// </summary>
    internal static class OnDemandZstdWriteQueue
    {
        private sealed class Job
        {
            public string ZstdPath;
            /// <summary>In-memory DXT/raw payload. Null when <see cref="NativeSourcePath"/> set.</summary>
            public byte[] Payload;
            /// <summary>Stream-compress existing .vamcache (avoids GetRawTextureData LOH copy).</summary>
            public string NativeSourcePath;
            public string NativeMetaPath;
            public int Width;
            public int Height;
            public TextureFormat Format;
            public bool IsReadable;
            public bool CreateMipMaps;
            public bool DidDownscale;
            public int SourceWidth;
            public int SourceHeight;
            public int Level;
            public bool Rewrite;
            public string DeleteNativePathAfterSuccess;
        }

        private static readonly object QueueLock = new object();
        private static readonly Queue<Job> Queue = new Queue<Job>(64);
        private static int s_Queued;
        private static int s_ActiveWriters;
        private static int s_MaxWriters = 2;
        private static volatile bool s_Cancel;

        [ThreadStatic]
        private static Compressor s_ThreadCompressor;
        [ThreadStatic]
        private static int s_ThreadCompressorLevel = int.MinValue;

        internal static int PendingCount
        {
            get { return Thread.VolatileRead(ref s_Queued) + Thread.VolatileRead(ref s_ActiveWriters); }
        }

        internal static bool IsIdle
        {
            get { return PendingCount <= 0; }
        }

        internal static void BeginJobSession()
        {
            s_Cancel = false;
            s_MaxWriters = ResolveMaxWriters();
            TryBoostThreadPool();
        }

        private static void TryBoostThreadPool()
        {
            try
            {
                int workers;
                int ios;
                ThreadPool.GetMinThreads(out workers, out ios);
                int want = s_MaxWriters + OnDemandMaxParallelImagesHint() + 2;
                if (want < 8) want = 8;
                if (want > 32) want = 32;
                if (want > workers)
                    ThreadPool.SetMinThreads(want, ios);

                ThreadPool.GetMaxThreads(out workers, out ios);
                if (want > workers)
                    ThreadPool.SetMaxThreads(want, ios);
            }
            catch { }
        }

        private static int OnDemandMaxParallelImagesHint()
        {
            try
            {
                int n = CustomImageLoaderThreaded.GetEffectiveMaxLoaderThreads();
                if (n < 4) n = 4;
                if (n > 10) n = 10;
                return n;
            }
            catch { return 6; }
        }

        internal static void RequestCancel()
        {
            s_Cancel = true;
            lock (QueueLock)
            {
                while (Queue.Count > 0)
                {
                    Job dropped = Queue.Dequeue();
                    Interlocked.Decrement(ref s_Queued);
                    ReleaseJob(dropped);
                }
            }
        }

        /// <summary>Enqueue payload ownership transfer. Returns false if path busy/cancel/invalid.</summary>
        internal static bool TryEnqueue(
            string zstdPath,
            byte[] payload,
            int width,
            int height,
            TextureFormat format,
            bool isReadable,
            bool createMipMaps,
            bool didDownscale,
            int sourceWidth,
            int sourceHeight,
            int level,
            bool rewrite,
            string deleteNativePathAfterSuccess)
        {
            if (s_Cancel || string.IsNullOrEmpty(zstdPath) || payload == null || payload.Length == 0 || width <= 0 || height <= 0)
                return false;

            if (!ImageLoadingMgr.TryAcquireZstdWritePath(zstdPath))
                return false;

            var job = new Job
            {
                ZstdPath = zstdPath,
                Payload = payload,
                NativeSourcePath = null,
                NativeMetaPath = null,
                Width = width,
                Height = height,
                Format = format,
                IsReadable = isReadable,
                CreateMipMaps = createMipMaps,
                DidDownscale = didDownscale,
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                Level = level,
                Rewrite = rewrite,
                DeleteNativePathAfterSuccess = deleteNativePathAfterSuccess
            };

            EnqueueJob(job);
            return true;
        }

        /// <summary>
        /// Stream-compress native .vamcache → .zvamcache on worker (same as Settings Compress Cache).
        /// Prefer this over memory Wrap — no GetRawTextureData, lower GC, external zstd can use multi-core.
        /// </summary>
        internal static bool TryEnqueueFromNativeFile(
            string zstdPath,
            string nativePath,
            string nativeMetaPath,
            int width,
            int height,
            TextureFormat format,
            bool isReadable,
            bool createMipMaps,
            bool didDownscale,
            int sourceWidth,
            int sourceHeight,
            int level,
            bool rewrite,
            string deleteNativePathAfterSuccess)
        {
            if (s_Cancel || string.IsNullOrEmpty(zstdPath) || string.IsNullOrEmpty(nativePath))
                return false;
            if (!File.Exists(nativePath))
                return false;

            if (!ImageLoadingMgr.TryAcquireZstdWritePath(zstdPath))
                return false;

            var job = new Job
            {
                ZstdPath = zstdPath,
                Payload = null,
                NativeSourcePath = nativePath,
                NativeMetaPath = nativeMetaPath,
                Width = width,
                Height = height,
                Format = format,
                IsReadable = isReadable,
                CreateMipMaps = createMipMaps,
                DidDownscale = didDownscale,
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                Level = level,
                Rewrite = rewrite,
                DeleteNativePathAfterSuccess = deleteNativePathAfterSuccess
            };

            EnqueueJob(job);
            return true;
        }

        private static void EnqueueJob(Job job)
        {
            lock (QueueLock)
            {
                Queue.Enqueue(job);
                Interlocked.Increment(ref s_Queued);
            }
            TrySpawnWriters();
        }

        internal static IEnumerator CoWaitUntilIdle()
        {
            float spinStart = Time.realtimeSinceStartup;
            while (!IsIdle)
            {
                if (s_Cancel && Thread.VolatileRead(ref s_ActiveWriters) <= 0)
                {
                    RequestCancel();
                    yield break;
                }

                if (Time.realtimeSinceStartup - spinStart > 600f)
                {
                    try { LogUtil.LogWarning("[VPB OnDemand] Zstd write queue wait timed out; pending=" + PendingCount); } catch { }
                    yield break;
                }

                yield return null;
            }
        }

        private static int ResolveMaxWriters()
        {
            try
            {
                int p = Environment.ProcessorCount;
                if (p < 1) p = 1;
                // Keep several compress+write cores busy while main Finishes next textures.
                int n = (p * 3) / 4;
                if (n < 4) n = 4;
                if (n > 12) n = 12;
                return n;
            }
            catch
            {
                return 3;
            }
        }

        private static void TrySpawnWriters()
        {
            while (true)
            {
                Job job = null;
                lock (QueueLock)
                {
                    if (Queue.Count == 0) return;
                    if (Thread.VolatileRead(ref s_ActiveWriters) >= s_MaxWriters) return;
                    job = Queue.Dequeue();
                    Interlocked.Decrement(ref s_Queued);
                    Interlocked.Increment(ref s_ActiveWriters);
                }

                Job captured = job;
                ThreadPool.QueueUserWorkItem(_ => RunWriter(captured));
            }
        }

        private static void RunWriter(Job job)
        {
            try
            {
                if (job == null) return;
                if (s_Cancel)
                {
                    ReleaseJob(job);
                    NativeTextureOnDemandCache.NotifyZstdWriteFailed();
                    return;
                }

                int originalLen = 0;
                if (job.Payload != null) originalLen = job.Payload.Length;
                else if (!string.IsNullOrEmpty(job.NativeSourcePath))
                {
                    try { originalLen = (int)Math.Min(int.MaxValue, new FileInfo(job.NativeSourcePath).Length); } catch { }
                }

                long compressedLen;
                long diskBytes;
                bool ok = WriteJob(job, out compressedLen, out diskBytes);
                if (ok)
                {
                    NativeTextureOnDemandCache.NotifyZstdWriteCompleted(
                        originalLen,
                        compressedLen,
                        diskBytes,
                        job.ZstdPath,
                        job.Rewrite,
                        job.DidDownscale,
                        job.SourceWidth,
                        job.SourceHeight,
                        job.Format,
                        job.DeleteNativePathAfterSuccess);
                }
                else
                {
                    NativeTextureOnDemandCache.NotifyZstdWriteFailed();
                }

                ReleaseJob(job);
            }
            catch
            {
                try { NativeTextureOnDemandCache.NotifyZstdWriteFailed(); } catch { }
                try { ReleaseJob(job); } catch { }
            }
            finally
            {
                Interlocked.Decrement(ref s_ActiveWriters);
                if (!s_Cancel)
                    TrySpawnWriters();
            }
        }

        private static bool WriteJob(Job job, out long compressedLen, out long diskBytes)
        {
            compressedLen = 0;
            diskBytes = 0;
            string zstdPath = job.ZstdPath;
            if (string.IsNullOrEmpty(zstdPath)) return false;

            // Prefer streaming native file → zstd (Bulk Compress Cache path).
            if (!string.IsNullOrEmpty(job.NativeSourcePath))
            {
                try
                {
                    if (!File.Exists(job.NativeSourcePath)) return false;
                    long rawLen = new FileInfo(job.NativeSourcePath).Length;
                    ZstdCompressor.SaveCacheFromFile(zstdPath, job.NativeSourcePath, job.Level);
                    if (!File.Exists(zstdPath)) return false;
                    compressedLen = new FileInfo(zstdPath).Length;

                    WriteOrCopyMeta(job, rawLen);

                    try
                    {
                        diskBytes = compressedLen;
                        string zmetaDisk = zstdPath + "meta";
                        if (File.Exists(zmetaDisk))
                            diskBytes += new FileInfo(zmetaDisk).Length;
                    }
                    catch { diskBytes = compressedLen; }

                    return true;
                }
                catch (Exception ex)
                {
                    try { LogUtil.LogWarning("[VPB OnDemand] Zstd file compress failed: " + zstdPath + " | " + ex.Message); } catch { }
                    return false;
                }
            }

            byte[] raw = job.Payload;
            if (raw == null || raw.Length == 0) return false;

            byte[] compressed = CompressWithThreadCompressor(raw, job.Level);
            if (compressed == null || compressed.Length == 0) return false;
            compressedLen = compressed.Length;

            try
            {
                string zdir = Path.GetDirectoryName(zstdPath);
                if (!string.IsNullOrEmpty(zdir) && !Directory.Exists(zdir)) Directory.CreateDirectory(zdir);
            }
            catch { }

            string dataTmp = zstdPath + ".tmp";
            string metaTmp = zstdPath + ".meta.tmp";

            try
            {
                try { if (File.Exists(dataTmp)) File.Delete(dataTmp); } catch { }
                try { if (File.Exists(metaTmp)) File.Delete(metaTmp); } catch { }

                File.WriteAllBytes(dataTmp, compressed);

                var zmeta = BuildZstdMeta(job, raw.Length);
                File.WriteAllText(metaTmp, VPB.src.util.JsonSerializationUtil.Serialize(zmeta, 1024));

                try { if (File.Exists(zstdPath + "meta")) File.Delete(zstdPath + "meta"); } catch { }
                try { if (File.Exists(zstdPath)) File.Delete(zstdPath); } catch { }

                File.Move(metaTmp, zstdPath + "meta");
                File.Move(dataTmp, zstdPath);

                try
                {
                    diskBytes = new FileInfo(zstdPath).Length;
                    string zmetaDisk = zstdPath + "meta";
                    if (File.Exists(zmetaDisk))
                        diskBytes += new FileInfo(zmetaDisk).Length;
                }
                catch { diskBytes = compressedLen; }

                return true;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB OnDemand] Zstd write failed: " + zstdPath + " | " + ex.Message); } catch { }
                try { if (File.Exists(dataTmp)) File.Delete(dataTmp); } catch { }
                try { if (File.Exists(metaTmp)) File.Delete(metaTmp); } catch { }
                return false;
            }
        }

        private static void WriteOrCopyMeta(Job job, long rawByteLength)
        {
            string zstdPath = job.ZstdPath;
            string metaTmp = zstdPath + ".meta.tmp";
            try
            {
                JSONNode zmeta = null;
                if (!string.IsNullOrEmpty(job.NativeMetaPath) && File.Exists(job.NativeMetaPath))
                {
                    try { zmeta = JSON.Parse(File.ReadAllText(job.NativeMetaPath)); } catch { zmeta = null; }
                }
                if (zmeta == null) zmeta = BuildZstdMeta(job, (int)Math.Min(int.MaxValue, rawByteLength));
                else
                {
                    zmeta["type"] = "compressed";
                    zmeta["zstdLevel"].AsInt = job.Level;
                    if (job.DidDownscale)
                    {
                        zmeta["downscaled"].AsBool = true;
                        zmeta["sourceWidth"] = job.SourceWidth.ToString();
                        zmeta["sourceHeight"] = job.SourceHeight.ToString();
                    }
                    if (job.Width > 0) zmeta["width"] = job.Width.ToString();
                    if (job.Height > 0) zmeta["height"] = job.Height.ToString();
                    try
                    {
                        TextureUtil.WriteMipFieldsToMeta(zmeta, job.Width, job.Height, job.Format, (int)Math.Min(int.MaxValue, rawByteLength), job.CreateMipMaps);
                    }
                    catch { }
                }

                File.WriteAllText(metaTmp, VPB.src.util.JsonSerializationUtil.Serialize(zmeta, 1024));
                try { if (File.Exists(zstdPath + "meta")) File.Delete(zstdPath + "meta"); } catch { }
                File.Move(metaTmp, zstdPath + "meta");
            }
            catch
            {
                try { if (File.Exists(metaTmp)) File.Delete(metaTmp); } catch { }
                try
                {
                    if (!string.IsNullOrEmpty(job.NativeMetaPath) && File.Exists(job.NativeMetaPath))
                        File.Copy(job.NativeMetaPath, zstdPath + "meta", true);
                }
                catch { }
            }
        }

        private static JSONClass BuildZstdMeta(Job job, int rawLength)
        {
            var zmeta = new JSONClass();
            zmeta["type"] = "compressed";
            zmeta["width"] = job.Width.ToString();
            zmeta["height"] = job.Height.ToString();
            zmeta["format"] = job.Format.ToString();
            if (job.IsReadable) zmeta["isReadable"] = "true";
            if (job.DidDownscale)
            {
                zmeta["downscaled"].AsBool = true;
                zmeta["sourceWidth"] = job.SourceWidth.ToString();
                zmeta["sourceHeight"] = job.SourceHeight.ToString();
            }
            zmeta["zstdLevel"].AsInt = job.Level;
            TextureUtil.WriteMipFieldsToMeta(zmeta, job.Width, job.Height, job.Format, rawLength, job.CreateMipMaps);
            return zmeta;
        }

        private static byte[] CompressWithThreadCompressor(byte[] raw, int level)
        {
            if (raw == null || raw.Length == 0) return null;
            if (level < 1) level = 1;
            if (level > 22) level = 22;

            try
            {
                NativeTextureOnDemandCache.EnsureZstdInitialized();
                if (s_ThreadCompressor == null || s_ThreadCompressorLevel != level)
                {
                    if (s_ThreadCompressor != null)
                    {
                        try { s_ThreadCompressor.Dispose(); } catch { }
                        s_ThreadCompressor = null;
                    }
                    s_ThreadCompressor = new Compressor(new CompressionOptions(level));
                    s_ThreadCompressorLevel = level;
                }
                return s_ThreadCompressor.Wrap(raw, 0, raw.Length);
            }
            catch
            {
                try { return ZstdCompressor.Compress(raw, level); }
                catch { return null; }
            }
        }

        private static void ReleaseJob(Job job)
        {
            if (job == null) return;
            try { ImageLoadingMgr.ReleaseZstdWritePath(job.ZstdPath); } catch { }
            job.Payload = null;
        }
    }
}
