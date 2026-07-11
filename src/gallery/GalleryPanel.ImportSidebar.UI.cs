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
                Image bg = importSidebarRoot.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0f);
                bg.raycastTarget = false;

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
            float w = ImportSidebarBaseWidth * s;
            float leftMargin = SideTabColumnLeftInsetX(s);
            float rightMargin = -SideTabColumnRightInsetX(s);
            float top = ImportSidebarTopOffsetY(s);          // negative: inset down from panel top
            // Clear the full footer chrome (the tab-column container uses 68, but its visible content stops at this
            // larger inset; the sidebar's Apply button is visible at the bottom, so it must clear the whole footer).
            float bottom = SideTabScrollBottomInsetY();

            importSidebarRT.pivot = new Vector2(0.5f, 0.5f);
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
            ApplyImportSidebarChromeHorizontalInsets(s, out float insetLeft, out float insetRight);

            if (importSidebarHeaderRT != null)
            {
                importSidebarHeaderRT.anchorMin = new Vector2(0f, 1f);
                importSidebarHeaderRT.anchorMax = new Vector2(1f, 1f);
                importSidebarHeaderRT.pivot = new Vector2(0.5f, 1f);
                importSidebarHeaderRT.anchoredPosition = Vector2.zero;
                importSidebarHeaderRT.offsetMin = new Vector2(insetLeft, -headerH);
                importSidebarHeaderRT.offsetMax = new Vector2(-insetRight, 0f);
            }

            if (importSidebarApplyRT != null)
            {
                importSidebarApplyRT.offsetMin = new Vector2(insetLeft, 0f);
                importSidebarApplyRT.offsetMax = new Vector2(-insetRight, applyH);
            }

            if (importSidebarBodyScrollRT != null)
            {
                // Full sidebar width — internal viewport reserves scrollbar gutter (matches header/apply content column).
                importSidebarBodyScrollRT.offsetMin = new Vector2(0f, applyH);
                importSidebarBodyScrollRT.offsetMax = new Vector2(0f, -(headerH + headerGap));
            }

            try { AlignImportSidebarScrollViewport(s); } catch { }
            try { SyncImportSidebarScrollContentLayout(s); } catch { }
            try { SyncImportSidebarTypeRadioGridWidth(s); } catch { }
            try { SyncImportSidebarScrollHoverBorders(); } catch { }
            try { StyleImportSidebarHeader(s); } catch { }
            EnsureImportSidebarHeaderClickable();
            SyncImportSidebarHeaderLabel();
            SyncImportSidebarHeaderTypography(s);
            SyncImportSidebarHeaderGateVisual();
            try { RefreshImportSidebarWizardHeader(); } catch { }
        }

        private static float ImportSidebarScrollBarWidthPx(float s) => ImportSidebarScrollBarWidthRef * s;

        /// <summary>Horizontal insets inside the 220px column: flush on panel-outer edge; pad only before scrollbar.</summary>
        private void GetImportSidebarContentWidthInsets(float s, out float insetLeft, out float insetRight, out float contentWidth)
        {
            float scrollW = ImportSidebarScrollBarWidthPx(s);
            float padInner = ImportSidebarInnerPadHRef * s;
            insetLeft = 0f;
            insetRight = scrollW + padInner;
            contentWidth = ImportSidebarBaseWidth * s - insetRight;
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
            bool gated = importSidebarOpenIntent && !ImportSidebarCategoryAllowed();
            Image bg = importSidebarHeaderRT.GetComponent<Image>();
            if (bg != null)
                bg.color = gated ? new Color(ColorCategory.r * 0.55f, ColorCategory.g * 0.55f, ColorCategory.b * 0.55f, 0.75f) : ColorCategory;
            if (importSidebarHeaderLabel != null)
                importSidebarHeaderLabel.color = gated ? new Color(0.82f, 0.82f, 0.82f, 0.9f) : Color.white;
            if (importSidebarHeaderBtn != null)
                importSidebarHeaderBtn.interactable = !gated;
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
            float inner = contentWidth - 4f * s;
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

        private void BuildImportSidebarHeader()
        {
            GameObject header = new GameObject("Header");
            header.transform.SetParent(importSidebarRoot.transform, false);
            importSidebarHeaderRT = header.AddComponent<RectTransform>();
            importSidebarHeaderRoot = importSidebarHeaderRT;
            importSidebarHeaderRT.anchorMin = new Vector2(0f, 1f);
            importSidebarHeaderRT.anchorMax = new Vector2(1f, 1f);
            importSidebarHeaderRT.pivot = new Vector2(0.5f, 1f);
            importSidebarHeaderRT.sizeDelta = new Vector2(0f, ImportSidebarBaseHeaderHeight);
            importSidebarHeaderRT.anchoredPosition = Vector2.zero;

            // Header tone matches the selected-Category row color so users recognize it as
            // a side-column header rather than a generic popup chrome.
            Image bg = AddImportSidebarRoundedBg(header, ImportSidebarHeaderBg);

            importSidebarHeaderLabel = CreateImportSidebarLabel(
                header.transform,
                FormatSidePanelHeaderLabel(importSidebarOnLeft, SidePanelHeaderTranslation("gallery.import.sidebar_header", "Import")),
                SidePanelHeaderFontRef);
            importSidebarHeaderLabel.color = Color.white;
            importSidebarHeaderLabel.fontStyle = FontStyle.Normal;
            importSidebarHeaderLabel.alignment = TextAnchor.MiddleCenter;
            importSidebarHeaderLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            importSidebarHeaderLabel.verticalOverflow = VerticalWrapMode.Truncate;
            StyleImportSidebarHeader();

            importSidebarHeaderBtn = header.AddComponent<Button>();
            importSidebarHeaderBtn.targetGraphic = bg;
            importSidebarHeaderBtn.onClick.AddListener(() => ToggleImportSidebar());
            AddTooltip(header, "gallery.side.collapse_tip", "Collapse side list");
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
            try { SyncImportSidebarScrollHoverBorders(); } catch { }
        }

        private Text CreateImportSidebarLabel(Transform parent, string text, int fontSize)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;

            Text t = go.AddComponent<Text>();
            t.text = text;
            t.color = UI.TextPrimary;
            t.fontSize = fontSize;
            t.alignment = TextAnchor.MiddleLeft;
            VPBUiFont.ApplyTo(t);
            t.raycastTarget = false;

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
            Color active = new Color(0.2f, 0.45f, 0.75f, 0.9f);
            Color gated = new Color(0.22f, 0.34f, 0.5f, 0.55f);
            Color idle = UI.IconButtonBackdrop;
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
            Apply(leftSceneImportSideBtn, importSidebarActive && importSidebarOnLeft, categoryGated && importSidebarOnLeft);
            Apply(rightSceneImportSideBtn, importSidebarActive && !importSidebarOnLeft, categoryGated && !importSidebarOnLeft);

            void ApplyTooltip(GameObject go, bool gatedSide)
            {
                if (go == null) return;
                if (gatedSide)
                    AddTooltip(go, "gallery.import.sidebar_gated_tip", "Import sidebar opens in Scenes category only");
                else
                    AddTooltip(go, "gallery.tooltip.scene_import", "Open the Import sidebar for the selected scene");
            }
            ApplyTooltip(leftSceneImportSideBtn, categoryGated && importSidebarOnLeft);
            ApplyTooltip(rightSceneImportSideBtn, categoryGated && !importSidebarOnLeft);
        }

    }
}
