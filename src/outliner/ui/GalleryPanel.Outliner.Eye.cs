using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        const string OutlinerBatchOnKeyPrefix = "batch|on|";

        readonly List<OutlinerNode> _outlinerEyeScratch = new List<OutlinerNode>(64);
        readonly List<string> _outlinerEyeUids = new List<string>(64);
        readonly List<bool> _outlinerEyeBefore = new List<bool>(64);
        readonly List<bool> _outlinerEyeAfter = new List<bool>(64);
        readonly Dictionary<string, HashSet<string>> _outlinerGroupOnMemory =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        Dictionary<string, bool> _outlinerSoloSnapshot;
        string _outlinerSoloKey;

        GameObject CreateOutlinerRowEye(GameObject row, OutlinerRowBind bind, float slot)
        {
            float s = OutlinerLiveScale();
            float icon = OutlinerBarIconSize(slot, s);
            GameObject eye = UI.CreateFloatChromeIconButton(
                row.transform, icon, "eye",
                GalleryUiColorTokens.ChromeIconWell, () => OnOutlinerRowEye(bind));
            if (eye == null) return null;
            RectTransform ert = eye.GetComponent<RectTransform>();
            ert.anchorMin = ert.anchorMax = new Vector2(1f, 0.5f);
            ert.pivot = new Vector2(1f, 0.5f);
            ert.anchoredPosition = new Vector2(-OutlinerBarInset(s), 0f);
            ert.sizeDelta = new Vector2(icon, icon);
            bind.EyeBtn = eye;
            bind.EyeState = -1;
            return eye;
        }

        float LayoutOutlinerRowEye(OutlinerRowBind bind, OutlinerNode node, float slot, float gap)
        {
            if (bind == null || bind.EyeBtn == null || node == null) return 0f;
            bool atom = node.Kind == OutlinerNodeKind.Atom;
            bool group = node.Kind == OutlinerNodeKind.TypeGroup;
            bool show = (atom && !node.IsSystemProtected) || group;
            if (!show)
            {
                bind.EyeBtn.SetActive(false);
                if (bind.Sub != null) bind.Sub.gameObject.SetActive(false);
                return 0f;
            }
            bind.EyeBtn.SetActive(true);
            if (!group && bind.Sub != null) bind.Sub.gameObject.SetActive(false);
            RectTransform ert = bind.EyeBtn.GetComponent<RectTransform>();
            if (ert != null)
            {
                float es = OutlinerLiveScale();
                float icon = OutlinerBarIconSize(slot, es);
                ert.anchorMin = ert.anchorMax = new Vector2(1f, 0.5f);
                ert.pivot = new Vector2(1f, 0.5f);
                ert.anchoredPosition = new Vector2(-OutlinerBarInset(es), 0f);
                ert.sizeDelta = new Vector2(icon, icon);
            }
            OutlinerEyeState state;
            int onCount = 0;
            int total = 0;
            if (atom)
            {
                state = node.On ? OutlinerEyeState.AllOn : OutlinerEyeState.AllOff;
            }
            else
            {
                OutlinerVisibility.CollectAtoms(_outlinerModel, node, _outlinerEyeScratch);
                state = OutlinerVisibility.Aggregate(_outlinerEyeScratch, out onCount, out total);
            }
            float reserved = slot + gap;
            if (group && bind.Sub != null)
            {
                bool showCount = total > 0;
                bind.Sub.gameObject.SetActive(showCount);
                if (showCount)
                {
                    float s = OutlinerLiveScale();
                    string countText = state == OutlinerEyeState.AllOn
                        ? total.ToString()
                        : onCount + "/" + total;
                    if (!string.Equals(bind.Sub.text, countText, StringComparison.Ordinal))
                        bind.Sub.text = countText;
                    ApplyOutlinerScaledFont(bind.Sub, GalleryUiDesignTokens.FontCaptionRef, s);
                    bind.Sub.color = state == OutlinerEyeState.AllOff
                        ? GalleryUiColorTokens.TextDim
                        : GalleryUiColorTokens.TextMuted;
                    float subW = slot * 1.6f;
                    RectTransform srt = bind.Sub.rectTransform;
                    srt.anchorMin = new Vector2(1f, 0f);
                    srt.anchorMax = new Vector2(1f, 1f);
                    srt.pivot = new Vector2(1f, 0.5f);
                    srt.sizeDelta = new Vector2(subW, 0f);
                    srt.anchoredPosition = new Vector2(-(slot + gap), 0f);
                    reserved += subW + gap;
                }
            }
            int stateCode = (int)state;
            if (bind.EyeState != stateCode)
            {
                bind.EyeState = stateCode;
                ApplyOutlinerEyeGlyph(bind.EyeBtn, state);
            }
            string tip = OutlinerEyeTip(node, state, onCount, total);
            if (!string.Equals(bind.EyeTip, tip, StringComparison.Ordinal))
            {
                bind.EyeTip = tip;
                AddTooltipPlain(bind.EyeBtn, tip);
            }
            return reserved;
        }

        static void ApplyOutlinerEyeGlyph(GameObject btn, OutlinerEyeState state)
        {
            if (btn == null) return;
            string role = state == OutlinerEyeState.AllOff ? "eye-off" : "eye";
            Color tint;
            Color well;
            switch (state)
            {
                case OutlinerEyeState.AllOn:
                    tint = UI.BarIconGlyphTint;
                    well = GalleryUiColorTokens.ChromeIconWell;
                    break;
                case OutlinerEyeState.Mixed:
                    tint = GalleryUiColorTokens.TextMuted;
                    well = GalleryUiColorTokens.ChromeIconWell;
                    break;
                default:
                    tint = GalleryUiColorTokens.TextDim;
                    well = GalleryUiColorTokens.RowIdle;
                    break;
            }
            Image bg = btn.GetComponent<Image>();
            if (bg != null) bg.color = well;
            Transform iconTr = btn.transform.Find("Icon");
            Image icon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            if (icon == null) return;
            try
            {
                Sprite sp = UI.LoadIconSprite(role, tint);
                if (sp != null)
                {
                    UI.SetIconSprite(icon, sp);
                    icon.enabled = true;
                }
            }
            catch { }
        }

        static string OutlinerEyeTip(OutlinerNode node, OutlinerEyeState state, int onCount, int total)
        {
            if (node.Kind == OutlinerNodeKind.Atom)
            {
                string head = node.On
                    ? VPBTranslation.T("outliner.eye.atom_on", "Shown · click to hide")
                    : VPBTranslation.T("outliner.eye.atom_off", "Hidden · click to show");
                string mods = VPBTranslation.T("outliner.eye.atom_mods",
                    "Shift: solo (again to restore) · Ctrl: whole selection · Alt: with children");
                return head + "\n" + mods;
            }
            string counts = onCount + " / " + total
                + VPBTranslation.T("outliner.eye.group_shown", " shown");
            string action = OutlinerVisibility.NextOnFor(state)
                ? VPBTranslation.T("outliner.eye.group_show", "click to show all")
                : VPBTranslation.T("outliner.eye.group_hide", "click to hide all");
            string gmods = VPBTranslation.T("outliner.eye.group_mods",
                "Shift: solo this group (again to restore)");
            return counts + " · " + action + "\n" + gmods;
        }

        void OnOutlinerRowEye(OutlinerRowBind bind)
        {
            if (bind == null || _outlinerModel == null) return;
            _outlinerLastFocused = true;
            OutlinerNode node = _outlinerModel.Find(bind.NodeId);
            if (node == null) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (node.Kind == OutlinerNodeKind.TypeGroup)
            {
                if (shift) SoloOutlinerNode(node);
                else ToggleOutlinerGroupVisibility(node);
                return;
            }
            if (node.Kind != OutlinerNodeKind.Atom || node.IsSystemProtected) return;

            if (shift)
            {
                SoloOutlinerNode(node);
                return;
            }
            if (alt && node.ChildIndices.Count > 0)
            {
                OutlinerVisibility.CollectAtoms(_outlinerModel, node, _outlinerEyeScratch);
                bool nextOn = !node.On;
                ApplyOutlinerVisibilityBatch(_outlinerEyeScratch, nextOn, node.Id,
                    nextOn
                        ? VPBTranslation.T("outliner.undo.show_tree", "Show with children")
                        : VPBTranslation.T("outliner.undo.hide_tree", "Hide with children"));
                return;
            }
            if (ctrl && _outlinerSelection.Count > 1 && _outlinerSelection.Contains(node.AtomUid))
            {
                CollectOutlinerSelectedNodes(_outlinerEyeScratch);
                bool nextOn = !node.On;
                ApplyOutlinerVisibilityBatch(_outlinerEyeScratch, nextOn, "selection",
                    nextOn
                        ? VPBTranslation.T("outliner.undo.show_selection", "Show selection")
                        : VPBTranslation.T("outliner.undo.hide_selection", "Hide selection"));
                return;
            }
            Atom atom = OutlinerEdits.GetAtom(node.AtomUid);
            if (atom == null) return;
            bool cur = true;
            try { cur = atom.on; } catch { }
            if (!OutlinerEdits.CanWrite(atom))
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                    "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                return;
            }
            ToggleOutlinerOn(atom, !cur);
            node.On = !cur;
            AfterOutlinerVisibilityChange();
        }

        void CollectOutlinerSelectedNodes(List<OutlinerNode> into)
        {
            into.Clear();
            if (_outlinerModel == null) return;
            for (int i = 0; i < _outlinerModel.Nodes.Count; i++)
            {
                OutlinerNode n = _outlinerModel.Nodes[i];
                if (n == null || n.Kind != OutlinerNodeKind.Atom || n.IsSystemProtected) continue;
                if (_outlinerSelection.Contains(n.AtomUid)) into.Add(n);
            }
        }

        void ToggleOutlinerGroupVisibility(OutlinerNode group)
        {
            OutlinerVisibility.CollectAtoms(_outlinerModel, group, _outlinerEyeScratch);
            int onCount;
            int total;
            OutlinerEyeState state = OutlinerVisibility.Aggregate(_outlinerEyeScratch, out onCount, out total);
            if (total == 0) return;
            bool nextOn = OutlinerVisibility.NextOnFor(state);
            string label = group.Label;
            if (!nextOn)
            {
                if (state == OutlinerEyeState.Mixed)
                {
                    var remember = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < _outlinerEyeScratch.Count; i++)
                        if (_outlinerEyeScratch[i].On) remember.Add(_outlinerEyeScratch[i].AtomUid);
                    _outlinerGroupOnMemory[group.Id] = remember;
                }
                else _outlinerGroupOnMemory.Remove(group.Id);
                ApplyOutlinerVisibilityBatch(_outlinerEyeScratch, false, group.Id,
                    VPBTranslation.T("outliner.undo.hide_group", "Hide ") + label);
                return;
            }
            HashSet<string> wasOn;
            if (_outlinerGroupOnMemory.TryGetValue(group.Id, out wasOn) && wasOn != null && wasOn.Count > 0)
            {
                _outlinerGroupOnMemory.Remove(group.Id);
                int restored = 0;
                for (int i = 0; i < _outlinerEyeScratch.Count; i++)
                    if (wasOn.Contains(_outlinerEyeScratch[i].AtomUid)) restored++;
                if (restored > 0 && restored < _outlinerEyeScratch.Count)
                {
                    ApplyOutlinerVisibilityMask(_outlinerEyeScratch, wasOn, group.Id,
                        VPBTranslation.T("outliner.undo.show_group", "Show ") + label);
                    ShowTemporaryStatus(VPBTranslation.T("outliner.eye.restored_prefix", "Restored ")
                        + restored + " / " + _outlinerEyeScratch.Count
                        + VPBTranslation.T("outliner.eye.restored_suffix",
                            " · click again to show every one"), 2.2f);
                    return;
                }
            }
            ApplyOutlinerVisibilityBatch(_outlinerEyeScratch, true, group.Id,
                VPBTranslation.T("outliner.undo.show_group", "Show ") + label);
        }

        void SoloOutlinerNode(OutlinerNode node)
        {
            if (node == null || _outlinerModel == null) return;
            string key = node.Id;
            if (_outlinerSoloSnapshot != null && string.Equals(_outlinerSoloKey, key, StringComparison.Ordinal))
            {
                RestoreOutlinerSolo();
                return;
            }
            var keep = new HashSet<string>(StringComparer.Ordinal);
            OutlinerVisibility.CollectAtoms(_outlinerModel, node, _outlinerEyeScratch);
            for (int i = 0; i < _outlinerEyeScratch.Count; i++) keep.Add(_outlinerEyeScratch[i].AtomUid);
            if (keep.Count == 0) return;

            CollectAllOutlinerAtomNodes(_outlinerEyeScratch);
            if (_outlinerSoloSnapshot == null)
            {
                _outlinerSoloSnapshot = new Dictionary<string, bool>(StringComparer.Ordinal);
                for (int i = 0; i < _outlinerEyeScratch.Count; i++)
                    _outlinerSoloSnapshot[_outlinerEyeScratch[i].AtomUid] = _outlinerEyeScratch[i].On;
            }
            _outlinerSoloKey = key;
            ApplyOutlinerVisibilityMask(_outlinerEyeScratch, keep, "solo",
                VPBTranslation.T("outliner.undo.solo", "Solo ") + node.Label);
            ShowTemporaryStatus(VPBTranslation.T("outliner.eye.solo_on", "Solo: ") + node.Label
                + VPBTranslation.T("outliner.eye.solo_hint", " · Shift-click the same eye to restore"), 2.4f);
        }

        void RestoreOutlinerSolo()
        {
            if (_outlinerSoloSnapshot == null) return;
            CollectAllOutlinerAtomNodes(_outlinerEyeScratch);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, bool> kv in _outlinerSoloSnapshot)
                if (kv.Value) keep.Add(kv.Key);
            for (int i = 0; i < _outlinerEyeScratch.Count; i++)
            {
                string uid = _outlinerEyeScratch[i].AtomUid;
                if (!_outlinerSoloSnapshot.ContainsKey(uid) && _outlinerEyeScratch[i].On) keep.Add(uid);
            }
            int restored = _outlinerSoloSnapshot.Count;
            _outlinerSoloSnapshot = null;
            _outlinerSoloKey = null;
            ApplyOutlinerVisibilityMask(_outlinerEyeScratch, keep, "solo",
                VPBTranslation.T("outliner.undo.solo_restore", "Restore from solo"));
            ShowTemporaryStatus(VPBTranslation.T("outliner.eye.solo_off", "Solo off · restored ")
                + restored + VPBTranslation.T("outliner.eye.solo_off_suffix", " atoms"), 2f);
        }

        void CollectAllOutlinerAtomNodes(List<OutlinerNode> into)
        {
            into.Clear();
            if (_outlinerModel == null) return;
            for (int i = 0; i < _outlinerModel.Nodes.Count; i++)
            {
                OutlinerNode n = _outlinerModel.Nodes[i];
                if (n == null || n.Kind != OutlinerNodeKind.Atom || n.IsSystemProtected) continue;
                if (string.IsNullOrEmpty(n.AtomUid)) continue;
                into.Add(n);
            }
        }

        void ApplyOutlinerVisibilityBatch(List<OutlinerNode> atoms, bool on, string batchKey, string label)
        {
            ApplyOutlinerVisibilityMask(atoms, null, batchKey, label, on);
        }

        void ApplyOutlinerVisibilityMask(List<OutlinerNode> atoms, HashSet<string> onSet,
            string batchKey, string label, bool uniformOn = false)
        {
            if (atoms == null || atoms.Count == 0) return;
            _outlinerEyeUids.Clear();
            _outlinerEyeBefore.Clear();
            _outlinerEyeAfter.Clear();
            int blocked = 0;
            int changed = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                OutlinerNode n = atoms[i];
                Atom atom = OutlinerEdits.GetAtom(n.AtomUid);
                if (atom == null) continue;
                bool before = true;
                try { before = atom.on; } catch { }
                bool after = onSet != null ? onSet.Contains(n.AtomUid) : uniformOn;
                if (before == after) continue;
                if (!OutlinerEdits.SetOn(atom, after))
                {
                    blocked++;
                    continue;
                }
                n.On = after;
                changed++;
                _outlinerEyeUids.Add(n.AtomUid);
                _outlinerEyeBefore.Add(before);
                _outlinerEyeAfter.Add(after);
            }
            if (blocked > 0 && changed == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.write_blocked",
                    "Cannot edit this while Play mode is on, or the atom is locked."), 2f);
                return;
            }
            if (changed > 0)
            {
                _outlinerUndo.Push(OutlinerBatchOnKeyPrefix + batchKey, label,
                    OutlinerVisibility.EncodeStates(_outlinerEyeUids, _outlinerEyeBefore),
                    OutlinerVisibility.EncodeStates(_outlinerEyeUids, _outlinerEyeAfter));
            }
            AfterOutlinerVisibilityChange();
        }

        bool PollOutlinerOnFlags()
        {
            if (_outlinerModel == null) return false;
            bool changed = false;
            for (int i = 0; i < _outlinerModel.Nodes.Count; i++)
            {
                OutlinerNode n = _outlinerModel.Nodes[i];
                if (n == null || n.Kind != OutlinerNodeKind.Atom) continue;
                Atom atom = OutlinerEdits.GetAtom(n.AtomUid);
                if (atom == null) continue;
                bool on = n.On;
                try { on = atom.on; } catch { }
                if (on == n.On) continue;
                n.On = on;
                changed = true;
            }
            if (!changed) return false;
            for (int i = 0; i < _outlinerRowPool.Count; i++)
            {
                OutlinerRowBind b = _outlinerRowPool[i] != null
                    ? _outlinerRowPool[i].GetComponent<OutlinerRowBind>()
                    : null;
                if (b != null) b.EyeState = -1;
            }
            return true;
        }

        void AfterOutlinerVisibilityChange()
        {
            _outlinerLastFocused = true;
            for (int i = 0; i < _outlinerRowPool.Count; i++)
            {
                OutlinerRowBind b = _outlinerRowPool[i] != null
                    ? _outlinerRowPool[i].GetComponent<OutlinerRowBind>()
                    : null;
                if (b != null) b.EyeState = -1;
            }
            UpdateOutlinerVirtualVisible(true);
            SyncOutlinerFactButtons();
            QueueOutlinerRebuild();
        }

        bool TryApplyOutlinerBatchOnUndo(OutlinerUndoRecord rec, bool undo)
        {
            if (rec == null || rec.Key == null) return false;
            if (!rec.Key.StartsWith(OutlinerBatchOnKeyPrefix, StringComparison.Ordinal)) return false;
            string payload = undo ? rec.Before : rec.After;
            OutlinerVisibility.DecodeStates(payload, _outlinerEyeUids, _outlinerEyeBefore);
            for (int i = 0; i < _outlinerEyeUids.Count; i++)
            {
                Atom atom = OutlinerEdits.GetAtom(_outlinerEyeUids[i]);
                if (atom == null) continue;
                OutlinerEdits.SetOn(atom, _outlinerEyeBefore[i]);
                OutlinerNode n = _outlinerModel != null ? _outlinerModel.Find("atom:" + _outlinerEyeUids[i]) : null;
                if (n != null) n.On = _outlinerEyeBefore[i];
            }
            if (string.Equals(rec.Key, OutlinerBatchOnKeyPrefix + "solo", StringComparison.Ordinal))
            {
                _outlinerSoloSnapshot = null;
                _outlinerSoloKey = null;
            }
            AfterOutlinerVisibilityChange();
            return true;
        }
    }
}
