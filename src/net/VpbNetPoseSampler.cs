using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public sealed class VpbNetPoseSampler
    {
        public const int DefaultHz = 45;
        public const int MinHz = 1;
        public const int MaxHz = 200;
        const float BacklogResyncPeriods = 2f;
        const int RingSlots = 32;

        // Local body must survive a pose-preset jump too.
        public const float JumpMeters = 0.25f;

        readonly VpbNetControllerSet _set = new VpbNetControllerSet();
        readonly Transform[] _ctl = new Transform[VpbNetControllerSet.Names.Length];
        readonly float[] _pose = new float[VpbPose.PoseFloats];
        readonly float[] _posePrev = new float[VpbPose.PoseFloats];
        readonly byte[] _scratch = new byte[VpbIpc.MaxDataPayload];
        readonly VpbNetPoseRing _ring = new VpbNetPoseRing(RingSlots, VpbPose.FrameBytes + ExtCapacity);

        const int ExtCapacity = 128;
        const float GazeEpsilonMeters = 0.01f;
        const float JawEpsilon = 0.002f;

        readonly VpbNetFingerRig _fingerRig = new VpbNetFingerRig();
        readonly VpbNetGazeRig _gazeRig = new VpbNetGazeRig();
        readonly VpbNetJawRig _jawRig = new VpbNetJawRig();
        readonly byte[] _ext = new byte[ExtCapacity];
        readonly byte[] _block = new byte[VpbNetFingers.WireBytes];
        readonly float[] _fingersNow = new float[VpbNetFingers.Count];
        readonly float[] _fingersSent = new float[VpbNetFingers.Count];

        uint _fidelityCaps;
        uint _peerCaps = 0xFFFFFFFFu;
        int _frameIndex;
        int _lastFingerFrame = -1;
        int _lastGazeFrame = -1;
        int _lastJawFrame = -1;
        int _fingerBlocks;
        int _gazeBlocks;
        int _jawBlocks;
        byte _gazeSentMode = 255;
        Vector3 _gazeSentPoint;
        float _jawSent;
        bool _fingersEverSent;

        double _elapsedSeconds;
        float _accum;
        float _period = 1f / DefaultHz;
        int _hz = DefaultHz;

        uint _seq;
        ushort _peerId;
        int _missingMask;
        int _lastClampedBones;
        int _clampedFrames;
        int _rateSlips;
        double _stalledSeconds;
        int _encodeFailures;
        int _lastFrameLength;
        bool _bound;
        bool _pendingKeyframe;
        bool _havePrevPose;
        int _jumps;
        float _lastJumpMeters;

        double _ticksToUs = 1000000.0 / Stopwatch.Frequency;
        double _lastEncodeUs;

        public bool MeasureTiming;
        public bool WantFingers;
        public bool WantGaze;
        public bool WantJaw;
        public bool GuardCollisions;

        public int Jumps { get { return _jumps; } }
        public float LastJumpMeters { get { return _lastJumpMeters; } }

        public uint FidelityCaps { get { return _fidelityCaps; } }
        public uint SendingCaps { get { return _fidelityCaps & _peerCaps; } }

        public void SetPeerCaps(uint caps)
        {
            _peerCaps = caps;
        }

        public int FingerBlocksSent { get { return _fingerBlocks; } }
        public int GazeBlocksSent { get { return _gazeBlocks; } }
        public int JawBlocksSent { get { return _jawBlocks; } }
        public string JawMorphUid { get { return _jawRig.MorphUid; } }

        public bool IsBound { get { return _bound; } }
        public Atom Atom { get { return _set.Atom; } }
        public VpbNetPoseRing Outbound { get { return _ring; } }
        public float[] Pose { get { return _pose; } }
        public int Hz { get { return _hz; } }
        public float Period { get { return _period; } }
        public uint Seq { get { return _seq; } }
        public int MissingMask { get { return _missingMask; } }
        public int LastClampedBones { get { return _lastClampedBones; } }
        public int ClampedFrames { get { return _clampedFrames; } }
        public int RateSlips { get { return _rateSlips; } }
        public double StalledSeconds { get { return _stalledSeconds; } }
        public int EncodeFailures { get { return _encodeFailures; } }
        public int LastFrameLength { get { return _lastFrameLength; } }
        public double LastEncodeUs { get { return _lastEncodeUs; } }
        public double ElapsedSeconds { get { return _elapsedSeconds; } }

        public ushort PeerId
        {
            get { return _peerId; }
            set { _peerId = value; }
        }

        public bool Bind(Atom atom, int hz)
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
                    _ctl[i] = root;
                    _missingMask |= 1 << i;
                }
                else
                {
                    _ctl[i] = t;
                }
            }

            SetHz(hz);
            _elapsedSeconds = 0.0;
            _accum = 0f;
            _seq = 0;
            _lastClampedBones = 0;
            _clampedFrames = 0;
            _rateSlips = 0;
            _stalledSeconds = 0.0;
            _encodeFailures = 0;
            _lastFrameLength = 0;
            _lastEncodeUs = 0.0;
            _pendingKeyframe = true;
            _havePrevPose = false;
            _jumps = 0;
            _lastJumpMeters = 0f;
            _ring.Clear();
            _ring.ResetCounters();
            ResolveFidelity(atom);
            if (GuardCollisions) VpbNetBodyGuard.Register(atom);
            _bound = true;
            return true;
        }

        void ResolveFidelity(Atom atom)
        {
            _fidelityCaps = 0;
            _frameIndex = 0;
            _lastFingerFrame = -1;
            _lastGazeFrame = -1;
            _lastJawFrame = -1;
            _fingerBlocks = 0;
            _gazeBlocks = 0;
            _jawBlocks = 0;
            _gazeSentMode = 255;
            _jawSent = 0f;
            _fingersEverSent = false;

            if (WantFingers && _fingerRig.Resolve(atom)) _fidelityCaps |= VpbNetCapability.Fingers;
            else _fingerRig.Clear();

            if (WantGaze && _gazeRig.Resolve(atom)) _fidelityCaps |= VpbNetCapability.Eyes;
            else _gazeRig.Clear();

            if (WantJaw && _jawRig.Resolve(atom)) _fidelityCaps |= VpbNetCapability.Jaw;
            else _jawRig.Clear();

            if (WantFingers && (_fidelityCaps & VpbNetCapability.Fingers) == 0)
                LogUtil.LogWarning("[VPB.Net] finger sync is on but this avatar's hands did not resolve ("
                    + _fingerRig.ResolvedCount + "/" + VpbNetFingers.Count
                    + " controls); fingers are not advertised to the peer");
            if (WantGaze && (_fidelityCaps & VpbNetCapability.Eyes) == 0)
                LogUtil.LogWarning("[VPB.Net] eye sync is on but this avatar has no EyesControl; gaze is not advertised to the peer");
            if (WantJaw && (_fidelityCaps & VpbNetCapability.Jaw) == 0)
                LogUtil.LogWarning("[VPB.Net] jaw sync is on but no mouth-open morph resolved on this avatar;"
                    + " jaw is not advertised to the peer");
        }

        public void Unbind()
        {
            _bound = false;
            try { VpbNetBodyGuard.Release(_set.Atom); } catch { }
            _set.Clear();
            for (int i = 0; i < _ctl.Length; i++) _ctl[i] = null;
            _fingerRig.Clear();
            _gazeRig.Clear();
            _jawRig.Clear();
            _fidelityCaps = 0;
            _ring.Clear();
        }

        public void SetHz(int hz)
        {
            _hz = Mathf.Clamp(hz, MinHz, MaxHz);
            _period = 1f / _hz;
        }

        public bool IsAlive()
        {
            return _bound && _set.IsAlive();
        }

        public void MarkKeyframe()
        {
            _pendingKeyframe = true;
        }

        public string DescribeMissing(StringBuilder sb)
        {
            return _set.DescribeMissing(sb);
        }

        public bool Tick(float dt)
        {
            if (!_bound) return false;

            _elapsedSeconds += dt;
            _accum += dt;
            if (_accum < _period) return false;

            _accum -= _period;
            if (_accum > _period * BacklogResyncPeriods)
            {
                _stalledSeconds += _accum;
                _accum = 0f;
                _rateSlips++;
            }

            return SampleAndEncode((uint)(_elapsedSeconds * 1000.0));
        }

        public bool Tick(float dt, uint tickMs)
        {
            if (!_bound) return false;

            _elapsedSeconds += dt;
            _accum += dt;
            if (_accum < _period) return false;

            _accum -= _period;
            if (_accum > _period * BacklogResyncPeriods)
            {
                _stalledSeconds += _accum;
                _accum = 0f;
                _rateSlips++;
            }

            return SampleAndEncode(tickMs);
        }

        public bool SampleAndEncode()
        {
            return SampleAndEncode((uint)(_elapsedSeconds * 1000.0));
        }

        public bool SampleAndEncode(uint tickMs)
        {
            if (!_bound) return false;

            long t0 = MeasureTiming ? Stopwatch.GetTimestamp() : 0L;

            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                Transform t = _ctl[i];
                Vector3 p = t.position;
                Quaternion q = t.rotation;
                int o = i * VpbPose.FloatsPerController;
                _pose[o] = p.x;
                _pose[o + 1] = p.y;
                _pose[o + 2] = p.z;
                _pose[o + 3] = q.x;
                _pose[o + 4] = q.y;
                _pose[o + 5] = q.z;
                _pose[o + 6] = q.w;
            }

            if (GuardCollisions) NoteJump();

            byte flags = 0;
            if (_pendingKeyframe) flags |= VpbPose.FlagKeyframe;

            int extLen = SendingCaps == 0 ? 0 : BuildFidelityExt();

            int clampedBones;
            int len = VpbPose.WriteFrame(_scratch, 0, flags, _peerId, _seq, tickMs,
                _pose, VpbPose.ControllerCount, _ext, 0, extLen, out clampedBones);

            if (MeasureTiming) _lastEncodeUs = (Stopwatch.GetTimestamp() - t0) * _ticksToUs;

            if (len < 0)
            {
                _encodeFailures++;
                return false;
            }

            _lastClampedBones = clampedBones;
            if (clampedBones != 0) _clampedFrames++;
            _lastFrameLength = len;
            _pendingKeyframe = false;
            _seq++;
            _frameIndex++;

            _ring.Enqueue(_scratch, len);
            return true;
        }

        // No alloc: one pass over the pose just read, then copy into the previous-pose buffer.
        void NoteJump()
        {
            if (!_havePrevPose)
            {
                Array.Copy(_pose, _posePrev, VpbPose.PoseFloats);
                _havePrevPose = true;
                return;
            }

            float worstSq = 0f;
            for (int i = 0; i < VpbPose.ControllerCount; i++)
            {
                int o = i * VpbPose.FloatsPerController;
                float dx = _pose[o] - _posePrev[o];
                float dy = _pose[o + 1] - _posePrev[o + 1];
                float dz = _pose[o + 2] - _posePrev[o + 2];
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 > worstSq) worstSq = d2;
            }
            Array.Copy(_pose, _posePrev, VpbPose.PoseFloats);

            if (worstSq <= JumpMeters * JumpMeters) return;

            _jumps++;
            _lastJumpMeters = Mathf.Sqrt(worstSq);
            VpbNetCollisionGuard.Suspend(_set.Atom, VpbNetCollisionGuard.JumpFrames,
                "your own pose moved " + _lastJumpMeters.ToString("0.00") + "m in one step"
                + " (a pose preset, a grab, or a teleport)");
        }

        int BuildFidelityExt()
        {
            int at = 0;
            bool refresh = _pendingKeyframe;
            uint caps = SendingCaps;

            if ((caps & VpbNetCapability.Fingers) != 0 && _fingerRig.IsAlive()
                && Due(_lastFingerFrame, VpbNetFidelityRate.FingerEveryNth))
            {
                if (_fingerRig.Read(_fingersNow))
                {
                    bool changed = !_fingersEverSent
                        || VpbNetFingers.Differs(_fingersNow, _fingersSent, VpbNetFingers.QuantStepDegrees);
                    if (changed || refresh || Stale(_lastFingerFrame))
                    {
                        if (VpbNetFingers.Write(_block, 0, _fingersNow) == VpbNetFingers.WireBytes)
                        {
                            int n = VpbPose.AppendExt(_ext, at, _ext.Length, VpbNetPoseExtId.Fingers,
                                _block, 0, VpbNetFingers.WireBytes);
                            if (n > 0)
                            {
                                at += n;
                                Array.Copy(_fingersNow, _fingersSent, VpbNetFingers.Count);
                                _fingersEverSent = true;
                                _lastFingerFrame = _frameIndex;
                                _fingerBlocks++;
                            }
                        }
                    }
                }
            }

            if ((caps & VpbNetCapability.Eyes) != 0 && _gazeRig.IsAlive()
                && Due(_lastGazeFrame, VpbNetFidelityRate.GazeEveryNth))
            {
                Vector3 world;
                byte mode = _gazeRig.Read(out world);
                Vector3 local;
                local.x = world.x - _pose[0];
                local.y = world.y - _pose[1];
                local.z = world.z - _pose[2];

                bool changed = mode != _gazeSentMode
                    || (mode == VpbNetGaze.ModePoint
                        && (local - _gazeSentPoint).sqrMagnitude > GazeEpsilonMeters * GazeEpsilonMeters);

                if (changed || refresh || Stale(_lastGazeFrame))
                {
                    int n = mode == VpbNetGaze.ModePoint
                        ? VpbNetGaze.WritePoint(_block, 0, local.x, local.y, local.z)
                        : (mode == VpbNetGaze.ModeViewer
                            ? VpbNetGaze.WriteViewer(_block, 0)
                            : VpbNetGaze.WriteNone(_block, 0));
                    if (n > 0)
                    {
                        int w = VpbPose.AppendExt(_ext, at, _ext.Length, VpbNetPoseExtId.Gaze, _block, 0, n);
                        if (w > 0)
                        {
                            at += w;
                            _gazeSentMode = mode;
                            _gazeSentPoint = local;
                            _lastGazeFrame = _frameIndex;
                            _gazeBlocks++;
                        }
                    }
                }
            }

            if ((caps & VpbNetCapability.Jaw) != 0 && _jawRig.IsAlive()
                && Due(_lastJawFrame, VpbNetFidelityRate.JawEveryNth))
            {
                float v = _jawRig.Read();
                float d = v - _jawSent;
                if (d < 0f) d = -d;
                if (d > JawEpsilon || refresh || Stale(_lastJawFrame))
                {
                    if (VpbNetJaw.Write(_block, 0, v) == VpbNetJaw.WireBytes)
                    {
                        int n = VpbPose.AppendExt(_ext, at, _ext.Length, VpbNetPoseExtId.Jaw,
                            _block, 0, VpbNetJaw.WireBytes);
                        if (n > 0)
                        {
                            at += n;
                            _jawSent = v;
                            _lastJawFrame = _frameIndex;
                            _jawBlocks++;
                        }
                    }
                }
            }

            return at;
        }

        bool Due(int lastFrame, int everyNth)
        {
            if (everyNth <= 1) return true;
            return lastFrame < 0 || _frameIndex - lastFrame >= everyNth;
        }

        bool Stale(int lastFrame)
        {
            return lastFrame < 0 || _frameIndex - lastFrame >= VpbNetFidelityRate.RefreshFrames;
        }
    }
}
