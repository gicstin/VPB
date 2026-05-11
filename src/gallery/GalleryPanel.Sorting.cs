using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly int SortTypeCount = Enum.GetValues(typeof(SortType)).Length;

        private void ApplyHistorySortPresetForMode(GalleryHistoryFilterMode mode)
        {
            SortState st = GetSortState("Files");
            if (st == null) return;

            if (mode == GalleryHistoryFilterMode.MostUsed)
            {
                st.Type = SortType.UsageCount;
                st.Direction = SortDirection.Descending;
                ShowTemporaryStatus(VPBTranslation.T("gallery.history.sort_preset_most_used", "History sort preset: Usage count (desc)."), 1.8f);
            }
            else
            {
                st.Type = SortType.Date;
                st.Direction = SortDirection.Descending;
                ShowTemporaryStatus(VPBTranslation.T("gallery.history.sort_preset_recent", "History sort preset: Last used (desc)."), 1.8f);
            }

            SaveSortState("Files", st);
            UpdateSortButtonText(fileSortTypeText, fileSortDirText, st);
        }

        private void ToggleRatingSort()
        {
            isRatingSortToggleEnabled = !isRatingSortToggleEnabled;
            SyncRatingSortToggleState();
            if (IsFilterActive)
            {
                try { ApplySearchWithinFilter(nameFilter); } catch { }
                return;
            }
            RefreshFiles();
        }

        private void SyncRatingSortToggleState()
        {
            if (ratingSortToggleBtnText != null)
            {
                ratingSortToggleBtnText.color = isRatingSortToggleEnabled ? Color.green : Color.white;
            }
            if (ratingSortIconImage != null)
            {
                Sprite target = isRatingSortToggleEnabled ? ratingStarOffSprite : ratingStarNormalSprite;
                if (target != null) ratingSortIconImage.sprite = target;
            }
        }

        private SortState GetSortState(string context)
        {
            if (!contentSortStates.TryGetValue(context, out SortState state) || state == null)
            {
                state = GallerySortManager.Instance.GetDefaultSortState(context);
                contentSortStates[context] = state;
            }
            return state;
        }

        // Overload: Old method for backward compatibility
        private void CycleSort(string context, Text buttonText)
        {
            CycleSort(context, buttonText, null);
        }

        private void CycleSort(string context, Text typeText, Text dirText)
        {
            var state = GetSortState(context);
            int currentType = (int)state.Type;

            SortType nextType = state.Type;
            do
            {
                currentType = (currentType + 1) % SortTypeCount;
                nextType = (SortType)currentType;
            } while (!IsSortTypeValid(context, nextType));

            CommitSortTypeChange(context, nextType, typeText, dirText);
        }

        /// <summary>Applies a new sort type with the same default directions and refresh behavior as <see cref="CycleSort"/>.</summary>
        private void CommitSortTypeChange(string context, SortType newType, Text typeText, Text dirText)
        {
            if (!IsSortTypeValid(context, newType)) return;

            var state = GetSortState(context);
            SortType prevType = state.Type;
            state.Type = newType;
            if (state.Type == SortType.Name || state.Type == SortType.HiddenOnly || state.Type == SortType.AutoInstallOnly || state.Type == SortType.LoadedOnly || state.Type == SortType.UnloadedOnly || state.Type == SortType.UnusedOnly)
                state.Direction = SortDirection.Ascending;
            else
                state.Direction = SortDirection.Descending;

            SaveSortState(context, state);
            UpdateSortButtonText(typeText, dirText, state);

            if (context == "Files")
            {
                if (IsFilterActive)
                {
                    try
                    {
                        if (activeContentType == ContentType.History)
                        {
                            RefreshHistoryListInPlace(true);
                        }
                        else
                        {
                            if (filterSearchBaseFiles != null)
                            {
                                List<FileEntry> rebuilt = BuildFilterModeView(filterSearchBaseFiles, filterSearchLower);
                                currentFilteredFiles.Clear();
                                currentFilteredFiles.AddRange(rebuilt);
                            }
                            ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, state.Type);
                            GallerySortManager.Instance.SortFiles(currentFilteredFiles, state);
                            if (recyclingGrid != null)
                            {
                                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                                recyclingGrid.Refresh();
                            }
                            ScrollGalleryToTop();
                            UpdatePaginationText();
                        }
                    }
                    catch { }
                }
                else
                {
                    bool prevExclusive =
                        prevType == SortType.HiddenOnly ||
                        prevType == SortType.AutoInstallOnly ||
                        prevType == SortType.LoadedOnly ||
                        prevType == SortType.UnloadedOnly ||
                        prevType == SortType.UnusedOnly;
                    bool nextExclusive =
                        newType == SortType.HiddenOnly ||
                        newType == SortType.AutoInstallOnly ||
                        newType == SortType.LoadedOnly ||
                        newType == SortType.UnloadedOnly ||
                        newType == SortType.UnusedOnly;

                    // Exclusive "only" modes prune the list in-place. Switching to/from them must rebuild the base list,
                    // otherwise the user can't "clear" the mode without changing categories.
                    if (prevExclusive || nextExclusive)
                    {
                        RefreshFiles(keepScroll: false);
                    }
                    else
                    {
                        if (!TryReapplyFilesSortWithoutFullRefresh())
                            RefreshFiles();
                    }
                }
            }
            else UpdateTabs();
        }

        /// <summary>
        /// Re-sorts the loaded file list and refreshes the recycling grid without re-running
        /// <see cref="GalleryPanel.RefreshFiles"/> (package scan / coroutine), then resets to top.
        /// </summary>
        private bool TryReapplyFilesSortWithoutFullRefresh()
        {
            if (IsHubMode) return false;
            if (!hasLoadedContent || currentFilteredFiles == null || recyclingGrid == null) return false;
            if (refreshCoroutine != null) return false;

            try
            {
                if (activeContentType == ContentType.History)
                {
                    RefreshHistoryListInPlace(true);
                    return true;
                }

                SortState st = GetSortState("Files");
                ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);
                GallerySortManager.Instance.SortFiles(currentFilteredFiles, st);

                if (lastFilteredFiles != null)
                {
                    lastFilteredFiles.Clear();
                    lastFilteredFiles.AddRange(currentFilteredFiles);
                }

                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                recyclingGrid.Refresh();
                ScrollGalleryToTop();

                UpdatePaginationText();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly SortType[] FileSortDropdownOrder =
        {
            SortType.Name, SortType.Date, SortType.DateCreated,
            SortType.DateAdded, SortType.DateUpdated,
            SortType.Size, SortType.Rating,
            SortType.UsageCount,
            SortType.UnusedOnly,
            SortType.Deps, SortType.Dependents, SortType.Missing,
            SortType.Hidden, SortType.HiddenOnly, SortType.AutoInstall, SortType.AutoInstallOnly, SortType.LoadedOnly, SortType.UnloadedOnly
        };

        private static string FileSortTypeFullLabel(SortType type)
        {
            switch (type)
            {
                case SortType.Name: return VPBTranslation.T("gallery.sort.full.name", "Alphabetical (name)");
                case SortType.Date: return VPBTranslation.T("gallery.sort.full.date", "Date modified");
                case SortType.DateCreated: return VPBTranslation.T("gallery.sort.full.date_created", "Date created");
                case SortType.DateAdded: return VPBTranslation.T("gallery.sort.full.date_added", "Date added (New)");
                case SortType.DateUpdated: return VPBTranslation.T("gallery.sort.full.date_updated", "Date updated");
                case SortType.Size: return VPBTranslation.T("gallery.sort.full.size", "File size");
                case SortType.Rating: return VPBTranslation.T("gallery.sort.full.rating", "Rating");
                case SortType.UsageCount: return VPBTranslation.T("gallery.sort.full.usage_count", "Usage count");
                case SortType.UnusedOnly: return VPBTranslation.T("gallery.sort.full.unused_only", "Unused (only)");
                case SortType.Deps: return VPBTranslation.T("gallery.sort.full.deps", "Dependencies");
                case SortType.Dependents: return VPBTranslation.T("gallery.sort.full.dependents", "Dependents");
                case SortType.Missing: return VPBTranslation.T("gallery.sort.full.missing", "Missing dependencies");
                case SortType.Hidden: return VPBTranslation.T("gallery.sort.full.hidden", "Hidden");
                case SortType.HiddenOnly: return VPBTranslation.T("gallery.sort.full.hidden_only", "Hidden (only)");
                case SortType.AutoInstall: return VPBTranslation.T("gallery.sort.full.autoinstall", "Auto Install");
                case SortType.AutoInstallOnly: return VPBTranslation.T("gallery.sort.full.autoinstall_only", "Auto Install (only)");
                case SortType.LoadedOnly: return VPBTranslation.T("gallery.sort.full.loaded_only", "All Loaded");
                case SortType.UnloadedOnly: return VPBTranslation.T("gallery.sort.full.unloaded_only", "All Unloaded");
                default: return type.ToString();
            }
        }

        private void SetupFileSortTypeMenu()
        {
            if (fileSortTypeMenuRoot != null || backgroundBoxGO == null) return;

            fileSortTypeMenuRoot = new GameObject("FileSortTypeMenu");
            fileSortTypeMenuRoot.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRT = fileSortTypeMenuRoot.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            GameObject backdropGO = new GameObject("Backdrop");
            backdropGO.transform.SetParent(fileSortTypeMenuRoot.transform, false);
            RectTransform backdropRT = backdropGO.AddComponent<RectTransform>();
            backdropRT.anchorMin = Vector2.zero;
            backdropRT.anchorMax = Vector2.one;
            backdropRT.offsetMin = Vector2.zero;
            backdropRT.offsetMax = Vector2.zero;
            Image backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0.001f);
            backdropImg.raycastTarget = true;
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(CloseFileSortTypeMenu);

            fileSortTypeMenuPanelGO = new GameObject("FileSortTypeMenuPanel");
            fileSortTypeMenuPanelGO.transform.SetParent(fileSortTypeMenuRoot.transform, false);
            RectTransform panelRT = fileSortTypeMenuPanelGO.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 1f);
            panelRT.anchorMax = new Vector2(0.5f, 1f);
            panelRT.pivot = new Vector2(0.5f, 1f);
            panelRT.anchoredPosition = new Vector2(108f, -72f);
            panelRT.sizeDelta = new Vector2(260f, 50f);

            Image panelImg = fileSortTypeMenuPanelGO.AddComponent<Image>();
            panelImg.color = new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f);

            VerticalLayoutGroup vlg = fileSortTypeMenuPanelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childAlignment = TextAnchor.UpperCenter;

            ContentSizeFitter csf = fileSortTypeMenuPanelGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            fileSortTypeMenuRoot.SetActive(false);
            RebuildFileSortTypeMenuOptions();
        }

        private static string SidePaneSortFullLabel(SortType type, SortDirection dir)
        {
            // Side panes generally only support Name / Count; include direction in the label.
            if (type == SortType.Name)
            {
                return dir == SortDirection.Ascending
                    ? VPBTranslation.T("gallery.sort.full.name_az", "Name (A→Z)")
                    : VPBTranslation.T("gallery.sort.full.name_za", "Name (Z→A)");
            }
            if (type == SortType.Count)
            {
                return dir == SortDirection.Ascending
                    ? VPBTranslation.T("gallery.sort.full.count_low_high", "Count (low→high)")
                    : VPBTranslation.T("gallery.sort.full.count_high_low", "Count (high→low)");
            }
            return type.ToString() + " " + (dir == SortDirection.Ascending ? "↑" : "↓");
        }

        private void SetupSidePaneSortMenu()
        {
            if (sidePaneSortMenuRoot != null || backgroundBoxGO == null) return;

            sidePaneSortMenuRoot = new GameObject("SidePaneSortMenu");
            sidePaneSortMenuRoot.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRT = sidePaneSortMenuRoot.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            GameObject backdropGO = new GameObject("Backdrop");
            backdropGO.transform.SetParent(sidePaneSortMenuRoot.transform, false);
            RectTransform backdropRT = backdropGO.AddComponent<RectTransform>();
            backdropRT.anchorMin = Vector2.zero;
            backdropRT.anchorMax = Vector2.one;
            backdropRT.offsetMin = Vector2.zero;
            backdropRT.offsetMax = Vector2.zero;
            Image backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0.001f);
            backdropImg.raycastTarget = true;
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(CloseSidePaneSortMenu);

            sidePaneSortMenuPanelGO = new GameObject("SidePaneSortMenuPanel");
            sidePaneSortMenuPanelGO.transform.SetParent(sidePaneSortMenuRoot.transform, false);
            sidePaneSortMenuPanelRT = sidePaneSortMenuPanelGO.AddComponent<RectTransform>();
            // Anchors / position are set at open-time based on which button was clicked.
            sidePaneSortMenuPanelRT.anchorMin = new Vector2(0f, 1f);
            sidePaneSortMenuPanelRT.anchorMax = new Vector2(0f, 1f);
            sidePaneSortMenuPanelRT.pivot = new Vector2(0f, 1f);
            sidePaneSortMenuPanelRT.anchoredPosition = new Vector2(10f, -95f);
            sidePaneSortMenuPanelRT.sizeDelta = new Vector2(240f, 50f);

            Image panelImg = sidePaneSortMenuPanelGO.AddComponent<Image>();
            panelImg.color = new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f);

            VerticalLayoutGroup vlg = sidePaneSortMenuPanelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childAlignment = TextAnchor.UpperCenter;

            ContentSizeFitter csf = sidePaneSortMenuPanelGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sidePaneSortMenuRoot.SetActive(false);
        }

        private void CloseSidePaneSortMenu()
        {
            if (sidePaneSortMenuRoot != null)
                sidePaneSortMenuRoot.SetActive(false);
            sidePaneSortMenuContext = null;
        }

        private void RebuildSidePaneSortMenuOptions(string context)
        {
            if (sidePaneSortMenuPanelGO == null) return;
            if (string.IsNullOrEmpty(context)) return;

            Transform panel = sidePaneSortMenuPanelGO.transform;
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

            SortState current = GetSortState(context);
            if (current == null) current = new SortState(SortType.Name, SortDirection.Ascending);

            // Side panes: offer explicit 4 choices (Name asc/desc, Count asc/desc) when valid.
            SortType[] optTypes = new SortType[] { SortType.Name, SortType.Name, SortType.Count, SortType.Count };
            SortDirection[] optDirs = new SortDirection[] { SortDirection.Ascending, SortDirection.Descending, SortDirection.Ascending, SortDirection.Descending };
            for (int oi = 0; oi < optTypes.Length && oi < optDirs.Length; oi++)
            {
                SortType optType = optTypes[oi];
                SortDirection optDir = optDirs[oi];
                if (!IsSortTypeValid(context, optType)) continue;

                bool isCurrent = current.Type == optType && current.Direction == optDir;
                string label = (isCurrent ? "\u2713  " : "    ") + SidePaneSortFullLabel(optType, optDir);
                SortType capturedType = optType;
                SortDirection capturedDir = optDir;

                GameObject row = UI.CreateUIButton(
                    sidePaneSortMenuPanelGO, 228, 34, label, 14, 0, 0,
                    AnchorPresets.middleCenter,
                    () =>
                    {
                        if (SupportsSidePaneFourModeSort(context))
                            ApplySidePaneFourModeSort(context, capturedType, capturedDir);
                        else
                        {
                            SortState st = GetSortState(context);
                            st.Type = capturedType;
                            st.Direction = capturedDir;
                            SaveSortState(context, st);
                            UpdateTabs();
                        }
                        CloseSidePaneSortMenu();
                    });

                Image rowImg = row.GetComponent<Image>();
                rowImg.color = isCurrent ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;

                Text rowT = row.GetComponentInChildren<Text>();
                if (rowT != null)
                {
                    rowT.color = UI.PopupText;
                    rowT.fontStyle = isCurrent ? FontStyle.Bold : FontStyle.Normal;
                    rowT.alignment = TextAnchor.MiddleLeft;
                    VPBUiFont.ApplyTo(rowT);
                }

                LayoutElement le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 36f;
                le.flexibleWidth = 1f;
            }
        }

        private void ToggleSidePaneSortMenu(string context, RectTransform anchorButtonRT)
        {
            if (sidePaneSortMenuRoot == null) SetupSidePaneSortMenu();
            if (sidePaneSortMenuRoot == null || sidePaneSortMenuPanelRT == null) return;

            if (sidePaneSortMenuRoot.activeSelf && string.Equals(sidePaneSortMenuContext ?? "", context ?? "", StringComparison.OrdinalIgnoreCase))
            {
                CloseSidePaneSortMenu();
                return;
            }

            sidePaneSortMenuContext = context ?? "";
            RebuildSidePaneSortMenuOptions(sidePaneSortMenuContext);

            // Position directly under the clicked sort button.
            bool isRight = false;
            try { isRight = anchorButtonRT != null && anchorButtonRT.anchorMin.x > 0.5f; } catch { isRight = false; }
            float gapY = 6f;
            float sc = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            Vector2 btnPos = anchorButtonRT != null ? anchorButtonRT.anchoredPosition : new Vector2(10f, -65f * sc);
            Vector2 btnSize = anchorButtonRT != null ? anchorButtonRT.sizeDelta : new Vector2(35f, 35f);

            sidePaneSortMenuPanelRT.anchorMin = sidePaneSortMenuPanelRT.anchorMax = (anchorButtonRT != null ? anchorButtonRT.anchorMin : new Vector2(0f, 1f));
            sidePaneSortMenuPanelRT.pivot = isRight ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            sidePaneSortMenuPanelRT.anchoredPosition = btnPos + new Vector2(0f, -(btnSize.y + gapY));

            sidePaneSortMenuRoot.SetActive(true);
            sidePaneSortMenuRoot.transform.SetAsLastSibling();
        }

        private void ToggleFileSortTypeMenu()
        {
            if (fileSortTypeMenuRoot == null) SetupFileSortTypeMenu();
            if (fileSortTypeMenuRoot == null) return;

            if (fileSortTypeMenuRoot.activeSelf)
            {
                CloseFileSortTypeMenu();
                return;
            }

            RebuildFileSortTypeMenuOptions();
            fileSortTypeMenuRoot.SetActive(true);
            fileSortTypeMenuRoot.transform.SetAsLastSibling();
        }

        private void CloseFileSortTypeMenu()
        {
            if (fileSortTypeMenuRoot != null)
                fileSortTypeMenuRoot.SetActive(false);
        }

        private void RebuildFileSortTypeMenuOptions()
        {
            if (fileSortTypeMenuPanelGO == null) return;

            Transform panel = fileSortTypeMenuPanelGO.transform;
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

            SortState current = GetSortState("Files");

            foreach (SortType sortType in FileSortDropdownOrder)
            {
                if (!IsSortTypeValid("Files", sortType)) continue;

                bool isCurrent = current.Type == sortType;
                string label = (isCurrent ? "\u2713  " : "    ") + FileSortTypeFullLabel(sortType);
                SortType captured = sortType;

                GameObject row = UI.CreateUIButton(
                    fileSortTypeMenuPanelGO, 248, 36, label, 14, 0, 0,
                    AnchorPresets.middleCenter,
                    () =>
                    {
                        CommitSortTypeChange("Files", captured, fileSortTypeText, fileSortDirText);
                        CloseFileSortTypeMenu();
                    });

                Image rowImg = row.GetComponent<Image>();
                rowImg.color = isCurrent ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;

                Text rowT = row.GetComponentInChildren<Text>();
                if (rowT != null)
                {
                    rowT.color = UI.PopupText;
                    rowT.fontStyle = isCurrent ? FontStyle.Bold : FontStyle.Normal;
                    rowT.alignment = TextAnchor.MiddleLeft;
                    VPBUiFont.ApplyTo(rowT);
                }

                LayoutElement le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 38f;
                le.flexibleWidth = 1f;
            }

            AppendHideOldVersionsMenuRow();
        }

        // Bottom-of-menu toggle: applies globally to the Files gallery view (not a sort mode itself).
        private void AppendHideOldVersionsMenuRow()
        {
            bool on = false;
            try { on = Settings.Instance != null && Settings.Instance.HideOldVersions != null && Settings.Instance.HideOldVersions.Value; } catch { }
            string label = (on ? "\u2713  " : "    ") + VPBTranslation.T("gallery.sort.full.hide_old_versions", "Hide old versions (keep newest only)");

            GameObject row = UI.CreateUIButton(
                fileSortTypeMenuPanelGO, 248, 36, label, 14, 0, 0,
                AnchorPresets.middleCenter,
                () =>
                {
                    try
                    {
                        if (Settings.Instance != null && Settings.Instance.HideOldVersions != null)
                            Settings.Instance.HideOldVersions.Value = !Settings.Instance.HideOldVersions.Value;
                    }
                    catch { }
                    CloseFileSortTypeMenu();
                    try { RefreshFiles(); } catch { }
                });

            Image rowImg = row.GetComponent<Image>();
            rowImg.color = on ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;

            Text rowT = row.GetComponentInChildren<Text>();
            if (rowT != null)
            {
                rowT.color = UI.PopupText;
                rowT.fontStyle = on ? FontStyle.Bold : FontStyle.Normal;
                rowT.alignment = TextAnchor.MiddleLeft;
                VPBUiFont.ApplyTo(rowT);
            }

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 38f;
            le.flexibleWidth = 1f;
        }

        private void ToggleFileSortDirection()
        {
            SortState st = GetSortState("Files");
            SortDirection next = st.Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
            CommitSortDirectionChange("Files", next, fileSortTypeText, fileSortDirText);
        }

        private void CommitSortDirectionChange(string context, SortDirection dir, Text typeText, Text dirText)
        {
            var state = GetSortState(context);
            state.Direction = dir;
            SaveSortState(context, state);
            UpdateSortButtonText(typeText, dirText, state);

            if (context == "Files")
            {
                if (IsFilterActive)
                {
                    try
                    {
                        if (activeContentType == ContentType.History)
                            RefreshHistoryListInPlace(true);
                        else
                        {
                            GallerySortManager.Instance.SortFiles(currentFilteredFiles, state);
                            if (recyclingGrid != null)
                            {
                                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                                recyclingGrid.Refresh();
                            }
                            ScrollGalleryToTop();
                            UpdatePaginationText();
                        }
                    }
                    catch { }
                }
                else
                {
                    if (!TryReapplyFilesSortWithoutFullRefresh())
                        RefreshFiles();
                }
            }
            else UpdateTabs();
        }

        private bool IsSortTypeValid(string context, SortType type)
        {
            if (context == "Files")
            {
                return type == SortType.Name || type == SortType.Date || type == SortType.DateCreated
                    || type == SortType.DateAdded || type == SortType.DateUpdated
                    || type == SortType.Size || type == SortType.Rating || type == SortType.Deps || type == SortType.Dependents || type == SortType.Missing
                    || type == SortType.UsageCount
                    || type == SortType.UnusedOnly
                    || type == SortType.Hidden || type == SortType.HiddenOnly || type == SortType.AutoInstall || type == SortType.AutoInstallOnly || type == SortType.LoadedOnly || type == SortType.UnloadedOnly;
            }
            else if (context == "Category" || context == "Creator" || context == "Path" || context == "UserTags" || context == "UserTagsApplied" || context == "Status" || context == "Tags" || context == "Hub" || context == "SceneSource")
            {
                return type == SortType.Name || type == SortType.Count;
            }
            return false;
        }

        private static bool SupportsSidePaneFourModeSort(string context)
        {
            return context == "Category" || context == "Creator" || context == "Path" || context == "UserTags" || context == "UserTagsApplied" || context == "Status" || context == "Tags" || context == "Hub";
        }

        /// <summary>Upper side pane: name A→Z, name Z→A, count low→high, count high→low (same icons as scene file sort).</summary>
        private static void SidePaneFourModeToState(int mode, out SortType type, out SortDirection dir)
        {
            switch (mode)
            {
                case 1: type = SortType.Name; dir = SortDirection.Descending; break;
                case 2: type = SortType.Count; dir = SortDirection.Ascending; break;
                case 3: type = SortType.Count; dir = SortDirection.Descending; break;
                default: type = SortType.Name; dir = SortDirection.Ascending; break;
            }
        }

        private static int TryGetSidePaneFourModeIndex(SortState st)
        {
            if (st == null) return -1;
            if (st.Type == SortType.Name && st.Direction == SortDirection.Ascending) return 0;
            if (st.Type == SortType.Name && st.Direction == SortDirection.Descending) return 1;
            if (st.Type == SortType.Count && st.Direction == SortDirection.Ascending) return 2;
            if (st.Type == SortType.Count && st.Direction == SortDirection.Descending) return 3;
            return -1;
        }

        private void CycleSidePaneTopSort(bool isLeft)
        {
            ContentType? ct = isLeft ? leftActiveContent : rightActiveContent;
            if (!ct.HasValue) return;
            string ctx = ct.Value.ToString();
            if (!SupportsSidePaneFourModeSort(ctx))
            {
                Text t = isLeft ? leftSortBtnText : rightSortBtnText;
                CycleSort(ctx, t);
                return;
            }
            SortState st = GetSortState(ctx);
            int i = TryGetSidePaneFourModeIndex(st);
            int next = (i < 0) ? 0 : (i + 1) % 4;
            SidePaneFourModeToState(next, out SortType ty, out SortDirection d);
            ApplySidePaneFourModeSort(ctx, ty, d);
        }

        /// <summary>Lower split row (tags / hub tags): same 4-mode cycle as upper pane, persisted as <c>Tags</c>.</summary>
        private void CycleSidePaneSubTagSort()
        {
            const string ctx = "Tags";
            SortState st = GetSortState(ctx);
            int i = TryGetSidePaneFourModeIndex(st);
            int next = (i < 0) ? 0 : (i + 1) % 4;
            SidePaneFourModeToState(next, out SortType ty, out SortDirection d);
            ApplySidePaneFourModeSort(ctx, ty, d);
        }

        private void ApplySidePaneFourModeSort(string context, SortType type, SortDirection direction)
        {
            if (!IsSortTypeValid(context, type)) return;
            SortState state = GetSortState(context);
            state.Type = type;
            state.Direction = direction;
            SaveSortState(context, state);
            UpdateTabs();
        }

        /// <summary>Upper side sort + tag sub-row: one place for vpb_icons vs legacy text.</summary>
        private void SyncSidePaneTopSortButtonVisuals()
        {
            if (leftSortBtn != null && leftActiveContent.HasValue)
            {
                string ctx = leftActiveContent.Value.ToString();
                SyncSidePaneFourModeSortButtonVisual(leftSortBtnBackdrop, leftSortBtnIconImage, leftSortBtnText, GetSortState(ctx), SupportsSidePaneFourModeSort(ctx));
            }
            if (rightSortBtn != null && rightActiveContent.HasValue)
            {
                string ctx = rightActiveContent.Value.ToString();
                SyncSidePaneFourModeSortButtonVisual(rightSortBtnBackdrop, rightSortBtnIconImage, rightSortBtnText, GetSortState(ctx), SupportsSidePaneFourModeSort(ctx));
            }

            if (leftSubSortBtn != null && leftSubSortBtn.activeSelf)
            {
                string subCtx = "Tags";
                if (leftActiveContent == ContentType.UserTags) subCtx = "UserTagsApplied";
                SortState tagSt = GetSortState(subCtx);
                bool tagIcon = SupportsSidePaneFourModeSort(subCtx);
                SyncSidePaneFourModeSortButtonVisual(leftSubSortBtnBackdrop, leftSubSortBtnIconImage, leftSubSortBtnText, tagSt, tagIcon);
            }
            if (rightSubSortBtn != null && rightSubSortBtn.activeSelf)
            {
                string subCtxR = "Tags";
                if (rightActiveContent == ContentType.UserTags) subCtxR = "UserTagsApplied";
                SortState tagStR = GetSortState(subCtxR);
                bool tagIconR = SupportsSidePaneFourModeSort(subCtxR);
                SyncSidePaneFourModeSortButtonVisual(rightSubSortBtnBackdrop, rightSubSortBtnIconImage, rightSubSortBtnText, tagStR, tagIconR);
            }
        }

        private void SyncSidePaneFourModeSortButtonVisual(Image backdrop, Image iconImg, Text legacyText, SortState st, bool iconMode)
        {
            if (backdrop == null) return;
            if (iconMode && sceneSourceSortModeSprites != null && iconImg != null)
            {
                if (legacyText != null) legacyText.gameObject.SetActive(false);
                iconImg.gameObject.SetActive(true);
                int idx = TryGetSidePaneFourModeIndex(st);
                int spIdx = idx >= 0 ? idx : 0;
                Sprite sp = spIdx >= 0 && spIdx < sceneSourceSortModeSprites.Length ? sceneSourceSortModeSprites[spIdx] : null;
                if (sp != null)
                {
                    iconImg.sprite = sp;
                    iconImg.enabled = true;
                }
                else
                    iconImg.enabled = false;
                backdrop.color = idx >= 0 ? SceneSourceSortBtnActive : SceneSourceSortBtnIdle;
            }
            else
            {
                if (iconImg != null)
                {
                    iconImg.enabled = false;
                    iconImg.gameObject.SetActive(false);
                }
                if (legacyText != null)
                {
                    legacyText.gameObject.SetActive(true);
                    if (st != null) UpdateSortButtonText(legacyText, st);
                }
                backdrop.color = SceneSourceSortBtnIdle;
            }
        }

        // Overload: Old method for combined button
        private void UpdateSortButtonText(Text t, SortState state)
        {
            if (t == null) return;
            string symbol = "";
            switch(state.Type)
            {
                case SortType.Name: symbol = "Az"; break;
                case SortType.Date: symbol = "Dt"; break;
                case SortType.DateCreated: symbol = "Dc"; break;
                case SortType.DateAdded: symbol = "Nw"; break;
                case SortType.DateUpdated: symbol = "Up"; break;
                case SortType.Size: symbol = "Sz"; break;
                case SortType.Count: symbol = "#"; break;
                case SortType.Score: symbol = "Sc"; break;
                case SortType.Rating: symbol = "Rt"; break;
                case SortType.UsageCount: symbol = "Us"; break;
                case SortType.UnusedOnly: symbol = "U0"; break;
                case SortType.Deps: symbol = "Dp"; break;
                case SortType.Dependents: symbol = "Dn"; break;
                case SortType.Missing: symbol = "Ms"; break;
                case SortType.Hidden: symbol = "Hd"; break;
                case SortType.HiddenOnly: symbol = "HO"; break;
                case SortType.AutoInstall: symbol = "Ai"; break;
                case SortType.AutoInstallOnly: symbol = "AO"; break;
                case SortType.LoadedOnly: symbol = "LO"; break;
                case SortType.UnloadedOnly: symbol = "UO"; break;
            }
            string arrow = state.Direction == SortDirection.Ascending ? "↑" : "↓";
            t.text = symbol + arrow;
        }

        // New method: Update separate type and direction buttons
        private void UpdateSortButtonText(Text typeText, Text dirText, SortState state)
        {
            if (typeText != null)
            {
                string symbol = "";
                switch(state.Type)
                {
                    case SortType.Name: symbol = "Az"; break;
                    case SortType.Date: symbol = "Dt"; break;
                    case SortType.DateCreated: symbol = "Dc"; break;
                    case SortType.DateAdded: symbol = "Nw"; break;
                    case SortType.DateUpdated: symbol = "Up"; break;
                    case SortType.Size: symbol = "Sz"; break;
                    case SortType.Count: symbol = "#"; break;
                    case SortType.Score: symbol = "Sc"; break;
                    case SortType.Rating: symbol = "Rt"; break;
                    case SortType.UsageCount: symbol = "Us"; break;
                    case SortType.UnusedOnly: symbol = "U0"; break;
                    case SortType.Deps: symbol = "Dp"; break;
                    case SortType.Dependents: symbol = "Dn"; break;
                    case SortType.Missing: symbol = "Ms"; break;
                    case SortType.Hidden: symbol = "Hd"; break;
                    case SortType.HiddenOnly: symbol = "HO"; break;
                    case SortType.AutoInstall: symbol = "Ai"; break;
                    case SortType.AutoInstallOnly: symbol = "AO"; break;
                    case SortType.LoadedOnly: symbol = "LO"; break;
                    case SortType.UnloadedOnly: symbol = "UO"; break;
                }
                typeText.text = symbol;
            }
            if (dirText != null)
            {
                string arrow = state.Direction == SortDirection.Ascending ? "↑" : "↓";
                dirText.text = arrow;
            }

            // Swap sort-direction icon sprite
            if (fileSortDirIconImage != null)
            {
                Sprite target = state.Direction == SortDirection.Ascending ? fileSortDirAscSprite : fileSortDirDescSprite;
                if (target != null) fileSortDirIconImage.sprite = target;
            }
        }

        private void SaveSortState(string context, SortState state)
        {
            contentSortStates[context] = state;
            GallerySortManager.Instance.SaveSortState(context, state);
        }

        /// <summary>Scene sub-pane: cycle sort order of All/Addon/Custom tabs only (not main file list).</summary>
        private void CycleSceneSourceTabSort()
        {
            SortState st = GetSortState("SceneSource");
            int i = TryGetSidePaneFourModeIndex(st);
            int next = (i < 0) ? 0 : (i + 1) % 4;
            SidePaneFourModeToState(next, out SortType t, out SortDirection d);
            ApplySceneSourceTabSort(t, d);
        }

        private void ApplySceneSourceTabSort(SortType type, SortDirection direction)
        {
            if (!IsSortTypeValid("SceneSource", type)) return;
            SortState state = GetSortState("SceneSource");
            state.Type = type;
            state.Direction = direction;
            SaveSortState("SceneSource", state);
            SyncSceneSourceSortButtonHighlights();
            UpdateTabs();
        }

        private static readonly Color SceneSourceSortBtnIdle = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color SceneSourceSortBtnActive = new Color(0.15f, 0.30f, 0.52f, 1f);

        private void SyncSceneSourceSortButtonHighlights()
        {
            SortState st = GetSortState("SceneSource");
            int idx = TryGetSidePaneFourModeIndex(st);
            int spriteIdx = idx >= 0 ? idx : 0;
            Sprite sp = null;
            if (sceneSourceSortModeSprites != null && spriteIdx >= 0 && spriteIdx < sceneSourceSortModeSprites.Length)
                sp = sceneSourceSortModeSprites[spriteIdx];
            Color backdropCol = idx >= 0 ? SceneSourceSortBtnActive : SceneSourceSortBtnIdle;

            void ApplyOne(Image backdropImg, Image iconImg)
            {
                if (backdropImg != null) backdropImg.color = backdropCol;
                if (iconImg != null)
                {
                    if (sp != null)
                    {
                        iconImg.sprite = sp;
                        iconImg.enabled = true;
                    }
                    else
                        iconImg.enabled = false;
                }
            }

            ApplyOne(leftSubSceneSortBtnBackdrop, leftSubSceneSortIconImage);
            ApplyOne(rightSubSceneSortBtnBackdrop, rightSubSceneSortIconImage);
        }
    }
}
