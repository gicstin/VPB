using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel
    {
        private sealed class SearchRescueContext
        {
            public int Token;
            public string RawQuery;
            public GallerySearchQuery Query;
            public string CategoryTitle;
            public string Extension;
            public string CreatorFilter;
            public bool CreatorFilterCaseInsensitive;
            public int LoadedState;
            public int PkgVersionFilter;
            public string LicenseFilter;
            public ClothingSubfilter ClothingSub;
            public HairSubfilter HairSub;
            public SceneHubSubfilter SceneHubSub;
            public List<string> PathExclusions;
            public List<string> PathInclusions;
            public List<string> HubItemScope;
            public HashSet<string> ActiveTags;
            public HashSet<string> UserTags;
            public HashSet<string> ExcludedUserTags;
            public bool UserTagsUntaggedOnly;
            public bool UserTagsTaggedOnly;
            public bool UserTagsRequireAll;
            public bool HasBrowseFilters;
            public bool IsEverything;
        }

        private const float SearchRescueRowHeightRef = GalleryUiDesignTokens.ControlSlotHeightRef;
        private const float SearchRescueDebounceSeconds = 0.4f;

        private static int s_SearchRescueWorkerBusy;

#pragma warning disable 649
        private GameObject _searchRescueRowsGO;
#pragma warning restore 649
        private readonly List<GameObject> _searchRescueRowGOs = new List<GameObject>(GallerySearchRescue.MaxOptions);
        private readonly List<GallerySearchRescueOption> _searchRescueOptions = new List<GallerySearchRescueOption>(GallerySearchRescue.MaxOptions);
        private Coroutine _searchRescueCo;
        private int _searchRescueToken;
        private string _searchRescueEvaluatedKey;
        private volatile List<GallerySearchRescueOption> _searchRescueWorkerResult;
        private volatile int _searchRescueWorkerToken = -1;

        private bool SearchRescueEnabled
        {
            get
            {
                try { return VPBConfig.Instance == null || VPBConfig.Instance.SearchRescueEnabled; }
                catch { return true; }
            }
        }

        private bool CanRunSearchRescue()
        {
            if (!SearchRescueEnabled) return false;
            if (!VpbSqlite3.IsAvailable) return false;
            if (activeContentType != ContentType.Category) return false;
            if (string.Equals(currentExtension ?? "", "varpkg", StringComparison.OrdinalIgnoreCase)) return false;
            if (!HasActiveNameFilter()) return false;
            if (IsFilterActive) return false;
            return true;
        }

        private string BuildSearchRescueKey()
        {
            return (currentCategoryTitle ?? "") + "\u0001" + (currentExtension ?? "") + "\u0001" + (nameFilter ?? "");
        }

        private void MaybeStartSearchRescue()
        {
            if (!CanRunSearchRescue())
            {
                CancelSearchRescue(clearRows: true);
                return;
            }

            string key = BuildSearchRescueKey();
            if (string.Equals(key, _searchRescueEvaluatedKey, StringComparison.Ordinal)) return;

            CancelSearchRescue(clearRows: true);
            _searchRescueEvaluatedKey = key;

            if (!isActiveAndEnabled)
            {
                _searchRescueEvaluatedKey = null;
                return;
            }
            _searchRescueCo = StartCoroutine(SearchRescueRoutine(key));
        }

        private void CancelSearchRescue(bool clearRows)
        {
            _searchRescueToken++;
            if (_searchRescueCo != null)
            {
                try { StopCoroutine(_searchRescueCo); } catch { }
                _searchRescueCo = null;
            }
            _searchRescueEvaluatedKey = null;
            if (clearRows && (_searchRescueOptions.Count > 0 || _searchRescueRowGOs.Count > 0))
            {
                _searchRescueOptions.Clear();
                RebuildSearchRescueRows();
            }
        }

        private SearchRescueContext CaptureSearchRescueContext()
        {
            try
            {
                var ctx = new SearchRescueContext();
                ctx.Token = ++_searchRescueToken;
                ctx.RawQuery = nameFilter ?? "";
                ctx.Query = nameFilterQuery ?? GallerySearchQuery.Empty;
                ctx.CategoryTitle = !string.IsNullOrEmpty(currentCategoryTitle)
                    ? currentCategoryTitle
                    : ((titleText != null ? titleText.text : "") ?? "");
                if (string.IsNullOrEmpty(ctx.CategoryTitle)) return null;
                ctx.Extension = currentExtension ?? "";
                ctx.CreatorFilter = GetCreatorFilterForQueries();
                ctx.CreatorFilterCaseInsensitive = GalleryConsolidateCreatorNamesEnabled;
                ctx.IsEverything = Gallery.IsEverythingCategoryName(ctx.CategoryTitle);

                ctx.LoadedState = -1;
                try
                {
                    if (FilesSortWantsLoadedOnly()) ctx.LoadedState = 1;
                    else if (FilesSortWantsUnloadedOnly()) ctx.LoadedState = 0;
                }
                catch { }

                ctx.PkgVersionFilter = VpbLocalDatabase.PkgVersionFilterOff;
                try
                {
                    if (_browseOldVersionsCycle == BrowseFilterCycle.Apply)
                        ctx.PkgVersionFilter = VpbLocalDatabase.PkgVersionFilterNewestOnly;
                    else if (_browseOldVersionsCycle == BrowseFilterCycle.Only)
                        ctx.PkgVersionFilter = VpbLocalDatabase.PkgVersionFilterOldOnly;
                    else if (Settings.Instance != null && Settings.Instance.HideOldVersions != null
                        && Settings.Instance.HideOldVersions.Value)
                        ctx.PkgVersionFilter = VpbLocalDatabase.PkgVersionFilterNewestOnly;
                }
                catch { }

                ctx.LicenseFilter = currentLicenseFilter ?? "";
                ctx.ClothingSub = clothingSubfilter;
                ctx.HairSub = hairSubfilter;
                try { ctx.SceneHubSub = EffectiveSceneHubSubfilter(); } catch { ctx.SceneHubSub = 0; }

                string path = currentPath ?? "";
                if (currentPaths != null)
                {
                    for (int i = 0; i < currentPaths.Count; i++)
                    {
                        if (string.Equals(currentPaths[i], "Saves/Person", StringComparison.OrdinalIgnoreCase))
                        {
                            if (ctx.PathExclusions == null) ctx.PathExclusions = new List<string>();
                            ctx.PathExclusions.Add("Saves/Person/appearance");
                        }
                    }
                }
                else if (string.Equals(path, "Saves/Person", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.PathExclusions = new List<string> { "Saves/Person/appearance" };
                }

                bool appearanceLocalOnly = ctx.CategoryTitle.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                    && ResolveEffectiveSourceFilterMode(true, path) == 1;
                if (appearanceLocalOnly && path.Length > 0)
                {
                    ctx.PathInclusions = new List<string>();
                    ctx.PathInclusions.Add(path.Replace('\\', '/').TrimEnd('/'));
                }

                try
                {
                    List<string> scope = EffectiveHubItemScopeCategories();
                    if (scope != null) ctx.HubItemScope = new List<string>(scope);
                }
                catch { }

                if (activeTags != null && activeTags.Count > 0)
                    ctx.ActiveTags = new HashSet<string>(activeTags);

                ctx.UserTagsUntaggedOnly = _userTagAvailMode == UserTagAvailMode.FilterUntagged;
                ctx.UserTagsTaggedOnly = _userTagAvailMode == UserTagAvailMode.FilterTaggedOnly;
                bool armed = !ctx.UserTagsUntaggedOnly && !ctx.UserTagsTaggedOnly && IsUserTagIncludeExcludeFilterArmed();
                if (armed && activeUserTags != null && activeUserTags.Count > 0)
                {
                    ctx.UserTags = new HashSet<string>(activeUserTags, StringComparer.OrdinalIgnoreCase);
                    ctx.UserTagsRequireAll = UserTagFilterRequiresAllTags();
                }
                if (armed && excludedUserTags != null && excludedUserTags.Count > 0)
                    ctx.ExcludedUserTags = new HashSet<string>(excludedUserTags, StringComparer.OrdinalIgnoreCase);

                ctx.HasBrowseFilters = HasActiveBrowseFiltersExcludingTitleSearch();
                return ctx;
            }
            catch { return null; }
        }

        private IEnumerator SearchRescueRoutine(string key)
        {
            float settle = Time.realtimeSinceStartup + SearchRescueDebounceSeconds;
            while (Time.realtimeSinceStartup < settle)
                yield return null;

            if (!CanRunSearchRescue()
                || !string.Equals(BuildSearchRescueKey(), key, StringComparison.Ordinal))
            {
                _searchRescueCo = null;
                yield break;
            }

            SearchRescueContext ctx = CaptureSearchRescueContext();
            if (ctx == null) { _searchRescueCo = null; yield break; }

            VpbLocalDatabase.EnsureSearchVocabularyAsync();
            bool vocabWasReady = VpbLocalDatabase.IsSearchVocabularyReady;

            yield return RunSearchRescuePass(ctx);
            if (ctx.Token != _searchRescueToken) { _searchRescueCo = null; yield break; }

            if (vocabWasReady || _searchRescueOptions.Count >= GallerySearchRescue.MaxOptions)
            {
                _searchRescueCo = null;
                yield break;
            }

            float vocabDeadline = Time.realtimeSinceStartup + 60f;
            while (!VpbLocalDatabase.IsSearchVocabularyReady)
            {
                if (Time.realtimeSinceStartup > vocabDeadline || ctx.Token != _searchRescueToken)
                {
                    _searchRescueCo = null;
                    yield break;
                }
                yield return null;
            }

            yield return RunSearchRescuePass(ctx);
            _searchRescueCo = null;
        }

        private IEnumerator RunSearchRescuePass(SearchRescueContext ctx)
        {
            float leaseDeadline = Time.realtimeSinceStartup + 20f;
            while (Interlocked.CompareExchange(ref s_SearchRescueWorkerBusy, 1, 0) != 0)
            {
                if (Time.realtimeSinceStartup > leaseDeadline || ctx.Token != _searchRescueToken) yield break;
                yield return null;
            }

            _searchRescueWorkerResult = null;
            _searchRescueWorkerToken = -1;
            bool queued = false;
            try
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    List<GallerySearchRescueOption> options = null;
                    try { options = EvaluateSearchRescueOnWorker(ctx); }
                    catch { options = null; }
                    finally { Interlocked.Exchange(ref s_SearchRescueWorkerBusy, 0); }
                    _searchRescueWorkerResult = options ?? new List<GallerySearchRescueOption>();
                    _searchRescueWorkerToken = ctx.Token;
                });
                queued = true;
            }
            catch { queued = false; }

            if (!queued)
            {
                Interlocked.Exchange(ref s_SearchRescueWorkerBusy, 0);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 30f;
            while (_searchRescueWorkerToken != ctx.Token)
            {
                if (Time.realtimeSinceStartup > deadline) yield break;
                yield return null;
            }

            if (ctx.Token != _searchRescueToken) yield break;
            PublishSearchRescueResult(_searchRescueWorkerResult);
        }

        private void PublishSearchRescueResult(List<GallerySearchRescueOption> result)
        {
            _searchRescueOptions.Clear();
            if (result != null)
            {
                for (int i = 0; i < result.Count && i < GallerySearchRescue.MaxOptions; i++)
                    _searchRescueOptions.Add(result[i]);
            }
            RebuildSearchRescueRows();
        }

        private static List<GallerySearchRescueOption> EvaluateSearchRescueOnWorker(SearchRescueContext ctx)
        {
            if (ctx == null) return null;

            var options = new List<GallerySearchRescueOption>(GallerySearchRescue.MaxOptions);

            if (!ctx.IsEverything)
            {
                int n = CountSearchRescueRows(ctx, ctx.Query, allCategories: true, dropBrowseFilters: true, creatorOverride: null);
                if (n > 0)
                {
                    options.Add(new GallerySearchRescueOption
                    {
                        Kind = GallerySearchRescueKind.AllCategories,
                        Count = n,
                    });
                }
            }

            if (options.Count < GallerySearchRescue.MaxOptions && ctx.HasBrowseFilters)
            {
                int n = CountSearchRescueRows(ctx, ctx.Query, allCategories: false, dropBrowseFilters: true, creatorOverride: null);
                if (n > 0)
                {
                    options.Add(new GallerySearchRescueOption
                    {
                        Kind = GallerySearchRescueKind.ClearFilters,
                        Count = n,
                    });
                }
            }

            if (options.Count < GallerySearchRescue.MaxOptions)
            {
                string anyWord = GallerySearchRescue.BuildAnyWordQuery(ctx.Query);
                if (!string.IsNullOrEmpty(anyWord))
                {
                    GallerySearchQuery relaxed = GallerySearchQuery.Parse(anyWord);
                    int n = CountSearchRescueRows(ctx, relaxed, allCategories: false, dropBrowseFilters: false, creatorOverride: null);
                    if (n > 0)
                    {
                        options.Add(new GallerySearchRescueOption
                        {
                            Kind = GallerySearchRescueKind.AnyWord,
                            Query = anyWord,
                            Count = n,
                        });
                    }
                }
            }

            var broadTokens = new List<string>(4);
            GallerySearchRescue.CollectBroadTokens(ctx.RawQuery, ctx.Query, broadTokens);
            while (broadTokens.Count > GallerySearchRescue.MaxTypoTokensProbed)
                broadTokens.RemoveAt(broadTokens.Count - 1);

            if (options.Count < GallerySearchRescue.MaxOptions && broadTokens.Count > 0)
            {
                for (int i = 0; i < broadTokens.Count; i++)
                {
                    string term = broadTokens[i];
                    string suggestion;
                    if (!VpbLocalDatabase.TryFindSearchVocabularySuggestion(term, out suggestion))
                        continue;

                    string corrected = GallerySearchRescue.BuildTermReplacementQuery(ctx.RawQuery, ctx.Query, term, suggestion);
                    if (string.IsNullOrEmpty(corrected)) continue;

                    GallerySearchQuery relaxed = GallerySearchQuery.Parse(corrected);
                    int n = CountSearchRescueRows(ctx, relaxed, allCategories: false, dropBrowseFilters: true, creatorOverride: null);
                    if (n <= 0)
                        n = CountSearchRescueRows(ctx, relaxed, allCategories: true, dropBrowseFilters: true, creatorOverride: null);
                    if (n <= 0) continue;

                    options.Add(new GallerySearchRescueOption
                    {
                        Kind = GallerySearchRescueKind.DidYouMean,
                        Query = corrected,
                        Payload = suggestion,
                        Count = n,
                    });
                    break;
                }
            }

            return options;
        }

        private static int CountSearchRescueRows(
            SearchRescueContext ctx,
            GallerySearchQuery query,
            bool allCategories,
            bool dropBrowseFilters,
            string creatorOverride)
        {
            try
            {
                string title = allCategories ? Gallery.EverythingCategoryName : ctx.CategoryTitle;
                string ext = allCategories ? Gallery.EverythingExtensionToken : ctx.Extension;
                string creator = creatorOverride != null
                    ? creatorOverride
                    : (dropBrowseFilters ? "" : ctx.CreatorFilter);

                int n;
                bool ok = VpbLocalDatabase.TryCountGalleryCategoryRows(
                    title,
                    ext,
                    creator,
                    query ?? GallerySearchQuery.Empty,
                    dropBrowseFilters ? 0 : ctx.ClothingSub,
                    dropBrowseFilters ? 0 : ctx.HairSub,
                    dropBrowseFilters ? 0 : ctx.SceneHubSub,
                    dropBrowseFilters ? -1 : ctx.LoadedState,
                    allCategories ? null : ctx.PathExclusions,
                    allCategories ? null : ctx.PathInclusions,
                    dropBrowseFilters ? null : ctx.ActiveTags,
                    dropBrowseFilters ? null : ctx.UserTags,
                    dropBrowseFilters ? false : ctx.UserTagsUntaggedOnly,
                    dropBrowseFilters ? false : ctx.UserTagsRequireAll,
                    dropBrowseFilters ? null : ctx.ExcludedUserTags,
                    ctx.PkgVersionFilter,
                    dropBrowseFilters ? false : ctx.UserTagsTaggedOnly,
                    dropBrowseFilters ? "" : ctx.LicenseFilter,
                    allCategories ? null : ctx.HubItemScope,
                    VpbLocalDatabase.SearchRescueCountCap,
                    out n);

                return ok ? n : 0;
            }
            catch { return 0; }
        }

        private string DescribeSearchRescueOption(GallerySearchRescueOption o)
        {
            string count = o.Count >= VpbLocalDatabase.SearchRescueCountCap
                ? o.Count.ToString() + "+"
                : o.Count.ToString();
            switch (o.Kind)
            {
                case GallerySearchRescueKind.AllCategories:
                    return VPBTranslation.T("gallery.rescue.all_categories", "{n} in All categories")
                        .Replace("{n}", count);
                case GallerySearchRescueKind.ClearFilters:
                    return VPBTranslation.T("gallery.rescue.clear_filters", "{n} with filters cleared")
                        .Replace("{n}", count);
                case GallerySearchRescueKind.AnyWord:
                    return VPBTranslation.T("gallery.rescue.any_word", "{n} match any word")
                        .Replace("{n}", count);
                case GallerySearchRescueKind.DidYouMean:
                    return VPBTranslation.T("gallery.rescue.did_you_mean", "Did you mean “{w}”? ({n})")
                        .Replace("{w}", o.Payload ?? "")
                        .Replace("{n}", count);
            }
            return count;
        }

        private void RebuildSearchRescueRows()
        {
            if (_searchRescueRowsGO == null) return;

            for (int i = 0; i < _searchRescueRowGOs.Count; i++)
            {
                GameObject go = _searchRescueRowGOs[i];
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _searchRescueRowGOs.Clear();

            float s = ChromeScale;
            if (s <= 0.01f) s = 1f;
            float rowH = SearchRescueRowHeightRef * s;
            int rowFont = GalleryUiMetrics.ScaledFontSize(
                GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);

            bool any = _searchRescueOptions.Count > 0;
            LayoutElement rowsLE = _searchRescueRowsGO.GetComponent<LayoutElement>();
            if (rowsLE != null)
            {
                rowsLE.preferredHeight = any
                    ? _searchRescueOptions.Count * rowH
                        + (_searchRescueOptions.Count - 1) * UI.GapControl(s)
                    : 0f;
            }
            _searchRescueRowsGO.SetActive(any);
            if (!any) return;

            for (int i = 0; i < _searchRescueOptions.Count; i++)
            {
                GallerySearchRescueOption o = _searchRescueOptions[i];
                GameObject row = UI.CreateChromeLayoutButton(
                    _searchRescueRowsGO.transform,
                    -1f,
                    rowH,
                    DescribeSearchRescueOption(o),
                    rowFont,
                    GalleryUiColorTokens.SurfaceMid,
                    null);
                if (row == null) continue;

                GallerySearchRescueOption captured = o;
                Button btn = row.GetComponent<Button>();
                if (btn != null) btn.onClick.AddListener(() => ApplySearchRescueOption(captured));

                try { AddTooltipPlain(row, BuildSearchRescueTooltip(o)); } catch { }
                _searchRescueRowGOs.Add(row);
            }
        }

        private string BuildSearchRescueTooltip(GallerySearchRescueOption o)
        {
            switch (o.Kind)
            {
                case GallerySearchRescueKind.AllCategories:
                    return VPBTranslation.T("gallery.rescue.all_categories_tip",
                        "Switch to the EVERYTHING category and keep this search. Ctrl+Z undoes.");
                case GallerySearchRescueKind.ClearFilters:
                    return VPBTranslation.T("gallery.rescue.clear_filters_tip",
                        "Drop the active browse filters and keep this search.");
                case GallerySearchRescueKind.AnyWord:
                    return VPBTranslation.T("gallery.rescue.any_word_tip",
                        "Match any word instead of all of them (OR). Ctrl+Z undoes.");
                case GallerySearchRescueKind.DidYouMean:
                    return VPBTranslation.T("gallery.rescue.did_you_mean_tip",
                        "Replace the misspelled word and search again. Ctrl+Z undoes.");
            }
            return "";
        }

        private void ApplySearchRescueOption(GallerySearchRescueOption o)
        {
            string previousQuery = nameFilter ?? "";
            string previousCategory = currentCategoryTitle ?? "";

            switch (o.Kind)
            {
                case GallerySearchRescueKind.AllCategories:
                    PushSearchRescueUndo(previousQuery, previousCategory);
                    SwitchToEverythingCategoryKeepingSearch();
                    break;

                case GallerySearchRescueKind.ClearFilters:
                    try { ClearBrowseFiltersKeepingSearch(); } catch { }
                    break;

                default:
                    if (string.IsNullOrEmpty(o.Query)) return;
                    PushSearchRescueUndo(previousQuery, null);
                    ApplySearchRescueQuery(o.Query, pushUndo: false);
                    break;
            }
        }

        private void PushSearchRescueUndo(string previousQuery, string previousCategory)
        {
            string queryCopy = previousQuery ?? "";
            string categoryCopy = previousCategory;
            try
            {
                PushUndo(() =>
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(categoryCopy))
                        {
                            Gallery.Category cat;
                            if (TryGetCategoryByName(categoryCopy, out cat))
                                ApplyCategoryQuickPick(cat);
                        }
                    }
                    catch { }
                    try { ApplySearchRescueQuery(queryCopy, pushUndo: false); } catch { }
                    try
                    {
                        ShowTemporaryStatus(
                            VPBTranslation.T("gallery.rescue.undo_ok", "Restored previous search."), 2f);
                    }
                    catch { }
                }, VPBTranslation.T("gallery.undo.search_rescue", "Search suggestion"));
            }
            catch { }
        }

        private void ApplySearchRescueQuery(string query, bool pushUndo)
        {
            string q = query ?? "";
            if (pushUndo) PushSearchRescueUndo(nameFilter ?? "", null);

            bool hadChips = HasTitleSearchChips();
            SetNameFilter(q);

            if (hadChips)
            {
                try { HydrateTitleSearchChipsFromCurrentFilter(); } catch { }
                try { SetTitleSearchDraftText("", null); } catch { }
                try { RebuildTitleSearchChipUi(); } catch { }
            }
            else
            {
                try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, q, _titleBarSearchOnValueChanged); } catch { }
            }

            try { SyncBrowseFilterChipChrome(); } catch { }
            try { SyncTitleBarSearchBackdrop(); } catch { }
            try { UpdateEmptyGridState(); } catch { }
        }

        private void SwitchToEverythingCategoryKeepingSearch()
        {
            string keep = nameFilter ?? "";
            Gallery.Category cat;
            if (!TryGetCategoryByName(Gallery.EverythingCategoryName, out cat))
            {
                try
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.rescue.no_everything", "EVERYTHING category is not available."), 2.5f);
                }
                catch { }
                return;
            }

            try { ApplyCategoryQuickPick(cat); } catch { }
            if (!isActiveAndEnabled) return;
            StartCoroutine(ReapplySearchAfterCategorySwitch(keep));
        }

        private IEnumerator ReapplySearchAfterCategorySwitch(string keep)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!Gallery.IsEverythingCategoryName(currentCategoryTitle ?? ""))
            {
                if (Time.realtimeSinceStartup > deadline) yield break;
                yield return null;
            }
            yield return null;

            if (string.Equals(nameFilter ?? "", keep ?? "", StringComparison.Ordinal))
            {
                try { UpdateEmptyGridState(); } catch { }
                yield break;
            }
            ApplySearchRescueQuery(keep, pushUndo: false);
        }
    }
}
