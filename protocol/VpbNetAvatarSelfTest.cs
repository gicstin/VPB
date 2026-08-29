using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetAvatarSelfTest
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

            Line(log, "===== avatar claim self-test =====");

            Seats(log, ref pass, ref fail);
            Spectators(log, ref pass, ref fail);
            Exclusive(log, ref pass, ref fail);
            Release(log, ref pass, ref fail);
            Identifiers(log, ref pass, ref fail);
            StateSync(log, ref pass, ref fail);
            Departure(log, ref pass, ref fail);
            Messages(log, ref pass, ref fail);
            SoleArbiter(log, ref pass, ref fail);
            Seating(log, ref pass, ref fail);
            Denials(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/5 spectator  a session with no avatar claimed is legal and is the starting state : " + V(fail));
            Line(log, "EXIT 2/5 exclusive  one person is ridden by at most one player, whoever asks second : " + V(fail));
            Line(log, "EXIT 3/5 authority  the host decides, and a stale broadcast never rewinds the state : " + V(fail));
            Line(log, "EXIT 4/5 hygiene    an unsafe uid is refused before it reaches an atom lookup : " + V(fail));
            Line(log, "EXIT 5/5 seating    the last free person is offered to exactly one player, and a refusal is spoken : " + V(fail));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end avatar claim self-test =====");
            return fail == 0;
        }

        const int SeatA = VpbNetAvatarAssignment.SeatA;
        const int SeatB = VpbNetAvatarAssignment.SeatB;

        static void Seats(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail,
                "a room is two seats, which is what the transport advertises as one remote peer",
                VpbNetAvatarAssignment.SeatCount == 2);

            Check(log, ref pass, ref fail, "every seat has exactly one other seat, and it is not itself",
                VpbNetAvatarAssignment.OtherSeat(SeatA) == SeatB
                && VpbNetAvatarAssignment.OtherSeat(SeatB) == SeatA);

            Check(log, ref pass, ref fail,
                "an unseated side has no other seat - it must not read as sitting opposite seat A",
                VpbNetAvatarAssignment.OtherSeat(VpbNetAvatarAssignment.Unseated)
                    == VpbNetAvatarAssignment.Unseated
                && VpbNetAvatarAssignment.OtherSeat(VpbNetAvatarAssignment.SeatCount)
                    == VpbNetAvatarAssignment.Unseated);

            Check(log, ref pass, ref fail, "only real seats are seats, and unseated is never one",
                VpbNetAvatarAssignment.IsSeat(SeatA) && VpbNetAvatarAssignment.IsSeat(SeatB)
                && !VpbNetAvatarAssignment.IsSeat(-1)
                && !VpbNetAvatarAssignment.IsSeat(VpbNetAvatarAssignment.Unseated)
                && !VpbNetAvatarAssignment.IsSeat(VpbNetAvatarAssignment.SeatCount));

            VpbNetAvatarAssignment unseated = new VpbNetAvatarAssignment();
            unseated.Arbitrate(SeatA, "Person");
            uint genBefore = unseated.Generation;
            Check(log, ref pass, ref fail,
                "an unbound seat degrades to a spectator that cannot claim, never to seat A by accident",
                unseated.SeatUid(VpbNetAvatarAssignment.Unseated).Length == 0
                && unseated.IsSpectator(VpbNetAvatarAssignment.Unseated)
                && unseated.Arbitrate(VpbNetAvatarAssignment.Unseated, "Person#2")
                    == VpbNetClaimResult.BadIdentifier
                && unseated.Generation == genBefore
                && unseated.SeatUid(SeatA) == "Person");

            Check(log, ref pass, ref fail, "a seat names itself as a letter, never as a network role",
                VpbNetAvatarAssignment.SeatName(SeatA) == "A"
                && VpbNetAvatarAssignment.SeatName(SeatB) == "B"
                && VpbNetAvatarAssignment.SeatName(9) == "?");
        }

        static void Spectators(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetAvatarAssignment a = new VpbNetAvatarAssignment();

            Check(log, ref pass, ref fail,
                "a fresh session has both seats spectating, so a session needs no Person atom at all to start",
                a.IsSpectator(SeatA) && a.IsSpectator(SeatB) && a.Generation == 0);

            Check(log, ref pass, ref fail, "claiming nothing while already spectating changes nothing",
                a.Arbitrate(SeatA, string.Empty) == VpbNetClaimResult.Unchanged && a.Generation == 0);

            Check(log, ref pass, ref fail, "one seat riding and one spectating is a normal state",
                a.Arbitrate(SeatA, "Person") == VpbNetClaimResult.Granted
                && a.SeatUid(SeatA) == "Person" && a.IsSpectator(SeatB));
        }

        static void Exclusive(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetAvatarAssignment a = new VpbNetAvatarAssignment();
            a.Arbitrate(SeatA, "Person");

            Check(log, ref pass, ref fail, "seat B cannot take the person seat A is riding",
                a.Arbitrate(SeatB, "Person") == VpbNetClaimResult.Taken
                && a.IsSpectator(SeatB) && a.SeatUid(SeatA) == "Person");

            Check(log, ref pass, ref fail, "seat B can take a different person",
                a.Arbitrate(SeatB, "Person#2") == VpbNetClaimResult.Granted
                && a.SeatUid(SeatB) == "Person#2");

            Check(log, ref pass, ref fail, "and seat A cannot then take seat B's",
                a.Arbitrate(SeatA, "Person#2") == VpbNetClaimResult.Taken && a.SeatUid(SeatA) == "Person");

            Check(log, ref pass, ref fail, "re-claiming your own is a no-op, not a refusal",
                a.Arbitrate(SeatA, "Person") == VpbNetClaimResult.Unchanged);

            uint before = a.Generation;
            a.Arbitrate(SeatB, "Person");
            Check(log, ref pass, ref fail, "a refused claim does not advance the generation, so it does not churn the wire",
                a.Generation == before);

            Check(log, ref pass, ref fail,
                "IsClaimedByAnotherSeat is what the UI greys a button with, and it never greys your own",
                a.IsClaimedByAnotherSeat(SeatB, "Person")
                && !a.IsClaimedByAnotherSeat(SeatA, "Person")
                && !a.IsClaimedByAnotherSeat(SeatA, "Person#3")
                && !a.IsClaimedByAnotherSeat(SeatA, string.Empty));

            Check(log, ref pass, ref fail,
                "a seat that does not exist is refused rather than indexed",
                a.Arbitrate(VpbNetAvatarAssignment.SeatCount, "Person#3")
                    == VpbNetClaimResult.BadIdentifier
                && a.Arbitrate(-1, "Person#3") == VpbNetClaimResult.BadIdentifier
                && a.SeatUid(-1).Length == 0
                && a.SeatUid(VpbNetAvatarAssignment.SeatCount).Length == 0);
        }

        static void Release(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetAvatarAssignment a = new VpbNetAvatarAssignment();
            a.Arbitrate(SeatA, "Person");
            a.Arbitrate(SeatB, "Person#2");

            Check(log, ref pass, ref fail, "a seat can go back to spectating",
                a.Arbitrate(SeatA, string.Empty) == VpbNetClaimResult.Released && a.IsSpectator(SeatA));

            Check(log, ref pass, ref fail, "and the person they let go becomes claimable by the other",
                !a.IsClaimedByAnotherSeat(SeatB, "Person")
                && a.Arbitrate(SeatB, "Person") == VpbNetClaimResult.Granted);

            Check(log, ref pass, ref fail, "swapping to another person releases the first one in the same step",
                a.SeatUid(SeatB) == "Person" && !a.IsClaimedByAnotherSeat(SeatA, "Person#2"));
        }

        static void Identifiers(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetAvatarAssignment a = new VpbNetAvatarAssignment();

            Check(log, ref pass, ref fail, "a traversal is refused",
                a.Arbitrate(SeatA, "../../evil") == VpbNetClaimResult.BadIdentifier && a.IsSpectator(SeatA));
            Check(log, ref pass, ref fail, "a drive letter is refused",
                a.Arbitrate(SeatA, "C:/Windows") == VpbNetClaimResult.BadIdentifier);
            Check(log, ref pass, ref fail, "a plugin reference is refused",
                a.Arbitrate(SeatA, "evil.cslist") == VpbNetClaimResult.BadIdentifier);
            Check(log, ref pass, ref fail, "null is refused, never dereferenced",
                a.Arbitrate(SeatA, null) == VpbNetClaimResult.BadIdentifier);
            Check(log, ref pass, ref fail, "an over-long uid is refused",
                a.Arbitrate(SeatA, new string('p', VpbNetAvatarAssignment.MaxUidChars + 1))
                    == VpbNetClaimResult.BadIdentifier);

            Check(log, ref pass, ref fail, "empty is valid and means spectator, not a bad name",
                VpbNetAvatarAssignment.IsValidUid(string.Empty)
                && VpbNetAvatarAssignment.IsValidUid("Creator.Pack.1:/Person")
                && !VpbNetAvatarAssignment.IsValidUid(null));
        }

        static void StateSync(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetAvatarAssignment seatBView = new VpbNetAvatarAssignment();

            Check(log, ref pass, ref fail, "seat B takes the arbitrator's broadcast as authoritative",
                seatBView.AcceptState(4, "Person", "Person#2")
                && seatBView.SeatUid(SeatA) == "Person" && seatBView.SeatUid(SeatB) == "Person#2"
                && seatBView.Generation == 4);

            Check(log, ref pass, ref fail,
                "a broadcast that arrived late is ignored, so a reordered pair cannot rewind who is riding what",
                !seatBView.AcceptState(3, "Person#2", string.Empty)
                && seatBView.SeatUid(SeatA) == "Person");

            Check(log, ref pass, ref fail, "a newer broadcast is taken",
                seatBView.AcceptState(5, string.Empty, "Person")
                && seatBView.IsSpectator(SeatA) && seatBView.SeatUid(SeatB) == "Person");

            Check(log, ref pass, ref fail,
                "a broadcast putting both seats on one person is refused rather than applied",
                !seatBView.AcceptState(6, "Person", "Person"));

            Check(log, ref pass, ref fail, "a broadcast with an unsafe uid is refused whole",
                !seatBView.AcceptState(7, "../../evil", string.Empty)
                && seatBView.SeatUid(SeatB) == "Person");

            Check(log, ref pass, ref fail, "both spectating is a legal broadcast, not an empty-string bug",
                seatBView.AcceptState(8, string.Empty, string.Empty)
                && seatBView.IsSpectator(SeatA) && seatBView.IsSpectator(SeatB));
        }

        static void Departure(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetAvatarAssignment a = new VpbNetAvatarAssignment();
            a.Arbitrate(SeatA, "Person");
            a.Arbitrate(SeatB, "Person#2");
            uint before = a.Generation;

            a.ClearSeat(SeatB);
            Check(log, ref pass, ref fail,
                "a seat emptying frees the person it was riding, so the next player in can take it",
                a.IsSpectator(SeatB) && a.SeatUid(SeatA) == "Person" && a.Generation == before + 1);

            a.ClearSeat(SeatB);
            Check(log, ref pass, ref fail, "clearing an already-empty seat does not churn the generation",
                a.Generation == before + 1);

            a.ClearSeat(VpbNetAvatarAssignment.SeatCount);
            Check(log, ref pass, ref fail, "clearing a seat that does not exist is ignored, not an index throw",
                a.Generation == before + 1 && a.SeatUid(SeatA) == "Person");

            a.Reset();
            Check(log, ref pass, ref fail, "Reset returns both seats to spectating",
                a.IsSpectator(SeatA) && a.IsSpectator(SeatB) && a.Generation == 0);
        }

        static void Messages(StringBuilder log, ref int pass, ref int fail)
        {
            bool named = true;
            for (int i = 0; i <= (int)VpbNetClaimResult.BadIdentifier; i++)
            {
                string s = VpbNetAvatarAssignment.Explain((VpbNetClaimResult)i, "Person");
                if (string.IsNullOrEmpty(s) || s.Length < 2) named = false;
            }
            Check(log, ref pass, ref fail, "every claim result has prose, never a bare code", named);

            Check(log, ref pass, ref fail, "a refusal names the person that was taken",
                VpbNetAvatarAssignment.Explain(VpbNetClaimResult.Taken, "Person#2")
                    .IndexOf("Person#2", StringComparison.Ordinal) >= 0);
        }

        static void SoleArbiter(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetAvatarAssignment arbiter = new VpbNetAvatarAssignment();
            VpbNetAvatarAssignment seatBView = new VpbNetAvatarAssignment();

            seatBView.AcceptState(0, string.Empty, string.Empty);
            arbiter.Arbitrate(SeatA, "Person");

            Check(log, ref pass, ref fail,
                "a seat that never writes its own table still has generation zero, so the arbitrator's "
                + "first state is taken rather than rejected as stale",
                seatBView.Generation == 0
                && seatBView.AcceptState(arbiter.Generation, arbiter.SeatUid(SeatA), arbiter.SeatUid(SeatB))
                && seatBView.SeatUid(SeatA) == "Person");

            VpbNetAvatarAssignment rogue = new VpbNetAvatarAssignment();
            VpbNetAvatarAssignment silent = new VpbNetAvatarAssignment();
            rogue.Arbitrate(SeatB, "Person");
            Check(log, ref pass, ref fail,
                "the desync this replaced: a seat-B-side write outruns an arbitrator that has claimed "
                + "nobody, so the authoritative state reads as stale and both sides then offer "
                + "the same Person",
                rogue.Generation == 1 && silent.Generation == 0
                && !rogue.AcceptState(silent.Generation, silent.SeatUid(SeatA), silent.SeatUid(SeatB))
                && rogue.SeatUid(SeatB) == "Person");
        }

        static void Seating(StringBuilder log, ref int pass, ref int fail)
        {
            string[] two = new string[] { "Person", "Person#2" };
            VpbNetAvatarAssignment a = new VpbNetAvatarAssignment();

            Check(log, ref pass, ref fail,
                "with nobody seated there is no single free Person, so nothing is handed out yet",
                a.SoleFreeUid(two, 2) == null);

            a.Arbitrate(SeatA, "Person");
            Check(log, ref pass, ref fail,
                "once seat A has picked, the one Person left over is seat B's without them asking",
                a.SoleFreeUid(two, 2) == "Person#2");

            a.Arbitrate(SeatB, "Person#2");
            Check(log, ref pass, ref fail, "and a full scene has nothing left to hand out",
                a.SoleFreeUid(two, 2) == null);

            string[] three = new string[] { "Person", "Person#2", "Person#3" };
            VpbNetAvatarAssignment b = new VpbNetAvatarAssignment();
            b.Arbitrate(SeatA, "Person");
            Check(log, ref pass, ref fail,
                "three people and one seat taken leaves a real choice, so seat B is asked rather than seated",
                b.SoleFreeUid(three, 3) == null);

            string[] one = new string[] { "Person" };
            VpbNetAvatarAssignment c = new VpbNetAvatarAssignment();
            Check(log, ref pass, ref fail,
                "a seat A that chose to spectate frees the only Person for seat B",
                c.SoleFreeUid(one, 1) == "Person");

            Check(log, ref pass, ref fail,
                "the scan never reads past the count it was given, and an empty roster seats nobody",
                c.SoleFreeUid(three, 0) == null
                && c.SoleFreeUid(null, 3) == null
                && c.SoleFreeUid(one, 99) == "Person");

            Check(log, ref pass, ref fail,
                "IsFree agrees with the claim table on both slots and refuses the spectator slot",
                a.IsFree("Person#3") && !a.IsFree("Person") && !a.IsFree("Person#2")
                && !a.IsFree(string.Empty) && !a.IsFree(null));
        }

        static void Denials(StringBuilder log, ref int pass, ref int fail)
        {
            bool named = true;
            for (byte r = 0; r < VpbNetClaimDeny.Count; r++)
            {
                string s = VpbNetClaimDeny.Explain(r, "Person#2");
                if (string.IsNullOrEmpty(s) || s.Length < 2) named = false;
            }
            Check(log, ref pass, ref fail,
                "every refusal reaches the asker as prose, so a blocked claim is never a silent dead button",
                named);

            Check(log, ref pass, ref fail, "a taken refusal names the Person that was taken",
                VpbNetClaimDeny.Explain(VpbNetClaimDeny.Taken, "Person#2")
                    .IndexOf("Person#2", StringComparison.Ordinal) >= 0);

            Check(log, ref pass, ref fail,
                "an unknown reason byte still says something rather than throwing",
                !string.IsNullOrEmpty(VpbNetClaimDeny.Explain(200, "Person")));

            Check(log, ref pass, ref fail,
                "a refusal with no uid reads as prose, not as a dangling sentence",
                VpbNetClaimDeny.Explain(VpbNetClaimDeny.Taken, string.Empty)
                    .IndexOf("that Person", StringComparison.Ordinal) >= 0);

            Check(log, ref pass, ref fail,
                "a bad name is never echoed back at the asker",
                VpbNetClaimDeny.Explain(VpbNetClaimDeny.BadIdentifier, "../../evil")
                    .IndexOf("evil", StringComparison.Ordinal) < 0);
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
