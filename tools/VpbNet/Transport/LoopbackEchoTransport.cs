using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace VpbNet.Transport
{
    public sealed class LoopbackEchoTransport : ISessionTransport
    {
        public const int SelfPeerId = 0;

        readonly Queue<byte[]> _free = new Queue<byte[]>();
        readonly Queue<Pending> _pending = new Queue<Pending>();
        readonly int _slotBytes;
        readonly int _maxQueued;

        bool _started;
        bool _peerAnnounced;
        uint _sent;
        uint _received;

        struct Pending
        {
            public byte[] Buffer;
            public int Length;
            public int PeerId;
            public byte Channel;
        }

        public LoopbackEchoTransport(int slotBytes, int maxQueued)
        {
            _slotBytes = slotBytes;
            _maxQueued = maxQueued;
            for (int i = 0; i < maxQueued; i++) _free.Enqueue(new byte[slotBytes]);
        }

        public string Name { get { return "loopback-echo"; } }

        public string InviteBlob { get { return string.Empty; } }

        public string FailureReason { get { return null; } }

        public string StatusHint { get { return null; } }

        public int Dropped { get; private set; }

        public void Start(TransportOptions options)
        {
            _started = true;
        }

        public void Poll(long nowMs)
        {
        }

        public bool Send(int peerId, byte[] buffer, int offset, int count, byte channel, bool reliable)
        {
            if (buffer == null || count <= 0 || count > _slotBytes) return false;
            if (_pending.Count >= _maxQueued || _free.Count == 0)
            {
                Dropped++;
                return false;
            }

            byte[] slot = _free.Dequeue();
            Buffer.BlockCopy(buffer, offset, slot, 0, count);
            _pending.Enqueue(new Pending { Buffer = slot, Length = count, PeerId = peerId, Channel = channel });
            _sent++;
            return true;
        }

        public int Receive(byte[] buffer, out int peerId, out byte channel)
        {
            peerId = -1;
            channel = 0;
            if (_pending.Count == 0) return 0;

            Pending p = _pending.Dequeue();
            int len = p.Length;
            if (len > buffer.Length) len = buffer.Length;
            Buffer.BlockCopy(p.Buffer, 0, buffer, 0, len);
            _free.Enqueue(p.Buffer);
            peerId = p.PeerId;
            channel = p.Channel;
            _received++;
            return len;
        }

        public bool NextPeerEvent(out int peerId, out PeerEventKind kind, out string reason)
        {
            peerId = SelfPeerId;
            kind = PeerEventKind.None;
            reason = string.Empty;

            if (!_started || _peerAnnounced) return false;
            _peerAnnounced = true;
            kind = PeerEventKind.Up;
            reason = "loopback";
            return true;
        }

        public bool TryGetStats(int peerId, out PeerStats stats)
        {
            stats = new PeerStats();
            if (peerId != SelfPeerId || !_started) return false;
            stats.Sent = _sent;
            stats.Received = _received;
            return true;
        }

        public void CollectWaitSockets(List<Socket> into)
        {
        }

        public void Dispose()
        {
            _pending.Clear();
            _free.Clear();
        }
    }
}
