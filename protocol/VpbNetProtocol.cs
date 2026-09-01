using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VpbNet
{
    public static class VpbNetChannel
    {
        public const byte Pose = 0;
        public const byte Event = 1;
        public const byte Ctrl = 2;

        public const byte Keyframe = 3;

        public const byte Contract = 4;

        public const byte Props = 5;

        // Own fragment assembler — too big for one event, too rare for the pose budget.
        public const byte Manifest = 6;
    }

    public static class VpbPose
    {
        public const byte ProtoVersion = 1;
        public const int HeaderSize = 12;
        public const int RootSize = 28;
        public const int BoneSize = 10;
        public const int ControllerCount = 17;
        public const int BoneCount = ControllerCount - 1;
        public const int FloatsPerController = 7;
        public const int PoseFloats = ControllerCount * FloatsPerController;
        public const int FrameBytes = HeaderSize + RootSize + BoneCount * BoneSize;

        public const int MinControllerCount = 2;
        public const int MaxControllerCount = 32;
        public const int MaxFrameBytes = HeaderSize + RootSize + (MaxControllerCount - 1) * BoneSize;

        public static int FrameBytesFor(int controllerCount)
        {
            if (controllerCount < MinControllerCount || controllerCount > MaxControllerCount) return -1;
            return HeaderSize + RootSize + (controllerCount - 1) * BoneSize;
        }

        public static bool IsValidFrameLength(int len)
        {
            if (len < FrameBytesFor(MinControllerCount) || len > MaxFrameBytes) return false;
            return (len - HeaderSize - RootSize) % BoneSize == 0;
        }

        public const byte FlagKeyframe = 1 << 0;
        public const byte FlagHasExt = 1 << 1;
        public const byte FlagSemanticMask = FlagKeyframe | FlagHasExt;

        public const byte FlagCountShift = 2;
        public const byte FlagCountMask = 0x7C;

        public static byte PackFlags(byte semantic, int controllerCount)
        {
            int c = controllerCount - 1;
            if (c < 0) c = 0;
            else if (c > 31) c = 31;
            return (byte)((semantic & FlagSemanticMask) | (c << FlagCountShift));
        }

        public static int CountFromFlags(byte flags)
        {
            return ((flags & FlagCountMask) >> FlagCountShift) + 1;
        }

        public const float PosRangeMeters = 4f;
        const float PosScale = 32767f / PosRangeMeters;
        const float PosInvScale = PosRangeMeters / 32767f;

        const float SqrtHalf = 0.7071067811865476f;
        const int Quant10Bits = 10;
        const int Quant10Mask = (1 << Quant10Bits) - 1;
        const int Quant10Radius = 511;
        const int Quant10Bias = 511;
        const float Quant10Scale = Quant10Radius / SqrtHalf;
        const float Quant10Inv = SqrtHalf / Quant10Radius;

        public static int PoseIndex(int controller)
        {
            return controller * FloatsPerController;
        }

        public static int WriteFrame(
            byte[] buf, int offset,
            byte flags, ushort peerId, uint seq, uint tickMs,
            float[] pose, int poseCount,
            byte[] ext, int extOffset, int extLen)
        {
            int clampedBones;
            return WriteFrame(buf, offset, flags, peerId, seq, tickMs, pose, poseCount, ext, extOffset, extLen,
                out clampedBones);
        }

        public static int WriteFrame(
            byte[] buf, int offset,
            byte flags, ushort peerId, uint seq, uint tickMs,
            float[] pose, int poseCount,
            byte[] ext, int extOffset, int extLen,
            out int clampedBones)
        {
            clampedBones = 0;
            if (buf == null || pose == null) return -1;
            int frameBytes = FrameBytesFor(poseCount);
            if (frameBytes < 0) return -1;
            if (pose.Length < poseCount * FloatsPerController) return -1;
            if (offset < 0 || offset > buf.Length) return -1;

            bool hasExt = ext != null && extLen > 0;
            if (hasExt)
            {
                if (extOffset < 0 || extLen < 0 || extOffset + extLen > ext.Length) return -1;
                flags |= FlagHasExt;
            }
            else
            {
                flags = (byte)(flags & ~FlagHasExt);
                extLen = 0;
            }

            int total = frameBytes + extLen;
            if (offset + total > buf.Length) return -1;
            if (total > VpbIpc.MaxDataPayload) return -1;

            int o = offset;
            buf[o] = ProtoVersion;
            buf[o + 1] = PackFlags(flags, poseCount);
            VpbIpc.WriteU16(buf, o + 2, peerId);
            VpbIpc.WriteU32(buf, o + 4, seq);
            VpbIpc.WriteU32(buf, o + 8, tickMs);
            o += HeaderSize;

            float rx = pose[0];
            float ry = pose[1];
            float rz = pose[2];
            WriteF32(buf, o, rx);
            WriteF32(buf, o + 4, ry);
            WriteF32(buf, o + 8, rz);
            WriteF32(buf, o + 12, pose[3]);
            WriteF32(buf, o + 16, pose[4]);
            WriteF32(buf, o + 20, pose[5]);
            WriteF32(buf, o + 24, pose[6]);
            o += RootSize;

            for (int i = 1; i < poseCount; i++)
            {
                int p = i * FloatsPerController;
                bool boneClamped = false;
                WriteI16(buf, o, QuantizePos(pose[p] - rx, ref boneClamped));
                WriteI16(buf, o + 2, QuantizePos(pose[p + 1] - ry, ref boneClamped));
                WriteI16(buf, o + 4, QuantizePos(pose[p + 2] - rz, ref boneClamped));
                VpbIpc.WriteU32(buf, o + 6, PackQuat(pose[p + 3], pose[p + 4], pose[p + 5], pose[p + 6]));
                if (boneClamped) clampedBones |= 1 << (i - 1);
                o += BoneSize;
            }

            if (extLen > 0) Buffer.BlockCopy(ext, extOffset, buf, o, extLen);
            return total;
        }

        public static bool TryReadFrame(
            byte[] buf, int offset, int len,
            float[] pose, int poseCount,
            out byte version, out byte flags, out ushort peerId, out uint seq, out uint tickMs,
            out int frameLen, out int extOffset, out int extLen)
        {
            version = 0;
            flags = 0;
            peerId = 0;
            seq = 0;
            tickMs = 0;
            frameLen = 0;
            extOffset = 0;
            extLen = 0;

            if (buf == null || pose == null) return false;
            int frameBytes = FrameBytesFor(poseCount);
            if (frameBytes < 0 || pose.Length < poseCount * FloatsPerController) return false;
            if (offset < 0 || len < frameBytes || offset + len > buf.Length) return false;

            int o = offset;
            version = buf[o];
            if (version != ProtoVersion) return false;
            byte rawFlags = buf[o + 1];
            if (CountFromFlags(rawFlags) != poseCount) return false;
            flags = (byte)(rawFlags & FlagSemanticMask);
            peerId = (ushort)VpbIpc.ReadU16(buf, o + 2);
            seq = VpbIpc.ReadU32(buf, o + 4);
            tickMs = VpbIpc.ReadU32(buf, o + 8);
            o += HeaderSize;

            float rx = ReadF32(buf, o);
            float ry = ReadF32(buf, o + 4);
            float rz = ReadF32(buf, o + 8);
            if (IsBad(rx) || IsBad(ry) || IsBad(rz)) return false;

            pose[0] = rx;
            pose[1] = ry;
            pose[2] = rz;
            pose[3] = ReadF32(buf, o + 12);
            pose[4] = ReadF32(buf, o + 16);
            pose[5] = ReadF32(buf, o + 20);
            pose[6] = ReadF32(buf, o + 24);
            if (IsBad(pose[3]) || IsBad(pose[4]) || IsBad(pose[5]) || IsBad(pose[6])) return false;
            o += RootSize;

            for (int i = 1; i < poseCount; i++)
            {
                int p = i * FloatsPerController;
                pose[p] = rx + DequantizePos(ReadI16(buf, o));
                pose[p + 1] = ry + DequantizePos(ReadI16(buf, o + 2));
                pose[p + 2] = rz + DequantizePos(ReadI16(buf, o + 4));
                UnpackQuat(VpbIpc.ReadU32(buf, o + 6), out pose[p + 3], out pose[p + 4], out pose[p + 5], out pose[p + 6]);
                o += BoneSize;
            }

            int end = offset + len;
            extOffset = o;
            extLen = end - o;
            if (extLen < 0) return false;
            if (!ExtRegionValid(buf, extOffset, extLen)) return false;

            frameLen = frameBytes + extLen;
            return true;
        }

        public static int AppendExt(byte[] buf, int offset, int bufLen, byte blockId, byte[] payload, int payloadOffset, int payloadLen)
        {
            if (buf == null) return -1;
            if (payloadLen < 0 || payloadLen > 0xFFFF) return -1;
            if (payloadLen > 0 && (payload == null || payloadOffset < 0 || payloadOffset + payloadLen > payload.Length))
                return -1;
            int need = 3 + payloadLen;
            if (offset < 0 || offset + need > bufLen || offset + need > buf.Length) return -1;
            if (offset + need > VpbIpc.MaxDataPayload) return -1;

            buf[offset] = blockId;
            VpbIpc.WriteU16(buf, offset + 1, payloadLen);
            if (payloadLen > 0) Buffer.BlockCopy(payload, payloadOffset, buf, offset + 3, payloadLen);
            return need;
        }

        public static bool ExtRegionValid(byte[] buf, int offset, int len)
        {
            if (len == 0) return true;
            if (buf == null || offset < 0 || len < 0 || offset + len > buf.Length) return false;
            int o = offset;
            int end = offset + len;
            while (o < end)
            {
                if (end - o < 3) return false;
                int blockLen = VpbIpc.ReadU16(buf, o + 1);
                if (blockLen < 0 || o + 3 + blockLen > end) return false;
                o += 3 + blockLen;
            }
            return o == end;
        }

        public static bool TryNextExt(
            byte[] buf, ref int offset, int end,
            out byte blockId, out int payloadOffset, out int payloadLen)
        {
            blockId = 0;
            payloadOffset = 0;
            payloadLen = 0;
            if (buf == null || offset == end) return false;
            if (offset < 0 || offset > end || end - offset < 3) return false;
            int blockLen = VpbIpc.ReadU16(buf, offset + 1);
            if (blockLen < 0 || offset + 3 + blockLen > end) return false;
            blockId = buf[offset];
            payloadOffset = offset + 3;
            payloadLen = blockLen;
            offset += 3 + blockLen;
            return true;
        }

        public static int QuantizePos(float meters)
        {
            bool ignored = false;
            return QuantizePos(meters, ref ignored);
        }

        static int QuantizePos(float meters, ref bool clamped)
        {
            float t = meters * PosScale;
            if (t > 32767f) { t = 32767f; clamped = true; }
            else if (t < -32767f) { t = -32767f; clamped = true; }
            else if (IsBad(t)) { t = 0f; clamped = true; }
            return RoundToInt(t);
        }

        public static float DequantizePos(int q)
        {
            return q * PosInvScale;
        }

        public static uint PackQuat(float x, float y, float z, float w)
        {
            float mag2 = x * x + y * y + z * z + w * w;
            if (mag2 < 1e-12f)
            {
                x = 0f;
                y = 0f;
                z = 0f;
                w = 1f;
            }
            else
            {
                float inv = (float)(1.0 / Math.Sqrt(mag2));
                x *= inv;
                y *= inv;
                z *= inv;
                w *= inv;
            }

            float ax = x >= 0f ? x : -x;
            float ay = y >= 0f ? y : -y;
            float az = z >= 0f ? z : -z;
            float aw = w >= 0f ? w : -w;

            int dropped = 0;
            float largest = ax;
            if (ay >= largest) { dropped = 1; largest = ay; }
            if (az >= largest) { dropped = 2; largest = az; }
            if (aw >= largest) { dropped = 3; }

            float sign = dropped == 0 ? x : dropped == 1 ? y : dropped == 2 ? z : w;
            if (sign < 0f)
            {
                x = -x;
                y = -y;
                z = -z;
                w = -w;
            }

            float a, b, c;
            if (dropped == 0) { a = y; b = z; c = w; }
            else if (dropped == 1) { a = x; b = z; c = w; }
            else if (dropped == 2) { a = x; b = y; c = w; }
            else { a = x; b = y; c = z; }

            uint qa = Quant10(a);
            uint qb = Quant10(b);
            uint qc = Quant10(c);
            return (uint)dropped | (qa << 2) | (qb << 12) | (qc << 22);
        }

        public static void UnpackQuat(uint packed, out float x, out float y, out float z, out float w)
        {
            int dropped = (int)(packed & 3);
            float a = Dequant10((packed >> 2) & Quant10Mask);
            float b = Dequant10((packed >> 12) & Quant10Mask);
            float c = Dequant10((packed >> 22) & Quant10Mask);
            float droppedSq = 1f - a * a - b * b - c * c;
            float droppedVal;
            if (droppedSq > 0f)
            {
                droppedVal = (float)Math.Sqrt(droppedSq);
            }
            else
            {
                droppedVal = 0f;
                float keptSq = a * a + b * b + c * c;
                if (keptSq > 1f)
                {
                    float inv = (float)(1.0 / Math.Sqrt(keptSq));
                    a *= inv;
                    b *= inv;
                    c *= inv;
                }
            }

            if (dropped == 0) { x = droppedVal; y = a; z = b; w = c; }
            else if (dropped == 1) { x = a; y = droppedVal; z = b; w = c; }
            else if (dropped == 2) { x = a; y = b; z = droppedVal; w = c; }
            else { x = a; y = b; z = c; w = droppedVal; }
        }

        public static int RunSelfTestConsole()
        {
            StringBuilder sb = new StringBuilder(2048);
            bool ok = RunSelfTest(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool RunSelfTest(StringBuilder log)
        {
            int fail = 0;
            int pass = 0;
            byte[] buf = new byte[VpbIpc.MaxDataPayload];
            float[] src = new float[PoseFloats];
            float[] dst = new float[PoseFloats];

            Line(log, "===== POSE codec self-test =====");
            Line(log, "layout: header=" + HeaderSize + " root=" + RootSize
                + " bones=" + BoneCount + "x" + BoneSize + " frame=" + FrameBytes + " B");

            Pass(log, ref pass, "frame size " + FrameBytes + " B");

            double kbps = FrameBytes * 45.0 * 8.0 / 1000.0;
            if (kbps > 80.0)
            {
                Fail(log, ref fail, "bandwidth", kbps.ToString("0.0") + " kbit/s at 45 Hz, over 80");
            }
            else
            {
                Pass(log, ref pass, "45 Hz = " + kbps.ToString("0.0") + " kbit/s (plan ~75)");
            }

            FillIdentity(src, 1f, 2f, 3f);
            int n = WriteFrame(buf, 0, 0, 0x0201, 0x04030201u, 0x08070605u, src, ControllerCount, null, 0, 0);
            if (n != FrameBytes)
            {
                Fail(log, ref fail, "write identity", "wrote " + n);
            }
            else if (buf[0] != ProtoVersion || buf[1] != PackFlags(0, ControllerCount)
                     || buf[2] != 0x01 || buf[3] != 0x02
                     || buf[4] != 0x01 || buf[5] != 0x02 || buf[6] != 0x03 || buf[7] != 0x04
                     || buf[8] != 0x05 || buf[9] != 0x06 || buf[10] != 0x07 || buf[11] != 0x08)
            {
                Fail(log, ref fail, "little-endian header", "bytes do not match the written fields");
            }
            else if (ReadF32(buf, HeaderSize) != 1f || ReadF32(buf, HeaderSize + 4) != 2f
                     || ReadF32(buf, HeaderSize + 8) != 3f)
            {
                Fail(log, ref fail, "root f32 bits", "position did not round-trip exactly");
            }
            else
            {
                Pass(log, ref pass, "explicit little-endian header + exact root f32");
            }

            byte ver, flags;
            ushort peer;
            uint seq, tick;
            int frameLen, extOff, extN;
            if (!TryReadFrame(buf, 0, n, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                    out frameLen, out extOff, out extN)
                || ver != ProtoVersion || flags != 0 || peer != 0x0201 || seq != 0x04030201u
                || tick != 0x08070605u || frameLen != FrameBytes || extN != 0)
            {
                Fail(log, ref fail, "read identity header", "fields mismatch");
            }
            else if (dst[0] != 1f || dst[1] != 2f || dst[2] != 3f)
            {
                Fail(log, ref fail, "read identity root pos", "not bit-exact");
            }
            else
            {
                float ang = QuatAngleDeg(src[3], src[4], src[5], src[6], dst[3], dst[4], dst[5], dst[6]);
                if (ang > 0.0001f)
                    Fail(log, ref fail, "read identity root quat", "angle " + ang.ToString("0.#####") + " deg");
                else
                    Pass(log, ref pass, "identity header/root round-trip");
            }

            float maxBonePosMm = 0f;
            float maxBoneAng = 0f;
            for (int i = 1; i < ControllerCount; i++)
            {
                int p = i * FloatsPerController;
                float dx = dst[p] - src[p];
                float dy = dst[p + 1] - src[p + 1];
                float dz = dst[p + 2] - src[p + 2];
                float mm = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000f;
                if (mm > maxBonePosMm) maxBonePosMm = mm;
                float a = QuatAngleDeg(src[p + 3], src[p + 4], src[p + 5], src[p + 6],
                    dst[p + 3], dst[p + 4], dst[p + 5], dst[p + 6]);
                if (a > maxBoneAng) maxBoneAng = a;
            }
            if (maxBonePosMm > 0.13f)
                Fail(log, ref fail, "identity bone pos", "max error " + maxBonePosMm.ToString("0.000") + " mm");
            else
                Pass(log, ref pass, "identity bone pos max " + maxBonePosMm.ToString("0.000") + " mm (LSB 0.122 mm)");
            if (maxBoneAng > 0.01f)
                Fail(log, ref fail, "identity bone quat", "max error " + maxBoneAng.ToString("0.0000") + " deg");
            else
                Pass(log, ref pass, "identity bone quat max " + maxBoneAng.ToString("0.0000") + " deg");

            CheckAxis90(log, ref pass, ref fail, buf, src, dst, 1f, 0f, 0f, "90 deg X");
            CheckAxis90(log, ref pass, ref fail, buf, src, dst, 0f, 1f, 0f, "90 deg Y");
            CheckAxis90(log, ref pass, ref fail, buf, src, dst, 0f, 0f, 1f, "90 deg Z");

            FillIdentity(src, 10f, 0f, -4f);
            SetController(src, 5, 10f + 0.8f, 1.2f, -4f + 0.3f, 0f, 0f, 0f, 1f);
            SetController(src, 6, 10f - 0.8f, 1.2f, -4f + 0.3f, 0f, 0.70710678f, 0f, 0.70710678f);
            n = WriteFrame(buf, 0, FlagKeyframe, 1, 7, 1234, src, ControllerCount, null, 0, 0);
            if (!TryReadFrame(buf, 0, n, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                    out frameLen, out extOff, out extN) || (flags & FlagKeyframe) == 0)
            {
                Fail(log, ref fail, "keyframe flag", "lost on the wire");
            }
            else
            {
                float err = DistMm(src, dst, 5);
                if (err > 0.13f)
                    Fail(log, ref fail, "world-delta bone 5", err.ToString("0.000") + " mm");
                else
                    Pass(log, ref pass, "world-delta at root (10,0,-4) bone 5 err " + err.ToString("0.000") + " mm");
            }

            FillIdentity(src, 0f, 0f, 0f);
            SetController(src, 1, 5f, 0f, 0f, 0f, 0f, 0f, 1f);
            n = WriteFrame(buf, 0, 0, 0, 0, 0, src, ControllerCount, null, 0, 0);
            TryReadFrame(buf, 0, n, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                out frameLen, out extOff, out extN);
            float clamped = dst[1 * FloatsPerController];
            if (clamped < 3.999f || clamped > 4.001f)
                Fail(log, ref fail, "pos clamp", "5 m encoded as " + clamped.ToString("0.000") + " m, want 4");
            else
                Pass(log, ref pass, "position clamp +/-4 m");

            Random rng = new Random(1);
            float maxRandPosMm = 0f;
            float maxRandAng = 0f;
            const int RandomN = 256;
            for (int k = 0; k < RandomN; k++)
            {
                FillIdentity(src, RandRange(rng, -20f, 20f), RandRange(rng, -2f, 3f), RandRange(rng, -20f, 20f));
                for (int i = 1; i < ControllerCount; i++)
                {
                    float qx, qy, qz, qw;
                    RandomUnitQuat(rng, out qx, out qy, out qz, out qw);
                    SetController(src, i,
                        src[0] + RandRange(rng, -1.5f, 1.5f),
                        src[1] + RandRange(rng, -1.5f, 1.5f),
                        src[2] + RandRange(rng, -1.5f, 1.5f),
                        qx, qy, qz, qw);
                }
                n = WriteFrame(buf, 0, 0, 0, (uint)k, 0, src, ControllerCount, null, 0, 0);
                if (!TryReadFrame(buf, 0, n, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                        out frameLen, out extOff, out extN))
                {
                    Fail(log, ref fail, "random read", "frame " + k);
                    break;
                }
                for (int i = 1; i < ControllerCount; i++)
                {
                    float mm = DistMm(src, dst, i);
                    if (mm > maxRandPosMm) maxRandPosMm = mm;
                    int p = i * FloatsPerController;
                    float a = QuatAngleDeg(src[p + 3], src[p + 4], src[p + 5], src[p + 6],
                        dst[p + 3], dst[p + 4], dst[p + 5], dst[p + 6]);
                    if (a > maxRandAng) maxRandAng = a;
                }
            }
            if (maxRandPosMm > 0.13f)
                Fail(log, ref fail, "random bone pos", "max " + maxRandPosMm.ToString("0.000") + " mm over " + RandomN);
            else
                Pass(log, ref pass, "random bone pos max " + maxRandPosMm.ToString("0.000") + " mm n=" + RandomN);
            if (maxRandAng > 0.25f)
                Fail(log, ref fail, "random bone quat", "max " + maxRandAng.ToString("0.0000") + " deg over " + RandomN);
            else
                Pass(log, ref pass, "random bone quat max " + maxRandAng.ToString("0.0000") + " deg n=" + RandomN);

            byte[] extScratch = new byte[64];
            byte[] payA = { 1, 2, 3, 4 };
            byte[] payB = { 9, 8, 7 };
            int e = 0;
            int aN = AppendExt(extScratch, e, extScratch.Length, 7, payA, 0, payA.Length);
            e += aN;
            int skipN = AppendExt(extScratch, e, extScratch.Length, 99, payB, 0, payB.Length);
            e += skipN;
            int bN = AppendExt(extScratch, e, extScratch.Length, 7, payA, 0, payA.Length);
            e += bN;
            FillIdentity(src, 0f, 0f, 0f);
            n = WriteFrame(buf, 0, 0, 0, 1, 0, src, ControllerCount, extScratch, 0, e);
            if (n != FrameBytes + e)
            {
                Fail(log, ref fail, "ext write size", "wrote " + n + " want " + (FrameBytes + e));
            }
            else if (!TryReadFrame(buf, 0, n, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                         out frameLen, out extOff, out extN)
                     || (flags & FlagHasExt) == 0 || extN != e)
            {
                Fail(log, ref fail, "ext flag/len", "flags=" + flags + " extLen=" + extN);
            }
            else
            {
                int walk = extOff;
                int end = extOff + extN;
                int seenKnown = 0;
                int seenUnknown = 0;
                bool extOk = true;
                byte bid;
                int po, pl;
                while (walk < end)
                {
                    if (!TryNextExt(buf, ref walk, end, out bid, out po, out pl))
                    {
                        extOk = false;
                        break;
                    }
                    if (bid == 7) seenKnown++;
                    else if (bid == 99) seenUnknown++;
                    else extOk = false;
                }
                if (!extOk || seenKnown != 2 || seenUnknown != 1)
                    Fail(log, ref fail, "ext skip", "known=" + seenKnown + " unknown=" + seenUnknown + " (unknown must be skipped, not fatal)");
                else
                    Pass(log, ref pass, "ext: 2 known + 1 unknown id, unknown skipped");
            }

            if (TryReadFrame(buf, 0, FrameBytes - 1, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                    out frameLen, out extOff, out extN))
                Fail(log, ref fail, "short frame", "accepted a truncated pose");
            else
                Pass(log, ref pass, "refuse truncated frame");

            buf[0] = 99;
            if (TryReadFrame(buf, 0, FrameBytes, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                    out frameLen, out extOff, out extN) || ver != 99)
                Fail(log, ref fail, "bad version", "accepted proto v99 or lost the version byte");
            else
                Pass(log, ref pass, "refuse unknown protoVersion");

            FillIdentity(src, 0f, 0f, 0f);
            n = WriteFrame(buf, 0, 0, 0, 0, 0, src, ControllerCount, null, 0, 0);
            int extStart = AppendExt(buf, n, buf.Length, 1, payA, 0, payA.Length);
            if (!TryReadFrame(buf, 0, n + extStart - 1, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                    out frameLen, out extOff, out extN))
                Pass(log, ref pass, "refuse truncated ext block");
            else
                Fail(log, ref fail, "truncated ext", "accepted a cut-off block length");

            n = WriteFrame(buf, 0, 0, 0, 0, 0, src, ControllerCount, null, 0, 0);
            if (n > 0 && !TryReadFrame(buf, 0, n, dst, ControllerCount - 1, out ver, out flags, out peer, out seq, out tick,
                    out frameLen, out extOff, out extN))
                Pass(log, ref pass, "a frame written for " + ControllerCount + " controllers is refused by a reader expecting " + (ControllerCount - 1));
            else
                Fail(log, ref fail, "wrong count", "a " + ControllerCount + "-controller frame decoded as " + (ControllerCount - 1));

            if (WriteFrame(buf, 0, 0, 0, 0, 0, src, MaxControllerCount + 1, null, 0, 0) >= 0)
                Fail(log, ref fail, "count range", "accepted poseCount " + (MaxControllerCount + 1));
            else
                Pass(log, ref pass, "a count past the " + MaxControllerCount + "-controller ceiling is refused");

            FillIdentity(src, 0f, 0f, 0f);
            n = WriteFrame(buf, 0, 0, 0, 0, 0, src, ControllerCount, null, 0, 0);
            float worstMag = 0f;
            for (int d = 0; d < 4; d++)
            {
                uint hostile = (uint)d | (1022u << 2) | (1022u << 12) | (1022u << 22);
                VpbIpc.WriteU32(buf, HeaderSize + RootSize + 6, hostile);
                if (!TryReadFrame(buf, 0, n, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                        out frameLen, out extOff, out extN))
                    continue;
                int p = 1 * FloatsPerController;
                float mag = (float)Math.Sqrt(dst[p + 3] * dst[p + 3] + dst[p + 4] * dst[p + 4]
                    + dst[p + 5] * dst[p + 5] + dst[p + 6] * dst[p + 6]);
                float off = mag > 1f ? mag - 1f : 1f - mag;
                if (off > worstMag) worstMag = off;
            }
            if (worstMag > 0.001f)
                Fail(log, ref fail, "hostile quat", "decoded |q| off unit by " + worstMag.ToString("0.0000"));
            else
                Pass(log, ref pass, "corrupt quat word still decodes to a unit rotation");

            FillIdentity(src, 0f, 0f, 0f);
            for (int i = 1; i < ControllerCount; i++)
                SetController(src, i, 0.3f, 1.2f, -0.4f, 0f, 0f, 0f, 1f);
            int clampMask;
            WriteFrame(buf, 0, 0, 0, 0, 0, src, ControllerCount, null, 0, 0, out clampMask);
            if (clampMask != 0)
            {
                Fail(log, ref fail, "clamp signal", "in-range pose reported mask " + clampMask);
            }
            else
            {
                SetController(src, 1, 0f, 0f, 9f, 0f, 0f, 0f, 1f);
                WriteFrame(buf, 0, 0, 0, 0, 0, src, ControllerCount, null, 0, 0, out clampMask);
                if (clampMask != 1)
                    Fail(log, ref fail, "clamp signal", "hip past " + PosRangeMeters + " m reported mask " + clampMask + ", want 1");
                else
                    Pass(log, ref pass, "clamp reports bone bitmask (silent teleport is now loggable)");
            }

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 layout    " + FrameBytes + " B @ 45 Hz = " + kbps.ToString("0.0") + " kbit/s : "
                + (FrameBytes == 200 && kbps <= 80.0 ? "PASS" : "FAIL"));
            Line(log, "EXIT 2/4 quant     pos<=0.13mm quat identity=0 90deg<=0.05 random<=0.25deg : "
                + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 ext skip  unknown ids never fatal   : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 malformed refused, decode always unit, clamp signalled : "
                + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end POSE codec self-test =====");
            return fail == 0;
        }

        static void CheckAxis90(StringBuilder log, ref int pass, ref int fail, byte[] buf, float[] src, float[] dst,
            float ax, float ay, float az, string name)
        {
            float s = SqrtHalf;
            FillIdentity(src, 0f, 0f, 0f);
            SetController(src, 4, 0.1f, 1.5f, 0.2f, ax * s, ay * s, az * s, s);
            int n = WriteFrame(buf, 0, 0, 0, 0, 0, src, ControllerCount, null, 0, 0);
            byte ver, flags;
            ushort peer;
            uint seq, tick;
            int frameLen, extOff, extN;
            if (!TryReadFrame(buf, 0, n, dst, ControllerCount, out ver, out flags, out peer, out seq, out tick,
                    out frameLen, out extOff, out extN))
            {
                Fail(log, ref fail, name, "decode failed");
                return;
            }
            int p = 4 * FloatsPerController;
            float ang = QuatAngleDeg(src[p + 3], src[p + 4], src[p + 5], src[p + 6],
                dst[p + 3], dst[p + 4], dst[p + 5], dst[p + 6]);
            if (ang > 0.05f)
            {
                Fail(log, ref fail, name, "angle error " + ang.ToString("0.0000") + " deg");
                return;
            }
            Pass(log, ref pass, name + " err " + ang.ToString("0.0000") + " deg");
        }

        static void FillIdentity(float[] pose, float x, float y, float z)
        {
            for (int i = 0; i < ControllerCount; i++)
                SetController(pose, i, x, y, z, 0f, 0f, 0f, 1f);
        }

        static void SetController(float[] pose, int i, float x, float y, float z, float qx, float qy, float qz, float qw)
        {
            int p = i * FloatsPerController;
            pose[p] = x;
            pose[p + 1] = y;
            pose[p + 2] = z;
            pose[p + 3] = qx;
            pose[p + 4] = qy;
            pose[p + 5] = qz;
            pose[p + 6] = qw;
        }

        static float DistMm(float[] a, float[] b, int controller)
        {
            int p = controller * FloatsPerController;
            float dx = a[p] - b[p];
            float dy = a[p + 1] - b[p + 1];
            float dz = a[p + 2] - b[p + 2];
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) * 1000f;
        }

        static float QuatAngleDeg(float ax, float ay, float az, float aw, float bx, float by, float bz, float bw)
        {
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f) dot = -dot;
            if (dot > 1f) dot = 1f;
            return (float)(2.0 * Math.Acos(dot) * (180.0 / Math.PI));
        }

        static void RandomUnitQuat(Random rng, out float x, out float y, out float z, out float w)
        {
            double u1 = rng.NextDouble();
            double u2 = rng.NextDouble() * 2.0 * Math.PI;
            double u3 = rng.NextDouble() * 2.0 * Math.PI;
            double sq1 = Math.Sqrt(1.0 - u1);
            double sq2 = Math.Sqrt(u1);
            x = (float)(sq1 * Math.Sin(u2));
            y = (float)(sq1 * Math.Cos(u2));
            z = (float)(sq2 * Math.Sin(u3));
            w = (float)(sq2 * Math.Cos(u3));
        }

        static float RandRange(Random rng, float lo, float hi)
        {
            return lo + (float)rng.NextDouble() * (hi - lo);
        }

        static void Line(StringBuilder log, string s)
        {
            if (log == null) return;
            log.Append(s);
            log.Append('\n');
        }

        static void Pass(StringBuilder log, ref int pass, string s)
        {
            pass++;
            Line(log, "PASS  " + s);
        }

        static void Fail(StringBuilder log, ref int fail, string name, string detail)
        {
            fail++;
            Line(log, "FAIL  " + name + ": " + detail);
        }

        static uint Quant10(float v)
        {
            int q = RoundToInt(v * Quant10Scale);
            if (q > Quant10Radius) q = Quant10Radius;
            else if (q < -Quant10Radius) q = -Quant10Radius;
            return (uint)(q + Quant10Bias);
        }

        static float Dequant10(uint stored)
        {
            int q = (int)stored - Quant10Bias;
            if (q > Quant10Radius) q = Quant10Radius;
            else if (q < -Quant10Radius) q = -Quant10Radius;
            return q * Quant10Inv;
        }

        static int RoundToInt(float v)
        {
            return v >= 0f ? (int)(v + 0.5f) : (int)(v - 0.5f);
        }

        static bool IsBad(float v)
        {
            return float.IsNaN(v) || float.IsInfinity(v);
        }

        static void WriteI16(byte[] b, int o, int v)
        {
            VpbIpc.WriteU16(b, o, v & 0xFFFF);
        }

        static int ReadI16(byte[] b, int o)
        {
            return (short)VpbIpc.ReadU16(b, o);
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
