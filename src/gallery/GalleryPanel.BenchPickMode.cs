using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        enum BenchPickTarget { Scenes, Packages }

        bool _benchPickModeActive;
        BenchPickTarget _benchPickTarget;
        string _benchPickExpectedCategoryTitle;
        bool _benchPickRestoreSettingsLeft;
        bool _benchPickRestoreSettingsRight;

        GameObject _benchPickBannerRoot;
        Text _benchPickBannerLabel;
        bool _benchPickLimitWarned;

        public bool IsBenchPickModeActive() => _benchPickModeActive;

        void BenchStartPickScenes()
        {
            BenchEnterPickMode(BenchPickTarget.Scenes, "Scenes");
        }

        void BenchStartPickPackages()
        {
            BenchEnterPickMode(BenchPickTarget.Packages, "ALL VAR");
        }

        void BenchEnterPickMode(BenchPickTarget target, string categoryToken)
        {
            if (_benchPickModeActive) return;
            if (backgroundBoxGO == null) return;

            _benchPickRestoreSettingsLeft = leftActiveContent == ContentType.Settings;
            _benchPickRestoreSettingsRight = rightActiveContent == ContentType.Settings;

            try
            {
                if (IsSettingsPanelOpen() || settingsListViewActive)
                    ExitInternalSettingsMode(true);
            }
            catch { }

            if (_benchModalRoot != null)
                _benchModalRoot.SetActive(false);

            try
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
            }
            catch { }

            _benchPickTarget = target;
            _benchPickModeActive = true;
            _benchPickExpectedCategoryTitle = null;
            _benchPickLimitWarned = false;

            if (!TryNavigateBenchPickCategory(categoryToken, out string resolvedTitle))
            {
                _benchPickModeActive = false;
                ShowTemporaryStatus(VPBTranslation.T("bench.pick.category_missing",
                    "Could not open gallery category for selection."), 3f);
                if (_benchModalRoot != null) _benchModalRoot.SetActive(true);
                return;
            }

            _benchPickExpectedCategoryTitle = resolvedTitle;
            BenchEnsurePickBanner();
            BenchUpdatePickBanner();

            string hint = target == BenchPickTarget.Scenes
                ? VPBTranslation.T("bench.pick.hint_scenes", "Select scenes, then tap Done.")
                : VPBTranslation.T("bench.pick.hint_packages", "Select packages (.var), then tap Done.");
            ShowTemporaryStatus("[VPB] " + hint, 3.5f);
        }

        bool TryNavigateBenchPickCategory(string token, out string resolvedTitle)
        {
            resolvedTitle = null;
            try
            {
                if (!categoriesCached) CacheCategoryCounts();
                var display = categories != null
                    ? new List<Gallery.Category>(categories)
                    : new List<Gallery.Category>();
                if (!TryResolveQuickCategoryFromToken(token, display, out Gallery.Category found))
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

        bool BenchPickModeAllowsShowRequest(string title)
        {
            if (!_benchPickModeActive) return true;
            if (string.IsNullOrEmpty(_benchPickExpectedCategoryTitle)) return true;
            return string.Equals(title, _benchPickExpectedCategoryTitle, StringComparison.OrdinalIgnoreCase);
        }

        void BenchOnGallerySelectionChangedInPickMode()
        {
            if (!_benchPickModeActive) return;
            BenchClampPickSelection();
            BenchUpdatePickBanner();
            try { RefreshSelectionVisuals(); } catch { }
        }

        void BenchClampPickSelection()
        {
            if (!_benchPickModeActive || selectedFiles == null) return;
            int max = BenchPickMaxPerSession;
            if (selectedFiles.Count <= max) return;

            while (selectedFiles.Count > max)
                selectedFiles.RemoveAt(selectedFiles.Count - 1);

            selectedFilePaths.Clear();
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                string k = f.Path;
                if (string.IsNullOrEmpty(k)) k = f.Uid;
                if (!string.IsNullOrEmpty(k)) selectedFilePaths.Add(k);
            }

            if (!_benchPickLimitWarned)
            {
                _benchPickLimitWarned = true;
                string msg = VPBTranslation.T("bench.pick.limit",
                        "Selection capped at {0} per pick (large lists freeze the test UI).")
                    .Replace("{0}", max.ToString());
                ShowTemporaryStatus("[VPB] " + msg, 4f);
            }
        }

        void BenchExitPickMode(bool apply)
        {
            BenchFinishPickMode(apply, reopenModal: true);
        }

        void BenchAbortPickMode(bool reopenModal)
        {
            BenchFinishPickMode(apply: false, reopenModal: reopenModal);
        }

        void BenchFinishPickMode(bool apply, bool reopenModal)
        {
            if (!_benchPickModeActive) return;
            _benchPickModeActive = false;
            _benchPickExpectedCategoryTitle = null;
            BenchDestroyPickBanner();

            int selCount = selectedFiles != null ? selectedFiles.Count : 0;
            int added = 0;
            int skippedCap = 0;
            if (apply)
            {
                try
                {
                    VpbBenchConfig cfg = VpbBenchConfigStore.Working;
                    if (cfg != null)
                    {
                        if (_benchPickTarget == BenchPickTarget.Scenes)
                            added = BenchAddScenesFromSelection(cfg, out skippedCap);
                        else
                            added = BenchAddPackagesFromSelection(BenchGetDefaultCandidate(cfg), out skippedCap);
                        if (added > 0) VpbBenchConfigStore.MarkDirty();
                    }
                }
                catch (Exception ex)
                {
                    try { LogUtil.LogError("[VPB.Bench] Pick apply failed: " + ex.Message); } catch { }
                }
            }

            try
            {
                LogUtil.Log("[VPB.Bench] Pick finish apply=" + apply + " target=" + _benchPickTarget
                    + " selected=" + selCount + " added=" + added + " skippedCap=" + skippedCap);
            }
            catch { }

            try
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                RefreshSelectionVisuals();
            }
            catch { }

            try
            {
                if (_benchPickRestoreSettingsLeft)
                    ToggleLeft(ContentType.Settings);
                else if (_benchPickRestoreSettingsRight)
                    ToggleRight(ContentType.Settings);
            }
            catch { }

            _benchPickRestoreSettingsLeft = false;
            _benchPickRestoreSettingsRight = false;

            if (reopenModal)
            {
                try { ShowBenchEditorModal(); } catch { }
                if (apply)
                    BenchShowPickApplyResult(added, skippedCap);
            }
        }

        void BenchShowPickApplyResult(int added, int skippedCap)
        {
            if (added <= 0)
            {
                string empty = _benchPickTarget == BenchPickTarget.Scenes
                    ? VPBTranslation.T("bench.pick.done_empty_scenes", "No scenes selected.")
                    : VPBTranslation.T("bench.pick.done_empty_packages", "No packages selected.");
                BenchSetStatus(empty);
                ShowTemporaryStatus("[VPB] " + empty, 2.5f);
                return;
            }

            string msg = _benchPickTarget == BenchPickTarget.Scenes
                ? VPBTranslation.T("bench.simple.added_scenes", "Added {0} scene(s).").Replace("{0}", added.ToString())
                : VPBTranslation.T("bench.simple.added_packages", "Added {0} package(s).").Replace("{0}", added.ToString());
            if (skippedCap > 0)
            {
                int maxTotal = _benchPickTarget == BenchPickTarget.Scenes ? BenchMaxScenesInConfig : BenchMaxPackagesInConfig;
                msg += " " + VPBTranslation.T("bench.pick.skipped_cap",
                        "{0} skipped (max {1} in list).").Replace("{0}", skippedCap.ToString()).Replace("{1}", maxTotal.ToString());
            }
            BenchSetStatus(msg);
            ShowTemporaryStatus("[VPB] " + msg, 3f);
        }

        void BenchEnsurePickBanner()
        {
            if (_benchPickBannerRoot != null || backgroundBoxGO == null) return;

            float s = ChromeScale;
            int font = new GalleryModalTypography(s).Body;

            _benchPickBannerRoot = new GameObject("VPB_BenchPickBanner");
            _benchPickBannerRoot.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rt = _benchPickBannerRoot.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 8f * s);
            rt.sizeDelta = new Vector2(-24f * s, 52f * s);

            Image bg = _benchPickBannerRoot.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.22f, 0.32f, 0.96f);
            bg.raycastTarget = true;

            HorizontalLayoutGroup h = _benchPickBannerRoot.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(Mathf.RoundToInt(12f * s), Mathf.RoundToInt(12f * s), Mathf.RoundToInt(6f * s), Mathf.RoundToInt(6f * s));
            h.spacing = 10f * s;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childControlHeight = true;

            GameObject lblGo = new GameObject("Label");
            lblGo.transform.SetParent(_benchPickBannerRoot.transform, false);
            _benchPickBannerLabel = lblGo.AddComponent<Text>();
            _benchPickBannerLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _benchPickBannerLabel.fontSize = font;
            _benchPickBannerLabel.color = Color.white;
            _benchPickBannerLabel.alignment = TextAnchor.MiddleLeft;
            _benchPickBannerLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            try { VPBUiFont.ApplyTo(_benchPickBannerLabel); } catch { }
            LayoutElement lblLe = lblGo.AddComponent<LayoutElement>();
            lblLe.flexibleWidth = 1f;
            lblLe.minWidth = 120f;

            ScanWlCreateHeaderButton(_benchPickBannerRoot.transform, 100f * s, 38f * s,
                VPBTranslation.T("bench.pick.done", "Done"),
                font, new Color(0.22f, 0.52f, 0.30f, 1f), () => BenchExitPickMode(true));

            ScanWlCreateHeaderButton(_benchPickBannerRoot.transform, 90f * s, 38f * s,
                VPBTranslation.T("bench.pick.cancel", "Cancel"),
                font, new Color(0.38f, 0.32f, 0.22f, 1f), () => BenchExitPickMode(false));

            BenchUpdatePickBanner();
        }

        void BenchUpdatePickBanner()
        {
            if (_benchPickBannerLabel == null) return;
            int n = selectedFiles != null ? selectedFiles.Count : 0;
            string kind = _benchPickTarget == BenchPickTarget.Scenes
                ? VPBTranslation.T("bench.pick.kind_scenes", "scenes")
                : VPBTranslation.T("bench.pick.kind_packages", "packages");
            string cat = _benchPickExpectedCategoryTitle ?? "";
            _benchPickBannerLabel.text = VPBTranslation.T("bench.pick.banner",
                    "Scene Load Test — pick {0} in {1} ({2} selected, max {3})")
                .Replace("{0}", kind).Replace("{1}", cat).Replace("{2}", n.ToString())
                .Replace("{3}", BenchPickMaxPerSession.ToString());
        }

        void BenchDestroyPickBanner()
        {
            if (_benchPickBannerRoot == null) return;
            try { UnityEngine.Object.Destroy(_benchPickBannerRoot); } catch { }
            _benchPickBannerRoot = null;
            _benchPickBannerLabel = null;
        }
    }
}
