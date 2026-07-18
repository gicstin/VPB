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
            if (_userTagAvailMode != UserTagAvailMode.FilterByTags) return;
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

        /// <summary>Must match EnsureUserTagSideTabBulkBlock padding, spacing, and title/font scale (s).</summary>
        private float UserTagsAvailStickyHeightPx()
        {
            float s = ChromeScale;
            int fs = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            float padTop = Mathf.RoundToInt(4f * s);
            float padBottom = Mathf.RoundToInt(10f * s);
            // LayoutElement preferred 34*s is short when font is ~19*u; keep viewport/shrink in sync.
            float titleBand = Mathf.Max(34f * s, fs * 1.22f);
            return padTop + titleBand + 7f * s + 36f * s + padBottom;
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

            float h = UserTagsAvailStickyHeightPx();
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

            float topChrome = h + pinnedH;
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
                    vp.offsetMin = new Vector2(defMin.x, defMin.y + fh);

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
            t.text = string.Format(VPBTranslation.T("gallery.usertags.tags_with_count", "Tags ({0})"), n);
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

        private void ApplyUserTagDropVisual(GameObject btnGO, FileEntry file)
        {
            if (btnGO == null || !IsUserTagDropVisualActive(file)) return;
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
            if (recyclingGrid != null && recyclingGrid.content != null)
            {
                foreach (Transform child in recyclingGrid.content)
                {
                    if (child == null || !child.gameObject.activeSelf) continue;
                    FileEntry fe = ResolveVisibleFileEntryFromButton(child.gameObject);
                    if (fe == null) continue;
                    string key = GetSelectionIdentityKey(fe, false);
                    if (string.Equals(key, wantedKey, StringComparison.OrdinalIgnoreCase))
                        UpdateFileButtonVisuals(child.gameObject, fe);
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

        private FileEntry ResolveVisibleFileEntryFromButton(GameObject btn)
        {
            if (btn == null) return null;
            var diag = btn.GetComponent<UIDraggableItem>();
            var rgvItem = btn.GetComponent<RecyclingGridItem>();
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
            string pickTip = _userTagAvailMode == UserTagAvailMode.FilterByTags
                ? GetUserTagPickRowTooltipFilter()
                : VPBTranslation.T("gallery.usertags.pick_row_tooltip", "Click: toggle this tag on selected item(s). Drag to Applied below.");
            float rowH = UserTagPinnedRowHeightPx();
            float s = ChromeScale;

            for (int ri = 0; ri < count; ri++)
            {
                UserTagSideTabEntry ut = _userTagStickyRows[ri];
                GameObject btnGO = UI.CreateUIButton(strip, 170, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                AddHoverDelegate(btnGO);
                LayoutElement le = btnGO.GetComponent<LayoutElement>();
                if (le == null) le = btnGO.AddComponent<LayoutElement>();
                le.minHeight = rowH;
                le.preferredHeight = rowH;
                le.flexibleWidth = 1f;
                BindUserTagVirtButton(btnGO, ut, utAccent, pickTip, isLeft);
            }

            try { LayoutRebuilder.ForceRebuildLayoutImmediate(strip.GetComponent<RectTransform>()); } catch { }
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
            if (viewportH < rowH * 2f)
                viewportH = rowH * 10f;
            return viewportH;
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

        private IEnumerator CoUserTagVirtLayoutRefresh(bool isLeft, Transform tabContainer, bool resetScrollToTop, float preserveOffsetPx)
        {
            yield return null;
            if (!IsUserTagsSideTabOpen(isLeft))
            {
                _userTagVirtLayoutCo = null;
                yield break;
            }
            try
            {
                Canvas.ForceUpdateCanvases();
                if (resetScrollToTop)
                {
                    ScrollRect sr = GetUserTagAvailScrollRect(isLeft);
                    if (sr != null) sr.verticalNormalizedPosition = 1f;
                }
                else
                    ApplyUserTagAvailScrollOffsetPx(isLeft, preserveOffsetPx);
                if (tabContainer != null)
                    UpdateUserTagVirtualVisible(isLeft, UserTagStateOnColor, tabContainer);
                ApplyUserTagsStickyScrollChrome(TabScrollTopOffset());
            }
            catch { }
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

        /// <summary>Shared by tag-drag hover hint and drop: first gallery file row hit (this panel).</summary>
        internal static bool TryResolveGalleryRowFromRaycastHits(GalleryPanel panel, IList<RaycastResult> hits, out FileEntry file)
        {
            file = null;
            if (panel == null || hits == null) return false;
            for (int i = 0; i < hits.Count; i++)
            {
                GameObject go = hits[i].gameObject;
                if (go == null) continue;
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
            if (_userTagAvailMode != UserTagAvailMode.FilterByTags || activeUserTags == null || activeUserTags.Count == 0)
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

            bool filterModeRemove = remove && _userTagAvailMode == UserTagAvailMode.FilterByTags;

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

            int titleFs = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, s, GalleryUiDesignTokens.FontMinRef);
            float miniSq = 34f * s;
            float titleBand = Mathf.Max(miniSq, Mathf.Max(30f * s, titleFs * 1.22f));

            GameObject titleRow = UI.CreateChildRT(root, "TagsTitleRow");
            UI.AddHLG(titleRow, 5f * s, childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: false);
            LayoutElement titleRowLe = UI.AddLE(titleRow, minHeight: titleBand, preferredHeight: titleBand, flexibleWidth: 1f);

            Text titleTxt = UI.CreateLabel(titleRow, string.Format(VPBTranslation.T("gallery.usertags.tags_with_count", "Tags ({0})"), 0),
                titleFs, Color.white, TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, name: "BulkTitle");
            LayoutElement titleLe = UI.AddLE(titleTxt.gameObject, minHeight: titleBand, preferredHeight: titleBand, flexibleWidth: 1f);

            CreateUserTagModeMiniButton(titleRow, "F", UserTagAvailMode.FilterByTags, miniSq, s,
                VPBTranslation.T("gallery.usertags.mini_filter_tip", "Filter Mode: grid shows items matching selected tags."));
            CreateUserTagModeMiniButton(titleRow, "N", UserTagAvailMode.FilterUntagged, miniSq, s,
                VPBTranslation.T("gallery.usertags.mini_untagged_tip", "Not Tagged: grid shows only items with no user tags."));
            CreateUserTagModeMiniButton(titleRow, "T", UserTagAvailMode.Tag, miniSq, s,
                VPBTranslation.T("gallery.usertags.mini_tag_tip", "Tag Mode: click tags to apply them to the selection."));

            // false: only Apply (flexibleWidth 1) grows; true spreads width across all children and crushes the label.
            GameObject btnRow = UI.CreateChildRT(root, "BulkBtnRow");
            UI.AddHLG(btnRow, 6f * s, childForceExpandWidth: false);
            LayoutElement rowLe = UI.AddLE(btnRow, minHeight: 34f * s, preferredHeight: 36f * s, flexibleWidth: 1f);

            float editSq = 36f * s;
            Sprite editSpr = UI.LoadIconSprite("vpb_icons/edit.png", new Color(0.88f, 0.88f, 0.9f, 1f));
            Color editBackdrop = new Color(0.38f, 0.26f, 0.52f, 1f);
            GameObject editBtn = UI.CreateSideTabSquareIconButton(btnRow, editSq, editSpr, ShowUserTagListEditor, editBackdrop, 8f * s);
            editBtn.name = "VPB_UserTagEditBtn";
            AddTooltipPlain(editBtn, VPBTranslation.T("gallery.usertags.btn_edit_tooltip", "Open tag database editor: search, sort, create/remove/merge tags."));
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
                string tip = BuildUserTagInheritVarToChildrenTip();
                if (enter)
                {
                    if (temporaryStatusCoroutine != null)
                    {
                        StopCoroutine(temporaryStatusCoroutine);
                        temporaryStatusCoroutine = null;
                    }
                    temporaryStatusMsg = tip;
                    temporaryStatusOwner = btnGo;
                }
                else if (temporaryStatusOwner == btnGo)
                {
                    temporaryStatusMsg = null;
                    temporaryStatusOwner = null;
                }
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

            Transform btnRow = bulkBlockV3.Find("BulkBtnRow");
            if (btnRow == null) return;

            float s = ChromeScale;
            float sq = 36f * s;

            Transform filterT = btnRow.Find("VPB_UserTagFilterModeBtn");
            if (filterT != null) UnityEngine.Object.Destroy(filterT.gameObject);
            Transform applyT = btnRow.Find("VPB_UserTagApplyBtn");
            if (applyT != null) UnityEngine.Object.Destroy(applyT.gameObject);

            if (btnRow.Find("VPB_UserTagEditBtn") == null)
            {
                Sprite editSpr = UI.LoadIconSprite("vpb_icons/edit.png", new Color(0.88f, 0.88f, 0.9f, 1f));
                GameObject editGo = UI.CreateSideTabSquareIconButton(btnRow.gameObject, sq, editSpr, ShowUserTagListEditor, new Color(0.38f, 0.26f, 0.52f, 1f), 8f * s);
                editGo.name = "VPB_UserTagEditBtn";
                AddTooltipPlain(editGo, VPBTranslation.T("gallery.usertags.btn_edit_tooltip", "Open tag database editor: search, sort, create/remove/merge tags."));
            }

            GameObject filterGo = UI.CreateUIButton(
                btnRow.gameObject,
                0f,
                0f,
                VPBTranslation.T("gallery.usertags.filter_button_label", "Filter"),
                GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef),
                0f,
                0f,
                AnchorPresets.stretchAll,
                OnUserTagAvailFilterModeClicked);
            filterGo.name = "VPB_UserTagFilterModeBtn";
            AddUserTagFilterButtonIconAndLabel(filterGo, s);
            AddTooltipPlain(filterGo, VPBTranslation.T("gallery.usertags.filter_mode_toggle_tip", "Cycles: Tag Mode (apply tags), Filter Mode (grid matches selected tags), Not Tagged (grid shows only items with no user tags)."));
            filterGo.transform.SetSiblingIndex(Mathf.Min(1, filterGo.transform.GetSiblingIndex()));

            HorizontalLayoutGroup hlg = btnRow.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth = false;
                hlg.padding = new RectOffset(0, 0, 0, 0);
            }

            Transform editT = btnRow.Find("VPB_UserTagEditBtn");
            LayoutElement le = editT != null ? editT.GetComponent<LayoutElement>() : null;
            if (le != null)
            {
                le.flexibleWidth = 0f;
                le.minWidth = sq;
                le.preferredWidth = sq;
            }
            LayoutElement fle = filterGo.GetComponent<LayoutElement>();
            if (fle == null) fle = filterGo.AddComponent<LayoutElement>();
            fle.flexibleWidth = 1f;
            fle.minWidth = 120f * s;
            fle.preferredWidth = 0f;
            fle.minHeight = sq;
            fle.preferredHeight = sq;
            SyncUserTagFilterModeToggleVisualSticky(bulkBlockV3);
        }

        private void AddUserTagFilterButtonIconAndLabel(GameObject filterGo, float s)
        {
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
                    // Mirror the icon inset on both sides so the label is visually centered
                    // in the button while never overlapping the icon on the left.
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
                ? VPBTranslation.T("gallery.usertags.pick_row_tooltip_filter_all", "Tap to cycle: include (all must match) \u2192 exclude (hide items with this tag) \u2192 off. Drag to Applied below.")
                : VPBTranslation.T("gallery.usertags.pick_row_tooltip_filter_any", "Tap to cycle: include (any can match) \u2192 exclude (hide items with this tag) \u2192 off. Drag to Applied below.");
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
            if (!userTagsCached) return false;
            if (ut.Count == int.MinValue) return false;
            if (_userTagSelectionRowCount > 0 && !string.IsNullOrEmpty(ut.Name))
            {
                UserTagSelectionState st = GetUserTagSelectionState(ut.Name);
                if (st != UserTagSelectionState.Off) return false;
            }
            return ut.Count <= 0;
        }

        /// <summary>When User Tags side panel opens, apply configured default mode (filter / apply / untagged).</summary>
        internal void ApplyDefaultUserTagAvailModeOnTagsPanelOpen()
        {
            UserTagAvailMode mode = ResolveDefaultUserTagAvailMode();
            if (_userTagAvailMode == mode)
            {
                try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                return;
            }
            UserTagAvailMode prev = _userTagAvailMode;
            _userTagAvailMode = mode;
            if (prev == UserTagAvailMode.FilterUntagged || _userTagAvailMode == UserTagAvailMode.FilterUntagged)
                try { ClearUntaggedTaggedPinKeys(); } catch { }
            try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
            try { RefreshFiles(true, false, false, null); } catch { }
        }

        private void OnUserTagAvailFilterModeClicked()
        {
            SetUserTagAvailMode((UserTagAvailMode)(((int)_userTagAvailMode + 1) % 3));
        }

        private void SetUserTagAvailMode(UserTagAvailMode mode)
        {
            UserTagAvailMode prev = _userTagAvailMode;
            if (prev == mode) return;
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
            try { RefreshFiles(true, false, false, null); } catch { }
            try { UpdateTabs(); } catch { }
        }

        /// <summary>Backdrop colour for each tag-availability mode (shared by the toggle and the F/N/T mini buttons).</summary>
        private Color UserTagAvailModeColor(UserTagAvailMode mode)
        {
            switch (mode)
            {
                case UserTagAvailMode.Tag: return new Color(0.20f, 0.50f, 0.25f, 1f);
                case UserTagAvailMode.FilterByTags: return new Color(0.18f, 0.38f, 0.62f, 1f);
                default: return new Color(0.45f, 0.32f, 0.14f, 1f);
            }
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
            btn.onClick.AddListener(() => SetUserTagAvailMode(target));
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
            SyncUserTagModeMiniButton(row, UserTagAvailMode.FilterByTags);
            SyncUserTagModeMiniButton(row, UserTagAvailMode.FilterUntagged);
            SyncUserTagModeMiniButton(row, UserTagAvailMode.Tag);
        }

        private void SyncUserTagModeMiniButton(Transform row, UserTagAvailMode mode)
        {
            Transform tr = row.Find("VPB_UserTagModeMiniBtn_" + mode);
            if (tr == null) return;
            bool active = _userTagAvailMode == mode;
            Color baseCol = UserTagAvailModeColor(mode);
            Color showCol = active ? baseCol : new Color(baseCol.r * 0.42f, baseCol.g * 0.42f, baseCol.b * 0.42f, 0.85f);
            EnsureUserTagSideChromeRoundedBg(tr.gameObject, showCol);
            Text lbl = tr.GetComponentInChildren<Text>(true);
            if (lbl != null)
                lbl.color = active ? Color.white : UI.TextMuted;
        }

        private void SyncUserTagFilterModeToggleVisualsEverywhere()
        {
            try { SyncUserTagFilterModeStickySide(leftUserTagsAvailStickyGO); } catch { }
            try { SyncUserTagFilterModeStickySide(rightUserTagsAvailStickyGO); } catch { }
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
            Transform btnRow = bulkBlockV3.Find("BulkBtnRow");
            if (btnRow == null) return;
            Transform filterBtn = btnRow.Find("VPB_UserTagFilterModeBtn");
            if (filterBtn == null) return;
            Transform iconTr = filterBtn.Find("Icon");
            Image iconImg = iconTr != null ? iconTr.GetComponent<Image>() : null;
            Color iconTint = new Color(0.88f, 0.88f, 0.9f, 1f);
            Sprite onSpr = UI.LoadIconSprite("vpb_icons/filter_on.png", iconTint);
            Sprite offSpr = UI.LoadIconSprite("vpb_icons/filter_off.png", iconTint);
            Sprite tagSpr = UI.LoadIconSprite("vpb_icons/gallery_tags.png", iconTint);
            if (tagSpr == null) tagSpr = UI.LoadIconSprite("vpb_icons/tags.png", iconTint);
            if (tagSpr == null) tagSpr = offSpr;
            if (iconImg != null)
            {
                if (_userTagAvailMode == UserTagAvailMode.FilterByTags)
                    iconImg.sprite = onSpr;
                else if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                    iconImg.sprite = offSpr != null ? offSpr : onSpr;
                else
                    iconImg.sprite = tagSpr;
                iconImg.color = Color.white;
            }
            Text label = filterBtn.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                if (_userTagAvailMode == UserTagAvailMode.FilterByTags)
                    label.text = VPBTranslation.T("gallery.usertags.filter_button_on_label", "Filter Mode");
                else if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                    label.text = VPBTranslation.T("gallery.usertags.not_tagged_mode_button_label", "Not Tagged");
                else
                    label.text = VPBTranslation.T("gallery.usertags.tag_mode_button_label", "Tag Mode");
            }
            Image bd = filterBtn.GetComponent<Image>();
            if (bd != null)
            {
                Color tagModeBackdrop = new Color(0.20f, 0.50f, 0.25f, 1f);
                Color filterModeBackdrop = new Color(0.18f, 0.38f, 0.62f, 1f);
                Color untaggedModeBackdrop = new Color(0.45f, 0.32f, 0.14f, 1f);
                if (_userTagAvailMode == UserTagAvailMode.FilterByTags)
                    bd.color = filterModeBackdrop;
                else if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                    bd.color = untaggedModeBackdrop;
                else
                    bd.color = tagModeBackdrop;
            }
        }

        private void ShowUserTagListEditor()
        {
            if (backgroundBoxGO == null) return;
            EnsureUserTagEditorUiBuilt();
            if (_userTagEditorRoot == null) return;
            _userTagEditorRowSelection.Clear();
            _userTagEditorAnchorTag = null;
            _userTagEditorSortMode = 0;
            if (_userTagEditorFilterInput != null) _userTagEditorFilterInput.text = "";
            if (_userTagEditorNewTagInput != null) _userTagEditorNewTagInput.text = "";
            if (_userTagEditorMergeModalGo != null) _userTagEditorMergeModalGo.SetActive(false);
            if (_userTagEditorMergeModalInput != null) _userTagEditorMergeModalInput.text = "";
            if (_userTagEditorRenameModalGo != null) _userTagEditorRenameModalGo.SetActive(false);
            if (_userTagEditorRenameModalInput != null) _userTagEditorRenameModalInput.text = "";
            _userTagEditorRenameSourcePrefix = null;
            CloseTagCategoryEditorModal();
            UserTagEditorSyncSortIcon();
            RebuildUserTagEditorRows();
            _userTagEditorRoot.SetActive(true);
            _userTagEditorRoot.transform.SetAsLastSibling();
        }

        private void HideUserTagListEditor()
        {
            CloseTagCategoryEditorModal();
            if (_userTagEditorRoot != null)
                _userTagEditorRoot.SetActive(false);
        }

        private void EnsureUserTagEditorUiBuilt()
        {
            if (_userTagEditorRoot != null && _userTagEditorRoot.name != "VPB_UserTagEditorOverlay")
            {
                UnityEngine.Object.Destroy(_userTagEditorRoot);
                _userTagEditorRoot = null;
                _userTagEditorRowsParent = null;
                _userTagEditorFilterInput = null;
                _userTagEditorNewTagInput = null;
                _userTagEditorNewTagInputChrome = null;
                UserTagEditorStopNewTagChromeFlash();
                _userTagEditorSortIconImage = null;
                _userTagEditorTitleText = null;
                _userTagEditorMergeModalGo = null;
                _userTagEditorMergeModalTitleText = null;
                _userTagEditorMergeModalInput = null;
                _userTagEditorRenameModalGo = null;
                _userTagEditorRenameModalTitleText = null;
                _userTagEditorRenameModalInput = null;
                _userTagEditorRenameSourcePrefix = null;
            }
            if (_userTagEditorRoot != null && _userTagEditorRoot.transform.Find("UserTagEditorPanel/TitleRow/TagsDbTitle") == null)
            {
                UnityEngine.Object.Destroy(_userTagEditorRoot);
                _userTagEditorRoot = null;
                _userTagEditorRowsParent = null;
                _userTagEditorFilterInput = null;
                _userTagEditorNewTagInput = null;
                _userTagEditorNewTagInputChrome = null;
                UserTagEditorStopNewTagChromeFlash();
                _userTagEditorSortIconImage = null;
                _userTagEditorTitleText = null;
                _userTagEditorMergeModalGo = null;
                _userTagEditorMergeModalTitleText = null;
                _userTagEditorMergeModalInput = null;
                _userTagEditorRenameModalGo = null;
                _userTagEditorRenameModalTitleText = null;
                _userTagEditorRenameModalInput = null;
                _userTagEditorRenameSourcePrefix = null;
            }
            if (_userTagEditorRoot != null) return;
            if (backgroundBoxGO == null) return;

            float s = ChromeScale;
            float u = s * 1.38f;
            GalleryModalTypography editorType = new GalleryModalTypography(u);
            int smallFont = editorType.Body;
            int bodyFont = editorType.Body;
            float headerChromeSq = Mathf.Max(30f, 36f * s);
            float searchBarH = headerChromeSq;

            GameObject panel;
            GameObject dim = UI.CreateModalChrome(
                backgroundBoxGO, "VPB_UserTagEditorOverlay", 620f * s, 720f * s,
                new Color(0.11f, 0.11f, 0.13f, 1f), HideUserTagListEditor, out panel, dimAlpha: 0.55f);
            panel.name = "UserTagEditorPanel";

            UI.AddVLG(panel, 8f * s, UI.Pad(14f, 14f, 12f, 12f, s));

            GameObject titleRow = UI.CreateChildRT(panel, "TitleRow");
            UI.AddHLG(titleRow, 0f, childAlignment: TextAnchor.UpperLeft);
            LayoutElement titleRowLe = UI.AddLE(titleRow, minHeight: Mathf.Max(26f * s, headerChromeSq * 0.72f));
            titleRowLe.preferredHeight = titleRowLe.minHeight;

            Text headerTitleTxt = UI.CreateLabel(titleRow, "", editorType.Prose, new Color(0.92f, 0.92f, 0.95f, 1f), name: "TagsDbTitle");
            _userTagEditorTitleText = headerTitleTxt;
            GameObject titleGo = headerTitleTxt.gameObject;
            LayoutElement titleHLe = UI.AddLE(titleGo, minHeight: titleRowLe.minHeight, flexibleWidth: 0f);
            ContentSizeFitter titleCsf = titleGo.AddComponent<ContentSizeFitter>();
            titleCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            titleCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject headerRow = UI.CreateChildRT(panel, "HeaderRow");
            UI.AddHLG(headerRow, 6f * s, childForceExpandWidth: false);
            LayoutElement hle = UI.AddLE(headerRow, minHeight: Mathf.Max(headerChromeSq, searchBarH), preferredHeight: headerChromeSq);

            float sortSq = headerChromeSq;
            Color sortBackdropCol = new Color(0.22f, 0.42f, 0.58f, 1f);
            Sprite sortSpr0 = sceneSourceSortModeSprites != null && sceneSourceSortModeSprites.Length > 0 ? sceneSourceSortModeSprites[0] : null;
            GameObject sortBtnGo = UI.CreateSideTabSquareIconButton(headerRow, sortSq, sortSpr0, UserTagEditorCycleSort, sortBackdropCol, 5f * s);
            sortBtnGo.name = "UserTagEditorSortBtn";
            Transform sortIconTr = sortBtnGo.transform.Find("Icon");
            _userTagEditorSortIconImage = sortIconTr != null ? sortIconTr.GetComponent<Image>() : null;
            UserTagEditorSyncSortIcon();
            AddTooltipPlain(sortBtnGo, VPBTranslation.T("gallery.usertags.editor_sort_cycle_tip", "Sort list: tap to cycle name A→Z / Z→A / count high→low / low→high."));

            _userTagEditorFilterInput = UI.CreateChromeLayoutInputField(
                headerRow.transform, bodyFont, searchBarH, 1f, 8f * s, 2f * s,
                new Color(0.07f, 0.07f, 0.09f, 1f), new Color(0.42f, 0.42f, 0.45f, 1f),
                VPBTranslation.T("gallery.usertags.editor_filter_ph", "Filter list…"), "FilterInput");
            Text filterPh = _userTagEditorFilterInput.placeholder as Text;
            if (filterPh != null) filterPh.alignment = TextAnchor.MiddleLeft;
            if (_userTagEditorFilterInput.textComponent != null)
                _userTagEditorFilterInput.textComponent.alignment = TextAnchor.MiddleLeft;
            LayoutElement fiLe = _userTagEditorFilterInput.GetComponent<LayoutElement>();
            if (fiLe != null) fiLe.minWidth = 0f;
            _userTagEditorFilterInput.onValueChanged.AddListener(_ => RebuildUserTagEditorRows());

            float clearSz = searchBarH;
            Sprite clearSpr = UI.LoadIconSprite("vpb_icons/clear_selection.png", new Color(0.78f, 0.78f, 0.78f, 1f));
            Color clearSearchBackdrop = new Color(0.44f, 0.36f, 0.20f, 1f);
            GameObject clearBtn = UI.CreateSideTabSquareIconButton(headerRow, clearSz, clearSpr, UserTagEditorClearFilter, clearSearchBackdrop, 5f * s);
            AddTooltipPlain(clearBtn, VPBTranslation.T("gallery.usertags.editor_clear_search_tip", "Clear filter text and highlighted row selection"));

            float closeSz = searchBarH;
            Sprite closeSpr = UI.LoadIconSprite("vpb_icons/close.png", new Color(0.9f, 0.9f, 0.9f, 1f));
            Color closeBackdrop = new Color(0.52f, 0.30f, 0.32f, 1f);
            GameObject closeBtn = UI.CreateSideTabSquareIconButton(headerRow, closeSz, closeSpr, HideUserTagListEditor, closeBackdrop, 6f * s);
            AddTooltipPlain(closeBtn, VPBTranslation.T("gallery.usertags.editor_close_tip", "Close"));

            GameObject scrollGO = UI.CreateVScrollableContent(panel, new Color(0, 0, 0, 0), AnchorPresets.stretchAll, 0f, 300f * s, Vector2.zero, 14f * s, 3f * s, false);
            LayoutElement scLe = UI.AddLE(scrollGO, minHeight: 280f * s, flexibleHeight: 1f);
            Transform vp = scrollGO.transform.Find("Viewport");
            _userTagEditorRowsParent = vp != null ? vp.Find("Content") : null;
            if (_userTagEditorRowsParent != null)
            {
                VerticalLayoutGroup rowsVlg = _userTagEditorRowsParent.GetComponent<VerticalLayoutGroup>();
                if (rowsVlg != null)
                {
                    int sidePad = Mathf.RoundToInt(8f * s);
                    rowsVlg.padding = new RectOffset(sidePad, sidePad, 0, 0);
                }
            }

            GameObject newTagBlock = UI.CreateChildRT(panel, "NewTagBlock");
            UI.AddVLG(newTagBlock, 4f * s);
            LayoutElement ntbLe = UI.AddLE(newTagBlock, minHeight: 152f * s, preferredHeight: 168f * s, flexibleWidth: 1f);

            GameObject newInGo = new GameObject("NewTagInput");
            newInGo.transform.SetParent(newTagBlock.transform, false);
            Image nBg = UI.AddImage(newInGo, UserTagEditorNewTagChromeBaseCol);
            _userTagEditorNewTagInputChrome = nBg;
            LayoutElement nLe = UI.AddLE(newInGo, minHeight: 160f * s, preferredHeight: 168f * s, flexibleWidth: 1f);
            GameObject nta = new GameObject("TextArea");
            nta.transform.SetParent(newInGo.transform, false);
            RectTransform ntaRt = nta.AddComponent<RectTransform>();
            ntaRt.anchorMin = Vector2.zero;
            ntaRt.anchorMax = Vector2.one;
            ntaRt.offsetMin = new Vector2(8f * s, 6f * s);
            ntaRt.offsetMax = new Vector2(-8f * s, -6f * s);
            Text nphT = UI.CreateLabel(nta, VPBTranslation.T("gallery.usertags.editor_new_ph", "Type or paste tag names here…"), bodyFont, new Color(0.42f, 0.42f, 0.45f, 1f), TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow, name: "Placeholder");
            Text ntxT = UI.CreateLabel(nta, "", bodyFont, Color.white, TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow);
            _userTagEditorNewTagInput = newInGo.AddComponent<InputField>();
            _userTagEditorNewTagInput.textComponent = ntxT;
            _userTagEditorNewTagInput.placeholder = nphT;
            _userTagEditorNewTagInput.lineType = InputField.LineType.MultiLineNewline;
            _userTagEditorNewTagInput.characterLimit = 0;
            FixMultilineHomeEndBehavior(_userTagEditorNewTagInput);

            string userTagEditorInfoWisdom =
                VPBTranslation.T(
                    "gallery.usertags.editor_paste_hint",
                    "Create-tag button: one tag per line (newline split). Blank lines ignored; trim line ends. Other fields may still use comma / tab / semicolon where noted.")
                + "\n"
                + VPBTranslation.T(
                    "gallery.usertags.editor_tag_rules_hint",
                    "Each tag: 1–512 characters; unicode / emoji / punctuation / spaces allowed. No line breaks inside name; control chars rejected (tab allowed). Names fold lowercase for dedupe.")
                + "\n"
                + VPBTranslation.T(
                    "gallery.usertags.editor_limits_hint",
                    "Up to 10 000 distinct names per paste; library holds up to 10 000 tag names; each item may have up to 100 tags.");

            float actSq = GalleryUiDesignTokens.GoldenRatio * headerChromeSq;

            GameObject actionRow = new GameObject("ActionRow");
            actionRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup arH = UI.AddHLG(actionRow, spacing: 8f * s, childAlignment: TextAnchor.MiddleCenter, childForceExpandWidth: false);
            LayoutElement arLe = UI.AddLE(actionRow, minHeight: actSq + 4f * s, preferredHeight: actSq + 4f * s);

            GameObject arPadL = new GameObject("PadL");
            arPadL.transform.SetParent(actionRow.transform, false);
            LayoutElement arPadLle = UI.AddLE(arPadL, minWidth: 0f, flexibleWidth: 1f);

            Color createCol = new Color(0.25f, 0.45f, 0.28f, 1f);
            Color removeCol = new Color(0.45f, 0.22f, 0.22f, 1f);
            Color mergeCol = new Color(0.22f, 0.38f, 0.55f, 1f);
            Color ioImpCol = new Color(0.24f, 0.40f, 0.35f, 1f);
            Color ioExpCol = new Color(0.24f, 0.32f, 0.48f, 1f);
            Sprite sprPlus = UI.LoadIconSprite("vpb_icons/tag_plus.png", Color.white);
            Sprite sprMinus = UI.LoadIconSprite("vpb_icons/tag_minus.png", Color.white);
            Sprite sprMerge = UI.LoadIconSprite("vpb_icons/arrow_merge.png", Color.white);
            Sprite sprImp = UI.LoadIconSprite("vpb_icons/file_import.png", Color.white);
            Sprite sprExp = UI.LoadIconSprite("vpb_icons/file_export.png", Color.white);
            Sprite sprInfo = UI.LoadIconSprite("vpb_icons/info_square.png", Color.white);
            GameObject createTagsBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprPlus, UserTagEditorOnCreateTagsClicked, createCol, 10f * s);
            AddTooltipPlain(createTagsBtn, VPBTranslation.T("gallery.usertags.editor_create_tags_tip", "Create tag rows: one tag per line (newline split). Blank lines skipped; trim line ends."));
            GameObject removeSelBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprMinus, UserTagEditorRemoveSelectedFromDb, removeCol, 10f * s);
            AddTooltipPlain(removeSelBtn, VPBTranslation.T("gallery.usertags.editor_remove_selected_tip", "Delete selected tag(s) from the database for all items (cannot undo)."));
            GameObject mergeBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprMerge, UserTagEditorOpenMergeDialog, mergeCol, 10f * s);
            AddTooltipPlain(mergeBtn, VPBTranslation.T("gallery.usertags.editor_merge_tip", "Merge selected tags into one name (opens confirmation)."));
            Color renameCol = new Color(0.26f, 0.34f, 0.46f, 1f);
            Sprite sprRename = UI.LoadIconSprite("vpb_icons/rename.png", Color.white);
            GameObject renameBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprRename, UserTagEditorOpenRenameDialog, renameCol, 10f * s);
            AddTooltipPlain(renameBtn, VPBTranslation.T("gallery.usertags.editor_rename_tip", "Rename the selected tag (opens dialog)."));
            Color catCol = new Color(0.42f, 0.34f, 0.55f, 1f);
            Sprite sprCat = UI.LoadIconSprite("vpb_icons/gallery_category.png", Color.white);
            GameObject catBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprCat, UserTagEditorOpenCategoryDialog, catCol, 10f * s);
            AddTooltipPlain(catBtn, VPBTranslation.T("gallery.usertags.editor_category_tip", "Assign the selected tag(s) to a color category, or create/manage categories."));
            GameObject impBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprImp, UserTagEditorBeginImportYaml, ioImpCol, 10f * s);
            AddTooltipPlain(impBtn, VPBTranslation.T("gallery.usertags.editor_import_tip", "Import tag assignments from a YAML file (tag→items or item→tags)."));
            GameObject expBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprExp, UserTagEditorBeginExportYaml, ioExpCol, 10f * s);
            AddTooltipPlain(expBtn, VPBTranslation.T("gallery.usertags.editor_export_tip", "Export two YAML files: tag→items and item→tags (same folder, shared base name)."));

            Color infoBackdrop = new Color(0.30f, 0.34f, 0.38f, 1f);
            GameObject infoBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprInfo, null, infoBackdrop, 10f * s);
            AddTooltipPlain(infoBtn, userTagEditorInfoWisdom);

            GameObject arPadR = new GameObject("PadR");
            arPadR.transform.SetParent(actionRow.transform, false);
            LayoutElement arPadRle = UI.AddLE(arPadR, minWidth: 0f, flexibleWidth: 1f);

            UserTagEditorBuildNameDialog(
                dim.transform, "UserTagEditorMergeModal", "MergeDialogPanel", "MergeDialogInput", "MergeDialogButtons",
                VPBTranslation.T("gallery.usertags.editor_merge_dialog_title", "Merge tags into…"), "MergeTitle",
                VPBTranslation.T("gallery.usertags.editor_merge_ph", "New tag name…"),
                VPBTranslation.T("gallery.usertags.editor_merge_cancel", "Cancel"),
                VPBTranslation.T("gallery.usertags.editor_merge_confirm", "Merge"),
                editorType.Prose, bodyFont, smallFont, s,
                UserTagEditorCloseMergeDialog, UserTagEditorConfirmMergeFromDialog,
                out _userTagEditorMergeModalGo, out _userTagEditorMergeModalTitleText, out _userTagEditorMergeModalInput);

            UserTagEditorBuildNameDialog(
                dim.transform, "UserTagEditorRenameModal", "RenameDialogPanel", "RenameDialogInput", "RenameDialogButtons",
                VPBTranslation.T("gallery.usertags.editor_rename_dialog_title_idle", "Rename to…"), "RenameTitle",
                VPBTranslation.T("gallery.usertags.editor_rename_ph", "New tag name…"),
                VPBTranslation.T("gallery.usertags.editor_merge_cancel", "Cancel"),
                VPBTranslation.T("gallery.usertags.editor_rename_confirm", "Rename"),
                editorType.Prose, bodyFont, smallFont, s,
                UserTagEditorCloseRenameDialog, UserTagEditorConfirmRenameFromDialog,
                out _userTagEditorRenameModalGo, out _userTagEditorRenameModalTitleText, out _userTagEditorRenameModalInput);

            SetLayerRecursive(dim, backgroundBoxGO.layer);

            userTagsCached = false;
            CacheUserTagsSideTab();
            UserTagEditorSetTitleCount(cachedUserTagSideTab.Count);

            _userTagEditorRoot = dim;
            _userTagEditorRoot.SetActive(false);
        }

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
            bool hadFilter = _userTagEditorFilterInput != null && !string.IsNullOrEmpty(_userTagEditorFilterInput.text);
            if (_userTagEditorFilterInput != null && hadFilter)
            {
                // onValueChanged handler triggers rebuild once
                _userTagEditorFilterInput.text = "";
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
            try { UpdateTabs(); } catch { }
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
            float u = s * 1.38f;
            int rowFont = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, u, GalleryUiDesignTokens.FontMinRef);

            var rows = new List<UserTagSideTabEntry>(cachedUserTagSideTab);
            string filt = _userTagEditorFilterInput != null ? _userTagEditorFilterInput.text : "";
            if (!string.IsNullOrEmpty(filt))
            {
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    if (rows[i].Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0)
                        rows.RemoveAt(i);
                }
            }

            switch (_userTagEditorSortMode)
            {
                case 1:
                    rows.Sort((a, b) => string.Compare(b.Name, a.Name, StringComparison.OrdinalIgnoreCase));
                    break;
                case 2:
                    rows.Sort((a, b) => b.Count.CompareTo(a.Count));
                    break;
                case 3:
                    rows.Sort((a, b) => a.Count.CompareTo(b.Count));
                    break;
                default:
                    rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    break;
            }

            Color baseCol = new Color(0.2f, 0.2f, 0.22f, 1f);
            Color selCol = new Color(0.28f, 0.38f, 0.32f, 1f);
            float rowH = 34f * s;

            _userTagEditorRowBaseCol = baseCol;
            _userTagEditorRowSelCol = selCol;
            _userTagEditorVisibleRows.Clear();
            _userTagEditorVisibleRows.AddRange(rows);
            _userTagEditorRowVisuals.Clear();

            for (int ri = 0; ri < rows.Count; ri++)
            {
                UserTagSideTabEntry e = rows[ri];
                string nameSnap = e.Name;
                string label = e.Name + " (" + e.Count + ")";

                GameObject rowGo = new GameObject("EditorTagRow");
                rowGo.transform.SetParent(_userTagEditorRowsParent, false);
                bool sel = _userTagEditorRowSelection.Contains(nameSnap);
                Image bg = UI.AddGalleryElementRoundedBg(rowGo, sel ? selCol : baseCol);
                _userTagEditorRowVisuals.Add(new UserTagEditorRowVisual { Name = nameSnap, Bg = bg });
                LayoutElement rle = UI.AddLE(rowGo, minHeight: rowH, preferredHeight: rowH, flexibleWidth: 1f);

                Button btn = rowGo.AddComponent<Button>();
                btn.targetGraphic = bg;
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                btn.colors = cb;
                btn.transition = Selectable.Transition.None;
                int riCapture = ri;
                btn.onClick.AddListener(() => UserTagEditorOnRowClicked(nameSnap, riCapture, rows, bg, baseCol, selCol));

                // Category color swatch (US-02): shows the tag's assigned category color, or a faint empty chip.
                Color? catColor = TryGetUserTagCategoryColor(nameSnap);
                float swSize = rowH * 0.46f;
                GameObject swGo = new GameObject("CatSwatch");
                swGo.transform.SetParent(rowGo.transform, false);
                Image swImg = UI.AddImage(swGo, catColor.HasValue ? catColor.Value : new Color(1f, 1f, 1f, 0.06f), false);
                RectTransform swRt = swGo.GetComponent<RectTransform>();
                swRt.anchorMin = new Vector2(0f, 0.5f);
                swRt.anchorMax = new Vector2(0f, 0.5f);
                swRt.pivot = new Vector2(0f, 0.5f);
                swRt.sizeDelta = new Vector2(swSize, swSize);
                swRt.anchoredPosition = new Vector2(12f * s, 0f);

                float labelLeft = 12f * s + swSize + 8f * s;
                Text txt = UI.CreateLabel(rowGo, label, rowFont, new Color(0.93f, 0.93f, 0.95f, 1f), TextAnchor.MiddleLeft, name: "Label");
                RectTransform trt = txt.GetComponent<RectTransform>();
                trt.offsetMin = new Vector2(labelLeft, 2f * s);
                trt.offsetMax = new Vector2(-14f * s, -2f * s);
            }
        }
    }

    internal static class UserTagDragSession
    {
        public static List<string> PendingTags;
        /// <summary>True when dragging from Applied rows — apply drop zones must ignore <see cref="IDropHandler.OnDrop"/>.</summary>
        public static bool PendingIsAppliedRowRemove;

        public static void Clear()
        {
            PendingTags = null;
            PendingIsAppliedRowRemove = false;
        }
    }

    internal sealed class UserTagPickDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        public GalleryPanel Panel;
        public string PrimaryTag;
        /// <summary>When true, drag originates from Applied list — drop targets remove control only (not apply zones).</summary>
        public bool IsAppliedRowDrag;
        private CanvasGroup _cg;
        private bool _pressed;
        private bool _dragging;
        private Vector2 _pressPos;
        private float _pressTime;
        private GameObject _ghost;
        private Text _ghostText;
        private RectTransform _ghostRT;
        private Canvas _rootCanvas;
        private readonly List<RaycastResult> _raycastHits = new List<RaycastResult>(16);
        public bool ConsumedByDrag { get; private set; }
        private bool _releaseProcessed;

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
                EndManualDrag(eventData);
                ConsumedByDrag = true;
            }
        }

        private void Update()
        {
            if (Panel == null) return;

            if (_dragging)
            {
                UpdateGhostPosition();
                if (!IsAppliedRowDrag)
                    RefreshUserTagApplyDragHoverStatus();
                if (!_releaseProcessed && Input.GetMouseButtonUp(0))
                    EndManualDrag(null);
                return;
            }

            if (!_pressed) return;

            bool isVR = XrUtils.IsVrActive();
            float distThreshold = isVR ? 50f : 10f;
            float holdThreshold = isVR ? 0.25f : 0f;

            Vector2 cur = Input.mousePosition;
            float dist = (cur - _pressPos).magnitude;
            if (dist < distThreshold) return;

            float held = Time.unscaledTime - _pressTime;
            if (held < holdThreshold) return;

            BeginManualDrag();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Panel == null) return;

            if (XrUtils.IsVrActive())
            {
                float held = Time.unscaledTime - _pressTime;
                if (held < 0.25f) return;

                if (eventData != null)
                {
                    float dist = (eventData.position - _pressPos).magnitude;
                    if (dist < 50f) return;
                }
            }

            BeginManualDrag();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging) UpdateGhostPosition();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_dragging)
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
            UserTagDragSession.PendingIsAppliedRowRemove = IsAppliedRowDrag;
            _dragging = true;

            if (_cg != null)
            {
                _cg.alpha = 0.65f;
                _cg.blocksRaycasts = false;
            }

            CreateGhostLabel(list);
            UpdateGhostPosition();
        }

        private void EndManualDrag(PointerEventData eventData)
        {
            if (IsAppliedRowDrag)
                TryDropToRemoveZone(eventData);
            else
                TryDropToApplyZone(eventData);
            CleanupDragVisuals();
        }

        private void TryDropToRemoveZone(PointerEventData eventData)
        {
            if (Panel == null) return;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return;

            EventSystem es = EventSystem.current;
            if (es == null) return;

            var ped = eventData ?? new PointerEventData(es);
            ped.position = (eventData != null) ? eventData.position : (Vector2)Input.mousePosition;
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
                    break;
                }
            }
        }

        private void TryDropToApplyZone(PointerEventData eventData)
        {
            if (Panel == null) return;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return;

            EventSystem es = EventSystem.current;
            if (es == null)
            {
                Panel.UserTagApplyDroppedTags(tags);
                return;
            }

            var ped = eventData ?? new PointerEventData(es);
            ped.position = (eventData != null) ? eventData.position : (Vector2)Input.mousePosition;
            _raycastHits.Clear();
            es.RaycastAll(ped, _raycastHits);
            for (int i = 0; i < _raycastHits.Count; i++)
            {
                GameObject go = _raycastHits[i].gameObject;
                if (go == null) continue;
                UserTagApplyDropZone dz = go.GetComponentInParent<UserTagApplyDropZone>();
                if (dz != null && dz.Panel == Panel)
                {
                    Panel.UserTagApplyDroppedTags(tags);
                    return;
                }
            }

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
            var ped = new PointerEventData(es) { position = (Vector2)Input.mousePosition };
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
            _pressed = false;
            _dragging = false;
            ConsumedByDrag = false;
            _releaseProcessed = false;
            try { if (Panel != null && !IsAppliedRowDrag) Panel.dragHoverItem(null, null); } catch { }
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

            _ghost = new GameObject("VPB_UserTagDragGhost");
            _ghost.layer = root.gameObject.layer;
            _ghostRT = _ghost.AddComponent<RectTransform>();
            _ghostRT.SetParent(root.transform, false);
            _ghostRT.anchorMin = new Vector2(0f, 0f);
            _ghostRT.anchorMax = new Vector2(0f, 0f);
            _ghostRT.pivot = new Vector2(0f, 0f);
            _ghostRT.sizeDelta = new Vector2(240f, 34f);

            Image bg = UI.AddImage(_ghost, new Color(0.15f, 0.15f, 0.18f, 0.85f), false);

            _ghostText = UI.CreateLabel(_ghost, tags.Count == 1 ? ("Tag: " + tags[0]) : ("Tags: " + tags.Count),
                GalleryUiDesignTokens.FontBodyRef, new Color(0.95f, 0.95f, 0.97f, 1f), TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow);
            RectTransform trt = _ghostText.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(10f, 6f);
            trt.offsetMax = new Vector2(-10f, -6f);
        }

        private void UpdateGhostPosition()
        {
            if (_ghostRT == null) return;
            Vector2 pos = Input.mousePosition;
            _ghostRT.position = new Vector3(pos.x + 14f, pos.y + 14f, 0f);
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
            return UserTagDragSession.PendingTags != null && UserTagDragSession.PendingTags.Count > 0;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Image == null) return;
            if (UserTagDragSession.PendingTags == null || UserTagDragSession.PendingTags.Count == 0) return;
            Image.color = new Color(1f, 1f, 1f, HoverAlpha);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Image == null) return;
            Image.color = new Color(1f, 1f, 1f, 0.0f);
        }
    }
}
