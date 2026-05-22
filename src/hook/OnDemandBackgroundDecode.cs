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
            bool invert)
        {
            var job = new Job();
            if (string.IsNullOrEmpty(imgUidPath))
            {
                job.State = StateFailed;
                job.Error = "Empty image path";
                return job;
            }

            ThreadPool.QueueUserWorkItem(_ => Run(job, imgUidPath, setSize, setWidth, setHeight, compress, linear, isNormalMap, createAlphaFromGrayscale, createNormalFromBump, bumpStrength, invert));
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
            try
            {
                if (!FileManager.FileExists(imgUidPath))
                {
                    job.State = StateFailed;
                    job.Error = "Path not found: " + imgUidPath;
                    return;
                }

                byte[] fileBytes = FileManager.ReadAllBytes(imgUidPath);
                if (fileBytes == null || fileBytes.Length == 0)
                {
                    job.State = StateFailed;
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
                        job.State = StateFailed;
                        job.Error = decodeErr ?? "VaM decode failed";
                        return;
                    }

                    job.Raw = raw;
                    job.RawLength = rawLen;
                    job.Width = w;
                    job.Height = h;
                    job.Format = fmt;
                    job.State = StateSucceeded;
                }
            }
            catch (Exception ex)
            {
                job.State = StateFailed;
                job.Error = ex.Message;
            }
        }
    }
}
