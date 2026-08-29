using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VpbNet;

namespace VpbNet.Rendezvous
{
    public sealed class RendezvousServer : IDisposable
    {
        public const int DefaultPort = 47773;

        readonly RendezvousTable _table = new RendezvousTable();
        readonly byte[] _rx = new byte[2048 + VpbNetRendezvous.RelayHeaderBytes];
        readonly byte[] _tx = new byte[2048 + VpbNetRendezvous.RelayHeaderBytes];
        readonly byte[] _token = new byte[VpbNetRendezvous.TokenBytes];
        readonly byte[] _ticket = new byte[VpbNetRendezvous.TicketBytes];
        readonly byte[] _outTicket = new byte[VpbNetRendezvous.TicketBytes];
        readonly byte[] _relayTx = new byte[2048];
        readonly VpbNetEndpoint[] _peers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];

        Socket _socket;
        EndPoint _from;
        bool _verbose;
        bool _relayEnabled = true;

        long _served;
        long _refused;
        long _ignored;
        long _relayed;

        public long Served { get { return _served; } }
        public long Refused { get { return _refused; } }
        public long Ignored { get { return _ignored; } }
        public long Relayed { get { return _relayed; } }
        public RendezvousTable Table { get { return _table; } }

        public int BoundPort
        {
            get
            {
                IPEndPoint ep = _socket == null ? null : _socket.LocalEndPoint as IPEndPoint;
                return ep == null ? 0 : ep.Port;
            }
        }

        public bool RelayEnabled { get { return _relayEnabled; } set { _relayEnabled = value; } }

        public void Start(int port, bool verbose)
        {
            _verbose = verbose;
            _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            _socket.Blocking = false;
            _from = new IPEndPoint(IPAddress.IPv6Any, 0);
        }

        public void Poll(long nowMs)
        {
            if (_socket == null) return;

            _table.Sweep(nowMs);

            while (true)
            {
                int len;
                EndPoint from = _from;
                try
                {
                    if (_socket.Available <= 0) return;
                    len = _socket.ReceiveFrom(_rx, 0, _rx.Length, SocketFlags.None, ref from);
                }
                catch (SocketException)
                {
                    return;
                }
                if (len <= 0) return;

                IPEndPoint ip = from as IPEndPoint;
                if (ip == null)
                {
                    _ignored++;
                    continue;
                }

                Handle(ip, len, nowMs);
            }
        }

        void Handle(IPEndPoint ip, int len, long nowMs)
        {
            uint nonce;
            byte role;
            ushort localPort;
            VpbNetRendezvousRefusal refusal;

            if (len > 4 && _rx[3] == (byte)VpbNetRendezvousOp.Relay)
            {
                if (_relayEnabled) HandleRelay(ip, len, nowMs);
                else _ignored++;
                return;
            }

            if (!VpbNetRendezvous.TryReadAnnounce(_rx, len, _token, out nonce, out role, out localPort, out refusal))
            {
                if (refusal == VpbNetRendezvousRefusal.Version)
                {
                    Send(ip, VpbNetRendezvous.WriteRefused(_tx, nonce, refusal));
                    _refused++;
                    return;
                }
                _ignored++;
                return;
            }

            VpbNetEndpoint self = ToEndpoint(ip);
            if (!self.IsPresent)
            {
                _ignored++;
                return;
            }

            int peerCount;
            VpbNetRendezvousRefusal result = _table.Announce(_token, self, localPort, role, nowMs, _peers, out peerCount, _ticket);
            if (result != VpbNetRendezvousRefusal.None)
            {
                Send(ip, VpbNetRendezvous.WriteRefused(_tx, nonce, result));
                _refused++;
                return;
            }

            Send(ip, VpbNetRendezvous.WritePeers(_tx, nonce, self, _peers, peerCount, _ticket));
            _served++;
        }

        void HandleRelay(IPEndPoint ip, int len, long nowMs)
        {
            int offset, payloadLen;
            if (!VpbNetRendezvous.TryReadRelay(_rx, len, _token, _ticket, out offset, out payloadLen))
            {
                _ignored++;
                return;
            }

            VpbNetEndpoint from = ToEndpoint(ip);
            if (!from.IsPresent)
            {
                _ignored++;
                return;
            }

            int n = _table.Forward(_token, _ticket, from, nowMs, _peers);
            if (n <= 0)
            {
                _ignored++;
                return;
            }

            if (payloadLen > _relayTx.Length) { _ignored++; return; }
            Buffer.BlockCopy(_rx, offset, _relayTx, 0, payloadLen);

            Array.Clear(_outTicket, 0, _outTicket.Length);
            for (int i = 0; i < n; i++)
            {
                IPEndPoint to = ToIpEndPoint(_peers[i]);
                if (to == null) continue;
                int outLen = VpbNetRendezvous.WriteRelay(_tx, _token, _outTicket, _relayTx, 0, payloadLen);
                if (outLen <= 0) continue;
                try { _socket.SendTo(_tx, 0, outLen, SocketFlags.None, to); }
                catch (SocketException) { }
            }
            _relayed++;
        }

        static IPEndPoint ToIpEndPoint(VpbNetEndpoint ep)
        {
            if (!ep.IsPresent) return null;
            try { return new IPEndPoint(new IPAddress(ep.Address), ep.Port); }
            catch { return null; }
        }

        void Send(IPEndPoint to, int len)
        {
            if (len <= 0) return;
            if (len > VpbNetRendezvous.RequestBytes) return;
            try { _socket.SendTo(_tx, 0, len, SocketFlags.None, to); }
            catch (SocketException) { }
        }

        public void ReportLine(StringBuilder sb, long nowMs)
        {
            sb.Length = 0;
            sb.Append("rooms ");
            sb.Append(_table.RoomCount);
            sb.Append(" peers ");
            sb.Append(_table.PeerCount);
            sb.Append(" | served ");
            sb.Append(_served);
            sb.Append(" refused ");
            sb.Append(_refused);
            sb.Append(" relayed ");
            sb.Append(_relayed);
            sb.Append(" ignored ");
            sb.Append(_ignored);
        }

        public bool Verbose { get { return _verbose; } }

        static VpbNetEndpoint ToEndpoint(IPEndPoint ip)
        {
            VpbNetEndpoint ep = new VpbNetEndpoint();
            IPAddress addr = ip.Address;
            if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();

            byte[] bytes = addr.GetAddressBytes();
            if (bytes.Length == 4) ep.Family = VpbNetRendezvous.FamilyV4;
            else if (bytes.Length == 16) ep.Family = VpbNetRendezvous.FamilyV6;
            else return ep;

            ep.Address = bytes;
            ep.Port = (ushort)ip.Port;
            return ep;
        }

        public void Dispose()
        {
            try
            {
                if (_socket != null) _socket.Close();
            }
            catch { }
            _socket = null;
        }
    }
}
