using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void BuildRemoveClothingTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            List<Atom> personAtoms = SuperController.singleton != null
                ? SuperController.singleton.GetAtoms().Where(a => a != null && SceneUtils.IsPersonLikeAtom(a)).ToList()
                : new List<Atom>();

            if (personAtoms.Count <= 0) return;

            Color removeColor = ColorDangerRow;
            Color newItemColor = ColorNewItemRow;
            Color cancelColor = ColorCancelRow;
            Color allColor = ColorDangerAllRow;
            Color groupColor = ColorGroupRow;

            AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);

            foreach (Atom target in personAtoms)
            {
                var items = new List<KeyValuePair<string, string>>();
                JSONStorable geometry = target.GetStorableByID("geometry");
                if (geometry != null)
                {
                    foreach (var name in geometry.GetBoolParamNames())
                    {
                        if (string.IsNullOrEmpty(name) || !name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                        string clothingUid = name.Substring(9);
                        JSONStorableBool jsb = geometry.GetBoolJSONParam(name);
                        if (jsb != null && jsb.val)
                        {
                            string p = clothingUid.Replace("\\", "/");
                            string[] parts = p.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                            string typeFolder = null;
                            int clothingIdx = -1;
                            for (int i = 0; i < parts.Length; i++)
                            {
                                if (string.Equals(parts[i], "clothing", StringComparison.OrdinalIgnoreCase))
                                {
                                    clothingIdx = i;
                                    break;
                                }
                            }
                            if (clothingIdx >= 0 && clothingIdx + 1 < parts.Length) typeFolder = parts[clothingIdx + 1];

                            string fileName = parts.Length > 0 ? Path.GetFileNameWithoutExtension(parts[parts.Length - 1]) : "";
                            string label = !string.IsNullOrEmpty(typeFolder)
                                ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + fileName)
                                : fileName;
                            if (!string.IsNullOrEmpty(label)) items.Add(new KeyValuePair<string, string>(clothingUid, label));
                        }
                    }
                }
                var options = DistinctSortFilterOptions(items, removeClothingFilter);

                if (options.Count > 0)
                {
                    if (personAtoms.Count > 1)
                    {
                        AddPersonHeaderRow(container.transform, trackedButtons, target.uid, groupColor);
                    }

                    CreateTabButton(container.transform, "REMOVE ALL (" + target.uid + ")", allColor, false, () => {
                        UIDraggableItem dragger = (isLeft ? leftRemoveAllClothingBtn : rightRemoveAllClothingBtn)?.GetComponent<UIDraggableItem>();
                        if (dragger == null) dragger = (isLeft ? leftRemoveAllClothingBtn : rightRemoveAllClothingBtn)?.AddComponent<UIDraggableItem>();
                        if (dragger != null) { dragger.Panel = this; dragger.RemoveAllClothing(target); }
                        UpdateTabs();
                    }, trackedButtons, null, "Remove ALL clothing from " + target.uid);

                    GameObject btnGO = trackedButtons.Count > 0 ? trackedButtons[trackedButtons.Count - 1] : null;
                    if (btnGO != null)
                    {
                        UIHoverDelegate del = btnGO.GetComponent<UIHoverDelegate>();
                        if (del == null) del = btnGO.AddComponent<UIHoverDelegate>();
                        del.OnHoverChange += (enter) => {
                            if (enter) ApplyClothingAllPreview(target);
                            else ClearClothingAllPreview(target);
                        };
                    }

                    foreach (var opt in options)
                    {
                        string uid = opt.Key;
                        string label = opt.Value;
                        string tooltip = "Remove: " + label + " from " + target.uid;

                        Color btnColor = removeColor;
                        if (_sessionInitialClothingUids.TryGetValue(target.uid, out var initialUids) && !initialUids.Contains(uid))
                        {
                            btnColor = newItemColor;
                        }

                        CreateTabButton(container.transform, label, btnColor, false, () => {
                            ClearClothingPreview();
                            UIDraggableItem dragger = (isLeft ? leftRemoveAllClothingBtn : rightRemoveAllClothingBtn)?.GetComponent<UIDraggableItem>();
                            if (dragger == null) dragger = (isLeft ? leftRemoveAllClothingBtn : rightRemoveAllClothingBtn)?.AddComponent<UIDraggableItem>();
                            if (dragger != null) { dragger.Panel = this; dragger.RemoveClothingItemByUid(target, uid); }
                            UpdateTabs();
                        }, trackedButtons, null, tooltip);
                        GameObject itemBtnGO = trackedButtons.Count > 0 ? trackedButtons[trackedButtons.Count - 1] : null;
                        if (itemBtnGO != null)
                        {
                            UIHoverDelegate del = itemBtnGO.GetComponent<UIHoverDelegate>();
                            if (del == null) del = itemBtnGO.AddComponent<UIHoverDelegate>();
                            del.OnHoverChange += (enter) => {
                                if (enter) ApplyClothingPreview(target, uid);
                                else ClearClothingPreview(target, uid);
                            };
                        }
                    }
                }
            }

            AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);
        }

        private void BuildRemoveHairTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            List<Atom> personAtoms = SuperController.singleton != null
                ? SuperController.singleton.GetAtoms().Where(a => a != null && SceneUtils.IsPersonLikeAtom(a)).ToList()
                : new List<Atom>();

            if (personAtoms.Count <= 0) return;

            Color removeColor = ColorDangerRow;
            Color cancelColor = ColorCancelRow;
            Color allColor = ColorDangerAllRow;
            Color groupColor = ColorGroupRow;

            AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);

            foreach (Atom target in personAtoms)
            {
                var items = new List<KeyValuePair<string, string>>();
                DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
                if (dcs != null && dcs.hairItems != null)
                {
                    foreach (var item in dcs.hairItems)
                    {
                        if (item == null || !item.active) continue;
                        string path = item.uid;
                        if (string.IsNullOrEmpty(path)) continue;
                        string p = path.Replace("\\", "/");
                        int idx = p.ToLowerInvariant().IndexOf("/hair/");
                        string label;
                        if (idx >= 0)
                        {
                            string[] parts = p.Substring(idx).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            string typeFolder = (parts.Length >= 2) ? parts[1] : null;
                            string fileName = Path.GetFileNameWithoutExtension(p);
                            label = !string.IsNullOrEmpty(typeFolder)
                                ? (CultureInfo.InvariantCulture.TextInfo.ToTitleCase(typeFolder.ToLowerInvariant()) + ": " + fileName)
                                : fileName;
                        }
                        else
                        {
                            label = Path.GetFileNameWithoutExtension(p);
                        }
                        if (!string.IsNullOrEmpty(label)) items.Add(new KeyValuePair<string, string>(item.uid, label));
                    }
                }
                var options = DistinctSortFilterOptions(items, removeHairFilter);

                if (options.Count > 0)
                {
                    if (personAtoms.Count > 1)
                    {
                        AddPersonHeaderRow(container.transform, trackedButtons, target.uid, groupColor);
                    }

                    CreateTabButton(container.transform, "REMOVE ALL (" + target.uid + ")", allColor, false, () => {
                        UIDraggableItem dragger = (isLeft ? leftRemoveAllHairBtn : rightRemoveAllHairBtn)?.GetComponent<UIDraggableItem>();
                        if (dragger == null) dragger = (isLeft ? leftRemoveAllHairBtn : rightRemoveAllHairBtn)?.AddComponent<UIDraggableItem>();
                        if (dragger != null) { dragger.Panel = this; dragger.RemoveAllHair(target); }
                        UpdateTabs();
                    }, trackedButtons, null, "Remove ALL hair from " + target.uid);

                    GameObject btnGO = trackedButtons.Count > 0 ? trackedButtons[trackedButtons.Count - 1] : null;
                    if (btnGO != null)
                    {
                        UIHoverDelegate del = btnGO.GetComponent<UIHoverDelegate>();
                        if (del == null) del = btnGO.AddComponent<UIHoverDelegate>();
                        del.OnHoverChange += (enter) => {
                            if (enter) ApplyHairAllPreview(target);
                            else ClearHairAllPreview(target);
                        };
                    }

                    foreach (var opt in options)
                    {
                        string uid = opt.Key;
                        string label = opt.Value;
                        string tooltip = "Remove: " + label + " from " + target.uid;
                        CreateTabButton(container.transform, label, removeColor, false, () => {
                            ClearHairPreview();
                            UIDraggableItem dragger = (isLeft ? leftRemoveAllHairBtn : rightRemoveAllHairBtn)?.GetComponent<UIDraggableItem>();
                            if (dragger == null) dragger = (isLeft ? leftRemoveAllHairBtn : rightRemoveAllHairBtn)?.AddComponent<UIDraggableItem>();
                            if (dragger != null) { dragger.Panel = this; dragger.RemoveHairItemByUid(target, uid); }
                            UpdateTabs();
                        }, trackedButtons, null, tooltip);
                        GameObject itemBtnGO = trackedButtons.Count > 0 ? trackedButtons[trackedButtons.Count - 1] : null;
                        if (itemBtnGO != null)
                        {
                            UIHoverDelegate del = itemBtnGO.GetComponent<UIHoverDelegate>();
                            if (del == null) del = itemBtnGO.AddComponent<UIHoverDelegate>();
                            del.OnHoverChange += (enter) => {
                                if (enter) ApplyHairPreview(target, uid);
                                else ClearHairPreview(target, uid);
                            };
                        }
                    }
                }
            }

            AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);
        }

        private void BuildRemoveAtomTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (SuperController.singleton == null) return;

            var options = SuperController.singleton.GetAtoms()
                .Where(a => a != null && !string.IsNullOrEmpty(a.uid) && !a.uid.StartsWith("CoreControl") && !a.uid.Equals("CameraRig"))
                .Select(a => new KeyValuePair<string, string>(a.uid, (!string.IsNullOrEmpty(a.type) ? a.type + ": " : "") + a.uid))
                .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase).ToList();

            options = DistinctSortFilterOptions(options, removeAtomFilter);
            Color removeColor = ColorDangerRow;
            Color cancelColor = ColorCancelRow;
            AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);

            foreach (var opt in options)
            {
                string uid = opt.Key;
                string label = opt.Value;
                string tooltip = "Remove: " + uid;
                CreateTabButton(container.transform, label, removeColor, false, () => {
                    Atom a = SuperController.singleton.GetAtomByUid(uid);
                    if (a != null) { PushUndoSnapshotForAtomRemoval(a); SuperController.singleton.RemoveAtom(a); }
                    UpdateTabs();
                }, trackedButtons, null, tooltip);
            }

            if (options.Count > 0)
            {
                AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);
            }
        }
    }
}

