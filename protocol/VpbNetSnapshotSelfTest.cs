using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetSnapshotSelfTest
    {
        const double TickMs = 1000.0 / 45.0;

        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(4096);
            bool ok = Run(sb);
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== snapshot buffer + applier core self-test =====");

            Accuracy(log, ref pass, ref fail);
            ExactAtSnapshots(log, ref pass, ref fail);
            Reordering(log, ref pass, ref fail);
            Duplicates(log, ref pass, ref fail);
            Loss(log, ref pass, ref fail);
            ExtrapolateAndFreeze(log, ref pass, ref fail);
            FreezeContinuity(log, ref pass, ref fail);
            Hostile(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 fidelity   interpolation tracks the source curve      : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 disorder   reorder recovered, duplicates refused      : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 loss       3% loss invisible, gap extrapolates <=150ms: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 no snap    freeze is continuous, never teleports      : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end snapshot buffer self-test =====");
            return fail == 0;
        }

        static void Curve(double tMs, float[] pose)
        {
            double t = tMs / 1000.0;
            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                int p = i * VpbPose.FloatsPerController;
                double phase = t * 2.0 + i * 0.37;
                pose[p] = (float)(Math.Sin(phase) * 0.6);
                pose[p + 1] = (float)(1.2 + Math.Sin(phase * 1.3) * 0.25);
                pose[p + 2] = (float)(Math.Cos(phase) * 0.6);

                double ang = phase * 0.5;
                double s = Math.Sin(ang * 0.5);
                double axis = 1.0 / Math.Sqrt(3.0);
                pose[p + 3] = (float)(s * axis);
                pose[p + 4] = (float)(s * axis);
                pose[p + 5] = (float)(s * axis);
                pose[p + 6] = (float)Math.Cos(ang * 0.5);
            }
        }

        static void Accuracy(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];
            float[] got = new float[VpbPose.PoseFloats];
            float[] want = new float[VpbPose.PoseFloats];

            double maxMm = 0.0;
            double maxDeg = 0.0;
            uint seq = 0;
            double produced = 0.0;
            double render = -60.0;
            int samples = 0;

            for (int frame = 0; frame < 1800; frame++)
            {
                double now = frame * (1000.0 / 90.0);
                while (produced <= now)
                {
                    Curve(produced, src);
                    buf.Insert(seq, produced, src, 0);
                    seq++;
                    produced += TickMs;
                }

                render = now - 60.0;
                if (render < 0.0) continue;
                if (buf.Sample(render, got) != VpbNetSampleState.Interpolated) continue;

                Curve(render, want);
                double mm = MaxPosErrMm(got, want);
                double deg = MaxAngErrDeg(got, want);
                if (mm > maxMm) maxMm = mm;
                if (deg > maxDeg) maxDeg = deg;
                samples++;
            }

            Check(log, ref pass, ref fail, samples > 1000 && maxMm < 3.0 && maxDeg < 0.6,
                "interpolation tracks a 2 rad/s curve to " + F(maxMm, 3) + "mm / " + F(maxDeg, 4)
                    + "deg over " + samples + " frames",
                "interpolation error " + F(maxMm, 3) + "mm / " + F(maxDeg, 4) + "deg over " + samples + " frames");
        }

        static void ExactAtSnapshots(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];
            float[] got = new float[VpbPose.PoseFloats];
            float[] want = new float[VpbPose.PoseFloats];

            for (uint i = 0; i < 10; i++)
            {
                Curve(i * TickMs, src);
                buf.Insert(i, i * TickMs, src, 0);
            }

            double maxMm = 0.0;
            for (int i = 1; i < 9; i++)
            {
                buf.Sample(i * TickMs, got);
                Curve(i * TickMs, want);
                double mm = MaxPosErrMm(got, want);
                if (mm > maxMm) maxMm = mm;
            }

            Check(log, ref pass, ref fail, maxMm < 0.001,
                "sampling exactly at a snapshot returns that snapshot (" + F(maxMm, 6) + "mm)",
                "sampling at a snapshot instant drifted " + F(maxMm, 6) + "mm");
        }

        static void Reordering(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];
            float[] got = new float[VpbPose.PoseFloats];
            float[] want = new float[VpbPose.PoseFloats];

            uint[] arrival = { 0, 2, 1, 3, 5, 4, 6, 8, 7, 9, 10, 12, 11, 13 };
            int naiveKept = 0;
            uint naiveNewest = 0;
            bool first = true;

            for (int i = 0; i < arrival.Length; i++)
            {
                uint s = arrival[i];
                Curve(s * TickMs, src);
                buf.Insert(s, s * TickMs, src, 0);

                if (first || s > naiveNewest) { naiveKept++; naiveNewest = s; first = false; }
            }

            double maxMm = 0.0;
            for (int i = 1; i < 13; i++)
            {
                double t = (i + 0.5) * TickMs;
                buf.Sample(t, got);
                Curve(t, want);
                double mm = MaxPosErrMm(got, want);
                if (mm > maxMm) maxMm = mm;
            }

            Check(log, ref pass, ref fail, buf.Inserted == arrival.Length && buf.Reordered > 0,
                "reordered arrivals are inserted in order, not dropped: kept " + buf.Inserted + "/"
                    + arrival.Length + " (" + buf.Reordered + " landed out of order); a naive drop-by-sequence keeps "
                    + naiveKept,
                "reordering lost frames: kept " + buf.Inserted + "/" + arrival.Length);

            Check(log, ref pass, ref fail, maxMm < 3.0,
                "interpolation across the reordered span stays clean (" + F(maxMm, 3) + "mm)",
                "reordered span interpolated to " + F(maxMm, 3) + "mm");
        }

        static void Duplicates(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];

            for (uint i = 0; i < 5; i++)
            {
                Curve(i * TickMs, src);
                buf.Insert(i, i * TickMs, src, 0);
            }
            Curve(2 * TickMs, src);
            bool dup = buf.Insert(2, 2 * TickMs, src, 0);

            Check(log, ref pass, ref fail, !dup && buf.RejectedDuplicate == 1 && buf.Count == 5,
                "a duplicate sequence is refused (count " + buf.Count + ")",
                "duplicate accepted: count " + buf.Count + ", rejected " + buf.RejectedDuplicate);
        }

        static void Loss(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];
            float[] got = new float[VpbPose.PoseFloats];
            float[] want = new float[VpbPose.PoseFloats];
            Random rng = new Random(4);

            double maxMm = 0.0;
            int frozen = 0;
            int extrapolated = 0;
            int interpolated = 0;
            uint seq = 0;
            double produced = 0.0;
            int dropped = 0;

            for (int frame = 0; frame < 1800; frame++)
            {
                double now = frame * (1000.0 / 90.0);
                while (produced <= now)
                {
                    if (rng.NextDouble() >= 0.03)
                    {
                        Curve(produced, src);
                        buf.Insert(seq, produced, src, 0);
                    }
                    else dropped++;
                    seq++;
                    produced += TickMs;
                }

                double render = now - 60.0;
                if (render < 0.0) continue;

                VpbNetSampleState st = buf.Sample(render, got);
                if (st == VpbNetSampleState.Interpolated) interpolated++;
                else if (st == VpbNetSampleState.Extrapolated) extrapolated++;
                else if (st == VpbNetSampleState.Frozen) frozen++;

                Curve(render, want);
                double mm = MaxPosErrMm(got, want);
                if (mm > maxMm) maxMm = mm;
            }

            Check(log, ref pass, ref fail, frozen == 0 && extrapolated == 0 && maxMm < 12.0,
                "3% loss (" + dropped + " frames) is invisible behind a 60ms buffer: "
                    + interpolated + " interpolated, 0 extrapolated, 0 frozen, max err " + F(maxMm, 2) + "mm",
                "3% loss leaked through: interp " + interpolated + " extrap " + extrapolated
                    + " frozen " + frozen + " err " + F(maxMm, 2) + "mm");
        }

        static void ExtrapolateAndFreeze(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];
            float[] got = new float[VpbPose.PoseFloats];

            for (uint i = 0; i < 8; i++)
            {
                Curve(i * TickMs, src);
                buf.Insert(i, i * TickMs, src, 0);
            }
            double last = 7 * TickMs;

            int extrap = 0;
            int frozenCount = 0;
            bool monotonic = true;
            bool stateOrderOk = true;
            double prevDisp = -1.0;
            double firstFrozenDisp = -1.0;
            float[] atLast = new float[VpbPose.PoseFloats];
            buf.Sample(last, atLast);

            for (int i = 0; i <= 400; i++)
            {
                double ahead = i;
                VpbNetSampleState st = buf.Sample(last + ahead, got);
                double disp = PosDelta(got, atLast);

                if (ahead <= VpbNetSnapshotBuffer.MaxExtrapolateMs)
                {
                    if (st != VpbNetSampleState.Extrapolated && ahead > 0.0) stateOrderOk = false;
                }
                else
                {
                    if (st != VpbNetSampleState.Frozen) stateOrderOk = false;
                    if (firstFrozenDisp < 0.0) firstFrozenDisp = disp;
                    else if (Math.Abs(disp - firstFrozenDisp) > 0.000001) monotonic = false;
                    frozenCount++;
                    continue;
                }

                if (disp < prevDisp - 0.000001) monotonic = false;
                prevDisp = disp;
                extrap++;
            }

            Check(log, ref pass, ref fail, stateOrderOk,
                "gap walks Extrapolated for <=150ms then Frozen (" + extrap + " extrapolated, "
                    + frozenCount + " frozen)",
                "state machine wrong across the gap");
            Check(log, ref pass, ref fail, monotonic,
                "extrapolated displacement never reverses, and freeze holds it still",
                "extrapolation reversed direction or the frozen pose drifted");
        }

        static void FreezeContinuity(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];
            float[] a = new float[VpbPose.PoseFloats];
            float[] b = new float[VpbPose.PoseFloats];

            for (uint i = 0; i < 8; i++)
            {
                Curve(i * TickMs, src);
                buf.Insert(i, i * TickMs, src, 0);
            }
            double last = 7 * TickMs;
            double m = VpbNetSnapshotBuffer.MaxExtrapolateMs;

            buf.Sample(last + m - 0.5, a);
            buf.Sample(last + m + 0.5, b);
            double jumpMm = PosDelta(a, b) * 1000.0;

            buf.Sample(last + 0.001, a);
            buf.Sample(last, b);
            double seamMm = PosDelta(a, b) * 1000.0;

            buf.Sample(last + m - 20.0, a);
            buf.Sample(last + m, b);
            double tailMm = PosDelta(a, b) * 1000.0;
            buf.Sample(last + 0.0, a);
            buf.Sample(last + 20.0, b);
            double headMm = PosDelta(a, b) * 1000.0;

            Check(log, ref pass, ref fail, jumpMm < 0.5,
                "no teleport at the freeze boundary (" + F(jumpMm, 4) + "mm across 1ms)",
                "freeze boundary teleports " + F(jumpMm, 3) + "mm");
            Check(log, ref pass, ref fail, seamMm < 0.5,
                "no seam where interpolation hands over to extrapolation (" + F(seamMm, 4) + "mm)",
                "interp/extrap seam jumps " + F(seamMm, 3) + "mm");
            Check(log, ref pass, ref fail, tailMm < headMm * 0.5,
                "velocity fades into the freeze: last 20ms moves " + F(tailMm, 2)
                    + "mm vs " + F(headMm, 2) + "mm in the first 20ms",
                "velocity does not fade: tail " + F(tailMm, 2) + "mm vs head " + F(headMm, 2) + "mm");
        }

        static void Hostile(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSnapshotBuffer buf = new VpbNetSnapshotBuffer();
            float[] src = new float[VpbPose.PoseFloats];
            float[] got = new float[VpbPose.PoseFloats];

            for (uint i = 0; i < 6; i++)
            {
                Curve(i * TickMs, src);
                buf.Insert(i, i * TickMs, src, 0);
            }

            Curve(0.0, src);
            bool far = buf.Insert(999, 1000000.0, src, 0);
            bool nan = buf.Insert(1000, double.NaN, src, 0);

            VpbNetSampleState st = buf.Sample(3 * TickMs, got);

            Check(log, ref pass, ref fail,
                !far && !nan && buf.RejectedFuture == 1 && st == VpbNetSampleState.Interpolated,
                "a far-future or NaN stamp is refused and cannot stall the buffer",
                "hostile stamp accepted: far=" + far + " nan=" + nan + " state=" + st);

            VpbNetSnapshotBuffer full = new VpbNetSnapshotBuffer();
            for (uint i = 0; i < VpbNetSnapshotBuffer.Capacity + 20; i++)
            {
                Curve(i * TickMs, src);
                full.Insert(i, i * TickMs, src, 0);
            }
            Curve(0.0, src);
            bool ancient = full.Insert(0, 0.0, src, 0);

            Check(log, ref pass, ref fail,
                !ancient && full.Count == VpbNetSnapshotBuffer.Capacity && full.Evicted == 20,
                "a full buffer evicts oldest and refuses an ancient frame (count "
                    + full.Count + ", evicted " + full.Evicted + ")",
                "buffer overflow wrong: count " + full.Count + " evicted " + full.Evicted
                    + " ancient accepted " + ancient);
        }

        static double PosDelta(float[] a, float[] b)
        {
            double max = 0.0;
            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                int p = i * VpbPose.FloatsPerController;
                double dx = a[p] - b[p];
                double dy = a[p + 1] - b[p + 1];
                double dz = a[p + 2] - b[p + 2];
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > max) max = d;
            }
            return max;
        }

        static double MaxPosErrMm(float[] a, float[] b)
        {
            return PosDelta(a, b) * 1000.0;
        }

        static double MaxAngErrDeg(float[] a, float[] b)
        {
            double max = 0.0;
            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                int p = i * VpbPose.FloatsPerController;
                double dot = a[p + 3] * b[p + 3] + a[p + 4] * b[p + 4] + a[p + 5] * b[p + 5] + a[p + 6] * b[p + 6];
                if (dot < 0.0) dot = -dot;
                if (dot > 1.0) dot = 1.0;
                double deg = 2.0 * Math.Acos(dot) * (180.0 / Math.PI);
                if (deg > max) max = deg;
            }
            return max;
        }

        static string F(double v, int decimals)
        {
            return v.ToString("F" + decimals.ToString());
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, bool ok, string passText, string failText)
        {
            if (ok)
            {
                pass++;
                Line(log, "PASS  " + passText);
            }
            else
            {
                fail++;
                Line(log, "FAIL  " + failText);
            }
        }

        static void Line(StringBuilder log, string s)
        {
            if (log == null) return;
            log.Append(s);
            log.Append('\n');
        }
    }
}
