using System.Collections.Generic;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal class QuickMenuSaveOption
        {
            public string Label;
            public string Tooltip;
            public System.Action Action;
            public bool Enabled;
        }

        // Internal wrappers so VamHookPlugin quick-menu buttons can trigger existing private actions.
        // (GalleryPanel is partial, so this file can call private members defined in other parts.)

        internal void QuickMenu_Undo()
        {
            try { Undo(); } catch { }
        }

        internal void QuickMenu_Redo()
        {
            try { Redo(); } catch { }
        }

        internal void QuickMenu_LoadRandom()
        {
            try { LoadRandom(); } catch { }
        }

        internal void QuickMenu_LoadRandomFromCategory(string categoryName, bool preserveUi, bool preserveTarget)
        {
            try
            {
                if (string.IsNullOrEmpty(categoryName)) return;
                StartCoroutine(QuickMenu_LoadRandomFromCategoryRoutine(categoryName, preserveUi, preserveTarget));
            }
            catch { }
        }

        private System.Collections.IEnumerator QuickMenu_LoadRandomFromCategoryRoutine(string categoryName, bool preserveUi, bool preserveTarget)
        {
            // Wait for category refresh before calling LoadRandom, otherwise we may pick from old list.
            if (string.IsNullOrEmpty(categoryName)) yield break;

            // Capture current panel view state.
            string prevTitle = null;
            string prevExt = null;
            string prevPath = null;
            try { prevTitle = currentCategoryTitle; } catch { prevTitle = null; }
            try { prevExt = currentExtension; } catch { prevExt = null; }
            try { prevPath = currentPath; } catch { prevPath = null; }

            string targetUid = null;
            if (preserveTarget)
            {
                try { targetUid = QuickMenu_GetSelectedTargetPersonUid(); } catch { targetUid = null; }
            }

            // Resolve category definition without touching UI first.
            // Note: some categories have display aliases (e.g. "Person Skin").
            string lookupName = categoryName;
            bool catFound = false;
            Gallery.Category cat = default(Gallery.Category);
            try
            {
                if (categories != null)
                {
                    // Alias fix: quickmenu "Skin" maps to category name "Person Skin" in some builds.
                    if (string.Equals(lookupName, "Skin", System.StringComparison.OrdinalIgnoreCase))
                    {
                        for (int pass = 0; pass < 2; pass++)
                        {
                            string name = (pass == 0) ? "Skin" : "Person Skin";
                            for (int i = 0; i < categories.Count; i++)
                            {
                                var c = categories[i];
                                if (string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase))
                                {
                                    cat = c;
                                    catFound = true;
                                    break;
                                }
                            }
                            if (catFound) break;
                        }
                    }
                    else
                    {
                    for (int i = 0; i < categories.Count; i++)
                    {
                        var c = categories[i];
                        if (string.Equals(c.name, lookupName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            cat = c;
                            catFound = true;
                            break;
                        }
                    }
                    }
                }
            }
            catch { catFound = false; }
            if (!catFound) yield break;

            // Temporarily show category so LoadRandom uses correct pool + auto-action rules.
            try { Show(cat.name, cat.extension, cat.path); } catch { }

            // Wait for async refresh to complete for new category.
            // At least one frame so RefreshFilesRoutine can start and bind lists.
            yield return null;
            int guard = 0;
            while (refreshCoroutine != null && guard < 600)
            {
                guard++;
                yield return null;
            }
            // If RefreshFiles did not run, still allow one more frame for list rebuild.
            if (guard == 0) yield return null;

            if (preserveTarget && !string.IsNullOrEmpty(targetUid))
            {
                try { QuickMenu_SetSelectedTargetPersonUid(targetUid); } catch { }
            }

            try { LoadRandom(); } catch { }

            if (preserveUi && !string.IsNullOrEmpty(prevTitle))
            {
                // Restore previous view (best-effort).
                try { Show(prevTitle, prevExt, prevPath); } catch { }
                if (preserveTarget && !string.IsNullOrEmpty(targetUid))
                {
                    try { QuickMenu_SetSelectedTargetPersonUid(targetUid); } catch { }
                }
            }
        }

        internal string QuickMenu_GetSelectedTargetPersonUid()
        {
            try { return SelectedTargetAtom != null ? SelectedTargetAtom.uid : null; }
            catch { return null; }
        }

        internal void QuickMenu_SetSelectedTargetPersonUid(string uid)
        {
            try
            {
                if (string.IsNullOrEmpty(uid)) return;
                RefreshTargetDropdown();
                if (personAtoms == null) return;

                int idx = -1;
                for (int i = 0; i < personAtoms.Count; i++)
                {
                    Atom a = personAtoms[i];
                    if (a == null) continue;
                    try { if (a.uid == uid) { idx = i; break; } } catch { }
                }
                if (idx < 0) return;
                if (targetDropdownValue == idx) return;

                targetDropdownValue = idx;
                UpdateTargetDropdownUI();
                OnTargetAtomChanged("quickmenu");
            }
            catch { }
        }

        internal void QuickMenu_Save()
        {
            // Save scene from gallery (opens VaM file dialog)
            try { SaveSceneFromGallery(); } catch { }
        }

        internal List<QuickMenuSaveOption> QuickMenu_GetSaveOptions()
        {
            var res = new List<QuickMenuSaveOption>();
            try
            {
                var opts = BuildSaveMenuOptions();
                if (opts == null) return res;
                for (int i = 0; i < opts.Count; i++)
                {
                    var o = opts[i];
                    if (o == null) continue;
                    res.Add(new QuickMenuSaveOption
                    {
                        Label = o.Label,
                        Tooltip = o.Tooltip,
                        Action = o.Action,
                        Enabled = o.Enabled,
                    });
                }
            }
            catch { }
            return res;
        }

        internal void QuickMenu_ToggleReplaceMode()
        {
            try { ToggleReplaceMode(); } catch { }
        }

        internal void QuickMenu_ToggleAutoHide()
        {
            try { ToggleAutoHideMode(); } catch { }
        }

        internal void QuickMenu_ToggleShowHiddenPackages()
        {
            try { ToggleGalleryShowHiddenPackages(); } catch { }
        }

        internal void QuickMenu_ToggleFpsCounter()
        {
            try
            {
                if (fpsText != null && fpsText.gameObject != null)
                    fpsText.gameObject.SetActive(!fpsText.gameObject.activeSelf);
            }
            catch { }
        }

        internal void QuickMenu_RemoveAllHair()
        {
            try
            {
                Atom target = null;
                try { target = SelectedTargetAtom; } catch { target = null; }
                if (target == null) try { target = GetBestTargetAtom(); } catch { target = null; }
                if (target == null) return;
                var go = new UnityEngine.GameObject("VPB_QM_RemoveAllHair");
                go.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                try
                {
                    var dragger = go.AddComponent<UIDraggableItem>();
                    dragger.Panel = this;
                    dragger.RemoveAllHair(target);
                }
                catch { }
                finally { try { UnityEngine.Object.Destroy(go); } catch { } }
            }
            catch { }
        }

        internal void QuickMenu_RemoveAllClothing()
        {
            try
            {
                Atom target = null;
                try { target = SelectedTargetAtom; } catch { target = null; }
                if (target == null) try { target = GetBestTargetAtom(); } catch { target = null; }
                if (target == null) return;
                var go = new UnityEngine.GameObject("VPB_QM_RemoveAllClothing");
                go.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
                try
                {
                    var dragger = go.AddComponent<UIDraggableItem>();
                    dragger.Panel = this;
                    dragger.RemoveAllClothing(target);
                }
                catch { }
                finally { try { UnityEngine.Object.Destroy(go); } catch { } }
            }
            catch { }
        }

        /// <summary>Same as the History side button: toggle History browse / usage filters.</summary>
        internal void QuickMenu_OpenGalleryHistory()
        {
            try
            {
                if (isFixedLocally) ToggleLeft(ContentType.History);
                else ToggleRight(ContentType.History);
            }
            catch { }
        }

        /// <summary>Open Cleanup mode using the same entry point as the toolbox Cleanup action.</summary>
        internal void QuickMenu_OpenCleanupMode()
        {
            try { TboxOpenCleanupView(); } catch { }
        }

        /// <summary>Toggle Cleanup mode: open when closed, exit to previous side state when open.</summary>
        internal void QuickMenu_ToggleCleanupMode()
        {
            try
            {
                if (cleanupModeActive) ExitCleanupModeForSidePanelNavigation();
                else TboxOpenCleanupView();
            }
            catch { }
        }
    }
}

