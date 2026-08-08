using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Geometry mirrors side tab column tokens; aliases keep import module call sites stable.
        private const float ImportSidebarBaseWidth = GalleryUiDesignTokens.ImportSidebarWidthRef;
        private const float ImportSidebarBaseHeaderHeight = GalleryUiDesignTokens.ImportSidebarHeaderHeightRef;
        private const float ImportSidebarBaseApplyHeight = GalleryUiDesignTokens.ImportSidebarApplyHeightRef;
        private const float ImportSidebarBaseApplyReasonHeight = GalleryUiDesignTokens.ImportSidebarApplyReasonHeightRef;
        private const float ImportSidebarBaseSideMargin = GalleryUiDesignTokens.ImportSidebarSideMarginRef;
        private const float ImportSidebarBaseTopRowRef = GalleryUiDesignTokens.ImportSidebarTopRowRef;
        private const float ImportSidebarScrollBarWidthRef = GalleryUiDesignTokens.ImportSidebarScrollBarWidthRef;
        private const float ImportSidebarInnerPadHRef = GalleryUiDesignTokens.ImportSidebarInnerPadHRef;
        private const float ImportSidebarBaseHeaderGap = GalleryUiDesignTokens.ImportSidebarHeaderGapRef;
        private const float ImportSidebarBaseRowSpacing = GalleryUiDesignTokens.ImportSidebarRowSpacingRef;

        private static readonly string[] ImportWizardStepTitleKeys =
        {
            "gallery.import.wizard.step_atoms",
            "gallery.import.wizard.step_type",
            "gallery.import.wizard.step_options"
        };

        private static readonly string[] ImportWizardStepTitleDefaults =
        {
            "Atoms", "Resource type", "Options"
        };

        public const float ImportSidebarBaseRowHeight = GalleryUiDesignTokens.ImportSidebarRowHeightRef;
        public const int ImportSidebarBaseFontSize = GalleryUiDesignTokens.ImportSidebarFontRef;
        public const int ImportSidebarBaseFontSizeMin = GalleryUiDesignTokens.ImportSidebarFontMin;

        private RectTransform importSidebarRT;
        private Transform importSidebarHeaderRoot;
        private RectTransform importSidebarHeaderRT;
        private Text importSidebarHeaderLabel;
        private Button importSidebarHeaderBtn;
        // Single scroll body: header pinned top, Apply pinned bottom, everything else scrolls between them.
        private RectTransform importSidebarBodyScrollRT;     // CreateVScrollableContent root (the scroll viewport host)
        private RectTransform importSidebarScrollContentRT;  // VLG content node holding all rows (target of ForceRebuild)
        private RectTransform importSidebarApplyRT;          // pinned Apply button
        private GameObject importSidebarHeaderFloatBtnGO;    // docked header "Float" control
        private Text importSidebarHeaderFloatBtnText;

        partial void BuildImportSidebar()
        {
            // Parent is backgroundBoxGO so the sidebar layers above the gallery grid
            // at the same z-depth as rightTabScrollGO (Creator/Category column).
            Transform parent = ResolveImportSidebarParent();
            if (parent == null)
            {
                LogUtil.LogError("[VPB import] Could not resolve sidebar parent: gallery panel not initialized");
                return;
            }

            // [diag] Root is created active and only deactivated at the end, so a mid-build throw leaves a half-rendered header; stage logs pin the throw, the catch destroys the partial tree so failure is a clean no-op.
            try
            {
                LogUtil.Log("[VPB import][diag] build: start");

                importSidebarRoot = new GameObject("VPB_ImportSidebar");
                importSidebarRoot.transform.SetParent(parent, false);

                importSidebarRT = importSidebarRoot.AddComponent<RectTransform>();
                ApplyImportSidebarBaseRect(1f);

                // Transparent root, like leftTabScrollGO / rightTabScrollGO. Rows render against
                // the gallery panel background, so the sidebar visually reads as part of the same
                // UI family rather than a foreign popup tinted with PopupBackdrop.
                importSidebarRootBg = UI.AddImage(importSidebarRoot, new Color(0f, 0f, 0f, 0f), false);

                int siblingIndex = ResolveImportSidebarSiblingIndex(parent);
                importSidebarRoot.transform.SetSiblingIndex(siblingIndex);

                LogUtil.Log("[VPB import][diag] build: header");
                BuildImportSidebarHeader();
                LogUtil.Log("[VPB import][diag] build: body scroll");
                BuildImportSidebarBodyScroll();
                LogUtil.Log("[VPB import][diag] build: pinned apply");
                BuildImportSidebarPinnedApply();
                LogUtil.Log("[VPB import][diag] build: wizard body");
                BuildImportSidebarWizardBody();
                LogUtil.Log("[VPB import][diag] build: float chrome");
                BuildImportSidebarFloatChrome();
                LoadImportSidebarFloatGeometryFromConfig();
                // Match QuickFilters / Settings: reparent to float host BEFORE applying float
                // geometry. Saved center is canvas-local — Apply+Clamp under dock parent
                // rewrites it into the wrong space and restore lands wrong.
                if (importSidebarDetached)
                    ApplyImportSidebarDetachChrome(reposition: true, persist: false);
                else
                    ApplyImportSidebarDockChrome(persist: false);
                // Float entry lives on docked header (not scroll row).

                // Re-run rect/font scaling whenever VPB's inner-pane scale changes (Settings UI scale slider).
                innerPaneScaleActions.Add(ApplyImportSidebarBaseRect);
                // The scroll content (VLG + ContentSizeFitter) only recomputes when forced; rebuild it after a scale
                // change so row heights / the type-radio grid settle to the new scale.
                innerPaneScaleActions.Add(s => RebuildImportSidebarContent());

                ApplyImportSidebarBaseRect(ChromeScale);
                // Row label fonts are scaled only by the innerPaneScaleActions closures (fired on a
                // scale-slider change). Fire them once now so a sidebar built at a non-1 global UI
                // scale renders at the correct text size instead of the unscaled design size.
                try { ApplyInnerPaneScaleLegacyActions(ChromeScale); } catch { }
                RebuildImportSidebarContent();
                importSidebarRoot.SetActive(false);
                LogUtil.Log("[VPB import][diag] build: complete OK");
            }
            catch (System.Exception ex)
            {
                LogUtil.LogError("[VPB import][diag] build FAILED (stage just logged above): " + ex);
                if (importSidebarRoot != null)
                {
                    UnityEngine.Object.Destroy(importSidebarRoot);
                    importSidebarRoot = null;
                }
            }
        }

        // Vertical-stretch rect (anchored to panel top AND bottom) with raw-px insets, mirroring leftTabRT/rightTabRT
        // so it tracks the panel at any UI scale instead of a fixed-height box that overflows the column.
        private float ImportSidebarTopOffsetY(float s) => -ImportSidebarBaseTopRowRef * s;

        private void ApplyImportSidebarBaseRect(float s)
        {
            if (importSidebarRT == null) return;
            if (importSidebarDetached)
                ApplyImportSidebarFloatRect(s);
            else
                ApplyImportSidebarDockRect(s);

            try { AlignImportSidebarScrollViewport(s); } catch { }
            try { SyncImportSidebarScrollContentLayout(s); } catch { }
            try { SyncImportSidebarTypeRadioGridWidth(s); } catch { }
            try { SyncImportSidebarHoverChrome(); } catch { }
            try { StyleImportSidebarHeader(s); } catch { }
            EnsureImportSidebarHeaderClickable();
            SyncImportSidebarHeaderLabel();
            SyncImportSidebarHeaderTypography(s);
            SyncImportSidebarHeaderGateVisual();
            try { RefreshImportSidebarWizardHeader(); } catch { }
            SyncImportSidebarFloatChromeScale(s);
            if (!importSidebarDetached)
                SyncImportSidebarFloatDetachRowLabel();
        }

        private void ApplyImportSidebarDockRect(float s)
        {
            float w = ImportSidebarBaseWidth * s;
            float leftMargin = SideTabColumnLeftInsetX(s);
            float rightMargin = -SideTabColumnRightInsetX(s);
            float top = ImportSidebarTopOffsetY(s);
            float bottom = SideTabScrollBottomInsetY();

            importSidebarRT.pivot = new Vector2(0.5f, 0.5f);
            importSidebarRT.sizeDelta = Vector2.zero;
            if (importSidebarOnLeft)
            {
                importSidebarRT.anchorMin = new Vector2(0f, 0f);
                importSidebarRT.anchorMax = new Vector2(0f, 1f);
                importSidebarRT.offsetMin = new Vector2(leftMargin, bottom);
                importSidebarRT.offsetMax = new Vector2(leftMargin + w, top);
            }
            else
            {
                importSidebarRT.anchorMin = new Vector2(1f, 0f);
                importSidebarRT.anchorMax = new Vector2(1f, 1f);
                importSidebarRT.offsetMin = new Vector2(-rightMargin - w, bottom);
                importSidebarRT.offsetMax = new Vector2(-rightMargin, top);
            }

            float headerH = ImportSidebarBaseHeaderHeight * s;
            float headerGap = ImportSidebarBaseHeaderGap * s;
            float applyH = ImportSidebarBaseApplyHeight * s;
            float reasonH = ResolveImportSidebarApplyReasonHeight(s);
            ApplyImportSidebarChromeHorizontalInsets(s, out float insetLeft, out float insetRight);
            LayoutImportSidebarInnerChrome(s, 0f, 0f, headerH, headerGap, applyH, reasonH, insetLeft, insetRight, showBody: true);
        }

        private void ApplyImportSidebarFloatRect(float s)
        {
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
            float footerH = importSidebarFloatCollapsed ? 0f : GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            // Float title bar owns identity (type → target). Docked header chip stays hidden — no duplicate banner.
            float headerH = 0f;
            float headerGap = 0f;
            float applyH = importSidebarFloatCollapsed ? 0f : ImportSidebarBaseApplyHeight * s;
            float reasonH = importSidebarFloatCollapsed ? 0f : ResolveImportSidebarApplyReasonHeight(s);

            float wRef = ResolveImportSidebarFloatWidthRef();
            float hRef = importSidebarFloatCollapsed
                ? GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef
                : ResolveImportSidebarFloatHeightRef();
            float w = wRef * s;
            float h = hRef * s;

            importSidebarRT.anchorMin = new Vector2(0.5f, 0.5f);
            importSidebarRT.anchorMax = new Vector2(0.5f, 0.5f);
            importSidebarRT.pivot = new Vector2(0f, 1f);
            // Pivot top-left: resize only — keep title corner fixed on UI scale.
            // Center→topLeft re-apply (ApplyImportSidebarFloatAnchorsAndPos) drifts on size change.
            Vector2 keepTopLeft = importSidebarRT.anchoredPosition;
            importSidebarRT.sizeDelta = new Vector2(w, h);

            if (importSidebarCollapsedTopLeftPos.HasValue && importSidebarFloatCollapsed)
                importSidebarRT.anchoredPosition = importSidebarCollapsedTopLeftPos.Value;
            else if (!importSidebarSavedFloatPosCenter.HasValue)
                ApplyImportSidebarFloatAnchorsAndPos(s);
            else
                importSidebarRT.anchoredPosition = keepTopLeft;
            ClampImportSidebarFloatIntoHost();
            if (!importSidebarFloatCollapsed)
            {
                // Memory + Instance fields only — BaseRect runs on every scale/rebuild.
                // UI-scale hotkey deferred Save flushes disk.
                CaptureImportSidebarFloatGeometryToMemory();
                try { PersistImportSidebarFloatGeometryFieldsOnly(); } catch { }
            }

            ApplyImportSidebarChromeHorizontalInsets(s, out float insetLeft, out float insetRight);
            LayoutImportSidebarInnerChrome(s, titleH, footerH, headerH, headerGap, applyH, reasonH, insetLeft, insetRight,
                showBody: !importSidebarFloatCollapsed);

            if (importSidebarRootBg != null)
            {
                // Collapsed: match title bar so no dark panel strip under chrome.
                importSidebarRootBg.color = importSidebarFloatCollapsed
                    ? ImportSidebarFloatTitleBarBg
                    : ImportSidebarFloatPanelBg;
            }
            if (importSidebarFloatTitleBarGO != null)
            {
                importSidebarFloatTitleBarGO.SetActive(true);
                RectTransform titleRT = importSidebarFloatTitleBarGO.GetComponent<RectTransform>();
                if (titleRT != null)
                {
                    titleRT.anchorMin = new Vector2(0f, 1f);
                    titleRT.anchorMax = new Vector2(1f, 1f);
                    titleRT.pivot = new Vector2(0.5f, 1f);
                    titleRT.anchoredPosition = Vector2.zero;
                    // Match Filter Presets: sizeDelta only — offsetMin/Max fight point-Y anchors.
                    titleRT.sizeDelta = new Vector2(0f, titleH);
                }
                UI.LayoutFloatTitleWindowIcon(
                    importSidebarFloatTitleBarGO,
                    GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                HorizontalLayoutGroup titleHlg = importSidebarFloatTitleBarGO.GetComponent<HorizontalLayoutGroup>();
                Transform gripTr = importSidebarFloatTitleBarGO.transform.Find("Grip");
                UI.ApplyFloatTitleBarMetrics(
                    titleHlg, gripTr != null ? gripTr.gameObject : null, s);
            }
            if (importSidebarFloatFooterGO != null)
            {
                importSidebarFloatFooterGO.SetActive(!importSidebarFloatCollapsed);
                RectTransform footerRT = importSidebarFloatFooterGO.GetComponent<RectTransform>();
                if (footerRT != null && !importSidebarFloatCollapsed)
                {
                    footerRT.anchorMin = new Vector2(0f, 0f);
                    footerRT.anchorMax = new Vector2(1f, 0f);
                    footerRT.pivot = new Vector2(0.5f, 0f);
                    footerRT.anchoredPosition = Vector2.zero;
                    footerRT.sizeDelta = new Vector2(0f, footerH);
                }
                importSidebarFloatFooterGO.transform.SetAsLastSibling();
            }
            if (importSidebarFloatTitleBarGO != null)
                importSidebarFloatTitleBarGO.transform.SetAsLastSibling();
        }

        private float ResolveImportSidebarApplyReasonHeight(float s)
        {
            if (importSidebarApplyReasonRT == null || !importSidebarApplyReasonRT.gameObject.activeSelf)
                return 0f;
            return ImportSidebarBaseApplyReasonHeight * s;
        }

        /// <summary>Reposition Apply + reason strip without full BaseRect (called from RefreshApplyButtonEnabled).</summary>
        private void LayoutImportSidebarApplyBand(float s)
        {
            if (importSidebarRT == null || !importSidebarActive) return;
            float titleH = 0f;
            float footerH = 0f;
            if (importSidebarDetached)
            {
                titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
                footerH = importSidebarFloatCollapsed ? 0f : GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            }
            // Detached: title bar only (no docked header). Docked: full header chip.
            bool hideDockedHeader = importSidebarDetached;
            float headerH = (hideDockedHeader || importSidebarFloatCollapsed) ? 0f : ImportSidebarBaseHeaderHeight * s;
            float headerGap = (hideDockedHeader || importSidebarFloatCollapsed) ? 0f : ImportSidebarBaseHeaderGap * s;
            float applyH = (importSidebarDetached && importSidebarFloatCollapsed) ? 0f : ImportSidebarBaseApplyHeight * s;
            float reasonH = (importSidebarDetached && importSidebarFloatCollapsed) ? 0f : ResolveImportSidebarApplyReasonHeight(s);
            ApplyImportSidebarChromeHorizontalInsets(s, out float insetLeft, out float insetRight);
            bool showBody = !(importSidebarDetached && importSidebarFloatCollapsed);
            LayoutImportSidebarInnerChrome(s, titleH, footerH, headerH, headerGap, applyH, reasonH, insetLeft, insetRight, showBody);
        }

        private void LayoutImportSidebarInnerChrome(
            float s, float titleH, float footerH, float headerH, float headerGap, float applyH, float reasonH,
            float insetLeft, float insetRight, bool showBody)
        {
            if (importSidebarHeaderRT != null)
            {
                // Docked side panel only — float window already has a title bar with the same summary.
                bool showHeader = showBody && !importSidebarDetached && headerH > 0.5f;
                importSidebarHeaderRT.gameObject.SetActive(showHeader);
                if (showHeader)
                {
                    importSidebarHeaderRT.anchorMin = new Vector2(0f, 1f);
                    importSidebarHeaderRT.anchorMax = new Vector2(1f, 1f);
                    importSidebarHeaderRT.pivot = new Vector2(0.5f, 1f);
                    importSidebarHeaderRT.anchoredPosition = new Vector2(0f, -titleH);
                    importSidebarHeaderRT.offsetMin = new Vector2(insetLeft, -titleH - headerH);
                    importSidebarHeaderRT.offsetMax = new Vector2(-insetRight, -titleH);
                }
            }

            float applyBand = applyH + reasonH;

            if (importSidebarApplyRT != null)
            {
                importSidebarApplyRT.gameObject.SetActive(showBody);
                if (showBody)
                {
                    importSidebarApplyRT.anchorMin = new Vector2(0f, 0f);
                    importSidebarApplyRT.anchorMax = new Vector2(1f, 0f);
                    importSidebarApplyRT.pivot = new Vector2(0.5f, 0f);
                    importSidebarApplyRT.anchoredPosition = new Vector2(0f, footerH);
                    importSidebarApplyRT.offsetMin = new Vector2(insetLeft, footerH);
                    importSidebarApplyRT.offsetMax = new Vector2(-insetRight, footerH + applyH);
                }
            }

            if (importSidebarApplyReasonRT != null)
            {
                bool showReason = showBody && importSidebarApplyReasonRT.gameObject.activeSelf && reasonH > 0.5f;
                if (showReason)
                {
                    importSidebarApplyReasonRT.anchorMin = new Vector2(0f, 0f);
                    importSidebarApplyReasonRT.anchorMax = new Vector2(1f, 0f);
                    importSidebarApplyReasonRT.pivot = new Vector2(0.5f, 0f);
                    importSidebarApplyReasonRT.anchoredPosition = new Vector2(0f, footerH + applyH);
                    importSidebarApplyReasonRT.offsetMin = new Vector2(insetLeft, footerH + applyH);
                    importSidebarApplyReasonRT.offsetMax = new Vector2(-insetRight, footerH + applyBand);
                }
            }

            if (importSidebarBodyScrollRT != null)
            {
                importSidebarBodyScrollRT.gameObject.SetActive(showBody);
                if (showBody)
                {
                    importSidebarBodyScrollRT.anchorMin = Vector2.zero;
                    importSidebarBodyScrollRT.anchorMax = Vector2.one;
                    importSidebarBodyScrollRT.pivot = new Vector2(0.5f, 0.5f);
                    importSidebarBodyScrollRT.offsetMin = new Vector2(0f, footerH + applyBand);
                    importSidebarBodyScrollRT.offsetMax = new Vector2(0f, -(titleH + headerH + headerGap));
                }
            }
        }

        private void SyncImportSidebarFloatChromeScale(float s)
        {
            if (!importSidebarDetached) return;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            ScaleImportSidebarFloatChromeBtn(importSidebarFloatCollapseBtnGO, chromeSz, s);
            ScaleImportSidebarFloatChromeBtn(importSidebarFloatCloseBtnGO, chromeSz, s);
            ScaleImportSidebarFloatChromeBtn(importSidebarFloatResizeHandleGO, chromeSz, s);
            if (importSidebarFloatDockBtnGO != null)
            {
                LayoutElement le = importSidebarFloatDockBtnGO.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredWidth = 72f * s;
                    le.preferredHeight = chromeSz;
                    le.minWidth = 56f * s;
                }
                Text dockTxt = importSidebarFloatDockBtnGO.GetComponentInChildren<Text>();
                if (dockTxt != null)
                    GalleryUiMetrics.ApplyFont(dockTxt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            if (importSidebarFloatTitleLabel != null)
                GalleryUiMetrics.ApplyFont(importSidebarFloatTitleLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
        }

        private static void ScaleImportSidebarFloatChromeBtn(GameObject go, float size, float s)
        {
            if (go == null || size <= 0f) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.minWidth = size;
            le.minHeight = size;
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(size, size);
            Transform iconTr = go.transform.Find("Icon");
            if (iconTr != null)
            {
                RectTransform irt = iconTr as RectTransform;
                if (irt != null)
                {
                    float pad = 5f * s;
                    irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
                }
            }
        }

        private static float ImportSidebarScrollBarWidthPx(float s) => ImportSidebarScrollBarWidthRef * s;

        /// <summary>Horizontal insets: flush on panel-outer edge; pad only before scrollbar. Uses live width when floating.</summary>
        private void GetImportSidebarContentWidthInsets(float s, out float insetLeft, out float insetRight, out float contentWidth)
        {
            float scrollW = ImportSidebarScrollBarWidthPx(s);
            float padInner = ImportSidebarInnerPadHRef * s;
            insetLeft = 0f;
            insetRight = scrollW + padInner;
            float panelW = ImportSidebarBaseWidth * s;
            if (importSidebarDetached && importSidebarRT != null)
            {
                float live = importSidebarRT.sizeDelta.x;
                if (live < 1f) live = importSidebarRT.rect.width;
                if (live > 1f) panelW = live;
            }
            contentWidth = Mathf.Max(8f, panelW - insetRight);
        }

        private void ApplyImportSidebarChromeHorizontalInsets(float s,
            out float insetLeft, out float insetRight)
        {
            GetImportSidebarContentWidthInsets(s, out insetLeft, out insetRight, out _);
        }

        private void SyncImportSidebarTypeRadioGridWidth(float s)
        {
            if (importSidebarTypeRadioContainer == null) return;
            GridLayoutGroup g = importSidebarTypeRadioContainer.GetComponent<GridLayoutGroup>();
            LayoutElement le = importSidebarTypeRadioContainer.GetComponent<LayoutElement>();
            if (g == null) return;
            GetImportSidebarContentWidthInsets(s, out _, out _, out float contentWidth);
            float rowH = ImportSidebarBaseRowHeight * s;
            float gap = ImportSidebarBaseRowSpacing * s;
            float gridW = Mathf.Floor(contentWidth);
            float cellW = Mathf.Floor((gridW - gap) * 0.5f);
            const int typeRadioRows = 6;
            g.cellSize = new Vector2(cellW, rowH);
            g.spacing = new Vector2(gap, gap);
            if (le != null)
            {
                le.preferredWidth = gridW;
                le.flexibleWidth = 0f;
                le.preferredHeight = typeRadioRows * rowH + (typeRadioRows - 1) * gap;
            }
        }

        /// <summary>
        /// Atom/type/option rows add Button without UIHoverBorder — gallery pane enforcer only walks
        /// backgroundBoxGO. Floated import reparents to canvas, so auto-restore never gets borders
        /// until a dock round-trip. Apply policy on the import tree itself, then force inward rims
        /// under scroll RectMask2D (outward clips).
        /// </summary>
        private void SyncImportSidebarHoverChrome()
        {
            if (importSidebarRoot != null)
            {
                try { UI.ApplyGalleryPaneHoverPolicy(importSidebarRoot); } catch { }
            }
            SyncImportSidebarScrollHoverBorders();
        }

        /// <summary>Scroll body sits under RectMask2D — outward hover rims clip; draw inward like side-tab rows.</summary>
        private void SyncImportSidebarScrollHoverBorders()
        {
            if (importSidebarScrollContentRT == null) return;
            UIHoverBorder[] borders = importSidebarScrollContentRT.GetComponentsInChildren<UIHoverBorder>(true);
            for (int i = 0; i < borders.Length; i++)
            {
                UIHoverBorder hb = borders[i];
                if (hb == null) continue;
                hb.inward = true;
                try { hb.ApplyBorderSettings(); } catch { }
            }
        }

        private void SyncImportSidebarScrollContentLayout(float s)
        {
            if (importSidebarScrollContentRT == null) return;
            VerticalLayoutGroup vlg = importSidebarScrollContentRT.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) return;
            float gap = ImportSidebarBaseRowSpacing * s;
            vlg.spacing = gap;
            vlg.padding = new RectOffset(0, 0, Mathf.RoundToInt(gap), Mathf.RoundToInt(gap * 0.5f));
        }

        private void SyncImportSidebarHeaderTypography(float s)
        {
            if (importSidebarHeaderLabel == null) return;
            GalleryUiMetrics.ApplyFont(importSidebarHeaderLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
        }

        private void SyncImportSidebarHeaderGateVisual()
        {
            if (importSidebarHeaderRT == null) return;
            bool scenesLocked = importSidebarActive && importSidebarDetached && !ImportSidebarCategoryAllowed();
            bool gatedClosed = importSidebarOpenIntent && !ImportSidebarCategoryAllowed() && !importSidebarDetached;

            Image bg = importSidebarHeaderRT.GetComponent<Image>();
            if (bg != null)
            {
                if (scenesLocked)
                    bg.color = ImportSidebarScenesLockedHeaderBg;
                else if (gatedClosed)
                    bg.color = GalleryUiColorTokens.SurfaceMid;
                else
                    bg.color = ImportSidebarHeaderBg;
            }
            if (importSidebarHeaderLabel != null)
            {
                if (scenesLocked)
                    importSidebarHeaderLabel.color = ImportSidebarScenesLockedBanner;
                else if (gatedClosed)
                    importSidebarHeaderLabel.color = GalleryUiColorTokens.TextMuted;
                else
                    importSidebarHeaderLabel.color = GalleryUiColorTokens.TextPrimary;
            }
            if (importSidebarHeaderBtn != null)
                importSidebarHeaderBtn.interactable = !gatedClosed && !importSidebarDetached;
            if (importSidebarHeaderFloatBtnGO != null)
                importSidebarHeaderFloatBtnGO.SetActive(!importSidebarDetached && importSidebarActive);
        }

        private void BuildImportSidebarHeader()
        {
            GameObject header = UI.CreateChildRT(importSidebarRoot, "Header", AnchorPresets.hStretchTop, new Vector2(0f, ImportSidebarBaseHeaderHeight));
            importSidebarHeaderRT = header.GetComponent<RectTransform>();
            importSidebarHeaderRoot = importSidebarHeaderRT;

            Image bg = AddImportSidebarRoundedBg(header, ImportSidebarHeaderBg);

            UI.AddHLG(
                header, spacing: 4f, padding: UI.Pad(2, 4, 2, 2),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: true);

            GameObject labelHost = new GameObject("HeaderLabelHost");
            labelHost.transform.SetParent(header.transform, false);
            UI.AddLE(labelHost, flexibleWidth: 1f, minWidth: 40f);
            RectTransform labelHostRT = labelHost.GetComponent<RectTransform>();
            if (labelHostRT == null) labelHostRT = labelHost.AddComponent<RectTransform>();

            importSidebarHeaderLabel = CreateImportSidebarLabel(
                labelHost.transform,
                FormatSidePanelHeaderLabel(importSidebarOnLeft, SidePanelHeaderTranslation("gallery.import.sidebar_header", "Import")),
                SidePanelHeaderFontRef);
            importSidebarHeaderLabel.color = GalleryUiColorTokens.TextPrimary;
            importSidebarHeaderLabel.fontStyle = FontStyle.Normal;
            importSidebarHeaderLabel.alignment = TextAnchor.MiddleCenter;
            importSidebarHeaderLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            importSidebarHeaderLabel.verticalOverflow = VerticalWrapMode.Truncate;
            StyleImportSidebarHeader();

            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef;
            GameObject floatBtn = UI.CreateUIButton(
                header, chromeSz * 1.6f, chromeSz,
                VPBTranslation.T("gallery.import.detach_short", "Float"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                0, 0, AnchorPresets.middleCenter,
                DetachImportSidebar);
            floatBtn.name = "HeaderFloat";
            importSidebarHeaderFloatBtnGO = floatBtn;
            Image floatImg = floatBtn.GetComponent<Image>();
            if (floatImg != null) floatImg.color = ImportSidebarSecondaryActionBg;
            Text floatTxt = floatBtn.GetComponentInChildren<Text>();
            if (floatTxt != null)
            {
                importSidebarHeaderFloatBtnText = floatTxt;
                floatTxt.alignment = TextAnchor.MiddleCenter;
                floatTxt.color = UI.PopupText;
                try { VPBUiFont.ApplyTo(floatTxt); } catch { }
            }
            UI.AddLE(floatBtn, preferredWidth: chromeSz * 1.6f, preferredHeight: chromeSz, flexibleWidth: 0f);
            AddTooltip(floatBtn, "gallery.import.tip.detach",
                "Detach as floating window (move / resize). Alt+I toggles.");

            importSidebarHeaderBtn = header.AddComponent<Button>();
            importSidebarHeaderBtn.targetGraphic = bg;
            importSidebarHeaderBtn.onClick.AddListener(() => ToggleImportSidebar());
            AddTooltip(header, "gallery.side.collapse_tip", "Collapse side list");

            GameObject floatBtnC = floatBtn;
            Text floatTxtC = importSidebarHeaderFloatBtnText;
            innerPaneScaleActions.Add(s =>
            {
                if (floatBtnC == null) return;
                float sz = GalleryUiDesignTokens.ButtonSizeRef * s;
                LayoutElement le = floatBtnC.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredWidth = sz * 1.6f;
                    le.preferredHeight = sz;
                }
                if (floatTxtC != null)
                    GalleryUiMetrics.ApplyFont(floatTxtC, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            });
        }

        private void EnsureImportSidebarHeaderClickable()
        {
            if (importSidebarHeaderRT == null) return;
            GameObject headerGo = importSidebarHeaderRT.gameObject;
            if (importSidebarHeaderBtn == null)
                importSidebarHeaderBtn = headerGo.GetComponent<Button>();
            if (importSidebarHeaderBtn != null) return;

            Image bg = headerGo.GetComponent<Image>();
            if (bg == null) return;
            importSidebarHeaderBtn = headerGo.AddComponent<Button>();
            importSidebarHeaderBtn.targetGraphic = bg;
            importSidebarHeaderBtn.onClick.AddListener(() => ToggleImportSidebar());
            AddTooltip(headerGo, "gallery.side.collapse_tip", "Collapse side list");
        }

        private void ApplyImportSidebarHeaderLabelText(string full)
        {
            if (importSidebarHeaderLabel == null) return;
            float s = ChromeScale;
            GetImportSidebarContentWidthInsets(s, out _, out _, out float contentWidth);
            // Reserve Float button width when docked.
            float floatReserve = (!importSidebarDetached && importSidebarHeaderFloatBtnGO != null
                && importSidebarHeaderFloatBtnGO.activeSelf)
                ? GalleryUiDesignTokens.ButtonSizeRef * 1.6f * s + 8f * s
                : 0f;
            float inner = contentWidth - 4f * s - floatReserve;
            if (inner <= 2f) inner = 120f * s;
            importSidebarHeaderLabel.text = EllipsizeTextPreferredWidth(importSidebarHeaderLabel, full ?? "", inner);
        }

        private void SyncImportSidebarHeaderLabel()
        {
            if (importSidebarHeaderLabel == null) return;
            string title = SidePanelHeaderTranslation("gallery.import.sidebar_header", "Import");
            ApplyImportSidebarHeaderLabelText(FormatSidePanelHeaderLabel(importSidebarOnLeft, title));
        }

        // Same clamp-and-localScale technique GalleryPanel.Tabs.cs uses to keep text legible
        // at low scales (Unity Text.fontSize is int and visually clamps below ~10).
        public static void ApplyScaledFont(Text txt, int baseFont, float s)
        {
            GalleryUiMetrics.ApplyFont(txt, baseFont, s, ImportSidebarBaseFontSizeMin);
        }

        /// <summary>Rounded row/button fill — matches gallery <see cref="RoundedRect"/> chrome.</summary>
        private static Image AddImportSidebarRoundedBg(GameObject go, Color color, bool raycastTarget = true)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = color;
            rr.raycastTarget = raycastTarget;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            return rr;
        }

        private Transform ResolveImportSidebarParent()
        {
            return backgroundBoxGO != null ? backgroundBoxGO.transform : null;
        }

        private int ResolveImportSidebarSiblingIndex(Transform parent)
        {
            return Mathf.Max(0, parent.childCount - 1);
        }

        // One scroll for the whole body (between the pinned header and pinned Apply): all rows live in its VLG content
        // and scroll as a unit when the panel is short, instead of fixed bands that clip. Insets set in ApplyImportSidebarBaseRect.
        private void BuildImportSidebarBodyScroll()
        {
            GameObject scroll = UI.CreateVScrollableContent(
                importSidebarRoot, new Color(0f, 0f, 0f, 0f), AnchorPresets.stretchAll,
                0f, 0f, Vector2.zero, scrollBarWidth: ImportSidebarScrollBarWidthRef,
                spacing: ImportSidebarBaseRowSpacing, addBottomFlexSpacer: false);
            importSidebarBodyScrollRT = scroll.GetComponent<RectTransform>();
            importSidebarScrollContentRT = scroll.GetComponent<ScrollRect>().content.GetComponent<RectTransform>();
        }

        private void BuildImportSidebarPinnedApply()
        {
            BuildImportSidebarApplyButton(importSidebarRoot.transform);
            importSidebarApplyRT = importSidebarApplyButton != null
                ? importSidebarApplyButton.GetComponent<RectTransform>() : null;
        }

        // Force the scroll content's VLG + ContentSizeFitter to recompute (size changes after scale, type swap, or row
        // count change don't settle on their own reliably for nested layout groups).
        private void RebuildImportSidebarContent()
        {
            if (importSidebarScrollContentRT != null)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(importSidebarScrollContentRT);
            try { SyncImportSidebarHoverChrome(); } catch { }
        }

        private Text CreateImportSidebarLabel(Transform parent, string text, int fontSize)
        {
            Text t = UI.CreateLabel(parent.gameObject, text, fontSize, UI.TextPrimary, TextAnchor.MiddleLeft, raycastTarget: false, name: "Label");
            RectTransform rt = t.GetComponent<RectTransform>();

            RectTransform rtCaptured = rt;
            Text tCaptured = t;
            int fontCaptured = fontSize;
            innerPaneScaleActions.Add(s =>
            {
                ApplyImportSidebarLabelInsets(rtCaptured, s);
                ApplyScaledFont(tCaptured, fontCaptured, s);
            });
            ApplyImportSidebarLabelInsets(rt, ChromeScale);
            ApplyScaledFont(t, fontCaptured, ChromeScale);
            return t;
        }

        private static void ApplyImportSidebarLabelInsets(RectTransform rt, float s)
        {
            if (rt == null) return;
            if (s <= 0f) s = 1f;
            rt.offsetMin = new Vector2(GalleryUiDesignTokens.ImportSidebarLabelPadLeftRef * s, 0f);
            rt.offsetMax = new Vector2(-GalleryUiDesignTokens.ImportSidebarLabelPadRightRef * s, 0f);
        }

        // Checklist rows use a fixed height; disable wrap so long atom ids stay on one visible line.
        private static void ConfigureImportSidebarChecklistLabel(Text t)
        {
            if (t == null) return;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // [diag] Dump resolved rects one frame after activation so an empty-body symptom can be
        // attributed to zero-size / off-screen containers vs missing children, without guessing.
        private System.Collections.IEnumerator DiagDumpImportSidebarRects()
        {
            yield return new WaitForEndOfFrame();
            RebuildImportSidebarContent();
            yield return null;  // let the forced rebuild settle before reading rects
            DiagLogRect("root", importSidebarRT);
            DiagLogRect("header", importSidebarHeaderRT);
            DiagLogRect("bodyScroll", importSidebarBodyScrollRT);
            DiagLogRect("scrollContent", importSidebarScrollContentRT);
            DiagLogRect("typeRadio", importSidebarTypeRadioContainer as RectTransform);
            DiagLogRect("optionsHost", importSidebarOptionsPanelHost as RectTransform);
            DiagLogRect("apply", importSidebarApplyRT);
            // The type-radio overflow check: cellSize vs panel width tells if the cellW fix took.
            RectTransform trc = importSidebarTypeRadioContainer as RectTransform;
            GridLayoutGroup g = trc != null ? trc.GetComponent<GridLayoutGroup>() : null;
            if (g != null)
                LogUtil.Log("[VPB import][diag] typeRadio cellSize=(" + g.cellSize.x.ToString("F1") + "x"
                    + g.cellSize.y.ToString("F1") + ") gridWidth=" + (trc != null ? trc.rect.width.ToString("F0") : "?"));
        }

        private void DiagLogRect(string name, RectTransform rt)
        {
            if (rt == null) { LogUtil.Log("[VPB import][diag] rect " + name + " = NULL"); return; }
            Vector3[] c = new Vector3[4];
            rt.GetWorldCorners(c);
            LogUtil.Log("[VPB import][diag] rect " + name
                + " size=(" + rt.rect.width.ToString("F0") + "x" + rt.rect.height.ToString("F0") + ")"
                + " active=" + rt.gameObject.activeInHierarchy
                + " worldBL=(" + c[0].x.ToString("F0") + "," + c[0].y.ToString("F0") + ")"
                + " worldTR=(" + c[2].x.ToString("F0") + "," + c[2].y.ToString("F0") + ")");
        }

        partial void UpdateImportToggleBtnVisual()
        {
            Color active = ColorSceneImport;
            Color gated = new Color(ColorSceneImport.r, ColorSceneImport.g, ColorSceneImport.b, 0.45f);
            Color idle = ColorSceneImport;
            void Apply(GameObject go, bool highlighted, bool gatedSide)
            {
                if (go == null) return;
                Image img = go.GetComponent<Image>();
                if (img == null) return;
                if (highlighted) img.color = active;
                else if (gatedSide) img.color = gated;
                else img.color = idle;
            }
            bool categoryGated = importSidebarOpenIntent && !ImportSidebarCategoryAllowed();
            bool dockedOpen = ImportSidebarOccupiesSideColumn;
            bool floatOpen = importSidebarActive && importSidebarDetached;
            Apply(leftSceneImportSideBtn, (dockedOpen && importSidebarOnLeft) || floatOpen, categoryGated && importSidebarOnLeft);
            Apply(rightSceneImportSideBtn, (dockedOpen && !importSidebarOnLeft) || floatOpen, categoryGated && !importSidebarOnLeft);

            void ApplyTooltip(GameObject go, bool gatedSide)
            {
                if (go == null) return;
                if (gatedSide)
                    AddTooltip(go, "gallery.import.sidebar_gated_tip",
                        "Import needs Scenes — click to switch category and restore");
                else if (importSidebarDetached && importSidebarOpenIntent)
                    AddTooltip(go, "gallery.import.tip.float_toggle", "Scene Import floating — click to close; Alt+I toggles float");
                else
                    AddTooltip(go, "gallery.tooltip.scene_import", "Open the Import sidebar for the selected scene");
            }
            ApplyTooltip(leftSceneImportSideBtn, categoryGated && importSidebarOnLeft);
            ApplyTooltip(rightSceneImportSideBtn, categoryGated && !importSidebarOnLeft);
        }

    }
}
