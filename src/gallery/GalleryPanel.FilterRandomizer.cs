using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MVR.FileManagement;
using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Filter-preset randomizer: apply saved filter set, wait for refresh, LoadRandom, optionally restore UI.
    /// Warm path — not per-frame. Reuses <see cref="ApplyQuickFilterState"/> + <see cref="LoadRandom"/>.
    /// With preserveUi, runs as quiet background refresh (frozen grid, no overlay / layout thrash).
    /// Merged presets run each leaf member in order (e.g. clothing then pose).
    /// Multi-member Replace: full family wipe once (ClothingItem is region-only otherwise), then Add
    /// subsequent members so the rolled set stacks on a clean wardrobe.
    /// Single clothing/hair dice: region replace only — keep other-category garments.
    /// Rapid re-dice / merge: drain deferred clothing applies before wipe and between wear steps.
    /// </summary>
    public partial class GalleryPanel
    {
        private Coroutine _filterRandomizeCo;
        private int _filterRandomizeGen;

        // Cached yields — warm path; avoid per-wait alloc (Unity scripting strategies).
        private static readonly WaitForEndOfFrame s_FilterRandWaitEof = new WaitForEndOfFrame();

        /// <summary>
        /// Random load from a saved filter preset. Default restores previous gallery view after load
        /// so browsing context stays put (background randomize).
        /// </summary>
        public void RandomizeFromFilterPreset(QuickFilterEntry entry, bool preserveUi = true)
        {
            if (entry == null) return;
            try
            {
                if (_filterRandomizeCo != null)
                {
                    try { StopCoroutine(_filterRandomizeCo); } catch { }
                    _filterRandomizeCo = null;
                }
                // StopCoroutine skips finally — clear quiet freeze if a prior run left it on.
                if (_quietGalleryRefresh)
                {
                    try { EndQuietGalleryRefresh(); } catch { _quietGalleryRefresh = false; _quietDisplayFiles.Clear(); }
                }
                // Force-clear sticky override + abort deferred clothing/hair from superseded LoadRandom.
                try { ClearDragDropReplaceOverride(); } catch { }
                try { InvalidateClothingApplySerial(); } catch { }
                _filterRandomizeGen++;
                int gen = _filterRandomizeGen;
                _filterRandomizeCo = StartCoroutine(RandomizeFromFilterPresetRoutine(entry, preserveUi, gen));
            }
            catch (Exception ex)
            {
                try { LogUtil.LogWarning("[VPB] RandomizeFromFilterPreset failed: " + ex.Message); } catch { }
            }
        }

        private static List<QuickFilterEntry> ResolveRandomizeSteps(QuickFilterEntry entry)
        {
            var steps = new List<QuickFilterEntry>();
            if (entry == null) return steps;
            if (entry.IsMerged)
            {
                QuickFilterEntry.CollectMergeLeaves(
                    entry, steps, GalleryUiDesignTokens.QuickFiltersMergeMaxMembers);
                return steps;
            }
            steps.Add(entry);
            return steps;
        }

        private void BeginQuietGalleryRefresh()
        {
            _quietGalleryRefresh = true;
            _quietDisplayFiles.Clear();
            if (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                _quietDisplayFiles.AddRange(currentFilteredFiles);

            // Redirect bind so scroll during quiet rebuild still shows frozen cells.
            if (recyclingGrid != null && _quietDisplayFiles.Count > 0)
            {
                recyclingGrid.onBindItem = (go, index) =>
                {
                    if (index >= 0 && index < _quietDisplayFiles.Count)
                    {
                        int centerIdx = recyclingGrid != null ? recyclingGrid.CachedCenterItemIndex : 0;
                        int dist = Mathf.Abs(index - centerIdx);
                        _nextThumbPriority = Mathf.Min(90, dist * 3);
                        BindFileButton(go, _quietDisplayFiles[index]);
                    }
                };
            }
        }

        private void EndQuietGalleryRefresh()
        {
            _quietGalleryRefresh = false;
            _quietDisplayFiles.Clear();

            // Restore normal bind. Do not Refresh/SetItemCount — frozen cells already match
            // pre-randomize view; a rebind here can flash if hide-follow-up was skipped.
            if (recyclingGrid != null && currentFilteredFiles != null)
            {
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
            }
        }

        private IEnumerator WaitForFilterRefresh(int gen)
        {
            yield return null;
            if (gen != _filterRandomizeGen) yield break;

            int guard = 0;
            while (refreshCoroutine != null && guard < 600)
            {
                if (gen != _filterRandomizeGen) yield break;
                guard++;
                yield return null;
            }
            if (gen != _filterRandomizeGen) yield break;
            if (guard == 0)
            {
                yield return null;
                if (gen != _filterRandomizeGen) yield break;
            }
        }

        /// <summary>
        /// Drain deferred clothing/hair applies (serial-invalidated or just finished).
        /// Lets superseded toggles hit serial checks and End before wipe/load of next dice.
        /// </summary>
        private IEnumerator WaitForClothingApplySettle(int gen)
        {
            // One frame so StartCoroutine work can BeginClothingApplyWork before we poll.
            yield return null;
            if (gen != _filterRandomizeGen) yield break;
            yield return s_FilterRandWaitEof;
            if (gen != _filterRandomizeGen) yield break;

            int guard = 0;
            while (HasPendingClothingApplyWork() && guard < 300)
            {
                if (gen != _filterRandomizeGen) yield break;
                guard++;
                yield return null;
            }

            // Geometry/Clear stick after last End.
            yield return s_FilterRandWaitEof;
            if (gen != _filterRandomizeGen) yield break;
            yield return null;
        }

        /// <summary>
        /// Clothing / hair category for merged-random replace-once semantics.
        /// </summary>
        private static int ClassifyFilterWearFamily(QuickFilterEntry step, string categoryTitle, string categoryPath)
        {
            if (step != null)
            {
                // Subfilter bits survive even when title/path strings are empty on leaf snapshots.
                if (step.HairSubfilter != 0) return 2;
                if (step.ClothingSubfilter != 0) return 1;
            }

            string title = !string.IsNullOrEmpty(step != null ? step.CategoryTitle : null)
                ? step.CategoryTitle
                : (categoryTitle ?? "");
            string path = !string.IsNullOrEmpty(step != null ? step.CategoryPath : null)
                ? step.CategoryPath
                : (categoryPath ?? "");
            string t = title.ToLowerInvariant();
            string p = (path ?? "").Replace('\\', '/').ToLowerInvariant();

            if (t.IndexOf("hair", StringComparison.Ordinal) >= 0
                || p.IndexOf("/hair", StringComparison.Ordinal) >= 0)
                return 2; // hair
            if (t.IndexOf("clothing", StringComparison.Ordinal) >= 0
                || p.IndexOf("/clothing", StringComparison.Ordinal) >= 0)
                return 1; // clothing
            return 0;
        }

        /// <summary>
        /// When Replace ON for a merged set: first clothing/hair member → replace, later same family → add.
        /// Non-wear members leave null (honor config). Never force-add over Replace for unclassified steps.
        /// </summary>
        private static bool? ResolveMultiReplaceOverride(
            bool wantReplace,
            int family,
            ref bool clothingReplaceUsed,
            ref bool hairReplaceUsed)
        {
            if (!wantReplace) return null;
            if (family == 1)
            {
                if (!clothingReplaceUsed)
                {
                    clothingReplaceUsed = true;
                    return true;
                }
                return false;
            }
            if (family == 2)
            {
                if (!hairReplaceUsed)
                {
                    hairReplaceUsed = true;
                    return true;
                }
                return false;
            }
            return null;
        }

        /// <summary>
        /// Full family wipe before first merged clothing/hair member with Replace ON.
        /// ClothingItem path is region-only — without this, later Add members stack on old wardrobe.
        /// Single-preset dice must NOT call this (keep other-category garments).
        /// </summary>
        private void WipeWearFamilyForFilterRandom(int family)
        {
            Atom target = null;
            try { target = GetBestTargetAtom(); } catch { target = null; }
            if (target == null) return;

            if (family == 1)
            {
                try { ClothingLoadingUtils.RemoveRealGarmentClothing(target); } catch { }
            }
            else if (family == 2)
            {
                try { ClothingLoadingUtils.RemoveAllHair(target); } catch { }
            }
        }

        private IEnumerator RandomizeFromFilterPresetRoutine(QuickFilterEntry entry, bool preserveUi, int gen)
        {
            if (entry == null) yield break;

            List<QuickFilterEntry> steps = ResolveRandomizeSteps(entry);
            if (steps == null || steps.Count == 0) yield break;

            string presetName = entry.Name ?? "";
            bool multi = steps.Count > 1;
            if (multi)
            {
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("quickfilters.randomizing_merge", "Randomizing {0} filters from '{1}'…"),
                        steps.Count,
                        presetName),
                    1.5f);
            }
            else
            {
                ShowTemporaryStatus(
                    string.Format(
                        VPBTranslation.T("quickfilters.randomizing", "Randomizing from '{0}'…"),
                        presetName),
                    1.5f);
            }

            // Rapid re-dice: let invalidated deferred applies End before we wipe/load again.
            yield return WaitForClothingApplySettle(gen);
            if (gen != _filterRandomizeGen) yield break;

            QuickFilterEntry restore = null;
            if (preserveUi)
            {
                try { restore = CaptureQuickFilterState(); } catch { restore = null; }
                BeginQuietGalleryRefresh();
            }

            // Snapshot persisted replace flag (getter may later return override).
            bool wantReplace = VPBConfig.Instance != null && VPBConfig.Instance.DragDropReplaceMode;
            bool clothingReplaceUsed = false;
            bool hairReplaceUsed = false;

            var loadedNames = new List<string>();

            try
            {
                for (int si = 0; si < steps.Count; si++)
                {
                    if (gen != _filterRandomizeGen) yield break;
                    QuickFilterEntry step = steps[si];
                    if (step == null) continue;

                    try { ApplyQuickFilterState(step, false, quietUi: preserveUi); }
                    catch (Exception ex)
                    {
                        try { LogUtil.LogWarning("[VPB] Randomize: apply preset failed: " + ex.Message); } catch { }
                        continue;
                    }

                    yield return WaitForFilterRefresh(gen);
                    if (gen != _filterRandomizeGen) yield break;

                    var pool = (currentFilteredFiles != null && currentFilteredFiles.Count > 0)
                        ? currentFilteredFiles
                        : lastFilteredFiles;

                    if (pool == null || pool.Count == 0)
                    {
                        string stepName = !string.IsNullOrEmpty(step.Name) ? step.Name : (step.CategoryTitle ?? "?");
                        ShowTemporaryStatus(
                            string.Format(
                                VPBTranslation.T("quickfilters.random_empty", "No items in preset '{0}'"),
                                stepName),
                            2f);
                        continue;
                    }

                    // Merged + Replace: first wear member wipe+replace, later same family force Add.
                    // Single preset: null override → region replace from config (keep other categories).
                    bool? replaceOverride = null;
                    int family = ClassifyFilterWearFamily(step, currentCategoryTitle, currentPath);
                    bool wipeFamily = false;
                    if (multi && wantReplace && (family == 1 || family == 2))
                    {
                        replaceOverride = ResolveMultiReplaceOverride(
                            wantReplace, family, ref clothingReplaceUsed, ref hairReplaceUsed);
                        wipeFamily = replaceOverride == true;
                    }

                    if (wipeFamily)
                    {
                        // Prior step / prior dice deferred toggles must be dead before Clear.
                        yield return WaitForClothingApplySettle(gen);
                        if (gen != _filterRandomizeGen) yield break;

                        WipeWearFamilyForFilterRandom(family);
                        yield return s_FilterRandWaitEof;
                        if (gen != _filterRandomizeGen) yield break;
                        yield return null;
                        if (gen != _filterRandomizeGen) yield break;
                    }

                    bool historyBrowse = activeContentType == ContentType.History;
                    string excludeKey = null;
                    try { excludeKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse); } catch { excludeKey = null; }
                    if (string.IsNullOrEmpty(excludeKey))
                    {
                        try { excludeKey = selectedPath; } catch { excludeKey = null; }
                    }

                    string loadedName = null;
                    try
                    {
                        // Override held only for sync LoadRandom — never across yields.
                        LoadRandom(excludeKey, replaceOverride, gen);
                        try
                        {
                            if (!string.IsNullOrEmpty(selectedPath))
                                loadedName = Path.GetFileName(selectedPath);
                            else if (selectedFiles != null && selectedFiles.Count > 0 && selectedFiles[0] != null)
                            {
                                FileEntry f = selectedFiles[0];
                                loadedName = !string.IsNullOrEmpty(f.Path)
                                    ? Path.GetFileName(f.Path)
                                    : (f.Uid ?? "?");
                            }
                        }
                        catch { loadedName = null; }
                    }
                    catch (Exception ex)
                    {
                        try { LogUtil.LogWarning("[VPB] Randomize: LoadRandom failed: " + ex.Message); } catch { }
                    }

                    if (!string.IsNullOrEmpty(loadedName))
                        loadedNames.Add(loadedName);

                    // Wear loads often defer (preset wait / catalog retry). Drain before next leaf
                    // or before UI restore so rapid re-dice and merge Add see stable geometry.
                    if (family == 1 || family == 2)
                    {
                        yield return WaitForClothingApplySettle(gen);
                        if (gen != _filterRandomizeGen) yield break;
                    }
                    else if (si < steps.Count - 1)
                    {
                        yield return null;
                        if (gen != _filterRandomizeGen) yield break;
                    }
                }

                if (gen != _filterRandomizeGen) yield break;

                if (preserveUi && restore != null)
                {
                    try { ApplyQuickFilterState(restore, false, quietUi: true); } catch { }
                    yield return WaitForFilterRefresh(gen);
                    if (gen != _filterRandomizeGen) yield break;
                }

                if (loadedNames.Count > 0)
                {
                    string joined = string.Join(", ", loadedNames.ToArray());
                    ShowTemporaryStatus(
                        string.Format(
                            VPBTranslation.T("quickfilters.random_loaded", "Random: {0} → {1}"),
                            presetName,
                            joined),
                        2.5f);
                }
                else
                {
                    ShowTemporaryStatus(
                        string.Format(
                            VPBTranslation.T("quickfilters.random_done", "Randomized from '{0}'"),
                            presetName),
                        2f);
                }
            }
            finally
            {
                // Token-scoped — superseded run cannot wipe a newer override owner.
                try { EndDragDropReplaceOverride(gen); } catch { }
                if (preserveUi)
                {
                    try { EndQuietGalleryRefresh(); } catch { }
                }
                if (gen == _filterRandomizeGen)
                    _filterRandomizeCo = null;
            }
        }
    }
}
