using System;
using System.Collections.Generic;
using System.IO;
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
            Vector3 lookDir = target.position - eventData.pressEventCamera.transform.position;
            if (lookDir != Vector3.zero)
            {
                bool lockRotation = (Settings.Instance != null && Settings.Instance.LockGalleryRotation != null && Settings.Instance.LockGalleryRotation.Value);
                if (lockRotation)
                {
                    // Enforce horizontal leveling (Up = Vector3.up) but allow Pitch (use full lookDir)
                    target.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                }
                else
                {
                    target.rotation = Quaternion.LookRotation(lookDir, eventData.pressEventCamera.transform.up);
                }
            }
        }
    }

    public class UIResizable : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        public RectTransform target;
        public Vector2 minSize = new Vector2(400, 300);
        public Vector2 maxSize = new Vector2(2000, 2000);
        public int anchor = AnchorPresets.bottomRight;

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Consume the drag start event so it doesn't bubble up to parent UIDraggable
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null || target.parent == null) return;
            if (eventData.pressEventCamera == null) return;

            RectTransform parentRect = target.parent as RectTransform;
            if (parentRect == null) return;

            // 3. Get Mouse Position in Parent Local Space
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out Vector2 localMouse))
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

                if (target.sizeDelta != newSize || target.anchoredPosition != newPos)
                {
                    target.sizeDelta = newSize;
                    target.anchoredPosition = newPos;
                }
            }
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
        
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (card) card.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (card) card.SetActive(false);
        }
    }

    public class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public FileEntry FileEntry;
        public RawImage ThumbnailImage;
        public GalleryPanel Panel;
        
        private bool isDraggingItem = false;
        private GameObject ghostObject;
        private Image ghostBorder;
        // private Vector3 offset; // Unused
        private float planeDistance;

        public void Update()
        {
            if (isDraggingItem)
            {
                if (!Input.GetMouseButton(0))
                {
                     PointerEventData dummy = new PointerEventData(EventSystem.current);
                     // dummy.pressEventCamera is read-only, but DetectAtom falls back to Camera.main if null
                     OnEndDrag(dummy);
                }
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            isDraggingItem = true;
            CreateGhost(eventData);

            string msg;
            float dist;
            Atom atom = DetectAtom(eventData, out msg, out dist);
            if (Panel != null) Panel.SetStatus(msg);
            
            bool valid = (atom != null && atom.type == "Person");
            UpdateGhost(eventData, valid, dist);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (isDraggingItem)
            {
                string msg;
                float dist;
                Atom atom = DetectAtom(eventData, out msg, out dist);
                bool valid = (atom != null && atom.type == "Person");
                
                UpdateGhost(eventData, valid, dist);
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
                isDraggingItem = false;
                
                if (Panel != null) Panel.SetStatus("");

                string msg;
                Atom atom = DetectAtom(eventData, out msg);
                if (atom != null && FileEntry != null)
                {
                    ApplyClothingToAtom(atom, FileEntry.Uid);
                }
            }
        }

        private Atom DetectAtom(PointerEventData eventData, out string statusMsg, out float distance)
        {
            Camera cam = eventData.pressEventCamera;
            if (cam == null) cam = Camera.main;

            string hitMsg;
            RaycastHit hit;
            Atom atom = SceneUtils.RaycastAtom(eventData.position, cam, out hitMsg, out hit);
            
            statusMsg = hitMsg;
            distance = (hit.collider != null) ? hit.distance : planeDistance;

            if (atom != null && atom.type == "Person")
            {
                 statusMsg = $"Drop {FileEntry.Name} to {atom.name}";
            }
            return atom;
        }

        private Atom DetectAtom(PointerEventData eventData, out string statusMsg)
        {
            float dummy;
            return DetectAtom(eventData, out statusMsg, out dummy);
        }

        private void ApplyClothingToAtom(Atom atom, string path)
        {
            bool installed = false;
            if (FileEntry is VarFileEntry varEntry && varEntry.Package != null)
            {
                installed = varEntry.Package.InstallRecursive();
            }
            else if (FileEntry is SystemFileEntry sysEntry && sysEntry.package != null)
            {
                installed = sysEntry.package.InstallRecursive();
            }

            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            string normalizedPath = path.Replace('\\', '/');
            string currentDir = Directory.GetCurrentDirectory().Replace('\\', '/');
            
            if (normalizedPath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.Substring(currentDir.Length);
                if (normalizedPath.StartsWith("/")) normalizedPath = normalizedPath.Substring(1);
            }

            string legacyPath = normalizedPath;
            int colonIndex = normalizedPath.IndexOf(":/");
            if (colonIndex >= 0)
            {
                legacyPath = normalizedPath.Substring(colonIndex + 2);
            }

            LogUtil.Log($"[DragDropDebug] Attempting to apply. FullPath: {normalizedPath}, LegacyPath: {legacyPath}, Installed: {installed}");

            JSONStorable geometry = atom.GetStorableByID("geometry");

            if (Panel != null && Panel.DragDropReplaceMode)
            {
                if (normalizedPath.IndexOf("/Hair/", StringComparison.OrdinalIgnoreCase) >= 0 && geometry != null)
                {
                     LogUtil.Log("[DragDropDebug] Replace mode: Clearing existing hair.");
                     List<string> all = geometry.GetBoolParamNames();
                     if (all != null)
                     {
                         foreach(string n in all)
                         {
                             if (n.StartsWith("hair:"))
                             {
                                 JSONStorableBool p = geometry.GetBoolJSONParam(n);
                                 if (p != null && p.val) p.val = false;
                             }
                         }
                     }
                }
            }

            // Try to load as preset first (standard for Clothing/Hair presets)
            string ext = Path.GetExtension(normalizedPath).ToLowerInvariant();
            if (ext == ".vap" || ext == ".json" || ext == ".vam")
            {
                try
                {
                    LogUtil.Log($"[DragDropDebug] Trying LoadPreset with: {normalizedPath}");
                    atom.LoadPreset(normalizedPath);
                }
                catch (Exception ex)
                {
                     LogUtil.LogError("[DragDropDebug] LoadPreset failed for " + normalizedPath + ": " + ex.Message);
                     // Fallthrough to legacy toggle
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
             if (eventData.pressEventCamera == null) return;
             
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
             rt.sizeDelta = new Vector2(80, 80); 
             rt.pivot = new Vector2(0.5f, 0.5f);
             
             RectTransform contentRT = contentGO.AddComponent<RectTransform>();
             contentRT.anchorMin = Vector2.zero;
             contentRT.anchorMax = Vector2.one;
             contentRT.offsetMin = new Vector2(5, 5);
             contentRT.offsetMax = new Vector2(-5, -5);
             
             planeDistance = Vector3.Dot(transform.position - eventData.pressEventCamera.transform.position, eventData.pressEventCamera.transform.forward);
             
             UpdateGhost(eventData, false, planeDistance);
        }
        
        private void UpdateGhost(PointerEventData eventData, bool isValidTarget, float distance)
        {
             if (ghostObject == null || eventData.pressEventCamera == null) return;
             
             UpdateGhostPosition(eventData, isValidTarget, distance);
             
             if (isValidTarget)
             {
                 if (ghostBorder != null) ghostBorder.color = new Color(0, 1, 0, 0.4f);
             }
             else
             {
                 if (ghostBorder != null) ghostBorder.color = new Color(1, 1, 1, 0.2f);
             }
        }
        
        private void UpdateGhostPosition(PointerEventData eventData, bool isValidTarget, float distance)
        {
             Camera cam = eventData.pressEventCamera;
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

        private void DestroyGhost()
        {
            if (ghostObject != null)
            {
                Destroy(ghostObject);
                ghostObject = null;
                ghostBorder = null;
            }
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
                 if (atom != null)
                 {
                     if (atom.type == "Person")
                     {
                         statusMsg = $"Target: {atom.name}";
                         return atom;
                     }
                     else
                     {
                         statusMsg = $"Hit Atom: {atom.name} ({atom.type})";
                         return atom;
                     }
                 }
                 else
                 {
                     statusMsg = $"Hit: {hit.collider.name}";
                 }
            }
            return null;
        }
    }
}
