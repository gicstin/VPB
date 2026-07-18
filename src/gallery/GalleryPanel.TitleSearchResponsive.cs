using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        /// <summary>Gaps between neighbouring title-bar controls (× scale).</summary>
        private const float TitleBarChromeElementGapRef = 8f;
        /// <summary>Gap categoryΓåöcontrols and controlsΓåöfps pack.</summary>
        private const float TitleBarChromeSectionGapRef = 12f;
        /// <summary>Padding before close button hits window edge.</summary>
        private const float TitleBarChromeEndMarginRef = 10f;
        /// <summary>Usable inner width below this (├ù inner pane scale) switches to compact search icon.</summary>
        private const float TitleSearchCollapseWidthPx = 128f;
        private const float TitleBarCategoryClampMaxRef = 260f;
        private const float TitleBarCategoryClampMinRef = 120f;
        /// <summary>Preferred labeled category width when space allows (no void-filling).</summary>
        private const float TitleBarCategoryPreferredRef = 168f;
        private const float TitleSearchFieldMaxWidthRef = 240f;
        private const float TitleSearchPopupDismissAfterAwaySeconds = 0.72f;
        private const float TitleSearchPopupVicinityInflateScreenPx = 18f;

        /// <summary>
        /// Pin left group flush left (category · source · settings · language; overflow: category · settings · …).
        /// Mid group (remaining filters · search · sort) centers between pin end and FPS/right pack.
        /// </summary>
        private void ApplyTitleBarResponsiveLayout(float paneScale)
        {
            if (titleSearchInput == null || backgroundBoxGO == null) return;
            float s = paneScale <= 0f ? 1f : paneScale;

            RectTransform titleBarRT = titleSearchInput.transform.parent as RectTransform;
            if (titleBarRT == null) return;

            float W = titleBarRT.rect.width;
            if (W < 8f)
                return;

            bool hasSourceFilter = globalSourceFilterBtn != null;
            float fpsWRead = (_titleBarFpsRT != null) ? Mathf.Max(_titleBarFpsRT.rect.width, 72f * s) : 100f * s;
            bool overflowMode = TitleBarUsesOverflowMenu(hasSourceFilter, W, s);
            bool catShown = _categoryQuickChromeRootGO != null && _categoryQuickChromeRootGO.activeSelf;
            bool flushLeftInset = CategoryQuickSwitchFlushLeftEdge();

            const float widthEps = 0.5f;
            if (!float.IsNaN(_titleBarLayoutLastScale)
                && Mathf.Abs(_titleBarLayoutLastScale - s) < 0.0001f
                && Mathf.Abs(_titleBarLayoutLastW - W) < widthEps
                && Mathf.Abs(_titleBarLayoutLastFpsW - fpsWRead) < widthEps
                && _titleBarLayoutLastOverflow == overflowMode
                && _titleBarLayoutLastCatShown == catShown
                && _titleBarLayoutLastFlushLeft == flushLeftInset
                && _titleBarLayoutLastHasSource == hasSourceFilter)
            {
                return;
            }

            _titleBarLayoutLastScale = s;
            _titleBarLayoutLastW = W;
            _titleBarLayoutLastFpsW = fpsWRead;
            _titleBarLayoutLastOverflow = overflowMode;
            _titleBarLayoutLastCatShown = catShown;
            _titleBarLayoutLastFlushLeft = flushLeftInset;
            _titleBarLayoutLastHasSource = hasSourceFilter;

            try { RescaleTitleBarChromeInternal(UiMetrics); } catch { }

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(titleBarRT);
                Canvas.ForceUpdateCanvases();
            }
            catch { }

            W = titleBarRT.rect.width;
            if (W < 8f)
                return;
            fpsWRead = (_titleBarFpsRT != null) ? Mathf.Max(_titleBarFpsRT.rect.width, 72f * s) : 100f * s;
            overflowMode = TitleBarUsesOverflowMenu(hasSourceFilter, W, s);
            _titleBarLayoutLastW = W;
            _titleBarLayoutLastFpsW = fpsWRead;
            _titleBarLayoutLastOverflow = overflowMode;

            float halfW = W * 0.5f;
            float g = TitleBarChromeElementGapRef * s;
            float sec = TitleBarChromeSectionGapRef * s;
            float endM = TitleBarChromeEndMarginRef * s;
            float chip = GalleryUiDesignTokens.TitleBarChipRef * s;
            float halfChip = chip * 0.5f;
            float leftInset = flushLeftInset ? 0f : GalleryUiDesignTokens.TitleBarTitleLeftInsetRef * s;

            int sortCount = overflowMode ? 3 : 4;
            float sortSpan = sortCount * chip + (sortCount - 1) * g;

            float rp = endM + chip + g + chip + g + chip + (overflowMode ? 0f : (g + fpsWRead));
            float rightPackLeft = halfW - rp;

            float sourceFullW = GlobalSourceFilterButtonWidth * s;
            float catLabeledW = Mathf.Clamp(TitleBarCategoryPreferredRef * s,
                TitleBarCategoryClampMinRef * s, TitleBarCategoryClampMaxRef * s);

            bool categoryCompact = !catShown ? false : overflowMode;
            bool sourceCompact = false;
            float sourceW = sourceFullW;
            float catW = 0f;

            for (int pass = 0; pass < 4; pass++)
            {
                catW = !catShown ? 0f : (categoryCompact ? chip : catLabeledW);
                sourceW = (!hasSourceFilter || overflowMode) ? 0f : (sourceCompact ? chip : sourceFullW);
                float lp = TitleBarLeftPackWidthEstimate(overflowMode, hasSourceFilter && !overflowMode, sourceW, chip, g);
                float need = leftInset + catW
                    + (catShown ? sec : 0f)
                    + lp + g + chip + g + sortSpan
                    + sec + rp;
                if (need <= W + 0.5f)
                    break;
                if (!categoryCompact && catShown)
                    categoryCompact = true;
                else if (!sourceCompact && hasSourceFilter && !overflowMode)
                    sourceCompact = true;
                else
                    break;
            }

            try { SetCategoryQuickCompactMode(catShown && categoryCompact, s); } catch { }
            try { SetGlobalSourceFilterCompactMode(sourceCompact, s); } catch { }

            if (_categoryQuickChromeRootRT != null && catShown)
            {
                float catH = GalleryUiDesignTokens.TitleBarChipRef * s;
                _categoryQuickChromeRootRT.sizeDelta = new Vector2(catW, catH);
                _categoryQuickChromeRootRT.anchoredPosition = new Vector2(leftInset, 0f);
            }

            RectTransform langRT = null;
            if (languageSwitcherBtnGO != null)
                langRT = languageSwitcherBtnGO.GetComponent<RectTransform>();

            // ── Right pack (flush right) ──────────────────────────────────────
            float xRight = halfW - endM;
            float xc = xRight - halfChip;
            if (_titleBarCloseBtnRT != null)
                _titleBarCloseBtnRT.anchoredPosition = new Vector2(xc, 0f);
            xRight = xc - halfChip - g;
            xc = xRight - halfChip;
            if (_titleBarMinimizeBtnRT != null)
                _titleBarMinimizeBtnRT.anchoredPosition = new Vector2(xc, 0f);
            xRight = xc - halfChip - g;
            xc = xRight - halfChip;
            if (_titleBarHelpBtnRT != null)
                _titleBarHelpBtnRT.anchoredPosition = new Vector2(xc, 0f);
            xRight = xc - halfChip;
            if (!overflowMode)
            {
                xRight -= g;
                xc = xRight - fpsWRead * 0.5f;
                if (_titleBarFpsRT != null)
                    _titleBarFpsRT.anchoredPosition = new Vector2(xc, 0f);
                rightPackLeft = xc - fpsWRead * 0.5f;
            }
            else
            {
                rightPackLeft = xRight;
            }

            // ── Left pin flush left ───────────────────────────────────────────
            // Normal: category · source · settings · language. Overflow: category · settings · …
            float xl = -halfW + leftInset;
            int pinned = 0;
            const int LeftPinCount = 4;

            if (catShown)
            {
                xl += catW;
                pinned++;
                if (pinned < LeftPinCount) xl += g;
            }

            bool sourcePinned = false;
            if (pinned < LeftPinCount && !overflowMode && hasSourceFilter && globalSourceFilterBtn != null)
            {
                RectTransform sourceRT = globalSourceFilterBtn.GetComponent<RectTransform>();
                if (sourceRT != null)
                {
                    sourceRT.anchoredPosition = new Vector2(xl + sourceW * 0.5f, 0f);
                    xl += sourceW;
                    pinned++;
                    sourcePinned = true;
                    if (pinned < LeftPinCount) xl += g;
                }
            }

            bool settingsPinned = false;
            if (pinned < LeftPinCount && _titleBarSettingsBtnRT != null)
            {
                _titleBarSettingsBtnRT.anchoredPosition = new Vector2(xl + halfChip, 0f);
                xl += chip;
                pinned++;
                settingsPinned = true;
                if (pinned < LeftPinCount) xl += g;
            }

            bool languagePinned = false;
            float languagePinX = 0f;
            if (pinned < LeftPinCount && !overflowMode && langRT != null)
            {
                languagePinX = xl + halfChip;
                langRT.anchoredPosition = new Vector2(languagePinX, 0f);
                xl += chip;
                pinned++;
                languagePinned = true;
                if (pinned < LeftPinCount) xl += g;
            }

            // Overflow “…” fills remaining pin slots when mid filters are hidden.
            float overflowPinX = xl;
            bool overflowInPin = false;
            if (overflowMode && pinned < LeftPinCount && _titleBarOverflowBtnGO != null)
            {
                overflowInPin = true;
                overflowPinX = xl;
                xl += chip;
                pinned++;
            }

            float leftPinEnd = xl;
            float midZoneLeft = leftPinEnd + sec;
            float midZoneRight = rightPackLeft - sec;

            // Sync overflow visibility / non-pin overflow button; pin placement overrides X when needed.
            float overflowCursor = overflowInPin ? overflowPinX : midZoneLeft;
            try { overflowMode = ApplyTitleBarOverflowLayout(s, W, overflowCursor, chip, g, ref overflowCursor); } catch { }
            if (overflowInPin && _titleBarOverflowBtnRT != null)
                _titleBarOverflowBtnRT.anchoredPosition = new Vector2(overflowPinX + halfChip, 0f);
            if (languagePinned && langRT != null && languageSwitcherBtnGO != null && languageSwitcherBtnGO.activeSelf)
                langRT.anchoredPosition = new Vector2(languagePinX, 0f);

            // ── Mid group width (filters after pin · search · sort) ───────────
            int midFilterCount = 0;
            if (!overflowMode)
            {
                if (!sourcePinned && hasSourceFilter) midFilterCount++;
                if (!settingsPinned && _titleBarSettingsBtnRT != null) midFilterCount++;
                if (!languagePinned && langRT != null) midFilterCount++;
                if (_titleBarQfToggleBtnRT != null) midFilterCount++;
                if (titleCreatorBtn != null) midFilterCount++;
            }
            else if (!overflowInPin && _titleBarOverflowBtnGO != null)
            {
                midFilterCount++;
            }

            float midFiltersSpan = midFilterCount <= 0
                ? 0f
                : midFilterCount * chip + (midFilterCount - 1) * g;
            // Source in mid (rare): replace one chip with sourceW.
            if (!overflowMode && !sourcePinned && hasSourceFilter)
                midFiltersSpan += sourceW - chip;

            // Mid strip: [filters][g][search][g][sort], centered in zone before FPS/right pack.
            float midAvail = Mathf.Max(0f, midZoneRight - midZoneLeft);
            float gapsAroundSearch = (midFiltersSpan > 0f ? g : 0f) + (sortSpan > 0f ? g : 0f);
            float searchBudget = Mathf.Max(0f, midAvail - midFiltersSpan - sortSpan - gapsAroundSearch);

            float iconW = chip;
            bool useCompact =
                searchBudget < TitleSearchCollapseWidthPx * s - 0.5f ||
                searchBudget + 0.5f < iconW;
            float wSearch;
            if (useCompact)
            {
                wSearch = Mathf.Min(iconW, searchBudget);
                if (wSearch < iconW * 0.75f)
                    wSearch = Mathf.Max(wSearch, Mathf.Min(iconW, searchBudget));
            }
            else
            {
                wSearch = Mathf.Clamp(searchBudget, iconW, TitleSearchFieldMaxWidthRef * s);
                if (wSearch < iconW)
                {
                    useCompact = true;
                    wSearch = Mathf.Min(iconW, Mathf.Max(0f, searchBudget));
                }
            }

            float midGroupW = midFiltersSpan
                + (midFiltersSpan > 0f ? g : 0f)
                + wSearch
                + (sortSpan > 0f ? g : 0f)
                + sortSpan;

            float midStart = midZoneLeft + Mathf.Max(0f, (midAvail - midGroupW) * 0.5f);
            float xm = midStart;

            if (!overflowMode)
            {
                if (!sourcePinned && hasSourceFilter && globalSourceFilterBtn != null)
                {
                    RectTransform sourceRT = globalSourceFilterBtn.GetComponent<RectTransform>();
                    if (sourceRT != null)
                    {
                        sourceRT.anchoredPosition = new Vector2(xm + sourceW * 0.5f, 0f);
                        xm += sourceW + g;
                    }
                }
                if (!settingsPinned && _titleBarSettingsBtnRT != null)
                {
                    _titleBarSettingsBtnRT.anchoredPosition = new Vector2(xm + halfChip, 0f);
                    xm += chip + g;
                }
                if (!languagePinned && langRT != null)
                {
                    langRT.anchoredPosition = new Vector2(xm + halfChip, 0f);
                    xm += chip + g;
                }
                if (_titleBarQfToggleBtnRT != null)
                {
                    _titleBarQfToggleBtnRT.anchoredPosition = new Vector2(xm + halfChip, 0f);
                    xm += chip + g;
                }
                if (titleCreatorBtn != null)
                {
                    RectTransform crt = titleCreatorBtn.GetComponent<RectTransform>();
                    if (crt != null)
                    {
                        crt.anchoredPosition = new Vector2(xm + halfChip, 0f);
                        xm += chip + g;
                    }
                }
            }
            else if (!overflowInPin && _titleBarOverflowBtnRT != null && _titleBarOverflowBtnGO != null
                     && _titleBarOverflowBtnGO.activeSelf)
            {
                _titleBarOverflowBtnRT.anchoredPosition = new Vector2(xm + halfChip, 0f);
                xm += chip + g;
            }

            float cxSearch = xm + wSearch * 0.5f;
            RectTransform searchRT = titleSearchInput.GetComponent<RectTransform>();
            if (useCompact)
            {
                if (titleSearchInput.gameObject.activeSelf)
                    titleSearchInput.gameObject.SetActive(false);
                if (_titleSearchCompactGO != null)
                {
                    _titleSearchCompactGO.SetActive(true);
                    if (_titleSearchCompactRT != null)
                    {
                        float compactSz = GalleryUiDesignTokens.TitleBarChipRef * s;
                        _titleSearchCompactRT.anchoredPosition = new Vector2(cxSearch, 0f);
                        _titleSearchCompactRT.sizeDelta = new Vector2(compactSz, compactSz);
                        ScaleButtonIconPadding(_titleSearchCompactRT, s);
                    }
                }
            }
            else
            {
                CloseTitleSearchPopup();
                if (_titleSearchCompactGO != null)
                    _titleSearchCompactGO.SetActive(false);
                titleSearchInput.gameObject.SetActive(true);
                if (searchRT != null)
                {
                    searchRT.sizeDelta = new Vector2(wSearch, GalleryUiDesignTokens.TitleBarChipRef * s);
                    searchRT.anchoredPosition = new Vector2(cxSearch, 0f);
                }
                RescaleSearchInput(titleSearchInput, s, GalleryUiDesignTokens.TitleBarChipRef);
            }
            xm += wSearch + g;

            if (_titleBarFileSortTypeBtnRT != null)
            {
                _titleBarFileSortTypeBtnRT.anchoredPosition = new Vector2(xm + halfChip, 0f);
                xm += chip + g;
            }
            if (_titleBarFileSortDirBtnRT != null)
            {
                _titleBarFileSortDirBtnRT.anchoredPosition = new Vector2(xm + halfChip, 0f);
                xm += chip + g;
            }
            if (!overflowMode && _titleBarRatingSortToggleBtnRT != null)
            {
                _titleBarRatingSortToggleBtnRT.anchoredPosition = new Vector2(xm + halfChip, 0f);
                xm += chip + g;
            }
            if (_titleBarRefreshBtnRT != null)
                _titleBarRefreshBtnRT.anchoredPosition = new Vector2(xm + halfChip, 0f);

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
            _titleSearchCompactGO = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, "", 18, 0, 0, AnchorPresets.middleCenter, () => OpenTitleSearchPopup());
            _titleSearchCompactGO.name = "TitleSearchCompact";
            _titleSearchCompactGO.SetActive(false);
            RectTransform crt = _titleSearchCompactGO.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef);
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
            try { AddTooltip(_titleSearchCompactGO, "gallery.search.main", "Search grid (name/path)."); } catch { }
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

            _titleSearchPopupRootGO = UI.CreateChildRT(backgroundBoxGO, "TitleSearchPopupBackdrop", AnchorPresets.stretchAll);
            Image rootImg = UI.AddImage(_titleSearchPopupRootGO, new Color(0f, 0f, 0f, 0f), false);

            GameObject panel = new GameObject("TitleSearchPopupPanel");
            panel.transform.SetParent(_titleSearchPopupRootGO.transform, false);
            _titleSearchPopupPanelRT = panel.AddComponent<RectTransform>();
            _titleSearchPopupPanelRT.anchorMin = new Vector2(0.5f, 1f);
            _titleSearchPopupPanelRT.anchorMax = new Vector2(0.5f, 1f);
            _titleSearchPopupPanelRT.pivot = new Vector2(0.5f, 1f);
            Image pbg = UI.AddImage(panel, new Color(0.14f, 0.14f, 0.16f, 1f));

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
            _titleSearchPopupPanelRT.sizeDelta = new Vector2(pw, GalleryUiDesignTokens.TitleBarChipRef * s + 10f);
            float popupX = (_titleSearchCompactRT != null && _titleSearchCompactGO != null && _titleSearchCompactGO.activeSelf)
                ? _titleSearchCompactRT.anchoredPosition.x
                : 0f;
            float halfPw = pw * 0.5f;
            float halfBw = bw * 0.5f;
            popupX = Mathf.Clamp(popupX, -halfBw + halfPw + 4f * s, halfBw - halfPw - 4f * s);
            _titleSearchPopupPanelRT.anchoredPosition = new Vector2(popupX, -GalleryUiDesignTokens.TitleBarHeightRef * s - 6f);

            RectTransform ifrt = _titleSearchPopupField.GetComponent<RectTransform>();
            ifrt.sizeDelta = new Vector2(pw - 12f * s, GalleryUiDesignTokens.TitleBarChipRef * s);
            RescaleSearchInput(_titleSearchPopupField, s, GalleryUiDesignTokens.TitleBarChipRef);

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
