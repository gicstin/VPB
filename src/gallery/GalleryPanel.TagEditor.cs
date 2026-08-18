using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Unified tag editor: Apply (selection tagging) + Database (vocab admin) in one
    /// DetailStripTagMenu window. Side-pane Edit opens Database mode; strip opens Apply.
    /// Path class: warm/cold UI — rebuild on demand, no per-frame work.
    /// </summary>
    public partial class GalleryPanel
    {
        private enum DetailStripTagMenuMode
        {
            Apply = 0,
            Database = 1
        }

        private const float DetailStripTagMenuDbComfortHeightRef = 520f;
        private const float DetailStripTagMenuDbNewTagHRef = 72f;
        private const float DetailStripTagMenuModeTabWRef = 56f;

        private static readonly Color DetailStripTagMenuModeOnCol = GalleryUiColorTokens.ActiveSelected;
        private static readonly Color DetailStripTagMenuModeOffCol = GalleryUiColorTokens.SurfaceDarker;

        private DetailStripTagMenuMode _detailStripTagMenuMode = DetailStripTagMenuMode.Apply;
        private GameObject _detailStripTagMenuModeTabsGO;
        private GameObject _detailStripTagMenuModeApplyBtn;
        private GameObject _detailStripTagMenuModeDbBtn;
        private Image _detailStripTagMenuModeApplyImg;
        private Image _detailStripTagMenuModeDbImg;
        private Text _detailStripTagMenuModeApplyText;
        private Text _detailStripTagMenuModeDbText;
        private GameObject _detailStripTagMenuDbHostGO;
        private GameObject _detailStripTagMenuDbListHeaderGO;
        private GameObject _detailStripTagMenuDbScrollGO;
        private GameObject _detailStripTagMenuDbSortBtnGO;
        private GameObject _detailStripTagMenuDbClearBtnGO;
        private GameObject _detailStripTagMenuDbActionRowGO;
        private GameObject _detailStripTagMenuDbNewTagBlockGO;
        private Text _detailStripTagMenuDbListHintText;

        /// <summary>Side-pane Edit — open unified window in Database mode (vocab CRUD).</summary>
        private void ShowUserTagListEditor()
        {
            DestroyLegacyUserTagEditorOverlayIfPresent();
            DetailStripEnsureTagMenu();
            DetailStripEnsureTagMenuDatabasePane();
            if (_detailStripTagMenuRoot == null) return;

            _userTagEditorRowSelection.Clear();
            _userTagEditorAnchorTag = null;
            _userTagEditorSortMode = 0;
            if (_userTagEditorNewTagInput != null) _userTagEditorNewTagInput.text = "";
            if (_userTagEditorMergeModalGo != null) _userTagEditorMergeModalGo.SetActive(false);
            if (_userTagEditorMergeModalInput != null) _userTagEditorMergeModalInput.text = "";
            if (_userTagEditorRenameModalGo != null) _userTagEditorRenameModalGo.SetActive(false);
            if (_userTagEditorRenameModalInput != null) _userTagEditorRenameModalInput.text = "";
            _userTagEditorRenameSourcePrefix = null;
            CloseTagCategoryEditorModal();
            UserTagEditorSyncSortIcon();

            DetailStripOpenTagMenu(DetailStripTagMenuMode.Database);
        }

        private void HideUserTagListEditor()
        {
            CloseTagCategoryEditorModal();
            if (_userTagEditorMergeModalGo != null) _userTagEditorMergeModalGo.SetActive(false);
            if (_userTagEditorRenameModalGo != null) _userTagEditorRenameModalGo.SetActive(false);
            DetailStripCloseTagMenu();
        }

        /// <summary>Ensures Database pane exists inside unified tag menu (no separate modal overlay).</summary>
        private void EnsureUserTagEditorUiBuilt()
        {
            DestroyLegacyUserTagEditorOverlayIfPresent();
            DetailStripEnsureTagMenu();
            DetailStripEnsureTagMenuDatabasePane();
        }

        private void DestroyLegacyUserTagEditorOverlayIfPresent()
        {
            if (_userTagEditorRoot == null) return;
            // Only destroy old centered modal — never the unified DetailStripTagMenu root.
            if (_userTagEditorRoot.name != "VPB_UserTagEditorOverlay") return;

            try { UnityEngine.Object.Destroy(_userTagEditorRoot); } catch { }
            ClearUserTagEditorUiFieldRefs();
        }

        private void ClearUserTagEditorUiFieldRefs()
        {
            _userTagEditorRoot = null;
            _userTagEditorRowsParent = null;
            _userTagEditorFilterInput = null;
            _userTagEditorNewTagInput = null;
            _userTagEditorNewTagInputChrome = null;
            UserTagEditorStopNewTagChromeFlash();
            _userTagEditorSortIconImage = null;
            _userTagEditorTitleText = null;
            _userTagEditorMergeModalGo = null;
            _userTagEditorMergeModalTitleText = null;
            _userTagEditorMergeModalInput = null;
            _userTagEditorRenameModalGo = null;
            _userTagEditorRenameModalTitleText = null;
            _userTagEditorRenameModalInput = null;
            _userTagEditorRenameSourcePrefix = null;
            _userTagEditorRowVisuals.Clear();
            _userTagEditorVisibleRows.Clear();
        }

        private void DetailStripClearTagMenuDatabaseFieldRefs()
        {
            _detailStripTagMenuModeTabsGO = null;
            _detailStripTagMenuModeApplyBtn = null;
            _detailStripTagMenuModeDbBtn = null;
            _detailStripTagMenuModeApplyImg = null;
            _detailStripTagMenuModeDbImg = null;
            _detailStripTagMenuModeApplyText = null;
            _detailStripTagMenuModeDbText = null;
            _detailStripTagMenuDbHostGO = null;
            _detailStripTagMenuDbListHeaderGO = null;
            _detailStripTagMenuDbScrollGO = null;
            _detailStripTagMenuDbSortBtnGO = null;
            _detailStripTagMenuDbClearBtnGO = null;
            _detailStripTagMenuDbActionRowGO = null;
            _detailStripTagMenuDbNewTagBlockGO = null;
            _detailStripTagMenuDbListHintText = null;
            ClearUserTagEditorUiFieldRefs();
        }

        /// <summary>
        /// Keep mode tabs + Database pane on global ChromeScale (ButtonSizeRef / PopupMenu fonts).
        /// Called from <see cref="DetailStripSyncTagMenuLayout"/>.
        /// </summary>
        private void DetailStripSyncTagMenuUnifiedChrome(float s)
        {
            if (s <= 0f) s = 1f;
            float btn = GalleryUiDesignTokens.ButtonSizeRef * s;
            float chromeBtn = DetailStripTagMenuChromeBtnRef * s;
            float iconPadChrome = 5f * s;
            float iconPadAction = 6f * s;
            float gap = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;

            if (_detailStripTagMenuModeTabsGO != null)
            {
                LayoutElement tabsLe = _detailStripTagMenuModeTabsGO.GetComponent<LayoutElement>();
                if (tabsLe != null)
                {
                    tabsLe.preferredHeight = chromeBtn;
                    tabsLe.minHeight = chromeBtn;
                }
                HorizontalLayoutGroup tabsHlg = _detailStripTagMenuModeTabsGO.GetComponent<HorizontalLayoutGroup>();
                if (tabsHlg != null)
                {
                    tabsHlg.spacing = 4f * s;
                    tabsHlg.childForceExpandHeight = false;
                    tabsHlg.childControlHeight = true;
                    tabsHlg.childControlWidth = true;
                }
                DetailStripSyncTagMenuModeTabButton(_detailStripTagMenuModeApplyBtn, _detailStripTagMenuModeApplyText, DetailStripTagMenuModeTabWRef * s, chromeBtn, s);
                DetailStripSyncTagMenuModeTabButton(_detailStripTagMenuModeDbBtn, _detailStripTagMenuModeDbText, DetailStripTagMenuModeTabWRef * 1.35f * s, chromeBtn, s);
                DetailStripSyncTagMenuModeTabChrome();
            }

            if (_detailStripTagMenuDbHostGO == null) return;

            VerticalLayoutGroup hostVlg = _detailStripTagMenuDbHostGO.GetComponent<VerticalLayoutGroup>();
            if (hostVlg != null) hostVlg.spacing = gap;
            LayoutElement hostLe = _detailStripTagMenuDbHostGO.GetComponent<LayoutElement>();
            if (hostLe != null)
            {
                hostLe.minHeight = 80f * s;
                hostLe.preferredHeight = 80f * s;
                hostLe.flexibleHeight = 1f;
            }

            if (_detailStripTagMenuDbListHeaderGO != null)
            {
                LayoutElement hdrLe = _detailStripTagMenuDbListHeaderGO.GetComponent<LayoutElement>();
                if (hdrLe != null)
                {
                    hdrLe.preferredHeight = chromeBtn;
                    hdrLe.minHeight = chromeBtn;
                }
                HorizontalLayoutGroup hdrHlg = _detailStripTagMenuDbListHeaderGO.GetComponent<HorizontalLayoutGroup>();
                if (hdrHlg != null)
                {
                    hdrHlg.spacing = 4f * s;
                    hdrHlg.childForceExpandHeight = false;
                    hdrHlg.childControlHeight = true;
                }
            }

            DetailStripSyncTagMenuSquareIconBtn(_detailStripTagMenuDbSortBtnGO, chromeBtn, iconPadChrome);
            DetailStripSyncTagMenuSquareIconBtn(_detailStripTagMenuDbClearBtnGO, chromeBtn, iconPadChrome);
            UserTagEditorSyncSortIcon();

            if (_detailStripTagMenuDbListHintText != null)
                GalleryUiMetrics.ApplyFont(
                    _detailStripTagMenuDbListHintText,
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    s,
                    GalleryUiDesignTokens.FontMinRef);

            if (_detailStripTagMenuDbScrollGO != null)
            {
                LayoutElement scrollLe = _detailStripTagMenuDbScrollGO.GetComponent<LayoutElement>();
                if (scrollLe != null)
                {
                    scrollLe.minHeight = 80f * s;
                    scrollLe.preferredHeight = 80f * s;
                    scrollLe.flexibleHeight = 1f;
                }
            }

            if (_userTagEditorRowsParent != null)
            {
                VerticalLayoutGroup rowsVlg = _userTagEditorRowsParent.GetComponent<VerticalLayoutGroup>();
                if (rowsVlg != null)
                {
                    int sidePad = Mathf.RoundToInt(4f * s);
                    rowsVlg.padding = new RectOffset(sidePad, sidePad, 0, 0);
                    rowsVlg.spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
                }
                // Do NOT RebuildUserTagEditorRows here — full row recreate is warm-path heavy.
                // Scale applied on next open / filter / sort rebuild.
            }

            float newH = DetailStripTagMenuDbNewTagHRef * s;
            if (_detailStripTagMenuDbNewTagBlockGO != null)
            {
                LayoutElement ntbLe = _detailStripTagMenuDbNewTagBlockGO.GetComponent<LayoutElement>();
                if (ntbLe != null)
                {
                    ntbLe.minHeight = newH;
                    ntbLe.preferredHeight = newH;
                }
            }
            if (_userTagEditorNewTagInput != null)
            {
                LayoutElement nLe = _userTagEditorNewTagInput.GetComponent<LayoutElement>();
                if (nLe != null)
                {
                    nLe.minHeight = newH;
                    nLe.preferredHeight = newH;
                }
                Transform nta = _userTagEditorNewTagInput.transform.Find("TextArea");
                if (nta != null)
                {
                    RectTransform ntaRt = nta.GetComponent<RectTransform>();
                    if (ntaRt != null)
                    {
                        ntaRt.offsetMin = new Vector2(6f * s, 4f * s);
                        ntaRt.offsetMax = new Vector2(-6f * s, -4f * s);
                    }
                }
                if (_userTagEditorNewTagInput.textComponent != null)
                    GalleryUiMetrics.ApplyFont(
                        _userTagEditorNewTagInput.textComponent,
                        GalleryUiDesignTokens.PopupMenuRowFontRef,
                        s,
                        GalleryUiDesignTokens.FontMinRef);
                if (_userTagEditorNewTagInput.placeholder is Text nph)
                    GalleryUiMetrics.ApplyFont(
                        nph,
                        GalleryUiDesignTokens.PopupMenuRowFontRef,
                        s,
                        GalleryUiDesignTokens.FontMinRef);
            }

            if (_detailStripTagMenuDbActionRowGO != null)
            {
                LayoutElement arLe = _detailStripTagMenuDbActionRowGO.GetComponent<LayoutElement>();
                if (arLe != null)
                {
                    arLe.minHeight = btn + 2f * s;
                    arLe.preferredHeight = btn + 2f * s;
                }
                HorizontalLayoutGroup arHlg = _detailStripTagMenuDbActionRowGO.GetComponent<HorizontalLayoutGroup>();
                if (arHlg != null) arHlg.spacing = 4f * s;

                Transform arTr = _detailStripTagMenuDbActionRowGO.transform;
                for (int i = 0; i < arTr.childCount; i++)
                {
                    Transform ch = arTr.GetChild(i);
                    if (ch == null || ch.name == "PadL" || ch.name == "PadR") continue;
                    // Square icon toolbar buttons only.
                    if (ch.GetComponent<AspectRatioFitter>() == null) continue;
                    DetailStripSyncTagMenuSquareIconBtn(ch.gameObject, btn, iconPadAction);
                }
            }
        }

        private void DetailStripSyncTagMenuModeTabButton(GameObject btnGO, Text label, float width, float height, float s)
        {
            if (btnGO == null) return;
            LayoutElement le = btnGO.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = width;
                le.minWidth = width;
                le.preferredHeight = height;
                le.minHeight = height;
                le.flexibleWidth = 1f;
                le.flexibleHeight = 0f;
            }
            RectTransform rt = btnGO.GetComponent<RectTransform>();
            if (rt != null)
                rt.sizeDelta = new Vector2(width, height);
            if (label != null)
                GalleryUiMetrics.ApplyFont(label, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
        }

        private static void DetailStripSyncTagMenuSquareIconBtn(GameObject go, float edge, float iconPad)
        {
            if (go == null || edge <= 0f) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = edge;
                le.preferredHeight = edge;
                le.minWidth = edge;
                le.minHeight = edge;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 0f;
            }
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.sizeDelta = new Vector2(edge, edge);
            Transform iconTr = go.transform.Find("Icon");
            if (iconTr != null)
            {
                RectTransform irt = iconTr.GetComponent<RectTransform>();
                if (irt != null)
                {
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    irt.sizeDelta = new Vector2(-iconPad * 2f, -iconPad * 2f);
                    irt.anchoredPosition = Vector2.zero;
                }
            }
        }

        private bool DetailStripTagMenuNeedsUnifiedRebuild()
        {
            if (_detailStripTagMenuPanelGO == null) return false;
            if (_detailStripTagMenuPanelGO.transform.Find("ModeTabs") == null) return true;
            if (_detailStripTagMenuPanelGO.transform.Find("DatabaseHost") == null) return true;
            return false;
        }

        private void DetailStripEnsureTagMenuModeTabs()
        {
            if (_detailStripTagMenuPanelGO == null) return;
            if (_detailStripTagMenuModeTabsGO != null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            // Insert mode tabs as first body row (after header + rule): Tag | Database.
            _detailStripTagMenuModeTabsGO = UI.CreateChildRT(_detailStripTagMenuPanelGO, "ModeTabs");
            UI.AddHLG(
                _detailStripTagMenuModeTabsGO,
                spacing: UI.GapTight(s),
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: false);
            float tabH = DetailStripTagMenuChromeBtnRef * s;
            UI.AddLE(
                _detailStripTagMenuModeTabsGO,
                preferredHeight: tabH,
                minHeight: tabH,
                flexibleWidth: 1f,
                flexibleHeight: 0f);

            float tabW = DetailStripTagMenuModeTabWRef * s;
            _detailStripTagMenuModeApplyBtn = UI.CreateUIButton(
                _detailStripTagMenuModeTabsGO, tabW, tabH,
                VPBTranslation.T("gallery.detail.tag_menu_mode_tag", "Tag"),
                GalleryUiDesignTokens.PopupMenuRowFontRef, 0f, 0f, AnchorPresets.middleCenter,
                () => DetailStripSetTagMenuMode(DetailStripTagMenuMode.Apply));
            _detailStripTagMenuModeApplyBtn.name = "ModeApply";
            _detailStripTagMenuModeApplyImg = _detailStripTagMenuModeApplyBtn.GetComponent<Image>();
            _detailStripTagMenuModeApplyText = _detailStripTagMenuModeApplyBtn.GetComponentInChildren<Text>(true);
            UI.AddLE(_detailStripTagMenuModeApplyBtn, preferredWidth: tabW, preferredHeight: tabH, minWidth: tabW, minHeight: tabH, flexibleWidth: 1f);
            if (_detailStripTagMenuModeApplyText != null)
                GalleryUiMetrics.ApplyFont(_detailStripTagMenuModeApplyText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            AddTooltipPlain(
                _detailStripTagMenuModeApplyBtn,
                VPBTranslation.T("gallery.detail.tag_menu_mode_tag_tip", "Tag current selection (Applied / Available)."));

            float tabWDb = tabW * 1.35f;
            _detailStripTagMenuModeDbBtn = UI.CreateUIButton(
                _detailStripTagMenuModeTabsGO, tabWDb, tabH,
                VPBTranslation.T("gallery.detail.tag_menu_mode_db", "Database"),
                GalleryUiDesignTokens.PopupMenuRowFontRef, 0f, 0f, AnchorPresets.middleCenter,
                () => DetailStripSetTagMenuMode(DetailStripTagMenuMode.Database));
            _detailStripTagMenuModeDbBtn.name = "ModeDatabase";
            _detailStripTagMenuModeDbImg = _detailStripTagMenuModeDbBtn.GetComponent<Image>();
            _detailStripTagMenuModeDbText = _detailStripTagMenuModeDbBtn.GetComponentInChildren<Text>(true);
            UI.AddLE(_detailStripTagMenuModeDbBtn, preferredWidth: tabWDb, preferredHeight: tabH, minWidth: tabWDb, minHeight: tabH, flexibleWidth: 1f);
            if (_detailStripTagMenuModeDbText != null)
                GalleryUiMetrics.ApplyFont(_detailStripTagMenuModeDbText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            AddTooltipPlain(
                _detailStripTagMenuModeDbBtn,
                VPBTranslation.T(
                    "gallery.detail.tag_menu_mode_db_tip",
                    "Manage tag vocabulary: create, purge, merge, rename, categories, YAML."));

            Button applyBtn = _detailStripTagMenuModeApplyBtn.GetComponent<Button>();
            if (applyBtn != null) applyBtn.transition = Selectable.Transition.None;
            Button dbBtn = _detailStripTagMenuModeDbBtn.GetComponent<Button>();
            if (dbBtn != null) dbBtn.transition = Selectable.Transition.None;

            // Sit after Header + HeaderRule + FilterRow — before Tip.
            int insertAt = 2;
            Transform filterRow = _detailStripTagMenuPanelGO.transform.Find("FilterRow");
            if (filterRow != null)
                insertAt = filterRow.GetSiblingIndex() + 1;
            else if (_detailStripTagMenuPanelGO.transform.Find("HeaderRule") == null)
                insertAt = 1;
            _detailStripTagMenuModeTabsGO.transform.SetSiblingIndex(insertAt);
        }

        private void DetailStripEnsureTagMenuDatabasePane()
        {
            if (_detailStripTagMenuPanelGO == null || _detailStripTagMenuRoot == null) return;
            if (_detailStripTagMenuDbHostGO != null && _userTagEditorRowsParent != null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float u = s * 1.38f;
            GalleryModalTypography editorType = new GalleryModalTypography(u);
            int smallFont = editorType.Body;
            int bodyFont = GalleryUiMetrics.ScaledFontSize(
                GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            float chromeBtn = DetailStripTagMenuChromeBtnRef * s;
            float actionBtn = GalleryUiDesignTokens.ButtonSizeRef * s;
            float iconPadChrome = 5f * s;
            float iconPadAction = 6f * s;

            _detailStripTagMenuDbHostGO = UI.CreateChildRT(_detailStripTagMenuPanelGO, "DatabaseHost");
            UI.AddVLG(
                _detailStripTagMenuDbHostGO,
                spacing: GalleryUiDesignTokens.PopupMenuRowSpacingRef * s,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.UpperLeft,
                childForceExpandWidth: true,
                childForceExpandHeight: false);
            UI.AddLE(
                _detailStripTagMenuDbHostGO,
                flexibleWidth: 1f,
                flexibleHeight: 1f,
                minHeight: 80f * s,
                preferredHeight: 80f * s);

            // List header: sort + clear selection
            _detailStripTagMenuDbListHeaderGO = UI.CreateChildRT(_detailStripTagMenuDbHostGO, "DbListHeader");
            UI.AddHLG(
                _detailStripTagMenuDbListHeaderGO,
                spacing: UI.GapTight(s),
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.MiddleLeft,
                childForceExpandWidth: false,
                childForceExpandHeight: false);
            UI.AddLE(
                _detailStripTagMenuDbListHeaderGO,
                preferredHeight: chromeBtn,
                minHeight: chromeBtn,
                flexibleWidth: 1f,
                flexibleHeight: 0f);

            Color sortBackdropCol = GalleryUiColorTokens.ActiveUtility;
            Sprite sortSpr0 = sceneSourceSortModeSprites != null && sceneSourceSortModeSprites.Length > 0
                ? sceneSourceSortModeSprites[0]
                : null;
            _detailStripTagMenuDbSortBtnGO = UI.CreateSideTabSquareIconButton(
                _detailStripTagMenuDbListHeaderGO, chromeBtn, sortSpr0, UserTagEditorCycleSort, sortBackdropCol, iconPadChrome);
            _detailStripTagMenuDbSortBtnGO.name = "UserTagEditorSortBtn";
            Transform sortIconTr = _detailStripTagMenuDbSortBtnGO.transform.Find("Icon");
            _userTagEditorSortIconImage = sortIconTr != null ? sortIconTr.GetComponent<Image>() : null;
            UserTagEditorSyncSortIcon();
            AddTooltipPlain(
                _detailStripTagMenuDbSortBtnGO,
                VPBTranslation.T(
                    "gallery.usertags.editor_sort_cycle_tip",
                    "Sort list: tap to cycle name A→Z / Z→A / count high→low / low→high."));

            _detailStripTagMenuDbListHintText = UI.CreateLabel(
                _detailStripTagMenuDbListHeaderGO,
                VPBTranslation.T("gallery.detail.tag_menu_db_list_hint", "All tags · click select · Shift range"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                UI.PopupMutedText,
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "DbListHint");
            GalleryUiMetrics.ApplyFont(
                _detailStripTagMenuDbListHintText,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                s,
                GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(_detailStripTagMenuDbListHintText.gameObject, flexibleWidth: 1f, minWidth: 40f * s);

            Sprite clearSpr = UI.LoadIconSprite("square-x", GalleryUiColorTokens.TextMuted);
            Color clearSearchBackdrop = GalleryUiColorTokens.ActiveWarnHeader;
            _detailStripTagMenuDbClearBtnGO = UI.CreateSideTabSquareIconButton(
                _detailStripTagMenuDbListHeaderGO, chromeBtn, clearSpr, UserTagEditorClearFilter, clearSearchBackdrop, iconPadChrome);
            AddTooltipPlain(
                _detailStripTagMenuDbClearBtnGO,
                VPBTranslation.T("gallery.usertags.editor_clear_search_tip", "Clear filter text and highlighted row selection"));

            _detailStripTagMenuDbScrollGO = UI.CreateVScrollableContent(
                _detailStripTagMenuDbHostGO,
                DetailStripTagMenuColBg,
                AnchorPresets.stretchAll,
                0f,
                200f * s,
                Vector2.zero,
                12f * s,
                GalleryUiDesignTokens.PopupMenuRowSpacingRef * s,
                false);
            UI.AddLE(_detailStripTagMenuDbScrollGO, flexibleWidth: 1f, flexibleHeight: 1f, minHeight: 80f * s, preferredHeight: 80f * s);
            ScrollRect dbSr = _detailStripTagMenuDbScrollGO.GetComponent<ScrollRect>();
            if (dbSr != null) dbSr.movementType = ScrollRect.MovementType.Clamped;
            Transform vp = _detailStripTagMenuDbScrollGO.transform.Find("Viewport");
            _userTagEditorRowsParent = vp != null ? vp.Find("Content") : null;
            if (_userTagEditorRowsParent != null)
            {
                VerticalLayoutGroup rowsVlg = _userTagEditorRowsParent.GetComponent<VerticalLayoutGroup>();
                if (rowsVlg != null)
                {
                    int sidePad = Mathf.RoundToInt(4f * s);
                    rowsVlg.padding = new RectOffset(sidePad, sidePad, 0, 0);
                    rowsVlg.spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
                }
            }

            // Compact multiline create — height follows ChromeScale.
            _detailStripTagMenuDbNewTagBlockGO = UI.CreateChildRT(_detailStripTagMenuDbHostGO, "NewTagBlock");
            UI.AddVLG(_detailStripTagMenuDbNewTagBlockGO, UI.GapHair(s));
            float newH = DetailStripTagMenuDbNewTagHRef * s;
            UI.AddLE(_detailStripTagMenuDbNewTagBlockGO, minHeight: newH, preferredHeight: newH, flexibleWidth: 1f, flexibleHeight: 0f);

            GameObject newInGo = new GameObject("NewTagInput");
            newInGo.transform.SetParent(_detailStripTagMenuDbNewTagBlockGO.transform, false);
            Image nBg = UI.AddImage(newInGo, UserTagEditorNewTagChromeBaseCol);
            _userTagEditorNewTagInputChrome = nBg;
            UI.AddLE(newInGo, minHeight: newH, preferredHeight: newH, flexibleWidth: 1f);
            GameObject nta = new GameObject("TextArea");
            nta.transform.SetParent(newInGo.transform, false);
            RectTransform ntaRt = nta.AddComponent<RectTransform>();
            ntaRt.anchorMin = Vector2.zero;
            ntaRt.anchorMax = Vector2.one;
            ntaRt.offsetMin = new Vector2(6f * s, 4f * s);
            ntaRt.offsetMax = new Vector2(-6f * s, -4f * s);
            Text nphT = UI.CreateLabel(
                nta,
                VPBTranslation.T("gallery.usertags.editor_new_ph", "Type or paste tag names here…"),
                bodyFont,
                new Color(0.42f, 0.42f, 0.45f, 1f),
                TextAnchor.UpperLeft,
                HorizontalWrapMode.Wrap,
                VerticalWrapMode.Overflow,
                name: "Placeholder");
            Text ntxT = UI.CreateLabel(nta, "", bodyFont, Color.white, TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow);
            GalleryUiMetrics.ApplyFont(nphT, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            GalleryUiMetrics.ApplyFont(ntxT, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            _userTagEditorNewTagInput = newInGo.AddComponent<InputField>();
            _userTagEditorNewTagInput.textComponent = ntxT;
            _userTagEditorNewTagInput.placeholder = nphT;
            _userTagEditorNewTagInput.lineType = InputField.LineType.MultiLineNewline;
            _userTagEditorNewTagInput.characterLimit = 0;
            FixMultilineHomeEndBehavior(_userTagEditorNewTagInput);

            string userTagEditorInfoWisdom =
                VPBTranslation.T(
                    "gallery.usertags.editor_paste_hint",
                    "Create-tag button: one tag per line (newline split). Blank lines ignored; trim line ends. Other fields may still use comma / tab / semicolon where noted.")
                + "\n"
                + VPBTranslation.T(
                    "gallery.usertags.editor_tag_rules_hint",
                    "Each tag: 1–512 characters; unicode / emoji / punctuation / spaces allowed. No line breaks inside name; control chars rejected (tab allowed). Names fold lowercase for dedupe.")
                + "\n"
                + VPBTranslation.T(
                    "gallery.usertags.editor_limits_hint",
                    "Up to 10 000 distinct names per paste; library holds up to 10 000 tag names; each item may have up to 100 tags.");

            _detailStripTagMenuDbActionRowGO = UI.CreateChildRT(_detailStripTagMenuDbHostGO, "ActionRow");
            UI.AddHLG(_detailStripTagMenuDbActionRowGO, spacing: UI.GapTight(s), childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: false);
            UI.AddLE(_detailStripTagMenuDbActionRowGO, minHeight: actionBtn + 2f * s, preferredHeight: actionBtn + 2f * s, flexibleWidth: 1f, flexibleHeight: 0f);

            GameObject arPadL = new GameObject("PadL");
            arPadL.transform.SetParent(_detailStripTagMenuDbActionRowGO.transform, false);
            UI.AddLE(arPadL, minWidth: 0f, flexibleWidth: 1f);

            Color createCol = GalleryUiColorTokens.AccentConfirm;
            Color removeCol = GalleryUiColorTokens.AccentDanger;
            Color mergeCol = GalleryUiColorTokens.ActiveUtility;
            Color ioImpCol = GalleryUiColorTokens.ActiveOn;
            Color ioExpCol = GalleryUiColorTokens.ActiveSecondary;
            Sprite sprPlus = UI.LoadIconSprite("tag-plus", Color.white);
            Sprite sprMinus = UI.LoadIconSprite("tag-minus", Color.white);
            Sprite sprMerge = UI.LoadIconSprite("arrow-merge", Color.white);
            Sprite sprImp = UI.LoadIconSprite("file-import", Color.white);
            Sprite sprExp = UI.LoadIconSprite("file-export", Color.white);
            Sprite sprInfo = UI.LoadIconSprite("info-square", Color.white);
            Sprite sprRename = UI.LoadIconSprite("pencil-check", Color.white);
            Sprite sprCat = UI.LoadIconSprite("category-2", Color.white);

            GameObject createTagsBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprPlus, UserTagEditorOnCreateTagsClicked, createCol, iconPadAction);
            AddTooltipPlain(createTagsBtn, VPBTranslation.T("gallery.usertags.editor_create_tags_tip", "Create tag rows: one tag per line (newline split). Blank lines skipped; trim line ends."));
            GameObject removeSelBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprMinus, UserTagEditorRemoveSelectedFromDb, removeCol, iconPadAction);
            AddTooltipPlain(removeSelBtn, VPBTranslation.T("gallery.usertags.editor_remove_selected_tip", "Delete selected tag(s) from the database for all items (cannot undo)."));
            GameObject mergeBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprMerge, UserTagEditorOpenMergeDialog, mergeCol, iconPadAction);
            AddTooltipPlain(mergeBtn, VPBTranslation.T("gallery.usertags.editor_merge_tip", "Merge selected tags into one name (opens confirmation)."));
            Color renameCol = GalleryUiColorTokens.ActiveUtility;
            GameObject renameBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprRename, UserTagEditorOpenRenameDialog, renameCol, iconPadAction);
            AddTooltipPlain(renameBtn, VPBTranslation.T("gallery.usertags.editor_rename_tip", "Rename the selected tag (opens dialog)."));
            Color catCol = GalleryUiColorTokens.SurfaceMid;
            GameObject catBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprCat, UserTagEditorOpenCategoryDialog, catCol, iconPadAction);
            AddTooltipPlain(catBtn, VPBTranslation.T("gallery.usertags.editor_category_tip", "Assign the selected tag(s) to a color category, or create/manage categories."));
            GameObject impBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprImp, UserTagEditorBeginImportYaml, ioImpCol, iconPadAction);
            AddTooltipPlain(impBtn, VPBTranslation.T("gallery.usertags.editor_import_tip", "Import tag assignments from a YAML file (tag→items or item→tags)."));
            GameObject expBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprExp, UserTagEditorBeginExportYaml, ioExpCol, iconPadAction);
            AddTooltipPlain(expBtn, VPBTranslation.T("gallery.usertags.editor_export_tip", "Export two YAML files: tag→items and item→tags (same folder, shared base name)."));
            Color infoBackdrop = GalleryUiColorTokens.ActiveSecondary;
            GameObject infoBtn = UI.CreateSideTabSquareIconButton(_detailStripTagMenuDbActionRowGO, actionBtn, sprInfo, null, infoBackdrop, iconPadAction);
            AddTooltipPlain(infoBtn, userTagEditorInfoWisdom);

            GameObject arPadR = new GameObject("PadR");
            arPadR.transform.SetParent(_detailStripTagMenuDbActionRowGO.transform, false);
            UI.AddLE(arPadR, minWidth: 0f, flexibleWidth: 1f);

            // Nested modals on tag-menu root (same canvas freedom as parent window).
            UserTagEditorBuildNameDialog(
                _detailStripTagMenuRoot.transform, "UserTagEditorMergeModal", "MergeDialogPanel", "MergeDialogInput", "MergeDialogButtons",
                VPBTranslation.T("gallery.usertags.editor_merge_dialog_title", "Merge tags into…"), "MergeTitle",
                VPBTranslation.T("gallery.usertags.editor_merge_ph", "New tag name…"),
                VPBTranslation.T("gallery.usertags.editor_merge_cancel", "Cancel"),
                VPBTranslation.T("gallery.usertags.editor_merge_confirm", "Merge"),
                editorType.Prose, bodyFont, smallFont, s,
                UserTagEditorCloseMergeDialog, UserTagEditorConfirmMergeFromDialog,
                out _userTagEditorMergeModalGo, out _userTagEditorMergeModalTitleText, out _userTagEditorMergeModalInput);

            UserTagEditorBuildNameDialog(
                _detailStripTagMenuRoot.transform, "UserTagEditorRenameModal", "RenameDialogPanel", "RenameDialogInput", "RenameDialogButtons",
                VPBTranslation.T("gallery.usertags.editor_rename_dialog_title_idle", "Rename to…"), "RenameTitle",
                VPBTranslation.T("gallery.usertags.editor_rename_ph", "New tag name…"),
                VPBTranslation.T("gallery.usertags.editor_merge_cancel", "Cancel"),
                VPBTranslation.T("gallery.usertags.editor_rename_confirm", "Rename"),
                editorType.Prose, bodyFont, smallFont, s,
                UserTagEditorCloseRenameDialog, UserTagEditorConfirmRenameFromDialog,
                out _userTagEditorRenameModalGo, out _userTagEditorRenameModalTitleText, out _userTagEditorRenameModalInput);

            // Reuse title text slot for DB count when in Database mode.
            _userTagEditorTitleText = _detailStripTagMenuSelText;
            _userTagEditorRoot = _detailStripTagMenuRoot;
            _userTagEditorFilterInput = _detailStripTagMenuSearch;

            // Place before search row / create button (footer stays last).
            if (_detailStripTagMenuSearchRowGO != null)
                _detailStripTagMenuDbHostGO.transform.SetSiblingIndex(_detailStripTagMenuSearchRowGO.transform.GetSiblingIndex());
            else
                _detailStripTagMenuDbHostGO.transform.SetAsLastSibling();

            _detailStripTagMenuDbHostGO.SetActive(false);
            DetailStripSyncTagMenuUnifiedChrome(s);

            userTagsCached = false;
            CacheUserTagsSideTab();
        }

        /// <summary>Open unified tag menu in a mode (Apply or Database).</summary>
        private void DetailStripOpenTagMenu(DetailStripTagMenuMode mode)
        {
            DetailStripEnsureTagMenu();
            DetailStripEnsureTagMenuModeTabs();
            DetailStripEnsureTagMenuDatabasePane();
            try { DetailStripEnsureTagMenuColumnDropZones(); } catch { }
            if (_detailStripTagMenuRoot == null) return;

            if (_detailStripTagMenuRoot.activeSelf
                && _detailStripTagMenuMode == mode)
            {
                // Already open in requested mode — bring to front + refresh.
                _detailStripTagMenuRoot.transform.SetAsLastSibling();
                DetailStripApplyTagMenuModeUi(mode, rebuild: true);
                return;
            }

            _detailStripTagMenuFilter = "";
            _detailStripTagMenuSelectionKey = "";
            _detailStripTagMenuAppliedOrder = null;
            _detailStripTagMenuFocusIdx = -1;
            if (_detailStripTagMenuSearch != null)
            {
                try { _detailStripTagMenuSearch.text = ""; } catch { }
            }

            DetailStripLoadTagMenuSavedPosFromConfig();
            DetailStripLoadTagMenuSavedSizeFromConfig();
            if (_detailStripTagMenuSavedPos.HasValue)
                _detailStripTagMenuDragged = true;
            else
                _detailStripTagMenuDragged = false;
            DetailStripPositionTagMenu();
            if (_detailStripTagMenuSavedPos.HasValue && _detailStripTagMenuPanelRT != null)
            {
                Vector2 size = _detailStripTagMenuPanelRT.sizeDelta;
                _detailStripTagMenuPanelRT.anchoredPosition =
                    DetailStripTagMenuCenterToTopLeft(_detailStripTagMenuSavedPos.Value, size);
            }
            DetailStripClampTagMenuPanelInView();
            if (_detailStripTagMenuDragged && _detailStripTagMenuPanelRT != null)
            {
                Vector2 center = DetailStripTagMenuTopLeftToCenter(
                    _detailStripTagMenuPanelRT.anchoredPosition, _detailStripTagMenuPanelRT.sizeDelta);
                _detailStripTagMenuSavedPos = center;
                DetailStripPersistTagMenuPos(center);
            }

            _detailStripTagMenuRoot.SetActive(true);
            _detailStripTagMenuRoot.transform.SetAsLastSibling();
            DetailStripApplyTagMenuModeUi(mode, rebuild: true);
            DetailStripSyncTagMenuLayout(ChromeScale > 0f ? ChromeScale : 1f);
            DetailStripClampTagMenuPanelInView();
            try
            {
                if (_detailStripTagMenuSearch != null)
                    _detailStripTagMenuSearch.ActivateInputField();
            }
            catch { }
        }

        private void DetailStripSetTagMenuMode(DetailStripTagMenuMode mode)
        {
            if (_detailStripTagMenuRoot == null || !_detailStripTagMenuRoot.activeSelf)
            {
                DetailStripOpenTagMenu(mode);
                return;
            }
            DetailStripApplyTagMenuModeUi(mode, rebuild: true);
            // Belt: mode switch must never leave window hidden (strip refresh races).
            if (_detailStripTagMenuRoot != null && !_detailStripTagMenuRoot.activeSelf)
            {
                _detailStripTagMenuRoot.SetActive(true);
                _detailStripTagMenuRoot.transform.SetAsLastSibling();
            }
        }

        private void DetailStripApplyTagMenuModeUi(DetailStripTagMenuMode mode, bool rebuild)
        {
            if (_detailStripTagMenuRoot == null) return;

            _detailStripTagMenuMode = mode;
            bool db = mode == DetailStripTagMenuMode.Database;

            // Keep root alive for whole mode switch — no intermediate hide.
            if (!_detailStripTagMenuRoot.activeSelf)
                _detailStripTagMenuRoot.SetActive(true);

            if (db)
                DetailStripEnsureTagMenuHeightForDatabase();

            if (_detailStripTagMenuColumnsGO != null)
                _detailStripTagMenuColumnsGO.SetActive(!db);
            if (_detailStripTagMenuCreateGO != null && db)
                _detailStripTagMenuCreateGO.SetActive(false);
            if (_detailStripTagMenuDbHostGO != null)
                _detailStripTagMenuDbHostGO.SetActive(db);

            DetailStripSyncTagMenuModeTabChrome();
            DetailStripSyncTagMenuModeTipAndTitle();
            DetailStripSyncTagMenuSearchPlaceholder();

            if (!rebuild) return;

            if (db)
            {
                _detailStripTagMenuFocusIdx = -1;
                RebuildUserTagEditorRows();
            }
            else
            {
                // Drop Database rows before showing Apply — deactivate of huge hierarchy was hitching.
                if (_userTagEditorRowsParent != null)
                {
                    try { UI.DestroyAllChildren(_userTagEditorRowsParent); } catch { }
                    _userTagEditorRowVisuals.Clear();
                    _userTagEditorVisibleRows.Clear();
                }
                DetailStripSyncOpenTagMenuIfSelectionChanged(force: true);
                DetailStripUpdateTagMenuCreateButton();
            }

            if (_detailStripTagMenuRoot != null && !_detailStripTagMenuRoot.activeSelf)
            {
                _detailStripTagMenuRoot.SetActive(true);
                _detailStripTagMenuRoot.transform.SetAsLastSibling();
            }
        }

        private void DetailStripEnsureTagMenuHeightForDatabase()
        {
            if (_detailStripTagMenuPanelRT == null) return;
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            Vector2 size = _detailStripTagMenuPanelRT.sizeDelta;
            float minDbH = DetailStripTagMenuDbComfortHeightRef * s;
            if (size.y >= minDbH) return;
            size.y = Mathf.Min(minDbH, DetailStripTagMenuMaxHeightRef * s);
            float maxW = DetailStripTagMenuMaxWidthRef * s;
            float minW = DetailStripTagMenuMinWidthRef * s;
            size.x = Mathf.Clamp(size.x, minW, maxW);
            _detailStripTagMenuPanelRT.sizeDelta = size;
            DetailStripRefreshTagMenuColumnWidths(s);
            DetailStripClampTagMenuPanelInView();
        }

        private void DetailStripSyncTagMenuModeTabChrome()
        {
            bool db = _detailStripTagMenuMode == DetailStripTagMenuMode.Database;
            if (_detailStripTagMenuModeApplyImg != null)
                _detailStripTagMenuModeApplyImg.color = db ? DetailStripTagMenuModeOffCol : DetailStripTagMenuModeOnCol;
            if (_detailStripTagMenuModeDbImg != null)
                _detailStripTagMenuModeDbImg.color = db ? DetailStripTagMenuModeOnCol : DetailStripTagMenuModeOffCol;
            if (_detailStripTagMenuModeApplyText != null)
            {
                _detailStripTagMenuModeApplyText.fontStyle = db ? FontStyle.Normal : FontStyle.Bold;
                _detailStripTagMenuModeApplyText.color = UI.PopupText;
            }
            if (_detailStripTagMenuModeDbText != null)
            {
                _detailStripTagMenuModeDbText.fontStyle = db ? FontStyle.Bold : FontStyle.Normal;
                _detailStripTagMenuModeDbText.color = UI.PopupText;
            }
        }

        private void DetailStripSyncTagMenuModeTipAndTitle()
        {
            bool db = _detailStripTagMenuMode == DetailStripTagMenuMode.Database;
            if (_detailStripTagMenuTipText != null)
            {
                _detailStripTagMenuTipText.text = db
                    ? VPBTranslation.T(
                        "gallery.detail.tag_menu_db_tip",
                        "Select rows · Shift-range · toolbar create / purge / merge / rename / category / YAML")
                    : VPBTranslation.T(
                        "gallery.detail.tag_menu_tip",
                        "Click toggle · drag reorder · drop on Available to remove");
            }

            if (db)
            {
                if (!userTagsCached) CacheUserTagsSideTab();
                UserTagEditorSetTitleCount(cachedUserTagSideTab != null ? cachedUserTagSideTab.Count : 0);
                if (_detailStripTagMenuHeaderGO != null)
                {
                    Image headerImg = _detailStripTagMenuHeaderGO.GetComponent<Image>();
                    if (headerImg != null) headerImg.color = DetailStripTagMenuTitleBarBg;
                }
            }
            else
            {
                DetailStripUpdateTagMenuSelectionChrome();
            }
        }

        private void DetailStripSyncTagMenuSearchPlaceholder()
        {
            if (_detailStripTagMenuSearch == null) return;
            Text ph = _detailStripTagMenuSearch.placeholder as Text;
            if (ph == null) return;
            ph.text = _detailStripTagMenuMode == DetailStripTagMenuMode.Database
                ? VPBTranslation.T("gallery.usertags.editor_filter_ph", "Filter list…")
                : VPBTranslation.T("gallery.detail.tag_search", "Filter Available / create…");
        }

        private bool DetailStripTagMenuIsDatabaseMode()
        {
            return _detailStripTagMenuMode == DetailStripTagMenuMode.Database
                && _detailStripTagMenuRoot != null
                && _detailStripTagMenuRoot.activeSelf;
        }

        /// <summary>Shared filter path for Apply + Database list rebuilds.</summary>
        private void DetailStripOnTagMenuFilterChanged(string val)
        {
            _detailStripTagMenuFilter = val ?? "";
            DetailStripRefreshTagMenuFilterClearVisible();
            if (DetailStripTagMenuIsDatabaseMode())
            {
                // Debounce — RebuildUserTagEditorRows destroys/recreates UI (warm hitch if per-key).
                DetailStripScheduleTagMenuFilterRebuild();
                return;
            }
            DetailStripUpdateTagMenuCreateButton();
            DetailStripScheduleTagMenuFilterRebuild();
        }

        private void DetailStripOnTagMenuFilterCleared()
        {
            _detailStripTagMenuFilter = "";
            DetailStripRefreshTagMenuFilterClearVisible();
            DetailStripStopTagMenuFilterRebuild();
            if (DetailStripTagMenuIsDatabaseMode())
            {
                RebuildUserTagEditorRows();
                return;
            }
            DetailStripUpdateTagMenuCreateButton();
            DetailStripRebuildTagMenuList(fullLayoutSync: false);
        }

        private Transform DetailStripTagMenuCategoryModalHost()
        {
            // Prefer tag-menu panel so dim + dialog center on floating window (not full canvas).
            if (_detailStripTagMenuPanelGO != null) return _detailStripTagMenuPanelGO.transform;
            if (_detailStripTagMenuRoot != null) return _detailStripTagMenuRoot.transform;
            if (_userTagEditorRoot != null) return _userTagEditorRoot.transform;
            return backgroundBoxGO != null ? backgroundBoxGO.transform : null;
        }
    }
}
