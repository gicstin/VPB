using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using VpbNet;

namespace VpbNet.Rendezvous
{
    public sealed class RendezvousTable
    {
        public const int DefaultTtlMs = 60000;
        public const int MaxRooms = 4096;
        public const int RateWindowMs = 10000;
        public const int MaxRequestsPerWindow = 20;
        public const int MaxRateSources = 8192;
        public const int SweepIntervalMs = 5000;

        struct Entry
        {
            public VpbNetEndpoint Ep;
            public ushort LocalPort;
            public byte Role;
            public long SeenMs;
            public byte[] Ticket;
        }

        sealed class Room
        {
            public readonly Entry[] Entries = new Entry[VpbNetRendezvous.MaxPeers];
            public int Count;
            public long TouchedMs;
        }

        sealed class RateSource
        {
            public long WindowStartMs;
            public int Count;
        }

        readonly Dictionary<string, Room> _rooms = new Dictionary<string, Room>(StringComparer.Ordinal);
        readonly Dictionary<string, RateSource> _rate = new Dictionary<string, RateSource>(StringComparer.Ordinal);
        readonly List<string> _dead = new List<string>();
        readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        readonly int _ttlMs;
        long _nextSweepMs;

        public RendezvousTable() : this(DefaultTtlMs) { }

        public RendezvousTable(int ttlMs)
        {
            _ttlMs = ttlMs > 0 ? ttlMs : DefaultTtlMs;
        }

        public int RoomCount { get { return _rooms.Count; } }
        public int RateSourceCount { get { return _rate.Count; } }

        public int PeerCount
        {
            get
            {
                int n = 0;
                foreach (KeyValuePair<string, Room> kv in _rooms) n += kv.Value.Count;
                return n;
            }
        }

        public VpbNetRendezvousRefusal Announce(byte[] token, VpbNetEndpoint from, ushort localPort, byte role,
            long nowMs, VpbNetEndpoint[] peersOut, out int peerCount)
        {
            return Announce(token, from, localPort, role, nowMs, peersOut, out peerCount, null);
        }

        public VpbNetRendezvousRefusal Announce(byte[] token, VpbNetEndpoint from, ushort localPort, byte role,
            long nowMs, VpbNetEndpoint[] peersOut, out int peerCount, byte[] ticketOut)
        {
            peerCount = 0;
            if (token == null || token.Length < VpbNetRendezvous.TokenBytes) return VpbNetRendezvousRefusal.Malformed;
            if (!from.IsPresent || peersOut == null) return VpbNetRendezvousRefusal.Malformed;

            Sweep(nowMs);

            if (!AllowRate(from, nowMs)) return VpbNetRendezvousRefusal.RateLimited;

            string key = Hex(token, VpbNetRendezvous.TokenBytes);
            Room room;
            if (!_rooms.TryGetValue(key, out room))
            {
                if (_rooms.Count >= MaxRooms) return VpbNetRendezvousRefusal.TableFull;
                room = new Room();
                _rooms[key] = room;
            }

            int slot = -1;
            for (int i = 0; i < room.Count; i++)
            {
                if (room.Entries[i].Ep.SameAs(from))
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                if (room.Count >= VpbNetRendezvous.MaxPeers)
                {
                    if (room.Count == 0) _rooms.Remove(key);
                    return VpbNetRendezvousRefusal.RoomFull;
                }
                slot = room.Count++;
            }

            if (room.Entries[slot].Ticket == null)
            {
                byte[] t = new byte[VpbNetRendezvous.TicketBytes];
                _rng.GetBytes(t);
                room.Entries[slot].Ticket = t;
            }

            room.Entries[slot].Ep = from;
            room.Entries[slot].LocalPort = localPort;
            room.Entries[slot].Role = role;
            room.Entries[slot].SeenMs = nowMs;
            room.TouchedMs = nowMs;

            if (ticketOut != null && ticketOut.Length >= VpbNetRendezvous.TicketBytes)
                Buffer.BlockCopy(room.Entries[slot].Ticket, 0, ticketOut, 0, VpbNetRendezvous.TicketBytes);

            for (int i = 0; i < room.Count; i++)
            {
                if (i == slot) continue;
                if (nowMs - room.Entries[i].SeenMs > _ttlMs) continue;
                if (peerCount >= VpbNetRendezvous.MaxReturnedPeers || peerCount >= peersOut.Length) break;
                peersOut[peerCount++] = room.Entries[i].Ep;
            }

            return VpbNetRendezvousRefusal.None;
        }

        public int Forward(byte[] token, byte[] ticket, VpbNetEndpoint from, long nowMs, VpbNetEndpoint[] toOut)
        {
            if (token == null || ticket == null || toOut == null) return 0;
            if (token.Length < VpbNetRendezvous.TokenBytes || ticket.Length < VpbNetRendezvous.TicketBytes) return 0;
            if (!from.IsPresent) return 0;

            Room room;
            if (!_rooms.TryGetValue(Hex(token, VpbNetRendezvous.TokenBytes), out room)) return 0;

            int self = -1;
            for (int i = 0; i < room.Count; i++)
            {
                if (!room.Entries[i].Ep.SameAs(from)) continue;
                if (!SameTicket(room.Entries[i].Ticket, ticket)) return 0;
                self = i;
                break;
            }
            if (self < 0) return 0;

            room.Entries[self].SeenMs = nowMs;
            room.TouchedMs = nowMs;

            int n = 0;
            for (int i = 0; i < room.Count; i++)
            {
                if (i == self) continue;
                if (nowMs - room.Entries[i].SeenMs > _ttlMs) continue;
                if (n >= toOut.Length) break;
                toOut[n++] = room.Entries[i].Ep;
            }
            return n;
        }

        static bool SameTicket(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length < VpbNetRendezvous.TicketBytes || b.Length < VpbNetRendezvous.TicketBytes)
                return false;

            int diff = 0;
            for (int i = 0; i < VpbNetRendezvous.TicketBytes; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public void Sweep(long nowMs)
        {
            if (nowMs < _nextSweepMs) return;
            _nextSweepMs = nowMs + SweepIntervalMs;

            _dead.Clear();
            foreach (KeyValuePair<string, Room> kv in _rooms)
            {
                Room room = kv.Value;
                int w = 0;
                for (int i = 0; i < room.Count; i++)
                {
                    if (nowMs - room.Entries[i].SeenMs > _ttlMs) continue;
                    if (w != i) room.Entries[w] = room.Entries[i];
                    w++;
                }
                for (int i = w; i < room.Count; i++) room.Entries[i] = new Entry();
                room.Count = w;
                if (w == 0) _dead.Add(kv.Key);
            }
            for (int i = 0; i < _dead.Count; i++) _rooms.Remove(_dead[i]);

            _dead.Clear();
            foreach (KeyValuePair<string, RateSource> kv in _rate)
            {
                if (nowMs - kv.Value.WindowStartMs > RateWindowMs * 2L) _dead.Add(kv.Key);
            }
            for (int i = 0; i < _dead.Count; i++) _rate.Remove(_dead[i]);
        }

        bool AllowRate(VpbNetEndpoint from, long nowMs)
        {
            string key = Hex(from.Address, from.Address.Length);
            RateSource src;
            if (!_rate.TryGetValue(key, out src))
            {
                if (_rate.Count >= MaxRateSources) return false;
                src = new RateSource();
                src.WindowStartMs = nowMs;
                _rate[key] = src;
            }

            if (nowMs - src.WindowStartMs > RateWindowMs)
            {
                src.WindowStartMs = nowMs;
                src.Count = 0;
            }

            src.Count++;
            return src.Count <= MaxRequestsPerWindow;
        }

        static string Hex(byte[] bytes, int count)
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
    }
}
