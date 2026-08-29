using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetEventType
    {
        public const byte Join = 1;
        public const byte Leave = 2;
        public const byte Clothing = 4;
        public const byte Morphs = 5;
        public const byte Expression = 6;
        public const byte Chat = 7;
        public const byte Kick = 8;
        public const byte Trigger = 9;
        public const byte AtomAdd = 10;
        public const byte AtomRemove = 11;
        public const byte AvatarClaim = 12;
        public const byte SceneState = 13;

        // AtomAdd is create-only — later loads need their own message.
        public const byte SubSceneRef = 14;

        // Full list every time, not a delta.
        public const byte Hair = 15;

        // Look lives in the preset file — send the path, not materials.
        public const byte PresetApply = 16;

        // Carry placement explicitly — two machines deriving it independently drift a metre.
        public const byte DualPose = 17;

        // Position is not settings — carry named storable values so lamps match.
        public const byte AtomParam = 18;

        // Sent before the silence it describes — VaM load blocks the thread (looks like death).
        public const byte Busy = 19;

        // Published not negotiated; own message so it can change mid-session and re-send on Join.
        public const byte Rules = 20;

        // Names a scene the peer may not own yet — SceneState is too late to fetch.
        public const byte SceneOffer = 21;

        // Both sides, while fetch is in flight — so a stall is not mistaken for a dead peer.
        public const byte ContentState = 22;

        // Host only — someone has to decide.
        public const byte SceneGo = 23;
    }

    // Selector not address — wire never names which atom.
    public static class VpbNetEventTarget
    {
        public const byte Self = 0;
        public const byte Peer = 1;

        public static bool IsKnown(byte t)
        {
            return t == Self || t == Peer;
        }
    }

    // Code not sentence — unknown degrades to generic phrase, not refused.
    public static class VpbNetBusyKind
    {
        public const byte Content = 0;
        public const byte Appearance = 1;
        public const byte Pose = 2;
        public const byte Skin = 3;
        public const byte Clothing = 4;
        public const byte Hair = 5;
        public const byte Morphs = 6;
        public const byte DualPose = 7;
        public const byte Scene = 8;

        public static string Describe(byte kind)
        {
            switch (kind)
            {
                case Appearance: return "loading a look";
                case Pose: return "loading a pose";
                case Skin: return "loading a skin";
                case Clothing: return "loading clothing";
                case Hair: return "loading hair";
                case Morphs: return "loading morphs";
                case DualPose: return "loading a two-person pose";
                case Scene: return "loading a scene";
            }
            return "loading content";
        }
    }

    public static class VpbNetEventPack
    {
        public const int MaxItemsPerEvent = 8;

        public static int ItemBytes(string id, int valueBytes)
        {
            if (id == null) return 1 + valueBytes;
            int n = id.Length * 3;
            return 1 + n + valueBytes;
        }

        public static bool Fits(int usedBytes, string id, int valueBytes)
        {
            return usedBytes + ItemBytes(id, valueBytes) <= VpbNetEventLimits.MaxPayload;
        }
    }

    public static class VpbNetEventLimits
    {
        public const int MaxPayload = 512;
        public const int MaxChat = 200;
        public const int MaxIdentifier = 160;

        // 240 not 160 — real library paths (uid/folder/file) were refused.
        public const int MaxEntryPath = 240;
        public const int MaxPresetAction = 24;
        public const int MaxClothingItems = 32;
        public const int MaxHairItems = 8;
        public const int MaxMorphs = 48;
        public const int MaxTriggersPerEvent = 4;
        public const int MaxTriggerQueue = 16;
        public const int MaxAtomsPerEvent = 2;
        public const int MaxSubScenesPerEvent = 2;
        public const int MaxParamsPerEvent = 3;
        public const int MaxEventsPerSecond = 20;
        public const int MaxQueueDepth = 16;
        public const float MorphRange = 4f;
    }

    public enum VpbNetEventReject
    {
        None = 0,
        BadVersion = 1,
        Truncated = 2,
        Oversize = 3,
        BadString = 4,
        BadIdentifier = 5,
        PluginReference = 6,
        CountCap = 7,
        UnknownType = 8,
        Duplicate = 9,
        RateLimited = 10,
        QueueFull = 11
    }

    public static class VpbNetEventCodec
    {
        // v2 target is a leading byte — trailing would let v1 silently parse as v2.
        public const byte ProtoVersion = 2;
        public const int HeaderSize = 8;

        public static bool IsSafeText(string s, int maxLength)
        {
            if (s == null) return false;
            if (s.Length > maxLength) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x20 || c == 0x7F) return false;
            }
            return true;
        }

        public static bool IsSafeIdentifier(string s)
        {
            return IsSafeIdentifier(s, VpbNetEventLimits.MaxIdentifier);
        }

        public static bool IsSafeIdentifier(string s, int maxLength)
        {
            if (s == null) return false;
            if (s.Length == 0 || s.Length > maxLength) return false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x20 || c == 0x7F) return false;
                if (c == '\\') return false;
                if (c == '?' || c == '*' || c == '"' || c == '<' || c == '>' || c == '|') return false;
            }

            char last = s[s.Length - 1];
            if (last == '.' || last == ' ') return false;

            if (s[0] == '/') return false;
            if (s.Length >= 3 && s[1] == ':' && s[2] == '/' && IsAsciiLetter(s[0])) return false;
            if (IndexOfOrdinal(s, "..") >= 0) return false;
            if (IndexOfOrdinal(s, "//") >= 0) return false;

            return true;
        }

        public static bool IsPluginReference(string s)
        {
            if (s == null) return false;

            int end = s.Length;
            while (end > 0 && (s[end - 1] == '.' || s[end - 1] == ' ')) end--;
            if (end != s.Length) s = s.Substring(0, end);

            return EndsWithOrdinalIgnoreCase(s, ".cs")
                || EndsWithOrdinalIgnoreCase(s, ".cslist")
                || EndsWithOrdinalIgnoreCase(s, ".dll")
                || EndsWithOrdinalIgnoreCase(s, ".dvar");
        }

        public static int QuantizeMorph(float v)
        {
            float t = v * (32767f / VpbNetEventLimits.MorphRange);
            if (t > 32767f) t = 32767f;
            else if (t < -32767f) t = -32767f;
            return t >= 0f ? (int)(t + 0.5f) : (int)(t - 0.5f);
        }

        public static float DequantizeMorph(int q)
        {
            return q * (VpbNetEventLimits.MorphRange / 32767f);
        }

        static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        static int IndexOfOrdinal(string s, string needle)
        {
            return s.IndexOf(needle, StringComparison.Ordinal);
        }

        static bool EndsWithOrdinalIgnoreCase(string s, string suffix)
        {
            if (s.Length < suffix.Length) return false;
            int off = s.Length - suffix.Length;
            for (int i = 0; i < suffix.Length; i++)
            {
                char a = s[off + i];
                char b = suffix[i];
                if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
                if (b >= 'A' && b <= 'Z') b = (char)(b + 32);
                if (a != b) return false;
            }
            return true;
        }
    }

    public static class VpbNetEventFloat
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        struct Bits
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public float F;
            [System.Runtime.InteropServices.FieldOffset(0)] public uint U;
        }

        public static uint ToBits(float v)
        {
            Bits b = new Bits();
            b.F = v;
            return b.U;
        }

        public static float FromBits(uint v)
        {
            Bits b = new Bits();
            b.U = v;
            return b.F;
        }
    }

    public sealed class VpbNetEventWriter
    {
        readonly byte[] _buf;
        int _at;
        int _start;
        bool _failed;

        public VpbNetEventWriter(int capacity)
        {
            _buf = new byte[capacity < 64 ? 64 : capacity];
        }

        public byte[] Buffer { get { return _buf; } }
        public int Length { get { return _at; } }
        public bool Failed { get { return _failed; } }

        public void Begin(byte type, uint seq)
        {
            _start = 0;
            _at = 0;
            _failed = false;
            if (_buf.Length < VpbNetEventCodec.HeaderSize)
            {
                _failed = true;
                return;
            }
            _buf[0] = VpbNetEventCodec.ProtoVersion;
            _buf[1] = type;
            VpbIpc.WriteU16(_buf, 2, 0);
            VpbIpc.WriteU32(_buf, 4, seq);
            _at = VpbNetEventCodec.HeaderSize;
        }

        public void WriteByte(byte v)
        {
            if (_failed || _at + 1 > _buf.Length) { _failed = true; return; }
            _buf[_at] = v;
            _at++;
        }

        public void WriteU16(int v)
        {
            if (_failed || _at + 2 > _buf.Length) { _failed = true; return; }
            VpbIpc.WriteU16(_buf, _at, v);
            _at += 2;
        }

        public void WriteI16(int v)
        {
            WriteU16(v & 0xFFFF);
        }

        public void WriteU32(uint v)
        {
            if (_failed || _at + 4 > _buf.Length) { _failed = true; return; }
            VpbIpc.WriteU32(_buf, _at, v);
            _at += 4;
        }

        public void WriteF32(float v)
        {
            WriteU32(VpbNetEventFloat.ToBits(v));
        }

        public void WriteString(string s, int maxLength)
        {
            if (_failed) return;
            if (s == null) s = string.Empty;
            if (s.Length > maxLength) { _failed = true; return; }

            int n;
            try { n = Encoding.UTF8.GetByteCount(s); }
            catch { _failed = true; return; }

            if (n > 255 || _at + 1 + n > _buf.Length) { _failed = true; return; }
            _buf[_at] = (byte)n;
            _at++;
            if (n > 0)
            {
                try { Encoding.UTF8.GetBytes(s, 0, s.Length, _buf, _at); }
                catch { _failed = true; return; }
                _at += n;
            }
        }

        public int End()
        {
            if (_failed) return -1;
            int payload = _at - VpbNetEventCodec.HeaderSize;
            if (payload < 0 || payload > VpbNetEventLimits.MaxPayload) return -1;
            VpbIpc.WriteU16(_buf, _start + 2, payload);
            return _at;
        }
    }

    public sealed class VpbNetEventReader
    {
        byte[] _buf;
        int _at;
        int _end;
        bool _failed;

        public byte Type { get; private set; }
        public uint Seq { get; private set; }
        public VpbNetEventReject Reject { get; private set; }
        public bool Failed { get { return _failed; } }
        public int Remaining { get { return _end - _at; } }

        public bool Begin(byte[] buf, int offset, int len)
        {
            _buf = buf;
            _failed = false;
            Reject = VpbNetEventReject.None;
            Type = 0;
            Seq = 0;

            if (buf == null || offset < 0 || len < VpbNetEventCodec.HeaderSize
                || offset + len > buf.Length)
            {
                Reject = VpbNetEventReject.Truncated;
                _failed = true;
                return false;
            }

            if (buf[offset] != VpbNetEventCodec.ProtoVersion)
            {
                Reject = VpbNetEventReject.BadVersion;
                _failed = true;
                return false;
            }

            Type = buf[offset + 1];
            int payload = VpbIpc.ReadU16(buf, offset + 2);
            Seq = VpbIpc.ReadU32(buf, offset + 4);

            if (payload > VpbNetEventLimits.MaxPayload)
            {
                Reject = VpbNetEventReject.Oversize;
                _failed = true;
                return false;
            }

            if (VpbNetEventCodec.HeaderSize + payload != len)
            {
                Reject = VpbNetEventReject.Truncated;
                _failed = true;
                return false;
            }

            _at = offset + VpbNetEventCodec.HeaderSize;
            _end = _at + payload;
            return true;
        }

        public byte ReadByte()
        {
            if (_failed || _at + 1 > _end) { Fail(VpbNetEventReject.Truncated); return 0; }
            byte v = _buf[_at];
            _at++;
            return v;
        }

        public int ReadU16()
        {
            if (_failed || _at + 2 > _end) { Fail(VpbNetEventReject.Truncated); return 0; }
            int v = VpbIpc.ReadU16(_buf, _at);
            _at += 2;
            return v;
        }

        public int ReadI16()
        {
            return (short)ReadU16();
        }

        public uint ReadU32()
        {
            if (_failed || _at + 4 > _end) { Fail(VpbNetEventReject.Truncated); return 0; }
            uint v = VpbIpc.ReadU32(_buf, _at);
            _at += 4;
            return v;
        }

        public float ReadF32()
        {
            float v = VpbNetEventFloat.FromBits(ReadU32());
            if (_failed) return 0f;
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                Fail(VpbNetEventReject.BadString);
                return 0f;
            }
            return v;
        }

        public string ReadText(int maxLength)
        {
            string s = ReadRaw();
            if (s == null) return null;
            if (!VpbNetEventCodec.IsSafeText(s, maxLength))
            {
                Fail(VpbNetEventReject.BadString);
                return null;
            }
            return s;
        }

        public string ReadIdentifier()
        {
            return ReadIdentifier(VpbNetEventLimits.MaxIdentifier);
        }

        public string ReadIdentifier(int maxLength)
        {
            string s = ReadRaw();
            if (s == null) return null;
            if (!VpbNetEventCodec.IsSafeIdentifier(s, maxLength))
            {
                Fail(VpbNetEventReject.BadIdentifier);
                return null;
            }
            if (VpbNetEventCodec.IsPluginReference(s))
            {
                Fail(VpbNetEventReject.PluginReference);
                return null;
            }
            return s;
        }

        public int ReadCount(int cap)
        {
            int n = ReadByte();
            if (_failed) return 0;
            if (n > cap)
            {
                Fail(VpbNetEventReject.CountCap);
                return 0;
            }
            return n;
        }

        string ReadRaw()
        {
            if (_failed || _at + 1 > _end) { Fail(VpbNetEventReject.Truncated); return null; }
            int n = _buf[_at];
            _at++;
            if (_at + n > _end) { Fail(VpbNetEventReject.Truncated); return null; }
            string s;
            try { s = Encoding.UTF8.GetString(_buf, _at, n); }
            catch { Fail(VpbNetEventReject.BadString); return null; }
            _at += n;
            return s;
        }

        void Fail(VpbNetEventReject why)
        {
            if (_failed) return;
            _failed = true;
            Reject = why;
        }
    }

    public sealed class VpbNetEventQueue
    {
        readonly byte[][] _slots;
        readonly int[] _lengths;
        readonly uint[] _seqs;
        readonly bool[] _used;

        uint _nextSeq = 1;
        int _held;
        int _accepted;
        int _released;
        int _duplicates;
        int _rateLimited;
        int _queueFull;
        int _rateCount;
        double _rateWindowStart;
        bool _started;

        public VpbNetEventQueue(int slotBytes)
        {
            int cap = VpbNetEventLimits.MaxQueueDepth;
            _slots = new byte[cap][];
            _lengths = new int[cap];
            _seqs = new uint[cap];
            _used = new bool[cap];
            for (int i = 0; i < cap; i++) _slots[i] = new byte[slotBytes];
        }

        public uint NextSeq { get { return _nextSeq; } }
        public int Held { get { return _held; } }
        public int Accepted { get { return _accepted; } }
        public int Released { get { return _released; } }
        public int Duplicates { get { return _duplicates; } }
        public int RateLimited { get { return _rateLimited; } }
        public int QueueFull { get { return _queueFull; } }

        public void Reset()
        {
            _nextSeq = 1;
            _held = 0;
            _accepted = 0;
            _released = 0;
            _duplicates = 0;
            _rateLimited = 0;
            _queueFull = 0;
            _rateCount = 0;
            _started = false;
            for (int i = 0; i < _used.Length; i++) _used[i] = false;
        }

        public VpbNetEventReject Offer(byte[] buf, int offset, int len, uint seq, double nowMs)
        {
            if (!_started)
            {
                _started = true;
                _rateWindowStart = nowMs;
                _rateCount = 0;
            }

            if (nowMs - _rateWindowStart >= 1000.0)
            {
                _rateWindowStart = nowMs;
                _rateCount = 0;
            }
            _rateCount++;
            if (_rateCount > VpbNetEventLimits.MaxEventsPerSecond)
            {
                _rateLimited++;
                return VpbNetEventReject.RateLimited;
            }

            if (seq < _nextSeq)
            {
                _duplicates++;
                return VpbNetEventReject.Duplicate;
            }

            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i] && _seqs[i] == seq)
                {
                    _duplicates++;
                    return VpbNetEventReject.Duplicate;
                }
            }

            int slot = -1;
            for (int i = 0; i < _used.Length; i++)
            {
                if (_used[i]) continue;
                slot = i;
                break;
            }
            if (slot < 0)
            {
                _queueFull++;
                return VpbNetEventReject.QueueFull;
            }

            if (len > _slots[slot].Length)
            {
                _queueFull++;
                return VpbNetEventReject.Oversize;
            }

            System.Buffer.BlockCopy(buf, offset, _slots[slot], 0, len);
            _lengths[slot] = len;
            _seqs[slot] = seq;
            _used[slot] = true;
            _held++;
            _accepted++;
            return VpbNetEventReject.None;
        }

        public int TryRelease(byte[] dst, out uint seq)
        {
            seq = 0;
            for (int i = 0; i < _used.Length; i++)
            {
                if (!_used[i] || _seqs[i] != _nextSeq) continue;

                int len = _lengths[i];
                if (len > dst.Length) len = dst.Length;
                System.Buffer.BlockCopy(_slots[i], 0, dst, 0, len);
                seq = _seqs[i];
                _used[i] = false;
                _held--;
                _released++;
                _nextSeq++;
                return len;
            }
            return 0;
        }
    }
}
