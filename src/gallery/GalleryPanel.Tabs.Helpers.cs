using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Pretty-name + search-scope diagnostics. Flip to true when investigating label/search behavior.
        internal static bool LogPrettyNameDiagnostics = false;
        private static int s_PrettyNameSampleCount;
        private const int PrettyNameSampleMax = 12;

        private static void LogPrettyNameSample(FileEntry file, string returned, string caller)
        {
            if (!LogPrettyNameDiagnostics) return;
            if (s_PrettyNameSampleCount >= PrettyNameSampleMax) return;
            try
            {
                s_PrettyNameSampleCount++;
                string kind = file == null ? "null" : file.GetType().Name;
                string nameRaw = file != null ? (file.Name ?? "<null>") : "<null>";
                bool pretty = VPBConfig.Instance != null && VPBConfig.Instance.GalleryPrettyPresetNames;
                LogUtil.LogWarning("[VPB] PRETTY " + caller + " pretty=" + pretty + " kind=" + kind + " raw='" + nameRaw + "' -> '" + (returned ?? "<null>") + "'");
            }
            catch { }
        }

        internal static void ResetPrettyNameDiagnosticsSample()
        {
            s_PrettyNameSampleCount = 0;
        }

        private static readonly Color ColorInactiveRow = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color ColorCancelRow = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color ColorGroupRow = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color ColorDangerRow = new Color(0.6f, 0.2f, 0.2f, 1f);
        private static readonly Color ColorDangerAllRow = new Color(0.8f, 0.2f, 0.2f, 1f);
        private static readonly Color ColorNewItemRow = new Color(0.2f, 0.5f, 0.4f, 1f);
        private static readonly Color ColorFacetActiveRow = new Color(0.35f, 0.35f, 0.6f, 1f);

        /// <summary>List row label: package uid (Creator.Package.Version) unless legacy file-name mode is on, or pretty mode is on (then the BA-style stripped name wins for every entry kind).</summary>
        private static string GetGalleryListRowDisplayName(FileEntry file)
        {
            if (file == null) return "[UNNAMED]";
            bool legacy = VPBConfig.Instance != null && VPBConfig.Instance.GalleryListNamesLegacyFileName;
            bool pretty = VPBConfig.Instance != null && VPBConfig.Instance.GalleryPrettyPresetNames;

            if (pretty)
            {
                string r = GetPrettyEntryDisplayName(file);
                LogPrettyNameSample(file, r, "ListRow");
                return r;
            }

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

        /// <summary>
        /// Mirrors BA's resourceDisplayName for every entry kind so pretty mode = stripped label everywhere it renders.
        /// Order of precedence:
        ///   1. .vap presets strip 7-char "Preset_" (BA ResourceManifest.cs:4029).
        ///   2. .json session plugin presets strip 8-char "Plugins_" (BA ResourceManifest.cs:4030).
        ///   3. VAR package rows (PackageListEntry / non-preset VarFileEntry) render <see cref="VarPackage.Name"/> only (BA PackagedRVGE.resourceDisplayName).
        ///   4. Missing-package rows keep their RequestedUid (no Package to read Name from).
        ///   5. Loose system files fall back to filename without extension.
        /// Couples display with search so what users see equals what they can type.
        /// </summary>
        internal static string GetPrettyEntryDisplayName(FileEntry file)
        {
            if (file == null) return "[UNNAMED]";

            string raw = file.Name;
            if (!string.IsNullOrEmpty(raw))
            {
                int dot = raw.LastIndexOf('.');
                string stem = dot > 0 ? raw.Substring(0, dot) : raw;
                string ext = dot > 0 ? raw.Substring(dot + 1) : "";

                if (string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase)
                    && stem.StartsWith("Preset_", StringComparison.Ordinal)
                    && stem.Length > 7)
                    return stem.Substring(7);

                if (string.Equals(ext, "json", StringComparison.OrdinalIgnoreCase)
                    && stem.StartsWith("Plugins_", StringComparison.Ordinal)
                    && stem.Length > 8)
                    return stem.Substring(8);
            }

            // Package-level rows (and non-preset VarFileEntry items that share a package) reduce to the package's Name.
            try
            {
                if (file is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Name))
                    return ple.Package.Name;
                if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                    return mple.RequestedUid;
                if (file is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Name))
                    return vfe.Package.Name;
            }
            catch { }

            if (!string.IsNullOrEmpty(raw))
            {
                int dot2 = raw.LastIndexOf('.');
                return dot2 > 0 ? raw.Substring(0, dot2) : raw;
            }
            return file.Path ?? "[UNNAMED]";
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
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.raycastTarget = false;
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
            ReconcileAutoGenderForCurrentTarget();
            RefreshFiles();
            UpdateTabs();
        }

        private string GetSelectedTargetGenderLabel()
        {
            try
            {
                Atom atom = SelectedTargetAtom;
                if (atom == null) return "None";
                if (AtomGenderUtils.IsMale(atom)) return "Male";
                if (AtomGenderUtils.IsFemale(atom)) return "Female";
                return "Unknown";
            }
            catch { return "Unknown"; }
        }

        private void ReconcileAutoGenderForCurrentTarget()
        {
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryAutoGenderFilter) return;
                string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
                bool isClothing = !string.IsNullOrEmpty(title) && title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHair = !string.IsNullOrEmpty(title) && title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isClothing && !isHair) return;

                string genderLabel = GetSelectedTargetGenderLabel();
                if (genderLabel != "Male" && genderLabel != "Female") return;
                bool atomMale = (genderLabel == "Male");

                if (isClothing && !_clothingGenderUserOverride)
                {
                    ClothingSubfilter targetFlag = atomMale ? ClothingSubfilter.Male : ClothingSubfilter.Female;
                    ClothingSubfilter genderBits = clothingSubfilter & (ClothingSubfilter.Male | ClothingSubfilter.Female);
                    if (genderBits == 0)
                    {
                        clothingSubfilter |= targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender apply: Clothing -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                    else if (genderBits != targetFlag)
                    {
                        clothingSubfilter = (clothingSubfilter & ~genderBits) | targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender swap: Clothing " + genderBits + " -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                }
                else if (isHair && !_hairGenderUserOverride)
                {
                    HairSubfilter targetFlag = atomMale ? HairSubfilter.Male : HairSubfilter.Female;
                    HairSubfilter genderBits = hairSubfilter & (HairSubfilter.Male | HairSubfilter.Female);
                    if (genderBits == 0)
                    {
                        hairSubfilter |= targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender apply: Hair -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                    else if (genderBits != targetFlag)
                    {
                        hairSubfilter = (hairSubfilter & ~genderBits) | targetFlag;
                        tagsCached = false;
                        LogUtil.Log("[VPB.Gallery] auto-gender swap: Hair " + genderBits + " -> " + targetFlag + " (target=" + genderLabel + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Gallery] ReconcileAutoGenderForCurrentTarget failed: " + ex.Message);
            }
        }

        private void OnTargetAtomChanged(string source)
        {
            try
            {
                string uid = "(none)";
                try { Atom a = SelectedTargetAtom; if (a != null) uid = a.uid; } catch { }
                string genderLabel = GetSelectedTargetGenderLabel();
                LogUtil.Log("[VPB.Gallery] target changed via " + (source ?? "unknown") + " -> uid='" + uid + "' gender=" + genderLabel);

                string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
                bool isClothing = !string.IsNullOrEmpty(title) && title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isHair = !string.IsNullOrEmpty(title) && title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isClothing && !isHair)
                {
                    LogUtil.Log("[VPB.Gallery] target change ignored for grid (active category '" + title + "' is not Clothing/Hair)");
                    return;
                }
                RefreshFilesAndTabs();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB.Gallery] OnTargetAtomChanged failed: " + ex.Message);
            }
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
    }
}

