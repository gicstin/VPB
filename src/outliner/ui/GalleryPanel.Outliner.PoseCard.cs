using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        struct OutlinerPoseValueRow
        {
            internal OutlinerPoseMorphEntry Entry;
            internal Text Value;
            internal float Shown;
        }

        const string OutlinerPoseCardId = "pose";
        const float OutlinerPoseSettleSeconds = 1.5f;
        const float OutlinerPoseValuePollSeconds = 1f;

        readonly List<OutlinerPoseMorphEntry> _outlinerPoseEntries = new List<OutlinerPoseMorphEntry>(64);
        readonly int[] _outlinerPoseBucketActive = new int[(int)OutlinerPoseBucket.Count];
        readonly int[] _outlinerPoseBucketTotal = new int[(int)OutlinerPoseBucket.Count];
        readonly List<OutlinerPoseValueRow> _outlinerPoseRows = new List<OutlinerPoseValueRow>(16);
        readonly List<Atom> _outlinerPoseAtoms = new List<Atom>(8);
        int _outlinerPoseBuiltSig;
        string _outlinerPoseBuiltUid = "";
        int _outlinerPoseCandidateSig;
        float _outlinerPoseCandidateSince;
        float _outlinerPoseNextValuePollAt;

        int OutlinerPoseSignature(int active)
        {
            int sig = 17;
            sig = sig * 31 + active;
            for (int i = 0; i < _outlinerPoseEntries.Count; i++)
            {
                OutlinerPoseMorphEntry e = _outlinerPoseEntries[i];
                if (e == null) continue;
                sig = sig * 31 + (e.Label ?? "").GetHashCode();
                sig = sig * 31 + (int)e.Bucket;
                sig = sig * 31 + (e.Driven ? 1 : 0);
            }
            return sig;
        }

        string OutlinerPoseCardTitle(int active)
        {
            string title = VPBTranslation.T("outliner.pose", "Pose morphs");
            if (active > 0) title = title + "  ·  " + active.ToString();
            return title;
        }

        void BuildOutlinerPoseCardShell(Atom atom, float s)
        {
            _outlinerPoseRows.Clear();
            _outlinerPoseBuiltUid = "";
            if (!OutlinerPoseMorphs.Supports(atom)) return;
            string type = "";
            try { type = atom.type; } catch { }
            if (!OutlinerInspectorShows(OutlinerPoseCardId, type)) return;

            bool open = OutlinerCardIsOpen(OutlinerPoseCardId, true);
            int active;
            _outlinerPoseBuiltSig = OutlinerPoseMorphs.ActiveSignature(atom, out active);
            int sig = _outlinerPoseBuiltSig;
            if (open)
            {
                active = OutlinerPoseMorphs.Collect(atom, _outlinerPoseEntries,
                    _outlinerPoseBucketActive, _outlinerPoseBucketTotal);
                sig = OutlinerPoseSignature(active);
            }
            try { _outlinerPoseBuiltUid = atom.uid ?? ""; } catch { _outlinerPoseBuiltUid = ""; }

            Atom captured = atom;
            GameObject card = BeginOutlinerCard(OutlinerPoseCardId, OutlinerPoseCardTitle(active), true, s,
                () => ZeroOutlinerPoseMorphs(captured, OutlinerPoseMorphs.AllBuckets), sig);
            if (_outlinerCardReused)
            {
                RebindOutlinerPoseValueRows(card);
                return;
            }
            if (!open)
            {
                UI.AddLE(CreateOutlinerPlaceholder(card, s),
                    preferredHeight: GalleryUiDesignTokens.HairGapRef * s);
                return;
            }
            FillOutlinerPoseCardBody(card, atom, active, s);
        }

        void RebindOutlinerPoseValueRows(GameObject card)
        {
            _outlinerPoseRows.Clear();
            if (card == null) return;
            Transform t = card.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child == null || !child.name.StartsWith("PoseRow_", StringComparison.Ordinal)) continue;
                int idx;
                if (!int.TryParse(child.name.Substring(8), out idx)) continue;
                if (idx < 0 || idx >= _outlinerPoseEntries.Count) continue;
                Transform valueTr = child.Find("Value");
                Text value = valueTr != null ? valueTr.GetComponent<Text>() : null;
                if (value == null) continue;
                OutlinerPoseValueRow row = new OutlinerPoseValueRow();
                row.Entry = _outlinerPoseEntries[idx];
                row.Value = value;
                row.Shown = row.Entry != null ? row.Entry.Value : 0f;
                _outlinerPoseRows.Add(row);
            }
        }

        void FillOutlinerPoseCardBody(GameObject card, Atom atom, int active, float s)
        {
            float h = OutlinerSlotH(s);
            int total = 0;
            for (int i = 0; i < _outlinerPoseBucketTotal.Length; i++) total += _outlinerPoseBucketTotal[i];

            if (active == 0)
            {
                string msg = total > 0
                    ? VPBTranslation.T("outliner.pose.neutral", "Every pose morph is at neutral.")
                        + "  (" + total + ")"
                    : VPBTranslation.T("outliner.pose.none", "This person exposes no pose morphs.");
                Text empty = UI.CreateLabel(card, msg,
                    GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextDim,
                    TextAnchor.MiddleLeft);
                ApplyOutlinerScaledFont(empty, GalleryUiDesignTokens.FontCaptionRef, s);
                ClipOutlinerText(empty);
                UI.AddLE(empty.gameObject, preferredHeight: h, flexibleWidth: 1f);
                return;
            }

            Text hint = UI.CreateLabel(card,
                VPBTranslation.T("outliner.pose.hint",
                    "Off neutral — a stuck expression or hand pose usually lives here."),
                GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(hint, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(hint);
            UI.AddLE(hint.gameObject, preferredHeight: h, flexibleWidth: 1f);

            GameObject actionRow = new GameObject("PoseActions");
            actionRow.transform.SetParent(card.transform, false);
            UI.AddLE(actionRow, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(actionRow, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            Atom captured = atom;
            AddOutlinerActionButton(actionRow, h, s,
                VPBTranslation.T("outliner.pose.zero_all", "Zero all pose morphs"),
                VPBTranslation.T("outliner.pose.zero_all_tip",
                    "Set every pose morph on this person back to 0 — expressions, fingers, visemes.\n"
                    + "Appearance morphs are never touched.\n"
                    + "Undo stays on the footer bar for 10 seconds."),
                "reset",
                () => ZeroOutlinerPoseMorphs(captured, OutlinerPoseMorphs.AllBuckets));

            int shown = 0;
            int max = GalleryUiDesignTokens.OutlinerPoseRowMax;
            for (int b = 0; b < (int)OutlinerPoseBucket.Count; b++)
            {
                if (_outlinerPoseBucketActive[b] == 0) continue;
                OutlinerPoseBucket bucket = (OutlinerPoseBucket)b;
                AddOutlinerPoseBucketHeader(card, atom, bucket, _outlinerPoseBucketActive[b], h, s);
                int inBucket = 0;
                for (int i = 0; i < _outlinerPoseEntries.Count; i++)
                {
                    OutlinerPoseMorphEntry e = _outlinerPoseEntries[i];
                    if (e == null || e.Bucket != bucket) continue;
                    if (shown >= max) break;
                    AddOutlinerPoseMorphRow(card, atom, e, i, h, s);
                    shown++;
                    inBucket++;
                }
                int hidden = _outlinerPoseBucketActive[b] - inBucket;
                if (hidden > 0)
                {
                    Text moreT = UI.CreateLabel(card,
                        VPBTranslation.T("outliner.look.more", "+ ") + hidden
                            + VPBTranslation.T("outliner.look.more_tail", " more"),
                        GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextDim,
                        TextAnchor.MiddleLeft);
                    ApplyOutlinerScaledFont(moreT, GalleryUiDesignTokens.FontCaptionRef, s);
                    ClipOutlinerText(moreT);
                    UI.AddLE(moreT.gameObject, preferredHeight: GalleryUiDesignTokens.ButtonSizeRef * s,
                        flexibleWidth: 1f);
                }
            }
        }

        void AddOutlinerPoseBucketHeader(GameObject card, Atom atom, OutlinerPoseBucket bucket,
            int count, float h, float s)
        {
            GameObject row = new GameObject("PoseBucket_" + (int)bucket);
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            string label = OutlinerPoseMorphs.BucketLabel(bucket);
            Text caption = UI.CreateLabel(row, label + "  ·  " + count.ToString(),
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(caption, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(caption);
            UI.AddLE(caption.gameObject, preferredHeight: h, minHeight: h,
                preferredWidth: GalleryUiDesignTokens.OutlinerAxisNameWidthRef * s,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s,
                flexibleWidth: 1f);

            Atom captured = atom;
            int capturedBucket = (int)bucket;
            AddOutlinerActionButton(row, h, s,
                VPBTranslation.T("outliner.pose.zero_group", "Zero"),
                VPBTranslation.T("outliner.pose.zero_group_tip", "Set every ")
                    + label.ToLowerInvariant()
                    + VPBTranslation.T("outliner.pose.zero_group_tip_tail",
                        " pose morph on this person back to 0.\nUndo stays on the footer bar for 10 seconds."),
                OutlinerPoseMorphs.BucketIcon(bucket),
                () => ZeroOutlinerPoseMorphs(captured, capturedBucket));
        }

        void AddOutlinerPoseMorphRow(GameObject card, Atom atom, OutlinerPoseMorphEntry e, int index,
            float h, float s)
        {
            GameObject row = new GameObject("PoseRow_" + index);
            row.transform.SetParent(card.transform, false);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s,
                UI.Pad(GalleryUiDesignTokens.Space4Ref, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            Text name = UI.CreateLabel(row, e.Label, GalleryUiDesignTokens.FontBodyRef,
                e.Driven ? GalleryUiColorTokens.TextMuted : GalleryUiColorTokens.TextPrimary,
                TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(name, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(name);
            if (name.gameObject.GetComponent<RectMask2D>() == null)
                name.gameObject.AddComponent<RectMask2D>();
            UI.AddLE(name.gameObject, preferredHeight: h, minHeight: h,
                minWidth: GalleryUiDesignTokens.ButtonSizeRef * 2f * s, flexibleWidth: 1f);

            Text value = UI.CreateLabel(row, OutlinerPoseMorphs.FormatValue(e.Value),
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.TextDim, TextAnchor.MiddleRight,
                HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "Value");
            ApplyOutlinerScaledFont(value, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(value);
            UI.AddLE(value.gameObject, preferredHeight: h, minHeight: h,
                preferredWidth: GalleryUiDesignTokens.OutlinerPoseValueWidthRef * s,
                minWidth: GalleryUiDesignTokens.OutlinerPoseValueWidthRef * s, flexibleWidth: 0f);

            string tip = e.Label;
            if (!string.IsNullOrEmpty(e.Region)) tip = tip + "\n" + e.Region;
            if (e.Driven)
            {
                tip = tip + "\n" + VPBTranslation.T("outliner.pose.driven_tip", "Driven by ")
                    + (string.IsNullOrEmpty(e.DrivenBy) ? VPBTranslation.T("outliner.pose.driven_unknown", "another morph or plugin") : e.DrivenBy)
                    + VPBTranslation.T("outliner.pose.driven_tip_tail",
                        " — zeroing it here would be overwritten on the next frame.");
            }
            else
            {
                tip = tip + "\n" + VPBTranslation.T("outliner.pose.row_tip", "Zero this one morph. Ctrl+Z puts it back.");
            }

            Atom captured = atom;
            OutlinerPoseMorphEntry capturedEntry = e;
            GameObject btn = UI.CreateFloatChromeIconButton(row.transform, h, "reset",
                e.Driven ? GalleryUiColorTokens.RowIdle : GalleryUiColorTokens.ChromeIconWell,
                () => ZeroOutlinerPoseMorph(captured, capturedEntry));
            if (btn != null)
            {
                UI.AddLE(btn, preferredWidth: h, preferredHeight: h, minWidth: h, minHeight: h,
                    flexibleWidth: 0f, flexibleHeight: 0f);
                if (e.Driven)
                {
                    CanvasGroup cg = btn.GetComponent<CanvasGroup>();
                    if (cg == null) cg = btn.AddComponent<CanvasGroup>();
                    cg.alpha = OutlinerDisabledIconAlpha;
                }
                AddTooltipPlain(btn, tip);
            }
            AddTooltipPlain(row, tip);

            OutlinerPoseValueRow live = new OutlinerPoseValueRow();
            live.Entry = e;
            live.Value = value;
            live.Shown = e.Value;
            _outlinerPoseRows.Add(live);
        }

        void ZeroOutlinerPoseMorphs(Atom atom, int bucket)
        {
            if (atom == null) return;
            if (!OutlinerEdits.CanWrite(atom))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            CollectOutlinerEditAtoms(atom, _outlinerPoseAtoms);
            string scope = bucket == OutlinerPoseMorphs.AllBuckets
                ? VPBTranslation.T("outliner.pose", "Pose morphs")
                : OutlinerPoseMorphs.BucketLabel((OutlinerPoseBucket)bucket);
            var snap = new OutlinerResetSnapshot();
            snap.Label = scope;
            snap.UndoTitle = VPBTranslation.T("outliner.pose.undo", "Restore");
            int done = 0;
            int driven = 0;
            int persons = 0;
            for (int i = 0; i < _outlinerPoseAtoms.Count; i++)
            {
                Atom a = _outlinerPoseAtoms[i];
                if (!OutlinerPoseMorphs.Supports(a)) continue;
                if (!OutlinerEdits.CanWrite(a)) continue;
                int skipped;
                done += OutlinerPoseMorphs.Zero(a, bucket, snap, out skipped);
                driven += skipped;
                persons++;
            }
            ReportOutlinerPoseZero(done, driven, persons, scope, snap);
        }

        void ReportOutlinerPoseZero(int done, int driven, int persons, string scope, OutlinerResetSnapshot snap)
        {
            if (done == 0)
            {
                string msg = driven > 0
                    ? driven + VPBTranslation.T("outliner.pose.only_driven",
                        " pose morphs are driven by a plugin or formula — nothing here to zero.")
                    : VPBTranslation.T("outliner.pose.already_neutral", "Every pose morph is already at neutral.");
                ShowTemporaryStatus(msg, 2.4f);
                return;
            }
            if (snap != null && !snap.IsEmpty) ArmOutlinerResetUndo(snap);
            _outlinerLastFocused = true;
            string status = scope + ": " + done + VPBTranslation.T("outliner.pose.zeroed_n", " morphs set to neutral");
            if (persons > 1) status = status + VPBTranslation.T("outliner.pose.on_n", " on ") + persons + VPBTranslation.T("outliner.pose.persons", " persons");
            if (driven > 0) status = status + "  ·  " + driven + VPBTranslation.T("outliner.pose.driven_left", " driven left alone");
            ShowTemporaryStatus(status, 3f);
            LogUtil.Log("[VPB.Outliner] Zero pose morphs: scope=" + scope + " zeroed=" + done
                + " driven=" + driven + " persons=" + persons);
            QueueOutlinerPoseRefresh();
        }

        void ZeroOutlinerPoseMorph(Atom atom, OutlinerPoseMorphEntry e)
        {
            if (atom == null || e == null || e.Morph == null) return;
            if (e.Driven)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.pose.driven_blocked",
                    "That morph is driven — zero its driver instead."), 2.4f);
                return;
            }
            if (!OutlinerEdits.CanWrite(atom))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            float before = OutlinerPoseMorphs.Read(e.Morph);
            if (OutlinerPoseMorphs.IsNeutral(before))
            {
                QueueOutlinerPoseRefresh();
                return;
            }
            if (!OutlinerPoseMorphs.Write(e.Morph, 0f))
            {
                ShowOutlinerWriteBlocked();
                return;
            }
            string key = OutlinerPoseMorphs.UndoKey(atom, e);
            if (key.Length > 0)
                _outlinerUndo.Push(key, e.Label, before.ToString("R"), "0");
            _outlinerLastFocused = true;
            ShowTemporaryStatus(e.Label + VPBTranslation.T("outliner.pose.zeroed_one", " set to neutral"), 1.8f);
            QueueOutlinerPoseRefresh();
        }

        void QueueOutlinerPoseRefresh()
        {
            InvalidateOutlinerCard(OutlinerPoseCardId);
            InvalidateOutlinerParamCard(OutlinerPoseMorphs.GeometryStorableId);
            _outlinerPoseCandidateSig = 0;
            RebuildOutlinerInspector();
        }

        void PollOutlinerPoseMorphs()
        {
            if (_outlinerPoseRows.Count == 0 && string.IsNullOrEmpty(_outlinerPoseBuiltUid)) return;
            Atom atom = OutlinerEdits.GetAtom(_outlinerSelection.PrimaryUid);
            if (atom == null || !OutlinerPoseMorphs.Supports(atom)) return;
            string uid = "";
            try { uid = atom.uid ?? ""; } catch { uid = ""; }
            if (!string.Equals(uid, _outlinerPoseBuiltUid, StringComparison.Ordinal)) return;
            float clock = Time.unscaledTime;
            if (clock < _outlinerPoseNextValuePollAt) return;
            _outlinerPoseNextValuePollAt = clock + OutlinerPoseValuePollSeconds;

            for (int i = 0; i < _outlinerPoseRows.Count; i++)
            {
                OutlinerPoseValueRow row = _outlinerPoseRows[i];
                if (row.Entry == null || row.Value == null) continue;
                float now = OutlinerPoseMorphs.Read(row.Entry.Morph);
                if (Mathf.Abs(now - row.Shown) < 0.005f) continue;
                row.Shown = now;
                row.Value.text = OutlinerPoseMorphs.FormatValue(now);
                _outlinerPoseRows[i] = row;
            }

            int active;
            int sig = OutlinerPoseMorphs.ActiveSignature(atom, out active);
            if (sig == _outlinerPoseBuiltSig)
            {
                _outlinerPoseCandidateSig = 0;
                return;
            }
            if (sig != _outlinerPoseCandidateSig)
            {
                _outlinerPoseCandidateSig = sig;
                _outlinerPoseCandidateSince = clock;
                return;
            }
            if (clock - _outlinerPoseCandidateSince < OutlinerPoseSettleSeconds) return;
            _outlinerPoseCandidateSig = 0;
            InvalidateOutlinerCard(OutlinerPoseCardId);
            RebuildOutlinerInspector();
        }

        internal int ZeroPoseMorphsOnScene()
        {
            List<Atom> atoms = null;
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null) atoms = sc.GetAtoms();
            }
            catch { atoms = null; }
            if (atoms == null) return 0;
            var snap = new OutlinerResetSnapshot();
            snap.Label = VPBTranslation.T("outliner.pose.scene", "Scene pose morphs");
            snap.UndoTitle = VPBTranslation.T("outliner.pose.undo", "Restore");
            int done = 0;
            int driven = 0;
            int persons = 0;
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom a = atoms[i];
                if (!OutlinerPoseMorphs.Supports(a)) continue;
                if (!OutlinerEdits.CanWrite(a)) continue;
                int skipped;
                done += OutlinerPoseMorphs.Zero(a, OutlinerPoseMorphs.AllBuckets, snap, out skipped);
                driven += skipped;
                persons++;
            }
            if (persons == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("outliner.pose.no_person", "No person in the scene."), 2f);
                return 0;
            }
            ReportOutlinerPoseZero(done, driven, persons, snap.Label, snap);
            return done;
        }

        internal void ZeroPoseMorphsOnSelection()
        {
            Atom atom = OutlinerEdits.GetAtom(_outlinerSelection.PrimaryUid);
            if (atom == null || !OutlinerPoseMorphs.Supports(atom))
            {
                try
                {
                    SuperController sc = SuperController.singleton;
                    Atom vamSel = sc != null ? sc.GetSelectedAtom() : null;
                    if (OutlinerPoseMorphs.Supports(vamSel)) atom = vamSel;
                }
                catch { }
            }
            if (atom != null && OutlinerPoseMorphs.Supports(atom))
            {
                ZeroOutlinerPoseMorphs(atom, OutlinerPoseMorphs.AllBuckets);
                return;
            }
            ZeroPoseMorphsOnScene();
        }
    }
}
