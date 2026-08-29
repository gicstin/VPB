using System;
using System.Diagnostics;
using System.Text;
using VpbNet;
using VpbNet.Transport;

namespace VpbNet.Rendezvous
{
    public static class DirectConnectSelfTest
    {
        const int OverallTimeoutMs = 25000;
        const int SlotBytes = 1200;

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

            Line(log, "===== direct connect self-test =====");

            using (RendezvousServer server = new RendezvousServer())
            {
                try { server.Start(0, false); }
                catch (Exception e)
                {
                    Line(log, "  FAIL rendezvous could not bind: " + e.Message);
                    Line(log, "===== end direct connect self-test =====");
                    return false;
                }

                string blob = LanUdpTransport.RendezvousPrefix + "127.0.0.1:" + server.BoundPort;
                Line(log, "  rendezvous on UDP " + server.BoundPort + ", both peers use \"" + blob + "\"");

                SessionAuth.RoomKeys keys = SessionAuth.Derive("K7M2-QB94-XTVR");

                LanUdpTransport host = new LanUdpTransport(SlotBytes, null);
                LanUdpTransport join = new LanUdpTransport(SlotBytes, null);

                try
                {
                    host.Start(Options(TransportRole.Host, blob, keys));
                    join.Start(Options(TransportRole.Join, blob, keys));

                    Check(log, ref pass, ref fail, "both peers report the resolved mode as direct-udp",
                        host.Name == "direct-udp" && join.Name == "direct-udp");
                    Check(log, ref pass, ref fail, "neither peer failed at start",
                        host.FailureReason == null && join.FailureReason == null);

                    Stopwatch clock = Stopwatch.StartNew();
                    int hostPeer = -1;
                    int joinPeer = -1;
                    long connectedMs = -1;

                    while (clock.ElapsedMilliseconds < OverallTimeoutMs)
                    {
                        long now = clock.ElapsedMilliseconds;
                        server.Poll(now);
                        host.Poll(now);
                        join.Poll(now);

                        int id;
                        PeerEventKind kind;
                        string reason;
                        while (host.NextPeerEvent(out id, out kind, out reason))
                        {
                            if (kind == PeerEventKind.Up) hostPeer = id;
                        }
                        while (join.NextPeerEvent(out id, out kind, out reason))
                        {
                            if (kind == PeerEventKind.Up) joinPeer = id;
                        }

                        if (hostPeer > 0 && joinPeer > 0)
                        {
                            connectedMs = now;
                            break;
                        }
                        if (host.FailureReason != null || join.FailureReason != null) break;
                        System.Threading.Thread.Sleep(2);
                    }

                    Check(log, ref pass, ref fail, "host never failed" + Why(host), host.FailureReason == null);
                    Check(log, ref pass, ref fail, "joiner never failed" + Why(join), join.FailureReason == null);
                    Check(log, ref pass, ref fail, "both peers came up through the rendezvous (in "
                        + (connectedMs < 0 ? "never" : connectedMs + "ms") + ")", hostPeer > 0 && joinPeer > 0);

                    if (hostPeer > 0 && joinPeer > 0)
                    {
                        Check(log, ref pass, ref fail, "the rendezvous served exactly the announces it was sent",
                            server.Served > 0 && server.Ignored == 0);

                        byte[] payload = new byte[64];
                        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7 + 3);

                        Check(log, ref pass, ref fail, "host sends on the punched path",
                            host.Send(hostPeer, payload, 0, payload.Length, 1, false));
                        Check(log, ref pass, ref fail, "joiner sends on the punched path",
                            join.Send(joinPeer, payload, 0, payload.Length, 2, false));

                        byte[] rxA = new byte[SlotBytes];
                        byte[] rxB = new byte[SlotBytes];
                        int gotA = 0;
                        int gotB = 0;
                        byte chA = 0;
                        byte chB = 0;
                        long until = clock.ElapsedMilliseconds + 3000;
                        while (clock.ElapsedMilliseconds < until && (gotA == 0 || gotB == 0))
                        {
                            long now = clock.ElapsedMilliseconds;
                            host.Poll(now);
                            join.Poll(now);

                            int peerId;
                            byte channel;
                            if (gotA == 0)
                            {
                                gotA = join.Receive(rxA, out peerId, out channel);
                                chA = channel;
                            }
                            if (gotB == 0)
                            {
                                gotB = host.Receive(rxB, out peerId, out channel);
                                chB = channel;
                            }
                            System.Threading.Thread.Sleep(2);
                        }

                        Check(log, ref pass, ref fail, "payload arrives host -> joiner intact",
                            gotA == payload.Length && Same(rxA, payload) && chA == 1);
                        Check(log, ref pass, ref fail, "payload arrives joiner -> host intact",
                            gotB == payload.Length && Same(rxB, payload) && chB == 2);
                    }

                    WrongCodeNeverMeets(log, ref pass, ref fail, server, blob);
                }
                finally
                {
                    host.Dispose();
                    join.Dispose();
                }
            }

            LanModeUnaffected(log, ref pass, ref fail);
            InviteJoinsWithNoRendezvous(log, ref pass, ref fail);
            RelayCarriesWhatDirectCannot(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/3 resolve    both peers find each other via the rendezvous : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/3 punch      the session handshake completes over that path: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/3 isolation  a different room code never shares a path     : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS - NAT success rate is still unmeasured; loopback has no NAT");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end direct connect self-test =====");
            return fail == 0;
        }

        static void RelayCarriesWhatDirectCannot(StringBuilder log, ref int pass, ref int fail)
        {
            using (RendezvousServer server = new RendezvousServer())
            {
                try { server.Start(0, false); }
                catch (Exception e)
                {
                    Check(log, ref pass, ref fail, "relay rendezvous binds (" + e.Message + ")", false);
                    return;
                }

                string blob = LanUdpTransport.RendezvousPrefix + "127.0.0.1:" + server.BoundPort;
                SessionAuth.RoomKeys keys = SessionAuth.Derive("QB94-XTVR-K7M2");

                LanUdpTransport host = new LanUdpTransport(SlotBytes, null);
                LanUdpTransport join = new LanUdpTransport(SlotBytes, null);
                try
                {
                    host.SimulateDirectBlocked = true;
                    join.SimulateDirectBlocked = true;
                    host.Start(Options(TransportRole.Host, blob, keys));
                    join.Start(Options(TransportRole.Join, blob, keys));

                    Stopwatch clock = Stopwatch.StartNew();
                    int hostPeer = -1;
                    int joinPeer = -1;
                    bool sawFallback = false;
                    long deadline = LanUdpTransportConnectTimeoutMs + 12000;

                    while (clock.ElapsedMilliseconds < deadline && (hostPeer < 0 || joinPeer < 0))
                    {
                        long now = clock.ElapsedMilliseconds;
                        server.Poll(now);
                        host.Poll(now);
                        join.Poll(now);

                        if (!sawFallback && (host.Name == "relayed" || join.Name == "relayed"))
                        {
                            sawFallback = true;
                            Line(log, "         fell back to the relay at " + clock.ElapsedMilliseconds + "ms");
                        }

                        int id;
                        PeerEventKind kind;
                        string reason;
                        while (host.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) hostPeer = id; }
                        while (join.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) joinPeer = id; }
                        System.Threading.Thread.Sleep(2);
                    }

                    Check(log, ref pass, ref fail, "the session falls back to the relay rather than failing", sawFallback);
                    Check(log, ref pass, ref fail, "both peers come up over the relay" + Why(join),
                        hostPeer > 0 && joinPeer > 0);
                    Check(log, ref pass, ref fail, "both sides agree they are relayed",
                        host.Name == "relayed" && join.Name == "relayed");
                    Check(log, ref pass, ref fail, "the relay actually forwarded (" + server.Relayed + " datagrams)",
                        server.Relayed > 0);

                    if (hostPeer > 0 && joinPeer > 0)
                    {
                        byte[] payload = new byte[96];
                        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i ^ 0x5A);
                        host.Send(hostPeer, payload, 0, payload.Length, 3, false);

                        byte[] rx = new byte[SlotBytes];
                        int got = 0;
                        byte ch = 0;
                        long until = clock.ElapsedMilliseconds + 4000;
                        while (clock.ElapsedMilliseconds < until && got == 0)
                        {
                            long now = clock.ElapsedMilliseconds;
                            server.Poll(now);
                            host.Poll(now);
                            join.Poll(now);
                            int peerId;
                            byte channel;
                            got = join.Receive(rx, out peerId, out channel);
                            ch = channel;
                            System.Threading.Thread.Sleep(2);
                        }
                        Check(log, ref pass, ref fail, "a payload survives the relay intact",
                            got == payload.Length && Same(rx, payload) && ch == 3);
                    }
                }
                finally
                {
                    host.Dispose();
                    join.Dispose();
                }
            }
        }

        const int LanUdpTransportConnectTimeoutMs = 15000;

        static void InviteJoinsWithNoRendezvous(StringBuilder log, ref int pass, ref int fail)
        {
            string roomCode = "K7M2QB94XTVR";
            LanUdpTransport host = new LanUdpTransport(SlotBytes, null);
            LanUdpTransport join = new LanUdpTransport(SlotBytes, null);

            try
            {
                SessionAuth.RoomKeys hostKeys = SessionAuth.Derive(roomCode);
                TransportOptions ho = Options(TransportRole.Host, "0", hostKeys);
                ho.LobbyToken = null;
                host.Start(ho);

                int colon = host.InviteBlob.LastIndexOf(':');
                ushort port = 0;
                if (colon >= 0) ushort.TryParse(host.InviteBlob.Substring(colon + 1), out port);

                VpbNetEndpoint ep = new VpbNetEndpoint();
                ep.Family = VpbNetRendezvous.FamilyV4;
                ep.Address = new byte[] { 127, 0, 0, 1 };
                ep.Port = port;
                string invite = VpbNetInviteCode.Group(VpbNetInviteCode.Create(roomCode, ep));
                Check(log, ref pass, ref fail, "the host turns its address and code into one invite", invite != null);
                Line(log, "         invite: " + invite);

                string decodedCode;
                VpbNetEndpoint decodedHost;
                VpbNetInviteFault fault;
                bool parsed = VpbNetInviteCode.TryParse(invite, out decodedCode, out decodedHost, out fault);
                Check(log, ref pass, ref fail, "the joiner recovers both halves from the paste alone",
                    parsed && decodedCode == roomCode && decodedHost.Port == port);

                if (!parsed) return;

                SessionAuth.RoomKeys joinKeys = SessionAuth.Derive(decodedCode);
                Check(log, ref pass, ref fail, "both sides derive the same session key from it",
                    SameBytes(hostKeys.SessionKey, joinKeys.SessionKey));

                StringBuilder blob = new StringBuilder(48);
                decodedHost.Describe(blob);

                string rRoom, rBlob;
                bool overridden;
                VpbNetInviteFault rFault;
                VpbNetInviteCode.TryResolveJoinTarget(invite, "192.168.1.42:47772",
                    out rRoom, out rBlob, out overridden, out rFault);
                Check(log, ref pass, ref fail, "a stale address field never overrides a pasted invite",
                    rBlob == blob.ToString() && rRoom == roomCode);
                Check(log, ref pass, ref fail, "and the override is reported, never silent", overridden);

                VpbNetInviteCode.TryResolveJoinTarget(invite, string.Empty,
                    out rRoom, out rBlob, out overridden, out rFault);
                Check(log, ref pass, ref fail, "an empty address field is not an override", !overridden);

                VpbNetInviteCode.TryResolveJoinTarget(roomCode, "192.168.1.42:47772",
                    out rRoom, out rBlob, out overridden, out rFault);
                Check(log, ref pass, ref fail, "a plain room code leaves the typed address alone",
                    rBlob == "192.168.1.42:47772" && rRoom == roomCode && !overridden);

                TransportOptions jo = Options(TransportRole.Join, blob.ToString(), joinKeys);
                jo.LobbyToken = null;
                join.Start(jo);

                Stopwatch clock = Stopwatch.StartNew();
                int hostPeer = -1;
                int joinPeer = -1;
                while (clock.ElapsedMilliseconds < 5000 && (hostPeer < 0 || joinPeer < 0))
                {
                    long now = clock.ElapsedMilliseconds;
                    host.Poll(now);
                    join.Poll(now);

                    int id;
                    PeerEventKind kind;
                    string reason;
                    while (host.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) hostPeer = id; }
                    while (join.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) joinPeer = id; }
                    System.Threading.Thread.Sleep(2);
                }

                Check(log, ref pass, ref fail, "a pasted invite connects with no rendezvous running" + Why(join),
                    hostPeer > 0 && joinPeer > 0);
            }
            finally
            {
                host.Dispose();
                join.Dispose();
            }
        }

        static void LanModeUnaffected(StringBuilder log, ref int pass, ref int fail)
        {
            SessionAuth.RoomKeys keys = SessionAuth.Derive("vpb-lan-test");
            LanUdpTransport host = new LanUdpTransport(SlotBytes, null);
            LanUdpTransport join = new LanUdpTransport(SlotBytes, null);

            try
            {
                TransportOptions ho = Options(TransportRole.Host, "0", keys);
                ho.LobbyToken = null;
                host.Start(ho);

                Check(log, ref pass, ref fail, "without a rendezvous the mode is still lan-udp", host.Name == "lan-udp");
                Check(log, ref pass, ref fail, "the host still publishes an address:port invite" + Why(host),
                    host.FailureReason == null && !string.IsNullOrEmpty(host.InviteBlob)
                    && host.InviteBlob.IndexOf(LanUdpTransport.RendezvousPrefix, StringComparison.Ordinal) < 0);

                int colon = host.InviteBlob.LastIndexOf(':');
                string port = colon >= 0 ? host.InviteBlob.Substring(colon + 1) : string.Empty;

                TransportOptions jo = Options(TransportRole.Join, "127.0.0.1:" + port, keys);
                jo.LobbyToken = null;
                join.Start(jo);

                Stopwatch clock = Stopwatch.StartNew();
                int hostPeer = -1;
                int joinPeer = -1;
                while (clock.ElapsedMilliseconds < 5000 && (hostPeer < 0 || joinPeer < 0))
                {
                    long now = clock.ElapsedMilliseconds;
                    host.Poll(now);
                    join.Poll(now);

                    int id;
                    PeerEventKind kind;
                    string reason;
                    while (host.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) hostPeer = id; }
                    while (join.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) joinPeer = id; }
                    System.Threading.Thread.Sleep(2);
                }

                Check(log, ref pass, ref fail, "a plain LAN session still connects" + Why(join),
                    hostPeer > 0 && joinPeer > 0);

                if (hostPeer > 0 && joinPeer > 0)
                {
                    byte[] payload = new byte[32];
                    for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(200 - i);
                    host.Send(hostPeer, payload, 0, payload.Length, 1, false);

                    byte[] rx = new byte[SlotBytes];
                    int got = 0;
                    long until = clock.ElapsedMilliseconds + 2000;
                    while (clock.ElapsedMilliseconds < until && got == 0)
                    {
                        long now = clock.ElapsedMilliseconds;
                        host.Poll(now);
                        join.Poll(now);
                        int peerId;
                        byte channel;
                        got = join.Receive(rx, out peerId, out channel);
                        System.Threading.Thread.Sleep(2);
                    }
                    Check(log, ref pass, ref fail, "a plain LAN session still carries data",
                        got == payload.Length && Same(rx, payload));
                }
            }
            finally
            {
                host.Dispose();
                join.Dispose();
            }
        }

        static void WrongCodeNeverMeets(StringBuilder log, ref int pass, ref int fail, RendezvousServer server, string blob)
        {
            SessionAuth.RoomKeys mine = SessionAuth.Derive("K7M2-QB94-XTVR");
            SessionAuth.RoomKeys theirs = SessionAuth.Derive("K7M2-QB94-XTVQ");

            Check(log, ref pass, ref fail, "a different room code is a different rendezvous token",
                !SameBytes(mine.LobbyToken, theirs.LobbyToken));
            Check(log, ref pass, ref fail, "and a different session key",
                !SameBytes(mine.SessionKey, theirs.SessionKey));
            Check(log, ref pass, ref fail, "the published token never yields the session key",
                !SameBytes(mine.LobbyToken, mine.SessionKey));

            LanUdpTransport a = new LanUdpTransport(SlotBytes, null);
            LanUdpTransport b = new LanUdpTransport(SlotBytes, null);
            try
            {
                a.Start(Options(TransportRole.Host, blob, mine));
                b.Start(Options(TransportRole.Join, blob, theirs));

                Stopwatch clock = Stopwatch.StartNew();
                bool met = false;
                while (clock.ElapsedMilliseconds < 2500)
                {
                    long now = clock.ElapsedMilliseconds;
                    server.Poll(now);
                    a.Poll(now);
                    b.Poll(now);

                    int id;
                    PeerEventKind kind;
                    string reason;
                    while (a.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) met = true; }
                    while (b.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) met = true; }
                    System.Threading.Thread.Sleep(2);
                }
                Check(log, ref pass, ref fail, "peers on different codes never meet at the same rendezvous", !met);
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        static TransportOptions Options(TransportRole role, string blob, SessionAuth.RoomKeys keys)
        {
            TransportOptions o = new TransportOptions();
            o.Role = role;
            o.MaxPeers = 2;
            o.ConnectBlob = blob;
            o.SessionKey = keys.SessionKey;
            o.LobbyToken = keys.LobbyToken;
            return o;
        }

        static string Why(LanUdpTransport t)
        {
            return t.FailureReason == null ? string.Empty : " (" + t.FailureReason + ")";
        }

        static bool Same(byte[] got, byte[] want)
        {
            if (got == null || want == null || got.Length < want.Length) return false;
            for (int i = 0; i < want.Length; i++)
            {
                if (got[i] != want[i]) return false;
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
