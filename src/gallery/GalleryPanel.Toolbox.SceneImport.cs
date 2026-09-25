using MeshVR;
using MVR.FileManagement;
using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using global::VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private static string BuildVarScopedJsonLoadPath(FileEntry entry)
        {
            if (entry == null) return null;

            if (!string.IsNullOrEmpty(entry.Uid))
            {
                string u = entry.Uid.Replace('\\', '/');
                int ux = u.IndexOf(":/", StringComparison.Ordinal);
                if (ux > 0)
                {
                    string pref = u.Substring(0, ux);
                    if (pref.IndexOf('/') < 0)
                        return u;
                }
            }

            string path = (entry.Path ?? "").Replace('\\', '/');
            int sep = path.IndexOf(":/", StringComparison.Ordinal);

            string uid = null;
            if (entry is VarFileEntry vfe)
                uid = vfe.GetRowPackageUid();

            if (string.IsNullOrEmpty(uid))
                uid = TryGetPackageUidForEntry(entry);

            // Prefer manifest/index UID + path-after-colon from gallery Path (fixes UID vs .var filename mismatch).
            if (sep >= 0 && !string.IsNullOrEmpty(uid))
                return uid + ":/" + NormalizeVarInternalPath(path.Substring(sep + 2));

            if (sep >= 0)
            {
                string prefix = sep > 0 ? path.Substring(0, sep) : "";
                uid = prefix.Split('/').Last().Replace(".var", string.Empty).Replace(".zip", string.Empty);
                return uid + ":/" + NormalizeVarInternalPath(path.Substring(sep + 2));
            }

            return path;
        }

        /// <summary>Zip/gallery paths sometimes contain a stray space after '/' (e.g. Saves/scene/ Emilie.json).</summary>
        private static string NormalizeVarInternalPath(string inner)
        {
            if (string.IsNullOrEmpty(inner)) return inner;
            inner = inner.Replace('\\', '/').TrimStart('/');
            while (inner.Contains("/ "))
                inner = inner.Replace("/ ", "/");
            return inner;
        }

        private void OpenImportSidebarFromSideButton(bool fromLeftRailButton, bool rightClick)
        {
            if (!ImportSidebarCategoryAllowed())
            {
                if (!TryNavigateGalleryToScenes())
                {
                    try
                    {
                        ShowTemporaryStatus(VPBTranslation.T(
                            "gallery.import.sidebar_gated_tip",
                            "Import sidebar opens in Scenes category only"), 2f);
                    }
                    catch { }
                    return;
                }
            }

            if (IsImportSidebarActive)
            {
                ToggleImportSidebar();
                return;
            }
            importSidebarForceOnLeft = PreferLeftSidePanelFromRail(fromLeftRailButton, rightClick);
            if (selectedFiles != null && selectedFiles.Count == 1)
                OpenImportSidebarWith(selectedFiles[0], SelectedTargetAtom);
            else
                ToggleImportSidebar();
        }

        private void ToggleSuppressAppearanceScale()
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.SuppressAppearanceScaleChange =
                !VPBConfig.Instance.SuppressAppearanceScaleChange;
            // Disk only — Save(true) fires ConfigChanged → ApplyInnerPaneScale/UpdateLayout → side-rail gap jump.
            try { VPBConfig.Instance.Save(false); } catch { }
            RefreshSuppressScaleBtnVisual();
            RefreshImportSidebarOptionToggles();
            ShowTemporaryStatus(
                VPBConfig.Instance.SuppressAppearanceScaleChange
                    ? "Scale change on Appearance Import: SUPPRESSED"
                    : "Scale change on Appearance Import: ALLOWED",
                1.5f);
        }

        private void RefreshSuppressScaleBtnVisual()
        {
            if (tboxSuppressScaleBtn == null) return;
            var img = tboxSuppressScaleBtn.GetComponent<UnityEngine.UI.Image>();
            if (img == null) return;
            bool on = VPBConfig.Instance != null && VPBConfig.Instance.SuppressAppearanceScaleChange;
            img.color = on
                ? new Color(0.2f, 0.45f, 0.75f, 0.9f)
                : GalleryUiColorTokens.ChromeIconWell;
        }
    }
}
