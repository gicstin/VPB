using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using SimpleJSON;

namespace VPB
{
    public class UIDraggable : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public Transform target;
        private float planeDistance;
        private Vector3 offset;
        private Camera dragCam;

        public void OnBeginDrag(PointerEventData eventData)
        {
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

                if (target.sizeDelta != newSize || target.anchoredPosition != newPos)
                {
                    target.sizeDelta = newSize;
                    target.anchoredPosition = newPos;
                }
            }
        }
    }

    public class UIHoverBorder : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Graphic targetGraphic;
        public Color hoverColor = new Color(1f, 1f, 0f, 1f); // Bright yellow visible highlight
        public float borderSize = 2f;
        
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
            if (outline != null) outline.enabled = false;
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

    public class UIGridAdaptive : MonoBehaviour
    {
        public GridLayoutGroup grid;
        public float minSize = 200f;
        public float maxSize = 260f;
        public float spacing = 10f;
        
        private RectTransform rt;
        private float lastWidth = -1f;

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
            if (Mathf.Abs(width - lastWidth) < 0.1f) return;
            lastWidth = width;

            float usableWidth = width - grid.padding.left - grid.padding.right;
            if (usableWidth <= 0) return;
            
            // Calculate number of columns using a target size that balances between min and max
            // We want to change column count when the cell size would exceed maxSize or drop below minSize.
            // A simple approach is to use the floor of (usableWidth + spacing) / (minSize + spacing)
            // but to avoid the size becoming too large for small column counts, 
            // we can use a target size in the middle of our range.
            float targetSize = (minSize + maxSize) * 0.5f;
            int n = Mathf.FloorToInt((usableWidth + spacing) / (minSize + spacing));
            if (n < 1) n = 1;
            
            float cellSize = (usableWidth - (n - 1) * spacing) / n;
            
            // If the resulting cellSize is too much larger than our max, we could force an extra column
            // even if it drops below minSize slightly, but usually the floor logic is sufficient.
            if (cellSize > maxSize && n > 0)
            {
                // Optional: force n+1 if it's closer to the range
            }

            grid.cellSize = new Vector2(cellSize, cellSize);
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
        private Text ghostText; // Added text component
        // private Vector3 offset; // Unused
        private float planeDistance;
        private Camera dragCam;

        private static Dictionary<string, HashSet<string>> _globalRegionCache = new Dictionary<string, HashSet<string>>();

        private enum ItemType { Clothing, Hair, Pose, Skin, Morphs, Appearance, Animation, BreastPhysics, GlutePhysics, Plugins, General, ClothingItem, HairItem, SubScene, Scene, Other }

        private ItemType GetItemType(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Path)) return ItemType.Other;
            string p = entry.Path.Replace('\\', '/');
            
            // Check for person preset categories (these use .vap or .json)
            if (p.IndexOf("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Appearance;
            if (p.IndexOf("Custom/Atom/Person/AnimationPresets", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Animation;
            if (p.IndexOf("Custom/Atom/Person/BreastPhysics", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.BreastPhysics;
            if (p.IndexOf("Custom/Atom/Person/Clothing", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Clothing;
            if (p.IndexOf("Custom/Atom/Person/GlutePhysics", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.GlutePhysics;
            if (p.IndexOf("Custom/Atom/Person/Hair", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Hair;
            if (p.IndexOf("Custom/Atom/Person/Morphs", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Morphs;
            if (p.IndexOf("Custom/Atom/Person/Plugins", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Plugins;
            if (p.IndexOf("Custom/Atom/Person/Pose", StringComparison.OrdinalIgnoreCase) >= 0 || p.EndsWith(".vac", StringComparison.OrdinalIgnoreCase)) return ItemType.Pose;
            if (p.IndexOf("Custom/Atom/Person/Skin", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.Skin;
            if (p.IndexOf("Custom/Atom/Person/General", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.General;
            
            // Check for clothing/hair items (these use .vam and toggle on/off)
            if (p.IndexOf("Custom/Clothing/", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.ClothingItem;
            if (p.IndexOf("Custom/Hair/", StringComparison.OrdinalIgnoreCase) >= 0) return ItemType.HairItem;
            
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
                case ItemType.GlutePhysics: return "FemaleGlutePhysicsPresets";
                case ItemType.Hair: return "HairPresets";
                case ItemType.HairItem: return "HairPresets";
                case ItemType.Morphs: return "MorphPresets";
                case ItemType.Plugins: return "PluginPresets";
                case ItemType.Pose: return "PosePresets";
                case ItemType.Skin: return "SkinPresets";
                default: return null;
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragCam = eventData.pressEventCamera;
            if (dragCam == null) dragCam = Camera.main;

            isDraggingItem = true;
            CreateGhost(eventData);
            if (Panel != null)
            {
                Panel.ShowCancelDropZone(true);
            }

            string msg;
            float dist;
            Atom atom = DetectAtom(eventData, out msg, out dist);
            bool overCancel = Panel != null && Panel.IsPointerOverCancelDropZone(eventData);
            if (Panel != null) Panel.SetStatus(overCancel ? "Drop to cancel" : msg);
            
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
                     bool overCancel = Panel.IsPointerOverCancelDropZone(eventData);
                     Panel.SetStatus(overCancel ? "Drop to cancel" : msg);
                }
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (isDraggingItem)
            {
                bool cancelDrop = Panel != null && Panel.IsPointerOverCancelDropZone(eventData);
                DestroyGhost();
                isDraggingItem = false;
                
                if (Panel != null)
                {
                    Panel.ShowCancelDropZone(false);
                    Panel.SetStatus("");
                }

                if (cancelDrop)
                {
                    dragCam = null;
                    return;
                }

                ItemType itemType = GetItemType(FileEntry);
                
                // Handle subscenes differently - load directly without requiring atom
                if (itemType == ItemType.SubScene && FileEntry != null)
                {
                    LoadSubScene(FileEntry.Uid);
                }
                else if (itemType == ItemType.Scene && FileEntry != null)
                {
                    LoadSceneFile(FileEntry.Uid);
                }
                else
                {
                    string msg;
                    Atom atom = DetectAtom(eventData, out msg);
                    if (atom != null && FileEntry != null)
                    {
                        ApplyClothingToAtom(atom, FileEntry.Uid);
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
                isDraggingItem = false;
                if (Panel != null) Panel.SetStatus("");
                dragCam = null;
            }
            if (Panel != null) Panel.ShowCancelDropZone(false);
        }

        public void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && isDraggingItem)
            {
                DestroyGhost();
                isDraggingItem = false;
                if (Panel != null) Panel.SetStatus("");
                dragCam = null;
            }
            if (!hasFocus && Panel != null) Panel.ShowCancelDropZone(false);
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

            ItemType itemType = GetItemType(FileEntry);
            
            if (itemType == ItemType.SubScene)
            {
                statusMsg = $"Drop to load SubScene: {FileEntry.Name}";
            }
            else if (itemType == ItemType.Scene)
            {
                statusMsg = $"Release to launch scene {FileEntry.Name}";
            }
            else if (atom != null && atom.type == "Person")
            {
                 string action = (Panel != null && Panel.DragDropReplaceMode) ? "Replacing" : "Adding";
                 statusMsg = $"{action} {FileEntry.Name} to {atom.name}";
            }
            return atom;
        }

        private Atom DetectAtom(PointerEventData eventData, out string statusMsg)
        {
            float dummy;
            return DetectAtom(eventData, out statusMsg, out dummy);
        }

        private void LoadSubScene(string path)
        {
            bool installed = EnsureInstalled();

            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            string normalizedPath = NormalizePath(path);

            LogUtil.Log($"[DragDropDebug] Loading SubScene: {normalizedPath}");
            
            try
            {
                StartCoroutine(LoadSubSceneCoroutine(normalizedPath));
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[DragDropDebug] Failed to load subscene: {ex.Message}");
            }
        }

        private void LoadSceneFile(string path)
        {
            bool installed = EnsureInstalled();

            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            string normalizedPath = NormalizePath(path);
            LogUtil.Log($"[DragDropDebug] Loading Scene: {normalizedPath}");
            try
            {
                SuperController sc = SuperController.singleton;
                if (sc != null)
                {
                    sc.Load(normalizedPath);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"[DragDropDebug] Failed to load scene: {ex.Message}");
            }
        }

        private System.Collections.IEnumerator LoadSubSceneCoroutine(string path)
        {
            yield return SuperController.singleton.AddAtomByType("SubScene", "", true, true, true);
            yield return new WaitForEndOfFrame();
            
            // Find the newly created SubScene atom
            Atom subSceneAtom = null;
            foreach (var atom in SuperController.singleton.GetAtoms())
            {
                if (atom.type == "SubScene")
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
                    LogUtil.Log($"[DragDropDebug] Calling LoadSubSceneWithPath on SubScene atom with path: {path}");
                    MethodInfo loadMethod = typeof(SubScene).GetMethod("LoadSubSceneWithPath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (loadMethod != null)
                    {
                        loadMethod.Invoke(subScene, new object[] { path });
                    }
                }
            }
        }

        private bool EnsureInstalled()
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
            return installed;
        }

        private string NormalizePath(string path)
        {
            string normalizedPath = path.Replace('\\', '/');
            string currentDir = Directory.GetCurrentDirectory().Replace('\\', '/');
            
            if (normalizedPath.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.Substring(currentDir.Length);
                if (normalizedPath.StartsWith("/")) normalizedPath = normalizedPath.Substring(1);
            }
            return normalizedPath;
        }

        private void ApplyClothingToAtom(Atom atom, string path)
        {
            bool installed = EnsureInstalled();

            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                FileManager.Refresh();
            }

            string normalizedPath = NormalizePath(path);

            string legacyPath = normalizedPath;
            int colonIndex = normalizedPath.IndexOf(":/");
            if (colonIndex >= 0)
            {
                legacyPath = normalizedPath.Substring(colonIndex + 2);
            }

            LogUtil.Log($"[DragDropDebug] Attempting to apply. FullPath: {normalizedPath}, LegacyPath: {legacyPath}, Installed: {installed}");

            JSONStorable geometry = atom.GetStorableByID("geometry");
            ItemType itemType = GetItemType(FileEntry);

            // Capture state for Undo
            if (Panel != null)
            {
                try
                {
                    // Only snapshot relevant storables to avoid breaking physics/scene state
                    // We primarily care about geometry (clothing/hair items) and StorableIds for presets
                    List<JSONClass> storableSnapshots = new List<JSONClass>();
                    
                    // 1. Geometry (Direct toggle items)
                    JSONStorable geometryStorable = atom.GetStorableByID("geometry");
                    if (geometryStorable != null) storableSnapshots.Add(geometryStorable.GetJSON());

                    // 2. Preset Managers (Clothing, Hair, Pose, Skin, etc)
                    // We can snapshot all PresetManagers on the atom as they control the state of what's applied
                    foreach(var storable in atom.GetStorableIDs())
                    {
                         // Heuristic: If it ends in "Presets" or is a known manager
                         if (storable.EndsWith("Presets") || storable == "Skin" || storable.EndsWith("Physics"))
                         {
                             JSONStorable s = atom.GetStorableByID(storable);
                             if (s != null) storableSnapshots.Add(s.GetJSON());
                         }
                    }

                    string atomUid = atom.uid; 
                    Panel.PushUndo(() => {
                        Atom targetAtom = SuperController.singleton.GetAtomByUid(atomUid);
                        if (targetAtom != null)
                        {
                            // Restore specific storables
                            foreach(var snap in storableSnapshots)
                            {
                                string sid = snap["id"].Value;
                                JSONStorable s = targetAtom.GetStorableByID(sid);
                                if (s != null)
                                {
                                    s.RestoreFromJSON(snap);
                                }
                            }
                            LogUtil.Log($"[Gallery] Undo performed on {atomUid} (Storables)");
                        }
                        else
                        {
                            LogUtil.LogError($"[Gallery] Undo failed: Atom {atomUid} not found.");
                        }
                    });
                }
                catch(Exception ex)
                {
                    LogUtil.LogError("[Gallery] Failed to capture undo state: " + ex.Message);
                }
            }
            
            bool replaceMode = Panel != null && Panel.DragDropReplaceMode;
            bool isClothingOrHair = (itemType == ItemType.Clothing || itemType == ItemType.Hair || itemType == ItemType.ClothingItem || itemType == ItemType.HairItem);
            LogUtil.Log($"[DragDropDebug] Panel={Panel != null}, ReplaceMode={replaceMode}, ItemType={itemType}, IsClothingOrHair={isClothingOrHair}");

            if (Panel != null && Panel.DragDropReplaceMode && (itemType == ItemType.Clothing || itemType == ItemType.Hair || itemType == ItemType.ClothingItem || itemType == ItemType.HairItem))
            {
                bool isHair = (itemType == ItemType.Hair || itemType == ItemType.HairItem);
                bool isClothing = !isHair;

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
                                 else
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

            // Try to load as preset first (standard for Clothing/Hair presets and Poses)
            string ext = Path.GetExtension(normalizedPath).ToLowerInvariant();
            if (ext == ".vap" || ext == ".json" || ext == ".vac")
            {
                string storableId = GetStorableIdForItemType(itemType);
                if (storableId != null && atom.type == "Person")
                {
                    bool isPose = itemType == ItemType.Pose;
                    Dictionary<string, bool> originalLocks = new Dictionary<string, bool>();

                    // If it's a pose, we want to lock Clothing and Hair to prevent them being changed if the pose preset contains them
                    if (isPose && atom.presetManagerControls != null)
                    {
                        foreach (var pmc in atom.presetManagerControls)
                        {
                            if (pmc != null && (pmc.name == "ClothingPresets" || pmc.name == "HairPresets"))
                            {
                                originalLocks[pmc.name] = pmc.lockParams;
                                pmc.lockParams = true;
                            }
                        }
                    }

                    bool presetLoaded = false;
                    try
                    {
                        LogUtil.Log($"[DragDropDebug] Loading preset type={itemType}, storableId={storableId}, path={normalizedPath}");
                        
                        // Get the storable for this preset type
                        JSONStorable presetStorable = atom.GetStorableByID(storableId);
                        if (presetStorable != null)
                        {
                            // Get the PresetManager component
                            MeshVR.PresetManager presetManager = presetStorable.GetComponentInChildren<MeshVR.PresetManager>();
                            if (presetManager != null)
                            {
                                // Load the preset JSON
                                JSONClass presetJSON = SuperController.singleton.LoadJSON(normalizedPath).AsObject;
                                if (presetJSON != null)
                                {
                                    // Replace SELF: and ./ references with actual package paths
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
                                    
                                    // Apply the preset
                                    atom.SetLastRestoredData(presetJSON, true, true);
                                    presetManager.LoadPresetFromJSON(presetJSON, false);
                                    presetLoaded = true;
                                    LogUtil.Log($"[DragDropDebug] Successfully loaded preset via PresetManager");
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
                        // Restore locks
                        if (isPose && atom.presetManagerControls != null)
                        {
                            foreach (var pmc in atom.presetManagerControls)
                            {
                                if (pmc != null && originalLocks.ContainsKey(pmc.name))
                                {
                                    pmc.lockParams = originalLocks[pmc.name];
                                }
                            }
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
             
             // --- Added Text ---
             GameObject textGO = new GameObject("ActionText");
             textGO.transform.SetParent(ghostObject.transform, false);
             ghostText = textGO.AddComponent<Text>();
             ghostText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
             ghostText.fontSize = 24;
             ghostText.color = Color.white;
             ghostText.alignment = TextAnchor.UpperCenter;
             ghostText.horizontalOverflow = HorizontalWrapMode.Overflow;
             ghostText.verticalOverflow = VerticalWrapMode.Overflow;
             
             // Add Outline
             textGO.AddComponent<Outline>().effectColor = Color.black;

             RectTransform textRT = textGO.GetComponent<RectTransform>();
             textRT.anchorMin = new Vector2(0.5f, 0);
             textRT.anchorMax = new Vector2(0.5f, 0);
             textRT.pivot = new Vector2(0.5f, 1);
             textRT.anchoredPosition = new Vector2(0, -10);
             textRT.sizeDelta = new Vector2(400, 60);
             // ------------------

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
             
             planeDistance = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
             
             UpdateGhost(eventData, null, planeDistance);
        }
        
        private void UpdateGhost(PointerEventData eventData, Atom atom, float distance)
        {
             Camera cam = dragCam != null ? dragCam : eventData.pressEventCamera;
             if (cam == null) cam = Camera.main;
             if (ghostObject == null || cam == null) return;
             
             bool isCancelZone = Panel != null && Panel.IsPointerOverCancelDropZone(eventData);
             if (isCancelZone)
             {
                 UpdateGhostPosition(eventData, false, distance);
                 if (ghostBorder != null) ghostBorder.color = new Color(0.8f, 0.2f, 0.2f, 0.6f);
                 if (ghostText != null)
                 {
                     ghostText.text = "Release to cancel";
                     ghostText.color = new Color(1f, 0.7f, 0.7f);
                 }
                 return;
             }
             
             bool isValidTarget = (atom != null && atom.type == "Person");
             ItemType itemType = GetItemType(FileEntry);
             bool isHair = (itemType == ItemType.Hair || itemType == ItemType.HairItem);
             bool isClothing = (itemType == ItemType.Clothing || itemType == ItemType.ClothingItem);
             bool isScene = itemType == ItemType.Scene;

             UpdateGhostPosition(eventData, isValidTarget, distance);
             
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

    public static class UI
    {
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
            if (onClick != null) btn.onClick.AddListener(onClick);

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
            
            // Add Hover Border
            buttonGO.AddComponent<UIHoverBorder>();

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
