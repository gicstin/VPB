using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
{
        private static readonly List<string> ClothingSlotPickerOptions = new List<string>()
        {
            "arms",
            "feet",
            "full body",
            "hands",
            "head",
            "hip",
            "legs",
            "neck",
            "torso",
            "accessory",
            "bodysuit",
            "bottom",
            "bra",
            "dress",
            "glasses",
            "gloves",
            "hat",
            "jacket",
            "jewelry",
            "mask",
            "panties",
            "pants",
            "shirt",
            "shoes",
            "shorts",
            "skirt",
            "socks",
            "stockings",
            "sweater",
            "top",
            "trunks",
            "underwear",
            "vest",
        };

        private struct SideButtonLayoutEntry
        {
            public int buttonIndex;
            public int row;
            public int gapTier;

            public SideButtonLayoutEntry(int buttonIndex, int row, int gapTier)
            {
                this.buttonIndex = buttonIndex;
                this.row = row;
                this.gapTier = gapTier;
            }
        }
        private void CreatePaginationControls()
        {
            // Pagination Container (Footer Bar)
            GameObject pageContainer = new GameObject("PaginationContainer");
            pageContainer.transform.SetParent(backgroundBoxGO.transform, false);
            paginationRT = pageContainer.AddComponent<RectTransform>();
            paginationRT.anchorMin = new Vector2(0, 0);
            paginationRT.anchorMax = new Vector2(1, 0); // Stretch horizontally
            paginationRT.pivot = new Vector2(0.5f, 0);
            paginationRT.anchoredPosition = new Vector2(0, 0);
            paginationRT.sizeDelta = new Vector2(0, 40); // Footer bar height for buttons
            
            HorizontalLayoutGroup footerHLG = pageContainer.AddComponent<HorizontalLayoutGroup>();
            footerHLG.padding = new RectOffset(60, 10, 0, 0); // 60 padding on left for resize handle
            footerHLG.childControlWidth = true;
            footerHLG.childControlHeight = true;
            footerHLG.childForceExpandWidth = true;

            // --- Left Section (Follow Controls) ---
            GameObject leftSection = new GameObject("LeftSection");
            leftSection.transform.SetParent(pageContainer.transform, false);
            leftSection.AddComponent<RectTransform>();
            leftSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup leftHLG = leftSection.AddComponent<HorizontalLayoutGroup>();
            leftHLG.childControlWidth = false;
            leftHLG.childForceExpandWidth = false;
            leftHLG.childAlignment = TextAnchor.MiddleLeft;
            leftHLG.spacing = 10;

            // Follow Quick Toggles
            footerFollowAngleBtn = UI.CreateUIButton(leftSection, 40, 40, "∡", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Angle"));
            footerFollowAngleImage = footerFollowAngleBtn.GetComponent<Image>();
            AddTooltip(footerFollowAngleBtn, "Follow Angle");
            
            footerFollowDistanceBtn = UI.CreateUIButton(leftSection, 40, 40, "↕", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Distance"));
            footerFollowDistanceImage = footerFollowDistanceBtn.GetComponent<Image>();
            AddTooltip(footerFollowDistanceBtn, "Follow Distance");
            
            footerFollowHeightBtn = UI.CreateUIButton(leftSection, 40, 40, "⊙", 20, 0, 0, AnchorPresets.middleCenter, () => ToggleFollowQuick("Height"));
            footerFollowHeightImage = footerFollowHeightBtn.GetComponent<Image>();
            AddTooltip(footerFollowHeightBtn, "Follow Eye Height");

            // --- Center Section (Pagination) ---
            GameObject centerSection = new GameObject("CenterSection");
            centerSection.transform.SetParent(pageContainer.transform, false);
            centerSection.AddComponent<RectTransform>();
            centerSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup centerHLG = centerSection.AddComponent<HorizontalLayoutGroup>();
            centerHLG.childControlWidth = false;
            centerHLG.childForceExpandWidth = false;
            centerHLG.childAlignment = TextAnchor.MiddleCenter;
            centerHLG.spacing = 10;

            paginationFirstBtn = UI.CreateUIButton(centerSection, 40, 40, "|<", 18, 0, 0, AnchorPresets.middleCenter, FirstPage);
            paginationPrev10Btn = UI.CreateUIButton(centerSection, 40, 40, "<<", 18, 0, 0, AnchorPresets.middleCenter, Prev10Page);
            paginationPrevBtn = UI.CreateUIButton(centerSection, 40, 40, "<", 20, 0, 0, AnchorPresets.middleCenter, PrevPage);
            
            GameObject textGO = new GameObject("PageText");
            textGO.transform.SetParent(centerSection.transform, false);
            paginationText = textGO.AddComponent<Text>();
            paginationText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            paginationText.fontSize = 18;
            paginationText.color = Color.white;
            paginationText.alignment = TextAnchor.MiddleCenter;
            paginationText.text = "1 / 1";
            paginationText.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.sizeDelta = new Vector2(200, 40);

            paginationNextBtn = UI.CreateUIButton(centerSection, 40, 40, ">", 20, 0, 0, AnchorPresets.middleCenter, NextPage);
            paginationNext10Btn = UI.CreateUIButton(centerSection, 40, 40, ">>", 18, 0, 0, AnchorPresets.middleCenter, Next10Page);
            paginationLastBtn = UI.CreateUIButton(centerSection, 40, 40, ">|", 18, 0, 0, AnchorPresets.middleCenter, LastPage);

            // --- Right Section (Utility Controls) ---
            GameObject rightSection = new GameObject("RightSection");
            rightSection.transform.SetParent(pageContainer.transform, false);
            rightSection.AddComponent<RectTransform>();
            rightSection.AddComponent<LayoutElement>().flexibleWidth = 1;
            
            HorizontalLayoutGroup rightHLG = rightSection.AddComponent<HorizontalLayoutGroup>();
            rightHLG.childControlWidth = false;
            rightHLG.childForceExpandWidth = false;
            rightHLG.childAlignment = TextAnchor.MiddleRight;
            rightHLG.spacing = 10;

            selectAllBtn = UI.CreateUIButton(rightSection, 40, 40, "A", 20, 0, 0, AnchorPresets.middleCenter, SelectAll);
            clearSelectionBtn = UI.CreateUIButton(rightSection, 40, 40, "C", 20, 0, 0, AnchorPresets.middleCenter, ClearSelection);
            gridSizeMinusBtn = UI.CreateUIButton(rightSection, 40, 40, "-", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(1));
            gridSizePlusBtn = UI.CreateUIButton(rightSection, 40, 40, "+", 24, 0, 0, AnchorPresets.middleCenter, () => AdjustGridColumns(-1));
            
            footerLayoutBtn = UI.CreateUIButton(rightSection, 40, 40, "≡", 20, 0, 0, AnchorPresets.middleCenter, ToggleLayoutMode);
            footerLayoutBtnImage = footerLayoutBtn.GetComponent<Image>();
            footerLayoutBtnText = footerLayoutBtn.GetComponentInChildren<Text>();

            footerHeightBtn = UI.CreateUIButton(rightSection, 40, 40, "↕", 20, 0, 0, AnchorPresets.middleCenter, ToggleFixedHeightMode);
            footerHeightBtnImage = footerHeightBtn.GetComponent<Image>();
            footerHeightBtnText = footerHeightBtn.GetComponentInChildren<Text>();

            footerAutoHideBtn = UI.CreateUIButton(rightSection, 40, 40, "A", 20, 0, 0, AnchorPresets.middleCenter, ToggleAutoHideMode);
            footerAutoHideBtnImage = footerAutoHideBtn.GetComponent<Image>();
            footerAutoHideBtnText = footerAutoHideBtn.GetComponentInChildren<Text>();

            // --- Context Actions (Category-aware) ---
            footerRemoveAllHairBtn = UI.CreateUIButton(rightSection, 40, 40, "Hr", 16, 0, 0, AnchorPresets.middleCenter, () => {
                Atom target = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                if (target == null)
                {
                    LogUtil.LogWarning("[VPB] Please select a Person atom.");
                    return;
                }

                UIDraggableItem dragger = footerRemoveAllHairBtn.GetComponent<UIDraggableItem>();
                if (dragger == null) dragger = footerRemoveAllHairBtn.AddComponent<UIDraggableItem>();
                dragger.Panel = this;
                dragger.RemoveAllHair(target);
            });
            footerRemoveAllHairBtnImage = footerRemoveAllHairBtn.GetComponent<Image>();
            footerRemoveAllHairBtnText = footerRemoveAllHairBtn.GetComponentInChildren<Text>();
            AddTooltip(footerRemoveAllHairBtn, "Remove All Hair from Target");

            // Hover support
            AddHoverDelegate(paginationFirstBtn);
            AddTooltip(paginationFirstBtn, "First Page");
            AddHoverDelegate(paginationPrev10Btn);
            AddTooltip(paginationPrev10Btn, "Back 10 Pages");
            AddHoverDelegate(paginationPrevBtn);
            AddTooltip(paginationPrevBtn, "Previous Page");
            AddHoverDelegate(paginationNextBtn);
            AddTooltip(paginationNextBtn, "Next Page");
            AddHoverDelegate(paginationNext10Btn);
            AddTooltip(paginationNext10Btn, "Forward 10 Pages");
            AddHoverDelegate(paginationLastBtn);
            AddTooltip(paginationLastBtn, "Last Page");
            AddHoverDelegate(selectAllBtn);
            AddTooltip(selectAllBtn, "Select All");
            AddHoverDelegate(clearSelectionBtn);
            AddTooltip(clearSelectionBtn, "Clear Selection");
            AddHoverDelegate(gridSizeMinusBtn);
            AddTooltip(gridSizeMinusBtn, "Decrease Columns");
            AddHoverDelegate(gridSizePlusBtn);
            AddTooltip(gridSizePlusBtn, "Increase Columns");
            AddHoverDelegate(footerFollowAngleBtn);
            AddHoverDelegate(footerFollowDistanceBtn);
            AddHoverDelegate(footerFollowHeightBtn);
            AddHoverDelegate(footerLayoutBtn);
            AddTooltip(footerLayoutBtn, "Toggle Layout Mode");
            AddHoverDelegate(footerHeightBtn);
            AddTooltip(footerHeightBtn, "Toggle Fixed Height Mode");
            AddHoverDelegate(footerAutoHideBtn);
            AddTooltip(footerAutoHideBtn, "Auto-Hide (Fixed)");
            AddHoverDelegate(footerRemoveAllHairBtn);

            // Hover Path Text (Now placed above the buttons with background)
            GameObject pathGO = UI.AddChildGOImage(backgroundBoxGO, new Color(0, 0, 0, 0.85f), AnchorPresets.hStretchBottom, 0, 40, new Vector2(0, 40));
            pathGO.name = "HoverPathContainer";
            pathGO.GetComponent<Image>().raycastTarget = false;
            hoverPathRT = pathGO.GetComponent<RectTransform>();
            hoverPathCanvasGroup = pathGO.AddComponent<CanvasGroup>();
            hoverPathCanvasGroup.alpha = 0;
            hoverPathCanvasGroup.blocksRaycasts = false;
            hoverPathCanvasGroup.interactable = false;
            
            GameObject hoverPathTextGO = new GameObject("HoverPathText");
            hoverPathTextGO.transform.SetParent(pathGO.transform, false);
            hoverPathText = hoverPathTextGO.AddComponent<Text>();
            hoverPathText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            hoverPathText.fontSize = 20; // Slightly smaller to ensure 2 rows fit comfortably in 60px
            hoverPathText.color = Color.white;
            var shadow = hoverPathTextGO.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.8f);
            shadow.effectDistance = new Vector2(1, -1);
            hoverPathText.alignment = TextAnchor.MiddleCenter;
            hoverPathText.horizontalOverflow = HorizontalWrapMode.Wrap;
            hoverPathText.verticalOverflow = VerticalWrapMode.Truncate;
            hoverPathText.lineSpacing = 0.9f;
            hoverPathText.text = "";
            hoverPathText.raycastTarget = false;
            
            RectTransform hoverPathTextRT = hoverPathTextGO.GetComponent<RectTransform>();
            hoverPathTextRT.anchorMin = Vector2.zero;
            hoverPathTextRT.anchorMax = Vector2.one;
            hoverPathTextRT.sizeDelta = Vector2.zero;
            hoverPathTextRT.anchoredPosition = Vector2.zero;

            UpdateSideButtonsVisibility();
            UpdateFooterFollowStates();
            UpdateFooterLayoutState();
            UpdateFooterHeightState();
            UpdateFooterAutoHideState();
            UpdateFooterContextActions();
            UpdatePaginationText();
        }

        private void UpdateFooterContextActions()
        {
            // Default to hidden
            if (footerRemoveAllHairBtn != null) footerRemoveAllHairBtn.SetActive(false);

            string title = currentCategoryTitle ?? "";
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;

            if (footerRemoveAllHairBtn != null) footerRemoveAllHairBtn.SetActive(isHair);

            // Slight visual cue (optional but consistent with other footer buttons)
            if (footerRemoveAllHairBtnImage != null) footerRemoveAllHairBtnImage.color = new Color(0.6f, 0.2f, 0.2f, 1f);
            if (footerRemoveAllHairBtnText != null) footerRemoveAllHairBtnText.color = Color.white;
        }

        private void ToggleLayoutMode()
        {
            layoutMode = (layoutMode == GalleryLayoutMode.Grid) ? GalleryLayoutMode.VerticalCard : GalleryLayoutMode.Grid;
            
            // Immediately update grid component
            if (contentGO != null)
            {
                UIGridAdaptive adaptive = contentGO.GetComponent<UIGridAdaptive>();
                if (adaptive != null)
                {
                    adaptive.isVerticalCard = (layoutMode == GalleryLayoutMode.VerticalCard);
                    adaptive.forcedColumnCount = gridColumnCount;
                    adaptive.UpdateGrid();
                }
            }

            // FULL PURGE: Clear both pooled and active buttons because templates are fundamentally different
            foreach (var go in fileButtonPool) if (go != null) Destroy(go);
            fileButtonPool.Clear();
            
            foreach (var go in activeButtons) if (go != null) Destroy(go);
            activeButtons.Clear();

            UpdateFooterLayoutState();
            RefreshFiles(true); // Force full refresh
        }

        private void UpdateFooterLayoutState()
        {
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            if (footerLayoutBtnImage != null)
                footerLayoutBtnImage.color = (layoutMode == GalleryLayoutMode.VerticalCard) ? activeColor : inactiveColor;
            
            if (footerLayoutBtnText != null)
                footerLayoutBtnText.text = (layoutMode == GalleryLayoutMode.VerticalCard) ? "≡" : "▤";
        }

        private void ToggleFixedHeightMode()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.DesktopFixedHeightMode = (VPBConfig.Instance.DesktopFixedHeightMode + 1) % 2;
            VPBConfig.Instance.Save();
            UpdateFooterHeightState();
            UpdateLayout();
        }

        private void UpdateFooterHeightState()
        {
            if (VPBConfig.Instance == null) return;
            
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            
            if (footerHeightBtnImage != null)
                footerHeightBtnImage.color = VPBConfig.Instance.DesktopFixedHeightMode > 0 ? activeColor : inactiveColor;
                
            if (footerHeightBtnText != null)
            {
                switch(VPBConfig.Instance.DesktopFixedHeightMode)
                {
                    case 0: footerHeightBtnText.text = "H1"; break;
                    case 1: footerHeightBtnText.text = "HC"; break;
                }
            }
        }

        private void ToggleAutoHideMode()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.DesktopFixedAutoCollapse = !VPBConfig.Instance.DesktopFixedAutoCollapse;
            VPBConfig.Instance.Save();
            UpdateFooterAutoHideState();
            UpdateLayout();
        }

        private void UpdateFooterAutoHideState()
        {
            if (VPBConfig.Instance == null) return;

            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            if (footerAutoHideBtnImage != null)
                footerAutoHideBtnImage.color = VPBConfig.Instance.DesktopFixedAutoCollapse ? activeColor : inactiveColor;

            if (footerAutoHideBtnText != null)
            {
                footerAutoHideBtnText.text = VPBConfig.Instance.DesktopFixedAutoCollapse ? "AH" : "AO";
            }
        }

        private void ToggleFollowQuick(string type)
        {
            if (VPBConfig.Instance == null) return;
            
            if (type == "Angle") {
                VPBConfig.Instance.FollowAngle = (VPBConfig.Instance.FollowAngle == "Off") ? "Both" : "Off";
            } else if (type == "Distance") {
                VPBConfig.Instance.FollowDistance = (VPBConfig.Instance.FollowDistance == "Off") ? "Both" : "Off";
            } else if (type == "Height") {
                VPBConfig.Instance.FollowEyeHeight = (VPBConfig.Instance.FollowEyeHeight == "Off") ? "Both" : "Off";
            }
            
            VPBConfig.Instance.TriggerChange();
            UpdateFooterFollowStates();
        }

        private void UpdateFooterFollowStates()
        {
            if (VPBConfig.Instance == null) return;
            
            Color activeColor = new Color(0.15f, 0.45f, 0.6f, 1f);
            Color inactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            
            if (footerFollowAngleImage != null)
                footerFollowAngleImage.color = VPBConfig.Instance.FollowAngle != "Off" ? activeColor : inactiveColor;
                
            if (footerFollowDistanceImage != null)
                footerFollowDistanceImage.color = VPBConfig.Instance.FollowDistance != "Off" ? activeColor : inactiveColor;
                
            if (footerFollowHeightImage != null)
                footerFollowHeightImage.color = VPBConfig.Instance.FollowEyeHeight != "Off" ? activeColor : inactiveColor;
        }

        private void AddTooltip(GameObject go, string tooltip)
        {
            if (go == null) return;
            var del = go.GetComponent<UIHoverDelegate>();
            if (del == null) del = go.AddComponent<UIHoverDelegate>();
            
            del.OnHoverChange += (enter) => {
                if (enter)
                {
                    if (temporaryStatusCoroutine != null)
                    {
                        StopCoroutine(temporaryStatusCoroutine);
                        temporaryStatusCoroutine = null;
                    }
                    temporaryStatusMsg = tooltip;
                }
                else if (temporaryStatusMsg == tooltip) temporaryStatusMsg = null;
            };
        }


        private void UpdateDesktopModeButton()
        {
            if (VPBConfig.Instance == null) return;

            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }

            bool fixedMode = isFixedLocally;
            string text = fixedMode ? "Floating" : "Fixed";
            Color color = fixedMode ? new Color(0.15f, 0.45f, 0.6f, 1f) : new Color(0.15f, 0.15f, 0.15f, 1f);

            if (rightDesktopModeBtnText != null) 
            {
                rightDesktopModeBtnText.text = text;
                rightDesktopModeBtnText.transform.parent.gameObject.SetActive(!isVR);
            }
            if (rightDesktopModeBtnImage != null) rightDesktopModeBtnImage.color = color;

            if (leftDesktopModeBtnText != null) 
            {
                leftDesktopModeBtnText.text = text;
                leftDesktopModeBtnText.transform.parent.gameObject.SetActive(!isVR);
            }
            if (leftDesktopModeBtnImage != null) leftDesktopModeBtnImage.color = color;

            if (footerFollowAngleBtn != null) footerFollowAngleBtn.SetActive(!fixedMode);
            if (footerFollowDistanceBtn != null) footerFollowDistanceBtn.SetActive(!fixedMode);
            if (footerFollowHeightBtn != null) footerFollowHeightBtn.SetActive(!fixedMode);
            if (footerHeightBtn != null) footerHeightBtn.SetActive(fixedMode);
            if (footerAutoHideBtn != null) footerAutoHideBtn.SetActive(fixedMode);

            UpdateSideButtonPositions();
        }

        private void PopulateClothingSubmenuButtons(Atom target)
        {
            // Avoid briefly hiding buttons during periodic resync while the pointer is over the submenu.
            if (!clothingSubmenuOpen) SetClothingSubmenuButtonsVisible(false);

            if (target == null) return;

            try
            {
                clothingSubmenuTargetAtomUid = target.uid;
            }
            catch { }

            JSONStorable geometry = null;
            try { geometry = target.GetStorableByID("geometry"); } catch { }

            List<KeyValuePair<string, string>> options = null;
            try
            {
                var items = new List<KeyValuePair<string, string>>();
                bool addedAny = false;
                if (geometry != null)
                {
                    try
                    {
                        int geometryActiveCount = 0;
                        foreach (var name in geometry.GetBoolParamNames())
                        {
                            if (string.IsNullOrEmpty(name)) continue;
                            if (!name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;

                            string clothingUid = null;
                            try { clothingUid = name.Substring(9); } catch { }
                            if (string.IsNullOrEmpty(clothingUid)) continue;

                            JSONStorableBool jsb = null;
                            try { jsb = geometry.GetBoolJSONParam(name); } catch { }
                            if (jsb == null) continue;

                            if (jsb.val) geometryActiveCount++;

                            bool isPreviewItem = (!string.IsNullOrEmpty(previewRemoveClothingItemUid) && string.Equals(clothingUid, previewRemoveClothingItemUid, StringComparison.OrdinalIgnoreCase));
                            if (!jsb.val && !isPreviewItem) continue;

                            // Skip built-in clothing (ref impl does this to avoid issues)
                            if (!clothingUid.Contains("/")) continue;

                            string path = clothingUid;
                            if (string.IsNullOrEmpty(path)) continue;

                            string p = path.Replace("\\", "/");
                            string pl = p.ToLowerInvariant();
                            int idx = pl.IndexOf("/custom/clothing/");
                            if (idx < 0) idx = pl.IndexOf("/clothing/");
                            if (idx < 0 && pl.StartsWith("custom/clothing/", StringComparison.OrdinalIgnoreCase)) idx = 0;
                            if (idx < 0 && pl.StartsWith("clothing/", StringComparison.OrdinalIgnoreCase)) idx = 0;
                            if (idx < 0) continue;

                            string sub = p.Substring(idx);
                            string[] parts = sub.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int pi = 0; pi < parts.Length; pi++) parts[pi] = parts[pi].Trim();

                            string typeFolder = (parts.Length >= 4) ? parts[3] : null;
                            string fileName = null;
                            try
                            {
                                string last = parts.Length > 0 ? parts[parts.Length - 1] : null;
                                if (!string.IsNullOrEmpty(last))
                                {
                                    int dot = last.LastIndexOf('.');
                                    fileName = dot > 0 ? last.Substring(0, dot) : last;
                                }
                            }
                            catch { }

                            string label = !string.IsNullOrEmpty(typeFolder)
                                ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + (fileName ?? ""))
                                : (fileName ?? "");

                            if (!string.IsNullOrEmpty(label))
                            {
                                items.Add(new KeyValuePair<string, string>(clothingUid, label));
                                addedAny = true;
                            }
                        }

                    }
                    catch { }
                }

                // Fallback if geometry bools aren't available
                if (!addedAny)
                {
                    DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                    if (dcs != null && dcs.clothingItems != null)
                    {
                        foreach (var item in dcs.clothingItems)
                        {
                            if (item == null) continue;
                            bool isPreviewItem = (!string.IsNullOrEmpty(previewRemoveClothingItemUid) && string.Equals(item.uid, previewRemoveClothingItemUid, StringComparison.OrdinalIgnoreCase));
                            bool isVisible = false;
                            try { isVisible = item.active; } catch { isVisible = false; }
                            if (!isVisible && !isPreviewItem) continue;

                            string path = null;
                            try { path = item.uid; } catch { }
                            if (string.IsNullOrEmpty(path)) continue;

                            string p = path.Replace("\\", "/");
                            string pl = p.ToLowerInvariant();
                            int idx = pl.IndexOf("/custom/clothing/");
                            if (idx < 0) idx = pl.IndexOf("/clothing/");
                            if (idx < 0) continue;

                            string sub = p.Substring(idx);
                            string[] parts = sub.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int pi = 0; pi < parts.Length; pi++) parts[pi] = parts[pi].Trim();

                            string typeFolder = (parts.Length >= 4) ? parts[3] : null;
                            string fileName = null;
                            try
                            {
                                string last = parts.Length > 0 ? parts[parts.Length - 1] : null;
                                if (!string.IsNullOrEmpty(last))
                                {
                                    int dot = last.LastIndexOf('.');
                                    fileName = dot > 0 ? last.Substring(0, dot) : last;
                                }
                            }
                            catch { }

                            string label = !string.IsNullOrEmpty(typeFolder)
                                ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + (fileName ?? ""))
                                : (fileName ?? "");

                            if (!string.IsNullOrEmpty(label))
                            {
                                items.Add(new KeyValuePair<string, string>(item.uid, label));
                            }
                        }
                    }
                }

                options = items
                    .Where(kvp => !string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                    .GroupBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { }

            if (options == null) options = new List<KeyValuePair<string, string>>();
            int optionTotal = options.Count;
            int count = Mathf.Min(optionTotal, HairSubmenuMaxButtons);

            clothingSubmenuLastOptionCount = optionTotal;
            UpdateRemoveClothingButtonLabels(optionTotal);

            try
            {
                if (rightRemoveClothingSubmenuPanelGO != null) rightRemoveClothingSubmenuPanelGO.SetActive(count > 0);
                if (leftRemoveClothingSubmenuPanelGO != null) leftRemoveClothingSubmenuPanelGO.SetActive(count > 0);
            }
            catch { }

            for (int i = 0; i < HairSubmenuMaxButtons; i++)
            {
                string uid = i < count ? options[i].Key : null;
                string label = i < count ? options[i].Value : null;

                void Configure(GameObject btnGO, bool isRight)
                {
                    if (btnGO == null) return;
                    Button btn = btnGO.GetComponent<Button>();
                    Text t = btnGO.GetComponentInChildren<Text>();
                    if (t != null) t.text = label ?? "";

                    if (btn != null) btn.transition = Selectable.Transition.None;

                    try
                    {
                        var et = btnGO.GetComponent<EventTrigger>();
                        if (et == null) et = btnGO.AddComponent<EventTrigger>();

                        if (et.triggers == null) et.triggers = new List<EventTrigger.Entry>();
                        et.triggers.RemoveAll(e => e != null && (e.eventID == EventTriggerType.PointerEnter || e.eventID == EventTriggerType.PointerExit));

                        var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                        enterEntry.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount++;
                                clothingSubmenuOptionsHovered = true;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;

                                Atom tgt = null;
                                try
                                {
                                    if (!string.IsNullOrEmpty(clothingSubmenuTargetAtomUid)) tgt = SuperController.singleton.GetAtomByUid(clothingSubmenuTargetAtomUid);
                                }
                                catch { }
                                if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;

                                if (tgt != null && !string.IsNullOrEmpty(uid))
                                {
                                    ApplyClothingPreview(tgt, uid);
                                }
                            }
                            catch { }
                        });
                        et.triggers.Add(enterEntry);

                        var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                        exitEntry.callback.AddListener((data) => {
                            try
                            {
                                clothingSubmenuOptionsHoverCount--;
                                if (clothingSubmenuOptionsHoverCount < 0) clothingSubmenuOptionsHoverCount = 0;
                                clothingSubmenuOptionsHovered = clothingSubmenuOptionsHoverCount > 0;
                                clothingSubmenuLastHoverTime = Time.unscaledTime;

                                Atom tgt = null;
                                try
                                {
                                    if (!string.IsNullOrEmpty(clothingSubmenuTargetAtomUid)) tgt = SuperController.singleton.GetAtomByUid(clothingSubmenuTargetAtomUid);
                                }
                                catch { }
                                if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;

                                if (tgt != null && !string.IsNullOrEmpty(uid))
                                {
                                    ClearClothingPreview(tgt, uid);
                                }
                            }
                            catch { }
                        });
                        et.triggers.Add(exitEntry);
                    }
                    catch { }
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        if (!string.IsNullOrEmpty(uid))
                        {
                            btn.onClick.AddListener(() => {
                                bool keepSubmenuOpen = false;
                                try
                                {
                                    Atom tgt = null;
                                    try
                                    {
                                        if (!string.IsNullOrEmpty(clothingSubmenuTargetAtomUid)) tgt = SuperController.singleton.GetAtomByUid(clothingSubmenuTargetAtomUid);
                                    }
                                    catch { }
                                    if (tgt == null) tgt = actionsPanel != null ? actionsPanel.GetBestTargetAtom() : SelectedTargetAtom;
                                    if (tgt == null) return;

                                    // Hover preview temporarily hides clothing by flipping geometry bools.
                                    // Restore before actual removal so the item is "active" when we attempt to remove it.
                                    ClearClothingPreview();

                                    GameObject removeBtnGO = isRight ? rightRemoveAllClothingBtn : leftRemoveAllClothingBtn;
                                    UIDraggableItem dragger = removeBtnGO != null ? removeBtnGO.GetComponent<UIDraggableItem>() : null;
                                    if (dragger == null && removeBtnGO != null) dragger = removeBtnGO.AddComponent<UIDraggableItem>();
                                    if (dragger != null)
                                    {
                                        dragger.Panel = this;
                                        dragger.RemoveClothingItemByUid(tgt, uid);
                                    }
                                    else
                                    {
                                        LogUtil.LogWarning("[VPB] RemoveClothing submenu click: UIDraggableItem not available");
                                    }
                                    SyncClothingSubmenu(tgt, true);
                                    keepSubmenuOpen = true;
                                }
                                finally
                                {
                                    if (!keepSubmenuOpen)
                                    {
                                        CloseClothingSubmenuUI();
                                        clothingSubmenuOpen = false;
                                        SetClothingSubmenuButtonsVisible(false);
                                        UpdateSideButtonPositions();
                                    }
                                }
                            });
                        }
                    }
                    btnGO.SetActive(i < count);
                }

                if (i < rightRemoveClothingSubmenuButtons.Count) Configure(rightRemoveClothingSubmenuButtons[i], true);
                if (i < leftRemoveClothingSubmenuButtons.Count) Configure(leftRemoveClothingSubmenuButtons[i], false);
            }
        }

        private void ToggleClothingSubmenuFromSideButtons(Atom target)
        {
            clothingSubmenuOpen = !clothingSubmenuOpen;
            if (clothingSubmenuOpen)
            {
                ClearClothingPreview();
                clothingSubmenuLastSyncTime = Time.unscaledTime;
                PopulateClothingSubmenuButtons(target);
                clothingSubmenuLastOptionCount = Mathf.Min(HairSubmenuMaxButtons, rightRemoveClothingSubmenuButtons != null ? rightRemoveClothingSubmenuButtons.Count : 0);
            }
            else
            {
                CloseClothingSubmenuUI();
            }

            UpdateSideButtonPositions();
        }

        private void CloseClothingSubmenuUI()
        {
            try
            {
                ClearClothingPreview();
                clothingSubmenuOpen = false;
                clothingSubmenuParentHovered = false;
                clothingSubmenuOptionsHovered = false;
                clothingSubmenuParentHoverCount = 0;
                clothingSubmenuOptionsHoverCount = 0;
                clothingSubmenuLastOptionCount = 0;
                SetClothingSubmenuButtonsVisible(false);
                UpdateRemoveClothingButtonLabels(0);
            }
            catch { }
        }

        private void SyncClothingSubmenu(Atom target, bool keepOpenIfHasOptions)
        {
            if (target == null) { CloseClothingSubmenuUI(); return; }
            PopulateClothingSubmenuButtons(target);
            int options = 0;
            try
            {
                options = 0;
                int optionsLeft = 0;
                for (int i = 0; i < rightRemoveClothingSubmenuButtons.Count; i++)
                {
                    var b = rightRemoveClothingSubmenuButtons[i];
                    if (b != null && b.activeSelf) options++;
                }
                for (int i = 0; i < leftRemoveClothingSubmenuButtons.Count; i++)
                {
                    var b = leftRemoveClothingSubmenuButtons[i];
                    if (b != null && b.activeSelf) optionsLeft++;
                }
                options = Mathf.Max(options, optionsLeft);
            }
            catch { }
            clothingSubmenuLastOptionCount = options;
            if (options <= 0)
            {
                CloseClothingSubmenuUI();
            }
            else
            {
                clothingSubmenuOpen = keepOpenIfHasOptions;
                SetClothingSubmenuButtonsVisible(true);
            }
            UpdateSideButtonPositions();
        }

        private void UpdateRemoveButtonLabels(GameObject leftBtn, GameObject rightBtn, string baseLabel, int optionCount)
        {
            try
            {
                bool hasOptions = optionCount > 0;
                string suffix = hasOptions ? (" (" + optionCount.ToString() + ")") : "";

                if (leftBtn != null)
                {
                    Text t = leftBtn.GetComponentInChildren<Text>();
                    if (t != null) t.text = hasOptions ? ("< " + baseLabel + suffix) : baseLabel;
                }
                if (rightBtn != null)
                {
                    Text t = rightBtn.GetComponentInChildren<Text>();
                    if (t != null) t.text = hasOptions ? (baseLabel + " >" + suffix) : baseLabel;
                }
            }
            catch { }
        }

        private void UpdateRemoveClothingButtonLabels(int optionCount)
        {
            UpdateRemoveButtonLabels(leftRemoveAllClothingBtn, rightRemoveAllClothingBtn, "Remove\nClothing", optionCount);
        }

        private void ApplyClothingPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;

                if (!string.IsNullOrEmpty(previewRemoveClothingAtomUid) && !string.IsNullOrEmpty(previewRemoveClothingItemUid))
                {
                    if (!string.Equals(previewRemoveClothingAtomUid, target.uid, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(previewRemoveClothingItemUid, itemUid, StringComparison.OrdinalIgnoreCase))
                    {
                        ClearClothingPreview();
                    }
                }

                if (!string.IsNullOrEmpty(previewRemoveClothingAtomUid) && !string.IsNullOrEmpty(previewRemoveClothingItemUid))
                {
                    return;
                }

                JSONStorable geometry = null;
                try { geometry = target.GetStorableByID("geometry"); } catch { }
                if (geometry == null) return;

                JSONStorableBool active = null;
                try { active = geometry.GetBoolJSONParam("clothing:" + itemUid); } catch { }
                if (active == null) return;

                previewRemoveClothingAtomUid = target.uid;
                previewRemoveClothingItemUid = itemUid;
                previewRemoveClothingPrevGeometryVal = active.val;

                // Observed VaM semantics for these bools:
                // active.val == true -> visible, so set false to hide during preview
                if (active.val) active.val = false;
            }
            catch { }
        }

        private void ClearClothingPreview(Atom target, string itemUid)
        {
            try
            {
                if (target == null || string.IsNullOrEmpty(itemUid)) return;
                if (string.IsNullOrEmpty(previewRemoveClothingAtomUid) || string.IsNullOrEmpty(previewRemoveClothingItemUid)) return;
                if (!string.Equals(previewRemoveClothingAtomUid, target.uid, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.Equals(previewRemoveClothingItemUid, itemUid, StringComparison.OrdinalIgnoreCase)) return;
                RestoreClothingPreview();
            }
            catch { }
        }

        private void ClearClothingPreview()
        {
            try { RestoreClothingPreview(); }
            catch { }
        }

        private void RestoreClothingPreview()
        {
            try
            {
                if (string.IsNullOrEmpty(previewRemoveClothingAtomUid) || string.IsNullOrEmpty(previewRemoveClothingItemUid))
                {
                    previewRemoveClothingAtomUid = null;
                    previewRemoveClothingItemUid = null;
                    previewRemoveClothingPrevGeometryVal = null;
                    return;
                }

                Atom atom = null;
                try { atom = SuperController.singleton.GetAtomByUid(previewRemoveClothingAtomUid); } catch { }
                if (atom == null)
                {
                    previewRemoveClothingAtomUid = null;
                    previewRemoveClothingItemUid = null;
                    previewRemoveClothingPrevGeometryVal = null;
                    return;
                }

                JSONStorable geometry = null;
                try { geometry = atom.GetStorableByID("geometry"); } catch { }
                if (geometry != null)
                {
                    JSONStorableBool active = null;
                    try { active = geometry.GetBoolJSONParam("clothing:" + previewRemoveClothingItemUid); } catch { }
                    if (active != null && previewRemoveClothingPrevGeometryVal.HasValue)
                    {
                        active.val = previewRemoveClothingPrevGeometryVal.Value;
                    }
                }

                previewRemoveClothingAtomUid = null;
                previewRemoveClothingItemUid = null;
                previewRemoveClothingPrevGeometryVal = null;
            }
            catch
            {
                previewRemoveClothingAtomUid = null;
                previewRemoveClothingItemUid = null;
                previewRemoveClothingPrevGeometryVal = null;
            }
        }

        private void ToggleDesktopMode()
        {
            if (VPBConfig.Instance == null) return;

            bool isVR = false;
            try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }
            if (isVR)
            {
                if (isFixedLocally) SetFixedLocally(false);
                return;
            }
            
            bool targetFixed = !isFixedLocally;
            
            if (targetFixed)
            {
                // Only one can be fixed. Revert others.
                if (Gallery.singleton != null)
                {
                    foreach (var p in Gallery.singleton.Panels)
                    {
                        if (p != this) p.SetFixedLocally(false);
                    }
                }
                isFixedLocally = true;
                VPBConfig.Instance.DesktopFixedMode = true;
            }
            else
            {
                isFixedLocally = false;
                VPBConfig.Instance.DesktopFixedMode = false;
            }
            
            VPBConfig.Instance.Save();
            UpdateDesktopModeButton();
            UpdateLayout();
        }

        public void SetFixedLocally(bool fixedMode)
        {
            if (fixedMode)
            {
                bool isVR = false;
                try { isVR = UnityEngine.XR.XRSettings.enabled; } catch { }
                if (isVR) fixedMode = false;
            }

            if (isFixedLocally == fixedMode) return;
            isFixedLocally = fixedMode;
            if (!fixedMode) SetCollapsed(false);
            UpdateDesktopModeButton();
            UpdateSideButtonsVisibility();
            UpdateLayout();
        }

        public void SetCollapsed(bool collapsed)
        {
            if (isCollapsed == collapsed) return;
            isCollapsed = collapsed;
            collapseTimer = 0f;
            
            if (backgroundBoxGO != null)
            {
                RectTransform rt = backgroundBoxGO.GetComponent<RectTransform>();
                rt.anchoredPosition = collapsed ? new Vector2(rt.rect.width, 0) : Vector2.zero;
            }

            if (collapseTriggerGO != null)
            {
                Image img = collapseTriggerGO.GetComponent<Image>();
                if (img != null) 
                {
                    img.color = collapsed ? new Color(0.15f, 0.15f, 0.15f, 0.4f) : new Color(1, 1, 1, 0f);
                    img.raycastTarget = collapsed;
                }
            }
            if (collapseHandleText != null)
            {
                collapseHandleText.gameObject.SetActive(collapsed);
            }
            
            UpdateSideButtonsVisibility();
            UpdateLayout();
        }

        private void NextPage()
        {
            currentPage++;
            RefreshFiles();
        }

        private void Next10Page()
        {
            currentPage += 10;
            RefreshFiles();
        }

        private void FirstPage()
        {
            currentPage = 0;
            RefreshFiles(false, true);
        }

        private void LastPage()
        {
            currentPage = Mathf.Max(0, lastTotalPages - 1);
            RefreshFiles();
        }

        private void PrevPage()
        {
            if (currentPage > 0)
            {
                currentPage--;
                RefreshFiles(false, true);
            }
        }

        private void Prev10Page()
        {
            if (currentPage > 0)
            {
                currentPage = Mathf.Max(0, currentPage - 10);
                RefreshFiles(false, true);
            }
        }

        private void SelectAll()
        {
            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectionAnchorPath = null;

            for (int i = 0; i < lastFilteredFiles.Count; i++)
            {
                var f = lastFilteredFiles[i];
                if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                if (selectedFilePaths.Add(f.Path)) selectedFiles.Add(f);
            }

            if (selectedFiles.Count > 0)
            {
                selectedPath = selectedFiles[0].Path;
                selectionAnchorPath = selectedPath;
                SetHoverPath(selectedFiles[0]);
            }
            else
            {
                selectedPath = null;
                SetHoverPath("");
            }

            RefreshFiles(true);
            UpdatePaginationText();
            actionsPanel?.HandleSelectionChanged(selectedFiles, selectedHubItem);
        }

        private void ClearSelection()
        {
            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectionAnchorPath = null;
            selectedPath = null;
            selectedHubItem = null;
            SetHoverPath("");
            RefreshFiles(true);
            UpdatePaginationText();
            actionsPanel?.HandleSelectionChanged(selectedFiles, selectedHubItem);
        }

        private void AdjustGridColumns(int delta)
        {
            gridColumnCount = Mathf.Clamp(gridColumnCount + delta, 1, 12);
            if (contentGO != null)
            {
                UIGridAdaptive adaptive = contentGO.GetComponent<UIGridAdaptive>();
                if (adaptive != null)
                {
                    adaptive.forcedColumnCount = gridColumnCount;
                    adaptive.UpdateGrid();
                }
            }
            RefreshFiles(true);
        }

        private void UpdatePaginationText()
        {
            if (paginationText != null)
            {
                int page = currentPage + 1;
                int totalPages = Mathf.Max(1, lastTotalPages);
                int total = Mathf.Max(0, lastTotalItems);
                paginationText.text = $"{page} / {totalPages} ({total})";
            }
        }

        private void ToggleRight(ContentType type)
        {
            if (rightActiveContent == type) 
            {
                rightActiveContent = null;
            }
            else 
            {
                rightActiveContent = type;
                // Collapse Left IF it is the SAME type
                if (leftActiveContent == type) leftActiveContent = null;
            }
            
            UpdateLayout();
            UpdateTabs();

            if (leftActiveContent == ContentType.ActiveItems || rightActiveContent == ContentType.ActiveItems)
            {
                RefreshFiles();
            }
        }

        private void ToggleLeft(ContentType type)
        {
            if (leftActiveContent == type)
            {
                leftActiveContent = null;
            }
            else
            {
                leftActiveContent = type;
                // Collapse Right IF it is the SAME type
                if (rightActiveContent == type) rightActiveContent = null;
            }
            
            UpdateLayout();
            UpdateTabs();

            if (leftActiveContent == ContentType.ActiveItems || rightActiveContent == ContentType.ActiveItems)
            {
                RefreshFiles();
            }
        }

        private void UpdateReplaceButtonState()
        {
            string text = DragDropReplaceMode ? "Replace" : "Add";
            Color color = DragDropReplaceMode ? new Color(0.6f, 0.15f, 0.15f, 1f) : new Color(0.15f, 0.45f, 0.15f, 1f);

            if (rightReplaceBtnText != null) rightReplaceBtnText.text = text;
            if (rightReplaceBtnImage != null) rightReplaceBtnImage.color = color;
            
            if (leftReplaceBtnText != null) leftReplaceBtnText.text = text;
            if (leftReplaceBtnImage != null) leftReplaceBtnImage.color = color;
        }

        private void ToggleReplaceMode()
        {
            DragDropReplaceMode = !DragDropReplaceMode;
            UpdateReplaceButtonState();
        }

        private void UpdateApplyModeButtonState()
        {
            string text = ItemApplyMode == ApplyMode.SingleClick ? "1-Click" : "2-Click";
            Color color = ItemApplyMode == ApplyMode.SingleClick ? new Color(0.6f, 0.45f, 0.15f, 1f) : new Color(0.15f, 0.15f, 0.45f, 1f);

            if (rightApplyModeBtnText != null) rightApplyModeBtnText.text = text;
            if (rightApplyModeBtnImage != null) rightApplyModeBtnImage.color = color;
            
            if (leftApplyModeBtnText != null) leftApplyModeBtnText.text = text;
            if (leftApplyModeBtnImage != null) leftApplyModeBtnImage.color = color;
        }

        private void ToggleApplyMode()
        {
            ItemApplyMode = (ItemApplyMode == ApplyMode.SingleClick) ? ApplyMode.DoubleClick : ApplyMode.SingleClick;
            UpdateApplyModeButtonState();
        }


    }

}
