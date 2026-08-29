using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetRoomBookSelfTest
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

            Line(log, "===== room book self-test =====");

            RoundTrip(log, ref pass, ref fail);
            JoinDoesNotClobberHost(log, ref pass, ref fail);
            CapEvictsOldestUnlocked(log, ref pass, ref fail);
            LockedHostSurvivesCap(log, ref pass, ref fail);
            RememberJoinDedupes(log, ref pass, ref fail);
            SeedLegacy(log, ref pass, ref fail);
            ForgetSelectsNeighbor(log, ref pass, ref fail);
            ReplaceRefusesLock(log, ref pass, ref fail);
            InviteJoinKey(log, ref pass, ref fail);
            GarbageBlob(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end room book self-test =====");
            return fail == 0;
        }

        static void RoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            Check(log, ref pass, ref fail, "add host", VpbNetRoomBook.AddHost(s, "K7M2-QB94-XTVR", 10));
            VpbNetRoomBook.SetHostLock(s, true);
            Check(log, ref pass, ref fail, "remember join",
                VpbNetRoomBook.RememberJoin(s, "k7m2 qb94 xtvr", "Alex", 20));

            string blob = VpbNetRoomBook.Encode(s);
            VpbNetRoomBookState d = new VpbNetRoomBookState();
            Check(log, ref pass, ref fail, "decode", VpbNetRoomBook.TryDecode(blob, d));
            Check(log, ref pass, ref fail, "selected host round-trips",
                d.SelectedHostKey == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "host lock round-trips",
                VpbNetRoomBook.HostLocked(d));
            Check(log, ref pass, ref fail, "join nick round-trips",
                d.Joins.Count == 1 && d.Joins[0].Nick == "Alex");
        }

        static void JoinDoesNotClobberHost(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            VpbNetRoomBook.AddHost(s, "K7M2QB94XTVR", 1);
            VpbNetRoomBook.SetHostLock(s, true);
            VpbNetRoomBook.RememberJoin(s, "3H9P2Q8K4X7R", "Sam", 2);
            Check(log, ref pass, ref fail, "join recents do not replace host key",
                VpbNetRoomBook.SelectedHost(s).Key == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "host stays locked after join remember",
                VpbNetRoomBook.HostLocked(s));
            Check(log, ref pass, ref fail, "join list has the friend's code",
                s.Joins.Count == 1 && s.Joins[0].Key == "3H9P2Q8K4X7R");
        }

        static void CapEvictsOldestUnlocked(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            for (int i = 0; i < VpbNetRoomBook.Cap; i++)
            {
                string code = CodeFromSeed(i + 1);
                Check(log, ref pass, ref fail, "fill host " + i, VpbNetRoomBook.AddHost(s, code, i + 1));
            }
            string newest = CodeFromSeed(VpbNetRoomBook.Cap + 1);
            Check(log, ref pass, ref fail, "add past cap succeeds", VpbNetRoomBook.AddHost(s, newest, 100));
            Check(log, ref pass, ref fail, "cap holds", s.Hosts.Count == VpbNetRoomBook.Cap);
            Check(log, ref pass, ref fail, "newest is selected", s.SelectedHostKey == VpbNetRoomBook.CanonicalRoom(newest));
            Check(log, ref pass, ref fail, "oldest unlocked dropped",
                VpbNetRoomBook.FindHost(s, VpbNetRoomBook.CanonicalRoom(CodeFromSeed(1))) == null);
        }

        static void LockedHostSurvivesCap(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            string keep = CodeFromSeed(1);
            VpbNetRoomBook.AddHost(s, keep, 1);
            VpbNetRoomBook.SetHostLock(s, true);
            for (int i = 2; i <= VpbNetRoomBook.Cap; i++)
                VpbNetRoomBook.AddHost(s, CodeFromSeed(i), i);

            string extra = CodeFromSeed(VpbNetRoomBook.Cap + 5);
            VpbNetRoomBook.AddHost(s, extra, 99);
            Check(log, ref pass, ref fail, "locked first host survives eviction",
                VpbNetRoomBook.FindHost(s, VpbNetRoomBook.CanonicalRoom(keep)) != null);
        }

        static void RememberJoinDedupes(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            VpbNetRoomBook.RememberJoin(s, "K7M2-QB94-XTVR", "A", 1);
            VpbNetRoomBook.RememberJoin(s, "3H9P2Q8K4X7R", "B", 2);
            VpbNetRoomBook.RememberJoin(s, "k7m2qb94xtvr", "Alex", 3);
            Check(log, ref pass, ref fail, "same room is one join row", s.Joins.Count == 2);
            Check(log, ref pass, ref fail, "re-join moves to front",
                s.Joins[0].Key == "K7M2QB94XTVR" && s.Joins[0].Nick == "Alex");
        }

        static void SeedLegacy(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            VpbNetRoomBook.SeedFromLegacy(s, "K7M2-QB94-XTVR", true, 5);
            Check(log, ref pass, ref fail, "legacy seeds a locked host",
                VpbNetRoomBook.HostLocked(s) && s.SelectedHostKey == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "legacy also seeds join recents",
                s.Joins.Count == 1 && s.Joins[0].Key == "K7M2QB94XTVR");
            Check(log, ref pass, ref fail, "legacy default code is refused",
                VpbNetRoomBook.CanonicalRoom("vpb-lan-test") == null);
        }

        static void ForgetSelectsNeighbor(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            VpbNetRoomBook.AddHost(s, CodeFromSeed(1), 1);
            VpbNetRoomBook.AddHost(s, CodeFromSeed(2), 2);
            string selected = s.SelectedHostKey;
            Check(log, ref pass, ref fail, "forget selected", VpbNetRoomBook.ForgetHost(s, selected));
            Check(log, ref pass, ref fail, "a remaining host is selected",
                s.Hosts.Count == 1 && VpbNetRoomBook.SelectedHost(s) != null);
        }

        static void ReplaceRefusesLock(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            VpbNetRoomBook.AddHost(s, "K7M2QB94XTVR", 1);
            VpbNetRoomBook.SetHostLock(s, true);
            Check(log, ref pass, ref fail, "replace refuses while locked",
                !VpbNetRoomBook.ReplaceSelectedHost(s, CodeFromSeed(3), 2));
            Check(log, ref pass, ref fail, "locked key unchanged",
                s.SelectedHostKey == "K7M2QB94XTVR");
        }

        static void InviteJoinKey(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetEndpoint ep = new VpbNetEndpoint();
            ep.Family = VpbNetRendezvous.FamilyV4;
            ep.Address = new byte[] { 192, 168, 1, 9 };
            ep.Port = 47772;
            string invite = VpbNetInviteCode.Create("K7M2QB94XTVR", ep);
            Check(log, ref pass, ref fail, "test invite created", invite != null && invite.Length > 0);

            string grouped = VpbNetInviteCode.Group(invite);
            string compact = VpbNetInviteCode.Compact(grouped);
            Check(log, ref pass, ref fail, "grouped invite compacts", compact != null && compact == invite);

            VpbNetRoomBookState s = new VpbNetRoomBookState();
            Check(log, ref pass, ref fail, "remember invite join",
                VpbNetRoomBook.RememberJoin(s, grouped, "Sam", 1));
            Check(log, ref pass, ref fail, "join row marked invite",
                s.Joins.Count == 1 && s.Joins[0].Invite);
        }

        static void GarbageBlob(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRoomBookState s = new VpbNetRoomBookState();
            Check(log, ref pass, ref fail, "empty blob is empty book", VpbNetRoomBook.TryDecode(string.Empty, s)
                && s.Hosts.Count == 0 && s.Joins.Count == 0);
            Check(log, ref pass, ref fail, "unknown version refused", !VpbNetRoomBook.TryDecode("2|S|", s));
            Check(log, ref pass, ref fail, "junk tag refused", !VpbNetRoomBook.TryDecode("1|Z|nope", s));
        }

        static string CodeFromSeed(int seed)
        {
            byte[] e = new byte[VpbNetRoomCode.EntropyBytes];
            for (int i = 0; i < e.Length; i++) e[i] = (byte)((seed * 37 + i * 19) & 0xFF);
            return VpbNetRoomCode.FromEntropy(e, 0);
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
