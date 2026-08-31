using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Strip Scene create-fill: pick default SubScene from gallery (SubScenes category).
    /// Jakob — banner pick pattern. Single selection (one default path).
    /// Confirm Pick = apply; Cancel Pick = abort (Esc).
    /// </summary>
    public partial class GalleryPanel
    {
        private bool _stripKeepSubScenePickActive;
        private string _stripKeepSubScenePickExpectedTitle;
        private GameObject _stripKeepSubScenePickBannerRoot;
        private Text _stripKeepSubScenePickBannerLabel;

        public bool IsStripKeepSubScenePickActive()
        {
            return _stripKeepSubScenePickActive;
        }

        private void StripKeepStartSubScenePickMode()
        {
            if (_stripKeepSubScenePickActive) return;
            if (backgroundBoxGO == null) return;


            // Collapsed pane deactivates content subtree — must expand for pick (Fitts / visibility).
            try
            {
                if (isCollapsed)
                    SetCollapsed(false);
            }
            catch { }
            try { _userHidden = false; } catch { }
            try
            {
                if (canvas != null && !canvas.enabled)
                    ApplyImmediateVisibility(true);
            }
            catch { }

            try
            {
                if (settingsListViewActive)
                    ExitInternalSettingsMode(true);
            }
            catch { }

            // Hide strip chrome; keep session (selections / create-fill state).
            if (IsStripKeepSelectorOpen())
            {
                try { HideStripKeepSelectorInternal(resetSession: false); } catch { }
            }

            try
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
            }
            catch { }

            _stripKeepSubScenePickActive = true;
            _stripKeepSubScenePickExpectedTitle = null;

            if (!TryNavigateStripKeepSubScenePickCategory(out string resolvedTitle))
            {
                _stripKeepSubScenePickActive = false;
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_pick_cat_missing",
                        "Could not open SubScenes category."),
                    3f);
                try { StripKeepReopenSelectorAfterPick(); } catch { }
                return;
            }

            _stripKeepSubScenePickExpectedTitle = resolvedTitle;
            StripKeepEnsureSubScenePickBanner();
            StripKeepUpdateSubScenePickBanner();
            try { StartCoroutine(StripKeepPreselectSavedSubSceneDeferred()); } catch { }

            ShowTemporaryStatus(
                VPBTranslation.T(
                    "gallery.creator.strip_subscene_pick_hint",
                    "Pick a SubScene — Confirm Pick to use, Cancel Pick to abort."),
                3.5f);
        }

        private System.Collections.IEnumerator StripKeepPreselectSavedSubSceneDeferred()
        {
            // Wait for category Show to populate filtered list.
            yield return null;
            yield return null;
            if (!_stripKeepSubScenePickActive) yield break;
            try { StripKeepTryPreselectSavedSubScene(); } catch { }
            StripKeepUpdateSubScenePickBanner();
        }

        private bool TryNavigateStripKeepSubScenePickCategory(out string resolvedTitle)
        {
            resolvedTitle = null;
            try
            {
                if (!categoriesCached) CacheCategoryCounts();
                List<Gallery.Category> display = categories != null
                    ? new List<Gallery.Category>(categories)
                    : new List<Gallery.Category>();
                Gallery.Category found;
                if (!TryResolveQuickCategoryFromToken("SubScenes", display, out found))
                    return false;
                if (string.IsNullOrEmpty(found.name)) return false;
                resolvedTitle = found.name;
                Show(found.name, found.extension, found.path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Recognition: if saved path visible in current list, select it.</summary>
        private void StripKeepTryPreselectSavedSubScene()
        {
            if (string.IsNullOrEmpty(_stripKeepDefaultSubScenePath)) return;
            if (currentFilteredFiles == null) return;

            string want = _stripKeepDefaultSubScenePath.Replace('\\', '/');
            FileEntry match = null;
            for (int i = 0; i < currentFilteredFiles.Count; i++)
            {
                FileEntry f = currentFilteredFiles[i];
                if (f == null) continue;
                string p = f.Path;
                if (string.IsNullOrEmpty(p)) p = f.Uid;
                if (string.IsNullOrEmpty(p)) continue;
                string norm = p.Replace('\\', '/');
                if (string.Equals(norm, want, StringComparison.OrdinalIgnoreCase)
                    || norm.EndsWith(want, StringComparison.OrdinalIgnoreCase)
                    || want.EndsWith(norm, StringComparison.OrdinalIgnoreCase))
                {
                    match = f;
                    break;
                }
            }
            if (match == null) return;

            selectedFiles.Clear();
            selectedFilePaths.Clear();
            selectedFiles.Add(match);
            string key = match.Path;
            if (string.IsNullOrEmpty(key)) key = match.Uid;
            if (!string.IsNullOrEmpty(key)) selectedFilePaths.Add(key);
            selectionAnchorPath = key;
            try { RefreshSelectionVisuals(); } catch { }
        }

        private bool StripKeepSubScenePickAllowsShowRequest(string title)
        {
            if (!_stripKeepSubScenePickActive) return true;
            if (string.IsNullOrEmpty(_stripKeepSubScenePickExpectedTitle)) return true;
            return string.Equals(
                title, _stripKeepSubScenePickExpectedTitle, StringComparison.OrdinalIgnoreCase);
        }

        private void StripKeepOnGallerySelectionChangedInSubScenePick()
        {
            if (!_stripKeepSubScenePickActive) return;
            StripKeepClampSubScenePickSelection();
            StripKeepUpdateSubScenePickBanner();
            try { RefreshSelectionVisuals(); } catch { }
        }

        /// <summary>One default SubScene — keep newest pick only.</summary>
        private void StripKeepClampSubScenePickSelection()
        {
            if (!_stripKeepSubScenePickActive || selectedFiles == null) return;
            if (selectedFiles.Count <= 1) return;

            FileEntry last = selectedFiles[selectedFiles.Count - 1];
            selectedFiles.Clear();
            selectedFilePaths.Clear();
            if (last != null)
            {
                selectedFiles.Add(last);
                string k = last.Path;
                if (string.IsNullOrEmpty(k)) k = last.Uid;
                if (!string.IsNullOrEmpty(k)) selectedFilePaths.Add(k);
                selectionAnchorPath = k;
            }
        }

        /// <summary>Confirm Pick — apply gallery pick as default SubScene path.</summary>
        private void StripKeepEndSubSceneSelection()
        {
            StripKeepFinishSubScenePickMode(apply: true, reopenStrip: true);
        }

        /// <summary>Cancel Pick — abort without changing saved path.</summary>
        private void StripKeepEndSubScenePick()
        {
            StripKeepFinishSubScenePickMode(apply: false, reopenStrip: true);
        }

        private void StripKeepAbortSubScenePickMode(bool reopenStrip)
        {
            StripKeepFinishSubScenePickMode(apply: false, reopenStrip: reopenStrip);
        }

        private void StripKeepFinishSubScenePickMode(bool apply, bool reopenStrip)
        {
            if (!_stripKeepSubScenePickActive) return;

            if (apply)
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.creator.strip_subscene_pick_need_one",
                            "Select a SubScene, then Confirm Pick / Enter."),
                        2f);
                    return; // stay in pick mode
                }
                FileEntry f = selectedFiles[0];
                string chosenPath = null;
                if (f != null)
                {
                    chosenPath = f.Path;
                    if (string.IsNullOrEmpty(chosenPath)) chosenPath = f.Uid;
                }
                if (string.IsNullOrEmpty(chosenPath))
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.creator.strip_subscene_pick_need_one",
                            "Select a SubScene, then Confirm Pick / Enter."),
                        2f);
                    return;
                }

                _stripKeepSubScenePickActive = false;
                _stripKeepSubScenePickExpectedTitle = null;
                StripKeepDestroySubScenePickBanner();

                string normalized = chosenPath;
                try { normalized = UI.NormalizePath(chosenPath); }
                catch { normalized = chosenPath; }

                try
                {
                    selectedFiles.Clear();
                    selectedFilePaths.Clear();
                    selectionAnchorPath = null;
                    selectionAnchorIdentityKey = null;
                    RefreshSelectionVisuals();
                }
                catch { }

                _stripKeepDefaultSubScenePath = normalized != null ? normalized.Trim() : "";
                _stripKeepCreateFillMode = StripKeepCreateFillMode.ImportSubScene;
                _stripKeepSelectedUids.Remove(StripKeepSyntheticDefault3PUid);
                _stripKeepSelectedUids.Add(StripKeepSyntheticImportSubSceneUid);
                PersistStripKeepCreateOptionsToConfig();
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T(
                            "gallery.creator.strip_subscene_set",
                            "Default SubScene → {0}"),
                        StripKeepSubSceneDisplayName(_stripKeepDefaultSubScenePath)),
                    1.75f);

                if (reopenStrip && creatorModeActive)
                {
                    try { StripKeepReopenSelectorAfterPick(); } catch { }
                }
                return;
            }

            // Cancel Pick / abort
            _stripKeepSubScenePickActive = false;
            _stripKeepSubScenePickExpectedTitle = null;
            StripKeepDestroySubScenePickBanner();

            try
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                RefreshSelectionVisuals();
            }
            catch { }

            if (string.IsNullOrEmpty(_stripKeepDefaultSubScenePath)
                && _stripKeepSelectedUids.Contains(StripKeepSyntheticImportSubSceneUid))
            {
                _stripKeepSelectedUids.Remove(StripKeepSyntheticImportSubSceneUid);
                _stripKeepCreateFillMode = StripKeepCreateFillMode.None;
                PersistStripKeepCreateOptionsToConfig();
            }

            if (reopenStrip && creatorModeActive)
            {
                try { StripKeepReopenSelectorAfterPick(); } catch { }
            }
        }

        private void StripKeepReopenSelectorAfterPick()
        {
            if (!creatorModeActive) return;
            if (creatorModeStripBusy) return;
            if (IsStripKeepSelectorOpen()) return;
            if (StripKeepResolveUiHost() == null) return;
            // Session state already live — rebuild chrome only.
            BuildStripKeepSelectorModal();
            RefreshCreatorModeChrome();
        }

        private void StripKeepEnsureSubScenePickBanner()
        {
            if (_stripKeepSubScenePickBannerRoot != null || backgroundBoxGO == null) return;

            float s = ChromeScale;
            int font = new GalleryModalTypography(s).Body;
            float barH = 52f * s;

            _stripKeepSubScenePickBannerRoot = UI.CreateChildRT(
                backgroundBoxGO,
                "VPB_StripSubScenePickBanner",
                AnchorPresets.hStretchBottom,
                new Vector2(-24f * s, barH),
                new Vector2(0f, 8f * s));

            UI.AddImage(_stripKeepSubScenePickBannerRoot, new Color(0.10f, 0.26f, 0.18f, 0.96f));

            HorizontalLayoutGroup h = _stripKeepSubScenePickBannerRoot.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(
                Mathf.RoundToInt(12f * s), Mathf.RoundToInt(12f * s),
                Mathf.RoundToInt(6f * s), Mathf.RoundToInt(6f * s));
            h.spacing = 10f * s;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childControlHeight = true;

            _stripKeepSubScenePickBannerLabel = UI.CreateLabel(
                _stripKeepSubScenePickBannerRoot, "", font, Color.white,
                TextAnchor.MiddleLeft, name: "Label");
            UI.AddLE(_stripKeepSubScenePickBannerLabel.gameObject, minWidth: 120f, flexibleWidth: 1f);

            // Primary: Confirm Pick. Secondary: Cancel Pick.
            UI.CreateChromeLayoutButton(
                _stripKeepSubScenePickBannerRoot.transform, 130f * s, 38f * s,
                VPBTranslation.T("gallery.creator.strip_subscene_confirm_pick", "Confirm Pick"),
                font, new Color(0.22f, 0.52f, 0.30f, 1f),
                StripKeepEndSubSceneSelection);

            UI.CreateChromeLayoutButton(
                _stripKeepSubScenePickBannerRoot.transform, 120f * s, 38f * s,
                VPBTranslation.T("gallery.creator.strip_subscene_cancel_pick", "Cancel Pick"),
                font, new Color(0.38f, 0.32f, 0.22f, 1f),
                StripKeepEndSubScenePick);

            StripKeepUpdateSubScenePickBanner();
        }

        private void StripKeepUpdateSubScenePickBanner()
        {
            if (_stripKeepSubScenePickBannerLabel == null) return;
            int n = selectedFiles != null ? selectedFiles.Count : 0;
            string cat = _stripKeepSubScenePickExpectedTitle ?? "SubScenes";
            string selName = "";
            if (n > 0 && selectedFiles[0] != null)
            {
                string p = selectedFiles[0].Path;
                if (string.IsNullOrEmpty(p)) p = selectedFiles[0].Uid;
                selName = StripKeepSubSceneDisplayName(p);
            }

            if (n > 0 && !string.IsNullOrEmpty(selName))
            {
                _stripKeepSubScenePickBannerLabel.text = string.Format(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_pick_banner_sel",
                        "Strip — pick SubScene in {0}  ·  {1}"),
                    cat, selName);
            }
            else
            {
                _stripKeepSubScenePickBannerLabel.text = string.Format(
                    VPBTranslation.T(
                        "gallery.creator.strip_subscene_pick_banner",
                        "Strip — pick SubScene in {0}  ·  none selected"),
                    cat);
            }
        }

        private void StripKeepDestroySubScenePickBanner()
        {
            if (_stripKeepSubScenePickBannerRoot == null) return;
            try { UnityEngine.Object.Destroy(_stripKeepSubScenePickBannerRoot); } catch { }
            _stripKeepSubScenePickBannerRoot = null;
            _stripKeepSubScenePickBannerLabel = null;
        }

        /// <summary>Esc / Enter while SubScene pick banner up.</summary>
        private bool StripKeepHandleSubScenePickKeys()
        {
            if (!_stripKeepSubScenePickActive) return false;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                StripKeepEndSubScenePick();
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (selectedFiles != null && selectedFiles.Count > 0)
                    StripKeepEndSubSceneSelection();
                else
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.creator.strip_subscene_pick_need_one",
                            "Select a SubScene, then Confirm Pick / Enter."),
                        2f);
                }
                return true;
            }
            return false;
        }
    }
}
