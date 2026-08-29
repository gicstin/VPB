using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetRendezvousSelfTest
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

            Line(log, "===== rendezvous codec self-test =====");

            NeverAmplifies(log, ref pass, ref fail);
            AnnounceRoundTrip(log, ref pass, ref fail);
            PeersRoundTrip(log, ref pass, ref fail);
            RefusedRoundTrip(log, ref pass, ref fail);
            MalformedRefused(log, ref pass, ref fail);
            CarriesNothingElse(log, ref pass, ref fail);
            EveryRefusalActionable(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 amplify    response is always smaller than request : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 roundtrip  announce and peers survive v4 and v6    : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 malformed  truncated, padded and forged are refused: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 minimal    the wire carries a token and nothing more: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end rendezvous codec self-test =====");
            return fail == 0;
        }

        static void NeverAmplifies(StringBuilder log, ref int pass, ref int fail)
        {
            Check(log, ref pass, ref fail,
                "worst-case response " + VpbNetRendezvous.MaxResponseBytes + "B < request " + VpbNetRendezvous.RequestBytes + "B",
                VpbNetRendezvous.MaxResponseBytes < VpbNetRendezvous.RequestBytes);

            byte[] tx = new byte[VpbNetRendezvous.MaxResponseBytes];
            VpbNetEndpoint[] peers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            for (int i = 0; i < peers.Length; i++) peers[i] = V6((byte)(i + 1), (ushort)(5000 + i));

            int worst = VpbNetRendezvous.WritePeers(tx, 1u, V6(9, 47772), peers, peers.Length);
            Check(log, ref pass, ref fail,
                "a full v6 peer list writes " + worst + "B, still under the request",
                worst > 0 && worst < VpbNetRendezvous.RequestBytes);

            Check(log, ref pass, ref fail, "refusal is the smallest reply there is",
                VpbNetRendezvous.WriteRefused(tx, 1u, VpbNetRendezvousRefusal.TableFull) == VpbNetRendezvous.ResponseHeaderBytes);

            Check(log, ref pass, ref fail, "request is padded well past what it uses",
                VpbNetRendezvous.RequestBytes > VpbNetRendezvous.RequestUsedBytes);
        }

        static void AnnounceRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] token = Token(0x5A);
            byte[] buf = new byte[VpbNetRendezvous.RequestBytes];
            int n = VpbNetRendezvous.WriteAnnounce(buf, token, 0xDEADBEEFu, VpbNetRendezvous.RoleJoin, 47772);
            Check(log, ref pass, ref fail, "announce writes exactly " + VpbNetRendezvous.RequestBytes + "B", n == VpbNetRendezvous.RequestBytes);

            byte[] outToken = new byte[VpbNetRendezvous.TokenBytes];
            uint nonce;
            byte role;
            ushort port;
            VpbNetRendezvousRefusal why;
            bool ok = VpbNetRendezvous.TryReadAnnounce(buf, n, outToken, out nonce, out role, out port, out why);

            Check(log, ref pass, ref fail, "announce round trips", ok);
            Check(log, ref pass, ref fail, "token survives", Same(token, outToken));
            Check(log, ref pass, ref fail, "nonce survives", nonce == 0xDEADBEEFu);
            Check(log, ref pass, ref fail, "role survives", role == VpbNetRendezvous.RoleJoin);
            Check(log, ref pass, ref fail, "local port survives", port == 47772);
            Check(log, ref pass, ref fail, "a clean read reports no refusal", why == VpbNetRendezvousRefusal.None);

            byte[] shortBuf = new byte[VpbNetRendezvous.RequestBytes - 1];
            Check(log, ref pass, ref fail, "a buffer too small to pad is refused, never partly written",
                VpbNetRendezvous.WriteAnnounce(shortBuf, token, 1u, 0, 1) == 0);
        }

        static void PeersRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] tx = new byte[VpbNetRendezvous.MaxResponseBytes];
            VpbNetEndpoint self = V4(203, 0, 113, 7, 51820);
            VpbNetEndpoint[] peers = new VpbNetEndpoint[VpbNetRendezvous.MaxReturnedPeers];
            peers[0] = V4(198, 51, 100, 22, 47772);
            peers[1] = V6(3, 30000);

            int n = VpbNetRendezvous.WritePeers(tx, 7u, self, peers, 2);
            Check(log, ref pass, ref fail, "mixed v4/v6 peer list writes", n > 0);

            VpbNetRendezvousOp op;
            uint nonce;
            VpbNetEndpoint gotSelf;
            VpbNetEndpoint[] got;
            int count;
            VpbNetRendezvousRefusal reason;
            bool ok = VpbNetRendezvous.TryReadResponse(tx, n, out op, out nonce, out gotSelf, out got, out count, out reason);

            Check(log, ref pass, ref fail, "peers response round trips", ok && op == VpbNetRendezvousOp.Peers);
            Check(log, ref pass, ref fail, "nonce echoes", nonce == 7u);
            Check(log, ref pass, ref fail, "reflexive address survives", gotSelf.SameAs(self));
            Check(log, ref pass, ref fail, "peer count survives", count == 2);
            Check(log, ref pass, ref fail, "v4 peer survives", count == 2 && got[0].SameAs(peers[0]));
            Check(log, ref pass, ref fail, "v6 peer survives", count == 2 && got[1].SameAs(peers[1]));

            int empty = VpbNetRendezvous.WritePeers(tx, 8u, self, peers, 0);
            ok = VpbNetRendezvous.TryReadResponse(tx, empty, out op, out nonce, out gotSelf, out got, out count, out reason);
            Check(log, ref pass, ref fail, "a lone first peer still learns its own address",
                ok && count == 0 && gotSelf.SameAs(self));

            peers[2] = V4(192, 0, 2, 3, 47772);
            int over = VpbNetRendezvous.WritePeers(tx, 9u, self, peers, VpbNetRendezvous.MaxReturnedPeers + 3);
            Check(log, ref pass, ref fail, "an over-long peer list is clamped, not overflowed",
                over > 0 && over <= VpbNetRendezvous.MaxResponseBytes);

            peers[2] = new VpbNetEndpoint();
            Check(log, ref pass, ref fail, "one unset entry refuses the whole response, never a partial one",
                VpbNetRendezvous.WritePeers(tx, 10u, self, peers, 3) == 0);
        }

        static void RefusedRoundTrip(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] tx = new byte[VpbNetRendezvous.MaxResponseBytes];
            VpbNetRendezvousRefusal[] all =
            {
                VpbNetRendezvousRefusal.RateLimited,
                VpbNetRendezvousRefusal.TableFull,
                VpbNetRendezvousRefusal.RoomFull,
                VpbNetRendezvousRefusal.Version,
                VpbNetRendezvousRefusal.Malformed
            };

            bool allOk = true;
            for (int i = 0; i < all.Length; i++)
            {
                int n = VpbNetRendezvous.WriteRefused(tx, 42u, all[i]);
                VpbNetRendezvousOp op;
                uint nonce;
                VpbNetEndpoint self;
                VpbNetEndpoint[] peers;
                int count;
                VpbNetRendezvousRefusal reason;
                if (!VpbNetRendezvous.TryReadResponse(tx, n, out op, out nonce, out self, out peers, out count, out reason)
                    || op != VpbNetRendezvousOp.Refused || reason != all[i] || nonce != 42u || count != 0)
                {
                    allOk = false;
                }
            }
            Check(log, ref pass, ref fail, "every refusal reason round trips", allOk);

            int len = VpbNetRendezvous.WriteRefused(tx, 1u, VpbNetRendezvousRefusal.None);
            VpbNetRendezvousOp op2;
            uint nonce2;
            VpbNetEndpoint self2;
            VpbNetEndpoint[] peers2;
            int count2;
            VpbNetRendezvousRefusal reason2;
            Check(log, ref pass, ref fail, "a refusal with no reason is itself malformed",
                !VpbNetRendezvous.TryReadResponse(tx, len, out op2, out nonce2, out self2, out peers2, out count2, out reason2));
        }

        static void MalformedRefused(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] token = Token(0x11);
            byte[] buf = new byte[VpbNetRendezvous.RequestBytes];
            byte[] outToken = new byte[VpbNetRendezvous.TokenBytes];
            uint nonce;
            byte role;
            ushort port;
            VpbNetRendezvousRefusal why;

            VpbNetRendezvous.WriteAnnounce(buf, token, 1u, 0, 1);
            Check(log, ref pass, ref fail, "a short announce is refused",
                !VpbNetRendezvous.TryReadAnnounce(buf, VpbNetRendezvous.RequestBytes - 1, outToken, out nonce, out role, out port, out why));
            Check(log, ref pass, ref fail, "a long announce is refused, never trailing-parsed",
                !VpbNetRendezvous.TryReadAnnounce(buf, VpbNetRendezvous.RequestBytes + 1, outToken, out nonce, out role, out port, out why));

            VpbNetRendezvous.WriteAnnounce(buf, token, 1u, 0, 1);
            buf[0] = (byte)'X';
            Check(log, ref pass, ref fail, "a wrong magic is refused",
                !VpbNetRendezvous.TryReadAnnounce(buf, buf.Length, outToken, out nonce, out role, out port, out why));

            VpbNetRendezvous.WriteAnnounce(buf, token, 1u, 0, 1);
            buf[2] = VpbNetRendezvous.Version + 1;
            bool ok = VpbNetRendezvous.TryReadAnnounce(buf, buf.Length, outToken, out nonce, out role, out port, out why);
            Check(log, ref pass, ref fail, "a future version is refused BY NAME, so the peer can be told to update",
                !ok && why == VpbNetRendezvousRefusal.Version);

            VpbNetRendezvous.WriteAnnounce(buf, token, 1u, 0, 1);
            buf[24] = 9;
            Check(log, ref pass, ref fail, "an unknown role is refused",
                !VpbNetRendezvous.TryReadAnnounce(buf, buf.Length, outToken, out nonce, out role, out port, out why));

            byte[] tx = new byte[VpbNetRendezvous.MaxResponseBytes];
            VpbNetEndpoint[] p = new VpbNetEndpoint[1];
            p[0] = V4(1, 2, 3, 4, 5);
            int n = VpbNetRendezvous.WritePeers(tx, 1u, V4(9, 9, 9, 9, 9), p, 1);

            VpbNetRendezvousOp op;
            uint rnonce;
            VpbNetEndpoint rself;
            VpbNetEndpoint[] rpeers;
            int rcount;
            VpbNetRendezvousRefusal rreason;

            Check(log, ref pass, ref fail, "a truncated peer list is refused",
                !VpbNetRendezvous.TryReadResponse(tx, n - 1, out op, out rnonce, out rself, out rpeers, out rcount, out rreason));
            Check(log, ref pass, ref fail, "trailing bytes are refused, never skipped",
                !VpbNetRendezvous.TryReadResponse(tx, n + 1, out op, out rnonce, out rself, out rpeers, out rcount, out rreason));

            VpbNetRendezvous.WritePeers(tx, 1u, V4(9, 9, 9, 9, 9), p, 1);
            tx[8] = VpbNetRendezvous.MaxReturnedPeers + 1;
            Check(log, ref pass, ref fail, "a count past the cap is refused before any allocation",
                !VpbNetRendezvous.TryReadResponse(tx, n, out op, out rnonce, out rself, out rpeers, out rcount, out rreason));

            VpbNetRendezvous.WritePeers(tx, 1u, V4(9, 9, 9, 9, 9), p, 1);
            tx[VpbNetRendezvous.ResponseHeaderBytes + VpbNetRendezvous.TicketBytes] = 7;
            Check(log, ref pass, ref fail, "an unknown address family is refused",
                !VpbNetRendezvous.TryReadResponse(tx, n, out op, out rnonce, out rself, out rpeers, out rcount, out rreason));

            int tooShort = VpbNetRendezvous.WriteRefused(tx, 1u, VpbNetRendezvousRefusal.None);
            tx[3] = (byte)VpbNetRendezvousOp.Peers;
            tx[9] = 0;
            Check(log, ref pass, ref fail, "a peers response too short to hold a ticket is refused",
                !VpbNetRendezvous.TryReadResponse(tx, tooShort, out op, out rnonce, out rself, out rpeers, out rcount, out rreason));
        }

        static void CarriesNothingElse(StringBuilder log, ref int pass, ref int fail)
        {
            byte[] token = Token(0x77);
            byte[] buf = new byte[VpbNetRendezvous.RequestBytes];
            VpbNetRendezvous.WriteAnnounce(buf, token, 0u, VpbNetRendezvous.RoleHost, 0);

            bool padZero = true;
            for (int i = VpbNetRendezvous.RequestUsedBytes; i < VpbNetRendezvous.RequestBytes; i++)
            {
                if (buf[i] != 0) padZero = false;
            }
            Check(log, ref pass, ref fail, "padding is zero, never uninitialized memory", padZero);

            Check(log, ref pass, ref fail, "the request has room for a token and nothing resembling a name",
                VpbNetRendezvous.RequestUsedBytes == 4 + VpbNetRendezvous.TokenBytes + 4 + 1 + 1 + 2);

            byte[] reused = new byte[VpbNetRendezvous.RequestBytes];
            for (int i = 0; i < reused.Length; i++) reused[i] = 0xEE;
            VpbNetRendezvous.WriteAnnounce(reused, token, 0u, VpbNetRendezvous.RoleHost, 0);
            bool clean = true;
            for (int i = VpbNetRendezvous.RequestUsedBytes; i < reused.Length; i++)
            {
                if (reused[i] != 0) clean = false;
            }
            Check(log, ref pass, ref fail, "a reused buffer never leaks the previous datagram into the padding", clean);
        }

        static void EveryRefusalActionable(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetRendezvousRefusal[] all =
            {
                VpbNetRendezvousRefusal.RateLimited,
                VpbNetRendezvousRefusal.TableFull,
                VpbNetRendezvousRefusal.RoomFull,
                VpbNetRendezvousRefusal.Version,
                VpbNetRendezvousRefusal.Malformed
            };
            bool allExplained = true;
            for (int i = 0; i < all.Length; i++)
            {
                string why = VpbNetRendezvous.Explain(all[i]);
                if (why == null || why.Length < 30) allExplained = false;
            }
            Check(log, ref pass, ref fail, "every refusal names a cause and a next step", allExplained);
            Check(log, ref pass, ref fail, "success explains nothing",
                VpbNetRendezvous.Explain(VpbNetRendezvousRefusal.None) == null);
        }

        static VpbNetEndpoint V4(byte a, byte b, byte c, byte d, ushort port)
        {
            VpbNetEndpoint ep = new VpbNetEndpoint();
            ep.Family = VpbNetRendezvous.FamilyV4;
            ep.Address = new byte[] { a, b, c, d };
            ep.Port = port;
            return ep;
        }

        static VpbNetEndpoint V6(byte tag, ushort port)
        {
            VpbNetEndpoint ep = new VpbNetEndpoint();
            ep.Family = VpbNetRendezvous.FamilyV6;
            ep.Address = new byte[16];
            for (int i = 0; i < 16; i++) ep.Address[i] = (byte)(tag + i);
            ep.Port = port;
            return ep;
        }

        static byte[] Token(byte seed)
        {
            byte[] t = new byte[VpbNetRendezvous.TokenBytes];
            for (int i = 0; i < t.Length; i++) t[i] = (byte)(seed + i * 3);
            return t;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
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
