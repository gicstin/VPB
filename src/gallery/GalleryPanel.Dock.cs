using System;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private GalleryDockSide _dockSideCache = GalleryDockSide.None;
        private int _dockSideCacheVersion = -1;

        internal void InvalidateDockSideCache()
        {
            _dockSideCacheVersion = -1;
        }

        internal GalleryDockSide DockSide
        {
            get
            {
                int v = GalleryDockLayout.Version;
                if (_dockSideCacheVersion != v)
                {
                    _dockSideCacheVersion = v;
                    _dockSideCache = GalleryDockLayout.SideOf(PanelId);
                }
                return _dockSideCache;
            }
        }

        internal GalleryDockSide EffectiveDockSide
        {
            get
            {
                GalleryDockSide s = DockSide;
                if (s != GalleryDockSide.None) return s;

                VPBConfig cfg = VPBConfig.Instance;
                if (cfg == null) return GalleryDockSide.Right;
                if (cfg.DesktopFixedEnforceDockSide)
                    return GalleryDockLayout.Parse(cfg.DesktopFixedEnforcedDockSide);
                return GalleryDockLayout.Parse(cfg.DesktopFixedDockSide);
            }
        }

        internal string EffectiveDockSideString
        {
            get { return GalleryDockLayout.ToConfigString(EffectiveDockSide); }
        }

        internal bool IsDockedLeftSide
        {
            get { return EffectiveDockSide == GalleryDockSide.Left; }
        }

        internal bool IsDockedTopSide
        {
            get { return EffectiveDockSide == GalleryDockSide.Top; }
        }

        internal bool IsDockedRightSide
        {
            get { return EffectiveDockSide == GalleryDockSide.Right; }
        }

        /// <summary>Slot backing this pane's dock chrome. Null only when config is unavailable.</summary>
        internal GalleryDockSlot EffectiveDockSlot
        {
            get
            {
                GalleryDockSlot s = GalleryDockLayout.Slot(EffectiveDockSide);
                if (s != null) return s;
                return VPBConfig.Instance != null ? VPBConfig.Instance.ActiveDockSlot : null;
            }
        }

        internal int DockHeightMode
        {
            get
            {
                GalleryDockSlot s = EffectiveDockSlot;
                return s != null ? s.HeightMode : 0;
            }
            set
            {
                GalleryDockSlot s = EffectiveDockSlot;
                if (s == null || s.HeightMode == value) return;
                s.HeightMode = value;
                GalleryDockLayout.BumpVersion();
                MarkSessionArrangementDirty(false);
            }
        }

        internal bool DockAutoHide
        {
            get
            {
                GalleryDockSlot s = EffectiveDockSlot;
                return s != null && s.AutoHide;
            }
            set
            {
                GalleryDockSlot s = EffectiveDockSlot;
                if (s == null || s.AutoHide == value) return;
                s.AutoHide = value;
                GalleryDockLayout.BumpVersion();
                MarkSessionArrangementDirty(false);
            }
        }

        internal float DockWidthFree
        {
            get
            {
                GalleryDockSlot s = EffectiveDockSlot;
                return s != null ? s.WidthFree : GalleryUiDesignTokens.GoldenRatioMajor;
            }
            set
            {
                GalleryDockSlot s = EffectiveDockSlot;
                if (s == null || Mathf.Abs(s.WidthFree - value) < 0.0001f) return;
                s.WidthFree = value;
                GalleryDockLayout.BumpVersion();
                MarkSessionArrangementDirty(false);
            }
        }

        internal float DockCustomHeight
        {
            get
            {
                GalleryDockSlot s = EffectiveDockSlot;
                return s != null ? s.CustomHeight : 0.5f;
            }
            set
            {
                GalleryDockSlot s = EffectiveDockSlot;
                if (s == null || Mathf.Abs(s.CustomHeight - value) < 0.0001f) return;
                s.CustomHeight = value;
                GalleryDockLayout.BumpVersion();
                MarkSessionArrangementDirty(false);
            }
        }

        /// <summary>Distinct sort order per edge so an expanded dock's popups draw above a neighbour's background where they overhang.</summary>
        internal void ApplyDockSortingOrder()
        {
            if (canvas == null) return;

            int order = DockBaseSortingOrder;
            if (isFixedLocally)
            {
                GalleryDockSide side = EffectiveDockSide;
                if (side == GalleryDockSide.Right) order = DockBaseSortingOrder + 1;
                else if (side == GalleryDockSide.Top) order = DockBaseSortingOrder + 2;
            }
            if (canvas.sortingOrder != order) canvas.sortingOrder = order;
        }

        internal const int DockBaseSortingOrder = -10000;

        /// <summary>Writes this pane's dock rect straight onto the background, before the Update loop runs.</summary>
        internal void ApplyDockAnchorsImmediate()
        {
            ApplyDockSortingOrder();
            if (backgroundBoxGO == null) return;

            RectTransform bgRT = backgroundBoxGO.GetComponent<RectTransform>();
            if (bgRT == null) return;

            GalleryDockSide side = EffectiveDockSide;
            Vector2 dockMin, dockMax;
            if (GalleryDockLayout.TryGetRect(side, out dockMin, out dockMax))
            {
                bgRT.anchorMin = dockMin;
                bgRT.anchorMax = dockMax;
            }
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            if (!isCollapsed) return;

            float w = bgRT.rect.width > 0 ? bgRT.rect.width : 1200f;
            float h = bgRT.rect.height > 0 ? bgRT.rect.height : 800f;
            if (side == GalleryDockSide.Left)
                bgRT.anchoredPosition = new Vector2(-w, 0f);
            else if (side == GalleryDockSide.Top)
                bgRT.anchoredPosition = new Vector2(0f, h);
            else
                bgRT.anchoredPosition = new Vector2(w, 0f);
        }

        internal static float ClampDockWidthFreeAgainstOpposite(GalleryDockSide side, float widthFree)
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return widthFree;

            GalleryDockSide opposite = side == GalleryDockSide.Left ? GalleryDockSide.Right : GalleryDockSide.Left;
            GalleryDockSlot other = cfg.DockSlotFor(opposite);
            if (other == null || !other.Occupied) return widthFree;

            float otherWidth = 1f - other.WidthFree;
            float maxOwn = GalleryDockLayout.MaxSideWidthSum - otherWidth;
            if (maxOwn < GalleryDockLayout.MinSideWidth) maxOwn = GalleryDockLayout.MinSideWidth;

            float own = 1f - widthFree;
            if (own > maxOwn) own = maxOwn;
            if (own < GalleryDockLayout.MinSideWidth) own = GalleryDockLayout.MinSideWidth;
            return 1f - own;
        }

        internal GalleryDockSide ClaimDockSide(GalleryDockSide preferred)
        {
            GalleryDockSide side = GalleryDockLayout.FirstFreeSide(preferred, PanelId);
            if (side == GalleryDockSide.None) return GalleryDockSide.None;
            if (!GalleryDockLayout.TryClaim(side, PanelId)) return GalleryDockSide.None;
            InvalidateDockSideCache();
            return side;
        }

        internal void ReleaseDockSide()
        {
            GalleryDockLayout.Release(PanelId);
            InvalidateDockSideCache();
        }

        internal void ReleaseDockSideByUser()
        {
            GalleryDockLayout.ReleaseByUser(PanelId);
            InvalidateDockSideCache();
        }

        internal GalleryDockSide ResolvePreferredDockSide(string hintOrNull)
        {
            VPBConfig cfg = VPBConfig.Instance;
            if (cfg == null) return GalleryDockSide.Right;

            try
            {
                if (cfg.DesktopFixedEnforceDockSide)
                    return GalleryDockLayout.Parse(cfg.DesktopFixedEnforcedDockSide);
            }
            catch { }

            if (!string.IsNullOrEmpty(hintOrNull))
                return GalleryDockLayout.Parse(hintOrNull);

            return GalleryDockLayout.Parse(cfg.DesktopFixedDefaultDockSide);
        }

        internal void DockPaneToSide(GalleryDockSide side)
        {
            if (VPBConfig.Instance == null) return;
            if (side == GalleryDockSide.None) return;
            bool isVR = false;
            try { isVR = XrUtils.IsVrActive(); } catch { }
            if (isVR) return;

            if (isFixedLocally && EffectiveDockSide == side)
                return;

            string sideName = GalleryDockLayout.ToConfigString(side);

            if (!GalleryDockLayout.IsFreeFor(side, PanelId))
            {
                ShowDockSideTakenStatus(sideName);
                return;
            }

            if (isFixedLocally)
            {
                if (!GalleryDockLayout.TryMove(PanelId, side)) return;
                InvalidateDockSideCache();
                PersistMovedDockSide(sideName);
                return;
            }

            if (!GalleryDockLayout.TryClaim(side, PanelId))
            {
                ShowDockSideTakenStatus(sideName);
                return;
            }
            InvalidateDockSideCache();
            isFixedLocally = true;
            VPBConfig.Instance.DesktopFixedMode = true;
            VPBConfig.Instance.DesktopFixedDockSide = sideName;
            VPBConfig.Instance.Save();
            UpdateDockAnchorButton();
            try { UpdateSpringScrollButtonToggleUI(); } catch { }
            InvalidateFooterOverflowLayout();
            MarkGalleryPaneChromeDirty();
            UpdateLayout();
            try { SyncCategoryQuickSwitchChrome(); } catch { }
        }

        private void ShowDockSideTakenStatus(string sideName)
        {
            ShowTemporaryStatus(string.Format(
                VPBTranslation.T("gallery.status.dock_side_taken",
                    "The {0} edge is in use — undock that pane first."), sideName), 2f);
        }

        private void PersistMovedDockSide(string nextName)
        {
            bool enforce = false;
            try { enforce = VPBConfig.Instance.DesktopFixedEnforceDockSide; } catch { enforce = false; }
            if (enforce) VPBConfig.Instance.DesktopFixedEnforcedDockSide = nextName;
            VPBConfig.Instance.DesktopFixedDockSide = nextName;
            VPBConfig.Instance.Save(true, true);
            UpdateFooterDockButtonState();
            UpdateFooterAutoHideState();
            InvalidateFooterOverflowLayout();
            MarkGalleryPaneChromeDirty();
            UpdateLayout();
            try { SyncCategoryQuickSwitchChrome(); } catch { }
        }

        private int _triggerBandVersion = -1;

        private void SyncCollapseTriggerBands()
        {
            int v = GalleryDockLayout.Version;
            if (_triggerBandVersion == v) return;
            _triggerBandVersion = v;

            RectTransform topRT = GetCachedCollapseTriggerRT(collapseTriggerTopGO, ref _collapseTriggerTopRT);
            if (topRT != null)
            {
                float min, max;
                GalleryDockLayout.TopTriggerBand(out min, out max);
                topRT.anchorMin = new Vector2(min, 1f);
                topRT.anchorMax = new Vector2(max, 1f);
                topRT.anchoredPosition = Vector2.zero;
            }

            float sMin, sMax;
            GalleryDockLayout.SideTriggerBand(out sMin, out sMax);

            RectTransform leftRT = GetCachedCollapseTriggerRT(collapseTriggerLeftGO, ref _collapseTriggerLeftRT);
            if (leftRT != null)
            {
                leftRT.anchorMin = new Vector2(0f, sMin);
                leftRT.anchorMax = new Vector2(0f, sMax);
                leftRT.anchoredPosition = Vector2.zero;
            }

            RectTransform rightRT = GetCachedCollapseTriggerRT(collapseTriggerGO, ref _collapseTriggerRT);
            if (rightRT != null)
            {
                rightRT.anchorMin = new Vector2(1f, sMin);
                rightRT.anchorMax = new Vector2(1f, sMax);
                rightRT.anchoredPosition = Vector2.zero;
            }
        }
    }
}
