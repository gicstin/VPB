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
            if (isRatingSortToggleEnabled)
            {
                isRatingSortToggleEnabled = false;
                var restore = ratingSortTogglePreviousState != null
                    ? ratingSortTogglePreviousState.Clone()
                    : GallerySortManager.Instance.GetDefaultSortState("Files");
                ratingSortTogglePreviousState = null;

                SaveSortState("Files", restore);
                UpdateSortButtonText(fileSortBtnText, restore);
                SyncRatingSortToggleState();

                currentPage = 0;
                RefreshFiles();
                return;
            }

            var current = GetSortState("Files");
            if (current != null) ratingSortTogglePreviousState = current.Clone();
            isRatingSortToggleEnabled = true;

            var state = new SortState(SortType.Rating, SortDirection.Descending);
            SaveSortState("Files", state);
            UpdateSortButtonText(fileSortBtnText, state);
            SyncRatingSortToggleState();

            currentPage = 0;
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

        private void CycleSort(string context, Text buttonText)
        {
            if (context == "Files" && isRatingSortToggleEnabled)
            {
                isRatingSortToggleEnabled = false;
                ratingSortTogglePreviousState = null;
                SyncRatingSortToggleState();
            }

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
            UpdateSortButtonText(buttonText, state);
            
            if (context == "Files") 
            {
                currentPage = 0;
                RefreshFiles();
            }
            else UpdateTabs();
        }
        
        private void ToggleSortDirection(string context, Text buttonText)
        {
            if (context == "Files" && isRatingSortToggleEnabled)
            {
                isRatingSortToggleEnabled = false;
                ratingSortTogglePreviousState = null;
                SyncRatingSortToggleState();
            }

            var state = GetSortState(context);
            state.Direction = (state.Direction == SortDirection.Ascending) ? SortDirection.Descending : SortDirection.Ascending;
            SaveSortState(context, state);
            UpdateSortButtonText(buttonText, state);
            
            if (context == "Files") 
            {
                currentPage = 0;
                RefreshFiles();
            }
            else UpdateTabs();
        }

        private bool IsSortTypeValid(string context, SortType type)
        {
            if (context == "Files")
            {
                return type == SortType.Name || type == SortType.Date || type == SortType.Size || type == SortType.Rating;
            }
            else if (context == "Category" || context == "Creator" || context == "Status" || context == "Tags")
            {
                return type == SortType.Name || type == SortType.Count;
            }
            return false;
        }

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
            }
            string arrow = state.Direction == SortDirection.Ascending ? "↑" : "↓";
            t.text = symbol + arrow;
        }

        private void SaveSortState(string context, SortState state)
        {
            contentSortStates[context] = state;
            GallerySortManager.Instance.SaveSortState(context, state);
        }
    }
}
