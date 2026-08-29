using System;
using System.Security.Cryptography;
using System.Text;

namespace VpbNet.Transport
{
    public static class SessionAuth
    {
        public const int KeyBytes = 32;
        public const int TagBytes = 8;

        const int Pbkdf2Iterations = 210000;

        static readonly byte[] Pbkdf2Salt = Encoding.UTF8.GetBytes("VPB-net-room-v2");
        static readonly byte[] SessionLabel = Encoding.UTF8.GetBytes("vpb/session-key");
        static readonly byte[] LobbyLabel = Encoding.UTF8.GetBytes("vpb/lobby-token/2");
        static readonly byte[] UnkeyedFallback = Encoding.UTF8.GetBytes("vpb-lan-unkeyed");

        public sealed class RoomKeys
        {
            public byte[] SessionKey;

            public byte[] LobbyToken;

            public string LobbyTokenHex;
        }

        public static RoomKeys Derive(string roomCode)
        {
            byte[] master = Stretch(roomCode);
            try
            {
                RoomKeys k = new RoomKeys();
                k.SessionKey = SubKey(master, SessionLabel);
                k.LobbyToken = SubKey(master, LobbyLabel);
                k.LobbyTokenHex = ToHex(k.LobbyToken, 16);
                return k;
            }
            finally
            {
                Array.Clear(master, 0, master.Length);
            }
        }

        public static string DeriveLobbyTokenHex(string roomCode)
        {
            byte[] master = Stretch(roomCode);
            try
            {
                byte[] token = SubKey(master, LobbyLabel);
                return ToHex(token, 16);
            }
            finally
            {
                Array.Clear(master, 0, master.Length);
            }
        }

        static byte[] Stretch(string roomCode)
        {
            byte[] code = Encoding.UTF8.GetBytes(VpbNetRoomCode.Canonical(roomCode));
            using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(code, Pbkdf2Salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
            {
                return kdf.GetBytes(KeyBytes);
            }
        }

        static byte[] SubKey(byte[] master, byte[] label)
        {
            using (HMACSHA256 mac = new HMACSHA256(master))
            {
                return mac.ComputeHash(label);
            }
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

        public static byte[] KeyOrFallback(byte[] sessionKey)
        {
            if (sessionKey != null && sessionKey.Length > 0) return sessionKey;
            return UnkeyedFallback;
        }
    }

    public sealed class SessionMac : IDisposable
    {
        readonly HMACSHA256 _mac;
        readonly byte[] _scratch = new byte[32];

        public SessionMac(byte[] sessionKey)
        {
            _mac = new HMACSHA256(SessionAuth.KeyOrFallback(sessionKey));
        }

        public int TagSize { get { return SessionAuth.TagBytes; } }

        public void Sign(byte[] buf, int len)
        {
            if (buf == null || len < 0 || len + SessionAuth.TagBytes > buf.Length) return;
            int written;
            _mac.TryComputeHash(new ReadOnlySpan<byte>(buf, 0, len), _scratch, out written);
            Buffer.BlockCopy(_scratch, 0, buf, len, SessionAuth.TagBytes);
        }

        public bool Verify(byte[] buf, int len)
        {
            if (buf == null || len < 0 || len + SessionAuth.TagBytes > buf.Length) return false;
            int written;
            if (!_mac.TryComputeHash(new ReadOnlySpan<byte>(buf, 0, len), _scratch, out written)) return false;

            return CryptographicOperations.FixedTimeEquals(
                new ReadOnlySpan<byte>(_scratch, 0, SessionAuth.TagBytes),
                new ReadOnlySpan<byte>(buf, len, SessionAuth.TagBytes));
        }

        public void Dispose()
        {
            try { _mac.Dispose(); }
            catch { }
        }
    }
}
