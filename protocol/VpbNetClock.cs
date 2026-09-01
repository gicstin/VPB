using System;
using System.Text;

namespace VpbNet
{
    public sealed class VpbNetClock
    {
        public const int RttWindow = 64;
        public const int SyncSamples = 4;
        public const double AcceptMarginMs = 4.0;
        public const double AcceptScale = 0.5;
        public const double OffsetAlpha = 0.05;
        public const double RttAlpha = 0.125;
        public const double JitterAlpha = 0.25;
        public const double StepThresholdMs = 250.0;
        public const int StepConfirmSamples = 3;

        readonly double[] _rttRing = new double[RttWindow];
        readonly double _ticksToMs;

        int _rttCount;
        int _rttHead;

        double _offsetMs;
        double _rttMs;
        double _jitterMs;
        double _minRttMs;

        int _samples;
        int _accepted;
        int _rejected;
        int _steps;
        int _stepRun;
        double _stepCandidate;
        bool _synced;

        public VpbNetClock(long ticksPerSecond)
        {
            _ticksToMs = ticksPerSecond > 0 ? 1000.0 / ticksPerSecond : 1.0;
            Reset();
        }

        public double OffsetMs { get { return _offsetMs; } }
        public double RttMs { get { return _rttMs; } }
        public double JitterMs { get { return _jitterMs; } }
        public double MinRttMs { get { return _minRttMs; } }
        public bool Synced { get { return _synced; } }
        public int Samples { get { return _samples; } }
        public int Accepted { get { return _accepted; } }
        public int Rejected { get { return _rejected; } }
        public int Steps { get { return _steps; } }

        public void Reset()
        {
            _rttCount = 0;
            _rttHead = 0;
            _offsetMs = 0.0;
            _rttMs = 0.0;
            _jitterMs = 0.0;
            _minRttMs = 0.0;
            _samples = 0;
            _accepted = 0;
            _rejected = 0;
            _stepRun = 0;
            _stepCandidate = 0.0;
            _synced = false;
        }

        public bool AddSample(long t0LocalTicks, long tRemoteTicks, long t2LocalTicks)
        {
            _samples++;

            long rttTicks = t2LocalTicks - t0LocalTicks;
            if (rttTicks < 0) { _rejected++; return false; }

            double rtt = rttTicks * _ticksToMs;
            double midLocal = (t0LocalTicks + rttTicks * 0.5) * _ticksToMs;
            double remote = tRemoteTicks * _ticksToMs;
            double sample = remote - midLocal;

            PushRtt(rtt);

            if (_rttCount == 1)
            {
                _rttMs = rtt;
                _jitterMs = 0.0;
            }
            else
            {
                double err = rtt - _rttMs;
                if (err < 0.0) err = -err;
                _jitterMs += JitterAlpha * (err - _jitterMs);
                _rttMs += RttAlpha * (rtt - _rttMs);
            }

            if (!_synced)
            {
                _accepted++;
                _offsetMs = _accepted == 1 ? sample : _offsetMs + (sample - _offsetMs) / _accepted;
                if (_accepted >= SyncSamples) _synced = true;
                return true;
            }

            if (rtt > _minRttMs + _minRttMs * AcceptScale + AcceptMarginMs)
            {
                _rejected++;
                CheckStep(sample);
                return false;
            }

            double delta = sample - _offsetMs;
            double adelta = delta < 0.0 ? -delta : delta;
            if (adelta > StepThresholdMs)
            {
                _rejected++;
                CheckStep(sample);
                return false;
            }

            _stepRun = 0;
            _accepted++;
            _offsetMs += OffsetAlpha * delta;
            return true;
        }

        void CheckStep(double sample)
        {
            double gap = sample - _stepCandidate;
            if (gap < 0.0) gap = -gap;

            if (_stepRun > 0 && gap < StepThresholdMs)
            {
                _stepRun++;
            }
            else
            {
                _stepRun = 1;
                _stepCandidate = sample;
            }

            if (_stepRun < StepConfirmSamples) return;

            double drift = sample - _offsetMs;
            if (drift < 0.0) drift = -drift;
            if (drift <= StepThresholdMs) return;

            _offsetMs = sample;
            _steps++;
            _stepRun = 0;
            _rttCount = 0;
            _rttHead = 0;
            _minRttMs = 0.0;
        }

        void PushRtt(double rtt)
        {
            _rttRing[_rttHead] = rtt;
            _rttHead++;
            if (_rttHead >= RttWindow) _rttHead = 0;
            if (_rttCount < RttWindow) _rttCount++;

            double min = _rttRing[0];
            for (int i = 1; i < _rttCount; i++)
            {
                if (_rttRing[i] < min) min = _rttRing[i];
            }
            _minRttMs = min;
        }

        public double RemoteToLocalMs(double remoteMs)
        {
            return remoteMs - _offsetMs;
        }

        public double LocalToRemoteMs(double localMs)
        {
            return localMs + _offsetMs;
        }
    }

    public sealed class VpbNetTimeline
    {
        public const double MinBufferMs = 45.0;
        public const double MaxBufferMs = 150.0;
        public const double JitterFactor = 3.0;
        public const double IntervalFactor = 2.0;
        public const double SlewMsPerSecond = 25.0;

        readonly VpbNetClock _clock;

        double _frameIntervalMs;
        double _delayMs;
        double _lastRenderMs;
        double _lastLocalMs;
        bool _haveRender;
        bool _syncedSeen;
        int _stepsSeen;
        int _snaps;
        int _rewindsBlocked;

        public VpbNetTimeline(VpbNetClock clock, double frameIntervalMs)
        {
            _clock = clock;
            _frameIntervalMs = frameIntervalMs > 0.0 ? frameIntervalMs : 1000.0 / 45.0;
            _delayMs = MinBufferMs;
        }

        public double DelayMs { get { return _delayMs; } }
        public double TargetDelayMs { get { return ComputeTarget(); } }
        public bool Ready { get { return _clock.Synced; } }
        public int Snaps { get { return _snaps; } }
        public double TransitMs { get { return _clock.Synced ? _clock.MinRttMs * 0.5 : 0.0; } }
        public double BufferMs { get { return ComputeBuffer(); } }
        public int RewindsBlocked { get { return _rewindsBlocked; } }

        public void SetFrameInterval(double ms)
        {
            if (ms > 0.0) _frameIntervalMs = ms;
        }

        public void Reset()
        {
            _delayMs = MinBufferMs;
            _haveRender = false;
            _syncedSeen = false;
            _stepsSeen = 0;
            _snaps = 0;
            _lastRenderMs = 0.0;
            _lastLocalMs = 0.0;
            _rewindsBlocked = 0;
        }

        double ComputeBuffer()
        {
            double buffer = IntervalFactor * _frameIntervalMs + JitterFactor * _clock.JitterMs;
            if (buffer < MinBufferMs) buffer = MinBufferMs;
            else if (buffer > MaxBufferMs) buffer = MaxBufferMs;
            return buffer;
        }

        double ComputeTarget()
        {
            return TransitMs + ComputeBuffer();
        }

        public double RenderRemoteMs(double nowLocalMs)
        {
            double elapsed = _haveRender ? nowLocalMs - _lastLocalMs : 0.0;
            if (elapsed < 0.0) elapsed = 0.0;
            _lastLocalMs = nowLocalMs;

            bool snap = !_haveRender;
            if (_clock.Synced && (!_syncedSeen || _clock.Steps != _stepsSeen))
            {
                snap = true;
                _syncedSeen = true;
                _stepsSeen = _clock.Steps;
            }

            double target = ComputeTarget();
            if (snap)
            {
                _snaps++;
                _delayMs = target;
            }
            else
            {
                double slew = SlewMsPerSecond * elapsed / 1000.0;
                double diff = target - _delayMs;
                if (diff > slew) diff = slew;
                else if (diff < -slew) diff = -slew;
                _delayMs += diff;
            }

            double render = _clock.LocalToRemoteMs(nowLocalMs) - _delayMs;

            if (_haveRender && render < _lastRenderMs)
            {
                _rewindsBlocked++;
                render = _lastRenderMs;
            }

            _lastRenderMs = render;
            _haveRender = true;
            return render;
        }
    }

    public static class VpbNetClockSelfTest
    {
        const long Hz = 10000000L;

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

            Line(log, "===== clock sync + timeline self-test =====");

            IdealLink(log, ref pass, ref fail);
            WanLink(log, ref pass, ref fail);
            DrainLagLink(log, ref pass, ref fail);
            AsymmetricLink(log, ref pass, ref fail);
            DriftLink(log, ref pass, ref fail);
            StepLink(log, ref pass, ref fail);
            TimelineChecks(log, ref pass, ref fail);

            Line(log, "-----");
            Line(log, "passed=" + pass + " failed=" + fail);
            Line(log, "EXIT 1/4 accuracy  ideal <0.5ms, WAN 150/30 <5ms          : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 2/4 drain lag one-sided 340ms spikes rejected        : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 3/4 recovery  clock step resynced, drift tracked     : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            Line(log, "EXIT 4/4 timeline  monotonic, slew-limited, 45-150ms      : " + (fail == 0 ? "PASS" : "see FAIL lines"));
            if (fail == 0) Line(log, "RESULT: PASS");
            else Line(log, "RESULT: FAIL");
            Line(log, "===== end clock sync self-test =====");
            return fail == 0;
        }

        static double Converge(VpbNetClock clock, Random rng, double trueOffsetMs, int count,
            double upMs, double downMs, double jitterMs, double spikeChance, double spikeMs,
            double driftPpm, double pingIntervalMs, ref double naiveOffsetMs)
        {
            double localMs = 1000.0;
            for (int i = 0; i < count; i++)
            {
                double up = upMs + Jitter(rng, jitterMs);
                double down = downMs + Jitter(rng, jitterMs);
                if (up < 0.0) up = 0.0;
                if (down < 0.0) down = 0.0;
                if (spikeChance > 0.0 && rng.NextDouble() < spikeChance) down += spikeMs;

                double t0 = localMs;
                double arrive = t0 + up;
                double drift = driftPpm * 0.000001 * arrive;
                double remoteAt = arrive + trueOffsetMs + drift;
                double t2 = arrive + down;

                clock.AddSample(MsToTicks(t0), MsToTicks(remoteAt), MsToTicks(t2));

                double naiveSample = remoteAt - (t0 + (t2 - t0) * 0.5);
                naiveOffsetMs = i == 0 ? naiveSample : naiveOffsetMs + 0.05 * (naiveSample - naiveOffsetMs);

                localMs = t2 + pingIntervalMs;
            }
            return localMs;
        }

        static void IdealLink(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(11);
            double naive = 0.0;
            const double TrueOffset = 987654.25;
            Converge(c, rng, TrueOffset, 200, 0.0625, 0.0625, 0.02, 0.0, 0.0, 0.0, 1000.0, ref naive);

            double err = Abs(c.OffsetMs - TrueOffset);
            Check(log, ref pass, ref fail, err < 0.5,
                "ideal LAN offset err " + F(err, 4) + "ms (rtt " + F(c.RttMs, 4) + "ms)",
                "ideal LAN offset err " + F(err, 4) + "ms, want <0.5");
        }

        static void WanLink(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(22);
            double naive = 0.0;
            const double TrueOffset = -45231.5;
            Converge(c, rng, TrueOffset, 300, 75.0, 75.0, 30.0, 0.0, 0.0, 0.0, 1000.0, ref naive);

            double err = Abs(c.OffsetMs - TrueOffset);
            Check(log, ref pass, ref fail, err < 5.0,
                "WAN 150ms/30ms jitter offset err " + F(err, 3) + "ms, jitter est " + F(c.JitterMs, 1) + "ms",
                "WAN offset err " + F(err, 3) + "ms, want <5");
        }

        static void DrainLagLink(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(33);
            double naive = 0.0;
            const double TrueOffset = 5000.0;
            Converge(c, rng, TrueOffset, 400, 0.0625, 0.0625, 0.02, 0.15, 340.0, 0.0, 1000.0, ref naive);

            double err = Abs(c.OffsetMs - TrueOffset);
            double naiveErr = Abs(naive - TrueOffset);

            Check(log, ref pass, ref fail, err < 2.0,
                "one-sided drain lag (15% x 340ms) offset err " + F(err, 3) + "ms, "
                    + c.Rejected + "/" + c.Samples + " samples rejected",
                "drain lag offset err " + F(err, 3) + "ms, want <2");

            Check(log, ref pass, ref fail, naiveErr > err * 5.0,
                "filtering earns its keep: naive EWMA err " + F(naiveErr, 1) + "ms vs filtered " + F(err, 3) + "ms",
                "naive EWMA err " + F(naiveErr, 1) + "ms is not materially worse than filtered "
                    + F(err, 3) + "ms - filtering may be unnecessary");
        }

        static void AsymmetricLink(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(44);
            double naive = 0.0;
            const double TrueOffset = 0.0;
            const double Up = 10.0;
            const double Down = 40.0;
            Converge(c, rng, TrueOffset, 300, Up, Down, 1.0, 0.0, 0.0, 0.0, 1000.0, ref naive);

            double expected = (Down - Up) * 0.5;
            double err = Abs(c.OffsetMs - TrueOffset);
            double residual = Abs(err - expected);

            Check(log, ref pass, ref fail, residual < 2.0,
                "asymmetric path 10/40ms: offset err " + F(err, 2) + "ms == half the asymmetry ("
                    + F(expected, 1) + "ms), the information-theoretic floor",
                "asymmetric path err " + F(err, 2) + "ms, expected about " + F(expected, 1) + "ms");
        }

        static void DriftLink(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(55);
            double naive = 0.0;
            const double TrueOffset = 100.0;
            const double Ppm = 100.0;
            double endLocal = Converge(c, rng, TrueOffset, 600, 1.0, 1.0, 0.2, 0.0, 0.0, Ppm, 1000.0, ref naive);

            double expected = TrueOffset + Ppm * 0.000001 * endLocal;
            double err = Abs(c.OffsetMs - expected);
            Check(log, ref pass, ref fail, err < 5.0,
                "100 ppm drift over " + F(endLocal / 1000.0, 0) + "s tracked to " + F(err, 3) + "ms",
                "drift tracking err " + F(err, 3) + "ms, want <5");
        }

        static void StepLink(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(66);
            double naive = 0.0;
            Converge(c, rng, 1000.0, 100, 1.0, 1.0, 0.2, 0.0, 0.0, 0.0, 1000.0, ref naive);
            double before = c.OffsetMs;

            int stepsBefore = c.Steps;
            Converge(c, rng, 9000.0, 40, 1.0, 1.0, 0.2, 0.0, 0.0, 0.0, 1000.0, ref naive);
            double err = Abs(c.OffsetMs - 9000.0);

            Check(log, ref pass, ref fail, c.Steps > stepsBefore && err < 5.0,
                "8s clock step resynced (" + (c.Steps - stepsBefore) + " step, err " + F(err, 3) + "ms after)",
                "clock step not recovered: offset " + F(c.OffsetMs, 1) + " from " + F(before, 1)
                    + ", steps " + (c.Steps - stepsBefore) + ", err " + F(err, 1) + "ms");

            VpbNetClock c2 = new VpbNetClock(Hz);
            Random rng2 = new Random(77);
            double naive2 = 0.0;
            Converge(c2, rng2, 1000.0, 60, 1.0, 1.0, 0.2, 0.0, 0.0, 0.0, 1000.0, ref naive2);
            double steady = c2.OffsetMs;
            int steps2 = c2.Steps;

            double localMs = 100000.0;
            for (int i = 0; i < 3; i++)
            {
                double t0 = localMs;
                double arrive = t0 + 1.0;
                c2.AddSample(MsToTicks(t0), MsToTicks(arrive + 1000.0 + 4000.0), MsToTicks(arrive + 1.0));
                localMs = arrive + 1.0 + 5000.0;
                double t0b = localMs;
                double arriveB = t0b + 1.0;
                c2.AddSample(MsToTicks(t0b), MsToTicks(arriveB + 1000.0), MsToTicks(arriveB + 1.0));
                localMs = arriveB + 1.0 + 1000.0;
            }

            double moved = Abs(c2.OffsetMs - steady);
            Check(log, ref pass, ref fail, c2.Steps == steps2 && moved < 5.0,
                "isolated outliers never trigger a resync (offset moved " + F(moved, 3) + "ms)",
                "isolated outliers moved the offset " + F(moved, 2) + "ms / caused "
                    + (c2.Steps - steps2) + " resync");
        }

        static void TimelineChecks(StringBuilder log, ref int pass, ref int fail)
        {
            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(88);
            double naive = 0.0;
            Converge(c, rng, 0.0, 200, 75.0, 75.0, 30.0, 0.0, 0.0, 0.0, 1000.0, ref naive);

            VpbNetTimeline tl = new VpbNetTimeline(c, 1000.0 / 45.0);

            double t = 0.0;
            double prev = double.NegativeInfinity;
            bool monotonic = true;
            double maxJump = 0.0;
            double prevDelay = tl.DelayMs;
            double joinTarget = tl.TargetDelayMs;
            double joinDelay = -1.0;
            for (int i = 0; i < 2000; i++)
            {
                t += 1000.0 / 90.0;
                double r = tl.RenderRemoteMs(t);
                if (r < prev) monotonic = false;
                prev = r;
                if (i == 0) joinDelay = tl.DelayMs;
                else
                {
                    double d = Abs(tl.DelayMs - prevDelay);
                    if (d > maxJump) maxJump = d;
                }
                prevDelay = tl.DelayMs;
            }

            double allowed = VpbNetTimeline.SlewMsPerSecond * (1000.0 / 90.0) / 1000.0 + 0.0001;
            Check(log, ref pass, ref fail, monotonic,
                "render time is monotonic across 2000 frames",
                "render time went backwards - the applier would interpolate in reverse");
            Check(log, ref pass, ref fail, Abs(joinDelay - joinTarget) < 0.001,
                "the first sample SNAPS to " + F(joinDelay, 1)
                    + "ms instead of slewing - at 25ms/s a 150ms link would spend ~9s extrapolating on join",
                "join did not snap: delay " + F(joinDelay, 1) + "ms vs target " + F(joinTarget, 1) + "ms");
            Check(log, ref pass, ref fail, maxJump <= allowed,
                "after join the delay is slew limited: max step " + F(maxJump, 4) + "ms <= " + F(allowed, 4) + "ms/frame",
                "delay jumped " + F(maxJump, 4) + "ms in one frame after join, limit " + F(allowed, 4));
            Check(log, ref pass, ref fail,
                tl.BufferMs >= VpbNetTimeline.MinBufferMs - 0.001 && tl.BufferMs <= VpbNetTimeline.MaxBufferMs + 0.001,
                "jitter buffer settled at " + F(tl.BufferMs, 1) + "ms inside the 45-150ms band (jitter "
                    + F(c.JitterMs, 1) + "ms)",
                "jitter buffer " + F(tl.BufferMs, 1) + "ms outside the 45-150ms band");

            Check(log, ref pass, ref fail, tl.DelayMs >= tl.TransitMs + VpbNetTimeline.MinBufferMs - 0.5,
                "delay " + F(tl.DelayMs, 1) + "ms covers one-way transit " + F(tl.TransitMs, 1)
                    + "ms plus the buffer - the cursor sits behind the freshest frame, not ahead of it",
                "delay " + F(tl.DelayMs, 1) + "ms does not cover transit " + F(tl.TransitMs, 1)
                    + "ms: the render cursor is AHEAD of arrived data and will extrapolate forever");

            VpbNetClock quiet = new VpbNetClock(Hz);
            Random rng2 = new Random(99);
            double naive2 = 0.0;
            Converge(quiet, rng2, 0.0, 200, 0.0625, 0.0625, 0.02, 0.0, 0.0, 0.0, 1000.0, ref naive2);
            VpbNetTimeline tl2 = new VpbNetTimeline(quiet, 1000.0 / 45.0);
            double t2 = 0.0;
            for (int i = 0; i < 2000; i++)
            {
                t2 += 1000.0 / 90.0;
                tl2.RenderRemoteMs(t2);
            }
            Check(log, ref pass, ref fail, Abs(tl2.DelayMs - VpbNetTimeline.MinBufferMs) < 0.5,
                "a clean LAN settles to the 45ms floor, not the ceiling",
                "clean LAN delay settled at " + F(tl2.DelayMs, 1) + "ms, want the 45ms floor");

            ArrivalCursor(log, ref pass, ref fail);
            ColdStart(log, ref pass, ref fail);

            VpbNetClock back = new VpbNetClock(Hz);
            Random rng3 = new Random(101);
            double naive3 = 0.0;
            Converge(back, rng3, 0.0, 100, 1.0, 1.0, 0.2, 0.0, 0.0, 0.0, 1000.0, ref naive3);
            VpbNetTimeline tl3 = new VpbNetTimeline(back, 1000.0 / 45.0);
            double t3 = 0.0;
            for (int i = 0; i < 200; i++) { t3 += 11.0; tl3.RenderRemoteMs(t3); }
            double atStall = tl3.RenderRemoteMs(t3);
            double afterRepeat = tl3.RenderRemoteMs(t3);
            Check(log, ref pass, ref fail, afterRepeat >= atStall,
                "a repeated timestamp does not rewind the timeline",
                "a repeated timestamp rewound the timeline");
        }

        static void ArrivalCursor(StringBuilder log, ref int pass, ref int fail)
        {
            const double OneWay = 150.0;
            const double Jit = 30.0;
            const double SendInterval = 1000.0 / 45.0;

            VpbNetClock c = new VpbNetClock(Hz);
            Random rng = new Random(202);
            double naive = 0.0;
            Converge(c, rng, 0.0, 300, OneWay, OneWay, Jit, 0.0, 0.0, 0.0, 1000.0, ref naive);
            VpbNetTimeline tl = new VpbNetTimeline(c, SendInterval);

            double[] dueAt = new double[8192];
            double[] stamp = new double[8192];
            int qn = 0;

            double local = 0.0;
            double newestArrived = double.NegativeInfinity;
            double nextSend = 0.0;
            int total = 0;
            int aheadCount = 0;
            double worstAhead = 0.0;
            double worstBehind = 0.0;

            for (int i = 0; i < 4000; i++)
            {
                local += 1000.0 / 90.0;

                while (nextSend <= local)
                {
                    double due = nextSend + OneWay + Jitter(rng, Jit);
                    if (qn < dueAt.Length)
                    {
                        dueAt[qn] = due;
                        stamp[qn] = nextSend;
                        qn++;
                    }
                    nextSend += SendInterval;
                }

                int w = 0;
                for (int k = 0; k < qn; k++)
                {
                    if (dueAt[k] > local)
                    {
                        dueAt[w] = dueAt[k];
                        stamp[w] = stamp[k];
                        w++;
                        continue;
                    }
                    if (stamp[k] > newestArrived) newestArrived = stamp[k];
                }
                qn = w;

                double render = tl.RenderRemoteMs(local);
                if (newestArrived == double.NegativeInfinity) continue;

                total++;
                double delta = render - newestArrived;
                if (delta > 0.0)
                {
                    aheadCount++;
                    if (delta > worstAhead) worstAhead = delta;
                }
                else if (-delta > worstBehind)
                {
                    worstBehind = -delta;
                }
            }

            Check(log, ref pass, ref fail, aheadCount == 0,
                "over a 150ms/30ms link the render cursor never outruns arrived data ("
                    + total + " frames, worst lag behind newest " + F(worstBehind, 1) + "ms)",
                "render cursor outran arrived data on " + aheadCount + "/" + total
                    + " frames by up to " + F(worstAhead, 1)
                    + "ms - the applier would extrapolate permanently instead of interpolating");

            Check(log, ref pass, ref fail, tl.DelayMs > OneWay,
                "delay " + F(tl.DelayMs, 1) + "ms exceeds the " + F(OneWay, 0) + "ms one-way transit",
                "delay " + F(tl.DelayMs, 1) + "ms is under the " + F(OneWay, 0) + "ms one-way transit");
        }

        static void ColdStart(StringBuilder log, ref int pass, ref int fail)
        {
            const double OneWay = 150.0;
            const double Jit = 30.0;
            const double SendInterval = 1000.0 / 45.0;

            VpbNetClock c = new VpbNetClock(Hz);
            VpbNetTimeline tl = new VpbNetTimeline(c, SendInterval);
            Random rng = new Random(303);

            double[] dueAt = new double[8192];
            double[] stamp = new double[8192];
            int qn = 0;

            double local = 0.0;
            double nextSend = 0.0;
            double nextPing = 0.0;
            double newestArrived = double.NegativeInfinity;
            double readyAt = -1.0;
            int total = 0;
            int aheadCount = 0;
            double worstAhead = 0.0;

            for (int i = 0; i < 3000; i++)
            {
                local += 1000.0 / 90.0;

                if (local >= nextPing)
                {
                    nextPing = local + (c.Synced ? 1000.0 : 100.0);
                    double up = OneWay + Jitter(rng, Jit);
                    double down = OneWay + Jitter(rng, Jit);
                    double t0 = local - (up + down);
                    if (t0 >= 0.0)
                        c.AddSample(MsToTicks(t0), MsToTicks(t0 + up), MsToTicks(local));
                }

                while (nextSend <= local)
                {
                    double due = nextSend + OneWay + Jitter(rng, Jit);
                    if (qn < dueAt.Length)
                    {
                        dueAt[qn] = due;
                        stamp[qn] = nextSend;
                        qn++;
                    }
                    nextSend += SendInterval;
                }

                int w = 0;
                for (int k = 0; k < qn; k++)
                {
                    if (dueAt[k] > local)
                    {
                        dueAt[w] = dueAt[k];
                        stamp[w] = stamp[k];
                        w++;
                        continue;
                    }
                    if (stamp[k] > newestArrived) newestArrived = stamp[k];
                }
                qn = w;

                if (!tl.Ready) continue;
                if (readyAt < 0.0) readyAt = local;

                double render = tl.RenderRemoteMs(local);
                if (newestArrived == double.NegativeInfinity) continue;

                total++;
                double delta = render - newestArrived;
                if (delta > 0.0)
                {
                    aheadCount++;
                    if (delta > worstAhead) worstAhead = delta;
                }
            }

            Check(log, ref pass, ref fail, aheadCount == 0,
                "COLD START on a 150ms link: from the moment the clock is ready the cursor never outruns arrived data ("
                    + total + " frames, " + tl.Snaps + " snap)",
                "cold start extrapolated on " + aheadCount + "/" + total + " frames by up to "
                    + F(worstAhead, 1) + "ms - the delay did not snap when the clock became ready");

            Check(log, ref pass, ref fail, readyAt > 0.0 && readyAt < 1500.0,
                "clock is ready " + F(readyAt, 0) + "ms after join (pings burst until synced, then back off)",
                "clock took " + F(readyAt, 0) + "ms to become ready - the avatar cannot be driven until then");
        }

        static double Jitter(Random rng, double magnitude)
        {
            if (magnitude <= 0.0) return 0.0;
            return (rng.NextDouble() * 2.0 - 1.0) * magnitude;
        }

        static long MsToTicks(double ms)
        {
            return (long)(ms * (Hz / 1000.0));
        }

        static double Abs(double v)
        {
            return v < 0.0 ? -v : v;
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
