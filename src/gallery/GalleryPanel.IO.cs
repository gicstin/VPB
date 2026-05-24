using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        internal void BeginPaneLoadTiming(System.Diagnostics.Stopwatch startedStopwatch, string kind)
        {
            if (startedStopwatch == null) return;
            _paneLoadTimingStopwatch = startedStopwatch;
            _paneLoadTimingKind = kind ?? "";
        }

        private void CompletePaneLoadTimingIfPending(string suffix = null)
        {
            if (_paneLoadTimingStopwatch == null) return;
            _paneLoadTimingStopwatch.Stop();
            long ms = _paneLoadTimingStopwatch.ElapsedMilliseconds;
            string k = string.IsNullOrEmpty(_paneLoadTimingKind) ? "?" : _paneLoadTimingKind;
            string extra = string.IsNullOrEmpty(suffix) ? "" : " " + suffix;
            LogUtil.Log("[Gallery] Pane load timing (" + k + "): " + ms + " ms until grid ready" + extra + ".");
            _paneLoadTimingStopwatch = null;
            _paneLoadTimingKind = null;
        }

        // Shared creator/category side-tab metadata: identical for any panel with the same filters + category list while package scan is unchanged.
        private static readonly object s_SharedSideMetaLock = new object();
        private static DateTime s_SharedSideMetaPackageStamp = DateTime.MinValue;
        private static readonly Dictionary<string, SharedSideMetaSnapshot> s_SharedSideMetaByKey =
            new Dictionary<string, SharedSideMetaSnapshot>(StringComparer.Ordinal);

        private sealed class SharedSideMetaSnapshot
        {
            public List<CreatorCacheEntry> Creators;
            public Dictionary<string, int> CategoryCounts;
        }

        private const int SharedSideMetaMaxEntries = 24;

        private static void InvalidateSharedSideMetaIfPackageScanAdvanced()
        {
            DateTime t = DateTime.MinValue;
            try { t = FileManager.lastPackageRefreshTime; } catch { }
            if (t != s_SharedSideMetaPackageStamp)
            {
                s_SharedSideMetaPackageStamp = t;
                lock (s_SharedSideMetaLock) { s_SharedSideMetaByKey.Clear(); }
            }
        }

        private static string BuildSharedSideMetaCacheKey(string creator, string ext, string path, List<string> paths, List<Gallery.Category> cats, string categoryTitle = null)
        {
            var sb = new StringBuilder(512);
            sb.Append(creator ?? ""); sb.Append('\u001E');
            sb.Append(categoryTitle ?? ""); sb.Append('\u001E');
            sb.Append(ext ?? ""); sb.Append('\u001E');
            sb.Append(path ?? ""); sb.Append('\u001E');
            if (paths != null)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    sb.Append(paths[i] ?? "");
                    sb.Append('\u001F');
                }
            }
            sb.Append('\u001E');
            if (cats != null)
            {
                for (int i = 0; i < cats.Count; i++)
                {
                    var c = cats[i];
                    sb.Append(c.name ?? ""); sb.Append('\u0001'); sb.Append(c.extension ?? ""); sb.Append('\u0001'); sb.Append(c.path ?? ""); sb.Append('\u001F');
                    if (c.paths != null)
                    {
                        for (int j = 0; j < c.paths.Count; j++)
                        {
                            sb.Append(c.paths[j] ?? "");
                            sb.Append('\u0002');
                        }
                    }
                    sb.Append('\u001F');
                }
            }
            return sb.ToString();
        }

        private static List<CreatorCacheEntry> CloneCreatorCacheList(List<CreatorCacheEntry> src)
        {
            if (src == null) return new List<CreatorCacheEntry>();
            var r = new List<CreatorCacheEntry>(src.Count);
            for (int i = 0; i < src.Count; i++)
                r.Add(src[i]);
            return r;
        }

        private static Dictionary<string, int> CloneCategoryCountsDict(Dictionary<string, int> src)
        {
            if (src == null) return new Dictionary<string, int>(StringComparer.Ordinal);
            return new Dictionary<string, int>(src, StringComparer.Ordinal);
        }

        private static bool TryGetSharedSideMeta(string key, out List<CreatorCacheEntry> creators, out Dictionary<string, int> counts)
        {
            creators = null;
            counts = null;
            if (string.IsNullOrEmpty(key)) return false;
            lock (s_SharedSideMetaLock)
            {
                if (!s_SharedSideMetaByKey.TryGetValue(key, out SharedSideMetaSnapshot snap) || snap == null) return false;
                creators = CloneCreatorCacheList(snap.Creators);
                counts = CloneCategoryCountsDict(snap.CategoryCounts);
                return true;
            }
        }

        private static void StoreSharedSideMetaIfRoom(string key, List<CreatorCacheEntry> creators, Dictionary<string, int> counts)
        {
            if (string.IsNullOrEmpty(key) || creators == null || counts == null) return;
            lock (s_SharedSideMetaLock)
            {
                if (s_SharedSideMetaByKey.Count >= SharedSideMetaMaxEntries)
                    s_SharedSideMetaByKey.Clear();
                s_SharedSideMetaByKey[key] = new SharedSideMetaSnapshot
                {
                    Creators = CloneCreatorCacheList(creators),
                    CategoryCounts = CloneCategoryCountsDict(counts),
                };
            }
        }

        private enum PackageFilterMode
        {
            None = 0,
            Dependencies = 1,
            Dependents = 2,
        }

        private struct FilterFrame
        {
            public List<FileEntry> files;
            public string desc;
            public PackageFilterMode mode;
            public string masterUid;
            public int count;
            public List<FileEntry> searchBase;
            public string searchLower;
            public string savedNameFilter;
            public bool enteredFromTopSearch;
        }
        private Stack<FilterFrame> _filterStack = new Stack<FilterFrame>();

        private List<FileEntry> currentFilteredFiles = new List<FileEntry>();
        private string filterBaseAnchorKey = null; // Scroll anchor captured when first entering filter mode
        private string currentFilterDesc = null; // Description of active filter (e.g., "Dependents of X.var")
        private PackageFilterMode currentPackageFilterMode = PackageFilterMode.None;
        private string currentPackageFilterMasterUid = null;
        private int currentPackageFilterCount = 0;
        private List<FileEntry> filterSearchBaseFiles = null; // Base list for search within filter mode
        private string filterSearchLower = "";
        private List<FileEntry> topSearchBaseFiles = null; // Base list for top search (non-filter mode)
        private bool _topSearchBaseIsClean = false; // true only when topSearchBaseFiles was captured from an unfiltered load
        private RecyclingGridView recyclingGrid;
        private string filterRestoreAnchorKey = null;
        private Coroutine filterRestoreCoroutine = null;

        private static string GetEntryAnchorKey(FileEntry entry)
        {
            if (entry == null) return null;
            try
            {
                if (!string.IsNullOrEmpty(entry.Uid)) return entry.Uid;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(entry.Path)) return entry.Path;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// After AllPackages ↔ AddonPackages moves, sync <see cref="FileEntry.Path"/> only for rows whose package UID is in <paramref name="packageUids"/>.
        /// </summary>
        internal void RefreshDisplayedVarPathsAfterPackageMoves(HashSet<string> packageUids)
        {
            if (packageUids == null || packageUids.Count == 0) return;
            RefreshVarRelatedPathsInList(currentFilteredFiles, packageUids);
            RefreshVarRelatedPathsInList(selectedFiles, packageUids);
            RefreshVarRelatedPathsInList(topSearchBaseFiles, packageUids);
            RefreshVarRelatedPathsInList(filterSearchBaseFiles, packageUids);
            foreach (var frame in _filterStack)
            {
                RefreshVarRelatedPathsInList(frame.files, packageUids);
                RefreshVarRelatedPathsInList(frame.searchBase, packageUids);
            }
            try
            {
                if (lastFilteredFiles != null && lastFilteredFiles.Count > 0)
                    RefreshVarRelatedPathsInList(lastFilteredFiles, packageUids);
            }
            catch { }
            try
            {
                if (selectedFile != null && !string.IsNullOrEmpty(selectedPath))
                    selectedPath = selectedFile.Path;
            }
            catch { }
            try { RestoreSelectedHoverPath(); } catch { }
        }

        private static void RefreshVarRelatedPathsInList(List<FileEntry> list, HashSet<string> packageUids)
        {
            if (list == null || list.Count == 0 || packageUids == null || packageUids.Count == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                FileEntry fe = list[i];
                if (fe == null) continue;
                VarFileEntry vfe = fe as VarFileEntry;
                if (vfe != null)
                {
                    string rowUid = vfe.GetRowPackageUid();
                    if (string.IsNullOrEmpty(rowUid) || !packageUids.Contains(rowUid)) continue;
                    try { vfe.TryRefreshPathsFromLivePackage(); } catch { }
                    continue;
                }
                PackageListEntry ple = fe as PackageListEntry;
                if (ple != null)
                {
                    string u = ple.Package != null ? ple.Package.Uid : null;
                    if (string.IsNullOrEmpty(u) || !packageUids.Contains(u)) continue;
                    try { ple.RefreshPathsFromPackage(); } catch { }
                    continue;
                }
                SystemFileEntry sfe = fe as SystemFileEntry;
                if (sfe != null && sfe.isVar && sfe.package != null)
                {
                    string u = sfe.package.Uid;
                    if (string.IsNullOrEmpty(u) || !packageUids.Contains(u)) continue;
                    try { sfe.RefreshVarDisplayPathFromPackage(); } catch { }
                }
            }
        }

        private void SaveFilterScrollAnchor()
        {
            filterRestoreAnchorKey = null;
            if (recyclingGrid == null || currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;

            int idx = -1;
            try { idx = recyclingGrid.GetCenterItemIndex(); } catch { idx = -1; }
            if (idx < 0 || idx >= currentFilteredFiles.Count) return;

            filterRestoreAnchorKey = GetEntryAnchorKey(currentFilteredFiles[idx]);
        }

        private bool TryGetPackageFromEntry(FileEntry file, out VarPackage pkg, out string label)
        {
            pkg = null;
            label = null;
            if (file == null) return false;

            try
            {
                if (file is VarFileEntry vfe && vfe.Package != null)
                {
                    pkg = vfe.Package;
                    label = file.Name;
                    return true;
                }
                if (file is PackageListEntry ple && ple.Package != null)
                {
                    pkg = ple.Package;
                    label = ple.Package.Uid;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static string GetPackageGroupShortUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            try
            {
                // VarPackage UID format: Author.Name.Version (Version may be numeric or a constraint like latest/minX)
                int firstDot = uid.IndexOf('.');
                if (firstDot < 0) return null;
                int secondDot = uid.IndexOf('.', firstDot + 1);
                if (secondDot < 0) return null;
                return uid.Substring(0, secondDot);
            }
            catch { return null; }
        }

        private static bool DepRefersToTarget(string depUidOrPath, string targetUid, string targetShort)
        {
            if (string.IsNullOrEmpty(depUidOrPath) || string.IsNullOrEmpty(targetUid)) return false;
            try
            {
                // Normalize common inputs:
                // - Some dependency strings may include ".var" or a full path; strip to filename if so.
                string d = depUidOrPath.Replace('\\', '/');
                int lastSlash = d.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < d.Length) d = d.Substring(lastSlash + 1);
                if (d.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    d = d.Substring(0, d.Length - 4);

                if (string.Equals(d, targetUid, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.IsNullOrEmpty(targetShort)) return false;

                // Accept any dependency that targets the same package group (Author.Name.*), including:
                // - Author.Name.1
                // - Author.Name.latest
                // - Author.Name.min3
                if (d.Length > targetShort.Length + 1 &&
                    d.StartsWith(targetShort, StringComparison.OrdinalIgnoreCase) &&
                    d[targetShort.Length] == '.')
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void RefreshRecycleGridAfterFilterChange()
        {
            if (recyclingGrid == null || currentFilteredFiles == null) return;
            try
            {
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                recyclingGrid.Refresh();
            }
            catch (Exception ex)
            {
                try { LogUtil.Log("[VPB] RefreshRecycleGridAfterFilterChange: " + ex.Message); } catch { }
            }
        }

        /// <summary>
        /// Rebind currently visible rows without rebuilding filters/sort/list contents.
        /// Used when badge-only state changes (e.g. temporary scan-whitelist UID overrides).
        /// </summary>
        internal void RefreshVisibleGridVisualsOnly()
        {
            if (!HasLoadedContent || !IsVisible || IsHubMode) return;
            if (recyclingGrid == null) return;
            try { recyclingGrid.Refresh(); } catch { }
            try { RefreshSelectionVisuals(); } catch { }
        }

        /// <summary>Scene category or already showing package-level rows — use package list for deps/dependents filter.</summary>
        private bool PackageFilterUsesPackageListRows()
        {
            string title = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
            if (!string.IsNullOrEmpty(title) && title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return false;
            FileEntry head = currentFilteredFiles[0];
            if (head == null) return false;
            return head is PackageListEntry || head is MissingPackageListEntry;
        }

        private static HashSet<string> BuildUidSetForDependenciesFilter(VarPackage pkg)
        {
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pkg == null) return uids;
            if (!string.IsNullOrEmpty(pkg.Uid)) uids.Add(pkg.Uid);
            // Prefer SQLite transitive dependency edges when available (matches RecursivePackageDependencies behavior).
            try
            {
                var fromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (VpbLocalDatabase.TryReadRecursiveDependencyUids(pkg.Uid ?? "", fromSql))
                {
                    foreach (var d in fromSql) if (!string.IsNullOrEmpty(d)) uids.Add(d);
                    return uids;
                }
            }
            catch { }

            var deps = pkg.RecursivePackageDependencies;
            if (deps == null) return uids;
            for (int i = 0; i < deps.Count; i++)
            {
                string d = deps[i];
                if (!string.IsNullOrEmpty(d)) uids.Add(d);
            }
            return uids;
        }

        private static HashSet<string> CollectUidsForDependentsPackageListFilter(string targetUid, string targetShort)
        {
            var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(targetUid))
            {
                try { uids.Add(targetUid); } catch { }
            }
            if (FileManager.PackagesByUid == null || string.IsNullOrEmpty(targetUid)) return uids;

            // Prefer SQLite reverse edges when available.
            try
            {
                var fromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (VpbLocalDatabase.TryReadDependentUids(targetUid, targetShort, fromSql))
                {
                    foreach (var d in fromSql) if (!string.IsNullOrEmpty(d)) uids.Add(d);
                    return uids;
                }
            }
            catch { }
            return uids;
        }

        private static void AddVarFileEntriesWithPackageInDepList(List<FileEntry> filtered, FileEntry master, IList<FileEntry> source, List<string> depUids)
        {
            if (depUids == null)
            {
                if (filtered == null || source == null) return;
                return;
            }
            var depSet = new HashSet<string>(depUids, StringComparer.OrdinalIgnoreCase);
            AddVarFileEntriesWithPackageInUidSet(filtered, master, source, depSet);
        }

        private static void AddVarFileEntriesWithPackageInUidSet(List<FileEntry> filtered, FileEntry master, IList<FileEntry> source, HashSet<string> uids)
        {
            if (filtered == null || source == null || uids == null || uids.Count == 0) return;
            for (int i = 0; i < source.Count; i++)
            {
                FileEntry other = source[i];
                if (other == master) continue;
                if (other is VarFileEntry vfe && vfe.Package != null && uids.Contains(vfe.Package.Uid))
                {
                    if (PackageHidePrefs.IsExcludedByGalleryHideFilter(other)) continue;
                    filtered.Add(other);
                }
            }
        }

        /// <summary>Same dependency matching as package-list dependents path (exact UID + version-group / path forms).</summary>
        private static void AddVarFileEntriesThatDependOnPackageUid(List<FileEntry> filtered, FileEntry master, IList<FileEntry> source, string targetUid, string targetShort)
        {
            if (filtered == null || source == null || string.IsNullOrEmpty(targetUid)) return;
            try
            {
                var fromSql = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (VpbLocalDatabase.TryReadDependentUids(targetUid, targetShort, fromSql))
                {
                    AddVarFileEntriesWithPackageInUidSet(filtered, master, source, fromSql);
                    return;
                }
            }
            catch { }
            for (int i = 0; i < source.Count; i++)
            {
                FileEntry other = source[i];
                if (other == master) continue;
                if (other is VarFileEntry vfe && vfe.Package != null)
                {
                    var od = vfe.Package.RecursivePackageDependencies;
                    if (od == null) continue;
                    for (int j = 0; j < od.Count; j++)
                    {
                        if (DepRefersToTarget(od[j], targetUid, targetShort))
                        {
                            if (PackageHidePrefs.IsExcludedByGalleryHideFilter(other)) break;
                            filtered.Add(other);
                            break;
                        }
                    }
                }
            }
        }

        private void PushFilterFrame()
        {
            bool fromTopSearch = false;
            try { fromTopSearch = nameFilterTerms != null && nameFilterTerms.Length > 0; } catch { }

            _filterStack.Push(new FilterFrame
            {
                files = new List<FileEntry>(currentFilteredFiles),
                desc = currentFilterDesc,
                mode = currentPackageFilterMode,
                masterUid = currentPackageFilterMasterUid,
                count = currentPackageFilterCount,
                searchBase = filterSearchBaseFiles != null ? new List<FileEntry>(filterSearchBaseFiles) : null,
                searchLower = filterSearchLower,
                savedNameFilter = nameFilter ?? "",
                enteredFromTopSearch = fromTopSearch,
            });

            // Save scroll anchor only on the first (outermost) entry
            if (_filterStack.Count == 1)
            {
                filterBaseAnchorKey = null;
                SaveFilterScrollAnchor();
                filterBaseAnchorKey = filterRestoreAnchorKey;
            }

            // Initialise filter-mode search base from the current list
            filterSearchBaseFiles = new List<FileEntry>(currentFilteredFiles);
            filterSearchLower = "";
        }

        // Legacy alias – callers will be migrated to PushFilterFrame directly.
        private void EnsureFilterBaseCaptured() => PushFilterFrame();

        private void ApplyFilteredList(List<FileEntry> filtered, string desc)
        {
            if (filtered == null) filtered = new List<FileEntry>();

            // Reset filter-mode search base whenever the filter result changes.
            if (IsFilterActive)
            {
                filterSearchBaseFiles = new List<FileEntry>(filtered);
                // Don't carry the active top search into filter mode — show all deps immediately.
                // The search box is repurposed for searching within the dep list.
                filterSearchLower = "";
                filtered = BuildFilterModeView(filterSearchBaseFiles, filterSearchLower);

                // Clear the top search box so it's ready for in-filter searching
                try
                {
                    nameFilter = "";
                    nameFilterLower = "";
                    nameFilterTerms = new string[0];
                    if (titleSearchInput != null) titleSearchInput.text = "";
                }
                catch { }
            }

            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(filtered);
            currentFilterDesc = desc;

            try
            {
                var st = GetSortState("Files");
                ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);
                if (activeContentType != ContentType.History)
                    GallerySortManager.Instance.SortFiles(currentFilteredFiles, st);
            }
            catch { }

            try { UpdateTabs(); } catch { }
            try { UpdatePaginationText(); } catch { }
            RefreshRecycleGridAfterFilterChange();
            ScrollGalleryToTop();
        }

        public void ApplySearchWithinFilter(string query)
        {
            if (!IsFilterActive) return;
            filterSearchLower = query ?? "";

            if (filterSearchBaseFiles == null) filterSearchBaseFiles = new List<FileEntry>(currentFilteredFiles);

            List<FileEntry> filtered = BuildFilterModeView(filterSearchBaseFiles, filterSearchLower);

            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(filtered);
            try
            {
                var st = GetSortState("Files");
                ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);
                if (activeContentType != ContentType.History)
                    GallerySortManager.Instance.SortFiles(currentFilteredFiles, st);
            }
            catch { }
            try { UpdatePaginationText(); } catch { }
            RefreshRecycleGridAfterFilterChange();
            // Filter-mode search should also start at top of the narrowed results.
            ScrollGalleryToTop();
        }

        private List<FileEntry> BuildFilterModeView(List<FileEntry> baseList, string searchQuery)
        {
            var source = baseList ?? new List<FileEntry>();
            string[] terms = SplitSearchTerms(searchQuery);
            bool needSearch = terms != null && terms.Length > 0;
            var result = new List<FileEntry>();

            for (int i = 0; i < source.Count; i++)
            {
                FileEntry e = source[i];
                if (e == null) continue;

                if (isRatingSortToggleEnabled)
                {
                    try { if (RatingsManager.Instance.GetRating(e) <= 0) continue; } catch { continue; }
                }

                if (!needSearch)
                {
                    result.Add(e);
                    continue;
                }

                if (MatchesFileEntryByScope(e, terms))
                    result.Add(e);
            }
            return result;
        }

        public string GetFilterModeLabel
        {
            get
            {
                switch (currentPackageFilterMode)
                {
                    case PackageFilterMode.Dependencies:
                        // Check if this is a missing dependencies filter
                        if (currentFilteredFiles != null && currentFilteredFiles.Count > 0 && currentFilteredFiles[0] is VirtualFileEntry)
                            return "Missing";
                        return "Dependencies";
                    case PackageFilterMode.Dependents: return "Dependents";
                    default: return "";
                }
            }
        }

        public int GetFilterModeCount => currentPackageFilterCount;

        public bool IsFilterMasterEntry(FileEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(currentPackageFilterMasterUid)) return false;
            try
            {
                if (entry is VarFileEntry vfe && vfe.Package != null)
                    return string.Equals(vfe.Package.Uid, currentPackageFilterMasterUid, StringComparison.OrdinalIgnoreCase);
                if (entry is PackageListEntry ple && ple.Package != null)
                    return string.Equals(ple.Package.Uid, currentPackageFilterMasterUid, StringComparison.OrdinalIgnoreCase);
                if (entry is MissingPackageListEntry mpe)
                    return string.Equals(mpe.RequestedUid, currentPackageFilterMasterUid, StringComparison.OrdinalIgnoreCase);
                // Handle scene files (generic FileEntry with .Path)
                if (entry.Path != null)
                    return string.Equals(entry.Path, currentPackageFilterMasterUid, StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return false;
        }

        private IEnumerator RestoreFilterScrollAnchorNextFrame()
        {
            yield return null;
            filterRestoreCoroutine = null;

            if (string.IsNullOrEmpty(filterRestoreAnchorKey)) yield break;
            if (recyclingGrid == null || currentFilteredFiles == null || currentFilteredFiles.Count == 0) yield break;

            int idx = -1;
            for (int i = 0; i < currentFilteredFiles.Count; i++)
            {
                string key = GetEntryAnchorKey(currentFilteredFiles[i]);
                if (!string.IsNullOrEmpty(key) && string.Equals(key, filterRestoreAnchorKey, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
            {
                try { recyclingGrid.ScrollToCenterItem(idx); } catch { }
            }
        }

        private void ScheduleFilterScrollRestore(string anchorKey)
        {
            filterRestoreAnchorKey = anchorKey;
            if (filterRestoreCoroutine != null)
            {
                try { StopCoroutine(filterRestoreCoroutine); } catch { }
                filterRestoreCoroutine = null;
            }
            try { filterRestoreCoroutine = StartCoroutine(RestoreFilterScrollAnchorNextFrame()); } catch { }
        }

        private List<FileEntry> BuildCategoryEntriesForPackageUids(HashSet<string> uids)
        {
            var result = new List<FileEntry>();
            if (uids == null || uids.Count == 0) return result;

            // Mirror the category/prefix/extension matching logic used in RefreshFilesRoutine / ApplyPackageDelta,
            // but restrict the package set to the UID list.
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            bool hasExt = !Gallery.IsEverythingCategoryExtension(currentExtension)
                && extensions.Length > 0 && !(extensions.Length == 1 && string.IsNullOrEmpty(extensions[0]));
            string[] nameTerms = nameFilterTerms;
            bool hasNameFilt = nameTerms != null && nameTerms.Length > 0;

            foreach (var uid in uids)
            {
                if (string.IsNullOrEmpty(uid)) continue;

                VarPackage pkg = null;
                // IMPORTANT: Filtering is read-only; do not auto-install packages/dependencies here.
                try { pkg = FileManager.GetPackage(uid, ensureInstalled: false); } catch { pkg = null; }
                if (pkg == null) continue;

                // Respect creator filter if set
                try
                {
                    if (!CreatorFilterMatchesPackageCreator(pkg.Creator)) continue;
                }
                catch { continue; }

                List<string> names; List<long> ticks; List<long> sizes;
                try
                {
                    if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                    {
                        continue;
                    }
                }
                catch { continue; }

                for (int i = 0; i < names.Count; i++)
                {
                    string ip = names[i];
                    if (string.IsNullOrEmpty(ip)) continue;

                    // Extension filter
                    if (hasExt)
                    {
                        string entryExt = System.IO.Path.GetExtension(ip);
                        if (string.IsNullOrEmpty(entryExt)) continue;
                        entryExt = entryExt.Substring(1);
                        bool extMatch = false;
                        for (int e = 0; e < extensions.Length; e++)
                            if (string.Equals(entryExt, extensions[e], StringComparison.OrdinalIgnoreCase)) { extMatch = true; break; }
                        if (!extMatch) continue;
                    }
                    else if (Gallery.IsEverythingCategoryExtension(currentExtension))
                    {
                        string pe = System.IO.Path.GetExtension(ip);
                        if (string.IsNullOrEmpty(pe) || pe.Length < 2) continue;
                        if (Gallery.IsEverythingExcludedPreviewExtension(pe.Substring(1))) continue;
                    }

                    // Path prefix filter (normalize slashes so VAR entries like Custom\Scripts\ match Custom/Scripts/)
                    bool pathOk = true;
                    if (currentPaths != null && currentPaths.Count > 0)
                    {
                        pathOk = false;
                        for (int p = 0; p < currentPaths.Count; p++)
                        {
                            string pref = currentPaths[p];
                            if (GalleryInternalPathStartsWithPrefix(ip, pref))
                            {
                                string prefN = GalleryNormalizePathSlashes(pref).TrimEnd('/');
                                if (string.Equals(prefN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (GalleryNormalizePathSlashes(ip).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase)) continue;
                                }
                                pathOk = true;
                                break;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(currentPath))
                    {
                        pathOk = false;
                        if (GalleryInternalPathStartsWithPrefix(ip, currentPath))
                        {
                            string curN = GalleryNormalizePathSlashes(currentPath).TrimEnd('/');
                            if (string.Equals(curN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!GalleryNormalizePathSlashes(ip).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                    pathOk = true;
                            }
                            else pathOk = true;
                        }
                    }
                    if (!pathOk) continue;

                    // Name filter
                    if (hasNameFilt && !MatchesPackageByScope(pkg != null ? pkg.Uid : "", pkg != null ? pkg.Path : "", ip, nameTerms)) continue;

                    var entry = new VarFileEntry(pkg, ip, pkg.LastWriteTime, pkg.Size);

                    // Apply the rest of the active filters (tags/rating/size/scene source/etc)
                    if (!PassesFilters(entry, true)) continue;

                    result.Add(entry);
                }
            }

            // Keep display stable
            try
            {
                var sortState = GetSortState("Files");
                GallerySortManager.Instance.SortFiles(result, sortState);
            }
            catch { }

            return result;
        }

        private List<FileEntry> BuildPackageListEntriesForUids(HashSet<string> uids)
        {
            var result = new List<FileEntry>();
            if (uids == null || uids.Count == 0) return result;

            // Defensive: callers sometimes hand us a set built from mixed sources; avoid double-adds
            // if enumeration includes duplicates due to comparer mismatches or intermediate list reuse.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Prefer SQLite package rows when available to avoid per-UID package resolution.
            var pkgRows = new List<VpbLocalDatabase.PackageRow>();
            bool gotSql = false;
            try { gotSql = VpbLocalDatabase.TryQueryPackageRowsForUids(uids, pkgRows); }
            catch { gotSql = false; }

            Dictionary<string, VpbLocalDatabase.PackageRow> byUid = null;
            if (gotSql)
            {
                byUid = new Dictionary<string, VpbLocalDatabase.PackageRow>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < pkgRows.Count; i++)
                {
                    var r = pkgRows[i];
                    if (!string.IsNullOrEmpty(r.PackageUid))
                        byUid[r.PackageUid] = r;
                }
            }

            foreach (var uid in uids)
            {
                if (string.IsNullOrEmpty(uid)) continue;
                if (!seen.Add(uid)) continue;

                if (byUid != null && byUid.TryGetValue(uid, out var r))
                {
                    DateTime wt = DateTime.MinValue;
                    if (r.LastWriteTicksOrInvalid != long.MinValue)
                    {
                        try { wt = DateTime.FromBinary(r.LastWriteTicksOrInvalid); }
                        catch { wt = DateTime.MinValue; }
                    }
                    long sz = r.PackageSizeOrInvalid != long.MinValue ? r.PackageSizeOrInvalid : 0;

                    try
                    {
                        var row = new PackageListEntry(uid, r.VarPath ?? "", wt, sz, r.PackageCreationTicksOrInvalid, r.FirstScannedTicksOrInvalid);
                        if (PackageHidePrefs.IsExcludedByGalleryHideFilter(row)) continue;
                        result.Add(row);
                    }
                    catch
                    {
                        result.Add(new MissingPackageListEntry(uid));
                    }
                    continue;
                }

                // Fallback: resolve from the live inventory (read-only; do not auto-install).
                try
                {
                    var pkg = FileManager.GetPackage(uid, ensureInstalled: false);
                    if (pkg != null)
                    {
                        var row = new PackageListEntry(pkg);
                        if (PackageHidePrefs.IsExcludedByGalleryHideFilter(row)) continue;
                        result.Add(row);
                    }
                    else result.Add(new MissingPackageListEntry(uid));
                }
                catch
                {
                    result.Add(new MissingPackageListEntry(uid));
                }
            }

            // Stable sort by display name
            try
            {
                result.Sort((a, b) => string.Compare(a != null ? a.Name : "", b != null ? b.Name : "", StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            return result;
        }

        private bool TryGetKnownPosePeopleCount(FileEntry entry, out int peopleCount)
        {
            peopleCount = 1;
            if (entry == null) return false;

            string p = null;
            try { p = entry.Path; } catch { p = null; }
            if (string.IsNullOrEmpty(p) || !p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return false;

            string key = null;
            try { key = !string.IsNullOrEmpty(entry.Uid) ? entry.Uid : entry.Path; } catch { key = entry.Path; }
            if (string.IsNullOrEmpty(key)) return false;

            try
            {
                int persisted;
                if (PosePeopleCountIndex.Instance.TryGet(key, out persisted) && persisted > 0)
                {
                    peopleCount = persisted;
                    return true;
                }
            }
            catch { }

            lock (posePeopleCountCacheLock)
            {
                int cached;
                if (posePeopleCountCache.TryGetValue(key, out cached) && cached > 0)
                {
                    peopleCount = cached;
                    return true;
                }
            }

            return false;
        }

        private void EnqueuePosePeopleIndex(FileEntry entry)
        {
            if (entry == null) return;
            string key = null;
            try { key = !string.IsNullOrEmpty(entry.Uid) ? entry.Uid : entry.Path; } catch { key = entry.Path; }
            if (string.IsNullOrEmpty(key)) return;

            lock (posePeopleIndexLock)
            {
                if (posePeopleIndexQueued.Contains(key)) return;
                posePeopleIndexQueued.Add(key);
                posePeopleIndexQueue.Enqueue(entry);
            }
        }

        private void StartPosePeopleIndexCoroutine(string groupId)
        {
            posePeopleIndexGroupId = groupId ?? "";
            if (posePeopleIndexCoroutine != null)
            {
                StopCoroutine(posePeopleIndexCoroutine);
                posePeopleIndexCoroutine = null;
            }
            posePeopleIndexCoroutine = StartCoroutine(PosePeopleIndexRoutine(groupId));
        }

        private const int PosePeopleIndexWorkerBatchSize = 16;

        private IEnumerator PosePeopleIndexRoutine(string groupId)
        {
            int processed = 0;
            int sinceSave = 0;
            float lastUiUpdate = Time.realtimeSinceStartup;
            float lastRefresh = Time.realtimeSinceStartup;

            while (true)
            {
                if (groupId != posePeopleIndexGroupId) yield break;

                var batch = new List<FileEntry>(PosePeopleIndexWorkerBatchSize);
                lock (posePeopleIndexLock)
                {
                    while (batch.Count < PosePeopleIndexWorkerBatchSize && posePeopleIndexQueue.Count > 0)
                        batch.Add(posePeopleIndexQueue.Dequeue());
                }

                if (batch.Count == 0) break;

                int batchDone = 0;
                string batchGroupId = groupId;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        for (int bi = 0; bi < batch.Count; bi++)
                        {
                            if (batchGroupId != posePeopleIndexGroupId) break;
                            try { ComputeAndCachePosePeopleCount(batch[bi]); } catch { }
                        }
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref batchDone, 1);
                    }
                });

                while (System.Threading.Interlocked.CompareExchange(ref batchDone, 0, 0) == 0)
                    yield return null;

                processed += batch.Count;
                sinceSave += batch.Count;

                if (Time.realtimeSinceStartup - lastUiUpdate > 0.35f)
                {
                    lastUiUpdate = Time.realtimeSinceStartup;
                    try { UpdateTabs(); } catch { }
                }

                if (sinceSave >= 100)
                {
                    sinceSave = 0;
                    try { PosePeopleCountIndex.Instance.Save(); } catch { }
                }

                if (posePeopleFilter != PosePeopleFilter.All && (processed % 250) == 0)
                {
                    if (Time.realtimeSinceStartup - lastRefresh > 1.0f)
                    {
                        lastRefresh = Time.realtimeSinceStartup;
                        try { UpdateTabs(); } catch { }
                    }
                }

                yield return null;
            }

            try { PosePeopleCountIndex.Instance.Save(); } catch { }
            lock (posePeopleIndexLock)
            {
                posePeopleIndexQueue.Clear();
                posePeopleIndexQueued.Clear();
            }
            posePeopleIndexCoroutine = null;
        }

        private static bool TryParsePeopleCountFromJsonText(string text, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(text)) return false;

            int idx = text.LastIndexOf("\"PeopleCount\"", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            int colon = text.IndexOf(':', idx);
            if (colon < 0) return false;

            int i = colon + 1;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i < text.Length && text[i] == '"') i++;

            int start = i;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i <= start) return false;

            int parsed;
            if (!int.TryParse(text.Substring(start, i - start), out parsed)) return false;
            if (parsed <= 0) return false;

            count = parsed;
            return true;
        }

        /// <summary>Reads pose JSON off the main thread; updates memory + persistent index caches.</summary>
        private void ComputeAndCachePosePeopleCount(FileEntry entry)
        {
            if (entry == null) return;
            int count = ComputePosePeopleCountUncached(entry);
            string key = null;
            try { key = !string.IsNullOrEmpty(entry.Uid) ? entry.Uid : entry.Path; } catch { key = entry.Path; }
            if (string.IsNullOrEmpty(key)) return;

            lock (posePeopleCountCacheLock)
            {
                if (posePeopleCountCache.Count > 20000) posePeopleCountCache.Clear();
                posePeopleCountCache[key] = count;
            }
            try { PosePeopleCountIndex.Instance.Set(key, count); } catch { }
        }

        private int ComputePosePeopleCountUncached(FileEntry entry)
        {
            if (entry == null) return 1;

            string entryPath = null;
            try { entryPath = entry.Path; } catch { entryPath = null; }
            if (string.IsNullOrEmpty(entryPath) || !entryPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return 1;

            int count = 1;
            try
            {
                string p = entry.Path ?? "";
                string norm = p.Replace('\\', '/');

                // Only attempt JSON read for pose-like json
                if (norm.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    // Avoid parsing non-pose json when possible
                    bool looksPose = norm.IndexOf("/pose", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    norm.IndexOf("Custom/Atom/Person/Pose", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    norm.IndexOf("Saves/Person", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (looksPose)
                    {
                        bool haveValue = false;

                        // If stream is seekable (local files), read the tail where PeopleCount typically lives.
                        try
                        {
                            using (var stream = entry.OpenStream())
                            {
                                if (stream != null && stream.Stream != null && stream.Stream.CanSeek)
                                {
                                    Stream s = stream.Stream;
                                    long len = 0;
                                    try { len = s.Length; } catch { len = 0; }

                                    if (len > 0)
                                    {
                                        long readLen = Math.Min(65536, len);
                                        s.Seek(-readLen, SeekOrigin.End);
                                        byte[] tailBytes = new byte[(int)readLen];
                                        int totalRead = 0;
                                        while (totalRead < (int)readLen)
                                        {
                                            int r = s.Read(tailBytes, totalRead, (int)readLen - totalRead);
                                            if (r <= 0) break;
                                            totalRead += r;
                                        }

                                        if (totalRead > 0)
                                        {
                                            string tailText = Encoding.UTF8.GetString(tailBytes, 0, totalRead);

                                            int parsed;
                                            if (TryParsePeopleCountFromJsonText(tailText, out parsed))
                                            {
                                                count = parsed;
                                                haveValue = true;
                                            }
                                            else if (tailText.IndexOf("\"Person2\"", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                count = 2;
                                                haveValue = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch { }

                        if (haveValue)
                        {
                            // fall through to cache write
                        }
                        else
                        {
                        // Stream scan for "PeopleCount" to avoid reading entire file into memory.
                        // This is a simple state machine that matches the exact key (case-sensitive as stored).
                        const string needle = "\"PeopleCount\"";
                        int match = 0;
                        bool foundKey = false;
                        bool afterColon = false;
                        int parsed = 0;
                        bool parsingDigits = false;
                        bool haveValue2 = false;

                        try
                        {
                            using (var reader = entry.OpenStreamReader())
                            {
                                char[] buf = new char[4096];
                                int n;
                                if (reader.StreamReader == null) throw new Exception("Null StreamReader");
                                while ((n = reader.StreamReader.Read(buf, 0, buf.Length)) > 0)
                                {
                                    for (int bi = 0; bi < n; bi++)
                                    {
                                        char c = buf[bi];

                                        if (!foundKey)
                                        {
                                            if (c == needle[match])
                                            {
                                                match++;
                                                if (match == needle.Length)
                                                {
                                                    foundKey = true;
                                                    match = 0;
                                                }
                                            }
                                            else
                                            {
                                                match = (c == needle[0]) ? 1 : 0;
                                            }
                                            continue;
                                        }

                                        if (!afterColon)
                                        {
                                            if (c == ':')
                                            {
                                                afterColon = true;
                                            }
                                            continue;
                                        }

                                        if (!parsingDigits)
                                        {
                                            if (char.IsWhiteSpace(c)) continue;
                                            if (c == '"') continue;
                                            if (char.IsDigit(c))
                                            {
                                                parsingDigits = true;
                                                parsed = (c - '0');
                                                continue;
                                            }
                                            // Unexpected token; stop trying.
                                            break;
                                        }

                                        // parsingDigits
                                        if (char.IsDigit(c))
                                        {
                                            int d = (c - '0');
                                            // Avoid overflow; PeopleCount is tiny.
                                            if (parsed < 1000) parsed = parsed * 10 + d;
                                            continue;
                                        }

                                        // End of digits
                                        if (parsed > 0)
                                        {
                                            count = parsed;
                                            haveValue2 = true;
                                        }
                                        break;
                                    }

                                    // Early exit once we got a value.
                                    if (haveValue2) break;
                                }

                                // Handle case where digits end at EOF
                                if (!haveValue2 && foundKey && afterColon && parsingDigits && parsed > 0) count = parsed;
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                        }
                    }
                }
            }
            catch
            {
                count = 1;
            }

            return count;
        }

        private int GetPosePeopleCount(FileEntry entry)
        {
            if (entry == null) return 1;

            string entryPath = null;
            try { entryPath = entry.Path; } catch { entryPath = null; }
            if (string.IsNullOrEmpty(entryPath) || !entryPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return 1;

            string key = null;
            try { key = !string.IsNullOrEmpty(entry.Uid) ? entry.Uid : entry.Path; } catch { key = entry.Path; }
            if (string.IsNullOrEmpty(key)) return 1;

            try
            {
                int persisted;
                if (PosePeopleCountIndex.Instance.TryGet(key, out persisted))
                {
                    lock (posePeopleCountCacheLock)
                    {
                        if (posePeopleCountCache.Count > 20000) posePeopleCountCache.Clear();
                        posePeopleCountCache[key] = persisted;
                    }
                    return persisted;
                }
            }
            catch { }

            lock (posePeopleCountCacheLock)
            {
                int cached;
                if (posePeopleCountCache.TryGetValue(key, out cached)) return cached;
            }

            int count = ComputePosePeopleCountUncached(entry);
            lock (posePeopleCountCacheLock)
            {
                if (posePeopleCountCache.Count > 20000) posePeopleCountCache.Clear();
                posePeopleCountCache[key] = count;
            }
            try { PosePeopleCountIndex.Instance.Set(key, count); } catch { }
            return count;
        }

        private bool PassesFilters(FileEntry entry)
        {
            return PassesFilters(entry, false, false);
        }

        /// <summary>
        /// Clothing path gate (classify + subfilters) for <see cref="VarFileEntry.Path"/> or loose file path form.
        /// </summary>
        internal static bool PassesClothingGalleryFiltersForPath(string path, ClothingSubfilter clothingSubfilter, bool isVarPackageEntry)
        {
            string p = path ?? "";
            int lastDot = p.LastIndexOf('.');
            string ext = (lastDot >= 0 && lastDot < p.Length - 1) ? p.Substring(lastDot + 1) : "";
            bool isPreset = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);

            string norm = p.Replace('\\', '/');
            bool isCustomLoose = !isVarPackageEntry &&
                                (norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 norm.IndexOf("/Saves/", StringComparison.OrdinalIgnoreCase) >= 0);

            ClothingLoadingUtils.ResourceKind k;
            ClothingLoadingUtils.ResourceGender g;
            ClothingLoadingUtils.ClassifyClothingHairPath(p, out k, out g);
            if (k != ClothingLoadingUtils.ResourceKind.Clothing) return false;

            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(p);

            // Issue #101: when no clothing subfilter is active, default to base items only (.vam)
            // so the grid does not show duplicate .vam + .vap pairs. Toggling "Presets" or "Custom"
            // on the side panel re-enables preset visibility.
            if (clothingSubfilter == 0)
            {
                if (isPreset) return false;
            }
            else
            {
                bool wantsRealType = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
                bool wantsDecalType = ((clothingSubfilter & ClothingSubfilter.Decals) != 0);

                bool typeExplicit = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Decals)) != 0);
                if (typeExplicit)
                {
                    bool okType = (!isDecal && (clothingSubfilter & ClothingSubfilter.RealClothing) != 0) ||
                                  (isDecal && (clothingSubfilter & ClothingSubfilter.Decals) != 0);
                    if (!okType) return false;
                }
                else
                {
                    if (wantsRealType && isDecal && !wantsDecalType) return false;
                }

                bool wantsPresets = (clothingSubfilter & ClothingSubfilter.Presets) != 0;
                bool wantsCustom = (clothingSubfilter & ClothingSubfilter.Custom) != 0;
                if (wantsPresets) { if (!isPreset) return false; }
                if (wantsCustom) { if (!isCustomLoose) return false; }
                // Default-hide presets unless user explicitly opts in via Presets/Custom toggle.
                if (!wantsPresets && !wantsCustom) { if (isPreset) return false; }
                if ((clothingSubfilter & ClothingSubfilter.Items) != 0) { if (isPreset) return false; }
                // If gender unknown, keep visible under either toggle (VaM content often not in gendered folders).
                if ((clothingSubfilter & ClothingSubfilter.Male) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Male && g != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                if ((clothingSubfilter & ClothingSubfilter.Female) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Female && g != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
            }

            return true;
        }

        /// <summary>
        /// Hair path gate (classify + subfilters) for <see cref="VarFileEntry.Path"/> or loose file path form.
        /// Mirrors Clothing preset-hiding behavior from Issue #101.
        /// </summary>
        internal static bool PassesHairGalleryFiltersForPath(string path, HairSubfilter hairSubfilter, bool isVarPackageEntry)
        {
            string p = path ?? "";
            int lastDot = p.LastIndexOf('.');
            string ext = (lastDot >= 0 && lastDot < p.Length - 1) ? p.Substring(lastDot + 1) : "";
            bool isPreset = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);

            string norm = p.Replace('\\', '/');
            bool isCustomLoose = !isVarPackageEntry &&
                                (norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                 norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 norm.IndexOf("/Saves/", StringComparison.OrdinalIgnoreCase) >= 0);

            ClothingLoadingUtils.ResourceKind k;
            ClothingLoadingUtils.ResourceGender g;
            ClothingLoadingUtils.ClassifyClothingHairPath(p, out k, out g);
            if (k != ClothingLoadingUtils.ResourceKind.Hair) return false;

            // Issue #101 parity: when no hair subfilter active, default to base items only (.vam)
            // so the grid does not show duplicate .vam + .vap pairs.
            if (hairSubfilter == 0)
            {
                if (isPreset) return false;
            }
            else
            {
                bool wantsPresets = (hairSubfilter & HairSubfilter.Presets) != 0;
                bool wantsCustom = (hairSubfilter & HairSubfilter.Custom) != 0;
                if (wantsPresets) { if (!isPreset) return false; }
                if (wantsCustom) { if (!isCustomLoose) return false; }
                // Default-hide presets unless user explicitly opts in via Presets/Custom toggle.
                if (!wantsPresets && !wantsCustom) { if (isPreset) return false; }
                if ((hairSubfilter & HairSubfilter.Items) != 0) { if (isPreset) return false; }
                // If gender unknown, keep visible under either toggle.
                if ((hairSubfilter & HairSubfilter.Male) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Male && g != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
                if ((hairSubfilter & HairSubfilter.Female) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Female && g != ClothingLoadingUtils.ResourceGender.Unknown) return false; }
            }

            return true;
        }

        private bool PassesFilters(FileEntry entry, bool ignorePosePeopleFilter)
        {
            return PassesFilters(entry, ignorePosePeopleFilter, false);
        }

        private bool PassesFilters(FileEntry entry, bool ignorePosePeopleFilter, bool skipClothingGalleryFilters)
        {
            if (entry == null) return false;

            // History: skip category-only filters (rating/size/source); keep path filter, search, tags.
            if (activeContentType == ContentType.History)
            {
                if (!string.IsNullOrEmpty(currentPackagePathFilter))
                {
                    string folder = TryGetPathFilterFolderForEntry(entry);
                    if (!GalleryPathFilterMatchesFolder(folder, currentPackagePathFilter))
                        return false;
                }
                if (nameFilterTerms != null && nameFilterTerms.Length > 0)
                    if (!MatchesFileEntryByScope(entry, nameFilterTerms))
                        return false;
                if (activeTags != null && activeTags.Count > 0)
                {
                    bool tagMatch = false;
                    foreach (var tag in activeTags)
                    {
                        if (entry.Path != null && entry.Path.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            tagMatch = true;
                            break;
                        }
                        if (TagsManager.Instance.HasTag(entry.Uid, tag))
                        {
                            tagMatch = true;
                            break;
                        }
                    }
                    if (!tagMatch) return false;
                }
                return true;
            }

            if (!string.IsNullOrEmpty(currentPackagePathFilter))
            {
                string folder = TryGetPathFilterFolderForEntry(entry);
                if (!GalleryPathFilterMatchesFolder(folder, currentPackagePathFilter))
                    return false;
            }

            // Hide filtering and sort-only narrowing run in PostFilesListHideAndSortFollowupRoutine after the grid is shown.
            // to avoid per-entry FileManager.FileExists calls blocking the scan drain loop.

            // Clothing subfilter (Gallery left Tags panel)
            // Applies only when browsing Clothing category.
            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            string cp = currentPath ?? "";
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("/Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("\\Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isClothing && !skipClothingGalleryFilters)
            {
                string p = entry.Path;
                bool isVarPackageEntry = (entry is VarFileEntry) || ((entry as SystemFileEntry) != null && ((SystemFileEntry)entry).isVar);
                if (!PassesClothingGalleryFiltersForPath(p, clothingSubfilter, isVarPackageEntry))
                    return false;
            }

            // Hair subfilter (Issue #101 parity with Clothing)
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("/Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("\\Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isHair)
            {
                string p = entry.Path;
                bool isVarPackageEntry = (entry is VarFileEntry) || ((entry as SystemFileEntry) != null && ((SystemFileEntry)entry).isVar);
                if (!PassesHairGalleryFiltersForPath(p, hairSubfilter, isVarPackageEntry))
                    return false;
            }

            // Pose subfilter (Single vs Dual)
            bool isPose = title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!ignorePosePeopleFilter && isPose && posePeopleFilter != PosePeopleFilter.All)
            {
                int peopleCount = GetPosePeopleCount(entry);
                bool isDual = peopleCount >= 2;
                if (posePeopleFilter == PosePeopleFilter.Single)
                {
                    if (isDual) return false;
                }
                else if (posePeopleFilter == PosePeopleFilter.Dual)
                {
                    if (!isDual) return false;
                }
            }

            // Appearance subfilter (Gallery left Tags panel)
            // Applies only when browsing Appearance category.
            bool isAppearance = title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0;

            // Global source filter (early gate). Cheap type check, runs first. Bypassed when the
            // per-category Local toggle is ON for this category (toggle has override semantics).
            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
            {
                bool toggleOverridesGlobal = false;
                if (isAppearance && string.Equals(currentAppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase))
                    toggleOverridesGlobal = true;
                bool isScene = title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0
                               || cp.IndexOf("Saves/scene", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isScene && string.Equals(currentSceneSourceFilter, "local", StringComparison.OrdinalIgnoreCase))
                    toggleOverridesGlobal = true;

                if (!toggleOverridesGlobal)
                {
                    bool isVarBackedForGate = IsVarBacked(entry);
                    if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local && isVarBackedForGate) return false;
                    if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var   && !isVarBackedForGate) return false;
                }
            }

            if (isAppearance)
            {
                string p = entry.Path ?? "";
                string norm = p.Replace('\\', '/');

                int lastDot = norm.LastIndexOf('.');
                string ext = (lastDot >= 0 && lastDot < norm.Length - 1) ? norm.Substring(lastDot + 1) : "";
                bool isVap = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);

                bool isVarEntry = (entry is VarFileEntry);

                // Inside .var: identify by internal path prefix
                bool isVarAppearanceVap = false;
                var vfe = entry as VarFileEntry;
                if (vfe != null)
                {
                    string ip = (vfe.InternalPath ?? "").Replace('\\', '/');
                    isVarAppearanceVap = isVap && ip.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
                }

                // Outside .var: identify by VaM folders
                bool isLocalAppearanceVap = (!isVarEntry) && isVap &&
                    (
                        norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase) ||
                        norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase)
                    );

                // Per-category Local toggle override. When ON, ignore global filter and show local-only.
                // When OFF (empty string), the global filter applies at the earlier gate.
                bool appearanceLocalToggleOn = string.Equals(currentAppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase);
                if (appearanceLocalToggleOn)
                {
                    if (IsVarBacked(entry)) return false;
                    if (AppearanceGenderClassifier.IsAppearanceFolderBrowsePath(cp)
                        && !AppearanceGenderClassifier.EntryMatchesAppearanceBrowseScope(entry, cp, currentPaths))
                        return false;
                }

                if (appearanceSubfilter != 0)
                {
                    bool isCustomAppearance = AppearanceGenderClassifier.ResolveIsCustomAppearance(entry);
                    bool isPresetAppearance = isVap && AppearanceGenderClassifier.ResolveIsPresetAppearance(entry);

                    string catForGender = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : (titleText != null ? titleText.text : "");
                    EnsureAppearanceGenderRefreshCaches(catForGender ?? "");
                    AppearanceGender g = AppearanceGender.Unknown;
                    try { g = AppearanceGenderClassifier.ClassifyForGenderFilter(entry, catForGender ?? "", _appearanceUserTagsByRowKey); } catch { g = AppearanceGender.Unknown; }

                    bool wantsPresets = (appearanceSubfilter & AppearanceSubfilter.Presets) != 0;
                    bool wantsCustom = (appearanceSubfilter & AppearanceSubfilter.Custom) != 0;
                    if (wantsPresets || wantsCustom)
                    {
                        if (!(wantsPresets && wantsCustom))
                        {
                            if (wantsPresets && !isPresetAppearance) return false;
                            if (wantsCustom && !isCustomAppearance) return false;
                        }
                    }

                    if (!AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(g, appearanceSubfilter))
                        return false;
                }
            }

            // Rating/Size filters
            if (!string.IsNullOrEmpty(currentRatingFilter))
            {
                // Rating filter when status is NOT set (or even if it is, as an additional filter)
                int rating = RatingsManager.Instance.GetRating(entry);
                if (currentRatingFilter == "All Ratings") { if (rating <= 0) return false; }
                else if (currentRatingFilter == "5 Stars") { if (rating != 5) return false; }
                else if (currentRatingFilter == "4 Stars") { if (rating != 4) return false; }
                else if (currentRatingFilter == "3 Stars") { if (rating != 3) return false; }
                else if (currentRatingFilter == "2 Stars") { if (rating != 2) return false; }
                else if (currentRatingFilter == "1 Star") { if (rating != 1) return false; }
                else if (currentRatingFilter == "No Ratings") { if (rating != 0) return false; }
            }

            if (!string.IsNullOrEmpty(currentSizeFilter))
            {
                // Size filter when status is NOT set
                long size = entry.Size;
                long mb = 1024 * 1024;
                if (currentSizeFilter == "Tiny (< 10MB)") { if (size >= 10 * mb) return false; }
                else if (currentSizeFilter == "Small (10-100MB)") { if (size < 10 * mb || size >= 100 * mb) return false; }
                else if (currentSizeFilter == "Medium (100-500MB)") { if (size < 100 * mb || size >= 500 * mb) return false; }
                else if (currentSizeFilter == "Large (500MB-1GB)") { if (size < 500 * mb || size >= 1024 * mb) return false; }
                else if (currentSizeFilter == "Very Large (> 1GB)") { if (size < 1024 * mb) return false; }
            }

            // Scene Source Filter.
            // Per-category Local toggle override. When ON, ignore global filter and show local-only.
            // When OFF, the global filter applies at the earlier gate.
            // Gated to scene categories so leftover toggle state cannot hide var rows in unrelated categories.
            bool isSceneCategoryForLocal =
                title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("Saves/scene", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("Saves\\scene", StringComparison.OrdinalIgnoreCase) >= 0;
            bool sceneLocalToggleOn = isSceneCategoryForLocal
                && string.Equals(currentSceneSourceFilter, "local", StringComparison.OrdinalIgnoreCase);
            if (sceneLocalToggleOn)
            {
                if (IsVarBacked(entry)) return false;
            }

            // Name Filter
            if (nameFilterTerms != null && nameFilterTerms.Length > 0)
                if (!MatchesFileEntryByScope(entry, nameFilterTerms))
                    return false;

            // Tag Filter
            if (activeTags != null && activeTags.Count > 0)
            {
                bool tagMatch = false;
                foreach (var tag in activeTags)
                {
                    // Check path-based tags (original logic)
                    if (entry.Path.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        tagMatch = true;
                        break;
                    }

                    // Check user-defined tags
                    if (TagsManager.Instance.HasTag(entry.Uid, tag))
                    {
                        tagMatch = true;
                        break;
                    }
                }
                if (!tagMatch) return false;
            }

            // Gallery SQLite user tags (Available Filter Mode). VAR rows from SQLite bulk query already match tags;
            // loose Custom/Saves files merged afterward must still be checked (same keys as gallery_item_user_tag).
            if (_userTagAvailFilterMode && activeUserTags != null && activeUserTags.Count > 0
                && activeContentType == ContentType.Category && VpbSqlite3.IsAvailable)
            {
                VarFileEntry vfeUt = entry as VarFileEntry;
                bool bulkSqlAlreadyFilteredVar =
                    (_refreshSqliteBulkIncludedUserTagGridFilter || System.Threading.Thread.VolatileRead(ref _refreshWorkerFallbackUserTagPrefilterFlag) != 0)
                    && vfeUt != null
                    && vfeUt.Package != null;

                if (!bulkSqlAlreadyFilteredVar)
                {
                    string catUt = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                    if (string.IsNullOrEmpty(catUt)) return false;
                    string pkgK, ipK;
                    if (!TryGetGalleryRowKeysForUserTags(entry, out pkgK, out ipK)) return false;
                    if (!VpbLocalDatabase.TryGalleryRowMatchesAllUserTags(catUt, pkgK, ipK, activeUserTags))
                        return false;
                }
            }

            return true;
        }

        private IEnumerator RetryRefreshAfterNoCacheDelay()
        {
            // No fixed delay. Wait until FileManager scan likely finished, with bounded backoff.
            float start = Time.realtimeSinceStartup;
            float nextWait = 0.05f;
            int polls = 0;
            while (!Gallery.IsSuppressed() && !IsHubMode)
            {
                polls++;
                bool scanning = false;
                try { scanning = FileManager.IsScanning; } catch { scanning = false; }
                if (!scanning) break;

                // Cap total wait so we never "hang" retry forever.
                float elapsed = Time.realtimeSinceStartup - start;
                if (elapsed >= 2.5f) break;

                // Backoff up to 0.5s between polls.
                float wait = Mathf.Clamp(nextWait, 0.02f, 0.5f);
                nextWait = Mathf.Min(nextWait * 1.7f, 0.5f);
                yield return new WaitForSecondsRealtime(wait);
            }

            if (!Gallery.IsSuppressed() && !IsHubMode)
            {
                if (LogGalleryRefreshDeepTiming)
                {
                    try
                    {
                        float elapsed = Time.realtimeSinceStartup - start;
                        LogUtil.Log("[VPB.Gallery.DeepTiming] RetryRefreshAfterNoCacheDelay FIRE | waited=" + (elapsed * 1000f).ToString("0") + "ms"
                            + " | polls=" + polls
                            + " | FileManager.IsScanning=" + (FileManager.IsScanning ? "1" : "0")
                            + " | lastPackageRefreshTime=" + FileManager.lastPackageRefreshTime.ToString("o"));
                    }
                    catch { }
                }
                LogUtil.Log("[VPB] RetryRefreshAfterNoCacheDelay: retrying refresh for packages with missing cache.");
                // isRetry=true keeps _cacheRetryPending=true so this retry cannot spawn another retry.
                RefreshFiles(false, false, isRetry: true);
            }
            else
            {
                // Refresh was skipped; clear the flag so future user-triggered loads can retry.
                _cacheRetryPending = false;
            }
        }

        public void RefreshFiles(bool keepScroll = false, bool scrollToBottom = false, bool isRetry = false, string refreshDebugSource = null)
        {
            // Clear any active dependency filter when refreshing
            ClearPackageFilter();
            // Reset in-memory top search base; RefreshFiles rebuilds the list.
            topSearchBaseFiles = null;
            _topSearchBaseIsClean = false;

            // Check if gallery auto-refresh is suppressed (during scene/preset loading)
            if (Gallery.IsSuppressed())
            {
                LogUtil.Log("[VPB] GalleryPanel.RefreshFiles: SKIPPED (suppressed)");
                CompletePaneLoadTimingIfPending("(refresh suppressed)");
                return;
            }
            
            if (IsHubMode)
            {
                RefreshHubItems();
                return;
            }

            if (IsSettingsPanelOpen() || settingsListViewActive)
            {
                RefreshInternalSettingsListRows(keepScroll);
                return;
            }

            // Reset the retry guard on user-triggered refreshes so future loads can retry again.
            // When called from RetryRefreshAfterNoCacheDelay (isRetry=true) we intentionally keep
            // _cacheRetryPending=true so that the retry run does NOT spawn yet another retry.
            if (!isRetry)
                _cacheRetryPending = false;

            if (thumbnailCacheCoroutine != null) StopCoroutine(thumbnailCacheCoroutine);
            thumbnailCacheCoroutine = null;
            if (pendingThumbnailCacheJobs != null) pendingThumbnailCacheJobs.Clear();
            _thumbCacheTotalEnqueued = 0;
            _thumbCacheSaved = 0;
            _thumbCacheFinishTime = -1f;
            _nextThumbPriority = 0;
            HideThumbnailCacheProgress();
            if (_deferredGallerySideTabsCoroutine != null)
            {
                try { StopCoroutine(_deferredGallerySideTabsCoroutine); } catch { }
                _deferredGallerySideTabsCoroutine = null;
            }
            if (_sideTabsTagCountSliceCo != null)
            {
                try { StopCoroutine(_sideTabsTagCountSliceCo); } catch { }
                _sideTabsTagCountSliceCo = null;
            }
            if (_historyModeCountsCo != null)
            {
                try { StopCoroutine(_historyModeCountsCo); } catch { }
                _historyModeCountsCo = null;
            }
            if (_earlyMetaApplyCoroutine != null)
            {
                try { StopCoroutine(_earlyMetaApplyCoroutine); } catch { }
                _earlyMetaApplyCoroutine = null;
            }
            // Rotate the group ID here (synchronously) so that any in-flight thumbnail callbacks
            // from the old category fail the capturedGroupId == currentLoadingGroupId guard and
            // don't pollute the new session. The coroutine's yield-return-null would be too late.
            if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
                CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
            currentLoadingGroupId = Guid.NewGuid().ToString();
            unchecked { _deferredSubPaneSessionId++; }
            System.Threading.Interlocked.Increment(ref galleryFileRefreshSequence);
            if (refreshCoroutine != null) StopCoroutine(refreshCoroutine);
            if (_refreshHistoryLightCo != null)
            {
                try { StopCoroutine(_refreshHistoryLightCo); } catch { }
                _refreshHistoryLightCo = null;
            }
            _boundCategoryNavSessionForCurrentRefresh = _categoryTypeNavStopwatch != null ? _categoryTypeNavTargetSession : 0;
            _refreshFilesDebugSource = refreshDebugSource;
            refreshCoroutine = StartCoroutine(RefreshFilesRoutine(keepScroll, scrollToBottom));
        }

        /// <summary>
        /// Incrementally updates the gallery when only a subset of packages changed.
        /// Removes entries from <paramref name="removed"/> packages and inserts entries from
        /// <paramref name="added"/> packages that pass the current filters, then re-sorts and
        /// restores the scroll position using a UID anchor so the viewport doesn't jump.
        ///
        /// Falls back to a full <see cref="RefreshFiles"/> when the gallery hasn't loaded yet
        /// or the delta lists are null/empty (which shouldn't normally happen, but is safe).
        /// </summary>
        /// <returns>True when the grid or side metadata was updated.</returns>
        public bool ApplyPackageDelta(List<VarPackage> added, List<VarPackage> removed)
        {
            lastPackageDeltaChangedGrid = false;
            if (!VamOnDemandLoader.IsMainThread())
            {
                LogPackageDeltaSkip("background_thread");
                return false;
            }
            if (Gallery.IsSuppressed())
            {
                LogPackageDeltaSkip("suppressed");
                return false;
            }
            if (IsHubMode)
            {
                LogPackageDeltaSkip("hub_mode");
                return false;
            }
            if (IsSettingsPanelOpen() || settingsListViewActive)
            {
                LogPackageDeltaSkip("settings_open");
                return false;
            }

            bool hasRemovals = removed != null && removed.Count > 0;
            bool hasAdditions = added != null && added.Count > 0;

            // If we have never loaded, the scan just completed and we have a full PackagesByUid
            // for the first time – do a clean initial load now.
            if (!hasLoadedContent || recyclingGrid == null || scrollRect == null)
            {
                if (!hasRemovals && !hasAdditions && HasDeferredStartupRefreshPending)
                {
                    LogPackageDeltaSkip("deferred_startup_refresh_pending");
                    return false;
                }
                if (!hasRemovals && !hasAdditions && VpbLocalDatabase.HasGalleryIndexReadyOnDisk())
                {
                    lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime;
                    refreshOnNextShow = false;
                    LogPackageDeltaSkip("init_sql_ready_empty_delta");
                    return false;
                }
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta full RefreshFiles (not loaded yet) title='"
                        + (currentCategoryTitle ?? "") + "'");
                }
                catch { }
                RefreshFiles(false);
                return true;
            }

            // If neither list has entries the package set didn't change at all.
            // Just sync the timestamp so future notifications aren't treated as "new" and return
            // without touching the grid – this is the key guard that prevents a spurious full
            // refresh (and scroll-to-top) when the initial scan finds no package delta.
            if (!hasRemovals && !hasAdditions)
            {
                lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime;
                refreshOnNextShow = false;
                LogPackageDeltaSkip("empty_delta");
                return false;
            }

            try
            {
                LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta START title='" + (currentCategoryTitle ?? "")
                    + "' ext='" + (currentExtension ?? "") + "' path='" + (currentPath ?? "")
                    + "' added=" + (added != null ? added.Count : 0)
                    + " removed=" + (removed != null ? removed.Count : 0)
                    + " gridBefore=" + (currentFilteredFiles != null ? currentFilteredFiles.Count : 0));
            }
            catch { }

            // If the refresh coroutine is still running (shouldn't normally happen after the
            // !init||flag gate, but be defensive) cancel it so we work on a stable list.
            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
            }
            if (_earlyMetaApplyCoroutine != null)
            {
                try { StopCoroutine(_earlyMetaApplyCoroutine); } catch { }
                _earlyMetaApplyCoroutine = null;
            }

            // ── Scroll anchor ─────────────────────────────────────────────────────────────
            // Save the UID of the item currently centred in the viewport so we can scroll
            // back to it after the list is modified (indices shift when items are inserted or
            // removed before the anchor position).
            string anchorUid = null;
            int centerIdx = recyclingGrid.GetCenterItemIndex();
            if (centerIdx >= 0 && centerIdx < currentFilteredFiles.Count)
                anchorUid = currentFilteredFiles[centerIdx]?.Uid;

            bool changed = false;
            bool skippedForNoCache = false;

            // ── Remove ────────────────────────────────────────────────────────────────────
            if (removed != null && removed.Count > 0)
            {
                var removedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pkg in removed) if (pkg != null) removedUids.Add(pkg.Uid);

                int before = currentFilteredFiles.Count;
                for (int i = currentFilteredFiles.Count - 1; i >= 0; i--)
                {
                    var vfe = currentFilteredFiles[i] as VarFileEntry;
                    if (vfe?.Package != null && removedUids.Contains(vfe.Package.Uid))
                        currentFilteredFiles.RemoveAt(i);
                }
                for (int i = lastFilteredFiles.Count - 1; i >= 0; i--)
                {
                    var vfe = lastFilteredFiles[i] as VarFileEntry;
                    if (vfe?.Package != null && removedUids.Contains(vfe.Package.Uid))
                        lastFilteredFiles.RemoveAt(i);
                }
                if (currentFilteredFiles.Count != before) changed = true;
            }

            // ── Add ───────────────────────────────────────────────────────────────────────
            if (added != null && added.Count > 0)
            {
                string[] extensions = string.IsNullOrEmpty(currentExtension)
                    ? new string[0]
                    : currentExtension.Split('|');
                bool hasExt = !Gallery.IsEverythingCategoryExtension(currentExtension)
                    && extensions.Length > 0 && !(extensions.Length == 1 && string.IsNullOrEmpty(extensions[0]));
                string[] nameTerms = nameFilterTerms;
                bool hasNameFilt = nameTerms != null && nameTerms.Length > 0;

                var newEntries = new List<FileEntry>();
                var existingUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int fi = 0; fi < currentFilteredFiles.Count; fi++)
                {
                    FileEntry fe = currentFilteredFiles[fi];
                    if (fe != null && !string.IsNullOrEmpty(fe.Uid))
                        existingUids.Add(fe.Uid);
                }

                foreach (var pkg in added)
                {
                    if (pkg == null) continue;

                    // Package-level creator filter
                    if (!CreatorFilterMatchesPackageCreator(pkg.Creator)) continue;

                    List<string> names; List<long> ticks; List<long> sizes;
                    if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                    {
                        try { pkg.Scan(); } catch { }
                        if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                        {
                            skippedForNoCache = true;
                            continue;
                        }
                    }

                    for (int i = 0; i < names.Count; i++)
                    {
                        string ip = names[i];

                        // Extension filter
                        if (hasExt)
                        {
                            string entryExt = System.IO.Path.GetExtension(ip);
                            if (string.IsNullOrEmpty(entryExt)) continue;
                            entryExt = entryExt.Substring(1);
                            bool extMatch = false;
                            for (int e = 0; e < extensions.Length; e++)
                                if (string.Equals(entryExt, extensions[e], StringComparison.OrdinalIgnoreCase)) { extMatch = true; break; }
                            if (!extMatch) continue;
                        }
                        else if (Gallery.IsEverythingCategoryExtension(currentExtension))
                        {
                            string pe = System.IO.Path.GetExtension(ip);
                            if (string.IsNullOrEmpty(pe) || pe.Length < 2) continue;
                            if (Gallery.IsEverythingExcludedPreviewExtension(pe.Substring(1))) continue;
                        }

                        // Path prefix filter (mirrors RefreshFilesRoutine ThreadPool worker logic)
                        bool pathOk = true;
                        if (currentPaths != null && currentPaths.Count > 0)
                        {
                            pathOk = false;
                            for (int p = 0; p < currentPaths.Count; p++)
                            {
                                string pref = currentPaths[p];
                                if (GalleryInternalPathStartsWithPrefix(ip, pref))
                                {
                                    string prefN = GalleryNormalizePathSlashes(pref).TrimEnd('/');
                                    if (string.Equals(prefN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (GalleryNormalizePathSlashes(ip).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase)) continue;
                                    }
                                    pathOk = true;
                                    break;
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(currentPath))
                        {
                            pathOk = false;
                            if (GalleryInternalPathStartsWithPrefix(ip, currentPath))
                            {
                                string curN = GalleryNormalizePathSlashes(currentPath).TrimEnd('/');
                                if (string.Equals(curN, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!GalleryNormalizePathSlashes(ip).StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                        pathOk = true;
                                }
                                else pathOk = true;
                            }
                        }
                        if (!pathOk) continue;

                        // Name filter
                        if (hasNameFilt && !MatchesPackageByScope(pkg != null ? pkg.Uid : "", pkg != null ? pkg.Path : "", ip, nameTerms)) continue;

                        DateTime entryTime = pkg.LastWriteTime;
                        if (ticks != null && i < ticks.Count && ticks[i] != 0L)
                        {
                            try { entryTime = new DateTime(ticks[i], DateTimeKind.Utc).ToLocalTime(); }
                            catch { entryTime = pkg.LastWriteTime; }
                        }
                        long entrySize = pkg.Size;
                        if (sizes != null && i < sizes.Count)
                            entrySize = sizes[i];

                        var entry = new VarFileEntry(pkg, ip, entryTime, entrySize);

                        // Full filter check (clothing/appearance subfilters, tags, rating, size, scene source …)
                        if (!PassesFilters(entry, true)) continue;
                        if (!existingUids.Add(entry.Uid)) continue;

                        newEntries.Add(entry);
                    }
                }

                if (newEntries.Count == 0 && added != null && added.Count > 0
                    && string.Equals(currentExtension, "varpkg", StringComparison.OrdinalIgnoreCase))
                {
                    var addedUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int ai = 0; ai < added.Count; ai++)
                    {
                        VarPackage ap = added[ai];
                        if (ap != null && !string.IsNullOrEmpty(ap.Uid))
                            addedUids.Add(ap.Uid);
                    }
                    if (addedUids.Count > 0)
                    {
                        List<FileEntry> pkgRows = BuildPackageListEntriesForUids(addedUids);
                        for (int pi = 0; pi < pkgRows.Count; pi++)
                        {
                            FileEntry row = pkgRows[pi];
                            if (row == null || string.IsNullOrEmpty(row.Uid)) continue;
                            if (!PassesFilters(row, true)) continue;
                            if (!existingUids.Add(row.Uid)) continue;
                            newEntries.Add(row);
                        }
                    }
                }

                if (newEntries.Count > 0)
                {
                    try
                    {
                        LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta append entries=" + newEntries.Count
                            + " title='" + (currentCategoryTitle ?? "") + "'");
                    }
                    catch { }
                    currentFilteredFiles.AddRange(newEntries);
                    lastFilteredFiles.AddRange(newEntries);

                    var sortState = GetSortState("Files");
                    if (activeContentType != ContentType.History)
                    {
                        GallerySortManager.Instance.SortFiles(currentFilteredFiles, sortState);
                        GallerySortManager.Instance.SortFiles(lastFilteredFiles, sortState);
                        try { GallerySortManager.ApplyHideOldVersionsFilter(currentFilteredFiles); } catch { }
                        try { GallerySortManager.ApplyHideOldVersionsFilter(lastFilteredFiles); } catch { }
                    }

                    changed = true;
                    lastPackageDeltaChangedGrid = true;
                }
            }

            if (skippedForNoCache && !changed && !Gallery.IsSuppressed() && !_cacheRetryPending)
            {
                _cacheRetryPending = true;
                try { StartCoroutine(RetryRefreshAfterNoCacheDelay()); } catch { }
            }

            if (!changed)
            {
                // Nothing actually changed – keep gallery exactly as-is.
                lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime;
                refreshOnNextShow = false;
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta NO_CHANGE title='" + (currentCategoryTitle ?? "")
                        + "' skippedNoCache=" + (skippedForNoCache ? "1" : "0"));
                }
                catch { }
                return false;
            }

            InvalidateGalleryPreHideFileListSnapshot();

            // ── Update grid ───────────────────────────────────────────────────────────────
            recyclingGrid.SetItemCount(currentFilteredFiles.Count);
            try { recyclingGrid.Refresh(); } catch { }

            // ── Restore scroll via UID anchor ─────────────────────────────────────────────
            if (anchorUid != null)
            {
                int newIdx = -1;
                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    if (string.Equals(currentFilteredFiles[i]?.Uid, anchorUid, StringComparison.OrdinalIgnoreCase))
                    { newIdx = i; break; }
                }
                if (newIdx >= 0) recyclingGrid.ScrollToCenterItem(newIdx);
            }

            UpdatePaginationText();
            lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime;
            lastPackageDeltaChangedGrid = true;
            refreshOnNextShow = false;
            if (ShouldSkipHeavyAppearanceTagParallelScan())
            {
                if (!TryRecomputeAppearanceGenderFacetCountsScoped())
                {
                    if (TryApplyAppearanceFacetCountsFromSql())
                        TryMergeLooseVapAppearanceGenderFacetCounts();
                }
                tagsCached = true;
            }
            else
                tagsCached = false;
            userTagsCached = false;
            pathsCached = false;
            try { RefreshSideTabsAfterPackageDelta(); } catch { }

            try
            {
                LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta CHANGED gridAfter="
                    + (currentFilteredFiles != null ? currentFilteredFiles.Count : 0)
                    + " title='" + (currentCategoryTitle ?? "") + "'");
            }
            catch { }
            return true;
        }

        private void LogPackageDeltaSkip(string reason)
        {
            try
            {
                LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta SKIP reason=" + reason
                    + " title='" + (currentCategoryTitle ?? "") + "'");
            }
            catch { }
        }

        /// <summary>Rebuild category/creator side-tab counts after an in-memory package delta.</summary>
        private void RefreshSideTabsAfterPackageDelta()
        {
            if (IsHubMode) return;
            if (!VamOnDemandLoader.IsMainThread()) return;
            try { InvalidateSharedSideMetaIfPackageScanAdvanced(); } catch { }
            categoriesCached = false;
            creatorsCached = false;
            _deferSideTabCountsForceRefresh = true;
            if (!IsVisible && !hasLoadedContent) return;
            if (_packageDeltaSideTabsCoroutine != null) return;
            try { _packageDeltaSideTabsCoroutine = StartCoroutine(CoRefreshSideTabsAfterPackageDelta()); } catch { }
        }

        private IEnumerator CoRefreshSideTabsAfterPackageDelta()
        {
            yield return null;
            _packageDeltaSideTabsCoroutine = null;
            if (IsHubMode) yield break;
            try { CacheCategoryCounts(); } catch { }
            try { CacheCreators(); } catch { }
            if (!IsVisible && !hasLoadedContent) yield break;
            try { UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: false); } catch { }
        }

        /// <summary>Key for <see cref="GalleryFileListSnapshotCache"/> when the full enumeration result is reproducible from panel state.</summary>
        private bool TryBuildFileListSnapshotCacheKey(out string key)
        {
            key = null;
            if (IsHubMode) return false;
            if (IsFilterActive) return false;

            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : null) ?? "";

            try
            {
                var sb = new StringBuilder(640);
                sb.Append((int)activeContentType).Append('\u001E');
                if (activeContentType == ContentType.History)
                    sb.Append((int)galleryHistoryFilterMode).Append('\u001E');
                sb.Append(currentExtension ?? "").Append('\u001E');
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
                sb.Append(GalleryConsolidateCreatorNamesEnabled ? '1' : '0').Append('\u001E');
                sb.Append(GetCreatorFilterForQueries()).Append('\u001E');
                sb.Append(currentPackagePathFilter ?? "").Append('\u001E');
                sb.Append(nameFilterLower ?? "").Append('\u001E');
                sb.Append(VPBConfig.Instance != null ? VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope) : "PathAndName").Append('\u001E');
                sb.Append(title).Append('\u001E');
                sb.Append((int)posePeopleFilter).Append('\u001E');
                sb.Append((int)clothingSubfilter).Append('\u001E');
                sb.Append((int)hairSubfilter).Append('\u001E');
                sb.Append((int)appearanceSubfilter).Append('\u001E');
                sb.Append(currentSceneSourceFilter ?? "").Append('\u001E');
                sb.Append(currentAppearanceSourceFilter ?? "").Append('\u001E');
                sb.Append(currentRatingFilter ?? "").Append('\u001E');
                sb.Append(currentSizeFilter ?? "").Append('\u001E');
                sb.Append(categoryFilter ?? "").Append('\u001E');
                sb.Append(creatorFilter ?? "").Append('\u001E');
                sb.Append(tagFilter ?? "").Append('\u001E');
                sb.Append(isRatingSortToggleEnabled ? '1' : '0').Append('\u001E');
                if (activeTags != null && activeTags.Count > 0)
                {
                    var arr = new List<string>(activeTags);
                    arr.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < arr.Count; i++)
                    {
                        sb.Append(arr[i] ?? "");
                        sb.Append('\u001F');
                    }
                }
                sb.Append('\u001E');
                sb.Append(_userTagAvailFilterMode ? '1' : '0').Append('\u001E');
                if (_userTagAvailFilterMode && activeUserTags != null && activeUserTags.Count > 0)
                {
                    var uarr = new List<string>(activeUserTags);
                    uarr.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < uarr.Count; i++)
                    {
                        sb.Append(uarr[i] ?? "");
                        sb.Append('\u001F');
                    }
                }
                sb.Append('\u001E');
                SortState st = GetSortState("Files");
                sb.Append((int)st.Type).Append('\u001E').Append((int)st.Direction);
                key = sb.ToString();
                return true;
            }
            catch
            {
                key = null;
                return false;
            }
        }

        /// <summary>
        /// When true, SQLite bulk rows need no per-item work on the main thread (filters/ratings/pose are default),
        /// so <see cref="List{T}.AddRange"/> is equivalent to the drain loop.
        /// </summary>
        /// <param name="filtersAppliedOnWorker">Category SQL query already applied clothing/hair/source subfilters.</param>
        private bool RefreshFilesRoutineCanFastAppendSqliteBulkList(bool wantsPoseCountsLocal, bool filtersAppliedOnWorker)
        {
            if (isRatingSortToggleEnabled) return false;
            if (!string.IsNullOrEmpty(currentRatingFilter)) return false;
            if (!string.IsNullOrEmpty(currentSizeFilter)) return false;
            if (!string.IsNullOrEmpty(currentSceneSourceFilter)) return false;
            // nameFilterTerms and activeTags are now handled by SQL
            if (wantsPoseCountsLocal || posePeopleFilter != PosePeopleFilter.All) return false;
            // LoadedOnly/UnloadedOnly is applied in the SQLite query via loadedState.

            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
            string cp = currentPath ?? "";
            if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!string.IsNullOrEmpty(currentAppearanceSourceFilter)) return false;
                if (appearanceSubfilter != 0) return false;
            }

            if (!filtersAppliedOnWorker)
            {
                bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                    || cp.IndexOf("/Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                    || cp.IndexOf("\\Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isClothing && clothingSubfilter != 0) return false;

                bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
                    || cp.IndexOf("/Hair", StringComparison.OrdinalIgnoreCase) >= 0
                    || cp.IndexOf("\\Hair", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isHair && hairSubfilter != 0) return false;

                if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// One step of RefreshFilesRoutine main-thread drain: filters, pose/rating side effects, append to <paramref name="targetFiles"/>.
        /// Returns true when <paramref name="yieldWatch"/> has exceeded <paramref name="maxMsBudget"/> after a successful add (caller should yield).
        /// </summary>
        private bool RefreshFilesRoutineDrainProcessAndShouldYield(
            FileEntry entry,
            List<FileEntry> targetFiles,
            ref System.Diagnostics.Stopwatch yieldWatch,
            long maxMsBudget,
            string localLoadingGroupIdForCancel,
            bool wantsPoseCountsLocal,
            bool skipClothingGalleryFilters)
        {
            if (localLoadingGroupIdForCancel != currentLoadingGroupId) return false;

            bool baseOk = PassesFilters(entry, true, skipClothingGalleryFilters);
            if (!baseOk) return false;

            int pcPose = 1;
            bool needPc = wantsPoseCountsLocal || (posePeopleFilter != PosePeopleFilter.All);
            if (needPc)
            {
                bool isJsonPose = false;
                try { isJsonPose = (entry.Path != null && entry.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)); } catch { isJsonPose = false; }
                if (isJsonPose)
                {
                    int known;
                    if (TryGetKnownPosePeopleCount(entry, out known))
                    {
                        pcPose = known;
                    }
                    else
                    {
                        EnqueuePosePeopleIndex(entry);
                        pcPose = 1;
                    }
                }
                else
                {
                    pcPose = 1;
                }
                if (wantsPoseCountsLocal)
                {
                    if (pcPose >= 2) posePeopleFacetCountDual++;
                    else posePeopleFacetCountSingle++;
                }
                if (posePeopleFilter == PosePeopleFilter.Single && pcPose >= 2) return false;
                if (posePeopleFilter == PosePeopleFilter.Dual && pcPose < 2) return false;
            }

            if (isRatingSortToggleEnabled)
            {
                if (RatingsManager.Instance.GetRating(entry) <= 0) return false;
            }

            targetFiles.Add(entry);
            return yieldWatch.ElapsedMilliseconds > maxMsBudget;
        }

        /// <summary>Map SQLite History rows to <see cref="VarFileEntry"/>.</summary>
        private List<FileEntry> BuildHistoryBulkListFromRows(List<VpbLocalDatabase.Row> idxRows, string localLoadingGroupId, int wantsLoadedStateForIndexMain)
        {
            var bulk = new List<FileEntry>(idxRows != null && idxRows.Count > 0 ? idxRows.Count : 16);
            if (idxRows == null) return bulk;
            for (int ri = 0; ri < idxRows.Count; ri++)
            {
                if (localLoadingGroupId != currentLoadingGroupId) return bulk;

                VpbLocalDatabase.Row r = idxRows[ri];
                string internalPath = r.InternalPath;
                // Launch from index paths; ItemUsageKey is for remove-from-history identity only (legacy keys can break load).

                string varHint = r.VarPath ?? "";
                string listPath = r.ListPath ?? "";
                if (!string.IsNullOrEmpty(r.ItemUsageKey))
                {
                    if (!string.IsNullOrEmpty(varHint))
                    {
                        listPath = string.Equals(internalPath, "meta.json", StringComparison.OrdinalIgnoreCase)
                            ? varHint
                            : varHint + ":/" + internalPath;
                    }
                }
                else if (string.IsNullOrEmpty(listPath))
                {
                    if (!string.IsNullOrEmpty(varHint))
                    {
                        listPath = string.Equals(internalPath, "meta.json", StringComparison.OrdinalIgnoreCase)
                            ? varHint
                            : varHint + ":/" + internalPath;
                    }
                }
                if (string.IsNullOrEmpty(listPath))
                    continue;

                if (wantsLoadedStateForIndexMain != -1)
                {
                    bool loaded = r.PackageIsLoaded;
                    if (!loaded)
                    {
                        string lp = (listPath ?? "").Replace('\\', '/');
                        int sep = lp.IndexOf(":/", StringComparison.Ordinal);
                        string root = (sep >= 0) ? lp.Substring(0, sep) : lp;
                        loaded =
                            root.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) ||
                            root.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                            root.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase);
                    }
                    bool wantsLoaded = wantsLoadedStateForIndexMain == 1;
                    if (wantsLoaded && !loaded) continue;
                    if (!wantsLoaded && loaded) continue;
                }

                DateTime entryTime = DateTime.MinValue;
                if (r.LastWriteTicksOrInvalid != long.MinValue)
                {
                    try { entryTime = DateTime.FromBinary(r.LastWriteTicksOrInvalid); }
                    catch { entryTime = DateTime.MinValue; }
                }
                long entrySize = 0;
                if (r.PackageSizeOrInvalid != long.MinValue)
                    entrySize = r.PackageSizeOrInvalid;

                VarFileEntry vfe = new VarFileEntry(r.PackageUid, internalPath, entryTime, entrySize, listPath, varHint, r.PackageCreationTicksOrInvalid, r.FirstScannedTicksOrInvalid, r.ItemUsageKey);
                bulk.Add(vfe);
            }
            return bulk;
        }

        /// <summary>Re-query History and rebind the grid (no full <see cref="RefreshFiles"/>).</summary>
        public void RefreshHistoryListInPlace(bool keepScroll = true)
        {
            if (Gallery.IsSuppressed()) return;
            if (IsHubMode) return;
            if (activeContentType != ContentType.History)
            {
                RefreshFiles(keepScroll);
                return;
            }
            string snapProbe;
            if (!TryBuildFileListSnapshotCacheKey(out snapProbe))
            {
                RefreshFiles(keepScroll);
                return;
            }

            if (refreshCoroutine != null)
            {
                try { StopCoroutine(refreshCoroutine); } catch { }
                refreshCoroutine = null;
            }
            if (_earlyMetaApplyCoroutine != null)
            {
                try { StopCoroutine(_earlyMetaApplyCoroutine); } catch { }
                _earlyMetaApplyCoroutine = null;
            }
            if (_refreshHistoryLightCo != null)
            {
                try { StopCoroutine(_refreshHistoryLightCo); } catch { }
                _refreshHistoryLightCo = null;
            }

            if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
                CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
            currentLoadingGroupId = Guid.NewGuid().ToString();
            unchecked { _deferredSubPaneSessionId++; }
            System.Threading.Interlocked.Increment(ref galleryFileRefreshSequence);

            _refreshHistoryLightCo = StartCoroutine(RefreshHistoryListInPlaceRoutine(keepScroll));
        }

        public void RefreshHistoryBrowseIfActive(bool keepScroll = true)
        {
            if (activeContentType != ContentType.History) return;
            RefreshHistoryBrowsePreferLight(keepScroll);
        }

        public void RefreshHistoryBrowsePreferLight(bool keepScroll = true)
        {
            if (Gallery.IsSuppressed()) return;
            if (IsHubMode) return;
            if (activeContentType != ContentType.History)
            {
                lastHistoryQueryFailed = false;
                lastHistoryQueryRejectReason = null;
                lastHistoryQueryHadNameFilter = false;
                RefreshFiles(keepScroll);
                return;
            }
            if (hasLoadedContent && recyclingGrid != null && scrollRect != null)
            {
                RefreshHistoryListInPlace(keepScroll);
                return;
            }
            RefreshFiles(keepScroll);
        }

        public void RetryHistoryBrowseQuery()
        {
            if (IsHubMode || activeContentType != ContentType.History) return;
            lastHistoryQueryFailed = false;
            lastHistoryQueryRejectReason = null;
            RefreshHistoryBrowsePreferLight(true);
        }

        /// <summary>Apply History query failure state for footer text (call on main thread after worker).</summary>
        private void SyncHistoryBrowseFailureFlagsFromStats(ContentType contentSnap, VpbLocalDatabase.GalleryCategoryQueryStats stats, string[] nameTermsSnapshot)
        {
            if (contentSnap != ContentType.History) return;
            bool failed = !stats.ExecutedQuery || !string.IsNullOrEmpty(stats.RejectReason);
            lastHistoryQueryFailed = failed;
            lastHistoryQueryRejectReason = failed ? (stats.RejectReason ?? "history_query_failed") : null;
            lastHistoryQueryHadNameFilter = nameTermsSnapshot != null && nameTermsSnapshot.Length > 0;
        }

        private IEnumerator RefreshHistoryListInPlaceRoutine(bool keepScroll)
        {
            yield return null;

            bool useCenterItemRestore = keepScroll && hasLoadedContent;
            int savedCenterItemIndex = (useCenterItemRestore && recyclingGrid != null)
                ? recyclingGrid.GetCenterItemIndex()
                : -1;
            float savedScrollNormalizedPos = useCenterItemRestore
                ? (scrollRect != null ? scrollRect.verticalNormalizedPosition : 1f)
                : _pendingScrollRestore;

            string localId = currentLoadingGroupId;
            var histMode = galleryHistoryFilterMode;
            string[] nameTerms = nameFilterTerms;

            int wantsLoadedStateForIndexMain = -1;
            try
            {
                SortType st = GetSortState("Files").Type;
                if (st == SortType.LoadedOnly) wantsLoadedStateForIndexMain = 1;
                else if (st == SortType.UnloadedOnly) wantsLoadedStateForIndexMain = 0;
            }
            catch { }

            List<FileEntry> bulkResult = null;
            var workerDone = new int[1];
            bool historyQuerySucceeded = false;
            string historyRejectReason = null;
            bool hadNameFilter = nameTerms != null && nameTerms.Length > 0;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (!VpbSqlite3.IsAvailable)
                    {
                        historyRejectReason = "sqlite_unavailable";
                        return;
                    }
                    var idxRows = new List<VpbLocalDatabase.Row>();
                    VpbLocalDatabase.GalleryCategoryQueryStats histStats;
                    if (!VpbLocalDatabase.TryQueryGalleryHistoryRows(histMode, nameTerms, idxRows, out histStats))
                    {
                        historyRejectReason = histStats.RejectReason ?? "history_query_failed";
                        idxRows.Clear();
                    }
                    else
                    {
                        historyQuerySucceeded = true;
                    }
                    bulkResult = BuildHistoryBulkListFromRows(idxRows, localId, wantsLoadedStateForIndexMain);
                }
                catch (Exception ex)
                {
                    historyRejectReason = "exception:" + ex.Message;
                    bulkResult = null;
                }
                finally
                {
                    workerDone[0] = 1;
                }
            });

            while (workerDone[0] == 0)
                yield return null;

            if (localId != currentLoadingGroupId)
            {
                _refreshHistoryLightCo = null;
                yield break;
            }

            List<FileEntry> bulk = bulkResult ?? new List<FileEntry>();
            var sortState = GetSortState("Files");
            ApplyFilesSortExclusiveFiltersInPlace(bulk, sortState.Type);

            var filtered = new List<FileEntry>(bulk.Count);
            for (int i = 0; i < bulk.Count; i++)
            {
                if (PassesFilters(bulk[i], true, true))
                    filtered.Add(bulk[i]);
            }

            currentFilteredFiles = filtered;
            lastFilteredFiles = new List<FileEntry>(filtered);
            lastHistoryQueryHadNameFilter = hadNameFilter;
            lastHistoryQueryFailed = !historyQuerySucceeded;
            lastHistoryQueryRejectReason = lastHistoryQueryFailed ? (historyRejectReason ?? "history_query_failed") : null;

            string snapKey;
            if (TryBuildFileListSnapshotCacheKey(out snapKey))
                GalleryFileListSnapshotCache.Put(snapKey, filtered);

            if (recyclingGrid != null && contentGO != null)
            {
                recyclingGrid.scrollRect = scrollRect;
                recyclingGrid.content = contentGO.GetComponent<RectTransform>();
                recyclingGrid.onCreateItem = () => CreateNewFileButtonGO();
                recyclingGrid.onBindItem = (go, index) =>
                {
                    if (index >= 0 && index < currentFilteredFiles.Count)
                    {
                        int centerIdx = recyclingGrid != null ? recyclingGrid.CachedCenterItemIndex : 0;
                        int dist = Mathf.Abs(index - centerIdx);
                        _nextThumbPriority = Mathf.Min(90, dist * 3);
                        BindFileButton(go, currentFilteredFiles[index]);
                    }
                };

                if (layoutMode == GalleryLayoutMode.List)
                {
                    recyclingGrid.fixedColumns = 1;
                    recyclingGrid.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh: true);
                    recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh: true);
                }
                else
                {
                    recyclingGrid.SetGridConfig(100f, GetGridCellConfigHeight(), EffectiveGridSpacingX(), EffectiveGridSpacingY(), GridColumnCount, deferRefresh: true);
                    recyclingGrid.SetAdaptiveConfig(true, 200f, GridColumnCount, false, deferRefresh: true);
                }

                if (savedCenterItemIndex >= 0)
                    recyclingGrid.SetItemCountAtItem(currentFilteredFiles.Count, savedCenterItemIndex);
                else
                    recyclingGrid.SetItemCountAtScroll(currentFilteredFiles.Count, savedScrollNormalizedPos);
            }

            UpdatePaginationText();
            UpdateLayout();

            if (scrollRect != null && recyclingGrid != null)
            {
                if (savedCenterItemIndex >= 0)
                    recyclingGrid.ScrollToCenterItem(savedCenterItemIndex);
                else
                {
                    scrollRect.verticalNormalizedPosition = savedScrollNormalizedPos;
                    recyclingGrid.Refresh();
                }
            }

            try
            {
                StartCoroutine(PostFilesListHideAndSortFollowupRoutine(currentLoadingGroupId, keepScroll, false, savedScrollNormalizedPos));
            }
            catch { }

            try { UpdateSelectionContextMenu(); } catch { }

            _refreshHistoryLightCo = null;
        }

        /// <summary>Apply creator/category side-tab caches built during refresh (worker or shared snapshot). Does not run unless loading session still matches.</summary>
        private void ApplyEarlyMetaRefreshResults(
            string metaBuildGroupId,
            bool earlyBuildCreators,
            bool earlyBuildCats,
            string sideMetaCacheKey,
            bool allowStoreSharedSideMeta,
            List<CreatorCacheEntry> earlyNewCreators,
            Dictionary<string, int> earlyNewCatCounts)
        {
            if (metaBuildGroupId != currentLoadingGroupId) return;
            try
            {
                if (earlyBuildCreators)
                {
                    cachedCreators = earlyNewCreators ?? new List<CreatorCacheEntry>();
                    creatorsCached = true;
                    unchecked { creatorSideTabDataRevision++; }
                }
                if (earlyBuildCats)
                {
                    if (earlyNewCatCounts != null)
                    {
                        categoryCounts.Clear();
                        foreach (var kv in earlyNewCatCounts) categoryCounts[kv.Key] = kv.Value;
                    }
                    categoriesCached = true;
                    unchecked { categorySideTabDataRevision++; }
                    StampSideTabCountsForCurrentScan();
                }
                if (allowStoreSharedSideMeta && sideMetaCacheKey != null && earlyBuildCreators && earlyBuildCats
                    && earlyNewCreators != null && earlyNewCatCounts != null)
                    StoreSharedSideMetaIfRoom(sideMetaCacheKey, earlyNewCreators, earlyNewCatCounts);
            }
            catch { }
        }

        private IEnumerator DestroyLegacyActiveButtonsBudgetCo(List<GameObject> pending)
        {
            const int perFrame = 48;
            int i = 0;
            int n = pending != null ? pending.Count : 0;
            while (i < n)
            {
                int end = Mathf.Min(i + perFrame, n);
                for (; i < end; i++)
                {
                    GameObject go = pending[i];
                    if (go != null) Destroy(go);
                }
                yield return null;
            }
        }

        private IEnumerator RefreshFilesRoutine(bool keepScroll, bool scrollToBottom)
        {
            if (!hasLoadedContent)
            {
                try { VpbProgressService.BeginGalleryUiLoad(); } catch { }
            }

            int navSessionForThisRun = _boundCategoryNavSessionForCurrentRefresh;
            var swDeep = LogGalleryRefreshDeepTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
            long syncCpuBeforeFirstYieldMs = 0;
            long stallUntilRoutineResumeMs = -1;
            long deepAfterFirstYieldMs = -1;
            long deepAfterDrainMs = -1;
            long deepAfterSysFilesMs = -1;
            long deepAfterSortMs = -1;
            long deepAfterGridBindMs = -1;
            long deepGbListCopyMs = -1;
            long deepGbConfigMs = -1;
            long deepGbSetItemMs = -1;
            long deepGbCreateTotalMs = 0;
            int deepGbCreateCount = 0;
            long deepGbBindTotalMs = 0;
            int deepGbBindCount = 0;
            long deepAfterEarlyMetaWaitMs = -1;
            long deepUpdateLayoutMs = -1;
            int deepFilesCountAfterDrain = 0;
            int deepSysFilesAdded = 0;
            bool deepSysCacheHit = false;

            if (swDeep != null) syncCpuBeforeFirstYieldMs = swDeep.ElapsedMilliseconds;

            float packageWaitStart = Time.realtimeSinceStartup;
            while (!Gallery.IsSuppressed() && !IsHubMode)
            {
                long scanBin = 0;
                try { scanBin = FileManager.lastPackageRefreshTime.ToBinary(); } catch { }
                if (scanBin != 0 && scanBin != DateTime.MinValue.ToBinary()) break;

                bool inventoryBusy = false;
                try { inventoryBusy = FileManager.IsScanning; } catch { inventoryBusy = false; }
                if (!inventoryBusy && VamHookPlugin.IsFileManagerInited) break;

                if (Time.realtimeSinceStartup - packageWaitStart >= 120f) break;
                yield return null;
            }
            if (LogGalleryRefreshDeepTiming)
            {
                float waitedMs = (Time.realtimeSinceStartup - packageWaitStart) * 1000f;
                if (waitedMs >= 50f)
                {
                    try
                    {
                        LogUtil.Log("[VPB.Gallery.DeepTiming] waited for package inventory ms=" + waitedMs.ToString("0")
                            + " scanning=" + (FileManager.IsScanning ? "1" : "0")
                            + " refreshTime=" + FileManager.lastPackageRefreshTime.ToString("o"));
                    }
                    catch { }
                }
            }

            yield return null; // Allow UI to render first — next MoveNext may wait while same click handler runs UpdateTabs() etc.
            LogGalleryCategoryTypeNavPhase("RefreshFilesRoutine_after_first_yield");
            if (swDeep != null)
            {
                deepAfterFirstYieldMs = swDeep.ElapsedMilliseconds;
                stallUntilRoutineResumeMs = deepAfterFirstYieldMs - syncCpuBeforeFirstYieldMs;
            }

            // Reset pose facet counts for this refresh
            posePeopleFacetCountSingle = 0;
            posePeopleFacetCountDual = 0;
            _refreshSqliteBulkIncludedUserTagGridFilter = false;
            System.Threading.Interlocked.Exchange(ref _refreshWorkerFallbackUserTagPrefilterFlag, 0);

            // currentLoadingGroupId was already rotated synchronously in RefreshFiles()
            // before this coroutine started; no need to rotate again here.

            // Determine scroll target before clearing the grid.
            // Auto-refresh (keepScroll=true, content already loaded): capture the center item index now,
            //   before SetItemCount(0) zeroes the content height and the ScrollRect clamps to top.
            //   Using an item index (not a normalized float) keeps the same row visible even when the
            //   column count or content height changes (e.g. side panel open/close).
            // Category change or first load: use _pendingScrollRestore set by Show()
            //   (either a persisted position from the cache, or 1f for top).
            bool useCenterItemRestore = keepScroll && hasLoadedContent;
            int savedCenterItemIndex = (useCenterItemRestore && recyclingGrid != null)
                ? recyclingGrid.GetCenterItemIndex()
                : -1;
            // Preserve normalized scroll only when we have already loaded content.
            // Early refresh paths should use the pending restore target (top by default).
            float savedScrollNormalizedPos = useCenterItemRestore
                ? (scrollRect != null ? scrollRect.verticalNormalizedPosition : 1f)
                : _pendingScrollRestore;

            // Configure grid immediately so it has correct dimensions even while loading
            if (contentGO != null)
            {
                if (recyclingGrid == null) recyclingGrid = contentGO.GetComponent<RecyclingGridView>();
                if (recyclingGrid != null)
                {
                    if (layoutMode == GalleryLayoutMode.List)
                    {
                        recyclingGrid.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh: true);
                        recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh: true);
                    }
                    else
                    {
                        // Grid mode
                        recyclingGrid.SetGridConfig(100f, GetGridCellConfigHeight(), EffectiveGridSpacingX(), EffectiveGridSpacingY(), GridColumnCount, deferRefresh: true);
                        recyclingGrid.SetAdaptiveConfig(true, 200f, GridColumnCount, false, deferRefresh: true);
                    }
                    recyclingGrid.SetItemCount(0); // Clear initially — single Refresh after deferred config
                }
            }
            
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            string[] nameTerms = nameFilterTerms;
            bool hasNameFilter = nameTerms != null && nameTerms.Length > 0;

            int tagScanRefreshSeq = GalleryFileRefreshSequence;
            TagParallelWaiter tagParallelWaiterForThisRun = null;

            string titleForCounts = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            bool isPoseCategory = titleForCounts.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!tagsCached && DeferredSubPaneNeedsTagCountCachePass() && !ShouldSkipHeavyAppearanceTagParallelScan())
            {
                string tagSnapKeyProbe;
                bool haveSnap = TryBuildTagCountCacheKey(out tagSnapKeyProbe) && GalleryTagCountSnapshotCache.HasSnapshot(tagSnapKeyProbe);
                TagCountParallelInputs tagParallelInputs;
                if (!haveSnap && TryBuildTagCountParallelInputs(out tagParallelInputs))
                {
                    tagParallelWaiterForThisRun = new TagParallelWaiter();
                    tagParallelWaiter = tagParallelWaiterForThisRun;
                    GalleryTagCountBackgroundScan.QueueParallelScan(this, tagScanRefreshSeq, tagParallelInputs, tagParallelWaiterForThisRun);
                }
            }

            // Note: Show() calls RefreshFiles() before UpdateTabs(), so the split sub-pane may not be active yet.
            // We still want counters to populate as soon as loading finishes.
            bool wantsPoseCounts = isPoseCategory;

            // Reset progressive index queue when browsing Pose
            if (isPoseCategory)
            {
                lock (posePeopleIndexLock)
                {
                    posePeopleIndexQueue.Clear();
                    posePeopleIndexQueued.Clear();
                }
                posePeopleIndexGroupId = currentLoadingGroupId;
            }
            else
            {
                // Cancel any outstanding pose indexing work when leaving Pose category.
                posePeopleIndexGroupId = "";
                if (posePeopleIndexCoroutine != null)
                {
                    try { StopCoroutine(posePeopleIndexCoroutine); } catch { }
                    posePeopleIndexCoroutine = null;
                }
                lock (posePeopleIndexLock)
                {
                    posePeopleIndexQueue.Clear();
                    posePeopleIndexQueued.Clear();
                }
            }
            
            // Time-based yielding: first full load uses a larger per-frame budget so the list finishes in fewer frames (still yields to avoid long stalls).
            bool isColdGalleryContentLoad = !hasLoadedContent;
            var yieldWatch = new System.Diagnostics.Stopwatch();
            // Prior default (22ms warm) makes refresh frame-rate bound on large libraries (can take 20s+).
            // Bias toward faster refresh completion; UI still yields, but in larger chunks.
            long maxMsPerFrame = isColdGalleryContentLoad ? 120 : 120;

            yieldWatch.Start();

            int[] skippedForNoCache = { 0 };
            // Sample of package UIDs missing cache, for diagnosing 3s retry loops.
            // Single worker thread writes; main thread reads after drain completes.
            string[] skippedForNoCacheSample = new string[3];
            int skippedForNoCacheSampleCount = 0;

            PushCreatorFilterSqlModeForDatabase();

            string fileListSnapKey;
            bool canFileListCache = TryBuildFileListSnapshotCacheKey(out fileListSnapKey);
            bool bypassFileListCache = string.Equals(_refreshFilesDebugSource, "sql_index_updated", StringComparison.Ordinal);
            List<FileEntry> snapList = null;
            bool fileListFromCache = false;
            bool fileListFromSibling = false;
            if (!bypassFileListCache && canFileListCache && Gallery.singleton != null)
            {
                var panels = Gallery.singleton.Panels;
                for (int pi = 0; pi < panels.Count; pi++)
                {
                    GalleryPanel o = panels[pi];
                    if (o == null || o == this || !o.HasLoadedContent) continue;
                    string ok;
                    if (!o.TryBuildFileListSnapshotCacheKey(out ok) || !string.Equals(ok, fileListSnapKey, StringComparison.Ordinal)) continue;
                    if (o.currentFilteredFiles == null || o.currentFilteredFiles.Count == 0) continue;
                    snapList = new List<FileEntry>(o.currentFilteredFiles);
                    fileListFromCache = true;
                    fileListFromSibling = true;
                    break;
                }
            }
            if (!bypassFileListCache && !fileListFromCache && canFileListCache)
                fileListFromCache = GalleryFileListSnapshotCache.TryGet(fileListSnapKey, out snapList);
            List<FileEntry> files = (fileListFromCache && snapList != null) ? snapList : new List<FileEntry>();
            if (fileListFromSibling && canFileListCache && fileListSnapKey != null && files.Count > 0)
                GalleryFileListSnapshotCache.Put(fileListSnapKey, files);

            SortState fileListSortSnapForWorker = null;
            int[] sqliteBulkSortedOnWorkerFlag = null;
            int[] sysLooseFilesAddedCount = null;
            long refreshDrainWallMs = 0;
            if (!fileListFromCache)
            {
                fileListSortSnapForWorker = GetSortState("Files").Clone();
                sqliteBulkSortedOnWorkerFlag = new int[1];
                sysLooseFilesAddedCount = new int[1];
            }

            // Start creator/category metadata build immediately so it overlaps package scanning (same work as the block after the grid, previously sequential).
            string metaBuildGroupId = currentLoadingGroupId;
            bool earlyBuildCreators = !creatorsCached;
            bool earlyBuildCats = !categoriesCached;
            bool earlyMetaNeeded = earlyBuildCreators || earlyBuildCats;
            List<CreatorCacheEntry> earlyNewCreators = null;
            Dictionary<string, int> earlyNewCatCounts = null;
            bool earlyMetaBuildDone = !earlyMetaNeeded;
            string sideMetaCacheKey = null;
            bool skipEarlyMetaThread = false;

            if (earlyMetaNeeded && earlyBuildCreators && earlyBuildCats)
            {
                InvalidateSharedSideMetaIfPackageScanAdvanced();
                sideMetaCacheKey = BuildSharedSideMetaCacheKey(
                    GetCreatorFilterForQueries(), currentExtension, currentPath, currentPaths, categories, currentCategoryTitle);
                List<CreatorCacheEntry> sharedCreators;
                Dictionary<string, int> sharedCounts;
                if (TryGetSharedSideMeta(sideMetaCacheKey, out sharedCreators, out sharedCounts))
                {
                    earlyNewCreators = sharedCreators;
                    earlyNewCatCounts = sharedCounts;
                    earlyMetaBuildDone = true;
                    skipEarlyMetaThread = true;
                }
            }

            if (earlyMetaNeeded && !skipEarlyMetaThread)
            {
                string _bCreator = GetCreatorFilterForQueries();
                bool _bCreatorCaseInsensitive = GalleryConsolidateCreatorNamesEnabled;
                string _bExtension = currentExtension;
                List<string> _bPaths = currentPaths != null ? new List<string>(currentPaths) : null;
                string _bPath = currentPath;
                string _bPackagePathFilter = currentPackagePathFilter;
                string _bCategoryTitle = currentCategoryTitle;
                var _bCategories = categories != null ? new List<Gallery.Category>(categories) : null;
                bool _buildCreators = earlyBuildCreators;
                bool _buildCats = earlyBuildCats;

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (_buildCreators)
                        {
                            var counts = new Dictionary<string, int>();
                            // Package-only category: count packages by creator (not internal file entries)
                            if (string.Equals(_bExtension, "varpkg", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!VpbLocalDatabase.TryReadVarPackageCreatorCounts(counts, _bPackagePathFilter) && FileManager.PackagesByUid != null)
                                {
                                    foreach (var pkg in FileManager.PackagesByUid.Values)
                                    {
                                        if (pkg == null) continue;
                                        if (string.IsNullOrEmpty(pkg.Creator)) continue;
                                        if (!string.IsNullOrEmpty(_bPackagePathFilter) &&
                                            !GalleryPathFilterMatchesRawPath(pkg.Path, _bPackagePathFilter))
                                            continue;
                                        int cur;
                                        counts.TryGetValue(pkg.Creator, out cur);
                                        counts[pkg.Creator] = cur + 1;
                                    }
                                }
                            }
                            else if (!VpbLocalDatabase.TryReadCreatorFileCounts(counts, _bExtension, _bPaths, _bPath, null, _bCategoryTitle, _bPackagePathFilter))
                            {
                                string[] exts2 = string.IsNullOrEmpty(_bExtension) ? new string[0] : _bExtension.Split('|');
                                var tExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var e in exts2) if (!string.IsNullOrEmpty(e)) tExts.Add(e.Trim());

                                if (FileManager.PackagesByUid != null)
                                {
                                    foreach (var pkg in FileManager.PackagesByUid.Values)
                                    {
                                        if (string.IsNullOrEmpty(pkg.Creator)) continue;
                                        if (pkg.FileEntries == null) continue;
                                        if (!string.IsNullOrEmpty(_bPackagePathFilter) &&
                                            !GalleryPathFilterMatchesRawPath(pkg.Path, _bPackagePathFilter))
                                            continue;
                                        int cnt = pkg.FileEntries.Count;
                                        for (int i = 0; i < cnt; i++)
                                        {
                                            string ip = pkg.FileEntries[i].InternalPath;
                                            int dot = ip.LastIndexOf('.');
                                            if (dot < 0 || dot == ip.Length - 1) continue;
                                            if (!tExts.Contains(ip.Substring(dot + 1))) continue;
                                            bool match = false;
                                            if (_bPaths != null && _bPaths.Count > 0)
                                            { for (int k = 0; k < _bPaths.Count; k++) if (GalleryInternalPathStartsWithPrefix(ip, _bPaths[k])) { match = true; break; } }
                                            else if (!string.IsNullOrEmpty(_bPath))
                                                match = GalleryInternalPathStartsWithPrefix(ip, _bPath);
                                            else match = true;
                                            if (match) { int cur; counts.TryGetValue(pkg.Creator, out cur); counts[pkg.Creator] = cur + 1; }
                                        }
                                    }
                                }
                            }
                            earlyNewCreators = counts.Select(kv => new CreatorCacheEntry { Name = kv.Key, Count = kv.Value })
                                                     .OrderBy(c => c.Name).ToList();
                        }

                        if (_buildCats && _bCategories != null)
                        {
                            var catCounts2 = new Dictionary<string, int>();
                            foreach (var c in _bCategories)
                                catCounts2[c.name] = 0;

                            if (!VpbLocalDatabase.TryReadCategoryMemberCounts(catCounts2, _bCreator, null, _bPackagePathFilter))
                            {
                                var extToCats2 = new Dictionary<string, List<Gallery.Category>>(StringComparer.OrdinalIgnoreCase);
                                foreach (var c in _bCategories)
                                {
                                    if (string.IsNullOrEmpty(c.extension)) continue;
                                    foreach (string ce in c.extension.Split('|'))
                                    {
                                        if (string.IsNullOrEmpty(ce)) continue;
                                        string et = ce.Trim();
                                        if (!extToCats2.ContainsKey(et)) extToCats2[et] = new List<Gallery.Category>();
                                        extToCats2[et].Add(c);
                                    }
                                }
                                if (FileManager.PackagesByUid != null)
                                {
                                    HashSet<string> earlyCreatorFilterSet = null;
                                    if (!string.IsNullOrEmpty(_bCreator))
                                    {
                                        earlyCreatorFilterSet = new HashSet<string>(
                                            _bCreatorCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                                        AddCreatorFilterPartsToSet(_bCreator, earlyCreatorFilterSet);
                                    }
                                    foreach (var pkg in FileManager.PackagesByUid.Values)
                                    {
                                        if (earlyCreatorFilterSet != null && earlyCreatorFilterSet.Count > 0)
                                        {
                                            if (string.IsNullOrEmpty(pkg.Creator) || !earlyCreatorFilterSet.Contains(pkg.Creator)) continue;
                                        }
                                        if (!string.IsNullOrEmpty(_bPackagePathFilter) &&
                                            !GalleryPathFilterMatchesRawPath(pkg.Path, _bPackagePathFilter))
                                            continue;
                                        if (pkg.FileEntries == null) continue;
                                        int cnt = pkg.FileEntries.Count;
                                        for (int i = 0; i < cnt; i++)
                                        {
                                            string ip = pkg.FileEntries[i].InternalPath;
                                            int dot = ip.LastIndexOf('.');
                                            if (dot < 0 || dot == ip.Length - 1) continue;
                                            List<Gallery.Category> cands2;
                                            if (extToCats2.TryGetValue(ip.Substring(dot + 1), out cands2))
                                            {
                                                for (int j = 0; j < cands2.Count; j++)
                                                {
                                                    var cat2 = cands2[j];
                                                    bool pm = false;
                                                    if (cat2.paths != null && cat2.paths.Count > 0)
                                                    { for (int k = 0; k < cat2.paths.Count; k++) if (GalleryInternalPathStartsWithPrefix(ip, cat2.paths[k])) { pm = true; break; } }
                                                    else if (!string.IsNullOrEmpty(cat2.path)) pm = GalleryInternalPathStartsWithPrefix(ip, cat2.path);
                                                    else pm = true;
                                                    if (pm) { catCounts2[cat2.name]++; break; }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            AddLocalCustomScriptsCountToCategory(catCounts2, _bPackagePathFilter);
                            earlyNewCatCounts = catCounts2;
                        }
                    }
                    catch { }
                    finally { earlyMetaBuildDone = true; }
                });
            }

            if (!fileListFromCache)
            {
            string titleForAppearanceCaches = !string.IsNullOrEmpty(currentCategoryTitle)
                ? currentCategoryTitle
                : ((titleText != null ? titleText.text : "") ?? "");
            if (titleForAppearanceCaches.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ClearAppearanceGenderRefreshCaches();
                EnsureAppearanceGenderRefreshCaches(titleForAppearanceCaches);
            }

            if (FileManager.PackagesByUid != null)
            {
                string localLoadingGroupId = currentLoadingGroupId;

                Queue<FileEntry> candidateQueue = new Queue<FileEntry>();
                object candidateQueueLock = new object();
                int workerDoneFlag = 0;
                List<FileEntry> sqliteBulkList = null;

                string snapKeyProbeMain;
                bool canFileListSnapKeyMain = TryBuildFileListSnapshotCacheKey(out snapKeyProbeMain);
                string titleForIndexMain = !string.IsNullOrEmpty(currentCategoryTitle) ? currentCategoryTitle : ((titleText != null ? titleText.text : "") ?? "");
                string extForIndexMain = currentExtension ?? "";
                string creatorForIndexMain = GetCreatorFilterForQueries();
                bool creatorFilterCiSnap = GalleryConsolidateCreatorNamesEnabled;
                string packagePathFilterForIndexMain = currentPackagePathFilter ?? "";
                HashSet<string> creatorFilterSetForWorker = null;
                if (!string.IsNullOrEmpty(creatorForIndexMain))
                {
                    creatorFilterSetForWorker = new HashSet<string>(
                        creatorFilterCiSnap ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                    AddCreatorFilterPartsToSet(creatorForIndexMain, creatorFilterSetForWorker);
                }
                int wantsLoadedStateForIndexMain = -1;
                try
                {
                    SortType st = GetSortState("Files").Type;
                    if (st == SortType.LoadedOnly) wantsLoadedStateForIndexMain = 1;
                    else if (st == SortType.UnloadedOnly) wantsLoadedStateForIndexMain = 0;
                }
                catch { }
                ContentType activeContentSnap = activeContentType;
                GalleryHistoryFilterMode histFilterSnap = galleryHistoryFilterMode;
                ClothingSubfilter sqliteWorkerClothingSub = clothingSubfilter;
                string pathForIndexMain = currentPath ?? "";
                string workerPathSnap = pathForIndexMain;
                List<string> workerPathsSnap = currentPaths != null ? new List<string>(currentPaths) : null;
                bool appearanceWorkerLocalOnly = titleForIndexMain.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                    && (string.Equals(currentAppearanceSourceFilter, "local", StringComparison.OrdinalIgnoreCase)
                        || ResolveEffectiveSourceFilterMode(true, pathForIndexMain) == 1);
                bool appearanceWorkerSkipPathMatch = titleForIndexMain.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                    && !appearanceWorkerLocalOnly;
                List<string> pathInclusionsForSql = null;
                if (appearanceWorkerLocalOnly && !string.IsNullOrEmpty(pathForIndexMain))
                {
                    pathInclusionsForSql = new List<string>();
                    pathInclusionsForSql.Add(pathForIndexMain.Replace('\\', '/').TrimEnd('/'));
                }
                bool sqliteDrainSkipClothingGateOnMain = (titleForIndexMain.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
                    || pathForIndexMain.IndexOf("/Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                    || pathForIndexMain.IndexOf("\\Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
                HairSubfilter sqliteWorkerHairSub = hairSubfilter;
                bool sqliteDrainApplyHairGateOnMain = (titleForIndexMain.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
                    || pathForIndexMain.IndexOf("/Hair", StringComparison.OrdinalIgnoreCase) >= 0
                    || pathForIndexMain.IndexOf("\\Hair", StringComparison.OrdinalIgnoreCase) >= 0;

                VpbLocalDatabase.GalleryCategoryQueryStats catQueryStats = new VpbLocalDatabase.GalleryCategoryQueryStats();

                bool userTagGridFilterModeSnap = _userTagAvailFilterMode;
                HashSet<string> userTagNamesForGridSqlSnap = null;
                if (userTagGridFilterModeSnap && activeUserTags != null && activeUserTags.Count > 0)
                    userTagNamesForGridSqlSnap = new HashSet<string>(activeUserTags, StringComparer.OrdinalIgnoreCase);
                int[] refreshDrainUtSqlFilterApplied = { 0 };
                int[] refreshWorkerUsedSqliteFlag = { 0 };

                ThreadPool.QueueUserWorkItem((state) =>
                {
                    VpbLocalDatabase.GalleryCreatorFilterCaseInsensitive = creatorFilterCiSnap;
                    var swWorker = System.Diagnostics.Stopwatch.StartNew();
                    bool useSqliteIndex = false;
                    try
                    {
                        List<VpbLocalDatabase.Row> idxRows = new List<VpbLocalDatabase.Row>();
                        List<string> pathExclusions = null;
                        // SQLite index usage must not depend on snapshot-cache key availability.
                        // Snapshot cache is an optimization; SQLite query is primary fast path.
                        if (VpbSqlite3.IsAvailable
                            && activeContentSnap == ContentType.History)
                        {
                            bool hr = VpbLocalDatabase.TryQueryGalleryHistoryRows(
                                histFilterSnap,
                                nameTerms,
                                idxRows,
                                out catQueryStats);
                            if (!hr)
                                idxRows.Clear();
                            useSqliteIndex = true;
                        }
                        else if (VpbSqlite3.IsAvailable
                            && activeContentSnap == ContentType.Category)
                        {
                            // Pseudo-extension category: package-level listing. Not indexed in SQLite; force non-SQL path.
                            if (string.Equals(extForIndexMain, "varpkg", StringComparison.OrdinalIgnoreCase))
                            {
                                useSqliteIndex = false;
                            }
                            else
                            {
                            if (!Gallery.IsEverythingCategoryName(titleForIndexMain))
                            {
                            if (workerPathsSnap != null && workerPathsSnap.Count > 0)
                            {
                                for (int i = 0; i < workerPathsSnap.Count; i++)
                                {
                                    if (string.Equals(workerPathsSnap[i], "Saves/Person", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (pathExclusions == null) pathExclusions = new List<string>();
                                        pathExclusions.Add("Saves/Person/appearance");
                                    }
                                }
                            }
                            else if (string.Equals(workerPathSnap, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                            {
                                pathExclusions = new List<string> { "Saves/Person/appearance" };
                            }
                            }

                            useSqliteIndex = VpbLocalDatabase.TryQueryGalleryCategoryRows(
                                titleForIndexMain,
                                extForIndexMain,
                                creatorForIndexMain,
                                idxRows,
                                out catQueryStats,
                                sqliteWorkerClothingSub,
                                loadedState: wantsLoadedStateForIndexMain,
                                nameTerms: nameTerms,
                                pathExclusions: pathExclusions,
                                pathInclusions: pathInclusionsForSql,
                                activeTags: activeTags,
                                activeUserTags: userTagNamesForGridSqlSnap,
                                sortState: fileListSortSnapForWorker);
                            }
                        }
                        else
                        {
                            useSqliteIndex = false;
                            if (!VpbSqlite3.IsAvailable)
                                catQueryStats.RejectReason = "gate:sqlite_unavailable";
                            else if (activeContentSnap == ContentType.History)
                                catQueryStats.RejectReason = "gate:history_not_sqlite_indexable";
                            else if (activeContentSnap != ContentType.Category)
                                catQueryStats.RejectReason = "gate:not_category_content";
                            else
                                catQueryStats.RejectReason = "gate:category_not_sqlite_indexable";
                        }

                        if (useSqliteIndex)
                            System.Threading.Interlocked.Exchange(ref refreshWorkerUsedSqliteFlag[0], 1);

                        if (useSqliteIndex)
                        {
                            var swBulk = System.Diagnostics.Stopwatch.StartNew();
                            List<FileEntry> bulk;
                            if (activeContentSnap == ContentType.History)
                            {
                                bulk = BuildHistoryBulkListFromRows(idxRows, localLoadingGroupId, wantsLoadedStateForIndexMain);
                            }
                            else
                            {
                                bulk = new List<FileEntry>(idxRows.Count > 0 ? idxRows.Count : 16);
                                for (int ri = 0; ri < idxRows.Count; ri++)
                                {
                                    if (localLoadingGroupId != currentLoadingGroupId) return;

                                    VpbLocalDatabase.Row r = idxRows[ri];
                                    string internalPath = r.InternalPath;

                                    if (Gallery.IsEverythingCategoryName(titleForIndexMain))
                                    {
                                        int ldP = internalPath.LastIndexOf('.');
                                        if (ldP > 0 && ldP < internalPath.Length - 1
                                            && Gallery.IsEverythingExcludedPreviewExtension(internalPath.Substring(ldP + 1)))
                                            continue;
                                    }

                                    if (!appearanceWorkerSkipPathMatch
                                        && !RefreshWorkerPathMatches(internalPath, workerPathsSnap, workerPathSnap))
                                        continue;

                                    string varHint = r.VarPath ?? "";
                                    string listPath = r.ListPath ?? "";
                                    if (string.IsNullOrEmpty(listPath))
                                    {
                                        if (!string.IsNullOrEmpty(varHint))
                                        {
                                            listPath = string.Equals(internalPath, "meta.json", StringComparison.OrdinalIgnoreCase)
                                                ? varHint
                                                : varHint + ":/" + internalPath;
                                        }
                                    }
                                    if (string.IsNullOrEmpty(listPath))
                                        continue;

                                    if (!string.IsNullOrEmpty(packagePathFilterForIndexMain))
                                    {
                                        string rawPath = !string.IsNullOrEmpty(varHint) ? varHint : listPath;
                                        if (!GalleryPathFilterMatchesRawPath(rawPath, packagePathFilterForIndexMain))
                                            continue;
                                    }

                                    if (wantsLoadedStateForIndexMain != -1)
                                    {
                                        bool loaded = r.PackageIsLoaded;
                                        if (!loaded)
                                        {
                                            string lp = (listPath ?? "").Replace('\\', '/');
                                            int sep = lp.IndexOf(":/", StringComparison.Ordinal);
                                            string root = (sep >= 0) ? lp.Substring(0, sep) : lp;
                                            loaded =
                                                root.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) ||
                                                root.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                                root.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase);
                                        }
                                        bool wantsLoaded = wantsLoadedStateForIndexMain == 1;
                                        if (wantsLoaded && !loaded) continue;
                                        if (!wantsLoaded && loaded) continue;
                                    }

                                    if (sqliteDrainSkipClothingGateOnMain && sqliteWorkerClothingSub != 0)
                                    {
                                        if ((r.ClothingAttrPacked & VpbLocalDatabase.ClothingAttrPresentFlag) != 0)
                                        {
                                            if (!VpbLocalDatabase.ClothingPackedAttrMatchesSubfilter(r.ClothingAttrPacked, sqliteWorkerClothingSub))
                                                continue;
                                        }
                                        else
                                        {
                                            if (!PassesClothingGalleryFiltersForPath(listPath, sqliteWorkerClothingSub, true))
                                                continue;
                                        }
                                    }

                                    if (sqliteDrainApplyHairGateOnMain)
                                    {
                                        // Hair is not yet narrowed at SQL level; apply same path/subfilter gate
                                        // here so default grid does not show duplicate base + preset pairs.
                                        if (!PassesHairGalleryFiltersForPath(listPath, sqliteWorkerHairSub, true))
                                            continue;
                                    }

                                    DateTime entryTime = DateTime.MinValue;
                                    if (r.LastWriteTicksOrInvalid != long.MinValue)
                                    {
                                        try { entryTime = DateTime.FromBinary(r.LastWriteTicksOrInvalid); }
                                        catch { entryTime = DateTime.MinValue; }
                                    }
                                    long entrySize = 0;
                                    if (r.PackageSizeOrInvalid != long.MinValue)
                                        entrySize = r.PackageSizeOrInvalid;

                                    VarFileEntry vfe = new VarFileEntry(r.PackageUid, internalPath, entryTime, entrySize, listPath, varHint, r.PackageCreationTicksOrInvalid, r.FirstScannedTicksOrInvalid, null);
                                    bulk.Add(vfe);
                                }
                            }

                            if (activeContentSnap == ContentType.History)
                            {
                                if (sqliteBulkSortedOnWorkerFlag != null)
                                    sqliteBulkSortedOnWorkerFlag[0] = 1;
                            }
                            else if (bulk.Count >= 2 && fileListSortSnapForWorker != null)
                            {
                                bool alreadySorted = false;
                                switch (fileListSortSnapForWorker.Type)
                                {
                                    case SortType.Date:
                                    case SortType.Size:
                                    case SortType.DateCreated:
                                        alreadySorted = true;
                                        break;
                                }

                                if (alreadySorted || GallerySortManager.TrySortFilesEntryFieldsOnly(bulk, fileListSortSnapForWorker))
                                {
                                    if (sqliteBulkSortedOnWorkerFlag != null)
                                        sqliteBulkSortedOnWorkerFlag[0] = 1;
                                }
                            }

                            swBulk.Stop();

                            sqliteBulkList = bulk;
                            Thread.MemoryBarrier();
                        }
                        else
                        {
                            if (activeContentSnap == ContentType.History)
                            {
                                sqliteBulkList = new List<FileEntry>();
                                Thread.MemoryBarrier();
                            }
                            else
                            {
                                HashSet<string> utCatMemKeyHits = null;
                                if (activeContentSnap == ContentType.Category
                                    && userTagGridFilterModeSnap && userTagNamesForGridSqlSnap != null && userTagNamesForGridSqlSnap.Count > 0
                                    && !string.IsNullOrEmpty(titleForIndexMain) && VpbSqlite3.IsAvailable)
                                {
                                    var utBuilt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    if (VpbLocalDatabase.TryBuildCatMemRowKeysMatchingAllUserTags(titleForIndexMain, userTagNamesForGridSqlSnap, utBuilt))
                                    {
                                        utCatMemKeyHits = utBuilt;
                                        System.Threading.Interlocked.Exchange(ref _refreshWorkerFallbackUserTagPrefilterFlag, 1);
                                    }
                                }

                                bool wantsPackageListOnly = false;
                                try
                                {
                                    wantsPackageListOnly = (extensions != null && extensions.Length == 1 && string.Equals(extensions[0], "varpkg", StringComparison.OrdinalIgnoreCase));
                                }
                                catch { wantsPackageListOnly = false; }

                                if (wantsPackageListOnly)
                                {
                                    bool built = false;
                                    if (VpbSqlite3.IsAvailable)
                                    {
                                        try
                                        {
                                            var rows = new List<VpbLocalDatabase.PackageRow>();
                                            if (VpbLocalDatabase.TryQueryVarPackageRowsForList(
                                                creatorForIndexMain,
                                                packagePathFilterForIndexMain,
                                                wantsLoadedStateForIndexMain,
                                                nameTerms,
                                                fileListSortSnapForWorker,
                                                rows))
                                            {
                                                for (int ri = 0; ri < rows.Count; ri++)
                                                {
                                                    if (localLoadingGroupId != currentLoadingGroupId) return;
                                                    var r = rows[ri];
                                                    DateTime wt = DateTime.MinValue;
                                                    try { if (r.LastWriteTicksOrInvalid != long.MinValue) wt = DateTime.FromBinary(r.LastWriteTicksOrInvalid); } catch { wt = DateTime.MinValue; }
                                                    long sz = r.PackageSizeOrInvalid != long.MinValue ? r.PackageSizeOrInvalid : 0;
                                                    try
                                                    {
                                                        var row = new PackageListEntry(r.PackageUid, r.VarPath ?? "", wt, sz, r.PackageCreationTicksOrInvalid, r.FirstScannedTicksOrInvalid);
                                                        if (PackageHidePrefs.IsExcludedByGalleryHideFilter(row)) continue;
                                                        if (utCatMemKeyHits != null)
                                                        {
                                                            string utk = VpbLocalDatabase.FormatCatMemRowLookupKey(r.PackageUid, "meta.json");
                                                            if (!utCatMemKeyHits.Contains(utk)) continue;
                                                        }
                                                        lock (candidateQueueLock) { candidateQueue.Enqueue(row); }
                                                    }
                                                    catch
                                                    {
                                                        lock (candidateQueueLock) { candidateQueue.Enqueue(new MissingPackageListEntry(r.PackageUid)); }
                                                    }
                                                }
                                                built = true;
                                            }
                                        }
                                        catch { }
                                    }

                                    if (!built && FileManager.PackagesByUid != null)
                                    {
                                        foreach (var pkg in FileManager.PackagesByUid.Values)
                                        {
                                            if (localLoadingGroupId != currentLoadingGroupId) return;
                                            if (pkg == null) continue;
                                            if (creatorFilterSetForWorker != null && creatorFilterSetForWorker.Count > 0)
                                            {
                                                if (string.IsNullOrEmpty(pkg.Creator) || !creatorFilterSetForWorker.Contains(pkg.Creator)) continue;
                                            }
                                            if (!string.IsNullOrEmpty(packagePathFilterForIndexMain) &&
                                                !GalleryPathFilterMatchesRawPath(pkg.Path, packagePathFilterForIndexMain))
                                                continue;
                                            if (hasNameFilter)
                                            {
                                                if (!MatchesPackageByScope(pkg.Uid ?? "", pkg.Path ?? "", null, nameTerms)) continue;
                                            }
                                            if (utCatMemKeyHits != null)
                                            {
                                                string utk = VpbLocalDatabase.FormatCatMemRowLookupKey(pkg.Uid, "meta.json");
                                                if (!utCatMemKeyHits.Contains(utk)) continue;
                                            }
                                            lock (candidateQueueLock) { candidateQueue.Enqueue(new PackageListEntry(pkg)); }
                                        }
                                    }
                                    return;
                                }

                                VarPackage[] refreshPkgSnap;
                                lock (FileManager.packagesLock)
                                {
                                    if (FileManager.PackagesByUid == null || FileManager.PackagesByUid.Count == 0)
                                        refreshPkgSnap = new VarPackage[0];
                                    else
                                    {
                                        var vals = FileManager.PackagesByUid.Values;
                                        refreshPkgSnap = new VarPackage[vals.Count];
                                        int copyIdx = 0;
                                        foreach (VarPackage vp in vals)
                                            refreshPkgSnap[copyIdx++] = vp;
                                    }
                                }

                                int refreshPkgNext = -1;
                                int refreshScanWorkers = Math.Min(
                                    FileManager.GetEffectiveMaxDeepScanWorkers(),
                                    Math.Max(1, refreshPkgSnap.Length / 48));
                                var refreshScanDone = new ManualResetEvent(false);
                                int refreshScanDoneCount = 0;
                                for (int rw = 0; rw < refreshScanWorkers; rw++)
                                {
                                    ThreadPool.QueueUserWorkItem(__ =>
                                    {
                                        try
                                        {
                                            while (true)
                                            {
                                                int pi = System.Threading.Interlocked.Increment(ref refreshPkgNext);
                                                if (pi >= refreshPkgSnap.Length) break;
                                                VarPackage pkg = refreshPkgSnap[pi];
                                                if (localLoadingGroupId != currentLoadingGroupId) return;

                                                if (creatorFilterSetForWorker != null && creatorFilterSetForWorker.Count > 0)
                                                {
                                                    if (string.IsNullOrEmpty(pkg.Creator) || !creatorFilterSetForWorker.Contains(pkg.Creator)) continue;
                                                }
                                                if (!string.IsNullOrEmpty(packagePathFilterForIndexMain) &&
                                                    !GalleryPathFilterMatchesRawPath(pkg.Path, packagePathFilterForIndexMain))
                                                    continue;

                                                List<string> names;
                                                List<long> ticks;
                                                List<long> sizes;
                                                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                                                {
                                                    System.Threading.Interlocked.Increment(ref skippedForNoCache[0]);
                                                    lock (candidateQueueLock)
                                                    {
                                                        if (skippedForNoCacheSampleCount < skippedForNoCacheSample.Length)
                                                        {
                                                            try
                                                            {
                                                                skippedForNoCacheSample[skippedForNoCacheSampleCount++] =
                                                                    pkg != null ? (pkg.Uid ?? pkg.Path ?? "") : "";
                                                            }
                                                            catch { }
                                                        }
                                                    }
                                                    continue;
                                                }

                                                for (int i = 0; i < names.Count; i++)
                                                {
                                                    if (localLoadingGroupId != currentLoadingGroupId) return;
                                                    string internalPath = names[i];
                                                    string checkPath = internalPath;

                                                    bool extMatch = false;
                                                    if (Gallery.IsEverythingCategoryExtension(currentExtension))
                                                    {
                                                        string evExt = Path.GetExtension(checkPath);
                                                        if (!string.IsNullOrEmpty(evExt) && evExt.Length > 1)
                                                        {
                                                            string eNd = evExt.Substring(1);
                                                            extMatch = !Gallery.IsEverythingExcludedPreviewExtension(eNd);
                                                        }
                                                    }
                                                    else if (extensions == null || extensions.Length == 0 || (extensions.Length == 1 && string.IsNullOrEmpty(extensions[0])))
                                                    {
                                                        extMatch = true;
                                                    }
                                                    else
                                                    {
                                                        string entryExt = Path.GetExtension(checkPath);
                                                        if (!string.IsNullOrEmpty(entryExt))
                                                        {
                                                            entryExt = entryExt.Substring(1);
                                                            for (int e = 0; e < extensions.Length; e++)
                                                            {
                                                                string ext = extensions[e];
                                                                if (string.Equals(entryExt, ext, StringComparison.OrdinalIgnoreCase))
                                                                {
                                                                    extMatch = true;
                                                                    break;
                                                                }
                                                            }
                                                        }
                                                    }
                                                    if (!extMatch) continue;

                                                    if (!appearanceWorkerSkipPathMatch
                                                        && !RefreshWorkerPathMatches(checkPath, workerPathsSnap, workerPathSnap))
                                                        continue;

                                                    if (hasNameFilter)
                                                    {
                                                        if (!MatchesPackageByScope(pkg != null ? pkg.Uid : "", pkg != null ? pkg.Path : "", internalPath, nameTerms)) continue;
                                                    }

                                                    if (utCatMemKeyHits != null)
                                                    {
                                                        string utk = VpbLocalDatabase.FormatCatMemRowLookupKey(pkg != null ? pkg.Uid : "", internalPath);
                                                        if (!utCatMemKeyHits.Contains(utk)) continue;
                                                    }

                                                    DateTime entryTime = pkg != null ? pkg.LastWriteTime : DateTime.MinValue;
                                                    long entrySize = pkg != null ? pkg.Size : 0;
                                                    lock (candidateQueueLock)
                                                    {
                                                        candidateQueue.Enqueue(new VarFileEntry(pkg, internalPath, entryTime, entrySize));
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            if (System.Threading.Interlocked.Increment(ref refreshScanDoneCount) == refreshScanWorkers)
                                            {
                                                try { refreshScanDone.Set(); } catch { }
                                            }
                                        }
                                    });
                                }
                                if (refreshPkgSnap.Length > 0)
                                {
                                    refreshScanDone.WaitOne();
                                    try { refreshScanDone.Close(); } catch { }
                                }
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (useSqliteIndex && activeContentSnap == ContentType.Category
                                && userTagNamesForGridSqlSnap != null && userTagNamesForGridSqlSnap.Count > 0)
                                refreshDrainUtSqlFilterApplied[0] = 1;
                        }
                        catch { }
                        if (useSqliteIndex)
                        {
                            try { FileManager.NotifyFirstGallerySqlRefreshComplete(); } catch { }
                        }
                        if (LogGalleryRefreshDeepTiming)
                        {
                            try
                            {
                                long ms = swWorker.ElapsedMilliseconds;
                                LogUtil.Log("[VPB.Gallery.DeepTiming] RefreshWorker DONE"
                                    + " | ms=" + ms
                                    + " | useSql=" + (useSqliteIndex ? "1" : "0")
                                    + " | sqlMs=" + catQueryStats.SqlElapsedMs
                                    + " | sqlRows=" + catQueryStats.RowsRead
                                    + " | reject=" + (catQueryStats.RejectReason ?? "")
                                    + " | title='" + (titleForIndexMain ?? "") + "'"
                                    + " | ext='" + (extForIndexMain ?? "") + "'"
                                    + " | path='" + (currentPath ?? "") + "'");
                            }
                            catch { }
                        }
                        swWorker.Stop();
                        Interlocked.Exchange(ref workerDoneFlag, 1);
                    }
                });

                bool sqliteBulkConsumed = false;
                // Drain results incrementally on main thread (SQLite fast path delivers one batched list — no per-row lock).
                var swDrainMain = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    if (localLoadingGroupId != currentLoadingGroupId)
                    {
                        HideLoadingOverlay();
                        refreshCoroutine = null;
                        CompletePaneLoadTimingIfPending("(refresh superseded)");
                        yield break;
                    }

                    bool hadWork = false;
                    FileEntry entry;
                    while (true)
                    {
                        lock (candidateQueueLock)
                        {
                            if (candidateQueue.Count == 0)
                            {
                                break;
                            }

                            entry = candidateQueue.Dequeue();
                        }

                        hadWork = true;

                        if (RefreshFilesRoutineDrainProcessAndShouldYield(entry, files, ref yieldWatch, maxMsPerFrame, localLoadingGroupId, wantsPoseCounts, false))
                        {
                            yield return null;
                            yieldWatch.Reset();
                            yieldWatch.Start();
                        }
                    }

                    if (!sqliteBulkConsumed
                        && Interlocked.CompareExchange(ref workerDoneFlag, 0, 0) == 1
                        && sqliteBulkList != null)
                    {
                        List<FileEntry> bulk = sqliteBulkList;
                        sqliteBulkList = null;
                        sqliteBulkConsumed = true;
                        _refreshSqliteBulkIncludedUserTagGridFilter = refreshDrainUtSqlFilterApplied[0] != 0;
                        if (activeContentSnap == ContentType.History)
                            SyncHistoryBrowseFailureFlagsFromStats(activeContentSnap, catQueryStats, nameTerms);
                        long bulkBudgetMs = maxMsPerFrame;
                        int bc = bulk.Count;
                        if (bc >= 16000) bulkBudgetMs = System.Math.Max(maxMsPerFrame, 160L);
                        else if (bc >= 8000) bulkBudgetMs = System.Math.Max(maxMsPerFrame, 120L);
                        else if (bc >= 3000) bulkBudgetMs = System.Math.Max(maxMsPerFrame, 80L);

                        bool filtersOnWorker = System.Threading.Interlocked.CompareExchange(ref refreshWorkerUsedSqliteFlag[0], 0, 0) != 0;
                        if (RefreshFilesRoutineCanFastAppendSqliteBulkList(wantsPoseCounts, filtersOnWorker) && bulk.Count > 0)
                        {
                            if (localLoadingGroupId != currentLoadingGroupId)
                            {
                                HideLoadingOverlay();
                                refreshCoroutine = null;
                                CompletePaneLoadTimingIfPending("(refresh superseded)");
                                yield break;
                            }
                            int needCap = files.Count + bulk.Count;
                            if (files.Capacity < needCap)
                                files.Capacity = needCap;
                            files.AddRange(bulk);
                        }
                        else
                        {
                            if (LogGalleryRefreshDeepTiming && bulk.Count >= 5000)
                            {
                                try
                                {
                                    LogUtil.Log("[VPB.Gallery.DeepTiming] RefreshFilesRoutine bulk slow-path"
                                        + " | bulk=" + bulk.Count
                                        + " | ratingToggle=" + (isRatingSortToggleEnabled ? "1" : "0")
                                        + " | ratingFilter=" + (string.IsNullOrEmpty(currentRatingFilter) ? "0" : "1")
                                        + " | sizeFilter=" + (string.IsNullOrEmpty(currentSizeFilter) ? "0" : "1")
                                        + " | sceneSrcFilter=" + (string.IsNullOrEmpty(currentSceneSourceFilter) ? "0" : "1")
                                        + " | poseFilter=" + ((posePeopleFilter != PosePeopleFilter.All || wantsPoseCounts) ? "1" : "0")
                                        + " | appearanceFilter=" + ((!string.IsNullOrEmpty(currentAppearanceSourceFilter) || appearanceSubfilter != 0) ? "1" : "0"));
                                }
                                catch { }
                            }
                            for (int bi = 0; bi < bulk.Count; bi++)
                            {
                                if (localLoadingGroupId != currentLoadingGroupId)
                                {
                                    HideLoadingOverlay();
                                    refreshCoroutine = null;
                                    CompletePaneLoadTimingIfPending("(refresh superseded)");
                                    yield break;
                                }

                                if (RefreshFilesRoutineDrainProcessAndShouldYield(bulk[bi], files, ref yieldWatch, bulkBudgetMs, localLoadingGroupId, wantsPoseCounts, true))
                                {
                                    yield return null;
                                    yieldWatch.Reset();
                                    yieldWatch.Start();
                                }
                            }
                        }

                        hadWork = true;
                    }

                    if (!hadWork && Interlocked.CompareExchange(ref workerDoneFlag, 0, 0) == 1)
                    {
                        break;
                    }
                    yield return null;
                }
                swDrainMain.Stop();
                refreshDrainWallMs = swDrainMain.ElapsedMilliseconds;
            }
            }
            if (swDeep != null)
            {
                deepAfterDrainMs = swDeep.ElapsedMilliseconds;
                deepFilesCountAfterDrain = files != null ? files.Count : 0;
            }

            List<string> pathsToSearch = new List<string>();
            if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
            else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

            if (activeContentType == ContentType.Category)
            {
                if (!HasCreatorFilter())
                {
                    // When file list came from GalleryFileListSnapshotCache, it already merged VAR index + loose files for this key.
                    // Re-enumerating Saves/* (SafeGetFiles + PassesFilters per path) duplicates work and can burn 10s+ on large trees with sysAdded=0.
                    if (!fileListFromCache)
                    {
                    // Fast path: reuse SQLite-cached loose-file listings for this category/path/ext combo when unchanged.
                    string sysCacheKey = null;
                    string sysCacheSig = null;
                    List<VpbLocalDatabase.SystemFileRow> sysCachedRows = null;
                    bool sysCacheHit = false;
                    try
                    {
                        // Cache key: category + extensions + search paths.
                        var sbKey = new System.Text.StringBuilder(256);
                        sbKey.Append(currentCategoryTitle ?? "").Append("|ext=");
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

                        // Signature: deep max(mtime) per scan root. Top-level mtime alone misses additions in
                        // subfolders, which kept the cache stale across sessions. DeepMaxDirMtimeBinary walks the dir tree and takes max mtime so
                        // any subfolder change invalidates.
                        var sbSig = new System.Text.StringBuilder(256);
                        for (int i = 0; i < p2.Count; i++)
                        {
                            string sp = p2[i];
                            long t = VpbLocalDatabase.DeepMaxDirMtimeBinary(sp);
                            if (i != 0) sbSig.Append('|');
                            sbSig.Append(t.ToString());
                        }
                        sysCacheSig = sbSig.ToString();

                        sysCachedRows = new List<VpbLocalDatabase.SystemFileRow>();
                        sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCachedRows);
                    }
                    catch { sysCacheHit = false; sysCachedRows = null; }
                    if (swDeep != null)
                        deepSysCacheHit = sysCacheHit;

                    if (sysCacheHit && sysCachedRows != null)
                    {
                        bool prunedMissingCachedRows = false;
                        List<VpbLocalDatabase.SystemFileRow> sysRowsToKeep = new List<VpbLocalDatabase.SystemFileRow>(sysCachedRows.Count);
                        string titleForGeneratedSceneSkip = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                        bool skipVpbGeneratedLocalScenes = titleForGeneratedSceneSkip.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
                        for (int i = 0; i < sysCachedRows.Count; i++)
                        {
                            var r = sysCachedRows[i];
                            if (string.IsNullOrEmpty(r.Path)) continue;
                            if (skipVpbGeneratedLocalScenes && LocalSceneGallerySupport.IsVpbGeneratedLocalScenePath(r.Path))
                            {
                                LocalSceneGallerySupport.TryEnsureVpbGeneratedSceneHideMarker(r.Path);
                                prunedMissingCachedRows = true;
                                continue;
                            }
                            if (!File.Exists(r.Path))
                            {
                                prunedMissingCachedRows = true;
                                continue;
                            }
                            sysRowsToKeep.Add(r);
                            DateTime wt = DateTime.MinValue;
                            if (r.LastWriteBinaryOrInvalid != long.MinValue)
                            {
                                try { wt = DateTime.FromBinary(r.LastWriteBinaryOrInvalid); } catch { wt = DateTime.MinValue; }
                            }
                            long sz = r.SizeOrInvalid != long.MinValue ? r.SizeOrInvalid : 0;
                            var sysEntryFast = new SystemFileEntry(r.Path, wt, sz, exists: true);
                            if (!PassesFilters(sysEntryFast, true)) continue;
                            files.Add(sysEntryFast);
                            if (sysLooseFilesAddedCount != null) sysLooseFilesAddedCount[0]++;
                            if (swDeep != null) deepSysFilesAdded++;
                        }
                        if (prunedMissingCachedRows)
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                                    VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysRowsToKeep);
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        var sysRowsForWrite = new List<VpbLocalDatabase.SystemFileRow>(256);
                        string titleForGeneratedSceneSkip = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                        bool skipVpbGeneratedLocalScenes = titleForGeneratedSceneSkip.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
                    string[] diskExtsForLoose = Gallery.DiskScanExtensionsOrEverything(currentExtension, extensions);
                    foreach (var searchPath in pathsToSearch)
                    {
                        if (!Directory.Exists(searchPath)) continue;

                        foreach (var ext in diskExtsForLoose)
                        {
                            string[] sysFiles = new string[0];
                            try
                            {
                                List<string> sysFileList = new List<string>();
                                FileManager.SafeGetFiles(searchPath, "*." + ext, sysFileList);
                                sysFiles = sysFileList.ToArray();
                            }
                            catch { }

                            foreach (var sysPath in sysFiles)
                            {
                                if (skipVpbGeneratedLocalScenes && LocalSceneGallerySupport.IsVpbGeneratedLocalScenePath(sysPath))
                                {
                                    LocalSceneGallerySupport.TryEnsureVpbGeneratedSceneHideMarker(sysPath);
                                    continue;
                                }

                                if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                                {
                                    yield return null;
                                    yieldWatch.Reset();
                                    yieldWatch.Start();
                                }

                                var sysEntry = new SystemFileEntry(sysPath);

                                bool baseOk = PassesFilters(sysEntry, true);
                                if (!baseOk) continue;

                                int pcPose = 1;
                                bool needPc = wantsPoseCounts || (posePeopleFilter != PosePeopleFilter.All);
                                if (needPc)
                                {
                                    bool isJsonPose = false;
                                    try { isJsonPose = (sysEntry.Path != null && sysEntry.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)); } catch { isJsonPose = false; }
                                    if (isJsonPose)
                                    {
                                        int known;
                                        if (TryGetKnownPosePeopleCount(sysEntry, out known))
                                        {
                                            pcPose = known;
                                        }
                                        else
                                        {
                                            EnqueuePosePeopleIndex(sysEntry);
                                            pcPose = 1;
                                        }
                                    }
                                    else
                                    {
                                        pcPose = 1;
                                    }
                                    if (wantsPoseCounts)
                                    {
                                        if (pcPose >= 2) posePeopleFacetCountDual++;
                                        else posePeopleFacetCountSingle++;
                                    }
                                    if (posePeopleFilter == PosePeopleFilter.Single && pcPose >= 2) continue;
                                    if (posePeopleFilter == PosePeopleFilter.Dual && pcPose < 2) continue;
                                }

                                if (isRatingSortToggleEnabled)
                                {
                                    if (RatingsManager.Instance.GetRating(sysEntry) <= 0) continue;
                                }

                                files.Add(sysEntry);
                                if (sysLooseFilesAddedCount != null)
                                    sysLooseFilesAddedCount[0]++;
                                if (swDeep != null) deepSysFilesAdded++;

                                try
                                {
                                    var rr = new VpbLocalDatabase.SystemFileRow();
                                    rr.Path = sysEntry.Path ?? sysPath;
                                    long wtB = long.MinValue;
                                    try { wtB = sysEntry.LastWriteTime.ToBinary(); } catch { wtB = long.MinValue; }
                                    rr.LastWriteBinaryOrInvalid = wtB;
                                    rr.SizeOrInvalid = sysEntry.Size;
                                    sysRowsForWrite.Add(rr);
                                }
                                catch { }
                            }
                        }
                    }
                        try
                        {
                            if (!string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null)
                                VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysRowsForWrite);
                        }
                        catch { }
                    }
                    }
                }
            }

            if (swDeep != null) deepAfterSysFilesMs = swDeep.ElapsedMilliseconds;

            if (!fileListFromCache)
            {
                yield return null; // Yield before sorting
                var sortState = GetSortState("Files");
                if (LogGalleryRefreshDeepTiming)
                {
                    try
                    {
                        LogUtil.Log("[VPB.Gallery.DeepTiming] Sort start"
                            + " | type=" + (sortState != null ? sortState.Type.ToString() : "(null)")
                            + " | dir=" + (sortState != null ? sortState.Direction.ToString() : "(null)")
                            + " | count=" + (files != null ? files.Count : 0)
                            + " | title='" + (currentCategoryTitle ?? "") + "'"
                            + " | path='" + (currentPath ?? "") + "'");
                    }
                    catch { }
                }
                bool sortSnapStillMatches = fileListSortSnapForWorker != null
                    && sortState.Type == fileListSortSnapForWorker.Type
                    && sortState.Direction == fileListSortSnapForWorker.Direction;
                bool skipMainThreadSort = sortSnapStillMatches
                    && sqliteBulkSortedOnWorkerFlag != null && sqliteBulkSortedOnWorkerFlag[0] != 0
                    && sysLooseFilesAddedCount != null && sysLooseFilesAddedCount[0] == 0;
                var swSortMain = System.Diagnostics.Stopwatch.StartNew();
                bool historyBrowseOrder = activeContentType == ContentType.History;
                if (!historyBrowseOrder && !skipMainThreadSort)
                    GallerySortManager.Instance.SortFiles(files, sortState);
                swSortMain.Stop();
                if (LogGalleryRefreshDeepTiming)
                {
                    try { LogUtil.Log("[VPB.Gallery.DeepTiming] Sort done ms=" + swSortMain.ElapsedMilliseconds); } catch { }
                }
                if (canFileListCache && fileListSnapKey != null)
                    GalleryFileListSnapshotCache.Put(fileListSnapKey, files);
            }
            if (swDeep != null) deepAfterSortMs = swDeep.ElapsedMilliseconds;

            if (IsSettingsPanelOpen() || settingsListViewActive)
            {
                RefreshInternalSettingsListRows(keepScroll);
                refreshCoroutine = null;
                yield break;
            }

            // Cache the filtered list for selection operations (Select All, counts, etc)
            lastFilteredFiles.Clear();
            lastFilteredFiles.AddRange(files);

            try
            {
                galleryFilesPreHideSnapshot.Clear();
                galleryFilesPreHideSnapshot.AddRange(files);
                galleryPreHideSnapshotValid = true;
            }
            catch
            {
                galleryFilesPreHideSnapshot.Clear();
                galleryPreHideSnapshotValid = false;
            }

            // Promote to class member for RecyclingGridView — one copy pass from lastFilteredFiles (same snapshot as files)
            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(lastFilteredFiles);
            // If no name filter was active, the next SetNameFilter call can use currentFilteredFiles
            // as a trustworthy unfiltered base for in-memory search.
            if (nameFilterTerms == null || nameFilterTerms.Length == 0)
                _topSearchBaseIsClean = true;

            if (swDeep != null) deepGbListCopyMs = swDeep.ElapsedMilliseconds;

            // Setup Recycling Grid
            if (contentGO != null)
            {
                // RecyclingGridView is already initialized in Init.cs, but ensure we have it
                if (recyclingGrid == null) recyclingGrid = contentGO.GetComponent<RecyclingGridView>();
                if (recyclingGrid == null) recyclingGrid = contentGO.AddComponent<RecyclingGridView>();
                
                // Ensure correct component references
                recyclingGrid.scrollRect = this.scrollRect;
                recyclingGrid.content = contentGO.GetComponent<RectTransform>();

                // Setup Callbacks
                recyclingGrid.onCreateItem = () => {
                    var swC = System.Diagnostics.Stopwatch.StartNew();
                    var go0 = CreateNewFileButtonGO();
                    swC.Stop();
                    deepGbCreateTotalMs += swC.ElapsedMilliseconds;
                    deepGbCreateCount++;
                    return go0;
                };
                recyclingGrid.onBindItem = (go, index) => {
                    if (index >= 0 && index < currentFilteredFiles.Count)
                    {
                        // Use CachedCenterItemIndex (computed once per UpdateVisibleItems call)
                        // instead of calling GetCenterItemIndex() per item — avoids N viewport.rect accesses.
                        int centerIdx = recyclingGrid != null ? recyclingGrid.CachedCenterItemIndex : 0;
                        int dist = Mathf.Abs(index - centerIdx);
                        _nextThumbPriority = Mathf.Min(90, dist * 3); // center=0 (first), edges=higher (later)
                        var swB = System.Diagnostics.Stopwatch.StartNew();
                        BindFileButton(go, currentFilteredFiles[index]);
                        swB.Stop();
                        deepGbBindTotalMs += swB.ElapsedMilliseconds;
                        deepGbBindCount++;
                    }
                };
                
                // Use Adaptive Config
                float minSize = 200f;
                int cols = GridColumnCount;
                
                // Initialize spacing and adaptive config
                if (layoutMode == GalleryLayoutMode.List)
                {
                    // List/Table mode: ALWAYS 1 column; +/- controls row height/thumb size.
                    recyclingGrid.fixedColumns = 1;
                    recyclingGrid.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh: true);
                    recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh: true);
                }
                else
                {
                    recyclingGrid.SetGridConfig(100f, GetGridCellConfigHeight(), EffectiveGridSpacingX(), EffectiveGridSpacingY(), cols, deferRefresh: true);
                    recyclingGrid.SetAdaptiveConfig(true, minSize, cols, false, deferRefresh: true);
                }
                if (swDeep != null) deepGbConfigMs = swDeep.ElapsedMilliseconds;

                // Set item count and pre-position scroll so the first UpdateVisibleItems
                // binds the correct viewport items, not items at the top.
                if (scrollToBottom)
                {
                    recyclingGrid.SetItemCountAtScroll(currentFilteredFiles.Count, 0f);
                }
                else if (savedCenterItemIndex >= 0)
                {
                    recyclingGrid.SetItemCountAtItem(currentFilteredFiles.Count, savedCenterItemIndex);
                }
                else
                {
                    recyclingGrid.SetItemCountAtScroll(currentFilteredFiles.Count, savedScrollNormalizedPos);
                }
                if (swDeep != null) deepGbSetItemMs = swDeep.ElapsedMilliseconds;
            }
            if (swDeep != null) deepAfterGridBindMs = swDeep.ElapsedMilliseconds;

            // Legacy nav/file buttons (non-recycling): destroy in slices so main thread yields between batches (VaM stays responsive).
            int legacyBtnCount = activeButtons.Count;
            if (legacyBtnCount > 0)
            {
                var pendingDestroy = new List<GameObject>(legacyBtnCount);
                pendingDestroy.AddRange(activeButtons);
                activeButtons.Clear();
                StartCoroutine(DestroyLegacyActiveButtonsBudgetCo(pendingDestroy));
            }
            else
                activeButtons.Clear();
            fileButtonImages.Clear();

            UpdatePaginationText();

            if (earlyMetaNeeded)
            {
                if (skipEarlyMetaThread)
                {
                    ApplyEarlyMetaRefreshResults(metaBuildGroupId, earlyBuildCreators, earlyBuildCats, sideMetaCacheKey, false,
                        earlyNewCreators, earlyNewCatCounts);
                    if (metaBuildGroupId == currentLoadingGroupId)
                    {
                        try { UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: false); } catch { }
                    }
                }
                else
                {
                    IEnumerator CoApplyEarlyMetaWhenReady()
                    {
                        while (!earlyMetaBuildDone) yield return null;
                        ApplyEarlyMetaRefreshResults(metaBuildGroupId, earlyBuildCreators, earlyBuildCats, sideMetaCacheKey, true,
                            earlyNewCreators, earlyNewCatCounts);
                        // ApplyEarlyMetaRefreshResults no-ops if refresh was superseded; do not run full side-tab rebuild on stale session.
                        if (metaBuildGroupId != currentLoadingGroupId) yield break;
                        try { UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: false); } catch { }
                    }
                    _earlyMetaApplyCoroutine = StartCoroutine(CoApplyEarlyMetaWhenReady());
                }
            }
            if (swDeep != null) deepAfterEarlyMetaWaitMs = swDeep.ElapsedMilliseconds;

            LogGalleryCategoryTypeNavPhase("RefreshFilesRoutine_before_UpdateLayout");
            if (swDeep != null)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.DeepTiming] RefreshFilesRoutine pre-UpdateLayout"
                        + " | t=" + swDeep.ElapsedMilliseconds + "ms"
                        + " | syncCpuBeforeFirstYield=" + syncCpuBeforeFirstYieldMs + "ms"
                        + " | stallUntilRoutineResume=" + stallUntilRoutineResumeMs + "ms"
                        + " | afterFirstYield=" + deepAfterFirstYieldMs + "ms"
                        + " | refreshSrc=" + (_refreshFilesDebugSource ?? "")
                        + " | afterDrain=" + deepAfterDrainMs + "ms"
                        + " | drainWall=" + refreshDrainWallMs + "ms"
                        + " | afterSysFiles=" + deepAfterSysFilesMs + "ms"
                        + " | sysCacheHit=" + (deepSysCacheHit ? "1" : "0")
                        + " | sysAdded=" + deepSysFilesAdded
                        + " | afterSort=" + deepAfterSortMs + "ms"
                        + " | afterGridBind=" + deepAfterGridBindMs + "ms"
                        + " | gbListCopy=" + deepGbListCopyMs + "ms"
                        + " | gbConfig=" + deepGbConfigMs + "ms"
                        + " | gbSetItem=" + deepGbSetItemMs + "ms"
                        + " | gbCreate=" + deepGbCreateTotalMs + "ms/" + deepGbCreateCount
                        + " | gbBind=" + deepGbBindTotalMs + "ms/" + deepGbBindCount
                        + " | afterEarlyMetaWait=" + deepAfterEarlyMetaWaitMs + "ms"
                        + " | filesAfterDrain=" + deepFilesCountAfterDrain
                        + " | filesFinal=" + (files != null ? files.Count : 0)
                        + " | title='" + (currentCategoryTitle ?? "") + "'"
                        + " | path='" + (currentPath ?? "") + "'");
                }
                catch { }
            }
            // Worker thread builds creator/category counts during refresh — skip redundant main-thread VAR scans here (still allow user-tag cache).
            bool suppressSyncCreatorCategoryCaches = earlyMetaNeeded && !skipEarlyMetaThread;
            UpdateLayout(!suppressSyncCreatorCategoryCaches, true);
            if (swDeep != null) deepUpdateLayoutMs = swDeep.ElapsedMilliseconds;
            LogGalleryCategoryTypeNavPhase("RefreshFilesRoutine_after_UpdateLayout");
            // Layout rebuild can clamp ScrollRect and undo the position we just set.
            if (scrollRect != null && !scrollToBottom)
            {
                if (savedCenterItemIndex >= 0 && recyclingGrid != null)
                    recyclingGrid.ScrollToCenterItem(savedCenterItemIndex);
                else
                {
                    scrollRect.verticalNormalizedPosition = savedScrollNormalizedPos;
                    if (recyclingGrid != null) recyclingGrid.Refresh();
                }
            }

            // Hide overlay and stop pane timing before full UpdateTabs(): side-tab rebuild (hundreds of buttons) is not the file grid
            // and was inflating "until grid ready" by 1–2+ s. Thumbnails for visible rows use memory cache + threaded queue (BindFileButton/LoadThumbnail), not a full-grid decode here.
            bool rebuildSideTabsAfterFirstLoad = _sideTabsNeedFullRebuildAfterFirstRefresh;
            _deferSideTabCountsForceRefresh = rebuildSideTabsAfterFirstLoad;
            if (_sideTabsNeedFullRebuildAfterFirstRefresh)
            {
                categoriesCached = false;
                creatorsCached = false;
                try { InvalidateSharedSideMetaIfPackageScanAdvanced(); } catch { }
                _sideTabsNeedFullRebuildAfterFirstRefresh = false;
            }

            HideLoadingOverlay();
            hasLoadedContent = true;
            try { VpbProgressService.NotifyGalleryUiReady(); } catch { }
            refreshCoroutine = null;
            CompletePaneLoadTimingIfPending();

            if (swDeep != null)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.DeepTiming] RefreshFilesRoutine DONE"
                        + " | total=" + swDeep.ElapsedMilliseconds + "ms"
                        + " | updateLayoutAt=" + deepUpdateLayoutMs + "ms"
                        + " | refreshSrc=" + (_refreshFilesDebugSource ?? "")
                        + " | title='" + (currentCategoryTitle ?? "") + "'"
                        + " | path='" + (currentPath ?? "") + "'"
                        + " | files=" + (files != null ? files.Count : 0));
                }
                catch { }
            }

            // Show() used UpdateTabsImpl(false) while this coroutine ran, so category/creator/tag side lists stay stale until here.
            // Defer one frame (same as first-load / Pose) so we do not block overlay hide; covers every category switch.
            if (!IsHubMode && (leftTabContainerGO != null || rightTabContainerGO != null))
                _deferredGallerySideTabsCoroutine = StartCoroutine(DeferredGallerySideTabsAfterGridReady(navSessionForThisRun, _deferredSubPaneSessionId, tagParallelWaiterForThisRun, tagScanRefreshSeq));

            // Defer hide filtering until after the grid is visible (prescan .hide markers then filter in a coroutine).
            // Always run follow-up: hide strip (unless sort needs hidden rows), then Hidden-only / AutoInstall-only narrowing, then re-sort.
            try
            {
                StartCoroutine(PostFilesListHideAndSortFollowupRoutine(currentLoadingGroupId, keepScroll, scrollToBottom, savedScrollNormalizedPos));
            }
            catch { }

            // If packages were skipped because their content cache wasn't ready yet
            // (FileManager scan still in progress), schedule a single retry — but only
            // if no retry is already pending/running. This prevents an infinite refresh
            // loop where each retry finds uncached packages and spawns yet another retry.
            if (skippedForNoCache[0] > 0 && !Gallery.IsSuppressed() && !_cacheRetryPending)
            {
                if (LogGalleryRefreshDeepTiming)
                {
                    try
                    {
                        string s0 = skippedForNoCacheSample != null && skippedForNoCacheSample.Length > 0 ? (skippedForNoCacheSample[0] ?? "") : "";
                        string s1 = skippedForNoCacheSample != null && skippedForNoCacheSample.Length > 1 ? (skippedForNoCacheSample[1] ?? "") : "";
                        string s2 = skippedForNoCacheSample != null && skippedForNoCacheSample.Length > 2 ? (skippedForNoCacheSample[2] ?? "") : "";
                        string samp = (s0.Length + s1.Length + s2.Length) == 0 ? "" : (" | sample='" + s0 + (s1.Length > 0 ? ("; " + s1) : "") + (s2.Length > 0 ? ("; " + s2) : "") + "'");
                        LogUtil.Log("[VPB.Gallery.DeepTiming] RefreshFilesRoutine missing-cache packages=" + skippedForNoCache[0]
                            + " | FileManager.IsScanning=" + (FileManager.IsScanning ? "1" : "0")
                            + " | lastPackageRefreshTime=" + FileManager.lastPackageRefreshTime.ToString("o")
                            + samp);
                    }
                    catch { }
                }
                LogUtil.Log($"[VPB] RefreshFilesRoutine: {skippedForNoCache[0]} packages had no cache yet; scheduling one-shot retry.");
                _cacheRetryPending = true;
                StartCoroutine(RetryRefreshAfterNoCacheDelay());
            }

            if (isPoseCategory)
            {
                try { PosePeopleCountIndex.Instance.Save(); } catch { }

                // Start background indexing for unknown pose json entries.
                bool hasWork = false;
                lock (posePeopleIndexLock) { hasWork = posePeopleIndexQueue.Count > 0; }
                if (hasWork)
                {
                    try { StartPosePeopleIndexCoroutine(currentLoadingGroupId); } catch { }
                }
            }
        }

        /// <summary>Scene split sub-pane uses <see cref="ContentType.SceneSource"/> only — no <see cref="GalleryPanel.CacheTagCounts"/> pass.</summary>
        private bool DeferredSubPaneNeedsTagCountCachePass()
        {
            if (IsHubMode) return false;
            string title = titleText != null ? titleText.text : "";
            bool leftCat = leftActiveContent.HasValue && leftActiveContent.Value == ContentType.Category;
            bool rightCat = rightActiveContent.HasValue && rightActiveContent.Value == ContentType.Category;
            if (!leftCat && !rightCat) return false;

            bool thickSplit =
                title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!thickSplit) return false;

            if (title.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (leftCat && leftSubTabScrollGO != null && leftSubTabContainerGO != null) return true;
            if (rightCat && rightSubTabScrollGO != null && rightSubTabContainerGO != null) return true;
            return false;
        }

        private IEnumerator DeferredGallerySideTabsAfterGridReady(int categoryNavTargetSessionForThisRefresh, int deferredSubPaneSessionWhenScheduled, TagParallelWaiter tagParallelWaiterForThisRefresh, int tagParallelRefreshSeq)
        {
            yield return null;
            // Phase 1: main side strips (category/creator). Sub-pane is cleared here; tag UI fills in phase 2 after "interactive DONE".
            LogGalleryCategoryTypeNavPhase("deferred_sideTabs_phase1_main_before");
            bool forceCounts = _deferSideTabCountsForceRefresh;
            _deferSideTabCountsForceRefresh = false;
            try { EnsureSideTabCountsFreshAfterGridReady(force: forceCounts); } catch { }
            try { UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: false); } catch { }
            if (DeferredSubPaneNeedsTagCountCachePass())
            {
                string titleSnap = titleText != null ? titleText.text : "";
                if (titleSnap.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string tckSnap;
                    TagCountSnapshot snap = null;
                    bool haveSnap = false;
                    if (TryBuildTagCountCacheKey(out tckSnap))
                        haveSnap = GalleryTagCountSnapshotCache.TryGet(tckSnap, out snap) && snap != null;
                    if (haveSnap)
                    {
                        try { RestoreTagCountSnapshot(snap); tagsCached = true; } catch { }
                    }
                    else if (!TryPrimeAppearanceSubPaneCounts())
                    {
                        tagsCached = false;
                    }
                    try { RebuildSubPaneSideTabListsOnly(); } catch { }
                }
            }
            LogGalleryCategoryTypeNavPhase("deferred_sideTabs_phase1_main_after");
            yield return null;

            string subPaneLogLabel = null;
            int subPaneLogSession = 0;
            if (LogGalleryCategoryTypeSwitchTiming)
            {
                subPaneLogLabel = _categoryTypeNavLabel;
                subPaneLogSession = _categoryTypeNavTargetSession;
            }

            if (!TryFinalizeGalleryCategoryTypeNavigationFromDeferred(categoryNavTargetSessionForThisRefresh))
                yield break;

            // Phase 2: heavy tag/appearance sub-pane — does not block category navigation timing (fills in background).
            System.Diagnostics.Stopwatch subPaneSw = null;
            if (LogGalleryCategoryTypeSwitchTiming)
            {
                subPaneSw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    LogUtil.Log("[VPB.Gallery.Timing] catNav#" + subPaneLogSession + " subPane_async START '" + (subPaneLogLabel ?? "") + "' (tag scan + sub-pane UI)");
                }
                catch { }
            }

            // CS1626: cannot yield inside try/catch — keep yields here, wrap only the sync rebuild in try/catch.
            bool ranSlicedTagScan = false;
            if (ShouldSkipHeavyAppearanceTagParallelScan())
            {
                bool primed = TryRecomputeAppearanceGenderFacetCountsScoped();
                if (!primed && TryApplyAppearanceFacetCountsFromSql())
                {
                    TryMergeLooseVapAppearanceGenderFacetCounts();
                    primed = true;
                }
                if (primed)
                {
                    tagsCached = true;
                    ranSlicedTagScan = true;
                    string tckScoped;
                    if (TryBuildTagCountCacheKey(out tckScoped))
                    {
                        try { GalleryTagCountSnapshotCache.Put(tckScoped, CaptureTagCountSnapshot()); } catch { }
                    }
                }
            }
            if (tagParallelWaiterForThisRefresh != null && !ranSlicedTagScan)
            {
                System.Diagnostics.Stopwatch waitSw = LogGalleryRefreshDeepTiming ? System.Diagnostics.Stopwatch.StartNew() : null;
                while (!tagParallelWaiterForThisRefresh.Finished)
                {
                    if (deferredSubPaneSessionWhenScheduled != _deferredSubPaneSessionId)
                        yield break;
                    yield return null;
                }
                if (waitSw != null)
                {
                    long ms = waitSw.ElapsedMilliseconds;
                    if (ms >= 50)
                    {
                        try { LogUtil.Log("[VPB.Gallery.DeepTiming] deferred_sideTabs tagParallelWait total=" + ms + "ms | tagParallelRefreshSeq=" + tagParallelRefreshSeq + " | curRefreshSeq=" + GalleryFileRefreshSequence); } catch { }
                    }
                }
                if (deferredSubPaneSessionWhenScheduled != _deferredSubPaneSessionId)
                    yield break;
                TagCountParallelOutcome parallelOutcome = tagParallelWaiterForThisRefresh.Outcome;
                if (parallelOutcome != null && !parallelOutcome.Aborted && parallelOutcome.Snapshot != null
                    && tagParallelRefreshSeq == GalleryFileRefreshSequence)
                {
                    RestoreTagCountSnapshot(parallelOutcome.Snapshot);
                    tagsCached = true;
                    string tckPut;
                    if (TryBuildTagCountCacheKey(out tckPut))
                    {
                        try { GalleryTagCountSnapshotCache.Put(tckPut, CaptureTagCountSnapshot()); } catch { }
                    }
                    ranSlicedTagScan = true;
                }
            }
            while (_sideTabsTagCountSliceCo != null)
            {
                if (deferredSubPaneSessionWhenScheduled != _deferredSubPaneSessionId)
                    yield break;
                yield return null;
            }

            if (!tagsCached && DeferredSubPaneNeedsTagCountCachePass())
            {
                ranSlicedTagScan = true;
                IEnumerator scan = CoCacheTagCountsInternal(TagCountScanDeferredSliceMs, deferredSubPaneSessionWhenScheduled);
                while (scan.MoveNext())
                {
                    if (deferredSubPaneSessionWhenScheduled != _deferredSubPaneSessionId)
                        yield break;
                    yield return scan.Current;
                }
            }

            if (deferredSubPaneSessionWhenScheduled != _deferredSubPaneSessionId)
                yield break;
            if (ranSlicedTagScan && !tagsCached)
                yield break;

            try { RebuildSubPaneSideTabListsOnly(); } catch { }

            if (LogGalleryCategoryTypeSwitchTiming && subPaneSw != null)
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Timing] catNav#" + subPaneLogSession + " subPane_async DONE total=" + subPaneSw.ElapsedMilliseconds + "ms '" + (subPaneLogLabel ?? "") + "'");
                }
                catch { }
            }
        }

        private bool FilesSortKeepsHiddenInList()
        {
            try
            {
                SortType t = GetSortState("Files").Type;
                return t == SortType.Hidden || t == SortType.HiddenOnly;
            }
            catch { return false; }
        }

        private bool FilesSortWantsLoadedOnly()
        {
            try { return GetSortState("Files").Type == SortType.LoadedOnly; }
            catch { return false; }
        }

        private bool FilesSortWantsUnloadedOnly()
        {
            try { return GetSortState("Files").Type == SortType.UnloadedOnly; }
            catch { return false; }
        }

        /// <summary>Removes non-matching rows for Hidden-only / AutoInstall-only file sort modes (list is modified in place).</summary>
        private static void ApplyFilesSortExclusiveFiltersInPlace(List<FileEntry> list, SortType type)
        {
            if (list == null) return;
            if (type == SortType.HiddenOnly)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (!PackageHidePrefs.IsGalleryHideBadgeVisible(list[i]))
                            list.RemoveAt(i);
                    }
                    catch { try { list.RemoveAt(i); } catch { } }
                }
            }
            else if (type == SortType.AutoInstallOnly)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        if (list[i] == null || !list[i].IsAutoInstall())
                            list.RemoveAt(i);
                    }
                    catch { try { list.RemoveAt(i); } catch { } }
                }
            }
            else if (type == SortType.LoadedOnly)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        FileEntry e = list[i];
                        if (e == null) { list.RemoveAt(i); continue; }
                        // IMPORTANT: Only check the package root (before ":/") so internal paths like
                        // "...var:/Custom/..." don't incorrectly count as "loaded".
                        string p = (e.Path ?? "").Replace('\\', '/');
                        int sep = p.IndexOf(":/", StringComparison.Ordinal);
                        string root = (sep >= 0) ? p.Substring(0, sep) : p;
                        bool loaded =
                            root.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) ||
                            root.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                            root.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase);
                        if (!loaded) list.RemoveAt(i);
                    }
                    catch { try { list.RemoveAt(i); } catch { } }
                }
            }
            else if (type == SortType.UnloadedOnly)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        FileEntry e = list[i];
                        if (e == null) { list.RemoveAt(i); continue; }
                        string p = (e.Path ?? "").Replace('\\', '/');
                        int sep = p.IndexOf(":/", StringComparison.Ordinal);
                        string root = (sep >= 0) ? p.Substring(0, sep) : p;
                        bool loaded =
                            root.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase) ||
                            root.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                            root.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase);
                        if (loaded) list.RemoveAt(i);
                    }
                    catch { try { list.RemoveAt(i); } catch { } }
                }
            }
            else if (type == SortType.UnusedOnly)
            {
                try
                {
                    var keys = new List<string>(list.Count);
                    for (int i = 0; i < list.Count; i++)
                        keys.Add(VpbLocalDatabase.BuildUsageKey(list[i]));

                    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    if (!VpbLocalDatabase.TryReadItemUseCountsForKeys(keys, counts))
                        return;

                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            string k = VpbLocalDatabase.BuildUsageKey(list[i]);
                            int c = 0;
                            if (!string.IsNullOrEmpty(k) && counts.TryGetValue(k, out int got)) c = got;
                            if (c != 0) list.RemoveAt(i);
                        }
                        catch { try { list.RemoveAt(i); } catch { } }
                    }
                }
                catch
                {
                    // If usage DB is unavailable, keep the list unchanged (fail open).
                }
            }
        }

        /// <summary>Call when <see cref="lastFilteredFiles"/> / grid list mutates without completing <see cref="RefreshFilesRoutine"/>.</summary>
        private void InvalidateGalleryPreHideFileListSnapshot()
        {
            galleryPreHideSnapshotValid = false;
        }

        /// <summary>Rebuilds file list for show-hidden toggle from last full drain snapshot — skips package scan, sort on worker, and <see cref="UpdateLayout"/>.</summary>
        private bool TryFastApplyGalleryShowHiddenToggle(bool keepScroll)
        {
            if (IsHubMode) return false;
            try { if (Gallery.IsSuppressed()) return false; } catch { return false; }
            if (refreshCoroutine != null) return false;
            try { if (_refreshHistoryLightCo != null) return false; } catch { }
            if (!hasLoadedContent) return false;
            if (recyclingGrid == null || currentFilteredFiles == null || scrollRect == null) return false;
            if (!galleryPreHideSnapshotValid) return false;
            if (IsFilterActive) return false;
            if (currentPackageFilterMode != PackageFilterMode.None) return false;
            try { if (cleanupModeActive) return false; } catch { }
            try { if (_userTagAvailFilterMode) return false; } catch { }
            try
            {
                if (nameFilterTerms != null && nameFilterTerms.Length > 0) return false;
            }
            catch { return false; }
            if (activeContentType == ContentType.History) return false;
            if (galleryFilesPreHideSnapshot == null || galleryFilesPreHideSnapshot.Count == 0) return false;

            bool showHidden = false;
            try { showHidden = VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; } catch { }
            bool keepHiddenForSort = FilesSortKeepsHiddenInList();

            float targetScrollNormalizedPos = 1f;
            if (keepScroll && hasLoadedContent)
                targetScrollNormalizedPos = scrollRect.verticalNormalizedPosition;

            try
            {
                currentFilteredFiles.Clear();
                if (showHidden || keepHiddenForSort)
                    currentFilteredFiles.AddRange(galleryFilesPreHideSnapshot);
                else
                {
                    var snap = galleryFilesPreHideSnapshot;
                    for (int i = 0; i < snap.Count; i++)
                    {
                        var e = snap[i];
                        if (!PackageHidePrefs.IsExcludedByGalleryHideFilter(e))
                            currentFilteredFiles.Add(e);
                    }
                }

                SortState st = GetSortState("Files");
                ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);
                if (activeContentType != ContentType.History)
                    GallerySortManager.Instance.SortFiles(currentFilteredFiles, st);

                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                recyclingGrid.Refresh();

                if (keepScroll)
                {
                    if (targetScrollNormalizedPos >= 0.999f)
                        ScrollGalleryToTop();
                    else
                    {
                        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(targetScrollNormalizedPos);
                        recyclingGrid.Refresh();
                    }
                }

                UpdatePaginationText();
                try { RefreshSelectionVisuals(); } catch { }
                try { UpdateSelectionContextMenu(); } catch { }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerator PostFilesListHideAndSortFollowupRoutine(string groupId, bool keepScroll, bool scrollToBottom, float targetScrollNormalizedPos)
        {
            yield return null;
            yield return null;

            if (groupId != currentLoadingGroupId || currentFilteredFiles == null) yield break;

            bool showHidden = false;
            try { showHidden = VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; } catch { }
            bool keepHiddenForSort = FilesSortKeepsHiddenInList();

            if (!showHidden && !keepHiddenForSort)
            {
                int n = currentFilteredFiles.Count;
                int w = 0;
                for (int r = 0; r < n; r++)
                {
                    if (groupId != currentLoadingGroupId) yield break;
                    try
                    {
                        var fe = currentFilteredFiles[r];
                        if (!PackageHidePrefs.IsExcludedByGalleryHideFilter(fe))
                        {
                            if (w != r) currentFilteredFiles[w] = fe;
                            w++;
                        }
                    }
                    catch { }

                    if ((r & 2047) == 2047)
                        yield return null;
                }
                if (w < n)
                {
                    try { currentFilteredFiles.RemoveRange(w, n - w); }
                    catch { }
                }
            }

            if (groupId != currentLoadingGroupId) yield break;

            try
            {
                SortState st = GetSortState("Files");
                int beforeExclusive = currentFilteredFiles.Count;
                ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);

                if (activeContentType != ContentType.History)
                    GallerySortManager.Instance.SortFiles(currentFilteredFiles, st);
            }
            catch { }

            if (groupId == currentLoadingGroupId)
            {
                try
                {
                    if (recyclingGrid != null)
                    {
                        recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                        recyclingGrid.Refresh();
                    }
                    if (!scrollToBottom)
                    {
                        // Follow-up pass must honor the same resolved scroll target as the main refresh pass.
                        if (targetScrollNormalizedPos >= 0.999f)
                        {
                            ScrollGalleryToTop();
                        }
                        else if (scrollRect != null)
                        {
                            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(targetScrollNormalizedPos);
                            if (recyclingGrid != null) recyclingGrid.Refresh();
                        }
                    }
                    UpdatePaginationText();
                }
                catch { }
            }
        }

        /// <summary>Filter to show only the selected package and its dependencies.</summary>
        public void ApplyDependenciesFilter(FileEntry file)
        {
            EnsureFilterBaseCaptured();

            // Try to handle as VarPackage first
            if (TryGetPackageFromEntry(file, out VarPackage pkg, out string label) && pkg != null)
            {
                try { DependencyGraph.EnsureForPackage(pkg.Uid); } catch { }
                List<FileEntry> filtered;

                if (PackageFilterUsesPackageListRows())
                {
                    HashSet<string> uids = BuildUidSetForDependenciesFilter(pkg);
                    try { DependencyGraph.EnsureForUids(uids); } catch { }
                    filtered = BuildPackageListEntriesForUids(uids);
                    currentPackageFilterCount = Math.Max(0, uids.Count - 1);
                }
                else
                {
                    HashSet<string> depUids = BuildUidSetForDependenciesFilter(pkg);
                    try { DependencyGraph.EnsureForUids(depUids); } catch { }
                    if (!string.IsNullOrEmpty(pkg.Uid)) depUids.Remove(pkg.Uid);
                    filtered = new List<FileEntry> { file };
                    AddVarFileEntriesWithPackageInUidSet(filtered, file, currentFilteredFiles, depUids);
                    currentPackageFilterCount = Math.Max(0, filtered.Count - 1);
                }

                currentPackageFilterMasterUid = pkg.Uid;
                currentPackageFilterMode = PackageFilterMode.Dependencies;
                ApplyFilteredList(filtered, $"Dependencies of {label}");
            }
            // Handle scene files
            else if (file != null && (file.Path?.ToLowerInvariant().EndsWith(".json") ?? false))
            {
                var deps = GallerySortManager.ExtractSceneDependencies(file);
                if (deps != null && deps.Count > 0)
                {
                    // Deduplicate: keep only latest version of each Author.Name
                    deps = GallerySortManager.DeduplicateDependenciesByLatestVersion(deps);
                    try { DependencyGraph.EnsureForUids(deps); } catch { }

                    List<FileEntry> filtered;
                    if (PackageFilterUsesPackageListRows())
                    {
                        // In the Scene categories, show package-level rows so missing deps
                        // use the same "Missing" styling as other dependency filters.
                        var uids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var dep in deps)
                        {
                            if (!string.IsNullOrEmpty(dep)) uids.Add(dep);
                        }
                        filtered = new List<FileEntry> { file };
                        filtered.AddRange(BuildPackageListEntriesForUids(uids));
                    }
                    else
                    {
                        filtered = new List<FileEntry> { file };
                        // Resolve each dependency to actual VarPackage and add as VarFileEntry
                        foreach (var dep in deps)
                        {
                            VarPackage depPkg = FileManager.GetPackageForDependency(dep, false);
                            if (depPkg != null)
                            {
                                // Create VarFileEntry - always use meta.json to show master package only
                                string internalPath = "meta.json";
                                try
                                {
                                    VarFileEntry vfe = new VarFileEntry(depPkg, internalPath, depPkg.LastWriteTime, depPkg.Size);
                                    if (!string.IsNullOrEmpty(vfe.Name) && !string.IsNullOrEmpty(vfe.Path))
                                    {
                                        if (!PackageHidePrefs.IsExcludedByGalleryHideFilter(vfe))
                                            filtered.Add(vfe);
                                    }
                                    else
                                    {
                                        LogUtil.LogError($"[VPB] Invalid VarFileEntry created for {depPkg.Uid}/{internalPath}");
                                        filtered.Add(new VirtualFileEntry(dep));
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogError($"[VPB] Failed to create VarFileEntry for {depPkg.Uid}: {ex}");
                                    filtered.Add(new VirtualFileEntry(dep));
                                }
                            }
                            else
                            {
                                // If package not found, use placeholder
                                try
                                {
                                    VirtualFileEntry vfe = new VirtualFileEntry(dep);
                                    if (!string.IsNullOrEmpty(vfe.Name) && !string.IsNullOrEmpty(vfe.Path))
                                    {
                                        filtered.Add(vfe);
                                    }
                                    else
                                    {
                                        LogUtil.LogError($"[VPB] Invalid VirtualFileEntry created for {dep}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogError($"[VPB] Failed to create VirtualFileEntry for {dep}: {ex}");
                                }
                            }
                        }
                    }

                    currentPackageFilterCount = deps.Count;
                    currentPackageFilterMasterUid = file.Path;
                    currentPackageFilterMode = PackageFilterMode.Dependencies;
                    ApplyFilteredList(filtered, $"Dependencies ({deps.Count})");
                }
            }
        }

        /// <summary>Filter to show only the selected package and its dependents.</summary>
        public void ApplyDependentsFilter(FileEntry file)
        {
            if (!TryGetPackageFromEntry(file, out VarPackage pkg, out string label) || pkg == null) return;

            EnsureFilterBaseCaptured();
            try { DependencyGraph.EnsureForPackage(pkg.Uid); } catch { }

            string targetUid = pkg.Uid;
            string targetShort = GetPackageGroupShortUid(targetUid);

            List<FileEntry> filtered;
            if (PackageFilterUsesPackageListRows())
            {
                HashSet<string> uids = CollectUidsForDependentsPackageListFilter(targetUid, targetShort);
                filtered = BuildPackageListEntriesForUids(uids);
                currentPackageFilterCount = Math.Max(0, uids.Count - 1);
            }
            else
            {
                HashSet<string> uids = CollectUidsForDependentsPackageListFilter(targetUid, targetShort);
                if (!string.IsNullOrEmpty(targetUid)) uids.Remove(targetUid);
                filtered = new List<FileEntry> { file };
                AddVarFileEntriesWithPackageInUidSet(filtered, file, currentFilteredFiles, uids);
                currentPackageFilterCount = Math.Max(0, filtered.Count - 1);
            }

            currentPackageFilterMasterUid = pkg.Uid;
            currentPackageFilterMode = PackageFilterMode.Dependents;
            ApplyFilteredList(filtered, $"Dependents of {label}");
        }

        /// <summary>Filter to show only the missing dependencies of the selected package.</summary>
        public void ApplyMissingDependenciesFilter(FileEntry file)
        {
            try
            {
                EnsureFilterBaseCaptured();

                // Try to handle as VarPackage first
                if (TryGetPackageFromEntry(file, out VarPackage pkg, out string label) && pkg != null)
                {
                    try { DependencyGraph.EnsureForPackage(pkg.Uid); } catch { }
                    var deps = pkg.RecursivePackageDependencies;
                    if (deps == null || deps.Count == 0)
                    {
                        return;
                    }

                    // Build a list of missing dependency UIDs and create placeholder entries
                    List<string> missingUids = new List<string>();
                    List<FileEntry> filtered = new List<FileEntry>();

                    foreach (var depUid in deps)
                    {
                        VarPackage depPkg = FileManager.GetPackageForDependency(depUid, false);
                        if (depPkg == null)
                        {
                            missingUids.Add(depUid);
                            try
                            {
                                VirtualFileEntry vfe = new VirtualFileEntry(depUid);
                                if (!string.IsNullOrEmpty(vfe.Name) && !string.IsNullOrEmpty(vfe.Path))
                                {
                                    filtered.Add(vfe);
                                }
                                else
                                {
                                    LogUtil.LogError($"[VPB] Invalid VirtualFileEntry created for {depUid}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError($"[VPB] Failed to create VirtualFileEntry for {depUid}: {ex}");
                            }
                        }
                    }

                    if (missingUids.Count == 0)
                    {
                        return;
                    }

                    currentPackageFilterCount = missingUids.Count;
                    currentPackageFilterMasterUid = pkg.Uid;
                    currentPackageFilterMode = PackageFilterMode.Dependencies;
                    ApplyFilteredList(filtered, $"Missing Dependencies ({missingUids.Count})");
                }
                // Handle scene files
                else if (file != null && (file.Path?.ToLowerInvariant().EndsWith(".json") ?? false))
                {
                    var deps = GallerySortManager.ExtractSceneDependencies(file);
                    if (deps == null || deps.Count == 0)
                    {
                        return;
                    }

                    // Build a list of missing dependencies
                    List<string> missingDeps = new List<string>();
                    List<FileEntry> filtered = new List<FileEntry>();

                    foreach (var dep in deps)
                    {
                        VarPackage depPkg = FileManager.GetPackageForDependency(dep, false);
                        if (depPkg == null)
                        {
                            missingDeps.Add(dep);
                            try
                            {
                                VirtualFileEntry vfe = new VirtualFileEntry(dep);
                                if (!string.IsNullOrEmpty(vfe.Name) && !string.IsNullOrEmpty(vfe.Path))
                                {
                                    filtered.Add(vfe);
                                }
                                else
                                {
                                    LogUtil.LogError($"[VPB] Invalid VirtualFileEntry created for {dep}");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogUtil.LogError($"[VPB] Failed to create VirtualFileEntry for {dep}: {ex}");
                            }
                        }
                    }

                    if (missingDeps.Count == 0)
                    {
                        return;
                    }

                    currentPackageFilterCount = missingDeps.Count;
                    currentPackageFilterMasterUid = file.Path;
                    currentPackageFilterMode = PackageFilterMode.Dependencies;
                    ApplyFilteredList(filtered, $"Missing ({missingDeps.Count})");
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] ApplyMissingDependenciesFilter error: " + ex);
            }
        }

        /// <summary>Pop one filter level and return to the previous view.</summary>
        public void NavigateBack()
        {
            if (_filterStack.Count == 0) return;

            FilterFrame frame = _filterStack.Pop();

            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(frame.files);
            currentFilterDesc = frame.desc;
            currentPackageFilterMode = frame.mode;
            currentPackageFilterMasterUid = frame.masterUid;
            currentPackageFilterCount = frame.count;
            filterSearchBaseFiles = frame.searchBase;
            filterSearchLower = frame.searchLower;

            try
            {
                nameFilter = frame.savedNameFilter;
                nameFilterLower = string.IsNullOrEmpty(frame.savedNameFilter) ? "" : frame.savedNameFilter.ToLowerInvariant();
                nameFilterTerms = SplitSearchTerms(frame.savedNameFilter);
                if (titleSearchInput != null) titleSearchInput.text = frame.savedNameFilter;
            }
            catch { }

            filterBaseAnchorKey = null;
            InvalidateGalleryPreHideFileListSnapshot();
            RefreshRecycleGridAfterFilterChange();
            try { UpdateTabs(); } catch { }
            try { UpdatePaginationText(); } catch { }
            ScrollGalleryToTop();
        }

        /// <summary>Clear all filter levels and restore the original unfiltered list.</summary>
        public void ClearPackageFilter()
        {
            if (_filterStack.Count == 0) return;

            InvalidateGalleryPreHideFileListSnapshot();

            // Drain the stack; keep the bottom (outermost) frame for restoration
            FilterFrame bottom = _filterStack.Pop();
            while (_filterStack.Count > 0)
                bottom = _filterStack.Pop();

            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(bottom.files);
            currentFilterDesc = null;
            currentPackageFilterMode = PackageFilterMode.None;
            currentPackageFilterMasterUid = null;
            currentPackageFilterCount = 0;
            filterSearchBaseFiles = null;
            filterSearchLower = "";

            RefreshRecycleGridAfterFilterChange();
            try { UpdateTabs(); } catch { }
            try { UpdatePaginationText(); } catch { }

            filterBaseAnchorKey = null;
            ScrollGalleryToTop();

            // If the user entered filter mode while a top search was active, clearing the filter
            // should return to the full category list (not the search-limited snapshot).
            if (bottom.enteredFromTopSearch)
            {
                try
                {
                    nameFilter = "";
                    nameFilterLower = "";
                    nameFilterTerms = new string[0];
                    if (titleSearchInput != null) titleSearchInput.text = "";
                }
                catch { }
                try
                {
                    if (topSearchBaseFiles != null)
                    {
                        currentFilteredFiles.Clear();
                        currentFilteredFiles.AddRange(topSearchBaseFiles);
                        topSearchBaseFiles = null;
                        RefreshRecycleGridAfterFilterChange();
                        ScrollGalleryToTop();
                        try { UpdatePaginationText(); } catch { }
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    nameFilter = bottom.savedNameFilter;
                    nameFilterLower = string.IsNullOrEmpty(bottom.savedNameFilter) ? "" : bottom.savedNameFilter.ToLowerInvariant();
                    nameFilterTerms = SplitSearchTerms(bottom.savedNameFilter);
                    if (titleSearchInput != null) titleSearchInput.text = bottom.savedNameFilter;
                }
                catch { }
            }
        }

        /// <summary>Returns whether a filter is currently active.</summary>
        public bool IsFilterActive => _filterStack.Count > 0;

        /// <summary>Returns the description of the active filter, or null if none.</summary>
        public string GetFilterDescription => currentFilterDesc;
    }

    /// <summary>Virtual/placeholder file entry for displaying missing dependencies.</summary>
    public class VirtualFileEntry : FileEntry
    {
        public VirtualFileEntry(string uid)
        {
            this.Uid = uid;
            this.Name = uid;
            this.Path = "[MISSING] " + uid;
            this.Size = 0;
            this.LastWriteTime = DateTime.MinValue;
        }

        public override FileEntryStream OpenStream()
        {
            return null; // Virtual entries cannot be opened
        }

        public override FileEntryStreamReader OpenStreamReader()
        {
            return null; // Virtual entries cannot be read
        }

        public override string ToString() => $"[MISSING] {Name}";
    }
}
