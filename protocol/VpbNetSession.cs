using System;

namespace VpbNet
{
    public enum VpbNetSessionState
    {
        Idle = 0,
        Connecting = 1,
        Syncing = 2,
        Running = 3,
        Stalled = 4,
        Reconnecting = 5,
        Dropped = 6
    }

    public enum VpbNetDropReason
    {
        None = 0,
        LocalLeave = 1,
        PeerLeave = 2,
        ConnectTimeout = 3,
        DataTimeout = 4,
        TransportError = 5,
        VersionMismatch = 6,
        AuthFailed = 7,
        ContentMismatch = 8,
        ReconnectExhausted = 9,
        Kicked = 10
    }

    public sealed class VpbNetSession
    {
        public const double StallMs = 2000.0;

        // 30s not 10s — VaM load/preset frames sat below the old timeout.
        public const double DataTimeoutMs = 30000.0;

        public const double ConnectTimeoutMs = 8000.0;
        public const double SyncTimeoutMs = 8000.0;
        public const int MaxReconnectAttempts = 5;

        // Gap this long between Ticks is this process not running, not a slow frame.
        public const double LocalStallThresholdMs = 1500.0;

        // Credit back time we were asleep; bound so hitching cannot hide a dead peer forever.
        public const double MaxCreditPerSilenceMs = 120000.0;

        // Cap on how long a peer may claim to be busy — a mid-load death must not hold forever.
        public const double MaxPeerBusyMs = 180000.0;

        static readonly double[] BackoffMs = { 250.0, 500.0, 1000.0, 2000.0, 4000.0 };

        VpbNetSessionState _state = VpbNetSessionState.Idle;
        VpbNetDropReason _reason = VpbNetDropReason.None;

        bool _haveCredentials;
        bool _clockReady;
        int _attempts;
        int _stalls;
        int _resumes;

        double _stateEnteredMs;
        double _lastDataMs;
        double _lastAliveMs;
        double _nextAttemptMs;
        bool _connectWanted;
        bool _peerStreams = true;

        double _lastTickMs;
        double _creditMs;
        int _localStalls;
        double _localStallTotalMs;
        double _longestLocalStallMs;

        double _peerBusyUntilMs;
        string _peerBusyReason = string.Empty;
        string _peerBusyText = string.Empty;
        bool _peerBusy;
        int _peerBusyHolds;

        public VpbNetSessionState State { get { return _state; } }
        public VpbNetDropReason Reason { get { return _reason; } }
        public bool HasCredentials { get { return _haveCredentials; } }
        public int ReconnectAttempts { get { return _attempts; } }
        public int Stalls { get { return _stalls; } }
        public int Resumes { get { return _resumes; } }

        public int LocalStalls { get { return _localStalls; } }
        public double LocalStallTotalMs { get { return _localStallTotalMs; } }
        public double LongestLocalStallMs { get { return _longestLocalStallMs; } }

        public bool PeerBusy { get { return _peerBusy; } }
        public string PeerBusyReason { get { return _peerBusyReason; } }
        public int PeerBusyHolds { get { return _peerBusyHolds; } }

        public bool IsTerminal
        {
            get { return _state == VpbNetSessionState.Dropped || _state == VpbNetSessionState.Idle; }
        }

        public bool CanDriveAvatar
        {
            get { return _state == VpbNetSessionState.Running || _state == VpbNetSessionState.Stalled; }
        }

        public bool PeerLooksStalled
        {
            get { return _state == VpbNetSessionState.Stalled || _state == VpbNetSessionState.Reconnecting; }
        }

        public bool ConnectWanted { get { return _connectWanted; } }

        public bool PeerStreams { get { return _peerStreams; } }

        // Spectator sends no pose — silence on data is not absence; ping clock still required.
        public void SetPeerStreaming(bool streaming)
        {
            _peerStreams = streaming;
        }

        public void NotePeerAlive(double nowMs)
        {
            if (IsTerminal) return;
            _lastAliveMs = nowMs;
        }

        public void Begin(double nowMs)
        {
            _haveCredentials = true;
            _clockReady = false;
            _attempts = 0;
            _stalls = 0;
            _resumes = 0;
            _reason = VpbNetDropReason.None;
            Enter(VpbNetSessionState.Connecting, nowMs);
            _lastDataMs = nowMs;
            _lastAliveMs = nowMs;
            _peerStreams = true;
            _connectWanted = true;
            _lastTickMs = nowMs;
            _creditMs = 0.0;
            ClearPeerBusy();
        }

        public void OnTransportUp(double nowMs)
        {
            if (_state != VpbNetSessionState.Connecting && _state != VpbNetSessionState.Reconnecting) return;
            _connectWanted = false;
            _lastDataMs = nowMs;
            _lastAliveMs = nowMs;
            Enter(VpbNetSessionState.Syncing, nowMs);
        }

        public void OnTransportDown(double nowMs, VpbNetDropReason why)
        {
            if (IsTerminal) return;
            Drop(nowMs, why == VpbNetDropReason.None ? VpbNetDropReason.TransportError : why);
        }

        public void OnClockReady(bool ready, double nowMs)
        {
            _clockReady = ready;
            if (!ready && _state == VpbNetSessionState.Running)
            {
                Enter(VpbNetSessionState.Syncing, nowMs);
            }
        }

        public void OnData(double nowMs)
        {
            if (IsTerminal) return;
            _lastDataMs = nowMs;
            _lastAliveMs = nowMs;
            _creditMs = 0.0;
            if (_state == VpbNetSessionState.Stalled)
            {
                _resumes++;
                Enter(VpbNetSessionState.Running, nowMs);
            }
        }

        public void OnPeerBye(double nowMs)
        {
            Drop(nowMs, VpbNetDropReason.PeerLeave);
        }

        public void OnKicked(double nowMs)
        {
            Drop(nowMs, VpbNetDropReason.Kicked);
        }

        public void OnFatal(double nowMs, VpbNetDropReason why)
        {
            Drop(nowMs, why);
        }

        public void LocalLeave(double nowMs)
        {
            _reason = VpbNetDropReason.LocalLeave;
            _haveCredentials = false;
            _connectWanted = false;
            Enter(VpbNetSessionState.Idle, nowMs);
        }

        public void AwaitPeer(double nowMs)
        {
            _clockReady = false;
            _connectWanted = false;
            _reason = VpbNetDropReason.None;
            _lastDataMs = nowMs;
            _lastAliveMs = nowMs;
            _peerStreams = true;
            _creditMs = 0.0;
            ClearPeerBusy();
            Enter(VpbNetSessionState.Connecting, nowMs);
        }

        // Sent before the work — once the thread is blocked, the peer cannot say anything.
        public void NotePeerBusy(double nowMs, double expectedMs, string reason)
        {
            if (IsTerminal) return;
            if (expectedMs < 0.0) expectedMs = 0.0;
            if (expectedMs > MaxPeerBusyMs) expectedMs = MaxPeerBusyMs;

            double until = nowMs + expectedMs;
            if (until <= _peerBusyUntilMs) return;

            _peerBusyUntilMs = until;
            _peerBusyReason = reason ?? string.Empty;
            _peerBusy = true;
            _peerBusyHolds++;

            // Compose once here, not in DescribeState — that runs every frame.
            _peerBusyText = _peerBusyReason.Length == 0
                ? "the other person is loading content - holding their last pose"
                : "the other person is " + _peerBusyReason + " - holding their last pose";
        }

        public void NotePeerReady()
        {
            ClearPeerBusy();
        }

        void ClearPeerBusy()
        {
            _peerBusyUntilMs = 0.0;
            _peerBusyReason = string.Empty;
            _peerBusyText = string.Empty;
            _peerBusy = false;
        }

        public void Tick(double nowMs)
        {
            CreditLocalStall(nowMs);
            _peerBusy = nowMs < _peerBusyUntilMs;

            for (int guard = 0; guard < 8; guard++)
            {
                VpbNetSessionState before = _state;
                Step(nowMs);
                if (_state == before) return;
            }
        }

        // Push deadlines by the time we were gone — wall clock ran while this process was blocked.
        void CreditLocalStall(double nowMs)
        {
            double since = nowMs - _lastTickMs;
            bool first = _lastTickMs <= 0.0;
            _lastTickMs = nowMs;

            if (first || since < LocalStallThresholdMs) return;
            if (IsTerminal) return;

            _localStalls++;
            _localStallTotalMs += since;
            if (since > _longestLocalStallMs) _longestLocalStallMs = since;

            double room = MaxCreditPerSilenceMs - _creditMs;
            if (room <= 0.0) return;

            double credit = since < room ? since : room;
            _creditMs += credit;
            _lastDataMs += credit;
            _lastAliveMs += credit;
            _stateEnteredMs += credit;
            _nextAttemptMs += credit;
        }

        void Step(double nowMs)
        {
            switch (_state)
            {
                case VpbNetSessionState.Connecting:
                    if (_connectWanted && nowMs - _stateEnteredMs > ConnectTimeoutMs)
                        Drop(nowMs, VpbNetDropReason.ConnectTimeout);
                    break;

                case VpbNetSessionState.Syncing:
                    if (_clockReady)
                    {
                        _attempts = 0;
                        _lastDataMs = nowMs;
                        _lastAliveMs = nowMs;
                        Enter(VpbNetSessionState.Running, nowMs);
                    }
                    else if (nowMs - _stateEnteredMs > SyncTimeoutMs)
                    {
                        Drop(nowMs, VpbNetDropReason.ConnectTimeout);
                    }
                    break;

                case VpbNetSessionState.Running:
                    if (_peerStreams && nowMs - _lastDataMs > StallMs)
                    {
                        _stalls++;
                        Enter(VpbNetSessionState.Stalled, nowMs);
                    }
                    else if (!_peerStreams && !_peerBusy && nowMs - _lastAliveMs > DataTimeoutMs)
                    {
                        Drop(nowMs, VpbNetDropReason.DataTimeout);
                    }
                    break;

                case VpbNetSessionState.Stalled:
                    if (!_peerStreams)
                    {
                        Enter(VpbNetSessionState.Running, nowMs);
                        break;
                    }
                    if (_peerBusy) break;
                    if (nowMs - _lastAliveMs > DataTimeoutMs)
                        Drop(nowMs, VpbNetDropReason.DataTimeout);
                    break;

                case VpbNetSessionState.Reconnecting:
                    if (!_connectWanted && nowMs >= _nextAttemptMs)
                    {
                        if (_attempts >= MaxReconnectAttempts)
                        {
                            _reason = VpbNetDropReason.ReconnectExhausted;
                            Enter(VpbNetSessionState.Dropped, nowMs);
                            break;
                        }
                        _connectWanted = true;
                        _attempts++;
                        int i = _attempts - 1;
                        if (i >= BackoffMs.Length) i = BackoffMs.Length - 1;
                        _nextAttemptMs = nowMs + BackoffMs[i];
                    }
                    break;
            }
        }

        public void OnAttemptFailed(double nowMs)
        {
            if (_state != VpbNetSessionState.Reconnecting) return;
            _connectWanted = false;
        }

        void Drop(double nowMs, VpbNetDropReason why)
        {
            if (IsTerminal) return;
            _reason = why;
            _connectWanted = false;
            _clockReady = false;

            if (IsRetryable(why) && _haveCredentials && _attempts < MaxReconnectAttempts)
            {
                _nextAttemptMs = nowMs;
                Enter(VpbNetSessionState.Reconnecting, nowMs);
                return;
            }

            if (IsRetryable(why) && _attempts >= MaxReconnectAttempts)
                _reason = VpbNetDropReason.ReconnectExhausted;

            Enter(VpbNetSessionState.Dropped, nowMs);
        }

        public static bool IsRetryable(VpbNetDropReason why)
        {
            return why == VpbNetDropReason.DataTimeout
                || why == VpbNetDropReason.TransportError;
        }

        void Enter(VpbNetSessionState next, double nowMs)
        {
            _state = next;
            _stateEnteredMs = nowMs;
        }

        public string DescribeState()
        {
            switch (_state)
            {
                case VpbNetSessionState.Idle: return "not in a session";
                case VpbNetSessionState.Connecting: return "connecting";
                case VpbNetSessionState.Syncing: return "synchronising clocks";
                case VpbNetSessionState.Running: return "running";
                case VpbNetSessionState.Stalled:
                    if (_peerBusy && _peerBusyText.Length != 0) return _peerBusyText;
                    return "peer stalled - holding their last pose";
                case VpbNetSessionState.Reconnecting: return "connection lost - rejoining automatically";
                case VpbNetSessionState.Dropped: return "disconnected";
            }
            return "unknown";
        }

        public string DescribeReason()
        {
            switch (_reason)
            {
                case VpbNetDropReason.None:
                    return string.Empty;
                case VpbNetDropReason.LocalLeave:
                    return "You left the session.";
                case VpbNetDropReason.PeerLeave:
                    return "The other person left the session. Host a new one or ask them to invite you again.";
                case VpbNetDropReason.ConnectTimeout:
                    return "No answer from the host. A wrong room code, a wrong address and a blocked port all look identical from here, "
                        + "so check the code matches exactly, check the address the host printed, then allow VaM through the firewall.";
                case VpbNetDropReason.DataTimeout:
                    return "The other person stopped sending for " + (int)(DataTimeoutMs / 1000.0)
                        + " seconds, and did not warn us they were loading something. Their game may have"
                        + " frozen or their connection dropped. Rejoining automatically.";
                case VpbNetDropReason.TransportError:
                    return "The connection failed. Rejoining automatically - you will not need the room code again.";
                case VpbNetDropReason.VersionMismatch:
                    return "The other person is running a different VPB version. Both sides need the same version to share a session.";
                case VpbNetDropReason.AuthFailed:
                    return "The room code did not match. Check it character by character - it is case sensitive.";
                case VpbNetDropReason.ContentMismatch:
                    return "You are missing packages the host's scene needs. Open the missing package report to get the Hub links.";
                case VpbNetDropReason.ReconnectExhausted:
                    return "Could not rejoin after " + MaxReconnectAttempts
                        + " attempts. The host may have closed the session or gone offline. Rejoin with the room code when they are back.";
                case VpbNetDropReason.Kicked:
                    return "The host removed you from the session.";
            }
            return "Disconnected for an unknown reason.";
        }
    }
}
