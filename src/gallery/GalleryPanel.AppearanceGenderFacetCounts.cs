using System;
using System.Collections.Generic;
using System.IO;

namespace VPB
{
    public partial class GalleryPanel
    {
        private bool IsAppearanceCategoryTitle()
        {
            string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            return title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Appearance grid narrowed to loose/custom only (per-category Local toggle or global Local).</summary>
        private bool IsAppearanceLocalOnlyActive()
        {
            if (!IsAppearanceCategoryTitle()) return false;
            if (string.Equals(currentAppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase))
                return true;
            return ResolveEffectiveSourceFilterMode(true, currentPath ?? "") == 1;
        }

        /// <summary>Fast gender badges + skip heavy parallel tag scan when Source: Local is active.</summary>
        private bool IsAppearanceLooseScopedBrowsing() => IsAppearanceLocalOnlyActive();

        /// <summary>Appearance facet counts come from one SQL pass; skip the VAR package walk (~3374 rows).</summary>
        private bool ShouldSkipHeavyAppearanceTagParallelScan()
        {
            if (!IsAppearanceCategoryTitle()) return false;
            if (IsAppearanceLocalOnlyActive()) return true;
            return VpbSqlite3.IsAvailable;
        }

        private void ResetAppearanceGenderFacetCounts()
        {
            appearanceSubfilterCountAll = 0;
            appearanceSubfilterCountPresets = 0;
            appearanceSubfilterCountCustom = 0;
            appearanceSubfilterCountMale = 0;
            appearanceSubfilterCountFemale = 0;
            appearanceSubfilterCountFuta = 0;
            appearanceSubfilterCountUnknown = 0;

            appearanceSubfilterFacetCountPresets = 0;
            appearanceSubfilterFacetCountCustom = 0;
            appearanceSubfilterFacetCountMale = 0;
            appearanceSubfilterFacetCountFemale = 0;
            appearanceSubfilterFacetCountFuta = 0;
            appearanceSubfilterFacetCountUnknown = 0;

            appearanceSubfilterCurrentCountAll = 0;
            appearanceSubfilterCurrentCountMale = 0;
            appearanceSubfilterCurrentCountFemale = 0;
            appearanceSubfilterCurrentCountFuta = 0;
            appearanceSubfilterCurrentCountUnknown = 0;

            appearanceSourceCountAll = 0;
            appearanceSourceCountCustom = 0;
        }

        /// <summary>Recount gender/source chips from loose .vap under the current path only (milliseconds).</summary>
        private bool TryRecomputeAppearanceGenderFacetCountsScoped()
        {
            if (!IsAppearanceLooseScopedBrowsing()) return false;

            ResetAppearanceGenderFacetCounts();

            string cat = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            EnsureAppearanceGenderRefreshCaches(cat ?? "");

            List<string> pathsToSearch = new List<string>();
            if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
            else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

            AppearanceSubfilter aSub = appearanceSubfilter;

            for (int pi = 0; pi < pathsToSearch.Count; pi++)
            {
                string searchPath = pathsToSearch[pi];
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                List<string> sysFileList = new List<string>();
                try { FileManager.SafeGetFiles(searchPath, "*.vap", sysFileList); }
                catch { continue; }

                for (int fi = 0; fi < sysFileList.Count; fi++)
                {
                    string sysPath = sysFileList[fi] ?? "";
                    string norm = sysPath.Replace('\\', '/');
                    if (!norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) &&
                        !norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    appearanceSourceCountCustom++;
                    appearanceSourceCountAll++;

                    bool isCustomLoose = norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                    bool isPresetLoose = norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);

                    AppearanceGender lg;
                    try { lg = AppearanceGenderClassifier.ClassifyLooseVapPath(sysPath, cat ?? "", _appearanceUserTagsByRowKey); }
                    catch { lg = AppearanceGender.Unknown; }

                    appearanceSubfilterCountAll++;
                    if (isPresetLoose) appearanceSubfilterCountPresets++;
                    if (isCustomLoose) appearanceSubfilterCountCustom++;
                    if (lg == AppearanceGender.Male) appearanceSubfilterCountMale++;
                    if (lg == AppearanceGender.Female) appearanceSubfilterCountFemale++;
                    if (lg == AppearanceGender.Futa) appearanceSubfilterCountFuta++;
                    if (lg == AppearanceGender.Unknown) appearanceSubfilterCountUnknown++;

                    if (LoosePassesAppearanceSubfilter(aSub ^ AppearanceSubfilter.Presets, isPresetLoose, isCustomLoose, lg)) appearanceSubfilterFacetCountPresets++;
                    if (LoosePassesAppearanceSubfilter(aSub ^ AppearanceSubfilter.Custom, isPresetLoose, isCustomLoose, lg)) appearanceSubfilterFacetCountCustom++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Male), isPresetLoose, isCustomLoose, lg)) appearanceSubfilterFacetCountMale++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Female), isPresetLoose, isCustomLoose, lg)) appearanceSubfilterFacetCountFemale++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Futa), isPresetLoose, isCustomLoose, lg)) appearanceSubfilterFacetCountFuta++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Unknown), isPresetLoose, isCustomLoose, lg)) appearanceSubfilterFacetCountUnknown++;

                    if (LoosePassesAppearanceSubfilter(aSub, isPresetLoose, isCustomLoose, lg))
                    {
                        appearanceSubfilterCurrentCountAll++;
                        if (lg == AppearanceGender.Male) appearanceSubfilterCurrentCountMale++;
                        if (lg == AppearanceGender.Female) appearanceSubfilterCurrentCountFemale++;
                        if (lg == AppearanceGender.Futa) appearanceSubfilterCurrentCountFuta++;
                        if (lg == AppearanceGender.Unknown) appearanceSubfilterCurrentCountUnknown++;
                    }
                }
            }

            return true;
        }

        private bool LoosePassesAppearanceSubfilter(AppearanceSubfilter f, bool isPresetLoose, bool isCustomLoose, AppearanceGender lg)
        {
            if (f == 0) return true;
            bool wPresets = (f & AppearanceSubfilter.Presets) != 0;
            bool wCustom = (f & AppearanceSubfilter.Custom) != 0;
            bool typeOk = true;
            if (wPresets || wCustom)
            {
                if (wPresets && wCustom) typeOk = true;
                else if (wPresets) typeOk = isPresetLoose;
                else if (wCustom) typeOk = isCustomLoose;
            }
            if (!typeOk) return false;
            if (!AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(lg, f)) return false;
            return true;
        }
    }
}
