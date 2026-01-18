using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private bool PassesFilters(FileEntry entry)
        {
            if (entry == null) return false;

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

            if (FileManager.PackagesByUid != null)
            {
                foreach (var pkg in FileManager.PackagesByUid.Values)
                {
                    string filterCreator = currentCreator;
                    if (!string.IsNullOrEmpty(filterCreator))
                    {
                        if (string.IsNullOrEmpty(pkg.Creator) || pkg.Creator != filterCreator) continue;
                    }

                    if (pkg.FileEntries != null)
                    {
                        foreach (var entry in pkg.FileEntries)
                        {
                            // Time-based yield
                            if (yieldWatch.ElapsedMilliseconds > maxMsPerFrame)
                            {
                                yield return null;
                                yieldWatch.Reset();
                                yieldWatch.Start();
                            }

                            if (IsMatch(entry, currentPaths, currentPath, extensions) && PassesFilters(entry))
                            {
                                files.Add(entry);
                            }
                        }
                    }
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
                                sysFiles = Directory.GetFiles(searchPath, "*." + ext, SearchOption.AllDirectories);
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

            refreshCoroutine = null;
            LogUtil.Log("RefreshFilesRoutine took: " + swTotal.ElapsedMilliseconds + "ms");
        }
    }
}
