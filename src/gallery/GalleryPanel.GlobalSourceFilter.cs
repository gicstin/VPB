using System;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel
    {
        // Widget for the global source filter. Lives in the title bar to the right of the category
        // quick-switch widget. Three rows: All / Local / .var. Persisted to VPBConfig.GlobalSourceFilter.
        // VR-safe overlay uses the same Canvas overrideSorting pattern as CategoryQuickSwitch with
        // a sortingOrder floor of 6000 to survive the gallery canvas's -10000 baseline.

        private const int GlobalSourceFilterButtonWidth = 100;
        private const float GlobalSourceFilterButtonHeight = GalleryUiDesignTokens.TitleBarChipRef;
        // Title-bar center-relative X for the button. Sits in the right-side button cluster, just left of
        // the settings gear (which is at -324). Center-anchored so it tracks the cluster as the panel resizes
        // rather than colliding with the left-anchored category chrome on wider gallery widths.
        private const float GlobalSourceFilterButtonCenterRelativeX = -398f;

        public void SetupGlobalSourceFilterDropdown(GameObject titleBarGO, GameObject backgroundBoxGO)
        {
            if (titleBarGO == null || backgroundBoxGO == null) return;

            // Sync runtime mirror with persisted config before building UI so the button label is correct.
            if (VPBConfig.Instance != null)
                currentGlobalSourceFilter = VPBConfig.Instance.GlobalSourceFilter;

            BuildGlobalSourceFilterButton(titleBarGO);
            BuildGlobalSourceFilterDropdown(backgroundBoxGO);
            UpdateGlobalSourceFilterButtonLabel();
        }

        private void BuildGlobalSourceFilterButton(GameObject titleBarGO)
        {
            globalSourceFilterBtn = UI.CreateUIButton(
                titleBarGO,
                GlobalSourceFilterButtonWidth,
                GlobalSourceFilterButtonHeight,
                "Source: All",
                16, 0, 0,
                AnchorPresets.middleCenter,
                null);

            Image backdrop = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<Image>() : null;
            if (backdrop != null) backdrop.color = new Color(0f, 0f, 0f, 0.5f);

            globalSourceFilterBtnText = globalSourceFilterBtn != null
                ? globalSourceFilterBtn.GetComponentInChildren<Text>(true)
                : null;
            if (globalSourceFilterBtnText != null)
            {
                // Stay inside chip without RectMask2D (mask clipped UIHoverBorder rim top/bottom).
                globalSourceFilterBtnText.horizontalOverflow = HorizontalWrapMode.Wrap;
                globalSourceFilterBtnText.verticalOverflow = VerticalWrapMode.Truncate;
                globalSourceFilterBtnText.alignment = TextAnchor.MiddleCenter;
                RectTransform textRT = globalSourceFilterBtnText.rectTransform;
                textRT.anchorMin = Vector2.zero;
                textRT.anchorMax = Vector2.one;
                textRT.offsetMin = new Vector2(6f, 2f);
                textRT.offsetMax = new Vector2(-6f, -2f);
            }

            RectTransform btnRT = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<RectTransform>() : null;
            if (btnRT != null)
            {
                btnRT.anchorMin = new Vector2(0.5f, 0.5f);
                btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot = new Vector2(0.5f, 0.5f);
                // Initial X is a one-frame fallback before ApplyTitleBarResponsiveLayout reassigns based on current title-bar width.
                btnRT.anchoredPosition = new Vector2(GlobalSourceFilterButtonCenterRelativeX, 0f);
            }
            // Remove legacy mask if present — it clipped the yellow hover rim.
            if (globalSourceFilterBtn != null)
            {
                RectMask2D staleMask = globalSourceFilterBtn.GetComponent<RectMask2D>();
                if (staleMask != null) UnityEngine.Object.Destroy(staleMask);
            }

            Button btn = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<Button>() : null;
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => ToggleGlobalSourceFilterDropdown());
            }

            if (globalSourceFilterBtn != null)
            {
                var rc = globalSourceFilterBtn.AddComponent<UIRightClickDelegate>();
                rc.OnRightClick = () =>
                {
                    if (currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All)
                        OnGlobalSourceFilterRowClicked(VPBConfig.GlobalSourceFilterValue.All);
                };
            }

            AddTooltip(globalSourceFilterBtn, "gallery.tooltip.global_source_filter",
                "Source filter (All / Local / .var). Applies to every category. (Right-click to reset to All)");

            // Compact: package glyph (not filter.png — that is filter-presets). Tabler "package".
            try
            {
                Sprite sp = UI.LoadIconSprite("vpb_icons/gallery_source.png", UI.BarIconGlyphTint);
                if (sp != null && globalSourceFilterBtn != null)
                {
                    GameObject iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(globalSourceFilterBtn.transform, false);
                    globalSourceFilterBtnIcon = UI.AddImage(iconGO, Color.white, false);
                    globalSourceFilterBtnIcon.sprite = sp;
                    globalSourceFilterBtnIcon.preserveAspect = true;
                    RectTransform irt = iconGO.GetComponent<RectTransform>();
                    irt.anchorMin = Vector2.zero;
                    irt.anchorMax = Vector2.one;
                    float pad = GalleryUiDesignTokens.SearchIconButtonPadRef;
                    irt.sizeDelta = new Vector2(-pad * 2f, -pad * 2f);
                    irt.anchoredPosition = Vector2.zero;
                    iconGO.SetActive(false);
                }
            }
            catch { }

            // Matches settings/lang/qf/creator chips: layout only — fonts via RescaleTitleBarChromeInternal.
            {
                var rt = btnRT;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt == null) return;
                    float w = _globalSourceFilterCompact
                        ? GalleryUiDesignTokens.TitleBarChipRef * s
                        : GlobalSourceFilterButtonWidth * s;
                    rt.sizeDelta = new Vector2(w, GlobalSourceFilterButtonHeight * s);
                });
            }
        }

        /// <summary>Narrow title bar: icon-only chip. Wide: labeled "Source: …" button.</summary>
        private void SetGlobalSourceFilterCompactMode(bool compact, float paneScale)
        {
            if (paneScale <= 0f) paneScale = 1f;
            _globalSourceFilterCompact = compact;

            if (globalSourceFilterBtnText != null)
            {
                if (globalSourceFilterBtnText.gameObject.activeSelf == compact)
                    globalSourceFilterBtnText.gameObject.SetActive(!compact);
                globalSourceFilterBtnText.horizontalOverflow = HorizontalWrapMode.Wrap;
                globalSourceFilterBtnText.verticalOverflow = VerticalWrapMode.Truncate;
            }

            if (globalSourceFilterBtnIcon != null)
            {
                GameObject iconGO = globalSourceFilterBtnIcon.gameObject;
                if (iconGO.activeSelf != compact)
                    iconGO.SetActive(compact);
            }

            if (globalSourceFilterBtn == null) return;
            RectTransform rt = globalSourceFilterBtn.GetComponent<RectTransform>();
            if (rt == null) return;
            float w = compact
                ? GalleryUiDesignTokens.TitleBarChipRef * paneScale
                : GlobalSourceFilterButtonWidth * paneScale;
            float h = GlobalSourceFilterButtonHeight * paneScale;
            rt.sizeDelta = new Vector2(w, h);
            if (compact)
                ScaleButtonIconPadding(rt, paneScale);
        }

        private void BuildGlobalSourceFilterDropdown(GameObject backgroundBoxGO)
        {
            // Same CreatePopupMenuRoot / CreatePopupMenuPanel path as language + sort menus.
            globalSourceFilterMenuRoot = UI.CreatePopupMenuRoot(backgroundBoxGO, "GlobalSourceFilterMenu", HideGlobalSourceFilterDropdown);
            globalSourceFilterMenuRoot.SetActive(false);

            globalSourceFilterMenuPanelGO = UI.CreatePopupMenuPanel(
                globalSourceFilterMenuRoot,
                "GlobalSourceFilterMenuPanel",
                AnchorPresets.topMiddle,
                new Vector2(GalleryUiDesignTokens.PopupMenuPanelWidthRef, 50f),
                new Vector2(GlobalSourceFilterButtonCenterRelativeX, -72f));
        }

        private void ToggleGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterMenuRoot == null) return;
            if (globalSourceFilterMenuRoot.activeSelf) HideGlobalSourceFilterDropdown();
            else ShowGlobalSourceFilterDropdown();
        }

        private void ShowGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterMenuRoot == null) return;
            RebuildGlobalSourceFilterMenuOptions();
            try { RescaleGlobalSourceFilterMenuInternal(ChromeScale); } catch { }
            try { globalSourceFilterMenuRoot.transform.SetAsLastSibling(); } catch { }
            globalSourceFilterMenuRoot.SetActive(true);
        }

        private void HideGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterMenuRoot == null) return;
            globalSourceFilterMenuRoot.SetActive(false);
        }

        private void RebuildGlobalSourceFilterMenuOptions()
        {
            if (globalSourceFilterMenuPanelGO == null) return;

            UI.DestroyAllChildren(globalSourceFilterMenuPanelGO.transform);

            int allCount = 0, localCount = 0, varCount = 0;
            ComputeGlobalSourceFilterRowCounts(out allCount, out localCount, out varCount);

            AddGlobalSourceFilterMenuRow(
                VPBConfig.GlobalSourceFilterValue.All,
                "All",
                allCount,
                currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.All);
            AddGlobalSourceFilterMenuRow(
                VPBConfig.GlobalSourceFilterValue.Local,
                "Local",
                localCount,
                currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Local);
            AddGlobalSourceFilterMenuRow(
                VPBConfig.GlobalSourceFilterValue.Var,
                ".var",
                varCount,
                currentGlobalSourceFilter == VPBConfig.GlobalSourceFilterValue.Var);

            try { RescaleGlobalSourceFilterMenuInternal(ChromeScale); } catch { }
            LayoutRebuilder.ForceRebuildLayoutImmediate(globalSourceFilterMenuPanelGO.GetComponent<RectTransform>());
        }

        private void AddGlobalSourceFilterMenuRow(
            VPBConfig.GlobalSourceFilterValue value,
            string name,
            int count,
            bool isActive)
        {
            // Language/sort pattern: checkmark prefix + left-aligned rounded PopupRow chrome.
            string label = (isActive ? "\u2713  " : "    ") + name + " (" + count + ")";
            UI.AddPopupMenuRow(
                globalSourceFilterMenuPanelGO,
                GalleryUiDesignTokens.PopupMenuPanelWidthRef - 12f,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                label,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                isActive,
                () => OnGlobalSourceFilterRowClicked(value),
                GalleryUiDesignTokens.PopupMenuRowHeightRef);
        }

        private void RescaleGlobalSourceFilterMenuInternal(float s)
        {
            if (globalSourceFilterMenuPanelGO == null) return;
            if (s <= 0f) s = 1f;

            ScaleVerticalPopupMenuRows(
                globalSourceFilterMenuPanelGO,
                s,
                GalleryUiDesignTokens.PopupMenuRowHeightRef,
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                GalleryUiDesignTokens.PopupMenuPanelWidthRef);

            RectTransform panelRT = globalSourceFilterMenuPanelGO.GetComponent<RectTransform>();
            if (panelRT == null) return;
            panelRT.anchorMin = new Vector2(0.5f, 1f);
            panelRT.anchorMax = new Vector2(0.5f, 1f);
            panelRT.pivot = new Vector2(0.5f, 1f);
            if (globalSourceFilterBtn == null) return;
            RectTransform btnRT = globalSourceFilterBtn.GetComponent<RectTransform>();
            if (btnRT == null) return;
            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            panelRT.anchoredPosition = new Vector2(
                btnRT.anchoredPosition.x,
                -(GalleryUiDesignTokens.TitleBarHeightRef + gap) * s);
        }

        private void OnGlobalSourceFilterRowClicked(VPBConfig.GlobalSourceFilterValue value)
        {
            if (currentGlobalSourceFilter == value)
            {
                HideGlobalSourceFilterDropdown();
                return;
            }

            currentGlobalSourceFilter = value;
            if (VPBConfig.Instance != null)
            {
                VPBConfig.Instance.GlobalSourceFilter = value;
                try { VPBConfig.Instance.Save(); } catch { /* swallow: save failures get logged elsewhere */ }
            }

            // Mutual reset with Creator filter (spec Section 6). Picking Local clears the creator filter;
            // picking .var or All leaves it intact.
            if (value == VPBConfig.GlobalSourceFilterValue.Local && HasCreatorFilter())
            {
                ClearCreatorFilters();
                try { UpdateTitleCreatorButtonVisual(); } catch { }
                LogUtil.Log("[VPB] Global source filter set to Local; cleared creator filter.");
            }

            UpdateGlobalSourceFilterButtonLabel();
            HideGlobalSourceFilterDropdown();
            RefreshFilesAndTabs();
            SyncBrowseFilterChipChrome();
        }

        private void UpdateGlobalSourceFilterButtonLabel()
        {
            string label;
            switch (currentGlobalSourceFilter)
            {
                case VPBConfig.GlobalSourceFilterValue.Local: label = "Source: Local"; break;
                case VPBConfig.GlobalSourceFilterValue.Var:   label = "Source: .var";  break;
                default:                                       label = "Source: All";   break;
            }
            if (globalSourceFilterBtnText != null)
                globalSourceFilterBtnText.text = label;

            // Accent tint when filter is non-default so the user can see at a glance that a filter is active.
            Image backdrop = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<Image>() : null;
            if (backdrop != null)
            {
                bool active = currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All;
                backdrop.color = active ? new Color(0.2f, 0.4f, 0.7f, 0.85f) : new Color(0f, 0f, 0f, 0.5f);
            }
        }

        private void ComputeGlobalSourceFilterRowCounts(out int allCount, out int localCount, out int varCount)
        {
            // Walk the current filtered file list and bucket by isVar. Counts reflect the user's current
            // category + filter context. When the global filter is non-All, the "other" bucket appears as
            // 0 because items outside the active source are not in lastFilteredFiles. Sum still equals the
            // visible row count, so users can compare visible-now vs picking-another-mode at a glance.
            allCount = 0;
            localCount = 0;
            varCount = 0;
            if (lastFilteredFiles == null) return;
            for (int i = 0; i < lastFilteredFiles.Count; i++)
            {
                FileEntry e = lastFilteredFiles[i];
                if (e == null) continue;
                allCount++;
                if (IsVarBacked(e)) varCount++;
                else localCount++;
            }
        }

        // Called from category-switch handlers, settings-panel open, etc. to dismiss the dropdown.
        public void HideGlobalSourceFilterDropdownIfOpen()
        {
            if (globalSourceFilterMenuRoot != null && globalSourceFilterMenuRoot.activeSelf)
                HideGlobalSourceFilterDropdown();
        }

        // Source-of-truth helper. Used by ComputeGlobalSourceFilterRowCounts here and by the
        // early gate in GalleryPanel.IO.cs PassesFilters. Partial class lets PassesFilters call it directly.
        internal static bool IsVarBacked(FileEntry entry)
        {
            if (entry == null) return false;
            // PackageListEntry: whole-VAR row (Scenes/Clothing/Pose use these for var packages). Always var-backed.
            // MissingPackageListEntry: placeholder row for an unresolved package reference; still var-backed.
            // VarFileEntry: single file inside a var. Always var-backed.
            // SystemFileEntry.isVar: loose-on-disk row that represents a packaged file (virtualized var path).
            if (entry is PackageListEntry) return true;
            if (entry is MissingPackageListEntry) return true;
            if (entry is VarFileEntry) return true;
            SystemFileEntry sfe = entry as SystemFileEntry;
            return sfe != null && sfe.isVar;
        }
    }
}
