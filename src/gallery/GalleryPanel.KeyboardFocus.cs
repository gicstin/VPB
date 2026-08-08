using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Explicit keyboard focus regions for gallery browse + settings float filter.
    /// Unity Selectables use Navigation.Mode.None — no built-in Tab cycle.
    /// Browse: TitleSearch ↔ Grid. Settings float (when focused): Filter ↔ settings chrome.
    /// Warm path only — no per-frame alloc.
    /// </summary>
    public partial class GalleryPanel
    {
        private enum GalleryKeyboardFocusRegion
        {
            Grid = 0,
            TitleSearch = 1,
            OtherInput = 2,
            SettingsSideSearch = 3
        }

        private float _keyboardFocusStatusUntil;

        /// <summary>
        /// Tab / Down leave InputField traps into work surface; Tab from list returns to search.
        /// Call before InputField early-out in <see cref="HandleKeyboardInput"/>.
        /// </summary>
        private bool TryHandleKeyboardFocusTransfer()
        {
            if (!IsVisible || isCollapsed) return false;
            if (_commandPaletteOpen) return false;
            if (_inAppHelpOpen) return false;

            bool tab = Input.GetKeyDown(KeyCode.Tab);
            bool down = Input.GetKeyDown(KeyCode.DownArrow);
            if (!tab && !down) return false;

            // Modifiers: Ctrl/Alt Tab left for OS / future chords.
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (ctrl || alt) return false;

            // Settings float is modeless: Tab cycle stays in gallery unless focus already in float.
            // Legacy middle-pane settings list (if re-enabled) still owns Tab while active.
            if (settingsListViewActive)
                return TryHandleSettingsKeyboardFocusTransfer(tab, down);
            if (IsSettingsPanelOpen() && IsKeyboardFocusInsideSettingsFloat())
                return TryHandleSettingsKeyboardFocusTransfer(tab, down);

            GalleryKeyboardFocusRegion region = ResolveKeyboardFocusRegion();

            if (region == GalleryKeyboardFocusRegion.TitleSearch)
            {
                // Tab or Down → grid (omnibox pattern; single-line field unused Down).
                if (tab || down)
                {
                    FocusGridFromKeyboard(showStatus: true);
                    return true;
                }
            }
            else if (region == GalleryKeyboardFocusRegion.OtherInput)
            {
                // Escape any side/filter field trap into work surface.
                if (tab)
                {
                    FocusGridFromKeyboard(showStatus: true);
                    return true;
                }
            }
            else // Grid
            {
                // Tab ↔ search cycle (Shift+Tab same with two regions).
                if (tab)
                {
                    try { FocusTitleSearchFromHotkey(); } catch { }
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Settings: side-rail filter ↔ list. Title-search popup never owns settings filter.
        /// </summary>
        private bool TryHandleSettingsKeyboardFocusTransfer(bool tab, bool down)
        {
            GalleryKeyboardFocusRegion region = ResolveKeyboardFocusRegion();

            if (region == GalleryKeyboardFocusRegion.SettingsSideSearch
                || region == GalleryKeyboardFocusRegion.TitleSearch)
            {
                // Tab / Down leave filter into settings list (no popup covering first row).
                if (tab || down)
                {
                    FocusSettingsListFromKeyboard(showStatus: true);
                    return true;
                }
                return false;
            }

            if (region == GalleryKeyboardFocusRegion.OtherInput)
            {
                if (tab)
                {
                    FocusSettingsListFromKeyboard(showStatus: true);
                    return true;
                }
                return false;
            }

            // Settings list — Tab returns to side filter (Ctrl+F same).
            if (tab)
            {
                try { FocusSettingsSideSearchFromHotkey(); } catch { }
                return true;
            }

            return false;
        }

        private GalleryKeyboardFocusRegion ResolveKeyboardFocusRegion()
        {
            InputField field;
            if (!TryGetFocusedInputField(out field))
                return GalleryKeyboardFocusRegion.Grid;

            if (IsSettingsSideSearchField(field))
                return GalleryKeyboardFocusRegion.SettingsSideSearch;

            if (IsGalleryTitleSearchField(field))
                return GalleryKeyboardFocusRegion.TitleSearch;

            return GalleryKeyboardFocusRegion.OtherInput;
        }

        private bool IsGalleryTitleSearchField(InputField field)
        {
            if (field == null) return false;
            if (field == titleSearchInput) return true;
            if (field == _titleSearchPopupField) return true;
            return false;
        }

        private bool IsSettingsSideSearchField(InputField field)
        {
            if (field == null) return false;
            InputField floatFilter = GetSettingsFloatFilterInput();
            if (floatFilter != null && field == floatFilter) return true;
            return false;
        }

        /// <summary>
        /// True when EventSystem selection lives under the Settings float root.
        /// Modeless float: only then keyboard transfer targets settings chrome.
        /// </summary>
        private bool IsKeyboardFocusInsideSettingsFloat()
        {
            if (!IsSettingsPanelOpen() || _settingsFloatRoot == null) return false;
            try
            {
                if (EventSystem.current == null) return false;
                GameObject sel = EventSystem.current.currentSelectedGameObject;
                if (sel == null) return false;
                Transform t = sel.transform;
                Transform root = _settingsFloatRoot.transform;
                return t == root || t.IsChildOf(root);
            }
            catch
            {
                return false;
            }
        }

        private InputField GetSettingsSideSearchInput()
        {
            return GetSettingsFloatFilterInput();
        }

        private static bool TryGetFocusedInputField(out InputField field)
        {
            field = null;
            try
            {
                if (EventSystem.current == null) return false;
                GameObject sel = EventSystem.current.currentSelectedGameObject;
                if (sel == null) return false;
                field = sel.GetComponent<InputField>();
                return field != null;
            }
            catch
            {
                field = null;
                return false;
            }
        }

        /// <summary>
        /// True when transform lives under this panel canvas (or background box fallback).
        /// Warm path — no alloc.
        /// </summary>
        private bool IsTransformUnderThisGalleryUi(Transform t)
        {
            if (t == null) return false;
            Transform root = null;
            try
            {
                if (canvas != null) root = canvas.transform;
                else if (backgroundBoxGO != null) root = backgroundBoxGO.transform;
            }
            catch { root = null; }
            if (root == null) return false;
            try { return t == root || t.IsChildOf(root); }
            catch { return false; }
        }

        /// <summary>
        /// True when EventSystem has an InputField focused under the main gallery pane.
        /// Ctrl+F search, filter rename, tag search, etc. count as engagement.
        /// Modeless float filters (Settings / Plugins / Import / presets) do not — float ≠ mode.
        /// </summary>
        private bool IsGalleryOwnedTextFocusActive()
        {
            InputField field;
            if (!TryGetFocusedInputField(out field)) return false;
            if (field == null) return false;
            try
            {
                if (!field.isFocused) return false;
            }
            catch { return false; }
            return IsTransformUnderGalleryPaneSubtree(field.transform);
        }

        /// <summary>
        /// Desktop stay-expanded / opaque engagement (not pointer-only).
        /// Pointer hover OR owned text focus OR modal work chrome.
        /// Modeless floats (Settings / Plugins / Import / Filter presets) do not pin the pane —
        /// open alone never blocks AH (Galitz: float ≠ mode). Typing in a float still engages
        /// via <see cref="IsGalleryOwnedTextFocusActive"/>.
        /// Norman locus of control + MacKenzie focus ownership + Shneiderman no surprise collapse mid-task.
        /// Warm path — no alloc; safe on Unity 2018 Mono.
        /// </summary>
        private bool IsGalleryInteractionEngaged()
        {
            if (hoverCount > 0) return true;
            if (isResizing) return true;
            if (IsPointerInsideGalleryWindowRect()) return true;

            try
            {
                if (UIDraggableItem.IsDragging) return true;
            }
            catch { }

            // Modal / blocking chrome only — not modeless floats.
            if (IsConfirmOverlayOpen()) return true;
            if (_commandPaletteOpen) return true;
            if (_inAppHelpOpen) return true;
            // Popup open before/while field focus lands — avoid AH race on Ctrl+F.
            if (_titleSearchPopupOpen) return true;

            if (IsGalleryOwnedTextFocusActive()) return true;

            return false;
        }

        /// <summary>
        /// Blur InputField, close title-search popup, ensure grid selection for arrow nav.
        /// </summary>
        internal void FocusGridFromKeyboard(bool showStatus)
        {
            if (!IsVisible || isCollapsed) return;

            try { CloseTitleSearchPopup(); } catch { }
            BlurKeyboardInputField();

            try { DetailStripUnlockAfterExternalSelectionChange(); } catch { }
            EnsureKeyboardGridSelection();

            if (!showStatus) return;
            if (Time.unscaledTime < _keyboardFocusStatusUntil) return;
            _keyboardFocusStatusUntil = Time.unscaledTime + 1.6f;
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.keyboard.grid_focus",
                        "Grid — arrows move · Enter applies · Tab / Ctrl+F search"),
                    1.6f);
            }
            catch { }
        }

        /// <summary>
        /// Settings list focus: commit live side filter, blur field, select first/visible row.
        /// Never opens package detail strip.
        /// </summary>
        internal void FocusSettingsListFromKeyboard(bool showStatus)
        {
            if (!IsVisible || isCollapsed) return;
            if (!IsSettingsPanelOpen()) return;

            CommitSettingsSideSearchIntoFilter();
            try { CloseTitleSearchPopup(); } catch { }
            try
            {
                InputField side = GetSettingsSideSearchInput();
                if (side != null) side.DeactivateInputField();
            }
            catch { }
            BlurKeyboardInputField();

            if (!showStatus) return;
            if (Time.unscaledTime < _keyboardFocusStatusUntil) return;
            _keyboardFocusStatusUntil = Time.unscaledTime + 1.6f;
            try
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.keyboard.settings_list_focus",
                        "Settings — Tab / Ctrl+F filter · Save/Cancel on window"),
                    1.6f);
            }
            catch { }
        }

        /// <summary>
        /// Ctrl+F while Settings float expanded: focus settings filter (not title-search popup).
        /// </summary>
        private void FocusSettingsSideSearchFromHotkey()
        {
            if (!IsVisible || isCollapsed) return;
            if (!IsSettingsPanelOpen() || _settingsFloatCollapsed) return;

            try { CloseTitleSearchPopup(); } catch { }
            try { SyncSettingsSideSearchInputFromFilter(); } catch { }

            InputField side = GetSettingsSideSearchInput();
            if (side == null) return;
            try
            {
                if (_settingsFloatFilterRow != null && !_settingsFloatFilterRow.activeSelf)
                    _settingsFloatFilterRow.SetActive(true);
                if (!side.gameObject.activeSelf)
                    side.gameObject.SetActive(true);
            }
            catch { }

            FocusTitleSearchInputField(side, selectAll: true);
        }

        /// <summary>
        /// Push side-rail text into <see cref="settingsFilter"/> without letting an empty
        /// unfocused field wipe a live filter (layout/ConfigChanged races).
        /// </summary>
        private void CommitSettingsSideSearchIntoFilter()
        {
            try
            {
                InputField side = GetSettingsSideSearchInput();
                if (side == null) return;
                string live = side.text ?? "";
                bool focused = false;
                try { focused = side.isFocused; } catch { focused = false; }
                if (focused)
                {
                    settingsFilter = live;
                    return;
                }
                if (!string.IsNullOrEmpty(live.Trim()))
                    settingsFilter = live;
            }
            catch { }
        }

        private void BlurKeyboardInputField()
        {
            try
            {
                if (EventSystem.current == null) return;
                GameObject sel = EventSystem.current.currentSelectedGameObject;
                if (sel != null)
                {
                    InputField field = sel.GetComponent<InputField>();
                    if (field != null)
                    {
                        try { field.DeactivateInputField(); } catch { }
                    }
                }
                EventSystem.current.SetSelectedGameObject(null);
            }
            catch { }
        }

        /// <summary>
        /// Pick visible grid item if none selected so arrow keys have an anchor.
        /// </summary>
        private void EnsureKeyboardGridSelection()
        {
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0)
                return;

            bool historyBrowse = activeContentType == ContentType.History;
            int idx = -1;

            string navKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse);
            if (!string.IsNullOrEmpty(navKey))
                idx = FindIndexBySelectionIdentity(currentFilteredFiles, navKey, historyBrowse);

            if (idx < 0 && !string.IsNullOrEmpty(selectedPath))
                idx = FindIndexBySelectionIdentity(currentFilteredFiles, selectedPath, historyBrowse);

            bool hadVisibleAnchor = idx >= 0;
            if (idx < 0)
                idx = 0;

            FileEntry file = currentFilteredFiles[idx];
            if (file == null) return;

            bool selectionEmpty = selectedFiles == null || selectedFiles.Count == 0;
            if (selectionEmpty || !hadVisibleAnchor)
            {
                if (selectedFiles != null) selectedFiles.Clear();
                if (selectedFilePaths != null) selectedFilePaths.Clear();
                AddFileToSelection(file, historyBrowse);
                SetSelectionAnchor(file, historyBrowse);
                selectedPath = historyBrowse ? GetSelectionIdentityKey(file, true) : file.Path;
                SetHoverPath(file);
            }

            if (recyclingGrid != null)
                recyclingGrid.EnsureItemVisible(hadVisibleAnchor ? idx : 0);

            // Legacy middle-pane settings only — float Settings never rewrites grid selection chrome.
            if (settingsListViewActive)
            {
                try { DetailStripHide(); } catch { }
                RefreshSelectionVisualsCore(runHeavySideEffects: false);
                try { UpdatePaginationText(); } catch { }
                return;
            }

            RefreshSelectionVisuals();
            try { UpdatePaginationText(); } catch { }
        }

        /// <summary>
        /// Up from first list/grid row returns to search (closed keyboard loop).
        /// Settings → side filter; browse → title search.
        /// </summary>
        private bool TryKeyboardUpToTitleSearch(int currentIndex, int move, int moveH, bool shift, bool ctrl)
        {
            if (move >= 0 || moveH != 0) return false;
            if (shift || ctrl) return false;
            if (currentIndex > 0) return false;
            // Legacy middle-pane settings → side filter. Float Settings never steals grid Up.
            if (settingsListViewActive)
            {
                try { FocusSettingsSideSearchFromHotkey(); } catch { }
                return true;
            }
            try { FocusTitleSearchFromHotkey(); } catch { }
            return true;
        }
    }
}
