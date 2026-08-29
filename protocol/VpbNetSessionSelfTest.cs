using System;
using System.Text;

namespace VpbNet
{
    public static class VpbNetSessionSelfTest
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

            Line(log, "===== session state machine self-test =====");

            HappyPath(log, ref pass, ref fail);
            StallAndResume(log, ref pass, ref fail);
            SpectatorNeverTimesOut(log, ref pass, ref fail);
            DataTimeoutReconnects(log, ref pass, ref fail);
            LocalFreezeIsNotAPeerTimeout(log, ref pass, ref fail);
            LocalFreezeCreditIsBounded(log, ref pass, ref fail);
            LocalFreezeDoesNotEatConnectOrBackoff(log, ref pass, ref fail);
            PeerBusyHoldsTheSession(log, ref pass, ref fail);
            PeerBusyIsBoundedAndClearable(log, ref pass, ref fail);
            ReconnectExhausted(log, ref pass, ref fail);
            GracefulLeaveNeverRetries(log, ref pass, ref fail);
            FatalNeverRetries(log, ref pass, ref fail);
            ConnectTimeoutNamesAllThree(log, ref pass, ref fail);
            EveryReasonActionable(log, ref pass, ref fail);
            DriveGating(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 lifecycle  connect -> sync -> running                : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 stall      2s stalled, " + (int)(VpbNetSession.DataTimeoutMs / 1000.0)
                + "s dropped, resume recovers : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 resume     rejoin needs no room code, backoff bounded: " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 messages   every drop reason names cause and fix     : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end session self-test =====");
            return fail == 0;
        }

        static void HappyPath(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = new VpbNetSession();
            double t = 1000.0;

            s.Begin(t);
            bool connecting = s.State == VpbNetSessionState.Connecting && s.ConnectWanted;

            t += 120.0;
            s.OnTransportUp(t);
            bool syncing = s.State == VpbNetSessionState.Syncing && !s.CanDriveAvatar;

            t += 650.0;
            s.OnClockReady(true, t);
            s.Tick(t);
            bool running = s.State == VpbNetSessionState.Running && s.CanDriveAvatar;

            Check(log, ref pass, ref fail, connecting && syncing && running,
                "connect -> sync -> running, and the avatar is not driven until the clock is ready",
                "lifecycle broken: connecting=" + connecting + " syncing=" + syncing + " running=" + running);
        }

        static void StallAndResume(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;

            s.OnData(t);
            t = Advance(s, t, t + 1500.0);
            bool stillRunning = s.State == VpbNetSessionState.Running;

            t = Advance(s, t, t + 1000.0);
            bool stalled = s.State == VpbNetSessionState.Stalled;
            bool stillDriving = s.CanDriveAvatar;

            t += 100.0;
            s.OnData(t);
            bool resumed = s.State == VpbNetSessionState.Running && s.Resumes == 1;

            Check(log, ref pass, ref fail, stillRunning && stalled && resumed,
                "2s without data marks the peer stalled, and data resumes it without a drop (" + s.Stalls + " stall, " + s.Resumes + " resume)",
                "stall handling broken: running@1.5s=" + stillRunning + " stalled@2.2s=" + stalled + " resumed=" + resumed);

            Check(log, ref pass, ref fail, stillDriving,
                "a stalled peer keeps being driven (last pose held) rather than released mid-air",
                "a stalled peer stopped being driven - the avatar would drop control");
        }

        // Spectator sends no pose by design — used to stall at 2s and drop every 30s.
        static void SpectatorNeverTimesOut(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;
            s.OnData(t);
            s.SetPeerStreaming(false);

            double end = t + VpbNetSession.DataTimeoutMs * 3.0;
            while (t < end)
            {
                t += 250.0;
                s.NotePeerAlive(t);
                s.Tick(t);
                if (s.State != VpbNetSessionState.Running) break;
            }

            bool held = s.State == VpbNetSessionState.Running && s.Stalls == 0;

            Check(log, ref pass, ref fail, held,
                "a spectating peer that only pings holds the session open indefinitely (" + s.Stalls + " stalls in "
                    + (int)(VpbNetSession.DataTimeoutMs * 3.0 / 1000.0) + "s)",
                "a spectator was treated as silence: state=" + s.State + " stalls=" + s.Stalls);

            VpbNetSession dead = Running(1000.0);
            t = 2000.0;
            dead.OnData(t);
            dead.SetPeerStreaming(false);
            t = Advance(dead, t, t + VpbNetSession.DataTimeoutMs + 1000.0);

            Check(log, ref pass, ref fail,
                dead.State == VpbNetSessionState.Reconnecting && dead.Reason == VpbNetDropReason.DataTimeout,
                "but a spectator that stops pinging is still noticed and rejoined",
                "a spectator that went away was never dropped: state=" + dead.State + " reason=" + dead.Reason);

            VpbNetSession back = Running(1000.0);
            t = 2000.0;
            back.OnData(t);
            t = Advance(back, t, t + 2500.0);
            bool stalled = back.State == VpbNetSessionState.Stalled;
            back.SetPeerStreaming(false);
            t += 250.0;
            back.NotePeerAlive(t);
            back.Tick(t);

            Check(log, ref pass, ref fail, stalled && back.State == VpbNetSessionState.Running,
                "and a peer that gives its avatar up while stalled stops being stalled at nothing",
                "claim release left the session stalled: stalled=" + stalled + " after=" + back.State);
        }

        static void DataTimeoutReconnects(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;
            s.OnData(t);

            t = Advance(s, t, t + VpbNetSession.DataTimeoutMs + 500.0);
            s.Tick(t);

            bool reconnecting = s.State == VpbNetSessionState.Reconnecting;
            bool reasonKept = s.Reason == VpbNetDropReason.DataTimeout;
            bool keptCreds = s.HasCredentials;

            t += 10.0;
            s.Tick(t);
            bool dialed = s.ConnectWanted && s.ReconnectAttempts == 1;

            s.OnTransportUp(t);
            s.OnClockReady(true, t);
            s.Tick(t);
            bool resumedRunning = s.State == VpbNetSessionState.Running && s.ReconnectAttempts == 0;

            Check(log, ref pass, ref fail, reconnecting && reasonKept && keptCreds && dialed,
                (int)(VpbNetSession.DataTimeoutMs / 1000.0) + "s without data drops the peer with reason "
                    + VpbNetDropReason.DataTimeout
                    + " and rejoins automatically, credentials retained",
                "data timeout handling broken: state=" + s.State + " reason=" + s.Reason
                    + " creds=" + keptCreds + " dialed=" + dialed);

            Check(log, ref pass, ref fail, resumedRunning,
                "a successful rejoin resumes Running and clears the attempt counter - the room code is never re-typed",
                "rejoin did not resume: state=" + s.State + " attempts=" + s.ReconnectAttempts);
        }

        // VaM load blocks Tick while wall clock runs — without credit, 11s load dropped the peer.
        static void LocalFreezeIsNotAPeerTimeout(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;
            s.OnData(t);
            s.Tick(t);

            t += 11000.0;
            s.Tick(t);
            s.Tick(t);

            bool survived = s.State == VpbNetSessionState.Running || s.State == VpbNetSessionState.Stalled;
            bool counted = s.LocalStalls == 1 && s.LongestLocalStallMs >= 11000.0;

            t += 100.0;
            s.OnData(t);
            bool healthy = s.State == VpbNetSessionState.Running;

            Check(log, ref pass, ref fail, survived && counted && healthy,
                "an 11s content load freezes this process, and the peer is NOT dropped for a silence"
                    + " we slept through (" + s.LocalStalls + " local stall, "
                    + F(s.LongestLocalStallMs / 1000.0, 1) + "s)",
                "a local freeze still timed out the peer: state=" + s.State
                    + " localStalls=" + s.LocalStalls + " longest=" + F(s.LongestLocalStallMs, 0) + "ms");
        }

        // Past the credit bound, ordinary timeout applies.
        static void LocalFreezeCreditIsBounded(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;
            s.OnData(t);
            s.Tick(t);

            for (int i = 0; i < 20; i++)
            {
                t += 30000.0;
                s.Tick(t);
                s.Tick(t);
                if (s.State == VpbNetSessionState.Reconnecting || s.State == VpbNetSessionState.Dropped) break;
            }

            Check(log, ref pass, ref fail,
                s.State == VpbNetSessionState.Reconnecting || s.State == VpbNetSessionState.Dropped,
                "a machine that never stops freezing still eventually drops a peer that really left"
                    + " - the credit is bounded at " + (int)(VpbNetSession.MaxCreditPerSilenceMs / 1000.0)
                    + "s per silence (gave up after " + F((t - 2000.0) / 1000.0, 0) + "s)",
                "an endlessly freezing machine credited its way out of ever noticing a dead peer: state=" + s.State);
        }

        // Same hole on every wall-clock deadline, not just the data one.
        static void LocalFreezeDoesNotEatConnectOrBackoff(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = new VpbNetSession();
            double t = 1000.0;
            s.Begin(t);
            s.Tick(t);

            t += VpbNetSession.ConnectTimeoutMs + 5000.0;
            s.Tick(t);
            bool stillConnecting = s.State == VpbNetSessionState.Connecting;

            s.OnTransportUp(t);
            s.OnClockReady(true, t);
            s.Tick(t);
            bool running = s.State == VpbNetSessionState.Running;

            Check(log, ref pass, ref fail, stillConnecting && running,
                "a freeze while connecting is not counted as the host failing to answer",
                "a local freeze consumed the connect timeout: connecting=" + stillConnecting
                    + " running=" + running + " state=" + s.State);
        }

        // Peer says so before it blocks — hold instead of dropping.
        static void PeerBusyHoldsTheSession(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;
            s.OnData(t);
            s.NotePeerBusy(t, 60000.0, "loading a look");

            double last = t;
            for (int i = 0; i < 50; i++)
            {
                t += 1000.0;
                s.Tick(t);
                if (s.State == VpbNetSessionState.Reconnecting || s.State == VpbNetSessionState.Dropped) break;
                last = t;
            }

            bool held = s.State == VpbNetSessionState.Stalled && s.PeerBusy;
            bool driving = s.CanDriveAvatar;
            string text = s.DescribeState();
            bool explains = text.IndexOf("loading a look", StringComparison.Ordinal) >= 0;

            Check(log, ref pass, ref fail, held && driving && explains,
                "a peer that warned us it is loading is held for the whole load, keeps being driven,"
                    + " and the reason is shown verbatim (" + F((last - 2000.0) / 1000.0, 0) + "s in: \"" + text + "\")",
                "a peer that warned us was dropped anyway: state=" + s.State + " busy=" + s.PeerBusy
                    + " driving=" + driving + " text=\"" + text + "\"");
        }

        static void PeerBusyIsBoundedAndClearable(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;
            s.OnData(t);
            s.NotePeerBusy(t, VpbNetSession.MaxPeerBusyMs * 10.0, "loading a look");
            Advance(s, t, t + VpbNetSession.MaxPeerBusyMs + VpbNetSession.DataTimeoutMs + 5000.0);
            bool boundedOut = s.State == VpbNetSessionState.Reconnecting || s.State == VpbNetSessionState.Dropped;

            VpbNetSession r = Running(1000.0);
            double u = 2000.0;
            r.OnData(u);
            r.NotePeerBusy(u, 600000.0, "loading a look");
            double v = Advance(r, u, u + 5000.0);
            bool heldFirst = r.PeerBusy && r.State == VpbNetSessionState.Stalled;

            r.NotePeerReady();
            r.Tick(v);
            bool cleared = !r.PeerBusy;

            Advance(r, v, v + VpbNetSession.DataTimeoutMs + 3000.0);
            bool timesOutAfterReady = r.State == VpbNetSessionState.Reconnecting
                || r.State == VpbNetSessionState.Dropped;
            boundedOut = boundedOut && heldFirst;

            Check(log, ref pass, ref fail, boundedOut && cleared && timesOutAfterReady,
                "a peer cannot hold the session open forever: the claim is capped at "
                    + (int)(VpbNetSession.MaxPeerBusyMs / 1000.0) + "s, and \"I am back\" restores"
                    + " the ordinary timeout immediately",
                "peer-busy hold is not bounded: capped=" + boundedOut + " cleared=" + cleared
                    + " timesOutAfterReady=" + timesOutAfterReady);
        }

        static void ReconnectExhausted(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = Running(1000.0);
            double t = 2000.0;
            s.OnData(t);

            t = Advance(s, t, t + VpbNetSession.DataTimeoutMs + 500.0);

            double totalBackoff = 0.0;
            double last = t;
            for (int i = 0; i < VpbNetSession.MaxReconnectAttempts + 2; i++)
            {
                for (int k = 0; k < 200 && !s.ConnectWanted && s.State == VpbNetSessionState.Reconnecting; k++)
                {
                    t += 50.0;
                    s.Tick(t);
                }
                if (s.State != VpbNetSessionState.Reconnecting) break;
                totalBackoff += t - last;
                last = t;
                s.OnAttemptFailed(t);
                t += 10.0;
                s.Tick(t);
            }

            Check(log, ref pass, ref fail,
                s.State == VpbNetSessionState.Dropped && s.Reason == VpbNetDropReason.ReconnectExhausted,
                "after " + VpbNetSession.MaxReconnectAttempts + " failed attempts the session drops as "
                    + VpbNetDropReason.ReconnectExhausted + " instead of retrying forever (backoff spent "
                    + F(totalBackoff / 1000.0, 2) + "s)",
                "exhausted reconnect wrong: state=" + s.State + " reason=" + s.Reason
                    + " attempts=" + s.ReconnectAttempts);

            Check(log, ref pass, ref fail, totalBackoff > 1000.0,
                "reconnect backs off rather than hammering (" + F(totalBackoff / 1000.0, 2) + "s across all attempts)",
                "reconnect did not back off: total " + F(totalBackoff, 0) + "ms");
        }

        static void GracefulLeaveNeverRetries(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession peer = Running(1000.0);
            peer.OnPeerBye(2000.0);
            peer.Tick(2100.0);
            bool peerLeft = peer.State == VpbNetSessionState.Dropped
                && peer.Reason == VpbNetDropReason.PeerLeave
                && peer.ReconnectAttempts == 0;

            VpbNetSession self = Running(1000.0);
            self.LocalLeave(2000.0);
            self.Tick(2100.0);
            bool selfLeft = self.State == VpbNetSessionState.Idle
                && self.Reason == VpbNetDropReason.LocalLeave
                && !self.HasCredentials
                && !self.ConnectWanted;

            Check(log, ref pass, ref fail, peerLeft && selfLeft,
                "a graceful leave on either side is terminal - no reconnect storm after someone deliberately left",
                "graceful leave retried: peer=" + peer.State + "/" + peer.Reason
                    + " self=" + self.State + "/" + self.Reason);
        }

        static void FatalNeverRetries(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetDropReason[] fatal =
            {
                VpbNetDropReason.VersionMismatch,
                VpbNetDropReason.AuthFailed,
                VpbNetDropReason.ContentMismatch,
                VpbNetDropReason.Kicked
            };

            int terminal = 0;
            for (int i = 0; i < fatal.Length; i++)
            {
                VpbNetSession s = Running(1000.0);
                if (fatal[i] == VpbNetDropReason.Kicked) s.OnKicked(2000.0);
                else s.OnFatal(2000.0, fatal[i]);
                s.Tick(2100.0);
                if (s.State == VpbNetSessionState.Dropped && s.Reason == fatal[i]) terminal++;
            }

            Check(log, ref pass, ref fail, terminal == fatal.Length,
                "version mismatch, bad code, missing content and being kicked are terminal - retrying them cannot help ("
                    + terminal + "/" + fatal.Length + ")",
                "a fatal reason was retried: only " + terminal + "/" + fatal.Length + " terminal");
        }

        static void ConnectTimeoutNamesAllThree(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = new VpbNetSession();
            s.Begin(1000.0);
            Advance(s, 1000.0, 1000.0 + VpbNetSession.ConnectTimeoutMs + 500.0);

            string text = s.DescribeReason();
            string lower = text.ToLowerInvariant();
            bool code = lower.IndexOf("room code", StringComparison.Ordinal) >= 0;
            bool addr = lower.IndexOf("address", StringComparison.Ordinal) >= 0;
            bool fw = lower.IndexOf("firewall", StringComparison.Ordinal) >= 0;

            Check(log, ref pass, ref fail,
                s.State == VpbNetSessionState.Dropped && s.Reason == VpbNetDropReason.ConnectTimeout
                    && code && addr && fw,
                "a connect timeout names all three indistinguishable causes: wrong code, wrong address, blocked port",
                "connect timeout text incomplete: code=" + code + " address=" + addr + " firewall=" + fw);
        }

        static void EveryReasonActionable(StringBuilder log, ref int pass, ref int fail)
        {
            Array all = Enum.GetValues(typeof(VpbNetDropReason));
            int shortest = int.MaxValue;
            int missing = 0;
            string worst = string.Empty;

            for (int i = 0; i < all.Length; i++)
            {
                VpbNetDropReason why = (VpbNetDropReason)all.GetValue(i);
                if (why == VpbNetDropReason.None) continue;

                VpbNetSession s = new VpbNetSession();
                s.Begin(0.0);
                s.OnFatal(1.0, why);
                string text = s.DescribeReason();

                if (string.IsNullOrEmpty(text) || text.Length < 20)
                {
                    missing++;
                    worst = why.ToString();
                    continue;
                }
                if (text.Length < shortest)
                {
                    shortest = text.Length;
                    worst = why.ToString();
                }
            }

            Check(log, ref pass, ref fail, missing == 0,
                "all " + (all.Length - 1) + " drop reasons carry human text (shortest is "
                    + worst + " at " + shortest + " chars)",
                missing + " drop reasons have no usable text, e.g. " + worst);

            VpbNetSession st = new VpbNetSession();
            st.Begin(0.0);
            bool statesOk = true;
            Array states = Enum.GetValues(typeof(VpbNetSessionState));
            for (int i = 0; i < states.Length; i++)
            {
                if (st.DescribeState() == null) statesOk = false;
            }

            Check(log, ref pass, ref fail, statesOk,
                "every session state has a description for the diagnostics overlay",
                "a session state has no description");
        }

        static void DriveGating(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetSession s = new VpbNetSession();
            s.Begin(1000.0);
            bool notWhileConnecting = !s.CanDriveAvatar;

            s.OnTransportUp(1100.0);
            bool notWhileSyncing = !s.CanDriveAvatar;

            s.OnClockReady(true, 1200.0);
            s.Tick(1200.0);
            bool drivesWhenRunning = s.CanDriveAvatar;

            s.OnClockReady(false, 1300.0);
            bool stopsIfClockLost = !s.CanDriveAvatar && s.State == VpbNetSessionState.Syncing;

            Check(log, ref pass, ref fail,
                notWhileConnecting && notWhileSyncing && drivesWhenRunning && stopsIfClockLost,
                "the avatar is driven only once there is a shared timeline, and stops if clock sync is lost",
                "drive gating wrong: connecting=" + notWhileConnecting + " syncing=" + notWhileSyncing
                    + " running=" + drivesWhenRunning + " clockLost=" + stopsIfClockLost);
        }

        // Step like a running process — a 10s Tick jump is "this process was gone", not waiting.
        static double Advance(VpbNetSession s, double from, double to, double stepMs = 250.0)
        {
            double t = from;
            while (t < to)
            {
                t += stepMs;
                s.Tick(t);
                if (s.State == VpbNetSessionState.Reconnecting || s.State == VpbNetSessionState.Dropped) break;
            }
            return t;
        }

        static VpbNetSession Running(double t)
        {
            VpbNetSession s = new VpbNetSession();
            s.Begin(t);
            s.OnTransportUp(t + 50.0);
            s.OnClockReady(true, t + 100.0);
            s.Tick(t + 100.0);
            return s;
        }

        static string F(double v, int decimals)
        {
            return v.ToString("F" + decimals.ToString());
        }

        static void Check(StringBuilder log, ref int pass, ref int fail, bool ok, string passText, string failText)
        {
            if (ok)
            {
                pass++;
                Line(log, "PASS  " + passText);
            }
            else
            {
                fail++;
                Line(log, "FAIL  " + failText);
            }
        }

        static void Line(StringBuilder log, string s)
        {
            if (log == null) return;
            log.Append(s);
            log.Append('\n');
        }
    }
}
