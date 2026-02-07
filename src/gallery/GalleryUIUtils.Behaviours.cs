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
                }
                outline.effectDistance = new Vector2(borderSize, -borderSize);
                outline.effectColor = hoverColor;
                outline.enabled = false;
            }
        }

        void OnEnable()
        {
            if (outline != null) outline.enabled = isSelected;
        }

        void OnDisable()
        {
            if (outline != null) outline.enabled = false;
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
        public float minHeight => preferredHeight;
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
        public int forcedColumnCount = 0;
        
        private RectTransform rt;
        private float lastWidth = -1f;
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
            if (Mathf.Abs(width - lastWidth) < 0.1f && forcedColumnCount == lastForcedColumnCount && grid.cellSize.x > 0) return;
            
            lastWidth = width;
            lastForcedColumnCount = forcedColumnCount;

            float usableWidth = width - grid.padding.left - grid.padding.right;
            if (usableWidth <= 0) return;
            
            int forcedCols = forcedColumnCount;
            if (forcedCols < 0) forcedCols = 0;

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

    public class RecyclingGridView : MonoBehaviour
    {
        private ScrollRect _scrollRect;
        public ScrollRect scrollRect
        {
            get { return _scrollRect; }
            set
            {
                if (_scrollRect != null) _scrollRect.onValueChanged.RemoveListener(OnScroll);
                _scrollRect = value;
                if (_scrollRect != null)
                {
                    _scrollRect.onValueChanged.AddListener(OnScroll);
                    viewport = _scrollRect.viewport;
                    content = _scrollRect.content;
                }
            }
        }
        public RectTransform content;
        public RectTransform viewport;
        public GameObject itemTemplate;
        public int itemsCount = 0;
        
        public Action<GameObject, int> onBindItem;
        public Func<GameObject> onCreateItem;

        private List<RecyclingGridItem> activeItems = new List<RecyclingGridItem>();
        private Stack<RectTransform> pool = new Stack<RectTransform>();
        
        // Grid State
        private float itemWidth = 100f;
        private float itemHeight = 100f;
        private float spacingX = 5f;
        private float spacingY = 5f;
        private int colCount = 1;
        private int rowCount = 0;

        // Adaptive Config
        public bool isAdaptive = false;
        public float minCellSize = 200f;
        public int fixedColumns = 0;
        private float lastRectWidth = -1f;
        private int lastFixedColumns = -1;

        private bool _needsVisibleUpdate = false;
        private bool _needsLayoutUpdate = true; // Start with true to ensure initial layout

        private void Awake()
        {
            if (_scrollRect == null) scrollRect = GetComponent<ScrollRect>();
            _needsLayoutUpdate = true;
        }

        private void Update()
        {
            // Robust check: catch initial width or config changes
            RectTransform rt = GetComponent<RectTransform>();
            float usableWidth = viewport != null ? viewport.rect.width : (rt != null ? rt.rect.width : 0);
            
            if (isAdaptive && (Mathf.Abs(usableWidth - lastRectWidth) > 1.0f || fixedColumns != lastFixedColumns))
            {
                _needsLayoutUpdate = true;
            }

            if (_needsLayoutUpdate)
            {
                _needsLayoutUpdate = false;
                RecalculateLayout();
            }

            if (_needsVisibleUpdate)
            {
                _needsVisibleUpdate = false;
                UpdateVisibleItems();
            }
        }

        private void OnDestroy()
        {
            if (_scrollRect != null) _scrollRect.onValueChanged.RemoveListener(OnScroll);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isAdaptive) _needsLayoutUpdate = true;
        }

        private void RecalculateLayout()
        {
            if (content == null) return;
            
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null) return;
            
            // Priority: Viewport width (actual visible area)
            float usableWidth = viewport != null ? viewport.rect.width : rt.rect.width;
            
            // If we don't have a width yet, try to force it or use a sensible default
            bool wasZero = false;
            if (usableWidth <= 0) 
            {
                Canvas.ForceUpdateCanvases();
                usableWidth = viewport != null ? viewport.rect.width : rt.rect.width;
            }
            if (usableWidth <= 0) 
            {
                usableWidth = 1170f; 
                wasZero = true;
            }

            lastRectWidth = wasZero ? 0f : usableWidth; // Store 0 if we defaulted, to allow next change to trigger
            lastFixedColumns = fixedColumns;
            
            int cols = fixedColumns;
            if (cols <= 0)
            {
                cols = Mathf.FloorToInt((usableWidth + spacingX) / (minCellSize + spacingX));
                if (cols < 1) cols = 1;
            }
            
            float cellWidth = (usableWidth - (cols - 1) * spacingX) / cols;
            if (cellWidth < 10f) cellWidth = 10f; // Sanity check

            float cellHeight = cellWidth;

            // Internal update of config members
            itemWidth = cellWidth;
            itemHeight = cellHeight;
            colCount = Mathf.Max(1, cols);
            
            UpdateContentHeight();
            Refresh();
        }

        public void SetAdaptiveConfig(bool adaptive, float minSize, int fixedCols, bool unused)
        {
            isAdaptive = adaptive;
            minCellSize = minSize;
            fixedColumns = fixedCols;
            
            // Force immediate recalculation
            lastRectWidth = -1f; 
            RecalculateLayout();
        }

        public void SetGridConfig(float width, float height, float spaceX, float spaceY, int columns)
        {
            itemWidth = width;
            itemHeight = height;
            spacingX = spaceX;
            spacingY = spaceY;
            fixedColumns = columns; // Set this as the fixed target
            colCount = Mathf.Max(1, columns);
            
            lastRectWidth = -1f; // Force recalculation to sync width
            if (itemsCount > 0) UpdateContentHeight();
            Refresh();
        }

        public void SetItemCount(int count)
        {
            itemsCount = count;
            UpdateContentHeight();
            Refresh();
        }

        private void UpdateContentHeight()
        {
            rowCount = Mathf.CeilToInt((float)itemsCount / colCount);
            float totalHeight = rowCount * (itemHeight + spacingY) + spacingY;
            if (totalHeight < 0) totalHeight = 0;
            
            if (content != null)
            {
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = new Vector2(1, 1);
                content.pivot = new Vector2(0, 1);
                content.sizeDelta = new Vector2(0, totalHeight);
            }
        }

        public void Refresh()
        {
            RecycleAll();
            UpdateVisibleItems();
        }

        private void OnScroll(Vector2 pos)
        {
            _needsVisibleUpdate = true;
        }

        private void UpdateVisibleItems()
        {
            if (itemsCount == 0 || viewport == null || content == null) 
            {
                if (activeItems.Count > 0) RecycleAll();
                return;
            }

            // Ensure we have a valid item height to avoid division by zero or infinite loops
            float effectiveItemHeight = itemHeight + spacingY;
            if (effectiveItemHeight <= 0.1f) effectiveItemHeight = 200f;

            float viewHeight = viewport.rect.height;
            if (viewHeight <= 0) viewHeight = 800f; 

            float startY = content.anchoredPosition.y;
            float endY = startY + viewHeight;
            
            // Revert to simpler buffer logic that worked
            startY -= effectiveItemHeight; 
            endY += effectiveItemHeight;

            int startRow = Mathf.FloorToInt(Mathf.Max(0, startY) / effectiveItemHeight);
            int endRow = Mathf.CeilToInt(endY / effectiveItemHeight);
            
            startRow = Mathf.Max(0, startRow);
            endRow = Mathf.Min(rowCount - 1, endRow);

            int startIndex = startRow * colCount;
            int endIndex = Mathf.Min(itemsCount - 1, (endRow * colCount) + colCount - 1); 

            // Recycle items out of range
            for (int i = activeItems.Count - 1; i >= 0; i--)
            {
                RecyclingGridItem item = activeItems[i];
                if (item == null || item.index < startIndex || item.index > endIndex)
                {
                    if (item != null) Recycle(item.GetComponent<RectTransform>());
                    activeItems.RemoveAt(i);
                }
            }

            // Create missing items
            for (int i = startIndex; i <= endIndex; i++)
            {
                bool alreadyActive = false;
                for (int j = 0; j < activeItems.Count; j++)
                {
                    if (activeItems[j] != null && activeItems[j].index == i)
                    {
                        alreadyActive = true;
                        break;
                    }
                }

                if (!alreadyActive)
                {
                    RectTransform itemRT = GetItem();
                    if (itemRT != null)
                    {
                        RecyclingGridItem item = itemRT.GetComponent<RecyclingGridItem>();
                        item.index = i;
                        PositionItem(itemRT, i);
                        if (onBindItem != null) onBindItem(itemRT.gameObject, i);
                        activeItems.Add(item);
                    }
                }
            }
        }

        private void PositionItem(RectTransform item, int index)
        {
            int row = index / colCount;
            int col = index % colCount;

            float x = col * (itemWidth + spacingX) + spacingX;
            float y = -(row * (itemHeight + spacingY) + spacingY);

            item.anchoredPosition = new Vector2(x, y);
            item.sizeDelta = new Vector2(itemWidth, itemHeight);
        }

        private RectTransform GetItem()
        {
            RectTransform item = null;
            if (pool.Count > 0)
            {
                item = pool.Pop();
                item.gameObject.SetActive(true);
            }
            else
            {
                GameObject go = null;
                if (onCreateItem != null) go = onCreateItem();
                else if (itemTemplate != null) go = Instantiate(itemTemplate);
                
                if (go != null)
                {
                    item = go.GetComponent<RectTransform>();
                    go.transform.SetParent(content, false);
                    RecyclingGridItem rgi = go.GetComponent<RecyclingGridItem>();
                    if (rgi == null) rgi = go.AddComponent<RecyclingGridItem>();
                    
                    item.anchorMin = new Vector2(0, 1);
                    item.anchorMax = new Vector2(0, 1);
                    item.pivot = new Vector2(0, 1);
                }
            }
            return item;
        }

        private void Recycle(RectTransform item)
        {
            if (item == null) return;
            item.gameObject.SetActive(false);
            pool.Push(item);
        }

        private void RecycleAll()
        {
            for (int i = 0; i < activeItems.Count; i++)
            {
                Recycle(activeItems[i].GetComponent<RectTransform>());
            }
            activeItems.Clear();
        }
    }

    public class RecyclingGridItem : MonoBehaviour
    {
        public int index;
    }

    public class ScrollbarSync : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        private ScrollRect _scrollRect;
        public ScrollRect scrollRect
        {
            get { return _scrollRect; }
            set
            {
                if (_scrollRect == value) return;
                if (_scrollRect != null) _scrollRect.onValueChanged.RemoveListener(SyncToScrollbar);
                _scrollRect = value;
                if (_scrollRect != null)
                {
                    _scrollRect.onValueChanged.AddListener(SyncToScrollbar);
                    if (scrollbar != null) SyncToScrollbar(_scrollRect.normalizedPosition);
                }
            }
        }

        private Scrollbar _scrollbar;
        public Scrollbar scrollbar
        {
            get { return _scrollbar; }
            set
            {
                if (_scrollbar == value) return;
                if (_scrollbar != null) _scrollbar.onValueChanged.RemoveListener(SyncToScrollRect);
                _scrollbar = value;
                if (_scrollbar != null)
                {
                    _scrollbar.onValueChanged.AddListener(SyncToScrollRect);
                    _scrollbar.interactable = true;
                    _scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
                    if (scrollRect != null) SyncToScrollbar(scrollRect.normalizedPosition);
                }
            }
        }

        public float minSizePixels = 30f;

        private bool _isPointerDown = false;
        private bool _isSyncing = false;
        private BoxCollider _collider;
        private RectTransform _scrollbarRT;

        private void Awake()
        {
            _scrollbarRT = transform as RectTransform;
            _collider = GetComponent<BoxCollider>();
        }

        private void OnEnable()
        {
            if (scrollbar != null) 
            {
                scrollbar.onValueChanged.RemoveListener(SyncToScrollRect);
                scrollbar.onValueChanged.AddListener(SyncToScrollRect);
                scrollbar.interactable = true;
            }
            if (scrollRect != null) 
            {
                scrollRect.onValueChanged.RemoveListener(SyncToScrollbar);
                scrollRect.onValueChanged.AddListener(SyncToScrollbar);
                SyncToScrollbar(scrollRect.normalizedPosition);
            }
            UpdateScrollbarSize();
        }

        private void Start()
        {
            if (scrollRect != null) SyncToScrollbar(scrollRect.normalizedPosition);
            UpdateScrollbarSize();
        }

        private void OnDisable()
        {
            if (scrollbar != null) scrollbar.onValueChanged.RemoveListener(SyncToScrollRect);
            if (scrollRect != null) scrollRect.onValueChanged.RemoveListener(SyncToScrollbar);
            _isPointerDown = false;
        }

        public void OnPointerDown(PointerEventData eventData) 
        { 
            _isPointerDown = true; 
        }

        public void OnPointerUp(PointerEventData eventData) 
        { 
            _isPointerDown = false; 
            // Final sync on release to ensure alignment
            if (scrollRect != null && scrollbar != null)
            {
                SyncToScrollRect(scrollbar.value);
            }
        }

        private void Update()
        {
            if (scrollbar == null) return;

            // Force interactable to prevent other scripts from disabling it
            if (!scrollbar.interactable) scrollbar.interactable = true;
            
            UpdateScrollbarSize();
        }

        private void LateUpdate()
        {
            if (scrollRect == null || scrollbar == null) return;

            // Keep decoupled to avoid Unity's internal auto-resizing which causes flickering
            if (scrollRect.verticalScrollbar != null)
                scrollRect.verticalScrollbar = null;

            UpdateScrollbarSize();

            // Fallback sync to ensure consistency if onValueChanged didn't catch a programmatic change
            if (!_isPointerDown && !_isSyncing)
            {
                float targetVal = scrollRect.verticalNormalizedPosition;
                if (Mathf.Abs(scrollbar.value - targetVal) > 0.001f)
                {
                    _isSyncing = true;
                    scrollbar.value = targetVal;
                    _isSyncing = false;
                }
            }
        }

        private void SyncToScrollRect(float val)
        {
            if (_isSyncing || scrollRect == null) return;
            _isSyncing = true;
            try 
            {
                scrollRect.verticalNormalizedPosition = val;
            } 
            finally 
            { 
                _isSyncing = false; 
            }
        }

        private void SyncToScrollbar(Vector2 vec)
        {
            // If user is dragging the scrollbar, don't let the ScrollRect override its value
            if (_isSyncing || scrollbar == null || _isPointerDown) return;
            
            _isSyncing = true;
            try 
            {
                scrollbar.value = vec.y;
            } 
            finally 
            { 
                _isSyncing = false; 
            }
        }

        public void UpdateScrollbarSize()
        {
            if (scrollbar == null || scrollRect == null || scrollRect.content == null || scrollRect.viewport == null) return;

            float contentHeight = scrollRect.content.rect.height;
            float viewportHeight = scrollRect.viewport.rect.height;

            float finalSize = 1f;
            if (contentHeight > viewportHeight + 0.1f)
            {
                float size = viewportHeight / contentHeight;
                
                // Use actual RT height for track height
                float trackHeight = _scrollbarRT != null ? _scrollbarRT.rect.height : ((RectTransform)scrollbar.transform).rect.height;
                if (trackHeight <= 1f) trackHeight = viewportHeight;
                
                float minSizeFraction = minSizePixels / Mathf.Max(1f, trackHeight);
                finalSize = Mathf.Max(size, minSizeFraction);
            }

            if (Mathf.Abs(scrollbar.size - finalSize) > 0.001f)
            {
                scrollbar.size = finalSize;
            }

            UpdateCollider();
        }

        private void UpdateCollider()
        {
            if (_collider == null) _collider = GetComponent<BoxCollider>();
            if (_collider == null) return;

            if (_scrollbarRT == null) _scrollbarRT = transform as RectTransform;
            if (_scrollbarRT == null) return;

            Vector3 targetSize = new Vector3(_scrollbarRT.rect.width, _scrollbarRT.rect.height, 1f);
            // Ensure some depth for easier interaction
            targetSize.z = 20f; 

            if (Vector3.SqrMagnitude(_collider.size - targetSize) > 0.001f)
            {
                _collider.size = targetSize;
            }

            Vector2 pivot = _scrollbarRT.pivot;
            Vector3 targetCenter = new Vector3((0.5f - pivot.x) * _scrollbarRT.rect.width, (0.5f - pivot.y) * _scrollbarRT.rect.height, 0f);
            
            if (Vector3.SqrMagnitude(_collider.center - targetCenter) > 0.001f)
            {
                _collider.center = targetCenter;
            }
        }
    }
}