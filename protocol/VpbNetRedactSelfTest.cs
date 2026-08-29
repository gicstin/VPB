using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetRedactSelfTest
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

            Line(log, "===== log redaction self-test =====");

            CodesMasked(log, ref pass, ref fail);
            AddressesMasked(log, ref pass, ref fail);
            ProseSurvives(log, ref pass, ref fail);
            NothingLeaksWhole(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/3 secrets   no whole room code or invite reaches a log : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/3 addresses public masked, private keeps its subnet    : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/3 prose     ordinary log text is left alone            : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS - logs are safe to paste into a support thread");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end log redaction self-test =====");
            return fail == 0;
        }

        static void CodesMasked(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a grouped room code keeps only its first group",
                VpbNetRedact.Code("K7M2QB94XTVR") == "K7M2-xxxx-xxxx");

            Check(log, ref pass, ref fail, "an ungrouped code normalises before it is masked",
                VpbNetRedact.Code("k7m2-qb94-xtvr") == "K7M2-xxxx-xxxx");

            Check(log, ref pass, ref fail, "an unreadable code says so rather than echoing it",
                VpbNetRedact.Code("not-a-code") == "an unreadable code");

            Check(log, ref pass, ref fail, "a code embedded in a broker line is masked in place",
                VpbNetRedact.Scrub("joining room K7M2-QB94-XTVR now")
                    == "joining room K7M2-xxxx-xxxx now");

            Check(log, ref pass, ref fail, "an unhyphenated code is masked too",
                VpbNetRedact.Scrub("code K7M2QB94XTVR ok") == "code K7M2xxxxxxxx ok");

            // Invites are longer than a room code and carry an address.
            Check(log, ref pass, ref fail, "an invite is masked over its whole length",
                VpbNetRedact.Scrub("paste ABCD-EFGH-JKMN-PQRS ok")
                    == "paste ABCD-xxxx-xxxx-xxxx ok");
        }

        static void AddressesMasked(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail, "a public address keeps nothing",
                VpbNetRedact.Endpoint("203.0.113.9:8765") == "x.x.x.x:8765");

            Check(log, ref pass, ref fail, "a private address keeps its subnet and its port",
                VpbNetRedact.Endpoint("192.168.1.42:8765") == "192.168.1.x:8765");

            Check(log, ref pass, ref fail, "10/8 is private",
                VpbNetRedact.Endpoint("10.0.5.7") == "10.0.5.x");

            Check(log, ref pass, ref fail, "172.16/12 is private and 172.32 is not",
                VpbNetRedact.Endpoint("172.20.1.5") == "172.20.1.x"
                    && VpbNetRedact.Endpoint("172.32.1.5") == "x.x.x.x");

            Check(log, ref pass, ref fail, "a host name is not echoed",
                VpbNetRedact.Endpoint("relay.example.org:7777") == "a named host:7777");

            Check(log, ref pass, ref fail, "v6 collapses",
                VpbNetRedact.Endpoint("[fe80::1a2b:3c4d]:8765") == "[x::x]:8765");

            Check(log, ref pass, ref fail, "an address inside a sentence is masked, port kept",
                VpbNetRedact.Scrub("no reply from 203.0.113.9:8765 after 10s")
                    == "no reply from x.x.x.x:8765 after 10s");

            Check(log, ref pass, ref fail, "a bracketed v6 inside a sentence is masked",
                VpbNetRedact.Scrub("found the host at [fe80::1]:9 on this subnet")
                    == "found the host at [x::x]:9 on this subnet");

            // Version prefix must not spare a real address.
            Check(log, ref pass, ref fail, "a bare address still masks when no version word precedes",
                VpbNetRedact.Scrub("connecting to 203.0.113.9 now")
                    == "connecting to x.x.x.x now");

            Check(log, ref pass, ref fail, "a ported address masks even after a version word",
                VpbNetRedact.Scrub("version 203.0.113.9:8765")
                    == "version x.x.x.x:8765");
        }

        // Must not eat ordinary words.
        static void ProseSurvives(StringBuilder log, ref int pass, ref int fail)
        {
            string[] untouched =
            {
                "the room is full, so it has been taken off Steam's room list",
                "reliable send queue: 12 message(s) held until the transport had room",
                "peer 1 up",
                "broker speaks IPC v3, plugin speaks v2",
                "VaM has not pumped this link for 7s - holding the session open",
                "Custom/Clothing/Female/Some.Creator/Outfit.vam",
                "MaxEventsPerSecond reached, dropping",
                "SOME_CONSTANT_NAME was refused",
                "plugin version 1.2.3.4 loaded",
                "build 2.0.0.1 started",
                "VpbNet v0.5.0.1 ready"
            };

            for (int i = 0; i < untouched.Length; i++)
            {
                Check(log, ref pass, ref fail, "left alone: " + untouched[i],
                    VpbNetRedact.Scrub(untouched[i]) == untouched[i]);
            }

            Check(log, ref pass, ref fail, "empty and null survive",
                VpbNetRedact.Scrub(string.Empty) == string.Empty && VpbNetRedact.Scrub(null) == null);
        }

        static void NothingLeaksWhole(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] seed = new byte[VpbNetRoomCode.EntropyBytes];
            int leaked = 0;

            for (int i = 0; i < 512; i++)
            {
                for (int b = 0; b < seed.Length; b++) seed[b] = (byte)((i * 31 + b * 7 + 11) & 0xFF);
                string code = VpbNetRoomCode.FromEntropy(seed, 0);
                if (code == null) continue;

                string grouped = VpbNetRoomCode.Group(code);
                if (Contains(VpbNetRedact.Code(code), code)) leaked++;
                if (Contains(VpbNetRedact.Scrub("hosting " + grouped + " now"), grouped)) leaked++;
                if (Contains(VpbNetRedact.Scrub("hosting " + code + " now"), code)) leaked++;
            }

            Check(log, ref pass, ref fail, "512 generated codes, none survives redaction", leaked == 0);
        }

        static bool Contains(string haystack, string needle)
        {
            return haystack != null && needle != null
                && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0;
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, string what, bool ok)
        {
            if (ok)
            {
                pass++;
                Line(log, "  ok   " + what);
                return;
            }
            fail++;
            Line(log, "  FAIL " + what);
        }

        static void Line(StringBuilder log, string text)
        {
            log.Append(text);
            log.Append('\n');
        }
    }
}
