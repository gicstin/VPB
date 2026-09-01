using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VpbNet
{
    public static class VpbNetPropLimits
    {
        public const byte ProtoVersion = 1;
        public const int HeaderSize = 8;
        public const int FixedPerAtom = 17;
        public const int MaxAtomsPerFrame = 8;
        public const int MaxUidChars = 96;
        public const int MaxTracked = 512;
    }

    public enum VpbNetPropReject
    {
        None = 0,
        BadVersion = 1,
        Truncated = 2,
        CountCap = 3,
        BadIdentifier = 4,
        PluginReference = 5,
        BadValue = 6
    }

    public sealed class VpbNetPropFrame
    {
        readonly string[] _uid = new string[VpbNetPropLimits.MaxAtomsPerFrame];
        readonly float[] _px = new float[VpbNetPropLimits.MaxAtomsPerFrame];
        readonly float[] _py = new float[VpbNetPropLimits.MaxAtomsPerFrame];
        readonly float[] _pz = new float[VpbNetPropLimits.MaxAtomsPerFrame];
        readonly float[] _qx = new float[VpbNetPropLimits.MaxAtomsPerFrame];
        readonly float[] _qy = new float[VpbNetPropLimits.MaxAtomsPerFrame];
        readonly float[] _qz = new float[VpbNetPropLimits.MaxAtomsPerFrame];
        readonly float[] _qw = new float[VpbNetPropLimits.MaxAtomsPerFrame];

        int _count;
        uint _seq;

        public int Count { get { return _count; } }
        public uint Seq { get { return _seq; } }

        public string Uid(int i) { return _uid[i]; }
        public float PosX(int i) { return _px[i]; }
        public float PosY(int i) { return _py[i]; }
        public float PosZ(int i) { return _pz[i]; }
        public float RotX(int i) { return _qx[i]; }
        public float RotY(int i) { return _qy[i]; }
        public float RotZ(int i) { return _qz[i]; }
        public float RotW(int i) { return _qw[i]; }

        public void Clear()
        {
            for (int i = 0; i < _uid.Length; i++) _uid[i] = null;
            _count = 0;
            _seq = 0;
        }

        public bool Add(string uid, float px, float py, float pz, float qx, float qy, float qz, float qw)
        {
            if (_count >= VpbNetPropLimits.MaxAtomsPerFrame) return false;
            if (!IsSendableUid(uid)) return false;

            int i = _count;
            _uid[i] = uid;
            _px[i] = px;
            _py[i] = py;
            _pz[i] = pz;
            _qx[i] = qx;
            _qy[i] = qy;
            _qz[i] = qz;
            _qw[i] = qw;
            _count++;
            return true;
        }

        public static bool IsSendableUid(string uid)
        {
            if (uid == null || uid.Length == 0 || uid.Length > VpbNetPropLimits.MaxUidChars) return false;
            if (!VpbNetEventCodec.IsSafeIdentifier(uid)) return false;
            return !VpbNetEventCodec.IsPluginReference(uid);
        }

        public static int Capacity(int uidBytesSoFar, int atomsSoFar)
        {
            return VpbNetPropLimits.HeaderSize + uidBytesSoFar
                + atomsSoFar * VpbNetPropLimits.FixedPerAtom;
        }

        public int Write(byte[] dst, uint seq)
        {
            if (dst == null || _count == 0) return -1;
            if (dst.Length < VpbNetPropLimits.HeaderSize) return -1;

            dst[0] = VpbNetPropLimits.ProtoVersion;
            dst[1] = (byte)_count;
            VpbIpc.WriteU16(dst, 2, 0);
            VpbIpc.WriteU32(dst, 4, seq);

            int at = VpbNetPropLimits.HeaderSize;
            for (int i = 0; i < _count; i++)
            {
                string uid = _uid[i];
                int n;
                try { n = Encoding.UTF8.GetByteCount(uid); }
                catch { return -1; }
                if (n > 255) return -1;
                if (at + 1 + n + 16 > dst.Length) return -1;
                if (at + 1 + n + 16 > VpbIpc.MaxDataPayload) return -1;

                dst[at] = (byte)n;
                at++;
                try { Encoding.UTF8.GetBytes(uid, 0, uid.Length, dst, at); }
                catch { return -1; }
                at += n;

                WriteF32(dst, at, _px[i]);
                WriteF32(dst, at + 4, _py[i]);
                WriteF32(dst, at + 8, _pz[i]);
                VpbIpc.WriteU32(dst, at + 12, VpbPose.PackQuat(_qx[i], _qy[i], _qz[i], _qw[i]));
                at += 16;
            }

            _seq = seq;
            return at;
        }

        public VpbNetPropReject Read(byte[] src, int offset, int len)
        {
            Clear();
            if (src == null || len < VpbNetPropLimits.HeaderSize) return VpbNetPropReject.Truncated;
            if (offset < 0 || offset + len > src.Length) return VpbNetPropReject.Truncated;
            if (src[offset] != VpbNetPropLimits.ProtoVersion) return VpbNetPropReject.BadVersion;

            int count = src[offset + 1];
            if (count > VpbNetPropLimits.MaxAtomsPerFrame) return VpbNetPropReject.CountCap;
            _seq = VpbIpc.ReadU32(src, offset + 4);

            int at = offset + VpbNetPropLimits.HeaderSize;
            int end = offset + len;

            for (int i = 0; i < count; i++)
            {
                if (at + 1 > end) return Fail(VpbNetPropReject.Truncated);
                int n = src[at];
                at++;
                if (at + n + 16 > end) return Fail(VpbNetPropReject.Truncated);

                string uid;
                try { uid = Encoding.UTF8.GetString(src, at, n); }
                catch { return Fail(VpbNetPropReject.BadIdentifier); }
                at += n;

                if (uid.Length == 0 || uid.Length > VpbNetPropLimits.MaxUidChars)
                    return Fail(VpbNetPropReject.BadIdentifier);
                if (!VpbNetEventCodec.IsSafeIdentifier(uid)) return Fail(VpbNetPropReject.BadIdentifier);
                if (VpbNetEventCodec.IsPluginReference(uid)) return Fail(VpbNetPropReject.PluginReference);

                float px = ReadF32(src, at);
                float py = ReadF32(src, at + 4);
                float pz = ReadF32(src, at + 8);
                if (IsBad(px) || IsBad(py) || IsBad(pz)) return Fail(VpbNetPropReject.BadValue);

                float qx, qy, qz, qw;
                VpbPose.UnpackQuat(VpbIpc.ReadU32(src, at + 12), out qx, out qy, out qz, out qw);
                at += 16;

                _uid[i] = uid;
                _px[i] = px;
                _py[i] = py;
                _pz[i] = pz;
                _qx[i] = qx;
                _qy[i] = qy;
                _qz[i] = qz;
                _qw[i] = qw;
                _count++;
            }

            if (at != end) return Fail(VpbNetPropReject.Truncated);
            return VpbNetPropReject.None;
        }

        VpbNetPropReject Fail(VpbNetPropReject why)
        {
            Clear();
            return why;
        }

        public static string Explain(VpbNetPropReject r)
        {
            switch (r)
            {
                case VpbNetPropReject.None: return "accepted";
                case VpbNetPropReject.BadVersion: return "that peer speaks a different prop format; update whichever VPB is older";
                case VpbNetPropReject.Truncated: return "a prop update arrived cut short and was dropped";
                case VpbNetPropReject.CountCap: return "a prop update named more atoms than one datagram may carry";
                case VpbNetPropReject.BadIdentifier: return "a prop update named an atom this build refuses to handle";
                case VpbNetPropReject.PluginReference: return "a prop update named a plugin, which is never accepted";
                case VpbNetPropReject.BadValue: return "a prop update carried a position that is not a number";
            }
            return "a prop update was refused";
        }

        static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }

        [StructLayout(LayoutKind.Explicit)]
        struct F32Bits
        {
            [FieldOffset(0)] public float F;
            [FieldOffset(0)] public uint U;
        }

        static void WriteF32(byte[] b, int o, float v)
        {
            F32Bits bits = new F32Bits();
            bits.F = v;
            VpbIpc.WriteU32(b, o, bits.U);
        }

        static float ReadF32(byte[] b, int o)
        {
            F32Bits bits = new F32Bits();
            bits.U = VpbIpc.ReadU32(b, o);
            return bits.F;
        }
    }
}
