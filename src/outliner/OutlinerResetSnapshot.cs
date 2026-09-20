using System;
using System.Collections.Generic;
using UnityEngine;

namespace VPB.Outliner
{
    internal sealed class OutlinerResetSnapshot
    {
        struct Entry
        {
            internal string AtomUid;
            internal string StorableId;
            internal string ParamId;
            internal OutlinerParamKind Kind;
            internal float Number;
            internal bool Flag;
            internal string Text;
            internal HSVColor Color;
        }

        struct XformEntry
        {
            internal string AtomUid;
            internal Vector3 Position;
            internal Vector3 Euler;
            internal bool HasScale;
            internal float Scale;
        }

        struct MorphEntry
        {
            internal DAZMorph Morph;
            internal float Value;
        }

        readonly List<Entry> _params = new List<Entry>(16);
        readonly List<XformEntry> _xforms = new List<XformEntry>(4);
        readonly List<MorphEntry> _morphs = new List<MorphEntry>(32);

        internal string Label;
        internal string UndoTitle;
        internal float TakenAt;

        internal bool IsEmpty { get { return _params.Count == 0 && _xforms.Count == 0 && _morphs.Count == 0; } }
        internal int MorphCount { get { return _morphs.Count; } }

        internal void CaptureMorph(DAZMorph morph)
        {
            if (morph == null) return;
            float v;
            try { v = morph.morphValue; } catch { return; }
            MorphEntry e = new MorphEntry();
            e.Morph = morph;
            e.Value = v;
            _morphs.Add(e);
        }

        internal void CaptureParam(Atom atom, OutlinerParamDescriptor d)
        {
            if (atom == null || d == null) return;
            JSONStorable st = null;
            try { st = OutlinerEdits.ResolveStorable(atom, d.StorableId); }
            catch { st = null; }
            if (st == null) return;

            Entry e = new Entry();
            e.AtomUid = atom.uid;
            e.StorableId = d.StorableId;
            e.ParamId = d.ParamId;
            e.Kind = d.Kind;
            e.Text = "";
            try
            {
                switch (d.Kind)
                {
                    case OutlinerParamKind.Float:
                        JSONStorableFloat f = st.GetFloatJSONParam(d.ParamId);
                        if (f == null) return;
                        e.Number = f.val;
                        break;
                    case OutlinerParamKind.Bool:
                        JSONStorableBool b = st.GetBoolJSONParam(d.ParamId);
                        if (b == null) return;
                        e.Flag = b.val;
                        break;
                    case OutlinerParamKind.String:
                    case OutlinerParamKind.Url:
                        JSONStorableString s = st.GetStringJSONParam(d.ParamId);
                        if (s == null) return;
                        e.Text = s.val ?? "";
                        break;
                    case OutlinerParamKind.StringChooser:
                        JSONStorableStringChooser c = st.GetStringChooserJSONParam(d.ParamId);
                        if (c == null) return;
                        e.Text = c.val ?? "";
                        break;
                    case OutlinerParamKind.Color:
                        JSONStorableColor col = st.GetColorJSONParam(d.ParamId);
                        if (col == null) return;
                        e.Color = col.val;
                        break;
                    default:
                        return;
                }
            }
            catch { return; }
            _params.Add(e);
        }

        internal void CaptureBool(Atom atom, string storableId, string paramId)
        {
            if (atom == null || string.IsNullOrEmpty(storableId) || string.IsNullOrEmpty(paramId)) return;
            JSONStorable st = null;
            try { st = OutlinerEdits.ResolveStorable(atom, storableId); }
            catch { st = null; }
            if (st == null) return;
            JSONStorableBool b = null;
            try { b = st.GetBoolJSONParam(paramId); }
            catch { b = null; }
            if (b == null) return;
            Entry e = new Entry();
            e.AtomUid = atom.uid;
            e.StorableId = storableId;
            e.ParamId = paramId;
            e.Kind = OutlinerParamKind.Bool;
            e.Text = "";
            e.Flag = b.val;
            _params.Add(e);
        }

        internal void CaptureTransform(Atom atom)
        {
            if (atom == null) return;
            Transform t = OutlinerEdits.ControlTransform(atom);
            if (t == null) return;
            XformEntry x = new XformEntry();
            x.AtomUid = atom.uid;
            x.Position = t.position;
            x.Euler = t.rotation.eulerAngles;
            JSONStorableFloat sp = OutlinerEdits.ScaleParam(atom);
            if (sp != null)
            {
                x.HasScale = true;
                x.Scale = sp.val;
            }
            _xforms.Add(x);
        }

        internal bool ResetToDefaults()
        {
            bool any = false;
            for (int i = 0; i < _params.Count; i++)
            {
                Entry e = _params[i];
                Atom atom = OutlinerEdits.GetAtom(e.AtomUid);
                if (atom == null) continue;
                switch (e.Kind)
                {
                    case OutlinerParamKind.Float:
                        any |= OutlinerEdits.ResetFloat(atom, e.StorableId, e.ParamId);
                        break;
                    case OutlinerParamKind.Bool:
                        any |= OutlinerEdits.ResetBool(atom, e.StorableId, e.ParamId);
                        break;
                    case OutlinerParamKind.String:
                    case OutlinerParamKind.Url:
                        any |= OutlinerEdits.ResetString(atom, e.StorableId, e.ParamId);
                        break;
                    case OutlinerParamKind.StringChooser:
                        any |= OutlinerEdits.ResetChooser(atom, e.StorableId, e.ParamId);
                        break;
                    case OutlinerParamKind.Color:
                        any |= OutlinerEdits.ResetColor(atom, e.StorableId, e.ParamId);
                        break;
                }
            }
            for (int i = 0; i < _xforms.Count; i++)
            {
                XformEntry x = _xforms[i];
                Atom atom = OutlinerEdits.GetAtom(x.AtomUid);
                if (atom == null) continue;
                any |= OutlinerEdits.WriteMainControllerPosition(atom, Vector3.zero);
                any |= OutlinerEdits.WriteMainControllerRotation(atom, Vector3.zero);
                if (x.HasScale)
                    any |= OutlinerEdits.WriteFloat(atom, OutlinerEdits.ScaleStorableId(atom), "scale", 1f);
            }
            for (int i = 0; i < _morphs.Count; i++)
                any |= OutlinerPoseMorphs.Write(_morphs[i].Morph, 0f);
            return any;
        }

        internal bool Restore()
        {
            bool any = false;
            for (int i = 0; i < _params.Count; i++)
            {
                Entry e = _params[i];
                Atom atom = OutlinerEdits.GetAtom(e.AtomUid);
                if (atom == null) continue;
                switch (e.Kind)
                {
                    case OutlinerParamKind.Float:
                        any |= OutlinerEdits.WriteFloat(atom, e.StorableId, e.ParamId, e.Number);
                        break;
                    case OutlinerParamKind.Bool:
                        any |= OutlinerEdits.WriteBool(atom, e.StorableId, e.ParamId, e.Flag);
                        break;
                    case OutlinerParamKind.String:
                    case OutlinerParamKind.Url:
                        any |= OutlinerEdits.WriteString(atom, e.StorableId, e.ParamId, e.Text);
                        break;
                    case OutlinerParamKind.StringChooser:
                        any |= OutlinerEdits.WriteChooser(atom, e.StorableId, e.ParamId, e.Text);
                        break;
                    case OutlinerParamKind.Color:
                        any |= OutlinerEdits.WriteColor(atom, e.StorableId, e.ParamId, e.Color);
                        break;
                }
            }
            for (int i = 0; i < _xforms.Count; i++)
            {
                XformEntry x = _xforms[i];
                Atom atom = OutlinerEdits.GetAtom(x.AtomUid);
                if (atom == null) continue;
                any |= OutlinerEdits.WriteMainControllerPosition(atom, x.Position);
                any |= OutlinerEdits.WriteMainControllerRotation(atom, x.Euler);
                if (x.HasScale)
                    any |= OutlinerEdits.WriteFloat(atom, OutlinerEdits.ScaleStorableId(atom), "scale", x.Scale);
            }
            for (int i = 0; i < _morphs.Count; i++)
                any |= OutlinerPoseMorphs.Write(_morphs[i].Morph, _morphs[i].Value);
            return any;
        }
    }
}
