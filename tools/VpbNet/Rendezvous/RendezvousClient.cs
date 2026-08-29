using System;
using System.Text;
using VpbNet;

namespace VpbNet.Rendezvous
{
    public enum RendezvousPhase : byte
    {
        Idle = 0,
        Announcing = 1,
        Resolved = 2,
        Failed = 3
    }

    public sealed class RendezvousClient
    {
        public const int AnnounceIntervalMs = 500;
        public const int TimeoutMs = 20000;
        public const int HintAfterAnnounces = 8;

        readonly byte[] _token = new byte[VpbNetRendezvous.TokenBytes];
        readonly byte[] _ticket = new byte[VpbNetRendezvous.TicketBytes];
        readonly VpbNetEndpoint[] _peers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];

        byte _role;
        ushort _localPort;
        uint _nonce;
        uint _nextNonce;
        long _startedMs = -1;
        long _lastSendMs = long.MinValue / 2;
        int _peerCount;
        int _announces;

        public RendezvousPhase Phase { get; private set; }
        public string FailureReason { get; private set; }
        public VpbNetEndpoint Reflexive { get; private set; }
        public bool HasTicket { get; private set; }
        public byte[] Ticket { get { return _ticket; } }
        public int PeerCount { get { return _peerCount; } }
        public int Announces { get { return _announces; } }

        public VpbNetEndpoint PeerAt(int i)
        {
            if (i < 0 || i >= _peerCount) return new VpbNetEndpoint();
            return _peers[i];
        }

        public void Start(byte[] lobbyToken, byte role, ushort localPort, uint seedNonce)
        {
            Phase = RendezvousPhase.Idle;
            FailureReason = null;
            Reflexive = new VpbNetEndpoint();
            HasTicket = false;
            Array.Clear(_ticket, 0, _ticket.Length);
            _peerCount = 0;
            _announces = 0;
            _startedMs = -1;
            _lastSendMs = long.MinValue / 2;
            _role = role;
            _localPort = localPort;
            _nextNonce = seedNonce == 0 ? 1u : seedNonce;
            _nonce = 0;

            if (lobbyToken == null || lobbyToken.Length < VpbNetRendezvous.TokenBytes)
            {
                Phase = RendezvousPhase.Failed;
                FailureReason = "the room code did not produce a rendezvous token - this is a bug, not a configuration problem";
                return;
            }

            Buffer.BlockCopy(lobbyToken, 0, _token, 0, VpbNetRendezvous.TokenBytes);
            Phase = RendezvousPhase.Announcing;
        }

        public int Tick(long nowMs, byte[] tx)
        {
            if (Phase == RendezvousPhase.Failed || Phase == RendezvousPhase.Idle) return 0;
            if (_startedMs < 0) _startedMs = nowMs;

            if (Phase == RendezvousPhase.Announcing && nowMs - _startedMs > TimeoutMs)
            {
                Phase = RendezvousPhase.Failed;
                FailureReason = "no answer from the rendezvous after " + (TimeoutMs / 1000)
                    + "s - check the address you were given, that the rendezvous is running, and that UDP is not blocked outbound";
                return 0;
            }

            if (nowMs - _lastSendMs < AnnounceIntervalMs) return 0;
            _lastSendMs = nowMs;
            _nonce = _nextNonce++;
            if (_nextNonce == 0) _nextNonce = 1;

            int n = VpbNetRendezvous.WriteAnnounce(tx, _token, _nonce, _role, _localPort);
            if (n > 0) _announces++;
            return n;
        }

        public bool Consume(byte[] rx, int len)
        {
            if (Phase != RendezvousPhase.Announcing && Phase != RendezvousPhase.Resolved) return false;

            VpbNetRendezvousOp op;
            uint nonce;
            VpbNetEndpoint self;
            VpbNetEndpoint[] peers;
            int count;
            VpbNetRendezvousRefusal reason;
            if (!VpbNetRendezvous.TryReadResponse(rx, len, out op, out nonce, out self, out peers, out count, out reason, _ticket))
                return false;

            if (nonce != _nonce) return false;

            if (op == VpbNetRendezvousOp.Refused)
            {
                if (reason == VpbNetRendezvousRefusal.RateLimited) return true;
                Phase = RendezvousPhase.Failed;
                FailureReason = VpbNetRendezvous.Explain(reason);
                return true;
            }

            Reflexive = self;
            HasTicket = true;
            _peerCount = 0;
            for (int i = 0; i < count && i < _peers.Length; i++)
            {
                if (!peers[i].IsPresent) continue;
                if (peers[i].SameAs(self)) continue;
                _peers[_peerCount++] = peers[i];
            }

            if (_peerCount > 0) Phase = RendezvousPhase.Resolved;
            return true;
        }

        public void Describe(StringBuilder sb)
        {
            sb.Append("rendezvous ");
            switch (Phase)
            {
                case RendezvousPhase.Announcing:
                    sb.Append("announcing (");
                    sb.Append(_announces);
                    sb.Append(" sent), you are ");
                    Reflexive.Describe(sb);
                    break;
                case RendezvousPhase.Resolved:
                    sb.Append("resolved ");
                    sb.Append(_peerCount);
                    sb.Append(" peer(s), you are ");
                    Reflexive.Describe(sb);
                    for (int i = 0; i < _peerCount; i++)
                    {
                        sb.Append(", peer ");
                        _peers[i].Describe(sb);
                    }
                    break;
                case RendezvousPhase.Failed:
                    sb.Append("failed: ");
                    sb.Append(FailureReason);
                    break;
                default:
                    sb.Append("idle");
                    break;
            }
        }

        public string ExplainNoPeer()
        {
            if (Phase == RendezvousPhase.Failed) return FailureReason;
            if (Phase == RendezvousPhase.Resolved) return null;

            if (_announces < HintAfterAnnounces || !Reflexive.IsPresent) return null;

            return "the rendezvous is answering, so your address and network are fine - it just has not seen the other peer."
                + " Check that they are using the same room code and the same rendezvous address, and that they have started.";
        }
    }
}
