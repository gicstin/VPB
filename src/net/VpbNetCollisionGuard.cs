using UnityEngine;

namespace VPB
{
    // Drop collisions on a teleport jump; session-panel switch holds the same mechanism open.
    public static class VpbNetCollisionGuard
    {
        // Physics frames, not render frames: Tick() runs from FixedUpdate.
        public const int JumpFrames = 90;
        public const int BindFrames = 150;
        public const int SettleFrames = 180;
        public const float LogIntervalSeconds = 1f;

        const int MaxTracked = VpbNetAvatarRoster.MaxAvatars;
        const float SweepSeconds = 0.25f;

        static readonly Atom[] _atom = new Atom[MaxTracked];
        static readonly bool[] _prior = new bool[MaxTracked];
        static readonly bool[] _midLoad = new bool[MaxTracked];
        static readonly int[] _left = new int[MaxTracked];

        static bool _forcedOff;
        static bool _loadHold;
        static bool _enabled = true;
        static int _suspends;
        static int _restores;
        static float _nextLog;
        static float _nextSweep;

        public static bool ForcedOff { get { return _forcedOff; } }
        public static bool LoadHeld { get { return _loadHold; } }
        public static int Suspends { get { return _suspends; } }
        public static int Restores { get { return _restores; } }

        // Count live slots, do not tally — scene load destroys atoms without Restore.
        public static int Held
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxTracked; i++)
                {
                    if (_atom[i] != null) n++;
                }
                return n;
            }
        }

        public static bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        public static void Suspend(Atom a, int frames, string why)
        {
            if (a == null || frames <= 0) return;
            if (!_enabled && !_forcedOff) return;

            int slot = SlotOf(a);
            if (slot < 0)
            {
                slot = Acquire(a);
                if (slot < 0) return;
                _suspends++;
                Say(a, why, frames);
            }
            if (frames > _left[slot]) _left[slot] = frames;
        }

        // Hold open for the whole load — a 90-frame countdown expires before new Persons land.
        public static void SetLoadHold(bool on)
        {
            if (on)
            {
                bool first = !_loadHold;
                _loadHold = true;

                float now = Time.realtimeSinceStartup;
                if (!first && now < _nextSweep) return;
                _nextSweep = now + SweepSeconds;

                int n = HoldEveryAvatar();
                if (!first) return;
                LogUtil.LogWarning("[VPB.Net] collisions off on " + n
                    + " person(s) while content loads - they come back "
                    + SettleFrames + " physics frames after the load lets go");
                return;
            }

            if (!_loadHold) return;
            _loadHold = false;
            _nextSweep = 0f;

            // Sweep again before settle — freshly loaded bodies never went through Acquire.
            HoldEveryAvatar();
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_atom[i] == null) continue;
                if (_left[i] < SettleFrames) _left[i] = SettleFrames;
            }
        }

        public static void Tick()
        {
            if (_forcedOff || _loadHold) return;

            for (int i = 0; i < MaxTracked; i++)
            {
                if (_atom[i] == null) continue;
                if (_left[i] > 0)
                {
                    _left[i]--;
                    if (_left[i] > 0) continue;
                }
                Restore(i, "settled");
            }
        }

        public static void SetForcedOff(bool off)
        {
            if (off == _forcedOff) return;
            _forcedOff = off;

            if (off)
            {
                int n = HoldEveryAvatar();
                LogUtil.LogWarning("[VPB.Net] collisions are OFF on " + n
                    + " person(s) and stay off until you press it again."
                    + " Use this while you throw poses at each other, then put it back on.");
                return;
            }

            int back = 0;
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_atom[i] == null) continue;
                if (_loadHold || _left[i] > 0) continue;
                Restore(i, "switched back on");
                back++;
            }
            LogUtil.LogWarning("[VPB.Net] collisions are back on for " + back + " person(s)");
        }

        public static void ReleaseAll()
        {
            _forcedOff = false;
            _loadHold = false;
            _nextSweep = 0f;
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_atom[i] == null) continue;
                Restore(i, "session ended");
            }
        }

        static int HoldEveryAvatar()
        {
            int n = 0;
            try { VpbNetAvatarRoster.Poll(); }
            catch { }
            for (int i = 0; i < VpbNetAvatarRoster.Count; i++)
            {
                Atom a = VpbNetAvatarRoster.AtomAt(i);
                if (a == null) continue;
                if (SlotOf(a) >= 0) { n++; continue; }
                if (Acquire(a) < 0) continue;
                _suspends++;
                n++;
            }
            return n;
        }

        static int Acquire(Atom a)
        {
            int slot = -1;
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_atom[i] != null) continue;
                slot = i;
                break;
            }
            if (slot < 0) return -1;

            _atom[slot] = a;
            _prior[slot] = Read(a);
            _left[slot] = 0;

            // Mid-load collisionEnabled is not the scene's intent.
            _midLoad[slot] = _loadHold;

            Write(a, false);
            return slot;
        }

        static void Restore(int slot, string why)
        {
            Atom a = _atom[slot];
            bool back = _midLoad[slot] || _prior[slot];
            _atom[slot] = null;
            _midLoad[slot] = false;
            _left[slot] = 0;
            if (a == null) return;

            Write(a, back);
            _restores++;
            if (!back) return;
            LogUtil.LogWarning("[VPB.Net] collisions back on for " + Name(a) + " (" + why + ")");
        }

        static int SlotOf(Atom a)
        {
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_atom[i] == a) return i;
            }
            return -1;
        }

        static void Say(Atom a, string why, int frames)
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextLog) return;
            _nextLog = now + LogIntervalSeconds;
            LogUtil.LogWarning("[VPB.Net] collisions off for " + Name(a) + " for "
                + frames + " physics frames: " + why
                + " - this is what stops a jumped pose from blowing the body apart");
        }

        static string Name(Atom a)
        {
            try { return a.uid; }
            catch { return "?"; }
        }

        static bool Read(Atom a)
        {
            try { return a.collisionEnabled; }
            catch { return true; }
        }

        static void Write(Atom a, bool v)
        {
            try { a.collisionEnabled = v; }
            catch { }
        }
    }
}
