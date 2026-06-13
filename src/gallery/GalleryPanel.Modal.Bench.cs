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

            _benchModalRoot = new GameObject("VPB_BenchModal");
            _benchModalRoot.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRt = _benchModalRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            GameObject dimGo = new GameObject("Dim");
            dimGo.transform.SetParent(_benchModalRoot.transform, false);
            RectTransform dimRt = dimGo.AddComponent<RectTransform>();
            dimRt.anchorMin = Vector2.zero;
            dimRt.anchorMax = Vector2.one;
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            Image dim = dimGo.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = true;
            Button dimBtn = dimGo.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            dimBtn.onClick.AddListener(HideBenchEditorModal);

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_benchModalRoot.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(660f * s, 760f * s);
            Image pbg = panel.AddComponent<Image>();
            pbg.color = new Color(0.07f, 0.08f, 0.10f, 1f);
            pbg.raycastTarget = true;
            BenchAddPanelClickBlocker(panel.transform);

            VerticalLayoutGroup pv = panel.AddComponent<VerticalLayoutGroup>();
            pv.padding = new RectOffset(Mathf.RoundToInt(16f * s), Mathf.RoundToInt(16f * s), Mathf.RoundToInt(14f * s), Mathf.RoundToInt(12f * s));
            pv.spacing = 10f * s;
            pv.childControlWidth = true;
            pv.childControlHeight = true;
            pv.childForceExpandWidth = true;
            pv.childForceExpandHeight = false;

            // Header
            GameObject header = new GameObject("Header");
            header.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup hh = header.AddComponent<HorizontalLayoutGroup>();
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.childForceExpandWidth = false;
            LayoutElement hle = header.AddComponent<LayoutElement>();
            hle.minHeight = 44f * s;

            GameObject titleGo = new GameObject("Title");
            titleGo.transform.SetParent(header.transform, false);
            Text title = titleGo.AddComponent<Text>();
            title.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            GalleryUiMetrics.ApplyEmphasisTitle(title, titleFont);
            title.fontStyle = FontStyle.Normal;
            title.color = Color.white;
            title.text = VPBTranslation.T("bench.simple.title", "Scene Load Test");
            try { VPBUiFont.ApplyTo(title); } catch { }
            LayoutElement tle = titleGo.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;

            ScanWlCreateHeaderButton(header.transform, 84f * s, 36f * s, VPBTranslation.T("hook.close", "Close"), bodyFont,
                new Color(0.38f, 0.32f, 0.22f, 1f), HideBenchEditorModal);

            GameObject helpGo = new GameObject("Help");
            helpGo.transform.SetParent(panel.transform, false);
            _benchHelpText = helpGo.AddComponent<Text>();
            _benchHelpText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _benchHelpText.fontSize = bodyFont;
            _benchHelpText.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            _benchHelpText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _benchHelpText.text = VPBTranslation.T("bench.simple.help",
                "Pick scenes → Run Test. Changes save automatically. Compares when baseline exists.");
            try { VPBUiFont.ApplyTo(_benchHelpText); } catch { }
            LayoutElement helpLe = helpGo.AddComponent<LayoutElement>();
            helpLe.minHeight = 52f * s;

            // Scroll body
            GameObject scrollHost = new GameObject("Scroll");
            scrollHost.transform.SetParent(panel.transform, false);
            LayoutElement scrollLe = scrollHost.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.minHeight = 400f * s;
            Image scrollBg = scrollHost.AddComponent<Image>();
            scrollBg.color = new Color(0.09f, 0.10f, 0.12f, 1f);
            scrollBg.raycastTarget = true;
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
            Image vpImg = viewport.AddComponent<Image>();
            vpImg.color = Color.white;
            vpImg.raycastTarget = true;

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
            GameObject statusGo = new GameObject("Status");
            statusGo.transform.SetParent(panel.transform, false);
            _benchStatusText = statusGo.AddComponent<Text>();
            _benchStatusText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _benchStatusText.fontSize = bodyFont;
            _benchStatusText.color = new Color(0.65f, 0.82f, 1f, 1f);
            _benchStatusText.alignment = TextAnchor.MiddleLeft;
            _benchStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            LayoutElement stLe = statusGo.AddComponent<LayoutElement>();
            stLe.minHeight = 36f * s;

            ScanWlCreateHeaderButton(panel.transform, 0f, 50f * s,
                VPBTranslation.T("bench.simple.run", "Run Test"),
                titleFont,
                new Color(0.22f, 0.52f, 0.30f, 1f), BenchModalRunTest);

            GameObject subRow = new GameObject("SubActions");
            subRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup subH = subRow.AddComponent<HorizontalLayoutGroup>();
            subH.spacing = 6f * s;
            subH.childForceExpandWidth = true;
            subH.childControlWidth = true;
            LayoutElement subLe = subRow.AddComponent<LayoutElement>();
            subLe.minHeight = 34f * s;
            int subFont = bodyFont;

            ScanWlCreateHeaderButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.set_baseline", "Baseline"),
                subFont, new Color(0.24f, 0.38f, 0.52f, 1f), BenchModalSetBaseline);

            ScanWlCreateHeaderButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.open_results", "Results"),
                subFont, new Color(0.22f, 0.40f, 0.48f, 1f), BenchOpenLastResultsFolder);

            ScanWlCreateHeaderButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.open_csv", "CSV"),
                subFont, new Color(0.22f, 0.40f, 0.48f, 1f), BenchOpenLastCompareCsv);

            ScanWlCreateHeaderButton(subRow.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.open_screenshots", "Shots"),
                subFont, new Color(0.22f, 0.40f, 0.48f, 1f), BenchOpenLastScreenshotsFolder);

            ScanWlCreateHeaderButton(subRow.transform, 0f, 32f * s,
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
            ScanWlCreateHeaderButton(_benchAdvancedBlock.transform, 180f * s, 34f * s,
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

            ScanWlCreateHeaderButton(_benchAdvancedBlock.transform, 140f * s, 34f * s,
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
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 40f * s;

            BenchAddPresetButton(row.transform, cfg, VpbBenchPresetKind.Quick, bodyFont, s);
            BenchAddPresetButton(row.transform, cfg, VpbBenchPresetKind.Standard, bodyFont, s);
            BenchAddPresetButton(row.transform, cfg, VpbBenchPresetKind.Deep, bodyFont, s);
        }

        void BenchAddPresetButton(Transform parent, VpbBenchConfig cfg, VpbBenchPresetKind kind, int bodyFont, float s)
        {
            bool active = cfg.Preset == kind;
            ScanWlCreateHeaderButton(parent, 0f, 38f * s, VpbBenchPresetUtil.Label(kind), bodyFont,
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
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 40f * s;

            ScanWlCreateHeaderButton(row.transform, 36f * s, 34f * s, "◀", bodyFont,
                new Color(0.22f, 0.24f, 0.28f, 1f), () =>
                {
                    int idx = ids.IndexOf(cfg.BaselineId ?? "main");
                    if (idx < 0) idx = 0;
                    idx = (idx - 1 + ids.Count) % ids.Count;
                    cfg.BaselineId = ids[idx];
                    VpbBenchConfigStore.MarkDirty();
                    RebuildBenchEditorModal();
                });

            GameObject nameGo = new GameObject("Name");
            nameGo.transform.SetParent(row.transform, false);
            Text nameText = nameGo.AddComponent<Text>();
            nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            nameText.fontSize = bodyFont;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.MiddleCenter;
            bool hasBaseline = VpbBenchComparer.BaselineExists(cfg.BaselineId);
            nameText.text = (cfg.BaselineId ?? "main") + (hasBaseline ? "" : " (new)");
            try { VPBUiFont.ApplyTo(nameText); } catch { }
            LayoutElement nle = nameGo.AddComponent<LayoutElement>();
            nle.minWidth = 180f * s;
            nle.preferredWidth = 220f * s;

            ScanWlCreateHeaderButton(row.transform, 36f * s, 34f * s, "▶", bodyFont,
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
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 30f * s;
            Image img = row.AddComponent<Image>();
            img.color = bg;

            GameObject tgo = new GameObject("Text");
            tgo.transform.SetParent(row.transform, false);
            RectTransform rt = tgo.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8f * s, 2f);
            rt.offsetMax = new Vector2(-8f * s, -2f);
            Text t = tgo.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = fg;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.text = text;
            try { VPBUiFont.ApplyTo(t); } catch { }
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
            float rowH = 34f * s;
            float removeW = 48f * s;

            GameObject row = new GameObject("Row");
            row.transform.SetParent(parent, false);
            Image rowBg = row.AddComponent<Image>();
            rowBg.color = altStripe ? new Color(0.11f, 0.11f, 0.14f, 1f) : new Color(0.09f, 0.09f, 0.11f, 1f);
            rowBg.raycastTarget = true;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(Mathf.RoundToInt(6f * s), Mathf.RoundToInt(4f * s), Mathf.RoundToInt(3f * s), Mathf.RoundToInt(3f * s));
            h.spacing = 6f * s;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            LayoutElement rle = row.AddComponent<LayoutElement>();
            rle.minHeight = rowH;
            rle.preferredHeight = rowH;
            rle.flexibleWidth = 1f;
            rle.minWidth = 0f;

            GameObject lbl = new GameObject("Label");
            lbl.transform.SetParent(row.transform, false);
            Text lt = lbl.AddComponent<Text>();
            lt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            lt.fontSize = fontSize;
            lt.color = new Color(0.92f, 0.92f, 0.94f, 1f);
            lt.alignment = TextAnchor.MiddleLeft;
            lt.horizontalOverflow = HorizontalWrapMode.Overflow;
            lt.text = label ?? "";
            try { VPBUiFont.ApplyTo(lt); } catch { }
            LayoutElement lle = lbl.AddComponent<LayoutElement>();
            lle.flexibleWidth = 1f;
            lle.minWidth = 0f;

            ScanWlCreateHeaderButton(row.transform, removeW, rowH - 6f * s, "×",
                fontSize,
                new Color(0.52f, 0.28f, 0.28f, 1f), onRemove);
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
            Image img = block.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.004f);
            img.raycastTarget = true;
        }

        static void BenchDestroyChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
                try { UnityEngine.Object.Destroy(parent.GetChild(i).gameObject); } catch { }
        }

        static void BenchAddSimpleLabel(Transform parent, string text, int fontSize, float s)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Normal;
            t.color = new Color(0.88f, 0.90f, 0.94f, 1f);
            t.text = text;
            try { VPBUiFont.ApplyTo(t); } catch { }
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = 28f * s;
        }

        static void BenchAddSimpleToggle(Transform parent, string label, bool on, Action<bool> onChanged, int fontSize, float s)
        {
            GameObject row = new GameObject("Toggle");
            row.transform.SetParent(parent, false);
            Image rowBg = row.AddComponent<Image>();
            rowBg.color = new Color(1f, 1f, 1f, 0.004f);
            rowBg.raycastTarget = true;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f * s;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 38f * s;

            ScanWlCreateHeaderButton(row.transform, 38f * s, 34f * s, on ? "✓" : "",
                fontSize, on ? new Color(0.28f, 0.50f, 0.34f, 1f) : new Color(0.20f, 0.22f, 0.26f, 1f),
                () => onChanged(!on));

            GameObject lbl = new GameObject("Text");
            lbl.transform.SetParent(row.transform, false);
            Text t = lbl.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fontSize;
            t.color = Color.white;
            t.text = label;
            try { VPBUiFont.ApplyTo(t); } catch { }
            LayoutElement tle = lbl.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;
        }

        void BenchAddSceneActionRow(Transform parent, VpbBenchConfig cfg, int fontSize, float s)
        {
            int actionFont = fontSize;
            int selCount = selectedFiles != null ? selectedFiles.Count : 0;
            GameObject row = new GameObject("SceneActions");
            row.transform.SetParent(parent, false);
            Image rowBg = row.AddComponent<Image>();
            rowBg.color = new Color(1f, 1f, 1f, 0.004f);
            rowBg.raycastTarget = true;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f * s;
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 34f * s;

            ScanWlCreateHeaderButton(row.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.pick_scenes", "Pick…"),
                actionFont, new Color(0.20f, 0.40f, 0.55f, 1f), BenchStartPickScenes);
            if (cfg != null && cfg.Scenes != null && cfg.Scenes.Count > 0)
            {
                ScanWlCreateHeaderButton(row.transform, 0f, 32f * s,
                    VPBTranslation.T("bench.simple.clear_scenes_short", "Clear"),
                    actionFont, new Color(0.52f, 0.28f, 0.28f, 1f), () => BenchConfirmClearAllScenes(cfg));
            }
            ScanWlCreateHeaderButton(row.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.add_selection_short", "Sel") + " (" + selCount + ")",
                actionFont, new Color(0.20f, 0.40f, 0.55f, 1f), BenchAddScenesFromGallerySelection);
        }

        void BenchAddPackageActionRow(Transform parent, VpbBenchCandidateConfig cand, int fontSize, float s)
        {
            int actionFont = fontSize;
            GameObject row = new GameObject("PackageActions");
            row.transform.SetParent(parent, false);
            Image rowBg = row.AddComponent<Image>();
            rowBg.color = new Color(1f, 1f, 1f, 0.004f);
            rowBg.raycastTarget = true;
            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6f * s;
            h.childForceExpandWidth = true;
            h.childControlWidth = true;
            LayoutElement le = row.AddComponent<LayoutElement>();
            le.minHeight = 34f * s;

            ScanWlCreateHeaderButton(row.transform, 0f, 32f * s,
                VPBTranslation.T("bench.simple.pick_packages", "Pick…"),
                actionFont, new Color(0.20f, 0.40f, 0.55f, 1f), BenchStartPickPackages);
            if (cand != null && cand.PackageUids != null && cand.PackageUids.Count > 0)
            {
                ScanWlCreateHeaderButton(row.transform, 0f, 32f * s,
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

            _benchConfirmRoot = new GameObject("VPB_BenchConfirm");
            _benchConfirmRoot.transform.SetParent(_benchModalRoot.transform, false);
            RectTransform rootRt = _benchConfirmRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Image dim = _benchConfirmRoot.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.55f);
            dim.raycastTarget = true;
            // Consume clicks on confirm overlay — do not pass through to bench dim close handler.
            Button dimBlock = _benchConfirmRoot.AddComponent<Button>();
            dimBlock.transition = Selectable.Transition.None;
            dimBlock.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(_benchConfirmRoot.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(480f * s, 220f * s);
            Image confirmPbg = panel.AddComponent<Image>();
            confirmPbg.color = new Color(0.08f, 0.09f, 0.11f, 1f);
            confirmPbg.raycastTarget = true;

            VerticalLayoutGroup v = panel.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(Mathf.RoundToInt(14f * s), Mathf.RoundToInt(14f * s), Mathf.RoundToInt(12f * s), Mathf.RoundToInt(12f * s));
            v.spacing = 8f * s;
            v.childControlWidth = true;
            v.childForceExpandWidth = true;

            GameObject titleGo = new GameObject("Title");
            titleGo.transform.SetParent(panel.transform, false);
            Text titleText = titleGo.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            GalleryUiMetrics.ApplyEmphasisTitle(titleText, titleFont);
            titleText.fontStyle = FontStyle.Normal;
            titleText.color = Color.white;
            titleText.text = title ?? "";
            try { VPBUiFont.ApplyTo(titleText); } catch { }

            GameObject bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(panel.transform, false);
            Text bodyText = bodyGo.AddComponent<Text>();
            bodyText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            bodyText.fontSize = bodyFont;
            bodyText.color = new Color(0.88f, 0.90f, 0.94f, 1f);
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            bodyText.text = message ?? "";
            try { VPBUiFont.ApplyTo(bodyText); } catch { }
            LayoutElement bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.minHeight = 72f * s;
            bodyLe.flexibleHeight = 1f;

            GameObject btnRow = new GameObject("Buttons");
            btnRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup brh = btnRow.AddComponent<HorizontalLayoutGroup>();
            brh.spacing = 8f * s;
            brh.childForceExpandWidth = true;
            brh.childControlWidth = true;
            LayoutElement brLe = btnRow.AddComponent<LayoutElement>();
            brLe.minHeight = 36f * s;

            ScanWlCreateHeaderButton(btnRow.transform, 0f, 34f * s,
                VPBTranslation.T("hook.cancel", "Cancel"), bodyFont, new Color(0.35f, 0.35f, 0.38f, 1f), BenchHideConfirm);
            string okLabel = string.IsNullOrEmpty(confirmLabel)
                ? VPBTranslation.T("bench.confirm.remove", "Remove")
                : confirmLabel;
            ScanWlCreateHeaderButton(btnRow.transform, 0f, 34f * s,
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
            LayoutElement lle = listHost.AddComponent<LayoutElement>();
            lle.minHeight = listHeight;
            lle.preferredHeight = listHeight;
            Image listBg = listHost.AddComponent<Image>();
            listBg.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            listBg.raycastTarget = true;

            GameObject vp = new GameObject("Viewport");
            vp.transform.SetParent(listHost.transform, false);
            RectTransform vpRt = vp.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(4f, 4f);
            vpRt.offsetMax = new Vector2(-(4f + BenchScrollBarWidth), -4f);
            vp.AddComponent<Mask>().showMaskGraphic = false;
            Image listVpImg = vp.AddComponent<Image>();
            listVpImg.color = Color.white;
            listVpImg.raycastTarget = true;

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
            LayoutElement contentLe = content.AddComponent<LayoutElement>();
            contentLe.flexibleWidth = 1f;
            contentLe.minWidth = 0f;
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

            GameObject lblGo = new GameObject("Label");
            lblGo.transform.SetParent(block.transform, false);
            Text lbl = lblGo.AddComponent<Text>();
            lbl.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            lbl.fontSize = fontSize;
            lbl.color = new Color(0.70f, 0.74f, 0.78f, 1f);
            lbl.text = label;
            try { VPBUiFont.ApplyTo(lbl); } catch { }

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
