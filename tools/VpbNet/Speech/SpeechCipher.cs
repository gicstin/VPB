using System;
using System.Security.Cryptography;
using System.Text;

namespace VpbNet.Speech
{
    internal sealed class SpeechCipher : IDisposable
    {
        const byte Hello = 0x71;
        const byte Sealed = 0x72;
        const int SaltBytes = 32;
        const int HeaderBytes = 11;
        const int TagBytes = 16;
        const int HelloBytes = 2 + SaltBytes + TagBytes;
        static readonly byte[] KeyLabel = Encoding.ASCII.GetBytes("VPB speech AES-GCM v1");
        readonly byte[] _roomKey;
        readonly byte[] _localSalt = RandomNumberGenerator.GetBytes(SaltBytes);
        readonly byte[] _hello = new byte[HelloBytes];
        readonly byte[] _nonce = new byte[12];
        readonly byte[] _sealed = new byte[VpbIpc.MaxDataPayload];
        readonly byte[] _plain = new byte[VpbNetSpeech.MaxPlainBytes];
        byte[] _remoteSalt;
        AesGcm _send;
        AesGcm _receive;
        readonly ulong[] _sequence = new ulong[2];
        readonly ulong[] _highest = new ulong[2];
        readonly ulong[] _seen = new ulong[2];
        long _nextHello;
        bool _confirmed;
        long _lastConfirmed;

        public SpeechCipher(byte[] roomKey)
        {
            if (roomKey == null || roomKey.Length != 32) throw new ArgumentException("Speech requires a room key.");
            _roomKey = (byte[])roomKey.Clone();
            _hello[0] = Hello;
            _hello[1] = VpbNetSpeech.Version;
            Buffer.BlockCopy(_localSalt, 0, _hello, 2, SaltBytes);
            byte[] mac = HMACSHA256.HashData(_roomKey, _hello.AsSpan(0, 2 + SaltBytes));
            Buffer.BlockCopy(mac, 0, _hello, 2 + SaltBytes, TagBytes);
            CryptographicOperations.ZeroMemory(mac);
        }

        public bool Ready { get { return _confirmed && Environment.TickCount64 - _lastConfirmed < 3000; } }

        public void Tick(long now, Action<byte[], int> send)
        {
            if (now < _nextHello) return;
            _nextHello = now + 1000;
            send(_hello, _hello.Length);
            if (_send != null)
            {
                _plain[0] = 0;
                int count = Encrypt(_plain, 1);
                if (count > 0) send(_sealed, count);
            }
        }

        public bool Send(byte[] data, int length, Action<byte[], int> send)
        {
            if (!Ready || data == null || length < 1 || length > VpbNetSpeech.MaxPlainBytes || length > data.Length) return false;
            int count = Encrypt(data, length);
            if (count == 0) return false;
            send(_sealed, count);
            return true;
        }

        int Encrypt(byte[] data, int length)
        {
            int lane = data[0] == VpbNetSpeech.Caption ? 1 : 0;
            if (_send == null || _sequence[lane] == ulong.MaxValue) return 0;
            _sealed[0] = Sealed;
            _sealed[1] = VpbNetSpeech.Version;
            _sealed[2] = (byte)lane;
            ulong sequence = ++_sequence[lane];
            WriteSequence(_sealed, 3, sequence);
            Array.Clear(_nonce, 0, _nonce.Length);
            _nonce[0] = (byte)lane;
            WriteSequence(_nonce, 4, sequence);
            _send.Encrypt(_nonce, data.AsSpan(0, length), _sealed.AsSpan(HeaderBytes, length),
                _sealed.AsSpan(HeaderBytes + length, TagBytes), _sealed.AsSpan(0, HeaderBytes));
            return HeaderBytes + length + TagBytes;
        }

        public bool Receive(byte[] data, int length, out byte[] plain, out int plainLength)
        {
            plain = null;
            plainLength = 0;
            if (data == null || length < 2 || length > data.Length || data[1] != VpbNetSpeech.Version) return false;
            if (data[0] == Hello)
            {
                AcceptHello(data, length);
                return false;
            }
            if (data[0] != Sealed || _receive == null || length < HeaderBytes + TagBytes + 1 || length > VpbIpc.MaxDataPayload) return false;
            int count = length - HeaderBytes - TagBytes;
            if (count > _plain.Length) return false;
            int lane = data[2];
            if (lane > 1) return false;
            ulong sequence = ReadSequence(data, 3);
            if (sequence == 0) return false;
            if (sequence <= _highest[lane] && (_highest[lane] - sequence >= 64 || (_seen[lane] & (1UL << (int)(_highest[lane] - sequence))) != 0)) return false;
            Array.Clear(_nonce, 0, _nonce.Length);
            _nonce[0] = (byte)lane;
            WriteSequence(_nonce, 4, sequence);
            try
            {
                _receive.Decrypt(_nonce, data.AsSpan(HeaderBytes, count), data.AsSpan(HeaderBytes + count, TagBytes),
                    _plain.AsSpan(0, count), data.AsSpan(0, HeaderBytes));
            }
            catch (CryptographicException) { return false; }
            if ((_plain[0] == VpbNetSpeech.Caption ? 1 : 0) != lane) return false;
            if (sequence > _highest[lane])
            {
                ulong advance = sequence - _highest[lane];
                _seen[lane] = advance >= 64 ? 1 : (_seen[lane] << (int)advance) | 1;
                _highest[lane] = sequence;
            }
            else _seen[lane] |= 1UL << (int)(_highest[lane] - sequence);
            _confirmed = true;
            _lastConfirmed = Environment.TickCount64;
            if (_plain[0] == 0) return false;
            plain = _plain;
            plainLength = count;
            return true;
        }

        void AcceptHello(byte[] data, int length)
        {
            if (length != HelloBytes || _remoteSalt != null) return;
            byte[] expected = HMACSHA256.HashData(_roomKey, data.AsSpan(0, 2 + SaltBytes));
            bool valid = CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, TagBytes), data.AsSpan(2 + SaltBytes, TagBytes));
            CryptographicOperations.ZeroMemory(expected);
            if (!valid || CryptographicOperations.FixedTimeEquals(_localSalt, data.AsSpan(2, SaltBytes))) return;
            _remoteSalt = data.AsSpan(2, SaltBytes).ToArray();
            byte[] sendKey = Derive(_localSalt, _remoteSalt);
            byte[] receiveKey = Derive(_remoteSalt, _localSalt);
            try
            {
                _send = new AesGcm(sendKey, TagBytes);
                _receive = new AesGcm(receiveKey, TagBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sendKey);
                CryptographicOperations.ZeroMemory(receiveKey);
            }
            _nextHello = 0;
        }

        byte[] Derive(byte[] sender, byte[] receiver)
        {
            byte[] context = new byte[KeyLabel.Length + SaltBytes * 2];
            Buffer.BlockCopy(KeyLabel, 0, context, 0, KeyLabel.Length);
            Buffer.BlockCopy(sender, 0, context, KeyLabel.Length, SaltBytes);
            Buffer.BlockCopy(receiver, 0, context, KeyLabel.Length + SaltBytes, SaltBytes);
            return HMACSHA256.HashData(_roomKey, context);
        }

        static void WriteSequence(byte[] data, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++) data[offset + i] = (byte)(value >> (i * 8));
        }

        static ulong ReadSequence(byte[] data, int offset)
        {
            ulong value = 0;
            for (int i = 0; i < 8; i++) value |= (ulong)data[offset + i] << (i * 8);
            return value;
        }

        public void Dispose()
        {
            if (_send != null) _send.Dispose();
            if (_receive != null) _receive.Dispose();
            _send = null;
            _receive = null;
            _confirmed = false;
            CryptographicOperations.ZeroMemory(_roomKey);
            CryptographicOperations.ZeroMemory(_plain);
            CryptographicOperations.ZeroMemory(_sealed);
        }
    }
}
