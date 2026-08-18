using System;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    /// <summary>
    /// Facet rail: exclusive browse chips only (Tags / Category / Creator / Path / History / Import).
    /// Window chrome and tools live on title, footer, toolbox.
    /// </summary>
    public partial class GalleryPanel
    {
        private void UpdateSideButtonsVisibility()
        {
            if (VPBConfig.Instance == null) return;
            string mode = VPBConfig.NormalizeShowSideButtons(VPBConfig.Instance.ShowSideButtons);
            bool fixedMode = isFixedLocally;
            string dock = "Right";
            try { dock = EffectiveDockSideString; } catch { dock = "Right"; }
            bool topDock = fixedMode && string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase);

            bool wantLeft;
            bool wantRight;
            ResolveFacetRailSides(mode, fixedMode, dock, topDock, out wantLeft, out wantRight);

            if (leftSideContainer != null)
            {
                bool on = !isCollapsed && !topDock && wantLeft;
                if (leftSideContainer.activeSelf != on) leftSideContainer.SetActive(on);
            }

            if (rightSideContainer != null)
            {
                bool on = !isCollapsed && !topDock && wantRight;
                if (rightSideContainer.activeSelf != on) rightSideContainer.SetActive(on);
            }

            Color historyBackdrop = ColorHistoryAccent;
            if (rightHistoryBtnImage != null) rightHistoryBtnImage.color = historyBackdrop;
            if (leftHistoryBtnImage != null) leftHistoryBtnImage.color = historyBackdrop;
            if (rightHistoryBtnIconImage != null) rightHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;
            if (leftHistoryBtnIconImage != null) leftHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;

            try { SyncSideRailFacetCaptions(); } catch { }
        }

        private void ResolveFacetRailSides(
            string mode, bool fixedMode, string dock, bool topDock,
            out bool wantLeft, out bool wantRight)
        {
            wantLeft = false;
            wantRight = false;
            if (topDock) return;

            if (string.Equals(mode, "Left", StringComparison.OrdinalIgnoreCase))
            {
                wantLeft = true;
                return;
            }
            if (string.Equals(mode, "Right", StringComparison.OrdinalIgnoreCase))
            {
                wantRight = true;
                return;
            }
            if (string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase))
            {
                if (fixedMode && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                    wantRight = true;
                else if (fixedMode && string.Equals(dock, "Right", StringComparison.OrdinalIgnoreCase))
                    wantLeft = true;
                else
                {
                    wantLeft = true;
                    wantRight = true;
                }
                return;
            }

            // Auto: one rail on the free inner edge (dock) or last floating edge.
            if (fixedMode && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
            {
                wantRight = true;
                return;
            }
            if (fixedMode && string.Equals(dock, "Right", StringComparison.OrdinalIgnoreCase))
            {
                wantLeft = true;
                return;
            }

            bool lastLeft = string.Equals(
                VPBConfig.NormalizeSideRailEdge(VPBConfig.Instance.LastGallerySideRailEdge),
                "Left",
                StringComparison.OrdinalIgnoreCase);
            wantLeft = lastLeft;
            wantRight = !lastLeft;
        }

        internal void FlipAutoSideRail()
        {
            if (VPBConfig.Instance == null) return;
            string mode = VPBConfig.NormalizeShowSideButtons(VPBConfig.Instance.ShowSideButtons);
            if (!string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.side.flip_auto_only", "Flip rail needs Show Side Buttons = Auto."),
                    2f);
                return;
            }
            if (isFixedLocally)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.side.flip_float_only", "Docked pane already uses the free edge."),
                    2f);
                return;
            }

            bool lastLeft = string.Equals(
                VPBConfig.NormalizeSideRailEdge(VPBConfig.Instance.LastGallerySideRailEdge),
                "Left",
                StringComparison.OrdinalIgnoreCase);
            VPBConfig.Instance.LastGallerySideRailEdge = lastLeft ? "Right" : "Left";
            VPBConfig.Instance.Save(true, true);
            UpdateSideButtonsVisibility();
            UpdateSideButtonPositions();
        }

        private bool IsShowSideButtonsAuto()
        {
            if (VPBConfig.Instance == null) return true;
            return string.Equals(
                VPBConfig.NormalizeShowSideButtons(VPBConfig.Instance.ShowSideButtons),
                "Auto",
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>VR: short caption on facet chips (no hover tooltip). Desktop: icon only.</summary>
        private void SyncSideRailFacetCaptions()
        {
            bool vr = false;
            try { vr = XrUtils.IsVrActive(); } catch { }

            ApplySideRailFacetCaption(leftUserTagsSideBtn, VPBTranslation.T("gallery.side.usertags_short", "Tag"), vr);
            ApplySideRailFacetCaption(rightUserTagsSideBtn, VPBTranslation.T("gallery.side.usertags_short", "Tag"), vr);
            ApplySideRailFacetCaption(
                leftCategoryBtnImage != null ? leftCategoryBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.category_short", "Cat"), vr);
            ApplySideRailFacetCaption(
                rightCategoryBtnImage != null ? rightCategoryBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.category_short", "Cat"), vr);
            ApplySideRailFacetCaption(
                leftCreatorBtnImage != null ? leftCreatorBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.creator_short", "Auth"), vr);
            ApplySideRailFacetCaption(
                rightCreatorBtnImage != null ? rightCreatorBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.creator_short", "Auth"), vr);
            ApplySideRailFacetCaption(
                leftPathBtnImage != null ? leftPathBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.path_short", "Path"), vr);
            ApplySideRailFacetCaption(
                rightPathBtnImage != null ? rightPathBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.path_short", "Path"), vr);
            ApplySideRailFacetCaption(
                leftHistoryBtnImage != null ? leftHistoryBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.history_short", "Hist"), vr);
            ApplySideRailFacetCaption(
                rightHistoryBtnImage != null ? rightHistoryBtnImage.gameObject : null,
                VPBTranslation.T("gallery.side.history_short", "Hist"), vr);
            ApplySideRailFacetCaption(leftSceneImportSideBtn, VPBTranslation.T("gallery.side.scene_import_short", "Import"), vr);
            ApplySideRailFacetCaption(rightSceneImportSideBtn, VPBTranslation.T("gallery.side.scene_import_short", "Import"), vr);
            ApplySideRailFacetCaption(
                leftFollowBtnImage != null ? leftFollowBtnImage.gameObject : null,
                VPBTranslation.T("gallery.follow.follow", "Follow"), vr);
            ApplySideRailFacetCaption(
                rightFollowBtnImage != null ? rightFollowBtnImage.gameObject : null,
                VPBTranslation.T("gallery.follow.follow", "Follow"), vr);
            ApplySideRailFacetCaption(leftRemoveModeSideBtn, VPBTranslation.T("gallery.side.remove_mode_short", "Eraser"), vr);
            ApplySideRailFacetCaption(rightRemoveModeSideBtn, VPBTranslation.T("gallery.side.remove_mode_short", "Eraser"), vr);
            ApplySideRailFacetCaption(leftSaveBtnGO, VPBTranslation.T("gallery.side.save", "Save"), vr);
            ApplySideRailFacetCaption(rightSaveBtnGO, VPBTranslation.T("gallery.side.save", "Save"), vr);
            ApplySideRailFacetCaption(
                leftDockAnchorBtnImage != null ? leftDockAnchorBtnImage.gameObject : null,
                vr ? VPBTranslation.T("gallery.side.clone", "Clone") : VPBTranslation.T("gallery.side.dock_anchor", "Dock"), vr);
            ApplySideRailFacetCaption(
                rightDockAnchorBtnImage != null ? rightDockAnchorBtnImage.gameObject : null,
                vr ? VPBTranslation.T("gallery.side.clone", "Clone") : VPBTranslation.T("gallery.side.dock_anchor", "Dock"), vr);
        }

        private void ApplySideRailFacetCaption(GameObject go, string shortLabel, bool vr)
        {
            if (go == null) return;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t == null) return;

            if (!vr)
            {
                if (string.IsNullOrEmpty(t.text)) t.text = " ";
                if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
                return;
            }

            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t.text = shortLabel ?? "";
            t.alignment = TextAnchor.LowerCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.color = Color.white;
            t.raycastTarget = false;
            float s = ChromeScale <= 0f ? 1f : ChromeScale;
            GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);

            RectTransform trt = t.rectTransform;
            if (trt != null)
            {
                trt.anchorMin = new Vector2(0f, 0f);
                trt.anchorMax = new Vector2(1f, 0.38f);
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
                trt.pivot = new Vector2(0.5f, 0f);
            }

            Transform iconT = go.transform.Find("Icon");
            RectTransform irt = iconT as RectTransform;
            if (irt != null)
            {
                float pad = GalleryUiDesignTokens.SearchIconButtonPadRef * s;
                irt.anchorMin = Vector2.zero;
                irt.anchorMax = Vector2.one;
                irt.offsetMin = new Vector2(pad, pad + 9f * s);
                irt.offsetMax = new Vector2(-pad, -pad);
            }
        }
    }
}
