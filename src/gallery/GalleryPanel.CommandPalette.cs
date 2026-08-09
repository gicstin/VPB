using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Command palette (Ctrl+Shift+P). Cold path overlay — built on open, destroyed on close.
    /// Recognition-assisted recall: groups, aliases, availability, recent, category type-ahead.
    /// </summary>
    public partial class GalleryPanel
    {
        private const int CommandPaletteRecentMax = 8;

        private enum CmdPaletteRowKind
        {
            Header = 0,
            Command = 1,
            Category = 2
        }

        private struct CommandPaletteEntry
        {
            public string Id;
            public string TitleKey;
            public string TitleDefault;
            public string Hint;
            public string Group;
            public string Aliases;
            public Action Run;
            public Func<bool> CanRun;
        }

        private struct CmdPaletteFilterRow
        {
            public CmdPaletteRowKind Kind;
            public int CatalogIdx;
            public string HeaderText;
            public string CatName;
            public string CatExt;
            public string CatPath;
            public string Title;
            public string Hint;
            public bool Enabled;
        }

        private GameObject _commandPaletteOverlayGO;
        private GameObject _commandPalettePanelGO;
        private InputField _commandPaletteInput;
        private RectTransform _commandPaletteListRT;
        private RectTransform _commandPaletteViewportRT;
        private Scrollbar _commandPaletteScrollbar;
        private readonly List<CommandPaletteEntry> _commandPaletteCatalog = new List<CommandPaletteEntry>(80);
        private readonly List<CmdPaletteFilterRow> _commandPaletteFiltered = new List<CmdPaletteFilterRow>(80);
        private readonly List<GameObject> _commandPaletteRowGOs = new List<GameObject>(80);
        private readonly List<string> _commandPaletteRecent = new List<string>(CommandPaletteRecentMax);
        private readonly StringBuilder _commandPaletteHaySb = new StringBuilder(128);
        private int _commandPaletteSelected;
        private bool _commandPaletteOpen;
        private string _commandPaletteLastFilter = "";
        private float _commandPaletteBuiltChromeScale;
        private float _commandPaletteRowSpacing;
        /// <summary>Pixels content scrolled down from top (content pivot top).</summary>
        private float _commandPaletteScrollY;
        private bool _commandPaletteScrollbarSync;

        private bool IsCommandPaletteOpen { get { return _commandPaletteOpen; } }

        private bool CmdPaletteHasSelection()
        {
            return selectedFiles != null && selectedFiles.Count > 0;
        }

        private bool CmdPaletteHasUndo()
        {
            return undoStack != null && undoStack.Count > 0;
        }

        private bool CmdPaletteHasRedo()
        {
            return redoStack != null && redoStack.Count > 0;
        }

        private bool CmdPaletteIsHistoryBrowse()
        {
            return activeContentType == ContentType.History;
        }

        private bool CmdPaletteTryOnSessionActive()
        {
            return _tryOnActive;
        }

        private void AddCommandPaletteEntry(
            string id,
            string titleKey,
            string titleDefault,
            string hint,
            string group,
            string aliases,
            Action run,
            Func<bool> canRun = null)
        {
            _commandPaletteCatalog.Add(new CommandPaletteEntry
            {
                Id = id,
                TitleKey = titleKey,
                TitleDefault = titleDefault,
                Hint = hint ?? "",
                Group = group ?? "",
                Aliases = aliases ?? "",
                Run = run,
                CanRun = canRun
            });
        }

        private void EnsureCommandPaletteCatalog()
        {
            if (_commandPaletteCatalog.Count > 0) return;

            const string GEdit = "Edit";
            const string GBrowse = "Browse";
            const string GModes = "Modes";
            const string GSel = "Selection";
            const string GPkg = "Packages";
            const string GView = "View";
            const string GTools = "Tools";
            const string GHelp = "Help";

            // --- Edit ---
            AddCommandPaletteEntry("undo", "gallery.cmd.undo", "Undo", "Ctrl+Z", GEdit, "revert back",
                () => { try { Undo(); } catch { } },
                CmdPaletteHasUndo);
            AddCommandPaletteEntry("redo", "gallery.cmd.redo", "Redo", "Ctrl+Y", GEdit, "forward",
                () => { try { Redo(); } catch { } },
                CmdPaletteHasRedo);

            // --- Browse ---
            AddCommandPaletteEntry("search", "gallery.cmd.focus_search", "Focus search", "Ctrl+F", GBrowse, "find filter title query",
                () => { try { FocusTitleSearchInput(); } catch { } });
            AddCommandPaletteEntry("clear_filters", "gallery.cmd.clear_filters", "Clear browse filters", "", GBrowse, "reset chips qf tags",
                () =>
                {
                    try { ClearAllBrowseFiltersKeepCategory(); }
                    catch { try { RefreshFiles(true); } catch { } }
                });
            AddCommandPaletteEntry("filter_presets", "gallery.cmd.filter_presets", "Filter presets", "Alt+F", GBrowse, "quick filters qf saved",
                () =>
                {
                    try
                    {
                        if (quickFiltersUI == null) return;
                        quickFiltersUI.SetVisible(!quickFiltersUI.IsVisible);
                    }
                    catch { }
                });
            AddCommandPaletteEntry("category_menu", "gallery.cmd.category_menu", "Category quick menu", "0–9", GBrowse, "switch page categories jump",
                () => { try { SetCategoryQuickMenuVisible(true); } catch { } });
            AddCommandPaletteEntry("refresh", "gallery.cmd.refresh", "Refresh browse", "", GBrowse, "reload files scan",
                () => { try { RefreshFiles(true); } catch { } });
            AddCommandPaletteEntry("refresh_history", "gallery.cmd.refresh_history", "Refresh History", "Ctrl+R", GBrowse, "usage recent",
                () => { try { RefreshHistoryBrowsePreferLight(true); } catch { } },
                CmdPaletteIsHistoryBrowse);
            AddCommandPaletteEntry("load_random", "gallery.cmd.load_random", "Load random (filtered view)", "", GBrowse, "dice chance pick",
                () => { try { LoadRandom(); } catch { } });

            // --- Modes ---
            AddCommandPaletteEntry("scene_tools", "gallery.cmd.scene_tools", "Toggle Scene Tools", "Ctrl+Shift+K", GModes, "creator strip keep atoms",
                () => { try { ToggleCreatorMode(); } catch { } });
            AddCommandPaletteEntry("scene_eraser", "gallery.cmd.scene_eraser", "Toggle Scene Eraser", "Ctrl+Shift+E", GModes, "remove delete atoms erase",
                () => { try { ToggleRemoveMode(false, false); } catch { } });
            AddCommandPaletteEntry("import", "gallery.cmd.import", "Toggle Import sidebar", "Alt+I", GModes, "scene import atoms",
                () => { try { ToggleImportSidebar(); } catch { } });
            AddCommandPaletteEntry("cleanup", "gallery.cmd.cleanup", "Open Cleanup", "", GModes, "orphans unused",
                () => { try { TboxOpenCleanupView(); } catch { } });
            AddCommandPaletteEntry("exit_cleanup", "gallery.cmd.exit_cleanup", "Exit Cleanup", "Esc", GModes, "leave cleanup mode",
                () => { try { ExitCleanupModeForSidePanelNavigation(); } catch { } },
                () => cleanupModeActive);
            AddCommandPaletteEntry("tryon_toggle", "gallery.cmd.tryon_toggle", "Toggle Try-On mode (settings)", "", GModes, "preview keep revert",
                () => { try { ToggleTryOnModeSettingFromPalette(); } catch { } });
            AddCommandPaletteEntry("tryon_keep", "gallery.cmd.tryon_keep", "Try-On: Keep", "", GModes, "commit preview",
                () => { try { TryOnKeep(); } catch { } },
                CmdPaletteTryOnSessionActive);
            AddCommandPaletteEntry("tryon_revert", "gallery.cmd.tryon_revert", "Try-On: Revert", "Esc", GModes, "cancel preview undo try",
                () => { try { TryOnRevert(); } catch { } },
                CmdPaletteTryOnSessionActive);
            AddCommandPaletteEntry("apply_mode", "gallery.cmd.apply_mode", "Toggle 1-Click / 2-Click apply", "", GModes, "click commit",
                () => { try { ToggleApplyMode(); } catch { } });
            AddCommandPaletteEntry("hold_launch", "gallery.cmd.hold_launch", "Toggle Hold-to-launch", "", GModes, "press hold",
                () => { try { ToggleHoldToLaunch(); } catch { } });
            AddCommandPaletteEntry("strip", "gallery.cmd.strip_scene", "Strip Scene window", "Ctrl+Shift+S", GModes, "keep recipe",
                () => { try { HotkeyOpenStripSceneDirect(); } catch { } });

            // --- Selection ---
            AddCommandPaletteEntry("apply", "gallery.cmd.apply", "Apply selection", "Enter", GSel, "load wear use",
                () => { try { TryKeyboardApplySelection(); } catch { } },
                () => CmdPaletteHasSelection() || !string.IsNullOrEmpty(selectedPath));
            AddCommandPaletteEntry("clear_selection", "gallery.cmd.clear_selection", "Clear selection", "Esc", GSel, "deselect none",
                () => { try { ClearSelection(); } catch { } },
                CmdPaletteHasSelection);
            AddCommandPaletteEntry("select_all", "gallery.cmd.select_all", "Select all visible", "Ctrl+A", GSel, "everything multi",
                () => { try { SelectAll(); } catch { } });
            AddCommandPaletteEntry("remove_history", "gallery.cmd.remove_history", "Remove from History", "Delete", GSel, "forget usage",
                () => { try { TboxRemoveSelectedFromHistory(); } catch { } },
                () => CmdPaletteIsHistoryBrowse() && CmdPaletteHasSelection());

            // --- Packages ---
            AddCommandPaletteEntry("delete_packages", "gallery.cmd.delete_packages", "Delete selected packages/scenes", "Delete", GPkg, "disk remove trash",
                () => { try { TboxDeleteSelectedPackages(); } catch { } },
                CmdPaletteHasSelection);
            AddCommandPaletteEntry("hide_packages", "gallery.cmd.hide_packages", "Hide selected packages", "", GPkg, "conceal",
                () => { try { TboxHideSelectedPackages(); } catch { } },
                CmdPaletteHasSelection);
            AddCommandPaletteEntry("unhide_packages", "gallery.cmd.unhide_packages", "Unhide selected packages", "", GPkg, "show reveal",
                () => { try { TboxUnhideSelectedPackages(); } catch { } },
                CmdPaletteHasSelection);
            AddCommandPaletteEntry("autoinstall", "gallery.cmd.autoinstall", "Enable auto-install / auto-load", "", GPkg, "whitelist scan",
                () => { try { TboxAutoInstallSelectedPackages(); } catch { } },
                CmdPaletteHasSelection);
            AddCommandPaletteEntry("clear_autoinstall", "gallery.cmd.clear_autoinstall", "Clear auto-install / auto-load", "", GPkg, "disable flags",
                () => { try { TboxDisableAutoInstallSelectedPackages(); } catch { } },
                CmdPaletteHasSelection);
            AddCommandPaletteEntry("open_hub_item", "gallery.cmd.open_hub_item", "Open selection on Hub", "", GPkg, "detail download",
                () =>
                {
                    try
                    {
                        FileEntry f = null;
                        if (selectedFiles != null && selectedFiles.Count > 0)
                            f = selectedFiles[selectedFiles.Count - 1];
                        OpenFileOnHub(f);
                    }
                    catch { }
                },
                CmdPaletteHasSelection);
            AddCommandPaletteEntry("open_hub", "gallery.cmd.open_hub", "Open Hub browse", "", GPkg, "store packages",
                () => { try { VamHookPlugin.singleton?.OpenHubBrowse(); } catch { } });

            // --- View ---
            AddCommandPaletteEntry("layout", "gallery.cmd.layout", "Toggle Grid / List layout", "", GView, "rows columns",
                () => { try { ToggleLayoutMode(); } catch { } });
            AddCommandPaletteEntry("ui_scale_up", "gallery.cmd.ui_scale_up", "UI scale up", "Ctrl+Alt+=", GView, "chrome bigger zoom",
                () => { try { NudgeGalleryUiScaleFromPalette(0.1f); } catch { } });
            AddCommandPaletteEntry("ui_scale_down", "gallery.cmd.ui_scale_down", "UI scale down", "Ctrl+Alt+-", GView, "chrome smaller",
                () => { try { NudgeGalleryUiScaleFromPalette(-0.1f); } catch { } });
            AddCommandPaletteEntry("grid_cols_more", "gallery.cmd.grid_cols_more", "More grid columns", "Ctrl+wheel", GView, "zoom out denser",
                () => { try { AdjustGridColumns(1); } catch { } });
            AddCommandPaletteEntry("grid_cols_fewer", "gallery.cmd.grid_cols_fewer", "Fewer grid columns", "Ctrl+wheel", GView, "zoom in larger thumbs",
                () => { try { AdjustGridColumns(-1); } catch { } });
            AddCommandPaletteEntry("settings", "gallery.cmd.settings", "Open Settings", "", GView, "prefs options config",
                () => { try { OpenSettingsSideTab(); } catch { } });
            AddCommandPaletteEntry("settings_interaction", "gallery.cmd.settings_interaction", "Settings → Interaction / Hotkeys", "", GView, "try-on apply hold bindings keys chords",
                () => { try { OpenSettingsGroup("interaction"); } catch { } });
            AddCommandPaletteEntry("settings_perf", "gallery.cmd.settings_perf", "Settings → Performance", "", GView, "fps cache",
                () => { try { OpenSettingsGroup("performance"); } catch { } });
            AddCommandPaletteEntry("settings_browsing", "gallery.cmd.settings_browsing", "Settings → Browsing", "", GView, "lists tags search",
                () => { try { OpenSettingsGroup("browsing"); } catch { } });

            // --- Tools ---
            AddCommandPaletteEntry("exit_scene_tools", "gallery.cmd.exit_scene_tools", "Exit Scene Tools", "Esc", GTools, "leave creator",
                () => { try { ExitCreatorMode(force: true); } catch { } },
                () => creatorModeActive || creatorModeStripBusy);
            AddCommandPaletteEntry("exit_eraser", "gallery.cmd.exit_eraser", "Exit Scene Eraser", "Esc", GTools, "leave remove mode",
                () => { try { if (_removeModeActive) ToggleRemoveMode(false, false); } catch { } },
                () => _removeModeActive);

            // --- Help ---
            AddCommandPaletteEntry("help", "gallery.cmd.help", "Open help (Hotkeys)", "F1 / ?", GHelp, "shortcuts chords",
                () => { try { OpenInAppHelpToSection("hotkeys"); } catch { try { ToggleInAppHelpPanel(); } catch { } } });
            AddCommandPaletteEntry("help_filtering", "gallery.cmd.help_filtering", "Help: Filtering", "", GHelp, "search chips",
                () => { try { OpenInAppHelpToSection("filtering"); } catch { } });
            AddCommandPaletteEntry("help_import", "gallery.cmd.help_import", "Help: Import", "", GHelp, "sidebar atoms",
                () => { try { OpenInAppHelpToSection("import"); } catch { } });
            AddCommandPaletteEntry("help_cleanup", "gallery.cmd.help_cleanup", "Help: Cleanup", "", GHelp, "orphans",
                () => { try { OpenInAppHelpToSection("cleanup"); } catch { } });
            AddCommandPaletteEntry("help_selection", "gallery.cmd.help_selection", "Help: Selection", "", GHelp, "multi apply",
                () => { try { OpenInAppHelpToSection("selection"); } catch { } });
            AddCommandPaletteEntry("help_advanced", "gallery.cmd.help_advanced", "Help: Advanced", "", GHelp, "power",
                () => { try { OpenInAppHelpToSection("advanced"); } catch { } });
            AddCommandPaletteEntry("palette_hint", "gallery.cmd.palette_hint", "Command palette (this)", "Ctrl+Shift+P", GHelp, "commands fuzzy",
                () =>
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.cmd.palette_toast", "Command palette — type to filter; ↑↓ Enter; Esc closes."),
                        2.5f);
                });
        }

        private void NudgeGalleryUiScaleFromPalette(float delta)
        {
            var cfg = VPBConfig.Instance;
            if (cfg == null) return;
            float before = cfg.InnerPaneScale;
            float after = Mathf.Clamp(Mathf.Round((before + delta) * 10f) / 10f, VPBConfig.MinUiScale, VPBConfig.MaxUiScale);
            if (!Mathf.Approximately(before, after))
            {
                cfg.InnerPaneScale = after;
                try { cfg.TriggerChange(); } catch { }
                try { ScheduleQuickFiltersConfigSave(); } catch { }
            }
            ShowTemporaryStatus(string.Format(
                VPBTranslation.T("gallery.status.ui_scale", "UI scale: {0:0.0}"),
                cfg.InnerPaneScale), 1.25f);
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

        private void RememberCommandPaletteRecent(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            for (int i = _commandPaletteRecent.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_commandPaletteRecent[i], id, StringComparison.Ordinal))
                    _commandPaletteRecent.RemoveAt(i);
            }
            _commandPaletteRecent.Insert(0, id);
            while (_commandPaletteRecent.Count > CommandPaletteRecentMax)
                _commandPaletteRecent.RemoveAt(_commandPaletteRecent.Count - 1);
        }

        private int FindCommandPaletteCatalogIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _commandPaletteCatalog.Count; i++)
            {
                if (string.Equals(_commandPaletteCatalog[i].Id, id, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        private bool CommandPaletteEntryAvailable(CommandPaletteEntry e)
        {
            if (e.CanRun == null) return true;
            try { return e.CanRun(); }
            catch { return false; }
        }

        private static bool CommandPaletteTokensMatch(string hayLower, string filterLower)
        {
            if (string.IsNullOrEmpty(filterLower)) return true;
            if (string.IsNullOrEmpty(hayLower)) return false;
            int start = 0;
            int n = filterLower.Length;
            while (start < n)
            {
                while (start < n && filterLower[start] == ' ') start++;
                if (start >= n) return true;
                int end = start;
                while (end < n && filterLower[end] != ' ') end++;
                // Substring without alloc when possible — net35: Substring allocates; keep small.
                string tok = filterLower.Substring(start, end - start);
                if (hayLower.IndexOf(tok, StringComparison.Ordinal) < 0) return false;
                start = end;
            }
            return true;
        }

        private string BuildCommandPaletteHaystack(CommandPaletteEntry e, string title)
        {
            _commandPaletteHaySb.Length = 0;
            _commandPaletteHaySb.Append(title);
            _commandPaletteHaySb.Append(' ');
            _commandPaletteHaySb.Append(e.Hint);
            _commandPaletteHaySb.Append(' ');
            _commandPaletteHaySb.Append(e.Id);
            _commandPaletteHaySb.Append(' ');
            _commandPaletteHaySb.Append(e.Aliases);
            _commandPaletteHaySb.Append(' ');
            _commandPaletteHaySb.Append(e.Group);
            return _commandPaletteHaySb.ToString().ToLowerInvariant();
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

            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            GalleryModalTypography type = new GalleryModalTypography(s);
            _commandPaletteBuiltChromeScale = s;
            _commandPaletteRowSpacing = 4f * s;

            float panelW = 560f * s;
            float panelH = 680f * s;
            float closeSz = 32f * s;

            GameObject panelGO;
            GameObject overlayGO = UI.CreateModalChrome(
                backgroundBoxGO, "CommandPaletteOverlay", panelW, panelH, UI.ChromeDarker, CloseCommandPalette, out panelGO, dimAlpha: 0.45f);
            _commandPaletteOverlayGO = overlayGO;
            _commandPalettePanelGO = panelGO;
            _commandPaletteOpen = true;
            _commandPaletteSelected = 0;
            _commandPaletteLastFilter = "";

            // Click-blocker on panel so dim dismiss does not fire through empty panel chrome.
            Image panelBlock = panelGO.GetComponent<Image>();
            if (panelBlock != null) panelBlock.raycastTarget = true;

            Text titleLbl = UI.CreateLabel(
                panelGO,
                VPBTranslation.T("gallery.cmd.title", "Commands"),
                type.Title,
                Color.white,
                TextAnchor.MiddleLeft,
                anchorPreset: AnchorPresets.hStretchTop,
                size: new Vector2(0, 36f * s),
                anchoredPosition: new Vector2(16f * s, -8f * s),
                name: "CmdTitle");
            GalleryUiMetrics.ApplyEmphasisTitle(titleLbl, type.Title);
            RectTransform titleRT = titleLbl != null ? titleLbl.GetComponent<RectTransform>() : null;
            if (titleRT != null)
            {
                titleRT.offsetMin = new Vector2(16f * s, titleRT.offsetMin.y);
                titleRT.offsetMax = new Vector2(-(closeSz + 20f * s), titleRT.offsetMax.y);
            }

            GameObject closeBtn = UI.CreateUIButton(
                panelGO, closeSz, closeSz, "", type.Body, 0, 0, AnchorPresets.topRight, CloseCommandPalette);
            closeBtn.name = "CmdClose";
            RectTransform closeRT = closeBtn.GetComponent<RectTransform>();
            if (closeRT != null)
            {
                closeRT.anchoredPosition = new Vector2(-10f * s, -10f * s);
                closeRT.sizeDelta = new Vector2(closeSz, closeSz);
            }
            Image closeImg = closeBtn.GetComponent<Image>();
            if (closeImg != null) closeImg.color = GalleryUiColorTokens.ChromeIconWell;
            try
            {
                Sprite xSpr = UI.LoadIconSprite("vpb_icons/x.png", UI.BarIconGlyphTint);
                if (xSpr != null) UI.AddIconToButton(closeBtn, xSpr, padding: 6f * s);
                else
                {
                    Text xt = closeBtn.GetComponentInChildren<Text>(true);
                    if (xt != null) xt.text = "X";
                }
            }
            catch
            {
                Text xt = closeBtn.GetComponentInChildren<Text>(true);
                if (xt != null) xt.text = "X";
            }
            UI.EnsureFloatChromeHoverBorder(closeBtn, inward: true);
            AddTooltip(closeBtn, "gallery.cmd.close", "Close (Esc)");

            GameObject searchRow = UI.CreateChildRT(panelGO, "CmdSearch", AnchorPresets.hStretchTop, new Vector2(0, 36f * s));
            RectTransform searchRT = searchRow.GetComponent<RectTransform>();
            searchRT.offsetMin = new Vector2(16f * s, -84f * s);
            searchRT.offsetMax = new Vector2(-16f * s, -48f * s);

            _commandPaletteInput = CreateSearchInput(
                searchRow,
                520f * s,
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
                try
                {
                    if (_commandPaletteInput.placeholder is Text ph)
                        ph.text = VPBTranslation.T("gallery.cmd.placeholder", "Type a command or category…");
                }
                catch { }
                try { RescaleSearchInput(_commandPaletteInput, s); } catch { }
            }

            float sbW = 12f * s;
            GameObject scrollGO = BuildCommandPaletteScrollHost(panelGO, sbW, s);
            RectTransform scrollRT = scrollGO != null ? scrollGO.GetComponent<RectTransform>() : null;
            if (scrollRT != null)
            {
                scrollRT.anchorMin = Vector2.zero;
                scrollRT.anchorMax = Vector2.one;
                scrollRT.offsetMin = new Vector2(12f * s, 12f * s);
                scrollRT.offsetMax = new Vector2(-12f * s, -96f * s);
            }

            RebuildCommandPaletteRows("");
            SetLayerRecursive(overlayGO, backgroundBoxGO.layer);

            if (_commandPaletteInput != null)
            {
                _commandPaletteInput.ActivateInputField();
                _commandPaletteInput.Select();
            }
        }

        /// <summary>
        /// Manual pixel scroller — no Unity ScrollRect (its normalized 0↔1 + Sync fights short lists).
        /// </summary>
        private GameObject BuildCommandPaletteScrollHost(GameObject panelGO, float sbW, float s)
        {
            GameObject host = UI.CreateChildRT(panelGO, "CmdScroll", AnchorPresets.stretchAll);
            Image hostBg = UI.AddImage(host, new Color(0.10f, 0.11f, 0.13f, 1f));
            if (hostBg != null) hostBg.raycastTarget = true;

            GameObject viewport = UI.CreateChildRT(host, "Viewport", AnchorPresets.stretchAll);
            RectTransform vpRT = viewport.GetComponent<RectTransform>();
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = new Vector2(-sbW, 0f);
            viewport.AddComponent<RectMask2D>();
            Image vpImg = UI.AddImage(viewport, new Color(0f, 0f, 0f, 0.001f));
            if (vpImg != null) vpImg.raycastTarget = true;
            _commandPaletteViewportRT = vpRT;

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup vlg = UI.AddVLG(content, spacing: _commandPaletteRowSpacing, padding: UI.Pad(4, 4, 4, 4, s));
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            _commandPaletteListRT = contentRT;
            _commandPaletteScrollY = 0f;

            GameObject sbGo = UI.CreateScrollBar(host, sbW, 0f, Scrollbar.Direction.BottomToTop);
            _commandPaletteScrollbar = sbGo != null ? sbGo.GetComponent<Scrollbar>() : null;
            if (_commandPaletteScrollbar != null)
            {
                BoxCollider col = _commandPaletteScrollbar.GetComponent<BoxCollider>();
                if (col != null)
                {
                    try { Destroy(col); } catch { }
                }
                _commandPaletteScrollbar.onValueChanged.RemoveAllListeners();
                _commandPaletteScrollbar.onValueChanged.AddListener(OnCommandPaletteScrollbarChanged);
                _commandPaletteScrollbar.size = 1f;
                _commandPaletteScrollbar.value = 1f;
            }

            AttachCommandPaletteWheel(host, s);
            AttachCommandPaletteWheel(viewport, s);
            return host;
        }

        private void AttachCommandPaletteWheel(GameObject go, float s)
        {
            if (go == null) return;
            UIScrollWheelHandler wheel = go.GetComponent<UIScrollWheelHandler>();
            if (wheel == null) wheel = go.AddComponent<UIScrollWheelHandler>();
            wheel.Sensitivity = 1f;
            float step = 36f * (s > 0.01f ? s : 1f);
            wheel.OnScrollValue = dy => CommandPaletteWheelStep(dy, step);
        }

        private void CommandPaletteWheelStep(float scrollDeltaY, float stepPx)
        {
            if (Mathf.Abs(scrollDeltaY) < 0.01f) return;
            // Normalize to one notch — some drivers send ±120 and would leap the whole list.
            float dir = scrollDeltaY > 0f ? 1f : -1f;
            CommandPaletteSetScrollY(_commandPaletteScrollY - dir * stepPx);
        }

        private void OnCommandPaletteScrollbarChanged(float value)
        {
            if (_commandPaletteScrollbarSync) return;
            float scrollable = CommandPaletteScrollablePx();
            if (scrollable <= 1f)
            {
                CommandPaletteSetScrollY(0f);
                return;
            }
            // BottomToTop: value 1 = top, 0 = bottom.
            CommandPaletteSetScrollY((1f - Mathf.Clamp01(value)) * scrollable, fromScrollbar: true);
        }

        private float CommandPaletteScrollablePx()
        {
            if (_commandPaletteListRT == null || _commandPaletteViewportRT == null) return 0f;
            float contentH = _commandPaletteListRT.rect.height;
            float viewH = _commandPaletteViewportRT.rect.height;
            float scrollable = contentH - viewH;
            return scrollable > 1f ? scrollable : 0f;
        }

        private void CommandPaletteSetScrollY(float y, bool fromScrollbar = false)
        {
            if (_commandPaletteListRT == null) return;
            float scrollable = CommandPaletteScrollablePx();
            float clamped = scrollable <= 1f ? 0f : Mathf.Clamp(y, 0f, scrollable);
            _commandPaletteScrollY = clamped;
            Vector2 ap = _commandPaletteListRT.anchoredPosition;
            ap.y = clamped;
            _commandPaletteListRT.anchoredPosition = ap;

            if (!fromScrollbar && _commandPaletteScrollbar != null)
            {
                _commandPaletteScrollbarSync = true;
                try
                {
                    float handle = 1f;
                    if (_commandPaletteViewportRT != null)
                    {
                        float contentH = _commandPaletteListRT.rect.height;
                        float viewH = _commandPaletteViewportRT.rect.height;
                        if (contentH > viewH + 1f)
                            handle = Mathf.Clamp01(viewH / contentH);
                    }
                    _commandPaletteScrollbar.size = Mathf.Max(handle, 0.08f);
                    _commandPaletteScrollbar.value = scrollable <= 1f ? 1f : (1f - clamped / scrollable);
                }
                finally
                {
                    _commandPaletteScrollbarSync = false;
                }
            }
        }

        /// <summary>Set content height from row LayoutElements — no ContentSizeFitter.</summary>
        private void FinalizeCommandPaletteContentHeight()
        {
            if (_commandPaletteListRT == null) return;

            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            float spacing = _commandPaletteRowSpacing > 0f ? _commandPaletteRowSpacing : 4f * s;
            float pad = 8f * s;
            VerticalLayoutGroup vlg = _commandPaletteListRT.GetComponent<VerticalLayoutGroup>();
            if (vlg != null && vlg.padding != null)
                pad = vlg.padding.top + vlg.padding.bottom;

            float h = pad;
            int n = _commandPaletteRowGOs.Count;
            for (int i = 0; i < n; i++)
            {
                GameObject go = _commandPaletteRowGOs[i];
                float rh = 34f * s;
                if (go != null)
                {
                    LayoutElement le = go.GetComponent<LayoutElement>();
                    if (le != null && le.preferredHeight > 1f)
                        rh = le.preferredHeight;
                }
                h += rh;
                if (i < n - 1) h += spacing;
            }
            if (h < 1f) h = 1f;
            _commandPaletteListRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            // Keep current scroll clamped to new range.
            CommandPaletteSetScrollY(_commandPaletteScrollY);
        }

        private void CloseCommandPalette()
        {
            _commandPaletteOpen = false;
            _commandPaletteBuiltChromeScale = 0f;
            _commandPaletteScrollY = 0f;
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
            _commandPaletteViewportRT = null;
            _commandPaletteScrollbar = null;
        }

        /// <summary>Live ChromeScale adapt — rebuild overlay, keep filter text + selection index.</summary>
        private void RescaleCommandPaletteIfOpen()
        {
            if (!_commandPaletteOpen) return;
            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            if (_commandPaletteBuiltChromeScale > 0f
                && Mathf.Abs(s - _commandPaletteBuiltChromeScale) < 0.0005f)
                return;

            string keepFilter = "";
            if (_commandPaletteInput != null && _commandPaletteInput.text != null)
                keepFilter = _commandPaletteInput.text;
            else if (_commandPaletteLastFilter != null)
                keepFilter = _commandPaletteLastFilter;
            int keepSel = _commandPaletteSelected;

            OpenCommandPalette();
            if (_commandPaletteInput != null && keepFilter.Length > 0)
                _commandPaletteInput.text = keepFilter;
            if (_commandPaletteFiltered.Count > 0)
                _commandPaletteSelected = Mathf.Clamp(keepSel, 0, _commandPaletteFiltered.Count - 1);
            RebuildCommandPaletteRowsForced();
        }

        private void OnCommandPaletteFilterChanged(string raw)
        {
            RebuildCommandPaletteRows(raw ?? "");
        }

        private void AddCommandPaletteFilterCommand(int catalogIdx, bool enabled)
        {
            CommandPaletteEntry e = _commandPaletteCatalog[catalogIdx];
            string title = VPBTranslation.T(e.TitleKey, e.TitleDefault);
            _commandPaletteFiltered.Add(new CmdPaletteFilterRow
            {
                Kind = CmdPaletteRowKind.Command,
                CatalogIdx = catalogIdx,
                Title = title,
                Hint = e.Hint,
                Enabled = enabled
            });
        }

        private void RebuildCommandPaletteRows(string raw, bool scrollSelectionIntoView = false)
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
            bool browsing = filterLower == null;

            if (browsing)
            {
                // Recent first
                bool anyRecent = false;
                for (int r = 0; r < _commandPaletteRecent.Count; r++)
                {
                    int idx = FindCommandPaletteCatalogIndex(_commandPaletteRecent[r]);
                    if (idx < 0) continue;
                    if (!anyRecent)
                    {
                        _commandPaletteFiltered.Add(new CmdPaletteFilterRow
                        {
                            Kind = CmdPaletteRowKind.Header,
                            HeaderText = VPBTranslation.T("gallery.cmd.group.recent", "Recent"),
                            Enabled = false
                        });
                        anyRecent = true;
                    }
                    AddCommandPaletteFilterCommand(idx, CommandPaletteEntryAvailable(_commandPaletteCatalog[idx]));
                }

                string lastGroup = null;
                var seenRecent = new HashSet<string>(StringComparer.Ordinal);
                for (int r = 0; r < _commandPaletteRecent.Count; r++)
                    seenRecent.Add(_commandPaletteRecent[r]);

                for (int i = 0; i < _commandPaletteCatalog.Count; i++)
                {
                    CommandPaletteEntry e = _commandPaletteCatalog[i];
                    if (seenRecent.Contains(e.Id)) continue;
                    if (!string.Equals(e.Group, lastGroup, StringComparison.Ordinal))
                    {
                        lastGroup = e.Group;
                        string gLabel = string.IsNullOrEmpty(e.Group)
                            ? VPBTranslation.T("gallery.cmd.group.other", "Other")
                            : e.Group;
                        _commandPaletteFiltered.Add(new CmdPaletteFilterRow
                        {
                            Kind = CmdPaletteRowKind.Header,
                            HeaderText = gLabel,
                            Enabled = false
                        });
                    }
                    AddCommandPaletteFilterCommand(i, CommandPaletteEntryAvailable(e));
                }
            }
            else
            {
                for (int i = 0; i < _commandPaletteCatalog.Count; i++)
                {
                    CommandPaletteEntry e = _commandPaletteCatalog[i];
                    string title = VPBTranslation.T(e.TitleKey, e.TitleDefault);
                    string hay = BuildCommandPaletteHaystack(e, title);
                    if (!CommandPaletteTokensMatch(hay, filterLower)) continue;
                    AddCommandPaletteFilterCommand(i, CommandPaletteEntryAvailable(e));
                }

                // Category type-ahead (recognition jump without memorizing 0–9 order)
                try
                {
                    List<Gallery.Category> cats = BuildOrderedCategoriesForQuickSwitch();
                    if (cats != null)
                    {
                        bool catHeader = false;
                        for (int c = 0; c < cats.Count; c++)
                        {
                            Gallery.Category cat = cats[c];
                            string name = cat.name ?? "";
                            if (name.Length == 0) continue;
                            string hay = (name + " " + (cat.extension ?? "") + " category page").ToLowerInvariant();
                            if (!CommandPaletteTokensMatch(hay, filterLower)) continue;
                            if (!catHeader)
                            {
                                _commandPaletteFiltered.Add(new CmdPaletteFilterRow
                                {
                                    Kind = CmdPaletteRowKind.Header,
                                    HeaderText = VPBTranslation.T("gallery.cmd.group.categories", "Categories"),
                                    Enabled = false
                                });
                                catHeader = true;
                            }
                            _commandPaletteFiltered.Add(new CmdPaletteFilterRow
                            {
                                Kind = CmdPaletteRowKind.Category,
                                CatName = name,
                                CatExt = cat.extension ?? "",
                                CatPath = cat.path ?? "",
                                Title = name,
                                Hint = VPBTranslation.T("gallery.cmd.category_hint", "Category"),
                                Enabled = true
                            });
                        }
                    }
                }
                catch { }
            }

            ClampCommandPaletteSelectionToRunnable();

            float keepY = _commandPaletteScrollY;
            bool keepScroll = scrollSelectionIntoView && _commandPaletteRowGOs.Count > 0;

            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            GalleryModalTypography type = new GalleryModalTypography(s);
            float rowH = 34f * s;
            float headerH = 22f * s;

            for (int r = 0; r < _commandPaletteFiltered.Count; r++)
            {
                CmdPaletteFilterRow row = _commandPaletteFiltered[r];
                if (row.Kind == CmdPaletteRowKind.Header)
                {
                    GameObject hdr = new GameObject("CmdHdr_" + r);
                    hdr.transform.SetParent(_commandPaletteListRT, false);
                    var hle = hdr.AddComponent<LayoutElement>();
                    hle.preferredHeight = headerH;
                    hle.minHeight = headerH;
                    hle.flexibleWidth = 1f;
                    hle.flexibleHeight = 0f;
                    Text ht = hdr.AddComponent<Text>();
                    ht.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    ht.text = row.HeaderText ?? "";
                    ht.color = new Color(0.65f, 0.70f, 0.78f, 0.95f);
                    ht.alignment = TextAnchor.MiddleLeft;
                    ht.raycastTarget = false;
                    GalleryUiMetrics.ApplyFont(ht, type.Caption, s, 11);
                    _commandPaletteRowGOs.Add(hdr);
                    continue;
                }

                string label = row.Title ?? "";
                if (!string.IsNullOrEmpty(row.Hint))
                    label = label + "  (" + row.Hint + ")";
                if (!row.Enabled)
                    label = label + "  —";

                int captured = r;
                Color rowBg = CommandPaletteRowBg(row.Enabled, r == _commandPaletteSelected);
                GameObject btn = UI.CreateChromeLayoutButton(
                    _commandPaletteListRT,
                    0f,
                    rowH,
                    label,
                    type.Body,
                    rowBg,
                    () => RunCommandPaletteFilteredRow(captured));
                btn.name = "CmdRow_" + r;
                Text btnTxt = btn.GetComponentInChildren<Text>(true);
                if (btnTxt != null)
                {
                    btnTxt.alignment = TextAnchor.MiddleLeft;
                    if (!row.Enabled)
                        btnTxt.color = new Color(0.55f, 0.55f, 0.58f, 0.9f);
                }
                LayoutElement le = btn.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = rowH;
                    le.minHeight = rowH;
                    le.flexibleHeight = 0f;
                    le.flexibleWidth = 1f;
                }
                _commandPaletteRowGOs.Add(btn);
            }

            try { Canvas.ForceUpdateCanvases(); } catch { }
            FinalizeCommandPaletteContentHeight();
            try { Canvas.ForceUpdateCanvases(); } catch { }

            if (scrollSelectionIntoView && keepScroll)
            {
                CommandPaletteSetScrollY(keepY);
                EnsureCommandPaletteSelectionVisible();
            }
            else
                CommandPaletteSetScrollY(0f);
        }

        private static Color CommandPaletteRowBg(bool enabled, bool selected)
        {
            if (!enabled) return new Color(0.12f, 0.13f, 0.14f, 0.75f);
            if (selected) return new Color(0.25f, 0.40f, 0.55f, 0.95f);
            return new Color(0.14f, 0.16f, 0.18f, 0.92f);
        }

        private void RefreshCommandPaletteSelectionVisuals(bool scrollIntoView)
        {
            for (int r = 0; r < _commandPaletteRowGOs.Count && r < _commandPaletteFiltered.Count; r++)
            {
                CmdPaletteFilterRow row = _commandPaletteFiltered[r];
                if (row.Kind == CmdPaletteRowKind.Header) continue;
                GameObject go = _commandPaletteRowGOs[r];
                if (go == null) continue;
                Image img = go.GetComponent<Image>();
                if (img != null)
                    img.color = CommandPaletteRowBg(row.Enabled, r == _commandPaletteSelected);
            }
            if (scrollIntoView)
                EnsureCommandPaletteSelectionVisible();
        }

        private void ClampCommandPaletteSelectionToRunnable()
        {
            if (_commandPaletteFiltered.Count == 0)
            {
                _commandPaletteSelected = 0;
                return;
            }
            if (_commandPaletteSelected < 0 || _commandPaletteSelected >= _commandPaletteFiltered.Count
                || _commandPaletteFiltered[_commandPaletteSelected].Kind == CmdPaletteRowKind.Header)
            {
                _commandPaletteSelected = FindNextCommandPaletteRunnable(0, 1);
                if (_commandPaletteSelected < 0) _commandPaletteSelected = 0;
            }
        }

        private int FindNextCommandPaletteRunnable(int from, int dir)
        {
            int n = _commandPaletteFiltered.Count;
            if (n <= 0) return -1;
            int i = from;
            for (int step = 0; step < n; step++)
            {
                if (i < 0) i = n - 1;
                if (i >= n) i = 0;
                if (_commandPaletteFiltered[i].Kind != CmdPaletteRowKind.Header)
                    return i;
                i += dir;
            }
            return -1;
        }

        private void MoveCommandPaletteSelection(int dir)
        {
            if (_commandPaletteFiltered.Count == 0) return;
            int start = _commandPaletteSelected + dir;
            int next = FindNextCommandPaletteRunnable(start, dir);
            if (next < 0) return;
            _commandPaletteSelected = next;
            RefreshCommandPaletteSelectionVisuals(scrollIntoView: true);
        }

        private void EnsureCommandPaletteSelectionVisible()
        {
            int idx = _commandPaletteSelected;
            int n = _commandPaletteRowGOs.Count;
            if (n <= 0 || idx < 0 || idx >= n) return;
            if (_commandPaletteListRT == null || _commandPaletteViewportRT == null) return;

            try { Canvas.ForceUpdateCanvases(); } catch { }

            float scrollable = CommandPaletteScrollablePx();
            if (scrollable <= 1f)
            {
                CommandPaletteSetScrollY(0f);
                return;
            }

            float spacing = _commandPaletteRowSpacing > 0f ? _commandPaletteRowSpacing : 4f;
            float padTop = 0f;
            VerticalLayoutGroup vlg = _commandPaletteListRT.GetComponent<VerticalLayoutGroup>();
            if (vlg != null && vlg.padding != null)
                padTop = vlg.padding.top;

            float rowTop = padTop;
            for (int i = 0; i < idx; i++)
            {
                RectTransform rt = _commandPaletteRowGOs[i] != null
                    ? _commandPaletteRowGOs[i].GetComponent<RectTransform>()
                    : null;
                float h = (rt != null && rt.rect.height > 1f) ? rt.rect.height : 22f;
                rowTop += h + spacing;
            }
            RectTransform rowRt = _commandPaletteRowGOs[idx].GetComponent<RectTransform>();
            float rowH = (rowRt != null && rowRt.rect.height > 1f) ? rowRt.rect.height : 34f;
            float rowBottom = rowTop + rowH;
            float viewH = _commandPaletteViewportRT.rect.height;
            float viewTop = _commandPaletteScrollY;
            float viewBottom = viewTop + viewH;
            float newY = viewTop;
            if (rowTop < viewTop)
                newY = rowTop;
            else if (rowBottom > viewBottom)
                newY = rowBottom - viewH;
            CommandPaletteSetScrollY(newY);
        }

        private void RebuildCommandPaletteRowsForced()
        {
            string keep = _commandPaletteLastFilter;
            _commandPaletteLastFilter = "\0";
            RebuildCommandPaletteRows(keep, scrollSelectionIntoView: true);
        }

        private void RunCommandPaletteFilteredRow(int filteredIdx)
        {
            if (filteredIdx < 0 || filteredIdx >= _commandPaletteFiltered.Count) return;
            CmdPaletteFilterRow row = _commandPaletteFiltered[filteredIdx];
            if (row.Kind == CmdPaletteRowKind.Header) return;

            if (!row.Enabled)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.cmd.unavailable", "Not available right now."),
                    1.5f);
                return;
            }

            if (row.Kind == CmdPaletteRowKind.Category)
            {
                CloseCommandPalette();
                try
                {
                    Gallery.Category cat = new Gallery.Category
                    {
                        name = row.CatName,
                        extension = row.CatExt,
                        path = row.CatPath
                    };
                    QueueDeferredCategoryQuickPick(cat);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("[VPB] Command palette category jump failed: " + ex.Message);
                }
                return;
            }

            RunCommandPaletteEntry(row.CatalogIdx);
        }

        private void RunCommandPaletteEntry(int catalogIdx)
        {
            if (catalogIdx < 0 || catalogIdx >= _commandPaletteCatalog.Count) return;
            CommandPaletteEntry e = _commandPaletteCatalog[catalogIdx];
            if (!CommandPaletteEntryAvailable(e))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.cmd.unavailable", "Not available right now."),
                    1.5f);
                return;
            }

            RememberCommandPaletteRecent(e.Id);
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
                MoveCommandPaletteSelection(-1);
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                MoveCommandPaletteSelection(1);
                return true;
            }

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                if (_commandPaletteFiltered.Count > 0
                    && _commandPaletteSelected >= 0
                    && _commandPaletteSelected < _commandPaletteFiltered.Count)
                {
                    RunCommandPaletteFilteredRow(_commandPaletteSelected);
                }
                return true;
            }

            return false;
        }

        /// <summary>Apply current grid selection via keyboard (Enter/Space). Settings: toggle row. Warm path.</summary>
        private void TryKeyboardApplySelection()
        {
            if (!IsVisible) return;
            if (_benchPickModeActive || _stripKeepSubScenePickActive) return;
            if (_removeModeActive) return;
            if (_commandPaletteOpen) return;

            if (settingsListViewActive)
            {
                TryKeyboardToggleSelectedSetting();
                return;
            }

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

        /// <summary>Enter/Space on settings list — toggle/cycle selected row (filter preserved).</summary>
        private void TryKeyboardToggleSelectedSetting()
        {
            FileEntry file = null;
            if (selectedFiles != null && selectedFiles.Count > 0)
                file = selectedFiles[selectedFiles.Count - 1];

            if (file == null && currentFilteredFiles != null && currentFilteredFiles.Count > 0
                && !string.IsNullOrEmpty(selectedPath))
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

            if (!(file is InternalSettingRowEntry))
            {
                EnsureKeyboardGridSelection();
                if (selectedFiles != null && selectedFiles.Count > 0)
                    file = selectedFiles[selectedFiles.Count - 1];
            }

            if (!(file is InternalSettingRowEntry))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.keyboard.settings_none", "No setting selected."),
                    1.5f);
                return;
            }

            bool secondary = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            try { HandleInternalSettingsRowClick(file, secondary); } catch { }
        }

        private void FocusTitleSearchInput()
        {
            try { FocusTitleSearchFromHotkey(); } catch { }
        }
    }
}
