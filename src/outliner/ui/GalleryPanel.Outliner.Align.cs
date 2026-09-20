using System;
using System.Collections.Generic;
using UnityEngine;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        readonly List<Atom> _outlinerAlignAtoms = new List<Atom>(8);

        bool OutlinerIsPrimaryAtom(Atom atom)
        {
            if (atom == null) return false;
            string uid = "";
            try { uid = atom.uid ?? ""; } catch { uid = ""; }
            return string.Equals(uid, _outlinerSelection.PrimaryUid, StringComparison.Ordinal);
        }

        bool CollectOutlinerAlignAtoms(Atom primary)
        {
            _outlinerAlignAtoms.Clear();
            if (primary == null) return false;
            _outlinerAlignAtoms.Add(primary);
            _outlinerSelection.CopyUids(_outlinerEditUids);
            string primaryUid = "";
            try { primaryUid = primary.uid ?? ""; } catch { }
            for (int i = 0; i < _outlinerEditUids.Count; i++)
            {
                string uid = _outlinerEditUids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                if (string.Equals(uid, primaryUid, StringComparison.Ordinal)) continue;
                Atom a = OutlinerEdits.GetAtom(uid);
                if (a != null) _outlinerAlignAtoms.Add(a);
            }
            return _outlinerAlignAtoms.Count > 1;
        }

        void BuildOutlinerAlignSection(GameObject card, Atom atom, float s)
        {
            if (_outlinerSelection.Count < 2) return;
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            AddOutlinerSectionCaption(card,
                VPBTranslation.T("outliner.align", "Align to this atom"), null, s);

            GameObject match = AddOutlinerActionStrip(card, s);
            AddOutlinerFieldCaption(match, VPBTranslation.T("outliner.align.match", "Match"), s, h);
            for (int row = 0; row < 3; row++)
            {
                int world = OutlinerWorldAxis(row);
                GameObject btn = AddOutlinerXformBtn(match, OutlinerAxisLabel(row),
                    () => AlignOutlinerSelectionAxis(atom, world), s);
                AddTooltipPlain(btn, VPBTranslation.T("outliner.align.match_tip",
                    "Give every other selected atom this atom's ") + OutlinerAxisLabel(row) + ".");
            }
            GameObject turn = AddOutlinerXformBtn(match, VPBTranslation.T("outliner.align.turn", "Turn"),
                () => AlignOutlinerSelectionRotation(atom), s);
            AddTooltipPlain(turn, VPBTranslation.T("outliner.align.turn_tip",
                "Give every other selected atom this atom's rotation."));

            if (_outlinerSelection.Count < 3) return;
            GameObject spread = AddOutlinerActionStrip(card, s);
            AddOutlinerFieldCaption(spread, VPBTranslation.T("outliner.align.spread", "Spread"), s, h);
            for (int row = 0; row < 3; row++)
            {
                int world = OutlinerWorldAxis(row);
                GameObject btn = AddOutlinerXformBtn(spread, OutlinerAxisLabel(row),
                    () => SpreadOutlinerSelection(atom, world), s);
                AddTooltipPlain(btn, VPBTranslation.T("outliner.align.spread_tip",
                    "Space the selected atoms evenly between the two outermost ones along ")
                    + OutlinerAxisLabel(row) + ".");
            }
        }

        OutlinerResetSnapshot BeginOutlinerAlignSnapshot(string label)
        {
            var snap = new OutlinerResetSnapshot();
            snap.Label = label;
            snap.UndoTitle = VPBTranslation.T("outliner.align.undo", "Undo align");
            for (int i = 0; i < _outlinerAlignAtoms.Count; i++)
                snap.CaptureTransform(_outlinerAlignAtoms[i]);
            return snap;
        }

        void FinishOutlinerAlign(OutlinerResetSnapshot snap, Atom anchor, int moved)
        {
            if (moved <= 0)
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            PulseOutlinerTargets();
            ArmOutlinerResetUndo(snap);
            _outlinerLastFocused = true;
            RefreshOutlinerTransformFields(anchor);
            ShowTemporaryStatus(VPBTranslation.T("outliner.align.done", "Aligned ")
                + moved + " " + VPBTranslation.T("outliner.align.atoms", "atoms."), 2f);
        }

        void AlignOutlinerSelectionAxis(Atom anchor, int world)
        {
            if (!CollectOutlinerAlignAtoms(anchor)) return;
            OutlinerResetSnapshot snap = BeginOutlinerAlignSnapshot(
                VPBTranslation.T("outliner.position", "Position"));
            FinishOutlinerAlign(snap, anchor,
                OutlinerAlign.MatchAxis(anchor, _outlinerAlignAtoms, world));
        }

        void AlignOutlinerSelectionRotation(Atom anchor)
        {
            if (!CollectOutlinerAlignAtoms(anchor)) return;
            OutlinerResetSnapshot snap = BeginOutlinerAlignSnapshot(
                VPBTranslation.T("outliner.rotation", "Rotation"));
            FinishOutlinerAlign(snap, anchor,
                OutlinerAlign.MatchRotation(anchor, _outlinerAlignAtoms));
        }

        void SpreadOutlinerSelection(Atom anchor, int world)
        {
            if (!CollectOutlinerAlignAtoms(anchor)) return;
            if (_outlinerAlignAtoms.Count < 3)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.align.spread_need",
                    "Select three or more atoms to spread them."), 2f);
                return;
            }
            OutlinerResetSnapshot snap = BeginOutlinerAlignSnapshot(
                VPBTranslation.T("outliner.align.spread", "Spread"));
            int moved = OutlinerAlign.Spread(_outlinerAlignAtoms, world);
            if (moved <= 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.align.spread_flat",
                    "These atoms already sit at the same spot on that axis."), 2f);
                return;
            }
            FinishOutlinerAlign(snap, anchor, moved);
        }

        void CopyOutlinerTransform(Atom atom)
        {
            CopyOutlinerTransformPart(atom, OutlinerPasteMode.PositionAndRotation);
        }

        void CopyOutlinerTransformPart(Atom atom, OutlinerPasteMode part)
        {
            if (atom == null || atom.mainController == null) return;
            Transform t = OutlinerEdits.ControlTransform(atom);
            if (t == null) return;
            bool wantPos = part == OutlinerPasteMode.Position || part == OutlinerPasteMode.PositionAndRotation;
            bool wantRot = part == OutlinerPasteMode.Rotation || part == OutlinerPasteMode.PositionAndRotation;
            bool wantScale = part == OutlinerPasteMode.Scale || part == OutlinerPasteMode.PositionAndRotation;
            JSONStorableFloat sp = wantScale ? OutlinerEdits.ScaleParam(atom) : null;
            if (part == OutlinerPasteMode.Scale && sp == null)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.copy.no_scale",
                    "This atom has no scale to copy."), 2f);
                return;
            }
            if (wantPos)
            {
                _outlinerXformClipPos = t.position;
                _outlinerXformClipHasPos = true;
            }
            if (wantRot)
            {
                _outlinerXformClipRot = t.rotation;
                _outlinerXformClipHasRot = true;
            }
            if (sp != null)
            {
                _outlinerXformClipScale = sp.val;
                _outlinerXformClipHasScale = true;
            }
            try { _outlinerXformClipSource = atom.uid ?? ""; } catch { _outlinerXformClipSource = ""; }
            string what = part == OutlinerPasteMode.PositionAndRotation
                ? VPBTranslation.T("outliner.xform.coords", "Coordinates")
                : OutlinerXformPartName(part);
            ShowTemporaryStatus(what + VPBTranslation.T("outliner.xform.copied_from", " copied from ")
                + _outlinerXformClipSource + ".", 2f);
            _outlinerLastFocused = true;
            RebuildOutlinerInspector();
        }

        void PasteOutlinerTransform(Atom atom, OutlinerPasteMode mode)
        {
            if (!OutlinerXformClipHas(mode))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.xform.no_clip_part", "Copy a ")
                    + OutlinerXformPartName(mode).ToLower()
                    + VPBTranslation.T("outliner.xform.no_clip_part_2", " from some atom first."), 2f);
                return;
            }
            if (atom == null || atom.mainController == null) return;
            Transform t = OutlinerEdits.ControlTransform(atom);
            if (t == null) return;

            if (mode == OutlinerPasteMode.Scale)
            {
                if (!_outlinerXformClipHasScale || OutlinerEdits.ScaleParam(atom) == null)
                {
                    ShowTemporaryStatus(VPBTranslation.T("outliner.paste.no_scale",
                        "This atom has no scale to paste into."), 2f);
                    return;
                }
                WriteOutlinerScale(atom, _outlinerXformClipScale);
                return;
            }

            if (mode != OutlinerPasteMode.Rotation && _outlinerXformClipHasPos)
                CommitOutlinerPosition(atom, t.position, _outlinerXformClipPos);
            if (mode != OutlinerPasteMode.Position && _outlinerXformClipHasRot)
                CommitOutlinerRotation(atom, t.rotation.eulerAngles, _outlinerXformClipRot.eulerAngles);
        }
    }
}
