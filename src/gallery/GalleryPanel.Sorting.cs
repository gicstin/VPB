using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel
    {
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
            if (!contentSortStates.ContainsKey(context))
            {
                contentSortStates[context] = GallerySortManager.Instance.GetDefaultSortState(context);
            }
            return contentSortStates[context];
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
            int maxType = Enum.GetNames(typeof(SortType)).Length;

            SortType nextType = state.Type;
            do
            {
                currentType = (currentType + 1) % maxType;
                nextType = (SortType)currentType;
            } while (!IsSortTypeValid(context, nextType));

            CommitSortTypeChange(context, nextType, typeText, dirText);
        }

        /// <summary>Applies a new sort type with the same default directions and refresh behavior as <see cref="CycleSort"/>.</summary>
        private void CommitSortTypeChange(string context, SortType newType, Text typeText, Text dirText)
        {
            if (!IsSortTypeValid(context, newType)) return;

            var state = GetSortState(context);
            state.Type = newType;
            if (state.Type == SortType.Name || state.Type == SortType.HiddenOnly || state.Type == SortType.AutoInstallOnly)
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
                        UpdatePaginationText();
                    }
                    catch { }
                }
                else
                {
                    RefreshFiles();
                }
            }
            else UpdateTabs();
        }

        private static readonly SortType[] FileSortDropdownOrder =
        {
            SortType.Name, SortType.Date, SortType.DateCreated, SortType.Size, SortType.Rating,
            SortType.Deps, SortType.Dependents, SortType.Missing,
            SortType.Hidden, SortType.HiddenOnly, SortType.AutoInstall, SortType.AutoInstallOnly
        };

        private static string FileSortTypeFullLabel(SortType type)
        {
            switch (type)
            {
                case SortType.Name: return VPBTranslation.T("gallery.sort.full.name", "Alphabetical (name)");
                case SortType.Date: return VPBTranslation.T("gallery.sort.full.date", "Date modified");
                case SortType.DateCreated: return VPBTranslation.T("gallery.sort.full.date_created", "Date created");
                case SortType.Size: return VPBTranslation.T("gallery.sort.full.size", "File size");
                case SortType.Rating: return VPBTranslation.T("gallery.sort.full.rating", "Rating");
                case SortType.Deps: return VPBTranslation.T("gallery.sort.full.deps", "Dependencies");
                case SortType.Dependents: return VPBTranslation.T("gallery.sort.full.dependents", "Dependents");
                case SortType.Missing: return VPBTranslation.T("gallery.sort.full.missing", "Missing dependencies");
                case SortType.Hidden: return VPBTranslation.T("gallery.sort.full.hidden", "Hidden");
                case SortType.HiddenOnly: return VPBTranslation.T("gallery.sort.full.hidden_only", "Hidden (only)");
                case SortType.AutoInstall: return VPBTranslation.T("gallery.sort.full.autoinstall", "Auto Install");
                case SortType.AutoInstallOnly: return VPBTranslation.T("gallery.sort.full.autoinstall_only", "Auto Install (only)");
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
            panelImg.color = new Color(0.09f, 0.09f, 0.16f, 0.97f);
            var outline = fileSortTypeMenuPanelGO.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            outline.effectDistance = new Vector2(1f, -1f);

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
                rowImg.color = isCurrent
                    ? new Color(0.15f, 0.30f, 0.52f, 1f)
                    : new Color(0.16f, 0.16f, 0.24f, 1f);

                Text rowT = row.GetComponentInChildren<Text>();
                if (rowT != null)
                {
                    rowT.color = isCurrent ? Color.white : new Color(0.82f, 0.82f, 0.92f, 1f);
                    rowT.fontStyle = isCurrent ? FontStyle.Bold : FontStyle.Normal;
                    rowT.alignment = TextAnchor.MiddleLeft;
                    VPBUiFont.ApplyTo(rowT);
                }

                LayoutElement le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 38f;
                le.flexibleWidth = 1f;
            }
        }

        // Overload: Old method for backward compatibility
        private void ToggleSortDirection(string context, Text buttonText)
        {
            ToggleSortDirection(context, buttonText, null);
        }

        private void ToggleSortDirection(string context, Text typeText, Text dirText)
        {
            var state = GetSortState(context);
            state.Direction = (state.Direction == SortDirection.Ascending) ? SortDirection.Descending : SortDirection.Ascending;
            SaveSortState(context, state);
            UpdateSortButtonText(typeText, dirText, state);

            if (context == "Files")
            {
                if (IsFilterActive)
                {
                    try
                    {
                        GallerySortManager.Instance.SortFiles(currentFilteredFiles, state);
                        if (recyclingGrid != null)
                        {
                            recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                            recyclingGrid.Refresh();
                        }
                        UpdatePaginationText();
                    }
                    catch { }
                }
                else
                {
                    RefreshFiles();
                }
            }
            else UpdateTabs();
        }

        private bool IsSortTypeValid(string context, SortType type)
        {
            if (context == "Files")
            {
                return type == SortType.Name || type == SortType.Date || type == SortType.DateCreated || type == SortType.Size || type == SortType.Rating || type == SortType.Deps || type == SortType.Dependents || type == SortType.Missing
                    || type == SortType.Hidden || type == SortType.HiddenOnly || type == SortType.AutoInstall || type == SortType.AutoInstallOnly;
            }
            else if (context == "Category" || context == "Creator" || context == "Status" || context == "Tags")
            {
                return type == SortType.Name || type == SortType.Count;
            }
            return false;
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
                case SortType.Size: symbol = "Sz"; break;
                case SortType.Count: symbol = "#"; break;
                case SortType.Score: symbol = "Sc"; break;
                case SortType.Rating: symbol = "Rt"; break;
                case SortType.Deps: symbol = "Dp"; break;
                case SortType.Dependents: symbol = "Dn"; break;
                case SortType.Missing: symbol = "Ms"; break;
                case SortType.Hidden: symbol = "Hd"; break;
                case SortType.HiddenOnly: symbol = "HO"; break;
                case SortType.AutoInstall: symbol = "Ai"; break;
                case SortType.AutoInstallOnly: symbol = "AO"; break;
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
                    case SortType.Size: symbol = "Sz"; break;
                    case SortType.Count: symbol = "#"; break;
                    case SortType.Score: symbol = "Sc"; break;
                    case SortType.Rating: symbol = "Rt"; break;
                    case SortType.Deps: symbol = "Dp"; break;
                    case SortType.Dependents: symbol = "Dn"; break;
                    case SortType.Missing: symbol = "Ms"; break;
                    case SortType.Hidden: symbol = "Hd"; break;
                    case SortType.HiddenOnly: symbol = "HO"; break;
                    case SortType.AutoInstall: symbol = "Ai"; break;
                    case SortType.AutoInstallOnly: symbol = "AO"; break;
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
    }
}
