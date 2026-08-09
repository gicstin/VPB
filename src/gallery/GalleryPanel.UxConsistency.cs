using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Part-4 UX consistency: sticky enter gate (Try-On Keep/Revert/Esc),
    /// armed apply reset, file-move Undo pairs, grid RMB actions menu,
    /// first-run modes onboarding. Warm/cold only — no per-frame alloc.
    /// </summary>
    public partial class GalleryPanel
    {
        private StickyToolMode _deferredStickyEnter = StickyToolMode.None;
        private bool _confirmEscIsDismiss;

        private GameObject _gridCtxMenuGO;
        private GameObject _gridCtxMenuPanelGO;
        private FileEntry _gridCtxMenuFile;
        private Vector2 _gridCtxMenuAnchorLocal;
        private enum GridCtxPage { Root = 0, Tools = 1, Rate = 2, Tags = 3, Select = 4 }
        private GridCtxPage _gridCtxMenuPage = GridCtxPage.Root;
        private const float GridCtxPanelWidthRef = 300f;
        private const float GridCtxAccelColRef = 28f;
        /// <summary>Inset from row right edge to accelerator column right edge.</summary>
        private const float GridCtxAccelPadRef = 18f;

        private struct GridCtxHotkey
        {
            public KeyCode Key;
            public UnityAction Action;
        }

        private readonly List<GridCtxHotkey> _gridCtxHotkeys = new List<GridCtxHotkey>(24);

        private struct FileMoveUndoPair
        {
            public string FromDeletedPath;
            public string ToOriginalPath;
        }

        /// <summary>
        /// Gate sticky enter while Try-On open. False = dialog pending (caller must return).
        /// Esc dismisses dialog and stays in Try-On (no Keep/Revert).
        /// </summary>
        private bool GateStickyEnterWhileTryOn(StickyToolMode entering)
        {
            if (!_tryOnActive || entering == StickyToolMode.TryOn)
                return true;

            _deferredStickyEnter = entering;
            _confirmEscIsDismiss = true;
            string keptName = _tryOnCurrentName;
            DisplayConfirm(
                VPBTranslation.T("gallery.tryon.sticky_title", "Try-On still open"),
                string.Format(
                    VPBTranslation.T(
                        "gallery.tryon.sticky_msg",
                        "Keep '{0}' before switching tools?\n\nKeep = commit · Revert = discard · Esc = stay in Try-On."),
                    string.IsNullOrEmpty(keptName) ? "…" : keptName),
                StickySwitchKeepThenEnter,
                StickySwitchRevertThenEnter,
                VPBTranslation.T("gallery.tryon.btn_keep", "Keep"),
                VPBTranslation.T("gallery.tryon.btn_revert", "Revert"));
            return false;
        }

        private void StickySwitchKeepThenEnter()
        {
            StickyToolMode mode = _deferredStickyEnter;
            _deferredStickyEnter = StickyToolMode.None;
            try { TryOnKeep(); } catch { }
            CompleteDeferredStickyEnter(mode);
        }

        private void StickySwitchRevertThenEnter()
        {
            StickyToolMode mode = _deferredStickyEnter;
            _deferredStickyEnter = StickyToolMode.None;
            try { TryOnRevert(); } catch { }
            CompleteDeferredStickyEnter(mode);
        }

        private void CompleteDeferredStickyEnter(StickyToolMode mode)
        {
            switch (mode)
            {
                case StickyToolMode.Creator:
                    EnterCreatorMode();
                    break;
                case StickyToolMode.Remove:
                    RemoveModeEnter(_removeModeSiderailUseLeft);
                    break;
                case StickyToolMode.Cleanup:
                    TboxOpenCleanupView();
                    break;
                case StickyToolMode.Import:
                    try { SetImportSidebarActive(true); } catch { }
                    break;
                case StickyToolMode.BenchPick:
                    // Bench enter owns its own args; abort deferred — user re-opens Bench.
                    ShowTemporaryStatus(
                        VPBTranslation.T(
                            "gallery.tryon.sticky_bench_reopen",
                            "Try-On closed — reopen Bench pick if needed."),
                        2f);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Clear Hold-launch / 1-Click when leaving sticky tools (idle).
        /// Prevents mode-error applies after Eraser / Scene Tools exit.
        /// </summary>
        private void ResetArmedApplySemanticsIfIdle(bool toast)
        {
            if (GetActiveStickyToolMode() != StickyToolMode.None)
                return;
            ForceClearArmedApplySemantics(toast);
        }

        private void PushFileMoveUndo(List<FileMoveUndoPair> pairs, string label, string refreshReason)
        {
            if (pairs == null || pairs.Count == 0) return;

            // Copy for closure lifetime (warm path; not per-frame).
            FileMoveUndoPair[] snap = pairs.ToArray();
            string reason = refreshReason;
            PushUndo(() =>
            {
                int restored = 0;
                for (int i = 0; i < snap.Length; i++)
                {
                    string from = snap[i].FromDeletedPath;
                    string to = snap[i].ToOriginalPath;
                    if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) continue;
                    try
                    {
                        if (!File.Exists(from)) continue;
                        string toDir = Path.GetDirectoryName(to);
                        if (!string.IsNullOrEmpty(toDir) && !Directory.Exists(toDir))
                            Directory.CreateDirectory(toDir);
                        if (File.Exists(to))
                        {
                            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                            string dir = Path.GetDirectoryName(to) ?? "";
                            string name = Path.GetFileNameWithoutExtension(to) ?? "restored";
                            string ext = Path.GetExtension(to) ?? "";
                            to = Path.Combine(dir, name + "__undo_" + stamp + ext);
                        }
                        File.Move(from, to);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError("[VPB] FileMoveUndo restore failed: " + ex.Message);
                    }
                }
                try
                {
                    if (!string.IsNullOrEmpty(reason))
                        FileManagerBridge.Refresh(reason, RefreshScope.Both);
                }
                catch { }
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("gallery.undo.files_restored", "Restored {0} file(s)."),
                        restored),
                    2f);
            }, label);
        }

        private void CloseGridContextMenu()
        {
            _gridCtxMenuFile = null;
            _gridCtxMenuPanelGO = null;
            _gridCtxMenuPage = GridCtxPage.Root;
            _gridCtxHotkeys.Clear();
            if (_gridCtxMenuGO != null)
            {
                try { Destroy(_gridCtxMenuGO); } catch { }
                _gridCtxMenuGO = null;
            }
        }

        private bool IsGridContextMenuOpen()
        {
            return _gridCtxMenuGO != null;
        }

        /// <summary>
        /// Context-menu keyboard: Esc/Backspace/← back or dismiss; single-letter/digit accelerators
        /// (Galitz menu accelerators; power-user dual path). No modifiers — avoids Ctrl/Alt chords.
        /// </summary>
        private bool TryHandleGridContextMenuKeys()
        {
            if (_gridCtxMenuGO == null) return false;

            if (Input.GetKeyDown(KeyCode.Escape)
                || Input.GetKeyDown(KeyCode.Backspace)
                || Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (_gridCtxMenuPage != GridCtxPage.Root)
                {
                    GridCtxShowPage(GridCtxPage.Root);
                    return true;
                }
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    CloseGridContextMenu();
                    return true;
                }
                // Backspace/← on root: still dismiss (locus of control exit).
                if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    CloseGridContextMenu();
                    return true;
                }
            }

            bool mod = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (mod) return false;

            for (int i = 0; i < _gridCtxHotkeys.Count; i++)
            {
                KeyCode k = _gridCtxHotkeys[i].Key;
                if (k == KeyCode.None) continue;
                if (!Input.GetKeyDown(k)) continue;
                UnityAction act = _gridCtxHotkeys[i].Action;
                if (act != null) act.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Screen-space RMB context menu on grid row (Galitz popup: object verbs near cursor).
        /// Icons + single-key accelerators; Tools/Rate/Tags/Select cascades (►).
        /// </summary>
        private void ShowGridItemContextMenu(FileEntry file)
        {
            CloseGridContextMenu();
            if (file == null || backgroundBoxGO == null) return;
            _gridCtxMenuFile = file;
            _gridCtxMenuPage = GridCtxPage.Root;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            GameObject root = UI.CreatePopupMenuRoot(backgroundBoxGO, "VPB_GridCtxMenu", CloseGridContextMenu);
            _gridCtxMenuGO = root;

            _gridCtxMenuPanelGO = UI.CreatePopupMenuPanel(
                root,
                "Panel",
                AnchorPresets.middleCenter,
                new Vector2(GridCtxPanelWidthRef, 50f),
                Vector2.zero);

            try
            {
                RectTransform canvasRT = backgroundBoxGO.GetComponent<RectTransform>();
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, Input.mousePosition, null, out local);
                _gridCtxMenuAnchorLocal = local;
                RectTransform panelRT = _gridCtxMenuPanelGO.GetComponent<RectTransform>();
                panelRT.anchorMin = new Vector2(0.5f, 0.5f);
                panelRT.anchorMax = new Vector2(0.5f, 0.5f);
                panelRT.pivot = new Vector2(0f, 1f);
                panelRT.anchoredPosition = local;
            }
            catch { }

            GridCtxRebuildPage();
            RescaleGridContextMenuInternal(s);
            SetLayerRecursive(root, backgroundBoxGO.layer);
        }

        private void GridCtxShowPage(GridCtxPage page)
        {
            if (_gridCtxMenuGO == null || _gridCtxMenuPanelGO == null) return;
            _gridCtxMenuPage = page;
            GridCtxRebuildPage();
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            RescaleGridContextMenuInternal(s);
        }

        private void GridCtxRebuildPage()
        {
            if (_gridCtxMenuPanelGO == null) return;
            _gridCtxHotkeys.Clear();
            UI.DestroyAllChildren(_gridCtxMenuPanelGO.transform);

            switch (_gridCtxMenuPage)
            {
                case GridCtxPage.Tools:
                    GridCtxBuildToolsPage();
                    break;
                case GridCtxPage.Rate:
                    GridCtxBuildRatePage();
                    break;
                case GridCtxPage.Tags:
                    GridCtxBuildTagsPage();
                    break;
                case GridCtxPage.Select:
                    GridCtxBuildSelectPage();
                    break;
                default:
                    GridCtxBuildRootPage();
                    break;
            }
        }

        private Sprite GridCtxIcon(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            try { return UI.LoadIconSprite(relativePath, Color.white); }
            catch { return null; }
        }

        private void GridCtxBuildRootPage()
        {
            FileEntry file = _gridCtxMenuFile;
            int selCount = selectedFiles != null ? selectedFiles.Count : 0;
            string title;
            if (selCount > 1)
            {
                title = string.Format(
                    VPBTranslation.T("gallery.detail.selected_many", "{0} selected"),
                    selCount);
            }
            else
            {
                title = file != null ? (file.Name ?? file.Uid ?? "Item") : "Item";
                if (title.Length > 28) title = title.Substring(0, 26) + "…";
            }
            GridCtxAddTitle(title);

            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.apply", "Apply"),
                KeyCode.A, "A",
                GridCtxIcon("vpb_icons/gallery_apply_1click.png"),
                () =>
                {
                    FileEntry f = _gridCtxMenuFile;
                    CloseGridContextMenu();
                    if (f != null) try { ExecuteAutoActionForFile(f); } catch { }
                });

            bool canHub = selCount == 1
                && selectedFiles != null
                && selectedFiles[0] != null
                && !string.IsNullOrEmpty(TryGetPackageUidForEntry(selectedFiles[0]));
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.open_hub", "Open on Hub"),
                KeyCode.O, "O",
                GridCtxIcon("vpb_icons/hub.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxOpenSelectedItemOnHub(); } catch { }
                },
                enabled: canHub);

            GridCtxAddAction(
                VPBTranslation.T("gallery.detail.expand", "Details"),
                KeyCode.I, "I",
                GridCtxIcon("vpb_icons/expand_up.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { DetailStripToggleExpanded(); } catch { }
                });

            GridCtxAddSeparator();
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.copy_names", "Copy Names"),
                KeyCode.C, "C",
                GridCtxIcon("vpb_icons/clipboard_list.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { CopySelectedPackageNamesToClipboard(); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.copy_paths", "Copy Paths"),
                KeyCode.P, "P",
                GridCtxIcon("vpb_icons/folder.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { CopySelectedPackageNamesToClipboard(forceFullPaths: true); } catch { }
                });
            if (file != null)
            {
                GridCtxAddAction(
                    VPBTranslation.T("gallery.gridctx.copy_missing_deps", "Copy Missing Deps"),
                    KeyCode.M, "M",
                    GridCtxIcon("vpb_icons/load_deps.png"),
                    () =>
                    {
                        FileEntry f = _gridCtxMenuFile;
                        CloseGridContextMenu();
                        if (f != null) try { CopyMissingDependenciesToClipboard(f); } catch { }
                    });
            }

            int hideN = 0, unhideN = 0;
            try { DetailStripCountHideUnhide(out hideN, out unhideN); }
            catch { hideN = 0; unhideN = 0; }
            if (hideN > 0 || unhideN > 0)
            {
                GridCtxAddSeparator();
                if (hideN > 0)
                {
                    GridCtxAddAction(
                        VPBTranslation.T("gallery.tbox.hide", "Hide"),
                        KeyCode.H, "H",
                        GridCtxIcon("vpb_icons/show_hidden.png"),
                        () =>
                        {
                            CloseGridContextMenu();
                            try { TboxHideSelectedPackages(); } catch { }
                        });
                }
                if (unhideN > 0)
                {
                    GridCtxAddAction(
                        VPBTranslation.T("gallery.tbox.unhide", "Unhide"),
                        KeyCode.U, "U",
                        GridCtxIcon("vpb_icons/show_hidden_off.png"),
                        () =>
                        {
                            CloseGridContextMenu();
                            try { TboxUnhideSelectedPackages(); } catch { }
                        });
                }
            }

            GridCtxAddSeparator();
            GridCtxAddSubmenu(
                VPBTranslation.T("gallery.gridctx.rate", "Rate"),
                KeyCode.R, "R",
                GridCtxIcon("vpb_icons/star.png"),
                () => GridCtxShowPage(GridCtxPage.Rate));
            GridCtxAddSubmenu(
                VPBTranslation.T("gallery.gridctx.tags", "Tags"),
                KeyCode.G, "G",
                GridCtxIcon("vpb_icons/tags.png"),
                () => GridCtxShowPage(GridCtxPage.Tags));
            GridCtxAddSubmenu(
                VPBTranslation.T("gallery.gridctx.tools", "Tools"),
                KeyCode.T, "T",
                GridCtxIcon("vpb_icons/settings.png"),
                () => GridCtxShowPage(GridCtxPage.Tools));
            GridCtxAddSubmenu(
                VPBTranslation.T("gallery.gridctx.select", "Select"),
                KeyCode.S, "S",
                GridCtxIcon("vpb_icons/select_all.png"),
                () => GridCtxShowPage(GridCtxPage.Select));

            bool historyBrowse = activeContentType == ContentType.History;
            int deleteN = 0;
            try
            {
                deleteN = GetTboxDeleteEligiblePackageCount()
                    + GetTboxDeleteEligibleLocalSceneCount()
                    + GetTboxDeleteEligibleLocalPresetCount();
            }
            catch { deleteN = 0; }

            if (historyBrowse || deleteN > 0)
            {
                GridCtxAddSeparator();
                if (historyBrowse)
                {
                    GridCtxAddAction(
                        VPBTranslation.T("gallery.tbox.remove_history", "Remove History"),
                        KeyCode.D, "D",
                        GridCtxIcon("vpb_icons/list_remove.png"),
                        () =>
                        {
                            CloseGridContextMenu();
                            try { TboxRemoveSelectedFromHistory(); } catch { }
                        },
                        destructive: true);
                }
                else
                {
                    GridCtxAddAction(
                        VPBTranslation.T("gallery.tbox.delete", "Delete"),
                        KeyCode.D, "D",
                        GridCtxIcon("vpb_icons/delete.png"),
                        () =>
                        {
                            CloseGridContextMenu();
                            try { TboxDeleteSelectedPackages(); } catch { }
                        },
                        destructive: true);
                }
            }
        }

        private void GridCtxBuildToolsPage()
        {
            GridCtxAddTitle(VPBTranslation.T("gallery.gridctx.tools", "Tools"));
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.back", "◄ Back"),
                KeyCode.B, "B",
                null,
                () => GridCtxShowPage(GridCtxPage.Root));
            GridCtxAddSeparator();

            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.load", "Load"),
                KeyCode.L, "L",
                GridCtxIcon("vpb_icons/load.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxLoadSelectedPackages(); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.load_deps", "Load Deps"),
                KeyCode.E, "E",
                GridCtxIcon("vpb_icons/load_deps.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxLoadDepsSelectedPackages(); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.unload", "Unload"),
                KeyCode.N, "N",
                GridCtxIcon("vpb_icons/unload.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxUnloadSelectedPackages(); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.cache_textures", "Cache Textures"),
                KeyCode.X, "X",
                GridCtxIcon("vpb_icons/cache_texture.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxCacheTexturesSelected(); } catch { }
                });

            GridCtxAddSeparator();
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.autoinstall", "Autoinstall"),
                KeyCode.A, "A",
                GridCtxIcon("vpb_icons/auto.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxAutoInstallSelectedPackages(); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.no_autoinstall", "No autoinstall"),
                KeyCode.O, "O",
                GridCtxIcon("vpb_icons/auto_off.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxDisableAutoInstallSelectedPackages(); } catch { }
                });

            GridCtxAddSeparator();
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.whitelist", "Scan whitelist"),
                KeyCode.W, "W",
                GridCtxIcon("vpb_icons/filter.png"),
                () =>
                {
                    FileEntry f = _gridCtxMenuFile;
                    CloseGridContextMenu();
                    if (f == null) return;
                    bool applySel = PrepareFileEntryGestureSelection(f);
                    try { HandleDesktopScanWhitelistClickGesture(f, applySel, temporary: false); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.temp_whitelist", "Temp whitelist"),
                KeyCode.Y, "Y",
                GridCtxIcon("vpb_icons/temporary.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxScanWhitelistTemporaryForSelection(); } catch { }
                });

            GridCtxAddSeparator();
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.cleanup", "Cleanup"),
                KeyCode.K, "K",
                GridCtxIcon("vpb_icons/cleanup.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { TboxOpenCleanupView(); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.detail.chip_old_vers", "Older Versions…"),
                KeyCode.V, "V",
                GridCtxIcon("vpb_icons/cleanup.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { DetailStripOnCleanupOldVersionsClick(); } catch { }
                });
        }

        private void GridCtxBuildRatePage()
        {
            GridCtxAddTitle(VPBTranslation.T("gallery.gridctx.rate", "Rate"));
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.back", "◄ Back"),
                KeyCode.B, "B",
                null,
                () => GridCtxShowPage(GridCtxPage.Root));
            GridCtxAddSeparator();

            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.rate_clear", "★ Clear"),
                KeyCode.Alpha0, "0",
                GridCtxIcon("vpb_icons/star_off.png"),
                () => GridCtxRunRating(0));
            // Also accept keypad 0.
            GridCtxRegisterHotkey(KeyCode.Keypad0, () => GridCtxRunRating(0));

            for (int r = 1; r <= 5; r++)
            {
                int stars = r;
                KeyCode alpha = (KeyCode)((int)KeyCode.Alpha1 + (r - 1));
                KeyCode keypad = (KeyCode)((int)KeyCode.Keypad1 + (r - 1));
                string label = string.Format(
                    VPBTranslation.T("gallery.gridctx.rate_n", "★ {0}"),
                    stars);
                GridCtxAddAction(
                    label,
                    alpha, stars.ToString(),
                    GridCtxIcon("vpb_icons/star.png"),
                    () => GridCtxRunRating(stars));
                GridCtxRegisterHotkey(keypad, () => GridCtxRunRating(stars));
            }
        }

        private void GridCtxBuildTagsPage()
        {
            GridCtxAddTitle(VPBTranslation.T("gallery.gridctx.tags", "Tags"));
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.back", "◄ Back"),
                KeyCode.B, "B",
                null,
                () => GridCtxShowPage(GridCtxPage.Root));
            GridCtxAddSeparator();

            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.copy_tags", "Copy Tags"),
                KeyCode.C, "C",
                GridCtxIcon("vpb_icons/tag_plus.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { UserTagClipboardCopyFromSelection(); } catch { }
                });
            bool canPaste = false;
            try { canPaste = UserTagClipboardHasTags(); } catch { canPaste = false; }
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.paste_tags", "Paste Tags"),
                KeyCode.V, "V",
                GridCtxIcon("vpb_icons/tags.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { UserTagClipboardPasteToSelection(replace: false); } catch { }
                },
                enabled: canPaste);
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.replace_tags", "Replace Tags"),
                KeyCode.R, "R",
                GridCtxIcon("vpb_icons/tag_minus.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { UserTagClipboardPasteToSelection(replace: true); } catch { }
                },
                enabled: canPaste);
            GridCtxAddSeparator();
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.edit_tags", "Edit Tags…"),
                KeyCode.E, "E",
                GridCtxIcon("vpb_icons/edit.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try
                    {
                        if (!DetailStripIsExpanded()) DetailStripToggleExpanded();
                        DetailStripToggleTagMenu();
                    }
                    catch { }
                });
        }

        private void GridCtxBuildSelectPage()
        {
            GridCtxAddTitle(VPBTranslation.T("gallery.gridctx.select", "Select"));
            GridCtxAddAction(
                VPBTranslation.T("gallery.gridctx.back", "◄ Back"),
                KeyCode.B, "B",
                null,
                () => GridCtxShowPage(GridCtxPage.Root));
            GridCtxAddSeparator();

            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.select_all", "Select All"),
                KeyCode.A, "A",
                GridCtxIcon("vpb_icons/select_all.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { SelectAll(); } catch { }
                });
            GridCtxAddAction(
                VPBTranslation.T("gallery.tbox.clear_selection", "Clear Selection"),
                KeyCode.C, "C",
                GridCtxIcon("vpb_icons/clear_selection.png"),
                () =>
                {
                    CloseGridContextMenu();
                    try { ClearSelection(); } catch { }
                });
        }

        private void GridCtxRunRating(int ratingValue)
        {
            CloseGridContextMenu();
            try { GridCtxApplyRatingToSelection(ratingValue); } catch { }
        }

        /// <summary>Apply rating without requiring toolbox rate handler to exist.</summary>
        private void GridCtxApplyRatingToSelection(int ratingValue)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            ratingValue = Mathf.Clamp(ratingValue, 0, 5);
            int applied = 0;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                try
                {
                    RatingsManager.Instance.SetRating(f, ratingValue);
                    applied++;
                }
                catch { }
            }
            if (applied == 0) return;
            try
            {
                if (tboxGridRateHandler != null)
                {
                    tboxGridRateHandler.SetDisplayOnly(ratingValue);
                    tboxGridRateHandler.CloseSelector();
                }
            }
            catch { }
            try { TboxAfterGridRateChanged(); } catch { }
            try
            {
                _detailStripStarRating = ratingValue;
                _detailStripStarHover = 0;
                DetailStripPaintStars();
            }
            catch { }
        }

        private void RescaleGridContextMenuInternal(float s)
        {
            if (_gridCtxMenuGO == null || _gridCtxMenuPanelGO == null) return;
            if (s <= 0f) s = 1f;
            ScaleVerticalPopupMenuRows(
                _gridCtxMenuPanelGO,
                s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GridCtxPanelWidthRef);

            Transform panel = _gridCtxMenuPanelGO.transform;
            float sepH = Mathf.Max(1f, 2f * s);
            float pad = GalleryUiDesignTokens.PopupMenuRowTextPadXRef * s;
            for (int i = 0; i < panel.childCount; i++)
            {
                Transform ch = panel.GetChild(i);
                if (ch == null) continue;
                if (ch.name == "Separator")
                {
                    LayoutElement le = ch.GetComponent<LayoutElement>();
                    if (le != null)
                    {
                        le.preferredHeight = sepH;
                        le.minHeight = sepH;
                    }
                    continue;
                }

                Transform accelTr = ch.Find("Accel");
                if (accelTr != null)
                {
                    GridCtxLayoutAccel(accelTr as RectTransform, s);
                    Text at = accelTr.GetComponent<Text>();
                    if (at != null)
                        GalleryUiMetrics.ApplyFont(at, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                }

                Text labelT = null;
                Transform textTr = ch.Find("Text");
                if (textTr != null) labelT = textTr.GetComponent<Text>();
                if (labelT == null)
                {
                    Text[] texts = ch.GetComponentsInChildren<Text>(true);
                    for (int ti = 0; ti < texts.Length; ti++)
                    {
                        if (texts[ti] != null && texts[ti].gameObject.name != "Accel")
                        {
                            labelT = texts[ti];
                            break;
                        }
                    }
                }
                if (labelT != null)
                {
                    float leftExtraRef = 0f;
                    if (ch.Find("RowIcon") != null)
                        leftExtraRef = GalleryUiDesignTokens.PopupMenuRowIconSizeRef + GalleryUiDesignTokens.PopupMenuRowIconGapRef;
                    RectTransform lrt = labelT.rectTransform;
                    if (lrt != null)
                    {
                        float left = pad + leftExtraRef * s;
                        float right = accelTr != null
                            ? (GridCtxAccelPadRef * s + GridCtxAccelColRef * s + 8f * s)
                            : pad;
                        lrt.offsetMin = new Vector2(left, 0f);
                        lrt.offsetMax = new Vector2(-right, 0f);
                    }
                }
            }

            RectTransform panelRT = _gridCtxMenuPanelGO.GetComponent<RectTransform>();
            RectTransform overlayRT = backgroundBoxGO != null
                ? backgroundBoxGO.GetComponent<RectTransform>()
                : null;
            if (panelRT != null)
            {
                panelRT.anchoredPosition = _gridCtxMenuAnchorLocal;
                if (overlayRT != null)
                {
                    float clampPad = 8f * s;
                    UI.ClampPopupMenuPanelX(panelRT, overlayRT, clampPad);
                    UI.ClampPopupMenuPanelY(panelRT, overlayRT, clampPad);
                }
            }
        }

        private void GridCtxAddTitle(string title)
        {
            if (_gridCtxMenuPanelGO == null) return;
            Text titleTxt = UI.CreateLabel(
                _gridCtxMenuPanelGO,
                title ?? "",
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                UI.PopupMutedText,
                TextAnchor.MiddleCenter,
                raycastTarget: false,
                name: "Title");
            if (titleTxt == null) return;
            LayoutElement titleLe = titleTxt.gameObject.GetComponent<LayoutElement>();
            if (titleLe == null) titleLe = titleTxt.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredHeight = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            titleLe.minHeight = GalleryUiDesignTokens.PopupMenuRowHeightRef;
            titleLe.flexibleWidth = 1f;
        }

        private void GridCtxAddSeparator()
        {
            if (_gridCtxMenuPanelGO == null) return;
            GameObject sep = UI.CreateChildRT(_gridCtxMenuPanelGO, "Separator", AnchorPresets.stretchAll);
            Image img = UI.AddImage(sep, new Color(1f, 1f, 1f, 0.18f), raycastTarget: false);
            if (img != null) img.raycastTarget = false;
            UI.AddLE(sep, preferredHeight: 2f, minHeight: 2f, flexibleWidth: 1f);
        }

        private void GridCtxRegisterHotkey(KeyCode key, UnityAction action)
        {
            if (key == KeyCode.None || action == null) return;
            _gridCtxHotkeys.Add(new GridCtxHotkey { Key = key, Action = action });
        }

        private void GridCtxAddAction(
            string label,
            KeyCode hotkey,
            string accelGlyph,
            Sprite icon,
            UnityAction onClick,
            bool enabled = true,
            bool destructive = false)
        {
            if (_gridCtxMenuPanelGO == null || onClick == null) return;

            GameObject row = UI.AddStretchPopupMenuRow(
                _gridCtxMenuPanelGO.transform,
                label,
                onClick,
                isActive: false,
                enabled: enabled,
                rowHeight: GalleryUiDesignTokens.PopupMenuRowHeightRef,
                icon: icon);
            if (row == null) return;

            if (destructive)
            {
                Image rowImg = row.GetComponent<Image>();
                if (rowImg != null && enabled)
                    rowImg.color = new Color(0.35f, 0.15f, 0.15f, 1f);
            }

            if (!string.IsNullOrEmpty(accelGlyph))
            {
                GameObject accelGO = new GameObject("Accel");
                accelGO.transform.SetParent(row.transform, false);
                Text accel = accelGO.AddComponent<Text>();
                accel.text = accelGlyph;
                accel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                accel.fontSize = GalleryUiDesignTokens.PopupMenuRowFontRef;
                accel.alignment = TextAnchor.MiddleCenter;
                accel.raycastTarget = false;
                accel.horizontalOverflow = HorizontalWrapMode.Overflow;
                accel.verticalOverflow = VerticalWrapMode.Truncate;
                accel.color = enabled ? UI.PopupMutedText : new Color(1f, 1f, 1f, 0.35f);
                try { VPBUiFont.ApplyTo(accel); } catch { }
                GridCtxLayoutAccel(accel.rectTransform, 1f);

                // Leave room for accel on the main label.
                Text labelT = row.transform.Find("Text") != null
                    ? row.transform.Find("Text").GetComponent<Text>()
                    : null;
                if (labelT == null)
                {
                    Text[] texts = row.GetComponentsInChildren<Text>(true);
                    for (int ti = 0; ti < texts.Length; ti++)
                    {
                        if (texts[ti] != null && texts[ti].gameObject != accelGO)
                        {
                            labelT = texts[ti];
                            break;
                        }
                    }
                }
                if (labelT != null)
                {
                    float leftExtraRef = icon != null
                        ? GalleryUiDesignTokens.PopupMenuRowIconSizeRef + GalleryUiDesignTokens.PopupMenuRowIconGapRef
                        : 0f;
                    float pad = GalleryUiDesignTokens.PopupMenuRowTextPadXRef;
                    float right = GridCtxAccelPadRef + GridCtxAccelColRef + 8f;
                    RectTransform lrt = labelT.rectTransform;
                    lrt.offsetMin = new Vector2(pad + leftExtraRef, 0f);
                    lrt.offsetMax = new Vector2(-right, 0f);
                }
            }

            if (enabled && hotkey != KeyCode.None)
                GridCtxRegisterHotkey(hotkey, onClick);
        }

        /// <summary>
        /// Pin accelerator in a right-side column with explicit edge insets.
        /// Uses offsetMin/Max (not sizeDelta alone) so stretch leftovers cannot flush the glyph.
        /// </summary>
        private static void GridCtxLayoutAccel(RectTransform art, float scale)
        {
            if (art == null) return;
            if (scale <= 0f) scale = 1f;
            float pad = GridCtxAccelPadRef * scale;
            float col = GridCtxAccelColRef * scale;
            art.localScale = Vector3.one;
            art.anchorMin = new Vector2(1f, 0f);
            art.anchorMax = new Vector2(1f, 1f);
            art.pivot = new Vector2(1f, 0.5f);
            // Right edge inset by pad; left edge inset by pad+col (column width).
            art.offsetMin = new Vector2(-(pad + col), 0f);
            art.offsetMax = new Vector2(-pad, 0f);
        }

        private void GridCtxAddSubmenu(string label, KeyCode hotkey, string accelGlyph, Sprite icon, UnityAction onOpen)
        {
            // Galitz intent indicator: triangle / ► means cascade.
            GridCtxAddAction((label ?? "") + "  \u25B8", hotkey, accelGlyph, icon, onOpen);
        }

        private bool IsSearchClearUndoTop()
        {
            try
            {
                if (undoStack == null || undoStack.Count == 0) return false;
                string label = PeekUndoLabel();
                if (string.IsNullOrEmpty(label)) return false;
                return string.Equals(label, VPBTranslation.T("gallery.undo.search_clear", "Search clear"), StringComparison.Ordinal)
                    || string.Equals(label, "Search clear", StringComparison.Ordinal);
            }
            catch { return false; }
        }
    }
}
