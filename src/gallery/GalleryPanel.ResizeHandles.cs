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

            // Create resize handle in bottom-right corner
            resizeHandleGO = new GameObject("ResizeHandle");
            resizeHandleGO.transform.SetParent(backgroundBoxGO.transform, false);

            RectTransform handleRT = resizeHandleGO.AddComponent<RectTransform>();
            handleRT.anchorMin = new Vector2(1, 0);
            handleRT.anchorMax = new Vector2(1, 0);
            handleRT.pivot = new Vector2(1, 0);
            handleRT.anchoredPosition = Vector2.zero;
            handleRT.sizeDelta = new Vector2(30, 30);

            // Add visual triangle image
            Image handleImg = resizeHandleGO.AddComponent<Image>();
            handleImg.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);

            // Create triangle sprite procedurally
            Texture2D triangleTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    // Draw triangle in bottom-right corner
                    bool isTriangle = (32 - x) + (32 - y) > 32;
                    triangleTex.SetPixel(x, y, isTriangle ? Color.white : Color.clear);
                }
            }
            triangleTex.Apply();
            handleImg.sprite = Sprite.Create(triangleTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));

            // Add drag handler
            ResizeDragHandler dragHandler = resizeHandleGO.AddComponent<ResizeDragHandler>();
            dragHandler.Target = bgRT;
            dragHandler.MinSize = new Vector2(800, 600);
            dragHandler.MaxSize = new Vector2(1920, 1200);

            // Add hover effect
            UIHoverColor hoverColor = resizeHandleGO.AddComponent<UIHoverColor>();
            if (hoverColor != null)
            {
                hoverColor.normalColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);
                hoverColor.hoverColor = new Color(1f, 1f, 1f, 1f);
            }
        }
    }
}
