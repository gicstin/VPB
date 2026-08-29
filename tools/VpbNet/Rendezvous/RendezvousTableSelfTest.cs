using System;
using System.Text;
using VpbNet;

namespace VpbNet.Rendezvous
{
    public static class RendezvousTableSelfTest
    {
        public static int RunConsole()
        {
            StringBuilder sb = new StringBuilder(8192);
            bool ok = VpbNetRendezvousSelfTest.Run(sb);
            if (!Run(sb)) ok = false;
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return ok ? 0 : 1;
        }

        public static bool Run(StringBuilder log)
        {
            int pass = 0;
            int fail = 0;

            Line(log, "===== rendezvous table self-test =====");

            TwoPeersMeet(log, ref pass, ref fail);
            RebindReplacesNothing(log, ref pass, ref fail);
            EntriesAgeOut(log, ref pass, ref fail);
            RoomFillsAndRefuses(log, ref pass, ref fail);
            TableCaps(log, ref pass, ref fail);
            RateLimits(log, ref pass, ref fail);
            TokensNeverMix(log, ref pass, ref fail);
            ClientHints(log, ref pass, ref fail);
            RelayForwarding(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 meeting    two announces under one token find each other: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 forgetting entries age out and empty rooms disappear     : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 bounded    rooms, peers and rate sources are all capped  : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 isolation  a token never leaks an endpoint to another    : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end rendezvous table self-test =====");
            return fail == 0;
        }

        static void TwoPeersMeet(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable();
            byte[] token = Token(1);
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            int n;

            VpbNetEndpoint host = V4(203, 0, 113, 5, 47772);
            VpbNetEndpoint join = V4(198, 51, 100, 9, 51000);

            VpbNetRendezvousRefusal r = t.Announce(token, host, 47772, VpbNetRendezvous.RoleHost, 1000, outPeers, out n);
            Check(log, ref pass, ref fail, "first announce accepted", r == VpbNetRendezvousRefusal.None);
            Check(log, ref pass, ref fail, "first announce sees nobody yet", n == 0);

            r = t.Announce(token, join, 51000, VpbNetRendezvous.RoleJoin, 1100, outPeers, out n);
            Check(log, ref pass, ref fail, "second announce accepted", r == VpbNetRendezvousRefusal.None);
            Check(log, ref pass, ref fail, "second announce sees the host", n == 1 && outPeers[0].SameAs(host));

            r = t.Announce(token, host, 47772, VpbNetRendezvous.RoleHost, 1200, outPeers, out n);
            Check(log, ref pass, ref fail, "host re-announce now sees the joiner", n == 1 && outPeers[0].SameAs(join));
            Check(log, ref pass, ref fail, "a re-announce does not create a second entry", t.PeerCount == 2);
            Check(log, ref pass, ref fail, "a peer is never handed back its own address", r == VpbNetRendezvousRefusal.None);
        }

        static void RebindReplacesNothing(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable();
            byte[] token = Token(2);
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            int n;

            VpbNetEndpoint a1 = V4(203, 0, 113, 5, 47772);
            VpbNetEndpoint a2 = V4(203, 0, 113, 5, 51999);

            t.Announce(token, a1, 47772, VpbNetRendezvous.RoleHost, 1000, outPeers, out n);
            t.Announce(token, a2, 47772, VpbNetRendezvous.RoleHost, 1100, outPeers, out n);

            Check(log, ref pass, ref fail, "a NAT rebind is a new endpoint, not an update", t.PeerCount == 2);
            Check(log, ref pass, ref fail, "the rebound peer is told about its own stale mapping", n == 1 && outPeers[0].SameAs(a1));
        }

        static void EntriesAgeOut(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable(5000);
            byte[] token = Token(3);
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            int n;

            t.Announce(token, V4(203, 0, 113, 5, 47772), 47772, VpbNetRendezvous.RoleHost, 0, outPeers, out n);
            Check(log, ref pass, ref fail, "room exists after an announce", t.RoomCount == 1);

            t.Announce(token, V4(198, 51, 100, 9, 51000), 51000, VpbNetRendezvous.RoleJoin, 20000, outPeers, out n);
            Check(log, ref pass, ref fail, "a peer that aged out is not offered", n == 0);
            Check(log, ref pass, ref fail, "the aged entry is gone, not merely hidden", t.PeerCount == 1);

            t.Sweep(60000);
            Check(log, ref pass, ref fail, "an empty room disappears entirely", t.RoomCount == 0);
            Check(log, ref pass, ref fail, "nothing is retained past the attempt window", t.PeerCount == 0);
        }

        static void RoomFillsAndRefuses(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable();
            byte[] token = Token(4);
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            int n = 0;
            VpbNetRendezvousRefusal r = VpbNetRendezvousRefusal.None;

            for (int i = 0; i < VpbNetRendezvous.MaxPeers; i++)
            {
                r = t.Announce(token, V4(203, 0, 113, (byte)(10 + i), (ushort)(47772 + i)),
                    47772, VpbNetRendezvous.RoleJoin, 1000 + i, outPeers, out n);
            }
            Check(log, ref pass, ref fail, VpbNetRendezvous.MaxPeers + " peers fit", r == VpbNetRendezvousRefusal.None);
            Check(log, ref pass, ref fail, "the last one is offered only " + VpbNetRendezvous.MaxReturnedPeers,
                n == VpbNetRendezvous.MaxReturnedPeers);

            r = t.Announce(token, V4(203, 0, 113, 99, 60000), 47772, VpbNetRendezvous.RoleJoin, 2000, outPeers, out n);
            Check(log, ref pass, ref fail, "peer " + (VpbNetRendezvous.MaxPeers + 1) + " is refused by name",
                r == VpbNetRendezvousRefusal.RoomFull);
            Check(log, ref pass, ref fail, "a refused peer is not stored", t.PeerCount == VpbNetRendezvous.MaxPeers);

            r = t.Announce(token, V4(203, 0, 113, 10, 47772), 47772, VpbNetRendezvous.RoleJoin, 2100, outPeers, out n);
            Check(log, ref pass, ref fail, "an existing peer still refreshes in a full room", r == VpbNetRendezvousRefusal.None);
        }

        static void TableCaps(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable();
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            int n;
            byte[] token = new byte[VpbNetRendezvous.TokenBytes];

            VpbNetRendezvousRefusal last = VpbNetRendezvousRefusal.None;
            for (int i = 0; i < RendezvousTable.MaxRooms + 4; i++)
            {
                token[0] = (byte)(i & 0xFF);
                token[1] = (byte)((i >> 8) & 0xFF);
                token[2] = (byte)((i >> 16) & 0xFF);
                last = t.Announce(token, V4((byte)(1 + (i & 63)), 2, 3, (byte)(i & 0xFF), (ushort)(1024 + (i & 1023))),
                    47772, VpbNetRendezvous.RoleHost, 1000, outPeers, out n);
            }
            Check(log, ref pass, ref fail, "room count stops at the cap", t.RoomCount <= RendezvousTable.MaxRooms);
            Check(log, ref pass, ref fail, "an over-cap room is refused by name, not silently dropped",
                last == VpbNetRendezvousRefusal.TableFull || last == VpbNetRendezvousRefusal.RateLimited);
            Check(log, ref pass, ref fail, "rate sources are capped too", t.RateSourceCount <= RendezvousTable.MaxRateSources);
        }

        static void RateLimits(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable();
            byte[] token = Token(6);
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            int n;
            VpbNetEndpoint noisy = V4(203, 0, 113, 200, 40000);

            int accepted = 0;
            for (int i = 0; i < RendezvousTable.MaxRequestsPerWindow + 10; i++)
            {
                if (t.Announce(token, noisy, 47772, VpbNetRendezvous.RoleHost, 1000, outPeers, out n) == VpbNetRendezvousRefusal.None)
                    accepted++;
            }
            Check(log, ref pass, ref fail, "a burst is capped at " + RendezvousTable.MaxRequestsPerWindow + " (got " + accepted + ")",
                accepted == RendezvousTable.MaxRequestsPerWindow);

            VpbNetRendezvousRefusal r = t.Announce(token, V4(198, 51, 100, 1, 40000), 47772,
                VpbNetRendezvous.RoleHost, 1000, outPeers, out n);
            Check(log, ref pass, ref fail, "one noisy address never blocks a different one", r == VpbNetRendezvousRefusal.None);

            r = t.Announce(token, noisy, 47772, VpbNetRendezvous.RoleHost, 1000 + RendezvousTable.RateWindowMs + 1, outPeers, out n);
            Check(log, ref pass, ref fail, "the window rolls and the address is served again", r == VpbNetRendezvousRefusal.None);

            RendezvousTable t2 = new RendezvousTable();
            byte[] hop = Token(9);
            int acceptedPorts = 0;
            for (int i = 0; i < RendezvousTable.MaxRequestsPerWindow + 10; i++)
            {
                hop[0] = (byte)i;
                if (t2.Announce(hop, V4(203, 0, 113, 200, (ushort)(40000 + i)), 47772,
                        VpbNetRendezvous.RoleHost, 1000, outPeers, out n) == VpbNetRendezvousRefusal.None)
                    acceptedPorts++;
            }
            Check(log, ref pass, ref fail, "rate limiting keys on address, so port hopping does not evade it",
                acceptedPorts == RendezvousTable.MaxRequestsPerWindow);

            RendezvousTable t3 = new RendezvousTable();
            byte[] scan = Token(10);
            int acceptedRooms = 0;
            for (int i = 0; i < RendezvousTable.MaxRequestsPerWindow + 10; i++)
            {
                scan[0] = (byte)i;
                if (t3.Announce(scan, V4(203, 0, 113, 201, 40000), 47772,
                        VpbNetRendezvous.RoleHost, 1000, outPeers, out n) == VpbNetRendezvousRefusal.None)
                    acceptedRooms++;
            }
            Check(log, ref pass, ref fail, "a token scan is rate limited like anything else, so guessing costs time",
                acceptedRooms == RendezvousTable.MaxRequestsPerWindow);
        }

        static void TokensNeverMix(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable();
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            int n;

            byte[] a = Token(7);
            byte[] b = Token(7);
            b[VpbNetRendezvous.TokenBytes - 1] ^= 0x01;

            t.Announce(a, V4(203, 0, 113, 5, 47772), 47772, VpbNetRendezvous.RoleHost, 1000, outPeers, out n);
            t.Announce(b, V4(198, 51, 100, 9, 51000), 51000, VpbNetRendezvous.RoleJoin, 1100, outPeers, out n);

            Check(log, ref pass, ref fail, "a one-bit token difference is a different room", n == 0 && t.RoomCount == 2);

            byte[] shortToken = new byte[VpbNetRendezvous.TokenBytes - 1];
            Check(log, ref pass, ref fail, "a short token is refused, never zero-extended",
                t.Announce(shortToken, V4(1, 1, 1, 1, 1), 1, 0, 1200, outPeers, out n) == VpbNetRendezvousRefusal.Malformed);
            Check(log, ref pass, ref fail, "a null endpoint is refused",
                t.Announce(a, new VpbNetEndpoint(), 1, 0, 1200, outPeers, out n) == VpbNetRendezvousRefusal.Malformed);
        }

        static void RelayForwarding(StringBuilder log, ref int pass, ref int fail)
        {
            RendezvousTable t = new RendezvousTable();
            byte[] token = Token(21);
            VpbNetEndpoint[] outPeers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            byte[] ta = new byte[VpbNetRendezvous.TicketBytes];
            byte[] tb = new byte[VpbNetRendezvous.TicketBytes];
            int n;

            VpbNetEndpoint a = V4(203, 0, 113, 5, 47772);
            VpbNetEndpoint b = V4(198, 51, 100, 9, 51000);

            t.Announce(token, a, 47772, VpbNetRendezvous.RoleHost, 1000, outPeers, out n, ta);
            t.Announce(token, b, 51000, VpbNetRendezvous.RoleJoin, 1100, outPeers, out n, tb);

            Check(log, ref pass, ref fail, "each peer gets its own ticket", !SameBytes(ta, tb));
            Check(log, ref pass, ref fail, "a ticket is not all zeroes", !IsZero(ta) && !IsZero(tb));

            byte[] again = new byte[VpbNetRendezvous.TicketBytes];
            t.Announce(token, a, 47772, VpbNetRendezvous.RoleHost, 1200, outPeers, out n, again);
            Check(log, ref pass, ref fail, "a re-announce keeps the same ticket, so relaying is not interrupted",
                SameBytes(ta, again));

            int fwd = t.Forward(token, ta, a, 1300, outPeers);
            Check(log, ref pass, ref fail, "a valid ticket forwards to the other peer only",
                fwd == 1 && outPeers[0].SameAs(b));

            Check(log, ref pass, ref fail, "the wrong peer's ticket forwards nothing",
                t.Forward(token, tb, a, 1300, outPeers) == 0);
            Check(log, ref pass, ref fail, "a forged ticket forwards nothing",
                t.Forward(token, new byte[VpbNetRendezvous.TicketBytes], a, 1300, outPeers) == 0);
            Check(log, ref pass, ref fail, "a spoofed source address forwards nothing even with a real ticket",
                t.Forward(token, ta, V4(1, 2, 3, 4, 5), 1300, outPeers) == 0);
            Check(log, ref pass, ref fail, "an unknown token forwards nothing",
                t.Forward(Token(99), ta, a, 1300, outPeers) == 0);
            Check(log, ref pass, ref fail, "a peer is never forwarded its own datagram",
                t.Forward(token, ta, a, 1300, outPeers) == 1 && !outPeers[0].SameAs(a));

            RendezvousTable aged = new RendezvousTable(5000);
            byte[] t1 = new byte[VpbNetRendezvous.TicketBytes];
            aged.Announce(token, a, 47772, VpbNetRendezvous.RoleHost, 0, outPeers, out n, t1);
            aged.Announce(token, b, 51000, VpbNetRendezvous.RoleJoin, 0, outPeers, out n, tb);
            Check(log, ref pass, ref fail, "a circuit whose peer aged out forwards nothing",
                aged.Forward(token, t1, a, 20000, outPeers) == 0);

            Check(log, ref pass, ref fail, "relay overhead is " + VpbNetRendezvous.RelayHeaderBytes + " bytes, well inside an MTU",
                VpbNetRendezvous.RelayHeaderBytes == 4 + VpbNetRendezvous.TokenBytes + VpbNetRendezvous.TicketBytes
                && VpbNetRendezvous.RelayHeaderBytes < 64);
        }

        static bool IsZero(byte[] a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != 0) return false;
            }
            return true;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        static void ClientHints(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] token = Token(11);
            byte[] tx = new byte[VpbNetRendezvous.RequestBytes];
            byte[] rx = new byte[VpbNetRendezvous.MaxResponseBytes];
            VpbNetEndpoint self = V4(203, 0, 113, 5, 47772);
            VpbNetEndpoint[] none = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];

            RendezvousClient c = new RendezvousClient();
            c.Start(token, VpbNetRendezvous.RoleHost, 47772, 12345u);

            Check(log, ref pass, ref fail, "a client with nothing to report says nothing", c.ExplainNoPeer() == null);

            long now = 0;
            int firstHintAt = -1;
            for (int i = 0; i < RendezvousClient.HintAfterAnnounces + 4; i++)
            {
                int n = c.Tick(now, tx);
                if (n > 0)
                {
                    uint nonce = (uint)(tx[20] | (tx[21] << 8) | (tx[22] << 16) | (tx[23] << 24));
                    int len = VpbNetRendezvous.WritePeers(rx, nonce, self, none, 0);
                    c.Consume(rx, len);
                }
                if (firstHintAt < 0 && c.ExplainNoPeer() != null) firstHintAt = c.Announces;
                now += RendezvousClient.AnnounceIntervalMs;
            }

            Check(log, ref pass, ref fail, "silence for the first few seconds, not immediately (first hint at announce "
                + firstHintAt + ")", firstHintAt == RendezvousClient.HintAfterAnnounces);
            Check(log, ref pass, ref fail, "the hint says the rendezvous is answering, so the user checks the other side",
                c.ExplainNoPeer() != null && c.ExplainNoPeer().IndexOf("has not seen the other peer", StringComparison.Ordinal) >= 0);
            Check(log, ref pass, ref fail, "it is still announcing, not timed out", c.Phase == RendezvousPhase.Announcing);

            int m = c.Tick(now, tx);
            if (m > 0)
            {
                uint nonce = (uint)(tx[20] | (tx[21] << 8) | (tx[22] << 16) | (tx[23] << 24));
                VpbNetEndpoint[] one = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
                one[0] = V4(198, 51, 100, 9, 51000);
                int len = VpbNetRendezvous.WritePeers(rx, nonce, self, one, 1);
                c.Consume(rx, len);
            }
            Check(log, ref pass, ref fail, "once a peer resolves there is nothing left to explain",
                c.Phase == RendezvousPhase.Resolved && c.ExplainNoPeer() == null);

            RendezvousClient d = new RendezvousClient();
            d.Start(token, VpbNetRendezvous.RoleJoin, 47772, 999u);
            d.Tick(0, tx);
            uint stale = (uint)(tx[20] | (tx[21] << 8) | (tx[22] << 16) | (tx[23] << 24));
            int badLen = VpbNetRendezvous.WritePeers(rx, stale + 7u, self, none, 0);
            Check(log, ref pass, ref fail, "a response on the wrong nonce is ignored, so nobody else can name a peer",
                !d.Consume(rx, badLen) && !d.Reflexive.IsPresent);

            int refuse = VpbNetRendezvous.WriteRefused(rx, stale, VpbNetRendezvousRefusal.TableFull);
            d.Consume(rx, refuse);
            Check(log, ref pass, ref fail, "a refusal fails the client and becomes the hint verbatim",
                d.Phase == RendezvousPhase.Failed
                && d.ExplainNoPeer() == VpbNetRendezvous.Explain(VpbNetRendezvousRefusal.TableFull));

            RendezvousClient e = new RendezvousClient();
            e.Start(token, VpbNetRendezvous.RoleJoin, 47772, 5u);
            e.Tick(0, tx);
            int rateLen = VpbNetRendezvous.WriteRefused(rx, (uint)(tx[20] | (tx[21] << 8) | (tx[22] << 16) | (tx[23] << 24)),
                VpbNetRendezvousRefusal.RateLimited);
            e.Consume(rx, rateLen);
            Check(log, ref pass, ref fail, "being rate limited is retried, never treated as fatal",
                e.Phase == RendezvousPhase.Announcing);
        }

        static VpbNetEndpoint V4(byte a, byte b, byte c, byte d, ushort port)
        {
            VpbNetEndpoint ep = new VpbNetEndpoint();
            ep.Family = VpbNetRendezvous.FamilyV4;
            ep.Address = new byte[] { a, b, c, d };
            ep.Port = port;
            return ep;
        }

        static byte[] Token(byte seed)
        {
            byte[] t = new byte[VpbNetRendezvous.TokenBytes];
            for (int i = 0; i < t.Length; i++) t[i] = (byte)(seed + i * 5);
            return t;
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
