using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using SimpleJSON;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        /// <summary>
        /// Splits a search query into lowercase terms (whitespace separated), removing empties.
        /// </summary>
        internal static string[] SplitSearchTerms(string query)
        {
            // .NET 3.5 compatibility: no string.IsNullOrWhiteSpace / Array.Empty<T>()
            if (query == null) return new string[0];
            query = query.Trim();
            if (query.Length == 0) return new string[0];

            // Avoid allocations for common small queries.
            string[] raw = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (raw.Length == 0) return new string[0];
            for (int i = 0; i < raw.Length; i++)
                raw[i] = raw[i].ToLowerInvariant();
            return raw;
        }

        /// <summary>True if every term appears in either <paramref name="a"/> or <paramref name="b"/> (case-insensitive).</summary>
        internal static bool MatchesAllTermsInEither(string a, string b, string[] termsLower)
        {
            if (termsLower == null || termsLower.Length == 0) return true;
            if (a == null) a = "";
            if (b == null) b = "";
            for (int i = 0; i < termsLower.Length; i++)
            {
                string t = termsLower[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (a.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (b.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                return false;
            }
            return true;
        }

        /// <summary>VAR zip paths often use '\'; category roots use '/'. Matches Browser Assist-style Custom/Scripts checks.</summary>
        private static string GalleryNormalizePathSlashes(string p)
        {
            return string.IsNullOrEmpty(p) ? p : p.Replace('\\', '/');
        }

        /// <summary>True if internal path is under prefix (after slash normalization).</summary>
        private static bool GalleryInternalPathStartsWithPrefix(string internalPath, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return true;
            return GalleryNormalizePathSlashes(internalPath).StartsWith(GalleryNormalizePathSlashes(prefix), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Path rules for VAR file worker / SQLite index path (matches <see cref="GalleryPanel.IO"/> RefreshFilesRoutine).</summary>
        internal static bool RefreshWorkerPathMatches(string checkPath, List<string> currentPaths, string currentPath)
        {
            bool pathOk = true;
            if (currentPaths != null && currentPaths.Count > 0)
            {
                pathOk = false;
                for (int p = 0; p < currentPaths.Count; p++)
                {
                    string pref = currentPaths[p];
                    if (GalleryInternalPathStartsWithPrefix(checkPath, pref))
                    {
                        string prefN = GalleryNormalizePathSlashes(pref).TrimEnd('/');
                        if (string.Equals(prefN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                        {
                            if (GalleryNormalizePathSlashes(checkPath).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        pathOk = true;
                        break;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(currentPath))
            {
                pathOk = false;
                if (GalleryInternalPathStartsWithPrefix(checkPath, currentPath))
                {
                    string curN = GalleryNormalizePathSlashes(currentPath).TrimEnd('/');
                    if (string.Equals(curN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!GalleryNormalizePathSlashes(checkPath).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                            pathOk = true;
                    }
                    else
                    {
                        pathOk = true;
                    }
                }
            }
            return pathOk;
        }

        public string CurrentCategoryTitle => currentCategoryTitle;
        public GalleryLayoutMode LayoutMode => layoutMode;

        public static float BenchmarkStartTime = 0f;

        public void SetLayoutMode(GalleryLayoutMode mode)
        {
            if (layoutMode == mode) return;
            
            if (mode == GalleryLayoutMode.List)
            {
                 BenchmarkStartTime = Time.realtimeSinceStartup;
                 UnityEngine.Debug.Log("[Benchmark] Starting Switch to List Mode at " + BenchmarkStartTime);
            }

            layoutMode = mode;

            // Persist across restarts
            try
            {
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.GalleryLayoutMode = (int)layoutMode;
                    VPBConfig.Instance.Save(true, true);
                }
            }
            catch { }
            
            // ALWAYS use internal UI now
            if (scrollRect != null) scrollRect.gameObject.SetActive(true);

            UpdateFooterLayoutState();
            UpdateLayout();

            // Layout switch should not force a full RefreshFiles().
            // The grid items support both modes, so we just reconfigure and rebind visible rows.
            try
            {
                if (contentGO != null)
                {
                    var rgv = contentGO.GetComponent<RecyclingGridView>();
                    if (rgv != null)
                    {
                        try { rgv.preserveCenterItemIndex = rgv.GetCenterItemIndex(); } catch { }

                        if (layoutMode == GalleryLayoutMode.List)
                        {
                            rgv.SetGridConfig(100f, ListRowHeight, 5f, 5f, 1);
                            rgv.SetAdaptiveConfig(true, 0f, 1, true);
                        }
                        else
                        {
                            int cols = GridColumnCount;
                            rgv.SetGridConfig(100f, 100f, 10f, 10f, cols);
                            rgv.SetAdaptiveConfig(true, 200f, cols, false);
                        }
                        rgv.Refresh();
                    }
                }
            }
            catch { }
        }

        public Atom SelectedTargetAtom
        {
            get
            {
                if (personAtoms == null || targetDropdownValue < 0 || targetDropdownValue >= personAtoms.Count)
                    return null;
                Atom a = personAtoms[targetDropdownValue];
                if (a == null) return null;
                try { _ = a.uid; return a; } catch { return null; }
            }
        }

        private void CacheCategoryCounts()
        {
            if (categories == null) return;
            categoryCounts.Clear();
            
            // Build optimized lookup map for categories by extension
            // Map: Extension (lowercase, no dot) -> List of Categories
            Dictionary<string, List<Gallery.Category>> extToCats = new Dictionary<string, List<Gallery.Category>>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var c in categories) 
            {
                categoryCounts[c.name] = 0;
                if (string.IsNullOrEmpty(c.extension)) continue;
                string[] exts = c.extension.Split('|');
                foreach(string ext in exts)
                {
                    if (string.IsNullOrEmpty(ext)) continue;
                    string e = ext.Trim();
                    if (!extToCats.ContainsKey(e)) extToCats[e] = new List<Gallery.Category>();
                    extToCats[e].Add(c);
                }
            }

            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    // Filter by creator if set
                    if (!string.IsNullOrEmpty(currentCreator))
                    {
                        if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                    }

                    if (pkg.FileEntries == null) continue;
                    
                    int count = pkg.FileEntries.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var entry = pkg.FileEntries[i];
                        string internalPath = entry.InternalPath;
                        
                        // Fast extension extraction
                        int lastDot = internalPath.LastIndexOf('.');
                        if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                        
                        string ext = internalPath.Substring(lastDot + 1);
                        
                        List<Gallery.Category> candidates;
                        if (extToCats.TryGetValue(ext, out candidates))
                        {
                            int candCount = candidates.Count;
                            for (int j = 0; j < candCount; j++)
                            {
                                var cat = candidates[j];
                                // Check path match
                                bool pathMatch = false;
                                if (cat.paths != null && cat.paths.Count > 0)
                                {
                                    int pCount = cat.paths.Count;
                                    for(int k=0; k<pCount; k++)
                                    {
                                        if (GalleryInternalPathStartsWithPrefix(internalPath, cat.paths[k]))
                                        {
                                            pathMatch = true;
                                            break;
                                        }
                                    }
                                }
                                else if (!string.IsNullOrEmpty(cat.path))
                                {
                                    if (GalleryInternalPathStartsWithPrefix(internalPath, cat.path))
                                        pathMatch = true;
                                }
                                else
                                {
                                    // No path specified means match all (unlikely for category but possible)
                                    pathMatch = true;
                                }

                                if (pathMatch)
                                {
                                    categoryCounts[cat.name]++;
                                    break; // File belongs to one category
                                }
                            }
                        }
                    }
                }
            }

            // Tab counts are VAR-only above; Custom/Scripts plugins live on local disk (same tree RefreshFiles scans).
            AddLocalCustomScriptsCountToCategory(categoryCounts);

            categoriesCached = true;
            unchecked { categorySideTabDataRevision++; }
        }

        /// <summary>
        /// Count .cs / .cslist / .dll under Custom/Scripts on disk so the Plugins category is not stuck at 0.
        /// (Package-only counting misses almost all session plugins.)
        /// </summary>
        private static void AddLocalCustomScriptsCountToCategory(Dictionary<string, int> counts)
        {
            if (counts == null || !counts.ContainsKey("Plugins")) return;
            const string root = "Custom/Scripts";
            try
            {
                if (!Directory.Exists(root)) return;
            }
            catch { return; }

            // Prefer SQLite-cached enumeration to avoid recursive disk walks on every side-tab rebuild.
            var exts = new[] { "cs", "cslist", "dll" };
            string sig = "0";
            try { sig = Directory.GetLastWriteTimeUtc(root).ToBinary().ToString(); } catch { sig = "0"; }
            string cacheKey = "plugins:custom_scripts|root=" + (Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/')) + "|exts=cs,cslist,dll";

            int n = 0;
            try
            {
                var cached = new List<VpbLocalDatabase.SystemFileRow>();
                bool hit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(cacheKey, sig, cached);
                if (hit && cached.Count > 0)
                {
                    n = cached.Count;
                }
                else
                {
                    var rows = new List<VpbLocalDatabase.SystemFileRow>(256);
                    for (int ei = 0; ei < exts.Length; ei++)
                    {
                        string ext = exts[ei];
                        var buf = new List<string>();
                        try
                        {
                            FileManager.SafeGetFiles(root, "*." + ext, buf);
                            n += buf.Count;
                            for (int i = 0; i < buf.Count; i++)
                            {
                                string p = buf[i];
                                if (string.IsNullOrEmpty(p)) continue;
                                var r = new VpbLocalDatabase.SystemFileRow();
                                try { r.Path = Path.GetFullPath(p); } catch { r.Path = p; }
                                r.LastWriteBinaryOrInvalid = long.MinValue;
                                r.SizeOrInvalid = long.MinValue;
                                rows.Add(r);
                            }
                        }
                        catch { }
                    }
                    if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(cacheKey, sig, rows);
                }
            }
            catch { }
            counts["Plugins"] += n;
        }

        private void CacheCreators()
        {
            if (FileManager.PackagesByUid == null) return;

            Dictionary<string, int> counts = new Dictionary<string, int>();
            if (!VpbLocalDatabase.TryReadCreatorFileCounts(counts, currentExtension, currentPaths, currentPath))
            {
                string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
                HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in extensions) if (!string.IsNullOrEmpty(e)) targetExts.Add(e.Trim());

                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    if (string.IsNullOrEmpty(pkg.Creator)) continue;
                    if (pkg.FileEntries == null) continue;

                    int count = pkg.FileEntries.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var entry = pkg.FileEntries[i];
                        string internalPath = entry.InternalPath;

                        int lastDot = internalPath.LastIndexOf('.');
                        if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                        string ext = internalPath.Substring(lastDot + 1);
                        if (!targetExts.Contains(ext)) continue;

                        bool match = false;
                        if (currentPaths != null && currentPaths.Count > 0)
                        {
                            for (int k = 0; k < currentPaths.Count; k++)
                            {
                                if (GalleryInternalPathStartsWithPrefix(internalPath, currentPaths[k])) { match = true; break; }
                            }
                        }
                        else if (!string.IsNullOrEmpty(currentPath))
                        {
                            if (GalleryInternalPathStartsWithPrefix(internalPath, currentPath)) match = true;
                        }
                        else
                        {
                            match = true;
                        }

                        if (match)
                        {
                            int cur;
                            counts.TryGetValue(pkg.Creator, out cur);
                            counts[pkg.Creator] = cur + 1;
                        }
                    }
                }
            }

            cachedCreators = counts.Select(kv => new CreatorCacheEntry { Name = kv.Key, Count = kv.Value })
                                   .OrderBy(c => c.Name).ToList();
            creatorsCached = true;
            unchecked { creatorSideTabDataRevision++; }
        }

        public void InvalidateTags()
        {
            tagsCached = false;
            try { GalleryTagCountSnapshotCache.Clear(); } catch { }
        }

        /// <summary>When max slice ms is below this, <see cref="CoCacheTagCountsInternal"/> yields so the UI thread stays responsive.</summary>
        private const int TagCountScanNoSliceMs = 1_000_000;

        /// <summary>Per-frame budget when tag counting runs from <see cref="GalleryPanel.DeferredGallerySideTabsAfterGridReady"/>.</summary>
        private const int TagCountScanDeferredSliceMs = 20;

        private static bool TagCountScanShouldYieldFrame(int maxMsPerSlice, Stopwatch sliceWatch, int deferredSessionId, int currentDeferredSessionId, out bool cancelled)
        {
            cancelled = false;
            if (maxMsPerSlice >= TagCountScanNoSliceMs || sliceWatch == null) return false;
            if (sliceWatch.ElapsedMilliseconds < maxMsPerSlice) return false;
            sliceWatch.Reset();
            sliceWatch.Start();
            if (deferredSessionId >= 0 && deferredSessionId != currentDeferredSessionId)
            {
                cancelled = true;
                return false;
            }
            return true;
        }

        /// <summary>Runs the full tag/facet scan on the current thread (can block for many seconds). Prefer <see cref="ScheduleTagCountsForSideTabsNonBlocking"/> from UI paths.</summary>
        private void CacheTagCounts()
        {
            var e = CoCacheTagCountsInternal(TagCountScanNoSliceMs, -1);
            while (e.MoveNext()) { }
        }

        /// <summary>
        /// Used from <see cref="GalleryPanel.UpdateTabs"/> when <c>!tagsCached</c>: same work as <see cref="CacheTagCounts"/> but time-sliced so Clothing subfilter clicks do not freeze the UI
        /// while <see cref="GalleryPanel.RefreshFiles"/> is also queued (previously both compounded on the main thread).
        /// </summary>
        private void ScheduleTagCountsForSideTabsNonBlocking()
        {
            if (tagsCached) return;
            if (_sideTabsTagCountSliceCo != null)
                return;
            int sessionSnap = _deferredSubPaneSessionId;
            _sideTabsTagCountSliceCo = StartCoroutine(CoTagCountsForSideTabsSlice(sessionSnap));
        }

        private IEnumerator CoTagCountsForSideTabsSlice(int sessionWhenStarted)
        {
            try
            {
                IEnumerator scan = CoCacheTagCountsInternal(TagCountScanDeferredSliceMs, sessionWhenStarted);
                while (scan.MoveNext())
                {
                    if (sessionWhenStarted != _deferredSubPaneSessionId)
                        yield break;
                    yield return scan.Current;
                }
                if (!tagsCached || sessionWhenStarted != _deferredSubPaneSessionId)
                    yield break;
                try { RebuildSubPaneSideTabListsOnly(); } catch { }
            }
            finally
            {
                _sideTabsTagCountSliceCo = null;
            }
        }

        private void ApplyTagScanTotalsFromWorker(GalleryTagCountBackgroundScan.TagScanTotals t)
        {
            if (t == null) return;
            appearanceSourceCountAll = t.AppearanceSourceCountAll;
            appearanceSourceCountPresets = t.AppearanceSourceCountPresets;
            appearanceSourceCountCustom = t.AppearanceSourceCountCustom;
            clothingSubfilterCountAll = t.ClothingSubfilterCountAll;
            clothingSubfilterCountReal = t.ClothingSubfilterCountReal;
            clothingSubfilterCountPresets = t.ClothingSubfilterCountPresets;
            clothingSubfilterCountCustom = t.ClothingSubfilterCountCustom;
            clothingSubfilterCountItems = t.ClothingSubfilterCountItems;
            clothingSubfilterCountMale = t.ClothingSubfilterCountMale;
            clothingSubfilterCountFemale = t.ClothingSubfilterCountFemale;
            clothingSubfilterCountDecals = t.ClothingSubfilterCountDecals;
            appearanceSubfilterCountAll = t.AppearanceSubfilterCountAll;
            appearanceSubfilterCountPresets = t.AppearanceSubfilterCountPresets;
            appearanceSubfilterCountCustom = t.AppearanceSubfilterCountCustom;
            appearanceSubfilterCountMale = t.AppearanceSubfilterCountMale;
            appearanceSubfilterCountFemale = t.AppearanceSubfilterCountFemale;
            appearanceSubfilterCountFuta = t.AppearanceSubfilterCountFuta;
            clothingSubfilterFacetCountReal = t.ClothingSubfilterFacetCountReal;
            clothingSubfilterFacetCountPresets = t.ClothingSubfilterFacetCountPresets;
            clothingSubfilterFacetCountCustom = t.ClothingSubfilterFacetCountCustom;
            clothingSubfilterFacetCountItems = t.ClothingSubfilterFacetCountItems;
            clothingSubfilterFacetCountMale = t.ClothingSubfilterFacetCountMale;
            clothingSubfilterFacetCountFemale = t.ClothingSubfilterFacetCountFemale;
            clothingSubfilterFacetCountDecals = t.ClothingSubfilterFacetCountDecals;
            appearanceSubfilterFacetCountPresets = t.AppearanceSubfilterFacetCountPresets;
            appearanceSubfilterFacetCountCustom = t.AppearanceSubfilterFacetCountCustom;
            appearanceSubfilterFacetCountMale = t.AppearanceSubfilterFacetCountMale;
            appearanceSubfilterFacetCountFemale = t.AppearanceSubfilterFacetCountFemale;
            appearanceSubfilterFacetCountFuta = t.AppearanceSubfilterFacetCountFuta;
            appearanceSubfilterCurrentCountAll = t.AppearanceSubfilterCurrentCountAll;
            appearanceSubfilterCurrentCountMale = t.AppearanceSubfilterCurrentCountMale;
            appearanceSubfilterCurrentCountFemale = t.AppearanceSubfilterCurrentCountFemale;
            appearanceSubfilterCurrentCountFuta = t.AppearanceSubfilterCurrentCountFuta;
        }

        private IEnumerator CoCacheTagCountsInternal(int maxMsPerSlice, int deferredSessionId)
        {
            tagCounts.Clear();
            if (FileManager.PackagesByUid == null) yield break;

            string tagCountCacheKey;
            if (TryBuildTagCountCacheKey(out tagCountCacheKey))
            {
                TagCountSnapshot cachedSnap;
                if (GalleryTagCountSnapshotCache.TryGet(tagCountCacheKey, out cachedSnap))
                {
                    RestoreTagCountSnapshot(cachedSnap);
                    tagsCached = true;
                    yield break;
                }
            }

            Stopwatch sliceWatch = (maxMsPerSlice < TagCountScanNoSliceMs) ? Stopwatch.StartNew() : null;

            appearanceSourceCountAll = 0;
            appearanceSourceCountPresets = 0;
            appearanceSourceCountCustom = 0;

            clothingSubfilterCountAll = 0;
            clothingSubfilterCountReal = 0;
            clothingSubfilterCountPresets = 0;
            clothingSubfilterCountCustom = 0;
            clothingSubfilterCountItems = 0;
            clothingSubfilterCountMale = 0;
            clothingSubfilterCountFemale = 0;
            clothingSubfilterCountDecals = 0;

            appearanceSubfilterCountAll = 0;
            appearanceSubfilterCountPresets = 0;
            appearanceSubfilterCountCustom = 0;
            appearanceSubfilterCountMale = 0;
            appearanceSubfilterCountFemale = 0;
            appearanceSubfilterCountFuta = 0;

            clothingSubfilterFacetCountReal = 0;
            clothingSubfilterFacetCountPresets = 0;
            clothingSubfilterFacetCountCustom = 0;
            clothingSubfilterFacetCountItems = 0;
            clothingSubfilterFacetCountMale = 0;
            clothingSubfilterFacetCountFemale = 0;
            clothingSubfilterFacetCountDecals = 0;

            appearanceSubfilterFacetCountPresets = 0;
            appearanceSubfilterFacetCountCustom = 0;
            appearanceSubfilterFacetCountMale = 0;
            appearanceSubfilterFacetCountFemale = 0;
            appearanceSubfilterFacetCountFuta = 0;

            appearanceSubfilterCurrentCountAll = 0;
            appearanceSubfilterCurrentCountMale = 0;
            appearanceSubfilterCurrentCountFemale = 0;
            appearanceSubfilterCurrentCountFuta = 0;

            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            // Build extension set for fast lookup
            HashSet<string> targetExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in extensions) if (!string.IsNullOrEmpty(e)) targetExts.Add(e.Trim());

            // Collect all relevant tags to count
            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string title = titleText != null ? titleText.text : "";
            bool isClothingTitle = (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0);
            bool isAppearanceTitle = (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0);
            if (title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagsToCount.UnionWith(TagFilter.AllClothingTags);
                tagsToCount.UnionWith(TagFilter.ClothingUnknownTags);
            }
            else if (title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagsToCount.UnionWith(TagFilter.AllHairTags);
                tagsToCount.UnionWith(TagFilter.HairUnknownTags);
            }
            
            // Include user-defined tags
            tagsToCount.UnionWith(TagsManager.Instance.GetAllUserTags());

            bool hasAnyTagsToCount = (tagsToCount.Count > 0);

            // Split tags into single-word and multi-word
            HashSet<string> singleWordTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> multiWordTags = new List<string>();
            char[] separators = new char[] { '/', '\\', '.', '_', '-', ' ' };
            char[] multiWordSeparators = new char[] { ' ', '_', '-' };

            if (hasAnyTagsToCount)
            {
                foreach (var t in tagsToCount)
                {
                    if (t.IndexOfAny(multiWordSeparators) >= 0) multiWordTags.Add(t);
                    else singleWordTags.Add(t);
                }
            }

            HashSet<string> foundTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TagCountParallelInputs tagParForScan;
            TryBuildTagCountParallelInputs(out tagParForScan);
            var coTagScanTotals = new GalleryTagCountBackgroundScan.TagScanTotals();
            bool coVarFromSql = false;
            if (tagParForScan != null && VpbSqlite3.IsAvailable && sliceWatch != null)
            {
                if (deferredSessionId >= 0 && deferredSessionId != _deferredSubPaneSessionId) yield break;
                // One frame for RefreshFilesRoutine / file-list worker to start before we synchronously load huge SQL row lists here.
                yield return null;
                if (deferredSessionId >= 0 && deferredSessionId != _deferredSubPaneSessionId) yield break;
            }
            if (tagParForScan != null && VpbSqlite3.IsAvailable)
            {
                var tagSqlRowsCo = new List<VpbLocalDatabase.Row>();
                string extJ = GalleryTagCountBackgroundScan.JoinExtensionsForTagScan(tagParForScan.ExtensionsSplit);
                if (VpbLocalDatabase.TryQueryGalleryCategoryRows(tagParForScan.Title, extJ, tagParForScan.CurrentCreator ?? "", tagSqlRowsCo, out _))
                {
                    coVarFromSql = true;
                    for (int ri = 0; ri < tagSqlRowsCo.Count; ri++)
                    {
                        if ((ri & 0xFF) == 0xFF)
                        {
                            if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledSqlTag))
                                yield return null;
                            if (cancelledSqlTag) yield break;
                        }
                        VpbLocalDatabase.Row row = tagSqlRowsCo[ri];
                        GalleryTagCountBackgroundScan.TagScanProcessOneVarRow(tagParForScan, row.InternalPath, row.PackageUid ?? "", targetExts, tagCounts, foundTags, coTagScanTotals);
                    }
                }
            }

            if (!coVarFromSql)
            {
            foreach (var pkg in FileManager.PackagesByUid.Values)
            {
                if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledPkg))
                    yield return null;
                if (cancelledPkg) yield break;

                if (pkg.FileEntries == null) continue;
                
                // If filtering by creator, respect it
                if (!string.IsNullOrEmpty(currentCreator))
                {
                    if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                }

                int count = pkg.FileEntries.Count;
                for (int i = 0; i < count; i++)
                {
                    if ((i & 0xFF) == 0xFF)
                    {
                        if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledEntry))
                            yield return null;
                        if (cancelledEntry) yield break;
                    }

                    var entry = pkg.FileEntries[i];
                    string internalPath = entry.InternalPath;

                    // 1. Check extension
                    int lastDot = internalPath.LastIndexOf('.');
                    if (lastDot < 0 || lastDot == internalPath.Length - 1) continue;
                    string ext = internalPath.Substring(lastDot + 1);
                    if (!targetExts.Contains(ext)) continue;

                    // 2. Check path match (Inline IsMatch logic)
                    bool match = false;
                    if (currentPaths != null && currentPaths.Count > 0)
                    {
                        for(int k=0; k<currentPaths.Count; k++)
                        {
                            if (internalPath.StartsWith(currentPaths[k], StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                        }
                    }
                    else if (!string.IsNullOrEmpty(currentPath))
                    {
                         if (internalPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase)) match = true;
                    }
                    else
                    {
                        match = true;
                    }

                    if (!match) continue;

                    if (isClothingTitle)
                    {
						ClothingLoadingUtils.ResourceKind ck = ClothingLoadingUtils.ResourceKind.Unknown;
						ClothingLoadingUtils.ResourceGender cg = ClothingLoadingUtils.ResourceGender.Unknown;
						bool isClothingEntry = false;
						bool isPresetEntry = false;
						bool isCustomPreset = false;

						ClothingLoadingUtils.ClassifyClothingHairPath(internalPath, out ck, out cg);
						isClothingEntry = (ck == ClothingLoadingUtils.ResourceKind.Clothing);
						if (isClothingEntry)
                        {
                            // For Clothing category we include both .vam and .vap, and subfilters split them.
							isPresetEntry = (ext.Equals("vap", StringComparison.OrdinalIgnoreCase));
							// VAR entries are never considered "Custom".
							isCustomPreset = false;

                            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(internalPath);

                            ClothingSubfilter cur = clothingSubfilter;
                            bool PassesClothingSubfilters(ClothingSubfilter f)
                            {
                                if (f == 0) return true;

                                bool wantsRealType = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                bool wantsDecalType = ((f & ClothingSubfilter.Decals) != 0);

                                bool typeExplicit = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                if (typeExplicit)
                                {
                                    bool okType = (!isDecal && (f & ClothingSubfilter.RealClothing) != 0) ||
                                                  (isDecal && (f & ClothingSubfilter.Decals) != 0);
                                    if (!okType) return false;
                                }
                                else
                                {
                                    if (wantsRealType && isDecal && !wantsDecalType) return false;
                                }

                                bool wantsPresets = (f & ClothingSubfilter.Presets) != 0;
								bool wantsCustom = (f & ClothingSubfilter.Custom) != 0;
								if (wantsPresets) { if (!isPresetEntry) return false; }
								if (wantsCustom) { if (!isCustomPreset) return false; }
								if ((f & ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
								if ((f & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return false; }
								if ((f & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return false; }

                                return true;
                            }

                            // Facet counts: how many would be shown if the user toggled that flag now.
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.RealClothing)) clothingSubfilterFacetCountReal++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Presets)) clothingSubfilterFacetCountPresets++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Custom)) clothingSubfilterFacetCountCustom++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Items)) clothingSubfilterFacetCountItems++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Male)) clothingSubfilterFacetCountMale++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Female)) clothingSubfilterFacetCountFemale++;
							if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Decals)) clothingSubfilterFacetCountDecals++;

							// All Clothing includes everything: real clothing + decals
							clothingSubfilterCountAll++;

                            // Decals are counted separately and excluded from real clothing filters by default.
                            if (isDecal)
                            {
                                clothingSubfilterCountDecals++;

                                // Apply active subfilters (if any) to tag counting.
                                if (clothingSubfilter != 0)
                                {
                                    bool wantsRealType = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                    bool wantsDecalType = ((clothingSubfilter & ClothingSubfilter.Decals) != 0);

                                    bool typeExplicit = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        if ((clothingSubfilter & ClothingSubfilter.Decals) == 0) continue;
                                    }
                                    else
                                    {
                                        if (wantsRealType && !wantsDecalType) continue;
                                    }

                                    // If user also selected real-only constraints, decals won't match.
                                    if ((clothingSubfilter & (ClothingSubfilter.Presets | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0) continue;
                                }
                            }
                            else
                            {
                                clothingSubfilterCountReal++;
                                if (isPresetEntry) clothingSubfilterCountPresets++;
								if (isCustomPreset) clothingSubfilterCountCustom++;
								if (!isPresetEntry) clothingSubfilterCountItems++;
                                if (cg == ClothingLoadingUtils.ResourceGender.Male) clothingSubfilterCountMale++;
                                else if (cg == ClothingLoadingUtils.ResourceGender.Female) clothingSubfilterCountFemale++;

                                // Apply active subfilters (if any) to tag counting.
                                if (clothingSubfilter != 0)
                                {
                                    bool typeExplicit = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        if ((clothingSubfilter & ClothingSubfilter.RealClothing) == 0) continue;
                                    }
                                    // Additional constraints
                                    if ((clothingSubfilter & ClothingSubfilter.Presets) != 0) { if (!isPresetEntry) continue; }
								if ((clothingSubfilter & ClothingSubfilter.Custom) != 0) { if (!isCustomPreset) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Items) != 0) { if (isPresetEntry) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) continue; }
                                    if ((clothingSubfilter & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) continue; }
                                }
                            }
                        }
                        else
                        {
                            // When browsing Clothing, ignore non-clothing entries for tag counts.
                            continue;
                        }
                    }

                    if (isAppearanceTitle)
                    {
                        string p = internalPath.Replace('\\', '/');
                        bool isAppearance = p.IndexOf("/appearance", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isAppearance)
                        {
                            // When browsing Appearance, ignore non-appearance entries for tag counts.
                            continue;
                        }

                        bool isCustomAppearance = p.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                        bool isPresetAppearance = p.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);

                        AppearanceGender g = AppearanceGender.Unknown;
                        try { g = GetAppearanceGender(entry); } catch { g = AppearanceGender.Unknown; }

                        appearanceSubfilterCountAll++;
                        if (isPresetAppearance) appearanceSubfilterCountPresets++;
                        if (isCustomAppearance) appearanceSubfilterCountCustom++;
                        if (g == AppearanceGender.Male) appearanceSubfilterCountMale++;
                        if (g == AppearanceGender.Female) appearanceSubfilterCountFemale++;
                        if (g == AppearanceGender.Futa) appearanceSubfilterCountFuta++;

                        AppearanceSubfilter cur = appearanceSubfilter;
                        bool PassesAppearanceSubfilters(AppearanceSubfilter f)
                        {
                            if (f == 0) return true;
                            bool wantsPresets = (f & AppearanceSubfilter.Presets) != 0;
                            bool wantsCustom = (f & AppearanceSubfilter.Custom) != 0;
                            bool wantsMale = (f & AppearanceSubfilter.Male) != 0;
                            bool wantsFemale = (f & AppearanceSubfilter.Female) != 0;
                            bool wantsFuta = (f & AppearanceSubfilter.Futa) != 0;

                            // If both are selected, it's effectively no type restriction.
                            bool typeOk = true;
                            if (wantsPresets || wantsCustom)
                            {
                                if (wantsPresets && wantsCustom) typeOk = true;
                                else if (wantsPresets) typeOk = isPresetAppearance;
                                else if (wantsCustom) typeOk = isCustomAppearance;
                            }
                            if (!typeOk) return false;

                            bool wantsAnyGender = wantsMale || wantsFemale || wantsFuta;
                            if (wantsAnyGender)
                            {
                                bool genderOk = false;
                                if (wantsMale && g == AppearanceGender.Male) genderOk = true;
                                if (wantsFemale && g == AppearanceGender.Female) genderOk = true;
                                if (wantsFuta && g == AppearanceGender.Futa) genderOk = true;
                                if (!genderOk) return false;
                            }

                            return true;
                        }

                        // Facet counts: how many would be shown if the user toggled that flag now.
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Presets)) appearanceSubfilterFacetCountPresets++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Custom)) appearanceSubfilterFacetCountCustom++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Male)) appearanceSubfilterFacetCountMale++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Female)) appearanceSubfilterFacetCountFemale++;
                        if (PassesAppearanceSubfilters(cur ^ AppearanceSubfilter.Futa)) appearanceSubfilterFacetCountFuta++;

                        // Current counts: how many are shown under the current active subfilter set.
                        if (PassesAppearanceSubfilters(appearanceSubfilter))
                        {
                            appearanceSubfilterCurrentCountAll++;
                            if (g == AppearanceGender.Male) appearanceSubfilterCurrentCountMale++;
                            if (g == AppearanceGender.Female) appearanceSubfilterCurrentCountFemale++;
                            if (g == AppearanceGender.Futa) appearanceSubfilterCurrentCountFuta++;
                        }

                        // Apply active subfilters (if any) to tag counting.
                        if (appearanceSubfilter != 0)
                        {
                            if (!PassesAppearanceSubfilters(appearanceSubfilter)) continue;
                        }
                    }

                    // Appearance split-pane counts (All/Presets/Custom)
                    if (isAppearanceTitle)
                    {
                        if (string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase))
                        {
                            if (internalPath.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                            {
                                // Presets = appearance .vap inside .var packages
                                appearanceSourceCountPresets++;
                                appearanceSourceCountAll++;
                            }
                        }
                    }

                    if (hasAnyTagsToCount)
                    {
                        // 3. Count tags
                        // Tokenize path for single-word tags; singleWordTags uses OrdinalIgnoreCase so no lowering needed
                        string[] tokens = internalPath.Split(separators);

                        foundTags.Clear();

                        // Check tokens against single word tags
                        for (int k = 0; k < tokens.Length; k++)
                        {
                            if (singleWordTags.Contains(tokens[k]))
                            {
                                foundTags.Add(tokens[k].ToLowerInvariant());
                            }
                        }

                        // Check multi-word tags using case-insensitive IndexOf
                        for (int k = 0; k < multiWordTags.Count; k++)
                        {
                            if (internalPath.IndexOf(multiWordTags[k], StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                foundTags.Add(multiWordTags[k]);
                            }
                        }

                        // Check user-defined tags specifically for this entry
                        var uTags = TagsManager.Instance.GetTags(entry.Uid);
                        foreach (var ut in uTags)
                        {
                            if (tagsToCount.Contains(ut)) foundTags.Add(ut);
                        }

                        // Increment counts
                        foreach (var tag in foundTags)
                        {
                            int cur;
                            tagCounts.TryGetValue(tag, out cur);
                            tagCounts[tag] = cur + 1;
                        }
                    }
                }
            }
            }
            if (coVarFromSql)
                ApplyTagScanTotalsFromWorker(coTagScanTotals);

            // Count Clothing (local filesystem) entries for subfilter facet counts.
            // This is intentionally separate from the package loop above.
            if (isClothingTitle)
            {
                if (string.IsNullOrEmpty(currentCreator))
                {
                    List<string> pathsToSearch = new List<string>();
                    if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
                    else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

                    // Prefer SQLite-cached loose-file enumeration (same mechanism as RefreshFilesRoutine).
                    string sysCacheKey = null;
                    string sysCacheSig = null;
                    List<VpbLocalDatabase.SystemFileRow> sysCached = null;
                    bool sysCacheHit = false;
                    try
                    {
                        var sbKey = new StringBuilder(256);
                        sbKey.Append("tags:loose:clothing|");
                        sbKey.Append("ext=");
                        if (extensions != null && extensions.Length > 0)
                        {
                            var ex = new List<string>(extensions);
                            ex.Sort(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < ex.Count; i++)
                            {
                                if (i != 0) sbKey.Append(',');
                                sbKey.Append(ex[i] ?? "");
                            }
                        }
                        sbKey.Append("|paths=");
                        var p2 = new List<string>(pathsToSearch);
                        p2.Sort(StringComparer.OrdinalIgnoreCase);
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
                            try { if (Directory.Exists(p2[i])) t = Directory.GetLastWriteTimeUtc(p2[i]).ToBinary(); } catch { t = 0; }
                            if (i != 0) sbSig.Append('|');
                            sbSig.Append(t.ToString());
                        }
                        sysCacheSig = sbSig.ToString();

                        sysCached = new List<VpbLocalDatabase.SystemFileRow>();
                        sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCached);
                    }
                    catch { sysCacheHit = false; sysCached = null; }

                    for (int pi = 0; pi < pathsToSearch.Count; pi++)
                    {
                        string searchPath = pathsToSearch[pi];
                        if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                        for (int ei = 0; ei < extensions.Length; ei++)
                        {
                            string ext = extensions[ei];
                            if (string.IsNullOrEmpty(ext)) continue;

                            List<string> sysFileList = null;
                            if (sysCacheHit && sysCached != null && sysCached.Count > 0)
                            {
                                sysFileList = new List<string>();
                                for (int i = 0; i < sysCached.Count; i++)
                                {
                                    string p = sysCached[i].Path ?? "";
                                    if (p.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase))
                                        sysFileList.Add(p);
                                }
                            }
                            else
                            {
                                sysFileList = new List<string>();
                                try { FileManager.SafeGetFiles(searchPath, "*." + ext, sysFileList); }
                                catch { continue; }
                            }

                            for (int fi = 0; fi < sysFileList.Count; fi++)
                            {
                                if ((fi & 0x7F) == 0x7F)
                                {
                                    if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledClothFs))
                                        yield return null;
                                    if (cancelledClothFs) yield break;
                                }

                                string sysPath = sysFileList[fi] ?? "";
                                string norm = sysPath.Replace('\\', '/');
                                bool isPresetEntry = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);
                                bool isCustomPreset =
                                    (norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                     norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                     norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     norm.IndexOf("/Saves/", StringComparison.OrdinalIgnoreCase) >= 0);

                                ClothingLoadingUtils.ResourceKind ck = ClothingLoadingUtils.ResourceKind.Unknown;
                                ClothingLoadingUtils.ResourceGender cg = ClothingLoadingUtils.ResourceGender.Unknown;
                                ClothingLoadingUtils.ClassifyClothingHairPath(sysPath, out ck, out cg);
                                if (ck != ClothingLoadingUtils.ResourceKind.Clothing) continue;

                                bool isDecal = ClothingLoadingUtils.IsDecalLikePath(sysPath);

                                ClothingSubfilter cur = clothingSubfilter;
                                bool PassesClothingSubfilters(ClothingSubfilter f)
                                {
                                    if (f == 0) return true;

                                    bool wantsRealType = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                                    bool wantsDecalType = ((f & ClothingSubfilter.Decals) != 0);

                                    bool typeExplicit = ((f & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                                    if (typeExplicit)
                                    {
                                        bool okType = (!isDecal && (f & ClothingSubfilter.RealClothing) != 0) ||
                                                      (isDecal && (f & ClothingSubfilter.Decals) != 0);
                                        if (!okType) return false;
                                    }
                                    else
                                    {
                                        if (wantsRealType && isDecal && !wantsDecalType) return false;
                                    }

                                    bool wantsPresets = (f & ClothingSubfilter.Presets) != 0;
                                    bool wantsCustom = (f & ClothingSubfilter.Custom) != 0;
                                    if (wantsPresets || wantsCustom)
                                    {
                                        if (!isPresetEntry) return false;
                                        if (wantsPresets && !wantsCustom) { if (isCustomPreset) return false; }
                                        if (wantsCustom && !wantsPresets) { if (!isCustomPreset) return false; }
                                    }
                                    if ((f & ClothingSubfilter.Items) != 0) { if (isPresetEntry) return false; }
                                    if ((f & ClothingSubfilter.Male) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Male) return false; }
                                    if ((f & ClothingSubfilter.Female) != 0) { if (cg != ClothingLoadingUtils.ResourceGender.Female) return false; }

                                    return true;
                                }

                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.RealClothing)) clothingSubfilterFacetCountReal++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Presets)) clothingSubfilterFacetCountPresets++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Custom)) clothingSubfilterFacetCountCustom++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Items)) clothingSubfilterFacetCountItems++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Male)) clothingSubfilterFacetCountMale++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Female)) clothingSubfilterFacetCountFemale++;
                                if (PassesClothingSubfilters(cur ^ ClothingSubfilter.Decals)) clothingSubfilterFacetCountDecals++;

                                clothingSubfilterCountAll++;
                                if (isDecal)
                                {
                                    clothingSubfilterCountDecals++;
                                }
                                else
                                {
                                    clothingSubfilterCountReal++;
                                    if (isPresetEntry) clothingSubfilterCountPresets++;
                                    if (isCustomPreset) clothingSubfilterCountCustom++;
                                    if (!isPresetEntry) clothingSubfilterCountItems++;
                                    if (cg == ClothingLoadingUtils.ResourceGender.Male) clothingSubfilterCountMale++;
                                    else if (cg == ClothingLoadingUtils.ResourceGender.Female) clothingSubfilterCountFemale++;
                                }
                            }
                        }
                    }

                    if (!sysCacheHit && !string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                    {
                        try
                        {
                            var rows = new List<VpbLocalDatabase.SystemFileRow>(512);
                            for (int pi = 0; pi < pathsToSearch.Count; pi++)
                            {
                                string sp = pathsToSearch[pi];
                                if (string.IsNullOrEmpty(sp) || !Directory.Exists(sp)) continue;
                                for (int ei = 0; ei < extensions.Length; ei++)
                                {
                                    string ext = extensions[ei];
                                    if (string.IsNullOrEmpty(ext)) continue;
                                    var buf = new List<string>();
                                    try { FileManager.SafeGetFiles(sp, "*." + ext, buf); }
                                    catch { continue; }
                                    for (int i = 0; i < buf.Count; i++)
                                    {
                                        string p = buf[i] ?? "";
                                        if (p.Length == 0) continue;
                                        var r = new VpbLocalDatabase.SystemFileRow();
                                        r.Path = p;
                                        r.LastWriteBinaryOrInvalid = long.MinValue;
                                        r.SizeOrInvalid = long.MinValue;
                                        rows.Add(r);
                                    }
                                }
                            }
                            if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, rows);
                        }
                        catch { }
                    }
                }
            }

            // Count Custom (local filesystem) appearances for split-pane counts.
            // This is intentionally separate from the package loop above.
            if (isAppearanceTitle)
            {
                List<string> pathsToSearch = new List<string>();
                if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
                else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

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
                        try { if (Directory.Exists(p2[i])) t = Directory.GetLastWriteTimeUtc(p2[i]).ToBinary(); } catch { t = 0; }
                        if (i != 0) sbSig.Append('|');
                        sbSig.Append(t.ToString());
                    }
                    sysCacheSig = sbSig.ToString();

                    sysCached = new List<VpbLocalDatabase.SystemFileRow>();
                    sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCached);
                }
                catch { sysCacheHit = false; sysCached = null; }

                for (int pi = 0; pi < pathsToSearch.Count; pi++)
                {
                    string searchPath = pathsToSearch[pi];
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath)) continue;

                    List<string> sysFileList = null;
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
                        if ((fi & 0x7F) == 0x7F)
                        {
                            if (TagCountScanShouldYieldFrame(maxMsPerSlice, sliceWatch, deferredSessionId, _deferredSubPaneSessionId, out bool cancelledAppFs))
                                yield return null;
                            if (cancelledAppFs) yield break;
                        }

                        string sysPath = sysFileList[fi] ?? "";
                        string norm = sysPath.Replace('\\', '/');
                        if (!norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) &&
                            !norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        appearanceSourceCountCustom++;
                        appearanceSourceCountAll++;
                    }
                }

                if (!sysCacheHit && !string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                {
                    try
                    {
                        var rows = new List<VpbLocalDatabase.SystemFileRow>(512);
                        for (int pi = 0; pi < pathsToSearch.Count; pi++)
                        {
                            string sp = pathsToSearch[pi];
                            if (string.IsNullOrEmpty(sp) || !Directory.Exists(sp)) continue;
                            var buf = new List<string>();
                            try { FileManager.SafeGetFiles(sp, "*.vap", buf); }
                            catch { continue; }
                            for (int i = 0; i < buf.Count; i++)
                            {
                                string p = buf[i] ?? "";
                                if (p.Length == 0) continue;
                                var r = new VpbLocalDatabase.SystemFileRow();
                                r.Path = p;
                                r.LastWriteBinaryOrInvalid = long.MinValue;
                                r.SizeOrInvalid = long.MinValue;
                                rows.Add(r);
                            }
                        }
                        if (rows.Count > 0) VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, rows);
                    }
                    catch { }
                }
            }

            tagsCached = true;
            if (TryBuildTagCountCacheKey(out tagCountCacheKey))
            {
                try { GalleryTagCountSnapshotCache.Put(tagCountCacheKey, CaptureTagCountSnapshot()); } catch { }
            }
        }

        /// <summary>Stable key for <see cref="GalleryTagCountSnapshotCache"/> when tag/facet counts depend only on category + filters + package scan.</summary>
        private bool TryBuildTagCountCacheKey(out string key)
        {
            key = null;
            try
            {
                var sb = new StringBuilder(384);
                sb.Append(titleText != null ? titleText.text : "").Append('\u001E');
                sb.Append(currentPath ?? "").Append('\u001E');
                if (currentPaths != null)
                {
                    for (int i = 0; i < currentPaths.Count; i++)
                    {
                        sb.Append(currentPaths[i] ?? "");
                        sb.Append('\u001F');
                    }
                }
                sb.Append('\u001E');
                sb.Append(currentExtension ?? "").Append('\u001E');
                sb.Append(currentCreator ?? "").Append('\u001E');
                sb.Append((int)clothingSubfilter).Append('\u001E');
                sb.Append((int)appearanceSubfilter).Append('\u001E');
                sb.Append(currentAppearanceSourceFilter ?? "").Append('\u001E');
                long pr = 0;
                try { pr = FileManager.lastPackageRefreshTime.ToBinary(); } catch { pr = 0; }
                sb.Append(pr).Append('\u001E');
                int utc = 0;
                try { utc = TagsManager.Instance.GetAllUserTags().Count; } catch { utc = 0; }
                sb.Append(utc);
                key = sb.ToString();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Builds immutable inputs for <see cref="GalleryTagCountBackgroundScan"/> (main thread only).</summary>
        internal bool TryBuildTagCountParallelInputs(out TagCountParallelInputs inputs)
        {
            inputs = null;
            if (FileManager.PackagesByUid == null) return false;

            inputs = new TagCountParallelInputs();
            inputs.Title = titleText != null ? titleText.text : "";
            inputs.CurrentPath = currentPath ?? "";
            inputs.CurrentPathsCopy = currentPaths != null ? new List<string>(currentPaths) : null;
            inputs.CurrentCreator = currentCreator ?? "";
            inputs.ClothingSubfilterVal = clothingSubfilter;
            inputs.AppearanceSubfilterVal = appearanceSubfilter;
            inputs.ExtensionsSplit = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');

            inputs.IsClothingTitle = (inputs.Title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0);
            inputs.IsAppearanceTitle = (inputs.Title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0);

            HashSet<string> tagsToCount = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (inputs.IsClothingTitle)
            {
                tagsToCount.UnionWith(TagFilter.AllClothingTags);
                tagsToCount.UnionWith(TagFilter.ClothingUnknownTags);
            }
            else if (inputs.Title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                tagsToCount.UnionWith(TagFilter.AllHairTags);
                tagsToCount.UnionWith(TagFilter.HairUnknownTags);
            }
            tagsToCount.UnionWith(TagsManager.Instance.GetAllUserTags());
            inputs.TagsToCount = tagsToCount;
            inputs.HasAnyTagsToCount = (tagsToCount.Count > 0);

            inputs.SingleWordTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            inputs.MultiWordTags = new List<string>();
            if (inputs.HasAnyTagsToCount)
            {
                char[] multiWordSeparators = new char[] { ' ', '_', '-' };
                foreach (string t in tagsToCount)
                {
                    if (t.IndexOfAny(multiWordSeparators) >= 0) inputs.MultiWordTags.Add(t);
                    else inputs.SingleWordTags.Add(t);
                }
            }

            return true;
        }

        private TagCountSnapshot CaptureTagCountSnapshot()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tagCounts)
                d[kv.Key] = kv.Value;
            return new TagCountSnapshot
            {
                TagCounts = d,
                AppearanceSourceCountAll = appearanceSourceCountAll,
                AppearanceSourceCountPresets = appearanceSourceCountPresets,
                AppearanceSourceCountCustom = appearanceSourceCountCustom,
                ClothingSubfilterCountAll = clothingSubfilterCountAll,
                ClothingSubfilterCountReal = clothingSubfilterCountReal,
                ClothingSubfilterCountPresets = clothingSubfilterCountPresets,
                ClothingSubfilterCountCustom = clothingSubfilterCountCustom,
                ClothingSubfilterCountItems = clothingSubfilterCountItems,
                ClothingSubfilterCountMale = clothingSubfilterCountMale,
                ClothingSubfilterCountFemale = clothingSubfilterCountFemale,
                ClothingSubfilterCountDecals = clothingSubfilterCountDecals,
                AppearanceSubfilterCountAll = appearanceSubfilterCountAll,
                AppearanceSubfilterCountPresets = appearanceSubfilterCountPresets,
                AppearanceSubfilterCountCustom = appearanceSubfilterCountCustom,
                AppearanceSubfilterCountMale = appearanceSubfilterCountMale,
                AppearanceSubfilterCountFemale = appearanceSubfilterCountFemale,
                AppearanceSubfilterCountFuta = appearanceSubfilterCountFuta,
                ClothingSubfilterFacetCountReal = clothingSubfilterFacetCountReal,
                ClothingSubfilterFacetCountPresets = clothingSubfilterFacetCountPresets,
                ClothingSubfilterFacetCountCustom = clothingSubfilterFacetCountCustom,
                ClothingSubfilterFacetCountItems = clothingSubfilterFacetCountItems,
                ClothingSubfilterFacetCountMale = clothingSubfilterFacetCountMale,
                ClothingSubfilterFacetCountFemale = clothingSubfilterFacetCountFemale,
                ClothingSubfilterFacetCountDecals = clothingSubfilterFacetCountDecals,
                AppearanceSubfilterFacetCountPresets = appearanceSubfilterFacetCountPresets,
                AppearanceSubfilterFacetCountCustom = appearanceSubfilterFacetCountCustom,
                AppearanceSubfilterFacetCountMale = appearanceSubfilterFacetCountMale,
                AppearanceSubfilterFacetCountFemale = appearanceSubfilterFacetCountFemale,
                AppearanceSubfilterFacetCountFuta = appearanceSubfilterFacetCountFuta,
                AppearanceSubfilterCurrentCountAll = appearanceSubfilterCurrentCountAll,
                AppearanceSubfilterCurrentCountMale = appearanceSubfilterCurrentCountMale,
                AppearanceSubfilterCurrentCountFemale = appearanceSubfilterCurrentCountFemale,
                AppearanceSubfilterCurrentCountFuta = appearanceSubfilterCurrentCountFuta,
            };
        }

        private void RestoreTagCountSnapshot(TagCountSnapshot s)
        {
            if (s == null) return;
            tagCounts.Clear();
            if (s.TagCounts != null)
            {
                foreach (var kv in s.TagCounts)
                    tagCounts[kv.Key] = kv.Value;
            }
            appearanceSourceCountAll = s.AppearanceSourceCountAll;
            appearanceSourceCountPresets = s.AppearanceSourceCountPresets;
            appearanceSourceCountCustom = s.AppearanceSourceCountCustom;
            clothingSubfilterCountAll = s.ClothingSubfilterCountAll;
            clothingSubfilterCountReal = s.ClothingSubfilterCountReal;
            clothingSubfilterCountPresets = s.ClothingSubfilterCountPresets;
            clothingSubfilterCountCustom = s.ClothingSubfilterCountCustom;
            clothingSubfilterCountItems = s.ClothingSubfilterCountItems;
            clothingSubfilterCountMale = s.ClothingSubfilterCountMale;
            clothingSubfilterCountFemale = s.ClothingSubfilterCountFemale;
            clothingSubfilterCountDecals = s.ClothingSubfilterCountDecals;
            appearanceSubfilterCountAll = s.AppearanceSubfilterCountAll;
            appearanceSubfilterCountPresets = s.AppearanceSubfilterCountPresets;
            appearanceSubfilterCountCustom = s.AppearanceSubfilterCountCustom;
            appearanceSubfilterCountMale = s.AppearanceSubfilterCountMale;
            appearanceSubfilterCountFemale = s.AppearanceSubfilterCountFemale;
            appearanceSubfilterCountFuta = s.AppearanceSubfilterCountFuta;
            clothingSubfilterFacetCountReal = s.ClothingSubfilterFacetCountReal;
            clothingSubfilterFacetCountPresets = s.ClothingSubfilterFacetCountPresets;
            clothingSubfilterFacetCountCustom = s.ClothingSubfilterFacetCountCustom;
            clothingSubfilterFacetCountItems = s.ClothingSubfilterFacetCountItems;
            clothingSubfilterFacetCountMale = s.ClothingSubfilterFacetCountMale;
            clothingSubfilterFacetCountFemale = s.ClothingSubfilterFacetCountFemale;
            clothingSubfilterFacetCountDecals = s.ClothingSubfilterFacetCountDecals;
            appearanceSubfilterFacetCountPresets = s.AppearanceSubfilterFacetCountPresets;
            appearanceSubfilterFacetCountCustom = s.AppearanceSubfilterFacetCountCustom;
            appearanceSubfilterFacetCountMale = s.AppearanceSubfilterFacetCountMale;
            appearanceSubfilterFacetCountFemale = s.AppearanceSubfilterFacetCountFemale;
            appearanceSubfilterFacetCountFuta = s.AppearanceSubfilterFacetCountFuta;
            appearanceSubfilterCurrentCountAll = s.AppearanceSubfilterCurrentCountAll;
            appearanceSubfilterCurrentCountMale = s.AppearanceSubfilterCurrentCountMale;
            appearanceSubfilterCurrentCountFemale = s.AppearanceSubfilterCurrentCountFemale;
            appearanceSubfilterCurrentCountFuta = s.AppearanceSubfilterCurrentCountFuta;
        }

        public void SetCategories(List<Gallery.Category> cats)
        {
            categories = cats;
            categoriesCached = false;

            // Try to restore last tab if currentPath is not yet set (e.g. freshly created panels
            // that were not yet shown via Show()). Panels that already have a category displayed
            // are left unchanged.
            string lastPageName = null;
            if (VPBConfig.Instance != null && !string.IsNullOrEmpty(VPBConfig.Instance.LastGalleryCategory))
                lastPageName = VPBConfig.Instance.LastGalleryCategory;
            else if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                lastPageName = Settings.Instance.LastGalleryPage.Value;
            LogUtil.Log("[Gallery] SetCategories: currentPath='" + currentPath + "' memoryLastCat='" + (VPBConfig.Instance != null ? VPBConfig.Instance.LastGalleryCategory : "null") + "' resolvedLastPage='" + (lastPageName ?? "null") + "'");

            if (string.IsNullOrEmpty(currentPath) && !string.IsNullOrEmpty(lastPageName))
            {
                // Normalize legacy enum-style names ("CategoryHair" -> "Hair", "PresetHair" -> "Hair")
                lastPageName = lastPageName.Trim();
                if (lastPageName.StartsWith("Category ", StringComparison.OrdinalIgnoreCase))
                    lastPageName = lastPageName.Substring("Category ".Length);
                else if (lastPageName.StartsWith("Category", StringComparison.OrdinalIgnoreCase) && lastPageName.Length > "Category".Length)
                    lastPageName = lastPageName.Substring("Category".Length);

                if (lastPageName.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                    lastPageName = lastPageName.Substring("Preset ".Length);
                else if (lastPageName.StartsWith("Preset", StringComparison.OrdinalIgnoreCase) && lastPageName.Length > "Preset".Length)
                    lastPageName = lastPageName.Substring("Preset".Length);

                lastPageName = lastPageName.Trim();
                if (string.Equals(lastPageName, "Scene", StringComparison.OrdinalIgnoreCase))
                    lastPageName = "Scenes";

                var cat = categories.FirstOrDefault(c => string.Equals(c.name, lastPageName, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(cat.name))
                {
                    currentPath = cat.path;
                    currentPaths = cat.paths;
                    currentExtension = cat.extension;
                    currentCategoryTitle = cat.name;
                    titleText.text = cat.name;
                    activeTags.Clear();
                }
            }

            if (string.IsNullOrEmpty(currentPath) && categories.Count > 0)
            {
                // Fallback to first category
                currentPath = categories[0].path;
                currentPaths = categories[0].paths;
                currentExtension = categories[0].extension;
                currentCategoryTitle = categories[0].name;
                titleText.text = categories[0].name;
                activeTags.Clear();
            }

            LogUtil.Log("[Gallery] SetCategories resolved: currentPath='" + currentPath + "' currentCategoryTitle='" + currentCategoryTitle + "'");
            // Full UpdateTabs() runs synchronous CacheCategoryCounts/CacheCreators and can take many seconds on large libraries.
            // New panes defer that work to RefreshFilesRoutine (background cache + one UpdateTabs at the end).
            if (hasLoadedContent)
                UpdateTabs();
            else
            {
                _sideTabsNeedFullRebuildAfterFirstRefresh = true;
                UpdateTabsImpl(rebuildSideTabLists: false);
            }
            // If we have categories but no path, set title to first category
            if (categories.Count > 0 && string.IsNullOrEmpty(currentPath))
            {
                titleText.text = categories[0].name;
            }
        }

        public void PushUndo(Action action)
        {
            if (action == null) return;
            undoStack.Push(action);
            if (!isApplyingUndoRedo)
            {
                try { redoStack.Clear(); } catch { }
            }
            UpdateUndoRedoButtonLabels();
            if (undoStack.Count > 20) // Limit stack size
            {
                // Stack doesn't have RemoveFromBottom, but 20 is small enough.
                // Or we can just let it grow a bit. 20 is safe.
            }
        }

        private void UpdateUndoRedoButtonLabels()
        {
            try
            {
                string undoText = VPBTranslation.T("gallery.footer.undo_abbrev", "U") + " (" + (undoStack != null ? undoStack.Count : 0) + ")";
                string redoText = VPBTranslation.T("gallery.footer.redo_abbrev", "R") + " (" + (redoStack != null ? redoStack.Count : 0) + ")";

                if (footerUndoBtnGO != null)
                {
                    Text t = null;
                    try { t = footerUndoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = undoText;
                }

                if (footerRedoBtnGO != null)
                {
                    Text t = null;
                    try { t = footerRedoBtnGO.GetComponentInChildren<Text>(true); } catch { }
                    if (t != null) t.text = redoText;
                }
            }
            catch { }
        }

        private Atom GetBestUndoRedoTargetAtom()
        {
            Atom a = null;
            try { a = GetBestTargetAtom(); } catch { a = null; }
            if (a == null)
            {
                try { a = SelectedTargetAtom; } catch { a = null; }
            }
            if (a == null)
            {
                try
                {
                    if (SuperController.singleton != null)
                    {
                        var atoms = SuperController.singleton.GetAtoms();
                        if (atoms != null) a = atoms.FirstOrDefault(x => x != null && x.type == "Person");
                    }
                }
                catch { a = null; }
            }
            return a;
        }

        private Action CaptureAtomSnapshotAction(Atom atom)
        {
            if (atom == null) return null;
            string atomUid = null;
            try { atomUid = atom.uid; } catch { atomUid = null; }
            if (string.IsNullOrEmpty(atomUid)) return null;

            Dictionary<string, bool> geometryToggleSnapshot = null;
            List<JSONClass> storableSnapshots = new List<JSONClass>();

            bool ShouldSnapshotStorableId(string sid)
            {
                if (string.IsNullOrEmpty(sid)) return false;
                if (string.Equals(sid, "geometry", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(sid, "Skin", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.EndsWith("Presets", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.EndsWith("Preset", StringComparison.OrdinalIgnoreCase)) return true;
                if (sid.IndexOf("clothing", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (sid.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (sid.IndexOf("appearance", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }

            try
            {
                JSONStorable geometry = null;
                try { geometry = atom.GetStorableByID("geometry"); } catch { geometry = null; }
                if (geometry != null)
                {
                    geometryToggleSnapshot = new Dictionary<string, bool>();
                    List<string> names = null;
                    try { names = geometry.GetBoolParamNames(); } catch { names = null; }
                    if (names != null)
                    {
                        foreach (string key in names)
                        {
                            if (key == null) continue;
                            if (!(key.StartsWith("clothing:") || key.StartsWith("hair:"))) continue;
                            JSONStorableBool b = null;
                            try { b = geometry.GetBoolJSONParam(key); } catch { b = null; }
                            if (b != null) geometryToggleSnapshot[key] = b.val;
                        }
                    }
                }
            }
            catch { geometryToggleSnapshot = null; }

            try
            {
                List<string> ids = null;
                try { ids = atom.GetStorableIDs(); } catch { ids = null; }
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        string sid = ids[i];
                        if (string.IsNullOrEmpty(sid)) continue;
                        if (!ShouldSnapshotStorableId(sid)) continue;
                        JSONStorable s = null;
                        try { s = atom.GetStorableByID(sid); } catch { s = null; }
                        if (s == null) continue;
                        JSONClass snap = null;
                        try { snap = s.GetJSON(); } catch { snap = null; }
                        if (snap != null) storableSnapshots.Add(snap);
                    }
                }
            }
            catch { }

            return () =>
            {
                Atom targetAtom = null;
                try { targetAtom = SuperController.singleton != null ? SuperController.singleton.GetAtomByUid(atomUid) : null; } catch { targetAtom = null; }
                if (targetAtom == null) return;

                try
                {
                    if (geometryToggleSnapshot != null)
                    {
                        JSONStorable geo = null;
                        try { geo = targetAtom.GetStorableByID("geometry"); } catch { geo = null; }
                        if (geo != null)
                        {
                            foreach (var kvp in geometryToggleSnapshot)
                            {
                                JSONStorableBool b = null;
                                try { b = geo.GetBoolJSONParam(kvp.Key); } catch { b = null; }
                                if (b != null) b.val = kvp.Value;
                            }

                            List<string> currentNames = null;
                            try { currentNames = geo.GetBoolParamNames(); } catch { currentNames = null; }
                            if (currentNames != null)
                            {
                                foreach (string key2 in currentNames)
                                {
                                    if (string.IsNullOrEmpty(key2)) continue;
                                    if ((key2.StartsWith("clothing:") || key2.StartsWith("hair:")) && !geometryToggleSnapshot.ContainsKey(key2))
                                    {
                                        JSONStorableBool b2 = null;
                                        try { b2 = geo.GetBoolJSONParam(key2); } catch { b2 = null; }
                                        if (b2 != null) b2.val = false;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    for (int i = 0; i < storableSnapshots.Count; i++)
                    {
                        JSONClass snap = storableSnapshots[i];
                        if (snap == null) continue;
                        string sid = null;
                        try { sid = snap["id"].Value; } catch { sid = null; }
                        if (string.IsNullOrEmpty(sid)) continue;
                        if (!ShouldSnapshotStorableId(sid)) continue;
                        JSONStorable s = null;
                        try { s = targetAtom.GetStorableByID(sid); } catch { s = null; }
                        if (s == null) continue;
                        try { s.RestoreFromJSON(snap); } catch { }
                    }
                }
                catch { }
            };
        }

        private Action CaptureUndoRedoSnapshotAction()
        {
            Atom a = GetBestUndoRedoTargetAtom();
            if (a != null && string.Equals(a.type, "Person", StringComparison.OrdinalIgnoreCase))
            {
                Action atomSnap = CaptureAtomSnapshotAction(a);
                if (atomSnap != null) return atomSnap;
            }
            return CaptureSceneSnapshotAction();
        }

        private Action CaptureSceneSnapshotAction()
        {
            try
            {
                if (SuperController.singleton == null) return null;
                string tempPath = Path.Combine(SuperController.singleton.savesDir, "vpb_temp_undo_redo_scene_" + Guid.NewGuid().ToString() + ".json");

                JSONNode sceneRoot = null;
                try
                {
                    SuperController sc = SuperController.singleton;
                    if (sc == null) return null;

                    string[] candidates = new[]
                    {
                        "GetSaveJSON",
                        "GetSaveSceneJSON",
                        "GetSceneJSON",
                        "GetJSON",
                        "GetSaveJson",
                        "GetSceneJson",
                    };

                    object TryInvoke(MethodInfo mi)
                    {
                        if (mi == null) return null;
                        ParameterInfo[] ps = null;
                        try { ps = mi.GetParameters(); }
                        catch { ps = null; }

                        Atom bestAtom = null;
                        try { bestAtom = GetBestTargetAtom(); } catch { }
                        if (bestAtom == null)
                        {
                            try { bestAtom = SelectedTargetAtom; } catch { bestAtom = null; }
                        }
                        if (bestAtom == null)
                        {
                            try
                            {
                                if (SuperController.singleton != null)
                                {
                                    var atoms = SuperController.singleton.GetAtoms();
                                    if (atoms != null) bestAtom = atoms.FirstOrDefault(a => a != null && a.type == "Person");
                                }
                            }
                            catch { bestAtom = null; }
                        }

                        object[] args = null;
                        if (ps != null && ps.Length > 0)
                        {
                            args = new object[ps.Length];
                            for (int pi = 0; pi < ps.Length; pi++)
                            {
                                Type t = ps[pi].ParameterType;
                                bool isByRef = false;
                                try { isByRef = t != null && t.IsByRef; } catch { isByRef = false; }
                                if (isByRef)
                                {
                                    try { t = t.GetElementType(); }
                                    catch { t = ps[pi].ParameterType; }
                                }

                                if (t == typeof(bool)) args[pi] = false;
                                else if (t == typeof(int)) args[pi] = 0;
                                else if (t == typeof(float)) args[pi] = 0f;
                                else if (t == typeof(string)) args[pi] = "";
                                else if (t == typeof(JSONNode) || t == typeof(JSONClass)) args[pi] = new JSONClass();
                                else if (t == typeof(Atom)) args[pi] = bestAtom;
                                else
                                {
                                    return null;
                                }
                            }
                        }

                        try { return mi.Invoke(sc, args); }
                        catch { return null; }
                    }

                    bool TrySetSceneRootFromResult(object result)
                    {
                        if (result == null) return false;
                        try
                        {
                            if (result is JSONNode node)
                            {
                                sceneRoot = node;
                                return true;
                            }

                            string s = null;
                            try { s = result.ToString(); }
                            catch { s = null; }
                            if (string.IsNullOrEmpty(s)) return false;

                            try
                            {
                                JSONNode parsed = JSON.Parse(s);
                                if (parsed != null)
                                {
                                    sceneRoot = parsed;
                                    return true;
                                }
                            }
                            catch { }
                        }
                        catch { }
                        return false;
                    }

                    for (int i = 0; i < candidates.Length && sceneRoot == null; i++)
                    {
                        MethodInfo[] methods = null;
                        try { methods = sc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => string.Equals(m.Name, candidates[i], StringComparison.Ordinal)).ToArray(); }
                        catch { methods = null; }
                        if (methods == null || methods.Length == 0) continue;

                        for (int m = 0; m < methods.Length && sceneRoot == null; m++)
                        {
                            object result = TryInvoke(methods[m]);
                            if (TrySetSceneRootFromResult(result)) break;
                        }
                    }
                }
                catch { sceneRoot = null; }

                if (sceneRoot == null) return null;

                try
                {
                    File.WriteAllText(tempPath, sceneRoot.ToString());
                }
                catch
                {
                    return null;
                }

                string loadPath = null;
                try { loadPath = UI.NormalizePath(tempPath); }
                catch { loadPath = tempPath; }

                return () =>
                {
                    try
                    {
                        if (SuperController.singleton == null) return;
                        if (!File.Exists(tempPath)) return;
                        SceneLoadingUtils.LoadScene(loadPath, true);
                    }
                    catch { }
                    finally
                    {
                        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        public void RefreshTargetDropdown()
        {
            string currentSelectionUid = null;
            if (targetDropdownValue >= 0 && targetDropdownValue < personAtoms.Count)
            {
                Atom cur = personAtoms[targetDropdownValue];
                if (cur != null) try { currentSelectionUid = cur.uid; } catch { }
            }

            personAtoms.Clear();
            targetDropdownOptions.Clear();

            if (SuperController.singleton != null)
            {
                List<Atom> allAtoms = null;
                try { allAtoms = SuperController.singleton.GetAtoms(); } catch { }
                if (allAtoms != null)
                {
                    foreach (Atom a in allAtoms)
                    {
                        if (a == null) continue;
                        try
                        {
                            if (a.type == "Person")
                            {
                                string uid = a.uid;
                                if (uid != null)
                                {
                                    personAtoms.Add(a);
                                    targetDropdownOptions.Add(uid);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            if (targetDropdownOptions.Count == 0)
            {
                targetDropdownOptions.Add("None");
                personAtoms.Add(null);
            }

            // Restore previous selection by UID, or default to first
            if (currentSelectionUid != null)
            {
                int idx = -1;
                for (int i = 0; i < personAtoms.Count; i++)
                {
                    Atom a = personAtoms[i];
                    if (a == null) continue;
                    try { if (a.uid == currentSelectionUid) { idx = i; break; } } catch { }
                }
                targetDropdownValue = idx >= 0 ? idx : 0;
            }
            else
            {
                targetDropdownValue = 0;
            }

            UpdateTargetDropdownUI();
        }

        public void CycleTarget(bool forward)
        {
            bool wasShowingNone = personAtoms.Count == 1 && personAtoms[0] == null;
            RefreshTargetDropdown();

            bool hasRealPersons = personAtoms.Count > 0 && personAtoms[0] != null;

            // First click when stale "None" was displayed: just reveal the first person, don't cycle past it
            if (wasShowingNone && hasRealPersons)
                return;

            // Nothing to cycle if still None-only
            if (!hasRealPersons)
                return;

            if (forward)
                targetDropdownValue = (targetDropdownValue + 1) % targetDropdownOptions.Count;
            else
                targetDropdownValue = (targetDropdownValue - 1 + targetDropdownOptions.Count) % targetDropdownOptions.Count;
            UpdateTargetDropdownUI();
        }

        private void UpdateTargetDropdownUI()
        {
            string raw = (targetDropdownValue >= 0 && targetDropdownValue < targetDropdownOptions.Count)
                ? targetDropdownOptions[targetDropdownValue]
                : "None";
            string valText = (raw == "None") ? VPBTranslation.T("gallery.side.target_val_none", "None") : raw;
            string fullText = VPBTranslation.T("gallery.side.target_prefix", "Target: ") + valText;

            sideTargetTooltipLive = fullText;

            bool iconMode = galleryTargetSprite != null
                && leftTargetBtnIconImage != null
                && rightTargetBtnIconImage != null;
            if (iconMode)
            {
                if (leftTargetBtnText != null) leftTargetBtnText.gameObject.SetActive(false);
                if (rightTargetBtnText != null) rightTargetBtnText.gameObject.SetActive(false);
                if (leftTargetBtnIconImage != null) leftTargetBtnIconImage.enabled = true;
                if (rightTargetBtnIconImage != null) rightTargetBtnIconImage.enabled = true;
            }
            else
            {
                if (leftTargetBtnText != null)
                {
                    leftTargetBtnText.gameObject.SetActive(true);
                    leftTargetBtnText.text = fullText;
                }
                if (rightTargetBtnText != null)
                {
                    rightTargetBtnText.gameObject.SetActive(true);
                    rightTargetBtnText.text = fullText;
                }
            }
        }

        private void Undo()
        {
            if (undoStack.Count > 0)
            {
                Action action = undoStack.Pop();
                try
                {
                    Action redoAction = CaptureUndoRedoSnapshotAction();
                    if (redoAction != null) redoStack.Push(redoAction);
                    isApplyingUndoRedo = true;
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Undo: " + ex.Message);
                }
                finally
                {
                    isApplyingUndoRedo = false;
                }

                UpdateUndoRedoButtonLabels();
                try
                {
                    // Ensure context submenus refresh immediately after Undo restores items.
                    Atom tgt = null;
                    try { tgt = GetBestTargetAtom(); } catch { }
                    if (clothingSubmenuOpen) SyncClothingSubmenu(tgt, true);
                    if (hairSubmenuOpen) SyncHairSubmenu(tgt, true);
                    UpdateSideContextActions();
                }
                catch { }
            }
            else
            {
                LogUtil.Log("[VPB] Undo: stack empty");
                UpdateUndoRedoButtonLabels();
            }
        }

        private void Redo()
        {
            if (redoStack.Count > 0)
            {
                Action action = redoStack.Pop();
                try
                {
                    Action undoAction = CaptureUndoRedoSnapshotAction();
                    if (undoAction != null) undoStack.Push(undoAction);
                    isApplyingUndoRedo = true;
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError("Error during Redo: " + ex.Message);
                }
                finally
                {
                    isApplyingUndoRedo = false;
                }

                UpdateUndoRedoButtonLabels();
                try
                {
                    Atom tgt = null;
                    try { tgt = GetBestTargetAtom(); } catch { }
                    if (clothingSubmenuOpen) SyncClothingSubmenu(tgt, true);
                    if (hairSubmenuOpen) SyncHairSubmenu(tgt, true);
                    UpdateSideContextActions();
                }
                catch { }
            }
            else
            {
                LogUtil.Log("[VPB] Redo: stack empty");
                UpdateUndoRedoButtonLabels();
            }
        }

        private bool IsMatch(FileEntry entry, List<string> paths, string singlePath, string[] extensions)
        {
            if (entry == null) return false;

            string checkPath = entry.Path;
            if (entry is VarFileEntry vfe)
            {
                checkPath = vfe.InternalPath;
            }
            
            // Extension Filter
            bool extMatch = false;
            if (extensions == null || extensions.Length == 0 || (extensions.Length == 1 && string.IsNullOrEmpty(extensions[0])))
            {
                extMatch = true;
            }
            else
            {
                string entryExt = Path.GetExtension(checkPath);
                if (!string.IsNullOrEmpty(entryExt))
                {
                    entryExt = entryExt.Substring(1); // remove dot
                    foreach (var ext in extensions)
                    {
                        if (string.Equals(entryExt, ext, StringComparison.OrdinalIgnoreCase))
                        {
                            extMatch = true;
                            break;
                        }
                    }
                }
            }
            if (!extMatch) return false;

            // Path Filter
            if (paths != null && paths.Count > 0)
            {
                foreach (var p in paths)
                {
                    if (checkPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)) 
                    {
                        // Special Case: "Saves/Person" is often used for Poses, but "Saves/Person/appearance" are Appearances.
                        // If we are looking for Poses (Saves/Person) and found an appearance, skip it unless specifically requested.
                        if (string.Equals(p, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                        {
                            if (checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                continue;
                        }
                        return true;
                    }
                }
                return false;
            }
            
            if (!string.IsNullOrEmpty(singlePath))
            {
                if (checkPath.StartsWith(singlePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(singlePath, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(singlePath, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                    {
                        if (checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    return true;
                }
                return false;
            }

            return true;
        }

        private void ClearCurrentFilter(bool isRight)
        {
            ContentType? type = isRight ? rightActiveContent : leftActiveContent;
            
            if (!type.HasValue) return;

            // Simply close the panel (toggle off)
            if (isRight) ToggleRight(type.Value);
            else ToggleLeft(type.Value);
            
            // Optionally clear filters if desired, but "X" on a side tab usually implies "Close this tab"
            // If the user meant "Clear Filter" specifically for search text, that's inside the panel.
            // "the X button should be on the outside of the side buttons... side buttons that are being hidden"
            // This strongly suggests a close button for the side panel overlay.
            
            UpdateTabs();
        }
    }
}
