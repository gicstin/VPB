using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Lightweight command palette (Ctrl+Shift+P). Cold/warm path — built on open, destroyed on close.
    /// </summary>
    public partial class GalleryPanel
    {
        private struct CommandPaletteEntry
        {
            public string Id;
            public string TitleKey;
            public string TitleDefault;
            public string Hint;
            public Action Run;
        }

        private GameObject _commandPaletteOverlayGO;
        private GameObject _commandPalettePanelGO;
        private InputField _commandPaletteInput;
        private RectTransform _commandPaletteListRT;
        private readonly List<CommandPaletteEntry> _commandPaletteCatalog = new List<CommandPaletteEntry>(40);
        private readonly List<int> _commandPaletteFiltered = new List<int>(40);
        private readonly List<GameObject> _commandPaletteRowGOs = new List<GameObject>(40);
        private int _commandPaletteSelected;
        private bool _commandPaletteOpen;
        private string _commandPaletteLastFilter = "";

        private bool IsCommandPaletteOpen { get { return _commandPaletteOpen; } }

        private void AddCommandPaletteEntry(string id, string titleKey, string titleDefault, string hint, Action run)
        {
            _commandPaletteCatalog.Add(new CommandPaletteEntry
            {
                Id = id,
                TitleKey = titleKey,
                TitleDefault = titleDefault,
                Hint = hint ?? "",
                Run = run
            });
        }

        private void EnsureCommandPaletteCatalog()
        {
            if (_commandPaletteCatalog.Count > 0) return;

            AddCommandPaletteEntry("undo", "gallery.cmd.undo", "Undo", "Ctrl+Z",
                () => { try { Undo(); } catch { } });
            AddCommandPaletteEntry("redo", "gallery.cmd.redo", "Redo", "Ctrl+Y",
                () => { try { Redo(); } catch { } });
            AddCommandPaletteEntry("apply", "gallery.cmd.apply", "Apply selection", "Enter",
                () => { try { TryKeyboardApplySelection(); } catch { } });
            AddCommandPaletteEntry("clear_selection", "gallery.cmd.clear_selection", "Clear selection", "Esc",
                () => { try { ClearSelection(); } catch { } });
            AddCommandPaletteEntry("search", "gallery.cmd.focus_search", "Focus search", "Ctrl+F",
                () => { try { FocusTitleSearchInput(); } catch { } });
            AddCommandPaletteEntry("clear_filters", "gallery.cmd.clear_filters", "Clear browse filters", "",
                () =>
                {
                    try { ClearAllBrowseFiltersKeepCategory(); } catch { try { RefreshFiles(true); } catch { } }
                });
            AddCommandPaletteEntry("scene_tools", "gallery.cmd.scene_tools", "Toggle Scene Tools", "Ctrl+Shift+K",
                () => { try { ToggleCreatorMode(); } catch { } });
            AddCommandPaletteEntry("scene_eraser", "gallery.cmd.scene_eraser", "Toggle Scene Eraser", "Ctrl+Shift+E",
                () => { try { ToggleRemoveMode(false, false); } catch { } });
            AddCommandPaletteEntry("import", "gallery.cmd.import", "Toggle Import sidebar", "Alt+I",
                () => { try { ToggleImportSidebar(); } catch { } });
            AddCommandPaletteEntry("cleanup", "gallery.cmd.cleanup", "Open Cleanup", "",
                () => { try { TboxOpenCleanupView(); } catch { } });
            AddCommandPaletteEntry("tryon_toggle", "gallery.cmd.tryon_toggle", "Toggle Try-On mode (settings)", "",
                () => { try { ToggleTryOnModeSettingFromPalette(); } catch { } });
            AddCommandPaletteEntry("apply_mode", "gallery.cmd.apply_mode", "Toggle 1-Click / 2-Click apply", "",
                () => { try { ToggleApplyMode(); } catch { } });
            AddCommandPaletteEntry("hold_launch", "gallery.cmd.hold_launch", "Toggle Hold-to-launch", "",
                () => { try { ToggleHoldToLaunch(); } catch { } });
            AddCommandPaletteEntry("filter_presets", "gallery.cmd.filter_presets", "Filter presets", "Alt+F",
                () =>
                {
                    try
                    {
                        if (quickFiltersUI == null) return;
                        quickFiltersUI.SetVisible(!quickFiltersUI.IsVisible);
                    }
                    catch { }
                });
            AddCommandPaletteEntry("delete_packages", "gallery.cmd.delete_packages", "Delete selected packages/scenes", "Delete",
                () => { try { TboxDeleteSelectedPackages(); } catch { } });
            AddCommandPaletteEntry("help", "gallery.cmd.help", "Open help (Hotkeys)", "F1 / ?",
                () => { try { OpenInAppHelpToSection("hotkeys"); } catch { try { ToggleInAppHelpPanel(); } catch { } } });
            AddCommandPaletteEntry("strip", "gallery.cmd.strip_scene", "Strip Scene window", "Ctrl+Shift+S",
                () => { try { HotkeyOpenStripSceneDirect(); } catch { } });
            AddCommandPaletteEntry("palette", "gallery.cmd.palette_hint", "Command palette", "Ctrl+Shift+P",
                () => { });
        }

        private void ToggleTryOnModeSettingFromPalette()
        {
            if (VPBConfig.Instance == null) return;
            bool next = !VPBConfig.Instance.TryOnModeEnabled;
            VPBConfig.Instance.TryOnModeEnabled = next;
            try { VPBConfig.Instance.TriggerChange(); } catch { }
            try { VPBConfig.Instance.Save(); } catch { }
            ShowTemporaryStatus(
                next
                    ? VPBTranslation.T("gallery.tryon.enabled", "Try-On mode ON — eligible applies preview first (Keep / Revert / Esc).")
                    : VPBTranslation.T("gallery.tryon.disabled", "Try-On mode OFF — grid applies commit immediately."),
                2.25f);
            try { RefreshModeAmbientChrome(); } catch { }
        }

        private void ToggleCommandPalette()
        {
            if (_commandPaletteOpen) CloseCommandPalette();
            else OpenCommandPalette();
        }

        private void OpenCommandPalette()
        {
            if (backgroundBoxGO == null) return;
            EnsureCommandPaletteCatalog();
            CloseCommandPalette();

            GameObject panelGO;
            GameObject overlayGO = UI.CreateModalChrome(
                backgroundBoxGO, "CommandPaletteOverlay", 520f, 420f, UI.ChromeDarker, null, out panelGO, dimAlpha: 0.45f);
            _commandPaletteOverlayGO = overlayGO;
            _commandPalettePanelGO = panelGO;
            _commandPaletteOpen = true;
            _commandPaletteSelected = 0;
            _commandPaletteLastFilter = "";

            UI.CreateLabel(
                panelGO,
                VPBTranslation.T("gallery.cmd.title", "Commands"),
                GalleryUiDesignTokens.FontRef,
                Color.white,
                TextAnchor.MiddleLeft,
                anchorPreset: AnchorPresets.hStretchTop,
                size: new Vector2(0, 36),
                anchoredPosition: new Vector2(16, -8),
                name: "CmdTitle");

            GameObject searchRow = UI.CreateChildRT(panelGO, "CmdSearch", AnchorPresets.hStretchTop, new Vector2(0, 36));
            RectTransform searchRT = searchRow.GetComponent<RectTransform>();
            searchRT.offsetMin = new Vector2(16, -84);
            searchRT.offsetMax = new Vector2(-16, -48);

            _commandPaletteInput = CreateSearchInput(
                searchRow,
                480f,
                OnCommandPaletteFilterChanged,
                () =>
                {
                    if (_commandPaletteInput != null) _commandPaletteInput.text = "";
                    RebuildCommandPaletteRows("");
                },
                CloseCommandPalette);
            if (_commandPaletteInput != null)
            {
                RectTransform irt = _commandPaletteInput.GetComponent<RectTransform>();
                if (irt != null)
                {
                    irt.anchorMin = new Vector2(0f, 0f);
                    irt.anchorMax = new Vector2(1f, 1f);
                    irt.pivot = new Vector2(0.5f, 0.5f);
                    irt.offsetMin = Vector2.zero;
                    irt.offsetMax = Vector2.zero;
                }
            }

            GameObject listGO = UI.CreateChildRT(panelGO, "CmdList", AnchorPresets.stretchAll);
            _commandPaletteListRT = listGO.GetComponent<RectTransform>();
            _commandPaletteListRT.offsetMin = new Vector2(12, 12);
            _commandPaletteListRT.offsetMax = new Vector2(-12, -96);
            var vlg = UI.AddVLG(listGO, spacing: 4f, childAlignment: TextAnchor.UpperLeft);
            if (vlg != null)
            {
                vlg.childForceExpandHeight = false;
                vlg.childForceExpandWidth = true;
            }

            RebuildCommandPaletteRows("");
            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);

            if (_commandPaletteInput != null)
            {
                _commandPaletteInput.ActivateInputField();
                _commandPaletteInput.Select();
            }
        }

        private void CloseCommandPalette()
        {
            _commandPaletteOpen = false;
            _commandPaletteRowGOs.Clear();
            _commandPaletteFiltered.Clear();
            try
            {
                if (_commandPaletteOverlayGO != null) Destroy(_commandPaletteOverlayGO);
            }
            catch { }
            _commandPaletteOverlayGO = null;
            _commandPalettePanelGO = null;
            _commandPaletteInput = null;
            _commandPaletteListRT = null;
        }

        private void OnCommandPaletteFilterChanged(string raw)
        {
            RebuildCommandPaletteRows(raw ?? "");
        }

        private void RebuildCommandPaletteRows(string raw)
        {
            if (_commandPaletteListRT == null) return;
            string filter = raw != null ? raw.Trim() : "";
            if (filter == _commandPaletteLastFilter && _commandPaletteRowGOs.Count > 0
                && _commandPaletteFiltered.Count > 0)
                return;
            _commandPaletteLastFilter = filter;

            for (int i = 0; i < _commandPaletteRowGOs.Count; i++)
            {
                try { if (_commandPaletteRowGOs[i] != null) Destroy(_commandPaletteRowGOs[i]); } catch { }
            }
            _commandPaletteRowGOs.Clear();
            _commandPaletteFiltered.Clear();

            string filterLower = filter.Length > 0 ? filter.ToLowerInvariant() : null;
            for (int i = 0; i < _commandPaletteCatalog.Count; i++)
            {
                CommandPaletteEntry e = _commandPaletteCatalog[i];
                // Skip self-hint entry from runnable list unless searching for it.
                if (e.Id == "palette" && filterLower == null) continue;
                string title = VPBTranslation.T(e.TitleKey, e.TitleDefault);
                if (filterLower != null)
                {
                    string hay = (title + " " + e.Hint + " " + e.Id).ToLowerInvariant();
                    if (hay.IndexOf(filterLower, StringComparison.Ordinal) < 0) continue;
                }
                _commandPaletteFiltered.Add(i);
            }

            if (_commandPaletteSelected >= _commandPaletteFiltered.Count)
                _commandPaletteSelected = Math.Max(0, _commandPaletteFiltered.Count - 1);

            for (int r = 0; r < _commandPaletteFiltered.Count; r++)
            {
                int catalogIdx = _commandPaletteFiltered[r];
                CommandPaletteEntry e = _commandPaletteCatalog[catalogIdx];
                string title = VPBTranslation.T(e.TitleKey, e.TitleDefault);
                if (!string.IsNullOrEmpty(e.Hint))
                    title = title + "  (" + e.Hint + ")";

                int captured = catalogIdx;
                bool selected = r == _commandPaletteSelected;
                GameObject row = UI.CreateUIButton(
                    _commandPaletteListRT.gameObject,
                    0, 34,
                    title,
                    GalleryUiDesignTokens.FontBodyRef,
                    0, 0,
                    AnchorPresets.hStretchTop,
                    () => RunCommandPaletteEntry(captured));
                row.name = "CmdRow_" + e.Id;
                Image img = row.GetComponent<Image>();
                if (img != null)
                    img.color = selected
                        ? new Color(0.25f, 0.40f, 0.55f, 0.95f)
                        : new Color(0.14f, 0.16f, 0.18f, 0.92f);
                var le = row.GetComponent<LayoutElement>();
                if (le == null) le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 34f;
                le.flexibleWidth = 1f;
                _commandPaletteRowGOs.Add(row);
            }
        }

        private void RunCommandPaletteEntry(int catalogIdx)
        {
            if (catalogIdx < 0 || catalogIdx >= _commandPaletteCatalog.Count) return;
            CommandPaletteEntry e = _commandPaletteCatalog[catalogIdx];
            CloseCommandPalette();
            try
            {
                if (e.Run != null) e.Run();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] Command palette run failed (" + e.Id + "): " + ex.Message);
                try
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.cmd.run_failed", "Command failed. See log."),
                        2f);
                }
                catch { }
            }
        }

        private bool TryHandleCommandPaletteKeyboard()
        {
            if (!_commandPaletteOpen) return false;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseCommandPalette();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (_commandPaletteFiltered.Count > 0)
                {
                    _commandPaletteSelected--;
                    if (_commandPaletteSelected < 0)
                        _commandPaletteSelected = _commandPaletteFiltered.Count - 1;
                    RebuildCommandPaletteRowsForced();
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (_commandPaletteFiltered.Count > 0)
                {
                    _commandPaletteSelected++;
                    if (_commandPaletteSelected >= _commandPaletteFiltered.Count)
                        _commandPaletteSelected = 0;
                    RebuildCommandPaletteRowsForced();
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_commandPaletteFiltered.Count > 0
                    && _commandPaletteSelected >= 0
                    && _commandPaletteSelected < _commandPaletteFiltered.Count)
                {
                    RunCommandPaletteEntry(_commandPaletteFiltered[_commandPaletteSelected]);
                }
                return true;
            }

            return false;
        }

        private void RebuildCommandPaletteRowsForced()
        {
            string keep = _commandPaletteLastFilter;
            _commandPaletteLastFilter = "\0";
            RebuildCommandPaletteRows(keep);
        }

        /// <summary>Apply current grid selection via keyboard (Enter/Space). Warm path.</summary>
        private void TryKeyboardApplySelection()
        {
            if (!IsVisible) return;
            if (_benchPickModeActive || _stripKeepSubScenePickActive) return;
            if (_removeModeActive) return;
            if (IsSettingsPanelOpen() || settingsListViewActive) return;
            if (_commandPaletteOpen) return;

            FileEntry file = null;
            if (selectedFiles != null && selectedFiles.Count > 0)
                file = selectedFiles[selectedFiles.Count - 1];
            if (file == null && currentFilteredFiles != null && currentFilteredFiles.Count > 0)
            {
                // No selection — apply first visible only if user already navigated (hover path set).
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    for (int i = 0; i < currentFilteredFiles.Count; i++)
                    {
                        FileEntry f = currentFilteredFiles[i];
                        if (f == null) continue;
                        if (string.Equals(f.Path, selectedPath, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(f.Uid, selectedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            file = f;
                            break;
                        }
                    }
                }
            }

            if (file == null)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.keyboard.apply_none", "Nothing selected to apply."),
                    1.5f);
                return;
            }

            // Verb clarity: Try-On intercept uses same entry; status after apply is owned by Try-On/Import.
            if (TryOnIsEnabled() && TryOnClassify(file) != TryOnKind.None)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.keyboard.try_hint", "Try-On: previewing (Keep / Revert / Esc)."),
                    1.5f);
            }

            ApplyFileEntryNow(file);
        }

        private void FocusTitleSearchInput()
        {
            try { FocusTitleSearchFromHotkey(); } catch { }
        }
    }
}
