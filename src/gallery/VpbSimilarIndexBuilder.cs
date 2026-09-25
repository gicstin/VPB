using System;
using System.Diagnostics;
using System.Threading;

namespace VPB
{
    internal static class VpbSimilarIndexBuilder
    {
        private const int ContendedWriteAttempts = 3;
        private const int ContendedWriteBackoffMs = 4000;

        private static readonly object Sync = new object();
        private static bool s_Running;
        private static bool s_AbortRequested;
        private static bool s_Registered;
        private static bool s_LastBuildFailed;

        internal static bool IsRunning { get { lock (Sync) { return s_Running; } } }

        internal static bool LastBuildFailed { get { lock (Sync) { return s_LastBuildFailed; } } }

        internal static void RequestAbort()
        {
            lock (Sync) { s_AbortRequested = true; }
        }

        internal static void EnsureBuiltInBackground()
        {
            lock (Sync)
            {
                if (s_Running) return;
                if (!s_Registered)
                {
                    s_Registered = true;
                    try { VpbShutdown.Register("SimilarIndexBuilder", RequestAbort); } catch { }
                }
                s_Running = true;
                s_AbortRequested = false;
            }

            try
            {
                ThreadPool.QueueUserWorkItem(RunBuild);
            }
            catch (Exception ex)
            {
                lock (Sync) { s_Running = false; }
                try { LogUtil.LogWarning("[VPB] similar index: could not queue build: " + ex.Message); } catch { }
            }
        }

        private static bool ShouldAbort()
        {
            if (VpbShutdown.IsQuitting) return true;
            lock (Sync) { return s_AbortRequested; }
        }

        private static void RunBuild(object _)
        {
            bool ok = false;
            int seeds = 0, rows = 0;
            var watch = Stopwatch.StartNew();
            try
            {
                if (ShouldAbort()) return;

                string storedSignature, currentSignature;
                SimilarIndexState state = VpbLocalDatabase.GetSimilarIndexState(out storedSignature, out currentSignature);
                if (state == SimilarIndexState.Ready) { ok = true; return; }
                try
                {
                    LogUtil.Log("[VPB] similar index rebuild: state=" + state
                        + " stored_sig=" + (storedSignature ?? "none")
                        + " current_sig=" + (currentSignature ?? "none"));
                }
                catch { }

                for (int attempt = 0; attempt < ContendedWriteAttempts && !ok; attempt++)
                {
                    if (attempt > 0 && !VpbShutdown.SleepOrQuit(ContendedWriteBackoffMs * attempt)) return;
                    if (ShouldAbort()) return;
                    ok = VpbLocalDatabase.BuildSimilarIndex(ShouldAbort, out seeds, out rows);
                }
                if (ok)
                {
                    try
                    {
                        LogUtil.Log("[VPB] similar index built: seeds=" + seeds
                            + " rows=" + rows
                            + " in " + watch.ElapsedMilliseconds + "ms");
                    }
                    catch { }
                }
                else if (!ShouldAbort())
                {
                    try { LogUtil.LogWarning("[VPB] similar index build produced no rows"); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] similar index build threw: " + ex.Message); } catch { }
            }
            finally
            {
                lock (Sync)
                {
                    s_Running = false;
                    s_LastBuildFailed = !ok && !s_AbortRequested;
                }
            }
        }
    }
}
