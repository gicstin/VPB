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
            if (rightClearCreatorBtn != null) rightClearCreatorBtn.SetActive(showRightSide && !string.IsNullOrEmpty(currentCreator));
        }

        private void UpdateClearButtonPosition(bool isRight, ContentType type)
        {
            GameObject btn = null;
            if (type == ContentType.Creator) btn = isRight ? rightClearCreatorBtn : leftClearCreatorBtn;
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
            // 5: Creator
            // 6: Target
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
                case ContentType.Creator: targetIndex = 5; break;
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
                // Clear creator button now lives in the side container, so position it relative to the Creator icon:
                // always immediately to the LEFT of the creator button (same Y).
                float gap = 6f;
                float tw = 0f;
                float bw = 0f;
                try { tw = targetBtnRT.rect.width; } catch { tw = targetBtnRT.sizeDelta.x; }
                try { bw = btnRT.rect.width; } catch { bw = btnRT.sizeDelta.x; }
                if (tw <= 0f) tw = targetBtnRT.sizeDelta.x;
                if (bw <= 0f) bw = btnRT.sizeDelta.x;

                // anchoredPosition is at the pivot (typically center), so use half-widths.
                float targetX = targetBtnRT.anchoredPosition.x - (tw * 0.5f + bw * 0.5f + gap);
                float targetY = targetBtnRT.anchoredPosition.y;
                btnRT.anchoredPosition = new Vector2(targetX, targetY);
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

        /// <summary>
        /// Single place for gallery panel <see cref="VPBConfig.ConfigChanged"/> wiring.
        /// REGRESSION GUARD: never subscribe <see cref="UpdateTabs"/> here — it repopulates O(n) side-tab buttons and freezes the UI on every Save/TriggerChange.
        /// </summary>
        private void SubscribeGalleryPanelToVpBConfigChanged()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.ConfigChanged += ApplySideButtonScale;
            VPBConfig.Instance.ConfigChanged += ApplyInnerPaneScale;
            VPBConfig.Instance.ConfigChanged += UpdateSideButtonsVisibility;
            VPBConfig.Instance.ConfigChanged += UpdateFooterFollowStates;
            VPBConfig.Instance.ConfigChanged += UpdateDesktopModeButton;
            VPBConfig.Instance.ConfigChanged += UpdateLayout;
            VPBConfig.Instance.ConfigChanged += RefreshSideTabAreasForConfigChange;
            VPBConfig.Instance.ConfigChanged += ApplyVamMenuGateVisibility;
        }

        private void UnsubscribeGalleryPanelFromVpBConfigChanged()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.ConfigChanged -= ApplySideButtonScale;
            VPBConfig.Instance.ConfigChanged -= ApplyInnerPaneScale;
            VPBConfig.Instance.ConfigChanged -= UpdateSideButtonsVisibility;
            VPBConfig.Instance.ConfigChanged -= UpdateFooterFollowStates;
            VPBConfig.Instance.ConfigChanged -= UpdateDesktopModeButton;
            VPBConfig.Instance.ConfigChanged -= UpdateLayout;
            VPBConfig.Instance.ConfigChanged -= RefreshSideTabAreasForConfigChange;
            VPBConfig.Instance.ConfigChanged -= ApplyVamMenuGateVisibility;
        }

        void OnDestroy()
        {
            // Re-enable saving on teardown so the cache isn't left permanently paused.
            if (GalleryThumbnailCache.Instance != null)
                GalleryThumbnailCache.Instance.SavingPaused = false;

            UnsubscribeLocaleChanged();

            UnsubscribeGalleryPanelFromVpBConfigChanged();

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
