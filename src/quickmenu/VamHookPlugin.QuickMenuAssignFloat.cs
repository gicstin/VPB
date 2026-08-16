using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class VamHookPlugin
    {
        private static readonly Color QmAssignFloatTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color QmAssignFloatFooterBarBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color QmAssignFloatPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color QmAssignFloatScrollBg = GalleryUiColorTokens.ModalSurface;
        private static readonly Color QmAssignFloatGroupActive = GalleryUiColorTokens.SurfaceMid;
        private static readonly Color QmAssignFloatGroupInactive = GalleryUiColorTokens.SurfacePanel;
        private static readonly Color QmAssignFloatFilterBg = GalleryUiColorTokens.SurfaceDarker;
        private static readonly Color QmAssignFloatRowBg = GalleryUiColorTokens.SurfaceDarker;

        private const float QmAssignFloatGroupTabPadRef = 6f;
        private const float QmAssignFloatGroupTabRowGapRef = 4f;
        private const float QmAssignFloatGroupChipIconSizeRef = 16f;
        private const float QmAssignFloatGroupChipIconGapRef = 4f;
        private const float QmAssignFloatGroupChipPadLRef = 6f;
        private const float QmAssignFloatGroupChipPadRRef = 8f;
        private const float QmAssignFloatRowsSpacingRef = 2f;
        private const float QmAssignFloatRowsPadRef = 6f;
        private const float QmAssignFloatChromeScale = 1f;

        private const string QmAssignGroupAll = "all";
        private const string QmAssignGroupCore = "core";
        private const string QmAssignGroupGallery = "gallery";
        private const string QmAssignGroupRandom = "random";
        private const string QmAssignGroupCategory = "category";
        private const string QmAssignGroupScene = "scene";
        private const string QmAssignGroupPerf = "perf";
        private const string QmAssignGroupOutfit = "outfit";

        private struct QmAssignCatalogEntry
        {
            public QuickMenuAssignableAction Action;
            public string Group;
            public QmAssignCatalogEntry(QuickMenuAssignableAction action, string group)
            {
                Action = action;
                Group = group;
            }
        }

        private static readonly QmAssignCatalogEntry[] QmAssignCatalog = new QmAssignCatalogEntry[]
        {
            new QmAssignCatalogEntry(QuickMenuAssignableAction.None, QmAssignGroupCore),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.CoreSettingsButton, QmAssignGroupCore),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.CorePageButton, QmAssignGroupCore),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.PageNext, QmAssignGroupCore),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.PagePrev, QmAssignGroupCore),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.SwitchWatchHand, QmAssignGroupCore),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.CreateGallery, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.ShowHide, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.BringFront, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.CloseAll, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.AutoHideGallery, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.ShowHiddenPackages, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.ToggleImportSidebar, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.StarFilter, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.History, QmAssignGroupGallery),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.Random, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomScenes, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomSubScenes, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomClothing, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomHair, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomPose, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomAppearance, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomSkin, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RandomSceneImport, QmAssignGroupRandom),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategoryScenes, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategorySubScenes, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategoryClothing, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategoryHair, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategoryPose, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategoryAppearance, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategoryPlugins, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategorySkin, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.OpenCategoryAll, QmAssignGroupCategory),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.Save, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.Undo, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.Redo, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.Hub, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.Cleanup, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.CreatorMode, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.TargetAtom, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.CompressCache, QmAssignGroupScene),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.FpsCounter, QmAssignGroupPerf),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.PerfMode, QmAssignGroupPerf),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.PerfStepUp, QmAssignGroupPerf),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.PerfStepDown, QmAssignGroupPerf),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.ReplaceAddToggle, QmAssignGroupOutfit),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RemoveAllClothing, QmAssignGroupOutfit),
            new QmAssignCatalogEntry(QuickMenuAssignableAction.RemoveAllHair, QmAssignGroupOutfit),
        };

        private RectTransform m_QmAssignFloatTitleBarRT;
        private Transform m_QmAssignFloatRowsParent;
        private Transform m_QmAssignFloatGroupTabsParent;
        private RectTransform m_QmAssignFloatGroupTabsContentRT;
        private ScrollRect m_QmAssignFloatScrollRect;
        private InputField m_QmAssignFloatFilterInput;
        private UnityAction<string> m_QmAssignFloatFilterOnValueChanged;
        private GameObject m_QmAssignFloatFilterClearGo;
        private GameObject m_QmAssignFloatFilterRow;
        private GameObject m_QmAssignFloatGroupTabsRow;
        private GameObject m_QmAssignFloatScrollHost;
        private GameObject m_QmAssignFloatFooter;
        private GameObject m_QmAssignFloatCollapseBtn;
        private Image m_QmAssignFloatCollapseIcon;
        private Text m_QmAssignFloatHintText;
        private GameObject m_QmAssignFloatEmptyGo;
        private Text m_QmAssignFloatEmptyText;
        private CanvasGroup m_QmAssignFloatCanvasGroup;
        private readonly List<QmAssignCatalogEntry> m_QmAssignFloatRowEntries = new List<QmAssignCatalogEntry>(64);
        private float m_QmAssignFloatGroupTabsH;
        private Vector2? m_QmAssignFloatSavedPosCenter;
        private Vector2? m_QmAssignFloatSavedSizeRef;
        private float? m_QmAssignFloatExpandHeightRef;
        private bool m_QmAssignFloatCollapsed;
        private Vector2? m_QmAssignFloatCollapsedTopLeftPos;
        private string m_QmAssignFloatFilter = "";
        private string m_QmAssignFloatGroup = QmAssignGroupAll;
        private bool m_QmAssignFloatPlaced;

        private void QuickMenuBuildAssignFloat(GameObject host)
        {
            if (host == null) return;
            float s = QmAssignFloatChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float searchH = GalleryUiDesignTokens.SearchFieldHeightRef * s;

            QuickMenuLoadAssignFloatGeometryFromConfig();
            float panelWRef = GalleryUiDesignTokens.QmAssignFloatDefaultWidthRef;
            float panelHRef = GalleryUiDesignTokens.QmAssignFloatDefaultHeightRef;
            if (m_QmAssignFloatSavedSizeRef.HasValue)
            {
                panelWRef = Mathf.Clamp(
                    m_QmAssignFloatSavedSizeRef.Value.x,
                    GalleryUiDesignTokens.QmAssignFloatMinWidthRef,
                    GalleryUiDesignTokens.QmAssignFloatMaxWidthRef);
                panelHRef = Mathf.Clamp(
                    m_QmAssignFloatSavedSizeRef.Value.y,
                    GalleryUiDesignTokens.QmAssignFloatMinHeightRef,
                    GalleryUiDesignTokens.QmAssignFloatMaxHeightRef);
            }
            float panelW = panelWRef * s;
            float panelH = panelHRef * s;

            GameObject panel = UI.CreateChildRT(
                host, "VPB_QM_AssignFloat", AnchorPresets.middleCenter,
                new Vector2(panelW, panelH), Vector2.zero);
            UI.AddImage(panel, QmAssignFloatPanelBg);
            if (panel.GetComponent<RectMask2D>() == null)
                panel.AddComponent<RectMask2D>();
            m_QmAssignFloatCanvasGroup = panel.AddComponent<CanvasGroup>();
            m_QuickMenuAssignPopupRoot = panel;
            m_QuickMenuAssignPopupRT = panel.GetComponent<RectTransform>();
            if (m_QuickMenuAssignPopupRT != null)
            {
                m_QuickMenuAssignPopupRT.pivot = new Vector2(0f, 1f);
                m_QuickMenuAssignPopupRT.anchorMin = new Vector2(0.5f, 0.5f);
                m_QuickMenuAssignPopupRT.anchorMax = new Vector2(0.5f, 0.5f);
                m_QuickMenuAssignPopupRT.sizeDelta = new Vector2(panelW, panelH);
                Vector2 center = m_QmAssignFloatSavedPosCenter.HasValue
                    ? m_QmAssignFloatSavedPosCenter.Value
                    : Vector2.zero;
                m_QuickMenuAssignPopupRT.anchoredPosition = QmAssignFloatCenterToTopLeft(center, m_QuickMenuAssignPopupRT.sizeDelta);
                m_QmAssignFloatPlaced = m_QmAssignFloatSavedPosCenter.HasValue;
            }

            GameObject titleBar = UI.CreateChildRT(panel, "TitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            Image titleBg = UI.AddImage(titleBar, QmAssignFloatTitleBarBg);
            if (titleBg != null) titleBg.raycastTarget = true;
            m_QmAssignFloatTitleBarRT = titleBar.GetComponent<RectTransform>();
            if (m_QmAssignFloatTitleBarRT != null)
            {
                m_QmAssignFloatTitleBarRT.pivot = new Vector2(0.5f, 1f);
                m_QmAssignFloatTitleBarRT.anchoredPosition = Vector2.zero;
                m_QmAssignFloatTitleBarRT.sizeDelta = new Vector2(0f, titleH);
            }
            HorizontalLayoutGroup titleHlg = UI.AddHLG(
                titleBar, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (titleBar.GetComponent<RectMask2D>() == null)
                titleBar.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(titleBar, "\u2807", font,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.ApplyFloatTitleBarMetrics(titleHlg, grip.gameObject, s);

            float winIconSz = GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s;
            UI.CreateFloatTitleWindowIcon(titleBar, "square-plus-2", winIconSz);

            Text title = UI.CreateEmphasisTitleLabel(
                titleBar,
                VPBTranslation.T("hook.qmassign.float_title", "Assignable Buttons"),
                font, Color.white, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(title.gameObject, flexibleWidth: 1f, minWidth: 60f * s);

            m_QmAssignFloatCollapseBtn = UI.CreateFloatChromeIconButton(
                titleBar.transform, chromeSz, "chevron-up",
                GalleryUiColorTokens.ChromeIconWell, QuickMenuToggleAssignFloatCollapsed);
            if (m_QmAssignFloatCollapseBtn != null)
            {
                m_QmAssignFloatCollapseBtn.name = "CollapseBtn";
                Transform iconTr = m_QmAssignFloatCollapseBtn.transform.Find("Icon");
                m_QmAssignFloatCollapseIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }

            GameObject closeBtn = UI.CreateFloatChromeIconButton(
                titleBar.transform, chromeSz, "x",
                GalleryUiColorTokens.ChromeIconWell, QuickMenuCloseAssignFloat);
            if (closeBtn != null) closeBtn.name = "TitleClose";

            var headerDrag = titleBar.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = m_QuickMenuAssignPopupRT;
            // HUD canvas rect is ~100px; VR parent-rect clamp would pin this to origin.
            headerDrag.ClampPositionInVr = false;
            headerDrag.OnMoved = OnQmAssignFloatMoved;

            m_QmAssignFloatFilterRow = UI.CreateChildRT(panel, "FilterRow", AnchorPresets.hStretchTop,
                new Vector2(0f, filterH), new Vector2(0f, -titleH));
            RectTransform filterRT = m_QmAssignFloatFilterRow.GetComponent<RectTransform>();
            if (filterRT != null)
            {
                filterRT.pivot = new Vector2(0.5f, 1f);
                filterRT.sizeDelta = new Vector2(0f, filterH);
                filterRT.anchoredPosition = new Vector2(0f, -titleH);
            }
            UI.AddHLG(m_QmAssignFloatFilterRow, spacing: 0f,
                padding: UI.Pad(
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef,
                    GalleryUiDesignTokens.FloatSearchRowPadRef, s),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);

            m_QmAssignFloatFilterInput = UI.CreateChromeLayoutInputField(
                m_QmAssignFloatFilterRow.transform,
                font,
                searchH,
                1f,
                8f * s,
                2f * s,
                QmAssignFloatFilterBg,
                UI.InputFieldPlaceholderColor,
                VPBTranslation.T("hook.qmassign.filter", "Filter actions…"),
                "AssignFilter");
            if (m_QmAssignFloatFilterInput != null)
            {
                UI.LayoutChromeSearchIcon(m_QmAssignFloatFilterInput.gameObject, s);
                float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                Transform textAreaTr = m_QmAssignFloatFilterInput.transform.Find("TextArea");
                if (textAreaTr != null)
                {
                    RectTransform taRt = textAreaTr as RectTransform;
                    if (taRt != null)
                        taRt.offsetMax = new Vector2(-clearSz, taRt.offsetMax.y);
                }

                m_QmAssignFloatFilterClearGo = UI.CreateUIButton(
                    m_QmAssignFloatFilterInput.gameObject,
                    clearSz, searchH, "X", 24, 0, 0, AnchorPresets.middleRight,
                    () =>
                    {
                        QuickMenuSetAssignFloatFilterTextWithoutNotify("");
                        m_QmAssignFloatFilter = "";
                        QuickMenuApplyAssignFloatRowFilter();
                        try
                        {
                            m_QmAssignFloatFilterInput.ActivateInputField();
                            m_QmAssignFloatFilterInput.MoveTextEnd(false);
                        }
                        catch { }
                        QuickMenuRefreshAssignFloatFilterClearVisible();
                    });
                if (m_QmAssignFloatFilterClearGo != null)
                {
                    m_QmAssignFloatFilterClearGo.name = "FilterClear";
                    RectTransform clearRT = m_QmAssignFloatFilterClearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                    {
                        clearRT.anchorMin = new Vector2(1f, 0f);
                        clearRT.anchorMax = new Vector2(1f, 1f);
                        clearRT.pivot = new Vector2(1f, 0.5f);
                        clearRT.anchoredPosition = Vector2.zero;
                        clearRT.sizeDelta = new Vector2(clearSz, 0f);
                    }
                    Image clearBg = m_QmAssignFloatFilterClearGo.GetComponent<Image>();
                    if (clearBg != null) clearBg.color = new Color(0f, 0f, 0f, 0f);
                    try
                    {
                        Sprite xSpr = UI.LoadIconSprite("x", GalleryUiColorTokens.SearchClearIconTint);
                        if (xSpr != null)
                            UI.AddIconToButton(m_QmAssignFloatFilterClearGo, xSpr, 6f * s, new Color(0f, 0f, 0f, 0f));
                    }
                    catch { }
                    Text clearLabel = m_QmAssignFloatFilterClearGo.GetComponentInChildren<Text>(true);
                    if (clearLabel != null) clearLabel.text = "";
                    var clearHover = m_QmAssignFloatFilterClearGo.AddComponent<UIHoverBorder>();
                    clearHover.hoverColor = new Color(1f, 0.2f, 0.2f, 1f);
                    clearHover.borderSize = 2f;
                    clearHover.inward = true;
                }

                m_QmAssignFloatFilterOnValueChanged = val =>
                {
                    m_QmAssignFloatFilter = val ?? "";
                    QuickMenuRefreshAssignFloatFilterClearVisible();
                    QuickMenuApplyAssignFloatRowFilter();
                };
                m_QmAssignFloatFilterInput.onValueChanged.AddListener(m_QmAssignFloatFilterOnValueChanged);
                try
                {
                    m_QmAssignFloatFilterInput.gameObject.AddComponent<CtrlBackspaceWordDeleteHandler>()
                        .Initialize(m_QmAssignFloatFilterInput);
                }
                catch { }
                QuickMenuRefreshAssignFloatFilterClearVisible();
            }

            m_QmAssignFloatGroupTabsH = chromeSz + 8f * s;
            m_QmAssignFloatGroupTabsRow = UI.CreateChildRT(panel, "GroupTabs", AnchorPresets.hStretchTop,
                new Vector2(0f, m_QmAssignFloatGroupTabsH), new Vector2(0f, -(titleH + filterH)));
            RectTransform groupRT = m_QmAssignFloatGroupTabsRow.GetComponent<RectTransform>();
            if (groupRT != null)
            {
                groupRT.pivot = new Vector2(0.5f, 1f);
                groupRT.sizeDelta = new Vector2(0f, m_QmAssignFloatGroupTabsH);
                groupRT.anchoredPosition = new Vector2(0f, -(titleH + filterH));
            }
            UI.AddImage(m_QmAssignFloatGroupTabsRow, GalleryUiColorTokens.SurfaceDarker);
            GameObject groupContent = new GameObject("Content");
            groupContent.transform.SetParent(m_QmAssignFloatGroupTabsRow.transform, false);
            m_QmAssignFloatGroupTabsContentRT = groupContent.AddComponent<RectTransform>();
            m_QmAssignFloatGroupTabsContentRT.anchorMin = new Vector2(0f, 1f);
            m_QmAssignFloatGroupTabsContentRT.anchorMax = new Vector2(1f, 1f);
            m_QmAssignFloatGroupTabsContentRT.pivot = new Vector2(0f, 1f);
            m_QmAssignFloatGroupTabsContentRT.anchoredPosition = Vector2.zero;
            float pad = QmAssignFloatGroupTabPadRef * s;
            m_QmAssignFloatGroupTabsContentRT.offsetMin = new Vector2(pad, -m_QmAssignFloatGroupTabsH);
            m_QmAssignFloatGroupTabsContentRT.offsetMax = new Vector2(-pad, -pad);
            m_QmAssignFloatGroupTabsParent = groupContent.transform;

            m_QmAssignFloatFooter = UI.CreateChildRT(panel, "Footer", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            UI.AddImage(m_QmAssignFloatFooter, QmAssignFloatFooterBarBg);
            RectTransform footerRT = m_QmAssignFloatFooter.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                m_QmAssignFloatFooter, spacing: 6f * s, padding: UI.Pad(8, 8, 4, 4, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);
            if (m_QmAssignFloatFooter.GetComponent<RectMask2D>() == null)
                m_QmAssignFloatFooter.AddComponent<RectMask2D>();

            GameObject footerDragArea = UI.CreateFloatFooterDragArea(m_QmAssignFloatFooter);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = m_QuickMenuAssignPopupRT;
                footerDrag.ClampPositionInVr = false;
                footerDrag.OnMoved = OnQmAssignFloatMoved;
            }

            m_QmAssignFloatHintText = UI.CreateLabel(
                m_QmAssignFloatFooter,
                VPBTranslation.T("hook.qmassign.float_hint", "Drag an action onto a slot"),
                font, GalleryUiColorTokens.TextDim, TextAnchor.MiddleLeft,
                raycastTarget: false, name: "Hint");
            GalleryUiMetrics.ApplyFont(m_QmAssignFloatHintText, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            if (m_QmAssignFloatHintText != null)
            {
                m_QmAssignFloatHintText.horizontalOverflow = HorizontalWrapMode.Overflow;
                m_QmAssignFloatHintText.verticalOverflow = VerticalWrapMode.Truncate;
            }
            UI.AddLE(m_QmAssignFloatHintText.gameObject, flexibleWidth: 1f, minWidth: 24f * s);

            GameObject resizeHandle = UI.AddChildGOImage(
                m_QmAssignFloatFooter, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                chromeSz, chromeSz, Vector2.zero, rounded: true);
            resizeHandle.name = "ResizeHandle";
            Image rhImg = resizeHandle.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            UI.EnsureFloatChromeHoverBorder(resizeHandle);
            LayoutElement rhLe = resizeHandle.GetComponent<LayoutElement>();
            if (rhLe == null) rhLe = resizeHandle.AddComponent<LayoutElement>();
            rhLe.minWidth = rhLe.preferredWidth = chromeSz;
            rhLe.minHeight = rhLe.preferredHeight = chromeSz;
            rhLe.flexibleWidth = 0f;
            try
            {
                Sprite rhSpr = UI.LoadIconSprite("chevrons-down-right", UI.BarIconGlyphTint);
                if (rhSpr != null)
                    UI.AddIconToButton(resizeHandle, rhSpr, 5f * s, UI.IconButtonBackdrop);
            }
            catch { }
            var resizer = resizeHandle.AddComponent<UIFloatPanelResize>();
            resizer.Target = m_QuickMenuAssignPopupRT;
            resizer.GetMinSize = () => new Vector2(
                GalleryUiDesignTokens.QmAssignFloatMinWidthRef * QmAssignFloatChromeScale,
                GalleryUiDesignTokens.QmAssignFloatMinHeightRef * QmAssignFloatChromeScale);
            resizer.GetMaxSize = () => new Vector2(
                GalleryUiDesignTokens.QmAssignFloatMaxWidthRef * QmAssignFloatChromeScale,
                GalleryUiDesignTokens.QmAssignFloatMaxHeightRef * QmAssignFloatChromeScale);
            resizer.OnResized = OnQmAssignFloatResized;

            m_QmAssignFloatScrollHost = UI.CreateChildRT(panel, "ScrollHost", AnchorPresets.stretchAll);
            RectTransform scrollRT = m_QmAssignFloatScrollHost.GetComponent<RectTransform>();
            if (scrollRT != null)
            {
                scrollRT.offsetMin = new Vector2(0f, footerH);
                scrollRT.offsetMax = new Vector2(0f, -(titleH + filterH + m_QmAssignFloatGroupTabsH));
            }
            UI.AddImage(m_QmAssignFloatScrollHost, QmAssignFloatScrollBg);
            if (m_QmAssignFloatScrollHost.GetComponent<RectMask2D>() == null)
                m_QmAssignFloatScrollHost.AddComponent<RectMask2D>();

            float sbW = GalleryUiDesignTokens.QuickFiltersScrollBarWidthRef * s;
            m_QmAssignFloatScrollRect = m_QmAssignFloatScrollHost.AddComponent<ScrollRect>();
            m_QmAssignFloatScrollRect.horizontal = false;
            m_QmAssignFloatScrollRect.vertical = true;
            m_QmAssignFloatScrollRect.movementType = ScrollRect.MovementType.Clamped;
            m_QmAssignFloatScrollRect.scrollSensitivity =
                VpbScrollTuning.Sensitivity(GalleryUiDesignTokens.SettingsFloatScrollSensitivityRef, 1f);
            m_QmAssignFloatScrollRect.verticalScrollbar = null;

            GameObject viewport = UI.CreateChildRT(m_QmAssignFloatScrollHost, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRt = viewport.GetComponent<RectTransform>();
            if (vpRt != null) vpRt.offsetMax = new Vector2(-sbW, 0f);
            viewport.AddComponent<RectMask2D>();
            m_QmAssignFloatScrollRect.viewport = vpRt;

            GameObject scrollbarGO = UI.CreateScrollBar(m_QmAssignFloatScrollHost, sbW, 0f, Scrollbar.Direction.BottomToTop);
            RectTransform sbRT = scrollbarGO.GetComponent<RectTransform>();
            if (sbRT != null)
            {
                sbRT.anchorMin = new Vector2(1f, 0f);
                sbRT.anchorMax = new Vector2(1f, 1f);
                sbRT.pivot = new Vector2(1f, 0.5f);
                sbRT.offsetMin = new Vector2(-sbW, 0f);
                sbRT.offsetMax = Vector2.zero;
            }
            ScrollbarSync sync = scrollbarGO.AddComponent<ScrollbarSync>();
            sync.scrollRect = m_QmAssignFloatScrollRect;
            sync.scrollbar = scrollbarGO.GetComponent<Scrollbar>();
            sync.minSizePixels = 20f;

            GameObject content = UI.CreateChildRT(viewport, "Content", AnchorPresets.hStretchTop);
            RectTransform contentRt = content.GetComponent<RectTransform>();
            m_QmAssignFloatScrollRect.content = contentRt;
            VerticalLayoutGroup cv = UI.AddVLG(
                content,
                spacing: QmAssignFloatRowsSpacingRef * s,
                padding: UI.Pad(QmAssignFloatRowsPadRef, QmAssignFloatRowsPadRef, QmAssignFloatRowsPadRef, QmAssignFloatRowsPadRef, s));
            cv.childForceExpandHeight = false;
            cv.childForceExpandWidth = true;
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            m_QmAssignFloatRowsParent = content.transform;

            m_QmAssignFloatEmptyGo = UI.CreateChildRT(m_QmAssignFloatScrollHost, "Empty", AnchorPresets.stretchAll);
            m_QmAssignFloatEmptyText = UI.CreateLabel(
                m_QmAssignFloatEmptyGo,
                VPBTranslation.T("hook.qmassign.filter_empty", "No matching actions"),
                font, GalleryUiColorTokens.TextMuted, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "EmptyText");
            m_QmAssignFloatEmptyGo.SetActive(false);

            m_QmAssignFloatCollapsed = false;
            m_QmAssignFloatExpandHeightRef = panelHRef;
            QuickMenuRebuildAssignFloatGroupTabs(font, s, chromeSz);
            QuickMenuRebuildAssignFloatRows(font, s);
            try { UI.ApplyFloatRootHoverPolicy(m_QuickMenuAssignPopupRoot); } catch { }
            m_QuickMenuAssignPopupRoot.SetActive(false);
        }

        private void QuickMenuHideAssignPopup()
        {
            m_QuickMenuAssignPopupTargetIdx = -1;
            m_QuickMenuSavePopupTargetIdx = -1;
            m_QuickMenuSavePopupPanel = null;
            QuickMenuCaptureAssignFloatGeometryToMemory();
            QuickMenuPersistAssignFloatGeometry();
            if (m_QuickMenuSavePopupRoot != null && m_QuickMenuSavePopupRoot.activeSelf)
                m_QuickMenuSavePopupRoot.SetActive(false);
            if (m_QuickMenuAssignPopupRoot != null && m_QuickMenuAssignPopupRoot.activeSelf)
                m_QuickMenuAssignPopupRoot.SetActive(false);
            QuickMenuSetAssignFloatRaycasts(true);
        }

        private void QuickMenuCloseAssignFloat()
        {
            QuickMenuHideAssignPopup();
            if (!m_QuickMenuEditMode) return;
            m_QuickMenuEditMode = false;
            for (int k = 0; k < QuickMenuGridSlotCount; k++) QuickMenuRefreshSlotVisual(k);
        }

        private void QuickMenuShowAssignPopup(int slotIdx, Vector2 anchoredPos)
        {
            if (m_QuickMenuAssignPopupRoot == null || m_QuickMenuAssignPopupRT == null) return;
            if (slotIdx >= 0 && slotIdx <= 3) return;
            m_QuickMenuAssignPopupTargetIdx = slotIdx;
            m_QuickMenuSavePopupTargetIdx = -1;
            m_QuickMenuSavePopupPanel = null;
            QuickMenuPresentAssignFloat(anchoredPos);
            QuickMenuRefreshAssignFloatHint();
        }

        private void QuickMenuShowAssignPaletteForEditMode()
        {
            if (m_QuickMenuAssignPopupRoot == null || m_QuickMenuAssignPopupRT == null) return;
            m_QuickMenuAssignPopupTargetIdx = -1;
            m_QuickMenuSavePopupTargetIdx = -1;
            m_QuickMenuSavePopupPanel = null;

            Vector2 pos = Vector2.zero;
            try
            {
                if (m_QuickMenuGridButtonRTs != null && m_QuickMenuEditSlotIdx >= 0 && m_QuickMenuEditSlotIdx < m_QuickMenuGridButtonRTs.Length && m_QuickMenuGridButtonRTs[m_QuickMenuEditSlotIdx] != null)
                    pos = m_QuickMenuGridButtonRTs[m_QuickMenuEditSlotIdx].anchoredPosition + new Vector2(60f, 0f);
            }
            catch { }
            QuickMenuPresentAssignFloat(pos + QuickMenuPopupOffset);
            QuickMenuRefreshAssignFloatHint();
            QuickMenuSetTooltip(VPBTranslation.T("hook.qmtooltip.drag_assign", "Edit mode: drag an action from the list and drop it on a slot to assign."));
        }

        private void QuickMenuPresentAssignFloat(Vector2 fallbackTopLeft)
        {
            if (m_QuickMenuAssignPopupRoot == null || m_QuickMenuAssignPopupRT == null) return;
            if (m_QmAssignFloatCollapsed)
                QuickMenuToggleAssignFloatCollapsed();

            if (m_QmAssignFloatSavedPosCenter.HasValue)
            {
                m_QuickMenuAssignPopupRT.anchoredPosition = QmAssignFloatCenterToTopLeft(
                    m_QmAssignFloatSavedPosCenter.Value, m_QuickMenuAssignPopupRT.sizeDelta);
                m_QmAssignFloatPlaced = true;
            }
            else if (!m_QmAssignFloatPlaced)
            {
                m_QuickMenuAssignPopupRT.anchoredPosition = fallbackTopLeft;
                m_QmAssignFloatPlaced = true;
                QuickMenuCaptureAssignFloatGeometryToMemory();
                QuickMenuPersistAssignFloatGeometry();
            }

            if (!m_QuickMenuAssignPopupRoot.activeSelf) m_QuickMenuAssignPopupRoot.SetActive(true);
            try { m_QuickMenuAssignPopupRoot.transform.SetAsLastSibling(); } catch { }
            float s = QmAssignFloatChromeScale;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            try { QuickMenuRelayoutAssignFloatGroupTabs(s, chromeSz); } catch { }
        }

        private void QuickMenuRebuildAssignFloatGroupTabs(int font, float s, float chromeSz)
        {
            if (m_QmAssignFloatGroupTabsParent == null) return;
            for (int c = m_QmAssignFloatGroupTabsParent.childCount - 1; c >= 0; c--)
            {
                try
                {
                    GameObject go = m_QmAssignFloatGroupTabsParent.GetChild(c).gameObject;
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
                catch { }
            }

            AddQmAssignGroupChip(QmAssignGroupAll, VPBTranslation.T("hook.qmassign.group.all", "All"), "apps", font, s, chromeSz);
            AddQmAssignGroupChip(QmAssignGroupCore, VPBTranslation.T("hook.qmassign.group.core", "Core"), "settings", font, s, chromeSz);
            AddQmAssignGroupChip(QmAssignGroupGallery, VPBTranslation.T("hook.qmassign.group.gallery", "Gallery"), "layout-grid", font, s, chromeSz);
            AddQmAssignGroupChip(QmAssignGroupRandom, VPBTranslation.T("hook.qmassign.group.random", "Random"), "dice-3", font, s, chromeSz);
            AddQmAssignGroupChip(QmAssignGroupCategory, VPBTranslation.T("hook.qmassign.group.category", "Category"), "category-2", font, s, chromeSz);
            AddQmAssignGroupChip(QmAssignGroupScene, VPBTranslation.T("hook.qmassign.group.scene", "Scene"), "masks-theater", font, s, chromeSz);
            AddQmAssignGroupChip(QmAssignGroupPerf, VPBTranslation.T("hook.qmassign.group.perf", "Perf"), "gauge", font, s, chromeSz);
            AddQmAssignGroupChip(QmAssignGroupOutfit, VPBTranslation.T("hook.qmassign.group.outfit", "Outfit"), "shirt", font, s, chromeSz);

            QuickMenuRelayoutAssignFloatGroupTabs(s, chromeSz);
            try { UI.ApplyFloatRootHoverPolicy(m_QmAssignFloatGroupTabsParent.gameObject); } catch { }
        }

        private void AddQmAssignGroupChip(string key, string label, string iconRole, int font, float s, float chromeSz)
        {
            bool active = string.Equals(m_QmAssignFloatGroup, key, StringComparison.OrdinalIgnoreCase);
            Color bg = active ? QmAssignFloatGroupActive : QmAssignFloatGroupInactive;
            string capturedKey = key;
            GameObject btn = UI.CreateChromeLayoutButton(
                m_QmAssignFloatGroupTabsParent, 0f, chromeSz, label, font, bg,
                () =>
                {
                    m_QmAssignFloatGroup = capturedKey;
                    QuickMenuRebuildAssignFloatGroupTabs(font, s, chromeSz);
                    QuickMenuApplyAssignFloatRowFilter();
                });
            if (btn == null) return;
            LayoutElement le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.AddComponent<LayoutElement>();
            le.flexibleWidth = 0f;
            le.minWidth = 48f * s;
            le.preferredHeight = chromeSz;
            le.minHeight = chromeSz;
            bool hasIcon = ApplyQmAssignGroupChipIcon(btn, iconRole, s);
            Text chipTxt = btn.GetComponentInChildren<Text>(true);
            float textW = chipTxt != null ? chipTxt.preferredWidth : 0f;
            float chromeW = hasIcon
                ? (QmAssignFloatGroupChipPadLRef + QmAssignFloatGroupChipIconSizeRef
                    + QmAssignFloatGroupChipIconGapRef + QmAssignFloatGroupChipPadRRef) * s
                : 16f * s;
            le.preferredWidth = Mathf.Max(48f * s, textW + chromeW);
        }

        private static bool ApplyQmAssignGroupChipIcon(GameObject btn, string iconRole, float s)
        {
            if (btn == null || string.IsNullOrEmpty(iconRole) || s <= 0f) return false;
            Sprite spr = UI.LoadIconSprite(iconRole, UI.BarIconGlyphTint);
            if (spr == null) return false;

            float iconSz = QmAssignFloatGroupChipIconSizeRef * s;
            float gap = QmAssignFloatGroupChipIconGapRef * s;
            if (btn.GetComponent<HorizontalLayoutGroup>() == null)
            {
                UI.AddHLG(
                    btn,
                    spacing: gap,
                    padding: UI.Pad(QmAssignFloatGroupChipPadLRef, QmAssignFloatGroupChipPadRRef, 0f, 0f, s),
                    childAlignment: TextAnchor.MiddleCenter,
                    childForceExpandWidth: false,
                    childForceExpandHeight: false);
            }

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(btn.transform, false);
            iconGO.transform.SetAsFirstSibling();
            Image img = UI.AddImage(iconGO, Color.white, false);
            img.preserveAspect = true;
            UI.SetIconSprite(img, spr);
            UI.AddLE(
                iconGO,
                minWidth: iconSz, minHeight: iconSz,
                preferredWidth: iconSz, preferredHeight: iconSz,
                flexibleWidth: 0f, flexibleHeight: 0f);

            Text t = btn.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleLeft;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.raycastTarget = false;
                LayoutElement tle = t.GetComponent<LayoutElement>();
                if (tle == null) tle = t.gameObject.AddComponent<LayoutElement>();
                tle.preferredWidth = t.preferredWidth;
                tle.flexibleWidth = 0f;
            }
            return true;
        }

        private void QuickMenuRelayoutAssignFloatGroupTabs(float s, float chromeSz)
        {
            if (m_QmAssignFloatGroupTabsContentRT == null || m_QmAssignFloatGroupTabsRow == null) return;

            float rowH = chromeSz;
            float colSpacing = QmAssignFloatGroupTabRowGapRef * s;
            float rowSpacing = QmAssignFloatGroupTabRowGapRef * s;
            float pad = QmAssignFloatGroupTabPadRef * s;

            float availW = 0f;
            try
            {
                if (m_QuickMenuAssignPopupRT != null)
                    availW = m_QuickMenuAssignPopupRT.rect.width - pad * 2f;
            }
            catch { }
            if (availW <= 1f)
            {
                try { availW = m_QmAssignFloatGroupTabsContentRT.rect.width; } catch { availW = 0f; }
            }
            if (availW <= 1f) availW = 280f * s;

            float x = 0f;
            float y = 0f;
            int rows = 1;
            int n = m_QmAssignFloatGroupTabsContentRT.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform child = m_QmAssignFloatGroupTabsContentRT.GetChild(i);
                if (child == null || !child.gameObject.activeSelf) continue;
                RectTransform rt = child as RectTransform;
                if (rt == null) continue;

                float w = 0f;
                LayoutElement le = child.GetComponent<LayoutElement>();
                if (le != null && le.preferredWidth > 1f) w = le.preferredWidth;
                if (w <= 1f)
                {
                    try { LayoutRebuilder.ForceRebuildLayoutImmediate(rt); } catch { }
                    w = LayoutUtility.GetPreferredWidth(rt);
                }
                if (w <= 1f) w = rt.sizeDelta.x;
                if (w <= 1f) w = 48f * s;

                if (x > 0f && x + w > availW + 0.5f)
                {
                    x = 0f;
                    y -= rowH + rowSpacing;
                    rows++;
                }

                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(x, y);
                rt.sizeDelta = new Vector2(w, rowH);

                x += w + colSpacing;
            }

            float totalH = rows * rowH + (rows - 1) * rowSpacing + pad * 2f;
            if (totalH < chromeSz + 8f * s) totalH = chromeSz + 8f * s;
            m_QmAssignFloatGroupTabsH = totalH;

            RectTransform groupRT = m_QmAssignFloatGroupTabsRow.GetComponent<RectTransform>();
            if (groupRT != null)
                groupRT.sizeDelta = new Vector2(0f, totalH);

            m_QmAssignFloatGroupTabsContentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalH);
            m_QmAssignFloatGroupTabsContentRT.offsetMin = new Vector2(pad, -totalH);
            m_QmAssignFloatGroupTabsContentRT.offsetMax = new Vector2(-pad, -pad);

            QuickMenuApplyAssignFloatScrollHostInsets(s);
        }

        private void QuickMenuApplyAssignFloatScrollHostInsets(float s)
        {
            if (m_QmAssignFloatScrollHost == null) return;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float filterH = GalleryUiDesignTokens.FloatSearchRowHeightRef * s;
            float groupH = m_QmAssignFloatGroupTabsH > 1f ? m_QmAssignFloatGroupTabsH : (chromeSz + 8f * s);

            RectTransform scrollRT = m_QmAssignFloatScrollHost.GetComponent<RectTransform>();
            if (scrollRT == null) return;
            scrollRT.offsetMin = new Vector2(0f, footerH);
            scrollRT.offsetMax = new Vector2(0f, -(titleH + filterH + groupH));

            if (m_QmAssignFloatGroupTabsRow != null)
            {
                RectTransform groupRT = m_QmAssignFloatGroupTabsRow.GetComponent<RectTransform>();
                if (groupRT != null)
                    groupRT.anchoredPosition = new Vector2(0f, -(titleH + filterH));
            }
        }

        private void QuickMenuRebuildAssignFloatRows(int font, float s)
        {
            if (m_QmAssignFloatRowsParent == null) return;
            foreach (var b in m_QuickMenuAssignPopupButtons)
            {
                try { if (b != null) DestroyImmediate(b); } catch { }
            }
            m_QuickMenuAssignPopupButtons.Clear();
            m_QmAssignFloatRowEntries.Clear();

            float rowH = GalleryUiDesignTokens.QmAssignFloatRowHeightRef * s;
            for (int i = 0; i < QmAssignCatalog.Length; i++)
            {
                QmAssignCatalogEntry entry = QmAssignCatalog[i];
                int iCopy = i;
                string label = QuickMenuGetAssignRowLabel(entry.Action);
                GameObject btnGo = UI.CreateChromeLayoutButton(
                    m_QmAssignFloatRowsParent, 0f, rowH, label, font, QmAssignFloatRowBg,
                    () =>
                    {
                        QmAssignCatalogEntry picked = QmAssignCatalog[iCopy];
                        int idx = m_QuickMenuAssignPopupTargetIdx;
                        if (idx >= 0 && idx < QuickMenuGridSlotCount)
                            QuickMenuSetAssignment(idx, picked.Action);
                    });
                QuickMenuAddLeftIconAndKeepLabel(btnGo, QuickMenuGetAssignPopupIcon(entry.Action));
                QuickMenuAttachAssignDragSource(btnGo, entry.Action);
                m_QuickMenuAssignPopupButtons.Add(btnGo);
                m_QmAssignFloatRowEntries.Add(entry);
            }

            QuickMenuApplyAssignFloatRowFilter();
            try { UI.ApplyFloatRootHoverPolicy(m_QmAssignFloatRowsParent.gameObject); } catch { }
        }

        private void QuickMenuApplyAssignFloatRowFilter()
        {
            string filter = (m_QmAssignFloatFilter ?? "").Trim();
            bool hasFilter = filter.Length > 0;
            string group = m_QmAssignFloatGroup ?? QmAssignGroupAll;
            bool allGroup = string.Equals(group, QmAssignGroupAll, StringComparison.OrdinalIgnoreCase);
            int visible = 0;
            int n = Mathf.Min(m_QuickMenuAssignPopupButtons.Count, m_QmAssignFloatRowEntries.Count);
            for (int i = 0; i < n; i++)
            {
                GameObject go = m_QuickMenuAssignPopupButtons[i];
                if (go == null) continue;
                QmAssignCatalogEntry entry = m_QmAssignFloatRowEntries[i];
                bool groupOk = allGroup || string.Equals(entry.Group, group, StringComparison.OrdinalIgnoreCase);
                bool textOk = !hasFilter;
                if (hasFilter)
                {
                    string label = QuickMenuGetAssignRowLabel(entry.Action);
                    textOk = (label ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                bool show = groupOk && textOk;
                if (go.activeSelf != show) go.SetActive(show);
                if (show) visible++;
            }
            if (m_QmAssignFloatEmptyGo != null)
                m_QmAssignFloatEmptyGo.SetActive(visible == 0);
        }

        private void QuickMenuRefreshAssignFloatFilterClearVisible()
        {
            if (m_QmAssignFloatFilterClearGo == null) return;
            bool show = m_QmAssignFloatFilterInput != null
                && !string.IsNullOrEmpty(m_QmAssignFloatFilterInput.text);
            if (m_QmAssignFloatFilterClearGo.activeSelf != show)
                m_QmAssignFloatFilterClearGo.SetActive(show);
        }

        private void QuickMenuSetAssignFloatFilterTextWithoutNotify(string t)
        {
            if (m_QmAssignFloatFilterInput == null) return;
            if (m_QmAssignFloatFilterOnValueChanged != null)
                m_QmAssignFloatFilterInput.onValueChanged.RemoveListener(m_QmAssignFloatFilterOnValueChanged);
            m_QmAssignFloatFilterInput.text = t ?? "";
            if (m_QmAssignFloatFilterOnValueChanged != null)
                m_QmAssignFloatFilterInput.onValueChanged.AddListener(m_QmAssignFloatFilterOnValueChanged);
            QuickMenuRefreshAssignFloatFilterClearVisible();
        }

        private void QuickMenuRefreshAssignFloatHint()
        {
            if (m_QmAssignFloatHintText == null) return;
            if (m_QuickMenuAssignPopupTargetIdx >= 0)
                m_QmAssignFloatHintText.text = VPBTranslation.T("hook.qmassign.float_hint_slot", "Click assigns this slot · drag also works");
            else
                m_QmAssignFloatHintText.text = VPBTranslation.T("hook.qmassign.float_hint", "Drag an action onto a slot");
        }

        private void QuickMenuSetAssignFloatRaycasts(bool on)
        {
            if (m_QmAssignFloatCanvasGroup != null)
                m_QmAssignFloatCanvasGroup.blocksRaycasts = on;
        }

        private void QuickMenuToggleAssignFloatCollapsed()
        {
            if (m_QuickMenuAssignPopupRT == null) return;
            float s = QmAssignFloatChromeScale;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;

            if (!m_QmAssignFloatCollapsed)
            {
                QuickMenuCaptureAssignFloatGeometryToMemory();
                m_QmAssignFloatExpandHeightRef = m_QmAssignFloatSavedSizeRef.HasValue
                    ? m_QmAssignFloatSavedSizeRef.Value.y
                    : GalleryUiDesignTokens.QmAssignFloatDefaultHeightRef;
                m_QmAssignFloatCollapsedTopLeftPos = m_QuickMenuAssignPopupRT.anchoredPosition;
                m_QmAssignFloatCollapsed = true;
            }
            else
            {
                float h = m_QmAssignFloatExpandHeightRef.HasValue
                    ? m_QmAssignFloatExpandHeightRef.Value
                    : GalleryUiDesignTokens.QmAssignFloatDefaultHeightRef;
                h = Mathf.Clamp(h,
                    GalleryUiDesignTokens.QmAssignFloatMinHeightRef,
                    GalleryUiDesignTokens.QmAssignFloatMaxHeightRef);
                float w = m_QmAssignFloatSavedSizeRef.HasValue
                    ? m_QmAssignFloatSavedSizeRef.Value.x
                    : GalleryUiDesignTokens.QmAssignFloatDefaultWidthRef;
                m_QmAssignFloatSavedSizeRef = new Vector2(
                    Mathf.Clamp(w, GalleryUiDesignTokens.QmAssignFloatMinWidthRef, GalleryUiDesignTokens.QmAssignFloatMaxWidthRef),
                    h);
                m_QmAssignFloatCollapsed = false;
                m_QmAssignFloatCollapsedTopLeftPos = null;
            }

            QuickMenuSyncAssignFloatCollapseChrome(titleH);
            QuickMenuCaptureAssignFloatGeometryToMemory();
            QuickMenuPersistAssignFloatGeometry();
        }

        private void QuickMenuSyncAssignFloatCollapseChrome(float titleH)
        {
            if (m_QmAssignFloatFilterRow != null) m_QmAssignFloatFilterRow.SetActive(!m_QmAssignFloatCollapsed);
            if (m_QmAssignFloatGroupTabsRow != null) m_QmAssignFloatGroupTabsRow.SetActive(!m_QmAssignFloatCollapsed);
            if (m_QmAssignFloatScrollHost != null) m_QmAssignFloatScrollHost.SetActive(!m_QmAssignFloatCollapsed);
            if (m_QmAssignFloatFooter != null) m_QmAssignFloatFooter.SetActive(!m_QmAssignFloatCollapsed);

            if (m_QmAssignFloatCollapseIcon != null)
            {
                string path = m_QmAssignFloatCollapsed ? "chevron-down" : "chevron-up";
                Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
                if (spr != null)
                    UI.SetIconSprite(m_QmAssignFloatCollapseIcon, spr);
            }

            if (m_QuickMenuAssignPopupRT != null)
            {
                float s = QmAssignFloatChromeScale;
                if (m_QmAssignFloatCollapsed)
                {
                    Vector2 size = m_QuickMenuAssignPopupRT.sizeDelta;
                    size.y = titleH;
                    m_QuickMenuAssignPopupRT.sizeDelta = size;
                    if (m_QmAssignFloatCollapsedTopLeftPos.HasValue)
                        m_QuickMenuAssignPopupRT.anchoredPosition = m_QmAssignFloatCollapsedTopLeftPos.Value;
                }
                else if (m_QmAssignFloatSavedSizeRef.HasValue)
                {
                    m_QuickMenuAssignPopupRT.sizeDelta = new Vector2(
                        m_QmAssignFloatSavedSizeRef.Value.x * s,
                        m_QmAssignFloatSavedSizeRef.Value.y * s);
                }
            }
        }

        private void QuickMenuLoadAssignFloatGeometryFromConfig()
        {
            m_QmAssignFloatSavedPosCenter = null;
            m_QmAssignFloatSavedSizeRef = null;
            try
            {
                if (VPBConfig.Instance == null) return;
                if (VPBConfig.Instance.QuickMenuAssignFloatPosSaved)
                {
                    m_QmAssignFloatSavedPosCenter = new Vector2(
                        VPBConfig.Instance.QuickMenuAssignFloatPosX,
                        VPBConfig.Instance.QuickMenuAssignFloatPosY);
                }
                if (VPBConfig.Instance.QuickMenuAssignFloatSizeSaved)
                {
                    float w = VPBConfig.Instance.QuickMenuAssignFloatWidthRef;
                    float h = VPBConfig.Instance.QuickMenuAssignFloatHeightRef;
                    if (w >= GalleryUiDesignTokens.QmAssignFloatMinWidthRef
                        && h >= GalleryUiDesignTokens.QmAssignFloatMinHeightRef)
                    {
                        m_QmAssignFloatSavedSizeRef = new Vector2(
                            Mathf.Clamp(w, GalleryUiDesignTokens.QmAssignFloatMinWidthRef, GalleryUiDesignTokens.QmAssignFloatMaxWidthRef),
                            Mathf.Clamp(h, GalleryUiDesignTokens.QmAssignFloatMinHeightRef, GalleryUiDesignTokens.QmAssignFloatMaxHeightRef));
                    }
                }
            }
            catch { }
        }

        private void QuickMenuCaptureAssignFloatGeometryToMemory()
        {
            if (m_QuickMenuAssignPopupRT == null) return;
            float s = QmAssignFloatChromeScale;
            m_QmAssignFloatSavedPosCenter = QmAssignFloatTopLeftToCenter(
                m_QuickMenuAssignPopupRT.anchoredPosition, m_QuickMenuAssignPopupRT.sizeDelta);
            if (!m_QmAssignFloatCollapsed)
            {
                m_QmAssignFloatSavedSizeRef = new Vector2(
                    Mathf.Clamp(m_QuickMenuAssignPopupRT.sizeDelta.x / s, GalleryUiDesignTokens.QmAssignFloatMinWidthRef, GalleryUiDesignTokens.QmAssignFloatMaxWidthRef),
                    Mathf.Clamp(m_QuickMenuAssignPopupRT.sizeDelta.y / s, GalleryUiDesignTokens.QmAssignFloatMinHeightRef, GalleryUiDesignTokens.QmAssignFloatMaxHeightRef));
            }
        }

        private void QuickMenuPersistAssignFloatGeometry()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                if (m_QmAssignFloatSavedPosCenter.HasValue)
                {
                    VPBConfig.Instance.QuickMenuAssignFloatPosSaved = true;
                    VPBConfig.Instance.QuickMenuAssignFloatPosX = m_QmAssignFloatSavedPosCenter.Value.x;
                    VPBConfig.Instance.QuickMenuAssignFloatPosY = m_QmAssignFloatSavedPosCenter.Value.y;
                }
                if (m_QmAssignFloatSavedSizeRef.HasValue)
                {
                    VPBConfig.Instance.QuickMenuAssignFloatSizeSaved = true;
                    VPBConfig.Instance.QuickMenuAssignFloatWidthRef = m_QmAssignFloatSavedSizeRef.Value.x;
                    VPBConfig.Instance.QuickMenuAssignFloatHeightRef = m_QmAssignFloatSavedSizeRef.Value.y;
                }
                VPBConfig.Instance.Save(false, true);
            }
            catch { }
        }

        private void OnQmAssignFloatMoved()
        {
            m_QmAssignFloatPlaced = true;
            QuickMenuCaptureAssignFloatGeometryToMemory();
            QuickMenuPersistAssignFloatGeometry();
        }

        private void OnQmAssignFloatResized()
        {
            if (m_QmAssignFloatCollapsed) return;
            float s = QmAssignFloatChromeScale;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            try { QuickMenuRelayoutAssignFloatGroupTabs(s, chromeSz); } catch { }
            QuickMenuCaptureAssignFloatGeometryToMemory();
            QuickMenuPersistAssignFloatGeometry();
        }

        private static Vector2 QmAssignFloatCenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 QmAssignFloatTopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private void QuickMenuRefreshAssignFloatLocalizedChrome()
        {
            if (m_QuickMenuAssignPopupRoot == null) return;
            float s = QmAssignFloatChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;

            if (m_QmAssignFloatTitleBarRT != null)
            {
                Transform titleTr = m_QmAssignFloatTitleBarRT.Find("Title");
                Text title = titleTr != null ? titleTr.GetComponent<Text>() : null;
                if (title != null)
                    title.text = VPBTranslation.T("hook.qmassign.float_title", "Assignable Buttons");
            }

            if (m_QmAssignFloatFilterInput != null)
            {
                Text ph = m_QmAssignFloatFilterInput.placeholder as Text;
                if (ph != null)
                    ph.text = VPBTranslation.T("hook.qmassign.filter", "Filter actions…");
            }

            if (m_QmAssignFloatEmptyText != null)
                m_QmAssignFloatEmptyText.text = VPBTranslation.T("hook.qmassign.filter_empty", "No matching actions");

            QuickMenuRefreshAssignFloatHint();
            QuickMenuRebuildAssignFloatGroupTabs(font, s, chromeSz);
            QuickMenuRebuildAssignFloatRows(font, s);
        }

        private string QuickMenuGetAssignRowLabel(QuickMenuAssignableAction a)
        {
            switch (a)
            {
                case QuickMenuAssignableAction.None: return VPBTranslation.T("hook.qmassign.none", "None");
                case QuickMenuAssignableAction.CoreSettingsButton: return VPBTranslation.T("hook.qmbutton.core_settings", "Core: Settings");
                case QuickMenuAssignableAction.CorePageButton: return VPBTranslation.T("hook.qmbutton.core_page", "Core: Page");
                case QuickMenuAssignableAction.PageNext: return VPBTranslation.T("hook.qmbutton.page_next", "Page Forward");
                case QuickMenuAssignableAction.PagePrev: return VPBTranslation.T("hook.qmbutton.page_prev", "Page Backward");
                case QuickMenuAssignableAction.SwitchWatchHand: return VPBTranslation.T("hook.qmbutton.switch_watch_hand", "Switch Watch Hand");
                case QuickMenuAssignableAction.CreateGallery: return VPBTranslation.T("hook.qmbutton.create_gallery", "Create Gallery");
                case QuickMenuAssignableAction.ShowHide: return VPBTranslation.T("hook.qmbutton.show_hide", "Show/Hide");
                case QuickMenuAssignableAction.BringFront: return VPBTranslation.T("hook.qmbutton.bring_front", "Bring Front");
                case QuickMenuAssignableAction.CloseAll: return VPBTranslation.T("hook.qmbutton.close_all", "Close All");
                case QuickMenuAssignableAction.Save: return VPBTranslation.T("hook.qmbutton.save", "Save");
                case QuickMenuAssignableAction.Undo: return VPBTranslation.T("hook.qmbutton.undo", "Undo");
                case QuickMenuAssignableAction.Redo: return VPBTranslation.T("hook.qmbutton.redo", "Redo");
                case QuickMenuAssignableAction.Hub: return VPBTranslation.T("hook.qmbutton.hub", "Hub");
                case QuickMenuAssignableAction.Cleanup: return VPBTranslation.T("hook.qmbutton.cleanup", "Cleanup");
                case QuickMenuAssignableAction.CreatorMode: return VPBTranslation.T("hook.qmbutton.creator_mode", "Scene Tools");
                case QuickMenuAssignableAction.TargetAtom: return VPBTranslation.T("hook.qmbutton.target_atom", "Target Atom");
                case QuickMenuAssignableAction.ReplaceAddToggle: return VPBTranslation.T("hook.qmbutton.replace_add", "Replace/Add");
                case QuickMenuAssignableAction.CompressCache: return VPBTranslation.T("hook.qmbutton.compress_cache", "Compress Cache");
                case QuickMenuAssignableAction.AutoHideGallery: return VPBTranslation.T("hook.qmbutton.autohide", "Auto-Hide");
                case QuickMenuAssignableAction.ShowHiddenPackages: return VPBTranslation.T("hook.qmbutton.show_hidden", "Show Hidden");
                case QuickMenuAssignableAction.FpsCounter: return VPBTranslation.T("hook.qmbutton.fps", "FPS Counter");
                case QuickMenuAssignableAction.History: return VPBTranslation.T("hook.qmbutton.history", "History");
                case QuickMenuAssignableAction.PerfMode: return VPBTranslation.T("hook.qmbutton.perf_mode", "Perf Mode");
                case QuickMenuAssignableAction.RemoveAllClothing: return VPBTranslation.T("hook.qmbutton.remove_all_clothing", "Remove All Clothing");
                case QuickMenuAssignableAction.RemoveAllHair: return VPBTranslation.T("hook.qmbutton.remove_all_hair", "Remove All Hair");
                case QuickMenuAssignableAction.ToggleImportSidebar: return VPBTranslation.T("hook.qmbutton.toggle_import_sidebar", "Toggle Import Sidebar");
                case QuickMenuAssignableAction.StarFilter: return VPBTranslation.T("hook.qmbutton.star_filter", "Star Filter");
                case QuickMenuAssignableAction.PerfStepUp: return VPBTranslation.T("hook.qmbutton.perf_step_up", "Perf Step Up");
                case QuickMenuAssignableAction.PerfStepDown: return VPBTranslation.T("hook.qmbutton.perf_step_down", "Perf Step Down");
                case QuickMenuAssignableAction.Random: return VPBTranslation.T("hook.qmbutton.random_current", "Random: Gallery Filter");
                case QuickMenuAssignableAction.RandomScenes: return VPBTranslation.T("hook.qmbutton.random_scenes", "Random: Scenes");
                case QuickMenuAssignableAction.RandomSubScenes: return VPBTranslation.T("hook.qmbutton.random_subscenes", "Random: SubScenes");
                case QuickMenuAssignableAction.RandomClothing: return VPBTranslation.T("hook.qmbutton.random_clothing", "Random: Clothing");
                case QuickMenuAssignableAction.RandomHair: return VPBTranslation.T("hook.qmbutton.random_hair", "Random: Hair");
                case QuickMenuAssignableAction.RandomPose: return VPBTranslation.T("hook.qmbutton.random_pose", "Random: Pose");
                case QuickMenuAssignableAction.RandomAppearance: return VPBTranslation.T("hook.qmbutton.random_appearance", "Random: Appearance");
                case QuickMenuAssignableAction.RandomSkin: return VPBTranslation.T("hook.qmbutton.random_skin", "Random: Skin");
                case QuickMenuAssignableAction.RandomSceneImport: return VPBTranslation.T("hook.qmbutton.random_scene_import", "Random: Scene Import");
                case QuickMenuAssignableAction.OpenCategoryScenes: return VPBTranslation.T("hook.qmbutton.open_category_scenes", "Open Category: Scenes");
                case QuickMenuAssignableAction.OpenCategorySubScenes: return VPBTranslation.T("hook.qmbutton.open_category_subscenes", "Open Category: SubScenes");
                case QuickMenuAssignableAction.OpenCategoryClothing: return VPBTranslation.T("hook.qmbutton.open_category_clothing", "Open Category: Clothing");
                case QuickMenuAssignableAction.OpenCategoryHair: return VPBTranslation.T("hook.qmbutton.open_category_hair", "Open Category: Hair");
                case QuickMenuAssignableAction.OpenCategoryPose: return VPBTranslation.T("hook.qmbutton.open_category_pose", "Open Category: Pose");
                case QuickMenuAssignableAction.OpenCategoryAppearance: return VPBTranslation.T("hook.qmbutton.open_category_appearance", "Open Category: Appearance");
                case QuickMenuAssignableAction.OpenCategoryPlugins: return VPBTranslation.T("hook.qmbutton.open_category_plugins", "Open Category: Plugins");
                case QuickMenuAssignableAction.OpenCategorySkin: return VPBTranslation.T("hook.qmbutton.open_category_skin", "Open Category: Skin");
                case QuickMenuAssignableAction.OpenCategoryAll: return VPBTranslation.T("hook.qmbutton.open_category_all", "Open Category: All");
                default: return a.ToString();
            }
        }
    }
}
