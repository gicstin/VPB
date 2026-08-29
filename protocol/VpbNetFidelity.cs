using System;

namespace VpbNet
{
    public static class VpbNetPoseExtId
    {
        public const byte Fingers = 1;
        public const byte Gaze = 2;
        public const byte Jaw = 3;

        public const int HeaderBytes = 3;
    }

    public static class VpbNetFidelityRate
    {
        public const int FingerEveryNth = 3;
        public const int GazeEveryNth = 3;
        public const int JawEveryNth = 1;

        public const int RefreshFrames = 45;

        public const double TierCeilingKbps = 90.0;

        public static double WorstCaseKbps(int poseHz)
        {
            double perFrame = VpbPose.FrameBytes
                + (VpbNetPoseExtId.HeaderBytes + VpbNetFingers.WireBytes) / (double)FingerEveryNth
                + (VpbNetPoseExtId.HeaderBytes + VpbNetGaze.PointBytes) / (double)GazeEveryNth
                + (VpbNetPoseExtId.HeaderBytes + VpbNetJaw.WireBytes) / (double)JawEveryNth;
            return perFrame * poseHz * 8.0 / 1000.0;
        }

        public static double SteadyKbps(int poseHz)
        {
            double extPerRefresh = VpbNetPoseExtId.HeaderBytes + VpbNetFingers.WireBytes
                + VpbNetPoseExtId.HeaderBytes + VpbNetGaze.PointBytes;
            double perFrame = VpbPose.FrameBytes
                + extPerRefresh / RefreshFrames
                + (VpbNetPoseExtId.HeaderBytes + VpbNetJaw.WireBytes) / (double)JawEveryNth;
            return perFrame * poseHz * 8.0 / 1000.0;
        }
    }

    public static class VpbNetFingers
    {
        public const int JointsPerFinger = 5;
        public const int FingersPerHand = 5;
        public const int PerHand = JointsPerFinger * FingersPerHand;
        public const int Count = PerHand * 2;
        public const int WireBytes = Count;

        public const int LeftBase = 0;
        public const int RightBase = PerHand;

        public const int OffsetProximalBend = 0;
        public const int OffsetProximalSpread = 1;
        public const int OffsetProximalTwist = 2;
        public const int OffsetMiddleBend = 3;
        public const int OffsetDistalBend = 4;

        public const float RangeDegrees = 100f;
        const float Scale = 127f / RangeDegrees;
        const float InvScale = RangeDegrees / 127f;

        public static readonly string[] FingerNames = { "thumb", "index", "middle", "ring", "pinky" };

        public static int Index(bool rightHand, int finger, int joint)
        {
            if (finger < 0 || finger >= FingersPerHand) return -1;
            if (joint < 0 || joint >= JointsPerFinger) return -1;
            return (rightHand ? RightBase : LeftBase) + finger * JointsPerFinger + joint;
        }

        public static int Quantize(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return 0;
            float t = degrees * Scale;
            if (t > 127f) t = 127f;
            else if (t < -127f) t = -127f;
            return t >= 0f ? (int)(t + 0.5f) : (int)(t - 0.5f);
        }

        public static float Dequantize(int q)
        {
            if (q > 127) q = 127;
            else if (q < -127) q = -127;
            return q * InvScale;
        }

        public static int Write(byte[] dst, int offset, float[] values)
        {
            if (dst == null || values == null) return -1;
            if (values.Length < Count) return -1;
            if (offset < 0 || offset + WireBytes > dst.Length) return -1;

            for (int i = 0; i < Count; i++)
                dst[offset + i] = (byte)(sbyte)Quantize(values[i]);
            return WireBytes;
        }

        public static bool Read(byte[] src, int offset, int len, float[] values)
        {
            if (src == null || values == null) return false;
            if (len != WireBytes) return false;
            if (values.Length < Count) return false;
            if (offset < 0 || offset + len > src.Length) return false;

            for (int i = 0; i < Count; i++)
                values[i] = Dequantize((sbyte)src[offset + i]);
            return true;
        }

        public static bool Differs(float[] a, float[] b, float toleranceDegrees)
        {
            if (a == null || b == null) return true;
            if (a.Length < Count || b.Length < Count) return true;
            for (int i = 0; i < Count; i++)
            {
                float d = a[i] - b[i];
                if (d < 0f) d = -d;
                if (d > toleranceDegrees) return true;
            }
            return false;
        }

        public static float QuantStepDegrees { get { return InvScale; } }
    }

    public static class VpbNetGaze
    {
        public const byte ModeNone = 0;
        public const byte ModeViewer = 1;
        public const byte ModePoint = 2;

        public const int ShortBytes = 1;
        public const int PointBytes = 7;

        public const float RangeMeters = 16f;
        const float Scale = 32767f / RangeMeters;
        const float InvScale = RangeMeters / 32767f;

        public static bool IsKnownMode(byte mode)
        {
            return mode == ModeNone || mode == ModeViewer || mode == ModePoint;
        }

        public static int Quantize(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters)) return 0;
            float t = meters * Scale;
            if (t > 32767f) t = 32767f;
            else if (t < -32767f) t = -32767f;
            return t >= 0f ? (int)(t + 0.5f) : (int)(t - 0.5f);
        }

        public static float Dequantize(int q)
        {
            return q * InvScale;
        }

        public static int WriteNone(byte[] dst, int offset)
        {
            return WriteShort(dst, offset, ModeNone);
        }

        public static int WriteViewer(byte[] dst, int offset)
        {
            return WriteShort(dst, offset, ModeViewer);
        }

        public static int WritePoint(byte[] dst, int offset, float x, float y, float z)
        {
            if (dst == null || offset < 0 || offset + PointBytes > dst.Length) return -1;
            dst[offset] = ModePoint;
            VpbIpc.WriteU16(dst, offset + 1, Quantize(x) & 0xFFFF);
            VpbIpc.WriteU16(dst, offset + 3, Quantize(y) & 0xFFFF);
            VpbIpc.WriteU16(dst, offset + 5, Quantize(z) & 0xFFFF);
            return PointBytes;
        }

        public static bool Read(byte[] src, int offset, int len,
            out byte mode, out float x, out float y, out float z)
        {
            mode = ModeNone;
            x = 0f;
            y = 0f;
            z = 0f;
            if (src == null || len < ShortBytes) return false;
            if (offset < 0 || offset + len > src.Length) return false;

            byte m = src[offset];
            if (!IsKnownMode(m)) return false;

            if (m == ModePoint)
            {
                if (len != PointBytes) return false;
                x = Dequantize((short)VpbIpc.ReadU16(src, offset + 1));
                y = Dequantize((short)VpbIpc.ReadU16(src, offset + 3));
                z = Dequantize((short)VpbIpc.ReadU16(src, offset + 5));
            }
            else if (len != ShortBytes)
            {
                return false;
            }

            mode = m;
            return true;
        }

        public static string Name(byte mode)
        {
            if (mode == ModeNone) return "none";
            if (mode == ModeViewer) return "viewer";
            if (mode == ModePoint) return "point";
            return "mode#" + mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static int WriteShort(byte[] dst, int offset, byte mode)
        {
            if (dst == null || offset < 0 || offset + ShortBytes > dst.Length) return -1;
            dst[offset] = mode;
            return ShortBytes;
        }
    }

    public static class VpbNetJaw
    {
        public const int WireBytes = 2;

        public static int Write(byte[] dst, int offset, float value)
        {
            if (dst == null || offset < 0 || offset + WireBytes > dst.Length) return -1;
            VpbIpc.WriteU16(dst, offset, VpbNetEventCodec.QuantizeMorph(value) & 0xFFFF);
            return WireBytes;
        }

        public static bool Read(byte[] src, int offset, int len, out float value)
        {
            value = 0f;
            if (src == null || len != WireBytes) return false;
            if (offset < 0 || offset + len > src.Length) return false;
            value = VpbNetEventCodec.DequantizeMorph((short)VpbIpc.ReadU16(src, offset));
            return true;
        }
    }

    public sealed class VpbNetFidelityState
    {
        public readonly float[] Fingers = new float[VpbNetFingers.Count];

        public bool HasFingers;
        public bool HasGaze;
        public bool HasJaw;

        public byte GazeMode = VpbNetGaze.ModeNone;
        public float GazeX;
        public float GazeY;
        public float GazeZ;
        public float Jaw;

        public uint Seq;
        public int UnknownBlocks;
        public int RefusedBlocks;

        public void Reset()
        {
            for (int i = 0; i < Fingers.Length; i++) Fingers[i] = 0f;
            HasFingers = false;
            HasGaze = false;
            HasJaw = false;
            GazeMode = VpbNetGaze.ModeNone;
            GazeX = 0f;
            GazeY = 0f;
            GazeZ = 0f;
            Jaw = 0f;
            Seq = 0;
            UnknownBlocks = 0;
            RefusedBlocks = 0;
        }

        public bool ReadFrom(byte[] buf, int extOffset, int extLen, uint seq)
        {
            if (buf == null || extLen <= 0) return false;

            int walk = extOffset;
            int end = extOffset + extLen;
            bool any = false;
            byte id;
            int payloadOffset, payloadLen;

            while (walk < end)
            {
                if (!VpbPose.TryNextExt(buf, ref walk, end, out id, out payloadOffset, out payloadLen))
                    return any;

                if (id == VpbNetPoseExtId.Fingers)
                {
                    if (VpbNetFingers.Read(buf, payloadOffset, payloadLen, Fingers))
                    {
                        HasFingers = true;
                        any = true;
                    }
                    else RefusedBlocks++;
                }
                else if (id == VpbNetPoseExtId.Gaze)
                {
                    byte mode;
                    float x, y, z;
                    if (VpbNetGaze.Read(buf, payloadOffset, payloadLen, out mode, out x, out y, out z))
                    {
                        GazeMode = mode;
                        GazeX = x;
                        GazeY = y;
                        GazeZ = z;
                        HasGaze = true;
                        any = true;
                    }
                    else RefusedBlocks++;
                }
                else if (id == VpbNetPoseExtId.Jaw)
                {
                    float v;
                    if (VpbNetJaw.Read(buf, payloadOffset, payloadLen, out v))
                    {
                        Jaw = v;
                        HasJaw = true;
                        any = true;
                    }
                    else RefusedBlocks++;
                }
                else
                {
                    UnknownBlocks++;
                }
            }

            if (any) Seq = seq;
            return any;
        }
    }
}
