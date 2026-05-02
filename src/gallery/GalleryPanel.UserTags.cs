using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
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

        private bool TryGetVarCatMemForUserTags(FileEntry fe, out string pkgUid, out string internalPath)
        {
            pkgUid = "";
            internalPath = "";
            if (fe == null) return false;
            string cat = currentCategoryTitle ?? "";
            if (titleText != null && string.IsNullOrEmpty(cat)) cat = titleText.text ?? "";
            if (string.IsNullOrEmpty(cat)) return false;
            VarFileEntry vfe = fe as VarFileEntry;
            if (vfe == null || vfe.Package == null) return false;
            pkgUid = vfe.Package.Uid ?? "";
            internalPath = vfe.InternalPath ?? "";
            return !string.IsNullOrEmpty(pkgUid) && !string.IsNullOrEmpty(internalPath);
        }

        /// <summary>Rebuild lower «Applied to selection» pane when grid/list selection changes (same partial class as <see cref="GalleryPanel.Actions.RefreshSelectionVisuals"/>).</summary>
        private void RefreshAppliedUserTagsPaneAfterSelectionChange()
        {
            userTagAppliedRemoveFocus = null;
            bool leftUt = leftActiveContent == ContentType.UserTags && leftSubTabScrollGO != null && leftSubTabScrollGO.activeSelf && leftSubTabContainerGO != null;
            bool rightUt = rightActiveContent == ContentType.UserTags && rightSubTabScrollGO != null && rightSubTabScrollGO.activeSelf && rightSubTabContainerGO != null;
            if (!leftUt && !rightUt) return;
            try
            {
                if (leftUt) UpdateTabs(ContentType.UserTagsApplied, leftSubTabContainerGO, leftSubActiveTabButtons, true);
                if (rightUt) UpdateTabs(ContentType.UserTagsApplied, rightSubTabContainerGO, rightSubActiveTabButtons, false);
            }
            catch { }
        }

        /// <summary>Toolbox shortcut: open User Tags side list.</summary>
        private void OpenUserTagsSidePanelFromToolbox()
        {
            if (isFixedLocally)
            {
                if (leftActiveContent != ContentType.UserTags)
                    ToggleLeft(ContentType.UserTags);
            }
            else
            {
                if (rightActiveContent != ContentType.UserTags)
                    ToggleRight(ContentType.UserTags);
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
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            string cat = currentCategoryTitle ?? "";
            if (titleText != null && string.IsNullOrEmpty(cat)) cat = titleText.text ?? "";
            if (string.IsNullOrEmpty(cat)) return;

            var uniqueRows = new List<KeyValuePair<string, string>>(selectedFiles.Count);
            var seenRow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nSel = selectedFiles.Count;
            for (int i = 0; i < nSel; i++)
            {
                FileEntry fe = selectedFiles[i];
                string pkg, ip;
                if (!TryGetVarCatMemForUserTags(fe, out pkg, out ip)) continue;
                string rk = pkg + "\n" + ip;
                if (!seenRow.Add(rk)) continue;
                uniqueRows.Add(new KeyValuePair<string, string>(pkg, ip));
            }
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!VpbLocalDatabase.TryAccumulateGalleryUserTagSelectionCounts(cat, uniqueRows, counts)) return;
            foreach (var kv in counts)
                cachedAppliedUserTagsSelection.Add(new UserTagSideTabEntry { Name = kv.Key, Count = kv.Value });
            cachedAppliedUserTagsSelection.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureUserTagsAppliedToolbar(Transform container)
        {
            if (container == null || backgroundBoxGO == null) return;
            Transform legacyTb = container.Find("VPB_UserTagsAppliedToolbar_v1");
            if (legacyTb != null)
                UnityEngine.Object.Destroy(legacyTb.gameObject);
            if (container.Find("VPB_UserTagsAppliedToolbar_v2") != null) return;

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;

            GameObject root = new GameObject("VPB_UserTagsAppliedToolbar_v2");
            root.transform.SetParent(container, false);
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

            LayoutElement titleRowLe = titleRow.AddComponent<LayoutElement>();
            titleRowLe.minHeight = 30f * s;
            titleRowLe.preferredHeight = 34f * s;
            titleRowLe.flexibleWidth = 1f;

            GameObject titleGo = new GameObject("AppliedTitleText");
            titleGo.transform.SetParent(titleRow.transform, false);
            Text titleTxt = titleGo.AddComponent<Text>();
            titleTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleTxt.fontSize = Mathf.Max(14, Mathf.RoundToInt(17f * u));
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = new Color(0.88f, 0.88f, 0.92f, 1f);
            titleTxt.text = VPBTranslation.T("gallery.usertags.applied_section_title", "Applied");
            LayoutElement titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;
            titleLe.minHeight = 28f * s;

            float delSz = Mathf.Max(26f, 30f * s);
            Sprite delSpr = UI.LoadIconSprite("vpb_icons/delete.png", new Color(0.92f, 0.92f, 0.92f, 1f));
            GameObject delBtn = UI.CreateSideTabSquareIconButton(titleRow, delSz, delSpr, RemoveFocusedAppliedUserTagFromSelection, new Color(0.5f, 0.22f, 0.22f, 1f), 5f * s);
            delBtn.name = "RemoveAppliedIconBtn";
            AddTooltipPlain(delBtn, VPBTranslation.T("gallery.usertags.remove_applied_tooltip", "Remove focused tag from selection (click a row in the list below first)."));
        }

        private void SyncUserTagsAppliedToolbarDropZones(Transform container)
        {
            if (container == null) return;
            Transform tb = container.Find("VPB_UserTagsAppliedToolbar_v2");
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
                le.minHeight = 96f * s;
                le.preferredHeight = 120f * s;
                le.flexibleWidth = 1f;
                UserTagApplyDropZone dz = stripGo.AddComponent<UserTagApplyDropZone>();
                dz.Panel = this;
                AddTooltipPlain(stripGo, VPBTranslation.T("gallery.usertags.apply_drop_zone_tip", "Drop tags here to apply to selection."));
            }
            else
            {
                stripGo = stripT.gameObject;
                UserTagApplyDropZone dz = stripGo.GetComponent<UserTagApplyDropZone>();
                if (dz == null) dz = stripGo.AddComponent<UserTagApplyDropZone>();
                dz.Panel = this;
            }
            stripGo.transform.SetAsLastSibling();
        }

        internal void UserTagPickDragBeginPayload(string primaryTag, List<string> tagsOut)
        {
            if (tagsOut == null) return;
            tagsOut.Clear();
            if (string.IsNullOrEmpty(primaryTag)) return;
            if (activeUserTags != null && activeUserTags.Contains(primaryTag))
            {
                foreach (string t in activeUserTags)
                    tagsOut.Add(t);
            }
            else
                tagsOut.Add(primaryTag);
        }

        internal void UserTagApplyDroppedTags(List<string> tags)
        {
            if (tags == null || tags.Count == 0) return;
            ApplyTagsToSelectedPackages(new List<string>(tags), remove: false);
        }

        private void RemoveFocusedAppliedUserTagFromSelection()
        {
            if (string.IsNullOrEmpty(userTagAppliedRemoveFocus))
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.pick_applied_first", "Select a tag in the list below (click its row)."), 2.2f);
                return;
            }
            var one = new List<string> { userTagAppliedRemoveFocus };
            ApplyTagsToSelectedPackages(one, remove: true);
            userTagAppliedRemoveFocus = null;
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
            if (tags == null || tags.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.no_tags", "No tags parsed."), 1.5f);
                return;
            }

            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            string cat = currentCategoryTitle ?? (titleText != null ? titleText.text : "");
            int touched = 0;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry fe = selectedFiles[i];
                string pkg, ip;
                if (!TryGetVarCatMemForUserTags(fe, out pkg, out ip)) continue;
                if (remove)
                {
                    int _d;
                    if (VpbLocalDatabase.TryRemoveGalleryUserTagsFromRow(cat, pkg, ip, tags, out _d))
                        touched++;
                }
                else
                {
                    int ins;
                    if (VpbLocalDatabase.TryAssignGalleryUserTagsToRow(cat, pkg, ip, tags, out ins))
                        touched++;
                }
            }

            InvalidateTags();
            userTagsCached = false;
            RefreshFiles(true);
            UpdateTabs();
            ShowTemporaryStatus(string.Format(VPBTranslation.T("gallery.usertags.done_count", "Updated {0} item(s)."), touched), 2f);
        }

        private void EnsureUserTagSideTabBulkBlock(Transform container)
        {
            if (container == null || backgroundBoxGO == null) return;
            Transform legacy = container.Find("VPB_UserTagBulkBlock");
            if (legacy != null)
                UnityEngine.Object.Destroy(legacy.gameObject);
            Transform legacyV2 = container.Find("VPB_UserTagBulkBlock_v2");
            if (legacyV2 != null)
                UnityEngine.Object.Destroy(legacyV2.gameObject);
            if (container.Find("VPB_UserTagBulkBlock_v3") != null) return;

            float s = VPBConfig.Instance != null ? VPBConfig.Instance.CurrentInnerPaneScale : 1f;
            float u = s * 1.38f;

            GameObject root = new GameObject("VPB_UserTagBulkBlock_v3");
            root.transform.SetParent(container, false);
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
            titleTxt.fontSize = Mathf.Max(15, Mathf.RoundToInt(19f * u));
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = Color.white;
            titleTxt.text = VPBTranslation.T("gallery.usertags.overlay_title", "Available");
            LayoutElement titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.minHeight = 30f * s;
            titleLe.preferredHeight = 34f * s;
            titleLe.flexibleWidth = 1f;

            GameObject btnRow = new GameObject("BulkBtnRow");
            btnRow.transform.SetParent(root.transform, false);
            HorizontalLayoutGroup hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f * s;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            LayoutElement rowLe = btnRow.AddComponent<LayoutElement>();
            rowLe.minHeight = 46f * s;
            rowLe.preferredHeight = 48f * s;
            rowLe.flexibleWidth = 1f;

            int btnFont = Mathf.Max(13, Mathf.RoundToInt(16f * u));
            GameObject editBtn = UI.CreateUIButton(btnRow, 0f, 0f, VPBTranslation.T("gallery.usertags.btn_edit", "Edit"), btnFont, 0f, 0f, AnchorPresets.stretchAll, ShowUserTagListEditor);
            editBtn.GetComponent<Image>().color = new Color(0.28f, 0.35f, 0.42f, 1f);
            LayoutElement editLe = editBtn.AddComponent<LayoutElement>();
            editLe.minWidth = 0f;
            editLe.preferredWidth = 0f;
            editLe.flexibleWidth = 1f;
            editLe.minHeight = 44f * s;
            AddTooltipPlain(editBtn, VPBTranslation.T("gallery.usertags.btn_edit_tooltip", "Open tag database editor: search, sort, create/remove/merge tags."));

            GameObject applyBtn = UI.CreateUIButton(btnRow, 0f, 0f, VPBTranslation.T("gallery.usertags.btn_apply_checked", "Apply"), btnFont, 0f, 0f, AnchorPresets.stretchAll, ApplyActiveFilterUserTagsToSelection);
            applyBtn.GetComponent<Image>().color = new Color(0.22f, 0.38f, 0.55f, 1f);
            LayoutElement applyLe = applyBtn.AddComponent<LayoutElement>();
            applyLe.minWidth = 0f;
            applyLe.preferredWidth = 0f;
            applyLe.flexibleWidth = 1f;
            applyLe.minHeight = 44f * s;
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
            Sprite sprPlus = UI.LoadIconSprite("vpb_icons/tag_plus.png", new Color(0.92f, 0.92f, 0.92f, 1f));
            Sprite sprMinus = UI.LoadIconSprite("vpb_icons/tag_minus.png", new Color(0.92f, 0.92f, 0.92f, 1f));
            Sprite sprMerge = UI.LoadIconSprite("vpb_icons/arrow_merge.png", new Color(0.92f, 0.92f, 0.92f, 1f));
            Sprite sprImp = UI.LoadIconSprite("vpb_icons/file_import.png", new Color(0.92f, 0.92f, 0.92f, 1f));
            Sprite sprExp = UI.LoadIconSprite("vpb_icons/file_export.png", new Color(0.92f, 0.92f, 0.92f, 1f));
            Sprite sprInfo = UI.LoadIconSprite("vpb_icons/info_square.png", new Color(0.92f, 0.92f, 0.92f, 1f));
            GameObject createTagsBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprPlus, UserTagEditorOnCreateTagsClicked, createCol, 10f * s);
            AddTooltipPlain(createTagsBtn, VPBTranslation.T("gallery.usertags.editor_create_tags_tip", "Create tag rows from the text field (comma / line separated)."));
            GameObject removeSelBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprMinus, UserTagEditorRemoveSelectedFromDb, removeCol, 10f * s);
            AddTooltipPlain(removeSelBtn, VPBTranslation.T("gallery.usertags.editor_remove_selected_tip", "Delete selected tag(s) from the database for all items (cannot undo)."));
            GameObject mergeBtn = UI.CreateSideTabSquareIconButton(actionRow, actSq, sprMerge, UserTagEditorOpenMergeDialog, mergeCol, 10f * s);
            AddTooltipPlain(mergeBtn, VPBTranslation.T("gallery.usertags.editor_merge_tip", "Merge selected tags into one name (opens confirmation)."));
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
            if (_userTagEditorFilterInput != null) _userTagEditorFilterInput.text = "";
            _userTagEditorRowSelection.Clear();
            _userTagEditorAnchorTag = null;
            RebuildUserTagEditorRows();
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
                    RebuildUserTagEditorRows();
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
            try { RefreshFiles(true); UpdateTabs(); } catch { }
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
            try { RefreshFiles(true); UpdateTabs(); } catch { }
            RebuildUserTagEditorRows();
            ShowTemporaryStatus(
                string.Format(VPBTranslation.T("gallery.usertags.editor_merge_done", "Merged into «{0}». Updated {1} item-tag link(s)."), normTarget, nTouch),
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

            int nLinks = UserTagEditorApplyImportedAssignments(tagToItemKeys, itemKeyToTags);
            InvalidateTags();
            userTagsCached = false;
            try { RefreshFiles(true); UpdateTabs(); } catch { }
            RebuildUserTagEditorRows();
            ShowTemporaryStatus(
                string.Format(VPBTranslation.T("gallery.usertags.editor_import_done", "Import applied: {0} tag link(s) updated."), nLinks),
                2.8f);
        }

        /// <summary>Merges tag→items and item→tags maps (one usually empty), applies DB assignments.</summary>
        private int UserTagEditorApplyImportedAssignments(
            Dictionary<string, List<string>> tagToItemKeys,
            Dictionary<string, List<string>> itemKeyToTags)
        {
            var rowTags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            if (tagToItemKeys != null)
            {
                foreach (var kv in tagToItemKeys)
                {
                    string nt = VpbLocalDatabase.NormalizeGalleryUserTagName(kv.Key);
                    if (string.IsNullOrEmpty(nt)) continue;
                    List<string> items = kv.Value;
                    if (items == null) continue;
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

            userTagsCached = false;
            CacheUserTagsSideTab();
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

        public static void Clear()
        {
            PendingTags = null;
        }
    }

    internal sealed class UserTagPickDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public GalleryPanel Panel;
        public string PrimaryTag;
        private CanvasGroup _cg;

        private void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Panel == null) return;
            var list = new List<string>();
            Panel.UserTagPickDragBeginPayload(PrimaryTag, list);
            if (list.Count == 0) return;
            UserTagDragSession.PendingTags = list;
            _cg.alpha = 0.65f;
            _cg.blocksRaycasts = false;
        }

        public void OnDrag(PointerEventData eventData) { }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_cg != null)
            {
                _cg.alpha = 1f;
                _cg.blocksRaycasts = true;
            }
            UserTagDragSession.Clear();
        }
    }

    internal sealed class UserTagApplyDropZone : MonoBehaviour, IDropHandler
    {
        public GalleryPanel Panel;

        public void OnDrop(PointerEventData eventData)
        {
            if (Panel == null) return;
            List<string> tags = UserTagDragSession.PendingTags;
            if (tags == null || tags.Count == 0) return;
            Panel.UserTagApplyDroppedTags(tags);
            UserTagDragSession.Clear();
        }
    }
}
