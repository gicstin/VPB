using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace VpbNet.Transport
{
    public static class ReliabilitySelfTest
    {
        const int SlotBytes = VpbIpc.MaxDatagram;
        const int ConnectTimeoutMs = 8000;
        const int MessageCount = 120;
        const int PayloadBytes = 200;

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

            Line(log, "===== reliable delivery self-test =====");
            Line(log, "  every third datagram in both directions is dropped after it leaves the sender");

            LossyRun(log, ref pass, ref fail, 3);
            CleanRun(log, ref pass, ref fail);
            UnreliableIsStillUnreliable(log, ref pass, ref fail, 3);
            OneLostMessageNeverWedgesTheRest(log, ref pass, ref fail);
            AFullWindowRefusesRatherThanLies(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/3 delivery  every reliable message arrives under loss     : " + Verdict(fail));
            Line(log, "EXIT 2/3 order     it arrives once, in order, byte for byte      : " + Verdict(fail));
            Line(log, "EXIT 3/3 scope     pose-rate traffic is not made reliable by it  : " + Verdict(fail));
            if (fail == 0) Line(log, "RESULT: PASS - retransmit and ordering hold under loss, reorder and a full window");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end reliable delivery self-test =====");
            return fail == 0;
        }

        static void LossyRun(StringBuilder log, ref int pass, ref int fail, int dropOneIn)
        {
            Pair pair = null;
            try
            {
                pair = Pair.Connect(log, ref pass, ref fail, "lossy", dropOneIn);
                if (pair == null) return;

                int[] order = new int[MessageCount];
                int got = Exchange(pair, MessageCount, 30000, order, 7);

                Check(log, ref pass, ref fail, "every reliable message arrives (" + got + "/" + MessageCount + ")",
                    got == MessageCount);
                Check(log, ref pass, ref fail, "they arrive in the order they were sent, none twice",
                    InOrder(order, got));

                uint sent, resent, abandoned, duplicates;
                int unacked, held;
                pair.Host.TryGetReliableStats(pair.HostPeer, out sent, out resent, out abandoned,
                    out duplicates, out unacked, out held);
                Line(log, "         sender: " + sent + " sent, " + resent + " resent, " + abandoned
                    + " abandoned, " + unacked + " still unacknowledged");

                Check(log, ref pass, ref fail, "the loss was real - something had to be resent (" + resent + ")",
                    resent > 0);
                Check(log, ref pass, ref fail, "nothing was abandoned", abandoned == 0);
                Check(log, ref pass, ref fail, "every message was acknowledged in the end", unacked == 0);

                pair.Join.TryGetReliableStats(pair.JoinPeer, out sent, out resent, out abandoned,
                    out duplicates, out unacked, out held);
                Check(log, ref pass, ref fail, "the receiver holds nothing once the stream drains", held == 0);
                Line(log, "         receiver discarded " + duplicates + " duplicate arrivals");
            }
            finally
            {
                if (pair != null) pair.Dispose();
            }
        }

        static void CleanRun(StringBuilder log, ref int pass, ref int fail)
        {
            Pair pair = null;
            try
            {
                pair = Pair.Connect(log, ref pass, ref fail, "clean", 0);
                if (pair == null) return;

                int[] order = new int[MessageCount];
                int got = Exchange(pair, MessageCount, 10000, order, 7);

                Check(log, ref pass, ref fail, "with no loss everything still arrives once and in order",
                    got == MessageCount && InOrder(order, got));

                uint sent, resent, abandoned, duplicates;
                int unacked, held;
                pair.Host.TryGetReliableStats(pair.HostPeer, out sent, out resent, out abandoned,
                    out duplicates, out unacked, out held);
                Check(log, ref pass, ref fail, "a healthy link retransmits nothing (" + resent + " resends)",
                    resent == 0);
            }
            finally
            {
                if (pair != null) pair.Dispose();
            }
        }

        static void UnreliableIsStillUnreliable(StringBuilder log, ref int pass, ref int fail, int dropOneIn)
        {
            Pair pair = null;
            try
            {
                pair = Pair.Connect(log, ref pass, ref fail, "unreliable", dropOneIn);
                if (pair == null) return;

                byte[] payload = new byte[PayloadBytes];
                for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i ^ 0x33);

                int sentCount = 0;
                for (int i = 0; i < MessageCount; i++)
                {
                    if (pair.Host.Send(pair.HostPeer, payload, 0, payload.Length, 9, false)) sentCount++;
                }

                byte[] rx = new byte[SlotBytes];
                int received = 0;
                bool intact = true;
                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 2000)
                {
                    long now = clock.ElapsedMilliseconds;
                    pair.Host.Poll(now);
                    pair.Join.Poll(now);

                    int peerId;
                    byte channel;
                    int len;
                    while ((len = pair.Join.Receive(rx, out peerId, out channel)) > 0)
                    {
                        received++;
                        if (len != payload.Length || channel != 9) intact = false;
                        for (int i = 0; i < payload.Length && intact; i++)
                        {
                            if (rx[i] != payload[i]) intact = false;
                        }
                    }
                    System.Threading.Thread.Sleep(2);
                }

                Check(log, ref pass, ref fail, "an unreliable send is left unreliable (" + received + "/"
                    + sentCount + " arrived)", received > 0 && received < sentCount);
                Check(log, ref pass, ref fail, "and carries no sequence prefix into the payload", intact);

                uint s, resent, abandoned, duplicates;
                int unacked, held;
                pair.Host.TryGetReliableStats(pair.HostPeer, out s, out resent, out abandoned,
                    out duplicates, out unacked, out held);
                Check(log, ref pass, ref fail, "nothing unreliable is tracked for retransmission",
                    s == 0 && resent == 0 && unacked == 0);
            }
            finally
            {
                if (pair != null) pair.Dispose();
            }
        }

        static void OneLostMessageNeverWedgesTheRest(StringBuilder log, ref int pass, ref int fail)
        {
            Pair pair = null;
            try
            {
                pair = Pair.Connect(log, ref pass, ref fail, "wedge", 0);
                if (pair == null) return;

                pair.Host.RelLifetimeMs = 1200;
                pair.Join.RelHoldMs = 1800;

                byte[] payload = new byte[PayloadBytes];
                byte[] rx = new byte[SlotBytes];

                pair.Host.SimulateDropRelSeq = 1;

                for (int i = 0; i < 5; i++)
                {
                    Fill(payload, i);
                    pair.Host.Send(pair.HostPeer, payload, 0, payload.Length, 7, true);
                }

                int[] order = new int[8];
                int got = 0;
                bool sawHoldback = false;
                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 8000 && got < 4)
                {
                    long now = clock.ElapsedMilliseconds;
                    pair.Host.Poll(now);
                    pair.Join.Poll(now);

                    if (!sawHoldback && now < 1000 && got == 0) sawHoldback = true;

                    int peerId;
                    byte ch;
                    int len;
                    while ((len = pair.Join.Receive(rx, out peerId, out ch)) > 0 && got < order.Length)
                    {
                        order[got++] = Index(rx);
                    }
                    System.Threading.Thread.Sleep(2);
                }

                Check(log, ref pass, ref fail, "a message behind a hole is held, not delivered early", sawHoldback);
                Check(log, ref pass, ref fail, "a message that never arrives is given up on rather than wedging the"
                    + " stream (" + got + " of the 4 behind it delivered)", got == 4);
                Check(log, ref pass, ref fail, "and the ones behind it keep their order",
                    got == 4 && order[0] == 1 && order[1] == 2 && order[2] == 3 && order[3] == 4);

                uint sent, resent, abandoned, duplicates;
                int unacked, held;
                pair.Host.TryGetReliableStats(pair.HostPeer, out sent, out resent, out abandoned,
                    out duplicates, out unacked, out held);
                Check(log, ref pass, ref fail, "the sender abandoned exactly the one it could not deliver ("
                    + abandoned + ")", abandoned == 1 && unacked == 0);
            }
            finally
            {
                if (pair != null) pair.Dispose();
            }
        }

        // Broker send-queue: refuse+retry must not lose or reorder after acks resume.
        static void AFullWindowRefusesRatherThanLies(StringBuilder log, ref int pass, ref int fail)
        {
            Pair pair = null;
            try
            {
                pair = Pair.Connect(log, ref pass, ref fail, "backpressure", 0);
                if (pair == null) return;

                pair.Join.SimulateDropOneInN = 1;

                const int Count = 100;
                byte[] payload = new byte[PayloadBytes];
                byte[] rx = new byte[SlotBytes];
                List<Outbox> queue = new List<Outbox>();
                for (int i = 0; i < Count; i++)
                {
                    Fill(payload, i);
                    Outbox o = new Outbox();
                    o.Buf = (byte[])payload.Clone();
                    queue.Add(o);
                }

                int next = 0;
                int refused = 0;
                int got = 0;
                int[] order = new int[Count + 8];
                bool acksBack = false;

                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 20000 && got < Count)
                {
                    long now = clock.ElapsedMilliseconds;
                    pair.Host.Poll(now);
                    pair.Join.Poll(now);

                    if (!acksBack && now > 2500)
                    {
                        acksBack = true;
                        pair.Join.SimulateDropOneInN = 0;
                    }

                    while (next < Count)
                    {
                        if (!pair.Host.Send(pair.HostPeer, queue[next].Buf, 0, PayloadBytes, 7, true))
                        {
                            refused++;
                            break;
                        }
                        next++;
                    }

                    int peerId;
                    byte ch;
                    int len;
                    while ((len = pair.Join.Receive(rx, out peerId, out ch)) > 0 && got < order.Length)
                    {
                        order[got++] = Index(rx);
                    }

                    System.Threading.Thread.Sleep(1);
                }

                Check(log, ref pass, ref fail, "a peer that stops acknowledging makes Send refuse rather than drop"
                    + " silently (" + refused + " refusals)", refused > 0);
                Check(log, ref pass, ref fail, "a caller that holds the refusal and retries loses nothing ("
                    + got + "/" + Count + ")", got == Count);
                Check(log, ref pass, ref fail, "and the retried messages keep their order", InOrder(order, got));

                uint sent, resent, abandoned, duplicates;
                int unacked, held;
                pair.Host.TryGetReliableStats(pair.HostPeer, out sent, out resent, out abandoned,
                    out duplicates, out unacked, out held);
                Check(log, ref pass, ref fail, "nothing was abandoned while the window was full", abandoned == 0);
            }
            finally
            {
                if (pair != null) pair.Dispose();
            }
        }

        sealed class Outbox
        {
            public byte[] Buf;
        }

        static int Exchange(Pair pair, int count, int timeoutMs, int[] order, byte channel)
        {
            byte[] payload = new byte[PayloadBytes];
            byte[] rx = new byte[SlotBytes];
            int next = 0;
            int got = 0;
            int refused = 0;

            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs && got < count)
            {
                long now = clock.ElapsedMilliseconds;
                pair.Host.Poll(now);
                pair.Join.Poll(now);

                if (next < count)
                {
                    Fill(payload, next);
                    if (pair.Host.Send(pair.HostPeer, payload, 0, payload.Length, channel, true)) next++;
                    else refused++;
                }

                int peerId;
                byte ch;
                int len;
                while ((len = pair.Join.Receive(rx, out peerId, out ch)) > 0)
                {
                    if (got >= order.Length) break;
                    order[got++] = (len == payload.Length && ch == channel && Matches(rx, len)) ? Index(rx) : -1;
                }

                System.Threading.Thread.Sleep(1);
            }

            long drainUntil = clock.ElapsedMilliseconds + 2500;
            while (clock.ElapsedMilliseconds < drainUntil)
            {
                long now = clock.ElapsedMilliseconds;
                pair.Host.Poll(now);
                pair.Join.Poll(now);

                uint s, resent, abandoned, duplicates;
                int unacked, held;
                if (pair.Host.TryGetReliableStats(pair.HostPeer, out s, out resent, out abandoned,
                    out duplicates, out unacked, out held) && unacked == 0 && held == 0) break;

                System.Threading.Thread.Sleep(1);
            }

            pair.Refused = refused;
            return got;
        }

        static void Fill(byte[] buf, int index)
        {
            buf[0] = (byte)(index & 0xFF);
            buf[1] = (byte)((index >> 8) & 0xFF);
            for (int i = 2; i < buf.Length; i++) buf[i] = (byte)((index * 31 + i) & 0xFF);
        }

        static int Index(byte[] buf)
        {
            return buf[0] | (buf[1] << 8);
        }

        static bool Matches(byte[] buf, int len)
        {
            int index = Index(buf);
            for (int i = 2; i < len; i++)
            {
                if (buf[i] != (byte)((index * 31 + i) & 0xFF)) return false;
            }
            return true;
        }

        static bool InOrder(int[] order, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (order[i] != i) return false;
            }
            return true;
        }

        sealed class Pair : IDisposable
        {
            public LanUdpTransport Host;
            public LanUdpTransport Join;
            public int HostPeer;
            public int JoinPeer;
            public int Refused;

            public static Pair Connect(StringBuilder log, ref int pass, ref int fail, string label, int dropOneIn)
            {
                SessionAuth.RoomKeys keys = SessionAuth.Derive("K7M2QB94XTVR");
                Pair pair = new Pair();
                pair.Host = new LanUdpTransport(SlotBytes, null);
                pair.Join = new LanUdpTransport(SlotBytes, null);

                TransportOptions ho = new TransportOptions();
                ho.Role = TransportRole.Host;
                ho.MaxPeers = 1;
                ho.ConnectBlob = "0";
                ho.SessionKey = keys.SessionKey;
                pair.Host.Start(ho);

                int colon = pair.Host.InviteBlob.LastIndexOf(':');
                string port = colon >= 0 ? pair.Host.InviteBlob.Substring(colon + 1) : string.Empty;

                TransportOptions jo = new TransportOptions();
                jo.Role = TransportRole.Join;
                jo.MaxPeers = 1;
                jo.ConnectBlob = "127.0.0.1:" + port;
                jo.SessionKey = keys.SessionKey;
                pair.Join.Start(jo);

                Stopwatch clock = Stopwatch.StartNew();
                pair.HostPeer = -1;
                pair.JoinPeer = -1;
                while (clock.ElapsedMilliseconds < ConnectTimeoutMs && (pair.HostPeer < 0 || pair.JoinPeer < 0))
                {
                    long now = clock.ElapsedMilliseconds;
                    pair.Host.Poll(now);
                    pair.Join.Poll(now);

                    int id;
                    PeerEventKind kind;
                    string reason;
                    while (pair.Host.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) pair.HostPeer = id; }
                    while (pair.Join.NextPeerEvent(out id, out kind, out reason)) { if (kind == PeerEventKind.Up) pair.JoinPeer = id; }
                    System.Threading.Thread.Sleep(2);
                }

                Check(log, ref pass, ref fail, "[" + label + "] both peers connect over loopback"
                    + (pair.Host.FailureReason == null ? string.Empty : " (" + pair.Host.FailureReason + ")"),
                    pair.HostPeer > 0 && pair.JoinPeer > 0);

                if (pair.HostPeer < 0 || pair.JoinPeer < 0)
                {
                    pair.Dispose();
                    return null;
                }

                pair.Host.SimulateDropOneInN = dropOneIn;
                pair.Join.SimulateDropOneInN = dropOneIn;
                return pair;
            }

            public void Dispose()
            {
                if (Host != null) Host.Dispose();
                if (Join != null) Join.Dispose();
                Host = null;
                Join = null;
            }
        }

        static string Verdict(int fail)
        {
            return fail == 0 ? "PASS" : "see FAIL lines";
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, string what, bool ok)
        {
            if (ok) pass++;
            else fail++;
            Line(log, (ok ? "  ok   " : "  FAIL ") + what);
        }

        static void Line(StringBuilder log, string text)
        {
            log.Append(text);
            log.Append(Environment.NewLine);
        }
    }
}
