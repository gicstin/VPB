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

        /// <summary>Scene category or already showing package-level rows — use package list for deps/dependents filter.</summary>
        private bool PackageFilterUsesPackageListRows()
        {
            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
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

            foreach (VarPackage pkg2 in FileManager.PackagesByUid.Values)
            {
                if (pkg2 == null) continue;
                try
                {
                    var otherDeps = pkg2.RecursivePackageDependencies;
                    if (otherDeps == null) continue;
                    for (int di = 0; di < otherDeps.Count; di++)
                    {
                        if (!DepRefersToTarget(otherDeps[di], targetUid, targetShort)) continue;
                        if (!string.IsNullOrEmpty(pkg2.Uid)) uids.Add(pkg2.Uid);
                        break;
                    }
                }
                catch { }
            }
            return uids;
        }

        private static void AddVarFileEntriesWithPackageInDepList(List<FileEntry> filtered, FileEntry master, IList<FileEntry> source, List<string> depUids)
        {
            if (filtered == null || depUids == null || source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                FileEntry other = source[i];
                if (other == master) continue;
                if (other is VarFileEntry vfe && vfe.Package != null && depUids.Contains(vfe.Package.Uid))
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

                string p = null;
                string n = null;
                try { p = e.Path; } catch { }
                try { n = e.Name; } catch { }
                if (MatchesAllTermsInEither(p, n, terms))
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
            bool hasExt = extensions.Length > 0 && !(extensions.Length == 1 && string.IsNullOrEmpty(extensions[0]));
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
                if (!string.IsNullOrEmpty(currentCreator))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator) continue;
                    }
                    catch { continue; }
                }

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
                    if (hasNameFilt && !MatchesAllTermsInEither(pkg != null ? pkg.Path : "", ip, nameTerms)) continue;

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
                        var row = new PackageListEntry(uid, r.VarPath ?? "", wt, sz, r.PackageCreationTicksOrInvalid);
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

            if (clothingSubfilter != 0)
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
                if ((clothingSubfilter & ClothingSubfilter.Items) != 0) { if (isPreset) return false; }
                if ((clothingSubfilter & ClothingSubfilter.Male) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Male) return false; }
                if ((clothingSubfilter & ClothingSubfilter.Female) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Female) return false; }
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

            // Hide filtering and sort-only narrowing run in PostFilesListHideAndSortFollowupRoutine after the grid is shown.
            // to avoid per-entry FileManager.FileExists calls blocking the scan drain loop.

            // Clothing subfilter (Gallery left Tags panel)
            // Applies only when browsing Clothing category.
            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isClothing && !skipClothingGalleryFilters)
            {
                string p = entry.Path;
                bool isVarPackageEntry = (entry is VarFileEntry) || ((entry as SystemFileEntry) != null && ((SystemFileEntry)entry).isVar);
                if (!PassesClothingGalleryFiltersForPath(p, clothingSubfilter, isVarPackageEntry))
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

                if (!string.IsNullOrEmpty(currentAppearanceSourceFilter))
                {
                    if (currentAppearanceSourceFilter == "presets")
                    {
                        // Presets = appearance .vap in a .var package
                        if (!isVarAppearanceVap) return false;
                    }
                    else if (currentAppearanceSourceFilter == "custom")
                    {
                        // Custom = appearance presets outside .var (Saves/Custom folders)
                        if (!isLocalAppearanceVap) return false;
                    }
                }

                if (appearanceSubfilter != 0)
                {
                    bool isCustomAppearance = norm.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase);
                    bool isPresetAppearance = false;
                    if (entry is VarFileEntry vfe2)
                    {
                        string ip2 = (vfe2.InternalPath ?? "").Replace('\\', '/');
                        isPresetAppearance = isVap && ip2.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        isPresetAppearance = isVap && norm.StartsWith("Custom/Atom/Person/Appearance", StringComparison.OrdinalIgnoreCase);
                    }

                    AppearanceGender g = AppearanceGender.Unknown;
                    try { g = GetAppearanceGender(entry); } catch { g = AppearanceGender.Unknown; }

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

                    bool wantsMale = (appearanceSubfilter & AppearanceSubfilter.Male) != 0;
                    bool wantsFemale = (appearanceSubfilter & AppearanceSubfilter.Female) != 0;
                    bool wantsFuta = (appearanceSubfilter & AppearanceSubfilter.Futa) != 0;
                    bool wantsAnyGender = wantsMale || wantsFemale || wantsFuta;
                    if (wantsAnyGender)
                    {
                        bool ok = false;
                        if (wantsMale && g == AppearanceGender.Male) ok = true;
                        if (wantsFemale && g == AppearanceGender.Female) ok = true;
                        if (wantsFuta && g == AppearanceGender.Futa) ok = true;
                        if (!ok) return false;
                    }
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

            // Scene Source Filter
            if (!string.IsNullOrEmpty(currentSceneSourceFilter))
            {
                if (currentSceneSourceFilter == "Addon Scenes")
                {
                    if (!(entry is VarFileEntry)) return false;
                }
                else if (currentSceneSourceFilter == "Custom Scenes")
                {
                    if (entry is VarFileEntry) return false;
                    // Custom scenes are from Saves folder
                    if (!entry.Path.StartsWith("Saves", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }

            // Name Filter
            if (nameFilterTerms != null && nameFilterTerms.Length > 0)
                if (!MatchesAllTermsInEither(entry.Path, "", nameFilterTerms))
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

            return true;
        }

        private IEnumerator RetryRefreshAfterNoCacheDelay()
        {
            yield return new WaitForSeconds(3f);
            if (!Gallery.IsSuppressed() && !IsHubMode)
            {
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

        public void RefreshFiles(bool keepScroll = false, bool scrollToBottom = false, bool isRetry = false)
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
            // Rotate the group ID here (synchronously) so that any in-flight thumbnail callbacks
            // from the old category fail the capturedGroupId == currentLoadingGroupId guard and
            // don't pollute the new session. The coroutine's yield-return-null would be too late.
            if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
                CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
            currentLoadingGroupId = Guid.NewGuid().ToString();
            unchecked { _deferredSubPaneSessionId++; }
            System.Threading.Interlocked.Increment(ref galleryFileRefreshSequence);
            if (refreshCoroutine != null) StopCoroutine(refreshCoroutine);
            _boundCategoryNavSessionForCurrentRefresh = _categoryTypeNavStopwatch != null ? _categoryTypeNavTargetSession : 0;
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
        public void ApplyPackageDelta(List<VarPackage> added, List<VarPackage> removed)
        {
            if (Gallery.IsSuppressed()) return;
            if (IsHubMode) return;

            // If we have never loaded, the scan just completed and we have a full PackagesByUid
            // for the first time – do a clean initial load now.
            if (!hasLoadedContent || recyclingGrid == null || scrollRect == null)
            {
                RefreshFiles(false);
                return;
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
                return;
            }

            // If the refresh coroutine is still running (shouldn't normally happen after the
            // !init||flag gate, but be defensive) cancel it so we work on a stable list.
            if (refreshCoroutine != null)
            {
                StopCoroutine(refreshCoroutine);
                refreshCoroutine = null;
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
                bool hasExt = extensions.Length > 0 && !(extensions.Length == 1 && string.IsNullOrEmpty(extensions[0]));
                string[] nameTerms = nameFilterTerms;
                bool hasNameFilt = nameTerms != null && nameTerms.Length > 0;

                var newEntries = new List<FileEntry>();
                foreach (var pkg in added)
                {
                    if (pkg == null) continue;

                    // Package-level creator filter
                    if (!string.IsNullOrEmpty(currentCreator) &&
                        (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != currentCreator)) continue;

                    List<string> names; List<long> ticks; List<long> sizes;
                    if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null) continue;

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
                        if (hasNameFilt && !MatchesAllTermsInEither(pkg != null ? pkg.Path : "", ip, nameTerms)) continue;

                        var entry = new VarFileEntry(pkg, ip, pkg.LastWriteTime, pkg.Size);

                        // Full filter check (clothing/appearance subfilters, tags, rating, size, scene source …)
                        if (!PassesFilters(entry, true)) continue;

                        newEntries.Add(entry);
                    }
                }

                if (newEntries.Count > 0)
                {
                    currentFilteredFiles.AddRange(newEntries);
                    lastFilteredFiles.AddRange(newEntries);

                    var sortState = GetSortState("Files");
                    GallerySortManager.Instance.SortFiles(currentFilteredFiles, sortState);
                    GallerySortManager.Instance.SortFiles(lastFilteredFiles, sortState);

                    changed = true;
                }
            }

            if (!changed)
            {
                // Nothing actually changed – keep gallery exactly as-is.
                lastAppliedPackageRefreshTime = FileManager.lastPackageRefreshTime;
                refreshOnNextShow = false;
                return;
            }

            // ── Update grid ───────────────────────────────────────────────────────────────
            recyclingGrid.SetItemCount(currentFilteredFiles.Count);

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
            refreshOnNextShow = false;
            creatorsCached = false;
            tagsCached = false;
            categoriesCached = false;
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
                sb.Append(currentCreator ?? "").Append('\u001E');
                sb.Append(nameFilterLower ?? "").Append('\u001E');
                sb.Append(title).Append('\u001E');
                sb.Append((int)posePeopleFilter).Append('\u001E');
                sb.Append((int)clothingSubfilter).Append('\u001E');
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
            if (isRatingSortToggleEnabled) return false;
            if (!string.IsNullOrEmpty(currentRatingFilter)) return false;
            if (!string.IsNullOrEmpty(currentSizeFilter)) return false;
            if (!string.IsNullOrEmpty(currentSceneSourceFilter)) return false;
            // nameFilterTerms and activeTags are now handled by SQL
            if (wantsPoseCountsLocal || posePeopleFilter != PosePeopleFilter.All) return false;
            if (FilesSortWantsLoadedOnly()) return false;
            if (FilesSortWantsUnloadedOnly()) return false;

            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
            if (title.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!string.IsNullOrEmpty(currentAppearanceSourceFilter)) return false;
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

            if (isRatingSortToggleEnabled)
            {
                if (RatingsManager.Instance.GetRating(entry) <= 0) return false;
            }

            targetFiles.Add(entry);
            return yieldWatch.ElapsedMilliseconds > maxMsBudget;
        }

        private IEnumerator RefreshFilesRoutine(bool keepScroll, bool scrollToBottom)
        {
            int navSessionForThisRun = _boundCategoryNavSessionForCurrentRefresh;
            yield return null; // Allow UI to render first
            LogGalleryCategoryTypeNavPhase("RefreshFilesRoutine_after_first_yield");

            // Reset pose facet counts for this refresh
            posePeopleFacetCountSingle = 0;
            posePeopleFacetCountDual = 0;
            
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
                        recyclingGrid.SetGridConfig(100f, ListRowHeight, 5f, 5f, 1);
                        recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true);
                    }
                    else
                    {
                        // Grid mode
                        recyclingGrid.SetGridConfig(100f, GetGridCellConfigHeight(), 10f, 10f, GridColumnCount);
                        recyclingGrid.SetAdaptiveConfig(true, 200f, GridColumnCount, false);
                    }
                    recyclingGrid.SetItemCount(0); // Clear initially
                }
            }
            
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            string[] nameTerms = nameFilterTerms;
            bool hasNameFilter = nameTerms != null && nameTerms.Length > 0;

            int tagScanRefreshSeq = GalleryFileRefreshSequence;
            TagParallelWaiter tagParallelWaiterForThisRun = null;

            string titleForCounts = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            bool isPoseCategory = titleForCounts.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!tagsCached && DeferredSubPaneNeedsTagCountCachePass())
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
            // Warm loads: allow slightly larger chunks than 10ms to cut yield overhead on huge libraries (still yields often).
            long maxMsPerFrame = isColdGalleryContentLoad ? 36 : 22;

            yieldWatch.Start();

            int[] skippedForNoCache = { 0 };

            string fileListSnapKey;
            bool canFileListCache = TryBuildFileListSnapshotCacheKey(out fileListSnapKey);
            List<FileEntry> snapList = null;
            bool fileListFromCache = false;
            bool fileListFromSibling = false;
            if (canFileListCache && Gallery.singleton != null)
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
                    if (VPBConfig.IsLogConfigPerfEnabled())
                    {
                        try { LogUtil.Log("[VPB] Gallery file-list snapshot SIBLING reuse (count=" + snapList.Count + ")"); } catch { }
                    }
                    break;
                }
            }
            if (!fileListFromCache && canFileListCache)
                fileListFromCache = GalleryFileListSnapshotCache.TryGet(fileListSnapKey, out snapList);
            if (fileListFromCache && !fileListFromSibling && VPBConfig.IsLogConfigPerfEnabled())
            {
                try { LogUtil.Log("[VPB] Gallery file-list snapshot cache HIT"); } catch { }
            }
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
                    currentCreator, currentExtension, currentPath, currentPaths, categories, currentCategoryTitle);
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
                string _bCreator = currentCreator;
                string _bExtension = currentExtension;
                List<string> _bPaths = currentPaths != null ? new List<string>(currentPaths) : null;
                string _bPath = currentPath;
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
                            if (!VpbLocalDatabase.TryReadCreatorFileCounts(counts, _bExtension, _bPaths, _bPath, null, _bCategoryTitle))
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

                            if (!VpbLocalDatabase.TryReadCategoryMemberCounts(catCounts2, _bCreator))
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
                                    foreach (var pkg in FileManager.PackagesByUid.Values)
                                    {
                                        if (!string.IsNullOrEmpty(_bCreator) && (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != _bCreator)) continue;
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
                            AddLocalCustomScriptsCountToCategory(catCounts2);
                            earlyNewCatCounts = catCounts2;
                        }
                    }
                    catch { }
                    finally { earlyMetaBuildDone = true; }
                });
            }

            if (!fileListFromCache)
            {
            if (FileManager.PackagesByUid != null)
            {
                string localLoadingGroupId = currentLoadingGroupId;

                Queue<FileEntry> candidateQueue = new Queue<FileEntry>();
                object candidateQueueLock = new object();
                int workerDoneFlag = 0;
                List<FileEntry> sqliteBulkList = null;

                string snapKeyProbeMain;
                bool canFileListSnapKeyMain = TryBuildFileListSnapshotCacheKey(out snapKeyProbeMain);
                string titleForIndexMain = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                string extForIndexMain = currentExtension ?? "";
                string creatorForIndexMain = currentCreator ?? "";
                int wantsLoadedStateForIndexMain = -1;
                try
                {
                    SortType st = GetSortState("Files").Type;
                    if (st == SortType.LoadedOnly) wantsLoadedStateForIndexMain = 1;
                    else if (st == SortType.UnloadedOnly) wantsLoadedStateForIndexMain = 0;
                }
                catch { }
                ContentType activeContentSnap = activeContentType;
                ClothingSubfilter sqliteWorkerClothingSub = clothingSubfilter;
                bool sqliteDrainSkipClothingGateOnMain = (titleForIndexMain.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0);

                VpbLocalDatabase.GalleryCategoryQueryStats catQueryStats = new VpbLocalDatabase.GalleryCategoryQueryStats();

                ThreadPool.QueueUserWorkItem((state) =>
                {
                    var swWorker = System.Diagnostics.Stopwatch.StartNew();
                    string workerPathLabel = "legacy";
                    long workerBulkBuildMs = 0;
                    int workerBulkOutCount = 0;
                    try
                    {
                        List<VpbLocalDatabase.Row> idxRows = new List<VpbLocalDatabase.Row>();
                        bool useSqliteIndex;
                        if (VpbSqlite3.IsAvailable
                            && activeContentSnap == ContentType.Category
                            && canFileListSnapKeyMain)
                        {
                            List<string> pathExclusions = null;
                            if (currentPaths != null && currentPaths.Count > 0)
                            {
                                for (int i = 0; i < currentPaths.Count; i++)
                                {
                                    if (string.Equals(currentPaths[i], "Saves/Person", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (pathExclusions == null) pathExclusions = new List<string>();
                                        pathExclusions.Add("Saves/Person/appearance");
                                    }
                                }
                            }
                            else if (string.Equals(currentPath, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                            {
                                pathExclusions = new List<string> { "Saves/Person/appearance" };
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
                                activeTags: activeTags,
                                sortState: fileListSortSnapForWorker);
                        }
                        else
                        {
                            useSqliteIndex = false;
                            if (!VpbSqlite3.IsAvailable)
                                catQueryStats.RejectReason = "gate:sqlite_unavailable";
                            else if (activeContentSnap != ContentType.Category)
                                catQueryStats.RejectReason = "gate:not_category_content";
                            else
                                catQueryStats.RejectReason = "gate:snapshot_cache_key_failed";
                        }

                        if (useSqliteIndex)
                        {
                            workerPathLabel = "sqlite";
                            var swBulk = System.Diagnostics.Stopwatch.StartNew();
                            var bulk = new List<FileEntry>(idxRows.Count > 0 ? idxRows.Count : 16);
                            for (int ri = 0; ri < idxRows.Count; ri++)
                            {
                                if (localLoadingGroupId != currentLoadingGroupId) return;

                                VpbLocalDatabase.Row r = idxRows[ri];
                                string internalPath = r.InternalPath;

                                // SQL filters (name, tags, path exclusions) are now applied in the query.
                                // We still keep RefreshWorkerPathMatches for complex path logic that SQL can't easily do,
                                // but most rows should already be filtered out.
                                if (!RefreshWorkerPathMatches(internalPath, currentPaths, currentPath)) continue;

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

                                // Exclusive filter (Loaded-only) is pushed into SQLite query when possible,
                                // but keep a defensive check here for any non-indexed paths.
                                if (wantsLoadedStateForIndexMain != -1)
                                {
                                    bool loaded = r.PackageIsLoaded;
                                    if (!loaded)
                                    {
                                        // "Loaded" means the PACKAGE ROOT is under AddonPackages/, or a loose path under Custom/ / Saves/.
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

                                // Name filter and active tags are now handled by SQL.
                                // We can skip MatchesAllTermsInEither here for the SQL path.

                                // Clothing: with an active subfilter, avoid ClassifyClothingHairPath per row when the SQLite
                                // index carries packed attrs (rebuild after schema bump); else fall back to path classification.
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

                                DateTime entryTime = DateTime.MinValue;
                                if (r.LastWriteTicksOrInvalid != long.MinValue)
                                {
                                    try { entryTime = DateTime.FromBinary(r.LastWriteTicksOrInvalid); }
                                    catch { entryTime = DateTime.MinValue; }
                                }
                                long entrySize = 0;
                                if (r.PackageSizeOrInvalid != long.MinValue)
                                    entrySize = r.PackageSizeOrInvalid;

                                VarFileEntry vfe = new VarFileEntry(r.PackageUid, internalPath, entryTime, entrySize, listPath, varHint, r.PackageCreationTicksOrInvalid);
                                bulk.Add(vfe);
                            }

                            if (bulk.Count >= 2 && fileListSortSnapForWorker != null)
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
                            workerBulkBuildMs = swBulk.ElapsedMilliseconds;
                            workerBulkOutCount = bulk.Count;

                            sqliteBulkList = bulk;
                            Thread.MemoryBarrier();
                        }
                        else
                        {
                            foreach (var pkg in FileManager.PackagesByUid.Values)
                            {
                                if (localLoadingGroupId != currentLoadingGroupId) return;

                                string filterCreator = currentCreator;
                                if (!string.IsNullOrEmpty(filterCreator))
                                {
                                    if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != filterCreator) continue;
                                }

                                List<string> names;
                                List<long> ticks;
                                List<long> sizes;
                                if (!pkg.TryGetCachedFileEntryData(out names, out ticks, out sizes) || names == null)
                                {
                                    skippedForNoCache[0]++;
                                    continue;
                                }

                                for (int i = 0; i < names.Count; i++)
                                {
                                    if (localLoadingGroupId != currentLoadingGroupId) return;
                                    string internalPath = names[i];

                                    string checkPath = internalPath;

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

                                    if (!RefreshWorkerPathMatches(checkPath, currentPaths, currentPath)) continue;

                                    if (hasNameFilter)
                                    {
                                        if (!MatchesAllTermsInEither(pkg != null ? pkg.Path : "", internalPath, nameTerms)) continue;
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
                    finally
                    {
                        swWorker.Stop();
                        if (VPBConfig.IsLogConfigPerfEnabled())
                        {
                            try
                            {
                                string rej = catQueryStats.RejectReason ?? "";
                                if (rej.Length > 220)
                                    rej = rej.Substring(0, 217) + "...";
                                LogUtil.Log("[VPB.Gallery.SqlIndex] worker path=" + workerPathLabel
                                    + " worker_total_ms=" + swWorker.ElapsedMilliseconds
                                    + " sql_ms=" + catQueryStats.SqlElapsedMs
                                    + " sql_rows_read=" + catQueryStats.RowsRead
                                    + " bulk_build_ms=" + workerBulkBuildMs
                                    + " bulk_after_worker_filter=" + workerBulkOutCount
                                    + " sql_executed=" + catQueryStats.ExecutedQuery
                                    + " snap_key_ok=" + (canFileListSnapKeyMain ? "1" : "0")
                                    + (string.IsNullOrEmpty(rej) ? "" : (" reject=" + rej))
                                    + " cat='" + titleForIndexMain + "'");
                            }
                            catch { }
                        }
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
                                    files.AddRange(bulk);
                                }
                                else
                                {
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

            List<string> pathsToSearch = new List<string>();
            if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
            else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

            if (activeContentType == ContentType.Category)
            {
                if (string.IsNullOrEmpty(currentCreator))
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

                        // Signature: directory last write times.
                        var sbSig = new System.Text.StringBuilder(256);
                        for (int i = 0; i < p2.Count; i++)
                        {
                            string sp = p2[i];
                            long t = 0;
                            try { if (Directory.Exists(sp)) t = Directory.GetLastWriteTimeUtc(sp).ToBinary(); } catch { t = 0; }
                            if (i != 0) sbSig.Append('|');
                            sbSig.Append(t.ToString());
                        }
                        sysCacheSig = sbSig.ToString();

                        sysCachedRows = new List<VpbLocalDatabase.SystemFileRow>();
                        sysCacheHit = VpbLocalDatabase.TryReadSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysCachedRows);
                    }
                    catch { sysCacheHit = false; sysCachedRows = null; }

                    if (sysCacheHit && sysCachedRows != null && sysCachedRows.Count > 0)
                    {
                        for (int i = 0; i < sysCachedRows.Count; i++)
                        {
                            var r = sysCachedRows[i];
                            if (string.IsNullOrEmpty(r.Path)) continue;
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
                        }
                    }
                    else
                    {
                        var sysRowsForWrite = new List<VpbLocalDatabase.SystemFileRow>(256);
                    foreach (var searchPath in pathsToSearch)
                    {
                        if (!Directory.Exists(searchPath)) continue;

                        foreach (var ext in extensions)
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
                            if (!string.IsNullOrEmpty(sysCacheKey) && sysCacheSig != null && sysRowsForWrite.Count > 0)
                                VpbLocalDatabase.TryWriteSystemFilesForCacheKey(sysCacheKey, sysCacheSig, sysRowsForWrite);
                        }
                        catch { }
                    }
                }
            }
            }

            if (!fileListFromCache)
            {
                yield return null; // Yield before sorting
                var sortState = GetSortState("Files");
                bool sortSnapStillMatches = fileListSortSnapForWorker != null
                    && sortState.Type == fileListSortSnapForWorker.Type
                    && sortState.Direction == fileListSortSnapForWorker.Direction;
                bool skipMainThreadSort = sortSnapStillMatches
                    && sqliteBulkSortedOnWorkerFlag != null && sqliteBulkSortedOnWorkerFlag[0] != 0
                    && sysLooseFilesAddedCount != null && sysLooseFilesAddedCount[0] == 0;
                var swSortMain = System.Diagnostics.Stopwatch.StartNew();
                if (!skipMainThreadSort)
                    GallerySortManager.Instance.SortFiles(files, sortState);
                swSortMain.Stop();
                if (VPBConfig.IsLogConfigPerfEnabled())
                {
                    try
                    {
                        int sysN = sysLooseFilesAddedCount != null ? sysLooseFilesAddedCount[0] : 0;
                        LogUtil.Log("[VPB.Gallery.SqlIndex] main path=after_refresh drain_wall_ms=" + refreshDrainWallMs
                            + " sort_ms=" + swSortMain.ElapsedMilliseconds
                            + " sort_skipped=" + skipMainThreadSort
                            + " worker_presorted_flag=" + (sqliteBulkSortedOnWorkerFlag != null ? sqliteBulkSortedOnWorkerFlag[0].ToString() : "n/a")
                            + " files_out=" + files.Count
                            + " sys_loose_added=" + sysN
                            + " cat='" + (currentCategoryTitle ?? "") + "'");
                    }
                    catch { }
                }
                if (canFileListCache && fileListSnapKey != null)
                    GalleryFileListSnapshotCache.Put(fileListSnapKey, files);
            }

            // Cache the filtered list for selection operations (Select All, counts, etc)
            lastFilteredFiles.Clear();
            lastFilteredFiles.AddRange(files);
            
            // Promote to class member for RecyclingGridView
            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(files);
            // If no name filter was active, the next SetNameFilter call can use currentFilteredFiles
            // as a trustworthy unfiltered base for in-memory search.
            if (nameFilterTerms == null || nameFilterTerms.Length == 0)
                _topSearchBaseIsClean = true;

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
                recyclingGrid.onCreateItem = () => CreateNewFileButtonGO();
                recyclingGrid.onBindItem = (go, index) => {
                    if (index >= 0 && index < currentFilteredFiles.Count)
                    {
                        int centerIdx = recyclingGrid != null ? recyclingGrid.GetCenterItemIndex() : 0;
                        int dist = Mathf.Abs(index - centerIdx);
                        _nextThumbPriority = Mathf.Min(90, dist * 3); // center=0 (first), edges=higher (later)
                        BindFileButton(go, currentFilteredFiles[index]);
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
                    recyclingGrid.SetGridConfig(100f, ListRowHeight, 5f, 5f, 1);
                    recyclingGrid.SetAdaptiveConfig(true, 0f, 1, true);
                }
                else
                {
                    recyclingGrid.SetGridConfig(100f, GetGridCellConfigHeight(), 10f, 10f, cols);
                    recyclingGrid.SetAdaptiveConfig(true, minSize, cols, false);
                }
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
            }

            // We still need to clear activeButtons if they were used outside recycling grid,
            // but RecyclingGridView manages its own pool now.
            foreach (var btn in activeButtons)
            {
                if (btn != null) Destroy(btn);
            }
            activeButtons.Clear();
            fileButtonImages.Clear();

            UpdatePaginationText();

            if (earlyMetaNeeded)
            {
                if (!skipEarlyMetaThread)
                    while (!earlyMetaBuildDone) yield return null;

                if (metaBuildGroupId == currentLoadingGroupId)
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
                    }
                    if (!skipEarlyMetaThread && sideMetaCacheKey != null && earlyBuildCreators && earlyBuildCats
                        && earlyNewCreators != null && earlyNewCatCounts != null)
                        StoreSharedSideMetaIfRoom(sideMetaCacheKey, earlyNewCreators, earlyNewCatCounts);
                }
            }

            LogGalleryCategoryTypeNavPhase("RefreshFilesRoutine_before_UpdateLayout");
            UpdateLayout();
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
            if (_sideTabsNeedFullRebuildAfterFirstRefresh)
                _sideTabsNeedFullRebuildAfterFirstRefresh = false;

            HideLoadingOverlay();
            hasLoadedContent = true;
            refreshCoroutine = null;
            CompletePaneLoadTimingIfPending();

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
                LogUtil.Log($"[VPB] RefreshFilesRoutine: {skippedForNoCache[0]} packages had no cache yet; scheduling one-shot retry in 3s.");
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
            try { UpdateTabsImpl(rebuildSideTabLists: true, rebuildSubPaneSideTabLists: false); } catch { }
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
            if (tagParallelWaiterForThisRefresh != null)
            {
                while (!tagParallelWaiterForThisRefresh.Finished)
                {
                    if (deferredSubPaneSessionWhenScheduled != _deferredSubPaneSessionId)
                        yield break;
                    yield return null;
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
        }

        private IEnumerator PostFilesListHideAndSortFollowupRoutine(string groupId, bool keepScroll, bool scrollToBottom, float targetScrollNormalizedPos)
        {
            yield return null;
            yield return null;

            if (groupId != currentLoadingGroupId || currentFilteredFiles == null) yield break;

            try { PackageHidePrefs.RebuildHideMarkerCache(); } catch { }

            bool showHidden = false;
            try { showHidden = VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; } catch { }
            bool keepHiddenForSort = FilesSortKeepsHiddenInList();

            if (!showHidden && !keepHiddenForSort)
            {
                for (int i = currentFilteredFiles.Count - 1; i >= 0; i--)
                {
                    if (groupId != currentLoadingGroupId) yield break;
                    try
                    {
                        if (PackageHidePrefs.IsExcludedByGalleryHideFilter(currentFilteredFiles[i]))
                        {
                            currentFilteredFiles.RemoveAt(i);
                        }
                    }
                    catch { }

                    if (i % 2000 == 0)
                        yield return null;
                }
            }

            if (groupId != currentLoadingGroupId) yield break;

            try
            {
                SortState st = GetSortState("Files");
                int beforeExclusive = currentFilteredFiles.Count;
                ApplyFilesSortExclusiveFiltersInPlace(currentFilteredFiles, st.Type);

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
                List<FileEntry> filtered;
                var deps = pkg.RecursivePackageDependencies;

                if (PackageFilterUsesPackageListRows())
                {
                    HashSet<string> uids = BuildUidSetForDependenciesFilter(pkg);
                    filtered = BuildPackageListEntriesForUids(uids);
                    currentPackageFilterCount = Math.Max(0, uids.Count - 1);
                }
                else
                {
                    filtered = new List<FileEntry> { file };
                    AddVarFileEntriesWithPackageInDepList(filtered, file, currentFilteredFiles, deps);
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
                filtered = new List<FileEntry> { file };
                AddVarFileEntriesThatDependOnPackageUid(filtered, file, currentFilteredFiles, targetUid, targetShort);
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
            RefreshRecycleGridAfterFilterChange();
            try { UpdateTabs(); } catch { }
            try { UpdatePaginationText(); } catch { }
            ScrollGalleryToTop();
        }

        /// <summary>Clear all filter levels and restore the original unfiltered list.</summary>
        public void ClearPackageFilter()
        {
            if (_filterStack.Count == 0) return;

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
