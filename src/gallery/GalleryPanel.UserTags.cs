using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

        private void RefreshFilesThenUpdateTabs(bool keepScroll)
        {
            RefreshFiles(keepScroll, false, false, null);
            try { UpdateTabs(); } catch { }
        }

        internal static List<string> ParseGalleryUserTagPaste(string pasted)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(pasted)) return result;
            char[] splitChars = new[] { ',', ';', '\t', '\n', '\r' };
            string[] parts = pasted.Split(splitChars, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parts.Length; i++)
            {
                string n = VpbLocalDatabase.NormalizeGalleryUserTagName(parts[i]);
                if (!string.IsNullOrEmpty(n) && seen.Add(n))
                {
                    result.Add(n);
                    if (result.Count >= VpbLocalDatabase.GalleryUserTagPasteMaxUniqueNames) break;
                }
            }
            return result;
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
        }

        private void updatePanelForSelection()
        {
            CacheAppliedUserTagsForSelection();
            try
            {
                if (leftActiveContent == ContentType.UserTags && leftTabContainerGO != null)
                    UpdateUserTagVirtualVisible(true, UserTagStateOnColor, leftTabContainerGO.transform);
                if (rightActiveContent == ContentType.UserTags && rightTabContainerGO != null)
                    UpdateUserTagVirtualVisible(false, UserTagStateOnColor, rightTabContainerGO.transform);
            }
            catch { }
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

        /// <summary>Must match EnsureUserTagSideTabBulkBlock padding, spacing, and title/font scale (u = s*1.38).</summary>
        private static float UserTagsAvailStickyHeightPx()
        {
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;
            int fs = Mathf.Max(15, Mathf.RoundToInt(19f * u));
            float padTop = Mathf.RoundToInt(4f * s);
            float padBottom = Mathf.RoundToInt(10f * s);
            // LayoutElement preferred 34*s is short when font is ~19*u; keep viewport/shrink in sync.
            float titleBand = Mathf.Max(34f * s, fs * 1.22f);
            return padTop + titleBand + 7f * s + 48f * s + padBottom;
        }

        private static float UserTagsAvailFooterHeightPx()
        {
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            // Match EnsureUserTagInheritVarToChildrenButtonInFooter row height + padding.
            float rowH = Mathf.Max(28f, 34f * s);
            float pad = Mathf.RoundToInt(4f * s);
            return rowH + pad * 2;
        }

        private static float UserTagsAppliedStickyHeightPx()
        {
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
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
            vp.offsetMin = defMin;
            vp.offsetMax = new Vector2(defMax.x, defMax.y - h);

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
            vp.offsetMin = defMin;
            vp.offsetMax = new Vector2(defMax.x, defMax.y - h);
        }

        private void SyncUserTagAvailTitleCount(bool isLeft)
        {
            int n = _userTagVirtView != null ? _userTagVirtView.Count : 0;
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

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;

            GameObject root = new GameObject("VPB_UserTagsAppliedToolbar_v3");
            root.transform.SetParent(sticky, false);
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0f, 1f);
            rootRT.anchorMax = new Vector2(1f, 1f);
            rootRT.pivot = new Vector2(0.5f, 1f);
            rootRT.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f * s;
            vlg.padding = new RectOffset(Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s), Mathf.RoundToInt(4 * s), Mathf.RoundToInt(8 * s));
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            LayoutElement rootLe = root.AddComponent<LayoutElement>();
            rootLe.flexibleWidth = 1f;
            ContentSizeFitter rootCsf = root.AddComponent<ContentSizeFitter>();
            rootCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject titleRow = new GameObject("AppliedTitleRow");
            titleRow.transform.SetParent(root.transform, false);
            HorizontalLayoutGroup trH = titleRow.AddComponent<HorizontalLayoutGroup>();
            trH.spacing = 8f * s;
            trH.padding = new RectOffset(0, 0, 0, 0);
            trH.childAlignment = TextAnchor.MiddleLeft;
            trH.childControlWidth = true;
            trH.childControlHeight = true;
            trH.childForceExpandWidth = true;
            trH.childForceExpandHeight = false;

            float delSz = Mathf.Max(32f, 42f * s);

            LayoutElement titleRowLe = titleRow.AddComponent<LayoutElement>();
            titleRowLe.minHeight = Mathf.Max(30f * s, delSz);
            titleRowLe.preferredHeight = Mathf.Max(34f * s, delSz);
            titleRowLe.flexibleWidth = 1f;

            GameObject titleGo = new GameObject("AppliedTitleText");
            titleGo.transform.SetParent(titleRow.transform, false);
            Text titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleTxt.fontSize = Mathf.Max(14, Mathf.RoundToInt(17f * u));
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = new Color(0.88f, 0.88f, 0.92f, 1f);
            titleTxt.text = string.Format(VPBTranslation.T("gallery.usertags.applied_with_count", "Applied ({0})"), 0);
            LayoutElement titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;
            titleLe.minHeight = Mathf.Max(28f * s, delSz * 0.85f);
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
            if (tb == null) tb = container.Find("VPB_UserTagsAppliedToolbar_v2");
            if (tb == null && leftUserTagsAppliedStickyGO != null)
                tb = leftUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v3");
            if (tb == null && leftUserTagsAppliedStickyGO != null)
                tb = leftUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v2");
            if (tb == null && rightUserTagsAppliedStickyGO != null)
                tb = rightUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v3");
            if (tb == null && rightUserTagsAppliedStickyGO != null)
                tb = rightUserTagsAppliedStickyGO.transform.Find("VPB_UserTagsAppliedToolbar_v2");
            if (tb == null) return;
            GameObject rootGo = tb.gameObject;
            Image rayImg = rootGo.GetComponent<Image>();
            if (rayImg == null)
            {
                rayImg = rootGo.AddComponent<Image>();
                rayImg.color = new Color(1f, 1f, 1f, 0.02f);
                rayImg.raycastTarget = true;
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
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            Transform stripT = container.Find(stripName);
            GameObject stripGo;
            if (stripT == null)
            {
                stripGo = new GameObject(stripName);
                stripGo.transform.SetParent(container, false);
                Image img = stripGo.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.03f);
                img.raycastTarget = true;
                LayoutElement le = stripGo.AddComponent<LayoutElement>();
                le.minHeight = 2f;
                le.preferredHeight = 4f * s;
                le.flexibleWidth = 1f;
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

                Image img = go.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.0f);
                img.raycastTarget = true;

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

        private void RefreshVisibleUserTagRows()
        {
            try
            {
                if (leftActiveContent == ContentType.UserTags && leftTabContainerGO != null)
                    UpdateUserTagVirtualVisible(true, UserTagStateOnColor, leftTabContainerGO.transform);
                if (rightActiveContent == ContentType.UserTags && rightTabContainerGO != null)
                    UpdateUserTagVirtualVisible(false, UserTagStateOnColor, rightTabContainerGO.transform);
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
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(8f * scale, 0f);
            }
            iconGo.SetActive(true);
            iconGo.transform.SetAsLastSibling();
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
            if (!_userTagAvailFilterMode || activeUserTags == null || activeUserTags.Count == 0)
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
                if (!VpbLocalDatabase.TryGalleryRowMatchesAllUserTags(cat, pkg, ip, activeUserTags))
                    removeRefs.Add(fe);
            }

            if (removeRefs.Count == 0) return false;

            RemoveFileEntriesFromLists(currentFilteredFiles, removeRefs);
            RemoveFileEntriesFromLists(lastFilteredFiles, removeRefs);
            RemoveFileEntriesFromLists(topSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(filterSearchBaseFiles, removeRefs);
            RemoveFileEntriesFromLists(selectedFiles, removeRefs);
            return true;
        }

        private void RefreshUiAfterUserTagMutate(bool remove, List<VpbLocalDatabase.GalleryUserTagRowKey> updatedRows)
        {
            InvalidateTags();
            CacheAppliedUserTagsForSelection();

            bool filterModeRemove = remove
                && _userTagAvailFilterMode
                && activeUserTags != null && activeUserTags.Count > 0
                && activeContentType == ContentType.Category
                && VpbSqlite3.IsAvailable;

            if (filterModeRemove)
            {
                if (TryPruneVisibleGridAfterUserTagRemove(updatedRows))
                {
                    if (recyclingGrid != null)
                    {
                        recyclingGrid.SetItemCount(currentFilteredFiles.Count);
                        recyclingGrid.Refresh();
                    }
                    try { UpdatePaginationText(); } catch { }
                }
                try { RefreshSelectionVisuals(); } catch { }
                try { UpdateTabs(); } catch { }
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

            RefreshUiAfterUserTagMutate(remove, rows);
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

            RefreshUiAfterUserTagMutate(remove, rows);
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

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;

            GameObject root = new GameObject("VPB_UserTagBulkBlock_v3");
            root.transform.SetParent(bulkParent, false);
            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0f, 1f);
            rootRT.anchorMax = new Vector2(1f, 1f);
            rootRT.pivot = new Vector2(0.5f, 1f);
            rootRT.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 7f * s;
            vlg.padding = new RectOffset(Mathf.RoundToInt(6 * s), Mathf.RoundToInt(6 * s), Mathf.RoundToInt(4 * s), Mathf.RoundToInt(10 * s));
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            LayoutElement rootLe = root.AddComponent<LayoutElement>();
            rootLe.flexibleWidth = 1f;
            ContentSizeFitter rootCsf = root.AddComponent<ContentSizeFitter>();
            rootCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject titleGo = new GameObject("BulkTitle");
            titleGo.transform.SetParent(root.transform, false);
            Text titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            int titleFs = Mathf.Max(13, Mathf.RoundToInt(15f * u));
            float titleBand = Mathf.Max(30f * s, titleFs * 1.22f);
            titleTxt.fontSize = titleFs;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = Color.white;
            titleTxt.text = string.Format(VPBTranslation.T("gallery.usertags.tags_with_count", "Tags ({0})"), 0);
            titleTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            titleTxt.verticalOverflow = VerticalWrapMode.Truncate;
            LayoutElement titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.minHeight = Mathf.Min(30f * s, titleBand);
            titleLe.preferredHeight = titleBand;
            titleLe.flexibleWidth = 1f;

            GameObject btnRow = new GameObject("BulkBtnRow");
            btnRow.transform.SetParent(root.transform, false);
            HorizontalLayoutGroup hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(0, 0, 0, 0);
            hlg.spacing = 6f * s;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            // false: only Apply (flexibleWidth 1) grows; true spreads width across all children and crushes the label.
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            LayoutElement rowLe = btnRow.AddComponent<LayoutElement>();
            rowLe.minHeight = 46f * s;
            rowLe.preferredHeight = 48f * s;
            rowLe.flexibleWidth = 1f;

            float editSq = 44f * s;
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

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;
            int font = Mathf.Max(12, Mathf.RoundToInt(14f * u));

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
                LayoutElement le = rowGo.AddComponent<LayoutElement>();
                le.minHeight = Mathf.Max(28f, 34f * s);
                le.preferredHeight = Mathf.Max(28f, 34f * s);
                le.flexibleWidth = 1f;

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
            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
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
                t.fontSize = Mathf.Max(12, Mathf.RoundToInt(14f * (s * 1.38f)));
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

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float sq = 44f * s;

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
                Mathf.Max(12, Mathf.RoundToInt(13f * (s * 1.38f))),
                0f,
                0f,
                AnchorPresets.stretchAll,
                OnUserTagAvailFilterModeClicked);
            filterGo.name = "VPB_UserTagFilterModeBtn";
            AddUserTagFilterButtonIconAndLabel(filterGo, s);
            AddTooltipPlain(filterGo, VPBTranslation.T("gallery.usertags.filter_mode_toggle_tip", "When on, clicking tag rows filters the main grid by those tags. When off, clicking tag rows toggles tags on selected item(s)."));
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

            GameObject iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(filterGo.transform, false);
            Image iconImg = iconGo.AddComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            iconImg.color = Color.white;
            RectTransform irt = iconGo.GetComponent<RectTransform>();
            if (irt != null)
            {
                float size = Mathf.Clamp(22f * s, 16f, 30f);
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(size, size);
                irt.anchoredPosition = new Vector2(12f * s, 0f);
            }

            Text t = filterGo.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.gameObject.SetActive(true);
                t.alignment = TextAnchor.MiddleRight;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                RectTransform trt = t.GetComponent<RectTransform>();
                if (trt != null)
                {
                    trt.offsetMin = new Vector2(42f * s, trt.offsetMin.y);
                    trt.offsetMax = new Vector2(-10f * s, trt.offsetMax.y);
                }
            }
        }

        private void OnUserTagAvailFilterModeClicked()
        {
            _userTagAvailFilterMode = !_userTagAvailFilterMode;
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
                iconImg.sprite = _userTagAvailFilterMode ? onSpr : tagSpr;
                iconImg.color = Color.white;
            }
            Text label = filterBtn.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.gameObject.SetActive(true);
                label.text = _userTagAvailFilterMode
                    ? VPBTranslation.T("gallery.usertags.filter_button_on_label", "Filter Mode")
                    : VPBTranslation.T("gallery.usertags.tag_mode_button_label", "Tag Mode");
            }
            Image bd = filterBtn.GetComponent<Image>();
            if (bd != null)
            {
                Color tagModeBackdrop = new Color(0.20f, 0.50f, 0.25f, 1f);
                Color filterModeBackdrop = new Color(0.18f, 0.38f, 0.62f, 1f);
                bd.color = _userTagAvailFilterMode ? filterModeBackdrop : tagModeBackdrop;
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
            UserTagEditorSyncSortIcon();
            RebuildUserTagEditorRows();
            _userTagEditorRoot.SetActive(true);
            _userTagEditorRoot.transform.SetAsLastSibling();
        }

        private void HideUserTagListEditor()
        {
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

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;
            int smallFont = Mathf.Max(11, Mathf.RoundToInt(13f * u));
            int bodyFont = Mathf.Max(13, Mathf.RoundToInt(15f * u));
            float headerChromeSq = Mathf.Max(30f, 36f * s);
            float searchBarH = headerChromeSq;

            GameObject dim = new GameObject("VPB_UserTagEditorOverlay");
            dim.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform dimRT = dim.AddComponent<RectTransform>();
            dimRT.anchorMin = Vector2.zero;
            dimRT.anchorMax = Vector2.one;
            dimRT.sizeDelta = Vector2.zero;
            Image dimImg = dim.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.55f);
            dimImg.raycastTarget = true;
            Button dimBtn = dim.AddComponent<Button>();
            ColorBlock dcb = dimBtn.colors;
            dcb.normalColor = Color.white;
            dimBtn.colors = dcb;
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(HideUserTagListEditor);

            GameObject panel = new GameObject("UserTagEditorPanel");
            panel.transform.SetParent(dim.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(620f * s, 720f * s);

            Image pbg = panel.AddComponent<Image>();
            pbg.color = new Color(0.11f, 0.11f, 0.13f, 1f);
            pbg.raycastTarget = true;

            VerticalLayoutGroup pvlg = panel.AddComponent<VerticalLayoutGroup>();
            pvlg.padding = new RectOffset(Mathf.RoundToInt(14 * s), Mathf.RoundToInt(14 * s), Mathf.RoundToInt(12 * s), Mathf.RoundToInt(12 * s));
            pvlg.spacing = 8f * s;
            pvlg.childControlWidth = true;
            pvlg.childControlHeight = true;
            pvlg.childForceExpandWidth = true;
            pvlg.childForceExpandHeight = false;

            GameObject titleRow = new GameObject("TitleRow");
            titleRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup trh = titleRow.AddComponent<HorizontalLayoutGroup>();
            trh.childAlignment = TextAnchor.UpperLeft;
            trh.spacing = 0f;
            trh.childControlWidth = true;
            trh.childControlHeight = true;
            trh.childForceExpandWidth = true;
            trh.childForceExpandHeight = false;
            LayoutElement titleRowLe = titleRow.AddComponent<LayoutElement>();
            titleRowLe.minHeight = Mathf.Max(26f * s, headerChromeSq * 0.72f);
            titleRowLe.preferredHeight = titleRowLe.minHeight;

            GameObject titleGo = new GameObject("TagsDbTitle");
            titleGo.transform.SetParent(titleRow.transform, false);
            Text headerTitleTxt = titleGo.AddComponent<Text>();
            headerTitleTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            headerTitleTxt.fontSize = Mathf.Max(14, Mathf.RoundToInt(17f * u));
            headerTitleTxt.fontStyle = FontStyle.Bold;
            headerTitleTxt.color = new Color(0.92f, 0.92f, 0.95f, 1f);
            headerTitleTxt.alignment = TextAnchor.UpperLeft;
            _userTagEditorTitleText = headerTitleTxt;
            LayoutElement titleHLe = titleGo.AddComponent<LayoutElement>();
            titleHLe.flexibleWidth = 0f;
            titleHLe.minHeight = titleRowLe.minHeight;
            ContentSizeFitter titleCsf = titleGo.AddComponent<ContentSizeFitter>();
            titleCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            titleCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject headerRow = new GameObject("HeaderRow");
            headerRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup hh = headerRow.AddComponent<HorizontalLayoutGroup>();
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.spacing = 6f * s;
            hh.childControlWidth = true;
            hh.childControlHeight = true;
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = false;
            LayoutElement hle = headerRow.AddComponent<LayoutElement>();
            hle.minHeight = Mathf.Max(headerChromeSq, searchBarH);
            hle.preferredHeight = headerChromeSq;

            float sortSq = headerChromeSq;
            Color sortBackdropCol = new Color(0.22f, 0.42f, 0.58f, 1f);
            Sprite sortSpr0 = sceneSourceSortModeSprites != null && sceneSourceSortModeSprites.Length > 0 ? sceneSourceSortModeSprites[0] : null;
            GameObject sortBtnGo = UI.CreateSideTabSquareIconButton(headerRow, sortSq, sortSpr0, UserTagEditorCycleSort, sortBackdropCol, 5f * s);
            sortBtnGo.name = "UserTagEditorSortBtn";
            Transform sortIconTr = sortBtnGo.transform.Find("Icon");
            _userTagEditorSortIconImage = sortIconTr != null ? sortIconTr.GetComponent<Image>() : null;
            UserTagEditorSyncSortIcon();
            AddTooltipPlain(sortBtnGo, VPBTranslation.T("gallery.usertags.editor_sort_cycle_tip", "Sort list: tap to cycle name A→Z / Z→A / count high→low / low→high."));

            GameObject filterGo = new GameObject("FilterInput");
            filterGo.transform.SetParent(headerRow.transform, false);
            Image fiBg = filterGo.AddComponent<Image>();
            fiBg.color = new Color(0.07f, 0.07f, 0.09f, 1f);
            LayoutElement fiLe = filterGo.AddComponent<LayoutElement>();
            fiLe.flexibleWidth = 1f;
            fiLe.minWidth = 0f;
            fiLe.minHeight = searchBarH;

            GameObject fta = new GameObject("TextArea");
            fta.transform.SetParent(filterGo.transform, false);
            RectTransform ftaRt = fta.AddComponent<RectTransform>();
            ftaRt.anchorMin = Vector2.zero;
            ftaRt.anchorMax = Vector2.one;
            ftaRt.offsetMin = new Vector2(8f * s, 2f * s);
            ftaRt.offsetMax = new Vector2(-8f * s, -2f * s);
            GameObject fph = new GameObject("Placeholder");
            fph.transform.SetParent(fta.transform, false);
            Text fphT = fph.AddComponent<Text>();
            fphT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            fphT.fontSize = bodyFont;
            fphT.color = new Color(0.42f, 0.42f, 0.45f, 1f);
            fphT.alignment = TextAnchor.MiddleLeft;
            fphT.text = VPBTranslation.T("gallery.usertags.editor_filter_ph", "Filter list…");
            RectTransform fphRt = fph.GetComponent<RectTransform>();
            fphRt.anchorMin = Vector2.zero;
            fphRt.anchorMax = Vector2.one;
            fphRt.sizeDelta = Vector2.zero;
            GameObject ftc = new GameObject("Text");
            ftc.transform.SetParent(fta.transform, false);
            Text ftcT = ftc.AddComponent<Text>();
            ftcT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            ftcT.fontSize = bodyFont;
            ftcT.color = Color.white;
            ftcT.alignment = TextAnchor.MiddleLeft;
            RectTransform ftcRt = ftc.GetComponent<RectTransform>();
            ftcRt.anchorMin = Vector2.zero;
            ftcRt.anchorMax = Vector2.one;
            ftcRt.sizeDelta = Vector2.zero;
            _userTagEditorFilterInput = filterGo.AddComponent<InputField>();
            _userTagEditorFilterInput.textComponent = ftcT;
            _userTagEditorFilterInput.placeholder = fphT;
            _userTagEditorFilterInput.lineType = InputField.LineType.SingleLine;
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
            LayoutElement scLe = scrollGO.AddComponent<LayoutElement>();
            scLe.flexibleHeight = 1f;
            scLe.minHeight = 280f * s;
            Transform vp = scrollGO.transform.Find("Viewport");
            _userTagEditorRowsParent = vp != null ? vp.Find("Content") : null;

            GameObject newTagBlock = new GameObject("NewTagBlock");
            newTagBlock.transform.SetParent(panel.transform, false);
            VerticalLayoutGroup ntbV = newTagBlock.AddComponent<VerticalLayoutGroup>();
            ntbV.spacing = 4f * s;
            ntbV.childAlignment = TextAnchor.UpperLeft;
            ntbV.childControlWidth = true;
            ntbV.childControlHeight = true;
            ntbV.childForceExpandWidth = true;
            ntbV.childForceExpandHeight = false;
            LayoutElement ntbLe = newTagBlock.AddComponent<LayoutElement>();
            ntbLe.flexibleWidth = 1f;
            ntbLe.minHeight = 152f * s;
            ntbLe.preferredHeight = 168f * s;

            GameObject newInGo = new GameObject("NewTagInput");
            newInGo.transform.SetParent(newTagBlock.transform, false);
            Image nBg = newInGo.AddComponent<Image>();
            nBg.color = new Color(0.07f, 0.07f, 0.09f, 1f);
            LayoutElement nLe = newInGo.AddComponent<LayoutElement>();
            nLe.flexibleWidth = 1f;
            nLe.minHeight = 160f * s;
            nLe.preferredHeight = 168f * s;
            GameObject nta = new GameObject("TextArea");
            nta.transform.SetParent(newInGo.transform, false);
            RectTransform ntaRt = nta.AddComponent<RectTransform>();
            ntaRt.anchorMin = Vector2.zero;
            ntaRt.anchorMax = Vector2.one;
            ntaRt.offsetMin = new Vector2(8f * s, 6f * s);
            ntaRt.offsetMax = new Vector2(-8f * s, -6f * s);
            GameObject nph = new GameObject("Placeholder");
            nph.transform.SetParent(nta.transform, false);
            Text nphT = nph.AddComponent<Text>();
            nphT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            nphT.fontSize = bodyFont;
            nphT.color = new Color(0.42f, 0.42f, 0.45f, 1f);
            nphT.horizontalOverflow = HorizontalWrapMode.Wrap;
            nphT.verticalOverflow = VerticalWrapMode.Overflow;
            nphT.text = VPBTranslation.T("gallery.usertags.editor_new_ph", "Type or paste tag names here…");
            RectTransform nphRt = nph.GetComponent<RectTransform>();
            nphRt.anchorMin = Vector2.zero;
            nphRt.anchorMax = Vector2.one;
            nphRt.sizeDelta = Vector2.zero;
            GameObject ntx = new GameObject("Text");
            ntx.transform.SetParent(nta.transform, false);
            Text ntxT = ntx.AddComponent<Text>();
            ntxT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            ntxT.fontSize = bodyFont;
            ntxT.color = Color.white;
            ntxT.horizontalOverflow = HorizontalWrapMode.Wrap;
            ntxT.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform ntxRt = ntx.GetComponent<RectTransform>();
            ntxRt.anchorMin = Vector2.zero;
            ntxRt.anchorMax = Vector2.one;
            ntxRt.sizeDelta = Vector2.zero;
            _userTagEditorNewTagInput = newInGo.AddComponent<InputField>();
            _userTagEditorNewTagInput.textComponent = ntxT;
            _userTagEditorNewTagInput.placeholder = nphT;
            _userTagEditorNewTagInput.lineType = InputField.LineType.MultiLineNewline;
            _userTagEditorNewTagInput.characterLimit = 0;

            string userTagEditorInfoWisdom =
                VPBTranslation.T(
                    "gallery.usertags.editor_paste_hint",
                    "Separate tags with comma, semicolon, tab, or line breaks. Paste from spreadsheets or plain lists.")
                + "\n"
                + VPBTranslation.T(
                    "gallery.usertags.editor_tag_rules_hint",
                    "Each tag: 1–30 characters; letters, digits, single spaces, hyphen (-), underscore (_). Other punctuation rejected.")
                + "\n"
                + VPBTranslation.T(
                    "gallery.usertags.editor_limits_hint",
                    "Up to 10 000 distinct names per paste; library holds up to 10 000 tag names; each item may have up to 100 tags.");

            float actSq = 1.618f * headerChromeSq;

            GameObject actionRow = new GameObject("ActionRow");
            actionRow.transform.SetParent(panel.transform, false);
            HorizontalLayoutGroup arH = actionRow.AddComponent<HorizontalLayoutGroup>();
            arH.spacing = 8f * s;
            arH.childAlignment = TextAnchor.MiddleCenter;
            arH.childControlWidth = true;
            arH.childControlHeight = true;
            arH.childForceExpandWidth = false;
            arH.childForceExpandHeight = false;
            LayoutElement arLe = actionRow.AddComponent<LayoutElement>();
            arLe.minHeight = actSq + 4f * s;
            arLe.preferredHeight = actSq + 4f * s;

            GameObject arPadL = new GameObject("PadL");
            arPadL.transform.SetParent(actionRow.transform, false);
            LayoutElement arPadLle = arPadL.AddComponent<LayoutElement>();
            arPadLle.flexibleWidth = 1f;
            arPadLle.minWidth = 0f;

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
            AddTooltipPlain(createTagsBtn, VPBTranslation.T("gallery.usertags.editor_create_tags_tip", "Create tag rows from the text field (comma / line separated)."));
            GameObject removeSelBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprMinus, UserTagEditorRemoveSelectedFromDb, removeCol, 10f * s);
            AddTooltipPlain(removeSelBtn, VPBTranslation.T("gallery.usertags.editor_remove_selected_tip", "Delete selected tag(s) from the database for all items (cannot undo)."));
            GameObject mergeBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprMerge, UserTagEditorOpenMergeDialog, mergeCol, 10f * s);
            AddTooltipPlain(mergeBtn, VPBTranslation.T("gallery.usertags.editor_merge_tip", "Merge selected tags into one name (opens confirmation)."));
            Color renameCol = new Color(0.26f, 0.34f, 0.46f, 1f);
            Sprite sprRename = UI.LoadIconSprite("vpb_icons/rename.png", Color.white);
            GameObject renameBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprRename, UserTagEditorOpenRenameDialog, renameCol, 10f * s);
            AddTooltipPlain(renameBtn, VPBTranslation.T("gallery.usertags.editor_rename_tip", "Rename the selected tag (opens dialog)."));
            GameObject impBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprImp, UserTagEditorBeginImportYaml, ioImpCol, 10f * s);
            AddTooltipPlain(impBtn, VPBTranslation.T("gallery.usertags.editor_import_tip", "Import tag assignments from a YAML file (tag→items or item→tags)."));
            GameObject expBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprExp, UserTagEditorBeginExportYaml, ioExpCol, 10f * s);
            AddTooltipPlain(expBtn, VPBTranslation.T("gallery.usertags.editor_export_tip", "Export two YAML files: tag→items and item→tags (same folder, shared base name)."));

            Color infoBackdrop = new Color(0.30f, 0.34f, 0.38f, 1f);
            GameObject infoBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprInfo, null, infoBackdrop, 10f * s);
            AddTooltipPlain(infoBtn, userTagEditorInfoWisdom);

            GameObject arPadR = new GameObject("PadR");
            arPadR.transform.SetParent(actionRow.transform, false);
            LayoutElement arPadRle = arPadR.AddComponent<LayoutElement>();
            arPadRle.flexibleWidth = 1f;
            arPadRle.minWidth = 0f;

            _userTagEditorMergeModalGo = new GameObject("UserTagEditorMergeModal");
            _userTagEditorMergeModalGo.transform.SetParent(dim.transform, false);
            RectTransform mmRootRt = _userTagEditorMergeModalGo.AddComponent<RectTransform>();
            mmRootRt.anchorMin = Vector2.zero;
            mmRootRt.anchorMax = Vector2.one;
            mmRootRt.sizeDelta = Vector2.zero;
            Image mmDim = _userTagEditorMergeModalGo.AddComponent<Image>();
            mmDim.color = new Color(0f, 0f, 0f, 0.5f);
            mmDim.raycastTarget = true;
            _userTagEditorMergeModalGo.SetActive(false);

            GameObject mmPanel = new GameObject("MergeDialogPanel");
            mmPanel.transform.SetParent(_userTagEditorMergeModalGo.transform, false);
            RectTransform mmPanelRt = mmPanel.AddComponent<RectTransform>();
            mmPanelRt.anchorMin = mmPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
            mmPanelRt.pivot = new Vector2(0.5f, 0.5f);
            mmPanelRt.sizeDelta = new Vector2(420f * s, 200f * s);
            Image mmPbg = mmPanel.AddComponent<Image>();
            mmPbg.color = new Color(0.14f, 0.14f, 0.17f, 1f);
            mmPbg.raycastTarget = true;
            VerticalLayoutGroup mmV = mmPanel.AddComponent<VerticalLayoutGroup>();
            mmV.padding = new RectOffset(Mathf.RoundToInt(14 * s), Mathf.RoundToInt(14 * s), Mathf.RoundToInt(12 * s), Mathf.RoundToInt(12 * s));
            mmV.spacing = 10f * s;
            mmV.childControlWidth = true;
            mmV.childControlHeight = true;
            mmV.childForceExpandWidth = true;

            GameObject mmTitleGo = new GameObject("MergeTitle");
            mmTitleGo.transform.SetParent(mmPanel.transform, false);
            _userTagEditorMergeModalTitleText = mmTitleGo.AddComponent<Text>();
            _userTagEditorMergeModalTitleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _userTagEditorMergeModalTitleText.fontSize = Mathf.Max(13, Mathf.RoundToInt(15f * u));
            _userTagEditorMergeModalTitleText.fontStyle = FontStyle.Bold;
            _userTagEditorMergeModalTitleText.color = Color.white;
            _userTagEditorMergeModalTitleText.text = VPBTranslation.T("gallery.usertags.editor_merge_dialog_title", "Merge tags into…");
            LayoutElement mmTle = mmTitleGo.AddComponent<LayoutElement>();
            mmTle.minHeight = 24f * s;
            mmTle.flexibleWidth = 1f;

            GameObject mmInGo = new GameObject("MergeDialogInput");
            mmInGo.transform.SetParent(mmPanel.transform, false);
            Image mmIBg = mmInGo.AddComponent<Image>();
            mmIBg.color = new Color(0.07f, 0.07f, 0.09f, 1f);
            LayoutElement mmILe = mmInGo.AddComponent<LayoutElement>();
            mmILe.flexibleWidth = 1f;
            mmILe.minHeight = 36f * s;
            GameObject mmTa = new GameObject("TextArea");
            mmTa.transform.SetParent(mmInGo.transform, false);
            RectTransform mmTaRt = mmTa.AddComponent<RectTransform>();
            mmTaRt.anchorMin = Vector2.zero;
            mmTaRt.anchorMax = Vector2.one;
            mmTaRt.offsetMin = new Vector2(8f * s, 4f * s);
            mmTaRt.offsetMax = new Vector2(-8f * s, -4f * s);
            GameObject mmPh = new GameObject("Placeholder");
            mmPh.transform.SetParent(mmTa.transform, false);
            Text mmPhT = mmPh.AddComponent<Text>();
            mmPhT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            mmPhT.fontSize = bodyFont;
            mmPhT.color = new Color(0.42f, 0.42f, 0.45f, 1f);
            mmPhT.text = VPBTranslation.T("gallery.usertags.editor_merge_ph", "New tag name…");
            RectTransform mmPhRt = mmPh.GetComponent<RectTransform>();
            mmPhRt.anchorMin = Vector2.zero;
            mmPhRt.anchorMax = Vector2.one;
            mmPhRt.sizeDelta = Vector2.zero;
            GameObject mmTx = new GameObject("Text");
            mmTx.transform.SetParent(mmTa.transform, false);
            Text mmTxT = mmTx.AddComponent<Text>();
            mmTxT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            mmTxT.fontSize = bodyFont;
            mmTxT.color = Color.white;
            RectTransform mmTxRt = mmTx.GetComponent<RectTransform>();
            mmTxRt.anchorMin = Vector2.zero;
            mmTxRt.anchorMax = Vector2.one;
            mmTxRt.sizeDelta = Vector2.zero;
            _userTagEditorMergeModalInput = mmInGo.AddComponent<InputField>();
            _userTagEditorMergeModalInput.textComponent = mmTxT;
            _userTagEditorMergeModalInput.placeholder = mmPhT;
            _userTagEditorMergeModalInput.lineType = InputField.LineType.SingleLine;

            GameObject mmBtnRow = new GameObject("MergeDialogButtons");
            mmBtnRow.transform.SetParent(mmPanel.transform, false);
            HorizontalLayoutGroup mmBH = mmBtnRow.AddComponent<HorizontalLayoutGroup>();
            mmBH.spacing = 8f * s;
            mmBH.childAlignment = TextAnchor.MiddleCenter;
            mmBH.childForceExpandWidth = true;
            LayoutElement mmBRle = mmBtnRow.AddComponent<LayoutElement>();
            mmBRle.minHeight = 40f * s;
            mmBRle.flexibleWidth = 1f;

            GameObject mmCancel = UI.CreateUIButton(mmBtnRow, 0f, 0f, VPBTranslation.T("gallery.usertags.editor_merge_cancel", "Cancel"), smallFont, 0f, 0f, AnchorPresets.stretchAll, UserTagEditorCloseMergeDialog);
            mmCancel.GetComponent<Image>().color = new Color(0.32f, 0.32f, 0.36f, 1f);
            LayoutElement mmCL = mmCancel.AddComponent<LayoutElement>();
            mmCL.flexibleWidth = 1f;
            mmCL.minHeight = 38f * s;

            GameObject mmOk = UI.CreateUIButton(mmBtnRow, 0f, 0f, VPBTranslation.T("gallery.usertags.editor_merge_confirm", "Merge"), smallFont, 0f, 0f, AnchorPresets.stretchAll, UserTagEditorConfirmMergeFromDialog);
            mmOk.GetComponent<Image>().color = new Color(0.22f, 0.42f, 0.58f, 1f);
            LayoutElement mmOL = mmOk.AddComponent<LayoutElement>();
            mmOL.flexibleWidth = 1f;
            mmOL.minHeight = 38f * s;

            _userTagEditorMergeModalGo.transform.SetAsLastSibling();

            _userTagEditorRenameModalGo = new GameObject("UserTagEditorRenameModal");
            _userTagEditorRenameModalGo.transform.SetParent(dim.transform, false);
            RectTransform rmRootRt = _userTagEditorRenameModalGo.AddComponent<RectTransform>();
            rmRootRt.anchorMin = Vector2.zero;
            rmRootRt.anchorMax = Vector2.one;
            rmRootRt.sizeDelta = Vector2.zero;
            Image rmDim = _userTagEditorRenameModalGo.AddComponent<Image>();
            rmDim.color = new Color(0f, 0f, 0f, 0.5f);
            rmDim.raycastTarget = true;
            _userTagEditorRenameModalGo.SetActive(false);

            GameObject rmPanel = new GameObject("RenameDialogPanel");
            rmPanel.transform.SetParent(_userTagEditorRenameModalGo.transform, false);
            RectTransform rmPanelRt = rmPanel.AddComponent<RectTransform>();
            rmPanelRt.anchorMin = rmPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
            rmPanelRt.pivot = new Vector2(0.5f, 0.5f);
            rmPanelRt.sizeDelta = new Vector2(420f * s, 200f * s);
            Image rmPbg = rmPanel.AddComponent<Image>();
            rmPbg.color = new Color(0.14f, 0.14f, 0.17f, 1f);
            rmPbg.raycastTarget = true;
            VerticalLayoutGroup rmV = rmPanel.AddComponent<VerticalLayoutGroup>();
            rmV.padding = new RectOffset(Mathf.RoundToInt(14 * s), Mathf.RoundToInt(14 * s), Mathf.RoundToInt(12 * s), Mathf.RoundToInt(12 * s));
            rmV.spacing = 10f * s;
            rmV.childControlWidth = true;
            rmV.childControlHeight = true;
            rmV.childForceExpandWidth = true;

            GameObject rmTitleGo = new GameObject("RenameTitle");
            rmTitleGo.transform.SetParent(rmPanel.transform, false);
            _userTagEditorRenameModalTitleText = rmTitleGo.AddComponent<Text>();
            _userTagEditorRenameModalTitleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _userTagEditorRenameModalTitleText.fontSize = Mathf.Max(13, Mathf.RoundToInt(15f * u));
            _userTagEditorRenameModalTitleText.fontStyle = FontStyle.Bold;
            _userTagEditorRenameModalTitleText.color = Color.white;
            _userTagEditorRenameModalTitleText.text = VPBTranslation.T("gallery.usertags.editor_rename_dialog_title_idle", "Rename to…");
            LayoutElement rmTle = rmTitleGo.AddComponent<LayoutElement>();
            rmTle.minHeight = 24f * s;
            rmTle.flexibleWidth = 1f;

            GameObject rmInGo = new GameObject("RenameDialogInput");
            rmInGo.transform.SetParent(rmPanel.transform, false);
            Image rmIBg = rmInGo.AddComponent<Image>();
            rmIBg.color = new Color(0.07f, 0.07f, 0.09f, 1f);
            LayoutElement rmILe = rmInGo.AddComponent<LayoutElement>();
            rmILe.flexibleWidth = 1f;
            rmILe.minHeight = 36f * s;
            GameObject rmTa = new GameObject("TextArea");
            rmTa.transform.SetParent(rmInGo.transform, false);
            RectTransform rmTaRt = rmTa.AddComponent<RectTransform>();
            rmTaRt.anchorMin = Vector2.zero;
            rmTaRt.anchorMax = Vector2.one;
            rmTaRt.offsetMin = new Vector2(8f * s, 4f * s);
            rmTaRt.offsetMax = new Vector2(-8f * s, -4f * s);
            GameObject rmPh = new GameObject("Placeholder");
            rmPh.transform.SetParent(rmTa.transform, false);
            Text rmPhT = rmPh.AddComponent<Text>();
            rmPhT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            rmPhT.fontSize = bodyFont;
            rmPhT.color = new Color(0.42f, 0.42f, 0.45f, 1f);
            rmPhT.text = VPBTranslation.T("gallery.usertags.editor_rename_ph", "New tag name…");
            RectTransform rmPhRt = rmPh.GetComponent<RectTransform>();
            rmPhRt.anchorMin = Vector2.zero;
            rmPhRt.anchorMax = Vector2.one;
            rmPhRt.sizeDelta = Vector2.zero;
            GameObject rmTx = new GameObject("Text");
            rmTx.transform.SetParent(rmTa.transform, false);
            Text rmTxT = rmTx.AddComponent<Text>();
            rmTxT.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            rmTxT.fontSize = bodyFont;
            rmTxT.color = Color.white;
            RectTransform rmTxRt = rmTx.GetComponent<RectTransform>();
            rmTxRt.anchorMin = Vector2.zero;
            rmTxRt.anchorMax = Vector2.one;
            rmTxRt.sizeDelta = Vector2.zero;
            _userTagEditorRenameModalInput = rmInGo.AddComponent<InputField>();
            _userTagEditorRenameModalInput.textComponent = rmTxT;
            _userTagEditorRenameModalInput.placeholder = rmPhT;
            _userTagEditorRenameModalInput.lineType = InputField.LineType.SingleLine;

            GameObject rmBtnRow = new GameObject("RenameDialogButtons");
            rmBtnRow.transform.SetParent(rmPanel.transform, false);
            HorizontalLayoutGroup rmBH = rmBtnRow.AddComponent<HorizontalLayoutGroup>();
            rmBH.spacing = 8f * s;
            rmBH.childAlignment = TextAnchor.MiddleCenter;
            rmBH.childForceExpandWidth = true;
            LayoutElement rmBRle = rmBtnRow.AddComponent<LayoutElement>();
            rmBRle.minHeight = 40f * s;
            rmBRle.flexibleWidth = 1f;

            GameObject rmCancel = UI.CreateUIButton(rmBtnRow, 0f, 0f, VPBTranslation.T("gallery.usertags.editor_merge_cancel", "Cancel"), smallFont, 0f, 0f, AnchorPresets.stretchAll, UserTagEditorCloseRenameDialog);
            rmCancel.GetComponent<Image>().color = new Color(0.32f, 0.32f, 0.36f, 1f);
            LayoutElement rmCL = rmCancel.AddComponent<LayoutElement>();
            rmCL.flexibleWidth = 1f;
            rmCL.minHeight = 38f * s;

            GameObject rmOk = UI.CreateUIButton(rmBtnRow, 0f, 0f, VPBTranslation.T("gallery.usertags.editor_rename_confirm", "Rename"), smallFont, 0f, 0f, AnchorPresets.stretchAll, UserTagEditorConfirmRenameFromDialog);
            rmOk.GetComponent<Image>().color = new Color(0.22f, 0.42f, 0.58f, 1f);
            LayoutElement rmOL = rmOk.AddComponent<LayoutElement>();
            rmOL.flexibleWidth = 1f;
            rmOL.minHeight = 38f * s;

            _userTagEditorRenameModalGo.transform.SetAsLastSibling();

            SetLayerRecursive(dim, backgroundBoxGO.layer);

            userTagsCached = false;
            CacheUserTagsSideTab();
            UserTagEditorSetTitleCount(cachedUserTagSideTab.Count);

            _userTagEditorRoot = dim;
            _userTagEditorRoot.SetActive(false);
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

        private void UserTagEditorOnCreateTagsClicked()
        {
            string raw = _userTagEditorNewTagInput != null ? _userTagEditorNewTagInput.text : "";
            List<string> parts = ParseGalleryUserTagPaste(raw);
            if (parts.Count == 0)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T(
                        "gallery.usertags.editor_no_names",
                        "No valid tag names. Use 1–30 characters: letters, digits, spaces, - and _."),
                    2f);
                return;
            }
            int ok = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (VpbLocalDatabase.TryEnsureGalleryUserTagInVocabulary(parts[i], out string norm) && !string.IsNullOrEmpty(norm))
                    ok++;
            }
            if (_userTagEditorNewTagInput != null) _userTagEditorNewTagInput.text = "";
            InvalidateTags();
            userTagsCached = false;
            RebuildUserTagEditorRows();
            try { UpdateTabs(); } catch { }
            ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.usertags.editor_created_n", "Created {0} tag(s)."), ok), 2f);
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
                        "Choose tag row(s) and enter valid merge target (1–30 chars: letters, digits, spaces, - _)."),
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
                        "Enter valid new name (1–30 chars: letters, digits, spaces, - _). Renamed tags must stay valid length."),
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
                        "Enter valid new name (1–30 chars: letters, digits, spaces, - _). Renamed tags must stay valid length."),
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

            string yamlTag = GalleryUserTagYamlBrain.BuildTagToItemsYaml(tagToItems);
            string yamlItem = GalleryUserTagYamlBrain.BuildItemToTagsYaml(itemToTags);
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
                    out string err))
            {
                ShowTemporaryStatus(
                    string.Format(VPBTranslation.T("gallery.usertags.editor_import_parse_fail", "Import parse failed: {0}"), err ?? "?"),
                    3f);
                return;
            }

            int nLinks = UserTagEditorApplyImportedAssignments(tagToItemKeys, itemKeyToTags, out int nUnassigned);
            InvalidateTags();
            userTagsCached = false;
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
            ShowTemporaryStatus(importMsg, 2.8f);
        }

        /// <summary>Merges tag→items and item→tags maps (one usually empty), applies DB assignments.</summary>
        private int UserTagEditorApplyImportedAssignments(
            Dictionary<string, List<string>> tagToItemKeys,
            Dictionary<string, List<string>> itemKeyToTags,
            out int unassignedTagsEnsured)
        {
            unassignedTagsEnsured = 0;
            var rowTags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            if (tagToItemKeys != null)
            {
                foreach (var kv in tagToItemKeys)
                {
                    string nt = VpbLocalDatabase.NormalizeGalleryUserTagName(kv.Key);
                    if (string.IsNullOrEmpty(nt)) continue;
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
                        string ntag = VpbLocalDatabase.NormalizeGalleryUserTagName(tags[i]);
                        if (!string.IsNullOrEmpty(ntag)) set.Add(ntag);
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

            for (int i = _userTagEditorRowsParent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_userTagEditorRowsParent.GetChild(i).gameObject);

            if (!userTagsCached) CacheUserTagsSideTab();
            UserTagEditorSetTitleCount(cachedUserTagSideTab.Count);

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;
            int rowFont = Mathf.Max(12, Mathf.RoundToInt(14f * u));

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
                Image bg = rowGo.AddComponent<Image>();
                bool sel = _userTagEditorRowSelection.Contains(nameSnap);
                bg.color = sel ? selCol : baseCol;
                _userTagEditorRowVisuals.Add(new UserTagEditorRowVisual { Name = nameSnap, Bg = bg });
                LayoutElement rle = rowGo.AddComponent<LayoutElement>();
                rle.minHeight = rowH;
                rle.preferredHeight = rowH;
                rle.flexibleWidth = 1f;

                Button btn = rowGo.AddComponent<Button>();
                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                btn.colors = cb;
                btn.transition = Selectable.Transition.None;
                int riCapture = ri;
                btn.onClick.AddListener(() => UserTagEditorOnRowClicked(nameSnap, riCapture, rows, bg, baseCol, selCol));

                GameObject txtGo = new GameObject("Label");
                txtGo.transform.SetParent(rowGo.transform, false);
                Text txt = txtGo.AddComponent<Text>();
                txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.fontSize = rowFont;
                txt.color = new Color(0.93f, 0.93f, 0.95f, 1f);
                txt.text = label;
                txt.alignment = TextAnchor.MiddleLeft;
                RectTransform trt = txtGo.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(10f * s, 2f * s);
                trt.offsetMax = new Vector2(-10f * s, -2f * s);
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

        private void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Panel == null) return;
            if (eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = true;
            _dragging = false;
            _pressPos = eventData.position;
            _pressTime = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            if (_dragging)
                EndManualDrag(eventData);
        }

        private void Update()
        {
            if (Panel == null) return;

            if (_dragging)
            {
                UpdateGhostPosition();
                if (!IsAppliedRowDrag)
                    RefreshUserTagApplyDragHoverStatus();
                // Mouse-up may not route to source once we disable raycasts.
                if (Input.GetMouseButtonUp(0))
                    EndManualDrag(null);
                return;
            }

            if (!_pressed) return;

            // Start manual drag once pointer moved enough. Works even when ScrollRect eats drag events.
            Vector2 cur = Input.mousePosition;
            float dist = (cur - _pressPos).magnitude;
            if (dist < 10f) return;
            BeginManualDrag();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Keep Unity path (desktop). Manual path handles ScrollRect conflicts.
            if (Panel == null) return;
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

            Image bg = _ghost.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.18f, 0.85f);
            bg.raycastTarget = false;

            GameObject tgo = new GameObject("Text");
            tgo.transform.SetParent(_ghost.transform, false);
            RectTransform trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 6f);
            trt.offsetMax = new Vector2(-10f, -6f);

            _ghostText = tgo.AddComponent<Text>();
            _ghostText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _ghostText.fontSize = 16;
            _ghostText.color = new Color(0.95f, 0.95f, 0.97f, 1f);
            _ghostText.alignment = TextAnchor.MiddleLeft;
            _ghostText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _ghostText.verticalOverflow = VerticalWrapMode.Truncate;
            _ghostText.text = tags.Count == 1 ? ("Tag: " + tags[0]) : ("Tags: " + tags.Count);
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
