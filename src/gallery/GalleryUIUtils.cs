using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MVR.FileManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SimpleJSON;

namespace VPB
{
    public class UIListReorderable : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform target;
        public UnityAction OnReorder;
        public int minIndex = 0; // Minimum sibling index this item can move to
        
        private Transform parent;
        private int startIndex;
        private Camera dragCam;
        private Button btn;
        private bool wasBtnEnabled;

        void Awake()
        {
            if (target == null) target = transform as RectTransform;
            parent = target.parent;
            btn = GetComponent<Button>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            startIndex = target.GetSiblingIndex();
            dragCam = eventData.pressEventCamera;
            if (dragCam == null) dragCam = Camera.main;
            
            // Disable button during drag to prevent accidental click on release
            if (btn != null)
            {
                wasBtnEnabled = btn.enabled;
                btn.enabled = false;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (parent == null || dragCam == null) return;

            // Find which index we should be at based on vertical position
            int currentIndex = target.GetSiblingIndex();
            Vector2 localMouse;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent as RectTransform, eventData.position, dragCam, out localMouse))
            {
                // Iterate through siblings to see if we should swap
                for (int i = minIndex; i < parent.childCount; i++)
                {
                    if (i == currentIndex) continue;
                    
                    Transform child = parent.GetChild(i);
                    RectTransform childRT = child as RectTransform;
                    if (childRT == null) continue;

                    // Use midpoint + buffer to prevent flickering
                    float childMidY = childRT.anchoredPosition.y - (childRT.rect.height * 0.5f);
                    float buffer = 5f;

                    if (currentIndex < i) // Dragging down
                    {
                        if (localMouse.y < childMidY - buffer)
                        {
                            target.SetSiblingIndex(i);
                            break;
                        }
                    }
                    else // Dragging up
                    {
                        if (localMouse.y > childMidY + buffer)
                        {
                            target.SetSiblingIndex(i);
                            break;
                        }
                    }
                }
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // Restore button state
            if (btn != null) btn.enabled = wasBtnEnabled;

            if (target.GetSiblingIndex() != startIndex)
            {
                if (OnReorder != null) OnReorder();
            }
        }
    }

    public class UIDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Transform target;
        public bool isDragging { get; private set; }
        public UnityAction OnDragEnd;
        private float planeDistance;
        private Vector3 offset;
        private Camera dragCam;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            isDragging = true;

            if (target == null) target = transform;
            dragCam = eventData.pressEventCamera;
            if (dragCam == null) dragCam = Camera.main;
            if (dragCam == null) return;
            
            planeDistance = Vector3.Dot(target.position - dragCam.transform.position, dragCam.transform.forward);
            
            Ray ray = dragCam.ScreenPointToRay(eventData.position);
            Vector3 hitPoint = ray.GetPoint(planeDistance);
            offset = target.position - hitPoint;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (dragCam == null) return;
            
            Ray ray = dragCam.ScreenPointToRay(eventData.position);
            target.position = ray.GetPoint(planeDistance) + offset;
            
            // Face camera
            Vector3 lookDir = target.position - dragCam.transform.position;
            if (lookDir != Vector3.zero)
            {
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    // Face camera, but no roll
                    target.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;
            if (OnDragEnd != null) OnDragEnd();
        }
    }

    public class UIResizable : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        public RectTransform target;
        public Vector2 minSize = new Vector2(400, 300);
        public Vector2 maxSize = new Vector2(2000, 2000);
        public int anchor = AnchorPresets.bottomRight;
        public UnityAction<bool> onResizeStatusChange;
        private Camera dragCam;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            // Consume the drag start event so it doesn't bubble up to parent UIDraggable
            dragCam = eventData.pressEventCamera;
            if (dragCam == null) dragCam = Camera.main;
            
            if (onResizeStatusChange != null) onResizeStatusChange(true);
        }
        
        public void OnEndDrag(PointerEventData eventData)
        {
            if (onResizeStatusChange != null) onResizeStatusChange(false);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null || target.parent == null) return;
            if (dragCam == null) return;

            RectTransform parentRect = target.parent as RectTransform;
            if (parentRect == null) return;

            // 3. Get Mouse Position in Parent Local Space
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, dragCam, out Vector2 localMouse))
            {
                Vector2 pos = target.anchoredPosition;
                Vector2 size = target.sizeDelta;
                Vector2 newPos = pos;
                Vector2 newSize = size;

                if (anchor == AnchorPresets.bottomRight) 
                {
                    // Stationary: Top-Left
                    float stationaryX = pos.x - size.x * 0.5f;
                    float stationaryY = pos.y + size.y * 0.5f;
                    
                    float w = localMouse.x - stationaryX;
                    float h = stationaryY - localMouse.y;
                    
                    w = Mathf.Clamp(w, minSize.x, maxSize.x);
                    h = Mathf.Clamp(h, minSize.y, maxSize.y);
                    
                    newSize = new Vector2(w, h);
                    newPos = new Vector2(stationaryX + w * 0.5f, stationaryY - h * 0.5f);
                }
                else if (anchor == AnchorPresets.topLeft) 
                {
                    // Stationary: Bottom-Right
                    float stationaryX = pos.x + size.x * 0.5f;
                    float stationaryY = pos.y - size.y * 0.5f;

                    float w = stationaryX - localMouse.x;
                    float h = localMouse.y - stationaryY;
                    
                    w = Mathf.Clamp(w, minSize.x, maxSize.x);
                    h = Mathf.Clamp(h, minSize.y, maxSize.y);
                    
                    newSize = new Vector2(w, h);
                    newPos = new Vector2(stationaryX - w * 0.5f, stationaryY + h * 0.5f);
                }
                else if (anchor == AnchorPresets.topRight) 
                {
                    // Stationary: Bottom-Left
                    float stationaryX = pos.x - size.x * 0.5f;
                    float stationaryY = pos.y - size.y * 0.5f;

                    float w = localMouse.x - stationaryX;
                    float h = localMouse.y - stationaryY;
                    
                    w = Mathf.Clamp(w, minSize.x, maxSize.x);
                    h = Mathf.Clamp(h, minSize.y, maxSize.y);
                    
                    newSize = new Vector2(w, h);
                    newPos = new Vector2(stationaryX + w * 0.5f, stationaryY + h * 0.5f);
                }
                else if (anchor == AnchorPresets.bottomLeft) 
                {
                    // Stationary: Top-Right
                    float stationaryX = pos.x + size.x * 0.5f;
                    float stationaryY = pos.y + size.y * 0.5f;

                    float w = stationaryX - localMouse.x;
                    float h = stationaryY - localMouse.y;
                    
                    w = Mathf.Clamp(w, minSize.x, maxSize.x);
                    h = Mathf.Clamp(h, minSize.y, maxSize.y);
                    
                    newSize = new Vector2(w, h);
                    newPos = new Vector2(stationaryX - w * 0.5f, stationaryY - h * 0.5f);
                }
                else if (anchor == AnchorPresets.hStretchBottom || anchor == AnchorPresets.bottomMiddle)
                {
                    // Resize height only (for fixed mode or general use)
                    float stationaryY = pos.y + size.y * 0.5f;
                    float h = stationaryY - localMouse.y;
                    h = Mathf.Clamp(h, minSize.y, maxSize.y);
                    newSize = new Vector2(size.x, h);
                    newPos = new Vector2(pos.x, stationaryY - h * 0.5f);
                }

                if (target.sizeDelta != newSize || target.anchoredPosition != newPos)
                {
                    target.sizeDelta = newSize;
                    target.anchoredPosition = newPos;
                }
            }
        }
    }

    public class UIAnchorResizer : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        public RectTransform target;
        public RectTransform previewTarget;
        public bool deferred = false;
        public bool resizeX = false;
        public bool resizeY = true;
        public float minAnchorX = 0.05f;
        public float maxAnchorX = 0.95f;
        public float minAnchorY = 0.05f;
        public float maxAnchorY = 0.95f;
        public UnityAction<float> onResized;
        public UnityAction<Vector2> onResizedVec2;
        public UnityAction<bool> onResizeStatusChange;
        private Camera dragCam;
        private Vector2 currentAnchors;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            // For ScreenSpaceOverlay, camera MUST be null for RectTransformUtility to work
            Canvas canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                dragCam = null;
            else
                dragCam = eventData.pressEventCamera ?? Camera.main;

            if (target != null) currentAnchors = target.anchorMin;
            if (previewTarget != null) 
            {
                previewTarget.gameObject.SetActive(true);
                previewTarget.anchorMin = target.anchorMin;
                previewTarget.anchorMax = target.anchorMax;
                previewTarget.offsetMin = target.offsetMin;
                previewTarget.offsetMax = target.offsetMax;
            }

            if (onResizeStatusChange != null) onResizeStatusChange(true);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (deferred && target != null)
            {
                target.anchorMin = currentAnchors;
                if (onResized != null && resizeY) onResized(currentAnchors.y);
                if (onResizedVec2 != null) onResizedVec2(currentAnchors);
            }

            if (previewTarget != null) previewTarget.gameObject.SetActive(false);
            if (onResizeStatusChange != null) onResizeStatusChange(false);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null || target.parent == null) return;
            RectTransform parentRect = target.parent as RectTransform;
            if (parentRect == null) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, dragCam, out Vector2 localMouse))
            {
                Vector2 newAnchors = currentAnchors;
                bool changed = false;

                if (resizeX && parentRect.rect.width > 0)
                {
                    float ratioX = (localMouse.x - parentRect.rect.xMin) / parentRect.rect.width;
                    ratioX = Mathf.Clamp(ratioX, minAnchorX, maxAnchorX);
                    if (newAnchors.x != ratioX)
                    {
                        newAnchors.x = ratioX;
                        changed = true;
                    }
                }

                if (resizeY && parentRect.rect.height > 0)
                {
                    float ratioY = (localMouse.y - parentRect.rect.yMin) / parentRect.rect.height;
                    ratioY = Mathf.Clamp(ratioY, minAnchorY, maxAnchorY);
                    if (newAnchors.y != ratioY)
                    {
                        newAnchors.y = ratioY;
                        changed = true;
                    }
                }
                
                if (changed)
                {
                    currentAnchors = newAnchors;
                    
                    if (previewTarget != null)
                    {
                        previewTarget.anchorMin = newAnchors;
                    }

                    if (!deferred)
                    {
                        target.anchorMin = newAnchors;
                        if (onResized != null && resizeY) onResized(newAnchors.y);
                        if (onResizedVec2 != null) onResizedVec2(newAnchors);
                    }
                }
            }
        }
    }

    public class UIDragBlocker : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public void OnBeginDrag(PointerEventData eventData) { eventData.useDragThreshold = false; }
        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) { }
    }

    public class UIHoverBorder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Graphic targetGraphic;
        public Color hoverColor = new Color(1f, 1f, 0f, 1f); // Bright yellow visible highlight
        public float borderSize = 2f;
        public bool isSelected = false; // Add this to keep border visible
        
        private Outline outline;

        void Awake()
        {
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
            if (targetGraphic != null)
            {
                outline = targetGraphic.gameObject.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = targetGraphic.gameObject.AddComponent<Outline>();
                    outline.enabled = false;
                }
                outline.effectDistance = new Vector2(borderSize, -borderSize);
                outline.effectColor = hoverColor;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (outline != null) outline.enabled = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (outline != null && !isSelected) outline.enabled = false;
        }
    }

    public class UIHoverColor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IEndDragHandler
    {
        public Text targetText;
        public Image targetImage;
        public Color normalColor = Color.white;
        public Color hoverColor = Color.green;
        
        private bool isDragging = false;
        private bool isHovering = false;

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovering = true;
            SetColor(hoverColor);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovering = false;
            if (!isDragging)
            {
                SetColor(normalColor);
            }
        }
        
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            isDragging = true;
            SetColor(hoverColor);
        }
        
        public void OnEndDrag(PointerEventData eventData)
        {
            isDragging = false;
            if (!isHovering)
            {
                SetColor(normalColor);
            }
        }
        
        private void SetColor(Color c)
        {
            if (targetText != null) targetText.color = c;
            if (targetImage != null) targetImage.color = c;
        }
    }

    public class UIHoverReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GameObject card;
        public GalleryPanel panel;
        public FileEntry file;
        
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (card) card.SetActive(true);
            if (panel != null && file != null) panel.SetHoverPath(file);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (card) card.SetActive(false);
            if (panel != null) panel.RestoreSelectedHoverPath();
        }
    }

    /// <summary>
    /// Forces a layout element to have a height based on its current width (or vice versa).
    /// Used for 1:1 aspect ratio thumbnails in VerticalLayoutGroups.
    /// </summary>
    public class AspectRatioLayoutElement : MonoBehaviour, ILayoutElement
    {
        public float aspectRatio = 1f;
        public float minWidth => -1;
        public float preferredWidth => -1;
        public float flexibleWidth => -1;
        public float minHeight => -1;
        public float preferredHeight 
        {
            get {
                RectTransform rt = transform as RectTransform;
                if (rt == null) return -1;
                return rt.rect.width / aspectRatio;
            }
        }
        public float flexibleHeight => 0;
        public int layoutPriority => 1;

        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
    }

    public class UIGridAdaptive : MonoBehaviour
    {
        public GridLayoutGroup grid;
        public float minSize = 200f;
        public float maxSize = 260f;
        public float spacing = 10f;
        public bool isVerticalCard = false;
        public int forcedColumnCount = 0;
        
        private RectTransform rt;
        private float lastWidth = -1f;
        private bool lastIsVerticalCard = false;
        private int lastForcedColumnCount = -1;

        void Awake()
        {
            rt = GetComponent<RectTransform>();
            if (grid == null) grid = GetComponent<GridLayoutGroup>();
        }

        void OnEnable()
        {
            UpdateGrid();
        }

        void OnRectTransformDimensionsChange()
        {
            UpdateGrid();
        }

        public void UpdateGrid()
        {
            if (rt == null || grid == null) return;
            float width = rt.rect.width;
            if (width <= 0) return;
            if (Mathf.Abs(width - lastWidth) < 0.1f && isVerticalCard == lastIsVerticalCard && forcedColumnCount == lastForcedColumnCount && grid.cellSize.x > 0) return;
            
            lastWidth = width;
            lastIsVerticalCard = isVerticalCard;
            lastForcedColumnCount = forcedColumnCount;

            float usableWidth = width - grid.padding.left - grid.padding.right;
            if (usableWidth <= 0) return;
            
            int forcedCols = forcedColumnCount;
            if (forcedCols < 0) forcedCols = 0;

            if (isVerticalCard)
            {
                int n = forcedCols > 0 ? forcedCols : 0;
                if (n <= 0)
                {
                    float cardWidth = minSize > 0 ? minSize : 260f;
                    n = Mathf.FloorToInt((usableWidth + spacing) / (cardWidth + spacing));
                    if (n < 1) n = 1;
                }
                if (n < 1) n = 1;
                float actualCardWidth = (usableWidth - (n - 1) * spacing) / n;
                grid.cellSize = new Vector2(actualCardWidth, actualCardWidth * 1.618f);
                return;
            }

            int colCount = forcedCols > 0 ? forcedCols : 0;
            if (colCount <= 0)
            {
                float baseMinSize = minSize > 0 ? minSize : 200f;
                colCount = Mathf.FloorToInt((usableWidth + spacing) / (baseMinSize + spacing));
            }
            if (colCount < 1) colCount = 1;
            
            float cellSize = (usableWidth - (colCount - 1) * spacing) / colCount;
            grid.cellSize = new Vector2(cellSize, cellSize);
        }
    }

    public class VerticalCardInfoSizer : MonoBehaviour
    {
        public LayoutElement infoLE;
        public LayoutElement nameLE;
        public Text nameText;
        public LayoutElement dateLE;
        public GameObject dateGO;
        public float ratingHeight = 45f;
        public float maxInfoHeight = 85f;
        public float minInfoHeight = 30f;
        public float hideDateBelowInfoHeight = 55f;
        public float dateHeight = 22f;

        private RectTransform rt;
        private float lastWidth = -1f;
        private CanvasGroup dateCG;

        void Awake()
        {
            rt = GetComponent<RectTransform>();
            if (dateGO != null)
            {
                dateCG = dateGO.GetComponent<CanvasGroup>();
                if (dateCG == null) dateCG = dateGO.AddComponent<CanvasGroup>();
            }
        }

        void OnEnable()
        {
            UpdateLayout();
        }

        void OnRectTransformDimensionsChange()
        {
            UpdateLayout();
        }

        private void UpdateLayout()
        {
            if (rt == null || infoLE == null || nameLE == null) return;
            float w = rt.rect.width;
            if (w <= 0) return;
            if (Mathf.Abs(w - lastWidth) < 0.1f) return;
            lastWidth = w;

            float remaining = w * 0.618f;
            float desiredInfo = remaining - ratingHeight;
            desiredInfo = Mathf.Clamp(desiredInfo, minInfoHeight, maxInfoHeight);

            infoLE.minHeight = 0;
            infoLE.preferredHeight = desiredInfo;
            infoLE.flexibleHeight = 0;

            bool showDate = desiredInfo >= hideDateBelowInfoHeight;
            if (dateLE != null)
            {
                dateLE.minHeight = showDate ? dateHeight : 0f;
                dateLE.preferredHeight = showDate ? dateHeight : 0f;
                dateLE.flexibleHeight = 0f;
            }
            if (dateCG != null)
            {
                dateCG.alpha = showDate ? 1f : 0f;
                dateCG.interactable = false;
                dateCG.blocksRaycasts = false;
            }

            if (showDate)
            {
                nameLE.minHeight = 42;
                nameLE.preferredHeight = 42;
                if (dateLE != null)
                {
                    dateLE.minHeight = dateHeight;
                    dateLE.preferredHeight = dateHeight;
                    dateLE.flexibleHeight = 0;
                }
                if (nameText != null) nameText.fontSize = 20;
            }
            else
            {
                nameLE.minHeight = 24;
                nameLE.preferredHeight = desiredInfo;
                if (nameText != null) nameText.fontSize = 18;
            }
        }
    }

    /// <summary>
    /// Smoothly bends UI vertices along a cylinder.
    /// </summary>
    public class CurvedUIVertexModifier : MonoBehaviour, IMeshModifier
    {
        public RectTransform canvasRT;
        
        public void ModifyMesh(Mesh mesh) { }
        public void ModifyMesh(VertexHelper vh)
        {
            if (!enabled || VPBConfig.Instance == null || !VPBConfig.Instance.EnableCurvature) return;
            if (canvasRT == null) return;

            // 0. Subdivide if it's just a simple quad (like background images)
            // Use a higher subdivision for larger elements to avoid artifacts
            if (vh.currentVertCount == 4)
            {
                RectTransform rt = transform as RectTransform;
                float width = rt != null ? rt.rect.width : 100f;
                int segments = width > 500 ? 50 : 15; 
                SubdivideQuad(vh, segments);
            }

            // Use a stable reference distance for radius calculation (2 meters)
            float radius = 2.0f;
            radius *= (1.0f / VPBConfig.Instance.CurvatureIntensity);
            if (radius < 0.1f) radius = 0.1f;

            // We need to know the scale to convert between local (pixels) and world units
            float scaleX = canvasRT.lossyScale.x;
            if (scaleX == 0) scaleX = 0.001f;

            // Cache transformations
            Matrix4x4 localToCanvas = canvasRT.worldToLocalMatrix * transform.localToWorldMatrix;
            Matrix4x4 canvasToLocal = transform.worldToLocalMatrix * canvasRT.localToWorldMatrix;

            // Small Z-bias based on hierarchy depth to prevent Z-fighting
            float zBias = 0f;
            Transform p = transform.parent;
            while (p != null && p != canvasRT.transform) { zBias += 0.1f; p = p.parent; }

            UIVertex v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                
                // 1. To Canvas Local Space
                Vector3 cPos = localToCanvas.MultiplyPoint3x4(v.position);
                
                // 2. Apply Cylinder Bend
                // Convert local X to world-scale X for the angle calculation
                float worldX = cPos.x * scaleX;
                float angle = worldX / radius;
                
                // Calculate new position in world-scale local space
                // Wrapping TOWARD user: z becomes negative as |x| increases
                float newWorldX = Mathf.Sin(angle) * radius;
                float newWorldZ = (Mathf.Cos(angle) - 1.0f) * radius;
                
                // 3. Back to Local Space
                cPos.x = newWorldX / scaleX;
                cPos.z = (newWorldZ - zBias * 0.001f) / scaleX; // Apply small Z-bias toward camera
                
                v.position = canvasToLocal.MultiplyPoint3x4(cPos);
                vh.SetUIVertex(v, i);
            }
        }

        private void SubdivideQuad(VertexHelper vh, int segments)
        {
            List<UIVertex> verts = new List<UIVertex>();
            // PopulateUIVertex is safer than GetUIVertexStream for raw quads
            for (int i = 0; i < 4; i++) { UIVertex v = new UIVertex(); vh.PopulateUIVertex(ref v, i); verts.Add(v); }

            UIVertex vLB = verts[0];
            UIVertex vLT = verts[1];
            UIVertex vRT = verts[2];
            UIVertex vRB = verts[3];

            vh.Clear();

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                UIVertex vBottom = LerpVertex(vLB, vRB, t);
                UIVertex vTop = LerpVertex(vLT, vRT, t);
                vh.AddVert(vBottom);
                vh.AddVert(vTop);
            }

            for (int i = 0; i < segments; i++)
            {
                int baseIdx = i * 2;
                vh.AddTriangle(baseIdx, baseIdx + 1, baseIdx + 3);
                vh.AddTriangle(baseIdx, baseIdx + 3, baseIdx + 2);
            }
        }

        private UIVertex LerpVertex(UIVertex a, UIVertex b, float t)
        {
            UIVertex v = new UIVertex();
            v.position = Vector3.Lerp(a.position, b.position, t);
            v.color = Color32.Lerp(a.color, b.color, t);
            v.uv0 = Vector2.Lerp(a.uv0, b.uv0, t);
            v.uv1 = Vector2.Lerp(a.uv1, b.uv1, t);
            v.normal = Vector3.Lerp(a.normal, b.normal, t);
            v.tangent = Vector4.Lerp(a.tangent, b.tangent, t);
            return v;
        }
    }

    /// <summary>
    /// Custom raycaster that un-bends the laser ray to match the curved UI.
    /// </summary>
    public class CylindricalGraphicRaycaster : GraphicRaycaster
    {
        private RectTransform canvasRT;

        protected override void Start()
        {
            base.Start();
            canvasRT = GetComponent<RectTransform>();
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppend)
        {
            if (VPBConfig.Instance == null || !VPBConfig.Instance.EnableCurvature || canvasRT == null)
            {
                base.Raycast(eventData, resultAppend);
                return;
            }

            Camera eventCam = eventData.pressEventCamera ?? Camera.main;
            if (eventCam == null) return;
            Ray ray = eventCam.ScreenPointToRay(eventData.position);

            // 1. Check for hits on flat colliders (like the Settings Panel)
            // We use RaycastAll to find if we hit any of our UI colliders, even if something else is in the way.
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
            foreach (var h in hits)
            {
                if (h.collider is BoxCollider && h.collider.transform.IsChildOf(canvasRT))
                {
                    // For flat panels that are just rotated/moved in 3D (no vertex distortion),
                    // we map the physical hit point to screen space for the standard raycaster.
                    Vector3 localHit = h.collider.transform.InverseTransformPoint(h.point);
                    localHit.z = 0; // Map to the UI plane
                    Vector3 worldHit = h.collider.transform.TransformPoint(localHit);
                    
                    Vector2 originalScreenPos = eventData.position;
                    eventData.position = eventCam.WorldToScreenPoint(worldHit);
                    base.Raycast(eventData, resultAppend);
                    eventData.position = originalScreenPos;
                    
                    // If we found a valid UI hit on our panel, we can stop
                    if (resultAppend.Count > 0) return;
                }
            }

            // 2. Perform cylindrical unwrapping for the curved parts
            // We no longer guard this with a physical raycast check to ensure standard UI elements 
            // without colliders still work correctly if they are within the curved volume.
            Vector3 localOrigin = canvasRT.InverseTransformPoint(ray.origin);
            Vector3 localDir = canvasRT.InverseTransformDirection(ray.direction);

            // Use same stable reference distance as the modifier
            float radius = 2.0f;
            radius *= (1.0f / VPBConfig.Instance.CurvatureIntensity);
            if (radius < 0.1f) radius = 0.1f;

            float scaleX = canvasRT.lossyScale.x;
            if (scaleX == 0) scaleX = 0.001f;
            
            // Convert local units to world-scale for intersection math
            localOrigin.x *= scaleX;
            localOrigin.z *= scaleX;
            localDir.x *= scaleX;
            localDir.z *= scaleX;

            // Intersection with Cylinder: x^2 + (z + R)^2 = R^2
            // Center is at (0, 0, -R)
            float A = localDir.x * localDir.x + localDir.z * localDir.z;
            float B = 2 * (localOrigin.x * localDir.x + (localOrigin.z + radius) * localDir.z);
            float C = localOrigin.x * localOrigin.x + (localOrigin.z + radius) * (localOrigin.z + radius) - radius * radius;

            float disc = B * B - 4 * A * C;
            if (disc < 0) return;

            float t = (-B - Mathf.Sqrt(disc)) / (2 * A);
            if (t < 0) t = (-B + Mathf.Sqrt(disc)) / (2 * A);
            if (t < 0) return;

            Vector3 hitPoint = localOrigin + localDir * t;

            // Map back to flat plane coordinates: x_flat = angle * radius
            float x_flat_world = Mathf.Atan2(hitPoint.x, hitPoint.z + radius) * radius;
            float x_flat_local = x_flat_world / scaleX;
            float y_flat_local = hitPoint.y; // Y is unaffected by cylinder wrap

            Vector3 flatWorldPos = canvasRT.TransformPoint(new Vector3(x_flat_local, y_flat_local, 0));
            Vector2 screenPos = eventCam.WorldToScreenPoint(flatWorldPos);

            Vector2 originalPos = eventData.position;
            eventData.position = screenPos;
            base.Raycast(eventData, resultAppend);
            eventData.position = originalPos;
        }
    }

    public class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public FileEntry FileEntry;
        public Hub.GalleryHubItem HubItem;
        public RawImage ThumbnailImage;
        public GalleryPanel Panel;
        
        private bool? _isDualPose = null;
        private JSONNode _dualPoseNode = null;
        
        private bool isDraggingItem = false;
        private GameObject ghostObject;
        private Image ghostBorder;
        private Text ghostText; // Added text component
        private Renderer ghostRenderer;
        private GameObject groundIndicator;
        private Vector3 lastGroundPoint;
        private bool hasGroundPoint;
        // private Vector3 offset; // Unused
        private float planeDistance;
        private Camera dragCam;

        private static Dictionary<string, HashSet<string>> _globalRegionCache = new Dictionary<string, HashSet<string>>();
        private static string _lastAppearanceClothingMode = "keep";

        public static HashSet<string> GetTagSetForClothingItem(object item)
        {
            if (item == null) return null;
            try
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Type t = item.GetType();

                // Common patterns seen in VaM objects / mods
                object tagsObj = null;
                FieldInfo f = t.GetField("tags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) tagsObj = f.GetValue(item);
                if (tagsObj == null)
                {
                    PropertyInfo p = t.GetProperty("tags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead) tagsObj = p.GetValue(item, null);
                }

                if (tagsObj is IEnumerable<string> tagsEnum)
                {
                    foreach (string s in tagsEnum)
                    {
                        if (string.IsNullOrEmpty(s)) continue;
                        set.Add(s.Trim().ToLowerInvariant());
                    }
                }
                else if (tagsObj is string tagStr)
                {
                    if (!string.IsNullOrEmpty(tagStr))
                    {
                        // Some implementations store comma-separated tags
                        var parts = tagStr.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++)
                        {
                            string s = parts[i].Trim();
                            if (!string.IsNullOrEmpty(s)) set.Add(s.ToLowerInvariant());
                        }
                    }
                }

                // Body-region style properties sometimes exist
                string[] extraNames = new string[] { "bodyRegion", "region", "clothingType", "type", "category", "slot" };
                for (int i = 0; i < extraNames.Length; i++)
                {
                    string name = extraNames[i];
                    try
                    {
                        FieldInfo ef = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (ef != null)
                        {
                            object v = ef.GetValue(item);
                            if (v is string vs && !string.IsNullOrEmpty(vs)) set.Add(vs.Trim().ToLowerInvariant());
                        }
                        else
                        {
                            PropertyInfo ep = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (ep != null && ep.CanRead)
                            {
                                object v = ep.GetValue(item, null);
                                if (v is string vs && !string.IsNullOrEmpty(vs)) set.Add(vs.Trim().ToLowerInvariant());
                            }
                        }
                    }
                    catch { }
                }

                return set.Count > 0 ? set : null;
            }
            catch
            {
                return null;
            }
        }

        private void PushUndoSnapshotForClothingHair(Atom target)
        {
            if (Panel == null || target == null) return;
            try
            {
                string atomUid = target.uid;

                Dictionary<string, bool> geometryToggleSnapshot = null;
                JSONClass clothingSnapshot = null;
                JSONClass hairSnapshot = null;

                JSONStorable geo = target.GetStorableByID("geometry");
                if (geo != null)
                {
                    List<string> names = geo.GetBoolParamNames();
                    if (names != null)
                    {
                        geometryToggleSnapshot = new Dictionary<string, bool>();
                        foreach (string key in names)
                        {
                            if (key.StartsWith("clothing:") || key.StartsWith("hair:"))
                            {
                                JSONStorableBool b = geo.GetBoolJSONParam(key);
                                if (b != null) geometryToggleSnapshot[key] = b.val;
                            }
                        }
                    }
                }

                JSONStorable clothing = target.GetStorableByID("Clothing");
                if (clothing != null)
                {
                    clothingSnapshot = clothing.GetJSON();
                }

                JSONStorable hair = target.GetStorableByID("Hair");
                if (hair != null)
                {
                    hairSnapshot = hair.GetJSON();
                }

                Panel.PushUndo(() =>
                {
                    Atom undoAtom = SuperController.singleton.GetAtomByUid(atomUid);
                    if (undoAtom == null) return;

                    if (geometryToggleSnapshot != null)
                    {
                        JSONStorable undoGeo = undoAtom.GetStorableByID("geometry");
                        if (undoGeo != null)
                        {
                            foreach (var kvp in geometryToggleSnapshot)
                            {
                                JSONStorableBool b = undoGeo.GetBoolJSONParam(kvp.Key);
                                if (b != null) b.val = kvp.Value;
                            }
                        }
                    }

                    if (clothingSnapshot != null)
                    {
                        JSONStorable undoClothing = undoAtom.GetStorableByID("Clothing");
                        if (undoClothing != null) undoClothing.RestoreFromJSON(clothingSnapshot);
                    }

                    if (hairSnapshot != null)
                    {
                        JSONStorable undoHair = undoAtom.GetStorableByID("Hair");
                        if (undoHair != null) undoHair.RestoreFromJSON(hairSnapshot);
                    }
                });
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PushUndoSnapshotForClothingHair exception: " + ex);
            }
        }

        private bool PushUndoSnapshotForFullAtomState(Atom atom)
        {
            if (Panel == null || atom == null)
            {
                LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: Panel or atom null");
                return false;
            }
            if (SuperController.singleton == null)
            {
                LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: SuperController.singleton null");
                return false;
            }

            try
            {
                LogUtil.Log("[VPB] PushUndoSnapshotForFullAtomState called for " + atom.uid + " (" + atom.type + ")");
            }
            catch { }

            try
            {
                string atomUid = null;
                try { atomUid = atom.uid; } catch { }
                if (string.IsNullOrEmpty(atomUid))
                {
                    LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: atom uid empty");
                    return false;
                }

                JSONNode atomNode = null;
                try
                {
                    string[] candidates = new[] { "GetSaveJSON", "GetJSON", "GetAtomJSON", "GetSceneJSON" };
                    for (int i = 0; i < candidates.Length && atomNode == null; i++)
                    {
                        MethodInfo mi = null;
                        try { mi = atom.GetType().GetMethod(candidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch { }
                        if (mi == null) continue;
                        var ps = mi.GetParameters();
                        if (ps != null && ps.Length != 0) continue;
                        object result = null;
                        try { result = mi.Invoke(atom, null); }
                        catch { }
                        if (result == null) continue;
                        if (result is JSONNode node)
                        {
                            atomNode = node;
                        }
                        else
                        {
                            try
                            {
                                string s = result.ToString();
                                if (!string.IsNullOrEmpty(s)) atomNode = JSON.Parse(s);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                if (atomNode == null)
                {
                    LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: failed to serialize atom " + atomUid + "; attempting scene-save fallback");

                    try
                    {
                        SuperController sc = SuperController.singleton;
                        if (sc == null) return false;
                        JSONNode sceneRoot = null;
                        try
                        {
                            string[] sceneCandidates = new[]
                            {
                                "GetSaveJSON",
                                "GetSaveSceneJSON",
                                "GetSceneJSON",
                                "GetJSON",
                                "GetSaveJson",
                                "GetSceneJson",
                            };

                            for (int i = 0; i < sceneCandidates.Length && sceneRoot == null; i++)
                            {
                                MethodInfo mi = null;
                                try { mi = sc.GetType().GetMethod(sceneCandidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
                                catch { }
                                if (mi == null) continue;
                                var ps = mi.GetParameters();
                                if (ps != null && ps.Length != 0) continue;

                                object result = null;
                                try { result = mi.Invoke(sc, null); }
                                catch { }
                                if (result == null) continue;

                                if (result is JSONNode node)
                                {
                                    sceneRoot = node;
                                }
                                else
                                {
                                    try
                                    {
                                        string s = result.ToString();
                                        if (!string.IsNullOrEmpty(s)) sceneRoot = JSON.Parse(s);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        if (sceneRoot == null || sceneRoot["atoms"] == null)
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: scene JSON reflection failed for " + atomUid);
                            return false;
                        }

                        JSONArray atoms = null;
                        try { atoms = sceneRoot["atoms"].AsArray; } catch { }
                        if (atoms == null)
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: scene atoms missing for " + atomUid);
                            return false;
                        }

                        JSONArray newAtoms = new JSONArray();
                        for (int i = 0; i < atoms.Count; i++)
                        {
                            JSONNode a = atoms[i];
                            if (a == null) continue;
                            try
                            {
                                if (a["id"] != null && string.Equals(a["id"].Value, atomUid, StringComparison.OrdinalIgnoreCase))
                                {
                                    newAtoms.Add(a);
                                }
                            }
                            catch { }
                        }

                        if (newAtoms.Count == 0)
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: atom not found in scene JSON for " + atomUid);
                            return false;
                        }

                        JSONClass miniScene = new JSONClass();
                        miniScene["atoms"] = newAtoms;

                        string undoTempPathFallback = Path.Combine(sc.savesDir, "vpb_temp_undo_atom_" + Guid.NewGuid().ToString() + ".json");
                        try { File.WriteAllText(undoTempPathFallback, miniScene.ToString()); }
                        catch
                        {
                            LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: failed to write temp scene for " + atomUid);
                            return false;
                        }

                        try
                        {
                            string loadPath = UI.NormalizePath(undoTempPathFallback);
                            Panel.PushUndo(() => {
                                try
                                {
                                    if (SuperController.singleton == null) return;
                                    if (!File.Exists(undoTempPathFallback)) return;
                                    SceneLoadingUtils.LoadScene(loadPath, true);
                                }
                                catch { }
                                finally
                                {
                                    try { if (File.Exists(undoTempPathFallback)) File.Delete(undoTempPathFallback); } catch { }
                                }
                            });
                        }
                        catch { }

                        LogUtil.Log("[VPB] Undo snapshot pushed (full atom via scene-json): " + atomUid);
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        LogUtil.LogError("[VPB] PushUndoSnapshotForFullAtomState fallback exception: " + ex2);
                        return false;
                    }
                }

                try
                {
                    if (atomNode["id"] == null || string.IsNullOrEmpty(atomNode["id"].Value)) atomNode["id"] = atomUid;
                }
                catch { }

                string atomJson = null;
                try { atomJson = atomNode.ToString(); }
                catch { }
                if (string.IsNullOrEmpty(atomJson))
                {
                    LogUtil.LogWarning("[VPB] PushUndoSnapshotForFullAtomState: atom json empty for " + atomUid);
                    return false;
                }

                JSONClass mini = new JSONClass();
                JSONArray one = new JSONArray();
                try { one.Add(JSON.Parse(atomJson)); }
                catch { one.Add(atomNode); }
                mini["atoms"] = one;

                string undoTempPath = Path.Combine(SuperController.singleton.savesDir, "vpb_temp_undo_atom_" + Guid.NewGuid().ToString() + ".json");
                File.WriteAllText(undoTempPath, mini.ToString());

                Panel.PushUndo(() => {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        if (!File.Exists(undoTempPath)) return;
                        SceneLoadingUtils.LoadScene(undoTempPath, true);
                    }
                    catch { }
                    finally
                    {
                        try { if (File.Exists(undoTempPath)) File.Delete(undoTempPath); } catch { }
                    }
                });

                LogUtil.Log("[VPB] Undo snapshot pushed (full atom): " + atomUid);
                return true;
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PushUndoSnapshotForFullAtomState exception: " + ex);
                return false;
            }
        }

        private bool IsPluginLikeStorableId(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            if (string.Equals(sid, "PluginPresets", StringComparison.OrdinalIgnoreCase)) return false;
            if (sid.IndexOf("plugin#", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("clothingplugin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("hairplugin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("plugindestructor", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("stopper.", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.IndexOf("plugin", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private IEnumerator PostUndoPersonRefreshCoroutine(string atomUid, JSONClass geometrySnapshot, JSONClass skinSnapshot, int framesToWait)
        {
            if (framesToWait < 1) framesToWait = 1;
            for (int i = 0; i < framesToWait; i++)
            {
                yield return new WaitForEndOfFrame();
            }

            Atom targetAtom = null;
            try { targetAtom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null; } catch { }
            if (targetAtom == null) yield break;

            try
            {
                if (geometrySnapshot != null)
                {
                    JSONStorable geo = null;
                    try { geo = targetAtom.GetStorableByID("geometry"); } catch { }
                    try { if (geo != null) geo.RestoreFromJSON(geometrySnapshot); } catch { }
                }
            }
            catch { }

            try
            {
                if (skinSnapshot != null)
                {
                    JSONStorable skin = null;
                    try { skin = targetAtom.GetStorableByID("Skin"); } catch { }
                    try { if (skin != null) skin.RestoreFromJSON(skinSnapshot); } catch { }
                }
            }
            catch { }

            try
            {
                DAZCharacterSelector dcs = null;
                try { dcs = targetAtom.GetComponentInChildren<DAZCharacterSelector>(); } catch { }
                if (dcs != null)
                {
                    string[] methodCandidates = new[] { "Refresh", "RefreshAll", "RefreshGeometry", "RefreshSkin", "ResetSkin", "ResetMaterials", "SyncSkin", "SyncMaterials" };
                    for (int i = 0; i < methodCandidates.Length; i++)
                    {
                        MethodInfo mi = null;
                        try { mi = dcs.GetType().GetMethod(methodCandidates[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); } catch { }
                        if (mi == null) continue;
                        var ps = mi.GetParameters();
                        if (ps != null && ps.Length != 0) continue;
                        try { mi.Invoke(dcs, null); } catch { }
                    }
                }
            }
            catch { }
        }

        private JSONClass ExtractAtomFromScene(JSONClass sceneJSON, string atomType)
        {
            if (sceneJSON == null || sceneJSON["atoms"] == null) return null;
            
            JSONArray atoms = sceneJSON["atoms"].AsArray;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (atoms[i]["type"].Value == atomType)
                {
                    JSONClass personAtom = atoms[i].AsObject;
                    JSONClass extracted = new JSONClass();
                    extracted["storables"] = personAtom["storables"];
                    if (personAtom["setUnlistedParamsToDefault"] != null)
                        extracted["setUnlistedParamsToDefault"] = personAtom["setUnlistedParamsToDefault"];
                    return extracted;
                }
            }
            return null;
        }

        private bool CheckDualPose()
        {
            if (_isDualPose.HasValue) return _isDualPose.Value;
            
            _isDualPose = false;
            
            if (FileEntry != null && FileEntry.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                // Try reading using SuperController.singleton.ReadFileIntoString first if path is normalized or manageable
                // Otherwise try stream
                
                string content = null;
                try
                {
                    // Prefer using FileManager or SuperController which handles reading better
                    string normalized = UI.NormalizePath(FileEntry.Path);
                    if (normalized.Contains(":")) // Var
                    {
                         // Use OpenStreamReader for vars as it handles the archive access
                         using (var reader = FileEntry.OpenStreamReader())
                         {
                             content = reader.ReadToEnd();
                         }
                    }
                    else
                    {
                        // For loose files, standard file IO might be safer or SuperController
                        // But FileEntry.OpenStreamReader should ideally work.
                        // However, let's try SuperController read if it's a file path
                         using (var reader = FileEntry.OpenStreamReader())
                         {
                             content = reader.ReadToEnd();
                         }
                    }

                    if (!string.IsNullOrEmpty(content))
                    {
                        _dualPoseNode = JSON.Parse(content);
                        if (_dualPoseNode != null)
                        {
                            // Check PeopleCount (string or int)
                            if (_dualPoseNode["PeopleCount"] != null)
                            {
                                int count = _dualPoseNode["PeopleCount"].AsInt;
                                if (count >= 2)
                                {
                                    _isDualPose = true;
                                    LogUtil.Log($"[DragDropDebug] Detected Dual Pose: PeopleCount={count} in {FileEntry.Name}");
                                }
                                else
                                {
                                    LogUtil.Log($"[DragDropDebug] Not Dual Pose: PeopleCount={count} in {FileEntry.Name}");
                                }
                            }
                            else
                            {
                                 // LogUtil.Log($"[DragDropDebug] Not Dual Pose: No PeopleCount in {FileEntry.Name}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                     LogUtil.LogError($"[DragDropDebug] CheckDualPose error reading {FileEntry.Name}: {ex.Message}");
                }
            }
            return _isDualPose.Value;
        }

        private static string GetItemKeyForMatching(string actualItemName)
        {
            if (string.IsNullOrEmpty(actualItemName)) return "";

            string s = actualItemName;
            int colonIndex = s.IndexOf(":/", StringComparison.Ordinal);
            if (colonIndex >= 0)
            {
                s = s.Substring(colonIndex + 2);
            }

            s = s.Replace('\\', '/');
            int slash = s.LastIndexOf('/');
            if (slash >= 0 && slash < s.Length - 1)
            {
                s = s.Substring(slash + 1);
            }

            if (s.EndsWith(".vam", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(0, s.Length - 4);
            }

            return s;
        }

        private static void TryGetCreatorFromPresetPath(string presetPath, bool isClothing, out string creator)
        {
            creator = "";
            if (string.IsNullOrEmpty(presetPath)) return;

            string p = presetPath.Replace('\\', '/');
            string[] parts = p.Split('/');
            if (parts == null || parts.Length < 6) return;

            int idx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], "Clothing", StringComparison.OrdinalIgnoreCase) && isClothing)
                {
                    idx = i;
                    break;
                }
                if (string.Equals(parts[i], "Hair", StringComparison.OrdinalIgnoreCase) && !isClothing)
                {
                    idx = i;
                    break;
                }
            }

            // Expected: Custom/Clothing/Female/<creator>/<item>/<preset>.vap
            // Expected: Custom/Hair/Female/<creator>/<item>/<preset>.vap
            if (idx >= 0 && idx + 2 < parts.Length)
            {
                int creatorIdx = idx + 2;
                if (creatorIdx >= 0 && creatorIdx < parts.Length)
                {
                    creator = parts[creatorIdx] ?? "";
                }
            }
        }

        private static JSONStorable FindItemPresetStorable(Atom atom, string itemUid, string itemName, string creator, out string storableId)
        {
            storableId = null;
            if (atom == null) return null;

            // Preferred ids (match VaM)
            if (!string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(itemName))
            {
                storableId = creator + ":" + itemName + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;

                // Check without Preset suffix (e.g. Sim storables)
                storableId = creator + ":" + itemName;
                s = atom.GetStorableByID(storableId);
                if (s != null && s.GetComponentInChildren<MeshVR.PresetManager>() != null) return s;
            }

            if (!string.IsNullOrEmpty(itemName))
            {
                storableId = itemName + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;

                // Check without Preset suffix
                storableId = itemName;
                s = atom.GetStorableByID(storableId);
                if (s != null && s.GetComponentInChildren<MeshVR.PresetManager>() != null) return s;
            }

            if (!string.IsNullOrEmpty(itemUid))
            {
                storableId = itemUid + "Preset";
                JSONStorable s = atom.GetStorableByID(storableId);
                if (s != null) return s;
            }

            // Fallback: search all storables for name match
            foreach (string sid in atom.GetStorableIDs())
            {
                if (sid.EndsWith("Preset", StringComparison.OrdinalIgnoreCase) &&
                    (!string.IsNullOrEmpty(itemName) && sid.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    JSONStorable s = atom.GetStorableByID(sid);
                    if (s != null && s.GetComponentInChildren<MeshVR.PresetManager>() != null)
                    {
                        storableId = sid;
                        return s;
                    }
                }
            }

            storableId = null;
            return null;
        }

        private static JSONClass LoadPresetJsonWithPathFixups(string normalizedPresetPath)
        {
            if (string.IsNullOrEmpty(normalizedPresetPath)) return null;

            JSONNode node = SuperController.singleton.LoadJSON(normalizedPresetPath);
            JSONClass presetJSON = (node != null) ? node.AsObject : null;
            if (presetJSON == null) return null;

            if (normalizedPresetPath.Contains(":"))
            {
                string presetPackageName = normalizedPresetPath.Substring(0, normalizedPresetPath.IndexOf(':'));
                string folderFullPath = MVR.FileManagementSecure.FileManagerSecure.GetDirectoryName(normalizedPresetPath);
                folderFullPath = MVR.FileManagementSecure.FileManagerSecure.NormalizeLoadPath(folderFullPath);

                string presetJSONString = presetJSON.ToString();
                bool modified = false;

                if (presetJSONString.Contains("SELF:"))
                {
                    presetJSONString = presetJSONString.Replace("SELF:", presetPackageName + ":");
                    modified = true;
                }

                if (presetJSONString.Contains("\":\"./"))
                {
                    presetJSONString = presetJSONString.Replace("\":\"./", "\":\"" + folderFullPath + "/");
                    modified = true;
                }

                if (modified)
                {
                    JSONNode parsed = SimpleJSON.JSON.Parse(presetJSONString);
                    presetJSON = (parsed != null) ? parsed.AsObject : presetJSON;
                }

                bool fixedCustomPaths = false;
                FixupUnprefixedCustomPathsInVarPreset(presetJSON, presetPackageName, ref fixedCustomPaths);
            }

            return presetJSON;
        }

        private static void FixupUnprefixedCustomPathsInVarPreset(JSONNode node, string presetPackageName, ref bool modified)
        {
            if (node == null || string.IsNullOrEmpty(presetPackageName)) return;

            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                foreach (KeyValuePair<string, JSONNode> kvp in obj)
                {
                    FixupUnprefixedCustomPathsInVarPreset(kvp.Value, presetPackageName, ref modified);
                }
                return;
            }

            JSONArray arr = node as JSONArray;
            if (arr != null)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    FixupUnprefixedCustomPathsInVarPreset(arr[i], presetPackageName, ref modified);
                }
                return;
            }

            string v = node.Value;
            if (string.IsNullOrEmpty(v)) return;
            if (v.IndexOf(':') >= 0) return;

            string vNorm = v.Replace('\\', '/');
            if (!vNorm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase)) return;

            string candidate = presetPackageName + ":/" + vNorm;
            string normalizedCandidate = MVR.FileManagementSecure.FileManagerSecure.NormalizePath(candidate);
            if (MVR.FileManagementSecure.FileManagerSecure.FileExists(normalizedCandidate))
            {
                node.Value = candidate;
                modified = true;
            }
        }

        private static string LongestCommonPrefix(List<string> values)
        {
            if (values == null || values.Count == 0) return "";
            string prefix = values[0] ?? "";
            for (int i = 1; i < values.Count; i++)
            {
                string s = values[i] ?? "";
                int j = 0;
                int max = Mathf.Min(prefix.Length, s.Length);
                while (j < max && prefix[j] == s[j]) j++;
                prefix = prefix.Substring(0, j);
                if (prefix.Length == 0) break;
            }
            return prefix;
        }

        private static string InferClothingHairBaseIdFromPresetJson(JSONClass presetJSON)
        {
            if (presetJSON == null || presetJSON["storables"] == null) return "";
            JSONArray storables = presetJSON["storables"].AsArray;
            if (storables == null || storables.Count == 0) return "";

            var baseCandidates = new List<string>();
            var allIds = new List<string>();

            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode node = storables[i];
                if (node == null || node["id"] == null) continue;
                string id = node["id"].Value;
                if (string.IsNullOrEmpty(id)) continue;

                allIds.Add(id);

                if (id.EndsWith("Material", StringComparison.Ordinal))
                {
                    baseCandidates.Add(id.Substring(0, id.Length - 8));
                    continue;
                }

                if (id.EndsWith("Sim", StringComparison.Ordinal))
                {
                    baseCandidates.Add(id.Substring(0, id.Length - 3));
                    continue;
                }

                if (id.EndsWith("Physics", StringComparison.Ordinal))
                {
                    baseCandidates.Add(id.Substring(0, id.Length - 7));
                    continue;
                }
            }

            // Prefer the most common candidate base (best signal for clothing item presets)
            if (baseCandidates.Count > 0)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string c in baseCandidates)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    counts[c] = counts.TryGetValue(c, out int n) ? (n + 1) : 1;
                }
                if (counts.Count > 0)
                {
                    string best = null;
                    int bestCount = -1;
                    foreach (var kvp in counts)
                    {
                        if (kvp.Value > bestCount)
                        {
                            best = kvp.Key;
                            bestCount = kvp.Value;
                        }
                    }
                    if (!string.IsNullOrEmpty(best)) return NormalizeInferredBaseId(best);
                }
            }

            // Fallback: longest common prefix across all ids, then trim to a safe boundary
            string lcp = LongestCommonPrefix(allIds);
            if (string.IsNullOrEmpty(lcp)) return "";
            return NormalizeInferredBaseId(lcp);
        }

        private static string NormalizeInferredBaseId(string baseId)
        {
            if (string.IsNullOrEmpty(baseId)) return "";
            string s = baseId;

            // Many "Sim" / "Material" storables use an underscore separator before the suffix.
            while (s.EndsWith("_", StringComparison.Ordinal) || s.EndsWith("-", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - 1);
                if (s.Length == 0) break;
            }

            return s;
        }

        private static string ExtractKeyFromInferredBaseId(string inferredBaseId)
        {
            if (string.IsNullOrEmpty(inferredBaseId)) return "";
            string s = NormalizeInferredBaseId(inferredBaseId);
            int colon = s.IndexOf(':');
            if (colon >= 0 && colon < s.Length - 1)
            {
                s = s.Substring(colon + 1);
            }
            return s;
        }

        private static IEnumerator ActivateClothingHairItemPresetCoroutine(Atom atom, FileEntry entry, bool isClothing, string itemUid, string itemName)
        {
            if (atom == null || entry == null) yield break;

            string normalizedPath = UI.NormalizePath(entry.Path);
            string creator;
            TryGetCreatorFromPresetPath(entry.Path, isClothing, out creator);

            // Load preset JSON first so we can infer the real storable prefix for variant folders.
            JSONClass presetJSON = LoadPresetJsonWithPathFixups(normalizedPath);
            string inferredBaseId = InferClothingHairBaseIdFromPresetJson(presetJSON);

            string lookupName = !string.IsNullOrEmpty(inferredBaseId) ? inferredBaseId : itemName;
            LogUtil.Log($"[DragDropDebug] Waiting for item preset storable. isClothing={isClothing}, itemName={itemName}, inferredBaseId={inferredBaseId}, itemUid={itemUid}, creator={creator}, presetPath={normalizedPath}");

            DateTime startDelayTime = DateTime.Now;
            while ((DateTime.Now - startDelayTime).TotalSeconds < 10)
            {
                string storableId;
                JSONStorable presetStorable = FindItemPresetStorable(atom, itemUid, itemName, creator, out storableId);
                MeshVR.PresetManager pm = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;

                if (pm == null && !string.IsNullOrEmpty(inferredBaseId))
                {
                    presetStorable = FindItemPresetStorable(atom, itemUid, inferredBaseId, creator, out storableId);
                    pm = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;
                }

                if (pm == null && !string.IsNullOrEmpty(inferredBaseId))
                {
                    // Direct check by inferred base id
                    string directId = inferredBaseId + "Preset";
                    presetStorable = atom.GetStorableByID(directId);
                    pm = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;
                    if (pm != null)
                    {
                        storableId = directId;
                    }
                }

                if (pm != null)
                {
                    if (presetJSON == null)
                    {
                        LogUtil.LogWarning($"[DragDropDebug] Failed to load preset JSON from path: {normalizedPath}");
                        yield break;
                    }

                    LogUtil.Log($"[DragDropDebug] Found item preset storable: {storableId}. Applying preset now.");

                    JSONStorableString presetNameJSS = presetStorable.GetStringJSONParam("presetName");
                    if (presetNameJSS != null)
                    {
                        string fileNameNoExt = Path.GetFileNameWithoutExtension(normalizedPath);
                        if (normalizedPath.Contains(":"))
                        {
                            string presetPackageName = normalizedPath.Substring(0, normalizedPath.IndexOf(':'));
                            presetNameJSS.val = presetPackageName + ":" + fileNameNoExt + ".vap";
                        }
                        else
                        {
                            presetNameJSS.val = fileNameNoExt + ".vap";
                        }
                    }

                    LogUtil.Log($"[DragDropDebug] Loading preset into {storableId} via JSON (delayed)");

                    try
                    {
                        MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath);
                    }
                    catch { }

                    try
                    {
                        pm.LoadPresetFromJSON(presetJSON, false);
                    }
                    finally
                    {
                        try
                        {
                            MVR.FileManagement.FileManager.PopLoadDir();
                        }
                        catch { }
                    }
                    yield break;
                }

                yield return new WaitForEndOfFrame();
            }

            LogUtil.LogWarning($"[DragDropDebug] Timed out waiting for item preset storable for {lookupName} ({itemUid}). Preset not applied: {entry.Path}");
        }

        public static void ActivateClothingHairItemPreset(Atom atom, FileEntry entry, bool isClothing)
        {
            ClothingLoadingUtils.ActivateClothingHairItemPreset(atom, entry, isClothing);
        }

        private bool IsAtomMale(Atom atom)
        {
            if (atom == null) return false;
            JSONStorable geometry = atom.GetStorableByID("geometry");
            if (geometry != null)
            {
                JSONStorableStringChooser charChooser = geometry.GetStringChooserJSONParam("character");
                if (charChooser != null)
                {
                    string val = charChooser.val;
                    if (!string.IsNullOrEmpty(val) && val.StartsWith("Male", StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false; 
        }

        private enum ItemType { Clothing, Hair, Pose, Skin, Morphs, Appearance, Animation, BreastPhysics, Plugins, General, ClothingItem, HairItem, ClothingPreset, HairPreset, SubScene, Scene, CUA, Other }

        private ItemType GetItemType(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Path)) return ItemType.Other;
            string p = entry.Path.Replace('\\', '/');
            
            // Check for person preset categories (these use .vap or .json)
            if (p.IndexOf("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Appearance;
            if (p.IndexOf("Custom/Atom/Person/AnimationPresets", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Animation;
            if (p.IndexOf("Custom/Atom/Person/BreastPhysics", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.BreastPhysics;
            if (p.IndexOf("Custom/Atom/Person/Clothing", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Clothing;
            if (p.IndexOf("Custom/Atom/Person/Hair", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Hair;
            if (p.IndexOf("Custom/Atom/Person/Morphs", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Morphs;
            if (p.IndexOf("Custom/Atom/Person/Plugins", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Plugins;
            if (p.IndexOf("Custom/Atom/Person/Pose", StringComparison.OrdinalIgnoreCase) >= 0 || p.EndsWith(".vac", StringComparison.OrdinalIgnoreCase)) return ItemType.Pose;
            if (p.IndexOf("Custom/Atom/Person/Skin", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Skin;
            if (p.IndexOf("Custom/Atom/Person/General", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.General;
            
            // Check for clothing/hair items (these use .vam and toggle on/off)
            if (p.IndexOf("Custom/Clothing/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)) return ItemType.ClothingPreset;
                return ItemType.ClothingItem;
            }
            if (p.IndexOf("Custom/Hair/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)) return ItemType.HairPreset;
                return ItemType.HairItem;
            }
            
            // Check for subscenes
            if (p.IndexOf("Custom/SubScene", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.SubScene;

            // Scenes
            if (p.IndexOf("Saves/scene", StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf("/scene/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return ItemType.Scene;
            }
            if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ItemType.Scene;
            }

            // Pose fallback for loose .json pose presets (non-.vap) when path/name indicates pose
            if (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && p.IndexOf("pose", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ItemType.Pose;
            }

            // CUA
            if (p.IndexOf("Custom/Assets", StringComparison.OrdinalIgnoreCase) >= 0 || p.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
            {
                return ItemType.CUA;
            }
            
            return ItemType.Other;
        }

        private string GetStorableIdForItemType(ItemType itemType)
        {
            switch (itemType)
            {
                case ItemType.Appearance: return "AppearancePresets";
                case ItemType.Animation: return "AnimationPresets";
                case ItemType.BreastPhysics: return "FemaleBreastPhysicsPresets";
                case ItemType.Clothing: return "ClothingPresets";
                case ItemType.ClothingItem: return "ClothingPresets";
                case ItemType.General: return "Preset";
                case ItemType.Hair: return "HairPresets";
                case ItemType.HairItem: return "HairPresets";
                case ItemType.ClothingPreset: return null; // Targets specific clothing items
                case ItemType.HairPreset: return null; // Targets specific hair items
                case ItemType.Morphs: return "MorphPresets";
                case ItemType.Plugins: return "PluginPresets";
                case ItemType.Pose: return "PosePresets";
                case ItemType.Skin: return "SkinPresets";
                default: return null;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            _isDualPose = null;
            _dualPoseNode = null;
            dragCam = eventData.pressEventCamera;
            if (dragCam == null) dragCam = Camera.main;

            isDraggingItem = true;
            CreateGhost(eventData);

            string msg;
            float dist;
            Atom atom = DetectAtom(eventData, out msg, out dist);
            if (Panel != null) Panel.SetStatus(msg);
            
            UpdateGhost(eventData, atom, dist);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (isDraggingItem)
            {
                string msg;
                float dist;
                Atom atom = DetectAtom(eventData, out msg, out dist);
                
                UpdateGhost(eventData, atom, dist);
                if (Panel != null)
                {
                     Panel.SetStatus(msg);
                }
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (isDraggingItem)
            {
                DestroyGhost();
                DestroyGroundIndicator();
                isDraggingItem = false;
                
                if (Panel != null)
                {
                    Panel.SetStatus("");
                }

                if (HubItem != null)
                {
                    LogUtil.Log("Dropped Hub Item: " + HubItem.Title);
                    // Handle Hub Item drop (e.g. Download)
                    dragCam = null;
                    return;
                }

                ItemType itemType = GetItemType(FileEntry);
                
                // Handle subscenes differently - load directly without requiring atom
                if (itemType == ItemType.SubScene && FileEntry != null)
                {
                    if (Panel != null && Panel.DragDropReplaceMode)
                    {
                        List<Atom> toRemove = new List<Atom>();
                        foreach (var a in SuperController.singleton.GetAtoms())
                        {
                            if (a.type == "SubScene")
                            {
                                toRemove.Add(a);
                            }
                        }
                        
                        if (toRemove.Count > 0)
                        {
                            LogUtil.Log($"[VPB] Replace mode: Removing {toRemove.Count} existing SubScenes");
                            foreach (var a in toRemove)
                            {
                                SuperController.singleton.RemoveAtom(a);
                            }
                        }
                    }
                    
                    LoadSubScene(FileEntry.Uid);
                }
                else if (itemType == ItemType.Scene && FileEntry != null)
                {
                    string msg;
                    float dist;
                    Atom atom = DetectAtom(eventData, out msg, out dist);

                    // Calculate Drop Position for Context Menu
                    Vector3 dropPos = transform.position;
                    Camera cam = dragCam;
                    if (cam == null) cam = Camera.main;
                    if (cam != null)
                    {
                         Ray ray = cam.ScreenPointToRay(eventData.position);
                         if (atom != null)
                             dropPos = ray.GetPoint(dist);
                         else
                             dropPos = ray.GetPoint(planeDistance);
                    }
                    
                    if (IsAmbiguousDrop(atom, FileEntry))
                    {
                        HandleDropWithContext(atom, FileEntry, dropPos);
                    }
                    else
                    {
                        LoadSceneFile(FileEntry.Uid);
                    }
                }
                else if (itemType == ItemType.CUA && FileEntry != null)
                {
                    string msg;
                    Atom atom = DetectAtom(eventData, out msg);
                    if (atom != null && atom.type == "CustomUnityAsset")
                    {
                        LoadCUAIntoAtom(atom, FileEntry.Uid);
                    }
                    else
                    {
                        LoadCUA(FileEntry.Uid);
                    }
                }
                else
                {
                    string msg;
                    float dist;
                    Atom atom = DetectAtom(eventData, out msg, out dist);
                    if (atom != null && FileEntry != null)
                    {
                        // Calculate Drop Position
                        Vector3 dropPos = transform.position;
                        Camera cam = dragCam;
                        if (cam == null) cam = Camera.main;
                        if (cam != null)
                        {
                            Ray ray = cam.ScreenPointToRay(eventData.position);
                            dropPos = ray.GetPoint(dist);
                        }

                        // Special case: dropping an Appearance preset onto an existing Person atom should
                        // apply to that person (instead of spawning a new person).
                        if (itemType == ItemType.Appearance && atom.type == "Person")
                        {
                            ApplyClothingToAtom(atom, FileEntry.Uid, "replace");
                        }
                        else if (IsAmbiguousDrop(atom, FileEntry))
                        {
                            HandleDropWithContext(atom, FileEntry, dropPos);
                        }
                        else
                        {
                            ApplyClothingToAtom(atom, FileEntry.Uid);
                        }
                    }
                }
                dragCam = null;
            }
        }

        public void OnDisable()
        {
            if (isDraggingItem)
            {
                DestroyGhost();
                DestroyGroundIndicator();
                isDraggingItem = false;
                if (Panel != null) Panel.SetStatus("");
                dragCam = null;
            }
        }

        public void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && isDraggingItem)
            {
                DestroyGhost();
                DestroyGroundIndicator();
                isDraggingItem = false;
                if (Panel != null) Panel.SetStatus("");
                dragCam = null;
            }
        }

        private Atom DetectAtom(PointerEventData eventData, out string statusMsg, out float distance)
        {
            Camera cam = dragCam;
            if (cam == null) cam = eventData.pressEventCamera;
            if (cam == null) cam = Camera.main;

            string hitMsg;
            RaycastHit hit;
            Atom atom = SceneUtils.RaycastAtom(eventData.position, cam, out hitMsg, out hit);
            
            statusMsg = hitMsg;
            distance = (hit.collider != null) ? hit.distance : planeDistance;

            if (HubItem != null)
            {
                statusMsg = $"Drop to download/view {HubItem.Title}";
                return atom;
            }

            ItemType itemType = GetItemType(FileEntry);
            
            if (itemType == ItemType.SubScene)
            {
                statusMsg = $"Drop to load SubScene: {FileEntry.Name}";
            }
            else if (itemType == ItemType.Scene)
            {
                statusMsg = $"Release to launch scene {FileEntry.Name}";
            }
            else if (itemType == ItemType.CUA)
            {
                 if (atom != null && atom.type == "CustomUnityAsset")
                 {
                     statusMsg = $"Drop to load into {atom.name}";
                 }
                 else
                 {
                     statusMsg = $"Drop to create new Custom Unity Asset";
                 }
            }
            else if (atom != null && atom.type == "Person")
            {
                 string action = (Panel != null && Panel.DragDropReplaceMode) ? "Replacing" : "Adding";
                 if (itemType == ItemType.ClothingPreset || itemType == ItemType.HairPreset)
                 {
                     statusMsg = $"{action} Preset {FileEntry.Name} to {atom.name}";
                 }
                 else
                 {
                     statusMsg = $"{action} {FileEntry.Name} to {atom.name}";
                 }
            }
            return atom;
        }

        private Atom DetectAtom(PointerEventData eventData, out string statusMsg)
        {
            float dummy;
            return DetectAtom(eventData, out statusMsg, out dummy);
        }

        public void LoadCUA(string path)
        {
            string normalizedPath = UI.NormalizePath(path);
            LogUtil.Log($"[DragDropDebug] Loading CUA: {normalizedPath}");
            if (Panel != null) Panel.StartCoroutine(LoadCUACoroutine(normalizedPath));
            else StartCoroutine(LoadCUACoroutine(normalizedPath));
        }

        private System.Collections.IEnumerator LoadCUACoroutine(string path)
        {
            yield return SuperController.singleton.AddAtomByType("CustomUnityAsset", Path.GetFileNameWithoutExtension(path), true, true, true);
            
            Atom newAtom = SuperController.singleton.GetSelectedAtom();
            if (newAtom != null && newAtom.type == "CustomUnityAsset")
            {
                LoadCUAIntoAtom(newAtom, path);
            }
        }

        public void LoadCUAIntoAtom(Atom atom, string path)
        {
            if (Panel != null) Panel.StartCoroutine(LoadCUAIntoAtomCoroutine(atom, path));
            else StartCoroutine(LoadCUAIntoAtomCoroutine(atom, path));
        }

        private System.Collections.IEnumerator LoadCUAIntoAtomCoroutine(Atom atom, string path)
        {
            string atomUid = atom.uid;
            bool installed = EnsureInstalled();
            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
                yield return new WaitForSeconds(1.0f);
            }

            // Refresh atom reference
            Atom targetAtom = SuperController.singleton.GetAtomByUid(atomUid);
            if (targetAtom == null)
            {
                 LogUtil.LogError("[DragDropDebug] Atom " + atomUid + " not found after refresh");
                 yield break;
            }

            string normalizedPath = UI.NormalizePath(path);
            JSONStorableUrl urlParam = targetAtom.GetUrlJSONParam("assetUrl");
            if (urlParam == null)
            {
                // Try getting from "asset" storable explicitly
                JSONStorable assetStorable = targetAtom.GetStorableByID("asset");
                if (assetStorable != null)
                {
                    urlParam = assetStorable.GetUrlJSONParam("assetUrl");
                }
            }

            if (urlParam != null)
            {
                LogUtil.Log("[DragDropDebug] Setting assetUrl to " + normalizedPath);
                urlParam.val = normalizedPath;
                
                // Automatically set assetName if possible
                bool done = false;
                List<string> assetNames = null;
                yield return CustomAssetLoader.GetAssetBundleContent(path, (names) => {
                     assetNames = names;
                     done = true;
                });
                
                while (!done) yield return null;
                
                if (assetNames != null && assetNames.Count > 0)
                {
                     LogUtil.Log($"[DragDropDebug] Found {assetNames.Count} assets in bundle.");
                     JSONStorableString nameParam = targetAtom.GetStringJSONParam("assetName");
                     if (nameParam == null)
                     {
                          JSONStorable assetStorable = targetAtom.GetStorableByID("asset");
                          if (assetStorable != null) nameParam = assetStorable.GetStringJSONParam("assetName");
                     }
                     
                     if (nameParam != null)
                     {
                          // Sort assets alphabetically to match VaM UI
                          assetNames.Sort();
                          
                          // Default to the first asset (Position 1)
                          string match = assetNames[0];
                          
                          LogUtil.Log($"[DragDropDebug] Auto-setting assetName to: {match}");
                          nameParam.val = match;
                     }
                }
            }
            else
            {
                LogUtil.LogError("[DragDropDebug] assetUrl param not found on " + targetAtom.name);
                foreach (string sid in targetAtom.GetStorableIDs())
                {
                    LogUtil.Log("[DragDropDebug] Storable: " + sid);
                    JSONStorable storable = targetAtom.GetStorableByID(sid);
                    if (storable != null)
                    {
                        List<string> urlParams = storable.GetUrlParamNames();
                        if (urlParams != null)
                            foreach (string pid in urlParams) LogUtil.Log("  UrlParam: " + pid);
                            
                        List<string> stringParams = storable.GetStringParamNames();
                        if (stringParams != null)
                            foreach (string pid in stringParams) LogUtil.Log("  StringParam: " + pid);
                    }
                }
            }
        }

        public void LoadSubScene(string path)
        {
            bool installed = EnsureInstalled();

            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            string normalizedPath = UI.NormalizePath(path);

            LogUtil.Log($"[VPB] LoadSubScene: {normalizedPath}");
            
            // Handle Replace mode for clicks too
            if (Panel != null && Panel.DragDropReplaceMode)
            {
                List<Atom> toRemove = new List<Atom>();
                foreach (var a in SuperController.singleton.GetAtoms())
                {
                    if (a.type == "SubScene")
                    {
                        toRemove.Add(a);
                    }
                }
                
                if (toRemove.Count > 0)
                {
                    LogUtil.Log($"[VPB] Replace mode (click): Removing {toRemove.Count} existing SubScenes");
                    foreach (var a in toRemove)
                    {
                        SuperController.singleton.RemoveAtom(a);
                    }
                }
            }

            try
            {
                if (Panel != null) Panel.StartCoroutine(LoadSubSceneCoroutine(normalizedPath));
                else StartCoroutine(LoadSubSceneCoroutine(normalizedPath));
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] Failed to load subscene: {ex.Message}");
            }
        }

        public void LoadSceneFile(string path)
        {
            try
            {
                FileEntry entry = FileEntry;
                if (!string.IsNullOrEmpty(path))
                {
                    if (entry == null
                        || (!string.Equals(entry.Uid, path, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        entry = VPB.FileManager.GetFileEntry(path);
                    }
                }

                if (entry != null)
                {
                    UI.LoadSceneFile(entry);
                }
                else if (!string.IsNullOrEmpty(path) && SuperController.singleton != null)
                {
                    string normalized = UI.NormalizePath(path);
                    SuperController.singleton.Load(normalized);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] LoadSceneFile error: {ex.Message}");
            }
        }

        public void LoadClothing(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadClothing: No target atom provided.");
                return;
            }
            LogUtil.Log($"[VPB] LoadClothing: Applying {FileEntry.Name} to {target.uid}");
            ApplyClothingToAtom(target, FileEntry.Uid);
        }

        public void LoadHair(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadHair: No target atom provided.");
                return;
            }
            LogUtil.Log($"[VPB] LoadHair: Applying {FileEntry.Name} to {target.uid}");
            ApplyClothingToAtom(target, FileEntry.Uid);
        }

        public void LoadSkin(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadSkin: No target atom provided.");
                return;
            }
            LogUtil.Log($"[VPB] LoadSkin: Applying {FileEntry.Name} to {target.uid}");
            ApplyClothingToAtom(target, FileEntry.Uid);
        }

        public void LoadMorphs(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadMorphs: No target atom provided.");
                return;
            }
            LogUtil.Log($"[VPB] LoadMorphs: Applying {FileEntry.Name} to {target.uid}");
            ApplyClothingToAtom(target, FileEntry.Uid);
        }

        public void LoadAppearance(Atom target, string mode = null)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadAppearance: No target atom provided.");
                return;
            }
            LogUtil.Log($"[VPB] LoadAppearance: Applying {FileEntry.Name} to {target.uid} (Mode: {mode ?? "default"})");
            ApplyClothingToAtom(target, FileEntry.Uid, mode);
        }

        public void LoadPose(Atom target, bool suppressRoot = true)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadPose: No target atom provided.");
                return;
            }
            
            string normalizedPath = UI.NormalizePath(FileEntry.Path);
            LogUtil.Log($"[VPB] LoadPose: Applying {FileEntry.Name} to {target.uid} (SuppressRoot: {suppressRoot})");

            JSONNode node = SuperController.singleton.LoadJSON(normalizedPath);
            if (node == null) return;
            JSONClass presetJSON = node.AsObject;
            
            if (FileButton.EnsureInstalledByText(presetJSON.ToString()))
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }
            
            // Detect if this is a scene file and extract the first Person atom's pose
            if (presetJSON["atoms"] != null)
            {
                JSONClass extracted = ExtractAtomFromScene(presetJSON, "Person");
                if (extracted != null)
                {
                    presetJSON = extracted;
                }
                else
                {
                    LogUtil.LogWarning("[VPB] LoadPose: Scene file does not contain a Person atom.");
                    return;
                }
            }
            
            if (suppressRoot)
            {
                if (presetJSON["storables"] != null)
                {
                    JSONArray storables = presetJSON["storables"] as JSONArray;
                    if (storables != null)
                    {
                        for (int i = 0; i < storables.Count; i++)
                        {
                            JSONClass s = storables[i] as JSONClass;
                            if (s == null) continue;

                            if (s["id"].Value == "control")
                            {
                                // Clean top-level control storable if it exists
                                if (s.HasKey("position")) s.Remove("position");
                                if (s.HasKey("rotation")) s.Remove("rotation");
                            }

                            if (s["id"].Value == "PosePresets" || s["id"].Value == "control")
                            {
                                if (s["presets"] != null) CleanPresets(s["presets"] as JSONArray);
                            }
                        }
                    }
                }
                else if (presetJSON["presets"] != null)
                {
                    CleanPresets(presetJSON["presets"] as JSONArray);
                }
            }
            
            JSONStorable presetStorable = target.GetStorableByID("PosePresets");
            if (presetStorable != null)
            {
                 var pm = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                 if (pm != null)
                 {
                    try
                    {
                        MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath);
                        pm.LoadPresetFromJSON(presetJSON, false);
                    }
                    finally
                    {
                        MVR.FileManagement.FileManager.PopLoadDir();
                    }
                 }
            }
        }
        
        private void CleanPresets(JSONArray presets)
        {
            if (presets == null) return;
            for (int j = 0; j < presets.Count; j++)
            {
                JSONClass p = presets[j] as JSONClass;
                if (p != null && p["id"].Value == "control")
                {
                    if (p.HasKey("position")) p.Remove("position");
                    if (p.HasKey("rotation")) p.Remove("rotation");
                }
            }
        }

        public void MirrorPose(Atom target)
        {
            if (target == null) return;
            JSONStorable storable = target.GetStorableByID("PosePresets");
            if (storable == null) return;
            var pm = storable.GetComponentInChildren<MeshVR.PresetManager>();
            if (pm != null)
            {
                var method = pm.GetType().GetMethod("Mirror", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (method != null) method.Invoke(pm, null);
                else LogUtil.LogWarning("[VPB] Mirror method not found on PresetManager");
            }
        }

        public void RemoveAllClothing(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] RemoveAllClothing: target is null");
                return;
            }

            LogUtil.Log($"[VPB] RemoveAllClothing: target={target.uid} ({target.type})");

            PushUndoSnapshotForClothingHair(target);

            ClothingLoadingUtils.RemoveAllClothing(target);
        }

        public void RemoveClothingBySlot(Atom target, string slot)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] RemoveClothingBySlot: target is null");
                return;
            }
            if (string.IsNullOrEmpty(slot))
            {
                LogUtil.LogWarning("[VPB] RemoveClothingBySlot: slot is empty");
                return;
            }

            string slotLower = slot.Trim().ToLowerInvariant();
            LogUtil.Log($"[VPB] RemoveClothingBySlot: target={target.uid} ({target.type}) slot={slotLower}");

            PushUndoSnapshotForClothingHair(target);

            JSONStorable geometry = null;
            try { geometry = target.GetStorableByID("geometry"); }
            catch { }

            DAZCharacterSelector dcs = null;
            try { dcs = target.GetComponentInChildren<DAZCharacterSelector>(); }
            catch { }
            if (dcs == null)
            {
                LogUtil.LogWarning("[VPB] RemoveClothingBySlot: DAZCharacterSelector not found on target");
                return;
            }

            MethodInfo miSetActiveItem = null;
            MethodInfo miSetActiveItemByUid = null;
            try
            {
                foreach (var m in dcs.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.Name != "SetActiveClothingItem") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 2)
                    {
                        if (ps[0].ParameterType == typeof(DAZClothingItem)) miSetActiveItem = m;
                        else if (ps[0].ParameterType == typeof(string)) miSetActiveItemByUid = m;
                    }
                }
            }
            catch { }

            string ResolveClothingItemPath(DAZClothingItem item)
            {
                if (item == null) return null;

                string path = null;
                try { path = item.uid; } catch { }

                if (string.IsNullOrEmpty(path) || (!path.Contains(":/") && !path.Contains(":\\")))
                {
                    try
                    {
                        string internalId = null;
                        string containingVAMDir = null;
                        Type it = item.GetType();

                        FieldInfo fInternalId = it.GetField("internalId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fInternalId != null) internalId = fInternalId.GetValue(item) as string;

                        FieldInfo fVamDir = it.GetField("containingVAMDir", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fVamDir != null) containingVAMDir = fVamDir.GetValue(item) as string;

                        if (string.IsNullOrEmpty(internalId))
                        {
                            FieldInfo fItemPath = it.GetField("itemPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fItemPath != null) internalId = fItemPath.GetValue(item) as string;
                        }

                        if (!string.IsNullOrEmpty(containingVAMDir) && !string.IsNullOrEmpty(internalId))
                        {
                            path = containingVAMDir.Replace("\\", "/").TrimEnd('/') + "/" + internalId.Replace("\\", "/").TrimStart('/');
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(path)) return null;
                return path.Replace("\\", "/");
            }

            string ExtractClothingTypeFromPath(string path)
            {
                if (string.IsNullOrEmpty(path)) return null;
                string pl = path.ToLowerInvariant();
                int idx = pl.IndexOf("/custom/clothing/");
                if (idx < 0) idx = pl.IndexOf("/clothing/");
                if (idx < 0) return null;

                string sub = path.Substring(idx);
                string[] parts = sub.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts == null || parts.Length < 4) return null;
                string typeFolder = parts[3];
                if (string.IsNullOrEmpty(typeFolder)) return null;
                return typeFolder.Trim().ToLowerInvariant();
            }

            int removedCount = 0;
            try
            {
                if (dcs.clothingItems != null)
                {
                    foreach (var item in dcs.clothingItems)
                    {
                        if (item == null) continue;
                        if (!item.active) continue;

                        bool match = false;
                        try
                        {
                            string p = ResolveClothingItemPath(item);
                            string t = ExtractClothingTypeFromPath(p);
                            if (!string.IsNullOrEmpty(t) && string.Equals(t, slotLower, StringComparison.OrdinalIgnoreCase)) match = true;
                        }
                        catch { }

                        if (!match)
                        {
                            HashSet<string> tags = GetTagSetForClothingItem(item);
                            match = tags != null && tags.Contains(slotLower);

                            if (!match && tags == null)
                            {
                                string n = null;
                                try { n = item.name; } catch { }
                                if (!string.IsNullOrEmpty(n) && n.IndexOf(slotLower, StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                            }
                        }

                        if (!match) continue;

                        try
                        {
                            if (geometry != null)
                            {
                                JSONStorableBool active = geometry.GetBoolJSONParam("clothing:" + item.uid);
                                if (active != null) active.val = false;
                            }
                        }
                        catch { }

                        try
                        {
                            if (miSetActiveItem != null)
                            {
                                miSetActiveItem.Invoke(dcs, new object[] { item, false });
                            }
                            else if (miSetActiveItemByUid != null)
                            {
                                miSetActiveItemByUid.Invoke(dcs, new object[] { item.uid, false });
                            }
                            else
                            {
                                item.active = false;
                            }
                        }
                        catch
                        {
                            try { item.active = false; } catch { }
                        }

                        removedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] RemoveClothingBySlot exception: " + ex);
            }

            LogUtil.Log($"[VPB] RemoveClothingBySlot: removed/disabled {removedCount} items for slot={slotLower}");
        }

        public void RemoveClothingItemByUid(Atom target, string itemUid)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] RemoveClothingItemByUid: target is null");
                return;
            }
            if (string.IsNullOrEmpty(itemUid))
            {
                LogUtil.LogWarning("[VPB] RemoveClothingItemByUid: itemUid is empty");
                return;
            }

            PushUndoSnapshotForClothingHair(target);

            JSONStorable geometry = null;
            try { geometry = target.GetStorableByID("geometry"); }
            catch { }

            DAZCharacterSelector dcs = null;
            try { dcs = target.GetComponentInChildren<DAZCharacterSelector>(); }
            catch { }
            if (dcs == null)
            {
                LogUtil.LogWarning("[VPB] RemoveClothingItemByUid: DAZCharacterSelector not found on target");
                return;
            }

            MethodInfo miSetActiveItem = null;
            MethodInfo miSetActiveItemByUid = null;
            try
            {
                foreach (var m in dcs.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.Name != "SetActiveClothingItem") continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 2 && ps[1].ParameterType == typeof(bool))
                    {
                        if (ps[0].ParameterType == typeof(DAZClothingItem)) miSetActiveItem = m;
                        else if (ps[0].ParameterType == typeof(string)) miSetActiveItemByUid = m;
                    }
                }
            }
            catch { }

            DAZClothingItem matched = null;
            try
            {
                if (dcs.clothingItems != null)
                {
                    foreach (var it in dcs.clothingItems)
                    {
                        if (it == null) continue;
                        if (string.Equals(it.uid, itemUid, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = it;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (matched == null)
            {
                LogUtil.LogWarning("[VPB] RemoveClothingItemByUid: clothing item not found: " + itemUid);
                return;
            }

            

            bool geometryBoolWasTrue = false;
            bool geometryBoolFound = false;
            bool itemWasActive = false;
            try { itemWasActive = matched.active; } catch { itemWasActive = false; }

            // Prefer ref-style removal: flip the geometry clothing:<uid> bool.
            // This is the canonical wear/remove signal in VaM and triggers callbacks.
            JSONStorableBool itemJsb = null;
            try
            {
                if (geometry != null)
                {
                    try { itemJsb = geometry.GetBoolJSONParam("clothing:" + itemUid); } catch { }
                }
            }
            catch { }

            string NormalizeClothingUid(string uid)
            {
                if (string.IsNullOrEmpty(uid)) return null;
                string u = uid.Replace("\\", "/");
                // Strip VAR prefix like "Author.Package.1:" if present
                int colon = u.IndexOf(":/");
                if (colon >= 0) u = u.Substring(colon + 2);
                // Remove leading slashes
                while (u.StartsWith("/")) u = u.Substring(1);
                return u;
            }

            string wantedNorm = NormalizeClothingUid(itemUid);

            try
            {
                if (geometry == null)
                {
                    LogUtil.LogWarning("[VPB] RemoveClothingItemByUid: geometry storable not found");
                }
                else if (itemJsb != null)
                {
                    geometryBoolFound = true;
                    geometryBoolWasTrue = itemJsb.val;
                    bool before = itemJsb.val;
                    itemJsb.val = false;
                }
                else
                {
                    LogUtil.LogWarning($"[VPB] RemoveClothingItemByUid: geometry bool not found for clothing:{itemUid}");
                }
            }
            catch { }

            // If the exact uid bool wasn't active, try to find the active clothing bool by normalized uid suffix.
            if (geometry != null && (!geometryBoolFound || !geometryBoolWasTrue) && !string.IsNullOrEmpty(wantedNorm))
            {
                try
                {
                    int matches = 0;
                    string bestKey = null;
                    JSONStorableBool bestJsb = null;

                    foreach (var n in geometry.GetBoolParamNames())
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        if (!n.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                        JSONStorableBool jsb = null;
                        try { jsb = geometry.GetBoolJSONParam(n); } catch { }
                        if (jsb == null || !jsb.val) continue;

                        string uid = null;
                        try { uid = n.Substring(9); } catch { }
                        if (string.IsNullOrEmpty(uid)) continue;

                        string candNorm = NormalizeClothingUid(uid);
                        if (string.IsNullOrEmpty(candNorm)) continue;

                        // match if exact normalized match or suffix match (handles different root prefixes)
                        if (string.Equals(candNorm, wantedNorm, StringComparison.OrdinalIgnoreCase) ||
                            candNorm.EndsWith(wantedNorm, StringComparison.OrdinalIgnoreCase) ||
                            wantedNorm.EndsWith(candNorm, StringComparison.OrdinalIgnoreCase))
                        {
                            matches++;
                            // Prefer the longest normalized uid as the most specific
                            if (bestKey == null || candNorm.Length > NormalizeClothingUid(bestKey).Length)
                            {
                                bestKey = uid;
                                bestJsb = jsb;
                            }
                        }
                    }

                    if (matches > 0 && bestJsb != null && bestKey != null)
                    {
                        bool before = bestJsb.val;
                        // toggle true->false to ensure callbacks fire
                        bestJsb.val = true;
                        bestJsb.val = false;
                        geometryBoolFound = true;
                        geometryBoolWasTrue = true;
                        LogUtil.Log($"[VPB] RemoveClothingItemByUid: normalized match removed clothing:{bestKey} true -> false (matches={matches})");
                    }
                    else
                    {
                        LogUtil.Log($"[VPB] RemoveClothingItemByUid: normalized match found 0 active clothing bools for '{wantedNorm}'");
                    }
                }
                catch { }
            }

            try
            {
                if (miSetActiveItem != null)
                {
                    miSetActiveItem.Invoke(dcs, new object[] { matched, false });
                }
                else if (miSetActiveItemByUid != null)
                {
                    miSetActiveItemByUid.Invoke(dcs, new object[] { matched.uid, false });
                }
                else
                {
                    matched.active = false;
                }
            }
            catch
            {
                try { matched.active = false; } catch { }
            }

            // If we couldn't target the exact jsb, try to find active clothing JSBs by filename match.
            if (geometry != null && (!geometryBoolFound || !geometryBoolWasTrue))
            {
                try
                {
                    string wanted = null;
                    try
                    {
                        string p = itemUid.Replace("\\", "/");
                        int slash = p.LastIndexOf('/');
                        string last = slash >= 0 ? p.Substring(slash + 1) : p;
                        int dot = last.LastIndexOf('.');
                        wanted = dot > 0 ? last.Substring(0, dot) : last;
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(wanted))
                    {
                        int hits = 0;
                        foreach (var n in geometry.GetBoolParamNames())
                        {
                            if (string.IsNullOrEmpty(n)) continue;
                            if (!n.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                            JSONStorableBool jsb = null;
                            try { jsb = geometry.GetBoolJSONParam(n); } catch { }
                            if (jsb == null || !jsb.val) continue;

                            string uid = null;
                            try { uid = n.Substring(9); } catch { }
                            if (string.IsNullOrEmpty(uid)) continue;

                            string candidate = null;
                            try
                            {
                                string p = uid.Replace("\\", "/");
                                int slash = p.LastIndexOf('/');
                                string last = slash >= 0 ? p.Substring(slash + 1) : p;
                                int dot = last.LastIndexOf('.');
                                candidate = dot > 0 ? last.Substring(0, dot) : last;
                            }
                            catch { }

                            if (string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase))
                            {
                                bool before = jsb.val;
                                jsb.val = false;
                                hits++;
                                LogUtil.Log($"[VPB] RemoveClothingItemByUid: filename-match removed clothing:{uid} {before} -> {jsb.val}");
                            }
                        }

                        if (hits > 0)
                        {
                            geometryBoolFound = true;
                            geometryBoolWasTrue = true;
                            LogUtil.Log($"[VPB] RemoveClothingItemByUid: filename-match removed {hits} items for '{wanted}'");
                        }
                    }
                }
                catch { }
            }

            // If the item was already inactive/hidden, try a stronger approach to actually unload/remove.
            // Some VaM versions keep inactive clothing items in the list; we attempt to force a refresh and/or invoke remove-style APIs via reflection.
            if (!itemWasActive && geometryBoolFound && !geometryBoolWasTrue)
            {
                try
                {
                    if (miSetActiveItem != null)
                    {
                        LogUtil.Log("[VPB] RemoveClothingItemByUid: item already inactive; attempting force refresh via SetActiveClothingItem(true->false)");
                        miSetActiveItem.Invoke(dcs, new object[] { matched, true });
                        miSetActiveItem.Invoke(dcs, new object[] { matched, false });
                    }
                    else if (miSetActiveItemByUid != null)
                    {
                        LogUtil.Log("[VPB] RemoveClothingItemByUid: item already inactive; attempting force refresh via SetActiveClothingItem(uid, true->false)");
                        miSetActiveItemByUid.Invoke(dcs, new object[] { matched.uid, true });
                        miSetActiveItemByUid.Invoke(dcs, new object[] { matched.uid, false });
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB] RemoveClothingItemByUid: force refresh exception: " + ex.Message);
                }

                // Try calling remove/unload methods if present.
                try
                {
                    bool invoked = false;

                    try
                    {
                        JSONStorable clothing = null;
                        try { clothing = target.GetStorableByID("Clothing"); } catch { }
                        if (clothing != null)
                        {
                            foreach (var m in clothing.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            {
                                if (m == null) continue;
                                if (m.Name == null) continue;
                                if (m.Name.IndexOf("remove", StringComparison.OrdinalIgnoreCase) < 0) continue;

                                var ps = m.GetParameters();
                                if (ps == null) continue;

                                if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                                {
                                    LogUtil.Log($"[VPB] RemoveClothingItemByUid: invoking Clothing.{m.Name}(string)");
                                    m.Invoke(clothing, new object[] { matched.uid });
                                    invoked = true;
                                }
                                else if (ps.Length == 1 && ps[0].ParameterType == typeof(DAZClothingItem))
                                {
                                    LogUtil.Log($"[VPB] RemoveClothingItemByUid: invoking Clothing.{m.Name}(DAZClothingItem)");
                                    m.Invoke(clothing, new object[] { matched });
                                    invoked = true;
                                }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        foreach (var m in dcs.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            if (m == null) continue;
                            if (m.Name == null) continue;
                            if (m.Name.IndexOf("remove", StringComparison.OrdinalIgnoreCase) < 0 && m.Name.IndexOf("unload", StringComparison.OrdinalIgnoreCase) < 0) continue;

                            var ps = m.GetParameters();
                            if (ps == null) continue;

                            if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                            {
                                LogUtil.Log($"[VPB] RemoveClothingItemByUid: invoking DAZCharacterSelector.{m.Name}(string)");
                                m.Invoke(dcs, new object[] { matched.uid });
                                invoked = true;
                            }
                            else if (ps.Length == 1 && ps[0].ParameterType == typeof(DAZClothingItem))
                            {
                                LogUtil.Log($"[VPB] RemoveClothingItemByUid: invoking DAZCharacterSelector.{m.Name}(DAZClothingItem)");
                                m.Invoke(dcs, new object[] { matched });
                                invoked = true;
                            }
                        }
                    }
                    catch { }

                    if (!invoked)
                    {
                        LogUtil.Log("[VPB] RemoveClothingItemByUid: no remove/unload methods found to invoke");
                    }
                }
                catch { }
            }

            // Ref implementation refreshes dynamic items after clothing/hair toggles.
            

            
        }

        public void RemoveAllHair(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] RemoveAllHair: target is null");
                return;
            }

            LogUtil.Log($"[VPB] RemoveAllHair: target={target.uid} ({target.type})");

            PushUndoSnapshotForClothingHair(target);

            bool cleared = false;
            try
            {
                JSONStorable hair = target.GetStorableByID("Hair");
                LogUtil.Log($"[VPB] RemoveAllHair: Hair storable {(hair != null ? "found" : "NOT found")}");
                if (hair != null)
                {
                    var method = hair.GetType().GetMethod("Clear", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    LogUtil.Log($"[VPB] RemoveAllHair: Clear() method {(method != null ? "found" : "NOT found")} on {hair.GetType().FullName}");
                    if (method != null)
                    {
                        method.Invoke(hair, null);
                        cleared = true;
                        LogUtil.Log("[VPB] RemoveAllHair: Clear() invoked");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] RemoveAllHair: Clear() exception: " + ex);
            }

            if (!cleared)
            {
                LogUtil.LogWarning("[VPB] RemoveAllHair: falling back to geometry bool disable");
                try
                {
                    JSONStorable geometry = target.GetStorableByID("geometry");
                    if (geometry == null)
                    {
                        LogUtil.LogWarning("[VPB] RemoveAllHair: geometry storable NOT found");
                        return;
                    }

                    DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                    if (dcs == null)
                    {
                        LogUtil.LogWarning("[VPB] RemoveAllHair: DAZCharacterSelector not found on target");
                        return;
                    }

                    int disabledCount = 0;
                    if (dcs.hairItems != null)
                    {
                        foreach (var item in dcs.hairItems)
                        {
                            if (item == null) continue;
                            JSONStorableBool active = geometry.GetBoolJSONParam("hair:" + item.uid);
                            if (active != null)
                            {
                                if (active.val)
                                {
                                    active.val = false;
                                    disabledCount++;
                                }
                            }
                        }
                    }

                    LogUtil.Log($"[VPB] RemoveAllHair: geometry fallback disabled {disabledCount} hair items");
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] RemoveAllHair: geometry fallback exception: " + ex);
                }
            }
        }

        public void RemoveHairItemByUid(Atom target, string itemUid)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] RemoveHairItemByUid: target is null");
                return;
            }
            if (string.IsNullOrEmpty(itemUid))
            {
                LogUtil.LogWarning("[VPB] RemoveHairItemByUid: itemUid is empty");
                return;
            }

            PushUndoSnapshotForClothingHair(target);

            JSONStorable geometry = null;
            try { geometry = target.GetStorableByID("geometry"); }
            catch { }

            DAZCharacterSelector dcs = null;
            try { dcs = target.GetComponentInChildren<DAZCharacterSelector>(); }
            catch { }
            if (dcs == null)
            {
                LogUtil.LogWarning("[VPB] RemoveHairItemByUid: DAZCharacterSelector not found on target");
                return;
            }

            MethodInfo miSetActiveItem = null;
            MethodInfo miSetActiveItemByUid = null;
            try
            {
                foreach (var m in dcs.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.Name != "SetActiveHairItem") continue;
                    var ps = m.GetParameters();
                    if (ps.Length >= 2)
                    {
                        if (ps[0].ParameterType == typeof(string))
                        {
                            miSetActiveItemByUid = m;
                        }
                        else
                        {
                            // Don't take a hard dependency on DAZHairItem type (it may not exist in some builds)
                            miSetActiveItem = m;
                        }
                    }
                }
            }
            catch { }

            object matched = null;
            try
            {
                if (dcs.hairItems != null)
                {
                    foreach (var it in dcs.hairItems)
                    {
                        if (it == null) continue;

                        string uid = null;
                        try
                        {
                            var pUid = it.GetType().GetProperty("uid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (pUid != null) uid = pUid.GetValue(it, null) as string;
                            if (string.IsNullOrEmpty(uid))
                            {
                                var fUid = it.GetType().GetField("uid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (fUid != null) uid = fUid.GetValue(it) as string;
                            }
                        }
                        catch { }

                        if (string.Equals(uid, itemUid, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = it;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (matched == null)
            {
                LogUtil.LogWarning("[VPB] RemoveHairItemByUid: hair item not found: " + itemUid);
                return;
            }

            try
            {
                if (geometry != null)
                {
                    string uid = null;
                    try
                    {
                        var pUid = matched.GetType().GetProperty("uid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pUid != null) uid = pUid.GetValue(matched, null) as string;
                        if (string.IsNullOrEmpty(uid))
                        {
                            var fUid = matched.GetType().GetField("uid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fUid != null) uid = fUid.GetValue(matched) as string;
                        }
                    }
                    catch { }

                    if (string.IsNullOrEmpty(uid)) uid = itemUid;

                    JSONStorableBool active = geometry.GetBoolJSONParam("hair:" + uid);
                    if (active != null) active.val = false;
                }
            }
            catch { }

            try
            {
                if (miSetActiveItem != null)
                {
                    miSetActiveItem.Invoke(dcs, new object[] { matched, false });
                }
                else if (miSetActiveItemByUid != null)
                {
                    miSetActiveItemByUid.Invoke(dcs, new object[] { itemUid, false });
                }
                else
                {
                    try
                    {
                        var pActive = matched.GetType().GetProperty("active", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pActive != null && pActive.CanWrite)
                        {
                            pActive.SetValue(matched, false, null);
                        }
                        else
                        {
                            var fActive = matched.GetType().GetField("active", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (fActive != null) fActive.SetValue(matched, false);
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                try
                {
                    var pActive = matched.GetType().GetProperty("active", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pActive != null && pActive.CanWrite) pActive.SetValue(matched, false, null);
                }
                catch { }
            }
        }

        public void PlayAudioPreview(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string normalizedPath = UI.NormalizePath(path);
            
            Atom audioAtom = null;
            foreach (Atom a in SuperController.singleton.GetAtoms())
            {
                if (a.type == "InvisibleAudioSource" || a.type == "AudioSource")
                {
                    audioAtom = a;
                    break;
                }
            }
            
            if (audioAtom == null)
            {
                Atom selected = SuperController.singleton.GetSelectedAtom();
                if (selected != null && selected.GetStorableByID("AudioSource") != null)
                {
                    audioAtom = selected;
                }
            }
            
            if (audioAtom != null)
            {
                JSONStorable urlStorable = audioAtom.GetStorableByID("AudioSource");
                if (urlStorable != null)
                {
                    JSONStorableUrl urlParam = urlStorable.GetUrlJSONParam("url");
                    if (urlParam != null)
                    {
                        urlParam.val = normalizedPath;
                        var playAction = urlStorable.GetAction("Play");
                        if (playAction != null) playAction.actionCallback();
                        return;
                    }
                }
            }
            
            LogUtil.LogWarning("[VPB] No suitable AudioSource atom found to play preview. Please add an InvisibleAudioSource to the scene.");
        }

        public void StopAudioPreview()
        {
             foreach (Atom a in SuperController.singleton.GetAtoms())
             {
                 JSONStorable urlStorable = a.GetStorableByID("AudioSource");
                 if (urlStorable != null)
                 {
                     var stopAction = urlStorable.GetAction("Stop");
                     if (stopAction != null) stopAction.actionCallback();
                 }
             }
        }

        public void MergeSceneFile(string path, bool atPlayer = false)
        {
            try
            {
                LogUtil.Log($"[VPB] MergeSceneFile started: {path} (atPlayer: {atPlayer})");
                FileEntry entryForPath = null;
                try { entryForPath = VPB.FileManager.GetFileEntry(path); } catch { }
                if (entryForPath == null) entryForPath = FileEntry;

                bool installed = false;
                try { installed = UI.EnsureInstalled(entryForPath); } catch { installed = false; }

                if (installed)
                {
                    LogUtil.Log("[VPB] Refreshing FileManagers...");
                    MVR.FileManagement.FileManager.Refresh();
                    FileManager.Refresh();
                }

                string normalizedPath = UI.NormalizePath(path);
                try
                {
                    if (SceneLoadingUtils.TryPrepareLocalSceneForLoad(entryForPath, out string rewritten))
                    {
                        normalizedPath = UI.NormalizePath(rewritten);
                        LogUtil.Log($"[VPB] Using rewritten scene: {normalizedPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning($"[VPB] Scene rewrite skipped due to error: {ex.Message}");
                }
                LogUtil.Log($"[VPB] Normalized path: {normalizedPath}");
                
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    // Track atoms before merge to identify new ones if atPlayer is requested
                    HashSet<string> atomsBefore = null;
                    if (atPlayer)
                    {
                        atomsBefore = new HashSet<string>();
                        foreach (Atom a in sc.GetAtoms()) atomsBefore.Add(a.uid);
                    }

                    if (!SceneLoadingUtils.LoadScene(normalizedPath, true))
                    {
                        LogUtil.LogError("[VPB] MergeSceneFile failed: scene load returned false");
                    }

                    if (atPlayer)
                    {
                        if (Panel != null) Panel.StartCoroutine(TeleportNewAtomsToPlayer(atomsBefore));
                        else StartCoroutine(TeleportNewAtomsToPlayer(atomsBefore));
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] MergeSceneFile crash: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void MergeScenePersonsOnly(string path, bool atPlayer = false, string personUidToImport = null, bool ensureUniqueIds = true, string targetUid = null)
        {
            LogUtil.Log($"[VPB] MergeScenePersonsOnly: {path}, atPlayer={atPlayer}, person='{personUidToImport}', unique={ensureUniqueIds}, target='{targetUid}'");
            
            if (!string.IsNullOrEmpty(targetUid))
            {
                ApplySceneDataToAtom(path, personUidToImport, targetUid, (atomNode) => true, "Full Person Preset");
                return;
            }

            MergeSceneFiltered(path, (atom) => {
                if (atom == null) return false;
                string type = atom["type"].Value;
                string id = atom["id"].Value;
                
                if (type != "Person") return false;
                
                // If a specific person is requested, check the ID
                if (!string.IsNullOrEmpty(personUidToImport))
                {
                    if (id != personUidToImport) 
                    {
                        // LogUtil.Log($"[VPB] Skipping person '{id}' (looking for '{personUidToImport}')");
                        return false;
                    }
                }
                
                LogUtil.Log($"[VPB] Including person: {id}");
                // Force atom to be On
                atom["on"] = "true";
                return true;
            }, "Merge Scene (Persons Only)", ensureUniqueIds, atPlayer);
        }

        public void MergeSceneAppearanceOnly(string path, string personUidToImport, bool ensureUniqueIds = true, string targetUid = null)
        {
            LogUtil.Log($"[VPB] MergeSceneAppearanceOnly: {path}, person='{personUidToImport}', target='{targetUid}'");
            
            if (!string.IsNullOrEmpty(targetUid))
            {
                ApplySceneDataToAtom(path, personUidToImport, targetUid, (storableId) => storableId == "AppearancePresets", "Appearance Only");
                return;
            }

            MergeSceneFiltered(path, (atom) => {
                if (atom == null) return false;
                if (atom["type"].Value != "Person") return false;
                if (atom["id"].Value != personUidToImport) return false;

                // Strip everything except basic info and AppearancePresets storable
                JSONArray storables = atom["storables"].AsArray;
                JSONArray filteredStorables = new JSONArray();
                foreach (JSONNode storable in storables)
                {
                    string id = storable["id"].Value;
                    if (id == "AppearancePresets")
                    {
                        filteredStorables.Add(storable);
                    }
                }
                atom["storables"] = filteredStorables;
                return true;
            }, "Merge Appearance Only", ensureUniqueIds, false);
        }

        public void MergeScenePoseOnly(string path, string personUidToImport, bool ensureUniqueIds = true, string targetUid = null)
        {
            LogUtil.Log($"[VPB] MergeScenePoseOnly: {path}, person='{personUidToImport}', target='{targetUid}'");

            if (!string.IsNullOrEmpty(targetUid))
            {
                ApplySceneDataToAtom(path, personUidToImport, targetUid, (storableId) => storableId == "PosePresets" || storableId == "control" || storableId.Contains("Control"), "Pose Only");
                return;
            }

            MergeSceneFiltered(path, (atom) => {
                if (atom == null) return false;
                if (atom["type"].Value != "Person") return false;
                if (atom["id"].Value != personUidToImport) return false;

                // Keep only Control storables (pose) and PosePresets if present
                JSONArray storables = atom["storables"].AsArray;
                JSONArray filteredStorables = new JSONArray();
                foreach (JSONNode storable in storables)
                {
                    string id = storable["id"].Value;
                    // Most pose info is in 'control' or 'PosePresets' or atom-specific pose storables
                    if (id == "PosePresets" || id == "control" || id.Contains("Control"))
                    {
                        filteredStorables.Add(storable);
                    }
                }
                atom["storables"] = filteredStorables;
                return true;
            }, "Merge Pose Only", ensureUniqueIds, false);
        }

        private void ApplySceneDataToAtom(string path, string sourcePersonId, string targetUid, Func<string, bool> storableFilter, string label)
        {
            try
            {
                JSONNode root = UI.LoadJSONWithFallback(path, this.FileEntry);
                if (root == null || root["atoms"] == null) return;

                if (FileButton.EnsureInstalledByText(root.ToString()))
                {
                    MVR.FileManagement.FileManager.Refresh();
                    FileManager.Refresh();
                }

                JSONNode sourceAtom = null;
                foreach (JSONNode atom in root["atoms"].AsArray)
                {
                    if (atom["type"].Value == "Person" && atom["id"].Value == sourcePersonId)
                    {
                        sourceAtom = atom;
                        break;
                    }
                }

                if (sourceAtom == null)
                {
                    LogUtil.LogError($"[VPB] Source person '{sourcePersonId}' not found in {path}");
                    return;
                }

                Atom targetAtom = SuperController.singleton.GetAtomByUid(targetUid);
                if (targetAtom == null)
                {
                    LogUtil.LogError($"[VPB] Target person '{targetUid}' not found in scene");
                    return;
                }

                LogUtil.Log($"[VPB] Applying {label} from {sourcePersonId} to {targetUid}");
                
                int appliedCount = 0;
                int skippedCount = 0;

                List<KeyValuePair<JSONStorable, JSONClass>> lateRestoreTargets = null;
                
                foreach (JSONNode storable in sourceAtom["storables"].AsArray)
                {
                    string id = storable["id"].Value;
                    if (storableFilter(id))
                    {
                        JSONStorable targetStorable = targetAtom.GetStorableByID(id);
                        if (targetStorable != null)
                        {
                            // Try using PresetManager if it exists (cleaner load)
                            var pm = targetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                            if (pm != null)
                            {
                                try
                                {
                                    MVR.FileManagement.FileManager.PushLoadDirFromFilePath(path);
                                    pm.LoadPresetFromJSON(storable.AsObject, false);
                                }
                                finally
                                {
                                    MVR.FileManagement.FileManager.PopLoadDir();
                                }
                                appliedCount++;
                            }
                            else
                            {
                                targetStorable.RestoreFromJSON(storable.AsObject);
                                if (lateRestoreTargets == null) lateRestoreTargets = new List<KeyValuePair<JSONStorable, JSONClass>>();
                                lateRestoreTargets.Add(new KeyValuePair<JSONStorable, JSONClass>(targetStorable, storable.AsObject));
                                appliedCount++;
                            }
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                }
                LogUtil.Log($"[VPB] Scene data application complete: {appliedCount} storables applied, {skippedCount} storables missing on target.");

                // Align with BA SceneImportCache lifecycle: LateRestore next frame + reset sim clothing.
                // We only LateRestore storables restored via RestoreFromJSON; preset managers handle their own internal lifecycle.
                SceneLoadingUtils.SchedulePostPersonApplyFixup(targetAtom, lateRestoreTargets);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] Error applying scene data: {ex.Message}");
            }
        }

        public void ReplaceSceneKeepPersons(string path)
        {
            if (Panel != null) Panel.StartCoroutine(ReplaceSceneKeepPersonsCoroutine(path));
            else StartCoroutine(ReplaceSceneKeepPersonsCoroutine(path));
        }

        private void MergeSceneFiltered(string path, Func<JSONNode, bool> atomFilter, string label, bool ensureUniqueIds = false, bool atPlayer = false)
        {
            string normalizedPath = UI.NormalizePath(path);
            LogUtil.Log($"[VPB] {label}: {normalizedPath}");
            
            SuperController sc = SuperController.singleton;
            HashSet<string> atomsBefore = null;
            if (atPlayer && sc != null)
            {
                atomsBefore = new HashSet<string>();
                foreach (Atom a in sc.GetAtoms()) atomsBefore.Add(a.uid);
                LogUtil.Log($"[VPB] Tracking {atomsBefore.Count} atoms before merge for teleport.");
            }

            string tempPath = SceneLoadingUtils.CreateFilteredSceneJSON(normalizedPath, this.FileEntry, atomFilter, ensureUniqueIds);
            if (!string.IsNullOrEmpty(tempPath))
            {
                LogUtil.Log($"[VPB] Created filtered temp file: {tempPath}");
                try
                {
                    if (!SceneLoadingUtils.LoadScene(tempPath, true))
                    {
                        LogUtil.LogError($"[VPB] Failed to {label}: scene load returned false");
                    }
                    
                    if (atPlayer && atomsBefore != null)
                    {
                        if (Panel != null) Panel.StartCoroutine(TeleportNewAtomsToPlayer(atomsBefore));
                        else StartCoroutine(TeleportNewAtomsToPlayer(atomsBefore));
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"[VPB] Failed to {label}: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else
            {
                LogUtil.LogError($"[VPB] Failed to create filtered scene JSON for {normalizedPath}");
            }
        }

        private System.Collections.IEnumerator ReplaceSceneKeepPersonsCoroutine(string path)
        {
            string normalizedPath = UI.NormalizePath(path);
            LogUtil.Log($"[VPB] Replace Scene Keep Persons: {normalizedPath}");

            SuperController sc = SuperController.singleton;
            if (sc == null) yield break;

            List<string> personUids = new List<string>();
            foreach (Atom a in sc.GetAtoms())
            {
                if (a.type == "Person") personUids.Add(a.uid);
            }
            
            if (personUids.Count == 0)
            {
                SceneLoadingUtils.LoadScene(normalizedPath, false);
                yield break;
            }

            string currentSceneTemp = Path.Combine(sc.savesDir, "vpb_temp_current_" + Guid.NewGuid().ToString() + ".json");
            sc.Save(currentSceneTemp);
            
            string personsOnlyTemp = CreateFilteredSceneJSON(currentSceneTemp, null, (atom) => atom["type"].Value == "Person", false);
            if (File.Exists(currentSceneTemp)) File.Delete(currentSceneTemp);
            
            if (string.IsNullOrEmpty(personsOnlyTemp))
            {
                LogUtil.LogError("[VPB] Failed to extract persons from current scene.");
                yield break;
            }
            
            // Remove persons from new scene to prevent duplication/conflict
            string newSceneNoPersons = CreateFilteredSceneJSON(normalizedPath, this.FileEntry, (atom) => atom["type"].Value != "Person", false);
            string sceneToLoad = string.IsNullOrEmpty(newSceneNoPersons) ? normalizedPath : newSceneNoPersons;
            
            SceneLoadingUtils.LoadScene(sceneToLoad, false);
            
            // Wait for load
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame(); // Extra frame

            SceneLoadingUtils.LoadScene(personsOnlyTemp, true);
        }

        private string CreateFilteredSceneJSON(string path, FileEntry entry, Func<JSONNode, bool> atomFilter, bool ensureUniqueIds = false)
        {
            try
            {
                return SceneLoadingUtils.CreateFilteredSceneJSON(path, entry, atomFilter, ensureUniqueIds);
            }
            catch
            {
                return null;
            }
        }

        private System.Collections.IEnumerator TeleportNewAtomsToPlayer(HashSet<string> atomsBefore)
        {
            // Wait for merge to finish (usually synchronous for the structure, but some components might take a frame)
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            SuperController sc = SuperController.singleton;
            if (sc == null || sc.centerCameraTarget == null) yield break;

            Vector3 targetPos = sc.centerCameraTarget.transform.position + sc.centerCameraTarget.transform.forward * 1.5f;
            // Keep height reasonable
            targetPos.y = sc.centerCameraTarget.transform.position.y;
            
            Quaternion targetRot = Quaternion.LookRotation(-sc.centerCameraTarget.transform.forward, Vector3.up);
            // Level out the rotation
            Vector3 euler = targetRot.eulerAngles;
            euler.x = 0;
            euler.z = 0;
            targetRot = Quaternion.Euler(euler);

            Atom atomToSelect = null;
            Atom lastAddedAtom = null;
            foreach (Atom atom in sc.GetAtoms())
            {
                if (!atomsBefore.Contains(atom.uid))
                {
                    if (atom != null && atom.mainController != null)
                    {
                        atom.mainController.transform.position = targetPos;
                        atom.mainController.transform.rotation = targetRot;
                        lastAddedAtom = atom;
                        // If we found a person, prioritize selecting them
                        if (atom.type == "Person")
                        {
                            atomToSelect = atom;
                        }
                    }
                }
            }
            
            if (atomToSelect == null && lastAddedAtom != null)
            {
                atomToSelect = lastAddedAtom;
            }

            if (atomToSelect != null)
            {
                // Use reflection for SelectAtom since it might be missing from the build-time references
                // but is usually present in the VaM environment.
                try
                {
                    MethodInfo selectAtom = sc.GetType().GetMethod("SelectAtom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (selectAtom != null)
                    {
                        selectAtom.Invoke(sc, new object[] { atomToSelect });
                    }
                }
                catch
                {
                    // Ignore if selection fails
                }
            }
        }

        private System.Collections.IEnumerator LoadSubSceneCoroutine(string path)
        {
            // Track existing atoms to find the new one
            HashSet<string> existingAtoms = new HashSet<string>();
            foreach (var a in SuperController.singleton.GetAtoms()) existingAtoms.Add(a.uid);

            yield return SuperController.singleton.AddAtomByType("SubScene", "", true, true, true);
            yield return new WaitForEndOfFrame();
            
            // Find the newly created SubScene atom
            Atom subSceneAtom = null;
            foreach (var atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "SubScene" && !existingAtoms.Contains(atom.uid))
                {
                    subSceneAtom = atom;
                    break;
                }
            }
            
            if (subSceneAtom != null)
            {
                SubScene subScene = subSceneAtom.GetComponentInChildren<SubScene>();
                if (subScene != null)
                {
                    LogUtil.Log($"[VPB] Calling LoadSubSceneWithPath on SubScene atom {subSceneAtom.uid} with path: {path}");
                    MethodInfo loadMethod = typeof(SubScene).GetMethod("LoadSubSceneWithPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (loadMethod != null)
                    {
                        loadMethod.Invoke(subScene, new object[] { path });
                    }
                    else
                    {
                        LogUtil.LogError("[VPB] Method LoadSubSceneWithPath not found on SubScene component");
                    }
                }
                else
                {
                    LogUtil.LogError("[VPB] SubScene component not found on newly created atom");
                }
            }
            else
            {
                LogUtil.LogError("[VPB] Could not find newly created SubScene atom");
            }

            if (VPBConfig.Instance != null)
                VPBConfig.Instance.EndSceneLoad();
        }

        private bool EnsureInstalled()
        {
            return UI.EnsureInstalled(FileEntry);
        }

        private void ApplyClothingToAtom(Atom atom, string path, string appearanceClothingMode = null)
        {
            bool installed = EnsureInstalled();

            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            string normalizedPath = UI.NormalizePath(path);

            string legacyPath = normalizedPath;
            int colonIndex = normalizedPath.IndexOf(":/");
            if (colonIndex >= 0)
            {
                legacyPath = normalizedPath.Substring(colonIndex + 2);
            }

            LogUtil.Log($"[DragDropDebug] Attempting to apply. FullPath: {normalizedPath}, LegacyPath: {legacyPath}, Installed: {installed}");

            JSONStorable geometry = atom.GetStorableByID("geometry");
            ItemType itemType = GetItemType(FileEntry);
            string ext = Path.GetExtension(normalizedPath).ToLowerInvariant();
            string appearanceMode = appearanceClothingMode;
            if (string.IsNullOrEmpty(appearanceMode))
            {
                appearanceMode = (itemType == ItemType.Appearance) ? "replace" : "merge";
            }

            bool isPoseCategory = false;
            if (Panel != null)
            {
                string catPath = Panel.GetCurrentPath();
                if (!string.IsNullOrEmpty(catPath))
                {
                    catPath = catPath.Replace("\\", "/");
                    if (catPath.IndexOf("/Pose", StringComparison.OrdinalIgnoreCase) >= 0 || catPath.IndexOf("Saves/Person", StringComparison.OrdinalIgnoreCase) >= 0)
                        isPoseCategory = true;
                }

                string catTitle = Panel.GetTitle();
                if (!string.IsNullOrEmpty(catTitle) && catTitle.IndexOf("pose", StringComparison.OrdinalIgnoreCase) >= 0)
                    isPoseCategory = true;

                string catExt = Panel.GetCurrentExtension();
                if (!string.IsNullOrEmpty(catExt) && catExt.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0 && catExt.IndexOf("vap", StringComparison.OrdinalIgnoreCase) >= 0)
                    isPoseCategory = true;
            }

            if (ext == ".json" && atom.type == "Person" && (itemType == ItemType.Other || itemType == ItemType.Scene || isPoseCategory)) itemType = ItemType.Pose;

            if (CheckDualPose())
            {
                ApplyDualPose(atom, _dualPoseNode);
                return;
            }

            // Capture state for Undo
            if (Panel != null)
            {
                try
                {
                    try
                    {
                        LogUtil.Log("[VPB] Undo capture: itemType=" + itemType + " atomType=" + atom.type + " entryPath=" + (FileEntry != null ? FileEntry.Path : "<null>"));
                    }
                    catch { }

                    if (itemType == ItemType.Appearance && atom.type == "Person")
                    {
                        bool ok = PushUndoSnapshotForFullAtomState(atom);
                        if (!ok)
                        {
                            LogUtil.LogWarning("[VPB] Full atom undo snapshot unavailable; falling back to storable snapshot for " + atom.uid);

                            List<JSONClass> storableSnapshotsAll = new List<JSONClass>();
                            JSONClass geometrySnapshotAll = null;
                            JSONClass skinSnapshotAll = null;
                            try
                            {
                                foreach (var sid in atom.GetStorableIDs())
                                {
                                    if (IsPluginLikeStorableId(sid)) continue;
                                    JSONStorable s = null;
                                    try { s = atom.GetStorableByID(sid); } catch { }
                                    if (s == null) continue;
                                    JSONClass snap = null;
                                    try { snap = s.GetJSON(); } catch { }
                                    if (snap != null)
                                    {
                                        if (string.Equals(sid, "geometry", StringComparison.OrdinalIgnoreCase)) geometrySnapshotAll = snap;
                                        if (string.Equals(sid, "Skin", StringComparison.OrdinalIgnoreCase)) skinSnapshotAll = snap;
                                    }
                                    if (snap != null) storableSnapshotsAll.Add(snap);
                                }
                            }
                            catch { }

                            string atomUid = atom.uid;
                            Panel.PushUndo(() =>
                            {
                                Atom targetAtom = null;
                                try { targetAtom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null; } catch { }
                                if (targetAtom == null)
                                {
                                    LogUtil.LogError("[VPB] Undo failed: Atom " + atomUid + " not found.");
                                    return;
                                }

                                for (int i = 0; i < storableSnapshotsAll.Count; i++)
                                {
                                    JSONClass snap = storableSnapshotsAll[i];
                                    if (snap == null) continue;
                                    string sid = null;
                                    try { sid = snap["id"].Value; } catch { }
                                    if (string.IsNullOrEmpty(sid)) continue;
                                    if (IsPluginLikeStorableId(sid)) continue;
                                    JSONStorable s = null;
                                    try { s = targetAtom.GetStorableByID(sid); } catch { }
                                    if (s == null) continue;
                                    try { s.RestoreFromJSON(snap); } catch { }
                                }

                                try
                                {
                                    StartCoroutine(PostUndoPersonRefreshCoroutine(atomUid, geometrySnapshotAll, skinSnapshotAll, 5));
                                }
                                catch { }

                                LogUtil.Log("[VPB] Undo performed on " + atomUid + " (AllStorables)");
                            });
                        }
                    }
                    else
                    {
                        // Only snapshot relevant storables to avoid breaking physics/scene state
                        // We primarily care about geometry (clothing/hair items) and StorableIds for presets
                        List<JSONClass> storableSnapshots = new List<JSONClass>();
                        Dictionary<string, bool> geometryToggleSnapshot = null;

                        // 1. Geometry (Direct toggle items)
                        JSONStorable geometryStorable = atom.GetStorableByID("geometry");
                        if (geometryStorable != null)
                        {
                            geometryToggleSnapshot = new Dictionary<string, bool>();
                            List<string> names = geometryStorable.GetBoolParamNames();
                            if (names != null)
                            {
                                foreach (string key in names)
                                {
                                    if (key.StartsWith("clothing:") || key.StartsWith("hair:"))
                                    {
                                        JSONStorableBool b = geometryStorable.GetBoolJSONParam(key);
                                        if (b != null) geometryToggleSnapshot[key] = b.val;
                                    }
                                }
                            }
                        }

                        // 2. Preset Managers (Clothing, Hair, Pose, Skin, etc)
                        // We can snapshot all PresetManagers on the atom as they control the state of what's applied
                        foreach (var storable in atom.GetStorableIDs())
                        {
                            // Heuristic: If it ends in "Presets"/"Preset" or is a known manager
                            bool snapshot = false;
                            if (storable.EndsWith("Presets") || storable.EndsWith("Preset") || storable == "Skin" || storable.EndsWith("Physics"))
                            {
                                snapshot = true;
                            }
                            else if (storable.IndexOf("morph", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Morph state must be included for Appearance undo to fully restore the person.
                                snapshot = true;
                            }
                            else if (storable.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase) || storable.StartsWith("hairItem", StringComparison.OrdinalIgnoreCase) || storable.IndexOf("ClothingItem", StringComparison.OrdinalIgnoreCase) >= 0 || storable.IndexOf("HairItem", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                snapshot = true;
                            }

                            if (snapshot)
                            {
                                JSONStorable s = atom.GetStorableByID(storable);
                                if (s != null) storableSnapshots.Add(s.GetJSON());
                            }
                        }

                        string atomUid = atom.uid;
                        Panel.PushUndo(() =>
                        {
                            Atom targetAtom = SuperController.singleton.GetAtomByUid(atomUid);
                            if (targetAtom == null)
                            {
                                LogUtil.LogError($"[Gallery] Undo failed: Atom {atomUid} not found.");
                                return;
                            }

                            if (geometryToggleSnapshot != null)
                            {
                                JSONStorable geo = targetAtom.GetStorableByID("geometry");
                                if (geo != null)
                                {
                                    foreach (var kvp in geometryToggleSnapshot)
                                    {
                                        JSONStorableBool b = geo.GetBoolJSONParam(kvp.Key);
                                        if (b != null) b.val = kvp.Value;
                                    }

                                    List<string> currentNames = geo.GetBoolParamNames();
                                    if (currentNames != null)
                                    {
                                        foreach (string key2 in currentNames)
                                        {
                                            if ((key2.StartsWith("clothing:") || key2.StartsWith("hair:")) && !geometryToggleSnapshot.ContainsKey(key2))
                                            {
                                                JSONStorableBool b = geo.GetBoolJSONParam(key2);
                                                if (b != null) b.val = false;
                                            }
                                        }
                                    }
                                }
                            }

                            // Restore specific storables
                            foreach (var snap in storableSnapshots)
                            {
                                string sid = snap["id"].Value;
                                JSONStorable s = targetAtom.GetStorableByID(sid);
                                if (s != null) s.RestoreFromJSON(snap);
                            }

                            LogUtil.Log($"[Gallery] Undo performed on {atomUid} (Storables)");
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[Gallery] Failed to capture undo state: " + ex.Message);
                }
            }

            bool replaceMode = Panel != null && Panel.DragDropReplaceMode;
            bool isClothingOrHair = (itemType == ItemType.Clothing || itemType == ItemType.Hair || itemType == ItemType.ClothingItem || itemType == ItemType.HairItem || itemType == ItemType.ClothingPreset || itemType == ItemType.HairPreset);
            LogUtil.Log($"[DragDropDebug] Panel={Panel != null}, ReplaceMode={replaceMode}, ItemType={itemType}, IsClothingOrHair={isClothingOrHair}");

            if (Panel != null && Panel.DragDropReplaceMode && isClothingOrHair)
            {
                bool isHair = (itemType == ItemType.Hair || itemType == ItemType.HairItem || itemType == ItemType.HairPreset);
                bool isClothing = (itemType == ItemType.Clothing || itemType == ItemType.ClothingItem || itemType == ItemType.ClothingPreset);

                if (geometry != null)
                {
                     LogUtil.Log($"[DragDropDebug] Replace mode check: Checking types...");
                     
                     HashSet<string> droppedRegions = isHair ? GetHairRegions(FileEntry) : GetClothingRegions(FileEntry);
                     LogUtil.Log($"[DragDropDebug] Dropped regions: {string.Join(",", droppedRegions.ToArray())}");

                     List<string> all = geometry.GetBoolParamNames();
                     if (all != null)
                     {
                         foreach(string n in all)
                         {
                             bool check = false;
                             string paramType = "";
                             if (isHair && n.StartsWith("hair:")) 
                             {
                                 check = true; 
                                 paramType = "hair";
                             }
                             else if (isClothing && n.StartsWith("clothing:")) 
                             {
                                 check = true;
                                 paramType = "clothing";
                             }

                             if (check)
                             {
                                 string itemName = n.Substring(paramType.Length + 1); // remove "hair:" or "clothing:"
                                 VarFileEntry existingEntry = FileManager.GetVarFileEntry(itemName);
                                 
                                 HashSet<string> existingRegions;
                                 if (existingEntry != null)
                                 {
                                     existingRegions = isHair ? GetHairRegions(existingEntry) : GetClothingRegions(existingEntry);
                                 }
                                 else
                                 {
                                     // Try heuristics on the param name
                                     existingRegions = isHair ? GetRegionsFromHeuristics(itemName) : GetClothingRegionsFromHeuristics(itemName);
                                     // No default fallback for existing items - safer to NOT clear if unknown
                                 }

                                 if (droppedRegions.Overlaps(existingRegions))
                                 {
                                     JSONStorableBool p = geometry.GetBoolJSONParam(n);
                                     if (p != null && p.val) 
                                     {
                                         var intersection = droppedRegions.Intersect(existingRegions);
                                         LogUtil.Log($"[DragDropDebug] Clearing overlapping {paramType} {n}. Dropped regions: [{string.Join(",", droppedRegions.ToArray())}]. Existing regions: [{string.Join(",", existingRegions.ToArray())}]. Overlap on: [{string.Join(",", intersection.ToArray())}]");
                                         p.val = false;
                                     }
                                 }
                                 else if (VPBConfig.Instance.IsDevMode)
                                 {
                                     LogUtil.Log($"[DragDropDebug] Preserving {paramType} {n} (Regions: {string.Join(",", existingRegions.ToArray())}) - No overlap.");
                                 }
                             }
                         }
                     }
                }
            }
            else
            {
                LogUtil.Log($"[DragDropDebug] Add Mode (Replace OFF). Skipping overlap checks for {normalizedPath}");
            }

            if (itemType == ItemType.Appearance && appearanceMode == "replace" && geometry != null)
            {
                foreach (var name in geometry.GetBoolParamNames())
                {
                    if (name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase) || name.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                    {
                        JSONStorableBool p = geometry.GetBoolJSONParam(name);
                        if (p != null) p.val = false;
                    }
                }
            }

            if (itemType == ItemType.ClothingPreset || itemType == ItemType.HairPreset)
            {
                // Clothing/Hair Item Presets (.vap)
                LogUtil.Log($"[DragDropDebug] Applying {itemType}: {normalizedPath}");
                ActivateClothingHairItemPreset(atom, FileEntry, itemType == ItemType.ClothingPreset);
                return;
            }

            // Try to load as preset first (standard for Clothing/Hair presets and Poses)
            ext = Path.GetExtension(normalizedPath).ToLowerInvariant();
            if (ext == ".vap" || ext == ".json" || ext == ".vac" || (ext == ".vam" && itemType == ItemType.Appearance))
            {
                string storableId = GetStorableIdForItemType(itemType);
                if (storableId != null && atom.type == "Person")
                {
                    bool isPose = itemType == ItemType.Pose;
                    PresetLockStore lockStore = new PresetLockStore();

                    if (atom.presetManagerControls != null)
                    {
                        bool isAppearance = itemType == ItemType.Appearance;
                        bool lockClothing = isPose;
                        bool lockMorphs = isPose;

                        // Clear all locks, and specifically lock what we don't want changed
                        if (isPose || (isAppearance && appearanceMode == "replace"))
                        {
                            lockStore.StorePresetLocks(atom, true, lockClothing, lockMorphs);
                        }
                    }

                    bool presetLoaded = false;
                    bool suppressRoot = isPose && !Input.GetKey(KeyCode.LeftShift); // Default to suppress root (In Place), hold Shift to move
                    
                    // Capture state for restoration
                    JSONStorable presetStorable = atom.GetStorableByID(storableId);
                    JSONStorableBool loadOnSelectJSB = presetStorable != null ? presetStorable.GetBoolJSONParam("loadPresetOnSelect") : null;
                    bool loadOnSelectPreState = loadOnSelectJSB != null ? loadOnSelectJSB.val : false;
                    JSONStorableString presetNameJSS = presetStorable != null ? presetStorable.GetStringJSONParam("presetName") : null;
                    string initialPresetName = presetNameJSS != null ? presetNameJSS.val : "";

                    try
                    {
                        if (loadOnSelectJSB != null) loadOnSelectJSB.val = false;

                        LogUtil.Log($"[DragDropDebug] Loading preset type={itemType}, storableId={storableId}, path={normalizedPath}, SuppressRoot={suppressRoot}");
                        
                        // Get the storable for this preset type
                        if (presetStorable != null)
                        {
                            MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                            if (presetManager != null)
                            {
                                bool isVarPath = normalizedPath.Contains(":");
                                bool isPosePath = normalizedPath.IndexOf("Custom/Atom/Person/Pose", StringComparison.OrdinalIgnoreCase) >= 0;
                                // NEW: For .json legacy files, check if they are in Saves/Person/Pose too
                                if (!isPosePath) 
                                {
                                    isPosePath = normalizedPath.IndexOf("Saves/Person/Pose", StringComparison.OrdinalIgnoreCase) >= 0;
                                }

                                if (presetNameJSS != null)
                                {
                                    presetNameJSS.val = presetManager.GetPresetNameFromFilePath(SuperController.singleton.NormalizePath(normalizedPath));
                                }

                                // Standardizing on JSON loading for all presets to avoid "not compatible with store folder path" errors
                                // This also ensures that VAR paths and loose files work identically.
                                JSONClass presetJSON = SuperController.singleton.LoadJSON(normalizedPath).AsObject;
                                if (presetJSON != null)
                                {
                                    if (FileButton.EnsureInstalledByText(presetJSON.ToString()))
                                    {
                                        MVR.FileManagement.FileManager.Refresh();
                                        FileManager.Refresh();
                                    }

                                    // Detect if this is a scene file and extract the appropriate atom data
                                    if (presetJSON["atoms"] != null)
                                    {
                                        JSONClass extracted = ExtractAtomFromScene(presetJSON, atom.type);
                                        if (extracted != null)
                                        {
                                            presetJSON = extracted;
                                        }
                                        else
                                        {
                                            LogUtil.LogWarning($"[VPB] ApplyClothingToAtom: Scene file does not contain a {atom.type} atom.");
                                            // Fallback: don't return, maybe it works anyway? No, if it has atoms it's a scene.
                                            // But let's stay safe and just continue with extracted if possible.
                                        }
                                    }

                                    string presetPackageName = "";
                                    string folderFullPath = "";
                                    
                                    if (normalizedPath.Contains(":"))
                                    {
                                        presetPackageName = normalizedPath.Substring(0, normalizedPath.IndexOf(':'));
                                        folderFullPath = MVR.FileManagementSecure.FileManagerSecure.GetDirectoryName(normalizedPath);
                                        folderFullPath = MVR.FileManagementSecure.FileManagerSecure.NormalizeLoadPath(folderFullPath);
                                        
                                        string presetJSONString = presetJSON.ToString();
                                        bool modified = false;
                                        
                                        if (presetJSONString.Contains("SELF:"))
                                        {
                                            presetJSONString = presetJSONString.Replace("SELF:", presetPackageName + ":");
                                            modified = true;
                                        }
                                        
                                        if (presetJSONString.Contains("\":\"./"))
                                        {
                                            presetJSONString = presetJSONString.Replace("\":\"./", "\":\"" + folderFullPath + "/");
                                            modified = true;
                                        }
                                        
                                        if (modified)
                                        {
                                            presetJSON = SimpleJSON.JSON.Parse(presetJSONString).AsObject;
                                        }
                                    }
                                    
                                    LogUtil.Log($"[DragDropDebug] JSON loaded successfully from {normalizedPath}");

                                        if (itemType == ItemType.Appearance && appearanceMode == "keep" && presetJSON["storables"] != null)
                                        {
                                            JSONArray storables = presetJSON["storables"].AsArray;
                                            JSONArray filteredStorables = new JSONArray();
                                            foreach (JSONNode node in storables)
                                            {
                                                string sid = node["id"].Value;
                                                bool isClothing = sid.StartsWith("clothing", StringComparison.OrdinalIgnoreCase) || sid.StartsWith("wearable", StringComparison.OrdinalIgnoreCase);
                                                bool isHair = sid.StartsWith("hair", StringComparison.OrdinalIgnoreCase);
                                                if (isClothing || isHair) continue;
                                                filteredStorables.Add(node);
                                            }
                                            presetJSON["storables"] = filteredStorables;
                                        }

                                        if (itemType == ItemType.Appearance)
                                        {
                                            presetJSON["setUnlistedParamsToDefault"].AsBool = true;
                                        }

                                        // Function to clean presets array (Shared logic)
                                        void CleanPresets(JSONArray presets)
                                        {
                                            if (presets == null) return;
                                            for (int j = 0; j < presets.Count; j++)
                                            {
                                                JSONClass p = presets[j] as JSONClass;
                                                if (p != null && p["id"].Value == "control")
                                                {
                                                    // Instead of removing the node, we strip its position/rotation
                                                    // This avoids invalidating the preset if 'control' is required
                                                    if (p.HasKey("position")) p.Remove("position");
                                                    if (p.HasKey("rotation")) p.Remove("rotation");

                                                    LogUtil.Log("[DragDropDebug] Suppressed root node (control) properties from Pose Preset.");
                                                    break; 
                                                }
                                            }
                                        }

                                        // NEW: Suppress Root Node logic
                                        if (suppressRoot && itemType == ItemType.Pose)
                                        {
                                            try
                                            {
                                                if (presetJSON["storables"] != null)
                                                {
                                                    JSONArray storables = presetJSON["storables"] as JSONArray;
                                                    if (storables != null)
                                                    {
                                                        for (int i = 0; i < storables.Count; i++)
                                                        {
                                                            JSONClass s = storables[i] as JSONClass;
                                                            // Check for PosePresets ID or any other that matches the target storableId
                                                            if (s != null && s["id"].Value == storableId)
                                                            {
                                                                if (s["presets"] != null) CleanPresets(s["presets"] as JSONArray);
                                                            }
                                                        }
                                                    }
                                                }
                                                else if (presetJSON["presets"] != null)
                                                {
                                                    // Direct storable dump?
                                                    // Verify ID if present, otherwise assume it's the right one
                                                    if (presetJSON["id"] == null || presetJSON["id"].Value == storableId)
                                                    {
                                                        CleanPresets(presetJSON["presets"] as JSONArray);
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                LogUtil.LogError("[DragDropDebug] Failed to suppress root node: " + ex.Message);
                                            }
                                        }

                                        // Simplified handling: Use direct PresetManager load
                                        // This bypasses the complexity of storable actions + temp files
                                        try
                                        {
                                            if (itemType == ItemType.Pose)
                                            {
                                                LogUtil.Log($"[DragDropDebug] Loading Pose via direct PresetManager injection (Bypassing temp files)");
                                                
                                                // Specific logging for .json files debugging
                                                if (ext == ".json")
                                                {
                                                    // Convert Keys to array for string.Join compatibility in older .NET/Unity versions
                                                    string[] keys = new string[0];
                                                    if (presetJSON.Keys != null) keys = presetJSON.Keys.ToArray();
                                                    LogUtil.Log($"[DragDropDebug] .json Pose Debug: Keys in JSON: {string.Join(", ", keys)}");
                                                    
                                                    if (presetJSON["id"] != null) LogUtil.Log($"[DragDropDebug] .json Pose Debug: Existing 'id': {presetJSON["id"].Value}");
                                                    else LogUtil.Log($"[DragDropDebug] .json Pose Debug: No 'id' field found.");
                                                    
                                                    if (presetJSON["presets"] != null) LogUtil.Log($"[DragDropDebug] .json Pose Debug: Found 'presets' array.");
                                                    if (presetJSON["storables"] != null) LogUtil.Log($"[DragDropDebug] .json Pose Debug: Found 'storables' array.");
                                                }
                                            }

                                            // Ensure ID is correct (fixes "not a preset for current store" error)
                                            // Only inject if it's NOT a container (no 'storables' array)
                                            // If it has 'storables', we assume the ID is correct for the container (e.g. 'Person')
                                            if (presetJSON["storables"] == null)
                                            {
                                                // Handle 'atoms' root key (Legacy scene/person save used as pose)
                                                // Optimized Native Loading: Use direct Atom.Restore for maximum performance and compatibility
                                                if (presetJSON["atoms"] != null)
                                                {
                                                    LogUtil.Log($"[DragDropDebug] 'atoms' root key detected. Using optimized Native Atom Restoration...");
                                                    JSONArray atomsArray = presetJSON["atoms"] as JSONArray;
                                                    
                                                    if (atomsArray != null && atomsArray.Count > 0)
                                                    {
                                                        // Find the target atom (usually "Person" or just the first one)
                                                        JSONClass targetAtom = null;
                                                        for(int i=0; i<atomsArray.Count; i++) 
                                                        {
                                                            JSONClass a = atomsArray[i] as JSONClass;
                                                            if (a != null && (a["id"].Value == "Person" || a["type"].Value == "Person"))
                                                            {
                                                                targetAtom = a;
                                                                break;
                                                            }
                                                        }
                                                        if (targetAtom == null) targetAtom = atomsArray[0] as JSONClass;

                                                        if (targetAtom != null)
                                                        {
                                                            LogUtil.Log($"[DragDropDebug] Restoring atom data from '{targetAtom["id"]?.Value}' directly to '{atom.name}'");

                                                            // Handle Suppress Root (Load in Place)
                                                            if (suppressRoot)
                                                            {
                                                                // Strip control position/rotation from the source JSON before restoring
                                                                JSONArray targetStorables = targetAtom["storables"] as JSONArray;
                                                                if (targetStorables != null)
                                                                {
                                                                    for(int k=0; k<targetStorables.Count; k++)
                                                                    {
                                                                        JSONClass s = targetStorables[k] as JSONClass;
                                                                        if (s != null && s["id"].Value == "control")
                                                                        {
                                                                             if (s.HasKey("position")) s.Remove("position");
                                                                             if (s.HasKey("rotation")) s.Remove("rotation");
                                                                             LogUtil.Log($"[DragDropDebug] Suppressed root motion in legacy atom dump.");
                                                                             break;
                                                                        }
                                                                    }
                                                                }
                                                            }

                                                            // EXECUTE NATIVE RESTORE PIPELINE
                                                            // We set restoreAppearance=false to ensure we only load the Pose (Physics/Transform)
                                                            // We set restorePhysical=true
                                                            
                                                            atom.PreRestore(true, false);
                                                            
                                                            // Only restore main transform if not suppressing root
                                                            if (!suppressRoot)
                                                            {
                                                                atom.RestoreTransform(targetAtom);
                                                            }
                                                            
                                                            // Restore(jc, restorePhysical, restoreAppearance, restoreParent)
                                                            atom.Restore(targetAtom, true, false, false);
                                                            
                                                            atom.LateRestore(targetAtom, true, false, false);
                                                            atom.PostRestore(true, false);
                                                            
                                                            LogUtil.Log($"[DragDropDebug] Native Atom Restoration complete.");

                                                            // Post-fixup: sim clothing often needs a reset after pose/physics restore.
                                                            SceneLoadingUtils.SchedulePostPersonApplyFixup(atom);
                                                            presetLoaded = true;
                                                            return; // Skip the rest of the PresetManager logic
                                                        }
                                                    }
                                                }

                                                // If we have a 'storables' root key now (either from conversion or original), 
                                                // we don't need to inject ID. It's a Package-style preset.
                                                if (presetJSON["storables"] == null)
                                                {
                                                    if (presetJSON["id"] == null || presetJSON["id"].Value != storableId)
                                                    {
                                                        LogUtil.Log($"[DragDropDebug] Injecting missing/correcting ID '{storableId}' into preset JSON (No 'storables' detected)");
                                                        presetJSON["id"] = storableId;
                                                    }
                                                }
                                                else
                                                {
                                                    LogUtil.Log($"[DragDropDebug] 'storables' detected (or created). Preserving container structure.");
                                                }
                                            }
                                            else
                                            {
                                                LogUtil.Log($"[DragDropDebug] 'storables' detected in JSON. Keeping existing ID '{presetJSON["id"]?.Value}' to preserve container structure.");
                                            }

                                            // Special handling for legacy .json files:
                                            // They might not have the "presets" array wrapper if they are direct dumps.
                                            // But if they are direct dumps, they usually have "id" matched or null.
                                            // The CleanPresets logic already handles "presets" vs "storables" vs direct.
                                            
                                            // Ensure we are setting the last restored data so 'Undo' might work (or just system consistency)
                                            atom.SetLastRestoredData(presetJSON, true, true);
                                            
                                            bool merge = true;
                                            if (itemType == ItemType.Appearance) merge = (appearanceMode == "merge");
                                            else if (itemType == ItemType.Pose) merge = false;

                                            LogUtil.Log($"[DragDropDebug] Calling LoadPresetFromJSON (merge={merge})...");
                                            try
                                            {
                                                MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath);
                                                presetManager.LoadPresetFromJSON(presetJSON, merge); 
                                                presetLoaded = true;
                                            }
                                            finally
                                            {
                                                MVR.FileManagement.FileManager.PopLoadDir();
                                            }
                                            LogUtil.Log($"[DragDropDebug] Successfully loaded preset via PresetManager.LoadPresetFromJSON");

                                            // Post-fixup: after applying appearance/clothing/morph/pose presets, reset sim clothing.
                                            // This helps ensure clothing respects updated body physics/colliders.
                                            SceneLoadingUtils.SchedulePostPersonApplyFixup(atom);
                                        }
                                        catch (Exception ex)
                                        {
                                            LogUtil.LogError("[DragDropDebug] Direct PresetManager load failed: " + ex.Message);
                                        }
                                    }
                                    else
                                    {
                                        LogUtil.LogError($"[DragDropDebug] Failed to load preset JSON from {normalizedPath}");
                                    }
                                }
                                else
                                {
                                    LogUtil.LogError($"[DragDropDebug] PresetManager not found on storable {storableId}");
                                }
                            }
                            else
                            {
                                LogUtil.LogError($"[DragDropDebug] Storable {storableId} not found on atom");
                            }
                        }
                        catch (Exception ex)
                        {
                             LogUtil.LogError("[DragDropDebug] LoadPreset failed for " + normalizedPath + ": " + ex.Message);
                             // Fallthrough to legacy toggle
                        }
                        finally
                        {
                            if (loadOnSelectJSB != null) loadOnSelectJSB.val = loadOnSelectPreState;
                            if (presetNameJSS != null) presetNameJSS.val = initialPresetName;

                            // Restore locks
                            if (atom.type == "Person")
                            {
                                lockStore.RestorePresetLocks(atom);
                            }
                        }
                        
                        if (presetLoaded) return;
                    }
                }

            if (geometry != null)
            {
                // Helper to try toggling
                bool TryToggle(string p)
                {
                    string paramName = "clothing:" + p;
                    JSONStorableBool param = geometry.GetBoolJSONParam(paramName);
                    if (param != null) 
                    {
                        LogUtil.Log($"[DragDropDebug] Found clothing param: {paramName}, setting to true.");
                        param.val = true;
                        return true;
                    }
                    paramName = "hair:" + p;
                    param = geometry.GetBoolJSONParam(paramName);
                    if (param != null)
                    {
                        LogUtil.Log($"[DragDropDebug] Found hair param: {paramName}, setting to true.");
                        param.val = true;
                        return true;
                    }
                    LogUtil.Log($"[DragDropDebug] Param not found: {paramName}");
                    return false;
                }

                LogUtil.Log($"[DragDropDebug] Trying legacy toggle with: {legacyPath}");
                if (TryToggle(legacyPath)) return;

                if (normalizedPath != legacyPath)
                {
                    LogUtil.Log($"[DragDropDebug] Trying legacy toggle with full path: {normalizedPath}");
                    if (TryToggle(normalizedPath)) return;
                }

                // Try .vaj replacement for .vam (legacy handling)
                if (ext == ".vam")
                {
                    string vajPath = legacyPath.Substring(0, legacyPath.Length - 4) + ".vaj";
                    LogUtil.Log($"[DragDropDebug] Trying .vaj toggle with: {vajPath}");
                    if (TryToggle(vajPath)) return;

                    if (normalizedPath != legacyPath)
                    {
                        string vajFullPath = normalizedPath.Substring(0, normalizedPath.Length - 4) + ".vaj";
                        LogUtil.Log($"[DragDropDebug] Trying .vaj toggle with full path: {vajFullPath}");
                        if (TryToggle(vajFullPath)) return;
                    }
                }
            }
            else
            {
                LogUtil.Log("[DragDropDebug] Geometry storable not found on atom.");
            }
        }

        private void CreateGhost(PointerEventData eventData)
        {
             Camera cam = dragCam != null ? dragCam : eventData.pressEventCamera;
             if (cam == null) cam = Camera.main;
             if (cam == null) return;

             ghostRenderer = null;

             bool fixedMode = false;
             try { fixedMode = (Panel != null && Panel.isFixedLocally); } catch { }

             if (fixedMode)
             {
                 ghostObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                 ghostObject.name = "DragGhost";
                 ghostObject.layer = 2;
                 Collider c = null;
                 try { c = ghostObject.GetComponent<Collider>(); } catch { }
                 try { if (c != null) Destroy(c); } catch { }

                 try
                 {
                     ghostRenderer = ghostObject.GetComponent<Renderer>();
                     if (ghostRenderer != null)
                     {
                         Material m = new Material(Shader.Find("Unlit/Transparent"));
                         if (ThumbnailImage != null) m.mainTexture = ThumbnailImage.texture;
                         m.color = new Color(1f, 1f, 1f, 0.9f);
                         ghostRenderer.material = m;
                     }
                 }
                 catch { }

                 try { ghostObject.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f); } catch { }
             }
             else
             {
                 ghostObject = new GameObject("DragGhost");
                 
                 Canvas rootCanvas = GetComponentInParent<Canvas>();
                 if (rootCanvas == null && Panel != null) rootCanvas = Panel.canvas;
                 
                 if (rootCanvas != null) 
                 {
                     ghostObject.transform.SetParent(rootCanvas.transform, false);
                     ghostObject.layer = rootCanvas.gameObject.layer;
                     ghostObject.transform.localScale = Vector3.one;
                 }
                 
                 ghostBorder = ghostObject.AddComponent<Image>();
                 ghostBorder.raycastTarget = false;
                 ghostBorder.color = new Color(1, 1, 1, 0.2f);
                 
                 GameObject textGO = new GameObject("ActionText");
                 textGO.transform.SetParent(ghostObject.transform, false);
                 ghostText = textGO.AddComponent<Text>();
                 ghostText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                 ghostText.fontSize = 24;
                 ghostText.color = Color.white;
                 ghostText.alignment = TextAnchor.UpperCenter;
                 ghostText.horizontalOverflow = HorizontalWrapMode.Overflow;
                 ghostText.verticalOverflow = VerticalWrapMode.Overflow;
                 
                 textGO.AddComponent<Outline>().effectColor = Color.black;

                 RectTransform textRT = textGO.GetComponent<RectTransform>();
                 textRT.anchorMin = new Vector2(0.5f, 0);
                 textRT.anchorMax = new Vector2(0.5f, 0);
                 textRT.pivot = new Vector2(0.5f, 1);
                 textRT.anchoredPosition = new Vector2(0, -10);
                 textRT.sizeDelta = new Vector2(400, 60);

                 GameObject contentGO = new GameObject("Content");
                 contentGO.transform.SetParent(ghostObject.transform, false);
                 contentGO.layer = ghostObject.layer;
                 RawImage img = contentGO.AddComponent<RawImage>();
                 img.raycastTarget = false;
                 img.color = new Color(1, 1, 1, 0.7f);
                 if (ThumbnailImage != null)
                 {
                     img.texture = ThumbnailImage.texture;
                 }
                 
                 RectTransform rt = ghostObject.GetComponent<RectTransform>();
                 if (rt == null) rt = ghostObject.AddComponent<RectTransform>();
                 rt.sizeDelta = new Vector2(80, 80); 
                 rt.pivot = new Vector2(0.5f, 0.5f);
                 
                 RectTransform contentRT = contentGO.GetComponent<RectTransform>();
                 if (contentRT == null) contentRT = contentGO.AddComponent<RectTransform>();
                 contentRT.anchorMin = Vector2.zero;
                 contentRT.anchorMax = Vector2.one;
                 contentRT.offsetMin = new Vector2(5, 5);
                 contentRT.offsetMax = new Vector2(-5, -5);
             }
             
             planeDistance = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
             
             UpdateGhost(eventData, null, planeDistance);
        }
        
        private void UpdateGhost(PointerEventData eventData, Atom atom, float distance)
        {
             Camera cam = dragCam != null ? dragCam : eventData.pressEventCamera;
             if (cam == null) cam = Camera.main;
             if (ghostObject == null || cam == null) return;
             
             bool isValidTarget = (atom != null && atom.type == "Person");

             if (HubItem != null)
             {
                 UpdateGhostPosition(eventData, false, distance);
                 if (ghostBorder != null) ghostBorder.color = new Color(1f, 0.5f, 0f, 0.4f); // Orange
                 if (ghostText != null)
                 {
                     ghostText.text = $"Release to download/view\n{HubItem.Title}";
                     ghostText.color = new Color(1f, 0.8f, 0.4f);
                 }
                 return;
             }

             ItemType itemType = GetItemType(FileEntry);
             bool isHair = (itemType == ItemType.Hair || itemType == ItemType.HairItem);
             bool isClothing = (itemType == ItemType.Clothing || itemType == ItemType.ClothingItem);
             bool isScene = itemType == ItemType.Scene;

             UpdateGhostPosition(eventData, isValidTarget, distance);

             if (itemType == ItemType.Appearance)
             {
                 HideGroundIndicator();
                 if (ghostBorder != null) ghostBorder.color = new Color(0f, 1f, 0f, 0.25f);
                 if (ghostRenderer != null) try { ghostRenderer.material.color = new Color(1f, 1f, 1f, 0.95f); } catch { }
                 if (ghostText != null)
                 {
                     ghostText.text = "Release for options";
                     ghostText.color = new Color(0.5f, 1f, 0.5f);
                 }
                 return;
             }
             else
             {
                 HideGroundIndicator();
             }
             
             if (isScene)
             {
                 if (ghostBorder != null) ghostBorder.color = new Color(0.4f, 0.8f, 1f, 0.4f);
                 if (ghostText != null)
                 {
                     ghostText.text = $"Release to launch scene\n{FileEntry.Name}";
                     ghostText.color = new Color(0.6f, 0.9f, 1f);
                 }
                 return;
             }
             
             if (isValidTarget)
             {
                 if (ghostBorder != null) ghostBorder.color = new Color(0, 1, 0, 0.4f);
                 
                 if (ghostText != null)
                 {
                     if (CheckDualPose())
                     {
                         bool isMale = IsAtomMale(atom);
                         string genderStr = isMale ? "Male" : "Female";
                         ghostText.text = $"Applying Dual Pose ({genderStr}) to\n{atom.name}";
                         ghostText.color = new Color(0.5f, 1f, 0.5f);
                         return;
                     }

                     HashSet<string> regions = isHair ? GetHairRegions(FileEntry) : GetClothingRegions(FileEntry);

                     string typeStr;
                     if (regions.Count > 0)
                     {
                         typeStr = string.Join("/", regions.Select(r => char.ToUpper(r[0]) + r.Substring(1)).ToArray());
                     }
                     else
                     {
                         if (isHair) typeStr = "Hair";
                         else if (isClothing) typeStr = "Clothing";
                         else if (itemType == ItemType.Pose) typeStr = "Pose";
                         else typeStr = "Item";
                     }

                     if (Panel != null && Panel.DragDropReplaceMode && (isClothing || isHair))
                     {
                         ghostText.text = $"Replacing {typeStr} on\n" + atom.name;
                         ghostText.color = new Color(1f, 0.5f, 0.5f); // Reddish
                     }
                     else
                     {
                         string action = (itemType == ItemType.Pose) ? "Applying" : "Adding";
                         ghostText.text = $"{action} {typeStr} to\n" + atom.name;
                         ghostText.color = new Color(0.5f, 1f, 0.5f); // Greenish
                     }
                 }
             }
             else
             {
                 if (ghostBorder != null) ghostBorder.color = new Color(1, 1, 1, 0.2f);
                 if (ghostText != null) ghostText.text = "";
             }
        }

        private void UpdateGroundIndicator(PointerEventData eventData)
        {
            hasGroundPoint = false;
            Camera cam = dragCam != null ? dragCam : eventData.pressEventCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null) { HideGroundIndicator(); return; }

            Ray ray = cam.ScreenPointToRay(eventData.position);
            Vector3 floorPoint;
            if (SpawnAtomElement.TryRaycastFloor(ray, out floorPoint))
            {
                lastGroundPoint = floorPoint;
                hasGroundPoint = true;
            }

            if (!hasGroundPoint) { HideGroundIndicator(); return; }

            if (groundIndicator == null) CreateGroundIndicator();
            if (groundIndicator == null) return;
            groundIndicator.SetActive(true);
            try
            {
                var r = groundIndicator.GetComponent<Renderer>();
                if (r != null) r.enabled = true;
            }
            catch { }
            groundIndicator.transform.position = lastGroundPoint + Vector3.up * 0.01f;
        }

        private void CreateGroundIndicator()
        {
            try
            {
                groundIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                groundIndicator.name = "VPB_DropIndicator";
                groundIndicator.layer = 2;

                Collider col = groundIndicator.GetComponent<Collider>();
                if (col != null) Destroy(col);

                groundIndicator.transform.localScale = new Vector3(0.35f, 0.005f, 0.35f);

                var r = groundIndicator.GetComponent<Renderer>();
                if (r != null)
                {
                    Material m = new Material(Shader.Find("Unlit/Color"));
                    m.color = new Color(0.2f, 1f, 0.2f, 0.65f);
                    r.material = m;
                    r.enabled = false;
                }

                try { groundIndicator.transform.position = new Vector3(0, -10000f, 0); } catch { }
                groundIndicator.SetActive(false);
            }
            catch
            {
                groundIndicator = null;
            }
        }

        private void HideGroundIndicator()
        {
            if (groundIndicator != null)
            {
                try
                {
                    var r = groundIndicator.GetComponent<Renderer>();
                    if (r != null) r.enabled = false;
                }
                catch { }
                groundIndicator.SetActive(false);
            }
        }

        private void DestroyGroundIndicator()
        {
            if (groundIndicator != null)
            {
                Destroy(groundIndicator);
                groundIndicator = null;
            }
        }
        
        private void UpdateGhostPosition(PointerEventData eventData, bool isValidTarget, float distance)
        {
             Camera cam = dragCam != null ? dragCam : eventData.pressEventCamera;
             if (cam == null) cam = Camera.main;
             if (cam == null) return;

             float finalDist = distance;
             if (isValidTarget)
             {
                 finalDist = distance * 0.5f;
             }
             else
             {
                 // In desktop, ensure it's at least 0.4m away so it doesn't fill the screen
                 bool isVr = UnityEngine.XR.XRSettings.enabled;
                 if (!isVr)
                 {
                     finalDist = Mathf.Max(distance, 0.4f);
                 }
             }

             Ray ray = cam.ScreenPointToRay(eventData.position);
             ghostObject.transform.position = ray.GetPoint(finalDist);
             ghostObject.transform.rotation = cam.transform.rotation;
        }

        private HashSet<string> GetHairRegions(FileEntry entry)
        {
            if (entry == null) return new HashSet<string>();
            string cacheKey = "hair:" + entry.Uid;
            if (_globalRegionCache.TryGetValue(cacheKey, out HashSet<string> cached)) return cached;

            HashSet<string> regions = new HashSet<string>();
            
            // 1. Try VarFileEntry pre-parsed tags
            if (entry is VarFileEntry vfe && vfe.HairTags != null && vfe.HairTags.Count > 0)
            {
                foreach (var t in vfe.HairTags)
                {
                    string lower = t.ToLowerInvariant();
                    if (TagFilter.HairRegionTags.Contains(lower))
                    {
                        regions.Add(lower);
                    }
                }
            }

            // 2. If no regions found yet, try reading file content (for loose files or missing cache)
            if (regions.Count == 0 && entry != null)
            {
                string ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (ext == ".vap" || ext == ".json" || ext == ".vam")
                {
                    try 
                    {
                        using (var reader = entry.OpenStreamReader())
                        {
                            string content = reader.ReadToEnd();
                            JSONNode node = JSON.Parse(content);
                            if (node != null && node["tags"] != null)
                            {
                                 string tagStr = node["tags"].Value;
                                 if (!string.IsNullOrEmpty(tagStr))
                                 {
                                     var tags = tagStr.Split(',').Select(t => t.Trim().ToLowerInvariant());
                                     foreach(var t in tags)
                                     {
                                         if (TagFilter.HairRegionTags.Contains(t))
                                         {
                                             regions.Add(t);
                                         }
                                     }
                                 }
                            }
                        }
                    }
                    catch(Exception ex) 
                    {
                        LogUtil.LogError("Error parsing tags from file " + entry.Path + ": " + ex.Message);
                    }
                }
            }

            // 3. If still no regions, try filename heuristics
            if (regions.Count == 0 && entry != null)
            {
                string name = Path.GetFileNameWithoutExtension(entry.Path);
                regions = GetRegionsFromHeuristics(name);
            }
            
            if (entry != null) _globalRegionCache[cacheKey] = regions;
            return regions;
        }

        private HashSet<string> GetClothingRegions(FileEntry entry)
        {
            if (entry == null) return new HashSet<string>();
            string cacheKey = "clothing:" + entry.Uid;
            if (_globalRegionCache.TryGetValue(cacheKey, out HashSet<string> cached)) return cached;
            
            HashSet<string> regions = new HashSet<string>();
            
            // 1. Try VarFileEntry pre-parsed tags
            if (entry is VarFileEntry vfe && vfe.ClothingTags != null && vfe.ClothingTags.Count > 0)
            {
                foreach (var t in vfe.ClothingTags)
                {
                    string lower = t.ToLowerInvariant();
                    if (TagFilter.ClothingRegionTags.Contains(lower))
                    {
                        regions.Add(lower);
                    }
                }
            }

            // 2. Try file content
            if (regions.Count == 0 && entry != null)
            {
                string ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (ext == ".vap" || ext == ".json" || ext == ".vam")
                {
                    try 
                    {
                        using (var reader = entry.OpenStreamReader())
                        {
                            string content = reader.ReadToEnd();
                            JSONNode node = JSON.Parse(content);
                            if (node != null && node["tags"] != null)
                            {
                                 string tagStr = node["tags"].Value;
                                 if (!string.IsNullOrEmpty(tagStr))
                                 {
                                     var tags = tagStr.Split(',').Select(t => t.Trim().ToLowerInvariant());
                                     foreach(var t in tags)
                                     {
                                         if (TagFilter.ClothingRegionTags.Contains(t))
                                         {
                                             regions.Add(t);
                                         }
                                     }
                                 }
                            }
                        }
                    }
                    catch (Exception) 
                    {
                         // ignore
                    }
                }
            }
            
            // 3. Heuristics
            if (regions.Count == 0 && entry != null)
            {
                string name = Path.GetFileNameWithoutExtension(entry.Path);
                regions = GetClothingRegionsFromHeuristics(name);
            }
            
            if (entry != null) _globalRegionCache[cacheKey] = regions;
            return regions;
        }

        private static HashSet<string> GetRegionsFromHeuristics(string name)
        {
            HashSet<string> regions = new HashSet<string>();
            if (string.IsNullOrEmpty(name)) return regions;
            
            name = name.ToLowerInvariant();
            
            if (name.Contains("genital") || name.Contains("pubic")) regions.Add("genital");
            
            if (name.Contains("beard") || name.Contains("mustache") || name.Contains("stubble") || name.Contains("face")) regions.Add("face");
            
            if (name.Contains("torso") || name.Contains("chest") || name.Contains("nipple") || name.Contains("stomach") || name.Contains("belly")) regions.Add("torso");
            
            if ((name.Contains("leg") && !name.Contains("legend") && !name.Contains("collection")) || name.Contains("stocking")) regions.Add("legs");
            
            if (name.Contains("arm") && !name.Contains("armour") && !name.Contains("warm")) regions.Add("arms");
            
            if (name.Contains("body") && !name.Contains("nobody")) regions.Add("full body"); 
            
            if (name.Contains("bang")) regions.Add("bangs");
            
            if (name.Contains("brow") || name.Contains("lash")) regions.Add("face");
            
            if (regions.Count == 0) regions.Add("head");

            return regions;
        }
        
        private static HashSet<string> GetClothingRegionsFromHeuristics(string name)
        {
             HashSet<string> regions = new HashSet<string>();
             if (string.IsNullOrEmpty(name)) return regions;
             
             name = name.ToLowerInvariant();
             
             if (name.Contains("top") || name.Contains("shirt") || name.Contains("bra") || name.Contains("jacket") || name.Contains("sweater")) regions.Add("torso");
             if (name.Contains("bottom") || name.Contains("pant") || name.Contains("skirt") || name.Contains("short") || name.Contains("underwear") || name.Contains("thong")) regions.Add("hip"); // usually Hip/Pelvis
             
             if (name.Contains("dress") || name.Contains("bodysuit") || name.Contains("suit")) 
             {
                 regions.Add("torso");
                 regions.Add("hip");
             }
             
             if (name.Contains("sock") || name.Contains("stocking") || name.Contains("shoe") || name.Contains("boot") || name.Contains("heel")) regions.Add("feet");
             if (name.Contains("glove")) regions.Add("hands");
             
             if (name.Contains("hat") || name.Contains("cap") || name.Contains("mask") || name.Contains("glasses")) regions.Add("head");
             
             return regions;
        }



        private void ApplyDualPose(Atom targetAtom, JSONNode dualPoseNode)
        {
            if (dualPoseNode == null) return;
            
            try
            {
                LogUtil.Log("[Gallery] Applying Dual Pose...");
                
                string p1Id = dualPoseNode["Person1"]?.Value;
                string p2Id = dualPoseNode["Person2"]?.Value;
                
                if (string.IsNullOrEmpty(p1Id) || string.IsNullOrEmpty(p2Id))
                {
                    LogUtil.LogError("[Gallery] Dual Pose missing Person1/Person2 fields.");
                    return;
                }
                
                JSONArray atomsArray = dualPoseNode["atoms"] as JSONArray;
                if (atomsArray == null) return;
                
                JSONClass p1AtomData = null;
                JSONClass p2AtomData = null;
                bool p1IsMale = false;
                bool p2IsMale = false;
                
                for(int i=0; i<atomsArray.Count; i++)
                {
                    JSONClass a = atomsArray[i] as JSONClass;
                    if (a == null) continue;
                    string aid = a["id"].Value;
                    
                    if (aid == p1Id) 
                    {
                         p1AtomData = a;
                         p1IsMale = CheckGenderInJSON(a);
                    }
                    else if (aid == p2Id)
                    {
                         p2AtomData = a;
                         p2IsMale = CheckGenderInJSON(a);
                    }
                }
                
                if (p1AtomData == null || p2AtomData == null)
                {
                     LogUtil.LogError("[Gallery] Could not find atom data for Person1 or Person2.");
                     return;
                }
                
                bool targetIsMale = IsAtomMale(targetAtom);
                
                JSONClass targetData = null;
                JSONClass partnerData = null;
                
                if (targetIsMale == p1IsMale) { targetData = p1AtomData; partnerData = p2AtomData; }
                else if (targetIsMale == p2IsMale) { targetData = p2AtomData; partnerData = p1AtomData; }
                else 
                {
                    if (targetIsMale)
                    {
                        if (p1IsMale) { targetData = p1AtomData; partnerData = p2AtomData; }
                        else if (p2IsMale) { targetData = p2AtomData; partnerData = p1AtomData; }
                    }
                    else
                    {
                        if (!p1IsMale) { targetData = p1AtomData; partnerData = p2AtomData; }
                        else if (!p2IsMale) { targetData = p2AtomData; partnerData = p1AtomData; }
                    }
                }
                
                if (targetData == null)
                {
                    targetData = p1AtomData;
                    partnerData = p2AtomData;
                }
                
                Atom partnerAtom = null;
                List<Atom> allAtoms = SuperController.singleton.GetAtoms();
                float closestDist = float.MaxValue;
                bool requiredPartnerMale = CheckGenderInJSON(partnerData);
                
                foreach(Atom a in allAtoms)
                {
                    if (a == targetAtom) continue;
                    if (a.type != "Person") continue;
                    
                    bool aIsMale = IsAtomMale(a);
                    if (aIsMale == requiredPartnerMale)
                    {
                        float d = Vector3.Distance(targetAtom.transform.position, a.transform.position);
                        if (d < closestDist)
                        {
                            closestDist = d;
                            partnerAtom = a;
                        }
                    }
                }
                
                if (partnerAtom == null)
                {
                    foreach(Atom a in allAtoms)
                    {
                        if (a == targetAtom) continue;
                        if (a.type != "Person") continue;
                        float d = Vector3.Distance(targetAtom.transform.position, a.transform.position);
                        if (d < closestDist)
                        {
                            closestDist = d;
                            partnerAtom = a;
                        }
                    }
                }
                
                if (targetAtom != null && targetData != null)
                {
                     LogUtil.Log($"[Gallery] Applying dual pose to target {targetAtom.name}");
                     ApplyPoseToAtom(targetAtom, targetData);
                }
                
                if (partnerAtom != null && partnerData != null)
                {
                     LogUtil.Log($"[Gallery] Applying dual pose to partner {partnerAtom.name}");
                     ApplyPoseToAtom(partnerAtom, partnerData);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[Gallery] Error applying dual pose: {ex.Message}");
            }
        }
        
        private bool CheckGenderInJSON(JSONClass atomData)
        {
             if (atomData == null) return false;
             JSONArray storables = atomData["storables"] as JSONArray;
             if (storables != null)
             {
                 for(int i=0; i<storables.Count; i++)
                 {
                     JSONClass s = storables[i] as JSONClass;
                     if (s != null && s["id"].Value == "geometry")
                     {
                         string c = s["character"]?.Value;
                         if (!string.IsNullOrEmpty(c) && c.StartsWith("Male", StringComparison.OrdinalIgnoreCase)) return true;
                     }
                 }
             }
             return false;
        }

        private void ApplyPoseToAtom(Atom atom, JSONClass data)
        {
             bool suppressRoot = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
             
             if (suppressRoot)
             {
                 JSONArray targetStorables = data["storables"] as JSONArray;
                 if (targetStorables != null)
                 {
                      for(int k=0; k<targetStorables.Count; k++)
                      {
                           JSONClass s = targetStorables[k] as JSONClass;
                           if (s != null && s["id"].Value == "control")
                           {
                                if (s.HasKey("position")) s.Remove("position");
                                if (s.HasKey("rotation")) s.Remove("rotation");
                                break;
                           }
                      }
                 }
             }

             atom.PreRestore(true, false);
             if (!suppressRoot)
             {
                 atom.RestoreTransform(data);
             }
             atom.Restore(data, true, false, false);
             atom.LateRestore(data, true, false, false);
             atom.PostRestore(true, false);
        }

        private void DestroyGhost()
        {
            if (ghostObject != null)
            {
                Destroy(ghostObject);
                ghostObject = null;
                ghostBorder = null;
                ghostRenderer = null;
            }
        }

        private bool IsAmbiguousDrop(Atom atom, FileEntry entry)
        {
            if (entry == null) return false;
            ItemType type = GetItemType(entry);
            
            if (type == ItemType.Scene) return true;
            if (type == ItemType.Appearance) return true;
            
            return false;
        }

        private void HandleDropWithContext(Atom atom, FileEntry entry, Vector3 position)
        {
            List<ContextMenuPanel.Option> options = new List<ContextMenuPanel.Option>();
            ItemType type = GetItemType(entry);

            if (type == ItemType.Scene)
            {
                 options.Add(new ContextMenuPanel.Option("Load Scene", () => LoadSceneFile(entry.Uid)));
                 options.Add(new ContextMenuPanel.Option("Merge Scene", () => MergeSceneFile(entry.Uid, false)));

                 if (atom != null && atom.type == "Person")
                 {
                     options.Add(new ContextMenuPanel.Option("Import From Scene", () => {
                         ShowImportCategories(entry, atom);
                     }, false, true));
                 } 
            }
            else if (type == ItemType.Appearance)
            {
                options.Add(new ContextMenuPanel.Option("Spawn Person Appearance", () => {
                    try { if (ContextMenuPanel.Instance != null) ContextMenuPanel.Instance.Hide(); } catch { }
                    Vector3 pos = position;
                    StartCoroutine(CreatePersonAndApplyAppearance(entry, pos, "replace"));
                }));
            }
            
            if (options.Count > 0)
            {
                string title = entry != null ? entry.Name : "Menu";
                if (entry is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Creator))
                {
                    title += "\n<color=#aaaaaa><size=18>by " + vfe.Package.Creator + "</size></color>";
                }
                ContextMenuPanel.Instance.Show(position, options, title);
            }
            else
            {
                if (type == ItemType.Scene) LoadSceneFile(entry.Uid);
                else if (atom != null) ApplyClothingToAtom(atom, entry.Uid);
            }
        }

        private IEnumerator CreatePersonAndApplyAppearance(FileEntry entry, Vector3 position, string clothingMode)
        {
            if (entry == null) yield break;

            Atom spawned = null;
            SpawnAtomElement.SpawnSuppressionHandle suppression = null;
            yield return SpawnAtomElement.SpawnPersonAtFloorSuppressed(position, (a, h) => { spawned = a; suppression = h; });

            if (spawned == null) yield break;

            FileEntry prevEntry = FileEntry;
            try { FileEntry = entry; } catch { }

            try
            {
                ApplyClothingToAtom(spawned, entry.Uid, clothingMode);
            }
            catch { }

            try
            {
                ApplyPoseFromPresetPath(spawned, entry.Uid, true);
            }
            catch { }

            try { FileEntry = prevEntry; } catch { }

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            try
            {
                SceneLoadingUtils.SchedulePostPersonApplyFixup(spawned);
            }
            catch { }

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            try
            {
                if (suppression != null) suppression.Restore();
            }
            catch { }

            try
            {
                if (Panel != null && spawned != null)
                {
                    string spawnedUid = null;
                    try { spawnedUid = spawned.uid; } catch { }
                    if (!string.IsNullOrEmpty(spawnedUid))
                    {
                        GalleryPanel panelRef = Panel;
                        Panel.PushUndo(() =>
                        {
                            try
                            {
                                if (SuperController.singleton == null) return;
                                Atom a = null;
                                try { a = SuperController.singleton.GetAtomByUid(spawnedUid); } catch { a = null; }
                                if (a != null) SuperController.singleton.RemoveAtom(a);
                            }
                            catch { }

                            try
                            {
                                if (panelRef != null) panelRef.RefreshTargetDropdown();
                            }
                            catch { }
                        });
                    }
                }
            }
            catch { }

            try
            {
                if (Panel != null) Panel.RefreshTargetDropdown();
            }
            catch { }
        }

        private void ApplyPoseFromPresetPath(Atom target, string path, bool suppressRoot)
        {
            if (target == null) return;
            if (string.IsNullOrEmpty(path)) return;

            string normalizedPath = UI.NormalizePath(path);
            JSONNode node = null;
            try { node = SuperController.singleton.LoadJSON(normalizedPath); } catch { node = null; }
            if (node == null) return;

            JSONClass presetJSON = null;
            try { presetJSON = node.AsObject; } catch { presetJSON = null; }
            if (presetJSON == null) return;

            try
            {
                if (presetJSON["atoms"] != null)
                {
                    JSONClass extracted = ExtractAtomFromScene(presetJSON, "Person");
                    if (extracted != null) presetJSON = extracted;
                }
            }
            catch { }

            if (suppressRoot)
            {
                try
                {
                    if (presetJSON["storables"] != null)
                    {
                        JSONArray storables = presetJSON["storables"] as JSONArray;
                        if (storables != null)
                        {
                            for (int i = 0; i < storables.Count; i++)
                            {
                                JSONClass s = storables[i] as JSONClass;
                                if (s == null) continue;
                                if (s["id"].Value == "control")
                                {
                                    if (s.HasKey("position")) s.Remove("position");
                                    if (s.HasKey("rotation")) s.Remove("rotation");
                                }

                                if (s["id"].Value == "PosePresets" || s["id"].Value == "control")
                                {
                                    if (s["presets"] != null) CleanPresets(s["presets"] as JSONArray);
                                }
                            }
                        }
                    }
                    else if (presetJSON["presets"] != null)
                    {
                        CleanPresets(presetJSON["presets"] as JSONArray);
                    }
                }
                catch { }
            }

            JSONStorable presetStorable = null;
            try { presetStorable = target.GetStorableByID("PosePresets"); } catch { presetStorable = null; }
            if (presetStorable == null) return;

            MeshVR.PresetManager pm = null;
            try { pm = presetStorable.GetComponentInChildren<MeshVR.PresetManager>(); } catch { pm = null; }
            if (pm == null) return;

            try
            {
                MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath);
                pm.LoadPresetFromJSON(presetJSON, false);
            }
            finally
            {
                try { MVR.FileManagement.FileManager.PopLoadDir(); } catch { }
            }
        }

        private void ShowImportCategories(FileEntry entry, Atom targetAtom)
        {
            List<ContextMenuPanel.Option> options = new List<ContextMenuPanel.Option>();

            options.Add(new ContextMenuPanel.Option("Clothing", () => {
                ShowSourceAtomsForImport(entry, targetAtom, "Clothing", "merge");
            }, false, true));

            options.Add(new ContextMenuPanel.Option("Appearance", () => {
                ShowAppearanceClothingModes(entry, targetAtom);
            }, false, true));

            ContextMenuPanel.Instance.PushPage("Import Category", options);
        }

        private string GetAppearanceModeLabel(string clothingMode)
        {
            if (clothingMode == "replace") return "Replace Clothing";
            if (clothingMode == "merge") return "Merge Clothing";
            return "Keep Existing Clothing";
        }

        private void ApplyAppearanceMode(string clothingMode, Action<string> action)
        {
            if (string.IsNullOrEmpty(clothingMode)) clothingMode = "keep";
            _lastAppearanceClothingMode = clothingMode;
            action(clothingMode);
        }

        private void AddAppearanceOptions(List<ContextMenuPanel.Option> options, Action<string> handler)
        {
            string mode = string.IsNullOrEmpty(_lastAppearanceClothingMode) ? "keep" : _lastAppearanceClothingMode;
            string lastLabel = "Last Used (" + GetAppearanceModeLabel(mode) + ")";

            options.Add(new ContextMenuPanel.Option(lastLabel, () => {
                ApplyAppearanceMode(mode, handler);
            }));

            options.Add(new ContextMenuPanel.Option("Keep Existing Clothing", () => {
                ApplyAppearanceMode("keep", handler);
            }));

            options.Add(new ContextMenuPanel.Option("Replace Clothing", () => {
                ApplyAppearanceMode("replace", handler);
            }));

            options.Add(new ContextMenuPanel.Option("Merge Clothing", () => {
                ApplyAppearanceMode("merge", handler);
            }));
        }

        private void ShowAppearanceClothingModes(FileEntry entry, Atom targetAtom)
        {
            List<ContextMenuPanel.Option> options = new List<ContextMenuPanel.Option>();
            AddAppearanceOptions(options, mode => ShowSourceAtomsForImport(entry, targetAtom, "Appearance", mode));
            ContextMenuPanel.Instance.PushPage("Appearance Options", options);
        }

        private void ShowSourceAtomsForImport(FileEntry entry, Atom targetAtom, string category, string clothingMode)
        {
            if (ContextMenuPanel.Instance != null)
            {
                ContextMenuPanel.Instance.StartCoroutine(ParseSceneAndShowAtoms(entry, targetAtom, category, clothingMode));
            }
        }

        private System.Collections.IEnumerator ParseSceneAndShowAtoms(FileEntry entry, Atom targetAtom, string category, string clothingMode)
        {
            string content = null;
            using (var reader = entry.OpenStreamReader())
            {
                content = reader.ReadToEnd();
            }

            if (string.IsNullOrEmpty(content)) yield break;

            JSONNode root = JSON.Parse(content);
            if (root == null) yield break;

            JSONArray atoms = root["atoms"].AsArray;
            List<ContextMenuPanel.Option> atomOptions = new List<ContextMenuPanel.Option>();
            List<JSONClass> personNodes = new List<JSONClass>();

            foreach (JSONNode node in atoms)
            {
                if (node["type"].Value == "Person")
                {
                    personNodes.Add(node.AsObject);
                    string atomId = node["id"].Value;

                    atomOptions.Add(new ContextMenuPanel.Option(atomId, () => {
                        if (targetAtom == null)
                        {
                            LogUtil.LogError("No target atom selected for import.");
                        }
                        else
                        {
                            ApplyImport(node.AsObject, targetAtom, category, clothingMode, entry.Path);
                        }
                    }));
                }
            }

            if (personNodes.Count == 1 && targetAtom != null)
            {
                ApplyImport(personNodes[0], targetAtom, category, clothingMode, entry.Path);
                yield break;
            }

            ContextMenuPanel.Instance.PushPage("Select Source Person", atomOptions);
        }

        private void ApplyImport(JSONClass sourceAtomNode, Atom targetAtom, string category, string clothingMode, string path = null)
        {
            JSONClass preset = new JSONClass();
            JSONArray storables = new JSONArray();
            preset["storables"] = storables;

            JSONArray sourceStorables = sourceAtomNode["storables"].AsArray;

            foreach (JSONNode snode in sourceStorables)
            {
                string id = snode["id"].Value;
                string url = snode["url"] != null ? snode["url"].Value : "";

                bool include = false;

                bool isAnimation = id.EndsWith("Animation", StringComparison.OrdinalIgnoreCase) && snode["steps"] != null;
                bool isPlugin = id.IndexOf("plugin#", StringComparison.OrdinalIgnoreCase) >= 0 || id.Equals("PluginManager", StringComparison.OrdinalIgnoreCase);
                bool isClothing = id.StartsWith("clothing", StringComparison.OrdinalIgnoreCase) || id.StartsWith("wearable", StringComparison.OrdinalIgnoreCase) || url.IndexOf("/Clothing/", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHair = id.StartsWith("hair", StringComparison.OrdinalIgnoreCase) || url.IndexOf("/Hair/", StringComparison.OrdinalIgnoreCase) >= 0;

                if (category == "Clothing")
                {
                    if (isClothing || isHair) include = true;
                }
                else if (category == "Appearance")
                {
                    if (!isAnimation && !isPlugin)
                    {
                        if (clothingMode == "keep")
                        {
                            if (!isClothing && !isHair) include = true;
                        }
                        else
                        {
                            include = true;
                        }
                    }
                }

                if (include) storables.Add(snode.AsObject);
            }

            string presetJson = preset.ToString();
            if (FileButton.EnsureInstalledByText(presetJson))
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            bool appliedViaPresetManager = false;

            if (category == "Appearance" && storables.Count > 0)
            {
                preset["setUnlistedParamsToDefault"].AsBool = true;

                JSONStorable presetStorable = targetAtom.GetStorableByID("AppearancePresets");
                MeshVR.PresetManager presetManager = presetStorable != null ? presetStorable.GetComponentInChildren<MeshVR.PresetManager>() : null;

                if (presetManager != null)
                {
                    try
                    {
                        if (clothingMode == "replace")
                        {
                            JSONStorable geometry = targetAtom.GetStorableByID("geometry");
                            if (geometry != null)
                            {
                                foreach (var name in geometry.GetBoolParamNames())
                                {
                                    if (name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase) || name.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        JSONStorableBool p = geometry.GetBoolJSONParam(name);
                                        if (p != null) p.val = false;
                                    }
                                }
                            }
                        }

                        targetAtom.SetLastRestoredData(preset, true, true);
                        LogUtil.Log($"[Import] Applying Appearance preset via PresetManager.LoadPresetFromJSON ({storables.Count} storables)");
                        
                        try
                        {
                            if (!string.IsNullOrEmpty(path)) MVR.FileManagement.FileManager.PushLoadDirFromFilePath(UI.NormalizePath(path));
                            presetManager.LoadPresetFromJSON(preset, false);
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(path)) MVR.FileManagement.FileManager.PopLoadDir();
                        }

                        appliedViaPresetManager = true;
                        LogUtil.Log("[Import] Appearance preset application successful.");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[Import] Appearance preset load failed: " + ex.Message);
                    }
                }
                else
                {
                    LogUtil.LogWarning("[Import] AppearancePresets storable or PresetManager missing on target atom. Falling back to direct storable restoration.");
                }
            }
            
            if (!appliedViaPresetManager)
            {
                LogUtil.Log($"[Import] Restoring {storables.Count} storables directly to atom {targetAtom.name}");
                int directApplied = 0;
                int directSkipped = 0;
                
                if (category == "Appearance" && clothingMode == "replace")
                {
                    JSONStorable geometry = targetAtom.GetStorableByID("geometry");
                    if (geometry != null)
                    {
                        foreach (var name in geometry.GetBoolParamNames())
                        {
                            if (name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase) || name.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                            {
                                JSONStorableBool p = geometry.GetBoolJSONParam(name);
                                if (p != null) p.val = false;
                            }
                        }
                    }
                }

                foreach (JSONNode snode in storables)
                {
                    string id = snode["id"].Value;
                    JSONStorable storable = targetAtom.GetStorableByID(id);
                    if (storable != null)
                    {
                        storable.RestoreFromJSON(snode.AsObject);
                        directApplied++;
                    }
                    else
                    {
                        directSkipped++;
                    }
                }
                LogUtil.Log($"[Import] Direct restoration complete: {directApplied} applied, {directSkipped} skipped.");
            }

            ContextMenuPanel.Instance.Hide();
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
        public const int middleCenter = 16;

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
                case middleCenter: return new Vector2(0.5f, 0.5f);
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
                case middleCenter: return new Vector2(0.5f, 0.5f);
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
                case middleCenter: return new Vector2(0.5f, 0.5f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }
    }

    public class ChamferedRect : Image
    {
        public float chamferSize = 20f;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if (sprite != null) { base.OnPopulateMesh(vh); return; }
            vh.Clear();
            Rect r = rectTransform.rect;
            float cX = Mathf.Min(chamferSize, r.width);
            float cY = Mathf.Min(chamferSize, r.height * 0.5f);
            UIVertex v = UIVertex.simpleVert;
            v.color = color;
            v.uv0 = Vector2.zero;

            v.position = new Vector3(r.xMin, r.yMin + cY); vh.AddVert(v);
            v.position = new Vector3(r.xMin + cX, r.yMin); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMin); vh.AddVert(v);
            v.position = new Vector3(r.xMax, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMin + cX, r.yMax); vh.AddVert(v);
            v.position = new Vector3(r.xMin, r.yMax - cY); vh.AddVert(v);

            vh.AddTriangle(1, 2, 3);
            vh.AddTriangle(1, 3, 4);
            vh.AddTriangle(0, 1, 4);
            vh.AddTriangle(0, 4, 5);
        }
    }

    public static class UI
    {
        private static float _lastLoadSceneStartTime = -9999f;

        public static bool EnsureInstalled(FileEntry entry)
        {
            if (entry == null) return false;
            try
            {
                return SceneLoadingUtils.EnsureInstalled(entry);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] EnsureInstalled error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public static void LoadSceneFile(FileEntry entry)
        {
            if (entry == null) return;

            // Guard against duplicate triggers in the same click/frame burst.
            // This can happen via UI event duplication and causes visible "default Person" flashes.
            float now = Time.unscaledTime;
            if (now - _lastLoadSceneStartTime < 0.75f)
            {
                LogUtil.LogWarning("[VPB] UI.LoadSceneFile ignored (throttled)");
                return;
            }
            _lastLoadSceneStartTime = now;

            try
            {
                string path = entry.Uid;
                LogUtil.Log($"[VPB] UI.LoadSceneFile started for: {path}");
                
                bool installed = EnsureInstalled(entry);
                LogUtil.Log($"[VPB] UI.EnsureInstalled (with dependency scan) depsChanged: {installed}");
                if (!installed)
                {
                    LogUtil.Log("[VPB] UI.EnsureInstalled: depsChanged=false means no packages were moved; missing deps (if any) are logged above by EnsureInstalled.");
                }

                if (installed)
                {
                    LogUtil.Log("[VPB] Refreshing FileManagers...");
                    if (MVR.FileManagement.FileManager.singleton != null)
                        MVR.FileManagement.FileManager.Refresh();
                    
                    FileManager.Refresh();
                }

                string normalizedPath = UI.NormalizePath(path);
                try
                {
                    if (SceneLoadingUtils.TryPrepareLocalSceneForLoad(entry, out string rewritten))
                    {
                        normalizedPath = UI.NormalizePath(rewritten);
                        LogUtil.Log($"[VPB] Using rewritten scene: {normalizedPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning($"[VPB] Scene rewrite skipped due to error: {ex.Message}");
                }

                LogUtil.Log($"[VPB] Normalized path: {normalizedPath}");
                
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    LogUtil.Log($"[VPB] Calling sc.Load({normalizedPath})");
                    sc.Load(normalizedPath);
                }
                else
                {
                    LogUtil.LogError("[VPB] SuperController.singleton is null!");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] UI.LoadSceneFile crash: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            try
            {
                // FileManager.NormalizePath is more reliable in this codebase
                return FileManager.NormalizePath(path);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] FileManager.NormalizePath error: {ex.Message}");
            }
                
            string normalizedPath = path.Replace('\\', '/');
            try
            {
                string currentDir = Directory.GetCurrentDirectory().Replace('\\', '/');
                
                if (normalizedPath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedPath = normalizedPath.Substring(currentDir.Length);
                    if (normalizedPath.StartsWith("/")) normalizedPath = normalizedPath.Substring(1);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[VPB] UI.NormalizePath fallback error: {ex.Message}");
            }
            return normalizedPath;
        }

        public static JSONNode LoadJSONWithFallback(string path, FileEntry entry = null)
        {
            // Use SuperController.singleton.LoadJSON which is most reliable for VARs and various paths
            JSONNode root = SuperController.singleton.LoadJSON(path);
            
            if (root == null)
            {
                LogUtil.LogWarning($"[VPB] SuperController.singleton.LoadJSON returned null for {path}, trying manual read...");
                string content = null;
                
                // If we have an entry, it's the best way to read (handles VAR streams)
                if (entry != null && (entry.Uid == path || entry.Path == path))
                {
                    using (var reader = entry.OpenStreamReader()) content = reader.ReadToEnd();
                }
                else 
                {
                    // If no entry, try to find it in the file manager or loose file
                    string normalized = UI.NormalizePath(path);
                    if (normalized.Contains(":")) // Likely a VAR path like Creator.Package:/path
                    {
                        // We don't have a direct package reader here without entry, 
                        // but sometimes entry is provided. 
                    }
                    else if (File.Exists(path))
                    {
                        content = File.ReadAllText(path);
                    }
                }
                
                if (!string.IsNullOrEmpty(content))
                {
                    // Fix SELF:/ paths if we are extracting from a VAR package
                    if (entry is VarFileEntry varEntry && varEntry.Package != null)
                    {
                        string packageUid = varEntry.Package.Uid;
                        content = content.Replace("SELF:/", packageUid + ":/");
                        content = content.Replace("SELF:\\", packageUid + ":/");
                    }
                    root = JSON.Parse(content);
                }
            }
            return root;
        }

        public static GameObject CreateVScrollableContent(GameObject parentGO, Color backgroundColor, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float scrollBarWidth = 15f, float spacing = 0f)
        {
            GameObject scrollableContentGO = AddChildGOImage(parentGO, backgroundColor, anchorPreset, horizontalSize, verticalSize, anchoredPositionOffset);

            GameObject viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollableContentGO.transform, false);
            RectTransform viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.sizeDelta = new Vector2(-scrollBarWidth, 0);
            viewportRT.anchoredPosition = new Vector2(-scrollBarWidth / 2 - 5, 0); // Shift left slightly to avoid clip
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
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

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

        public static GameObject AddChildGOChamferedImage(GameObject parentGO, Color color, int anchorPreset, float horizontalSize, float verticalSize, Vector2 anchoredPositionOffset, float chamferSize = 20f)
        {
            GameObject go = new GameObject("ChamferedImage");
            go.transform.SetParent(parentGO.transform, false);
            ChamferedRect img = go.AddComponent<ChamferedRect>();
            img.color = color;
            img.chamferSize = chamferSize;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = AnchorPresets.GetAnchorMin(anchorPreset);
            rt.anchorMax = AnchorPresets.GetAnchorMax(anchorPreset);
            rt.pivot = AnchorPresets.GetPivot(anchorPreset);
            rt.anchoredPosition = anchoredPositionOffset;
            rt.sizeDelta = new Vector2(horizontalSize, verticalSize);

            return go;
        }

        public static Mesh GenerateCurvedMesh(RectTransform targetRT, RectTransform canvasRT, float radiusBase = 2.0f, int segments = 50)
        {
            if (targetRT == null || canvasRT == null || VPBConfig.Instance == null) return null;

            float intensity = VPBConfig.Instance.CurvatureIntensity;
            float radius = radiusBase * (1.0f / (intensity > 0 ? intensity : 0.001f));

            Mesh mesh = new Mesh();
            mesh.name = "CurvedUIMesh";

            Rect rect = targetRT.rect;
            float scaleX = canvasRT.lossyScale.x;
            if (scaleX == 0) scaleX = 0.001f;

            Matrix4x4 localToCanvas = canvasRT.worldToLocalMatrix * targetRT.localToWorldMatrix;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float x = Mathf.Lerp(rect.xMin, rect.xMax, t);

                for (int j = 0; j <= 1; j++)
                {
                    float y = (j == 0) ? rect.yMin : rect.yMax;
                    Vector3 pos = new Vector3(x, y, 0);

                    // To Canvas Local Space
                    Vector3 cPos = localToCanvas.MultiplyPoint3x4(pos);

                    // Apply Cylinder Bend
                    float worldX = cPos.x * scaleX;
                    float angle = worldX / radius;

                    float newWorldX = Mathf.Sin(angle) * radius;
                    float newWorldZ = (Mathf.Cos(angle) - 1.0f) * radius;

                    cPos.x = newWorldX / scaleX;
                    cPos.z = newWorldZ / scaleX;

                    // Back to Target Local Space
                    vertices.Add(targetRT.worldToLocalMatrix.MultiplyPoint3x4(canvasRT.localToWorldMatrix.MultiplyPoint3x4(cPos)));
                    uvs.Add(new Vector2(t, j));
                }
            }

            for (int i = 0; i < segments; i++)
            {
                int baseIdx = i * 2;
                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 3);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 3);
                triangles.Add(baseIdx + 2);
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public static GameObject CreateUIButton(GameObject parentGO, float width, float height, string label, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction onClick)
        {
            GameObject buttonGO = AddChildGOImage(parentGO, new Color(0.2f, 0.2f, 0.2f, 1f), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            buttonGO.name = "Button_" + label;
            Button btn = buttonGO.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            // Configure button colors to ensure dark background by default (avoiding white boxes)
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f); // Slightly brighter on hover
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f); // Darker and more transparent when disabled
            btn.colors = cb;
            btn.transition = Selectable.Transition.None;
            btn.navigation = new Navigation { mode = Navigation.Mode.None };
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(buttonGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;
            
            // Add Hover Border
            buttonGO.AddComponent<UIHoverBorder>();

            return buttonGO;
        }

        public static GameObject CreateUIToggle(GameObject parentGO, float width, float height, string label, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = AddChildGOImage(parentGO, new Color(0, 0, 0, 0), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            toggleGO.name = "Toggle_" + label;
            Toggle toggle = toggleGO.AddComponent<Toggle>();

            // Outer Box (Border - White)
            GameObject boxGO = new GameObject("Box");
            boxGO.transform.SetParent(toggleGO.transform, false);
            RectTransform boxRT = boxGO.AddComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0, 0.5f);
            boxRT.anchorMax = new Vector2(0, 0.5f);
            boxRT.pivot = new Vector2(0, 0.5f);
            boxRT.anchoredPosition = new Vector2(10, 0);
            boxRT.sizeDelta = new Vector2(20, 20);
            Image boxImg = boxGO.AddComponent<Image>();
            boxImg.color = Color.white;
            toggle.targetGraphic = boxImg;

            // Inner Box (Background - Black)
            GameObject innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(boxGO.transform, false);
            RectTransform innerRT = innerGO.AddComponent<RectTransform>();
            innerRT.anchorMin = new Vector2(0.5f, 0.5f);
            innerRT.anchorMax = new Vector2(0.5f, 0.5f);
            innerRT.pivot = new Vector2(0.5f, 0.5f);
            innerRT.sizeDelta = new Vector2(16, 16);
            Image innerImg = innerGO.AddComponent<Image>();
            innerImg.color = Color.black;

            // Checkmark (Fill - White)
            GameObject checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(innerGO.transform, false); 
            RectTransform checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.5f, 0.5f);
            checkRT.anchorMax = new Vector2(0.5f, 0.5f);
            checkRT.pivot = new Vector2(0.5f, 0.5f);
            checkRT.sizeDelta = new Vector2(14, 14); 
            Image checkImg = checkGO.AddComponent<Image>();
            checkImg.color = Color.white;
            toggle.graphic = checkImg;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(toggleGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = new Vector2(35, 0);
            labelRT.offsetMax = new Vector2(0, 0);
            Text t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;

            toggle.onValueChanged.AddListener(onValueChanged);
            return toggleGO;
        }

        public static GameObject CreateToggle(GameObject parentGO, string label, float width, float height, float xOffset, float yOffset, int anchorPreset, UnityAction<bool> onValueChanged)
        {
            GameObject toggleGO = AddChildGOImage(parentGO, new Color(0, 0, 0, 0), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            toggleGO.name = "Toggle_" + label;
            Toggle toggle = toggleGO.AddComponent<Toggle>();

            // Outer Box (Border - White)
            GameObject boxGO = new GameObject("Box");
            boxGO.transform.SetParent(toggleGO.transform, false);
            RectTransform boxRT = boxGO.AddComponent<RectTransform>();
            boxRT.anchorMin = new Vector2(0, 0.5f);
            boxRT.anchorMax = new Vector2(0, 0.5f);
            boxRT.pivot = new Vector2(0, 0.5f);
            boxRT.anchoredPosition = new Vector2(10, 0);
            boxRT.sizeDelta = new Vector2(20, 20);
            Image boxImg = boxGO.AddComponent<Image>();
            boxImg.color = Color.white;
            toggle.targetGraphic = boxImg;

            // Inner Box (Background - Black)
            GameObject innerGO = new GameObject("Inner");
            innerGO.transform.SetParent(boxGO.transform, false);
            RectTransform innerRT = innerGO.AddComponent<RectTransform>();
            innerRT.anchorMin = new Vector2(0.5f, 0.5f);
            innerRT.anchorMax = new Vector2(0.5f, 0.5f);
            innerRT.pivot = new Vector2(0.5f, 0.5f);
            innerRT.sizeDelta = new Vector2(16, 16);
            Image innerImg = innerGO.AddComponent<Image>();
            innerImg.color = Color.black;

            // Checkmark (Fill - White)
            GameObject checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(innerGO.transform, false); // Parent to inner or box, doesn't matter much if positioned correctly
            RectTransform checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = new Vector2(0.5f, 0.5f);
            checkRT.anchorMax = new Vector2(0.5f, 0.5f);
            checkRT.pivot = new Vector2(0.5f, 0.5f);
            checkRT.sizeDelta = new Vector2(14, 14); // Slightly smaller to leave a hint of border or full size? Let's use 14 to leave black gap, or 16 for solid. User said "white is selected". Solid white looks best.
            // Actually if I make it 16, it covers the black inner completely, merging with white outer.
            checkRT.sizeDelta = new Vector2(16, 16); 
            Image checkImg = checkGO.AddComponent<Image>();
            checkImg.color = Color.white;
            toggle.graphic = checkImg;

            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(toggleGO.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0);
            labelRT.anchorMax = new Vector2(1, 1);
            labelRT.offsetMin = new Vector2(35, 0);
            labelRT.offsetMax = new Vector2(0, 0);
            Text t = labelGO.AddComponent<Text>();
            t.text = label;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 16;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;

            toggle.onValueChanged.AddListener(onValueChanged);
            return toggleGO;
        }

        public static GameObject CreateSlider(GameObject parentGO, string label, float width, float height, float min, float max, float currentVal, UnityAction<float> onValueChanged)
        {
            GameObject container = AddChildGOImage(parentGO, new Color(0,0,0,0), AnchorPresets.middleCenter, width, height, Vector2.zero);
            
            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(container.transform, false);
            RectTransform labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0, 0.5f);
            labelRT.anchorMax = new Vector2(0.5f, 1f);
            labelRT.offsetMin = new Vector2(5, 0);
            Text t = labelGO.AddComponent<Text>();
            t.text = label + ": " + currentVal.ToString("F2");
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = 14;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;

            // Slider
            GameObject sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(container.transform, false);
            RectTransform sliderRT = sliderGO.AddComponent<RectTransform>();
            sliderRT.anchorMin = new Vector2(0.5f, 0.1f);
            sliderRT.anchorMax = new Vector2(0.95f, 0.9f);
            
            Slider slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = currentVal;
            
            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderGO.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.2f);
            RectTransform bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 0.25f);
            bgRT.anchorMax = new Vector2(1, 0.75f);
            
            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGO.transform, false);
            RectTransform fillAreaRT = fillArea.AddComponent<RectTransform>();
            fillAreaRT.anchorMin = new Vector2(0, 0.25f);
            fillAreaRT.anchorMax = new Vector2(1, 0.75f);
            
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.25f, 0.5f, 0.8f);
            RectTransform fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            slider.fillRect = fillRT;
            
            // Handle
            GameObject handleArea = new GameObject("Handle Area");
            handleArea.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRT = handleArea.AddComponent<RectTransform>();
            handleAreaRT.anchorMin = Vector2.zero;
            handleAreaRT.anchorMax = Vector2.one;
            
            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            RectTransform handleRT = handle.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(20, 0);
            slider.handleRect = handleRT;
            slider.targetGraphic = handleImg;

            slider.onValueChanged.AddListener((val) => {
                t.text = label + ": " + val.ToString("F2");
                onValueChanged(val);
            });
            
            return container;
        }

        public static GameObject CreateDropdown(GameObject parentGO, string label, float width, float height, List<string> options, int currentIdx, UnityAction<int> onValueChanged)
        {
            GameObject container = AddChildGOImage(parentGO, new Color(0,0,0,0), AnchorPresets.middleCenter, width, height, Vector2.zero);
            
            GameObject btnGO = CreateUIButton(container, width, height, label + ": " + (options.Count > currentIdx ? options[currentIdx] : ""), 14, 0, 0, AnchorPresets.middleCenter, null);
            Button btn = btnGO.GetComponent<Button>();
            Text t = btnGO.GetComponentInChildren<Text>();
            
            // Use a local variable to capture index if possible, but UnityAction works with captured vars
            // We need a wrapper class to hold state if we want it to persist, but for now closure is fine
            int idx = currentIdx;
            
            btn.onClick.AddListener(() => {
                idx = (idx + 1) % options.Count;
                t.text = label + ": " + options[idx];
                onValueChanged(idx);
            });
            
            return container;
        }

        public static GameObject CreateTextInput(GameObject parentGO, float width, float height, string defaultText, int fontSize, float xOffset, float yOffset, int anchorPreset, UnityAction<string> onEndEdit)
        {
            GameObject inputGO = AddChildGOImage(parentGO, new Color(0.1f, 0.1f, 0.1f, 1f), anchorPreset, width, height, new Vector2(xOffset, yOffset));
            inputGO.name = "TextInput";
            
            InputField inputField = inputGO.AddComponent<InputField>();
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(inputGO.transform, false);
            Text t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleLeft;
            t.supportRichText = false;
            
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = new Vector2(-10, -10);
            textRT.anchoredPosition = new Vector2(5, 0);
            
            inputField.textComponent = t;
            
            GameObject placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(inputGO.transform, false);
            Text p = placeholderGO.AddComponent<Text>();
            p.text = defaultText;
            p.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            p.fontSize = fontSize;
            p.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            p.alignment = TextAnchor.MiddleLeft;
            p.fontStyle = FontStyle.Italic;
            
            RectTransform placeholderRT = placeholderGO.GetComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.sizeDelta = new Vector2(-10, -10);
            placeholderRT.anchoredPosition = new Vector2(5, 0);
            
            inputField.placeholder = p;
            
            if (onEndEdit != null) inputField.onEndEdit.AddListener(onEndEdit);
            
            return inputGO;
        }
    }

    public static class SceneUtils
    {
        public static Atom DetectAtom(Vector2 screenPos, Camera cam, out string statusMsg)
        {
            RaycastHit hit;
            return RaycastAtom(screenPos, cam, out statusMsg, out hit);
        }

        public static Atom RaycastAtom(Vector2 screenPos, Camera cam, out string statusMsg, out RaycastHit hit)
        {
            statusMsg = "";
            hit = new RaycastHit();
            if (cam == null) return null;

            Ray ray = cam.ScreenPointToRay(screenPos);
            
            // Mask out UI layer (5) and Ignore Raycast (2)
            int layerMask = Physics.DefaultRaycastLayers & ~(1 << 5);
            
            if (Physics.Raycast(ray, out hit, 1000f, layerMask))
            {
                 Atom atom = hit.collider.GetComponentInParent<Atom>();
                 if (atom != null && atom.type == "Person")
                 {
                     statusMsg = $"Target: {atom.name}";
                     return atom;
                 }
                 // Return the atom for drag-drop logic even if it's not a Person, but skip message processing
                 return atom;
            }
            return null;
        }
    }
}
