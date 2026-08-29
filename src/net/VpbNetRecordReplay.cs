using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VPB
{
    public static class VpbNetRecordReplay
    {
        const string HostName = "VPBNetClip";

        static GameObject _host;
        static VpbNetRecordReplayRunner _runner;
        static float _nextPoll;

        public static bool IsRunning { get { return _runner != null; } }

        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextPoll) return;
            _nextPoll = now + 1f;

            bool record = false;
            bool replay = false;
            try
            {
                Settings s = Settings.Instance;
                if (s != null)
                {
                    record = s.NetRecordEnabled != null && s.NetRecordEnabled.Value;
                    replay = s.NetReplayEnabled != null && s.NetReplayEnabled.Value;
                }
            }
            catch { }

            if (record && replay)
            {
                Stop();
                LogUtil.LogError("[VPB.Net] Net.RecordEnabled and Net.ReplayEnabled cannot both be on - pick one");
                _nextPoll = now + 30f;
                return;
            }

            bool want = record || replay;
            if (want == (_runner != null)) return;
            if (want) Start(record);
            else Stop();
        }

        static void Start(bool record)
        {
            if (_runner != null) return;
            try
            {
                _host = new GameObject(HostName);
                UnityEngine.Object.DontDestroyOnLoad(_host);
                _runner = _host.AddComponent<VpbNetRecordReplayRunner>();
                _runner.Configure(record);
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] clip runner start failed: " + e.Message);
                Stop();
            }
        }

        public static void Stop()
        {
            try
            {
                if (_runner != null)
                {
                    _runner.Shutdown();
                    _runner = null;
                }
                if (_host != null)
                {
                    UnityEngine.Object.Destroy(_host);
                    _host = null;
                }
            }
            catch { }
        }
    }

    public sealed class VpbNetRecordReplayRunner : MonoBehaviour
    {
        enum Phase { Resolve, Run, Idle }

        const float AcquireSeconds = 2.5f;
        const float ResolveRetrySeconds = 2f;
        const float ProgressSeconds = 10f;

        readonly VpbNetControllerSet _set = new VpbNetControllerSet();
        readonly List<string> _nameList = new List<string>(VpbNetControllerSet.Names.Length);

        VpbNetPoseRecorder _recorder;
        VpbNetPoseClip _clip;

        Transform[] _ctl;
        string[] _names;
        int[] _map;
        Vector3[] _pos;
        Quaternion[] _rot;
        Vector3[] _acquirePos;
        Quaternion[] _acquireRot;
        int _n;

        bool _record;
        bool _shutdown;
        Phase _phase = Phase.Resolve;

        string _atomWanted = string.Empty;
        string _clipName = string.Empty;
        int _hz = 45;
        bool _loop = true;
        bool _lockRoot;
        float _speed = 1f;

        float _nextResolveAttempt;
        bool _waitLogged;
        string _waitLoggedFor = string.Empty;
        float _nextProgress;
        float _startTime;
        float _playTime;
        float _acquireStart;
        float _sampleInterval;
        float _nextSample;
        uint _seq;
        int _loops;
        int _applied;
        int _rootClip = -1;
        int _rootCompact = -1;
        Vector3 _homePos;

        public void Configure(bool record)
        {
            _record = record;
            ReadConfig();
        }

        void ReadConfig()
        {
            try
            {
                Settings s = Settings.Instance;
                if (s == null) return;

                if (_record)
                {
                    if (s.NetRecordAtom != null && s.NetRecordAtom.Value != null) _atomWanted = s.NetRecordAtom.Value.Trim();
                    if (s.NetRecordHz != null) _hz = Mathf.Clamp(s.NetRecordHz.Value, 1, 200);
                }
                else
                {
                    if (s.NetReplayAtom != null && s.NetReplayAtom.Value != null) _atomWanted = s.NetReplayAtom.Value.Trim();
                    if (s.NetReplayLoop != null) _loop = s.NetReplayLoop.Value;
                    if (s.NetReplaySpeed != null) _speed = Mathf.Clamp(s.NetReplaySpeed.Value, 0.05f, 4f);
                    if (s.NetReplayLockRoot != null) _lockRoot = s.NetReplayLockRoot.Value;
                }
                if (s.NetClipFile != null && s.NetClipFile.Value != null) _clipName = s.NetClipFile.Value.Trim();
            }
            catch { }

            _sampleInterval = 1f / _hz;
        }

        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;

            if (_recorder != null)
            {
                _recorder.Close();
                ReportRecording();
                _recorder = null;
            }
            else if (!_record && _phase == Phase.Run)
            {
                ReportReplay();
            }

            if (!_record)
            {
                try { _set.RestoreStates(); } catch { }
                try { _set.PauseComply(30); } catch { }
            }
            _set.Clear();
            _clip = null;
        }

        void OnDestroy()
        {
            Shutdown();
        }

        void FixedUpdate()
        {
            if (_shutdown || _phase == Phase.Idle) return;

            if (_phase == Phase.Resolve)
            {
                TryResolve();
                return;
            }

            if (!_set.IsAlive())
            {
                LogUtil.LogError("[VPB.Net] the Person went away, clip runner stopped");
                Shutdown();
                _phase = Phase.Idle;
                return;
            }

            if (_record) StepRecord();
            else StepReplay();
        }

        void TryResolve()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextResolveAttempt) return;
            _nextResolveAttempt = now + ResolveRetrySeconds;

            bool loading = false;
            try { loading = LogUtil.IsSceneLoadActive() || LogUtil.IsSceneLoading(); }
            catch { }
            if (loading) return;

            Atom atom = FindAtom();
            if (atom == null)
            {
                // Log once per wanted name — the setting outlives scenes; repeating buries session logs.
                if (!_waitLogged || !string.Equals(_waitLoggedFor, _atomWanted, StringComparison.Ordinal))
                {
                    _waitLogged = true;
                    _waitLoggedFor = _atomWanted;
                    LogUtil.LogWarning("[VPB.Net] the clip harness is waiting for a Person atom"
                        + (string.IsNullOrEmpty(_atomWanted) ? string.Empty : " named \"" + _atomWanted + "\"")
                        + "; this scene has " + DescribePersons()
                        + ". Point Net." + (_record ? "RecordAtom" : "ReplayAtom")
                        + " at one of those or turn the harness off.");
                }
                return;
            }

            _waitLogged = false;
            _waitLoggedFor = string.Empty;

            if (!_set.Resolve(atom))
            {
                LogUtil.LogError("[VPB.Net] could not resolve controllers on " + atom.uid);
                return;
            }

            BuildCompactSet();
            if (_n == 0)
            {
                LogUtil.LogError("[VPB.Net] " + atom.uid + " has none of the network controllers");
                _phase = Phase.Idle;
                return;
            }

            if (_record) BeginRecord(atom);
            else BeginReplay(atom);
        }

        Atom FindAtom()
        {
            List<Atom> atoms = null;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) atoms = sc.GetAtoms();
            }
            catch { }
            if (atoms == null) return null;

            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null || !a.on || !SceneUtils.IsPersonLikeAtom(a)) continue;
                if (string.IsNullOrEmpty(_atomWanted)) return a;
                if (string.Equals(a.uid, _atomWanted, StringComparison.Ordinal)) return a;
            }
            return null;
        }

        string DescribePersons()
        {
            List<Atom> atoms = null;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) atoms = sc.GetAtoms();
            }
            catch { }
            if (atoms == null) return "no atoms yet";

            string names = string.Empty;
            int n = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (a == null || !a.on || !SceneUtils.IsPersonLikeAtom(a)) continue;
                names = n == 0 ? a.uid : names + ", " + a.uid;
                n++;
            }
            return n == 0 ? "no Person atoms" : names;
        }

        void BuildCompactSet()
        {
            _nameList.Clear();
            int shared = 0;
            for (int i = 0; i < VpbNetControllerSet.Names.Length; i++)
            {
                if (_set.Controls[i] != null) shared++;
            }

            _ctl = new Transform[shared];
            _names = new string[shared];
            _pos = new Vector3[shared];
            _rot = new Quaternion[shared];
            _acquirePos = new Vector3[shared];
            _acquireRot = new Quaternion[shared];

            int k = 0;
            _rootCompact = -1;
            for (int i = 0; i < VpbNetControllerSet.Names.Length; i++)
            {
                if (_set.Controls[i] == null) continue;
                _ctl[k] = _set.Controls[i];
                _names[k] = VpbNetControllerSet.Names[i];
                if (i == VpbNetControllerSet.RootIndex) _rootCompact = k;
                k++;
            }
            _n = shared;
        }

        void BeginRecord(Atom atom)
        {
            string path = VpbNetClipFormat.ResolvePath(
                string.IsNullOrEmpty(_clipName) ? VpbNetClipFormat.TimestampedName() : _clipName);

            _recorder = new VpbNetPoseRecorder();
            if (!_recorder.Open(path, _names, _n, _hz))
            {
                LogUtil.LogError("[VPB.Net] recording failed to start: " + _recorder.LastError);
                _recorder = null;
                _phase = Phase.Idle;
                return;
            }

            _seq = 0;
            _startTime = Time.time;
            _nextSample = 0f;
            _nextProgress = Time.realtimeSinceStartup + ProgressSeconds;
            _phase = Phase.Run;
            LogUtil.LogWarning("[VPB.Net] recording " + atom.uid + " (" + _n + " controllers) at "
                + _hz + " Hz to " + path);
        }

        void BeginReplay(Atom atom)
        {
            string path = string.IsNullOrEmpty(_clipName)
                ? VpbNetClipFormat.NewestClipPath()
                : VpbNetClipFormat.ResolvePath(_clipName);

            if (string.IsNullOrEmpty(path))
            {
                LogUtil.LogError("[VPB.Net] no clip to replay - record one first, or set Net.ClipFile. Clips live in "
                    + VpbNetClipFormat.ClipDirectory);
                _phase = Phase.Idle;
                return;
            }

            string error;
            _clip = VpbNetPoseClip.Load(path, out error);
            if (_clip == null)
            {
                LogUtil.LogError("[VPB.Net] " + error);
                _phase = Phase.Idle;
                return;
            }

            _map = new int[_clip.ControllerCount];
            int matched = _clip.MapTo(_names, _map);
            if (matched == 0)
            {
                LogUtil.LogError("[VPB.Net] none of the clip's controllers exist on " + atom.uid);
                _clip = null;
                _phase = Phase.Idle;
                return;
            }

            _pos = new Vector3[_clip.ControllerCount];
            _rot = new Quaternion[_clip.ControllerCount];

            ReadConfig();

            _rootClip = -1;
            for (int i = 0; i < _map.Length; i++)
            {
                if (_map[i] == _rootCompact) _rootClip = i;
            }
            Transform root = _rootCompact >= 0 ? _ctl[_rootCompact] : _set.Controls[VpbNetControllerSet.RootIndex];
            _homePos = root != null ? root.position : Vector3.zero;

            _set.SaveStates();
            _set.ApplyStates(FreeControllerV3.PositionState.On, FreeControllerV3.RotationState.On);

            _playTime = 0f;
            _loops = 0;
            _applied = 0;
            BeginAcquire();
            _phase = Phase.Run;

            _clip.Sample(0f, _pos, _rot);
            Vector3 clipRoot = Vector3.zero;
            if (_rootClip >= 0) clipRoot = _pos[_rootClip];
            float dx = clipRoot.x - _homePos.x;
            float dy = clipRoot.y - _homePos.y;
            float dz = clipRoot.z - _homePos.z;
            float delta = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

            LogUtil.LogWarning("[VPB.Net] replaying " + Path.GetFileName(path)
                + " on " + atom.uid
                + " lockRoot=" + (_lockRoot ? "on" : "off")
                + " skipControl=" + ((_lockRoot && _rootCompact >= 0) ? "on" : "off")
                + " home=" + _homePos.x.ToString("0.00") + "," + _homePos.y.ToString("0.00") + "," + _homePos.z.ToString("0.00")
                + " clipRoot=" + clipRoot.x.ToString("0.00") + "," + clipRoot.y.ToString("0.00") + "," + clipRoot.z.ToString("0.00")
                + " delta=" + delta.ToString("0.00") + "m"
                + (_clip.Truncated ? " truncated" : string.Empty));
            if (!_lockRoot && delta > 0.05f)
            {
                LogUtil.LogWarning("[VPB.Net] ReplayLockRoot is off - clip control is "
                    + delta.ToString("0.00") + "m from this Person's slot, so replay will move the root");
            }
        }

        void StepRecord()
        {
            float elapsed = Time.time - _startTime;
            if (elapsed < _nextSample) return;
            _nextSample += _sampleInterval;
            if (elapsed - _nextSample > 0.25f) _nextSample = elapsed + _sampleInterval;

            for (int i = 0; i < _n; i++)
            {
                Transform t = _ctl[i];
                _pos[i] = t.position;
                _rot[i] = t.rotation;
            }

            _recorder.WriteFrame(_seq, elapsed, _pos, _rot, _n);
            _seq++;

            float now = Time.realtimeSinceStartup;
            if (now < _nextProgress) return;
            _nextProgress = now + ProgressSeconds;
            LogUtil.LogWarning(string.Format("[VPB.Net] recording {0:0}s, {1} frames{2}",
                elapsed, _recorder.Frames,
                _recorder.Dropped > 0 ? ", " + _recorder.Dropped + " DROPPED (disk could not keep up)" : string.Empty));
        }

        void StepReplay()
        {
            _playTime += Time.fixedDeltaTime * _speed;

            float duration = _clip.Duration;
            if (_playTime > duration)
            {
                if (!_loop)
                {
                    LogUtil.LogWarning("[VPB.Net] replay reached the end of the clip");
                    ReportReplay();
                    _set.RestoreStates();
                    _phase = Phase.Idle;
                    return;
                }
                _playTime -= duration;
                if (_playTime < 0f) _playTime = 0f;
                _loops++;
                _clip.Rewind();
                BeginAcquire();
            }

            _clip.Sample(_playTime, _pos, _rot);

            float ox = 0f, oy = 0f, oz = 0f;
            if (_lockRoot && _rootClip >= 0 && _rootCompact >= 0)
            {
                Vector3 live = _ctl[_rootCompact].position;
                ox = live.x - _pos[_rootClip].x;
                oy = live.y - _pos[_rootClip].y;
                oz = live.z - _pos[_rootClip].z;
            }

            float acquire = 1f;
            if (_acquireStart > 0f)
            {
                float a = Mathf.Clamp01((Time.time - _acquireStart) / AcquireSeconds);
                acquire = a * a * (3f - 2f * a);
                if (a >= 1f) _acquireStart = 0f;
            }

            for (int i = 0; i < _map.Length; i++)
            {
                int k = _map[i];
                if (k < 0) continue;

                Vector3 p = _pos[i];
                Quaternion r = _rot[i];
                if (_lockRoot)
                {
                    if (k == _rootCompact) continue;
                    p.x += ox;
                    p.y += oy;
                    p.z += oz;
                }
                if (acquire < 1f)
                {
                    p = Vector3.Lerp(_acquirePos[k], p, acquire);
                    r = Quaternion.Slerp(_acquireRot[k], r, acquire);
                }

                Transform t = _ctl[k];
                t.position = p;
                t.rotation = r;
            }
            _applied++;
        }

        void BeginAcquire()
        {
            for (int i = 0; i < _n; i++)
            {
                Transform t = _ctl[i];
                _acquirePos[i] = t.position;
                _acquireRot[i] = t.rotation;
            }
            _acquireStart = Time.time;
        }

        void ReportRecording()
        {
            if (_recorder == null) return;
            long bytes = 0;
            try { if (File.Exists(_recorder.Path)) bytes = new FileInfo(_recorder.Path).Length; }
            catch { }

            LogUtil.LogWarning(string.Format(
                "[VPB.Net] recorded {0} frames ({1:0.0} KB) to {2}{3}{4}",
                _recorder.Frames, bytes / 1024.0, _recorder.Path,
                _recorder.Dropped > 0 ? " - " + _recorder.Dropped + " frames DROPPED" : string.Empty,
                string.IsNullOrEmpty(_recorder.LastError) ? string.Empty : " - " + _recorder.LastError));
        }

        void ReportReplay()
        {
            if (_clip == null) return;
            LogUtil.LogWarning(string.Format(
                "[VPB.Net] replay applied {0} ticks over {1} loop(s) of a {2:0.0}s clip",
                _applied, _loops + 1, _clip.Duration));
        }
    }
}
