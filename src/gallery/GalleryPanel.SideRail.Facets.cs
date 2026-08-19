using System;
using UnityEngine;
using UnityEngine.UI;

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

        private void SyncSideRailFacetCaptions()
        {
            ApplySideRailIconOnly(leftUserTagsSideBtn);
            ApplySideRailIconOnly(rightUserTagsSideBtn);
            ApplySideRailIconOnly(leftCategoryBtnImage != null ? leftCategoryBtnImage.gameObject : null);
            ApplySideRailIconOnly(rightCategoryBtnImage != null ? rightCategoryBtnImage.gameObject : null);
            ApplySideRailIconOnly(leftCreatorBtnImage != null ? leftCreatorBtnImage.gameObject : null);
            ApplySideRailIconOnly(rightCreatorBtnImage != null ? rightCreatorBtnImage.gameObject : null);
            ApplySideRailIconOnly(leftPathBtnImage != null ? leftPathBtnImage.gameObject : null);
            ApplySideRailIconOnly(rightPathBtnImage != null ? rightPathBtnImage.gameObject : null);
            ApplySideRailIconOnly(leftHistoryBtnImage != null ? leftHistoryBtnImage.gameObject : null);
            ApplySideRailIconOnly(rightHistoryBtnImage != null ? rightHistoryBtnImage.gameObject : null);
            ApplySideRailIconOnly(leftSceneImportSideBtn);
            ApplySideRailIconOnly(rightSceneImportSideBtn);
            ApplySideRailIconOnly(leftFollowBtnImage != null ? leftFollowBtnImage.gameObject : null);
            ApplySideRailIconOnly(rightFollowBtnImage != null ? rightFollowBtnImage.gameObject : null);
            ApplySideRailIconOnly(leftRemoveModeSideBtn);
            ApplySideRailIconOnly(rightRemoveModeSideBtn);
            ApplySideRailIconOnly(leftSaveBtnGO);
            ApplySideRailIconOnly(rightSaveBtnGO);
            ApplySideRailIconOnly(leftDockAnchorBtnImage != null ? leftDockAnchorBtnImage.gameObject : null);
            ApplySideRailIconOnly(rightDockAnchorBtnImage != null ? rightDockAnchorBtnImage.gameObject : null);
        }

        private void ApplySideRailIconOnly(GameObject go)
        {
            if (go == null) return;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                if (string.IsNullOrEmpty(t.text)) t.text = " ";
                if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
            }
            Transform iconT = go.transform.Find("Icon");
            RectTransform irt = iconT as RectTransform;
            if (irt == null) return;
            float s = ChromeScale <= 0f ? 1f : ChromeScale;
            UI.SizeButtonIcon(irt, go.GetComponent<RectTransform>(), GalleryUiDesignTokens.SideButtonIconPadRef * s);
        }
    }
}
