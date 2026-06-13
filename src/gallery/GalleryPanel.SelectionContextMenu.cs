using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using VPB.src.util;

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
        private GameObject tboxCleanupClearBtn;
        private GameObject tboxCleanupAddExcludeBtn;
        private GameObject tboxCleanupRemoveExcludeBtn;
        private GameObject tboxAutoInstallBtn;
        private GameObject tboxDisableAutoInstallBtn;
        private GameObject tboxHideBtn;
        private GameObject tboxUnhideBtn;
        private GameObject tboxScanWhitelistTemporaryBtn;
        private GameObject tboxLoadBtn;
        private GameObject tboxUnloadBtn;
        private GameObject tboxLoadDepsBtn;
        private GameObject tboxCacheTexturesBtn;
        // Debug-only: dumps the selected row's thumbnail pipeline to Cache/VPB/_thumbdebug/. Visible when TextureLogLevel >= 2.
        private GameObject tboxThumbDebugBtn;
        private GameObject tboxOpenHubBtn;
        private GameObject tboxOverwriteSceneBtn;
        private GameObject tboxSuppressScaleBtn;
        private GameObject tboxSelectAllBtn;
        private GameObject tboxClearSelectionBtn;
        private GameObject tboxGridRateBtn;
        private Image tboxGridRateIconImage;
        private RatingHandler tboxGridRateHandler;
        private GameObject tboxGridRateSelectorGO;
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

        // Responsive tbox action buttons: 1–3 rows, flexible widths
        private GameObject tboxButtonsFlexRoot;
        private RectTransform tboxButtonsFlexRootRT;
        private GameObject tboxBtnRow0GO;
        private GameObject tboxBtnRow1GO;
        private GameObject tboxBtnRow2GO;
        private RectTransform tboxBtnRow0RT;
        private RectTransform tboxBtnRow1RT;
        private RectTransform tboxBtnRow2RT;
        private LayoutElement tboxBtnRow0LE;
        private LayoutElement tboxBtnRow1LE;
        private LayoutElement tboxBtnRow2LE;
        private HorizontalLayoutGroup tboxBtnRow0HLG;
        private HorizontalLayoutGroup tboxBtnRow1HLG;
        private HorizontalLayoutGroup tboxBtnRow2HLG;
        private GameObject tboxButtonStash;
        private int tboxButtonLayoutRows = 1;
        private float tboxLastFlexAvailW = -1f;
        private struct TboxBaseWidthSpec
        {
            public float minW;
            public float prefW;
            public float flexW;
        }
        private readonly Dictionary<GameObject, TboxBaseWidthSpec> tboxBaseWidthSpec = new Dictionary<GameObject, TboxBaseWidthSpec>(64);
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
            // Fixed button height (centered in the row) — do not stretch to fill the info row.
            le.flexibleHeight = 0f;
        }

        /// <summary>
        /// Uniform toolbox action-button height: scales the design ref proportionally to the
        /// info-row height so it tracks the same chrome scale as side-rail buttons / title chips,
        /// instead of nearly filling the info row (which made the buttons look oversized).
        /// </summary>
        private float TboxActionButtonInnerHeight()
        {
            float rowH = tboxInfoRowHeight > 0f ? tboxInfoRowHeight : GalleryUiDesignTokens.FooterInfoRowHeightRef;
            float h = GalleryUiDesignTokens.TboxActionButtonSizeRef
                      * (rowH / GalleryUiDesignTokens.FooterInfoRowHeightRef);
            float maxH = Mathf.Max(34f, rowH - 8f);
            return Mathf.Clamp(h, 28f, maxH);
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
            one(tboxAutoInstallBtn);
            one(tboxDeleteBtn);
            one(tboxRemoveHistoryBtn);
            one(tboxCleanupBtn);
            one(tboxCleanupApplyBtn);
            one(tboxCleanupClearBtn);
            one(tboxCleanupAddExcludeBtn);
            one(tboxCleanupRemoveExcludeBtn);
            one(tboxLoadBtn);
            one(tboxUnloadBtn);
            one(tboxLoadDepsBtn);
            one(tboxCacheTexturesBtn);
            one(tboxOpenHubBtn);
            one(tboxCopyPkgNamesBtn);
            one(tboxOverwriteSceneBtn);
            one(tboxSuppressScaleBtn);
            one(tboxSelectAllBtn);
            one(tboxClearSelectionBtn);
            one(tboxGridRateBtn);
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
            d(tboxAutoInstallBtn);
            d(tboxDeleteBtn);
            d(tboxRemoveHistoryBtn);
            d(tboxCleanupBtn);
            d(tboxCleanupApplyBtn);
            d(tboxCleanupClearBtn);
            d(tboxCleanupAddExcludeBtn);
            d(tboxCleanupRemoveExcludeBtn);
            d(tboxLoadBtn);
            d(tboxUnloadBtn);
            d(tboxLoadDepsBtn);
            d(tboxCacheTexturesBtn);
            d(tboxOpenHubBtn);
            d(tboxCopyPkgNamesBtn);
            d(tboxOverwriteSceneBtn);
            d(tboxSuppressScaleBtn);
            d(tboxSelectAllBtn);
            d(tboxClearSelectionBtn);
            d(tboxGridRateBtn);
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

        /// <summary>Wrap tbox actions to up to three rows when widths no longer fit; stretch button band height.</summary>
        private void RefreshTboxFlexButtonLayout()
        {
            if (tboxButtonsFlexRootRT == null || tboxBtnRow0HLG == null || tboxBtnRow1HLG == null || tboxBtnRow2HLG == null) return;

            // Rows fill the full info-row slot (band has no gap); buttons are centered at the
            // smaller uniform height so they match the side-rail / title-chip family.
            float rowSlotH = tboxInfoRowHeight > 0f ? tboxInfoRowHeight : GalleryUiDesignTokens.FooterInfoRowHeightRef;
            float innerH = TboxActionButtonInnerHeight();
            if (tboxBtnRow0LE != null) { tboxBtnRow0LE.minHeight = rowSlotH; tboxBtnRow0LE.preferredHeight = rowSlotH; }
            if (tboxBtnRow1LE != null) { tboxBtnRow1LE.minHeight = rowSlotH; tboxBtnRow1LE.preferredHeight = rowSlotH; }
            if (tboxBtnRow2LE != null) { tboxBtnRow2LE.minHeight = rowSlotH; tboxBtnRow2LE.preferredHeight = rowSlotH; }
            TboxSetAllFlexActionButtonHeights(innerH);

            Canvas.ForceUpdateCanvases();
            float avail = tboxButtonsFlexRootRT.rect.width;
            if (avail < 8f)
                avail = (tboxLastFlexAvailW > 8f) ? tboxLastFlexAvailW : 640f;

            const float gap = 10f;

            bool TryGetBaseWidths(GameObject go, out float minW, out float prefW)
            {
                minW = 56f;
                prefW = 100f;
                if (go == null) return false;
                var le = go.GetComponent<LayoutElement>();
                if (le == null) return false;
                if (!tboxBaseWidthSpec.TryGetValue(go, out var spec))
                {
                    spec.minW = le.minWidth;
                    spec.prefW = le.preferredWidth;
                    spec.flexW = le.flexibleWidth;
                    tboxBaseWidthSpec[go] = spec;
                }
                minW = spec.minW;
                prefW = spec.prefW;
                return true;
            }

            int Weight(GameObject go)
            {
                // "Target selector" counts as 4 button slots for balanced wrap + width allocation.
                if (go != null && tboxTargetDropdownRowGO != null && go == tboxTargetDropdownRowGO) return 4;
                return 1;
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
                if (vis(tboxAutoInstallBtn)) ltr.Add(tboxAutoInstallBtn);
                if (vis(tboxUnhideBtn)) ltr.Add(tboxUnhideBtn);
                if (vis(tboxHideBtn)) ltr.Add(tboxHideBtn);
                if (vis(tboxScanWhitelistTemporaryBtn)) ltr.Add(tboxScanWhitelistTemporaryBtn);
                if (vis(tboxDeleteBtn)) ltr.Add(tboxDeleteBtn);
                if (vis(tboxRemoveHistoryBtn)) ltr.Add(tboxRemoveHistoryBtn);
                if (vis(tboxCleanupBtn)) ltr.Add(tboxCleanupBtn);
                if (vis(tboxCleanupApplyBtn)) ltr.Add(tboxCleanupApplyBtn);
                if (vis(tboxCleanupClearBtn)) ltr.Add(tboxCleanupClearBtn);
                if (vis(tboxCleanupAddExcludeBtn)) ltr.Add(tboxCleanupAddExcludeBtn);
                if (vis(tboxCleanupRemoveExcludeBtn)) ltr.Add(tboxCleanupRemoveExcludeBtn);
                if (vis(tboxLoadBtn)) ltr.Add(tboxLoadBtn);
                if (vis(tboxUnloadBtn)) ltr.Add(tboxUnloadBtn);
                if (vis(tboxLoadDepsBtn)) ltr.Add(tboxLoadDepsBtn);
                if (vis(tboxCacheTexturesBtn)) ltr.Add(tboxCacheTexturesBtn);
                if (vis(tboxOpenHubBtn)) ltr.Add(tboxOpenHubBtn);
                if (vis(tboxCopyPkgNamesBtn)) ltr.Add(tboxCopyPkgNamesBtn);
                if (vis(tboxGridRateBtn)) ltr.Add(tboxGridRateBtn);
                if (vis(tboxOverwriteSceneBtn)) ltr.Add(tboxOverwriteSceneBtn);
                if (vis(tboxSuppressScaleBtn)) ltr.Add(tboxSuppressScaleBtn);
                if (vis(tboxSelectAllBtn)) ltr.Add(tboxSelectAllBtn);
                if (vis(tboxClearSelectionBtn)) ltr.Add(tboxClearSelectionBtn);
            }

            // Prefer target selector on last row (acts like "wide control").
            if (!IsSettingsPanelOpen() && tboxTargetDropdownRowGO != null && ltr.Contains(tboxTargetDropdownRowGO) && ltr.Count > 1)
            {
                ltr.Remove(tboxTargetDropdownRowGO);
                ltr.Add(tboxTargetDropdownRowGO);
            }

            var rtl = new List<GameObject>(ltr.Count);
            for (int i = ltr.Count - 1; i >= 0; i--)
                rtl.Add(ltr[i]);

            // Restore base widths before measuring/wrapping. Equal-width pass overwrites preferredWidth.
            for (int i = 0; i < ltr.Count; i++)
            {
                var go = ltr[i];
                if (go == null) continue;
                var le = go.GetComponent<LayoutElement>();
                if (le == null) continue;
                if (!tboxBaseWidthSpec.TryGetValue(go, out var spec))
                {
                    spec.minW = le.minWidth;
                    spec.prefW = le.preferredWidth;
                    spec.flexW = le.flexibleWidth;
                    tboxBaseWidthSpec[go] = spec;
                }
                else
                {
                    le.preferredWidth = spec.prefW;
                    le.flexibleWidth = spec.flexW;
                }
            }

            List<GameObject> row0rtl = new List<GameObject>();
            List<GameObject> row1rtl = new List<GameObject>();
            List<GameObject> row2rtl = new List<GameObject>();

            void ApplySlotWidths(List<GameObject> row, int slotCount)
            {
                if (row == null || row.Count == 0) return;
                if (slotCount <= 0) return;
                float unitW = (avail - gap * (slotCount - 1)) / slotCount;
                for (int i = 0; i < row.Count; i++)
                {
                    GameObject go = row[i];
                    if (go == null) continue;
                    var le = go.GetComponent<LayoutElement>();
                    if (le == null) continue;
                    if (!TryGetBaseWidths(go, out float minW, out _)) continue;

                    int wSlots = Weight(go);
                    float w = unitW * wSlots + gap * (wSlots - 1);
                    w = Mathf.Max(minW, w);
                    le.preferredWidth = w;
                    le.flexibleWidth = 0f;
                }
            }

            // Weighted slot packing.
            // Goal: use available width, split slots roughly evenly across rows, with special wide controls (Target) using multiple slots.
            int totalSlots = 0;
            float unitMinW = 0f;
            for (int i = 0; i < rtl.Count; i++)
            {
                var go = rtl[i];
                if (go == null) continue;
                int w = Weight(go);
                totalSlots += w;
                if (TryGetBaseWidths(go, out float mw, out _))
                    unitMinW = Mathf.Max(unitMinW, mw / Mathf.Max(1, w));
            }
            if (unitMinW < 8f) unitMinW = 56f;
            int maxSlotsPerRow = Mathf.Max(1, Mathf.FloorToInt((avail + gap) / (unitMinW + gap)));
            int rowsNeed = Mathf.CeilToInt(totalSlots / Mathf.Max(1f, (float)maxSlotsPerRow));
            if (rowsNeed < 1) rowsNeed = 1;
            if (rowsNeed > 3) rowsNeed = 3;

            int row0SlotsTarget = totalSlots;
            int row1SlotsTarget = 0;
            int row2SlotsTarget = 0;
            if (rowsNeed == 2)
            {
                row0SlotsTarget = Mathf.Min(maxSlotsPerRow, Mathf.CeilToInt(totalSlots / 2f));
                row1SlotsTarget = totalSlots - row0SlotsTarget;
            }
            else if (rowsNeed == 3)
            {
                row0SlotsTarget = Mathf.Min(maxSlotsPerRow, Mathf.CeilToInt(totalSlots / 3f));
                int rem = totalSlots - row0SlotsTarget;
                row1SlotsTarget = Mathf.Min(maxSlotsPerRow, Mathf.CeilToInt(rem / 2f));
                row2SlotsTarget = totalSlots - row0SlotsTarget - row1SlotsTarget;
            }

            int curRow = 0;
            int usedSlots0 = 0, usedSlots1 = 0, usedSlots2 = 0;
            for (int i = 0; i < rtl.Count; i++)
            {
                var go = rtl[i];
                if (go == null) continue;
                int w = Weight(go);
                if (rowsNeed == 1)
                {
                    row0rtl.Add(go);
                    usedSlots0 += w;
                    continue;
                }

                if (curRow == 0)
                {
                    if (usedSlots0 + w <= row0SlotsTarget || row0rtl.Count == 0)
                    {
                        row0rtl.Add(go);
                        usedSlots0 += w;
                        continue;
                    }
                    curRow = 1;
                }
                if (curRow == 1)
                {
                    if (rowsNeed == 2)
                    {
                        row1rtl.Add(go);
                        usedSlots1 += w;
                        continue;
                    }
                    if (usedSlots1 + w <= row1SlotsTarget || row1rtl.Count == 0)
                    {
                        row1rtl.Add(go);
                        usedSlots1 += w;
                        continue;
                    }
                    curRow = 2;
                }
                row2rtl.Add(go);
                usedSlots2 += w;
            }

            tboxButtonLayoutRows = 1;
            if (row1rtl.Count > 0) tboxButtonLayoutRows = 2;
            if (row2rtl.Count > 0) tboxButtonLayoutRows = 3;

            // Apply widths per row based on slot counts actually used in row.
            if (tboxButtonLayoutRows == 1)
                ApplySlotWidths(ltr, Mathf.Max(1, usedSlots0));
            else if (tboxButtonLayoutRows == 2)
            {
                ApplySlotWidths(row0rtl, Mathf.Max(1, usedSlots0));
                ApplySlotWidths(row1rtl, Mathf.Max(1, usedSlots1));
            }
            else
            {
                ApplySlotWidths(row0rtl, Mathf.Max(1, usedSlots0));
                ApplySlotWidths(row1rtl, Mathf.Max(1, usedSlots1));
                ApplySlotWidths(row2rtl, Mathf.Max(1, usedSlots2));
            }

            TboxDetachAllActionButtonsForLayout();
            if (tboxButtonLayoutRows == 1)
            {
                TboxPopulateRowLtr(tboxBtnRow0HLG, ltr);
                tboxBtnRow1GO.SetActive(false);
                if (tboxBtnRow2GO != null) tboxBtnRow2GO.SetActive(false);
            }
            else if (tboxButtonLayoutRows == 2)
            {
                tboxBtnRow1GO.SetActive(true);
                if (tboxBtnRow2GO != null) tboxBtnRow2GO.SetActive(false);
                TboxPopulateRowFromRtlPack(tboxBtnRow0HLG, row0rtl);
                TboxPopulateRowFromRtlPack(tboxBtnRow1HLG, row1rtl);
            }
            else
            {
                tboxBtnRow1GO.SetActive(true);
                if (tboxBtnRow2GO != null) tboxBtnRow2GO.SetActive(true);
                TboxPopulateRowFromRtlPack(tboxBtnRow0HLG, row0rtl);
                TboxPopulateRowFromRtlPack(tboxBtnRow1HLG, row1rtl);
                TboxPopulateRowFromRtlPack(tboxBtnRow2HLG, row2rtl);
            }

            float band = tboxInfoRowHeight * tboxButtonLayoutRows + (tboxButtonLayoutRows > 1 ? (tboxBtnRowGap * (tboxButtonLayoutRows - 1)) : 0f);
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

            const int tboxCollapsedFont = GalleryUiDesignTokens.FontBodyRef;

            var labelTextGO = new GameObject("Text");
            labelTextGO.transform.SetParent(rowGO.transform, false);
            tboxLabel = labelTextGO.AddComponent<Text>();
            tboxLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxLabel.fontSize = tboxCollapsedFont;
            tboxLabel.fontStyle = FontStyle.Normal;
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

            float innerRowH = TboxActionButtonInnerHeight();

            tboxBtnRow0GO = new GameObject("TboxBtnRow0");
            tboxBtnRow0GO.transform.SetParent(flexGO.transform, false);
            tboxBtnRow0RT = tboxBtnRow0GO.AddComponent<RectTransform>();
            tboxBtnRow0RT.anchorMin = Vector2.zero;
            tboxBtnRow0RT.anchorMax = Vector2.one;
            tboxBtnRow0RT.sizeDelta = Vector2.zero;
            tboxBtnRow0LE = tboxBtnRow0GO.AddComponent<LayoutElement>();
            tboxBtnRow0LE.minHeight = tboxInfoRowHeight;
            tboxBtnRow0LE.preferredHeight = tboxInfoRowHeight;
            tboxBtnRow0LE.flexibleWidth = 1f;
            tboxBtnRow0HLG = tboxBtnRow0GO.AddComponent<HorizontalLayoutGroup>();
            tboxBtnRow0HLG.spacing = 10f;
            tboxBtnRow0HLG.padding = new RectOffset(0, 0, 0, 0);
            tboxBtnRow0HLG.childAlignment = TextAnchor.MiddleRight;
            tboxBtnRow0HLG.childControlWidth = true;
            tboxBtnRow0HLG.childForceExpandWidth = false;
            tboxBtnRow0HLG.childControlHeight = true;
            tboxBtnRow0HLG.childForceExpandHeight = false;

            tboxBtnRow1GO = new GameObject("TboxBtnRow1");
            tboxBtnRow1GO.transform.SetParent(flexGO.transform, false);
            tboxBtnRow1RT = tboxBtnRow1GO.AddComponent<RectTransform>();
            tboxBtnRow1RT.anchorMin = Vector2.zero;
            tboxBtnRow1RT.anchorMax = Vector2.one;
            tboxBtnRow1RT.sizeDelta = Vector2.zero;
            tboxBtnRow1LE = tboxBtnRow1GO.AddComponent<LayoutElement>();
            tboxBtnRow1LE.minHeight = tboxInfoRowHeight;
            tboxBtnRow1LE.preferredHeight = tboxInfoRowHeight;
            tboxBtnRow1LE.flexibleWidth = 1f;
            tboxBtnRow1HLG = tboxBtnRow1GO.AddComponent<HorizontalLayoutGroup>();
            tboxBtnRow1HLG.spacing = 10f;
            tboxBtnRow1HLG.padding = new RectOffset(0, 0, 0, 0);
            tboxBtnRow1HLG.childAlignment = TextAnchor.MiddleRight;
            tboxBtnRow1HLG.childControlWidth = true;
            tboxBtnRow1HLG.childForceExpandWidth = false;
            tboxBtnRow1HLG.childControlHeight = true;
            tboxBtnRow1HLG.childForceExpandHeight = false;
            tboxBtnRow1GO.SetActive(false);

            tboxBtnRow2GO = new GameObject("TboxBtnRow2");
            tboxBtnRow2GO.transform.SetParent(flexGO.transform, false);
            tboxBtnRow2RT = tboxBtnRow2GO.AddComponent<RectTransform>();
            tboxBtnRow2RT.anchorMin = Vector2.zero;
            tboxBtnRow2RT.anchorMax = Vector2.one;
            tboxBtnRow2RT.sizeDelta = Vector2.zero;
            tboxBtnRow2LE = tboxBtnRow2GO.AddComponent<LayoutElement>();
            tboxBtnRow2LE.minHeight = tboxInfoRowHeight;
            tboxBtnRow2LE.preferredHeight = tboxInfoRowHeight;
            tboxBtnRow2LE.flexibleWidth = 1f;
            tboxBtnRow2HLG = tboxBtnRow2GO.AddComponent<HorizontalLayoutGroup>();
            tboxBtnRow2HLG.spacing = 10f;
            tboxBtnRow2HLG.padding = new RectOffset(0, 0, 0, 0);
            tboxBtnRow2HLG.childAlignment = TextAnchor.MiddleRight;
            tboxBtnRow2HLG.childControlWidth = true;
            tboxBtnRow2HLG.childForceExpandWidth = false;
            tboxBtnRow2HLG.childControlHeight = true;
            tboxBtnRow2HLG.childForceExpandHeight = false;
            tboxBtnRow2GO.SetActive(false);

            // ── Dependency Filter Mode Row ─────────────────────────────────────
            tboxFilterModeRowGO = new GameObject("TboxFilterModeRow");
            tboxFilterModeRowGO.transform.SetParent(flexGO.transform, false);
            tboxFilterModeRowGO.transform.SetAsFirstSibling();
            tboxFilterModeRowRT = tboxFilterModeRowGO.AddComponent<RectTransform>();
            tboxFilterModeRowRT.anchorMin = Vector2.zero;
            tboxFilterModeRowRT.anchorMax = Vector2.one;
            tboxFilterModeRowRT.sizeDelta = Vector2.zero;
            tboxFilterModeRowLE = tboxFilterModeRowGO.AddComponent<LayoutElement>();
            tboxFilterModeRowLE.minHeight = tboxInfoRowHeight;
            tboxFilterModeRowLE.preferredHeight = tboxInfoRowHeight;
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
                tboxFilterModeText.fontSize = GalleryUiDesignTokens.FontBodyRef;
                tboxFilterModeText.fontStyle = FontStyle.Normal;
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

            const int tboxActionBtnFont = GalleryUiDesignTokens.FontBodyRef;

            // Placeholders — layout is resolved in RefreshTboxFlexButtonLayout (stretch + LayoutElement).

            tboxOverwriteSceneBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxOverwriteSaveSceneClicked
            );
            tboxOverwriteSceneBtn.name = "Tbox_OverwriteScene";
            TboxConfigureActionButtonFlex(tboxOverwriteSceneBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(
                tboxOverwriteSceneBtn,
                "gallery.tooltip.overwrite_scene",
                "Overwrite Save: open save dialog with the selected scene name prefilled.");
            try
            {
                var saveSpr = gallerySaveSprite ?? UI.LoadIconSprite("vpb_icons/gallery_save.png", Color.white);
                if (saveSpr != null)
                    UI.AddIconToButton(tboxOverwriteSceneBtn, saveSpr, padding: 6f, backdropOverride: new Color(0.18f, 0.42f, 0.28f, 0.96f));
            }
            catch { }

            tboxSuppressScaleBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                ToggleSuppressAppearanceScale
            );
            tboxSuppressScaleBtn.name = "Tbox_SuppressScale";
            TboxConfigureActionButtonFlex(tboxSuppressScaleBtn, innerRowH, innerRowH, innerRowH);
            AddTooltip(tboxSuppressScaleBtn, "gallery.tooltip.suppress_scale",
                "Keep target's current scale when importing Appearance");
            try
            {
                var s = UI.LoadIconSprite("vpb_icons/arrows_vertical.png", Color.white);
                if (s != null) UI.AddIconToButton(tboxSuppressScaleBtn, s, padding: 6f);
            }
            catch { }
            RefreshSuppressScaleBtnVisual();

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

            // Grid layout: rate selected package(s) in one action (list view keeps per-row ★ control).
            {
                Color gridRateBackdrop = new Color(0.34f, 0.27f, 0.14f, 0.96f);
                tboxGridRateBtn = UI.CreateUIButton(
                    tboxBtnRow0GO, 0, 0,
                    "", tboxActionBtnFont,
                    0, 0, AnchorPresets.stretchAll,
                    null
                );
                tboxGridRateBtn.name = "Tbox_GridRate";
                TboxConfigureActionButtonFlex(tboxGridRateBtn, innerRowH, innerRowH, innerRowH);
                AddTooltip(tboxGridRateBtn, "gallery.tooltip.tbox_grid_rate", "Rate selected packages (0–5 applies to all selected items).");
                try
                {
                    var starSpr = UI.LoadIconSprite("vpb_icons/star.png", Color.white);
                    if (starSpr != null)
                    {
                        UI.AddIconToButton(tboxGridRateBtn, starSpr, padding: 6f, backdropOverride: gridRateBackdrop);
                        tboxGridRateIconImage = tboxGridRateBtn.transform.Find("Icon")?.GetComponent<Image>();
                    }
                }
                catch { }

                tboxGridRateSelectorGO = new GameObject("TboxGridRatingSelector");
                tboxGridRateSelectorGO.transform.SetParent(tboxGridRateBtn.transform, false);
                RectTransform selectorRT = tboxGridRateSelectorGO.AddComponent<RectTransform>();
                // Anchor top-right of star button, pivot bottom-right of panel so the grid opens **upward**
                // into toolbox rows — not downward over gallery (was stealing clicks on bottom grid cells).
                selectorRT.anchorMin = new Vector2(1f, 1f);
                selectorRT.anchorMax = new Vector2(1f, 1f);
                selectorRT.pivot = new Vector2(1f, 0f);
                selectorRT.sizeDelta = new Vector2(80, 114);
                selectorRT.anchoredPosition = new Vector2(-2f, 0f);

                CanvasGroup selectorCg = tboxGridRateSelectorGO.AddComponent<CanvasGroup>();
                selectorCg.alpha = 0f;
                selectorCg.interactable = false;
                selectorCg.blocksRaycasts = false;

                Image selectorBgImg = tboxGridRateSelectorGO.AddComponent<Image>();
                selectorBgImg.color = new Color(0.05f, 0.05f, 0.05f, 0.96f);

                GridLayoutGroup selectorGrid = tboxGridRateSelectorGO.AddComponent<GridLayoutGroup>();
                selectorGrid.cellSize = new Vector2(38, 36);
                selectorGrid.spacing = new Vector2(2, 2);
                selectorGrid.padding = new RectOffset(1, 1, 1, 1);
                selectorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                selectorGrid.constraintCount = 2;
                selectorGrid.childAlignment = TextAnchor.UpperLeft;

                tboxGridRateHandler = tboxGridRateBtn.AddComponent<RatingHandler>();
                Image[] grOptImages = new Image[6];
                Text[] grOptTexts = new Text[6];
                GameObject[] grOptBorders = new GameObject[6];
                for (int ri = 0; ri <= 5; ri++)
                {
                    int ratingValue = ri;
                    string label = ri == 0 ? "X" : ri.ToString();
                    GameObject optBtnGO = UI.CreateUIButton(tboxGridRateSelectorGO, 38, 36, label, 22, 0, 0, AnchorPresets.middleCenter,
                        () => TboxApplyGridRatingToSelection(ratingValue));
                    optBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
                    grOptImages[ri] = optBtnGO.GetComponent<Image>();
                    grOptImages[ri].color = RatingHandler.RatingColors[ri];
                    grOptTexts[ri] = optBtnGO.GetComponentInChildren<Text>();
                    grOptTexts[ri].color = ri == 0 ? Color.red : Color.black;

                    GameObject borderGO = new GameObject("SelectionBorder");
                    borderGO.transform.SetParent(optBtnGO.transform, false);
                    borderGO.transform.SetSiblingIndex(0);
                    RectTransform borderRT = borderGO.AddComponent<RectTransform>();
                    borderRT.anchorMin = Vector2.zero;
                    borderRT.anchorMax = Vector2.one;
                    borderRT.offsetMin = Vector2.zero;
                    borderRT.offsetMax = Vector2.zero;
                    AddBorderEdge(borderGO, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, 3));
                    AddBorderEdge(borderGO, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 3));
                    AddBorderEdge(borderGO, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(3, 0));
                    AddBorderEdge(borderGO, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(3, 0));
                    borderGO.SetActive(false);
                    grOptBorders[ri] = borderGO;
                }
                tboxGridRateHandler.SetOptionRefs(grOptImages, grOptTexts, grOptBorders);

                Button gridRateMainBtn = tboxGridRateBtn.GetComponent<Button>();
                gridRateMainBtn.onClick.AddListener(() =>
                {
                    if (tboxGridRateHandler != null) tboxGridRateHandler.ToggleSelector();
                });

                tboxGridRateBtn.SetActive(false);
            }

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

            tboxThumbDebugBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "DBG", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxDumpThumbnailDebugForSelection
            );
            tboxThumbDebugBtn.name = "Tbox_ThumbDebug";
            TboxConfigureActionButtonFlex(tboxThumbDebugBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxThumbDebugBtn, "gallery.tooltip.tbox_thumb_debug", "Debug: dump thumbnail pipeline for the first selected file to Cache/VPB/_thumbdebug/ (cache bytes, source bytes, Unity + TurboJPEG decodes, displayed texture).");
            try
            {
                Text t = tboxThumbDebugBtn.GetComponentInChildren<Text>(true);
                if (t != null) { t.text = "DBG"; t.color = new Color(1f, 0.6f, 0.2f, 1f); }
                Image bg = tboxThumbDebugBtn.GetComponent<Image>();
                if (bg != null) bg.color = new Color(0.25f, 0.15f, 0.10f, 1f);
            }
            catch { }
            tboxThumbDebugBtn.SetActive(false); // gated by IsTextureLogVerbose() in RefreshTboxConditionalActionButtons

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
            pinRT.sizeDelta = new Vector2(GalleryUiDesignTokens.TboxPinBtnSizeRef, GalleryUiDesignTokens.TboxPinBtnSizeRef);

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
                    if (rsRT != null) rsRT.anchoredPosition = new Vector2(0f, GalleryUiDesignTokens.FooterInfoRowHeightRef * s);
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
                    float rowH = GalleryUiDesignTokens.FooterInfoRowHeightRef * s;
                    float pinW = GalleryUiDesignTokens.TboxPinBtnSizeRef * s;
                    tboxInfoRowHeight = rowH;
                    if (lRT != null) lRT.sizeDelta = new Vector2(-pinW, rowH);
                    if (bRT != null) bRT.anchoredPosition = new Vector2(0f, rowH);
                    if (pRT != null)
                    {
                        pRT.sizeDelta = new Vector2(GalleryUiDesignTokens.TboxPinBtnSizeRef * s, GalleryUiDesignTokens.TboxPinBtnSizeRef * s);
                        pRT.anchoredPosition = new Vector2(0f, rowH * 0.5f);
                    }
                    if (fLE != null) { fLE.minHeight = rowH; fLE.preferredHeight = rowH; }
                    try { RescaleFooterInfoBarInternal(s); } catch { }
                    try { TboxSetAllFlexActionButtonHeights(TboxActionButtonInnerHeight()); } catch { }
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

        private string GetTargetAtomDisplayLabel(Atom atom, string uid)
        {
            if (IsSubSceneTargetMode()) return GetSubSceneAtomDisplayLabel(atom, uid);
            return GetPersonAtomDisplayLabel(atom, uid);
        }

        private string GetSubSceneAtomDisplayLabel(Atom atom, string uid)
        {
            if (atom == null) return uid ?? "Unknown";
            try
            {
                SubScene subScene = atom.GetComponentInChildren<SubScene>();
                if (subScene != null)
                {
                    MethodInfo getStorePathMethod = typeof(SubScene).GetMethod(
                        "GetStorePath",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (getStorePathMethod != null)
                    {
                        object ret = getStorePathMethod.Invoke(subScene, new object[] { true });
                        string path = ret as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            string name = MVR.FileManagementSecure.FileManagerSecure.GetFileName(path);
                            if (!string.IsNullOrEmpty(name))
                                return $"{name} ({uid})";
                        }
                    }
                }
            }
            catch { }
            return uid ?? "Unknown";
        }

        private string GetPersonAtomDisplayLabel(Atom atom, string uid)
        {
            string gender = "";
            try
            {
                if (atom != null && string.Equals(atom.type, "Person", StringComparison.Ordinal))
                {
                    if (AtomGenderUtils.IsFuta(atom)) gender = "[Futa] ";
                    else if (AtomGenderUtils.IsMale(atom)) gender = "[Male] ";
                    else if (AtomGenderUtils.IsFemale(atom)) gender = "[Female] ";
                }
            }
            catch { gender = ""; }

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
                            return $"{gender}{presetName} ({uid})";
                    }
                }
            }
            catch { }
            return $"{gender}{uid}";
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
                try { CloseTboxTargetMenu(); } catch { }
                try { RefreshTboxFlexButtonLayout(); } catch { }
                return;
            }

            float innerRowH   = TboxActionButtonInnerHeight();
            Transform stash      = tboxButtonStash.transform;
            Color inactiveColor  = new Color(0.18f, 0.18f, 0.20f, 1f);

            // Single dropup button row (active person label). Click opens list of all people.
            tboxTargetDropdownRowGO = new GameObject("TboxTargetDropdownRow");
            tboxTargetDropdownRowGO.transform.SetParent(stash, false);
            var rowRT = tboxTargetDropdownRowGO.AddComponent<RectTransform>();
            rowRT.anchorMin = Vector2.zero; rowRT.anchorMax = Vector2.one;
            rowRT.pivot = new Vector2(0.5f, 0.5f);
            rowRT.offsetMin = rowRT.offsetMax = Vector2.zero;
            var rowHLG = tboxTargetDropdownRowGO.AddComponent<HorizontalLayoutGroup>();
            rowHLG.spacing = 2f; rowHLG.childAlignment = TextAnchor.MiddleLeft;
            rowHLG.childControlWidth = true; rowHLG.childForceExpandWidth = true;
            rowHLG.childControlHeight = true; rowHLG.childForceExpandHeight = true;
            var rowLE = tboxTargetDropdownRowGO.AddComponent<LayoutElement>();
            rowLE.minWidth = 140f;
            rowLE.preferredWidth = 220f;
            rowLE.flexibleWidth = 1f;
            rowLE.minHeight = innerRowH; rowLE.preferredHeight = innerRowH; rowLE.flexibleHeight = 0f;
            tboxPersonAtomBtns.Add(tboxTargetDropdownRowGO);

            float sScale = ChromeScale;
            int dropdownFont = Mathf.RoundToInt(Mathf.Max(18f, 18f * sScale));

            string activeLabel = IsSubSceneTargetMode()
                ? VPBTranslation.T("gallery.tbox.subscene_target", "SubScene")
                : "Target";
            try
            {
                int i = targetDropdownValue;
                Atom a = (i >= 0 && i < personAtoms.Count) ? personAtoms[i] : null;
                string uid = (targetDropdownOptions.Count > i) ? targetDropdownOptions[i] : null;
                if (a != null)
                    activeLabel = GetTargetAtomDisplayLabel(a, uid ?? "Unknown");
            }
            catch { }

            var btn = UI.CreateUIButton(tboxTargetDropdownRowGO, 0, 0, activeLabel + "  ▲", dropdownFont, 0, 0, AnchorPresets.stretchAll,
                () => { try { ToggleTboxTargetMenu(); } catch { } });
            btn.name = "TboxTargetDropdownBtn";
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = inactiveColor;
            var le = btn.GetComponent<LayoutElement>() ?? btn.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.minHeight = innerRowH;
            le.preferredHeight = innerRowH; le.flexibleHeight = 1f;
            tboxTargetDropdownBtnText = btn.GetComponentInChildren<Text>(true);
            if (tboxTargetDropdownBtnText != null) tboxTargetDropdownBtnText.gameObject.SetActive(true);
            AddTooltipPlain(btn, IsSubSceneTargetMode()
                ? VPBTranslation.T("gallery.tbox.subscene_target_select", "Select active SubScene target. Right click: cycle targets")
                : VPBTranslation.T("gallery.tbox.target_select", "Select active person target. Right click: cycle targets"));
            try { AddRightClickDelegate(btn, () => CycleTarget(true)); } catch { }

            try
            {
                Canvas.ForceUpdateCanvases();
                RefreshTboxFlexButtonLayout();
                Canvas.ForceUpdateCanvases();
            }
            catch { }
        }

        private void EnsureTboxTargetMenuBuilt()
        {
            if (tboxTargetMenuRootGO != null || backgroundBoxGO == null) return;

            tboxTargetMenuRootGO = new GameObject("TboxTargetMenu");
            tboxTargetMenuRootGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRT = tboxTargetMenuRootGO.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            GameObject backdropGO = new GameObject("Backdrop");
            backdropGO.transform.SetParent(tboxTargetMenuRootGO.transform, false);
            RectTransform backdropRT = backdropGO.AddComponent<RectTransform>();
            backdropRT.anchorMin = Vector2.zero;
            backdropRT.anchorMax = Vector2.one;
            backdropRT.offsetMin = Vector2.zero;
            backdropRT.offsetMax = Vector2.zero;
            Image backdropImg = backdropGO.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0.001f);
            backdropImg.raycastTarget = true;
            Button backdropBtn = backdropGO.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(CloseTboxTargetMenu);

            tboxTargetMenuPanelGO = new GameObject("Panel");
            tboxTargetMenuPanelGO.transform.SetParent(tboxTargetMenuRootGO.transform, false);
            tboxTargetMenuPanelRT = tboxTargetMenuPanelGO.AddComponent<RectTransform>();
            tboxTargetMenuPanelRT.anchorMin = tboxTargetMenuPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
            tboxTargetMenuPanelRT.pivot = new Vector2(0f, 0f);
            tboxTargetMenuPanelRT.anchoredPosition = Vector2.zero;
            tboxTargetMenuPanelRT.sizeDelta = new Vector2(380f, 50f);

            Image panelImg = tboxTargetMenuPanelGO.AddComponent<Image>();
            panelImg.color = new Color(0.09f, 0.09f, 0.16f, 0.97f);
            var outline = tboxTargetMenuPanelGO.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
            outline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup vlg = tboxTargetMenuPanelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 4;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.childAlignment = TextAnchor.UpperCenter;

            ContentSizeFitter csf = tboxTargetMenuPanelGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            tboxTargetMenuRootGO.SetActive(false);
            tboxTargetMenuOpen = false;
        }

        private void RebuildTboxTargetMenuOptions()
        {
            if (tboxTargetMenuPanelGO == null) return;

            Transform panel = tboxTargetMenuPanelGO.transform;
            for (int i = panel.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(panel.GetChild(i).gameObject);

            float sScale      = ChromeScale;
            Sprite renameSpr  = UI.LoadIconSprite("vpb_icons/rename.png", new Color(0.78f, 0.78f, 0.78f, 1f));
            Sprite saveSpr    = gallerySaveSprite;
            Color renameBackdrop = new Color(0.35f, 0.35f, 0.42f, 1f);
            Color saveBackdrop   = new Color(0.20f, 0.35f, 0.22f, 1f);
            float rowH = Mathf.Max(44f, 44f * sScale);
            int labelFont = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, sScale, GalleryUiDesignTokens.FontMinRef);

            int count = personAtoms != null ? personAtoms.Count : 0;
            for (int i = 0; i < count; i++)
            {
                Atom atom = null;
                try { atom = personAtoms[i]; } catch { atom = null; }
                if (atom == null) continue;

                string uid = null;
                try { uid = (targetDropdownOptions.Count > i) ? targetDropdownOptions[i] : null; } catch { uid = null; }
                string label = GetTargetAtomDisplayLabel(atom, uid ?? "Unknown");
                bool isCurrent = (i == targetDropdownValue);
                int captured = i;

                // Row container: select button + (optional) save + rename icons
                GameObject rowGO = new GameObject("TboxTargetMenuRow_" + i);
                rowGO.transform.SetParent(tboxTargetMenuPanelGO.transform, false);
                var rowRT = rowGO.AddComponent<RectTransform>();
                rowRT.anchorMin = new Vector2(0f, 1f);
                rowRT.anchorMax = new Vector2(0f, 1f);
                rowRT.pivot = new Vector2(0f, 1f);
                rowRT.sizeDelta = new Vector2(0f, rowH);

                var rowImg = rowGO.AddComponent<Image>();
                rowImg.color = isCurrent ? new Color(0.15f, 0.30f, 0.52f, 1f) : new Color(0.16f, 0.16f, 0.24f, 1f);
                rowImg.raycastTarget = false; // children handle clicks

                var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
                rowHLG.padding = new RectOffset(4, 4, 2, 2);
                rowHLG.spacing = 4f;
                rowHLG.childControlHeight = true;
                rowHLG.childControlWidth = true;
                rowHLG.childForceExpandHeight = true;
                rowHLG.childForceExpandWidth = false;
                rowHLG.childAlignment = TextAnchor.MiddleLeft;

                var rowLE = rowGO.AddComponent<LayoutElement>();
                rowLE.preferredHeight = rowH;
                rowLE.flexibleWidth = 1f;

                string rowLabel = (isCurrent ? "\u2713  " : "    ") + label;
                GameObject selectBtn = UI.CreateUIButton(
                    rowGO, 0, 0, rowLabel, labelFont, 0, 0,
                    AnchorPresets.stretchAll,
                    () =>
                    {
                        bool changed = targetDropdownValue != captured;
                        targetDropdownValue = captured;
                        UpdateTargetDropdownUI();
                        CloseTboxTargetMenu();
                        if (changed) OnTargetAtomChanged("dropdown");
                    });

                var selectImg = selectBtn.GetComponent<Image>();
                if (selectImg != null) selectImg.color = new Color(0f, 0f, 0f, 0f);
                var selectLE = selectBtn.GetComponent<LayoutElement>() ?? selectBtn.AddComponent<LayoutElement>();
                selectLE.flexibleWidth = 1f;
                selectLE.preferredHeight = rowH - 4f;

                Text rowT = selectBtn.GetComponentInChildren<Text>(true);
                if (rowT != null)
                {
                    rowT.color = isCurrent ? Color.white : new Color(0.82f, 0.82f, 0.92f, 1f);
                    rowT.fontStyle = FontStyle.Normal;
                    rowT.alignment = TextAnchor.MiddleLeft;
                    VPBUiFont.ApplyTo(rowT);
                    // VPBUiFont.ApplyTo may reset size; force our desired dropdown font.
                    rowT.fontSize = labelFont;
                }

                float iconSz = Mathf.Min(rowH - 6f, 40f * sScale);
                if (!IsSubSceneTargetMode())
                {
                    if (saveSpr != null)
                    {
                        Atom capturedAtom = atom;
                        var saveBtn = UI.CreateSideTabSquareIconButton(
                            rowGO, iconSz, saveSpr,
                            () => SavePresetFromStorable(capturedAtom, "AppearancePresets"),
                            saveBackdrop, Mathf.Max(3f, 4f * sScale));
                        AddTooltipPlain(saveBtn, VPBTranslation.T("gallery.tbox.save_appearance", "Save appearance preset for this person"));
                    }
                    if (renameSpr != null)
                    {
                        Atom capturedAtom = atom;
                        var renameBtn = UI.CreateSideTabSquareIconButton(
                            rowGO, iconSz, renameSpr,
                            () => ShowPersonAtomRenameOverlay(capturedAtom),
                            renameBackdrop, Mathf.Max(3f, 4f * sScale));
                        AddTooltipPlain(renameBtn, VPBTranslation.T("gallery.rename.tooltip", "Rename this person"));
                    }
                }
            }
        }

        private void CloseTboxTargetMenu()
        {
            tboxTargetMenuOpen = false;
            if (tboxTargetMenuRootGO != null) tboxTargetMenuRootGO.SetActive(false);
        }

        private void ToggleTboxTargetMenu()
        {
            EnsureTboxTargetMenuBuilt();
            if (tboxTargetMenuRootGO == null || tboxTargetMenuPanelRT == null) return;

            if (tboxTargetMenuOpen)
            {
                CloseTboxTargetMenu();
                return;
            }

            // Position panel directly above dropdown button (always expand up).
            RectTransform anchorBtnRT = null;
            try
            {
                // Unity version in this project does not support GetComponentInParent<T>(bool includeInactive).
                // Button is clicked only when active, so standard GetComponentInParent is enough.
                if (tboxTargetDropdownBtnText != null)
                    anchorBtnRT = tboxTargetDropdownBtnText.GetComponentInParent<Button>()?.GetComponent<RectTransform>();
            }
            catch { anchorBtnRT = null; }

            RectTransform rootRT = backgroundBoxGO != null ? backgroundBoxGO.GetComponent<RectTransform>() : null;
            if (rootRT != null && anchorBtnRT != null)
            {
                try
                {
                    Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(rootRT, anchorBtnRT);
                    float gap = 6f;
                    // Align panel left edge with button left, place panel bottom at button top + gap.
                    Vector2 p = new Vector2(b.min.x, b.max.y + gap);
                    tboxTargetMenuPanelRT.anchorMin = tboxTargetMenuPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                    tboxTargetMenuPanelRT.pivot = new Vector2(0f, 0f);
                    tboxTargetMenuPanelRT.anchoredPosition = p;
                }
                catch { }
            }

            RebuildTboxTargetMenuOptions();
            tboxTargetMenuRootGO.SetActive(true);
            tboxTargetMenuRootGO.transform.SetAsLastSibling();
            tboxTargetMenuOpen = true;
        }

        // ─────────────────────────────────────────────────────────────────────────

        // Records the active subfilter chip's label Text so UpdateSelectionContextMenu can keep its
        // "(N)" equal to the grid's live count. The captured Text is the same one CreateTabButton ran
        // EllipsizeTextPreferredWidth on; the chip labels here are short enough not to ellipsize.
        private void CaptureActiveSubfilterChip(GameObject chipGO, string labelPrefix)
        {
            _activeSubfilterChipText = null;
            _activeSubfilterChipLabelPrefix = null;
            if (chipGO == null) return;
            try
            {
                var t = chipGO.GetComponentInChildren<Text>();
                if (t != null)
                {
                    _activeSubfilterChipText = t;
                    _activeSubfilterChipLabelPrefix = labelPrefix;
                }
            }
            catch { }
        }

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

            // Active clothing/hair chip uses the same `total` as the bottom counter, so the selected
            // chip's "(N)" can't diverge from the grid (survives package add/remove, hide-old-versions).
            if (_activeSubfilterChipText != null)
            {
                try { _activeSubfilterChipText.text = _activeSubfilterChipLabelPrefix + " (" + total + ")"; }
                catch { _activeSubfilterChipText = null; }
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
                // The import sidebar's bottom inset comes from this same toolbox height; resync it so the Apply
                // button doesn't overlap the toolbox when it expands to two rows.
                if (importSidebarActive && importSidebarRT != null)
                    importSidebarRT.offsetMin = new Vector2(importSidebarRT.offsetMin.x, SideTabScrollBottomInsetY());
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

            bool needsTboxActions = sel > 0 || cleanupModeActive || activeContentType == ContentType.History || IsSettingsPanelOpen();
            if (needsTboxActions)
            {
                string tboxKey = BuildTboxConditionalRefreshCacheKey();
                if (!string.Equals(tboxKey, _tboxConditionalRefreshCacheKey, StringComparison.Ordinal))
                {
                    _tboxConditionalRefreshCacheKey = tboxKey;
                    RefreshTboxConditionalActionButtons();
                }
            }
            else if (!string.IsNullOrEmpty(_tboxConditionalRefreshCacheKey))
            {
                _tboxConditionalRefreshCacheKey = "";
            }

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

        private string BuildTboxConditionalRefreshCacheKey()
        {
            var sb = new StringBuilder(256);
            sb.Append(cleanupModeActive ? 'C' : 'c');
            sb.Append(IsSettingsPanelOpen() ? 'S' : 's');
            sb.Append(activeContentType == ContentType.History ? 'H' : 'h');
            try { sb.Append(ScanWhitelistManager.Instance.IsEnabled ? 'W' : 'w'); }
            catch { sb.Append('w'); }

            if (selectedFiles == null || selectedFiles.Count == 0) return sb.ToString();

            bool historyBrowse = activeContentType == ContentType.History;
            var keys = new List<string>(selectedFiles.Count);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                keys.Add(GetSelectionIdentityKey(f, historyBrowse));
            }
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append('|');
                sb.Append(keys[i]);
            }
            return sb.ToString();
        }

        /// <summary>Copy/Delete/Hide/Unhide/Autoinstall: counts in labels and compact layout for the hide/AI group.</summary>
        private void RefreshTboxConditionalActionButtons()
        {
            int copyN = 0, deleteN = 0, hideN = 0, unhideN = 0, aiN = 0, noAiN = 0, scanWlTemporaryN = 0;
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
                if (!anyCleanupEntry)
                {
                    try { ExitCleanupModeForSidePanelNavigation(); } catch { }
                }
            }
            bool isCleanup = cleanupModeActive;
            bool historyBrowse = activeContentType == ContentType.History;
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
                try { CloseTboxTargetMenu(); } catch { }

                show(tboxCleanupBtn, false);
                show(tboxCleanupApplyBtn, false);
                show(tboxCleanupClearBtn, false);
                show(tboxCleanupAddExcludeBtn, false);
                show(tboxCleanupRemoveExcludeBtn, false);

                show(tboxAutoInstallBtn, false);
                show(tboxDisableAutoInstallBtn, false);
                show(tboxHideBtn, false);
                show(tboxUnhideBtn, false);
                show(tboxScanWhitelistTemporaryBtn, false);
                show(tboxLoadBtn, false);
                show(tboxUnloadBtn, false);
                show(tboxLoadDepsBtn, false);
                show(tboxCacheTexturesBtn, false);
                show(tboxThumbDebugBtn, false);
                show(tboxOpenHubBtn, false);
                show(tboxCopyPkgNamesBtn, false);
                show(tboxOverwriteSceneBtn, false);
                show(tboxSuppressScaleBtn, false);
                show(tboxDeleteBtn, false);
                show(tboxRemoveHistoryBtn, false);
                show(tboxSelectAllBtn, false);
                show(tboxClearSelectionBtn, false);

                try { RefreshSceneImportSideButtonVisibility(); } catch { }
                try { UpdateSideButtonPositions(); } catch { }
                try { RefreshTboxGridRateControlState(); } catch { }
                try { RefreshTboxFlexButtonLayout(); } catch { }
                return;
            }

            show(tboxSettingsCancelBtn, false);
            show(tboxSettingsSaveBtn, false);

            // Person target dropdown row: keep visible whenever person list exists.
            bool hasPersonAtoms = personAtoms != null && personAtoms.Count > 0 && personAtoms[0] != null;
            for (int i = 0; i < tboxPersonAtomBtns.Count; i++) show(tboxPersonAtomBtns[i], hasPersonAtoms);
            if (!hasPersonAtoms) { try { CloseTboxTargetMenu(); } catch { } }

            show(tboxCleanupBtn, !isCleanup);
            show(tboxCleanupApplyBtn, false);
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
            show(tboxLoadBtn, !isCleanup && !ScanWhitelistManager.Instance.IsEnabled);
            show(tboxUnloadBtn, !isCleanup && !ScanWhitelistManager.Instance.IsEnabled);
            show(tboxLoadDepsBtn, !isCleanup);
            show(tboxCacheTexturesBtn, !isCleanup);
            show(tboxThumbDebugBtn, !isCleanup && IsTextureLogVerbose());
            show(tboxOpenHubBtn, !isCleanup);
            // Non-settings mode must explicitly re-show buttons hidden by Settings mode.
            // Otherwise, once Settings hides them, they stay inactive forever.
            show(tboxCopyPkgNamesBtn, true);
            show(tboxDeleteBtn, true);
            show(tboxOverwriteSceneBtn, !isCleanup);
            show(tboxSuppressScaleBtn, !isCleanup);
            show(tboxSelectAllBtn, !isCleanup);
            show(tboxClearSelectionBtn, !isCleanup);

            if (isCleanup)
            {
                show(tboxRemoveHistoryBtn, false);
                int selectedCount = selectedFiles != null ? selectedFiles.Count : 0;
                SetTboxButtonEnabledVisual(tboxDeleteBtn, selectedCount > 0);
                SetTboxButtonEnabledVisual(tboxCleanupApplyBtn, false);
                SetTboxButtonEnabledVisual(tboxCleanupClearBtn, false);
                SetTboxButtonEnabledVisual(tboxCleanupAddExcludeBtn, cleanupHasNonExcludedSelection);
                SetTboxButtonEnabledVisual(tboxCleanupRemoveExcludeBtn, cleanupHasExcludedSelection);

                SetTboxButtonEnabledVisual(tboxCopyPkgNamesBtn, selectedCount > 0);
                SetTboxButtonEnabledVisual(tboxHideBtn, false);
                SetTboxButtonEnabledVisual(tboxUnhideBtn, false);
                SetTboxButtonEnabledVisual(tboxScanWhitelistTemporaryBtn, false);
                SetTboxButtonEnabledVisual(tboxAutoInstallBtn, false);
                SetTboxButtonEnabledVisual(tboxDisableAutoInstallBtn, false);
                SetTboxButtonEnabledVisual(tboxLoadBtn, false);
                SetTboxButtonEnabledVisual(tboxUnloadBtn, false);
                SetTboxButtonEnabledVisual(tboxLoadDepsBtn, false);
                SetTboxButtonEnabledVisual(tboxCacheTexturesBtn, false);
                SetTboxButtonEnabledVisual(tboxOpenHubBtn, false);
                show(tboxOverwriteSceneBtn, false);

                try { RefreshSceneImportSideButtonVisibility(); } catch { }
                try { UpdateSideButtonPositions(); } catch { }
                try { RefreshTboxGridRateControlState(); } catch { }
                RefreshTboxFlexButtonLayout();
                return;
            }

            if (selectedFiles != null && selectedFiles.Count > 0)
            {
                copyN = CollectUniquePackageUidsFromSelection(selectedFiles).Count
                    + CollectUniqueLocalSceneGalleryRelativePathsFromSelection(selectedFiles).Count;
                try { deleteN = GetTboxDeleteEligiblePackageCount() + GetTboxDeleteEligibleLocalSceneCount() + GetTboxDeleteEligibleLocalPresetCount(); } catch { deleteN = 0; }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;
                    if (TryGetTboxResolvablePackageState(f, out string uid, out FileEntry diskFe, out bool hidden, out bool fiAi, out bool uidAl, out bool uidWl))
                    {
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
                        continue;
                    }

                    if (TryGetTboxResolvableLocalPresetHideState(f, out string presetKey, out bool presetHidden))
                    {
                        if (!seen.Add(presetKey)) continue;
                        if (presetHidden) unhideN++;
                        else hideN++;
                        continue;
                    }
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
                                tempCandidates.Add(depId);
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
            bool hasAnyPkg = anyPkgInstalled || anyPkgNotInstalled;
            if (tboxUnloadBtn != null)   SetTboxButtonEnabledVisual(tboxUnloadBtn, hasAnyPkg && anyPkgInstalled);

            // Hub button: show when selection exists; enable only for single selected package row (.var uid resolvable).
            bool showOpenHubBtn = selectedFiles != null && selectedFiles.Count > 0;
            bool canOpenHub = false;
            if (selectedFiles != null && selectedFiles.Count == 1 && tboxOpenHubBtn != null)
            {
                try
                {
                    var uid = TryGetPackageUidForEntry(selectedFiles[0]);
                    canOpenHub = !string.IsNullOrEmpty(uid);
                }
                catch { canOpenHub = false; }
            }
            if (tboxOpenHubBtn != null)
            {
                tboxOpenHubBtn.SetActive(showOpenHubBtn);
                SetTboxButtonEnabledVisual(tboxOpenHubBtn, canOpenHub);
            }

            show(tboxRemoveHistoryBtn, historyBrowse);
            if (tboxRemoveHistoryBtn != null)
                SetTboxButtonEnabledVisual(
                    tboxRemoveHistoryBtn,
                    historyBrowse && selectedFiles != null && selectedFiles.Count > 0);

            bool canOverwriteScene = false;
            try { canOverwriteScene = TryGetSceneOverwriteSaveContext(out _); } catch { canOverwriteScene = false; }
            if (tboxOverwriteSceneBtn != null)
                SetTboxButtonEnabledVisual(tboxOverwriteSceneBtn, canOverwriteScene);

            try { RefreshSceneImportSideButtonVisibility(); } catch { }
            try { UpdateSideButtonPositions(); } catch { }
            try { RefreshTboxGridRateControlState(); } catch { }
            RefreshTboxFlexButtonLayout();
        }

        private void TboxAfterGridRateChanged()
        {
            try { RefreshVisibleGridVisualsOnly(); } catch { }
        }

        private void TboxApplyGridRatingToSelection(int ratingValue)
        {
            if (tboxGridRateHandler == null || selectedFiles == null || selectedFiles.Count == 0) return;
            ratingValue = Mathf.Clamp(ratingValue, 0, 5);
            int applied = 0;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                try
                {
                    RatingsManager.Instance.SetRating(f, ratingValue);
                    applied++;
                }
                catch { }
            }
            if (applied == 0) return;
            tboxGridRateHandler.SetDisplayOnly(ratingValue);
            tboxGridRateHandler.CloseSelector();
            TboxAfterGridRateChanged();
        }

        private static int TboxConsensusRatingDisplay(List<FileEntry> eligible)
        {
            if (eligible == null || eligible.Count == 0) return 0;
            int r0 = int.MinValue;
            for (int i = 0; i < eligible.Count; i++)
            {
                int r = 0;
                try { r = RatingsManager.Instance.GetRating(eligible[i]); }
                catch { r = 0; }
                if (r0 == int.MinValue) r0 = r;
                else if (r0 != r) return 0;
            }
            return Mathf.Clamp(r0 == int.MinValue ? 0 : r0, 0, 5);
        }

        private void RefreshTboxGridRateControlState()
        {
            if (tboxGridRateBtn == null || tboxGridRateHandler == null) return;
            var eligible = new List<FileEntry>();
            if (selectedFiles != null && selectedFiles.Count > 0)
            {
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    FileEntry f = selectedFiles[i];
                    if (f == null) continue;
                    // Allow rating for packages and local Custom Scenes (Saves/scene JSON).
                    // RatingsManager.SetRating(FileEntry, ...) supports both.
                    if (!string.IsNullOrEmpty(TryGetPackageUidForEntry(f)) ||
                        LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out _, false) ||
                        TryGetTboxResolvableLocalPresetHideState(f, out _, out _))
                        eligible.Add(f);
                }
            }
            bool allow =
                layoutMode == GalleryLayoutMode.Grid
                && !cleanupModeActive
                && !IsSettingsPanelOpen()
                && eligible.Count >= 1;
            if (!allow)
            {
                if (tboxGridRateBtn.activeSelf) tboxGridRateBtn.SetActive(false);
                try { tboxGridRateHandler.CloseSelector(); } catch { }
                return;
            }
            if (!tboxGridRateBtn.activeSelf) tboxGridRateBtn.SetActive(true);
            Text txt = tboxGridRateBtn.GetComponentInChildren<Text>(true);
            // Use stable synthetic id so selector stays open across recycled FileEntry instances (Custom Scenes in particular).
            string stableId = null;
            try { stableId = TryGetPackageUidForEntry(eligible[0]); } catch { stableId = null; }
            if (string.IsNullOrEmpty(stableId))
            {
                try
                {
                    if (LocalSceneGallerySupport.TryResolveSavesSceneJson(eligible[0], out string abs, out string rel, false))
                        stableId = "LOCALSCENE:" + (!string.IsNullOrEmpty(rel) ? rel : abs);
                }
                catch { }
            }
            if (string.IsNullOrEmpty(stableId))
            {
                try { stableId = !string.IsNullOrEmpty(eligible[0].Uid) ? eligible[0].Uid : eligible[0].Path; } catch { stableId = null; }
            }
            tboxGridRateHandler.Init(stableId ?? "", txt, tboxGridRateSelectorGO);
            tboxGridRateHandler.SetDisplayOnly(TboxConsensusRatingDisplay(eligible));
            if (tboxGridRateIconImage != null)
                tboxGridRateHandler.BindStarIcon(tboxGridRateIconImage);
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

        /// <summary>
        /// Local custom presets live on disk (not inside .var) and use VaM-native .hide markers.
        /// Keep separate from <see cref="TryGetTboxResolvablePackageState"/> so autoinstall/load logic stays package-only.
        /// </summary>
        private bool TryGetTboxResolvableLocalPresetHideState(FileEntry f, out string key, out bool hidden)
        {
            key = null;
            hidden = false;
            if (f == null) return false;

            string p = null;
            try { p = f.Path ?? f.Uid; } catch { p = null; }
            if (string.IsNullOrEmpty(p)) return false;
            p = p.Replace('\\', '/');
            if (p.IndexOf(":/", StringComparison.Ordinal) >= 0) return false; // inside .var

            if (!LocalPresetDeleteSupport.IsAllowedLocalPresetRelativePath(p)) return false;

            key = p;
            try { hidden = f.IsHidden(); } catch { hidden = false; }
            return true;
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

        private static bool IsShiftHeld()
        {
            try
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
            catch { return false; }
        }

        private string GetCopyNamesTooltipText(bool ctrlHeld, bool shiftHeld)
        {
            if (ctrlHeld && shiftHeld)
                return VPBTranslation.T(
                    "gallery.tooltip.tbox_copy_names_internalpaths",
                    "Copy full disk paths down to each selected file's internal .json/.vap to clipboard."
                );
            if (ctrlHeld)
                return VPBTranslation.T(
                    "gallery.tooltip.tbox_copy_names_fullpaths",
                    "Copy full disk paths for selected packages and local scenes to clipboard. Add Shift for internal file paths. Release Ctrl to copy names."
                );

            return VPBTranslation.T(
                "gallery.tooltip.tbox_copy_names",
                "Copy selected package .var names and local Saves/scene paths to clipboard. Hold Ctrl for full paths, Ctrl+Shift for internal file paths."
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

                    // Set immediately, then keep it responsive to Ctrl/Shift state while hovered.
                    tboxCopyNamesTooltipLast = GetCopyNamesTooltipText(IsCtrlHeld(), IsShiftHeld());
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
                try { msg = GetCopyNamesTooltipText(IsCtrlHeld(), IsShiftHeld()); } catch { msg = null; }

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

                bool ctrlHeld = IsCtrlHeld();
                bool shiftHeld = IsShiftHeld();
                bool fullPaths = ctrlHeld;
                bool internalPaths = ctrlHeld && shiftHeld;

                if (internalPaths)
                {
                    CopySelectedInternalFilePathsToClipboard();
                    return;
                }

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

        // Ctrl+Shift variant: per-selected-file full disk path including the internal
        // .json/.vap/etc. inside the .var (e.g. "C:\...\AddonPackages\Foo.var:/Custom/.../preset.vap").
        private void CopySelectedInternalFilePathsToClipboard()
        {
            try
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var list = new List<string>(selectedFiles.Count);
                var varDiskAbsCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    var f = selectedFiles[i];
                    if (f == null) continue;

                    string entry = null;

                    if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out string abs, out string _, false))
                    {
                        if (!string.IsNullOrEmpty(abs)) entry = abs.Replace('\\', '/');
                    }
                    else if (f is VarFileEntry vfe)
                    {
                        string uid = TryGetPackageUidForEntry(f);
                        string varAbs = null;
                        if (!string.IsNullOrEmpty(uid) && !varDiskAbsCache.TryGetValue(uid, out varAbs))
                        {
                            string varDisk = null;
                            try { varDisk = ResolveVarPathForUid(uid); } catch { varDisk = null; }
                            if (!string.IsNullOrEmpty(varDisk))
                            {
                                try { varAbs = FileManager.GetFullPath(varDisk); } catch { varAbs = varDisk; }
                            }
                            varDiskAbsCache[uid] = varAbs;
                        }

                        string internalPath = vfe.InternalPath ?? "";
                        if (!string.IsNullOrEmpty(varAbs) && !string.IsNullOrEmpty(internalPath))
                            entry = varAbs.Replace('\\', '/') + ":/" + internalPath;
                    }
                    else if (!string.IsNullOrEmpty(f.Path))
                    {
                        try { entry = FileManager.GetFullPath(f.Path); } catch { entry = f.Path; }
                        if (!string.IsNullOrEmpty(entry)) entry = entry.Replace('\\', '/');
                    }

                    if (!string.IsNullOrEmpty(entry) && seen.Add(entry))
                        list.Add(entry);
                }

                if (list.Count == 0)
                {
                    ShowTemporaryStatus("No internal file paths in selection.");
                    return;
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);

                string text = string.Join("\n", list.ToArray());
                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus($"Copied {list.Count} internal path(s) to clipboard.", 2f);
                TryPulseTboxCopyNamesIcon();
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] CopySelectedInternalFilePathsToClipboard error: " + ex.Message);
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
