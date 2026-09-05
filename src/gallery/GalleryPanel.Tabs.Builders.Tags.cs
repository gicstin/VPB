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

            // Cleared on every sub-pane rebuild; the active chip (if any) re-registers its handle.
            _activeSubfilterChipText = null;
            _activeSubfilterChipLabelPrefix = null;

            // Determine which tags to show
            List<string> tagsToShow = new List<string>();
            string title = titleText != null ? titleText.text : "";

            if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ApplyClothingChipCountsFromSqlIfEnabled();
                // Clothing subfilters (shown only for Clothing)
                {
                    Color inactive = ColorInactiveRow;
                    Color active = ColorFacetActiveRow;

                    string[] options = new string[] { "Real Clothing", "Presets", "Custom", "Custom Preset", "Base Clothing", "Male", "Female", "Decals" };
                    for (int gi = 0; gi < options.Length; gi++)
                    {
                        string opt = options[gi];
                        if (!AccordionChildPassesListFilter(opt)) continue;
                        ClothingSubfilter flag = 0;
                        if (opt == "Real Clothing") flag = ClothingSubfilter.RealClothing;
                        else if (opt == "Presets") flag = ClothingSubfilter.Presets;
                        else if (opt == "Custom") flag = ClothingSubfilter.Custom;
                        else if (opt == "Custom Preset") flag = ClothingSubfilter.CustomPreset;
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
                        else if (opt == "Custom Preset") cnt = isActive ? clothingSubfilterCountCustomPreset : clothingSubfilterFacetCountCustomPreset;
                        else if (opt == "Base Clothing") cnt = isActive ? clothingSubfilterCountItems : clothingSubfilterFacetCountItems;
                        else if (opt == "Male") cnt = isActive ? clothingSubfilterCountMale : clothingSubfilterFacetCountMale;
                        else if (opt == "Female") cnt = isActive ? clothingSubfilterCountFemale : clothingSubfilterFacetCountFemale;
                        else if (opt == "Decals") cnt = isActive ? clothingSubfilterCountDecals : clothingSubfilterFacetCountDecals;

                        // Active chip shows the live grid count (kept equal to the bottom "X Items" by
                        // UpdateSelectionContextMenu); inactive chips show the SQL facet count as a prediction.
                        if (isActive && currentFilteredFiles != null) cnt = currentFilteredFiles.Count;

                        string label = opt + " (" + cnt + ")";

                        GameObject chipGO = CreateTabButton(container.transform, label, btnColor, isActive, () => {
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
                            SyncBrowseFilterChipChrome();
                        }, trackedButtons);
                        if (isActive) CaptureActiveSubfilterChip(chipGO, opt);
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

                    string[] options = new string[] { "Presets", "Custom", "Custom Preset", "Base Hair", "Male", "Female" };
                    for (int gi = 0; gi < options.Length; gi++)
                    {
                        string opt = options[gi];
                        if (!AccordionChildPassesListFilter(opt)) continue;
                        HairSubfilter flag = 0;
                        if (opt == "Presets") flag = HairSubfilter.Presets;
                        else if (opt == "Custom") flag = HairSubfilter.Custom;
                        else if (opt == "Custom Preset") flag = HairSubfilter.CustomPreset;
                        else if (opt == "Base Hair") flag = HairSubfilter.Items;
                        else if (opt == "Male") flag = HairSubfilter.Male;
                        else if (opt == "Female") flag = HairSubfilter.Female;

                        bool isActive = (flag != 0) && ((hairSubfilter & flag) != 0);
                        Color btnColor = isActive ? active : inactive;

                        int cnt = 0;
                        if (opt == "Presets") cnt = isActive ? hairSubfilterCountPresets : hairSubfilterFacetCountPresets;
                        else if (opt == "Custom") cnt = isActive ? hairSubfilterCountCustom : hairSubfilterFacetCountCustom;
                        else if (opt == "Custom Preset") cnt = isActive ? hairSubfilterCountCustomPreset : hairSubfilterFacetCountCustomPreset;
                        else if (opt == "Base Hair") cnt = isActive ? hairSubfilterCountItems : hairSubfilterFacetCountItems;
                        else if (opt == "Male") cnt = isActive ? hairSubfilterCountMale : hairSubfilterFacetCountMale;
                        else if (opt == "Female") cnt = isActive ? hairSubfilterCountFemale : hairSubfilterFacetCountFemale;

                        if (isActive && currentFilteredFiles != null) cnt = currentFilteredFiles.Count;

                        string label = opt + " (" + cnt + ")";

                        GameObject chipGO = CreateTabButton(container.transform, label, btnColor, isActive, () => {
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
                            SyncBrowseFilterChipChrome();
                        }, trackedButtons);
                        if (isActive) CaptureActiveSubfilterChip(chipGO, opt);
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

                    string[] options = new string[] { "Female", "Male", "Futa", "Unknown" };
                    for (int gi = 0; gi < options.Length; gi++)
                    {
                        string opt = options[gi];
                        if (!AccordionChildPassesListFilter(opt)) continue;
                        AppearanceSubfilter flag = 0;
                        if (opt == "Male") flag = AppearanceSubfilter.Male;
                        else if (opt == "Female") flag = AppearanceSubfilter.Female;
                        else if (opt == "Futa") flag = AppearanceSubfilter.Futa;
                        else if (opt == "Unknown") flag = AppearanceSubfilter.Unknown;

                        bool isActive = (flag != 0) && ((appearanceSubfilter & flag) != 0);
                        Color btnColor = isActive ? active : inactive;

                        int cnt = 0;
                        if (opt == "Male") cnt = isActive ? appearanceSubfilterCurrentCountMale : appearanceSubfilterFacetCountMale;
                        else if (opt == "Female") cnt = isActive ? appearanceSubfilterCurrentCountFemale : appearanceSubfilterFacetCountFemale;
                        else if (opt == "Futa") cnt = isActive ? appearanceSubfilterCurrentCountFuta : appearanceSubfilterFacetCountFuta;
                        else if (opt == "Unknown") cnt = isActive ? appearanceSubfilterCurrentCountUnknown : appearanceSubfilterFacetCountUnknown;

                        string label = opt + " (" + cnt + ")";

                        CreateTabButton(container.transform, label, btnColor, isActive, () => {
                            if (flag != 0)
                            {
                                if ((appearanceSubfilter & flag) != 0) appearanceSubfilter = 0;
                                else appearanceSubfilter = flag;
                            }
                            OnAppearanceGenderFilterChanged();
                        }, trackedButtons);
                    }
                }
            }
            else if ((IsGalleryScenesCategory(currentCategoryTitle) || IsGalleryScenesCategory(title))
                && LookFacetHubModeAvailable())
            {
                Color inactive = ColorInactiveRow;
                Color active = ColorFacetActiveRow;
                SceneHubSubfilter sceneHubShown = EffectiveSceneHubSubfilter();
                string[] options = new string[] { "Hub: Scenes", "Hub: Looks", "Unclassified", "Other Hub types" };
                for (int gi = 0; gi < options.Length; gi++)
                {
                    string opt = options[gi];
                    if (!AccordionChildPassesListFilter(opt)) continue;
                    SceneHubSubfilter flag = 0;
                    if (opt == "Hub: Scenes") flag = SceneHubSubfilter.HubScenes;
                    else if (opt == "Hub: Looks") flag = SceneHubSubfilter.HubLooks;
                    else if (opt == "Unclassified") flag = SceneHubSubfilter.Unclassified;
                    else if (opt == "Other Hub types") flag = SceneHubSubfilter.Other;

                    bool isActive = (flag != 0) && ((sceneHubShown & flag) != 0);
                    Color btnColor = isActive ? active : inactive;
                    string label = opt;
                    if (opt == "Hub: Scenes")
                        label = VPBTranslation.T("gallery.scenes.chip_hub_scenes", "Hub: Scenes");
                    else if (opt == "Hub: Looks")
                        label = VPBTranslation.T("gallery.scenes.chip_hub_looks", "Hub: Looks");
                    else if (opt == "Unclassified")
                        label = VPBTranslation.T("gallery.scenes.chip_unclassified", "Unclassified");
                    else if (opt == "Other Hub types")
                        label = VPBTranslation.T("gallery.scenes.chip_hub_other", "Other Hub types");

                    SceneHubSubfilter flagSnap = flag;
                    CreateTabButton(container.transform, label, btnColor, isActive, () => {
                        if (flagSnap != 0)
                            SetSceneHubSubfilterExplicit(EffectiveSceneHubSubfilter() ^ flagSnap);
                        tagsCached = false;
                        RefreshFilesAndTabs();
                        SyncBrowseFilterChipChrome();
                    }, trackedButtons);
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

                        string singleLabel = string.Format(VPBTranslation.T("gallery.pose.single_count", "Single ({0})"), posePeopleFacetCountSingle);
                        if (AccordionChildPassesListFilter("Single") || AccordionChildPassesListFilter(singleLabel))
                        {
                            CreateTabButton(container.transform,
                                singleLabel,
                                isSingleActive ? active : inactive, isSingleActive, () => {
                                    posePeopleFilter = (posePeopleFilter == PosePeopleFilter.Single) ? PosePeopleFilter.All : PosePeopleFilter.Single;
                                    RefreshFilesAndTabs();
                                }, trackedButtons);
                        }

                        string dualLabel = string.Format(VPBTranslation.T("gallery.pose.dual_count", "Dual ({0})"), posePeopleFacetCountDual);
                        if (AccordionChildPassesListFilter("Dual") || AccordionChildPassesListFilter(dualLabel))
                        {
                            CreateTabButton(container.transform,
                                dualLabel,
                                isDualActive ? active : inactive, isDualActive, () => {
                                    posePeopleFilter = (posePeopleFilter == PosePeopleFilter.Dual) ? PosePeopleFilter.All : PosePeopleFilter.Dual;
                                    RefreshFilesAndTabs();
                                }, trackedButtons);
                        }
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
                if (!AccordionChildPassesListFilter(tag)) continue;
                int count = 0;
                if (tagCounts.ContainsKey(tag)) count = tagCounts[tag];

                bool isActive = activeTags.Contains(tag);

                if (count == 0 && !isActive) continue;

                string label = tag + " (" + count + ")";
                Color btnColor = isActive ? GalleryUiColorTokens.FacetTag : ColorInactiveRow;

                CreateTabButton(container.transform, label, btnColor, isActive, () => {
                    if (activeTags.Contains(tag)) activeTags.Remove(tag);
                    else activeTags.Add(tag);

                    RefreshFilesAndTabs();
                    SyncBrowseFilterChipChrome();
                }, trackedButtons);
            }

            // Update Clear Button
            // Split-pane clear lives at footer of old sub-scroller — accordion uses filter chips instead.
            GameObject clearBtn = isLeft ? leftSubClearBtn : rightSubClearBtn;
            Text clearBtnText = isLeft ? leftSubClearBtnText : rightSubClearBtnText;

            if (clearBtn != null)
            {
                if (!_categoryAccordionFillActive && activeTags.Count > 0)
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

