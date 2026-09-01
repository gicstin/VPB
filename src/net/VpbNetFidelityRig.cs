using System;
using System.Collections.Generic;
using MeshVR.Hands;
using UnityEngine;
using VpbNet;

namespace VPB
{
    public sealed class VpbNetFingerRig
    {
        const string DrivenMode = "JSONParams";

        readonly JSONStorableFloat[] _slots = new JSONStorableFloat[VpbNetFingers.Count];
        readonly float[] _savedValues = new float[VpbNetFingers.Count];

        HandControl _leftControl;
        HandControl _rightControl;
        string _leftSavedMode;
        string _rightSavedMode;
        int _resolved;
        bool _driving;

        public bool IsResolved { get { return _resolved == VpbNetFingers.Count; } }
        public int ResolvedCount { get { return _resolved; } }
        public bool IsDriving { get { return _driving; } }

        public bool Resolve(Atom atom)
        {
            Clear();
            if (atom == null) return false;

            HandOutput[] outputs = null;
            try { outputs = atom.GetComponentsInChildren<HandOutput>(true); }
            catch { }
            if (outputs == null) return false;

            for (int i = 0; i < outputs.Length; i++)
            {
                HandOutput o = outputs[i];
                if (o == null) continue;

                bool right;
                try { right = o.hand == HandOutput.Hand.Right; }
                catch { continue; }

                if (right && _rightControl != null) continue;
                if (!right && _leftControl != null) continue;

                HandControl c = null;
                try { c = o.GetComponent<HandControl>(); }
                catch { }
                if (c == null) continue;

                if (!MapHand(o, right)) continue;
                if (right) _rightControl = c;
                else _leftControl = c;
            }

            return IsResolved;
        }

        public void Clear()
        {
            Release();
            for (int i = 0; i < _slots.Length; i++) _slots[i] = null;
            _leftControl = null;
            _rightControl = null;
            _resolved = 0;
        }

        public bool IsAlive()
        {
            return IsResolved && _leftControl != null && _rightControl != null;
        }

        public bool Read(float[] values)
        {
            if (values == null || values.Length < VpbNetFingers.Count || !IsResolved) return false;
            for (int i = 0; i < VpbNetFingers.Count; i++)
            {
                JSONStorableFloat f = _slots[i];
                if (f == null) return false;
                try { values[i] = f.val; }
                catch { values[i] = 0f; }
            }
            return true;
        }

        public bool TakeControl()
        {
            if (_driving) return true;
            if (!IsResolved) return false;

            Read(_savedValues);
            _leftSavedMode = SetMode(_leftControl, DrivenMode);
            _rightSavedMode = SetMode(_rightControl, DrivenMode);
            if (_leftSavedMode == null && _rightSavedMode == null)
            {
                LogUtil.LogWarning("[VPB.Net] the peer's hands have no finger control mode to take over;"
                    + " finger sync is off for this avatar");
                return false;
            }

            _driving = true;
            return true;
        }

        // Restore joint.targetRotation first, then the mode — FingerOutput already wrote 30 joints.
        public void Release()
        {
            if (!_driving) return;
            _driving = false;

            for (int i = 0; i < VpbNetFingers.Count; i++)
            {
                JSONStorableFloat f = _slots[i];
                if (f == null) continue;
                try { f.val = _savedValues[i]; }
                catch { }
            }

            if (_leftSavedMode != null) SetMode(_leftControl, _leftSavedMode);
            if (_rightSavedMode != null) SetMode(_rightControl, _rightSavedMode);
            _leftSavedMode = null;
            _rightSavedMode = null;
        }

        public bool Drive(float[] values)
        {
            if (values == null || values.Length < VpbNetFingers.Count) return false;
            if (!_driving || !IsResolved) return false;

            for (int i = 0; i < VpbNetFingers.Count; i++)
            {
                JSONStorableFloat f = _slots[i];
                if (f == null) continue;
                try { f.val = values[i]; }
                catch { }
            }
            return true;
        }

        static string SetMode(HandControl c, string mode)
        {
            if (c == null) return null;
            try
            {
                JSONStorableStringChooser chooser = c.fingerControlModeJSON;
                if (chooser == null) return null;
                string was = chooser.val;
                chooser.val = mode;
                return was;
            }
            catch { return null; }
        }

        bool MapHand(HandOutput o, bool right)
        {
            int at = right ? VpbNetFingers.RightBase : VpbNetFingers.LeftBase;
            int before = _resolved;

            Put(at + 0, o.thumbProximalBendJSON);
            Put(at + 1, o.thumbProximalSpreadJSON);
            Put(at + 2, o.thumbProximalTwistJSON);
            Put(at + 3, o.thumbMiddleBendJSON);
            Put(at + 4, o.thumbDistalBendJSON);

            Put(at + 5, o.indexProximalBendJSON);
            Put(at + 6, o.indexProximalSpreadJSON);
            Put(at + 7, o.indexProximalTwistJSON);
            Put(at + 8, o.indexMiddleBendJSON);
            Put(at + 9, o.indexDistalBendJSON);

            Put(at + 10, o.middleProximalBendJSON);
            Put(at + 11, o.middleProximalSpreadJSON);
            Put(at + 12, o.middleProximalTwistJSON);
            Put(at + 13, o.middleMiddleBendJSON);
            Put(at + 14, o.middleDistalBendJSON);

            Put(at + 15, o.ringProximalBendJSON);
            Put(at + 16, o.ringProximalSpreadJSON);
            Put(at + 17, o.ringProximalTwistJSON);
            Put(at + 18, o.ringMiddleBendJSON);
            Put(at + 19, o.ringDistalBendJSON);

            Put(at + 20, o.pinkyProximalBendJSON);
            Put(at + 21, o.pinkyProximalSpreadJSON);
            Put(at + 22, o.pinkyProximalTwistJSON);
            Put(at + 23, o.pinkyMiddleBendJSON);
            Put(at + 24, o.pinkyDistalBendJSON);

            return _resolved - before == VpbNetFingers.PerHand;
        }

        void Put(int index, JSONStorableFloat f)
        {
            if (f == null || _slots[index] != null) return;
            _slots[index] = f;
            _resolved++;
        }
    }

    public sealed class VpbNetGazeRig
    {
        const string TargetName = "VPBNetGazeTarget";

        EyesControl _eyes;
        GameObject _targetGo;
        Transform _target;
        Transform _savedLookAt;
        EyesControl.LookMode _savedMode;
        byte _appliedMode = 255;
        bool _driving;

        public bool IsResolved { get { return _eyes != null; } }
        public bool IsDriving { get { return _driving; } }

        public bool Resolve(Atom atom)
        {
            Clear();
            if (atom == null) return false;
            try { _eyes = atom.GetComponentInChildren<EyesControl>(true); }
            catch { _eyes = null; }
            return _eyes != null;
        }

        public void Clear()
        {
            Release();
            _eyes = null;
            if (_targetGo != null)
            {
                try { UnityEngine.Object.Destroy(_targetGo); }
                catch { }
                _targetGo = null;
                _target = null;
            }
        }

        public bool IsAlive()
        {
            return _eyes != null;
        }

        public byte Read(out Vector3 point)
        {
            point = Vector3.zero;
            if (_eyes == null) return VpbNetGaze.ModeNone;

            EyesControl.LookMode mode;
            try { mode = _eyes.currentLookMode; }
            catch { return VpbNetGaze.ModeNone; }

            if (mode == EyesControl.LookMode.None) return VpbNetGaze.ModeNone;
            if (mode == EyesControl.LookMode.Player) return VpbNetGaze.ModeViewer;

            Transform t = null;
            try
            {
                t = _eyes.lookAt;
                if (t == null && _eyes.lookAt1 != null) t = _eyes.lookAt1.target;
            }
            catch { t = null; }
            if (t == null) return VpbNetGaze.ModeNone;

            try { point = t.position; }
            catch { return VpbNetGaze.ModeNone; }
            return VpbNetGaze.ModePoint;
        }

        public bool TakeControl()
        {
            if (_driving) return true;
            if (_eyes == null) return false;

            try
            {
                _savedMode = _eyes.currentLookMode;
                _savedLookAt = _eyes.lookAt;
            }
            catch { return false; }

            if (_targetGo == null)
            {
                try
                {
                    _targetGo = new GameObject(TargetName);
                    UnityEngine.Object.DontDestroyOnLoad(_targetGo);
                    _target = _targetGo.transform;
                }
                catch
                {
                    _targetGo = null;
                    _target = null;
                    return false;
                }
            }

            _appliedMode = 255;
            _driving = true;
            return true;
        }

        public void Release()
        {
            if (!_driving) return;
            _driving = false;
            _appliedMode = 255;
            if (_eyes == null) return;
            try
            {
                _eyes.lookAt = _savedLookAt;
                _eyes.currentLookMode = _savedMode;
            }
            catch { }
        }

        public void Apply(byte mode, Vector3 worldPoint)
        {
            if (!_driving || _eyes == null) return;

            if (mode == VpbNetGaze.ModePoint)
            {
                if (_target == null) return;
                try { _target.position = worldPoint; }
                catch { return; }
            }

            if (mode == _appliedMode) return;
            _appliedMode = mode;

            try
            {
                if (mode == VpbNetGaze.ModeNone)
                {
                    _eyes.currentLookMode = EyesControl.LookMode.None;
                }
                else if (mode == VpbNetGaze.ModeViewer)
                {
                    _eyes.currentLookMode = EyesControl.LookMode.Player;
                }
                else
                {
                    _eyes.lookAt = _target;
                    _eyes.currentLookMode = EyesControl.LookMode.Target;
                }
            }
            catch { }
        }
    }

    public sealed class VpbNetJawRig
    {
        static readonly string[] Candidates = { "Mouth Open", "Mouth Open Wide" };

        DAZCharacterSelector _selector;
        DAZMorph _morph;
        string _uid;
        int _bankCount = -1;

        public bool IsResolved { get { return _morph != null; } }
        public string MorphUid { get { return _uid; } }

        public bool Resolve(Atom atom)
        {
            Clear();
            if (atom == null) return false;
            try { _selector = atom.GetComponentInChildren<DAZCharacterSelector>(true); }
            catch { _selector = null; }
            if (_selector == null) return false;
            return Find();
        }

        public void Clear()
        {
            _selector = null;
            _morph = null;
            _uid = null;
            _bankCount = -1;
        }

        public bool IsAlive()
        {
            return _selector != null && _morph != null;
        }

        public float Read()
        {
            if (_morph == null && !Find()) return 0f;
            try { return _morph.morphValue; }
            catch { return 0f; }
        }

        public bool Apply(float value)
        {
            if (_morph == null && !Find()) return false;
            try
            {
                _morph.morphValue = value;
                return true;
            }
            catch { return false; }
        }

        bool Find()
        {
            if (_selector == null) return false;

            GenerateDAZMorphsControlUI ui = null;
            try { ui = _selector.morphsControlUI; }
            catch { }
            if (ui == null) return false;

            List<DAZMorph> all = null;
            try { all = ui.GetMorphs(); }
            catch { }
            if (all == null) return false;
            if (_morph != null && all.Count == _bankCount) return true;
            _bankCount = all.Count;

            for (int c = 0; c < Candidates.Length; c++)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    DAZMorph m = all[i];
                    if (m == null) continue;

                    string name = null;
                    try { name = m.displayName; }
                    catch { }
                    if (name == null || !string.Equals(name, Candidates[c], StringComparison.OrdinalIgnoreCase))
                        continue;

                    string uid = null;
                    try { uid = m.uid; }
                    catch { }
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (!VpbNetEventCodec.IsSafeIdentifier(uid)) continue;
                    if (VpbNetEventCodec.IsPluginReference(uid)) continue;

                    _morph = m;
                    _uid = uid;
                    return true;
                }
            }

            _morph = null;
            _uid = null;
            return false;
        }
    }
}
