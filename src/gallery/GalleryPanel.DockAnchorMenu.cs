using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    internal enum DockMenuPlacement
    {
        Above = 0,
        RightOf = 1,
        LeftOf = 2
    }

    public partial class GalleryPanel
    {
        private GameObject _dockAnchorMenuGO;
        private GameObject _dockAnchorMenuAnchorGO;
        private bool _dockAnchorMenuOpen;

        private const float DockMenuPanelWidthRef = 300f;

        private Sprite _dockMenuTopIcon;
        private Sprite _dockMenuLeftIcon;
        private Sprite _dockMenuRightIcon;
        private Sprite _dockMenuCloneIcon;
        private Sprite _dockMenuFloatIcon;
        private Sprite _dockMenuCollapseIcon;
        private Sprite _dockMenuExpandIcon;
        private Sprite _dockMenuAutoHideOnIcon;
        private Sprite _dockMenuAutoHideOffIcon;
        private Sprite _dockMenuLayoutsIcon;
        private Sprite _dockMenuCloseIcon;

        private static readonly GalleryDockSide[] DockMenuSideOrder =
        {
            GalleryDockSide.Left, GalleryDockSide.Top, GalleryDockSide.Right
        };

        private void EnsureDockAnchorMenuChrome()
        {
            if (_dockAnchorMenuGO != null) return;
            if (backgroundBoxGO == null) return;

            _dockMenuTopIcon = UI.LoadIconSprite("box-align-top", UI.BarIconGlyphTint);
            _dockMenuLeftIcon = UI.LoadIconSprite("box-align-left", UI.BarIconGlyphTint);
            _dockMenuRightIcon = UI.LoadIconSprite("box-align-right", UI.BarIconGlyphTint);
            _dockMenuCloneIcon = UI.LoadIconSprite("copy-plus", UI.BarIconGlyphTint);
            _dockMenuFloatIcon = UI.LoadIconSprite("float-center", UI.BarIconGlyphTint);
            _dockMenuCollapseIcon = UI.LoadIconSprite("layout-sidebar-right-collapse", UI.BarIconGlyphTint);
            _dockMenuExpandIcon = UI.LoadIconSprite("layout-sidebar-right-expand", UI.BarIconGlyphTint);
            _dockMenuAutoHideOnIcon = UI.LoadIconSprite("eye-off", UI.BarIconGlyphTint);
            _dockMenuAutoHideOffIcon = UI.LoadIconSprite("eye", UI.BarIconGlyphTint);
            _dockMenuLayoutsIcon = UI.LoadIconSprite("layout-board-split", UI.BarIconGlyphTint);
            _dockMenuCloseIcon = UI.LoadIconSprite("square-x", UI.BarIconGlyphTint);

            _dockAnchorMenuGO = UI.CreatePopupMenuRoot(backgroundBoxGO, "DockAnchorMenu", CloseDockAnchorMenu);
            _dockAnchorMenuGO.SetActive(false);

            GameObject panel = new GameObject("DockAnchorPanel");
            panel.transform.SetParent(_dockAnchorMenuGO.transform, false);
            RectTransform panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(DockMenuPanelWidthRef, 50f);
            UI.AddImage(panel, new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f));
            UI.AddVLG(panel, spacing: UI.GapTight(), padding: UI.PadPopup());
            ContentSizeFitter csf = panel.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private Sprite DockMenuSideIcon(GalleryDockSide side)
        {
            if (side == GalleryDockSide.Left) return _dockMenuLeftIcon;
            if (side == GalleryDockSide.Top) return _dockMenuTopIcon;
            return _dockMenuRightIcon;
        }

        private void RebuildDockAnchorMenuRows(Transform panel)
        {
            if (panel == null) return;
            UI.DestroyAllChildren(panel);

            bool canClone = Gallery.singleton != null && Gallery.singleton.PanelCount < Gallery.MaxPanels;

            for (int i = 0; i < DockMenuSideOrder.Length; i++)
            {
                GalleryDockSide side = DockMenuSideOrder[i];
                string name = GalleryDockLayout.ToConfigString(side);
                UI.AddStretchPopupMenuRow(panel,
                    string.Format(VPBTranslation.T("gallery.dock_anchor.to_side", "Dock to {0}"), name),
                    () => { CloseDockAnchorMenu(); DockPaneToSide(side); },
                    isActive: DockSide == side,
                    enabled: GalleryDockLayout.IsFreeFor(side, PanelId),
                    icon: DockMenuSideIcon(side));
            }

            UI.AddStretchPopupMenuRow(panel,
                VPBTranslation.T("gallery.dock_anchor.float", "Float this pane"),
                () => { CloseDockAnchorMenu(); FloatPaneFromDock(); },
                isActive: false, enabled: isFixedLocally, icon: _dockMenuFloatIcon);

            if (isFixedLocally)
            {
                UI.AddStretchPopupMenuRow(panel,
                    isCollapsed
                        ? VPBTranslation.T("gallery.dock_anchor.expand", "Expand this dock")
                        : VPBTranslation.T("gallery.dock_anchor.collapse", "Collapse this dock"),
                    () => { CloseDockAnchorMenu(); SetCollapsed(!isCollapsed); },
                    isActive: false, enabled: true,
                    icon: isCollapsed ? _dockMenuExpandIcon : _dockMenuCollapseIcon);

                bool autoHide = DockAutoHide;
                UI.AddStretchPopupMenuRow(panel,
                    autoHide
                        ? VPBTranslation.T("gallery.dock_anchor.autohide_on", "Auto-hide this dock (on)")
                        : VPBTranslation.T("gallery.dock_anchor.autohide_off", "Auto-hide this dock (off)"),
                    () => { CloseDockAnchorMenu(); ToggleAutoHideMode(); },
                    isActive: autoHide, enabled: true,
                    icon: autoHide ? _dockMenuAutoHideOnIcon : _dockMenuAutoHideOffIcon);
            }

            UI.AddStretchPopupMenuRow(panel,
                VPBTranslation.T("gallery.dock_anchor.clone_float", "Clone → floating pane"),
                () => { CloseDockAnchorMenu(); CloneFloatingPane(); },
                isActive: false, enabled: canClone, icon: _dockMenuCloneIcon);

            for (int i = 0; i < DockMenuSideOrder.Length; i++)
            {
                GalleryDockSide side = DockMenuSideOrder[i];
                if (!GalleryDockLayout.IsFreeFor(side, null)) continue;
                string name = GalleryDockLayout.ToConfigString(side);
                UI.AddStretchPopupMenuRow(panel,
                    string.Format(VPBTranslation.T("gallery.dock_anchor.clone_side", "Clone → dock to {0}"), name),
                    () => { CloseDockAnchorMenu(); CloneAndDockPane(side); },
                    isActive: false, enabled: canClone, icon: _dockMenuCloneIcon);
            }

            UI.AddStretchPopupMenuRow(panel,
                VPBTranslation.T("gallery.dock_anchor.layouts", "Layouts…"),
                () => { CloseDockAnchorMenu(); ToggleLayoutPresetsFloat(); },
                isActive: false, enabled: true, icon: _dockMenuLayoutsIcon);

            UI.AddStretchPopupMenuRow(panel,
                VPBTranslation.T("gallery.dock_anchor.close_pane", "Close this pane"),
                () => { CloseDockAnchorMenu(); Close(); },
                isActive: false,
                enabled: Gallery.singleton != null && Gallery.singleton.PanelCount > 1,
                icon: _dockMenuCloseIcon);
        }

        internal void ToggleDockAnchorMenu(GameObject anchorGO, DockMenuPlacement placement)
        {
            bool isVR = false;
            try { isVR = XrUtils.IsVrActive(); } catch { }
            if (isVR)
            {
                try
                {
                    if (Gallery.singleton != null && Gallery.singleton.PanelCount < Gallery.MaxPanels)
                        Gallery.singleton.ClonePanel(this, placement != DockMenuPlacement.RightOf);
                }
                catch { }
                return;
            }

            EnsureDockAnchorMenuChrome();
            if (_dockAnchorMenuGO == null) return;

            if (_dockAnchorMenuOpen && _dockAnchorMenuAnchorGO == anchorGO)
            {
                CloseDockAnchorMenu();
                return;
            }

            _dockAnchorMenuOpen = true;
            _dockAnchorMenuAnchorGO = anchorGO;

            Transform panel = _dockAnchorMenuGO.transform.Find("DockAnchorPanel");
            if (panel != null)
            {
                RebuildDockAnchorMenuRows(panel);
                try
                {
                    ScaleVerticalPopupMenuRows(panel.gameObject, ChromeScale,
                        GalleryUiDesignTokens.PopupMenuRowHeightRef,
                        GalleryUiDesignTokens.PopupMenuOverflowFontRef,
                        DockMenuPanelWidthRef);
                }
                catch { }
                PositionDockAnchorMenuPanel(panel as RectTransform, anchorGO, placement);
            }
            _dockAnchorMenuGO.transform.SetAsLastSibling();
            _dockAnchorMenuGO.SetActive(true);
        }

        private void PositionDockAnchorMenuPanel(RectTransform panelRT, GameObject anchorGO, DockMenuPlacement placement)
        {
            if (panelRT == null) return;
            RectTransform overlayRT = _dockAnchorMenuGO != null ? _dockAnchorMenuGO.GetComponent<RectTransform>() : null;
            RectTransform anchorRT = anchorGO != null ? anchorGO.GetComponent<RectTransform>() : null;

            if (overlayRT == null || anchorRT == null)
            {
                panelRT.pivot = new Vector2(0.5f, 0.5f);
                panelRT.anchoredPosition = Vector2.zero;
                return;
            }

            float s = ChromeScale > 0f ? ChromeScale : 1f;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s + 8f * s;
            Vector3 worldCenter = anchorRT.TransformPoint(anchorRT.rect.center);
            Vector3 local = overlayRT.InverseTransformPoint(worldCenter);

            if (placement == DockMenuPlacement.Above)
            {
                panelRT.pivot = new Vector2(0.5f, 0f);
                panelRT.anchoredPosition = new Vector2(local.x, local.y + anchorRT.rect.height * 0.5f + gap);
                UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
                UI.ClampPopupMenuPanelY(panelRT, overlayRT, 8f * s);
                return;
            }

            bool toRight = placement == DockMenuPlacement.RightOf;
            panelRT.pivot = new Vector2(toRight ? 0f : 1f, 0.5f);
            float halfBtn = anchorRT.rect.width * 0.5f;
            panelRT.anchoredPosition = new Vector2(
                toRight ? local.x + halfBtn + gap : local.x - halfBtn - gap,
                local.y);
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
            UI.ClampPopupMenuPanelY(panelRT, overlayRT, 8f * s);
        }

        /// <summary>Live UI-scale change while the menu is open — rows and panel width must both follow.</summary>
        private void RescaleDockAnchorMenuInternal(float s)
        {
            if (_dockAnchorMenuGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = _dockAnchorMenuGO.transform.Find("DockAnchorPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(panel.gameObject, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuOverflowFontRef,
                DockMenuPanelWidthRef);
        }

        private void CloseDockAnchorMenu()
        {
            _dockAnchorMenuOpen = false;
            _dockAnchorMenuAnchorGO = null;
            if (_dockAnchorMenuGO != null) _dockAnchorMenuGO.SetActive(false);
        }

        private void CloneFloatingPane()
        {
            if (Gallery.singleton == null) return;
            Gallery.singleton.ClonePanel(this, true);
        }

        private void CloneAndDockPane(GalleryDockSide side)
        {
            if (Gallery.singleton == null) return;

            if (!GalleryDockLayout.IsFreeFor(side, null))
            {
                ShowDockSideTakenStatus(GalleryDockLayout.ToConfigString(side));
                return;
            }

            GalleryPanel clone = Gallery.singleton.ClonePanel(this, true);
            if (clone == null) return;
            StartCoroutine(DockClonedPaneCo(clone, side));
        }

        private IEnumerator DockClonedPaneCo(GalleryPanel clone, GalleryDockSide side)
        {
            yield return null;
            if (clone == null || clone.canvas == null) yield break;
            try { clone.DockPaneToSide(side); }
            catch { }
        }
    }
}
