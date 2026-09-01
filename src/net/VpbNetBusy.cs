using UnityEngine;
using VpbNet;

namespace VPB
{
    public delegate void VpbNetBusySend(bool begin, int seconds, byte kind);

    // Announce BEFORE a blocking VaM load. Pair with CreditLocalStall.
    public static class VpbNetBusy
    {
        public const int DefaultSeconds = 25;
        public const int MaxSeconds = 180;

        // Only End() closes early. Closing a real load early is worse than a late leak.
        const float OverrunGraceSeconds = 20f;

        static VpbNetBusySend _sender;
        static int _depth;
        static byte _kind;
        static int _seconds;
        static float _startedAt;
        static int _announced;
        static int _forcedEnds;

        public static bool Active { get { return _depth > 0; } }
        public static string Reason { get { return VpbNetBusyKind.Describe(_kind); } }
        public static int Announced { get { return _announced; } }
        public static int ForcedEnds { get { return _forcedEnds; } }

        // Depth still balances if a load straddles session end. Installing a sender resets debris.
        public static void SetSender(VpbNetBusySend sender)
        {
            _sender = sender;
            if (sender != null) _depth = 0;
        }

        public static void ResetCounters()
        {
            _announced = 0;
            _forcedEnds = 0;
        }

        public static void Begin(byte kind)
        {
            Begin(kind, DefaultSeconds);
        }

        // Nested loads announce once (outermost kind/budget) — do not spend datagrams per inner step.
        public static void Begin(byte kind, int seconds)
        {
            _depth++;
            if (_depth > 1) return;

            if (seconds < 1) seconds = 1;
            if (seconds > MaxSeconds) seconds = MaxSeconds;

            _kind = kind;
            _seconds = seconds;
            _startedAt = Now();
            Announce(true, seconds, kind);
        }

        public static void End()
        {
            if (_depth <= 0) return;
            _depth--;
            if (_depth > 0) return;

            Announce(false, 0, _kind);
            _kind = VpbNetBusyKind.Content;
            _seconds = 0;
        }

        public static void Poll()
        {
            if (_depth <= 0) return;
            if (Now() - _startedAt < _seconds + OverrunGraceSeconds) return;

            _forcedEnds++;
            _depth = 0;
            LogUtil.LogWarning("[VPB.Net] a content load said it would take about " + _seconds
                + "s and never reported finishing, so the other side is being told you are back."
                + " If their view of you was wrong for a while, this is why.");
            Announce(false, 0, _kind);
            _kind = VpbNetBusyKind.Content;
            _seconds = 0;
        }

        static void Announce(bool begin, int seconds, byte kind)
        {
            VpbNetBusySend sender = _sender;
            if (sender == null) return;
            try
            {
                sender(begin, seconds, kind);
                if (begin) _announced++;
            }
            catch { }
        }

        static float Now()
        {
            try { return Time.realtimeSinceStartup; }
            catch { return 0f; }
        }
    }
}
