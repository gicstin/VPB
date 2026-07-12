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
        public IEnumerator RunTeleportMergedAtomsToPlayer(HashSet<string> atomsBefore)
        {
            yield return TeleportNewAtomsToPlayer(atomsBefore);
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

        private Atom TryGetSelectedSubSceneTarget()
        {
            if (Panel == null) return null;
            try
            {
                if (!Panel.IsSubSceneTargetMode()) return null;
                Atom a = Panel.SelectedTargetAtom;
                if (SceneUtils.IsSubSceneAtom(a)) return a;
            }
            catch { }
            return null;
        }

        private static void InvokeLoadSubSceneWithPath(Atom subSceneAtom, string path)
        {
            if (subSceneAtom == null) return;
            SubScene subScene = subSceneAtom.GetComponentInChildren<SubScene>();
            if (subScene == null)
            {
                LogUtil.LogError("[VPB] SubScene component not found on atom " + subSceneAtom.uid);
                return;
            }

            LogUtil.Log($"[VPB] Calling LoadSubSceneWithPath on SubScene atom {subSceneAtom.uid} with path: {path}");
            MethodInfo loadMethod = typeof(SubScene).GetMethod(
                "LoadSubSceneWithPath",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (loadMethod != null)
                loadMethod.Invoke(subScene, new object[] { path });
            else
                LogUtil.LogError("[VPB] Method LoadSubSceneWithPath not found on SubScene component");
        }

        private static void RemoveAllSubSceneAtoms()
        {
            if (SuperController.singleton == null) return;
            List<Atom> toRemove = new List<Atom>();
            foreach (var a in SuperController.singleton.GetAtoms())
            {
                if (a != null && SceneUtils.IsSubSceneAtom(a))
                    toRemove.Add(a);
            }
            if (toRemove.Count <= 0) return;
            LogUtil.Log($"[VPB] Replace mode: Removing {toRemove.Count} existing SubScenes");
            foreach (var a in toRemove)
            {
                try { SuperController.singleton.RemoveAtom(a); } catch { }
            }
        }

        private System.Collections.IEnumerator LoadSubSceneCoroutine(string path)
        {
            Atom subSceneAtom = TryGetSelectedSubSceneTarget();

            if (subSceneAtom == null)
            {
                if (Panel != null && Panel.DragDropReplaceMode)
                    RemoveAllSubSceneAtoms();

                HashSet<string> existingAtoms = new HashSet<string>();
                foreach (var a in SuperController.singleton.GetAtoms()) existingAtoms.Add(a.uid);

                yield return SuperController.singleton.AddAtomByType("SubScene", "", true, true, true);
                yield return new WaitForEndOfFrame();

                foreach (var atom in SuperController.singleton.GetAtoms())
                {
                    if (SceneUtils.IsSubSceneAtom(atom) && !existingAtoms.Contains(atom.uid))
                    {
                        subSceneAtom = atom;
                        break;
                    }
                }

                if (subSceneAtom == null)
                    LogUtil.LogError("[VPB] Could not find newly created SubScene atom");
            }

            if (subSceneAtom != null)
                InvokeLoadSubSceneWithPath(subSceneAtom, path);

            if (Panel != null)
            {
                try { Panel.RefreshTargetDropdown(); } catch { }
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
            string normalizedPath = UI.NormalizePath(path);

            string legacyPath = normalizedPath;
            int colonIndex = normalizedPath.IndexOf(":/");
            if (colonIndex >= 0)
            {
                legacyPath = normalizedPath.Substring(colonIndex + 2);
            }

            ItemType itemType = GetItemType(FileEntry);
            string ext = Path.GetExtension(normalizedPath).ToLowerInvariant();
            string appearanceMode = appearanceClothingMode;
            if (itemType == ItemType.Appearance && !string.IsNullOrEmpty(appearanceMode))
            {
                if (string.Equals(appearanceMode, "clothingonly", StringComparison.OrdinalIgnoreCase))
                    appearanceMode = "clothingOnly";
                else if (string.Equals(appearanceMode, "keep", StringComparison.OrdinalIgnoreCase))
                    appearanceMode = "keep";
                else if (string.Equals(appearanceMode, "replace", StringComparison.OrdinalIgnoreCase))
                    appearanceMode = "replace";
                else if (string.Equals(appearanceMode, "merge", StringComparison.OrdinalIgnoreCase))
                    appearanceMode = "merge";
            }
            if (string.IsNullOrEmpty(appearanceMode))
            {
                if (itemType == ItemType.Appearance)
                {
                    string cfgMode = VPBConfig.Instance != null ? VPBConfig.Instance.AppearanceClothingApplyMode : "replace";
                    if (string.IsNullOrEmpty(cfgMode)) cfgMode = "replace";
                    if (string.Equals(cfgMode, "keep", StringComparison.OrdinalIgnoreCase))
                        appearanceMode = "keep";
                    else if (string.Equals(cfgMode, "clothingonly", StringComparison.OrdinalIgnoreCase))
                        appearanceMode = "clothingOnly";
                    else
                        appearanceMode = "replace";
                }
                else
                {
                    appearanceMode = "merge";
                }
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

            bool installed;
            var movedUids = new List<string>();
            installed = UI.EnsureInstalled(FileEntry, movedUids);

            if (installed)
            {
                FileManagerBridge.Refresh("scene_merge_drop", RefreshScope.InstallOnly, movedUids, flushNativeImmediately: true);
            }

            bool shouldPrewarmOnDemand =
                itemType == ItemType.Clothing ||
                itemType == ItemType.Hair ||
                itemType == ItemType.ClothingItem ||
                itemType == ItemType.HairItem ||
                itemType == ItemType.ClothingPreset ||
                itemType == ItemType.HairPreset ||
                itemType == ItemType.Skin ||
                itemType == ItemType.Pose ||
                itemType == ItemType.Morphs;
            if (shouldPrewarmOnDemand)
            {
                try
                {
                    SceneLoadingUtils.PrewarmOnDemandPackagesForEntry(FileEntry, normalizedPath);
                    if (ShouldForcePrewarmRefreshBeforeApply(itemType))
                    {
                        // First-click reliability: if prewarm queued a coalesced refresh, run it now
                        // before one-shot preset/material lookup work starts.
                        VamOnDemandLoader.ForceRunPendingCoalescedVamRefresh("pre_apply_prewarm_flush");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogWarning("[VPB OnDemand] Asset prewarm failed: " + ex.Message);
                }
            }

            LogUtil.Log($"[DragDropDebug] Attempting to apply. FullPath: {normalizedPath}, LegacyPath: {legacyPath}, Installed: {installed}");

            JSONStorable geometry = atom.GetStorableByID("geometry");

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

                    if (true)
                    {
                        ClothingLoadingUtils.ClothingHairUndoState clothingHairSnapshot =
                            ClothingLoadingUtils.CaptureClothingHairUndoState(atom);

                        string atomUid = atom.uid;
                        Panel.PushUndo(() =>
                        {
                            Atom targetAtom = SuperController.singleton.GetAtomByUid(atomUid);
                            if (targetAtom == null)
                            {
                                LogUtil.LogError($"[Gallery] Undo failed: Atom {atomUid} not found.");
                                return;
                            }

                            ClothingLoadingUtils.RestoreClothingHairUndoState(targetAtom, clothingHairSnapshot);
                            LogUtil.Log($"[Gallery] Undo performed on {atomUid} (Clothing/Hair)");
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[Gallery] Failed to capture undo state: " + ex.Message);
                }
            }

            if (itemType == ItemType.Appearance)
            {
                ClothingApplyMode clothingMode = ClothingApplyMode.Replace;
                if (string.Equals(appearanceMode, "keep", StringComparison.OrdinalIgnoreCase))
                    clothingMode = ClothingApplyMode.Keep;
                else if (string.Equals(appearanceMode, "clothingOnly", StringComparison.OrdinalIgnoreCase))
                    clothingMode = ClothingApplyMode.ClothingOnly;
                else if (string.Equals(appearanceMode, "merge", StringComparison.OrdinalIgnoreCase))
                    clothingMode = ClothingApplyMode.Merge;

                VpbImport.LoadPreset(FileEntry, atom, VpbResourceType.Appearance, clothingMode);
                return;
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
                     ClothingLoadingUtils.ClothingWearClass droppedWearClass = ClothingLoadingUtils.ClothingWearClass.Unknown;
                     if (isClothing)
                         droppedWearClass = ClothingLoadingUtils.ClassifyClothingWearClass(
                             FileEntry != null ? FileEntry.Uid : normalizedPath, FileEntry, atom);
                     LogUtil.Log($"[DragDropDebug] Dropped regions: {string.Join(",", droppedRegions.ToArray())}, wearClass={droppedWearClass}");

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

                                 if (isClothing)
                                 {
                                     ClothingLoadingUtils.ClothingWearClass existingWearClass =
                                         ClothingLoadingUtils.ClassifyClothingWearClass(itemName, existingEntry, atom);
                                     if (!ClothingLoadingUtils.ShouldClearClothingOnReplace(droppedWearClass, existingWearClass))
                                     {
                                         if (VPBConfig.Instance.IsDevMode)
                                             LogUtil.Log($"[DragDropDebug] Preserving {paramType} {n} (wearClass={existingWearClass}, dropped={droppedWearClass}) — different clothing class.");
                                         continue;
                                     }
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

            if (itemType == ItemType.ClothingPreset || itemType == ItemType.HairPreset)
            {
                // Clothing/Hair Item Presets (.vap)
                LogUtil.Log($"[DragDropDebug] Applying {itemType}: {normalizedPath}");
                ActivateClothingHairItemPreset(atom, FileEntry, itemType == ItemType.ClothingPreset);
                return;
            }

            // Try to load as preset first (standard for Clothing/Hair presets and Poses)
            ext = Path.GetExtension(normalizedPath).ToLowerInvariant();
            if (ext == ".vap" || ext == ".json" || ext == ".vac")
            {
                string storableId = GetStorableIdForItemType(itemType);
                if (storableId != null && atom.type == "Person")
                {
                    bool isPose = itemType == ItemType.Pose;
                    PresetLockStore lockStore = new PresetLockStore();

                    if (atom.presetManagerControls != null)
                    {
                        bool lockClothing = isPose;
                        bool lockMorphs = isPose;

                        // Clear all locks, and specifically lock what we don't want changed
                        if (isPose)
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
                                // Use LoadJSONWithFallback so packages with non-standard names (e.g. spaces) can be read via stream.
                                JSONNode presetNode = UI.LoadJSONWithFallback(normalizedPath, FileEntry);
                                JSONClass presetJSON = (presetNode != null) ? presetNode.AsObject : null;
                                if (presetJSON != null)
                                {
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

                                    string ensureDepsText = presetJSON.ToString();
                                    if (FileButton.EnsureInstalledByText(ensureDepsText))
                                    {
                                        FileManagerBridge.Refresh("scene_merge_preset_deps", RefreshScope.Both);
                                    }

                                    LogUtil.Log($"[DragDropDebug] JSON loaded successfully from {normalizedPath}");

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
                                            // VPB-refactor: native atom restore, deferred from import-unification
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
                                            
                                            bool ddReplaceMode = Panel != null && Panel.DragDropReplaceMode;
                                            bool isPersonClothingPreset = itemType == ItemType.Clothing && ext == ".vap" && storableId == "ClothingPresets";
                                            ClothingApplyMode mode = ClothingApplyMode.Merge;
                                            if (itemType == ItemType.Pose) mode = ClothingApplyMode.Replace;
                                            if (ddReplaceMode && isPersonClothingPreset)
                                            {
                                                ClothingLoadingUtils.RemoveRealGarmentClothing(atom);
                                                mode = ClothingApplyMode.Replace;
                                            }

                                            VpbResourceType resType;
                                            string storableOverride = null;
                                            switch (itemType)
                                            {
                                                case ItemType.Pose:     resType = VpbResourceType.Pose;     break;
                                                case ItemType.Clothing: resType = VpbResourceType.Clothing; break;
                                                case ItemType.Hair:     resType = VpbResourceType.Hair;     break;
                                                default:
                                                    if (string.IsNullOrEmpty(storableId))
                                                    {
                                                        LogUtil.LogWarning($"[VpbImport] No storableId resolved for itemType={itemType}; aborting.");
                                                        return;
                                                    }
                                                    resType = VpbResourceType.General;
                                                    storableOverride = storableId;
                                                    break;
                                            }

                                            VpbImport.LoadPreset(FileEntry, atom, resType, mode, presetJC: presetJSON, storableNameOverride: storableOverride);
                                            presetLoaded = true;

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
                LogUtil.Log($"[DragDropDebug] Trying legacy toggle with: {legacyPath}");
                if (TryToggleLegacyClothingHairParam(geometry, legacyPath, "[DragDropDebug]")) return;

                if (normalizedPath != legacyPath)
                {
                    LogUtil.Log($"[DragDropDebug] Trying legacy toggle with full path: {normalizedPath}");
                    if (TryToggleLegacyClothingHairParam(geometry, normalizedPath, "[DragDropDebug]")) return;
                }

                // Try .vaj replacement for .vam (legacy handling)
                if (ext == ".vam")
                {
                    string vajPath = legacyPath.Substring(0, legacyPath.Length - 4) + ".vaj";
                    LogUtil.Log($"[DragDropDebug] Trying .vaj toggle with: {vajPath}");
                    if (TryToggleLegacyClothingHairParam(geometry, vajPath, "[DragDropDebug]")) return;

                    if (normalizedPath != legacyPath)
                    {
                        string vajFullPath = normalizedPath.Substring(0, normalizedPath.Length - 4) + ".vaj";
                        LogUtil.Log($"[DragDropDebug] Trying .vaj toggle with full path: {vajFullPath}");
                        if (TryToggleLegacyClothingHairParam(geometry, vajFullPath, "[DragDropDebug]")) return;
                    }
                }

                // On-demand registration can queue a delayed FileManager.Refresh; during that window
                // geometry bools are not yet populated, so first click can miss.
                // Retry briefly so the same click still succeeds once handlers finish.
                if (ShouldRetryLegacyToggle(itemType) && SuperController.singleton != null && atom != null)
                {
                    string atomUid = atom.uid;
                    SuperController.singleton.StartCoroutine(RetryLegacyToggleAfterRefreshCoroutine(atomUid, legacyPath, normalizedPath, ext));
                }
            }
            else
            {
                LogUtil.Log("[DragDropDebug] Geometry storable not found on atom.");
            }
        }

        private IEnumerator RetryLegacyToggleAfterRefreshCoroutine(string atomUid, string legacyPath, string normalizedPath, string ext)
        {
            if (string.IsNullOrEmpty(atomUid)) yield break;

            DateTime start = DateTime.UtcNow;
            bool loggedWait = false;
            while ((DateTime.UtcNow - start).TotalSeconds < 5.0)
            {
                Atom atom = null;
                try { atom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null; } catch { }
                if (atom == null) yield break;

                JSONStorable geometry = null;
                try { geometry = atom.GetStorableByID("geometry"); } catch { }
                if (geometry != null)
                {
                    if (TryToggleLegacyClothingHairParam(geometry, legacyPath, "[DragDropDebug] Deferred toggle")) yield break;
                    if (!string.Equals(normalizedPath, legacyPath, StringComparison.Ordinal)
                        && TryToggleLegacyClothingHairParam(geometry, normalizedPath, "[DragDropDebug] Deferred toggle")) yield break;

                    if (string.Equals(ext, ".vam", StringComparison.OrdinalIgnoreCase))
                    {
                        string vajPath = legacyPath.Substring(0, legacyPath.Length - 4) + ".vaj";
                        if (TryToggleLegacyClothingHairParam(geometry, vajPath, "[DragDropDebug] Deferred toggle")) yield break;
                        if (!string.Equals(normalizedPath, legacyPath, StringComparison.Ordinal))
                        {
                            string vajFullPath = normalizedPath.Substring(0, normalizedPath.Length - 4) + ".vaj";
                            if (TryToggleLegacyClothingHairParam(geometry, vajFullPath, "[DragDropDebug] Deferred toggle")) yield break;
                        }
                    }
                }

                if (!loggedWait)
                {
                    LogUtil.Log($"[DragDropDebug] Deferred toggle waiting for catalog refresh to expose geometry bools: {legacyPath}");
                    loggedWait = true;
                }

                yield return new WaitForSeconds(0.15f);
            }

            LogUtil.LogWarning($"[DragDropDebug] Deferred toggle timed out waiting for param: {legacyPath}");
        }

        private static bool TryToggleLegacyClothingHairParam(JSONStorable geometry, string path, string logPrefix)
        {
            if (geometry == null || string.IsNullOrEmpty(path)) return false;

            string paramName = "clothing:" + path;
            JSONStorableBool param = geometry.GetBoolJSONParam(paramName);
            if (param != null)
            {
                LogUtil.Log($"{logPrefix} found clothing param: {paramName}, setting to true.");
                param.val = true;
                return true;
            }

            paramName = "hair:" + path;
            param = geometry.GetBoolJSONParam(paramName);
            if (param != null)
            {
                LogUtil.Log($"{logPrefix} found hair param: {paramName}, setting to true.");
                param.val = true;
                return true;
            }

            // Fallback: some VaM builds/store variants expose bool names that don't match the file path exactly.
            // Best-effort scan for any bool whose suffix matches the provided path.
            try
            {
                string p0 = path.Replace('\\', '/');
                string p1 = p0;
                while (p1.StartsWith("/")) p1 = p1.Substring(1);
                foreach (var n in geometry.GetBoolParamNames())
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    if (!(n.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase) || n.StartsWith("hair:", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (n.EndsWith(path, StringComparison.OrdinalIgnoreCase) || n.EndsWith(p0, StringComparison.OrdinalIgnoreCase) || n.EndsWith(p1, StringComparison.OrdinalIgnoreCase))
                    {
                        var p = geometry.GetBoolJSONParam(n);
                        if (p != null)
                        {
                            LogUtil.Log($"{logPrefix} found param by suffix match: {n}, setting to true.");
                            p.val = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            LogUtil.Log($"{logPrefix} param not found: {paramName}");
            return false;
        }

        private static bool ShouldRetryLegacyToggle(ItemType itemType)
        {
            return itemType == ItemType.Clothing
                || itemType == ItemType.Hair
                || itemType == ItemType.ClothingItem
                || itemType == ItemType.HairItem;
        }

        private static bool ShouldForcePrewarmRefreshBeforeApply(ItemType itemType)
        {
            return itemType == ItemType.Appearance
                || itemType == ItemType.Clothing
                || itemType == ItemType.Hair
                || itemType == ItemType.ClothingPreset
                || itemType == ItemType.HairPreset
                || itemType == ItemType.ClothingItem
                || itemType == ItemType.HairItem;
        }

        private void CreateGhost(PointerEventData eventData)
        {
             Camera cam = dragCam != null ? dragCam : eventData.pressEventCamera;
             if (cam == null) cam = Camera.main;
             if (cam == null) return;

             ghostRenderer = null;
             ghostImg = null;
             ghostText = null;
             ghostBorder = null;

             // 8b — resolve thumbnail texture; fall back to memory cache if async load is still pending
             Texture ghostTex = GetGhostTexture();

             bool fixedMode = false;
             try { fixedMode = (Panel != null && Panel.isFixedLocally); } catch { }

             if (fixedMode)
             {
                 // Fixed-mode ghost renders in world space.
                 CreateGhostUi(ghostTex, null, true);
                 if (ghostObject != null)
                 {
                     var rt = ghostObject.GetComponent<RectTransform>();
                     if (rt != null) rt.localScale = new Vector3(0.0022f, 0.0022f, 0.0022f);
                 }
             }
             else
             {
                 Canvas rootCanvas = GetComponentInParent<Canvas>();
                 if (rootCanvas == null && Panel != null) rootCanvas = Panel.canvas;
                 CreateGhostUi(ghostTex, rootCanvas, false);
             }

             // 8b — if texture was unavailable at drag start, poll until ThumbnailImage loads it
             if (ghostTex == null) StartCoroutine(UpdateGhostTextureFromThumbnail());

             planeDistance = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);

             UpdateGhost(eventData, null, planeDistance);
        }

        private void CreateGhostUi(Texture ghostTex, Canvas parentCanvasOrNull, bool worldSpace)
        {
            try
            {
                ghostObject = new GameObject("DragGhost");

                if (worldSpace)
                {
                    ghostObject.layer = 2;
                    var wc = ghostObject.AddComponent<Canvas>();
                    wc.renderMode = RenderMode.WorldSpace;
                    wc.sortingOrder = 5000;
                    ghostObject.AddComponent<CanvasRenderer>();
                }
                else
                {
                    if (parentCanvasOrNull != null)
                    {
                        ghostObject.transform.SetParent(parentCanvasOrNull.transform, false);
                        ghostObject.layer = parentCanvasOrNull.gameObject.layer;
                        ghostObject.transform.localScale = Vector3.one;
                    }
                }

                ghostBorder = UI.AddImage(ghostObject, new Color(1, 1, 1, 0.2f), false);

                ghostText = UI.CreateLabel(ghostObject, "", GalleryUiDesignTokens.FontRef, Color.white, TextAnchor.UpperCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Overflow, name: "ActionText");
                ghostText.gameObject.AddComponent<Outline>().effectColor = Color.black;

                RectTransform textRT = ghostText.GetComponent<RectTransform>();
                textRT.anchorMin = new Vector2(0.5f, 0);
                textRT.anchorMax = new Vector2(0.5f, 0);
                textRT.pivot = new Vector2(0.5f, 1);
                textRT.anchoredPosition = new Vector2(0, -10);
                textRT.sizeDelta = new Vector2(400, 60);
                // Desktop: keep fontSize high for glyph detail, scale transform down for ~3x smaller label.
                bool vr = VPB.src.util.XrUtils.IsVrActive();
                if (!vr) textRT.localScale = new Vector3(0.3333f, 0.3333f, 1f);

                GameObject contentGO = UI.CreateChildRT(ghostObject, "Content", AnchorPresets.stretchAll);
                contentGO.layer = ghostObject.layer;
                RawImage img = contentGO.AddComponent<RawImage>();
                img.raycastTarget = false;
                img.color = ghostTex != null ? new Color(1f, 1f, 1f, 0.7f) : Color.clear;
                img.texture = ghostTex;
                ghostImg = img;

                RectTransform rt = ghostObject.GetComponent<RectTransform>();
                if (rt == null) rt = ghostObject.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(80, 80);
                rt.pivot = new Vector2(0.5f, 0.5f);

                RectTransform contentRT = contentGO.GetComponent<RectTransform>();
                contentRT.offsetMin = new Vector2(5, 5);
                contentRT.offsetMax = new Vector2(-5, -5);
            }
            catch
            {
                ghostObject = null;
                ghostBorder = null;
                ghostText = null;
                ghostImg = null;
            }
        }

        // 8b — returns the best available thumbnail texture at drag start
        private Texture GetGhostTexture()
        {
            if (ThumbnailImage != null && ThumbnailImage.texture != null)
                return ThumbnailImage.texture;

            // Thumbnail may not have loaded yet — check the memory cache directly
            if (CustomImageLoaderThreaded.singleton != null && FileEntry != null)
            {
                string imgPath = GetThumbnailImgPath();
                if (!string.IsNullOrEmpty(imgPath))
                {
                    Texture2D cached = CustomImageLoaderThreaded.singleton.GetCachedThumbnail(imgPath);
                    if (cached != null) return cached;
                }
            }
            return null;
        }

        // 8b — resolves the thumbnail image path for a FileEntry (mirrors GalleryPanel.Thumbnails.cs logic)
        private string GetThumbnailImgPath()
        {
            if (FileEntry == null) return null;
            if (FileEntry is PackageListEntry) return null;
            string p = FileEntry.Path ?? "";
            if (p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                return FileEntry.Path;
            string testJpg = Path.ChangeExtension(FileEntry.Path, ".jpg");
            if (FileManager.FileExists(testJpg)) return testJpg;
            return null;
        }

        // 8b — coroutine: watches ThumbnailImage until its texture arrives, then pushes it to the ghost
        private IEnumerator UpdateGhostTextureFromThumbnail()
        {
            float elapsed = 0f;
            const float timeout = 10f;
            while (elapsed < timeout && ghostObject != null)
            {
                Texture tex = GetGhostTexture();
                if (tex != null)
                {
                    if (ghostImg != null)
                    {
                        ghostImg.texture = tex;
                        ghostImg.color = new Color(1f, 1f, 1f, 0.7f);
                    }
                    if (ghostRenderer != null && ghostRenderer.material != null)
                    {
                        ghostRenderer.material.mainTexture = tex;
                        ghostRenderer.material.color = new Color(1f, 1f, 1f, 0.9f);
                    }
                    yield break;
                }
                yield return null;
                elapsed += Time.deltaTime;
            }
        }
        
        private void UpdateGhost(PointerEventData eventData, Atom atom, float distance)
        {
             Camera cam = dragCam != null ? dragCam : eventData.pressEventCamera;
             if (cam == null) cam = Camera.main;
             if (ghostObject == null || cam == null) return;
             
             bool isValidTarget = (atom != null && SceneUtils.IsPersonLikeAtom(atom));

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
                     string cfg = VPBConfig.Instance != null ? VPBConfig.Instance.AppearanceClothingApplyMode : "replace";
                     string line2;
                     if (string.Equals(cfg, "keep", StringComparison.OrdinalIgnoreCase))
                         line2 = "(keep body clothes)";
                     else if (string.Equals(cfg, "clothingonly", StringComparison.OrdinalIgnoreCase))
                         line2 = "(clothes only)";
                     else
                         line2 = "(preset outfit)";
                     if (isValidTarget)
                         ghostText.text = $"Apply to {atom.name}\n{line2}";
                     else
                         ghostText.text = $"Release for options\n{line2}";
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

                     bool replace = (Panel != null && Panel.DragDropReplaceMode && (isClothing || isHair));
                     if (replace)
                     {
                         string replaceScope = "";
                         if (isClothing)
                         {
                             ClothingLoadingUtils.ClothingWearClass wearClass =
                                 ClothingLoadingUtils.ClassifyClothingWearClass(FileEntry != null ? FileEntry.Uid : "", FileEntry);
                             if (wearClass == ClothingLoadingUtils.ClothingWearClass.RealGarment)
                                 replaceScope = " (garments only)";
                             else if (wearClass == ClothingLoadingUtils.ClothingWearClass.Cosmetic)
                                 replaceScope = " (cosmetics only)";
                         }
                         ghostText.text = $"Replacing {typeStr}{replaceScope} on\n" + atom.name;
                         ghostText.color = new Color(1f, 0.5f, 0.5f); // Reddish
                     }
                     else
                     {
                         string action = GetDragActionVerb(itemType, false);
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
                 if (!VPB.src.util.XrUtils.IsVrActive())
                 {
                     finalDist = Mathf.Max(distance, 0.4f);
                 }
             }

             Ray ray = cam.ScreenPointToRay(eventData.position);
             ghostObject.transform.position = ray.GetPoint(finalDist);
             ghostObject.transform.rotation = cam.transform.rotation;
        }


    }

}
