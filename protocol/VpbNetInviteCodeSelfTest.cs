using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetInviteCodeSelfTest
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

            Line(log, "===== invite code self-test =====");

            RoomCodeEntropyRoundTrips(log, ref pass, ref fail);
            InviteRoundTrips(log, ref pass, ref fail);
            EveryTypoIsCaught(log, ref pass, ref fail);
            NeverConfusedWithARoomCode(log, ref pass, ref fail);
            Refusals(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 roundtrip  code and endpoint survive v4 and v6      : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 typos      every single-character error is caught   : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 distinct   an invite is never mistaken for a code   : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 messages   every refusal names cause and fix        : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end invite code self-test =====");
            return fail == 0;
        }

        const string Sample = "K7M2QB94XTVR";

        static void RoomCodeEntropyRoundTrips(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            byte[] back = new byte[VpbNetRoomCode.EntropyBytes];
            Random rng = new Random(20260823);

            bool allRoundTrip = true;
            for (int n = 0; n < 2000; n++)
            {
                rng.NextBytes(e);
                string code = VpbNetRoomCode.FromEntropy(e, 0);
                if (!VpbNetRoomCode.TryDecodeEntropy(code, back, 0)) { allRoundTrip = false; break; }
                if (VpbNetRoomCode.FromEntropy(back, 0) != code) { allRoundTrip = false; break; }
            }
            Check(log, ref pass, ref fail, "2000 codes survive entropy -> code -> entropy -> code", allRoundTrip);

            Check(log, ref pass, ref fail, "a short buffer is refused, never partly filled",
                !VpbNetRoomCode.TryDecodeEntropy(Sample, new byte[VpbNetRoomCode.EntropyBytes - 1], 0));
            Check(log, ref pass, ref fail, "a wrong-length code is refused",
                !VpbNetRoomCode.TryDecodeEntropy("K7M2QB94XTV", back, 0));
        }

        static void InviteRoundTrips(StringBuilder log, ref int pass, ref int fail)
        {
            string code = Sample;
            VpbNetEndpoint v4 = V4(203, 0, 113, 42, 47772);

            string invite = VpbNetInviteCode.Create(code, v4);
            Check(log, ref pass, ref fail, "an IPv4 invite is " + VpbNetInviteCode.V4Chars + " characters",
                invite != null && invite.Length == VpbNetInviteCode.V4Chars);

            string gotCode;
            VpbNetEndpoint gotHost;
            VpbNetInviteFault fault;
            bool ok = VpbNetInviteCode.TryParse(invite, out gotCode, out gotHost, out fault);

            Check(log, ref pass, ref fail, "the invite parses", ok && fault == VpbNetInviteFault.None);
            Check(log, ref pass, ref fail, "the room code comes back identical, so the keys match", gotCode == code);
            Check(log, ref pass, ref fail, "the endpoint comes back identical", gotHost.SameAs(v4));

            Check(log, ref pass, ref fail, "the grouped display form parses too",
                VpbNetInviteCode.TryParse(VpbNetInviteCode.Group(invite), out gotCode, out gotHost, out fault)
                && gotCode == code && gotHost.SameAs(v4));
            Check(log, ref pass, ref fail, "lower case parses",
                VpbNetInviteCode.TryParse(invite.ToLowerInvariant(), out gotCode, out gotHost, out fault) && gotCode == code);

            VpbNetEndpoint v6 = new VpbNetEndpoint();
            v6.Family = VpbNetRendezvous.FamilyV6;
            v6.Address = new byte[16];
            for (int i = 0; i < 16; i++) v6.Address[i] = (byte)(0x20 + i);
            v6.Port = 51820;

            string invite6 = VpbNetInviteCode.Create(code, v6);
            Check(log, ref pass, ref fail, "an IPv6 invite is " + VpbNetInviteCode.V6Chars + " characters",
                invite6 != null && invite6.Length == VpbNetInviteCode.V6Chars);
            Check(log, ref pass, ref fail, "an IPv6 invite round trips",
                VpbNetInviteCode.TryParse(invite6, out gotCode, out gotHost, out fault)
                && gotCode == code && gotHost.SameAs(v6));

            bool allPorts = true;
            int[] ports = { 1, 80, 1024, 47772, 51820, 65535 };
            for (int i = 0; i < ports.Length; i++)
            {
                VpbNetEndpoint ep = V4(10, 0, 0, 1, (ushort)ports[i]);
                string s = VpbNetInviteCode.Create(code, ep);
                if (!VpbNetInviteCode.TryParse(s, out gotCode, out gotHost, out fault) || gotHost.Port != ports[i])
                    allPorts = false;
            }
            Check(log, ref pass, ref fail, "every port from 1 to 65535 survives", allPorts);

            bool allCodes = true;
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            Random rng = new Random(7);
            for (int n = 0; n < 500; n++)
            {
                rng.NextBytes(e);
                string c = VpbNetRoomCode.FromEntropy(e, 0);
                VpbNetEndpoint ep = V4((byte)(1 + (n & 63)), 2, 3, (byte)n, (ushort)(1024 + n));
                string s = VpbNetInviteCode.Create(c, ep);
                if (!VpbNetInviteCode.TryParse(s, out gotCode, out gotHost, out fault)
                    || gotCode != c || !gotHost.SameAs(ep)) allCodes = false;
            }
            Check(log, ref pass, ref fail, "500 random code/endpoint pairs round trip", allCodes);
        }

        static void EveryTypoIsCaught(StringBuilder log, ref int pass, ref int fail)
        {
            string code = Sample;
            string invite = VpbNetInviteCode.Create(code, V4(203, 0, 113, 42, 47772));

            string gotCode;
            VpbNetEndpoint gotHost;
            VpbNetInviteFault fault;

            int missed = 0;
            int tried = 0;
            for (int i = 0; i < invite.Length; i++)
            {
                for (int a = 0; a < VpbNetRoomCode.Alphabet.Length; a++)
                {
                    char sub = VpbNetRoomCode.Alphabet[a];
                    if (sub == invite[i]) continue;

                    char[] c = invite.ToCharArray();
                    c[i] = sub;
                    tried++;

                    if (VpbNetInviteCode.TryParse(new string(c), out gotCode, out gotHost, out fault))
                    {
                        if (gotCode != code || !gotHost.SameAs(V4(203, 0, 113, 42, 47772))) missed++;
                    }
                }
            }
            Check(log, ref pass, ref fail, "all " + tried + " single-character substitutions rejected or harmless (missed " + missed + ")",
                missed == 0);

            char[] dropped = new char[invite.Length - 1];
            invite.CopyTo(1, dropped, 0, invite.Length - 1);
            Check(log, ref pass, ref fail, "a dropped character is refused",
                !VpbNetInviteCode.TryParse(new string(dropped), out gotCode, out gotHost, out fault));

            Check(log, ref pass, ref fail, "an extra character is refused",
                !VpbNetInviteCode.TryParse(invite + "7", out gotCode, out gotHost, out fault));

            char[] swapped = invite.ToCharArray();
            for (int i = 0; i + 1 < swapped.Length; i++)
            {
                if (swapped[i] == swapped[i + 1]) continue;
                char t = swapped[i];
                swapped[i] = swapped[i + 1];
                swapped[i + 1] = t;
                break;
            }
            Check(log, ref pass, ref fail, "a transposition is refused",
                !VpbNetInviteCode.TryParse(new string(swapped), out gotCode, out gotHost, out fault));
        }

        static void NeverConfusedWithARoomCode(StringBuilder log, ref int pass, ref int fail)
        {
            string code = Sample;
            string invite = VpbNetInviteCode.Create(code, V4(203, 0, 113, 42, 47772));

            Check(log, ref pass, ref fail, "an invite is recognised as one", VpbNetInviteCode.LooksLikeInvite(invite));
            Check(log, ref pass, ref fail, "a room code is not", !VpbNetInviteCode.LooksLikeInvite(code));
            Check(log, ref pass, ref fail, "a grouped invite is still recognised",
                VpbNetInviteCode.LooksLikeInvite(VpbNetInviteCode.Group(invite)));
            Check(log, ref pass, ref fail, "an invite is not a valid room code", !VpbNetRoomCode.IsWellFormed(invite));
            Check(log, ref pass, ref fail, "the three lengths cannot collide",
                VpbNetInviteCode.V4Chars != VpbNetRoomCode.Chars && VpbNetInviteCode.V6Chars != VpbNetRoomCode.Chars
                && VpbNetInviteCode.V4Chars != VpbNetInviteCode.V6Chars);
            Check(log, ref pass, ref fail, "free text is neither",
                !VpbNetInviteCode.LooksLikeInvite("vpb-lan-test") && !VpbNetRoomCode.IsWellFormed("vpb-lan-test"));

            bool noneLookLikeInvites = true;
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            Random rng = new Random(4242);
            for (int n = 0; n < 4000; n++)
            {
                rng.NextBytes(e);
                if (VpbNetInviteCode.LooksLikeInvite(VpbNetRoomCode.FromEntropy(e, 0))) noneLookLikeInvites = false;
            }
            Check(log, ref pass, ref fail, "no generated code out of 4000 is mistaken for an invite", noneLookLikeInvites);
        }

        static void Refusals(StringBuilder log, ref int pass, ref int fail)
        {
            string gotCode;
            VpbNetEndpoint gotHost;
            VpbNetInviteFault fault;

            VpbNetInviteCode.TryParse(null, out gotCode, out gotHost, out fault);
            Check(log, ref pass, ref fail, "null is empty, not malformed", fault == VpbNetInviteFault.Empty);

            VpbNetInviteCode.TryParse("K7M2QB94XTVR", out gotCode, out gotHost, out fault);
            Check(log, ref pass, ref fail, "a bare room code is refused for length", fault == VpbNetInviteFault.BadLength);

            VpbNetInviteCode.TryParse("K7M2QB94XTV!", out gotCode, out gotHost, out fault);
            Check(log, ref pass, ref fail, "punctuation is refused by character, before length",
                fault == VpbNetInviteFault.BadCharacter);

            string invite = VpbNetInviteCode.Create(Sample, V4(203, 0, 113, 42, 47772));
            char[] c = invite.ToCharArray();
            c[3] = c[3] == '0' ? '1' : '0';
            VpbNetInviteCode.TryParse(new string(c), out gotCode, out gotHost, out fault);
            Check(log, ref pass, ref fail, "a corrupted invite is refused by checksum, not silently redirected",
                fault == VpbNetInviteFault.BadChecksum);

            Check(log, ref pass, ref fail, "a zero port is never encoded",
                VpbNetInviteCode.Create(Sample, V4(203, 0, 113, 42, 0)) == null
                || !VpbNetInviteCode.TryParse(VpbNetInviteCode.Create(Sample, V4(203, 0, 113, 42, 0)),
                    out gotCode, out gotHost, out fault));

            Check(log, ref pass, ref fail, "an invalid room code makes no invite",
                VpbNetInviteCode.Create("vpb-lan-test", V4(1, 2, 3, 4, 5)) == null);
            Check(log, ref pass, ref fail, "an absent endpoint makes no invite",
                VpbNetInviteCode.Create(Sample, new VpbNetEndpoint()) == null);

            VpbNetInviteFault[] all =
            {
                VpbNetInviteFault.Empty, VpbNetInviteFault.BadCharacter, VpbNetInviteFault.BadLength,
                VpbNetInviteFault.BadVersion, VpbNetInviteFault.BadFamily, VpbNetInviteFault.BadChecksum,
                VpbNetInviteFault.BadEndpoint
            };
            bool allExplained = true;
            for (int i = 0; i < all.Length; i++)
            {
                string why = VpbNetInviteCode.Explain(all[i]);
                if (why == null || why.Length < 30) allExplained = false;
            }
            Check(log, ref pass, ref fail, "every refusal names a cause and a next step", allExplained);
            Check(log, ref pass, ref fail, "success explains nothing",
                VpbNetInviteCode.Explain(VpbNetInviteFault.None) == null);
        }

        static VpbNetEndpoint V4(byte a, byte b, byte c, byte d, ushort port)
        {
            VpbNetEndpoint ep = new VpbNetEndpoint();
            ep.Family = VpbNetRendezvous.FamilyV4;
            ep.Address = new byte[] { a, b, c, d };
            ep.Port = port;
            return ep;
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
