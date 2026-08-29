using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VpbNet.Transport.Steam
{
    public sealed class SteamP2PTransport : ISessionTransport
    {
        public const byte SteamVersion = 1;
        public const ushort AppProto = 1;

        const int HeaderSize = 12;
        const int TagSize = SessionAuth.TagBytes;
        const int OverheadSize = HeaderSize + TagSize;
        const int HardMaxPeers = 4;
        const int SlotCount = 128;
        const int RecvBatch = 32;
        const int SteamChannel = 0;

        const int ConnectRetryMs = 500;
        const int ConnectTimeoutMs = 20000;
        const int SearchRetryMs = 2000;
        const int SearchTimeoutMs = 90000;
        const int PingIntervalMs = 500;
        const int StallMs = 2000;
        const int DropMs = 10000;
        const int PendingMs = 3000;
        const int MaxTrackedSessions = 32;
        const int LobbyDataRetryMs = 3000;
        const int ApiResultBytes = 256;

        enum Msg : byte
        {
            None = 0,
            Connect = 1,
            Accept = 2,
            Bye = 3,
            Data = 4,
            Ping = 5,
            Pong = 6
        }

        enum Phase
        {
            Idle = 0,
            Creating = 1,
            Hosting = 2,
            Searching = 3,
            Entering = 4,
            Dialing = 5,
            Live = 6
        }

        sealed class Peer
        {
            public bool InUse;
            public bool Up;
            public bool Stalled;
            public ulong SteamId;
            public IntPtr Identity;
            public uint TxSeq;
            public uint RxSeq;
            public bool RxSeqValid;
            public long LastRxMs;
            public long LastPingMs;
            public double RttMs;
            public double JitterMs;
            public bool RttValid;
            public long BoundMs;
            public PeerStats Stats;
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
        readonly Action<int, IntPtr, int> _onCallback;
        readonly string _brokerDir;
        readonly string _apiPathHint;
        readonly string _roomLabel;

        readonly byte[] _rx = new byte[OverheadSize + 1400];
        readonly byte[] _tx = new byte[OverheadSize + 1400];
        readonly Peer[] _peers = new Peer[HardMaxPeers];
        readonly Queue<byte[]> _free = new Queue<byte[]>();
        readonly Queue<PendingRx> _pending = new Queue<PendingRx>();
        readonly Queue<PendingEvent> _events = new Queue<PendingEvent>();
        readonly List<ulong> _sessions = new List<ulong>();

        SessionMac _mac;
        TransportRole _role;
        int _maxPeers;
        uint _connectNonce;
        uint _appId = VpbNetSteam.DefaultAppId;
        string _lobbyToken = string.Empty;

        IntPtr _callbackScratch;
        IntPtr _apiResult;
        IntPtr _txNative;
        IntPtr _msgPtrs;
        IntPtr _scratchIdentity;

        Phase _phase;
        ulong _lobby;
        ulong _createCall;
        ulong _listCall;
        ulong _joinCall;
        ulong _hostSteamId;
        long _startedMs = -1;
        long _phaseEnteredMs = -1;
        long _lastConnectMs;
        long _lastSearchMs;
        long _lastLobbyDataMs;
        bool _lobbyDataPublished;
        bool _lobbyShielded;
        bool _roomJoined;
        bool _connectPending;
        bool _relayLogged;
        int _unauthTotal;
        int _unauthLogged;
        int _searchRounds;
        int _lobbiesSeen;
        long _nowMs = -1;

        public SteamP2PTransport(int slotBytes, string brokerDir, string apiPathHint, string roomLabel, Action<byte, string> log)
        {
            _log = log;
            _onCallback = OnCallback;
            _brokerDir = brokerDir;
            _apiPathHint = apiPathHint;
            _roomLabel = roomLabel;
            for (int i = 0; i < HardMaxPeers; i++) _peers[i] = new Peer();
            for (int i = 0; i < SlotCount; i++) _free.Enqueue(new byte[slotBytes]);
        }

        public string Name { get { return "steam"; } }

        public string InviteBlob { get; private set; }

        public string FailureReason { get; private set; }

        public string StatusHint
        {
            get
            {
                if (_phase == Phase.Searching || _phase == Phase.Entering)
                    return VpbNetSteam.SearchingHint(_roomLabel, ElapsedSeconds());
                if (_phase == Phase.Hosting && !_lobbyDataPublished)
                    return "publishing the room to Steam";
                if (_phase == Phase.Hosting)
                    return "room is open on Steam - they only need the room code";
                if (_phase == Phase.Dialing)
                    return "found them on Steam, opening the connection";
                return null;
            }
        }

        public int Dropped { get; private set; }

        public void Start(TransportOptions options)
        {
            InviteBlob = string.Empty;
            _role = options.Role;
            _maxPeers = options.MaxPeers < 1 ? 1 : (options.MaxPeers > HardMaxPeers ? HardMaxPeers : options.MaxPeers);
            _mac = new SessionMac(options.SessionKey);

            VpbNetSteamFault fault;
            if (!VpbNetSteam.TryParseConnectBlob(options.ConnectBlob, out _appId, out fault))
            {
                Fail(VpbNetSteam.Explain(fault));
                return;
            }

            if (options.LobbyToken == null || options.LobbyToken.Length < VpbNetRendezvous.TokenBytes)
            {
                Fail("the room code produced no lobby token - this build cannot use Steam");
                return;
            }
            _lobbyToken = ToHex(options.LobbyToken, 16);

            string path = SteamNative.FindLibrary(_apiPathHint, _brokerDir);
            if (path == null)
            {
                Fail(VpbNetSteam.MissingLibrary(_brokerDir));
                return;
            }

            string error;
            if (!SteamNative.Load(path, out error))
            {
                Fail(error);
                return;
            }
            Log(0, "using " + path);

            if (!SteamNative.Start(_appId, _brokerDir, out error))
            {
                Fail(error);
                return;
            }

            if (!ApplyTransportPrivacy()) return;

            _callbackScratch = Marshal.AllocHGlobal(SteamNative.CallbackMsgBytes);
            _apiResult = Marshal.AllocHGlobal(ApiResultBytes);
            _txNative = Marshal.AllocHGlobal(_tx.Length);
            _msgPtrs = Marshal.AllocHGlobal(IntPtr.Size * RecvBatch);
            _scratchIdentity = SteamNative.AllocIdentity(0);

            DiscardQueuedMessages();

            byte[] seed = new byte[4];
            RandomNumberGenerator.Fill(seed);
            _connectNonce = BitConverter.ToUInt32(seed, 0);

            _lastConnectMs = long.MinValue / 2;
            _lastSearchMs = long.MinValue / 2;
            _lastLobbyDataMs = long.MinValue / 2;

            InviteBlob = VpbNetSteam.BuildConnectBlob(_appId);

            if (_role == TransportRole.Host)
            {
                EnterPhase(Phase.Creating);
                _createCall = SteamNative.CreateLobby(SteamLobbyType.Public, _maxPeers + 1);
                if (_createCall == 0)
                {
                    Fail("Steam refused to open a room. Sign out and back into Steam, then try again.");
                    return;
                }
                Log(0, "opening a Steam room; the other player needs the room code and nothing else");
            }
            else
            {
                EnterPhase(Phase.Searching);
                Log(0, "looking for a Steam room with that code; no address, no port forwarding");
            }
        }

        public void Poll(long nowMs)
        {
            if (!SteamNative.Started) return;
            _nowMs = nowMs;
            if (_startedMs < 0) _startedMs = nowMs;
            if (_phaseEnteredMs < 0) _phaseEnteredMs = nowMs;

            SteamNative.RunFrame(_callbackScratch, _onCallback);
            DrainMessages(nowMs);
            NoteRelay();

            switch (_phase)
            {
                case Phase.Hosting:
                    RepublishLobbyData(nowMs);
                    break;
                case Phase.Searching:
                    StepSearch(nowMs);
                    break;
                case Phase.Dialing:
                    StepDial(nowMs);
                    break;
            }

            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (!p.InUse) continue;

                if (!p.Up)
                {
                    if (nowMs - p.BoundMs > PendingMs) Unbind(i);
                    continue;
                }

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

                if (nowMs - p.LastPingMs >= PingIntervalMs)
                {
                    if (_unauthTotal > _unauthLogged + 256)
                    {
                        _unauthLogged = _unauthTotal;
                        Log(1, "dropped " + _unauthTotal + " Steam messages that did not carry this room's key");
                    }
                    p.LastPingMs = nowMs;
                    VpbIpc.WriteI64(_tx, HeaderSize, Stopwatch.GetTimestamp());
                    SendTo(p, Msg.Ping, 8, 0, 0, false);
                }
            }
        }

        public bool Send(int peerId, byte[] buffer, int offset, int count, byte channel, bool reliable)
        {
            Peer p = Resolve(peerId);
            if (p == null || !p.Up) return false;
            if (count <= 0 || OverheadSize + count > _tx.Length)
            {
                Dropped++;
                return false;
            }

            Buffer.BlockCopy(buffer, offset, _tx, HeaderSize, count);
            return SendTo(p, Msg.Data, count, channel, reliable ? (byte)1 : (byte)0, reliable);
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

        public void CollectWaitSockets(List<Socket> into)
        {
        }

        void OnCallback(int callback, IntPtr param, int paramBytes)
        {
            switch (callback)
            {
                case SteamNative.CallbackApiCallCompleted:
                    OnApiCallCompleted(param, paramBytes);
                    break;

                case SteamNative.CallbackMessagesSessionRequest:
                    OnSessionRequest(param);
                    break;

                case SteamNative.CallbackMessagesSessionFailed:
                    Log(1, "Steam could not open a peer-to-peer session; retrying");
                    break;
            }
        }

        void OnApiCallCompleted(IntPtr param, int paramBytes)
        {
            if (paramBytes < 16) return;

            ulong call = (ulong)Marshal.ReadInt64(param, 0);
            int inner = Marshal.ReadInt32(param, 8);
            int innerBytes = Marshal.ReadInt32(param, 12);
            if (innerBytes < 0 || innerBytes > ApiResultBytes) return;

            bool failed;
            if (!SteamNative.GetApiCallResult(call, _apiResult, innerBytes, inner, out failed)) return;
            if (failed) return;

            if (call == _createCall && inner == SteamNative.CallbackLobbyCreated) OnLobbyCreated(innerBytes);
            else if (call == _listCall && inner == SteamNative.CallbackLobbyMatchList) OnLobbyMatchList(innerBytes);
            else if (call == _joinCall && inner == SteamNative.CallbackLobbyEnter) OnLobbyEnter(innerBytes);
        }

        void OnLobbyCreated(int bytes)
        {
            if (bytes < 16) return;
            _createCall = 0;

            int result = Marshal.ReadInt32(_apiResult, 0);
            if (result != SteamNative.ResultOk)
            {
                Fail("Steam refused to open a room (result " + result + ")."
                    + " Steam is signed in but would not create a lobby - this is usually a temporary Steam problem."
                    + " Try again, or use Direct instead.");
                return;
            }

            _lobby = (ulong)Marshal.ReadInt64(_apiResult, 8);
            if (_lobby == 0)
            {
                Fail("Steam opened a room with no id. Try again.");
                return;
            }

            _hostSteamId = SteamNative.SelfSteamId();
            _roomJoined = true;
            EnterPhase(Phase.Hosting);
            PublishLobbyData();
        }

        bool ApplyTransportPrivacy()
        {
            if (!SteamNative.EnableRelayOnly())
            {
                Fail(VpbNetSteam.RelayOnlyUnavailable());
                return false;
            }

            Log(0, "relay only: Steam carries this session and the other player's machine never sees your IP address");
            return true;
        }

        void PublishLobbyData()
        {
            _lastLobbyDataMs = _nowMs;
            bool ok = SteamNative.SetLobbyData(_lobby, VpbNetSteam.LobbyKeyRoom, _lobbyToken);
            SteamNative.SetLobbyJoinable(_lobby, true);

            if (!ok || _lobbyDataPublished) return;
            _lobbyDataPublished = true;
            Log(0, "the room is open on Steam - the other player types the same room code and presses Join");
        }

        void ShieldLobby()
        {
            if (_lobbyShielded || _lobby == 0) return;
            _lobbyShielded = true;
            SteamNative.SetLobbyJoinable(_lobby, false);
            SteamNative.SetLobbyType(_lobby, SteamLobbyType.Private);
            SteamNative.SetLobbyData(_lobby, VpbNetSteam.LobbyKeyRoom, string.Empty);
            Log(0, "the room is full, so it has been taken off Steam's room list."
                + " Nobody browsing Steam can see it, or see which accounts are in it, until someone leaves.");
        }

        void UnshieldLobby()
        {
            if (!_lobbyShielded || _lobby == 0) return;
            _lobbyShielded = false;
            SteamNative.SetLobbyType(_lobby, SteamLobbyType.Public);
            SteamNative.SetLobbyJoinable(_lobby, true);
            SteamNative.SetLobbyData(_lobby, VpbNetSteam.LobbyKeyRoom, _lobbyToken);
            Log(0, "the room is back on Steam's room list, waiting for someone with the code");
        }

        void RepublishLobbyData(long nowMs)
        {
            if (_lobbyDataPublished) return;
            if (nowMs - _lastLobbyDataMs < LobbyDataRetryMs) return;
            PublishLobbyData();
        }

        void StepSearch(long nowMs)
        {
            if (nowMs - _startedMs > SearchTimeoutMs)
            {
                Log(1, "asked Steam " + _searchRounds + " times and it offered "
                    + _lobbiesSeen + " rooms carrying that code");
                Fail(VpbNetSteam.NoRoom(_roomLabel, SearchTimeoutMs / 1000));
                return;
            }
            if (_listCall != 0) return;
            if (nowMs - _lastSearchMs < SearchRetryMs) return;

            _lastSearchMs = nowMs;
            _searchRounds++;
            _listCall = SteamNative.RequestLobbyList(VpbNetSteam.LobbyKeyRoom, _lobbyToken, VpbNetSteam.MaxLobbyResults);
        }

        void OnLobbyMatchList(int bytes)
        {
            _listCall = 0;
            if (bytes < 4) return;

            int count = Marshal.ReadInt32(_apiResult, 0);
            if (count < 0) count = 0;
            if (count > VpbNetSteam.MaxLobbyResults) count = VpbNetSteam.MaxLobbyResults;
            _lobbiesSeen += count;

            for (int i = 0; i < count; i++)
            {
                ulong lobby = SteamNative.LobbyByIndex(i);
                if (lobby == 0) continue;

                string token = SteamNative.GetLobbyData(lobby, VpbNetSteam.LobbyKeyRoom);
                if (!string.Equals(token, _lobbyToken, StringComparison.Ordinal)) continue;

                _lobby = lobby;
                EnterPhase(Phase.Entering);
                _joinCall = SteamNative.JoinLobby(lobby);
                if (_joinCall == 0)
                {
                    _lobby = 0;
                    EnterPhase(Phase.Searching);
                    return;
                }
                Log(0, "found the room on Steam; joining");
                return;
            }
        }

        void OnLobbyEnter(int bytes)
        {
            _joinCall = 0;
            if (bytes < 20) return;

            ulong lobby = (ulong)Marshal.ReadInt64(_apiResult, 0);
            int response = Marshal.ReadInt32(_apiResult, 16);
            if (response != 1)
            {
                _lobby = 0;
                EnterPhase(Phase.Searching);
                Log(1, "Steam would not let us into that room (" + ExplainEnter(response) + "); still looking");
                return;
            }

            _lobby = lobby;
            _hostSteamId = SteamNative.LobbyOwner(lobby);
            if (_hostSteamId == 0)
            {
                _lobby = 0;
                EnterPhase(Phase.Searching);
                return;
            }

            _roomJoined = true;
            EnterPhase(Phase.Dialing);
            _connectPending = true;
            _lastConnectMs = long.MinValue / 2;
        }

        static string ExplainEnter(int response)
        {
            switch (response)
            {
                case 2: return "the room no longer exists";
                case 3: return "the room is not open";
                case 4: return "the room is full";
                case 8: return "you are blocked by the person hosting";
                case 10: return "they have blocked you";
                case 11: return "you have blocked them";
            }
            return "response " + response;
        }

        void StepDial(long nowMs)
        {
            if (!_connectPending) return;
            if (nowMs - _lastConnectMs < ConnectRetryMs) return;
            _lastConnectMs = nowMs;

            if (nowMs - _phaseEnteredMs > ConnectTimeoutMs)
            {
                _connectPending = false;
                Fail("found their room on Steam but they never answered in " + (ConnectTimeoutMs / 1000) + "s."
                    + " They are in the room, so the code is right - either their VPB build is different,"
                    + " or Steam's relay is not reaching one of you. Both sides restarting Steam usually fixes it.");
                return;
            }

            SendConnect();
        }

        void OnSessionRequest(IntPtr param)
        {
            if (param == IntPtr.Zero) return;
            ulong who = SteamNative.IdentitySteamId(param);
            if (who == 0) return;

            if (!_roomJoined)
            {
                SteamNative.CloseSession(param);
                return;
            }

            NoteSession(who);
            SteamNative.AcceptSession(param);
        }

        void DrainMessages(long nowMs)
        {
            while (true)
            {
                int n = SteamNative.ReceiveMessages(SteamChannel, _msgPtrs, RecvBatch);
                if (n <= 0) return;

                for (int i = 0; i < n; i++)
                {
                    IntPtr msg = Marshal.ReadIntPtr(_msgPtrs, i * IntPtr.Size);
                    if (msg == IntPtr.Zero) continue;
                    try { HandleNative(nowMs, msg); }
                    finally { SteamNative.ReleaseMessage(msg); }
                }

                if (n < RecvBatch) return;
            }
        }

        void HandleNative(long nowMs, IntPtr msg)
        {
            IntPtr data = Marshal.ReadIntPtr(msg, 0);
            int size = Marshal.ReadInt32(msg, 8);
            if (data == IntPtr.Zero || size <= 0 || size > _rx.Length)
            {
                _unauthTotal++;
                return;
            }

            Marshal.Copy(data, _rx, 0, size);
            ulong sender = SteamNative.IdentitySteamId(IntPtr.Add(msg, 16));
            if (sender == 0)
            {
                _unauthTotal++;
                return;
            }

            if (size < OverheadSize) { _unauthTotal++; return; }
            if (_rx[0] != (byte)'V' || _rx[1] != (byte)'S' || _rx[2] != SteamVersion) { _unauthTotal++; return; }

            int payloadLen = VpbIpc.ReadU16(_rx, 8);
            if (payloadLen < 0 || HeaderSize + payloadLen + TagSize > size) { _unauthTotal++; return; }
            if (_mac == null || !_mac.Verify(_rx, HeaderSize + payloadLen)) { _unauthTotal++; return; }

            Msg type = (Msg)_rx[3];
            uint seq = VpbIpc.ReadU32(_rx, 4);
            byte channel = _rx[10];

            Handle(nowMs, sender, type, seq, channel, payloadLen);
        }

        void Handle(long nowMs, ulong sender, Msg type, uint seq, byte channel, int payloadLen)
        {
            if (type == Msg.Connect)
            {
                if (_role != TransportRole.Host || payloadLen < 6) return;
                HandleConnect(nowMs, sender);
                return;
            }

            if (type == Msg.Accept)
            {
                if (_role != TransportRole.Join || !_connectPending || payloadLen < 6) return;
                if (VpbIpc.ReadU32(_rx, HeaderSize) != _connectNonce) return;

                int slot = Bind(nowMs, sender, true);
                if (slot < 0) return;
                _connectPending = false;
                EnterPhase(Phase.Live);
                LeaveLobbyAfterJoin();
                Enqueue(slot + 1, PeerEventKind.Up, "connected over Steam");

                Peer bound = _peers[slot];
                bound.LastPingMs = nowMs;
                VpbIpc.WriteI64(_tx, HeaderSize, Stopwatch.GetTimestamp());
                SendTo(bound, Msg.Ping, 8, 0, 0, false);
                return;
            }

            int index = FindBySteamId(sender);
            if (index < 0) return;

            Peer p = _peers[index];
            p.LastRxMs = nowMs;

            if (!p.Up)
            {
                PromotePeer(index);
                if (!p.InUse) return;
            }

            switch (type)
            {
                case Msg.Data:
                    TrackSeq(p, seq);
                    QueueData(index, payloadLen, channel);
                    break;

                case Msg.Ping:
                    if (payloadLen < 8) return;
                    Buffer.BlockCopy(_rx, HeaderSize, _tx, HeaderSize, 8);
                    SendTo(p, Msg.Pong, 8, 0, 0, false);
                    break;

                case Msg.Pong:
                    if (payloadLen < 8) return;
                    OnPong(p, Stopwatch.GetTimestamp() - VpbIpc.ReadI64(_rx, HeaderSize));
                    break;

                case Msg.Bye:
                    DropPeer(index, "peer left the session");
                    break;
            }
        }

        void HandleConnect(long nowMs, ulong sender)
        {
            uint nonce = VpbIpc.ReadU32(_rx, HeaderSize);
            int existing = FindBySteamId(sender);

            int slot;
            if (existing >= 0)
            {
                slot = existing;
            }
            else
            {
                if (CountUpPeers() >= _maxPeers)
                {
                    Log(1, "refused a Steam connection: the room is full (" + _maxPeers + ")");
                    return;
                }
                slot = Bind(nowMs, sender, false);
                if (slot < 0) return;
            }

            VpbIpc.WriteU32(_tx, HeaderSize, nonce);
            VpbIpc.WriteU16(_tx, HeaderSize + 4, AppProto);
            SendTo(_peers[slot], Msg.Accept, 6, 0, 0, true);
        }

        void PromotePeer(int index)
        {
            Peer p = _peers[index];
            if (p.Up) return;

            if (CountUpPeers() >= _maxPeers)
            {
                Unbind(index);
                return;
            }

            p.Up = true;
            EnterPhase(Phase.Live);
            if (CountUpPeers() >= _maxPeers) ShieldLobby();
            Enqueue(index + 1, PeerEventKind.Up, "they joined over Steam");
        }

        void Unbind(int index)
        {
            Peer p = _peers[index];
            if (p.Identity != IntPtr.Zero) SteamNative.CloseSession(p.Identity);
            p.InUse = false;
            p.Up = false;
            p.Stalled = false;
            p.SteamId = 0;
        }

        void QueueData(int index, int payloadLen, byte channel)
        {
            if (payloadLen <= 0) return;
            if (_free.Count == 0)
            {
                Dropped++;
                return;
            }

            byte[] slot = _free.Dequeue();
            int len = payloadLen > slot.Length ? slot.Length : payloadLen;
            Buffer.BlockCopy(_rx, HeaderSize, slot, 0, len);
            _pending.Enqueue(new PendingRx { Buffer = slot, Length = len, PeerId = index + 1, Channel = channel });
            _peers[index].Stats.Received++;
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
            else if (p.RxSeq - seq < 256)
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

        int Bind(long nowMs, ulong steamId, bool up)
        {
            int slot = -1;
            for (int i = 0; i < HardMaxPeers; i++)
            {
                if (!_peers[i].InUse) { slot = i; break; }
            }
            if (slot < 0) return -1;

            NoteSession(steamId);

            Peer p = _peers[slot];
            p.InUse = true;
            p.Up = up;
            p.BoundMs = nowMs;
            p.Stalled = false;
            p.SteamId = steamId;
            if (p.Identity == IntPtr.Zero) p.Identity = SteamNative.AllocIdentity(steamId);
            else SteamNative.SetIdentity(p.Identity, steamId);
            p.TxSeq = 0;
            p.RxSeq = 0;
            p.RxSeqValid = false;
            p.LastRxMs = nowMs;
            p.LastPingMs = nowMs;
            p.RttMs = 0.0;
            p.JitterMs = 0.0;
            p.RttValid = false;
            p.Stats = new PeerStats();
            return slot;
        }

        void DropPeer(int index, string reason)
        {
            Peer p = _peers[index];
            if (!p.InUse) return;

            if (p.Identity != IntPtr.Zero) SteamNative.CloseSession(p.Identity);

            p.InUse = false;
            p.Up = false;
            p.Stalled = false;
            p.SteamId = 0;
            Enqueue(index + 1, PeerEventKind.Down, reason);

            if (_role == TransportRole.Host && CountPeers() < _maxPeers && FailureReason == null)
            {
                UnshieldLobby();
                if (_lobby != 0) EnterPhase(Phase.Hosting);
            }

            if (_role == TransportRole.Join && CountPeers() == 0 && FailureReason == null)
            {
                EnterPhase(Phase.Dialing);
                _connectPending = true;
                _lastConnectMs = long.MinValue / 2;
            }
        }

        void LeaveLobbyAfterJoin()
        {
            if (_lobby == 0) return;
            SteamNative.LeaveLobby(_lobby);
            _lobby = 0;
            Log(0, "left the Steam room now that the connection is up;"
                + " the session no longer shows this account to anyone browsing Steam");
        }

        bool SendTo(Peer p, Msg type, int payloadLen, byte channel, byte flags, bool reliable)
        {
            if (p == null || p.Identity == IntPtr.Zero) return false;

            _tx[0] = (byte)'V';
            _tx[1] = (byte)'S';
            _tx[2] = SteamVersion;
            _tx[3] = (byte)type;
            VpbIpc.WriteU32(_tx, 4, type == Msg.Data ? ++p.TxSeq : 0u);
            VpbIpc.WriteU16(_tx, 8, payloadLen);
            _tx[10] = channel;
            _tx[11] = flags;

            int signed = HeaderSize + payloadLen;
            _mac.Sign(_tx, signed);

            int total = signed + TagSize;
            Marshal.Copy(_tx, 0, _txNative, total);

            int sendFlags = reliable
                ? SteamNative.SendReliable | SteamNative.SendNoNagle
                : SteamNative.SendUnreliable | SteamNative.SendNoNagle;

            SteamNative.SendToUser(p.Identity, _txNative, total, sendFlags, SteamChannel);
            if (type == Msg.Data) p.Stats.Sent++;
            return true;
        }

        void SendConnect()
        {
            if (_hostSteamId == 0) return;
            NoteSession(_hostSteamId);
            SteamNative.SetIdentity(_scratchIdentity, _hostSteamId);

            _tx[0] = (byte)'V';
            _tx[1] = (byte)'S';
            _tx[2] = SteamVersion;
            _tx[3] = (byte)Msg.Connect;
            VpbIpc.WriteU32(_tx, 4, 0);
            VpbIpc.WriteU16(_tx, 8, 6);
            _tx[10] = 0;
            _tx[11] = 0;
            VpbIpc.WriteU32(_tx, HeaderSize, _connectNonce);
            VpbIpc.WriteU16(_tx, HeaderSize + 4, AppProto);

            int signed = HeaderSize + 6;
            _mac.Sign(_tx, signed);

            int total = signed + TagSize;
            Marshal.Copy(_tx, 0, _txNative, total);
            SteamNative.SendToUser(_scratchIdentity, _txNative, total,
                SteamNative.SendReliable | SteamNative.SendNoNagle, SteamChannel);
        }

        void NoteSession(ulong steamId)
        {
            if (steamId == 0) return;
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i] == steamId) return;
            }
            if (_sessions.Count >= MaxTrackedSessions)
            {
                SteamNative.SetIdentity(_scratchIdentity, _sessions[0]);
                SteamNative.CloseSession(_scratchIdentity);
                _sessions.RemoveAt(0);
            }
            _sessions.Add(steamId);
        }

        void CloseTrackedSessions()
        {
            if (_scratchIdentity == IntPtr.Zero) return;
            for (int i = 0; i < _sessions.Count; i++)
            {
                SteamNative.SetIdentity(_scratchIdentity, _sessions[i]);
                SteamNative.CloseSession(_scratchIdentity);
            }
            _sessions.Clear();
        }

        void DiscardQueuedMessages()
        {
            if (_msgPtrs == IntPtr.Zero) return;
            while (true)
            {
                int n = SteamNative.ReceiveMessages(SteamChannel, _msgPtrs, RecvBatch);
                if (n <= 0) return;
                for (int i = 0; i < n; i++)
                {
                    IntPtr msg = Marshal.ReadIntPtr(_msgPtrs, i * IntPtr.Size);
                    if (msg != IntPtr.Zero) SteamNative.ReleaseMessage(msg);
                }
                if (n < RecvBatch) return;
            }
        }

        void NoteRelay()
        {
            if (_relayLogged) return;
            SteamAvailability a = SteamNative.RelayStatus();
            if (a == SteamAvailability.Current)
            {
                _relayLogged = true;
                return;
            }
            if (a == SteamAvailability.Failed || a == SteamAvailability.CannotTry)
            {
                _relayLogged = true;
                Log(1, VpbNetSteam.RelayUnavailable());
            }
        }

        void EnterPhase(Phase phase)
        {
            _phase = phase;
            _phaseEnteredMs = _nowMs;
        }

        int ElapsedSeconds()
        {
            if (_startedMs < 0) return 0;
            long ms = _nowMs - _startedMs;
            return ms < 0 ? 0 : (int)(ms / 1000);
        }

        int FindBySteamId(ulong steamId)
        {
            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (p.InUse && p.SteamId == steamId) return i;
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

        int CountUpPeers()
        {
            int n = 0;
            for (int i = 0; i < HardMaxPeers; i++)
            {
                if (_peers[i].InUse && _peers[i].Up) n++;
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
            FailureReason = string.IsNullOrEmpty(reason) ? "the Steam connection failed" : reason;
        }

        void Log(byte level, string text)
        {
            if (_log != null) _log(level, text);
        }

        static string ToHex(byte[] bytes, int count)
        {
            if (bytes == null) return string.Empty;
            if (count > bytes.Length) count = bytes.Length;
            const string Digits = "0123456789abcdef";
            char[] c = new char[count * 2];
            for (int i = 0; i < count; i++)
            {
                c[i * 2] = Digits[(bytes[i] >> 4) & 0xF];
                c[i * 2 + 1] = Digits[bytes[i] & 0xF];
            }
            return new string(c);
        }

        public void Dispose()
        {
            for (int i = 0; i < HardMaxPeers; i++)
            {
                Peer p = _peers[i];
                if (p.InUse && p.Identity != IntPtr.Zero)
                {
                    SendTo(p, Msg.Bye, 0, 0, 0, true);
                    SteamNative.CloseSession(p.Identity);
                }
                if (p.Identity != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(p.Identity);
                    p.Identity = IntPtr.Zero;
                }
                p.InUse = false;
                p.Up = false;
                p.SteamId = 0;
            }

            CloseTrackedSessions();
            DiscardQueuedMessages();

            if (_lobby != 0)
            {
                SteamNative.LeaveLobby(_lobby);
                _lobby = 0;
            }

            _roomJoined = false;
            _lobbyShielded = false;

            FreeNative(ref _callbackScratch);
            FreeNative(ref _apiResult);
            FreeNative(ref _txNative);
            FreeNative(ref _msgPtrs);
            FreeNative(ref _scratchIdentity);

            try { if (_mac != null) _mac.Dispose(); }
            catch { }
            _mac = null;

            _pending.Clear();
            _free.Clear();
            _events.Clear();
        }

        static void FreeNative(ref IntPtr p)
        {
            if (p == IntPtr.Zero) return;
            try { Marshal.FreeHGlobal(p); }
            catch { }
            p = IntPtr.Zero;
        }
    }
}
