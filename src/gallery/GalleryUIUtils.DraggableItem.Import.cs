using System;
using MVR.FileManagement;
using UnityEngine;

namespace VPB
{
    public partial class UIDraggableItem : MonoBehaviour, UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IEndDragHandler
    {
        /// <summary>
        /// Route scene→person import through the Scene Import sidebar (single power surface).
        /// Retires the old Clothing/Appearance-only floating drill-down.
        /// </summary>
        private void OpenSceneImportSidebar(FileEntry entry, Atom targetAtom)
        {
            OpenSceneImportSidebar(entry, targetAtom, false, VpbResourceType.Appearance);
        }

        private void OpenSceneImportSidebar(FileEntry entry, Atom targetAtom, VpbResourceType preferredType)
        {
            OpenSceneImportSidebar(entry, targetAtom, true, preferredType);
        }

        private void OpenSceneImportSidebar(
            FileEntry entry,
            Atom targetAtom,
            bool applyPreferredType,
            VpbResourceType preferredType)
        {
            try
            {
                if (ContextMenuPanel.Instance != null)
                    ContextMenuPanel.Instance.Hide();
            }
            catch { }

            if (Panel == null)
            {
                LogUtil.LogWarning("[VPB] OpenSceneImportSidebar: no gallery panel.");
                return;
            }

            if (applyPreferredType)
                Panel.OpenImportSidebarWith(entry, targetAtom, preferredType);
            else
                Panel.OpenImportSidebarWith(entry, targetAtom);

            try
            {
                Panel.ShowTemporaryStatus(
                    VPBTranslation.T("ctx.scene.import_opened", "Scene Import opened."),
                    1.5f);
            }
            catch { }
        }
    }
}
