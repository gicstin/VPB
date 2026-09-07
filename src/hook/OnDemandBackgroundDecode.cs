using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// GDI+ / file read for on-demand cache prewarm off main thread; Unity compress + disk write stays on main thread.
    /// </summary>
    internal static class OnDemandBackgroundDecode
    {
        internal sealed class Job
        {
            internal volatile int State;
            internal string Error;
            internal byte[] Raw;
            internal int RawLength;
            internal int Width;
            internal int Height;
            internal TextureFormat Format;
            internal OnDemandZstdWriteQueue.Reservation Memory;
            private bool abandoned;

            internal byte[] ClaimRaw()
            {
                lock (this)
                {
                    if (abandoned || !Success || !Memory.IsCurrent) return null;
                    byte[] result = Raw;
                    Raw = null;
                    return result;
                }
            }

            internal void Abandon()
            {
                lock (this) { abandoned = true; Raw = null; }
            }

            internal void Complete(int state)
            {
                lock (this)
                {
                    if (abandoned || !Memory.IsCurrent) Raw = null;
                    State = state;
                }
            }

            internal bool IsDone => State != 0;
            internal bool Success => State == 2;
        }

        private const int StatePending = 0;
        private const int StateFailed = 1;
        private const int StateSucceeded = 2;

        internal static Job Start(
            string imgUidPath,
            bool setSize,
            int setWidth,
            int setHeight,
            bool compress,
            bool linear,
            bool isNormalMap,
            bool createAlphaFromGrayscale,
            bool createNormalFromBump,
            float bumpStrength,
            bool invert,
            OnDemandZstdWriteQueue.Reservation memory)
        {
            var job = new Job { Memory = memory };
            if (string.IsNullOrEmpty(imgUidPath))
            {
                job.State = StateFailed;
                job.Error = "Empty image path";
                return job;
            }

            if (memory == null || !memory.HoldWorker())
            {
                job.Error = "Cancelled";
                job.State = StateFailed;
                return job;
            }
            bool queued = false;
            try { queued = ThreadPool.QueueUserWorkItem(_ => Run(job, imgUidPath, setSize, setWidth, setHeight, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, bumpStrength, invert)); }
            catch (Exception ex) { job.Error = ex.Message; }
            if (!queued)
            {
                job.Error = job.Error ?? "Decode worker submission failed";
                job.Complete(StateFailed);
                memory.ReleaseWorker();
            }
            return job;
        }

        private static void Run(
            Job job,
            string imgUidPath,
            bool setSize,
            int setWidth,
            int setHeight,
            bool compress,
            bool linear,
            bool isNormalMap,
            bool createAlphaFromGrayscale,
            bool createNormalFromBump,
            float bumpStrength,
            bool invert)
        {
            int completion = StateFailed;
            try
            {
                if (!job.Memory.IsCurrent) return;
                if (!FileManager.FileExists(imgUidPath))
                {
                    job.Error = "Path not found: " + imgUidPath;
                    return;
                }

                byte[] fileBytes = FileManager.ReadAllBytes(imgUidPath);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    job.Error = "Empty file: " + imgUidPath;
                    return;
                }

                using (var ms = new MemoryStream(fileBytes, false))
                {
                    string decodeErr;
                    int w;
                    int h;
                    TextureFormat fmt;
                    byte[] raw;
                    int rawLen;
                    if (!VaMCompatibleImageDecode.TryDecodeFromStream(
                            ms,
                            setSize,
                            setWidth,
                            setHeight,
                            fillBackground: false,
                            compress,
                            isNormalMap,
                            createAlphaFromGrayscale,
                            createNormalFromBump,
                            bumpStrength,
                            invert,
                            out w,
                            out h,
                            out fmt,
                            out raw,
                            out rawLen,
                            out decodeErr))
                    {
                        job.Error = decodeErr ?? "VaM decode failed";
                        return;
                    }

                    job.Raw = raw;
                    job.RawLength = rawLen;
                    job.Width = w;
                    job.Height = h;
                    job.Format = fmt;
                    completion = StateSucceeded;
                }
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
            }
            finally
            {
                job.Complete(completion);
                job.Memory.ReleaseWorker();
            }
        }
    }
}
