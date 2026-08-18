using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly Color GalleryBackgroundOpaque = UI.ChromeDarker;
        private static readonly Color GalleryBackgroundTinted = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        /// <summary>Invisible hit area only — no visible side-rail backdrop.</summary>
        private static readonly Color SideHoverHitAreaInvisible = new Color(0f, 0f, 0f, 0f);
        private static readonly Color CollapseTriggerCollapsedTransparent = new Color(0.15f, 0.15f, 0.15f, 0.4f);
        private static readonly Color CollapseTriggerCollapsedOpaque = UI.ChromeDark;
        private static readonly Color CollapseTriggerExpandedHidden = new Color(1f, 1f, 1f, 0f);

        private UIHoverColor backgroundHoverColor;

        /// <summary>Recomputes and applies pane/side/dock-hover transparency immediately (settings toggle, ConfigChanged, Update).</summary>
        internal void ApplyGalleryTransparencyVisuals()
        {
            if (VPBConfig.Instance == null) return;

            if (backgroundCanvasGroup != null)
            {
                ComputeGalleryTransparencyState(
                    out float paneAlpha,
                    out float sideAlpha,
                    out Color backgroundColor,
                    out bool forceShowSideButtons);

                if (forceShowSideButtons)
                    sideButtonsFadeDelayTimer = 0f;

                if (backgroundCanvasGroup.alpha != paneAlpha)
                    backgroundCanvasGroup.alpha = paneAlpha;

                try
                {
                    if (backgroundHoverColor != null)
                    {
                        backgroundHoverColor.normalColor = backgroundColor;
                        backgroundHoverColor.hoverColor = backgroundColor;
                    }
                    if (backgroundBoxGO != null)
                    {
                        Image bgImg = backgroundBoxGO.GetComponent<Image>();
                        if (bgImg != null) bgImg.color = backgroundColor;
                    }
                }
                catch { }

                sideButtonsAlpha = sideAlpha;
                for (int i = 0; i < sideButtonGroups.Count; i++)
                {
                    CanvasGroup cg = sideButtonGroups[i];
                    if (cg != null && cg.alpha != sideButtonsAlpha)
                        cg.alpha = sideButtonsAlpha;
                }
            }

            try { ApplyGalleryDockHoverVisuals(); } catch { }
        }

        private void ApplyGalleryDockHoverVisuals()
        {
            // Side rails: invisible hit targets only (buttons are independent visuals, no panel backdrop).
            ApplyGallerySideHoverHitArea(leftSideHoverStrip);
            ApplyGallerySideHoverHitArea(rightSideHoverStrip);
            ApplyGallerySideHoverHitArea(leftSideContainer);
            ApplyGallerySideHoverHitArea(rightSideContainer);

            ApplyCollapseTriggerVisual(collapseTriggerGO, collapseHandleText);
            ApplyCollapseTriggerVisual(collapseTriggerLeftGO, collapseHandleLeftText);
            ApplyCollapseTriggerVisual(collapseTriggerTopGO, collapseHandleTopText);
        }

        private static void ApplyGallerySideHoverHitArea(GameObject go)
        {
            if (go == null) return;
            try
            {
                Image img = go.GetComponent<Image>();
                if (img == null) return;
                if (img.color != SideHoverHitAreaInvisible) img.color = SideHoverHitAreaInvisible;
                img.raycastTarget = true;
            }
            catch { }
        }

        private void ApplyCollapseTriggerVisual(GameObject trigger, Text handleText)
        {
            if (trigger == null) return;
            bool opaque = VPBConfig.Instance != null && VPBConfig.Instance.ShouldDisableGalleryDockHoverTransparency();
            try
            {
                Image img = trigger.GetComponent<Image>();
                if (img != null)
                {
                    Color collapsedColor = opaque ? CollapseTriggerCollapsedOpaque : CollapseTriggerCollapsedTransparent;
                    img.color = isCollapsed ? collapsedColor : CollapseTriggerExpandedHidden;
                    img.raycastTarget = isCollapsed;
                }
            }
            catch { }
            if (handleText != null) handleText.gameObject.SetActive(isCollapsed);
        }

        internal static void ApplyGalleryTransparencyToAllPanels()
        {
            try
            {
                if (Gallery.singleton != null)
                {
                    var panels = Gallery.singleton.Panels;
                    if (panels != null)
                    {
                        for (int i = 0; i < panels.Count; i++)
                        {
                            GalleryPanel p = panels[i];
                            if (p != null) p.ApplyGalleryTransparencyVisuals();
                        }
                    }
                }
                if (VamHookPlugin.singleton != null)
                    VamHookPlugin.singleton.RefreshQuickMenuAssignableTransparency();
            }
            catch { }
        }

        private void AdvanceSideButtonsFadeDelayTimer()
        {
            // Same engagement as auto-hide — typing / search chrome keeps side rails visible.
            if (IsGalleryInteractionEngaged())
            {
                sideButtonsFadeDelayTimer = 0f;
                return;
            }
            sideButtonsFadeDelayTimer += Time.deltaTime;
        }

        private bool ShouldShowSideButtonsAfterFadeDelay()
        {
            if (IsGalleryInteractionEngaged())
                return true;
            return sideButtonsFadeDelayTimer < SideButtonsFadeDelay;
        }

        private void ComputeGalleryTransparencyState(
            out float paneCanvasAlpha,
            out float sideButtonsCanvasAlpha,
            out Color backgroundImageColor,
            out bool forceShowSideButtons)
        {
            paneCanvasAlpha = 1f;
            sideButtonsCanvasAlpha = 1f;
            backgroundImageColor = GalleryBackgroundOpaque;
            forceShowSideButtons = false;

            if (VPBConfig.Instance.DisableGalleryTransparency)
            {
                forceShowSideButtons = true;
                return;
            }

            backgroundImageColor = GalleryBackgroundTinted;

            bool isEngaged = IsGalleryInteractionEngaged();
            if (!VPBConfig.Instance.ShouldDisableGalleryPaneTransparency()
                && VPBConfig.Instance.EnableGalleryTranslucency && !isEngaged)
                paneCanvasAlpha = Mathf.Max(0.1f, VPBConfig.Instance.GalleryOpacity);

            bool enableFade = VPBConfig.Instance.EnableGalleryFade;
            bool vr = false;
            try { vr = VPB.src.util.XrUtils.IsVrActive(); } catch { }
            if (vr)
                forceShowSideButtons = true;
            else if (enableFade && !ShouldShowSideButtonsAfterFadeDelay())
                sideButtonsCanvasAlpha = 0f;
        }
    }
}
