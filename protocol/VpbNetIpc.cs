  using System;
using System.Text;

namespace VpbNet
{
    public enum VpbIpcMsg : byte
    {
        None = 0,
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        Ping = 4,
        Pong = 5,
        Echo = 6,
        EchoReply = 7,
        Bye = 8,
        Log = 9,
        OpenSession = 10,
        CloseSession = 11,
        SessionState = 12,
        PeerEvent = 13,
        Data = 14,
        PeerStats = 15,
    }

    public enum VpbIpcReject : byte
    {
        None = 0,
        BadSecret = 1,
        IpcVersionMismatch = 2,
        AlreadyBound = 3,
        RateLimited = 4,
        Malformed = 5,
        UnknownBackend = 6,
        SessionBusy = 7,
    }

    public enum VpbIpcSession : byte
    {
        Idle = 0,
        Listening = 1,
        Connecting = 2,
        Connected = 3,
        Failed = 4,
        Closed = 5,
    }

    public enum VpbIpcPeerEvent : byte
    {
        None = 0,
        Up = 1,
        Down = 2,
        Stalled = 3,
        Resumed = 4,
    }

    public enum VpbIpcBackend : byte
    {
        LoopbackEcho = 0,
        Lan = 1,
        Steam = 2,
    }

    public static class VpbIpc
    {
        public const byte Magic0 = (byte)'V';
        public const byte Magic1 = (byte)'P';

        public const byte IpcVersion = 3;
        public const int HeaderSize = 28;
        public const int MaxDatagram = 1200;
        public const int MaxPayload = MaxDatagram - HeaderSize;
        public const int TokenSize = 16;
        public const int SecretSize = 32;
        public const int MaxEchoPayload = 1024;
        public const int MaxTextBytes = 512;

        public const int DataHeaderSize = 4;
        public const int MaxDataPayload = 1024;
        public const int MaxCodeBytes = 64;
        public const int MaxBlobBytes = 128;
        public const int MaxPeers = 4;

        public const byte DataFlagReliable = 1;

        public const int HeartbeatMs = 1000;
        public const int PeerTimeoutMs = 5000;

        // Loopback stall ≠ network — VaM blocks for seconds on load; 5s killed healthy sessions.
        public const int StalledTimeoutMs = 45000;
        public const int StallWarnMs = 4000;
        public const int HandshakeTimeoutMs = 8000;
        public const int MaxMessagesPerSecond = 1000;

        public static bool TryReadHeader(byte[] buf, int len, out byte version, out VpbIpcMsg type, out uint seq, out int payloadOffset, out int payloadLen)
        {
            version = 0;
            type = VpbIpcMsg.None;
            seq = 0;
            payloadOffset = HeaderSize;
            payloadLen = 0;

            if (buf == null || len < HeaderSize) return false;
            if (buf[0] != Magic0 || buf[1] != Magic1) return false;

            version = buf[2];
            type = (VpbIpcMsg)buf[3];
            seq = ReadU32(buf, 4);
            int declared = ReadU16(buf, 8);
            if (declared < 0 || declared > MaxPayload) return false;
            if (HeaderSize + declared > len) return false;
            payloadLen = declared;
            return true;
        }

        public static bool TokenMatches(byte[] buf, byte[] token)
        {
            if (buf == null || token == null || token.Length != TokenSize) return false;
            int diff = 0;
            for (int i = 0; i < TokenSize; i++) diff |= buf[12 + i] ^ token[i];
            return diff == 0;
        }

        public static void CopyToken(byte[] buf, byte[] token)
        {
            for (int i = 0; i < TokenSize; i++) token[i] = buf[12 + i];
        }

        public static int WriteHeader(byte[] buf, VpbIpcMsg type, uint seq, byte[] token, int payloadLen)
        {
            return WriteHeaderVersioned(buf, IpcVersion, type, seq, token, payloadLen);
        }

        public static int WriteHeaderVersioned(byte[] buf, byte version, VpbIpcMsg type, uint seq, byte[] token, int payloadLen)
        {
            buf[0] = Magic0;
            buf[1] = Magic1;
            buf[2] = version;
            buf[3] = (byte)type;
            WriteU32(buf, 4, seq);
            WriteU16(buf, 8, payloadLen);
            WriteU16(buf, 10, 0);
            if (token != null && token.Length == TokenSize)
            {
                for (int i = 0; i < TokenSize; i++) buf[12 + i] = token[i];
            }
            else
            {
                for (int i = 0; i < TokenSize; i++) buf[12 + i] = 0;
            }
            return HeaderSize + payloadLen;
        }

        public static int WriteHello(byte[] buf, uint seq, byte[] secret, ushort appProtoVersion, uint pluginPid)
        {
            for (int i = 0; i < SecretSize; i++) buf[HeaderSize + i] = secret[i];
            WriteU16(buf, HeaderSize + SecretSize, appProtoVersion);
            WriteU32(buf, HeaderSize + SecretSize + 2, pluginPid);
            return WriteHeader(buf, VpbIpcMsg.Hello, seq, null, SecretSize + 6);
        }

        public static bool ReadHello(byte[] buf, int payloadLen, byte[] secretOut, out ushort appProtoVersion, out uint pluginPid)
        {
            appProtoVersion = 0;
            pluginPid = 0;
            if (payloadLen < SecretSize + 6) return false;
            for (int i = 0; i < SecretSize; i++) secretOut[i] = buf[HeaderSize + i];
            appProtoVersion = (ushort)ReadU16(buf, HeaderSize + SecretSize);
            pluginPid = ReadU32(buf, HeaderSize + SecretSize + 2);
            return true;
        }

        public static int WriteWelcome(byte[] buf, uint seq, byte[] token, ushort brokerBuild, uint brokerPid)
        {
            for (int i = 0; i < TokenSize; i++) buf[HeaderSize + i] = token[i];
            WriteU16(buf, HeaderSize + TokenSize, IpcVersion);
            WriteU16(buf, HeaderSize + TokenSize + 2, brokerBuild);
            WriteU32(buf, HeaderSize + TokenSize + 4, brokerPid);
            return WriteHeader(buf, VpbIpcMsg.Welcome, seq, token, TokenSize + 8);
        }

        public static bool ReadWelcome(byte[] buf, int payloadLen, byte[] tokenOut, out ushort brokerIpcVersion, out ushort brokerBuild, out uint brokerPid)
        {
            brokerIpcVersion = 0;
            brokerBuild = 0;
            brokerPid = 0;
            if (payloadLen < TokenSize + 8) return false;
            for (int i = 0; i < TokenSize; i++) tokenOut[i] = buf[HeaderSize + i];
            brokerIpcVersion = (ushort)ReadU16(buf, HeaderSize + TokenSize);
            brokerBuild = (ushort)ReadU16(buf, HeaderSize + TokenSize + 2);
            brokerPid = ReadU32(buf, HeaderSize + TokenSize + 4);
            return true;
        }

        public static int WriteReject(byte[] buf, uint seq, VpbIpcReject reason, string text)
        {
            return WriteRejectVersioned(buf, IpcVersion, seq, reason, text);
        }

        public static int WriteRejectVersioned(byte[] buf, byte version, uint seq, VpbIpcReject reason, string text)
        {
            buf[HeaderSize] = (byte)reason;
            int textLen = WriteText(buf, HeaderSize + 3, text);
            WriteU16(buf, HeaderSize + 1, textLen);
            return WriteHeaderVersioned(buf, version, VpbIpcMsg.Reject, seq, null, 3 + textLen);
        }

        public static VpbIpcReject ReadReject(byte[] buf, int payloadLen, out string text)
        {
            text = string.Empty;
            if (payloadLen < 3) return VpbIpcReject.None;
            VpbIpcReject reason = (VpbIpcReject)buf[HeaderSize];
            int textLen = ReadU16(buf, HeaderSize + 1);
            if (textLen > 0 && 3 + textLen <= payloadLen) text = ReadText(buf, HeaderSize + 3, textLen);
            return reason;
        }

        public static int WritePing(byte[] buf, uint seq, byte[] token, long pluginTicks)
        {
            WriteI64(buf, HeaderSize, pluginTicks);
            return WriteHeader(buf, VpbIpcMsg.Ping, seq, token, 8);
        }

        public static int WritePong(byte[] buf, uint seq, byte[] token, long pluginTicks, long brokerTicks)
        {
            WriteI64(buf, HeaderSize, pluginTicks);
            WriteI64(buf, HeaderSize + 8, brokerTicks);
            return WriteHeader(buf, VpbIpcMsg.Pong, seq, token, 16);
        }

        public static bool ReadPong(byte[] buf, int payloadLen, out long pluginTicks, out long brokerTicks)
        {
            pluginTicks = 0;
            brokerTicks = 0;
            if (payloadLen < 16) return false;
            pluginTicks = ReadI64(buf, HeaderSize);
            brokerTicks = ReadI64(buf, HeaderSize + 8);
            return true;
        }

        public static int WriteEcho(byte[] buf, uint seq, byte[] token, byte[] payload, int payloadLen, bool reply)
        {
            if (payloadLen < 0) payloadLen = 0;
            if (payloadLen > MaxEchoPayload) payloadLen = MaxEchoPayload;
            for (int i = 0; i < payloadLen; i++) buf[HeaderSize + i] = payload[i];
            return WriteHeader(buf, reply ? VpbIpcMsg.EchoReply : VpbIpcMsg.Echo, seq, token, payloadLen);
        }

        public static int WriteBye(byte[] buf, uint seq, byte[] token)
        {
            return WriteHeader(buf, VpbIpcMsg.Bye, seq, token, 0);
        }

        public static int WriteLog(byte[] buf, uint seq, byte[] token, byte level, string text)
        {
            buf[HeaderSize] = level;
            int textLen = WriteText(buf, HeaderSize + 3, text);
            WriteU16(buf, HeaderSize + 1, textLen);
            return WriteHeader(buf, VpbIpcMsg.Log, seq, token, 3 + textLen);
        }

        public static bool ReadLog(byte[] buf, int payloadLen, out byte level, out string text)
        {
            level = 0;
            text = string.Empty;
            if (payloadLen < 3) return false;
            level = buf[HeaderSize];
            int textLen = ReadU16(buf, HeaderSize + 1);
            if (textLen > 0 && 3 + textLen <= payloadLen) text = ReadText(buf, HeaderSize + 3, textLen);
            return true;
        }

        public static int WriteOpenSession(byte[] buf, uint seq, byte[] token, byte backendId, byte role, byte maxPeers, string roomCode, string connectBlob)
        {
            buf[HeaderSize] = backendId;
            buf[HeaderSize + 1] = role;
            buf[HeaderSize + 2] = maxPeers;
            buf[HeaderSize + 3] = 0;
            int codeLen = WriteCapped(buf, HeaderSize + 8, roomCode, MaxCodeBytes);
            int blobLen = WriteCapped(buf, HeaderSize + 8 + codeLen, connectBlob, MaxBlobBytes);
            WriteU16(buf, HeaderSize + 4, codeLen);
            WriteU16(buf, HeaderSize + 6, blobLen);
            return WriteHeader(buf, VpbIpcMsg.OpenSession, seq, token, 8 + codeLen + blobLen);
        }

        public static bool ReadOpenSession(byte[] buf, int payloadLen, out byte backendId, out byte role, out byte maxPeers, out string roomCode, out string connectBlob)
        {
            backendId = 0;
            role = 0;
            maxPeers = 0;
            roomCode = string.Empty;
            connectBlob = string.Empty;
            if (payloadLen < 8) return false;

            backendId = buf[HeaderSize];
            role = buf[HeaderSize + 1];
            maxPeers = buf[HeaderSize + 2];
            int codeLen = ReadU16(buf, HeaderSize + 4);
            int blobLen = ReadU16(buf, HeaderSize + 6);
            if (codeLen < 0 || blobLen < 0 || codeLen > MaxCodeBytes || blobLen > MaxBlobBytes) return false;
            if (8 + codeLen + blobLen > payloadLen) return false;

            roomCode = ReadText(buf, HeaderSize + 8, codeLen);
            connectBlob = ReadText(buf, HeaderSize + 8 + codeLen, blobLen);
            return true;
        }

        public static int WriteCloseSession(byte[] buf, uint seq, byte[] token)
        {
            return WriteHeader(buf, VpbIpcMsg.CloseSession, seq, token, 0);
        }

        public static int WriteSessionState(byte[] buf, uint seq, byte[] token, byte state, byte backendId, int peerCount, string inviteBlob, string text)
        {
            buf[HeaderSize] = state;
            buf[HeaderSize + 1] = backendId;
            WriteU16(buf, HeaderSize + 2, peerCount);
            int inviteLen = WriteCapped(buf, HeaderSize + 8, inviteBlob, MaxBlobBytes);
            int textLen = WriteCapped(buf, HeaderSize + 8 + inviteLen, text, MaxTextBytes);
            WriteU16(buf, HeaderSize + 4, inviteLen);
            WriteU16(buf, HeaderSize + 6, textLen);
            return WriteHeader(buf, VpbIpcMsg.SessionState, seq, token, 8 + inviteLen + textLen);
        }

        public static bool ReadSessionState(byte[] buf, int payloadLen, out byte state, out byte backendId, out int peerCount, out string inviteBlob, out string text)
        {
            state = 0;
            backendId = 0;
            peerCount = 0;
            inviteBlob = string.Empty;
            text = string.Empty;
            if (payloadLen < 8) return false;

            state = buf[HeaderSize];
            backendId = buf[HeaderSize + 1];
            peerCount = ReadU16(buf, HeaderSize + 2);
            int inviteLen = ReadU16(buf, HeaderSize + 4);
            int textLen = ReadU16(buf, HeaderSize + 6);
            if (inviteLen > MaxBlobBytes || textLen > MaxTextBytes) return false;
            if (8 + inviteLen + textLen > payloadLen) return false;

            inviteBlob = ReadText(buf, HeaderSize + 8, inviteLen);
            text = ReadText(buf, HeaderSize + 8 + inviteLen, textLen);
            return true;
        }

        public static int WritePeerEvent(byte[] buf, uint seq, byte[] token, byte kind, int peerId, string text)
        {
            buf[HeaderSize] = kind;
            WriteU16(buf, HeaderSize + 1, peerId);
            int textLen = WriteCapped(buf, HeaderSize + 5, text, MaxTextBytes);
            WriteU16(buf, HeaderSize + 3, textLen);
            return WriteHeader(buf, VpbIpcMsg.PeerEvent, seq, token, 5 + textLen);
        }

        public static bool ReadPeerEvent(byte[] buf, int payloadLen, out byte kind, out int peerId, out string text)
        {
            kind = 0;
            peerId = 0;
            text = string.Empty;
            if (payloadLen < 5) return false;

            kind = buf[HeaderSize];
            peerId = ReadU16(buf, HeaderSize + 1);
            int textLen = ReadU16(buf, HeaderSize + 3);
            if (textLen > 0 && 5 + textLen <= payloadLen) text = ReadText(buf, HeaderSize + 5, textLen);
            return true;
        }

        public static int WriteData(byte[] buf, uint seq, byte[] token, int peerId, byte channel, byte flags, byte[] payload, int offset, int len)
        {
            if (len < 0) len = 0;
            if (len > MaxDataPayload) len = MaxDataPayload;
            WriteU16(buf, HeaderSize, peerId);
            buf[HeaderSize + 2] = channel;
            buf[HeaderSize + 3] = flags;
            Buffer.BlockCopy(payload, offset, buf, HeaderSize + DataHeaderSize, len);
            return WriteHeader(buf, VpbIpcMsg.Data, seq, token, DataHeaderSize + len);
        }

        public static bool ReadDataHeader(byte[] buf, int payloadLen, out int peerId, out byte channel, out byte flags, out int dataOffset, out int dataLen)
        {
            peerId = 0;
            channel = 0;
            flags = 0;
            dataOffset = HeaderSize + DataHeaderSize;
            dataLen = 0;
            if (payloadLen < DataHeaderSize) return false;

            peerId = ReadU16(buf, HeaderSize);
            channel = buf[HeaderSize + 2];
            flags = buf[HeaderSize + 3];
            dataLen = payloadLen - DataHeaderSize;
            if (dataLen > MaxDataPayload) return false;
            return true;
        }

        public static int WritePeerStats(byte[] buf, uint seq, byte[] token, int peerId, uint sent, uint received, uint lost, uint reordered, uint rttMicros, uint jitterMicros)
        {
            WriteU16(buf, HeaderSize, peerId);
            WriteU16(buf, HeaderSize + 2, 0);
            WriteU32(buf, HeaderSize + 4, sent);
            WriteU32(buf, HeaderSize + 8, received);
            WriteU32(buf, HeaderSize + 12, lost);
            WriteU32(buf, HeaderSize + 16, reordered);
            WriteU32(buf, HeaderSize + 20, rttMicros);
            WriteU32(buf, HeaderSize + 24, jitterMicros);
            return WriteHeader(buf, VpbIpcMsg.PeerStats, seq, token, 28);
        }

        public static bool ReadPeerStats(byte[] buf, int payloadLen, out int peerId, out uint sent, out uint received, out uint lost, out uint reordered, out uint rttMicros, out uint jitterMicros)
        {
            peerId = 0;
            sent = 0;
            received = 0;
            lost = 0;
            reordered = 0;
            rttMicros = 0;
            jitterMicros = 0;
            if (payloadLen < 28) return false;

            peerId = ReadU16(buf, HeaderSize);
            sent = ReadU32(buf, HeaderSize + 4);
            received = ReadU32(buf, HeaderSize + 8);
            lost = ReadU32(buf, HeaderSize + 12);
            reordered = ReadU32(buf, HeaderSize + 16);
            rttMicros = ReadU32(buf, HeaderSize + 20);
            jitterMicros = ReadU32(buf, HeaderSize + 24);
            return true;
        }

        static int WriteCapped(byte[] buf, int offset, string text, int maxBytes)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int room = buf.Length - offset;
            if (room > maxBytes) room = maxBytes;
            if (room <= 0) return 0;

            int chars = text.Length;
            while (chars > 0 && Encoding.UTF8.GetByteCount(text.Substring(0, chars)) > room) chars--;
            if (chars <= 0) return 0;
            return Encoding.UTF8.GetBytes(text, 0, chars, buf, offset);
        }

        static int WriteText(byte[] buf, int offset, string text)
        {
            return WriteCapped(buf, offset, text, MaxTextBytes);
        }

        static string ReadText(byte[] buf, int offset, int len)
        {
            try { return Encoding.UTF8.GetString(buf, offset, len); }
            catch { return string.Empty; }
        }

        public static void WriteU16(byte[] b, int o, int v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
        }

        public static int ReadU16(byte[] b, int o)
        {
            return b[o] | (b[o + 1] << 8);
        }

        public static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF);
            b[o + 3] = (byte)((v >> 24) & 0xFF);
        }

        public static uint ReadU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        public static void WriteI64(byte[] b, int o, long v)
        {
            for (int i = 0; i < 8; i++) b[o + i] = (byte)((v >> (i * 8)) & 0xFF);
        }

        public static long ReadI64(byte[] b, int o)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) v |= (long)b[o + i] << (i * 8);
            return v;
        }

        public static string ToHex(byte[] b, int len)
        {
            const string digits = "0123456789abcdef";
            char[] c = new char[len * 2];
            for (int i = 0; i < len; i++)
            {
                c[i * 2] = digits[(b[i] >> 4) & 0xF];
                c[i * 2 + 1] = digits[b[i] & 0xF];
            }
            return new string(c);
        }

        public static bool FromHex(string s, byte[] outBytes, int len)
        {
            if (string.IsNullOrEmpty(s) || s.Length < len * 2) return false;
            for (int i = 0; i < len; i++)
            {
                int hi = HexVal(s[i * 2]);
                int lo = HexVal(s[i * 2 + 1]);
                if (hi < 0 || lo < 0) return false;
                outBytes[i] = (byte)((hi << 4) | lo);
            }
            return true;
        }

        static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
