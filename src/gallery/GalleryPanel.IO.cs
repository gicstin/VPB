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
        private enum PackageFilterMode
        {
            None = 0,
            Dependencies = 1,
            Dependents = 2,
        }

        private List<FileEntry> currentFilteredFiles = new List<FileEntry>();
        private List<FileEntry> filterBaseFiles = null; // Original list when filtering first activated
        private string filterBaseAnchorKey = null; // Scroll anchor captured when first entering filter mode
        private string currentFilterDesc = null; // Description of active filter (e.g., "Dependents of X.var")
        private PackageFilterMode currentPackageFilterMode = PackageFilterMode.None;
        private string currentPackageFilterMasterUid = null;
        private int currentPackageFilterCount = 0;
        private List<FileEntry> filterSearchBaseFiles = null; // Base list for search within filter mode
        private string filterSearchLower = "";
        private bool filterEnteredFromTopSearch = false;
        private List<FileEntry> topSearchBaseFiles = null; // Base list for top search (non-filter mode)
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

        private void EnsureFilterBaseCaptured()
        {
            if (filterBaseFiles != null) return;
            filterBaseFiles = new List<FileEntry>(currentFilteredFiles);

            // Capture "return point" once for Clear Filter
            filterBaseAnchorKey = null;
            SaveFilterScrollAnchor();
            filterBaseAnchorKey = filterRestoreAnchorKey;

            // Initialize filter-mode search base
            filterSearchBaseFiles = new List<FileEntry>(currentFilteredFiles);
            filterSearchLower = "";

            // If filter mode is entered while the top search is narrowing the list, "Clear Filter"
            // should return to the full category list (not the search snapshot).
            try { filterEnteredFromTopSearch = !string.IsNullOrEmpty(nameFilterLower); } catch { filterEnteredFromTopSearch = false; }
        }

        private void ApplyFilteredList(List<FileEntry> filtered, string desc)
        {
            if (filtered == null) filtered = new List<FileEntry>();

            // Reset filter-mode search base whenever the filter result changes.
            if (IsFilterActive)
            {
                filterSearchBaseFiles = new List<FileEntry>(filtered);
                filterSearchLower = string.IsNullOrEmpty(nameFilterLower) ? "" : nameFilterLower;
                filtered = BuildFilterModeView(filterSearchBaseFiles, filterSearchLower);
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
        }

        public void ApplySearchWithinFilter(string query)
        {
            if (!IsFilterActive) return;
            filterSearchLower = string.IsNullOrEmpty(query) ? "" : query.ToLowerInvariant();

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
        }

        private List<FileEntry> BuildFilterModeView(List<FileEntry> baseList, string searchLower)
        {
            var source = baseList ?? new List<FileEntry>();
            bool needSearch = !string.IsNullOrEmpty(searchLower);
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
                if ((!string.IsNullOrEmpty(p) && p.IndexOf(searchLower, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(n) && n.IndexOf(searchLower, StringComparison.OrdinalIgnoreCase) >= 0))
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

        private List<FileEntry> BuildCategoryEntriesForPackageUids(HashSet<string> uids)
        {
            var result = new List<FileEntry>();
            if (uids == null || uids.Count == 0) return result;

            // Mirror the category/prefix/extension matching logic used in RefreshFilesRoutine / ApplyPackageDelta,
            // but restrict the package set to the UID list.
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            bool hasExt = extensions.Length > 0 && !(extensions.Length == 1 && string.IsNullOrEmpty(extensions[0]));
            bool hasNameFilt = !string.IsNullOrEmpty(nameFilterLower);

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

                    // Path prefix filter
                    bool pathOk = true;
                    if (currentPaths != null && currentPaths.Count > 0)
                    {
                        pathOk = false;
                        for (int p = 0; p < currentPaths.Count; p++)
                        {
                            string pref = currentPaths[p];
                            if (ip.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.Equals(pref, "Saves/Person", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(pref, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (ip.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase)) continue;
                                }
                                pathOk = true;
                                break;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(currentPath))
                    {
                        pathOk = false;
                        if (ip.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.Equals(currentPath, "Saves/Person", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(currentPath, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!ip.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                    pathOk = true;
                            }
                            else pathOk = true;
                        }
                    }
                    if (!pathOk) continue;

                    // Name filter
                    if (hasNameFilt &&
                        pkg.Path.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0 &&
                        ip.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0) continue;

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

            foreach (var uid in uids)
            {
                if (string.IsNullOrEmpty(uid)) continue;
                try
                {
                    // Read-only resolve (no auto-install)
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
            return PassesFilters(entry, false);
        }

        private bool PassesFilters(FileEntry entry, bool ignorePosePeopleFilter)
        {
            if (entry == null) return false;

            // Hide filtering and sort-only narrowing run in PostFilesListHideAndSortFollowupRoutine after the grid is shown.
            // to avoid per-entry FileManager.FileExists calls blocking the scan drain loop.

            // Clothing subfilter (Gallery left Tags panel)
            // Applies only when browsing Clothing category.
            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isClothing)
            {
                // Determine file extension
                string p = entry.Path;
                int lastDot = (p != null) ? p.LastIndexOf('.') : -1;
                string ext = (lastDot >= 0 && lastDot < p.Length - 1) ? p.Substring(lastDot + 1) : "";
                bool isPreset = string.Equals(ext, "vap", StringComparison.OrdinalIgnoreCase);

                string norm = (p ?? "").Replace('\\', '/');
                bool isVarEntry = (entry is VarFileEntry) || ((entry as SystemFileEntry) != null && ((SystemFileEntry)entry).isVar);
                bool isCustomLoose = !isVarEntry &&
                                    (norm.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                                     norm.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase) ||
                                     norm.IndexOf("/Custom/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     norm.IndexOf("/Saves/", StringComparison.OrdinalIgnoreCase) >= 0);

                ClothingLoadingUtils.ResourceKind k;
                ClothingLoadingUtils.ResourceGender g;
                ClothingLoadingUtils.ClassifyClothingHairPath(p, out k, out g);
                if (k != ClothingLoadingUtils.ResourceKind.Clothing) return false;

                bool isDecal = ClothingLoadingUtils.IsDecalLikePath(p);

                // Multi-select subfilter semantics:
                // - No flags selected: show all clothing content.
                // - Real Clothing / Decals: type filters (OR within type group).
                // - Presets/Items/Male/Female: additional constraints (AND).
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
                        // If user selected real-only constraints but didn't explicitly pick type, default to real clothing.
                        if (wantsRealType && isDecal && !wantsDecalType) return false;
                    }

                    // Additional constraints
                    bool wantsPresets = (clothingSubfilter & ClothingSubfilter.Presets) != 0;
                    bool wantsCustom = (clothingSubfilter & ClothingSubfilter.Custom) != 0;
                    if (wantsPresets) { if (!isPreset) return false; }
                    if (wantsCustom) { if (!isCustomLoose) return false; }
                    if ((clothingSubfilter & ClothingSubfilter.Items) != 0) { if (isPreset) return false; }
                    if ((clothingSubfilter & ClothingSubfilter.Male) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Male) return false; }
                    if ((clothingSubfilter & ClothingSubfilter.Female) != 0) { if (g != ClothingLoadingUtils.ResourceGender.Female) return false; }
                }
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
            if (!string.IsNullOrEmpty(nameFilterLower))
            {
                if (entry.Path.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0)
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

            // Check if gallery auto-refresh is suppressed (during scene/preset loading)
            if (Gallery.IsSuppressed())
            {
                LogUtil.Log("[VPB] GalleryPanel.RefreshFiles: SKIPPED (suppressed)");
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
            // Rotate the group ID here (synchronously) so that any in-flight thumbnail callbacks
            // from the old category fail the capturedGroupId == currentLoadingGroupId guard and
            // don't pollute the new session. The coroutine's yield-return-null would be too late.
            if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
                CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
            currentLoadingGroupId = Guid.NewGuid().ToString();
            if (refreshCoroutine != null) StopCoroutine(refreshCoroutine);
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
                bool hasNameFilt = !string.IsNullOrEmpty(nameFilterLower);

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
                                if (ip.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (string.Equals(pref, "Saves/Person", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(pref, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (ip.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase)) continue;
                                    }
                                    pathOk = true;
                                    break;
                                }
                            }
                        }
                        else if (!string.IsNullOrEmpty(currentPath))
                        {
                            pathOk = false;
                            if (ip.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.Equals(currentPath, "Saves/Person", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(currentPath, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!ip.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                        pathOk = true;
                                }
                                else pathOk = true;
                            }
                        }
                        if (!pathOk) continue;

                        // Name filter
                        if (hasNameFilt &&
                            pkg.Path.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0 &&
                            ip.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0) continue;

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

        private IEnumerator RefreshFilesRoutine(bool keepScroll, bool scrollToBottom)
        {
            yield return null; // Allow UI to render first

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
            // Use keepScroll (not useCenterItemRestore) so that if keepScroll=true but hasLoadedContent
            // is still false (e.g. package scan completed while the initial load coroutine was mid-run
            // and got cancelled before reaching hasLoadedContent=true), we still capture the current
            // scroll position rather than falling back to _pendingScrollRestore (which defaults to 1f/top).
            float savedScrollNormalizedPos = keepScroll
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
                        recyclingGrid.SetGridConfig(100f, 100f, 10f, 10f, GridColumnCount);
                        recyclingGrid.SetAdaptiveConfig(true, 200f, GridColumnCount, false);
                    }
                    recyclingGrid.SetItemCount(0); // Clear initially
                }
            }
            
            List<FileEntry> files = new List<FileEntry>();
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            bool hasNameFilter = !string.IsNullOrEmpty(nameFilterLower);

            string titleForCounts = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            bool isPoseCategory = titleForCounts.IndexOf("Pose", StringComparison.OrdinalIgnoreCase) >= 0;

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
            
            // Time-based yielding configuration
            var yieldWatch = new System.Diagnostics.Stopwatch();
            long maxMsPerFrame = 10; // Allow 10ms of work per frame

            yieldWatch.Start();

            int[] skippedForNoCache = { 0 };

            if (FileManager.PackagesByUid != null)
            {
                string localLoadingGroupId = currentLoadingGroupId;

                Queue<FileEntry> candidateQueue = new Queue<FileEntry>();
                object candidateQueueLock = new object();
                int workerDoneFlag = 0;

                ThreadPool.QueueUserWorkItem((state) =>
                {
                    try
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

                                bool pathOk = true;
                                if (currentPaths != null && currentPaths.Count > 0)
                                {
                                    pathOk = false;
                                    for (int p = 0; p < currentPaths.Count; p++)
                                    {
                                        string pref = currentPaths[p];
                                        if (checkPath.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (string.Equals(pref, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(pref, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                                            {
                                                if (checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
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
                                    if (checkPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (string.Equals(currentPath, "Saves/Person", StringComparison.OrdinalIgnoreCase) || string.Equals(currentPath, "Saves/Person/", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (!checkPath.StartsWith("Saves/Person/appearance", StringComparison.OrdinalIgnoreCase))
                                                pathOk = true;
                                        }
                                        else
                                        {
                                            pathOk = true;
                                        }
                                    }
                                }
                                if (!pathOk) continue;

                                if (hasNameFilter)
                                {
                                    if (pkg.Path.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0
                                        && internalPath.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0)
                                    {
                                        continue;
                                    }
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
                        Interlocked.Exchange(ref workerDoneFlag, 1);
                    }
                });

                // Drain results incrementally on main thread
                while (true)
                {
                    if (localLoadingGroupId != currentLoadingGroupId)
                    {
                        HideLoadingOverlay();
                        refreshCoroutine = null;
                        yield break;
                    }

                    FileEntry entry;
                    bool hadWork = false;
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

                        bool baseOk = PassesFilters(entry, true);
                        if (!baseOk) continue;

                        int pcPose = 1;
                        bool needPc = wantsPoseCounts || (posePeopleFilter != PosePeopleFilter.All);
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
                            if (RatingsManager.Instance.GetRating(entry) <= 0) continue;
                        }

                        files.Add(entry);

                        if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                        {
                            yield return null;
                            yieldWatch.Reset();
                            yieldWatch.Start();
                        }
                    }

                    if (!hadWork && Interlocked.CompareExchange(ref workerDoneFlag, 0, 0) == 1)
                    {
                        break;
                    }
                    yield return null;
                }
            }

            List<string> pathsToSearch = new List<string>();
            if (currentPaths != null && currentPaths.Count > 0) pathsToSearch.AddRange(currentPaths);
            else if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath)) pathsToSearch.Add(currentPath);

            if (activeContentType == ContentType.Category)
            {
                if (string.IsNullOrEmpty(currentCreator))
                {
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
                            }
                        }
                    }
                }
            }

            yield return null; // Yield before sorting
            var sortState = GetSortState("Files");
            GallerySortManager.Instance.SortFiles(files, sortState);

            // Cache the filtered list for selection operations (Select All, counts, etc)
            lastFilteredFiles.Clear();
            lastFilteredFiles.AddRange(files);
            
            // Promote to class member for RecyclingGridView
            currentFilteredFiles.Clear();
            currentFilteredFiles.AddRange(files);

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
                    recyclingGrid.SetGridConfig(100, 100, 10f, 10f, cols);
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

            // Build creator and category caches on a background thread so the main thread
            // doesn't block for ~2 s iterating all 19k+ packages synchronously.
            if (!creatorsCached || !categoriesCached)
            {
                // Snapshot all state the background thread will need.
                string _bCreator    = currentCreator;
                string _bExtension  = currentExtension;
                List<string> _bPaths = currentPaths != null ? new List<string>(currentPaths) : null;
                string _bPath       = currentPath;
                var _bCategories    = categories != null ? new List<Gallery.Category>(categories) : null;

                bool _buildCreators = !creatorsCached;
                bool _buildCats     = !categoriesCached;

                List<CreatorCacheEntry> _newCreators = null;
                Dictionary<string, int> _newCatCounts = null;
                bool _buildDone = false;

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (_buildCreators)
                        {
                            var counts = new Dictionary<string, int>();
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
                                        { for (int k = 0; k < _bPaths.Count; k++) if (ip.StartsWith(_bPaths[k], StringComparison.OrdinalIgnoreCase)) { match = true; break; } }
                                        else if (!string.IsNullOrEmpty(_bPath))
                                            match = ip.StartsWith(_bPath, StringComparison.OrdinalIgnoreCase);
                                        else match = true;
                                        if (match) { int cur; counts.TryGetValue(pkg.Creator, out cur); counts[pkg.Creator] = cur + 1; }
                                    }
                                }
                            }
                            _newCreators = counts.Select(kv => new CreatorCacheEntry { Name = kv.Key, Count = kv.Value })
                                                 .OrderBy(c => c.Name).ToList();
                        }

                        if (_buildCats && _bCategories != null)
                        {
                            var catCounts2 = new Dictionary<string, int>();
                            var extToCats2 = new Dictionary<string, List<Gallery.Category>>(StringComparer.OrdinalIgnoreCase);
                            foreach (var c in _bCategories)
                            {
                                catCounts2[c.name] = 0;
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
                                                { for (int k = 0; k < cat2.paths.Count; k++) if (ip.StartsWith(cat2.paths[k], StringComparison.OrdinalIgnoreCase)) { pm = true; break; } }
                                                else if (!string.IsNullOrEmpty(cat2.path)) pm = ip.StartsWith(cat2.path, StringComparison.OrdinalIgnoreCase);
                                                else pm = true;
                                                if (pm) { catCounts2[cat2.name]++; break; }
                                            }
                                        }
                                    }
                                }
                            }
                            _newCatCounts = catCounts2;
                        }
                    }
                    catch { }
                    finally { _buildDone = true; }
                });

                while (!_buildDone) yield return null;

                // Apply results on the main thread.
                if (_buildCreators)
                {
                    cachedCreators = _newCreators ?? new List<CreatorCacheEntry>();
                    creatorsCached = true;
                }
                if (_buildCats)
                {
                    if (_newCatCounts != null)
                    {
                        categoryCounts.Clear();
                        foreach (var kv in _newCatCounts) categoryCounts[kv.Key] = kv.Value;
                    }
                    categoriesCached = true;
                }
            }

            UpdateLayout();
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

            HideLoadingOverlay();
            hasLoadedContent = true;
            refreshCoroutine = null;

            // Defer hide filtering until after the grid is visible (prescan .hide markers then filter in a coroutine).
            // Always run follow-up: hide strip (unless sort needs hidden rows), then Hidden-only / AutoInstall-only narrowing, then re-sort.
            try
            {
                StartCoroutine(PostFilesListHideAndSortFollowupRoutine(currentLoadingGroupId));
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
                try { UpdateTabs(); } catch { }
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

        private bool FilesSortKeepsHiddenInList()
        {
            try
            {
                SortType t = GetSortState("Files").Type;
                return t == SortType.Hidden || t == SortType.HiddenOnly;
            }
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
        }

        private IEnumerator PostFilesListHideAndSortFollowupRoutine(string groupId)
        {
            yield return null;
            yield return null;

            if (groupId != currentLoadingGroupId || currentFilteredFiles == null) yield break;

            try { PackageHidePrefs.RebuildHideMarkerCache(); } catch { }

            bool showHidden = false;
            try { showHidden = VPBConfig.Instance != null && VPBConfig.Instance.GalleryShowHiddenPackages; } catch { }
            bool keepHiddenForSort = FilesSortKeepsHiddenInList();

            bool anyRemoved = false;
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
                            anyRemoved = true;
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
                if (currentFilteredFiles.Count != beforeExclusive)
                    anyRemoved = true;
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

        /// <summary>Clear any active filter and restore the full list.</summary>
        public void ClearPackageFilter()
        {
            if (filterBaseFiles != null)
            {
                currentFilteredFiles.Clear();
                currentFilteredFiles.AddRange(filterBaseFiles);
                filterBaseFiles = null;
                currentFilterDesc = null;
                currentPackageFilterMode = PackageFilterMode.None;
                currentPackageFilterMasterUid = null;
                currentPackageFilterCount = 0;
                filterSearchBaseFiles = null;
                filterSearchLower = "";
                bool enteredFromSearch = filterEnteredFromTopSearch;
                filterEnteredFromTopSearch = false;

                RefreshRecycleGridAfterFilterChange();

                try { UpdateTabs(); } catch { }
                try { UpdatePaginationText(); } catch { }

                // Restore scroll after the grid has rebound.
                filterRestoreAnchorKey = filterBaseAnchorKey;
                filterBaseAnchorKey = null;
                if (filterRestoreCoroutine != null)
                {
                    try { StopCoroutine(filterRestoreCoroutine); } catch { }
                    filterRestoreCoroutine = null;
                }
                try { filterRestoreCoroutine = StartCoroutine(RestoreFilterScrollAnchorNextFrame()); } catch { }

                // If the user entered filter mode while a top search was active, clearing the filter should
                // return to the full category list (not the search-limited snapshot).
                if (enteredFromSearch)
                {
                    try
                    {
                        nameFilter = "";
                        nameFilterLower = "";
                        if (titleSearchInput != null) titleSearchInput.text = "";
                    }
                    catch { }
                    try
                    {
                        // Restore full list instantly via in-memory top search base.
                        if (topSearchBaseFiles != null)
                        {
                            currentFilteredFiles.Clear();
                            currentFilteredFiles.AddRange(topSearchBaseFiles);
                            topSearchBaseFiles = null;
                            RefreshRecycleGridAfterFilterChange();
                            try { UpdatePaginationText(); } catch { }
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>Returns whether a filter is currently active.</summary>
        public bool IsFilterActive => filterBaseFiles != null;

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
