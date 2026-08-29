using System;
using System.Text;

namespace VpbNet
{
    public sealed class VpbNetDiagnostics
    {
        readonly char[] _digits = new char[24];
        readonly int[] _snapshot = new int[19];
        readonly int[] _previous = new int[19];

        bool _havePrevious;

        public VpbNetSessionState State = VpbNetSessionState.Idle;
        public VpbNetDropReason Reason = VpbNetDropReason.None;
        public string TransportMode = "none";
        public string PeerName = string.Empty;
        public int PeerCount;

        public double RttMs;
        public double JitterMs;
        public double OffsetMs;
        public double TransitMs;
        public double BufferMs;
        public double DelayMs;

        public int BufferDepth;
        public double LossPercent;
        public double FrameAgeMs;
        public double ServiceLocalMs;
        public double ServicePeerMs;

        public double SamplerUs;
        public double ApplierUs;

        public int Stalls;
        public int Reconnects;
        public int Interpolated;
        public int Extrapolated;
        public int Frozen;

        public void Reset()
        {
            State = VpbNetSessionState.Idle;
            Reason = VpbNetDropReason.None;
            TransportMode = "none";
            PeerName = string.Empty;
            PeerCount = 0;
            RttMs = 0.0;
            JitterMs = 0.0;
            OffsetMs = 0.0;
            TransitMs = 0.0;
            BufferMs = 0.0;
            DelayMs = 0.0;
            BufferDepth = 0;
            LossPercent = 0.0;
            FrameAgeMs = 0.0;
            SamplerUs = 0.0;
            ApplierUs = 0.0;
            Stalls = 0;
            Reconnects = 0;
            Extrapolated = 0;
            Interpolated = 0;
            Frozen = 0;
            _havePrevious = false;
        }

        public bool HasChanged()
        {
            _snapshot[0] = (int)State;
            _snapshot[1] = (int)Reason;
            _snapshot[2] = PeerCount;
            _snapshot[3] = Quant(RttMs, 10.0);
            _snapshot[4] = Quant(JitterMs, 10.0);
            _snapshot[5] = Quant(DelayMs, 10.0);
            _snapshot[6] = BufferDepth;
            _snapshot[7] = Quant(LossPercent, 100.0);
            _snapshot[8] = Quant(FrameAgeMs, 10.0);
            _snapshot[9] = Quant(SamplerUs, 10.0);
            _snapshot[10] = Quant(ApplierUs, 10.0);
            _snapshot[11] = Stalls;
            _snapshot[12] = Reconnects;
            _snapshot[13] = Extrapolated;
            _snapshot[14] = Frozen;
            _snapshot[15] = TransportMode == null ? 0 : TransportMode.Length;
            _snapshot[16] = Interpolated;
            _snapshot[17] = Quant(ServiceLocalMs, 10.0);
            _snapshot[18] = Quant(ServicePeerMs, 10.0);

            if (!_havePrevious)
            {
                for (int i = 0; i < _snapshot.Length; i++) _previous[i] = _snapshot[i];
                _havePrevious = true;
                return true;
            }

            bool changed = false;
            for (int i = 0; i < _snapshot.Length; i++)
            {
                if (_snapshot[i] == _previous[i]) continue;
                _previous[i] = _snapshot[i];
                changed = true;
            }
            return changed;
        }

        public int AppliedFrames { get { return Interpolated + Extrapolated + Frozen; } }

        public double InterpolatedPercent
        {
            get
            {
                int total = AppliedFrames;
                return total > 0 ? Interpolated * 100.0 / total : 0.0;
            }
        }

        public void Format(StringBuilder sb)
        {
            if (sb == null) return;
            sb.Length = 0;

            sb.Append("VPB net  ");
            sb.Append(StateName(State));
            if (PeerCount > 0)
            {
                sb.Append("  peers ");
                AppendInt(sb, PeerCount);
            }
            if (!string.IsNullOrEmpty(PeerName))
            {
                sb.Append("  ");
                sb.Append(PeerName);
            }
            sb.Append('\n');

            sb.Append("link  ");
            sb.Append(TransportMode);
            sb.Append("  rtt ");
            AppendFixed(sb, RttMs, 1);
            sb.Append("ms  jitter ");
            AppendFixed(sb, JitterMs, 1);
            sb.Append("ms  loss ");
            AppendFixed(sb, LossPercent, 2);
            sb.Append('%');
            sb.Append('\n');

            sb.Append("buffer  delay ");
            AppendFixed(sb, DelayMs, 0);
            sb.Append("ms (transit ");
            AppendFixed(sb, TransitMs, 0);
            sb.Append(" + jitter ");
            AppendFixed(sb, BufferMs, 0);
            sb.Append(")  depth ");
            AppendInt(sb, BufferDepth);
            sb.Append("  age ");
            AppendFixed(sb, FrameAgeMs, 0);
            sb.Append("ms");
            sb.Append('\n');

            sb.Append("service  yours ");
            AppendFixed(sb, ServiceLocalMs, 1);
            sb.Append("ms  peer ");
            AppendFixed(sb, ServicePeerMs, 1);
            sb.Append("ms");
            sb.Append('\n');

            sb.Append("cost  sampler ");
            AppendFixed(sb, SamplerUs, 1);
            sb.Append("us  applier ");
            AppendFixed(sb, ApplierUs, 1);
            sb.Append("us");
            sb.Append('\n');

            sb.Append("health  stalls ");
            AppendInt(sb, Stalls);
            sb.Append("  rejoins ");
            AppendInt(sb, Reconnects);
            sb.Append("  interp ");
            AppendFixed(sb, InterpolatedPercent, 1);
            sb.Append("% of ");
            AppendInt(sb, AppliedFrames);
            sb.Append("  extrap ");
            AppendInt(sb, Extrapolated);
            sb.Append("  frozen ");
            AppendInt(sb, Frozen);

            if (Reason != VpbNetDropReason.None)
            {
                sb.Append('\n');
                sb.Append(ReasonName(Reason));
            }
        }

        public static string StateName(VpbNetSessionState s)
        {
            switch (s)
            {
                case VpbNetSessionState.Idle: return "idle";
                case VpbNetSessionState.Connecting: return "connecting";
                case VpbNetSessionState.Syncing: return "syncing";
                case VpbNetSessionState.Running: return "running";
                case VpbNetSessionState.Stalled: return "STALLED";
                case VpbNetSessionState.Reconnecting: return "REJOINING";
                case VpbNetSessionState.Dropped: return "DROPPED";
            }
            return "?";
        }

        public static string ReasonName(VpbNetDropReason r)
        {
            switch (r)
            {
                case VpbNetDropReason.None: return string.Empty;
                case VpbNetDropReason.LocalLeave: return "you left";
                case VpbNetDropReason.PeerLeave: return "peer left";
                case VpbNetDropReason.ConnectTimeout: return "no answer from host";
                case VpbNetDropReason.DataTimeout: return "peer stopped sending";
                case VpbNetDropReason.TransportError: return "connection failed";
                case VpbNetDropReason.VersionMismatch: return "version mismatch";
                case VpbNetDropReason.AuthFailed: return "room code rejected";
                case VpbNetDropReason.ContentMismatch: return "missing packages";
                case VpbNetDropReason.ReconnectExhausted: return "could not rejoin";
                case VpbNetDropReason.Kicked: return "removed by host";
            }
            return "?";
        }

        static int Quant(double v, double scale)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return int.MinValue;
            double t = v * scale;
            if (t > 2147483000.0) t = 2147483000.0;
            else if (t < -2147483000.0) t = -2147483000.0;
            return (int)(t >= 0.0 ? t + 0.5 : t - 0.5);
        }

        public void AppendInt(StringBuilder sb, long v)
        {
            if (v < 0)
            {
                sb.Append('-');
                v = -v;
            }
            int n = 0;
            do
            {
                _digits[n] = (char)('0' + (int)(v % 10));
                n++;
                v /= 10;
            }
            while (v > 0 && n < _digits.Length);

            for (int i = n - 1; i >= 0; i--) sb.Append(_digits[i]);
        }

        public void AppendFixed(StringBuilder sb, double v, int decimals)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                sb.Append("--");
                return;
            }
            if (decimals < 0) decimals = 0;
            else if (decimals > 6) decimals = 6;

            bool neg = v < 0.0;
            if (neg) v = -v;

            long scale = 1;
            for (int i = 0; i < decimals; i++) scale *= 10;

            double t = v * scale + 0.5;
            if (t > 9.0e15) t = 9.0e15;
            long scaled = (long)t;

            long whole = scaled / scale;
            long frac = scaled - whole * scale;

            if (neg && (whole != 0 || frac != 0)) sb.Append('-');
            AppendInt(sb, whole);

            if (decimals == 0) return;
            sb.Append('.');
            long div = scale / 10;
            while (div > 0)
            {
                sb.Append((char)('0' + (int)((frac / div) % 10)));
                div /= 10;
            }
        }
    }
}
