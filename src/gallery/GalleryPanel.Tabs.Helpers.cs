using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly Color ColorInactiveRow = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color ColorCancelRow = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color ColorGroupRow = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color ColorDangerRow = new Color(0.6f, 0.2f, 0.2f, 1f);
        private static readonly Color ColorDangerAllRow = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color ColorNewItemRow = new Color(0.2f, 0.5f, 0.4f, 1f);
        private static readonly Color ColorFacetActiveRow = new Color(0.35f, 0.35f, 0.6f, 1f);

        /// <summary>List row label: package uid (Creator.Package.Version) unless legacy file-name mode is on.</summary>
        private static string GetGalleryListRowDisplayName(FileEntry file)
        {
            if (file == null) return "[UNNAMED]";
            bool legacy = VPBConfig.Instance != null && VPBConfig.Instance.GalleryListNamesLegacyFileName;
            if (legacy)
                return string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name;
            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                    return vfe.Package.Uid;
                if (file is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                    return ple.Package.Uid;
                if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                    return mple.RequestedUid;
            }
            catch { }
            return string.IsNullOrEmpty(file.Name) ? file.Path ?? "[UNNAMED]" : file.Name;
        }

        private void SetGalleryListRowNameTooltip(GameObject nameGO, FileEntry file)
        {
            if (nameGO == null || file == null) return;
            try
            {
                bool legacy = VPBConfig.Instance != null && VPBConfig.Instance.GalleryListNamesLegacyFileName;
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    if (legacy)
                        AddTooltipPlain(
                            nameGO,
                            string.Format(
                                VPBTranslation.T("gallery.tooltip.package_uid", "Package: {0}.var"),
                                vfe.Package.Uid));
                    else
                    {
                        string hint = string.IsNullOrEmpty(vfe.InternalPath) ? vfe.Name : vfe.InternalPath.Replace('\\', '/');
                        AddTooltipPlain(nameGO, hint);
                    }
                }
                else if (file is PackageListEntry ple && ple.Package != null)
                {
                    if (legacy)
                        AddTooltipPlain(
                            nameGO,
                            string.Format(
                                VPBTranslation.T("gallery.tooltip.package_uid", "Package: {0}.var"),
                                ple.Package.Uid));
                    else if (!string.IsNullOrEmpty(ple.Path))
                        AddTooltipPlain(nameGO, ple.Path);
                }
            }
            catch { }
        }

        private static string FormatBytesForList(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            double d = bytes;
            int i = 0;
            while (d >= 1024.0 && i < suffix.Length - 1)
            {
                d /= 1024.0;
                i++;
            }
            if (i == 0) return bytes.ToString() + " " + suffix[i];
            return d.ToString("0.0") + " " + suffix[i];
        }

        private static void AddBorderEdge(GameObject parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta)
        {
            AddBorderEdge(parent, anchorMin, anchorMax, pivot, sizeDelta, Color.white);
        }

        private static void AddBorderEdge(GameObject parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 sizeDelta, Color color)
        {
            GameObject go = new GameObject("E");
            go.transform.SetParent(parent.transform, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = Vector2.zero;
            go.AddComponent<UnityEngine.UI.Image>().color = color;
        }

        /// <summary>Removes side-tab rows that pair a primary tab button with optional trailing controls (see <see cref="UI.CreateSideTabSquareIconButton"/>).</summary>
        private static void CleanupSideTabLabeledRows(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Transform ch = container.GetChild(i);
                if (ch == null) continue;
                string n = ch.gameObject.name;
                if (string.Equals(n, "SideTabLabeledRow", StringComparison.Ordinal)
                    || string.Equals(n, "TargetPersonRow", StringComparison.Ordinal))
                    UnityEngine.Object.Destroy(ch.gameObject);
            }
        }

        private void RefreshFilesAndTabs()
        {
            // Issue #101 QoL: when browsing Clothing/Hair with a target Person selected,
            // auto-apply gender toggle only if user did not already pick Male/Female.
            try
            {
                string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
                bool isClothing = !string.IsNullOrEmpty(title) && title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHair = !string.IsNullOrEmpty(title) && title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                Atom atom = SelectedTargetAtom;
                if (atom != null && (isClothing || isHair))
                {
                    bool atomMale = false;
                    try
                    {
                        JSONStorable geometry = atom.GetStorableByID("geometry");
                        if (geometry != null)
                        {
                            var charChooser = geometry.GetStringChooserJSONParam("character");
                            if (charChooser != null && !string.IsNullOrEmpty(charChooser.val)
                                && charChooser.val.StartsWith("Male", StringComparison.OrdinalIgnoreCase))
                                atomMale = true;
                        }
                    }
                    catch { }

                    if (isClothing)
                    {
                        bool hasGender = (clothingSubfilter & (ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0;
                        if (!hasGender)
                        {
                            clothingSubfilter |= atomMale ? ClothingSubfilter.Male : ClothingSubfilter.Female;
                            tagsCached = false;
                        }
                    }
                    else if (isHair)
                    {
                        bool hasGender = (hairSubfilter & (HairSubfilter.Male | HairSubfilter.Female)) != 0;
                        if (!hasGender)
                        {
                            hairSubfilter |= atomMale ? HairSubfilter.Male : HairSubfilter.Female;
                            tagsCached = false;
                        }
                    }
                }
            }
            catch { }
            RefreshFiles();
            UpdateTabs();
        }

        private void CloseSidePane(bool isLeft)
        {
            if (isLeft) leftActiveContent = leftPrevActiveContent;
            else rightActiveContent = rightPrevActiveContent;
            UpdateTabs();
        }

        private void AddCloseSidePaneRow(Transform container, List<GameObject> trackedButtons, bool isLeft, Color cancelColor)
        {
            if (container == null || trackedButtons == null) return;
            CreateTabButton(container, VPBTranslation.T("gallery.side.close", "Close"), cancelColor, false, () => CloseSidePane(isLeft), trackedButtons);
        }

        private void AddPersonHeaderRow(Transform container, List<GameObject> trackedButtons, string uid, Color groupColor)
        {
            if (container == null || trackedButtons == null) return;
            CreateTabButton(container, " PERSON: " + (uid ?? "") + " ", groupColor, true, null, trackedButtons);
        }

        private static bool MatchesFilter(string value, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<KeyValuePair<string, string>> DistinctSortFilterOptions(IEnumerable<KeyValuePair<string, string>> items, string filter)
        {
            if (items == null) return new List<KeyValuePair<string, string>>();
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in items)
            {
                if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value)) continue;
                if (!MatchesFilter(kvp.Value, filter)) continue;
                if (!seen.ContainsKey(kvp.Key)) seen[kvp.Key] = kvp.Value;
            }
            return seen.Select(k => new KeyValuePair<string, string>(k.Key, k.Value))
                .OrderBy(kvp => kvp.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Issue #101: Clothing/Hair items are gender-locked in VaM (a male item won't load on a female
        /// Person and vice-versa). When the gallery has a Person target selected, dim items whose
        /// classified gender does not match the target so the user can see at-a-glance what is unusable.
        /// </summary>
        private bool ShouldGreyoutForSelectedAtomGender(FileEntry file)
        {
            if (file == null) return false;
            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            if (string.IsNullOrEmpty(title)) return false;
            bool isClothingOrHair = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                                 || title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isClothingOrHair) return false;

            Atom atom = SelectedTargetAtom;
            if (atom == null) return false;

            string path = file.Path ?? "";
            ClothingLoadingUtils.ResourceKind k;
            ClothingLoadingUtils.ResourceGender fileG;
            ClothingLoadingUtils.ClassifyClothingHairPath(path, out k, out fileG);
            if (fileG == ClothingLoadingUtils.ResourceGender.Unknown) return false;
            if (ClothingLoadingUtils.IsDecalLikePath(path)) return false;

            bool atomMale = false;
            try
            {
                JSONStorable geometry = atom.GetStorableByID("geometry");
                if (geometry != null)
                {
                    var charChooser = geometry.GetStringChooserJSONParam("character");
                    if (charChooser != null && !string.IsNullOrEmpty(charChooser.val)
                        && charChooser.val.StartsWith("Male", StringComparison.OrdinalIgnoreCase))
                        atomMale = true;
                }
            }
            catch { return false; }

            bool fileMale = fileG == ClothingLoadingUtils.ResourceGender.Male;
            return atomMale != fileMale;
        }
    }
}

