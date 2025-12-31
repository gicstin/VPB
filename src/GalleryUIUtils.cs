using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace var_browser
{
    public class UIDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public Transform target;
        private float planeDistance;
        private Vector3 offset;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (target == null) target = transform;
            if (eventData.pressEventCamera == null) return;
            
            planeDistance = Vector3.Dot(target.position - eventData.pressEventCamera.transform.position, eventData.pressEventCamera.transform.forward);
            
            Ray ray = eventData.pressEventCamera.ScreenPointToRay(eventData.position);
            Vector3 hitPoint = ray.GetPoint(planeDistance);
            offset = target.position - hitPoint;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pressEventCamera == null) return;
            
            Ray ray = eventData.pressEventCamera.ScreenPointToRay(eventData.position);
            target.position = ray.GetPoint(planeDistance) + offset;
            
            // Face camera
            target.rotation = eventData.pressEventCamera.transform.rotation;
        }
    }

    public static class AnchorPresets
    {
        public const int none = -1;
        public const int topLeft = 0;
        public const int topMiddle = 1;
        public const int topRight = 2;
        public const int vStretchLeft = 3;
        public const int vStretchMiddle = 4;
        public const int vStretchRight = 5;
        public const int bottomLeft = 6;
        public const int bottomMiddle = 7;
        public const int bottomRight = 8;
        public const int hStretchTop = 9;
        public const int hStretchMiddle = 10;
        public const int hStretchBottom = 11;
        public const int centre = 12;
        public const int stretchAll = 13;
        public const int middleLeft = 14;
        public const int middleRight = 15;

        public static Vector2 GetAnchorMin(int preset)
        {
            switch (preset)
            {
                case topLeft: return new Vector2(0, 1);
                case topMiddle: return new Vector2(0.5f, 1);
                case topRight: return new Vector2(1, 1);
                case vStretchLeft: return new Vector2(0, 0);
                case vStretchMiddle: return new Vector2(0.5f, 0);
                case vStretchRight: return new Vector2(1, 0);
                case bottomLeft: return new Vector2(0, 0);
                case bottomMiddle: return new Vector2(0.5f, 0);
                case bottomRight: return new Vector2(1, 0);
                case hStretchTop: return new Vector2(0, 1);
                case hStretchMiddle: return new Vector2(0, 0.5f);
                case hStretchBottom: return new Vector2(0, 0);
                case centre: return new Vector2(0.5f, 0.5f);
                case stretchAll: return new Vector2(0, 0);
                case middleLeft: return new Vector2(0, 0.5f);
                case middleRight: return new Vector2(1, 0.5f);
                default: return Vector2.zero;
            }
        }

        public static Vector2 GetAnchorMax(int preset)
        {
            switch (preset)
            {
                case topLeft: return new Vector2(0, 1);
                case topMiddle: return new Vector2(0.5f, 1);
                case topRight: return new Vector2(1, 1);
                case vStretchLeft: return new Vector2(0, 1);
                case vStretchMiddle: return new Vector2(0.5f, 1);
                case vStretchRight: return new Vector2(1, 1);
                case bottomLeft: return new Vector2(0, 0);
                case bottomMiddle: return new Vector2(0.5f, 0);
                case bottomRight: return new Vector2(1, 0);
                case hStretchTop: return new Vector2(1, 1);
                case hStretchMiddle: return new Vector2(1, 0.5f);
                case hStretchBottom: return new Vector2(1, 0);
                case centre: return new Vector2(0.5f, 0.5f);
                case stretchAll: return new Vector2(1, 1);
                case middleLeft: return new Vector2(0, 0.5f);
                case middleRight: return new Vector2(1, 0.5f);
                default: return Vector2.zero;
            }
        }

        public static Vector2 GetPivot(int preset)
        {
            switch (preset)
            {
                case topLeft: return new Vector2(0, 1);
                case topMiddle: return new Vector2(0.5f, 1);
                case topRight: return new Vector2(1, 1);
                case vStretchLeft: return new Vector2(0, 0.5f);
                case vStretchMiddle: return new Vector2(0.5f, 0.5f);
                case vStretchRight: return new Vector2(1, 0.5f);
                case bottomLeft: return new Vector2(0, 0);
                case bottomMiddle: return new Vector2(0.5f, 0);
                case bottomRight: return new Vector2(1, 0);
                case hStretchTop: return new Vector2(0.5f, 1);
                case hStretchMiddle: return new Vector2(0.5f, 0.5f);
                case hStretchBottom: return new Vector2(0.5f, 0);
                case centre: return new Vector2(0.5f, 0.5f);
                case stretchAll: return new Vector2(0.5f, 0.5f);
                case middleLeft: return new Vector2(0, 0.5f);
                case middleRight: return new Vector2(1, 0.5f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }
    }

    public static class UI
    {
        public static GameObject CreateVScrollableContent(GameObject parentGO, Color backgroundColor, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float scrollBarWidth = 10f, float spacing = 0f)
        {
            GameObject scrollableContentGO = AddChildGOImage(parentGO, backgroundColor, anchorPreset, horizontalSize, verticalSize, anchoredPositionOffset);

            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollableContentGO.transform, false);
            RectTransform viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.sizeDelta = new Vector2(-scrollBarWidth, 0);
            viewportRT.anchoredPosition = new Vector2(-scrollBarWidth / 2, 0);
            viewportGO.AddComponent<RectMask2D>();

            GameObject contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.spacing = spacing;

            ContentSizeFitter csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject scrollbarGO = CreateScrollBar(scrollableContentGO, scrollBarWidth, verticalSize, Scrollbar.Direction.BottomToTop);
            
            ScrollRect scrollRect = scrollableContentGO.AddComponent<ScrollRect>();
            scrollRect.content = contentRT;
            scrollRect.viewport = viewportRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.verticalScrollbar = scrollbarGO.GetComponent<Scrollbar>();
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            return scrollableContentGO;
        }

        public static GameObject CreateScrollBar(GameObject parentGO, float width, float height, Scrollbar.Direction direction)
        {
            GameObject scrollbarGO = new GameObject("Scrollbar");
            scrollbarGO.transform.SetParent(parentGO.transform, false);
            RectTransform rt = scrollbarGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(width, 0);

            Image bg = scrollbarGO.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            Scrollbar scrollbar = scrollbarGO.AddComponent<Scrollbar>();
            scrollbar.direction = direction;

            GameObject slidingArea = new GameObject("Sliding Area");
            slidingArea.transform.SetParent(scrollbarGO.transform, false);
            RectTransform slidingRT = slidingArea.AddComponent<RectTransform>();
            slidingRT.anchorMin = Vector2.zero;
            slidingRT.anchorMax = Vector2.one;
            slidingRT.sizeDelta = Vector2.zero;

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(slidingArea.transform, false);
            RectTransform handleRT = handle.AddComponent<RectTransform>();
            handleRT.sizeDelta = Vector2.zero;
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = new Color(0.6f, 0.6f, 0.6f, 1f);

            scrollbar.handleRect = handleRT;
            scrollbar.targetGraphic = handleImg;

            return scrollbarGO;
        }

        public static GameObject AddChildGOImage(GameObject parentGO, Color color, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset)
        {
            GameObject go = new GameObject("Image");
            go.transform.SetParent(parentGO.transform, false);
            Image img = go.AddComponent<Image>();
            img.color = color;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = AnchorPresets.GetAnchorMin(anchorPreset);
            rt.anchorMax = AnchorPresets.GetAnchorMax(anchorPreset);
            rt.pivot = AnchorPresets.GetPivot(anchorPreset);
            rt.anchoredPosition = anchoredPositionOffset;
            rt.sizeDelta = new Vector2(horizontalSize, verticalSize);

            return go;
        }

        public static GameObject CreateUIButton(GameObject parentGO, float width, float height, string label, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction onClick)
        {
            GameObject buttonGO = AddChildGOImage(parentGO, Color.white, anchorPreset, width, height, new Vector2(xOffset, yOffset));
            buttonGO.name = "Button_" + label;
            Button btn = buttonGO.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.black;
            t.alignment = TextAnchor.MiddleCenter;

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            return buttonGO;
        }
    }
}
