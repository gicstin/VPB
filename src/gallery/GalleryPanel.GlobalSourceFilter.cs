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
        private const int GlobalSourceFilterButtonHeight = 40;
        private const int GlobalSourceFilterDropdownWidth = 160;
        private const int GlobalSourceFilterDropdownRowHeight = 36;
        private const int GlobalSourceFilterDropdownPadding = 8;
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
                globalSourceFilterBtnText.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform btnRT = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<RectTransform>() : null;
            if (btnRT != null)
            {
                btnRT.anchorMin = new Vector2(0.5f, 0.5f);
                btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot = new Vector2(0.5f, 0.5f);
                // Initial X is a one-frame fallback before ApplyTitleBarResponsiveLayout reassigns based on current title-bar width.
                btnRT.anchoredPosition = new Vector2(GlobalSourceFilterButtonCenterRelativeX, 0f);
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

            // Matches the pattern used by settings/lang/qf/creator chips: scale action handles sizeDelta + fontSize only.
            // Anchored position is set every frame by ApplyTitleBarResponsiveLayout as part of the left-pack sweep.
            {
                var rt = btnRT;
                var txt = globalSourceFilterBtnText;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt != null) rt.sizeDelta = new Vector2(GlobalSourceFilterButtonWidth * s, GlobalSourceFilterButtonHeight * s);
                    if (txt != null) txt.fontSize = Mathf.RoundToInt(16 * s);
                });
            }
        }

        private void BuildGlobalSourceFilterDropdown(GameObject backgroundBoxGO)
        {
            // Invisible click-outside blocker. Active only while dropdown is open.
            globalSourceFilterDropdownBlocker = new GameObject("GlobalSourceFilterBlocker");
            globalSourceFilterDropdownBlocker.transform.SetParent(backgroundBoxGO.transform, false);
            {
                RectTransform rt = globalSourceFilterDropdownBlocker.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                Image img = globalSourceFilterDropdownBlocker.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.001f);
                img.raycastTarget = true;
                Button blockerBtn = globalSourceFilterDropdownBlocker.AddComponent<Button>();
                blockerBtn.targetGraphic = img;
                blockerBtn.transition = Selectable.Transition.None;
                blockerBtn.onClick.AddListener(() => HideGlobalSourceFilterDropdown());
            }
            globalSourceFilterDropdownBlocker.SetActive(false);

            // Dropdown root
            globalSourceFilterDropdown = new GameObject("GlobalSourceFilterDropdown");
            globalSourceFilterDropdown.transform.SetParent(backgroundBoxGO.transform, false);
            globalSourceFilterDropdown.transform.SetAsLastSibling();

            RectTransform ddRT = globalSourceFilterDropdown.AddComponent<RectTransform>();
            // Anchor top-center of the gallery panel; offset to match the button's center-relative X.
            // The dropdown's top-center sits directly below the source button.
            ddRT.anchorMin = new Vector2(0.5f, 1f);
            ddRT.anchorMax = new Vector2(0.5f, 1f);
            ddRT.pivot = new Vector2(0.5f, 1f);
            ddRT.anchoredPosition = new Vector2(GlobalSourceFilterButtonCenterRelativeX, -70f);
            ddRT.sizeDelta = new Vector2(GlobalSourceFilterDropdownWidth,
                                          GlobalSourceFilterDropdownRowHeight * 3 + GlobalSourceFilterDropdownPadding * 2);

            Image ddImg = globalSourceFilterDropdown.AddComponent<Image>();
            ddImg.color = new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f);

            // No child Canvas / overrideSorting / SuperController.AddCanvas. Earlier attempts at all three
            // either left the popup behind gallery rows in VR (overrideSorting unreliable for nested WorldSpace
            // canvases) or broke raycast (z-position offset). Matching TitleCreatorDropdown's pattern: stay in
            // the parent gallery canvas, rely on hierarchy sibling order (SetAsLastSibling on show) to render
            // above rows. Within a single canvas, sibling order is the render order.

            // Three rows
            globalSourceFilterRowAllCountText   = AddGlobalSourceFilterRow(globalSourceFilterDropdown, 0, "All",   VPBConfig.GlobalSourceFilterValue.All);
            globalSourceFilterRowLocalCountText = AddGlobalSourceFilterRow(globalSourceFilterDropdown, 1, "Local", VPBConfig.GlobalSourceFilterValue.Local);
            globalSourceFilterRowVarCountText   = AddGlobalSourceFilterRow(globalSourceFilterDropdown, 2, ".var",  VPBConfig.GlobalSourceFilterValue.Var);

            // Root sizeDelta tracks pane scale. Position is re-synced to the current button X on every Show call,
            // because the responsive title-bar layout repositions the button per frame as panel width changes.
            {
                var rt = ddRT;
                innerPaneScaleActions.Add(s =>
                {
                    if (rt != null)
                        rt.sizeDelta = new Vector2(GlobalSourceFilterDropdownWidth * s,
                                                   (GlobalSourceFilterDropdownRowHeight * 3 + GlobalSourceFilterDropdownPadding * 2) * s);
                });
            }

            globalSourceFilterDropdown.SetActive(false);
        }

        private Text AddGlobalSourceFilterRow(GameObject parent, int rowIndex, string label, VPBConfig.GlobalSourceFilterValue value)
        {
            GameObject row = new GameObject("Row_" + label);
            row.transform.SetParent(parent.transform, false);

            RectTransform rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(GlobalSourceFilterDropdownPadding + rowIndex * GlobalSourceFilterDropdownRowHeight));
            rt.sizeDelta = new Vector2(-GlobalSourceFilterDropdownPadding * 2, GlobalSourceFilterDropdownRowHeight);

            Image rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(0f, 0f, 0f, 0.0f);
            rowImg.raycastTarget = true;

            Button rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = rowImg;
            ColorBlock cb = rowBtn.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 0f);
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
            cb.pressedColor = new Color(1f, 1f, 1f, 0.16f);
            cb.disabledColor = cb.normalColor;
            rowBtn.colors = cb;
            rowBtn.onClick.AddListener(() => OnGlobalSourceFilterRowClicked(value));

            // Label on the left
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(row.transform, false);
            Text labelText = labelGO.AddComponent<Text>();
            VPBUiFont.ApplyTo(labelText);
            labelText.text = label;
            labelText.fontSize = 16;
            labelText.color = Color.white;
            labelText.alignment = TextAnchor.MiddleLeft;
            labelText.raycastTarget = false;
            RectTransform labelRT = labelGO.GetComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(0.5f, 1f);
            labelRT.offsetMin = new Vector2(8f, 0f);
            labelRT.offsetMax = new Vector2(0f, 0f);

            // Count on the right
            GameObject countGO = new GameObject("Count");
            countGO.transform.SetParent(row.transform, false);
            Text countText = countGO.AddComponent<Text>();
            VPBUiFont.ApplyTo(countText);
            countText.text = "";
            countText.fontSize = 14;
            countText.color = new Color(1f, 1f, 1f, 0.75f);
            countText.alignment = TextAnchor.MiddleRight;
            countText.horizontalOverflow = HorizontalWrapMode.Overflow;
            countText.raycastTarget = false;
            RectTransform countRT = countGO.GetComponent<RectTransform>();
            countRT.anchorMin = new Vector2(0.5f, 0f);
            countRT.anchorMax = new Vector2(1f, 1f);
            countRT.offsetMin = new Vector2(0f, 0f);
            countRT.offsetMax = new Vector2(-8f, 0f);

            // Per-row scale action: positions/size + label/count fonts and side paddings all track pane scale,
            // so the popup keeps the same visual proportions as the rest of the title-bar cluster.
            {
                var rRT = rt;
                var lRT = labelRT;
                var lT = labelText;
                var cRT = countRT;
                var cT = countText;
                int idx = rowIndex;
                innerPaneScaleActions.Add(s =>
                {
                    if (rRT != null)
                    {
                        rRT.anchoredPosition = new Vector2(0f, -(GlobalSourceFilterDropdownPadding + idx * GlobalSourceFilterDropdownRowHeight) * s);
                        rRT.sizeDelta = new Vector2(-GlobalSourceFilterDropdownPadding * 2 * s, GlobalSourceFilterDropdownRowHeight * s);
                    }
                    if (lRT != null) lRT.offsetMin = new Vector2(8f * s, 0f);
                    if (lT != null) lT.fontSize = Mathf.RoundToInt(16 * s);
                    if (cRT != null) cRT.offsetMax = new Vector2(-8f * s, 0f);
                    if (cT != null) cT.fontSize = Mathf.RoundToInt(14 * s);
                });
            }

            return countText;
        }

        private void ToggleGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterDropdown == null) return;
            if (globalSourceFilterDropdown.activeSelf) HideGlobalSourceFilterDropdown();
            else ShowGlobalSourceFilterDropdown();
        }

        private void ShowGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterDropdown == null) return;
            RecomputeGlobalSourceFilterRowCounts();
            // Sync popup X to whatever X the responsive layout last assigned to the button (panel width / scale
            // changes move the button between frames). Y is the scaled drop distance below the title bar.
            SyncGlobalSourceFilterDropdownPositionToButton();
            // Blocker first (last sibling = above gallery rows, absorbs raycasts behind the dropdown),
            // then dropdown above the blocker. Matches TitleCreatorDropdown's working VR pattern.
            if (globalSourceFilterDropdownBlocker != null)
            {
                globalSourceFilterDropdownBlocker.SetActive(true);
                try { globalSourceFilterDropdownBlocker.transform.SetAsLastSibling(); } catch { }
            }
            try { globalSourceFilterDropdown.transform.SetAsLastSibling(); } catch { }
            globalSourceFilterDropdown.SetActive(true);
        }

        private void SyncGlobalSourceFilterDropdownPositionToButton()
        {
            if (globalSourceFilterBtn == null || globalSourceFilterDropdown == null) return;
            RectTransform btnRT = globalSourceFilterBtn.GetComponent<RectTransform>();
            RectTransform ddRT = globalSourceFilterDropdown.GetComponent<RectTransform>();
            if (btnRT == null || ddRT == null) return;
            float s = 1f;
            try { if (VPBConfig.Instance != null) s = VPBConfig.Instance.CurrentInnerPaneScale; } catch { }
            if (s <= 0f) s = 1f;
            ddRT.anchoredPosition = new Vector2(btnRT.anchoredPosition.x, -70f * s);
        }

        private void HideGlobalSourceFilterDropdown()
        {
            if (globalSourceFilterDropdown == null) return;
            globalSourceFilterDropdown.SetActive(false);
            if (globalSourceFilterDropdownBlocker != null) globalSourceFilterDropdownBlocker.SetActive(false);
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
                LogUtil.Log("[VPB] Global source filter set to Local; cleared creator filter.");
            }

            UpdateGlobalSourceFilterButtonLabel();
            HideGlobalSourceFilterDropdown();
            RefreshFilesAndTabs();
        }

        private void UpdateGlobalSourceFilterButtonLabel()
        {
            if (globalSourceFilterBtnText == null) return;
            string label;
            switch (currentGlobalSourceFilter)
            {
                case VPBConfig.GlobalSourceFilterValue.Local: label = "Source: Local"; break;
                case VPBConfig.GlobalSourceFilterValue.Var:   label = "Source: .var";  break;
                default:                                       label = "Source: All";   break;
            }
            globalSourceFilterBtnText.text = label;

            // Accent tint when filter is non-default so the user can see at a glance that a filter is active.
            Image backdrop = globalSourceFilterBtn != null ? globalSourceFilterBtn.GetComponent<Image>() : null;
            if (backdrop != null)
            {
                bool active = currentGlobalSourceFilter != VPBConfig.GlobalSourceFilterValue.All;
                backdrop.color = active ? new Color(0.2f, 0.4f, 0.7f, 0.85f) : new Color(0f, 0f, 0f, 0.5f);
            }
        }

        private void RecomputeGlobalSourceFilterRowCounts()
        {
            if (globalSourceFilterRowAllCountText == null) return;

            // Walk the current filtered file list and bucket by isVar. Counts reflect the user's current
            // category + filter context. When the global filter is non-All, the "other" bucket appears as
            // 0 because items outside the active source are not in lastFilteredFiles. Sum still equals the
            // visible row count, so users can compare visible-now vs picking-another-mode at a glance.
            int allCount = 0, localCount = 0, varCount = 0;
            if (lastFilteredFiles != null)
            {
                for (int i = 0; i < lastFilteredFiles.Count; i++)
                {
                    FileEntry e = lastFilteredFiles[i];
                    if (e == null) continue;
                    allCount++;
                    if (IsVarBacked(e)) varCount++;
                    else localCount++;
                }
            }

            globalSourceFilterRowAllCountText.text   = "(" + allCount + ")";
            globalSourceFilterRowLocalCountText.text = "(" + localCount + ")";
            globalSourceFilterRowVarCountText.text   = "(" + varCount + ")";
        }

        // Called from category-switch handlers, settings-panel open, etc. to dismiss the dropdown.
        public void HideGlobalSourceFilterDropdownIfOpen()
        {
            if (globalSourceFilterDropdown != null && globalSourceFilterDropdown.activeSelf)
                HideGlobalSourceFilterDropdown();
        }

        // Source-of-truth helper. Used by RecomputeGlobalSourceFilterRowCounts here and by the
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
