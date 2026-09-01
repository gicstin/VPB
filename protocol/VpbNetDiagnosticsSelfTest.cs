using System;
using System.Globalization;
using System.Text;

namespace VpbNet
{
    public static class VpbNetDiagnosticsSelfTest
    {
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

            Line(log, "===== diagnostics overlay self-test =====");

            NumberFormatting(log, ref pass, ref fail);
            ChangeDetection(log, ref pass, ref fail);
            Content(log, ref pass, ref fail);
            Extremes(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/3 format     fixed-point matches ToString, no boxing  : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/3 idle       unchanged stats rebuild nothing          : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/3 content    every 1.9 field is present               : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end diagnostics self-test =====");
            return fail == 0;
        }

        static void NumberFormatting(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetDiagnostics d = new VpbNetDiagnostics();
            StringBuilder sb = new StringBuilder(64);

            double[] values = { 0.0, 0.0, 0.0, 1.0, 1.24, 1.25, 9.99, 10.0, 123.456, 123.456, 123.456, 1000.0, 234.4, 234.6, 0.125, 295.649 };
            int[] decimals = { 0, 1, 2, 1, 1, 1, 1, 0, 0, 1, 2, 0, 0, 0, 2, 1 };
            string[] expect = { "0", "0.0", "0.00", "1.0", "1.2", "1.3", "10.0", "10", "123", "123.5", "123.46", "1000", "234", "235", "0.13", "295.6" };

            int mismatches = 0;
            string firstBad = string.Empty;
            for (int i = 0; i < values.Length; i++)
            {
                sb.Length = 0;
                d.AppendFixed(sb, values[i], decimals[i]);
                string got = sb.ToString();
                if (got == expect[i]) continue;
                mismatches++;
                if (firstBad.Length == 0)
                    firstBad = values[i].ToString(CultureInfo.InvariantCulture) + " F" + decimals[i]
                        + ": got " + got + " want " + expect[i];
            }

            sb.Length = 0;
            d.AppendFixed(sb, -12.34, 1);
            string neg = sb.ToString();

            sb.Length = 0;
            d.AppendInt(sb, 0);
            d.AppendInt(sb, 7);
            d.AppendInt(sb, 1234567);
            d.AppendInt(sb, -42);
            string ints = sb.ToString();

            Check(log, ref pass, ref fail, mismatches == 0,
                "fixed-point formatting is exact on " + values.Length
                    + " cases including ties (1.25 -> 1.3, half away from zero) - deliberately NOT compared against ToString, which rounds half to even on .NET 8 and half away from zero on Mono",
                mismatches + " formatting mismatches, first: " + firstBad);

            Check(log, ref pass, ref fail, neg == "-12.3" && ints == "071234567-42",
                "signs and integers append digit by digit (" + neg + ", " + ints + ")",
                "digit append wrong: neg=" + neg + " ints=" + ints);
        }

        static void ChangeDetection(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetDiagnostics d = new VpbNetDiagnostics();
            d.State = VpbNetSessionState.Running;
            d.RttMs = 12.34;
            d.BufferDepth = 5;

            bool first = d.HasChanged();
            bool second = d.HasChanged();
            bool third = d.HasChanged();

            d.RttMs = 12.341;
            bool tiny = d.HasChanged();

            d.RttMs = 12.5;
            bool real = d.HasChanged();

            d.BufferDepth = 6;
            bool depth = d.HasChanged();

            Check(log, ref pass, ref fail, first && !second && !third,
                "the first sample reports changed, then a static link reports unchanged - the overlay never touches Text.text while idle",
                "change detection wrong: first=" + first + " second=" + second + " third=" + third);

            Check(log, ref pass, ref fail, !tiny && real && depth,
                "sub-0.1ms rtt noise does not trigger a rebuild, but 12.34 -> 12.5 and a depth change do",
                "quantization wrong: tiny=" + tiny + " real=" + real + " depth=" + depth);
        }

        static void Content(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetDiagnostics d = new VpbNetDiagnostics();
            d.State = VpbNetSessionState.Running;
            d.TransportMode = "LAN";
            d.PeerName = "Amelia";
            d.PeerCount = 1;
            d.RttMs = 295.6;
            d.JitterMs = 22.2;
            d.DelayMs = 234.4;
            d.TransitMs = 123.4;
            d.BufferMs = 111.0;
            d.BufferDepth = 32;
            d.LossPercent = 1.76;
            d.FrameAgeMs = 41.0;
            d.ServiceLocalMs = 118.4;
            d.ServicePeerMs = 121.7;
            d.SamplerUs = 10.0;
            d.ApplierUs = 15.0;
            d.Stalls = 1;
            d.Reconnects = 0;
            d.Interpolated = 997;
            d.Extrapolated = 2;
            d.Frozen = 1;

            StringBuilder sb = new StringBuilder(512);
            d.Format(sb);
            string text = sb.ToString();

            string[] required =
            {
                "running", "LAN", "Amelia",
                "rtt 295.6ms", "jitter 22.2ms", "loss 1.76%",
                "delay 234ms", "transit 123", "depth 32", "age 41ms",
                "sampler 10.0us", "applier 15.0us", "service  yours 118.4ms", "peer 121.7ms",
                "stalls 1", "rejoins 0", "interp 99.7% of 1000", "extrap 2", "frozen 1"
            };

            int missing = 0;
            string firstMissing = string.Empty;
            for (int i = 0; i < required.Length; i++)
            {
                if (text.IndexOf(required[i], StringComparison.Ordinal) >= 0) continue;
                missing++;
                if (firstMissing.Length == 0) firstMissing = required[i];
            }

            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') lines++;
            }

            Check(log, ref pass, ref fail, missing == 0,
                "every field the plan names is rendered: rtt, jitter, buffer depth, loss, frame age, transport mode, sampler/applier us ("
                    + lines + " lines, " + text.Length + " chars)",
                missing + " required fields missing, first: " + firstMissing);

            d.State = VpbNetSessionState.Dropped;
            d.Reason = VpbNetDropReason.ContentMismatch;
            sb.Length = 0;
            d.Format(sb);
            string dropped = sb.ToString();

            Check(log, ref pass, ref fail,
                dropped.IndexOf("DROPPED", StringComparison.Ordinal) >= 0
                    && dropped.IndexOf("missing packages", StringComparison.Ordinal) >= 0,
                "a drop shows the state in caps and names the reason on its own line",
                "drop rendering wrong: " + dropped);

            VpbNetDiagnostics fresh = new VpbNetDiagnostics();
            sb.Length = 0;
            fresh.Format(sb);
            string idle = sb.ToString();

            Check(log, ref pass, ref fail,
                fresh.AppliedFrames == 0 && fresh.InterpolatedPercent == 0.0
                    && idle.IndexOf("interp 0.0% of 0", StringComparison.Ordinal) >= 0,
                "a session that has applied nothing yet reads 0.0% instead of dividing by zero",
                "empty applied-frame ratio rendered as: " + idle);

            VpbNetDiagnostics counted = new VpbNetDiagnostics();
            counted.Interpolated = 3;
            counted.Extrapolated = 1;
            counted.HasChanged();
            bool sawFirst = true;
            counted.Interpolated = 4;
            bool sawInterpChange = counted.HasChanged();

            Check(log, ref pass, ref fail,
                sawFirst && sawInterpChange && counted.AppliedFrames == 5,
                "an interpolated-frame count is part of the change snapshot, so the overlay redraws when only it moves",
                "interpolated count is missing from the change snapshot");
        }

        static void Extremes(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetDiagnostics d = new VpbNetDiagnostics();
            StringBuilder sb = new StringBuilder(512);

            d.RttMs = double.NaN;
            d.JitterMs = double.PositiveInfinity;
            d.LossPercent = -1.0;
            d.DelayMs = 1.0e18;
            d.BufferDepth = int.MaxValue;
            d.TransportMode = null;
            d.PeerName = null;

            bool threw = false;
            try
            {
                d.HasChanged();
                d.Format(sb);
            }
            catch
            {
                threw = true;
            }

            string text = sb.ToString();
            bool hasNanMarker = text.IndexOf("--", StringComparison.Ordinal) >= 0;

            Check(log, ref pass, ref fail, !threw && text.Length > 0 && hasNanMarker,
                "NaN, infinity, negative and absurd values render as text instead of throwing inside a UI callback",
                "extreme values broke the overlay: threw=" + threw + " len=" + text.Length);
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
