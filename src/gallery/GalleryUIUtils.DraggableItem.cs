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
    public partial class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
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


    }

}
