using System;
using System.Diagnostics;
using System.Text;

namespace VpbNet.Transport
{
    public static class DiscoverySelfTest
    {
        const int SlotBytes = VpbIpc.MaxDatagram;
        const int ConnectTimeoutMs = 8000;
        const int SilenceWindowMs = 2500;

        const string RoomA = "K7M2QB94XTVR";
        const string RoomB = "9XQ4TB7KM2VR";

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

            Line(log, "===== LAN discovery self-test =====");
            Line(log, "  a joiner given no address at all broadcasts its signed connect datagram;");
            Line(log, "  the room key is the only thing deciding who may answer it");

            LanUdpTransport probe = new LanUdpTransport(SlotBytes, null);
            bool portFree;
            try
            {
                probe.Start(Options(TransportRole.Host, string.Empty, RoomA));
                portFree = probe.FailureReason == null;
            }
            finally { probe.Dispose(); }

            if (!portFree)
            {
                Line(log, "  SKIP  UDP " + LanUdpTransport.DefaultPort + " is already held on this machine,");
                Line(log, "        so the discovery port cannot be tested here. Nothing was proved either way.");
                Line(log, "===== end LAN discovery self-test =====");
                return true;
            }

            SameRoomIsFound(log, ref pass, ref fail);
            OtherRoomIsNotAnswered(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/2 reach   a room code alone is enough on one subnet    : " + Verdict(fail));
            Line(log, "EXIT 2/2 scope   a different code is never answered           : " + Verdict(fail));
            if (fail == 0) Line(log, "RESULT: PASS - discovery costs the joiner no address and gives away no session");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end LAN discovery self-test =====");
            return fail == 0;
        }

        static void SameRoomIsFound(StringBuilder log, ref int pass, ref int fail)
        {
            LanUdpTransport host = new LanUdpTransport(SlotBytes, null);
            LanUdpTransport join = new LanUdpTransport(SlotBytes, null);
            try
            {
                host.Start(Options(TransportRole.Host, string.Empty, RoomA));
                join.Start(Options(TransportRole.Join, string.Empty, RoomA));

                Check(log, ref pass, ref fail, "an empty address is no longer a refusal to dial"
                    + Why(join), join.FailureReason == null);

                int hostPeer = -1;
                int joinPeer = -1;
                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < ConnectTimeoutMs && (hostPeer < 0 || joinPeer < 0))
                {
                    long now = clock.ElapsedMilliseconds;
                    host.Poll(now);
                    join.Poll(now);
                    Collect(host, ref hostPeer);
                    Collect(join, ref joinPeer);
                    System.Threading.Thread.Sleep(2);
                }

                Check(log, ref pass, ref fail, "the joiner finds the host with no address and no invite"
                    + Why(join), hostPeer > 0 && joinPeer > 0);
            }
            finally
            {
                host.Dispose();
                join.Dispose();
            }
        }

        static void OtherRoomIsNotAnswered(StringBuilder log, ref int pass, ref int fail)
        {
            LanUdpTransport host = new LanUdpTransport(SlotBytes, null);
            LanUdpTransport join = new LanUdpTransport(SlotBytes, null);
            try
            {
                host.Start(Options(TransportRole.Host, string.Empty, RoomA));
                join.Start(Options(TransportRole.Join, string.Empty, RoomB));

                int hostPeer = -1;
                int joinPeer = -1;
                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < SilenceWindowMs)
                {
                    long now = clock.ElapsedMilliseconds;
                    host.Poll(now);
                    join.Poll(now);
                    Collect(host, ref hostPeer);
                    Collect(join, ref joinPeer);
                    System.Threading.Thread.Sleep(2);
                }

                Check(log, ref pass, ref fail,
                    "a broadcast carrying a different room code is dropped, not answered",
                    hostPeer < 0 && joinPeer < 0);
            }
            finally
            {
                host.Dispose();
                join.Dispose();
            }
        }

        static void Collect(LanUdpTransport t, ref int peer)
        {
            int id;
            PeerEventKind kind;
            string reason;
            while (t.NextPeerEvent(out id, out kind, out reason))
            {
                if (kind == PeerEventKind.Up) peer = id;
            }
        }

        static TransportOptions Options(TransportRole role, string blob, string roomCode)
        {
            SessionAuth.RoomKeys keys = SessionAuth.Derive(roomCode);
            TransportOptions o = new TransportOptions();
            o.Role = role;
            o.MaxPeers = 1;
            o.ConnectBlob = blob;
            o.SessionKey = keys.SessionKey;
            o.LobbyToken = keys.LobbyToken;
            return o;
        }

        static string Why(LanUdpTransport t)
        {
            return t.FailureReason == null ? string.Empty : " (" + t.FailureReason + ")";
        }

        static string Verdict(int fail)
        {
            return fail == 0 ? "PASS" : "see FAIL lines";
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, string what, bool ok)
        {
            if (ok)
            {
                pass++;
                Line(log, "  ok    " + what);
                return;
            }
            fail++;
            Line(log, "  FAIL  " + what);
        }

        static void Line(StringBuilder log, string text)
        {
            log.Append(text);
            log.Append('\n');
        }
    }
}
