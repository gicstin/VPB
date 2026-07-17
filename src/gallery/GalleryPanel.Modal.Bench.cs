using System;
using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        const string BenchDefaultCandidateId = "default";
        const float BenchScrollBarWidth = 16f;
        /// <summary>Max gallery rows selectable per pick session (shift-range can otherwise select 10k+).</summary>
        internal const int BenchPickMaxPerSession = 64;
        internal const int BenchMaxScenesInConfig = 128;
        internal const int BenchMaxPackagesInConfig = 32;
        const int BenchMaxVisibleListRows = 50;

        GameObject _benchModalRoot;
        Transform _benchScrollContent;
        Text _benchStatusText;
        Text _benchHelpText;
        Transform _benchScenesParent;
        Transform _benchPackagesParent;
        GameObject _benchAdvancedBlock;
        InputField _benchHotkeyInput;
        InputField _benchBaselineIdInput;
        InputField _benchScenePathInput;
        bool _benchAdvancedOpen;
        bool _benchShowAllResults;
        bool _benchTrackRunning;
        string _benchLastDisplayedRunId;
        string _benchLastDisplayedRunDir;
        string _benchLastDisplayedCompareCsv;
        GameObject _benchConfirmRoot;

        public void ShowBenchEditorModal()
        {
            if (backgroundBoxGO == null) return;
            // Pick flow marks Working dirty before reopen — unconditional reload dropped those scenes.
            try
            {
                if (VpbBenchConfigStore.IsDirty)
                    VpbBenchConfigStore.EnsureLoaded();
                else
                    VpbBenchConfigStore.ReloadFromDisk();
            }
            catch { }
            BenchNormalizeConfigForUi(VpbBenchConfigStore.Working);
            if (_benchModalRoot != null)
            {
                _benchModalRoot.SetActive(true);
                RebuildBenchEditorModal();
                return;
            }
            BuildBenchEditorModal();
        }

        void HideBenchEditorModal()
        {
            BenchHideConfirm();
            if (_benchPickModeActive)
                BenchAbortPickMode(reopenModal: false);
            if (_benchModalRoot != null)
            {
                try { UnityEngine.Object.Destroy(_benchModalRoot); } catch { }
                _benchModalRoot = null;
            }
            _benchScrollContent = null;
            _benchStatusText = null;
            _benchHelpText = null;
            _benchScenesParent = null;
            _benchPackagesParent = null;
            _benchAdvancedBlock = null;
            _benchHotkeyInput = null;
            _benchBaselineIdInput = null;
            _benchScenePathInput = null;
        }

        void BuildBenchEditorModal()
        {
            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int titleFont = type.Title;
            int bodyFont = type.Body;

            GameObject panel;
            _benchModalRoot = UI.CreateModalChrome(backgroundBoxGO, "VPB_BenchModal", 660f * s, 760f * s, new Color(0.07f, 0.08f, 0.10f, 1f), HideBenchEditorModal, out panel);
            BenchAddPanelClickBlocker(panel.transform);

            UI.AddVLG(panel, 10f * s, UI.Pad(16f, 16f, 14f, 12f, s));

            // Header
            GameObject header = new GameObject("Header");
            header.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup hh = header.AddComponent<HorizontalLayoutGroup>();
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.childForceExpandWidth = false;
            LayoutElement hle = UI.AddLE(header, minHeight: 44f * s);

            Text title = UI.CreateEmphasisTitleLabel(header, VPBTranslation.T("bench.simple.title", "Scene Load Test"), titleFont);
            LayoutElement tle = UI.AddLE(title.gameObject, flexibleWidth: 1f);

            UI.CreateChromeLayoutButton(header.transform, 84f * s, 36f * s, VPBTranslation.T("hook.close", "Close"), bodyFont,
                new Color(0.38f, 0.32f, 0.22f, 1f), HideBenchEditorModal);

            _benchHelpText = UI.CreateLabel(panel, VPBTranslation.T("bench.simple.help",
                "Pick scenes → Run Test. Changes save automatically. Compares when baseline exists."),
                bodyFont, new Color(0.72f, 0.76f, 0.82f, 1f), name: "Help");
            LayoutElement helpLe = UI.AddLE(_benchHelpText.gameObject, minHeight: 52f * s);

            // Scroll body
            GameObject scrollHost = new GameObject("Scroll");
            scrollHost.transform.SetParent(panel.transform, false);
            LayoutElement scrollLe = UI.AddLE(scrollHost, minHeight: 400f * s, flexibleHeight: 1f);
            Image scrollBg = UI.AddImage(scrollHost, new Color(0.09f, 0.10f, 0.12f, 1f));
            ScrollRect sr = scrollHost.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollHost.transform, false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = new Vector2(-BenchScrollBarWidth, 0f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            Image vpImg = UI.AddImage(viewport, Color.white);

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0f, 1f);
            contentRt.sizeDelta = Vector2.zero;
            VerticalLayoutGroup cv = content.AddComponent<VerticalLayoutGroup>();
            cv.padding = new RectOffset(Mathf.RoundToInt(12f * s), Mathf.RoundToInt(12f * s), Mathf.RoundToInt(8f * s), Mathf.RoundToInt(8f * s));
            cv.spacing = 12f * s;
            cv.childControlWidth = true;
            cv.childForceExpandWidth = true;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vpRt;
            sr.content = contentRt;
            BenchAttachVerticalScrollbar(scrollHost, sr);
            _benchScrollContent = content.transform;

            // Status + actions (fixed below scroll)
            _benchStatusText = UI.CreateLabel(panel, "", bodyFont, new Color(0.65f, 0.82f, 1f, 1f), TextAnchor.MiddleLeft, name: "Status");
            LayoutElement stLe = UI.AddLE(_benchStatusText.gameObject, minHeight: 36f * s);

            UI.CreateChromeLayoutButton(panel.transform, 0f, 50f * s,
                VPBTranslation.T("bench.simple.run", "Run Test"),
                titleFont,
                new Color(0.22f, 0.52f, 0.30f, 1f), BenchModalRunTest);

            GameObject subRow = new GameObject("SubActions");
            subRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup subH = subRow.AddComponent<HorizontalLayoutGroup>();
            subH.spacing = 6f * s;
            subH.childForceExpandWidth = true;
            subH.childControlWidth = true;
            LayoutElement subLe = UI.AddLE(subRow, minHeight: 34f * s);
            int subFont = bodyFont;

            UI.CreateChromeLayoutButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.set_baseline", "Baseline"),
                subFont, new Color(0.24f, 0.38f, 0.52f, 1f), BenchModalSetBaseline);

            UI.CreateChromeLayoutButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.open_results", "Results"),
                subFont, new Color(0.22f, 0.40f, 0.48f, 1f), BenchOpenLastResultsFolder);

            UI.CreateChromeLayoutButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.open_csv", "CSV"),
                subFont, new Color(0.22f, 0.40f, 0.48f, 1f), BenchOpenLastCompareCsv);

            UI.CreateChromeLayoutButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.open_screenshots", "Shots"),
                subFont, new Color(0.22f, 0.40f, 0.48f, 1f), BenchOpenLastScreenshotsFolder);

            UI.CreateChromeLayoutButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.advanced", "More…"),
                subFont, new Color(0.22f, 0.24f, 0.28f, 1f), BenchToggleAdvanced);

            RebuildBenchEditorModal();
        }

        void RebuildBenchEditorModal()
        {
            if (_benchScrollContent == null) return;
            BenchHideConfirm();
            BenchDestroyChildren(_benchScrollContent);

            float s = ChromeScale;
            int bodyFont = new GalleryModalTypography(s).Body;
            VpbBenchConfig cfg = VpbBenchConfigStore.Working;
            if (cfg == null) return;
            BenchNormalizeConfigForUi(cfg);

            BenchAddLastResultsSection(_benchScrollContent, bodyFont, s);

            BenchAddPresetRow(_benchScrollContent, cfg, bodyFont, s);
            BenchAddBaselineSelector(_benchScrollContent, cfg, bodyFont, s);

            BenchAddSimpleToggle(_benchScrollContent, VPBTranslation.T("bench.simple.enabled", "Enable hotkey runner"), cfg.Enabled, v =>
            {
                cfg.Enabled = v;
                VpbBenchConfigStore.MarkDirty();
                RebuildBenchEditorModal();
            }, bodyFont, s);

            // Scenes
            BenchAddListSection(_benchScrollContent, VPBTranslation.T("bench.simple.scenes", "Scenes to load"),
                cfg.Scenes, bodyFont, s, 140f * s, out _benchScenesParent,
                i => BenchConfirmRemoveScene(cfg, i),
                VPBTranslation.T("bench.simple.no_scenes", "No scenes yet — pick from gallery or add current selection."));
            BenchAddSceneActionRow(_benchScrollContent, cfg, bodyFont, s);

            VpbBenchCandidateConfig cand = BenchGetDefaultCandidate(cfg);
            BenchAddListSection(_benchScrollContent, VPBTranslation.T("bench.simple.packages", "Packages to use"),
                cand.PackageUids, bodyFont, s, 100f * s, out _benchPackagesParent,
                i => BenchConfirmRemovePackage(cand, i),
                VPBTranslation.T("bench.simple.no_packages", "No extra packages — optional."));
            BenchAddPackageActionRow(_benchScrollContent, cand, bodyFont, s);

            // Advanced block
            _benchAdvancedBlock = new GameObject("Advanced");
            _benchAdvancedBlock.transform.SetParent(_benchScrollContent, false);
            VerticalLayoutGroup advV = _benchAdvancedBlock.AddComponent<VerticalLayoutGroup>();
            advV.spacing = 8f * s;
            advV.childControlWidth = true;
            advV.childForceExpandWidth = true;
            _benchAdvancedBlock.SetActive(_benchAdvancedOpen);

            _benchHotkeyInput = BenchAddSimpleField(_benchAdvancedBlock.transform,
                VPBTranslation.T("bench.simple.hotkey", "Keyboard shortcut"),
                cfg.Hotkey ?? "Ctrl+F10", bodyFont, s, v => { cfg.Hotkey = v; VpbBenchConfigStore.MarkDirty(); });

            _benchBaselineIdInput = BenchAddSimpleField(_benchAdvancedBlock.transform,
                VPBTranslation.T("bench.simple.baseline_name", "Baseline name"),
                cfg.BaselineId ?? "main", bodyFont, s, v =>
                {
                    cfg.BaselineId = string.IsNullOrEmpty(v) ? "main" : v.Trim();
                    VpbBenchConfigStore.MarkDirty();
                });

            BenchAddSimpleField(_benchAdvancedBlock.transform,
                VPBTranslation.T("bench.simple.wait_sec", "Wait between scenes (seconds)"),
                cfg.WaitBetweenScenesSeconds.ToString("0.#"), bodyFont, s, v =>
                {
                    float wait;
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out wait) && wait >= 0f)
                    {
                        cfg.WaitBetweenScenesSeconds = wait;
                        BenchMarkPresetCustom(cfg);
                        VpbBenchConfigStore.MarkDirty();
                    }
                });

            _benchScenePathInput = BenchAddSimpleField(_benchAdvancedBlock.transform,
                VPBTranslation.T("bench.simple.scene_path", "Scene path (manual)"),
                "", bodyFont, s, null);
            UI.CreateChromeLayoutButton(_benchAdvancedBlock.transform, 180f * s, 34f * s,
                VPBTranslation.T("bench.simple.add_path", "Add path"),
                bodyFont, new Color(0.22f, 0.40f, 0.55f, 1f), () =>
                {
                    string p = _benchScenePathInput != null ? _benchScenePathInput.text : null;
                    if (!string.IsNullOrEmpty(p) && !cfg.Scenes.Contains(p.Trim()))
                    {
                        cfg.Scenes.Add(p.Trim());
                        VpbBenchConfigStore.MarkDirty();
                        RebuildBenchEditorModal();
                    }
                });

            BenchAddSimpleToggle(_benchAdvancedBlock.transform,
                VPBTranslation.T("bench.simple.capture_screenshots", "Capture screenshot after each scene"),
                cfg.CaptureSceneScreenshots, v =>
                {
                    cfg.CaptureSceneScreenshots = v;
                    BenchMarkPresetCustom(cfg);
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                }, bodyFont, s);
            BenchAddSimpleToggle(_benchAdvancedBlock.transform,
                VPBTranslation.T("bench.simple.clear_tex", "Clear texture cache before run"),
                cfg.ClearTextureCache, v =>
                {
                    cfg.ClearTextureCache = v;
                    BenchMarkPresetCustom(cfg);
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                }, bodyFont, s);
            BenchAddSimpleToggle(_benchAdvancedBlock.transform,
                VPBTranslation.T("bench.simple.clear_ab", "Clear asset-bundle cache before run"),
                cfg.ClearAbCache, v =>
                {
                    cfg.ClearAbCache = v;
                    BenchMarkPresetCustom(cfg);
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                }, bodyFont, s);

            UI.CreateChromeLayoutButton(_benchAdvancedBlock.transform, 140f * s, 34f * s,
                VPBTranslation.T("bench.simple.save", "Save settings"),
                bodyFont, new Color(0.24f, 0.38f, 0.52f, 1f), BenchModalSaveOnly);

            BenchUpdateStatus(cfg);
            _benchTrackRunning = VpbBenchRunner.IsRunning;
        }

        internal void BenchModalRuntimeTick()
        {
            if (_benchModalRoot == null || !_benchModalRoot.activeInHierarchy)
            {
                _benchTrackRunning = VpbBenchRunner.IsRunning;
                return;
            }

            bool running = VpbBenchRunner.IsRunning;
            if (_benchTrackRunning && !running)
                RebuildBenchEditorModal();
            else if (running)
            {
                VpbBenchConfig cfg = VpbBenchConfigStore.Working;
                if (cfg != null) BenchUpdateStatus(cfg);
            }
            _benchTrackRunning = running;
        }

        static void BenchMarkPresetCustom(VpbBenchConfig cfg)
        {
            if (cfg == null) return;
            cfg.Preset = VpbBenchPresetKind.Custom;
        }

        void BenchAddPresetRow(Transform parent, VpbBenchConfig cfg, int bodyFont, float s)
        {
            BenchAddSimpleLabel(parent, VPBTranslation.T("bench.simple.preset", "Test preset"), bodyFont, s);
            GameObject row = new GameObject("PresetRow");
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f * s;
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            LayoutElement le = UI.AddLE(row, minHeight: 40f * s);

            BenchAddPresetButton(row.transform, cfg, VpbBenchPresetKind.Quick, bodyFont, s);
            BenchAddPresetButton(row.transform, cfg, VpbBenchPresetKind.Standard, bodyFont, s);
            BenchAddPresetButton(row.transform, cfg, VpbBenchPresetKind.Deep, bodyFont, s);
        }

        void BenchAddPresetButton(Transform parent, VpbBenchConfig cfg, VpbBenchPresetKind kind, int bodyFont, float s)
        {
            bool active = cfg.Preset == kind;
            UI.CreateChromeLayoutButton(parent, 0f, 38f * s, VpbBenchPresetUtil.Label(kind), bodyFont,
                active ? new Color(0.30f, 0.48f, 0.62f, 1f) : new Color(0.18f, 0.20f, 0.24f, 1f),
                () =>
                {
                    VpbBenchPresetUtil.Apply(cfg, kind);
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                });
        }

        void BenchAddBaselineSelector(Transform parent, VpbBenchConfig cfg, int bodyFont, float s)
        {
            var ids = VpbBenchPaths.ListBaselineIds();
            string current = string.IsNullOrEmpty(cfg.BaselineId) ? "main" : cfg.BaselineId;
            if (!ids.Contains(current)) ids.Insert(0, current);

            BenchAddSimpleLabel(parent, VPBTranslation.T("bench.simple.baseline", "Compare to baseline"), bodyFont, s);
            GameObject row = new GameObject("BaselineRow");
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f * s;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            LayoutElement le = UI.AddLE(row, minHeight: 40f * s);

            UI.CreateChromeLayoutButton(row.transform, 36f * s, 34f * s, "◀", bodyFont,
                new Color(0.22f, 0.24f, 0.28f, 1f), () =>
                {
                    int idx = ids.IndexOf(cfg.BaselineId ?? "main");
                    if (idx < 0) idx = 0;
                    idx = (idx - 1 + ids.Count) % ids.Count;
                    cfg.BaselineId = ids[idx];
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                });

            bool hasBaseline = VpbBenchComparer.BaselineExists(cfg.BaselineId);
            Text nameText = UI.CreateLabel(row, (cfg.BaselineId ?? "main") + (hasBaseline ? "" : " (new)"), bodyFont, Color.white, TextAnchor.MiddleCenter, name: "Name");
            LayoutElement nle = UI.AddLE(nameText.gameObject, minWidth: 180f * s, preferredWidth: 220f * s);

            UI.CreateChromeLayoutButton(row.transform, 36f * s, 34f * s, "▶", bodyFont,
                new Color(0.22f, 0.24f, 0.28f, 1f), () =>
                {
                    int idx = ids.IndexOf(cfg.BaselineId ?? "main");
                    if (idx < 0) idx = 0;
                    idx = (idx + 1) % ids.Count;
                    cfg.BaselineId = ids[idx];
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                });
        }

        void BenchAddScenesFromGallerySelection()
        {
            VpbBenchConfig cfg = VpbBenchConfigStore.Working;
            if (cfg == null) return;
            int skipped;
            int added = BenchAddScenesFromSelection(cfg, out skipped);
            if (added <= 0 && skipped <= 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("bench.simple.no_selection", "No scene files in gallery selection."), 3f);
                return;
            }
            VpbBenchConfigStore.MarkDirty();
            string msg = VPBTranslation.T("bench.simple.added_scenes", "Added {0} scene(s).").Replace("{0}", added.ToString());
            if (skipped > 0)
                msg += " " + VPBTranslation.T("bench.simple.skipped_cap", "Skipped {0} (list cap).").Replace("{0}", skipped.ToString());
            ShowTemporaryStatus("[VPB] " + msg, 3f);
            RebuildBenchEditorModal();
        }

        void BenchModalSetBaseline()
        {
            BenchNormalizeConfigForUi(VpbBenchConfigStore.Working);
            string saveErr;
            if (!VpbBenchConfigStore.TrySave(out saveErr))
            {
                BenchSetStatus(saveErr);
                ShowTemporaryStatus("[VPB] Save failed: " + saveErr, 4f);
                return;
            }

            VpbBenchConfig cfg = VpbBenchConfigStore.Working;
            string err;
            if (!VpbBenchRunner.TryPromoteLastRunToBaseline(cfg != null ? cfg.BaselineId : null, out err))
            {
                BenchSetStatus(err);
                ShowTemporaryStatus("[VPB] " + err, 4f);
                return;
            }

            string ok = VPBTranslation.T("bench.simple.baseline_saved", "Baseline saved: ") + (cfg != null ? cfg.BaselineId : "");
            BenchSetStatus(ok);
            ShowTemporaryStatus("[VPB] " + ok, 3f);
            RebuildBenchEditorModal();
        }

        void BenchOpenLastResultsFolder()
        {
            string path = _benchLastDisplayedRunDir;
            if (string.IsNullOrEmpty(path))
                path = VpbBenchRunSummary.LastRunDir;
            if (string.IsNullOrEmpty(path))
            {
                ShowTemporaryStatus(VPBTranslation.T("bench.simple.no_results", "No test results yet."), 3f);
                return;
            }
            VpbBenchPaths.TryOpenInShell(path);
        }

        void BenchOpenLastCompareCsv()
        {
            string path = _benchLastDisplayedCompareCsv;
            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(_benchLastDisplayedRunId))
                path = VpbBenchPaths.CompareCsvPath(_benchLastDisplayedRunId);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                ShowTemporaryStatus(VPBTranslation.T("bench.simple.no_csv", "No compare CSV — need baseline + completed run."), 3f);
                return;
            }
            VpbBenchPaths.TryOpenInShell(path);
        }

        void BenchOpenLastScreenshotsFolder()
        {
            string runDir = _benchLastDisplayedRunDir;
            if (string.IsNullOrEmpty(runDir))
                runDir = VpbBenchRunSummary.LastRunDir;
            if (string.IsNullOrEmpty(runDir))
            {
                ShowTemporaryStatus(VPBTranslation.T("bench.simple.no_results", "No test results yet."), 3f);
                return;
            }
            string shots = System.IO.Path.Combine(runDir, "screenshots");
            if (!System.IO.Directory.Exists(shots))
            {
                ShowTemporaryStatus(VPBTranslation.T("bench.simple.no_screenshots", "No screenshots folder — enable in preset or Advanced."), 3f);
                return;
            }
            VpbBenchPaths.TryOpenInShell(shots);
        }

        void BenchToggleAdvanced()
        {
            _benchAdvancedOpen = !_benchAdvancedOpen;
            if (_benchAdvancedBlock != null) _benchAdvancedBlock.SetActive(_benchAdvancedOpen);
        }

        void BenchModalRunTest()
        {
            BenchNormalizeConfigForUi(VpbBenchConfigStore.Working);
            string saveErr;
            if (!VpbBenchConfigStore.TrySave(out saveErr))
            {
                BenchSetStatus(saveErr);
                ShowTemporaryStatus("[VPB] Save failed: " + saveErr, 4f);
                return;
            }
            string err;
            if (!VpbBenchConfigStore.TryValidateForUiRun(out err))
            {
                BenchSetStatus(err);
                ShowTemporaryStatus("[VPB] " + err, 4f);
                return;
            }
            _benchTrackRunning = true;
            BenchUpdateStatus(VpbBenchConfigStore.Working);
            try { VamHookPlugin.singleton?.RequestBenchRunFromGallery(); }
            catch (Exception ex) { BenchSetStatus(ex.Message); _benchTrackRunning = VpbBenchRunner.IsRunning; }
        }

        void BenchModalSaveOnly()
        {
            BenchNormalizeConfigForUi(VpbBenchConfigStore.Working);
            string err;
            if (!VpbBenchConfigStore.TrySave(out err))
            {
                BenchSetStatus(err);
                ShowTemporaryStatus("[VPB] Save failed: " + err, 4f);
                return;
            }
            BenchSetStatus(VPBTranslation.T("bench.simple.saved", "Saved."));
            ShowTemporaryStatus(VPBTranslation.T("bench.simple.saved", "Saved."), 2f);
        }

        static void BenchNormalizeConfigForUi(VpbBenchConfig cfg)
        {
            VpbBenchUiNormalize.PrepareForSave(cfg);
        }

        static VpbBenchCandidateConfig BenchGetDefaultCandidate(VpbBenchConfig cfg)
        {
            return VpbBenchUiNormalize.GetDefaultCandidate(cfg);
        }

        void BenchUpdateStatus(VpbBenchConfig cfg)
        {
            if (VpbBenchRunner.IsRunning)
            {
                BenchSetStatus(VPBTranslation.T("bench.simple.status_running", "Test running…"));
                return;
            }
            if (!string.IsNullOrEmpty(VpbBenchRunSummary.LastRunHeadline))
            {
                BenchSetStatus(VpbBenchRunSummary.LastRunHeadline);
                return;
            }
            if (!cfg.Enabled)
            {
                BenchSetStatus(VPBTranslation.T("bench.simple.status_off", "Runner off — enable to use shortcut or Run Test."));
                return;
            }
            if (cfg.Scenes.Count == 0)
            {
                BenchSetStatus(VPBTranslation.T("bench.simple.status_need_scenes", "Add at least one scene."));
                return;
            }
            string baseline = string.IsNullOrEmpty(cfg.BaselineId) ? "main" : cfg.BaselineId;
            bool hasBaseline = VpbBenchComparer.BaselineExists(baseline);
            string hotkey = string.IsNullOrEmpty(cfg.Hotkey) ? "Ctrl+F10" : cfg.Hotkey;
            string compareHint = hasBaseline
                ? VPBTranslation.T("bench.simple.status_compare", " — will compare to '") + baseline + "'"
                : VPBTranslation.T("bench.simple.status_no_baseline", " — no baseline yet (Set as baseline after run)");
            BenchSetStatus(VPBTranslation.T("bench.simple.status_ready", "Ready — ") + hotkey
                + VPBTranslation.T("bench.simple.status_ready2", " or Run Test") + compareHint);
        }

        void BenchAddLastResultsSection(Transform parent, int fontSize, float s)
        {
            _benchLastDisplayedRunId = null;
            _benchLastDisplayedRunDir = null;
            _benchLastDisplayedCompareCsv = null;

            JSONClass doc;
            string runDir;
            if (!VpbBenchRunSummary.TryLoadLatestRun(out doc, out runDir)) return;

            JSONNode sum = doc["resultsSummary"];
            if (sum == null) return;

            string headline = sum["headline"] != null ? sum["headline"].Value : "";
            string runId = sum["runId"] != null ? sum["runId"].Value : "";
            _benchLastDisplayedRunId = runId;
            _benchLastDisplayedRunDir = runDir;
            if (sum["compareCsv"] != null && !string.IsNullOrEmpty(sum["compareCsv"].Value))
                _benchLastDisplayedCompareCsv = System.IO.Path.Combine(VpbBenchPaths.PluginDir, sum["compareCsv"].Value);
            else if (!string.IsNullOrEmpty(runId))
                _benchLastDisplayedCompareCsv = VpbBenchPaths.CompareCsvPath(runId);

            BenchAddSimpleLabel(parent, VPBTranslation.T("bench.simple.last_results", "Last test results"), fontSize, s);
            if (!string.IsNullOrEmpty(headline))
                ScanWlAddPlaceholderRow(parent, headline, fontSize, s);

            JSONNode results = doc["results"];
            if (results == null || results.Count == 0) return;

            int regressionCount = 0;
            int okHidden = 0;
            for (int i = 0; i < results.Count; i++)
            {
                JSONNode row = results[i];
                if (row == null) continue;
                string status = row["status"] != null ? row["status"].Value : "OK";
                if (VpbBenchRunSummary.IsRegressionStatus(status)) regressionCount++;
                else okHidden++;
            }

            if (regressionCount == 0 && okHidden > 0 && !_benchShowAllResults)
            {
                ScanWlAddPlaceholderRow(parent,
                    VPBTranslation.T("bench.simple.all_ok", "All {0} scene(s) OK — no regressions.")
                        .Replace("{0}", okHidden.ToString()),
                    fontSize, s);
            }

            int shown = 0;
            for (int i = 0; i < results.Count && shown < BenchMaxVisibleListRows; i++)
            {
                JSONNode row = results[i];
                if (row == null) continue;
                string status = row["status"] != null ? row["status"].Value : "OK";
                if (!_benchShowAllResults && !VpbBenchRunSummary.IsRegressionStatus(status)) continue;
                string line = row["summaryLine"] != null ? row["summaryLine"].Value : "";
                if (string.IsNullOrEmpty(line)) continue;
                BenchAddResultRow(parent, line, status, fontSize, s, (shown & 1) == 1);
                shown++;
            }

            if (okHidden > 0)
            {
                BenchAddSimpleToggle(parent,
                    _benchShowAllResults
                        ? VPBTranslation.T("bench.simple.hide_ok", "Hide OK scenes")
                        : VPBTranslation.T("bench.simple.show_all", "Show all scenes ({0} OK)").Replace("{0}", okHidden.ToString()),
                    _benchShowAllResults, v =>
                    {
                        _benchShowAllResults = v;
                        RebuildBenchEditorModal();
                    }, fontSize, s);
            }
        }

        static void BenchAddResultRow(Transform parent, string text, string status, int fontSize, float s, bool alt)
        {
            Color bg = alt ? new Color(0.08f, 0.09f, 0.11f, 1f) : new Color(0.06f, 0.07f, 0.09f, 1f);
            Color fg = Color.white;
            if (string.Equals(status, "WARN", StringComparison.OrdinalIgnoreCase))
                fg = new Color(1f, 0.85f, 0.35f, 1f);
            else if (string.Equals(status, "FAIL", StringComparison.OrdinalIgnoreCase))
                fg = new Color(1f, 0.45f, 0.40f, 1f);
            else
                fg = new Color(0.65f, 0.95f, 0.70f, 1f);

            GameObject row = new GameObject("ResultRow");
            row.transform.SetParent(parent, false);
            LayoutElement le = UI.AddLE(row, minHeight: 30f * s);
            Image img = UI.AddGalleryElementRoundedBg(row, bg);

            Text t = UI.CreateLabel(row, text, fontSize, fg, TextAnchor.MiddleLeft, name: "Text");
            RectTransform rt = t.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(8f * s, 2f);
            rt.offsetMax = new Vector2(-8f * s, -2f);
        }

        static bool TryGetBenchSceneSpec(FileEntry f, out string sceneSpec)
        {
            sceneSpec = null;
            if (f == null) return false;

            if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string rel, false))
            {
                string norm = rel.Replace('\\', '/');
                if (!norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase))
                    norm = "Saves/scene/" + norm.TrimStart('/');
                sceneSpec = norm;
                return true;
            }

            string uid = f.Uid;
            if (!string.IsNullOrEmpty(uid))
            {
                string inner = uid;
                int colon = uid.IndexOf(":/", StringComparison.Ordinal);
                if (colon >= 0) inner = uid.Substring(colon + 2);
                inner = inner.Replace('\\', '/');
                if (inner.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    && inner.IndexOf("/scene/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sceneSpec = uid.Replace('\\', '/');
                    return true;
                }
            }

            string path = (f.Path ?? "").Replace('\\', '/');
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && path.IndexOf("/scene/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sceneSpec = !string.IsNullOrEmpty(uid) ? uid.Replace('\\', '/') : path;
                return true;
            }
            return false;
        }

        int BenchAddScenesFromSelection(VpbBenchConfig cfg, out int skippedDueToCap)
        {
            skippedDueToCap = 0;
            if (cfg == null) return 0;
            if (selectedFiles == null || selectedFiles.Count == 0) return 0;
            IList<FileEntry> sel = selectedFiles;
            int room = BenchMaxScenesInConfig - cfg.Scenes.Count;
            if (room <= 0) return 0;
            int added = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cfg.Scenes.Count; i++)
            {
                string existing = cfg.Scenes[i];
                if (!string.IsNullOrEmpty(existing)) seen.Add(existing);
            }
            for (int i = 0; i < sel.Count; i++)
            {
                FileEntry f = sel[i];
                if (!TryGetBenchSceneSpec(f, out string spec) || string.IsNullOrEmpty(spec)) continue;
                if (!seen.Add(spec)) continue;
                if (added >= room)
                {
                    skippedDueToCap++;
                    continue;
                }
                cfg.Scenes.Add(spec);
                added++;
            }
            return added;
        }

        int BenchAddPackagesFromSelection(VpbBenchCandidateConfig cand, out int skippedDueToCap)
        {
            skippedDueToCap = 0;
            if (cand == null) return 0;
            if (selectedFiles == null || selectedFiles.Count == 0) return 0;
            IList<FileEntry> sel = selectedFiles;
            int room = BenchMaxPackagesInConfig - cand.PackageUids.Count;
            if (room <= 0) return 0;
            var uids = CollectUniquePackageUidsFromSelection(sel);
            int added = 0;
            foreach (string uid in uids)
            {
                if (string.IsNullOrEmpty(uid)) continue;
                if (cand.PackageUids.Contains(uid)) continue;
                if (added >= room)
                {
                    skippedDueToCap++;
                    continue;
                }
                cand.PackageUids.Add(uid);
                added++;
            }
            return added;
        }

        static void BenchAddRemovableRow(Transform parent, string label, int fontSize, float s, bool altStripe, UnityAction onRemove)
        {
            UI.CreateRemovableStripeRow(
                parent, label, fontSize, 34f * s, 48f * s, 6f * s,
                6f * s, UI.Pad(6, 4, 3, 3, s), altStripe, "×", onRemove, flexibleRowWidth: true);
        }

        static string BenchShortenListLabel(string entry, int maxLen = 52)
        {
            if (string.IsNullOrEmpty(entry)) return "";
            string s = entry.Replace('\\', '/');
            if (s.Length <= maxLen) return s;
            return "…" + s.Substring(s.Length - (maxLen - 1));
        }

        static void BenchAttachVerticalScrollbar(GameObject scrollHost, ScrollRect sr)
        {
            if (scrollHost == null || sr == null) return;
            try
            {
                GameObject sbGo = UI.CreateScrollBar(scrollHost, BenchScrollBarWidth, 0f, Scrollbar.Direction.BottomToTop);
                if (sbGo == null) return;
                Scrollbar sb = sbGo.GetComponent<Scrollbar>();
                ScrollbarSync sync = sbGo.AddComponent<ScrollbarSync>();
                sync.scrollRect = sr;
                sync.scrollbar = sb;
                sync.minSizePixels = 30f;
                sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                sr.verticalScrollbar = null;
            }
            catch { }
        }

        void BenchSetStatus(string msg)
        {
            if (_benchStatusText != null) _benchStatusText.text = msg ?? "";
        }

        static void BenchAddPanelClickBlocker(Transform panel)
        {
            if (panel == null) return;
            GameObject block = new GameObject("ClickBlocker");
            block.transform.SetParent(panel, false);
            block.transform.SetAsFirstSibling();
            RectTransform rt = block.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = UI.AddImage(block, new Color(1f, 1f, 1f, 0.004f));
        }

        static void BenchDestroyChildren(Transform parent) => UI.DestroyAllChildren(parent);

        static void BenchAddSimpleLabel(Transform parent, string text, int fontSize, float s)
        {
            Text t = UI.CreateLabel(parent.gameObject, text, fontSize, new Color(0.88f, 0.90f, 0.94f, 1f), name: "Label");
            LayoutElement le = UI.AddLE(t.gameObject, minHeight: 28f * s);
        }

        static void BenchAddSimpleToggle(Transform parent, string label, bool on, Action<bool> onChanged, int fontSize, float s)
        {
            GameObject row = new GameObject("Toggle");
            row.transform.SetParent(parent, false);
            Image rowBg = UI.AddImage(row, new Color(1f, 1f, 1f, 0.004f));
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f * s;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            LayoutElement le = UI.AddLE(row, minHeight: 38f * s);

            UI.CreateChromeLayoutButton(row.transform, 38f * s, 34f * s, on ? "✓" : "",
                fontSize, on ? new Color(0.28f, 0.50f, 0.34f, 1f) : new Color(0.20f, 0.22f, 0.26f, 1f),
                () => onChanged(!on));

            Text t = UI.CreateLabel(row, label, fontSize, Color.white, name: "Text");
            LayoutElement tle = UI.AddLE(t.gameObject, flexibleWidth: 1f);
        }

        void BenchAddSceneActionRow(Transform parent, VpbBenchConfig cfg, int fontSize, float s)
        {
            int actionFont = fontSize;
            int selCount = selectedFiles != null ? selectedFiles.Count : 0;
            GameObject row = new GameObject("SceneActions");
            row.transform.SetParent(parent, false);
            Image rowBg = UI.AddImage(row, new Color(1f, 1f, 1f, 0.004f));
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f * s;
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            LayoutElement le = UI.AddLE(row, minHeight: 34f * s);

            UI.CreateChromeLayoutButton(row.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.pick_scenes", "Pick…"),
                actionFont, new Color(0.20f, 0.40f, 0.55f, 1f), BenchStartPickScenes);
            if (cfg != null && cfg.Scenes != null && cfg.Scenes.Count > 0)
            {
                UI.CreateChromeLayoutButton(row.transform, 0f, 32f * s,
                    VPBTranslation.T("bench.simple.clear_scenes_short", "Clear"),
                    actionFont, new Color(0.52f, 0.28f, 0.28f, 1f), () => BenchConfirmClearAllScenes(cfg));
            }
            UI.CreateChromeLayoutButton(row.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.add_selection_short", "Sel") + " (" + selCount + ")",
                actionFont, new Color(0.20f, 0.40f, 0.55f, 1f), BenchAddScenesFromGallerySelection);
        }

        void BenchAddPackageActionRow(Transform parent, VpbBenchCandidateConfig cand, int fontSize, float s)
        {
            int actionFont = fontSize;
            GameObject row = new GameObject("PackageActions");
            row.transform.SetParent(parent, false);
            Image rowBg = UI.AddImage(row, new Color(1f, 1f, 1f, 0.004f));
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f * s;
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            LayoutElement le = UI.AddLE(row, minHeight: 34f * s);

            UI.CreateChromeLayoutButton(row.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.pick_packages", "Pick…"),
                actionFont, new Color(0.20f, 0.40f, 0.55f, 1f), BenchStartPickPackages);
            if (cand != null && cand.PackageUids != null && cand.PackageUids.Count > 0)
            {
                UI.CreateChromeLayoutButton(row.transform, 0f, 32f * s,
                    VPBTranslation.T("bench.simple.clear_packages_short", "Clear"),
                    actionFont, new Color(0.52f, 0.28f, 0.28f, 1f), () => BenchConfirmClearAllPackages(cand));
            }
        }

        void BenchConfirmRemoveScene(VpbBenchConfig cfg, int index)
        {
            if (cfg == null || cfg.Scenes == null || index < 0 || index >= cfg.Scenes.Count) return;
            string entry = BenchShortenListLabel(cfg.Scenes[index], 48);
            BenchShowConfirm(
                VPBTranslation.T("bench.confirm.remove_scene.title", "Remove scene?"),
                VPBTranslation.T("bench.confirm.remove_scene.body", "Remove this scene from the test list?\n\n{0}").Replace("{0}", entry),
                () =>
                {
                    if (index >= 0 && index < cfg.Scenes.Count)
                        cfg.Scenes.RemoveAt(index);
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                });
        }

        void BenchConfirmClearAllScenes(VpbBenchConfig cfg)
        {
            if (cfg == null || cfg.Scenes == null || cfg.Scenes.Count == 0) return;
            int n = cfg.Scenes.Count;
            BenchShowConfirm(
                VPBTranslation.T("bench.confirm.clear_scenes.title", "Clear all scenes?"),
                VPBTranslation.T("bench.confirm.clear_scenes.body", "Remove all {0} scene(s) from the test list?").Replace("{0}", n.ToString()),
                () =>
                {
                    cfg.Scenes.Clear();
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                });
        }

        void BenchConfirmRemovePackage(VpbBenchCandidateConfig cand, int index)
        {
            if (cand == null || cand.PackageUids == null || index < 0 || index >= cand.PackageUids.Count) return;
            string entry = BenchShortenListLabel(cand.PackageUids[index], 48);
            BenchShowConfirm(
                VPBTranslation.T("bench.confirm.remove_package.title", "Remove package?"),
                VPBTranslation.T("bench.confirm.remove_package.body", "Remove this package from the test list?\n\n{0}").Replace("{0}", entry),
                () =>
                {
                    if (index >= 0 && index < cand.PackageUids.Count)
                        cand.PackageUids.RemoveAt(index);
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                });
        }

        void BenchConfirmClearAllPackages(VpbBenchCandidateConfig cand)
        {
            if (cand == null || cand.PackageUids == null || cand.PackageUids.Count == 0) return;
            int n = cand.PackageUids.Count;
            BenchShowConfirm(
                VPBTranslation.T("bench.confirm.clear_packages.title", "Clear all packages?"),
                VPBTranslation.T("bench.confirm.clear_packages.body", "Remove all {0} package(s) from the test list?").Replace("{0}", n.ToString()),
                () =>
                {
                    cand.PackageUids.Clear();
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                },
                VPBTranslation.T("bench.confirm.clear", "Clear"));
        }

        void BenchShowConfirm(string title, string message, Action onConfirm, string confirmLabel = null)
        {
            BenchHideConfirm();
            if (_benchModalRoot == null) return;

            float s = ChromeScale;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int titleFont = type.Title;
            int bodyFont = type.Body;

            // Dim Button with null dismiss consumes clicks — do not pass through to bench dim close.
            GameObject panel;
            _benchConfirmRoot = UI.CreateModalChrome(
                _benchModalRoot, "VPB_BenchConfirm", 480f * s, 220f * s,
                new Color(0.08f, 0.09f, 0.11f, 1f), null, out panel, dimAlpha: 0.55f);

            VerticalLayoutGroup v = panel.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(Mathf.RoundToInt(14f * s), Mathf.RoundToInt(14f * s), Mathf.RoundToInt(12f * s), Mathf.RoundToInt(12f * s));
            v.spacing = 8f * s;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            UI.CreateEmphasisTitleLabel(panel, title ?? "", titleFont);

            Text bodyText = UI.CreateLabel(panel, message ?? "", bodyFont, new Color(0.88f, 0.90f, 0.94f, 1f), TextAnchor.UpperLeft, verticalWrap: VerticalWrapMode.Overflow, name: "Body");
            LayoutElement bodyLe = UI.AddLE(bodyText.gameObject, minHeight: 72f * s, flexibleHeight: 1f);

            GameObject btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup brh = btnRow.AddComponent<HorizontalLayoutGroup>();
            brh.spacing = 8f * s;
            brh.childForceExpandWidth = true;
            brh.childControlWidth = true;
            LayoutElement brLe = UI.AddLE(btnRow, minHeight: 36f * s);

            UI.CreateChromeLayoutButton(btnRow.transform, 0f, 34f * s,
                VPBTranslation.T("hook.cancel", "Cancel"), bodyFont, new Color(0.35f, 0.35f, 0.38f, 1f), BenchHideConfirm);
            string okLabel = string.IsNullOrEmpty(confirmLabel)
                ? VPBTranslation.T("bench.confirm.remove", "Remove")
                : confirmLabel;
            UI.CreateChromeLayoutButton(btnRow.transform, 0f, 34f * s,
                okLabel, bodyFont, new Color(0.55f, 0.28f, 0.28f, 1f), () =>
                {
                    BenchHideConfirm();
                    if (onConfirm != null) onConfirm();
                });
        }

        void BenchHideConfirm()
        {
            if (_benchConfirmRoot == null) return;
            try { UnityEngine.Object.Destroy(_benchConfirmRoot); } catch { }
            _benchConfirmRoot = null;
        }

        void BenchAddListSection(Transform parent, string title, List<string> items,
            int fontSize, float s, float listHeight, out Transform listParent, Action<int> onRemove,
            string emptyText)
        {
            BenchAddSimpleLabel(parent, title + "  (" + (items != null ? items.Count : 0) + ")", fontSize, s);

            GameObject listHost = new GameObject("List");
            listHost.transform.SetParent(parent, false);
            LayoutElement lle = UI.AddLE(listHost, minHeight: listHeight, preferredHeight: listHeight);
            Image listBg = UI.AddImage(listHost, new Color(0.05f, 0.06f, 0.08f, 1f));

            GameObject vp = new GameObject("Viewport");
            vp.transform.SetParent(listHost.transform, false);
            RectTransform vpRt = vp.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(4f, 4f);
            vpRt.offsetMax = new Vector2(-(4f + BenchScrollBarWidth), -4f);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            Image listVpImg = UI.AddImage(vp, Color.white);

            GameObject content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            RectTransform crt = content.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            VerticalLayoutGroup v = content.AddComponent<VerticalLayoutGroup>();
            v.childControlWidth = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.spacing = 2f;
            LayoutElement contentLe = UI.AddLE(content, minWidth: 0f, flexibleWidth: 1f);
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            listParent = content.transform;

            ScrollRect sr = listHost.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.viewport = vpRt;
            sr.content = crt;
            BenchAttachVerticalScrollbar(listHost, sr);

            if (items == null || items.Count == 0)
                ScanWlAddPlaceholderRow(listParent, emptyText, fontSize, s);
            else
            {
                int showN = Mathf.Min(items.Count, BenchMaxVisibleListRows);
                for (int i = 0; i < showN; i++)
                {
                    int idx = i;
                    string entry = BenchShortenListLabel(items[i]);
                    BenchAddRemovableRow(listParent, entry, fontSize, s, (i & 1) == 1, () => onRemove(idx));
                }
                if (items.Count > showN)
                {
                    int more = items.Count - showN;
                    ScanWlAddPlaceholderRow(listParent,
                        VPBTranslation.T("bench.simple.list_truncated", "… and {0} more (scroll list / remove above).")
                            .Replace("{0}", more.ToString()),
                        fontSize, s);
                }
            }
        }

        static InputField BenchAddSimpleField(Transform parent, string label, string value, int fontSize, float s, Action<string> onChanged)
        {
            GameObject block = new GameObject("Field");
            block.transform.SetParent(parent, false);
            VerticalLayoutGroup v = block.AddComponent<VerticalLayoutGroup>();
            v.spacing = 4f * s;
            v.childForceExpandWidth = true;

            Text lbl = UI.CreateLabel(block, label, fontSize, new Color(0.70f, 0.74f, 0.78f, 1f), name: "Label");

            InputField input = ScanWlCreateInputField(block.transform, fontSize, s, 1f, value);
            if (input != null)
            {
                input.text = value ?? "";
                if (onChanged != null)
                    input.onEndEdit.AddListener(delegate(string t) { onChanged(t); });
            }
            return input;
        }
    }
}
