using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        const float OutlinerResetUndoSeconds = 10f;
        const float OutlinerDisabledIconAlpha = 0.35f;

        OutlinerResetSnapshot _outlinerResetUndo;
        float _outlinerResetUndoUntil;
        GameObject _outlinerResetUndoBtn;
        Text _outlinerResetUndoLabel;
        readonly List<Atom> _outlinerEditAtoms = new List<Atom>(8);
        readonly List<string> _outlinerEditUids = new List<string>(8);

        void CollectOutlinerEditAtoms(Atom primary, List<Atom> into)
        {
            into.Clear();
            if (primary == null) return;
            into.Add(primary);
            if (!OutlinerGroupLinked()) return;
            _outlinerSelection.CopyUids(_outlinerEditUids);
            string primaryUid = "";
            try { primaryUid = primary.uid ?? ""; } catch { }
            for (int i = 0; i < _outlinerEditUids.Count; i++)
            {
                string uid = _outlinerEditUids[i];
                if (string.IsNullOrEmpty(uid)) continue;
                if (string.Equals(uid, primaryUid, StringComparison.Ordinal)) continue;
                Atom a = OutlinerEdits.GetAtom(uid);
                if (a != null) into.Add(a);
            }
        }

        void MirrorOutlinerParamToGroup(Atom primary, OutlinerParamDescriptor d)
        {
            if (primary == null || d == null || !OutlinerGroupLinked()) return;
            JSONStorable src = null;
            try { src = OutlinerEdits.ResolveStorable(primary, d.StorableId); } catch { }
            if (src == null) return;
            CollectOutlinerEditAtoms(primary, _outlinerEditAtoms);
            for (int i = 1; i < _outlinerEditAtoms.Count; i++)
            {
                Atom target = _outlinerEditAtoms[i];
                try
                {
                    switch (d.Kind)
                    {
                        case OutlinerParamKind.Float:
                            JSONStorableFloat f = src.GetFloatJSONParam(d.ParamId);
                            if (f != null) OutlinerEdits.WriteFloat(target, d.StorableId, d.ParamId, f.val);
                            break;
                        case OutlinerParamKind.Bool:
                            JSONStorableBool b = src.GetBoolJSONParam(d.ParamId);
                            if (b != null) OutlinerEdits.WriteBool(target, d.StorableId, d.ParamId, b.val);
                            break;
                        case OutlinerParamKind.String:
                        case OutlinerParamKind.Url:
                            JSONStorableString t = src.GetStringJSONParam(d.ParamId);
                            if (t != null) OutlinerEdits.WriteString(target, d.StorableId, d.ParamId, t.val);
                            break;
                        case OutlinerParamKind.StringChooser:
                            JSONStorableStringChooser c = src.GetStringChooserJSONParam(d.ParamId);
                            if (c != null) OutlinerEdits.WriteChooser(target, d.StorableId, d.ParamId, c.val);
                            break;
                        case OutlinerParamKind.Color:
                            JSONStorableColor col = src.GetColorJSONParam(d.ParamId);
                            if (col != null) OutlinerEdits.WriteColor(target, d.StorableId, d.ParamId, col.val);
                            break;
                    }
                }
                catch { }
            }
        }

        void MirrorOutlinerFactToGroup(Atom primary, string fact, bool value)
        {
            if (primary == null || !OutlinerGroupLinked()) return;
            CollectOutlinerEditAtoms(primary, _outlinerEditAtoms);
            for (int i = 1; i < _outlinerEditAtoms.Count; i++)
            {
                Atom target = _outlinerEditAtoms[i];
                if (fact == "on") OutlinerEdits.SetOn(target, value);
                else if (fact == "hidden") OutlinerEdits.SetHidden(target, value);
                else if (fact == "collision") OutlinerEdits.SetCollision(target, value);
            }
        }

        void ResetOutlinerQualifiedCard(Atom atom, List<string> qualified, string label)
        {
            if (atom == null || qualified == null || qualified.Count == 0) return;
            var snap = new OutlinerResetSnapshot();
            snap.Label = label;
            CollectOutlinerEditAtoms(atom, _outlinerEditAtoms);
            for (int a = 0; a < _outlinerEditAtoms.Count; a++)
            {
                Atom target = _outlinerEditAtoms[a];
                for (int i = 0; i < qualified.Count; i++)
                {
                    string q = qualified[i];
                    if (string.IsNullOrEmpty(q)) continue;
                    int slash = q.IndexOf('/');
                    if (slash <= 0) continue;
                    string sid = q.Substring(0, slash);
                    string pid = q.Substring(slash + 1);
                    JSONStorable st = null;
                    try { st = OutlinerEdits.ResolveStorable(target, sid); } catch { }
                    OutlinerParamDescriptor d = OutlinerParamCatalog.DescribeNamed(st, sid, pid);
                    if (d != null) snap.CaptureParam(target, d);
                }
            }
            FinishOutlinerCardReset(snap);
        }

        void ResetOutlinerStorableCard(Atom atom, string sid, string label)
        {
            if (atom == null || string.IsNullOrEmpty(sid)) return;
            var snap = new OutlinerResetSnapshot();
            snap.Label = label;
            CollectOutlinerEditAtoms(atom, _outlinerEditAtoms);
            for (int a = 0; a < _outlinerEditAtoms.Count; a++)
            {
                Atom target = _outlinerEditAtoms[a];
                JSONStorable st = null;
                try { st = OutlinerEdits.ResolveStorable(target, sid); } catch { }
                if (st == null) continue;
                List<OutlinerParamDescriptor> desc = OutlinerParamCatalog.FromStorable(st, sid);
                if (desc == null) continue;
                for (int i = 0; i < desc.Count; i++)
                {
                    OutlinerParamDescriptor d = desc[i];
                    if (d == null || d.Kind == OutlinerParamKind.Action) continue;
                    if (!string.IsNullOrEmpty(d.DisabledReason)) continue;
                    snap.CaptureParam(target, d);
                }
            }
            FinishOutlinerCardReset(snap);
        }

        void ResetOutlinerTransformCard(Atom atom)
        {
            if (atom == null || atom.mainController == null) return;
            if (OutlinerLock.IsLocked(atom))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.lock.blocked",
                    "This atom's transform is locked — unlock it first."), 2f);
                return;
            }
            var snap = new OutlinerResetSnapshot();
            snap.Label = VPBTranslation.T("outliner.transform", "Transform");
            CollectOutlinerEditAtoms(atom, _outlinerEditAtoms);
            for (int a = 0; a < _outlinerEditAtoms.Count; a++)
                snap.CaptureTransform(_outlinerEditAtoms[a]);
            if (snap.IsEmpty) return;
            ResetOutlinerTransform(atom);
            WriteOutlinerScale(atom, 1f);
            ArmOutlinerResetUndo(snap);
            InvalidateOutlinerCards();
            RebuildOutlinerInspector();
        }

        void ResetOutlinerSection(Atom atom, bool position)
        {
            if (atom == null || atom.mainController == null) return;
            if (OutlinerLock.IsLocked(atom))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.lock.blocked",
                    "This atom's transform is locked — unlock it first."), 2f);
                return;
            }
            var snap = new OutlinerResetSnapshot();
            snap.Label = position
                ? VPBTranslation.T("outliner.position", "Position")
                : VPBTranslation.T("outliner.rotation", "Rotation");
            CollectOutlinerEditAtoms(atom, _outlinerEditAtoms);
            for (int a = 0; a < _outlinerEditAtoms.Count; a++)
                snap.CaptureTransform(_outlinerEditAtoms[a]);
            if (snap.IsEmpty) return;
            if (position) ResetOutlinerPosition(atom);
            else ResetOutlinerRotation(atom);
            ArmOutlinerResetUndo(snap);
            InvalidateOutlinerCards();
            RebuildOutlinerInspector();
        }

        void FinishOutlinerCardReset(OutlinerResetSnapshot snap)
        {
            if (snap == null || snap.IsEmpty) return;
            if (!snap.ResetToDefaults())
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            ArmOutlinerResetUndo(snap);
            _outlinerLastFocused = true;
            InvalidateOutlinerCards();
            RebuildOutlinerInspector();
        }

        void ArmOutlinerResetUndo(OutlinerResetSnapshot snap)
        {
            if (snap == null) return;
            snap.TakenAt = Time.unscaledTime;
            _outlinerResetUndo = snap;
            _outlinerResetUndoUntil = Time.unscaledTime + OutlinerResetUndoSeconds;
            SyncOutlinerResetUndoBar();
        }

        void UndoOutlinerReset()
        {
            OutlinerResetSnapshot snap = _outlinerResetUndo;
            _outlinerResetUndo = null;
            _outlinerResetUndoUntil = 0f;
            SyncOutlinerResetUndoBar();
            if (snap == null) return;
            if (!snap.Restore())
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            _outlinerLastFocused = true;
            ShowTemporaryStatus(VPBTranslation.T("outliner.reset.undone", "Undone: ") + snap.Label, 2f);
            InvalidateOutlinerCards();
            ArmOutlinerLookRecheck();
            RebuildOutlinerInspector();
        }

        static string OutlinerResetUndoTitle(OutlinerResetSnapshot snap)
        {
            if (snap != null && !string.IsNullOrEmpty(snap.UndoTitle)) return snap.UndoTitle;
            return VPBTranslation.T("outliner.reset.undo", "Undo reset");
        }

        GameObject _outlinerUndoBtn;
        GameObject _outlinerRedoBtn;

        void BuildOutlinerUndoButtons(GameObject footer, float chrome, float s)
        {
            _outlinerUndoBtn = UI.CreateFloatChromeIconButton(
                footer.transform, chrome, "arrow-back-up",
                GalleryUiColorTokens.ChromeIconWell, PerformOutlinerUndoFromBar);
            SizeOutlinerFooterIcon(_outlinerUndoBtn, chrome);
            _outlinerRedoBtn = UI.CreateFloatChromeIconButton(
                footer.transform, chrome, "arrow-forward-up",
                GalleryUiColorTokens.ChromeIconWell, PerformOutlinerRedoFromBar);
            SizeOutlinerFooterIcon(_outlinerRedoBtn, chrome);
            SyncOutlinerUndoButtons();
        }

        static void SizeOutlinerFooterIcon(GameObject btn, float chrome)
        {
            if (btn == null) return;
            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = chrome;
            le.minHeight = le.preferredHeight = chrome;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
        }

        void PerformOutlinerUndoFromBar()
        {
            _outlinerLastFocused = true;
            if (TryHandleOutlinerUndo()) return;
            ShowTemporaryStatus(VPBTranslation.T("outliner.undo.none", "Nothing left to undo here."), 1.6f);
        }

        void PerformOutlinerRedoFromBar()
        {
            _outlinerLastFocused = true;
            if (TryHandleOutlinerRedo()) return;
            ShowTemporaryStatus(VPBTranslation.T("outliner.redo.none", "Nothing to redo."), 1.6f);
        }

        int _outlinerUndoShown = -1;
        int _outlinerRedoShown = -1;

        void SyncOutlinerUndoButtons()
        {
            if (_outlinerUndoBtn == null && _outlinerRedoBtn == null) return;
            int undoCount = _outlinerUndo.UndoCount;
            int redoCount = _outlinerUndo.RedoCount;
            if (undoCount == _outlinerUndoShown && redoCount == _outlinerRedoShown) return;
            _outlinerUndoShown = undoCount;
            _outlinerRedoShown = redoCount;
            float chrome = OutlinerChromeSize(_outlinerChromeScale > 0f ? _outlinerChromeScale : 1f);
            bool canUndo = undoCount > 0;
            bool canRedo = redoCount > 0;
            StyleOutlinerUndoButton(_outlinerUndoBtn, "arrow-back-up", canUndo, chrome,
                canUndo
                    ? VPBTranslation.T("outliner.undo.tip", "Undo ") + _outlinerUndo.PeekUndoLabel()
                    : VPBTranslation.T("outliner.undo.none", "Nothing left to undo here."),
                VpbShortcut.Undo);
            StyleOutlinerUndoButton(_outlinerRedoBtn, "arrow-forward-up", canRedo, chrome,
                canRedo
                    ? VPBTranslation.T("outliner.redo.tip", "Redo the last undone edit.")
                    : VPBTranslation.T("outliner.redo.none", "Nothing to redo."),
                VpbShortcut.Redo);
        }

        void StyleOutlinerUndoButton(GameObject btn, string icon, bool enabled, float chrome,
            string tip, VpbShortcut shortcut)
        {
            if (btn == null) return;
            Color well = enabled ? GalleryUiColorTokens.ChromeIconWell : GalleryUiColorTokens.RowIdle;
            try { UI.StyleFloatChromeIconButton(btn, chrome, icon, well); }
            catch { }
            CanvasGroup cg = btn.GetComponent<CanvasGroup>();
            if (cg == null) cg = btn.AddComponent<CanvasGroup>();
            cg.alpha = enabled ? 1f : OutlinerDisabledIconAlpha;
            string pattern = "";
            try { pattern = VpbShortcutMap.GetPattern(shortcut); }
            catch { pattern = ""; }
            AddTooltipPlain(btn, string.IsNullOrEmpty(pattern) ? tip : tip + "  (" + pattern + ")");
        }

        void BuildOutlinerResetUndoBar(GameObject footer, float chrome, float s)
        {
            float h = chrome;
            _outlinerResetUndoBtn = UI.CreateChromeLayoutButton(footer.transform, 0f, h,
                VPBTranslation.T("outliner.reset.undo", "Undo reset"),
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.ActiveWarn, UndoOutlinerReset);
            if (_outlinerResetUndoBtn == null) return;
            UI.AddLE(_outlinerResetUndoBtn, preferredHeight: h, minHeight: h,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 3f * s,
                flexibleWidth: 0f, flexibleHeight: 0f);
            _outlinerResetUndoLabel = _outlinerResetUndoBtn.GetComponentInChildren<Text>();
            ApplyOutlinerScaledFont(_outlinerResetUndoLabel, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(_outlinerResetUndoLabel);
            OutlinerUseInwardHoverRim(_outlinerResetUndoBtn);
            AddTooltipPlain(_outlinerResetUndoBtn, VPBTranslation.T("outliner.reset.undo_tip",
                "Put back the values this reset replaced."));
            _outlinerResetUndoBtn.SetActive(false);
        }

        void TickOutlinerResetUndo()
        {
            if (_outlinerResetUndo == null) return;
            if (Time.unscaledTime >= _outlinerResetUndoUntil)
            {
                _outlinerResetUndo = null;
                _outlinerResetUndoUntil = 0f;
                SyncOutlinerResetUndoBar();
                return;
            }
            SyncOutlinerResetUndoLabel();
        }

        void SyncOutlinerResetUndoBar()
        {
            if (_outlinerResetUndoBtn == null) return;
            bool on = _outlinerResetUndo != null;
            if (_outlinerResetUndoBtn.activeSelf != on) _outlinerResetUndoBtn.SetActive(on);
            if (on) SyncOutlinerResetUndoLabel();
        }

        void SyncOutlinerResetUndoLabel()
        {
            if (_outlinerResetUndoLabel == null || _outlinerResetUndo == null) return;
            int left = Mathf.CeilToInt(Mathf.Max(0f, _outlinerResetUndoUntil - Time.unscaledTime));
            string next = OutlinerResetUndoTitle(_outlinerResetUndo)
                + " " + _outlinerResetUndo.Label + "  " + left + "s";
            if (!string.Equals(_outlinerResetUndoLabel.text, next, StringComparison.Ordinal))
                _outlinerResetUndoLabel.text = next;
        }
    }
}
