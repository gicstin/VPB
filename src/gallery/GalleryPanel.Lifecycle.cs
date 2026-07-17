using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
{
        public void Close()
        {
            VpbPerfDiag.LogTransition("GalleryPanel.Close", null);
            if (Gallery.singleton != null)
            {
                Gallery.singleton.RemovePanel(this);
            }

            if (canvas != null)
            {
                if (SuperController.singleton != null) SuperController.singleton.RemoveCanvas(canvas);
                Destroy(canvas.gameObject);
            }

            Destroy(this.gameObject);
        }

        private void StopCo(ref Coroutine co)
        {
            if (co == null) return;
            StopCoroutine(co);
            co = null;
        }

        private void UpdateSideButtonsVisibility()
        {
            if (VPBConfig.Instance == null) return;
            string mode = VPBConfig.Instance.ShowSideButtons;
            bool fixedMode = isFixedLocally;
            string dock = "Right";
            try { dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide); } catch { dock = "Right"; }
            bool topDock = fixedMode && string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase);

            if (leftSideContainer != null) 
            {
                if (isCollapsed || topDock) leftSideContainer.SetActive(false);
                else if (fixedMode && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase))
                    leftSideContainer.SetActive(false);
                else
                    leftSideContainer.SetActive(mode == "Both" || mode == "Left");
            }
            
            if (rightSideContainer != null) 
            {
                if (isCollapsed || topDock) rightSideContainer.SetActive(false);
                else if (fixedMode)
                    rightSideContainer.SetActive(string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase) && (mode == "Both" || mode == "Right"));
                else
                    rightSideContainer.SetActive(mode == "Both" || mode == "Right");
            }

            bool showLeftSide = !isCollapsed && (mode == "Both" || mode == "Left");
            if (fixedMode && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) showLeftSide = false;

            bool showRightSide = !isCollapsed && (mode == "Both" || mode == "Right");
            if (fixedMode && !string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) showRightSide = false;

            bool hideCreatorRail = VPBConfig.Instance.GalleryHideCreatorSideButtons;
            if (hideCreatorRail)
            {
                bool cleared = false;
                if (leftActiveContent == ContentType.Creator) { leftActiveContent = null; cleared = true; }
                if (rightActiveContent == ContentType.Creator) { rightActiveContent = null; cleared = true; }
                if (cleared) SyncActiveContentTypeFromSidePanels();
            }

            bool showLeftCreatorBtn = showLeftSide && !hideCreatorRail;
            bool showRightCreatorBtn = showRightSide && !hideCreatorRail;
            if (leftCreatorBtnImage != null && leftCreatorBtnImage.gameObject.activeSelf != showLeftCreatorBtn)
                leftCreatorBtnImage.gameObject.SetActive(showLeftCreatorBtn);
            if (rightCreatorBtnImage != null && rightCreatorBtnImage.gameObject.activeSelf != showRightCreatorBtn)
                rightCreatorBtnImage.gameObject.SetActive(showRightCreatorBtn);

            // Keep History side buttons on the same purple family as active side-tab buttons.
            Color historyBackdrop = ColorHistoryAccent;
            if (rightHistoryBtnImage != null) rightHistoryBtnImage.color = historyBackdrop;
            if (leftHistoryBtnImage != null) leftHistoryBtnImage.color = historyBackdrop;
            if (rightHistoryBtnIconImage != null) rightHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;
            if (leftHistoryBtnIconImage != null) leftHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;
        }

        private void AddHoverDelegate(GameObject go)
        {
            if (go == null) return;

            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += (enter) => {
                if (enter) hoverCount++;
                else hoverCount--;
                if (hoverCount < 0) hoverCount = 0;
            };
            del.OnPointerEnterEvent += (d) => {
                currentPointerData = d;
            };
        }

        private void AddRightClickDelegate(GameObject go, Action action)
        {
            var del = go.AddComponent<UIRightClickDelegate>();
            del.OnRightClick = action;
        }

        /// <summary>
        /// Single place for gallery panel <see cref="VPBConfig.ConfigChanged"/> wiring.
        /// REGRESSION GUARD: never subscribe <see cref="UpdateTabs"/> here — it repopulates O(n) side-tab buttons and freezes the UI on every Save/TriggerChange.
        /// </summary>
        private void SubscribeGalleryPanelToVpBConfigChanged()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.ConfigChanged += ApplyInnerPaneScale;
            VPBConfig.Instance.ConfigChanged += UpdateSideButtonsVisibility;
            VPBConfig.Instance.ConfigChanged += UpdateFooterFollowStates;
            VPBConfig.Instance.ConfigChanged += UpdateDesktopModeButton;
            VPBConfig.Instance.ConfigChanged += UpdateLayout;
            VPBConfig.Instance.ConfigChanged += RefreshSideTabAreasForConfigChange;
            VPBConfig.Instance.ConfigChanged += ApplyVamMenuGateVisibility;
            VPBConfig.Instance.ConfigChanged += RefreshCategoryQuickSwitchOnConfigChanged;
            VPBConfig.Instance.ConfigChanged += OnGalleryTransparencyConfigChanged;
            VPBConfig.Instance.ConfigChanged += ApplySpringScrollButtonFromConfig;
            VPBConfig.Instance.ConfigChanged += SyncTboxPinnedFromConfig;
        }

        private void UnsubscribeGalleryPanelFromVpBConfigChanged()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.ConfigChanged -= ApplyInnerPaneScale;
            VPBConfig.Instance.ConfigChanged -= UpdateSideButtonsVisibility;
            VPBConfig.Instance.ConfigChanged -= UpdateFooterFollowStates;
            VPBConfig.Instance.ConfigChanged -= UpdateDesktopModeButton;
            VPBConfig.Instance.ConfigChanged -= UpdateLayout;
            VPBConfig.Instance.ConfigChanged -= RefreshSideTabAreasForConfigChange;
            VPBConfig.Instance.ConfigChanged -= ApplyVamMenuGateVisibility;
            VPBConfig.Instance.ConfigChanged -= RefreshCategoryQuickSwitchOnConfigChanged;
            VPBConfig.Instance.ConfigChanged -= OnGalleryTransparencyConfigChanged;
            VPBConfig.Instance.ConfigChanged -= ApplySpringScrollButtonFromConfig;
            VPBConfig.Instance.ConfigChanged -= SyncTboxPinnedFromConfig;
        }

        private void OnGalleryTransparencyConfigChanged()
        {
            try { ApplyGalleryTransparencyVisuals(); } catch { }
        }

        void OnDestroy()
        {
            StopCo(ref _categoryQuickApplyCoroutine);

            RemoveModeDestroyPopup();

            _gridHoverBadgeBtnGO = null;
            _gridHoverBadgeFile = null;

            // Re-enable saving on teardown so the cache isn't left permanently paused.
            if (GalleryThumbnailCache.Instance != null)
                GalleryThumbnailCache.Instance.SavingPaused = false;

            UnsubscribeLocaleChanged();

            UnsubscribeGalleryPanelFromVpBConfigChanged();

            UnsubscribeFromAtomEvents();

            if (canvas != null)
            {
                if (SuperController.singleton != null)
                {
                    SuperController.singleton.RemoveCanvas(canvas);
                }
                Destroy(canvas.gameObject);
            }
            // Remove from manager if needed
            if (Gallery.singleton != null)
            {
                Gallery.singleton.RemovePanel(this);
            }

        }

    }

}
