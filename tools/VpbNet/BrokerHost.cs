using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using VpbNet.Transport;
using VpbNet.Transport.Steam;

namespace VpbNet
{
    public sealed class BrokerHost : IDisposable
    {
        public const ushort BrokerBuild = 3;

        const int WaitMicros = 2000;
        const int StatsIntervalMs = 1000;
        const int MaxOutboundQueue = 256;

        sealed class Outbound
        {
            public byte[] Buffer;
            public int Length;
            public int PeerId;
            public byte Channel;
        }

        readonly byte[] _rx = new byte[VpbIpc.MaxDatagram];
        readonly byte[] _tx = new byte[VpbIpc.MaxDatagram];
        readonly byte[] _transportBuf = new byte[VpbIpc.MaxDatagram];
        readonly byte[] _logBuf = new byte[VpbIpc.MaxDatagram];
        readonly byte[] _token = new byte[VpbIpc.TokenSize];
        readonly byte[] _expectedSecret = new byte[VpbIpc.SecretSize];
        readonly byte[] _receivedSecret = new byte[VpbIpc.SecretSize];
        readonly List<Socket> _waitList = new List<Socket>(2);
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly List<Outbound> _outbound = new List<Outbound>(MaxOutboundQueue);
        readonly Queue<Outbound> _outboundFree = new Queue<Outbound>(MaxOutboundQueue);
        readonly bool[] _outboundBlocked = new bool[16];

        long _outboundSent;
        long _outboundDropped;
        int _outboundPeak;
        bool _warnedOutbound;

        readonly int _pluginPort;
        readonly uint _parentPid;

        ISessionTransport _transport;
        byte _backendId;
        bool _echoMode;
        string _hostRoomCode;
        string _hostRoomDisplay;
        string _inviteRoomCode;
        VpbIpcSession _lastSessionState;
        string _lastSessionText;
        string _inviteCache;
        string _inviteCacheFor;
        int _peerCount;
        long _lastStatsMs;


        Socket _socket;
        IPEndPoint _pluginEndpoint;
        Process _parent;

        bool _bound;
        uint _seq;
        long _lastInboundMs;
        bool _warnedStalled;
        long _windowStartMs;
        int _windowMessages;
        int _rateLimitedDrops;

        public BrokerHost(int pluginPort, uint parentPid, byte[] secret)
        {
            _pluginPort = pluginPort;
            _parentPid = parentPid;
            Buffer.BlockCopy(secret, 0, _expectedSecret, 0, VpbIpc.SecretSize);
        }

        public int Port { get; private set; }
        public bool ShouldExit { get; private set; }
        public string ExitReason { get; private set; }

        public void Start()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            _socket.Blocking = false;
            Port = ((IPEndPoint)_socket.LocalEndPoint).Port;

            _pluginEndpoint = new IPEndPoint(IPAddress.Loopback, _pluginPort);

            try { _parent = _parentPid > 0 ? Process.GetProcessById((int)_parentPid) : null; }
            catch { _parent = null; }

            _lastInboundMs = _clock.ElapsedMilliseconds;
            _windowStartMs = _lastInboundMs;

            Console.Out.WriteLine("READY " + Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Console.Out.Flush();
        }

        public void RunOnce()
        {
            long now = _clock.ElapsedMilliseconds;

            bool worked = ReceiveIpc(now);

            if (_transport != null)
            {
                _transport.Poll(now);
                worked |= DrainOutbound();
                worked |= PumpTransport();
                PumpPeerEvents();
                PumpStats(now);
                PumpStatusHint();
                CheckTransportFailure();
            }

            CheckLiveness(now);

            if (!worked && !ShouldExit) Wait();
        }

        void Wait()
        {
            _waitList.Clear();
            _waitList.Add(_socket);
            if (_transport != null) _transport.CollectWaitSockets(_waitList);
            try { Socket.Select(_waitList, null, null, WaitMicros); }
            catch { }
        }

        bool ReceiveIpc(long now)
        {
            bool any = false;
            EndPoint from = new IPEndPoint(IPAddress.Loopback, 0);

            while (true)
            {
                int n;
                try { n = _socket.ReceiveFrom(_rx, 0, _rx.Length, SocketFlags.None, ref from); }
                catch (SocketException se)
                {
                    if (se.SocketErrorCode == SocketError.WouldBlock) return any;
                    if (se.SocketErrorCode == SocketError.ConnectionReset) continue;
                    Exit("socket error: " + se.SocketErrorCode);
                    return any;
                }
                catch { return any; }

                if (n <= 0) return any;
                any = true;

                IPEndPoint sender = from as IPEndPoint;
                if (sender == null || !IPAddress.IsLoopback(sender.Address)) continue;
                if (sender.Port != _pluginPort) continue;

                if (!RateLimitOk(now)) continue;

                byte version;
                VpbIpcMsg type;
                uint seq;
                int payloadOffset;
                int payloadLen;
                if (!VpbIpc.TryReadHeader(_rx, n, out version, out type, out seq, out payloadOffset, out payloadLen)) continue;

                if (version != VpbIpc.IpcVersion)
                {
                    if (type == VpbIpcMsg.Hello)
                    {
                        int len = VpbIpc.WriteRejectVersioned(_tx, version, seq, VpbIpcReject.IpcVersionMismatch,
                            "broker speaks IPC v" + VpbIpc.IpcVersion + ", plugin speaks v" + version);
                        SendRaw(len);
                        Exit("ipc version mismatch (plugin v" + version + ")");
                    }
                    continue;
                }

                _lastInboundMs = now;
                Dispatch(type, seq, payloadLen);
            }
        }

        void Dispatch(VpbIpcMsg type, uint seq, int payloadLen)
        {
            switch (type)
            {
                case VpbIpcMsg.Hello:
                    HandleHello(seq, payloadLen);
                    break;

                case VpbIpcMsg.Ping:
                    if (!Authenticated()) return;
                    if (payloadLen >= 8)
                    {
                        long pluginTicks = VpbIpc.ReadI64(_rx, VpbIpc.HeaderSize);
                        SendRaw(VpbIpc.WritePong(_tx, NextSeq(), _token, pluginTicks, _clock.ElapsedTicks));
                    }
                    break;

                case VpbIpcMsg.Echo:
                    if (!Authenticated()) return;
                    if (payloadLen > 0 && payloadLen <= VpbIpc.MaxEchoPayload && EnsureEchoTransport())
                        _transport.Send(LoopbackEchoTransport.SelfPeerId, _rx, VpbIpc.HeaderSize, payloadLen, 0, false);
                    break;

                case VpbIpcMsg.OpenSession:
                    if (!Authenticated()) return;
                    HandleOpenSession(seq, payloadLen);
                    break;

                case VpbIpcMsg.CloseSession:
                    if (!Authenticated()) return;
                    CloseSession("closed by the plugin");
                    break;

                case VpbIpcMsg.Data:
                    if (!Authenticated()) return;
                    HandleData(payloadLen);
                    break;

                case VpbIpcMsg.Bye:
                    if (!Authenticated()) return;
                    Exit("plugin said goodbye");
                    break;
            }
        }

        void HandleHello(uint seq, int payloadLen)
        {
            if (_bound)
            {
                SendRaw(VpbIpc.WriteReject(_tx, seq, VpbIpcReject.AlreadyBound, "a plugin is already bound to this broker"));
                return;
            }

            ushort appProto;
            uint pluginPid;
            if (!VpbIpc.ReadHello(_rx, payloadLen, _receivedSecret, out appProto, out pluginPid))
            {
                SendRaw(VpbIpc.WriteReject(_tx, seq, VpbIpcReject.Malformed, "malformed hello"));
                return;
            }

            int diff = 0;
            for (int i = 0; i < VpbIpc.SecretSize; i++) diff |= _expectedSecret[i] ^ _receivedSecret[i];
            if (diff != 0)
            {
                SendRaw(VpbIpc.WriteReject(_tx, seq, VpbIpcReject.BadSecret, "bad launch secret"));
                Log(2, "rejected a hello with a bad secret from port " + _pluginPort);
                return;
            }

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(_token);
            }

            _bound = true;
            SendRaw(VpbIpc.WriteWelcome(_tx, NextSeq(), _token, BrokerBuild, (uint)Process.GetCurrentProcess().Id));
            Log(0, "bound to plugin pid " + pluginPid + " appProto=v" + appProto);
        }

        void HandleOpenSession(uint seq, int payloadLen)
        {
            if (_transport != null && !_echoMode)
            {
                SendRaw(VpbIpc.WriteReject(_tx, seq, VpbIpcReject.SessionBusy, "a session is already open on this broker"));
                return;
            }

            byte backendId;
            byte role;
            byte maxPeers;
            string roomCode;
            string connectBlob;
            if (!VpbIpc.ReadOpenSession(_rx, payloadLen, out backendId, out role, out maxPeers, out roomCode, out connectBlob))
            {
                SendRaw(VpbIpc.WriteReject(_tx, seq, VpbIpcReject.Malformed, "malformed open-session"));
                return;
            }

            CloseTransport();

            _inviteRoomCode = null;
            _hostRoomCode = null;
            bool steam = (VpbIpcBackend)backendId == VpbIpcBackend.Steam;
            bool wasInvite = VpbNetInviteCode.LooksLikeInvite(roomCode);
            if (wasInvite)
            {
                string resolvedRoom, resolvedBlob;
                bool addressOverridden;
                VpbNetInviteFault fault;
                if (!VpbNetInviteCode.TryResolveJoinTarget(roomCode, connectBlob,
                        out resolvedRoom, out resolvedBlob, out addressOverridden, out fault))
                {
                    SendRaw(VpbIpc.WriteReject(_tx, seq, VpbIpcReject.Malformed, VpbNetInviteCode.Explain(fault)));
                    return;
                }

                roomCode = resolvedRoom;
                _inviteRoomCode = resolvedRoom;

                if (steam)
                {
                    Log(0, "invite accepted: taking the room code out of it and connecting over Steam."
                        + " The address it carries is not used, and the other player never learns yours.");
                }
                else if (addressOverridden)
                {
                    connectBlob = resolvedBlob;
                    Log(1, "invite accepted: joining " + resolvedBlob + " - IGNORING the address field, which still says something else. Clear it if you meant to dial that instead.");
                }
                else
                {
                    connectBlob = resolvedBlob;
                    Log(0, "invite accepted: joining " + resolvedBlob);
                }
            }

            TransportOptions options = new TransportOptions();
            options.Role = role == (byte)TransportRole.Join ? TransportRole.Join : TransportRole.Host;
            options.MaxPeers = maxPeers < 1 ? 1 : (maxPeers > VpbIpc.MaxPeers ? VpbIpc.MaxPeers : maxPeers);
            options.ConnectBlob = connectBlob;
            SessionAuth.RoomKeys keys = SessionAuth.Derive(roomCode);
            options.SessionKey = keys.SessionKey;
            options.LobbyToken = keys.LobbyToken;
            _hostRoomCode = VpbNetRoomCode.Normalize(roomCode);
            _hostRoomDisplay = _hostRoomCode != null
                ? VpbNetRoomCode.Group(_hostRoomCode)
                : (roomCode == null ? string.Empty : roomCode.Trim());

            ISessionTransport backend;
            switch ((VpbIpcBackend)backendId)
            {
                case VpbIpcBackend.Lan:
                    backend = new LanUdpTransport(VpbIpc.MaxDatagram, Log);
                    break;
                case VpbIpcBackend.LoopbackEcho:
                    backend = new LoopbackEchoTransport(VpbIpc.MaxDatagram, 64);
                    break;
                case VpbIpcBackend.Steam:
                    backend = new SteamP2PTransport(VpbIpc.MaxDatagram, BrokerDirectory(),
                        Environment.GetEnvironmentVariable("VPB_STEAM_API"),
                        _hostRoomDisplay, Log);
                    break;
                default:
                    SendRaw(VpbIpc.WriteReject(_tx, seq, VpbIpcReject.UnknownBackend, "this broker has no backend " + backendId));
                    return;
            }

            _transport = backend;

            _backendId = backendId;
            _echoMode = false;
            _peerCount = 0;
            _lastStatsMs = _clock.ElapsedMilliseconds;

            _inviteCache = null;
            _inviteCacheFor = null;

            try { _transport.Start(options); }
            catch (Exception e)
            {
                string reason = "backend failed to start: " + e.Message;
                CloseTransport();
                SendSessionState(VpbIpcSession.Failed, string.Empty, reason);
                Log(2, reason);
                return;
            }

            if (_transport.FailureReason != null)
            {
                string reason = _transport.FailureReason;
                CloseTransport();
                SendSessionState(VpbIpcSession.Failed, string.Empty, reason);
                Log(2, reason);
                return;
            }

            SendSessionState(
                options.Role == TransportRole.Host ? VpbIpcSession.Listening : VpbIpcSession.Connecting,
                CurrentInvite(),
                options.Role == TransportRole.Host ? "hosting on " + _transport.Name : "connecting over " + _transport.Name);
        }

        void HandleData(int payloadLen)
        {
            int peerId;
            byte channel;
            byte flags;
            int dataOffset;
            int dataLen;
            if (!VpbIpc.ReadDataHeader(_rx, payloadLen, out peerId, out channel, out flags, out dataOffset, out dataLen)) return;
            if (_transport == null || dataLen <= 0) return;

            bool reliable = (flags & VpbIpc.DataFlagReliable) != 0;

            if (!reliable)
            {
                _transport.Send(peerId, _rx, dataOffset, dataLen, channel, false);
                return;
            }

            // If this peer already has queued bytes, later reliable msgs queue behind — no overtake.
            if (QueuedFor(peerId) > 0 || !_transport.Send(peerId, _rx, dataOffset, dataLen, channel, true))
                QueueOutbound(peerId, _rx, dataOffset, dataLen, channel);
        }

        // Reliable Send can refuse when the ack window is full; queue so we do not drop.
        void QueueOutbound(int peerId, byte[] src, int offset, int len, byte channel)
        {
            if (len <= 0 || len > VpbIpc.MaxDatagram) return;

            if (_outbound.Count >= MaxOutboundQueue)
            {
                _outboundDropped++;
                if (!_warnedOutbound)
                {
                    _warnedOutbound = true;
                    Log(2, "the send queue for peer " + peerId + " is full (" + MaxOutboundQueue
                        + " messages waiting to be acknowledged) - dropping changes. The other side is not"
                        + " acknowledging anything; the session is about to be reported as timed out.");
                }
                return;
            }

            Outbound o = _outboundFree.Count > 0 ? _outboundFree.Dequeue() : new Outbound();
            if (o.Buffer == null) o.Buffer = new byte[VpbIpc.MaxDatagram];
            Buffer.BlockCopy(src, offset, o.Buffer, 0, len);
            o.Length = len;
            o.PeerId = peerId;
            o.Channel = channel;
            _outbound.Add(o);
            if (_outbound.Count > _outboundPeak) _outboundPeak = _outbound.Count;
        }

        int QueuedFor(int peerId)
        {
            if (_outbound.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < _outbound.Count; i++)
            {
                if (_outbound[i].PeerId == peerId) n++;
            }
            return n;
        }

        // Drain per-peer: a full window blocks only that peer, preserving order.
        bool DrainOutbound()
        {
            if (_outbound.Count == 0) return false;

            Array.Clear(_outboundBlocked, 0, _outboundBlocked.Length);
            bool any = false;
            int write = 0;

            for (int i = 0; i < _outbound.Count; i++)
            {
                Outbound o = _outbound[i];
                int slot = o.PeerId >= 0 && o.PeerId < _outboundBlocked.Length ? o.PeerId : 0;

                if (!_outboundBlocked[slot] && _transport.Send(o.PeerId, o.Buffer, 0, o.Length, o.Channel, true))
                {
                    _outboundFree.Enqueue(o);
                    _outboundSent++;
                    any = true;
                    continue;
                }

                _outboundBlocked[slot] = true;
                _outbound[write++] = o;
            }

            _outbound.RemoveRange(write, _outbound.Count - write);
            if (_outbound.Count == 0) _warnedOutbound = false;
            return any;
        }

        void ClearOutbound()
        {
            for (int i = 0; i < _outbound.Count; i++) _outboundFree.Enqueue(_outbound[i]);
            _outbound.Clear();
            _warnedOutbound = false;
        }

        void ClearOutboundFor(int peerId)
        {
            int write = 0;
            for (int i = 0; i < _outbound.Count; i++)
            {
                Outbound o = _outbound[i];
                if (o.PeerId == peerId) _outboundFree.Enqueue(o);
                else _outbound[write++] = o;
            }
            _outbound.RemoveRange(write, _outbound.Count - write);
            if (_outbound.Count == 0) _warnedOutbound = false;
        }

        bool EnsureEchoTransport()
        {
            if (_transport != null) return true;

            _transport = new LoopbackEchoTransport(VpbIpc.MaxDatagram, 64);
            _transport.Start(new TransportOptions { Role = TransportRole.Host, MaxPeers = 1 });
            _backendId = (byte)VpbIpcBackend.LoopbackEcho;
            _echoMode = true;
            return true;
        }

        bool PumpTransport()
        {
            bool any = false;
            int peerId;
            byte channel;
            int len;
            while ((len = _transport.Receive(_transportBuf, out peerId, out channel)) > 0)
            {
                any = true;
                if (!_bound) continue;

                if (_echoMode) SendRaw(VpbIpc.WriteEcho(_tx, NextSeq(), _token, _transportBuf, len, true));
                else SendRaw(VpbIpc.WriteData(_tx, NextSeq(), _token, peerId, channel, 0, _transportBuf, 0, len));
            }
            return any;
        }

        void PumpPeerEvents()
        {
            int peerId;
            PeerEventKind kind;
            string reason;
            while (_transport.NextPeerEvent(out peerId, out kind, out reason))
            {
                if (kind == PeerEventKind.Up) _peerCount++;
                else if (kind == PeerEventKind.Down && _peerCount > 0) _peerCount--;

                if (kind == PeerEventKind.Up || kind == PeerEventKind.Down) ClearOutboundFor(peerId);

                if (_echoMode) continue;

                SendRaw(VpbIpc.WritePeerEvent(_tx, NextSeq(), _token, (byte)kind, peerId, reason));

                if (kind == PeerEventKind.Up) SendSessionState(VpbIpcSession.Connected, CurrentInvite(), reason);
                else if (kind == PeerEventKind.Down && _peerCount == 0)
                    SendSessionState(VpbIpcSession.Listening, CurrentInvite(), "waiting for a peer");
            }
        }

        void PumpStats(long now)
        {
            if (_echoMode || _peerCount == 0) return;
            if (now - _lastStatsMs < StatsIntervalMs) return;
            _lastStatsMs = now;

            for (int peerId = 1; peerId <= VpbIpc.MaxPeers; peerId++)
            {
                PeerStats s;
                if (!_transport.TryGetStats(peerId, out s)) continue;
                SendRaw(VpbIpc.WritePeerStats(_tx, NextSeq(), _token, peerId,
                    s.Sent, s.Received, s.Lost, s.Reordered, s.RttMicros, s.JitterMicros));
            }
        }

        void CheckTransportFailure()
        {
            string reason = _transport.FailureReason;
            if (reason == null) return;

            CloseTransport();
            SendSessionState(VpbIpcSession.Failed, string.Empty, reason);
            Log(2, reason);
        }

        void CloseSession(string reason)
        {
            if (_transport == null) return;
            CloseTransport();
            SendSessionState(VpbIpcSession.Closed, string.Empty, reason);
        }

        static string BrokerDirectory()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string dir = System.IO.Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            catch { }
            try { return AppContext.BaseDirectory; }
            catch { return null; }
        }

        string CurrentInvite()
        {
            if (_transport == null) return string.Empty;

            if (_backendId == (byte)VpbIpcBackend.Steam)
                return _hostRoomDisplay == null ? string.Empty : _hostRoomDisplay;

            string blob = _transport.InviteBlob;
            if (string.IsNullOrEmpty(blob)) return string.Empty;
            if (_hostRoomCode == null) return blob;
            if (blob.StartsWith(LanUdpTransport.RendezvousPrefix, StringComparison.OrdinalIgnoreCase)) return blob;

            if (_inviteCache != null && string.Equals(_inviteCacheFor, blob, StringComparison.Ordinal))
                return _inviteCache;

            VpbNetEndpoint ep;
            string built = null;
            if (TryParseEndpoint(blob, out ep))
            {
                string code = VpbNetInviteCode.Create(_hostRoomCode, ep);
                if (code != null) built = VpbNetInviteCode.Group(code);
            }

            _inviteCacheFor = blob;
            _inviteCache = built != null ? built : blob;
            return _inviteCache;
        }

        static bool TryParseEndpoint(string blob, out VpbNetEndpoint ep)
        {
            ep = new VpbNetEndpoint();
            if (string.IsNullOrEmpty(blob)) return false;

            string text = blob.Trim();
            int colon = text.LastIndexOf(':');
            if (colon <= 0 || colon == text.Length - 1) return false;

            int port;
            if (!int.TryParse(text.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return false;
            if (port <= 0 || port > 65535) return false;

            string hostPart = text.Substring(0, colon).Trim();
            if (hostPart.StartsWith("[", StringComparison.Ordinal) && hostPart.EndsWith("]", StringComparison.Ordinal))
                hostPart = hostPart.Substring(1, hostPart.Length - 2);

            IPAddress address;
            if (!IPAddress.TryParse(hostPart, out address)) return false;

            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4) ep.Family = VpbNetRendezvous.FamilyV4;
            else if (bytes.Length == 16) ep.Family = VpbNetRendezvous.FamilyV6;
            else return false;

            ep.Address = bytes;
            ep.Port = (ushort)port;
            return true;
        }

        void CloseTransport()
        {
            if (_transport == null) return;

            try
            {
                if (_outboundSent > 0 || _outboundDropped > 0)
                    Log(_outboundDropped > 0 ? (byte)1 : (byte)0, "reliable send queue: " + _outboundSent
                        + " message(s) held until the transport had room, " + _outboundDropped
                        + " dropped, deepest " + _outboundPeak + " of " + MaxOutboundQueue);
            }
            catch { }

            ClearOutbound();
            _outboundSent = 0;
            _outboundDropped = 0;
            _outboundPeak = 0;

            try { _transport.Dispose(); }
            catch { }
            _transport = null;
            _echoMode = false;
            _peerCount = 0;
        }

        void SendSessionState(VpbIpcSession state, string invite, string text)
        {
            if (!_bound) return;
            _lastSessionState = state;
            _lastSessionText = text;
            SendRaw(VpbIpc.WriteSessionState(_tx, NextSeq(), _token, (byte)state, _backendId, _peerCount, invite, text));
        }

        void PumpStatusHint()
        {
            if (_transport == null || !_bound) return;
            if (_lastSessionState != VpbIpcSession.Listening && _lastSessionState != VpbIpcSession.Connecting) return;

            string hint = _transport.StatusHint;
            if (string.IsNullOrEmpty(hint)) return;
            if (string.Equals(hint, _lastSessionText, StringComparison.Ordinal)) return;

            SendSessionState(_lastSessionState, CurrentInvite(), hint);
        }

        void CheckLiveness(long now)
        {
            if (ShouldExit) return;

            long silent = now - _lastInboundMs;
            if (silent > VpbIpc.StalledTimeoutMs)
            {
                Exit("no plugin traffic for " + VpbIpc.StalledTimeoutMs + "ms");
                return;
            }

            // Warn once on stall; hitch is not a hang (content load freezes the pump).
            if (silent > VpbIpc.StallWarnMs)
            {
                if (!_warnedStalled)
                {
                    _warnedStalled = true;
                    Log(1, "VaM has not pumped this link for " + (silent / 1000) + "s"
                        + " - holding the session open (loading content freezes it);"
                        + " giving up at " + (VpbIpc.StalledTimeoutMs / 1000) + "s");
                }
            }
            else _warnedStalled = false;

            if (_parent != null)
            {
                bool gone = false;
                try { gone = _parent.HasExited; }
                catch { gone = true; }
                if (gone) Exit("parent process exited");
            }
        }

        bool Authenticated()
        {
            if (!_bound) return false;
            return VpbIpc.TokenMatches(_rx, _token);
        }

        bool RateLimitOk(long now)
        {
            if (now - _windowStartMs >= 1000)
            {
                _windowStartMs = now;
                _windowMessages = 0;
            }
            _windowMessages++;
            if (_windowMessages <= VpbIpc.MaxMessagesPerSecond) return true;

            _rateLimitedDrops++;
            if (_rateLimitedDrops == 1) Log(1, "rate limit hit, dropping messages");
            return false;
        }

        void SendRaw(int len)
        {
            try { _socket.SendTo(_tx, 0, len, SocketFlags.None, _pluginEndpoint); }
            catch { }
        }

        public void Log(byte level, string text)
        {
            if (_bound)
            {
                try
                {
                    int len = VpbIpc.WriteLog(_logBuf, NextSeq(), _token, level, text);
                    _socket.SendTo(_logBuf, 0, len, SocketFlags.None, _pluginEndpoint);
                    return;
                }
                catch { }
            }
            Console.Out.WriteLine(text);
            Console.Out.Flush();
        }

        void Exit(string reason)
        {
            if (ShouldExit) return;
            ShouldExit = true;
            ExitReason = reason;
        }

        uint NextSeq()
        {
            return ++_seq;
        }

        public void Dispose()
        {
            CloseTransport();

            try { SteamNative.Stop(); }
            catch { }

            try { if (_socket != null) _socket.Close(); }
            catch { }
            _socket = null;

            try { if (_parent != null) _parent.Dispose(); }
            catch { }
            _parent = null;
        }
    }
}
