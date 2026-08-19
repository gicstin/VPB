using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>Pane body off, canvas kept as float host (Hide disables the canvas and takes floats with it).</summary>
        private bool _floatsOnly;

        internal bool IsFloatsOnly
        {
            get { return _floatsOnly; }
        }

        /// <summary>Canvas drawing — including floats-only. Capture/save need this, not <see cref="IsVisible"/>.</summary>
        internal bool HasLiveCanvas
        {
            get { return canvas != null && canvas.gameObject.activeInHierarchy && canvas.enabled; }
        }

        private void AdoptFloatsOnlyFromConfig()
        {
            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryFloatsOnlyMode) return;
            _floatsOnly = true;
            ApplyFloatsOnlyChrome();
        }

        internal static GalleryPanel EnsureFloatHostPane()
        {
            Gallery g = Gallery.singleton;
            if (g == null) return null;

            List<GalleryPanel> panels = g.Panels;
            if (panels != null)
            {
                for (int i = 0; i < panels.Count; i++)
                    if (panels[i] != null && panels[i].IsVisible) return panels[i];
                for (int i = 0; i < panels.Count; i++)
                    if (panels[i] != null && panels[i].HasLiveCanvas) return panels[i];
            }

            if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryFloatsOnlyMode) return null;

            try
            {
                VamHookPlugin hook = VamHookPlugin.singleton;
                if (hook != null) hook.EnsureGalleryCategories();
                g.CreatePane(null, false);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB][FloatsOnly] host pane: " + ex.Message); }

            panels = g.Panels;
            if (panels == null || panels.Count == 0) return null;

            GalleryPanel host = panels[panels.Count - 1];
            if (host != null && !host._floatsOnly) host.SetFloatsOnly(true, false);
            return host;
        }

        internal static void SetFloatsOnlyAllPanes(bool on)
        {
            if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryFloatsOnlyMode != on)
            {
                VPBConfig.Instance.GalleryFloatsOnlyMode = on;
                try { VPBConfig.Instance.Save(false, true); } catch { }
            }

            List<GalleryPanel> panels = Gallery.singleton != null ? Gallery.singleton.Panels : null;
            if (panels == null) return;
            for (int i = 0; i < panels.Count; i++)
            {
                GalleryPanel p = panels[i];
                if (p == null) continue;
                try { p.SetFloatsOnly(on, false); }
                catch (Exception ex) { LogUtil.LogError("[VPB][FloatsOnly] pane: " + ex.Message); }
            }
        }

        /// <param name="persist">False when the caller already wrote the preference (bulk toggle, preset apply).</param>
        internal void SetFloatsOnly(bool on, bool persist)
        {
            if (persist && VPBConfig.Instance != null && VPBConfig.Instance.GalleryFloatsOnlyMode != on)
            {
                VPBConfig.Instance.GalleryFloatsOnlyMode = on;
                try { VPBConfig.Instance.Save(false, true); } catch { }
            }

            if (_floatsOnly == on) return;
            _floatsOnly = on;

            if (on)
            {
                try { PersistCurrentBrowsePlace(); } catch { }
                // Dock slot still sizes other panes — an invisible claimed edge would steal their width.
                if (isFixedLocally)
                {
                    try { SetFixedLocally(false); } catch { }
                }
                _hiddenByMenuGate = false;
                hoverCount = 0;
                try { HideHoverPreview(null); } catch { }
                try { RemoveModeHidePopup(); RemoveModeClearHelp(); } catch { }
                ApplyFloatsOnlyChrome();
            }
            else if (!_userHidden)
            {
                SetCanvasVisible(true);
                if (hasLoadedContent && refreshOnNextShow)
                {
                    refreshOnNextShow = false;
                    try { RefreshFiles(true); } catch { }
                }
                try { UpdateLayout(); } catch { }
            }

            try { UpdateFooterFloatsOnlyState(); } catch { }
            VpbPerfDiag.LogTransition("GalleryPanel.SetFloatsOnly", "on=" + on);
        }

        /// <summary>Drop mode without reveal — Show already does SetCanvasVisible / layout / refresh.</summary>
        private void ClearFloatsOnlyForShow()
        {
            if (!_floatsOnly) return;
            _floatsOnly = false;

            if (VPBConfig.Instance != null && VPBConfig.Instance.GalleryFloatsOnlyMode)
            {
                VPBConfig.Instance.GalleryFloatsOnlyMode = false;
                try { VPBConfig.Instance.Save(false, true); } catch { }
            }
            try { UpdateFooterFloatsOnlyState(); } catch { }
            VpbPerfDiag.LogTransition("GalleryPanel.SetFloatsOnly", "on=False src=show");
        }

        /// <summary>Canvas on, pane subtree off. Collapse triggers are canvas siblings of the body.</summary>
        private void ApplyFloatsOnlyChrome()
        {
            if (canvas == null) return;

            if (!_userHidden)
            {
                canvas.enabled = true;
                GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null) raycaster.enabled = true;
            }

            if (backgroundBoxGO != null && backgroundBoxGO.activeSelf) backgroundBoxGO.SetActive(false);
            if (collapseTriggerGO != null && collapseTriggerGO.activeSelf) collapseTriggerGO.SetActive(false);
            if (collapseTriggerLeftGO != null && collapseTriggerLeftGO.activeSelf) collapseTriggerLeftGO.SetActive(false);
            if (collapseTriggerTopGO != null && collapseTriggerTopGO.activeSelf) collapseTriggerTopGO.SetActive(false);
        }

        /// <summary>Restore float host after save/screenshot Hide without Show (Show would drop the mode).</summary>
        internal void RestoreFloatsOnlyHostAfterCapture()
        {
            _userHidden = false;
            SetCanvasVisible(true);
        }

        private void FloatsOnlyUpdateTick()
        {
            try { ApplyVamMenuGateVisibility(); } catch { }
            try { GalleryVrThumbstickScroll.TickOncePerFrame(); } catch { }
            try { PluginSettingsHotkeyCaptureUpdate(); } catch { }
            try { HandleKeyboardInput(); } catch { }
            try { GalleryUiScaleHotkey.TickDeferredSave(); } catch { }
            try { TickLayoutRevertBar(); } catch { }
            try { TickLayoutModeWatch(); } catch { }
        }

        /// <summary>Only the primary pane writes the preference — a fresh pane inherits it.</summary>
        internal void ApplyPaneFloatsOnly(LayoutPaneState pane)
        {
            if (pane == null) return;
            bool primary = string.Equals(PanelId, PrimaryPanelId, StringComparison.Ordinal);
            SetFloatsOnly(pane.FloatsOnly, primary);
        }

        private void ToggleFloatsOnlyMode()
        {
            SetFloatsOnlyAllPanes(!_floatsOnly);
        }

        private void UpdateFooterFloatsOnlyState()
        {
            if (footerFloatsOnlyBtnImage != null)
                footerFloatsOnlyBtnImage.color = _floatsOnly ? UI.AccentBlue : GalleryUiColorTokens.ChromeIconWell;
            if (footerFloatsOnlyIconImage != null)
            {
                Sprite target = _floatsOnly ? footerFloatsOnlyOnSprite : footerFloatsOnlyOffSprite;
                if (target != null) UI.SetIconSprite(footerFloatsOnlyIconImage, target);
            }
        }
    }
}
