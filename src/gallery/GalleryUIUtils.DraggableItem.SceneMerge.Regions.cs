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

        /// <summary>Alt/Ctrl on drop: replay sticky last action without opening menu.</summary>
        private bool TrySkipContextMenu(Atom atom, FileEntry entry, Vector3 position)
        {
            if (!ContextMenuPanel.IsSkipModifierHeld()) return false;
            if (entry == null) return false;

            ItemType type = GetItemType(entry);
            if (type == ItemType.Scene)
                return TryRunLastSceneAction(entry, atom);
            if (type == ItemType.Appearance)
                return TryRunLastAppearanceAction(entry, atom, position);
            return false;
        }

        private bool TryRunLastSceneAction(FileEntry entry, Atom atom)
        {
            string last = ContextMenuPanel.GetLastSceneAction();
            if (string.IsNullOrEmpty(last)) return false;

            string uid = entry != null ? entry.Uid : null;
            Atom importTarget = ResolveImportTargetAtom(atom);

            switch (last)
            {
                case ContextMenuPanel.SceneActionId.Load:
                    LoadSceneFile(uid);
                    return true;
                case ContextMenuPanel.SceneActionId.Persons:
                    MergeSceneFile(uid, UI.SceneAddMode.PersonsOnly, false);
                    return true;
                case ContextMenuPanel.SceneActionId.Props:
                    MergeSceneFile(uid, UI.SceneAddMode.NonPersons, false);
                    return true;
                case ContextMenuPanel.SceneActionId.PersonsNear:
                    MergeSceneFile(uid, UI.SceneAddMode.PersonsOnly, true);
                    return true;
                case ContextMenuPanel.SceneActionId.PropsNear:
                    MergeSceneFile(uid, UI.SceneAddMode.NonPersons, true);
                    return true;
                case ContextMenuPanel.SceneActionId.ImportPerson:
                    if (!SceneUtils.IsPersonLikeAtom(importTarget)) return false;
                    OpenSceneImportSidebar(entry, importTarget);
                    return true;
                case ContextMenuPanel.SceneActionId.PickAtoms:
                    OpenSceneImportSidebar(entry, importTarget, VpbResourceType.Atoms);
                    return true;
                case ContextMenuPanel.SceneActionId.ImportOpen:
                    OpenSceneImportSidebar(entry, importTarget);
                    return true;
                case ContextMenuPanel.SceneActionId.MergeFilter:
                    MergeSceneFile(uid, UI.SceneAddMode.NonPersonsMergeLoad, false);
                    return true;
                case ContextMenuPanel.SceneActionId.FullMerge:
                    MergeSceneFile(uid, UI.SceneAddMode.FullMerge, false);
                    return true;
                case ContextMenuPanel.SceneActionId.FullMergeNear:
                    MergeSceneFile(uid, UI.SceneAddMode.FullMerge, true);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryRunLastAppearanceAction(FileEntry entry, Atom atom, Vector3 position)
        {
            string last = ContextMenuPanel.GetLastAppearanceAction();
            if (string.IsNullOrEmpty(last)) return false;

            Atom selected = ResolveImportTargetAtom(atom);

            switch (last)
            {
                case ContextMenuPanel.AppearanceActionId.Spawn:
                    StartCoroutine(CreatePersonAndApplyAppearance(entry, position, "replace", false));
                    return true;
                case ContextMenuPanel.AppearanceActionId.SpawnNoCollide:
                    StartCoroutine(CreatePersonAndApplyAppearance(entry, position, "replace", true));
                    return true;
                case ContextMenuPanel.AppearanceActionId.ApplySelected:
                    if (!SceneUtils.IsPersonLikeAtom(selected)) return false;
                    try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(entry), "appearance"); } catch { }
                    ApplyClothingToAtom(selected, entry.Uid, null);
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBuriedSceneAction(string actionId)
        {
            return actionId == ContextMenuPanel.SceneActionId.PersonsNear
                || actionId == ContextMenuPanel.SceneActionId.PropsNear
                || actionId == ContextMenuPanel.SceneActionId.PickAtoms
                || actionId == ContextMenuPanel.SceneActionId.ImportOpen
                || actionId == ContextMenuPanel.SceneActionId.MergeFilter
                || actionId == ContextMenuPanel.SceneActionId.FullMerge
                || actionId == ContextMenuPanel.SceneActionId.FullMergeNear;
        }

        private static string SceneActionLabel(string actionId)
        {
            switch (actionId)
            {
                case ContextMenuPanel.SceneActionId.Load:
                    return VPBTranslation.T("ctx.scene.load", "Load Scene");
                case ContextMenuPanel.SceneActionId.Persons:
                    return VPBTranslation.T("ctx.scene.add_persons", "Persons only");
                case ContextMenuPanel.SceneActionId.Props:
                    return VPBTranslation.T("ctx.scene.add_props", "Props & environment");
                case ContextMenuPanel.SceneActionId.PersonsNear:
                    return VPBTranslation.T("ctx.scene.persons_near", "Persons near me");
                case ContextMenuPanel.SceneActionId.PropsNear:
                    return VPBTranslation.T("ctx.scene.props_near", "Props near me");
                case ContextMenuPanel.SceneActionId.ImportPerson:
                    return VPBTranslation.T("ctx.scene.import_person", "Onto selected person");
                case ContextMenuPanel.SceneActionId.PickAtoms:
                    return VPBTranslation.T("ctx.scene.pick_atoms", "Pick atoms…");
                case ContextMenuPanel.SceneActionId.ImportOpen:
                    return VPBTranslation.T("ctx.scene.import_open", "Open Scene Import");
                case ContextMenuPanel.SceneActionId.MergeFilter:
                    return VPBTranslation.T("ctx.scene.merge_filter", "Merge load (no persons)");
                case ContextMenuPanel.SceneActionId.FullMerge:
                    return VPBTranslation.T("ctx.scene.merge_full", "Entire scene");
                case ContextMenuPanel.SceneActionId.FullMergeNear:
                    return VPBTranslation.T("ctx.scene.merge_full_near", "Entire scene near me");
                default:
                    return actionId;
            }
        }

        private static ContextMenuPanel.OptionKind KindForSceneAction(string actionId, string last)
        {
            if (!string.IsNullOrEmpty(last) && string.Equals(actionId, last, StringComparison.Ordinal))
                return ContextMenuPanel.OptionKind.Primary;
            if (actionId == ContextMenuPanel.SceneActionId.Load)
            {
                // Soft friction: Load is Destructive when not the sticky primary.
                if (string.IsNullOrEmpty(last) || last == ContextMenuPanel.SceneActionId.Load)
                    return ContextMenuPanel.OptionKind.Primary;
                return ContextMenuPanel.OptionKind.Destructive;
            }
            if (actionId == ContextMenuPanel.SceneActionId.FullMerge
                || actionId == ContextMenuPanel.SceneActionId.FullMergeNear)
                return ContextMenuPanel.OptionKind.Destructive;
            if (actionId == ContextMenuPanel.SceneActionId.MergeFilter)
                return ContextMenuPanel.OptionKind.Quiet;
            return ContextMenuPanel.OptionKind.Normal;
        }

        private void HandleDropWithContext(Atom atom, FileEntry entry, Vector3 position)
        {
            if (TrySkipContextMenu(atom, entry, position))
                return;

            List<ContextMenuPanel.Option> options = new List<ContextMenuPanel.Option>(12);
            ItemType type = GetItemType(entry);

            if (type == ItemType.Scene)
            {
                FileEntry capturedEntry = entry;
                string sceneUid = entry != null ? entry.Uid : null;
                string last = ContextMenuPanel.GetLastSceneAction();
                Atom importTarget = ResolveImportTargetAtom(atom);

                // Sticky buried action → Repeat row at top (Tesler: absorb recall).
                if (IsBuriedSceneAction(last))
                {
                    string buried = last;
                    options.Add(new ContextMenuPanel.Option(
                        VPBTranslation.T("ctx.repeat_prefix", "Repeat: ") + SceneActionLabel(buried),
                        VPBTranslation.T("ctx.repeat_sub", "Last used · Alt/Ctrl+drop skips menu"),
                        () =>
                        {
                            ContextMenuPanel.RememberSceneAction(buried);
                            TryRunLastSceneAction(capturedEntry, atom);
                        },
                        ContextMenuPanel.OptionKind.Primary));
                }

                // Load Scene — one click (subtitle warns). No confirm page.
                options.Add(new ContextMenuPanel.Option(
                    VPBTranslation.T("ctx.scene.load", "Load Scene"),
                    VPBTranslation.T("ctx.scene.load_sub", "Replaces the current scene"),
                    () =>
                    {
                        ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.Load);
                        LoadSceneFile(sceneUid);
                    },
                    KindForSceneAction(ContextMenuPanel.SceneActionId.Load, last)));

                options.Add(ContextMenuPanel.Option.Section(
                    VPBTranslation.T("ctx.scene.add_section", "Add to scene")));

                options.Add(new ContextMenuPanel.Option(
                    VPBTranslation.T("ctx.scene.add_persons", "Persons only"),
                    VPBTranslation.T("ctx.scene.add_persons_sub", "Merge Person atoms · skip props & lights"),
                    () =>
                    {
                        ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.Persons);
                        MergeSceneFile(sceneUid, UI.SceneAddMode.PersonsOnly, false);
                    },
                    KindForSceneAction(ContextMenuPanel.SceneActionId.Persons, last)));

                options.Add(new ContextMenuPanel.Option(
                    VPBTranslation.T("ctx.scene.add_props", "Props & environment"),
                    VPBTranslation.T("ctx.scene.add_props_sub", "Lights, props, CUAs · skip persons"),
                    () =>
                    {
                        ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.Props);
                        MergeSceneFile(sceneUid, UI.SceneAddMode.NonPersons, false);
                    },
                    KindForSceneAction(ContextMenuPanel.SceneActionId.Props, last)));

                options.Add(new ContextMenuPanel.Option(
                    VPBTranslation.T("ctx.scene.more", "More options"),
                    VPBTranslation.T("ctx.scene.more_sub", "Place near me · pick atoms · advanced merge"),
                    () =>
                    {
                        Atom t = ResolveImportTargetAtom(atom);
                        ContextMenuPanel.Instance.PushPage(
                            VPBTranslation.T("ctx.scene.more_title", "More options"),
                            BuildAddToSceneMoreOptions(capturedEntry, t));
                    },
                    ContextMenuPanel.OptionKind.Normal,
                    isSubMenu: true));

                if (SceneUtils.IsPersonLikeAtom(importTarget))
                {
                    Atom capturedTarget = importTarget;
                    options.Add(ContextMenuPanel.Option.Section(
                        VPBTranslation.T("ctx.scene.import_section", "Import")));

                    options.Add(new ContextMenuPanel.Option(
                        VPBTranslation.T("ctx.scene.import_person", "Onto selected person"),
                        VPBTranslation.T("ctx.scene.import_person_sub", "Appearance, pose, plugins, clothing…"),
                        () =>
                        {
                            ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.ImportPerson);
                            OpenSceneImportSidebar(capturedEntry, capturedTarget);
                        },
                        KindForSceneAction(ContextMenuPanel.SceneActionId.ImportPerson, last)));
                }
            }
            else if (type == ItemType.Appearance)
            {
                FileEntry capturedEntry = entry;
                Vector3 pos = position;
                string last = ContextMenuPanel.GetLastAppearanceAction();
                Atom selected = ResolveImportTargetAtom(atom);

                // Near-miss: drop missed person but selection is person-like.
                if (SceneUtils.IsPersonLikeAtom(selected)
                    && (atom == null || !SceneUtils.IsPersonLikeAtom(atom)))
                {
                    Atom applyTarget = selected;
                    string name = applyTarget != null ? applyTarget.name : "person";
                    bool applyPrimary = last == ContextMenuPanel.AppearanceActionId.ApplySelected
                        || string.IsNullOrEmpty(last);

                    options.Add(new ContextMenuPanel.Option(
                        VPBTranslation.T("ctx.appearance.apply_selected", "Apply to selected"),
                        string.Format(
                            VPBTranslation.T("ctx.appearance.apply_selected_sub", "Target: {0}"),
                            name),
                        () =>
                        {
                            ContextMenuPanel.RememberAppearanceAction(ContextMenuPanel.AppearanceActionId.ApplySelected);
                            try { VpbLocalDatabase.TryRecordItemUse(VpbLocalDatabase.BuildUsageKey(capturedEntry), "appearance"); } catch { }
                            ApplyClothingToAtom(applyTarget, capturedEntry.Uid, null);
                        },
                        applyPrimary ? ContextMenuPanel.OptionKind.Primary : ContextMenuPanel.OptionKind.Normal));
                }

                bool spawnPrimary = last == ContextMenuPanel.AppearanceActionId.Spawn
                    || (string.IsNullOrEmpty(last) && options.Count == 0);
                bool noCollidePrimary = last == ContextMenuPanel.AppearanceActionId.SpawnNoCollide;

                options.Add(new ContextMenuPanel.Option(
                    VPBTranslation.T("ctx.appearance.spawn", "Spawn Person Appearance"),
                    VPBTranslation.T("ctx.appearance.spawn_sub", "New person at drop point"),
                    () =>
                    {
                        ContextMenuPanel.RememberAppearanceAction(ContextMenuPanel.AppearanceActionId.Spawn);
                        try { if (ContextMenuPanel.Instance != null) ContextMenuPanel.Instance.Hide(); } catch { }
                        StartCoroutine(CreatePersonAndApplyAppearance(capturedEntry, pos, "replace", false));
                    },
                    spawnPrimary ? ContextMenuPanel.OptionKind.Primary : ContextMenuPanel.OptionKind.Normal));

                options.Add(new ContextMenuPanel.Option(
                    VPBTranslation.T("ctx.appearance.spawn_nocollide", "Spawn · collisions off"),
                    VPBTranslation.T("ctx.appearance.spawn_nocollide_sub", "Same spawn with collision disabled"),
                    () =>
                    {
                        ContextMenuPanel.RememberAppearanceAction(ContextMenuPanel.AppearanceActionId.SpawnNoCollide);
                        try { if (ContextMenuPanel.Instance != null) ContextMenuPanel.Instance.Hide(); } catch { }
                        StartCoroutine(CreatePersonAndApplyAppearance(capturedEntry, pos, "replace", true));
                    },
                    noCollidePrimary ? ContextMenuPanel.OptionKind.Primary : ContextMenuPanel.OptionKind.Quiet));
            }

            if (options.Count > 0)
            {
                string title = entry != null ? entry.Name : "Menu";
                if (entry is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Creator))
                {
                    title += "\n<color=#9aa3ad><size=14>by " + vfe.Package.Creator + "</size></color>";
                }
                string stickyHint = type == ItemType.Scene
                    ? ContextMenuPanel.GetLastSceneAction()
                    : ContextMenuPanel.GetLastAppearanceAction();
                if (!string.IsNullOrEmpty(stickyHint))
                {
                    title += "\n<color=#9aa3ad><size=14>"
                        + VPBTranslation.T("ctx.skip_hint", "Alt/Ctrl+drop = repeat last")
                        + "</size></color>";
                }
                ContextMenuPanel.Instance.Show(position, options, title);
            }
            else
            {
                if (type == ItemType.Scene) LoadSceneFile(entry.Uid);
                else if (atom != null) ApplyClothingToAtom(atom, entry.Uid);
            }
        }

        private Atom ResolveImportTargetAtom(Atom dropAtom)
        {
            if (SceneUtils.IsPersonLikeAtom(dropAtom))
                return dropAtom;
            if (Panel == null) return null;
            try
            {
                Atom sel = Panel.SelectedTargetAtom;
                if (SceneUtils.IsPersonLikeAtom(sel))
                    return sel;
            }
            catch { }
            return null;
        }

        private List<ContextMenuPanel.Option> BuildAddToSceneMoreOptions(FileEntry entry, Atom importTarget)
        {
            List<ContextMenuPanel.Option> addOpts = new List<ContextMenuPanel.Option>(10);
            FileEntry captured = entry;
            string uid = entry != null ? entry.Uid : null;
            string last = ContextMenuPanel.GetLastSceneAction();

            addOpts.Add(ContextMenuPanel.Option.Section(
                VPBTranslation.T("ctx.scene.place_section", "Place near me")));

            addOpts.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("ctx.scene.persons_near", "Persons near me"),
                VPBTranslation.T("ctx.scene.persons_near_sub", "Persons only, then move in front of you"),
                () =>
                {
                    ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.PersonsNear);
                    MergeSceneFile(uid, UI.SceneAddMode.PersonsOnly, true);
                },
                KindForSceneAction(ContextMenuPanel.SceneActionId.PersonsNear, last)));

            addOpts.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("ctx.scene.props_near", "Props near me"),
                VPBTranslation.T("ctx.scene.props_near_sub", "Props & environment, then move in front of you"),
                () =>
                {
                    ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.PropsNear);
                    MergeSceneFile(uid, UI.SceneAddMode.NonPersons, true);
                },
                KindForSceneAction(ContextMenuPanel.SceneActionId.PropsNear, last)));

            addOpts.Add(ContextMenuPanel.Option.Section(
                VPBTranslation.T("ctx.scene.tools_section", "Tools")));

            addOpts.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("ctx.scene.pick_atoms", "Pick atoms…"),
                VPBTranslation.T("ctx.scene.pick_atoms_sub", "Scene Import → Atoms checklist"),
                () =>
                {
                    ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.PickAtoms);
                    OpenSceneImportSidebar(captured, importTarget, VpbResourceType.Atoms);
                },
                KindForSceneAction(ContextMenuPanel.SceneActionId.PickAtoms, last)));

            addOpts.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("ctx.scene.import_open", "Open Scene Import"),
                VPBTranslation.T("ctx.scene.import_open_sub", "Full import sidebar for this scene"),
                () =>
                {
                    ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.ImportOpen);
                    OpenSceneImportSidebar(captured, importTarget);
                },
                KindForSceneAction(ContextMenuPanel.SceneActionId.ImportOpen, last)));

            addOpts.Add(ContextMenuPanel.Option.Section(
                VPBTranslation.T("ctx.scene.advanced", "Advanced")));

            addOpts.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("ctx.scene.merge_filter", "Merge load (no persons)"),
                VPBTranslation.T("ctx.scene.merge_filter_sub", "VaM filtered merge — may blank view"),
                () =>
                {
                    ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.MergeFilter);
                    MergeSceneFile(uid, UI.SceneAddMode.NonPersonsMergeLoad, false);
                },
                KindForSceneAction(ContextMenuPanel.SceneActionId.MergeFilter, last)));

            addOpts.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("ctx.scene.merge_full", "Entire scene"),
                VPBTranslation.T("ctx.scene.merge_full_sub", "Full LoadMerge · persons + everything"),
                () =>
                {
                    ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.FullMerge);
                    MergeSceneFile(uid, UI.SceneAddMode.FullMerge, false);
                },
                KindForSceneAction(ContextMenuPanel.SceneActionId.FullMerge, last)));

            addOpts.Add(new ContextMenuPanel.Option(
                VPBTranslation.T("ctx.scene.merge_full_near", "Entire scene near me"),
                VPBTranslation.T("ctx.scene.merge_full_near_sub", "Full merge, then move new atoms to you"),
                () =>
                {
                    ContextMenuPanel.RememberSceneAction(ContextMenuPanel.SceneActionId.FullMergeNear);
                    MergeSceneFile(uid, UI.SceneAddMode.FullMerge, true);
                },
                KindForSceneAction(ContextMenuPanel.SceneActionId.FullMergeNear, last)));

            return addOpts;
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
