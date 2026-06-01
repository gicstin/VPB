using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using VPB.src.util;

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

        /// <summary>Loose .vap gender chips apply when source filter is not Var-only.</summary>
        private bool ShouldCountLooseAppearanceGenderFiles()
        {
            if (!IsAppearanceCategoryTitle()) return false;
            return ResolveEffectiveSourceFilterMode(true, currentPath ?? "") != 2;
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
            appearanceSourceCountPresets = 0;
            appearanceSourceCountCustom = 0;
        }

        private void CollectAppearanceSearchPaths(List<string> pathsToSearch)
        {
            pathsToSearch.Clear();
            if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
            else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);
        }

        /// <summary>Loose .vap facet counts (uses system_files cache + loose_vap_gender probe cache).</summary>
        private void AccumulateLooseVapAppearanceGenderCounts(bool resetCountsFirst)
        {
            if (resetCountsFirst)
                ResetAppearanceGenderFacetCounts();

            string cat = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            EnsureAppearanceGenderRefreshCaches(cat ?? "");

            var pathsToSearch = new List<string>();
            CollectAppearanceSearchPaths(pathsToSearch);
            if (pathsToSearch.Count == 0) return;

            AppearanceSubfilter aSub = appearanceSubfilter;

            string sysCacheKey = null;
            string sysCacheSig = null;
            List<VpbLocalDatabase.SystemFileRow> sysCached = null;
            bool sysCacheHit = false;
            try
            {
                var p2 = new List<string>(pathsToSearch);
                p2.Sort(StringComparer.OrdinalIgnoreCase);
                var sbKey = new StringBuilder(256);
                sbKey.Append("tags:loose:appearance|ext=vap|paths=");
                for (int i = 0; i < p2.Count; i++)
                {
                    if (i != 0) sbKey.Append(';');
                    sbKey.Append((p2[i] ?? "").Replace('\\', '/').TrimEnd('/'));
                }
                sysCacheKey = sbKey.ToString();

                var sbSig = new StringBuilder(128);
                for (int i = 0; i < p2.Count; i++)
                {
                    long t = 0;
                    try { t = VpbLocalDatabase.DeepMaxDirMtimeBinary(p2[i]); } catch { t = 0; }
                    if (i != 0) sbSig.Append('|');
                    sbSig.Append(t.ToString());
                }
                sysCacheSig = sbSig.ToString();

                sysCached = new List<VpbLocalDatabase.SystemFileRow>();
                sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCached);
            }
            catch
            {
                sysCacheHit = false;
                sysCached = null;
            }

            var genderBulk = new LooseVapGenderBulkCache();
            for (int pi = 0; pi < pathsToSearch.Count; pi++)
            {
                string searchPath = pathsToSearch[pi];
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                List<string> sysFileList;
                if (sysCacheHit && sysCached != null && sysCached.Count > 0)
                {
                    sysFileList = new List<string>();
                    for (int i = 0; i < sysCached.Count; i++)
                    {
                        string p = sysCached[i].Path ?? "";
                        if (p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
                            sysFileList.Add(p);
                    }
                }
                else
                {
                    sysFileList = new List<string>();
                    try { FileManager.SafeGetFiles(searchPath, "*.vap", sysFileList); }
                    catch { continue; }
                }

                for (int fi = 0; fi < sysFileList.Count; fi++)
                {
                    string sysPath = sysFileList[fi] ?? "";
                    string norm = sysPath.Replace('\\', '/');
                    if (!norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) &&
                        !norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isCustomLoose = norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                    bool isPresetLoose = norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);

                    appearanceSourceCountAll++;
                    if (isCustomLoose) appearanceSourceCountCustom++;
                    if (isPresetLoose) appearanceSourceCountPresets++;

                    AppearanceGender lg;
                    try { lg = AppearanceGenderClassifier.ClassifyLooseVapPath(sysPath, cat ?? "", _appearanceUserTagsByRowKey, genderBulk); }
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
            genderBulk.Flush();
        }

        /// <summary>Time-sliced loose .vap merge — keeps category switch responsive on huge libraries.</summary>
        private IEnumerator CoMergeLooseVapAppearanceGenderFacetCounts(int maxMsPerSlice, int deferredSessionId)
        {
            if (!ShouldCountLooseAppearanceGenderFiles()) yield break;
            if (IsAppearanceLooseScopedBrowsing()) yield break;

            string cat = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            EnsureAppearanceGenderRefreshCaches(cat ?? "");

            var pathsToSearch = new List<string>();
            CollectAppearanceSearchPaths(pathsToSearch);
            if (pathsToSearch.Count == 0) yield break;

            AppearanceSubfilter aSub = appearanceSubfilter;
            Stopwatch sliceWatch = maxMsPerSlice > 0 ? Stopwatch.StartNew() : null;

            string sysCacheKey = null;
            string sysCacheSig = null;
            List<VpbLocalDatabase.SystemFileRow> sysCached = null;
            bool sysCacheHit = false;
            try
            {
                var p2 = new List<string>(pathsToSearch);
                p2.Sort(StringComparer.OrdinalIgnoreCase);
                var sbKey = new StringBuilder(256);
                sbKey.Append("tags:loose:appearance|ext=vap|paths=");
                for (int i = 0; i < p2.Count; i++)
                {
                    if (i != 0) sbKey.Append(';');
                    sbKey.Append((p2[i] ?? "").Replace('\\', '/').TrimEnd('/'));
                }
                sysCacheKey = sbKey.ToString();

                var sbSig = new StringBuilder(128);
                for (int i = 0; i < p2.Count; i++)
                {
                    long t = 0;
                    try { t = VpbLocalDatabase.DeepMaxDirMtimeBinary(p2[i]); } catch { t = 0; }
                    if (i != 0) sbSig.Append('|');
                    sbSig.Append(t.ToString());
                }
                sysCacheSig = sbSig.ToString();

                sysCached = new List<VpbLocalDatabase.SystemFileRow>();
                sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCached);
            }
            catch
            {
                sysCacheHit = false;
                sysCached = null;
            }

            var genderBulk = new LooseVapGenderBulkCache();
            for (int pi = 0; pi < pathsToSearch.Count; pi++)
            {
                if (deferredSessionId >= 0 && deferredSessionId != _deferredSubPaneSessionId) yield break;

                string searchPath = pathsToSearch[pi];
                if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                List<string> sysFileList;
                if (sysCacheHit && sysCached != null && sysCached.Count > 0)
                {
                    sysFileList = new List<string>();
                    for (int i = 0; i < sysCached.Count; i++)
                    {
                        string p = sysCached[i].Path ?? "";
                        if (p.EndsWith(".vap", StringComparison.OrdinalIgnoreCase))
                            sysFileList.Add(p);
                    }
                }
                else
                {
                    sysFileList = new List<string>();
                    try { FileManager.SafeGetFiles(searchPath, "*.vap", sysFileList); }
                    catch { continue; }
                }

                for (int fi = 0; fi < sysFileList.Count; fi++)
                {
                    if (deferredSessionId >= 0 && deferredSessionId != _deferredSubPaneSessionId) yield break;

                    string sysPath = sysFileList[fi] ?? "";
                    string norm = sysPath.Replace('\\', '/');
                    if (!norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) &&
                        !norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isCustomLoose = norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                    bool isPresetLoose = norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);

                    appearanceSourceCountAll++;
                    if (isCustomLoose) appearanceSourceCountCustom++;
                    if (isPresetLoose) appearanceSourceCountPresets++;

                    AppearanceGender lg;
                    try { lg = AppearanceGenderClassifier.ClassifyLooseVapPath(sysPath, cat ?? "", _appearanceUserTagsByRowKey, genderBulk); }
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

                    if (sliceWatch != null && fi % 64 == 63 && sliceWatch.ElapsedMilliseconds >= maxMsPerSlice)
                    {
                        yield return null;
                        if (deferredSessionId >= 0 && deferredSessionId != _deferredSubPaneSessionId) yield break;
                        sliceWatch.Reset();
                        sliceWatch.Start();
                    }
                }
            }
            genderBulk.Flush();
        }

        private void ScheduleAppearanceLooseMergeRefresh()
        {
            if (!ShouldCountLooseAppearanceGenderFiles()) return;
            if (IsAppearanceLooseScopedBrowsing()) return;
            if (_appearanceLooseMergeCo != null)
            {
                try { StopCoroutine(_appearanceLooseMergeCo); } catch { }
                _appearanceLooseMergeCo = null;
            }
            try { _appearanceLooseMergeCo = StartCoroutine(CoAppearanceLooseMergeRefresh()); } catch { }
        }

        private IEnumerator CoAppearanceLooseMergeRefresh()
        {
            try
            {
                IEnumerator merge = CoMergeLooseVapAppearanceGenderFacetCounts(TagCountScanDeferredSliceMs, -1);
                while (merge.MoveNext()) yield return merge.Current;
                string tckPut;
                if (TryBuildTagCountCacheKey(out tckPut))
                {
                    try { GalleryTagCountSnapshotCache.Put(tckPut, CaptureTagCountSnapshot()); } catch { }
                }
                try { RebuildSubPaneSideTabListsOnly(); } catch { }
            }
            finally
            {
                _appearanceLooseMergeCo = null;
            }
        }

        /// <summary>Recount gender/source chips from loose .vap under the current path only (milliseconds).</summary>
        private bool TryRecomputeAppearanceGenderFacetCountsScoped()
        {
            if (!IsAppearanceLooseScopedBrowsing()) return false;
            AccumulateLooseVapAppearanceGenderCounts(resetCountsFirst: true);
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
