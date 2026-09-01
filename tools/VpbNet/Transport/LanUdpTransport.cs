using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace VpbNet.Transport
{
    public sealed class LanUdpTransport : ISessionTransport
    {
        public const int DefaultPort = 47772;
        public const byte LanVersion = 2;
        public const ushort AppProto = 1;

        const int HeaderSize = 16;
        const int TagSize = SessionAuth.TagBytes;
        const int OverheadSize = HeaderSize + TagSize;
        const int HardMaxPeers = 8;
        const int SlotCount = 128;

        const int ConnectRetryMs = 500;
        const int ConnectTimeoutMs = 15000;
        const int PingIntervalMs = 500;
        const int StallMs = 2000;
        const int DropMs = 10000;
        const int UnauthWindowMs = 1000;
        const int MaxUnauthPerWindow = 64;
        const int ReorderWindow = 256;

        const byte FlagReliable = 1;
        const int RelSeqBytes = 4;
        const int MaxInFlight = 64;
        const int MaxHeld = 32;
        const int MaxAckPerDatagram = 16;
        const int MaxRelAttempts = 10;
        public const int DefaultRelLifetimeMs = 20000;
        public const int DefaultRelHoldMs = 25000;
        const int RelMinRtoMs = 60;
        const int RelMaxRtoMs = 1000;
        const int RelPoolSlots = 160;

        enum LanMsg : byte
        {
            None = 0,
            Connect = 1,
            Accept = 2,
            Bye = 3,
            Data = 4,
            Ping = 5,
            Pong = 6,
            Ack = 7,
        }

        sealed class RelPending
        {
            public uint Seq;
            public byte[] Buf;
            public int Len;
            public long FirstMs;
            public long NextMs;
            public int Attempts;
        }

        sealed class RelHeld
        {
            public uint Seq;
            public byte[] Buf;
            public int Len;
            public byte Channel;
        }

        sealed class Peer
        {
            public bool InUse;
            public bool Up;
            public bool Stalled;
            public IPEndPoint Endpoint;
            public uint ConnId;
            public uint TxSeq;
            public uint RxSeq;
            public bool RxSeqValid;
            public long LastRxMs;
            public long LastPingMs;
            public double RttMs;
            public double JitterMs;
            public bool RttValid;
            public PeerStats Stats;

            public uint RelTxSeq;
            public uint RelExpected;
            public readonly List<RelPending> InFlight = new List<RelPending>(MaxInFlight);
            public readonly List<RelHeld> Held = new List<RelHeld>(MaxHeld);
            public readonly List<uint> AckOut = new List<uint>(MaxAckPerDatagram);
            public uint RelSent;
            public uint RelResent;
            public uint RelAbandoned;
            public uint RelDuplicates;
            public uint RelSkipped;
            public long HoldSinceMs;
            public bool WarnedFull;
        }

        struct PendingRx
        {
            public byte[] Buffer;
            public int Length;
            public int PeerId;
            public byte Channel;
        }

        struct PendingEvent
        {
            public int PeerId;
            public PeerEventKind Kind;
            public string Reason;
        }

        readonly Action<byte, string> _log;
        readonly byte[] _rx = new byte[OverheadSize + 1400];
        readonly byte[] _tx = new byte[OverheadSize + 1400];
        readonly Peer[] _peers = new Peer[HardMaxPeers];
        readonly Queue<byte[]> _free = new Queue<byte[]>();
        readonly Queue<byte[]> _relFree = new Queue<byte[]>();
        readonly Queue<PendingRx> _pending = new Queue<PendingRx>();
        readonly Queue<PendingEvent> _events = new Queue<PendingEvent>();

        SessionMac _mac;
        Socket _socket;
        TransportRole _role;
        IPEndPoint _hostEndpoint;
        int _maxPeers;
        uint _connectNonce;

        readonly Rendezvous.RendezvousClient _rv = new Rendezvous.RendezvousClient();
        readonly byte[] _rvTx = new byte[VpbNetRendezvous.RequestBytes];
        readonly byte[] _lobbyToken = new byte[VpbNetRendezvous.TokenBytes];
        readonly byte[] _relayToken = new byte[VpbNetRendezvous.TokenBytes];
        readonly byte[] _relayTicket = new byte[VpbNetRendezvous.TicketBytes];
        readonly byte[] _relayTx = new byte[VpbIpc.MaxDatagram + VpbNetRendezvous.RelayHeaderBytes];
        IPEndPoint _rvEndpoint;
        bool _rvMode;
        bool _rvActed;
        bool _relayMode;
        long _lastPunchMs;
        long _punchStartedMs;
        long _startedMs;
        long _lastConnectMs;
        long _unauthWindowMs;
        int _unauthInWindow;
        int _unauthTotal;
        int _oversizeDropped;
        bool _connectPending;
        bool _discovering;
        IPEndPoint[] _discoveryTargets;
        long _nowMs;
        int _dropCounter;
        bool _warnedVersion;
        bool _suppressSend;

        public LanUdpTransport(int slotBytes, Action<byte, string> log)
        {
            _log = log;
            for (int i = 0; i < HardMaxPeers; i++) _peers[i] = new Peer();
            for (int i = 0; i < SlotCount; i++) _free.Enqueue(new byte[slotBytes]);
            for (int i = 0; i < RelPoolSlots; i++) _relFree.Enqueue(new byte[_tx.Length]);
        }

        public const string RendezvousPrefix = "rv:";

        public string Name { get { return _relayMode ? "relayed" : (_rvMode ? "direct-udp" : "lan-udp"); } }

        public string InviteBlob { get; private set; }

        public string FailureReason { get; private set; }

        public string StatusHint
        {
            get { return _rvMode ? _rv.ExplainNoPeer() : null; }
        }

        public int Dropped { get; private set; }

        public bool SimulateDirectBlocked;

        public int SimulateDropOneInN;

        public uint SimulateDropRelSeq;

        public int RelLifetimeMs = DefaultRelLifetimeMs;

        public int RelHoldMs = DefaultRelHoldMs;

        public void Start(TransportOptions options)
        {
            InviteBlob = string.Empty;
            _role = options.Role;
            _maxPeers = options.MaxPeers < 1 ? 1 : (options.MaxPeers > HardMaxPeers ? HardMaxPeers : options.MaxPeers);
            _mac = new SessionMac(options.SessionKey);

            _rvMode = false;
            _rvActed = false;
            _relayMode = false;
            _rvEndpoint = null;
            _discovering = false;
            _discoveryTargets = null;
            _lastPunchMs = long.MinValue / 2;
            _punchStartedMs = -1;

            string blob = options.ConnectBlob == null ? string.Empty : options.ConnectBlob.Trim();
            IPEndPoint bind;

            if (blob.StartsWith(RendezvousPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string address = blob.Substring(RendezvousPrefix.Length).Trim();
                if (!TryParseTarget(address, out _rvEndpoint))
                {
                    Fail("cannot read the rendezvous address \"" + Sanitize(address)
                        + "\" - it must be address:port, for example rv:example.org:" + Rendezvous.RendezvousServer.DefaultPort
                        + ". There is no default rendezvous; you use one you were given, or one you run.");
                    return;
                }
                if (options.LobbyToken == null || options.LobbyToken.Length < VpbNetRendezvous.TokenBytes)
                {
                    Fail("the room code produced no rendezvous token - this build cannot use a rendezvous");
                    return;
                }
                Buffer.BlockCopy(options.LobbyToken, 0, _lobbyToken, 0, VpbNetRendezvous.TokenBytes);
                _rvMode = true;
                bind = new IPEndPoint(IPAddress.Any, 0);
            }
            else if (_role == TransportRole.Host)
            {
                if (!TryParseBind(blob, out bind))
                {
                    Fail("cannot read the LAN bind address \"" + Sanitize(blob) + "\" - use a port, or address:port, or leave it empty for " + DefaultPort);
                    return;
                }
            }
            else if (blob.Length == 0)
            {
                // Empty joiner address: broadcast signed Connect; room key is the gate.
                _discovering = true;
                _hostEndpoint = new IPEndPoint(IPAddress.Broadcast, DefaultPort);
                bind = new IPEndPoint(IPAddress.Any, 0);
            }
            else
            {
                if (!TryParseTarget(blob, out _hostEndpoint))
                {
                    Fail("cannot read the LAN host address \"" + Sanitize(blob) + "\" - it must be address:port, for example 192.168.1.42:" + DefaultPort);
                    return;
                }
                bind = new IPEndPoint(IPAddress.Any, 0);
            }

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Blocking = false;
                SuppressConnReset(_socket);
                _socket.Bind(bind);
            }
            catch (SocketException se)
            {
                Fail("cannot bind " + bind + " (" + se.SocketErrorCode + ")"
                    + (se.SocketErrorCode == SocketError.AddressAlreadyInUse
                        ? " - another VPB session or another program already holds that port"
                        : string.Empty));
                return;
            }
            catch (Exception e)
            {
                Fail("cannot bind " + bind + ": " + e.Message);
                return;
            }

            if (_discovering)
            {
                try { _socket.EnableBroadcast = true; }
                catch { }
                _discoveryTargets = BuildDiscoveryTargets(DefaultPort);
            }

            _startedMs = -1;
            _lastConnectMs = long.MinValue / 2;

            byte[] seed = new byte[4];
            RandomNumberGenerator.Fill(seed);
            _connectNonce = BitConverter.ToUInt32(seed, 0);

            if (_rvMode)
            {
                ushort local = (ushort)((IPEndPoint)_socket.LocalEndPoint).Port;
                RandomNumberGenerator.Fill(seed);
                _rv.Start(options.LobbyToken, _role == TransportRole.Host ? VpbNetRendezvous.RoleHost : VpbNetRendezvous.RoleJoin,
                    local, BitConverter.ToUInt32(seed, 0));
                InviteBlob = RendezvousPrefix + _rvEndpoint;
                Log(0, "announcing to rendezvous " + _rvEndpoint + " from UDP " + local
                    + "; the other side uses the same room code and the same rendezvous");
                return;
            }

            if (_role == TransportRole.Host)
            {
                int port = ((IPEndPoint)_socket.LocalEndPoint).Port;
                InviteBlob = BuildInvite(port);
                Log(0, "listening on UDP " + port + "; joiner connects to " + InviteBlob);
            }
            else
            {
                _connectPending = true;
                if (_discovering)
                    Log(0, "no address given: asking this subnet for the host of that room code on UDP "
                        + DefaultPort + " (" + _discoveryTargets.Length + " target"
                        + (_discoveryTargets.Length == 1 ? string.Empty : "s")
                        + "). Only a host holding the same code can answer.");
                else Log(0, "connecting to " + _hostEndpoint);
            }
        }

        public void Poll(long nowMs)
        {
            _nowMs = nowMs;
            if (_socket == null) return;
            if (_startedMs < 0) _startedMs = nowMs;

            DrainSocket(nowMs);

            if (_rvMode) StepRendezvous(nowMs);

            if (_connectPending && nowMs - _lastConnectMs >= ConnectRetryMs)
            {
                _lastConnectMs = nowMs;
                if (nowMs - _startedMs > ConnectTimeoutMs)
                {
                    if (_rvMode && !_relayMode && _rv.HasTicket)
                    {
                        _relayMode = true;
                        _startedMs = nowMs;
                        _lastConnectMs = long.MinValue / 2;
                        Log(0, "direct connection did not open; falling back to the relay at " + _rvEndpoint);
                        return;
                    }

                    _connectPending = false;
                    if (_rvMode)
                    {
                        Fail("found the other peer at " + _hostEndpoint + " but could not open a path in "
                            + (ConnectTimeoutMs / 1000) + "s. Both sides are most likely behind a NAT that refuses"
                            + " direct connections (carrier-grade NAT on mobile or fibre does this). Use a relay,"
                            + " or have one side forward a UDP port and connect to it directly.");
                    }
                    else if (_discovering)
                    {
                        Fail("nobody on this subnet answered for that room code in " + (ConnectTimeoutMs / 1000)
                            + "s - check the other side is hosting with exactly the same code, that it did not override"
                            + " the port (discovery only asks " + DefaultPort + "), and that Windows Firewall is not"
                            + " blocking VpbNet.exe on the hosting machine. Discovery cannot cross subnets or VLANs,"
                            + " and guest Wi-Fi with client isolation blocks it outright: paste their invite instead.");
                    }
                    else
                    {
                        Fail("no reply from " + _hostEndpoint + " after " + (ConnectTimeoutMs / 1000)
                            + "s - check the address, that the other side started hosting, that the room code matches exactly, and that Windows Firewall is not blocking VpbNet.exe");
                    }
                    return;
                }
                SendConnect();
            }

            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (!p.InUse) continue;

                long silent = nowMs - p.LastRxMs;
                if (silent > DropMs)
                {
                    DropPeer(i, "no traffic for " + (DropMs / 1000) + "s");
                    continue;
                }
                if (silent > StallMs && !p.Stalled)
                {
                    p.Stalled = true;
                    Enqueue(i + 1, PeerEventKind.Stalled, "no traffic for " + silent + "ms");
                }
                else if (silent <= StallMs && p.Stalled)
                {
                    p.Stalled = false;
                    Enqueue(i + 1, PeerEventKind.Resumed, "traffic resumed");
                }

                PumpReliable(i, p, nowMs);
                PumpHeld(i, p, nowMs);
                FlushAcks(p);

                if (nowMs - p.LastPingMs >= PingIntervalMs)
                {
                    p.LastPingMs = nowMs;
                    VpbIpc.WriteI64(_tx, HeaderSize, Stopwatch.GetTimestamp());
                    SendTo(p, LanMsg.Ping, 8, 0, 0);
                }
            }
        }

        public bool Send(int peerId, byte[] buffer, int offset, int count, byte channel, bool reliable)
        {
            Peer p = Resolve(peerId);
            if (p == null || !p.Up) return false;

            int extra = reliable ? RelSeqBytes : 0;
            if (count <= 0 || OverheadSize + count + extra > _tx.Length)
            {
                Dropped++;
                return false;
            }

            if (!reliable)
            {
                Buffer.BlockCopy(buffer, offset, _tx, HeaderSize, count);
                return SendTo(p, LanMsg.Data, count, channel, 0);
            }

            if (p.InFlight.Count >= MaxInFlight || _relFree.Count == 0)
            {
                Dropped++;
                if (!p.WarnedFull)
                {
                    p.WarnedFull = true;
                    Log(1, "reliable send queue is full (" + p.InFlight.Count + " unacknowledged) - dropping"
                        + " messages to peer " + peerId + "; the peer is not acknowledging, or the link is down");
                }
                return false;
            }

            uint seq = p.RelTxSeq + 1;
            VpbIpc.WriteU32(_tx, HeaderSize, seq);
            Buffer.BlockCopy(buffer, offset, _tx, HeaderSize + RelSeqBytes, count);
            int payloadLen = count + RelSeqBytes;
            _suppressSend = SimulateDropRelSeq != 0 && SimulateDropRelSeq == seq;
            bool went = SendTo(p, LanMsg.Data, payloadLen, channel, FlagReliable);
            _suppressSend = false;
            if (!went) return false;

            p.RelTxSeq = seq;
            p.WarnedFull = false;

            int total = HeaderSize + payloadLen + TagSize;
            byte[] copy = _relFree.Dequeue();
            Buffer.BlockCopy(_tx, 0, copy, 0, total);

            RelPending entry = new RelPending();
            entry.Seq = seq;
            entry.Buf = copy;
            entry.Len = total;
            entry.FirstMs = _nowMs;
            entry.NextMs = _nowMs + Rto(p);
            entry.Attempts = 0;
            p.InFlight.Add(entry);
            p.RelSent++;
            return true;
        }

        long Rto(Peer p)
        {
            if (!p.RttValid) return RelMinRtoMs * 2;
            long rto = (long)(p.RttMs * 2.0 + p.JitterMs * 4.0);
            if (rto < RelMinRtoMs) rto = RelMinRtoMs;
            if (rto > RelMaxRtoMs) rto = RelMaxRtoMs;
            return rto;
        }

        void PumpReliable(int index, Peer p, long nowMs)
        {
            for (int i = p.InFlight.Count - 1; i >= 0; i--)
            {
                RelPending e = p.InFlight[i];
                if (nowMs < e.NextMs) continue;

                if (e.Attempts >= MaxRelAttempts || nowMs - e.FirstMs > RelLifetimeMs)
                {
                    p.InFlight.RemoveAt(i);
                    _relFree.Enqueue(e.Buf);
                    p.RelAbandoned++;
                    Dropped++;
                    Log(1, "gave up resending a message to peer " + (index + 1) + " after " + MaxRelAttempts
                        + " attempts (" + p.RelAbandoned + " abandoned this session) - the other side will be"
                        + " missing a change until the next resync");
                    continue;
                }

                e.Attempts++;
                int shift = e.Attempts - 1;
                if (shift > 3) shift = 3;
                long backoff = Rto(p) << shift;
                if (backoff > RelMaxRtoMs * 4) backoff = RelMaxRtoMs * 4;
                e.NextMs = nowMs + backoff;
                p.RelResent++;
                if (SimulateDropRelSeq != 0 && SimulateDropRelSeq == e.Seq) continue;
                SendRaw(e.Buf, e.Len, p.Endpoint);
            }
        }

        public int Receive(byte[] buffer, out int peerId, out byte channel)
        {
            peerId = -1;
            channel = 0;
            if (_pending.Count == 0) return 0;

            PendingRx r = _pending.Dequeue();
            int len = r.Length;
            if (len > buffer.Length) len = buffer.Length;
            Buffer.BlockCopy(r.Buffer, 0, buffer, 0, len);
            _free.Enqueue(r.Buffer);
            peerId = r.PeerId;
            channel = r.Channel;
            return len;
        }

        public bool NextPeerEvent(out int peerId, out PeerEventKind kind, out string reason)
        {
            peerId = 0;
            kind = PeerEventKind.None;
            reason = string.Empty;
            if (_events.Count == 0) return false;

            PendingEvent e = _events.Dequeue();
            peerId = e.PeerId;
            kind = e.Kind;
            reason = e.Reason;
            return true;
        }

        public bool TryGetStats(int peerId, out PeerStats stats)
        {
            stats = new PeerStats();
            Peer p = Resolve(peerId);
            if (p == null) return false;

            stats = p.Stats;
            stats.RttMicros = (uint)(p.RttMs * 1000.0);
            stats.JitterMicros = (uint)(p.JitterMs * 1000.0);
            return true;
        }

        public bool TryGetReliableStats(int peerId, out uint sent, out uint resent, out uint abandoned,
            out uint duplicates, out int unacked, out int held)
        {
            sent = 0;
            resent = 0;
            abandoned = 0;
            duplicates = 0;
            unacked = 0;
            held = 0;
            Peer p = Resolve(peerId);
            if (p == null) return false;

            sent = p.RelSent;
            resent = p.RelResent;
            abandoned = p.RelAbandoned;
            duplicates = p.RelDuplicates;
            unacked = p.InFlight.Count;
            held = p.Held.Count;
            return true;
        }

        public void CollectWaitSockets(List<Socket> into)
        {
            if (_socket != null) into.Add(_socket);
        }

        void DrainSocket(long nowMs)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                int n;
                try { n = _socket.ReceiveFrom(_rx, 0, _rx.Length, SocketFlags.None, ref from); }
                catch (SocketException se)
                {
                    if (se.SocketErrorCode == SocketError.WouldBlock) { FlushAllAcks(); return; }
                    if (se.SocketErrorCode == SocketError.ConnectionReset) continue;
                    if (se.SocketErrorCode == SocketError.MessageSize) { _oversizeDropped++; continue; }
                    Fail("socket error: " + se.SocketErrorCode);
                    FlushAllAcks();
                    return;
                }
                catch { FlushAllAcks(); return; }

                IPEndPoint sender = from as IPEndPoint;
                if (sender == null) continue;

                if (_rvMode && n >= 4 && _rx[0] == (byte)'V' && _rx[1] == (byte)'R'
                    && _rvEndpoint != null && sender.Port == _rvEndpoint.Port
                    && sender.Address.Equals(_rvEndpoint.Address))
                {
                    if (_rx[3] == (byte)VpbNetRendezvousOp.Relay)
                    {
                        int off, plen;
                        if (!VpbNetRendezvous.TryReadRelay(_rx, n, _relayToken, _relayTicket, out off, out plen))
                        {
                            CountUnauth(nowMs);
                            continue;
                        }
                        if (plen < OverheadSize || plen > _rx.Length) { CountUnauth(nowMs); continue; }
                        Buffer.BlockCopy(_rx, off, _rx, 0, plen);
                        n = plen;
                        sender = RelayPeerEndpoint();
                        if (sender == null) { CountUnauth(nowMs); continue; }

                        if (!_relayMode)
                        {
                            _relayMode = true;
                            Log(0, "peer reached us through the relay; replying the same way");
                        }
                    }
                    else
                    {
                        _rv.Consume(_rx, n);
                        continue;
                    }
                }

                if (n < OverheadSize) { CountUnauth(nowMs); continue; }

                if (_rx[0] != (byte)'V' || _rx[1] != (byte)'L') { CountUnauth(nowMs); continue; }

                if (_rx[2] != LanVersion)
                {
                    int otherLen = VpbIpc.ReadU16(_rx, 12);
                    if (otherLen >= 0 && HeaderSize + otherLen + TagSize <= n && Verify(_rx, HeaderSize + otherLen))
                    {
                        if (!_warnedVersion)
                        {
                            _warnedVersion = true;
                            Fail("the other side is running a different VPB network build (their transport version "
                                + _rx[2] + ", this one " + LanVersion + ") - both machines need the same VPB version,"
                                + " including VpbNet.exe");
                        }
                    }
                    else CountUnauth(nowMs);
                    continue;
                }

                int payloadLen = VpbIpc.ReadU16(_rx, 12);
                if (payloadLen < 0 || HeaderSize + payloadLen + TagSize > n) { CountUnauth(nowMs); continue; }
                if (!Verify(_rx, HeaderSize + payloadLen)) { CountUnauth(nowMs); continue; }

                LanMsg type = (LanMsg)_rx[3];
                uint connId = VpbIpc.ReadU32(_rx, 4);
                uint seq = VpbIpc.ReadU32(_rx, 8);
                byte channel = _rx[14];
                byte flags = _rx[15];

                Handle(nowMs, sender, type, connId, seq, channel, flags, payloadLen);
            }
        }

        void Handle(long nowMs, IPEndPoint sender, LanMsg type, uint connId, uint seq, byte channel, byte flags, int payloadLen)
        {
            if (type == LanMsg.Connect)
            {
                if (_role != TransportRole.Host || payloadLen < 6) return;
                HandleConnect(nowMs, sender, payloadLen);
                return;
            }

            if (type == LanMsg.Accept)
            {
                if (_role != TransportRole.Join || !_connectPending || payloadLen < 10) return;
                if (VpbIpc.ReadU32(_rx, HeaderSize) != _connectNonce) return;
                uint assigned = VpbIpc.ReadU32(_rx, HeaderSize + 4);
                int slot = Bind(nowMs, sender, assigned);
                if (slot < 0) return;
                _connectPending = false;
                if (_discovering)
                {
                    _discovering = false;
                    _hostEndpoint = sender;
                    Log(0, "found the host at " + sender + " on this subnet");
                }
                Enqueue(slot + 1, PeerEventKind.Up, "connected to " + sender);
                return;
            }

            int index = FindByConn(connId, sender);
            if (index < 0) return;

            Peer p = _peers[index];
            p.LastRxMs = nowMs;

            switch (type)
            {
                case LanMsg.Data:
                    TrackSeq(p, seq);
                    if ((flags & FlagReliable) != 0)
                    {
                        if (payloadLen < RelSeqBytes) return;
                        OnReliableData(index, p, VpbIpc.ReadU32(_rx, HeaderSize),
                            HeaderSize + RelSeqBytes, payloadLen - RelSeqBytes, channel);
                    }
                    else QueueData(index, HeaderSize, payloadLen, channel);
                    break;

                case LanMsg.Ack:
                    HandleAck(p, payloadLen);
                    break;

                case LanMsg.Ping:
                    if (payloadLen < 8) return;
                    Buffer.BlockCopy(_rx, HeaderSize, _tx, HeaderSize, 8);
                    SendTo(p, LanMsg.Pong, 8, 0, 0);
                    break;

                case LanMsg.Pong:
                    if (payloadLen < 8) return;
                    OnPong(p, Stopwatch.GetTimestamp() - VpbIpc.ReadI64(_rx, HeaderSize));
                    break;

                case LanMsg.Bye:
                    DropPeer(index, "peer left the session");
                    break;
            }
        }

        void HandleConnect(long nowMs, IPEndPoint sender, int payloadLen)
        {
            uint nonce = VpbIpc.ReadU32(_rx, HeaderSize);
            int existing = FindByEndpoint(sender);

            uint connId;
            int slot;
            if (existing >= 0)
            {
                slot = existing;
                connId = _peers[existing].ConnId;
            }
            else
            {
                if (CountPeers() >= _maxPeers)
                {
                    Log(1, "refused a connection from " + sender + ": session is full (" + _maxPeers + ")");
                    return;
                }
                byte[] idBytes = new byte[4];
                RandomNumberGenerator.Fill(idBytes);
                connId = BitConverter.ToUInt32(idBytes, 0);
                if (connId == 0) connId = 1;
                slot = Bind(nowMs, sender, connId);
                if (slot < 0) return;
                Enqueue(slot + 1, PeerEventKind.Up, "peer joined from " + sender);
            }

            VpbIpc.WriteU32(_tx, HeaderSize, nonce);
            VpbIpc.WriteU32(_tx, HeaderSize + 4, connId);
            VpbIpc.WriteU16(_tx, HeaderSize + 8, AppProto);
            SendTo(_peers[slot], LanMsg.Accept, 10, 0, 0);
        }

        void QueueData(int index, int offset, int payloadLen, byte channel)
        {
            QueueBytes(index, _rx, offset, payloadLen, channel);
        }

        void QueueBytes(int index, byte[] src, int offset, int payloadLen, byte channel)
        {
            if (payloadLen <= 0) return;
            if (_free.Count == 0)
            {
                Dropped++;
                return;
            }

            byte[] slot = _free.Dequeue();
            int len = payloadLen > slot.Length ? slot.Length : payloadLen;
            Buffer.BlockCopy(src, offset, slot, 0, len);
            _pending.Enqueue(new PendingRx { Buffer = slot, Length = len, PeerId = index + 1, Channel = channel });
            _peers[index].Stats.Received++;
        }

        void OnReliableData(int index, Peer p, uint seq, int offset, int len, byte channel)
        {
            if (seq < p.RelExpected)
            {
                p.RelDuplicates++;
                QueueAck(p, seq);
                return;
            }

            if (seq == p.RelExpected)
            {
                QueueData(index, offset, len, channel);
                p.RelExpected = seq + 1;
                QueueAck(p, seq);
                DrainHeld(index, p);
                return;
            }

            for (int i = 0; i < p.Held.Count; i++)
            {
                if (p.Held[i].Seq != seq) continue;
                p.RelDuplicates++;
                QueueAck(p, seq);
                return;
            }

            if (p.Held.Count >= MaxHeld || _relFree.Count == 0) return;

            if (p.Held.Count == 0) p.HoldSinceMs = _nowMs;

            RelHeld held = new RelHeld();
            held.Seq = seq;
            held.Buf = _relFree.Dequeue();
            held.Len = len > held.Buf.Length ? held.Buf.Length : len;
            held.Channel = channel;
            Buffer.BlockCopy(_rx, offset, held.Buf, 0, held.Len);
            p.Held.Add(held);
            QueueAck(p, seq);
        }

        void DrainHeld(int index, Peer p)
        {
            bool moved = true;
            while (moved && p.Held.Count > 0)
            {
                moved = false;
                for (int i = 0; i < p.Held.Count; i++)
                {
                    RelHeld h = p.Held[i];
                    if (h.Seq != p.RelExpected) continue;
                    QueueBytes(index, h.Buf, 0, h.Len, h.Channel);
                    p.RelExpected = h.Seq + 1;
                    p.Held.RemoveAt(i);
                    _relFree.Enqueue(h.Buf);
                    moved = true;
                    break;
                }
            }
            if (p.Held.Count > 0) p.HoldSinceMs = _nowMs;
        }

        void PumpHeld(int index, Peer p, long nowMs)
        {
            if (p.Held.Count == 0) return;
            if (nowMs - p.HoldSinceMs <= RelHoldMs) return;

            uint lowest = p.Held[0].Seq;
            for (int i = 1; i < p.Held.Count; i++)
            {
                if (p.Held[i].Seq < lowest) lowest = p.Held[i].Seq;
            }

            uint skipped = lowest - p.RelExpected;
            p.RelSkipped += skipped;
            p.RelExpected = lowest;
            p.HoldSinceMs = nowMs;
            Log(1, "gave up waiting for " + skipped + " message(s) from peer " + (index + 1)
                + " that never arrived - delivering the " + p.Held.Count + " behind them; the other side may be"
                + " missing a change until the next resync");
            DrainHeld(index, p);
        }

        void HandleAck(Peer p, int payloadLen)
        {
            if (payloadLen < 1) return;
            int count = _rx[HeaderSize];
            if (count <= 0 || count > MaxAckPerDatagram) return;
            if (payloadLen < 1 + count * 4) return;

            for (int i = 0; i < count; i++)
            {
                uint seq = VpbIpc.ReadU32(_rx, HeaderSize + 1 + i * 4);
                for (int j = 0; j < p.InFlight.Count; j++)
                {
                    if (p.InFlight[j].Seq != seq) continue;
                    _relFree.Enqueue(p.InFlight[j].Buf);
                    p.InFlight.RemoveAt(j);
                    break;
                }
            }
        }

        void QueueAck(Peer p, uint seq)
        {
            for (int i = 0; i < p.AckOut.Count; i++)
            {
                if (p.AckOut[i] == seq) return;
            }
            if (p.AckOut.Count >= MaxAckPerDatagram) FlushAcks(p);
            p.AckOut.Add(seq);
        }

        void FlushAllAcks()
        {
            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (p.InUse) FlushAcks(p);
            }
        }

        void FlushAcks(Peer p)
        {
            int count = p.AckOut.Count;
            if (count == 0) return;
            if (count > MaxAckPerDatagram) count = MaxAckPerDatagram;

            _tx[HeaderSize] = (byte)count;
            for (int i = 0; i < count; i++) VpbIpc.WriteU32(_tx, HeaderSize + 1 + i * 4, p.AckOut[i]);
            p.AckOut.RemoveRange(0, count);
            SendTo(p, LanMsg.Ack, 1 + count * 4, 0, 0);
        }

        void ResetReliable(Peer p)
        {
            for (int i = 0; i < p.InFlight.Count; i++) _relFree.Enqueue(p.InFlight[i].Buf);
            for (int i = 0; i < p.Held.Count; i++) _relFree.Enqueue(p.Held[i].Buf);
            p.InFlight.Clear();
            p.Held.Clear();
            p.AckOut.Clear();
            p.RelTxSeq = 0;
            p.RelExpected = 1;
            p.RelSent = 0;
            p.RelResent = 0;
            p.RelAbandoned = 0;
            p.RelDuplicates = 0;
            p.RelSkipped = 0;
            p.HoldSinceMs = 0;
            p.WarnedFull = false;
        }

        void TrackSeq(Peer p, uint seq)
        {
            if (!p.RxSeqValid)
            {
                p.RxSeqValid = true;
                p.RxSeq = seq;
                return;
            }

            if (seq > p.RxSeq)
            {
                uint gap = seq - p.RxSeq - 1;
                if (gap > 0) p.Stats.Lost += gap;
                p.RxSeq = seq;
            }
            else if (p.RxSeq - seq < ReorderWindow)
            {
                p.Stats.Reordered++;
                if (p.Stats.Lost > 0) p.Stats.Lost--;
            }
        }

        void OnPong(Peer p, long rttTicks)
        {
            if (rttTicks < 0) return;
            double rtt = rttTicks * 1000.0 / Stopwatch.Frequency;
            if (!p.RttValid)
            {
                p.RttValid = true;
                p.RttMs = rtt;
                p.JitterMs = 0.0;
                return;
            }

            double delta = rtt - p.RttMs;
            if (delta < 0.0) delta = -delta;
            p.JitterMs += (delta - p.JitterMs) * 0.125;
            p.RttMs += (rtt - p.RttMs) * 0.125;
        }

        int Bind(long nowMs, IPEndPoint sender, uint connId)
        {
            int slot = -1;
            for (int i = 0; i < _maxPeers; i++)
            {
                if (!_peers[i].InUse) { slot = i; break; }
            }
            if (slot < 0) return -1;

            Peer p = _peers[slot];
            p.InUse = true;
            p.Up = true;
            p.Stalled = false;
            p.Endpoint = new IPEndPoint(sender.Address, sender.Port);
            p.ConnId = connId;
            p.TxSeq = 0;
            p.RxSeq = 0;
            p.RxSeqValid = false;
            p.LastRxMs = nowMs;
            p.LastPingMs = nowMs;
            p.RttMs = 0.0;
            p.JitterMs = 0.0;
            p.RttValid = false;
            p.Stats = new PeerStats();
            ResetReliable(p);
            return slot;
        }

        void DropPeer(int index, string reason)
        {
            Peer p = _peers[index];
            if (!p.InUse) return;

            p.InUse = false;
            p.Up = false;
            p.Stalled = false;
            p.Endpoint = null;
            p.ConnId = 0;
            if (p.RelResent > 0 || p.RelAbandoned > 0)
                Log(0, "reliable to peer " + (index + 1) + ": " + p.RelSent + " sent, " + p.RelResent
                    + " resent, " + p.RelAbandoned + " abandoned, " + p.RelDuplicates + " duplicates received, "
                    + p.RelSkipped + " never arrived");
            ResetReliable(p);
            Enqueue(index + 1, PeerEventKind.Down, reason);

            if (_role == TransportRole.Join && CountPeers() == 0 && FailureReason == null)
            {
                _connectPending = true;
                _startedMs = 0;
                _lastConnectMs = -ConnectRetryMs;
            }
        }

        bool SendTo(Peer p, LanMsg type, int payloadLen, byte channel, byte flags)
        {
            if (_socket == null || p == null || p.Endpoint == null) return false;

            _tx[0] = (byte)'V';
            _tx[1] = (byte)'L';
            _tx[2] = LanVersion;
            _tx[3] = (byte)type;
            VpbIpc.WriteU32(_tx, 4, p.ConnId);
            VpbIpc.WriteU32(_tx, 8, type == LanMsg.Data ? ++p.TxSeq : 0u);
            VpbIpc.WriteU16(_tx, 12, payloadLen);
            _tx[14] = channel;
            _tx[15] = flags;

            int signed = HeaderSize + payloadLen;
            Sign(_tx, signed);

            SendRaw(_tx, signed + TagSize, p.Endpoint);
            if (type == LanMsg.Data) p.Stats.Sent++;
            return true;
        }

        void StepRendezvous(long nowMs)
        {
            int n = _rv.Tick(nowMs, _rvTx);
            if (n > 0) SendRaw(_rvTx, n, _rvEndpoint);

            if (_rv.Phase == Rendezvous.RendezvousPhase.Failed)
            {
                Fail(_rv.FailureReason);
                return;
            }

            if (_rv.Phase != Rendezvous.RendezvousPhase.Resolved) return;

            if (!_rvActed)
            {
                _rvActed = true;
                _startedMs = nowMs;
                StringBuilder sb = new StringBuilder(160);
                _rv.Describe(sb);
                Log(0, sb.ToString());

                if (_role == TransportRole.Join)
                {
                    _hostEndpoint = ToEndPoint(_rv.PeerAt(0));
                    if (_hostEndpoint == null)
                    {
                        Fail("the rendezvous named a peer address this build cannot reach");
                        return;
                    }
                    _connectPending = true;
                    _lastConnectMs = long.MinValue / 2;
                    Log(0, "punching to " + _hostEndpoint);
                }
            }

            if (_role != TransportRole.Host) return;
            if (nowMs - _lastPunchMs < ConnectRetryMs) return;
            _lastPunchMs = nowMs;

            for (int i = 0; i < _rv.PeerCount; i++)
            {
                IPEndPoint target = ToEndPoint(_rv.PeerAt(i));
                if (target == null || FindByEndpoint(target) >= 0) continue;
                SendPunch(target);
            }
        }

        IPEndPoint RelayPeerEndpoint()
        {
            if (_hostEndpoint != null) return _hostEndpoint;
            for (int i = 0; i < _rv.PeerCount; i++)
            {
                IPEndPoint ep = ToEndPoint(_rv.PeerAt(i));
                if (ep != null) return ep;
            }
            return null;
        }

        void SendPunch(IPEndPoint target)
        {
            _tx[0] = (byte)'V';
            _tx[1] = (byte)'L';
            _tx[2] = LanVersion;
            _tx[3] = (byte)LanMsg.Ping;
            VpbIpc.WriteU32(_tx, 4, 0);
            VpbIpc.WriteU32(_tx, 8, 0);
            VpbIpc.WriteU16(_tx, 12, 8);
            _tx[14] = 0;
            _tx[15] = 0;
            VpbIpc.WriteI64(_tx, HeaderSize, Stopwatch.GetTimestamp());

            int signed = HeaderSize + 8;
            Sign(_tx, signed);
            SendRaw(_tx, signed + TagSize, target);
        }

        void SendRaw(byte[] buf, int len, IPEndPoint target)
        {
            if (_socket == null || target == null || len <= 0) return;
            if (_suppressSend) return;

            bool toPeer = !ReferenceEquals(target, _rvEndpoint) && !ReferenceEquals(buf, _rvTx);
            if (SimulateDirectBlocked && toPeer && !_relayMode) return;
            if (SimulateDropOneInN > 0 && toPeer && (++_dropCounter % SimulateDropOneInN) == 0) return;

            if (_relayMode && _rv.HasTicket && toPeer)
            {
                int wrapped = VpbNetRendezvous.WriteRelay(_relayTx, _lobbyToken, _rv.Ticket, buf, 0, len);
                if (wrapped > 0)
                {
                    try { _socket.SendTo(_relayTx, 0, wrapped, SocketFlags.None, _rvEndpoint); }
                    catch (SocketException) { }
                    catch { }
                    return;
                }
            }

            try { _socket.SendTo(buf, 0, len, SocketFlags.None, target); }
            catch (SocketException se)
            {
                if (se.SocketErrorCode != SocketError.WouldBlock && se.SocketErrorCode != SocketError.ConnectionReset)
                    Log(1, "send to " + target + " failed: " + se.SocketErrorCode);
            }
            catch { }
        }

        static IPEndPoint ToEndPoint(VpbNetEndpoint ep)
        {
            if (!ep.IsPresent) return null;
            try { return new IPEndPoint(new IPAddress(ep.Address), ep.Port); }
            catch { return null; }
        }

        void SendConnect()
        {
            _tx[0] = (byte)'V';
            _tx[1] = (byte)'L';
            _tx[2] = LanVersion;
            _tx[3] = (byte)LanMsg.Connect;
            VpbIpc.WriteU32(_tx, 4, 0);
            VpbIpc.WriteU32(_tx, 8, 0);
            VpbIpc.WriteU16(_tx, 12, 6);
            _tx[14] = 0;
            _tx[15] = 0;
            VpbIpc.WriteU32(_tx, HeaderSize, _connectNonce);
            VpbIpc.WriteU16(_tx, HeaderSize + 4, AppProto);

            int signed = HeaderSize + 6;
            Sign(_tx, signed);
            int len = signed + TagSize;

            if (_discovering && _discoveryTargets != null)
            {
                for (int i = 0; i < _discoveryTargets.Length; i++) SendRaw(_tx, len, _discoveryTargets[i]);
                return;
            }
            SendRaw(_tx, len, _hostEndpoint);
        }

        void Sign(byte[] buf, int len)
        {
            _mac.Sign(buf, len);
        }

        bool Verify(byte[] buf, int len)
        {
            return _mac != null && _mac.Verify(buf, len);
        }

        void CountUnauth(long nowMs)
        {
            _unauthTotal++;
            if (nowMs - _unauthWindowMs >= UnauthWindowMs)
            {
                _unauthWindowMs = nowMs;
                if (_unauthInWindow > MaxUnauthPerWindow)
                    Log(1, "dropped " + _unauthInWindow + " unauthenticated datagrams in the last second (" + _unauthTotal + " total)");
                _unauthInWindow = 0;
            }
            _unauthInWindow++;
        }

        int FindByConn(uint connId, IPEndPoint sender)
        {
            if (connId == 0) return -1;
            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (!p.InUse || p.ConnId != connId) continue;
                if (p.Endpoint.Port != sender.Port || !p.Endpoint.Address.Equals(sender.Address)) return -1;
                return i;
            }
            return -1;
        }

        int FindByEndpoint(IPEndPoint sender)
        {
            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (!p.InUse || p.Endpoint == null) continue;
                if (p.Endpoint.Port == sender.Port && p.Endpoint.Address.Equals(sender.Address)) return i;
            }
            return -1;
        }

        int CountPeers()
        {
            int n = 0;
            for (int i = 0; i < HardMaxPeers; i++)
            {
                if (_peers[i].InUse) n++;
            }
            return n;
        }

        Peer Resolve(int peerId)
        {
            int index = peerId - 1;
            if (index < 0 || index >= HardMaxPeers) return null;
            Peer p = _peers[index];
            return p.InUse ? p : null;
        }

        void Enqueue(int peerId, PeerEventKind kind, string reason)
        {
            _events.Enqueue(new PendingEvent { PeerId = peerId, Kind = kind, Reason = reason });
        }

        void Fail(string reason)
        {
            if (FailureReason != null) return;
            FailureReason = reason;
        }

        void Log(byte level, string text)
        {
            if (_log != null) _log(level, text);
        }

        static void SuppressConnReset(Socket s)
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            try { s.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null); }
            catch { }
        }

        static string BuildInvite(int port)
        {
            string best = null;
            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length && best == null; i++)
                {
                    NetworkInterface nic = nics[i];
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    IPInterfaceProperties props = nic.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;

                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        best = ua.Address.ToString();
                        break;
                    }
                }
            }
            catch { }

            if (best == null)
            {
                try
                {
                    IPAddress[] all = Dns.GetHostAddresses(Dns.GetHostName());
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i].AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(all[i]))
                        {
                            best = all[i].ToString();
                            break;
                        }
                    }
                }
                catch { }
            }

            if (best == null) best = "127.0.0.1";
            return best + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        static IPEndPoint[] BuildDiscoveryTargets(int port)
        {
            List<IPEndPoint> targets = new List<IPEndPoint>(6);

            // Also directed-broadcast per NIC; multi-homed boxes can drop 255.255.255.255.
            targets.Add(new IPEndPoint(IPAddress.Broadcast, port));
            targets.Add(new IPEndPoint(IPAddress.Loopback, port));

            try
            {
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    NetworkInterface nic = nics[i];
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    foreach (UnicastIPAddressInformation ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (ua.IPv4Mask == null) continue;

                        byte[] addr = ua.Address.GetAddressBytes();
                        byte[] mask = ua.IPv4Mask.GetAddressBytes();
                        if (addr.Length != 4 || mask.Length != 4) continue;
                        if (mask[0] == 255 && mask[1] == 255 && mask[2] == 255 && mask[3] == 255) continue;

                        for (int b = 0; b < 4; b++) addr[b] |= (byte)~mask[b];
                        IPAddress directed = new IPAddress(addr);
                        if (HasTarget(targets, directed)) continue;
                        targets.Add(new IPEndPoint(directed, port));
                    }
                }
            }
            catch { }

            return targets.ToArray();
        }

        static bool HasTarget(List<IPEndPoint> targets, IPAddress address)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Address.Equals(address)) return true;
            }
            return false;
        }

        static bool TryParseBind(string blob, out IPEndPoint endpoint)
        {
            endpoint = new IPEndPoint(IPAddress.Any, DefaultPort);
            if (string.IsNullOrEmpty(blob)) return true;

            string text = blob.Trim();
            if (text.Length == 0) return true;

            int port;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
            {
                if (port < 0 || port > 65535) return false;
                endpoint = new IPEndPoint(IPAddress.Any, port);
                return true;
            }

            int colon = text.LastIndexOf(':');
            if (colon < 0) return false;

            string hostPart = text.Substring(0, colon).Trim();
            if (!int.TryParse(text.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return false;
            if (port < 0 || port > 65535) return false;

            IPAddress address;
            if (hostPart.Length == 0 || hostPart == "*") address = IPAddress.Any;
            else if (!IPAddress.TryParse(hostPart, out address)) return false;

            endpoint = new IPEndPoint(address, port);
            return true;
        }

        static bool TryParseTarget(string blob, out IPEndPoint endpoint)
        {
            endpoint = null;
            if (string.IsNullOrEmpty(blob)) return false;

            string text = blob.Trim();
            int colon = text.LastIndexOf(':');
            int port = DefaultPort;
            string hostPart = text;

            if (colon >= 0)
            {
                hostPart = text.Substring(0, colon).Trim();
                if (!int.TryParse(text.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return false;
            }
            if (hostPart.Length == 0 || port <= 0 || port > 65535) return false;

            IPAddress address;
            if (!IPAddress.TryParse(hostPart, out address))
            {
                try
                {
                    IPAddress[] all = Dns.GetHostAddresses(hostPart);
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i].AddressFamily == AddressFamily.InterNetwork)
                        {
                            address = all[i];
                            break;
                        }
                    }
                }
                catch { return false; }
                if (address == null) return false;
            }

            endpoint = new IPEndPoint(address, port);
            return true;
        }

        static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int len = text.Length > 64 ? 64 : text.Length;
            StringBuilder sb = new StringBuilder(len);
            for (int i = 0; i < len; i++)
            {
                char c = text[i];
                sb.Append(c < ' ' || c > '~' ? '?' : c);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (p.InUse && p.Endpoint != null) SendTo(p, LanMsg.Bye, 0, 0, 0);
                p.InUse = false;
                p.Up = false;
                p.Endpoint = null;
                ResetReliable(p);
            }

            try { if (_socket != null) _socket.Close(); }
            catch { }
            _socket = null;

            try { if (_mac != null) _mac.Dispose(); }
            catch { }
            _mac = null;

            _pending.Clear();
            _free.Clear();
            _events.Clear();
        }
    }
}
