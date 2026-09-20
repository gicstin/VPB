using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VPB.Outliner;

namespace VPB
{
    public partial class GalleryPanel
    {
        GameObject _outlinerInspectorScroll;
        Transform _outlinerInspectorContent;
        InputField _outlinerUidField;
        GameObject _outlinerInspectorEmpty;
        RectTransform _outlinerInspectorFilterRowRT;
        GameObject _outlinerInspectorFilterGO;
        GridLayoutGroup _outlinerInspectorFilterGrid;
        string _outlinerInspectorSection = OutlinerInspectorSections.All;
        readonly List<string> _outlinerInspectorSections = new List<string>(12);
        readonly HashSet<string> _outlinerOpenCards = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _outlinerClosedCards = new HashSet<string>(StringComparer.Ordinal);
        string _outlinerInspectorBuiltUid;

        sealed class OutlinerFactButtonBinding
        {
            internal Image Bg;
            internal Func<bool> Get;
            internal bool Shown;
        }

        readonly List<OutlinerFactButtonBinding> _outlinerFactButtons =
            new List<OutlinerFactButtonBinding>(3);

        sealed class OutlinerCardEntry
        {
            internal GameObject Go;
            internal bool Open;
            internal string Title;
            internal int Sig;
            internal bool HoldsThumbnails;
        }

        readonly Dictionary<string, OutlinerCardEntry> _outlinerCardCache =
            new Dictionary<string, OutlinerCardEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, OutlinerCardEntry> _outlinerCardLive =
            new Dictionary<string, OutlinerCardEntry>(StringComparer.Ordinal);
        readonly HashSet<string> _outlinerCardStale = new HashSet<string>(StringComparer.Ordinal);
        readonly List<OutlinerCardEntry> _outlinerCardSweep = new List<OutlinerCardEntry>(32);
        readonly List<GameObject> _outlinerInspectorOrder = new List<GameObject>(64);
        string _outlinerCardCacheUid = "";
        float _outlinerCardCacheScale;
        bool _outlinerCardCacheKeep;
        bool _outlinerCardReused;
        bool _outlinerLookReused;
        int _outlinerCardsKept;
        int _outlinerCardsDropped;

        const string OutlinerLookCardId = "look";
        const string OutlinerHeaderCardId = "__header";
        const string OutlinerLinkCardId = "__link";
        const string OutlinerTransformCardId = "transform";
        int _outlinerNavSig;
        const string OutlinerCardNamePrefix = "Card_";

        readonly Dictionary<string, bool> _outlinerStorableContentMemo =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        void BuildOutlinerInspectorPane(GameObject body, float gap, float s)
        {
            _outlinerInspectorPane = UI.CreateChildRT(body, "InspectorPane", AnchorPresets.stretchAll);
            UI.AddImage(_outlinerInspectorPane, OutlinerPaneBg);
            if (_outlinerInspectorPane.GetComponent<RectMask2D>() == null)
                _outlinerInspectorPane.AddComponent<RectMask2D>();
            float sbW = GalleryUiDesignTokens.SideTabScrollBarWidthRef * s;
            float pad = GalleryUiDesignTokens.TightGapRef * s;
            float filterH = OutlinerSlotH(s);
            _outlinerInspectorFilterRowRT = UI.CreateChildRT(_outlinerInspectorPane, "SectionNav",
                AnchorPresets.hStretchTop, new Vector2(0f, filterH), Vector2.zero).GetComponent<RectTransform>();
            BuildOutlinerInspectorSectionNav(_outlinerInspectorFilterRowRT.gameObject, s);

            _outlinerInspectorScroll = UI.CreateVScrollableContent(_outlinerInspectorPane, OutlinerPaneBg,
                AnchorPresets.stretchAll, 0f, 0f, Vector2.zero,
                sbW, pad, false);
            RectTransform scrollRT = _outlinerInspectorScroll.GetComponent<RectTransform>();
            if (scrollRT != null) scrollRT.offsetMax = new Vector2(0f, -filterH);
            ScrollRect sr = _outlinerInspectorScroll.GetComponent<ScrollRect>();
            _outlinerInspectorContent = sr != null && sr.content != null ? sr.content : _outlinerInspectorPane.transform;
            if (_outlinerInspectorContent != null)
            {
                VerticalLayoutGroup vlg = _outlinerInspectorContent.GetComponent<VerticalLayoutGroup>();
                if (vlg != null)
                    vlg.padding = UI.Pad(pad, pad, pad, pad);
            }

            _outlinerInspectorEmpty = UI.CreateLabel(_outlinerInspectorContent.gameObject,
                VPBTranslation.T("outliner.inspector.empty", "Select an atom."),
                GalleryUiDesignTokens.FontBodyRef, GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter).gameObject;
            Text emptyT = _outlinerInspectorEmpty.GetComponent<Text>();
            ApplyOutlinerScaledFont(emptyT, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(emptyT);
        }

        void RebuildOutlinerInspector()
        {
            if (_outlinerInspectorContent == null) return;
            string uid = _outlinerSelection.PrimaryUid;
            Atom atom = OutlinerEdits.GetAtom(uid);
            float s = OutlinerLiveScale();
            ClearOutlinerInspectorCards(uid, s);
            if (atom == null || string.IsNullOrEmpty(uid))
            {
                if (_outlinerInspectorEmpty != null) _outlinerInspectorEmpty.SetActive(true);
                _outlinerInspectorSections.Clear();
                RebuildOutlinerInspectorSectionNav();
                LayoutOutlinerInspectorChrome();
                    _outlinerInspectorBuiltUid = "";
                _outlinerLookItems.Clear();
                _outlinerLookThumbTargets.Clear();
                _outlinerLookThumbItems.Clear();
                _outlinerPoseRows.Clear();
                _outlinerPoseBuiltUid = "";
                return;
            }
            if (_outlinerInspectorEmpty != null) _outlinerInspectorEmpty.SetActive(false);

            _outlinerStorableContentMemo.Clear();
            CollectOutlinerInspectorSections(atom);
            RebuildOutlinerInspectorSectionNav();
            BuildOutlinerInspectorHeader(atom, s);
            BuildOutlinerLinkRow(s);
            BuildOutlinerFavouritesCard(atom, s);
            BuildOutlinerTransformCard(atom, s);
            BuildOutlinerLookCardShell(atom, s);
            BuildOutlinerPoseCardShell(atom, s);
            BuildOutlinerStorableCards(atom, s);
            SweepOutlinerCardCache();
            _outlinerCardStale.Clear();
            int moved = ApplyOutlinerInspectorOrder();
            LayoutOutlinerInspectorChrome();
            _outlinerInspectorBuiltUid = uid ?? "";
            if (!_outlinerLookReused)
            {
                _outlinerLookThumbAt = 0;
                _outlinerLookThumbTargets.Clear();
                _outlinerLookThumbItems.Clear();
            }
        }

        bool OutlinerInspectorShows(string cardId, string atomType)
        {
            return OutlinerInspectorSections.CardMatches(_outlinerInspectorSection, cardId, atomType);
        }

        void CollectOutlinerInspectorSections(Atom atom)
        {
            _outlinerInspectorSections.Clear();
            _outlinerInspectorSections.Add(OutlinerInspectorSections.All);
            if (atom == null) return;
            string type = "";
            try { type = atom.type; } catch { }
            if (atom.mainController != null)
                AddOutlinerInspectorSection(OutlinerInspectorSections.Transform);
            if (OutlinerLookPreviewsEnabled() && OutlinerLookPreview.Supports(atom))
                AddOutlinerInspectorSection(OutlinerInspectorSections.Look);
            if (OutlinerPoseMorphs.Supports(atom))
                AddOutlinerInspectorSection(OutlinerInspectorSections.Pose);
            if (_outlinerPinMap == null && VPBConfig.Instance != null)
                _outlinerPinMap = OutlinerPins.Parse(VPBConfig.Instance.OutlinerPinsJson);
            List<string> pins = null;
            if (_outlinerPinMap != null)
                _outlinerPinMap.TryGetValue(type, out pins);
            if (pins != null && pins.Count > 0)
                AddOutlinerInspectorSection(OutlinerInspectorSections.Favourites);
            CollectOutlinerStorableSections(atom, type);
            OutlinerInspectorSections.SortKeys(_outlinerInspectorSections);
            OutlinerInspectorSections.ClampVisible(_outlinerInspectorSections);
            bool keep = false;
            for (int i = 0; i < _outlinerInspectorSections.Count; i++)
            {
                if (string.Equals(_outlinerInspectorSections[i], _outlinerInspectorSection, StringComparison.Ordinal))
                {
                    keep = true;
                    break;
                }
            }
            if (!keep) _outlinerInspectorSection = OutlinerInspectorSections.All;
        }

        void AddOutlinerInspectorSection(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (int i = 0; i < _outlinerInspectorSections.Count; i++)
            {
                if (string.Equals(_outlinerInspectorSections[i], key, StringComparison.Ordinal))
                    return;
            }
            _outlinerInspectorSections.Add(key);
        }

        void BuildOutlinerInspectorSectionNav(GameObject row, float s)
        {
            UI.AddImage(row, OutlinerBarBg);
            float slot = GalleryUiDesignTokens.ButtonSizeRef * s;
            float gap = GalleryUiDesignTokens.TightGapRef * s;
            GridLayoutGroup grid = row.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(slot, slot);
            grid.spacing = new Vector2(gap, gap);
            grid.padding = UI.Pad(GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.ControlGapRef,
                GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.TightGapRef, s);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 1;
            _outlinerInspectorFilterGrid = grid;
            _outlinerInspectorFilterGO = row;
        }

        int OutlinerInspectorSectionColumns(float s)
        {
            float slot = GalleryUiDesignTokens.ButtonSizeRef * s;
            float gap = GalleryUiDesignTokens.TightGapRef * s;
            float width = 0f;
            if (_outlinerInspectorFilterRowRT != null) width = _outlinerInspectorFilterRowRT.rect.width;
            if (width < 8f && _outlinerInspectorPane != null)
            {
                RectTransform prt = _outlinerInspectorPane.GetComponent<RectTransform>();
                if (prt != null) width = prt.rect.width;
            }
            float pad = GalleryUiDesignTokens.ControlGapRef * s * 2f;
            float usable = width - pad;
            return Mathf.Max(1, Mathf.FloorToInt((usable + gap) / (slot + gap)));
        }

        void LayoutOutlinerInspectorChrome()
        {
            if (_outlinerInspectorFilterRowRT == null || _outlinerInspectorScroll == null) return;
            float s = OutlinerLiveScale();
            float slot = GalleryUiDesignTokens.ButtonSizeRef * s;
            float gap = GalleryUiDesignTokens.TightGapRef * s;
            int shown = _outlinerInspectorSections.Count;
            if (shown < 1) shown = 1;
            int cols = OutlinerInspectorSectionColumns(s);
            if (_outlinerInspectorFilterGrid != null)
            {
                _outlinerInspectorFilterGrid.cellSize = new Vector2(slot, slot);
                _outlinerInspectorFilterGrid.spacing = new Vector2(gap, gap);
                _outlinerInspectorFilterGrid.constraintCount = cols;
            }
            int lines = Mathf.Max(1, (shown + cols - 1) / cols);
            if (lines > OutlinerInspectorSections.MaxNavLines)
                lines = OutlinerInspectorSections.MaxNavLines;
            bool show = _outlinerInspectorEmpty == null || !_outlinerInspectorEmpty.activeSelf;
            float filterH = show ? lines * slot + (lines + 1) * gap : 0f;
            _outlinerInspectorFilterRowRT.sizeDelta = new Vector2(0f, filterH);
            _outlinerInspectorFilterRowRT.anchoredPosition = Vector2.zero;
            if (_outlinerInspectorFilterGO != null)
                _outlinerInspectorFilterGO.SetActive(show);
            RectTransform scrollRT = _outlinerInspectorScroll.GetComponent<RectTransform>();
            if (scrollRT != null)
                scrollRT.offsetMax = new Vector2(0f, -filterH);
        }

        void RebuildOutlinerInspectorSectionNav()
        {
            if (_outlinerInspectorFilterGO == null) return;
            int sig = 17;
            for (int i = 0; i < _outlinerInspectorSections.Count; i++)
                sig = sig * 31 + (_outlinerInspectorSections[i] ?? "").GetHashCode();
            sig = sig * 31 + (_outlinerInspectorSection ?? "").GetHashCode();
            sig = sig * 31 + Mathf.RoundToInt(OutlinerLiveScale() * 1000f);
            if (sig == _outlinerNavSig && _outlinerInspectorFilterGO.transform.childCount > 0)
            {
                LayoutOutlinerInspectorChrome();
                return;
            }
            _outlinerNavSig = sig;
            UI.DestroyAllChildren(_outlinerInspectorFilterGO.transform);
            float s = OutlinerLiveScale();
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            for (int i = 0; i < _outlinerInspectorSections.Count; i++)
            {
                string key = _outlinerInspectorSections[i];
                AddOutlinerInspectorSectionButton(key, h, s);
            }
            LayoutOutlinerInspectorChrome();
        }

        void AddOutlinerInspectorSectionButton(string key, float h, float s)
        {
            bool on = string.Equals(_outlinerInspectorSection, key, StringComparison.Ordinal);
            if (string.IsNullOrEmpty(key))
                on = string.IsNullOrEmpty(_outlinerInspectorSection);
            Color well = on ? GalleryUiColorTokens.ActiveSelected : GalleryUiColorTokens.RowIdle;
            string icon = OutlinerInspectorSections.Icon(key);
            string label = OutlinerInspectorSections.Label(key);
            string captured = key ?? "";
            GameObject btn = UI.CreateFloatChromeIconButton(
                _outlinerInspectorFilterGO.transform, h, icon, well, () => ToggleOutlinerInspectorSection(captured));
            if (btn == null) return;
            AddTooltipPlain(btn, label);
        }

        void ToggleOutlinerInspectorSection(string key)
        {
            if (string.Equals(_outlinerInspectorSection, key, StringComparison.Ordinal))
                _outlinerInspectorSection = OutlinerInspectorSections.All;
            else
                _outlinerInspectorSection = key ?? OutlinerInspectorSections.All;
            _outlinerLastFocused = true;
            RebuildOutlinerInspector();
        }

        internal bool OutlinerLinkEditEnabled()
        {
            return VPBConfig.Instance != null && VPBConfig.Instance.OutlinerLinkEdit;
        }

        internal bool OutlinerGroupLinked()
        {
            return OutlinerLinkEditEnabled() && _outlinerSelection.Count > 1;
        }

        void SetOutlinerLinkEdit(bool linked)
        {
            if (VPBConfig.Instance == null) return;
            if (VPBConfig.Instance.OutlinerLinkEdit == linked) return;
            VPBConfig.Instance.OutlinerLinkEdit = linked;
            VPBConfig.Instance.Save();
            _outlinerLastFocused = true;
            InvalidateOutlinerCards();
            RebuildOutlinerInspector();
        }

        void BuildOutlinerLinkRow(float s)
        {
            int count = _outlinerSelection.Count;
            if (count < 2) return;
            int linkSig = 17;
            linkSig = linkSig * 31 + count;
            linkSig = linkSig * 31 + (OutlinerLinkEditEnabled() ? 1 : 0);
            linkSig = linkSig * 31 + Mathf.RoundToInt(s * 1000f);
            if (TryReuseOutlinerChrome(OutlinerLinkCardId, linkSig)) return;
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject row = new GameObject("LinkRow");
            row.transform.SetParent(_outlinerInspectorContent, false);
            RecordOutlinerCard(OutlinerLinkCardId, row, false, "", linkSig);
            UI.AddLE(row, preferredHeight: h, minHeight: h, flexibleWidth: 1f);
            UI.AddHLG(row, GalleryUiDesignTokens.TightGapRef * s, UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);

            Text t = UI.CreateLabel(row, count + " " + VPBTranslation.T("outliner.link.selected", "selected"),
                GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextMuted, TextAnchor.MiddleLeft);
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontCaptionRef, s);
            ClipOutlinerText(t);
            UI.AddLE(t.gameObject, flexibleWidth: 1f, preferredHeight: h, minHeight: h);

            bool linked = OutlinerLinkEditEnabled();
            AddOutlinerModeChip(row, VPBTranslation.T("outliner.link.off", "Single"), !linked,
                VPBTranslation.T("outliner.link.off_tip",
                    "Edits change only the atom shown here."),
                () => SetOutlinerLinkEdit(false), s, h);
            AddOutlinerModeChip(row, VPBTranslation.T("outliner.link.on", "Linked"), linked,
                VPBTranslation.T("outliner.link.on_tip",
                    "Edits change every selected atom: they move, turn and scale as one group, "
                    + "and shared parameters are written to all of them."),
                () => SetOutlinerLinkEdit(true), s, h);
        }

        void AddOutlinerModeChip(GameObject row, string label, bool on, string tip,
            UnityEngine.Events.UnityAction act, float s, float h)
        {
            Color well = on ? GalleryUiColorTokens.ActiveSelected : GalleryUiColorTokens.RowIdle;
            GameObject btn = UI.CreateChromeLayoutButton(row.transform, 0f, h, label,
                GalleryUiDesignTokens.FontBodyRef, well, act);
            UI.AddLE(btn, flexibleWidth: 1f, preferredHeight: h, minHeight: h, minWidth: h);
            Text t = btn != null ? btn.GetComponentInChildren<Text>() : null;
            if (t != null) t.color = on ? GalleryUiColorTokens.TextPrimary : GalleryUiColorTokens.TextMuted;
            ApplyOutlinerScaledFont(t, GalleryUiDesignTokens.FontBodyRef, s);
            ClipOutlinerText(t);
            Button b = btn != null ? btn.GetComponent<Button>() : null;
            if (b != null)
            {
                try { UI.NeutralizeSelectableColorTint(b); } catch { }
            }
            OutlinerUseInwardHoverRim(btn);
            AddTooltipPlain(btn, tip);
        }

        void ClearOutlinerInspectorCards(string uid, float s)
        {
            _outlinerLookCard = null;
            _outlinerInspectorOrder.Clear();
            _outlinerCardsKept = 0;
            _outlinerCardsDropped = 0;
            if (_outlinerInspectorContent == null) return;
            bool retain = _outlinerCardCacheKeep
                && !string.IsNullOrEmpty(uid)
                && string.Equals(uid, _outlinerCardCacheUid, StringComparison.Ordinal)
                && Mathf.Abs(s - _outlinerCardCacheScale) < 0.0001f;
            if (retain) HoldOutlinerLiveCards();
            else DropOutlinerCardCache();
            _outlinerCardLive.Clear();
            if (!retain) DestroyAllOutlinerInspectorChildren();
            _outlinerCardCacheUid = uid ?? "";
            _outlinerCardCacheScale = s;
            _outlinerCardCacheKeep = true;
            if (_outlinerInspectorEmpty != null)
                _outlinerInspectorOrder.Add(_outlinerInspectorEmpty);
        }

        void HoldOutlinerLiveCards()
        {
            foreach (KeyValuePair<string, OutlinerCardEntry> kv in _outlinerCardLive)
            {
                OutlinerCardEntry e = kv.Value;
                if (e == null || e.Go == null) continue;
                if (!OutlinerCardCacheable(kv.Key) || _outlinerCardStale.Contains(kv.Key))
                {
                    DestroyOutlinerCard(e);
                    _outlinerCardsDropped++;
                    continue;
                }
                OutlinerCardEntry old;
                if (_outlinerCardCache.TryGetValue(kv.Key, out old) && old != null && old.Go != null)
                    DestroyOutlinerCard(old);
                _outlinerCardCache[kv.Key] = e;
                _outlinerCardsKept++;
            }
        }

        void DestroyAllOutlinerInspectorChildren()
        {
            Transform t = _outlinerInspectorContent;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject child = t.GetChild(i).gameObject;
                if (child == _outlinerInspectorEmpty) continue;
                DestroyOutlinerInspectorChild(child);
            }
        }

        static void DestroyOutlinerInspectorChild(GameObject go)
        {
            DestroyOutlinerInspectorChild(go, true);
        }

        static void DestroyOutlinerInspectorChild(GameObject go, bool mayHoldThumbnails)
        {
            if (go == null) return;
            if (mayHoldThumbnails) ReleaseOutlinerCardThumbnails(go);
            try { go.SetActive(false); } catch { }
            try { UnityEngine.Object.Destroy(go); } catch { }
        }

        int ApplyOutlinerInspectorOrder()
        {
            if (_outlinerInspectorContent == null) return 0;
            int target = 0;
            int moved = 0;
            for (int i = 0; i < _outlinerInspectorOrder.Count; i++)
            {
                GameObject go = _outlinerInspectorOrder[i];
                if (go == null) continue;
                Transform tr = go.transform;
                if (tr.parent != _outlinerInspectorContent) continue;
                if (tr.GetSiblingIndex() != target)
                {
                    tr.SetSiblingIndex(target);
                    moved++;
                }
                target++;
            }
            return moved;
        }

        void SweepOutlinerCardCache()
        {
            if (_outlinerCardCache.Count == 0) return;
            _outlinerCardSweep.Clear();
            foreach (KeyValuePair<string, OutlinerCardEntry> kv in _outlinerCardCache)
            {
                if (kv.Value != null) _outlinerCardSweep.Add(kv.Value);
            }
            _outlinerCardCache.Clear();
            for (int i = 0; i < _outlinerCardSweep.Count; i++)
            {
                DestroyOutlinerCard(_outlinerCardSweep[i]);
                _outlinerCardsDropped++;
            }
            _outlinerCardSweep.Clear();
        }

        void DropOutlinerCardCache()
        {
            SweepOutlinerCardCache();
            _outlinerCardStale.Clear();
            _outlinerCardLive.Clear();
        }

        static void DestroyOutlinerCard(OutlinerCardEntry entry)
        {
            if (entry == null || entry.Go == null) return;
            DestroyOutlinerInspectorChild(entry.Go, entry.HoldsThumbnails);
            entry.Go = null;
        }

        void InvalidateOutlinerCards()
        {
            _outlinerCardCacheKeep = false;
        }

        void InvalidateOutlinerCard(string id)
        {
            if (!string.IsNullOrEmpty(id)) _outlinerCardStale.Add(id);
        }

        void InvalidateOutlinerParamCard(string storableId)
        {
            if (!string.IsNullOrEmpty(storableId)) _outlinerCardStale.Add("st:" + storableId);
            _outlinerCardStale.Add("favourites");
        }

        static bool OutlinerCardIsThumbnailFree(string id)
        {
            return string.Equals(id, OutlinerTransformCardId, StringComparison.Ordinal)
                || string.Equals(id, OutlinerPoseCardId, StringComparison.Ordinal)
                || string.Equals(id, OutlinerHeaderCardId, StringComparison.Ordinal)
                || string.Equals(id, OutlinerLinkCardId, StringComparison.Ordinal);
        }

        static bool OutlinerCardCacheable(string id)
        {
            return !string.IsNullOrEmpty(id);
        }

        static void ReleaseOutlinerCardThumbnails(GameObject root)
        {
            if (root == null) return;
            RawImage[] images = null;
            try { images = root.GetComponentsInChildren<RawImage>(true); }
            catch { images = null; }
            if (images == null) return;
            for (int i = 0; i < images.Length; i++)
            {
                try { ClearThumbnailTarget(images[i]); } catch { }
            }
        }

        int OutlinerHeaderSignature(Atom atom, float s)
        {
            int sig = 17;
            string uid = "";
            try { uid = atom.uid ?? ""; } catch { }
            sig = sig * 31 + uid.GetHashCode();
            bool v = true;
            try { v = atom.on; } catch { }
            sig = sig * 31 + (v ? 1 : 0);
            v = false;
            try { v = atom.hidden; } catch { }
            sig = sig * 31 + (v ? 1 : 0);
            v = true;
            try { v = atom.collisionEnabled; } catch { }
            sig = sig * 31 + (v ? 1 : 0);
            sig = sig * 31 + (SceneUtils.IsPersonLikeAtom(atom) ? 1 : 0);
            sig = sig * 31 + (SceneUtils.IsSystemProtectedAtom(atom) ? 1 : 0);
            sig = sig * 31 + Mathf.RoundToInt(s * 1000f);
            return sig;
        }

        bool TryReuseOutlinerChrome(string id, int sig)
        {
            _outlinerCardReused = false;
            OutlinerCardEntry hit;
            if (!_outlinerCardCache.TryGetValue(id, out hit)) return false;
            if (hit == null || hit.Go == null || hit.Sig != sig) return false;
            _outlinerCardCache.Remove(id);
            _outlinerCardLive[id] = hit;
            _outlinerInspectorOrder.Add(hit.Go);
            _outlinerCardReused = true;
            return true;
        }

        void BuildOutlinerInspectorHeader(Atom atom, float s)
        {
            if (TryReuseOutlinerChrome(OutlinerHeaderCardId, OutlinerHeaderSignature(atom, s))) return;
            float h = OutlinerSlotH(s);
            GameObject head = UI.CreateChildRT(_outlinerInspectorContent.gameObject, "Header",
                AnchorPresets.hStretchTop, new Vector2(0f, h * 2f), Vector2.zero);
            RecordOutlinerCard(OutlinerHeaderCardId, head, false, "", OutlinerHeaderSignature(atom, s));
            UI.AddLE(head, preferredHeight: h * 2.1f, flexibleWidth: 1f);
            UI.AddVLG(head, GalleryUiDesignTokens.TightGapRef * s,
                UI.Pad(GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.TightGapRef,
                    GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.TightGapRef, s));

            string uid = "";
            try { uid = atom.uid; } catch { }

            GameObject uidRow = UI.CreateChildRT(head, "UidRow", AnchorPresets.hStretchTop, new Vector2(0f, h), Vector2.zero);
            UI.AddLE(uidRow, preferredHeight: h, flexibleWidth: 1f);
            GameObject field = UI.CreateTextInput(uidRow, 40f, h - 4f * s, uid,
                GalleryUiDesignTokens.FontBodyRef, 0f, 0f, AnchorPresets.stretchAll, null);
            _outlinerUidField = field.GetComponent<InputField>();
            if (_outlinerUidField != null)
            {
                _outlinerUidField.text = uid;
                _outlinerUidField.onEndEdit.AddListener(v => CommitOutlinerRename(atom, v));
                StyleOutlinerInputField(_outlinerUidField);
                ApplyOutlinerScaledFont(_outlinerUidField.textComponent, GalleryUiDesignTokens.FontBodyRef, s);
                ApplyOutlinerScaledFont(_outlinerUidField.placeholder as Text, GalleryUiDesignTokens.FontBodyRef, s);
            }

            GameObject actions = UI.CreateChildRT(head, "Actions", AnchorPresets.hStretchTop, new Vector2(0f, h), Vector2.zero);
            UI.AddLE(actions, preferredHeight: h, flexibleWidth: 1f);
            UI.AddHLG(actions, GalleryUiDesignTokens.HairGapRef * s, UI.Pad(0, 0, 0, 0));
            _outlinerFactButtons.Clear();
            AddOutlinerFactButton(actions, "on", VPBTranslation.T("outliner.fact.on", "Atom on"),
                () => OutlinerFactOn(atom), v => ToggleOutlinerOn(atom, v), s);
            AddOutlinerFactButton(actions, "hide", VPBTranslation.T("outliner.fact.hidden", "Hidden"),
                () => OutlinerFactHidden(atom), v => ToggleOutlinerHidden(atom, v), s);
            AddOutlinerFactButton(actions, "col", VPBTranslation.T("outliner.fact.collision", "Collision"),
                () => OutlinerFactCollision(atom), v => ToggleOutlinerCollision(atom, v), s);
            AddOutlinerChromeAction(actions, "focus-2", () => FrameOutlinerAtom(uid), "outliner.select_vam", "Select in VaM", s);
            AddOutlinerChromeAction(actions, "topology-star", () => SelectOutlinerRoot(atom), "outliner.select_root", "Select Root", s);
            if (SceneUtils.IsPersonLikeAtom(atom))
                AddOutlinerChromeAction(actions, "plug-connected", () => OpenOutlinerPluginWorkflow(atom),
                    "outliner.add_plugin", "Add plugin", s);
            if (!SceneUtils.IsSystemProtectedAtom(atom))
                AddOutlinerChromeAction(actions, "arrow-bar-to-up", () => ParentOutlinerToRoot(atom), "outliner.parent_root", "Parent to Root", s);
            AddOutlinerChromeAction(actions, "trash", () => DeleteOutlinerAtom(uid), "outliner.delete", "Delete atom", s);
        }

        static bool OutlinerFactOn(Atom atom)
        {
            try { return atom != null && atom.on; } catch { return false; }
        }

        static bool OutlinerFactHidden(Atom atom)
        {
            try { return atom != null && atom.hidden; } catch { return false; }
        }

        static bool OutlinerFactCollision(Atom atom)
        {
            try { return atom != null && atom.collisionEnabled; } catch { return false; }
        }

        void AddOutlinerFactButton(GameObject parent, string label, string tip,
            Func<bool> get, Action<bool> set, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            OutlinerFactButtonBinding bind = new OutlinerFactButtonBinding();
            bind.Get = get;
            GameObject btn = UI.CreateUIButton(parent, h, h, label, GalleryUiDesignTokens.FontBodyRef,
                0f, 0f, AnchorPresets.middleCenter, () =>
                {
                    set(!get());
                    SyncOutlinerFactButtons();
                    QueueOutlinerRebuild();
                });
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h);
            ApplyOutlinerScaledFont(btn.GetComponentInChildren<Text>(), GalleryUiDesignTokens.FontBodyRef, s);
            bind.Bg = btn.GetComponent<Image>();
            bind.Shown = !get();
            _outlinerFactButtons.Add(bind);
            SyncOutlinerFactButtons();
            AddTooltipPlain(btn, tip);
        }

        void SyncOutlinerFactButtons()
        {
            for (int i = _outlinerFactButtons.Count - 1; i >= 0; i--)
            {
                OutlinerFactButtonBinding bind = _outlinerFactButtons[i];
                if (bind == null || bind.Bg == null || bind.Get == null)
                {
                    _outlinerFactButtons.RemoveAt(i);
                    continue;
                }
                bool on = bind.Get();
                if (on == bind.Shown) continue;
                bind.Shown = on;
                bind.Bg.color = on ? GalleryUiColorTokens.ActiveOn : GalleryUiColorTokens.RowIdle;
            }
        }

        void AddOutlinerChromeAction(GameObject parent, string icon, UnityEngine.Events.UnityAction act, string tipKey, string tip, float s)
        {
            float h = GalleryUiDesignTokens.ButtonSizeRef * s;
            GameObject btn = UI.CreateFloatChromeIconButton(parent.transform, h, icon,
                GalleryUiColorTokens.ChromeIconWell, act);
            UI.AddLE(btn, preferredWidth: h, preferredHeight: h);
            AddTooltip(btn, tipKey, tip);
        }

        void BuildOutlinerFavouritesCard(Atom atom, float s)
        {
            string type = "";
            try { type = atom.type; } catch { }
            if (_outlinerPinMap == null && VPBConfig.Instance != null)
                _outlinerPinMap = OutlinerPins.Parse(VPBConfig.Instance.OutlinerPinsJson);
            List<string> pins = null;
            if (_outlinerPinMap != null)
                _outlinerPinMap.TryGetValue(type, out pins);
            if (pins == null || pins.Count == 0) return;
            if (!OutlinerInspectorShows("favourites", type)) return;
            List<string> pinsForReset = pins;
            GameObject card = BeginOutlinerCard("favourites", VPBTranslation.T("outliner.favourites", "Favourites"), true, s,
                () => ResetOutlinerQualifiedCard(atom, pinsForReset,
                    VPBTranslation.T("outliner.favourites", "Favourites")));
            if (_outlinerCardReused) return;
            if (!OutlinerCardIsOpen("favourites", true))
            {
                UI.AddLE(CreateOutlinerPlaceholder(card, s),
                    preferredHeight: GalleryUiDesignTokens.HairGapRef * s);
                return;
            }
            for (int i = 0; i < pins.Count; i++)
                AddOutlinerParamRowFromQualified(card, atom, pins[i], s);
        }

        void CollectOutlinerStorableSections(Atom atom, string type)
        {
            string[] allow = OutlinerParamCatalog.AllowlistForType(type);
            if (allow == null) return;
            for (int i = 0; i < allow.Length; i++)
            {
                string sid = allow[i];
                if (string.IsNullOrEmpty(sid)) continue;
                if (!OutlinerStorableHasContent(atom, sid)) continue;
                AddOutlinerInspectorSection(OutlinerInspectorSections.KeyForStorable(sid));
            }
        }

        void BuildOutlinerStorableCards(Atom atom, float s)
        {
            string type = "";
            try { type = atom.type; } catch { }
            string[] allow = OutlinerParamCatalog.AllowlistForType(type);
            if (allow == null) return;
            for (int i = 0; i < allow.Length; i++)
                BuildOutlinerOneStorableCard(atom, type, allow[i], s);
        }

        void BuildOutlinerOneStorableCard(Atom atom, string type, string sid, float s)
        {
            if (string.IsNullOrEmpty(sid)) return;
            if (!OutlinerInspectorShows("st:" + sid, type)) return;
            if (!OutlinerStorableHasContent(atom, sid))
            { return; }
            bool open = OutlinerCardIsOpen("st:" + sid, true);
            string cardTitle = OutlinerStorableCardTitle(sid);
            GameObject card = BeginOutlinerCard("st:" + sid, cardTitle, true, s,
                () => ResetOutlinerStorableCard(atom, sid, cardTitle));
            if (_outlinerCardReused)
            {
                return;
            }
            if (!open)
            {
                UI.AddLE(CreateOutlinerPlaceholder(card, s), preferredHeight: GalleryUiDesignTokens.HairGapRef * s);
                return;
            }
            JSONStorable st = null;
            try { st = OutlinerEdits.ResolveStorable(atom, sid); } catch { }
            List<OutlinerParamDescriptor> desc = OutlinerParamCatalog.FromStorable(st, sid);
            if (!OutlinerParamCatalog.HasEditableParams(desc))
            { return; }
            for (int d = 0; d < desc.Count; d++)
            {
                AddOutlinerParamRow(card, atom, desc[d], s);
            }
        }

        static string OutlinerStorableCardTitle(string sid)
        {
            if (string.Equals(sid, OutlinerParamCatalog.AtomStorableId, StringComparison.Ordinal))
                return VPBTranslation.T("outliner.card.atom", "Atom");
            if (string.Equals(sid, OutlinerPlugins.ManagerStorableId, StringComparison.OrdinalIgnoreCase))
                return VPBTranslation.T("outliner.card.plugins", "Plugins");
            return sid;
        }

        bool OutlinerStorableHasContent(Atom atom, string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            bool known;
            if (_outlinerStorableContentMemo.TryGetValue(sid, out known)) return known;
            bool has = false;
            if (!OutlinerParamCatalog.IsRedundantStorable(sid))
            {
                JSONStorable st = null;
                try { st = OutlinerEdits.ResolveStorable(atom, sid); } catch { }
                has = OutlinerParamCatalog.HasVisibleParams(st, sid);
            }
            _outlinerStorableContentMemo[sid] = has;
            return has;
        }

        bool OutlinerCardIsOpen(string id, bool defaultOpen)
        {
            return defaultOpen
                ? !_outlinerClosedCards.Contains(id)
                : _outlinerOpenCards.Contains(id);
        }

        void ToggleOutlinerCard(string id, bool defaultOpen)
        {
            if (defaultOpen)
            {
                if (!_outlinerClosedCards.Remove(id)) _outlinerClosedCards.Add(id);
            }
            else
            {
                if (!_outlinerOpenCards.Remove(id)) _outlinerOpenCards.Add(id);
            }
            RebuildOutlinerInspector();
        }

        GameObject BeginOutlinerCard(string id, string title, bool defaultOpen, float s,
            UnityEngine.Events.UnityAction reset = null, int sig = 0)
        {
            bool open = OutlinerCardIsOpen(id, defaultOpen);
            _outlinerCardReused = false;
            if (TryReuseOutlinerCard(id, open, title, sig)) return _outlinerCardLive[id].Go;
            GameObject card = new GameObject(OutlinerCardNamePrefix + id);
            card.transform.SetParent(_outlinerInspectorContent, false);
            UI.AddImage(card, GalleryUiColorTokens.SurfacePanel);
            UI.AddVLG(card, GalleryUiDesignTokens.TightGapRef * s,
                UI.Pad(GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.TightGapRef,
                    GalleryUiDesignTokens.TightGapRef, GalleryUiDesignTokens.TightGapRef, s));
            ContentSizeFitter csf = card.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            float h = OutlinerSlotH(s);
            float slot = OutlinerSlotH(s);
            GameObject hdr = UI.CreateUIButton(card, 0f, h, title,
                GalleryUiDesignTokens.FontBodyRef, 0f, 0f, AnchorPresets.hStretchTop,
                () => ToggleOutlinerCard(id, defaultOpen));
            UI.AddLE(hdr, preferredHeight: h, flexibleWidth: 1f);
            Text hdrTxt = hdr.GetComponentInChildren<Text>();
            if (hdrTxt != null)
            {
                hdrTxt.text = title;
                hdrTxt.alignment = TextAnchor.MiddleLeft;
                ApplyOutlinerScaledFont(hdrTxt, GalleryUiDesignTokens.FontBodyRef, s);
                ClipOutlinerText(hdrTxt);
                RectTransform trt = hdrTxt.rectTransform;
                trt.offsetMin = new Vector2(slot + GalleryUiDesignTokens.TightGapRef * s, 0f);
                trt.offsetMax = new Vector2(reset != null ? -slot : 0f, 0f);
            }
            ApplyOutlinerCardChevron(hdr, open, slot, s);
            if (reset != null) AddOutlinerCardResetButton(hdr, title, slot, s, reset);
            RecordOutlinerCard(id, card, open, title, sig);
            return card;
        }

        bool TryReuseOutlinerCard(string id, bool open, string title, int sig)
        {
            if (!OutlinerCardCacheable(id)) return false;
            if (_outlinerCardStale.Contains(id)) { return false; }
            OutlinerCardEntry hit;
            if (!_outlinerCardCache.TryGetValue(id, out hit))
            { return false; }
            if (hit == null || hit.Go == null) { return false; }
            if (hit.Open != open) { return false; }
            if (hit.Sig != sig) { return false; }
            if (!string.Equals(hit.Title, title, StringComparison.Ordinal))
            { return false; }
            _outlinerCardCache.Remove(id);
            _outlinerCardLive[id] = hit;
            _outlinerInspectorOrder.Add(hit.Go);
            _outlinerCardReused = true;
            return true;
        }

        void RecordOutlinerCard(string id, GameObject card, bool open, string title, int sig)
        {
            if (string.IsNullOrEmpty(id) || card == null) return;
            var entry = new OutlinerCardEntry();
            entry.Go = card;
            entry.Open = open;
            entry.Title = title;
            entry.Sig = sig;
            entry.HoldsThumbnails = !OutlinerCardIsThumbnailFree(id);
            _outlinerCardLive[id] = entry;
            _outlinerInspectorOrder.Add(card);
            _outlinerCardStale.Remove(id);
        }

        void AddOutlinerCardResetButton(GameObject hdr, string title, float slot, float s,
            UnityEngine.Events.UnityAction reset)
        {
            float icon = OutlinerBarIconSize(slot, s);
            GameObject btn = UI.CreateFloatChromeIconButton(hdr.transform, icon, "reset",
                GalleryUiColorTokens.ChromeIconWell, reset);
            if (btn == null) return;
            RectTransform rt = btn.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(icon, icon);
                rt.anchoredPosition = new Vector2(-OutlinerBarInset(s), 0f);
            }
            AddTooltipPlain(btn, VPBTranslation.T("outliner.card.reset_all", "Reset every control in ")
                + title
                + "\n" + VPBTranslation.T("outliner.card.reset_all_undo",
                    "Undo stays on the footer bar for 10 seconds."));
        }

        static void ApplyOutlinerCardChevron(GameObject hdr, bool open, float slot, float s)
        {
            if (hdr == null) return;
            Transform iconTr = hdr.transform.Find("CardChevron");
            Image img = iconTr != null ? iconTr.GetComponent<Image>() : null;
            if (img == null)
            {
                GameObject iconGO = UI.AddChildGOImage(hdr, Color.white, AnchorPresets.middleLeft, slot, slot, Vector2.zero, rounded: false);
                iconGO.name = "CardChevron";
                img = iconGO.GetComponent<Image>();
            }
            if (img == null) return;
            img.raycastTarget = false;
            float pad = GalleryUiDesignTokens.TreeRowExpandIconPadRef * s;
            float glyph = Mathf.Max(GalleryUiDesignTokens.Space3Ref * s, slot - pad * 2f);
            RectTransform irt = img.rectTransform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(glyph, glyph);
            irt.anchoredPosition = new Vector2(slot * 0.5f, 0f);
            string path = open ? "chevron-down" : "chevron-right";
            try
            {
                Sprite sp = UI.LoadIconSprite(path, GalleryUiColorTokens.TreeExpandIconTint);
                if (sp != null)
                {
                    UI.SetIconSprite(img, sp);
                    img.enabled = true;
                    return;
                }
            }
            catch { }
            img.enabled = false;
        }

        GameObject CreateOutlinerPlaceholder(GameObject card, float s)
        {
            GameObject go = new GameObject("Placeholder");
            go.transform.SetParent(card.transform, false);
            UI.AddLE(go, preferredHeight: GalleryUiDesignTokens.HairGapRef * s, flexibleWidth: 1f);
            return go;
        }
    }
}
