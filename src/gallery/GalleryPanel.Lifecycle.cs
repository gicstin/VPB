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

        private void UpdateSideButtonsVisibility()
        {
            if (VPBConfig.Instance == null) return;
            string mode = VPBConfig.Instance.ShowSideButtons;
            bool fixedMode = isFixedLocally;

            if (leftSideContainer != null) 
            {
                if (isCollapsed) leftSideContainer.SetActive(false);
                else leftSideContainer.SetActive(mode == "Both" || mode == "Left");
            }
            
            if (rightSideContainer != null) 
            {
                if (fixedMode || isCollapsed) rightSideContainer.SetActive(false);
                else rightSideContainer.SetActive(mode == "Both" || mode == "Right");
            }

            bool showLeftSide = !isCollapsed && (mode == "Both" || mode == "Left");
            bool showRightSide = !fixedMode && !isCollapsed && (mode == "Both" || mode == "Right");

            if (leftClearCreatorBtn != null) leftClearCreatorBtn.SetActive(showLeftSide && !string.IsNullOrEmpty(currentCreator));
            if (leftClearStatusBtn != null) leftClearStatusBtn.SetActive(showLeftSide && !string.IsNullOrEmpty(currentStatus));
            if (rightClearCreatorBtn != null) rightClearCreatorBtn.SetActive(showRightSide && !string.IsNullOrEmpty(currentCreator));
            if (rightClearStatusBtn != null) rightClearStatusBtn.SetActive(showRightSide && !string.IsNullOrEmpty(currentStatus));

            if (leftClearCreatorBtn != null && leftClearCreatorBtn.activeSelf) UpdateClearButtonPosition(false, ContentType.Creator);
            if (leftClearStatusBtn != null && leftClearStatusBtn.activeSelf) UpdateClearButtonPosition(false, ContentType.Status);
            if (rightClearCreatorBtn != null && rightClearCreatorBtn.activeSelf) UpdateClearButtonPosition(true, ContentType.Creator);
            if (rightClearStatusBtn != null && rightClearStatusBtn.activeSelf) UpdateClearButtonPosition(true, ContentType.Status);
        }

        private void UpdateClearButtonPosition(bool isRight, ContentType type)
        {
            GameObject btn = null;
            if (type == ContentType.Creator) btn = isRight ? rightClearCreatorBtn : leftClearCreatorBtn;
            else if (type == ContentType.Status) btn = isRight ? rightClearStatusBtn : leftClearStatusBtn;
            if (btn == null) return;

            // Find the button for this content type
            RectTransform targetBtnRT = null;
            
            // We need to find the specific button rect. 
            // We store them in sideButtons list but we need to know WHICH one corresponds to the type.
            // Indices based on GalleryPanel.UI.cs creation order:
            // 0: Fixed/Floating
            // 1: Settings
            // 2: Follow
            // 3: Clone
            // 4: Category
            // 5: ActiveItems
            // 6: Creator
            // 7: Status
            // 8: Target
            // 9: Apply Mode
            // 10: Replace
            // 11: Hub
            // 12: Undo
            // 13: Remove Clothing (context)
            // 14: Remove Hair (context)
            // 15: Random

            int targetIndex = -1;
            switch(type)
            {
                case ContentType.Creator: targetIndex = 6; break;
                case ContentType.Status: targetIndex = 7; break;
            }

            if (targetIndex >= 0)
            {
                List<RectTransform> list = isRight ? rightSideButtons : leftSideButtons;
                if (targetIndex < list.Count)
                {
                    targetBtnRT = list[targetIndex];
                }
            }

            if (targetBtnRT != null && targetBtnRT.gameObject.activeInHierarchy)
            {
                RectTransform btnRT = btn.GetComponent<RectTransform>();
                // Position "outside" means further away from the center panel.
                // Left side: To the left of the button.
                // Right side: To the right of the button.
                
                float xOffset = isRight ? 65f : -65f; // Button width is 120, center to edge is 60. +5 padding
                
                // But wait, the side containers are at -120 and +120.
                // The clear button is parented to backgroundBoxGO.
                // The side button is parented to sideContainer.
                // We need to convert or just use fixed offsets relative to side container position.
                
                // Side container anchors:
                // Left: (-120, 0)
                // Right: (120, 0)
                
                // Side Button anchor is (0, Y) inside that container.
                // So world position logic or just parent relative logic.
                // The Clear Button is child of backgroundBoxGO.
                
                float btnY = targetBtnRT.anchoredPosition.y; // Y inside container
                
                // The container is centered vertically? No, container anchor is middleLeft/Right.
                // Let's check container setup in CreateUI.
                // leftSideContainer = UI.AddChildGOImage(..., AnchorPresets.middleLeft, ..., new Vector2(-120, 0));
                // So container (0,0) is at (-120, 0) from background left edge?
                // No, AnchorPresets.middleLeft means (0, 0.5) anchor.
                // AnchoredPosition (-120, 0) means shifted left by 120.
                
                // backgroundBoxGO is the parent of ClearButton.
                // ClearButton anchor is middleLeft/Right.
                
                // If ClearButton has AnchorPresets.middleLeft (Left) or middleRight (Right):
                // Left Button X: containerX + xOffset
                // Right Button X: containerX + xOffset
                
                // Wait, "Outside" relative to the gallery.
                // Left Side: Outside is further Left (Negative X).
                // Right Side: Outside is further Right (Positive X).
                
                // Left container is at -120. Button is at 0 X inside it.
                // So button center is at -120.
                // We want clear button at -120 - 60 - 25 = -205?
                // Or maybe just -80 relative to the button center.
                
                float targetX = isRight ? (140f + 65f) : (-140f - 65f);
                
                btnRT.anchoredPosition = new Vector2(targetX, btnY);
            }
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

        void OnDestroy()
        {
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.ConfigChanged -= UpdateSideButtonPositions;
                VPBConfig.Instance.ConfigChanged -= UpdateSideButtonsVisibility;
                VPBConfig.Instance.ConfigChanged -= ApplyCurvatureToChildren;
                VPBConfig.Instance.ConfigChanged -= UpdateFooterFollowStates;
                VPBConfig.Instance.ConfigChanged -= UpdateDesktopModeButton;
                VPBConfig.Instance.ConfigChanged -= UpdateLayout;
            }

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

            if (targetMarkerGO != null)
            {
                Destroy(targetMarkerGO);
                targetMarkerGO = null;
                targetMarkerAtomUid = null;
            }
        }

        private void EnsureTargetMarker()
        {
            if (targetMarkerGO != null) return;
            
            targetMarkerGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            targetMarkerGO.name = "VPB_TargetMarker";
            
            Collider c = targetMarkerGO.GetComponent<Collider>();
            if (c != null) Destroy(c);

            targetMarkerGO.transform.localScale = Vector3.one * 0.08f;

            Renderer r = targetMarkerGO.GetComponent<Renderer>();
            if (r != null)
            {
                Shader unlit = Shader.Find("Unlit/Color");
                if (unlit == null) unlit = Shader.Find("Transparent/Diffuse");
                
                if (unlit != null)
                {
                    Material m = new Material(unlit);
                    m.color = Color.magenta;
                    r.material = m;
                }
            }

            targetMarkerGO.SetActive(false);
        }

        private void UpdateTargetMarker()
        {
            bool shouldShow = hoverCount > 0 && (canvas != null && canvas.gameObject.activeInHierarchy);
            Atom target = SelectedTargetAtom;
            if (!shouldShow || target == null || target.type != "Person")
            {
                if (targetMarkerGO != null) targetMarkerGO.SetActive(false);
                return;
            }

            EnsureTargetMarker();
            if (targetMarkerGO == null) return;

            Transform desiredParent = (target.mainController != null) ? target.mainController.transform : target.transform;

            if (targetMarkerAtomUid != target.uid || targetMarkerGO.transform.parent != desiredParent)
            {
                targetMarkerAtomUid = target.uid;
                targetMarkerGO.transform.SetParent(desiredParent, false);
                targetMarkerGO.transform.localPosition = Vector3.zero;
                targetMarkerGO.transform.localRotation = Quaternion.identity;
            }

            if (!targetMarkerGO.activeSelf) targetMarkerGO.SetActive(true);
        }


    }

}
