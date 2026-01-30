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


    }

}
