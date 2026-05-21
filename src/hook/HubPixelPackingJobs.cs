using System;
using System.Threading;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace VPB
{
    internal static class HubPixelPackingJobs
    {
        private static int _validationRuns;
        private static int _successLogCount;

        private struct PackColor32ToRgba32Job : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> Source;
            [WriteOnly] public NativeArray<byte> Destination;

            public void Execute(int index)
            {
                int dst = index * 4;
                Color32 c = Source[index];
                Destination[dst] = c.r;
                Destination[dst + 1] = c.g;
                Destination[dst + 2] = c.b;
                Destination[dst + 3] = c.a;
            }
        }

        public static bool TryPackColor32ToRgba32(Color32[] sourcePixels, byte[] destinationRaw, out string error)
        {
            error = null;

            if (sourcePixels == null || destinationRaw == null)
            {
                error = "null input";
                return false;
            }

            int expectedBytes = sourcePixels.Length * 4;
            if (destinationRaw.Length < expectedBytes)
            {
                error = "destination too small";
                return false;
            }

            NativeArray<Color32> source = default(NativeArray<Color32>);
            NativeArray<byte> destination = default(NativeArray<byte>);
            try
            {
                source = new NativeArray<Color32>(sourcePixels, Allocator.TempJob);
                destination = new NativeArray<byte>(expectedBytes, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                var job = new PackColor32ToRgba32Job
                {
                    Source = source,
                    Destination = destination
                };

                JobHandle handle = job.Schedule(source.Length, 64);
                handle.Complete();
                destination.CopyTo(destinationRaw);

                if (ShouldValidate() && !MatchesManagedPacking(sourcePixels, destinationRaw))
                {
                    error = "validation mismatch";
                    return false;
                }

                MaybeLogSuccess(sourcePixels.Length);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (destination.IsCreated) destination.Dispose();
                if (source.IsCreated) source.Dispose();
            }
        }

        private static bool ShouldValidate()
        {
            // Keep validation cheap: only run during debug-style image queue logging.
            if (_validationRuns >= 3) return false;
            if (Settings.Instance == null || Settings.Instance.LogImageQueueEvents == null || !Settings.Instance.LogImageQueueEvents.Value) return false;
            _validationRuns++;
            return true;
        }

        private static bool MatchesManagedPacking(Color32[] sourcePixels, byte[] packed)
        {
            int len = sourcePixels.Length;
            for (int i = 0; i < len; i++)
            {
                int idx = i * 4;
                Color32 c = sourcePixels[i];
                if (packed[idx] != c.r || packed[idx + 1] != c.g || packed[idx + 2] != c.b || packed[idx + 3] != c.a)
                    return false;
            }
            return true;
        }

        private static void MaybeLogSuccess(int pixels)
        {
            if (Settings.Instance == null || Settings.Instance.LogImageQueueEvents == null || !Settings.Instance.LogImageQueueEvents.Value)
                return;

            if (Interlocked.Increment(ref _successLogCount) == 1)
            {
                LogUtil.Log("[VPB] Hub pixel packing via Job System active. First packed image pixels=" + pixels);
            }
        }
    }
}
