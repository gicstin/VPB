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
        private FileEntry _gridCtxMenuFile;

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

        private bool TryHandleGridContextMenuEsc()
        {
            if (_gridCtxMenuGO == null) return false;
            if (!Input.GetKeyDown(KeyCode.Escape)) return false;
            CloseGridContextMenu();
            return true;
        }

        /// <summary>Screen-space RMB actions menu for a grid row (Jakob context-menu path).</summary>
        private void ShowGridItemContextMenu(FileEntry file)
        {
            CloseGridContextMenu();
            if (file == null || backgroundBoxGO == null) return;
            _gridCtxMenuFile = file;

            GameObject root = new GameObject("VPB_GridCtxMenu");
            root.transform.SetParent(backgroundBoxGO.transform, false);
            _gridCtxMenuGO = root;

            Image dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.01f);
            dim.raycastTarget = true;
            RectTransform rootRT = root.GetComponent<RectTransform>();
            if (rootRT == null) rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            Button dimBtn = root.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(CloseGridContextMenu);

            GameObject panel = UI.CreateChildRT(root, "Panel", AnchorPresets.middleCenter, new Vector2(220f, 200f));
            Image panelBg = UI.AddImage(panel, new Color(0.12f, 0.12f, 0.14f, 0.97f), true);
            UI.AddVLG(panel, spacing: 4f, padding: new RectOffset(8, 8, 8, 8), childAlignment: TextAnchor.UpperCenter);

            // Place near pointer (canvas local).
            try
            {
                RectTransform canvasRT = backgroundBoxGO.GetComponent<RectTransform>();
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, Input.mousePosition, null, out local);
                RectTransform panelRT = panel.GetComponent<RectTransform>();
                panelRT.anchorMin = new Vector2(0.5f, 0.5f);
                panelRT.anchorMax = new Vector2(0.5f, 0.5f);
                panelRT.anchoredPosition = local;
            }
            catch { }

            string name = file.Name ?? file.Uid ?? "Item";
            if (name.Length > 28) name = name.Substring(0, 26) + "…";
            UI.CreateLabel(panel, name, GalleryUiDesignTokens.FontBodyRef, new Color(0.85f, 0.85f, 0.88f, 1f),
                TextAnchor.MiddleCenter, raycastTarget: false, name: "Title");

            AddGridCtxButton(panel, VPBTranslation.T("gallery.gridctx.apply", "Apply"), () =>
            {
                FileEntry f = _gridCtxMenuFile;
                CloseGridContextMenu();
                if (f != null) try { ExecuteAutoActionForFile(f); } catch { }
            });
            AddGridCtxButton(panel, VPBTranslation.T("gallery.gridctx.whitelist", "Scan whitelist"), () =>
            {
                FileEntry f = _gridCtxMenuFile;
                CloseGridContextMenu();
                if (f == null) return;
                bool applySel = PrepareFileEntryGestureSelection(f);
                try { HandleDesktopScanWhitelistClickGesture(f, applySel, temporary: false); } catch { }
            });
            AddGridCtxButton(panel, VPBTranslation.T("gallery.gridctx.select_only", "Select"), () =>
            {
                FileEntry f = _gridCtxMenuFile;
                CloseGridContextMenu();
                if (f != null) try { PrepareFileEntryGestureSelection(f); } catch { }
            });
            AddGridCtxButton(panel, VPBTranslation.T("gallery.gridctx.close", "Close"), CloseGridContextMenu);

            SetLayerRecursive(root, backgroundBoxGO.layer);
        }

        private void AddGridCtxButton(GameObject parent, string label, UnityAction onClick)
        {
            GameObject btn = UI.CreateUIButton(parent, 200f, 32f, label, 14, 0, 0, AnchorPresets.middleCenter, onClick);
            var le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.AddComponent<LayoutElement>();
            le.preferredWidth = 200f;
            le.preferredHeight = 32f;
            le.minHeight = 32f;
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
