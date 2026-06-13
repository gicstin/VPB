using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private GameObject resizeHandleGO;

        private class ResizeDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public RectTransform Target;
            public Vector2 MinSize = new Vector2(800, 600);
            public Vector2 MaxSize = new Vector2(1920, 1200);

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null) return;
                
                Vector2 size = Target.sizeDelta;
                size.x += eventData.delta.x;
                size.y -= eventData.delta.y;
                
                size.x = Mathf.Clamp(size.x, MinSize.x, MaxSize.x);
                size.y = Mathf.Clamp(size.y, MinSize.y, MaxSize.y);
                
                Target.sizeDelta = size;
            }
        }

        private void CreateResizeHandles()
        {
            if (backgroundBoxGO == null) return;

            RectTransform bgRT = backgroundBoxGO.GetComponent<RectTransform>();
            if (bgRT == null) return;

            // Prevent double creation
            if (resizeHandleGO != null)
            {
                Destroy(resizeHandleGO);
                resizeHandleGO = null;
            }

            // Create container for all resize handles
            resizeHandleGO = new GameObject("ResizeHandles");
            resizeHandleGO.transform.SetParent(backgroundBoxGO.transform, false);

            // Corner handle definitions: (name, anchorMin, anchorMax, pivot, icon path)
            (string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, string iconPath)[] corners = new[]
            {
                ("ResizeHandle-TopLeft", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    "BepInEx/plugins/vpb_icons/chevrons_up_left.png"),
                ("ResizeHandle-TopRight", new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                    "BepInEx/plugins/vpb_icons/chevrons_up_right.png"),
                ("ResizeHandle-BottomLeft", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                    "BepInEx/plugins/vpb_icons/chevrons_down_left.png"),
                ("ResizeHandle-BottomRight", new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0),
                    "BepInEx/plugins/vpb_icons/chevrons_down_right.png"),
            };

            foreach (var (name, anchorMin, anchorMax, pivot, iconPath) in corners)
            {
                GameObject handleGO = new GameObject(name);
                handleGO.transform.SetParent(resizeHandleGO.transform, false);

                RectTransform handleRT = handleGO.AddComponent<RectTransform>();
                handleRT.anchorMin = anchorMin;
                handleRT.anchorMax = anchorMax;
                handleRT.pivot = pivot;
                handleRT.anchoredPosition = Vector2.zero;
                handleRT.sizeDelta = new Vector2(30, 30);

                // Load icon from file
                Image handleImg = handleGO.AddComponent<Image>();
                Sprite iconSprite = Resources.Load<Sprite>(iconPath);
                if (iconSprite != null)
                {
                    handleImg.sprite = iconSprite;
                    handleImg.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                }
                else
                {
                    // Fallback: colored rectangle if icon not found
                    handleImg.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                }

                // Add drag handler
                ResizeDragHandler dragHandler = handleGO.AddComponent<ResizeDragHandler>();
                dragHandler.Target = bgRT;
                dragHandler.MinSize = new Vector2(800, 600);
                dragHandler.MaxSize = new Vector2(1920, 1200);

                // Border-only hover
                UIHoverBorder hb = handleGO.AddComponent<UIHoverBorder>();
                hb.hoverColor = new Color(1f, 1f, 0f, 1f);
                hb.borderSize = 2f;
            }
        }
    }
}
