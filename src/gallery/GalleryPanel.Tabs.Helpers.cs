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
    }
}

