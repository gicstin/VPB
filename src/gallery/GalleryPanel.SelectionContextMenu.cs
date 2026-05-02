using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        // Selection toolbox ("tbox")
        private GameObject tbox;
        private Text tboxLabel;
        private Text tboxHintLabel;
        private GameObject tboxCopyPkgNamesBtn;
        private GameObject tboxDeleteBtn;
        private GameObject tboxRemoveHistoryBtn;
        private GameObject tboxCleanupBtn;
        private GameObject tboxCleanupApplyBtn;
        private GameObject tboxCleanupFilterAllBtn;
        private GameObject tboxCleanupFilterDupBtn;
        private GameObject tboxCleanupFilterOldBtn;
        private GameObject tboxCleanupFilterDamagedBtn;
        private GameObject tboxCleanupSelectVisibleBtn;
        private GameObject tboxCleanupSelectDupBtn;
        private GameObject tboxCleanupSelectOldBtn;
        private GameObject tboxCleanupSelectDamagedBtn;
        private GameObject tboxCleanupClearBtn;
        private GameObject tboxCleanupAddExcludeBtn;
        private GameObject tboxCleanupRemoveExcludeBtn;
        private GameObject tboxAutoInstallBtn;
        private GameObject tboxDisableAutoInstallBtn;
        private GameObject tboxHideBtn;
        private GameObject tboxUnhideBtn;
        private GameObject tboxScanWhitelistTemporaryBtn;
        private GameObject tboxScanWhitelistAddFolderBtn;
        private GameObject tboxScanWhitelistRemoveFolderBtn;
        private GameObject tboxLoadBtn;
        private GameObject tboxUnloadBtn;
        private GameObject tboxLoadDepsBtn;
        private GameObject tboxCacheTexturesBtn;
        private GameObject tboxJsonParserBenchBtn;
        private GameObject tboxOpenHubBtn;
        private GameObject tboxSceneImportBtn;
        private GameObject tboxSelectAllBtn;
        private GameObject tboxClearSelectionBtn;
        private GameObject tboxSettingsSaveBtn;
        private GameObject tboxSettingsCancelBtn;

        // Dependency filter controls in toolbox
        private GameObject tboxFilterModeRowGO;
        private RectTransform tboxFilterModeRowRT;
        private LayoutElement tboxFilterModeRowLE;
        private HorizontalLayoutGroup tboxFilterModeRowHLG;
        private GameObject tboxFilterBackBtn;
        private GameObject tboxFilterClearBtn;
        private Text tboxFilterModeText;

        private static void SetTboxButtonEnabledVisual(GameObject go, bool enabled, float disabledAlpha = 0.35f)
        {
            if (go == null) return;
            try
            {
                var btn = go.GetComponent<Button>();
                if (btn != null) btn.interactable = enabled;

                var cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = enabled ? 1f : disabledAlpha;
                cg.blocksRaycasts = enabled;
            }
            catch { }
        }

        // Copy Names icon swap (clipboard list -> clipboard check on success)
        private Sprite tboxClipboardListSprite;
        private Sprite tboxClipboardCheckSprite;
        private Image  tboxCopyNamesIconImage;
        private Coroutine tboxCopyNamesIconPulseCo;
        private Coroutine tboxCopyNamesTooltipCo;
        private bool tboxCopyNamesTooltipHovered = false;
        private string tboxCopyNamesTooltipLast = null;

        // Responsive tbox action buttons: 1–2 rows, flexible widths
        private GameObject tboxButtonsFlexRoot;
        private RectTransform tboxButtonsFlexRootRT;
        private GameObject tboxBtnRow0GO;
        private GameObject tboxBtnRow1GO;
        private RectTransform tboxBtnRow0RT;
        private RectTransform tboxBtnRow1RT;
        private LayoutElement tboxBtnRow0LE;
        private LayoutElement tboxBtnRow1LE;
        private HorizontalLayoutGroup tboxBtnRow0HLG;
        private HorizontalLayoutGroup tboxBtnRow1HLG;
        private GameObject tboxButtonStash;
        private int tboxButtonLayoutRows = 1;
        private float tboxLastFlexAvailW = -1f;
        private const float tboxBtnRowGap = 4f;

        private static void TboxConfigureActionButtonFlex(GameObject go, float minW, float prefW, float innerRowH)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.minWidth = minW;
            le.preferredWidth = prefW;
            le.flexibleWidth = 1f;
            le.minHeight = innerRowH;
            le.preferredHeight = innerRowH;
            le.flexibleHeight = 1f;
        }

        private void TboxSetAllFlexActionButtonHeights(float innerRowH)
        {
            void one(GameObject go)
            {
                if (go == null) return;
                var le = go.GetComponent<LayoutElement>();
                if (le == null) return;
                le.minHeight = innerRowH;
                le.preferredHeight = innerRowH;
            }
            one(tboxSettingsCancelBtn);
            one(tboxSettingsSaveBtn);
            one(tboxDisableAutoInstallBtn);
            one(tboxUnhideBtn);
            one(tboxHideBtn);
            one(tboxScanWhitelistTemporaryBtn);
            one(tboxScanWhitelistAddFolderBtn);
            one(tboxScanWhitelistRemoveFolderBtn);
            one(tboxAutoInstallBtn);
            one(tboxDeleteBtn);
            one(tboxRemoveHistoryBtn);
            one(tboxCleanupBtn);
            one(tboxCleanupApplyBtn);
            one(tboxCleanupFilterAllBtn);
            one(tboxCleanupFilterDupBtn);
            one(tboxCleanupFilterOldBtn);
            one(tboxCleanupFilterDamagedBtn);
            one(tboxCleanupSelectVisibleBtn);
            one(tboxCleanupSelectDupBtn);
            one(tboxCleanupSelectOldBtn);
            one(tboxCleanupSelectDamagedBtn);
            one(tboxCleanupClearBtn);
            one(tboxCleanupAddExcludeBtn);
            one(tboxCleanupRemoveExcludeBtn);
            one(tboxLoadBtn);
            one(tboxUnloadBtn);
            one(tboxLoadDepsBtn);
            one(tboxCacheTexturesBtn);
            one(tboxJsonParserBenchBtn);
            one(tboxOpenHubBtn);
            one(tboxCopyPkgNamesBtn);
            one(tboxSceneImportBtn);
            one(tboxSelectAllBtn);
            one(tboxClearSelectionBtn);
        }

        private void TboxDetachAllActionButtonsForLayout()
        {
            if (tboxButtonStash == null) return;
            Transform p = tboxButtonStash.transform;
            void d(GameObject go)
            {
                if (go != null) go.transform.SetParent(p, false);
            }
            d(tboxSettingsCancelBtn);
            d(tboxSettingsSaveBtn);
            d(tboxDisableAutoInstallBtn);
            d(tboxUnhideBtn);
            d(tboxHideBtn);
            d(tboxScanWhitelistTemporaryBtn);
            d(tboxScanWhitelistAddFolderBtn);
            d(tboxScanWhitelistRemoveFolderBtn);
            d(tboxAutoInstallBtn);
            d(tboxDeleteBtn);
            d(tboxRemoveHistoryBtn);
            d(tboxCleanupBtn);
            d(tboxCleanupApplyBtn);
            d(tboxCleanupFilterAllBtn);
            d(tboxCleanupFilterDupBtn);
            d(tboxCleanupFilterOldBtn);
            d(tboxCleanupFilterDamagedBtn);
            d(tboxCleanupSelectVisibleBtn);
            d(tboxCleanupSelectDupBtn);
            d(tboxCleanupSelectOldBtn);
            d(tboxCleanupSelectDamagedBtn);
            d(tboxCleanupClearBtn);
            d(tboxCleanupAddExcludeBtn);
            d(tboxCleanupRemoveExcludeBtn);
            d(tboxLoadBtn);
            d(tboxUnloadBtn);
            d(tboxLoadDepsBtn);
            d(tboxCacheTexturesBtn);
            d(tboxJsonParserBenchBtn);
            d(tboxOpenHubBtn);
            d(tboxCopyPkgNamesBtn);
            d(tboxSceneImportBtn);
            d(tboxSelectAllBtn);
            d(tboxClearSelectionBtn);
            foreach (var go in tboxPersonAtomBtns) { if (go != null) go.transform.SetParent(p, false); }
        }

        private static void TboxPopulateRowLtr(HorizontalLayoutGroup hlg, List<GameObject> listLtr)
        {
            if (hlg == null || listLtr == null) return;
            Transform t = hlg.transform;
            for (int i = 0; i < listLtr.Count; i++)
            {
                GameObject go = listLtr[i];
                if (go == null) continue;
                go.transform.SetParent(t, false);
            }
        }

        private static void TboxPopulateRowFromRtlPack(HorizontalLayoutGroup hlg, List<GameObject> rtlPacked)
        {
            if (hlg == null || rtlPacked == null) return;
            Transform t = hlg.transform;
            for (int i = rtlPacked.Count - 1; i >= 0; i--)
            {
                GameObject go = rtlPacked[i];
                if (go == null) continue;
                go.transform.SetParent(t, false);
            }
        }

        /// <summary>Wrap tbox actions to two rows when minimum widths no longer fit; stretch button band height.</summary>
        private void RefreshTboxFlexButtonLayout()
        {
            if (tboxButtonsFlexRootRT == null || tboxBtnRow0HLG == null || tboxBtnRow1HLG == null) return;

            float innerH = Mathf.Max(34f, tboxInfoRowHeight - 8f);
            if (tboxBtnRow0LE != null) { tboxBtnRow0LE.minHeight = innerH; tboxBtnRow0LE.preferredHeight = innerH; }
            if (tboxBtnRow1LE != null) { tboxBtnRow1LE.minHeight = innerH; tboxBtnRow1LE.preferredHeight = innerH; }
            TboxSetAllFlexActionButtonHeights(innerH);

            Canvas.ForceUpdateCanvases();
            float avail = tboxButtonsFlexRootRT.rect.width;
            if (avail < 8f)
                avail = (tboxLastFlexAvailW > 8f) ? tboxLastFlexAvailW : 640f;

            const float gap = 10f;

            bool TryGetWidths(GameObject go, out float minW, out float prefW)
            {
                minW = 56f;
                prefW = 100f;
                if (go == null) return false;
                var le = go.GetComponent<LayoutElement>();
                if (le == null) return false;
                minW = le.minWidth;
                prefW = le.preferredWidth;
                return true;
            }

            float RowPrefSum(List<GameObject> row)
            {
                float s = 0f;
                for (int i = 0; i < row.Count; i++)
                {
                    if (!TryGetWidths(row[i], out _, out float pw)) continue;
                    s += pw;
                    if (i > 0) s += gap;
                }
                return s;
            }

            bool vis(GameObject go) => go != null && go.activeSelf;

            // IMPORTANT: only layout active buttons. Hidden buttons still have widths and would force 2-row wrap.
            var ltr = new List<GameObject>(28 + tboxPersonAtomBtns.Count);

            // Settings mode: fixed 1-row layout, only CANCEL + SAVE.
            if (IsSettingsPanelOpen())
            {
                if (vis(tboxSettingsCancelBtn)) ltr.Add(tboxSettingsCancelBtn);
                if (vis(tboxSettingsSaveBtn)) ltr.Add(tboxSettingsSaveBtn);
            }
            else
            {
                // Person atom target buttons appear leftmost in the toolbar
                foreach (var go in tboxPersonAtomBtns) { if (vis(go)) ltr.Add(go); }
                // Keep these buttons in a fixed order to avoid layout shuffling as state flips.
                if (vis(tboxSettingsCancelBtn)) ltr.Add(tboxSettingsCancelBtn);
                if (vis(tboxSettingsSaveBtn)) ltr.Add(tboxSettingsSaveBtn);
                if (vis(tboxDisableAutoInstallBtn)) ltr.Add(tboxDisableAutoInstallBtn);
                if (vis(tboxUnhideBtn)) ltr.Add(tboxUnhideBtn);
                if (vis(tboxHideBtn)) ltr.Add(tboxHideBtn);
                if (vis(tboxScanWhitelistTemporaryBtn)) ltr.Add(tboxScanWhitelistTemporaryBtn);
                if (vis(tboxScanWhitelistAddFolderBtn)) ltr.Add(tboxScanWhitelistAddFolderBtn);
                if (vis(tboxScanWhitelistRemoveFolderBtn)) ltr.Add(tboxScanWhitelistRemoveFolderBtn);
                if (vis(tboxAutoInstallBtn)) ltr.Add(tboxAutoInstallBtn);
                if (vis(tboxDeleteBtn)) ltr.Add(tboxDeleteBtn);
                if (vis(tboxRemoveHistoryBtn)) ltr.Add(tboxRemoveHistoryBtn);
                if (vis(tboxCleanupBtn)) ltr.Add(tboxCleanupBtn);
                if (vis(tboxCleanupApplyBtn)) ltr.Add(tboxCleanupApplyBtn);
                if (vis(tboxCleanupFilterAllBtn)) ltr.Add(tboxCleanupFilterAllBtn);
                if (vis(tboxCleanupFilterDupBtn)) ltr.Add(tboxCleanupFilterDupBtn);
                if (vis(tboxCleanupFilterOldBtn)) ltr.Add(tboxCleanupFilterOldBtn);
                if (vis(tboxCleanupFilterDamagedBtn)) ltr.Add(tboxCleanupFilterDamagedBtn);
                if (vis(tboxCleanupSelectVisibleBtn)) ltr.Add(tboxCleanupSelectVisibleBtn);
                if (vis(tboxCleanupSelectDupBtn)) ltr.Add(tboxCleanupSelectDupBtn);
                if (vis(tboxCleanupSelectOldBtn)) ltr.Add(tboxCleanupSelectOldBtn);
                if (vis(tboxCleanupSelectDamagedBtn)) ltr.Add(tboxCleanupSelectDamagedBtn);
                if (vis(tboxCleanupClearBtn)) ltr.Add(tboxCleanupClearBtn);
                if (vis(tboxCleanupAddExcludeBtn)) ltr.Add(tboxCleanupAddExcludeBtn);
                if (vis(tboxCleanupRemoveExcludeBtn)) ltr.Add(tboxCleanupRemoveExcludeBtn);
                if (vis(tboxLoadBtn)) ltr.Add(tboxLoadBtn);
                if (vis(tboxUnloadBtn)) ltr.Add(tboxUnloadBtn);
                if (vis(tboxLoadDepsBtn)) ltr.Add(tboxLoadDepsBtn);
                if (vis(tboxCacheTexturesBtn)) ltr.Add(tboxCacheTexturesBtn);
                if (vis(tboxJsonParserBenchBtn)) ltr.Add(tboxJsonParserBenchBtn);
                if (vis(tboxOpenHubBtn)) ltr.Add(tboxOpenHubBtn);
                if (vis(tboxCopyPkgNamesBtn)) ltr.Add(tboxCopyPkgNamesBtn);
                if (vis(tboxSceneImportBtn)) ltr.Add(tboxSceneImportBtn);
                if (vis(tboxSelectAllBtn)) ltr.Add(tboxSelectAllBtn);
                if (vis(tboxClearSelectionBtn)) ltr.Add(tboxClearSelectionBtn);
            }

            var rtl = new List<GameObject>(ltr.Count);
            for (int i = ltr.Count - 1; i >= 0; i--)
                rtl.Add(ltr[i]);

            bool FitsOneRowMin()
            {
                // Prefer wrapping when preferred widths don't fit; avoids clipping/missing buttons in 1-row mode.
                return RowPrefSum(rtl) <= avail + 1f;
            }

            List<GameObject> row0rtl = new List<GameObject>();
            List<GameObject> row1rtl = new List<GameObject>();

            if (FitsOneRowMin())
            {
                tboxButtonLayoutRows = 1;
                row0rtl.AddRange(rtl);
            }
            else
            {
                if (IsSettingsPanelOpen())
                {
                    // Settings mode must stay 1 row (CANCEL/SAVE only).
                    tboxButtonLayoutRows = 1;
                    row0rtl.AddRange(rtl);
                    row1rtl.Clear();
                }
                else
                {
                float used = 0f;
                for (int i = 0; i < rtl.Count; i++)
                {
                    GameObject go = rtl[i];
                    if (!TryGetWidths(go, out _, out float pw)) continue;
                    float need = pw + (row0rtl.Count > 0 ? gap : 0f);
                    if (used + need <= avail + 1f)
                    {
                        row0rtl.Add(go);
                        used += need;
                    }
                    else
                    {
                        for (int j = i; j < rtl.Count; j++)
                            row1rtl.Add(rtl[j]);
                        break;
                    }
                }
                if (row0rtl.Count == 0 && rtl.Count > 0)
                {
                    row0rtl.Add(rtl[0]);
                    row1rtl.Clear();
                    for (int j = 1; j < rtl.Count; j++)
                        row1rtl.Add(rtl[j]);
                }
                tboxButtonLayoutRows = row1rtl.Count > 0 ? 2 : 1;
                }
            }

            TboxDetachAllActionButtonsForLayout();
            if (tboxButtonLayoutRows == 1)
            {
                TboxPopulateRowLtr(tboxBtnRow0HLG, ltr);
                tboxBtnRow1GO.SetActive(false);
            }
            else
            {
                tboxBtnRow1GO.SetActive(true);
                TboxPopulateRowFromRtlPack(tboxBtnRow0HLG, row0rtl);
                TboxPopulateRowFromRtlPack(tboxBtnRow1HLG, row1rtl);
            }

            float band = tboxInfoRowHeight * tboxButtonLayoutRows + (tboxButtonLayoutRows > 1 ? tboxBtnRowGap : 0f);
            // Add filter row height when active
            if (tboxFilterModeRowGO != null && tboxFilterModeRowGO.activeSelf)
                band += tboxInfoRowHeight + tboxBtnRowGap;
            if (tboxButtonsLayerRT != null)
                tboxButtonsLayerRT.sizeDelta = new Vector2(tboxButtonsLayerRT.sizeDelta.x, band);

            LayoutRebuilder.MarkLayoutForRebuild(tboxButtonsFlexRootRT);
            tboxLastFlexAvailW = tboxButtonsFlexRootRT.rect.width;
        }

        // Expand/collapse state
        private bool tboxIsHovered = false;
        private bool tboxPinned = false;
        private float tboxExpandT = 0f;        // 0 = collapsed, 1 = expanded

        private RectTransform tboxRT;
        private CanvasGroup tboxLabelCG;        // fades OUT when expanding
        private CanvasGroup tboxButtonsCG;      // fades IN when expanding
        private GameObject tboxPinBtn;
        private Text tboxPinBtnText;

        // Row height: matches the collapsed bar height set by the layout system.
        // Updated by layout code (UI.Layout.cs) and innerPaneScaleActions.
        private float tboxInfoRowHeight = 60f;   // single row height (= collapsed bar height)
        private float tboxTopOffsetBase = 120f;   // bar's top offset (offsetMax.y) when fully collapsed

        private RectTransform tboxLabelLayerRT;   // reference for scale updates
        private RectTransform tboxButtonsLayerRT; // reference for scale updates

        // ─────────────────────────────────────────────────────────────────────────

        private void EnsureTboxUI()
        {
            if (tbox != null) return;
            // Reuse the unified info bar (hoverPath container) as the tbox
            if (hoverPathRT == null) return;

            tbox = hoverPathRT.gameObject;
            tboxRT = hoverPathRT;
            tbox.name = "InfoBar";

            // Background already set to opaque grey in UI.cs; ensure raycastTarget on
            var img = tbox.GetComponent<Image>();
            if (img != null) { img.color = new Color(0.15f, 0.15f, 0.15f, 1f); img.raycastTarget = true; }

            var hoverDel = tbox.AddComponent<UIHoverDelegate>();
            hoverDel.OnHoverChange = h => tboxIsHovered = h;

            // ── "X Selected" + hover hint, one row (collapsed view) ─────────────
            var labelGO = new GameObject("TboxLabelLayer");
            labelGO.transform.SetParent(tbox.transform, false);
            tboxLabelCG = labelGO.AddComponent<CanvasGroup>();

            // Label layer occupies the BOTTOM row (always visible), leaving 48 px on right for pin
            var labelLayerRT = labelGO.GetComponent<RectTransform>();
            if (labelLayerRT == null) labelLayerRT = labelGO.AddComponent<RectTransform>();
            labelLayerRT.anchorMin = new Vector2(0f, 0f);
            labelLayerRT.anchorMax = new Vector2(1f, 0f);
            labelLayerRT.pivot = new Vector2(0.5f, 0f);
            labelLayerRT.anchoredPosition = Vector2.zero;
            labelLayerRT.sizeDelta = new Vector2(-48f, tboxInfoRowHeight);
            tboxLabelLayerRT = labelLayerRT;

            var rowGO = new GameObject("TboxLabelRow");
            rowGO.transform.SetParent(labelGO.transform, false);
            var rowRT = rowGO.AddComponent<RectTransform>();
            rowRT.anchorMin = Vector2.zero;
            rowRT.anchorMax = Vector2.one;
            rowRT.offsetMin = Vector2.zero;
            rowRT.offsetMax = Vector2.zero;

            var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowHLG.childAlignment = TextAnchor.MiddleCenter;
            rowHLG.spacing = 12f;
            rowHLG.childForceExpandWidth = false;
            rowHLG.childForceExpandHeight = true;
            rowHLG.childControlWidth = true;
            rowHLG.childControlHeight = true;
            rowHLG.padding = new RectOffset(8, 8, 0, 0);

            const int tboxCollapsedFont = 18;

            var labelTextGO = new GameObject("Text");
            labelTextGO.transform.SetParent(rowGO.transform, false);
            tboxLabel = labelTextGO.AddComponent<Text>();
            tboxLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxLabel.fontSize = tboxCollapsedFont;
            tboxLabel.fontStyle = FontStyle.Bold;
            tboxLabel.color = Color.white;
            tboxLabel.alignment = TextAnchor.MiddleCenter;
            tboxLabel.raycastTarget = false;
            var labelShadow = labelTextGO.AddComponent<Shadow>();
            labelShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            labelShadow.effectDistance = new Vector2(1f, -1f);
            var labelCSF = labelTextGO.AddComponent<ContentSizeFitter>();
            labelCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            labelCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var hintTextGO = new GameObject("HoverHint");
            hintTextGO.transform.SetParent(rowGO.transform, false);
            tboxHintLabel = hintTextGO.AddComponent<Text>();
            tboxHintLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxHintLabel.fontSize = tboxCollapsedFont;
            tboxHintLabel.fontStyle = FontStyle.Normal;
            tboxHintLabel.color = new Color(0.50f, 0.50f, 0.50f, 1f);
            tboxHintLabel.alignment = TextAnchor.MiddleCenter;
            tboxHintLabel.raycastTarget = false;
            tboxHintLabel.text = VPBTranslation.T("gallery.tbox.hover_expand", "Hover to expand");
            hintTextGO.SetActive(false);
            var hintShadow = hintTextGO.AddComponent<Shadow>();
            hintShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
            hintShadow.effectDistance = new Vector2(1f, -1f);
            var hintCSF = hintTextGO.AddComponent<ContentSizeFitter>();
            hintCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            hintCSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ── Buttons panel (expanded view) ─────────────────────────────────────
            var bpGO = new GameObject("TboxButtonsLayer");
            bpGO.transform.SetParent(tbox.transform, false);
            tboxButtonsCG = bpGO.AddComponent<CanvasGroup>();
            tboxButtonsCG.alpha = 0f;
            tboxButtonsCG.blocksRaycasts = false;
            tboxButtonsCG.interactable = false;

            // Buttons layer sits in the TOP row — directly above the label row.
            // Revealed by RectMask2D as the bar grows upward.
            var bpRT = bpGO.GetComponent<RectTransform>();
            if (bpRT == null) bpRT = bpGO.AddComponent<RectTransform>();
            bpRT.anchorMin = new Vector2(0f, 0f);
            bpRT.anchorMax = new Vector2(1f, 0f);
            bpRT.pivot = new Vector2(0.5f, 0f);
            bpRT.anchoredPosition = new Vector2(0f, tboxInfoRowHeight); // sits one row above bottom
            bpRT.sizeDelta = new Vector2(-48f, tboxInfoRowHeight);
            tboxButtonsLayerRT = bpRT;

            // Flex root + two HLG rows (second row toggled when wrapping)
            var flexGO = new GameObject("TboxButtonsFlexRoot");
            flexGO.transform.SetParent(bpGO.transform, false);
            tboxButtonsFlexRoot = flexGO;
            tboxButtonsFlexRootRT = flexGO.AddComponent<RectTransform>();
            tboxButtonsFlexRootRT.anchorMin = Vector2.zero;
            tboxButtonsFlexRootRT.anchorMax = Vector2.one;
            tboxButtonsFlexRootRT.offsetMin = new Vector2(8f, 0f);
            tboxButtonsFlexRootRT.offsetMax = new Vector2(-12f, 0f);
            tboxButtonsFlexRootRT.pivot = new Vector2(0.5f, 0f);
            var flexVlg = flexGO.AddComponent<VerticalLayoutGroup>();
            flexVlg.spacing = tboxBtnRowGap;
            flexVlg.padding = new RectOffset(0, 0, 0, 0);
            flexVlg.childAlignment = TextAnchor.UpperRight;
            flexVlg.childControlWidth = true;
            flexVlg.childForceExpandWidth = true;
            flexVlg.childControlHeight = true;
            flexVlg.childForceExpandHeight = false;

            // Hold buttons between relayout passes (sibling of flex root — not in the VLG).
            var stashGO = new GameObject("TboxBtnStash");
            stashGO.transform.SetParent(bpGO.transform, false);
            var stashRT = stashGO.AddComponent<RectTransform>();
            stashRT.anchorMin = Vector2.zero;
            stashRT.anchorMax = Vector2.zero;
            stashRT.pivot = Vector2.zero;
            stashRT.anchoredPosition = Vector2.zero;
            stashRT.sizeDelta = Vector2.zero;
            tboxButtonStash = stashGO;

            float innerRowH = Mathf.Max(34f, tboxInfoRowHeight - 8f);

            tboxBtnRow0GO = new GameObject("TboxBtnRow0");
            tboxBtnRow0GO.transform.SetParent(flexGO.transform, false);
            tboxBtnRow0RT = tboxBtnRow0GO.AddComponent<RectTransform>();
            tboxBtnRow0RT.anchorMin = Vector2.zero;
            tboxBtnRow0RT.anchorMax = Vector2.one;
            tboxBtnRow0RT.sizeDelta = Vector2.zero;
            tboxBtnRow0LE = tboxBtnRow0GO.AddComponent<LayoutElement>();
            tboxBtnRow0LE.minHeight = innerRowH;
            tboxBtnRow0LE.preferredHeight = innerRowH;
            tboxBtnRow0LE.flexibleWidth = 1f;
            tboxBtnRow0HLG = tboxBtnRow0GO.AddComponent<HorizontalLayoutGroup>();
            tboxBtnRow0HLG.spacing = 10f;
            tboxBtnRow0HLG.padding = new RectOffset(0, 0, 0, 0);
            tboxBtnRow0HLG.childAlignment = TextAnchor.MiddleRight;
            tboxBtnRow0HLG.childControlWidth = true;
            tboxBtnRow0HLG.childForceExpandWidth = false;
            tboxBtnRow0HLG.childControlHeight = true;
            tboxBtnRow0HLG.childForceExpandHeight = true;

            tboxBtnRow1GO = new GameObject("TboxBtnRow1");
            tboxBtnRow1GO.transform.SetParent(flexGO.transform, false);
            tboxBtnRow1RT = tboxBtnRow1GO.AddComponent<RectTransform>();
            tboxBtnRow1RT.anchorMin = Vector2.zero;
            tboxBtnRow1RT.anchorMax = Vector2.one;
            tboxBtnRow1RT.sizeDelta = Vector2.zero;
            tboxBtnRow1LE = tboxBtnRow1GO.AddComponent<LayoutElement>();
            tboxBtnRow1LE.minHeight = innerRowH;
            tboxBtnRow1LE.preferredHeight = innerRowH;
            tboxBtnRow1LE.flexibleWidth = 1f;
            tboxBtnRow1HLG = tboxBtnRow1GO.AddComponent<HorizontalLayoutGroup>();
            tboxBtnRow1HLG.spacing = 10f;
            tboxBtnRow1HLG.padding = new RectOffset(0, 0, 0, 0);
            tboxBtnRow1HLG.childAlignment = TextAnchor.MiddleRight;
            tboxBtnRow1HLG.childControlWidth = true;
            tboxBtnRow1HLG.childForceExpandWidth = false;
            tboxBtnRow1HLG.childControlHeight = true;
            tboxBtnRow1HLG.childForceExpandHeight = true;
            tboxBtnRow1GO.SetActive(false);

            // ── Dependency Filter Mode Row ─────────────────────────────────────
            tboxFilterModeRowGO = new GameObject("TboxFilterModeRow");
            tboxFilterModeRowGO.transform.SetParent(flexGO.transform, false);
            tboxFilterModeRowGO.transform.SetAsFirstSibling();
            tboxFilterModeRowRT = tboxFilterModeRowGO.AddComponent<RectTransform>();
            tboxFilterModeRowRT.anchorMin = Vector2.zero;
            tboxFilterModeRowRT.anchorMax = Vector2.one;
            tboxFilterModeRowRT.sizeDelta = Vector2.zero;
            tboxFilterModeRowLE = tboxFilterModeRowGO.AddComponent<LayoutElement>();
            tboxFilterModeRowLE.minHeight = innerRowH;
            tboxFilterModeRowLE.preferredHeight = innerRowH;
            tboxFilterModeRowLE.flexibleWidth = 1f;
            tboxFilterModeRowHLG = tboxFilterModeRowGO.AddComponent<HorizontalLayoutGroup>();
            tboxFilterModeRowHLG.spacing = 12f;
            tboxFilterModeRowHLG.padding = new RectOffset(8, 8, 0, 0);
            tboxFilterModeRowHLG.childAlignment = TextAnchor.MiddleCenter;
            tboxFilterModeRowHLG.childControlWidth = false;
            tboxFilterModeRowHLG.childForceExpandWidth = false;
            tboxFilterModeRowHLG.childControlHeight = true;
            tboxFilterModeRowHLG.childForceExpandHeight = true;
            tboxFilterModeRowGO.SetActive(false);

            // Filter Mode Label
            {
                var filterLabelGO = new GameObject("FilterModeLabel");
                filterLabelGO.transform.SetParent(tboxFilterModeRowGO.transform, false);
                tboxFilterModeText = filterLabelGO.AddComponent<Text>();
                tboxFilterModeText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                tboxFilterModeText.fontSize = 20;
                tboxFilterModeText.fontStyle = FontStyle.Bold;
                tboxFilterModeText.color = new Color(1f, 0.85f, 0f, 1f);
                tboxFilterModeText.alignment = TextAnchor.MiddleCenter;
                tboxFilterModeText.horizontalOverflow = HorizontalWrapMode.Overflow;
                tboxFilterModeText.verticalOverflow = VerticalWrapMode.Truncate;
                tboxFilterModeText.raycastTarget = false;
                var labelRT = filterLabelGO.GetComponent<RectTransform>();
                labelRT.sizeDelta = new Vector2(200, innerRowH);
            }

            // Back Button
            tboxFilterBackBtn = UI.CreateUIButton(tboxFilterModeRowGO, 80, 40, VPBTranslation.T("gallery.tbox.filter_back", "Back"), 16, 0, 0, AnchorPresets.stretchAll, NavigateBack);
            tboxFilterBackBtn.name = "TboxFilterBackBtn";
            tboxFilterBackBtn.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.6f, 0.9f);
            { var s = UI.LoadIconSprite("vpb_icons/arrow_left.png", Color.white); if (s != null) UI.AddIconToButton(tboxFilterBackBtn, s, padding: 6f); }
            AddTooltip(tboxFilterBackBtn, "gallery.tooltip.filter_back", VPBTranslation.T("gallery.tooltip.filter_back", "Back"));

            // Clear Filter Button
            tboxFilterClearBtn = UI.CreateUIButton(tboxFilterModeRowGO, 80, 40, VPBTranslation.T("gallery.tbox.filter_clear", "Clear"), 16, 0, 0, AnchorPresets.stretchAll, ClearPackageFilter);
            tboxFilterClearBtn.name = "TboxFilterClearBtn";
            tboxFilterClearBtn.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f, 0.9f);
            { var s = UI.LoadIconSprite("vpb_icons/filter_off.png", Color.white); if (s != null) UI.AddIconToButton(tboxFilterClearBtn, s, padding: 6f); }
            AddTooltip(tboxFilterClearBtn, "gallery.tooltip.filter_clear", VPBTranslation.T("gallery.tooltip.filter_clear", "Clear Filter"));

            const int tboxActionBtnFont = 16;

            // Placeholders — layout is resolved in RefreshTboxFlexButtonLayout (stretch + LayoutElement).

            tboxSceneImportBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.scene_import", "Import"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxSceneImportSelectedPackage
            );
            tboxSceneImportBtn.name = "Tbox_SceneImport";
            TboxConfigureActionButtonFlex(tboxSceneImportBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxSceneImportBtn, "gallery.tooltip.scene_import", "Import presets from a scene");

            tboxCopyPkgNamesBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.copy_names", "Copy Names"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                CopySelectedPackageNamesToClipboard
            );
            tboxCopyPkgNamesBtn.name = "Tbox_CopyPackageNames";
            TboxConfigureActionButtonFlex(tboxCopyPkgNamesBtn, innerRowH, innerRowH, innerRowH); // square icon button
            WireCopyNamesTooltip(tboxCopyPkgNamesBtn);

            try
            {
                tboxClipboardListSprite  = UI.LoadIconSprite("vpb_icons/clipboard_list.png",  Color.white);
                tboxClipboardCheckSprite = UI.LoadIconSprite("vpb_icons/clipboard_check.png", new Color(1f, 1f, 1f, 1f));
                if (tboxClipboardListSprite != null)
                {
                    UI.AddIconToButton(tboxCopyPkgNamesBtn, tboxClipboardListSprite, padding: 6f);
                    tboxCopyNamesIconImage = tboxCopyPkgNamesBtn.transform.Find("Icon")?.GetComponent<Image>();
                }
            }
            catch { }

            // Settings mode: replace normal toolbox actions with Save/Cancel row.
            tboxSettingsCancelBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                () => ExitInternalSettingsMode(false)
            );
            tboxSettingsCancelBtn.name = "Tbox_SettingsCancel";
            TboxConfigureActionButtonFlex(tboxSettingsCancelBtn, 96f, 120f, innerRowH);
            tboxSettingsCancelBtn.GetComponent<Image>().color = new Color(0.55f, 0.18f, 0.18f, 0.95f);
            AddTooltipPlain(tboxSettingsCancelBtn, VPBTranslation.T("settings.tbox.cancel.tip", "Discard changes and exit Settings"));
            try
            {
                var closeSpr = UI.LoadIconSprite("vpb_icons/close.png", Color.white);
                if (closeSpr != null) UI.AddIconToButton(tboxSettingsCancelBtn, closeSpr, padding: 6f);
            }
            catch { }
            tboxSettingsCancelBtn.SetActive(false);

            tboxSettingsSaveBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                () => ExitInternalSettingsMode(true)
            );
            tboxSettingsSaveBtn.name = "Tbox_SettingsSave";
            TboxConfigureActionButtonFlex(tboxSettingsSaveBtn, 88f, 112f, innerRowH);
            tboxSettingsSaveBtn.GetComponent<Image>().color = new Color(0.18f, 0.55f, 0.22f, 0.95f);
            AddTooltipPlain(tboxSettingsSaveBtn, VPBTranslation.T("settings.tbox.save.tip", "Save changes and exit Settings"));
            try
            {
                var saveSpr = gallerySaveSprite ?? UI.LoadIconSprite("vpb_icons/gallery_save.png", Color.white);
                if (saveSpr != null) UI.AddIconToButton(tboxSettingsSaveBtn, saveSpr, padding: 6f);
            }
            catch { }
            tboxSettingsSaveBtn.SetActive(false);

            tboxCacheTexturesBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxCacheTexturesSelected
            );
            tboxCacheTexturesBtn.name = "Tbox_CacheTextures";
            TboxConfigureActionButtonFlex(tboxCacheTexturesBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxCacheTexturesBtn, "gallery.tooltip.tbox_cache_textures", "Build VPB texture cache for selected .var packages (includes dependency packages). Hold Ctrl to rewrite existing zstd cache files. Hold Ctrl+Shift to purge the cache for selected items.");
            try
            {
                var cacheTextureIcon = UI.LoadIconSprite("vpb_icons/cache_texture.png", Color.white);
                if (cacheTextureIcon != null) UI.AddIconToButton(tboxCacheTexturesBtn, cacheTextureIcon, padding: 6f);
                else
                {
                    Text t = tboxCacheTexturesBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.cache_textures", "Cache Textures");
                }
            }
            catch { }

            // JSON meta.json bench (SimpleJSON vs BMH): handler in GalleryPanel.JsonBench.cs
            tboxJsonParserBenchBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxBenchmarkJsonParsersSelected
            );
            tboxJsonParserBenchBtn.name = "Tbox_JsonParserBench";
            TboxConfigureActionButtonFlex(tboxJsonParserBenchBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(
                tboxJsonParserBenchBtn,
                "gallery.tooltip.tbox_json_parser_bench",
                "Developer Mode: benchmark library meta.json with SimpleJSON vs Boyer-Moore-Horspool"
            );
            try
            {
                var parserIcon = UI.LoadIconSprite("vpb_icons/fps.png", Color.white);
                if (parserIcon != null) UI.AddIconToButton(tboxJsonParserBenchBtn, parserIcon, padding: 6f);
                else
                {
                    Text t = tboxJsonParserBenchBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = "JSON";
                }
            }
            catch { }

            try
            {
                if (tboxJsonParserBenchBtn != null)
                    tboxJsonParserBenchBtn.SetActive(VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode && !cleanupModeActive);
            }
            catch { }

            tboxOpenHubBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxOpenSelectedItemOnHub
            );
            tboxOpenHubBtn.name = "Tbox_OpenOnHub";
            TboxConfigureActionButtonFlex(tboxOpenHubBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxOpenHubBtn, "gallery.tooltip.tbox_open_hub", "Open this item in Hub");
            try
            {
                var hubIcon = UI.LoadIconSprite("vpb_icons/hub.png", Color.white);
                if (hubIcon != null) UI.AddIconToButton(tboxOpenHubBtn, hubIcon, padding: 6f);
                else
                {
                    Text t = tboxOpenHubBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.open_hub", "Hub");
                }
            }
            catch { }
            tboxOpenHubBtn.SetActive(false);

            tboxLoadDepsBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxLoadDepsSelectedPackages
            );
            tboxLoadDepsBtn.name = "Tbox_LoadDeps";
            TboxConfigureActionButtonFlex(tboxLoadDepsBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxLoadDepsBtn, "gallery.tooltip.tbox_load_deps", "Copy selected packages and their dependencies from AllPackages to AddonPackages (respects Settings → load deps with package)");
            try
            {
                var loadDepsIcon = UI.LoadIconSprite("vpb_icons/load_deps.png", Color.white);
                if (loadDepsIcon != null) UI.AddIconToButton(tboxLoadDepsBtn, loadDepsIcon, padding: 6f);
                else
                {
                    Text t = tboxLoadDepsBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.load_deps", "Load Deps");
                }
            }
            catch { }

            tboxUnloadBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxUnloadSelectedPackages
            );
            tboxUnloadBtn.name = "Tbox_Unload";
            TboxConfigureActionButtonFlex(tboxUnloadBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxUnloadBtn, "gallery.tooltip.tbox_unload", "Move selected installed .var files from AddonPackages back to AllPackages");
            try
            {
                var unloadIcon = UI.LoadIconSprite("vpb_icons/unload.png", Color.white);
                if (unloadIcon != null) UI.AddIconToButton(tboxUnloadBtn, unloadIcon, padding: 6f);
                else
                {
                    Text t = tboxUnloadBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.unload", "Unload");
                }
            }
            catch { }

            tboxLoadBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxLoadSelectedPackages
            );
            tboxLoadBtn.name = "Tbox_Load";
            TboxConfigureActionButtonFlex(tboxLoadBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxLoadBtn, "gallery.tooltip.tbox_load", "Copy selected .var from AllPackages to AddonPackages (this package only, no dependencies)");
            try
            {
                var loadIcon = UI.LoadIconSprite("vpb_icons/load.png", Color.white);
                if (loadIcon != null) UI.AddIconToButton(tboxLoadBtn, loadIcon, padding: 6f);
                else
                {
                    Text t = tboxLoadBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.load", "Load");
                }
            }
            catch { }

            tboxDeleteBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxDeleteSelectedPackages
            );
            tboxDeleteBtn.name = "Tbox_Delete";
            TboxConfigureActionButtonFlex(tboxDeleteBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxDeleteBtn, "gallery.tooltip.tbox_delete", "Move selected packages to DeletedPackages; local Saves/scene JSON (+ preview) to DeletedScenes");
            try
            {
                var delIcon = UI.LoadIconSprite("vpb_icons/delete.png", Color.white);
                if (delIcon != null)
                    UI.AddIconToButton(tboxDeleteBtn, delIcon, padding: 6f, backdropOverride: new Color(0.35f, 0.15f, 0.15f, 1f));
                else
                {
                    // Fallback: keep text label if icon missing
                    Text t = tboxDeleteBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.delete", "Delete");
                }
            }
            catch { }

            tboxRemoveHistoryBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxRemoveSelectedFromHistory
            );
            tboxRemoveHistoryBtn.name = "Tbox_RemoveHistory";
            TboxConfigureActionButtonFlex(tboxRemoveHistoryBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxRemoveHistoryBtn, "gallery.tooltip.tbox_remove_history", "Remove selected entries from History (does not delete packages or files)");
            try
            {
                var rhIcon = UI.LoadIconSprite("vpb_icons/list_remove.png", new Color(0.92f, 0.82f, 0.55f, 1f));
                if (rhIcon != null)
                    UI.AddIconToButton(tboxRemoveHistoryBtn, rhIcon, padding: 6f);
                else
                {
                    Text t = tboxRemoveHistoryBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.remove_history", "Rm Hist");
                }
            }
            catch { }
            if (tboxRemoveHistoryBtn != null) tboxRemoveHistoryBtn.SetActive(false);

            tboxSelectAllBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                SelectAll
            );
            tboxSelectAllBtn.name = "Tbox_SelectAll";
            TboxConfigureActionButtonFlex(tboxSelectAllBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxSelectAllBtn, "gallery.tooltip.select_all", "Select All");
            try
            {
                var selectAllIcon = UI.LoadIconSprite("vpb_icons/select_all.png", new Color(0.78f, 0.78f, 0.78f, 1f));
                if (selectAllIcon != null) UI.AddIconToButton(tboxSelectAllBtn, selectAllIcon, padding: 6f);
                else
                {
                    Text t = tboxSelectAllBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.select_all", "Select All");
                }
            }
            catch { }

            tboxClearSelectionBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                ClearSelection
            );
            tboxClearSelectionBtn.name = "Tbox_ClearSelection";
            TboxConfigureActionButtonFlex(tboxClearSelectionBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxClearSelectionBtn, "gallery.tooltip.clear_selection", "Clear Selection");
            try
            {
                var clearSelectionIcon = UI.LoadIconSprite("vpb_icons/clear_selection.png", new Color(0.78f, 0.78f, 0.78f, 1f));
                if (clearSelectionIcon != null) UI.AddIconToButton(tboxClearSelectionBtn, clearSelectionIcon, padding: 6f);
                else
                {
                    Text t = tboxClearSelectionBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.clear_selection", "Clear");
                }
            }
            catch { }

            tboxCleanupBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxOpenCleanupView
            );
            tboxCleanupBtn.name = "Tbox_Cleanup";
            TboxConfigureActionButtonFlex(tboxCleanupBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxCleanupBtn, "gallery.tooltip.tbox_cleanup", "Scan globally for duplicate, old, and damaged packages/local files.");
            try
            {
                var cleanupIcon = UI.LoadIconSprite("vpb_icons/cleanup.png", Color.white);
                if (cleanupIcon != null) UI.AddIconToButton(tboxCleanupBtn, cleanupIcon, padding: 6f);
                else
                {
                    Text t = tboxCleanupBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.cleanup", "Cleanup");
                }
            }
            catch { }

            tboxCleanupApplyBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_apply", "Apply"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxApplyCleanupSelected
            );
            tboxCleanupApplyBtn.name = "Tbox_CleanupApply";
            TboxConfigureActionButtonFlex(tboxCleanupApplyBtn, 72f, 84f, innerRowH);
            AddTooltip(tboxCleanupApplyBtn, "gallery.tooltip.tbox_cleanup_apply", "Move selected cleanup candidates to DeletedPackages/DeletedScenes by type.");

            tboxCleanupFilterAllBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_filter_all", "All"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxToggleCleanupFilterAll
            );
            tboxCleanupFilterAllBtn.name = "Tbox_CleanupFilterAll";
            TboxConfigureActionButtonFlex(tboxCleanupFilterAllBtn, 56f, 68f, innerRowH);

            tboxCleanupFilterDupBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_filter_dup", "Dup"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxToggleCleanupFilterDuplicate
            );
            tboxCleanupFilterDupBtn.name = "Tbox_CleanupFilterDup";
            TboxConfigureActionButtonFlex(tboxCleanupFilterDupBtn, 56f, 68f, innerRowH);

            tboxCleanupFilterOldBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_filter_old", "Old"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxToggleCleanupFilterOld
            );
            tboxCleanupFilterOldBtn.name = "Tbox_CleanupFilterOld";
            TboxConfigureActionButtonFlex(tboxCleanupFilterOldBtn, 56f, 68f, innerRowH);

            tboxCleanupFilterDamagedBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_filter_damaged", "Damaged"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxToggleCleanupFilterDamaged
            );
            tboxCleanupFilterDamagedBtn.name = "Tbox_CleanupFilterDamaged";
            TboxConfigureActionButtonFlex(tboxCleanupFilterDamagedBtn, 78f, 90f, innerRowH);

            tboxCleanupSelectVisibleBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_select_visible", "SelVisible"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxSelectAllVisibleCleanup
            );
            tboxCleanupSelectVisibleBtn.name = "Tbox_CleanupSelectVisible";
            TboxConfigureActionButtonFlex(tboxCleanupSelectVisibleBtn, 88f, 108f, innerRowH);

            tboxCleanupSelectDupBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_select_dup", "SelDup"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                () => TboxSelectCleanupByType(CleanupCandidateType.Duplicate)
            );
            tboxCleanupSelectDupBtn.name = "Tbox_CleanupSelectDup";
            TboxConfigureActionButtonFlex(tboxCleanupSelectDupBtn, 72f, 86f, innerRowH);

            tboxCleanupSelectOldBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_select_old", "SelOld"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                () => TboxSelectCleanupByType(CleanupCandidateType.OldVersion)
            );
            tboxCleanupSelectOldBtn.name = "Tbox_CleanupSelectOld";
            TboxConfigureActionButtonFlex(tboxCleanupSelectOldBtn, 72f, 86f, innerRowH);

            tboxCleanupSelectDamagedBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_select_damaged", "SelDamaged"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                () => TboxSelectCleanupByType(CleanupCandidateType.Damaged)
            );
            tboxCleanupSelectDamagedBtn.name = "Tbox_CleanupSelectDamaged";
            TboxConfigureActionButtonFlex(tboxCleanupSelectDamagedBtn, 88f, 112f, innerRowH);

            tboxCleanupClearBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_clear", "ClearSel"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxClearCleanupSelection
            );
            tboxCleanupClearBtn.name = "Tbox_CleanupClear";
            TboxConfigureActionButtonFlex(tboxCleanupClearBtn, 72f, 90f, innerRowH);

            tboxCleanupAddExcludeBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_add_exclude", "Add Exclude"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxCleanupAddSelectedToExclude
            );
            tboxCleanupAddExcludeBtn.name = "Tbox_CleanupAddExclude";
            TboxConfigureActionButtonFlex(tboxCleanupAddExcludeBtn, 92f, 118f, innerRowH);
            AddTooltip(tboxCleanupAddExcludeBtn, "gallery.tooltip.tbox_cleanup_add_exclude", "Add selected cleanup packages to the cleanup exclude list.");
            try
            {
                var addExcludeIcon = UI.LoadIconSprite("vpb_icons/list_add.png", Color.white);
                if (addExcludeIcon != null) UI.AddIconToButton(tboxCleanupAddExcludeBtn, addExcludeIcon, padding: 6f);
            }
            catch { }

            tboxCleanupRemoveExcludeBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cleanup_remove_exclude", "Remove Exclude"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxCleanupRemoveSelectedFromExclude
            );
            tboxCleanupRemoveExcludeBtn.name = "Tbox_CleanupRemoveExclude";
            TboxConfigureActionButtonFlex(tboxCleanupRemoveExcludeBtn, 112f, 138f, innerRowH);
            AddTooltip(tboxCleanupRemoveExcludeBtn, "gallery.tooltip.tbox_cleanup_remove_exclude", "Remove selected cleanup packages from the cleanup exclude list.");
            try
            {
                var removeExcludeIcon = UI.LoadIconSprite("vpb_icons/list_remove.png", Color.white);
                if (removeExcludeIcon != null) UI.AddIconToButton(tboxCleanupRemoveExcludeBtn, removeExcludeIcon, padding: 6f);
            }
            catch { }
            tboxCleanupApplyBtn.SetActive(false);
            tboxCleanupFilterAllBtn.SetActive(false);
            tboxCleanupFilterDupBtn.SetActive(false);
            tboxCleanupFilterOldBtn.SetActive(false);
            tboxCleanupFilterDamagedBtn.SetActive(false);
            tboxCleanupSelectVisibleBtn.SetActive(false);
            tboxCleanupSelectDupBtn.SetActive(false);
            tboxCleanupSelectOldBtn.SetActive(false);
            tboxCleanupSelectDamagedBtn.SetActive(false);
            tboxCleanupClearBtn.SetActive(false);
            tboxCleanupAddExcludeBtn.SetActive(false);
            tboxCleanupRemoveExcludeBtn.SetActive(false);

            tboxAutoInstallBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxAutoInstallSelectedPackages
            );
            tboxAutoInstallBtn.name = "Tbox_AutoInstall";
            TboxConfigureActionButtonFlex(tboxAutoInstallBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxAutoInstallBtn, "gallery.tooltip.tbox_autoinstall", "Flag selected packages for auto-install and auto-load. When scan whitelist is enabled, this also adds a persistent per-package startup-scan whitelist override. Packages in AllPackages are copied to AddonPackages on the next VaM start (not immediately).");
            try
            {
                var autoLoadIcon = UI.LoadIconSprite("vpb_icons/auto.png", Color.white);
                if (autoLoadIcon != null) UI.AddIconToButton(tboxAutoInstallBtn, autoLoadIcon, padding: 6f);
                else
                {
                    Text t = tboxAutoInstallBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.autoinstall", "Autoinstall");
                }
            }
            catch { }

            tboxHideBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxHideSelectedPackages
            );
            tboxHideBtn.name = "Tbox_Hide";
            TboxConfigureActionButtonFlex(tboxHideBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxHideBtn, "gallery.tooltip.tbox_hide", "Hide selected packages in VaM file lists (AddonPackagesFilePrefs … .hide)");
            try
            {
                // Hide = show_hidden ON
                var hideIcon = UI.LoadIconSprite("vpb_icons/show_hidden.png", Color.white);
                if (hideIcon != null)
                    UI.AddIconToButton(tboxHideBtn, hideIcon, padding: 6f);
                else
                {
                    Text t = tboxHideBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.hide", "Hide");
                }
            }
            catch { }

            tboxUnhideBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxUnhideSelectedPackages
            );
            tboxUnhideBtn.name = "Tbox_Unhide";
            TboxConfigureActionButtonFlex(tboxUnhideBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxUnhideBtn, "gallery.tooltip.tbox_unhide", "Remove .hide markers for selected packages");
            try
            {
                // Unhide = show_hidden OFF
                var unhideIcon = UI.LoadIconSprite("vpb_icons/show_hidden_off.png", Color.white);
                if (unhideIcon != null)
                    UI.AddIconToButton(tboxUnhideBtn, unhideIcon, padding: 6f);
                else
                {
                    Text t = tboxUnhideBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.unhide", "Unhide");
                }
            }
            catch { }

            tboxScanWhitelistTemporaryBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxScanWhitelistTemporaryForSelection
            );
            tboxScanWhitelistTemporaryBtn.name = "Tbox_ScanWlTemporary";
            TboxConfigureActionButtonFlex(tboxScanWhitelistTemporaryBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxScanWhitelistTemporaryBtn, "gallery.tooltip.tbox_scan_wl_temporary",
                "Temporarily allow selected packages in VaM startup scan whitelist for this session only. This does not save to scan_whitelist.json and resets on VaM restart.");
            try
            {
                var temporaryIcon = UI.LoadIconSprite("vpb_icons/temporary.png", Color.white);
                if (temporaryIcon != null)
                    UI.AddIconToButton(tboxScanWhitelistTemporaryBtn, temporaryIcon, padding: 6f);
                else
                {
                    Text t = tboxScanWhitelistTemporaryBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.scan_wl_temporary", "Temp W");
                }
            }
            catch { }

            tboxScanWhitelistAddFolderBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.scan_wl_add", "+W"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxScanWhitelistAddFolderForSelection
            );
            tboxScanWhitelistAddFolderBtn.name = "Tbox_ScanWlAddFolder";
            TboxConfigureActionButtonFlex(tboxScanWhitelistAddFolderBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxScanWhitelistAddFolderBtn, "gallery.tooltip.tbox_scan_wl_add_folder",
                "Add the selected packages' folders to the VaM scan whitelist. Packages in whitelisted folders are scanned by VaM on startup.");

            tboxScanWhitelistRemoveFolderBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.scan_wl_remove", "-W"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxScanWhitelistRemoveFolderForSelection
            );
            tboxScanWhitelistRemoveFolderBtn.name = "Tbox_ScanWlRemoveFolder";
            TboxConfigureActionButtonFlex(tboxScanWhitelistRemoveFolderBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxScanWhitelistRemoveFolderBtn, "gallery.tooltip.tbox_scan_wl_remove_folder",
                "Remove the selected packages' folders from the VaM scan whitelist. VaM will no longer scan these folders on startup.");

            tboxDisableAutoInstallBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxDisableAutoInstallSelectedPackages
            );
            tboxDisableAutoInstallBtn.name = "Tbox_NoAutoInstall";
            TboxConfigureActionButtonFlex(tboxDisableAutoInstallBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxDisableAutoInstallBtn, "gallery.tooltip.tbox_no_autoinstall", "Clear auto-install and VPB auto-load for selected packages. When scan whitelist is enabled, this also removes the persistent per-package startup-scan whitelist override.");
            try
            {
                var autoLoadOffIcon = UI.LoadIconSprite("vpb_icons/auto_off.png", Color.white);
                if (autoLoadOffIcon != null) UI.AddIconToButton(tboxDisableAutoInstallBtn, autoLoadOffIcon, padding: 6f);
                else
                {
                    Text t = tboxDisableAutoInstallBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.no_autoinstall", "No autoinstall");
                }
            }
            catch { }

            // ── Pin toggle (right edge, always visible) ───────────────────────────
            tboxPinBtn = UI.CreateUIButton(
                tbox, 44, 0, "", 15,
                0, 0, AnchorPresets.vStretchRight,
                () =>
                {
                    tboxPinned = !tboxPinned;
                    if (VPBConfig.Instance != null)
                    {
                        VPBConfig.Instance.GalleryTboxToolbarPinned = tboxPinned;
                        try { VPBConfig.Instance.Save(true, true); } catch { }
                    }
                    RefreshTboxPinVisual();
                }
            );
            tboxPinBtn.name = "Tbox_Pin";
            // Pin button is anchored to the bottom row (tooltip row), not the full bar
            var pinRT = tboxPinBtn.GetComponent<RectTransform>();
            pinRT.anchorMin = new Vector2(1f, 0f);
            pinRT.anchorMax = new Vector2(1f, 0f);
            pinRT.pivot = new Vector2(1f, 0.5f);
            pinRT.anchoredPosition = new Vector2(0f, tboxInfoRowHeight * 0.5f);
            pinRT.sizeDelta = new Vector2(44f, 44f);

            tboxPinBtnText = tboxPinBtn.GetComponentInChildren<Text>();

            tboxPinOnSprite = UI.LoadIconSprite("vpb_icons/pin_on.png", new Color(0.78f, 0.78f, 0.78f, 1f));
            tboxPinOffSprite = UI.LoadIconSprite("vpb_icons/pin_off.png", new Color(0.78f, 0.78f, 0.78f, 1f));
            { Sprite init = tboxPinOffSprite ?? tboxPinOnSprite; if (init != null) { UI.AddIconToButton(tboxPinBtn, init); tboxPinIconImage = tboxPinBtn.transform.Find("Icon")?.GetComponent<Image>(); } }

            // Left border line on pin button (visual separator)
            {
                var sep = new GameObject("Separator");
                sep.transform.SetParent(tboxPinBtn.transform, false);
                var sepImg = sep.AddComponent<Image>();
                sepImg.color = new Color(1f, 1f, 1f, 0.08f);
                sepImg.raycastTarget = false;
                var sepRT = sep.GetComponent<RectTransform>();
                sepRT.anchorMin = new Vector2(0f, 0.15f);
                sepRT.anchorMax = new Vector2(0f, 0.85f);
                sepRT.pivot = new Vector2(0f, 0.5f);
                sepRT.anchoredPosition = Vector2.zero;
                sepRT.sizeDelta = new Vector2(1f, 0f);
            }

            // Thin separator line at the row boundary (between tooltip row and toolbox row)
            {
                var rowSepGO = new GameObject("RowSeparator");
                rowSepGO.transform.SetParent(tbox.transform, false);
                var rowSepImg = rowSepGO.AddComponent<Image>();
                rowSepImg.color = new Color(1f, 1f, 1f, 0.12f);
                rowSepImg.raycastTarget = false;
                var rowSepRT = rowSepGO.GetComponent<RectTransform>();
                rowSepRT.anchorMin = new Vector2(0f, 0f);
                rowSepRT.anchorMax = new Vector2(1f, 0f);
                rowSepRT.pivot = new Vector2(0.5f, 0f);
                rowSepRT.anchoredPosition = new Vector2(0f, tboxInfoRowHeight);
                rowSepRT.sizeDelta = new Vector2(0f, 1f);

                // Scale action to reposition separator when InnerPaneScale changes
                var rsRT = rowSepRT;
                innerPaneScaleActions.Add(s =>
                {
                    if (rsRT != null) rsRT.anchoredPosition = new Vector2(0f, 60f * s);
                });
            }

            // Scale actions to resize rows when InnerPaneScale changes
            {
                var lRT = tboxLabelLayerRT;
                var bRT = tboxButtonsLayerRT;
                var pRT = pinRT;
                var fRT = tboxFilterModeRowRT;
                var fLE = tboxFilterModeRowLE;
                innerPaneScaleActions.Add(s =>
                {
                    float rowH = 60f * s;
                    tboxInfoRowHeight = rowH;
                    if (lRT != null) lRT.sizeDelta = new Vector2(lRT.sizeDelta.x, rowH);
                    if (bRT != null) bRT.anchoredPosition = new Vector2(0f, rowH);
                    if (pRT != null) pRT.sizeDelta = new Vector2(44f * s, 44f * s);
                    if (fLE != null) { fLE.minHeight = rowH; fLE.preferredHeight = rowH; }
                    try { TboxSetAllFlexActionButtonHeights(Mathf.Max(34f, rowH - 8f)); } catch { }
                    try { RefreshTboxFlexButtonLayout(); } catch { }
                });
            }

            if (VPBConfig.Instance != null)
                tboxPinned = VPBConfig.Instance.GalleryTboxToolbarPinned;
            RefreshTboxPinVisual();
            AddTooltip(tboxPinBtn, "gallery.tooltip.tbox_pin", "Pin — keep toolbar expanded");

            // Populate person atom buttons with whatever data is already loaded
            try { RefreshTboxPersonAtomButtons(); } catch { }
        }

        private void RefreshTboxPinVisual()
        {
            if (tboxPinBtnText == null) return;
            if (tboxPinned)
            {
                tboxPinBtnText.text = "●";
                tboxPinBtnText.color = new Color(0.45f, 0.75f, 0.90f, 1f); // teal accent
                if (tboxPinIconImage != null && tboxPinOnSprite != null) tboxPinIconImage.sprite = tboxPinOnSprite;
            }
            else
            {
                tboxPinBtnText.text = "○";
                tboxPinBtnText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                if (tboxPinIconImage != null && tboxPinOffSprite != null) tboxPinIconImage.sprite = tboxPinOffSprite;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Public wrapper called after scene loads to ensure toolbox person atom buttons are refreshed with the new atoms.</summary>
        public void RefreshTboxPersonAtomButtonsAfterSceneLoad()
        {
            try
            {
                EnsureTboxUI();
                RefreshTboxPersonAtomButtons();
                // Force layout rebuild to ensure buttons appear immediately
                try { Canvas.ForceUpdateCanvases(); } catch { }
            }
            catch { }
        }

        private string GetPersonAtomDisplayLabel(Atom atom, string uid)
        {
            try
            {
                JSONStorable storable = atom?.GetStorableByID("AppearancePresets");
                if (storable != null)
                {
                    JSONStorableString presetParam = null;
                    try { presetParam = storable.GetStringJSONParam("presetName"); } catch { }
                    if (presetParam != null && !string.IsNullOrEmpty(presetParam.val))
                    {
                        string presetName = MVR.FileManagementSecure.FileManagerSecure.GetFileName(presetParam.val);
                        if (!string.IsNullOrEmpty(presetName))
                            return $"{presetName} ({uid})";
                    }
                }
            }
            catch { }
            return uid;
        }

        private void RefreshTboxPersonAtomButtons()
        {
            EnsureTboxUI();
            if (tboxButtonStash == null) return;

            // Move old rows out of flex rows before destroying so the HLG stops seeing them this frame
            if (tboxButtonStash != null)
            {
                Transform s = tboxButtonStash.transform;
                foreach (var go in tboxPersonAtomBtns) { if (go != null) go.transform.SetParent(s, false); }
            }
            foreach (var go in tboxPersonAtomBtns) { if (go != null) Destroy(go); }
            tboxPersonAtomBtns.Clear();

            bool hasReal = personAtoms.Count > 0 && personAtoms[0] != null;
            if (!hasReal)
            {
                try { RefreshTboxFlexButtonLayout(); } catch { }
                return;
            }

            float innerRowH   = Mathf.Max(34f, tboxInfoRowHeight - 8f);
            float sScale      = VPBConfig.Instance != null ? VPBConfig.Instance.InnerPaneScale : 1f;
            Sprite renameSpr  = UI.LoadIconSprite("vpb_icons/rename.png",       new Color(0.78f, 0.78f, 0.78f, 1f));
            Sprite saveSpr    = gallerySaveSprite;
            Color renameBackdrop = new Color(0.35f, 0.35f, 0.42f, 1f);
            Color saveBackdrop   = new Color(0.20f, 0.35f, 0.22f, 1f);
            Color activeColor    = new Color(0.25f, 0.35f, 0.50f, 1f);
            Color inactiveColor  = new Color(0.18f, 0.18f, 0.20f, 1f);
            Transform stash      = tboxButtonStash.transform;
            float iconBtnSz      = innerRowH;
            float iconBtnCount   = (renameSpr != null ? 1 : 0) + (saveSpr != null ? 1 : 0);
            float iconBtnsW      = iconBtnCount * iconBtnSz + (iconBtnCount > 1 ? (iconBtnCount - 1) * 2f : 0f);

            for (int i = 0; i < personAtoms.Count; i++)
            {
                Atom atom = personAtoms[i];
                if (atom == null) continue;

                string uid      = targetDropdownOptions.Count > i ? targetDropdownOptions[i] : "Unknown";
                string label    = GetPersonAtomDisplayLabel(atom, uid);
                bool   isActive = (targetDropdownValue == i);
                int    captured = i;

                // Row GO — stashed initially, placed into flex rows by RefreshTboxFlexButtonLayout
                var rowGO = new GameObject("TboxPersonAtomRow_" + i);
                rowGO.transform.SetParent(stash, false);
                var rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.anchorMin = Vector2.zero; rowRT.anchorMax = Vector2.one;
                rowRT.pivot = new Vector2(0.5f, 0.5f);
                rowRT.offsetMin = rowRT.offsetMax = Vector2.zero;
                var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
                rowHLG.spacing = 2f; rowHLG.childAlignment = TextAnchor.MiddleLeft;
                rowHLG.childControlWidth = true; rowHLG.childForceExpandWidth = false;
                rowHLG.childControlHeight = true; rowHLG.childForceExpandHeight = true;
                var rowLE = rowGO.AddComponent<LayoutElement>();
                rowLE.minWidth = 90f + (iconBtnsW > 0 ? 2f + iconBtnsW : 0f);
                rowLE.preferredWidth = 160f + (iconBtnsW > 0 ? 2f + iconBtnsW : 0f);
                rowLE.flexibleWidth = 1f;
                rowLE.minHeight = innerRowH; rowLE.preferredHeight = innerRowH; rowLE.flexibleHeight = 1f;
                tboxPersonAtomBtns.Add(rowGO);

                // Main button (sets this atom as target on click)
                var mainBtn = UI.CreateUIButton(rowGO, 0, 0, label, 14, 0, 0, AnchorPresets.stretchAll,
                    () => { targetDropdownValue = captured; UpdateTargetDropdownUI(); });
                mainBtn.name = "PersonAtomBtn_" + label;
                var mainImg = mainBtn.GetComponent<Image>();
                if (mainImg != null) mainImg.color = isActive ? activeColor : inactiveColor;
                var mainLE = mainBtn.GetComponent<LayoutElement>() ?? mainBtn.AddComponent<LayoutElement>();
                mainLE.flexibleWidth = 1f; mainLE.minHeight = innerRowH;
                mainLE.preferredHeight = innerRowH; mainLE.flexibleHeight = 1f;
                var txt = mainBtn.GetComponentInChildren<Text>(true);
                if (txt != null) txt.gameObject.SetActive(true);
                string tooltipText = $"Person atom: {atom.uid}\nClick to select as target";
                AddTooltipPlain(mainBtn, tooltipText);

                // Save appearance preset button
                if (saveSpr != null)
                {
                    Atom capturedAtom = atom;
                    var saveBtn = UI.CreateSideTabSquareIconButton(
                        rowGO, iconBtnSz, saveSpr,
                        () => SavePresetFromStorable(capturedAtom, "AppearancePresets"),
                        saveBackdrop, Mathf.Max(3f, 4f * sScale));
                    AddTooltipPlain(saveBtn, VPBTranslation.T("gallery.tbox.save_appearance", "Save appearance preset for this person"));
                }

                // Rename button
                if (renameSpr != null)
                {
                    Atom capturedAtom = atom;
                    var renameBtn = UI.CreateSideTabSquareIconButton(
                        rowGO, iconBtnSz, renameSpr,
                        () => ShowPersonAtomRenameOverlay(capturedAtom),
                        renameBackdrop, Mathf.Max(3f, 4f * sScale));
                    AddTooltipPlain(renameBtn, VPBTranslation.T("gallery.rename.tooltip", "Rename this person"));
                }
            }

            try
            {
                Canvas.ForceUpdateCanvases();
                RefreshTboxFlexButtonLayout();
                Canvas.ForceUpdateCanvases();
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void UpdateSelectionContextMenu()
        {
            if (canvas == null) return;
            EnsureTboxUI();
            if (tbox == null) return;

            if (VPBConfig.Instance != null)
            {
                bool cfgPin = VPBConfig.Instance.GalleryTboxToolbarPinned;
                if (tboxPinned != cfgPin)
                {
                    tboxPinned = cfgPin;
                    RefreshTboxPinVisual();
                }
            }

            int sel = (selectedFiles != null) ? selectedFiles.Count : 0;
            int total = (currentFilteredFiles != null) ? currentFilteredFiles.Count : 0;

            // Update label: "X Selected  ·  Y Items" when selected, or just "Y Items"
            if (tboxLabel != null)
            {
                string countStr = string.Format(VPBTranslation.T("gallery.items.count", "{0} Items"), total);
                if (sel > 0)
                {
                    string selStr = sel == 1
                        ? VPBTranslation.T("gallery.tbox.selected_one", "1 Selected")
                        : string.Format(VPBTranslation.T("gallery.tbox.selected_many", "{0} Selected"), sel);
                    tboxLabel.text = string.Format("{0}  ·  {1}", selStr, countStr);
                }
                else
                {
                    tboxLabel.text = countStr;
                }
            }

            // Action buttons when there is a selection, cleanup mode active, or person atoms present; pin persists until user toggles (saved in VPB.cfg).
            bool hasPersonAtoms = personAtoms != null && personAtoms.Count > 0 && personAtoms[0] != null;
            bool isSettingsMode = IsSettingsPanelOpen();
            bool canExpand = isSettingsMode || sel > 0 || cleanupModeActive || hasPersonAtoms;
            // Only force collapse if not pinned and not currently expanded (preserve expansion state during category switches)
            if (!canExpand && !tboxPinned && tboxExpandT < 0.01f)
            {
                tboxExpandT = 0f;
                tboxIsHovered = false;
                tboxButtonLayoutRows = 1;
                float collapsedHeight = tboxInfoRowHeight;
                // Account for filter row when active even when collapsed
                if (tboxFilterModeRowGO != null && tboxFilterModeRowGO.activeSelf)
                    collapsedHeight += tboxInfoRowHeight + tboxBtnRowGap;
                if (tboxButtonsLayerRT != null)
                    tboxButtonsLayerRT.sizeDelta = new Vector2(tboxButtonsLayerRT.sizeDelta.x, collapsedHeight);
            }

            if (tboxHintLabel != null && tboxHintLabel.gameObject != null)
            {
                bool showPinnedHint = sel == 0 && tboxPinned && !cleanupModeActive;
                tboxHintLabel.gameObject.SetActive(showPinnedHint);
                if (showPinnedHint)
                    tboxHintLabel.text = VPBTranslation.T("gallery.tbox.pinned_select", "Pinned — select items for actions");
            }

            // Auto-expand toolbox if person atoms present, otherwise require hover or pin
            bool wantExpanded = isSettingsMode || (canExpand && (tboxIsHovered || tboxPinned || hasPersonAtoms));

            // No animation: snap expanded/collapsed state immediately
            float targetT = wantExpanded ? 1f : 0f;
            tboxExpandT = targetT;

            // Animate bar height: grow offsetMax upward to reveal the button band (1 or 2 rows)
            if (tboxRT != null)
            {
                if ((sel > 0 || cleanupModeActive || hasPersonAtoms) && tboxButtonsFlexRootRT != null && tboxExpandT > 0.02f)
                {
                    float w = tboxButtonsFlexRootRT.rect.width;
                    if (w > 8f && Mathf.Abs(w - tboxLastFlexAvailW) > 2f)
                    {
                        try { RefreshTboxFlexButtonLayout(); } catch { }
                    }
                }

                float btnBand = tboxInfoRowHeight * Mathf.Max(1, tboxButtonLayoutRows)
                    + (tboxButtonLayoutRows > 1 ? tboxBtnRowGap : 0f);
                // Add filter row height when active
                if (tboxFilterModeRowGO != null && tboxFilterModeRowGO.activeSelf)
                    btnBand += tboxInfoRowHeight + tboxBtnRowGap;
                float targetTop = tboxTopOffsetBase + btnBand * tboxExpandT;
                tboxRT.offsetMax = new Vector2(tboxRT.offsetMax.x, targetTop);
            }

            // Label is suppressed when path/status is actually visible, or buttons are expanded
            bool pathVisible = hoverPathText != null && hoverPathText.gameObject.activeSelf
                            && hoverPathCanvasGroup != null && hoverPathCanvasGroup.alpha > 0.1f;
            bool infoShowing = pathVisible
                             || !string.IsNullOrEmpty(dragStatusMsg)
                             || !string.IsNullOrEmpty(temporaryStatusMsg);
            // Label alpha tracks collapse directly — no separate lerp needed
            float labelTarget = (infoShowing || tboxExpandT > 0.05f) ? 0f : 1f;
            if (tboxLabelCG != null)
                tboxLabelCG.alpha = labelTarget;

            // Buttons stay fully opaque — RectMask2D handles the slide-in reveal as the bar grows.
            // Gate on tboxExpandT only (not infoShowing) so that a fading hover-path label
            // doesn't suppress buttons and cause them to flash when the path finally fades out.
            if (tboxButtonsCG != null)
            {
                bool showButtons = canExpand && tboxExpandT > 0.05f;
                tboxButtonsCG.alpha = showButtons ? 1f : 0f;
                tboxButtonsCG.blocksRaycasts = canExpand && tboxExpandT > 0.5f;
                tboxButtonsCG.interactable = canExpand && tboxExpandT > 0.85f;
            }

            if (sel > 0 || cleanupModeActive || (!IsHubMode && activeContentType == ContentType.History))
                RefreshTboxConditionalActionButtons();
            else if (IsSettingsPanelOpen())
                RefreshTboxConditionalActionButtons();

            // JSON bench is dev-only; RefreshTboxConditionalActionButtons does not run when selection is empty.
            try
            {
                bool benchVisible = !cleanupModeActive && VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode;
                if (tboxJsonParserBenchBtn != null && tboxJsonParserBenchBtn.activeSelf != benchVisible)
                {
                    tboxJsonParserBenchBtn.SetActive(benchVisible);
                    RefreshTboxFlexButtonLayout();
                }
            }
            catch { }

            // Keep grid / side tab scrollers above the footer while tbox height animates.
            try
            {
                if (contentScrollRT != null)
                {
                    float tabTop = TabScrollTopOffset();
                    SyncGalleryMainAreaBottomEdge(
                        contentScrollRT.offsetMin.x,
                        contentScrollRT.offsetMax.x,
                        contentScrollRT.offsetMax.y,
                        tabTop);
                }
            }
            catch { }
        }

        /// <summary>Copy/Delete/Hide/Unhide/Autoinstall: counts in labels and compact layout for the hide/AI group.</summary>
        private void RefreshTboxConditionalActionButtons()
        {
            int copyN = 0, deleteN = 0, hideN = 0, unhideN = 0, aiN = 0, noAiN = 0, scanWlTemporaryN = 0;
            int scanWlAddN = 0, scanWlRemoveN = 0;
            bool anyPkgInstalled = false;     // in AddonPackages
            bool anyPkgNotInstalled = false;  // in AllPackages

            if (cleanupModeActive && currentFilteredFiles != null && currentFilteredFiles.Count > 0)
            {
                bool anyCleanupEntry = false;
                for (int i = 0; i < currentFilteredFiles.Count; i++)
                {
                    if (currentFilteredFiles[i] is CleanupFileEntry)
                    {
                        anyCleanupEntry = true;
                        break;
                    }
                }
                if (!anyCleanupEntry) cleanupModeActive = false;
            }
            bool isCleanup = cleanupModeActive;
            bool historyBrowse = !IsHubMode && activeContentType == ContentType.History;
            bool isSettings = IsSettingsPanelOpen();
            void show(GameObject go, bool on)
            {
                if (go != null && go.activeSelf != on) go.SetActive(on);
            }

            if (isSettings)
            {
                // Settings mode: only show SAVE/CANCEL. Hide all other toolbox actions and person target buttons.
                show(tboxSettingsCancelBtn, true);
                show(tboxSettingsSaveBtn, true);
                show(tboxFilterModeRowGO, false);
                show(tboxFilterBackBtn, false);
                show(tboxFilterClearBtn, false);
                for (int i = 0; i < tboxPersonAtomBtns.Count; i++) show(tboxPersonAtomBtns[i], false);

                show(tboxCleanupBtn, false);
                show(tboxCleanupApplyBtn, false);
                show(tboxCleanupFilterAllBtn, false);
                show(tboxCleanupFilterDupBtn, false);
                show(tboxCleanupFilterOldBtn, false);
                show(tboxCleanupFilterDamagedBtn, false);
                show(tboxCleanupSelectVisibleBtn, false);
                show(tboxCleanupSelectDupBtn, false);
                show(tboxCleanupSelectOldBtn, false);
                show(tboxCleanupSelectDamagedBtn, false);
                show(tboxCleanupClearBtn, false);
                show(tboxCleanupAddExcludeBtn, false);
                show(tboxCleanupRemoveExcludeBtn, false);

                show(tboxAutoInstallBtn, false);
                show(tboxDisableAutoInstallBtn, false);
                show(tboxHideBtn, false);
                show(tboxUnhideBtn, false);
                show(tboxScanWhitelistTemporaryBtn, false);
                show(tboxScanWhitelistAddFolderBtn, false);
                show(tboxScanWhitelistRemoveFolderBtn, false);
                show(tboxLoadBtn, false);
                show(tboxUnloadBtn, false);
                show(tboxLoadDepsBtn, false);
                show(tboxCacheTexturesBtn, false);
                show(tboxJsonParserBenchBtn, false);
                show(tboxOpenHubBtn, false);
                show(tboxCopyPkgNamesBtn, false);
                show(tboxSceneImportBtn, false);
                show(tboxDeleteBtn, false);
                show(tboxRemoveHistoryBtn, false);
                show(tboxSelectAllBtn, false);
                show(tboxClearSelectionBtn, false);

                try { RefreshTboxFlexButtonLayout(); } catch { }
                return;
            }

            show(tboxSettingsCancelBtn, false);
            show(tboxSettingsSaveBtn, false);

            show(tboxCleanupBtn, !isCleanup);
            show(tboxCleanupApplyBtn, false);
            // Filtering moved to cleanup side-tab list (Category-style), keep toolbox cleaner.
            show(tboxCleanupFilterAllBtn, false);
            show(tboxCleanupFilterDupBtn, false);
            show(tboxCleanupFilterOldBtn, false);
            show(tboxCleanupFilterDamagedBtn, false);
            show(tboxCleanupSelectVisibleBtn, false);
            show(tboxCleanupSelectDupBtn, false);
            show(tboxCleanupSelectOldBtn, false);
            show(tboxCleanupSelectDamagedBtn, false);
            show(tboxCleanupClearBtn, false);
            bool cleanupHasExcludedSelection = isCleanup && CleanupSelectionHasExcludedEntries();
            bool cleanupHasNonExcludedSelection = isCleanup && CleanupSelectionHasNonExcludedEntries();
            show(tboxCleanupAddExcludeBtn, cleanupHasNonExcludedSelection);
            show(tboxCleanupRemoveExcludeBtn, cleanupHasExcludedSelection);

            show(tboxAutoInstallBtn, !isCleanup);
            show(tboxDisableAutoInstallBtn, !isCleanup);
            show(tboxHideBtn, !isCleanup);
            show(tboxUnhideBtn, !isCleanup);
            show(tboxScanWhitelistTemporaryBtn, !isCleanup);
            show(tboxScanWhitelistAddFolderBtn, false);
            show(tboxScanWhitelistRemoveFolderBtn, false);
            show(tboxLoadBtn, !isCleanup && !ScanWhitelistManager.Instance.IsEnabled);
            show(tboxUnloadBtn, !isCleanup && !ScanWhitelistManager.Instance.IsEnabled);
            show(tboxLoadDepsBtn, !isCleanup);
            show(tboxCacheTexturesBtn, !isCleanup);
            show(tboxJsonParserBenchBtn, !isCleanup && VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode);
            show(tboxOpenHubBtn, !isCleanup);

            if (isCleanup)
            {
                show(tboxRemoveHistoryBtn, false);
                int selectedCount = selectedFiles != null ? selectedFiles.Count : 0;
                SetTboxButtonEnabledVisual(tboxDeleteBtn, selectedCount > 0);
                SetTboxButtonEnabledVisual(tboxCleanupApplyBtn, false);
                SetTboxButtonEnabledVisual(tboxCleanupFilterAllBtn, true);
                SetTboxButtonEnabledVisual(tboxCleanupFilterDupBtn, true);
                SetTboxButtonEnabledVisual(tboxCleanupFilterOldBtn, true);
                SetTboxButtonEnabledVisual(tboxCleanupFilterDamagedBtn, true);
                SetTboxButtonEnabledVisual(tboxCleanupSelectVisibleBtn, false);
                SetTboxButtonEnabledVisual(tboxCleanupSelectDupBtn, currentFilteredFiles != null && currentFilteredFiles.Count > 0);
                SetTboxButtonEnabledVisual(tboxCleanupSelectOldBtn, currentFilteredFiles != null && currentFilteredFiles.Count > 0);
                SetTboxButtonEnabledVisual(tboxCleanupSelectDamagedBtn, currentFilteredFiles != null && currentFilteredFiles.Count > 0);
                SetTboxButtonEnabledVisual(tboxCleanupClearBtn, false);
                SetTboxButtonEnabledVisual(tboxCleanupAddExcludeBtn, cleanupHasNonExcludedSelection);
                SetTboxButtonEnabledVisual(tboxCleanupRemoveExcludeBtn, cleanupHasExcludedSelection);

                SetTboxButtonEnabledVisual(tboxCopyPkgNamesBtn, selectedCount > 0);
                SetTboxButtonEnabledVisual(tboxHideBtn, false);
                SetTboxButtonEnabledVisual(tboxUnhideBtn, false);
                SetTboxButtonEnabledVisual(tboxScanWhitelistTemporaryBtn, false);
                SetTboxButtonEnabledVisual(tboxScanWhitelistAddFolderBtn, false);
                SetTboxButtonEnabledVisual(tboxScanWhitelistRemoveFolderBtn, false);
                SetTboxButtonEnabledVisual(tboxAutoInstallBtn, false);
                SetTboxButtonEnabledVisual(tboxDisableAutoInstallBtn, false);
                SetTboxButtonEnabledVisual(tboxLoadBtn, false);
                SetTboxButtonEnabledVisual(tboxUnloadBtn, false);
                SetTboxButtonEnabledVisual(tboxLoadDepsBtn, false);
                SetTboxButtonEnabledVisual(tboxCacheTexturesBtn, false);
                SetTboxButtonEnabledVisual(tboxJsonParserBenchBtn, false);
                SetTboxButtonEnabledVisual(tboxOpenHubBtn, false);

                RefreshTboxFlexButtonLayout();
                return;
            }

            if (selectedFiles != null && selectedFiles.Count > 0)
            {
                copyN = CollectUniquePackageUidsFromSelection(selectedFiles).Count
                    + CollectUniqueLocalSceneGalleryRelativePathsFromSelection(selectedFiles).Count;
                try { deleteN = GetTboxDeleteEligiblePackageCount() + GetTboxDeleteEligibleLocalSceneCount(); } catch { deleteN = 0; }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out FileEntry diskFe, out bool hidden, out bool fiAi, out bool uidAl, out bool uidWl))
                        continue;
                    if (!seen.Add(uid)) continue;
                    if (hidden) unhideN++;
                    else hideN++;
                    bool scanWlEnabled = ScanWhitelistManager.Instance.IsEnabled;
                    bool uidWlAny = scanWlEnabled && ScanWhitelistManager.Instance.IsUidOverrideIncluded(uid);
                    bool hasAnyAiFlag = fiAi || uidAl || (scanWlEnabled && uidWl);
                    bool missingAnyAiFlag = !fiAi || !uidAl || (scanWlEnabled && !uidWl);
                    if (hasAnyAiFlag) noAiN++;
                    if (missingAnyAiFlag) aiN++;
                    if (scanWlEnabled && !uidWlAny) scanWlTemporaryN++;
                    // Scan whitelist add/remove counts (only when feature is enabled)
                    if (ScanWhitelistManager.Instance.IsEnabled)
                    {
                        try
                        {
                            var pkg = FileManager.GetPackage(uid, ensureInstalled: false);
                            if (pkg != null)
                            {
                                string normPath = (pkg.Path ?? "").Replace('\\', '/');
                                if (normPath.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase))
                                {
                                    string folder = ScanWhitelistManager.FolderFromVarPath(normPath);
                                    if (!string.IsNullOrEmpty(folder))
                                    {
                                        bool folderWhitelisted = ScanWhitelistManager.Instance.IsPathWhitelisted(normPath);
                                        if (folderWhitelisted) scanWlRemoveN++;
                                        else scanWlAddN++;
                                    }
                                }

                            }
                        }
                        catch { }
                    }

                    // Fast install-state summary for Load/Unload buttons.
                    // Use the resolved disk FileEntry (already computed by TryGetTboxResolvablePackageState) and
                    // infer from its path prefix; avoids any rescans or heavy indexing work.
                    try
                    {
                        // Local scenes (Saves/scene JSON) do not participate in load/unload.
                        if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false))
                            continue;

                        string p = null;
                        try { p = diskFe != null ? (diskFe.Path ?? diskFe.Uid) : null; } catch { p = null; }
                        if (!string.IsNullOrEmpty(p))
                        {
                            p = p.Replace('\\', '/');
                            int internalSep = p.IndexOf(":/", StringComparison.Ordinal);
                            if (internalSep >= 0) p = p.Substring(0, internalSep);
                            if (p.StartsWith("AddonPackages/", StringComparison.OrdinalIgnoreCase)) anyPkgInstalled = true;
                            else if (p.StartsWith("AllPackages/", StringComparison.OrdinalIgnoreCase)) anyPkgNotInstalled = true;
                        }
                    }
                    catch { }
                }

                // Temporary whitelist should account for selected packages + their dependencies.
                if (ScanWhitelistManager.Instance.IsEnabled)
                {
                    var tempCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var rootUid in seen)
                    {
                        if (string.IsNullOrEmpty(rootUid)) continue;
                        tempCandidates.Add(rootUid);
                        try
                        {
                            var deps = FileManager.GetDependenciesDeep(rootUid, 2);
                            if (deps == null || deps.Count == 0) continue;
                            foreach (var depId in deps)
                            {
                                if (string.IsNullOrEmpty(depId)) continue;
                                try
                                {
                                    var depPkg = FileManager.GetPackage(depId, ensureInstalled: false);
                                    string depUid = depPkg != null ? depPkg.Uid : depId;
                                    if (!string.IsNullOrEmpty(depUid)) tempCandidates.Add(depUid);
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    int tmpN = 0;
                    foreach (var uid in tempCandidates)
                    {
                        if (!ScanWhitelistManager.Instance.IsUidOverrideIncluded(uid))
                            tmpN++;
                    }
                    scanWlTemporaryN = tmpN;
                }
            }

            if (tboxCopyPkgNamesBtn != null)
                SetTboxCountButtonLabel(tboxCopyPkgNamesBtn, "gallery.tbox.copy_names_count", "Copy Names ({0})", copyN);
            // Delete is an icon button; count is intentionally not shown on the label.
            SetTboxButtonEnabledVisual(tboxDeleteBtn, deleteN > 0);

            bool showHide = hideN > 0;
            bool showUnhide = unhideN > 0;
            bool showAi = aiN > 0;
            bool showNoAi = noAiN > 0;

            if (tboxHideBtn != null)
            {
                SetTboxButtonEnabledVisual(tboxHideBtn, showHide);
                // Hide is an icon button; count is intentionally not shown on the label.
            }
            if (tboxUnhideBtn != null)
            {
                SetTboxButtonEnabledVisual(tboxUnhideBtn, showUnhide);
                // Unhide is an icon button; count is intentionally not shown on the label.
            }
            if (tboxScanWhitelistTemporaryBtn != null)
                SetTboxButtonEnabledVisual(tboxScanWhitelistTemporaryBtn, scanWlTemporaryN > 0);
            if (tboxScanWhitelistAddFolderBtn != null)
                SetTboxButtonEnabledVisual(tboxScanWhitelistAddFolderBtn, scanWlAddN > 0);
            if (tboxScanWhitelistRemoveFolderBtn != null)
                SetTboxButtonEnabledVisual(tboxScanWhitelistRemoveFolderBtn, scanWlRemoveN > 0);
            if (tboxAutoInstallBtn != null)
            {
                SetTboxButtonEnabledVisual(tboxAutoInstallBtn, showAi);
                // Auto install is an icon button; count is intentionally not shown on the label.
            }
            if (tboxDisableAutoInstallBtn != null)
            {
                SetTboxButtonEnabledVisual(tboxDisableAutoInstallBtn, showNoAi);
                // No auto install is an icon button; count is intentionally not shown on the label.
            }

            // Load/LoadDeps should always be available (requested); Unload still reflects install state.
            if (tboxLoadBtn != null)     SetTboxButtonEnabledVisual(tboxLoadBtn, true);
            if (tboxLoadDepsBtn != null) SetTboxButtonEnabledVisual(tboxLoadDepsBtn, true);
            if (tboxJsonParserBenchBtn != null)
                SetTboxButtonEnabledVisual(
                    tboxJsonParserBenchBtn,
                    VPBConfig.Instance != null && VPBConfig.Instance.IsDevMode && selectedFiles != null && selectedFiles.Count > 0);
            bool hasAnyPkg = anyPkgInstalled || anyPkgNotInstalled;
            if (tboxUnloadBtn != null)   SetTboxButtonEnabledVisual(tboxUnloadBtn, hasAnyPkg && anyPkgInstalled);

            // Hub button: only for a single selected package row (.var uid resolvable).
            bool showOpenHub = false;
            if (selectedFiles != null && selectedFiles.Count == 1 && tboxOpenHubBtn != null)
            {
                try
                {
                    var uid = TryGetPackageUidForEntry(selectedFiles[0]);
                    showOpenHub = !string.IsNullOrEmpty(uid);
                }
                catch { showOpenHub = false; }
            }
            if (tboxOpenHubBtn != null)
            {
                tboxOpenHubBtn.SetActive(showOpenHub);
                SetTboxButtonEnabledVisual(tboxOpenHubBtn, showOpenHub);
            }

            show(tboxRemoveHistoryBtn, historyBrowse);
            if (tboxRemoveHistoryBtn != null)
                SetTboxButtonEnabledVisual(
                    tboxRemoveHistoryBtn,
                    historyBrowse && selectedFiles != null && selectedFiles.Count > 0);

            RefreshTboxFlexButtonLayout();
        }

        private void TboxRemoveSelectedFromHistory()
        {
            if (activeContentType != ContentType.History || selectedFiles == null || selectedFiles.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            if (selectedFiles.Count > 1)
            {
                bool confirmed = pendingHistoryRemoveConfirm
                    && pendingHistoryRemoveConfirmCount == selectedFiles.Count
                    && now <= pendingHistoryRemoveConfirmUntilRealtime;
                if (!confirmed)
                {
                    pendingHistoryRemoveConfirm = true;
                    pendingHistoryRemoveConfirmCount = selectedFiles.Count;
                    pendingHistoryRemoveConfirmUntilRealtime = now + 4f;
                    ShowTemporaryStatus(
                        string.Format(
                            VPBTranslation.T("gallery.history.confirm_remove_n", "Press Remove History again to confirm removing {0} items."),
                            selectedFiles.Count),
                        4f);
                    return;
                }
                pendingHistoryRemoveConfirm = false;
                pendingHistoryRemoveConfirmCount = 0;
                pendingHistoryRemoveConfirmUntilRealtime = 0f;
            }

            var keySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var keys = new List<string>(selectedFiles.Count);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                var f = selectedFiles[i];
                if (f == null) continue;
                try
                {
                    string k = null;
                    if (f is VarFileEntry vfe && !string.IsNullOrEmpty(vfe.GalleryItemUsageKey))
                        k = vfe.GalleryItemUsageKey;
                    else
                        k = VpbLocalDatabase.BuildUsageKey(f);
                    if (VpbLocalDatabase.LogHistoryUsageDebug)
                    {
                        try
                        {
                            string gk = (f is VarFileEntry v2) ? (v2.GalleryItemUsageKey ?? "") : "";
                            string bk = "";
                            try { bk = VpbLocalDatabase.BuildUsageKey(f) ?? ""; } catch { }
                            LogUtil.Log("[VPB.History] remove_selection[" + i + "] type=" + f.GetType().Name +
                                        " galleryItemUsageKey=" + (string.IsNullOrEmpty(gk) ? "(none)" : gk) +
                                        " deleteKeyUsed=" + (k ?? "") +
                                        " buildUsageKey=" + bk +
                                        " entryUid=" + (f.Uid ?? "") +
                                        " entryPath=" + (f.Path ?? ""));
                        }
                        catch { }
                    }
                    if (!string.IsNullOrEmpty(k) && keySet.Add(k)) keys.Add(k);
                }
                catch { }
            }
            if (keys.Count == 0)
            {
                if (VpbLocalDatabase.LogHistoryUsageDebug)
                {
                    try { LogUtil.Log("[VPB.History] TboxRemoveSelectedFromHistory: no keys resolved; nothing deleted."); } catch { }
                }
                return;
            }
            var snapshotMap = new Dictionary<string, VpbLocalDatabase.ItemUsageSnapshot>(StringComparer.OrdinalIgnoreCase);
            VpbLocalDatabase.TryReadItemUsageSnapshotsForKeys(keys, snapshotMap);
            pendingHistoryUndoSnapshots = new List<VpbLocalDatabase.ItemUsageSnapshot>(snapshotMap.Values);
            pendingHistoryUndoUntilRealtime = now + 5f;

            if (VpbLocalDatabase.LogHistoryUsageDebug)
            {
                try
                {
                    LogUtil.Log("[VPB.History] TboxRemoveSelectedFromHistory calling TryDeleteItemUsageForKeys count=" + keys.Count +
                                " db=" + Path.GetFileName(VpbLocalDatabase.GetLocalDatabasePathForDiagnostics()));
                }
                catch { }
            }
            VpbLocalDatabase.TryDeleteItemUsageForKeys(keys);
            try
            {
                selectedFiles.Clear();
                selectedFilePaths.Clear();
                selectionAnchorPath = null;
                selectionAnchorIdentityKey = null;
                selectedPath = null;
                selectedHubItem = null;
                RefreshSelectionVisuals();
            }
            catch { }
            if (VpbLocalDatabase.LogHistoryUsageDebug)
            {
                try { LogUtil.Log("[VPB.History] TboxRemoveSelectedFromHistory RefreshHistoryListInPlace next"); } catch { }
            }
            RefreshHistoryListInPlace(true);
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.history.removed_n_with_undo", "Removed {0} from History. Press Ctrl+Z within 5s to undo."),
                    keys.Count),
                5f);
        }

        private bool TryUndoRecentHistoryRemoval()
        {
            if (pendingHistoryUndoSnapshots == null || pendingHistoryUndoSnapshots.Count == 0)
                return false;

            if (Time.realtimeSinceStartup > pendingHistoryUndoUntilRealtime)
            {
                pendingHistoryUndoSnapshots = null;
                pendingHistoryUndoUntilRealtime = 0f;
                return false;
            }

            bool restored = VpbLocalDatabase.TryRestoreItemUsageSnapshots(pendingHistoryUndoSnapshots);
            int restoredCount = pendingHistoryUndoSnapshots.Count;
            pendingHistoryUndoSnapshots = null;
            pendingHistoryUndoUntilRealtime = 0f;

            if (!restored)
            {
                ShowTemporaryStatus(
                    VPBTranslation.T("gallery.history.undo_failed", "Could not restore removed History entries. See log."),
                    2f);
                return true;
            }

            RefreshHistoryListInPlace(true);
            ShowTemporaryStatus(
                string.Format(
                    VPBTranslation.T("gallery.history.undo_restored_n", "Restored {0} History entries."),
                    restoredCount),
                2f);
            return true;
        }

        private void TboxOpenSelectedItemOnHub()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count != 1)
                {
                    ShowTemporaryStatus("Select a single item.");
                    return;
                }

                var f = selectedFiles[0];
                if (f == null)
                {
                    ShowTemporaryStatus("Nothing selected.");
                    return;
                }

                string uid = TryGetPackageUidForEntry(f);
                if (string.IsNullOrEmpty(uid))
                {
                    ShowTemporaryStatus("This item has no Hub page.");
                    return;
                }

                var hub = HubBrowse.singleton;
                // Open Hub first (ensures singleton is initialized in some VaM setups).
                try { VamHookPlugin.singleton?.OpenHubBrowse(); } catch { }
                if (hub == null) hub = HubBrowse.singleton;
                if (hub == null)
                {
                    ShowTemporaryStatus("Hub is not available.");
                    return;
                }

                // Prefer resource_id mapping when available, otherwise fall back to package-name lookup.
                string rid = null;
                try { rid = hub.GetPackageHubResourceId(uid); } catch { rid = null; }
                if (!string.IsNullOrEmpty(rid) && rid != "null")
                {
                    hub.OpenDetail(rid);
                    return;
                }

                // HubBrowse.OpenDetail supports package_name lookup when the second parameter is true.
                // The Hub backend expects the full package name including ".var".
                hub.OpenDetail(uid + ".var", isPackageName: true);
            }
            catch
            {
                ShowTemporaryStatus("Failed to open Hub page. See log.");
            }
        }

        private static void SetTboxCountButtonLabel(GameObject go, string key, string fallbackFmt, int count)
        {
            if (go == null) return;
            Text t = go.GetComponentInChildren<Text>(true);
            if (t != null)
                t.text = string.Format(VPBTranslation.T(key, fallbackFmt), count);
        }

        /// <summary>Unique gallery-relative paths for on-disk Saves/scene JSON rows (for Copy Names).</summary>
        private static HashSet<string> CollectUniqueLocalSceneGalleryRelativePathsFromSelection(IList<FileEntry> files)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (files == null) return set;
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                if (f == null) continue;
                if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string rel, false)) continue;
                if (!string.IsNullOrEmpty(rel)) set.Add(rel.Replace('\\', '/'));
            }
            return set;
        }

        /// <summary>Resolve a gallery row to an on-disk .var for tbox hide/autoinstall actions (one row may share a UID).</summary>
        private bool TryGetTboxResolvablePackageState(FileEntry f, out string uid, out FileEntry diskFe, out bool isHidden, out bool fileAutoInstall, out bool uidAutoLoad)
        {
            return TryGetTboxResolvablePackageState(f, out uid, out diskFe, out isHidden, out fileAutoInstall, out uidAutoLoad, out _);
        }

        /// <summary>Like <see cref="TryGetTboxResolvablePackageState(FileEntry, out string, out FileEntry, out bool, out bool, out bool)"/>, plus persistent scan-whitelist UID override state.</summary>
        private bool TryGetTboxResolvablePackageState(FileEntry f, out string uid, out FileEntry diskFe, out bool isHidden, out bool fileAutoInstall, out bool uidAutoLoad, out bool uidScanWhitelistPersisted)
        {
            uid = null;
            diskFe = null;
            isHidden = false;
            fileAutoInstall = false;
            uidAutoLoad = false;
            uidScanWhitelistPersisted = false;

            if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string relGallery, false))
            {
                uid = relGallery.Replace('\\', '/');
                diskFe = f;
                isHidden = PackageHidePrefs.IsLocalSceneJsonHidden(f);
                try { fileAutoInstall = LocalSceneGallerySupport.IsLocalSceneAutoInstallMarked(f); }
                catch { fileAutoInstall = false; }
                uidAutoLoad = false;
                uidScanWhitelistPersisted = false;
                return true;
            }

            uid = TryGetPackageUidForEntry(f);
            if (string.IsNullOrEmpty(uid)) return false;

            string path = ResolveVarPathForUid(uid);
            if (string.IsNullOrEmpty(path)) return false;

            try
            {
                var fe = FileManager.GetFileEntry(path, true);
                if (fe == null) return false;
                diskFe = fe;
                isHidden = PackageHidePrefs.IsPackageVarHidden(fe);
                try { fileAutoInstall = fe.IsAutoInstall(); }
                catch { fileAutoInstall = false; }
                try
                {
                    uidAutoLoad = AutoLoadPackagesManager.Instance != null && AutoLoadPackagesManager.Instance.IsAutoLoad(uid);
                }
                catch { uidAutoLoad = false; }
                try
                {
                    uidScanWhitelistPersisted = ScanWhitelistManager.Instance.IsUidOverridePersisted(uid);
                }
                catch { uidScanWhitelistPersisted = false; }
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private static bool IsCtrlHeld()
        {
            try
            {
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            }
            catch { return false; }
        }

        private string GetCopyNamesTooltipText(bool ctrlHeld)
        {
            if (ctrlHeld)
                return VPBTranslation.T(
                    "gallery.tooltip.tbox_copy_names_fullpaths",
                    "Copy full disk paths for selected packages and local scenes to clipboard. Release Ctrl to copy names."
                );

            return VPBTranslation.T(
                "gallery.tooltip.tbox_copy_names",
                "Copy selected package .var names and local Saves/scene paths to clipboard. Hold Ctrl to copy full paths."
            );
        }

        private void WireCopyNamesTooltip(GameObject copyNamesBtn)
        {
            if (copyNamesBtn == null) return;
            var del = copyNamesBtn.GetComponent<UIHoverDelegate>();
            if (del == null) del = copyNamesBtn.AddComponent<UIHoverDelegate>();
            del.OnHoverChange += OnCopyNamesTooltipHoverChange;
        }

        private void OnCopyNamesTooltipHoverChange(bool enter)
        {
            try
            {
                if (enter)
                {
                    tboxCopyNamesTooltipHovered = true;

                    if (temporaryStatusCoroutine != null)
                    {
                        StopCoroutine(temporaryStatusCoroutine);
                        temporaryStatusCoroutine = null;
                    }

                    if (tboxCopyNamesTooltipCo != null)
                    {
                        StopCoroutine(tboxCopyNamesTooltipCo);
                        tboxCopyNamesTooltipCo = null;
                    }

                    // Set immediately, then keep it responsive to Ctrl state while hovered.
                    tboxCopyNamesTooltipLast = GetCopyNamesTooltipText(IsCtrlHeld());
                    temporaryStatusMsg = tboxCopyNamesTooltipLast;
                    temporaryStatusOwner = tboxCopyPkgNamesBtn;
                    tboxCopyNamesTooltipCo = StartCoroutine(CopyNamesTooltipCoroutine());
                }
                else
                {
                    tboxCopyNamesTooltipHovered = false;
                    if (tboxCopyNamesTooltipCo != null)
                    {
                        StopCoroutine(tboxCopyNamesTooltipCo);
                        tboxCopyNamesTooltipCo = null;
                    }

                    // Only clear if we still own the tooltip text.
                    if (!string.IsNullOrEmpty(tboxCopyNamesTooltipLast) && temporaryStatusMsg == tboxCopyNamesTooltipLast)
                        temporaryStatusMsg = null;
                    if (temporaryStatusOwner == tboxCopyPkgNamesBtn) temporaryStatusOwner = null;
                    tboxCopyNamesTooltipLast = null;
                }
            }
            catch { }
        }

        private IEnumerator CopyNamesTooltipCoroutine()
        {
            // Update at a low rate; this is just for modifier-key responsiveness.
            var wait = new WaitForSecondsRealtime(0.05f);
            while (tboxCopyNamesTooltipHovered)
            {
                string msg = null;
                try { msg = GetCopyNamesTooltipText(IsCtrlHeld()); } catch { msg = null; }

                if (!string.IsNullOrEmpty(msg) && msg != tboxCopyNamesTooltipLast)
                {
                    // Only replace if we still own the tooltip slot.
                    if (temporaryStatusMsg == tboxCopyNamesTooltipLast || string.IsNullOrEmpty(temporaryStatusMsg))
                    {
                        temporaryStatusMsg = msg;
                        temporaryStatusOwner = tboxCopyPkgNamesBtn;
                        tboxCopyNamesTooltipLast = msg;
                    }
                    else
                    {
                        // Another tooltip/status took over; stop updating.
                        break;
                    }
                }

                yield return wait;
            }
            tboxCopyNamesTooltipCo = null;
        }

        private void CopySelectedPackageNamesToClipboard()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                bool fullPaths = IsCtrlHeld();

                var uids = CollectUniquePackageUidsFromSelection(selectedFiles);
                var list = new List<string>(uids.Count + 32);

                // Packages
                foreach (var uid in uids)
                {
                    if (string.IsNullOrEmpty(uid)) continue;
                    if (!fullPaths)
                    {
                        list.Add(uid + ".var");
                        continue;
                    }

                    string p = null;
                    try { p = ResolveVarPathForUid(uid); } catch { p = null; }
                    if (!string.IsNullOrEmpty(p))
                    {
                        try { p = FileManager.GetFullPath(p); } catch { }
                    }
                    list.Add(!string.IsNullOrEmpty(p) ? p : (uid + ".var"));
                }

                // Local scenes (Saves/scene/*.json)
                if (selectedFiles != null)
                {
                    for (int i = 0; i < selectedFiles.Count; i++)
                    {
                        var f = selectedFiles[i];
                        if (f == null) continue;
                        if (!LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out string abs, out string rel, false))
                            continue;

                        string add = fullPaths ? abs : rel;
                        if (!string.IsNullOrEmpty(add))
                            list.Add(add.Replace('\\', '/'));
                    }
                }

                if (list.Count == 0)
                {
                    ShowTemporaryStatus("No package or local scene paths in selection.");
                    return;
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);

                string text = string.Join("\n", list.ToArray());

                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus(fullPaths
                    ? $"Copied {list.Count} full path(s) to clipboard."
                    : $"Copied {list.Count} name(s) to clipboard.", 2f);
                TryPulseTboxCopyNamesIcon();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] CopySelectedPackageNamesToClipboard error: " + ex.Message);
                ShowTemporaryStatus("Copy failed. See log.", 2f);
            }
        }

        private void TryPulseTboxCopyNamesIcon()
        {
            try
            {
                if (tboxCopyNamesIconImage == null || tboxClipboardListSprite == null || tboxClipboardCheckSprite == null)
                    return;

                if (tboxCopyNamesIconPulseCo != null)
                {
                    StopCoroutine(tboxCopyNamesIconPulseCo);
                    tboxCopyNamesIconPulseCo = null;
                }
                tboxCopyNamesIconPulseCo = StartCoroutine(PulseCopyNamesIconCoroutine());
            }
            catch { }
        }

        private IEnumerator PulseCopyNamesIconCoroutine()
        {
            Sprite prevSprite = null;
            Color prevColor = Color.white;
            try
            {
                if (tboxCopyNamesIconImage == null) yield break;
                prevSprite = tboxCopyNamesIconImage.sprite;
                prevColor = tboxCopyNamesIconImage.color;

                tboxCopyNamesIconImage.sprite = tboxClipboardCheckSprite;
                tboxCopyNamesIconImage.color = new Color(0.25f, 0.85f, 0.35f, 1f);

                yield return new WaitForSecondsRealtime(0.8f);
            }
            finally
            {
                try
                {
                    if (tboxCopyNamesIconImage != null)
                    {
                        tboxCopyNamesIconImage.sprite = tboxClipboardListSprite;
                        tboxCopyNamesIconImage.color = Color.white;
                    }
                }
                catch { }
                tboxCopyNamesIconPulseCo = null;
            }
        }

        private static string TryGetPackageUidForEntry(FileEntry f)
        {
            if (f is VarFileEntry vfe && vfe.Package != null && !string.IsNullOrEmpty(vfe.Package.Uid))
                return vfe.Package.Uid;

            if (f is PackageListEntry ple && ple.Package != null && !string.IsNullOrEmpty(ple.Package.Uid))
                return ple.Package.Uid;

            if (f is MissingPackageListEntry mp && !string.IsNullOrEmpty(mp.RequestedUid))
                return mp.RequestedUid;

            string p = f.Path ?? "";
            if (string.IsNullOrEmpty(p)) return null;

            int internalSep = p.IndexOf(":/", StringComparison.Ordinal);
            if (internalSep >= 0) p = p.Substring(0, internalSep);

            p = p.Replace('\\', '/');
            if (!p.EndsWith(".var", StringComparison.OrdinalIgnoreCase)) return null;

            int slash = p.LastIndexOf('/');
            string file = (slash >= 0) ? p.Substring(slash + 1) : p;
            if (file.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - 4);

            return string.IsNullOrEmpty(file) ? null : file;
        }
    }
}
