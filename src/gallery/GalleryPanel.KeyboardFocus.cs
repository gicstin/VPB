using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
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

        private bool TryHandleKeyboardFocusTransfer()
        {
            if (!IsVisible || isCollapsed) return false;
            if (_commandPaletteOpen) return false;
            if (_inAppHelpOpen) return false;

            bool tab = Input.GetKeyDown(KeyCode.Tab);
            bool down = Input.GetKeyDown(KeyCode.DownArrow);
            if (!tab && !down) return false;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (ctrl || alt) return false;

            if (settingsListViewActive)
                return TryHandleSettingsKeyboardFocusTransfer(tab, down);
            if (IsSettingsPanelOpen() && IsKeyboardFocusInsideSettingsFloat())
                return TryHandleSettingsKeyboardFocusTransfer(tab, down);

            GalleryKeyboardFocusRegion region = ResolveKeyboardFocusRegion();

            if (region == GalleryKeyboardFocusRegion.TitleSearch)
            {
                if (tab || down)
                {
                    FocusGridFromKeyboard(showStatus: true);
                    return true;
                }
            }
            else if (region == GalleryKeyboardFocusRegion.OtherInput)
            {
                if (tab)
                {
                    FocusGridFromKeyboard(showStatus: true);
                    return true;
                }
            }
            else
            {
                if (tab)
                {
                    try { FocusTitleSearchFromHotkey(); } catch { }
                    return true;
                }
            }

            return false;
        }

        private bool TryHandleSettingsKeyboardFocusTransfer(bool tab, bool down)
        {
            GalleryKeyboardFocusRegion region = ResolveKeyboardFocusRegion();

            if (region == GalleryKeyboardFocusRegion.SettingsSideSearch
                || region == GalleryKeyboardFocusRegion.TitleSearch)
            {
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

        /// <summary>Pane engaged by hover, owned text focus, or modal chrome; modeless floats do not pin. No alloc.</summary>
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

        /// <summary>Push side-rail text into settingsFilter without letting an empty unfocused field wipe a live filter (layout/ConfigChanged races).</summary>
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
            try { EnsureGridSelectionFullyVisible(hadVisibleAnchor ? idx : 0); } catch { }
            try { UpdatePaginationText(); } catch { }
        }

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
