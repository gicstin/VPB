using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VpbNet;

namespace VPB
{
    // Rule list as a session subview. Stacked so 560-wide labels stay readable.
    public static class VpbNetRulesUi
    {
        const float MinListRef = 160f;
        const float ListMaxRef = 320f;
        const float RowPadHRef = GalleryUiDesignTokens.ControlGapRef;
        const float RowPadVRef = GalleryUiDesignTokens.ControlGapRef;
        const float SegMinRef = 72f;
        const float PeerColWidthRef = 88f;
        const float PresetWidthRef = 0f;
        const float ShareWidthRef = 200f;
        const float LabelColRef = 116f;
        const float ScrollBarRef = 10f;
        const float RowGapRef = GalleryUiDesignTokens.HairGapRef;
        const float RowHeightRef = VpbNetUiKit.LineRef * 2f + VpbNetUiKit.ButtonRef
            + RowGapRef * 2f + RowPadVRef * 2f;

        sealed class RuleRow
        {
            public byte Domain;
            public byte Axis;
            public GameObject Go;
            public Image Fill;
            public Color IdleFill;
            public VpbNetUiKit.Chip Blocked;
            public VpbNetUiKit.Chip Ask;
            public VpbNetUiKit.Chip Allowed;
            public Text Peer;
        }

        static readonly List<RuleRow> _rows = new List<RuleRow>(12);
        static readonly StringBuilder _sb = new StringBuilder(160);

        static GameObject _host;
        static VpbNetUiKit.Chip _presetLocked;
        static VpbNetUiKit.Chip _presetWatch;
        static VpbNetUiKit.Chip _presetTrust;
        static Text _presetState;
        static Text _footer;
        static VpbNetUiKit.Chip _undoChip;
        static VpbNetUiKit.Chip _shareChip;
        static VpbNetUiKit.TipLayer _tip;
        static float _scale = 1f;
        static float _nextRefresh;
        static uint _seenLocal = uint.MaxValue;
        static uint _seenPeer = uint.MaxValue;
        static bool _seenPublished;
        static bool _seenShare;
        static bool _seenShareLive;
        static bool _seenUndo;
        static float _pulseUntil;
        static byte _pulseDomain = 255;
        static byte _pulseAxis;

        const float PulseSeconds = 1.6f;

        public static bool IsOpen
        {
            get { return _host != null && _host.activeInHierarchy; }
        }

        public static void Pulse(byte domain, byte axis)
        {
            domain = VpbNetRuleTable.Answerable(domain);
            _pulseDomain = domain;
            _pulseAxis = axis;
            _pulseUntil = Time.realtimeSinceStartup + PulseSeconds;
            Open();
            ApplyPulseVisuals();
            RevealPulsed();
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public static void Open()
        {
            VpbNetSessionUi.RevealRules();
            RefreshNow();
        }

        public static void Close()
        {
            VpbNetSessionUi.HideRules();
        }

        public static void RescaleIfNeeded()
        {
            // Session window recreates this pane with it.
        }

        public static void Tick()
        {
            if (_host == null || !_host.activeInHierarchy) return;

            float now = Time.realtimeSinceStartup;
            if (_pulseUntil > 0f && now >= _pulseUntil)
            {
                _pulseUntil = 0f;
                _pulseDomain = 255;
                ApplyPulseVisuals();
            }

            if (now < _nextRefresh && _pulseUntil <= 0f) return;
            _nextRefresh = now + 0.25f;

            uint local = VpbNetRulebook.Local.Revision;
            uint peer = VpbNetRulebook.PeerRevision;
            bool published = VpbNetRulebook.PeerPublished;
            bool share = VpbNetPresence.ShareObjects;
            bool shareLive = VpbNetPresence.ShareObjectsLive;
            bool undo = VpbNetRulebook.CanUndo;
            if (local == _seenLocal && peer == _seenPeer && published == _seenPublished
                && share == _seenShare && shareLive == _seenShareLive && undo == _seenUndo
                && _pulseUntil <= 0f)
                return;

            _seenLocal = local;
            _seenPeer = peer;
            _seenPublished = published;
            _seenShare = share;
            _seenShareLive = shareLive;
            _seenUndo = undo;
            Refresh();
        }

        public static void SyncIfVisible()
        {
            RefreshNow();
        }

        static void RefreshNow()
        {
            if (_host == null) return;
            _seenLocal = VpbNetRulebook.Local.Revision;
            _seenPeer = VpbNetRulebook.PeerRevision;
            _seenPublished = VpbNetRulebook.PeerPublished;
            _seenShare = VpbNetPresence.ShareObjects;
            _seenShareLive = VpbNetPresence.ShareObjectsLive;
            _seenUndo = VpbNetRulebook.CanUndo;
            _nextRefresh = 0f;
            Refresh();
        }

        public static void Attach(GameObject parent, GameObject tipRoot, float scale)
        {
            Detach();
            if (parent == null) return;

            try
            {
                _host = parent;
                _scale = scale > 0f ? scale : 1f;
                float s = _scale;

                if (tipRoot != null) _tip = VpbNetUiKit.MakeTip(tipRoot, s);

                VpbNetUiKit.Line(parent,
                    VPBTranslation.T("net_rules.intro",
                        "Your answers, for your machine only. They cannot change them and you cannot change theirs. Seeing each other move, and seeing how they look, are always on and are not listed."),
                    VpbNetUiKit.FontCaption, UI.TextDim,
                    VpbNetUiKit.LineRef, s, true);

                BuildPresetRow(parent, s);
                BuildSessionFlags(parent, s);
                BuildColumnHeader(parent, s);
                BuildList(parent, s);
                BuildFooter(parent, s);
            }
            catch (Exception e)
            {
                LogUtil.LogError("[VPB.Net] rules pane create failed: " + e.Message);
                Detach();
            }
        }

        public static void Detach()
        {
            VpbNetUiKit.HideTip(_tip);
            _rows.Clear();
            _host = null;
            _tip = null;
            _presetLocked = null;
            _presetWatch = null;
            _presetTrust = null;
            _presetState = null;
            _footer = null;
            _undoChip = null;
            _shareChip = null;
            _seenLocal = uint.MaxValue;
            _seenPeer = uint.MaxValue;
            _seenPublished = false;
            _seenShare = false;
            _seenShareLive = false;
            _seenUndo = false;
        }

        public static void Destroy()
        {
            Detach();
            _pulseUntil = 0f;
            _pulseDomain = 255;
            NotifySettings();
        }

        static void BuildPresetRow(GameObject parent, float s)
        {
            GameObject row = VpbNetUiKit.Row(parent, VpbNetUiKit.RowRef, s);

            Text lead = VpbNetUiKit.Line(row,
                VPBTranslation.T("net_rules.preset.lead", "Start from"),
                VpbNetUiKit.FontBody, UI.TextMuted, VpbNetUiKit.RowRef, s, false);
            VpbNetUiKit.FixWidth(lead.gameObject, LabelColRef, s);

            _presetLocked = VpbNetUiKit.Btn(row,
                VPBTranslation.T("net_rules.preset.locked", "Locked down"), PresetWidthRef, s,
                ApplyLocked);
            _presetWatch = VpbNetUiKit.Btn(row,
                VPBTranslation.T("net_rules.preset.watch", "Watch together"), PresetWidthRef, s,
                ApplyWatch);
            _presetTrust = VpbNetUiKit.Btn(row,
                VPBTranslation.T("net_rules.preset.trust", "Full trust"), PresetWidthRef, s,
                ApplyTrust);

            GameObject hintRow = VpbNetUiKit.Row(parent, VpbNetUiKit.LineRef, s);
            GameObject hintPad = UI.CreateChildRT(hintRow, "HintPad");
            VpbNetUiKit.FixWidth(hintPad, LabelColRef, s);
            _presetState = VpbNetUiKit.Line(hintRow, string.Empty,
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, s, false);
        }

        static void BuildSessionFlags(GameObject parent, float s)
        {
            GameObject row = VpbNetUiKit.Row(parent, VpbNetUiKit.RowRef, s);

            Text lead = VpbNetUiKit.Line(row,
                VPBTranslation.T("net_rules.session.lead", "This session"),
                VpbNetUiKit.FontBody, UI.TextMuted, VpbNetUiKit.RowRef, s, false);
            VpbNetUiKit.FixWidth(lead.gameObject, LabelColRef, s);

            _shareChip = VpbNetUiKit.Btn(row,
                VPBTranslation.T("net_rules.session.share", "Load objects they add"), ShareWidthRef, s,
                ToggleShare);
            if (_shareChip != null && _shareChip.Go != null)
            {
                VpbNetUiKit.BindTip(_shareChip.Go, VPBTranslation.T("net_rules.session.share_tip",
                    "Whether THIS machine will spawn objects the other player adds or deletes. Off by default: a subscene can carry plugins, which then run here. Both of you must switch it on. Who may try is the \"Move things and change the room\" row below. Collisions while you play are a Physics button on the session window."),
                    _tip);
            }
        }

        static void ToggleShare()
        {
            VpbNetPresence.ToggleShareObjects();
            RefreshNow();
        }

        static void BuildColumnHeader(GameObject parent, float s)
        {
            VpbNetUiKit.Spacer(parent, GalleryUiDesignTokens.GroupGapRef, s);

            GameObject row = UI.CreateChildRT(parent, "ColumnHeader");
            UI.AddHLG(row, UI.Gap(VpbNetUiKit.GapRef, s),
                UI.Pad(RowPadHRef, RowPadHRef + ScrollBarRef, 0f, 0f, s),
                TextAnchor.MiddleLeft, true, true, false, false);
            UI.AddLE(row, minHeight: VpbNetUiKit.LineRef * s,
                preferredHeight: VpbNetUiKit.LineRef * s, flexibleHeight: 0f);

            VpbNetUiKit.Line(row, VPBTranslation.T("net_rules.col.action", "ACTION"),
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, s, false);

            Text theirs = VpbNetUiKit.Line(row, VPBTranslation.T("net_rules.col.you", "THEIRS"),
                VpbNetUiKit.FontCaption, UI.TextDim, VpbNetUiKit.LineRef, s, false);
            theirs.alignment = TextAnchor.MiddleRight;
            VpbNetUiKit.FixWidth(theirs.gameObject, PeerColWidthRef, s);
        }

        static void BuildList(GameObject parent, float s)
        {
            GameObject scroll = UI.CreateVScrollableContent(parent, UI.PopupBackdrop,
                AnchorPresets.topLeft, 0f, MinListRef * s, Vector2.zero, ScrollBarRef * s,
                GalleryUiDesignTokens.HairGapRef * s, false);
            scroll.name = "RuleList";
            UI.AddLE(scroll, minHeight: MinListRef * s, preferredHeight: ListMaxRef * s,
                flexibleHeight: 0f);

            Transform content = scroll.transform.Find("Viewport/Content");
            if (content == null) return;
            GameObject host = content.gameObject;

            _rows.Clear();
            VpbNetRuleCatalog.Entry[] entries = VpbNetRuleCatalog.Entries;
            int section = -1;
            int stripe = 0;

            for (int i = 0; i < entries.Length; i++)
            {
                VpbNetRuleCatalog.Entry e = entries[i];
                if (e.Section != section)
                {
                    section = e.Section;
                    stripe = 0;
                    BuildSectionBand(host, e.Section, s);
                }
                BuildRuleRow(host, e, s, (stripe++ & 1) == 0);
            }
        }

        static void BuildSectionBand(GameObject host, byte section, float s)
        {
            GameObject band = UI.CreateChildRT(host, "SectionBand");
            UI.AddImage(band, UI.ChromePanel, false);
            UI.AddHLG(band, 0f, UI.Pad(RowPadHRef, RowPadHRef, RowPadVRef, RowPadVRef, s),
                TextAnchor.MiddleLeft, true, true, false, false);
            float bandH = VpbNetUiKit.LineRef + RowPadVRef * 2f;
            UI.AddLE(band, minHeight: bandH * s, preferredHeight: bandH * s,
                flexibleHeight: 0f);

            VpbNetUiKit.Line(band, VpbNetRuleCatalog.SectionTitle(section),
                VpbNetUiKit.FontBody, UI.TextPrimary, VpbNetUiKit.LineRef, s, false);
            VpbNetUiKit.BindTip(band, VpbNetRuleCatalog.SectionHint(section), _tip);
        }

        static void BuildRuleRow(GameObject host, VpbNetRuleCatalog.Entry e, float s, bool even)
        {
            GameObject row = UI.CreateChildRT(host, "Rule");
            Color idle = even ? UI.PopupBackdrop : UI.ChromeDarker;
            UI.AddImage(row, idle, false);
            UI.AddVLG(row, UI.Gap(RowGapRef, s),
                UI.Pad(RowPadHRef, RowPadHRef, RowPadVRef, RowPadVRef, s),
                TextAnchor.UpperLeft, true, true, true, false);
            UI.AddLE(row, minHeight: RowHeightRef * s, preferredHeight: RowHeightRef * s,
                flexibleHeight: 0f);

            GameObject title = VpbNetUiKit.Row(row, VpbNetUiKit.LineRef, s);
            VpbNetUiKit.Line(title, e.Label, VpbNetUiKit.FontBody, UI.TextPrimary,
                VpbNetUiKit.LineRef, s, false);

            RuleRow r = new RuleRow();
            r.Domain = e.Domain;
            r.Axis = e.Axis;
            r.Go = row;
            r.Fill = row.GetComponent<Image>();
            r.IdleFill = idle;

            r.Peer = VpbNetUiKit.Line(title, "—", VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, false);
            r.Peer.alignment = TextAnchor.MiddleRight;
            VpbNetUiKit.FixWidth(r.Peer.gameObject, PeerColWidthRef, s);

            VpbNetUiKit.Line(row, e.Short, VpbNetUiKit.FontCaption, UI.TextDim,
                VpbNetUiKit.LineRef, s, false);
            VpbNetUiKit.BindTip(row, e.Tip, _tip);

            GameObject segs = VpbNetUiKit.Row(row, VpbNetUiKit.ButtonRef, s);
            byte d = e.Domain;
            byte a = e.Axis;
            r.Blocked = VpbNetUiKit.Btn(segs, VPBTranslation.T("net_rules.level.blocked", "Blocked"),
                0f, s, () => Set(d, a, VpbNetRuleLevel.Blocked));
            r.Ask = VpbNetUiKit.Btn(segs, VPBTranslation.T("net_rules.level.ask", "Ask"),
                0f, s, () => Set(d, a, VpbNetRuleLevel.Ask));
            r.Allowed = VpbNetUiKit.Btn(segs, VPBTranslation.T("net_rules.level.allowed", "Allowed"),
                0f, s, () => Set(d, a, VpbNetRuleLevel.Allowed));
            StretchSeg(r.Blocked);
            StretchSeg(r.Ask);
            StretchSeg(r.Allowed);

            _rows.Add(r);
        }

        static void StretchSeg(VpbNetUiKit.Chip c)
        {
            if (c == null || c.GoLE == null) return;
            float s = c.Scale > 0f ? c.Scale : 1f;
            c.GoLE.minWidth = SegMinRef * s;
            c.GoLE.preferredWidth = 0f;
            c.GoLE.flexibleWidth = 1f;
        }

        static void BuildFooter(GameObject parent, float s)
        {
            _footer = VpbNetUiKit.Line(parent, string.Empty, VpbNetUiKit.FontCaption,
                UI.TextDim, VpbNetUiKit.LineRef, s, true);

            GameObject acts = VpbNetUiKit.Row(parent, VpbNetUiKit.ButtonRef, s);
            _undoChip = VpbNetUiKit.Btn(acts,
                VPBTranslation.T("net_rules.undo", "Undo"), 96f, s, UndoClicked);
            VpbNetUiKit.Btn(acts, VPBTranslation.T("net_rules.done", "Done"), 104f, s, Close);
        }

        static void UndoClicked()
        {
            VpbNetRulebook.Undo();
            RefreshNow();
        }

        static void Set(byte domain, byte axis, byte level)
        {
            VpbNetRulebook.SetLocalLevel(domain, axis, level);
            RefreshNow();
        }

        static void ApplyLocked() { ApplyPreset(VpbNetRulePreset.LockedDown); }
        static void ApplyWatch() { ApplyPreset(VpbNetRulePreset.WatchTogether); }
        static void ApplyTrust() { ApplyPreset(VpbNetRulePreset.FullTrust); }

        static void ApplyPreset(byte preset)
        {
            VpbNetRulebook.ApplyPreset(preset);
            RefreshNow();
        }

        static void Refresh()
        {
            if (_host == null || !_host.activeInHierarchy) return;

            byte preset = VpbNetRulebook.LocalPreset();
            SetPresetChip(_presetLocked, preset == VpbNetRulePreset.LockedDown);
            SetPresetChip(_presetWatch, preset == VpbNetRulePreset.WatchTogether);
            SetPresetChip(_presetTrust, preset == VpbNetRulePreset.FullTrust);

            if (_presetState != null)
            {
                if (preset == VpbNetRulePreset.LockedDown)
                    _presetState.text = VPBTranslation.T("net_rules.preset.locked_hint", "move only");
                else if (preset == VpbNetRulePreset.WatchTogether)
                    _presetState.text = VPBTranslation.T("net_rules.preset.watch_hint", "room shared, body asks");
                else if (preset == VpbNetRulePreset.FullTrust)
                    _presetState.text = VPBTranslation.T("net_rules.preset.trust_hint", "never ask");
                else
                    _presetState.text = VPBTranslation.T("net_rules.preset.custom", "custom");
            }

            SyncShare();
            if (_undoChip != null) _undoChip.SetEnabled(VpbNetRulebook.CanUndo);

            bool published = VpbNetRulebook.PeerPublished;
            for (int i = 0; i < _rows.Count; i++)
            {
                RuleRow r = _rows[i];
                byte level = VpbNetRulebook.LocalLevel(r.Domain, r.Axis);
                SetSegment(r.Blocked, level == VpbNetRuleLevel.Blocked, UI.RuleBlocked, UI.RuleBlockedText);
                SetSegment(r.Ask, level == VpbNetRuleLevel.Ask, UI.RuleAsk, UI.RuleAskText);
                SetSegment(r.Allowed, level == VpbNetRuleLevel.Allowed, UI.RuleAllowed, UI.RuleAllowedText);

                if (r.Peer == null) continue;
                if (!published)
                {
                    r.Peer.text = "—";
                    r.Peer.color = UI.TextDim;
                    continue;
                }

                byte theirs = VpbNetRulebook.Peer.Effective(r.Domain, r.Axis);
                r.Peer.text = VpbNetRuleLevel.Name(theirs);
                r.Peer.color = theirs == VpbNetRuleLevel.Allowed
                    ? UI.RuleAllowedText
                    : (theirs == VpbNetRuleLevel.Ask ? UI.RuleAskText : UI.RuleBlockedText);
            }

            if (_footer == null) return;
            _sb.Length = 0;
            if (!VpbNetPresence.PeerUp)
            {
                _sb.Append(VPBTranslation.T("net_rules.footer.alone",
                    "Not in a session. These apply to whoever you connect to next."));
            }
            else if (!published)
            {
                _sb.Append(VPBTranslation.T("net_rules.footer.silent",
                    "They have not published their rules - treated as an older build that can change nothing of yours."));
            }
            else
            {
                _sb.Append(VPBTranslation.T("net_rules.footer.with", "With "));
                _sb.Append(string.IsNullOrEmpty(VpbNetPresence.PeerName)
                    ? VPBTranslation.T("net_rules.footer.peer", "the other player")
                    : VpbNetPresence.PeerName);
                _sb.Append(". ");
                _sb.Append(VPBTranslation.T("net_rules.footer.changes",
                    "A change applies at once, including to anything already waiting on an answer."));
            }
            _footer.text = _sb.ToString();
            ApplyPulseVisuals();
        }

        static void SyncShare()
        {
            if (_shareChip == null) return;
            bool want = VpbNetPresence.ShareObjects;
            if (!want)
            {
                _shareChip.SetText(VPBTranslation.T("net_rules.session.share_off", "Load objects: off"));
                _shareChip.SetRole(UI.ChromePanel, UI.TextPrimary);
            }
            else if (VpbNetPresence.ShareObjectsLive)
            {
                _shareChip.SetText(VPBTranslation.T("net_rules.session.share_on", "Load objects: both"));
                _shareChip.SetRole(UI.RuleAllowed, UI.RuleAllowedText);
            }
            else
            {
                _shareChip.SetText(VPBTranslation.T("net_rules.session.share_wait", "Load objects: waiting"));
                _shareChip.SetRole(UI.AccentBlue, UI.TextPrimary);
            }
        }

        static void SetPresetChip(VpbNetUiKit.Chip c, bool on)
        {
            if (c == null) return;
            c.SetTone(on ? UI.AccentBlue : UI.ChromePanel, on ? UI.TextPrimary : UI.TextMuted);
        }

        static void SetSegment(VpbNetUiKit.Chip c, bool on, Color fill, Color text)
        {
            if (c == null) return;
            c.SetTone(on ? fill : UI.ChromeDark, on ? text : UI.TextDim);
        }

        static void ApplyPulseVisuals()
        {
            float now = Time.realtimeSinceStartup;
            bool live = _pulseUntil > now;
            for (int i = 0; i < _rows.Count; i++)
            {
                RuleRow r = _rows[i];
                if (r.Fill == null) continue;
                bool hit = live && r.Axis == _pulseAxis && r.Domain == _pulseDomain;
                r.Fill.color = hit ? UI.RuleAsk : r.IdleFill;
            }
        }

        static void RevealPulsed()
        {
            RuleRow hit = null;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Domain == _pulseDomain && _rows[i].Axis == _pulseAxis)
                {
                    hit = _rows[i];
                    break;
                }
            }
            if (hit == null || hit.Go == null) return;

            ScrollRect sr = hit.Go.GetComponentInParent<ScrollRect>();
            if (sr == null || sr.content == null) return;
            RectTransform rowRt = hit.Go.transform as RectTransform;
            if (rowRt == null) return;
            Canvas.ForceUpdateCanvases();
            float contentH = sr.content.rect.height;
            float viewH = sr.viewport != null ? sr.viewport.rect.height : 0f;
            float span = contentH - viewH;
            if (span <= 1f)
            {
                sr.verticalNormalizedPosition = 1f;
                return;
            }
            float y = Mathf.Abs(rowRt.anchoredPosition.y);
            sr.verticalNormalizedPosition = 1f - Mathf.Clamp01(y / span);
        }

        static void NotifySettings()
        {
            try { GalleryPanel.RefreshNetRuleSettingsRows(); }
            catch { }
        }
    }
}
