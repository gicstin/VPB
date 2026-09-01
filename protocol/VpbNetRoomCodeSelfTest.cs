using System;
using System.Collections.Generic;
using System.Text;

namespace VpbNet
{
    public static class VpbNetRoomCodeSelfTest
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

            Line(log, "===== room code self-test =====");

            EntropyShape(log, ref pass, ref fail);
            RoundTrips(log, ref pass, ref fail);
            Confusables(log, ref pass, ref fail);
            Separators(log, ref pass, ref fail);
            LengthRefused(log, ref pass, ref fail);
            AlphabetRefused(log, ref pass, ref fail);
            Uniform(log, ref pass, ref fail);
            EveryFaultActionable(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 entropy    " + VpbNetRoomCode.EntropyBits + " bits in " + VpbNetRoomCode.Chars + " chars, uniform : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 typing     hyphens, spaces and case are free         : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 confusable I/L->1 and O->0, U never accepted         : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 messages   every refusal names cause and fix         : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end room code self-test =====");
            return fail == 0;
        }

        static void EntropyShape(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            for (int i = 0; i < e.Length; i++) e[i] = (byte)(i * 37 + 11);

            string code = VpbNetRoomCode.FromEntropy(e, 0);
            Check(log, ref pass, ref fail, "entropy produces " + VpbNetRoomCode.Chars + " chars",
                code != null && code.Length == VpbNetRoomCode.Chars);
            Check(log, ref pass, ref fail, "entropy output is well formed", VpbNetRoomCode.IsWellFormed(code));
            Check(log, ref pass, ref fail, "short buffer refused, never truncated",
                VpbNetRoomCode.FromEntropy(new byte[VpbNetRoomCode.EntropyBytes - 1], 0) == null);
            Check(log, ref pass, ref fail, "null buffer refused", VpbNetRoomCode.FromEntropy(null, 0) == null);
            Check(log, ref pass, ref fail, "offset past end refused",
                VpbNetRoomCode.FromEntropy(e, 1) == null);
            Check(log, ref pass, ref fail, VpbNetRoomCode.Chars + " chars carry " + VpbNetRoomCode.EntropyBits + " bits",
                VpbNetRoomCode.Chars * 5 == VpbNetRoomCode.EntropyBits);
        }

        static void RoundTrips(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            bool allRoundTrip = true;
            bool allGroupRoundTrip = true;
            for (int seed = 0; seed < 512; seed++)
            {
                for (int i = 0; i < e.Length; i++) e[i] = (byte)((seed * 131 + i * 17) & 0xFF);
                string code = VpbNetRoomCode.FromEntropy(e, 0);
                if (code == null || VpbNetRoomCode.Normalize(code) != code) allRoundTrip = false;

                string grouped = VpbNetRoomCode.Group(code);
                if (VpbNetRoomCode.Normalize(grouped) != code) allGroupRoundTrip = false;
            }
            Check(log, ref pass, ref fail, "512 generated codes normalize to themselves", allRoundTrip);
            Check(log, ref pass, ref fail, "grouped display form normalizes back", allGroupRoundTrip);

            string g = VpbNetRoomCode.Group("K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "group inserts hyphens every " + VpbNetRoomCode.GroupSize,
                g == "K7M2-QB94-XTVR");
        }

        static void Confusables(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "I reads as 1", VpbNetRoomCode.Normalize("I7M2QB94XTVR") == "17M2QB94XTVR");
            Check(log, ref pass, ref fail, "l reads as 1", VpbNetRoomCode.Normalize("l7M2QB94XTVR") == "17M2QB94XTVR");
            Check(log, ref pass, ref fail, "O reads as 0", VpbNetRoomCode.Normalize("O7M2QB94XTVR") == "07M2QB94XTVR");
            Check(log, ref pass, ref fail, "o reads as 0", VpbNetRoomCode.Normalize("o7M2QB94XTVR") == "07M2QB94XTVR");
            Check(log, ref pass, ref fail, "lower case is accepted", VpbNetRoomCode.Normalize("k7m2qb94xtvr") == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "U is refused, never folded",
                VpbNetRoomCode.Normalize("U7M2QB94XTVR") == null);
            Check(log, ref pass, ref fail, "u is refused, never folded",
                VpbNetRoomCode.Normalize("u7M2QB94XTVR") == null);

            bool noneEmitsExcluded = true;
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            for (int seed = 0; seed < 256; seed++)
            {
                for (int i = 0; i < e.Length; i++) e[i] = (byte)((seed * 73 + i * 29) & 0xFF);
                string code = VpbNetRoomCode.FromEntropy(e, 0);
                for (int i = 0; i < code.Length; i++)
                {
                    char c = code[i];
                    if (c == 'I' || c == 'L' || c == 'O' || c == 'U') noneEmitsExcluded = false;
                }
            }
            Check(log, ref pass, ref fail, "generator never emits I, L, O or U", noneEmitsExcluded);
        }

        static void Separators(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "hyphens ignored", VpbNetRoomCode.Normalize("K7M2-QB94-XTVR") == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "spaces ignored", VpbNetRoomCode.Normalize("K7M2 QB94 XTVR") == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "underscores ignored", VpbNetRoomCode.Normalize("K7M2_QB94_XTVR") == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "tabs ignored", VpbNetRoomCode.Normalize("K7M2\tQB94\tXTVR") == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "leading and trailing separators ignored",
                VpbNetRoomCode.Normalize("  -K7M2QB94XTVR-  ") == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "separators alone are empty, not short",
                Fault("----") == VpbNetRoomCodeFault.Empty);
        }

        static void LengthRefused(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "11 chars refused as short", Fault("K7M2QB94XTV") == VpbNetRoomCodeFault.TooShort);
            Check(log, ref pass, ref fail, "13 chars refused as long", Fault("K7M2QB94XTVRZ") == VpbNetRoomCodeFault.TooLong);
            Check(log, ref pass, ref fail, "empty refused", Fault(string.Empty) == VpbNetRoomCodeFault.Empty);
            Check(log, ref pass, ref fail, "null refused", Fault(null) == VpbNetRoomCodeFault.Empty);
            Check(log, ref pass, ref fail, "a legacy free-text code is refused, not silently truncated",
                VpbNetRoomCode.Normalize("vpb-lan-test") == null);
        }

        static void AlphabetRefused(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "punctuation refused", Fault("K7M2QB94XTV!") == VpbNetRoomCodeFault.BadCharacter);
            Check(log, ref pass, ref fail, "slash refused", Fault("K7M2QB94XTV/") == VpbNetRoomCodeFault.BadCharacter);
            Check(log, ref pass, ref fail, "non-ascii refused", Fault("K7M2QB94XTVé") == VpbNetRoomCodeFault.BadCharacter);
            Check(log, ref pass, ref fail, "control char refused", Fault("K7M2QB94XTV\n") == VpbNetRoomCodeFault.BadCharacter);
            Check(log, ref pass, ref fail, "bad character beats length", Fault("!") == VpbNetRoomCodeFault.BadCharacter);
        }

        static void Uniform(StringBuilder log, ref int pass, ref int fail)
        {
            Dictionary<char, int> hist = new Dictionary<char, int>();
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            Random rng = new Random(20260823);
            int codes = 4000;
            for (int n = 0; n < codes; n++)
            {
                rng.NextBytes(e);
                string code = VpbNetRoomCode.FromEntropy(e, 0);
                for (int i = 0; i < code.Length; i++)
                {
                    int c;
                    hist.TryGetValue(code[i], out c);
                    hist[code[i]] = c + 1;
                }
            }

            int symbols = 32;
            double expect = (double)codes * VpbNetRoomCode.Chars / symbols;
            double worst = 0.0;
            foreach (KeyValuePair<char, int> kv in hist)
            {
                double dev = Math.Abs(kv.Value - expect) / expect;
                if (dev > worst) worst = dev;
            }
            Check(log, ref pass, ref fail, "all 32 symbols reachable (" + hist.Count + "/32)", hist.Count == symbols);
            Check(log, ref pass, ref fail, "symbol frequency within 15% of uniform (worst " + (worst * 100.0).ToString("0.0") + "%)",
                worst < 0.15);

            Dictionary<string, int> seen = new Dictionary<string, int>();
            int collisions = 0;
            for (int n = 0; n < codes; n++)
            {
                rng.NextBytes(e);
                string code = VpbNetRoomCode.FromEntropy(e, 0);
                if (seen.ContainsKey(code)) collisions++;
                else seen[code] = 1;
            }
            Check(log, ref pass, ref fail, "no collisions in " + codes + " codes", collisions == 0);
        }

        static void EveryFaultActionable(StringBuilder log, ref int pass, ref int fail)
        {
            string[] bad = { null, string.Empty, "----", "K7M2QB94XTV", "K7M2QB94XTVRZ", "K7M2QB94XTV!", "vpb-lan-test", "U7M2QB94XTVR" };
            bool allExplained = true;
            bool allNameAFix = true;
            for (int i = 0; i < bad.Length; i++)
            {
                string why = VpbNetRoomCode.Explain(bad[i]);
                if (why == null || why.Length < 20) allExplained = false;
                else if (why.IndexOf("Room code") < 0) allNameAFix = false;
            }
            Check(log, ref pass, ref fail, "every refusal explains itself", allExplained);
            Check(log, ref pass, ref fail, "no refusal is a bare error code", allNameAFix);
            Check(log, ref pass, ref fail, "a valid code explains nothing",
                VpbNetRoomCode.Explain("K7M2-QB94-XTVR") == null);
        }

        static VpbNetRoomCodeFault Fault(string input)
        {
            VpbNetRoomCodeFault f;
            VpbNetRoomCode.Normalize(input, out f);
            return f;
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
