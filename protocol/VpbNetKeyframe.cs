using System;
using System.Text;

namespace VpbNet
{
    public enum VpbNetKeyframeReject
    {
        None = 0,
        BadVersion = 1,
        Truncated = 2,
        BadIndex = 3,
        BadCount = 4,
        BadStride = 5,
        Oversize = 6,
        Duplicate = 7,
        Stale = 8,
        Incomplete = 9,
        BadPayload = 10
    }

    public sealed class VpbNetPeerState
    {
        public const byte ProtoVersion = 2;
        public const int HeaderSize = 12;

        public const byte FlagHavePose = 1 << 0;
        public const byte FlagClothingAuthoritative = 1 << 1;

        readonly string[] _clothingId = new string[VpbNetEventLimits.MaxClothingItems];
        readonly bool[] _clothingOn = new bool[VpbNetEventLimits.MaxClothingItems];
        readonly string[] _morphId = new string[VpbNetEventLimits.MaxMorphs];
        readonly float[] _morphValue = new float[VpbNetEventLimits.MaxMorphs];
        readonly byte[] _pose = new byte[VpbPose.MaxFrameBytes];

        int _poseLen;
        bool _clothingAuthoritative;
        int _clothingCount;
        int _morphCount;
        string _expression = string.Empty;
        ushort _peerId;
        uint _eventSeq;
        bool _havePose;

        public int ClothingCount { get { return _clothingCount; } }
        public int MorphCount { get { return _morphCount; } }
        public string Expression { get { return _expression; } }
        public ushort PeerId { get { return _peerId; } set { _peerId = value; } }
        public uint EventSeq { get { return _eventSeq; } }
        public bool HavePose { get { return _havePose; } }

        public bool ClothingAuthoritative
        {
            get { return _clothingAuthoritative; }
            set { _clothingAuthoritative = value; }
        }

        public string ClothingId(int i) { return i >= 0 && i < _clothingCount ? _clothingId[i] : null; }
        public bool ClothingOn(int i) { return i >= 0 && i < _clothingCount && _clothingOn[i]; }
        public string MorphId(int i) { return i >= 0 && i < _morphCount ? _morphId[i] : null; }
        public float MorphValue(int i) { return i >= 0 && i < _morphCount ? _morphValue[i] : 0f; }

        public void ClearClothing()
        {
            _clothingCount = 0;
            _clothingAuthoritative = false;
        }

        public void Clear()
        {
            _clothingCount = 0;
            _clothingAuthoritative = false;
            _morphCount = 0;
            _expression = string.Empty;
            _eventSeq = 0;
            _havePose = false;
            _poseLen = 0;
        }

        public void NoteEventSeq(uint seq)
        {
            if (seq > _eventSeq) _eventSeq = seq;
        }

        public bool IsStaleEvent(uint seq)
        {
            return seq <= _eventSeq;
        }

        public void SetPose(byte[] frame, int offset, int len)
        {
            if (frame == null || !VpbPose.IsValidFrameLength(len)) return;
            if (offset < 0 || offset + len > frame.Length) return;
            Buffer.BlockCopy(frame, offset, _pose, 0, len);
            _poseLen = len;
            _havePose = true;
        }

        public void SetExpression(string s, uint seq)
        {
            if (!VpbNetEventCodec.IsSafeIdentifier(s) || VpbNetEventCodec.IsPluginReference(s)) return;
            _expression = s;
            NoteEventSeq(seq);
        }

        public bool SetClothing(string id, bool on, uint seq)
        {
            if (!VpbNetEventCodec.IsSafeIdentifier(id) || VpbNetEventCodec.IsPluginReference(id)) return false;

            for (int i = 0; i < _clothingCount; i++)
            {
                if (!string.Equals(_clothingId[i], id, StringComparison.Ordinal)) continue;
                _clothingOn[i] = on;
                NoteEventSeq(seq);
                return true;
            }

            if (_clothingCount >= VpbNetEventLimits.MaxClothingItems) return false;
            _clothingId[_clothingCount] = id;
            _clothingOn[_clothingCount] = on;
            _clothingCount++;
            NoteEventSeq(seq);
            return true;
        }

        public bool SetMorph(string id, float value, uint seq)
        {
            if (!VpbNetEventCodec.IsSafeIdentifier(id) || VpbNetEventCodec.IsPluginReference(id)) return false;

            for (int i = 0; i < _morphCount; i++)
            {
                if (!string.Equals(_morphId[i], id, StringComparison.Ordinal)) continue;
                _morphValue[i] = value;
                NoteEventSeq(seq);
                return true;
            }

            if (_morphCount >= VpbNetEventLimits.MaxMorphs) return false;
            _morphId[_morphCount] = id;
            _morphValue[_morphCount] = value;
            _morphCount++;
            NoteEventSeq(seq);
            return true;
        }

        public int Write(byte[] dst)
        {
            if (dst == null || dst.Length < HeaderSize) return -1;

            int o = 0;
            dst[o] = ProtoVersion;
            dst[o + 1] = (byte)((_havePose ? FlagHavePose : 0)
                | (_clothingAuthoritative ? FlagClothingAuthoritative : 0));
            VpbIpc.WriteU16(dst, o + 2, _peerId);
            VpbIpc.WriteU32(dst, o + 4, _eventSeq);
            VpbIpc.WriteU16(dst, o + 8, _havePose ? _poseLen : 0);
            VpbIpc.WriteU16(dst, o + 10, 0);
            o = HeaderSize;

            if (_havePose)
            {
                if (o + _poseLen > dst.Length) return -1;
                Buffer.BlockCopy(_pose, 0, dst, o, _poseLen);
                o += _poseLen;
            }

            o = WriteString(dst, o, _expression);
            if (o < 0) return -1;

            if (o + 1 > dst.Length) return -1;
            dst[o] = (byte)_clothingCount;
            o++;
            for (int i = 0; i < _clothingCount; i++)
            {
                o = WriteString(dst, o, _clothingId[i]);
                if (o < 0 || o + 1 > dst.Length) return -1;
                dst[o] = (byte)(_clothingOn[i] ? 1 : 0);
                o++;
            }

            if (o + 1 > dst.Length) return -1;
            dst[o] = (byte)_morphCount;
            o++;
            for (int i = 0; i < _morphCount; i++)
            {
                o = WriteString(dst, o, _morphId[i]);
                if (o < 0 || o + 2 > dst.Length) return -1;
                VpbIpc.WriteU16(dst, o, VpbNetEventCodec.QuantizeMorph(_morphValue[i]) & 0xFFFF);
                o += 2;
            }

            return o;
        }

        public static bool TryPeekEventSeq(byte[] src, int offset, int len, out uint seq)
        {
            seq = 0;
            if (src == null || len < HeaderSize || offset < 0 || offset + len > src.Length) return false;
            if (src[offset] != ProtoVersion) return false;
            seq = VpbIpc.ReadU32(src, offset + 4);
            return true;
        }

        public VpbNetKeyframeReject Read(byte[] src, int offset, int len)
        {
            if (src == null || len < HeaderSize || offset < 0 || offset + len > src.Length)
                return VpbNetKeyframeReject.Truncated;
            if (src[offset] != ProtoVersion) return VpbNetKeyframeReject.BadVersion;

            int end = offset + len;
            int o = offset;
            byte flags = src[o + 1];
            bool havePose = (flags & FlagHavePose) != 0;
            bool authoritative = (flags & FlagClothingAuthoritative) != 0;
            ushort peer = (ushort)VpbIpc.ReadU16(src, o + 2);
            uint eventSeq = VpbIpc.ReadU32(src, o + 4);
            int poseLen = VpbIpc.ReadU16(src, o + 8);
            o += HeaderSize;

            if (havePose != (poseLen > 0)) return VpbNetKeyframeReject.BadPayload;
            if (havePose && !VpbPose.IsValidFrameLength(poseLen)) return VpbNetKeyframeReject.BadPayload;
            if (havePose && o + poseLen > end) return VpbNetKeyframeReject.Truncated;

            byte[] posePending = null;
            int poseAt = o;
            if (havePose)
            {
                posePending = src;
                o += poseLen;
            }

            string expression;
            o = ReadString(src, o, end, out expression);
            if (o < 0) return VpbNetKeyframeReject.Truncated;
            if (expression.Length > 0
                && (!VpbNetEventCodec.IsSafeIdentifier(expression) || VpbNetEventCodec.IsPluginReference(expression)))
                return VpbNetKeyframeReject.BadPayload;

            if (o + 1 > end) return VpbNetKeyframeReject.Truncated;
            int clothing = src[o];
            o++;
            if (clothing > VpbNetEventLimits.MaxClothingItems) return VpbNetKeyframeReject.BadCount;

            string[] cid = new string[clothing];
            bool[] con = new bool[clothing];
            for (int i = 0; i < clothing; i++)
            {
                o = ReadString(src, o, end, out cid[i]);
                if (o < 0) return VpbNetKeyframeReject.Truncated;
                if (!VpbNetEventCodec.IsSafeIdentifier(cid[i]) || VpbNetEventCodec.IsPluginReference(cid[i]))
                    return VpbNetKeyframeReject.BadPayload;
                if (o + 1 > end) return VpbNetKeyframeReject.Truncated;
                con[i] = src[o] != 0;
                o++;
            }

            if (o + 1 > end) return VpbNetKeyframeReject.Truncated;
            int morphs = src[o];
            o++;
            if (morphs > VpbNetEventLimits.MaxMorphs) return VpbNetKeyframeReject.BadCount;

            string[] mid = new string[morphs];
            float[] mval = new float[morphs];
            for (int i = 0; i < morphs; i++)
            {
                o = ReadString(src, o, end, out mid[i]);
                if (o < 0) return VpbNetKeyframeReject.Truncated;
                if (!VpbNetEventCodec.IsSafeIdentifier(mid[i]) || VpbNetEventCodec.IsPluginReference(mid[i]))
                    return VpbNetKeyframeReject.BadPayload;
                if (o + 2 > end) return VpbNetKeyframeReject.Truncated;
                mval[i] = VpbNetEventCodec.DequantizeMorph((short)VpbIpc.ReadU16(src, o));
                o += 2;
            }

            Clear();
            _peerId = peer;
            _eventSeq = eventSeq;
            _clothingAuthoritative = authoritative;
            if (havePose) SetPose(posePending, poseAt, poseLen);
            _expression = expression;
            for (int i = 0; i < clothing; i++)
            {
                _clothingId[i] = cid[i];
                _clothingOn[i] = con[i];
            }
            _clothingCount = clothing;
            for (int i = 0; i < morphs; i++)
            {
                _morphId[i] = mid[i];
                _morphValue[i] = mval[i];
            }
            _morphCount = morphs;

            return VpbNetKeyframeReject.None;
        }

        public int CopyPose(byte[] dst)
        {
            if (!_havePose || dst == null || dst.Length < _poseLen) return 0;
            Buffer.BlockCopy(_pose, 0, dst, 0, _poseLen);
            return _poseLen;
        }

        static int WriteString(byte[] dst, int o, string s)
        {
            if (s == null) s = string.Empty;
            int n;
            try { n = Encoding.UTF8.GetByteCount(s); }
            catch { return -1; }
            if (n > 255 || o + 1 + n > dst.Length) return -1;
            dst[o] = (byte)n;
            o++;
            if (n > 0)
            {
                try { Encoding.UTF8.GetBytes(s, 0, s.Length, dst, o); }
                catch { return -1; }
                o += n;
            }
            return o;
        }

        static int ReadString(byte[] src, int o, int end, out string s)
        {
            s = string.Empty;
            if (o + 1 > end) return -1;
            int n = src[o];
            o++;
            if (o + n > end) return -1;
            try { s = Encoding.UTF8.GetString(src, o, n); }
            catch { return -1; }
            return o + n;
        }
    }

    public sealed class VpbNetKeyframeAssembler
    {
        public const byte ProtoVersion = 1;
        public const int FragmentHeader = 8;
        public const int FragmentPayload = VpbIpc.MaxDataPayload - FragmentHeader;
        public const int MaxFragments = 24;
        public const int MaxKeyframeBytes = FragmentPayload * MaxFragments;
        public const double ReassemblyTimeoutMs = 5000.0;

        readonly byte[] _buf = new byte[MaxKeyframeBytes];
        readonly bool[] _got = new bool[MaxFragments];

        int _gen = -1;
        int _count;
        int _have;
        int _total;
        double _startedMs;
        bool _active;

        int _completed;
        int _superseded;
        int _timedOut;
        int _duplicates;
        int _rejected;

        public bool Active { get { return _active; } }
        public bool IsComplete { get { return _active && _count > 0 && _have == _count; } }
        public int Generation { get { return _gen; } }
        public int Completed { get { return _completed; } }
        public int Superseded { get { return _superseded; } }
        public int TimedOut { get { return _timedOut; } }
        public int Duplicates { get { return _duplicates; } }
        public int Rejected { get { return _rejected; } }

        public static int FragmentCount(int totalLen)
        {
            if (totalLen <= 0) return 0;
            return (totalLen + FragmentPayload - 1) / FragmentPayload;
        }

        public static int WriteFragment(byte[] dst, byte[] whole, int wholeLen, int gen, int index)
        {
            if (dst == null || whole == null) return -1;
            if (wholeLen <= 0 || wholeLen > MaxKeyframeBytes) return -1;

            int count = FragmentCount(wholeLen);
            if (count > MaxFragments || index < 0 || index >= count) return -1;

            int start = index * FragmentPayload;
            int len = wholeLen - start;
            if (len > FragmentPayload) len = FragmentPayload;
            if (dst.Length < FragmentHeader + len) return -1;

            dst[0] = ProtoVersion;
            dst[1] = 0;
            VpbIpc.WriteU16(dst, 2, gen & 0xFFFF);
            dst[4] = (byte)index;
            dst[5] = (byte)count;
            VpbIpc.WriteU16(dst, 6, len);
            Buffer.BlockCopy(whole, start, dst, FragmentHeader, len);
            return FragmentHeader + len;
        }

        public void Reset()
        {
            _active = false;
            _gen = -1;
            _count = 0;
            _have = 0;
            _total = 0;
            for (int i = 0; i < MaxFragments; i++) _got[i] = false;
        }

        public void Tick(double nowMs)
        {
            if (!_active || IsComplete) return;
            if (nowMs - _startedMs < ReassemblyTimeoutMs) return;
            _timedOut++;
            Reset();
        }

        public VpbNetKeyframeReject Offer(byte[] frag, int offset, int len, double nowMs)
        {
            if (frag == null || offset < 0 || len < FragmentHeader || offset + len > frag.Length)
            {
                _rejected++;
                return VpbNetKeyframeReject.Truncated;
            }
            if (frag[offset] != ProtoVersion)
            {
                _rejected++;
                return VpbNetKeyframeReject.BadVersion;
            }

            int gen = VpbIpc.ReadU16(frag, offset + 2);
            int index = frag[offset + 4];
            int count = frag[offset + 5];
            int payload = VpbIpc.ReadU16(frag, offset + 6);

            if (count <= 0 || count > MaxFragments)
            {
                _rejected++;
                return VpbNetKeyframeReject.BadCount;
            }
            if (index >= count)
            {
                _rejected++;
                return VpbNetKeyframeReject.BadIndex;
            }
            if (payload <= 0 || payload > FragmentPayload || FragmentHeader + payload != len)
            {
                _rejected++;
                return VpbNetKeyframeReject.Truncated;
            }
            if (index != count - 1 && payload != FragmentPayload)
            {
                _rejected++;
                return VpbNetKeyframeReject.BadStride;
            }

            if (_active && gen != _gen)
            {
                if (IsNewerGeneration(gen, _gen))
                {
                    _superseded++;
                    Reset();
                }
                else
                {
                    _rejected++;
                    return VpbNetKeyframeReject.Stale;
                }
            }

            if (!_active)
            {
                _active = true;
                _gen = gen;
                _count = count;
                _have = 0;
                _total = 0;
                _startedMs = nowMs;
                for (int i = 0; i < MaxFragments; i++) _got[i] = false;
            }
            else if (count != _count)
            {
                _rejected++;
                return VpbNetKeyframeReject.BadCount;
            }

            if (_got[index])
            {
                _duplicates++;
                return VpbNetKeyframeReject.Duplicate;
            }

            int start = index * FragmentPayload;
            if (start + payload > _buf.Length)
            {
                _rejected++;
                return VpbNetKeyframeReject.Oversize;
            }

            Buffer.BlockCopy(frag, offset + FragmentHeader, _buf, start, payload);
            _got[index] = true;
            _have++;
            _total += payload;

            if (_have == _count) _completed++;
            return VpbNetKeyframeReject.None;
        }

        public int Take(byte[] dst)
        {
            if (!IsComplete || dst == null || dst.Length < _total) return 0;
            Buffer.BlockCopy(_buf, 0, dst, 0, _total);
            int n = _total;
            Reset();
            return n;
        }

        static bool IsNewerGeneration(int candidate, int current)
        {
            int diff = (candidate - current) & 0xFFFF;
            return diff != 0 && diff < 0x8000;
        }
    }
}
