using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        private sealed class UserTagEditorRowVisual
        {
            public string Name;
            public Image Bg;
        }

        private readonly List<UserTagSideTabEntry> _userTagEditorVisibleRows = new List<UserTagSideTabEntry>(1024);
        private readonly List<UserTagEditorRowVisual> _userTagEditorRowVisuals = new List<UserTagEditorRowVisual>(1024);
        /// <summary>Scratch for filter/sort build — reused to avoid per-keystroke List alloc (warm UI).</summary>
        private readonly List<UserTagSideTabEntry> _userTagEditorBuildScratch = new List<UserTagSideTabEntry>(1024);
        /// <summary>Hard cap on Database-mode visible rows (same family as Apply menu). Filter narrows; overflow hint shown.</summary>
        private const int UserTagEditorMaxVisibleRows = 64;
        private static readonly Comparison<UserTagSideTabEntry> UserTagEditorSortNameAsc =
            (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        private static readonly Comparison<UserTagSideTabEntry> UserTagEditorSortNameDesc =
            (a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase);
        private static readonly Comparison<UserTagSideTabEntry> UserTagEditorSortCountDesc =
            (a, b) => b.Count.CompareTo(a.Count);
        private static readonly Comparison<UserTagSideTabEntry> UserTagEditorSortCountAsc =
            (a, b) => a.Count.CompareTo(b.Count);
        private Color _userTagEditorRowBaseCol = new Color(0.2f, 0.2f, 0.22f, 1f);
        private Color _userTagEditorRowSelCol = new Color(0.28f, 0.38f, 0.32f, 1f);
        private static readonly Color UserTagEditorNewTagChromeBaseCol = new Color(0.07f, 0.07f, 0.09f, 1f);
        private static readonly Color UserTagEditorNewTagChromeOkPulseCol = new Color(0.11f, 0.38f, 0.20f, 1f);
        private static readonly Color UserTagEditorNewTagChromeBadPulseCol = new Color(0.48f, 0.10f, 0.12f, 1f);
        private const float UserTagEditorNewTagFlashHoldSec = 0.2f;
        private const float UserTagEditorNewTagFlashFadeSec = 0.28f;

        private void RefreshFilesThenUpdateTabs(bool keepScroll)
        {
            RefreshFiles(keepScroll, false, false, null);
            try { UpdateTabs(); } catch { }
        }

        /// <summary>SQLite row identity for <c>gallery_item_user_tag</c>: vars use pkg_uid + internal path; loose Custom/Saves files use <see cref="VpbLocalDatabase.GalleryUserTagLoosePkgUid"/> + normalized path.</summary>
        private bool TryGetGalleryRowKeysForUserTags(FileEntry fe, out string pkgUid, out string internalPath)
        {
            pkgUid = "";
            internalPath = "";
            if (fe == null) return false;

            VarFileEntry vfe = fe as VarFileEntry;
            if (vfe != null)
            {
                // Gallery fast-path rows (ALL VAR, etc.) often defer package resolve; badge check must not depend on Package != null.
                pkgUid = vfe.GetRowPackageUid() ?? "";
                internalPath = vfe.InternalPath ?? "";
                return !string.IsNullOrEmpty(pkgUid) && !string.IsNullOrEmpty(internalPath);
            }

            SystemFileEntry sfe = fe as SystemFileEntry;
            if (sfe != null)
            {
                if (sfe.isVar && sfe.package != null)
                {
                    pkgUid = sfe.package.Uid ?? "";
                    internalPath = "meta.json";
                    return !string.IsNullOrEmpty(pkgUid);
                }
                if (!sfe.isVar)
                {
                    string norm = VpbLocalDatabase.NormalizeLoosePathForGalleryUserTag(sfe.Path);
                    if (string.IsNullOrEmpty(norm)) return false;
                    pkgUid = VpbLocalDatabase.GalleryUserTagLoosePkgUid;
                    internalPath = norm;
                    return true;
                }
            }

            PackageListEntry ple = fe as PackageListEntry;
            if (ple != null)
            {
                string puid = ple.GetPackageUidForGalleryUserTags();
                if (string.IsNullOrEmpty(puid)) return false;
                pkgUid = puid;
                internalPath = "meta.json";
                return true;
            }

            return false;
        }

        /// <summary>Show gallery grid «T» badge when SQLite user tags exist for this row in current category.</summary>
        private bool IsGalleryUserTagBadgeVisible(FileEntry file)
        {
            if (file == null) return false;
            if (!TryGetGalleryRowKeysForUserTags(file, out string pkgUid, out string internalPath)) return false;
            string cat = currentCategoryTitle ?? "";
            if (string.IsNullOrEmpty(cat) && titleText != null) cat = titleText.text ?? "";
            if (string.IsNullOrEmpty(cat)) return false;
            // ALL VAR: package row is meta.json, but inherit mode can tag only child items.
            // Show badge when either package meta row tagged OR any child inside package tagged.
            if (VpbLocalDatabase.IsGalleryAllVarPseudoCategory(cat)
                && string.Equals(internalPath, "meta.json", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(pkgUid))
            {
                if (VpbLocalDatabase.TryHasAnyGalleryUserTagsForRow(cat, pkgUid, internalPath)) return true;
                return VpbLocalDatabase.TryHasAnyGalleryUserTagsForPackageAnyPath(pkgUid);
            }
            return VpbLocalDatabase.TryHasAnyGalleryUserTagsForRow(cat, pkgUid, internalPath);
        }

        /// <summary>Selection changed. Recount tag state for unified panel.</summary>
        private void RefreshAppliedUserTagsPaneAfterSelectionChange()
        {
            userTagAppliedRemoveSelection.Clear();
            userTagAppliedRemoveAnchor = null;
            updatePanelForSelection();
            // Detail strip tags line must refresh when selection (or applied tags) change.
            // Skip during thumb scrub — commit path rebuilds strip once on idle.
            if (_detailStripScrubActive) return;
            try { _detailStripCacheKey = ""; DetailStripRefresh(); } catch { }
        }

        private void ClearUntaggedTaggedPinKeys()
        {
            _untaggedTaggedPinKeys.Clear();
        }

        /// <summary>Not Tagged: drop pinned tagged rows the user just deselected (O(deselected) scan, no SQLite).</summary>
        private void PruneUntaggedGridAfterSelectionChange(HashSet<string> deselectedSelKeys)
        {
            if (deselectedSelKeys == null || deselectedSelKeys.Count == 0) return;
            if (_userTagAvailMode != UserTagAvailMode.FilterUntagged) return;
            if (!TryPruneUntaggedGridForDeselectedPins(deselectedSelKeys)) return;

            if (recyclingGrid != null)
            {
                recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                recyclingGrid.Refresh();
            }
            try { UpdatePaginationText(); } catch { }
        }

        private static HashSet<string> SnapshotSelectionIdentityKeys(GalleryPanel panel)
        {
            if (panel == null || panel.selectedFilePaths == null || panel.selectedFilePaths.Count == 0)
                return null;
            return new HashSet<string>(panel.selectedFilePaths, StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> BuildDeselectedSelectionKeys(HashSet<string> before, HashSet<string> after)
        {
            if (before == null || before.Count == 0) return null;
            var deselected = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
            if (after != null)
            {
                foreach (string k in after)
                    deselected.Remove(k);
            }
            return deselected.Count > 0 ? deselected : null;
        }

        private void updatePanelForSelection()
        {
            CacheAppliedUserTagsForSelection();
            try { EnsureUserTagAvailViewReflectsSelection(); } catch { }
            try
            {
                if (leftActiveContent == ContentType.UserTags && leftSubTabContainerGO != null
                    && leftSubTabScrollGO != null && leftSubTabScrollGO.activeSelf)
                    UpdateTabs(ContentType.UserTagsApplied, leftSubTabContainerGO, leftSubActiveTabButtons, true);
            }
            catch { }
            try
            {
                if (rightActiveContent == ContentType.UserTags && rightSubTabContainerGO != null
                    && rightSubTabScrollGO != null && rightSubTabScrollGO.activeSelf)
                    UpdateTabs(ContentType.UserTagsApplied, rightSubTabContainerGO, rightSubActiveTabButtons, false);
            }
            catch { }
        }

        private string BuildUserTagSelectionVirtSignature()
        {
            if (_userTagSelectionRowCount <= 0 || _userTagSelectionStates.Count == 0) return "";
            var names = new List<string>(_userTagSelectionStates.Count);
            foreach (var kv in _userTagSelectionStates)
            {
                if (!string.IsNullOrEmpty(kv.Key)) names.Add(kv.Key);
            }
            names.Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            var sb = new StringBuilder(Math.Max(32, names.Count * 8));
            sb.Append(_userTagSelectionRowCount);
            for (int i = 0; i < names.Count; i++)
            {
                sb.Append('\u001f');
                sb.Append(names[i]);
                sb.Append(':');
                UserTagSelectionState st;
                sb.Append(_userTagSelectionStates.TryGetValue(names[i], out st) ? (int)st : 0);
            }
            return sb.ToString();
        }

        private void EnsureSelectionUserTagsInAvailList(List<UserTagSideTabEntry> rows)
        {
            if (rows == null) return;
            if (_userTagAvailMode != UserTagAvailMode.FilterByTags || _userTagSelectionRowCount <= 0) return;

            foreach (var kv in _userTagSelectionStates)
            {
                string name = kv.Key;
                if (string.IsNullOrEmpty(name)) continue;
                bool found = false;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (string.Equals(rows[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (found) continue;

                int selCount = 0;
                for (int i = 0; i < cachedAppliedUserTagsSelection.Count; i++)
                {
                    if (string.Equals(cachedAppliedUserTagsSelection[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        selCount = cachedAppliedUserTagsSelection[i].Count;
                        break;
                    }
                }
                rows.Add(new UserTagSideTabEntry { Name = name, Count = selCount });
            }
        }

        private void EnsureUserTagAvailViewReflectsSelection()
        {
            bool leftOpen = leftActiveContent == ContentType.UserTags && leftTabContainerGO != null;
            bool rightOpen = rightActiveContent == ContentType.UserTags && rightTabContainerGO != null;
            if (!leftOpen && !rightOpen) return;

            if (_userTagAvailMode == UserTagAvailMode.FilterByTags)
            {
                string sigNew = ComputeUserTagVirtDataSignature();
                if (!string.Equals(_userTagVirtViewSig, sigNew, StringComparison.Ordinal))
                {
                    _userTagVirtViewSig = sigNew;
                    if (leftOpen)
                    {
                        SnapshotUserTagAvailScrollForPreserve(true);
                        RebuildUserTagVirtViewList(true, resetScrollToTop: false);
                        RestorePreservedUserTagAvailScroll();
                    }
                    if (rightOpen)
                    {
                        SnapshotUserTagAvailScrollForPreserve(false);
                        RebuildUserTagVirtViewList(false, resetScrollToTop: false);
                        RestorePreservedUserTagAvailScroll();
                    }
                }
            }
            RefreshVisibleUserTagRows(skipSelectionCache: true);
        }

        private static bool TagsIntersectActiveUserTagFilter(IEnumerable<string> tags, HashSet<string> activeFilterTags)
        {
            if (tags == null || activeFilterTags == null || activeFilterTags.Count == 0) return false;
            foreach (string raw in tags)
            {
                string norm = VpbLocalDatabase.NormalizeGalleryUserTagName(raw);
                if (!string.IsNullOrEmpty(norm) && activeFilterTags.Contains(norm)) return true;
            }
            return false;
        }

        private void SyncGridAfterUserTagRemoveInFilterMode(List<VpbLocalDatabase.GalleryUserTagRowKey> updatedRows, List<string> tags)
        {
            if (!IsUserTagIncludeFilterArmed()) return;
            if (activeUserTags == null || activeUserTags.Count == 0) return;
            if (activeContentType != ContentType.Category || !VpbSqlite3.IsAvailable) return;

            if (TryPruneVisibleGridAfterUserTagRemove(updatedRows))
            {
                if (recyclingGrid != null)
                {
                    recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                    recyclingGrid.Refresh();
                }
                try { UpdatePaginationText(); } catch { }
                return;
            }

            if (tags != null && TagsIntersectActiveUserTagFilter(tags, activeUserTags))
            {
                try { RefreshFiles(true, false, false, "user_tag_remove_filter_resync"); } catch { }
            }
        }

        private void OnLeftSubSortButtonClicked()
        {
            RectTransform rt = leftSubSortBtn != null ? leftSubSortBtn.GetComponent<RectTransform>() : null;
            if (leftSubSceneSortBtn != null && leftSubSceneSortBtn.activeSelf && leftSubSceneSortBarActive)
            {
                ToggleSidePaneSortMenu("SceneSource", rt);
                return;
            }
            if (leftActiveContent == ContentType.UserTags)
                ToggleSidePaneSortMenu("UserTagsApplied", rt);
            else
                ToggleSidePaneSortMenu("Tags", rt);
        }

        private void OnRightSubSortButtonClicked()
        {
            RectTransform rt = rightSubSortBtn != null ? rightSubSortBtn.GetComponent<RectTransform>() : null;
            if (rightSubSceneSortBtn != null && rightSubSceneSortBtn.activeSelf && rightSubSceneSortBarActive)
            {
                ToggleSidePaneSortMenu("SceneSource", rt);
                return;
            }
            if (rightActiveContent == ContentType.UserTags)
                ToggleSidePaneSortMenu("UserTagsApplied", rt);
            else
                ToggleSidePaneSortMenu("Tags", rt);
        }

        private void CacheAppliedUserTagsForSelection()
        {
            cachedAppliedUserTagsSelection.Clear();
            _userTagSelectionStates.Clear();
            _userTagSelectionRowCount = 0;
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            string cat = currentCategoryTitle ?? "";
            if (titleText != null && string.IsNullOrEmpty(cat)) cat = titleText.text ?? "";
            if (string.IsNullOrEmpty(cat)) return;

            bool allVar = VpbLocalDatabase.IsGalleryAllVarPseudoCategory(cat);
            var uniqueRows = new List<KeyValuePair<string, string>>(selectedFiles.Count);
            var seenRow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nSel = selectedFiles.Count;
            for (int i = 0; i < nSel; i++)
            {
                FileEntry fe = selectedFiles[i];
                string pkg, ip;
                if (!TryGetGalleryRowKeysForUserTags(fe, out pkg, out ip)) continue;

                // ALL VAR + inherit mode: package row (meta.json) can have no direct tag rows;
                // tags may exist only on child internal paths. Expand selection to child rows.
                if (allVar && _userTagInheritVarToChildren && !string.IsNullOrEmpty(pkg))
                {
                    var catMem = new List<KeyValuePair<string, string>>(256);
                    if (VpbLocalDatabase.TryReadCatMemRowsForPackage(pkg, catMem) && catMem.Count > 0)
                    {
                        for (int ci = 0; ci < catMem.Count; ci++)
                        {
                            string childIp = catMem[ci].Value ?? "";
                            if (string.IsNullOrEmpty(childIp)) continue;
                            string rk2 = pkg + "\n" + childIp;
                            if (!seenRow.Add(rk2)) continue;
                            uniqueRows.Add(new KeyValuePair<string, string>(pkg, childIp));
                        }
                        continue;
                    }
                }

                string rk = pkg + "\n" + ip;
                if (!seenRow.Add(rk)) continue;
                uniqueRows.Add(new KeyValuePair<string, string>(pkg, ip));
            }
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!VpbLocalDatabase.TryAccumulateGalleryUserTagSelectionCounts(cat, uniqueRows, counts)) return;
            _userTagSelectionRowCount = uniqueRows.Count;
            foreach (var kv in counts)
            {
                _userTagSelectionStates[kv.Key] = kv.Value >= _userTagSelectionRowCount
                    ? UserTagSelectionState.On
                    : UserTagSelectionState.Mixed;
            }
            foreach (var kv in counts)
                cachedAppliedUserTagsSelection.Add(new UserTagSideTabEntry { Name = kv.Key, Count = kv.Value });
            cachedAppliedUserTagsSelection.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private UserTagSelectionState GetUserTagSelectionState(string tagName)
        {
            if (string.IsNullOrEmpty(tagName) || _userTagSelectionRowCount <= 0) return UserTagSelectionState.Off;
            UserTagSelectionState st;
            return _userTagSelectionStates.TryGetValue(tagName, out st) ? st : UserTagSelectionState.Off;
        }

        private void toggleTagForSelectedItems(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return;
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            UserTagSelectionState st = GetUserTagSelectionState(tagName);
            bool remove = st == UserTagSelectionState.On;
            _userTagPulseTag = tagName;
            _userTagPulseUntil = Time.unscaledTime + UserTagVisualPulseSeconds;
            EnsureUserTagVisualPulse();
            ApplyUserTagsToFileEntries(new List<string> { tagName }, selectedFiles, remove);
        }

        /// <summary>Must match EnsureUserTagSideTabBulkBlock padding, spacing, title/hint/btn rows (s).</summary>
        private float UserTagsAvailStickyHeightPx()
        {
            float s = ChromeScale;
            int titleFs = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontTitleRef, s, GalleryUiDesignTokens.FontMinRef);
            int hintFs = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            // Pad(6,6,4,10) — top/bottom only affect sticky height.
            float padTop = Mathf.RoundToInt(4f * s);
            float padBottom = Mathf.RoundToInt(10f * s);
            float spacing = 7f * s;
            float titleBand = Mathf.Max(30f * s, titleFs * 1.22f);
            float hintH = Mathf.Max(22f * s, hintFs * 1.05f);
            float btnH = 36f * s;
            // title + hint + btn → two VLG gaps
            return padTop + titleBand + spacing + hintH + spacing + btnH + padBottom;
        }

        /// <summary>Prefer live preferred height after layout; formula is fallback / floor.</summary>
        private float ResolveUserTagsAvailStickyHeightPx(GameObject sticky)
        {
            float formula = UserTagsAvailStickyHeightPx();
            if (sticky == null || sticky.transform.childCount <= 0) return formula;
            try
            {
                RectTransform bulkRt = sticky.transform.GetChild(0) as RectTransform;
                if (bulkRt == null) return formula;
                LayoutRebuilder.ForceRebuildLayoutImmediate(bulkRt);
                float pref = LayoutUtility.GetPreferredHeight(bulkRt);
                if (pref > 1f) return Mathf.Max(formula, pref);
            }
            catch { }
            return formula;
        }

        private float UserTagsAvailFooterHeightPx()
        {
            float s = ChromeScale;
            // Match EnsureUserTagInheritVarToChildrenButtonInFooter row height + padding.
            float rowH = Mathf.Max(28f, 34f * s);
            float pad = Mathf.RoundToInt(4f * s);
            return rowH + pad * 2;
        }

        private float UserTagsAppliedStickyHeightPx()
        {
            float s = ChromeScale;
            float rowH = Mathf.Max(34f * s, Mathf.Max(32f, 42f * s));
            return 4f * s + rowH + 8f * s;
        }

        /// <summary>Pins Available / Applied toolbars; shrinks scroll viewports. Defaults restored when not UserTags.</summary>
        private void ApplyUserTagsStickyScrollChrome(float _)
        {
            // Last-wins: do not first-wins frame-gate. UpdateTabs applies chrome before content settles;
            // skipping later same-frame applies left one-frame wrong insets (Filter↔Tag flicker).
            ApplyUserTagsAvailStickyOneSide(true);
            ApplyUserTagsAvailStickyOneSide(false);
            ApplyUserTagsAppliedStickyOneSide(true);
            ApplyUserTagsAppliedStickyOneSide(false);
        }

        private void ApplyUserTagsAvailStickyOneSide(bool isLeft)
        {
            ContentType? ac = isLeft ? leftActiveContent : rightActiveContent;
            GameObject tabScroll = isLeft ? leftTabScrollGO : rightTabScrollGO;
            RectTransform vp = isLeft ? _leftTabViewportRT : _rightTabViewportRT;
            Vector2 defMin = isLeft ? _leftTabViewportDefOffsetMin : _rightTabViewportDefOffsetMin;
            Vector2 defMax = isLeft ? _leftTabViewportDefOffsetMax : _rightTabViewportDefOffsetMax;
            GameObject sticky = isLeft ? leftUserTagsAvailStickyGO : rightUserTagsAvailStickyGO;
            GameObject footer = isLeft ? leftUserTagsAvailFooterGO : rightUserTagsAvailFooterGO;
            if (vp == null || sticky == null || tabScroll == null) return;

            vp.offsetMin = defMin;
            vp.offsetMax = defMax;

            if (ac != ContentType.UserTags || !tabScroll.activeSelf)
            {
                sticky.SetActive(false);
                if (footer != null) footer.SetActive(false);
                GameObject hidePinned = isLeft ? leftUserTagsAvailPinnedStickyGO : rightUserTagsAvailPinnedStickyGO;
                if (hidePinned != null) hidePinned.SetActive(false);
                return;
            }

            float h = ResolveUserTagsAvailStickyHeightPx(sticky);
            sticky.SetActive(true);
            RectTransform srt = sticky.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.offsetMin = new Vector2(defMin.x, -h);
            srt.offsetMax = new Vector2(defMax.x, 0f);

            float pinnedH = UserTagsAvailPinnedStickyHeightPx();
            GameObject pinnedStrip = EnsureUserTagsAvailPinnedStickyGO(isLeft);
            if (pinnedStrip != null)
            {
                if (pinnedH > 0.5f)
                {
                    pinnedStrip.SetActive(true);
                    RectTransform prt = pinnedStrip.GetComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0f, 1f);
                    prt.anchorMax = new Vector2(1f, 1f);
                    prt.pivot = new Vector2(0.5f, 1f);
                    prt.offsetMin = new Vector2(defMin.x, -(h + pinnedH));
                    prt.offsetMax = new Vector2(defMax.x, -h);
                }
                else
                    pinnedStrip.SetActive(false);
            }

            // Measure Def-restored viewport before inset. Never shrink Mask below a usable strip —
            // over-inset → height≈0, Tags(N) still correct, rows clipped (black empty under chrome).
            float availH = vp.rect.height;
            if (availH < 1f)
            {
                try
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(vp);
                    Canvas.ForceUpdateCanvases();
                    availH = vp.rect.height;
                }
                catch { }
            }
            float topChrome = h + pinnedH;
            float minVp = Mathf.Max(72f, SideTabVirtRowStridePx() * 3f);
            if (availH > 1f)
            {
                float maxChrome = Mathf.Max(0f, availH - minVp);
                if (topChrome > maxChrome)
                    topChrome = maxChrome;
            }

            vp.offsetMin = defMin;
            vp.offsetMax = new Vector2(defMax.x, defMax.y - topChrome);

            // Footer: Inherit Tags toggle only for ALL VAR, pinned to bottom of Available half.
            if (footer != null)
            {
                bool showFooter = false;
                try
                {
                    string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
                    showFooter = VpbLocalDatabase.IsGalleryAllVarPseudoCategory(cat);
                }
                catch { showFooter = false; }

                if (!showFooter)
                {
                    footer.SetActive(false);
                }
                else
                {
                    float fh = UserTagsAvailFooterHeightPx();
                    footer.SetActive(true);
                    RectTransform frt = footer.GetComponent<RectTransform>();
                    frt.anchorMin = new Vector2(0f, 0f);
                    frt.anchorMax = new Vector2(1f, 0f);
                    frt.pivot = new Vector2(0.5f, 0f);
                    frt.offsetMin = new Vector2(defMin.x, 0f);
                    frt.offsetMax = new Vector2(defMax.x, fh);
                    float bottomInset = fh;
                    // Keep min viewport after footer inset too.
                    if (availH > 1f)
                    {
                        float maxBottom = Mathf.Max(0f, availH - topChrome - minVp);
                        if (bottomInset > maxBottom) bottomInset = maxBottom;
                    }
                    vp.offsetMin = new Vector2(defMin.x, defMin.y + bottomInset);

                    EnsureUserTagInheritVarToChildrenButtonInFooter(footer.transform);
                }
            }

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(srt);
                if (sticky.transform.childCount > 0)
                {
                    RectTransform bulkRt = sticky.transform.GetChild(0) as RectTransform;
                    if (bulkRt != null)
                        LayoutRebuilder.ForceRebuildLayoutImmediate(bulkRt);
                }
                if (footer != null && footer.activeSelf)
                {
                    RectTransform frt = footer.GetComponent<RectTransform>();
                    if (frt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(frt);
                }
            }
            catch { }
        }

        private void ApplyUserTagsAppliedStickyOneSide(bool isLeft)
        {
            ContentType? ac = isLeft ? leftActiveContent : rightActiveContent;
            GameObject subScroll = isLeft ? leftSubTabScrollGO : rightSubTabScrollGO;
            RectTransform vp = isLeft ? _leftSubTabViewportRT : _rightSubTabViewportRT;
            Vector2 defMin = isLeft ? _leftSubTabViewportDefOffsetMin : _rightSubTabViewportDefOffsetMin;
            Vector2 defMax = isLeft ? _leftSubTabViewportDefOffsetMax : _rightSubTabViewportDefOffsetMax;
            GameObject sticky = isLeft ? leftUserTagsAppliedStickyGO : rightUserTagsAppliedStickyGO;
            if (vp == null || sticky == null) return;

            vp.offsetMin = defMin;
            vp.offsetMax = defMax;

            if (ac != ContentType.UserTags || subScroll == null || !subScroll.activeSelf)
            {
                sticky.SetActive(false);
                GameObject ap = isLeft ? leftUserTagsAppliedPinnedStickyGO : rightUserTagsAppliedPinnedStickyGO;
                if (ap != null) ap.SetActive(false);
                return;
            }

            float h = UserTagsAppliedStickyHeightPx();
            sticky.SetActive(true);
            RectTransform srt = sticky.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.offsetMin = new Vector2(defMin.x, -h);
            srt.offsetMax = new Vector2(defMax.x, 0f);

            float pinnedH = UserTagsAppliedPinnedStickyHeightPx();
            GameObject pinnedStrip = EnsureUserTagsAppliedPinnedStickyGO(isLeft);
            if (pinnedStrip != null)
            {
                if (pinnedH > 0.5f)
                {
                    pinnedStrip.SetActive(true);
                    RectTransform prt = pinnedStrip.GetComponent<RectTransform>();
                    prt.anchorMin = new Vector2(0f, 1f);
                    prt.anchorMax = new Vector2(1f, 1f);
                    prt.pivot = new Vector2(0.5f, 1f);
                    prt.offsetMin = new Vector2(defMin.x, -(h + pinnedH));
                    prt.offsetMax = new Vector2(defMax.x, -h);
                }
                else
                    pinnedStrip.SetActive(false);
            }

            float topChrome = h + pinnedH;
            vp.offsetMin = defMin;
            vp.offsetMax = new Vector2(defMax.x, defMax.y - topChrome);
        }

        private void SyncUserTagAvailTitleCount(bool isLeft)
        {
            int sticky = _userTagStickyRows != null ? _userTagStickyRows.Count : 0;
            int scroll = _userTagVirtView != null ? _userTagVirtView.Count : 0;
            int n = sticky + scroll;
            Text t = isLeft ? leftUserTagAvailTitleText : rightUserTagAvailTitleText;
            if (t == null) return;
            t.text = FormatUserTagAvailTitle(n);
        }

        private string FormatUserTagAvailTitle(int n)
        {
            UserTagAvailMode chrome = ResolveUserTagWorkModeForChrome();
            string mode = chrome == UserTagAvailMode.Tag
                ? VPBTranslation.T("gallery.usertags.title_mode_tag", "Tag")
                : VPBTranslation.T("gallery.usertags.title_mode_filter", "Filter");
            return string.Format(
                VPBTranslation.T("gallery.usertags.tags_with_count_mode", "Tags ({0}) · {1}"),
                n, mode);
        }

        /// <summary>Verb-first work-mode hint under Tags title (recognition, not F/T recall).</summary>
        private string GetUserTagWorkModeHint()
        {
            if (ResolveUserTagWorkModeForChrome() == UserTagAvailMode.Tag)
            {
                return VPBTranslation.T(
                    "gallery.usertags.hint_tag",
                    "Click a tag → apply to selection");
            }
            return VPBTranslation.T(
                "gallery.usertags.hint_filter",
                "Click a tag → filter the grid");
        }

        /// <summary>Pick-row tip uses chrome work mode (Tag vs Filter), not Not-tagged browse.</summary>
        private string GetUserTagWorkModePickTip()
        {
            if (ResolveUserTagWorkModeForChrome() == UserTagAvailMode.FilterByTags)
                return GetUserTagPickRowTooltipFilter();
            return VPBTranslation.T(
                "gallery.usertags.pick_row_tooltip_tag",
                "Tag selection: click toggles this tag on selected item(s). Drag to Applied below.");
        }

        private void SyncUserTagApplyBtnCount(bool isLeft)
        {
            Text t = isLeft ? leftUserTagApplyBtnText : rightUserTagApplyBtnText;
            if (t == null) return;
            int c = activeUserTags != null ? activeUserTags.Count : 0;
            t.text = string.Format(VPBTranslation.T("gallery.usertags.btn_apply_with_count", "Tag ({0})"), c);
        }

        private void SyncUserTagAppliedTitleCount(int visibleCount, bool isLeft)
        {
            Text t = isLeft ? leftUserTagAppliedTitleText : rightUserTagAppliedTitleText;
            if (t == null) return;
            t.text = string.Format(VPBTranslation.T("gallery.usertags.applied_with_count", "Applied ({0})"), visibleCount);
        }

        private void EnsureUserTagsAppliedToolbar(Transform scrollContentContainer, bool isLeft)
        {
            if (scrollContentContainer == null || backgroundBoxGO == null) return;
            Transform sticky = isLeft ? leftUserTagsAppliedStickyGO?.transform : rightUserTagsAppliedStickyGO?.transform;
            if (sticky == null) return;

            Transform strayInScroll = scrollContentContainer.Find("VPB_UserTagsAppliedToolbar_v3");
            if (strayInScroll == null) strayInScroll = scrollContentContainer.Find("VPB_UserTagsAppliedToolbar_v2");
            if (strayInScroll != null)
                UnityEngine.Object.Destroy(strayInScroll.gameObject);

            Transform legacyTb = sticky.Find("VPB_UserTagsAppliedToolbar_v1");
            if (legacyTb != null)
                UnityEngine.Object.Destroy(legacyTb.gameObject);
            if (sticky.Find("VPB_UserTagsAppliedToolbar_v2") != null)
                UnityEngine.Object.Destroy(sticky.Find("VPB_UserTagsAppliedToolbar_v2").gameObject);
            if (sticky.Find("VPB_UserTagsAppliedToolbar_v3") != null) return;

            float s = ChromeScale;
            float u = s * 1.38f;

            GameObject root = new GameObject("VPB_UserTagsAppliedToolbar_v3");
            root.transform.SetParent(sticky, false);
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0f, 1f);
            rootRT.anchorMax = new Vector2(1f, 1f);
            rootRT.pivot = new Vector2(0.5f, 1f);
            rootRT.sizeDelta = Vector2.zero;

            UI.AddVLG(root, 6f * s, UI.Pad(6f, 6f, 4f, 8f, s), TextAnchor.UpperCenter);

            LayoutElement rootLe = UI.AddLE(root, flexibleWidth: 1f);
            ContentSizeFitter rootCsf = root.AddComponent<ContentSizeFitter>();
            rootCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject titleRow = UI.CreateChildRT(root, "AppliedTitleRow");
            UI.AddHLG(titleRow, 8f * s);

            float delSz = Mathf.Max(32f, 42f * s);

            LayoutElement titleRowLe = UI.AddLE(titleRow, minHeight: Mathf.Max(30f * s, delSz), preferredHeight: Mathf.Max(34f * s, delSz), flexibleWidth: 1f);

            Text titleTxt = UI.CreateLabel(titleRow, string.Format(VPBTranslation.T("gallery.usertags.applied_with_count", "Applied ({0})"), 0),
                GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, u, GalleryUiDesignTokens.FontMinRef),
                new Color(0.88f, 0.88f, 0.92f, 1f), name: "AppliedTitleText");
            LayoutElement titleLe = UI.AddLE(titleTxt.gameObject, minHeight: Mathf.Max(28f * s, delSz * 0.85f), flexibleWidth: 1f);
            Sprite delSpr = UI.LoadIconSprite("vpb_icons/delete.png", Color.white);
            GameObject delBtn = UI.CreateSideTabSquareIconButton(titleRow, delSz, delSpr, RemoveFocusedAppliedUserTagFromSelection, new Color(0.5f, 0.22f, 0.22f, 1f), 6f * s);
            delBtn.name = "RemoveAppliedIconBtn";
            AddTooltipPlain(delBtn, VPBTranslation.T("gallery.usertags.remove_applied_tooltip", "Remove selected tag(s) from selection. Select rows below first (Ctrl+click toggle, Shift+click range). Or drag tag row(s) onto this button."));

            if (isLeft) leftUserTagAppliedTitleText = titleTxt;
            else rightUserTagAppliedTitleText = titleTxt;
        }

        private void SyncUserTagsAppliedToolbarDropZones(Transform container)
        {
            if (container == null) return;
            Transform tb = container.Find("VPB_UserTagsAppliedToolbar_v3");
            if (tb == null && leftUserTagsAppliedStickyGO != null)
                tb = leftUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v3");
            if (tb == null && rightUserTagsAppliedStickyGO != null)
                tb = rightUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v3");
            if (tb == null) return;
            GameObject rootGo = tb.gameObject;
            Image rayImg = rootGo.GetComponent<Image>();
            if (rayImg == null)
            {
                rayImg = UI.AddImage(rootGo, new Color(1f, 1f, 1f, 0.02f));
            }
            UserTagApplyDropZone dz = rootGo.GetComponent<UserTagApplyDropZone>();
            if (dz == null) dz = rootGo.AddComponent<UserTagApplyDropZone>();
            dz.Panel = this;

            Transform titleRow = tb.Find("AppliedTitleRow");
            if (titleRow != null)
            {
                Transform delT = titleRow.Find("RemoveAppliedIconBtn");
                if (delT != null)
                {
                    UserTagRemoveDropZone rdz = delT.gameObject.GetComponent<UserTagRemoveDropZone>();
                    if (rdz == null) rdz = delT.gameObject.AddComponent<UserTagRemoveDropZone>();
                    rdz.Panel = this;
                }
            }
        }

        private void EnsureUserTagApplyDropCatchStrip(Transform container)
        {
            if (container == null) return;
            const string stripName = "VPB_UserTagApplyDropCatchStrip";
            float s = ChromeScale;
            Transform stripT = container.Find(stripName);
            GameObject stripGo;
            if (stripT == null)
            {
                stripGo = new GameObject(stripName);
                stripGo.transform.SetParent(container, false);
                Image img = UI.AddImage(stripGo, new Color(1f, 1f, 1f, 0.03f));
                LayoutElement le = UI.AddLE(stripGo, minHeight: 2f, preferredHeight: 4f * s, flexibleWidth: 1f);
                UserTagApplyDropZone dz = stripGo.AddComponent<UserTagApplyDropZone>();
                dz.Panel = this;
                AddTooltipPlain(stripGo, VPBTranslation.T("gallery.usertags.apply_drop_zone_tip", "Drop tags here to apply to selection."));
            }
            else
            {
                stripGo = stripT.gameObject;
                LayoutElement le = stripGo.GetComponent<LayoutElement>();
                if (le == null) le = stripGo.AddComponent<LayoutElement>();
                le.minHeight = 2f;
                le.preferredHeight = 4f * s;
                le.flexibleWidth = 1f;
                UserTagApplyDropZone dz = stripGo.GetComponent<UserTagApplyDropZone>();
                if (dz == null) dz = stripGo.AddComponent<UserTagApplyDropZone>();
                dz.Panel = this;
            }
            stripGo.transform.SetAsLastSibling();
        }

        private void EnsureUserTagApplyDropOverlay(Transform container)
        {
            if (container == null) return;
            const string overlayName = "VPB_UserTagApplyDropOverlay";

            Transform existing = container.Find(overlayName);
            GameObject go;
            if (existing == null)
            {
                go = new GameObject(overlayName);
                go.transform.SetParent(container, false);

                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                // Keep out of VerticalLayoutGroup sizing.
                LayoutElement le = go.AddComponent<LayoutElement>();
                le.ignoreLayout = true;

                Image img = UI.AddImage(go, new Color(1f, 1f, 1f, 0.0f));

                // Only participate in raycasts while a tag-drag session active.
                var gate = go.AddComponent<UserTagDropRaycastGate>();
                gate.Image = img;

                UserTagApplyDropZone dz = go.AddComponent<UserTagApplyDropZone>();
                dz.Panel = this;
            }
            else
            {
                go = existing.gameObject;
                LayoutElement le = go.GetComponent<LayoutElement>();
                if (le == null) le = go.AddComponent<LayoutElement>();
                le.ignoreLayout = true;

                Image img = go.GetComponent<Image>();
                if (img == null) img = go.AddComponent<Image>();
                img.raycastTarget = true;

                var gate = go.GetComponent<UserTagDropRaycastGate>();
                if (gate == null) gate = go.AddComponent<UserTagDropRaycastGate>();
                gate.Image = img;

                UserTagApplyDropZone dz = go.GetComponent<UserTagApplyDropZone>();
                if (dz == null) dz = go.AddComponent<UserTagApplyDropZone>();
                dz.Panel = this;
            }

            go.transform.SetAsLastSibling();
        }

        internal void UserTagPickDragBeginPayload(string primaryTag, List<string> tagsOut)
        {
            dragStartTag(primaryTag);
            if (tagsOut == null) return;
            tagsOut.Clear();
            if (string.IsNullOrEmpty(primaryTag)) return;
            tagsOut.Add(primaryTag);
        }

        internal void UserTagApplyDroppedTags(List<string> tags)
        {
            if (tags == null || tags.Count == 0) return;
            ApplyUserTagsToFileEntries(new List<string>(tags), selectedFiles, remove: false);
        }

        /// <summary>Drop tag on row. Selection not touched.</summary>
        internal void UserTagApplyDroppedTagsRespectingGalleryRow(List<string> tags, FileEntry galleryRowHit)
        {
            if (tags == null || tags.Count == 0) return;
            List<string> t = new List<string>(tags);

            if (galleryRowHit == null || galleryRowHit is InternalSettingRowEntry)
            {
                ApplyUserTagsToFileEntries(t, selectedFiles, remove: false);
                return;
            }

            dropTagOnItem(t, galleryRowHit);
        }

        private void dragStartTag(string tagName)
        {
            _userTagDragHoverFile = null;
            if (!string.IsNullOrEmpty(tagName))
                SetStatus(VPBTranslation.T("gallery.usertags.drag_started", "Drag tag to item."));
        }

        internal void dragHoverItem(FileEntry galleryRowHit, List<string> tags)
        {
            if (!ReferenceEquals(_userTagDragHoverFile, galleryRowHit))
            {
                FileEntry old = _userTagDragHoverFile;
                _userTagDragHoverFile = galleryRowHit;
                RefreshUserTagDropVisualForFile(old);
                RefreshUserTagDropVisualForFile(galleryRowHit);
            }
            SetStatus(BuildUserTagDragDropStatusHint(galleryRowHit, tags));
        }

        internal void dropTagOnItem(List<string> tags, FileEntry galleryRowHit)
        {
            if (tags == null || tags.Count == 0 || galleryRowHit == null || galleryRowHit is InternalSettingRowEntry) return;
            _userTagDropPulseKey = GetSelectionIdentityKey(galleryRowHit, false);
            _userTagDropPulseUntil = Time.unscaledTime + UserTagVisualPulseSeconds;
            EnsureUserTagVisualPulse();
            RefreshUserTagDropVisualForFile(galleryRowHit);
            ApplyUserTagsToFileEntries(new List<string>(tags), new List<FileEntry> { galleryRowHit }, remove: false);
        }

        private bool IsUserTagDropVisualActive(FileEntry file)
        {
            if (file == null) return false;
            if (_userTagDragHoverFile != null && ReferenceEquals(_userTagDragHoverFile, file)) return true;
            if (Time.unscaledTime >= _userTagDropPulseUntil || string.IsNullOrEmpty(_userTagDropPulseKey)) return false;
            string k = GetSelectionIdentityKey(file, false);
            return !string.IsNullOrEmpty(k) && string.Equals(k, _userTagDropPulseKey, StringComparison.OrdinalIgnoreCase);
        }

        private float GetUserTagDropFlashAlpha(FileEntry file)
        {
            if (file == null) return 0f;
            bool hover = _userTagDragHoverFile != null && ReferenceEquals(_userTagDragHoverFile, file);
            bool dropPulse = Time.unscaledTime < _userTagDropPulseUntil
                && !string.IsNullOrEmpty(_userTagDropPulseKey)
                && string.Equals(GetSelectionIdentityKey(file, false), _userTagDropPulseKey, StringComparison.OrdinalIgnoreCase);
            if (dropPulse)
            {
                // Brief bright flash then fade — clear "applied" cue.
                float remaining = _userTagDropPulseUntil - Time.unscaledTime;
                float u = Mathf.Clamp01(remaining / UserTagVisualPulseSeconds); // 1 → 0
                return Mathf.Lerp(0.08f, 0.55f, u * u);
            }
            if (hover) return 0.22f;
            return 0f;
        }

        private void ApplyUserTagDropVisual(GameObject btnGO, FileEntry file)
        {
            if (btnGO == null) return;
            bool active = IsUserTagDropVisualActive(file);
            float flashAlpha = GetUserTagDropFlashAlpha(file);

            // Full-cell flash so apply/hover is obvious (border alone is easy to miss).
            Transform flashTr = btnGO.transform.Find(UserTagDropFlashName);
            GameObject flashGO = flashTr != null ? flashTr.gameObject : null;
            if (active)
            {
                Color flashCol = new Color(
                    UserTagDropGlowColor.r, UserTagDropGlowColor.g, UserTagDropGlowColor.b, flashAlpha);
                if (flashGO == null)
                {
                    flashGO = new GameObject(UserTagDropFlashName);
                    flashGO.transform.SetParent(btnGO.transform, false);
                    flashGO.transform.SetAsLastSibling();
                    UI.AddImage(flashGO, flashCol, raycastTarget: false);
                    RectTransform frt = flashGO.GetComponent<RectTransform>();
                    if (frt != null)
                    {
                        frt.anchorMin = Vector2.zero;
                        frt.anchorMax = Vector2.one;
                        frt.offsetMin = Vector2.zero;
                        frt.offsetMax = Vector2.zero;
                    }
                    LayoutElement fle = flashGO.GetComponent<LayoutElement>();
                    if (fle == null) fle = flashGO.AddComponent<LayoutElement>();
                    fle.ignoreLayout = true;
                }
                else
                {
                    flashGO.SetActive(true);
                    flashGO.transform.SetAsLastSibling();
                    Image flashImg = flashGO.GetComponent<Image>();
                    if (flashImg != null)
                    {
                        flashImg.raycastTarget = false;
                        flashImg.color = flashCol;
                    }
                }

                UIHoverBorder hoverBorder = btnGO.GetComponent<UIHoverBorder>();
                if (hoverBorder != null)
                {
                    hoverBorder.enabled = true;
                    hoverBorder.hoverColor = UserTagDropGlowColor;
                    hoverBorder.isSelected = true;
                    hoverBorder.borderSize = Mathf.Max(hoverBorder.borderSize, 4f);
                    hoverBorder.ApplyBorderSettings();
                }
                Transform innerBorderTr = btnGO.transform.Find("GridInnerBorder");
                GameObject innerBorderGO = innerBorderTr != null ? innerBorderTr.gameObject : null;
                if (innerBorderGO != null)
                {
                    SetBorderThickness(innerBorderGO, Mathf.Max(EffectiveGridSelectedBorderWidth(), 4f));
                    SetGalleryInnerBorderEdgeTint(innerBorderGO, UserTagDropGlowColor);
                    innerBorderGO.SetActive(true);
                }
            }
            else if (flashGO != null)
            {
                flashGO.SetActive(false);
            }
        }

        private void RefreshUserTagDropVisualForFile(FileEntry file)
        {
            if (file == null) return;
            string wanted = GetSelectionIdentityKey(file, false);
            if (string.IsNullOrEmpty(wanted)) return;
            RefreshVisibleGalleryFileButtonsForKey(wanted);
        }

        private void RefreshVisibleGalleryFileButtonsForKey(string wantedKey)
        {
            if (string.IsNullOrEmpty(wantedKey)) return;
            if (recyclingGrid != null)
            {
                int n = recyclingGrid.ActiveItemCount;
                for (int i = 0; i < n; i++)
                {
                    RecyclingGridItem rgv = recyclingGrid.GetActiveItemAt(i);
                    if (rgv == null || rgv.gameObject == null || !rgv.gameObject.activeSelf) continue;
                    GameObject btn = rgv.gameObject;
                    FileEntry fe = ResolveVisibleFileEntryFromItem(rgv, rgv.binder);
                    if (fe == null) continue;
                    string key = GetSelectionIdentityKey(fe, false);
                    if (string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase))
                        UpdateFileButtonVisuals(btn, fe);
                }
                return;
            }

            for (int i = 0; activeButtons != null && i < activeButtons.Count; i++)
            {
                GameObject btn = activeButtons[i];
                if (btn == null || !btn.activeSelf) continue;
                FileEntry fe = ResolveVisibleFileEntryFromButton(btn);
                if (fe == null) continue;
                string key = GetSelectionIdentityKey(fe, false);
                if (string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase))
                    UpdateFileButtonVisuals(btn, fe);
            }
        }

        private FileEntry ResolveVisibleFileEntryFromItem(RecyclingGridItem rgvItem, FileButtonBinder binder)
        {
            if (rgvItem == null) return null;
            if (binder == null) binder = rgvItem.binder;
            if (binder == null) binder = FileButtonBinder.GetOrAdd(rgvItem.gameObject);
            UIDraggableItem diag = binder != null ? binder.draggable : null;
            try
            {
                if (settingsListViewActive && currentFilteredFiles != null
                    && rgvItem.index >= 0 && rgvItem.index < currentFilteredFiles.Count)
                    return currentFilteredFiles[rgvItem.index];
                if (diag != null) return diag.FileEntry;
            }
            catch { return diag != null ? diag.FileEntry : null; }
            return null;
        }

        private FileEntry ResolveVisibleFileEntryFromButton(GameObject btn)
        {
            if (btn == null) return null;
            FileButtonBinder binder = FileButtonBinder.GetOrAdd(btn);
            UIDraggableItem diag = binder != null ? binder.draggable : null;
            RecyclingGridItem rgvItem = binder != null ? binder.gridItem : null;
            try
            {
                if (settingsListViewActive && rgvItem != null && currentFilteredFiles != null
                    && rgvItem.index >= 0 && rgvItem.index < currentFilteredFiles.Count)
                    return currentFilteredFiles[rgvItem.index];
                if (diag != null) return diag.FileEntry;
            }
            catch { return diag != null ? diag.FileEntry : null; }
            return null;
        }

        private void EnsureUserTagVisualPulse()
        {
            if (_userTagVisualPulseCoroutine != null) return;
            _userTagVisualPulseCoroutine = StartCoroutine(UserTagVisualPulseCoroutine());
        }

        private IEnumerator UserTagVisualPulseCoroutine()
        {
            while (Time.unscaledTime < _userTagPulseUntil || Time.unscaledTime < _userTagDropPulseUntil)
            {
                RefreshVisibleUserTagRows();
                if (!string.IsNullOrEmpty(_userTagDropPulseKey))
                    RefreshVisibleGalleryFileButtonsForKey(_userTagDropPulseKey);
                yield return null;
            }

            string oldDropKey = _userTagDropPulseKey;
            _userTagPulseTag = null;
            _userTagDropPulseKey = null;
            RefreshVisibleUserTagRows();
            RefreshVisibleGalleryFileButtonsForKey(oldDropKey);
            _userTagVisualPulseCoroutine = null;
        }

        private void RefreshVisibleUserTagRows(bool skipSelectionCache = false)
        {
            if (!skipSelectionCache)
                CacheAppliedUserTagsForSelection();
            try
            {
                if (leftActiveContent == ContentType.UserTags && leftTabContainerGO != null)
                {
                    SyncUserTagAvailPinnedStickyRows(true, UserTagStateOnColor, leftTabContainerGO.transform);
                    UpdateUserTagVirtualVisible(true, UserTagStateOnColor, leftTabContainerGO.transform);
                }
                if (rightActiveContent == ContentType.UserTags && rightTabContainerGO != null)
                {
                    SyncUserTagAvailPinnedStickyRows(false, UserTagStateOnColor, rightTabContainerGO.transform);
                    UpdateUserTagVirtualVisible(false, UserTagStateOnColor, rightTabContainerGO.transform);
                }
            }
            catch { }
        }

        private void SyncUserTagRowFilterIcon(GameObject rowGo, bool active, float scale)
        {
            if (rowGo == null) return;
            const string iconName = "VPB_UserTagFilterActiveIcon";
            Transform existing = rowGo.transform.Find(iconName);
            GameObject iconGo = existing != null ? existing.gameObject : null;
            if (!active)
            {
                if (iconGo != null) iconGo.SetActive(false);
                return;
            }

            if (iconGo == null)
            {
                iconGo = new GameObject(iconName);
                iconGo.transform.SetParent(rowGo.transform, false);
                Image imgNew = iconGo.AddComponent<Image>();
                imgNew.raycastTarget = false;
                imgNew.preserveAspect = true;
            }

            Image img = iconGo.GetComponent<Image>();
            if (img != null)
            {
                Sprite spr = UI.LoadIconSprite("vpb_icons/filter_on.png", Color.white);
                if (spr == null) spr = UI.LoadIconSprite("vpb_icons/filter_off.png", Color.white);
                img.sprite = spr;
                img.color = Color.white;
            }

            RectTransform rt = iconGo.GetComponent<RectTransform>();
            if (rt != null)
            {
                float size = Mathf.Clamp(22f * scale, 16f, 30f);
                rt.anchorMin = new Vector2(1f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(-8f * scale, 0f);
            }
            LayoutElement iconLe = iconGo.GetComponent<LayoutElement>();
            if (iconLe == null) iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.ignoreLayout = true;
            iconGo.SetActive(true);
            iconGo.transform.SetAsLastSibling();
        }

        /// <summary>
        /// Sticky pin-strip row height only — must match virt/side-tab rows.
        /// VLG spacing is separate; do not bake <see cref="GalleryUiDesignTokens.SideTabRowSpacingRef"/> here
        /// (that was double-counting and made pin/filter toggles jump row gaps).
        /// </summary>
        private float UserTagPinnedRowHeightPx()
        {
            return SideTabRowHeightPx(ChromeScale);
        }

        private float UserTagsAvailPinnedStickyHeightPx()
        {
            int n = _userTagStickyRows != null ? _userTagStickyRows.Count : 0;
            if (n <= 0) return 0f;
            float s = ChromeScale;
            float rowH = UserTagPinnedRowHeightPx();
            float gap = GalleryUiDesignTokens.SideTabRowSpacingRef * s;
            return n * rowH + Mathf.Max(0f, n - 1) * gap + 4f * s;
        }

        private float UserTagsAppliedPinnedStickyHeightPx()
        {
            int n = _userTagAppliedPinnedRows != null ? _userTagAppliedPinnedRows.Count : 0;
            if (n <= 0) return 0f;
            float s = ChromeScale;
            float rowH = UserTagPinnedRowHeightPx();
            float gap = GalleryUiDesignTokens.SideTabRowSpacingRef * s;
            return n * rowH + Mathf.Max(0f, n - 1) * gap + 4f * s;
        }

        private GameObject EnsureUserTagsAvailPinnedStickyGO(bool isLeft)
        {
            GameObject go = isLeft ? leftUserTagsAvailPinnedStickyGO : rightUserTagsAvailPinnedStickyGO;
            if (go != null) return go;
            GameObject tabScroll = isLeft ? leftTabScrollGO : rightTabScrollGO;
            if (tabScroll == null) return null;
            go = new GameObject(isLeft ? "VPB_UserTagsAvailPinnedSticky_L" : "VPB_UserTagsAvailPinnedSticky_R");
            go.transform.SetParent(tabScroll.transform, false);
            go.SetActive(false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;
            float s = ChromeScale;
            UI.AddVLG(go, GalleryUiDesignTokens.SideTabRowSpacingRef * s, UI.Pad(5f, 5f, 0f, 4f, s), TextAnchor.UpperCenter);
            go.transform.SetAsLastSibling();
            if (isLeft) leftUserTagsAvailPinnedStickyGO = go;
            else rightUserTagsAvailPinnedStickyGO = go;
            return go;
        }

        private GameObject EnsureUserTagsAppliedPinnedStickyGO(bool isLeft)
        {
            GameObject go = isLeft ? leftUserTagsAppliedPinnedStickyGO : rightUserTagsAppliedPinnedStickyGO;
            if (go != null) return go;
            GameObject subScroll = isLeft ? leftSubTabScrollGO : rightSubTabScrollGO;
            if (subScroll == null) return null;
            go = new GameObject(isLeft ? "VPB_UserTagsAppliedPinnedSticky_L" : "VPB_UserTagsAppliedPinnedSticky_R");
            go.transform.SetParent(subScroll.transform, false);
            go.SetActive(false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = Vector2.zero;
            float s = ChromeScale;
            UI.AddVLG(go, GalleryUiDesignTokens.SideTabRowSpacingRef * s, UI.Pad(5f, 5f, 0f, 4f, s), TextAnchor.UpperCenter);
            go.transform.SetAsLastSibling();
            if (isLeft) leftUserTagsAppliedPinnedStickyGO = go;
            else rightUserTagsAppliedPinnedStickyGO = go;
            return go;
        }

        private void SyncUserTagAvailPinnedStickyRows(bool isLeft, Color utAccent, Transform tabContainer)
        {
            if (!IsUserTagsSideTabOpen(isLeft)) return;
            GameObject strip = EnsureUserTagsAvailPinnedStickyGO(isLeft);
            if (strip == null) return;
            if (VpbPerfDiag.CachedEnabled) VpbPerfDiag.UserTagPinnedRebuild++;
            UI.DestroyAllChildren(strip.transform);

            int count = _userTagStickyRows != null ? _userTagStickyRows.Count : 0;
            if (count == 0)
            {
                strip.SetActive(false);
                return;
            }

            strip.SetActive(true);
            string pickTip = GetUserTagWorkModePickTip();
            float rowH = UserTagPinnedRowHeightPx();

            // Create rows first, layout stretch, then bind labels.
            // Bind-before-layout used CreateUIButton width (~170) with scaled font → "Create Tag: ab" then "...".
            var stickyBtns = new GameObject[count];
            for (int ri = 0; ri < count; ri++)
            {
                GameObject btnGO = UI.CreateUIButton(strip, 170, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                AddHoverDelegate(btnGO);
                LayoutElement le = btnGO.GetComponent<LayoutElement>();
                if (le == null) le = btnGO.AddComponent<LayoutElement>();
                le.minHeight = rowH;
                le.preferredHeight = rowH;
                le.flexibleWidth = 1f;
                stickyBtns[ri] = btnGO;
            }

            try { LayoutRebuilder.ForceRebuildLayoutImmediate(strip.GetComponent<RectTransform>()); } catch { }

            for (int ri = 0; ri < count; ri++)
            {
                if (stickyBtns[ri] == null) continue;
                BindUserTagVirtButton(stickyBtns[ri], _userTagStickyRows[ri], utAccent, pickTip, isLeft);
            }
        }

        private void SyncUserTagAppliedPinnedStickyRows(bool isLeft, List<UserTagSideTabEntry> pinnedRows, Color accent, float scale)
        {
            GameObject strip = EnsureUserTagsAppliedPinnedStickyGO(isLeft);
            if (strip == null) return;
            UI.DestroyAllChildren(strip.transform);

            int count = pinnedRows != null ? pinnedRows.Count : 0;
            if (count == 0)
            {
                strip.SetActive(false);
                return;
            }

            strip.SetActive(true);
            float pinInset = 34f * scale;
            for (int vi = 0; vi < count; vi++)
            {
                UserTagSideTabEntry ae = pinnedRows[vi];
                bool isSel = userTagAppliedRemoveSelection.Contains(ae.Name);
                string labelA = ae.Name + " (" + ae.Count + ")";
                string tagFocusSnap = ae.Name;
                int viCapture = vi;
                var visiblePinned = pinnedRows;
                CreateTabButton(strip.transform, labelA,
                    isSel ? accent : ColorInactiveRow,
                    isSel,
                    () => { OnAppliedUserTagRowClicked(viCapture, visiblePinned, tagFocusSnap); },
                    null, null, null, tagFocusSnap, TextAnchor.MiddleCenter, pinInset, 0f);
                Transform last = strip.transform.GetChild(strip.transform.childCount - 1);
                if (last != null)
                    SyncUserTagRowPinButton(last.gameObject, tagFocusSnap, false, scale, isLeft, appliedRow: true);
            }

            try { LayoutRebuilder.ForceRebuildLayoutImmediate(strip.GetComponent<RectTransform>()); } catch { }
        }

        private float MeasureUserTagVirtViewportHeight(RectTransform viewport, float rowH)
        {
            float viewportH = viewport != null ? viewport.rect.height : 0f;
            if (viewportH >= rowH * 2f) return viewportH;
            try
            {
                if (viewport != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
                    Canvas.ForceUpdateCanvases();
                    viewportH = viewport.rect.height;
                }
            }
            catch { }
            // Invent only while sticky chrome is off (Def viewport / layout settling).
            // Sticky-on + ≤0.5 = collapsed Mask; inventing binds rows that clip forever (Tags(N), black list).
            bool stickyOn = (leftUserTagsAvailStickyGO != null && leftUserTagsAvailStickyGO.activeSelf)
                || (rightUserTagsAvailStickyGO != null && rightUserTagsAvailStickyGO.activeSelf);
            if (viewportH <= 0.5f && !stickyOn)
                viewportH = rowH * 10f;
            return viewportH;
        }

        private void InvalidateUserTagVirtWindowGate(bool isLeft)
        {
            if (isLeft)
            {
                _lastUserTagVirtFirstIdxLeft = int.MinValue;
                _lastUserTagVirtVisibleLeft = -1;
                _lastUserTagVirtTotalLeft = -1;
            }
            else
            {
                _lastUserTagVirtFirstIdxRight = int.MinValue;
                _lastUserTagVirtVisibleRight = -1;
                _lastUserTagVirtTotalRight = -1;
            }
        }

        private void RequestUserTagVirtLayoutRefresh(bool isLeft, Transform tabContainer, bool preserveScroll)
        {
            if (!IsUserTagsSideTabOpen(isLeft)) return;
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return;
            StopCo(ref _userTagVirtLayoutCo);
            float offsetPx = preserveScroll ? (TryGetUserTagAvailScrollOffsetPx(isLeft) ?? 0f) : 0f;
            bool resetTop = !preserveScroll;
            _userTagVirtLayoutCo = StartCoroutine(CoUserTagVirtLayoutRefresh(isLeft, tabContainer, resetTop, offsetPx));
        }

        /// <summary>
        /// After collapse/expand, scene load, or cold layout: sticky Mask can stay height≈0 for a few frames
        /// (invent disabled while sticky on → empty tag list). Mode F↔T workaround only forced this path.
        /// Preserve Tag/Filter work mode — refresh layout + virt bind only.
        /// </summary>
        private void RequestUserTagAvailVirtRecoverAfterLayout()
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy) return;
            bool leftOpen = IsUserTagsSideTabOpen(true) && leftTabContainerGO != null;
            bool rightOpen = IsUserTagsSideTabOpen(false) && rightTabContainerGO != null;
            if (!leftOpen && !rightOpen) return;
            StopCo(ref _userTagVirtLayoutCo);
            _userTagVirtLayoutCo = StartCoroutine(CoUserTagAvailVirtRecoverAfterLayout(leftOpen, rightOpen));
        }

        private IEnumerator CoUserTagAvailVirtRecoverAfterLayout(bool leftOpen, bool rightOpen)
        {
            // Scene-load collapse / first open: subtree or viewport may be inactive for several frames.
            const int maxAttempts = 4;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                yield return null;
                if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
                {
                    _userTagVirtLayoutCo = null;
                    yield break;
                }
                leftOpen = IsUserTagsSideTabOpen(true) && leftTabContainerGO != null;
                rightOpen = IsUserTagsSideTabOpen(false) && rightTabContainerGO != null;
                if (!leftOpen && !rightOpen)
                {
                    _userTagVirtLayoutCo = null;
                    yield break;
                }

                bool anyCollapsedMask = false;
                try
                {
                    ApplyUserTagsStickyScrollChrome(TabScrollTopOffset());
                    Canvas.ForceUpdateCanvases();
                    if (leftOpen)
                    {
                        SnapshotUserTagAvailScrollForPreserve(true);
                        InvalidateUserTagVirtWindowGate(true);
                        UpdateUserTagVirtualVisible(true, UserTagStateOnColor, leftTabContainerGO.transform);
                        RestorePreservedUserTagAvailScroll();
                        if (IsUserTagAvailViewportCollapsed(true)) anyCollapsedMask = true;
                    }
                    if (rightOpen)
                    {
                        SnapshotUserTagAvailScrollForPreserve(false);
                        InvalidateUserTagVirtWindowGate(false);
                        UpdateUserTagVirtualVisible(false, UserTagStateOnColor, rightTabContainerGO.transform);
                        RestorePreservedUserTagAvailScroll();
                        if (IsUserTagAvailViewportCollapsed(false)) anyCollapsedMask = true;
                    }
                }
                catch { }

                if (!anyCollapsedMask) break;
            }
            _userTagVirtLayoutCo = null;
        }

        private bool IsUserTagAvailViewportCollapsed(bool isLeft)
        {
            try
            {
                ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
                if (sr == null) return true;
                RectTransform viewport = sr.viewport != null ? sr.viewport : (sr.transform as RectTransform);
                float rowH = SideTabVirtRowStridePx();
                if (rowH <= 1f) rowH = 37f;
                return MeasureUserTagVirtViewportHeight(viewport, rowH) <= 0.5f;
            }
            catch { return true; }
        }

        private IEnumerator CoUserTagVirtLayoutRefresh(bool isLeft, Transform tabContainer, bool resetScrollToTop, float preserveOffsetPx)
        {
            const int maxAttempts = 4;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                yield return null;
                if (!IsUserTagsSideTabOpen(isLeft))
                {
                    _userTagVirtLayoutCo = null;
                    yield break;
                }
                try
                {
                    // Sticky first so Mask height is final before virt window measure/bind.
                    ApplyUserTagsStickyScrollChrome(TabScrollTopOffset());
                    Canvas.ForceUpdateCanvases();
                    InvalidateUserTagVirtWindowGate(isLeft);
                    if (attempt == 0)
                    {
                        if (resetScrollToTop)
                        {
                            ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
                            if (sr != null) sr.verticalNormalizedPosition = 1f;
                        }
                        else
                            ApplyUserTagAvailScrollOffsetPx(isLeft, preserveOffsetPx);
                    }
                    if (tabContainer != null)
                        UpdateUserTagVirtualVisible(isLeft, UserTagStateOnColor, tabContainer);
                }
                catch { }

                if (!IsUserTagAvailViewportCollapsed(isLeft)) break;
            }
            _userTagVirtLayoutCo = null;
        }

        private const char UserTagPinnedOrderLegacySep = '\x1e';
        private const string UserTagPinBtnName = "VPB_UserTagPinBtn";
        private const string UserTagAppliedRemoveBtnName = "VPB_UserTagAppliedRemoveBtn";
        private Sprite _userTagAppliedRemoveSprite;

        private static Image AddUserTagSideChromeRoundedBg(GameObject go, Color color)
        {
            return UI.AddGalleryElementRoundedBg(go, color);
        }

        private static void EnsureUserTagSideChromeRoundedBg(GameObject go, Color? colorOverride = null)
        {
            if (go == null) return;
            RoundedRect rr = go.GetComponent<RoundedRect>();
            if (rr == null)
            {
                Image legacy = go.GetComponent<Image>();
                if (legacy == null) return;
                Color c = colorOverride ?? legacy.color;
                bool ray = legacy.raycastTarget;
                UnityEngine.Object.Destroy(legacy);
                rr = go.AddComponent<RoundedRect>();
                rr.color = c;
                rr.raycastTarget = ray;
            }
            else if (colorOverride.HasValue)
            {
                rr.color = colorOverride.Value;
            }
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            Button btn = go.GetComponent<Button>();
            if (btn != null) btn.targetGraphic = rr;
            UIHoverBorder hb = go.GetComponent<UIHoverBorder>();
            if (hb != null)
            {
                try { hb.ApplyBorderSettings(); } catch { }
            }
        }

        private static void SyncUserTagSideChromeRoundedBg(GameObject go)
        {
            EnsureUserTagSideChromeRoundedBg(go, null);
        }

        private static bool UserTagSideChromeButtonNeedsRoundedUpgrade(GameObject go)
        {
            if (go == null) return false;
            Image img = go.GetComponent<Image>();
            return img != null && !(img is RoundedRect);
        }

        private static void ParseUserTagPinnedOrderSpec(string spec, List<string> dest)
        {
            if (dest == null) return;
            dest.Clear();
            if (string.IsNullOrEmpty(spec)) return;

            string[] parts;
            if (spec.IndexOf(UserTagPinnedOrderLegacySep) >= 0)
                parts = spec.Split(UserTagPinnedOrderLegacySep);
            else
                parts = spec.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                string norm = VpbLocalDatabase.NormalizeGalleryUserTagName(parts[i]);
                if (string.IsNullOrEmpty(norm)) continue;
                bool dup = false;
                for (int j = 0; j < dest.Count; j++)
                {
                    if (string.Equals(dest[j], norm, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (!dup) dest.Add(norm);
            }
        }

        private static string EncodeUserTagPinOrder(IList<string> order)
        {
            if (order == null || order.Count == 0) return "";
            var sb = new StringBuilder(order.Count * 12);
            for (int i = 0; i < order.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(order[i]);
            }
            return sb.ToString();
        }

        private void EnsureUserTagPinOrderRuntimeLoaded()
        {
            if (_userTagPinOrderRuntimeLoaded) return;
            _userTagPinOrderRuntimeLoaded = true;
            _userTagPinOrderRuntime.Clear();
            if (VPBConfig.Instance != null)
                ParseUserTagPinnedOrderSpec(VPBConfig.Instance.GalleryUserTagPinnedOrder, _userTagPinOrderRuntime);
            try { PruneStaleUserTagPinsInRuntimeList(false); } catch { }
        }

        /// <summary>Drop pins for tags removed from DB. Never calls <see cref="CacheUserTagsSideTab"/> (avoids load-time freeze).</summary>
        private bool PruneStaleUserTagPinsInRuntimeList(bool persistIfChanged)
        {
            if (_userTagPinOrderRuntime.Count == 0) return false;
            if (!VpbSqlite3.IsAvailable) return false;

            var allNames = new List<string>(128);
            if (!VpbLocalDatabase.TryReadAllGalleryUserTagNames(allNames) || allNames.Count == 0)
                return false;

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allNames.Count; i++)
            {
                string n = VpbLocalDatabase.NormalizeGalleryUserTagName(allNames[i]);
                if (!string.IsNullOrEmpty(n)) known.Add(n);
            }
            if (known.Count == 0) return false;

            bool changed = false;
            for (int i = _userTagPinOrderRuntime.Count - 1; i >= 0; i--)
            {
                if (!known.Contains(_userTagPinOrderRuntime[i]))
                {
                    _userTagPinOrderRuntime.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed && persistIfChanged)
                PersistUserTagPinOrderToConfig(false);
            return changed;
        }

        private void PersistUserTagPinOrderToConfig(bool immediate)
        {
            if (VPBConfig.Instance == null) return;
            VPBConfig.Instance.GalleryUserTagPinnedOrder = EncodeUserTagPinOrder(_userTagPinOrderRuntime);
            if (immediate)
            {
                StopCo(ref _userTagPinSaveCo);
                try { VPBConfig.Instance.Save(false); } catch { }
                return;
            }
            ScheduleUserTagPinOrderSave();
        }

        private void ScheduleUserTagPinOrderSave()
        {
            if (!isActiveAndEnabled) return;
            StopCo(ref _userTagPinSaveCo);
            _userTagPinSaveCo = StartCoroutine(CoDeferredUserTagPinOrderSave());
        }

        private IEnumerator CoDeferredUserTagPinOrderSave()
        {
            yield return new WaitForSeconds(0.35f);
            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.Save(false);
            }
            catch { }
            _userTagPinSaveCo = null;
        }

        private bool IsUserTagPinned(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return false;
            string norm = VpbLocalDatabase.NormalizeGalleryUserTagName(tagName);
            if (string.IsNullOrEmpty(norm)) return false;
            EnsureUserTagPinOrderRuntimeLoaded();
            for (int i = 0; i < _userTagPinOrderRuntime.Count; i++)
            {
                if (string.Equals(_userTagPinOrderRuntime[i], norm, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void ToggleUserTagPin(string tagName, bool isLeft)
        {
            string norm = VpbLocalDatabase.NormalizeGalleryUserTagName(tagName);
            if (string.IsNullOrEmpty(norm)) return;
            EnsureUserTagPinOrderRuntimeLoaded();
            int found = -1;
            for (int i = 0; i < _userTagPinOrderRuntime.Count; i++)
            {
                if (string.Equals(_userTagPinOrderRuntime[i], norm, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
            }
            if (found >= 0) _userTagPinOrderRuntime.RemoveAt(found);
            else _userTagPinOrderRuntime.Add(norm);
            PersistUserTagPinOrderToConfig(true);
            unchecked { _userTagPinRevision++; }
            _userTagVirtViewSig = null;
            try { RefreshUserTagsAvailPaneInPlace(isLeft); } catch { }
        }

        private void PartitionUserTagRowsPinnedFirst(List<UserTagSideTabEntry> rows, List<UserTagSideTabEntry> pinnedOut, List<UserTagSideTabEntry> normalOut)
        {
            if (pinnedOut != null) pinnedOut.Clear();
            if (normalOut != null) normalOut.Clear();
            if (rows == null || rows.Count == 0) return;
            EnsureUserTagPinOrderRuntimeLoaded();
            List<string> pinOrder = _userTagPinOrderRuntime;
            if (pinOrder.Count == 0)
            {
                if (normalOut != null) normalOut.AddRange(rows);
                return;
            }
            var pinIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pinOrder.Count; i++) pinIndex[pinOrder[i]] = i;
            for (int ri = 0; ri < rows.Count; ri++)
            {
                UserTagSideTabEntry e = rows[ri];
                if (string.IsNullOrEmpty(e.Name)) continue;
                int idx;
                if (pinIndex.TryGetValue(e.Name, out idx))
                {
                    if (pinnedOut != null) pinnedOut.Add(e);
                }
                else if (normalOut != null)
                    normalOut.Add(e);
            }
            if (pinnedOut != null && pinnedOut.Count > 1)
            {
                pinnedOut.Sort((a, b) =>
                {
                    int ia, ib;
                    pinIndex.TryGetValue(a.Name, out ia);
                    pinIndex.TryGetValue(b.Name, out ib);
                    return ia.CompareTo(ib);
                });
            }
        }

        private void EnsureUserTagPinSprites()
        {
            if (_userTagPinOnSprite == null)
                _userTagPinOnSprite = UI.LoadIconSprite("vpb_icons/pin_on.png", new Color(0.78f, 0.78f, 0.78f, 1f));
            if (_userTagPinOffSprite == null)
                _userTagPinOffSprite = UI.LoadIconSprite("vpb_icons/pin_off.png", new Color(0.78f, 0.78f, 0.78f, 1f));
        }

        private void EnsureUserTagAppliedRemoveSprite()
        {
            if (_userTagAppliedRemoveSprite == null)
                _userTagAppliedRemoveSprite = UI.LoadIconSprite("vpb_icons/list_remove.png", Color.white);
            if (_userTagAppliedRemoveSprite == null)
                _userTagAppliedRemoveSprite = UI.LoadIconSprite("vpb_icons/delete.png", Color.white);
        }

        private bool ShouldShowUserTagRemoveForRow(bool appliedRow, UserTagSelectionState availSelectionState = UserTagSelectionState.Off)
        {
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged) return false;
            if (selectedFiles == null || selectedFiles.Count == 0) return false;
            if (appliedRow) return true;
            return availSelectionState == UserTagSelectionState.On
                || availSelectionState == UserTagSelectionState.Mixed;
        }

        private void SyncUserTagRowRemoveButton(GameObject rowGo, string tagName, float scale)
        {
            if (rowGo == null) return;
            Transform existing = rowGo.transform.Find(UserTagAppliedRemoveBtnName);
            GameObject removeGo = existing != null ? existing.gameObject : null;
            if (removeGo != null && UserTagSideChromeButtonNeedsRoundedUpgrade(removeGo))
            {
                UnityEngine.Object.Destroy(removeGo);
                removeGo = null;
            }

            if (removeGo == null)
            {
                removeGo = new GameObject(UserTagAppliedRemoveBtnName);
                removeGo.transform.SetParent(rowGo.transform, false);
                Image bg = AddUserTagSideChromeRoundedBg(removeGo, new Color(0.62f, 0.14f, 0.14f, 1f));
                Button btn = removeGo.AddComponent<Button>();
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                btn.colors = cb;
                UI.ConfigButtonFlat(btn);
                btn.targetGraphic = bg;
                removeGo.AddComponent<UIHoverBorder>();
            }

            string norm = VpbLocalDatabase.NormalizeGalleryUserTagName(tagName);
            if (string.IsNullOrEmpty(norm))
            {
                removeGo.SetActive(false);
                return;
            }

            EnsureUserTagAppliedRemoveSprite();

            Image bgImg = removeGo.GetComponent<Image>();
            if (bgImg != null) bgImg.color = new Color(0.62f, 0.14f, 0.14f, 1f);

            Button removeBtn = removeGo.GetComponent<Button>();
            if (removeBtn != null)
            {
                removeBtn.onClick.RemoveAllListeners();
                string snap = norm;
                removeBtn.onClick.AddListener(() => RemoveSingleAppliedUserTagFromSelection(snap));
            }

            Image iconImg = removeGo.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImg == null && _userTagAppliedRemoveSprite != null)
            {
                // Pass the red backdrop (not white): AddIconToButton overwrites the button background
                // with its 4th arg, so Color.white here flashed the button white on the frame the icon
                // was first created (corrected only on a later refresh once the icon child exists).
                UI.AddIconToButton(removeGo, _userTagAppliedRemoveSprite, 6f, bgImg != null ? bgImg.color : new Color(0.62f, 0.14f, 0.14f, 1f));
                iconImg = removeGo.transform.Find("Icon")?.GetComponent<Image>();
            }
            if (iconImg != null)
            {
                if (_userTagAppliedRemoveSprite != null) iconImg.sprite = _userTagAppliedRemoveSprite;
                iconImg.color = Color.white;
                iconImg.raycastTarget = false;
            }

            float edge = Mathf.Clamp(26f * scale, 20f, 36f);
            RectTransform rt = removeGo.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(edge, edge);
                rt.anchoredPosition = new Vector2(6f * scale, 0f);
            }
            LayoutElement remLe = removeGo.GetComponent<LayoutElement>();
            if (remLe == null) remLe = removeGo.AddComponent<LayoutElement>();
            remLe.ignoreLayout = true;
            removeGo.SetActive(true);
            removeGo.transform.SetAsLastSibling();
            SyncUserTagSideChromeRoundedBg(removeGo);

            AddTooltipPlain(removeGo, VPBTranslation.T("gallery.usertags.remove_applied_row_tip", "Remove this tag from selected item(s)."));
        }

        private void HideUserTagRowRemoveButton(GameObject rowGo)
        {
            if (rowGo == null) return;
            Transform existing = rowGo.transform.Find(UserTagAppliedRemoveBtnName);
            if (existing != null) existing.gameObject.SetActive(false);
        }

        private void SyncUserTagRowPinButton(GameObject rowGo, string tagName, bool hide, float scale, bool isLeft, bool appliedRow = false, UserTagSelectionState availSelectionState = UserTagSelectionState.Off)
        {
            if (rowGo == null) return;

            if (ShouldShowUserTagRemoveForRow(appliedRow, availSelectionState))
            {
                Transform pinHide = rowGo.transform.Find(UserTagPinBtnName);
                if (pinHide != null) pinHide.gameObject.SetActive(false);
                SyncUserTagRowRemoveButton(rowGo, tagName, scale);
                return;
            }

            HideUserTagRowRemoveButton(rowGo);
            Transform existing = rowGo.transform.Find(UserTagPinBtnName);
            GameObject pinGo = existing != null ? existing.gameObject : null;
            if (hide)
            {
                if (pinGo != null) pinGo.SetActive(false);
                return;
            }

            string norm = VpbLocalDatabase.NormalizeGalleryUserTagName(tagName);
            if (string.IsNullOrEmpty(norm)) { if (pinGo != null) pinGo.SetActive(false); return; }

            bool pinned = IsUserTagPinned(norm);
            EnsureUserTagPinSprites();

            if (pinGo != null && UserTagSideChromeButtonNeedsRoundedUpgrade(pinGo))
            {
                UnityEngine.Object.Destroy(pinGo);
                pinGo = null;
            }

            if (pinGo == null)
            {
                pinGo = new GameObject(UserTagPinBtnName);
                pinGo.transform.SetParent(rowGo.transform, false);
                Image bg = AddUserTagSideChromeRoundedBg(pinGo, new Color(0.14f, 0.14f, 0.16f, 0.92f));
                Button btn = pinGo.AddComponent<Button>();
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                btn.colors = cb;
                UI.ConfigButtonFlat(btn);
                btn.targetGraphic = bg;
                pinGo.AddComponent<UIHoverBorder>();
            }

            Button pinBtn = pinGo.GetComponent<Button>();
            if (pinBtn != null)
            {
                pinBtn.onClick.RemoveAllListeners();
                string snap = norm;
                bool sideLeft = isLeft;
                pinBtn.onClick.AddListener(() => ToggleUserTagPin(snap, sideLeft));
            }

            Image iconImg = pinGo.transform.Find("Icon")?.GetComponent<Image>();
            Sprite spr = pinned ? _userTagPinOffSprite : _userTagPinOnSprite;
            if (spr == null) spr = pinned ? _userTagPinOnSprite : _userTagPinOffSprite;
            if (iconImg == null && spr != null)
            {
                UI.AddIconToButton(pinGo, spr, 6f, pinGo.GetComponent<Image>() != null ? pinGo.GetComponent<Image>().color : Color.white);
                iconImg = pinGo.transform.Find("Icon")?.GetComponent<Image>();
            }
            if (iconImg != null && spr != null) iconImg.sprite = spr;

            float edge = Mathf.Clamp(26f * scale, 20f, 36f);
            RectTransform rt = pinGo.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(edge, edge);
                rt.anchoredPosition = new Vector2(6f * scale, 0f);
            }
            LayoutElement pinLe = pinGo.GetComponent<LayoutElement>();
            if (pinLe == null) pinLe = pinGo.AddComponent<LayoutElement>();
            pinLe.ignoreLayout = true;
            pinGo.SetActive(true);
            pinGo.transform.SetAsLastSibling();
            SyncUserTagSideChromeRoundedBg(pinGo);

            string tipKey = pinned ? "gallery.usertags.pin_off_tip" : "gallery.usertags.pin_on_tip";
            string tipDefault = pinned ? "Unpin — return tag to sorted position." : "Pin — keep tag at top of list.";
            AddTooltipPlain(pinGo, VPBTranslation.T(tipKey, tipDefault));
        }

        internal string BuildUserTagDragDropStatusHint(FileEntry galleryRowHit, List<string> tags)
        {
            if (tags == null || tags.Count == 0) return "";
            string phrase = FormatUserTagPhraseForHover(tags);
            if (galleryRowHit == null || galleryRowHit is InternalSettingRowEntry)
                return "";

            return string.Format(
                VPBTranslation.T("gallery.usertags.drag_hover.tag_one_item", "Tag this item with {0} tag"),
                phrase);
        }

        private static string FormatUserTagPhraseForHover(List<string> tags)
        {
            if (tags == null || tags.Count == 0) return "";
            if (tags.Count == 1) return "\"" + tags[0] + "\"";
            if (tags.Count == 2) return "\"" + tags[0] + "\", \"" + tags[1] + "\"";
            return "\"" + tags[0] + "\" +" + (tags.Count - 1);
        }

        /// <summary>
        /// Floating quick-tagger / apply-remove zones sit above the grid.
        /// RaycastAll still returns gallery rows behind — treat these as occluders.
        /// </summary>
        internal bool IsUserTagDropOccluder(GameObject go)
        {
            if (go == null) return false;
            if (_detailStripTagMenuPanelGO != null
                && _detailStripTagMenuRoot != null
                && _detailStripTagMenuRoot.activeSelf
                && (go == _detailStripTagMenuPanelGO
                    || go.transform.IsChildOf(_detailStripTagMenuPanelGO.transform)))
                return true;
            if (go.GetComponentInParent<UserTagApplyDropZone>() != null) return true;
            if (go.GetComponentInParent<UserTagRemoveDropZone>() != null) return true;
            return false;
        }

        /// <summary>Shared by tag-drag hover hint and drop: first gallery file row hit (this panel).</summary>
        internal static bool TryResolveGalleryRowFromRaycastHits(GalleryPanel panel, IList<RaycastResult> hits, out FileEntry file)
        {
            file = null;
            if (panel == null || hits == null) return false;
            // Hits are front→back. Occluder in front of gallery = no pierce-through.
            for (int i = 0; i < hits.Count; i++)
            {
                GameObject go = hits[i].gameObject;
                if (go == null) continue;
                if (panel.IsUserTagDropOccluder(go))
                    return false;
                UIFileEntryLeftReleaseSelect lr = go.GetComponentInParent<UIFileEntryLeftReleaseSelect>();
                if (lr == null || lr.Panel != panel || lr.File == null) continue;
                if (lr.File is InternalSettingRowEntry) continue;
                file = lr.File;
                return true;
            }
            return false;
        }

        private void ApplyUserTagsToFileEntries(List<string> tags, List<FileEntry> targets, bool remove)
        {
            if (tags == null || tags.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.no_tags", "No tags parsed."), 1.5f);
                return;
            }

            List<FileEntry> list = NormalizeUserTagMutationTargets(targets);
            if (list.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            if (!string.IsNullOrEmpty(cat)
                && VpbLocalDatabase.IsGalleryAllVarPseudoCategory(cat)
                && _userTagInheritVarToChildren)
            {
                StartCoroutine(ApplyTagsToSelectedPackagesAllVarInheritCoroutine(new List<string>(tags), remove, list));
                return;
            }
            StartCoroutine(ApplyTagsToSelectedPackagesBulkCoroutine(new List<string>(tags), remove, list));
        }

        private static List<FileEntry> NormalizeUserTagMutationTargets(List<FileEntry> targets)
        {
            var list = new List<FileEntry>();
            if (targets == null) return list;
            for (int i = 0; i < targets.Count; i++)
            {
                FileEntry fe = targets[i];
                if (fe == null || fe is InternalSettingRowEntry) continue;
                list.Add(fe);
            }
            return list;
        }

        internal void UserTagAppliedDragBeginPayload(string primaryTag, List<string> tagsOut)
        {
            if (tagsOut == null) return;
            tagsOut.Clear();
            if (string.IsNullOrEmpty(primaryTag)) return;
            if (userTagAppliedRemoveSelection != null && userTagAppliedRemoveSelection.Count > 0
                && userTagAppliedRemoveSelection.Contains(primaryTag))
            {
                foreach (string t in userTagAppliedRemoveSelection)
                    tagsOut.Add(t);
            }
            else
                tagsOut.Add(primaryTag);
        }

        internal void UserTagRemoveDroppedTags(List<string> tags)
        {
            if (tags == null || tags.Count == 0) return;
            ApplyUserTagsToFileEntries(new List<string>(tags), selectedFiles, remove: true);
            userTagAppliedRemoveSelection.Clear();
            userTagAppliedRemoveAnchor = null;
        }

        private void PruneUserTagAppliedRemoveSelectionToCache()
        {
            if (cachedAppliedUserTagsSelection == null || cachedAppliedUserTagsSelection.Count == 0)
            {
                userTagAppliedRemoveSelection.Clear();
                userTagAppliedRemoveAnchor = null;
                return;
            }
            var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < cachedAppliedUserTagsSelection.Count; i++)
            {
                string n = cachedAppliedUserTagsSelection[i].Name;
                if (!string.IsNullOrEmpty(n)) valid.Add(n);
            }
            var stale = new List<string>();
            foreach (string t in userTagAppliedRemoveSelection)
            {
                if (!valid.Contains(t)) stale.Add(t);
            }
            for (int si = 0; si < stale.Count; si++)
                userTagAppliedRemoveSelection.Remove(stale[si]);
            if (!string.IsNullOrEmpty(userTagAppliedRemoveAnchor) && !valid.Contains(userTagAppliedRemoveAnchor))
                userTagAppliedRemoveAnchor = null;
        }

        private void OnAppliedUserTagRowClicked(int visibleIndex, List<UserTagSideTabEntry> visibleOrdered, string tagName)
        {
            if (visibleOrdered == null || string.IsNullOrEmpty(tagName)) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shift && visibleOrdered.Count > 0)
            {
                int anchorIdx = -1;
                if (!string.IsNullOrEmpty(userTagAppliedRemoveAnchor))
                {
                    for (int i = 0; i < visibleOrdered.Count; i++)
                    {
                        if (string.Equals(visibleOrdered[i].Name, userTagAppliedRemoveAnchor, StringComparison.OrdinalIgnoreCase))
                        {
                            anchorIdx = i;
                            break;
                        }
                    }
                }
                int curIdx = Mathf.Clamp(visibleIndex, 0, visibleOrdered.Count - 1);
                if (anchorIdx < 0)
                {
                    userTagAppliedRemoveSelection.Clear();
                    userTagAppliedRemoveSelection.Add(tagName);
                    userTagAppliedRemoveAnchor = tagName;
                    try { UpdateTabs(); } catch { }
                    return;
                }
                int lo = Mathf.Min(anchorIdx, curIdx);
                int hi = Mathf.Max(anchorIdx, curIdx);
                userTagAppliedRemoveSelection.Clear();
                for (int i = lo; i <= hi; i++)
                    userTagAppliedRemoveSelection.Add(visibleOrdered[i].Name);
                try { UpdateTabs(); } catch { }
                return;
            }

            if (ctrl)
            {
                if (userTagAppliedRemoveSelection.Contains(tagName))
                    userTagAppliedRemoveSelection.Remove(tagName);
                else
                    userTagAppliedRemoveSelection.Add(tagName);
                userTagAppliedRemoveAnchor = tagName;
                try { UpdateTabs(); } catch { }
                return;
            }

            if (userTagAppliedRemoveSelection.Count == 1 && userTagAppliedRemoveSelection.Contains(tagName))
            {
                userTagAppliedRemoveSelection.Clear();
                userTagAppliedRemoveAnchor = null;
            }
            else
            {
                userTagAppliedRemoveSelection.Clear();
                userTagAppliedRemoveSelection.Add(tagName);
                userTagAppliedRemoveAnchor = tagName;
            }
            try { UpdateTabs(); } catch { }
        }

        private void RemoveFocusedAppliedUserTagFromSelection()
        {
            if (userTagAppliedRemoveSelection == null || userTagAppliedRemoveSelection.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.pick_applied_first", "Select one or more tags in the list below (click rows; Ctrl+click / Shift+range)."), 2.2f);
                return;
            }
            var tags = new List<string>(userTagAppliedRemoveSelection);
            ApplyTagsToSelectedPackages(tags, remove: true);
            userTagAppliedRemoveSelection.Clear();
            userTagAppliedRemoveAnchor = null;
        }

        private void RemoveSingleAppliedUserTagFromSelection(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return;
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }
            userTagAppliedRemoveSelection.Clear();
            userTagAppliedRemoveAnchor = null;
            ApplyTagsToSelectedPackages(new List<string> { tagName }, remove: true);
        }

        private void ApplyActiveFilterUserTagsToSelection()
        {
            if (activeUserTags == null || activeUserTags.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.no_checked_tags", "Check tags in the upper list first (highlighted rows)."), 2f);
                return;
            }
            ApplyTagsToSelectedPackages(new List<string>(activeUserTags), remove: false);
        }

        private void ApplyTagsToSelectedPackages(List<string> tags, bool remove)
        {
            ApplyUserTagsToFileEntries(tags, selectedFiles, remove);
        }

        private static void RemoveFileEntriesFromLists(List<FileEntry> list, HashSet<FileEntry> removeRefs)
        {
            if (list == null || removeRefs == null || removeRefs.Count == 0) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (removeRefs.Contains(list[i]))
                    list.RemoveAt(i);
            }
        }

        /// <summary>
        /// After SQLite tag remove while Available filter mode narrows grid: drop visible rows that no longer match AND tag filter (skip full <see cref="RefreshFiles"/>).
        /// </summary>
        private bool TryPruneVisibleGridAfterUserTagRemove(List<VpbLocalDatabase.GalleryUserTagRowKey> updatedRows)
        {
            if (updatedRows == null || updatedRows.Count == 0) return false;
            if (!IsUserTagIncludeFilterArmed() || activeUserTags == null || activeUserTags.Count == 0)
                return false;
            if (activeContentType != ContentType.Category || !VpbSqlite3.IsAvailable)
                return false;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0)
                return false;

            string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
            if (string.IsNullOrEmpty(cat)) return false;

            var updatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < updatedRows.Count; i++)
            {
                var r = updatedRows[i];
                updatedKeys.Add((r.PkgUid ?? "") + "\n" + (r.InternalPath ?? ""));
            }

            var removeRefs = new HashSet<FileEntry>();
            for (int i = 0; i < currentFilteredFiles.Count; i++)
            {
                FileEntry fe = currentFilteredFiles[i];
                if (fe == null) continue;
                if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out string ip)) continue;
                string k = (pkg ?? "") + "\n" + (ip ?? "");
                if (!updatedKeys.Contains(k)) continue;
                if (!VpbLocalDatabase.TryGalleryRowMatchesUserTags(cat, pkg, ip, activeUserTags, UserTagFilterRequiresAllTags()))
                    removeRefs.Add(fe);
            }

            if (removeRefs.Count == 0) return false;

            RemoveFileEntriesFromLists(currentFilteredFiles, removeRefs);
            RemoveFileEntriesFromLists(lastFilteredFiles, removeRefs);
            InvalidateGalleryPreHideFileListSnapshot();
            RemoveFileEntriesFromLists(topSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(filterSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(selectedFiles, removeRefs);
            return true;
        }

        private bool TryGetSelectionKeyForGalleryUserTagRow(string pkgUid, string internalPath, out string selKey)
        {
            selKey = null;
            if (selectedFiles != null)
            {
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    FileEntry fe = selectedFiles[i];
                    if (fe == null) continue;
                    if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out string ip)) continue;
                    if (!string.Equals(pkg ?? "", pkgUid ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(ip ?? "", internalPath ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    selKey = GetSelectionIdentityKey(fe, false);
                    return !string.IsNullOrEmpty(selKey);
                }
            }
            if (currentFilteredFiles != null)
            {
                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    FileEntry fe = currentFilteredFiles[i];
                    if (fe == null) continue;
                    if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out string ip)) continue;
                    if (!string.Equals(pkg ?? "", pkgUid ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(ip ?? "", internalPath ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                    selKey = GetSelectionIdentityKey(fe, false);
                    return !string.IsNullOrEmpty(selKey);
                }
            }
            return false;
        }

        private void SyncUntaggedTaggedPinKeysAfterMutate(bool remove, List<VpbLocalDatabase.GalleryUserTagRowKey> updatedRows)
        {
            if (_userTagAvailMode != UserTagAvailMode.FilterUntagged || updatedRows == null || updatedRows.Count == 0)
                return;
            if (!VpbSqlite3.IsAvailable) return;

            string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "") ?? "";
            if (string.IsNullOrEmpty(cat)) return;

            for (int i = 0; i < updatedRows.Count; i++)
            {
                var r = updatedRows[i];
                if (!TryGetSelectionKeyForGalleryUserTagRow(r.PkgUid, r.InternalPath, out string selKey))
                    continue;
                if (!remove)
                {
                    _untaggedTaggedPinKeys.Add(selKey);
                    continue;
                }
                if (VpbLocalDatabase.TryGalleryRowHasNoUserTags(cat, r.PkgUid, r.InternalPath))
                    _untaggedTaggedPinKeys.Remove(selKey);
            }
        }

        /// <summary>Remove grid rows for deselected keys that were pinned as tagged overrides.</summary>
        private bool TryPruneUntaggedGridForDeselectedPins(HashSet<string> deselectedSelKeys)
        {
            if (deselectedSelKeys == null || deselectedSelKeys.Count == 0) return false;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return false;

            bool anyPin = false;
            foreach (string k in deselectedSelKeys)
            {
                if (_untaggedTaggedPinKeys.Contains(k)) { anyPin = true; break; }
            }
            if (!anyPin) return false;

            var removeRefs = new HashSet<FileEntry>();
            for (int i = 0; i < currentFilteredFiles.Count; i++)
            {
                FileEntry fe = currentFilteredFiles[i];
                if (fe == null) continue;
                string selKey = GetSelectionIdentityKey(fe, false);
                if (string.IsNullOrEmpty(selKey) || !deselectedSelKeys.Contains(selKey)) continue;
                if (!_untaggedTaggedPinKeys.Remove(selKey)) continue;
                removeRefs.Add(fe);
            }

            if (removeRefs.Count == 0) return false;

            RemoveFileEntriesFromLists(currentFilteredFiles, removeRefs);
            RemoveFileEntriesFromLists(lastFilteredFiles, removeRefs);
            InvalidateGalleryPreHideFileListSnapshot();
            RemoveFileEntriesFromLists(topSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(filterSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(selectedFiles, removeRefs);
            return true;
        }

        private void RefreshUiAfterUserTagMutate(bool remove, List<VpbLocalDatabase.GalleryUserTagRowKey> updatedRows, List<string> tags)
        {
            bool appearanceGenderLive = false;
            if (tags != null && tags.Count > 0)
            {
                try { appearanceGenderLive = TryHandleAppearanceGenderTagLiveUpdate(tags, remove, updatedRows); } catch { }
            }

            if (!appearanceGenderLive)
                InvalidateTags();
            else
            {
                userTagsCached = false;
                unchecked { userTagSideTabDataRevision++; }
            }

            CacheAppliedUserTagsForSelection();
            try { SyncUntaggedTaggedPinKeysAfterMutate(remove, updatedRows); } catch { }
            try { _detailStripCacheKey = ""; DetailStripRefresh(); } catch { }
            try { DetailStripRefreshTagMenuAfterMutation(); } catch { }

            bool filterModeRemove = remove && IsUserTagIncludeExcludeFilterArmed();

            if (filterModeRemove)
            {
                SyncGridAfterUserTagRemoveInFilterMode(updatedRows, tags);
                try { RefreshSelectionVisuals(); } catch { }
            }
            else
            {
                try { RefreshSelectionVisuals(); } catch { }
                try { RefreshVisibleUserTagRows(); } catch { }
                try
                {
                    if (leftActiveContent == ContentType.UserTags)
                        UpdateTabs(ContentType.UserTags, leftTabContainerGO, leftActiveTabButtons, true);
                }
                catch { }
                try
                {
                    if (rightActiveContent == ContentType.UserTags)
                        UpdateTabs(ContentType.UserTags, rightTabContainerGO, rightActiveTabButtons, false);
                }
                catch { }
            }

            if (appearanceGenderLive)
            {
                try { RebuildSubPaneSideTabListsOnly(); } catch { }
                try { UpdateTabs(); } catch { }
            }
        }

        private IEnumerator ApplyTagsToSelectedPackagesBulkCoroutine(List<string> tags, bool remove, List<FileEntry> targets)
        {
            if (tags == null || tags.Count == 0) yield break;
            if (targets == null || targets.Count == 0) yield break;

            string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            if (string.IsNullOrEmpty(cat)) yield break;

            var rows = new List<VpbLocalDatabase.GalleryUserTagRowKey>(targets.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                FileEntry fe = targets[i];
                if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out string ip)) continue;
                if (string.IsNullOrEmpty(pkg)) continue;
                string rk = (pkg ?? "") + "\n" + (ip ?? "");
                if (!seen.Add(rk)) continue;
                rows.Add(new VpbLocalDatabase.GalleryUserTagRowKey { Category = cat ?? "", PkgUid = pkg ?? "", InternalPath = ip ?? "" });
            }
            if (rows.Count == 0) yield break;

            int[] done = new int[1];
            int[] touchedOut = new int[1];
            int[] ok = new int[1];
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int touched;
                    bool success;
                    if (remove)
                        success = VpbLocalDatabase.TryRemoveGalleryUserTagsFromManyRows(rows, tags, out touched);
                    else
                        success = VpbLocalDatabase.TryAssignGalleryUserTagsToManyRows(rows, tags, out touched);
                    touchedOut[0] = touched;
                    ok[0] = success ? 1 : 0;
                }
                catch { ok[0] = 0; touchedOut[0] = 0; }
                finally { System.Threading.Interlocked.Exchange(ref done[0], 1); }
            });

            while (System.Threading.Interlocked.CompareExchange(ref done[0], 0, 0) == 0)
                yield return null;

            if (ok[0] == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.db_fail", "Update failed (database)."), 2.2f);
                yield break;
            }

            RefreshUiAfterUserTagMutate(remove, rows, tags);
            ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.usertags.done_count", "Updated {0} item(s)."), touchedOut[0]), 2f);
        }

        private IEnumerator ApplyTagsToSelectedPackagesAllVarInheritCoroutine(List<string> tags, bool remove, List<FileEntry> targets)
        {
            // Background DB work to avoid freezing UI when a package has many indexed rows.
            if (tags == null || tags.Count == 0) yield break;
            if (targets == null || targets.Count == 0) yield break;
            string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            if (string.IsNullOrEmpty(cat) || !VpbLocalDatabase.IsGalleryAllVarPseudoCategory(cat)) yield break;

            var pkgUids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                FileEntry fe = targets[i];
                if (!TryGetGalleryRowKeysForUserTags(fe, out string pkg, out _)) continue;
                if (!string.IsNullOrEmpty(pkg)) pkgUids.Add(pkg);
            }
            if (pkgUids.Count == 0) yield break;

            var rows = new List<VpbLocalDatabase.GalleryUserTagRowKey>(4096);
            foreach (var pu in pkgUids)
            {
                var catMem = new List<KeyValuePair<string, string>>(256);
                if (!VpbLocalDatabase.TryReadCatMemRowsForPackage(pu, catMem)) continue;
                for (int i = 0; i < catMem.Count; i++)
                {
                    var kv = catMem[i];
                    rows.Add(new VpbLocalDatabase.GalleryUserTagRowKey
                    {
                        Category = kv.Key ?? "",
                        PkgUid = pu ?? "",
                        InternalPath = kv.Value ?? ""
                    });
                }
            }
            if (rows.Count == 0) yield break;

            int[] done = new int[1];
            int[] touchedOut = new int[1];
            int[] ok = new int[1];
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int touched;
                    bool success;
                    if (remove)
                        success = VpbLocalDatabase.TryRemoveGalleryUserTagsFromManyRows(rows, tags, out touched);
                    else
                        success = VpbLocalDatabase.TryAssignGalleryUserTagsToManyRows(rows, tags, out touched);
                    touchedOut[0] = touched;
                    ok[0] = success ? 1 : 0;
                }
                catch { ok[0] = 0; touchedOut[0] = 0; }
                finally { System.Threading.Interlocked.Exchange(ref done[0], 1); }
            });

            while (System.Threading.Interlocked.CompareExchange(ref done[0], 0, 0) == 0)
                yield return null;

            if (ok[0] == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.db_fail", "Update failed (database)."), 2.2f);
                yield break;
            }

            RefreshUiAfterUserTagMutate(remove, rows, tags);
            ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.usertags.done_count", "Updated {0} item(s)."), touchedOut[0]), 2.2f);
        }

        private void EnsureUserTagSideTabBulkBlock(Transform scrollTabContainer, bool isLeft)
        {
            if (scrollTabContainer == null || backgroundBoxGO == null) return;
            Transform legacy = scrollTabContainer.Find("VPB_UserTagBulkBlock");
            if (legacy != null)
                UnityEngine.Object.Destroy(legacy.gameObject);
            Transform legacyV2 = scrollTabContainer.Find("VPB_UserTagBulkBlock_v2");
            if (legacyV2 != null)
                UnityEngine.Object.Destroy(legacyV2.gameObject);
            Transform strayV3 = scrollTabContainer.Find("VPB_UserTagBulkBlock_v3");
            if (strayV3 != null)
                UnityEngine.Object.Destroy(strayV3.gameObject);

            GameObject stickyGo = isLeft ? leftUserTagsAvailStickyGO : rightUserTagsAvailStickyGO;
            Transform bulkParent = stickyGo != null ? stickyGo.transform : scrollTabContainer;
            Transform existingBulkV3 = bulkParent.Find("VPB_UserTagBulkBlock_v3");
            if (existingBulkV3 != null)
            {
                EnsureUserTagUnifiedToolbar(existingBulkV3);
                // Toggle row moved to pinned footer; remove any legacy copy.
                try
                {
                    Transform legacyInherit = existingBulkV3.Find("VPB_UserTagInheritVarToggleRow_v1");
                    if (legacyInherit != null) UnityEngine.Object.Destroy(legacyInherit.gameObject);
                }
                catch { }
                return;
            }

            float s = ChromeScale;
            float u = s * 1.38f;

            GameObject root = new GameObject("VPB_UserTagBulkBlock_v3");
            root.transform.SetParent(bulkParent, false);
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0f, 1f);
            rootRT.anchorMax = new Vector2(1f, 1f);
            rootRT.pivot = new Vector2(0.5f, 1f);
            rootRT.sizeDelta = Vector2.zero;

            UI.AddVLG(root, 7f * s, UI.Pad(6f, 6f, 4f, 10f, s), TextAnchor.UpperCenter);

            LayoutElement rootLe = UI.AddLE(root, flexibleWidth: 1f);
            ContentSizeFitter rootCsf = root.AddComponent<ContentSizeFitter>();
            rootCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            int titleFs = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontTitleRef, s, GalleryUiDesignTokens.FontMinRef);
            float titleBand = Mathf.Max(30f * s, titleFs * 1.22f);

            GameObject titleRow = UI.CreateChildRT(root, "TagsTitleRow");
            UI.AddHLG(titleRow, 5f * s, childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: false);
            LayoutElement titleRowLe = UI.AddLE(titleRow, minHeight: titleBand, preferredHeight: titleBand, flexibleWidth: 1f);

            Text titleTxt = UI.CreateLabel(titleRow, FormatUserTagAvailTitle(0),
                titleFs, Color.white, TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, name: "BulkTitle");
            titleTxt.fontStyle = FontStyle.Bold;
            LayoutElement titleLe = UI.AddLE(titleTxt.gameObject, minHeight: titleBand, preferredHeight: titleBand, flexibleWidth: 1f);

            // Verb-first hint — replaces tribal F/T letter glyphs (recognition over recall).
            GameObject hintRow = UI.CreateChildRT(root, "TagsModeHintRow");
            UI.AddHLG(hintRow, 0f, childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: true);
            float hintH = Mathf.Max(22f * s, titleFs * 1.05f);
            LayoutElement hintRowLe = UI.AddLE(hintRow, minHeight: hintH, preferredHeight: hintH, flexibleWidth: 1f);
            Text hintTxt = UI.CreateLabel(hintRow, GetUserTagWorkModeHint(),
                GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef),
                new Color(0.78f, 0.84f, 0.92f, 1f), TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, name: "ModeHint");
            UI.AddLE(hintTxt.gameObject, minHeight: hintH, preferredHeight: hintH, flexibleWidth: 1f);

            // false: segment buttons share width; edit stays square.
            GameObject btnRow = UI.CreateChildRT(root, "BulkBtnRow");
            UI.AddHLG(btnRow, 6f * s, childForceExpandWidth: false);
            LayoutElement rowLe = UI.AddLE(btnRow, minHeight: 34f * s, preferredHeight: 36f * s, flexibleWidth: 1f);

            float editSq = 36f * s;
            Sprite editSpr = UI.LoadIconSprite("vpb_icons/edit.png", new Color(0.88f, 0.88f, 0.9f, 1f));
            Color editBackdrop = new Color(0.38f, 0.26f, 0.52f, 1f);
            GameObject editBtn = UI.CreateSideTabSquareIconButton(btnRow, editSq, editSpr, ShowUserTagListEditor, editBackdrop, 8f * s);
            editBtn.name = "VPB_UserTagEditBtn";
            AddTooltipPlain(editBtn, VPBTranslation.T("gallery.usertags.btn_edit_tooltip", "Open tag editor (Database mode): create / purge / merge / rename / categories / YAML. Same window as Set Tags."));
            if (isLeft) { leftUserTagAvailTitleText = titleTxt; leftUserTagApplyBtnText = null; }
            else { rightUserTagAvailTitleText = titleTxt; rightUserTagApplyBtnText = null; }

            EnsureUserTagUnifiedToolbar(root.transform);
        }

        private void EnsureUserTagInheritVarToChildrenButtonInFooter(Transform footerRoot)
        {
            if (footerRoot == null) return;

            float s = ChromeScale;
            float u = s * 1.38f;
            int font = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, u, GalleryUiDesignTokens.FontMinRef);

            Transform existing = footerRoot.Find("VPB_UserTagInheritVarToggleRow_v1");
            GameObject rowGo;
            if (existing != null)
            {
                rowGo = existing.gameObject;
                rowGo.SetActive(true);
            }
            else
            {
                rowGo = new GameObject("VPB_UserTagInheritVarToggleRow_v1");
                rowGo.transform.SetParent(footerRoot, false);
                RectTransform rrt = rowGo.AddComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0f, 0f);
                rrt.anchorMax = new Vector2(1f, 1f);
                rrt.pivot = new Vector2(0.5f, 0.5f);
                float pad = Mathf.RoundToInt(4f * s);
                rrt.offsetMin = new Vector2(pad, pad);
                rrt.offsetMax = new Vector2(-pad, -pad);
                LayoutElement le = UI.AddLE(rowGo, minHeight: Mathf.Max(28f, 34f * s), preferredHeight: Mathf.Max(28f, 34f * s), flexibleWidth: 1f);

                string tip = VPBTranslation.T(
                    "gallery.usertags.inherit_tags_tip",
                    "ALL VAR only.\n\nWhen on, applying/removing user tags on selected VAR package(s) will also apply/remove those tags on every indexed child item inside that VAR.\n\nUse carefully: may touch many items.");

                string labelOn = VPBTranslation.T("gallery.usertags.inherit_on", "Inherit ON");
                GameObject btnGo = UI.CreateUIButton(rowGo, 0f, le.preferredHeight, labelOn, font, 0f, 0f, AnchorPresets.stretchAll, OnUserTagInheritVarToChildrenBtnClicked);
                btnGo.name = "VPB_UserTagInheritVarToChildrenBtn";
                LayoutElement ble = btnGo.GetComponent<LayoutElement>();
                if (ble == null) ble = btnGo.AddComponent<LayoutElement>();
                ble.flexibleWidth = 1f;
                ble.minWidth = Mathf.Max(120f * s, 0f);

                WireUserTagInheritVarToChildrenBtnTooltip(btnGo);
            }

            // Ensure no row backdrop (avoid "gray box"). Button provides all visuals.
            try
            {
                Image bgExisting = rowGo.GetComponent<Image>();
                if (bgExisting != null) UnityEngine.Object.Destroy(bgExisting);
            }
            catch { }

            Transform bGo = rowGo.transform.Find("VPB_UserTagInheritVarToChildrenBtn");
            if (bGo != null)
            {
                Button b = bGo.GetComponent<Button>();
                if (b != null)
                {
                    b.onClick.RemoveListener(OnUserTagInheritVarToChildrenBtnClicked);
                    b.onClick.AddListener(OnUserTagInheritVarToChildrenBtnClicked);
                }
                WireUserTagInheritVarToChildrenBtnTooltip(bGo.gameObject);

                // Always fill row fully (no empty gray margins from older layout).
                RectTransform brt = bGo as RectTransform;
                if (brt != null)
                {
                    brt.anchorMin = Vector2.zero;
                    brt.anchorMax = Vector2.one;
                    brt.pivot = new Vector2(0.5f, 0.5f);
                    brt.anchoredPosition = Vector2.zero;
                    brt.sizeDelta = Vector2.zero;
                }
                LayoutElement ble2 = bGo.GetComponent<LayoutElement>();
                if (ble2 == null) ble2 = bGo.gameObject.AddComponent<LayoutElement>();
                ble2.flexibleWidth = 1f;

                SyncUserTagInheritVarToChildrenBtnVisual(bGo.gameObject);
            }
        }

        private string BuildUserTagInheritVarToChildrenTip()
        {
            string header = VPBTranslation.T("gallery.usertags.inherit_tip_header", "ALL VAR only.");
            string action = _userTagInheritVarToChildren
                ? VPBTranslation.T("gallery.usertags.inherit_tip_click_off", "Click: set Inherit OFF.")
                : VPBTranslation.T("gallery.usertags.inherit_tip_click_on", "Click: set Inherit ON.");
            string body = VPBTranslation.T(
                "gallery.usertags.inherit_tip_body",
                "When Inherit ON, applying/removing user tags on selected VAR package(s) also applies/removes those tags on every indexed child item inside that VAR.\n\nUse carefully: may touch many items.");
            return header + "\n" + action + "\n\n" + body;
        }

        private void WireUserTagInheritVarToChildrenBtnTooltip(GameObject btnGo)
        {
            if (btnGo == null) return;
            var del = btnGo.GetComponent<UIHoverDelegate>();
            if (del == null) del = btnGo.AddComponent<UIHoverDelegate>();

            // Replace any older tooltip handler with dynamic one (state-aware).
            del.OnHoverChange = (enter) =>
            {
                if (enter)
                    SetHoverTooltip(BuildUserTagInheritVarToChildrenTip(), btnGo);
                else
                    ClearHoverTooltip(btnGo);
            };
        }

        private void SyncUserTagInheritVarToChildrenBtnVisual(GameObject btnGo)
        {
            if (btnGo == null) return;
            float s = ChromeScale;
            Image img = btnGo.GetComponent<Image>();
            if (img != null)
            {
                // Strong, readable state colors.
                img.color = _userTagInheritVarToChildren
                    ? new Color(0.20f, 0.50f, 0.25f, 1f)   // ON: green
                    : new Color(0.22f, 0.28f, 0.36f, 1f);  // OFF: cool gray
            }

            Text t = btnGo.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = Color.white;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.resizeTextForBestFit = false;
                t.text = _userTagInheritVarToChildren
                    ? VPBTranslation.T("gallery.usertags.inherit_on", "Inherit ON")
                    : VPBTranslation.T("gallery.usertags.inherit_off", "Inherit OFF");
                t.alignment = TextAnchor.MiddleCenter;
                // Slight extra padding via text margins not available; keep size readable via font scaling already applied.
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.FontBodyRef, s * 1.38f, GalleryUiDesignTokens.FontMinRef);
            }
        }

        private void OnUserTagInheritVarToChildrenBtnClicked()
        {
            _userTagInheritVarToChildren = !_userTagInheritVarToChildren;
            try
            {
                string t = currentCategoryTitle ?? (titleText != null ? titleText.text : null) ?? "";
                string p = currentPath ?? "";
                if (!string.IsNullOrEmpty(t) || !string.IsNullOrEmpty(p))
                    SaveCurrentCategoryFilterState(t, p);
            }
            catch { }

            // Update both sides if footer visible on either.
            try
            {
                Transform l = leftUserTagsAvailFooterGO != null ? leftUserTagsAvailFooterGO.transform.Find("VPB_UserTagInheritVarToggleRow_v1/VPB_UserTagInheritVarToChildrenBtn") : null;
                if (l != null) SyncUserTagInheritVarToChildrenBtnVisual(l.gameObject);
            }
            catch { }
            try
            {
                Transform r = rightUserTagsAvailFooterGO != null ? rightUserTagsAvailFooterGO.transform.Find("VPB_UserTagInheritVarToggleRow_v1/VPB_UserTagInheritVarToChildrenBtn") : null;
                if (r != null) SyncUserTagInheritVarToChildrenBtnVisual(r.gameObject);
            }
            catch { }
        }

        private void EnsureUserTagUnifiedToolbar(Transform bulkBlockV3)
        {
            if (bulkBlockV3 == null) return;
            Transform legacyRow = bulkBlockV3.Find("VPB_UserTagFilterModeRow");
            if (legacyRow != null)
                UnityEngine.Object.Destroy(legacyRow.gameObject);

            // Legacy F/T letter minis + single cycle toggle — remove if present.
            Transform titleRow = bulkBlockV3.Find("TagsTitleRow");
            if (titleRow != null)
            {
                Transform legacyF = titleRow.Find("VPB_UserTagModeMiniBtn_" + UserTagAvailMode.FilterByTags);
                if (legacyF != null) UnityEngine.Object.Destroy(legacyF.gameObject);
                Transform legacyT = titleRow.Find("VPB_UserTagModeMiniBtn_" + UserTagAvailMode.Tag);
                if (legacyT != null) UnityEngine.Object.Destroy(legacyT.gameObject);
                Transform legacyN = titleRow.Find("VPB_UserTagModeMiniBtn_" + UserTagAvailMode.FilterUntagged);
                if (legacyN != null) UnityEngine.Object.Destroy(legacyN.gameObject);
            }

            EnsureUserTagWorkModeHintRow(bulkBlockV3);

            Transform btnRow = bulkBlockV3.Find("BulkBtnRow");
            if (btnRow == null) return;

            float s = ChromeScale;
            float sq = 36f * s;

            Transform legacyCycle = btnRow.Find("VPB_UserTagFilterModeBtn");
            if (legacyCycle != null) UnityEngine.Object.Destroy(legacyCycle.gameObject);
            Transform applyT = btnRow.Find("VPB_UserTagApplyBtn");
            if (applyT != null) UnityEngine.Object.Destroy(applyT.gameObject);

            if (btnRow.Find("VPB_UserTagEditBtn") == null)
            {
                Sprite editSpr = UI.LoadIconSprite("vpb_icons/edit.png", new Color(0.88f, 0.88f, 0.9f, 1f));
                GameObject editGo = UI.CreateSideTabSquareIconButton(btnRow.gameObject, sq, editSpr, ShowUserTagListEditor, new Color(0.38f, 0.26f, 0.52f, 1f), 8f * s);
                editGo.name = "VPB_UserTagEditBtn";
                AddTooltipPlain(editGo, VPBTranslation.T("gallery.usertags.btn_edit_tooltip", "Open tag editor (Database mode): create / purge / merge / rename / categories / YAML. Same window as Set Tags."));
            }

            EnsureUserTagWorkModeSegmentButton(
                btnRow,
                "VPB_UserTagWorkModeFilterBtn",
                UserTagAvailMode.FilterByTags,
                VPBTranslation.T("gallery.usertags.segment_filter", "Filter"),
                VPBTranslation.T(
                    "gallery.usertags.segment_filter_tip",
                    "Filter grid: click tags to include/exclude items in the gallery. Not tagged stays in the title-bar Filter menu."),
                s,
                sq);
            EnsureUserTagWorkModeSegmentButton(
                btnRow,
                "VPB_UserTagWorkModeTagBtn",
                UserTagAvailMode.Tag,
                VPBTranslation.T("gallery.usertags.segment_tag", "Tag"),
                VPBTranslation.T(
                    "gallery.usertags.segment_tag_tip",
                    "Tag selection: click tags to apply/remove them on the selected gallery item(s)."),
                s,
                sq);

            HorizontalLayoutGroup hlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth = false;
                hlg.padding = new RectOffset(0, 0, 0, 0);
            }

            Transform editT = btnRow.Find("VPB_UserTagEditBtn");
            if (editT != null)
            {
                editT.SetSiblingIndex(0);
                LayoutElement le = editT.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.flexibleWidth = 0f;
                    le.minWidth = sq;
                    le.preferredWidth = sq;
                }
            }
            Transform filterSeg = btnRow.Find("VPB_UserTagWorkModeFilterBtn");
            if (filterSeg != null) filterSeg.SetSiblingIndex(1);
            Transform tagSeg = btnRow.Find("VPB_UserTagWorkModeTagBtn");
            if (tagSeg != null) tagSeg.SetSiblingIndex(2);

            SyncUserTagFilterModeToggleVisualSticky(bulkBlockV3);
            try { ApplyUserTagsStickyScrollChrome(ChromeScale); } catch { }
        }

        private void EnsureUserTagWorkModeHintRow(Transform bulkBlockV3)
        {
            if (bulkBlockV3 == null) return;
            if (bulkBlockV3.Find("TagsModeHintRow") != null) return;

            float s = ChromeScale;
            int titleFs = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            float hintH = Mathf.Max(22f * s, titleFs * 1.05f);

            GameObject hintRow = UI.CreateChildRT(bulkBlockV3.gameObject, "TagsModeHintRow");
            UI.AddHLG(hintRow, 0f, childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: true);
            UI.AddLE(hintRow, minHeight: hintH, preferredHeight: hintH, flexibleWidth: 1f);
            Text hintTxt = UI.CreateLabel(hintRow, GetUserTagWorkModeHint(),
                GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef),
                new Color(0.78f, 0.84f, 0.92f, 1f), TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow, name: "ModeHint");
            UI.AddLE(hintTxt.gameObject, minHeight: hintH, preferredHeight: hintH, flexibleWidth: 1f);

            // Sit under title row when present.
            Transform titleRow = bulkBlockV3.Find("TagsTitleRow");
            if (titleRow != null)
                hintRow.transform.SetSiblingIndex(titleRow.GetSiblingIndex() + 1);
            else
                hintRow.transform.SetSiblingIndex(0);
        }

        private void EnsureUserTagWorkModeSegmentButton(
            Transform btnRow,
            string name,
            UserTagAvailMode mode,
            string label,
            string tip,
            float s,
            float height)
        {
            if (btnRow == null) return;
            Transform existing = btnRow.Find(name);
            if (existing != null)
            {
                Button existingBtn = existing.GetComponent<Button>();
                if (existingBtn != null)
                {
                    existingBtn.onClick.RemoveAllListeners();
                    UserTagAvailMode captured = mode;
                    existingBtn.onClick.AddListener(() => RequestUserTagWorkMode(captured));
                }
                Text existingLbl = existing.GetComponentInChildren<Text>(true);
                if (existingLbl != null) existingLbl.text = label;
                LayoutElement existingLe = existing.GetComponent<LayoutElement>();
                if (existingLe != null)
                {
                    existingLe.minWidth = 56f * s;
                    existingLe.minHeight = height;
                    existingLe.preferredHeight = height;
                    existingLe.flexibleWidth = 1f;
                }
                AddTooltipPlain(existing.gameObject, tip);
                return;
            }

            int font = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            GameObject go = UI.CreateUIButton(
                btnRow.gameObject,
                0f,
                0f,
                label,
                font,
                0f,
                0f,
                AnchorPresets.stretchAll,
                () => RequestUserTagWorkMode(mode));
            go.name = name;
            LayoutElement fle = go.GetComponent<LayoutElement>();
            if (fle == null) fle = go.AddComponent<LayoutElement>();
            fle.flexibleWidth = 1f;
            fle.minWidth = 56f * s;
            fle.preferredWidth = 0f;
            fle.minHeight = height;
            fle.preferredHeight = height;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
            AddTooltipPlain(go, tip);
        }

        private void AddUserTagFilterButtonIconAndLabel(GameObject filterGo, float s)
        {
            // Legacy cycle-button chrome — kept so older call sites compile; segments replace it.
            if (filterGo == null) return;
            Transform oldIcon = filterGo.transform.Find("Icon");
            if (oldIcon != null) UnityEngine.Object.Destroy(oldIcon.gameObject);

            float iconSize = Mathf.Clamp(22f * s, 16f, 30f);
            float iconPad = 10f * s;
            float iconRightEdge = iconPad + iconSize;

            GameObject iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(filterGo.transform, false);
            Image iconImg = UI.AddImage(iconGo, Color.white, false);
            iconImg.preserveAspect = true;
            RectTransform irt = iconGo.GetComponent<RectTransform>();
            if (irt != null)
            {
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(iconSize, iconSize);
                irt.anchoredPosition = new Vector2(iconPad, 0f);
            }

            Text t = filterGo.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.gameObject.SetActive(true);
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                RectTransform trt = t.GetComponent<RectTransform>();
                if (trt != null)
                {
                    float inset = iconRightEdge + 8f * s;
                    trt.offsetMin = new Vector2(inset, trt.offsetMin.y);
                    trt.offsetMax = new Vector2(-inset, trt.offsetMax.y);
                }
            }
        }

        private UserTagAvailMode ResolveDefaultUserTagAvailMode()
        {
            try
            {
                if (VPBConfig.Instance != null)
                    return VPBConfig.Instance.ResolveDefaultUserTagAvailMode();
            }
            catch { }
            return UserTagAvailMode.FilterByTags;
        }

        /// <summary>True when multi-tag grid filter uses Isolate (all tags); false for Compound (any tag).</summary>
        internal bool UserTagFilterRequiresAllTags()
        {
            try
            {
                if (VPBConfig.Instance != null)
                    return VPBConfig.Instance.IsGalleryUserTagFilterIsolate();
            }
            catch { }
            return false;
        }

        private string GetUserTagPickRowTooltipFilter()
        {
            return UserTagFilterRequiresAllTags()
                ? VPBTranslation.T(
                    "gallery.usertags.pick_row_tooltip_filter_all",
                    "Filter grid: tap include on/off (all must match). Right-click or drag to Exclude to hide items with this tag. Drag to Applied below.")
                : VPBTranslation.T(
                    "gallery.usertags.pick_row_tooltip_filter_any",
                    "Filter grid: tap include on/off (any can match). Right-click or drag to Exclude to hide items with this tag. Drag to Applied below.");
        }

        /// <summary>
        /// Include/exclude user-tag sets arm the grid filter independent of F/T work mode.
        /// FilterUntagged browse is exclusive and ignores include/exclude until dismissed.
        /// </summary>
        private bool IsUserTagIncludeExcludeFilterArmed()
        {
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged) return false;
            if (activeUserTags != null && activeUserTags.Count > 0) return true;
            if (excludedUserTags != null && excludedUserTags.Count > 0) return true;
            return false;
        }

        private bool IsUserTagIncludeFilterArmed()
        {
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged) return false;
            return activeUserTags != null && activeUserTags.Count > 0;
        }

        private bool IsUserTagExcludeFilterArmed()
        {
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged) return false;
            return excludedUserTags != null && excludedUserTags.Count > 0;
        }

        private bool UserTagNameIsInIncludeOrExcludeFilter(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return false;
            if (activeUserTags != null && activeUserTags.Contains(tagName)) return true;
            if (excludedUserTags != null && excludedUserTags.Contains(tagName)) return true;
            return false;
        }

        /// <summary>True when available tag row should be omitted in filter-by-tags mode (unused in current category).</summary>
        private bool ShouldHideUnusedUserTagInFilterAvailList(UserTagSideTabEntry ut)
        {
            if (_userTagAvailMode != UserTagAvailMode.FilterByTags) return false;
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode)
                    return false;
            }
            catch { return false; }
            // Side-list search: match full vocabulary (including zero-count).
            if (!string.IsNullOrEmpty(userTagFilter)) return false;
            // Wait until per-category counts are ready; vocabulary-only cache must not hide everything as Count=0.
            if (!_userTagSideTabCountsReady) return false;
            // Fresh/wiped DB: vocabulary exists but no assignments yet — hide-unused would empty the list.
            if (!_userTagAnyAssignmentExists) return false;
            if (ut.Count == UserTagCreateRowCountSentinel) return false;
            if (ut.Count == UserTagUnusedBucketHeaderSentinel) return false;
            // Expanded Unused bucket shows zero-count rows explicitly.
            if (_userTagShowUnusedBucket) return false;
            if (_userTagSelectionRowCount > 0 && !string.IsNullOrEmpty(ut.Name))
            {
                UserTagSelectionState st = GetUserTagSelectionState(ut.Name);
                if (st != UserTagSelectionState.Off) return false;
            }
            // Armed filter tags stay visible even when Count=0.
            if (UserTagNameIsInIncludeOrExcludeFilter(ut.Name)) return false;
            return ut.Count <= 0;
        }

        /// <summary>When User Tags side panel opens, apply configured default work mode (filter / apply). Not tagged browse filter stays until manual clear.</summary>
        internal void ApplyDefaultUserTagAvailModeOnTagsPanelOpen()
        {
            UserTagAvailMode mode = ResolveDefaultUserTagAvailMode();
            // Opening the tags rail must not dismiss title-bar Not tagged (browse filter ≠ panel work mode).
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
            {
                if (mode == UserTagAvailMode.Tag || mode == UserTagAvailMode.FilterByTags)
                    _userTagModeBeforeUntagged = mode;
                try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
                try { SyncBrowseFilterChipChrome(); } catch { }
                return;
            }
            if (mode == UserTagAvailMode.FilterUntagged)
            {
                SetUserTagAvailMode(UserTagAvailMode.FilterUntagged);
                return;
            }
            if (_userTagAvailMode == mode)
            {
                try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
                return;
            }
            RequestUserTagWorkMode(mode);
        }

        private void OnUserTagAvailFilterModeClicked()
        {
            // Tag \u2194 Filter only. Not tagged is title-bar browse filter — keep it armed.
            UserTagAvailMode chrome = ResolveUserTagWorkModeForChrome();
            if (chrome == UserTagAvailMode.Tag)
                RequestUserTagWorkMode(UserTagAvailMode.FilterByTags);
            else
                RequestUserTagWorkMode(UserTagAvailMode.Tag);
        }

        /// <summary>
        /// Switch Tag / Filter work mode. While Not tagged browse filter is on, only updates
        /// remembered work mode + chrome — does not clear the browse filter (manual dismiss only).
        /// </summary>
        private void RequestUserTagWorkMode(UserTagAvailMode mode)
        {
            if (mode != UserTagAvailMode.Tag && mode != UserTagAvailMode.FilterByTags)
            {
                SetUserTagAvailMode(mode);
                return;
            }
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
            {
                if (_userTagModeBeforeUntagged == mode)
                {
                    try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                    return;
                }
                _userTagModeBeforeUntagged = mode;
                try
                {
                    string t = currentCategoryTitle ?? (titleText != null ? titleText.text : null) ?? "";
                    string p = currentPath ?? "";
                    if (!string.IsNullOrEmpty(t) || !string.IsNullOrEmpty(p))
                        SaveCurrentCategoryFilterState(t, p);
                }
                catch { }
                try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                return;
            }
            SetUserTagAvailMode(mode);
        }

        private void SetUserTagAvailMode(UserTagAvailMode mode)
        {
            UserTagAvailMode prev = _userTagAvailMode;
            if (prev == mode) return;
            if (mode == UserTagAvailMode.FilterUntagged
                && prev != UserTagAvailMode.FilterUntagged)
                _userTagModeBeforeUntagged = prev == UserTagAvailMode.Tag
                    ? UserTagAvailMode.Tag
                    : UserTagAvailMode.FilterByTags;
            _userTagAvailMode = mode;
            if (prev == UserTagAvailMode.FilterUntagged || _userTagAvailMode == UserTagAvailMode.FilterUntagged)
                ClearUntaggedTaggedPinKeys();
            try
            {
                string t = currentCategoryTitle ?? (titleText != null ? titleText.text : null) ?? "";
                string p = currentPath ?? "";
                if (!string.IsNullOrEmpty(t) || !string.IsNullOrEmpty(p))
                    SaveCurrentCategoryFilterState(t, p);
            }
            catch { }
            SyncUserTagFilterModeToggleVisualsEverywhere();
            try { UpdateGlobalSourceFilterButtonLabel(); } catch { }
            try { SyncBrowseFilterChipChrome(); } catch { }
            if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
            {
                try { RebuildGlobalSourceFilterMenuOptions(); } catch { }
            }

            // Grid only changes when untagged browse toggles. Include/exclude filters stay live across F↔T
            // (work mode is list density + click binding only — never re-arm/disarm filter sets).
            bool untaggedInvolved = prev == UserTagAvailMode.FilterUntagged
                || mode == UserTagAvailMode.FilterUntagged;
            if (untaggedInvolved)
            {
                try { RefreshFiles(true, false, false, null); } catch { }
            }

            // Prefer in-place User Tags rebuild over full UpdateTabs — avoids left-panel flash on Filter↔Tag.
            _userTagVirtViewSig = null;
            bool leftOpen = IsUserTagsSideTabOpen(true);
            bool rightOpen = IsUserTagsSideTabOpen(false);
            if (leftOpen || rightOpen)
            {
                if (leftOpen)
                {
                    try { RefreshUserTagsAvailPaneInPlace(true); } catch { }
                    if (leftTabContainerGO != null)
                        try { RequestUserTagVirtLayoutRefresh(true, leftTabContainerGO.transform, preserveScroll: true); } catch { }
                }
                if (rightOpen)
                {
                    try { RefreshUserTagsAvailPaneInPlace(false); } catch { }
                    if (rightTabContainerGO != null)
                        try { RequestUserTagVirtLayoutRefresh(false, rightTabContainerGO.transform, preserveScroll: true); } catch { }
                }
            }
            else
            {
                try { UpdateTabs(); } catch { }
            }

            // Cold-path feedback when switching Tag ↔ Filter verbs.
            if ((prev == UserTagAvailMode.Tag || prev == UserTagAvailMode.FilterByTags)
                && (mode == UserTagAvailMode.Tag || mode == UserTagAvailMode.FilterByTags)
                && prev != mode)
            {
                try { ShowTemporaryStatus(GetUserTagWorkModeHint(), 1.6f); } catch { }
            }
        }

        /// <summary>Backdrop colour for Tag / Filter work modes (F/T mini + sticky toggle).</summary>
        private Color UserTagAvailModeColor(UserTagAvailMode mode)
        {
            switch (mode)
            {
                case UserTagAvailMode.Tag: return new Color(0.20f, 0.50f, 0.25f, 1f);
                case UserTagAvailMode.FilterByTags: return new Color(0.18f, 0.38f, 0.62f, 1f);
                default: return new Color(0.18f, 0.38f, 0.62f, 1f);
            }
        }

        /// <summary>Work mode shown on side-rail chrome while Not tagged browse filter is on.</summary>
        private UserTagAvailMode ResolveUserTagWorkModeForChrome()
        {
            if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
            {
                return _userTagModeBeforeUntagged == UserTagAvailMode.Tag
                    ? UserTagAvailMode.Tag
                    : UserTagAvailMode.FilterByTags;
            }
            return _userTagAvailMode;
        }

        private void CreateUserTagModeMiniButton(GameObject parent, string letter, UserTagAvailMode mode, float sq, float s, string tip)
        {
            GameObject go = new GameObject("VPB_UserTagModeMiniBtn_" + mode);
            go.transform.SetParent(parent.transform, false);
            Image img = AddUserTagSideChromeRoundedBg(go, UserTagAvailModeColor(mode));
            Button btn = go.AddComponent<Button>();
            UI.ConfigButtonFlat(btn);
            btn.targetGraphic = img;
            UserTagAvailMode target = mode;
            btn.onClick.AddListener(() => RequestUserTagWorkMode(target));
            go.AddComponent<UIHoverBorder>();

            LayoutElement le = UI.AddLE(go, minWidth: sq, minHeight: sq, preferredWidth: sq, preferredHeight: sq, flexibleWidth: 0f, flexibleHeight: 0f);

            UI.CreateLabel(go, letter, GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef),
                Color.white, TextAnchor.MiddleCenter, raycastTarget: false);

            AddTooltipPlain(go, tip);
        }

        private void SyncUserTagModeMiniButtons(Transform bulkBlockV3)
        {
            if (bulkBlockV3 == null) return;
            Transform row = bulkBlockV3.Find("TagsTitleRow");
            if (row == null) return;
            // Destroy legacy F/T/N letter minis — replaced by labeled segments.
            Transform legacyF = row.Find("VPB_UserTagModeMiniBtn_" + UserTagAvailMode.FilterByTags);
            if (legacyF != null) UnityEngine.Object.Destroy(legacyF.gameObject);
            Transform legacyT = row.Find("VPB_UserTagModeMiniBtn_" + UserTagAvailMode.Tag);
            if (legacyT != null) UnityEngine.Object.Destroy(legacyT.gameObject);
            Transform legacyN = row.Find("VPB_UserTagModeMiniBtn_" + UserTagAvailMode.FilterUntagged);
            if (legacyN != null) UnityEngine.Object.Destroy(legacyN.gameObject);
        }

        private void SyncUserTagFilterModeToggleVisualsEverywhere()
        {
            try { SyncUserTagFilterModeStickySide(leftUserTagsAvailStickyGO); } catch { }
            try { SyncUserTagFilterModeStickySide(rightUserTagsAvailStickyGO); } catch { }
            try { SyncUserTagAvailTitleCount(true); } catch { }
            try { SyncUserTagAvailTitleCount(false); } catch { }
        }

        private void SyncUserTagFilterModeStickySide(GameObject availStickyGo)
        {
            if (availStickyGo == null) return;
            Transform v3 = availStickyGo.transform.Find("VPB_UserTagBulkBlock_v3");
            if (v3 == null) return;
            SyncUserTagFilterModeToggleVisualSticky(v3);
        }

        private void SyncUserTagFilterModeToggleVisualSticky(Transform bulkBlockV3)
        {
            if (bulkBlockV3 == null) return;
            SyncUserTagModeMiniButtons(bulkBlockV3);

            Transform hintRow = bulkBlockV3.Find("TagsModeHintRow");
            if (hintRow != null)
            {
                Text hint = hintRow.GetComponentInChildren<Text>(true);
                if (hint != null) hint.text = GetUserTagWorkModeHint();
            }

            Transform btnRow = bulkBlockV3.Find("BulkBtnRow");
            if (btnRow == null) return;

            // Drop legacy single cycle button if an older pane still has it.
            Transform legacyCycle = btnRow.Find("VPB_UserTagFilterModeBtn");
            if (legacyCycle != null) UnityEngine.Object.Destroy(legacyCycle.gameObject);

            UserTagAvailMode chromeMode = ResolveUserTagWorkModeForChrome();
            SyncUserTagWorkModeSegmentVisual(
                btnRow.Find("VPB_UserTagWorkModeFilterBtn"),
                chromeMode == UserTagAvailMode.FilterByTags,
                UserTagAvailMode.FilterByTags,
                VPBTranslation.T("gallery.usertags.segment_filter", "Filter"));
            SyncUserTagWorkModeSegmentVisual(
                btnRow.Find("VPB_UserTagWorkModeTagBtn"),
                chromeMode == UserTagAvailMode.Tag,
                UserTagAvailMode.Tag,
                VPBTranslation.T("gallery.usertags.segment_tag", "Tag"));
        }

        private void SyncUserTagWorkModeSegmentVisual(Transform seg, bool active, UserTagAvailMode mode, string label)
        {
            if (seg == null) return;
            Color baseCol = UserTagAvailModeColor(mode);
            Color showCol = active
                ? baseCol
                : new Color(baseCol.r * 0.38f, baseCol.g * 0.38f, baseCol.b * 0.38f, 0.92f);
            Image bd = seg.GetComponent<Image>();
            if (bd != null) bd.color = showCol;
            Text labelTxt = seg.GetComponentInChildren<Text>(true);
            if (labelTxt != null)
            {
                labelTxt.gameObject.SetActive(true);
                labelTxt.text = label;
                labelTxt.color = active ? Color.white : UI.TextMuted;
            }
        }

        // ShowUserTagListEditor / HideUserTagListEditor / EnsureUserTagEditorUiBuilt live in
        // GalleryPanel.TagEditor.cs — unified DetailStripTagMenu (Tag | Database modes).

        /// <summary>
        /// Shared merge/rename name dialog: dim root, centered panel, title, single-line input, Cancel/OK row.
        /// </summary>
        private static void UserTagEditorBuildNameDialog(
            Transform parent,
            string rootName,
            string panelName,
            string inputName,
            string buttonsName,
            string title,
            string titleObjectName,
            string placeholder,
            string cancelLabel,
            string confirmLabel,
            int titleFont,
            int bodyFont,
            int smallFont,
            float s,
            UnityAction onCancel,
            UnityAction onConfirm,
            out GameObject rootGo,
            out Text titleText,
            out InputField input)
        {
            GameObject panel;
            rootGo = UI.CreateModalChrome(
                parent.gameObject, rootName, 420f * s, 200f * s,
                new Color(0.14f, 0.14f, 0.17f, 1f), null, out panel, dimAlpha: 0.5f);
            panel.name = panelName;
            rootGo.SetActive(false);
            UI.AddVLG(panel, 10f * s, UI.Pad(14f, 14f, 12f, 12f, s));

            titleText = UI.CreateLabel(panel, title, titleFont, Color.white, name: titleObjectName);
            UI.AddLE(titleText.gameObject, minHeight: 24f * s, flexibleWidth: 1f);

            input = UI.CreateChromeLayoutInputField(
                panel.transform, bodyFont, 36f * s, 1f, 8f * s, 4f * s,
                new Color(0.07f, 0.07f, 0.09f, 1f), new Color(0.42f, 0.42f, 0.45f, 1f),
                placeholder, inputName);

            GameObject btnRow = UI.CreateChildRT(panel, buttonsName);
            UI.AddHLG(btnRow, 8f * s, childAlignment: TextAnchor.MiddleCenter);
            UI.AddLE(btnRow, minHeight: 40f * s, flexibleWidth: 1f);

            GameObject cancel = UI.CreateUIButton(btnRow, 0f, 0f, cancelLabel, smallFont, 0f, 0f, AnchorPresets.stretchAll, onCancel);
            cancel.GetComponent<Image>().color = new Color(0.32f, 0.32f, 0.36f, 1f);
            UI.AddLE(cancel, minHeight: 38f * s, flexibleWidth: 1f);

            GameObject ok = UI.CreateUIButton(btnRow, 0f, 0f, confirmLabel, smallFont, 0f, 0f, AnchorPresets.stretchAll, onConfirm);
            ok.GetComponent<Image>().color = new Color(0.22f, 0.42f, 0.58f, 1f);
            UI.AddLE(ok, minHeight: 38f * s, flexibleWidth: 1f);

            rootGo.transform.SetAsLastSibling();
        }

        private void UserTagEditorSetTitleCount(int totalInDatabase)
        {
            if (_userTagEditorTitleText == null) return;
            _userTagEditorTitleText.text = string.Format(
                VPBTranslation.T("gallery.usertags.editor_db_title_fmt", "Tags Database ({0})"),
                totalInDatabase);
            _userTagEditorTitleText.fontStyle = FontStyle.Bold;
            _userTagEditorTitleText.color = UI.PopupText;
            _userTagEditorTitleText.alignment = TextAnchor.MiddleLeft;
        }

        private void UserTagEditorCycleSort()
        {
            _userTagEditorSortMode = (_userTagEditorSortMode + 1) % 4;
            UserTagEditorSyncSortIcon();
            RebuildUserTagEditorRows();
        }

        private void UserTagEditorSyncSortIcon()
        {
            if (_userTagEditorSortIconImage == null) return;
            if (sceneSourceSortModeSprites != null && sceneSourceSortModeSprites.Length > 0)
            {
                int idx = _userTagEditorSortMode % sceneSourceSortModeSprites.Length;
                Sprite sp = sceneSourceSortModeSprites[idx];
                if (sp != null)
                {
                    _userTagEditorSortIconImage.sprite = sp;
                    _userTagEditorSortIconImage.enabled = true;
                }
            }
        }

        private void UserTagEditorClearFilter()
        {
            _userTagEditorRowSelection.Clear();
            _userTagEditorAnchorTag = null;
            bool hadFilter = !string.IsNullOrEmpty(_detailStripTagMenuFilter)
                || (_userTagEditorFilterInput != null && !string.IsNullOrEmpty(_userTagEditorFilterInput.text));
            if (hadFilter)
            {
                _detailStripTagMenuFilter = "";
                if (_userTagEditorFilterInput != null)
                {
                    // onValueChanged / shared search handler triggers rebuild once
                    try { _userTagEditorFilterInput.text = ""; } catch { }
                    return;
                }
                RebuildUserTagEditorRows();
                return;
            }
            UserTagEditorSyncRowSelectionVisuals();
        }

        private void UserTagEditorOnRowClicked(string nameSnap, int rowIndex, List<UserTagSideTabEntry> visibleRows, Image bg, Color baseCol, Color selCol)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift && !string.IsNullOrEmpty(_userTagEditorAnchorTag) && visibleRows != null && visibleRows.Count > 0)
            {
                int anchorIdx = -1;
                for (int i = 0; i < visibleRows.Count; i++)
                {
                    if (string.Equals(visibleRows[i].Name, _userTagEditorAnchorTag, StringComparison.OrdinalIgnoreCase))
                    {
                        anchorIdx = i;
                        break;
                    }
                }
                if (anchorIdx >= 0)
                {
                    int lo = Mathf.Min(anchorIdx, rowIndex);
                    int hi = Mathf.Max(anchorIdx, rowIndex);
                    _userTagEditorRowSelection.Clear();
                    for (int j = lo; j <= hi; j++)
                        _userTagEditorRowSelection.Add(visibleRows[j].Name);
                    UserTagEditorSyncRowSelectionVisuals();
                    return;
                }
            }

            if (_userTagEditorRowSelection.Contains(nameSnap))
                _userTagEditorRowSelection.Remove(nameSnap);
            else
                _userTagEditorRowSelection.Add(nameSnap);
            _userTagEditorAnchorTag = nameSnap;
            bg.color = _userTagEditorRowSelection.Contains(nameSnap) ? selCol : baseCol;
        }

        private void UserTagEditorSyncRowSelectionVisuals()
        {
            if (_userTagEditorRowVisuals == null || _userTagEditorRowVisuals.Count == 0) return;
            for (int i = 0; i < _userTagEditorRowVisuals.Count; i++)
            {
                UserTagEditorRowVisual v = _userTagEditorRowVisuals[i];
                if (v == null || v.Bg == null || string.IsNullOrEmpty(v.Name)) continue;
                bool sel = _userTagEditorRowSelection != null && _userTagEditorRowSelection.Contains(v.Name);
                v.Bg.color = sel ? _userTagEditorRowSelCol : _userTagEditorRowBaseCol;
            }
        }

        /// <summary>Cave wire multiline Home/End fixer once.</summary>
        private static void FixMultilineHomeEndBehavior(InputField field)
        {
            if (field == null) return;
            GameObject go = field.gameObject;
            if (go.GetComponent<MultilineInputFieldHomeEndFix>() == null)
                go.AddComponent<MultilineInputFieldHomeEndFix>();
        }

        private void UserTagEditorStopNewTagChromeFlash()
        {
            StopCo(ref _userTagEditorNewTagFlashCo);
        }

        private IEnumerator UserTagEditorNewTagChromeFlashRoutine(Color flashCol, float holdSec, float fadeSec)
        {
            if (_userTagEditorNewTagInputChrome == null) yield break;
            Color baseCol = UserTagEditorNewTagChromeBaseCol;
            _userTagEditorNewTagInputChrome.color = flashCol;
            float t = 0f;
            while (t < holdSec)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            t = 0f;
            while (t < fadeSec)
            {
                t += Time.unscaledDeltaTime;
                float u = fadeSec > 0.0001f ? Mathf.Clamp01(t / fadeSec) : 1f;
                _userTagEditorNewTagInputChrome.color = Color.Lerp(flashCol, baseCol, u);
                yield return null;
            }
            _userTagEditorNewTagInputChrome.color = baseCol;
            _userTagEditorNewTagFlashCo = null;
        }

        private void UserTagEditorFlashNewTagChromeOk()
        {
            if (_userTagEditorNewTagInputChrome == null) return;
            UserTagEditorStopNewTagChromeFlash();
            _userTagEditorNewTagFlashCo = StartCoroutine(UserTagEditorNewTagChromeFlashRoutine(UserTagEditorNewTagChromeOkPulseCol, UserTagEditorNewTagFlashHoldSec, UserTagEditorNewTagFlashFadeSec));
        }

        private void UserTagEditorFlashNewTagChromeBad()
        {
            if (_userTagEditorNewTagInputChrome == null) return;
            UserTagEditorStopNewTagChromeFlash();
            _userTagEditorNewTagFlashCo = StartCoroutine(UserTagEditorNewTagChromeFlashRoutine(UserTagEditorNewTagChromeBadPulseCol, UserTagEditorNewTagFlashHoldSec + 0.15f, UserTagEditorNewTagFlashFadeSec));
        }

        /// <summary>Cave show every bad line; optional ack runs after Confirm.</summary>
        private void ShowTagValidationErrors(List<KeyValuePair<string, string>> invalidLines, int validCount, UnityAction onAck)
        {
            if (invalidLines == null || invalidLines.Count == 0)
            {
                if (onAck != null) onAck();
                return;
            }
            string body = GalleryUserTagEditorText.FormatTagValidationErrorBody(invalidLines);
            string msg;
            if (validCount > 0)
            {
                msg = VPBTranslation.T("gallery.usertags.editor_invalid_mixed_intro", "These lines were rejected:") + "\n\n" + body + "\n\n"
                    + string.Format(VPBTranslation.T("gallery.usertags.editor_invalid_confirm_creates_valid", "Confirm creates {0} valid tag(s). Cancel stops (nothing created)."), validCount);
            }
            else
                msg = VPBTranslation.T("gallery.usertags.editor_invalid_none_intro", "Nothing created. Rejected lines:") + "\n\n" + body;
            string title = VPBTranslation.T("gallery.usertags.editor_validation_title", "Tag validation");
            UnityAction ack = onAck != null ? onAck : delegate { };
            DisplayConfirm(title, msg, ack);
        }

        private void ShowTagDbRejectedPopup(List<string> names)
        {
            if (names == null || names.Count == 0) return;
            var sb = new StringBuilder(names.Count * 32);
            sb.AppendLine(VPBTranslation.T("gallery.usertags.editor_db_refused_head", "Database did not add these (full vocabulary cap or DB error). Full names:"));
            for (int i = 0; i < names.Count; i++)
            {
                sb.Append("\n• «");
                sb.Append(names[i] ?? "");
                sb.Append("»");
            }
            DisplayConfirm(
                VPBTranslation.T("gallery.usertags.editor_db_refused_title", "Tag create blocked"),
                sb.ToString(),
                delegate { });
        }

        private static string UserTagEditorBuildPathRiskDialogBody(List<KeyValuePair<string, string>> taggedHuman)
        {
            var sb = new StringBuilder(Math.Min(taggedHuman.Count, 512) * 48 + 128);
            sb.AppendLine(VPBTranslation.T("gallery.usertags.editor_path_risk_intro", "These tag names contain characters unsafe in Windows file names. Export / external tools may stumble. Please rename if that bites."));
            sb.AppendLine();
            sb.AppendLine(VPBTranslation.T("gallery.usertags.editor_path_risk_bad_chars_label", "Characters that trip file rules:"));
            for (int i = 0; i < taggedHuman.Count; i++)
            {
                sb.Append("\n• «");
                sb.Append(taggedHuman[i].Key ?? "");
                sb.Append("» → ");
                sb.Append(taggedHuman[i].Value ?? "");
            }
            sb.Append("\n\n");
            sb.Append(VPBTranslation.T("gallery.usertags.editor_path_risk_confirm", "Confirm creates them anyway. Cancel aborts this batch."));
            return sb.ToString();
        }

        private void UserTagEditorOnCreateTagsClicked()
        {
            string raw = _userTagEditorNewTagInput != null ? _userTagEditorNewTagInput.text : "";
            List<string> lines = GalleryUserTagEditorText.ParseMultilineTags(raw);
            if (lines.Count == 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.usertags.editor_no_lines", "No non-empty lines (paste list: one tag per line; blank lines ignored)."),
                    2.4f);
                UserTagEditorFlashNewTagChromeBad();
                return;
            }

            var validNorm = new List<string>(lines.Count);
            var invalidRows = new List<KeyValuePair<string, string>>(4);
            for (int i = 0; i < lines.Count; i++)
            {
                string ln = lines[i];
                if (!GalleryUserTagEditorText.ValidateTagName(ln, out string norm, out string err))
                    invalidRows.Add(new KeyValuePair<string, string>(ln, err));
                else
                    validNorm.Add(norm);
            }

            if (invalidRows.Count > 0)
            {
                UserTagEditorFlashNewTagChromeBad();
                int vc = validNorm.Count;
                if (vc == 0)
                {
                    if (invalidRows.Count >= GalleryUserTagEditorText.RejectPopupMinBadLineCount)
                        ShowTagValidationErrors(invalidRows, 0, null);
                    else
                        ShowTemporaryStatus(GalleryUserTagEditorText.FormatTagValidationErrorBody(invalidRows), 5f);
                    return;
                }
                ShowTagValidationErrors(invalidRows, vc, delegate { UserTagEditorRunPathWarningThenMaybeCreate(validNorm, true); });
                return;
            }

            UserTagEditorRunPathWarningThenMaybeCreate(validNorm, false);
        }

        private void UserTagEditorRunPathWarningThenMaybeCreate(List<string> validNormalized, bool hadInvalidLinesInBatch)
        {
            if (validNormalized == null || validNormalized.Count == 0) return;
            var pathRows = new List<KeyValuePair<string, string>>(8);
            for (int i = 0; i < validNormalized.Count; i++)
            {
                string n = validNormalized[i];
                if (VpbLocalDatabase.GalleryUserTagNameHasFilesystemRisk(n, out string human))
                    pathRows.Add(new KeyValuePair<string, string>(n, human));
            }
            if (pathRows.Count > 0)
            {
                string msg = UserTagEditorBuildPathRiskDialogBody(pathRows);
                string title = VPBTranslation.T("gallery.usertags.editor_path_risk_title", "File-name character warning");
                List<string> copy = new List<string>(validNormalized);
                DisplayConfirm(title, msg, delegate { UserTagEditorFinalizeCreateTagRows(copy, hadInvalidLinesInBatch); });
                return;
            }
            UserTagEditorFinalizeCreateTagRows(validNormalized, hadInvalidLinesInBatch);
        }

        private void UserTagEditorFinalizeCreateTagRows(List<string> validNormalized, bool hadInvalidLinesInBatch)
        {
            CreateTagRows(validNormalized, !hadInvalidLinesInBatch, out int created, out List<string> dbRejected);
            if (dbRejected != null && dbRejected.Count > 0)
            {
                UserTagEditorFlashNewTagChromeBad();
                ShowTagDbRejectedPopup(dbRejected);
            }
            else if (created > 0)
                UserTagEditorFlashNewTagChromeOk();

            if (created > 0)
                ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.usertags.editor_created_n", "Created {0} tag(s)."), created), 2f);
        }

        /// <summary>Cave push normalized names into SQLite vocabulary; list db rejects honest.</summary>
        private void CreateTagRows(IList<string> validNormalized, bool clearFieldWhenBatchClean, out int created, out List<string> dbRejected)
        {
            created = 0;
            dbRejected = new List<string>();
            if (validNormalized == null || validNormalized.Count == 0) return;
            for (int i = 0; i < validNormalized.Count; i++)
            {
                string nm = validNormalized[i];
                if (VpbLocalDatabase.TryEnsureGalleryUserTagInVocabulary(nm, out string norm) && !string.IsNullOrEmpty(norm))
                    created++;
                else
                    dbRejected.Add(nm);
            }

            if (clearFieldWhenBatchClean && dbRejected.Count == 0 && created > 0 && _userTagEditorNewTagInput != null)
                _userTagEditorNewTagInput.text = "";

            InvalidateTags();
            userTagsCached = false;
            RebuildUserTagEditorRows();
            try { DetailStripRefreshTagMenuAfterMutation(); } catch { }
            // Fresh vocabulary rows have Count=0; default Filter Mode hide-unused omits them.
            // Land in Tag Mode so Create tag rows is visible without hunting F/T.
            if (created > 0
                && _userTagAvailMode == UserTagAvailMode.FilterByTags
                && VPBConfig.Instance != null
                && VPBConfig.Instance.GalleryHideUnusedUserTagsInFilterMode)
            {
                try { RequestUserTagWorkMode(UserTagAvailMode.Tag); }
                catch { try { UpdateTabs(); } catch { } }
            }
            else
            {
                try { UpdateTabs(); } catch { }
            }
        }

        private void UserTagEditorRemoveSelectedFromDb()
        {
            var tags = CollectUserTagEditorCheckedTags();
            if (tags.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.editor_pick_rows", "Select one or more rows in the list (click)."), 2f);
                return;
            }
            int total = 0;
            for (int i = 0; i < tags.Count; i++)
            {
                if (VpbLocalDatabase.TryPurgeGalleryUserTagGlobally(tags[i], out int n))
                    total += n;
            }
            _userTagEditorRowSelection.Clear();
            _userTagEditorAnchorTag = null;
            InvalidateTags();
            userTagsCached = false;
            var actSnap = new List<string>(activeUserTags);
            for (int ai = 0; ai < actSnap.Count; ai++)
            {
                string a = actSnap[ai];
                for (int ti = 0; ti < tags.Count; ti++)
                {
                    if (string.Equals(a, tags[ti], StringComparison.OrdinalIgnoreCase))
                    {
                        activeUserTags.Remove(a);
                        break;
                    }
                }
            }
            try { RefreshFilesThenUpdateTabs(true); } catch { }
            RebuildUserTagEditorRows();
            try { DetailStripRefreshTagMenuAfterMutation(); } catch { }
            ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.usertags.editor_purge_done", "Removed tag(s); cleared {0} assignment(s)."), total), 2.5f);
        }

        private void UserTagEditorOpenMergeDialog()
        {
            var tags = CollectUserTagEditorCheckedTags();
            if (tags.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.editor_pick_rows", "Select one or more rows in the list (click)."), 2f);
                return;
            }
            if (_userTagEditorMergeModalTitleText != null)
            {
                _userTagEditorMergeModalTitleText.text = string.Format(
                    VPBTranslation.T("gallery.usertags.editor_merge_dialog_merge_n_into", "Merge {0} tags into…"),
                    tags.Count);
            }
            if (_userTagEditorMergeModalInput != null) _userTagEditorMergeModalInput.text = "";
            if (_userTagEditorMergeModalGo != null)
            {
                _userTagEditorMergeModalGo.SetActive(true);
                _userTagEditorMergeModalGo.transform.SetAsLastSibling();
            }
        }

        private void UserTagEditorCloseMergeDialog()
        {
            if (_userTagEditorMergeModalGo != null) _userTagEditorMergeModalGo.SetActive(false);
        }

        private void UserTagEditorConfirmMergeFromDialog()
        {
            var tags = CollectUserTagEditorCheckedTags();
            if (tags.Count == 0)
            {
                UserTagEditorCloseMergeDialog();
                return;
            }
            string rawTarget = _userTagEditorMergeModalInput != null ? _userTagEditorMergeModalInput.text : "";
            if (!VpbLocalDatabase.TryMergeGalleryUserTagsInto(tags, rawTarget, out string normTarget, out int nTouch))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.usertags.editor_merge_invalid",
                        "Choose tag row(s) and enter valid merge target (1–512 chars; unicode / punctuation ok; no line breaks or control chars except tab)."),
                    2.5f);
                return;
            }
            UserTagEditorCloseMergeDialog();
            _userTagEditorRowSelection.Clear();
            _userTagEditorAnchorTag = null;
            if (_userTagEditorMergeModalInput != null) _userTagEditorMergeModalInput.text = "";
            InvalidateTags();
            userTagsCached = false;
            var actSnapM = new List<string>(activeUserTags);
            for (int ai = 0; ai < actSnapM.Count; ai++)
            {
                string a = actSnapM[ai];
                for (int ti = 0; ti < tags.Count; ti++)
                {
                    if (string.Equals(a, tags[ti], StringComparison.OrdinalIgnoreCase))
                    {
                        activeUserTags.Remove(a);
                        break;
                    }
                }
            }
            try { RefreshFilesThenUpdateTabs(true); } catch { }
            RebuildUserTagEditorRows();
            try { DetailStripRefreshTagMenuAfterMutation(); } catch { }
            ShowTemporaryStatus(
                string.Format(VPBTranslation.T("gallery.usertags.editor_merge_done", "Merged into «{0}». Updated {1} item-tag link(s)."), normTarget, nTouch),
                2.5f);
        }

        private void UserTagEditorOpenRenameDialog()
        {
            var tags = CollectUserTagEditorCheckedTags();
            if (tags.Count != 1)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.usertags.editor_rename_pick_one", "Select exactly one tag row to rename (click)."),
                    2f);
                return;
            }
            _userTagEditorRenameSourcePrefix = tags[0];
            if (_userTagEditorRenameModalTitleText != null)
            {
                _userTagEditorRenameModalTitleText.text = string.Format(
                    VPBTranslation.T("gallery.usertags.editor_rename_dialog_title_fmt", "Rename «{0}» to…"),
                    tags[0]);
            }
            if (_userTagEditorRenameModalInput != null) _userTagEditorRenameModalInput.text = "";
            if (_userTagEditorRenameModalGo != null)
            {
                _userTagEditorRenameModalGo.SetActive(true);
                _userTagEditorRenameModalGo.transform.SetAsLastSibling();
            }
        }

        private void UserTagEditorCloseRenameDialog()
        {
            if (_userTagEditorRenameModalGo != null) _userTagEditorRenameModalGo.SetActive(false);
        }

        private void UserTagEditorRemapActiveUserTagsAfterRename(string normPrefix, string normNew)
        {
            if (activeUserTags == null || activeUserTags.Count == 0) return;
            if (string.IsNullOrEmpty(normPrefix)) return;
            var actSnap = new List<string>(activeUserTags);
            for (int ai = 0; ai < actSnap.Count; ai++)
            {
                string a = actSnap[ai];
                string na = VpbLocalDatabase.NormalizeGalleryUserTagName(a);
                if (string.IsNullOrEmpty(na)) continue;
                if (string.Equals(na, normPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    activeUserTags.Remove(a);
                    if (!string.IsNullOrEmpty(normNew))
                        activeUserTags.Add(normNew);
                    continue;
                }
                if (na.Length > normPrefix.Length
                    && na.StartsWith(normPrefix, StringComparison.OrdinalIgnoreCase)
                    && na[normPrefix.Length] == ' ')
                {
                    string mapped = normNew + na.Substring(normPrefix.Length);
                    if (!string.IsNullOrEmpty(VpbLocalDatabase.NormalizeGalleryUserTagName(mapped)))
                    {
                        activeUserTags.Remove(a);
                        activeUserTags.Add(mapped);
                    }
                }
            }
        }

        private void UserTagEditorConfirmRenameFromDialog()
        {
            if (string.IsNullOrEmpty(_userTagEditorRenameSourcePrefix))
            {
                UserTagEditorCloseRenameDialog();
                return;
            }
            string rawTarget = _userTagEditorRenameModalInput != null ? _userTagEditorRenameModalInput.text : "";
            if (!VpbLocalDatabase.TryPreviewGalleryUserTagRenameMergeConflict(_userTagEditorRenameSourcePrefix, rawTarget, out _, out bool wouldMerge))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.usertags.editor_rename_invalid",
                        "Enter valid new name (1–512 chars; unicode / punctuation ok; no line breaks or control chars except tab). Renamed tags must stay valid length."),
                    2.5f);
                return;
            }
            if (wouldMerge)
            {
                DisplayConfirm(
                    VPBTranslation.T("gallery.usertags.editor_rename_merge_confirm_title", "Name already in use"),
                    VPBTranslation.T(
                        "gallery.usertags.editor_rename_merge_confirm_msg",
                        "One or more destination names already exist. Merge item assignments into those existing tags?"),
                    UserTagEditorExecuteRenameConfirmed);
                return;
            }
            UserTagEditorExecuteRenameConfirmed();
        }

        private void UserTagEditorExecuteRenameConfirmed()
        {
            if (string.IsNullOrEmpty(_userTagEditorRenameSourcePrefix))
            {
                UserTagEditorCloseRenameDialog();
                return;
            }
            string rawTarget = _userTagEditorRenameModalInput != null ? _userTagEditorRenameModalInput.text : "";
            string normPref = VpbLocalDatabase.NormalizeGalleryUserTagName(_userTagEditorRenameSourcePrefix);
            if (!VpbLocalDatabase.TryRenameGalleryUserTagPrefixWithChildren(_userTagEditorRenameSourcePrefix, rawTarget, out string normTarget, out int nTouch))
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.usertags.editor_rename_invalid",
                        "Enter valid new name (1–512 chars; unicode / punctuation ok; no line breaks or control chars except tab). Renamed tags must stay valid length."),
                    2.5f);
                return;
            }
            UserTagEditorCloseRenameDialog();
            _userTagEditorRowSelection.Clear();
            _userTagEditorAnchorTag = null;
            if (_userTagEditorRenameModalInput != null) _userTagEditorRenameModalInput.text = "";
            _userTagEditorRenameSourcePrefix = null;
            UserTagEditorRemapActiveUserTagsAfterRename(normPref, normTarget);
            InvalidateTags();
            userTagsCached = false;
            try { RefreshFilesThenUpdateTabs(true); } catch { }
            RebuildUserTagEditorRows();
            try { DetailStripRefreshTagMenuAfterMutation(); } catch { }
            ShowTemporaryStatus(
                string.Format(VPBTranslation.T("gallery.usertags.editor_rename_done", "Renamed to «{0}». Updated {1} item-tag link(s)."), normTarget, nTouch),
                2.5f);
        }

        private List<string> CollectUserTagEditorCheckedTags()
        {
            var list = new List<string>();
            foreach (var t in _userTagEditorRowSelection)
                list.Add(t);
            return list;
        }

        private void UserTagEditorBeginExportYaml()
        {
            if (SuperController.singleton == null) return;
            try
            {
                if (SuperController.singleton.mainHUD != null && !SuperController.singleton.mainHUD.gameObject.activeSelf)
                    SuperController.singleton.ShowMainHUDMonitor();
            }
            catch { }

            string defaultFolder = "Custom/PluginData/VPB";
            SuperController.singleton.GetMediaPathDialog(
                UserTagEditorExportYamlPathChosen,
                "yaml",
                defaultFolder,
                false,
                true,
                false,
                "VPB_UserTags",
                true);
            try
            {
                if (SuperController.singleton.mediaFileBrowserUI != null)
                {
                    SuperController.singleton.mediaFileBrowserUI.SetTextEntry(true);
                    if (SuperController.singleton.mediaFileBrowserUI.fileEntryField != null)
                    {
                        SuperController.singleton.mediaFileBrowserUI.fileEntryField.text = "VPB_UserTags.yaml";
                        SuperController.singleton.mediaFileBrowserUI.ActivateFileNameField();
                    }
                }
            }
            catch { }
        }

        private void UserTagEditorExportYamlPathChosen(string selectedPath)
        {
            if (string.IsNullOrEmpty(selectedPath))
                return;
            string norm = selectedPath.Replace('\\', '/');
            string dir = Path.GetDirectoryName(norm);
            if (string.IsNullOrEmpty(dir)) dir = "";
            string baseName = Path.GetFileNameWithoutExtension(norm);
            if (string.IsNullOrEmpty(baseName)) baseName = "VPB_UserTags";
            string pathTag = Path.Combine(dir, baseName + "_by_tag.yaml").Replace('\\', '/');
            string pathItem = Path.Combine(dir, baseName + "_by_item.yaml").Replace('\\', '/');

            var rows = new List<VpbLocalDatabase.GalleryUserTagAssignmentRow>(4096);
            if (!VpbLocalDatabase.TryReadAllGalleryUserTagAssignments(rows))
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.editor_export_db_fail", "Export failed (database)."), 2.5f);
                return;
            }

            var tagToItems = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var itemToTags = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                VpbLocalDatabase.GalleryUserTagAssignmentRow r = rows[i];
                string itemKey = GalleryUserTagYamlBrain.EncodeItemKey(r.Category, r.PkgUid, r.InternalPath);
                if (!tagToItems.TryGetValue(r.TagName, out List<string> tiList))
                {
                    tiList = new List<string>();
                    tagToItems[r.TagName] = tiList;
                }
                tiList.Add(itemKey);

                if (!itemToTags.TryGetValue(itemKey, out List<string> itList))
                {
                    itList = new List<string>();
                    itemToTags[itemKey] = itList;
                }
                itList.Add(r.TagName);
            }

            var allVocab = new List<string>(tagToItems.Count + 32);
            if (VpbLocalDatabase.TryReadAllGalleryUserTagNames(allVocab))
            {
                for (int vi = 0; vi < allVocab.Count; vi++)
                {
                    string vn = allVocab[vi];
                    if (string.IsNullOrEmpty(vn) || tagToItems.ContainsKey(vn)) continue;
                    tagToItems[vn] = new List<string>();
                }
            }

            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categoriesExport = BuildUserTagCategoryExportList();
            string yamlTag = GalleryUserTagYamlBrain.BuildTagToItemsYaml(tagToItems, categoriesExport);
            string yamlItem = GalleryUserTagYamlBrain.BuildItemToTagsYaml(itemToTags, categoriesExport);
            try
            {
                FileManager.WriteAllText(pathTag, yamlTag);
                FileManager.WriteAllText(pathItem, yamlItem);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] User tag YAML export: " + ex);
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.editor_export_write_fail", "Export failed (write)."), 2.5f);
                return;
            }

            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.usertags.editor_export_done", "Exported:\n{0}\n{1}"),
                    pathTag,
                    pathItem),
                3.5f);
        }

        private void UserTagEditorBeginImportYaml()
        {
            if (SuperController.singleton == null) return;
            try
            {
                if (SuperController.singleton.mainHUD != null && !SuperController.singleton.mainHUD.gameObject.activeSelf)
                    SuperController.singleton.ShowMainHUDMonitor();
            }
            catch { }

            string defaultFolder = "Custom/PluginData/VPB";
            SuperController.singleton.GetMediaPathDialog(
                UserTagEditorImportYamlPathChosen,
                "yaml",
                defaultFolder,
                false,
                true,
                false,
                null,
                true);
        }

        private void UserTagEditorImportYamlPathChosen(string selectedPath)
        {
            if (string.IsNullOrEmpty(selectedPath))
                return;
            string text = null;
            try
            {
                text = FileManager.ReadAllText(selectedPath);
            }
            catch (Exception ex1)
            {
                try
                {
                    text = File.ReadAllText(selectedPath.Replace('/', Path.DirectorySeparatorChar));
                }
                catch (Exception ex2)
                {
                    LogUtil.LogError("[VPB] User tag YAML import read: " + ex1 + " | " + ex2);
                    ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.editor_import_read_fail", "Import failed (read file)."), 2.5f);
                    return;
                }
            }

            if (string.IsNullOrEmpty(text))
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.editor_import_empty", "File empty."), 2f);
                return;
            }

            if (!GalleryUserTagYamlBrain.TryParseImport(
                    text,
                    out Dictionary<string, List<string>> tagToItemKeys,
                    out Dictionary<string, List<string>> itemKeyToTags,
                    out List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> importedCategories,
                    out string err))
            {
                ShowTemporaryStatus(
                    string.Format(VPBTranslation.T("gallery.usertags.editor_import_parse_fail", "Import parse failed: {0}"), err ?? "?"),
                    3f);
                return;
            }

            int nLinks = UserTagEditorApplyImportedAssignments(tagToItemKeys, itemKeyToTags, out int nUnassigned, out List<string> skippedInvalidTags);
            int nCategories = UserTagEditorApplyImportedCategories(importedCategories);
            InvalidateTags();
            userTagsCached = false;
            if (nCategories > 0) InvalidateUserTagCategoryColorCache();
            try { RefreshFilesThenUpdateTabs(true); } catch { }
            RebuildUserTagEditorRows();
            try { DetailStripRefreshTagMenuAfterMutation(); } catch { }
            string importMsg;
            if (nUnassigned > 0)
            {
                importMsg = string.Format(
                    VPBTranslation.T("gallery.usertags.editor_import_done_unassigned", "Import applied: {0} tag link(s) updated, {1} unassigned tag(s) in vocabulary."),
                    nLinks,
                    nUnassigned);
            }
            else
            {
                importMsg = string.Format(
                    VPBTranslation.T("gallery.usertags.editor_import_done", "Import applied: {0} tag link(s) updated."),
                    nLinks);
            }
            if (nCategories > 0)
                importMsg += " " + string.Format(
                    VPBTranslation.T("gallery.usertags.editor_import_done_categories", "{0} categor(y/ies) applied."),
                    nCategories);
            ShowTemporaryStatus(importMsg, 2.8f);
            UserTagEditorNotifyImportSkippedTags(skippedInvalidTags);
        }

        /// <summary>Cave no silent drop on import: list tag names YAML had but Normalize rejected.</summary>
        private void UserTagEditorNotifyImportSkippedTags(List<string> skippedInvalidTags)
        {
            if (skippedInvalidTags == null || skippedInvalidTags.Count == 0) return;
            var rows = new List<KeyValuePair<string, string>>(skippedInvalidTags.Count);
            for (int i = 0; i < skippedInvalidTags.Count; i++)
            {
                string raw = skippedInvalidTags[i] ?? "";
                rows.Add(new KeyValuePair<string, string>(
                    raw,
                    VPBTranslation.T("gallery.usertags.editor_import_skip_reason", "Rejected by current tag rules (length / control chars / line breaks).")));
            }
            string body = GalleryUserTagEditorText.FormatTagValidationErrorBody(rows);
            string title = VPBTranslation.T("gallery.usertags.editor_import_skipped_title", "Import skipped tag name(s)");
            string msg = string.Format(
                    VPBTranslation.T("gallery.usertags.editor_import_skipped_intro", "{0} tag name(s) from file not imported:"),
                    skippedInvalidTags.Count)
                + "\n\n"
                + body;
            if (skippedInvalidTags.Count >= GalleryUserTagEditorText.RejectPopupMinBadLineCount)
                DisplayConfirm(title, msg, delegate { });
            else
                ShowTemporaryStatus(msg, 5f);
        }

        /// <summary>Merges tag→items and item→tags maps (one usually empty), applies DB assignments.</summary>
        private int UserTagEditorApplyImportedAssignments(
            Dictionary<string, List<string>> tagToItemKeys,
            Dictionary<string, List<string>> itemKeyToTags,
            out int unassignedTagsEnsured,
            out List<string> skippedInvalidTagNames)
        {
            unassignedTagsEnsured = 0;
            var skippedDistinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            skippedInvalidTagNames = new List<string>();
            var rowTags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            if (tagToItemKeys != null)
            {
                foreach (var kv in tagToItemKeys)
                {
                    string nt = VpbLocalDatabase.NormalizeGalleryUserTagName(kv.Key);
                    if (string.IsNullOrEmpty(nt))
                    {
                        if (!string.IsNullOrEmpty(kv.Key) && skippedDistinct.Add(kv.Key))
                            skippedInvalidTagNames.Add(kv.Key);
                        continue;
                    }
                    List<string> items = kv.Value;
                    if (items != null)
                    {
                        for (int i = 0; i < items.Count; i++)
                        {
                            string rawItem = items[i];
                            if (!GalleryUserTagYamlBrain.TryDecodeItemKey(rawItem, out string cat, out string pkg, out string ip))
                                continue;
                            string rowKey = GalleryUserTagYamlBrain.EncodeItemKey(cat, pkg, ip);
                            if (!rowTags.TryGetValue(rowKey, out HashSet<string> set))
                            {
                                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                rowTags[rowKey] = set;
                            }
                            set.Add(nt);
                        }
                    }
                    if (items == null || items.Count == 0)
                    {
                        if (VpbLocalDatabase.TryEnsureGalleryUserTagInVocabulary(nt, out _)) unassignedTagsEnsured++;
                    }
                }
            }

            if (itemKeyToTags != null)
            {
                foreach (var kv in itemKeyToTags)
                {
                    if (!GalleryUserTagYamlBrain.TryDecodeItemKey(kv.Key, out string cat, out string pkg, out string ip))
                        continue;
                    string rowKey = GalleryUserTagYamlBrain.EncodeItemKey(cat, pkg, ip);
                    if (!rowTags.TryGetValue(rowKey, out HashSet<string> set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        rowTags[rowKey] = set;
                    }
                    List<string> tags = kv.Value;
                    if (tags == null) continue;
                    for (int i = 0; i < tags.Count; i++)
                    {
                        string rawTag = tags[i];
                        string ntag = VpbLocalDatabase.NormalizeGalleryUserTagName(rawTag);
                        if (string.IsNullOrEmpty(ntag))
                        {
                            if (!string.IsNullOrEmpty(rawTag) && skippedDistinct.Add(rawTag))
                                skippedInvalidTagNames.Add(rawTag);
                            continue;
                        }
                        set.Add(ntag);
                    }
                }
            }

            int totalIns = 0;
            foreach (var kv in rowTags)
            {
                if (!GalleryUserTagYamlBrain.TryDecodeItemKey(kv.Key, out string c, out string p, out string path))
                    continue;
                var list = new List<string>(kv.Value);
                if (list.Count == 0) continue;
                int ins;
                if (VpbLocalDatabase.TryAssignGalleryUserTagsToRow(c, p, path, list, out ins))
                    totalIns += ins;
            }
            return totalIns;
        }

        private void RebuildUserTagEditorRows()
        {
            if (_userTagEditorRowsParent == null) return;

            UI.DestroyAllChildren(_userTagEditorRowsParent);

            if (!userTagsCached) CacheUserTagsSideTab();
            UserTagEditorSetTitleCount(cachedUserTagSideTab.Count);

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            int rowFont = GalleryUiMetrics.ScaledFontSize(
                GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);

            // Build into scratch (no full-copy + RemoveAt reverse walk).
            _userTagEditorBuildScratch.Clear();
            string filt = _detailStripTagMenuFilter;
            if (string.IsNullOrEmpty(filt) && _userTagEditorFilterInput != null)
                filt = _userTagEditorFilterInput.text ?? "";
            bool hasFilt = !string.IsNullOrEmpty(filt);
            int srcCount = cachedUserTagSideTab.Count;
            for (int i = 0; i < srcCount; i++)
            {
                UserTagSideTabEntry e = cachedUserTagSideTab[i];
                if (hasFilt && (e.Name == null || e.Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                _userTagEditorBuildScratch.Add(e);
            }

            switch (_userTagEditorSortMode)
            {
                case 1:
                    _userTagEditorBuildScratch.Sort(UserTagEditorSortNameDesc);
                    break;
                case 2:
                    _userTagEditorBuildScratch.Sort(UserTagEditorSortCountDesc);
                    break;
                case 3:
                    _userTagEditorBuildScratch.Sort(UserTagEditorSortCountAsc);
                    break;
                default:
                    _userTagEditorBuildScratch.Sort(UserTagEditorSortNameAsc);
                    break;
            }

            int matchCount = _userTagEditorBuildScratch.Count;
            int showCount = matchCount;
            if (showCount > UserTagEditorMaxVisibleRows)
                showCount = UserTagEditorMaxVisibleRows;

            Color baseCol = new Color(0.2f, 0.2f, 0.22f, 1f);
            Color selCol = new Color(0.28f, 0.38f, 0.32f, 1f);
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;

            _userTagEditorRowBaseCol = baseCol;
            _userTagEditorRowSelCol = selCol;
            _userTagEditorVisibleRows.Clear();
            _userTagEditorRowVisuals.Clear();

            for (int ri = 0; ri < showCount; ri++)
            {
                UserTagSideTabEntry e = _userTagEditorBuildScratch[ri];
                string nameSnap = e.Name;
                _userTagEditorVisibleRows.Add(e);

                string label = nameSnap + " (" + e.Count.ToString() + ")";

                GameObject rowGo = new GameObject("EditorTagRow");
                rowGo.transform.SetParent(_userTagEditorRowsParent, false);
                bool sel = _userTagEditorRowSelection.Contains(nameSnap);
                Image bg = UI.AddGalleryElementRoundedBg(rowGo, sel ? selCol : baseCol);
                _userTagEditorRowVisuals.Add(new UserTagEditorRowVisual { Name = nameSnap, Bg = bg });
                UI.AddLE(rowGo, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);

                Button btn = rowGo.AddComponent<Button>();
                btn.targetGraphic = bg;
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                btn.colors = cb;
                btn.transition = Selectable.Transition.None;
                int riCapture = ri;
                string nameCapture = nameSnap;
                // Capture index + name only; shift-range reads live _userTagEditorVisibleRows.
                btn.onClick.AddListener(() => UserTagEditorOnRowClicked(
                    nameCapture, riCapture, _userTagEditorVisibleRows, bg, baseCol, selCol));

                Color? catColor = TryGetUserTagCategoryColor(nameSnap);
                float swSize = rowH * 0.46f;
                GameObject swGo = new GameObject("CatSwatch");
                swGo.transform.SetParent(rowGo.transform, false);
                UI.AddImage(swGo, catColor.HasValue ? catColor.Value : new Color(1f, 1f, 1f, 0.06f), false);
                RectTransform swRt = swGo.GetComponent<RectTransform>();
                swRt.anchorMin = new Vector2(0f, 0.5f);
                swRt.anchorMax = new Vector2(0f, 0.5f);
                swRt.pivot = new Vector2(0f, 0.5f);
                swRt.sizeDelta = new Vector2(swSize, swSize);
                swRt.anchoredPosition = new Vector2(12f * s, 0f);

                float labelLeft = 12f * s + swSize + 8f * s;
                Text txt = UI.CreateLabel(rowGo, label, rowFont, new Color(0.93f, 0.93f, 0.95f, 1f), TextAnchor.MiddleLeft, name: "Label");
                GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                RectTransform trt = txt.GetComponent<RectTransform>();
                trt.offsetMin = new Vector2(labelLeft, 2f * s);
                trt.offsetMax = new Vector2(-14f * s, -2f * s);
            }

            int skipped = matchCount - showCount;
            if (skipped > 0)
            {
                GameObject moreGo = new GameObject("EditorTagOverflow");
                moreGo.transform.SetParent(_userTagEditorRowsParent, false);
                UI.AddLE(moreGo, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);
                string moreLabel = string.Format(
                    VPBTranslation.T("gallery.usertags.editor_overflow_fmt", "+{0} more — narrow filter"),
                    skipped);
                Text moreTxt = UI.CreateLabel(
                    moreGo, moreLabel, rowFont, UI.PopupMutedText, TextAnchor.MiddleCenter, name: "Label");
                GalleryUiMetrics.ApplyFont(moreTxt, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                RectTransform mrt = moreTxt.GetComponent<RectTransform>();
                if (mrt != null)
                {
                    mrt.offsetMin = new Vector2(8f * s, 0f);
                    mrt.offsetMax = new Vector2(-8f * s, 0f);
                }
            }
        }
    }

    internal static class UserTagDragSession
    {
        public static List<string> PendingTags;
        /// <summary>True when dragging from Applied rows — apply drop zones must ignore <see cref="IDropHandler.OnDrop"/>.</summary>
        public static bool PendingIsAppliedRowRemove;
        /// <summary>Index into panel title-search chips while dragging a committed chip (Incl↔Excl). −1 = none.</summary>
        public static int PendingTitleSearchChipIndex = -1;
        public static GalleryPanel PendingTitleSearchChipPanel;
        /// <summary>While chip-move drag active: whether Exclude drop is valid.</summary>
        public static bool PendingTitleSearchChipCanExclude;

        public static bool HasPendingTags
        {
            get { return PendingTags != null && PendingTags.Count > 0; }
        }

        /// <summary>Side/detail tag drag that may land on title-search Incl/Excl (not Applied-remove).</summary>
        public static bool IsTitleSearchTagDropActive
        {
            get { return HasPendingTags && !PendingIsAppliedRowRemove; }
        }

        public static bool IsTitleSearchChipMoveActive
        {
            get { return PendingTitleSearchChipIndex >= 0 && PendingTitleSearchChipPanel != null; }
        }

        public static bool IsTitleSearchDropActive
        {
            get { return IsTitleSearchTagDropActive || IsTitleSearchChipMoveActive; }
        }

        public static void Clear()
        {
            PendingTags = null;
            PendingIsAppliedRowRemove = false;
            PendingTitleSearchChipIndex = -1;
            PendingTitleSearchChipPanel = null;
            PendingTitleSearchChipCanExclude = false;
        }
    }

    internal sealed class UserTagPickDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public GalleryPanel Panel;
        public string PrimaryTag;
        /// <summary>When true, drag originates from Applied list — drop targets remove control only (not apply zones).</summary>
        public bool IsAppliedRowDrag;
        /// <summary>Quick-tagger Applied column: same pick-drag can reorder in-list via insert line.</summary>
        public bool DetailStripAppliedReorder;
        private CanvasGroup _cg;
        private bool _pressed;
        private bool _dragging;
        private Vector2 _pressPos;
        private Vector2 _lastScreenPos;
        private Camera _pressEventCamera;
        private float _pressTime;
        private GameObject _ghost;
        private Text _ghostText;
        private RectTransform _ghostRT;
        private Canvas _rootCanvas;
        private readonly List<RaycastResult> _raycastHits = new List<RaycastResult>(16);
        public bool ConsumedByDrag { get; private set; }
        private bool _releaseProcessed;
        /// <summary>Desktop: small screen slack past press. VR laser+world canvas barely moves screen px — skip (same as UIDraggableItem).</summary>
        private const float DesktopMinScreenPixelsForTagDrag = 10f;
        private const float VrHoldSecondsForTagDrag = 0.25f;

        private void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        private void OnDisable()
        {
            if (_pressed || _dragging)
                CleanupDragVisuals();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Panel == null) return;
            if (eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = true;
            _dragging = false;
            ConsumedByDrag = false;
            _releaseProcessed = false;
            _pressPos = eventData.position;
            _lastScreenPos = eventData.position;
            _pressEventCamera = eventData.pressEventCamera;
            _pressTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_pressed) return;
            if (!isActiveAndEnabled) return;

            _pressed = false;
            _releaseProcessed = true;
            if (_dragging)
            {
                if (eventData != null) _lastScreenPos = eventData.position;
                EndManualDrag(eventData);
            }
        }

        private void Update()
        {
            if (Panel == null) return;

            if (_dragging)
            {
                // Desktop Update-started drags may never get OnDrag — keep screen pos fresh.
                if (!XrUtils.IsVrActive())
                    _lastScreenPos = (Vector2)Input.mousePosition;
                UpdateGhostPosition();
                if (!IsAppliedRowDrag)
                {
                    bool reorderHint = false;
                    if (DetailStripAppliedReorder && Panel != null)
                    {
                        try { reorderHint = Panel.DetailStripUpdateAppliedReorderHint(PrimaryTag, _lastScreenPos); }
                        catch { reorderHint = false; }
                        try
                        {
                            bool overAvail = !reorderHint
                                && Panel.DetailStripScreenPosOverAvailableList(_lastScreenPos);
                            Panel.DetailStripSetTagMenuRemoveDragHint(true, overAvail);
                        }
                        catch { }
                    }
                    if (reorderHint)
                    {
                        try { Panel.dragHoverItem(null, null); } catch { }
                    }
                    else
                        RefreshUserTagApplyDragHoverStatus();
                }
                if (!_releaseProcessed && Input.GetMouseButtonUp(0))
                    EndManualDrag(null);
                return;
            }

            if (!_pressed) return;

            // VR: EventSystem OnBeginDrag starts drag (no extra pixel gate — laser barely moves).
            // Do not auto-start from hold alone (would steal long taps).
            if (XrUtils.IsVrActive()) return;

            Vector2 cur = Input.mousePosition;
            _lastScreenPos = cur;
            if ((cur - _pressPos).sqrMagnitude < DesktopMinScreenPixelsForTagDrag * DesktopMinScreenPixelsForTagDrag)
                return;

            BeginManualDrag();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Panel == null) return;
            if (eventData != null)
            {
                _lastScreenPos = eventData.position;
                if (eventData.pressEventCamera != null)
                    _pressEventCamera = eventData.pressEventCamera;
            }

            // VR: hold only — pixel delta vs press unreliable (same note as UIDraggableItem).
            if (XrUtils.IsVrActive())
            {
                if (Time.unscaledTime - _pressTime < VrHoldSecondsForTagDrag) return;
            }

            BeginManualDrag();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null) _lastScreenPos = eventData.position;
            if (_dragging) UpdateGhostPosition();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            if (eventData != null) _lastScreenPos = eventData.position;
            EndManualDrag(eventData);
        }

        private void BeginManualDrag()
        {
            if (Panel == null) return;
            if (_dragging) return;

            var list = new List<string>();
            if (IsAppliedRowDrag)
                Panel.UserTagAppliedDragBeginPayload(PrimaryTag, list);
            else
                Panel.UserTagPickDragBeginPayload(PrimaryTag, list);
            if (list.Count == 0) return;

            UserTagDragSession.PendingTags = list;
            // Applied-column quick-tagger: treat as remove-mode so strip/gallery apply zones ignore the drag.
            UserTagDragSession.PendingIsAppliedRowRemove = IsAppliedRowDrag || DetailStripAppliedReorder;
            _dragging = true;
            ConsumedByDrag = true;

            if (_cg != null)
            {
                _cg.alpha = 0.65f;
                _cg.blocksRaycasts = false;
            }

            CreateGhostLabel(list);
            UpdateGhostPosition();
            if (DetailStripAppliedReorder && Panel != null)
            {
                try { Panel.DetailStripSetTagMenuRemoveDragHint(true, false); } catch { }
            }
            if (!UserTagDragSession.PendingIsAppliedRowRemove && Panel != null)
            {
                try { Panel.TitleSearchOnExternalTagDragBegan(); } catch { }
            }
        }

        private void EndManualDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            if (IsAppliedRowDrag)
                TryDropToRemoveZone(eventData);
            else
                TryDropToApplyZone(eventData);
            CleanupDragVisuals();
        }

        private Vector2 ResolveDropScreenPos(PointerEventData eventData)
        {
            if (eventData != null) return eventData.position;
            return _lastScreenPos;
        }

        private void TryDropToRemoveZone(PointerEventData eventData)
        {
            TryDropToRemoveZoneAt(ResolveDropScreenPos(eventData), eventData);
        }

        private bool TryDropToRemoveZoneAt(Vector2 screenPos, PointerEventData eventData)
        {
            if (Panel == null) return false;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return false;

            EventSystem es = EventSystem.current;
            if (es == null) return false;

            var ped = eventData ?? new PointerEventData(es);
            ped.position = screenPos;
            _raycastHits.Clear();
            es.RaycastAll(ped, _raycastHits);
            for (int i = 0; i < _raycastHits.Count; i++)
            {
                GameObject go = _raycastHits[i].gameObject;
                if (go == null) continue;
                UserTagRemoveDropZone dz = go.GetComponentInParent<UserTagRemoveDropZone>();
                if (dz != null && dz.Panel == Panel)
                {
                    Panel.UserTagRemoveDroppedTags(tags);
                    return true;
                }
            }
            return false;
        }

        private void TryDropToApplyZone(PointerEventData eventData)
        {
            if (Panel == null) return;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return;

            EventSystem es = EventSystem.current;
            Vector2 screenPos = ResolveDropScreenPos(eventData);

            // Quick-tagger Applied: reorder in-list, or drop on Available → remove.
            if (DetailStripAppliedReorder)
            {
                try
                {
                    if (Panel.DetailStripTryCommitAppliedReorder(PrimaryTag, screenPos))
                        return;
                }
                catch { }
                TryDropToRemoveZoneAt(screenPos, eventData);
                return;
            }

            if (es == null)
            {
                Panel.UserTagApplyDroppedTags(tags);
                return;
            }

            var ped = eventData ?? new PointerEventData(es);
            ped.position = screenPos;
            _raycastHits.Clear();
            es.RaycastAll(ped, _raycastHits);

            // Front→back: title-search / apply zones / floating tag menu beat gallery behind them.
            for (int i = 0; i < _raycastHits.Count; i++)
            {
                GameObject go = _raycastHits[i].gameObject;
                if (go == null) continue;

                if (!UserTagDragSession.PendingIsAppliedRowRemove)
                {
                    TitleSearchChipDropZone tdz = go.GetComponentInParent<TitleSearchChipDropZone>();
                    if (tdz != null && tdz.Panel == Panel)
                    {
                        Panel.TitleSearchAcceptDroppedTags(tags, tdz.TargetPolarity);
                        return;
                    }
                }

                UserTagApplyDropZone dz = go.GetComponentInParent<UserTagApplyDropZone>();
                if (dz != null && dz.Panel == Panel)
                {
                    Panel.UserTagApplyDroppedTags(tags);
                    return;
                }

                // Tag manager / drop chrome in front of grid — apply to selection, never pierce.
                if (Panel.IsUserTagDropOccluder(go))
                {
                    Panel.UserTagApplyDroppedTags(tags);
                    return;
                }
            }

            // Gallery row only when nothing above occludes it.
            if (GalleryPanel.TryResolveGalleryRowFromRaycastHits(Panel, _raycastHits, out FileEntry galleryRow))
            {
                Panel.UserTagApplyDroppedTagsRespectingGalleryRow(tags, galleryRow);
                return;
            }

            // Fallback: drop anywhere inside this panel's canvas applies (selection-targeted).
            try
            {
                if (Panel.canvas != null)
                {
                    for (int i = 0; i < _raycastHits.Count; i++)
                    {
                        GameObject go = _raycastHits[i].gameObject;
                        if (go == null) continue;
                        if (go.transform.IsChildOf(Panel.canvas.transform))
                        {
                            Panel.UserTagApplyDroppedTags(tags);
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        private void RefreshUserTagApplyDragHoverStatus()
        {
            if (Panel == null) return;
            List<string> tags = UserTagDragSession.PendingTags;
            EventSystem es = EventSystem.current;
            if (tags == null || tags.Count == 0 || es == null)
            {
                Panel.dragHoverItem(null, tags);
                return;
            }
            var ped = new PointerEventData(es) { position = _lastScreenPos };
            _raycastHits.Clear();
            es.RaycastAll(ped, _raycastHits);
            if (!GalleryPanel.TryResolveGalleryRowFromRaycastHits(Panel, _raycastHits, out FileEntry rowHit))
            {
                Panel.dragHoverItem(null, tags);
                return;
            }
            Panel.dragHoverItem(rowHit, tags);
        }

        private void CleanupDragVisuals()
        {
            bool wasDragging = _dragging;
            _pressed = false;
            _dragging = false;
            _releaseProcessed = false;
            // Keep ConsumedByDrag until next PointerDown so Button onClick skips post-drag click.
            if (!wasDragging) ConsumedByDrag = false;
            try { if (Panel != null && !IsAppliedRowDrag) Panel.dragHoverItem(null, null); } catch { }
            try { if (Panel != null) Panel.DetailStripClearAppliedReorderHint(); } catch { }
            try { if (Panel != null) Panel.DetailStripSetTagMenuRemoveDragHint(false, false); } catch { }
            try { if (Panel != null) Panel.TitleSearchOnDragEnded(); } catch { }
            if (_cg != null)
            {
                _cg.alpha = 1f;
                _cg.blocksRaycasts = true;
            }
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            _ghostText = null;
            _ghostRT = null;
            _rootCanvas = null;
            UserTagDragSession.Clear();
        }

        private void CreateGhostLabel(List<string> tags)
        {
            Canvas root = null;
            try { root = GetComponentInParent<Canvas>(); } catch { }
            if (root == null && Panel != null) root = Panel.canvas;
            if (root == null) return;
            _rootCanvas = root;

            float s = 1f;
            try { if (Panel != null && Panel.ChromeScale > 0f) s = Panel.ChromeScale; } catch { }
            float pad = Mathf.Round(10f * s);

            _ghost = new GameObject("VPB_UserTagDragGhost");
            _ghost.layer = root.gameObject.layer;
            _ghostRT = _ghost.AddComponent<RectTransform>();
            _ghostRT.SetParent(root.transform, false);
            _ghostRT.anchorMin = new Vector2(0.5f, 0.5f);
            _ghostRT.anchorMax = new Vector2(0.5f, 0.5f);
            _ghostRT.pivot = new Vector2(0.5f, 0.5f);
            _ghostRT.sizeDelta = Vector2.zero;

            UI.AddImage(_ghost, new Color(0.12f, 0.12f, 0.14f, 0.94f), raycastTarget: false);
            UI.AddHLG(
                _ghost,
                spacing: 0f,
                padding: UI.Pad(pad, pad, pad * 0.7f, pad * 0.7f),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: false,
                childControlHeight: false,
                childForceExpandWidth: false,
                childForceExpandHeight: false);
            ContentSizeFitter rootCsf = _ghost.AddComponent<ContentSizeFitter>();
            rootCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string label = tags.Count == 1
                ? (VPBTranslation.T("gallery.usertags.drag_ghost_one", "Tag: ") + tags[0])
                : string.Format(VPBTranslation.T("gallery.usertags.drag_ghost_many", "Tags: {0}"), tags.Count);
            _ghostText = UI.CreateLabel(
                _ghost,
                label,
                GalleryUiDesignTokens.FontRef,
                UI.PopupText,
                TextAnchor.MiddleCenter,
                HorizontalWrapMode.Overflow,
                VerticalWrapMode.Overflow,
                raycastTarget: false,
                richText: true,
                anchorPreset: AnchorPresets.middleCenter);
            if (_ghostText != null)
            {
                try { GalleryUiMetrics.ApplyFont(_ghostText, GalleryUiDesignTokens.FontRef, s, GalleryUiDesignTokens.FontMinRef); }
                catch { }
                _ghostText.alignment = TextAnchor.MiddleCenter;
                _ghostText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _ghostText.verticalOverflow = VerticalWrapMode.Overflow;
                ContentSizeFitter textCsf = _ghostText.gameObject.GetComponent<ContentSizeFitter>();
                if (textCsf == null) textCsf = _ghostText.gameObject.AddComponent<ContentSizeFitter>();
                textCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                textCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                // Stretch insets from CreateLabel fight preferred-size fit — clear them.
                RectTransform trt = _ghostText.rectTransform;
                if (trt != null)
                {
                    trt.anchorMin = new Vector2(0.5f, 0.5f);
                    trt.anchorMax = new Vector2(0.5f, 0.5f);
                    trt.pivot = new Vector2(0.5f, 0.5f);
                    trt.anchoredPosition = Vector2.zero;
                    trt.sizeDelta = Vector2.zero;
                }
            }

            try { LayoutRebuilder.ForceRebuildLayoutImmediate(_ghostRT); } catch { }
        }

        private void UpdateGhostPosition()
        {
            if (_ghostRT == null) return;
            Canvas root = _rootCanvas;
            if (root == null)
            {
                _ghostRT.SetAsLastSibling();
                return;
            }

            RectTransform parent = root.transform as RectTransform;
            if (parent == null)
            {
                _ghostRT.SetAsLastSibling();
                return;
            }

            // Overlay: cam null. World/floating: press camera or canvas worldCamera.
            Camera cam = null;
            if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = _pressEventCamera;
                if (cam == null)
                    cam = root.worldCamera != null ? root.worldCamera : Camera.main;
            }

            Vector3 world;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, _lastScreenPos, cam, out world))
                _ghostRT.position = world;
            _ghostRT.SetAsLastSibling();
        }
    }

    internal sealed class UserTagApplyDropZone : MonoBehaviour, IDropHandler
    {
        public GalleryPanel Panel;

        public void OnDrop(PointerEventData eventData)
        {
            if (Panel == null) return;
            if (UserTagDragSession.PendingIsAppliedRowRemove) return;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return;
            Panel.UserTagApplyDroppedTags(tags);
            UserTagDragSession.Clear();
        }
    }

    internal sealed class UserTagRemoveDropZone : MonoBehaviour, IDropHandler
    {
        public GalleryPanel Panel;

        public void OnDrop(PointerEventData eventData)
        {
            if (Panel == null) return;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return;
            Panel.UserTagRemoveDroppedTags(tags);
            UserTagDragSession.Clear();
        }
    }

    internal sealed class UserTagDropRaycastGate : MonoBehaviour, ICanvasRaycastFilter, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Image;
        private const float HoverAlpha = 0.05f;

        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
        {
            return UserTagDragSession.HasPendingTags;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Image == null) return;
            if (!UserTagDragSession.HasPendingTags) return;
            Image.color = new Color(1f, 1f, 1f, HoverAlpha);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Image == null) return;
            Image.color = new Color(1f, 1f, 1f, 0.0f);
        }
    }
}
