using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using MVR.FileManagement;

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

        internal void QuickMenu_RandomSceneImport()
        {
            try { StartCoroutine(RandomSceneImportRoutine()); } catch { }
        }

        private IEnumerator RandomSceneImportRoutine()
        {
            // Capture current view state.
            string prevTitle = null;
            string prevExt = null;
            string prevPath = null;
            try { prevTitle = currentCategoryTitle; } catch { }
            try { prevExt = currentExtension; } catch { }
            try { prevPath = currentPath; } catch { }

            bool navigated = false;

            // Navigate to Scenes category when not already there so we get a scene file pool.
            if (!string.Equals(prevTitle, "Scenes", StringComparison.Ordinal))
            {
                Gallery.Category cat = default(Gallery.Category);
                bool catFound = false;
                try
                {
                    if (categories != null)
                    {
                        for (int i = 0; i < categories.Count; i++)
                        {
                            var c = categories[i];
                            if (string.Equals(c.name, "Scenes", StringComparison.OrdinalIgnoreCase))
                            { cat = c; catFound = true; break; }
                        }
                    }
                }
                catch { catFound = false; }

                if (catFound)
                {
                    try { Show(cat.name, cat.extension, cat.path); } catch { }
                    navigated = true;
                    yield return null;
                    int guard = 0;
                    while (refreshCoroutine != null && guard < 600) { guard++; yield return null; }
                    if (guard == 0) yield return null;
                }
            }

            // Pick from the current scene file pool.
            var pool = (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                ? currentFilteredFiles : lastFilteredFiles;

            if (pool == null || pool.Count == 0)
            {
                LogUtil.LogWarning("[VPB] Random Scene Import: no scenes in pool.");
                if (navigated && !string.IsNullOrEmpty(prevTitle))
                    try { Show(prevTitle, prevExt, prevPath); } catch { }
                yield break;
            }

            FileEntry sceneFile = pool[UnityEngine.Random.Range(0, pool.Count)];

            // LoadSourceScene is synchronous: populates importSidebarSourcePersonIds + importSidebarLoadedSceneJSON.
            try { LoadSourceScene(sceneFile); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB] Random Scene Import: LoadSourceScene failed: " + ex.Message); }

            // Restore view before any heavier work so the UI doesn't stay on Scenes.
            if (navigated && !string.IsNullOrEmpty(prevTitle))
                try { Show(prevTitle, prevExt, prevPath); } catch { }

            if (importSidebarSourcePersonIds.Count == 0)
            {
                LogUtil.LogWarning("[VPB] Random Scene Import: no Person atoms in scene: "
                    + (sceneFile.Path ?? sceneFile.Uid ?? "?"));
                yield break;
            }

            // Pick the best female person atom from the scene.
            importSidebarSourceAtomId = PickBestFemalePersonId(
                importSidebarSourcePersonIds, importSidebarLoadedSceneJSON);

            // Ensure a target atom is selected.
            if (importSidebarTargetAtom == null)
            {
                try { RefreshTargetCandidates(); } catch { }
                try { TryAutoSelectTargetIfUnset(); } catch { }
                if (importSidebarTargetAtom == null)
                    importSidebarTargetAtom = GetBestTargetAtom();
            }

            if (importSidebarTargetAtom == null)
            {
                LogUtil.LogWarning("[VPB] Random Scene Import: no target atom.");
                yield break;
            }

            // Apply using current sidebar type/option settings.
            try { OnImportSidebarApplyClicked(); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB] Random Scene Import: apply failed: " + ex.Message); }

            // Status feedback.
            try
            {
                string sceneName = !string.IsNullOrEmpty(sceneFile.Path)
                    ? System.IO.Path.GetFileName(sceneFile.Path)
                    : (sceneFile.Uid ?? "?");
                string typeName = importSidebarMultiTypeMode
                    ? "Multi"
                    : ShortNameForType(importSidebarPresetType);
                ShowTemporaryStatus(
                    "Rnd Import: " + typeName + " \u2190 " + importSidebarSourceAtomId + " in " + sceneName,
                    2.5f);
            }
            catch { }

            // Refresh sidebar UI if open.
            if (importSidebarActive)
            {
                try { RefreshImportSidebarWizardHeader(); } catch { }
                try { RenderSourceList(); } catch { }
                try { RefreshTargetSelectionVisual(); } catch { }
            }
        }

        /// <summary>
        /// Selects the most appropriate female Person atom from a scene.
        /// Priority: geometry storable <c>useFemaleMorphSet=true</c> &gt; id name heuristic &gt; first atom.
        /// </summary>
        private static string PickBestFemalePersonId(List<string> personIds, JSONClass sceneJSON)
        {
            if (personIds == null || personIds.Count == 0) return null;
            if (personIds.Count == 1) return personIds[0];

            // JSON path: look for geometry storable with useFemaleMorphSet = true.
            if (sceneJSON != null)
            {
                JSONArray atoms = sceneJSON["atoms"] != null ? sceneJSON["atoms"].AsArray : null;
                if (atoms != null)
                {
                    foreach (string id in personIds)
                    {
                        for (int i = 0; i < atoms.Count; i++)
                        {
                            JSONClass atom = atoms[i].AsObject;
                            if (atom == null) continue;
                            if (atom["id"] == null || atom["id"].Value != id) continue;
                            if (atom["storables"] == null) break;
                            JSONArray storables = atom["storables"].AsArray;
                            for (int j = 0; j < storables.Count; j++)
                            {
                                JSONClass s = storables[j].AsObject;
                                if (s == null) continue;
                                if (s["id"] == null || s["id"].Value != "geometry") continue;
                                if (s.HasKey("useFemaleMorphSet") && s["useFemaleMorphSet"].AsBool)
                                    return id;
                                break;
                            }
                            break;
                        }
                    }
                }
            }

            // Fallback: atom-id name heuristic — skip atoms whose id looks male.
            foreach (string id in personIds)
            {
                if (!LooksLikeMalePersonId(id)) return id;
            }
            return personIds[0];
        }

        private static bool LooksLikeMalePersonId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string lower = id.ToLowerInvariant();
            if (lower.Contains("female")) return false; // "female" contains "male" — not male
            if (lower == "male") return true;
            if (lower.StartsWith("male") || lower.EndsWith("male")) return true;
            if (lower.Contains(" male") || lower.Contains("_male") || lower.Contains(".male")) return true;
            return false;
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

        internal void QuickMenu_TogglePerfMode()
        {
            try { ToggleFooterPerfMode(); } catch { }
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

