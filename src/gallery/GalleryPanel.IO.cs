using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private bool PassesFilters(FileEntry entry)
        {
            if (entry == null) return false;

            // Clothing gender filter (Gallery left Tags panel)
            // Only applies when browsing Clothing category and user selected Female/Male.
            string title = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            bool isClothing = title.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isClothing && !string.IsNullOrEmpty(currentClothingGenderFilter) && currentClothingGenderFilter != "All")
            {
                ClothingLoadingUtils.ResourceKind k;
                ClothingLoadingUtils.ResourceGender g;
                ClothingLoadingUtils.ClassifyClothingHairPath(entry.Path, out k, out g);
                if (k != ClothingLoadingUtils.ResourceKind.Clothing) return false;
                if (currentClothingGenderFilter == "Female" && g != ClothingLoadingUtils.ResourceGender.Female) return false;
                if (currentClothingGenderFilter == "Male" && g != ClothingLoadingUtils.ResourceGender.Male) return false;
            }

            // Status Filter
            if (!string.IsNullOrEmpty(currentStatus))
            {
                if (currentStatus == "Hidden") { if (!entry.IsHidden()) return false; }
                else if (currentStatus == "Loaded") { if (!entry.IsInstalled()) return false; }
                else if (currentStatus == "Unloaded") { if (entry.IsInstalled()) return false; }
                else if (currentStatus == "Autoinstall") { if (!entry.IsAutoInstall()) return false; }
                else if (currentStatus == "Favorites")
                {
                    int rating = RatingsManager.Instance.GetRating(entry);
                    if (string.IsNullOrEmpty(currentRatingFilter) || currentRatingFilter == "All Ratings")
                    {
                        if (rating <= 0) return false;
                    }
                    else
                    {
                        if (currentRatingFilter == "5 Stars") { if (rating != 5) return false; }
                        else if (currentRatingFilter == "4 Stars") { if (rating != 4) return false; }
                        else if (currentRatingFilter == "3 Stars") { if (rating != 3) return false; }
                        else if (currentRatingFilter == "2 Stars") { if (rating != 2) return false; }
                        else if (currentRatingFilter == "1 Star") { if (rating != 1) return false; }
                        else if (currentRatingFilter == "No Ratings") { if (rating != 0) return false; }
                    }
                }
                else if (currentStatus == "Size")
                {
                    if (string.IsNullOrEmpty(currentSizeFilter) || currentSizeFilter == "All Sizes")
                    {
                        if (entry.Size <= 0) return false;
                    }
                    else
                    {
                        long size = entry.Size;
                        long mb = 1024 * 1024;
                        if (currentSizeFilter == "Tiny (< 10MB)") { if (size >= 10 * mb) return false; }
                        else if (currentSizeFilter == "Small (10-100MB)") { if (size < 10 * mb || size >= 100 * mb) return false; }
                        else if (currentSizeFilter == "Medium (100-500MB)") { if (size < 100 * mb || size >= 500 * mb) return false; }
                        else if (currentSizeFilter == "Large (500MB-1GB)") { if (size < 500 * mb || size >= 1024 * mb) return false; }
                        else if (currentSizeFilter == "Very Large (> 1GB)") { if (size < 1024 * mb) return false; }
                    }
                }
            }

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
                string pathLower = entry.Path.ToLowerInvariant();
                foreach (var tag in activeTags)
                {
                    // Check path-based tags (original logic)
                    if (pathLower.Contains(tag.ToLowerInvariant()))
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

        public void RefreshFiles(bool keepScroll = false, bool scrollToBottom = false)
        {
            if (IsHubMode)
            {
                RefreshHubItems();
                return;
            }
            if (thumbnailCacheCoroutine != null) StopCoroutine(thumbnailCacheCoroutine);
            thumbnailCacheCoroutine = null;
            if (pendingThumbnailCacheJobs != null) pendingThumbnailCacheJobs.Clear();
            ShowLoadingOverlay("Loading...");
            if (refreshCoroutine != null) StopCoroutine(refreshCoroutine);
            refreshCoroutine = StartCoroutine(RefreshFilesRoutine(keepScroll, scrollToBottom));
        }

        private IEnumerator RefreshFilesRoutine(bool keepScroll, bool scrollToBottom)
        {
            yield return null; // Allow UI to render first
            var swTotal = System.Diagnostics.Stopwatch.StartNew();
            
            if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
            {
                CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
            }
            currentLoadingGroupId = Guid.NewGuid().ToString();
            
            if (contentGO != null)
            {
                UIGridAdaptive adaptive = contentGO.GetComponent<UIGridAdaptive>();
                if (adaptive != null)
                {
                    adaptive.isVerticalCard = (layoutMode == GalleryLayoutMode.VerticalCard);
                    adaptive.forcedColumnCount = gridColumnCount;
                    adaptive.UpdateGrid();
                }
            }

            List<FileEntry> files = new List<FileEntry>();
            string[] extensions = string.IsNullOrEmpty(currentExtension) ? new string[0] : currentExtension.Split('|');
            bool hasNameFilter = !string.IsNullOrEmpty(nameFilterLower);
            
            // Time-based yielding configuration
            var yieldWatch = new System.Diagnostics.Stopwatch();
            long maxMsPerFrame = 10; // Allow 10ms of work per frame
            
            yieldWatch.Start();

            if (leftActiveContent == ContentType.ActiveItems || rightActiveContent == ContentType.ActiveItems)
            {
                List<FileEntry> activeEntries = GetActiveSceneEntries();
                foreach (var entry in activeEntries)
                {
                    if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                    {
                        yield return null;
                        yieldWatch.Reset();
                        yieldWatch.Start();
                    }

                    if (PassesFilters(entry))
                    {
                        // Check name filter if set
                        if (hasNameFilter)
                        {
                            if (entry.Path.IndexOf(nameFilterLower, StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                        }
                        files.Add(entry);
                    }
                }
            }
            else if (FileManager.PackagesByUid != null)
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

                                DateTime entryTime = new DateTime(ticks[i], DateTimeKind.Utc).ToLocalTime();
                                long entrySize = (sizes != null && i < sizes.Count) ? sizes[i] : 0;
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
                        if (PassesFilters(entry))
                        {
                            files.Add(entry);
                        }

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
                                if (PassesFilters(sysEntry))
                                {
                                    files.Add(sysEntry);
                                }
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

            // NOW clear the old buttons, just before we are ready to show new ones.
            foreach (var btn in activeButtons)
            {
                btn.SetActive(false);
                if (btn.name.StartsWith("NavButton_"))
                {
                    navButtonPool.Push(btn);
                }
                else
                {
                    fileButtonPool.Push(btn);
                }
            }
            activeButtons.Clear();
            fileButtonImages.Clear();

            int totalFiles = files.Count;
            int totalPages = Mathf.CeilToInt((float)totalFiles / itemsPerPage);
            if (totalPages == 0) totalPages = 1;
            lastTotalItems = totalFiles;
            lastTotalPages = totalPages;
            
            if (currentPage >= totalPages) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;
            
            UpdatePaginationText();

            if (paginationPrevBtn != null) 
                paginationPrevBtn.GetComponent<Button>().interactable = (currentPage > 0);
            
            if (paginationNextBtn != null) 
                paginationNextBtn.GetComponent<Button>().interactable = (currentPage < totalPages - 1);

            if (paginationFirstBtn != null)
                paginationFirstBtn.GetComponent<Button>().interactable = (currentPage > 0);
            
            if (paginationLastBtn != null)
                paginationLastBtn.GetComponent<Button>().interactable = (currentPage < totalPages - 1);

            int startIndex = currentPage * itemsPerPage;
            int endIndex = Mathf.Min(startIndex + itemsPerPage, totalFiles);
            lastShownCount = Mathf.Max(0, endIndex - startIndex);

            lastPageFiles.Clear();
            for (int i = startIndex; i < endIndex; i++) lastPageFiles.Add(files[i]);
            UpdatePaginationText();

            if (currentPage > 0)
            {
                GameObject prevBtn = InjectButton("Previous\nPage", PrevPage);
                prevBtn.transform.SetAsFirstSibling();
            }

            int createdCount = 0;
            int firstBatchSize = 32;
            yieldWatch.Reset();
            yieldWatch.Start();

            for (int i = startIndex; i < endIndex; i++)
            {
                try
                {
                    CreateFileButton(files[i]);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[VPB] Error creating button for " + files[i].Name + ": " + ex.ToString());
                }

                createdCount++;

                if (createdCount > firstBatchSize)
                {
                     if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                     {
                         yield return null;
                         yieldWatch.Reset();
                         yieldWatch.Start();
                     }
                }
            }

            if (currentPage < totalPages - 1)
            {
                GameObject nextBtn = InjectButton("Next\nPage", NextPage);
                nextBtn.transform.SetAsLastSibling();
            }
            
            if (scrollRect != null && !keepScroll)
            {
                scrollRect.verticalNormalizedPosition = scrollToBottom ? 0f : 1f;
            }

            HideLoadingOverlay();
            refreshCoroutine = null;
            LogUtil.Log("RefreshFilesRoutine took: " + swTotal.ElapsedMilliseconds + "ms");
        }
    }
}
