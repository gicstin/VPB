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

        /// <summary>Appearance grid narrowed to loose/custom only (global Source Local).</summary>
        private bool IsAppearanceLocalOnlyActive()
        {
            if (!IsAppearanceCategoryTitle()) return false;
            return IsGlobalSourceFilterLocal();
        }

        private bool IsAppearanceLooseScopedBrowsing() => IsAppearanceLocalOnlyActive();

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
            tagFacets.AppearanceSubfilterCountAll = 0;
            tagFacets.AppearanceSubfilterCountPresets = 0;
            tagFacets.AppearanceSubfilterCountCustom = 0;
            tagFacets.AppearanceSubfilterCountMale = 0;
            tagFacets.AppearanceSubfilterCountFemale = 0;
            tagFacets.AppearanceSubfilterCountFuta = 0;
            tagFacets.AppearanceSubfilterCountUnknown = 0;

            tagFacets.AppearanceSubfilterFacetCountPresets = 0;
            tagFacets.AppearanceSubfilterFacetCountCustom = 0;
            tagFacets.AppearanceSubfilterFacetCountMale = 0;
            tagFacets.AppearanceSubfilterFacetCountFemale = 0;
            tagFacets.AppearanceSubfilterFacetCountFuta = 0;
            tagFacets.AppearanceSubfilterFacetCountUnknown = 0;

            tagFacets.AppearanceSubfilterCurrentCountAll = 0;
            tagFacets.AppearanceSubfilterCurrentCountMale = 0;
            tagFacets.AppearanceSubfilterCurrentCountFemale = 0;
            tagFacets.AppearanceSubfilterCurrentCountFuta = 0;
            tagFacets.AppearanceSubfilterCurrentCountUnknown = 0;

            tagFacets.AppearanceSourceCountAll = 0;
            tagFacets.AppearanceSourceCountPresets = 0;
            tagFacets.AppearanceSourceCountCustom = 0;
        }

        private void CollectAppearanceSearchPaths(List<string> pathsToSearch)
        {
            pathsToSearch.Clear();
            if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
            else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);
        }

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

                    tagFacets.AppearanceSourceCountAll++;
                    if (isCustomLoose) tagFacets.AppearanceSourceCountCustom++;
                    if (isPresetLoose) tagFacets.AppearanceSourceCountPresets++;

                    AppearanceGender lg;
                    try { lg = AppearanceGenderClassifier.ClassifyLooseVapPath(sysPath, cat ?? "", _appearanceUserTagsByRowKey, genderBulk); }
                    catch { lg = AppearanceGender.Unknown; }

                    tagFacets.AppearanceSubfilterCountAll++;
                    if (isPresetLoose) tagFacets.AppearanceSubfilterCountPresets++;
                    if (isCustomLoose) tagFacets.AppearanceSubfilterCountCustom++;
                    if (lg == AppearanceGender.Male) tagFacets.AppearanceSubfilterCountMale++;
                    if (lg == AppearanceGender.Female) tagFacets.AppearanceSubfilterCountFemale++;
                    if (lg == AppearanceGender.Futa) tagFacets.AppearanceSubfilterCountFuta++;
                    if (lg == AppearanceGender.Unknown) tagFacets.AppearanceSubfilterCountUnknown++;

                    if (LoosePassesAppearanceSubfilter(aSub ^ AppearanceSubfilter.Presets, isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountPresets++;
                    if (LoosePassesAppearanceSubfilter(aSub ^ AppearanceSubfilter.Custom, isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountCustom++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Male), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountMale++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Female), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountFemale++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Futa), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountFuta++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Unknown), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountUnknown++;

                    if (LoosePassesAppearanceSubfilter(aSub, isPresetLoose, isCustomLoose, lg))
                    {
                        tagFacets.AppearanceSubfilterCurrentCountAll++;
                        if (lg == AppearanceGender.Male) tagFacets.AppearanceSubfilterCurrentCountMale++;
                        if (lg == AppearanceGender.Female) tagFacets.AppearanceSubfilterCurrentCountFemale++;
                        if (lg == AppearanceGender.Futa) tagFacets.AppearanceSubfilterCurrentCountFuta++;
                        if (lg == AppearanceGender.Unknown) tagFacets.AppearanceSubfilterCurrentCountUnknown++;
                    }
                }
            }
            genderBulk.Flush();
        }

        private IEnumerator CoMergeLooseVapAppearanceGenderFacetCounts(int maxMsPerSlice, int deferredSessionId, bool resetCountsFirst)
        {
            if (!ShouldCountLooseAppearanceGenderFiles()) yield break;

            if (resetCountsFirst)
                ResetAppearanceGenderFacetCounts();

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

                    tagFacets.AppearanceSourceCountAll++;
                    if (isCustomLoose) tagFacets.AppearanceSourceCountCustom++;
                    if (isPresetLoose) tagFacets.AppearanceSourceCountPresets++;

                    AppearanceGender lg;
                    try { lg = AppearanceGenderClassifier.ClassifyLooseVapPath(sysPath, cat ?? "", _appearanceUserTagsByRowKey, genderBulk); }
                    catch { lg = AppearanceGender.Unknown; }

                    tagFacets.AppearanceSubfilterCountAll++;
                    if (isPresetLoose) tagFacets.AppearanceSubfilterCountPresets++;
                    if (isCustomLoose) tagFacets.AppearanceSubfilterCountCustom++;
                    if (lg == AppearanceGender.Male) tagFacets.AppearanceSubfilterCountMale++;
                    if (lg == AppearanceGender.Female) tagFacets.AppearanceSubfilterCountFemale++;
                    if (lg == AppearanceGender.Futa) tagFacets.AppearanceSubfilterCountFuta++;
                    if (lg == AppearanceGender.Unknown) tagFacets.AppearanceSubfilterCountUnknown++;

                    if (LoosePassesAppearanceSubfilter(aSub ^ AppearanceSubfilter.Presets, isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountPresets++;
                    if (LoosePassesAppearanceSubfilter(aSub ^ AppearanceSubfilter.Custom, isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountCustom++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Male), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountMale++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Female), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountFemale++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Futa), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountFuta++;
                    if (LoosePassesAppearanceSubfilter(AppearanceGenderClassifier.HypotheticalGenderFacet(aSub, AppearanceSubfilter.Unknown), isPresetLoose, isCustomLoose, lg)) tagFacets.AppearanceSubfilterFacetCountUnknown++;

                    if (LoosePassesAppearanceSubfilter(aSub, isPresetLoose, isCustomLoose, lg))
                    {
                        tagFacets.AppearanceSubfilterCurrentCountAll++;
                        if (lg == AppearanceGender.Male) tagFacets.AppearanceSubfilterCurrentCountMale++;
                        if (lg == AppearanceGender.Female) tagFacets.AppearanceSubfilterCurrentCountFemale++;
                        if (lg == AppearanceGender.Futa) tagFacets.AppearanceSubfilterCurrentCountFuta++;
                        if (lg == AppearanceGender.Unknown) tagFacets.AppearanceSubfilterCurrentCountUnknown++;
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
            if (IsAppearanceLooseScopedBrowsing())
            {
                ScheduleAppearanceLooseScopedSliceRecount(_deferredSubPaneSessionId);
                return;
            }
            StopCo(ref _appearanceLooseMergeCo);
            _appearanceLooseMergeCo = StartCoroutine(CoAppearanceLooseMergeRefresh());
        }

        private IEnumerator CoAppearanceLooseMergeRefresh()
        {
            try
            {
                IEnumerator merge = CoMergeLooseVapAppearanceGenderFacetCounts(TagCountScanDeferredSliceMs, -1, resetCountsFirst: false);
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

        /// <summary>Source:Local Appearance — sliced full loose-.vap recount (never sync Accumulate on large trees).</summary>
        private void ScheduleAppearanceLooseScopedSliceRecount(int deferredSessionId)
        {
            if (!ShouldCountLooseAppearanceGenderFiles()) return;
            if (!IsAppearanceLooseScopedBrowsing()) return;
            StopCo(ref _appearanceLooseMergeCo);
            int sessionSnap = deferredSessionId >= 0 ? deferredSessionId : _deferredSubPaneSessionId;
            _appearanceLooseMergeCo = StartCoroutine(CoAppearanceLooseScopedSliceRecount(sessionSnap));
        }

        private IEnumerator CoAppearanceLooseScopedSliceRecount(int sessionWhenStarted)
        {
            try
            {
                IEnumerator merge = CoMergeLooseVapAppearanceGenderFacetCounts(TagCountScanDeferredSliceMs, sessionWhenStarted, resetCountsFirst: true);
                while (merge.MoveNext())
                {
                    if (sessionWhenStarted >= 0 && sessionWhenStarted != _deferredSubPaneSessionId)
                        yield break;
                    yield return merge.Current;
                }
                if (sessionWhenStarted >= 0 && sessionWhenStarted != _deferredSubPaneSessionId)
                    yield break;
                tagsCached = true;
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

        private bool TryRecomputeAppearanceGenderFacetCountsScoped()
        {
            if (!IsAppearanceLooseScopedBrowsing()) return false;
            bool primedSql = false;
            try { primedSql = TryApplyAppearanceFacetCountsFromSql(); } catch { primedSql = false; }
            ScheduleAppearanceLooseScopedSliceRecount(_deferredSubPaneSessionId);
            return primedSql;
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
