using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private const float TitleSearchChipRowHeightRef = 26f;
        private const float TitleSearchChipRowGapRef = 4f;
        private const float TitleSearchChipHostPadRef = 2f;

        private readonly List<TitleSearchChip> _titleSearchChips = new List<TitleSearchChip>(8);
        private readonly List<GameObject> _titleSearchChipButtons = new List<GameObject>(8);
        private readonly List<GameObject> _titleSearchChipPool = new List<GameObject>(8);
        private const int TitleSearchChipPoolMaxIdle = 24;

        private GameObject _titleSearchChipHostGO;
        private RectTransform _titleSearchChipHostRT;
        private GameObject _titleSearchChipRowsColGO;
        private GameObject _titleSearchChipClearAllGO;
        private RectTransform _titleSearchChipClearAllRT;
        private GameObject _titleSearchChipInclRowGO;
        private GameObject _titleSearchChipExclRowGO;
        private RectTransform _titleSearchChipInclContentRT;
        private RectTransform _titleSearchChipExclContentRT;
        private Text _titleSearchChipInclLabel;
        private Text _titleSearchChipExclLabel;
        private Text _titleSearchChipInclDropHint;
        private Text _titleSearchChipExclDropHint;
        private bool _titleSearchChipHostVisible;
        private int _titleSearchChipRowCount;
        /// <summary>Show Incl/Excl drop rows while a tag or chip drag is in progress.</summary>
        private bool _titleSearchChipDragReveal;
        /// <summary>True while a committed search chip is being dragged (skip full chip rebuild).</summary>
        private bool _titleSearchChipDragActive;

        private void CreateTitleSearchChipHost()
        {
            if (backgroundBoxGO == null || _titleSearchChipHostGO != null) return;

            _titleSearchChipHostGO = new GameObject("TitleSearchChipHost");
            _titleSearchChipHostGO.transform.SetParent(backgroundBoxGO.transform, false);
            _titleSearchChipHostRT = _titleSearchChipHostGO.AddComponent<RectTransform>();
            _titleSearchChipHostRT.anchorMin = new Vector2(0f, 1f);
            _titleSearchChipHostRT.anchorMax = new Vector2(1f, 1f);
            _titleSearchChipHostRT.pivot = new Vector2(0.5f, 1f);
            UI.AddImage(_titleSearchChipHostGO, new Color(0f, 0f, 0f, 0f), false);

            // [Clear-all X] | [Include / Exclude rows] — one square clear for both rows.
            // childForceExpandHeight false: keep clear square (not stretched to both rows).
            UI.AddHLG(
                _titleSearchChipHostGO,
                spacing: 6f,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: false);

            float clearSz = TitleSearchChipRowHeightRef;
            _titleSearchChipClearAllGO = UI.CreateChromeLayoutButton(
                _titleSearchChipHostGO.transform,
                clearSz,
                clearSz,
                "X",
                18,
                new Color(0.35f, 0.18f, 0.18f, 0.92f),
                () =>
                {
                    try { ClearTitleBarSearch(); } catch { }
                });
            _titleSearchChipClearAllGO.name = "TitleSearchChipClearAll";
            _titleSearchChipClearAllRT = _titleSearchChipClearAllGO.GetComponent<RectTransform>();
            {
                Sprite xSpr = UI.LoadIconSprite("vpb_icons/x.png", Color.white);
                if (xSpr != null)
                    UI.AddIconToButton(_titleSearchChipClearAllGO, xSpr, 5f, new Color(0.35f, 0.18f, 0.18f, 0.92f));
            }
            var clearHb = _titleSearchChipClearAllGO.GetComponent<UIHoverBorder>();
            if (clearHb != null)
            {
                clearHb.inward = true;
                clearHb.hoverColor = new Color(1f, 0.25f, 0.25f, 1f);
                clearHb.borderSize = 2f;
                try { clearHb.ApplyBorderSettings(); } catch { }
            }
            try { AddTooltip(_titleSearchChipClearAllGO, "gallery.search.clear_all_tip", "Clear all search. Ctrl+Z undoes."); } catch { }

            _titleSearchChipRowsColGO = new GameObject("TitleSearchChipRows");
            _titleSearchChipRowsColGO.transform.SetParent(_titleSearchChipHostGO.transform, false);
            _titleSearchChipRowsColGO.AddComponent<RectTransform>();
            UI.AddLE(_titleSearchChipRowsColGO, flexibleWidth: 1f, minWidth: 0f, preferredHeight: TitleSearchChipRowHeightRef * 2f + TitleSearchChipRowGapRef);
            UI.AddVLG(
                _titleSearchChipRowsColGO,
                spacing: TitleSearchChipRowGapRef,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.UpperLeft,
                childForceExpandWidth: true,
                childForceExpandHeight: false);

            BuildTitleSearchChipRow(
                _titleSearchChipRowsColGO,
                "Incl",
                VPBTranslation.T("gallery.search.chip_incl", "Include"),
                UserTagFilterActiveColor,
                TitleSearchChipPolarity.Include,
                out _titleSearchChipInclRowGO,
                out _titleSearchChipInclContentRT,
                out _titleSearchChipInclLabel,
                out _titleSearchChipInclDropHint);

            BuildTitleSearchChipRow(
                _titleSearchChipRowsColGO,
                "Excl",
                VPBTranslation.T("gallery.search.chip_excl", "Exclude"),
                UserTagFilterExcludedColor,
                TitleSearchChipPolarity.Exclude,
                out _titleSearchChipExclRowGO,
                out _titleSearchChipExclContentRT,
                out _titleSearchChipExclLabel,
                out _titleSearchChipExclDropHint);

            _titleSearchChipHostGO.SetActive(false);
            _titleSearchChipHostVisible = false;
            _titleSearchChipRowCount = 0;
        }

        private void BuildTitleSearchChipRow(
            GameObject parent,
            string name,
            string labelText,
            Color labelColor,
            TitleSearchChipPolarity polarity,
            out GameObject rowGO,
            out RectTransform contentRT,
            out Text label,
            out Text dropHint)
        {
            rowGO = new GameObject("TitleSearchChipRow_" + name);
            rowGO.transform.SetParent(parent.transform, false);
            RectTransform rowRT = rowGO.AddComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0f, 1f);
            UI.AddLE(rowGO, minHeight: TitleSearchChipRowHeightRef, preferredHeight: TitleSearchChipRowHeightRef, flexibleWidth: 1f);

            UI.AddHLG(
                rowGO,
                spacing: 8f,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: true);

            label = UI.CreateLabel(rowGO, labelText, GalleryUiDesignTokens.FontBodyRef, labelColor, TextAnchor.MiddleLeft, raycastTarget: false, name: "RowLabel");
            // CreateLabel defaults to stretch — pin left and size to text so "Include"/"Exclude" fully show.
            RectTransform labelRT = label.rectTransform;
            labelRT.anchorMin = new Vector2(0f, 0.5f);
            labelRT.anchorMax = new Vector2(0f, 0.5f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.sizeDelta = new Vector2(0f, TitleSearchChipRowHeightRef);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            ContentSizeFitter labelCsf = label.gameObject.AddComponent<ContentSizeFitter>();
            labelCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            labelCsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            UI.AddLE(label.gameObject, minWidth: 72f, preferredHeight: TitleSearchChipRowHeightRef, flexibleWidth: 0f, flexibleHeight: 0f);

            // Horizontal scroll viewport — many chips do not clip / thrash row height.
            GameObject viewportGO = new GameObject("ChipsViewport");
            viewportGO.transform.SetParent(rowGO.transform, false);
            RectTransform viewportRT = viewportGO.AddComponent<RectTransform>();
            UI.AddLE(viewportGO, flexibleWidth: 1f, preferredHeight: TitleSearchChipRowHeightRef, minHeight: TitleSearchChipRowHeightRef, minWidth: 0f);
            Image vpImg = UI.AddImage(viewportGO, new Color(1f, 1f, 1f, 0.01f));
            vpImg.raycastTarget = true;
            viewportGO.AddComponent<RectMask2D>();
            ScrollRect scroll = viewportGO.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = VpbScrollTuning.Sensitivity(28f, 1f);
            scroll.viewport = viewportRT;

            GameObject contentGO = new GameObject("Chips");
            contentGO.transform.SetParent(viewportGO.transform, false);
            contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 0.5f);
            contentRT.anchorMax = new Vector2(0f, 0.5f);
            contentRT.pivot = new Vector2(0f, 0.5f);
            contentRT.sizeDelta = new Vector2(0f, TitleSearchChipRowHeightRef);
            ContentSizeFitter contentCsf = contentGO.AddComponent<ContentSizeFitter>();
            contentCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Left-pack chips — no center alignment.
            UI.AddHLG(
                contentGO,
                spacing: 4f,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: true);
            scroll.content = contentRT;

            dropHint = UI.CreateLabel(
                contentGO,
                polarity == TitleSearchChipPolarity.Exclude
                    ? VPBTranslation.T("gallery.search.chip_excl_hint", "Drop chip here to exclude")
                    : VPBTranslation.T("gallery.search.chip_drop_hint", "Drop tags here"),
                GalleryUiDesignTokens.FontBodyRef,
                new Color(labelColor.r, labelColor.g, labelColor.b, 0.65f),
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "DropHint");
            dropHint.fontStyle = FontStyle.Italic;
            LayoutElement hintLE = UI.AddLE(dropHint.gameObject, preferredHeight: TitleSearchChipRowHeightRef, flexibleWidth: 0f);
            hintLE.ignoreLayout = false;
            dropHint.gameObject.SetActive(false);

            // Full-row drop overlay (raycast only while title-search drag active).
            GameObject dropGO = new GameObject("DropOverlay");
            dropGO.transform.SetParent(rowGO.transform, false);
            RectTransform dropRT = dropGO.AddComponent<RectTransform>();
            dropRT.anchorMin = Vector2.zero;
            dropRT.anchorMax = Vector2.one;
            dropRT.offsetMin = Vector2.zero;
            dropRT.offsetMax = Vector2.zero;
            LayoutElement dropLE = dropGO.AddComponent<LayoutElement>();
            dropLE.ignoreLayout = true;
            Image dropImg = UI.AddImage(dropGO, new Color(labelColor.r, labelColor.g, labelColor.b, 0f));
            dropImg.raycastTarget = true;
            var gate = dropGO.AddComponent<TitleSearchDropRaycastGate>();
            gate.Image = dropImg;
            gate.TargetPolarity = polarity;
            gate.HoverColor = new Color(labelColor.r, labelColor.g, labelColor.b, 0.18f);
            gate.RejectColor = new Color(0.85f, 0.22f, 0.22f, 0.28f);
            TitleSearchChipDropZone dz = dropGO.AddComponent<TitleSearchChipDropZone>();
            dz.Panel = this;
            dz.TargetPolarity = polarity;
            dropGO.transform.SetAsLastSibling();
        }

        private bool HasTitleSearchChips()
        {
            return _titleSearchChips != null && _titleSearchChips.Count > 0;
        }

        /// <summary>Field text shown in title search while browse mode (draft empty when chips committed).</summary>
        private string GetTitleSearchBrowseFieldText()
        {
            if (HasTitleSearchChips()) return "";
            return nameFilter ?? "";
        }

        private void ClearTitleSearchChipsState()
        {
            GalleryTitleSearchChipUtil.Clear(_titleSearchChips);
            _titleSearchChipDragReveal = false;
            _titleSearchChipDragActive = false;
        }

        private void HydrateTitleSearchChipsFromCurrentFilter()
        {
            GalleryTitleSearchChipUtil.HydrateFromQuery(nameFilterQuery ?? GallerySearchQuery.Empty, _titleSearchChips);
        }

        /// <summary>Ctrl+Backspace on empty draft — remove last committed chip (plain Backspace does not).</summary>
        internal bool TitleSearchTryPopLastChip()
        {
            if (!HasTitleSearchChips()) return false;
            RemoveTitleSearchChipAt(_titleSearchChips.Count - 1);
            return true;
        }

        /// <summary>Minus on empty draft — toggle polarity of last chip.</summary>
        internal bool TitleSearchTryToggleLastChipPolarity()
        {
            if (!HasTitleSearchChips()) return false;
            ToggleTitleSearchChipPolarityAt(_titleSearchChips.Count - 1);
            return true;
        }

        private void WireTitleSearchCommitKeys(InputField field)
        {
            if (field == null) return;
            TitleSearchCommitKeys keys = field.GetComponent<TitleSearchCommitKeys>();
            if (keys == null) keys = field.gameObject.AddComponent<TitleSearchCommitKeys>();
            keys.Panel = this;
            keys.Field = field;
        }

        /// <summary>Enter: commit draft into Include chips. Shift+Enter: commit into Exclude.</summary>
        internal void TitleSearchOnCommitDraft(InputField sourceField, bool forceExclude = false)
        {
            if (sourceField == null) return;

            string draft = sourceField.text ?? "";
            string trimmed = draft.Trim();
            if (trimmed.Length == 0 && !HasTitleSearchChips()) return;

            if (trimmed.Length > 0)
            {
                GallerySearchQuery parsed = GallerySearchQuery.Parse(trimmed);
                GalleryTitleSearchChipUtil.AppendFromQuery(parsed, _titleSearchChips, forceExclude);
            }

            ApplyTitleSearchChipsAndClearDraft(sourceField);
        }

        private void ApplyTitleSearchChipsAndClearDraft(InputField sourceField)
        {
            string serialized = GalleryTitleSearchChipUtil.Serialize(_titleSearchChips);
            try { SetTitleSearchDraftText("", sourceField); } catch { }
            try { RebuildTitleSearchChipUi(); } catch { }

            if (!string.Equals(serialized, nameFilter ?? "", StringComparison.Ordinal))
                SetNameFilter(serialized);
            else
            {
                try { SyncBrowseFilterChipChrome(); } catch { }
            }

            if (sourceField != null)
            {
                try
                {
                    sourceField.ActivateInputField();
                    sourceField.MoveTextEnd(false);
                }
                catch { }
            }
        }

        private void SetTitleSearchDraftText(string text, InputField preferField)
        {
            string t = text ?? "";
            if (preferField != null)
                SetTitleSearchInputTextWithoutNotify(preferField, t, _titleBarSearchOnValueChanged);
            if (titleSearchInput != null && titleSearchInput != preferField)
                SetTitleSearchInputTextWithoutNotify(titleSearchInput, t, _titleBarSearchOnValueChanged);
            if (_titleSearchPopupField != null && _titleSearchPopupField != preferField)
            {
                try
                {
                    _suppressTitleBarSearchValueChanged = true;
                    _titleSearchPopupField.text = t;
                }
                finally { _suppressTitleBarSearchValueChanged = false; }
            }
        }

        private void OnTitleSearchClearClicked()
        {
            // Field X already wiped draft text; only soft-undo when chips / committed filter existed.
            bool canUndo = HasTitleSearchChips() || !string.IsNullOrEmpty(nameFilter);
            if (!canUndo) return;

            CaptureTitleSearchClearUndo();
            ClearTitleSearchChipsState();
            if (!string.IsNullOrEmpty(nameFilter))
                SetNameFilter("");
            else
            {
                try { RebuildTitleSearchChipUi(); } catch { }
                try { SyncBrowseFilterChipChrome(); } catch { }
            }
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.search.cleared_with_undo", "Search cleared. Ctrl+Z undoes."),
                    2.5f);
            }
            catch { }
        }

        private void CaptureTitleSearchClearUndo()
        {
            string snap = null;
            if (HasTitleSearchChips())
                snap = GalleryTitleSearchChipUtil.Serialize(_titleSearchChips);
            if (string.IsNullOrEmpty(snap))
                snap = nameFilter ?? "";
            if (string.IsNullOrEmpty(snap))
            {
                return;
            }

            string snapCopy = snap;
            PushUndo(() =>
            {
                try { SetNameFilter(snapCopy); } catch { }
                try { HydrateTitleSearchChipsFromCurrentFilter(); } catch { }
                try { SetTitleSearchDraftText("", null); } catch { }
                try { RebuildTitleSearchChipUi(); } catch { }
                try { SyncBrowseFilterChipChrome(); } catch { }
                try { SyncTitleBarSearchBackdrop(); } catch { }
                try
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.search.undo_clear_ok", "Restored previous search."),
                        2f);
                }
                catch { }
            }, VPBTranslation.T("gallery.undo.search_clear", "Search clear"));
        }

        /// <summary>ESC: clear draft + close popup / unfocus — keep committed chips.</summary>
        private void TitleSearchOnEscape()
        {
            try { SetTitleSearchDraftText("", null); } catch { }
            try { CloseTitleSearchPopup(); } catch { }
            try
            {
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            }
            catch { }
            try { SyncTitleBarSearchBackdrop(); } catch { }
        }

        private void ApplySerializedTitleSearchChips()
        {
            if (_titleSearchChips == null || _titleSearchChips.Count == 0)
            {
                _titleSearchChipDragReveal = false;
                _titleSearchChipDragActive = false;
            }
            // Exact vocab #tag / -#tag → filter sets (source of truth); leave substring tags in title.
            bool filterSetsChanged = false;
            try { filterSetsChanged = BridgeExactTitleTagChipsIntoFilterSets(); } catch { }
            string serialized = GalleryTitleSearchChipUtil.Serialize(_titleSearchChips);
            try { RebuildTitleSearchChipUi(); } catch { }
            if (!string.Equals(serialized, nameFilter ?? "", StringComparison.Ordinal))
                SetNameFilter(serialized);
            else
            {
                try { SyncBrowseFilterChipChrome(); } catch { }
            }
            if (filterSetsChanged)
            {
                try { RefreshFiles(true, false, false, "title_tag_bridge_filter"); } catch { }
                try { SyncBrowseFilterChipChrome(); } catch { }
                try { RefreshUserTagsAvailPaneInPlace(true); } catch { }
                try { RefreshUserTagsAvailPaneInPlace(false); } catch { }
            }
        }

        private void RemoveTitleSearchChipAt(int index)
        {
            if (_titleSearchChips == null || index < 0 || index >= _titleSearchChips.Count) return;
            _titleSearchChips.RemoveAt(index);
            ApplySerializedTitleSearchChips();
        }

        private void ToggleTitleSearchChipPolarityAt(int index)
        {
            if (!GalleryTitleSearchChipUtil.TogglePolarity(_titleSearchChips, index))
            {
                try
                {
                    SetStatus(VPBTranslation.T(
                        "gallery.search.chip_excl_unsupported",
                        "Exclude works for tags / bare terms (not @creator or status)."));
                }
                catch { }
                return;
            }
            ApplySerializedTitleSearchChips();
        }

        private void SetTitleSearchChipPolarityAt(int index, TitleSearchChipPolarity polarity)
        {
            if (_titleSearchChips == null || index < 0 || index >= _titleSearchChips.Count) return;
            TitleSearchChip before = _titleSearchChips[index];
            if (before.Polarity == polarity && !(polarity == TitleSearchChipPolarity.Exclude && before.Kind == TitleSearchChipKind.Broad))
                return;
            if (!GalleryTitleSearchChipUtil.SetPolarity(_titleSearchChips, index, polarity))
            {
                try
                {
                    SetStatus(VPBTranslation.T(
                        "gallery.search.chip_excl_unsupported",
                        "Exclude works for tags / bare terms (not @creator or status)."));
                }
                catch { }
                return;
            }
            ApplySerializedTitleSearchChips();
        }

        /// <summary>Side / detail tag drag began — reveal Incl/Excl drop rows.</summary>
        internal void TitleSearchOnExternalTagDragBegan()
        {
            if (cleanupModeActive) return;
            if (!IsBrowseFilterChipContextActive()) return;
            _titleSearchChipDragReveal = true;
            try { CreateTitleSearchChipHost(); } catch { }
            try { RebuildTitleSearchChipUi(); } catch { }
            try { SyncBrowseFilterChipChrome(); } catch { }
            try { UpdateLayout(); } catch { }
        }

        /// <summary>Committed search chip drag began — keep both rows visible without destroying chips.</summary>
        internal void TitleSearchOnChipDragBegan(int chipIndex)
        {
            if (cleanupModeActive) return;
            _titleSearchChipDragReveal = true;
            _titleSearchChipDragActive = true;
            UserTagDragSession.PendingTitleSearchChipIndex = chipIndex;
            UserTagDragSession.PendingTitleSearchChipPanel = this;
            bool canExcl = true;
            if (_titleSearchChips != null && chipIndex >= 0 && chipIndex < _titleSearchChips.Count)
                canExcl = _titleSearchChips[chipIndex].CanExclude;
            UserTagDragSession.PendingTitleSearchChipCanExclude = canExcl;
            EnsureTitleSearchDragRevealRowsVisible();
            try { SyncBrowseFilterChipChrome(); } catch { }
            try { UpdateLayout(); } catch { }
        }

        internal void TitleSearchNotifyExcludeRejected()
        {
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.search.chip_excl_unsupported",
                        "Exclude works for tags / bare terms (not @creator or status)."),
                    2.2f);
            }
            catch { }
        }

        internal void TitleSearchOnDragEnded()
        {
            bool wasReveal = _titleSearchChipDragReveal || _titleSearchChipDragActive;
            _titleSearchChipDragReveal = false;
            _titleSearchChipDragActive = false;
            if (UserTagDragSession.PendingTitleSearchChipPanel == this)
            {
                UserTagDragSession.PendingTitleSearchChipIndex = -1;
                UserTagDragSession.PendingTitleSearchChipPanel = null;
                UserTagDragSession.PendingTitleSearchChipCanExclude = false;
            }
            if (!wasReveal) return;
            try { RebuildTitleSearchChipUi(); } catch { }
            try { SyncBrowseFilterChipChrome(); } catch { }
            try { UpdateLayout(); } catch { }
        }

        /// <summary>Drop Available/detail tags onto Incl or Excl search row (search filter only).</summary>
        internal void TitleSearchAcceptDroppedTags(List<string> tags, TitleSearchChipPolarity polarity)
        {
            if (tags == null || tags.Count == 0) return;

            bool changed = false;
            for (int i = 0; i < tags.Count; i++)
            {
                string t = tags[i];
                if (string.IsNullOrEmpty(t)) continue;
                string n = VpbLocalDatabase.NormalizeGalleryUserTagName(t);
                if (string.IsNullOrEmpty(n)) n = t.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(n)) continue;

                if (GalleryTitleSearchChipUtil.TryAdd(
                    _titleSearchChips,
                    TitleSearchChipKind.Tag,
                    polarity,
                    n,
                    0))
                    changed = true;
            }

            if (!changed) return;
            ApplySerializedTitleSearchChips();
            try { SetStatus(VPBTranslation.T("gallery.search.chip_drop_ok", "Added tag to search.")); } catch { }
        }

        internal void TitleSearchMoveChipToPolarity(int chipIndex, TitleSearchChipPolarity polarity)
        {
            SetTitleSearchChipPolarityAt(chipIndex, polarity);
        }

        private void EnsureTitleSearchDragRevealRowsVisible()
        {
            if (_titleSearchChipHostGO == null) return;
            _titleSearchChipHostVisible = true;
            _titleSearchChipHostGO.SetActive(true);
            if (_titleSearchChipInclRowGO != null) _titleSearchChipInclRowGO.SetActive(true);
            if (_titleSearchChipExclRowGO != null) _titleSearchChipExclRowGO.SetActive(true);
            _titleSearchChipRowCount = 2;
            UpdateTitleSearchDropHints(
                CountTitleSearchChipsWithPolarity(TitleSearchChipPolarity.Include) > 0,
                CountTitleSearchChipsWithPolarity(TitleSearchChipPolarity.Exclude) > 0,
                dragReveal: true);
            try
            {
                ApplyTitleSearchChipHostLayout(_lastBrowseGridLeftInset, _lastBrowseGridRightInset, ChromeScale);
            }
            catch { }
        }

        private int CountTitleSearchChipsWithPolarity(TitleSearchChipPolarity polarity)
        {
            int n = 0;
            if (_titleSearchChips == null) return 0;
            for (int i = 0; i < _titleSearchChips.Count; i++)
            {
                if (_titleSearchChips[i].Polarity == polarity) n++;
            }
            return n;
        }

        private void UpdateTitleSearchDropHints(bool hasInclChips, bool hasExclChips, bool dragReveal)
        {
            // Incl hint only while dragging into an empty include row.
            if (_titleSearchChipInclDropHint != null)
                _titleSearchChipInclDropHint.gameObject.SetActive(dragReveal && !hasInclChips);
            // Excl hint always when chips exist but none excluded — discoverability.
            if (_titleSearchChipExclDropHint != null)
            {
                bool showExclHint = !hasExclChips && (HasTitleSearchChips() || dragReveal);
                _titleSearchChipExclDropHint.gameObject.SetActive(showExclHint);
            }
        }

        private void ApplyTitleSearchRowLabelFonts(float s)
        {
            if (s <= 0f) s = 1f;
            try
            {
                if (_titleSearchChipInclLabel != null)
                    GalleryUiMetrics.ApplyFont(_titleSearchChipInclLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                if (_titleSearchChipExclLabel != null)
                    GalleryUiMetrics.ApplyFont(_titleSearchChipExclLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                if (_titleSearchChipInclDropHint != null)
                    GalleryUiMetrics.ApplyFont(_titleSearchChipInclDropHint, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                if (_titleSearchChipExclDropHint != null)
                    GalleryUiMetrics.ApplyFont(_titleSearchChipExclDropHint, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            catch { }
        }

        private void HideTitleSearchChipHostFully()
        {
            ClearTitleSearchChipButtons();
            _titleSearchChipHostVisible = false;
            _titleSearchChipRowCount = 0;
            if (_titleSearchChipHostGO != null) _titleSearchChipHostGO.SetActive(false);
            if (_titleSearchChipInclRowGO != null) _titleSearchChipInclRowGO.SetActive(false);
            if (_titleSearchChipExclRowGO != null) _titleSearchChipExclRowGO.SetActive(false);
            UpdateTitleSearchDropHints(false, false, false);
        }

        private void RebuildTitleSearchChipUi()
        {
            if (_titleSearchChipHostGO == null) return;

            // Don't destroy chips mid chip-drag (source GameObject must stay alive).
            if (_titleSearchChipDragActive)
            {
                EnsureTitleSearchDragRevealRowsVisible();
                return;
            }

            ClearTitleSearchChipButtons();

            bool dragReveal = _titleSearchChipDragReveal;
            bool show = (HasTitleSearchChips() || dragReveal)
                && IsVisible
                && !isCollapsed
                && !settingsListViewActive
                && !cleanupModeActive
                && IsBrowseFilterChipContextActive();

            bool wasVisible = _titleSearchChipHostVisible;
            int wasRows = _titleSearchChipRowCount;

            if (!show)
            {
                HideTitleSearchChipHostFully();
                if (wasVisible || wasRows > 0)
                {
                    try { UpdateLayout(); } catch { }
                }
                return;
            }

            _titleSearchChipHostVisible = true;
            _titleSearchChipHostGO.SetActive(true);

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            ApplyTitleSearchRowLabelFonts(s);
            int fontSize = UiMetrics.FontBody();
            float chipH = TitleSearchChipRowHeightRef * s;

            bool hasIncl = false;
            bool hasExcl = false;
            for (int i = 0; i < _titleSearchChips.Count; i++)
            {
                TitleSearchChip chip = _titleSearchChips[i];
                bool excl = chip.Polarity == TitleSearchChipPolarity.Exclude;
                if (excl) hasExcl = true;
                else hasIncl = true;

                RectTransform parent = excl ? _titleSearchChipExclContentRT : _titleSearchChipInclContentRT;
                if (parent == null) continue;
                GameObject go = AcquireTitleSearchChipControl(parent, i, chip, chipH, fontSize, s);
                if (go != null) _titleSearchChipButtons.Add(go);
            }

            // Always show both rows when any chips (or drag reveal) — Exclude must stay discoverable.
            if (_titleSearchChipInclRowGO != null) _titleSearchChipInclRowGO.SetActive(true);
            if (_titleSearchChipExclRowGO != null) _titleSearchChipExclRowGO.SetActive(true);
            UpdateTitleSearchDropHints(hasIncl, hasExcl, dragReveal);

            _titleSearchChipRowCount = 2;

            try
            {
                if (_titleSearchChipInclContentRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_titleSearchChipInclContentRT);
                if (_titleSearchChipExclContentRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_titleSearchChipExclContentRT);
            }
            catch { }

            if (!wasVisible || wasRows != _titleSearchChipRowCount)
            {
                try { UpdateLayout(); } catch { }
            }
        }

        private void ClearTitleSearchChipButtons()
        {
            for (int i = 0; i < _titleSearchChipButtons.Count; i++)
            {
                GameObject go = _titleSearchChipButtons[i];
                if (go != null)
                {
                    try { ReturnTitleSearchChipToPool(go); } catch { }
                }
            }
            _titleSearchChipButtons.Clear();
        }

        private void ReturnTitleSearchChipToPool(GameObject go)
        {
            if (go == null) return;

            Button dismissBtn = null;
            Transform dismissT = go.transform.Find("Dismiss");
            if (dismissT != null) dismissBtn = dismissT.GetComponent<Button>();
            if (dismissBtn != null) dismissBtn.onClick.RemoveAllListeners();
            if (dismissT != null)
            {
                UIChipDismissClick dismissClick = dismissT.GetComponent<UIChipDismissClick>();
                if (dismissClick != null) dismissClick.OnDismiss = null;
            }

            if (_titleSearchChipPool.Count >= TitleSearchChipPoolMaxIdle)
            {
                try { Destroy(go); } catch { }
                return;
            }

            go.SetActive(false);
            if (_titleSearchChipInclContentRT != null)
                go.transform.SetParent(_titleSearchChipInclContentRT, false);
            _titleSearchChipPool.Add(go);
        }

        private GameObject AcquireTitleSearchChipControl(
            Transform parent,
            int index,
            TitleSearchChip chip,
            float chipH,
            int fontSize,
            float s)
        {
            if (parent == null) return null;
            if (s <= 0f) s = 1f;

            GameObject go = null;
            while (_titleSearchChipPool.Count > 0 && go == null)
            {
                int last = _titleSearchChipPool.Count - 1;
                go = _titleSearchChipPool[last];
                _titleSearchChipPool.RemoveAt(last);
            }

            if (go == null)
                go = CreateTitleSearchChipControlScaffold(parent, chipH, fontSize, s);
            else
            {
                go.transform.SetParent(parent, false);
                go.SetActive(true);
            }

            BindTitleSearchChipControl(go, index, chip, chipH, fontSize, s);
            return go;
        }

        private GameObject CreateTitleSearchChipControlScaffold(
            Transform parent,
            float chipH,
            int fontSize,
            float s)
        {
            GameObject go = new GameObject("TitleSearchChip");
            go.transform.SetParent(parent, false);
            AddFilterChipRoundedBg(go, Color.white);

            int padLeft = Mathf.RoundToInt(6f * s);
            int padV = Mathf.Max(1, Mathf.RoundToInt(1f * s));
            float innerH = Mathf.Max(14f, chipH - padV * 2f);

            UI.AddHLG(go,
                spacing: GalleryUiDesignTokens.FilterChipLabelDismissGapRef * s,
                padding: new RectOffset(padLeft, 0, padV, padV),
                childAlignment: TextAnchor.MiddleLeft,
                childForceExpandWidth: false,
                childForceExpandHeight: true);

            UI.AddLE(go, minHeight: chipH, preferredHeight: chipH);
            ContentSizeFitter csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TitleSearchChipDragSource drag = go.AddComponent<TitleSearchChipDragSource>();
            drag.Panel = this;

            Text labelTxt = UI.CreateLabel(go, "", fontSize, Color.white, TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, raycastTarget: false, name: "Label");
            ContentSizeFitter labelCsf = labelTxt.gameObject.AddComponent<ContentSizeFitter>();
            labelCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            UI.AddLE(labelTxt.gameObject, preferredHeight: innerH, flexibleHeight: 0f);

            float dismissSize = innerH;
            GameObject dismissGO = new GameObject("Dismiss");
            dismissGO.transform.SetParent(go.transform, false);
            AddFilterChipRoundedBg(dismissGO, Color.gray);
            // Button kept for hover/target graphic; click path is UIChipDismissClick
            // (owns drag so parent TitleSearchChipDragSource / ScrollRect cannot cancel it).
            Button dismissBtn = dismissGO.AddComponent<Button>();
            dismissBtn.targetGraphic = dismissGO.GetComponent<Image>();
            UI.NeutralizeSelectableColorTint(dismissBtn);
            dismissGO.AddComponent<UIChipDismissClick>();

            float iconPad = GalleryUiDesignTokens.SearchIconButtonPadRef * s;
            Sprite closeSpr = UI.LoadIconSprite("vpb_icons/x.png", Color.white);
            if (closeSpr != null)
                UI.AddIconToButton(dismissGO, closeSpr, iconPad, Color.gray);
            else
            {
                Text xTxt = UI.CreateLabel(dismissGO, "\u00d7", (int)GalleryUiDesignTokens.FilterChipDismissSizeRef, Color.white, TextAnchor.MiddleCenter, raycastTarget: false, name: "X");
                GalleryUiMetrics.ApplyGlyphFont(xTxt, GalleryUiDesignTokens.FilterChipDismissSizeRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            UI.AddLE(dismissGO, minWidth: dismissSize, minHeight: dismissSize, preferredWidth: dismissSize, preferredHeight: dismissSize, flexibleHeight: 0f);

            // Inward rim — chips live under RectMask2D scroll; outward hover clips (Import sidebar same).
            go.AddComponent<UIHoverBorder>();
            try
            {
                var hb = go.GetComponent<UIHoverBorder>();
                if (hb != null)
                {
                    hb.inward = true;
                    hb.borderSize = 2f;
                    hb.ApplyBorderSettings();
                }
            }
            catch { }

            return go;
        }

        private void BindTitleSearchChipControl(
            GameObject go,
            int index,
            TitleSearchChip chip,
            float chipH,
            int fontSize,
            float s)
        {
            if (go == null) return;
            if (s <= 0f) s = 1f;

            bool excl = chip.Polarity == TitleSearchChipPolarity.Exclude;
            Color accent = excl ? UserTagFilterExcludedColor : UserTagFilterActiveColor;

            Image bg = go.GetComponent<Image>();
            if (bg != null)
                bg.color = new Color(accent.r, accent.g, accent.b, 0.96f);

            int padLeft = Mathf.RoundToInt(6f * s);
            int padV = Mathf.Max(1, Mathf.RoundToInt(1f * s));
            float innerH = Mathf.Max(14f, chipH - padV * 2f);

            HorizontalLayoutGroup hlg = go.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.spacing = GalleryUiDesignTokens.FilterChipLabelDismissGapRef * s;
                hlg.padding = new RectOffset(padLeft, 0, padV, padV);
            }

            LayoutElement goLE = go.GetComponent<LayoutElement>();
            if (goLE != null)
            {
                goLE.minHeight = chipH;
                goLE.preferredHeight = chipH;
            }

            TitleSearchChipDragSource drag = go.GetComponent<TitleSearchChipDragSource>();
            if (drag != null)
            {
                drag.Panel = this;
                drag.ChipIndex = index;
                drag.CanMoveToExclude = chip.CanExclude;
            }

            Transform labelT = go.transform.Find("Label");
            Text labelTxt = labelT != null ? labelT.GetComponent<Text>() : null;
            if (labelTxt != null)
            {
                labelTxt.text = chip.ToDisplayLabel();
                labelTxt.fontSize = fontSize;
                LayoutElement labelLE = labelTxt.GetComponent<LayoutElement>();
                if (labelLE != null) labelLE.preferredHeight = innerH;
            }

            Transform dismissT = go.transform.Find("Dismiss");
            if (dismissT != null)
            {
                float dismissSize = innerH;
                Image dismissBg = dismissT.GetComponent<Image>();
                if (dismissBg != null) dismissBg.color = FilterChipDismissBackdrop(accent);

                LayoutElement dismissLE = dismissT.GetComponent<LayoutElement>();
                if (dismissLE != null)
                {
                    dismissLE.minWidth = dismissSize;
                    dismissLE.minHeight = dismissSize;
                    dismissLE.preferredWidth = dismissSize;
                    dismissLE.preferredHeight = dismissSize;
                }

                Button dismissBtn = dismissT.GetComponent<Button>();
                if (dismissBtn != null)
                    dismissBtn.onClick.RemoveAllListeners();

                UIChipDismissClick dismissClick = dismissT.GetComponent<UIChipDismissClick>();
                if (dismissClick == null)
                    dismissClick = dismissT.gameObject.AddComponent<UIChipDismissClick>();
                int captured = index;
                dismissClick.OnDismiss = () =>
                {
                    try { RemoveTitleSearchChipAt(captured); } catch { }
                };
            }

            try { AddTooltip(dismissT != null ? dismissT.gameObject : go, "gallery.filter_chip.remove_tip", "Remove this filter"); } catch { }
            try
            {
                AddTooltip(go, "gallery.search.chip_drag_tip",
                    chip.CanExclude
                        ? "Drag to Include or Exclude row"
                        : "Drag to Include row");
            }
            catch { }
        }

        private float TitleSearchChipChromeTopInsetPx(float paneScale)
        {
            if (!_titleSearchChipHostVisible || _titleSearchChipRowCount < 1) return 0f;
            float s = paneScale <= 0f ? 1f : paneScale;
            int rows = _titleSearchChipRowCount;
            float rowsH = rows * TitleSearchChipRowHeightRef + (rows - 1) * TitleSearchChipRowGapRef;
            return (rowsH + TitleSearchChipHostPadRef * 2f) * s;
        }

        private void ApplyTitleSearchChipHostLayout(float leftOffset, float rightOffset, float paneScale)
        {
            if (_titleSearchChipHostGO == null || _titleSearchChipHostRT == null || !_titleSearchChipHostVisible)
                return;

            float s = paneScale <= 0f ? 1f : paneScale;
            ApplyTitleSearchRowLabelFonts(s);
            float titleBottom = -GalleryUiDesignTokens.SideTabTopOffsetRef * s;
            float hostH = TitleSearchChipChromeTopInsetPx(s);
            float pad = FilterChipHorizontalPaddingRef * s;
            float margin = TitleSearchChipHostPadRef * s;

            _titleSearchChipHostRT.anchorMin = new Vector2(0f, 1f);
            _titleSearchChipHostRT.anchorMax = new Vector2(1f, 1f);
            _titleSearchChipHostRT.pivot = new Vector2(0.5f, 1f);
            _titleSearchChipHostRT.offsetMin = new Vector2(leftOffset + pad, titleBottom - hostH + margin);
            _titleSearchChipHostRT.offsetMax = new Vector2(rightOffset - pad, titleBottom - margin);

            if (_titleSearchChipClearAllRT != null)
            {
                float clearSz = TitleSearchChipRowHeightRef * s;
                _titleSearchChipClearAllRT.sizeDelta = new Vector2(clearSz, clearSz);
                LayoutElement clearLe = _titleSearchChipClearAllGO != null
                    ? _titleSearchChipClearAllGO.GetComponent<LayoutElement>()
                    : null;
                if (clearLe != null)
                {
                    clearLe.minWidth = clearSz;
                    clearLe.minHeight = clearSz;
                    clearLe.preferredWidth = clearSz;
                    clearLe.preferredHeight = clearSz;
                }
                try { ScaleButtonIconPadding(_titleSearchChipClearAllRT, s); } catch { }
            }

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_titleSearchChipHostRT);
                if (_titleSearchChipInclContentRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_titleSearchChipInclContentRT);
                if (_titleSearchChipExclContentRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_titleSearchChipExclContentRT);
            }
            catch { }

            try
            {
                if (contentScrollRT != null)
                    _titleSearchChipHostGO.transform.SetSiblingIndex(contentScrollRT.transform.GetSiblingIndex() + 1);
                else
                    _titleSearchChipHostGO.transform.SetAsLastSibling();
            }
            catch { }
        }

        private sealed class TitleSearchCommitKeys : MonoBehaviour
        {
            public GalleryPanel Panel;
            public InputField Field;

            private void OnGUI()
            {
                if (Panel == null || Field == null || !Field.isFocused) return;
                Event e = Event.current;
                if (e == null || e.type != EventType.KeyDown) return;

                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    bool forceExclude = e.shift;
                    try { Panel.TitleSearchOnCommitDraft(Field, forceExclude); } catch { }
                    return;
                }

                // Consume Tab/Down — transfer owned by HandleKeyboardInput (avoid OnGUI+Update flip-flop).
                if (e.keyCode == KeyCode.Tab
                    || (e.keyCode == KeyCode.DownArrow && !e.control && !e.alt && !e.command))
                {
                    e.Use();
                    return;
                }

                // Empty draft + Ctrl/Cmd+Backspace pops last chip.
                // Plain Backspace must not — chips live outside field (filter chrome), not in-field tokens.
                if (e.keyCode == KeyCode.Backspace && (e.control || e.command) && !e.alt)
                {
                    string draft = Field.text ?? "";
                    if (draft.Length == 0)
                    {
                        try
                        {
                            if (Panel.TitleSearchTryPopLastChip())
                            {
                                e.Use();
                                return;
                            }
                        }
                        catch { }
                    }
                    return;
                }

                // Empty draft + Minus toggles last chip include/exclude.
                if ((e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus) && !e.control && !e.command)
                {
                    string draft = Field.text ?? "";
                    if (draft.Length == 0)
                    {
                        try
                        {
                            if (Panel.TitleSearchTryToggleLastChipPolarity())
                            {
                                e.Use();
                                return;
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        /// <summary>
        /// Exact vocabulary title #tag / -#tag chips move into include/exclude filter sets (one truth).
        /// Substring / unknown names stay in title search. Returns true when filter sets changed.
        /// </summary>
        private bool BridgeExactTitleTagChipsIntoFilterSets()
        {
            if (_bridgingUserTagFilterTitleSearch) return false;
            if (_titleSearchChips == null || _titleSearchChips.Count == 0) return false;

            _bridgingUserTagFilterTitleSearch = true;
            bool filterChanged = false;
            try
            {
                for (int i = _titleSearchChips.Count - 1; i >= 0; i--)
                {
                    TitleSearchChip c = _titleSearchChips[i];
                    if (c.Kind != TitleSearchChipKind.Tag) continue;
                    string n = VpbLocalDatabase.NormalizeGalleryUserTagName(c.Value);
                    if (string.IsNullOrEmpty(n)) continue;
                    if (!IsExactCachedUserTagVocabularyName(n)) continue;

                    if (c.Polarity == TitleSearchChipPolarity.Exclude)
                    {
                        if (activeUserTags != null && activeUserTags.Remove(n))
                            filterChanged = true;
                        if (excludedUserTags != null && !excludedUserTags.Contains(n))
                        {
                            excludedUserTags.Add(n);
                            filterChanged = true;
                        }
                    }
                    else
                    {
                        if (excludedUserTags != null && excludedUserTags.Remove(n))
                            filterChanged = true;
                        if (activeUserTags != null && !activeUserTags.Contains(n))
                        {
                            activeUserTags.Add(n);
                            filterChanged = true;
                        }
                    }

                    _titleSearchChips.RemoveAt(i);
                }

                // Title chip removal alone is handled by caller re-serialize; report filter set mutations only.
                return filterChanged;
            }
            finally
            {
                _bridgingUserTagFilterTitleSearch = false;
            }
        }

        /// <summary>When side/DetailStrip mutates filter sets, drop matching exact title #tag chips (avoid double filter UI).</summary>
        private void BridgeTitleSearchTagChipFromFilterSet(string tagName)
        {
            if (_bridgingUserTagFilterTitleSearch) return;
            if (string.IsNullOrEmpty(tagName)) return;
            if (_titleSearchChips == null || _titleSearchChips.Count == 0) return;

            string n = VpbLocalDatabase.NormalizeGalleryUserTagName(tagName);
            if (string.IsNullOrEmpty(n)) return;

            _bridgingUserTagFilterTitleSearch = true;
            bool removed = false;
            try
            {
                for (int i = _titleSearchChips.Count - 1; i >= 0; i--)
                {
                    TitleSearchChip c = _titleSearchChips[i];
                    if (c.Kind != TitleSearchChipKind.Tag) continue;
                    if (!string.Equals(c.Value, n, StringComparison.OrdinalIgnoreCase)) continue;
                    _titleSearchChips.RemoveAt(i);
                    removed = true;
                }
                if (removed)
                {
                    string serialized = GalleryTitleSearchChipUtil.Serialize(_titleSearchChips);
                    try { RebuildTitleSearchChipUi(); } catch { }
                    if (!string.Equals(serialized, nameFilter ?? "", StringComparison.Ordinal))
                    {
                        AssignNameFilterState(serialized);
                        try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, HasTitleSearchChips() ? "" : serialized, _titleBarSearchOnValueChanged); } catch { }
                    }
                }
            }
            finally
            {
                _bridgingUserTagFilterTitleSearch = false;
            }
        }

        private bool IsExactCachedUserTagVocabularyName(string normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName)) return false;
            if (cachedUserTagSideTab == null || cachedUserTagSideTab.Count == 0) return false;
            for (int i = 0; i < cachedUserTagSideTab.Count; i++)
            {
                if (string.Equals(cachedUserTagSideTab[i].Name, normalizedName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    /// <summary>Drop target for title-search Incl / Excl rows.</summary>
    internal sealed class TitleSearchChipDropZone : MonoBehaviour, IDropHandler
    {
        public GalleryPanel Panel;
        public TitleSearchChipPolarity TargetPolarity;

        public void OnDrop(PointerEventData eventData)
        {
            if (Panel == null) return;
            if (UserTagDragSession.IsTitleSearchChipMoveActive
                && UserTagDragSession.PendingTitleSearchChipPanel == Panel)
            {
                if (TargetPolarity == TitleSearchChipPolarity.Exclude
                    && !UserTagDragSession.PendingTitleSearchChipCanExclude)
                {
                    Panel.TitleSearchNotifyExcludeRejected();
                    UserTagDragSession.Clear();
                    return;
                }
                Panel.TitleSearchMoveChipToPolarity(UserTagDragSession.PendingTitleSearchChipIndex, TargetPolarity);
                UserTagDragSession.Clear();
                return;
            }
            if (!UserTagDragSession.IsTitleSearchTagDropActive) return;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return;
            Panel.TitleSearchAcceptDroppedTags(tags, TargetPolarity);
            UserTagDragSession.Clear();
        }
    }

    /// <summary>Raycast gate + hover tint for title-search drop overlays.</summary>
    internal sealed class TitleSearchDropRaycastGate : MonoBehaviour, ICanvasRaycastFilter, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Image;
        public TitleSearchChipPolarity TargetPolarity;
        public Color HoverColor = new Color(1f, 1f, 1f, 0.18f);
        public Color RejectColor = new Color(0.85f, 0.22f, 0.22f, 0.28f);

        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
        {
            return UserTagDragSession.IsTitleSearchDropActive;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Image == null) return;
            if (!UserTagDragSession.IsTitleSearchDropActive) return;
            if (TargetPolarity == TitleSearchChipPolarity.Exclude
                && UserTagDragSession.IsTitleSearchChipMoveActive
                && !UserTagDragSession.PendingTitleSearchChipCanExclude)
            {
                Image.color = RejectColor;
                return;
            }
            Image.color = HoverColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Image == null) return;
            Image.color = new Color(HoverColor.r, HoverColor.g, HoverColor.b, 0f);
        }
    }

    /// <summary>Drag a committed title-search chip to Incl / Excl row (polarity move).</summary>
    internal sealed class TitleSearchChipDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public GalleryPanel Panel;
        public int ChipIndex;
        public bool CanMoveToExclude;
        public bool ConsumedByDrag { get; private set; }

        private CanvasGroup _cg;
        private bool _pressed;
        private bool _dragging;
        private Vector2 _pressPos;
        private Vector2 _lastScreenPos;
        private Camera _pressEventCamera;
        private float _pressTime;
        private GameObject _ghost;
        private RectTransform _ghostRT;
        private Canvas _rootCanvas;
        private readonly List<RaycastResult> _raycastHits = new List<RaycastResult>(12);
        private bool _releaseProcessed;
        private const float DesktopMinScreenPixelsForChipDrag = 10f;
        private const float VrHoldSecondsForChipDrag = 0.25f;

        private void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        private void OnDisable()
        {
            if (_pressed || _dragging)
                CleanupDragVisuals();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Panel == null || eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = true;
            _dragging = false;
            ConsumedByDrag = false;
            _releaseProcessed = false;
            _pressPos = eventData.position;
            _lastScreenPos = eventData.position;
            _pressEventCamera = eventData.pressEventCamera;
            _pressTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_pressed) return;
            if (!isActiveAndEnabled) return;
            _pressed = false;
            _releaseProcessed = true;
            if (_dragging)
            {
                if (eventData != null) _lastScreenPos = eventData.position;
                EndManualDrag(eventData);
            }
        }

        private void Update()
        {
            if (Panel == null) return;
            if (_dragging)
            {
                if (!XrUtils.IsVrActive())
                    _lastScreenPos = (Vector2)Input.mousePosition;
                UpdateGhostPosition();
                if (!_releaseProcessed && Input.GetMouseButtonUp(0))
                    EndManualDrag(null);
                return;
            }
            if (!_pressed) return;

            // VR: OnBeginDrag starts (no pixel gate). Avoid hold-alone auto-start stealing taps.
            if (XrUtils.IsVrActive()) return;

            Vector2 cur = Input.mousePosition;
            _lastScreenPos = cur;
            if ((cur - _pressPos).sqrMagnitude < DesktopMinScreenPixelsForChipDrag * DesktopMinScreenPixelsForChipDrag)
                return;
            BeginManualDrag();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Panel == null) return;
            if (eventData != null)
            {
                _lastScreenPos = eventData.position;
                if (eventData.pressEventCamera != null)
                    _pressEventCamera = eventData.pressEventCamera;
            }
            if (XrUtils.IsVrActive())
            {
                if (Time.unscaledTime - _pressTime < VrHoldSecondsForChipDrag) return;
            }
            BeginManualDrag();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null) _lastScreenPos = eventData.position;
            if (_dragging) UpdateGhostPosition();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            if (eventData != null) _lastScreenPos = eventData.position;
            EndManualDrag(eventData);
        }

        private void BeginManualDrag()
        {
            if (Panel == null || _dragging) return;
            _dragging = true;
            ConsumedByDrag = true;
            try { Panel.TitleSearchOnChipDragBegan(ChipIndex); } catch { }

            if (_cg != null)
            {
                _cg.alpha = 0.65f;
                _cg.blocksRaycasts = false;
            }
            CreateGhost();
            UpdateGhostPosition();
        }

        private void EndManualDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            TryDrop(eventData);
            CleanupDragVisuals();
        }

        private void TryDrop(PointerEventData eventData)
        {
            if (Panel == null) return;
            EventSystem es = EventSystem.current;
            if (es == null) return;

            Vector2 screenPos = eventData != null ? eventData.position : _lastScreenPos;
            var ped = eventData ?? new PointerEventData(es);
            ped.position = screenPos;
            _raycastHits.Clear();
            es.RaycastAll(ped, _raycastHits);
            for (int i = 0; i < _raycastHits.Count; i++)
            {
                GameObject go = _raycastHits[i].gameObject;
                if (go == null) continue;
                TitleSearchChipDropZone tdz = go.GetComponentInParent<TitleSearchChipDropZone>();
                if (tdz == null || tdz.Panel != Panel) continue;
                if (tdz.TargetPolarity == TitleSearchChipPolarity.Exclude && !CanMoveToExclude)
                {
                    try { Panel.TitleSearchNotifyExcludeRejected(); } catch { }
                    return;
                }
                Panel.TitleSearchMoveChipToPolarity(ChipIndex, tdz.TargetPolarity);
                return;
            }
        }

        private void CleanupDragVisuals()
        {
            bool wasDragging = _dragging;
            _pressed = false;
            _dragging = false;
            _releaseProcessed = false;
            if (!wasDragging) ConsumedByDrag = false;
            if (_cg != null)
            {
                _cg.alpha = 1f;
                _cg.blocksRaycasts = true;
            }
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _ghostRT = null;
            _rootCanvas = null;
            try { if (Panel != null) Panel.TitleSearchOnDragEnded(); } catch { }
            if (UserTagDragSession.PendingTitleSearchChipPanel == Panel)
            {
                UserTagDragSession.PendingTitleSearchChipIndex = -1;
                UserTagDragSession.PendingTitleSearchChipPanel = null;
                UserTagDragSession.PendingTitleSearchChipCanExclude = false;
            }
        }

        private void CreateGhost()
        {
            Canvas root = null;
            try { root = GetComponentInParent<Canvas>(); } catch { }
            if (root == null && Panel != null) root = Panel.canvas;
            if (root == null) return;
            _rootCanvas = root;

            float s = 1f;
            try { if (Panel != null && Panel.ChromeScale > 0f) s = Panel.ChromeScale; } catch { }

            _ghost = new GameObject("VPB_TitleSearchChipDragGhost");
            _ghost.layer = root.gameObject.layer;
            _ghostRT = _ghost.AddComponent<RectTransform>();
            _ghostRT.SetParent(root.transform, false);
            _ghostRT.anchorMin = new Vector2(0.5f, 0.5f);
            _ghostRT.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRT.pivot = new Vector2(0.5f, 0.5f);

            UI.AddImage(_ghost, new Color(0.12f, 0.12f, 0.14f, 0.94f), raycastTarget: false);
            float pad = Mathf.Round(8f * s);
            UI.AddHLG(_ghost, spacing: 0f, padding: UI.Pad(pad, pad, pad * 0.6f, pad * 0.6f),
                childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: false, childForceExpandHeight: false);
            ContentSizeFitter csf = _ghost.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string text = "Chip";
            try
            {
                Transform lt = transform.Find("Label");
                if (lt != null)
                {
                    Text t = lt.GetComponent<Text>();
                    if (t != null && !string.IsNullOrEmpty(t.text)) text = t.text;
                }
            }
            catch { }

            Text ghostTxt = UI.CreateLabel(_ghost, text, GalleryUiDesignTokens.FontBodyRef, UI.PopupText,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, raycastTarget: false, name: "GhostLabel");
            try { GalleryUiMetrics.ApplyFont(ghostTxt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef); } catch { }
            try { LayoutRebuilder.ForceRebuildLayoutImmediate(_ghostRT); } catch { }
        }

        private void UpdateGhostPosition()
        {
            if (_ghostRT == null) return;
            Canvas root = _rootCanvas;
            if (root == null)
            {
                _ghostRT.SetAsLastSibling();
                return;
            }
            RectTransform parent = root.transform as RectTransform;
            if (parent == null)
            {
                _ghostRT.SetAsLastSibling();
                return;
            }
            Camera cam = null;
            if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = _pressEventCamera;
                if (cam == null)
                    cam = root.worldCamera != null ? root.worldCamera : Camera.main;
            }
            Vector3 world;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, _lastScreenPos, cam, out world))
                _ghostRT.position = world;
            _ghostRT.SetAsLastSibling();
        }
    }
}
