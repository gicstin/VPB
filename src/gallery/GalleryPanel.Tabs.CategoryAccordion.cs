using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        /// <summary>
        /// Hair/Clothing/Appearance/Pose/Scene facets live under the selected category row
        /// (one scroller). Replaces the old φ-split sub-pane for <see cref="ContentType.Category"/>.
        /// </summary>
        private static bool CategoryHasFacetChildren(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            return title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0
                || title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ClearCategoryAccordionButtons(bool isLeft)
        {
            List<GameObject> accList = isLeft ? leftCategoryAccordionButtons : rightCategoryAccordionButtons;
            if (accList == null) return;
            for (int i = 0; i < accList.Count; i++)
                ReturnTabButton(accList[i]);
            accList.Clear();
        }

        private void ClearCategoryAccordionAnchor(bool isLeft)
        {
            if (isLeft) _leftCategoryAccordionAnchor = null;
            else _rightCategoryAccordionAnchor = null;
        }

        /// <summary>Top side search: category names, and facets when selected category is expanded.</summary>
        private string GetCategorySideSearchPlaceholder()
        {
            string title = currentCategoryTitle;
            if (string.IsNullOrEmpty(title) && titleText != null)
                title = titleText.text;
            if (CategoryHasFacetChildren(title))
                return VPBTranslation.T("gallery.search.categories_facets", "Filter this list: categories & facets…");
            return GetContentTypePlaceholder(ContentType.Category);
        }

        private bool AccordionChildPassesListFilter(string label)
        {
            if (!_categoryAccordionFillActive || !_categoryAccordionFilterChildren)
                return true;
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(categoryFilter))
                return true;
            return label.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FillCategoryAccordion(bool isLeft)
        {
            List<GameObject> accList = isLeft ? leftCategoryAccordionButtons : rightCategoryAccordionButtons;
            if (accList == null) return;
            ClearCategoryAccordionButtons(isLeft);

            GameObject holder = isLeft ? leftCategoryTabHolder : rightCategoryTabHolder;
            if (holder == null)
                holder = isLeft ? leftTabContainerGO : rightTabContainerGO;
            GameObject anchor = isLeft ? _leftCategoryAccordionAnchor : _rightCategoryAccordionAnchor;

            string title = currentCategoryTitle;
            if (string.IsNullOrEmpty(title) && titleText != null)
                title = titleText.text;

            if (holder == null || anchor == null || !CategoryHasFacetChildren(title))
                return;
            if (anchor.transform == null || anchor.transform.parent != holder.transform)
                return;

            ContentType subType = InferCategorySubPaneTypeFromTitle(title);
            _categoryAccordionFillActive = true;
            _categoryAccordionFilterChildren = !string.IsNullOrEmpty(categoryFilter)
                && title.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) < 0;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            _sideTabAccordionChildInsetPx = GalleryUiDesignTokens.SideTabAccordionIndentRef * s;
            try
            {
                if (subType == ContentType.SceneSource)
                    BuildSceneSourceTabs(holder, accList);
                else if (subType == ContentType.AppearanceSource)
                    BuildAppearanceSourceTabs(holder, accList);
                else
                    BuildTagsTabs(holder, accList, isLeft);
            }
            finally
            {
                _categoryAccordionFillActive = false;
                _categoryAccordionFilterChildren = false;
                _sideTabAccordionChildInsetPx = 0f;
            }

            int idx = anchor.transform.GetSiblingIndex();
            for (int i = 0; i < accList.Count; i++)
            {
                GameObject go = accList[i];
                if (go == null) continue;
                go.transform.SetParent(holder.transform, false);
                go.transform.SetSiblingIndex(idx + 1 + i);
            }

            RectTransform holderRT = holder.GetComponent<RectTransform>();
            if (holderRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(holderRT);
        }
    }
}
