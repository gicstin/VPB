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

            // Hide-creator setting wins over rail mode — enforce first, then show only if off.
            ApplyCreatorSideRailButtonVisibility(showLeftSide, showRightSide);

            // Keep History side buttons on the same purple family as active side-tab buttons.
            Color historyBackdrop = ColorHistoryAccent;
            if (rightHistoryBtnImage != null) rightHistoryBtnImage.color = historyBackdrop;
            if (leftHistoryBtnImage != null) leftHistoryBtnImage.color = historyBackdrop;
            if (rightHistoryBtnIconImage != null) rightHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;
            if (leftHistoryBtnIconImage != null) leftHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;
        }

        private static bool HideCreatorSideRailButtonsRequested()
        {
            return VPBConfig.Instance != null && VPBConfig.Instance.GalleryHideCreatorSideButtons;
        }

        /// <summary>
        /// Creator rail buttons: hide setting wins — destroy buttons (not just SetActive) so layout/top-dock
        /// cannot resurrect them when a side pane unhides. Only when setting is off: ensure exist, then show
        /// per rail mode.
        /// </summary>
        private void ApplyCreatorSideRailButtonVisibility(bool showLeftSide, bool showRightSide)
        {
            if (HideCreatorSideRailButtonsRequested())
            {
                bool cleared = false;
                if (leftActiveContent == ContentType.Creator) { leftActiveContent = null; cleared = true; }
                if (rightActiveContent == ContentType.Creator) { rightActiveContent = null; cleared = true; }
                if (cleared)
                {
                    try { SyncActiveContentTypeFromSidePanels(); } catch { }
                }
                DestroyCreatorSideRailButton(isLeft: true);
                DestroyCreatorSideRailButton(isLeft: false);
                return;
            }

            EnsureCreatorSideRailButtonsExist();
            if (leftCreatorSideBtnGO != null && leftCreatorSideBtnGO.activeSelf != showLeftSide)
                leftCreatorSideBtnGO.SetActive(showLeftSide);
            if (rightCreatorSideBtnGO != null && rightCreatorSideBtnGO.activeSelf != showRightSide)
                rightCreatorSideBtnGO.SetActive(showRightSide);
        }

        /// <summary>Re-apply creator hide using current ShowSideButtons / dock / collapsed (layout paths).</summary>
        private void EnforceCreatorSideRailButtonVisibilityFromConfig()
        {
            if (VPBConfig.Instance == null) return;
            string mode = VPBConfig.Instance.ShowSideButtons;
            bool fixedMode = isFixedLocally;
            string dock = "Right";
            try { dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide); } catch { dock = "Right"; }

            bool showLeftSide = !isCollapsed && (mode == "Both" || mode == "Left");
            if (fixedMode && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) showLeftSide = false;

            bool showRightSide = !isCollapsed && (mode == "Both" || mode == "Right");
            if (fixedMode && !string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) showRightSide = false;

            ApplyCreatorSideRailButtonVisibility(showLeftSide, showRightSide);
        }

        private void DestroyCreatorSideRailButton(bool isLeft)
        {
            GameObject go = isLeft ? leftCreatorSideBtnGO : rightCreatorSideBtnGO;
            List<RectTransform> list = isLeft ? leftSideButtons : rightSideButtons;
            if (go != null)
            {
                if (list != null)
                {
                    RectTransform rt = go.GetComponent<RectTransform>();
                    if (rt != null) list.Remove(rt);
                }
                try { UnityEngine.Object.Destroy(go); } catch { }
            }
            if (isLeft)
            {
                leftCreatorSideBtnGO = null;
                leftCreatorBtnImage = null;
                leftCreatorBtnText = null;
                leftCreatorBtnIconImage = null;
            }
            else
            {
                rightCreatorSideBtnGO = null;
                rightCreatorBtnImage = null;
                rightCreatorBtnText = null;
                rightCreatorBtnIconImage = null;
            }
        }

        private void EnsureCreatorSideRailButtonsExist()
        {
            if (HideCreatorSideRailButtonsRequested()) return;
            bool created = false;
            if (leftCreatorSideBtnGO == null && leftSideContainer != null)
            {
                CreateLeftCreatorSideRailButton();
                created = true;
            }
            if (rightCreatorSideBtnGO == null && rightSideContainer != null)
            {
                CreateRightCreatorSideRailButton();
                created = true;
            }
            if (created)
            {
                try { ApplySideButtonScale(); } catch { }
            }
        }

        private void InsertCreatorSideButtonIntoList(List<RectTransform> list, RectTransform creatorRt, GameObject afterGo, Text pathBtnText)
        {
            if (list == null || creatorRt == null) return;
            if (list.Contains(creatorRt)) return;
            int insertAt = -1;
            if (afterGo != null)
            {
                int utIdx = list.FindIndex(rt => rt != null && rt.gameObject == afterGo);
                if (utIdx >= 0) insertAt = utIdx + 1;
            }
            if (insertAt < 0 && pathBtnText != null)
            {
                int pathIdx = list.FindIndex(rt => rt != null && rt.GetComponentInChildren<Text>(true) == pathBtnText);
                if (pathIdx >= 0) insertAt = pathIdx;
            }
            if (insertAt >= 0 && insertAt <= list.Count) list.Insert(insertAt, creatorRt);
            else list.Add(creatorRt);
        }

        private void CreateLeftCreatorSideRailButton()
        {
            if (leftSideContainer == null || leftCreatorSideBtnGO != null) return;
            float btnWidth = GalleryUiDesignTokens.SideButtonWidthRef;
            float btnHeight = GalleryUiDesignTokens.SideButtonHeightRef;
            float sideIconBtn = GalleryUiDesignTokens.SideButtonSquareRef;
            const float sideIconPad = 6f;
            int btnFontSize = GalleryUiDesignTokens.FontBodyRef;
            float crW = galleryCreatorSprite != null ? sideIconBtn : btnWidth;
            float crH = galleryCreatorSprite != null ? sideIconBtn : btnHeight;

            GameObject leftCreatorBtn = UI.CreateUIButton(leftSideContainer, crW, crH, " ", 8, 0, 0, AnchorPresets.centre, () => ToggleLeft(ContentType.Creator));
            leftCreatorSideBtnGO = leftCreatorBtn;
            leftCreatorBtnImage = leftCreatorBtn.GetComponent<Image>();
            leftCreatorBtnText = leftCreatorBtn.GetComponentInChildren<Text>(true);
            if (galleryCreatorSprite != null)
            {
                UI.AddIconToButton(leftCreatorBtn, galleryCreatorSprite, sideIconPad, ColorCreator);
                leftCreatorBtnIconImage = leftCreatorBtn.transform.Find("Icon") != null
                    ? leftCreatorBtn.transform.Find("Icon").GetComponent<Image>() : null;
            }
            else
            {
                if (leftCreatorBtnImage != null) leftCreatorBtnImage.color = ColorCreator;
                if (leftCreatorBtnText != null)
                {
                    leftCreatorBtnText.text = VPBTranslation.T("gallery.side.creator", "Creators");
                    leftCreatorBtnText.fontSize = btnFontSize;
                    leftCreatorBtnText.gameObject.SetActive(true);
                }
                leftCreatorBtnIconImage = null;
            }
            InsertCreatorSideButtonIntoList(leftSideButtons, leftCreatorBtn.GetComponent<RectTransform>(), leftUserTagsSideBtn, leftPathBtnText);
            AddRightClickDelegate(leftCreatorBtn, () => ToggleRight(ContentType.Creator));
            AddTooltip(leftCreatorBtn, "gallery.tooltip.creator_list", "Browse creators (side list). Title bar filters the grid.");
        }

        private void CreateRightCreatorSideRailButton()
        {
            if (rightSideContainer == null || rightCreatorSideBtnGO != null) return;
            float btnWidth = GalleryUiDesignTokens.SideButtonWidthRef;
            float btnHeight = GalleryUiDesignTokens.SideButtonHeightRef;
            float sideIconBtn = GalleryUiDesignTokens.SideButtonSquareRef;
            const float sideIconPad = 6f;
            int btnFontSize = GalleryUiDesignTokens.FontBodyRef;
            float crW = galleryCreatorSprite != null ? sideIconBtn : btnWidth;
            float crH = galleryCreatorSprite != null ? sideIconBtn : btnHeight;

            GameObject rightCreatorBtn = UI.CreateUIButton(rightSideContainer, crW, crH, " ", 8, 0, 0, AnchorPresets.centre, () => {
                if (isFixedLocally) ToggleLeft(ContentType.Creator); else ToggleRight(ContentType.Creator);
            });
            rightCreatorSideBtnGO = rightCreatorBtn;
            rightCreatorBtnImage = rightCreatorBtn.GetComponent<Image>();
            rightCreatorBtnText = rightCreatorBtn.GetComponentInChildren<Text>(true);
            if (galleryCreatorSprite != null)
            {
                UI.AddIconToButton(rightCreatorBtn, galleryCreatorSprite, sideIconPad, ColorCreator);
                rightCreatorBtnIconImage = rightCreatorBtn.transform.Find("Icon") != null
                    ? rightCreatorBtn.transform.Find("Icon").GetComponent<Image>() : null;
            }
            else
            {
                if (rightCreatorBtnImage != null) rightCreatorBtnImage.color = ColorCreator;
                if (rightCreatorBtnText != null)
                {
                    rightCreatorBtnText.text = VPBTranslation.T("gallery.side.creator", "Creators");
                    rightCreatorBtnText.fontSize = btnFontSize;
                    rightCreatorBtnText.gameObject.SetActive(true);
                }
                rightCreatorBtnIconImage = null;
            }
            InsertCreatorSideButtonIntoList(rightSideButtons, rightCreatorBtn.GetComponent<RectTransform>(), rightUserTagsSideBtn, rightPathBtnText);
            AddRightClickDelegate(rightCreatorBtn, () => ToggleRight(ContentType.Creator));
            AddTooltip(rightCreatorBtn, "gallery.tooltip.creator_list", "Browse creators (side list). Title bar filters the grid.");
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
