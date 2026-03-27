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
        private List<FileEntry> currentFilteredFiles = new List<FileEntry>();
        private RecyclingGridView recyclingGrid;

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
            if (thumbnailCacheCoroutine != null) StopCoroutine(thumbnailCacheCoroutine);
            thumbnailCacheCoroutine = null;
            if (pendingThumbnailCacheJobs != null) pendingThumbnailCacheJobs.Clear();
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
            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            // Reset pose facet counts for this refresh
            posePeopleFacetCountSingle = 0;
            posePeopleFacetCountDual = 0;
            
            if (!string.IsNullOrEmpty(currentLoadingGroupId) && CustomImageLoaderThreaded.singleton != null)
            {
                CustomImageLoaderThreaded.singleton.CancelGroup(currentLoadingGroupId);
            }
            currentLoadingGroupId = Guid.NewGuid().ToString();

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
                        _nextThumbPriority = Mathf.Max(10, 100 - dist * 3);
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
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
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

            // Apply scroll position. scrollToBottom (pagination) always wins.
            // For keepScroll (auto-refresh / layout change): restore via item index so the same row stays
            // centered even if the column count or content height changed.
            // For category change / first load: fall back to the saved normalised position.
            if (scrollRect != null)
            {
                if (scrollToBottom)
                {
                    scrollRect.verticalNormalizedPosition = 0f;
                    if (recyclingGrid != null) recyclingGrid.Refresh();
                }
                else if (savedCenterItemIndex >= 0 && recyclingGrid != null)
                {
                    recyclingGrid.ScrollToCenterItem(savedCenterItemIndex);
                }
                else
                {
                    scrollRect.verticalNormalizedPosition = savedScrollNormalizedPos;
                    if (recyclingGrid != null) recyclingGrid.Refresh();
                }
            }

            UpdateLayout();
            HideLoadingOverlay();
            hasLoadedContent = true;
            refreshCoroutine = null;
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
    }
}
