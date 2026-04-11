using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        // Selection toolbox ("tbox")
        private GameObject tbox;
        private Text       tboxLabel;
        private Text       tboxHintLabel;
        private GameObject tboxCopyPkgNamesBtn;
        private GameObject tboxDeleteBtn;
        private GameObject tboxAutoInstallBtn;
        private GameObject tboxDisableAutoInstallBtn;
        private GameObject tboxHideBtn;
        private GameObject tboxUnhideBtn;
        private GameObject tboxLoadBtn;
        private GameObject tboxUnloadBtn;
        private GameObject tboxLoadDepsBtn;
        private GameObject tboxCacheTexturesBtn;

        // Responsive tbox action buttons: 1–2 rows, flexible widths
        private GameObject    tboxButtonsFlexRoot;
        private RectTransform tboxButtonsFlexRootRT;
        private GameObject    tboxBtnRow0GO;
        private GameObject    tboxBtnRow1GO;
        private RectTransform tboxBtnRow0RT;
        private RectTransform tboxBtnRow1RT;
        private LayoutElement tboxBtnRow0LE;
        private LayoutElement tboxBtnRow1LE;
        private HorizontalLayoutGroup tboxBtnRow0HLG;
        private HorizontalLayoutGroup tboxBtnRow1HLG;
        private GameObject    tboxButtonStash;
        private int           tboxButtonLayoutRows = 1;
        private float         tboxLastFlexAvailW = -1f;
        private const float   tboxBtnRowGap = 4f;

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
            one(tboxDisableAutoInstallBtn);
            one(tboxUnhideBtn);
            one(tboxHideBtn);
            one(tboxAutoInstallBtn);
            one(tboxDeleteBtn);
            one(tboxLoadBtn);
            one(tboxUnloadBtn);
            one(tboxLoadDepsBtn);
            one(tboxCacheTexturesBtn);
            one(tboxCopyPkgNamesBtn);
        }

        private void TboxDetachAllActionButtonsForLayout()
        {
            if (tboxButtonStash == null) return;
            Transform p = tboxButtonStash.transform;
            void d(GameObject go)
            {
                if (go != null) go.transform.SetParent(p, false);
            }
            d(tboxDisableAutoInstallBtn);
            d(tboxUnhideBtn);
            d(tboxHideBtn);
            d(tboxAutoInstallBtn);
            d(tboxDeleteBtn);
            d(tboxLoadBtn);
            d(tboxUnloadBtn);
            d(tboxLoadDepsBtn);
            d(tboxCacheTexturesBtn);
            d(tboxCopyPkgNamesBtn);
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

            float RowMinSum(List<GameObject> row)
            {
                float s = 0f;
                for (int i = 0; i < row.Count; i++)
                {
                    if (!TryGetWidths(row[i], out float mw, out _)) continue;
                    s += mw;
                    if (i > 0) s += gap;
                }
                return s;
            }

            var ltr = new List<GameObject>(14);
            if (tboxDisableAutoInstallBtn != null && tboxDisableAutoInstallBtn.activeSelf) ltr.Add(tboxDisableAutoInstallBtn);
            if (tboxUnhideBtn != null && tboxUnhideBtn.activeSelf) ltr.Add(tboxUnhideBtn);
            if (tboxHideBtn != null && tboxHideBtn.activeSelf) ltr.Add(tboxHideBtn);
            if (tboxAutoInstallBtn != null && tboxAutoInstallBtn.activeSelf) ltr.Add(tboxAutoInstallBtn);
            if (tboxDeleteBtn != null) ltr.Add(tboxDeleteBtn);
            if (tboxLoadBtn != null) ltr.Add(tboxLoadBtn);
            if (tboxUnloadBtn != null) ltr.Add(tboxUnloadBtn);
            if (tboxLoadDepsBtn != null) ltr.Add(tboxLoadDepsBtn);
            if (tboxCacheTexturesBtn != null) ltr.Add(tboxCacheTexturesBtn);
            if (tboxCopyPkgNamesBtn != null) ltr.Add(tboxCopyPkgNamesBtn);

            var rtl = new List<GameObject>(ltr.Count);
            for (int i = ltr.Count - 1; i >= 0; i--)
                rtl.Add(ltr[i]);

            bool FitsOneRowMin()
            {
                return RowMinSum(rtl) <= avail + 1f;
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
                float used = 0f;
                for (int i = 0; i < rtl.Count; i++)
                {
                    GameObject go = rtl[i];
                    if (!TryGetWidths(go, out float mw, out _)) continue;
                    float need = mw + (row0rtl.Count > 0 ? gap : 0f);
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
            if (tboxButtonsLayerRT != null)
                tboxButtonsLayerRT.sizeDelta = new Vector2(tboxButtonsLayerRT.sizeDelta.x, band);

            LayoutRebuilder.MarkLayoutForRebuild(tboxButtonsFlexRootRT);
            tboxLastFlexAvailW = tboxButtonsFlexRootRT.rect.width;
        }

        // Expand/collapse state
        private bool  tboxIsHovered  = false;
        private bool  tboxPinned     = false;
        private float tboxExpandT    = 0f;        // 0 = collapsed, 1 = expanded

        private RectTransform tboxRT;
        private CanvasGroup   tboxLabelCG;        // fades OUT when expanding
        private CanvasGroup   tboxButtonsCG;      // fades IN when expanding
        private GameObject    tboxPinBtn;
        private Text          tboxPinBtnText;

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

            tbox   = hoverPathRT.gameObject;
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
            labelLayerRT.anchorMin        = new Vector2(0f, 0f);
            labelLayerRT.anchorMax        = new Vector2(1f, 0f);
            labelLayerRT.pivot            = new Vector2(0.5f, 0f);
            labelLayerRT.anchoredPosition = Vector2.zero;
            labelLayerRT.sizeDelta        = new Vector2(-48f, tboxInfoRowHeight);
            tboxLabelLayerRT = labelLayerRT;

            var rowGO = new GameObject("TboxLabelRow");
            rowGO.transform.SetParent(labelGO.transform, false);
            var rowRT = rowGO.AddComponent<RectTransform>();
            rowRT.anchorMin = Vector2.zero;
            rowRT.anchorMax = Vector2.one;
            rowRT.offsetMin = Vector2.zero;
            rowRT.offsetMax = Vector2.zero;

            var rowHLG = rowGO.AddComponent<HorizontalLayoutGroup>();
            rowHLG.childAlignment      = TextAnchor.MiddleCenter;
            rowHLG.spacing             = 12f;
            rowHLG.childForceExpandWidth  = false;
            rowHLG.childForceExpandHeight = true;
            rowHLG.childControlWidth   = true;
            rowHLG.childControlHeight  = true;
            rowHLG.padding             = new RectOffset(8, 8, 0, 0);

            const int tboxCollapsedFont = 18;

            var labelTextGO = new GameObject("Text");
            labelTextGO.transform.SetParent(rowGO.transform, false);
            tboxLabel = labelTextGO.AddComponent<Text>();
            tboxLabel.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxLabel.fontSize  = tboxCollapsedFont;
            tboxLabel.fontStyle = FontStyle.Bold;
            tboxLabel.color     = new Color(0.92f, 0.92f, 0.92f, 1f);
            tboxLabel.alignment = TextAnchor.MiddleCenter;
            tboxLabel.raycastTarget = false;
            var labelShadow = labelTextGO.AddComponent<Shadow>();
            labelShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
            labelShadow.effectDistance = new Vector2(1f, -1f);
            var labelCSF = labelTextGO.AddComponent<ContentSizeFitter>();
            labelCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            labelCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            var hintTextGO = new GameObject("HoverHint");
            hintTextGO.transform.SetParent(rowGO.transform, false);
            tboxHintLabel = hintTextGO.AddComponent<Text>();
            tboxHintLabel.font      = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tboxHintLabel.fontSize  = tboxCollapsedFont;
            tboxHintLabel.fontStyle = FontStyle.Normal;
            tboxHintLabel.color     = new Color(0.50f, 0.50f, 0.50f, 1f);
            tboxHintLabel.alignment = TextAnchor.MiddleCenter;
            tboxHintLabel.raycastTarget = false;
            tboxHintLabel.text      = VPBTranslation.T("gallery.tbox.hover_expand", "Hover to expand");
            hintTextGO.SetActive(false);
            var hintShadow = hintTextGO.AddComponent<Shadow>();
            hintShadow.effectColor    = new Color(0f, 0f, 0f, 0.5f);
            hintShadow.effectDistance = new Vector2(1f, -1f);
            var hintCSF = hintTextGO.AddComponent<ContentSizeFitter>();
            hintCSF.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            hintCSF.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── Buttons panel (expanded view) ─────────────────────────────────────
            var bpGO = new GameObject("TboxButtonsLayer");
            bpGO.transform.SetParent(tbox.transform, false);
            tboxButtonsCG = bpGO.AddComponent<CanvasGroup>();
            tboxButtonsCG.alpha          = 0f;
            tboxButtonsCG.blocksRaycasts = false;
            tboxButtonsCG.interactable   = false;

            // Buttons layer sits in the TOP row — directly above the label row.
            // Revealed by RectMask2D as the bar grows upward.
            var bpRT = bpGO.GetComponent<RectTransform>();
            if (bpRT == null) bpRT = bpGO.AddComponent<RectTransform>();
            bpRT.anchorMin        = new Vector2(0f, 0f);
            bpRT.anchorMax        = new Vector2(1f, 0f);
            bpRT.pivot            = new Vector2(0.5f, 0f);
            bpRT.anchoredPosition = new Vector2(0f, tboxInfoRowHeight); // sits one row above bottom
            bpRT.sizeDelta        = new Vector2(-48f, tboxInfoRowHeight);
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

            const int tboxActionBtnFont = 16;
            const float tboxWCopy = 210f;
            const float tboxWCache = 142f;
            const float tboxWLoadDeps = 118f;
            const float tboxWUnload = 90f;
            const float tboxWLoad = 88f;
            const float tboxWDelete = 180f;
            const float tboxWAi = 168f;
            const float tboxWHide = 100f;
            const float tboxMinCopy = 108f;
            const float tboxMinCache = 72f;
            const float tboxMinLoadDeps = 72f;
            const float tboxMinUnload = 64f;
            const float tboxMinLoad = 56f;
            const float tboxMinDelete = 80f;
            const float tboxMinAi = 104f;
            const float tboxMinHide = 56f;
            const float tboxMinUnhide = 64f;
            const float tboxMinNoAi = 104f;

            // Placeholders — layout is resolved in RefreshTboxFlexButtonLayout (stretch + LayoutElement).
            tboxCopyPkgNamesBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.copy_names", "Copy Names"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                CopySelectedPackageNamesToClipboard
            );
            tboxCopyPkgNamesBtn.name = "Tbox_CopyPackageNames";
            TboxConfigureActionButtonFlex(tboxCopyPkgNamesBtn, tboxMinCopy, tboxWCopy, innerRowH);
            AddTooltip(tboxCopyPkgNamesBtn, "gallery.tooltip.tbox_copy_names", "Copy package .var names and local Saves/scene paths (one per line) to clipboard");

            tboxCacheTexturesBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.cache_textures", "Cache Textures"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxCacheTexturesSelected
            );
            tboxCacheTexturesBtn.name = "Tbox_CacheTextures";
            TboxConfigureActionButtonFlex(tboxCacheTexturesBtn, tboxMinCache, tboxWCache, innerRowH);
            AddTooltip(tboxCacheTexturesBtn, "gallery.tooltip.tbox_cache_textures", "Build VPB texture cache for selected .var packages (same as F3 for packages)");

            tboxLoadDepsBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.load_deps", "Load Deps"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxLoadDepsSelectedPackages
            );
            tboxLoadDepsBtn.name = "Tbox_LoadDeps";
            TboxConfigureActionButtonFlex(tboxLoadDepsBtn, tboxMinLoadDeps, tboxWLoadDeps, innerRowH);
            AddTooltip(tboxLoadDepsBtn, "gallery.tooltip.tbox_load_deps", "Copy selected packages and their dependencies from AllPackages to AddonPackages (respects Settings → load deps with package)");

            tboxUnloadBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.unload", "Unload"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxUnloadSelectedPackages
            );
            tboxUnloadBtn.name = "Tbox_Unload";
            TboxConfigureActionButtonFlex(tboxUnloadBtn, tboxMinUnload, tboxWUnload, innerRowH);
            AddTooltip(tboxUnloadBtn, "gallery.tooltip.tbox_unload", "Move selected installed .var files from AddonPackages back to AllPackages");

            tboxLoadBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.load", "Load"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxLoadSelectedPackages
            );
            tboxLoadBtn.name = "Tbox_Load";
            TboxConfigureActionButtonFlex(tboxLoadBtn, tboxMinLoad, tboxWLoad, innerRowH);
            AddTooltip(tboxLoadBtn, "gallery.tooltip.tbox_load", "Copy selected .var from AllPackages to AddonPackages (this package only, no dependencies)");

            tboxDeleteBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.delete", "Delete"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxDeleteSelectedPackages
            );
            tboxDeleteBtn.name = "Tbox_Delete";
            TboxConfigureActionButtonFlex(tboxDeleteBtn, tboxMinDelete, tboxWDelete, innerRowH);
            AddTooltip(tboxDeleteBtn, "gallery.tooltip.tbox_delete", "Move selected packages to DeletedPackages; local Saves/scene JSON (+ preview) to DeletedScenes");
            try
            {
                var delImg = tboxDeleteBtn.GetComponent<Image>();
                if (delImg != null) delImg.color = new Color(0.35f, 0.15f, 0.15f, 1f);
            }
            catch { }

            tboxAutoInstallBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.autoinstall", "Autoinstall"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxAutoInstallSelectedPackages
            );
            tboxAutoInstallBtn.name = "Tbox_AutoInstall";
            TboxConfigureActionButtonFlex(tboxAutoInstallBtn, tboxMinAi, tboxWAi, innerRowH);
            AddTooltip(tboxAutoInstallBtn, "gallery.tooltip.tbox_autoinstall", "Flag selected packages for auto-install and auto-load. Packages in AllPackages are copied to AddonPackages on the next VaM start (not immediately).");

            tboxHideBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.hide", "Hide"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxHideSelectedPackages
            );
            tboxHideBtn.name = "Tbox_Hide";
            TboxConfigureActionButtonFlex(tboxHideBtn, tboxMinHide, tboxWHide, innerRowH);
            AddTooltip(tboxHideBtn, "gallery.tooltip.tbox_hide", "Hide selected packages in VaM file lists (AddonPackagesFilePrefs … .hide)");

            tboxUnhideBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.unhide", "Unhide"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxUnhideSelectedPackages
            );
            tboxUnhideBtn.name = "Tbox_Unhide";
            tboxUnhideBtn.SetActive(false);
            TboxConfigureActionButtonFlex(tboxUnhideBtn, tboxMinUnhide, tboxWHide, innerRowH);
            AddTooltip(tboxUnhideBtn, "gallery.tooltip.tbox_unhide", "Remove .hide markers for selected packages");

            tboxDisableAutoInstallBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                VPBTranslation.T("gallery.tbox.no_autoinstall", "No autoinstall"), tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxDisableAutoInstallSelectedPackages
            );
            tboxDisableAutoInstallBtn.name = "Tbox_NoAutoInstall";
            tboxDisableAutoInstallBtn.SetActive(false);
            TboxConfigureActionButtonFlex(tboxDisableAutoInstallBtn, tboxMinNoAi, tboxWAi, innerRowH);
            AddTooltip(tboxDisableAutoInstallBtn, "gallery.tooltip.tbox_no_autoinstall", "Clear auto-install and VPB auto-load for selected packages");

            // ── Pin toggle (right edge, always visible) ───────────────────────────
            tboxPinBtn = UI.CreateUIButton(
                tbox, 44, 0, "", 15,
                0, 0, AnchorPresets.vStretchRight,
                () =>
                {
                    tboxPinned = !tboxPinned;
                    RefreshTboxPinVisual();
                }
            );
            tboxPinBtn.name = "Tbox_Pin";
            // Pin button is anchored to the bottom row (tooltip row), not the full bar
            var pinRT = tboxPinBtn.GetComponent<RectTransform>();
            pinRT.anchorMin        = new Vector2(1f, 0f);
            pinRT.anchorMax        = new Vector2(1f, 0f);
            pinRT.pivot            = new Vector2(1f, 0f);
            pinRT.anchoredPosition = Vector2.zero;
            pinRT.sizeDelta        = new Vector2(44f, tboxInfoRowHeight);

            tboxPinBtnText = tboxPinBtn.GetComponentInChildren<Text>();

            // Left border line on pin button (visual separator)
            {
                var sep = new GameObject("Separator");
                sep.transform.SetParent(tboxPinBtn.transform, false);
                var sepImg = sep.AddComponent<Image>();
                sepImg.color = new Color(1f, 1f, 1f, 0.08f);
                sepImg.raycastTarget = false;
                var sepRT = sep.GetComponent<RectTransform>();
                sepRT.anchorMin        = new Vector2(0f, 0.15f);
                sepRT.anchorMax        = new Vector2(0f, 0.85f);
                sepRT.pivot            = new Vector2(0f, 0.5f);
                sepRT.anchoredPosition = Vector2.zero;
                sepRT.sizeDelta        = new Vector2(1f, 0f);
            }

            // Thin separator line at the row boundary (between tooltip row and toolbox row)
            {
                var rowSepGO = new GameObject("RowSeparator");
                rowSepGO.transform.SetParent(tbox.transform, false);
                var rowSepImg = rowSepGO.AddComponent<Image>();
                rowSepImg.color = new Color(1f, 1f, 1f, 0.12f);
                rowSepImg.raycastTarget = false;
                var rowSepRT = rowSepGO.GetComponent<RectTransform>();
                rowSepRT.anchorMin        = new Vector2(0f, 0f);
                rowSepRT.anchorMax        = new Vector2(1f, 0f);
                rowSepRT.pivot            = new Vector2(0.5f, 0f);
                rowSepRT.anchoredPosition = new Vector2(0f, tboxInfoRowHeight);
                rowSepRT.sizeDelta        = new Vector2(0f, 1f);

                // Scale action to reposition separator when InnerPaneScale changes
                var rsRT = rowSepRT;
                innerPaneScaleActions.Add(s => {
                    if (rsRT != null) rsRT.anchoredPosition = new Vector2(0f, 60f * s);
                });
            }

            // Scale actions to resize rows when InnerPaneScale changes
            {
                var lRT = tboxLabelLayerRT;
                var bRT = tboxButtonsLayerRT;
                var pRT = pinRT;
                innerPaneScaleActions.Add(s => {
                    float rowH = 60f * s;
                    tboxInfoRowHeight = rowH;
                    if (lRT != null) lRT.sizeDelta = new Vector2(lRT.sizeDelta.x, rowH);
                    if (bRT != null) bRT.anchoredPosition = new Vector2(0f, rowH);
                    if (pRT != null) pRT.sizeDelta = new Vector2(pRT.sizeDelta.x, rowH);
                    try { TboxSetAllFlexActionButtonHeights(Mathf.Max(34f, rowH - 8f)); } catch { }
                    try { RefreshTboxFlexButtonLayout(); } catch { }
                });
            }

            RefreshTboxPinVisual();
            AddTooltip(tboxPinBtn, "gallery.tooltip.tbox_pin", "Pin — keep toolbar expanded");
        }

        private void RefreshTboxPinVisual()
        {
            if (tboxPinBtnText == null) return;
            if (tboxPinned)
            {
                tboxPinBtnText.text  = "●";
                tboxPinBtnText.color = new Color(0.45f, 0.75f, 0.90f, 1f); // teal accent
                var pinImg = tboxPinBtn != null ? tboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.10f, 0.22f, 0.30f, 1f);
            }
            else
            {
                tboxPinBtnText.text  = "○";
                tboxPinBtnText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
                var pinImg = tboxPinBtn != null ? tboxPinBtn.GetComponent<Image>() : null;
                if (pinImg != null) pinImg.color = new Color(0.20f, 0.20f, 0.20f, 1f);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void UpdateSelectionContextMenu()
        {
            if (canvas == null) return;
            EnsureTboxUI();
            if (tbox == null) return;

            int sel   = (selectedFiles != null) ? selectedFiles.Count : 0;
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

            // Expansion only when there is a selection
            bool canExpand = sel > 0;
            if (!canExpand)
            {
                tboxExpandT   = 0f;
                tboxIsHovered = false;
                tboxButtonLayoutRows = 1;
                if (tboxButtonsLayerRT != null)
                    tboxButtonsLayerRT.sizeDelta = new Vector2(tboxButtonsLayerRT.sizeDelta.x, tboxInfoRowHeight);
                if (tboxPinned) { tboxPinned = false; RefreshTboxPinVisual(); }
            }

            bool wantExpanded = canExpand && (tboxIsHovered || tboxPinned);

            // Smooth animate expand T — fast snap
            float targetT = wantExpanded ? 1f : 0f;
            tboxExpandT = Mathf.Lerp(tboxExpandT, targetT, Time.deltaTime * 22f);
            if (Mathf.Abs(tboxExpandT - targetT) < 0.005f) tboxExpandT = targetT;

            // Animate bar height: grow offsetMax upward to reveal the button band (1 or 2 rows)
            if (tboxRT != null)
            {
                if (sel > 0 && tboxButtonsFlexRootRT != null && tboxExpandT > 0.02f)
                {
                    float w = tboxButtonsFlexRootRT.rect.width;
                    if (w > 8f && Mathf.Abs(w - tboxLastFlexAvailW) > 2f)
                    {
                        try { RefreshTboxFlexButtonLayout(); } catch { }
                    }
                }

                float btnBand = tboxInfoRowHeight * Mathf.Max(1, tboxButtonLayoutRows)
                    + (tboxButtonLayoutRows > 1 ? tboxBtnRowGap : 0f);
                float targetTop = tboxTopOffsetBase + btnBand * tboxExpandT;
                float newTop = Mathf.Lerp(tboxRT.offsetMax.y, targetTop, Time.deltaTime * 22f);
                if (Mathf.Abs(newTop - targetTop) < 0.5f) newTop = targetTop;
                tboxRT.offsetMax = new Vector2(tboxRT.offsetMax.x, newTop);
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
                tboxLabelCG.alpha = Mathf.Lerp(tboxLabelCG.alpha, labelTarget, Time.deltaTime * 22f);

            // Buttons stay fully opaque — RectMask2D handles the slide-in reveal as the bar grows.
            // Gate on tboxExpandT only (not infoShowing) so that a fading hover-path label
            // doesn't suppress buttons and cause them to flash when the path finally fades out.
            if (tboxButtonsCG != null)
            {
                bool showButtons = canExpand && tboxExpandT > 0.05f;
                tboxButtonsCG.alpha          = showButtons ? 1f : 0f;
                tboxButtonsCG.blocksRaycasts = canExpand && tboxExpandT > 0.5f;
                tboxButtonsCG.interactable   = canExpand && tboxExpandT > 0.85f;
            }

            if (sel > 0)
                RefreshTboxConditionalActionButtons();

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
            int copyN = 0, deleteN = 0, hideN = 0, unhideN = 0, aiN = 0, noAiN = 0;
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
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out bool hidden, out bool fiAi, out bool uidAl))
                        continue;
                    if (!seen.Add(uid)) continue;
                    if (hidden) unhideN++;
                    else hideN++;
                    if (fiAi || uidAl) noAiN++;
                    if (!fiAi || !uidAl) aiN++;
                }
            }

            if (tboxCopyPkgNamesBtn != null)
                SetTboxCountButtonLabel(tboxCopyPkgNamesBtn, "gallery.tbox.copy_names_count", "Copy Names ({0})", copyN);
            if (tboxDeleteBtn != null)
                SetTboxCountButtonLabel(tboxDeleteBtn, "gallery.tbox.delete_count", "Delete ({0})", deleteN);

            bool showHide = hideN > 0;
            bool showUnhide = unhideN > 0;
            bool showAi = aiN > 0;
            bool showNoAi = noAiN > 0;

            if (tboxHideBtn != null)
            {
                tboxHideBtn.SetActive(showHide);
                if (showHide) SetTboxCountButtonLabel(tboxHideBtn, "gallery.tbox.hide_count", "Hide ({0})", hideN);
            }
            if (tboxUnhideBtn != null)
            {
                tboxUnhideBtn.SetActive(showUnhide);
                if (showUnhide) SetTboxCountButtonLabel(tboxUnhideBtn, "gallery.tbox.unhide_count", "Unhide ({0})", unhideN);
            }
            if (tboxAutoInstallBtn != null)
            {
                tboxAutoInstallBtn.SetActive(showAi);
                if (showAi) SetTboxCountButtonLabel(tboxAutoInstallBtn, "gallery.tbox.autoinstall_count", "Autoinstall ({0})", aiN);
            }
            if (tboxDisableAutoInstallBtn != null)
            {
                tboxDisableAutoInstallBtn.SetActive(showNoAi);
                if (showNoAi) SetTboxCountButtonLabel(tboxDisableAutoInstallBtn, "gallery.tbox.no_autoinstall_count", "No autoinstall ({0})", noAiN);
            }

            RefreshTboxFlexButtonLayout();
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
            uid = null;
            diskFe = null;
            isHidden = false;
            fileAutoInstall = false;
            uidAutoLoad = false;

            if (LocalSceneGallerySupport.TryResolveSavesSceneJson(f, out _, out string relGallery, false))
            {
                uid = relGallery.Replace('\\', '/');
                diskFe = f;
                isHidden = PackageHidePrefs.IsLocalSceneJsonHidden(f);
                try { fileAutoInstall = LocalSceneGallerySupport.IsLocalSceneAutoInstallMarked(f); }
                catch { fileAutoInstall = false; }
                uidAutoLoad = false;
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
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private void CopySelectedPackageNamesToClipboard()
        {
            try
            {
                if (selectedFiles == null || selectedFiles.Count == 0)
                {
                    ShowTemporaryStatus("No selection.");
                    return;
                }

                var uids = CollectUniquePackageUidsFromSelection(selectedFiles);
                var localScenes = CollectUniqueLocalSceneGalleryRelativePathsFromSelection(selectedFiles);
                if (uids.Count == 0 && localScenes.Count == 0)
                {
                    ShowTemporaryStatus("No package or local scene paths in selection.");
                    return;
                }

                var list = new List<string>(uids.Count + localScenes.Count);
                foreach (var uid in uids)
                    list.Add(uid + ".var");
                foreach (var rel in localScenes)
                    list.Add(rel);
                list.Sort(StringComparer.OrdinalIgnoreCase);

                string text = string.Join("\n", list.ToArray());

                GUIUtility.systemCopyBuffer = text;
                ShowTemporaryStatus($"Copied {list.Count} name(s) to clipboard.", 2f);
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] CopySelectedPackageNamesToClipboard error: " + ex.Message);
                ShowTemporaryStatus("Copy failed. See log.", 2f);
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
