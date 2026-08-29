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
using VPB.src.util;

namespace VPB
{
    public partial class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
        private static string GetDragActionVerb(ItemType itemType, bool replaceMode)
        {
            if (replaceMode) return "Replacing";
            if (itemType == ItemType.Pose) return "Applying";
            return "Adding";
        }

        public FileEntry FileEntry;
        public RawImage ThumbnailImage;
        public GalleryPanel Panel;
        
        private bool? _isDualPose = null;
        private JSONNode _dualPoseNode = null;
        
        private bool isDraggingItem = false;
        private float _pointerDownTime = -1f;
        /// <summary>Time from <see cref="OnPointerDown"/> (unscaled); for tap vs hold heuristics on same row.</summary>
        public float LastPointerDownUnscaledTime => _pointerDownTime;
        private GameObject ghostObject;
        private Image ghostBorder;
        private Text ghostText; // Added text component
        private Renderer ghostRenderer;
        private RawImage ghostImg; // 8b — cached reference to ghost's RawImage for late texture update
        private GameObject groundIndicator;
        private Vector3 lastGroundPoint;
        private bool hasGroundPoint;
        // private Vector3 offset; // Unused
        private float planeDistance;
        private Camera dragCam;

        // 8c — drag overlay: blocks pointer events on side panels while dragging
        private GameObject _dragOverlay;
        public static bool IsDragging = false;

        private ScrollRect _galleryScrollRectPassthrough;

        /// <summary>True after we forwarded begin-drag to ScrollRect — item drag may still start once hold time + movement qualify.</summary>
        private bool _galleryPassthroughScrollUntilItemDrag;

        /// <summary>
        /// Screen pixels from press before a gallery row counts as intentional drag-drop (not a slow tap / micro-jitter).
        /// Unity fires <see cref="OnBeginDrag"/> near ~5–10px; below this we keep forwarding scroll until movement grows.
        /// VR: <see cref="PointerEventData.position"/> vs <see cref="PointerEventData.pressPosition"/> often barely changes for laser + world canvas; gate skipped when XR active.
        /// </summary>
        private const float GalleryMinScreenPixelsForItemDrag = 22f;

        private static bool IsXrPresentationActive()
        {
            return XrUtils.IsVrActive();
        }

        /// <summary>Non-VR: require screen-pixel slack past press. VR: rely on Unity begin-drag + optional hold only.</summary>
        private static bool PressDeltaQualifiesForGalleryItemDrag(PointerEventData eventData)
        {
            if (eventData == null) return false;
            if (IsXrPresentationActive()) return true;
            Vector2 deltaPress = (Vector2)eventData.position - eventData.pressPosition;
            float minSq = GalleryMinScreenPixelsForItemDrag * GalleryMinScreenPixelsForItemDrag;
            return deltaPress.sqrMagnitude >= minSq;
        }

        private ScrollRect ResolveGalleryScrollRectForPassthrough()
        {
            if (_galleryScrollRectPassthrough == null)
                _galleryScrollRectPassthrough = GetComponentInParent<ScrollRect>();
            return _galleryScrollRectPassthrough;
        }

        private static void ForwardPointerEventToScrollRect<T>(ScrollRect sr, PointerEventData d, ExecuteEvents.EventFunction<T> fn)
            where T : IEventSystemHandler
        {
            if (sr == null || d == null) return;
            ExecuteEvents.Execute(sr.gameObject, d, fn);
        }

        private static Dictionary<string, HashSet<string>> _globalRegionCache = new Dictionary<string, HashSet<string>>();
        private const int GlobalRegionCacheMaxEntries = 1024;

        /// <summary>Drop clothing/hair region L1 cache (package refresh / soak-test bound).</summary>
        public static void ClearGlobalRegionCache()
        {
            _globalRegionCache.Clear();
        }

        private static void PutGlobalRegionCache(string cacheKey, HashSet<string> regions)
        {
            if (string.IsNullOrEmpty(cacheKey) || regions == null) return;
            if (_globalRegionCache.Count >= GlobalRegionCacheMaxEntries
                && !_globalRegionCache.ContainsKey(cacheKey))
                _globalRegionCache.Clear();
            _globalRegionCache[cacheKey] = regions;
        }

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

        public void OnPointerDown(PointerEventData eventData)
        {
            bool isVR = XrUtils.IsVrActive();
            if (isVR || eventData.button == PointerEventData.InputButton.Left)
            {
                _galleryPassthroughScrollUntilItemDrag = false;
                _pointerDownTime = Time.unscaledTime;
            }
        }

        /// <summary>
        /// Hold delay before item drag. Desktop: 0 (movement threshold alone — click-drag must not be stolen by ScrollRect).
        /// VR: config hold (laser micro-moves otherwise start accidental item drags). 0 when DnD off.
        /// </summary>
        private static float EffectiveDragHoldSeconds()
        {
            var c = VPBConfig.Instance;
            if (c == null || !c.EffectiveEnableDragDrop) return 0f;
            if (!IsXrPresentationActive()) return 0f;
            return c.DragHoldThreshold;
        }

        public bool IsLongPress
        {
            get
            {
                if (VPBConfig.Instance != null && !VPBConfig.Instance.EffectiveEnableDragDrop) return false;
                float threshold = EffectiveDragHoldSeconds();
                if (threshold <= 0f) return false;
                return _pointerDownTime >= 0f && (Time.unscaledTime - _pointerDownTime >= threshold);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            bool isVR = XrUtils.IsVrActive();
            if (!isVR && eventData.button != PointerEventData.InputButton.Left) return;
            if (VPBConfig.Instance != null && !VPBConfig.Instance.EffectiveEnableDragDrop)
            {
                ForwardPointerEventToScrollRect(ResolveGalleryScrollRectForPassthrough(), eventData, ExecuteEvents.beginDragHandler);
                return;
            }
            float threshold = EffectiveDragHoldSeconds();
            float held = (_pointerDownTime >= 0f) ? (Time.unscaledTime - _pointerDownTime) : 0f;

            if (threshold > 0f && held < threshold)
            {
                ForwardPointerEventToScrollRect(ResolveGalleryScrollRectForPassthrough(), eventData, ExecuteEvents.beginDragHandler);
                _galleryPassthroughScrollUntilItemDrag = true;
                return;
            }
            // Hold time satisfied (or desktop: no hold) but movement still small — keep scroll until intentional drag distance.
            if (!PressDeltaQualifiesForGalleryItemDrag(eventData))
            {
                ForwardPointerEventToScrollRect(ResolveGalleryScrollRectForPassthrough(), eventData, ExecuteEvents.beginDragHandler);
                _galleryPassthroughScrollUntilItemDrag = true;
                return;
            }

            StartGalleryItemDragFromPointer(eventData);
        }

        private void StartGalleryItemDragFromPointer(PointerEventData eventData)
        {
            // If ScrollRect already owns this gesture, end it so grid does not keep scrolling under the ghost.
            if (_galleryPassthroughScrollUntilItemDrag)
                StopGalleryScrollPassthrough(eventData);

            _isDualPose = null;
            _dualPoseNode = null;
            dragCam = eventData.pressEventCamera;
            if (dragCam == null) dragCam = Camera.main;

            isDraggingItem = true;
            IsDragging = true;

            // 8c — create overlay BEFORE ghost so ghost is parented after it (renders on top)
            CreateDragOverlay();
            CreateGhost(eventData);

            string msg;
            float dist;
            Atom atom = DetectAtom(eventData, out msg, out dist);
            if (Panel != null) Panel.SetStatus(msg);
            
            UpdateGhost(eventData, atom, dist);
            _galleryPassthroughScrollUntilItemDrag = false;
        }

        /// <summary>Warm path: end ScrollRect drag + kill inertia when promoting gesture to item drag.</summary>
        private void StopGalleryScrollPassthrough(PointerEventData eventData)
        {
            ScrollRect sr = ResolveGalleryScrollRectForPassthrough();
            if (sr == null) return;
            try
            {
                if (eventData != null)
                    ForwardPointerEventToScrollRect(sr, eventData, ExecuteEvents.endDragHandler);
                sr.StopMovement();
                sr.velocity = Vector2.zero;
            }
            catch { }
            _galleryPassthroughScrollUntilItemDrag = false;
        }

        // 8c — full-screen transparent overlay that absorbs pointer events to side panels during drag
        private void CreateDragOverlay()
        {
            Canvas rootCanvas = GetComponentInParent<Canvas>();
            if (rootCanvas == null && Panel != null) rootCanvas = Panel.canvas;
            if (rootCanvas == null) return;

            _dragOverlay = UI.CreateChildRT(rootCanvas.gameObject, "DragInputBlocker", AnchorPresets.stretchAll);
            _dragOverlay.layer = rootCanvas.gameObject.layer;

            Image img = UI.AddImage(_dragOverlay, Color.clear);
        }

        private void DestroyDragOverlay()
        {
            if (_dragOverlay != null)
            {
                Destroy(_dragOverlay);
                _dragOverlay = null;
            }
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
            else if (VPBConfig.Instance != null && VPBConfig.Instance.EffectiveEnableDragDrop && _galleryPassthroughScrollUntilItemDrag)
            {
                float threshold = EffectiveDragHoldSeconds();
                float held = Time.unscaledTime - _pointerDownTime;
                if (held >= threshold && PressDeltaQualifiesForGalleryItemDrag(eventData))
                    StartGalleryItemDragFromPointer(eventData);
                else
                    ForwardPointerEventToScrollRect(ResolveGalleryScrollRectForPassthrough(), eventData, ExecuteEvents.dragHandler);
            }
            else
            {
                ForwardPointerEventToScrollRect(ResolveGalleryScrollRectForPassthrough(), eventData, ExecuteEvents.dragHandler);
            }
        }

        private void Update()
        {
            if (isDraggingItem && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)))
                CancelDrag();
        }

        private void CancelDrag()
        {
            isDraggingItem = false;
            _galleryPassthroughScrollUntilItemDrag = false;
            DestroyGhost();
            DestroyGroundIndicator();
            dragCam = null;
            if (Panel != null)
            {
                try { Panel.ClearPluginsFloatSessionDropHover(); } catch { }
                Panel.SetStatus("");
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
                    try { Panel.ClearPluginsFloatSessionDropHover(); } catch { }
                    Panel.SetStatus("");
                }

                ItemType itemType = GetItemType(FileEntry);
                
                // Handle subscenes differently - load directly without requiring atom
                if (itemType == ItemType.SubScene && FileEntry != null)
                {
                    string cat = Panel != null ? (Panel.CurrentCategoryTitle ?? "") : "";
                    if (cat.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf("Skin", StringComparison.OrdinalIgnoreCase) >= 0
                        || cat.IndexOf("Morphs", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        LogUtil.LogWarning("[VPB] Blocked SubScene drag-load under category '" + cat
                            + "' (prevents Replace wipe crash). path=" + FileEntry.Uid);
                        try
                        {
                            if (Panel != null)
                                Panel.ShowTemporaryStatus("SubScene file — use SubScene category.", 3f);
                        }
                        catch { }
                    }
                    else
                    {
                        // Do not sync-wipe SubScenes here — RemoveAtom of many SubScenes freezes the main
                        // thread. LoadSubSceneCoroutine yields removals when replace mode needs a wipe.
                        LoadSubScene(FileEntry.Uid);
                    }
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
                        // Dropped on empty space: fall back to the panel's CUA Target before spawning a new atom,
                        // so repeat picks keep replacing the same CUA instead of stacking duplicates.
                        Atom fallback = null;
                        try { if (Panel != null) fallback = Panel.ResolveCuaTargetAtom(); } catch { }
                        if (fallback != null) LoadCUAIntoAtom(fallback, FileEntry.Uid);
                        else LoadCUA(FileEntry.Uid);
                    }
                }
                else
                {
                    string msg;
                    float dist;
                    Atom atom = DetectAtom(eventData, out msg, out dist);

                    Vector3 dropPos = transform.position;
                    Camera cam = dragCam;
                    if (cam == null) cam = Camera.main;
                    if (cam != null)
                    {
                        Ray ray = cam.ScreenPointToRay(eventData.position);
                        dropPos = ray.GetPoint(dist);
                    }

                    ItemType itemTypeForDrop = itemType;

                    // Appearance on person-like → apply (fast path). Empty / non-person → context menu
                    // (spawn / apply-to-selected). Fixes empty-space appearance drops never opening menu.
                    if (itemTypeForDrop == ItemType.Appearance && FileEntry != null)
                    {
                        if (atom != null && SceneUtils.IsPersonLikeAtom(atom))
                        {
                            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "appearance"); } catch { }
                            ApplyClothingToAtom(atom, FileEntry.Uid, null);
                        }
                        else
                        {
                            HandleDropWithContext(atom, FileEntry, dropPos);
                        }
                    }
                    // Scripts: Person → person PluginManager; UI strip / void → SessionPluginManager (not Scene).
                    else if (itemTypeForDrop == ItemType.Plugins && FileEntry != null && IsPluginScriptEntry(FileEntry))
                    {
                        if (Panel != null && Panel.TryConsumePluginsFloatSessionDrop(eventData, FileEntry))
                        {
                            // Add-new or replace handled by Plugins float session strip.
                        }
                        else if (atom != null && SceneUtils.IsPersonLikeAtom(atom))
                            LoadPlugins(atom);
                        else
                            LoadPluginsAsSession();
                    }
                    else if (atom != null && FileEntry != null)
                    {
                        if (IsAmbiguousDrop(atom, FileEntry))
                        {
                            HandleDropWithContext(atom, FileEntry, dropPos);
                        }
                        else
                        {
                            string kind = "item";
                            try
                            {
                                switch (itemTypeForDrop)
                                {
                                    case ItemType.Clothing: kind = "clothing"; break;
                                    case ItemType.Hair: kind = "hair"; break;
                                    case ItemType.Skin: kind = "skin"; break;
                                    case ItemType.Morphs: kind = "morphs"; break;
                                    case ItemType.Appearance: kind = "appearance"; break;
                                    case ItemType.Pose: kind = "pose"; break;
                                    case ItemType.Plugins: kind = "plugins"; break;
                                    default: kind = "item"; break;
                                }
                            }
                            catch { kind = "item"; }
                            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), kind); } catch { }
                            ApplyClothingToAtom(atom, FileEntry.Uid);
                        }
                    }
                }
                dragCam = null;
            }
            else
            {
                ForwardPointerEventToScrollRect(ResolveGalleryScrollRectForPassthrough(), eventData, ExecuteEvents.endDragHandler);
                _galleryPassthroughScrollUntilItemDrag = false;
            }
        }

        public void OnDisable()
        {
            _galleryScrollRectPassthrough = null;
            _galleryPassthroughScrollUntilItemDrag = false;
            // Only cancel if this component still owns the active item drag. Pane auto-hide
            // used to SetActive(false) mid-drag; that path is suppressed while IsDragging.
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
                     Atom cuaTarget = null;
                     try { if (Panel != null) cuaTarget = Panel.ResolveCuaTargetAtom(); } catch { }
                     statusMsg = cuaTarget != null
                         ? $"Drop to load into {cuaTarget.name}"
                         : $"Drop to create new Custom Unity Asset";
                 }
            }
            else if (itemType == ItemType.Plugins && FileEntry != null && IsPluginScriptEntry(FileEntry))
            {
                string pluginName = !string.IsNullOrEmpty(FileEntry.Name) ? FileEntry.Name : "plugin";
                string floatDropMsg = null;
                if (Panel != null)
                    floatDropMsg = Panel.DescribePluginsFloatSessionDrop(eventData, pluginName);
                if (!string.IsNullOrEmpty(floatDropMsg))
                    statusMsg = floatDropMsg;
                else if (atom != null && SceneUtils.IsPersonLikeAtom(atom))
                    statusMsg = "Adding " + pluginName + " to " + atom.name;
                else
                    statusMsg = "Release for session: " + pluginName;
            }
            else if (atom != null && atom.type == "Person")
            {
                 bool replaceMode = (Panel != null && Panel.DragDropReplaceMode);
                 string action = GetDragActionVerb(itemType, replaceMode);
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
            // Usage recorded in LoadCUAIntoAtom when the asset is actually applied (avoid double-count).
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
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "cua"); } catch { }
            if (Panel != null) Panel.StartCoroutine(LoadCUAIntoAtomCoroutine(atom, path));
            else StartCoroutine(LoadCUAIntoAtomCoroutine(atom, path));
        }

        private System.Collections.IEnumerator LoadCUAIntoAtomCoroutine(Atom atom, string path)
        {
            string atomUid = atom.uid;
            bool installed = EnsureInstalled();
            if (installed)
            {
                FileManagerBridge.Refresh("dragdrop_cua", RefreshScope.Both, flushNativeImmediately: true);
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
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "subscene"); } catch { }
            bool installed = EnsureInstalled();

            if (installed)
            {
                FileManagerBridge.Refresh("dragdrop_subscene", RefreshScope.Both, flushNativeImmediately: true);
            }

            string normalizedPath = UI.NormalizePath(path);

            LogUtil.Log($"[VPB] LoadSubScene: {normalizedPath}");

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
                    UI.LoadSceneFile(entry, Panel);
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
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "clothing"); } catch { }
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
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "hair"); } catch { }
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
            ItemType typed = GetItemType(FileEntry);
            if (typed == ItemType.BreastPhysics)
            {
                LogUtil.LogWarning("[VPB] LoadSkin: entry is BreastPhysics — applying as breast physics, not skin.");
            }
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), typed == ItemType.Skin ? "skin" : "appearance"); } catch { }
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
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "morphs"); } catch { }
            LogUtil.Log($"[VPB] LoadMorphs: Applying {FileEntry.Name} to {target.uid}");
            ApplyClothingToAtom(target, FileEntry.Uid);
        }

        public void LoadPlugins(Atom target)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadPlugins: No target atom provided.");
                return;
            }
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "plugins"); } catch { }
            LogUtil.Log($"[VPB] LoadPlugins: Applying {FileEntry.Name} to {target.uid}");

            // Session scripts (.cs/.cslist/.dll) need CreatePlugin + URL — not PluginPresets clothing path.
            if (IsPluginScriptEntry(FileEntry))
            {
                ApplyPluginScriptToAtom(target, FileEntry);
                return;
            }

            // Plugin preset (.vap): register package under scan whitelist before PluginPresets apply.
            if (ScanWhitelistManager.Instance.IsEnabled)
            {
                try
                {
                    SceneLoadingUtils.PrewarmOnDemandPackagesForEntry(FileEntry, FileEntry != null ? FileEntry.Uid : null);
                    VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("plugin_preset_prewarm_flush");
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB] LoadPlugins: preset prewarm failed: " + ex.Message);
                }
            }
            ApplyClothingToAtom(target, FileEntry.Uid);
        }

        /// <summary>
        /// Load script onto VaM SessionPluginManager (persists across scenes). Void / non-person drops.
        /// Not ScenePluginManager — that saves with the scene only.
        /// </summary>
        public void LoadPluginsAsSession()
        {
            if (FileEntry == null) return;
            if (!IsPluginScriptEntry(FileEntry))
            {
                LogUtil.LogWarning("[VPB] LoadPluginsAsSession: not a script entry: " + FileEntry.Name);
                return;
            }
            Atom sessionAtom = GetSessionPluginAtom();
            MVRPluginManager sessionMgr = GetSessionPluginManager();
            if (sessionMgr == null)
            {
                string atomInfo = "none";
                try
                {
                    if (sessionAtom != null)
                        atomInfo = "uid=" + sessionAtom.uid + " name=" + sessionAtom.name + " type=" + sessionAtom.type;
                }
                catch { atomInfo = "err"; }
                LogUtil.LogWarning("[VPB] LoadPluginsAsSession: SessionPluginManager not found (" + atomInfo + ").");
                try
                {
                    if (Panel != null)
                        Panel.ShowTemporaryStatus(
                            VPBTranslation.T("gallery.plugins.session_missing", "Session plugins unavailable."),
                            2.5f);
                }
                catch { }
                return;
            }
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "plugins"); } catch { }
            // UIAssist/BA Start gate needs type SessionPluginManager. Older VaM keeps type CoreControl
            // on the same host — promote before CreatePlugin so script Start() sees the modern type.
            Atom hostForGate = sessionAtom ?? sessionMgr.containingAtom;
            EnsureSessionHostTypeForPluginGate(hostForGate);
            try
            {
                Atom ca = sessionMgr.containingAtom;
                LogUtil.Log("[VPB] LoadPluginsAsSession: " + FileEntry.Name
                    + " host name=" + (ca != null ? ca.name : "?")
                    + " uid=" + (ca != null ? ca.uid : "?")
                    + " type=" + (ca != null ? ca.type : "?"));
            }
            catch
            {
                LogUtil.Log("[VPB] LoadPluginsAsSession: " + FileEntry.Name);
            }
            ApplyPluginScriptToManager(sessionMgr, FileEntry, undoAtomUid: null);
            try
            {
                if (Panel != null)
                {
                    Panel.ShowTemporaryStatus(
                        VPBTranslation.T("gallery.plugins.loaded_session", "Loaded session plugin: ") + FileEntry.Name,
                        2f);
                    Panel.NotifyPluginsFloatSessionPluginsChanged();
                }
            }
            catch { }
        }

        /// <summary>
        /// Session PluginManager = Main Menu TabSessionPlugins binding, else CoreControl.PluginManager
        /// when it is NOT the ScenePluginManager instance.
        /// Scene plugins live on SuperController/ScenePluginManager — never use that for session loads.
        /// </summary>
        internal static MVRPluginManager GetSessionPluginManager()
        {
            MVRPluginManager sceneMgr = GetScenePluginManagerInstance();

            // 1) Manager whose UI is parented under TabSessionPlugins (authoritative).
            MVRPluginManager tabMgr = FindPluginManagerBoundToMainMenuTab("TabSessionPlugins");
            if (tabMgr != null && !RefEqPluginManager(tabMgr, sceneMgr))
            {
                try
                {
                    Atom ca = tabMgr.containingAtom;
                    LogUtil.Log("[VPB] GetSessionPluginManager: TabSessionPlugins"
                        + " name=" + (ca != null ? ca.name : "?")
                        + " uid=" + (ca != null ? ca.uid : "?")
                        + " type=" + (ca != null ? ca.type : "?"));
                }
                catch { }
                return tabMgr;
            }

            // 2) CoreControl.PluginManager — only if distinct from ScenePluginManager.
            Atom host = GetSessionPluginAtom();
            MVRPluginManager coreMgr = null;
            if (host != null)
            {
                try { coreMgr = host.GetStorableByID("PluginManager") as MVRPluginManager; } catch { coreMgr = null; }
                if (coreMgr == null)
                {
                    try { coreMgr = host.GetComponentInChildren<MVRPluginManager>(true); } catch { coreMgr = null; }
                }
            }

            if (coreMgr != null && RefEqPluginManager(coreMgr, sceneMgr))
            {
                try
                {
                    LogUtil.LogWarning("[VPB] GetSessionPluginManager: CoreControl.PluginManager is ScenePluginManager — skip");
                }
                catch { }
                coreMgr = null;
            }

            if (coreMgr != null)
            {
                try
                {
                    Atom ca = coreMgr.containingAtom;
                    LogUtil.Log("[VPB] GetSessionPluginManager: CoreControl.PluginManager"
                        + " name=" + (ca != null ? ca.name : "?")
                        + " uid=" + (ca != null ? ca.uid : "?")
                        + " type=" + (ca != null ? ca.type : "?")
                        + " sceneMgrSame=0");
                }
                catch { }
                return coreMgr;
            }

            try
            {
                LogUtil.LogWarning("[VPB] GetSessionPluginManager: no session manager"
                    + " tab=" + (tabMgr != null ? "1" : "0")
                    + " scene=" + (sceneMgr != null ? "1" : "0")
                    + " host=" + (host != null ? host.type : "null"));
            }
            catch { }
            return null;
        }

        internal static MVRPluginManager GetScenePluginManagerInstance()
        {
            try
            {
                if (SuperController.singleton == null) return null;
                Transform tr = SuperController.singleton.transform.Find("ScenePluginManager");
                if (tr == null) return null;
                return tr.GetComponent<MVRPluginManager>();
            }
            catch { return null; }
        }

        private static bool RefEqPluginManager(MVRPluginManager a, MVRPluginManager b)
        {
            if (a == null || b == null) return false;
            try { return object.ReferenceEquals(a, b); } catch { return false; }
        }

        /// <summary>
        /// Find MVRPluginManager whose UITransform / pluginListPanel lives under main-menu tab.
        /// </summary>
        private static MVRPluginManager FindPluginManagerBoundToMainMenuTab(string tabName)
        {
            if (string.IsNullOrEmpty(tabName) || SuperController.singleton == null) return null;
            try
            {
                UITabSelector selector = SuperController.singleton.mainMenuTabSelector;
                if (selector == null) return null;
                Transform tabTr = null;
                foreach (Transform child in selector.transform)
                {
                    if (child == null) continue;
                    if (string.Equals(child.name, tabName, StringComparison.Ordinal))
                    {
                        tabTr = child;
                        break;
                    }
                }
                if (tabTr == null) return null;

                MVRPluginManager[] all = UnityEngine.Object.FindObjectsOfType<MVRPluginManager>();
                if (all == null || all.Length == 0) return null;
                for (int i = 0; i < all.Length; i++)
                {
                    MVRPluginManager mgr = all[i];
                    if (mgr == null) continue;
                    try
                    {
                        if (mgr.pluginListPanel != null && IsTransformUnder(mgr.pluginListPanel, tabTr))
                            return mgr;
                    }
                    catch { }
                    try
                    {
                        if (mgr.UITransform != null && IsTransformUnder(mgr.UITransform, tabTr))
                            return mgr;
                    }
                    catch { }
                }

                // Fallback: manager component under the tab itself (rare).
                try { return tabTr.GetComponentInChildren<MVRPluginManager>(true); } catch { return null; }
            }
            catch { return null; }
        }

        private static bool IsTransformUnder(Transform node, Transform ancestor)
        {
            if (node == null || ancestor == null) return false;
            Transform t = node;
            while (t != null)
            {
                if (object.ReferenceEquals(t, ancestor)) return true;
                t = t.parent;
            }
            return false;
        }

        /// <summary>True if atom is CoreControl by name/uid (session plugin host).</summary>
        private static bool IsCoreControlNamed(Atom a)
        {
            if (a == null) return false;
            string n = null;
            string u = null;
            try { n = a.name; } catch { n = null; }
            try { u = a.uid; } catch { u = null; }
            return string.Equals(n, "CoreControl", StringComparison.Ordinal)
                || string.Equals(u, "CoreControl", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Session host atom: CoreControl name/uid with type SessionPluginManager (modern)
        /// or type CoreControl (legacy — GiveMeFPS).
        /// </summary>
        private static bool IsSessionPluginHostAtom(Atom a)
        {
            if (a == null || !IsCoreControlNamed(a)) return false;
            string t = null;
            try { t = a.type; } catch { return false; }
            return string.Equals(t, "SessionPluginManager", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "CoreControl", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSessionPluginManagerType(Atom a)
        {
            if (a == null) return false;
            string t = null;
            try { t = a.type; } catch { return false; }
            return string.Equals(t, "SessionPluginManager", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// UIAssist/BrowserAssist require type == SessionPluginManager. Older installs keep
        /// type CoreControl on the session host — promote so plugin Start() gates pass.
        /// Only touch the manager's containingAtom (not a random CoreControl).
        /// </summary>
        private static void EnsureSessionHostTypeForPluginGate(Atom host)
        {
            if (host == null || !IsCoreControlNamed(host)) return;
            string t = null;
            try { t = host.type; } catch { return; }
            if (string.Equals(t, "SessionPluginManager", StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.Equals(t, "CoreControl", StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                host.type = "SessionPluginManager";
                LogUtil.Log("[VPB] Session host type CoreControl → SessionPluginManager (UIAssist/BA gate)");
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] Session host type promote failed: " + ex.Message); } catch { }
            }
        }

        internal static Atom GetSessionPluginAtom()
        {
            try
            {
                if (SuperController.singleton == null) return null;

                // Prefer containingAtom of TabSessionPlugins-bound manager.
                try
                {
                    MVRPluginManager tabMgr = FindPluginManagerBoundToMainMenuTab("TabSessionPlugins");
                    MVRPluginManager sceneMgr = GetScenePluginManagerInstance();
                    if (tabMgr != null && !RefEqPluginManager(tabMgr, sceneMgr) && tabMgr.containingAtom != null)
                        return tabMgr.containingAtom;
                }
                catch { }

                // Gold: uid CoreControl (modern SessionPluginManager or legacy CoreControl type).
                Atom byUid = null;
                try { byUid = SuperController.singleton.GetAtomByUid("CoreControl"); } catch { byUid = null; }
                if (IsSessionPluginHostAtom(byUid))
                    return byUid;

                Atom typedFallback = null;
                foreach (Atom a in SuperController.singleton.GetAtoms())
                {
                    if (a == null) continue;
                    if (IsSessionPluginHostAtom(a))
                        return a;
                    if (IsSessionPluginManagerType(a) && typedFallback == null)
                        typedFallback = a;
                }

                try
                {
                    Transform container = null;
                    try { container = SuperController.singleton.atomContainer; } catch { container = null; }
                    if (container != null)
                    {
                        Atom[] kids = container.GetComponentsInChildren<Atom>(true);
                        if (kids != null)
                        {
                            for (int i = 0; i < kids.Length; i++)
                            {
                                Atom a = kids[i];
                                if (a == null) continue;
                                if (IsSessionPluginHostAtom(a))
                                    return a;
                                if (IsSessionPluginManagerType(a) && typedFallback == null)
                                    typedFallback = a;
                            }
                        }
                    }
                }
                catch { }

                if (typedFallback != null)
                    return typedFallback;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] GetSessionPluginAtom failed: " + ex.Message); } catch { }
            }
            return null;
        }

        /// <summary>True for Custom/Scripts session plugins (.cs / .cslist / .dll), including VAR-internal paths.</summary>
        private static bool IsPluginScriptEntry(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Path)) return false;
            string p = entry.Path.Replace('\\', '/');
            int varSep = p.IndexOf(":/", StringComparison.Ordinal);
            string internalPath = p;
            if (varSep >= 0 && varSep + 2 < p.Length)
            {
                // Skip Windows drive "C:/..." — real VAR sep is further along.
                if (!(varSep == 1 && char.IsLetter(p[0])))
                    internalPath = p.Substring(varSep + 2);
                else
                {
                    int varSep2 = p.IndexOf(":/", varSep + 1, StringComparison.Ordinal);
                    if (varSep2 >= 0 && varSep2 + 2 < p.Length)
                        internalPath = p.Substring(varSep2 + 2);
                }
            }
            if (internalPath.IndexOf("Custom/Scripts/", StringComparison.OrdinalIgnoreCase) < 0
                && p.IndexOf("Custom/Scripts/", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            string lower = internalPath.ToLowerInvariant();
            return lower.EndsWith(".cs") || lower.EndsWith(".cslist") || lower.EndsWith(".dll");
        }

        /// <summary>
        /// Install / on-demand whitelist-register package, then CreatePlugin + set script URL on person's PluginManager.
        /// Matches VaM UI "Add Plugin" after package is visible to native FileManager.
        /// </summary>
        private void ApplyPluginScriptToAtom(Atom atom, FileEntry entry)
        {
            if (atom == null || entry == null) return;
            MVRPluginManager mgr = null;
            try { mgr = atom.GetStorableByID("PluginManager") as MVRPluginManager; }
            catch { mgr = null; }
            if (mgr == null)
            {
                LogUtil.LogWarning("[VPB] LoadPlugins: PluginManager not found on atom " + atom.uid);
                return;
            }
            ApplyPluginScriptToManager(mgr, entry, undoAtomUid: atom.uid);
        }

        /// <summary>
        /// Shared CreatePlugin + URL path for Person PluginManager or SessionPluginManager.
        /// undoAtomUid null → undo removes from SessionPluginManager.
        /// </summary>
        private void ApplyPluginScriptToManager(MVRPluginManager mgr, FileEntry entry, string undoAtomUid)
        {
            if (mgr == null || entry == null) return;

            var movedUids = new List<string>();
            bool installed = false;
            try { installed = UI.EnsureInstalled(entry, movedUids); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB] LoadPlugins: EnsureInstalled failed: " + ex.Message); }
            if (installed)
            {
                try
                {
                    FileManagerBridge.Refresh("plugin_script_install", RefreshScope.InstallOnly, movedUids, flushNativeImmediately: true);
                }
                catch { }
            }

            string pluginUrl = ResolvePluginScriptUrl(entry);
            if (string.IsNullOrEmpty(pluginUrl))
            {
                LogUtil.LogWarning("[VPB] LoadPlugins: could not resolve plugin URL for " + entry.Name);
                return;
            }

            if (ScanWhitelistManager.Instance.IsEnabled)
            {
                try
                {
                    SceneLoadingUtils.PrewarmOnDemandPackagesForEntry(entry, pluginUrl, queueCoalescedRefresh: true);
                    VamOnDemandLoader.TryRegisterPackageOnDemandForEntryPath(pluginUrl);
                    pluginUrl = VamOnDemandLoader.RewriteEntryPathToBestAvailable(pluginUrl, attemptRegister: true);
                    VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("plugin_script_prewarm_flush");
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB] LoadPlugins: on-demand register failed: " + ex.Message);
                }
            }

            MVRPlugin plugin = null;
            try { plugin = mgr.CreatePlugin(); }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] LoadPlugins: CreatePlugin failed: " + ex.Message);
                return;
            }
            if (plugin == null || plugin.pluginURLJSON == null)
            {
                LogUtil.LogWarning("[VPB] LoadPlugins: CreatePlugin returned empty plugin slot.");
                return;
            }

            string pluginSlotUid = null;
            try { pluginSlotUid = plugin.uid; } catch { pluginSlotUid = null; }

            try
            {
                plugin.pluginURLJSON.val = pluginUrl;
                LogUtil.Log("[VPB] LoadPlugins: loaded script slot=" + (pluginSlotUid ?? "?")
                    + " url=" + pluginUrl
                    + (string.IsNullOrEmpty(undoAtomUid) ? " (session)" : " atom=" + undoAtomUid));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] LoadPlugins: setting plugin URL failed: " + ex.Message);
                try
                {
                    if (!string.IsNullOrEmpty(pluginSlotUid))
                        mgr.RemovePluginWithUID(pluginSlotUid);
                }
                catch { }
                return;
            }

            // Person drop closure (session toast lives in LoadPluginsAsSession).
            if (Panel != null && !string.IsNullOrEmpty(undoAtomUid))
            {
                try
                {
                    string pluginName = !string.IsNullOrEmpty(entry.Name) ? entry.Name : "plugin";
                    string personName = undoAtomUid;
                    try
                    {
                        Atom a = SuperController.singleton != null
                            ? SuperController.singleton.GetAtomByUid(undoAtomUid)
                            : null;
                        if (a != null && !string.IsNullOrEmpty(a.name))
                            personName = a.name;
                    }
                    catch { }
                    Panel.ShowTemporaryStatus(
                        string.Format(
                            VPBTranslation.T("gallery.plugins.added_to_person", "Added {0} to {1}"),
                            pluginName, personName),
                        2f);
                }
                catch { }
            }

            if (Panel == null || string.IsNullOrEmpty(pluginSlotUid)) return;
            string removeUid = pluginSlotUid;
            string atomUid = undoAtomUid;
            try
            {
                Panel.PushUndo(() =>
                {
                    MVRPluginManager undoMgr = null;
                    if (!string.IsNullOrEmpty(atomUid))
                    {
                        Atom targetAtom = SuperController.singleton != null
                            ? SuperController.singleton.GetAtomByUid(atomUid)
                            : null;
                        if (targetAtom == null) return;
                        undoMgr = targetAtom.GetStorableByID("PluginManager") as MVRPluginManager;
                    }
                    else
                    {
                        undoMgr = GetSessionPluginManager();
                    }
                    if (undoMgr == null) return;
                    try { undoMgr.RemovePluginWithUID(removeUid); }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning("[VPB] LoadPlugins undo failed: " + ex.Message);
                    }
                });
            }
            catch { }
        }

        private static string ResolvePluginScriptUrl(FileEntry entry)
        {
            if (entry == null) return null;
            string url = null;
            try { url = entry.Uid; } catch { url = null; }
            if (string.IsNullOrEmpty(url))
            {
                try { url = entry.Path; } catch { url = null; }
            }
            if (string.IsNullOrEmpty(url)) return null;
            url = UI.NormalizePath(url);
            // Prefer concrete package-internal form when Path is a loose .var display path.
            if (url.IndexOf(":/", StringComparison.Ordinal) < 0
                && entry is VarFileEntry vfe
                && vfe.Package != null
                && !string.IsNullOrEmpty(vfe.Package.Uid)
                && !string.IsNullOrEmpty(vfe.InternalPath))
            {
                url = vfe.Package.Uid + ":/" + vfe.InternalPath.Replace('\\', '/');
            }
            return url;
        }

        public void LoadAppearance(Atom target, string mode = null)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadAppearance: No target atom provided.");
                return;
            }
            // Defensive: Appearance gallery must not apply Pose/*.vap (Actions routing + path typing).
            ItemType typed = GetItemType(FileEntry);
            if (typed == ItemType.Pose)
            {
                LogUtil.LogWarning("[VPB] LoadAppearance: entry is Pose — routing to LoadPose instead of overwriting look as pose.");
                LoadPose(target);
                return;
            }
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "appearance"); } catch { }
            string cfgAppearanceClothing = VPBConfig.Instance != null ? VPBConfig.Instance.AppearanceClothingApplyMode : "replace";
            LogUtil.Log($"[VPB] LoadAppearance: Applying {FileEntry.Name} to {target.uid} (explicitMode: {mode ?? "<resolve>"}, AppearanceClothingCfg={cfgAppearanceClothing})");
            ApplyClothingToAtom(target, FileEntry.Uid, mode);
        }

        /// <summary>
        /// Applies a pose. Returns true only when a single-person pose actually landed on
        /// <paramref name="target"/>: a pose built for two is not one body's business, so it is
        /// handed to the cast window instead and this reports false.
        /// <paramref name="interactive"/> false refuses a two-person pose outright rather than
        /// asking - the peer's side of a session has nobody to ask.
        /// </summary>
        public bool LoadPose(Atom target, bool suppressRoot = true, bool interactive = true)
        {
            if (target == null)
            {
                LogUtil.LogWarning("[VPB] LoadPose: No target atom provided.");
                return false;
            }
            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(FileEntry), "pose"); } catch { }

            string normalizedPath = UI.NormalizePath(FileEntry.Path);
            LogUtil.Log($"[VPB] LoadPose: Applying {FileEntry.Name} to {target.uid} (SuppressRoot: {suppressRoot})");

            // Use LoadJSONWithFallback instead of SuperController.LoadJSON directly:
            // some .var packages have spaces in their name (e.g. "infiniteya.Pose Pack.1")
            // which VAM's native LoadJSON cannot resolve from a UID path, but VPB can read
            // directly from the ZipFile stream via FileEntry.OpenStreamReader().
            JSONNode node = UI.LoadJSONWithFallback(normalizedPath, FileEntry);
            if (node == null) return false;
            JSONClass presetJSON = node.AsObject;

            // A pose built for two describes two bodies. Which body takes which half is a
            // question, not a guess - the cast window asks it, here and across a session alike.
            if (VpbDualPose.LooksDual(presetJSON))
            {
                string dualWhy;
                VpbDualPoseFile dual = VpbDualPose.Parse(
                    VpbDualPose.EntryReference(FileEntry) ?? normalizedPath, presetJSON, out dualWhy);
                if (dual != null)
                {
                    if (!interactive)
                    {
                        LogUtil.LogWarning("[VPB] LoadPose: " + FileEntry.Name
                            + " is a two-person pose and nobody can be asked who takes which half here;"
                            + " not applying it.");
                        return false;
                    }
                    LogUtil.Log("[VPB] LoadPose: two-person pose, asking who takes which half.");
                    VpbDualPoseModal.Show(dual, target);
                    return false;
                }
                LogUtil.LogWarning("[VPB] LoadPose: " + FileEntry.Name
                    + " looked like a two-person pose but " + (dualWhy ?? "could not be split")
                    + "; applying it as a single pose.");
            }

            // After the two-person branch, not before it. A pose saved in raw atom format names
            // every package the whole Person was wearing, and registering those took nine seconds
            // and 115 packages on the first pose of a session - long enough for the other machine
            // to decide this one had died and tear the session down, which is exactly why the
            // FIRST two-person pose never reached the other side and the second always did. A
            // two-person pose is restored physical-only and never touches one of those references.
            if (ScanWhitelistManager.Instance.IsEnabled)
            {
                try { SceneLoadingUtils.PrewarmOnDemandPackagesForEntry(FileEntry, normalizedPath); }
                catch { }
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
                    return false;
                }
            }

            // Check whether the storables contain a PosePresets entry (standard VaM preset format)
            // or are raw bone storables (atom-save format). Raw bone storables must be applied via
            // atom.Restore(); passing them to PresetManager.LoadPresetFromJSON silently does nothing.
            bool hasPosePresetsStorable = false;
            JSONArray storablesArr = presetJSON["storables"] as JSONArray;
            if (storablesArr != null)
            {
                for (int i = 0; i < storablesArr.Count; i++)
                {
                    JSONClass s = storablesArr[i] as JSONClass;
                    if (s != null && s["id"].Value == "PosePresets") { hasPosePresetsStorable = true; break; }
                }
            }


            if (!hasPosePresetsStorable && storablesArr != null)
            {
                // VPB-refactor: native atom restore, deferred from import-unification
                LogUtil.Log($"[VPB] LoadPose: No PosePresets storable found; using atom.Restore() for raw storables.");
                target.PreRestore(true, false);
                if (!suppressRoot) target.RestoreTransform(presetJSON);
                target.Restore(presetJSON, true, false, false);
                target.LateRestore(presetJSON, true, false, false);
                target.PostRestore(true, false);
                return true;
            }

            FileEntry entry = FileEntry ?? VPB.FileManager.GetFileEntry(normalizedPath);
            if (entry == null)
            {
                LogUtil.LogWarning($"[VPB] LoadPose: could not resolve FileEntry for {normalizedPath}");
                return false;
            }
            VpbImport.LoadPreset(entry, target, VpbResourceType.Pose, ClothingApplyMode.Replace,
                                 presetJC: presetJSON, suppressRoot: suppressRoot);
            return true;
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

        // VaM SetActiveClothingItem / SetActiveHairItem is (id|item, bool active, bool fromRestore).
        // Pad fromRestore=false when the resolved MethodInfo has 3+ parameters.
        private static void InvokeSetActiveItem(MethodInfo mi, object dcs, object itemOrUid, bool active)
        {
            if (mi == null || dcs == null) return;
            ParameterInfo[] ps = mi.GetParameters();
            if (ps != null && ps.Length >= 3)
                mi.Invoke(dcs, new object[] { itemOrUid, active, false });
            else
                mi.Invoke(dcs, new object[] { itemOrUid, active });
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
                                InvokeSetActiveItem(miSetActiveItem, dcs, item, false);
                            }
                            else if (miSetActiveItemByUid != null)
                            {
                                InvokeSetActiveItem(miSetActiveItemByUid, dcs, item.uid, false);
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
                    // VaM: SetActiveClothingItem(item|uid, bool active, bool fromRestore = optional)
                    if (ps.Length >= 2 && ps[1].ParameterType == typeof(bool))
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
                    InvokeSetActiveItem(miSetActiveItem, dcs, matched, false);
                }
                else if (miSetActiveItemByUid != null)
                {
                    InvokeSetActiveItem(miSetActiveItemByUid, dcs, matched.uid, false);
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
                        InvokeSetActiveItem(miSetActiveItem, dcs, matched, true);
                        InvokeSetActiveItem(miSetActiveItem, dcs, matched, false);
                    }
                    else if (miSetActiveItemByUid != null)
                    {
                        LogUtil.Log("[VPB] RemoveClothingItemByUid: item already inactive; attempting force refresh via SetActiveClothingItem(uid, true->false)");
                        InvokeSetActiveItem(miSetActiveItemByUid, dcs, matched.uid, true);
                        InvokeSetActiveItem(miSetActiveItemByUid, dcs, matched.uid, false);
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

            ClothingLoadingUtils.RemoveAllHair(target);
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
                    InvokeSetActiveItem(miSetActiveItem, dcs, matched, false);
                }
                else if (miSetActiveItemByUid != null)
                {
                    InvokeSetActiveItem(miSetActiveItemByUid, dcs, itemUid, false);
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
            MergeSceneFile(path, UI.SceneAddMode.FullMerge, atPlayer);
        }

        public void MergeSceneFile(string path, UI.SceneAddMode mode, bool atPlayer = false)
        {
            try
            {
                FileEntry entryForPath = null;
                try { entryForPath = VPB.FileManager.GetFileEntry(path); } catch { }
                if (entryForPath == null) entryForPath = FileEntry;
                UI.MergeSceneFile(entryForPath, path, Panel, mode, atPlayer, this);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] MergeSceneFile error: " + ex.Message);
            }
        }


    }

}
