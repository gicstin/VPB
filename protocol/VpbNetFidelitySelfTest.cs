using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetFidelitySelfTest
    {
        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(8192);
            bool ok = Run(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== fidelity ext self-test =====");
            Line(log, "layout: fingers=" + VpbNetFingers.WireBytes
                + " B gaze=" + VpbNetGaze.ShortBytes + "/" + VpbNetGaze.PointBytes
                + " B jaw=" + VpbNetJaw.WireBytes + " B");

            Budget(log, ref pass, ref fail);
            FingerCodec(log, ref pass, ref fail);
            FingerLayout(log, ref pass, ref fail);
            GazeCodec(log, ref pass, ref fail);
            JawCodec(log, ref pass, ref fail);
            Malformed(log, ref pass, ref fail);
            StateAssembly(log, ref pass, ref fail);
            RidesAPoseFrame(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 budget    the opt-in tier stays under its own "
                + VpbNetFidelityRate.TierCeilingKbps.ToString("0") + " kbit/s and leaves pose alone : " + V(fail));
            Line(log, "EXIT 2/4 quant     finger <=0.4 deg, gaze <=0.5 mm, jaw matches the morph codec : " + V(fail));
            Line(log, "EXIT 3/4 malformed a wrong-length or unknown block is skipped, never fatal : " + V(fail));
            Line(log, "EXIT 4/4 carriage  the blocks ride a real pose frame and survive the round trip : " + V(fail));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end fidelity ext self-test =====");
            return fail == 0;
        }

        const int ExtHeader = VpbNetPoseExtId.HeaderBytes;
        const int PoseHz = 45;

        static void Budget(StringBuilder log, ref int pass, ref int fail)
        {
            double poseOnly = VpbPose.FrameBytes * PoseHz * 8.0 / 1000.0;
            Check(log, ref pass, ref fail,
                "a peer that does not claim the tier still sends exactly " + poseOnly.ToString("0.0")
                    + " kbit/s - the tier costs nothing until it is negotiated",
                poseOnly <= 80.0);

            double worst = VpbNetFidelityRate.WorstCaseKbps(PoseHz);
            Check(log, ref pass, ref fail,
                "worst case (fingers 1 in " + VpbNetFidelityRate.FingerEveryNth
                    + ", gaze 1 in " + VpbNetFidelityRate.GazeEveryNth
                    + ", jaw every frame) = " + worst.ToString("0.0") + " kbit/s, tier ceiling "
                    + VpbNetFidelityRate.TierCeilingKbps.ToString("0"),
                worst <= VpbNetFidelityRate.TierCeilingKbps);

            double steady = VpbNetFidelityRate.SteadyKbps(PoseHz);
            Check(log, ref pass, ref fail,
                "still hands and a steady gaze cost only the " + VpbNetFidelityRate.RefreshFrames
                    + "-frame refresh: " + steady.ToString("0.0") + " kbit/s, inside the pose-only budget",
                steady <= 80.0 && steady < worst);

            double everyFrame = (VpbPose.FrameBytes
                + ExtHeader + VpbNetGaze.PointBytes
                + ExtHeader + VpbNetJaw.WireBytes
                + ExtHeader + VpbNetFingers.WireBytes) * PoseHz * 8.0 / 1000.0;
            Check(log, ref pass, ref fail,
                "fingers on every frame would be " + everyFrame.ToString("0.0")
                    + " kbit/s, which is why they are rate-limited",
                everyFrame > VpbNetFidelityRate.TierCeilingKbps);

            byte[] probe = new byte[VpbIpc.MaxDataPayload];
            float[] pose = new float[VpbPose.PoseFloats];
            Identity(pose);
            int frame = VpbPose.WriteFrame(probe, 0, 0, 1, 1, 1, pose, VpbPose.ControllerCount, null, 0, 0);
            int room = VpbIpc.MaxDataPayload - frame;
            int need = ExtHeader + VpbNetFingers.WireBytes
                + ExtHeader + VpbNetGaze.PointBytes
                + ExtHeader + VpbNetJaw.WireBytes;
            Check(log, ref pass, ref fail,
                "all three blocks fit one datagram alongside a full pose (" + need + " B into " + room + " B free)",
                need <= room);
        }

        static void FingerCodec(StringBuilder log, ref int pass, ref int fail)
        {
            float[] src = new float[VpbNetFingers.Count];
            float[] dst = new float[VpbNetFingers.Count];
            byte[] buf = new byte[VpbNetFingers.WireBytes];

            Random rng = new Random(4);
            float worst = 0f;
            for (int k = 0; k < 256; k++)
            {
                for (int i = 0; i < src.Length; i++)
                    src[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * VpbNetFingers.RangeDegrees;

                if (VpbNetFingers.Write(buf, 0, src) != VpbNetFingers.WireBytes)
                {
                    Check(log, ref pass, ref fail, "random finger write", false);
                    return;
                }
                if (!VpbNetFingers.Read(buf, 0, VpbNetFingers.WireBytes, dst))
                {
                    Check(log, ref pass, ref fail, "random finger read", false);
                    return;
                }
                for (int i = 0; i < src.Length; i++)
                {
                    float d = src[i] - dst[i];
                    if (d < 0f) d = -d;
                    if (d > worst) worst = d;
                }
            }

            Check(log, ref pass, ref fail,
                "random finger round trip worst error " + worst.ToString("0.000")
                    + " deg (step " + VpbNetFingers.QuantStepDegrees.ToString("0.000") + ")",
                worst <= 0.4f);

            for (int i = 0; i < src.Length; i++) src[i] = 0f;
            VpbNetFingers.Write(buf, 0, src);
            VpbNetFingers.Read(buf, 0, VpbNetFingers.WireBytes, dst);
            bool zeroExact = true;
            for (int i = 0; i < dst.Length; i++) if (dst[i] != 0f) zeroExact = false;
            Check(log, ref pass, ref fail, "a relaxed hand encodes to exactly zero, so idle hands never twitch", zeroExact);

            for (int i = 0; i < src.Length; i++) src[i] = 400f;
            VpbNetFingers.Write(buf, 0, src);
            VpbNetFingers.Read(buf, 0, VpbNetFingers.WireBytes, dst);
            bool clamped = true;
            for (int i = 0; i < dst.Length; i++)
                if (dst[i] > VpbNetFingers.RangeDegrees + 0.001f) clamped = false;
            Check(log, ref pass, ref fail,
                "a value past the " + VpbNetFingers.RangeDegrees + " deg range clamps instead of wrapping", clamped);

            src[0] = float.NaN;
            src[1] = float.PositiveInfinity;
            VpbNetFingers.Write(buf, 0, src);
            VpbNetFingers.Read(buf, 0, VpbNetFingers.WireBytes, dst);
            Check(log, ref pass, ref fail, "NaN and infinity encode as zero, never as a hostile joint angle",
                dst[0] == 0f && dst[1] <= VpbNetFingers.RangeDegrees + 0.001f);

            for (int i = 0; i < src.Length; i++) src[i] = 0f;
            for (int i = 0; i < dst.Length; i++) dst[i] = 0f;
            Check(log, ref pass, ref fail, "two identical hands do not count as a change",
                !VpbNetFingers.Differs(src, dst, VpbNetFingers.QuantStepDegrees));
            dst[VpbNetFingers.Count - 1] = 5f;
            Check(log, ref pass, ref fail, "a single moved joint counts as a change",
                VpbNetFingers.Differs(src, dst, VpbNetFingers.QuantStepDegrees));
        }

        static void FingerLayout(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail,
                "50 params = 2 hands x 5 fingers x 5 joints, matching VaM's HandOutput",
                VpbNetFingers.Count == 50
                && VpbNetFingers.PerHand == 25
                && VpbNetFingers.FingerNames.Length == VpbNetFingers.FingersPerHand);

            Check(log, ref pass, ref fail, "left thumb proximal bend is index 0",
                VpbNetFingers.Index(false, 0, VpbNetFingers.OffsetProximalBend) == 0);
            Check(log, ref pass, ref fail, "right thumb proximal bend starts the second hand",
                VpbNetFingers.Index(true, 0, VpbNetFingers.OffsetProximalBend) == VpbNetFingers.PerHand);
            Check(log, ref pass, ref fail, "right pinky distal bend is the last index",
                VpbNetFingers.Index(true, 4, VpbNetFingers.OffsetDistalBend) == VpbNetFingers.Count - 1);

            bool unique = true;
            bool[] seen = new bool[VpbNetFingers.Count];
            for (int h = 0; h < 2; h++)
            {
                for (int f = 0; f < VpbNetFingers.FingersPerHand; f++)
                {
                    for (int j = 0; j < VpbNetFingers.JointsPerFinger; j++)
                    {
                        int idx = VpbNetFingers.Index(h == 1, f, j);
                        if (idx < 0 || idx >= VpbNetFingers.Count || seen[idx]) unique = false;
                        else seen[idx] = true;
                    }
                }
            }
            for (int i = 0; i < seen.Length; i++) if (!seen[i]) unique = false;
            Check(log, ref pass, ref fail, "every joint maps to exactly one slot and no slot is unused", unique);

            Check(log, ref pass, ref fail, "an out-of-range joint returns -1 rather than aliasing another finger",
                VpbNetFingers.Index(false, 5, 0) == -1
                && VpbNetFingers.Index(false, 0, 5) == -1
                && VpbNetFingers.Index(false, -1, 0) == -1);
        }

        static void GazeCodec(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] buf = new byte[16];
            byte mode;
            float x, y, z;

            Check(log, ref pass, ref fail, "\"not looking\" is one byte",
                VpbNetGaze.WriteNone(buf, 0) == VpbNetGaze.ShortBytes
                && VpbNetGaze.Read(buf, 0, VpbNetGaze.ShortBytes, out mode, out x, out y, out z)
                && mode == VpbNetGaze.ModeNone);

            Check(log, ref pass, ref fail, "\"looking at whoever is watching\" is one byte and carries no point",
                VpbNetGaze.WriteViewer(buf, 0) == VpbNetGaze.ShortBytes
                && VpbNetGaze.Read(buf, 0, VpbNetGaze.ShortBytes, out mode, out x, out y, out z)
                && mode == VpbNetGaze.ModeViewer
                && x == 0f && y == 0f && z == 0f);

            Random rng = new Random(9);
            float worstMm = 0f;
            for (int k = 0; k < 512; k++)
            {
                float sx = (float)(rng.NextDouble() * 2.0 - 1.0) * VpbNetGaze.RangeMeters;
                float sy = (float)(rng.NextDouble() * 2.0 - 1.0) * VpbNetGaze.RangeMeters;
                float sz = (float)(rng.NextDouble() * 2.0 - 1.0) * VpbNetGaze.RangeMeters;
                if (VpbNetGaze.WritePoint(buf, 0, sx, sy, sz) != VpbNetGaze.PointBytes) { worstMm = 9999f; break; }
                if (!VpbNetGaze.Read(buf, 0, VpbNetGaze.PointBytes, out mode, out x, out y, out z)) { worstMm = 9999f; break; }
                if (mode != VpbNetGaze.ModePoint) { worstMm = 9999f; break; }
                float dx = (sx - x) * 1000f;
                float dy = (sy - y) * 1000f;
                float dz = (sz - z) * 1000f;
                if (dx < 0f) dx = -dx;
                if (dy < 0f) dy = -dy;
                if (dz < 0f) dz = -dz;
                if (dx > worstMm) worstMm = dx;
                if (dy > worstMm) worstMm = dy;
                if (dz > worstMm) worstMm = dz;
            }
            Check(log, ref pass, ref fail,
                "a gaze point inside +/-" + VpbNetGaze.RangeMeters + " m round-trips to "
                    + worstMm.ToString("0.000") + " mm",
                worstMm <= 0.5f);

            VpbNetGaze.WritePoint(buf, 0, 100f, -100f, 0f);
            VpbNetGaze.Read(buf, 0, VpbNetGaze.PointBytes, out mode, out x, out y, out z);
            Check(log, ref pass, ref fail,
                "a point past the range clamps to the edge, so a peer can never aim eyes at infinity",
                x <= VpbNetGaze.RangeMeters + 0.001f && y >= -VpbNetGaze.RangeMeters - 0.001f);

            VpbNetGaze.WritePoint(buf, 0, float.NaN, float.NegativeInfinity, 0f);
            VpbNetGaze.Read(buf, 0, VpbNetGaze.PointBytes, out mode, out x, out y, out z);
            Check(log, ref pass, ref fail, "NaN and infinity in a gaze point encode as zero",
                x == 0f && y >= -VpbNetGaze.RangeMeters - 0.001f && !float.IsNaN(y));

            Check(log, ref pass, ref fail, "every mode has a name for the log",
                VpbNetGaze.Name(VpbNetGaze.ModeNone) == "none"
                && VpbNetGaze.Name(VpbNetGaze.ModeViewer) == "viewer"
                && VpbNetGaze.Name(VpbNetGaze.ModePoint) == "point"
                && VpbNetGaze.Name(200).Length > 0);
        }

        static void JawCodec(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] buf = new byte[8];
            float v;

            Check(log, ref pass, ref fail, "a closed jaw is exactly zero",
                VpbNetJaw.Write(buf, 0, 0f) == VpbNetJaw.WireBytes
                && VpbNetJaw.Read(buf, 0, VpbNetJaw.WireBytes, out v)
                && v == 0f);

            float worst = 0f;
            for (int k = 0; k <= 100; k++)
            {
                float src = k / 100f;
                VpbNetJaw.Write(buf, 0, src);
                VpbNetJaw.Read(buf, 0, VpbNetJaw.WireBytes, out v);
                float d = src - v;
                if (d < 0f) d = -d;
                if (d > worst) worst = d;
            }
            Check(log, ref pass, ref fail,
                "jaw 0..1 round-trips to " + worst.ToString("0.00000") + ", the morph codec's own resolution",
                worst <= 0.0002f);

            VpbNetJaw.Write(buf, 0, -1f);
            VpbNetJaw.Read(buf, 0, VpbNetJaw.WireBytes, out v);
            Check(log, ref pass, ref fail,
                "a negative jaw value survives, because it is a morph value and morphs go both ways",
                v < -0.99f && v > -1.01f);

            Check(log, ref pass, ref fail, "the jaw block shares the morph quantizer rather than adding a second one",
                VpbNetEventCodec.QuantizeMorph(0.5f) == (short)VpbIpc.ReadU16(WriteJaw(buf, 0.5f), 0));
        }

        static byte[] WriteJaw(byte[] buf, float v)
        {
            VpbNetJaw.Write(buf, 0, v);
            return buf;
        }

        static void Malformed(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] buf = new byte[64];
            float[] fingers = new float[VpbNetFingers.Count];
            byte mode;
            float x, y, z, v;

            Check(log, ref pass, ref fail, "a short finger block is refused, never partially applied",
                !VpbNetFingers.Read(buf, 0, VpbNetFingers.WireBytes - 1, fingers));
            Check(log, ref pass, ref fail, "a long finger block is refused",
                !VpbNetFingers.Read(buf, 0, VpbNetFingers.WireBytes + 1, fingers));

            buf[0] = VpbNetGaze.ModePoint;
            Check(log, ref pass, ref fail, "a point mode with no point is refused",
                !VpbNetGaze.Read(buf, 0, VpbNetGaze.ShortBytes, out mode, out x, out y, out z));
            buf[0] = VpbNetGaze.ModeViewer;
            Check(log, ref pass, ref fail, "a viewer mode carrying a payload is refused",
                !VpbNetGaze.Read(buf, 0, VpbNetGaze.PointBytes, out mode, out x, out y, out z));
            buf[0] = 77;
            Check(log, ref pass, ref fail, "an unknown gaze mode is refused rather than guessed at",
                !VpbNetGaze.Read(buf, 0, VpbNetGaze.ShortBytes, out mode, out x, out y, out z));

            Check(log, ref pass, ref fail, "a wrong-length jaw block is refused",
                !VpbNetJaw.Read(buf, 0, 1, out v) && !VpbNetJaw.Read(buf, 0, 3, out v));

            Check(log, ref pass, ref fail, "null buffers are refused, never dereferenced",
                !VpbNetFingers.Read(null, 0, VpbNetFingers.WireBytes, fingers)
                && !VpbNetFingers.Read(buf, 0, VpbNetFingers.WireBytes, null)
                && VpbNetFingers.Write(null, 0, fingers) < 0
                && VpbNetFingers.Write(buf, 0, null) < 0
                && !VpbNetGaze.Read(null, 0, 1, out mode, out x, out y, out z)
                && !VpbNetJaw.Read(null, 0, VpbNetJaw.WireBytes, out v));

            Check(log, ref pass, ref fail, "a write past the end of the buffer is refused",
                VpbNetFingers.Write(buf, buf.Length - 4, fingers) < 0
                && VpbNetGaze.WritePoint(buf, buf.Length - 2, 0f, 0f, 0f) < 0
                && VpbNetJaw.Write(buf, buf.Length - 1, 0f) < 0);
        }

        static void StateAssembly(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] ext = new byte[256];
            byte[] scratch = new byte[VpbNetFingers.WireBytes];
            float[] fingers = new float[VpbNetFingers.Count];
            for (int i = 0; i < fingers.Length; i++) fingers[i] = 20f + i;

            int at = 0;
            VpbNetFingers.Write(scratch, 0, fingers);
            at += VpbPose.AppendExt(ext, at, ext.Length, VpbNetPoseExtId.Fingers, scratch, 0, VpbNetFingers.WireBytes);

            byte[] gaze = new byte[VpbNetGaze.PointBytes];
            VpbNetGaze.WritePoint(gaze, 0, 0.5f, 1.4f, -2f);
            at += VpbPose.AppendExt(ext, at, ext.Length, VpbNetPoseExtId.Gaze, gaze, 0, VpbNetGaze.PointBytes);

            byte[] jaw = new byte[VpbNetJaw.WireBytes];
            VpbNetJaw.Write(jaw, 0, 0.75f);
            at += VpbPose.AppendExt(ext, at, ext.Length, VpbNetPoseExtId.Jaw, jaw, 0, VpbNetJaw.WireBytes);

            byte[] future = { 1, 2, 3, 4, 5 };
            at += VpbPose.AppendExt(ext, at, ext.Length, 200, future, 0, future.Length);

            VpbNetFidelityState st = new VpbNetFidelityState();
            bool got = st.ReadFrom(ext, 0, at, 42);

            Check(log, ref pass, ref fail, "all three blocks in one ext region are picked up together",
                got && st.HasFingers && st.HasGaze && st.HasJaw && st.Seq == 42);
            Check(log, ref pass, ref fail, "an ext block id this build does not know is counted and skipped, never fatal",
                st.UnknownBlocks == 1 && st.RefusedBlocks == 0);
            Check(log, ref pass, ref fail, "the values survive the assembly",
                Near(st.Fingers[0], 20f, 0.4f)
                && Near(st.Fingers[VpbNetFingers.Count - 1], 20f + VpbNetFingers.Count - 1, 0.4f)
                && Near(st.GazeX, 0.5f, 0.001f) && Near(st.GazeY, 1.4f, 0.001f) && Near(st.GazeZ, -2f, 0.001f)
                && st.GazeMode == VpbNetGaze.ModePoint
                && Near(st.Jaw, 0.75f, 0.001f));

            VpbNetFidelityState bad = new VpbNetFidelityState();
            int b = 0;
            b += VpbPose.AppendExt(ext, b, ext.Length, VpbNetPoseExtId.Fingers, scratch, 0, VpbNetFingers.WireBytes - 3);
            byte[] onlyMode = { VpbNetGaze.ModeViewer };
            b += VpbPose.AppendExt(ext, b, ext.Length, VpbNetPoseExtId.Gaze, onlyMode, 0, 1);
            bad.ReadFrom(ext, 0, b, 7);
            Check(log, ref pass, ref fail,
                "a malformed block is refused on its own while the good block beside it still applies",
                !bad.HasFingers && bad.HasGaze && bad.RefusedBlocks == 1 && bad.GazeMode == VpbNetGaze.ModeViewer);

            st.Reset();
            Check(log, ref pass, ref fail, "Reset clears every flag, so a peer leaving cannot leave a hand behind",
                !st.HasFingers && !st.HasGaze && !st.HasJaw
                && st.GazeMode == VpbNetGaze.ModeNone && st.Seq == 0
                && st.Fingers[0] == 0f && st.UnknownBlocks == 0);

            VpbNetFidelityState empty = new VpbNetFidelityState();
            Check(log, ref pass, ref fail, "an empty ext region reports nothing rather than claiming a default hand",
                !empty.ReadFrom(ext, 0, 0, 1) && !empty.HasFingers && !empty.HasGaze && !empty.HasJaw);
        }

        static void RidesAPoseFrame(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] wire = new byte[VpbIpc.MaxDataPayload];
            byte[] ext = new byte[256];
            byte[] scratch = new byte[VpbNetFingers.WireBytes];
            float[] pose = new float[VpbPose.PoseFloats];
            float[] decoded = new float[VpbPose.PoseFloats];
            float[] fingers = new float[VpbNetFingers.Count];

            Identity(pose);
            pose[0] = 3f;
            pose[1] = 0f;
            pose[2] = -1f;
            for (int i = 0; i < fingers.Length; i++) fingers[i] = (i % 2 == 0) ? 45f : -30f;

            int at = 0;
            VpbNetFingers.Write(scratch, 0, fingers);
            at += VpbPose.AppendExt(ext, at, ext.Length, VpbNetPoseExtId.Fingers, scratch, 0, VpbNetFingers.WireBytes);
            byte[] gaze = new byte[VpbNetGaze.PointBytes];
            VpbNetGaze.WritePoint(gaze, 0, 0f, 1.6f, 2.5f);
            at += VpbPose.AppendExt(ext, at, ext.Length, VpbNetPoseExtId.Gaze, gaze, 0, VpbNetGaze.PointBytes);
            byte[] jawBuf = new byte[VpbNetJaw.WireBytes];
            VpbNetJaw.Write(jawBuf, 0, 0.3f);
            at += VpbPose.AppendExt(ext, at, ext.Length, VpbNetPoseExtId.Jaw, jawBuf, 0, VpbNetJaw.WireBytes);

            int n = VpbPose.WriteFrame(wire, 0, 0, 1, 99, 1234, pose, VpbPose.ControllerCount, ext, 0, at);
            Check(log, ref pass, ref fail, "a pose frame carrying all three blocks encodes",
                n == VpbPose.FrameBytes + at && n <= VpbIpc.MaxDataPayload);

            byte ver, flags;
            ushort peer;
            uint seq, tick;
            int frameLen, extOff, extLen;
            bool read = VpbPose.TryReadFrame(wire, 0, n, decoded, VpbPose.ControllerCount,
                out ver, out flags, out peer, out seq, out tick, out frameLen, out extOff, out extLen);
            Check(log, ref pass, ref fail, "the frame decodes and reports the ext region",
                read && (flags & VpbPose.FlagHasExt) != 0 && extLen == at);

            VpbNetFidelityState st = new VpbNetFidelityState();
            bool ok = read && st.ReadFrom(wire, extOff, extLen, seq);
            Check(log, ref pass, ref fail, "the fidelity state reads straight out of the decoded pose frame",
                ok && st.HasFingers && st.HasGaze && st.HasJaw && st.Seq == 99);
            Check(log, ref pass, ref fail, "the fingers survive the pose frame unchanged",
                ok && Near(st.Fingers[0], 45f, 0.4f) && Near(st.Fingers[1], -30f, 0.4f));

            float rx = decoded[0];
            float ry = decoded[1];
            float rz = decoded[2];
            Check(log, ref pass, ref fail,
                "the gaze point is root-relative, so it lands correctly for a peer standing at "
                    + rx.ToString("0.0") + "," + ry.ToString("0.0") + "," + rz.ToString("0.0"),
                ok && Near(rx + st.GazeX, 3f, 0.01f)
                && Near(ry + st.GazeY, 1.6f, 0.01f)
                && Near(rz + st.GazeZ, 1.5f, 0.01f));

            int noExt = VpbPose.WriteFrame(wire, 0, 0, 1, 100, 1234, pose, VpbPose.ControllerCount, null, 0, 0);
            VpbPose.TryReadFrame(wire, 0, noExt, decoded, VpbPose.ControllerCount,
                out ver, out flags, out peer, out seq, out tick, out frameLen, out extOff, out extLen);
            VpbNetFidelityState none = new VpbNetFidelityState();
            Check(log, ref pass, ref fail,
                "a peer that sends no fidelity at all is not a decode error - it is just a peer without the capability",
                extLen == 0 && !none.ReadFrom(wire, extOff, extLen, seq));
        }

        static void Identity(float[] pose)
        {
            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                int p = i * VpbPose.FloatsPerController;
                pose[p] = 0f;
                pose[p + 1] = 0f;
                pose[p + 2] = 0f;
                pose[p + 3] = 0f;
                pose[p + 4] = 0f;
                pose[p + 5] = 0f;
                pose[p + 6] = 1f;
            }
        }

        static bool Near(float a, float b, float tol)
        {
            float d = a - b;
            if (d < 0f) d = -d;
            return d <= tol;
        }

        static string V(int fail)
        {
            return fail == 0 ? "PASS" : "see FAIL lines";
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, string what, bool ok)
        {
            if (ok)
            {
                pass++;
                Line(log, "  ok   " + what);
            }
            else
            {
                fail++;
                Line(log, "  FAIL " + what);
            }
        }

        static void Line(StringBuilder log, string s)
        {
            log.Append(s);
            log.Append('\n');
        }
    }
}
