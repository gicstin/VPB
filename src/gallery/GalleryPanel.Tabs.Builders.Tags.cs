using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void BuildTagsTabs(GameObject container, List<GameObject> trackedButtons, bool isLeft)
        {
            if (!tagsCached) ScheduleTagCountsForSideTabsNonBlocking();

            // Determine which tags to show
            List<string> tagsToShow = new List<string>();
            string title = titleText != null ? titleText.text : "";

            if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Clothing subfilters (shown only for Clothing)
                {
                    Color inactive = ColorInactiveRow;
                    Color active = ColorFacetActiveRow;

                    string[] options = new string[] { "Real Clothing", "Presets", "Custom", "Base Clothing", "Male", "Female", "Decals" };
                    for (int gi = 0; gi < options.Length; gi++)
                    {
                        string opt = options[gi];
                        ClothingSubfilter flag = 0;
                        if (opt == "Real Clothing") flag = ClothingSubfilter.RealClothing;
                        else if (opt == "Presets") flag = ClothingSubfilter.Presets;
                        else if (opt == "Custom") flag = ClothingSubfilter.Custom;
                        else if (opt == "Base Clothing") flag = ClothingSubfilter.Items;
                        else if (opt == "Male") flag = ClothingSubfilter.Male;
                        else if (opt == "Female") flag = ClothingSubfilter.Female;
                        else if (opt == "Decals") flag = ClothingSubfilter.Decals;

                        bool isActive = (flag != 0) && ((clothingSubfilter & flag) != 0);
                        Color btnColor = isActive ? active : inactive;

                        int cnt = 0;
                        if (opt == "Real Clothing") cnt = isActive ? clothingSubfilterCountReal : clothingSubfilterFacetCountReal;
                        else if (opt == "Presets") cnt = isActive ? clothingSubfilterCountPresets : clothingSubfilterFacetCountPresets;
                        else if (opt == "Custom") cnt = isActive ? clothingSubfilterCountCustom : clothingSubfilterFacetCountCustom;
                        else if (opt == "Base Clothing") cnt = isActive ? clothingSubfilterCountItems : clothingSubfilterFacetCountItems;
                        else if (opt == "Male") cnt = isActive ? clothingSubfilterCountMale : clothingSubfilterFacetCountMale;
                        else if (opt == "Female") cnt = isActive ? clothingSubfilterCountFemale : clothingSubfilterFacetCountFemale;
                        else if (opt == "Decals") cnt = isActive ? clothingSubfilterCountDecals : clothingSubfilterFacetCountDecals;

                        string label = opt + " (" + cnt + ")";

                        CreateTabButton(container.transform, label, btnColor, isActive, () => {
                            if (flag != 0)
                            {
                                if ((clothingSubfilter & flag) != 0) clothingSubfilter = 0;
                                else clothingSubfilter = flag;
                            }
                            if (flag == ClothingSubfilter.Male || flag == ClothingSubfilter.Female)
                            {
                                _clothingGenderUserOverride = true;
                                LogUtil.Log("[VPB.Gallery] user override Clothing gender = " + flag);
                            }
                            tagsCached = false;
                            RefreshFilesAndTabs();
                        }, trackedButtons);
                    }
                }

                tagsToShow.AddRange(TagFilter.ClothingTypeTags);
                tagsToShow.AddRange(TagFilter.ClothingRegionTags);
                tagsToShow.AddRange(TagFilter.ClothingOtherTags);
            }
            else if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Hair subfilters (Issue #101 parity with Clothing: hide preset .vap duplicates by default)
                {
                    Color inactive = ColorInactiveRow;
                    Color active = ColorFacetActiveRow;

                    string[] options = new string[] { "Presets", "Custom", "Base Hair", "Male", "Female" };
                    for (int gi = 0; gi < options.Length; gi++)
                    {
                        string opt = options[gi];
                        HairSubfilter flag = 0;
                        if (opt == "Presets") flag = HairSubfilter.Presets;
                        else if (opt == "Custom") flag = HairSubfilter.Custom;
                        else if (opt == "Base Hair") flag = HairSubfilter.Items;
                        else if (opt == "Male") flag = HairSubfilter.Male;
                        else if (opt == "Female") flag = HairSubfilter.Female;

                        bool isActive = (flag != 0) && ((hairSubfilter & flag) != 0);
                        Color btnColor = isActive ? active : inactive;

                        int cnt = 0;
                        if (opt == "Presets") cnt = isActive ? hairSubfilterCountPresets : hairSubfilterFacetCountPresets;
                        else if (opt == "Custom") cnt = isActive ? hairSubfilterCountCustom : hairSubfilterFacetCountCustom;
                        else if (opt == "Base Hair") cnt = isActive ? hairSubfilterCountItems : hairSubfilterFacetCountItems;
                        else if (opt == "Male") cnt = isActive ? hairSubfilterCountMale : hairSubfilterFacetCountMale;
                        else if (opt == "Female") cnt = isActive ? hairSubfilterCountFemale : hairSubfilterFacetCountFemale;

                        string label = opt + " (" + cnt + ")";

                        CreateTabButton(container.transform, label, btnColor, isActive, () => {
                            if (flag != 0)
                            {
                                if ((hairSubfilter & flag) != 0) hairSubfilter = 0;
                                else hairSubfilter = flag;
                            }
                            if (flag == HairSubfilter.Male || flag == HairSubfilter.Female)
                            {
                                _hairGenderUserOverride = true;
                                LogUtil.Log("[VPB.Gallery] user override Hair gender = " + flag);
                            }
                            tagsCached = false;
                            RefreshFilesAndTabs();
                        }, trackedButtons);
                    }
                }

                tagsToShow.AddRange(TagFilter.HairTypeTags);
                tagsToShow.AddRange(TagFilter.HairRegionTags);
                tagsToShow.AddRange(TagFilter.HairOtherTags);
            }
            else if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                {
                    Color inactive = ColorInactiveRow;
                    Color active = ColorFacetActiveRow;

                    string[] options = new string[] { "Female", "Male", "Futa" };
                    for (int gi = 0; gi < options.Length; gi++)
                    {
                        string opt = options[gi];
                        AppearanceSubfilter flag = 0;
                        if (opt == "Male") flag = AppearanceSubfilter.Male;
                        else if (opt == "Female") flag = AppearanceSubfilter.Female;
                        else if (opt == "Futa") flag = AppearanceSubfilter.Futa;

                        bool isActive = (flag != 0) && ((appearanceSubfilter & flag) != 0);
                        Color btnColor = isActive ? active : inactive;

                        int cnt = 0;
                        if (opt == "Male") cnt = isActive ? appearanceSubfilterCurrentCountMale : appearanceSubfilterFacetCountMale;
                        else if (opt == "Female") cnt = isActive ? appearanceSubfilterCurrentCountFemale : appearanceSubfilterFacetCountFemale;
                        else if (opt == "Futa") cnt = isActive ? appearanceSubfilterCurrentCountFuta : appearanceSubfilterFacetCountFuta;

                        string label = opt + " (" + cnt + ")";

                        CreateTabButton(container.transform, label, btnColor, isActive, () => {
                            if (flag != 0)
                            {
                                if ((appearanceSubfilter & flag) != 0) appearanceSubfilter = 0;
                                else appearanceSubfilter = flag;
                            }
                            tagsCached = false;
                            RefreshFilesAndTabs();
                        }, trackedButtons);
                    }
                }
            }
            else if (title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                {
                    Color inactive = ColorInactiveRow;
                    Color active = ColorFacetActiveRow;

                    // Pose people-count filter (Single vs Dual)
                    {
                        bool isSingleActive = (posePeopleFilter == PosePeopleFilter.Single);
                        bool isDualActive = (posePeopleFilter == PosePeopleFilter.Dual);

                        CreateTabButton(container.transform,
                            string.Format(VPBTranslation.T("gallery.pose.single_count", "Single ({0})"), posePeopleFacetCountSingle),
                            isSingleActive ? active : inactive, isSingleActive, () => {
                                posePeopleFilter = (posePeopleFilter == PosePeopleFilter.Single) ? PosePeopleFilter.All : PosePeopleFilter.Single;
                                RefreshFilesAndTabs();
                            }, trackedButtons);

                        CreateTabButton(container.transform,
                            string.Format(VPBTranslation.T("gallery.pose.dual_count", "Dual ({0})"), posePeopleFacetCountDual),
                            isDualActive ? active : inactive, isDualActive, () => {
                                posePeopleFilter = (posePeopleFilter == PosePeopleFilter.Dual) ? PosePeopleFilter.All : PosePeopleFilter.Dual;
                                RefreshFilesAndTabs();
                            }, trackedButtons);
                    }
                }
            }
            else if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // handled above
            }

            if (!string.IsNullOrEmpty(tagFilter))
            {
                tagsToShow.RemoveAll(t => t.IndexOf(tagFilter, StringComparison.OrdinalIgnoreCase) < 0);
            }

            var sortState = GetSortState("Tags");
            if (sortState.Type == SortType.Name)
            {
                if (sortState.Direction == SortDirection.Ascending) tagsToShow.Sort();
                else tagsToShow.Sort((a, b) => b.CompareTo(a));
            }
            else if (sortState.Type == SortType.Count)
            {
                tagsToShow.Sort((a, b) => {
                    int cA = tagCounts.ContainsKey(a) ? tagCounts[a] : 0;
                    int cB = tagCounts.ContainsKey(b) ? tagCounts[b] : 0;
                    int cmp = cA.CompareTo(cB);
                    if (cmp == 0) return a.CompareTo(b);
                    return sortState.Direction == SortDirection.Ascending ? cmp : -cmp;
                });
            }

            foreach (var tag in tagsToShow)
            {
                int count = 0;
                if (tagCounts.ContainsKey(tag)) count = tagCounts[tag];

                bool isActive = activeTags.Contains(tag);

                if (count == 0 && !isActive) continue;

                string label = tag + " (" + count + ")";
                Color btnColor = isActive ? new Color(0.5f, 0.2f, 0.5f, 1f) : ColorInactiveRow;

                CreateTabButton(container.transform, label, btnColor, isActive, () => {
                    if (activeTags.Contains(tag)) activeTags.Remove(tag);
                    else activeTags.Add(tag);

                    RefreshFilesAndTabs();
                }, trackedButtons);
            }

            // Update Clear Button
            GameObject clearBtn = isLeft ? leftSubClearBtn : rightSubClearBtn;
            Text clearBtnText = isLeft ? leftSubClearBtnText : rightSubClearBtnText;

            if (clearBtn != null)
            {
                if (activeTags.Count > 0)
                {
                    clearBtn.SetActive(true);
                    if (clearBtnText != null)
                        clearBtnText.text = string.Format(VPBTranslation.T("gallery.tags.clear_selected_count", "Clear Selected ({0})"), activeTags.Count);
                }
                else
                {
                    clearBtn.SetActive(false);
                }
            }
        }
    }
}

