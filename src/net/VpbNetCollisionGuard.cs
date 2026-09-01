using System;
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
        static bool _fullWarned;
        static bool _holdWholeScene;
        static string _seatA = string.Empty;
        static string _seatB = string.Empty;

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

        // Only the two seated avatars are session-driven; the rest of the scene keeps its own physics.
        public static void SetSeats(string seatA, string seatB)
        {
            _seatA = seatA == null ? string.Empty : seatA;
            _seatB = seatB == null ? string.Empty : seatB;
        }

        static bool IsSeated(Atom a)
        {
            if (a == null) return false;
            if (_seatA.Length == 0 && _seatB.Length == 0) return false;

            string uid;
            try { uid = a.uid; }
            catch { return false; }
            if (string.IsNullOrEmpty(uid)) return false;

            return string.Equals(uid, _seatA, StringComparison.Ordinal)
                || string.Equals(uid, _seatB, StringComparison.Ordinal);
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

        public static void SetLoadHold(bool on)
        {
            SetLoadHold(on, true);
        }

        // Hold open for the whole load — a 90-frame countdown expires before new Persons land.
        // wholeScene: a scene load moves every body, so hold them all. A preset apply moves only
        // the avatar it lands on, and switching physics off under four bystanders is not ours to do.
        public static void SetLoadHold(bool on, bool wholeScene)
        {
            if (on)
            {
                bool first = !_loadHold;
                _loadHold = true;
                if (wholeScene) _holdWholeScene = true;

                float now = Time.realtimeSinceStartup;
                if (!first && now < _nextSweep) return;
                _nextSweep = now + SweepSeconds;

                int n = HoldAvatars(!_holdWholeScene);
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
            HoldAvatars(!_holdWholeScene);
            _holdWholeScene = false;
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
                int n = HoldAvatars(false);
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
            _holdWholeScene = false;
            _nextSweep = 0f;
            _fullWarned = false;
            _seatA = string.Empty;
            _seatB = string.Empty;
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_atom[i] == null) continue;
                Restore(i, "session ended");
            }
        }

        static int HoldAvatars(bool seatedOnly)
        {
            int n = 0;
            try { VpbNetAvatarRoster.Poll(); }
            catch { }
            for (int i = 0; i < VpbNetAvatarRoster.Count; i++)
            {
                Atom a = VpbNetAvatarRoster.AtomAt(i);
                if (a == null) continue;
                if (seatedOnly && !IsSeated(a)) continue;
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
            if (slot < 0)
            {
                if (!_fullWarned)
                {
                    _fullWarned = true;
                    LogUtil.LogWarning("[VPB.Net] more than " + MaxTracked
                        + " people needed collisions held at once, so the extras kept theirs on."
                        + " If bodies are being thrown around, this is why.");
                }
                return -1;
            }

            _atom[slot] = a;
            _prior[slot] = Read(a);
            _left[slot] = 0;

            // Mid-load collisionEnabled is not the scene's intent, but only for a body we drive.
            _midLoad[slot] = _loadHold && IsSeated(a);

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
