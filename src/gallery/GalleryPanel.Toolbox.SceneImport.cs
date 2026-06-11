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
        /// <summary>
        /// Path passed to <see cref="SuperController.LoadJSON"/> for a file inside a .var.
        /// Must use the registered package UID from the VAR meta (same as <see cref="VarPackage.Uid"/>),
        /// not the .var filename from disk — filenames can differ in casing/spelling from the UID.
        /// </summary>
        private static string BuildVarScopedJsonLoadPath(FileEntry entry)
        {
            if (entry == null) return null;

            // Prefer indexed Uid (packageUid:/exact/internal/path) — matches zip entry keys; rebuilding from Path
            // can diverge when folders/files have irregular spaces (e.g. "12 05/ KM214" vs "12 05/KM214").
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
            // VaM virtual refs require ":/" after the UID (same as VarFileEntry), not "uid:Saves/..." alone.
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

        /// <summary>
        /// Zip/gallery paths sometimes contain a stray space after '/' (e.g. Saves/scene/ Emilie.json).
        /// VaM's LoadJSON is picky; collapse "/ " without stripping intentional spaces inside file names elsewhere.
        /// </summary>
        private static string NormalizeVarInternalPath(string inner)
        {
            if (string.IsNullOrEmpty(inner)) return inner;
            inner = inner.Replace('\\', '/').TrimStart('/');
            while (inner.Contains("/ "))
                inner = inner.Replace("/ ", "/");
            return inner;
        }

        // Side-rail Scene Import button is the sidebar toggle: open (preselecting the picked scene + target)
        // when closed, close on a second click. Docked: LMB=left column, RMB=right column. Floating: LMB follows clicked rail.
        private void OpenImportSidebarFromSideButton(bool fromLeftRailButton, bool rightClick)
        {
            if (IsImportSidebarActive)
            {
                ToggleImportSidebar();
                return;
            }
            bool preferLeftRail;
            if (isFixedLocally)
                preferLeftRail = !rightClick;
            else if (rightClick)
                preferLeftRail = !fromLeftRailButton;
            else
                preferLeftRail = fromLeftRailButton;
            importSidebarForceOnLeft = preferLeftRail;
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
            try { VPBConfig.Instance.Save(true, true); } catch { }
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
                : UI.IconButtonBackdrop;
        }

    }
}
