using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace VPB
{
    internal static class VpbShutdown
    {
        static volatile bool s_Quitting;
        static readonly ManualResetEvent s_QuitEvent = new ManualResetEvent(false);
        static readonly object s_Sync = new object();
        static readonly List<Entry> s_Stoppers = new List<Entry>();
        static int s_Armed;
        static int s_Begun;

        sealed class Entry
        {
            internal string Name;
            internal Action Stop;
        }

        internal static bool IsQuitting
        {
            get { return s_Quitting; }
        }

        internal static WaitHandle QuitHandle
        {
            get { return s_QuitEvent; }
        }

        internal static void Arm()
        {
            if (Interlocked.Exchange(ref s_Armed, 1) != 0) return;
            try { Application.wantsToQuit += OnWantsToQuit; } catch { }
            try { Application.quitting += OnQuitting; } catch { }
        }

        static bool OnWantsToQuit()
        {
            Begin();
            return true;
        }

        static void OnQuitting()
        {
            Begin();
        }

        internal static void Register(string name, Action stop)
        {
            if (stop == null) return;
            bool runNow;
            lock (s_Sync)
            {
                runNow = s_Quitting;
                if (!runNow)
                {
                    for (int i = 0; i < s_Stoppers.Count; i++)
                    {
                        if (string.Equals(s_Stoppers[i].Name, name, StringComparison.Ordinal))
                            return;
                    }
                    s_Stoppers.Add(new Entry { Name = name, Stop = stop });
                }
            }
            if (runNow)
            {
                try { stop(); } catch { }
            }
        }

        internal static void Begin()
        {
            s_Quitting = true;
            try { s_QuitEvent.Set(); } catch { }
            if (Interlocked.Exchange(ref s_Begun, 1) != 0) return;

            Entry[] list;
            lock (s_Sync)
            {
                list = s_Stoppers.ToArray();
                s_Stoppers.Clear();
            }

            for (int i = list.Length - 1; i >= 0; i--)
            {
                Entry e = list[i];
                if (e == null || e.Stop == null) continue;
                try
                {
                    e.Stop();
                }
                catch (Exception ex)
                {
                    try { LogUtil.LogWarning("[VPB][Shutdown] stopper '" + e.Name + "' failed: " + ex.Message); } catch { }
                }
            }
        }

        internal static bool WaitOrQuit(WaitHandle done)
        {
            return WaitOrQuit(done, Timeout.Infinite);
        }

        internal static bool WaitOrQuit(WaitHandle done, int timeoutMs)
        {
            if (done == null) return true;
            if (s_Quitting) return false;
            try
            {
                int idx = WaitHandle.WaitAny(new WaitHandle[] { done, s_QuitEvent }, timeoutMs, false);
                return idx == 0;
            }
            catch
            {
                return true;
            }
        }

        internal static bool SleepOrQuit(int ms)
        {
            if (s_Quitting) return false;
            try { return !s_QuitEvent.WaitOne(ms, false); }
            catch { return !s_Quitting; }
        }

        internal static bool WaitForIdleOrTimeout(Func<bool> isBusy, int timeoutMs, int pollMs)
        {
            if (isBusy == null) return true;
            if (pollMs < 5) pollMs = 5;
            int waited = 0;
            while (waited < timeoutMs)
            {
                bool busy;
                try { busy = isBusy(); }
                catch { return true; }
                if (!busy) return true;
                try { Thread.Sleep(pollMs); } catch { }
                waited += pollMs;
            }
            try { return !isBusy(); }
            catch { return true; }
        }
    }
}
