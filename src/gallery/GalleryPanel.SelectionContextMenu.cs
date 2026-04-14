using System;
using System.Collections;
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
        private Text tboxLabel;
        private Text tboxHintLabel;
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
        private GameObject tboxSceneImportBtn;

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
            one(tboxSceneImportBtn);
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
            d(tboxSceneImportBtn);
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
            // Keep these buttons in a fixed order to avoid layout shuffling as state flips.
            if (tboxDisableAutoInstallBtn != null) ltr.Add(tboxDisableAutoInstallBtn);
            if (tboxUnhideBtn != null) ltr.Add(tboxUnhideBtn);
            if (tboxHideBtn != null) ltr.Add(tboxHideBtn);
            if (tboxAutoInstallBtn != null) ltr.Add(tboxAutoInstallBtn);
            if (tboxDeleteBtn != null) ltr.Add(tboxDeleteBtn);
            if (tboxLoadBtn != null) ltr.Add(tboxLoadBtn);
            if (tboxUnloadBtn != null) ltr.Add(tboxUnloadBtn);
            if (tboxLoadDepsBtn != null) ltr.Add(tboxLoadDepsBtn);
            if (tboxCacheTexturesBtn != null) ltr.Add(tboxCacheTexturesBtn);
            if (tboxCopyPkgNamesBtn != null) ltr.Add(tboxCopyPkgNamesBtn);
            if (tboxSceneImportBtn != null) ltr.Add(tboxSceneImportBtn);

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
            tboxLabel.color = new Color(0.92f, 0.92f, 0.92f, 1f);
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
                tboxClipboardListSprite  = UI.LoadIconSprite("vpb_icons/clipboard_list.png",  new Color(0.92f, 0.92f, 0.92f, 1f));
                tboxClipboardCheckSprite = UI.LoadIconSprite("vpb_icons/clipboard_check.png", new Color(1f, 1f, 1f, 1f));
                if (tboxClipboardListSprite != null)
                {
                    UI.AddIconToButton(tboxCopyPkgNamesBtn, tboxClipboardListSprite, padding: 6f);
                    tboxCopyNamesIconImage = tboxCopyPkgNamesBtn.transform.Find("Icon")?.GetComponent<Image>();
                }
            }
            catch { }

            tboxCacheTexturesBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxCacheTexturesSelected
            );
            tboxCacheTexturesBtn.name = "Tbox_CacheTextures";
            TboxConfigureActionButtonFlex(tboxCacheTexturesBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxCacheTexturesBtn, "gallery.tooltip.tbox_cache_textures", "Build VPB texture cache for selected .var packages (same as F3 for packages)");
            try
            {
                var cacheTextureIcon = UI.LoadIconSprite("vpb_icons/cache_texture.png", new Color(0.92f, 0.92f, 0.92f, 1f));
                if (cacheTextureIcon != null) UI.AddIconToButton(tboxCacheTexturesBtn, cacheTextureIcon, padding: 6f);
                else
                {
                    Text t = tboxCacheTexturesBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.cache_textures", "Cache Textures");
                }
            }
            catch { }

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
                var loadDepsIcon = UI.LoadIconSprite("vpb_icons/load_deps.png", new Color(0.92f, 0.92f, 0.92f, 1f));
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
                var unloadIcon = UI.LoadIconSprite("vpb_icons/unload.png", new Color(0.92f, 0.92f, 0.92f, 1f));
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
                var loadIcon = UI.LoadIconSprite("vpb_icons/load.png", new Color(0.92f, 0.92f, 0.92f, 1f));
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
                var delIcon = UI.LoadIconSprite("vpb_icons/delete.png", new Color(0.92f, 0.92f, 0.92f, 1f));
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

            tboxAutoInstallBtn = UI.CreateUIButton(
                tboxBtnRow0GO, 0, 0,
                "", tboxActionBtnFont,
                0, 0, AnchorPresets.stretchAll,
                TboxAutoInstallSelectedPackages
            );
            tboxAutoInstallBtn.name = "Tbox_AutoInstall";
            TboxConfigureActionButtonFlex(tboxAutoInstallBtn, innerRowH, innerRowH, innerRowH); // square icon button
            AddTooltip(tboxAutoInstallBtn, "gallery.tooltip.tbox_autoinstall", "Flag selected packages for auto-install and auto-load. Packages in AllPackages are copied to AddonPackages on the next VaM start (not immediately).");
            try
            {
                var autoLoadIcon = UI.LoadIconSprite("vpb_icons/auto.png", new Color(0.92f, 0.92f, 0.92f, 1f));
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
                var hideIcon = UI.LoadIconSprite("vpb_icons/show_hidden.png", new Color(0.92f, 0.92f, 0.92f, 1f));
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
                var unhideIcon = UI.LoadIconSprite("vpb_icons/show_hidden_off.png", new Color(0.92f, 0.92f, 0.92f, 1f));
                if (unhideIcon != null)
                    UI.AddIconToButton(tboxUnhideBtn, unhideIcon, padding: 6f);
                else
                {
                    Text t = tboxUnhideBtn.GetComponentInChildren<Text>(true);
                    if (t != null) t.text = VPBTranslation.T("gallery.tbox.unhide", "Unhide");
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
            AddTooltip(tboxDisableAutoInstallBtn, "gallery.tooltip.tbox_no_autoinstall", "Clear auto-install and VPB auto-load for selected packages");
            try
            {
                var autoLoadOffIcon = UI.LoadIconSprite("vpb_icons/auto_off.png", new Color(0.92f, 0.92f, 0.92f, 1f));
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
                innerPaneScaleActions.Add(s =>
                {
                    float rowH = 60f * s;
                    tboxInfoRowHeight = rowH;
                    if (lRT != null) lRT.sizeDelta = new Vector2(lRT.sizeDelta.x, rowH);
                    if (bRT != null) bRT.anchoredPosition = new Vector2(0f, rowH);
                    if (pRT != null) pRT.sizeDelta = new Vector2(44f * s, 44f * s);
                    try { TboxSetAllFlexActionButtonHeights(Mathf.Max(34f, rowH - 8f)); } catch { }
                    try { RefreshTboxFlexButtonLayout(); } catch { }
                });
            }

            if (VPBConfig.Instance != null)
                tboxPinned = VPBConfig.Instance.GalleryTboxToolbarPinned;
            RefreshTboxPinVisual();
            AddTooltip(tboxPinBtn, "gallery.tooltip.tbox_pin", "Pin — keep toolbar expanded");
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

            // Action buttons only when there is a selection; pin persists until user toggles (saved in VPB.cfg).
            bool canExpand = sel > 0;
            if (!canExpand)
            {
                tboxExpandT = 0f;
                tboxIsHovered = false;
                tboxButtonLayoutRows = 1;
                if (tboxButtonsLayerRT != null)
                    tboxButtonsLayerRT.sizeDelta = new Vector2(tboxButtonsLayerRT.sizeDelta.x, tboxInfoRowHeight);
            }

            if (tboxHintLabel != null && tboxHintLabel.gameObject != null)
            {
                bool showPinnedHint = sel == 0 && tboxPinned;
                tboxHintLabel.gameObject.SetActive(showPinnedHint);
                if (showPinnedHint)
                    tboxHintLabel.text = VPBTranslation.T("gallery.tbox.pinned_select", "Pinned — select items for actions");
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
                tboxButtonsCG.alpha = showButtons ? 1f : 0f;
                tboxButtonsCG.blocksRaycasts = canExpand && tboxExpandT > 0.5f;
                tboxButtonsCG.interactable = canExpand && tboxExpandT > 0.85f;
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
            bool anyPkgInstalled = false;     // in AddonPackages
            bool anyPkgNotInstalled = false;  // in AllPackages
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
                    if (!TryGetTboxResolvablePackageState(f, out string uid, out FileEntry diskFe, out bool hidden, out bool fiAi, out bool uidAl))
                        continue;
                    if (!seen.Add(uid)) continue;
                    if (hidden) unhideN++;
                    else hideN++;
                    if (fiAi || uidAl) noAiN++;
                    if (!fiAi || !uidAl) aiN++;

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
            }

            if (tboxCopyPkgNamesBtn != null)
                SetTboxCountButtonLabel(tboxCopyPkgNamesBtn, "gallery.tbox.copy_names_count", "Copy Names ({0})", copyN);
            // Delete is an icon button; count is intentionally not shown on the label.

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
