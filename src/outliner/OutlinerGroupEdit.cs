using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB.Outliner
{
    internal sealed class OutlinerGroupEdit
    {
        readonly List<Atom> _atoms = new List<Atom>(8);
        readonly List<Vector3> _pos = new List<Vector3>(8);
        readonly List<Quaternion> _rot = new List<Quaternion>(8);
        readonly List<float> _scale = new List<float>(8);
        Vector3 _primaryPos;
        Quaternion _primaryRot = Quaternion.identity;

        internal int Count { get { return _atoms.Count; } }

        internal void Clear()
        {
            _atoms.Clear();
            _pos.Clear();
            _rot.Clear();
            _scale.Clear();
        }

        internal bool Capture(Atom primary, IList<string> uids)
        {
            Clear();
            if (primary == null || uids == null) return false;
            Transform pt = OutlinerEdits.ControlTransform(primary);
            if (pt == null) return false;
            _primaryPos = pt.position;
            _primaryRot = pt.rotation;
            string primaryUid = "";
            try { primaryUid = primary.uid ?? ""; } catch { }
            for (int i = 0; i < uids.Count; i++)
            {
                string uid = uids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                if (string.Equals(uid, primaryUid, StringComparison.Ordinal)) continue;
                Atom a = OutlinerEdits.GetAtom(uid);
                if (a == null || a == primary) continue;
                Transform t = OutlinerEdits.ControlTransform(a);
                if (t == null) continue;
                _atoms.Add(a);
                _pos.Add(t.position);
                _rot.Add(t.rotation);
                JSONStorableFloat sp = OutlinerEdits.ScaleParam(a);
                _scale.Add(sp != null ? sp.val : 1f);
            }
            return _atoms.Count > 0;
        }

        internal void ApplyFromPrimary(Vector3 nextPos, Quaternion nextRot)
        {
            Quaternion spin = nextRot * Quaternion.Inverse(_primaryRot);
            for (int i = 0; i < _atoms.Count; i++)
            {
                Atom a = _atoms[i];
                if (a == null) continue;
                Vector3 offset = spin * (_pos[i] - _primaryPos);
                OutlinerEdits.WriteMainControllerPosition(a, nextPos + offset);
                if (spin != Quaternion.identity)
                    OutlinerEdits.WriteMainControllerRotation(a, (spin * _rot[i]).eulerAngles);
            }
        }

        internal void ApplyScaleRatio(float ratio)
        {
            if (ratio <= 0f || float.IsNaN(ratio)) return;
            for (int i = 0; i < _atoms.Count; i++)
            {
                Atom a = _atoms[i];
                if (a == null) continue;
                JSONStorableFloat sp = OutlinerEdits.ScaleParam(a);
                if (sp == null) continue;
                float next = Mathf.Clamp(_scale[i] * ratio, sp.min, sp.max);
                OutlinerEdits.WriteFloat(a, OutlinerEdits.ScaleStorableId(a), "scale", next);
            }
        }

        internal void ForEach(Action<Atom> act)
        {
            if (act == null) return;
            for (int i = 0; i < _atoms.Count; i++)
            {
                Atom a = _atoms[i];
                if (a != null) act(a);
            }
        }
    }
}
