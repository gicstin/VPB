using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        private static readonly Color PluginsFloatCreatorRowBg = GalleryUiColorTokens.SurfaceDark;

        /// <summary>One visible tree line (creator / package / child) for Plugins float virtualization.</summary>
        private sealed class PluginsFloatFlatRow
        {
            public FileEntry Entry;
            public bool IsCreator;
            public bool IsChild;
            public bool CanExpand;
            public bool Expanded;
            public string ExpandKey;
            public string CreatorName;
            public string DisplayName;
            public int Version; // -1 = none / loose
            public int IndentLevel; // 0 creator, 1 package, 2 child
            public int ChildCountDisplay; // -1 unknown, else known count
        }

        private List<PluginsFloatCreatorGroup> _pluginsFloatCreators = new List<PluginsFloatCreatorGroup>();
        private string[] _pluginsFloatFilterTerms = new string[0];
        private readonly StringBuilder _pluginsFloatHaystackSb = new StringBuilder(128);
        /// <summary>One-shot: seed expand set when filter text changes (user can still collapse).</summary>
        private bool _pluginsFloatSearchAutoExpandPending;

        private void RefreshPluginsFloatTree(bool resetScroll)
        {
            if (_pluginsFloatRowsParent == null || _pluginsFloatScrollRect == null) return;

            StopPluginsFloatTreePopulate();

            try
            {
                var prevByKey = new Dictionary<string, PluginsFloatRoot>(StringComparer.OrdinalIgnoreCase);
                if (_pluginsFloatRoots != null)
                {
                    for (int i = 0; i < _pluginsFloatRoots.Count; i++)
                    {
                        PluginsFloatRoot pr = _pluginsFloatRoots[i];
                        if (pr == null || pr.Entry == null || !pr.IsCslist) continue;
                        string k = PluginsFloatExpandKey(pr.Entry);
                        if (!string.IsNullOrEmpty(k)) prevByKey[k] = pr;
                    }
                }

                IList<FileEntry> source = _pluginsFloatCatalog;
                _pluginsFloatRoots = BuildPluginsFloatRoots(source);

                if (prevByKey.Count > 0 && _pluginsFloatRoots != null)
                {
                    for (int i = 0; i < _pluginsFloatRoots.Count; i++)
                    {
                        PluginsFloatRoot root = _pluginsFloatRoots[i];
                        if (root == null || !root.IsCslist || root.Entry == null) continue;
                        string k = PluginsFloatExpandKey(root.Entry);
                        PluginsFloatRoot prev;
                        if (string.IsNullOrEmpty(k) || !prevByKey.TryGetValue(k, out prev) || prev == null)
                            continue;
                        if (!prev.ChildrenLoaded) continue;
                        root.Children = prev.Children;
                        root.ChildrenLoaded = true;
                        root.MaxRating = prev.MaxRating;
                    }
                }

                _pluginsFloatCreators = GroupPluginsFloatByCreator(_pluginsFloatRoots);
                _pluginsFloatCreators.Sort(ComparePluginsFloatCreatorGroups);
                RebuildPluginsFloatVirtView(source);

                EnsurePluginsFloatScrollHook();
                ApplyPluginsFloatContentHeight();

                if (resetScroll)
                {
                    try { _pluginsFloatScrollRect.verticalNormalizedPosition = 1f; } catch { }
                    _pluginsFloatVirtLastFirstIdx = -1;
                }

                int flat = _pluginsFloatVirtView.Count;
                UpdatePluginsFloatEmptyChrome(source, visibleRows: flat, visibleRoots: CountPluginsFloatVisiblePackages(), populating: false);
                UpdatePluginsFloatVirtualVisible(force: true);
            }
            catch (Exception ex)
            {
                try
                {
                    LogUtil.LogError("[VPB.PluginsFloat] RefreshPluginsFloatTree FAIL: "
                        + (ex != null ? ex.Message : "?"));
                }
                catch { }
                _pluginsFloatSearchAutoExpandPending = false;
            }
        }

        private int CountPluginsFloatVisiblePackages()
        {
            int n = 0;
            for (int i = 0; i < _pluginsFloatVirtView.Count; i++)
            {
                PluginsFloatFlatRow r = _pluginsFloatVirtView[i];
                if (r != null && !r.IsCreator && !r.IsChild) n++;
            }
            return n;
        }

        private sealed class PluginsFloatScoredCreator
        {
            public string CreatorLabel;
            public string ExpandKey;
            public int BestScore;
            public readonly List<PluginsFloatRoot> Roots = new List<PluginsFloatRoot>(8);
            public readonly List<List<FileEntry>> ChildLists = new List<List<FileEntry>>(8);
            public readonly List<bool> Expanded = new List<bool>(8);
            public readonly List<int> Scores = new List<int>(8);
        }

        private void RebuildPluginsFloatVirtView(IList<FileEntry> source)
        {
            _pluginsFloatVirtView.Clear();
            if (_pluginsFloatCreators == null) return;

            string filter = _pluginsFloatFilter ?? "";
            _pluginsFloatFilterTerms = SplitSearchTerms(filter);
            bool hasFilter = _pluginsFloatFilterTerms != null && _pluginsFloatFilterTerms.Length > 0;

            // One catalog pass for multi-level hits. Never OpenStream/Ensure all cslists on keystroke
            // (that froze/crashed VaM on large libraries).
            HashSet<string> pkgsHitByCatalog = null;
            if (hasFilter)
                pkgsHitByCatalog = BuildPluginsFloatPackageUidsMatchingTerms(source, _pluginsFloatFilterTerms);

            var scoredCreators = new List<PluginsFloatScoredCreator>(
                hasFilter ? Math.Min(64, _pluginsFloatCreators.Count) : _pluginsFloatCreators.Count);

            for (int gi = 0; gi < _pluginsFloatCreators.Count; gi++)
            {
                PluginsFloatCreatorGroup group = _pluginsFloatCreators[gi];
                if (group == null || group.Roots == null || group.Roots.Count == 0) continue;
                if (_pluginsFloatRatedOnly && group.MaxRating <= 0) continue;

                // Real creator key for scoring (not translated "Loose / Custom" label).
                string creatorKeyName = group.Creator ?? "";
                string creatorLabel = string.IsNullOrEmpty(creatorKeyName)
                    ? VPBTranslation.T("gallery.plugins.creator_loose", "Loose / Custom")
                    : creatorKeyName;

                PluginsFloatScoredCreator bucket = null;

                for (int ri = 0; ri < group.Roots.Count; ri++)
                {
                    PluginsFloatRoot root = group.Roots[ri];
                    if (root == null || root.Entry == null) continue;
                    if (_pluginsFloatRatedOnly && root.MaxRating <= 0) continue;

                    bool packageMatch = PluginsFloatRootMatchesTerms(root, creatorKeyName, _pluginsFloatFilterTerms);
                    bool catalogContentMatch = false;
                    if (!packageMatch && hasFilter && pkgsHitByCatalog != null
                        && !string.IsNullOrEmpty(root.PackageUid))
                        catalogContentMatch = pkgsHitByCatalog.Contains(root.PackageUid);

                    // Children only when already expanded — never for search probe.
                    string pkgKey = PluginsFloatExpandKey(root.Entry);
                    bool wantExpanded = root.IsCslist
                        && !string.IsNullOrEmpty(pkgKey)
                        && _pluginsFloatExpanded.Contains(pkgKey);
                    if (wantExpanded)
                        EnsurePluginsFloatRootChildren(root, source);

                    List<FileEntry> visibleChildren = null;
                    bool childMatch = false;
                    if (root.ChildrenLoaded && root.Children != null && root.Children.Count > 0)
                    {
                        visibleChildren = new List<FileEntry>(root.Children.Count);
                        for (int c = 0; c < root.Children.Count; c++)
                        {
                            FileEntry child = root.Children[c];
                            if (child == null) continue;
                            if (hasFilter && !PluginsFloatEntryMatchesTerms(child, creatorKeyName, root, _pluginsFloatFilterTerms))
                                continue;
                            visibleChildren.Add(child);
                            childMatch = true;
                        }
                    }

                    if (hasFilter && !packageMatch && !catalogContentMatch && !childMatch) continue;

                    int score = 0;
                    if (hasFilter)
                    {
                        string entryName = null;
                        try { entryName = root.Entry.Name; } catch { entryName = null; }
                        score = ScorePluginsFloatSearchMatch(
                            creatorKeyName,
                            root.PackageName,
                            entryName,
                            _pluginsFloatFilterTerms);
                    }

                    if (bucket == null)
                    {
                        bucket = new PluginsFloatScoredCreator
                        {
                            CreatorLabel = creatorLabel,
                            ExpandKey = group.ExpandKey,
                            BestScore = score
                        };
                        scoredCreators.Add(bucket);
                    }
                    else if (score > bucket.BestScore)
                    {
                        bucket.BestScore = score;
                    }

                    bucket.Roots.Add(root);
                    bucket.ChildLists.Add(visibleChildren);
                    bucket.Expanded.Add(wantExpanded);
                    bucket.Scores.Add(score);
                }
            }

            if (hasFilter && scoredCreators.Count > 1)
            {
                scoredCreators.Sort(ComparePluginsFloatScoredCreators);
                for (int i = 0; i < scoredCreators.Count; i++)
                    SortPluginsFloatScoredPackages(scoredCreators[i]);
            }

            for (int si = 0; si < scoredCreators.Count; si++)
            {
                PluginsFloatScoredCreator bucket = scoredCreators[si];
                if (bucket == null || bucket.Roots.Count == 0) continue;

                string creatorKey = bucket.ExpandKey;
                if (_pluginsFloatSearchAutoExpandPending && hasFilter && !string.IsNullOrEmpty(creatorKey))
                    _pluginsFloatExpanded.Add(creatorKey);
                bool creatorExpanded = !string.IsNullOrEmpty(creatorKey)
                    && _pluginsFloatExpanded.Contains(creatorKey);

                _pluginsFloatVirtView.Add(new PluginsFloatFlatRow
                {
                    Entry = null,
                    IsCreator = true,
                    IsChild = false,
                    CanExpand = true,
                    Expanded = creatorExpanded,
                    ExpandKey = creatorKey,
                    CreatorName = bucket.CreatorLabel,
                    DisplayName = bucket.CreatorLabel,
                    Version = -1,
                    IndentLevel = 0,
                    ChildCountDisplay = bucket.Roots.Count
                });

                if (!creatorExpanded) continue;

                for (int mi = 0; mi < bucket.Roots.Count; mi++)
                {
                    PluginsFloatRoot root = bucket.Roots[mi];
                    bool expanded = bucket.Expanded[mi];
                    List<FileEntry> kids = bucket.ChildLists[mi];

                    int childCount = 0;
                    if (kids != null)
                        childCount = kids.Count;
                    else if (root.ChildrenLoaded && root.Children != null)
                        childCount = root.Children.Count;
                    else if (root.IsCslist)
                        childCount = -1;

                    string pkgKey = PluginsFloatExpandKey(root.Entry);
                    string display = root.Entry.Name ?? root.PackageName ?? "";

                    _pluginsFloatVirtView.Add(new PluginsFloatFlatRow
                    {
                        Entry = root.Entry,
                        IsCreator = false,
                        IsChild = false,
                        CanExpand = root.IsCslist,
                        Expanded = expanded,
                        ExpandKey = pkgKey,
                        CreatorName = bucket.CreatorLabel,
                        DisplayName = display,
                        Version = root.Version,
                        IndentLevel = 1,
                        ChildCountDisplay = childCount
                    });

                    if (!expanded || kids == null) continue;
                    for (int c = 0; c < kids.Count; c++)
                    {
                        FileEntry child = kids[c];
                        if (child == null) continue;
                        _pluginsFloatVirtView.Add(new PluginsFloatFlatRow
                        {
                            Entry = child,
                            IsCreator = false,
                            IsChild = true,
                            CanExpand = false,
                            Expanded = false,
                            ExpandKey = null,
                            CreatorName = bucket.CreatorLabel,
                            DisplayName = child.Name ?? "",
                            Version = -1,
                            IndentLevel = 2,
                            ChildCountDisplay = 0
                        });
                    }
                }
            }

            _pluginsFloatSearchAutoExpandPending = false;
        }

        /// <summary>
        /// Rank search hits: real creator field ≫ package/entry name ≫ path/uid leftovers.
        /// So "CreatorX PluginY" prefers CreatorX's PluginY over CreatorZ's CreatorX_PluginY rename.
        /// </summary>
        private static int ScorePluginsFloatSearchMatch(
            string creator, string packageName, string entryName, string[] terms)
        {
            if (terms == null || terms.Length == 0) return 0;
            if (creator == null) creator = "";
            if (packageName == null) packageName = "";
            if (entryName == null) entryName = "";

            int score = 0;
            int termsInCreator = 0;
            int termsInPkg = 0;

            for (int i = 0; i < terms.Length; i++)
            {
                string t = terms[i];
                if (string.IsNullOrEmpty(t)) continue;
                bool inCreator = creator.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
                bool inPkg = packageName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                    || entryName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
                if (inCreator)
                {
                    termsInCreator++;
                    score += 10000;
                }
                else if (inPkg)
                {
                    termsInPkg++;
                    score += 100;
                }
                else
                {
                    // Matched only via uid/path/catalog — weakest.
                    score += 1;
                }
            }

            // Split roles: at least one term on creator AND one on package/name.
            if (termsInCreator > 0 && termsInPkg > 0)
                score += 50000;

            // Typed order: first token → creator, later tokens → plugin name.
            if (terms.Length >= 1
                && !string.IsNullOrEmpty(terms[0])
                && creator.IndexOf(terms[0], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 20000;
                if (string.Equals(creator, terms[0], StringComparison.OrdinalIgnoreCase))
                    score += 30000;
            }
            for (int i = 1; i < terms.Length; i++)
            {
                string t = terms[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (packageName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                    || entryName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    score += 5000;
            }

            return score;
        }

        private static int ComparePluginsFloatScoredCreators(PluginsFloatScoredCreator a, PluginsFloatScoredCreator b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int sd = b.BestScore.CompareTo(a.BestScore);
            if (sd != 0) return sd;
            string an = a.CreatorLabel ?? "";
            string bn = b.CreatorLabel ?? "";
            return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
        }

        private static void SortPluginsFloatScoredPackages(PluginsFloatScoredCreator bucket)
        {
            if (bucket == null || bucket.Roots.Count <= 1) return;
            int n = bucket.Roots.Count;
            // Simple insertion sort — small per-creator lists; avoids alloc of parallel arrays.
            for (int i = 1; i < n; i++)
            {
                int score = bucket.Scores[i];
                PluginsFloatRoot root = bucket.Roots[i];
                List<FileEntry> kids = bucket.ChildLists[i];
                bool exp = bucket.Expanded[i];
                int j = i - 1;
                while (j >= 0)
                {
                    int cmp = bucket.Scores[j].CompareTo(score);
                    if (cmp > 0) break;
                    if (cmp == 0)
                    {
                        string an = bucket.Roots[j].PackageName ?? "";
                        string bn = root.PackageName ?? "";
                        if (string.Compare(an, bn, StringComparison.OrdinalIgnoreCase) <= 0) break;
                    }
                    bucket.Scores[j + 1] = bucket.Scores[j];
                    bucket.Roots[j + 1] = bucket.Roots[j];
                    bucket.ChildLists[j + 1] = bucket.ChildLists[j];
                    bucket.Expanded[j + 1] = bucket.Expanded[j];
                    j--;
                }
                bucket.Scores[j + 1] = score;
                bucket.Roots[j + 1] = root;
                bucket.ChildLists[j + 1] = kids;
                bucket.Expanded[j + 1] = exp;
            }
        }

        private bool PluginsFloatRootMatchesTerms(PluginsFloatRoot root, string creatorLabel, string[] terms)
        {
            if (terms == null || terms.Length == 0) return true;
            if (root == null || root.Entry == null) return false;
            string hay = BuildPluginsFloatHaystack(creatorLabel, root, root.Entry);
            return MatchesAllTermsInEither(hay, "", terms);
        }

        private bool PluginsFloatEntryMatchesTerms(FileEntry entry, string creatorLabel, PluginsFloatRoot root, string[] terms)
        {
            if (terms == null || terms.Length == 0) return true;
            if (entry == null) return false;
            string hay = BuildPluginsFloatHaystack(creatorLabel, root, entry);
            return MatchesAllTermsInEither(hay, "", terms);
        }

        private string BuildPluginsFloatHaystack(string creatorLabel, PluginsFloatRoot root, FileEntry entry)
        {
            _pluginsFloatHaystackSb.Length = 0;
            if (!string.IsNullOrEmpty(creatorLabel))
            {
                _pluginsFloatHaystackSb.Append(creatorLabel);
                _pluginsFloatHaystackSb.Append(' ');
            }
            if (root != null)
            {
                if (!string.IsNullOrEmpty(root.PackageName))
                {
                    _pluginsFloatHaystackSb.Append(root.PackageName);
                    _pluginsFloatHaystackSb.Append(' ');
                }
                if (!string.IsNullOrEmpty(root.PackageUid))
                {
                    _pluginsFloatHaystackSb.Append(root.PackageUid);
                    _pluginsFloatHaystackSb.Append(' ');
                }
                if (root.Version >= 0)
                {
                    _pluginsFloatHaystackSb.Append('v');
                    _pluginsFloatHaystackSb.Append(root.Version);
                    _pluginsFloatHaystackSb.Append(' ');
                }
            }
            if (entry != null)
            {
                string n = null;
                try { n = entry.Name; } catch { n = null; }
                if (!string.IsNullOrEmpty(n))
                {
                    _pluginsFloatHaystackSb.Append(n);
                    _pluginsFloatHaystackSb.Append(' ');
                }
                string p = null;
                try { p = entry.Path; } catch { p = null; }
                if (!string.IsNullOrEmpty(p))
                    _pluginsFloatHaystackSb.Append(p);
            }
            return _pluginsFloatHaystackSb.ToString();
        }

        /// <summary>
        /// Single O(n) catalog scan: package UIDs where any file matches all terms.
        /// Covers nested script names without parsing every .cslist on the main thread.
        /// </summary>
        private HashSet<string> BuildPluginsFloatPackageUidsMatchingTerms(IList<FileEntry> source, string[] terms)
        {
            var hit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source == null || terms == null || terms.Length == 0) return hit;

            for (int i = 0; i < source.Count; i++)
            {
                FileEntry e = source[i];
                if (e == null) continue;
                string uid = PluginsFloatPackageUid(e);
                if (string.IsNullOrEmpty(uid) || hit.Contains(uid)) continue;

                string creator = null;
                string pkgName = null;
                int ver;
                TryParseVarUidParts(uid, out creator, out pkgName, out ver);

                _pluginsFloatHaystackSb.Length = 0;
                if (!string.IsNullOrEmpty(creator))
                {
                    _pluginsFloatHaystackSb.Append(creator);
                    _pluginsFloatHaystackSb.Append(' ');
                }
                if (!string.IsNullOrEmpty(pkgName))
                {
                    _pluginsFloatHaystackSb.Append(pkgName);
                    _pluginsFloatHaystackSb.Append(' ');
                }
                _pluginsFloatHaystackSb.Append(uid);
                _pluginsFloatHaystackSb.Append(' ');
                string n = null;
                try { n = e.Name; } catch { n = null; }
                if (!string.IsNullOrEmpty(n))
                {
                    _pluginsFloatHaystackSb.Append(n);
                    _pluginsFloatHaystackSb.Append(' ');
                }
                string p = null;
                try { p = e.Path; } catch { p = null; }
                if (!string.IsNullOrEmpty(p))
                    _pluginsFloatHaystackSb.Append(p);

                if (MatchesAllTermsInEither(_pluginsFloatHaystackSb.ToString(), "", terms))
                    hit.Add(uid);
            }
            return hit;
        }

        private float PluginsFloatVirtRowStride()
        {
            float s = _pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : ChromeScale;
            return (GalleryUiDesignTokens.PluginsFloatRowHeightRef + 2f) * s;
        }

        private void ApplyPluginsFloatContentHeight()
        {
            if (_pluginsFloatContentRT == null) return;
            float rowH = PluginsFloatVirtRowStride();
            float pad = 8f * (_pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : ChromeScale);
            float h = _pluginsFloatVirtView.Count * rowH + pad;
            if (h < 1f) h = 1f;
            _pluginsFloatContentRT.sizeDelta = new Vector2(0f, h);
        }

        private void EnsurePluginsFloatScrollHook()
        {
            if (_pluginsFloatScrollHooked || _pluginsFloatScrollRect == null) return;
            _pluginsFloatScrollHooked = true;
            _pluginsFloatScrollRect.onValueChanged.AddListener(_ =>
            {
                try { UpdatePluginsFloatVirtualVisible(force: false); } catch { }
            });
        }

        private void EnsurePluginsFloatVirtPool(int desired)
        {
            Transform parent = _pluginsFloatRowsParent;
            if (parent == null) return;
            if (desired < 8) desired = 8;
            if (desired > 64) desired = 64; // hard cap pool — viewport+buffer only

            for (int i = 0; i < _pluginsFloatVirtPool.Count; i++)
            {
                GameObject pooled = _pluginsFloatVirtPool[i];
                if (pooled == null || pooled.transform.parent != parent
                    || pooled.transform.Find("VerGap") == null)
                {
                    for (int d = 0; d < _pluginsFloatVirtPool.Count; d++)
                    {
                        if (_pluginsFloatVirtPool[d] != null)
                        {
                            try { UnityEngine.Object.Destroy(_pluginsFloatVirtPool[d]); } catch { }
                        }
                    }
                    _pluginsFloatVirtPool.Clear();
                    break;
                }
            }

            float s = _pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : ChromeScale;
            while (_pluginsFloatVirtPool.Count < desired)
            {
                GameObject row = CreatePluginsFloatVirtRowShell(s);
                if (row == null) break;
                _pluginsFloatVirtPool.Add(row);
            }
        }

        private void UpdatePluginsFloatVirtualVisible(bool force)
        {
            if (_pluginsFloatScrollRect == null || _pluginsFloatRowsParent == null) return;

            int total = _pluginsFloatVirtView.Count;
            if (total <= 0)
            {
                for (int i = 0; i < _pluginsFloatVirtPool.Count; i++)
                {
                    if (_pluginsFloatVirtPool[i] != null)
                        _pluginsFloatVirtPool[i].SetActive(false);
                }
                return;
            }

            float rowH = PluginsFloatVirtRowStride();
            if (rowH < 1f) rowH = 38f;

            RectTransform viewport = _pluginsFloatScrollRect.viewport != null
                ? _pluginsFloatScrollRect.viewport
                : (_pluginsFloatScrollRect.transform as RectTransform);
            float viewportH = viewport != null ? viewport.rect.height : 400f;
            if (viewportH < 1f) viewportH = 400f;

            float contentH = total * rowH;
            float scrollRange = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - Mathf.Clamp01(_pluginsFloatScrollRect.verticalNormalizedPosition)) * scrollRange;
            int firstIdx = (rowH > 0f) ? Mathf.FloorToInt(scrollY / rowH) : 0;
            if (firstIdx < 0) firstIdx = 0;
            if (firstIdx > total - 1) firstIdx = Mathf.Max(0, total - 1);

            if (!force && firstIdx == _pluginsFloatVirtLastFirstIdx)
                return;
            _pluginsFloatVirtLastFirstIdx = firstIdx;

            int visible = Mathf.CeilToInt(viewportH / rowH) + 8;
            EnsurePluginsFloatVirtPool(visible);

            float s = _pluginsFloatChromeScale > 0f ? _pluginsFloatChromeScale : ChromeScale;
            float padX = 4f * s;

            for (int i = 0; i < _pluginsFloatVirtPool.Count; i++)
            {
                int idx = firstIdx + i;
                GameObject go = _pluginsFloatVirtPool[i];
                if (go == null) continue;

                if (idx >= 0 && idx < total)
                {
                    go.SetActive(true);
                    BindPluginsFloatVirtRow(go, _pluginsFloatVirtView[idx], s);

                    RectTransform rt = go.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(0f, 1f);
                        rt.anchorMax = new Vector2(1f, 1f);
                        rt.pivot = new Vector2(0.5f, 1f);
                        float rowPx = GalleryUiDesignTokens.PluginsFloatRowHeightRef * s;
                        rt.sizeDelta = new Vector2(-padX * 2f, rowPx);
                        rt.anchoredPosition = new Vector2(0f, -idx * rowH - padX);
                    }
                }
                else
                {
                    go.SetActive(false);
                }
            }
        }

        private GameObject CreatePluginsFloatVirtRowShell(float s)
        {
            if (_pluginsFloatRowsParent == null) return null;

            float rowH = GalleryUiDesignTokens.PluginsFloatRowHeightRef * s;
            float expandW = GalleryUiDesignTokens.PluginsFloatExpandWidthRef * s;
            float indentW = GalleryUiDesignTokens.PluginsFloatChildIndentRef * s;
            float verW = GalleryUiDesignTokens.PluginsFloatVersionWidthRef * s;
            float starSz = GalleryUiDesignTokens.ButtonSizeRef * s * 0.9f;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;

            GameObject row = new GameObject("PluginVirtRow");
            row.transform.SetParent(_pluginsFloatRowsParent, false);
            RectTransform rowRT = row.AddComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);

            Image rowBg = UI.AddImage(row, PluginsFloatRowBg);
            if (rowBg != null) rowBg.raycastTarget = true;
            var rowHover = row.AddComponent<PluginsFloatRowHover>();
            rowHover.target = rowBg;
            rowHover.SetColors(PluginsFloatRowBg, Color.Lerp(PluginsFloatRowBg, Color.white, 0.14f));

            HorizontalLayoutGroup hlg = UI.AddHLG(row, spacing: 4f * s, padding: UI.Pad(2, 2, 2, 2, s),
                childForceExpandWidth: false);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;

            GameObject indent = new GameObject("Indent");
            indent.transform.SetParent(row.transform, false);
            indent.AddComponent<RectTransform>();
            UI.AddLE(indent, minWidth: indentW, preferredWidth: indentW, flexibleWidth: 0f,
                minHeight: rowH, preferredHeight: rowH);

            GameObject expandBtn = UI.CreateUIButton(
                row, expandW, rowH, "", font, 0, 0, AnchorPresets.middleLeft, null);
            expandBtn.name = "Expand";
            UI.AddLE(expandBtn, minWidth: expandW, preferredWidth: expandW, flexibleWidth: 0f,
                minHeight: rowH, preferredHeight: rowH, flexibleHeight: 0f);

            GameObject dragHost = new GameObject("DragHost");
            dragHost.transform.SetParent(row.transform, false);
            dragHost.AddComponent<RectTransform>();
            UI.AddLE(dragHost, flexibleWidth: 1f, minWidth: 40f * s, minHeight: rowH, preferredHeight: rowH);
            Image dragBg = UI.AddImage(dragHost, new Color(0f, 0f, 0f, 0f));
            if (dragBg != null) dragBg.raycastTarget = true;
            Text nameText = UI.CreateLabel(dragHost, "", font, Color.white,
                TextAnchor.MiddleLeft, raycastTarget: false, name: "Name");
            GalleryUiMetrics.ApplyFont(nameText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            RectTransform nameRT = nameText.GetComponent<RectTransform>();
            if (nameRT != null)
            {
                nameRT.anchorMin = Vector2.zero;
                nameRT.anchorMax = Vector2.one;
                nameRT.offsetMin = new Vector2(4f * s, 0f);
                nameRT.offsetMax = new Vector2(-4f * s, 0f);
            }
            UIDraggableItem draggable = dragHost.AddComponent<UIDraggableItem>();
            draggable.Panel = this;

            // Fixed version column — body size, gap before ★ (not crowded against star).
            Text verText = UI.CreateLabel(row, "", font, GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleRight, raycastTarget: false, name: "Ver");
            GalleryUiMetrics.ApplyFont(verText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(verText.gameObject, minWidth: verW, preferredWidth: verW, flexibleWidth: 0f,
                minHeight: rowH, preferredHeight: rowH);

            float verGap = GalleryUiDesignTokens.PluginsFloatVersionStarGapRef * s;
            GameObject verGapGo = new GameObject("VerGap");
            verGapGo.transform.SetParent(row.transform, false);
            verGapGo.AddComponent<RectTransform>();
            UI.AddLE(verGapGo, minWidth: verGap, preferredWidth: verGap, flexibleWidth: 0f,
                minHeight: 1f, preferredHeight: 1f);

            // Compact star badge (creator-row style: ★ when unrated, digit when rated).
            GameObject starBtnGO = UI.CreateUIButton(row, starSz, starSz, "", font, 0, 0,
                AnchorPresets.middleCenter, null);
            starBtnGO.name = "Star";
            UI.AddLE(starBtnGO, minWidth: starSz, preferredWidth: starSz, flexibleWidth: 0f,
                minHeight: starSz, preferredHeight: starSz, flexibleHeight: 0f);
            Image starBg = starBtnGO.GetComponent<Image>();
            if (starBg != null) starBg.color = UI.IconButtonBackdrop;
            Button starBtn = starBtnGO.GetComponent<Button>();
            if (starBtn != null)
                starBtn.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject starIconGO = new GameObject("StarIcon");
            starIconGO.transform.SetParent(starBtnGO.transform, false);
            Image starIconImg = starIconGO.AddComponent<Image>();
            starIconImg.raycastTarget = false;
            starIconImg.preserveAspect = true;
            try
            {
                Sprite spr = UI.LoadIconSprite("vpb_icons/star.png", Color.white);
                if (spr != null) starIconImg.sprite = spr;
            }
            catch { }
            RectTransform starIconRT = starIconGO.GetComponent<RectTransform>();
            if (starIconRT != null)
            {
                starIconRT.anchorMin = new Vector2(0.18f, 0.18f);
                starIconRT.anchorMax = new Vector2(0.82f, 0.82f);
                starIconRT.offsetMin = Vector2.zero;
                starIconRT.offsetMax = Vector2.zero;
            }

            GameObject digitGO = new GameObject("Digit");
            digitGO.transform.SetParent(starBtnGO.transform, false);
            Text digitText = digitGO.AddComponent<Text>();
            digitText.alignment = TextAnchor.MiddleCenter;
            digitText.raycastTarget = false;
            digitText.fontStyle = FontStyle.Bold;
            digitText.text = "";
            digitGO.SetActive(false);
            VPBUiFont.ApplyTo(digitText);
            GalleryUiMetrics.ApplyFont(digitText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            RectTransform digitRT = digitGO.GetComponent<RectTransform>();
            if (digitRT != null)
            {
                digitRT.anchorMin = Vector2.zero;
                digitRT.anchorMax = Vector2.one;
                digitRT.offsetMin = Vector2.zero;
                digitRT.offsetMax = Vector2.zero;
            }

            // Home under star; open reparents to float panel (escape ScrollRect mask) with world pose.
            GameObject selectorGO = new GameObject("RatingSelector");
            selectorGO.transform.SetParent(starBtnGO.transform, false);
            RectTransform selectorRT = selectorGO.AddComponent<RectTransform>();
            selectorRT.anchorMin = new Vector2(1f, 0f);
            selectorRT.anchorMax = new Vector2(1f, 0f);
            selectorRT.pivot = new Vector2(1f, 1f);
            selectorRT.sizeDelta = new Vector2(80f * s, 114f * s);
            selectorRT.anchoredPosition = new Vector2(0f, -2f * s);
            CanvasGroup selectorCG = selectorGO.AddComponent<CanvasGroup>();
            selectorCG.alpha = 0f;
            selectorCG.interactable = false;
            selectorCG.blocksRaycasts = false;
            UI.AddImage(selectorGO, new Color(0.05f, 0.05f, 0.05f, 0.95f));
            GridLayoutGroup selectorGrid = selectorGO.AddComponent<GridLayoutGroup>();
            selectorGrid.cellSize = new Vector2(38f * s, 36f * s);
            selectorGrid.spacing = new Vector2(2f * s, 2f * s);
            selectorGrid.padding = new RectOffset(1, 1, 1, 1);
            selectorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            selectorGrid.constraintCount = 2;
            selectorGO.SetActive(false);
            var selRef = row.AddComponent<PluginsFloatRowSelectorRef>();
            selRef.selector = selectorGO;

            RatingHandler ratingHandler = row.AddComponent<RatingHandler>();
            ratingHandler.panel = this;
            ratingHandler.selectorEscapeHost = _pluginsFloatPanelRT;
            Image[] optImages = new Image[6];
            Text[] optTexts = new Text[6];
            GameObject[] optBorders = new GameObject[6];
            for (int i = 0; i <= 5; i++)
            {
                int ratingValue = i;
                string optLabel = i == 0 ? "X" : i.ToString();
                GameObject optBtnGO = UI.CreateUIButton(selectorGO, 38f * s, 36f * s, optLabel, font, 0, 0,
                    AnchorPresets.middleCenter, () => ratingHandler.SetRating(ratingValue));
                Button optBtn = optBtnGO.GetComponent<Button>();
                if (optBtn != null)
                    optBtn.navigation = new Navigation { mode = Navigation.Mode.None };
                optImages[i] = optBtnGO.GetComponent<Image>();
                if (optImages[i] != null)
                    optImages[i].color = RatingHandler.RatingColors[i];
                optTexts[i] = optBtnGO.GetComponentInChildren<Text>();
                if (optTexts[i] != null)
                    optTexts[i].color = i == 0 ? Color.red : Color.black;
                optBorders[i] = null;
            }
            ratingHandler.SetOptionRefs(optImages, optTexts, optBorders);
            ratingHandler.BindStarIcon(starIconImg);
            if (starBtn != null)
            {
                starBtn.onClick.RemoveAllListeners();
                starBtn.onClick.AddListener(() => ratingHandler.ToggleSelector());
            }

            row.SetActive(false);
            return row;
        }

        private void BindPluginsFloatVirtRow(GameObject row, PluginsFloatFlatRow data, float s)
        {
            if (row == null || data == null) return;

            bool isCreator = data.IsCreator;
            bool isChild = data.IsChild;
            bool canExpand = data.CanExpand;
            bool expanded = data.Expanded;
            Color baseBg = isCreator
                ? PluginsFloatCreatorRowBg
                : (isChild ? PluginsFloatChildRowBg : PluginsFloatRowBg);
            Color hoverBg = Color.Lerp(baseBg, Color.white, isCreator ? 0.10f : 0.14f);
            Image rowBg = row.GetComponent<Image>();
            if (rowBg != null) rowBg.color = baseBg;
            PluginsFloatRowHover hover = row.GetComponent<PluginsFloatRowHover>();
            if (hover != null)
                hover.SetColors(baseBg, hoverBg);

            float indentUnit = GalleryUiDesignTokens.PluginsFloatChildIndentRef * s;
            Transform indentTr = row.transform.Find("Indent");
            if (indentTr != null)
            {
                LayoutElement ile = indentTr.GetComponent<LayoutElement>();
                float w = data.IndentLevel > 0 ? indentUnit * data.IndentLevel : 0f;
                if (ile != null)
                {
                    ile.minWidth = w;
                    ile.preferredWidth = w;
                }
                indentTr.gameObject.SetActive(w > 0.5f);
            }

            Transform expandTr = row.transform.Find("Expand");
            if (expandTr != null)
            {
                Button expandBtn = expandTr.GetComponent<Button>();
                Image expandBg = expandTr.GetComponent<Image>();
                if (expandBg != null)
                {
                    expandBg.color = canExpand
                        ? new Color(0.16f, 0.17f, 0.20f, 1f)
                        : new Color(0.10f, 0.10f, 0.12f, 0f);
                }
                Text expandGlyph = expandTr.GetComponentInChildren<Text>(true);
                if (expandGlyph != null)
                {
                    expandGlyph.text = !canExpand ? "" : (expanded ? "▼" : "▶");
                    expandGlyph.color = canExpand
                        ? new Color(0.82f, 0.84f, 0.88f, 1f)
                        : new Color(0.35f, 0.35f, 0.38f, 0f);
                }
                if (expandBtn != null)
                {
                    expandBtn.onClick.RemoveAllListeners();
                    if (canExpand && !string.IsNullOrEmpty(data.ExpandKey))
                    {
                        string key = data.ExpandKey;
                        expandBtn.onClick.AddListener(() => TogglePluginsFloatExpand(key));
                    }
                }
                Transform iconTr = expandTr.Find("Icon");
                if (iconTr != null) iconTr.gameObject.SetActive(false);
                if (expandGlyph != null) expandGlyph.gameObject.SetActive(true);
            }

            Transform dragTr = row.transform.Find("DragHost");
            if (dragTr != null)
            {
                UIDraggableItem drag = dragTr.GetComponent<UIDraggableItem>();
                if (drag != null)
                {
                    drag.Panel = this;
                    drag.FileEntry = isCreator ? null : data.Entry;
                }
                Image dragBg = dragTr.GetComponent<Image>();
                if (dragBg != null)
                    dragBg.raycastTarget = !isCreator;
                Transform nameTr = dragTr.Find("Name");
                Text nameText = nameTr != null ? nameTr.GetComponent<Text>() : null;
                if (nameText != null)
                {
                    string label = data.DisplayName ?? "";
                    if (isCreator)
                    {
                        if (data.ChildCountDisplay > 0)
                            label = string.Format("{0}  ({1})", label, data.ChildCountDisplay);
                    }
                    else if (!isChild && canExpand)
                    {
                        if (data.ChildCountDisplay > 0)
                            label = string.Format("{0}  ({1})", label, data.ChildCountDisplay);
                        else if (data.ChildCountDisplay < 0)
                            label = label + "  (…)";
                    }
                    nameText.text = label;
                    if (isCreator)
                        nameText.color = Color.white;
                    else if (isChild)
                        nameText.color = new Color(0.78f, 0.80f, 0.84f, 1f);
                    else
                        nameText.color = Color.white;
                    nameText.fontStyle = isCreator ? FontStyle.Bold : FontStyle.Normal;
                }
            }

            Transform verTr = row.transform.Find("Ver");
            if (verTr != null)
            {
                Text verText = verTr.GetComponent<Text>();
                if (verText != null)
                {
                    if (isCreator || isChild || data.Version < 0)
                        verText.text = "";
                    else
                        verText.text = "v" + data.Version.ToString();
                    verText.color = isCreator || isChild || data.Version < 0
                        ? GalleryUiColorTokens.TextDim
                        : new Color(0.88f, 0.90f, 0.94f, 1f);
                    GalleryUiMetrics.ApplyFont(verText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                }
                LayoutElement verLe = verTr.GetComponent<LayoutElement>();
                if (verLe != null)
                {
                    float verW = GalleryUiDesignTokens.PluginsFloatVersionWidthRef * s;
                    // Hide width on creator/child so name gets space; keep column on packages.
                    float w = (isCreator || isChild || data.Version < 0) ? 0f : verW;
                    verLe.minWidth = w;
                    verLe.preferredWidth = w;
                    verTr.gameObject.SetActive(w > 0.5f);
                }
            }
            Transform verGapTr = row.transform.Find("VerGap");
            if (verGapTr != null)
            {
                float gap = GalleryUiDesignTokens.PluginsFloatVersionStarGapRef * s;
                bool showGap = !isCreator && !isChild && data.Version >= 0;
                LayoutElement gapLe = verGapTr.GetComponent<LayoutElement>();
                if (gapLe != null)
                {
                    float w = showGap ? gap : 0f;
                    gapLe.minWidth = w;
                    gapLe.preferredWidth = w;
                }
                verGapTr.gameObject.SetActive(showGap);
            }

            Transform starTr = row.transform.Find("Star");
            if (starTr != null)
                starTr.gameObject.SetActive(!isCreator);

            RatingHandler rh = row.GetComponent<RatingHandler>();
            if (rh != null && !isCreator && data.Entry != null)
            {
                Text digit = null;
                Image starImg = null;
                if (starTr != null)
                {
                    Transform digitTr = starTr.Find("Digit");
                    if (digitTr != null) digit = digitTr.GetComponent<Text>();
                    Transform iconTr = starTr.Find("StarIcon");
                    if (iconTr != null) starImg = iconTr.GetComponent<Image>();
                }
                GameObject selectorGO = null;
                PluginsFloatRowSelectorRef selRef = row.GetComponent<PluginsFloatRowSelectorRef>();
                if (selRef != null) selectorGO = selRef.selector;
                rh.selectorEscapeHost = _pluginsFloatPanelRT;
                rh.BindStarIcon(starImg);
                rh.Init(data.Entry, digit, selectorGO);
            }
        }

        private void UpdatePluginsFloatEmptyChrome(IList<FileEntry> source, int visibleRows, int visibleRoots, bool populating)
        {
            string filter = _pluginsFloatFilter ?? "";
            bool hasFilter = filter.Length > 0;
            bool empty = visibleRows == 0;
            if (_pluginsFloatEmptyGo != null)
            {
                _pluginsFloatEmptyGo.SetActive(empty);
                if (_pluginsFloatEmptyText != null)
                {
                    if (populating || (!_pluginsFloatCatalogReady && (source == null || source.Count == 0)))
                        _pluginsFloatEmptyText.text = VPBTranslation.T("gallery.plugins.empty_loading", "Loading plugins…");
                    else if (hasFilter)
                        _pluginsFloatEmptyText.text = VPBTranslation.T("gallery.plugins.empty_filter", "No matches.");
                    else if (_pluginsFloatRatedOnly && _pluginsFloatCslistOnly)
                        _pluginsFloatEmptyText.text = VPBTranslation.T(
                            "gallery.plugins.empty_rated_cslist", "No rated .cslist plugins.");
                    else if (_pluginsFloatRatedOnly)
                        _pluginsFloatEmptyText.text = VPBTranslation.T("gallery.plugins.empty_rated", "No rated plugins.");
                    else if (_pluginsFloatCslistOnly)
                        _pluginsFloatEmptyText.text = VPBTranslation.T(
                            "gallery.plugins.empty_cslist", "No .cslist plugins.");
                    else
                        _pluginsFloatEmptyText.text = VPBTranslation.T("gallery.plugins.empty", "No plugins found.");
                }
            }

            if (_pluginsFloatStatusText != null)
            {
                if (empty)
                {
                    _pluginsFloatStatusText.text = VPBTranslation.T(
                        "gallery.plugins.float_hint", "Drag → header Person / Session · void = session");
                }
                else
                {
                    _pluginsFloatStatusText.text = string.Format(
                        VPBTranslation.T(
                            "gallery.plugins.float_count",
                            "{0} plugins · drop Person or Session"),
                        visibleRoots);
                }
            }
        }

        private int ComparePluginsFloatCreatorGroups(PluginsFloatCreatorGroup a, PluginsFloatCreatorGroup b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            if (_pluginsFloatRatedOnly)
            {
                int rd = b.MaxRating.CompareTo(a.MaxRating);
                if (rd != 0) return rd;
            }

            // Loose (empty creator) last.
            bool aLoose = string.IsNullOrEmpty(a.Creator);
            bool bLoose = string.IsNullOrEmpty(b.Creator);
            if (aLoose != bLoose) return aLoose ? 1 : -1;

            string an = a.Creator ?? "";
            string bn = b.Creator ?? "";
            return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
        }

        private int ComparePluginsFloatRoots(PluginsFloatRoot a, PluginsFloatRoot b)
        {
            if (a == b) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            if (_pluginsFloatRatedOnly)
            {
                int rd = b.MaxRating.CompareTo(a.MaxRating);
                if (rd != 0) return rd;
            }

            string ap = a.PackageName ?? "";
            string bp = b.PackageName ?? "";
            int pd = string.Compare(ap, bp, StringComparison.OrdinalIgnoreCase);
            if (pd != 0) return pd;

            // Newer version first when latest-only off (scan older siblings easily).
            int vd = b.Version.CompareTo(a.Version);
            if (vd != 0) return vd;

            string an = a.Entry != null ? (a.Entry.Name ?? "") : "";
            string bn = b.Entry != null ? (b.Entry.Name ?? "") : "";
            return string.Compare(an, bn, StringComparison.OrdinalIgnoreCase);
        }

        private static string PluginsFloatExpandKey(FileEntry entry)
        {
            if (entry == null) return "";
            try
            {
                string u = entry.Uid;
                if (!string.IsNullOrEmpty(u)) return u.ToLowerInvariant();
            }
            catch { }
            try
            {
                string p = entry.Path;
                if (!string.IsNullOrEmpty(p)) return p.Replace('\\', '/').ToLowerInvariant();
            }
            catch { }
            return "";
        }

        private void TogglePluginsFloatExpand(string expandKey)
        {
            if (string.IsNullOrEmpty(expandKey)) return;
            if (_pluginsFloatExpanded.Contains(expandKey))
            {
                _pluginsFloatExpanded.Remove(expandKey);
            }
            else
            {
                _pluginsFloatExpanded.Add(expandKey);
                if (!expandKey.StartsWith("c:", StringComparison.OrdinalIgnoreCase)
                    && _pluginsFloatRoots != null)
                {
                    for (int i = 0; i < _pluginsFloatRoots.Count; i++)
                    {
                        PluginsFloatRoot root = _pluginsFloatRoots[i];
                        if (root == null || root.Entry == null) continue;
                        if (!string.Equals(PluginsFloatExpandKey(root.Entry), expandKey, StringComparison.OrdinalIgnoreCase))
                            continue;
                        EnsurePluginsFloatRootChildren(root, _pluginsFloatCatalog);
                        break;
                    }
                }
            }
            RefreshPluginsFloatTree(false);
        }

        /// <summary>
        /// Expand all creator rows (shows packages). Skips mass .cslist OpenStream (QoS).
        /// </summary>
        private void ExpandAllPluginsFloatCreators()
        {
            if (_pluginsFloatCreators == null) return;
            int n = 0;
            for (int i = 0; i < _pluginsFloatCreators.Count; i++)
            {
                PluginsFloatCreatorGroup g = _pluginsFloatCreators[i];
                if (g == null || string.IsNullOrEmpty(g.ExpandKey)) continue;
                if (_pluginsFloatRatedOnly && g.MaxRating <= 0) continue;
                if (_pluginsFloatExpanded.Add(g.ExpandKey))
                    n++;
            }
            RefreshPluginsFloatTree(false);
            try
            {
                ShowTemporaryStatus(
                    n > 0
                        ? VPBTranslation.T("gallery.plugins.expand_all_done", "Expanded all creators")
                        : VPBTranslation.T("gallery.plugins.expand_all_noop", "Creators already expanded"),
                    1.2f);
            }
            catch { }
        }

        private void CollapseAllPluginsFloatTree()
        {
            int n = _pluginsFloatExpanded.Count;
            _pluginsFloatExpanded.Clear();
            RefreshPluginsFloatTree(false);
            try
            {
                ShowTemporaryStatus(
                    n > 0
                        ? VPBTranslation.T("gallery.plugins.collapse_all_done", "Collapsed all rows")
                        : VPBTranslation.T("gallery.plugins.collapse_all_noop", "Tree already collapsed"),
                    1.2f);
            }
            catch { }
        }
    }
}
