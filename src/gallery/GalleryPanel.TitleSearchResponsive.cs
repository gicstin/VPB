using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        /// <remarks>Reserved above category / around centre strip.</remarks>
        private const float TitleBarResponsiveGap = 6f;
        /// <summary>Gaps between neighbouring title-bar controls (┬▒ scale).</summary>
        private const float TitleBarChromeElementGapRef = 8f;
        /// <summary>Gap categoryΓåöcontrols and controlsΓåöfps pack.</summary>
        private const float TitleBarChromeSectionGapRef = 12f;
        /// <summary>Padding before close button hits window edge.</summary>
        private const float TitleBarChromeEndMarginRef = 10f;
        /// <summary>Usable inner width below this (├ù inner pane scale) switches to compact search icon.</summary>
        private const float TitleSearchCollapseWidthPx = 128f;
        private const float TitleBarCategoryClampMaxRef = 260f;
        private const float TitleBarCategoryClampMinRef = 120f;
        private const float TitleSearchFieldMaxWidthRef = 240f;
        private const float TitleSearchPopupDismissAfterAwaySeconds = 0.42f;
        private const float TitleSearchPopupVicinityInflateScreenPx = 18f;

        /// <summary>
        /// Order: category ΓÇö sectionGap ΓÇö settings, language, presets, creator ΓÇö search ΓÇö sort type, sort dir, Γÿà, Γƒ│ ΓÇö sectionGap ΓÇö FPS, minimise, close.
        /// Sizes scale with inner pane factor <paramref name="paneScale"/>.
        /// </summary>
        private void ApplyTitleBarResponsiveLayout(float paneScale)
        {
            if (titleSearchInput == null || backgroundBoxGO == null) return;
            float s = paneScale <= 0f ? 1f : paneScale;

            RectTransform titleBarRT = titleSearchInput.transform.parent as RectTransform;
            if (titleBarRT == null) return;

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(titleBarRT);
                Canvas.ForceUpdateCanvases();
            }
            catch { }

            float W = titleBarRT.rect.width;
            if (W < 8f)
                return;

            float halfW = W * 0.5f;
            float g = TitleBarChromeElementGapRef * s;
            float sec = TitleBarChromeSectionGapRef * s;
            float endM = TitleBarChromeEndMarginRef * s;

            float chip = GalleryUiDesignTokens.TitleBarChipRef * s;
            float halfChip = chip * 0.5f;

            // Global source filter button is wider than the standard chip, so it's tracked separately in the
            // left-pack math instead of being lumped into the uniform chip-count formula.
            bool hasSourceFilter = globalSourceFilterBtn != null;
            float sourceW = GlobalSourceFilterButtonWidth * s;
            float halfSourceW = sourceW * 0.5f;

            float fpsWRead = (_titleBarFpsRT != null) ? Mathf.Max(_titleBarFpsRT.rect.width, 72f * s) : 100f * s;

            bool overflowMode = TitleBarUsesOverflowMenu(hasSourceFilter, W, s);

            // Right pack widths: close ΓÇª help ΓÇª min ΓÇª FPS (+ end inset)
            float rp = endM + chip + g + chip + g + chip + (overflowMode ? 0f : (g + fpsWRead));

            // Left pack after section: settings + (overflow OR lang/presets/creator/source cluster).
            float lpSpan = TitleBarLeftPackWidthEstimate(overflowMode, hasSourceFilter, sourceW, chip, g);

            // Compact search worst-case: search + sort cluster (+ Γÿà when not overflow).
            float midMin = overflowMode
                ? (chip * 4f + g * 3f)
                : (chip * 5f + g * 4f);

            bool flushLeftInset = CategoryQuickSwitchFlushLeftEdge();
            float leftInset = flushLeftInset ? 0f : GalleryUiDesignTokens.TitleBarTitleLeftInsetRef * s;

            float reservesNoCat = sec + lpSpan + g + midMin + sec + rp;
            float catSpaceEff = W - leftInset - reservesNoCat - TitleBarResponsiveGap * s;

            bool catShown = _categoryQuickChromeRootGO != null && _categoryQuickChromeRootGO.activeSelf;
            if (_categoryQuickChromeRootRT != null && catShown)
            {
                float effCatW = Mathf.Clamp(catSpaceEff, TitleBarCategoryClampMinRef * s, TitleBarCategoryClampMaxRef * s);
                _categoryQuickChromeRootRT.sizeDelta = new Vector2(effCatW, GalleryUiDesignTokens.TitleBarCategoryRowHeightRef * s);

                try
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(titleBarRT);
                    Canvas.ForceUpdateCanvases();
                }
                catch { }
            }

            RectTransform langRT = null;
            if (languageSwitcherBtnGO != null)
                langRT = languageSwitcherBtnGO.GetComponent<RectTransform>();

            // ΓÇöΓÇö Right anchors: FPS, help, minimise, close ΓÇö place from RIGHT edge inward
            float xRight = halfW - endM;
            float xc = xRight - halfChip;
            if (_titleBarCloseBtnRT != null)
                _titleBarCloseBtnRT.anchoredPosition = new Vector2(xc, 0f);
            xRight = xc - halfChip;
            xRight -= g;
            xc = xRight - halfChip;
            if (_titleBarMinimizeBtnRT != null)
                _titleBarMinimizeBtnRT.anchoredPosition = new Vector2(xc, 0f);
            xRight = xc - halfChip;
            xRight -= g;
            xc = xRight - halfChip;
            if (_titleBarHelpBtnRT != null)
                _titleBarHelpBtnRT.anchoredPosition = new Vector2(xc, 0f);
            xRight = xc - halfChip;
            xRight -= g;

            xc = xRight - fpsWRead * 0.5f;
            if (_titleBarFpsRT != null)
                _titleBarFpsRT.anchoredPosition = new Vector2(xc, 0f);

            float rightPackLeftFace = xc - fpsWRead * 0.5f;
            float boundaryRefreshRight = rightPackLeftFace - sec;

            // ΓÇöΓÇö Refresh, Γÿà, sort dir, sort type: coords from FPS boundary (apply center shift later).
            float rSweep = boundaryRefreshRight;
            xc = rSweep - halfChip;
            float xfRefresh = xc;
            rSweep = xc - halfChip - g;
            float xfStar = 0f;
            if (!overflowMode)
            {
                xc = rSweep - halfChip;
                xfStar = xc;
                rSweep = xc - halfChip - g;
            }
            xc = rSweep - halfChip;
            float xfSortDir = xc;
            rSweep = xc - halfChip - g;
            xc = rSweep - halfChip;
            float xfSortType = xc;
            rSweep = xc - halfChip - g;

            float searchAvailRightBoundary = rSweep;

            float catTrailingX = (-halfW + leftInset);
            if (catShown && _categoryQuickChromeRootRT != null)
            {
                Bounds cqb = RectTransformUtility.CalculateRelativeRectTransformBounds(titleBarRT, _categoryQuickChromeRootRT);
                catTrailingX = cqb.max.x;
            }

            float LminAfterCat = catTrailingX + sec;
            float R = searchAvailRightBoundary;

            // Uniform chip-and-gap baseline, plus the extra width the source filter contributes over a standard chip.
            float leftPackSpan = TitleBarLeftPackWidthEstimate(overflowMode, hasSourceFilter, sourceW, chip, g);
            float availForSearchFloor = Mathf.Max(0f, R - LminAfterCat - leftPackSpan - g);
            float iconW = chip;
            bool useCompact =
                availForSearchFloor < TitleSearchCollapseWidthPx * s - 0.5f ||
                availForSearchFloor + 0.5f < iconW;

            float wSearch;
            float xlPackStart;

            RectTransform searchRT = titleSearchInput.GetComponent<RectTransform>();
            float cxSearch;

            float xlSticky;
            if (useCompact)
            {
                wSearch = iconW;
                xlSticky = Mathf.Max(LminAfterCat, R - wSearch - leftPackSpan);
            }
            else
            {
                wSearch = Mathf.Clamp(availForSearchFloor, iconW, TitleSearchFieldMaxWidthRef * s);
                xlSticky = Mathf.Max(LminAfterCat, R - wSearch - leftPackSpan);
                float squeezed = Mathf.Max(0f, R - xlSticky - leftPackSpan);
                if (squeezed < iconW)
                {
                    useCompact = true;
                    wSearch = iconW;
                    xlSticky = Mathf.Max(LminAfterCat, R - wSearch - leftPackSpan);
                }
                else if (squeezed < wSearch)
                {
                    wSearch = squeezed;
                    xlSticky = Mathf.Max(LminAfterCat, R - wSearch - leftPackSpan);
                }
            }

            float slackLeftSticky = Mathf.Max(0f, xlSticky - LminAfterCat);
            float stripCenterShift = slackLeftSticky * 0.5f;
            xlPackStart = xlSticky - stripCenterShift;

            xfSortType -= stripCenterShift;
            xfSortDir -= stripCenterShift;
            if (!overflowMode) xfStar -= stripCenterShift;
            xfRefresh -= stripCenterShift;

            if (_titleBarRefreshBtnRT != null)
                _titleBarRefreshBtnRT.anchoredPosition = new Vector2(xfRefresh, 0f);
            if (!overflowMode && _titleBarRatingSortToggleBtnRT != null)
                _titleBarRatingSortToggleBtnRT.anchoredPosition = new Vector2(xfStar, 0f);
            if (_titleBarFileSortTypeBtnRT != null)
                _titleBarFileSortTypeBtnRT.anchoredPosition = new Vector2(xfSortType, 0f);
            if (_titleBarFileSortDirBtnRT != null)
                _titleBarFileSortDirBtnRT.anchoredPosition = new Vector2(xfSortDir, 0f);

            float xl = xlPackStart;
            if (!overflowMode && hasSourceFilter)
            {
                RectTransform sourceRT = globalSourceFilterBtn.GetComponent<RectTransform>();
                if (sourceRT != null)
                {
                    sourceRT.anchoredPosition = new Vector2(xl + halfSourceW, 0f);
                    xl += sourceW + g;
                }
            }
            if (_titleBarSettingsBtnRT != null)
            {
                _titleBarSettingsBtnRT.anchoredPosition = new Vector2(xl + halfChip, 0f);
                xl += chip + g;
            }
            try { overflowMode = ApplyTitleBarOverflowLayout(s, W, xlPackStart, chip, g, ref xl); } catch { }
            if (!overflowMode)
            {
                if (langRT != null)
                {
                    langRT.anchoredPosition = new Vector2(xl + halfChip, 0f);
                    xl += chip + g;
                }
                if (_titleBarQfToggleBtnRT != null)
                {
                    _titleBarQfToggleBtnRT.anchoredPosition = new Vector2(xl + halfChip, 0f);
                    xl += chip + g;
                }
                if (titleCreatorBtn != null)
                {
                    RectTransform crt = titleCreatorBtn.GetComponent<RectTransform>();
                    if (crt != null)
                    {
                        crt.anchoredPosition = new Vector2(xl + halfChip, 0f);
                        xl += chip + g;
                    }
                }
            }

            // Place search after left pack using measured end + sort-type left boundary (prevents chip overlap).
            float searchZoneLeft = xl;
            float searchZoneRight = xfSortType - halfChip - g;
            float availSearch = Mathf.Max(0f, searchZoneRight - searchZoneLeft);
            useCompact = availSearch < TitleSearchCollapseWidthPx * s - 0.5f || availSearch + 0.5f < iconW;
            if (useCompact)
            {
                wSearch = Mathf.Min(iconW, Mathf.Max(0f, availSearch));
                if (wSearch < iconW * 0.75f) wSearch = Mathf.Max(wSearch, Mathf.Min(iconW, availSearch));
            }
            else
            {
                wSearch = Mathf.Clamp(availSearch, iconW, TitleSearchFieldMaxWidthRef * s);
                if (wSearch < iconW)
                {
                    useCompact = true;
                    wSearch = Mathf.Min(iconW, Mathf.Max(0f, availSearch));
                }
            }

            cxSearch = searchZoneLeft + wSearch * 0.5f;
            float searchHalf = wSearch * 0.5f;
            if (cxSearch + searchHalf > searchZoneRight)
                cxSearch = searchZoneRight - searchHalf;
            if (cxSearch - searchHalf < searchZoneLeft)
                cxSearch = searchZoneLeft + searchHalf;
            if (useCompact)
            {
                if (titleSearchInput.gameObject.activeSelf)
                    titleSearchInput.gameObject.SetActive(false);
                if (_titleSearchCompactGO != null)
                {
                    _titleSearchCompactGO.SetActive(true);
                    if (_titleSearchCompactRT != null)
                    {
                        _titleSearchCompactRT.anchoredPosition = new Vector2(cxSearch, 0f);
                        _titleSearchCompactRT.sizeDelta = new Vector2(Mathf.Max(wSearch, iconW * 0.85f), 40f * s);
                    }
                }
            }
            else
            {
                CloseTitleSearchPopup();
                if (_titleSearchCompactGO != null)
                    _titleSearchCompactGO.SetActive(false);
                titleSearchInput.gameObject.SetActive(true);
                searchRT.sizeDelta = new Vector2(wSearch, GalleryUiDesignTokens.TitleBarChipRef * s);
                searchRT.anchoredPosition = new Vector2(cxSearch, 0f);
                RescaleSearchInput(titleSearchInput, s);
            }

            try { SyncTitleBarSearchBackdrop(); } catch { }
        }

        /// <summary>Title search field + compact icon: grey when empty; blue when query non-empty.</summary>
        private void SyncTitleBarSearchBackdrop()
        {
            if (IsSettingsPanelOpen() || settingsListViewActive)
            {
                try { SyncTitleSearchChromeForActiveMode(); } catch { }
                return;
            }
            if (titleSearchInput == null) return;
            string tSearch = titleSearchInput.text ?? "";
            bool hasTerm = tSearch.Trim().Length > 0;
            Color c = hasTerm ? ColorTitleSearchFilterActive : ColorTitleSearchBackdropIdle;
            Image fieldBg = titleSearchInput.GetComponent<Image>();
            if (fieldBg != null) fieldBg.color = c;
            if (_titleSearchCompactGO != null)
            {
                Image cmpBg = _titleSearchCompactGO.GetComponent<Image>();
                if (cmpBg != null) cmpBg.color = c;
            }
        }

        private void SetupTitleSearchCompactControl(GameObject titleBarGO)
        {
            if (titleBarGO == null) return;
            _titleSearchCompactGO = UI.CreateUIButton(titleBarGO, 40, 40, "", 18, 0, 0, AnchorPresets.middleCenter, () => OpenTitleSearchPopup());
            _titleSearchCompactGO.name = "TitleSearchCompact";
            _titleSearchCompactGO.SetActive(false);
            RectTransform crt = _titleSearchCompactGO.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(40, 40);
            try
            {
                var sp = UI.LoadIconSprite("vpb_icons/search.png", UI.BarIconGlyphTint);
                if (sp != null) UI.AddIconToButton(_titleSearchCompactGO, sp, padding: 8f, ColorTitleSearchBackdropIdle);
            }
            catch { }
            var hb = _titleSearchCompactGO.GetComponent<UIHoverBorder>();
            if (hb != null)
            {
                hb.hoverColor = Color.white;
                try { hb.ApplyBorderSettings(); } catch { }
            }
            var compactIconImg = _titleSearchCompactGO.transform.Find("Icon")?.GetComponent<Image>();
            if (compactIconImg != null) compactIconImg.color = Color.white;
            _titleSearchCompactRT = crt;
            try { AddTooltip(_titleSearchCompactGO, "gallery.search.main", "Search..."); } catch { }
            AddRightClickDelegate(_titleSearchCompactGO, ClearTitleBarSearch);
        }

        private void ClearTitleBarSearch()
        {
            if (titleSearchInput == null) return;
            string cur = titleSearchInput.text ?? "";
            if (string.IsNullOrEmpty(nameFilter) && cur.Trim().Length == 0) return;

            try { CloseTitleSearchPopup(); } catch { }
            if (_titleSearchPopupField != null)
            {
                try
                {
                    _suppressTitleBarSearchValueChanged = true;
                    _titleSearchPopupField.text = "";
                }
                finally { _suppressTitleBarSearchValueChanged = false; }
            }
            try { SetTitleSearchInputTextWithoutNotify(titleSearchInput, "", _titleBarSearchOnValueChanged); } catch { }
            SetNameFilter("");
            try { SyncBrowseFilterChipChrome(); } catch { }
            try { UpdateEmptyGridState(); } catch { }
        }

        private void EnsureTitleSearchPopupBuilt()
        {
            if (_titleSearchPopupRootGO != null || backgroundBoxGO == null || titleSearchInput == null) return;

            _titleSearchPopupRootGO = new GameObject("TitleSearchPopupBackdrop");
            _titleSearchPopupRootGO.transform.SetParent(backgroundBoxGO.transform, false);
            RectTransform rootRT = _titleSearchPopupRootGO.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;
            Image rootImg = _titleSearchPopupRootGO.AddComponent<Image>();
            rootImg.color = new Color(0f, 0f, 0f, 0f);
            rootImg.raycastTarget = false;

            GameObject panel = new GameObject("TitleSearchPopupPanel");
            panel.transform.SetParent(_titleSearchPopupRootGO.transform, false);
            _titleSearchPopupPanelRT = panel.AddComponent<RectTransform>();
            _titleSearchPopupPanelRT.anchorMin = new Vector2(0.5f, 1f);
            _titleSearchPopupPanelRT.anchorMax = new Vector2(0.5f, 1f);
            _titleSearchPopupPanelRT.pivot = new Vector2(0.5f, 1f);
            Image pbg = panel.AddComponent<Image>();
            pbg.color = new Color(0.14f, 0.14f, 0.16f, 1f);
            pbg.raycastTarget = true;

            float w0 = 320f;
            _titleSearchPopupField = CreateSearchInput(panel, w0, (val) =>
            {
                if (_suppressTitleBarSearchValueChanged) return;
                _titleBarSearchOnValueChanged?.Invoke(val);
            });
            RectTransform ifrt = _titleSearchPopupField.GetComponent<RectTransform>();
            ifrt.anchorMin = new Vector2(0.5f, 0.5f);
            ifrt.anchorMax = new Vector2(0.5f, 0.5f);
            ifrt.pivot = new Vector2(0.5f, 0.5f);
            ifrt.anchoredPosition = Vector2.zero;

            _titleSearchPopupRootGO.SetActive(false);
        }

        private void OpenTitleSearchPopup()
        {
            if (titleSearchInput == null || backgroundBoxGO == null) return;
            EnsureTitleSearchPopupBuilt();
            if (_titleSearchPopupRootGO == null || _titleSearchPopupField == null || _titleSearchPopupPanelRT == null) return;
            if (_titleSearchPopupOpen && _titleSearchPopupRootGO.activeSelf) return;

            float s = ChromeScale;
            RectTransform bgRT = backgroundBoxGO.GetComponent<RectTransform>();
            float bw = bgRT != null ? bgRT.rect.width : 600f;
            float pw = Mathf.Clamp(Mathf.Min(288f * s, bw - 36f * s), 196f * s, 308f * s);

            _titleSearchPopupProximityAwayTimer = 0f;
            _titleSearchPopupPanelRT.sizeDelta = new Vector2(pw, 44f * s + 10f);
            _titleSearchPopupPanelRT.anchoredPosition = new Vector2(0f, -70f * s - 6f);

            RectTransform ifrt = _titleSearchPopupField.GetComponent<RectTransform>();
            ifrt.sizeDelta = new Vector2(pw - 12f * s, 40f * s);

            string t = titleSearchInput.text ?? "";
            try
            {
                _suppressTitleBarSearchValueChanged = true;
                _titleSearchPopupField.text = t;
            }
            finally { _suppressTitleBarSearchValueChanged = false; }

            _titleSearchPopupRootGO.transform.SetAsLastSibling();
            _titleSearchPopupRootGO.SetActive(true);
            _titleSearchPopupOpen = true;

            try { _titleSearchPopupField.ActivateInputField(); } catch { }
            try { _titleSearchPopupField.MoveTextEnd(false); } catch { }
        }

        private void CloseTitleSearchPopup()
        {
            if (!_titleSearchPopupOpen) return;
            _titleSearchPopupOpen = false;
            _titleSearchPopupProximityAwayTimer = 0f;
            if (_titleSearchPopupRootGO != null)
                _titleSearchPopupRootGO.SetActive(false);

            if (_titleSearchPopupField != null && titleSearchInput != null && _titleBarSearchOnValueChanged != null)
            {
                try
                {
                    SetTitleSearchInputTextWithoutNotify(titleSearchInput, _titleSearchPopupField.text ?? "", _titleBarSearchOnValueChanged);
                }
                catch { }
            }
        }

        private Camera TitleSearchUiRaycastCameraOrNull()
        {
            try
            {
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            }
            catch { }
            return null;
        }

        private static bool ScreenPointInRectTransformExpanded(RectTransform rt, Vector2 screenPoint, float inflateScreenPx, Camera cam)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            Vector3[] wc = new Vector3[4];
            rt.GetWorldCorners(wc);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, wc[i]);
                if (sp.x < minX) minX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y > maxY) maxY = sp.y;
            }
            float z = inflateScreenPx;
            return screenPoint.x >= minX - z && screenPoint.x <= maxX + z &&
                   screenPoint.y >= minY - z && screenPoint.y <= maxY + z;
        }

        private bool TitleSearchPopupPointerInVicinity(float paneScale)
        {
            Camera cam = TitleSearchUiRaycastCameraOrNull();
            Vector2 ptr;
            try { ptr = currentPointerData != null ? currentPointerData.position : (Vector2)Input.mousePosition; }
            catch { ptr = Input.mousePosition; }

            float s = paneScale <= 0f ? 1f : paneScale;
            float pad = TitleSearchPopupVicinityInflateScreenPx + 8f * s;
            bool hitCompact = _titleSearchCompactGO != null && _titleSearchCompactGO.activeSelf &&
                               _titleSearchCompactRT != null &&
                               ScreenPointInRectTransformExpanded(_titleSearchCompactRT, ptr, pad, cam);
            bool hitPanel = _titleSearchPopupPanelRT != null && _titleSearchPopupPanelRT.gameObject.activeSelf &&
                             ScreenPointInRectTransformExpanded(_titleSearchPopupPanelRT, ptr, pad, cam);
            return hitCompact || hitPanel;
        }

        private void TickTitleSearchPopupProximityDismiss(float paneScale)
        {
            if (!_titleSearchPopupOpen || _titleSearchPopupRootGO == null || !_titleSearchPopupRootGO.activeSelf)
            {
                _titleSearchPopupProximityAwayTimer = 0f;
                return;
            }
            if (!IsVisible || titleSearchInput == null)
            {
                _titleSearchPopupProximityAwayTimer = 0f;
                return;
            }
            if (TitleSearchPopupPointerInVicinity(paneScale))
                _titleSearchPopupProximityAwayTimer = 0f;
            else
            {
                _titleSearchPopupProximityAwayTimer += Time.unscaledDeltaTime;
                if (_titleSearchPopupProximityAwayTimer >= TitleSearchPopupDismissAfterAwaySeconds)
                    CloseTitleSearchPopup();
            }
        }
    }
}
