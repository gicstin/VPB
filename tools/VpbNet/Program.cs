using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace VpbNet
{
    public static class Program
    {
        const string PidFilePrefix = "vpbnet-";
        const string PidFileSuffix = ".pid";
        const string LegacyPidFileName = "vpbnet.pid";

        public static int Main(string[] args)
        {
            int pluginPort = 0;
            uint parentPid = 0;
            int declaredIpcVersion = VpbIpc.IpcVersion;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--self-test-pose") return VpbPose.RunSelfTestConsole();
                if (args[i] == "--self-test-clock") return VpbNetClockSelfTest.RunConsole();
                if (args[i] == "--self-test-snapshot") return VpbNetSnapshotSelfTest.RunConsole();
                if (args[i] == "--self-test-event") return VpbNetEventSelfTest.RunConsole();
                if (args[i] == "--self-test-session") return VpbNetSessionSelfTest.RunConsole();
                if (args[i] == "--self-test-keyframe") return VpbNetKeyframeSelfTest.RunConsole();
                if (args[i] == "--self-test-diag") return VpbNetDiagnosticsSelfTest.RunConsole();
                if (args[i] == "--self-test-rig") return VpbNetRigSelfTest.RunConsole();
                if (args[i] == "--self-test-roomcode") return VpbNetRoomCodeSelfTest.RunConsole();
                if (args[i] == "--self-test-redact") return VpbNetRedactSelfTest.RunConsole();
                if (args[i] == "--self-test-roombook") return VpbNetRoomBookSelfTest.RunConsole();
                if (args[i] == "--self-test-rendezvous") return Rendezvous.RendezvousTableSelfTest.RunConsole();
                if (args[i] == "--self-test-direct") return Rendezvous.DirectConnectSelfTest.RunConsole();
                if (args[i] == "--self-test-reliable") return Transport.ReliabilitySelfTest.RunConsole();
                if (args[i] == "--self-test-discovery") return Transport.DiscoverySelfTest.RunConsole();
                if (args[i] == "--self-test-invite") return VpbNetInviteCodeSelfTest.RunConsole();
                if (args[i] == "--self-test-contract") return VpbNetContractSelfTest.RunConsole();
                if (args[i] == "--self-test-offer") return VpbNetOfferSelfTest.RunConsole();
                if (args[i] == "--self-test-storable") return VpbNetStorableSelfTest.RunConsole();
                if (args[i] == "--self-test-fidelity") return VpbNetFidelitySelfTest.RunConsole();
                if (args[i] == "--self-test-prop") return VpbNetPropSelfTest.RunConsole();
                if (args[i] == "--self-test-avatar") return VpbNetAvatarSelfTest.RunConsole();
                if (args[i] == "--self-test-rules") return VpbNetRulesSelfTest.RunConsole();
                if (args[i] == "--self-test-steam") return VpbNetSteamSelfTest.RunConsole();
                if (args[i] == "--steam-probe") return RunSteamProbe(args, i);
                if (args[i] == "--rendezvous") return RunRendezvous(args, i);
            }

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--plugin-port":
                        int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out pluginPort);
                        break;
                    case "--parent-pid":
                        uint.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out parentPid);
                        break;
                    case "--ipc-version":
                        int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out declaredIpcVersion);
                        break;
                }
            }

            if (pluginPort <= 0 || pluginPort > 65535)
            {
                Console.Error.WriteLine("VpbNet: --plugin-port is required. This process is launched by the VPB plugin, not by hand.");
                return 2;
            }

            byte[] secret = new byte[VpbIpc.SecretSize];
            string diag;
            if (!ReadSecretFromStdin(secret, out diag))
            {
                Console.Error.WriteLine("VpbNet: launch secret rejected (" + diag + ")");
                return 3;
            }

            if (declaredIpcVersion != VpbIpc.IpcVersion)
            {
                Console.Out.WriteLine("plugin declared IPC v" + declaredIpcVersion
                    + ", this broker speaks v" + VpbIpc.IpcVersion + "; expect a version reject");
                Console.Out.Flush();
            }

            ReapOrphans();
            WritePidFile(pluginPort, parentPid);

            int exitCode = 0;
            try
            {
                using (BrokerHost host = new BrokerHost(pluginPort, parentPid, secret))
                {
                    host.Start();
                    while (!host.ShouldExit)
                    {
                        host.RunOnce();
                    }
                    Console.Out.WriteLine("exiting: " + host.ExitReason);
                    Console.Out.Flush();
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("VpbNet: fatal: " + e.Message);
                exitCode = 1;
            }
            finally
            {
                DeletePidFile();
            }

            return exitCode;
        }

        static int RunSteamProbe(string[] args, int at)
        {
            uint appId = VpbNetSteam.DefaultAppId;
            if (at + 1 < args.Length)
            {
                VpbNetSteamFault fault;
                uint parsed;
                if (VpbNetSteam.TryParseConnectBlob(args[at + 1], out parsed, out fault)) appId = parsed;
            }

            string dir = AppContext.BaseDirectory;
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string d = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(d)) dir = d;
            }
            catch { }

            Console.Out.WriteLine("===== steam probe =====");
            Console.Out.WriteLine("app id      " + appId);
            Console.Out.WriteLine("folder      " + dir);

            string hint = Environment.GetEnvironmentVariable("VPB_STEAM_API");
            string path = Transport.Steam.SteamNative.FindLibrary(hint, dir);
            if (path == null)
            {
                Console.Out.WriteLine("library     NOT FOUND");
                Console.Out.WriteLine(VpbNetSteam.MissingLibrary(dir));
                Console.Out.WriteLine("VERDICT: Steam connections cannot work until that file is in place.");
                return 5;
            }
            Console.Out.WriteLine("library     " + path);

            string error;
            if (!Transport.Steam.SteamNative.Load(path, out error))
            {
                Console.Out.WriteLine("load        FAILED");
                Console.Out.WriteLine(error);
                Console.Out.WriteLine("VERDICT: replace that copy of " + VpbNetSteam.NativeLibrary + ".");
                return 5;
            }
            Console.Out.WriteLine("load        ok, every entry point this build needs is present");

            if (!Transport.Steam.SteamNative.Start(appId, dir, out error))
            {
                Console.Out.WriteLine("steam api   FAILED");
                Console.Out.WriteLine(error);
                Console.Out.WriteLine("VERDICT: start Steam and sign in, then run this again.");
                return 5;
            }

            ulong me = Transport.Steam.SteamNative.SelfSteamId();
            Console.Out.WriteLine("steam api   ok, signed in" + (me == 0 ? " (no account id)" : string.Empty));

            Transport.Steam.SteamAvailability relay = Transport.Steam.SteamAvailability.Unknown;
            Stopwatch clock = Stopwatch.StartNew();
            IntPtr scratch = System.Runtime.InteropServices.Marshal.AllocHGlobal(Transport.Steam.SteamNative.CallbackMsgBytes);
            try
            {
                while (clock.ElapsedMilliseconds < 15000)
                {
                    Transport.Steam.SteamNative.RunFrame(scratch, (c, p, n) => { });
                    relay = Transport.Steam.SteamNative.RelayStatus();
                    if (relay == Transport.Steam.SteamAvailability.Current) break;
                    System.Threading.Thread.Sleep(50);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(scratch);
            }

            Console.Out.WriteLine("relay       " + relay);
            Transport.Steam.SteamNative.Stop();

            if (relay == Transport.Steam.SteamAvailability.Current)
            {
                Console.Out.WriteLine("VERDICT: this machine can host and join Steam rooms.");
                Console.Out.WriteLine("===== end steam probe =====");
                return 0;
            }

            Console.Out.WriteLine(VpbNetSteam.RelayUnavailable());
            Console.Out.WriteLine("VERDICT: Steam is signed in but its relay network is not ready here.");
            Console.Out.WriteLine("===== end steam probe =====");
            return 6;
        }

        static int RunRendezvous(string[] args, int at)
        {
            int port = Rendezvous.RendezvousServer.DefaultPort;
            if (at + 1 < args.Length)
                int.TryParse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
            if (port <= 0 || port > 65535) port = Rendezvous.RendezvousServer.DefaultPort;

            bool verbose = false;
            bool relay = true;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--verbose") verbose = true;
                if (args[i] == "--no-relay") relay = false;
            }

            using (Rendezvous.RendezvousServer server = new Rendezvous.RendezvousServer())
            {
                server.RelayEnabled = relay;
                try
                {
                    server.Start(port, verbose);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("VpbNet: rendezvous could not bind UDP " + port + ": " + e.Message);
                    return 4;
                }

                Console.Out.WriteLine("VpbNet rendezvous listening on UDP " + port
                    + " (protocol v" + VpbNetRendezvous.Version + ", no logging"
                    + (relay ? ", relay on" : ", rendezvous only")
                    + (verbose ? ", counters every 30s" : string.Empty) + ")");
                Console.Out.Flush();

                Stopwatch clock = Stopwatch.StartNew();
                StringBuilder report = new StringBuilder(128);
                long nextReport = 30000;

                while (true)
                {
                    long now = clock.ElapsedMilliseconds;
                    server.Poll(now);

                    if (verbose && now >= nextReport)
                    {
                        nextReport = now + 30000;
                        server.ReportLine(report, now);
                        Console.Out.WriteLine(report.ToString());
                        Console.Out.Flush();
                    }

                    System.Threading.Thread.Sleep(2);
                }
            }
        }

        const int SecretHexChars = VpbIpc.SecretSize * 2;

        static bool ReadSecretFromStdin(byte[] secret, out string diag)
        {
            int total = 0;
            byte[] buf = new byte[512];

            try
            {
                using (Stream stdin = Console.OpenStandardInput())
                {
                    while (total < buf.Length)
                    {
                        int n = stdin.Read(buf, total, buf.Length - total);
                        if (n <= 0) break;
                        total += n;
                        if (total >= SecretHexChars && IndexOf(buf, total, (byte)'\n') >= 0) break;
                    }
                }
            }
            catch (Exception e)
            {
                diag = "stdin read failed: " + e.Message;
                return false;
            }

            if (total == 0)
            {
                diag = "stdin closed without sending anything";
                return false;
            }

            int start = FindHexRun(buf, total, SecretHexChars);
            if (start < 0)
            {
                diag = "read " + total + " bytes but found no " + SecretHexChars
                    + "-char hex run; leading bytes were " + VpbIpc.ToHex(buf, Math.Min(total, 12));
                return false;
            }

            char[] chars = new char[SecretHexChars];
            for (int i = 0; i < SecretHexChars; i++) chars[i] = (char)buf[start + i];
            if (!VpbIpc.FromHex(new string(chars), secret, VpbIpc.SecretSize))
            {
                diag = "hex run at offset " + start + " failed to decode";
                return false;
            }

            diag = "ok";
            return true;
        }

        static int IndexOf(byte[] buf, int len, byte value)
        {
            for (int i = 0; i < len; i++)
            {
                if (buf[i] == value) return i;
            }
            return -1;
        }

        static int FindHexRun(byte[] buf, int len, int runLength)
        {
            int run = 0;
            for (int i = 0; i < len; i++)
            {
                if (IsHexByte(buf[i]))
                {
                    run++;
                    if (run >= runLength) return i - runLength + 1;
                }
                else run = 0;
            }
            return -1;
        }

        static bool IsHexByte(byte b)
        {
            return (b >= (byte)'0' && b <= (byte)'9')
                || (b >= (byte)'a' && b <= (byte)'f')
                || (b >= (byte)'A' && b <= (byte)'F');
        }

        static string PidDirectory
        {
            get
            {
                try
                {
                    string exe = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        string dir = Path.GetDirectoryName(exe);
                        if (!string.IsNullOrEmpty(dir)) return dir;
                    }
                }
                catch { }
                return AppContext.BaseDirectory;
            }
        }

        static string PidFilePath
        {
            get
            {
                return Path.Combine(PidDirectory,
                    PidFilePrefix + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + PidFileSuffix);
            }
        }

        static void ReapOrphans()
        {
            try
            {
                int self = Process.GetCurrentProcess().Id;
                string dir = PidDirectory;

                string[] files = Directory.GetFiles(dir, PidFilePrefix + "*" + PidFileSuffix);
                for (int i = 0; i < files.Length; i++) ReapOne(files[i], self);

                string legacy = Path.Combine(dir, LegacyPidFileName);
                if (File.Exists(legacy)) ReapOne(legacy, self);
            }
            catch { }
        }

        static void ReapOne(string path, int self)
        {
            try
            {
                int pid = 0;
                int parentPid = 0;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("pid=", StringComparison.Ordinal))
                        int.TryParse(lines[i].Substring(4).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
                    else if (lines[i].StartsWith("parentPid=", StringComparison.Ordinal))
                        int.TryParse(lines[i].Substring(10).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parentPid);
                }

                if (pid == self) return;

                if (pid <= 0 || !IsLiveBroker(pid))
                {
                    TryDelete(path);
                    return;
                }

                if (parentPid > 0 && IsAlive(parentPid)) return;

                try
                {
                    Process.GetProcessById(pid).Kill();
                    Console.Out.WriteLine("reaped orphaned broker pid=" + pid);
                }
                catch { }
                TryDelete(path);
            }
            catch { }
        }

        static bool IsLiveBroker(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                return !p.HasExited && string.Equals(p.ProcessName, "VpbNet", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static bool IsAlive(int pid)
        {
            try { return !Process.GetProcessById(pid).HasExited; }
            catch { return false; }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        static void WritePidFile(int pluginPort, uint parentPid)
        {
            try
            {
                using (StreamWriter w = new StreamWriter(PidFilePath, false))
                {
                    w.WriteLine("pid=" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                    w.WriteLine("parentPid=" + parentPid.ToString(CultureInfo.InvariantCulture));
                    w.WriteLine("pluginPort=" + pluginPort.ToString(CultureInfo.InvariantCulture));
                    w.WriteLine("started=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    w.WriteLine("ipcVersion=" + VpbIpc.IpcVersion.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch { }
        }

        static void DeletePidFile()
        {
            TryDelete(PidFilePath);
        }
    }
}
