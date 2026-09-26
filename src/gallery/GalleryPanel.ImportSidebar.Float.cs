using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private bool importSidebarDetached;
        private bool importSidebarFloatCollapsed;
        private float? importSidebarExpandHeightRef;
        private Vector2? importSidebarSavedFloatPosCenter;
        private Vector2? importSidebarSavedFloatSizeRef;
        private Vector2? importSidebarCollapsedTopLeftPos;

        private GameObject importSidebarFloatTitleBarGO;
        private GameObject importSidebarFloatFooterGO;
        private GameObject importSidebarFloatDetachBtnGO;
        private GameObject importSidebarFloatCollapseBtnGO;
        private GameObject importSidebarFloatCloseBtnGO;
        private GameObject importSidebarFloatDockBtnGO;
        private GameObject importSidebarFloatResizeHandleGO;
        private Image importSidebarFloatCollapseBtnIcon;
        private Text importSidebarFloatTitleLabel;
        private Text importSidebarFloatDetachBtnText;
        private Image importSidebarRootBg;

        private bool ImportSidebarOccupiesSideColumn
        {
            get { return importSidebarActive && !importSidebarDetached; }
        }

        public bool IsImportSidebarDetached { get { return importSidebarDetached; } }

        public void ToggleFloatingImportSidebar()
        {
            if (importSidebarActive && importSidebarDetached)
            {
                HideImportSidebarFloatKeepDetach();
                return;
            }

            if (!ImportSidebarCategoryAllowed())
            {
                if (!TryNavigateGalleryToScenes())
                {
                    try
                    {
                        SetStatus(VPBTranslation.T(
                            "gallery.import.sidebar_gated_tip",
                            "Import opens in Scenes or Appearances"));
                    }
                    catch { }
                    return;
                }
            }

            EnsureImportSidebarDetachedAndVisible();
        }

        internal bool TryHandleImportSidebarFloatEsc()
        {
            if (!importSidebarActive || !importSidebarDetached) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            HideImportSidebarFloatKeepDetach();
            return true;
        }

        private void EnsureImportSidebarDetachedAndVisible()
        {
            importSidebarOpenIntent = true;
            importSidebarOpenIntentLoaded = true;
            if (!importSidebarDetached)
                DetachImportSidebar();
            else
                ApplyImportSidebarDetachChrome(reposition: false, persist: false);
            RefreshImportSidebarCategoryGate();
            PersistImportSidebarOpenIntent();
            if (importSidebarActive && importSidebarRoot != null && !importSidebarRoot.activeSelf)
                importSidebarRoot.SetActive(true);
        }

        private void HideImportSidebarFloatKeepDetach()
        {
            if (importSidebarDetached)
            {
                if (!importSidebarFloatCollapsed)
                    CaptureImportSidebarFloatGeometryToMemory();
                else
                    RestoreImportSidebarExpandHeightIntoSavedSize();
            }
            importSidebarOpenIntent = false;
            importSidebarOpenIntentLoaded = true;
            RefreshImportSidebarCategoryGate();
            PersistImportSidebarOpenIntent();
        }

        private void DetachImportSidebar()
        {
            if (importSidebarDetached) return;
            importSidebarFloatCollapsed = false;
            importSidebarExpandHeightRef = null;
            if (!importSidebarSavedFloatSizeRef.HasValue)
            {
                importSidebarSavedFloatSizeRef = new Vector2(
                    GalleryUiDesignTokens.ImportSidebarFloatDefaultWidthRef,
                    GalleryUiDesignTokens.ImportSidebarFloatDefaultHeightRef);
            }
            ApplyImportSidebarDetachChrome(reposition: true, persist: true);
            ApplyImportSidebarBaseRect(ChromeScale);
            // Clamp is visual-only; capture post-layout center so first detach persists.
            CaptureImportSidebarFloatGeometryToMemory();
            PersistImportSidebarFloatGeometry();
            RebuildImportSidebarContent();
            try { UpdateLayout(); } catch { }
            UpdateImportToggleBtnVisual();
            try { InvalidateTaskChrome(); } catch { }
            try { RefreshModeAmbientChrome(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
        }

        private void DockImportSidebar()
        {
            if (!ImportSidebarCategoryAllowed())
            {
                if (!TryNavigateGalleryToScenes())
                {
                    try
                    {
                        ShowTemporaryStatus(VPBTranslation.T(
                            "gallery.import.dock_needs_scenes",
                            "Switch to Scenes or Appearances to dock Import"), 2.5f);
                    }
                    catch { }
                    return;
                }
            }

            if (!importSidebarDetached)
            {
                importSidebarOpenIntent = true;
                importSidebarOpenIntentLoaded = true;
                RefreshImportSidebarCategoryGate();
                PersistImportSidebarOpenIntent();
                return;
            }

            // Docked Import is sticky — gate before mutating float chrome / geometry.
            if (importSidebarActive)
            {
                if (!GateStickyEnterWhileTryOn(StickyToolMode.Import))
                    return;
                try { ExitOtherStickyToolModes(StickyToolMode.Import); } catch { }
            }

            if (!importSidebarFloatCollapsed)
                CaptureImportSidebarFloatGeometryToMemory();
            else
                RestoreImportSidebarExpandHeightIntoSavedSize();
            // Flush geometry before dock — PersistDetachedState(false) skips size/pos write.
            try { PersistImportSidebarFloatGeometryFieldsOnly(); } catch { }
            importSidebarFloatCollapsed = false;
            importSidebarExpandHeightRef = null;
            importSidebarCollapsedTopLeftPos = null;

            ApplyImportSidebarDockChrome(persist: true);
            importSidebarOpenIntent = true;
            importSidebarOpenIntentLoaded = true;
            RefreshImportSidebarCategoryGate();
            PersistImportSidebarOpenIntent();
            ApplyImportSidebarBaseRect(ChromeScale);
            RebuildImportSidebarContent();
            try { UpdateLayout(); } catch { }
            UpdateImportToggleBtnVisual();
            try { InvalidateTaskChrome(); } catch { }
            try { RefreshModeAmbientChrome(); } catch { }
            try { RefreshTboxConditionalActionButtons(); } catch { }
        }

        private void ToggleImportSidebarFloatCollapsed()
        {
            if (!importSidebarDetached) return;
            if (!importSidebarFloatCollapsed)
            {
                CaptureImportSidebarFloatGeometryToMemory();
                importSidebarExpandHeightRef = ResolveImportSidebarFloatHeightRef();
                if (importSidebarRT != null)
                    importSidebarCollapsedTopLeftPos = importSidebarRT.anchoredPosition;
                importSidebarFloatCollapsed = true;
            }
            else
            {
                RestoreImportSidebarExpandHeightIntoSavedSize();
                importSidebarFloatCollapsed = false;
                importSidebarCollapsedTopLeftPos = null;
            }
            SyncImportSidebarFloatCollapseButtonVisual();
            ApplyImportSidebarBaseRect(ChromeScale);
            RebuildImportSidebarContent();
        }

        private void BuildImportSidebarFloatChrome()
        {
            if (importSidebarRoot == null) return;

            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef;

            importSidebarFloatTitleBarGO = UI.CreateChildRT(importSidebarRoot, "FloatTitleBar", AnchorPresets.hStretchTop,
                new Vector2(0f, titleH), Vector2.zero);
            Image titleBg = UI.AddImage(importSidebarFloatTitleBarGO, ImportSidebarFloatTitleBarBg);
            if (titleBg != null) titleBg.raycastTarget = true;
            RectTransform titleRT = importSidebarFloatTitleBarGO.GetComponent<RectTransform>();
            if (titleRT != null)
            {
                titleRT.pivot = new Vector2(0.5f, 1f);
                titleRT.anchoredPosition = Vector2.zero;
                titleRT.sizeDelta = new Vector2(0f, titleH);
            }
            UI.AddHLG(
                importSidebarFloatTitleBarGO, spacing: 0f, padding: UI.Pad(0, 0, 0, 0),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            Text grip = UI.CreateLabel(importSidebarFloatTitleBarGO, "\u2807", GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            UI.ApplyFloatTitleBarMetrics(
                importSidebarFloatTitleBarGO.GetComponent<HorizontalLayoutGroup>(),
                grip.gameObject, 1f);

            UI.CreateFloatTitleWindowIcon(
                importSidebarFloatTitleBarGO, "package-import",
                GalleryUiDesignTokens.FloatTitleWindowIconSizeRef);

            importSidebarFloatTitleLabel = UI.CreateEmphasisTitleLabel(
                importSidebarFloatTitleBarGO,
                VPBTranslation.T("gallery.import.float_title", "Import"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiColorTokens.TextPrimary, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(importSidebarFloatTitleLabel.gameObject, flexibleWidth: 1f, minWidth: 60f);
            if (importSidebarFloatTitleBarGO.GetComponent<RectMask2D>() == null)
                importSidebarFloatTitleBarGO.AddComponent<RectMask2D>();

            importSidebarFloatCollapseBtnGO = UI.CreateUIButton(
                importSidebarFloatTitleBarGO, chromeSz, chromeSz, " ", 16, 0, 0, AnchorPresets.middleCenter,
                ToggleImportSidebarFloatCollapsed);
            importSidebarFloatCollapseBtnGO.name = "CollapseBtn";
            StyleImportSidebarFloatChromeIconBtn(importSidebarFloatCollapseBtnGO, chromeSz, "chevron-up");
            Transform collapseIconTr = importSidebarFloatCollapseBtnGO.transform.Find("Icon");
            importSidebarFloatCollapseBtnIcon = collapseIconTr != null ? collapseIconTr.GetComponent<Image>() : null;
            var collapseHover = importSidebarFloatCollapseBtnGO.AddComponent<UIHoverDelegate>();
            collapseHover.OnHoverChange += (enter) =>
            {
                if (enter)
                {
                    SetStatus(importSidebarFloatCollapsed
                        ? VPBTranslation.T("gallery.import.tip.expand", "Expand window")
                        : VPBTranslation.T("gallery.import.tip.collapse", "Collapse to title bar"));
                }
                else SetStatus(null);
            };

            importSidebarFloatCloseBtnGO = UI.CreateUIButton(
                importSidebarFloatTitleBarGO, chromeSz, chromeSz, " ", 16, 0, 0, AnchorPresets.middleCenter,
                HideImportSidebarFloatKeepDetach);
            importSidebarFloatCloseBtnGO.name = "TitleClose";
            StyleImportSidebarFloatChromeIconBtn(importSidebarFloatCloseBtnGO, chromeSz, "x");
            var closeHover = importSidebarFloatCloseBtnGO.AddComponent<UIHoverDelegate>();
            closeHover.OnHoverChange += (enter) =>
            {
                if (enter) SetStatus(VPBTranslation.T(
                    "gallery.import.tip.close_hide",
                    "Hide window (keeps floating position). Use Dock in footer to reattach."));
                else SetStatus(null);
            };

            var headerDrag = importSidebarFloatTitleBarGO.AddComponent<UIFloatPanelDrag>();
            headerDrag.Target = importSidebarRT;
            headerDrag.OnMoved = OnImportSidebarFloatMoved;

            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef;
            importSidebarFloatFooterGO = UI.CreateChildRT(importSidebarRoot, "FloatFooter", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            Image footerBg = UI.AddImage(importSidebarFloatFooterGO, ImportSidebarFloatFooterBarBg);
            RectTransform footerRT = importSidebarFloatFooterGO.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                importSidebarFloatFooterGO, spacing: UI.GapTight(), padding: UI.PadFloatFooter(),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            GameObject footerDragArea = UI.CreateFloatFooterDragArea(importSidebarFloatFooterGO);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<UIFloatPanelDrag>();
                footerDrag.Target = importSidebarRT;
                footerDrag.OnMoved = OnImportSidebarFloatMoved;
            }

            importSidebarFloatDockBtnGO = UI.CreateUIButton(
                importSidebarFloatFooterGO, 72f, chromeSz,
                VPBTranslation.T("gallery.import.dock", "Dock"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0, AnchorPresets.middleCenter,
                DockImportSidebar);
            importSidebarFloatDockBtnGO.name = "FloatDock";
            Image dockImg = importSidebarFloatDockBtnGO.GetComponent<Image>();
            if (dockImg != null) dockImg.color = ImportSidebarSecondaryActionBg;
            Button dockBtn = importSidebarFloatDockBtnGO.GetComponent<Button>();
            if (dockBtn != null) dockBtn.transition = Selectable.Transition.None;
            LayoutElement dockLe = importSidebarFloatDockBtnGO.GetComponent<LayoutElement>();
            if (dockLe == null) dockLe = importSidebarFloatDockBtnGO.AddComponent<LayoutElement>();
            dockLe.preferredWidth = 72f;
            dockLe.preferredHeight = chromeSz;
            dockLe.minWidth = 56f;
            dockLe.flexibleWidth = 0f;
            Text dockTxt = importSidebarFloatDockBtnGO.GetComponentInChildren<Text>();
            if (dockTxt != null)
            {
                dockTxt.alignment = TextAnchor.MiddleCenter;
                dockTxt.color = Color.white;
                try { VPBUiFont.ApplyTo(dockTxt); } catch { }
            }
            var dockHover = importSidebarFloatDockBtnGO.AddComponent<UIHoverDelegate>();
            dockHover.OnHoverChange += (enter) =>
            {
                if (enter) SetStatus(VPBTranslation.T(
                    "gallery.import.tip.dock",
                    "Dock as side panel (keeps open)"));
                else SetStatus(null);
            };

            GameObject footerSpacer = new GameObject("Spacer");
            footerSpacer.transform.SetParent(importSidebarFloatFooterGO.transform, false);
            footerSpacer.AddComponent<RectTransform>();
            UI.AddLE(footerSpacer, flexibleWidth: 1f, minWidth: 8f);
            UI.EnsureFloatFooterSpacerDragHit(footerSpacer);
            var spacerDrag = footerSpacer.AddComponent<UIFloatPanelDrag>();
            spacerDrag.Target = importSidebarRT;
            spacerDrag.OnMoved = OnImportSidebarFloatMoved;
            Text footerGrip = UI.CreateLabel(footerSpacer, "\u2807", GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiColorTokens.TextDim, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            if (footerGrip != null)
            {
                RectTransform gripRT = footerGrip.rectTransform;
                if (gripRT != null)
                {
                    gripRT.anchorMin = Vector2.zero;
                    gripRT.anchorMax = Vector2.one;
                    gripRT.offsetMin = Vector2.zero;
                    gripRT.offsetMax = Vector2.zero;
                }
            }

            float rh = GalleryUiDesignTokens.ButtonSizeRef;
            importSidebarFloatResizeHandleGO = UI.AddChildGOImage(
                importSidebarFloatFooterGO, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                rh, rh, Vector2.zero, rounded: true);
            importSidebarFloatResizeHandleGO.name = "ResizeHandle";
            Image rhImg = importSidebarFloatResizeHandleGO.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            UI.EnsureFloatChromeHoverBorder(importSidebarFloatResizeHandleGO);
            StyleImportSidebarFloatChromeIconBtn(
                importSidebarFloatResizeHandleGO, rh, "chevrons-down-right", UI.IconButtonBackdrop);
            var resizer = importSidebarFloatResizeHandleGO.AddComponent<UIFloatPanelResize>();
            resizer.Target = importSidebarRT;
            resizer.GetMinSize = GetImportSidebarFloatMinSizeScaled;
            resizer.GetMaxSize = GetImportSidebarFloatMaxSizeScaled;
            resizer.OnResizing = OnImportSidebarFloatResizing;
            resizer.OnResized = OnImportSidebarFloatResized;
            var rhHover = importSidebarFloatResizeHandleGO.AddComponent<UIHoverDelegate>();
            rhHover.OnHoverChange += (enter) =>
            {
                if (enter) SetStatus(VPBTranslation.T("gallery.import.tip.resize", "Drag to resize"));
                else SetStatus(null);
            };

            importSidebarFloatTitleBarGO.SetActive(false);
            importSidebarFloatFooterGO.SetActive(false);
            SyncImportSidebarFloatCollapseButtonVisual();
        }

        private void BuildImportSidebarFloatDetachRow()
        {
            if (importSidebarScrollContentRT == null) return;

            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            GameObject btn = UI.CreateUIButton(
                importSidebarScrollContentRT.gameObject,
                0f,
                rowH,
                VPBTranslation.T("gallery.import.detach_list", "Float as Window"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0,
                AnchorPresets.middleCenter,
                DetachImportSidebar);
            if (btn == null) return;

            btn.name = "FloatDetach";
            importSidebarFloatDetachBtnGO = btn;
            // First row in scroll (before multi-select hint / wizard steps).
            btn.transform.SetAsFirstSibling();

            Image img = btn.GetComponent<Image>();
            if (img != null) img.color = ImportSidebarSecondaryActionBg;
            Text txt = btn.GetComponentInChildren<Text>();
            if (txt != null)
            {
                importSidebarFloatDetachBtnText = txt;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = UI.PopupText;
                VPBUiFont.ApplyTo(txt);
            }
            UI.AddLE(btn, preferredHeight: rowH, flexibleWidth: 1f);
            innerPaneScaleActions.Add(s =>
            {
                if (importSidebarFloatDetachBtnGO == null) return;
                LayoutElement le = importSidebarFloatDetachBtnGO.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
                if (importSidebarFloatDetachBtnText != null)
                    GalleryUiMetrics.ApplyFont(
                        importSidebarFloatDetachBtnText,
                        GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            });

            var hover = btn.AddComponent<UIHoverDelegate>();
            hover.OnHoverChange += (enter) =>
            {
                if (enter) SetStatus(VPBTranslation.T(
                    "gallery.import.tip.detach",
                    "Detach as floating window (move / resize). Toggle float{hint:import_sidebar}"));
                else SetStatus(null);
            };
            AddTooltip(btn, "gallery.import.tip.detach",
                "Detach as floating window (move / resize). Toggle float{hint:import_sidebar}");

            btn.SetActive(!importSidebarDetached);
        }

        private void SyncImportSidebarFloatDetachRowLabel()
        {
            if (importSidebarFloatDetachBtnText == null) return;
            importSidebarFloatDetachBtnText.text = VPBTranslation.T(
                "gallery.import.detach_list", "Float as Window");
        }

        private static void StyleImportSidebarFloatChromeIconBtn(
            GameObject go, float size, string iconPath, Color? backdropOverride = null)
        {
            UI.StyleFloatChromeIconButton(
                go, size, iconPath,
                backdropOverride.HasValue ? backdropOverride.Value : ImportSidebarFloatChromeIconBg);
        }

        private void LoadImportSidebarFloatGeometryFromConfig()
        {
            importSidebarDetached = false;
            importSidebarSavedFloatPosCenter = null;
            importSidebarSavedFloatSizeRef = null;
            try
            {
                if (VPBConfig.Instance == null) return;
                importSidebarDetached = VPBConfig.Instance.GalleryImportSidebarDetached;
                FloatGeometrySlot slot = VPBConfig.Instance.GalleryImportSidebarGeometry.Current;
                importSidebarSavedFloatPosCenter = slot.SavedPos;
                importSidebarSavedFloatSizeRef = slot.SavedSize(
                    new Vector2(GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef, GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef),
                    new Vector2(GalleryUiDesignTokens.ImportSidebarFloatAbsoluteMaxWidthRef, GalleryUiDesignTokens.ImportSidebarFloatAbsoluteMaxHeightRef));
            }
            catch { }
        }

        private void ApplyImportSidebarDetachChrome(bool reposition, bool persist)
        {
            importSidebarDetached = true;
            GameObject floatHost = ResolveImportSidebarFloatHost();

            if (importSidebarRoot != null && floatHost != null
                && importSidebarRoot.transform.parent != floatHost.transform)
            {
                importSidebarRoot.transform.SetParent(floatHost.transform, false);
                SetImportSidebarLayerRecursive(importSidebarRoot, floatHost.layer);
            }

            if (importSidebarRootBg != null)
                importSidebarRootBg.color = ImportSidebarFloatPanelBg;
            if (importSidebarRoot != null && importSidebarRoot.GetComponent<RectMask2D>() == null)
                importSidebarRoot.AddComponent<RectMask2D>();

            if (importSidebarFloatTitleBarGO != null) importSidebarFloatTitleBarGO.SetActive(true);
            if (importSidebarFloatFooterGO != null) importSidebarFloatFooterGO.SetActive(!importSidebarFloatCollapsed);
            if (importSidebarFloatDetachBtnGO != null) importSidebarFloatDetachBtnGO.SetActive(false);
            if (importSidebarHeaderFloatBtnGO != null) importSidebarHeaderFloatBtnGO.SetActive(false);
            if (importSidebarHeaderBtn != null) importSidebarHeaderBtn.interactable = false;

            if (importSidebarRT != null)
            {
                importSidebarRT.anchorMin = new Vector2(0.5f, 0.5f);
                importSidebarRT.anchorMax = new Vector2(0.5f, 0.5f);
                importSidebarRT.pivot = new Vector2(0f, 1f);

                if (reposition)
                {
                    ApplyImportSidebarFloatAnchorsAndPos(ChromeScale > 0f ? ChromeScale : 1f);
                }
            }

            if (importSidebarRoot != null)
                importSidebarRoot.transform.SetAsLastSibling();
            if (importSidebarFloatTitleBarGO != null)
                importSidebarFloatTitleBarGO.transform.SetAsLastSibling();
            if (importSidebarFloatFooterGO != null)
                importSidebarFloatFooterGO.transform.SetAsLastSibling();

            if (!importSidebarFloatCollapsed && importSidebarSavedFloatSizeRef.HasValue)
            {
                importSidebarSavedFloatSizeRef = new Vector2(
                    Mathf.Clamp(importSidebarSavedFloatSizeRef.Value.x,
                        GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef,
                        ResolveImportSidebarFloatMaxWidthRef()),
                    Mathf.Clamp(importSidebarSavedFloatSizeRef.Value.y,
                        GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef,
                        ResolveImportSidebarFloatMaxHeightRef()));
            }

            if (persist) PersistImportSidebarDetachedState(true);
            try { SyncImportSidebarHoverChrome(); } catch { }
        }

        private void ApplyImportSidebarDockChrome(bool persist)
        {
            importSidebarDetached = false;
            importSidebarFloatCollapsed = false;
            Transform dockParent = ResolveImportSidebarParent();
            if (importSidebarRoot != null && dockParent != null
                && importSidebarRoot.transform.parent != dockParent)
            {
                importSidebarRoot.transform.SetParent(dockParent, false);
                SetImportSidebarLayerRecursive(importSidebarRoot, dockParent.gameObject.layer);
                int siblingIndex = ResolveImportSidebarSiblingIndex(dockParent);
                importSidebarRoot.transform.SetSiblingIndex(siblingIndex);
            }

            if (importSidebarRootBg != null)
                importSidebarRootBg.color = ImportSidebarFloatPanelBg;
            RectMask2D mask = importSidebarRoot != null ? importSidebarRoot.GetComponent<RectMask2D>() : null;
            if (mask != null) UnityEngine.Object.Destroy(mask);

            if (importSidebarFloatTitleBarGO != null) importSidebarFloatTitleBarGO.SetActive(false);
            if (importSidebarFloatFooterGO != null) importSidebarFloatFooterGO.SetActive(false);
            if (importSidebarFloatDetachBtnGO != null) importSidebarFloatDetachBtnGO.SetActive(false);
            if (importSidebarHeaderFloatBtnGO != null) importSidebarHeaderFloatBtnGO.SetActive(true);
            SyncImportSidebarFloatDetachRowLabel();
            if (importSidebarHeaderRT != null) importSidebarHeaderRT.gameObject.SetActive(true);
            if (importSidebarBodyScrollRT != null) importSidebarBodyScrollRT.gameObject.SetActive(true);
            if (importSidebarApplyRT != null) importSidebarApplyRT.gameObject.SetActive(true);
            if (importSidebarHeaderBtn != null) importSidebarHeaderBtn.interactable = true;

            if (persist) PersistImportSidebarDetachedState(false);
            try { SyncImportSidebarHoverChrome(); } catch { }
        }

        private GameObject ResolveImportSidebarFloatHost()
        {
            if (canvas != null) return canvas.gameObject;
            return backgroundBoxGO;
        }

        private Vector2 ImportSidebarPaneCenterInFloatHost()
        {
            RectTransform paneRT = backgroundBoxGO != null ? backgroundBoxGO.GetComponent<RectTransform>() : null;
            RectTransform hostRT = null;
            if (importSidebarRT != null && importSidebarRT.parent != null)
                hostRT = importSidebarRT.parent as RectTransform;
            if (hostRT == null)
            {
                GameObject host = ResolveImportSidebarFloatHost();
                if (host != null) hostRT = host.GetComponent<RectTransform>();
            }
            if (paneRT == null) return Vector2.zero;
            if (hostRT == null || hostRT == paneRT) return paneRT.anchoredPosition;

            Camera cam = null;
            try
            {
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            }
            catch { }

            Vector3 paneWorld = paneRT.TransformPoint(paneRT.rect.center);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, paneWorld);
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(hostRT, screen, cam, out local))
                return local;
            return paneRT.anchoredPosition;
        }

        private void SyncImportSidebarHostChromeAfterActivate()
        {
            if (importSidebarDetached)
                ApplyImportSidebarDetachChrome(reposition: true, persist: false);
            else
                ApplyImportSidebarDockChrome(persist: false);
        }

        private void ApplyImportSidebarFloatAnchorsAndPos(float s)
        {
            if (importSidebarRT == null) return;
            importSidebarRT.anchorMin = new Vector2(0.5f, 0.5f);
            importSidebarRT.anchorMax = new Vector2(0.5f, 0.5f);
            importSidebarRT.pivot = new Vector2(0f, 1f);

            float ss = s > 0f ? s : 1f;
            Vector2 maxLocal = ResolveImportSidebarFloatMaxSizeLocal();
            float wRef = ResolveImportSidebarFloatWidthRef();
            float hRef = importSidebarFloatCollapsed
                ? GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef
                : ResolveImportSidebarFloatHeightRef();
            Vector2 size = new Vector2(
                Mathf.Min(wRef * ss, maxLocal.x > 1f ? maxLocal.x : wRef * ss),
                Mathf.Min(hRef * ss, maxLocal.y > 1f ? maxLocal.y : hRef * ss));
            importSidebarRT.sizeDelta = size;

            Vector2 center = importSidebarSavedFloatPosCenter.HasValue
                ? importSidebarSavedFloatPosCenter.Value
                : ImportSidebarPaneCenterInFloatHost();
            importSidebarRT.anchoredPosition = FloatPanelCoords.CenterToTopLeft(center, size);
            ClampImportSidebarFloatIntoHost();
        }

        private void ClampImportSidebarFloatIntoHost()
        {
        }

        private float ResolveImportSidebarFloatWidthRef()
        {
            if (importSidebarSavedFloatSizeRef.HasValue)
            {
                return Mathf.Clamp(
                    importSidebarSavedFloatSizeRef.Value.x,
                    GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef,
                    ResolveImportSidebarFloatMaxWidthRef());
            }
            return GalleryUiDesignTokens.ImportSidebarFloatDefaultWidthRef;
        }

        private float ResolveImportSidebarFloatHeightRef()
        {
            if (importSidebarSavedFloatSizeRef.HasValue)
            {
                return Mathf.Clamp(
                    importSidebarSavedFloatSizeRef.Value.y,
                    GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef,
                    ResolveImportSidebarFloatMaxHeightRef());
            }
            return GalleryUiDesignTokens.ImportSidebarFloatDefaultHeightRef;
        }

        private void RestoreImportSidebarExpandHeightIntoSavedSize()
        {
            float h = importSidebarExpandHeightRef.HasValue
                ? importSidebarExpandHeightRef.Value
                : GalleryUiDesignTokens.ImportSidebarFloatDefaultHeightRef;
            h = Mathf.Clamp(h, GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef, ResolveImportSidebarFloatMaxHeightRef());
            float w = importSidebarSavedFloatSizeRef.HasValue
                ? importSidebarSavedFloatSizeRef.Value.x
                : GalleryUiDesignTokens.ImportSidebarFloatDefaultWidthRef;
            importSidebarSavedFloatSizeRef = new Vector2(
                Mathf.Clamp(w, GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef, ResolveImportSidebarFloatMaxWidthRef()),
                h);
        }

        private void SyncImportSidebarFloatCollapseButtonVisual()
        {
            if (importSidebarFloatCollapseBtnGO == null) return;
            string path = importSidebarFloatCollapsed ? "chevron-down" : "chevron-up";
            Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
            if (spr == null) return;
            if (importSidebarFloatCollapseBtnIcon != null)
            {
                UI.SetIconSprite(importSidebarFloatCollapseBtnIcon, spr);
            }
            else
            {
                StyleImportSidebarFloatChromeIconBtn(importSidebarFloatCollapseBtnGO, GalleryUiDesignTokens.ButtonSizeRef, path);
                Transform iconTr = importSidebarFloatCollapseBtnGO.transform.Find("Icon");
                importSidebarFloatCollapseBtnIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }
        }

        private void OnImportSidebarFloatMoved()
        {
            if (importSidebarFloatCollapsed && importSidebarRT != null)
            {
                importSidebarCollapsedTopLeftPos = importSidebarRT.anchoredPosition;
                float s = ChromeScale > 0f ? ChromeScale : 1f;
                float w = ResolveImportSidebarFloatWidthRef() * s;
                float h = ResolveImportSidebarFloatHeightRef() * s;
                importSidebarSavedFloatPosCenter = FloatPanelCoords.TopLeftToCenter(
                    importSidebarCollapsedTopLeftPos.Value, new Vector2(w, h));
                PersistImportSidebarFloatGeometry();
                return;
            }
            CaptureImportSidebarFloatGeometryToMemory();
            PersistImportSidebarFloatGeometry();
        }

        private void OnImportSidebarFloatResizing()
        {
            // Capture live size only during drag; end-drag applies layout. Sync type-radio grid cell size here.
            CaptureImportSidebarFloatGeometryToMemory();
            ClampImportSidebarFloatIntoHost();
            try
            {
                float s = ChromeScale > 0f ? ChromeScale : 1f;
                ApplyImportSidebarChromeHorizontalInsets(s, out float insetLeft, out float insetRight);
                float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
                float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
                float headerH = 0f;
                float headerGap = 0f;
                float applyH = ImportSidebarBaseApplyHeight * s;
                float reasonH = ResolveImportSidebarApplyReasonHeight(s);
                LayoutImportSidebarInnerChrome(s, titleH, footerH, headerH, headerGap, applyH, reasonH, insetLeft, insetRight, showBody: true);
                AlignImportSidebarScrollViewport(s);
                SyncImportSidebarTypeRadioGridWidth(s);
                RectTransform typeRadioRT = importSidebarTypeRadioContainer as RectTransform;
                if (typeRadioRT != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(typeRadioRT);
            }
            catch { }
        }

        private void OnImportSidebarFloatResized()
        {
            CaptureImportSidebarFloatGeometryToMemory();
            ClampImportSidebarFloatIntoHost();
            PersistImportSidebarFloatGeometry();
            try { ApplyImportSidebarBaseRect(ChromeScale); } catch { }
            try { RebuildImportSidebarContent(); } catch { }
        }

        private void CaptureImportSidebarFloatGeometryToMemory()
        {
            if (importSidebarRT == null || importSidebarFloatCollapsed) return;
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            importSidebarSavedFloatPosCenter = FloatPanelCoords.TopLeftToCenter(
                importSidebarRT.anchoredPosition, importSidebarRT.sizeDelta);
            float maxW = ResolveImportSidebarFloatMaxWidthRef();
            float maxH = ResolveImportSidebarFloatMaxHeightRef();
            importSidebarSavedFloatSizeRef = new Vector2(
                Mathf.Clamp(importSidebarRT.sizeDelta.x / s, GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef, maxW),
                Mathf.Clamp(importSidebarRT.sizeDelta.y / s, GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef, maxH));
        }

        private void PersistImportSidebarDetachedState(bool isDetached)
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.GalleryImportSidebarDetached = isDetached;
                if (isDetached)
                    PersistImportSidebarFloatGeometryFieldsOnly();
            }
            catch { return; }
            ScheduleQuickFiltersConfigSave();
        }

        private void PersistImportSidebarFloatGeometry()
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                PersistImportSidebarFloatGeometryFieldsOnly();
            }
            catch { return; }
            ScheduleQuickFiltersConfigSave();
        }

        private void PersistImportSidebarFloatGeometryFieldsOnly()
        {
            if (VPBConfig.Instance == null) return;
            FloatGeometrySlot slot = VPBConfig.Instance.GalleryImportSidebarGeometry.Current;
            slot.StorePos(importSidebarSavedFloatPosCenter);
            slot.StoreSize(importSidebarSavedFloatSizeRef);
        }

        private Vector2 ResolveImportSidebarFloatHostSizeLocal()
        {
            RectTransform hostRT = null;
            if (importSidebarRT != null && importSidebarRT.parent != null)
                hostRT = importSidebarRT.parent as RectTransform;
            if (hostRT == null)
            {
                GameObject host = ResolveImportSidebarFloatHost();
                if (host != null) hostRT = host.GetComponent<RectTransform>();
            }
            if (hostRT == null && backgroundBoxGO != null)
                hostRT = backgroundBoxGO.GetComponent<RectTransform>();
            if (hostRT == null) return Vector2.zero;
            Rect r = hostRT.rect;
            return new Vector2(Mathf.Abs(r.width), Mathf.Abs(r.height));
        }

        private Vector2 ResolveImportSidebarFloatMaxSizeLocal()
        {
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            float margin = GalleryUiDesignTokens.ImportSidebarFloatHostMarginRef * s;
            float maxW = GalleryUiDesignTokens.ImportSidebarFloatMaxWidthRef * s;
            float maxH = GalleryUiDesignTokens.ImportSidebarFloatMaxHeightRef * s;
            Vector2 host = ResolveImportSidebarFloatHostSizeLocal();
            if (host.x > 8f) maxW = Mathf.Max(0f, host.x - 2f * margin);
            if (host.y > 8f) maxH = Mathf.Max(0f, host.y - 2f * margin);

            float absW = GalleryUiDesignTokens.ImportSidebarFloatAbsoluteMaxWidthRef * s;
            float absH = GalleryUiDesignTokens.ImportSidebarFloatAbsoluteMaxHeightRef * s;
            maxW = Mathf.Min(maxW, absW);
            maxH = Mathf.Min(maxH, absH);

            Vector2 min = GetImportSidebarFloatMinSizeScaled();
            if (maxW < min.x) maxW = min.x;
            if (maxH < min.y) maxH = min.y;
            return new Vector2(maxW, maxH);
        }

        private float ResolveImportSidebarFloatMaxWidthRef()
        {
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            return ResolveImportSidebarFloatMaxSizeLocal().x / s;
        }

        private float ResolveImportSidebarFloatMaxHeightRef()
        {
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            return ResolveImportSidebarFloatMaxSizeLocal().y / s;
        }

        private Vector2 GetImportSidebarFloatMinSizeScaled()
        {
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            float minH = importSidebarFloatCollapsed
                ? GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef
                : GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef;
            return new Vector2(
                GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef * s,
                minH * s);
        }

        private Vector2 GetImportSidebarFloatMaxSizeScaled()
        {
            return ResolveImportSidebarFloatMaxSizeLocal();
        }

        private static void SetImportSidebarLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetImportSidebarLayerRecursive(t.GetChild(i).gameObject, layer);
        }
    }
}
