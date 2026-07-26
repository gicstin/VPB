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
        private static HashSet<string> GetRegionsImpl(FileEntry entry, string cachePrefix, List<string> regionTagSet, Func<VarFileEntry, List<string>> getVarTags, Func<string, HashSet<string>> heuristicFn)
        {
            if (entry == null) return new HashSet<string>();
            string cacheKey = cachePrefix + entry.Uid;
            if (_globalRegionCache.TryGetValue(cacheKey, out HashSet<string> cached)) return cached;

            HashSet<string> regions = new HashSet<string>();

            // 1. Try VarFileEntry pre-parsed tags
            if (entry is VarFileEntry vfe)
            {
                List<string> varTags = getVarTags(vfe);
                if (varTags != null && varTags.Count > 0)
                {
                    foreach (var t in varTags)
                    {
                        string lower = t.ToLowerInvariant();
                        if (regionTagSet.Contains(lower)) regions.Add(lower);
                    }
                }
            }

            // 2. Try reading file content (for loose files or missing cache)
            if (regions.Count == 0)
            {
                string ext = Path.GetExtension(entry.Path).ToLowerInvariant();
                if (ext == ".vap" || ext == ".json" || ext == ".vam")
                {
                    try
                    {
                        using (var reader = entry.OpenStreamReader())
                        {
                            JSONNode node = JSON.Parse(reader.ReadToEnd());
                            if (node != null && node["tags"] != null)
                            {
                                string tagStr = node["tags"].Value;
                                if (!string.IsNullOrEmpty(tagStr))
                                {
                                    foreach (var t in tagStr.Split(',').Select(x => x.Trim().ToLowerInvariant()))
                                        if (regionTagSet.Contains(t)) regions.Add(t);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("Error parsing tags from file " + entry.Path + ": " + ex.Message);
                    }
                }
            }

            // 3. Filename heuristics
            if (regions.Count == 0)
                regions = heuristicFn(Path.GetFileNameWithoutExtension(entry.Path));

            PutGlobalRegionCache(cacheKey, regions);
            return regions;
        }

        private static HashSet<string> GetHairRegions(FileEntry entry) =>
            GetRegionsImpl(entry, "hair:", TagFilter.HairRegionTags, vfe => vfe.HairTags, GetRegionsFromHeuristics);

        private static HashSet<string> GetClothingRegions(FileEntry entry) =>
            GetRegionsImpl(entry, "clothing:", TagFilter.ClothingRegionTags, vfe => vfe.ClothingTags, GetClothingRegionsFromHeuristics);

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
                    if (!SceneUtils.IsPersonLikeAtom(a)) continue;
                    
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
                        if (!SceneUtils.IsPersonLikeAtom(a)) continue;
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

        // VPB-refactor: native atom restore, deferred from import-unification
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
                ghostText = null;
                ghostRenderer = null;
                ghostImg = null;
            }
            // 8c — tear down the input-blocking overlay and clear the static drag flag
            DestroyDragOverlay();
            IsDragging = false;
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

                 if (atom != null && SceneUtils.IsPersonLikeAtom(atom))
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

                options.Add(new ContextMenuPanel.Option("Spawn With Collisions Disabled", () => {
                    try { if (ContextMenuPanel.Instance != null) ContextMenuPanel.Instance.Hide(); } catch { }
                    Vector3 pos = position;
                    StartCoroutine(CreatePersonAndApplyAppearance(entry, pos, "replace", true));
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

        private IEnumerator CreatePersonAndApplyAppearance(FileEntry entry, Vector3 position, string clothingMode, bool disableCollisions = false)
        {
            if (entry == null) yield break;

            Atom spawned = null;
            SpawnAtomElement.SpawnSuppressionHandle suppression = null;
            yield return SpawnAtomElement.SpawnPersonAtFloorSuppressed(position, (a, h) => { spawned = a; suppression = h; });

            if (spawned == null) yield break;

            Action applyCollisionDisabled = () =>
            {
                try
                {
                    var receiver = spawned.GetStorableByID("AtomControl");
                    if (receiver == null) return;
                    JSONStorableBool collisionEnabled = receiver.GetBoolJSONParam("collisionEnabled");
                    if (collisionEnabled != null) collisionEnabled.val = false;
                }
                catch { }
            };

            Action applyUnityCollisionDisabled = () =>
            {
                try
                {
                    if (spawned == null) return;
                    var root = spawned.gameObject;
                    if (root == null) return;

                    try
                    {
                        var colliders = root.GetComponentsInChildren<Collider>(true);
                        if (colliders != null)
                        {
                            for (int i = 0; i < colliders.Length; i++)
                            {
                                var c = colliders[i];
                                if (c == null) continue;
                                if (!c.enabled) continue;
                                c.enabled = false;
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        var ccs = root.GetComponentsInChildren<CharacterController>(true);
                        if (ccs != null)
                        {
                            for (int i = 0; i < ccs.Length; i++)
                            {
                                var cc = ccs[i];
                                if (cc == null) continue;
                                if (!cc.enabled) continue;
                                cc.enabled = false;
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        var rbs = root.GetComponentsInChildren<Rigidbody>(true);
                        if (rbs != null)
                        {
                            for (int i = 0; i < rbs.Length; i++)
                            {
                                var rb = rbs[i];
                                if (rb == null) continue;
                                if (!rb.detectCollisions) continue;
                                rb.detectCollisions = false;
                            }
                        }
                    }
                    catch { }
                }
                catch { }
            };

            if (disableCollisions)
            {
                applyCollisionDisabled();
                applyUnityCollisionDisabled();
            }

            FileEntry prevEntry = FileEntry;
            try { FileEntry = entry; } catch { }

            try
            {
                ApplyClothingToAtom(spawned, entry.Uid, clothingMode);
            }
            catch { }

            if (disableCollisions)
            {
                applyCollisionDisabled();
                applyUnityCollisionDisabled();
            }

            try
            {
                ApplyPoseFromPresetPath(spawned, entry.Uid, true);
            }
            catch { }

            if (disableCollisions)
            {
                applyCollisionDisabled();
                applyUnityCollisionDisabled();
            }

            try { FileEntry = prevEntry; } catch { }

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            try
            {
                SceneLoadingUtils.SchedulePostPersonApplyFixup(spawned);
            }
            catch { }

            if (disableCollisions)
            {
                applyCollisionDisabled();
                applyUnityCollisionDisabled();
            }

            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            try
            {
                if (suppression != null) suppression.Restore();
            }
            catch { }

            if (disableCollisions)
            {
                applyCollisionDisabled();
                applyUnityCollisionDisabled();
            }

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
            if (target == null || string.IsNullOrEmpty(path)) return;

            FileEntry entry = FileManager.GetFileEntry(path);
            if (entry == null) return;

            VpbImport.LoadPreset(entry, target, VpbResourceType.Pose, ClothingApplyMode.Replace,
                                 presetJC: null, suppressRoot: suppressRoot);
        }


    }

}
