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

                // Suppress gallery auto-refresh to preserve scroll position and state
                // Must activate BEFORE EnsureInstalledByText since it may trigger FileManager.Refresh internally
                // NOTE: Suppression is disabled after a delay via coroutine to allow merging to complete
                try
                {
                    Gallery.SuppressAutoRefresh(true);
                    
                    var movedUids = new List<string>();
                    if (FileButton.EnsureInstalledByText(root.ToString(), movedUids))
                    {
                        MVR.FileManagement.FileManager.Refresh();
                        if (movedUids.Count > 0)
                            FileManager.NotifyInstalled(movedUids);
                    }
                    
                    // Start coroutine to disable suppression after scene merge completes
                    if (Messager.singleton != null)
                    {
                        Messager.singleton.StartCoroutine(UI.DisableSuppressionAfterDelay(2f));
                    }
                    else
                    {
                        Gallery.SuppressAutoRefresh(false);
                    }
                }
                catch (Exception refreshEx)
                {
                    LogUtil.LogError($"[VPB] FileManager refresh error during scene merge: {refreshEx.Message}");
                    Gallery.SuppressAutoRefresh(false);
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

                // Keep LateRestore on next frame and reset sim clothing.
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

        /// <summary>Pending data for deferred clothing-only sub-preset/material restore (applied after items finish loading).</summary>
        private sealed class PendingClothingOnlySubpresetState
        {
            public string AtomUid;
            public List<KeyValuePair<string, JSONClass>> StorablesToApply;
            public JSONClass ClothingPresetsJson;
            public string LoadDirPath;
        }

        private static IEnumerator DelayedClothingOnlySubpresetRestoreCoroutine(PendingClothingOnlySubpresetState state)
        {
            if (state == null || state.StorablesToApply == null || state.StorablesToApply.Count == 0) yield break;
            // Wait a few frames for item loading to start, then poll until at least one storable appears.
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            Atom a = null;
            try { a = SuperController.singleton?.GetAtomByUid(state.AtomUid); } catch { }
            if (a == null) yield break;

            // Poll up to 12 seconds for at least one garment storable to be registered on the atom.
            DateTime startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalSeconds < 12)
            {
                bool anyFound = false;
                for (int si = 0; si < state.StorablesToApply.Count; si++)
                {
                    JSONStorable st = null;
                    try { st = a.GetStorableByID(state.StorablesToApply[si].Key); } catch { }
                    if (st != null) { anyFound = true; break; }
                }
                if (anyFound) break;
                yield return new WaitForEndOfFrame();
            }

            // Re-apply ClothingPresets first so the sub-preset selection is current when material storables are restored.
            if (state.ClothingPresetsJson != null)
            {
                JSONStorable cps = null;
                try { cps = a.GetStorableByID("ClothingPresets"); } catch { }
                if (cps != null)
                {
                    try
                    {
                        try { MVR.FileManagement.FileManager.PushLoadDirFromFilePath(state.LoadDirPath); } catch { }
                        cps.RestoreFromJSON(state.ClothingPresetsJson);
                    }
                    catch { }
                    finally { try { MVR.FileManagement.FileManager.PopLoadDir(); } catch { } }
                }
            }

            int count = 0;
            try { MVR.FileManagement.FileManager.PushLoadDirFromFilePath(state.LoadDirPath); } catch { }
            try
            {
                for (int si = 0; si < state.StorablesToApply.Count; si++)
                {
                    var kv = state.StorablesToApply[si];
                    JSONStorable cs = null;
                    try { cs = a.GetStorableByID(kv.Key); } catch { }
                    if (cs != null) { try { cs.RestoreFromJSON(kv.Value); count++; } catch { } }
                }
            }
            finally { try { MVR.FileManagement.FileManager.PopLoadDir(); } catch { } }
            LogUtil.Log($"[DragDropDebug][ClothingOnly] delayed subpreset storables applied: {count}");
        }

        /// <summary>Snapshot for "keep garments" appearance apply: body clothes only; hair/makeup load from preset.</summary>
        private sealed class KeepGarmentAppearanceState
        {
            public Dictionary<string, bool> GarmentGeometryBools;
            public List<KeyValuePair<string, JSONClass>> GarmentClothingItemJsons;
            /// <summary>ClothingPresets storable: which sub-preset / variant is active per item.</summary>
            public JSONClass ClothingPresetsJson;
        }

        /// <summary>Snapshot for "clothes only" appearance apply: captures body/morphs/hair to restore after applying the full preset (clothing comes from preset).</summary>
        private sealed class KeepBodyAppearanceState
        {
            /// <summary>geometry storable JSON with <c>clothing:</c> keys stripped — only body/morph/hair params kept.</summary>
            public JSONClass BodyGeometryJson;
            /// <summary>HairPresets storable JSON to restore original hair after the full preset is applied.</summary>
            public JSONClass HairPresetsJson;
            /// <summary>
            /// Clothing bool state captured from the atom's geometry immediately after the full preset is applied.
            /// Merged back into <see cref="BodyGeometryJson"/> during the body restore so that VaM's
            /// <c>setMissingToDefault=true</c> does not reset the newly-applied clothing to off.
            /// </summary>
            public Dictionary<string, bool> ClothingBoolsFromPreset = null;
        }

        private static readonly HashSet<string> GarmentClothingRegionTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "torso", "hip", "arms", "hands", "legs", "feet", "neck", "full body"
        };

        private static readonly string[] s_cosmeticKeywords =
        {
            "lash", "lashes", "makeup", "shadow", "liner", "eyeliner", "eyeshadow", "eyesdw", "brow",
            "gloss", "lipstick", "lip_", "lips", "nail", "toenail", "piercing", "earring",
            "reflection", "eye_reflection", "eyes_reflection", "tears", "tear",
            "teeth", "head flower", "glasses", "tattoo", "decal", "lash_female", "lash_male",
            "wetline", "wet_line", "eyelid", "cornea", "sclera", "contact", "overlay",
            "iris_tex", "eyeball", "pupil"
        };

        private static readonly string[] s_garmentKeywords =
        {
            "pant", "skirt", "short", "shirt", "top", "bra", "dress", "suit", "jacket", "coat",
            "sock", "shoe", "boot", "heel", "glove", "underwear", "thong", "brief", "jean",
            "bodysuit", "sweater", "stocking", "lingerie", "bikini", "bottom", "corset", "panty",
            "winter_clothes", "costume_o", "jean_short", "sandals", "uggs", "baggy",
            "outfit", "apron", "vest", "legging", "hoodie", "costume", "uniform", "robe", "cloak",
            "tunic", "kimono", "yukata", "leotard", "catsuit", "jumpsuit", "overalls", "sarong",
            "tights", "cardigan", "blazer", "necker", "scarf", "belt", "harness", "swimwear",
            "swimsuit", "idol", "nighty", "nightie", "pajama", "pyjama"
        };

        private static string PackageUidFirstSegment(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";
            int c = uid.IndexOf(':');
            return c > 0 ? uid.Substring(0, c) : uid;
        }

        private static bool ClothingGeometryUrlsLikelyMatch(string storableUrl, string geometryUid)
        {
            if (string.IsNullOrEmpty(storableUrl) || string.IsNullOrEmpty(geometryUid)) return false;
            if (string.Equals(storableUrl, geometryUid, StringComparison.OrdinalIgnoreCase)) return true;
            if (storableUrl.IndexOf(geometryUid, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (geometryUid.IndexOf(storableUrl, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string ps = PackageUidFirstSegment(storableUrl);
            string pg = PackageUidFirstSegment(geometryUid);
            if (!string.IsNullOrEmpty(ps) && string.Equals(ps, pg, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ExtractClothingUrlFromStorableJson(JSONClass jc)
        {
            if (jc == null) return null;
            try
            {
                if (jc["url"] != null && !string.IsNullOrEmpty(jc["url"].Value)) return jc["url"].Value;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Extracts a "Creator:AssetFileBase" prefix from a clothing UID and adds it to the set.
        /// UID format: "PackageName.Creator.version:/path/to/Asset.vam"
        /// Resulting prefix matches preset storables like "Creator:AssetFileBaseMaterialCombined".
        /// </summary>
        private static void BuildGarmentPrefix(string uid, HashSet<string> prefixes)
        {
            if (string.IsNullOrEmpty(uid)) return;
            int colonIdx = uid.IndexOf(':');
            if (colonIdx < 0) return;
            string pkgPart = uid.Substring(0, colonIdx);
            string pathPart = uid.Substring(colonIdx + 1);
            int dotIdx = pkgPart.IndexOf('.');
            string creator = dotIdx > 0 ? pkgPart.Substring(0, dotIdx) : pkgPart;
            string assetBase = Path.GetFileNameWithoutExtension(pathPart);
            if (!string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(assetBase))
                prefixes.Add(creator + ":" + assetBase);
        }

        private static bool IsCosmeticClothingUidHeuristic(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            string s = uid.ToLowerInvariant();
            for (int i = 0; i < s_cosmeticKeywords.Length; i++)
            {
                if (s.Contains(s_cosmeticKeywords[i])) return true;
            }
            // Check if any package name segment (before first colon) is or starts with "eye"/"eyes".
            // e.g. "Hunting-Succubus.Eye_WetLine_Test.1:..." → segment "eye" → cosmetic.
            // e.g. "xxxa.EyeSDW.latest:..." → segment "eyesdw" starts with "eye" → cosmetic.
            int colonIdx = s.IndexOf(':');
            string packagePart = colonIdx >= 0 ? s.Substring(0, colonIdx) : s;
            string[] segs = packagePart.Split(new char[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segs.Length; i++)
            {
                string seg = segs[i];
                if (seg == "eye" || seg == "eyes") return true;
                // Catch "eyesdw", "eyelid", "eyeliner", "eyeshadow", etc. as package name segments
                if (seg.StartsWith("eye") && seg != "eyelet") return true;
            }
            return false;
        }

        private static bool IsGarmentClothingUidHeuristicPositive(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            string s = uid.ToLowerInvariant();
            for (int i = 0; i < s_garmentKeywords.Length; i++)
            {
                if (s.Contains(s_garmentKeywords[i])) return true;
            }
            return false;
        }

        /// <summary>True when UID points at VaM female/male clothing assets (not hair/morphs).</summary>
        private static bool IsClothingAssetPathInUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            int colon = uid.IndexOf(':');
            string pathPart = colon >= 0 ? uid.Substring(colon + 1) : uid;
            if (pathPart.IndexOf("/custom/clothing/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (pathPart.IndexOf("\\custom\\clothing\\", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private bool IsGarmentClothingUid(string itemUid)
        {
            if (string.IsNullOrEmpty(itemUid)) return false;
            if (IsCosmeticClothingUidHeuristic(itemUid)) return false;
            // Check region data FIRST — it is the most reliable signal.
            // (Previously IsClothingAssetPathInUid ran here and short-circuited the region check,
            // causing makeup items tagged "head" to be misclassified as garments.)
            try
            {
                VarFileEntry e = FileManager.GetVarFileEntry(itemUid);
                if (e != null)
                {
                    HashSet<string> regions = GetClothingRegions(e);
                    if (regions != null && regions.Count > 0)
                    {
                        foreach (string r in regions)
                        {
                            if (GarmentClothingRegionTags.Contains(r)) return true;
                        }
                        // Has region data but no garment region → cosmetic/accessory
                        return false;
                    }
                }
            }
            catch { }
            // No region data available — fall back to path/keyword heuristics
            if (IsClothingAssetPathInUid(itemUid)) return true;
            return IsGarmentClothingUidHeuristicPositive(itemUid);
        }

        private static bool IsHairOrHairPresetStorableId(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            if (string.Equals(sid, "HairPresets", StringComparison.OrdinalIgnoreCase)) return true;
            if (sid.IndexOf("hairItem#", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (sid.StartsWith("hairItem", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private bool ShouldDropStorableFromKeepGarmentPreset(JSONClass storable)
        {
            if (storable == null) return false;
            string id = storable["id"]?.Value ?? "";
            if (IsHairOrHairPresetStorableId(id)) return false;
            if (string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase)) return true;
            if (id.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase) >= 0 || id.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase))
            {
                string url = ExtractClothingUrlFromStorableJson(storable);
                if (string.IsNullOrEmpty(url)) return true;
                return IsGarmentClothingUid(url);
            }
            if (id.StartsWith("wearable", StringComparison.OrdinalIgnoreCase))
            {
                string url = ExtractClothingUrlFromStorableJson(storable);
                if (string.IsNullOrEmpty(url)) return true;
                return IsGarmentClothingUid(url);
            }
            if (id.StartsWith("clothing", StringComparison.OrdinalIgnoreCase) && id.IndexOf("Item", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
            return false;
        }

        /// <summary>Dependency scan text for EnsureInstalledByText so clothes-only does not pull VAR names from hair/morph/flower keys in full geometry JSON.</summary>
        private static string BuildDependencyScanTextForAppearanceClothesOnly(JSONClass presetJSON)
        {
            if (presetJSON == null) return "";
            JSONArray arr = presetJSON["storables"] != null ? presetJSON["storables"].AsArray : null;
            if (arr == null) return presetJSON.ToString();
            System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
            foreach (JSONNode node in arr)
            {
                JSONClass sn = node as JSONClass;
                if (sn == null) continue;
                string id = sn["id"]?.Value ?? "";
                if (string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase)
                    || id.IndexOf("clothingItem", StringComparison.OrdinalIgnoreCase) >= 0
                    || id.StartsWith("wearable", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(sn.ToString());
                    sb.Append(' ');
                    continue;
                }
                if (!string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string key in sn.Keys)
                {
                    if (string.Equals(key, "id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                    string uid = key.Length > 9 ? key.Substring(9) : "";
                    if (IsCosmeticClothingUidHeuristic(uid)) continue;
                    sb.Append(uid);
                    sb.Append(' ');
                    try { sb.Append(sn[key].ToString()); } catch { }
                    sb.Append(' ');
                }
            }
            if (sb.Length < 8) return presetJSON.ToString();
            return sb.ToString();
        }

        private bool ShouldIncludeStorableForClothingOnlyApply(JSONClass storable)
        {
            if (storable == null) return false;
            string id = storable["id"]?.Value ?? "";
            if (IsHairOrHairPresetStorableId(id)) return false;
            if (string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase)) return true;
            if (id.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase) >= 0 || id.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase))
            {
                string url = ExtractClothingUrlFromStorableJson(storable);
                if (string.IsNullOrEmpty(url)) return true;
                return IsGarmentClothingUid(url);
            }
            if (id.StartsWith("wearable", StringComparison.OrdinalIgnoreCase))
            {
                string url = ExtractClothingUrlFromStorableJson(storable);
                if (string.IsNullOrEmpty(url)) return true;
                return IsGarmentClothingUid(url);
            }
            // Modern .vap files use the full clothing UID as the storable ID (stores subpreset/color/texture config)
            if (IsClothingAssetPathInUid(id)) return IsGarmentClothingUid(id);
            if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Reduces an appearance preset JSON to garment clothing only (body outfit), for merge load without replacing face/hair/morphs.
        /// </summary>
        /// <summary>Strip clothing: and hair: boolParams from the geometry storable in a preset, so Keep mode doesn't load new clothing.</summary>
        private void StripClothingFromPresetGeometry(JSONClass presetJSON)
        {
            if (presetJSON == null || presetJSON["storables"] == null) return;
            JSONArray storables = presetJSON["storables"].AsArray;
            if (storables == null) return;
            foreach (JSONNode node in storables)
            {
                JSONClass sn = node as JSONClass;
                if (sn == null) continue;
                string id = sn["id"]?.Value ?? "";
                if (!string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase)) continue;
                JSONArray boolParams = sn["boolParams"]?.AsArray;
                if (boolParams != null)
                {
                    JSONArray kept = new JSONArray();
                    foreach (JSONNode bp in boolParams)
                    {
                        string name = bp["name"]?.Value ?? "";
                        if (!name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase) &&
                            !name.StartsWith("hair:", StringComparison.OrdinalIgnoreCase))
                            kept.Add(bp);
                    }
                    sn["boolParams"] = kept;
                }
                break;
            }
        }

        private void FilterAppearancePresetToGarmentClothingOnly(JSONClass presetJSON)
        {
            if (presetJSON == null || presetJSON["storables"] == null) return;
            JSONArray storables = presetJSON["storables"].AsArray;
            if (storables == null) return;
            JSONArray filtered = new JSONArray();
            foreach (JSONNode node in storables)
            {
                JSONClass sn = node as JSONClass;
                if (sn == null) continue;
                string id = sn["id"]?.Value ?? "";
                if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase))
                {
                    JSONClass geomCopy = CloneJsonClass(sn);
                    if (geomCopy != null) filtered.Add(geomCopy);
                    continue;
                }
                if (ShouldIncludeStorableForClothingOnlyApply(sn))
                {
                    JSONClass copy = CloneJsonClass(sn);
                    if (copy != null) filtered.Add(copy);
                }
            }
            presetJSON["storables"] = filtered;
        }

        /// <summary>Clear outfit <c>clothing:</c> toggles before clothes-only apply; skips cosmetic-type UIDs (lashes, nails, etc.).</summary>
        private void ClearAtomGeometryGarmentClothingBools(Atom atom)
        {
            if (atom == null) return;
            JSONStorable geometry = atom.GetStorableByID("geometry");
            if (geometry == null) return;
            foreach (string name in geometry.GetBoolParamNames())
            {
                if (!name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                string uid = name.Length > 9 ? name.Substring(9) : "";
                // Only clear items positively identified as garments; anything else (makeup, accessories, ambiguous) is preserved.
                if (!IsGarmentClothingUid(uid)) continue;
                JSONStorableBool p = geometry.GetBoolJSONParam(name);
                if (p != null) p.val = false;
            }
        }

        /// <summary>
        /// Overlay preset <c>clothing:</c> params onto the atom's current geometry JSON, then RestoreFromJSON once.
        /// Per-key bool updates miss slots not yet on the person; full overlay preserves VaM JSON shape (e.g. nested val).
        /// </summary>
        private void MergeGarmentClothingGeometryBoolsFromPresetJson(Atom atom, JSONClass geometryJsonFromPreset)
        {
            if (atom == null || geometryJsonFromPreset == null) return;
            JSONStorable geom = atom.GetStorableByID("geometry");
            if (geom == null) return;

            JSONNode rawBase = null;
            try { rawBase = geom.GetJSON(); } catch { }
            if (rawBase == null)
            {
                LogUtil.LogWarning("[DragDropDebug] Appearance clothes-only: geometry.GetJSON() null; cannot merge outfit");
                return;
            }

            JSONClass mergedBase;
            try { mergedBase = JSON.Parse(rawBase.ToString()).AsObject; }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[DragDropDebug] Appearance clothes-only: clone geometry JSON failed: " + ex.Message);
                return;
            }

            int clothingColonInPreset = 0;
            int patched = 0;
            foreach (string key in geometryJsonFromPreset.Keys)
            {
                if (string.Equals(key, "id", StringComparison.OrdinalIgnoreCase)) continue;
                if (!key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                clothingColonInPreset++;
                string uid = key.Length > 9 ? key.Substring(9) : "";
                if (IsCosmeticClothingUidHeuristic(uid)) continue;
                JSONNode srcVal = geometryJsonFromPreset[key];
                if (srcVal == null) continue;
                try
                {
                    mergedBase[key] = JSON.Parse(srcVal.ToString());
                    patched++;
                }
                catch (Exception ex)
                {
                    if (VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode)
                        LogUtil.Log("[DragDropDebug] Appearance clothes-only: skip geometry key (clone failed) " + key + " — " + ex.Message);
                }
            }

            if (patched == 0)
            {
                // Fallback: .vap files store clothing as a "clothing" array, not as "clothing:uid" keys
                if (clothingColonInPreset == 0 && geometryJsonFromPreset["clothing"] != null)
                {
                    JSONArray srcArr = geometryJsonFromPreset["clothing"].AsArray;
                    JSONArray filteredArr = new JSONArray();
                    if (srcArr != null)
                    {
                        for (int ci = 0; ci < srcArr.Count; ci++)
                        {
                            JSONClass item = srcArr[ci] as JSONClass;
                            if (item == null) continue;
                            string uid = item["id"]?.Value ?? "";
                            if (!IsGarmentClothingUid(uid)) continue;
                            filteredArr.Add(JSON.Parse(item.ToString()));
                        }
                    }
                    if (filteredArr.Count > 0)
                    {
                        mergedBase["clothing"] = filteredArr;
                        try
                        {
                            geom.RestoreFromJSON(mergedBase);
                            LogUtil.Log($"[DragDropDebug] Appearance clothes-only: geometry clothing array overlay RestoreFromJSON items={filteredArr.Count}");
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogWarning("[DragDropDebug] Appearance clothes-only: geometry array overlay RestoreFromJSON failed: " + ex.Message);
                        }
                    }
                    else
                    {
                        LogUtil.Log("[DragDropDebug] Appearance clothes-only: geometry overlay keysPatched=0 clothingColonKeysInPreset=0 clothingArrayItems=0 (no garment clothing found in preset)");
                    }
                }
                else
                {
                    LogUtil.Log("[DragDropDebug] Appearance clothes-only: geometry overlay keysPatched=0 clothingColonKeysInPreset=" + clothingColonInPreset + " (no non-cosmetic clothing: keys copied from preset)");
                }
                return;
            }

            try
            {
                geom.RestoreFromJSON(mergedBase);
                LogUtil.Log("[DragDropDebug] Appearance clothes-only: geometry outfit overlay RestoreFromJSON keysPatched=" + patched + " clothingColonKeysInPreset=" + clothingColonInPreset);
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[DragDropDebug] Appearance clothes-only: geometry overlay RestoreFromJSON failed: " + ex.Message);
            }
        }

        private void ApplyAppearanceClothingOnlyStorablesDirect(Atom atom, JSONClass presetJSON)
        {
            if (atom == null || presetJSON == null) return;
            JSONArray arr = presetJSON["storables"] != null ? presetJSON["storables"].AsArray : null;
            if (arr == null || arr.Count == 0) return;
            var clothingPresets = new List<JSONClass>();
            var clothingItems = new List<JSONClass>();
            JSONClass geometryNode = null;
            for (int i = 0; i < arr.Count; i++)
            {
                JSONClass s = arr[i] as JSONClass;
                if (s == null) continue;
                string id = s["id"]?.Value ?? "";
                if (string.Equals(id, "ClothingPresets", StringComparison.OrdinalIgnoreCase)) clothingPresets.Add(s);
                else if (id.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase) >= 0 || id.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase)
                    || id.StartsWith("wearable", StringComparison.OrdinalIgnoreCase)) clothingItems.Add(s);
                else if (IsClothingAssetPathInUid(id)) clothingItems.Add(s);
                else if (string.Equals(id, "geometry", StringComparison.OrdinalIgnoreCase)) geometryNode = s;
            }
            void RestoreOne(JSONClass sn)
            {
                if (sn == null) return;
                string sid = sn["id"]?.Value;
                if (string.IsNullOrEmpty(sid)) return;
                JSONStorable st = null;
                try { st = atom.GetStorableByID(sid); } catch { }
                if (st == null) return;
                try { st.RestoreFromJSON(sn); }
                catch (Exception ex) { LogUtil.LogWarning("[VPB] Appearance clothes-only: RestoreFromJSON failed id=" + sid + " — " + ex.Message); }
            }
            for (int i = 0; i < clothingPresets.Count; i++) RestoreOne(clothingPresets[i]);
            for (int i = 0; i < clothingItems.Count; i++) RestoreOne(clothingItems[i]);
            if (geometryNode != null) MergeGarmentClothingGeometryBoolsFromPresetJson(atom, geometryNode);
        }

        /// <summary>
        /// Writes snapshotted garment clothing: toggles into the preset's geometry storable so LoadPresetFromJSON(merge=false)
        /// still applies the current outfit without merging morphs/hair.
        /// </summary>
        private void InjectKeepGarmentGeometryIntoPresetGeometry(JSONClass presetJSON, KeepGarmentAppearanceState state)
        {
            if (presetJSON == null || state?.GarmentGeometryBools == null || state.GarmentGeometryBools.Count == 0) return;
            JSONArray arr = presetJSON["storables"] != null ? presetJSON["storables"].AsArray : null;
            if (arr == null) return;
            for (int i = 0; i < arr.Count; i++)
            {
                JSONClass s = arr[i] as JSONClass;
                if (s == null) continue;
                if (!string.Equals(s["id"]?.Value, "geometry", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var kv in state.GarmentGeometryBools)
                {
                    try { s[kv.Key] = new JSONData(kv.Value); } catch { }
                }
                return;
            }
        }

        /// <summary>
        /// After filtering garment entries out of the appearance .vap, re-append current-atom ClothingPresets + clothingItem JSON so merge=false does not drop slots.
        /// </summary>
        private static void AppendKeepGarmentStorablesToPreset(JSONClass presetJSON, KeepGarmentAppearanceState state)
        {
            if (presetJSON == null || state == null) return;
            JSONArray arr = presetJSON["storables"] != null ? presetJSON["storables"].AsArray : null;
            if (arr == null) return;
            if (state.ClothingPresetsJson != null)
            {
                JSONClass c = CloneJsonClass(state.ClothingPresetsJson);
                if (c != null) arr.Add(c);
            }
            if (state.GarmentClothingItemJsons == null) return;
            for (int i = 0; i < state.GarmentClothingItemJsons.Count; i++)
            {
                JSONClass c = CloneJsonClass(state.GarmentClothingItemJsons[i].Value);
                if (c != null) arr.Add(c);
            }
        }

        private static JSONClass CloneJsonClass(JSONClass jc)
        {
            if (jc == null) return null;
            try { return JSON.Parse(jc.ToString()).AsObject; }
            catch { return jc; }
        }

        /// <summary>
        /// Strips every non-<c>clothing:</c> key from the <c>geometry</c> storable inside a preset JSON, in-place.
        /// Only the <c>id</c> field and <c>clothing:UID</c> boolean entries are kept; everything else
        /// (morphs, skin textures, eye-colour, material params, etc.) is removed so that
        /// <see cref="MeshVR.PresetManager.LoadPresetFromJSON"/> never queues or applies those textures
        /// to GPU materials — which would happen synchronously via VPB SIM's FastLoadMetadata cache path
        /// and could not be undone by a later <c>RestoreFromJSON</c> call.
        /// </summary>
        private static void StripNonClothingParamsFromPresetGeometry(JSONClass presetJSON)
        {
            if (presetJSON == null || presetJSON["storables"] == null) return;
            JSONArray storables = presetJSON["storables"].AsArray;
            if (storables == null) return;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONClass sn = storables[i] as JSONClass;
                if (sn == null) continue;
                if (!string.Equals(sn["id"]?.Value, "geometry", StringComparison.OrdinalIgnoreCase)) continue;

                JSONClass stripped = new JSONClass();
                stripped["id"] = sn["id"];
                foreach (string key in sn.Keys)
                {
                    if (key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                        stripped[key] = sn[key];
                }
                storables[i] = stripped;
                break;
            }
        }

        private static string TruncateForKeepGarmentLog(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen) + "…";
        }

        private static string SummarizeJsonTopKeys(JSONClass jc, int maxKeys)
        {
            if (jc == null) return "<null>";
            var parts = new List<string>();
            foreach (string k in jc.Keys)
            {
                if (parts.Count >= maxKeys) { parts.Add("…"); break; }
                parts.Add(k);
            }
            return parts.Count == 0 ? "<empty>" : string.Join(",", parts.ToArray());
        }

        private static bool IsClothingItemStorableId(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return false;
            return sid.IndexOf("clothingItem#", StringComparison.OrdinalIgnoreCase) >= 0
                || sid.StartsWith("clothingItem", StringComparison.OrdinalIgnoreCase);
        }

        private static string CollectClothingItemStorableIds(Atom atom)
        {
            if (atom == null) return "<no atom>";
            var ids = new List<string>();
            try
            {
                foreach (string sid in atom.GetStorableIDs())
                {
                    if (sid != null && IsClothingItemStorableId(sid))
                        ids.Add(sid);
                }
            }
            catch { }
            return ids.Count == 0 ? "<none>" : string.Join(" | ", ids.ToArray());
        }

        private void LogKeepGarmentSnapshotBuilt(Atom atom, KeepGarmentAppearanceState state)
        {
            if (state == null)
            {
                LogUtil.Log("[DragDropDebug][KeepGarment] Snapshot: state is null");
                return;
            }
            string atomLabel = "<null>";
            try { if (atom != null) atomLabel = atom.name + " (" + atom.uid + ")"; } catch { }
            int nB = state.GarmentGeometryBools != null ? state.GarmentGeometryBools.Count : 0;
            int nI = state.GarmentClothingItemJsons != null ? state.GarmentClothingItemJsons.Count : 0;
            bool hasCps = state.ClothingPresetsJson != null;
            int cpsChars = 0;
            string cpsKeys = "";
            try
            {
                if (state.ClothingPresetsJson != null)
                {
                    cpsChars = state.ClothingPresetsJson.ToString().Length;
                    cpsKeys = SummarizeJsonTopKeys(state.ClothingPresetsJson, 24);
                }
            }
            catch { }
            LogUtil.Log($"[DragDropDebug][KeepGarment] Snapshot built atom={atomLabel} garmentGeometryBools={nB} garmentClothingItems={nI} ClothingPresets={(hasCps ? "yes" : "no")} clothingPresetsApproxChars={cpsChars} clothingPresetsTopKeys=[{cpsKeys}]");
            LogUtil.LogVerboseUi("[DragDropDebug][KeepGarment] Snapshot clothingItem ids on atom: " + CollectClothingItemStorableIds(atom));

            if (state.GarmentGeometryBools != null)
            {
                foreach (var kv in state.GarmentGeometryBools)
                {
                    LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment]   geometry {kv.Key} = {kv.Value}");
                }
            }
            if (state.GarmentClothingItemJsons != null)
            {
                for (int i = 0; i < state.GarmentClothingItemJsons.Count; i++)
                {
                    var kv = state.GarmentClothingItemJsons[i];
                    string url = ExtractClothingUrlFromStorableJson(kv.Value);
                    string pn = "";
                    try { if (kv.Value != null && kv.Value["presetName"] != null) pn = kv.Value["presetName"].Value; } catch { }
                    LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment]   item[{i}] storableId={kv.Key} url={TruncateForKeepGarmentLog(url ?? "", 120)} presetName={TruncateForKeepGarmentLog(pn, 80)}");
                }
            }
        }

        private static void RestoreKeepGarmentClothingPresetsAndItems(Atom atom, KeepGarmentAppearanceState state, string phase)
        {
            if (atom == null || state == null) return;
            string atomLabel = "<atom>";
            try { atomLabel = atom.name; } catch { }

            if (state.ClothingPresetsJson != null)
            {
                JSONStorable cps = atom.GetStorableByID("ClothingPresets");
                if (cps == null)
                {
                    LogUtil.LogWarning($"[DragDropDebug][KeepGarment] {phase}: ClothingPresets storable missing on atom={atomLabel} (cannot restore preset picker / sub-presets)");
                }
                else
                {
                    try
                    {
                        cps.RestoreFromJSON(state.ClothingPresetsJson);
                        LogUtil.Log($"[DragDropDebug][KeepGarment] {phase}: ClothingPresets RestoreFromJSON OK atom={atomLabel}");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[DragDropDebug][KeepGarment] {phase}: ClothingPresets RestoreFromJSON FAILED atom={atomLabel} — {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] {phase}: no ClothingPresetsJson in snapshot (nothing to restore for preset manager)");
            }

            if (state.GarmentClothingItemJsons == null || state.GarmentClothingItemJsons.Count == 0)
            {
                LogUtil.Log($"[DragDropDebug][KeepGarment] {phase}: clothingItems restored=0 missingStorableId=0 failed=0 (no garment items in snapshot) atom={atomLabel}");
                LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] {phase}: no garment clothingItem snapshots");
            }
            else
            {
                int ok = 0, missingStorable = 0, fail = 0;
                for (int i = 0; i < state.GarmentClothingItemJsons.Count; i++)
                {
                    var kv = state.GarmentClothingItemJsons[i];
                    JSONStorable st = atom.GetStorableByID(kv.Key);
                    if (st == null)
                    {
                        missingStorable++;
                        LogUtil.LogWarning($"[DragDropDebug][KeepGarment] {phase}: clothingItem storable GONE id={kv.Key} (appearance load may have renumbered slots — restore skipped for this entry)");
                        continue;
                    }
                    if (kv.Value == null) { fail++; continue; }
                    try
                    {
                        st.RestoreFromJSON(kv.Value);
                        ok++;
                        LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] {phase}: RestoreFromJSON OK id={kv.Key}");
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        LogUtil.LogWarning($"[DragDropDebug][KeepGarment] {phase}: clothingItem RestoreFromJSON FAILED id={kv.Key} — {ex.GetType().Name}: {ex.Message}");
                    }
                }
                LogUtil.Log($"[DragDropDebug][KeepGarment] {phase}: clothingItems restored={ok} missingStorableId={missingStorable} failed={fail} atom={atomLabel}");
            }

            LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] {phase}: atom clothingItem ids now: " + CollectClothingItemStorableIds(atom));
        }

        private KeepGarmentAppearanceState BuildKeepGarmentAppearanceState(Atom atom)
        {
            var state = new KeepGarmentAppearanceState
            {
                GarmentGeometryBools = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                GarmentClothingItemJsons = new List<KeyValuePair<string, JSONClass>>()
            };
            if (atom == null) return state;

            JSONStorable geometry = atom.GetStorableByID("geometry");
            if (geometry != null)
            {
                List<string> names = geometry.GetBoolParamNames();
                if (names != null)
                {
                    foreach (string key in names)
                    {
                        if (!key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                        string uid = key.Substring("clothing:".Length);
                        if (!IsGarmentClothingUid(uid)) continue;
                        JSONStorableBool b = geometry.GetBoolJSONParam(key);
                        if (b != null) state.GarmentGeometryBools[key] = b.val;
                    }
                }
            }

            try
            {
                foreach (string sid in atom.GetStorableIDs())
                {
                    if (!IsClothingItemStorableId(sid)) continue;
                    JSONStorable st = atom.GetStorableByID(sid);
                    if (st == null) continue;
                    JSONClass jc = null;
                    try { jc = st.GetJSON(); } catch { continue; }
                    string url = ExtractClothingUrlFromStorableJson(jc);
                    if (string.IsNullOrEmpty(url) || !IsGarmentClothingUid(url)) continue;
                    bool on = false;
                    if (geometry != null)
                    {
                        foreach (var kv in state.GarmentGeometryBools)
                        {
                            if (!kv.Value) continue;
                            string gUid = kv.Key.Substring("clothing:".Length);
                            if (ClothingGeometryUrlsLikelyMatch(url, gUid)) { on = true; break; }
                        }
                    }
                    if (!on) continue;
                    state.GarmentClothingItemJsons.Add(new KeyValuePair<string, JSONClass>(sid, CloneJsonClass(jc)));
                }
            }
            catch { }

            try
            {
                JSONStorable cps = atom.GetStorableByID("ClothingPresets");
                if (cps != null) state.ClothingPresetsJson = CloneJsonClass(cps.GetJSON());
            }
            catch { }

            LogKeepGarmentSnapshotBuilt(atom, state);
            return state;
        }

        private static void ApplyKeepGarmentAppearanceRestore(Atom atom, KeepGarmentAppearanceState state)
        {
            if (atom == null || state == null) return;
            string atomLabel = "<atom>";
            try { atomLabel = atom.name; } catch { }
            if (state.GarmentGeometryBools != null && state.GarmentGeometryBools.Count > 0)
            {
                JSONStorable geo = atom.GetStorableByID("geometry");
                if (geo == null)
                {
                    LogUtil.LogWarning($"[DragDropDebug][KeepGarment] immediate: geometry storable missing atom={atomLabel}");
                }
                else
                {
                    int gOk = 0, gMiss = 0;
                    foreach (var kv in state.GarmentGeometryBools)
                    {
                        JSONStorableBool b = geo.GetBoolJSONParam(kv.Key);
                        if (b != null) { b.val = kv.Value; gOk++; }
                        else
                        {
                            gMiss++;
                            LogUtil.LogVerboseUi($"[DragDropDebug][KeepGarment] immediate: geometry param missing key={kv.Key}");
                        }
                    }
                    LogUtil.Log($"[DragDropDebug][KeepGarment] immediate: garmentGeometryBools applied={gOk} missingParam={gMiss} atom={atomLabel}");
                }
            }

            RestoreKeepGarmentClothingPresetsAndItems(atom, state, "immediate");
        }

        private static IEnumerator DelayedKeepGarmentAppearanceRestoreAtom(string atomUid, KeepGarmentAppearanceState state)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (string.IsNullOrEmpty(atomUid) || state == null) yield break;
            try
            {
                Atom a = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null;
                if (a != null)
                {
                    LogUtil.Log("[DragDropDebug][KeepGarment] delayed: second pass (3 frames after appearance load)");
                    RestoreKeepGarmentClothingPresetsAndItems(a, state, "delayed");
                }
                else
                {
                    LogUtil.LogWarning("[DragDropDebug][KeepGarment] delayed: atom not found uid=" + atomUid);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[DragDropDebug][KeepGarment] DelayedKeepGarmentAppearanceRestore: " + ex.Message);
            }
        }

        /// <summary>
        /// Capture the current body/morph/hair state of <paramref name="atom"/> so it can be restored after
        /// a full appearance preset is applied in "clothes only" mode.  The <c>clothing:</c> keys are
        /// intentionally excluded from the geometry snapshot so the preset's clothing remains in place.
        /// </summary>
        private static KeepBodyAppearanceState BuildKeepBodyAppearanceState(Atom atom)
        {
            if (atom == null) return null;
            var state = new KeepBodyAppearanceState();

            JSONStorable geom = null;
            try { geom = atom.GetStorableByID("geometry"); } catch { }
            if (geom != null)
            {
                JSONClass fullGeom = null;
                try { fullGeom = geom.GetJSON()?.AsObject; } catch { }
                if (fullGeom != null)
                {
                    JSONClass bodyGeom = CloneJsonClass(fullGeom);
                    if (bodyGeom != null)
                    {
                        var keysToRemove = new List<string>();
                        foreach (string key in bodyGeom.Keys)
                        {
                            if (key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                                keysToRemove.Add(key);
                        }
                        foreach (string k in keysToRemove)
                            try { bodyGeom.Remove(k); } catch { }
                        state.BodyGeometryJson = bodyGeom;
                    }
                }
            }

            JSONStorable hairPresets = null;
            try { hairPresets = atom.GetStorableByID("HairPresets"); } catch { }
            if (hairPresets != null)
                try { state.HairPresetsJson = hairPresets.GetJSON()?.AsObject; } catch { }

            string atomLabel = "<atom>";
            try { atomLabel = atom.uid; } catch { }
            int nBodyKeys = 0;
            try { nBodyKeys = state.BodyGeometryJson != null ? state.BodyGeometryJson.Keys.Count() : 0; } catch { }
            LogUtil.Log($"[DragDropDebug][ClothingOnly] Body snapshot built: atom={atomLabel} bodyGeomKeys={nBodyKeys} hairPresets={(state.HairPresetsJson != null ? "yes" : "no")}");
            return state;
        }

        /// <summary>
        /// After the full appearance preset has been applied in "clothes only" mode, restore the
        /// body/morph/hair state captured by <see cref="BuildKeepBodyAppearanceState"/>.
        /// The body JSON is restored first (which resets clothing booleans), then each
        /// <see cref="KeepBodyAppearanceState.ClothingBoolsFromPreset"/> value is re-applied directly
        /// via <c>JSONStorableBool.val</c> so VaM's clothing system sees the correct false→true
        /// transitions and starts loading the preset's clothing items.
        /// </summary>
        private static void ApplyClothingOnlyBodyRestore(Atom atom, KeepBodyAppearanceState state, string phase)
        {
            if (atom == null || state == null) return;
            string atomLabel = "<atom>";
            try { atomLabel = atom.uid; } catch { }

            JSONStorable geom = null;
            if (state.BodyGeometryJson != null)
            {
                try { geom = atom.GetStorableByID("geometry"); } catch { }
                if (geom != null)
                {
                    // Detect if restoring the body would trigger a character mesh reload (which unloads CPM clothing).
                    string origChar = null;
                    string currentChar = null;
                    try { origChar = state.BodyGeometryJson["character"]?.Value; } catch { }
                    try
                    {
                        JSONNode gj = geom.GetJSON();
                        if (gj != null) currentChar = gj["character"]?.Value;
                    }
                    catch { }
                    bool charWillChange = !string.IsNullOrEmpty(origChar) && !string.IsNullOrEmpty(currentChar) && origChar != currentChar;
                    LogUtil.Log($"[DragDropDebug][ClothingOnly] {phase}: char orig={origChar ?? "null"} current={currentChar ?? "null"} willChange={charWillChange}");

                    try
                    {
                        // Step 1: restore original body.
                        // Use the 5-arg overload with setMissingToDefault=true so ALL params not
                        // present in the body snapshot (preset morphs, eye color, skin textures, etc.)
                        // are reset to their VaM defaults — matching the original person's state.
                        geom.RestoreFromJSON(state.BodyGeometryJson, true, true, null, true);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] {phase}: geometry restore failed atom={atomLabel} — {ex.Message}");
                    }

                    // Step 2: clear ALL currently-active clothing bools to prevent accumulation from
                    // previous applies (RestoreFromJSON uses setMissingToDefault=false by default,
                    // so old bools survive the body restore and must be cleared explicitly).
                    int cleared = 0;
                    try
                    {
                        foreach (string bname in geom.GetBoolParamNames())
                        {
                            if (!bname.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                            JSONStorableBool jsb = null;
                            try { jsb = geom.GetBoolJSONParam(bname); } catch { }
                            if (jsb != null && jsb.val) { jsb.val = false; cleared++; }
                        }
                    }
                    catch { }

                    // Step 3: re-enable only the preset's non-cosmetic clothing items.
                    int reapplied = 0;
                    int skipped = 0;
                    int skippedCosmetic = 0;
                    if (state.ClothingBoolsFromPreset != null && state.ClothingBoolsFromPreset.Count > 0)
                    {
                        foreach (var kv in state.ClothingBoolsFromPreset)
                        {
                            if (!kv.Value) continue;
                            string uid = kv.Key.Length > 9 ? kv.Key.Substring(9) : kv.Key; // strip "clothing:" prefix
                            if (IsCosmeticClothingUidHeuristic(uid)) { skippedCosmetic++; continue; }
                            try
                            {
                                JSONStorableBool jsb = geom.GetBoolJSONParam(kv.Key);
                                if (jsb != null) { jsb.val = true; reapplied++; }
                                else skipped++;
                            }
                            catch { skipped++; }
                        }
                    }

                    // Count active clothing bools post-restore for diagnostics.
                    int postRestoreActive = 0;
                    try
                    {
                        foreach (string bn in geom.GetBoolParamNames())
                        {
                            if (!bn.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                            JSONStorableBool jsb2 = null;
                            try { jsb2 = geom.GetBoolJSONParam(bn); } catch { }
                            if (jsb2 != null && jsb2.val) postRestoreActive++;
                        }
                    }
                    catch { }
                    LogUtil.Log($"[DragDropDebug][ClothingOnly] {phase}: body geometry restored, cleared={cleared} reapplied={reapplied} skippedCosmetic={skippedCosmetic} skipped={skipped} activeAfterRestore={postRestoreActive} atom={atomLabel}");
                }
                else
                {
                    LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] {phase}: geometry storable not found atom={atomLabel}");
                }
            }

            if (state.HairPresetsJson != null)
            {
                JSONStorable hairPresets = null;
                try { hairPresets = atom.GetStorableByID("HairPresets"); } catch { }
                if (hairPresets != null)
                {
                    try
                    {
                        hairPresets.RestoreFromJSON(state.HairPresetsJson);
                        LogUtil.Log($"[DragDropDebug][ClothingOnly] {phase}: HairPresets restored atom={atomLabel}");
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] {phase}: HairPresets restore failed atom={atomLabel} — {ex.Message}");
                    }
                }
            }
        }

        private static IEnumerator DelayedClothingOnlyBodyRestoreAtom(string atomUid, KeepBodyAppearanceState state)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            if (string.IsNullOrEmpty(atomUid) || state == null) yield break;
            try
            {
                Atom a = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null;
                if (a != null)
                {
                    LogUtil.Log("[DragDropDebug][ClothingOnly] delayed: second body-restore pass (3 frames after preset load)");
                    ApplyClothingOnlyBodyRestore(a, state, "delayed");
                }
                else
                {
                    LogUtil.LogWarning("[DragDropDebug][ClothingOnly] delayed: atom not found uid=" + atomUid);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogWarning("[DragDropDebug][ClothingOnly] DelayedClothingOnlyBodyRestore: " + ex.Message);
            }
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

            if (itemType == ItemType.Appearance)
            {
                string cfgM = VPBConfig.Instance != null ? VPBConfig.Instance.AppearanceClothingApplyMode : "replace";
                LogUtil.Log($"[DragDropDebug] Appearance clothing mode={appearanceMode} AppearanceClothingCfg={cfgM}");
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
            if (itemType == ItemType.Appearance && string.Equals(appearanceMode, "clothingOnly", StringComparison.Ordinal))
                installed = SceneLoadingUtils.InstallHostPackageRecursive(FileEntry, movedUids);
            else
                installed = UI.EnsureInstalled(FileEntry, movedUids);

            if (installed)
            {
                MVR.FileManagement.FileManager.Refresh();
                if (movedUids.Count > 0)
                    FileManager.NotifyInstalled(movedUids);
            }

            bool shouldPrewarmOnDemand =
                itemType == ItemType.Clothing ||
                itemType == ItemType.Hair ||
                itemType == ItemType.ClothingItem ||
                itemType == ItemType.HairItem ||
                itemType == ItemType.ClothingPreset ||
                itemType == ItemType.HairPreset ||
                itemType == ItemType.Appearance ||
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

                    if (itemType == ItemType.Appearance && atom.type == "Person")
                    {
                        if (string.Equals(appearanceMode, "clothingOnly", StringComparison.Ordinal))
                        {
                            // Scoped snapshot: only captures clothing state, so undo never touches hair
                            PushUndoSnapshotForClothingOnly(atom);
                        }
                        else
                        if (!PushUndoSnapshotForFullAtomState(atom))
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
                                    if (Panel != null) Panel.StartCoroutine(PostUndoPersonRefreshCoroutine(atomUid, geometrySnapshotAll, skinSnapshotAll, 5));
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
            else if (itemType == ItemType.Appearance && appearanceMode == "clothingOnly")
            {
                ClearAtomGeometryGarmentClothingBools(atom);
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
                    KeepGarmentAppearanceState keepGarmentState = null;
                    KeepBodyAppearanceState keepBodyState = null;
                    JSONClass fullPresetJSONForClothingOnly = null;
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

                                    if (itemType == ItemType.Appearance && appearanceMode == "keep")
                                    {
                                        keepGarmentState = BuildKeepGarmentAppearanceState(atom);
                                        if (presetJSON["storables"] != null)
                                        {
                                            JSONArray storables = presetJSON["storables"].AsArray;
                                            JSONArray filteredStorables = new JSONArray();
                                            foreach (JSONNode node in storables)
                                            {
                                                JSONClass sn = node as JSONClass;
                                                if (sn == null) continue;
                                                if (ShouldDropStorableFromKeepGarmentPreset(sn)) continue;
                                                filteredStorables.Add(node);
                                            }
                                            presetJSON["storables"] = filteredStorables;
                                            if (keepGarmentState != null)
                                            {
                                                InjectKeepGarmentGeometryIntoPresetGeometry(presetJSON, keepGarmentState);
                                                AppendKeepGarmentStorablesToPreset(presetJSON, keepGarmentState);
                                            }
                                        }
                                    }
                                    else if (itemType == ItemType.Appearance && appearanceMode == "clothingOnly")
                                    {
                                        // Save full preset before filtering — we need the unfiltered version for the
                                        // new snapshot+apply+restore approach (plugin-based clothing lives in plugin
                                        // storables that the garment filter would otherwise discard).
                                        fullPresetJSONForClothingOnly = CloneJsonClass(presetJSON);
                                        FilterAppearancePresetToGarmentClothingOnly(presetJSON);
                                        LogUtil.Log("[DragDropDebug] Appearance clothes-only: preset filtered to garment clothing storables");
                                    }

                                    string ensureDepsText = (itemType == ItemType.Appearance && string.Equals(appearanceMode, "clothingOnly", StringComparison.Ordinal))
                                        ? BuildDependencyScanTextForAppearanceClothesOnly(presetJSON)
                                        : presetJSON.ToString();
                                    if (FileButton.EnsureInstalledByText(ensureDepsText))
                                    {
                                        MVR.FileManagement.FileManager.Refresh();
                                        FileManager.Refresh();
                                    }

                                    LogUtil.Log($"[DragDropDebug] JSON loaded successfully from {normalizedPath}");

                                    if (itemType == ItemType.Appearance)
                                    {
                                        presetJSON["setUnlistedParamsToDefault"].AsBool = appearanceMode != "clothingOnly";
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

                                            if (itemType == ItemType.Appearance && appearanceMode == "clothingOnly")
                                            {
                                                JSONClass fullPreset = fullPresetJSONForClothingOnly ?? presetJSON;

                                                // ── Extract geometry node from the full preset ────────────────────────
                                                JSONClass presetGeomNode = null;
                                                if (fullPreset["storables"] != null)
                                                {
                                                    JSONArray arr = fullPreset["storables"].AsArray;
                                                    if (arr != null)
                                                    {
                                                        for (int si = 0; si < arr.Count; si++)
                                                        {
                                                            JSONClass sn = arr[si] as JSONClass;
                                                            if (sn != null && string.Equals(sn["id"]?.Value, "geometry", StringComparison.OrdinalIgnoreCase))
                                                            { presetGeomNode = sn; break; }
                                                        }
                                                    }
                                                }

                                                // Build geometry-only JSON containing just the garment clothing items.
                                                // .vap files use a "clothing" array; legacy scene formats use "clothing:uid" bool keys.
                                                JSONClass clothingBoolsGeom = new JSONClass();
                                                clothingBoolsGeom["id"] = new JSONData("geometry");
                                                int boolsTotal = 0, boolsActive = 0;
                                                if (presetGeomNode != null)
                                                {
                                                    foreach (string key in presetGeomNode.Keys)
                                                    {
                                                        if (!key.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                                                        clothingBoolsGeom[key] = presetGeomNode[key];
                                                        boolsTotal++;
                                                        JSONNode v = presetGeomNode[key];
                                                        bool bval = (v is JSONClass vc) ? (vc["val"]?.AsBool ?? false) : v.AsBool;
                                                        if (bval) boolsActive++;
                                                    }
                                                    if (boolsTotal == 0 && presetGeomNode["clothing"] != null)
                                                    {
                                                        JSONArray srcArr = presetGeomNode["clothing"].AsArray;
                                                        JSONArray filteredArr = new JSONArray();
                                                        if (srcArr != null)
                                                        {
                                                            for (int ci = 0; ci < srcArr.Count; ci++)
                                                            {
                                                                JSONClass item = srcArr[ci] as JSONClass;
                                                                if (item == null) continue;
                                                                string uid = item["id"]?.Value ?? "";
                                                                if (!IsGarmentClothingUid(uid)) continue;
                                                                filteredArr.Add(item);
                                                                boolsTotal++;
                                                                JSONNode av = item["active"];
                                                                if (av != null && (av.AsBool || av.Value == "1")) boolsActive++;
                                                            }
                                                        }
                                                        if (filteredArr.Count > 0)
                                                            clothingBoolsGeom["clothing"] = filteredArr;
                                                    }
                                                }

                                                // Build plugins-only preset (MVRPluginManager) for plugin-based clothing.
                                                JSONClass pluginsOnlyPreset = null;
                                                if (fullPreset["storables"] != null)
                                                {
                                                    JSONArray arr = fullPreset["storables"].AsArray;
                                                    if (arr != null)
                                                    {
                                                        JSONArray pluginStorables = new JSONArray();
                                                        for (int si = 0; si < arr.Count; si++)
                                                        {
                                                            JSONClass sn = arr[si] as JSONClass;
                                                            if (sn == null) continue;
                                                            if (string.Equals(sn["id"]?.Value, "MVRPluginManager", StringComparison.OrdinalIgnoreCase))
                                                                pluginStorables.Add(CloneJsonClass(sn));
                                                        }
                                                        if (pluginStorables.Count > 0)
                                                        {
                                                            pluginsOnlyPreset = CloneJsonClass(fullPreset);
                                                            pluginsOnlyPreset["storables"] = pluginStorables;
                                                        }
                                                    }
                                                }

                                                LogUtil.Log($"[DragDropDebug][ClothingOnly] boolsInPreset={boolsTotal} activeInPreset={boolsActive} hasPlugins={pluginsOnlyPreset != null}");

                                                // Apply ClothingPresets from the appearance preset BEFORE activating clothing items.
                                                // Items read ClothingPresets on first activation; pre-setting it ensures they load
                                                // with the configured sub-preset (colour variant, etc.) rather than the base default.
                                                JSONArray fullStorablesEarly = fullPreset["storables"] != null ? fullPreset["storables"].AsArray : null;
                                                for (int siEarly = 0; fullStorablesEarly != null && siEarly < fullStorablesEarly.Count; siEarly++)
                                                {
                                                    JSONClass snEarly = fullStorablesEarly[siEarly] as JSONClass;
                                                    if (snEarly == null) continue;
                                                    if (!string.Equals(snEarly["id"]?.Value, "ClothingPresets", StringComparison.OrdinalIgnoreCase)) continue;
                                                    JSONStorable cpsEarly = atom.GetStorableByID("ClothingPresets");
                                                    if (cpsEarly != null)
                                                    {
                                                        try
                                                        {
                                                            try { MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath); } catch { }
                                                            cpsEarly.RestoreFromJSON(snEarly);
                                                            LogUtil.Log("[DragDropDebug][ClothingOnly] ClothingPresets pre-applied from preset before item activation");
                                                        }
                                                        catch { }
                                                        finally { try { MVR.FileManagement.FileManager.PopLoadDir(); } catch { } }
                                                    }
                                                    break;
                                                }

                                                ClearAtomGeometryGarmentClothingBools(atom);
                                                try
                                                {
                                                    JSONStorable geom = atom.GetStorableByID("geometry");
                                                    if (geom != null)
                                                    {
                                                        // setMissingToDefault=false ensures only supplied clothing keys change;
                                                        // character, morphs, skin, hair, eye colour are untouched.
                                                        geom.RestoreFromJSON(clothingBoolsGeom, true, true, null, false);
                                                        presetLoaded = true;

                                                        // Apply per-item material/subpreset storables from the full preset.
                                                        // Preset storable IDs use "{Creator}:{AssetFileBase}{Suffix}" (e.g. "JackyCracky:Zelda_BootsMaterialCombined").
                                                        // Build a prefix set from garment UIDs to match and restore only garment storables.
                                                        JSONArray fullStorables = fullPreset["storables"] != null ? fullPreset["storables"].AsArray : null;
                                                        if (fullStorables != null)
                                                        {
                                                            HashSet<string> garmentPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                                            JSONArray clothingArr = clothingBoolsGeom["clothing"] != null ? clothingBoolsGeom["clothing"].AsArray : null;
                                                            if (clothingArr != null)
                                                                for (int ci2 = 0; ci2 < clothingArr.Count; ci2++)
                                                                {
                                                                    JSONClass citem = clothingArr[ci2] as JSONClass;
                                                                    string cuid = citem?["id"]?.Value ?? "";
                                                                    if (!string.IsNullOrEmpty(cuid)) BuildGarmentPrefix(cuid, garmentPrefixes);
                                                                }
                                                            foreach (string bkey in clothingBoolsGeom.Keys)
                                                                if (bkey.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase))
                                                                    BuildGarmentPrefix(bkey.Substring("clothing:".Length), garmentPrefixes);

                                                            if (garmentPrefixes.Count > 0)
                                                            {
                                                                int subCount = 0;
                                                                try { MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath); } catch { }
                                                                try
                                                                {
                                                                    for (int si2 = 0; si2 < fullStorables.Count; si2++)
                                                                    {
                                                                        JSONClass sn2 = fullStorables[si2] as JSONClass;
                                                                        if (sn2 == null) continue;
                                                                        string sid = sn2["id"]?.Value ?? "";
                                                                        bool match = false;
                                                                        foreach (string pfx in garmentPrefixes)
                                                                            if (sid.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                                                                        if (!match) continue;
                                                                        JSONStorable cs = atom.GetStorableByID(sid);
                                                                        if (cs != null) { try { cs.RestoreFromJSON(sn2); subCount++; } catch { } }
                                                                    }
                                                                }
                                                                finally { try { MVR.FileManagement.FileManager.PopLoadDir(); } catch { } }
                                                                LogUtil.Log($"[DragDropDebug][ClothingOnly] subpreset storables applied: {subCount}");

                                                                // If items weren't loaded yet (subCount==0), schedule a delayed pass
                                                                // that polls until the material storables appear on the atom.
                                                                if (subCount == 0)
                                                                {
                                                                    string atomUidDelay = null;
                                                                    try { atomUidDelay = atom.uid; } catch { }
                                                                    if (!string.IsNullOrEmpty(atomUidDelay) && SuperController.singleton != null)
                                                                    {
                                                                        var pendingList = new List<KeyValuePair<string, JSONClass>>();
                                                                        for (int si3 = 0; si3 < fullStorables.Count; si3++)
                                                                        {
                                                                            JSONClass sn3 = fullStorables[si3] as JSONClass;
                                                                            if (sn3 == null) continue;
                                                                            string sid3 = sn3["id"]?.Value ?? "";
                                                                            bool match3 = false;
                                                                            foreach (string pfx3 in garmentPrefixes)
                                                                                if (sid3.StartsWith(pfx3, StringComparison.OrdinalIgnoreCase)) { match3 = true; break; }
                                                                            if (match3) pendingList.Add(new KeyValuePair<string, JSONClass>(sid3, CloneJsonClass(sn3)));
                                                                        }
                                                                        if (pendingList.Count > 0)
                                                                        {
                                                                            JSONClass cpJsonDelay = null;
                                                                            for (int si4 = 0; si4 < fullStorables.Count; si4++)
                                                                            {
                                                                                JSONClass sn4 = fullStorables[si4] as JSONClass;
                                                                                if (sn4 != null && string.Equals(sn4["id"]?.Value, "ClothingPresets", StringComparison.OrdinalIgnoreCase))
                                                                                { cpJsonDelay = CloneJsonClass(sn4); break; }
                                                                            }
                                                                            var pendingState = new PendingClothingOnlySubpresetState
                                                                            {
                                                                                AtomUid = atomUidDelay,
                                                                                StorablesToApply = pendingList,
                                                                                ClothingPresetsJson = cpJsonDelay,
                                                                                LoadDirPath = normalizedPath
                                                                            };
                                                                            SuperController.singleton.StartCoroutine(DelayedClothingOnlySubpresetRestoreCoroutine(pendingState));
                                                                            LogUtil.Log($"[DragDropDebug][ClothingOnly] scheduled delayed subpreset restore for {pendingList.Count} storables");
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] geometry restore failed: {ex.Message}");
                                                }

                                                if (pluginsOnlyPreset != null)
                                                {
                                                    try
                                                    {
                                                        MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath);
                                                        presetManager.LoadPresetFromJSON(pluginsOnlyPreset, false);
                                                        presetLoaded = true;
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        LogUtil.LogWarning($"[DragDropDebug][ClothingOnly] plugins apply failed: {ex.Message}");
                                                    }
                                                    finally
                                                    {
                                                        MVR.FileManagement.FileManager.PopLoadDir();
                                                    }
                                                }

                                                keepBodyState = null;
                                            }
                                            else
                                            {
                                                bool merge = true;
                                                // keep: merge=false so morphs/hair/skin replace from preset; garments come from injected snapshot in JSON (merge=true would compound morphs/hair).
                                                if (itemType == ItemType.Appearance)
                                                    merge = (appearanceMode == "merge");
                                                else if (itemType == ItemType.Pose) merge = false;

                                                bool ddReplaceMode = Panel != null && Panel.DragDropReplaceMode;
                                                bool isPersonClothingPreset = itemType == ItemType.Clothing && ext == ".vap" && storableId == "ClothingPresets";
                                                if (ddReplaceMode && isPersonClothingPreset)
                                                {
                                                    ClothingLoadingUtils.RemoveAllClothing(atom);
                                                    merge = false;
                                                }

                                                if (itemType == ItemType.Appearance && appearanceMode == "keep")
                                                {
                                                    // Apply ClothingPresetsJson BEFORE LoadPresetFromJSON so that when geometry bools
                                                    // activate clothing items, they read the correct sub-preset selection rather than base.
                                                    if (keepGarmentState?.ClothingPresetsJson != null)
                                                    {
                                                        JSONStorable cpsKeep = atom.GetStorableByID("ClothingPresets");
                                                        if (cpsKeep != null)
                                                        {
                                                            try
                                                            {
                                                                try { MVR.FileManagement.FileManager.PushLoadDirFromFilePath(normalizedPath); } catch { }
                                                                cpsKeep.RestoreFromJSON(keepGarmentState.ClothingPresetsJson);
                                                                LogUtil.Log("[DragDropDebug] Appearance keep: ClothingPresets pre-applied before LoadPresetFromJSON");
                                                            }
                                                            catch { }
                                                            finally { try { MVR.FileManagement.FileManager.PopLoadDir(); } catch { } }
                                                        }
                                                    }
                                                    LogUtil.Log($"[DragDropDebug] Appearance keep: LoadPresetFromJSON merge={merge} (garment state injected into preset JSON; post-restore still runs)");
                                                }
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
                                            }

                                            if (itemType == ItemType.Appearance && appearanceMode == "keep" && keepGarmentState != null)
                                            {
                                                ApplyKeepGarmentAppearanceRestore(atom, keepGarmentState);
                                                try
                                                {
                                                    string uid = null;
                                                    try { uid = atom.uid; } catch { }
                                                    if (!string.IsNullOrEmpty(uid) && SuperController.singleton != null)
                                                        SuperController.singleton.StartCoroutine(DelayedKeepGarmentAppearanceRestoreAtom(uid, keepGarmentState));
                                                }
                                                catch (Exception ex)
                                                {
                                                    LogUtil.LogWarning("[DragDropDebug] Keep-garment delayed restore schedule failed: " + ex.Message);
                                                }
                                            }

                                            if (itemType == ItemType.Appearance && appearanceMode == "clothingOnly" && keepBodyState != null)
                                            {
                                                // The immediate restore was already done inside the clothingOnly block above;
                                                // this is a safety second pass in case LoadPresetFromJSON triggered
                                                // synchronous storable callbacks that overwrote the body params.
                                                ApplyClothingOnlyBodyRestore(atom, keepBodyState, "post-load");
                                            }

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

             // 8b — resolve thumbnail texture; fall back to memory cache if async load is still pending
             Texture ghostTex = GetGhostTexture();

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
                         // 8b — use Sprites/Default (always available, unlit, alpha-blended)
                         Shader ghostShader = Shader.Find("Sprites/Default");
                         if (ghostShader == null) ghostShader = Shader.Find("Unlit/Transparent");
                         Material m = new Material(ghostShader);
                         m.mainTexture = ghostTex;
                         // hide the quad until we have a real texture to avoid a white flash
                         m.color = ghostTex != null ? new Color(1f, 1f, 1f, 0.9f) : Color.clear;
                         ghostRenderer.material = m;
                         ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                         ghostRenderer.receiveShadows = false;
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
                 // 8b — hide until we have a real texture; coroutine restores color when it arrives
                 img.color = ghostTex != null ? new Color(1f, 1f, 1f, 0.7f) : Color.clear;
                 img.texture = ghostTex;
                 ghostImg = img; // 8b — store for late texture update

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

             // 8b — if texture was unavailable at drag start, poll until ThumbnailImage loads it
             if (ghostTex == null) StartCoroutine(UpdateGhostTextureFromThumbnail());

             planeDistance = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);

             UpdateGhost(eventData, null, planeDistance);
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
