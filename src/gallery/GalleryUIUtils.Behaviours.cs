using System;
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
        public bool isSelected = false;
        // When set, show/hide this GO on hover instead of using the Outline component.
        // Used in list mode to avoid the Outline filling the entire semi-transparent row.
        public GameObject hoverBorderGO;

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
            if (hoverBorderGO != null) hoverBorderGO.SetActive(isSelected);
            else if (outline != null) outline.enabled = isSelected;
        }

        void OnDisable()
        {
            if (hoverBorderGO != null) hoverBorderGO.SetActive(false);
            else if (outline != null) outline.enabled = false;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (hoverBorderGO != null) hoverBorderGO.SetActive(true);
            else if (outline != null) outline.enabled = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (hoverBorderGO != null) { if (!isSelected) hoverBorderGO.SetActive(false); }
            else if (outline != null && !isSelected) outline.enabled = false;
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
            // Card overlay only shows in grid mode when always-on labels are OFF
            bool labelsActive = VPBConfig.Instance != null && VPBConfig.Instance.GalleryGridLabelsStripVisible()
                                && panel != null && panel.layoutMode == GalleryLayoutMode.Grid;
            if (!labelsActive && card && panel != null && panel.layoutMode == GalleryLayoutMode.Grid)
                card.SetActive(true);
            if (panel != null && file != null) panel.SetHoverPath(file);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (card && panel != null && panel.layoutMode == GalleryLayoutMode.Grid) card.SetActive(false);
            if (panel != null) panel.RestoreSelectedHoverPath();
        }
    }

    /// <summary>
    /// Hovering the thumbnail shows an anchored larger preview (bottom-left above the tbox).
    /// Bound per-row because list/grid rows are recycled.
    /// </summary>
    public class UIHoverPreviewTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public GalleryPanel panel;
        public FileEntry file;

        public void OnPointerEnter(PointerEventData eventData)
        {
            try { if (panel != null && file != null) panel.ShowHoverPreview(file); } catch { }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            try { if (panel != null) panel.HideHoverPreview(file); } catch { }
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
    /// Run after <see cref="ScrollbarSync.LateUpdate"/> (default order). Uses
    /// <see cref="ScrollRect.verticalNormalizedPosition"/> for row window (same mapping as
    /// <see cref="ScrollToCenterItem"/>). Do not call <see cref="Canvas.ForceUpdateCanvases"/> on scroll —
    /// it re-enters layout and breaks ScrollRect + decoupled scrollbar after first drag.
    /// One <see cref="Canvas.willRenderCanvases"/> flush per burst re-binds after layout. Cache busted on every
    /// <see cref="ScrollRect.onValueChanged"/> so LateUpdate never skips a pass on stale (start,end).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class RecyclingGridView : MonoBehaviour
    {
        public static float LastScrollRealtime { get; private set; }
        public static float LastScrollNormY { get; private set; }

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
        public Action<GameObject> onRecycleItem;

        /// <summary>
        /// The item index whose row was closest to viewport center during the last UpdateVisibleItems call.
        /// Cached so callers (e.g. thumbnail priority computation) don't recompute it per-item.
        /// </summary>
        public int CachedCenterItemIndex { get; private set; }

        private List<RecyclingGridItem> activeItems = new List<RecyclingGridItem>();
        private HashSet<int> _activeIndexSet = new HashSet<int>();
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
        public float targetAspectRatio = 1.0f;
        public bool useFixedHeight = false;
        private float lastRectWidth = -1f;
        private int lastFixedColumns = -1;

        // Set before a column-count change to restore the same center item after RecalculateLayout.
        public int preserveCenterItemIndex = -1;

        private bool _needsVisibleUpdate = false;
        private bool _needsLayoutUpdate = true; // Start with true to ensure initial layout

        /// <summary>Last committed visible index range (for diagnostics only).</summary>
        private int _lastVisibleStartIndex = -1;
        private int _lastVisibleEndIndex = -1;

        private bool _pendingScrollRenderFlush;

        private void InvalidateVisibleRangeCache()
        {
            _lastVisibleStartIndex = -1;
            _lastVisibleEndIndex = -1;
        }

        /// <summary>Top of visible band in content space — derived from norm (authoritative when <see cref="ScrollbarSync"/> drives ScrollRect).</summary>
        private float GetContentScrollTopYForVisibility()
        {
            if (viewport == null || content == null) return 0f;
            if (_scrollRect == null) return Mathf.Max(0f, content.anchoredPosition.y);

            float vh = viewport.rect.height;
            if (vh <= 0.01f) vh = 800f;
            float ch = Mathf.Max(content.rect.height, content.sizeDelta.y);
            float maxScrollY = Mathf.Max(0f, ch - vh);
            if (maxScrollY <= 0.01f) return 0f;
            float norm = Mathf.Clamp01(_scrollRect.verticalNormalizedPosition);
            return Mathf.Clamp((1f - norm) * maxScrollY, 0f, maxScrollY);
        }

        private void ScrollFlushBeforeRender()
        {
            Canvas.willRenderCanvases -= ScrollFlushBeforeRender;
            _pendingScrollRenderFlush = false;
            if (!isActiveAndEnabled || itemsCount == 0 || viewport == null || content == null) return;
            InvalidateVisibleRangeCache();
            UpdateVisibleItems('W');
        }

        private void QueueScrollFlushAfterLayout()
        {
            if (_pendingScrollRenderFlush) return;
            _pendingScrollRenderFlush = true;
            Canvas.willRenderCanvases += ScrollFlushBeforeRender;
        }

        private void Awake()
        {
            if (_scrollRect == null) scrollRect = GetComponent<ScrollRect>();
            _needsLayoutUpdate = true;
        }

        private void Update()
        {
            // Adaptive relayout must not run on tiny viewport width noise: RecalculateLayout() ends in
            // Refresh() -> RecycleAll(), which drops the visible-range cache and re-binds every cell
            // — main cause of grid scroll jitter when rect.width fluctuates 1–2px per frame.
            RectTransform rt = GetComponent<RectTransform>();
            float usableWidth = viewport != null ? viewport.rect.width : (rt != null ? rt.rect.width : 0);

            if (isAdaptive)
            {
                bool layoutDirty = false;
                if (fixedColumns != lastFixedColumns)
                    layoutDirty = true;
                else if (fixedColumns > 0)
                {
                    int cols = Mathf.Max(1, fixedColumns);
                    float inner = usableWidth - (cols - 1) * spacingX;
                    if (inner > 0.01f)
                    {
                        float newCellW = inner / cols;
                        if (Mathf.Abs(newCellW - itemWidth) > 0.5f)
                            layoutDirty = true;
                    }
                    else if (Mathf.Abs(usableWidth - lastRectWidth) > 4f)
                        layoutDirty = true;
                }
                else if (Mathf.Abs(usableWidth - lastRectWidth) > 4f)
                {
                    layoutDirty = true;
                }

                if (layoutDirty)
                    _needsLayoutUpdate = true;
            }

            if (_needsLayoutUpdate)
            {
                _needsLayoutUpdate = false;
                RecalculateLayout();
            }

            // Visibility refresh is deferred to LateUpdate: ScrollRect + manual ScrollbarSync apply
            // content.anchoredPosition after our Update runs; scrollbar jumps could leave stale scroll offset
            // here so start/end indices match old viewport → no recycle/bind → thumbnails never enqueue.
        }

        private void LateUpdate()
        {
            if (!_needsVisibleUpdate) return;
            _needsVisibleUpdate = false;
            UpdateVisibleItems('L');
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= ScrollFlushBeforeRender;
            _pendingScrollRenderFlush = false;
        }

        private void OnDestroy()
        {
            Canvas.willRenderCanvases -= ScrollFlushBeforeRender;
            if (_scrollRect != null) _scrollRect.onValueChanged.RemoveListener(OnScroll);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isAdaptive) _needsLayoutUpdate = true;
        }

        /// <param name="deferFinalRefresh">When true, updates dimensions and content height only — caller must end with <see cref="Refresh"/> or <see cref="SetItemCountAtScroll"/> / <see cref="SetItemCount"/> without defer.</param>
        private void RecalculateLayout(bool deferFinalRefresh = false)
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

            float cellHeight;
            if (useFixedHeight)
            {
                cellHeight = itemHeight;
            }
            else
            {
                if (targetAspectRatio <= 0.01f) targetAspectRatio = 1.0f;
                cellHeight = cellWidth / targetAspectRatio;
            }

            // Internal update of config members
            itemWidth = cellWidth;
            itemHeight = cellHeight;
            colCount = Mathf.Max(1, cols);

            // Consume the preserved index before any height/scroll changes.
            int centerIdx = preserveCenterItemIndex;
            preserveCenterItemIndex = -1;

            UpdateContentHeight();
            if (deferFinalRefresh) return;

            Refresh();

            if (centerIdx >= 0)
                ScrollToCenterItem(centerIdx);
        }

        public void SetAdaptiveConfig(bool adaptive, float minSize, int fixedCols, bool fixedHeight, bool deferRefresh = false)
        {
            isAdaptive = adaptive;
            minCellSize = minSize;
            fixedColumns = fixedCols;
            useFixedHeight = fixedHeight;
            
            // Force immediate recalculation
            lastRectWidth = -1f; 
            RecalculateLayout(deferFinalRefresh: deferRefresh);
        }

        public void SetGridConfig(float width, float height, float spaceX, float spaceY, int columns, bool deferRefresh = false)
        {
            itemWidth = width;
            itemHeight = height;
            if (height > 0) targetAspectRatio = width / height;
            else targetAspectRatio = 1.0f;

            spacingX = spaceX;
            spacingY = spaceY;
            fixedColumns = columns; // Set this as the fixed target
            colCount = Mathf.Max(1, columns);
            
            lastRectWidth = -1f; // Force recalculation to sync width
            if (itemsCount > 0) UpdateContentHeight();
            if (!deferRefresh) Refresh();
        }

        public void SetItemCount(int count, bool deferRefresh = false)
        {
            itemsCount = count;
            UpdateContentHeight();
            if (!deferRefresh) Refresh();
        }

        /// <summary>
        /// Sets item count and immediately positions the scroll to <paramref name="normalizedPos"/>
        /// before the first UpdateVisibleItems runs, so the initial item bind happens at the
        /// correct scroll position instead of always starting from the top.
        /// </summary>
        public void SetItemCountAtScroll(int count, float normalizedPos)
        {
            itemsCount = count;
            UpdateContentHeight();
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = normalizedPos;
            Refresh();
        }

        /// <summary>
        /// Sets item count and immediately centers the viewport on <paramref name="centerItemIndex"/>
        /// before the first UpdateVisibleItems runs.
        /// </summary>
        public void SetItemCountAtItem(int count, int centerItemIndex)
        {
            itemsCount = count;
            UpdateContentHeight();
            ScrollToCenterItem(centerItemIndex);
            // ScrollToCenterItem calls UpdateVisibleItems internally, so skip the Refresh() here.
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
            UpdateVisibleItems('R');
        }

        /// <summary>Returns the index of the item whose row is closest to the viewport center.</summary>
        public int GetCenterItemIndex()
        {
            if (content == null || viewport == null || itemsCount == 0) return 0;
            float effectiveItemHeight = itemHeight + spacingY;
            if (effectiveItemHeight <= 0.1f) return 0;
            float scrollTop = GetContentScrollTopYForVisibility();
            float centerY = scrollTop + viewport.rect.height * 0.5f;
            int centerRow = Mathf.FloorToInt(centerY / effectiveItemHeight);
            centerRow = Mathf.Clamp(centerRow, 0, Mathf.Max(0, rowCount - 1));
            return Mathf.Clamp(centerRow * colCount, 0, itemsCount - 1);
        }

        /// <summary>Scrolls so that the row containing <paramref name="index"/> is centered in the viewport.</summary>
        public void ScrollToCenterItem(int index)
        {
            if (content == null || viewport == null || itemsCount == 0 || index < 0) return;
            index = Mathf.Min(index, itemsCount - 1);
            float effectiveItemHeight = itemHeight + spacingY;
            int row = index / Mathf.Max(1, colCount);
            float rowCenterY = row * effectiveItemHeight + spacingY + itemHeight * 0.5f;
            float targetScrollY = rowCenterY - viewport.rect.height * 0.5f;
            float vh = viewport.rect.height;
            float ch = Mathf.Max(content.rect.height, content.sizeDelta.y);
            float maxScrollY = Mathf.Max(0f, ch - vh);
            targetScrollY = Mathf.Clamp(targetScrollY, 0f, maxScrollY);
            if (_scrollRect != null)
                _scrollRect.verticalNormalizedPosition = maxScrollY > 0f
                    ? 1f - targetScrollY / maxScrollY
                    : 1f;
            UpdateVisibleItems('C');
        }

        /// <summary>Jump scroll to the first row (Unity: vertical normalized 1 = top).</summary>
        public void ScrollToTopImmediate()
        {
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f;
            UpdateVisibleItems('T');
        }

        /// <summary>Jump scroll to the last row (Unity: vertical normalized 0 = bottom).</summary>
        public void ScrollToBottomImmediate()
        {
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 0f;
            UpdateVisibleItems('B');
        }

        private void OnScroll(Vector2 pos)
        {
            // Bust cache on every ScrollRect delta so LateUpdate cannot no-op on stale (start,end) before willRender runs.
            InvalidateVisibleRangeCache();
            _needsVisibleUpdate = true;
            QueueScrollFlushAfterLayout();

            try
            {
                LastScrollRealtime = Time.realtimeSinceStartup;
                LastScrollNormY = _scrollRect != null ? _scrollRect.verticalNormalizedPosition : pos.y;
            }
            catch { }
        }

        private void UpdateVisibleItems(char reason = '?')
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

            float startY = GetContentScrollTopYForVisibility();
            float endY = startY + viewHeight;

            // 2-row buffer above and below so thumbnails begin loading before items enter view.
            startY -= effectiveItemHeight * 2f;
            endY += effectiveItemHeight * 2f;

            int startRow = Mathf.FloorToInt(Mathf.Max(0, startY) / effectiveItemHeight);
            int endRow = Mathf.CeilToInt(endY / effectiveItemHeight);

            startRow = Mathf.Max(0, startRow);
            endRow = Mathf.Min(rowCount - 1, endRow);

            int startIndex = startRow * colCount;
            int endIndex = Mathf.Min(itemsCount - 1, (endRow * colCount) + colCount - 1);

            // Cache center index once for the entire bind pass so onBindItem callbacks
            // don't recompute it (and access viewport.rect) for every single item.
            CachedCenterItemIndex = GetCenterItemIndex();

            // Recycle items out of range, updating the index set in sync.
            for (int i = activeItems.Count - 1; i >= 0; i--)
            {
                RecyclingGridItem item = activeItems[i];
                if (item == null || item.index < startIndex || item.index > endIndex)
                {
                    if (item != null)
                    {
                        _activeIndexSet.Remove(item.index);
                        Recycle(item.GetComponent<RectTransform>());
                    }
                    activeItems.RemoveAt(i);
                }
            }

            // Create missing items — O(1) lookup via HashSet instead of O(N) inner scan.
            for (int i = startIndex; i <= endIndex; i++)
            {
                if (_activeIndexSet.Contains(i)) continue;

                RectTransform itemRT = GetItem();
                if (itemRT != null)
                {
                    RecyclingGridItem item = itemRT.GetComponent<RecyclingGridItem>();
                    item.index = i;
                    _activeIndexSet.Add(i);
                    PositionItem(itemRT, i);
                    if (onBindItem != null) onBindItem(itemRT.gameObject, i);
                    activeItems.Add(item);
                }
            }

            int idxMin = int.MaxValue;
            int idxMax = int.MinValue;
            for (int ai = 0; ai < activeItems.Count; ai++)
            {
                RecyclingGridItem it = activeItems[ai];
                if (it == null) continue;
                if (it.index < idxMin) idxMin = it.index;
                if (it.index > idxMax) idxMax = it.index;
            }

            bool setMismatch = _activeIndexSet.Count != activeItems.Count;
            bool outOfBand = activeItems.Count > 0 && (idxMin < startIndex || idxMax > endIndex);

            int prevStart = _lastVisibleStartIndex;
            int prevEnd = _lastVisibleEndIndex;
            _lastVisibleStartIndex = startIndex;
            _lastVisibleEndIndex = endIndex;

            if (prevStart != startIndex || prevEnd != endIndex)
            {
                float normY = -1f;
                try { if (_scrollRect != null) normY = _scrollRect.verticalNormalizedPosition; } catch { }
                string ldr = null;
                try { if (CustomImageLoaderThreaded.singleton != null) ldr = CustomImageLoaderThreaded.singleton.GetLoaderDebugSnapshot(); } catch { ldr = null; }
                LogUtil.LogVerboseUi("[VPB RGVScroll] range reason=" + reason
                    + " normY=" + normY.ToString("0.000")
                    + " idx=" + startIndex + ".." + endIndex
                    + " act=" + activeItems.Count
                    + " cols=" + colCount + " rows=" + rowCount
                    + (string.IsNullOrEmpty(ldr) ? "" : (" " + ldr)));
            }

            if (setMismatch || outOfBand)
            {
                string idxSpan = (activeItems.Count == 0 || idxMin == int.MaxValue) ? "na..na" : (idxMin + ".." + idxMax);
                LogUtil.LogWarning("[VPB RGVScroll] UVI_INTEGRITY_FAIL reason=" + reason + " pred=" + startIndex + ".." + endIndex + " idxAct=" + idxSpan);
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
            if (onRecycleItem != null) onRecycleItem(item.gameObject);
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
            _activeIndexSet.Clear();
            InvalidateVisibleRangeCache();
        }
    }

    public class RecyclingGridItem : MonoBehaviour
    {
        public int index;
    }

    public class ScrollbarSync : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public static float LastScrollbarDragRealtime { get; private set; }
        public static float LastScrollbarDragValue { get; private set; }

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
        private float _lastDragLogRealtime = -999f;
        private float _lastDragLogValue = -999f;

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
            try
            {
                LastScrollbarDragRealtime = Time.realtimeSinceStartup;
                LastScrollbarDragValue = scrollbar != null ? scrollbar.value : -1f;
            }
            catch { }

            string ldr = null;
            try { if (CustomImageLoaderThreaded.singleton != null) ldr = CustomImageLoaderThreaded.singleton.GetLoaderDebugSnapshot(); } catch { ldr = null; }
            LogUtil.LogVerboseUi("[VPB Scrollbar] down val=" + (scrollbar != null ? scrollbar.value.ToString("0.000") : "na")
                + " normY=" + (scrollRect != null ? scrollRect.verticalNormalizedPosition.ToString("0.000") : "na")
                + (string.IsNullOrEmpty(ldr) ? "" : (" " + ldr)));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPointerDown = false;
            // Final sync on release to ensure alignment
            if (scrollRect != null && scrollbar != null)
            {
                SyncToScrollRect(scrollbar.value);
            }

            string ldr = null;
            try { if (CustomImageLoaderThreaded.singleton != null) ldr = CustomImageLoaderThreaded.singleton.GetLoaderDebugSnapshot(); } catch { ldr = null; }
            LogUtil.LogVerboseUi("[VPB Scrollbar] up val=" + (scrollbar != null ? scrollbar.value.ToString("0.000") : "na")
                + " normY=" + (scrollRect != null ? scrollRect.verticalNormalizedPosition.ToString("0.000") : "na")
                + (string.IsNullOrEmpty(ldr) ? "" : (" " + ldr)));
        }

        private void Update()
        {
            if (scrollbar == null) return;

            // Force interactable to prevent other scripts from disabling it
            if (!scrollbar.interactable) scrollbar.interactable = true;
            // Scrollbar size + collider sync run in LateUpdate only — avoids duplicate rect reads every frame.
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

            if (_isPointerDown)
            {
                float now = Time.realtimeSinceStartup;
                if ((now - _lastDragLogRealtime) >= 0.10f || Mathf.Abs(val - _lastDragLogValue) >= 0.02f)
                {
                    _lastDragLogRealtime = now;
                    _lastDragLogValue = val;
                    try
                    {
                        LastScrollbarDragRealtime = now;
                        LastScrollbarDragValue = val;
                    }
                    catch { }
                    string ldr = null;
                    try { if (CustomImageLoaderThreaded.singleton != null) ldr = CustomImageLoaderThreaded.singleton.GetLoaderDebugSnapshot(); } catch { ldr = null; }
                    LogUtil.LogVerboseUi("[VPB Scrollbar] drag val=" + val.ToString("0.000")
                        + " normY=" + (scrollRect != null ? scrollRect.verticalNormalizedPosition.ToString("0.000") : "na")
                        + (string.IsNullOrEmpty(ldr) ? "" : (" " + ldr)));
                }
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