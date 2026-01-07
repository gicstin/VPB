using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        public string GetCurrentPath() => currentPath;
        public string GetCurrentExtension() => currentExtension;
        public string GetCurrentCreator() => currentCreator;
        public string GetTitle() => titleText != null ? titleText.text : "";
        public RectTransform GetBackgroundRT() => backgroundBoxGO != null ? backgroundBoxGO.GetComponent<RectTransform>() : null;

        public ContentType? GetLeftActiveContent() => leftActiveContent;
        public ContentType? GetRightActiveContent() => rightActiveContent;

        public void SetLeftActiveContent(ContentType? type)
        {
            leftActiveContent = type;
            UpdateLayout();
            UpdateTabs();
        }

        public void SetRightActiveContent(ContentType? type)
        {
            rightActiveContent = type;
            UpdateLayout();
            UpdateTabs();
        }

        public void SetFilters(string path, string extension, string creator)
        {
            currentPath = path;
            currentExtension = extension;
            currentCreator = creator;
            creatorsCached = false;
            tagsCached = false;
            categoriesCached = false;
        }

        private void CreateResizeHandles()
        {
            CreateResizeHandle(AnchorPresets.bottomRight, 0);
            CreateResizeHandle(AnchorPresets.bottomLeft, -90);
            CreateResizeHandle(AnchorPresets.topLeft, 180);
        }

        private void CreateResizeHandle(int anchor, float rotationZ)
        {
            GameObject handleGO = new GameObject("ResizeHandle_" + anchor);
            handleGO.transform.SetParent(backgroundBoxGO.transform, false);

            Image img = handleGO.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.01f); // Invisible hit area

            // Add Hover Border
            handleGO.AddComponent<UIHoverBorder>();

            RectTransform handleRT = handleGO.GetComponent<RectTransform>();
            handleRT.anchorMin = AnchorPresets.GetAnchorMin(anchor);
            handleRT.anchorMax = AnchorPresets.GetAnchorMax(anchor);
            handleRT.pivot = AnchorPresets.GetPivot(anchor);
            
            // Push handles outwards
            float offsetDist = 20f;
            Vector2 offset = Vector2.zero;
            if (anchor == AnchorPresets.bottomRight) offset = new Vector2(offsetDist, -offsetDist);
            else if (anchor == AnchorPresets.bottomLeft) offset = new Vector2(-offsetDist, -offsetDist);
            else if (anchor == AnchorPresets.topLeft) offset = new Vector2(-offsetDist, offsetDist);
            else if (anchor == AnchorPresets.topRight) offset = new Vector2(offsetDist, offsetDist);

            handleRT.anchoredPosition = offset;
            handleRT.sizeDelta = new Vector2(60, 60);

            // Text
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(handleGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.raycastTarget = false;
            t.text = "◢";
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 36; // Increased size
            t.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            t.alignment = TextAnchor.MiddleCenter;

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            textRT.localRotation = Quaternion.Euler(0, 0, rotationZ);

            // UIResizable
            UIResizable resizer = handleGO.AddComponent<UIResizable>();
            resizer.target = backgroundBoxGO.GetComponent<RectTransform>();
            resizer.anchor = anchor;
            resizer.onResizeStatusChange = (isResizing) => {
                 this.isResizing = isResizing;
            };

            // Hover Effect
            UIHoverColor hover = handleGO.AddComponent<UIHoverColor>();
            hover.targetText = t;
            hover.normalColor = t.color;
            hover.hoverColor = Color.green;
            
            AddHoverDelegate(handleGO); // Ensure tracking works here too
        }
    }
}
