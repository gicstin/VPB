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
            QuickMenu_LoadRandom(null);
        }

        internal void QuickMenu_LoadRandom(FileEntry preselected)
        {
            try
            {
                if (preselected != null && ApplyPickedRandomEntry(preselected)) return;
                LoadRandom();
            }
            catch { }
        }

        internal void QuickMenu_RandomSceneImport()
        {
            QuickMenu_RandomSceneImport(null);
        }

        internal void QuickMenu_RandomSceneImport(FileEntry preselected)
        {
            try { StartCoroutine(RandomSceneImportRoutine(preselected)); } catch { }
        }

        private IEnumerator RandomSceneImportRoutine(FileEntry preselected)
        {
            string prevTitle = null;
            string prevExt = null;
            string prevPath = null;
            try { prevTitle = currentCategoryTitle; } catch { }
            try { prevExt = currentExtension; } catch { }
            try { prevPath = currentPath; } catch { }

            bool navigated = false;

            if (preselected == null && !string.Equals(prevTitle, "Scenes", StringComparison.Ordinal))
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

            FileEntry sceneFile = preselected;
            if (sceneFile == null)
            {
                var pool = (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                    ? currentFilteredFiles : lastFilteredFiles;

                if (pool == null || pool.Count == 0)
                {
                    LogUtil.LogWarning("[VPB] Random Scene Import: no scenes in pool.");
                    if (navigated && !string.IsNullOrEmpty(prevTitle))
                        try { Show(prevTitle, prevExt, prevPath); } catch { }
                    yield break;
                }

                sceneFile = VpbRandomHistory.Pick(GetRandomHistoryScope(), pool, selectedPath, true);
                if (sceneFile == null)
                {
                    LogUtil.LogWarning("[VPB] Random Scene Import: no usable scene in pool.");
                    if (navigated && !string.IsNullOrEmpty(prevTitle))
                        try { Show(prevTitle, prevExt, prevPath); } catch { }
                    yield break;
                }
            }
            else
            {
                try { VpbRandomHistory.Note(GetRandomHistoryScope(), sceneFile); } catch { }
            }

            try { LoadSourceScene(sceneFile); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB] Random Scene Import: LoadSourceScene failed: " + ex.Message); }

            yield return WaitForImportSourceSceneReady(30f);

            // Restore view before any heavier work so the UI doesn't stay on Scenes.
            if (navigated && !string.IsNullOrEmpty(prevTitle))
                try { Show(prevTitle, prevExt, prevPath); } catch { }

            if (importSidebarSourcePersonIds.Count == 0)
            {
                LogUtil.LogWarning("[VPB] Random Scene Import: no Person atoms in scene: "
                    + (sceneFile.Path ?? sceneFile.Uid ?? "?"));
                yield break;
            }

            importSidebarSourceAtomId = PickBestFemalePersonId(
                importSidebarSourcePersonIds, importSidebarLoadedSceneJSON);

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

            try { OnImportSidebarApplyClicked(); }
            catch (Exception ex) { LogUtil.LogWarning("[VPB] Random Scene Import: apply failed: " + ex.Message); }

            try
            {
                string sceneName = !string.IsNullOrEmpty(sceneFile.Path)
                    ? System.IO.Path.GetFileName(sceneFile.Path)
                    : (sceneFile.Uid ?? "?");
                string typeName = ImportSidebarSelectedTypesSummary();
                ShowTemporaryStatus(
                    "Rnd Import: " + typeName + " \u2190 " + importSidebarSourceAtomId + " in " + sceneName,
                    2.5f);
            }
            catch { }

            if (importSidebarActive)
            {
                try { RefreshImportSidebarWizardHeader(); } catch { }
                try { RenderSourceList(); } catch { }
                try { RefreshTargetSelectionVisual(); } catch { }
            }
        }

        private static string PickBestFemalePersonId(List<string> personIds, JSONClass sceneJSON)
        {
            if (personIds == null || personIds.Count == 0) return null;
            if (personIds.Count == 1) return personIds[0];

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
            if (lower.Contains("female")) return false;
            if (lower == "male") return true;
            if (lower.StartsWith("male", StringComparison.Ordinal) || lower.EndsWith("male", StringComparison.Ordinal)) return true;
            if (lower.Contains(" male") || lower.Contains("_male") || lower.Contains(".male")) return true;
            return false;
        }

        internal void QuickMenu_LoadRandomFromCategory(string categoryName, bool preserveUi, bool preserveTarget)
        {
            try
            {
                if (string.IsNullOrEmpty(categoryName)) return;
                StartCoroutine(QuickMenu_LoadRandomFromCategoryRoutine(categoryName, preserveUi, preserveTarget, null));
            }
            catch { }
        }

        /// <param name="preselected">Hover-preview pick applied instead of a fresh draw; null = pick after refresh.</param>
        private System.Collections.IEnumerator QuickMenu_LoadRandomFromCategoryRoutine(string categoryName, bool preserveUi, bool preserveTarget, FileEntry preselected)
        {
            // Wait for category refresh before calling LoadRandom, otherwise we may pick from old list.
            if (string.IsNullOrEmpty(categoryName)) yield break;

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

            string lookupName = categoryName;
            bool catFound = false;
            Gallery.Category cat = default(Gallery.Category);
            try
            {
                if (categories != null)
                {
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

            try { Show(cat.name, cat.extension, cat.path); } catch { }

            yield return null;
            int guard = 0;
            while (refreshCoroutine != null && guard < 600)
            {
                guard++;
                yield return null;
            }
            if (guard == 0) yield return null;

            if (preserveTarget && !string.IsNullOrEmpty(targetUid))
            {
                try { QuickMenu_SetSelectedTargetPersonUid(targetUid); } catch { }
            }

            if (preselected == null || !ApplyPickedRandomEntry(preselected))
            {
                try { LoadRandom(); } catch { }
            }

            if (preserveUi && !string.IsNullOrEmpty(prevTitle))
            {
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

        internal void QuickMenu_OpenGalleryHistory()
        {
            try
            {
                if (isFixedLocally) ToggleLeft(ContentType.History);
                else ToggleRight(ContentType.History);
            }
            catch { }
        }

        internal void QuickMenu_ToggleCreatorMode()
        {
            try { ToggleCreatorMode(); } catch { }
        }

        internal void QuickMenu_OpenStripScene()
        {
            try { OpenSceneStripKeepSelector(); } catch { }
        }

        internal void QuickMenu_OpenCleanupMode()
        {
            try { TboxOpenCleanupView(); } catch { }
        }

        internal void QuickMenu_ToggleCleanupMode()
        {
            try
            {
                if (cleanupModeActive) ExitCleanupModeForSidePanelNavigation();
                else TboxOpenCleanupView();
            }
            catch { }
        }

        /// <summary>Cycle the ★ presence filter (Rated only → Not rated → Off). Same as title-bar ★.</summary>
        internal void QuickMenu_ToggleStarFilter()
        {
            try { ToggleRatingSort(); } catch { }
        }

        internal bool QuickMenu_IsStarFilterEnabled()
        {
            try { return HasRatingPresenceFilter(); } catch { return false; }
        }
    }
}
