using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Side-rail height fit: close zone/group gaps first, then collapse chips into “…”.

        private GameObject _leftSideRailOverflowBtnGO;
        private RectTransform _leftSideRailOverflowBtnRT;
        private GameObject _rightSideRailOverflowBtnGO;
        private RectTransform _rightSideRailOverflowBtnRT;
        private GameObject _sideRailOverflowMenuGO;
        private bool _sideRailOverflowOpen;
        private bool _sideRailOverflowOpenedFromLeft;
        private readonly List<int> _sideRailOverflowCollapsedIdx = new List<int>(16);
        private float _sideRailFitSpacing = GalleryUiDesignTokens.SideButtonSpacingRef;
        private float _sideRailFitGroupGap;
        private float _sideRailLastBottomInset = -1f;

        private void EnsureSideRailOverflowChrome()
        {
            if (_sideRailOverflowMenuGO != null) return;

            if (leftSideContainer != null && _leftSideRailOverflowBtnGO == null)
            {
                _leftSideRailOverflowBtnGO = UI.CreateUIButton(
                    leftSideContainer,
                    GalleryUiDesignTokens.SideButtonSquareRef,
                    GalleryUiDesignTokens.SideButtonSquareRef,
                    "\u2026",
                    GalleryUiDesignTokens.TitleBarOverflowFontRef,
                    0, 0,
                    AnchorPresets.middleCenter,
                    () => ToggleSideRailOverflowMenu(true));
                _leftSideRailOverflowBtnGO.name = "LeftSideRailOverflowBtn";
                _leftSideRailOverflowBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
                _leftSideRailOverflowBtnRT = _leftSideRailOverflowBtnGO.GetComponent<RectTransform>();
                _leftSideRailOverflowBtnGO.SetActive(false);
                AddTooltip(_leftSideRailOverflowBtnGO, "gallery.side.overflow", "More side actions");
                AddHoverDelegate(_leftSideRailOverflowBtnGO);
            }

            if (rightSideContainer != null && _rightSideRailOverflowBtnGO == null)
            {
                _rightSideRailOverflowBtnGO = UI.CreateUIButton(
                    rightSideContainer,
                    GalleryUiDesignTokens.SideButtonSquareRef,
                    GalleryUiDesignTokens.SideButtonSquareRef,
                    "\u2026",
                    GalleryUiDesignTokens.TitleBarOverflowFontRef,
                    0, 0,
                    AnchorPresets.middleCenter,
                    () => ToggleSideRailOverflowMenu(false));
                _rightSideRailOverflowBtnGO.name = "RightSideRailOverflowBtn";
                _rightSideRailOverflowBtnGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
                _rightSideRailOverflowBtnRT = _rightSideRailOverflowBtnGO.GetComponent<RectTransform>();
                _rightSideRailOverflowBtnGO.SetActive(false);
                AddTooltip(_rightSideRailOverflowBtnGO, "gallery.side.overflow", "More side actions");
                AddHoverDelegate(_rightSideRailOverflowBtnGO);
            }

            if (backgroundBoxGO == null) return;
            _sideRailOverflowMenuGO = UI.CreatePopupMenuRoot(backgroundBoxGO, "SideRailOverflowMenu", CloseSideRailOverflowMenu);
            _sideRailOverflowMenuGO.SetActive(false);
            UI.CreatePopupMenuPanel(
                _sideRailOverflowMenuGO, "OverflowMenuPanel",
                AnchorPresets.middleLeft,
                new Vector2(GalleryUiDesignTokens.OverflowMenuPanelWidthRef, 50f),
                Vector2.zero);
        }

        private float GetSideRailAvailableCenterSpan(float scale)
        {
            if (backgroundBoxGO == null) return 400f;
            RectTransform bg = backgroundBoxGO.GetComponent<RectTransform>();
            if (bg == null) return 400f;
            float h = bg.rect.height;
            if (h < 8f) return 400f;
            if (scale <= 0f) scale = 1f;
            // Stable pane chrome only — NOT live InfoBar/detail-strip height.
            // Detail-strip resize grows the toolbox into the grid; window height is unchanged.
            // Using GalleryMainAreaBottomInset() here squeezed rail gaps / overflow as the strip grew.
            float topChrome = (GalleryUiDesignTokens.TitleBarHeightRef + 20f) * scale;
            float bottomChrome = (GalleryUiDesignTokens.FooterBarHeightRef
                + GalleryUiDesignTokens.FooterToolboxTopRef
                + 20f) * scale;
            float btnH = GalleryUiDesignTokens.SideButtonSquareRef * scale;
            // Room for first↔last center span (total stack extent is span + btnH).
            return Mathf.Max(btnH, h - topChrome - bottomChrome - btnH);
        }

        private int CountVisibleSideRailButtons()
        {
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            int n = 0;
            for (int i = 0; i < layout.Length; i++)
            {
                if (!TryGetSideRailButtonPair(layout[i].buttonIndex, out RectTransform left, out RectTransform right))
                    continue;
                GameObject go = left != null ? left.gameObject : (right != null ? right.gameObject : null);
                if (go == null || !go.activeSelf) continue;
                if (_sideRailOverflowCollapsedIdx.Contains(layout[i].buttonIndex)) continue;
                n++;
            }
            return n;
        }

        private bool TryGetSideRailButtonPair(int idx, out RectTransform left, out RectTransform right)
        {
            left = null;
            right = null;
            if (idx < 0) return false;
            if (leftSideButtons != null && idx < leftSideButtons.Count)
                left = leftSideButtons[idx];
            if (rightSideButtons != null && idx < rightSideButtons.Count)
                right = rightSideButtons[idx];
            return left != null || right != null;
        }

        private void SetSideRailButtonIndexActive(int idx, bool active)
        {
            if (!TryGetSideRailButtonPair(idx, out RectTransform left, out RectTransform right)) return;
            // Hide-creator setting must win over overflow restore (SetActive true).
            bool hideCreator = HideCreatorSideRailButtonsRequested();
            if (left != null)
            {
                bool want = active;
                if (want && hideCreator && IsCreatorSideRailButtonGO(left.gameObject)) want = false;
                if (left.gameObject.activeSelf != want) left.gameObject.SetActive(want);
            }
            if (right != null)
            {
                bool want = active;
                if (want && hideCreator && IsCreatorSideRailButtonGO(right.gameObject)) want = false;
                if (right.gameObject.activeSelf != want) right.gameObject.SetActive(want);
            }
        }

        private bool IsSideRailFloatingIndex(int idx)
        {
            if (idx < 0) return false;
            GameObject leftDesk = leftDesktopModeBtnImage != null ? leftDesktopModeBtnImage.gameObject : null;
            GameObject rightDesk = rightDesktopModeBtnImage != null ? rightDesktopModeBtnImage.gameObject : null;
            if (TryGetSideRailButtonPair(idx, out RectTransform left, out RectTransform right))
            {
                if (left != null && leftDesk != null && left.gameObject == leftDesk) return true;
                if (right != null && rightDesk != null && right.gameObject == rightDesk) return true;
            }
            return false;
        }

        /// <summary>Next layout index to hide (tools/end first). Never hides Floating/Fixed.</summary>
        private int PickNextSideRailCollapseIndex()
        {
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            for (int i = layout.Length - 1; i >= 0; i--)
            {
                int idx = layout[i].buttonIndex;
                if (idx < 0) continue;
                if (IsSideRailFloatingIndex(idx)) continue;
                if (_sideRailOverflowCollapsedIdx.Contains(idx)) continue;
                if (!TryGetSideRailButtonPair(idx, out RectTransform left, out RectTransform right)) continue;
                GameObject go = left != null ? left.gameObject : right.gameObject;
                if (go == null || !go.activeSelf) continue;
                return idx;
            }
            return -1;
        }

        /// <summary>
        /// Fit side-rail stack to pane height: group gaps → flush spacing → overflow.
        /// Writes spacing/groupGap used by UpdateListPositions.
        /// </summary>
        private void ApplySideRailHeightFit(float scale, out float spacing, out float groupGap)
        {
            EnsureSideRailOverflowChrome();

            spacing = GalleryUiDesignTokens.SideButtonSpacingRef * scale;
            groupGap = (VPBConfig.Instance != null && VPBConfig.Instance.EnableButtonGaps)
                ? GalleryUiDesignTokens.SideButtonGroupGapRef * scale
                : 0f;

            // Restore prior overflow hides, then re-apply normal visibility rules.
            if (_sideRailOverflowCollapsedIdx.Count > 0)
            {
                for (int i = 0; i < _sideRailOverflowCollapsedIdx.Count; i++)
                    SetSideRailButtonIndexActive(_sideRailOverflowCollapsedIdx[i], true);
                _sideRailOverflowCollapsedIdx.Clear();
                try { UpdateSideButtonsVisibility(); } catch { }
                try { SyncSideFollowRailButtonsVisibility(); } catch { }
            }

            if (_leftSideRailOverflowBtnGO != null) _leftSideRailOverflowBtnGO.SetActive(false);
            if (_rightSideRailOverflowBtnGO != null) _rightSideRailOverflowBtnGO.SetActive(false);

            float avail = GetSideRailAvailableCenterSpan(scale);
            float btnH = GalleryUiDesignTokens.SideButtonSquareRef * scale;
            float stack = GetSideButtonsStackHeight(spacing, groupGap);

            // Phase 1: close zone/group gaps.
            if (stack > avail + 0.5f)
                groupGap = 0f;

            stack = GetSideButtonsStackHeight(spacing, groupGap);

            // Phase 2: squeeze base spacing down to flush (centers spaced by button height).
            float minSpacing = btnH;
            if (stack > avail + 0.5f)
            {
                int n = CountVisibleSideRailButtons();
                if (n > 1)
                {
                    float tight = avail / (n - 1);
                    spacing = Mathf.Clamp(tight, minSpacing, spacing);
                }
            }

            stack = GetSideButtonsStackHeight(spacing, groupGap);

            // Phase 3: collapse into … (account for … slot once any chip is hidden).
            while (true)
            {
                bool showOv = _sideRailOverflowCollapsedIdx.Count > 0;
                float need = stack;
                if (showOv) need += spacing; // one extra step for …
                if (need <= avail + 0.5f) break;

                int hideIdx = PickNextSideRailCollapseIndex();
                if (hideIdx < 0) break;
                SetSideRailButtonIndexActive(hideIdx, false);
                _sideRailOverflowCollapsedIdx.Add(hideIdx);
                stack = GetSideButtonsStackHeight(spacing, groupGap);
            }

            bool showOverflow = _sideRailOverflowCollapsedIdx.Count > 0;
            if (_leftSideRailOverflowBtnGO != null)
                _leftSideRailOverflowBtnGO.SetActive(showOverflow && leftSideContainer != null && leftSideContainer.activeSelf);
            if (_rightSideRailOverflowBtnGO != null)
                _rightSideRailOverflowBtnGO.SetActive(showOverflow && rightSideContainer != null && rightSideContainer.activeSelf);

            if (!showOverflow)
                CloseSideRailOverflowMenu();

            _sideRailFitSpacing = spacing;
            _sideRailFitGroupGap = groupGap;
        }

        private void PlaceSideRailOverflowButtons(float topY, float spacing, float groupGap, float scale)
        {
            if (_sideRailOverflowCollapsedIdx.Count <= 0) return;
            float y = topY;
            bool first = true;
            SideButtonLayoutEntry[] layout = GetSideButtonsLayout();
            for (int i = 0; i < layout.Length; i++)
            {
                int idx = layout[i].buttonIndex;
                if (idx < 0 || _sideRailOverflowCollapsedIdx.Contains(idx)) continue;
                if (!TryGetSideRailButtonPair(idx, out RectTransform left, out RectTransform right)) continue;
                GameObject go = left != null ? left.gameObject : right.gameObject;
                if (go == null || !go.activeSelf) continue;
                if (!first) y -= spacing + groupGap * layout[i].gapTier;
                first = false;
            }
            if (!first) y -= spacing;

            PlaceOneSideRailOverflowBtn(_leftSideRailOverflowBtnRT, leftSideButtons, y, isLeftRail: true, scale);
            PlaceOneSideRailOverflowBtn(_rightSideRailOverflowBtnRT, rightSideButtons, y, isLeftRail: false, scale);
        }

        private void PlaceOneSideRailOverflowBtn(RectTransform ovRT, List<RectTransform> list, float y, bool isLeftRail, float scale)
        {
            if (ovRT == null || !ovRT.gameObject.activeSelf) return;
            float sz = GalleryUiDesignTokens.SideButtonSquareRef * scale;
            ovRT.sizeDelta = new Vector2(sz, sz);
            float inset = GalleryUiDesignTokens.SideButtonEdgeInsetRef * scale;
            ovRT.anchorMin = ovRT.anchorMax = isLeftRail ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            ovRT.pivot = isLeftRail ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
            ovRT.anchoredPosition = new Vector2(isLeftRail ? -inset : inset, y);
            Text t = ovRT.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.gameObject.SetActive(true);
                GalleryUiMetrics.ApplyGlyphFont(t, GalleryUiDesignTokens.SideButtonSquareRef, scale, GalleryUiDesignTokens.FontMinRef);
            }
        }

        private void RebuildSideRailOverflowMenuRows(Transform panel)
        {
            if (panel == null) return;
            UI.DestroyAllChildren(panel);

            for (int i = 0; i < _sideRailOverflowCollapsedIdx.Count; i++)
            {
                int idx = _sideRailOverflowCollapsedIdx[i];
                if (!TryGetSideRailButtonPair(idx, out RectTransform left, out RectTransform right)) continue;
                GameObject invokeGo = _sideRailOverflowOpenedFromLeft
                    ? (left != null ? left.gameObject : (right != null ? right.gameObject : null))
                    : (right != null ? right.gameObject : (left != null ? left.gameObject : null));
                if (invokeGo == null) continue;

                string label = ResolveSideRailOverflowLabel(invokeGo);
                Sprite icon = UI.GetButtonIconSprite(invokeGo);
                GameObject capturedInvoke = invokeGo;
                GameObject row = UI.AddStretchPopupMenuRow(panel, label, () =>
                {
                    CloseSideRailOverflowMenu();
                    try
                    {
                        Button b = capturedInvoke.GetComponent<Button>();
                        if (b != null) b.onClick.Invoke();
                    }
                    catch { }
                }, icon: icon);
                if (row != null && TryResolveSideRailOverflowTooltip(invokeGo, out string tipKey, out string tipDefault))
                    AddTooltip(row, tipKey, tipDefault);
            }
        }

        private bool TryResolveSideRailOverflowTooltip(GameObject go, out string tipKey, out string tipDefault)
        {
            tipKey = null;
            tipDefault = null;
            if (go == null) return false;

            if (SideRailGoMatches(go, leftDesktopModeBtnImage, rightDesktopModeBtnImage, leftDesktopModeBtnText, rightDesktopModeBtnText))
            {
                tipKey = "gallery.tooltip.desktop_mode";
                tipDefault = "Toggle fixed vs floating panel (desktop only).";
                return true;
            }
            if (SideRailGoMatches(go, leftFollowBtnImage, rightFollowBtnImage, leftFollowBtnText, rightFollowBtnText))
            {
                tipKey = "gallery.tooltip.follow_mode";
                tipDefault = "Toggle camera follow for the panel.";
                return true;
            }
            if (SideRailGoMatchesIcon(go, leftCloneBtnIconImage, rightCloneBtnIconImage)
                || SideRailGoIs(go, leftCloneBtnText, rightCloneBtnText))
            {
                tipKey = "gallery.tooltip.clone_pane";
                tipDefault = "Clone this gallery pane.";
                return true;
            }
            if (SideRailGoIs(go, leftSaveBtnGO, rightSaveBtnGO)
                || SideRailGoMatchesIcon(go, leftSaveBtnIconImage, rightSaveBtnIconImage))
            {
                tipKey = "gallery.tooltip.save_pane";
                tipDefault = "Save presets and related actions.";
                return true;
            }
            if (SideRailGoIs(go, leftSceneImportSideBtn, rightSceneImportSideBtn))
            {
                tipKey = "gallery.tooltip.scene_import";
                tipDefault = "Open the Import sidebar for the selected scene";
                return true;
            }
            if (SideRailGoIs(go, leftUserTagsSideBtn, rightUserTagsSideBtn))
            {
                tipKey = "gallery.tooltip.user_tags_list";
                tipDefault = "Your tags (SQLite). Filter here; Edit opens tag manager.";
                return true;
            }
            if (SideRailGoIs(go, leftRemoveModeSideBtn, rightRemoveModeSideBtn))
            {
                tipKey = "gallery.tooltip.remove_mode";
                tipDefault = "Remove Item Mode: point at an item to fade it, click to remove. Also opens the remove list siderail for clothing/hair/scene.";
                return true;
            }
            if (SideRailGoIs(go, leftCreatorModeSideBtn, rightCreatorModeSideBtn)
                || SideRailGoMatchesIcon(go, leftCreatorModeBtnIconImage, rightCreatorModeBtnIconImage))
            {
                tipKey = "gallery.tooltip.creator_mode";
                tipDefault = "Creator Mode — sticky scene authoring tools. Not the Creators author list. Ctrl+Shift+K. Esc exits.";
                return true;
            }
            if (SideRailGoIs(go, leftRemoveAtomBtn, rightRemoveAtomBtn)
                || SideRailGoMatchesIcon(go, leftRemoveAtomBtnIconImage, rightRemoveAtomBtnIconImage)
                || SideRailGoIs(go, leftRemoveAllClothingBtn, rightRemoveAllClothingBtn)
                || SideRailGoIs(go, leftRemoveAllHairBtn, rightRemoveAllHairBtn))
            {
                tipKey = "gallery.tooltip.remove_context";
                tipDefault = "Remove or open remove options for the current category (clothing, hair, or scene).";
                return true;
            }
            if (SideRailGoMatches(go, leftCategoryBtnImage, rightCategoryBtnImage, leftCategoryBtnText, rightCategoryBtnText))
            {
                tipKey = "gallery.tooltip.category_list";
                tipDefault = "Browse all categories. Title = quick switch.";
                return true;
            }
            if (SideRailGoMatches(go, leftCreatorBtnImage, rightCreatorBtnImage, leftCreatorBtnText, rightCreatorBtnText))
            {
                tipKey = "gallery.tooltip.creator_list";
                tipDefault = "Browse creators (side list). Title bar filters the grid.";
                return true;
            }
            if (SideRailGoMatches(go, leftPathBtnImage, rightPathBtnImage, leftPathBtnText, rightPathBtnText))
            {
                tipKey = "gallery.tooltip.path_list";
                tipDefault = "Open package and file path list.";
                return true;
            }
            if (SideRailGoMatchesImage(go, leftHistoryBtnImage, rightHistoryBtnImage))
            {
                tipKey = "gallery.tooltip.history_list";
                tipDefault = "Launch history and usage filters.";
                return true;
            }
            if (SideRailGoMatches(go, leftApplyModeBtnImage, rightApplyModeBtnImage, leftApplyModeBtnText, rightApplyModeBtnText))
            {
                tipKey = "gallery.tooltip.apply_mode";
                tipDefault = "Toggle 1-click vs 2-click apply.";
                return true;
            }
            return false;
        }

        /// <summary>Icon side chips hide/blank Text — never use go.name (Button_…).</summary>
        private string ResolveSideRailOverflowLabel(GameObject go)
        {
            if (go == null) return VPBTranslation.T("gallery.side.overflow_item", "Action");

            if (SideRailGoMatches(go, leftDesktopModeBtnImage, rightDesktopModeBtnImage, leftDesktopModeBtnText, rightDesktopModeBtnText))
            {
                return isFixedLocally
                    ? VPBTranslation.T("gallery.desktop.floating", "Floating")
                    : VPBTranslation.T("gallery.desktop.fixed", "Fixed");
            }
            if (SideRailGoMatches(go, leftFollowBtnImage, rightFollowBtnImage, leftFollowBtnText, rightFollowBtnText))
                return VPBTranslation.T("gallery.side.overflow_follow", "Follow");
            if (SideRailGoMatchesIcon(go, leftCloneBtnIconImage, rightCloneBtnIconImage)
                || SideRailGoIs(go, leftCloneBtnText, rightCloneBtnText))
                return VPBTranslation.T("gallery.side.overflow_clone", "Clone pane");
            if (SideRailGoIs(go, leftSaveBtnGO, rightSaveBtnGO)
                || SideRailGoMatchesIcon(go, leftSaveBtnIconImage, rightSaveBtnIconImage))
                return VPBTranslation.T("gallery.side.overflow_save", "Save");
            if (SideRailGoIs(go, leftSceneImportSideBtn, rightSceneImportSideBtn))
                return VPBTranslation.T("gallery.side.overflow_import", "Import");
            if (SideRailGoIs(go, leftUserTagsSideBtn, rightUserTagsSideBtn))
                return VPBTranslation.T("gallery.side.overflow_tags", "User Tags");
            if (SideRailGoIs(go, leftRemoveModeSideBtn, rightRemoveModeSideBtn))
                return VPBTranslation.T("gallery.side.overflow_remove_mode", "Remove mode");
            if (SideRailGoIs(go, leftCreatorModeSideBtn, rightCreatorModeSideBtn)
                || SideRailGoMatchesIcon(go, leftCreatorModeBtnIconImage, rightCreatorModeBtnIconImage))
                return VPBTranslation.T("gallery.side.overflow_creator_mode", "Creator Mode");
            if (SideRailGoIs(go, leftRemoveAtomBtn, rightRemoveAtomBtn)
                || SideRailGoMatchesIcon(go, leftRemoveAtomBtnIconImage, rightRemoveAtomBtnIconImage))
                return VPBTranslation.T("gallery.side.overflow_remove", "Remove");
            if (SideRailGoIs(go, leftRemoveAllClothingBtn, rightRemoveAllClothingBtn))
                return VPBTranslation.T("gallery.side.overflow_remove_clothing", "Remove clothing");
            if (SideRailGoIs(go, leftRemoveAllHairBtn, rightRemoveAllHairBtn))
                return VPBTranslation.T("gallery.side.overflow_remove_hair", "Remove hair");
            if (SideRailGoMatches(go, leftCategoryBtnImage, rightCategoryBtnImage, leftCategoryBtnText, rightCategoryBtnText))
                return VPBTranslation.T("gallery.side.overflow_category", "Category");
            if (SideRailGoMatches(go, leftCreatorBtnImage, rightCreatorBtnImage, leftCreatorBtnText, rightCreatorBtnText))
                return VPBTranslation.T("gallery.side.overflow_creator", "Creator");
            if (SideRailGoMatches(go, leftPathBtnImage, rightPathBtnImage, leftPathBtnText, rightPathBtnText))
                return VPBTranslation.T("gallery.side.overflow_path", "Paths");
            if (SideRailGoMatchesImage(go, leftHistoryBtnImage, rightHistoryBtnImage))
                return VPBTranslation.T("gallery.side.overflow_history", "History");
            if (SideRailGoMatches(go, leftApplyModeBtnImage, rightApplyModeBtnImage, leftApplyModeBtnText, rightApplyModeBtnText))
                return VPBTranslation.T("gallery.side.overflow_apply_mode", "Apply mode");

            Text txt = go.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                string t = (txt.text ?? "").Trim();
                if (t.Length > 0 && t != " " && !t.StartsWith("Button_", StringComparison.Ordinal))
                    return t;
            }
            return VPBTranslation.T("gallery.side.overflow_item", "Action");
        }

        private static bool SideRailGoIs(GameObject go, GameObject a, GameObject b)
        {
            return go != null && (go == a || go == b);
        }

        private static bool SideRailGoIs(GameObject go, Text a, Text b)
        {
            if (go == null) return false;
            if (a != null && a.transform != null && (a.gameObject == go || a.transform.IsChildOf(go.transform))) return true;
            if (b != null && b.transform != null && (b.gameObject == go || b.transform.IsChildOf(go.transform))) return true;
            return false;
        }

        private static bool SideRailGoMatchesIcon(GameObject go, Image iconA, Image iconB)
        {
            if (go == null) return false;
            if (iconA != null && iconA.transform != null && iconA.transform.IsChildOf(go.transform)) return true;
            if (iconB != null && iconB.transform != null && iconB.transform.IsChildOf(go.transform)) return true;
            return false;
        }

        private static bool SideRailGoMatchesImage(GameObject go, Image a, Image b)
        {
            if (go == null) return false;
            if (a != null && go == a.gameObject) return true;
            if (b != null && go == b.gameObject) return true;
            return false;
        }

        private static bool SideRailGoMatches(GameObject go, Image imgA, Image imgB, Text textA, Text textB)
        {
            return SideRailGoMatchesImage(go, imgA, imgB)
                || SideRailGoMatchesIcon(go, imgA, imgB)
                || SideRailGoIs(go, textA, textB);
        }

        private void ToggleSideRailOverflowMenu(bool fromLeftRail)
        {
            if (_sideRailOverflowMenuGO == null) return;
            _sideRailOverflowOpen = !_sideRailOverflowOpen;
            if (_sideRailOverflowOpen)
            {
                _sideRailOverflowOpenedFromLeft = fromLeftRail;
                Transform panel = _sideRailOverflowMenuGO.transform.Find("OverflowMenuPanel");
                if (panel != null) RebuildSideRailOverflowMenuRows(panel);
                try { RescaleSideRailOverflowMenuInternal(ChromeScale); } catch { }
                _sideRailOverflowMenuGO.transform.SetAsLastSibling();
            }
            _sideRailOverflowMenuGO.SetActive(_sideRailOverflowOpen);
        }

        private void CloseSideRailOverflowMenu()
        {
            _sideRailOverflowOpen = false;
            if (_sideRailOverflowMenuGO != null) _sideRailOverflowMenuGO.SetActive(false);
        }

        private void PositionSideRailOverflowMenuPanel(RectTransform panelRT)
        {
            if (panelRT == null || _sideRailOverflowMenuGO == null) return;
            RectTransform overlayRT = _sideRailOverflowMenuGO.GetComponent<RectTransform>();
            if (overlayRT == null) return;

            RectTransform anchor = _sideRailOverflowOpenedFromLeft
                ? _leftSideRailOverflowBtnRT
                : _rightSideRailOverflowBtnRT;
            if (anchor == null || anchor.gameObject == null || !anchor.gameObject.activeSelf)
            {
                if (_rightSideRailOverflowBtnRT != null && _rightSideRailOverflowBtnGO != null && _rightSideRailOverflowBtnGO.activeSelf)
                    anchor = _rightSideRailOverflowBtnRT;
                else if (_leftSideRailOverflowBtnRT != null && _leftSideRailOverflowBtnGO != null && _leftSideRailOverflowBtnGO.activeSelf)
                    anchor = _leftSideRailOverflowBtnRT;
            }
            if (anchor == null) return;

            float s = ChromeScale <= 0f ? 1f : ChromeScale;
            bool openRight = anchor == _leftSideRailOverflowBtnRT;
            Vector3 world = anchor.TransformPoint(anchor.rect.center);
            Vector3 local = overlayRT.InverseTransformPoint(world);
            float halfBtn = anchor.rect.width * 0.5f;
            float gap = 10f * s;
            float pad = 8f * s;

            panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            if (openRight)
            {
                panelRT.pivot = new Vector2(0f, 0.5f);
                panelRT.anchoredPosition = new Vector2(local.x + halfBtn + gap, local.y);
            }
            else
            {
                panelRT.pivot = new Vector2(1f, 0.5f);
                panelRT.anchoredPosition = new Vector2(local.x - halfBtn - gap, local.y);
            }
            UI.ClampPopupMenuPanelX(panelRT, overlayRT, pad);

            // Keep menu above tooltip/info bar (same clearance as footer overflow).
            float? bottomFloor = null;
            if (hoverPathRT != null && hoverPathRT.gameObject.activeInHierarchy)
            {
                Vector3 worldInfoTop = hoverPathRT.TransformPoint(new Vector3(
                    hoverPathRT.rect.center.x,
                    hoverPathRT.rect.yMax,
                    0f));
                bottomFloor = overlayRT.InverseTransformPoint(worldInfoTop).y + gap;
            }
            else
            {
                bottomFloor = overlayRT.rect.yMin
                    + (GalleryUiDesignTokens.FooterBarHeightRef + GalleryUiDesignTokens.FooterInfoRowHeightRef) * s
                    + gap;
            }
            UI.ClampPopupMenuPanelY(panelRT, overlayRT, pad, bottomFloor);
        }

        private void RescaleSideRailOverflowMenuInternal(float s)
        {
            if (_sideRailOverflowMenuGO == null) return;
            if (s <= 0f) s = 1f;
            Transform panel = _sideRailOverflowMenuGO.transform.Find("OverflowMenuPanel");
            if (panel == null) return;
            ScaleVerticalPopupMenuRows(
                panel.gameObject, s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuOverflowFontRef,
                GalleryUiDesignTokens.OverflowMenuPanelWidthRef);
            if (_sideRailOverflowOpen)
                PositionSideRailOverflowMenuPanel(panel as RectTransform);
        }

        /// <summary>Top-dock horizontal side-button strip: squeeze gap then collapse into ….</summary>
        private void ApplyTopDockSideButtonsOverflowFit(float s, List<RectTransform> buttonList, SideButtonLayoutEntry[] layout, float availW, float btnSz, ref float gap)
        {
            if (buttonList == null || layout == null) return;
            EnsureSideRailOverflowChrome();

            if (_sideRailOverflowCollapsedIdx.Count > 0)
            {
                for (int i = 0; i < _sideRailOverflowCollapsedIdx.Count; i++)
                    SetSideRailButtonIndexActive(_sideRailOverflowCollapsedIdx[i], true);
                _sideRailOverflowCollapsedIdx.Clear();
                try { UpdateSideButtonsVisibility(); } catch { }
                try { SyncSideFollowRailButtonsVisibility(); } catch { }
            }

            if (_leftSideRailOverflowBtnGO != null) _leftSideRailOverflowBtnGO.SetActive(false);
            if (_rightSideRailOverflowBtnGO != null) _rightSideRailOverflowBtnGO.SetActive(false);

            float rowGap = 10f * s;
            float MeasureRow(float g)
            {
                int n = 0;
                for (int i = 0; i < layout.Length; i++)
                {
                    int idx = layout[i].buttonIndex;
                    if (idx < 0 || idx >= buttonList.Count) continue;
                    if (_sideRailOverflowCollapsedIdx.Contains(idx)) continue;
                    RectTransform rt = buttonList[idx];
                    if (rt == null || !rt.gameObject.activeSelf) continue;
                    n++;
                }
                if (_sideRailOverflowCollapsedIdx.Count > 0) n++; // …
                if (n <= 0) return 0f;
                return n * btnSz + (n - 1) * g;
            }

            if (MeasureRow(rowGap) > availW + 0.5f)
                rowGap = 0f;

            while (MeasureRow(rowGap) > availW + 0.5f)
            {
                int hideIdx = PickNextSideRailCollapseIndex();
                if (hideIdx < 0) break;
                SetSideRailButtonIndexActive(hideIdx, false);
                _sideRailOverflowCollapsedIdx.Add(hideIdx);
            }

            gap = rowGap;

            bool showOverflow = _sideRailOverflowCollapsedIdx.Count > 0;
            // Reparent left overflow chip into footer side group for top dock.
            if (showOverflow && _leftSideRailOverflowBtnGO != null && _footerSideButtonsGroupRT != null)
            {
                if (_leftSideRailOverflowBtnRT.parent != _footerSideButtonsGroupRT)
                    _leftSideRailOverflowBtnRT.SetParent(_footerSideButtonsGroupRT, worldPositionStays: false);
                _leftSideRailOverflowBtnGO.SetActive(true);
            }
        }
    }
}
