using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        // Selection toolbox ("tbox")
        private GameObject tbox;
        private Text       tboxLabel;
        private Text       tboxHintLabel;
        private GameObject tboxCopyPkgNamesBtn;
        private GameObject tboxDeleteBtn;
        private GameObject tboxAutoInstallBtn;
        private GameObject tboxDisableAutoInstallBtn;
        private GameObject tboxHideBtn;
        private GameObject tboxUnhideBtn;

        // Info box ("ibox") — shows details for a single selected item
        private GameObject ibox;
        private RectTransform iboxRT;

        private bool  iboxIsHovered = false;
        private bool  iboxPinned    = false;
        private float iboxExpandT   = 0f; // 0 = collapsed, 1 = expanded

        private CanvasGroup iboxCollapsedCG;
        private CanvasGroup iboxExpandedCG;

        private GameObject  iboxPinBtn;
        private Text        iboxPinBtnText;

        private Text        iboxHeaderText; // always visible (expanded/collapsed)

        private Text        iboxCollapsedLabel;
        private RawImage    iboxPreviewImage;
        private Text        iboxTitleText;
        private Text        iboxCreatorText;
        private Text        iboxStarsText;
        private Text        iboxDepsText;
        private Text        iboxMissingText;
        private Text        iboxDependentsText;
        private Text        iboxSizeText;
        private Text        iboxDateText;

        // Expand/collapse state
        private bool  tboxIsHovered  = false;
        private bool  tboxPinned     = false;
        private float tboxExpandT    = 0f;        // 0 = collapsed, 1 = expanded

        private RectTransform tboxRT;
        private CanvasGroup   tboxLabelCG;        // fades OUT when expanding
        private CanvasGroup   tboxButtonsCG;      // fades IN when expanding
        private GameObject    tboxPinBtn;
        private Text          tboxPinBtnText;

        private const float TboxCollapsedH = 38f;  // height when showing "X Selected" + hover hint (one row)
        private const float TboxExpandedH  = 56f;  // height when showing action buttons (4 action buttons)
        private const float TboxBottomY    = 120f; // sits above the hover-path bar

        private const float IboxCollapsedH = 34f;
        private const float IboxExpandedH  = 162f;
        private const float IboxTopOffsetY = -70f; // sits just below 70px title bar

        // ─────────────────────────────────────────────────────────────────────────

        private void EnsureTboxUI()
        {
            if (tbox != null) return;
            if (backgroundBoxGO == null) return;

            // ── Bar (full-width, anchored at bottom) ──────────────────────────────
            tbox = UI.AddChildGOImage(
                backgroundBoxGO,
                new Color(0f, 0f, 0f, 0.85f),
                AnchorPresets.hStretchBottom,
                0,
                TboxCollapsedH,
                new Vector2(0f, TboxBottomY)
            );
            tbox.name = "SelectionToolbox";
            tboxRT = tbox.GetComponent<RectTransform>();

            var img = tbox.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var hoverDel = tbox.AddComponent<UIHoverDelegate>();
            hoverDel.OnHoverChange = h => tboxIsHovered = h;

            // ── "X Selected" + hover hint, one row (collapsed view) ─────────────
            var labelGO = new GameObject("TboxLabelLayer");
            labelGO.transform.SetParent(tbox.transform, false);
            tboxLabelCG = labelGO.AddComponent<CanvasGroup>();

            // RectTransform — fill bar, leave 48 px on right for pin button
            var labelLayerRT = labelGO.GetComponent<RectTransform>();
            if (labelLayerRT == null) labelLayerRT = labelGO.AddComponent<RectTransform>();
            labelLayerRT.anchorMin = Vector2.zero;
            labelLayerRT.anchorMax = Vector2.one;
            labelLayerRT.offsetMin = new Vector2(0f,   0f);
            labelLayerRT.offsetMax = new Vector2(-48f, 0f);

            var rowGO = new GameObject("TboxLabelRow");
            rowGO.transform.SetParent(labelGO.transform, false);
            var rowRT = rowGO.AddComponent<RectTransform>();
            rowRT.anchorMin = Vector2.zero;
            rowRT.anchorMax = Vector2.one;
            rowRT.offsetMin = Vector2.zero;
            rowRT.offsetMax = Vector2.zero;

            var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowHLG.childAlignment      = TextAnchor.MiddleCenter;
            rowHLG.spacing             = 12f;
            rowHLG.childForceExpandWidth  = false;
            rowHLG.childForceExpandHeight = true;
            rowHLG.childControlWidth   = true;
            rowHLG.childControlHeight  = true;
            rowHLG.padding             = new RectOffset(8, 8, 0, 0);

            const int tboxCollapsedFont = 18;
            var labelColor = new Color(0.92f, 0.92f, 0.92f, 1f);

            var labelTextGO = new GameObject("Text");
            labelTextGO.transform.SetParent(rowGO.transform, false);
            tboxLabel = labelTextGO.AddComponent<Text>();
            tboxLabel.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxLabel.fontSize  = tboxCollapsedFont;
            tboxLabel.fontStyle = FontStyle.Bold;
            tboxLabel.color     = labelColor;
            tboxLabel.alignment = TextAnchor.MiddleCenter;
            tboxLabel.raycastTarget = false;
            var labelShadow = labelTextGO.AddComponent<Shadow>();
            labelShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
            labelShadow.effectDistance = new Vector2(1f, -1f);
            var labelCSF = labelTextGO.AddComponent<ContentSizeFitter>();
            labelCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            labelCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var hintTextGO = new GameObject("HoverHint");
            hintTextGO.transform.SetParent(rowGO.transform, false);
            tboxHintLabel = hintTextGO.AddComponent<Text>();
            tboxHintLabel.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxHintLabel.fontSize  = tboxCollapsedFont;
            tboxHintLabel.fontStyle = FontStyle.Normal;
            tboxHintLabel.color     = labelColor;
            tboxHintLabel.alignment = TextAnchor.MiddleCenter;
            tboxHintLabel.raycastTarget = false;
            tboxHintLabel.text      = VPBTranslation.T("gallery.tbox.hover_expand", "Hover to expand");
            var hintShadow = hintTextGO.AddComponent<Shadow>();
            hintShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
            hintShadow.effectDistance = new Vector2(1f, -1f);
            var hintCSF = hintTextGO.AddComponent<ContentSizeFitter>();
            hintCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            hintCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── Buttons panel (expanded view) ─────────────────────────────────────
            var bpGO = new GameObject("TboxButtonsLayer");
            bpGO.transform.SetParent(tbox.transform, false);
            tboxButtonsCG = bpGO.AddComponent<CanvasGroup>();
            tboxButtonsCG.alpha          = 0f;
            tboxButtonsCG.blocksRaycasts = false;
            tboxButtonsCG.interactable   = false;

            var bpRT = bpGO.GetComponent<RectTransform>();
            if (bpRT == null) bpRT = bpGO.AddComponent<RectTransform>();
            bpRT.anchorMin = Vector2.zero;
            bpRT.anchorMax = Vector2.one;
            bpRT.offsetMin = new Vector2(0f,   0f);
            bpRT.offsetMax = new Vector2(-48f, 0f); // same right inset as label layer

            const int tboxActionBtnFont = 16;
            tboxCopyPkgNamesBtn = UI.CreateUIButton(
                bpGO, 210, 42,
                VPBTranslation.T("gallery.tbox.copy_names", "Copy Names"), tboxActionBtnFont,
                -12, 0, AnchorPresets.middleRight,
                CopySelectedPackageNamesToClipboard
            );
            tboxCopyPkgNamesBtn.name = "Tbox_CopyPackageNames";
            AddTooltip(tboxCopyPkgNamesBtn, "gallery.tooltip.tbox_copy_names", "Copy package .var names and local Saves/scene paths (one per line) to clipboard");

            tboxDeleteBtn = UI.CreateUIButton(
                bpGO, 180, 42,
                VPBTranslation.T("gallery.tbox.delete", "Delete"), tboxActionBtnFont,
                -12 - 220, 0, AnchorPresets.middleRight,
                TboxDeleteSelectedPackages
            );
            tboxDeleteBtn.name = "Tbox_Delete";
            AddTooltip(tboxDeleteBtn, "gallery.tooltip.tbox_delete", "Move selected packages to DeletedPackages; local Saves/scene JSON (+ preview) to DeletedScenes");
            try
            {
                var delImg = tboxDeleteBtn.GetComponent<Image>();
                if (delImg != null) delImg.color = new Color(0.35f, 0.15f, 0.15f, 1f);
            }
            catch { }

            tboxAutoInstallBtn = UI.CreateUIButton(
                bpGO, 168, 42,
                VPBTranslation.T("gallery.tbox.autoinstall", "Autoinstall"), tboxActionBtnFont,
                -12 - 220 - 190, 0, AnchorPresets.middleRight,
                TboxAutoInstallSelectedPackages
            );
            tboxAutoInstallBtn.name = "Tbox_AutoInstall";
            AddTooltip(tboxAutoInstallBtn, "gallery.tooltip.tbox_autoinstall", "Flag selected packages for auto-install and auto-load. Packages in AllPackages are copied to AddonPackages on the next VaM start (not immediately).");

            tboxHideBtn = UI.CreateUIButton(
                bpGO, 100, 42,
                VPBTranslation.T("gallery.tbox.hide", "Hide"), tboxActionBtnFont,
                -12 - 220 - 190 - 178, 0, AnchorPresets.middleRight,
                TboxHideSelectedPackages
            );
            tboxHideBtn.name = "Tbox_Hide";
            AddTooltip(tboxHideBtn, "gallery.tooltip.tbox_hide", "Hide selected packages in VaM file lists (AddonPackagesFilePrefs … .hide)");

            const float tboxHideX = -12f - 220f - 190f - 178f;
            tboxUnhideBtn = UI.CreateUIButton(
                bpGO, 100, 42,
                VPBTranslation.T("gallery.tbox.unhide", "Unhide"), tboxActionBtnFont,
                tboxHideX - 10f - 100f, 0, AnchorPresets.middleRight,
                TboxUnhideSelectedPackages
            );
            tboxUnhideBtn.name = "Tbox_Unhide";
            tboxUnhideBtn.SetActive(false);
            AddTooltip(tboxUnhideBtn, "gallery.tooltip.tbox_unhide", "Remove .hide markers for selected packages");

            tboxDisableAutoInstallBtn = UI.CreateUIButton(
                bpGO, 168, 42,
                VPBTranslation.T("gallery.tbox.no_autoinstall", "No autoinstall"), tboxActionBtnFont,
                tboxHideX - 10f - 100f - 10f - 168f, 0, AnchorPresets.middleRight,
                TboxDisableAutoInstallSelectedPackages
            );
            tboxDisableAutoInstallBtn.name = "Tbox_NoAutoInstall";
            tboxDisableAutoInstallBtn.SetActive(false);
            AddTooltip(tboxDisableAutoInstallBtn, "gallery.tooltip.tbox_no_autoinstall", "Clear auto-install and VPB auto-load for selected packages");

            // ── Pin toggle (right edge, always visible) ───────────────────────────
            tboxPinBtn = UI.CreateUIButton(
                tbox, 44, 0, "", 15,
                0, 0, AnchorPresets.vStretchRight,
                () =>
                {
                    tboxPinned = !tboxPinned;
                    RefreshTboxPinVisual();
                }
            );
            tboxPinBtn.name = "Tbox_Pin";
            // vStretchRight: right-edge, full height of bar
            var pinRT = tboxPinBtn.GetComponent<RectTransform>();
            pinRT.anchorMin        = new Vector2(1f, 0f);
            pinRT.anchorMax        = new Vector2(1f, 1f);
            pinRT.pivot            = new Vector2(1f, 0.5f);
            pinRT.anchoredPosition = Vector2.zero;
            pinRT.sizeDelta        = new Vector2(44f, 0f);

            tboxPinBtnText = tboxPinBtn.GetComponentInChildren<Text>();

            // Left border line on pin button (visual separator)
            {
                var sep = new GameObject("Separator");
                sep.transform.SetParent(tboxPinBtn.transform, false);
                var sepImg = sep.AddComponent<Image>();
                sepImg.color = new Color(1f, 1f, 1f, 0.08f);
                sepImg.raycastTarget = false;
                var sepRT = sep.GetComponent<RectTransform>();
                sepRT.anchorMin        = new Vector2(0f, 0.15f);
                sepRT.anchorMax        = new Vector2(0f, 0.85f);
                sepRT.pivot            = new Vector2(0f, 0.5f);
                sepRT.anchoredPosition = Vector2.zero;
                sepRT.sizeDelta        = new Vector2(1f, 0f);
            }

            RefreshTboxPinVisual();
            AddTooltip(tboxPinBtn, "gallery.tooltip.tbox_pin", "Pin — keep toolbar expanded");
            AddTooltip(tbox, "gallery.tooltip.tbox_label", "Hover to expand selection toolbar");

            tbox.SetActive(false);
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void EnsureIboxUI()
        {
            if (ibox != null) return;
            if (backgroundBoxGO == null) return;

            // Bar: full-width, anchored at top, just below the title bar (expands downward)
            ibox = UI.AddChildGOImage(
                backgroundBoxGO,
                new Color(0f, 0f, 0f, 0.90f),
                AnchorPresets.hStretchTop,
                0,
                IboxCollapsedH,
                new Vector2(0f, IboxTopOffsetY)
            );
            ibox.name = "InfoBox";
            iboxRT = ibox.GetComponent<RectTransform>();
            if (iboxRT != null) iboxRT.pivot = new Vector2(0.5f, 1f);

            var img = ibox.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            var hoverDel = ibox.AddComponent<UIHoverDelegate>();
            hoverDel.OnHoverChange = h => iboxIsHovered = h;

            // Always-visible header (so name stays visible while expanded/collapsed layers cross-fade)
            {
                var headerGO = new GameObject("IboxHeader");
                headerGO.transform.SetParent(ibox.transform, false);
                var headerRT = headerGO.AddComponent<RectTransform>();
                headerRT.anchorMin = new Vector2(0f, 1f);
                headerRT.anchorMax = new Vector2(1f, 1f);
                headerRT.pivot = new Vector2(0.5f, 1f);
                headerRT.anchoredPosition = new Vector2(0f, 0f);
                headerRT.sizeDelta = new Vector2(-48f, 26f); // leave room for pin

                iboxHeaderText = headerGO.AddComponent<Text>();
                iboxHeaderText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                iboxHeaderText.fontSize = 20;
                iboxHeaderText.fontStyle = FontStyle.Bold;
                iboxHeaderText.color = Color.white;
                iboxHeaderText.alignment = TextAnchor.UpperLeft;
                iboxHeaderText.horizontalOverflow = HorizontalWrapMode.Overflow;
                iboxHeaderText.verticalOverflow = VerticalWrapMode.Truncate;
                iboxHeaderText.raycastTarget = false;

                var sh = headerGO.AddComponent<Shadow>();
                sh.effectColor = new Color(0f, 0f, 0f, 0.75f);
                sh.effectDistance = new Vector2(1f, -1f);
            }

            // Collapsed layer
            var collapsedGO = new GameObject("IboxCollapsedLayer");
            collapsedGO.transform.SetParent(ibox.transform, false);
            iboxCollapsedCG = collapsedGO.AddComponent<CanvasGroup>();

            var collapsedRT = collapsedGO.GetComponent<RectTransform>();
            if (collapsedRT == null) collapsedRT = collapsedGO.AddComponent<RectTransform>();
            collapsedRT.anchorMin = Vector2.zero;
            collapsedRT.anchorMax = Vector2.one;
            // Leave space for the always-visible header at the top
            collapsedRT.offsetMin = new Vector2(0f, 0f);
            collapsedRT.offsetMax = new Vector2(-48f, -26f); // right inset for pin button + top header

            var collapsedTextGO = new GameObject("Text");
            collapsedTextGO.transform.SetParent(collapsedGO.transform, false);
            iboxCollapsedLabel = collapsedTextGO.AddComponent<Text>();
            iboxCollapsedLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            iboxCollapsedLabel.fontSize = 20;
            iboxCollapsedLabel.fontStyle = FontStyle.Bold;
            iboxCollapsedLabel.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            iboxCollapsedLabel.alignment = TextAnchor.MiddleLeft;
            iboxCollapsedLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            iboxCollapsedLabel.verticalOverflow = VerticalWrapMode.Truncate;
            iboxCollapsedLabel.raycastTarget = false;
            var cShadow = collapsedTextGO.AddComponent<Shadow>();
            cShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            cShadow.effectDistance = new Vector2(1f, -1f);
            var collapsedTextRT = collapsedTextGO.GetComponent<RectTransform>();
            collapsedTextRT.anchorMin = Vector2.zero;
            collapsedTextRT.anchorMax = Vector2.one;
            collapsedTextRT.offsetMin = new Vector2(12f, 0f);
            collapsedTextRT.offsetMax = new Vector2(-12f, 0f);

            // Expanded layer
            var expandedGO = new GameObject("IboxExpandedLayer");
            expandedGO.transform.SetParent(ibox.transform, false);
            iboxExpandedCG = expandedGO.AddComponent<CanvasGroup>();
            iboxExpandedCG.alpha = 0f;
            iboxExpandedCG.blocksRaycasts = false;
            iboxExpandedCG.interactable = false;

            var expandedRT = expandedGO.GetComponent<RectTransform>();
            if (expandedRT == null) expandedRT = expandedGO.AddComponent<RectTransform>();
            expandedRT.anchorMin = Vector2.zero;
            expandedRT.anchorMax = Vector2.one;
            // Leave space for the always-visible header at the top
            expandedRT.offsetMin = new Vector2(0f, 0f);
            expandedRT.offsetMax = new Vector2(-48f, -26f); // right inset for pin + top header

            // Preview image (left)
            var previewGO = new GameObject("Preview");
            previewGO.transform.SetParent(expandedGO.transform, false);
            var previewBg = previewGO.AddComponent<Image>();
            previewBg.color = new Color(1f, 1f, 1f, 0.06f);
            previewBg.raycastTarget = false;
            var previewRT = previewGO.GetComponent<RectTransform>();
            previewRT.anchorMin = new Vector2(0f, 0.5f);
            previewRT.anchorMax = new Vector2(0f, 0.5f);
            previewRT.pivot = new Vector2(0f, 0.5f);
            previewRT.anchoredPosition = new Vector2(10f, -6f);
            previewRT.sizeDelta = new Vector2(112f, 112f);

            var rawGO = new GameObject("Image");
            rawGO.transform.SetParent(previewGO.transform, false);
            iboxPreviewImage = rawGO.AddComponent<RawImage>();
            iboxPreviewImage.color = new Color(1f, 1f, 1f, 0f);
            iboxPreviewImage.raycastTarget = false;
            var rawRT = rawGO.GetComponent<RectTransform>();
            rawRT.anchorMin = Vector2.zero;
            rawRT.anchorMax = Vector2.one;
            rawRT.offsetMin = new Vector2(6f, 6f);
            rawRT.offsetMax = new Vector2(-6f, -6f);
            var arf = rawGO.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.aspectRatio = 1f;

            // Info panel (right)
            var infoGO = new GameObject("Info");
            infoGO.transform.SetParent(expandedGO.transform, false);
            var infoRT = infoGO.AddComponent<RectTransform>();
            infoRT.anchorMin = new Vector2(0f, 0f);
            infoRT.anchorMax = new Vector2(1f, 1f);
            infoRT.offsetMin = new Vector2(132f, 8f);
            infoRT.offsetMax = new Vector2(-10f, -8f);

            var vlg = infoGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.spacing = 4f;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var titleRow = new GameObject("TitleRow");
            titleRow.transform.SetParent(infoGO.transform, false);
            var titleLE = titleRow.AddComponent<LayoutElement>();
            // Title is shown in the always-visible header; keep this row at 0 height.
            titleLE.minHeight = 0f;
            titleLE.preferredHeight = 0f;

            iboxTitleText = titleRow.AddComponent<Text>();
            iboxTitleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            iboxTitleText.fontSize = 26;
            iboxTitleText.fontStyle = FontStyle.Bold;
            iboxTitleText.color = Color.white;
            iboxTitleText.alignment = TextAnchor.MiddleLeft;
            iboxTitleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            iboxTitleText.verticalOverflow = VerticalWrapMode.Truncate;
            iboxTitleText.raycastTarget = false;

            // Info grid (expanded view)
            var gridGO = new GameObject("Grid");
            gridGO.transform.SetParent(infoGO.transform, false);
            var gridLE = gridGO.AddComponent<LayoutElement>();
            gridLE.minHeight = 124f;
            gridLE.preferredHeight = 124f;

            var grid = gridGO.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(0, 0, 0, 0);
            grid.spacing = new Vector2(10f, 8f);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.cellSize = new Vector2(170f, 56f);

            Text MakeCell(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(gridGO.transform, false);
                var t = go.AddComponent<Text>();
                t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                t.fontSize = 22;
                t.color = new Color(0.92f, 0.92f, 0.92f, 1f);
                t.alignment = TextAnchor.MiddleLeft;
                t.lineSpacing = 1.0f;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.raycastTarget = false;
                return t;
            }

            // 4x2 grid (8 cells) — keep stable ordering for quick scanning
            iboxCreatorText    = MakeCell("Creator");
            iboxStarsText      = MakeCell("Stars");
            iboxSizeText       = MakeCell("Size");
            iboxDateText       = MakeCell("Date");

            iboxDepsText       = MakeCell("Deps");
            iboxMissingText    = MakeCell("Missing");
            iboxDependentsText = MakeCell("Dependents");
            {
                // filler cell so grid stays 4x2; keeps spacing consistent even if we add later
                var filler = MakeCell("Filler");
                filler.text = "";
                filler.color = new Color(1f, 1f, 1f, 0f);
            }

            // Pin toggle (right edge, always visible)
            iboxPinBtn = UI.CreateUIButton(
                ibox, 44, 0, "", 14,
                0, 0, AnchorPresets.vStretchRight,
                () =>
                {
                    iboxPinned = !iboxPinned;
                    RefreshIboxPinVisual();
                }
            );
            iboxPinBtn.name = "Ibox_Pin";
            var pinRT = iboxPinBtn.GetComponent<RectTransform>();
            pinRT.anchorMin = new Vector2(1f, 0f);
            pinRT.anchorMax = new Vector2(1f, 1f);
            pinRT.pivot = new Vector2(1f, 0.5f);
            pinRT.anchoredPosition = Vector2.zero;
            pinRT.sizeDelta = new Vector2(44f, 0f);

            iboxPinBtnText = iboxPinBtn.GetComponentInChildren<Text>();

            // Separator line
            {
                var sep = new GameObject("Separator");
                sep.transform.SetParent(iboxPinBtn.transform, false);
                var sepImg = sep.AddComponent<Image>();
                sepImg.color = new Color(1f, 1f, 1f, 0.08f);
                sepImg.raycastTarget = false;
                var sepRT = sep.GetComponent<RectTransform>();
                sepRT.anchorMin = new Vector2(0f, 0.15f);
                sepRT.anchorMax = new Vector2(0f, 0.85f);
                sepRT.pivot = new Vector2(0f, 0.5f);
                sepRT.anchoredPosition = Vector2.zero;
                sepRT.sizeDelta = new Vector2(1f, 0f);
            }

            RefreshIboxPinVisual();
            AddTooltip(iboxPinBtn, "gallery.tooltip.ibox_pin", "Pin — keep info box expanded");
            AddTooltip(ibox, "gallery.tooltip.ibox_label", "Hover to expand info box");

            ibox.SetActive(false);
        }

        private void RefreshIboxPinVisual()
        {
            if (iboxPinBtnText == null) return;
            if (iboxPinned)
            {
                iboxPinBtnText.text = "●";
                iboxPinBtnText.color = new Color(0.45f, 0.75f, 0.90f, 1f);
                var pinImg = iboxPinBtn != null ? iboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.10f, 0.22f, 0.30f, 1f);
            }
            else
            {
                iboxPinBtnText.text = "○";
                iboxPinBtnText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                var pinImg = iboxPinBtn != null ? iboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.20f, 0.20f, 0.20f, 1f);
            }
        }

        private static string FormatBytes(long bytes)
        {
            try
            {
                if (bytes < 0) return "?";
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double b = bytes;
                int u = 0;
                while (b >= 1024.0 && u < units.Length - 1)
                {
                    b /= 1024.0;
                    u++;
                }
                if (u == 0) return ((long)b).ToString() + " " + units[u];
                return b.ToString(b >= 10.0 ? "0.0" : "0.00") + " " + units[u];
            }
            catch { return "?"; }
        }

        private static string SafeDate(DateTime dt)
        {
            try
            {
                if (dt == DateTime.MinValue) return "-";
                return dt.ToString("yyyy-MM-dd HH:mm");
            }
            catch { return "-"; }
        }

        private VarPackage TryGetPackageForEntry(FileEntry f)
        {
            if (f == null) return null;
            try
            {
                if (f is VarFileEntry vfe && vfe.Package != null) return vfe.Package;
                if (f is PackageListEntry ple && ple.Package != null) return ple.Package;
            }
            catch { }

            try
            {
                string uid = TryGetPackageUidForEntry(f);
                if (!string.IsNullOrEmpty(uid))
                {
                    return FileManager.GetPackageForDependency(uid, false);
                }
            }
            catch { }
            return null;
        }

        private void UpdateIboxUI()
        {
            if (canvas == null) return;
            EnsureIboxUI();
            if (ibox == null) return;

            int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
            bool visible = sel > 0;
            if (ibox.activeSelf != visible) ibox.SetActive(visible);

            if (!visible)
            {
                iboxExpandT = 0f;
                iboxIsHovered = false;
                if (iboxPinned) { iboxPinned = false; RefreshIboxPinVisual(); }
                return;
            }

            FileEntry firstEntry = null;
            try { firstEntry = selectedFiles[0]; } catch { firstEntry = null; }

            // Aggregate stats across selection (simple sums, as requested)
            string headerName = null;
            string creator = "-";
            int stars = 0;
            int deps = 0;
            int missing = 0;
            int dependents = 0;
            long size = 0;
            DateTime newestDate = DateTime.MinValue;

            if (sel == 1)
            {
                VarPackage pkg = TryGetPackageForEntry(firstEntry);
                try
                {
                    if (pkg != null)
                    {
                        headerName = pkg.Uid + ".var";
                        creator = string.IsNullOrEmpty(pkg.Creator) ? "-" : pkg.Creator;
                        dependents = Math.Max(0, pkg.DependentCount);
                        size = pkg.Size;
                        newestDate = pkg.LastWriteTime;

                        try
                        {
                            var direct = pkg.PackageDependencies;
                            if (direct != null) deps = direct.Count;
                            else if (pkg.RecursivePackageDependencies != null) deps = pkg.RecursivePackageDependencies.Count;
                        }
                        catch { deps = 0; }

                        try
                        {
                            if (pkg.MissingDepsCount >= 0) missing = pkg.MissingDepsCount;
                            else missing = DependencyGraph.GetMissingCount(pkg.Uid);
                        }
                        catch { missing = 0; }

                        try
                        {
                            stars = RatingsManager.Instance != null ? RatingsManager.Instance.GetRating(new PackageListEntry(pkg)) : 0;
                        }
                        catch { stars = 0; }
                    }
                    else if (firstEntry != null)
                    {
                        headerName = !string.IsNullOrEmpty(firstEntry.Name) ? firstEntry.Name : (firstEntry.Path ?? "Selected");
                        size = firstEntry.Size;
                        newestDate = firstEntry.LastWriteTime;
                        stars = RatingsManager.Instance != null ? RatingsManager.Instance.GetRating(firstEntry) : 0;
                    }
                }
                catch { }
            }
            else
            {
                headerName = sel.ToString() + " Selected";
                creator = "Mixed";
                stars = 0;
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var e = selectedFiles[i];
                    if (e == null) continue;
                    VarPackage pkg = null;
                    try { pkg = TryGetPackageForEntry(e); } catch { pkg = null; }

                    if (pkg != null)
                    {
                        try
                        {
                            var direct = pkg.PackageDependencies;
                            if (direct != null) deps += Math.Max(0, direct.Count);
                            else if (pkg.RecursivePackageDependencies != null) deps += Math.Max(0, pkg.RecursivePackageDependencies.Count);
                        }
                        catch { }

                        try
                        {
                            int m = (pkg.MissingDepsCount >= 0) ? pkg.MissingDepsCount : DependencyGraph.GetMissingCount(pkg.Uid);
                            missing += Math.Max(0, m);
                        }
                        catch { }

                        try { dependents += Math.Max(0, pkg.DependentCount); } catch { }
                        try { size += Math.Max(0, pkg.Size); } catch { }
                        try { if (pkg.LastWriteTime > newestDate) newestDate = pkg.LastWriteTime; } catch { }
                    }
                    else
                    {
                        try { size += Math.Max(0, e.Size); } catch { }
                        try { if (e.LastWriteTime > newestDate) newestDate = e.LastWriteTime; } catch { }
                    }
                }
            }

            if (iboxHeaderText != null) iboxHeaderText.text = headerName ?? "Selection";

            if (iboxCollapsedLabel != null)
            {
                string missTxt = "  Missing: " + missing.ToString();
                iboxCollapsedLabel.text = "Missing:" + missing.ToString();
            }

            if (iboxTitleText != null) iboxTitleText.text = "";
            // Expanded grid cells (single-line label/value)
            if (iboxCreatorText != null) iboxCreatorText.text = "Creator: " + (creator ?? "-");
            if (iboxStarsText != null) iboxStarsText.text = "★: " + (stars > 0 ? stars.ToString() : "-");
            if (iboxSizeText != null) iboxSizeText.text = "Sz: " + (size > 0 ? FormatBytes(size) : "-");
            if (iboxDateText != null) iboxDateText.text = "Date: " + SafeDate(newestDate);

            if (iboxDepsText != null) iboxDepsText.text = "D: " + deps.ToString();
            if (iboxMissingText != null) iboxMissingText.text = "M: " + missing.ToString();
            if (iboxDependentsText != null) iboxDependentsText.text = "Dn: " + dependents.ToString();

            // Preview: use the selected entry when possible (gives per-item sisters), otherwise fall back to package row
            if (iboxPreviewImage != null)
            {
                try
                {
                    if (sel == 1 && firstEntry != null)
                        LoadThumbnail(firstEntry, iboxPreviewImage, gridThumbnailContext: false);
                    else
                        ClearThumbnailTarget(iboxPreviewImage);
                }
                catch { }
            }

            // Expand/collapse animation (same approach as tbox)
            bool wantExpanded = iboxIsHovered || iboxPinned;
            float targetT = wantExpanded ? 1f : 0f;
            iboxExpandT = Mathf.Lerp(iboxExpandT, targetT, Time.deltaTime * 10f);
            if (Mathf.Abs(iboxExpandT - targetT) < 0.005f) iboxExpandT = targetT;

            if (iboxRT != null)
            {
                float h = Mathf.Lerp(IboxCollapsedH, IboxExpandedH, iboxExpandT);
                iboxRT.sizeDelta = new Vector2(0f, h);
            }

            if (iboxCollapsedCG != null)
                iboxCollapsedCG.alpha = Mathf.Lerp(iboxCollapsedCG.alpha, 1f - iboxExpandT, Time.deltaTime * 14f);

            if (iboxExpandedCG != null)
            {
                float targetAlpha = iboxExpandT;
                iboxExpandedCG.alpha = Mathf.Lerp(iboxExpandedCG.alpha, targetAlpha, Time.deltaTime * 14f);
                if (Mathf.Abs(iboxExpandedCG.alpha - targetAlpha) < 0.01f) iboxExpandedCG.alpha = targetAlpha;

                bool active = iboxExpandedCG.alpha > 0.1f;
                iboxExpandedCG.blocksRaycasts = active;
                iboxExpandedCG.interactable = iboxExpandedCG.alpha > 0.6f;
            }
        }

        private void RefreshTboxPinVisual()
        {
            if (tboxPinBtnText == null) return;
            if (tboxPinned)
            {
                tboxPinBtnText.text  = "●";
                tboxPinBtnText.color = new Color(0.45f, 0.75f, 0.90f, 1f); // teal accent
                var pinImg = tboxPinBtn != null ? tboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.10f, 0.22f, 0.30f, 1f);
            }
            else
            {
                tboxPinBtnText.text  = "○";
                tboxPinBtnText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                var pinImg = tboxPinBtn != null ? tboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.20f, 0.20f, 0.20f, 1f);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void UpdateSelectionContextMenu()
        {
            if (canvas == null) return;
            EnsureTboxUI();
            if (tbox == null) return;

            int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
            bool visible = sel > 0;
            if (tbox.activeSelf != visible) tbox.SetActive(visible);
            if (!visible)
            {
                tboxExpandT   = 0f;
                tboxIsHovered = false;
                if (tboxPinned) { tboxPinned = false; RefreshTboxPinVisual(); }
                // Keep ibox consistent when selection is cleared
                try { UpdateIboxUI(); } catch { }
                return;
            }

            if (tboxLabel != null)
                tboxLabel.text = sel == 1
                    ? VPBTranslation.T("gallery.tbox.selected_one", "1 Selected")
                    : string.Format(VPBTranslation.T("gallery.tbox.selected_many", "{0} Selected"), sel);

            // Whether the bar should be in expanded state
            bool wantExpanded = tboxIsHovered || tboxPinned;

            // Smooth animate expand T
            float targetT = wantExpanded ? 1f : 0f;
            tboxExpandT = Mathf.Lerp(tboxExpandT, targetT, Time.deltaTime * 10f);
            if (Mathf.Abs(tboxExpandT - targetT) < 0.005f) tboxExpandT = targetT;

            // Resize bar height (grows/shrinks upward, pivot is at bottom)
            if (tboxRT != null)
            {
                float h = Mathf.Lerp(TboxCollapsedH, TboxExpandedH, tboxExpandT);
                tboxRT.sizeDelta = new Vector2(0f, h);
            }

            // Cross-fade: label fades out, buttons fade in
            if (tboxLabelCG != null)
            {
                tboxLabelCG.alpha = Mathf.Lerp(tboxLabelCG.alpha, 1f - tboxExpandT, Time.deltaTime * 14f);
            }

            if (tboxButtonsCG != null)
            {
                float targetAlpha = tboxExpandT;
                tboxButtonsCG.alpha = Mathf.Lerp(tboxButtonsCG.alpha, targetAlpha, Time.deltaTime * 14f);
                if (Mathf.Abs(tboxButtonsCG.alpha - targetAlpha) < 0.01f)
                    tboxButtonsCG.alpha = targetAlpha;

                bool active = tboxButtonsCG.alpha > 0.1f;
                tboxButtonsCG.blocksRaycasts = active;
                tboxButtonsCG.interactable   = tboxButtonsCG.alpha > 0.6f;
            }

            RefreshTboxConditionalActionButtons();

            // Info box (top) updates independently (single selection only)
            try { UpdateIboxUI(); } catch { }
        }

        /// <summary>Copy/Delete/Hide/Unhide/Autoinstall: counts in labels and compact layout for the hide/AI group.</summary>
        private void RefreshTboxConditionalActionButtons()
        {
            int copyN = 0, deleteN = 0, hideN = 0, unhideN = 0, aiN = 0, noAiN = 0;
            if (selectedFiles != null && selectedFiles.Count > 0)
            {
                copyN = CollectUniquePackageUidsFromSelection(selectedFiles).Count
                    + CollectUniqueLocalSceneGalleryRelativePathsFromSelection(selectedFiles).Count;
                try { deleteN = GetTboxDeleteEligiblePackageCount() + GetTboxDeleteEligibleLocalSceneCount(); } catch { deleteN = 0; }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out bool hidden, out bool fiAi, out bool uidAl))
                        continue;
                    if (!seen.Add(uid)) continue;
                    if (hidden) unhideN++;
                    else hideN++;
                    if (fiAi || uidAl) noAiN++;
                    if (!fiAi || !uidAl) aiN++;
                }
            }

            if (tboxCopyPkgNamesBtn != null)
                SetTboxCountButtonLabel(tboxCopyPkgNamesBtn, "gallery.tbox.copy_names_count", "Copy Names ({0})", copyN);
            if (tboxDeleteBtn != null)
                SetTboxCountButtonLabel(tboxDeleteBtn, "gallery.tbox.delete_count", "Delete ({0})", deleteN);

            bool showHide = hideN > 0;
            bool showUnhide = unhideN > 0;
            bool showAi = aiN > 0;
            bool showNoAi = noAiN > 0;

            if (tboxHideBtn != null)
            {
                tboxHideBtn.SetActive(showHide);
                if (showHide) SetTboxCountButtonLabel(tboxHideBtn, "gallery.tbox.hide_count", "Hide ({0})", hideN);
            }
            if (tboxUnhideBtn != null)
            {
                tboxUnhideBtn.SetActive(showUnhide);
                if (showUnhide) SetTboxCountButtonLabel(tboxUnhideBtn, "gallery.tbox.unhide_count", "Unhide ({0})", unhideN);
            }
            if (tboxAutoInstallBtn != null)
            {
                tboxAutoInstallBtn.SetActive(showAi);
                if (showAi) SetTboxCountButtonLabel(tboxAutoInstallBtn, "gallery.tbox.autoinstall_count", "Autoinstall ({0})", aiN);
            }
            if (tboxDisableAutoInstallBtn != null)
            {
                tboxDisableAutoInstallBtn.SetActive(showNoAi);
                if (showNoAi) SetTboxCountButtonLabel(tboxDisableAutoInstallBtn, "gallery.tbox.no_autoinstall_count", "No autoinstall ({0})", noAiN);
            }

            LayoutTboxHideAutoinstallButtonRow(showAi, showHide, showUnhide, showNoAi);
        }

        private static void SetTboxCountButtonLabel(GameObject go, string key, string fallbackFmt, int count)
        {
            if (go == null) return;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null)
                t.text = string.Format(VPBTranslation.T(key, fallbackFmt), count);
        }

        /// <summary>Repack Autoinstall / Hide / Unhide / No autoinstall against Delete so hidden buttons leave no gap.</summary>
        private void LayoutTboxHideAutoinstallButtonRow(bool showAi, bool showHide, bool showUnhide, bool showNoAi)
        {
            const float gap = 10f;
            const float wAi = 168f;
            const float wHide = 100f;
            const float wUnhide = 100f;
            const float wNoAi = 168f;
            const float deletePivotX = -232f;
            const float deleteW = 180f;
            float x = deletePivotX - deleteW - gap;

            void Place(GameObject go, float width)
            {
                if (go == null) return;
                var rt = go.GetComponent<RectTransform>();
                if (rt == null) return;
                Vector2 p = rt.anchoredPosition;
                rt.anchoredPosition = new Vector2(x, p.y);
                x -= width + gap;
            }

            if (showAi) Place(tboxAutoInstallBtn, wAi);
            if (showHide) Place(tboxHideBtn, wHide);
            if (showUnhide) Place(tboxUnhideBtn, wUnhide);
            if (showNoAi) Place(tboxDisableAutoInstallBtn, wNoAi);
        }

        /// <summary>Unique gallery-relative paths for on-disk Saves/scene JSON rows (for Copy Names).</summary>
        private static HashSet<string> CollectUniqueLocalSceneGalleryRelativePathsFromSelection(IList<FileEntry> files)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (files == null) return set;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null) continue;
                if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string rel, false)) continue;
                if (!string.IsNullOrEmpty(rel)) set.Add(rel.Replace('\\', '/'));
            }
            return set;
        }

        /// <summary>Resolve a gallery row to an on-disk .var for tbox hide/autoinstall actions (one row may share a UID).</summary>
        private bool TryGetTboxResolvablePackageState(FileEntry f, out string uid, out FileEntry diskFe, out bool isHidden, out bool fileAutoInstall, out bool uidAutoLoad)
        {
            uid = null;
            diskFe = null;
            isHidden = false;
            fileAutoInstall = false;
            uidAutoLoad = false;

            if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string relGallery, false))
            {
                uid = relGallery.Replace('\\', '/');
                diskFe = f;
                isHidden = PackageHidePrefs.IsLocalSceneJsonHidden(f);
                try { fileAutoInstall = LocalSceneGallerySupport.IsLocalSceneAutoInstallMarked(f); }
                catch { fileAutoInstall = false; }
                uidAutoLoad = false;
                return true;
            }

            uid = TryGetPackageUidForEntry(f);
            if (string.IsNullOrEmpty(uid)) return false;

            string path = ResolveVarPathForUid(uid);
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                var fe = FileManager.GetFileEntry(path, true);
                if (fe == null) return false;
                diskFe = fe;
                isHidden = PackageHidePrefs.IsPackageVarHidden(fe);
                try { fileAutoInstall = fe.IsAutoInstall(); }
                catch { fileAutoInstall = false; }
                try
                {
                    uidAutoLoad = AutoLoadPackagesManager.Instance != null && AutoLoadPackagesManager.Instance.IsAutoLoad(uid);
                }
                catch { uidAutoLoad = false; }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void CopySelectedPackageNamesToClipboard()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var uids = CollectUniquePackageUidsFromSelection(selectedFiles);
                var localScenes = CollectUniqueLocalSceneGalleryRelativePathsFromSelection(selectedFiles);
                if (uids.Count == 0 && localScenes.Count == 0)
                {
                    ShowTemporaryStatus("No package or local scene paths in selection.");
                    return;
                }

                var list = new List<string>(uids.Count + localScenes.Count);
                foreach (var uid in uids)
                    list.Add(uid + ".var");
                foreach (var rel in localScenes)
                    list.Add(rel);
                list.Sort(StringComparer.OrdinalIgnoreCase);

                string text = string.Join("\n", list.ToArray());

                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus($"Copied {list.Count} name(s) to clipboard.", 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] CopySelectedPackageNamesToClipboard error: " + ex.Message);
                ShowTemporaryStatus("Copy failed. See log.", 2f);
            }
        }

        private static string TryGetPackageUidForEntry(FileEntry f)
        {
            if (f is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                return vfe.Package.Uid;

            if (f is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                return ple.Package.Uid;

            if (f is MissingPackageListEntry mp && !string.IsNullOrEmpty(mp.RequestedUid))
                return mp.RequestedUid;

            string p = f.Path ?? "";
            if (string.IsNullOrEmpty(p)) return null;

            int internalSep = p.IndexOf(":/", StringComparison.Ordinal);
            if (internalSep >= 0) p = p.Substring(0, internalSep);

            p = p.Replace('\\', '/');
            if (!p.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) return null;

            int slash = p.LastIndexOf('/');
            string file = (slash >= 0) ? p.Substring(slash + 1) : p;
            if (file.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - 4);

            return string.IsNullOrEmpty(file) ? null : file;
        }
    }
}
