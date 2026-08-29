using UnityEngine;

namespace VPB
{
    public static class VpbNetBodyGuard
    {
        public const int RepairCollisionFrames = 90;
        public const float DepenetrationVelocity = 2f;
        public const float BlowupSpeed = 18f;
        public const float StrayFactor = 2.5f;
        public const float StrayFloorMeters = 1.2f;
        public const float StrayPadMeters = 1f;
        public const int ScanPerStep = 24;
        public const float RepairIntervalSeconds = 2f;
        public const float LogIntervalSeconds = 1f;

        const int MaxTracked = VpbNetAvatarRoster.MaxAvatars;

        sealed class Tracked
        {
            public Atom Atom;
            public Rigidbody[] Bodies;
            public float[] PriorDepen;
            public Transform Root;
            public float StraySq;
            public int Cursor;
            public float LastRepairAt;
        }

        static readonly Tracked[] _slots = NewSlots();

        static bool _enabled = true;
        static int _registered;
        static int _repairs;
        static float _nextLog;

        public static bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; }
        }

        public static int Registered { get { return _registered; } }
        public static int Repairs { get { return _repairs; } }

        public static void ResetCounters()
        {
            _repairs = 0;
        }

        public static bool Register(Atom a)
        {
            if (a == null) return false;
            if (SlotOf(a) >= 0) return true;

            int slot = -1;
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_slots[i].Atom != null) continue;
                slot = i;
                break;
            }
            if (slot < 0) return false;

            Tracked t = _slots[slot];
            t.Atom = a;
            t.Bodies = ReadBodies(a);
            t.Root = RootOf(a);
            t.Cursor = 0;
            t.LastRepairAt = float.NegativeInfinity;
            MeasureReach(t);
            ClampDepenetration(t);
            _registered++;
            return true;
        }

        public static void Release(Atom a)
        {
            int slot = SlotOf(a);
            if (slot < 0) return;
            RestoreDepenetration(_slots[slot]);
            Forget(_slots[slot]);
        }

        public static void ReleaseAll()
        {
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_slots[i].Atom == null) continue;
                RestoreDepenetration(_slots[i]);
                Forget(_slots[i]);
            }
            _registered = 0;
        }

        public static void RepairNow(Atom a, string why)
        {
            int slot = SlotOf(a);
            if (slot < 0)
            {
                if (!Register(a)) return;
                slot = SlotOf(a);
                if (slot < 0) return;
            }
            Repair(_slots[slot], why, true);
        }

        public static void Tick()
        {
            if (!_enabled) return;

            for (int i = 0; i < MaxTracked; i++)
            {
                Tracked t = _slots[i];
                if (t.Atom == null) continue;
                if (!Alive(t)) { Forget(t); continue; }
                Scan(t);
            }
        }

        static void Scan(Tracked t)
        {
            Rigidbody[] bodies = t.Bodies;
            if (bodies == null || bodies.Length == 0) return;
            if (t.Root == null) return;

            Vector3 origin = t.Root.position;
            int n = bodies.Length;
            int budget = ScanPerStep < n ? ScanPerStep : n;

            for (int k = 0; k < budget; k++)
            {
                int i = t.Cursor;
                t.Cursor = i + 1 < n ? i + 1 : 0;

                Rigidbody rb = bodies[i];
                if (rb == null || rb.isKinematic) continue;

                Vector3 v = rb.velocity;
                float vs = v.x * v.x + v.y * v.y + v.z * v.z;
                if (Bad(vs))
                {
                    Repair(t, "a body's velocity went to NaN", false);
                    return;
                }
                if (vs > BlowupSpeed * BlowupSpeed)
                {
                    Repair(t, "a body reached " + Mathf.Sqrt(vs).ToString("0") + " m/s", false);
                    return;
                }

                Vector3 p = rb.position;
                float dx = p.x - origin.x;
                float dy = p.y - origin.y;
                float dz = p.z - origin.z;
                float ds = dx * dx + dy * dy + dz * dz;
                if (Bad(ds))
                {
                    Repair(t, "a body's position went to NaN", false);
                    return;
                }
                if (ds > t.StraySq)
                {
                    Repair(t, "a body drifted " + Mathf.Sqrt(ds).ToString("0.0") + "m from the root", false);
                    return;
                }
            }
        }

        static void Repair(Tracked t, string why, bool manual)
        {
            float now = Time.realtimeSinceStartup;
            if (!manual && now - t.LastRepairAt < RepairIntervalSeconds) return;
            t.LastRepairAt = now;
            _repairs++;

            VpbNetCollisionGuard.Suspend(t.Atom, RepairCollisionFrames, why);
            ZeroVelocities(t);
            try { t.Atom.ResetPhysics(true, false); }
            catch { }

            if (!manual && now < _nextLog) return;
            _nextLog = now + LogIntervalSeconds;
            LogUtil.LogWarning("[VPB.Net] caught " + Name(t.Atom) + " coming apart (" + why
                + "): velocities zeroed, collisions off for " + RepairCollisionFrames
                + " physics frames, and VaM's own physics reset run on it."
                + " This is the last line of defence and it costs a hitch - if it fires often,"
                + " something upstream is teleporting the body.");
        }

        static void ZeroVelocities(Tracked t)
        {
            Rigidbody[] bodies = t.Bodies;
            if (bodies == null) return;
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody rb = bodies[i];
                if (rb == null || rb.isKinematic) continue;
                try
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                catch { }
            }
        }

        static void MeasureReach(Tracked t)
        {
            float reach = StrayFloorMeters;
            Rigidbody[] bodies = t.Bodies;
            if (bodies != null && t.Root != null)
            {
                Vector3 origin = t.Root.position;
                for (int i = 0; i < bodies.Length; i++)
                {
                    Rigidbody rb = bodies[i];
                    if (rb == null) continue;
                    Vector3 p = rb.position;
                    float dx = p.x - origin.x;
                    float dy = p.y - origin.y;
                    float dz = p.z - origin.z;
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (Bad(d2)) continue;
                    float d = Mathf.Sqrt(d2);
                    if (d > reach) reach = d;
                }
            }
            float limit = reach * StrayFactor + StrayPadMeters;
            t.StraySq = limit * limit;
        }

        static void ClampDepenetration(Tracked t)
        {
            Rigidbody[] bodies = t.Bodies;
            if (bodies == null) { t.PriorDepen = null; return; }

            if (t.PriorDepen == null || t.PriorDepen.Length != bodies.Length)
                t.PriorDepen = new float[bodies.Length];

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody rb = bodies[i];
                if (rb == null) { t.PriorDepen[i] = -1f; continue; }
                try
                {
                    t.PriorDepen[i] = rb.maxDepenetrationVelocity;
                    if (rb.maxDepenetrationVelocity > DepenetrationVelocity)
                        rb.maxDepenetrationVelocity = DepenetrationVelocity;
                }
                catch { t.PriorDepen[i] = -1f; }
            }
        }

        static void RestoreDepenetration(Tracked t)
        {
            Rigidbody[] bodies = t.Bodies;
            float[] prior = t.PriorDepen;
            if (bodies == null || prior == null) return;

            int n = bodies.Length < prior.Length ? bodies.Length : prior.Length;
            for (int i = 0; i < n; i++)
            {
                Rigidbody rb = bodies[i];
                if (rb == null || prior[i] < 0f) continue;
                try { rb.maxDepenetrationVelocity = prior[i]; }
                catch { }
            }
        }

        static void Forget(Tracked t)
        {
            if (t.Atom != null && _registered > 0) _registered--;
            t.Atom = null;
            t.Bodies = null;
            t.PriorDepen = null;
            t.Root = null;
            t.Cursor = 0;
        }

        static bool Alive(Tracked t)
        {
            try { return t.Atom != null && t.Root != null; }
            catch { return false; }
        }

        static int SlotOf(Atom a)
        {
            if (a == null) return -1;
            for (int i = 0; i < MaxTracked; i++)
            {
                if (_slots[i].Atom == a) return i;
            }
            return -1;
        }

        static Rigidbody[] ReadBodies(Atom a)
        {
            try { return a.rigidbodies; }
            catch { return null; }
        }

        static Transform RootOf(Atom a)
        {
            try
            {
                FreeControllerV3 fc = a.mainController;
                if (fc != null)
                {
                    if (fc.control != null) return fc.control;
                    return fc.transform;
                }
                return a.transform;
            }
            catch { return null; }
        }

        static bool Bad(float f)
        {
            return float.IsNaN(f) || float.IsInfinity(f);
        }

        static string Name(Atom a)
        {
            try { return a.uid; }
            catch { return "?"; }
        }

        static Tracked[] NewSlots()
        {
            Tracked[] slots = new Tracked[MaxTracked];
            for (int i = 0; i < MaxTracked; i++) slots[i] = new Tracked();
            return slots;
        }
    }
}
