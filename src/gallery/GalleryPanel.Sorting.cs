using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
            // Cycle Type
            int currentType = (int)state.Type;
            int maxType = Enum.GetNames(typeof(SortType)).Length;

            // Basic cycle: Name -> Date -> Size -> Count -> Name
            // But Count is only for Category/Creator. Size/Date only for Files.
            // We need context-aware cycling.

            SortType nextType = state.Type;
            do {
                currentType = (currentType + 1) % maxType;
                nextType = (SortType)currentType;
            } while (!IsSortTypeValid(context, nextType));

            state.Type = nextType;

            // Default directions
            if (state.Type == SortType.Name) state.Direction = SortDirection.Ascending;
            else state.Direction = SortDirection.Descending; // Date, Count, Size usually Descending first

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
                if (IsFilterActive)
                {
                    return type == SortType.Name || type == SortType.Date || type == SortType.Size || type == SortType.Rating || type == SortType.Deps || type == SortType.Dependents;
                }
                return type == SortType.Name || type == SortType.Date || type == SortType.Size || type == SortType.Rating;
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
                case SortType.Size: symbol = "Sz"; break;
                case SortType.Count: symbol = "#"; break;
                case SortType.Score: symbol = "Sc"; break;
                case SortType.Rating: symbol = "Rt"; break;
                case SortType.Deps: symbol = "Dp"; break;
                case SortType.Dependents: symbol = "Dd"; break;
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
                    case SortType.Size: symbol = "Sz"; break;
                    case SortType.Count: symbol = "#"; break;
                    case SortType.Score: symbol = "Sc"; break;
                    case SortType.Rating: symbol = "Rt"; break;
                    case SortType.Deps: symbol = "Dp"; break;
                    case SortType.Dependents: symbol = "Dd"; break;
                }
                typeText.text = symbol;
            }
            if (dirText != null)
            {
                string arrow = state.Direction == SortDirection.Ascending ? "↑" : "↓";
                dirText.text = arrow;
            }
        }

        private void SaveSortState(string context, SortState state)
        {
            contentSortStates[context] = state;
            GallerySortManager.Instance.SaveSortState(context, state);
        }
    }
}
