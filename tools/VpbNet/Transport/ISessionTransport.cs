using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace VpbNet.Transport
{
    public enum TransportRole : byte
    {
        Host = 0,
        Join = 1,
    }

    public enum PeerEventKind : byte
    {
        None = 0,
        Up = 1,
        Down = 2,
        Stalled = 3,
        Resumed = 4,
    }

    public sealed class TransportOptions
    {
        public TransportRole Role;
        public int MaxPeers;

        public string ConnectBlob;
        public byte[] SessionKey;

        public byte[] LobbyToken;
    }

    public struct PeerStats
    {
        public uint Sent;
        public uint Received;
        public uint Lost;
        public uint Reordered;
        public uint RttMicros;
        public uint JitterMicros;
    }

    public interface ISessionTransport : IDisposable
    {
        string Name { get; }

        string InviteBlob { get; }

        string FailureReason { get; }

        string StatusHint { get; }

        void Start(TransportOptions options);

        void Poll(long nowMs);

        bool Send(int peerId, byte[] buffer, int offset, int count, byte channel, bool reliable);

        int Receive(byte[] buffer, out int peerId, out byte channel);

        bool NextPeerEvent(out int peerId, out PeerEventKind kind, out string reason);

        bool TryGetStats(int peerId, out PeerStats stats);

        void CollectWaitSockets(List<Socket> into);
    }
}
