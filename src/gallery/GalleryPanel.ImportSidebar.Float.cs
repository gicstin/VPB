using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Scene Import detachable float (Filter Presets pattern): one tree reparents dock↔canvas;
    /// drag / resize / collapse / X hide-keep; Dock reattaches and stays open.
    /// </summary>
    public partial class GalleryPanel
    {
        private static readonly Color ImportSidebarFloatTitleBarBg = new Color(0.10f, 0.22f, 0.34f, 1f);
        private static readonly Color ImportSidebarFloatFooterBarBg = new Color(0.08f, 0.10f, 0.13f, 1f);
        private static readonly Color ImportSidebarFloatPanelBg = new Color(0.07f, 0.08f, 0.10f, 0.98f);
        private const float ImportSidebarFloatChromeIconPadRef = 5f;

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

        /// <summary>True when import UI occupies the side column (grid inset / chrome suppress).</summary>
        private bool ImportSidebarOccupiesSideColumn
        {
            get { return importSidebarActive && !importSidebarDetached; }
        }

        public bool IsImportSidebarDetached { get { return importSidebarDetached; } }

        /// <summary>ALT+I — open/close floating Scene Import (detach if needed; hide keeps float).</summary>
        public void ToggleFloatingImportSidebar()
        {
            if (!ImportSidebarCategoryAllowed())
            {
                try { SetStatus(VPBTranslation.T("gallery.import.sidebar_gated_tip", "Import sidebar opens in Scenes category only")); } catch { }
                return;
            }

            if (importSidebarActive && importSidebarDetached)
            {
                HideImportSidebarFloatKeepDetach();
                return;
            }

            EnsureImportSidebarDetachedAndVisible();
        }

        /// <summary>Esc while floating Scene Import: hide-keep-detach.</summary>
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

        /// <summary>Docked header chip → float (reparent to canvas).</summary>
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
            RebuildImportSidebarContent();
            try { UpdateLayout(); } catch { }
            UpdateImportToggleBtnVisual();
        }

        /// <summary>Float footer Dock → reattach side column; stay open (work surface).</summary>
        private void DockImportSidebar()
        {
            if (!importSidebarDetached)
            {
                importSidebarOpenIntent = true;
                importSidebarOpenIntentLoaded = true;
                RefreshImportSidebarCategoryGate();
                PersistImportSidebarOpenIntent();
                return;
            }

            if (!importSidebarFloatCollapsed)
                CaptureImportSidebarFloatGeometryToMemory();
            else
                RestoreImportSidebarExpandHeightIntoSavedSize();
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
                importSidebarFloatTitleBarGO, spacing: 4f, padding: UI.Pad(6, 6, 4, 4),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            Text grip = UI.CreateLabel(importSidebarFloatTitleBarGO, "\u2807", GalleryUiDesignTokens.PopupMenuRowFontRef,
                new Color(0.65f, 0.72f, 0.80f, 1f), TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Grip");
            UI.AddLE(grip.gameObject, minWidth: 18f, preferredWidth: 18f);

            importSidebarFloatTitleLabel = UI.CreateLabel(
                importSidebarFloatTitleBarGO,
                VPBTranslation.T("gallery.import.float_title", "Scene Import"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                Color.white, TextAnchor.MiddleLeft, raycastTarget: false, name: "Title");
            UI.AddLE(importSidebarFloatTitleLabel.gameObject, flexibleWidth: 1f, minWidth: 60f);
            if (importSidebarFloatTitleBarGO.GetComponent<RectMask2D>() == null)
                importSidebarFloatTitleBarGO.AddComponent<RectMask2D>();

            importSidebarFloatCollapseBtnGO = UI.CreateUIButton(
                importSidebarFloatTitleBarGO, chromeSz, chromeSz, " ", 16, 0, 0, AnchorPresets.middleCenter,
                ToggleImportSidebarFloatCollapsed);
            importSidebarFloatCollapseBtnGO.name = "CollapseBtn";
            StyleImportSidebarFloatChromeIconBtn(importSidebarFloatCollapseBtnGO, chromeSz, "vpb_icons/chevron_up.png");
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
            StyleImportSidebarFloatChromeIconBtn(importSidebarFloatCloseBtnGO, chromeSz, "vpb_icons/x.png");
            var closeHover = importSidebarFloatCloseBtnGO.AddComponent<UIHoverDelegate>();
            closeHover.OnHoverChange += (enter) =>
            {
                if (enter) SetStatus(VPBTranslation.T(
                    "gallery.import.tip.close_hide",
                    "Hide window (keeps floating position). Use Dock in footer to reattach."));
                else SetStatus(null);
            };

            var headerDrag = importSidebarFloatTitleBarGO.AddComponent<ImportSidebarPanelDrag>();
            headerDrag.Target = importSidebarRT;
            headerDrag.OnMoved = OnImportSidebarFloatMoved;

            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef;
            importSidebarFloatFooterGO = UI.CreateChildRT(importSidebarRoot, "FloatFooter", AnchorPresets.hStretchBottom,
                new Vector2(0f, footerH), Vector2.zero);
            Image footerBg = UI.AddImage(importSidebarFloatFooterGO, ImportSidebarFloatFooterBarBg);
            if (footerBg != null) footerBg.raycastTarget = true;
            RectTransform footerRT = importSidebarFloatFooterGO.GetComponent<RectTransform>();
            if (footerRT != null)
            {
                footerRT.pivot = new Vector2(0.5f, 0f);
                footerRT.anchoredPosition = Vector2.zero;
                footerRT.sizeDelta = new Vector2(0f, footerH);
            }
            UI.AddHLG(
                importSidebarFloatFooterGO, spacing: 6f, padding: UI.Pad(8, 8, 4, 4),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: false);

            importSidebarFloatDockBtnGO = UI.CreateUIButton(
                importSidebarFloatFooterGO, chromeSz, chromeSz, " ", 14, 0, 0, AnchorPresets.middleCenter,
                DockImportSidebar);
            importSidebarFloatDockBtnGO.name = "FloatDock";
            StyleImportSidebarFloatChromeIconBtn(
                importSidebarFloatDockBtnGO, chromeSz, "vpb_icons/panel_bottom.png", new Color(0f, 0f, 0f, 0.5f));
            if (importSidebarFloatDockBtnGO.transform.Find("Icon") == null)
            {
                StyleImportSidebarFloatChromeIconBtn(
                    importSidebarFloatDockBtnGO, chromeSz, "vpb_icons/chevron_down.png", new Color(0f, 0f, 0f, 0.5f));
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

            float rh = GalleryUiDesignTokens.ButtonSizeRef;
            importSidebarFloatResizeHandleGO = UI.AddChildGOImage(
                importSidebarFloatFooterGO, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                rh, rh, Vector2.zero, rounded: true);
            importSidebarFloatResizeHandleGO.name = "ResizeHandle";
            Image rhImg = importSidebarFloatResizeHandleGO.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            StyleImportSidebarFloatChromeIconBtn(
                importSidebarFloatResizeHandleGO, rh, "vpb_icons/chevrons_down_right.png", UI.IconButtonBackdrop);
            var resizer = importSidebarFloatResizeHandleGO.AddComponent<ImportSidebarPanelResize>();
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

        /// <summary>Docked scroll-list row — same contract as Filter Presets "Float as Window".</summary>
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
            if (img != null) img.color = new Color(0.18f, 0.30f, 0.40f, 1f);
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
                    "Detach as floating window (move / resize). Alt+I toggles."));
                else SetStatus(null);
            };
            AddTooltip(btn, "gallery.import.tip.detach",
                "Detach as floating window (move / resize). Alt+I toggles.");

            // Built while docked; hide if already restoring as float.
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
            if (go == null) return;
            Image img = go.GetComponent<Image>();
            if (img != null)
                img.color = backdropOverride.HasValue ? backdropOverride.Value : new Color(0f, 0f, 0f, 0.01f);
            Button btn = go.GetComponent<Button>();
            if (btn != null) btn.transition = Selectable.Transition.None;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
            Text label = go.GetComponentInChildren<Text>(true);
            if (label != null) label.gameObject.SetActive(false);
            float pad = ImportSidebarFloatChromeIconPadRef;
            try
            {
                Sprite spr = UI.LoadIconSprite(iconPath, UI.BarIconGlyphTint);
                if (spr != null)
                {
                    Transform existing = go.transform.Find("Icon");
                    if (existing != null)
                    {
                        Image iconImg = existing.GetComponent<Image>();
                        if (iconImg != null)
                        {
                            iconImg.sprite = spr;
                            iconImg.color = Color.white;
                            RectTransform irt = existing as RectTransform;
                            if (irt != null)
                                irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
                        }
                    }
                    else
                        UI.AddIconToButton(go, spr, pad, backdropOverride ?? new Color(0f, 0f, 0f, 0f));
                }
            }
            catch { }
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
                if (VPBConfig.Instance.GalleryImportSidebarPosSaved)
                {
                    importSidebarSavedFloatPosCenter = new Vector2(
                        VPBConfig.Instance.GalleryImportSidebarPosX,
                        VPBConfig.Instance.GalleryImportSidebarPosY);
                }
                if (VPBConfig.Instance.GalleryImportSidebarSizeSaved)
                {
                    float w = VPBConfig.Instance.GalleryImportSidebarWidthRef;
                    float h = VPBConfig.Instance.GalleryImportSidebarHeightRef;
                    if (w >= GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef
                        && h >= GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef)
                    {
                        // Prefer absolute ceiling on load (host may not be ready); live layout clamps to host.
                        importSidebarSavedFloatSizeRef = new Vector2(
                            Mathf.Clamp(w, GalleryUiDesignTokens.ImportSidebarFloatMinWidthRef, GalleryUiDesignTokens.ImportSidebarFloatAbsoluteMaxWidthRef),
                            Mathf.Clamp(h, GalleryUiDesignTokens.ImportSidebarFloatMinHeightRef, GalleryUiDesignTokens.ImportSidebarFloatAbsoluteMaxHeightRef));
                    }
                }
            }
            catch { }
        }

        private void ApplyImportSidebarDetachChrome(bool reposition, bool persist)
        {
            importSidebarDetached = true;
            GameObject floatHost = ResolveImportSidebarFloatHost();
            Vector3 keepWorld = importSidebarRT != null ? importSidebarRT.position : Vector3.zero;
            bool hadWorld = importSidebarRT != null && importSidebarRoot != null
                && importSidebarRoot.transform.parent != null
                && (floatHost == null || importSidebarRoot.transform.parent != floatHost.transform);

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
            if (importSidebarHeaderBtn != null) importSidebarHeaderBtn.interactable = false;

            if (importSidebarRT != null)
            {
                importSidebarRT.anchorMin = new Vector2(0.5f, 0.5f);
                importSidebarRT.anchorMax = new Vector2(0.5f, 0.5f);
                importSidebarRT.pivot = new Vector2(0f, 1f);

                if (reposition)
                {
                    if (importSidebarSavedFloatPosCenter.HasValue)
                        ApplyImportSidebarFloatAnchorsAndPos(ChromeScale > 0f ? ChromeScale : 1f);
                    else if (hadWorld && floatHost != null)
                    {
                        importSidebarRT.position = keepWorld;
                        importSidebarSavedFloatPosCenter = ImportSidebarTopLeftToCenter(
                            importSidebarRT.anchoredPosition, importSidebarRT.sizeDelta);
                    }
                    else
                    {
                        importSidebarSavedFloatPosCenter = Vector2.zero;
                        ApplyImportSidebarFloatAnchorsAndPos(ChromeScale > 0f ? ChromeScale : 1f);
                    }
                }
            }

            if (importSidebarRoot != null)
                importSidebarRoot.transform.SetAsLastSibling();

            // Reparent may change host size — clamp saved geometry to new ceiling.
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
                importSidebarRootBg.color = new Color(0f, 0f, 0f, 0f);
            RectMask2D mask = importSidebarRoot != null ? importSidebarRoot.GetComponent<RectMask2D>() : null;
            if (mask != null) UnityEngine.Object.Destroy(mask);

            if (importSidebarFloatTitleBarGO != null) importSidebarFloatTitleBarGO.SetActive(false);
            if (importSidebarFloatFooterGO != null) importSidebarFloatFooterGO.SetActive(false);
            if (importSidebarFloatDetachBtnGO != null) importSidebarFloatDetachBtnGO.SetActive(true);
            SyncImportSidebarFloatDetachRowLabel();
            if (importSidebarHeaderRT != null) importSidebarHeaderRT.gameObject.SetActive(true);
            if (importSidebarBodyScrollRT != null) importSidebarBodyScrollRT.gameObject.SetActive(true);
            if (importSidebarApplyRT != null) importSidebarApplyRT.gameObject.SetActive(true);
            if (importSidebarHeaderBtn != null) importSidebarHeaderBtn.interactable = true;

            if (persist) PersistImportSidebarDetachedState(false);
        }

        private GameObject ResolveImportSidebarFloatHost()
        {
            if (canvas != null) return canvas.gameObject;
            return backgroundBoxGO;
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
            Vector2 size = importSidebarRT.sizeDelta;
            Vector2 center = importSidebarSavedFloatPosCenter.HasValue
                ? importSidebarSavedFloatPosCenter.Value
                : Vector2.zero;
            importSidebarRT.anchoredPosition = ImportSidebarCenterToTopLeft(center, size);
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
            string path = importSidebarFloatCollapsed ? "vpb_icons/chevron_down.png" : "vpb_icons/chevron_up.png";
            Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
            if (spr == null) return;
            if (importSidebarFloatCollapseBtnIcon != null)
            {
                importSidebarFloatCollapseBtnIcon.sprite = spr;
                importSidebarFloatCollapseBtnIcon.color = Color.white;
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
                importSidebarSavedFloatPosCenter = ImportSidebarTopLeftToCenter(
                    importSidebarCollapsedTopLeftPos.Value, new Vector2(w, h));
                PersistImportSidebarFloatGeometry();
                return;
            }
            CaptureImportSidebarFloatGeometryToMemory();
            PersistImportSidebarFloatGeometry();
        }

        private void OnImportSidebarFloatResizing()
        {
            // Capture live size only — full ApplyImportSidebarBaseRect repositions from center and
            // rebuilds chrome every drag tick (jitter + unnecessary work). End-drag applies layout.
            CaptureImportSidebarFloatGeometryToMemory();
            try
            {
                float s = ChromeScale > 0f ? ChromeScale : 1f;
                ApplyImportSidebarChromeHorizontalInsets(s, out float insetLeft, out float insetRight);
                float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
                float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
                float headerH = ImportSidebarBaseHeaderHeight * s;
                float headerGap = ImportSidebarBaseHeaderGap * s;
                float applyH = ImportSidebarBaseApplyHeight * s;
                LayoutImportSidebarInnerChrome(s, titleH, footerH, headerH, headerGap, applyH, insetLeft, insetRight, showBody: true);
                AlignImportSidebarScrollViewport(s);
            }
            catch { }
        }

        private void OnImportSidebarFloatResized()
        {
            CaptureImportSidebarFloatGeometryToMemory();
            PersistImportSidebarFloatGeometry();
            try { ApplyImportSidebarBaseRect(ChromeScale); } catch { }
            try { RebuildImportSidebarContent(); } catch { }
        }

        private void CaptureImportSidebarFloatGeometryToMemory()
        {
            if (importSidebarRT == null || importSidebarFloatCollapsed) return;
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            importSidebarSavedFloatPosCenter = ImportSidebarTopLeftToCenter(
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
            if (importSidebarSavedFloatPosCenter.HasValue)
            {
                VPBConfig.Instance.GalleryImportSidebarPosSaved = true;
                VPBConfig.Instance.GalleryImportSidebarPosX = importSidebarSavedFloatPosCenter.Value.x;
                VPBConfig.Instance.GalleryImportSidebarPosY = importSidebarSavedFloatPosCenter.Value.y;
            }
            if (importSidebarSavedFloatSizeRef.HasValue)
            {
                VPBConfig.Instance.GalleryImportSidebarSizeSaved = true;
                VPBConfig.Instance.GalleryImportSidebarWidthRef = importSidebarSavedFloatSizeRef.Value.x;
                VPBConfig.Instance.GalleryImportSidebarHeightRef = importSidebarSavedFloatSizeRef.Value.y;
            }
        }

        /// <summary>Float host rect in local units (canvas or panel). Zero if unavailable.</summary>
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

        /// <summary>
        /// Max float size in local px. When float host rect is known, ceiling is host−margin so low
        /// DPI/UI scale can fill the screen (token×scale alone shrinks the ceiling). Token is fallback only.
        /// </summary>
        private Vector2 ResolveImportSidebarFloatMaxSizeLocal()
        {
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            float margin = GalleryUiDesignTokens.ImportSidebarFloatHostMarginRef * s;
            float maxW = GalleryUiDesignTokens.ImportSidebarFloatMaxWidthRef * s;
            float maxH = GalleryUiDesignTokens.ImportSidebarFloatMaxHeightRef * s;
            Vector2 host = ResolveImportSidebarFloatHostSizeLocal();
            if (host.x > 8f) maxW = Mathf.Max(0f, host.x - margin);
            if (host.y > 8f) maxH = Mathf.Max(0f, host.y - margin);

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

        private static Vector2 ImportSidebarCenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 ImportSidebarTopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private static void SetImportSidebarLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                SetImportSidebarLayerRecursive(t.GetChild(i).gameObject, layer);
        }

        private sealed class ImportSidebarPanelDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public RectTransform Target;
            public Action OnMoved;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null || eventData == null) return;
                Target.anchoredPosition += eventData.delta;
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (OnMoved != null) OnMoved();
            }
        }

        private sealed class ImportSidebarPanelResize : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public RectTransform Target;
            public Func<Vector2> GetMinSize;
            public Func<Vector2> GetMaxSize;
            public Action OnResizing;
            public Action OnResized;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null || eventData == null) return;
                Vector2 size = Target.sizeDelta;
                size.x += eventData.delta.x;
                size.y -= eventData.delta.y;
                Vector2 min = GetMinSize != null ? GetMinSize() : new Vector2(220f, 320f);
                Vector2 max = GetMaxSize != null ? GetMaxSize() : new Vector2(640f, 900f);
                size.x = Mathf.Clamp(size.x, min.x, max.x);
                size.y = Mathf.Clamp(size.y, min.y, max.y);
                Target.sizeDelta = size;
                if (OnResizing != null) OnResizing();
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (OnResized != null) OnResized();
            }
        }
    }
}
