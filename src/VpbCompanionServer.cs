using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace VPB
{
    /// <summary>
    /// Local named-pipe server so VPM can ask a running VaM to reload scan_whitelist.json
    /// or load a scene. Not VpbNet multiplayer.
    /// </summary>
    internal static class VpbCompanionServer
    {
        public const string PipeName = "VPB-companion";
        static volatile bool s_started;
        static volatile bool s_stop;
        static readonly object s_PipeLock = new object();
        static readonly object s_MainLock = new object();
        static readonly Queue<Pending> s_Main = new Queue<Pending>();
        static NamedPipeServerStream s_listening;
        static Thread s_thread;

        sealed class Pending
        {
            public string Line;
            public string Reply;
            public ManualResetEvent Done;
        }

        public static void Start()
        {
            if (s_stop) return;
            if (s_started) return;
            s_started = true;
            s_stop = false;
            // Dedicated background thread — never park ThreadPool on WaitForConnection.
            // Occupied pool thread + native pipe wait keeps Unity from exiting (same class as #91).
            s_thread = new Thread(AcceptLoop);
            s_thread.IsBackground = true;
            s_thread.Name = "VPB_Companion";
            s_thread.Start();
        }

        /// <summary>
        /// Unblock WaitForConnection / ReadLine so VaM can exit. Flag alone is not enough —
        /// ConnectNamedPipe is a native wait; Unity shutdown stalls until it returns.
        /// </summary>
        public static void Stop()
        {
            s_stop = true;

            lock (s_MainLock)
            {
                while (s_Main.Count > 0)
                {
                    Pending p = s_Main.Dequeue();
                    if (p == null) continue;
                    p.Reply = "ERR stopping";
                    try { if (p.Done != null) p.Done.Set(); } catch { }
                }
            }

            NamedPipeServerStream pipe;
            Thread thread;
            lock (s_PipeLock)
            {
                pipe = s_listening;
                s_listening = null;
                thread = s_thread;
            }

            try { if (pipe != null) pipe.Dispose(); } catch { }

            // Mono often ignores Dispose of a waiting server pipe. Dummy connect unblocks it.
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                    client.Connect(250);
            }
            catch { }

            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(500); } catch { }
            }
        }

        public static void PumpMainThread()
        {
            if (s_stop) return;
            Pending p = null;
            lock (s_MainLock)
            {
                if (s_Main.Count == 0) return;
                p = s_Main.Dequeue();
            }
            if (p == null) return;
            try { p.Reply = ExecuteOnMain(p.Line); }
            catch (Exception ex) { p.Reply = "ERR " + ex.Message; }
            try { p.Done.Set(); } catch { }
        }

        static void AcceptLoop()
        {
            while (!s_stop)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None);
                    lock (s_PipeLock) { s_listening = pipe; }
                    if (s_stop) break;
                    pipe.WaitForConnection();
                    if (s_stop) break;
                    HandleClient(pipe);
                }
                catch (Exception ex)
                {
                    if (s_stop) break;
                    try { LogUtil.LogWarning("[VPB Companion] " + ex.Message); } catch { }
                    Thread.Sleep(250);
                }
                finally
                {
                    lock (s_PipeLock)
                    {
                        if (s_listening == pipe) s_listening = null;
                    }
                    try { if (pipe != null) pipe.Dispose(); } catch { }
                }
            }
        }

        static void HandleClient(NamedPipeServerStream pipe)
        {
            var reader = new StreamReader(pipe, Encoding.UTF8, false, 256);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            string line = reader.ReadLine();
            if (s_stop) return;
            if (string.IsNullOrEmpty(line))
            {
                writer.WriteLine("ERR empty");
                return;
            }

            var pending = new Pending { Line = line, Done = new ManualResetEvent(false) };
            lock (s_MainLock)
                s_Main.Enqueue(pending);
            if (!pending.Done.WaitOne(15000))
            {
                writer.WriteLine("ERR timeout");
                return;
            }
            if (s_stop) return;
            writer.WriteLine(pending.Reply ?? "ERR");
        }

        static string ExecuteOnMain(string line)
        {
            if (line.Equals("PING", StringComparison.OrdinalIgnoreCase))
                return "OK";
            if (line.Equals("RELOAD_WHITELIST", StringComparison.OrdinalIgnoreCase))
            {
                ScanWhitelistManager.Reload();
                var inst = ScanWhitelistManager.Instance;
                if (inst == null) return "ERR no whitelist manager";
                try { FileManagerBridge.Refresh("vpm_whitelist", RefreshScope.Both); } catch { }
                return "OK";
            }
            if (line.StartsWith("LOAD_SCENE ", StringComparison.OrdinalIgnoreCase))
            {
                string path = line.Substring(11).Trim();
                if (string.IsNullOrEmpty(path)) return "ERR no path";
                try
                {
                    if (SuperController.singleton != null)
                        SuperController.singleton.Load(path);
                    return "OK";
                }
                catch (Exception ex)
                {
                    return "ERR " + ex.Message;
                }
            }
            if (line.Equals("ONDEMAND_STATS", StringComparison.OrdinalIgnoreCase))
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                VpbLocalDatabase.TryReadOnDemandHits(counts);
                return "OK " + counts.Count.ToString();
            }
            return "ERR unknown";
        }
    }
}
