using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        // Selection toolbox ("tbox")
        private GameObject tbox;
        private Text tboxLabel;
        private GameObject tboxCopyPkgNamesBtn;
        private GameObject tboxDeleteBtn;

        private void EnsureTboxUI()
        {
            if (tbox != null) return;
            if (backgroundBoxGO == null) return;

            // Position above the hover-path bar (which sits at y=60..120 in the pane)
            tbox = UI.AddChildGOImage(
                backgroundBoxGO,
                new Color(0f, 0f, 0f, 0.85f),
                AnchorPresets.hStretchBottom,
                0,
                44,
                new Vector2(0, 120)
            );
            tbox.name = "SelectionToolbox";

            var img = tbox.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;

            // Label (left)
            {
                var labelGO = new GameObject("Label");
                labelGO.transform.SetParent(tbox.transform, false);
                tboxLabel = labelGO.AddComponent<Text>();
                tboxLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                tboxLabel.fontSize = 18;
                tboxLabel.color = Color.white;
                tboxLabel.alignment = TextAnchor.MiddleLeft;
                tboxLabel.text = "";
                tboxLabel.raycastTarget = false;

                var rt = labelGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(12, 0);
                rt.offsetMin = new Vector2(12, 0);
                rt.offsetMax = new Vector2(-520, 0); // leave room for right buttons
            }

            // Right buttons (Copy, Delete)
            {
                tboxCopyPkgNamesBtn = UI.CreateUIButton(
                    tbox,
                    210,
                    34,
                    "Copy Package Names",
                    16,
                    -12 - 220,
                    0,
                    AnchorPresets.middleRight,
                    CopySelectedPackageNamesToClipboard
                );
                tboxCopyPkgNamesBtn.name = "Tbox_CopyPackageNames";

                tboxDeleteBtn = UI.CreateUIButton(
                    tbox,
                    180,
                    34,
                    "Delete",
                    16,
                    -12,
                    0,
                    AnchorPresets.middleRight,
                    TboxDeleteSelectedPackages
                );
                tboxDeleteBtn.name = "Tbox_Delete";
                try
                {
                    var delImg = tboxDeleteBtn.GetComponent<Image>();
                    if (delImg != null) delImg.color = new Color(0.35f, 0.15f, 0.15f, 1f);
                }
                catch { }
            }

            tbox.SetActive(false);
        }

        private void UpdateSelectionContextMenu()
        {
            if (canvas == null) return;
            EnsureTboxUI();
            if (tbox == null) return;

            int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
            bool visible = sel > 0;
            if (tbox.activeSelf != visible) tbox.SetActive(visible);
            if (!visible) return;

            if (tboxLabel != null)
                tboxLabel.text = sel == 1 ? "1 item selected" : $"{sel} items selected";
        }

        private void CopySelectedPackageNamesToClipboard()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;

                    string uid = TryGetPackageUidForEntry(f);
                    if (string.IsNullOrEmpty(uid)) continue;
                    set.Add(uid + ".var");
                }

                if (set.Count == 0)
                {
                    ShowTemporaryStatus("No package names found in selection.");
                    return;
                }

                var list = set.ToList();
                list.Sort(StringComparer.OrdinalIgnoreCase);
                string text = string.Join("\n", list.ToArray());

                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus($"Copied {list.Count} package name(s) to clipboard.", 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] CopySelectedPackageNamesToClipboard error: " + ex.Message);
                ShowTemporaryStatus("Copy failed. See log.", 2f);
            }
        }

        private static string TryGetPackageUidForEntry(FileEntry f)
        {
            if (f is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                return vfe.Package.Uid;

            if (f is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                return ple.Package.Uid;

            if (f is MissingPackageListEntry mp && !string.IsNullOrEmpty(mp.RequestedUid))
                return mp.RequestedUid;

            // Fall back to parsing .var filename from the path (handles package entries and some virtual cases)
            string p = f.Path ?? "";
            if (string.IsNullOrEmpty(p)) return null;

            // Strip internal-file suffix (":/...")
            int internalSep = p.IndexOf(":/", StringComparison.Ordinal);
            if (internalSep >= 0) p = p.Substring(0, internalSep);

            // Normalize and extract filename without extension
            p = p.Replace('\\', '/');
            if (!p.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) return null;

            int slash = p.LastIndexOf('/');
            string file = (slash >= 0) ? p.Substring(slash + 1) : p;
            if (file.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - 4);

            return string.IsNullOrEmpty(file) ? null : file;
        }
    }
}

