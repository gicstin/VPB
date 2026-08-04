using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        public void Init()
        {
            if (canvas != null) return;

            // Subscribe to config changes
            if (VPBConfig.Instance != null)
            {
                bool isVR = XrUtils.IsVrActive();

                isFixedLocally = !isVR && (VPBConfig.Instance.DesktopFixedMode || VPBConfig.Instance.EnableAutoFixedGallery) && (Gallery.singleton == null || Gallery.singleton.PanelCount == 0);

                // Restore persisted layout mode (Grid/List) before UI is built.
                try
                {
                    int v = VPBConfig.Instance.GalleryLayoutMode;
                    if (v == (int)GalleryLayoutMode.Grid || v == (int)GalleryLayoutMode.List)
                        layoutMode = (GalleryLayoutMode)v;
                }
                catch { }
                
                if (isFixedLocally)
                {
                    isCollapsed = true;
                }

                // Fixed panes should start with side tab lists collapsed
                if (isFixedLocally)
                {
                    leftActiveContent = null;
                    rightActiveContent = null;
                }

                try
                {
                    if (!string.IsNullOrEmpty(VPBConfig.Instance.ApplyMode))
                        ItemApplyMode = (ApplyMode)Enum.Parse(typeof(ApplyMode), VPBConfig.Instance.ApplyMode);
                }
                catch (System.Exception ex) { 
                    LogUtil.LogError("[GalleryPanel.Init] Error loading ApplyMode: " + ex.Message);
                }

                SubscribeGalleryPanelToVpBConfigChanged();
            }

            // Persisted per-session toggles (shared across panes)
            try
            {
                if (VPBConfig.Instance != null)
                {
                    springScrollButtonEnabled = VPBConfig.Instance.IsSpringScrollButtonEnabled();
                    holdToLaunchEnabled = VPBConfig.Instance.HoldToLaunchEnabled;
                    holdToLaunchPrevEnableDragDrop = VPBConfig.Instance.HoldToLaunchPrevEnableDragDrop;
                }
            }
            catch { }

            // ... standard Init code follows ...
            // string nameSuffix = isUndocked ? "_Undocked" : "";
            GameObject canvasGO = new GameObject("VPB_GalleryCanvas");
            canvasGO.layer = 5; // UI layer
            canvas = canvasGO.AddComponent<Canvas>();
            RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(1200, 800);
            
            GraphicRaycaster gr = canvasGO.AddComponent<GraphicRaycaster>();
            gr.ignoreReversedGraphics = true;

            if (SuperController.singleton != null)
            {
                SuperController.singleton.AddCanvas(canvas);
                _registeredWithSuperController = true;
            }

            if (Application.isPlaying)
            {
                canvas.renderMode = isFixedLocally ? RenderMode.ScreenSpaceOverlay : RenderMode.WorldSpace;
                // Overlay hit-tests require null cam; keep worldCamera only for WorldSpace.
                canvas.worldCamera = isFixedLocally ? null : Camera.main;
                canvas.sortingOrder = -10000;
                // Position will be set in Show()
                if (isFixedLocally)
                    canvas.transform.localScale = Vector3.one;
                else
                {
                    // Native mainHUD ignores worldScale; lock WorldSpace lossyScale to meters-per-pixel.
                    ResetWorldSpaceCanvasScaleSync();
                    ApplyWorldSpaceCanvasScale();
                }
                canvasGO.layer = 5; // UI layer
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4;

            // Background
            backgroundBoxGO = UI.AddChildGOImage(canvasGO, new Color(0.1f, 0.1f, 0.1f, 0.9f), AnchorPresets.centre, 1200, 800, Vector2.zero);
            backgroundCanvasGroup = backgroundBoxGO.AddComponent<CanvasGroup>();
            backgroundCanvasGroup.ignoreParentGroups = true; // Ensure we control our own opacity separately if needed
            
            // Add UIHoverColor (This handles hover/drag color changes AND sets raycast target properly)
            UIHoverColor bgHover = backgroundBoxGO.AddComponent<UIHoverColor>();
            backgroundHoverColor = bgHover;
            bgHover.targetImage = backgroundBoxGO.GetComponent<Image>();
            bgHover.normalColor = GalleryBackgroundTinted;
            bgHover.hoverColor = GalleryBackgroundTinted;
            
            // AddHoverDelegate
            AddHoverDelegate(backgroundBoxGO);

            if (isFixedLocally)
            {
                RectTransform bgRT = backgroundBoxGO.GetComponent<RectTransform>();
                float leftRatio = VPBConfig.Instance.DesktopCustomWidth;
                float bottomAnchor = (VPBConfig.Instance.DesktopFixedHeightMode == 1) ? VPBConfig.Instance.DesktopCustomHeight : 0f;
                string dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide);
                if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                {
                    bgRT.anchorMin = new Vector2(0f, bottomAnchor);
                    bgRT.anchorMax = new Vector2(1f - leftRatio, 1f);
                }
                else if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase))
                {
                    bgRT.anchorMin = new Vector2(0f, bottomAnchor);
                    bgRT.anchorMax = new Vector2(1f, 1f);
                }
                else
                {
                    bgRT.anchorMin = new Vector2(leftRatio, bottomAnchor);
                    bgRT.anchorMax = new Vector2(1f, 1f);
                }
                bgRT.offsetMin = Vector2.zero;
                bgRT.offsetMax = Vector2.zero;

                if (isCollapsed)
                {
                    // Use a safe default width if layout hasn't run yet
                    float w = bgRT.rect.width > 0 ? bgRT.rect.width : 1200f;
                    float h = bgRT.rect.height > 0 ? bgRT.rect.height : 800f;
                    if (string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                        bgRT.anchoredPosition = new Vector2(-w, 0f);
                    else if (string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase))
                        bgRT.anchoredPosition = new Vector2(0f, h);
                    else
                        bgRT.anchoredPosition = new Vector2(w, 0f);
                }
            }
            
            void InitCollapseTrigger(GameObject go, out Text outText, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, string arrowText, ChamferedRect.ChamferSide chamferSide)
            {
                go.name = name;
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.pivot = pivot;
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = sizeDelta;
                var ch = go.GetComponent<ChamferedRect>();
                if (ch != null) ch.chamferSide = chamferSide;

                Text t = UI.CreateLabel(go, arrowText, FixedCollapseTriggerArrowFontSize, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleCenter, name: "Text");
                outText = t;

                var hov = go.AddComponent<UIHoverDelegate>();
                hov.OnHoverChange += (enter) =>
                {
                    if (go.activeInHierarchy) isHoveringTrigger = enter;
                };
            }

            // Collapse Trigger Areas (Right/Left/Top) — separate GOs so chamfer always correct
            collapseTriggerGO = UI.AddChildGOChamferedImage(canvasGO, new Color(0.15f, 0.15f, 0.15f, 0.4f), AnchorPresets.vStretchRight, FixedCollapseTriggerThickness, 0, Vector2.zero, FixedCollapseTriggerChamferSize);
            InitCollapseTrigger(
                collapseTriggerGO,
                out collapseHandleText,
                "FixedModeCollapseTrigger_Right",
                new Vector2(1f, 0.2f),
                new Vector2(1f, 0.8f),
                new Vector2(1f, 0.5f),
                new Vector2(FixedCollapseTriggerThickness, 0f),
                "<",
                ChamferedRect.ChamferSide.Left
            );

            collapseTriggerLeftGO = UI.AddChildGOChamferedImage(canvasGO, new Color(0.15f, 0.15f, 0.15f, 0.4f), AnchorPresets.vStretchLeft, FixedCollapseTriggerThickness, 0, Vector2.zero, FixedCollapseTriggerChamferSize);
            InitCollapseTrigger(
                collapseTriggerLeftGO,
                out collapseHandleLeftText,
                "FixedModeCollapseTrigger_Left",
                new Vector2(0f, 0.2f),
                new Vector2(0f, 0.8f),
                new Vector2(0f, 0.5f),
                new Vector2(FixedCollapseTriggerThickness, 0f),
                ">",
                ChamferedRect.ChamferSide.Right
            );

            collapseTriggerTopGO = UI.AddChildGOChamferedImage(canvasGO, new Color(0.15f, 0.15f, 0.15f, 0.4f), AnchorPresets.hStretchTop, 0, FixedCollapseTriggerThickness, Vector2.zero, FixedCollapseTriggerChamferSize);
            InitCollapseTrigger(
                collapseTriggerTopGO,
                out collapseHandleTopText,
                "FixedModeCollapseTrigger_Top",
                new Vector2(0.2f, 1f),
                new Vector2(0.8f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, FixedCollapseTriggerThickness),
                "˅",
                ChamferedRect.ChamferSide.Bottom
            );

            // Active in fixed mode; runtime selects which one based on dock side
            collapseTriggerGO.SetActive(false);
            if (collapseTriggerLeftGO != null) collapseTriggerLeftGO.SetActive(false);
            if (collapseTriggerTopGO != null) collapseTriggerTopGO.SetActive(false);
            
            dragger = backgroundBoxGO.AddComponent<UIDraggable>();
            dragger.target = canvasGO.transform;
            dragger.OnDragEnd = () => {
                // Toggle active state to force VaM/Unity to refresh interaction state after move
                if (canvasGO != null)
                {
                    canvasGO.SetActive(false);
                    canvasGO.SetActive(true);
                    
                }
            };

            quickFiltersUI = new QuickFiltersUI(this, backgroundBoxGO);

            // Register Panel
            if (Gallery.singleton != null)
            {
                Gallery.singleton.AddPanel(this);
            }

            // Title Bar
            GameObject titleBarGO = new GameObject("TitleBar");
            titleBarGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform titleBarRT = titleBarGO.AddComponent<RectTransform>();
            titleBarRT.anchorMin = new Vector2(0, 1);
            titleBarRT.anchorMax = new Vector2(1, 1);
            titleBarRT.pivot = new Vector2(0.5f, 1);
            titleBarRT.anchoredPosition = new Vector2(0, 0);
            titleBarRT.sizeDelta = new Vector2(0, 70);

            GameObject titleGO = new GameObject("Title");
            titleGO.transform.SetParent(titleBarGO.transform, false);
            titleText = titleGO.AddComponent<Text>();
            VPBUiFont.ApplyTo(titleText);
            titleText.fontSize = GalleryUiDesignTokens.FontRef;
            titleText.fontStyle = FontStyle.Normal;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleLeft;
            RectTransform titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 0.5f);
            titleRT.anchorMax = new Vector2(0, 0.5f);
            titleRT.pivot = new Vector2(0, 0.5f);
            titleRT.anchoredPosition = new Vector2(60, 10);
            titleRT.sizeDelta = new Vector2(300, 40);
            titleText.raycastTarget = false;

            SetupCategoryQuickSwitch(titleBarGO, backgroundBoxGO, titleGO);

            GameObject fpsGO = new GameObject("FPS");
            fpsGO.transform.SetParent(titleBarGO.transform, false);
            fpsText = fpsGO.AddComponent<Text>();
            VPBUiFont.ApplyTo(fpsText);
            fpsText.fontSize = GalleryUiDesignTokens.FontBodyRef;
            fpsText.color = Color.white;
            fpsText.alignment = TextAnchor.MiddleRight;
            RectTransform fpsRT = fpsGO.GetComponent<RectTransform>();
            fpsRT.anchorMin = new Vector2(0.5f, 0.5f);
            fpsRT.anchorMax = new Vector2(0.5f, 0.5f);
            fpsRT.pivot = new Vector2(0.5f, 0.5f);
            fpsRT.anchoredPosition = Vector2.zero;
            fpsRT.sizeDelta = new Vector2(100, 40);
            _titleBarFpsRT = fpsRT;

            SetupLanguageSwitcher(titleBarGO);

            _titleBarSearchOnValueChanged = (val) => {
                if (_suppressTitleBarSearchValueChanged) return;
                if (IsSettingsPanelOpen())
                {
                    settingsFilter = val ?? "";
                    try { UpdateTabs(); } catch { }
                    try { RefreshInternalSettingsListRows(true); } catch { }
                    try { SyncTitleBarSearchBackdrop(); } catch { }
                    return;
                }
                // Chip mode: field is draft only — filter updates on Enter / chip toggle.
                if (HasTitleSearchChips())
                {
                    try { SyncTitleBarSearchBackdrop(); } catch { }
                    return;
                }
                SetNameFilter(val);
                try { SyncTitleBarSearchBackdrop(); } catch { }
            };

            titleSearchInput = CreateSearchInput(titleBarGO, 240f, _titleBarSearchOnValueChanged, OnTitleSearchClearClicked, TitleSearchOnEscape);
            try { WireTitleSearchCommitKeys(titleSearchInput); } catch { }
            try { WireTitleSearchFieldChromeTips(titleSearchInput); } catch { }
            try
            {
                Text ph = titleSearchInput != null ? titleSearchInput.placeholder as Text : null;
                if (ph != null)
                    ph.text = VPBTranslation.T("gallery.search.main_chips", "Type + Enter chip · Shift+Enter exclude · Ctrl+F");
            }
            catch { }
            RectTransform titleSearchRT = titleSearchInput.GetComponent<RectTransform>();
            titleSearchRT.anchorMin = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchorMax = new Vector2(0.5f, 0.5f);
            titleSearchRT.pivot = new Vector2(0.5f, 0.5f);
            titleSearchRT.anchoredPosition = new Vector2(-40, 0);
            titleSearchRT.sizeDelta = new Vector2(240, GalleryUiDesignTokens.TitleBarChipRef);
            try
            {
                Image tsBg = titleSearchInput.GetComponent<Image>();
                if (tsBg != null) tsBg.color = ColorTitleSearchBackdropIdle;
            }
            catch { }
            SetupTitleSearchCompactControl(titleBarGO);
            try { SyncTitleBarSearchBackdrop(); } catch { }

            // Creator filter dropdown button (between Filter Presets and Search)
            SetupTitleCreatorFilterDropdown(titleBarGO, backgroundBoxGO);
            SetupGlobalSourceFilterDropdown(titleBarGO, backgroundBoxGO);
            try { EnsureTitleBarOverflowChrome(titleBarGO); } catch { }
            try { EnsureInAppHelpChrome(titleBarGO); } catch { }

            fileSortDirAscSprite = UI.LoadIconSprite("vpb_icons/sort_asc.png", UI.BarIconGlyphTint);
            fileSortDirDescSprite = UI.LoadIconSprite("vpb_icons/sort_desc.png", UI.BarIconGlyphTint);

            const float fileSortChip = 40f;
            const float fileSortGap = 8f;

            // File sort: type button (abbrev + type menu / RMB cycle); separate direction button (↑/↓ icon)
            GameObject fileSortTypeBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, VPBTranslation.T("gallery.sort.az", "Az"), 16, 0, 0, AnchorPresets.middleCenter, null);
            fileSortTypeBtn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            fileSortTypeBtn.GetComponentInChildren<Text>().color = Color.white;
            RectTransform fileSortTypeRT = fileSortTypeBtn.GetComponent<RectTransform>();
            fileSortTypeRT.anchorMin = new Vector2(0.5f, 0.5f);
            fileSortTypeRT.anchorMax = new Vector2(0.5f, 0.5f);
            fileSortTypeRT.pivot = new Vector2(0.5f, 0.5f);
            fileSortTypeRT.anchoredPosition = new Vector2(108, 0);
            _titleBarFileSortTypeBtnRT = fileSortTypeRT;

            fileSortTypeText = fileSortTypeBtn.GetComponentInChildren<Text>();
            if (fileSortTypeText != null)
            {
                fileSortTypeText.gameObject.SetActive(true);
                try { VPBUiFont.ApplyTo(fileSortTypeText); } catch { }
            }

            GameObject fileSortDirBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, "", 16, 0, 0, AnchorPresets.middleCenter, ToggleFileSortDirection);
            fileSortDirBtn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            Text fileSortDirBtnLabel = fileSortDirBtn.GetComponentInChildren<Text>();
            if (fileSortDirBtnLabel != null)
                fileSortDirBtnLabel.gameObject.SetActive(false);
            RectTransform fileSortDirRT = fileSortDirBtn.GetComponent<RectTransform>();
            fileSortDirRT.anchorMin = new Vector2(0.5f, 0.5f);
            fileSortDirRT.anchorMax = new Vector2(0.5f, 0.5f);
            fileSortDirRT.pivot = new Vector2(0.5f, 0.5f);
            fileSortDirRT.anchoredPosition = new Vector2(108f + fileSortChip * 0.5f + fileSortGap + fileSortChip * 0.5f, 0f);
            _titleBarFileSortDirBtnRT = fileSortDirRT;

            {
                const float iconPad = 4f;
                GameObject dirIconGo = new GameObject("DirIcon");
                dirIconGo.transform.SetParent(fileSortDirBtn.transform, false);
                Sprite initial = fileSortDirAscSprite ?? fileSortDirDescSprite;
                Image dirImg = UI.AddImage(dirIconGo, Color.white);
                dirImg.sprite = initial;
                dirImg.preserveAspect = true;
                dirImg.raycastTarget = false;
                RectTransform dirIrt = dirIconGo.GetComponent<RectTransform>();
                dirIrt.anchorMin = Vector2.zero;
                dirIrt.anchorMax = Vector2.one;
                dirIrt.sizeDelta = new Vector2(-iconPad * 2f, -iconPad * 2f);
                dirIrt.anchoredPosition = Vector2.zero;
                fileSortDirIconImage = dirImg;
            }

            fileSortDirText = null;

            Button fileSortTypeButton = fileSortTypeBtn.GetComponent<Button>();
            fileSortTypeButton.onClick.RemoveAllListeners();

            EventTrigger fileSortTypeEt = fileSortTypeBtn.GetComponent<EventTrigger>();
            if (fileSortTypeEt == null) fileSortTypeEt = fileSortTypeBtn.AddComponent<EventTrigger>();
            var fileSortPointerClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            fileSortPointerClick.callback.AddListener((data) =>
            {
                var ped = (PointerEventData)data;
                if (ped.button == PointerEventData.InputButton.Right)
                    CycleSort("Files", fileSortTypeText, fileSortDirText);
                else if (ped.button == PointerEventData.InputButton.Left)
                    ToggleFileSortTypeMenu();
            });
            fileSortTypeEt.triggers.Add(fileSortPointerClick);

            AddTooltip(fileSortTypeBtn, "gallery.tooltip.sort_cycle_field", "Sort field: left-click menu, right-click cycle field");
            AddTooltip(fileSortDirBtn, "gallery.tooltip.sort_toggle_dir", "Toggle sort direction (↑/↓)");
            SetupFileSortTypeMenu();

            // Keep fileSortBtnText for compatibility with existing code
            fileSortBtnText = fileSortTypeText;

            // Init File Sort State
            try { SyncBrowseFilterCyclesFromMirroredSettings(); } catch { }
            try { MigrateLegacyExclusiveFileSortIfNeeded(); } catch { }
            UpdateSortButtonText(fileSortTypeText, fileSortDirText, GetSortState("Files"));
            try { UpdateGlobalSourceFilterButtonLabel(); } catch { }

            ratingSortToggleBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, VPBTranslation.T("gallery.sort.star", "★"), 18, 0, 0, AnchorPresets.middleCenter, null);
            ratingSortToggleBtn.GetComponent<Image>().color = UI.ChromeDark;
            ratingSortToggleBtnText = ratingSortToggleBtn.GetComponentInChildren<Text>();
            ratingSortToggleBtnText.color = Color.white;
            ratingStarNormalSprite = UI.LoadIconSprite("vpb_icons/star.png",     UI.BarIconGlyphTint);
            ratingStarOffSprite    = UI.LoadIconSprite("vpb_icons/star_off.png", UI.BarIconGlyphTint);
            {
                Sprite initial = ratingStarNormalSprite ?? ratingStarOffSprite;
                if (initial != null)
                {
                    UI.AddIconToButton(ratingSortToggleBtn, initial);
                    ratingSortIconImage = ratingSortToggleBtn.transform.Find("Icon")?.GetComponent<Image>();
                }
            }
            RectTransform ratingSortToggleRT = ratingSortToggleBtn.GetComponent<RectTransform>();
            ratingSortToggleRT.anchorMin = new Vector2(0.5f, 0.5f);
            ratingSortToggleRT.anchorMax = new Vector2(0.5f, 0.5f);
            ratingSortToggleRT.pivot = new Vector2(0.5f, 0.5f);
            ratingSortToggleRT.anchoredPosition = new Vector2(197, 0);
            _titleBarRatingSortToggleBtnRT = ratingSortToggleRT;
            Button ratingSortToggleButton = ratingSortToggleBtn.GetComponent<Button>();
            ratingSortToggleButton.onClick.RemoveAllListeners();
            ratingSortToggleButton.onClick.AddListener(ToggleRatingSort);
            AddRightClickDelegate(ratingSortToggleBtn, DisableRatingSortFilterIfEnabled);
            AddDynamicTooltip(ratingSortToggleBtn, BuildRatingPresenceFilterTooltip);
            SyncRatingSortToggleState();

            // Refresh Button (to the right of Star) — square icon button
            GameObject refreshBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, VPBTranslation.T("gallery.title.refresh", "Refresh"), 16, 0, 0, AnchorPresets.middleCenter, null);
            refreshBtn.GetComponent<Image>().color = UI.ChromeDark;
            refreshBtn.GetComponentInChildren<Text>().color = Color.white;
            RectTransform refreshRT = refreshBtn.GetComponent<RectTransform>();
            refreshRT.anchorMin = new Vector2(0.5f, 0.5f);
            refreshRT.anchorMax = new Vector2(0.5f, 0.5f);
            refreshRT.pivot = new Vector2(0.5f, 0.5f);
            refreshRT.anchoredPosition = new Vector2(245, 0); // adjusted for narrower width
            _titleBarRefreshBtnRT = refreshRT;

            Button refreshButton = refreshBtn.GetComponent<Button>();
            refreshButton.onClick.RemoveAllListeners();

            EventTrigger refreshEt = refreshBtn.GetComponent<EventTrigger>();
            if (refreshEt == null) refreshEt = refreshBtn.AddComponent<EventTrigger>();
            var refreshPointerClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            refreshPointerClick.callback.AddListener((data) =>
            {
                var ped = (PointerEventData)data;
                if (ped.button == PointerEventData.InputButton.Right)
                    UserRequestedNativeFileManagerRefresh();
                else if (ped.button == PointerEventData.InputButton.Left)
                    UserRequestedPackageRefresh();
            });
            refreshEt.triggers.Add(refreshPointerClick);

            titleBarRefreshBtnText = refreshBtn.GetComponentInChildren<Text>();
            VPBUiFont.ApplyTo(titleBarRefreshBtnText);
            AddTooltip(refreshBtn, "gallery.tooltip.refresh_packages", "Refresh Packages (right-click: VaM file list only)");
            { var s = UI.LoadIconSprite("vpb_icons/refresh.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(refreshBtn, s); }

            // Settings (title bar, left of filter presets; side rails no longer host Settings)
            GameObject titleBarSettingsBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, " ", 16, 0, 0, AnchorPresets.middleCenter, () => {
                try { OpenSettingsSideTab(); } catch { }
            });
            titleBarSettingsBtn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            titleBarSettingsBtnText = titleBarSettingsBtn.GetComponentInChildren<Text>();
            titleBarSettingsBtnText.color = Color.white;
            { var s = UI.LoadIconSprite("vpb_icons/settings.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(titleBarSettingsBtn, s); }
            if (titleBarSettingsBtnText != null && titleBarSettingsBtn.transform.Find("Icon") != null)
                titleBarSettingsBtnText.text = " ";
            RectTransform titleBarSettingsRT = titleBarSettingsBtn.GetComponent<RectTransform>();
            titleBarSettingsRT.anchorMin = new Vector2(0.5f, 0.5f);
            titleBarSettingsRT.anchorMax = new Vector2(0.5f, 0.5f);
            titleBarSettingsRT.pivot = new Vector2(0.5f, 0.5f);
            // Keep clear of language button (-276): push Settings further left.
            titleBarSettingsRT.anchoredPosition = new Vector2(-324, 0);
            _titleBarSettingsBtnRT = titleBarSettingsRT;
            VPBUiFont.ApplyTo(titleBarSettingsBtnText);
            AddDynamicTooltip(titleBarSettingsBtn, BuildPluginInfoTooltip);

            // Filter Presets Button (match Creator dropdown chrome)
            GameObject qfToggleBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, " ", 16, 0, 0, AnchorPresets.middleCenter, ToggleQuickFilters);
            qfToggleBtn.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            quickFiltersToggleBtnText = qfToggleBtn.GetComponentInChildren<Text>();
            if (quickFiltersToggleBtnText != null)
            {
                quickFiltersToggleBtnText.text = " ";
                quickFiltersToggleBtnText.color = Color.white;
                quickFiltersToggleBtnText.gameObject.SetActive(false);
            }
            RectTransform qfToggleRT = qfToggleBtn.GetComponent<RectTransform>();
            qfToggleRT.anchorMin = new Vector2(0.5f, 0.5f);
            qfToggleRT.anchorMax = new Vector2(0.5f, 0.5f);
            qfToggleRT.pivot = new Vector2(0.5f, 0.5f);
            // Make room for Creator button between P and search:
            // search left = -160; 4px gap; Creator 40x40 centered -184; 4px gap; P centered -228
            qfToggleRT.anchoredPosition = new Vector2(-228, 0);
            _titleBarQfToggleBtnRT = qfToggleRT;
            VPBUiFont.ApplyTo(quickFiltersToggleBtnText);
            {
                var s = UI.LoadIconSprite("vpb_icons/filter.png", UI.BarIconGlyphTint);
                if (s != null)
                {
                    UI.AddIconToButton(qfToggleBtn, s);
                    Transform iconT = qfToggleBtn.transform.Find("Icon");
                    quickFiltersToggleBtnIconImage = iconT != null ? iconT.GetComponent<Image>() : null;
                    if (quickFiltersToggleBtnIconImage != null) quickFiltersToggleBtnIconImage.color = UI.BarIconGlyphTint;
                }
            }
            AddTooltip(qfToggleBtn, "gallery.tooltip.filter_presets", "Filter Presets — Alt+F toggles floating window; Float as Window detaches; X reattaches");

            // Register inner pane button scale actions (title bar)
            { var rt = titleBarRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(0, GalleryUiDesignTokens.TitleBarHeightRef * s); }); }
            // Title lives inside CategoryQuickSwitch chrome (stretch + MiddleLeft) — do not re-apply
            // legacy free-float (60,10)/(300×40); that top-shifts "Scenes" when pane goes wide.
            { var rt = fpsRT; innerPaneScaleActions.Add(s => { if (rt) rt.sizeDelta = new Vector2(100f * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var go = languageSwitcherBtnGO; var t = _langBtnText; innerPaneScaleActions.Add(s => { if (go) { var rt = go.GetComponent<RectTransform>(); rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); } if (t) { t.resizeTextMaxSize = Mathf.RoundToInt(GalleryUiDesignTokens.FontBodyRef * s); t.resizeTextMinSize = Mathf.RoundToInt(GalleryUiDesignTokens.FontMinRef * s); } }); }
            { var rt = titleSearchRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(rt.sizeDelta.x, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var rt = fileSortTypeRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var rt = fileSortDirRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var rt = ratingSortToggleRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var rt = refreshRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var rt = titleBarSettingsRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var rt = qfToggleRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var go = titleCreatorBtn; innerPaneScaleActions.Add(s => { if (go) go.GetComponent<RectTransform>().sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }

            // Tab Area - Create for all panels so undocked can clone/filter
            if (true)
            {
                float tabAreaWidth = 220f;

                {
                    Color iconTint = UI.BarIconGlyphTint;
                    sceneSourceSortModeSprites = new Sprite[]
                    {
                        UI.LoadIconSprite("vpb_icons/sort_name_asc.png", iconTint),
                        UI.LoadIconSprite("vpb_icons/sort_name_desc.png", iconTint),
                        UI.LoadIconSprite("vpb_icons/sort_number_asc.png", iconTint),
                        UI.LoadIconSprite("vpb_icons/sort_number_desc.png", iconTint),
                    };
                }
                
                // 1. Right Tab Area
                rightTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchRight, tabAreaWidth, 0, Vector2.zero);
                RectTransform rightTabRT = rightTabScrollGO.GetComponent<RectTransform>();
                rightTabRT.anchorMin = new Vector2(1, 0);
                rightTabRT.anchorMax = new Vector2(1, 1);
                rightTabRT.offsetMin = new Vector2(-tabAreaWidth - 10, 68); 
                rightTabRT.offsetMax = new Vector2(-10, -95);

                rightTabContainerGO = rightTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                {
                    var vlg = rightTabContainerGO.GetComponent<VerticalLayoutGroup>();
                    vlg.spacing = GalleryUiDesignTokens.SideTabRowSpacingRef;
                    vlg.padding = new RectOffset(0, Mathf.RoundToInt(GalleryUiDesignTokens.SideTabRowPadRef), 0, 0);
                    innerPaneScaleActions.Add(s => SyncSideTabScrollContentVerticalLayoutOn(vlg, s));
                }
                try { EnsureUserTagAvailScrollTrackingHooks(); } catch { }
                {
                    Transform vp = rightTabScrollGO.transform.Find("Viewport");
                    _rightTabViewportRT = vp != null ? vp.GetComponent<RectTransform>() : null;
                    if (_rightTabViewportRT != null)
                    {
                        _rightTabViewportDefOffsetMin = _rightTabViewportRT.offsetMin;
                        _rightTabViewportDefOffsetMax = _rightTabViewportRT.offsetMax;
                    }
                    rightUserTagsAvailStickyGO = new GameObject("VPB_UserTagsAvailSticky");
                    rightUserTagsAvailStickyGO.transform.SetParent(rightTabScrollGO.transform, false);
                    rightUserTagsAvailStickyGO.SetActive(false);
                    RectTransform rst = rightUserTagsAvailStickyGO.AddComponent<RectTransform>();
                    rst.anchorMin = new Vector2(0f, 1f);
                    rst.anchorMax = new Vector2(1f, 1f);
                    rst.pivot = new Vector2(0.5f, 1f);
                    rst.sizeDelta = Vector2.zero;
                    rightUserTagsAvailStickyGO.transform.SetAsLastSibling();

                    rightUserTagsAvailFooterGO = new GameObject("VPB_UserTagsAvailFooter");
                    rightUserTagsAvailFooterGO.transform.SetParent(rightTabScrollGO.transform, false);
                    rightUserTagsAvailFooterGO.SetActive(false);
                    RectTransform rft = rightUserTagsAvailFooterGO.AddComponent<RectTransform>();
                    rft.anchorMin = new Vector2(0f, 0f);
                    rft.anchorMax = new Vector2(1f, 0f);
                    rft.pivot = new Vector2(0.5f, 0f);
                    rft.sizeDelta = Vector2.zero;
                    rightUserTagsAvailFooterGO.transform.SetAsLastSibling();
                }

                // 1b. Right Sub Tab Area (For Tags split view)
                rightSubTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchRight, tabAreaWidth, 0, Vector2.zero, 15f, 0f, false);
                RectTransform rightSubTabRT = rightSubTabScrollGO.GetComponent<RectTransform>();
                rightSubTabRT.anchorMin = new Vector2(1, 0);
                rightSubTabRT.anchorMax = new Vector2(1, 0.5f); // Bottom half default
                rightSubTabRT.offsetMin = new Vector2(-tabAreaWidth - 10, 68);
                rightSubTabRT.offsetMax = new Vector2(-10, -45);
                
                rightSubTabContainerGO = rightSubTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                {
                    var vlg = rightSubTabContainerGO.GetComponent<VerticalLayoutGroup>();
                    vlg.spacing = GalleryUiDesignTokens.SideTabRowSpacingRef;
                    vlg.padding = new RectOffset(0, Mathf.RoundToInt(GalleryUiDesignTokens.SideTabRowPadRef), 0, 0);
                    innerPaneScaleActions.Add(s => SyncSideTabScrollContentVerticalLayoutOn(vlg, s));
                }
                rightSubTabScrollGO.SetActive(false); // Hidden by default
                {
                    Transform vp = rightSubTabScrollGO.transform.Find("Viewport");
                    _rightSubTabViewportRT = vp != null ? vp.GetComponent<RectTransform>() : null;
                    if (_rightSubTabViewportRT != null)
                    {
                        _rightSubTabViewportDefOffsetMin = _rightSubTabViewportRT.offsetMin;
                        _rightSubTabViewportDefOffsetMax = _rightSubTabViewportRT.offsetMax;
                    }
                    rightUserTagsAppliedStickyGO = new GameObject("VPB_UserTagsAppliedSticky");
                    rightUserTagsAppliedStickyGO.transform.SetParent(rightSubTabScrollGO.transform, false);
                    rightUserTagsAppliedStickyGO.SetActive(false);
                    RectTransform rap = rightUserTagsAppliedStickyGO.AddComponent<RectTransform>();
                    rap.anchorMin = new Vector2(0f, 1f);
                    rap.anchorMax = new Vector2(1f, 1f);
                    rap.pivot = new Vector2(0.5f, 1f);
                    rap.sizeDelta = Vector2.zero;
                    rightUserTagsAppliedStickyGO.transform.SetAsLastSibling();
                }

                // Right Sub Sort Button (tags split: same 35² icon cycle as upper row)
                {
                    Color sortBackdropCol = UI.ChromeDark;
                    rightSubSortBtn = UI.CreateUIButton(backgroundBoxGO, 35f, 35f, " ", 8, 0, 0, AnchorPresets.topRight, null);
                    if (sceneSourceSortModeSprites != null && sceneSourceSortModeSprites[0] != null)
                        UI.AddIconToButton(rightSubSortBtn, sceneSourceSortModeSprites[0], 4f, sortBackdropCol);
                    rightSubSortBtnBackdrop = rightSubSortBtn.GetComponent<Image>();
                    rightSubSortBtnIconImage = rightSubSortBtn.transform.Find("Icon") != null
                        ? rightSubSortBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    rightSubSortBtnText = rightSubSortBtn.GetComponentInChildren<Text>(true);
                    RectTransform rsSubRT = rightSubSortBtn.GetComponent<RectTransform>();
                    rsSubRT.anchorMin = new Vector2(1, 0.5f);
                    rsSubRT.anchorMax = new Vector2(1, 0.5f);
                    rsSubRT.pivot = new Vector2(1, 1);
                    rsSubRT.anchoredPosition = new Vector2(-10f, -10f);
                    rsSubRT.sizeDelta = new Vector2(35f, 35f);
                    { var rt = rsSubRT; innerPaneScaleActions.Add(s => { float sz = GalleryUiDesignTokens.SideTabRowHeightRef * s; rt.sizeDelta = new Vector2(sz, sz); }); }
                    Button rightSubSortButton = rightSubSortBtn.GetComponent<Button>();
                    rightSubSortButton.onClick.RemoveAllListeners();
                    rightSubSortButton.onClick.AddListener(OnRightSubSortButtonClicked);
                }

                {
                    Color backdrop = UI.ChromeDark;
                    rightSubSceneSortBtn = UI.CreateUIButton(backgroundBoxGO, 35f, 35f, " ", 8, 0, 0, AnchorPresets.topRight, null);
                    rightSubSceneSortBtn.name = "SceneSortRight";
                    RectTransform rsSceneRT = rightSubSceneSortBtn.GetComponent<RectTransform>();
                    rsSceneRT.anchorMin = new Vector2(1, 0.5f);
                    rsSceneRT.anchorMax = new Vector2(1, 0.5f);
                    rsSceneRT.pivot = new Vector2(1, 1);
                    rsSceneRT.anchoredPosition = new Vector2(-10f, -10f);
                    rsSceneRT.sizeDelta = new Vector2(35f, 35f);
                    if (sceneSourceSortModeSprites != null && sceneSourceSortModeSprites[0] != null)
                        UI.AddIconToButton(rightSubSceneSortBtn, sceneSourceSortModeSprites[0], 4f, backdrop);
                    rightSubSceneSortBtnBackdrop = rightSubSceneSortBtn.GetComponent<Image>();
                    rightSubSceneSortIconImage = rightSubSceneSortBtn.transform.Find("Icon") != null
                        ? rightSubSceneSortBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    rightSubSceneSortBtn.GetComponent<Button>().onClick.RemoveAllListeners();
                    rightSubSceneSortBtn.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        RectTransform rt = rightSubSceneSortBtn != null ? rightSubSceneSortBtn.GetComponent<RectTransform>() : null;
                        ToggleSidePaneSortMenu("SceneSource", rt);
                    });
                    rightSubSceneSortBtn.SetActive(false);
                }

                // Right Sub Search
                rightSubSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 60f, (val) => {
                    bool utAppliedPane = rightActiveContent == ContentType.UserTags && rightSubTabScrollGO != null && rightSubTabScrollGO.activeSelf;
                    if (utAppliedPane)
                        userTagAppliedFilter = val;
                    else
                    {
                        tagFilter = val;
                    }
                    UpdateTabs();
                }, () => {
                    bool utAppliedPane = rightActiveContent == ContentType.UserTags && rightSubTabScrollGO != null && rightSubTabScrollGO.activeSelf;
                    if (utAppliedPane)
                    {
                        userTagAppliedFilter = "";
                        UpdateTabs();
                        return;
                    }
                    tagFilter = "";
                    UpdateTabs();
                });
                RectTransform rSubSearchRT = rightSubSearchInput.GetComponent<RectTransform>();
                rSubSearchRT.anchorMin = new Vector2(1, 0.5f);
                rSubSearchRT.anchorMax = new Vector2(1, 0.5f);
                rSubSearchRT.pivot = new Vector2(1, 1);
                rSubSearchRT.anchoredPosition = new Vector2(-10, -10);
                {
                    innerPaneScaleActions.Add(s =>
                    {
                        ApplyRightSubSearchLayoutScaled(s);
                        ApplySideTabSubSortButtonLayout(rightSubSceneSortBtn, false, s);
                        ApplySideTabSubSortButtonLayout(rightSubSortBtn, false, s);
                        SyncSceneSourceSortButtonHighlights();
                    });
                }
                
                // Right Sub Clear Button
                rightSubClearBtn = UI.CreateUIButton(backgroundBoxGO, tabAreaWidth, 35, VPBTranslation.T("gallery.tags.clear_selected", "Clear Selected"), 14, 0, 0, AnchorPresets.bottomRight, () => {
                    activeTags.Clear();
                    RefreshFiles();
                    UpdateTabs();
                });
                rightSubClearBtn.GetComponent<Image>().color = UI.AccentRed; // Dark Red
                rightSubClearBtnText = rightSubClearBtn.GetComponentInChildren<Text>();
                rightSubClearBtnText.color = Color.white;
                
                RectTransform rSubClearRT = rightSubClearBtn.GetComponent<RectTransform>();
                rSubClearRT.anchorMin = new Vector2(1, 0);
                rSubClearRT.anchorMax = new Vector2(1, 0);
                rSubClearRT.pivot = new Vector2(1, 0);
                rSubClearRT.anchoredPosition = new Vector2(-10, 68);
                { var rt = rSubClearRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(220f*s, 35f*s); }); }
                rightSubClearBtn.SetActive(false);

                rightSubSortBtn.SetActive(false);
                rightSubSearchInput.gameObject.SetActive(false);

                // Right Sort Button (upper pane: same icon + 4-mode cycle as scene row)
                {
                    Color sortBackdropCol = UI.ChromeDark;
                    rightSortBtn = UI.CreateUIButton(backgroundBoxGO, 35f, 35f, " ", 8, 0, 0, AnchorPresets.topRight, null);
                    if (sceneSourceSortModeSprites != null && sceneSourceSortModeSprites[0] != null)
                        UI.AddIconToButton(rightSortBtn, sceneSourceSortModeSprites[0], 4f, sortBackdropCol);
                    rightSortBtnBackdrop = rightSortBtn.GetComponent<Image>();
                    rightSortBtnIconImage = rightSortBtn.transform.Find("Icon") != null
                        ? rightSortBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    rightSortBtnText = rightSortBtn.GetComponentInChildren<Text>(true);
                    RectTransform rsRT = rightSortBtn.GetComponent<RectTransform>();
                    rsRT.anchorMin = new Vector2(1, 1);
                    rsRT.anchorMax = new Vector2(1, 1);
                    rsRT.pivot = new Vector2(1, 1);
                    rsRT.anchoredPosition = new Vector2(-190, -65);
                    rsRT.sizeDelta = new Vector2(35f, 35f);
                    Button rightSortButton = rightSortBtn.GetComponent<Button>();
                    rightSortButton.onClick.RemoveAllListeners();
                    rightSortButton.onClick.AddListener(() =>
                    {
                        ContentType? ct = rightActiveContent;
                        if (!ct.HasValue) return;
                        RectTransform rt = rightSortBtn != null ? rightSortBtn.GetComponent<RectTransform>() : null;
                        ToggleSidePaneSortMenu(ct.Value.ToString(), rt);
                    });
                }

                // Right Refresh Button (to the right of Sort, still left of Search)
                rightRefreshBtn = UI.CreateUIButton(backgroundBoxGO, 40, 35, VPBTranslation.T("gallery.icon.refresh", "⟳"), 18, 0, 0, AnchorPresets.topRight, null);
                rightRefreshBtn.GetComponent<Image>().color = UI.ChromeDark;
                rightRefreshBtn.GetComponentInChildren<Text>().color = Color.white;
                RectTransform rrRT = rightRefreshBtn.GetComponent<RectTransform>();
                rrRT.anchorMin = new Vector2(1, 1);
                rrRT.anchorMax = new Vector2(1, 1);
                rrRT.pivot = new Vector2(1, 1);
                rrRT.anchoredPosition = new Vector2(-145, -65); // Between Sort and Search

                rightRefreshBtnText = rightRefreshBtn.GetComponentInChildren<Text>();
                Button rightRefreshButton = rightRefreshBtn.GetComponent<Button>();
                rightRefreshButton.onClick.RemoveAllListeners();

                EventTrigger rightRefreshEt = rightRefreshBtn.GetComponent<EventTrigger>();
                if (rightRefreshEt == null) rightRefreshEt = rightRefreshBtn.AddComponent<EventTrigger>();
                var rightRefreshPointerClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                rightRefreshPointerClick.callback.AddListener((data) =>
                {
                    var ped = (PointerEventData)data;
                    if (ped.button == PointerEventData.InputButton.Right)
                        UserRequestedNativeFileManagerRefresh();
                    else if (ped.button == PointerEventData.InputButton.Left)
                        UserRequestedPackageRefresh();
                });
                rightRefreshEt.triggers.Add(rightRefreshPointerClick);

                AddTooltip(rightRefreshBtn, "gallery.tooltip.refresh_packages", "Refresh Packages (right-click: VaM file list only)");

                _rightMainSideSearchOnValueChanged = (val) => {
                    if (_suppressMainSideSearchValueChanged) return;
                    if (rightActiveContent == ContentType.Category) categoryFilter = val;
                    else if (rightActiveContent == ContentType.Creator) creatorFilter = val;
                    else if (rightActiveContent == ContentType.UserTags) userTagFilter = val;
                    else if (rightActiveContent == ContentType.Path) pathFilter = val;
                    else if (rightActiveContent == ContentType.History) historyTabFilter = val;
                    else if (rightActiveContent == ContentType.Settings) settingsFilter = val;
                    else if (rightActiveContent == ContentType.RemoveClothing) removeClothingFilter = val;
                    else if (rightActiveContent == ContentType.RemoveHair) removeHairFilter = val;
                    else if (rightActiveContent == ContentType.RemoveAtom) removeAtomFilter = val;
                    else if (rightActiveContent == ContentType.Target) targetFilter = val;
                    UpdateTabs();
                    if (rightActiveContent == ContentType.Settings)
                        try { RefreshInternalSettingsListRows(true); } catch { }
                };
                rightSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, _rightMainSideSearchOnValueChanged, () => {
                    if (rightActiveContent == ContentType.Creator) {
                        ClearCreatorFilters();
                        OnCreatorFilterChanged(refreshFilesAndTabs: true);
                    }
                    else if (rightActiveContent == ContentType.UserTags) {
                        userTagFilter = "";
                        activeUserTags.Clear();
                        excludedUserTags.Clear();
                        userTagsCached = false;
                        if (_userTagAvailMode != UserTagAvailMode.Tag) { try { RefreshFiles(true); } catch { } }
                        try { UpdateTabs(); } catch { }
                    }
                    else if (rightActiveContent == ContentType.Path) {
                        currentPackagePathFilter = "";
                        categoriesCached = false;
                        creatorsCached = false;
                        tagsCached = false;
                        RefreshFiles();
                        UpdateTabs();
                    }
                    else if (rightActiveContent == ContentType.RemoveClothing) {
                        removeClothingFilter = "";
                        UpdateTabs();
                    }
                    else if (rightActiveContent == ContentType.RemoveHair) {
                        removeHairFilter = "";
                        UpdateTabs();
                    }
                    else if (rightActiveContent == ContentType.RemoveAtom) {
                        removeAtomFilter = "";
                        UpdateTabs();
                    }
                    else if (rightActiveContent == ContentType.Target) {
                        targetFilter = "";
                        UpdateTabs();
                    }
                    else if (rightActiveContent == ContentType.History) {
                        historyTabFilter = "";
                        UpdateTabs();
                    }
                    else if (rightActiveContent == ContentType.Settings) {
                        settingsFilter = "";
                        UpdateTabs();
                        try { RefreshInternalSettingsListRows(true); } catch { }
                    }
                });
                RectTransform rSearchRT = rightSearchInput.GetComponent<RectTransform>();
                rSearchRT.anchorMin = new Vector2(1, 1);
                rSearchRT.anchorMax = new Vector2(1, 1);
                rSearchRT.pivot = new Vector2(1, 1);
                rSearchRT.anchoredPosition = new Vector2(-10, -65);
                innerPaneScaleActions.Add(s => ApplyMainSideSearchRowLayout(false, s));

                // 2. Left Tab Area
                leftTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchLeft, tabAreaWidth, 0, Vector2.zero);
                RectTransform leftTabRT = leftTabScrollGO.GetComponent<RectTransform>();
                leftTabRT.anchorMin = new Vector2(0, 0);
                leftTabRT.anchorMax = new Vector2(0, 1);
                leftTabRT.offsetMin = new Vector2(10, 70);
                leftTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -95);

                leftTabContainerGO = leftTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                {
                    var vlg = leftTabContainerGO.GetComponent<VerticalLayoutGroup>();
                    vlg.spacing = GalleryUiDesignTokens.SideTabRowSpacingRef;
                    vlg.padding = new RectOffset(0, Mathf.RoundToInt(GalleryUiDesignTokens.SideTabRowPadRef), 0, 0);
                    innerPaneScaleActions.Add(s => SyncSideTabScrollContentVerticalLayoutOn(vlg, s));
                }
                leftTabScrollGO.SetActive(false); // Hidden by default
                try { EnsureUserTagAvailScrollTrackingHooks(); } catch { }
                {
                    Transform vp = leftTabScrollGO.transform.Find("Viewport");
                    _leftTabViewportRT = vp != null ? vp.GetComponent<RectTransform>() : null;
                    if (_leftTabViewportRT != null)
                    {
                        _leftTabViewportDefOffsetMin = _leftTabViewportRT.offsetMin;
                        _leftTabViewportDefOffsetMax = _leftTabViewportRT.offsetMax;
                    }
                    leftUserTagsAvailStickyGO = new GameObject("VPB_UserTagsAvailSticky");
                    leftUserTagsAvailStickyGO.transform.SetParent(leftTabScrollGO.transform, false);
                    leftUserTagsAvailStickyGO.SetActive(false);
                    RectTransform lst = leftUserTagsAvailStickyGO.AddComponent<RectTransform>();
                    lst.anchorMin = new Vector2(0f, 1f);
                    lst.anchorMax = new Vector2(1f, 1f);
                    lst.pivot = new Vector2(0.5f, 1f);
                    lst.sizeDelta = Vector2.zero;
                    leftUserTagsAvailStickyGO.transform.SetAsLastSibling();

                    leftUserTagsAvailFooterGO = new GameObject("VPB_UserTagsAvailFooter");
                    leftUserTagsAvailFooterGO.transform.SetParent(leftTabScrollGO.transform, false);
                    leftUserTagsAvailFooterGO.SetActive(false);
                    RectTransform lft = leftUserTagsAvailFooterGO.AddComponent<RectTransform>();
                    lft.anchorMin = new Vector2(0f, 0f);
                    lft.anchorMax = new Vector2(1f, 0f);
                    lft.pivot = new Vector2(0.5f, 0f);
                    lft.sizeDelta = Vector2.zero;
                    leftUserTagsAvailFooterGO.transform.SetAsLastSibling();
                }

                // 2b. Left Sub Tab Area (For Tags split view)
                leftSubTabScrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.vStretchLeft, tabAreaWidth, 0, Vector2.zero, 15f, 0f, false);
                RectTransform leftSubTabRT = leftSubTabScrollGO.GetComponent<RectTransform>();
                leftSubTabRT.anchorMin = new Vector2(0, 0);
                leftSubTabRT.anchorMax = new Vector2(0, 0.5f); // Bottom half default
                leftSubTabRT.offsetMin = new Vector2(10, 68);
                leftSubTabRT.offsetMax = new Vector2(tabAreaWidth + 10, -45);
                
                leftSubTabContainerGO = leftSubTabScrollGO.GetComponent<ScrollRect>().content.gameObject;
                {
                    var vlg = leftSubTabContainerGO.GetComponent<VerticalLayoutGroup>();
                    vlg.spacing = GalleryUiDesignTokens.SideTabRowSpacingRef;
                    vlg.padding = new RectOffset(0, Mathf.RoundToInt(GalleryUiDesignTokens.SideTabRowPadRef), 0, 0);
                    innerPaneScaleActions.Add(s => SyncSideTabScrollContentVerticalLayoutOn(vlg, s));
                }
                leftSubTabScrollGO.SetActive(false); // Hidden by default
                {
                    Transform vp = leftSubTabScrollGO.transform.Find("Viewport");
                    _leftSubTabViewportRT = vp != null ? vp.GetComponent<RectTransform>() : null;
                    if (_leftSubTabViewportRT != null)
                    {
                        _leftSubTabViewportDefOffsetMin = _leftSubTabViewportRT.offsetMin;
                        _leftSubTabViewportDefOffsetMax = _leftSubTabViewportRT.offsetMax;
                    }
                    leftUserTagsAppliedStickyGO = new GameObject("VPB_UserTagsAppliedSticky");
                    leftUserTagsAppliedStickyGO.transform.SetParent(leftSubTabScrollGO.transform, false);
                    leftUserTagsAppliedStickyGO.SetActive(false);
                    RectTransform lap = leftUserTagsAppliedStickyGO.AddComponent<RectTransform>();
                    lap.anchorMin = new Vector2(0f, 1f);
                    lap.anchorMax = new Vector2(1f, 1f);
                    lap.pivot = new Vector2(0.5f, 1f);
                    lap.sizeDelta = Vector2.zero;
                    leftUserTagsAppliedStickyGO.transform.SetAsLastSibling();
                }

                // Left Sub Sort Button (tags split: same 35² icon cycle as upper row)
                {
                    Color sortBackdropCol = UI.ChromeDark;
                    leftSubSortBtn = UI.CreateUIButton(backgroundBoxGO, 35f, 35f, " ", 8, 0, 0, AnchorPresets.topLeft, null);
                    if (sceneSourceSortModeSprites != null && sceneSourceSortModeSprites[0] != null)
                        UI.AddIconToButton(leftSubSortBtn, sceneSourceSortModeSprites[0], 4f, sortBackdropCol);
                    leftSubSortBtnBackdrop = leftSubSortBtn.GetComponent<Image>();
                    leftSubSortBtnIconImage = leftSubSortBtn.transform.Find("Icon") != null
                        ? leftSubSortBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    leftSubSortBtnText = leftSubSortBtn.GetComponentInChildren<Text>(true);
                    RectTransform lsSubRT = leftSubSortBtn.GetComponent<RectTransform>();
                    lsSubRT.anchorMin = new Vector2(0, 0.5f);
                    lsSubRT.anchorMax = new Vector2(0, 0.5f);
                    lsSubRT.pivot = new Vector2(0, 1);
                    lsSubRT.anchoredPosition = new Vector2(10f, -10f);
                    lsSubRT.sizeDelta = new Vector2(35f, 35f);
                    { var rt = lsSubRT; innerPaneScaleActions.Add(s => { float sz = GalleryUiDesignTokens.SideTabRowHeightRef * s; rt.sizeDelta = new Vector2(sz, sz); }); }
                    Button leftSubSortButton = leftSubSortBtn.GetComponent<Button>();
                    leftSubSortButton.onClick.RemoveAllListeners();
                    leftSubSortButton.onClick.AddListener(OnLeftSubSortButtonClicked);
                }

                // Scene sub-pane: one square button cycling 4 file-sort modes
                {
                    Color backdrop = UI.ChromeDark;
                    leftSubSceneSortBtn = UI.CreateUIButton(backgroundBoxGO, 35f, 35f, " ", 8, 0, 0, AnchorPresets.topLeft, null);
                    leftSubSceneSortBtn.name = "SceneSortLeft";
                    RectTransform lsSceneRT = leftSubSceneSortBtn.GetComponent<RectTransform>();
                    lsSceneRT.anchorMin = new Vector2(0, 0.5f);
                    lsSceneRT.anchorMax = new Vector2(0, 0.5f);
                    lsSceneRT.pivot = new Vector2(0, 1);
                    lsSceneRT.anchoredPosition = new Vector2(10f, -10f);
                    lsSceneRT.sizeDelta = new Vector2(35f, 35f);
                    if (sceneSourceSortModeSprites != null && sceneSourceSortModeSprites[0] != null)
                        UI.AddIconToButton(leftSubSceneSortBtn, sceneSourceSortModeSprites[0], 4f, backdrop);
                    leftSubSceneSortBtnBackdrop = leftSubSceneSortBtn.GetComponent<Image>();
                    leftSubSceneSortIconImage = leftSubSceneSortBtn.transform.Find("Icon") != null
                        ? leftSubSceneSortBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    leftSubSceneSortBtn.GetComponent<Button>().onClick.RemoveAllListeners();
                    leftSubSceneSortBtn.GetComponent<Button>().onClick.AddListener(() =>
                    {
                        RectTransform rt = leftSubSceneSortBtn != null ? leftSubSceneSortBtn.GetComponent<RectTransform>() : null;
                        ToggleSidePaneSortMenu("SceneSource", rt);
                    });
                    leftSubSceneSortBtn.SetActive(false);
                }

                // Left Sub Search
                leftSubSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 60f, (val) => {
                    bool utAppliedPane = leftActiveContent == ContentType.UserTags && leftSubTabScrollGO != null && leftSubTabScrollGO.activeSelf;
                    if (utAppliedPane)
                        userTagAppliedFilter = val;
                    else
                    {
                        tagFilter = val;
                    }
                    UpdateTabs();
                }, () => {
                    bool utAppliedPane = leftActiveContent == ContentType.UserTags && leftSubTabScrollGO != null && leftSubTabScrollGO.activeSelf;
                    if (utAppliedPane)
                    {
                        userTagAppliedFilter = "";
                        UpdateTabs();
                        return;
                    }
                    tagFilter = "";
                    UpdateTabs();
                });
                RectTransform lSubSearchRT = leftSubSearchInput.GetComponent<RectTransform>();
                lSubSearchRT.anchorMin = new Vector2(0, 0.5f);
                lSubSearchRT.anchorMax = new Vector2(0, 0.5f);
                lSubSearchRT.pivot = new Vector2(0, 1);
                lSubSearchRT.anchoredPosition = new Vector2(50, -10);
                {
                    var rt = lSubSearchRT;
                    innerPaneScaleActions.Add(s =>
                    {
                        ApplyLeftSubSearchLayoutScaled(s);
                        ApplySideTabSubSortButtonLayout(leftSubSceneSortBtn, true, s);
                        ApplySideTabSubSortButtonLayout(leftSubSortBtn, true, s);
                        SyncSceneSourceSortButtonHighlights();
                    });
                }

                // Left Sub Clear Button
                leftSubClearBtn = UI.CreateUIButton(backgroundBoxGO, tabAreaWidth, 35, VPBTranslation.T("gallery.tags.clear_selected", "Clear Selected"), 14, 0, 0, AnchorPresets.bottomLeft, () => {
                    activeTags.Clear();
                    RefreshFiles();
                    UpdateTabs();
                });
                leftSubClearBtn.GetComponent<Image>().color = UI.AccentRed; // Dark Red
                leftSubClearBtnText = leftSubClearBtn.GetComponentInChildren<Text>();
                leftSubClearBtnText.color = Color.white;
                
                RectTransform lSubClearRT = leftSubClearBtn.GetComponent<RectTransform>();
                lSubClearRT.anchorMin = new Vector2(0, 0);
                lSubClearRT.anchorMax = new Vector2(0, 0);
                lSubClearRT.pivot = new Vector2(0, 0);
                lSubClearRT.anchoredPosition = new Vector2(10, 68);
                { var rt = lSubClearRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(220f*s, 35f*s); }); }
                leftSubClearBtn.SetActive(false);

                leftSubSortBtn.SetActive(false);
                leftSubSearchInput.gameObject.SetActive(false);

                // Left Sort Button (upper pane: same icon + 4-mode cycle as scene row)
                {
                    Color sortBackdropCol = UI.ChromeDark;
                    leftSortBtn = UI.CreateUIButton(backgroundBoxGO, 35f, 35f, " ", 8, 0, 0, AnchorPresets.topLeft, null);
                    if (sceneSourceSortModeSprites != null && sceneSourceSortModeSprites[0] != null)
                        UI.AddIconToButton(leftSortBtn, sceneSourceSortModeSprites[0], 4f, sortBackdropCol);
                    leftSortBtnBackdrop = leftSortBtn.GetComponent<Image>();
                    leftSortBtnIconImage = leftSortBtn.transform.Find("Icon") != null
                        ? leftSortBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    leftSortBtnText = leftSortBtn.GetComponentInChildren<Text>(true);
                    RectTransform lsRT = leftSortBtn.GetComponent<RectTransform>();
                    lsRT.anchorMin = new Vector2(0, 1);
                    lsRT.anchorMax = new Vector2(0, 1);
                    lsRT.pivot = new Vector2(0, 1);
                    lsRT.anchoredPosition = new Vector2(10, -65);
                    lsRT.sizeDelta = new Vector2(35f, 35f);
                    Button leftSortButton = leftSortBtn.GetComponent<Button>();
                    leftSortButton.onClick.RemoveAllListeners();
                    leftSortButton.onClick.AddListener(() =>
                    {
                        ContentType? ct = leftActiveContent;
                        if (!ct.HasValue) return;
                        RectTransform rt = leftSortBtn != null ? leftSortBtn.GetComponent<RectTransform>() : null;
                        ToggleSidePaneSortMenu(ct.Value.ToString(), rt);
                    });
                }

                _leftMainSideSearchOnValueChanged = (val) => {
                    if (_suppressMainSideSearchValueChanged) return;
                    if (leftActiveContent == ContentType.Category) categoryFilter = val;
                    else if (leftActiveContent == ContentType.Creator) creatorFilter = val;
                    else if (leftActiveContent == ContentType.UserTags) userTagFilter = val;
                    else if (leftActiveContent == ContentType.Path) pathFilter = val;
                    else if (leftActiveContent == ContentType.History) historyTabFilter = val;
                    else if (leftActiveContent == ContentType.Settings) settingsFilter = val;
                    else if (leftActiveContent == ContentType.RemoveClothing) removeClothingFilter = val;
                    else if (leftActiveContent == ContentType.RemoveHair) removeHairFilter = val;
                    else if (leftActiveContent == ContentType.RemoveAtom) removeAtomFilter = val;
                    else if (leftActiveContent == ContentType.Target) targetFilter = val;
                    UpdateTabs();
                    if (leftActiveContent == ContentType.Settings)
                        try { RefreshInternalSettingsListRows(true); } catch { }
                };
                leftSearchInput = CreateSearchInput(backgroundBoxGO, tabAreaWidth - 45f, _leftMainSideSearchOnValueChanged, () => {
                    if (leftActiveContent == ContentType.Creator) {
                        ClearCreatorFilters();
                        OnCreatorFilterChanged(refreshFilesAndTabs: true);
                    }
                    else if (leftActiveContent == ContentType.UserTags) {
                        userTagFilter = "";
                        activeUserTags.Clear();
                        excludedUserTags.Clear();
                        userTagsCached = false;
                        if (_userTagAvailMode != UserTagAvailMode.Tag) { try { RefreshFiles(true); } catch { } }
                        try { UpdateTabs(); } catch { }
                    }
                    else if (leftActiveContent == ContentType.Path) {
                        currentPackagePathFilter = "";
                        categoriesCached = false;
                        creatorsCached = false;
                        tagsCached = false;
                        RefreshFiles();
                        UpdateTabs();
                    }
                    else if (leftActiveContent == ContentType.RemoveClothing) {
                        removeClothingFilter = "";
                        UpdateTabs();
                    }
                    else if (leftActiveContent == ContentType.RemoveHair) {
                        removeHairFilter = "";
                        UpdateTabs();
                    }
                    else if (leftActiveContent == ContentType.RemoveAtom) {
                        removeAtomFilter = "";
                        UpdateTabs();
                    }
                    else if (leftActiveContent == ContentType.Target) {
                        targetFilter = "";
                        UpdateTabs();
                    }
                    else if (leftActiveContent == ContentType.History) {
                        historyTabFilter = "";
                        UpdateTabs();
                    }
                    else if (leftActiveContent == ContentType.Settings) {
                        settingsFilter = "";
                        UpdateTabs();
                        try { RefreshInternalSettingsListRows(true); } catch { }
                    }
                });
                RectTransform lSearchRT = leftSearchInput.GetComponent<RectTransform>();
                lSearchRT.anchorMin = new Vector2(0, 1);
                lSearchRT.anchorMax = new Vector2(0, 1);
                lSearchRT.pivot = new Vector2(0, 1);
                lSearchRT.anchoredPosition = new Vector2(50, -65);
                innerPaneScaleActions.Add(s => ApplyMainSideSearchRowLayout(true, s));

                try { SyncSideTabColumnHorizontalInsets(1f); } catch { }

                // Right Button Container
                rightSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0f), AnchorPresets.middleRight, 130, 700, new Vector2(140, 0));
                sideButtonGroups.Add(rightSideContainer.AddComponent<CanvasGroup>());
                AddHoverDelegate(rightSideContainer);
                AddSubmenuSideHoverTrigger(rightSideContainer, false);

                // Full-height hover strip to cover top/bottom gaps outside the 700px side container
                rightSideHoverStrip = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0f), AnchorPresets.vStretchRight, GallerySideHoverStripWidth, 0, new Vector2(GallerySideHoverStripOffset, 0));
                AddHoverDelegate(rightSideHoverStrip);
                AddSubmenuSideHoverTrigger(rightSideHoverStrip, false);
                try
                {
                    // Ensure it doesn't intercept clicks on actual buttons (place behind container)
                    rightSideHoverStrip.transform.SetAsFirstSibling();
                }
                catch { }

                // Right Toggle Buttons — sizes/spacing match GalleryUiDesignTokens (ApplySideButtonScale / UpdateSideButtonPositions).
                int btnFontSize = GalleryUiDesignTokens.FontBodyRef;
                float btnWidth = GalleryUiDesignTokens.SideButtonWidthRef;
                float btnHeight = GalleryUiDesignTokens.SideButtonHeightRef;
                float sideIconBtn = GalleryUiDesignTokens.SideButtonSquareRef;
                const float sideIconPad = 6f;
                float spacing = GalleryUiDesignTokens.SideButtonSpacingRef;
                float groupGap = 10f;
                float startY = 320f;

                {
                    Color sideTint = UI.SideRailIconGlyphTint;
                    galleryFloatSprite = UI.LoadIconSprite("vpb_icons/gallery_float.png", sideTint);
                    galleryFixedSprite = UI.LoadIconSprite("vpb_icons/gallery_fixed.png", sideTint);
                    galleryFollowOnSprite = UI.LoadIconSprite("vpb_icons/gallery_follow_on.png", sideTint);
                    galleryFollowOffSprite = UI.LoadIconSprite("vpb_icons/gallery_follow_off.png", sideTint);
                    galleryCloneSprite = UI.LoadIconSprite("vpb_icons/gallery_clone.png", sideTint);
                    gallerySaveSprite = UI.LoadIconSprite("vpb_icons/gallery_save.png", sideTint);
                    galleryCategorySprite = UI.LoadIconSprite("vpb_icons/gallery_category.png", sideTint);
                    galleryCreatorSprite = UI.LoadIconSprite("vpb_icons/gallery_creator.png", sideTint);
                    galleryPathSprite = UI.LoadIconSprite("vpb_icons/folder.png", sideTint);
                    galleryHistorySprite = UI.LoadIconSprite("vpb_icons/history.png", sideTint);
                    galleryCreatorOffSprite = UI.LoadIconSprite("vpb_icons/gallery_creator_off.png", sideTint);
                    targetOnSprite  = UI.LoadIconSprite("vpb_icons/target_on.png",  UI.SideRailIconGlyphTint);
                    targetOffSprite = UI.LoadIconSprite("vpb_icons/target_off.png", UI.SideRailIconGlyphTint);
                    galleryApplySprite = UI.LoadIconSprite("vpb_icons/gallery_apply.png", sideTint);
                    galleryApplyOneClickSprite = UI.LoadIconSprite("vpb_icons/gallery_apply_1click.png", sideTint);
                    galleryApplyTwoClickSprite = UI.LoadIconSprite("vpb_icons/gallery_apply_2click.png", sideTint);
                    if (galleryApplyOneClickSprite == null) galleryApplyOneClickSprite = galleryApplySprite;
                    if (galleryApplyTwoClickSprite == null) galleryApplyTwoClickSprite = galleryApplySprite ?? galleryApplyOneClickSprite;
                    galleryAddSprite = UI.LoadIconSprite("vpb_icons/gallery_add.png", sideTint);
                    galleryReplaceSprite = UI.LoadIconSprite("vpb_icons/gallery_replace.png", sideTint);
                    galleryRemoveSprite = UI.LoadIconSprite("vpb_icons/gallery_remove.png", sideTint)
                        ?? UI.LoadIconSprite("vpb_icons/gallery_remove_atom.png", sideTint);
                }

                float removeCtxW = galleryRemoveSprite != null ? sideIconBtn : btnWidth;
                float removeCtxH = galleryRemoveSprite != null ? sideIconBtn : btnHeight;
                float removeExpandX = (104f / 120f) * removeCtxW;
                bool applyIconMode = galleryApplyOneClickSprite != null || galleryApplyTwoClickSprite != null;

                // Fixed/Floating (Topmost) — square icon; sprite matches next toggle action (see UpdateDesktopModeButton)
                float deskW = (galleryFloatSprite != null || galleryFixedSprite != null) ? sideIconBtn : btnWidth;
                float deskH = (galleryFloatSprite != null || galleryFixedSprite != null) ? sideIconBtn : btnHeight;
                GameObject rightDesktopBtn = UI.CreateUIButton(rightSideContainer, deskW, deskH, " ", 8, 0, startY, AnchorPresets.centre, () => ToggleDesktopModeWithDockHint("Right"));
                rightDesktopModeBtnImage = rightDesktopBtn.GetComponent<Image>();
                rightDesktopModeBtnText = rightDesktopBtn.GetComponentInChildren<Text>(true);
                {
                    Sprite s0 = isFixedLocally ? galleryFloatSprite : galleryFixedSprite;
                    if (s0 != null)
                    {
                        Color c0 = isFixedLocally ? UI.AccentBlue : UI.ChromeDark;
                        UI.AddIconToButton(rightDesktopBtn, s0, sideIconPad, c0);
                        rightDesktopModeBtnIconImage = rightDesktopBtn.transform.Find("Icon") != null
                            ? rightDesktopBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        rightDesktopModeBtnIconImage = null;
                        if (rightDesktopModeBtnText != null)
                        {
                            rightDesktopModeBtnText.text = isFixedLocally
                                ? VPBTranslation.T("gallery.desktop.floating", "Floating")
                                : VPBTranslation.T("gallery.desktop.fixed", "Fixed");
                            rightDesktopModeBtnText.fontSize = btnFontSize;
                            rightDesktopModeBtnText.gameObject.SetActive(true);
                        }
                        rightDesktopModeBtnImage.color = isFixedLocally
                            ? UI.AccentBlue
                            : UI.ChromeDark;
                    }
                }
                rightSideButtons.Add(rightDesktopBtn.GetComponent<RectTransform>());
                AddTooltip(rightDesktopBtn, "gallery.tooltip.desktop_mode", "Toggle fixed vs floating panel (desktop only).");

                // Follow — square icon
                float folW = (galleryFollowOnSprite != null || galleryFollowOffSprite != null) ? sideIconBtn : btnWidth;
                float folH = (galleryFollowOnSprite != null || galleryFollowOffSprite != null) ? sideIconBtn : btnHeight;
                GameObject rightFollowBtn = UI.CreateUIButton(rightSideContainer, folW, folH, " ", 8, 0, startY - spacing - groupGap, AnchorPresets.centre, ToggleFollowMode);
                rightFollowBtnImage = rightFollowBtn.GetComponent<Image>();
                rightFollowBtnText = rightFollowBtn.GetComponentInChildren<Text>(true);
                {
                    Sprite f0 = followUser ? galleryFollowOnSprite : galleryFollowOffSprite;
                    if (f0 != null)
                    {
                        Color fc = followUser ? UI.AccentBlue : UI.ChromeMid;
                        UI.AddIconToButton(rightFollowBtn, f0, sideIconPad, fc);
                        rightFollowBtnIconImage = rightFollowBtn.transform.Find("Icon") != null
                            ? rightFollowBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        rightFollowBtnIconImage = null;
                        if (rightFollowBtnText != null)
                        {
                            rightFollowBtnText.text = followUser
                                ? VPBTranslation.T("gallery.follow.follow", "Follow")
                                : VPBTranslation.T("gallery.follow.static", "Static");
                            rightFollowBtnText.fontSize = btnFontSize;
                            rightFollowBtnText.gameObject.SetActive(true);
                        }
                        rightFollowBtnImage.color = followUser
                            ? UI.AccentBlue
                            : UI.ChromeMid;
                    }
                }
                rightSideButtons.Add(rightFollowBtn.GetComponent<RectTransform>());
                AddTooltip(rightFollowBtn, "gallery.tooltip.follow_mode", "Toggle camera follow for the panel.");

                // Clone — square icon (gray)
                float clW = galleryCloneSprite != null ? sideIconBtn : btnWidth;
                float clH = galleryCloneSprite != null ? sideIconBtn : btnHeight;
                GameObject rightCloneBtn = UI.CreateUIButton(rightSideContainer, clW, clH, " ", 8, 0, startY - spacing * 2 - groupGap * 2, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, true);
                });
                rightCloneBtnText = rightCloneBtn.GetComponentInChildren<Text>(true);
                {
                    Color cloneBackdrop = UI.ChromeMid;
                    if (galleryCloneSprite != null)
                    {
                        UI.AddIconToButton(rightCloneBtn, galleryCloneSprite, sideIconPad, cloneBackdrop);
                        rightCloneBtnIconImage = rightCloneBtn.transform.Find("Icon") != null
                            ? rightCloneBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        rightCloneBtn.GetComponent<Image>().color = cloneBackdrop;
                        if (rightCloneBtnText != null)
                        {
                            rightCloneBtnText.text = VPBTranslation.T("gallery.side.clone", "Clone");
                            rightCloneBtnText.gameObject.SetActive(true);
                        }
                        rightCloneBtnIconImage = null;
                    }
                }
                rightSideButtons.Add(rightCloneBtn.GetComponent<RectTransform>());
                AddTooltip(rightCloneBtn, "gallery.tooltip.clone_pane", "Clone this gallery pane.");

                // Category (Red) — below Tags
                {
                    float cW = galleryCategorySprite != null ? sideIconBtn : btnWidth;
                    float cH = galleryCategorySprite != null ? sideIconBtn : btnHeight;
                    GameObject rightCatBtn = UI.CreateUIButton(rightSideContainer, cW, cH, " ", 8, 0, startY - spacing * 5 - groupGap * 3, AnchorPresets.centre, () => {
                        ToggleSideFromRailButton(ContentType.Category, false, false);
                    });
                    rightCategoryBtnImage = rightCatBtn.GetComponent<Image>();
                    rightCategoryBtnText = rightCatBtn.GetComponentInChildren<Text>(true);
                    if (galleryCategorySprite != null)
                    {
                        UI.AddIconToButton(rightCatBtn, galleryCategorySprite, sideIconPad, ColorCategory);
                        rightCategoryBtnIconImage = rightCatBtn.transform.Find("Icon") != null
                            ? rightCatBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        rightCategoryBtnImage.color = ColorCategory;
                        if (rightCategoryBtnText != null)
                        {
                            rightCategoryBtnText.text = VPBTranslation.T("gallery.side.category", "Categories");
                            rightCategoryBtnText.fontSize = btnFontSize;
                            rightCategoryBtnText.gameObject.SetActive(true);
                        }
                        rightCategoryBtnIconImage = null;
                    }
                    rightSideButtons.Add(rightCatBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(rightCatBtn, () => ToggleSideFromRailButton(ContentType.Category, false, true));
                    AddTooltip(rightCatBtn, "gallery.tooltip.category_list", "Browse all categories. Title = quick switch.");
                }

                // Scene Import — above Tags (sidebar toggle; layout positions dynamically)
                {
                    Color colorSceneImportRail = ColorSceneImport;
                    float impW = sideIconBtn;
                    float impH = sideIconBtn;
                    Sprite impSpr = null;
                    try { impSpr = UI.LoadIconSprite("vpb_icons/import.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject rightSceneImportBtn = UI.CreateUIButton(rightSideContainer, impW, impH, " ", 8, 0, startY - spacing * 3 - groupGap * 3, AnchorPresets.centre, () => OpenImportSidebarFromSideButton(false, false));
                    rightSceneImportSideBtn = rightSceneImportBtn;
                    rightSceneImportBtn.SetActive(false);
                    Image impImg = rightSceneImportBtn.GetComponent<Image>();
                    Text impTxt = rightSceneImportBtn.GetComponentInChildren<Text>(true);
                    if (impSpr != null)
                        UI.AddIconToButton(rightSceneImportBtn, impSpr, sideIconPad, colorSceneImportRail);
                    else if (impImg != null)
                    {
                        impImg.color = colorSceneImportRail;
                        if (impTxt != null)
                        {
                            impTxt.text = VPBTranslation.T("gallery.side.scene_import_short", "Import");
                            impTxt.fontSize = btnFontSize;
                            impTxt.gameObject.SetActive(true);
                        }
                    }
                    rightSideButtons.Add(rightSceneImportBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(rightSceneImportBtn, () => OpenImportSidebarFromSideButton(false, true));
                    AddTooltip(rightSceneImportBtn, "gallery.tooltip.scene_import", "Open the Import sidebar for the selected scene");
                }

                // User-defined tags (SQLite) — above Category
                {
                    Color colorUserTagRail = new Color(0.14f, 0.42f, 0.48f, 1f);
                    float utW = sideIconBtn;
                    float utH = sideIconBtn;
                    Sprite utSpr = null;
                    try { utSpr = UI.LoadIconSprite("vpb_icons/tags.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject rightUserTagsBtn = UI.CreateUIButton(rightSideContainer, utW, utH, " ", 8, 0, startY - spacing * 4 - groupGap * 3, AnchorPresets.centre, () =>
                    {
                        ToggleSideFromRailButton(ContentType.UserTags, false, false);
                    });
                    rightUserTagsSideBtn = rightUserTagsBtn;
                    Image utImg = rightUserTagsBtn.GetComponent<Image>();
                    Text utTxt = rightUserTagsBtn.GetComponentInChildren<Text>(true);
                    if (utSpr != null)
                        UI.AddIconToButton(rightUserTagsBtn, utSpr, sideIconPad, colorUserTagRail);
                    else if (utImg != null)
                    {
                        utImg.color = colorUserTagRail;
                        if (utTxt != null)
                        {
                            utTxt.text = VPBTranslation.T("gallery.side.usertags_short", "Tag");
                            utTxt.fontSize = btnFontSize;
                            utTxt.gameObject.SetActive(true);
                        }
                    }
                    rightSideButtons.Add(rightUserTagsBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(rightUserTagsBtn, () => ToggleSideFromRailButton(ContentType.UserTags, false, true));
                    AddTooltip(rightUserTagsBtn, "gallery.tooltip.user_tags_list", "Your tags (SQLite). Filter here; Edit opens tag manager.");
                }

                // Creator — always create once; hide setting uses SetActive (see ApplyCreatorSideRailButtonVisibility).
                CreateRightCreatorSideRailButton();

                // Path (Blue)
                {
                    float pW = galleryPathSprite != null ? sideIconBtn : btnWidth;
                    float pH = galleryPathSprite != null ? sideIconBtn : btnHeight;
                    GameObject rightPathBtn = UI.CreateUIButton(rightSideContainer, pW, pH, " ", 8, 0, startY - spacing * 8 - groupGap * 3, AnchorPresets.centre, () => {
                        ToggleSideFromRailButton(ContentType.Path, false, false);
                    });
                    rightPathBtnImage = rightPathBtn.GetComponent<Image>();
                    rightPathBtnText = rightPathBtn.GetComponentInChildren<Text>(true);
                    if (galleryPathSprite != null)
                    {
                        UI.AddIconToButton(rightPathBtn, galleryPathSprite, sideIconPad, ColorPath);
                        rightPathBtnIconImage = rightPathBtn.transform.Find("Icon") != null
                            ? rightPathBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        rightPathBtnImage.color = ColorPath;
                        if (rightPathBtnText != null)
                        {
                            rightPathBtnText.text = VPBTranslation.T("gallery.side.path", "Path");
                            rightPathBtnText.fontSize = btnFontSize;
                            rightPathBtnText.gameObject.SetActive(true);
                        }
                        rightPathBtnIconImage = null;
                    }
                    rightSideButtons.Add(rightPathBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(rightPathBtn, () => ToggleSideFromRailButton(ContentType.Path, false, true));
                    AddTooltip(rightPathBtn, "gallery.tooltip.path_list", "Open package and file path list.");
                }

                {
                    GameObject rightHistoryBtn = UI.CreateUIButton(rightSideContainer, sideIconBtn, sideIconBtn, " ", 8, 0, startY - spacing * 9 - groupGap * 3, AnchorPresets.centre, () => {
                        ToggleSideFromRailButton(ContentType.History, false, false);
                    });
                    rightHistoryBtnImage = rightHistoryBtn.GetComponent<Image>();
                    if (galleryHistorySprite != null)
                    {
                        UI.AddIconToButton(rightHistoryBtn, galleryHistorySprite, sideIconPad, ColorHistory);
                        rightHistoryBtnIconImage = rightHistoryBtn.transform.Find("Icon") != null
                            ? rightHistoryBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else if (rightHistoryBtnImage != null)
                        rightHistoryBtnImage.color = ColorHistory;
                    rightSideButtons.Add(rightHistoryBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(rightHistoryBtn, () => ToggleSideFromRailButton(ContentType.History, false, true));
                    AddTooltip(rightHistoryBtn, "gallery.tooltip.history_list", "Launch history and usage filters.");
                }

                // Apply Mode (Right) — icon swaps 1-click vs 2-click when both assets exist
                {
                    float apW = applyIconMode ? sideIconBtn : btnWidth;
                    float apH = applyIconMode ? sideIconBtn : btnHeight;
                    GameObject rightApplyModeBtn = UI.CreateUIButton(rightSideContainer, apW, apH, " ", 8, 0, startY - spacing * 11 - groupGap * 4, AnchorPresets.centre, ToggleApplyMode);
                    rightApplyModeBtnImage = rightApplyModeBtn.GetComponent<Image>();
                    rightApplyModeBtnText = rightApplyModeBtn.GetComponentInChildren<Text>(true);
                    if (applyIconMode)
                    {
                        Sprite apInitSpr = ItemApplyMode == ApplyMode.SingleClick
                            ? (galleryApplyOneClickSprite ?? galleryApplyTwoClickSprite)
                            : (galleryApplyTwoClickSprite ?? galleryApplyOneClickSprite);
                        Color apInit = ItemApplyMode == ApplyMode.SingleClick
                            ? new Color(0.6f, 0.45f, 0.15f, 1f)
                            : new Color(0.15f, 0.15f, 0.45f, 1f);
                        UI.AddIconToButton(rightApplyModeBtn, apInitSpr, sideIconPad, apInit);
                        rightApplyModeBtnIconImage = rightApplyModeBtn.transform.Find("Icon") != null
                            ? rightApplyModeBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        if (rightApplyModeBtnText != null)
                        {
                            rightApplyModeBtnText.text = ItemApplyMode == ApplyMode.SingleClick
                                ? VPBTranslation.T("gallery.apply.one_click", "1-Click")
                                : VPBTranslation.T("gallery.apply.two_click", "2-Click");
                            rightApplyModeBtnText.fontSize = btnFontSize;
                            rightApplyModeBtnText.gameObject.SetActive(true);
                        }
                        rightApplyModeBtnIconImage = null;
                    }
                    rightSideButtons.Add(rightApplyModeBtn.GetComponent<RectTransform>());
                    AddTooltip(rightApplyModeBtn, "gallery.tooltip.apply_mode", "Toggle 1-click vs 2-click apply.");
                }

                // Remove Item Mode (Right) — hover-to-remove tool: fades the pointed item to half
                // opacity, click removes it (clothing/hair on a Person, or whole CUA/light/mirror/atom).
                {
                    Color colorRemoveModeRail = RemoveModeRailBackdrop;
                    float rmW = sideIconBtn;
                    float rmH = sideIconBtn;
                    Sprite rmSpr = null;
                    try { rmSpr = UI.LoadIconSprite("vpb_icons/delete.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject rightRemoveModeBtn = UI.CreateUIButton(rightSideContainer, rmW, rmH, " ", 8, 0, startY - spacing * 10 - groupGap * 4, AnchorPresets.centre, () => ToggleRemoveMode(false, false));
                    rightRemoveModeSideBtn = rightRemoveModeBtn;
                    Image rmImg = rightRemoveModeBtn.GetComponent<Image>();
                    Text rmTxt = rightRemoveModeBtn.GetComponentInChildren<Text>(true);
                    if (rmSpr != null)
                    {
                        UI.AddIconToButton(rightRemoveModeBtn, rmSpr, sideIconPad, colorRemoveModeRail);
                        rightRemoveModeBtnIconImage = rightRemoveModeBtn.transform.Find("Icon") != null
                            ? rightRemoveModeBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else if (rmImg != null)
                    {
                        rmImg.color = colorRemoveModeRail;
                        if (rmTxt != null)
                        {
                            rmTxt.text = VPBTranslation.T("gallery.side.remove_mode_short", "Erase");
                            rmTxt.fontSize = btnFontSize;
                            rmTxt.gameObject.SetActive(true);
                        }
                    }
                    rightRemoveModeBtnOutline = RemoveModeAddRailOutline(rightRemoveModeBtn);
                    rightSideButtons.Add(rightRemoveModeBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(rightRemoveModeBtn, () => ToggleRemoveMode(false, true));
                    AddTooltip(rightRemoveModeBtn, "gallery.tooltip.remove_mode", "Remove Item Mode: point at an item to fade it, click to remove. Also opens the remove list siderail for clothing/hair/scene.");
                }

                // Creator Mode (Right) — sticky scene-tools mode (Strip Scene, …). Not Creators author list.
                {
                    float cmW = sideIconBtn;
                    float cmH = sideIconBtn;
                    Sprite cmSpr = null;
                    try { cmSpr = UI.LoadIconSprite("vpb_icons/creator_mode.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject rightCreatorModeBtn = UI.CreateUIButton(rightSideContainer, cmW, cmH, " ", 8, 0, 0, AnchorPresets.centre, () => ToggleCreatorMode(false, false));
                    rightCreatorModeSideBtn = rightCreatorModeBtn;
                    Image cmImg = rightCreatorModeBtn.GetComponent<Image>();
                    Text cmTxt = rightCreatorModeBtn.GetComponentInChildren<Text>(true);
                    if (cmSpr != null)
                    {
                        UI.AddIconToButton(rightCreatorModeBtn, cmSpr, sideIconPad, CreatorModeRailBackdrop);
                        rightCreatorModeBtnIconImage = rightCreatorModeBtn.transform.Find("Icon") != null
                            ? rightCreatorModeBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else if (cmImg != null)
                    {
                        cmImg.color = CreatorModeRailBackdrop;
                        if (cmTxt != null)
                        {
                            cmTxt.text = VPBTranslation.T("gallery.side.creator_mode_short", "Make");
                            cmTxt.fontSize = btnFontSize;
                            cmTxt.gameObject.SetActive(true);
                        }
                    }
                    rightCreatorModeBtnOutline = CreatorModeAddRailOutline(rightCreatorModeBtn);
                    rightSideButtons.Add(rightCreatorModeBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(rightCreatorModeBtn, () => ToggleCreatorMode(false, true));
                    AddTooltip(rightCreatorModeBtn, "gallery.tooltip.creator_mode",
                        "Creator Mode — sticky scene authoring tools. Not the Creators author list. Ctrl+Shift+K. Esc exits.");
                }

                // Appearance clothing-apply-mode is now a 3-button segmented row docked in the
                // toolbox (see EnsureTboxUI / tboxClothingModeRow), not a side tab.

                // Add/Replace toggle lives in the toolbox (next to Keep Scale).

                {
                    float saveW = gallerySaveSprite != null ? sideIconBtn : btnWidth;
                    float saveH = gallerySaveSprite != null ? sideIconBtn : btnHeight;
                    rightSaveBtnGO = UI.CreateUIButton(rightSideContainer, saveW, saveH, " ", 8, 0, startY - spacing * 14 - groupGap * 4, AnchorPresets.centre, () => {
                        try
                        {
                            ToggleSaveSubmenuFromSideButtons(PreferLeftSidePanelFromRail(false, false));
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("[VPB] Save (Right) exception: " + ex);
                        }
                    });
                    Color saveCol = new Color(0.2f, 0.4f, 0.2f, 1f);
                    if (gallerySaveSprite != null)
                    {
                        UI.AddIconToButton(rightSaveBtnGO, gallerySaveSprite, sideIconPad, saveCol);
                        rightSaveBtnIconImage = rightSaveBtnGO.transform.Find("Icon") != null
                            ? rightSaveBtnGO.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        rightSaveBtnGO.GetComponent<Image>().color = saveCol;
                        var st = rightSaveBtnGO.GetComponentInChildren<Text>(true);
                        if (st != null)
                        {
                            st.text = VPBTranslation.T("gallery.side.save", "Save");
                            st.fontSize = btnFontSize;
                            st.gameObject.SetActive(true);
                        }
                        rightSaveBtnIconImage = null;
                    }
                    rightSideButtons.Add(rightSaveBtnGO.GetComponent<RectTransform>());
                    rightSaveBtnGO.SetActive(false);
                    AddRightClickDelegate(rightSaveBtnGO, () => {
                        try { ToggleSaveSubmenuFromSideButtons(PreferLeftSidePanelFromRail(false, true)); }
                        catch (Exception ex) { LogUtil.LogError("[VPB] Save (Right RMB) exception: " + ex); }
                    });
                    AddTooltip(rightSaveBtnGO, "gallery.tooltip.save_pane", "Save presets and related actions.");
                }


                // Scene Context (Right) — same remove icon as clothing/hair; action depends on active category
                {
                    float rmW = removeCtxW;
                    float rmH = removeCtxH;
                    Color rmCol = UI.AccentRed;
                    rightRemoveAtomBtn = UI.CreateUIButton(rightSideContainer, rmW, rmH, " ", 8, 0, 0, AnchorPresets.centre, () => {
                        try
                        {
                            if (SuperController.singleton == null) return;
                            ToggleAtomSubmenuFromSideButtons(PreferLeftSidePanelFromRail(false, false));
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("[VPB] Remove (scene) (Right) exception: " + ex);
                        }
                    });
                    if (galleryRemoveSprite != null)
                    {
                        UI.AddIconToButton(rightRemoveAtomBtn, galleryRemoveSprite, sideIconPad, rmCol);
                        rightRemoveAtomBtnIconImage = rightRemoveAtomBtn.transform.Find("Icon") != null
                            ? rightRemoveAtomBtn.transform.Find("Icon").GetComponent<Image>() : null;
                        var tx = rightRemoveAtomBtn.GetComponentInChildren<Text>(true);
                        if (tx != null) tx.gameObject.SetActive(false);
                    }
                    else
                    {
                        rightRemoveAtomBtn.GetComponent<Image>().color = rmCol;
                        var tx = rightRemoveAtomBtn.GetComponentInChildren<Text>(true);
                        if (tx != null)
                        {
                            tx.text = VPBTranslation.T("gallery.side.remove", "Remove");
                            tx.fontSize = GalleryUiDesignTokens.FontBodyRef;
                            tx.gameObject.SetActive(true);
                        }
                        rightRemoveAtomBtnIconImage = null;
                    }
                    rightSideButtons.Add(rightRemoveAtomBtn.GetComponent<RectTransform>());
                    rightRemoveAtomBtn.SetActive(false);
                    AddRightClickDelegate(rightRemoveAtomBtn, () => {
                        try
                        {
                            if (SuperController.singleton == null) return;
                            ToggleAtomSubmenuFromSideButtons(PreferLeftSidePanelFromRail(false, true));
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("[VPB] Remove (scene) (Right RMB) exception: " + ex);
                        }
                    });
                    AddTooltip(rightRemoveAtomBtn, "gallery.tooltip.remove_context", "Remove or open remove options for the current category (clothing, hair, or scene).");
                }


                // Context Actions (Right)
                rightRemoveAllClothingBtn = UI.CreateUIButton(rightSideContainer, removeCtxW, removeCtxH, VPBTranslation.T("gallery.side.remove_clothing", "Remove\nClothing"), 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Clothing (Right)");
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleClothingSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(false, false));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing (Right) exception: " + ex);
                    }
                });
                rightRemoveAllClothingBtn.GetComponent<Image>().color = UI.AccentRed;
                {
                    var tx0 = rightRemoveAllClothingBtn.GetComponentInChildren<Text>(true);
                    if (galleryRemoveSprite != null)
                    {
                        UI.AddIconToButton(rightRemoveAllClothingBtn, galleryRemoveSprite, sideIconPad, UI.AccentRed);
                        rightRemoveAllClothingBtnIconImage = rightRemoveAllClothingBtn.transform.Find("Icon") != null
                            ? rightRemoveAllClothingBtn.transform.Find("Icon").GetComponent<Image>() : null;
                        if (tx0 != null) tx0.gameObject.SetActive(false);
                    }
                    else
                    {
                        rightRemoveAllClothingBtnIconImage = null;
                        if (tx0 != null)
                        {
                            tx0.color = Color.white;
                            tx0.gameObject.SetActive(true);
                        }
                    }
                }
                rightSideButtons.Add(rightRemoveAllClothingBtn.GetComponent<RectTransform>());
                rightRemoveAllClothingBtn.SetActive(false);
                AddRightClickDelegate(rightRemoveAllClothingBtn, () => {
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleClothingSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(false, true));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing (Right RMB) exception: " + ex);
                    }
                });
                AddTooltip(rightRemoveAllClothingBtn, "gallery.tooltip.remove_context", "Remove or open remove options for the current category (clothing, hair, or scene).");


                rightRemoveAllHairBtn = UI.CreateUIButton(rightSideContainer, removeCtxW, removeCtxH, VPBTranslation.T("gallery.side.remove_hair", "Remove\nHair"), 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Hair (Right)");
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleHairSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(false, false));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair (Right) exception: " + ex);
                    }
                });
                rightRemoveAllHairBtn.GetComponent<Image>().color = UI.AccentRed;
                {
                    var tx0 = rightRemoveAllHairBtn.GetComponentInChildren<Text>(true);
                    if (galleryRemoveSprite != null)
                    {
                        UI.AddIconToButton(rightRemoveAllHairBtn, galleryRemoveSprite, sideIconPad, UI.AccentRed);
                        rightRemoveAllHairBtnIconImage = rightRemoveAllHairBtn.transform.Find("Icon") != null
                            ? rightRemoveAllHairBtn.transform.Find("Icon").GetComponent<Image>() : null;
                        if (tx0 != null) tx0.gameObject.SetActive(false);
                    }
                    else
                    {
                        rightRemoveAllHairBtnIconImage = null;
                        if (tx0 != null) { tx0.color = Color.white; tx0.gameObject.SetActive(true); }
                    }
                }
                rightSideButtons.Add(rightRemoveAllHairBtn.GetComponent<RectTransform>());
                rightRemoveAllHairBtn.SetActive(false);
                AddRightClickDelegate(rightRemoveAllHairBtn, () => {
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleHairSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(false, true));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair (Right RMB) exception: " + ex);
                    }
                });
                AddTooltip(rightRemoveAllHairBtn, "gallery.tooltip.remove_context", "Remove or open remove options for the current category (clothing, hair, or scene).");


                // Left Button Container
                leftSideContainer = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0f), AnchorPresets.middleLeft, 130, 700, new Vector2(-140, 0));
                sideButtonGroups.Add(leftSideContainer.AddComponent<CanvasGroup>());
                AddHoverDelegate(leftSideContainer);
                AddSubmenuSideHoverTrigger(leftSideContainer, true);

                // Full-height hover strip to cover top/bottom gaps outside the 700px side container
                leftSideHoverStrip = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0f), AnchorPresets.vStretchLeft, GallerySideHoverStripWidth, 0, new Vector2(-GallerySideHoverStripOffset, 0));
                AddHoverDelegate(leftSideHoverStrip);
                AddSubmenuSideHoverTrigger(leftSideHoverStrip, true);
                try
                {
                    // Ensure it doesn't intercept clicks on actual buttons (place behind container)
                    leftSideHoverStrip.transform.SetAsFirstSibling();
                }
                catch { }

                // Left Toggle Buttons (same 50×50 icon row as right; sprites already loaded above)
                // Fixed/Floating (Topmost)
                GameObject leftDesktopBtn = UI.CreateUIButton(leftSideContainer, deskW, deskH, " ", 8, 0, startY, AnchorPresets.centre, () => ToggleDesktopModeWithDockHint("Left"));
                leftDesktopModeBtnImage = leftDesktopBtn.GetComponent<Image>();
                leftDesktopModeBtnText = leftDesktopBtn.GetComponentInChildren<Text>(true);
                {
                    Sprite s0 = isFixedLocally ? galleryFloatSprite : galleryFixedSprite;
                    if (s0 != null)
                    {
                        Color c0 = isFixedLocally ? UI.AccentBlue : UI.ChromeDark;
                        UI.AddIconToButton(leftDesktopBtn, s0, sideIconPad, c0);
                        leftDesktopModeBtnIconImage = leftDesktopBtn.transform.Find("Icon") != null
                            ? leftDesktopBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        leftDesktopModeBtnIconImage = null;
                        if (leftDesktopModeBtnText != null)
                        {
                            leftDesktopModeBtnText.text = isFixedLocally
                                ? VPBTranslation.T("gallery.desktop.floating", "Floating")
                                : VPBTranslation.T("gallery.desktop.fixed", "Fixed");
                            leftDesktopModeBtnText.fontSize = btnFontSize;
                            leftDesktopModeBtnText.gameObject.SetActive(true);
                        }
                        leftDesktopModeBtnImage.color = isFixedLocally
                            ? UI.AccentBlue
                            : UI.ChromeDark;
                    }
                }
                leftSideButtons.Add(leftDesktopBtn.GetComponent<RectTransform>());
                AddTooltip(leftDesktopBtn, "gallery.tooltip.desktop_mode", "Toggle fixed vs floating panel (desktop only).");

                // Follow
                GameObject leftFollowBtn = UI.CreateUIButton(leftSideContainer, folW, folH, " ", 8, 0, startY - spacing - groupGap, AnchorPresets.centre, ToggleFollowMode);
                leftFollowBtnImage = leftFollowBtn.GetComponent<Image>();
                leftFollowBtnText = leftFollowBtn.GetComponentInChildren<Text>(true);
                {
                    Sprite f0 = followUser ? galleryFollowOnSprite : galleryFollowOffSprite;
                    if (f0 != null)
                    {
                        Color fc = followUser ? UI.AccentBlue : UI.ChromeMid;
                        UI.AddIconToButton(leftFollowBtn, f0, sideIconPad, fc);
                        leftFollowBtnIconImage = leftFollowBtn.transform.Find("Icon") != null
                            ? leftFollowBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        leftFollowBtnIconImage = null;
                        if (leftFollowBtnText != null)
                        {
                            leftFollowBtnText.text = followUser
                                ? VPBTranslation.T("gallery.follow.follow", "Follow")
                                : VPBTranslation.T("gallery.follow.static", "Static");
                            leftFollowBtnText.fontSize = btnFontSize;
                            leftFollowBtnText.gameObject.SetActive(true);
                        }
                        leftFollowBtnImage.color = followUser
                            ? UI.AccentBlue
                            : UI.ChromeMid;
                    }
                }
                leftSideButtons.Add(leftFollowBtn.GetComponent<RectTransform>());
                AddTooltip(leftFollowBtn, "gallery.tooltip.follow_mode", "Toggle camera follow for the panel.");

                // Clone (Gray)
                GameObject leftCloneBtn = UI.CreateUIButton(leftSideContainer, clW, clH, " ", 8, 0, startY - spacing * 2 - groupGap * 2, AnchorPresets.centre, () => {
                    if (Gallery.singleton != null) Gallery.singleton.ClonePanel(this, false);
                });
                leftCloneBtnText = leftCloneBtn.GetComponentInChildren<Text>(true);
                {
                    Color cloneBackdrop = UI.ChromeMid;
                    if (galleryCloneSprite != null)
                    {
                        UI.AddIconToButton(leftCloneBtn, galleryCloneSprite, sideIconPad, cloneBackdrop);
                        leftCloneBtnIconImage = leftCloneBtn.transform.Find("Icon") != null
                            ? leftCloneBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        leftCloneBtn.GetComponent<Image>().color = cloneBackdrop;
                        if (leftCloneBtnText != null)
                        {
                            leftCloneBtnText.text = VPBTranslation.T("gallery.side.clone", "Clone");
                            leftCloneBtnText.gameObject.SetActive(true);
                        }
                        leftCloneBtnIconImage = null;
                    }
                }
                leftSideButtons.Add(leftCloneBtn.GetComponent<RectTransform>());
                AddTooltip(leftCloneBtn, "gallery.tooltip.clone_pane", "Clone this gallery pane.");

                // Category (Red) — below Tags
                {
                    float cW = galleryCategorySprite != null ? sideIconBtn : btnWidth;
                    float cH = galleryCategorySprite != null ? sideIconBtn : btnHeight;
                    GameObject leftCatBtn = UI.CreateUIButton(leftSideContainer, cW, cH, " ", 8, 0, startY - spacing * 5 - groupGap * 3, AnchorPresets.centre, () => ToggleSideFromRailButton(ContentType.Category, true, false));
                    leftCategoryBtnImage = leftCatBtn.GetComponent<Image>();
                    leftCategoryBtnText = leftCatBtn.GetComponentInChildren<Text>(true);
                    if (galleryCategorySprite != null)
                    {
                        UI.AddIconToButton(leftCatBtn, galleryCategorySprite, sideIconPad, ColorCategory);
                        leftCategoryBtnIconImage = leftCatBtn.transform.Find("Icon") != null
                            ? leftCatBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        leftCategoryBtnImage.color = ColorCategory;
                        if (leftCategoryBtnText != null)
                        {
                            leftCategoryBtnText.text = VPBTranslation.T("gallery.side.category", "Categories");
                            leftCategoryBtnText.fontSize = btnFontSize;
                            leftCategoryBtnText.gameObject.SetActive(true);
                        }
                        leftCategoryBtnIconImage = null;
                    }
                    leftSideButtons.Add(leftCatBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(leftCatBtn, () => ToggleSideFromRailButton(ContentType.Category, true, true));
                    AddTooltip(leftCatBtn, "gallery.tooltip.category_list", "Browse all categories. Title = quick switch.");
                }

                // Scene Import — above Tags (sidebar toggle; layout positions dynamically)
                {
                    Color colorSceneImportRailL = ColorSceneImport;
                    float impW = sideIconBtn;
                    float impH = sideIconBtn;
                    Sprite impSprL = null;
                    try { impSprL = UI.LoadIconSprite("vpb_icons/import.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject leftSceneImportBtn = UI.CreateUIButton(leftSideContainer, impW, impH, " ", 8, 0, startY - spacing * 3 - groupGap * 3, AnchorPresets.centre, () => OpenImportSidebarFromSideButton(true, false));
                    leftSceneImportSideBtn = leftSceneImportBtn;
                    leftSceneImportBtn.SetActive(false);
                    Image impImgL = leftSceneImportBtn.GetComponent<Image>();
                    Text impTxtL = leftSceneImportBtn.GetComponentInChildren<Text>(true);
                    if (impSprL != null)
                        UI.AddIconToButton(leftSceneImportBtn, impSprL, sideIconPad, colorSceneImportRailL);
                    else if (impImgL != null)
                    {
                        impImgL.color = colorSceneImportRailL;
                        if (impTxtL != null)
                        {
                            impTxtL.text = VPBTranslation.T("gallery.side.scene_import_short", "Import");
                            impTxtL.fontSize = btnFontSize;
                            impTxtL.gameObject.SetActive(true);
                        }
                    }
                    leftSideButtons.Add(leftSceneImportBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(leftSceneImportBtn, () => OpenImportSidebarFromSideButton(true, true));
                    AddTooltip(leftSceneImportBtn, "gallery.tooltip.scene_import", "Open the Import sidebar for the selected scene");
                }

                // User-defined tags (SQLite) — above Category
                {
                    Color colorUserTagRailL = new Color(0.14f, 0.42f, 0.48f, 1f);
                    float utW = sideIconBtn;
                    float utH = sideIconBtn;
                    Sprite utSprL = null;
                    try { utSprL = UI.LoadIconSprite("vpb_icons/tags.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject leftUserTagsBtn = UI.CreateUIButton(leftSideContainer, utW, utH, " ", 8, 0, startY - spacing * 4 - groupGap * 3, AnchorPresets.centre, () => ToggleSideFromRailButton(ContentType.UserTags, true, false));
                    leftUserTagsSideBtn = leftUserTagsBtn;
                    Image utImgL = leftUserTagsBtn.GetComponent<Image>();
                    Text utTxtL = leftUserTagsBtn.GetComponentInChildren<Text>(true);
                    if (utSprL != null)
                        UI.AddIconToButton(leftUserTagsBtn, utSprL, sideIconPad, colorUserTagRailL);
                    else if (utImgL != null)
                    {
                        utImgL.color = colorUserTagRailL;
                        if (utTxtL != null)
                        {
                            utTxtL.text = VPBTranslation.T("gallery.side.usertags_short", "Tag");
                            utTxtL.fontSize = btnFontSize;
                            utTxtL.gameObject.SetActive(true);
                        }
                    }
                    leftSideButtons.Add(leftUserTagsBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(leftUserTagsBtn, () => ToggleSideFromRailButton(ContentType.UserTags, true, true));
                    AddTooltip(leftUserTagsBtn, "gallery.tooltip.user_tags_list", "Your tags (SQLite). Filter here; Edit opens tag manager.");
                }

                // Creator — always create once; hide setting uses SetActive (see ApplyCreatorSideRailButtonVisibility).
                CreateLeftCreatorSideRailButton();

                // Path (Blue)
                {
                    float pW = galleryPathSprite != null ? sideIconBtn : btnWidth;
                    float pH = galleryPathSprite != null ? sideIconBtn : btnHeight;
                    GameObject leftPathBtn = UI.CreateUIButton(leftSideContainer, pW, pH, " ", 8, 0, startY - spacing * 8 - groupGap * 3, AnchorPresets.centre, () => ToggleSideFromRailButton(ContentType.Path, true, false));
                    leftPathBtnImage = leftPathBtn.GetComponent<Image>();
                    leftPathBtnText = leftPathBtn.GetComponentInChildren<Text>(true);
                    if (galleryPathSprite != null)
                    {
                        UI.AddIconToButton(leftPathBtn, galleryPathSprite, sideIconPad, ColorPath);
                        leftPathBtnIconImage = leftPathBtn.transform.Find("Icon") != null
                            ? leftPathBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        leftPathBtnImage.color = ColorPath;
                        if (leftPathBtnText != null)
                        {
                            leftPathBtnText.text = VPBTranslation.T("gallery.side.path", "Path");
                            leftPathBtnText.fontSize = btnFontSize;
                            leftPathBtnText.gameObject.SetActive(true);
                        }
                        leftPathBtnIconImage = null;
                    }
                    leftSideButtons.Add(leftPathBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(leftPathBtn, () => ToggleSideFromRailButton(ContentType.Path, true, true));
                    AddTooltip(leftPathBtn, "gallery.tooltip.path_list", "Open package and file path list.");
                }

                {
                    GameObject leftHistoryBtn = UI.CreateUIButton(leftSideContainer, sideIconBtn, sideIconBtn, " ", 8, 0, startY - spacing * 9 - groupGap * 3, AnchorPresets.centre, () => ToggleSideFromRailButton(ContentType.History, true, false));
                    leftHistoryBtnImage = leftHistoryBtn.GetComponent<Image>();
                    if (galleryHistorySprite != null)
                    {
                        UI.AddIconToButton(leftHistoryBtn, galleryHistorySprite, sideIconPad, ColorHistory);
                        leftHistoryBtnIconImage = leftHistoryBtn.transform.Find("Icon") != null
                            ? leftHistoryBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else if (leftHistoryBtnImage != null)
                        leftHistoryBtnImage.color = ColorHistory;
                    leftSideButtons.Add(leftHistoryBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(leftHistoryBtn, () => ToggleSideFromRailButton(ContentType.History, true, true));
                    AddTooltip(leftHistoryBtn, "gallery.tooltip.history_list", "Launch history and usage filters.");
                }

                // Apply Mode (Left)
                {
                    float apW = applyIconMode ? sideIconBtn : btnWidth;
                    float apH = applyIconMode ? sideIconBtn : btnHeight;
                    GameObject leftApplyModeBtn = UI.CreateUIButton(leftSideContainer, apW, apH, " ", 8, 0, startY - spacing * 11 - groupGap * 4, AnchorPresets.centre, ToggleApplyMode);
                    leftApplyModeBtnImage = leftApplyModeBtn.GetComponent<Image>();
                    leftApplyModeBtnText = leftApplyModeBtn.GetComponentInChildren<Text>(true);
                    if (applyIconMode)
                    {
                        Sprite apInitSpr = ItemApplyMode == ApplyMode.SingleClick
                            ? (galleryApplyOneClickSprite ?? galleryApplyTwoClickSprite)
                            : (galleryApplyTwoClickSprite ?? galleryApplyOneClickSprite);
                        Color apInit = ItemApplyMode == ApplyMode.SingleClick
                            ? new Color(0.6f, 0.45f, 0.15f, 1f)
                            : new Color(0.15f, 0.15f, 0.45f, 1f);
                        UI.AddIconToButton(leftApplyModeBtn, apInitSpr, sideIconPad, apInit);
                        leftApplyModeBtnIconImage = leftApplyModeBtn.transform.Find("Icon") != null
                            ? leftApplyModeBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        if (leftApplyModeBtnText != null)
                        {
                            leftApplyModeBtnText.text = ItemApplyMode == ApplyMode.SingleClick
                                ? VPBTranslation.T("gallery.apply.one_click", "1-Click")
                                : VPBTranslation.T("gallery.apply.two_click", "2-Click");
                            leftApplyModeBtnText.fontSize = btnFontSize;
                            leftApplyModeBtnText.gameObject.SetActive(true);
                        }
                        leftApplyModeBtnIconImage = null;
                    }
                    leftSideButtons.Add(leftApplyModeBtn.GetComponent<RectTransform>());
                    AddTooltip(leftApplyModeBtn, "gallery.tooltip.apply_mode", "Toggle 1-click vs 2-click apply.");
                }

                // Remove Item Mode (Left) — hover-to-remove tool (mirror of the right rail button).
                {
                    Color colorRemoveModeRailL = RemoveModeRailBackdrop;
                    float rmW = sideIconBtn;
                    float rmH = sideIconBtn;
                    Sprite rmSprL = null;
                    try { rmSprL = UI.LoadIconSprite("vpb_icons/delete.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject leftRemoveModeBtn = UI.CreateUIButton(leftSideContainer, rmW, rmH, " ", 8, 0, startY - spacing * 10 - groupGap * 4, AnchorPresets.centre, () => ToggleRemoveMode(true, false));
                    leftRemoveModeSideBtn = leftRemoveModeBtn;
                    Image rmImgL = leftRemoveModeBtn.GetComponent<Image>();
                    Text rmTxtL = leftRemoveModeBtn.GetComponentInChildren<Text>(true);
                    if (rmSprL != null)
                    {
                        UI.AddIconToButton(leftRemoveModeBtn, rmSprL, sideIconPad, colorRemoveModeRailL);
                        leftRemoveModeBtnIconImage = leftRemoveModeBtn.transform.Find("Icon") != null
                            ? leftRemoveModeBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else if (rmImgL != null)
                    {
                        rmImgL.color = colorRemoveModeRailL;
                        if (rmTxtL != null)
                        {
                            rmTxtL.text = VPBTranslation.T("gallery.side.remove_mode_short", "Erase");
                            rmTxtL.fontSize = btnFontSize;
                            rmTxtL.gameObject.SetActive(true);
                        }
                    }
                    leftRemoveModeBtnOutline = RemoveModeAddRailOutline(leftRemoveModeBtn);
                    leftSideButtons.Add(leftRemoveModeBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(leftRemoveModeBtn, () => ToggleRemoveMode(true, true));
                    AddTooltip(leftRemoveModeBtn, "gallery.tooltip.remove_mode", "Remove Item Mode: point at an item to fade it, click to remove. Also opens the remove list siderail for clothing/hair/scene.");
                }

                // Creator Mode (Left) — sticky scene-tools mode. Not Creators author list.
                {
                    float cmW = sideIconBtn;
                    float cmH = sideIconBtn;
                    Sprite cmSprL = null;
                    try { cmSprL = UI.LoadIconSprite("vpb_icons/creator_mode.png", UI.SideRailIconGlyphTint); } catch { }
                    GameObject leftCreatorModeBtn = UI.CreateUIButton(leftSideContainer, cmW, cmH, " ", 8, 0, 0, AnchorPresets.centre, () => ToggleCreatorMode(true, false));
                    leftCreatorModeSideBtn = leftCreatorModeBtn;
                    Image cmImgL = leftCreatorModeBtn.GetComponent<Image>();
                    Text cmTxtL = leftCreatorModeBtn.GetComponentInChildren<Text>(true);
                    if (cmSprL != null)
                    {
                        UI.AddIconToButton(leftCreatorModeBtn, cmSprL, sideIconPad, CreatorModeRailBackdrop);
                        leftCreatorModeBtnIconImage = leftCreatorModeBtn.transform.Find("Icon") != null
                            ? leftCreatorModeBtn.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else if (cmImgL != null)
                    {
                        cmImgL.color = CreatorModeRailBackdrop;
                        if (cmTxtL != null)
                        {
                            cmTxtL.text = VPBTranslation.T("gallery.side.creator_mode_short", "Make");
                            cmTxtL.fontSize = btnFontSize;
                            cmTxtL.gameObject.SetActive(true);
                        }
                    }
                    leftCreatorModeBtnOutline = CreatorModeAddRailOutline(leftCreatorModeBtn);
                    leftSideButtons.Add(leftCreatorModeBtn.GetComponent<RectTransform>());
                    AddRightClickDelegate(leftCreatorModeBtn, () => ToggleCreatorMode(true, true));
                    AddTooltip(leftCreatorModeBtn, "gallery.tooltip.creator_mode",
                        "Creator Mode — sticky scene authoring tools. Not the Creators author list. Ctrl+Shift+K. Esc exits.");
                }

                // Appearance clothing-apply-mode is now a 3-button segmented row docked in the
                // toolbox (see EnsureTboxUI / tboxClothingModeRow), not a side tab.

                // Add/Replace toggle lives in the toolbox (next to Keep Scale).

                {
                    float saveW = gallerySaveSprite != null ? sideIconBtn : btnWidth;
                    float saveH = gallerySaveSprite != null ? sideIconBtn : btnHeight;
                    leftSaveBtnGO = UI.CreateUIButton(leftSideContainer, saveW, saveH, " ", 8, 0, startY - spacing * 14 - groupGap * 4, AnchorPresets.centre, () => {
                        try
                        {
                            ToggleSaveSubmenuFromSideButtons(PreferLeftSidePanelFromRail(true, false));
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("[VPB] Save (Left) exception: " + ex);
                        }
                    });
                    Color saveCol = new Color(0.2f, 0.4f, 0.2f, 1f);
                    if (gallerySaveSprite != null)
                    {
                        UI.AddIconToButton(leftSaveBtnGO, gallerySaveSprite, sideIconPad, saveCol);
                        leftSaveBtnIconImage = leftSaveBtnGO.transform.Find("Icon") != null
                            ? leftSaveBtnGO.transform.Find("Icon").GetComponent<Image>() : null;
                    }
                    else
                    {
                        leftSaveBtnGO.GetComponent<Image>().color = saveCol;
                        var st = leftSaveBtnGO.GetComponentInChildren<Text>(true);
                        if (st != null)
                        leftSaveBtnIconImage = null;
                    }
                    leftSideButtons.Add(leftSaveBtnGO.GetComponent<RectTransform>());
                    leftSaveBtnGO.SetActive(false);
                    AddRightClickDelegate(leftSaveBtnGO, () => {
                        try { ToggleSaveSubmenuFromSideButtons(PreferLeftSidePanelFromRail(true, true)); }
                        catch (Exception ex) { LogUtil.LogError("[VPB] Save (Left RMB) exception: " + ex); }
                    });
                    AddTooltip(leftSaveBtnGO, "gallery.tooltip.save_pane", "Save presets and related actions.");
                }


                // Scene Context (Left)
                {
                    float rmW = removeCtxW;
                    float rmH = removeCtxH;
                    Color rmCol = UI.AccentRed;
                    leftRemoveAtomBtn = UI.CreateUIButton(leftSideContainer, rmW, rmH, " ", 8, 0, 0, AnchorPresets.centre, () => {
                        try
                        {
                            if (SuperController.singleton == null) return;
                            ToggleAtomSubmenuFromSideButtons(PreferLeftSidePanelFromRail(true, false));
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("[VPB] Remove (scene) (Left) exception: " + ex);
                        }
                    });
                    if (galleryRemoveSprite != null)
                    {
                        UI.AddIconToButton(leftRemoveAtomBtn, galleryRemoveSprite, sideIconPad, rmCol);
                        leftRemoveAtomBtnIconImage = leftRemoveAtomBtn.transform.Find("Icon") != null
                            ? leftRemoveAtomBtn.transform.Find("Icon").GetComponent<Image>() : null;
                        var tx = leftRemoveAtomBtn.GetComponentInChildren<Text>(true);
                        if (tx != null) tx.gameObject.SetActive(false);
                    }
                    else
                    {
                        leftRemoveAtomBtn.GetComponent<Image>().color = rmCol;
                        var tx = leftRemoveAtomBtn.GetComponentInChildren<Text>(true);
                        if (tx != null)
                        {
                            tx.text = VPBTranslation.T("gallery.side.remove", "Remove");
                            tx.fontSize = GalleryUiDesignTokens.FontBodyRef;
                            tx.gameObject.SetActive(true);
                        }
                        leftRemoveAtomBtnIconImage = null;
                    }
                    leftSideButtons.Add(leftRemoveAtomBtn.GetComponent<RectTransform>());
                    leftRemoveAtomBtn.SetActive(false);
                    AddRightClickDelegate(leftRemoveAtomBtn, () => {
                        try
                        {
                            if (SuperController.singleton == null) return;
                            ToggleAtomSubmenuFromSideButtons(PreferLeftSidePanelFromRail(true, true));
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogError("[VPB] Remove (scene) (Left RMB) exception: " + ex);
                        }
                    });
                    AddTooltip(leftRemoveAtomBtn, "gallery.tooltip.remove_context", "Remove or open remove options for the current category (clothing, hair, or scene).");
                }


                // Context Actions (Left)
                leftRemoveAllClothingBtn = UI.CreateUIButton(leftSideContainer, removeCtxW, removeCtxH, VPBTranslation.T("gallery.side.remove_clothing", "Remove\nClothing"), 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Clothing (Left)");
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleClothingSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(true, false));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing (Left) exception: " + ex);
                    }
                });
                leftRemoveAllClothingBtn.GetComponent<Image>().color = UI.AccentRed;
                {
                    var tx0 = leftRemoveAllClothingBtn.GetComponentInChildren<Text>(true);
                    if (galleryRemoveSprite != null)
                    {
                        UI.AddIconToButton(leftRemoveAllClothingBtn, galleryRemoveSprite, sideIconPad, UI.AccentRed);
                        leftRemoveAllClothingBtnIconImage = leftRemoveAllClothingBtn.transform.Find("Icon") != null
                            ? leftRemoveAllClothingBtn.transform.Find("Icon").GetComponent<Image>() : null;
                        if (tx0 != null) tx0.gameObject.SetActive(false);
                    }
                    else
                    {
                        leftRemoveAllClothingBtnIconImage = null;
                        if (tx0 != null)
                        {
                            tx0.color = Color.white;
                            tx0.gameObject.SetActive(true);
                        }
                    }
                }
                leftSideButtons.Add(leftRemoveAllClothingBtn.GetComponent<RectTransform>());
                leftRemoveAllClothingBtn.SetActive(false);
                AddRightClickDelegate(leftRemoveAllClothingBtn, () => {
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleClothingSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(true, true));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Clothing (Left RMB) exception: " + ex);
                    }
                });
                AddTooltip(leftRemoveAllClothingBtn, "gallery.tooltip.remove_context", "Remove or open remove options for the current category (clothing, hair, or scene).");

                leftRemoveAllHairBtn = UI.CreateUIButton(leftSideContainer, removeCtxW, removeCtxH, VPBTranslation.T("gallery.side.remove_hair", "Remove\nHair"), 18, 0, 0, AnchorPresets.centre, () => {
                    LogUtil.Log("[VPB] SideButton click: Remove Hair (Left)");
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleHairSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(true, false));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair (Left) exception: " + ex);
                    }
                });
                leftRemoveAllHairBtn.GetComponent<Image>().color = UI.AccentRed;
                {
                    var tx0 = leftRemoveAllHairBtn.GetComponentInChildren<Text>(true);
                    if (galleryRemoveSprite != null)
                    {
                        UI.AddIconToButton(leftRemoveAllHairBtn, galleryRemoveSprite, sideIconPad, UI.AccentRed);
                        leftRemoveAllHairBtnIconImage = leftRemoveAllHairBtn.transform.Find("Icon") != null
                            ? leftRemoveAllHairBtn.transform.Find("Icon").GetComponent<Image>() : null;
                        if (tx0 != null) tx0.gameObject.SetActive(false);
                    }
                    else
                    {
                        leftRemoveAllHairBtnIconImage = null;
                        if (tx0 != null) { tx0.color = Color.white; tx0.gameObject.SetActive(true); }
                    }
                }
                leftSideButtons.Add(leftRemoveAllHairBtn.GetComponent<RectTransform>());
                leftRemoveAllHairBtn.SetActive(false);
                AddRightClickDelegate(leftRemoveAllHairBtn, () => {
                    try
                    {
                        Atom target = GetBestTargetAtom();
                        if (target == null) return;
                        ToggleHairSubmenuFromSideButtons(target, PreferLeftSidePanelFromRail(true, true));
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] Remove Hair (Left RMB) exception: " + ex);
                    }
                });
                AddTooltip(leftRemoveAllHairBtn, "gallery.tooltip.remove_context", "Remove or open remove options for the current category (clothing, hair, or scene).");



                try { UpdateUndoRedoButtonLabels(); } catch { }

                UpdateDesktopModeButton();
                UpdateFollowButtonState();
                try { UpdateTargetDropdownUI(); } catch { }
                try { UpdateReplaceButtonState(); } catch { }
                try { UpdateApplyModeButtonState(); } catch { }
            }

            // Main Content Area
            GameObject scrollGO = UI.CreateVScrollableContent(backgroundBoxGO, new Color(0, 0, 0, 0), AnchorPresets.stretchAll, 0, 0, Vector2.zero);
            // Tab scroll panels must render above the image grid so their VR scroll buttons aren't covered.
            if (leftTabScrollGO != null) leftTabScrollGO.transform.SetAsLastSibling();
            if (leftSubTabScrollGO != null) leftSubTabScrollGO.transform.SetAsLastSibling();
            if (rightTabScrollGO != null) rightTabScrollGO.transform.SetAsLastSibling();
            if (rightSubTabScrollGO != null) rightSubTabScrollGO.transform.SetAsLastSibling();
            scrollRect = scrollGO.GetComponent<ScrollRect>();
            try { GalleryViewportCtrlScrollColumns.TryAttach(this, scrollRect); } catch { }
            contentScrollRT = scrollGO.GetComponent<RectTransform>();
            contentScrollRT.offsetMin = new Vector2(0, 110);
            contentScrollRT.offsetMax = new Vector2(-230, -65); // Default top margin (Quick Filters hidden)
            lastScrollTime = Time.unscaledTime;
            if (scrollRect != null)
            {
                scrollRect.onValueChanged.AddListener((v) => { 
                    lastScrollTime = Time.unscaledTime;
                    // Do not auto-close toolbox rating selector on scroll changes.
                    // Scroll value can change due to layout rebuilds / content refresh (not user intent),
                    // which makes the selector unusable in some modes (e.g. Custom Scenes).
                    // LogUtil.Log("Scroll changed: " + v.y);
                });
            }

            // Spring drag scroll button (WorldSpace and ScreenSpaceOverlay).
            try
            {
                if (scrollRect != null)
                {
                    Transform sb = scrollGO.transform.Find("Scrollbar");
                    if (sb != null)
                    {
                        // Parent to the scrollbar so it follows layout/offsets.
                        float w = isFixedLocally
                            ? GalleryUiDesignTokens.SpringScrollBtnWidthFixedRef
                            : GalleryUiDesignTokens.SpringScrollBtnWidthFloatRef;
                        float h = w * GalleryUiDesignTokens.SpringScrollBtnAspectRef;
                        GameObject springBtn = SpringScrollButton.Create(sb.gameObject, scrollRect, w, h);

                        // Ensure it doesn't block other interactions outside its square.
                        springBtn.transform.SetAsLastSibling();

                        // Lower sensitivity for big-button spring scrolling in VR.
                        SpringScrollButton ssb = springBtn.GetComponent<SpringScrollButton>();
                        if (ssb != null)
                        {
                            // Retuned for practical hand movement: reach high speed without huge drags.
                            ssb.deadzoneFraction = 0.10f;
                            ssb.maxViewportHeightsPerSecond = 2.25f;
                            ssb.speedSmoothing = 12f;
                            ssb.responsePower = 2.0f;
                        }

                        // Icon: scroll.png
                        try
                        {
                            Sprite icon = UI.LoadIconSprite("vpb_icons/scroll.png", UI.SideRailIconGlyphTint);
                            if (icon != null)
                            {
                                GameObject iconGO = new GameObject("Icon");
                                iconGO.transform.SetParent(springBtn.transform, false);
                                Image img = UI.AddImage(iconGO, UI.SideRailIconGlyphTint);
                                img.sprite = icon;
                                img.preserveAspect = true;
                                img.raycastTarget = false;

                                RectTransform irt = iconGO.GetComponent<RectTransform>();
                                irt.anchorMin = Vector2.zero;
                                irt.anchorMax = Vector2.one;
                                irt.sizeDelta = new Vector2(-24f, -24f);
                                irt.anchoredPosition = Vector2.zero;
                            }
                        }
                        catch { }

                        // Tooltip: teach the gesture (localized)
                        try
                        {
                            AddTooltip(springBtn, "gallery.tooltip.spring_scroll_drag", "Hold and drag up/down to scroll (farther = faster). Release to stop.");
                        }
                        catch { }

                        // Track + apply default ON/OFF state (footer toggle updates this too).
                        springScrollButtonGO = springBtn;
                        ApplySpringScrollButtonScale(ChromeScale);
                        springScrollButtonGO.SetActive(springScrollButtonEnabled);

                        try
                        {
                            EnsureScrollbarJumpButtonsExist();
                            LayoutScrollbarJumpButtons();
                        }
                        catch { }
                    }
                }
            }
            catch { }
            
            contentGO = scrollRect.content.gameObject;
            CreateLoadingOverlay(scrollRect != null && scrollRect.viewport != null ? scrollRect.viewport.gameObject : scrollGO);
            CreateEmptyGridStateOverlay(scrollRect != null && scrollRect.viewport != null ? scrollRect.viewport.gameObject : scrollGO);
            CreateThumbnailCacheProgressPanel(scrollRect != null && scrollRect.viewport != null ? scrollRect.viewport.gameObject : scrollGO);

            // Clean up legacy layout components that interfere with virtualization
            var legacyGLG = contentGO.GetComponent<GridLayoutGroup>();
            if (legacyGLG != null) DestroyImmediate(legacyGLG);
            var legacyCSF = contentGO.GetComponent<ContentSizeFitter>();
            if (legacyCSF != null) DestroyImmediate(legacyCSF);
            var legacyVLG = contentGO.GetComponent<VerticalLayoutGroup>();
            if (legacyVLG != null) DestroyImmediate(legacyVLG);

            // Initialize RecyclingGridView immediately instead of legacy layout components
            recyclingGrid = contentGO.AddComponent<RecyclingGridView>();
            recyclingGrid.scrollRect = scrollRect;
            recyclingGrid.content = contentGO.GetComponent<RectTransform>();
            
            // Set initial adaptive config
            float minSize = 200f;
            recyclingGrid.SetGridConfig(100f, GetGridCellConfigHeight(), EffectiveGridSpacingX(), EffectiveGridSpacingY(), gridColumnCount);
            recyclingGrid.SetAdaptiveConfig(true, minSize, gridColumnCount, false);

            // Pagination Controls (Bottom Left)
            CreatePaginationControls();
            try { CreateFirstRunHintStrip(); } catch { }
            try { CreateTitleSearchChipHost(); } catch { }
            try { CreateActiveFilterChipBar(); } catch { }
            try { RefreshFooterPerfChrome(); } catch { }

            // Status Bar (Now shares the hoverPathRT container)
            GameObject statusBarGO = new GameObject("StatusBar");
            statusBarGO.transform.SetParent(hoverPathRT.transform, false);
            statusBarText = statusBarGO.AddComponent<Text>();
            statusBarText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            statusBarText.fontSize = GalleryUiDesignTokens.FontRef;
            statusBarText.fontStyle = FontStyle.Normal;
            statusBarText.color = Color.white;
            var statusShadow = statusBarGO.AddComponent<Shadow>();
            statusShadow.effectColor = new Color(0, 0, 0, 0.8f);
            statusShadow.effectDistance = new Vector2(1, -1);
            statusBarText.alignment = TextAnchor.MiddleCenter;
            statusBarText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusBarText.verticalOverflow = VerticalWrapMode.Truncate;
            statusBarText.raycastTarget = false;
            
            RectTransform statusRT = statusBarGO.GetComponent<RectTransform>();
            // Bottom-row anchor: same as hoverPathText and tboxLabelLayer
            statusRT.anchorMin        = new Vector2(0f, 0f);
            statusRT.anchorMax        = new Vector2(1f, 0f);
            statusRT.pivot            = new Vector2(0.5f, 0f);
            statusRT.anchoredPosition = Vector2.zero;
            statusRT.sizeDelta        = new Vector2(0f, GalleryUiDesignTokens.FooterInfoRowHeightRef);
            {
                var sRT = statusRT;
                innerPaneScaleActions.Add(s => { if (sRT != null) sRT.sizeDelta = new Vector2(0f, GalleryUiDesignTokens.FooterInfoRowHeightRef * s); });
            }

            // Pointer Dot
            pointerDotGO = new GameObject("PointerDot");
            pointerDotGO.transform.SetParent(canvasGO.transform, false);
            Image dotImg = UI.AddImage(pointerDotGO, new Color(1, 1, 1, 0.5f));
            // We use a dot texture if available, otherwise just a small circle/square
            pointerDotGO.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 8);
            pointerDotGO.SetActive(false);

            CreateResizeHandles();

            // Minimize button (title bar icon row)
            GameObject minimizeBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, "_", 30, 0, 0, AnchorPresets.middleCenter, () => {
                Hide();
            });
            RectTransform minRT = minimizeBtn.GetComponent<RectTransform>();
            minRT.anchorMin = new Vector2(0.5f, 0.5f);
            minRT.anchorMax = new Vector2(0.5f, 0.5f);
            minRT.pivot     = new Vector2(0.5f, 0.5f);
            minRT.anchoredPosition = Vector2.zero;
            _titleBarMinimizeBtnRT = minRT;
            minimizeBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
            AddHoverDelegate(minimizeBtn);
            { var s = UI.LoadIconSprite("vpb_icons/minimize.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(minimizeBtn, s); }

            // Close button (title bar icon row) - rendered last to be on top
            GameObject closeBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, "X", 30, 0, 0, AnchorPresets.middleCenter, () => {
                Close();
            });
            RectTransform closeRT = closeBtn.GetComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(0.5f, 0.5f);
            closeRT.anchorMax = new Vector2(0.5f, 0.5f);
            closeRT.pivot     = new Vector2(0.5f, 0.5f);
            closeRT.anchoredPosition = Vector2.zero;
            _titleBarCloseBtnRT = closeRT;
            closeBtn.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
            AddHoverDelegate(closeBtn);
            { var s = UI.LoadIconSprite("vpb_icons/close.png", UI.BarIconGlyphTint); if (s != null) UI.AddIconToButton(closeBtn, s); }

            // Register inner pane button scale actions (close/minimize — X anchored by ApplyTitleBarResponsiveLayout)
            { var rt = minRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }
            { var rt = closeRT; innerPaneScaleActions.Add(s => { rt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef * s, GalleryUiDesignTokens.TitleBarChipRef * s); }); }

            ApplyInnerPaneScale();
            ApplySidePanelDefaultsFromConfig();
            // Import last-session open only in-session; cold start uses settings defaults / Import via Apply above.
            bool restoreGlobalImportOpen = !importSidebarInitAsClone
                && Gallery.singleton != null
                && Gallery.singleton.PanelCount == 1
                && Gallery.SessionBrowseMemoryActive;
            TryRestoreImportSidebarOpenFromGlobalPref(restoreGlobalImportOpen);
            importSidebarInitAsClone = false;
            UpdateSideButtonsVisibility();
            UpdateLayout();
            SubscribeLocaleChanged();
            RefreshLocalizedUi();

            // Kill ColorTint on all Selectables + border on buttons; enforcer re-runs on a throttle
            // so UI rebuilt after init cannot restore default hover fill. Enforcer must not rewrite
            // existing UIHoverBorder.hoverColor (side-rail selected tints) or rims pulse / cost FPS.
            UI.ApplyGalleryPaneHoverPolicy(backgroundBoxGO);
            if (backgroundBoxGO.GetComponent<GalleryPaneChromeEnforcer>() == null)
                backgroundBoxGO.AddComponent<GalleryPaneChromeEnforcer>();

            try { ApplyGalleryTransparencyVisuals(); } catch { }

            // Default lastAppliedPackageRefreshTime was DateTime.MinValue, so the first Show() always saw
            // pkgRefreshTime > lastApplied, set packagesChanged, cleared creator/category caches, and
            // UpdateLayout rebuilt them synchronously on the main thread (~seconds). Align to the current
            // FileManager baseline so only real refreshes invalidate caches (RefreshFilesRoutine already
            // rebuilds counts on a worker thread when needed).
            try { lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime; } catch { }

            try { CreateFirstRunHintStrip(); } catch { }
            try { CreateTitleSearchChipHost(); } catch { }
            try { CreateActiveFilterChipBar(); } catch { }
            try { RefreshFirstRunHintStrip(); } catch { }
            try { StartCoroutine(ShowModeSetupWizardDeferred()); } catch { }
        }

        private void AddSubmenuSideHoverTrigger(GameObject go, bool isLeft)
        {
            if (go == null) return;
            try
            {
                EventTrigger et = go.GetComponent<EventTrigger>();
                if (et == null) et = go.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener((data) => {
                    try
                    {
                        CloseOtherSideIfSubmenu(isLeft);
                    }
                    catch { }
                });
                et.triggers.Add(entry);
            }
            catch { }
        }
    }
}
