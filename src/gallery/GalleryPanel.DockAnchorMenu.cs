using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    /// <summary>Which way the dock menu opens off the button that raised it.</summary>
    internal enum DockMenuPlacement
    {
        Above = 0,
        RightOf = 1,
        LeftOf = 2
    }

    public partial class GalleryPanel
    {
        // Single dock control. The side rail used to carry a float/fixed toggle AND a dock-to-top
        // button; this menu replaces both — every edge, the clone variants, and the way back to floating.

        private GameObject _dockAnchorMenuGO;
        private GameObject _dockAnchorMenuAnchorGO;
        private bool _dockAnchorMenuOpen;

        /// <summary>
        /// Wider than <see cref="GalleryUiDesignTokens.PopupMenuPanelWidthRef"/> (230): these rows carry
        /// both an icon and a two-clause label ("Clone → dock to Right"), which clips at the shared width.
        /// </summary>
        private const float DockMenuPanelWidthRef = 300f;

        private Sprite _dockMenuTopIcon;
        private Sprite _dockMenuLeftIcon;
        private Sprite _dockMenuRightIcon;
        private Sprite _dockMenuCloneIcon;
        private Sprite _dockMenuFloatIcon;

        /// <summary>Edge the "other side" entry targets: the far side from this pane, preferring a free one.</summary>
        internal GalleryDockSide OppositeDockSideChoice()
        {
            GalleryDockSide cur = DockSide;
            if (cur == GalleryDockSide.Left) return GalleryDockSide.Right;
            if (cur == GalleryDockSide.Right) return GalleryDockSide.Left;

            if (GalleryDockLayout.IsFreeFor(GalleryDockSide.Right, PanelId)) return GalleryDockSide.Right;
            if (GalleryDockLayout.IsFreeFor(GalleryDockSide.Left, PanelId)) return GalleryDockSide.Left;
            return GalleryDockSide.Right;
        }

        private void EnsureDockAnchorMenuChrome()
        {
            if (_dockAnchorMenuGO != null) return;
            if (backgroundBoxGO == null) return;

            _dockMenuTopIcon = UI.LoadIconSprite("box-align-top", UI.BarIconGlyphTint);
            _dockMenuLeftIcon = UI.LoadIconSprite("box-align-left", UI.BarIconGlyphTint);
            _dockMenuRightIcon = UI.LoadIconSprite("box-align-right", UI.BarIconGlyphTint);
            _dockMenuCloneIcon = UI.LoadIconSprite("copy-plus", UI.BarIconGlyphTint);
            _dockMenuFloatIcon = UI.LoadIconSprite("float-center", UI.BarIconGlyphTint);

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

            GalleryDockSide other = OppositeDockSideChoice();
            string otherName = GalleryDockLayout.ToConfigString(other);

            bool topFree = GalleryDockLayout.IsFreeFor(GalleryDockSide.Top, PanelId);
            bool otherFree = GalleryDockLayout.IsFreeFor(other, PanelId);
            // A clone needs an edge nobody holds — "free for this pane" is not enough.
            bool topFreeForClone = GalleryDockLayout.IsFreeFor(GalleryDockSide.Top, null);
            bool otherFreeForClone = GalleryDockLayout.IsFreeFor(other, null);
            bool canClone = Gallery.singleton != null && Gallery.singleton.PanelCount < Gallery.MaxPanels;

            UI.AddStretchPopupMenuRow(panel,
                VPBTranslation.T("gallery.dock_anchor.to_top", "Dock to Top"),
                () => { CloseDockAnchorMenu(); DockPaneToSide(GalleryDockSide.Top); },
                isActive: DockSide == GalleryDockSide.Top, enabled: topFree, icon: _dockMenuTopIcon);

            UI.AddStretchPopupMenuRow(panel,
                string.Format(VPBTranslation.T("gallery.dock_anchor.to_side", "Dock to {0}"), otherName),
                () => { CloseDockAnchorMenu(); DockPaneToSide(other); },
                isActive: DockSide == other, enabled: otherFree, icon: DockMenuSideIcon(other));

            UI.AddStretchPopupMenuRow(panel,
                VPBTranslation.T("gallery.dock_anchor.clone_top", "Clone → dock to Top"),
                () => { CloseDockAnchorMenu(); CloneAndDockPane(GalleryDockSide.Top); },
                isActive: false, enabled: canClone && topFreeForClone, icon: _dockMenuCloneIcon);

            UI.AddStretchPopupMenuRow(panel,
                string.Format(VPBTranslation.T("gallery.dock_anchor.clone_side", "Clone → dock to {0}"), otherName),
                () => { CloseDockAnchorMenu(); CloneAndDockPane(other); },
                isActive: false, enabled: canClone && otherFreeForClone, icon: _dockMenuCloneIcon);

            // The only way back out of a dock now that the float/fixed rail toggle is gone.
            UI.AddStretchPopupMenuRow(panel,
                VPBTranslation.T("gallery.dock_anchor.float", "Float this pane"),
                () => { CloseDockAnchorMenu(); FloatPaneFromDock(); },
                isActive: false, enabled: isFixedLocally, icon: _dockMenuFloatIcon);
        }

        /// <summary>
        /// Opens off <paramref name="anchorGO"/>. Re-clicking the same button closes; clicking a
        /// different one re-anchors, so the rail and footer entry points never fight over the menu.
        /// </summary>
        internal void ToggleDockAnchorMenu(GameObject anchorGO, DockMenuPlacement placement)
        {
            bool isVR = false;
            try { isVR = XrUtils.IsVrActive(); } catch { }
            if (isVR)
            {
                try
                {
                    // Menu RightOf the button = left rail; clone onto that side.
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
                    // Passing the width ref matters: without it the panel keeps its 1× width while the
                    // rows, icons and fonts scale up, which is exactly how the labels start clipping.
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

        /// <summary>Footer button opens upward; rail buttons open inward, away from their own edge.</summary>
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
                return;
            }

            bool toRight = placement == DockMenuPlacement.RightOf;
            panelRT.pivot = new Vector2(toRight ? 0f : 1f, 0.5f);
            float halfBtn = anchorRT.rect.width * 0.5f;
            panelRT.anchoredPosition = new Vector2(
                toRight ? local.x + halfBtn + gap : local.x - halfBtn - gap,
                local.y);
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, 8f * s);
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

        /// <summary>
        /// Spawns a clone and parks it on <paramref name="side"/>. The clone starts floating and its
        /// canvas render mode only settles a frame later, so the dock claim waits one frame.
        /// </summary>
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
