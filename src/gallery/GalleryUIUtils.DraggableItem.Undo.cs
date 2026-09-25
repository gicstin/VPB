using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MVR.FileManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SimpleJSON;
using VPB.src.util;

namespace VPB
{
    public partial class UIDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private void PushUndoSnapshotForClothingHair(Atom target)
        {
            if (Panel == null || target == null) return;
            try
            {
                string atomUid = target.uid;
                ClothingLoadingUtils.ClothingHairUndoState snapshot =
                    ClothingLoadingUtils.CaptureClothingHairUndoState(target);

                Panel.PushUndo(() =>
                {
                    Atom undoAtom = SuperController.singleton.GetAtomByUid(atomUid);
                    if (undoAtom == null) return;
                    ClothingLoadingUtils.RestoreClothingHairUndoState(undoAtom, snapshot);
                }, GalleryPanel.DescribeUndoTargetAtom(
                    VPBTranslation.T("gallery.undo.clothing_hair", "Clothing & hair"), target));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PushUndoSnapshotForClothingHair exception: " + ex);
            }
        }

        private void PushUndoSnapshotForCua(Atom target)
        {
            if (Panel == null || target == null) return;
            try
            {
                string atomUid = target.uid;
                JSONStorableUrl urlParam = ResolveCuaAssetUrlParam(target);
                JSONStorableStringChooser nameParam = ResolveCuaAssetNameParam(target);
                if (urlParam == null && nameParam == null) return;

                string prevUrl = urlParam != null ? urlParam.val : null;
                string prevName = nameParam != null ? nameParam.val : null;
                GalleryPanel panel = Panel;

                Panel.PushUndo(() =>
                {
                    if (panel == null) return;
                    panel.StartCoroutine(RestoreCuaAssetCoroutine(atomUid, prevUrl, prevName));
                }, GalleryPanel.DescribeUndoTargetAtom(
                    VPBTranslation.T("gallery.undo.cua_asset", "CUA asset"), target));
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] PushUndoSnapshotForCua exception: " + ex);
            }
        }

        private static IEnumerator RestoreCuaAssetCoroutine(string atomUid, string prevUrl, string prevName)
        {
            Atom atom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null;
            if (atom == null) yield break;

            JSONStorableUrl urlParam = ResolveCuaAssetUrlParam(atom);
            if (urlParam != null) urlParam.val = prevUrl ?? "";

            if (string.IsNullOrEmpty(prevUrl) || string.IsNullOrEmpty(prevName)
                || string.Equals(prevName, "None", StringComparison.Ordinal)) yield break;

            yield return SelectCuaAssetCoroutine(atomUid, prevName);
        }

        private JSONClass ExtractAtomFromScene(JSONClass sceneJSON, string atomType)
        {
            if (sceneJSON == null || sceneJSON["atoms"] == null) return null;
            
            JSONArray atoms = sceneJSON["atoms"].AsArray;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (atoms[i]["type"].Value == atomType)
                {
                    JSONClass personAtom = atoms[i].AsObject;
                    JSONClass extracted = new JSONClass();
                    extracted["storables"] = personAtom["storables"];
                    if (personAtom["setUnlistedParamsToDefault"] != null)
                        extracted["setUnlistedParamsToDefault"] = personAtom["setUnlistedParamsToDefault"];
                    return extracted;
                }
            }
            return null;
        }

        private bool CheckDualPose()
        {
            if (_isDualPose.HasValue) return _isDualPose.Value;
            
            _isDualPose = false;
            
            if (FileEntry != null && FileEntry.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                string content = null;
                try
                {
                    string normalized = UI.NormalizePath(FileEntry.Path);
                    if (UI.IsLikelyVarPackageReference(normalized))
                    {
                         using (var reader = FileEntry.OpenStreamReader())
                         {
                             content = reader.ReadToEnd();
                         }
                    }
                    else
                    {
                         using (var reader = FileEntry.OpenStreamReader())
                         {
                             content = reader.ReadToEnd();
                         }
                    }

                    if (!string.IsNullOrEmpty(content))
                    {
                        _dualPoseNode = JSON.Parse(content);
                        if (_dualPoseNode != null)
                        {
                            if (_dualPoseNode["PeopleCount"] != null)
                            {
                                int count = _dualPoseNode["PeopleCount"].AsInt;
                                if (count >= 2)
                                {
                                    _isDualPose = true;
                                    LogUtil.LogVerbose($"[DragDropDebug] Detected Dual Pose: PeopleCount={count} in {FileEntry.Name}");
                                }
                                else
                                {
                                    LogUtil.LogVerbose($"[DragDropDebug] Not Dual Pose: PeopleCount={count} in {FileEntry.Name}");
                                }
                            }
                            else
                            {
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                     LogUtil.LogError($"[DragDropDebug] CheckDualPose error reading {FileEntry.Name}: {ex.Message}");
                }
            }
            return _isDualPose.Value;
        }

        public static void ActivateClothingHairItemPreset(Atom atom, FileEntry entry, bool isClothing)
        {
            ClothingLoadingUtils.ActivateClothingHairItemPreset(atom, entry, isClothing);
        }

        private bool IsAtomMale(Atom atom)
        {
            return AtomGenderUtils.IsMale(atom);
        }

        private enum ItemType { Clothing, Hair, Pose, Skin, Morphs, Appearance, Animation, BreastPhysics, Plugins, General, ClothingItem, HairItem, ClothingPreset, HairPreset, SubScene, Scene, CUA, Other }

        private ItemType GetItemType(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Path)) return ItemType.Other;
            string p = entry.Path.Replace('\\', '/');
            // .var display paths ("AddonPackages/pkg.var:/Custom/Atom/..."): prefix checks need internal path only
            int varSep = p.IndexOf(":/", StringComparison.Ordinal);
            if (varSep == 1 && char.IsLetter(p[0]))
                varSep = p.IndexOf(":/", varSep + 1, StringComparison.Ordinal);
            if (varSep >= 0 && varSep + 2 < p.Length)
                p = p.Substring(varSep + 2);
            bool isVap = p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase);
            bool isJson = p.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            bool isVam = p.EndsWith(".vam", StringComparison.OrdinalIgnoreCase);
            
            if (p.StartsWith("Custom/Atom/Person/AnimationPresets", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.Animation;
            if (p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.Appearance;
            if (p.StartsWith("Custom/Atom/Person/BreastPhysics", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.BreastPhysics;
            if (p.StartsWith("Custom/Atom/Person/Clothing", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.Clothing;
            if (p.StartsWith("Custom/Atom/Person/General", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.General;
            if (p.StartsWith("Custom/Atom/Person/GlutePhysics", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.General;
            if (p.StartsWith("Custom/Atom/Person/Hair", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.Hair;
            if (p.StartsWith("Custom/Atom/Person/Morphs", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.Morphs;
            if (p.StartsWith("Custom/Atom/Person/Plugins", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.Plugins;
            if ((p.StartsWith("Custom/Atom/Person/Pose", StringComparison.OrdinalIgnoreCase) && isVap) || p.EndsWith(".vac", StringComparison.OrdinalIgnoreCase)) return ItemType.Pose;
            if (p.StartsWith("Custom/Atom/Person/Skin", StringComparison.OrdinalIgnoreCase) && isVap) return ItemType.Skin;
            
            if (p.StartsWith("Custom/SubScene", StringComparison.OrdinalIgnoreCase) && isJson) return ItemType.SubScene;
            if (p.StartsWith("Saves/scene", StringComparison.OrdinalIgnoreCase) && isJson) return ItemType.Scene;

            if ((p.StartsWith("Custom/Clothing/Female", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Custom/Clothing/Male", StringComparison.OrdinalIgnoreCase)) && isVam)
            {
                return ItemType.ClothingItem;
            }
            if ((p.StartsWith("Custom/Hair/Female", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Custom/Hair/Male", StringComparison.OrdinalIgnoreCase)) && isVam)
            {
                return ItemType.HairItem;
            }
            if ((p.StartsWith("Custom/Clothing/Female", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Custom/Clothing/Male", StringComparison.OrdinalIgnoreCase)) && isVap)
            {
                return ItemType.ClothingPreset;
            }
            if ((p.StartsWith("Custom/Hair/Female", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Custom/Hair/Male", StringComparison.OrdinalIgnoreCase)) && isVap)
            {
                return ItemType.HairPreset;
            }

            if (p.StartsWith("Custom/Assets", StringComparison.OrdinalIgnoreCase) &&
                (p.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)))
            {
                return ItemType.CUA;
            }

            if (p.StartsWith("Custom/Scripts", StringComparison.OrdinalIgnoreCase) &&
                (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".cslist", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                return ItemType.Plugins;
            }
            if (p.StartsWith("Custom/PluginPresets", StringComparison.OrdinalIgnoreCase) && isVap)
            {
                return ItemType.Plugins;
            }

            if (isJson && p.IndexOf("scene", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ItemType.Scene;
            }
            if (isJson && p.IndexOf("pose", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ItemType.Pose;
            }
            if (p.EndsWith(".assetbundle", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase))
            {
                return ItemType.CUA;
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
                case ItemType.Hair: return "HairPresets";
                case ItemType.HairItem: return "HairPresets";
                case ItemType.ClothingPreset: return null;
                case ItemType.HairPreset: return null;
                case ItemType.Morphs: return "MorphPresets";
                case ItemType.Plugins: return "PluginPresets";
                case ItemType.Pose: return "PosePresets";
                case ItemType.Skin: return "SkinPresets";
                default: return null;
            }
        }
    }
}
