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
            try { PersistCurrentBrowsePlace(); } catch { }
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

            // Hide-creator setting + rail mode. Never Destroy/recreate here (deferred Destroy left centre-anchored orphans).
            ApplyCreatorSideRailButtonVisibility();

            // Keep History side buttons on the same purple family as active side-tab buttons.
            Color historyBackdrop = ColorHistoryAccent;
            if (rightHistoryBtnImage != null) rightHistoryBtnImage.color = historyBackdrop;
            if (leftHistoryBtnImage != null) leftHistoryBtnImage.color = historyBackdrop;
            if (rightHistoryBtnIconImage != null) rightHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;
            if (leftHistoryBtnIconImage != null) leftHistoryBtnIconImage.color = UI.SideRailIconGlyphTint;
        }

        private const string CreatorSideRailBtnNameLeft = "VPB_SideRail_Creator_L";
        private const string CreatorSideRailBtnNameRight = "VPB_SideRail_Creator_R";

        private static bool HideCreatorSideRailButtonsRequested()
        {
            return VPBConfig.Instance != null && VPBConfig.Instance.GalleryHideCreatorSideButtons;
        }

        private bool IsCreatorSideRailButtonGO(GameObject go)
        {
            if (go == null) return false;
            if (leftCreatorSideBtnGO != null && go == leftCreatorSideBtnGO) return true;
            if (rightCreatorSideBtnGO != null && go == rightCreatorSideBtnGO) return true;
            string n = go.name;
            return n == CreatorSideRailBtnNameLeft || n == CreatorSideRailBtnNameRight;
        }

        /// <summary>
        /// Left/right creator chip wanted-active for current dock / ShowSideButtons / collapsed.
        /// Hide-creator setting handled separately (always force off).
        /// </summary>
        private void ComputeCreatorSideRailShowFlags(out bool showLeft, out bool showRight)
        {
            showLeft = false;
            showRight = false;
            if (VPBConfig.Instance == null || isCollapsed) return;

            string mode = VPBConfig.Instance.ShowSideButtons ?? "Both";
            bool fixedMode = isFixedLocally;
            string dock = "Right";
            try { dock = VPBConfig.NormalizeDesktopFixedDockSide(VPBConfig.Instance.DesktopFixedDockSide); } catch { dock = "Right"; }
            bool topDock = fixedMode && string.Equals(dock, "Top", StringComparison.OrdinalIgnoreCase);

            // Top dock reparents leftSideButtons into footer — only left creator participates.
            if (topDock)
            {
                showLeft = true;
                showRight = false;
                return;
            }

            showLeft = mode == "Both" || mode == "Left";
            if (fixedMode && string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) showLeft = false;

            showRight = mode == "Both" || mode == "Right";
            if (fixedMode && !string.Equals(dock, "Left", StringComparison.OrdinalIgnoreCase)) showRight = false;
        }

        /// <summary>
        /// Creator rail buttons: one lifetime from Init. Hide setting + rail mode use SetActive only.
        /// Destroy/recreate caused duplicate centre-anchored ghosts (Unity Destroy is end-of-frame).
        /// Layout omits chips when hide setting on; overflow must not resurrect them.
        /// </summary>
        private void ApplyCreatorSideRailButtonVisibility()
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
                EnsureCreatorSideRailButtonsExist();
                SetCreatorSideRailButtonActive(leftCreatorSideBtnGO, false);
                SetCreatorSideRailButtonActive(rightCreatorSideBtnGO, false);
                return;
            }

            EnsureCreatorSideRailButtonsExist();
            bool showLeft;
            bool showRight;
            ComputeCreatorSideRailShowFlags(out showLeft, out showRight);
            SetCreatorSideRailButtonActive(leftCreatorSideBtnGO, showLeft);
            SetCreatorSideRailButtonActive(rightCreatorSideBtnGO, showRight);
        }

        private static void SetCreatorSideRailButtonActive(GameObject go, bool active)
        {
            if (go == null) return;
            if (go.activeSelf != active) go.SetActive(active);
        }

        /// <summary>Re-apply creator hide using current ShowSideButtons / dock / collapsed (layout paths).</summary>
        private void EnforceCreatorSideRailButtonVisibilityFromConfig()
        {
            ApplyCreatorSideRailButtonVisibility();
        }

        /// <summary>Cold-path: kill deferred-Destroy orphans left at centre (0,0) beside edge-aligned rail.</summary>
        private bool LooksLikeOrphanCreatorSideButton(GameObject go, GameObject keep)
        {
            if (go == null || (keep != null && go == keep)) return false;
            string n = go.name;
            if (n == CreatorSideRailBtnNameLeft || n == CreatorSideRailBtnNameRight) return true;
            // Prior builds / pre-rename CreateUIButton: identify by creator icon sprite.
            if (galleryCreatorSprite == null) return false;
            Transform iconT = go.transform.Find("Icon");
            if (iconT == null) return false;
            Image iconImg = iconT.GetComponent<Image>();
            return iconImg != null && iconImg.sprite == galleryCreatorSprite;
        }

        private void PurgeOrphanCreatorSideRailButtons(Transform parent, GameObject keep)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform c = parent.GetChild(i);
                if (c == null) continue;
                GameObject go = c.gameObject;
                if (!LooksLikeOrphanCreatorSideButton(go, keep)) continue;
                // Never remove the live tracked chip if keep was null but refs already set elsewhere.
                if (leftCreatorSideBtnGO != null && go == leftCreatorSideBtnGO) continue;
                if (rightCreatorSideBtnGO != null && go == rightCreatorSideBtnGO) continue;
                try { UnityEngine.Object.DestroyImmediate(go); }
                catch
                {
                    try { UnityEngine.Object.Destroy(go); } catch { }
                }
            }
        }

        private void PurgeAllOrphanCreatorSideRailButtons()
        {
            PurgeOrphanCreatorSideRailButtons(leftSideContainer != null ? leftSideContainer.transform : null, leftCreatorSideBtnGO);
            PurgeOrphanCreatorSideRailButtons(rightSideContainer != null ? rightSideContainer.transform : null, rightCreatorSideBtnGO);
            PurgeOrphanCreatorSideRailButtons(_footerSideButtonsGroupRT, leftCreatorSideBtnGO);
        }

        private void AdoptCreatorSideButtonParent(GameObject go, bool isLeft)
        {
            if (go == null) return;
            if (isLeft && IsFixedTopDockMode() && _titleBarSideButtonsReparented && _footerSideButtonsGroupRT != null)
            {
                if (go.transform.parent != _footerSideButtonsGroupRT)
                    go.transform.SetParent(_footerSideButtonsGroupRT, worldPositionStays: false);
                return;
            }
            GameObject container = isLeft ? leftSideContainer : rightSideContainer;
            if (container == null) return;
            if (go.transform.parent != container.transform)
                go.transform.SetParent(container.transform, worldPositionStays: false);
        }

        private void EnsureCreatorSideRailButtonsExist()
        {
            // Unity fake-null: destroyed GO still referenced until cleared.
            if (leftCreatorSideBtnGO == null) leftCreatorSideBtnGO = null;
            if (rightCreatorSideBtnGO == null) rightCreatorSideBtnGO = null;

            bool needLeft = leftCreatorSideBtnGO == null && leftSideContainer != null;
            bool needRight = rightCreatorSideBtnGO == null && rightSideContainer != null;
            // Purge ghosts only when (re)binding or once per ensure-miss — avoid per-layout DestroyImmediate.
            if (needLeft || needRight)
            {
                try { PurgeAllOrphanCreatorSideRailButtons(); } catch { }
            }

            bool created = false;
            if (needLeft)
            {
                CreateLeftCreatorSideRailButton();
                created = true;
            }
            else if (leftCreatorSideBtnGO != null)
            {
                AdoptCreatorSideButtonParent(leftCreatorSideBtnGO, isLeft: true);
            }

            if (needRight)
            {
                CreateRightCreatorSideRailButton();
                created = true;
            }
            else if (rightCreatorSideBtnGO != null)
            {
                AdoptCreatorSideButtonParent(rightCreatorSideBtnGO, isLeft: false);
            }

            if (created)
            {
                _creatorSideRailOrphansPurged = false;
                try { ApplySideButtonScale(); } catch { }
            }
            if (!_creatorSideRailOrphansPurged)
            {
                try { PurgeAllOrphanCreatorSideRailButtons(); } catch { }
                _creatorSideRailOrphansPurged = true;
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
            PurgeOrphanCreatorSideRailButtons(leftSideContainer.transform, null);
            if (_footerSideButtonsGroupRT != null)
                PurgeOrphanCreatorSideRailButtons(_footerSideButtonsGroupRT, null);

            float btnWidth = GalleryUiDesignTokens.SideButtonWidthRef;
            float btnHeight = GalleryUiDesignTokens.SideButtonHeightRef;
            float sideIconBtn = GalleryUiDesignTokens.SideButtonSquareRef;
            const float sideIconPad = 6f;
            int btnFontSize = GalleryUiDesignTokens.FontBodyRef;
            float crW = galleryCreatorSprite != null ? sideIconBtn : btnWidth;
            float crH = galleryCreatorSprite != null ? sideIconBtn : btnHeight;

            GameObject leftCreatorBtn = UI.CreateUIButton(leftSideContainer, crW, crH, " ", 8, 0, 0, AnchorPresets.centre, () => ToggleSideFromRailButton(ContentType.Creator, true, false));
            leftCreatorBtn.name = CreatorSideRailBtnNameLeft;
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
            AdoptCreatorSideButtonParent(leftCreatorBtn, isLeft: true);
            AddRightClickDelegate(leftCreatorBtn, () => ToggleSideFromRailButton(ContentType.Creator, true, true));
            AddTooltip(leftCreatorBtn, "gallery.tooltip.creator_list", "Browse creators (side list). Title bar filters the grid.");
        }

        private void CreateRightCreatorSideRailButton()
        {
            if (rightSideContainer == null || rightCreatorSideBtnGO != null) return;
            PurgeOrphanCreatorSideRailButtons(rightSideContainer.transform, null);

            float btnWidth = GalleryUiDesignTokens.SideButtonWidthRef;
            float btnHeight = GalleryUiDesignTokens.SideButtonHeightRef;
            float sideIconBtn = GalleryUiDesignTokens.SideButtonSquareRef;
            const float sideIconPad = 6f;
            int btnFontSize = GalleryUiDesignTokens.FontBodyRef;
            float crW = galleryCreatorSprite != null ? sideIconBtn : btnWidth;
            float crH = galleryCreatorSprite != null ? sideIconBtn : btnHeight;

            GameObject rightCreatorBtn = UI.CreateUIButton(rightSideContainer, crW, crH, " ", 8, 0, 0, AnchorPresets.centre, () => {
                ToggleSideFromRailButton(ContentType.Creator, false, false);
            });
            rightCreatorBtn.name = CreatorSideRailBtnNameRight;
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
            AdoptCreatorSideButtonParent(rightCreatorBtn, isLeft: false);
            AddRightClickDelegate(rightCreatorBtn, () => ToggleSideFromRailButton(ContentType.Creator, false, true));
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

            // Creator Strip: clear static suppress + stop routine before canvas teardown.
            try
            {
                if (creatorModeStripRoutine != null)
                {
                    try { StopCoroutine(creatorModeStripRoutine); } catch { }
                    creatorModeStripRoutine = null;
                }
                creatorModeStripBusy = false;
                SuppressAtomRemovedGalleryNotify = false;
                if (_stripKeepSubScenePickActive)
                {
                    try { StripKeepAbortSubScenePickMode(reopenStrip: false); } catch { }
                }
                try { HideStripKeepSelectorInternal(resetSession: true); } catch { }
                creatorModeActive = false;
            }
            catch { }

            RemoveModeDestroyPopup();

            _gridHoverBadgeBtnGO = null;
            _hoverPathRevealOwner = null;

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
