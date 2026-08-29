using System;
using System.Text;

namespace VpbNet
{
    public enum VpbNetRendezvousOp : byte
    {
        None = 0,
        Announce = 1,
        Peers = 2,
        Refused = 3,
        Relay = 4
    }

    public enum VpbNetRendezvousRefusal : byte
    {
        None = 0,
        RateLimited = 1,
        TableFull = 2,
        RoomFull = 3,
        Version = 4,
        Malformed = 5
    }

    public struct VpbNetEndpoint
    {
        public byte Family;
        public byte[] Address;
        public ushort Port;

        public bool IsPresent { get { return Family != 0 && Address != null; } }

        public static int AddressBytesFor(byte family)
        {
            if (family == VpbNetRendezvous.FamilyV4) return 4;
            if (family == VpbNetRendezvous.FamilyV6) return 16;
            return -1;
        }

        public bool SameAs(VpbNetEndpoint other)
        {
            if (Family != other.Family || Port != other.Port) return false;
            if (Address == null || other.Address == null) return Address == other.Address;
            if (Address.Length != other.Address.Length) return false;
            for (int i = 0; i < Address.Length; i++)
            {
                if (Address[i] != other.Address[i]) return false;
            }
            return true;
        }

        public void Describe(StringBuilder sb)
        {
            if (!IsPresent)
            {
                sb.Append("(none)");
                return;
            }
            if (Family == VpbNetRendezvous.FamilyV6) sb.Append('[');
            for (int i = 0; i < Address.Length; i++)
            {
                if (Family == VpbNetRendezvous.FamilyV4)
                {
                    if (i > 0) sb.Append('.');
                    sb.Append(Address[i]);
                }
                else
                {
                    if (i > 0 && (i % 2) == 0) sb.Append(':');
                    sb.Append(Address[i].ToString("x2"));
                }
            }
            if (Family == VpbNetRendezvous.FamilyV6) sb.Append(']');
            sb.Append(':');
            sb.Append(Port);
        }
    }

    public static class VpbNetRendezvous
    {
        public const byte Magic0 = (byte)'V';
        public const byte Magic1 = (byte)'R';
        public const byte Version = 1;

        public const byte FamilyV4 = 4;
        public const byte FamilyV6 = 6;

        public const int TokenBytes = 16;
        public const int MaxPeers = 4;
        public const int MaxReturnedPeers = MaxPeers - 1;

        public const int RequestBytes = 128;
        public const int RequestUsedBytes = 28;

        public const int TicketBytes = 8;

        public const int ResponseHeaderBytes = 10;
        public const int MaxEndpointBytes = 1 + 16 + 2;
        public const int MaxResponseBytes = ResponseHeaderBytes + TicketBytes + MaxEndpointBytes * (1 + MaxReturnedPeers);

        public const int RelayHeaderBytes = 4 + TokenBytes + TicketBytes;

        public const byte RoleHost = 0;
        public const byte RoleJoin = 1;

        public static int WriteAnnounce(byte[] buf, byte[] token, uint nonce, byte role, ushort localPort)
        {
            if (buf == null || buf.Length < RequestBytes) return 0;
            if (token == null || token.Length < TokenBytes) return 0;

            for (int i = 0; i < RequestBytes; i++) buf[i] = 0;

            buf[0] = Magic0;
            buf[1] = Magic1;
            buf[2] = Version;
            buf[3] = (byte)VpbNetRendezvousOp.Announce;
            Buffer.BlockCopy(token, 0, buf, 4, TokenBytes);
            WriteU32(buf, 20, nonce);
            buf[24] = role;
            buf[25] = 0;
            WriteU16(buf, 26, localPort);
            return RequestBytes;
        }

        public static bool TryReadAnnounce(byte[] buf, int len, byte[] tokenOut,
            out uint nonce, out byte role, out ushort localPort, out VpbNetRendezvousRefusal refusal)
        {
            nonce = 0;
            role = RoleHost;
            localPort = 0;
            refusal = VpbNetRendezvousRefusal.Malformed;

            if (buf == null || tokenOut == null || tokenOut.Length < TokenBytes) return false;
            if (len != RequestBytes) return false;
            if (buf[0] != Magic0 || buf[1] != Magic1) return false;
            if (buf[3] != (byte)VpbNetRendezvousOp.Announce) return false;
            if (buf[2] != Version)
            {
                refusal = VpbNetRendezvousRefusal.Version;
                return false;
            }

            Buffer.BlockCopy(buf, 4, tokenOut, 0, TokenBytes);
            nonce = ReadU32(buf, 20);
            role = buf[24];
            if (role != RoleHost && role != RoleJoin) return false;
            localPort = ReadU16(buf, 26);

            refusal = VpbNetRendezvousRefusal.None;
            return true;
        }

        static readonly byte[] ZeroTicket = new byte[TicketBytes];

        public static int WritePeers(byte[] buf, uint nonce, VpbNetEndpoint self, VpbNetEndpoint[] peers, int peerCount)
        {
            return WritePeers(buf, nonce, self, peers, peerCount, ZeroTicket);
        }

        public static int WritePeers(byte[] buf, uint nonce, VpbNetEndpoint self, VpbNetEndpoint[] peers, int peerCount, byte[] ticket)
        {
            if (buf == null || buf.Length < MaxResponseBytes) return 0;
            if (ticket == null || ticket.Length < TicketBytes) return 0;
            if (peerCount < 0) peerCount = 0;
            if (peerCount > MaxReturnedPeers) peerCount = MaxReturnedPeers;

            buf[0] = Magic0;
            buf[1] = Magic1;
            buf[2] = Version;
            buf[3] = (byte)VpbNetRendezvousOp.Peers;
            WriteU32(buf, 4, nonce);
            buf[8] = (byte)peerCount;
            buf[9] = (byte)VpbNetRendezvousRefusal.None;

            Buffer.BlockCopy(ticket, 0, buf, ResponseHeaderBytes, TicketBytes);

            int w = ResponseHeaderBytes + TicketBytes;
            w = WriteEndpoint(buf, w, self);
            if (w < 0) return 0;

            for (int i = 0; i < peerCount; i++)
            {
                w = WriteEndpoint(buf, w, peers[i]);
                if (w < 0) return 0;
            }
            return w;
        }

        public static int WriteRefused(byte[] buf, uint nonce, VpbNetRendezvousRefusal reason)
        {
            if (buf == null || buf.Length < ResponseHeaderBytes) return 0;
            buf[0] = Magic0;
            buf[1] = Magic1;
            buf[2] = Version;
            buf[3] = (byte)VpbNetRendezvousOp.Refused;
            WriteU32(buf, 4, nonce);
            buf[8] = 0;
            buf[9] = (byte)reason;
            return ResponseHeaderBytes;
        }

        public static bool TryReadResponse(byte[] buf, int len, out VpbNetRendezvousOp op, out uint nonce,
            out VpbNetEndpoint self, out VpbNetEndpoint[] peers, out int peerCount, out VpbNetRendezvousRefusal reason)
        {
            return TryReadResponse(buf, len, out op, out nonce, out self, out peers, out peerCount, out reason, null);
        }

        public static bool TryReadResponse(byte[] buf, int len, out VpbNetRendezvousOp op, out uint nonce,
            out VpbNetEndpoint self, out VpbNetEndpoint[] peers, out int peerCount, out VpbNetRendezvousRefusal reason,
            byte[] ticketOut)
        {
            op = VpbNetRendezvousOp.None;
            nonce = 0;
            self = new VpbNetEndpoint();
            peers = null;
            peerCount = 0;
            reason = VpbNetRendezvousRefusal.Malformed;

            if (buf == null || len < ResponseHeaderBytes || len > MaxResponseBytes) return false;
            if (buf[0] != Magic0 || buf[1] != Magic1 || buf[2] != Version) return false;

            byte rawOp = buf[3];
            if (rawOp != (byte)VpbNetRendezvousOp.Peers && rawOp != (byte)VpbNetRendezvousOp.Refused) return false;

            op = (VpbNetRendezvousOp)rawOp;
            nonce = ReadU32(buf, 4);
            int count = buf[8];
            byte rawReason = buf[9];
            if (rawReason > (byte)VpbNetRendezvousRefusal.Malformed) return false;
            reason = (VpbNetRendezvousRefusal)rawReason;

            if (op == VpbNetRendezvousOp.Refused)
            {
                if (len != ResponseHeaderBytes || count != 0) return false;
                if (reason == VpbNetRendezvousRefusal.None) return false;
                return true;
            }

            if (reason != VpbNetRendezvousRefusal.None) return false;
            if (count > MaxReturnedPeers) return false;
            if (len < ResponseHeaderBytes + TicketBytes) return false;

            if (ticketOut != null && ticketOut.Length >= TicketBytes)
                Buffer.BlockCopy(buf, ResponseHeaderBytes, ticketOut, 0, TicketBytes);

            int r = ResponseHeaderBytes + TicketBytes;
            r = ReadEndpoint(buf, r, len, out self);
            if (r < 0) return false;

            peers = new VpbNetEndpoint[count];
            for (int i = 0; i < count; i++)
            {
                r = ReadEndpoint(buf, r, len, out peers[i]);
                if (r < 0) return false;
            }
            if (r != len) return false;

            peerCount = count;
            return true;
        }

        public static int WriteRelay(byte[] buf, byte[] token, byte[] ticket, byte[] payload, int offset, int count)
        {
            if (buf == null || token == null || ticket == null || payload == null) return 0;
            if (token.Length < TokenBytes || ticket.Length < TicketBytes) return 0;
            if (count <= 0 || RelayHeaderBytes + count > buf.Length) return 0;

            buf[0] = Magic0;
            buf[1] = Magic1;
            buf[2] = Version;
            buf[3] = (byte)VpbNetRendezvousOp.Relay;
            Buffer.BlockCopy(token, 0, buf, 4, TokenBytes);
            Buffer.BlockCopy(ticket, 0, buf, 4 + TokenBytes, TicketBytes);
            Buffer.BlockCopy(payload, offset, buf, RelayHeaderBytes, count);
            return RelayHeaderBytes + count;
        }

        public static bool TryReadRelay(byte[] buf, int len, byte[] tokenOut, byte[] ticketOut,
            out int payloadOffset, out int payloadLen)
        {
            payloadOffset = 0;
            payloadLen = 0;
            if (buf == null || tokenOut == null || ticketOut == null) return false;
            if (tokenOut.Length < TokenBytes || ticketOut.Length < TicketBytes) return false;
            if (len <= RelayHeaderBytes) return false;
            if (buf[0] != Magic0 || buf[1] != Magic1 || buf[2] != Version) return false;
            if (buf[3] != (byte)VpbNetRendezvousOp.Relay) return false;

            Buffer.BlockCopy(buf, 4, tokenOut, 0, TokenBytes);
            Buffer.BlockCopy(buf, 4 + TokenBytes, ticketOut, 0, TicketBytes);
            payloadOffset = RelayHeaderBytes;
            payloadLen = len - RelayHeaderBytes;
            return true;
        }

        public static bool IsRendezvousDatagram(byte[] buf, int len)
        {
            return buf != null && len >= 4 && buf[0] == Magic0 && buf[1] == Magic1;
        }

        public static string Explain(VpbNetRendezvousRefusal reason)
        {
            switch (reason)
            {
                case VpbNetRendezvousRefusal.RateLimited:
                    return "The rendezvous refused this address for sending too fast. Wait a few seconds and retry, or use a different rendezvous.";
                case VpbNetRendezvousRefusal.TableFull:
                    return "The rendezvous is at capacity. Try again shortly, use a different one, or exchange the host address directly.";
                case VpbNetRendezvousRefusal.RoomFull:
                    return "This room already holds " + MaxPeers + " peers. Someone must leave before another can join.";
                case VpbNetRendezvousRefusal.Version:
                    return "The rendezvous speaks a different protocol version. Update VPB, or point at a rendezvous that matches this build.";
                case VpbNetRendezvousRefusal.Malformed:
                    return "The rendezvous rejected the request as malformed. This is a bug or a middlebox rewriting UDP - try another network or rendezvous.";
                default:
                    return null;
            }
        }

        static int WriteEndpoint(byte[] buf, int w, VpbNetEndpoint ep)
        {
            int n = VpbNetEndpoint.AddressBytesFor(ep.Family);
            if (n < 0 || ep.Address == null || ep.Address.Length != n) return -1;
            if (w + 1 + n + 2 > buf.Length) return -1;

            buf[w++] = ep.Family;
            Buffer.BlockCopy(ep.Address, 0, buf, w, n);
            w += n;
            WriteU16(buf, w, ep.Port);
            return w + 2;
        }

        static int ReadEndpoint(byte[] buf, int r, int len, out VpbNetEndpoint ep)
        {
            ep = new VpbNetEndpoint();
            if (r + 1 > len) return -1;

            byte family = buf[r++];
            int n = VpbNetEndpoint.AddressBytesFor(family);
            if (n < 0) return -1;
            if (r + n + 2 > len) return -1;

            byte[] addr = new byte[n];
            Buffer.BlockCopy(buf, r, addr, 0, n);
            r += n;

            ep.Family = family;
            ep.Address = addr;
            ep.Port = ReadU16(buf, r);
            return r + 2;
        }

        static void WriteU16(byte[] buf, int at, ushort v)
        {
            buf[at] = (byte)(v & 0xFF);
            buf[at + 1] = (byte)((v >> 8) & 0xFF);
        }

        static void WriteU32(byte[] buf, int at, uint v)
        {
            buf[at] = (byte)(v & 0xFF);
            buf[at + 1] = (byte)((v >> 8) & 0xFF);
            buf[at + 2] = (byte)((v >> 16) & 0xFF);
            buf[at + 3] = (byte)((v >> 24) & 0xFF);
        }

        static ushort ReadU16(byte[] buf, int at)
        {
            return (ushort)(buf[at] | (buf[at + 1] << 8));
        }

        static uint ReadU32(byte[] buf, int at)
        {
            return (uint)(buf[at] | (buf[at + 1] << 8) | (buf[at + 2] << 16) | (buf[at + 3] << 24));
        }
    }
}
