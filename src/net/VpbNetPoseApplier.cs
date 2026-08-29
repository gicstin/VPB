using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public sealed class VpbNetPoseApplier
    {
        public const float HardResetMeters = 0.5f;
        public const int ComplyPauseFrames = 5;
        public const double GapRecoveryMs = 500.0;
        public const int HardResetLogLimit = 32;

        // Past this silence, hold the avatar — do not slam toward a stale target every frame.
        public const double StaleHoldMs = 1000.0;

        // After rebind the travel exceeds the reset threshold; sync bodies instead of resetting.
        public const int BindGraceFrames = 45;

        // A reset that repeats every other frame is not recovering, it is grinding.
        public const double HardResetMinIntervalMs = 250.0;

        // Drop collisions past this one-frame jump — buried colliders shove and the body comes apart.
        public const float CollisionGuardMeters = 0.25f;

        // Cap control speed so a jumped pose travels over frames; stretched joints tear the body.
        public const float MaxControlSpeed = 8f;
        public const float MaxControlDegrees = 1440f;

        // Must not wait forever: peer flags a keyframe only on ITS rebind; we may have bound late.
        public const int PreKeyframePatience = 120;

        readonly VpbNetControllerSet _set = new VpbNetControllerSet();
        readonly Transform[] _ctl = new Transform[VpbNetControllerSet.Names.Length];
        readonly VpbNetSnapshotBuffer _buffer = new VpbNetSnapshotBuffer();
        readonly float[] _decode = new float[VpbPose.PoseFloats];
        readonly float[] _pose = new float[VpbPose.PoseFloats];

        readonly VpbNetFingerRig _fingerRig = new VpbNetFingerRig();
        readonly VpbNetGazeRig _gazeRig = new VpbNetGazeRig();
        readonly VpbNetJawRig _jawRig = new VpbNetJawRig();
        readonly VpbNetFidelityState _fidelity = new VpbNetFidelityState();

        uint _fidelityBound;
        uint _fidelityCapsApplied;
        uint _fidelitySeq;
        uint _fingersDrivenSeq;
        uint _jawDrivenSeq;
        int _fidelityFrames;
        int _fidelityRefused;
        bool _haveFidelitySeq;

        readonly double _ticksToUs = 1000000.0 / Stopwatch.Frequency;

        int _missingMask;
        int _decodeFailures;
        int _hardResets;
        int _hardResetsAfterGap;
        int _applied;
        int _gapFrames;
        double _gapRecoveryMs;
        double _lastRenderMs;
        bool _haveLastRender;
        uint _lastSeq;
        bool _bound;
        bool _awaitingKeyframe;
        int _graceFrames;
        int _preKeyframeDropped;
        int _heldStale;
        int _collisionHolds;
        int _governedFrames;
        float _governedWorst;
        double _lastHardResetMs;
        bool _slotLocked;
        bool _lockRoot;
        Vector3 _homePos;
        Vector3 _slotOffset;
        double _lastApplyUs;
        VpbNetSampleState _state = VpbNetSampleState.Empty;
        VpbNetSampleState _prevState = VpbNetSampleState.Empty;

        public bool MeasureTiming;
        public bool LockRoot;
        public uint FidelityCaps;

        public uint FidelityBound { get { return _fidelityBound; } }
        public int FidelityFrames { get { return _fidelityFrames; } }
        public int FidelityRefused { get { return _fidelityRefused; } }

        public bool IsBound { get { return _bound; } }
        public Atom Atom { get { return _set.Atom; } }
        public VpbNetSnapshotBuffer Buffer { get { return _buffer; } }
        public float[] Pose { get { return _pose; } }
        public int MissingMask { get { return _missingMask; } }
        public int DecodeFailures { get { return _decodeFailures; } }
        public int HardResets { get { return _hardResets; } }
        public int HardResetsAfterGap { get { return _hardResetsAfterGap; } }
        public int Applied { get { return _applied; } }
        public uint LastSeq { get { return _lastSeq; } }
        public double LastApplyUs { get { return _lastApplyUs; } }
        public VpbNetSampleState State { get { return _state; } }
        public bool AwaitingKeyframe { get { return _awaitingKeyframe; } }
        public int PreKeyframeDropped { get { return _preKeyframeDropped; } }
        public int HeldStale { get { return _heldStale; } }
        public int CollisionHolds { get { return _collisionHolds; } }
        public int GovernedFrames { get { return _governedFrames; } }
        public float GovernedWorstMeters { get { return _governedWorst; } }

        public bool Bind(Atom atom)
        {
            Unbind();
            if (atom == null) return false;
            if (!_set.Resolve(atom)) return false;

            Transform root = _set.Controls[VpbNetControllerSet.RootIndex];
            if (root == null)
            {
                _set.Clear();
                return false;
            }

            _missingMask = 0;
            for (int i = 0; i < _ctl.Length; i++)
            {
                Transform t = _set.Controls[i];
                if (t == null)
                {
                    _ctl[i] = null;
                    _missingMask |= 1 << i;
                }
                else
                {
                    _ctl[i] = t;
                }
            }

            _set.SaveStates();
            _set.ApplyStates(FreeControllerV3.PositionState.On, FreeControllerV3.RotationState.On);
            try { _set.PauseComply(10); } catch { }
            VpbNetBodyGuard.Register(atom);

            _homePos = root.position;
            _slotOffset = Vector3.zero;
            _slotLocked = false;
            _lockRoot = LockRoot;

            _buffer.Clear();
            _buffer.ResetCounters();
            _decodeFailures = 0;
            _hardResets = 0;
            _hardResetsAfterGap = 0;
            _applied = 0;
            _gapFrames = 0;
            _gapRecoveryMs = 0.0;
            _lastRenderMs = 0.0;
            _haveLastRender = false;
            _lastSeq = 0;
            _lastApplyUs = 0.0;
            _state = VpbNetSampleState.Empty;
            _prevState = VpbNetSampleState.Empty;
            // Frames in flight still describe the previous avatar.
            _awaitingKeyframe = true;
            _graceFrames = BindGraceFrames;
            _preKeyframeDropped = 0;
            _heldStale = 0;
            _collisionHolds = 0;
            _governedFrames = 0;
            _governedWorst = 0f;
            _lastHardResetMs = double.NegativeInfinity;
            Guard(VpbNetCollisionGuard.BindFrames, "taking over " + AtomName());
            BindFidelity(atom);
            _bound = true;
            LogUtil.LogWarning("[VPB.Net] applier bind " + atom.uid
                + " lockRoot=" + (_lockRoot ? "on" : "off")
                + " home=" + _homePos.x.ToString("0.00") + "," + _homePos.y.ToString("0.00") + "," + _homePos.z.ToString("0.00"));
            return true;
        }

        // Scene load moved the avatar; buffer is the old room. Wait for next keyframe.
        public void Rebase(string why)
        {
            if (!_bound) return;

            _buffer.Clear();
            _awaitingKeyframe = true;
            _preKeyframeDropped = 0;
            _slotLocked = false;
            _slotOffset = Vector3.zero;
            _heldStale = 0;
            _gapFrames = 0;
            _gapRecoveryMs = 0.0;
            _lastRenderMs = 0.0;
            _haveLastRender = false;
            _lastHardResetMs = double.NegativeInfinity;
            _graceFrames = BindGraceFrames;
            _state = VpbNetSampleState.Empty;
            _prevState = VpbNetSampleState.Empty;

            Transform root = _set.Controls[VpbNetControllerSet.RootIndex];
            if (root != null) _homePos = root.position;

            try { _set.ApplyStates(FreeControllerV3.PositionState.On, FreeControllerV3.RotationState.On); }
            catch { }
            try { _set.PauseComply(ComplyPauseFrames); }
            catch { }

            Guard(VpbNetCollisionGuard.BindFrames, why);
        }

        public void SetFidelityCaps(uint caps)
        {
            if (caps == _fidelityCapsApplied && _bound) return;
            _fidelityCapsApplied = caps;
            FidelityCaps = caps;
            if (!_bound) return;
            UnbindFidelity();
            BindFidelity(_set.Atom);
        }

        void BindFidelity(Atom atom)
        {
            _fidelityCapsApplied = FidelityCaps;
            _fidelityBound = 0;
            _fidelitySeq = 0;
            _fingersDrivenSeq = 0;
            _jawDrivenSeq = 0;
            _fidelityFrames = 0;
            _fidelityRefused = 0;
            _haveFidelitySeq = false;
            _fidelity.Reset();

            if ((FidelityCaps & VpbNetCapability.Fingers) != 0
                && _fingerRig.Resolve(atom) && _fingerRig.TakeControl())
                _fidelityBound |= VpbNetCapability.Fingers;
            else _fingerRig.Clear();

            if ((FidelityCaps & VpbNetCapability.Eyes) != 0
                && _gazeRig.Resolve(atom) && _gazeRig.TakeControl())
                _fidelityBound |= VpbNetCapability.Eyes;
            else _gazeRig.Clear();

            if ((FidelityCaps & VpbNetCapability.Jaw) != 0 && _jawRig.Resolve(atom))
                _fidelityBound |= VpbNetCapability.Jaw;
            else _jawRig.Clear();

            if (FidelityCaps == 0) return;

            StringBuilder sb = new StringBuilder(64);
            VpbNetCapability.Describe(sb, _fidelityBound);
            LogUtil.LogWarning("[VPB.Net] applier fidelity on " + (atom == null ? "?" : atom.uid)
                + ": " + sb.ToString()
                + (_fidelityBound == FidelityCaps
                    ? string.Empty
                    : " (the peer offered more than this avatar can take)"));
        }

        void UnbindFidelity()
        {
            try { _fingerRig.Clear(); } catch { }
            try { _gazeRig.Clear(); } catch { }
            try { _jawRig.Clear(); } catch { }
            _fidelityBound = 0;
            _fidelity.Reset();
            _haveFidelitySeq = false;
        }

        public void Unbind()
        {
            if (_bound)
            {
                // Settle first, restore second.
                try { SettleBodies(); } catch { }
                try { _set.RestoreStates(); } catch { }
                try { _set.PauseComply(30); } catch { }
                try { VpbNetBodyGuard.Release(_set.Atom); } catch { }
            }
            UnbindFidelity();
            _bound = false;
            _awaitingKeyframe = false;
            _graceFrames = 0;
            _heldStale = 0;
            _slotLocked = false;
            _slotOffset = Vector3.zero;
            _gapFrames = 0;
            _gapRecoveryMs = 0.0;
            _haveLastRender = false;
            _prevState = VpbNetSampleState.Empty;
            _set.Clear();
            for (int i = 0; i < _ctl.Length; i++) _ctl[i] = null;
            _buffer.Clear();
        }

        public bool IsAlive()
        {
            return _bound && _set.IsAlive();
        }

        public string DescribeMissing(StringBuilder sb)
        {
            return _set.DescribeMissing(sb);
        }

        public bool PushFrame(byte[] frame, int len)
        {
            if (!_bound || frame == null || len <= 0) return false;

            byte ver, flags;
            ushort peer;
            uint seq, tickMs;
            int frameLen, extOff, extLen;
            if (!VpbPose.TryReadFrame(frame, 0, len, _decode, VpbPose.ControllerCount,
                    out ver, out flags, out peer, out seq, out tickMs,
                    out frameLen, out extOff, out extLen))
            {
                _decodeFailures++;
                return false;
            }

            if (_awaitingKeyframe)
            {
                bool isKeyframe = (flags & VpbPose.FlagKeyframe) != 0;
                if (!isKeyframe)
                {
                    _preKeyframeDropped++;
                    if (_preKeyframeDropped < PreKeyframePatience) return false;
                }

                _awaitingKeyframe = false;
                _buffer.Clear();
                _slotLocked = false;
                LogUtil.LogWarning("[VPB.Net] applier " + AtomName()
                    + (isKeyframe
                        ? ": first frame for this avatar arrived after "
                            + _preKeyframeDropped + " stale frames from the previous one"
                        : ": no keyframe after " + _preKeyframeDropped
                            + " frames, so taking the stream as it is - the peer bound its avatar"
                            + " before this side was ready to drive one"));
            }

            _lastSeq = seq;
            if (extLen > 0 && _fidelityBound != 0) TakeFidelity(frame, extOff, extLen, seq);
            return _buffer.Insert(seq, tickMs, _decode, 0);
        }

        void TakeFidelity(byte[] frame, int extOffset, int extLen, uint seq)
        {
            if (_haveFidelitySeq && !Newer(seq, _fidelitySeq)) return;

            int refusedBefore = _fidelity.RefusedBlocks;
            if (!_fidelity.ReadFrom(frame, extOffset, extLen, seq)) return;

            _fidelityRefused += _fidelity.RefusedBlocks - refusedBefore;
            _fidelitySeq = seq;
            _haveFidelitySeq = true;
            _fidelityFrames++;
        }

        static bool Newer(uint seq, uint last)
        {
            return (int)(seq - last) > 0;
        }

        public VpbNetSampleState Apply(double renderRemoteMs)
        {
            if (!_bound || _awaitingKeyframe) return VpbNetSampleState.Empty;

            // Hold still on silence — do not extrapolate a paused session.
            if (_buffer.Count > 0 && renderRemoteMs - _buffer.NewestMs > StaleHoldMs)
            {
                if (_heldStale == 0)
                    LogUtil.LogWarning("[VPB.Net] applier " + AtomName()
                        + ": no fresh pose for " + StaleHoldMs.ToString("0")
                        + "ms, holding it where it stands until the peer sends again");
                _heldStale++;
                _state = VpbNetSampleState.Frozen;
                return _state;
            }
            if (_heldStale > 0)
            {
                _heldStale = 0;
                _graceFrames = BindGraceFrames;
                _slotLocked = false;
                // Whatever the peer did during the silence lands in one frame.
                Guard(VpbNetCollisionGuard.JumpFrames, "the peer went quiet and came back");
                LogUtil.LogWarning("[VPB.Net] applier " + AtomName() + ": pose resumed");
            }

            long t0 = MeasureTiming ? Stopwatch.GetTimestamp() : 0L;

            _state = _buffer.Sample(renderRemoteMs, _pose);

            double dt = _haveLastRender ? renderRemoteMs - _lastRenderMs : 0.0;
            if (dt < 0.0) dt = 0.0;
            _lastRenderMs = renderRemoteMs;
            _haveLastRender = true;

            int gapBefore = _gapFrames;
            double recoveryBefore = _gapRecoveryMs;
            if (_state == VpbNetSampleState.Interpolated)
            {
                if (_gapFrames > 0)
                {
                    _gapRecoveryMs = GapRecoveryMs;
                }
                else if (_gapRecoveryMs > 0.0)
                {
                    _gapRecoveryMs -= dt;
                    if (_gapRecoveryMs < 0.0) _gapRecoveryMs = 0.0;
                }
                _gapFrames = 0;
            }
            else
            {
                _gapFrames++;
                _gapRecoveryMs = 0.0;
            }

            if (_state == VpbNetSampleState.Empty)
            {
                _prevState = _state;
                if (MeasureTiming) _lastApplyUs = (Stopwatch.GetTimestamp() - t0) * _ticksToUs;
                return _state;
            }

            _lockRoot = LockRoot;
            if (_lockRoot)
            {
                Vector3 poseRoot;
                poseRoot.x = _pose[0];
                poseRoot.y = _pose[1];
                poseRoot.z = _pose[2];
                _slotOffset = _homePos - poseRoot;
                _slotLocked = true;
            }
            else if (!_slotLocked)
            {
                // Peer world coords ARE this scene. Do not pin to bind position — that put poses a metre out.
                _slotOffset = Vector3.zero;
                _slotLocked = true;
            }

            float limitSq = HardResetMeters * HardResetMeters;
            float worstSq = -1f;
            int worstIndex = -1;

            float step = Time.fixedDeltaTime;
            if (step <= 0f) step = 1f / 72f;
            float maxStep = MaxControlSpeed * step;
            float maxStepSq = maxStep * maxStep;
            float maxTurn = MaxControlDegrees * step;
            bool governed = false;

            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                Transform t = _ctl[i];
                if (t == null) continue;

                int p = i * VpbPose.FloatsPerController;
                Vector3 target;
                target.x = _pose[p] + _slotOffset.x;
                target.y = _pose[p + 1] + _slotOffset.y;
                target.z = _pose[p + 2] + _slotOffset.z;
                if (_lockRoot && i == VpbNetControllerSet.RootIndex)
                {
                    target.x = _homePos.x;
                    target.y = _homePos.y;
                    target.z = _homePos.z;
                }

                Quaternion rot;
                rot.x = _pose[p + 3];
                rot.y = _pose[p + 4];
                rot.z = _pose[p + 5];
                rot.w = _pose[p + 6];

                Vector3 cur = t.position;
                float dx = target.x - cur.x;
                float dy = target.y - cur.y;
                float dz = target.z - cur.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > worstSq)
                {
                    worstSq = d2;
                    worstIndex = i;
                }

                if (d2 > maxStepSq)
                {
                    float k = maxStep / Mathf.Sqrt(d2);
                    target.x = cur.x + dx * k;
                    target.y = cur.y + dy * k;
                    target.z = cur.z + dz * k;
                    governed = true;
                }

                t.position = target;
                t.rotation = Quaternion.RotateTowards(t.rotation, rot, maxTurn);
            }

            if (governed)
            {
                _governedFrames++;
                float far = Mathf.Sqrt(worstSq);
                if (far > _governedWorst) _governedWorst = far;
            }

            if (worstSq > CollisionGuardMeters * CollisionGuardMeters)
                Guard(VpbNetCollisionGuard.JumpFrames, "the peer's pose moved "
                    + Mathf.Sqrt(worstSq).ToString("0.00") + "m in one frame at "
                    + (worstIndex >= 0 && worstIndex < VpbNetControllerSet.Names.Length
                        ? VpbNetControllerSet.Names[worstIndex] : "?"));

            if (_graceFrames > 0)
            {
                // Carry bodies with controls; never call this travel a reset.
                _graceFrames--;
                SyncBodies();
            }
            else if (!governed && worstSq > limitSq
                && renderRemoteMs - _lastHardResetMs >= HardResetMinIntervalMs)
            {
                _lastHardResetMs = renderRemoteMs;
                HardReset(worstIndex, worstSq, gapBefore > 0 || recoveryBefore > 0.0, gapBefore, renderRemoteMs);
            }

            if (_fidelityBound != 0) ApplyFidelity();

            _prevState = _state;
            _applied++;
            if (MeasureTiming) _lastApplyUs = (Stopwatch.GetTimestamp() - t0) * _ticksToUs;
            return _state;
        }

        void Guard(int frames, string why)
        {
            Atom a = _set.Atom;
            if (a == null) return;
            _collisionHolds++;
            VpbNetCollisionGuard.Suspend(a, frames, why);
        }

        void SyncBodies()
        {
            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                Transform t = _ctl[i];
                if (t == null) continue;

                Rigidbody rb = _set.Bodies[i];
                if (rb == null) continue;

                try
                {
                    rb.position = t.position;
                    rb.rotation = t.rotation;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                catch { }
            }
        }

        void SettleBodies()
        {
            SyncBodies();
            try { _set.PauseComply(ComplyPauseFrames); }
            catch { }
            LogUtil.LogWarning("[VPB.Net] released " + AtomName()
                + " back to the scene: bodies settled onto their controls, velocities zeroed");
        }

        string AtomName()
        {
            try { return _set.Atom != null ? _set.Atom.uid : "?"; }
            catch { return "?"; }
        }

        void ApplyFidelity()
        {
            if (!_haveFidelitySeq) return;

            if ((_fidelityBound & VpbNetCapability.Fingers) != 0
                && _fidelity.HasFingers && _fidelitySeq != _fingersDrivenSeq)
            {
                _fingersDrivenSeq = _fidelitySeq;
                _fingerRig.Drive(_fidelity.Fingers);
            }

            if ((_fidelityBound & VpbNetCapability.Jaw) != 0
                && _fidelity.HasJaw && _fidelitySeq != _jawDrivenSeq)
            {
                _jawDrivenSeq = _fidelitySeq;
                _jawRig.Apply(_fidelity.Jaw);
            }

            if ((_fidelityBound & VpbNetCapability.Eyes) != 0 && _fidelity.HasGaze)
            {
                Vector3 world;
                world.x = _pose[0] + _slotOffset.x + _fidelity.GazeX;
                world.y = _pose[1] + _slotOffset.y + _fidelity.GazeY;
                world.z = _pose[2] + _slotOffset.z + _fidelity.GazeZ;
                _gazeRig.Apply(_fidelity.GazeMode, world);
            }
        }

        void HardReset(int worstIndex, float worstSq, bool afterGap, int gapFrames, double renderRemoteMs)
        {
            _hardResets++;
            if (afterGap) _hardResetsAfterGap++;

            if (_hardResets <= HardResetLogLimit)
            {
                double ahead = _buffer.Count > 0 ? renderRemoteMs - _buffer.NewestMs : 0.0;
                LogUtil.LogWarning("[VPB.Net] applier hard reset " + _set.Atom.uid
                    + " worst " + (worstIndex >= 0 && worstIndex < VpbNetControllerSet.Names.Length
                        ? VpbNetControllerSet.Names[worstIndex] : "?")
                    + " " + Mathf.Sqrt(worstSq).ToString("0.00") + "m"
                    + " | state " + StateName(_prevState) + "->" + StateName(_state)
                    + " gap " + gapFrames + " frames"
                    + " | cursor " + ahead.ToString("0") + "ms past newest"
                    + " | " + (afterGap ? "post-gap" : "STEADY"));
            }

            SyncBodies();
            try { _set.PauseComply(ComplyPauseFrames); } catch { }
        }

        static string StateName(VpbNetSampleState s)
        {
            if (s == VpbNetSampleState.Interpolated) return "Interpolated";
            if (s == VpbNetSampleState.Extrapolated) return "Extrapolated";
            if (s == VpbNetSampleState.Frozen) return "Frozen";
            return "Empty";
        }
    }
}
