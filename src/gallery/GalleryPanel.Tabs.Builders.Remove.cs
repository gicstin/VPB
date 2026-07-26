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
        private struct RemoveTabItem
        {
            public string Uid;
            public string Label;
            public bool IsNew;
        }

        private static string FormatResourceTypeLabel(string normalizedPath, string markerFolder)
        {
            string p = normalizedPath.Replace("\\", "/");
            string pl = p.ToLowerInvariant();
            int idx = pl.IndexOf("/" + markerFolder + "/");
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
            return label;
        }

        private static List<RemoveTabItem> EnumerateRemovableClothing(Atom target)
        {
            var items = new List<RemoveTabItem>();
            if (target == null) return items;

            JSONStorable geometry = target.GetStorableByID("geometry");
            if (geometry == null) return items;

            foreach (var name in geometry.GetBoolParamNames())
            {
                if (string.IsNullOrEmpty(name) || !name.StartsWith("clothing:", StringComparison.OrdinalIgnoreCase)) continue;
                string clothingUid = name.Substring(9);
                JSONStorableBool jsb = geometry.GetBoolJSONParam(name);
                if (jsb == null || !jsb.val) continue;

                string label = FormatResourceTypeLabel(clothingUid, "clothing");
                if (!string.IsNullOrEmpty(label))
                    items.Add(new RemoveTabItem { Uid = clothingUid, Label = label });
            }
            return items;
        }

        private List<RemoveTabItem> EnumerateRemovableHair(Atom target)
        {
            var items = new List<RemoveTabItem>();
            if (target == null) return items;

            DAZCharacterSelector dcs = target.GetComponentInChildren<DAZCharacterSelector>();
            if (dcs == null || dcs.hairItems == null) return items;

            foreach (var item in dcs.hairItems)
            {
                if (item == null || !item.active) continue;
                string path = item.uid;
                if (string.IsNullOrEmpty(path)) continue;
                string label = FormatResourceTypeLabel(path, "hair");
                if (!string.IsNullOrEmpty(label))
                    items.Add(new RemoveTabItem { Uid = item.uid, Label = label });
            }
            return items;
        }

        private void BuildRemoveResourceTabs(
            GameObject container,
            List<GameObject> trackedButtons,
            bool isLeft,
            string resourceName,
            string filter,
            Func<Atom, List<RemoveTabItem>> enumerate,
            GameObject leftRemoveAllBtn,
            GameObject rightRemoveAllBtn,
            Action<Atom, GameObject> removeAll,
            Action<Atom, string, GameObject> removeOne,
            Action clearPreviewBeforeRemove,
            Action<Atom> applyAllPreview,
            Action<Atom> clearAllPreview,
            Action<Atom, string> applyOnePreview,
            Action<Atom, string> clearOnePreview,
            Func<Atom, RemoveTabItem, Color> getItemColor = null)
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
                List<RemoveTabItem> rawItems = enumerate(target);
                var options = new List<KeyValuePair<string, string>>(rawItems.Count);
                for (int i = 0; i < rawItems.Count; i++)
                {
                    RemoveTabItem item = rawItems[i];
                    options.Add(new KeyValuePair<string, string>(item.Uid, item.Label));
                }
                options = DistinctSortFilterOptions(options, filter);

                if (options.Count <= 0) continue;

                if (personAtoms.Count > 1)
                    AddPersonHeaderRow(container.transform, trackedButtons, target.uid, groupColor);

                GameObject removeAllBtnGO = null;
                CreateTabButton(container.transform, "REMOVE ALL (" + target.uid + ")", allColor, false, () =>
                {
                    clearPreviewBeforeRemove?.Invoke();
                    GameObject host = (isLeft ? leftRemoveAllBtn : rightRemoveAllBtn) ?? removeAllBtnGO;
                    removeAll(target, host);
                    UpdateTabs();
                }, trackedButtons, null, "Remove ALL " + resourceName + " from " + target.uid);
                removeAllBtnGO = trackedButtons.Count > 0 ? trackedButtons[trackedButtons.Count - 1] : null;

                if (removeAllBtnGO != null && applyAllPreview != null && clearAllPreview != null)
                {
                    UIHoverDelegate del = removeAllBtnGO.GetComponent<UIHoverDelegate>();
                    if (del == null) del = removeAllBtnGO.AddComponent<UIHoverDelegate>();
                    del.OnHoverChange += (enter) =>
                    {
                        if (enter) applyAllPreview(target);
                        else clearAllPreview(target);
                    };
                }

                foreach (var opt in options)
                {
                    string uid = opt.Key;
                    string label = opt.Value;
                    string tooltip = "Remove: " + label + " from " + target.uid;

                    RemoveTabItem tabItem = default(RemoveTabItem);
                    tabItem.Uid = uid;
                    tabItem.Label = label;
                    for (int ri = 0; ri < rawItems.Count; ri++)
                    {
                        if (string.Equals(rawItems[ri].Uid, uid, StringComparison.OrdinalIgnoreCase))
                        {
                            tabItem = rawItems[ri];
                            break;
                        }
                    }

                    Color btnColor = getItemColor != null ? getItemColor(target, tabItem) : removeColor;

                    GameObject itemBtnGO = null;
                    CreateTabButton(container.transform, label, btnColor, false, () =>
                    {
                        clearPreviewBeforeRemove?.Invoke();
                        GameObject host = (isLeft ? leftRemoveAllBtn : rightRemoveAllBtn) ?? itemBtnGO;
                        removeOne(target, uid, host);
                        UpdateTabs();
                    }, trackedButtons, null, tooltip);
                    itemBtnGO = trackedButtons.Count > 0 ? trackedButtons[trackedButtons.Count - 1] : null;

                    if (itemBtnGO != null && applyOnePreview != null && clearOnePreview != null)
                    {
                        UIHoverDelegate del = itemBtnGO.GetComponent<UIHoverDelegate>();
                        if (del == null) del = itemBtnGO.AddComponent<UIHoverDelegate>();
                        del.OnHoverChange += (enter) =>
                        {
                            if (enter) applyOnePreview(target, uid);
                            else clearOnePreview(target, uid);
                        };
                    }
                }
            }

            AddCloseSidePaneRow(container.transform, trackedButtons, isLeft, cancelColor);
        }

        private void BuildRemoveClothingTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            BuildRemoveResourceTabs(
                container, trackedButtons, isLeft, "clothing", removeClothingFilter,
                target =>
                {
                    List<RemoveTabItem> items = EnumerateRemovableClothing(target);
                    if (items.Count > 0
                        && _sessionInitialClothingUids.TryGetValue(target.uid, out HashSet<string> initialUids))
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            RemoveTabItem item = items[i];
                            item.IsNew = !initialUids.Contains(item.Uid);
                            items[i] = item;
                        }
                    }
                    return items;
                },
                leftRemoveAllClothingBtn, rightRemoveAllClothingBtn,
                (target, host) =>
                {
                    UIDraggableItem dragger = host != null ? host.GetComponent<UIDraggableItem>() : null;
                    if (dragger == null && host != null) dragger = host.AddComponent<UIDraggableItem>();
                    if (dragger != null) { dragger.Panel = this; dragger.RemoveAllClothing(target); }
                },
                (target, uid, host) =>
                {
                    UIDraggableItem dragger = host != null ? host.GetComponent<UIDraggableItem>() : null;
                    if (dragger == null && host != null) dragger = host.AddComponent<UIDraggableItem>();
                    if (dragger != null) { dragger.Panel = this; dragger.RemoveClothingItemByUid(target, uid); }
                },
                ClearClothingPreview,
                ApplyClothingAllPreview, ClearClothingAllPreview,
                ApplyClothingPreview, ClearClothingPreview,
                (target, item) => item.IsNew ? ColorNewItemRow : ColorDangerRow);
        }

        private void BuildRemoveHairTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            BuildRemoveResourceTabs(
                container, trackedButtons, isLeft, "hair", removeHairFilter,
                EnumerateRemovableHair,
                leftRemoveAllHairBtn, rightRemoveAllHairBtn,
                (target, host) =>
                {
                    UIDraggableItem dragger = host?.GetComponent<UIDraggableItem>();
                    if (dragger == null) dragger = host?.AddComponent<UIDraggableItem>();
                    if (dragger != null) { dragger.Panel = this; dragger.RemoveAllHair(target); }
                },
                (target, uid, host) =>
                {
                    UIDraggableItem dragger = host?.GetComponent<UIDraggableItem>();
                    if (dragger == null) dragger = host?.AddComponent<UIDraggableItem>();
                    if (dragger != null) { dragger.Panel = this; dragger.RemoveHairItemByUid(target, uid); }
                },
                ClearHairPreview,
                ApplyHairAllPreview, ClearHairAllPreview,
                ApplyHairPreview, ClearHairPreview);
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
