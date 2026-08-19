using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            DateTime t = FileManager.lastPackageRefreshTime;
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
        /// <summary>
        /// RefreshFiles builds the non-search filtered list, then applies title search via
        /// <see cref="ApplyTitleSearchToBaseListInMemory"/> (same path as live keystrokes).
        /// Avoids SQL/PassesFilters search diverging from live in-memory results.
        /// </summary>
        private bool _refreshDeferNameFilterToInMemory;
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

        internal void InvalidateCachedPathTabs()
        {
            pathsCached = false;
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
                if (file is PackageListEntry ple)
                {
                    string uid = ple.GetPackageUidForGalleryUserTags();
                    VarPackage resolved = ple.Package;
                    if (resolved != null)
                    {
                        pkg = resolved;
                        label = !string.IsNullOrEmpty(resolved.Uid) ? resolved.Uid : uid;
                        return true;
                    }
                    if (!string.IsNullOrEmpty(uid))
                    {
                        try
                        {
                            VarPackage live = FileManager.GetPackage(uid, ensureInstalled: false);
                            if (live != null)
                            {
                                pkg = live;
                                label = uid;
                                VpbPackageIndexDiagnostics.Log(uid, "navResolve", "source=PackageListEntry_fallback");
                                return true;
                            }
                        }
                        catch { }
                        VpbPackageIndexDiagnostics.Log(uid, "navResolveFail", "source=PackageListEntry");
                    }
                }
                if (file is MissingPackageListEntry mple && !string.IsNullOrEmpty(mple.RequestedUid))
                {
                    try
                    {
                        VarPackage live = FileManager.GetPackage(mple.RequestedUid, ensureInstalled: false);
                        if (live != null)
                        {
                            pkg = live;
                            label = mple.RequestedUid;
                            VpbPackageIndexDiagnostics.Log(mple.RequestedUid, "navResolve", "source=MissingPackageListEntry");
                            return true;
                        }
                    }
                    catch { }
                    VpbPackageIndexDiagnostics.Log(mple.RequestedUid, "navResolveFail", "source=MissingPackageListEntry");
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
        /// Package deps/dependents/missing filter enter/leave: title + side chrome only.
        /// Never rebuilds side-tab button lists (see <see cref="UpdateTabs"/>).
        /// </summary>
        private void RefreshChromeAfterPackageFilterListChange()
        {
            if (titleText != null)
            {
                bool showTitle = !IsFilterActive;
                if (titleText.gameObject.activeSelf != showTitle)
                    titleText.gameObject.SetActive(showTitle);
                if (showTitle)
                {
                    if (settingsListViewActive)
                        titleText.text = VPBTranslation.T("settings.title", "Settings");
                    else
                        titleText.text = currentCategoryTitle;
                }
            }

            try { SyncCategoryQuickSwitchChrome(); } catch { }
            try { UpdateSideContextActions(); } catch { }
            try { ApplyTitleBarResponsiveLayout(ChromeScale); } catch { }
            try { ApplyFooterOverflowLayout(ChromeScale); } catch { }
            try { UpdateSideButtonsVisibility(); } catch { }
            MarkGalleryPaneChromeDirty();
        }

        /// <summary>
        /// Rebind currently visible rows without rebuilding filters/sort/list contents.
        /// Used when badge-only state changes (e.g. temporary scan-whitelist UID overrides).
        /// </summary>
        internal void RefreshVisibleGridVisualsOnly()
        {
            if (!HasLoadedContent || !IsVisible) return;
            if (recyclingGrid == null) return;
            try { recyclingGrid.Refresh(); } catch { }
            try { RefreshSelectionVisuals(); } catch { }
            try { UpdateEmptyGridState(); } catch { }
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
            if (string.IsNullOrEmpty(targetUid)) return uids;

            // Prefer SQLite reverse edges when available (same source as count when ready).
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

            // Match ResolveDependentCount: when SQL stale/unavailable, use graph/bulk edges.
            try
            {
                HashSet<string> deps;
                if (DependencyGraph.TryGetTransitiveDependents(targetUid, out deps) && deps != null)
                {
                    foreach (var d in deps)
                        if (!string.IsNullOrEmpty(d)) uids.Add(d);
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
            try { fromTopSearch = HasActiveNameFilter(); } catch { }

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
                    ClearNameFilterState();
                    SetTitleSearchInputTextWithoutNotify(titleSearchInput, "", _titleBarSearchOnValueChanged);
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

            // Package filter only changes the file grid + title/footer chrome.
            // Full UpdateTabs() rebuilds every side-tab button list (can take seconds) — avoid here.
            try { RefreshChromeAfterPackageFilterListChange(); } catch { }
            try { UpdatePaginationText(); } catch { }
            RefreshRecycleGridAfterFilterChange();
            ScrollGalleryToTop();
            try { SyncBrowseFilterChipChrome(); } catch { }
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
            var query = GallerySearchQuery.Parse(searchQuery);
            bool needSearch = query != null && !query.IsEmpty;
            var tagKeys = needSearch ? BuildTagKeyLookupForSearch(query) : null;
            var result = new List<FileEntry>();

            for (int i = 0; i < source.Count; i++)
            {
                FileEntry e = source[i];
                if (e == null) continue;

                if (!PassesLiveStarFilters(e))
                    continue;

                if (!needSearch)
                {
                    result.Add(e);
                    continue;
                }

                if (MatchesFileEntryBySearchQuery(e, query, tagKeys))
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
            StopCo(ref filterRestoreCoroutine);
            filterRestoreCoroutine = StartCoroutine(RestoreFilterScrollAnchorNextFrame());
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
            GallerySearchQuery searchQ = nameFilterQuery ?? GallerySearchQuery.Empty;
            bool hasNameFilt = searchQ != null && !searchQ.IsEmpty;

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
                    if (hasNameFilt && !MatchesPackageFallbackSearch(searchQ, pkg != null ? pkg.Uid : "", pkg != null ? pkg.Path : "", ip)) continue;

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
                        if (PackageHidePrefs.IsExcludedByGalleryHideFilter(row))
                        {
                            VpbPackageIndexDiagnostics.Log(uid, "galleryRowSkip", "reason=hide_filter sqlPath='" + (r.VarPath ?? "") + "'");
                            continue;
                        }
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
                    else
                    {
                        VpbPackageIndexDiagnostics.Log(uid, "galleryBuildRow", "row=missing_no_registry");
                        result.Add(new MissingPackageListEntry(uid));
                    }
                }
                catch
                {
                    result.Add(new MissingPackageListEntry(uid));
                }
                if (VpbPackageIndexDiagnostics.ShouldTrace(uid))
                {
                    bool added = result.Exists(e =>
                        (e is PackageListEntry ple && string.Equals(ple.GetPackageUidForGalleryUserTags(), uid, StringComparison.OrdinalIgnoreCase))
                        || (e is MissingPackageListEntry m && string.Equals(m.RequestedUid, uid, StringComparison.OrdinalIgnoreCase)));
                    VpbPackageIndexDiagnostics.Log(uid, "galleryBuildRow", added ? "row=added" : "row=missing_entry");
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
            StopCo(ref posePeopleIndexCoroutine);
            posePeopleIndexCoroutine = StartCoroutine(PosePeopleIndexRoutine(groupId));
        }

        private IEnumerator PosePeopleIndexRoutine(string groupId)
        {
            int processed = 0;
            int sinceSave = 0;
            float lastUiUpdate = Time.realtimeSinceStartup;
            float lastRefresh = Time.realtimeSinceStartup;

            while (true)
            {
                if (groupId != posePeopleIndexGroupId) yield break;

                FileEntry entry = null;
                lock (posePeopleIndexLock)
                {
                    if (posePeopleIndexQueue.Count > 0) entry = posePeopleIndexQueue.Dequeue();
                }

                if (entry == null) break;

                // This will do the expensive scan only once and persist it.
                try { GetPosePeopleCount(entry); } catch { }

                processed++;
                sinceSave++;

                // Periodically update UI counters (non-blocking)
                if (Time.realtimeSinceStartup - lastUiUpdate > 0.35f)
                {
                    lastUiUpdate = Time.realtimeSinceStartup;
                    try { UpdateTabs(); } catch { }
                }

                // Save occasionally
                if (sinceSave >= 100)
                {
                    sinceSave = 0;
                    try { PosePeopleCountIndex.Instance.Save(); } catch { }
                }

                // If filtering by Dual/Single, re-run refresh sometimes so list becomes accurate as we learn counts.
                // NOTE: don't call RefreshFiles() here; it resets currentLoadingGroupId and would cancel this coroutine.
                // We instead just refresh the tab labels and let the user trigger a refresh if needed.
                if (posePeopleFilter != PosePeopleFilter.All && (processed % 250) == 0)
                {
                    if (Time.realtimeSinceStartup - lastRefresh > 1.0f)
                    {
                        lastRefresh = Time.realtimeSinceStartup;
                        try { UpdateTabs(); } catch { }
                    }
                }

                // Yield every few items to keep UI responsive.
                if ((processed % 10) == 0) yield return null;
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

        private int GetPosePeopleCount(FileEntry entry)
        {
            if (entry == null) return 1;

            // Only .json poses can be dual/multi; everything else is treated as Single.
            string entryPath = null;
            try { entryPath = entry.Path; } catch { entryPath = null; }
            if (string.IsNullOrEmpty(entryPath) || !entryPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return 1;

            string key = null;
            try { key = !string.IsNullOrEmpty(entry.Uid) ? entry.Uid : entry.Path; } catch { key = entry.Path; }
            if (string.IsNullOrEmpty(key)) return 1;

            // Persistent index for .var (and any UID-based entries)
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

            lock (posePeopleCountCacheLock)
            {
                // Cap cache size to avoid unbounded growth
                if (posePeopleCountCache.Count > 20000) posePeopleCountCache.Clear();
                posePeopleCountCache[key] = count;
            }

            try
            {
                // Persist discovered counts so VAR pose browsing doesn't need rescans next time.
                PosePeopleCountIndex.Instance.Set(key, count);
            }
            catch { }

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
            bool isCustomItem = !isVarPackageEntry && ClothingLoadingUtils.IsLooseCustomClothingItemPath(norm);
            bool isCustomPresetLoose = !isVarPackageEntry && ClothingLoadingUtils.IsLooseCustomClothingPresetPath(norm);

            ClothingLoadingUtils.ResourceKind k;
            ClothingLoadingUtils.ResourceGender g;
            ClothingLoadingUtils.ClassifyClothingHairPath(p, out k, out g);
            if (k != ClothingLoadingUtils.ResourceKind.Clothing) return false;

            bool isDecal = ClothingLoadingUtils.IsDecalLikePath(p);

            // Default view shows base items only: hide all .vap presets (VAR and custom).
            if (clothingSubfilter == 0)
            {
                if (isPreset) return false;
            }
            else
            {
                bool wantsRealType = ((clothingSubfilter & (ClothingSubfilter.RealClothing | ClothingSubfilter.Presets | ClothingSubfilter.Custom | ClothingSubfilter.CustomPreset | ClothingSubfilter.Items | ClothingSubfilter.Male | ClothingSubfilter.Female)) != 0);
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
                bool wantsCustomPreset = (clothingSubfilter & ClothingSubfilter.CustomPreset) != 0;
                if (wantsPresets) { if (!isPreset || isCustomItem || isCustomPresetLoose) return false; }
                if (wantsCustom) { if (!isCustomItem) return false; }
                if (wantsCustomPreset) { if (!isCustomPresetLoose || !isPreset) return false; }
                // Default-hide presets unless Presets/Custom/Custom Preset toggle is on.
                if (!wantsPresets && !wantsCustom && !wantsCustomPreset) { if (isPreset) return false; }
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
            bool isCustomItem = !isVarPackageEntry && ClothingLoadingUtils.IsLooseCustomHairItemPath(norm);
            bool isCustomPresetLoose = !isVarPackageEntry && ClothingLoadingUtils.IsLooseCustomHairPresetPath(norm);

            ClothingLoadingUtils.ResourceKind k;
            ClothingLoadingUtils.ResourceGender g;
            ClothingLoadingUtils.ClassifyClothingHairPath(p, out k, out g);
            if (k != ClothingLoadingUtils.ResourceKind.Hair) return false;

            // Default view shows base items only: hide all .vap presets (VAR and custom).
            if (hairSubfilter == 0)
            {
                if (isPreset) return false;
            }
            else
            {
                bool wantsPresets = (hairSubfilter & HairSubfilter.Presets) != 0;
                bool wantsCustom = (hairSubfilter & HairSubfilter.Custom) != 0;
                bool wantsCustomPreset = (hairSubfilter & HairSubfilter.CustomPreset) != 0;
                if (wantsPresets) { if (!isPreset || isCustomItem || isCustomPresetLoose) return false; }
                if (wantsCustom) { if (!isCustomItem) return false; }
                if (wantsCustomPreset) { if (!isCustomPresetLoose || !isPreset) return false; }
                // Default-hide presets unless Presets/Custom/Custom Preset toggle is on.
                if (!wantsPresets && !wantsCustom && !wantsCustomPreset) { if (isPreset) return false; }
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

            // History: skip size/source category filters; keep path, search, tags, and live ★ filters
            // (presence + star-count) so title-bar ★ matches visible History rows.
            if (activeContentType == ContentType.History)
            {
                if (!string.IsNullOrEmpty(currentPackagePathFilter))
                {
                    string folder = TryGetPathFilterFolderForEntry(entry);
                    if (!GalleryPathFilterMatchesFolder(folder, currentPackagePathFilter))
                        return false;
                }
                if (!PassesLiveStarFilters(entry))
                    return false;
                if (HasActiveNameFilter() && !_refreshDeferNameFilterToInMemory)
                {
                    bool skipSqlOwned = nameFilterQuery.RequiresSqlRefresh && IsGallerySqlIndexedSearchEntry(entry);
                    if (!MatchesFileEntryBySearchQuery(entry, nameFilterQuery, GetSearchTagKeysCached(),
                            skipSqlOwnedPredicates: skipSqlOwned))
                        return false;
                }
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

            // Hair subfilter gate. Shares skipClothingGalleryFilters with clothing: when set, both
            // subfilter gates are bypassed because the subfilter was applied upstream (SQL for VAR rows).
            bool isHair = title.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("/Hair", StringComparison.OrdinalIgnoreCase) >= 0
                || cp.IndexOf("\\Hair", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isHair && !skipClothingGalleryFilters)
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

            // Global source filter (early gate). Cheap type check, runs first.
            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
            {
                bool isVarBackedForGate = IsVarBacked(entry);
                if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local && isVarBackedForGate) return false;
                if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var && !isVarBackedForGate) return false;
            }

            if (isAppearance)
            {
                string p = entry.Path ?? "";
                string norm = p.Replace('\\', '/');

                int lastDot = norm.LastIndexOf('.');
                string ext = (lastDot >= 0 && lastDot < norm.Length - 1) ? norm.Substring(lastDot + 1) : "";
                bool isVap = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);

                // Global Local + Appearance folder browse: keep path-scope gate (was under legacy Local toggle).
                if (currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local
                    && AppearanceGenderClassifier.IsAppearanceFolderBrowsePath(cp)
                    && !AppearanceGenderClassifier.EntryMatchesAppearanceBrowseScope(entry, cp, currentPaths))
                    return false;

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

            // Rating: star-count tab + title-bar ★ presence (one GetRating).
            if (!PassesLiveStarFilters(entry))
                return false;

            if (HasLicenseFilter() && !PassesLicenseFilter(entry))
                return false;

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

            // Scene Local is global Source Local (early gate). No per-category override.

            // Name Filter (bare terms OR user tags; tag:/creator:/status structured).
            // Only skip SQL-owned time/loaded/tagged for VAR index rows — loose files need in-memory time match.
            // When deferring, RefreshFiles applies the same in-memory pass as live SetNameFilter after the list builds.
            if (HasActiveNameFilter() && !_refreshDeferNameFilterToInMemory)
            {
                bool skipSqlOwned = nameFilterQuery.RequiresSqlRefresh && IsGallerySqlIndexedSearchEntry(entry);
                if (!MatchesFileEntryBySearchQuery(entry, nameFilterQuery, GetSearchTagKeysCached(),
                        skipSqlOwnedPredicates: skipSqlOwned))
                    return false;
            }

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

            // Gallery SQLite user tags. Include/exclude filter always live (orthogonal to F/T work mode).
            // FilterUntagged / FilterTaggedOnly browse is exclusive. VAR rows from SQLite bulk query already match tags;
            // loose Custom/Saves files merged afterward must still be checked (same keys as gallery_item_user_tag).
            if (activeContentType == ContentType.Category && VpbSqlite3.IsAvailable
                && (_userTagAvailMode == UserTagAvailMode.FilterUntagged
                    || _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly
                    || IsUserTagIncludeExcludeFilterArmed()))
            {
                VarFileEntry vfeUt = entry as VarFileEntry;
                bool bulkSqlAlreadyFilteredVar =
                    (_refreshSqliteBulkIncludedUserTagGridFilter || System.Threading.Thread.VolatileRead(ref _refreshWorkerFallbackUserTagPrefilterFlag) != 0)
                    && vfeUt != null
                    && vfeUt.Package != null;

                if (!bulkSqlAlreadyFilteredVar)
                {
                    string catUt = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                    string pkgK, ipK;
                    if (!TryGetGalleryRowKeysForUserTags(entry, out pkgK, out ipK)) return false;
                    if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                    {
                        if (!VpbLocalDatabase.TryGalleryRowHasNoUserTags(catUt, pkgK, ipK))
                        {
                            string selKeyUt = GetSelectionIdentityKey(entry, false);
                            bool keepTaggedVisible = !string.IsNullOrEmpty(selKeyUt)
                                && ((selectedFilePaths != null && selectedFilePaths.Contains(selKeyUt))
                                    || _untaggedTaggedPinKeys.Contains(selKeyUt));
                            if (!keepTaggedVisible) return false;
                        }
                    }
                    else if (_userTagAvailMode == UserTagAvailMode.FilterTaggedOnly)
                    {
                        if (VpbLocalDatabase.TryGalleryRowHasNoUserTags(catUt, pkgK, ipK))
                            return false;
                    }
                    else
                    {
                        if (activeUserTags != null && activeUserTags.Count > 0)
                        {
                            if (!VpbLocalDatabase.TryGalleryRowMatchesUserTags(catUt, pkgK, ipK, activeUserTags, UserTagFilterRequiresAllTags()))
                                return false;
                        }
                        if (excludedUserTags != null && excludedUserTags.Count > 0)
                        {
                            if (!VpbLocalDatabase.TryGalleryRowHasNoneOfUserTags(catUt, pkgK, ipK, excludedUserTags))
                                return false;
                        }
                    }
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
            while (!Gallery.IsSuppressed())
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

            if (!Gallery.IsSuppressed())
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
            // Category switch / full reload owns this refresh — kill keystroke debounce so it cannot
            // start a second RefreshFiles after we begin loading.
            try { CancelTitleSearchSqlDebounce(); } catch { }
            try { CancelTitleSearchInMemoryDebounce(); } catch { }
            _refreshDeferNameFilterToInMemory = false;

            // Clear any active dependency filter when refreshing
            ClearPackageFilter();
            // Reset in-memory top search base; RefreshFiles rebuilds the list.
            // Title-bar SQL search may keep the snapshot so clear-search stays instant.
            if (_keepTopSearchBaseAcrossRefresh)
            {
                _keepTopSearchBaseAcrossRefresh = false;
            }
            else
            {
                topSearchBaseFiles = null;
                _topSearchBaseIsClean = false;
            }

            // Inactive parent → recycling grid viewport 0. Defer until Show.
            if (_floatsOnly)
            {
                refreshOnNextShow = true;
                CompletePaneLoadTimingIfPending("(floats-only)");
                return;
            }

            // Check if gallery auto-refresh is suppressed (during scene/preset loading)
            if (Gallery.IsSuppressed())
            {
                LogUtil.Log("[VPB] GalleryPanel.RefreshFiles: SKIPPED (suppressed)");
                CompletePaneLoadTimingIfPending("(refresh suppressed)");
                return;
            }
            
            if (settingsListViewActive)
            {
                RefreshInternalSettingsListRows(keepScroll);
                return;
            }

            // Reset the retry guard on user-triggered refreshes so future loads can retry again.
            // When called from RetryRefreshAfterNoCacheDelay (isRetry=true) we intentionally keep
            // _cacheRetryPending=true so that the retry run does NOT spawn yet another retry.
            if (!isRetry)
                _cacheRetryPending = false;

            StopCo(ref thumbnailCacheCoroutine);
            if (pendingThumbnailCacheJobs != null) pendingThumbnailCacheJobs.Clear();
            _thumbCacheTotalEnqueued = 0;
            _thumbCacheSaved = 0;
            _nextThumbPriority = 0;
            StopCo(ref _deferredGallerySideTabsCoroutine);
            StopCo(ref _sideTabsTagCountSliceCo);
            StopCo(ref _appearanceLooseMergeCo);
            StopCo(ref _historyModeCountsCo);
            StopCo(ref _earlyMetaApplyCoroutine);
            // Quiet background refresh keeps visible thumbs; do not cancel the active image group.
            if (!_quietGalleryRefresh)
            {
                // Rotate the group ID here (synchronously) so that any in-flight thumbnail callbacks
                // from the old category fail the capturedGroupId == currentLoadingGroupId guard and
                // don't pollute the new session. The coroutine's yield-return-null would be too late.
                if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
                    CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
                currentLoadingGroupId = Guid.NewGuid().ToString();
            }
            unchecked { _deferredSubPaneSessionId++; }
            System.Threading.Interlocked.Increment(ref galleryFileRefreshSequence);
            StopCo(ref refreshCoroutine);
            StopCo(ref _refreshHistoryLightCo);
            _boundCategoryNavSessionForCurrentRefresh = _categoryTypeNavStopwatch != null ? _categoryTypeNavTargetSession : 0;
            _refreshFilesDebugSource = refreshDebugSource;
            if (!_quietGalleryRefresh)
                ShowLoadingOverlay(null);
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
            if (settingsListViewActive)
            {
                LogPackageDeltaSkip("settings_open");
                return false;
            }

            // Path filter folder deleted in Explorer — incremental delta cannot rebuild loose Custom/Saves.
            if (TryClearStalePackagePathFilter())
            {
                try
                {
                    LogUtil.Log("[VPB.Gallery.Delta] ApplyPackageDelta full RefreshFiles (stale Path filter cleared)");
                }
                catch { }
                pathsCached = false;
                RefreshFiles(true);
                return true;
            }

            // If we have never loaded, the scan just completed and we have a full PackagesByUid
            // for the first time – do a clean initial load now.
            if (!hasLoadedContent || recyclingGrid == null || scrollRect == null)
            {
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
            bool hasRemovals  = removed != null && removed.Count > 0;
            bool hasAdditions = added   != null && added.Count   > 0;
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
            StopCo(ref refreshCoroutine);
            StopCo(ref _earlyMetaApplyCoroutine);

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
                GallerySearchQuery searchQ = nameFilterQuery ?? GallerySearchQuery.Empty;
                bool hasNameFilt = searchQ != null && !searchQ.IsEmpty;

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
                        if (hasNameFilt && !MatchesPackageFallbackSearch(searchQ, pkg != null ? pkg.Uid : "", pkg != null ? pkg.Path : "", ip)) continue;

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
                        // SortFiles applies hide-old unless SQL already filtered via pkg.is_newest.
                        GallerySortManager.Instance.SortFiles(currentFilteredFiles, sortState, _fileListHadSqlPkgVersionFilter);
                        GallerySortManager.Instance.SortFiles(lastFilteredFiles, sortState, _fileListHadSqlPkgVersionFilter);
                        if (_browseOldVersionsCycle == BrowseFilterCycle.Only && !_fileListHadSqlPkgVersionFilter)
                        {
                            try { GallerySortManager.ApplyOldVersionsOnlyFilter(currentFilteredFiles); } catch { }
                            try { GallerySortManager.ApplyOldVersionsOnlyFilter(lastFilteredFiles); } catch { }
                        }
                    }

                    changed = true;
                    lastPackageDeltaChangedGrid = true;
                }
            }

            if (skippedForNoCache && !changed && !Gallery.IsSuppressed() && !_cacheRetryPending)
            {
                _cacheRetryPending = true;
                StartCoroutine(RetryRefreshAfterNoCacheDelay());
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

            // Grid mutated in place: bump the sub-pane session so the clothing chip-count memo
            // (keyed on _deferredSubPaneSessionId) recomputes against the already-updated cat_mem
            // index instead of returning its pre-change cache.
            unchecked { _deferredSubPaneSessionId++; }

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
                    TryApplyAppearanceFacetCountsFromSql();
                tagsCached = true;
            }
            else
                tagsCached = false;
            userTagsCached = false;
            pathsCached = false;
            RefreshSideTabsAfterPackageDelta();

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
            if (!VamOnDemandLoader.IsMainThread()) return;
            InvalidateSharedSideMetaIfPackageScanAdvanced();
            categoriesCached = false;
            creatorsCached = false;
            _deferSideTabCountsForceRefresh = true;
            if (!IsVisible && !hasLoadedContent) return;
            if (_packageDeltaSideTabsCoroutine != null) return;
            _packageDeltaSideTabsCoroutine = StartCoroutine(CoRefreshSideTabsAfterPackageDelta());
        }

        private IEnumerator CoRefreshSideTabsAfterPackageDelta()
        {
            yield return null;
            _packageDeltaSideTabsCoroutine = null;
            try { CacheCategoryCounts(); } catch { }
            try { CacheCreators(); } catch { }
            // ApplyPackageDelta already cleared userTagsCached; fill amounts once cat_mem is current.
            try { CacheUserTagsSideTab(); } catch { }
            if (!IsVisible && !hasLoadedContent) yield break;
            try { UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: true); } catch { }
        }

        /// <summary>Key for <see cref="GalleryFileListSnapshotCache"/> when the full enumeration result is reproducible from panel state.</summary>
        private bool TryBuildFileListSnapshotCacheKey(out string key)
        {
            key = null;
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
                sb.Append((int)currentGlobalSourceFilter).Append('\u001E');
                sb.Append((int)currentGlobalSourceFilter).Append('\u001E');
                try
                {
                    sb.Append((int)currentGlobalSourceFilter).Append('\u001E');
                }
                catch
                {
                    sb.Append("0").Append('\u001E');
                }
                sb.Append(currentRatingFilter ?? "").Append('\u001E');
                sb.Append(currentLicenseFilter ?? "").Append('\u001E');
                sb.Append(currentSizeFilter ?? "").Append('\u001E');
                sb.Append(categoryFilter ?? "").Append('\u001E');
                sb.Append(creatorFilter ?? "").Append('\u001E');
                sb.Append(tagFilter ?? "").Append('\u001E');
                sb.Append((char)('0' + (int)_ratingPresenceFilterMode)).Append('\u001E');
                sb.Append((VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages) ? '1' : '0').Append('\u001E');
                sb.Append(((int)_browseHiddenCycle).ToString()).Append('\u001E');
                sb.Append(((int)_browseAlwaysLoadedCycle).ToString()).Append('\u001E');
                sb.Append(((int)_browseOldVersionsCycle).ToString()).Append('\u001E');
                sb.Append(((int)_browseLoadedMode).ToString()).Append('\u001E');
                sb.Append(((int)_browseUnusedCycle).ToString()).Append('\u001E');
                try
                {
                    sb.Append((Settings.Instance != null && Settings.Instance.HideOldVersions != null && Settings.Instance.HideOldVersions.Value) ? '1' : '0').Append('\u001E');
                }
                catch
                {
                    sb.Append('0').Append('\u001E');
                }
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
                sb.Append((int)_userTagAvailMode).Append('\u001E');
                if (IsUserTagIncludeFilterArmed())
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
                // Excluded (none-of) user tags must vary the key too, else toggling an exclude reuses the
                // previously cached, unfiltered list and the exclusion appears to do nothing.
                if (IsUserTagExcludeFilterArmed())
                {
                    var xarr = new List<string>(excludedUserTags);
                    xarr.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < xarr.Count; i++)
                    {
                        sb.Append(xarr[i] ?? "");
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
        private bool RefreshFilesRoutineCanFastAppendSqliteBulkList(bool wantsPoseCountsLocal)
        {
            if (HasRatingPresenceFilter()) return false;
            if (!string.IsNullOrEmpty(currentRatingFilter)) return false;
            // License filter is applied in SQL (pkg.license); in-memory PassesLicenseFilter no-ops when flag set.
            if (HasLicenseFilter() && !_fileListHadSqlLicenseFilter) return false;
            if (!string.IsNullOrEmpty(currentSizeFilter)) return false;
            // bulk (cat_mem, VAR-only) is fast-appended without per-entry PassesFilters, where the source gate lives;
            // a bulk AddRange under Source:Local would leak every var row, so force the gated drain when it's active.
            if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All) return false;
            // Title search: SQL PathAndName is a pre-filter only. Live typing uses MatchesFileEntryBySearchQuery
            // (scope + creator/uid/user-tag + exclude). Fast-append skipped PassesFilters → preset restore
            // showed more hits than when the filter was saved. Always re-check via drain.
            if (HasActiveNameFilter()) return false;
            // activeTags still applied in SQL; keep fast-append when tags-only.
            if (wantsPoseCountsLocal || posePeopleFilter != PosePeopleFilter.All) return false;
            // LoadedOnly/UnloadedOnly is applied in the SQLite query via loadedState.

            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
            if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (appearanceSubfilter != 0) return false;
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

                // Local (non-package) history row, e.g. a loose Saves/scene scene. These have no
                // package UID, so build a loose SystemFileEntry directly from the recorded path.
                if (string.IsNullOrEmpty(r.PackageUid))
                {
                    string localPath = !string.IsNullOrEmpty(r.ListPath) ? r.ListPath : r.ItemUsageKey;
                    if (string.IsNullOrEmpty(localPath)) continue;

                    if (wantsLoadedStateForIndexMain == 0) continue; // loose files are always "loaded"

                    SystemFileEntry sfe;
                    try { sfe = new SystemFileEntry(localPath); }
                    catch { continue; }
                    if (!sfe.Exists) continue;
                    bulk.Add(sfe);
                    continue;
                }

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

            StopCo(ref refreshCoroutine);
            StopCo(ref _earlyMetaApplyCoroutine);
            StopCo(ref _refreshHistoryLightCo);

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
            if (activeContentType != ContentType.History) return;
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

        private void SyncHistoryBrowseFailureFlagsFromStats(ContentType contentSnap, VpbLocalDatabase.GalleryCategoryQueryStats stats, GallerySearchQuery searchSnapshot)
        {
            string[] legacy = searchSnapshot != null && !searchSnapshot.IsEmpty
                ? new string[] { "1" }
                : new string[0];
            SyncHistoryBrowseFailureFlagsFromStats(contentSnap, stats, legacy);
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
            GallerySearchQuery searchSnap = nameFilterQuery ?? GallerySearchQuery.Empty;
            string[] nameTerms = searchSnap.BroadTermsArray();

            int wantsLoadedStateForIndexMain = -1;
            try
            {
                if (FilesSortWantsLoadedOnly()) wantsLoadedStateForIndexMain = 1;
                else if (FilesSortWantsUnloadedOnly()) wantsLoadedStateForIndexMain = 0;
            }
            catch { }

            List<FileEntry> bulkResult = null;
            var workerDone = new int[1];
            bool historyQuerySucceeded = false;
            string historyRejectReason = null;
            bool hadNameFilter = searchSnap != null && !searchSnap.IsEmpty;

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
                    if (!VpbLocalDatabase.TryQueryGalleryHistoryRows(histMode, searchSnap, idxRows, out histStats))
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

                if (layoutMode == GalleryLayoutMode.List || settingsListViewActive)
                {
                    recyclingGrid.fixedColumns = 1;
                    recyclingGrid.fixedBottomChromePx = 0f;
                    recyclingGrid.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh: true);
                    recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh: true);
                }
                else
                {
                    ApplyGridRecyclingLayoutConfig(recyclingGrid, GridColumnCount, deferRefresh: true);
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
                    cachedCreators.Clear();
                    if (earlyNewCreators != null && earlyNewCreators.Count > 0)
                    {
                        if (cachedCreators.Capacity < earlyNewCreators.Count)
                            cachedCreators.Capacity = earlyNewCreators.Count;
                        cachedCreators.AddRange(earlyNewCreators);
                    }
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
            while (!Gallery.IsSuppressed())
            {
                long scanBin = FileManager.lastPackageRefreshTime.ToBinary();
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
            // Quiet mode: keep frozen display cells — SetItemCount(0) would blank the viewport.
            if (!_quietGalleryRefresh && contentGO != null)
            {
                if (recyclingGrid == null) recyclingGrid = contentGO.GetComponent<RecyclingGridView>();
                if (recyclingGrid != null)
                {
                    if (layoutMode == GalleryLayoutMode.List || settingsListViewActive)
                    {
                        recyclingGrid.fixedColumns = 1;
                        recyclingGrid.fixedBottomChromePx = 0f;
                        recyclingGrid.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh: true);
                        recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh: true);
                    }
                    else
                    {
                        ApplyGridRecyclingLayoutConfig(recyclingGrid, GridColumnCount, deferRefresh: true);
                    }
                    recyclingGrid.SetItemCount(0); // Clear initially — single Refresh after deferred config
                }
            }
            
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            // Live keystrokes: in-memory filter on the non-search base list.
            // Preset/RefreshFiles used to bake search into SQL — different (often looser) results.
            // Defer normal title search to ApplyTitleSearchToBaseListInMemory after build (same matcher).
            // Status windows (loaded/tagged) still need SQL (RequiresSqlRefresh).
            GallerySearchQuery searchQueryLive = nameFilterQuery ?? GallerySearchQuery.Empty;
            bool deferNameToInMemory = false;
            try
            {
                deferNameToInMemory = searchQueryLive != null
                    && !searchQueryLive.IsEmpty
                    && !searchQueryLive.RequiresSqlRefresh;
            }
            catch { deferNameToInMemory = false; }
            _refreshDeferNameFilterToInMemory = deferNameToInMemory;

            GallerySearchQuery searchQuerySnap = searchQueryLive;
            if (deferNameToInMemory)
                searchQuerySnap = GallerySearchQuery.Empty;
            else
            {
                try
                {
                    string scope = VPBConfig.Instance != null
                        ? VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope)
                        : "PathAndName";
                    if (scope != "PathAndName" && searchQueryLive != null && !searchQueryLive.IsEmpty)
                        searchQuerySnap = searchQueryLive.WithoutBroadTerms();
                }
                catch { searchQuerySnap = searchQueryLive; }
            }
            string[] nameTerms = searchQuerySnap.BroadTermsArray();
            // Worker fallback / diagnostics: live query still "has" a name filter when deferred.
            bool hasNameFilter = searchQueryLive != null && !searchQueryLive.IsEmpty
                && !deferNameToInMemory;

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
                StopCo(ref posePeopleIndexCoroutine);
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
            // Always build into reusable scratch — snapshot cache Put clones; never store _refreshBuildFiles itself.
            _refreshBuildFiles.Clear();
            List<FileEntry> files = _refreshBuildFiles;
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
                    if (files.Capacity < o.currentFilteredFiles.Count)
                        files.Capacity = o.currentFilteredFiles.Count;
                    files.AddRange(o.currentFilteredFiles);
                    snapList = files;
                    fileListFromCache = true;
                    fileListFromSibling = true;
                    break;
                }
            }
            if (!bypassFileListCache && !fileListFromCache && canFileListCache)
                fileListFromCache = GalleryFileListSnapshotCache.TryCopyInto(fileListSnapKey, files);
            if (fileListFromCache) snapList = files;
            if (fileListFromCache)
                _refreshDeferNameFilterToInMemory = false;
            if (fileListFromSibling && canFileListCache && fileListSnapKey != null && files.Count > 0)
                GalleryFileListSnapshotCache.Put(fileListSnapKey, files);

            SortState fileListSortSnapForWorker = null;
            int[] sqliteBulkSortedOnWorkerFlag = null;
            int[] sqlPkgVersionFilterAppliedFlag = null;
            int[] sqlLicenseFilterAppliedFlag = null;
            int[] sysLooseFilesAddedCount = null;
            long refreshDrainWallMs = 0;
            if (!fileListFromCache)
            {
                fileListSortSnapForWorker = GetSortState("Files").Clone();
                sqliteBulkSortedOnWorkerFlag = new int[1];
                sqlPkgVersionFilterAppliedFlag = new int[1];
                sqlLicenseFilterAppliedFlag = new int[1];
                sysLooseFilesAddedCount = new int[1];
                _fileListHadSqlPkgVersionFilter = false;
                _fileListHadSqlLicenseFilter = false;
            }
            else
            {
                // Sibling/cache snapshot already matched browse Old-versions mode in cache key.
                _fileListHadSqlPkgVersionFilter = _browseOldVersionsCycle != BrowseFilterCycle.Off
                    || (Settings.Instance != null && Settings.Instance.HideOldVersions != null
                        && Settings.Instance.HideOldVersions.Value);
                _fileListHadSqlLicenseFilter = HasLicenseFilter();
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
                            bool packageOnlyCreators = string.Equals(_bExtension, "varpkg", StringComparison.OrdinalIgnoreCase)
                                || VpbLocalDatabase.IsGalleryAllVarPseudoCategory(_bCategoryTitle);
                            if (packageOnlyCreators)
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
                                foreach (var e in exts2)
                                {
                                    if (string.IsNullOrEmpty(e)) continue;
                                    string et = e.Trim();
                                    if (et.Length == 0 || Gallery.IsGalleryPseudoExtensionToken(et)) continue;
                                    tExts.Add(et);
                                }
                                bool everythingExtForCreators = Gallery.IsEverythingCategoryExtension(_bExtension)
                                    || Gallery.IsEverythingCategoryName(_bCategoryTitle);

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
                                            string fileExt = ip.Substring(dot + 1);
                                            if (everythingExtForCreators && Gallery.IsEverythingExcludedPreviewExtension(fileExt)) continue;
                                            if (!everythingExtForCreators && !tExts.Contains(fileExt)) continue;
                                            // EVERYTHING: match all non-preview internals (category.paths are loose-disk roots only).
                                            bool match = everythingExtForCreators;
                                            if (!match)
                                            {
                                                if (_bPaths != null && _bPaths.Count > 0)
                                                { for (int k = 0; k < _bPaths.Count; k++) if (GalleryInternalPathStartsWithPrefix(ip, _bPaths[k])) { match = true; break; } }
                                                else if (!string.IsNullOrEmpty(_bPath))
                                                    match = GalleryInternalPathStartsWithPrefix(ip, _bPath);
                                                else match = true;
                                            }
                                            if (match) { int cur; counts.TryGetValue(pkg.Creator, out cur); counts[pkg.Creator] = cur + 1; }
                                        }
                                    }
                                }
                            }
                            var creatorList = new List<CreatorCacheEntry>(counts.Count > 0 ? counts.Count : 16);
                            FillCreatorCacheEntriesSorted(counts, creatorList);
                            earlyNewCreators = creatorList;
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
                    if (FilesSortWantsLoadedOnly()) wantsLoadedStateForIndexMain = 1;
                    else if (FilesSortWantsUnloadedOnly()) wantsLoadedStateForIndexMain = 0;
                }
                catch { }
                int pkgVersionFilterForIndexMain = VpbLocalDatabase.PkgVersionFilterOff;
                try
                {
                    if (_browseOldVersionsCycle == BrowseFilterCycle.Apply)
                        pkgVersionFilterForIndexMain = VpbLocalDatabase.PkgVersionFilterNewestOnly;
                    else if (_browseOldVersionsCycle == BrowseFilterCycle.Only)
                        pkgVersionFilterForIndexMain = VpbLocalDatabase.PkgVersionFilterOldOnly;
                    else if (Settings.Instance != null && Settings.Instance.HideOldVersions != null
                        && Settings.Instance.HideOldVersions.Value)
                        pkgVersionFilterForIndexMain = VpbLocalDatabase.PkgVersionFilterNewestOnly;
                }
                catch { }
                string licenseFilterForIndexMain = currentLicenseFilter ?? "";
                ContentType activeContentSnap = activeContentType;
                GalleryHistoryFilterMode histFilterSnap = galleryHistoryFilterMode;
                ClothingSubfilter sqliteWorkerClothingSub = clothingSubfilter;
                string pathForIndexMain = currentPath ?? "";
                string workerPathSnap = pathForIndexMain;
                List<string> workerPathsSnap = currentPaths != null ? new List<string>(currentPaths) : null;
                bool appearanceWorkerLocalOnly = titleForIndexMain.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                    && ResolveEffectiveSourceFilterMode(true, pathForIndexMain) == 1;
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
                int[] sqlHairGalleryFilterAppliedFlag = new int[1];

                VpbLocalDatabase.GalleryCategoryQueryStats catQueryStats = new VpbLocalDatabase.GalleryCategoryQueryStats();

                bool userTagGridFilterUntaggedSnap = _userTagAvailMode == UserTagAvailMode.FilterUntagged;
                bool userTagGridFilterTaggedOnlySnap = _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly;
                bool userTagIncludeExcludeArmedSnap = !userTagGridFilterUntaggedSnap
                    && !userTagGridFilterTaggedOnlySnap
                    && IsUserTagIncludeExcludeFilterArmed();
                bool userTagFilterIsolateSnap = userTagIncludeExcludeArmedSnap
                    && activeUserTags != null && activeUserTags.Count > 0
                    && UserTagFilterRequiresAllTags();
                HashSet<string> userTagNamesForGridSqlSnap = null;
                if (userTagIncludeExcludeArmedSnap && activeUserTags != null && activeUserTags.Count > 0)
                    userTagNamesForGridSqlSnap = new HashSet<string>(activeUserTags, StringComparer.OrdinalIgnoreCase);
                HashSet<string> excludedUserTagNamesForGridSqlSnap = null;
                if (userTagIncludeExcludeArmedSnap && excludedUserTags != null && excludedUserTags.Count > 0)
                    excludedUserTagNamesForGridSqlSnap = new HashSet<string>(excludedUserTags, StringComparer.OrdinalIgnoreCase);
                int[] refreshDrainUtSqlFilterApplied = { 0 };

                ThreadPool.QueueUserWorkItem((state) =>
                {
                    VpbLocalDatabase.GalleryCreatorFilterCaseInsensitive = creatorFilterCiSnap;
                    var swWorker = System.Diagnostics.Stopwatch.StartNew();
                    bool useSqliteIndex = false;
                    try
                    {
                        // One-shot: fill pkg.license from live packages (ZIP ok on worker).
                        if (!string.IsNullOrEmpty(licenseFilterForIndexMain))
                        {
                            try { VpbLocalDatabase.TryBackfillPkgLicensesFromLivePackagesIfNeeded(); } catch { }
                        }

                        List<VpbLocalDatabase.Row> idxRows = new List<VpbLocalDatabase.Row>();
                        List<string> pathExclusions = null;
                        // SQLite index usage must not depend on snapshot-cache key availability.
                        // Snapshot cache is an optimization; SQLite query is primary fast path.
                        if (VpbSqlite3.IsAvailable
                            && activeContentSnap == ContentType.History)
                        {
                            bool hr = VpbLocalDatabase.TryQueryGalleryHistoryRows(
                                histFilterSnap,
                                searchQuerySnap,
                                idxRows,
                                out catQueryStats);
                            if (!hr)
                                idxRows.Clear();
                            useSqliteIndex = true;
                        }
                        else if (VpbSqlite3.IsAvailable
                            && activeContentSnap == ContentType.Category)
                        {
                            // Package-level listing (ext=varpkg): not in cat_mem — skip category SQL.
                            // Worker still uses TryQueryVarPackageRowsForList below.
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
                                wantsLoadedStateForIndexMain,
                                searchQuerySnap,
                                pathExclusions,
                                pathInclusionsForSql,
                                activeTags,
                                userTagNamesForGridSqlSnap,
                                fileListSortSnapForWorker,
                                userTagGridFilterUntaggedSnap,
                                userTagFilterIsolateSnap,
                                excludedUserTagNamesForGridSqlSnap,
                                pkgVersionFilterForIndexMain,
                                userTagGridFilterTaggedOnlySnap,
                                licenseFilterForIndexMain,
                                sqliteWorkerHairSub);
                            if (useSqliteIndex && catQueryStats.AppliedPkgVersionFilter
                                && sqlPkgVersionFilterAppliedFlag != null)
                                sqlPkgVersionFilterAppliedFlag[0] = 1;
                            if (useSqliteIndex && !string.IsNullOrEmpty(licenseFilterForIndexMain)
                                && sqlLicenseFilterAppliedFlag != null)
                                sqlLicenseFilterAppliedFlag[0] = 1;
                            if (useSqliteIndex && catQueryStats.AppliedHairGallerySubfilter)
                                sqlHairGalleryFilterAppliedFlag[0] = 1;
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

                                    // Appearance: always require look paths — even when skipPathMatch (non-Local).
                                    // Else json|vap package fallback floods SubScene/Scene into Appearance grid.
                                    if (titleForIndexMain.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (IsForbiddenInAppearanceCategory(internalPath)
                                            || !IsAppearanceLookInternalPath(internalPath))
                                            continue;
                                    }
                                    else if (!appearanceWorkerSkipPathMatch
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

                                    // Hair: SQL narrows via BuildHairSubfilterSqlAnd when AppliedHairGallerySubfilter.
                                    // Mirror Clothing — only C# gate when SQL miss, or active subfilter + missing cloth_attr.
                                    bool hairSqlApplied = sqlHairGalleryFilterAppliedFlag[0] != 0;
                                    if (sqliteDrainApplyHairGateOnMain)
                                    {
                                        if (!hairSqlApplied)
                                        {
                                            if (!PassesHairGalleryFiltersForPath(listPath, sqliteWorkerHairSub, true))
                                                continue;
                                        }
                                        else if (sqliteWorkerHairSub != 0)
                                        {
                                            if ((r.ClothingAttrPacked & VpbLocalDatabase.ClothingAttrPresentFlag) != 0)
                                            {
                                                if (!VpbLocalDatabase.HairPackedAttrMatchesSubfilter(r.ClothingAttrPacked, sqliteWorkerHairSub))
                                                    continue;
                                            }
                                            else if (!PassesHairGalleryFiltersForPath(listPath, sqliteWorkerHairSub, true))
                                            {
                                                continue;
                                            }
                                        }
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

                                bool skipHideOld = sqlPkgVersionFilterAppliedFlag != null
                                    && sqlPkgVersionFilterAppliedFlag[0] != 0;
                                if (alreadySorted)
                                {
                                    if (!skipHideOld)
                                        GallerySortManager.ApplyHideOldVersionsFilter(bulk);
                                    if (sqliteBulkSortedOnWorkerFlag != null)
                                        sqliteBulkSortedOnWorkerFlag[0] = 1;
                                }
                                else if (GallerySortManager.TrySortFilesEntryFieldsOnly(bulk, fileListSortSnapForWorker, skipHideOld))
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
                                    && (userTagGridFilterUntaggedSnap
                                        || (userTagNamesForGridSqlSnap != null && userTagNamesForGridSqlSnap.Count > 0))
                                    && !userTagGridFilterTaggedOnlySnap
                                    && !string.IsNullOrEmpty(titleForIndexMain) && VpbSqlite3.IsAvailable)
                                {
                                    var utBuilt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    bool utOk = false;
                                    if (userTagGridFilterUntaggedSnap)
                                        utOk = VpbLocalDatabase.TryBuildCatMemRowKeysWithNoUserTags(titleForIndexMain, utBuilt);
                                    else if (userTagNamesForGridSqlSnap != null && userTagNamesForGridSqlSnap.Count > 0)
                                        utOk = VpbLocalDatabase.TryBuildCatMemRowKeysMatchingUserTags(titleForIndexMain, userTagNamesForGridSqlSnap, utBuilt, userTagFilterIsolateSnap);
                                    if (utOk)
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
                                            bool appliedVarPkgVersion = false;
                                            if (VpbLocalDatabase.TryQueryVarPackageRowsForList(
                                                creatorForIndexMain,
                                                packagePathFilterForIndexMain,
                                                wantsLoadedStateForIndexMain,
                                                nameTerms,
                                                fileListSortSnapForWorker,
                                                rows,
                                                pkgVersionFilterForIndexMain,
                                                licenseFilterForIndexMain,
                                                out appliedVarPkgVersion))
                                            {
                                                if (appliedVarPkgVersion
                                                    && sqlPkgVersionFilterAppliedFlag != null)
                                                    sqlPkgVersionFilterAppliedFlag[0] = 1;
                                                if (!string.IsNullOrEmpty(licenseFilterForIndexMain)
                                                    && sqlLicenseFilterAppliedFlag != null)
                                                    sqlLicenseFilterAppliedFlag[0] = 1;
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
                                            if (hasNameFilter
                                                && !MatchesPackageFallbackSearch(searchQuerySnap, pkg.Uid ?? "", pkg.Path ?? "", null))
                                                continue;
                                            if (!string.IsNullOrEmpty(licenseFilterForIndexMain))
                                            {
                                                string plic = "";
                                                try
                                                {
                                                    if (string.IsNullOrEmpty(pkg.LicenseType))
                                                        pkg.TryEnsureMetaJsonLiteFields();
                                                    plic = pkg.LicenseType ?? "";
                                                }
                                                catch { plic = ""; }
                                                if (!string.Equals(plic, licenseFilterForIndexMain, StringComparison.OrdinalIgnoreCase))
                                                    continue;
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

                                foreach (var pkg in FileManager.PackagesByUid.Values)
                                {
                                if (localLoadingGroupId != currentLoadingGroupId) return;

                                // Use captured snapshot to avoid cross-thread stale reads
                                if (creatorFilterSetForWorker != null && creatorFilterSetForWorker.Count > 0)
                                {
                                    if (string.IsNullOrEmpty(pkg.Creator) || !creatorFilterSetForWorker.Contains(pkg.Creator)) continue;
                                }
                                if (!string.IsNullOrEmpty(packagePathFilterForIndexMain) &&
                                    !GalleryPathFilterMatchesRawPath(pkg.Path, packagePathFilterForIndexMain))
                                {
                                    continue;
                                }

                                List<string> names;
                                List<long> ticks;
                                List<long> sizes;
                                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                                {
                                    skippedForNoCache[0]++;
                                    if (skippedForNoCacheSampleCount < skippedForNoCacheSample.Length)
                                    {
                                        try { skippedForNoCacheSample[skippedForNoCacheSampleCount++] = pkg != null ? (pkg.Uid ?? pkg.Path ?? "") : ""; } catch { }
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

                                    // Appearance package scan: never accept SubScene/Scene/other Person presets.
                                    if (titleForIndexMain.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (IsForbiddenInAppearanceCategory(checkPath)
                                            || !IsAppearanceLookInternalPath(checkPath))
                                            continue;
                                    }
                                    else if (!appearanceWorkerSkipPathMatch
                                        && !RefreshWorkerPathMatches(checkPath, workerPathsSnap, workerPathSnap))
                                        continue;

                                    if (hasNameFilter
                                        && !MatchesPackageFallbackSearch(searchQuerySnap, pkg != null ? pkg.Uid : "", pkg != null ? pkg.Path : "", internalPath))
                                        continue;

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
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (useSqliteIndex && activeContentSnap == ContentType.Category
                                && (userTagGridFilterUntaggedSnap
                                    || userTagGridFilterTaggedOnlySnap
                                    || (userTagNamesForGridSqlSnap != null && userTagNamesForGridSqlSnap.Count > 0)
                                    || (excludedUserTagNamesForGridSqlSnap != null && excludedUserTagNamesForGridSqlSnap.Count > 0)))
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
                        if (sqlPkgVersionFilterAppliedFlag != null)
                            _fileListHadSqlPkgVersionFilter = sqlPkgVersionFilterAppliedFlag[0] != 0;
                        if (sqlLicenseFilterAppliedFlag != null)
                            _fileListHadSqlLicenseFilter = sqlLicenseFilterAppliedFlag[0] != 0;
                        if (activeContentSnap == ContentType.History)
                            SyncHistoryBrowseFailureFlagsFromStats(activeContentSnap, catQueryStats, searchQuerySnap);
                        long bulkBudgetMs = maxMsPerFrame;
                        int bc = bulk.Count;
                        if (bc >= 16000) bulkBudgetMs = System.Math.Max(maxMsPerFrame, 160L);
                        else if (bc >= 8000) bulkBudgetMs = System.Math.Max(maxMsPerFrame, 120L);
                        else if (bc >= 3000) bulkBudgetMs = System.Math.Max(maxMsPerFrame, 80L);

                        if (RefreshFilesRoutineCanFastAppendSqliteBulkList(wantsPoseCounts) && bulk.Count > 0)
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
                            try { VpbProgressService.ReportBrowseRefreshPhase("Loading items"); } catch { }
                            // Chunk AddRange — zero-yield AddRange stalls busy chrome strip.
                            const int browseChunk = 512;
                            for (int bi = 0; bi < bulk.Count; bi++)
                            {
                                if (localLoadingGroupId != currentLoadingGroupId)
                                {
                                    HideLoadingOverlay();
                                    refreshCoroutine = null;
                                    CompletePaneLoadTimingIfPending("(refresh superseded)");
                                    yield break;
                                }
                                files.Add(bulk[bi]);
                                if ((bi % browseChunk) == browseChunk - 1 || bi == bulk.Count - 1)
                                {
                                    try { VpbProgressService.ReportBrowseRefresh(bi + 1, bulk.Count, "Loading items"); } catch { }
                                    if (yieldWatch.ElapsedMilliseconds >= bulkBudgetMs)
                                    {
                                        yield return null;
                                        yieldWatch.Reset();
                                        yieldWatch.Start();
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (LogGalleryRefreshDeepTiming && bulk.Count >= 5000)
                            {
                                try
                                {
                                    LogUtil.Log("[VPB.Gallery.DeepTiming] RefreshFilesRoutine bulk slow-path"
                                        + " | bulk=" + bulk.Count
                                        + " | ratingToggle=" + ((int)_ratingPresenceFilterMode)
                                        + " | ratingFilter=" + (string.IsNullOrEmpty(currentRatingFilter) ? "0" : "1")
                                        + " | sizeFilter=" + (string.IsNullOrEmpty(currentSizeFilter) ? "0" : "1")
                                        + " | sceneSrcFilter=0"
                                        + " | poseFilter=" + ((posePeopleFilter != PosePeopleFilter.All || wantsPoseCounts) ? "1" : "0")
                                        + " | appearanceFilter=" + (appearanceSubfilter != 0 ? "1" : "0"));
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
                if (sqlPkgVersionFilterAppliedFlag != null)
                    _fileListHadSqlPkgVersionFilter = sqlPkgVersionFilterAppliedFlag[0] != 0;
                if (sqlLicenseFilterAppliedFlag != null)
                    _fileListHadSqlLicenseFilter = sqlLicenseFilterAppliedFlag[0] != 0;
            }
            }
            if (swDeep != null)
            {
                deepAfterDrainMs = swDeep.ElapsedMilliseconds;
                deepFilesCountAfterDrain = files != null ? files.Count : 0;
            }

            _refreshPathsToSearch.Clear();
            List<string> pathsToSearch = _refreshPathsToSearch;
            Gallery.CollectLooseDiskSearchRoots(pathsToSearch, currentPaths, currentPath);

            if (activeContentType == ContentType.Category)
            {
                if (!HasCreatorFilter())
                {
                    // When file list came from GalleryFileListSnapshotCache, it already merged VAR index + loose files for this key.
                    // Re-enumerating Saves/* (SafeGetFiles + PassesFilters per path) duplicates work and can burn 10s+ on large trees with sysAdded=0.
                    if (!fileListFromCache)
                    {
                    string titleForLooseSceneScan = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                    bool applyVaMLocalSceneLooseFilter =
                        titleForLooseSceneScan.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0
                        && titleForLooseSceneScan.IndexOf("SubScene", StringComparison.OrdinalIgnoreCase) < 0;
                    // Fast path: reuse SQLite-cached loose-file listings for this category/path/ext combo when unchanged.
                    string sysCacheKey = null;
                    string sysCacheSig = null;
                    List<VpbLocalDatabase.SystemFileRow> sysCachedRows = null;
                    bool sysCacheHit = false;
                    try
                    {
                        // Cache key: format tag + category + extensions + search paths. The "sf5" tag marks resolved
                        // disk roots + local-scene listing rules (any json under Saves/scene; sibling jpg NOT required).
                        // Bumped sf3->sf4 so caches built under the old "jpg required" rule regenerate and pick up
                        // preview-less scenes. Bumped sf4->sf5 so caches written under the OLD rule — where the live
                        // filter state (user-tag "Untagged" mode, creator, source, pose, rating) was baked into cache
                        // membership and silently hid tagged scenes next launch — are discarded and rebuilt with the
                        // filter-independent membership rule (#64).
                        var sbKey = new System.Text.StringBuilder(256);
                        sbKey.Append("sf5|").Append(currentCategoryTitle ?? "").Append("|ext=");
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
                        _refreshPathKeySortScratch.Clear();
                        if (_refreshPathKeySortScratch.Capacity < pathsToSearch.Count)
                            _refreshPathKeySortScratch.Capacity = pathsToSearch.Count;
                        _refreshPathKeySortScratch.AddRange(pathsToSearch);
                        _refreshPathKeySortScratch.Sort(StringComparer.OrdinalIgnoreCase);
                        List<string> p2 = _refreshPathKeySortScratch;
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

                        _refreshSysCachedRowsScratch.Clear();
                        sysCachedRows = _refreshSysCachedRowsScratch;
                        sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCachedRows);
                    }
                    catch { sysCacheHit = false; sysCachedRows = null; }
                    if (swDeep != null)
                        deepSysCacheHit = sysCacheHit;

                    if (sysCacheHit && sysCachedRows != null)
                    {
                        bool prunedMissingCachedRows = false;
                        _refreshSysRowsToKeepScratch.Clear();
                        if (_refreshSysRowsToKeepScratch.Capacity < sysCachedRows.Count)
                            _refreshSysRowsToKeepScratch.Capacity = sysCachedRows.Count;
                        List<VpbLocalDatabase.SystemFileRow> sysRowsToKeep = _refreshSysRowsToKeepScratch;
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
                            if (applyVaMLocalSceneLooseFilter
                                && r.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                && !LocalSceneGallerySupport.IsVaMLocalSceneListingCandidate(r.Path))
                                continue;

                            // Loose scan roots are resolved to absolute disk paths; gallery classify/filter
                            // logic keys off VaM-relative paths, so normalize the entry path back.
                            var sysEntryFast = new SystemFileEntry(FileManager.NormalizePath(r.Path), wt, sz, exists: true);
                            if (!PassesFilters(sysEntryFast, true)) continue;

                            // Cache stores unfiltered candidates; pose people filter lives outside
                            // PassesFilters and must be re-applied on cache hit (#64). Star presence /
                            // star-count filters are inside PassesFilters above.
                            if (posePeopleFilter != PosePeopleFilter.All)
                            {
                                int pcPoseRead = 1;
                                bool isJsonPoseRead = false;
                                try { isJsonPoseRead = (sysEntryFast.Path != null && sysEntryFast.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)); } catch { isJsonPoseRead = false; }
                                if (isJsonPoseRead)
                                {
                                    int knownRead;
                                    if (TryGetKnownPosePeopleCount(sysEntryFast, out knownRead)) pcPoseRead = knownRead;
                                    else { EnqueuePosePeopleIndex(sysEntryFast); pcPoseRead = 1; }
                                }
                                if (posePeopleFilter == PosePeopleFilter.Single && pcPoseRead >= 2) continue;
                                if (posePeopleFilter == PosePeopleFilter.Dual && pcPoseRead < 2) continue;
                            }

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
                        _refreshSysRowsForWriteScratch.Clear();
                        List<VpbLocalDatabase.SystemFileRow> sysRowsForWrite = _refreshSysRowsForWriteScratch;
                        string titleForGeneratedSceneSkip = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                        bool skipVpbGeneratedLocalScenes = titleForGeneratedSceneSkip.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0;
                    string[] diskExtsForLoose = Gallery.DiskScanExtensionsOrEverything(currentExtension, extensions);
                    foreach (var searchPath in pathsToSearch)
                    {
                        if (!Directory.Exists(searchPath)) continue;

                        foreach (var ext in diskExtsForLoose)
                        {
                            _refreshSysFilePathScratch.Clear();
                            try
                            {
                                FileManager.SafeGetFiles(searchPath, "*." + ext, _refreshSysFilePathScratch);
                            }
                            catch { continue; }

                            for (int sfi = 0; sfi < _refreshSysFilePathScratch.Count; sfi++)
                            {
                                string sysPath = _refreshSysFilePathScratch[sfi];
                                if (string.IsNullOrEmpty(sysPath)) continue;
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

                                if (applyVaMLocalSceneLooseFilter
                                    && string.Equals(ext, "json", StringComparison.OrdinalIgnoreCase)
                                    && !LocalSceneGallerySupport.IsVaMLocalSceneListingCandidate(sysPath))
                                    continue;

                                // Loose scan roots are resolved to absolute disk paths; gallery classify/filter
                                // logic keys off VaM-relative paths, so normalize the entry path back.
                                var sysEntry = new SystemFileEntry(FileManager.NormalizePath(sysPath));

                                // Cache membership = every valid loose-scene candidate, INDEPENDENT of the live
                                // filter state. The sf5 cache key is category|ext|paths only (no filter signature),
                                // so the read path (above) re-applies PassesFilters/pose/rating per row. Writing the
                                // row here, before any filter `continue`, prevents a scan performed while a transient
                                // filter was active (e.g. user-tag "Untagged" mode during the tag-a-scene workflow)
                                // from baking that filtering into the persisted cache and silently hiding tagged
                                // scenes on the next launch until a folder mtime change invalidates the cache (#64).
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

                                // From here down decides GRID membership only (cache row already written above).
                                // The clothing/hair subfilter is skipped for the facet/pass gate and re-applied on read.
                                if (!PassesFilters(sysEntry, true, true)) continue;
                                bool gridOk = PassesFilters(sysEntry, true);

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

                                if (gridOk)
                                {
                                    files.Add(sysEntry);
                                    if (sysLooseFilesAddedCount != null)
                                        sysLooseFilesAddedCount[0]++;
                                    if (swDeep != null) deepSysFilesAdded++;
                                }
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
                {
                    try { VpbProgressService.ReportBrowseRefreshPhase("Sorting"); } catch { }
                    GallerySortManager.Instance.SortFiles(files, sortState, _fileListHadSqlPkgVersionFilter);
                }
                swSortMain.Stop();
                if (LogGalleryRefreshDeepTiming)
                {
                    try { LogUtil.Log("[VPB.Gallery.DeepTiming] Sort done ms=" + swSortMain.ElapsedMilliseconds); } catch { }
                }

                // Same finalize as live title search: filter the non-search base in memory.
                if (_refreshDeferNameFilterToInMemory)
                {
                    try
                    {
                        topSearchBaseFiles = new List<FileEntry>(files.Count);
                        topSearchBaseFiles.AddRange(files);
                        _topSearchBaseIsClean = true;
                        ApplyTitleSearchToBaseListInMemory();
                        files.Clear();
                        if (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                        {
                            if (files.Capacity < currentFilteredFiles.Count)
                                files.Capacity = currentFilteredFiles.Count;
                            files.AddRange(currentFilteredFiles);
                        }
                    }
                    catch
                    {
                        try { topSearchBaseFiles = null; } catch { }
                    }
                    finally
                    {
                        _refreshDeferNameFilterToInMemory = false;
                    }
                }

                if (canFileListCache && fileListSnapKey != null)
                    GalleryFileListSnapshotCache.Put(fileListSnapKey, files);
            }
            else if (_refreshDeferNameFilterToInMemory)
            {
                // Cache hit / early path already had search in the key — do not re-filter.
                _refreshDeferNameFilterToInMemory = false;
            }
            if (swDeep != null) deepAfterSortMs = swDeep.ElapsedMilliseconds;

            if (settingsListViewActive)
            {
                RefreshInternalSettingsListRows(keepScroll);
                refreshCoroutine = null;
                yield break;
            }

            // Cache the filtered list for selection operations (Select All, counts, etc)
            try { VpbProgressService.ReportBrowseRefreshPhase("Building grid"); } catch { }
            lastFilteredFiles.Clear();
            if (files.Count > 0)
            {
                if (lastFilteredFiles.Capacity < files.Count)
                    lastFilteredFiles.Capacity = files.Count;
                const int snapChunk = 1024;
                for (int si = 0; si < files.Count; si++)
                {
                    lastFilteredFiles.Add(files[si]);
                    if ((si % snapChunk) == snapChunk - 1)
                    {
                        try { VpbProgressService.ReportBrowseRefresh(si + 1, files.Count, "Building grid"); } catch { }
                        yield return null;
                    }
                }
            }

            bool snapOk = false;
            try
            {
                galleryFilesPreHideSnapshot.Clear();
                if (files.Count > 0 && galleryFilesPreHideSnapshot.Capacity < files.Count)
                    galleryFilesPreHideSnapshot.Capacity = files.Count;
                snapOk = true;
            }
            catch
            {
                try { galleryFilesPreHideSnapshot.Clear(); } catch { }
                galleryPreHideSnapshotValid = false;
            }

            if (snapOk)
            {
                const int hideChunk = 1024;
                for (int si = 0; si < files.Count; si++)
                {
                    try { galleryFilesPreHideSnapshot.Add(files[si]); }
                    catch
                    {
                        try { galleryFilesPreHideSnapshot.Clear(); } catch { }
                        galleryPreHideSnapshotValid = false;
                        snapOk = false;
                        break;
                    }
                    if ((si % hideChunk) == hideChunk - 1)
                        yield return null;
                }
                if (snapOk)
                    galleryPreHideSnapshotValid = true;
            }

            // Promote to class member for RecyclingGridView — one copy pass from lastFilteredFiles (same snapshot as files)
            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(lastFilteredFiles);
            try { NotifyPluginsFloatAfterGridReady(); } catch { }
            // If no name filter was active, the next SetNameFilter call can use currentFilteredFiles
            // as a trustworthy unfiltered base for in-memory search.
            if (!HasActiveNameFilter())
                _topSearchBaseIsClean = true;

            if (swDeep != null) deepGbListCopyMs = swDeep.ElapsedMilliseconds;

            // Setup Recycling Grid (skipped in quiet mode — keep frozen display cells bound to _quietDisplayFiles)
            if (!_quietGalleryRefresh && contentGO != null)
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
                int cols = GridColumnCount;
                
                // Initialize spacing and adaptive config
                if (layoutMode == GalleryLayoutMode.List || settingsListViewActive)
                {
                    // List/Table mode: ALWAYS 1 column; +/- controls row height/thumb size.
                    recyclingGrid.fixedColumns = 1;
                    recyclingGrid.fixedBottomChromePx = 0f;
                    recyclingGrid.SetGridConfig(100f, EffectiveListRowHeightForGallery(), 5f, 5f, 1, deferRefresh: true);
                    recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true, deferRefresh: true);
                }
                else
                {
                    ApplyGridRecyclingLayoutConfig(recyclingGrid, cols, deferRefresh: true);
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
            if (!_quietGalleryRefresh)
            {
                try { UpdateEmptyGridState(); } catch { }
            }

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
            if (!_quietGalleryRefresh)
                UpdateLayout(!suppressSyncCreatorCategoryCaches, true);
            if (swDeep != null) deepUpdateLayoutMs = swDeep.ElapsedMilliseconds;
            LogGalleryCategoryTypeNavPhase("RefreshFilesRoutine_after_UpdateLayout");
            // Layout rebuild can clamp ScrollRect and undo the position we just set.
            if (!_quietGalleryRefresh && scrollRect != null && !scrollToBottom)
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
                // User-tag amounts need same post-index rebuild as category/creator (issue #84).
                userTagsCached = false;
                InvalidateSharedSideMetaIfPackageScanAdvanced();
                _sideTabsNeedFullRebuildAfterFirstRefresh = false;
            }

            HideLoadingOverlay();
            hasLoadedContent = true;
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
            // Quiet background randomize: skip — side tabs + hide follow-up would thrash the frozen grid.
            if (!_quietGalleryRefresh)
            {
                if (leftTabContainerGO != null || rightTabContainerGO != null)
                    _deferredGallerySideTabsCoroutine = StartCoroutine(DeferredGallerySideTabsAfterGridReady(navSessionForThisRun, _deferredSubPaneSessionId, tagParallelWaiterForThisRun, tagScanRefreshSeq));

                // Defer hide filtering until after the grid is visible (prescan .hide markers then filter in a coroutine).
                // Always run follow-up: hide strip (unless sort needs hidden rows), then Hidden-only / AutoInstall-only narrowing, then re-sort.
                StartCoroutine(PostFilesListHideAndSortFollowupRoutine(currentLoadingGroupId, keepScroll, scrollToBottom, savedScrollNormalizedPos));
            }
            // (FileManager scan still in progress), schedule a single retry — but only
            // if no retry is already pending/running. This prevents an infinite refresh
            // loop where each retry finds uncached packages and spawns yet another retry.
            if (!_quietGalleryRefresh && skippedForNoCache[0] > 0 && !Gallery.IsSuppressed() && !_cacheRetryPending)
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
            string title = titleText != null ? titleText.text : "";
            bool leftCat = leftActiveContent.HasValue && leftActiveContent.Value == ContentType.Category;
            bool rightCat = rightActiveContent.HasValue && rightActiveContent.Value == ContentType.Category;
            if (!leftCat && !rightCat) return false;

            if (!CategoryNeedsTagCountCachePass(title)) return false;

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
                else if (titleSnap.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
                      || titleSnap.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try { TryApplyHairClothingSubfilterCountsFromSql(); } catch { }
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
                if (!primed)
                    primed = TryApplyAppearanceFacetCountsFromSql();
                // Source:Local already scheduled sliced recount inside TryRecompute.
                // Non-Local: merge loose counts onto SQL/VAR totals in slices.
                if (primed && ShouldCountLooseAppearanceGenderFiles() && !IsAppearanceLooseScopedBrowsing())
                {
                    IEnumerator looseMerge = CoMergeLooseVapAppearanceGenderFacetCounts(TagCountScanDeferredSliceMs, deferredSubPaneSessionWhenScheduled, resetCountsFirst: false);
                    while (looseMerge.MoveNext())
                    {
                        if (deferredSubPaneSessionWhenScheduled != _deferredSubPaneSessionId)
                            yield break;
                        yield return looseMerge.Current;
                    }
                    ranSlicedTagScan = true;
                }
                if (IsAppearanceLooseScopedBrowsing())
                    ranSlicedTagScan = true;
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
                if (_browseHiddenCycle != BrowseFilterCycle.Off) return true;
                SortType t = GetSortState("Files").Type;
                return t == SortType.Hidden || t == SortType.HiddenOnly;
            }
            catch { return false; }
        }

        private bool FilesSortWantsLoadedOnly()
        {
            try
            {
                if (_browseLoadedMode == BrowseLoadedMode.LoadedOnly) return true;
                return GetSortState("Files").Type == SortType.LoadedOnly;
            }
            catch { return false; }
        }

        private bool FilesSortWantsUnloadedOnly()
        {
            try
            {
                if (_browseLoadedMode == BrowseLoadedMode.UnloadedOnly) return true;
                return GetSortState("Files").Type == SortType.UnloadedOnly;
            }
            catch { return false; }
        }

        /// <summary>Removes non-matching rows for exclusive browse/sort modes (list is modified in place).</summary>
        private void ApplyFilesSortExclusiveFiltersInPlace(List<FileEntry> list, SortType type)
        {
            if (list == null) return;

            bool hiddenOnly = _browseHiddenCycle == BrowseFilterCycle.Only || type == SortType.HiddenOnly;
            bool alwaysLoadedOnly = _browseAlwaysLoadedCycle == BrowseFilterCycle.Only || type == SortType.AutoInstallOnly;
            bool loadedOnly = _browseLoadedMode == BrowseLoadedMode.LoadedOnly || type == SortType.LoadedOnly;
            bool unloadedOnly = _browseLoadedMode == BrowseLoadedMode.UnloadedOnly || type == SortType.UnloadedOnly;
            bool unusedOnly = _browseUnusedCycle == BrowseFilterCycle.Only || type == SortType.UnusedOnly;

            if (hiddenOnly)
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
            if (alwaysLoadedOnly)
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
            if (loadedOnly)
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
            if (unloadedOnly)
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
            if (unusedOnly)
            {
                try
                {
                    var keys = new List<string>(list.Count);
                    for (int i = 0; i < list.Count; i++)
                        keys.Add(VpbLocalDatabase.BuildUsageKey(list[i]));

                    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    if (VpbLocalDatabase.TryReadItemUseCountsForKeys(keys, counts))
                    {
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
                }
                catch
                {
                    // If usage DB is unavailable, keep the list unchanged (fail open).
                }
            }

            if (_browseOldVersionsCycle == BrowseFilterCycle.Only && !_fileListHadSqlPkgVersionFilter)
            {
                try { GallerySortManager.ApplyOldVersionsOnlyFilter(list); } catch { }
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
            try { if (Gallery.IsSuppressed()) return false; } catch { return false; }
            if (refreshCoroutine != null) return false;
            try { if (_refreshHistoryLightCo != null) return false; } catch { }
            if (!hasLoadedContent) return false;
            if (recyclingGrid == null || currentFilteredFiles == null || scrollRect == null) return false;
            if (!galleryPreHideSnapshotValid) return false;
            if (IsFilterActive) return false;
            if (currentPackageFilterMode != PackageFilterMode.None) return false;
            try { if (cleanupModeActive) return false; } catch { }
            try { if (_userTagAvailMode != UserTagAvailMode.Tag) return false; } catch { }
            try
            {
                if (HasActiveNameFilter()) return false;
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
                    // Active chip reads currentFilteredFiles.Count; this pass just settled it (hide-strip +
                    // hide-old-versions), so rebuild the sub-pane chips so the active chip shows the final count.
                    try { if (clothingSubfilter != 0 || hairSubfilter != 0) RebuildSubPaneSideTabListsOnly(); } catch { }
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
                AssignNameFilterState(frame.savedNameFilter);
                HydrateTitleSearchChipsFromCurrentFilter();
                SetTitleSearchInputTextWithoutNotify(titleSearchInput, GetTitleSearchBrowseFieldText(), _titleBarSearchOnValueChanged);
            }
            catch { }

            filterBaseAnchorKey = null;
            InvalidateGalleryPreHideFileListSnapshot();
            RefreshRecycleGridAfterFilterChange();
            try { RefreshChromeAfterPackageFilterListChange(); } catch { }
            try { UpdatePaginationText(); } catch { }
            ScrollGalleryToTop();
            try { SyncBrowseFilterChipChrome(); } catch { }
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
            try { RefreshChromeAfterPackageFilterListChange(); } catch { }
            try { UpdatePaginationText(); } catch { }

            filterBaseAnchorKey = null;
            ScrollGalleryToTop();

            // If the user entered filter mode while a top search was active, clearing the filter
            // should return to the full category list (not the search-limited snapshot).
            if (bottom.enteredFromTopSearch)
            {
                try
                {
                    ClearNameFilterState();
                    SetTitleSearchInputTextWithoutNotify(titleSearchInput, "", _titleBarSearchOnValueChanged);
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
                    AssignNameFilterState(bottom.savedNameFilter);
                    HydrateTitleSearchChipsFromCurrentFilter();
                    SetTitleSearchInputTextWithoutNotify(titleSearchInput, GetTitleSearchBrowseFieldText(), _titleBarSearchOnValueChanged);
                }
                catch { }
            }
            try { SyncBrowseFilterChipChrome(); } catch { }
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
