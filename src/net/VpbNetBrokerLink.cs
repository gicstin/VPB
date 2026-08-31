using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using VpbNet;

namespace VPB
{
    public enum VpbNetLinkState
    {
        Idle,
        Launching,
        Handshaking,
        Ready,
        Failed,
        Stopping,
    }

    public static class VpbNetBrokerLink
    {
        const string BrokerFolder = "VpbNet";
        const string BrokerExe = "VpbNet.exe";
        const string PidFilePrefix = "vpbnet-";
        const string PidFileSuffix = ".pid";
        const string LegacyPidFileName = "vpbnet.pid";
        const ushort AppProtoVersion = 1;
        const int RingSlots = 256;

        static readonly RNGCryptoServiceProvider Rng = new RNGCryptoServiceProvider();

        public static bool FillRandom(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return false;
            try
            {
                Rng.GetBytes(buffer);
                return true;
            }
            catch
            {
                return false;
            }
        }
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static readonly object LogLock = new object();
        static readonly List<string> PendingBrokerOutput = new List<string>();
        static readonly byte[] Token = new byte[VpbIpc.TokenSize];
        static readonly byte[] Secret = new byte[VpbIpc.SecretSize];
        static readonly byte[] RxScratch = new byte[VpbIpc.MaxDatagram];
        static readonly byte[] TxScratch = new byte[VpbIpc.MaxDatagram];
        static readonly byte[] DrainScratch = new byte[VpbIpc.MaxDatagram];

        static VpbNetFrameRing _inbound;
        static VpbNetFrameRing _outbound;
        static AutoResetEvent _txSignal;
        static Socket _socket;
        static Thread _rxThread;
        static Thread _txThread;
        static Process _proc;
        static volatile bool _running;

        static VpbNetLinkState _state = VpbNetLinkState.Idle;
        static string _lastError = string.Empty;
        static int _brokerPort;
        static int _localPort;
        static uint _seq;
        static long _stateEnteredMs;
        static long _lastInboundMs;
        static bool _warnedStalled;
        static long _lastPingMs;
        static uint _brokerPid;
        static ushort _brokerBuild;
        static long _arrivalTicks;

        public static double TicksToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }

        public static double LastRttMs;
        public static double MeanRttMs;
        public static double LastDrainLagMs;
        public static double MeanDrainLagMs;
        static int _drainLagSamples;
        public static int PingsSent;
        public static int PongsReceived;
        public static int EchoesSent;
        public static int EchoRepliesReceived;
        public static int MalformedDropped;

        static readonly bool[] PeerUp = new bool[VpbIpc.MaxPeers + 1];
        static readonly uint[] PeerRemoteSent = new uint[VpbIpc.MaxPeers + 1];
        static readonly uint[] PeerRemoteReceived = new uint[VpbIpc.MaxPeers + 1];
        static readonly uint[] PeerLost = new uint[VpbIpc.MaxPeers + 1];
        static readonly uint[] PeerReordered = new uint[VpbIpc.MaxPeers + 1];
        static readonly uint[] PeerRttMicros = new uint[VpbIpc.MaxPeers + 1];
        static readonly uint[] PeerJitterMicros = new uint[VpbIpc.MaxPeers + 1];

        public static VpbIpcSession SessionState = VpbIpcSession.Idle;
        public static string SessionText = string.Empty;
        public static string InviteBlob = string.Empty;
        static VpbIpcSession _loggedState = (VpbIpcSession)255;
        static string _loggedText;
        public static int PeerCount;
        public static int DataSent;
        public static int DataReceived;
        public static int DataSendFailed;


        public static bool SessionRunning { get { return SessionState == VpbIpcSession.Connected; } }

        public static bool IsPeerUp(int peerId)
        {
            return peerId > 0 && peerId <= VpbIpc.MaxPeers && PeerUp[peerId];
        }

        public static int FirstPeer()
        {
            for (int i = 1; i <= VpbIpc.MaxPeers; i++)
            {
                if (PeerUp[i]) return i;
            }
            return -1;
        }

        public static double PeerRttMs(int peerId)
        {
            return peerId > 0 && peerId <= VpbIpc.MaxPeers ? PeerRttMicros[peerId] / 1000.0 : 0.0;
        }

        public static double PeerJitterMs(int peerId)
        {
            return peerId > 0 && peerId <= VpbIpc.MaxPeers ? PeerJitterMicros[peerId] / 1000.0 : 0.0;
        }

        public static uint PeerLostCount(int peerId)
        {
            return peerId > 0 && peerId <= VpbIpc.MaxPeers ? PeerLost[peerId] : 0u;
        }

        public static uint PeerReorderedCount(int peerId)
        {
            return peerId > 0 && peerId <= VpbIpc.MaxPeers ? PeerReordered[peerId] : 0u;
        }

        public static uint PeerRemoteSentCount(int peerId)
        {
            return peerId > 0 && peerId <= VpbIpc.MaxPeers ? PeerRemoteSent[peerId] : 0u;
        }

        public static uint PeerRemoteReceivedCount(int peerId)
        {
            return peerId > 0 && peerId <= VpbIpc.MaxPeers ? PeerRemoteReceived[peerId] : 0u;
        }

        public static VpbNetLinkState State { get { return _state; } }
        public static string LastError { get { return _lastError; } }
        public static bool IsReady { get { return _state == VpbNetLinkState.Ready; } }
        public static int InboundDropped { get { return _inbound != null ? _inbound.Dropped : 0; } }
        public static int OutboundDropped { get { return _outbound != null ? _outbound.Dropped : 0; } }

        public static string BrokerDirectory
        {
            get { return VpbPaths.FindDir(VpbPaths.NetDirName, BrokerFolder); }
        }

        public static string BrokerExePath
        {
            get
            {
                try
                {
                    Settings s = Settings.Instance;
                    if (s != null && s.NetBrokerPath != null && !string.IsNullOrEmpty(s.NetBrokerPath.Value))
                        return s.NetBrokerPath.Value;
                }
                catch { }
                return Path.Combine(BrokerDirectory, BrokerExe);
            }
        }


        public static bool MultiplayerEnabled
        {
            get
            {
                try
                {
                    Settings s = Settings.Instance;
                    return s != null && s.NetEnabled != null && s.NetEnabled.Value;
                }
                catch { return false; }
            }
        }

        public static bool Start(string reason)
        {
            if (_state == VpbNetLinkState.Launching || _state == VpbNetLinkState.Handshaking || _state == VpbNetLinkState.Ready)
                return true;

            if (!MultiplayerEnabled)
            {
                Fail("multiplayer is disabled (Net.Enabled = false); broker not launched");
                return false;
            }

            string exe = BrokerExePath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                Fail("broker not found at " + exe + " - build tools/VpbNet and copy VpbNet.exe there");
                return false;
            }

            ReapOrphans();

            try
            {
                _inbound = new VpbNetFrameRing(RingSlots, VpbIpc.MaxDatagram);
                _outbound = new VpbNetFrameRing(RingSlots, VpbIpc.MaxDatagram);
                _txSignal = new AutoResetEvent(false);
                _seq = 0;
                LastRttMs = 0.0;
                MeanRttMs = 0.0;
                PingsSent = 0;
                PongsReceived = 0;
                EchoesSent = 0;
                EchoRepliesReceived = 0;
                MalformedDropped = 0;
                ResetSession();
                _brokerPort = 0;
                _brokerPid = 0;
                _brokerBuild = 0;

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                _socket.ReceiveTimeout = 250;
                _localPort = ((IPEndPoint)_socket.LocalEndPoint).Port;

                Rng.GetBytes(Secret);

                if (!LaunchBroker(exe)) return false;

                _running = true;
                SetState(VpbNetLinkState.Launching);
                LogUtil.LogWarning("[VPB.Net] broker launching (" + reason + ") pluginPort=" + _localPort);
                return true;
            }
            catch (Exception e)
            {
                Fail("broker start failed: " + e.Message);
                Cleanup();
                return false;
            }
        }

        static bool LaunchBroker(string exe)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = "--plugin-port " + _localPort.ToString(CultureInfo.InvariantCulture)
                    + " --parent-pid " + GetCurrentPid().ToString(CultureInfo.InvariantCulture)
                    + " --ipc-version " + VpbIpc.IpcVersion.ToString(CultureInfo.InvariantCulture);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardInput = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = BrokerDirectory;

                try
                {
                    string steamApi = VpbNetTransportChoice.LibraryPath();
                    if (!string.IsNullOrEmpty(steamApi)) psi.EnvironmentVariables["VPB_STEAM_API"] = steamApi;
                }
                catch { }

                _proc = Process.Start(psi);
                if (_proc == null)
                {
                    Fail("broker process failed to start");
                    return false;
                }

                _proc.OutputDataReceived += OnBrokerOutput;
                _proc.ErrorDataReceived += OnBrokerOutput;
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();

                WriteSecretToStdin();
                return true;
            }
            catch (Exception e)
            {
                Fail("broker launch failed: " + e.Message);
                return false;
            }
        }

        static void WriteSecretToStdin()
        {
            string hex = VpbIpc.ToHex(Secret, VpbIpc.SecretSize);
            byte[] line = new byte[hex.Length + 1];
            for (int i = 0; i < hex.Length; i++) line[i] = (byte)hex[i];
            line[hex.Length] = (byte)'\n';

            Stream raw = _proc.StandardInput.BaseStream;
            raw.Write(line, 0, line.Length);
            raw.Flush();
            _proc.StandardInput.Close();
        }

        static void OnBrokerOutput(object sender, DataReceivedEventArgs e)
        {
            if (e == null || string.IsNullOrEmpty(e.Data)) return;
            lock (LogLock)
            {
                if (PendingBrokerOutput.Count < 256) PendingBrokerOutput.Add(e.Data);
            }
        }

        public static void Stop(string reason)
        {
            if (_state == VpbNetLinkState.Idle && _proc == null) return;

            SetState(VpbNetLinkState.Stopping);
            try
            {
                if (_socket != null && _brokerPort > 0)
                {
                    int len = VpbIpc.WriteBye(TxScratch, NextSeq(), Token);
                    try { _socket.Send(TxScratch, 0, len, SocketFlags.None); }
                    catch { }
                }
            }
            catch { }

            Cleanup();
            VPB.src.util.VPBLogger.Main.LogWarning("[VPB.Net] broker link stopped (" + reason + ")", false);
            SetState(VpbNetLinkState.Idle);
        }

        static void ResetSession()
        {
            SessionState = VpbIpcSession.Idle;
            SessionText = string.Empty;
            InviteBlob = string.Empty;
            _loggedState = (VpbIpcSession)255;
            _loggedText = null;
            PeerCount = 0;
            DataSent = 0;
            DataReceived = 0;
            DataSendFailed = 0;
            for (int i = 0; i <= VpbIpc.MaxPeers; i++)
            {
                PeerUp[i] = false;
                PeerRemoteSent[i] = 0;
                PeerRemoteReceived[i] = 0;
                PeerLost[i] = 0;
                PeerReordered[i] = 0;
                PeerRttMicros[i] = 0;
                PeerJitterMicros[i] = 0;
            }
        }

        public static bool OpenSession(VpbIpcBackend backend, bool asHost, string roomCode, string connectBlob)
        {
            if (_state != VpbNetLinkState.Ready) return false;

            ResetSession();
            int len = VpbIpc.WriteOpenSession(TxScratch, NextSeq(), Token, (byte)backend,
                asHost ? (byte)0 : (byte)1,
                (byte)(VpbNetAvatarAssignment.SeatCount - 1), roomCode, connectBlob);
            return Enqueue(TxScratch, len);
        }

        public static void CloseSession()
        {
            if (_state != VpbNetLinkState.Ready) return;
            Enqueue(TxScratch, VpbIpc.WriteCloseSession(TxScratch, NextSeq(), Token));
            ResetSession();
        }

        public static bool SendData(int peerId, byte channel, byte[] payload, int len, bool reliable)
        {
            if (_state != VpbNetLinkState.Ready || !IsPeerUp(peerId)) return false;
            if (len <= 0 || len > VpbIpc.MaxDataPayload) return false;

            int n = VpbIpc.WriteData(TxScratch, NextSeq(), Token, peerId, channel,
                reliable ? VpbIpc.DataFlagReliable : (byte)0, payload, 0, len);
            if (!Enqueue(TxScratch, n))
            {
                DataSendFailed++;
                return false;
            }
            DataSent++;
            return true;
        }

        static void Cleanup()
        {
            _running = false;
            ResetSession();
            try { if (_txSignal != null) _txSignal.Set(); }
            catch { }

            try { if (_socket != null) _socket.Close(); }
            catch { }
            _socket = null;

            JoinThread(_rxThread);
            JoinThread(_txThread);
            _rxThread = null;
            _txThread = null;

            try
            {
                if (_proc != null)
                {
                    if (!_proc.WaitForExit(1500))
                    {
                        try { _proc.Kill(); }
                        catch { }
                    }
                    _proc.Dispose();
                }
            }
            catch { }
            _proc = null;

            try { if (_txSignal != null) _txSignal.Close(); }
            catch { }
            _txSignal = null;

            DeletePidFile();
            _brokerPort = 0;
        }

        static void JoinThread(Thread t)
        {
            try
            {
                if (t != null && t.IsAlive && !t.Join(1000)) t.Abort();
            }
            catch { }
        }

        public static void Tick()
        {
            DrainBrokerOutput();

            if (_state == VpbNetLinkState.Idle || _state == VpbNetLinkState.Failed) return;

            if (_proc != null)
            {
                bool exited = false;
                try { exited = _proc.HasExited; }
                catch { }
                if (exited && _state != VpbNetLinkState.Stopping)
                {
                    Fail("broker exited unexpectedly");
                    Cleanup();
                    return;
                }
            }

            long now = Clock.ElapsedMilliseconds;

            if (_state == VpbNetLinkState.Launching)
            {
                if (_brokerPort > 0) BeginHandshake();
                else if (now - _stateEnteredMs > VpbIpc.HandshakeTimeoutMs)
                {
                    Fail("broker never reported a port (see broker output above)");
                    Cleanup();
                }
                return;
            }

            DrainInbound();

            if (_state == VpbNetLinkState.Handshaking)
            {
                if (now - _stateEnteredMs > VpbIpc.HandshakeTimeoutMs)
                {
                    Fail("handshake timed out");
                    Cleanup();
                }
                return;
            }

            if (_state != VpbNetLinkState.Ready) return;

            long silent = now - _lastInboundMs;
            if (silent > VpbIpc.StalledTimeoutMs)
            {
                Fail("broker stopped responding");
                Cleanup();
                return;
            }

            // Almost always VaM froze (nothing pumped/drained).
            if (silent > VpbIpc.StallWarnMs)
            {
                if (!_warnedStalled)
                {
                    _warnedStalled = true;
                    LogUtil.LogWarning("[VPB.Net] this side stopped servicing the link for "
                        + (silent / 1000) + "s - VaM was busy (loading content freezes it)."
                        + " The session is held open; it only drops after "
                        + (VpbIpc.StalledTimeoutMs / 1000) + "s.");
                }
            }
            else _warnedStalled = false;

            if (now - _lastPingMs >= VpbIpc.HeartbeatMs)
            {
                _lastPingMs = now;
                int len = VpbIpc.WritePing(TxScratch, NextSeq(), Token, Stopwatch.GetTimestamp());
                if (Enqueue(TxScratch, len)) PingsSent++;
            }
        }

        static void BeginHandshake()
        {
            try
            {
                _socket.Connect(new IPEndPoint(IPAddress.Loopback, _brokerPort));

                _rxThread = new Thread(RxLoop);
                _rxThread.IsBackground = true;
                _rxThread.Name = "VpbNetRx";
                _rxThread.Start();

                _txThread = new Thread(TxLoop);
                _txThread.IsBackground = true;
                _txThread.Name = "VpbNetTx";
                _txThread.Start();

                int len = VpbIpc.WriteHello(TxScratch, NextSeq(), Secret, AppProtoVersion, GetCurrentPid());
                Enqueue(TxScratch, len);

                _lastInboundMs = Clock.ElapsedMilliseconds;
                SetState(VpbNetLinkState.Handshaking);
            }
            catch (Exception e)
            {
                Fail("connect failed: " + e.Message);
                Cleanup();
            }
        }

        static void DrainInbound()
        {
            if (_inbound == null) return;

            int len;
            long arrivalTicks;
            while ((len = _inbound.TryDequeue(DrainScratch, out arrivalTicks)) > 0)
            {
                _arrivalTicks = arrivalTicks;
                double lag = TicksToMs(Stopwatch.GetTimestamp() - arrivalTicks);
                if (lag >= 0.0)
                {
                    LastDrainLagMs = lag;
                    _drainLagSamples++;
                    MeanDrainLagMs = _drainLagSamples == 1 ? lag : MeanDrainLagMs + (lag - MeanDrainLagMs) / _drainLagSamples;
                }

                byte version;
                VpbIpcMsg type;
                uint seq;
                int payloadOffset;
                int payloadLen;
                if (!VpbIpc.TryReadHeader(DrainScratch, len, out version, out type, out seq, out payloadOffset, out payloadLen))
                {
                    MalformedDropped++;
                    continue;
                }

                if (version != VpbIpc.IpcVersion && type != VpbIpcMsg.Reject)
                {
                    MalformedDropped++;
                    continue;
                }

                if (type != VpbIpcMsg.Welcome && type != VpbIpcMsg.Reject && !VpbIpc.TokenMatches(DrainScratch, Token))
                {
                    MalformedDropped++;
                    continue;
                }

                _lastInboundMs = Clock.ElapsedMilliseconds;
                HandleMessage(type, payloadLen);
            }
        }

        static void HandleMessage(VpbIpcMsg type, int payloadLen)
        {
            switch (type)
            {
                case VpbIpcMsg.Welcome:
                {
                    if (_state != VpbNetLinkState.Handshaking) return;
                    ushort ipcVersion;
                    ushort build;
                    uint pid;
                    if (!VpbIpc.ReadWelcome(DrainScratch, payloadLen, Token, out ipcVersion, out build, out pid))
                    {
                        MalformedDropped++;
                        return;
                    }
                    _brokerBuild = build;
                    _brokerPid = pid;
                    _lastPingMs = Clock.ElapsedMilliseconds;
                    SetState(VpbNetLinkState.Ready);
                    LogUtil.LogWarning(string.Format(
                        "[VPB.Net] broker ready: ipc=v{0} build={1} pid={2} port={3}",
                        ipcVersion, build, pid, _brokerPort));
                    break;
                }
                case VpbIpcMsg.Reject:
                {
                    string text;
                    VpbIpcReject reason = VpbIpc.ReadReject(DrainScratch, payloadLen, out text);
                    Fail("broker rejected the handshake: " + reason + (string.IsNullOrEmpty(text) ? "" : " - " + text));
                    if (reason == VpbIpcReject.IpcVersionMismatch)
                        LogUtil.LogError("[VPB.Net] the VpbNet.exe next to VPB.dll is a different IPC version than this build. Replace it with the broker from this VPB release.");
                    Cleanup();
                    break;
                }
                case VpbIpcMsg.Pong:
                {
                    long sent;
                    long brokerTicks;
                    if (!VpbIpc.ReadPong(DrainScratch, payloadLen, out sent, out brokerTicks)) return;
                    double rtt = TicksToMs(_arrivalTicks - sent);
                    LastRttMs = rtt;
                    PongsReceived++;
                    MeanRttMs = PongsReceived == 1 ? rtt : MeanRttMs + (rtt - MeanRttMs) / PongsReceived;
                    break;
                }
                case VpbIpcMsg.EchoReply:
                {
                    EchoRepliesReceived++;
                    break;
                }
                case VpbIpcMsg.SessionState:
                {
                    byte state;
                    byte backendId;
                    int peerCount;
                    string invite;
                    string text;
                    if (!VpbIpc.ReadSessionState(DrainScratch, payloadLen, out state, out backendId, out peerCount, out invite, out text))
                    {
                        MalformedDropped++;
                        return;
                    }

                    SessionState = (VpbIpcSession)state;
                    SessionText = text;
                    PeerCount = peerCount;
                    if (!string.IsNullOrEmpty(invite)) InviteBlob = invite;

                    bool sameAsLast = _loggedState == SessionState
                        && string.Equals(_loggedText, text, StringComparison.Ordinal);
                    _loggedState = SessionState;
                    _loggedText = text;

                    if (SessionState == VpbIpcSession.Failed)
                        LogUtil.LogError("[VPB.Net] session failed: " + text);
                    else if (!sameAsLast)
                        LogUtil.LogWarning("[VPB.Net] session " + SessionState + (string.IsNullOrEmpty(text) ? "" : " - " + text));

                    VpbNetPresence.OnSessionState(SessionState, InviteBlob, text);
                    break;
                }
                case VpbIpcMsg.PeerEvent:
                {
                    byte kind;
                    int peerId;
                    string text;
                    if (!VpbIpc.ReadPeerEvent(DrainScratch, payloadLen, out kind, out peerId, out text))
                    {
                        MalformedDropped++;
                        return;
                    }
                    if (peerId <= 0 || peerId > VpbIpc.MaxPeers) return;

                    VpbIpcPeerEvent ev = (VpbIpcPeerEvent)kind;
                    if (ev == VpbIpcPeerEvent.Up) PeerUp[peerId] = true;
                    else if (ev == VpbIpcPeerEvent.Down) PeerUp[peerId] = false;

                    LogUtil.LogWarning("[VPB.Net] peer " + peerId + " " + ev + (string.IsNullOrEmpty(text) ? "" : " - " + text));
                    VpbNetPresence.OnPeerEvent(peerId, ev, text);
                    break;
                }
                case VpbIpcMsg.Data:
                {
                    int peerId;
                    byte channel;
                    byte flags;
                    int dataOffset;
                    int dataLen;
                    if (!VpbIpc.ReadDataHeader(DrainScratch, payloadLen, out peerId, out channel, out flags, out dataOffset, out dataLen))
                    {
                        MalformedDropped++;
                        return;
                    }
                    DataReceived++;
                    VpbNetPresence.OnData(peerId, channel, DrainScratch, dataOffset, dataLen, _arrivalTicks);
                    break;
                }
                case VpbIpcMsg.PeerStats:
                {
                    int peerId;
                    uint sent;
                    uint received;
                    uint lost;
                    uint reordered;
                    uint rttUs;
                    uint jitterUs;
                    if (!VpbIpc.ReadPeerStats(DrainScratch, payloadLen, out peerId, out sent, out received, out lost, out reordered, out rttUs, out jitterUs))
                    {
                        MalformedDropped++;
                        return;
                    }
                    if (peerId <= 0 || peerId > VpbIpc.MaxPeers) return;

                    PeerRemoteSent[peerId] = sent;
                    PeerRemoteReceived[peerId] = received;
                    PeerLost[peerId] = lost;
                    PeerReordered[peerId] = reordered;
                    PeerRttMicros[peerId] = rttUs;
                    PeerJitterMicros[peerId] = jitterUs;
                    break;
                }
                case VpbIpcMsg.Log:
                {
                    byte level;
                    string text;
                    if (!VpbIpc.ReadLog(DrainScratch, payloadLen, out level, out text)) return;
                    text = VpbNetRedact.Scrub(text);
                    if (level >= 2) LogUtil.LogError("[VPB.Net][broker] " + text);
                    else if (level == 1) LogUtil.LogWarning("[VPB.Net][broker] " + text);
                    else LogUtil.Log("[VPB.Net][broker] " + text);
                    break;
                }
                case VpbIpcMsg.Bye:
                {
                    LogUtil.LogWarning("[VPB.Net] broker said goodbye");
                    Cleanup();
                    SetState(VpbNetLinkState.Idle);
                    break;
                }
            }
        }

        public static bool SendEcho(byte[] payload, int len)
        {
            if (_state != VpbNetLinkState.Ready) return false;
            int n = VpbIpc.WriteEcho(TxScratch, NextSeq(), Token, payload, len, false);
            if (!Enqueue(TxScratch, n)) return false;
            EchoesSent++;
            return true;
        }

        static bool Enqueue(byte[] buf, int len)
        {
            if (_outbound == null) return false;
            bool ok = _outbound.TryEnqueue(buf, len);
            if (ok && _txSignal != null)
            {
                try { _txSignal.Set(); }
                catch { }
            }
            return ok;
        }

        static void RxLoop()
        {
            Socket sock = _socket;
            while (_running && sock != null)
            {
                try
                {
                    int n = sock.Receive(RxScratch, 0, RxScratch.Length, SocketFlags.None);
                    if (n > 0) _inbound.TryEnqueue(RxScratch, n);
                }
                catch (SocketException se)
                {
                    if (se.SocketErrorCode == SocketError.TimedOut) continue;
                    break;
                }
                catch { break; }
            }
        }

        static void TxLoop()
        {
            byte[] buf = new byte[VpbIpc.MaxDatagram];
            AutoResetEvent signal = _txSignal;
            while (_running)
            {
                try
                {
                    if (signal != null) signal.WaitOne(250);
                    Socket sock = _socket;
                    if (sock == null) continue;

                    int len;
                    while ((len = _outbound.TryDequeue(buf)) > 0)
                    {
                        sock.Send(buf, 0, len, SocketFlags.None);
                    }
                }
                catch { if (!_running) break; }
            }
        }

        static void DrainBrokerOutput()
        {
            if (PendingBrokerOutput.Count == 0) return;

            lock (LogLock)
            {
                for (int i = 0; i < PendingBrokerOutput.Count; i++)
                {
                    string line = PendingBrokerOutput[i];
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("READY ", StringComparison.Ordinal))
                    {
                        int port;
                        if (int.TryParse(line.Substring(6).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
                            && port > 0 && port <= 65535)
                        {
                            _brokerPort = port;
                            continue;
                        }
                    }
                    LogUtil.Log("[VPB.Net][broker] " + VpbNetRedact.Scrub(line));
                }
                PendingBrokerOutput.Clear();
            }
        }

        public static void ReapOrphans()
        {
            try
            {
                string dir = PidDirectory;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                uint self = GetCurrentPid();
                string[] files = Directory.GetFiles(dir, PidFilePrefix + "*" + PidFileSuffix);
                for (int i = 0; i < files.Length; i++) ReapOne(files[i], self);

                string legacy = Path.Combine(dir, LegacyPidFileName);
                if (File.Exists(legacy)) ReapOne(legacy, self);
            }
            catch { }
        }

        static void ReapOne(string path, uint selfPid)
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

                if (pid <= 0 || !IsLiveBroker(pid))
                {
                    TryDelete(path);
                    return;
                }

                if (parentPid > 0 && parentPid != (int)selfPid && IsAlive(parentPid)) return;

                try
                {
                    Process.GetProcessById(pid).Kill();
                    LogUtil.LogWarning("[VPB.Net] reaped orphaned broker pid=" + pid);
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
                return p != null && !p.HasExited
                    && string.Equals(p.ProcessName, "VpbNet", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static bool IsAlive(int pid)
        {
            try
            {
                Process p = Process.GetProcessById(pid);
                return p != null && !p.HasExited;
            }
            catch { return false; }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        static string PidDirectory
        {
            get
            {
                try { return Path.GetDirectoryName(BrokerExePath); }
                catch { return BrokerDirectory; }
            }
        }

        static void DeletePidFile()
        {
            if (_brokerPid == 0) return;
            TryDelete(Path.Combine(PidDirectory,
                PidFilePrefix + _brokerPid.ToString(CultureInfo.InvariantCulture) + PidFileSuffix));
        }

        static uint NextSeq()
        {
            return ++_seq;
        }

        static uint GetCurrentPid()
        {
            try { return (uint)Process.GetCurrentProcess().Id; }
            catch { return 0; }
        }

        static void SetState(VpbNetLinkState state)
        {
            _state = state;
            _stateEnteredMs = Clock.ElapsedMilliseconds;
        }

        static void Fail(string message)
        {
            _lastError = message;
            SetState(VpbNetLinkState.Failed);
            LogUtil.LogError("[VPB.Net] " + message);
        }

        public static string DescribeStatus()
        {
            return string.Format(
                "state={0} brokerPid={1} build={2} wireRtt={3:0.000}/{4:0.000}ms drainLag={5:0.000}/{6:0.000}ms pings={7}/{8} echoes={9}/{10} malformed={11} dropIn={12} dropOut={13}",
                _state, _brokerPid, _brokerBuild, LastRttMs, MeanRttMs,
                LastDrainLagMs, MeanDrainLagMs,
                PongsReceived, PingsSent, EchoRepliesReceived, EchoesSent,
                MalformedDropped, InboundDropped, OutboundDropped);
        }

        public static string DescribeSession()
        {
            return DescribeSession(FirstPeer());
        }

        public static string DescribeSession(int peer)
        {
            return string.Format(
                "session={0} peers={1} data={2}/{3} sendFail={4} peer{5}: rtt={6:0.00}ms jitter={7:0.00}ms remoteSent={8} remoteRecv={9} lost={10} reordered={11}",
                SessionState, PeerCount, DataReceived, DataSent, DataSendFailed,
                peer < 0 ? 0 : peer,
                PeerRttMs(peer), PeerJitterMs(peer),
                PeerRemoteSentCount(peer), PeerRemoteReceivedCount(peer),
                PeerLostCount(peer), PeerReorderedCount(peer));
        }
    }
}
