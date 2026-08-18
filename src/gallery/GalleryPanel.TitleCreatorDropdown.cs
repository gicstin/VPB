using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VPB
{
    public partial class GalleryPanel
    {
        private void SetupTitleCreatorFilterDropdown(GameObject titleBarGO, GameObject backgroundBoxGO)
        {
            if (titleBarGO == null || backgroundBoxGO == null) return;

            // Button (between filter presets and title search)
            titleCreatorBtn = UI.CreateUIButton(titleBarGO, GalleryUiDesignTokens.TitleBarChipRef, GalleryUiDesignTokens.TitleBarChipRef, " ", 16, 0, 0, AnchorPresets.middleCenter, null);
            titleCreatorBtnBackdrop = titleCreatorBtn != null ? titleCreatorBtn.GetComponent<Image>() : null;
            titleCreatorBtnText = titleCreatorBtn != null ? titleCreatorBtn.GetComponentInChildren<Text>(true) : null;
            if (titleCreatorBtnBackdrop != null) titleCreatorBtnBackdrop.color = new Color(0f, 0f, 0f, 0.5f);
            if (titleCreatorBtnText != null) { titleCreatorBtnText.text = " "; titleCreatorBtnText.gameObject.SetActive(false); }
            try
            {
                // galleryCreatorSprite loaded later in Init; load directly so title bar button always has icon.
                Sprite s = galleryCreatorSprite;
                if (s == null) s = UI.LoadIconSprite("user", UI.BarIconGlyphTint);
                if (s != null)
                {
                    UI.AddIconToButton(titleCreatorBtn, s, 4f, GalleryUiColorTokens.ChromeIconWell);
                    var iconT = titleCreatorBtn.transform.Find("Icon");
                    titleCreatorBtnIconImage = iconT != null ? iconT.GetComponent<Image>() : null;
                    if (titleCreatorBtnIconImage != null) titleCreatorBtnIconImage.color = UI.BarIconGlyphTint;
                }
            }
            catch { }

            RectTransform btnRT = titleCreatorBtn != null ? titleCreatorBtn.GetComponent<RectTransform>() : null;
            if (btnRT != null)
            {
                btnRT.anchorMin = new Vector2(0.5f, 0.5f);
                btnRT.anchorMax = new Vector2(0.5f, 0.5f);
                btnRT.pivot = new Vector2(0.5f, 0.5f);
                // Search is centered at x=-40, width=240 => left edge ~ -160. Put button left with small gap.
                // Between P (-228) and search left edge (-160): center -184.
                btnRT.anchoredPosition = new Vector2(-184, 0);
            }

            var btn = titleCreatorBtn != null ? titleCreatorBtn.GetComponent<Button>() : null;
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    ToggleTitleCreatorDropdown();
                });
            }

            var rightClick = titleCreatorBtn != null ? titleCreatorBtn.GetComponent<UIRightClickDelegate>() : null;
            if (titleCreatorBtn != null && rightClick == null) rightClick = titleCreatorBtn.AddComponent<UIRightClickDelegate>();
            if (rightClick != null)
            {
                rightClick.OnRightClick = () =>
                {
                    if (!HasCreatorFilter()) return;
                    ClearCreatorFilters();
                    OnCreatorFilterChanged(refreshFilesAndTabs: true);
                    HideTitleCreatorDropdown();
                };
            }

            AddTooltip(titleCreatorBtn, "gallery.tooltip.creator_filter", "Multi-select creators → filter grid. Right-click clear.");

            // Click-outside blocker (behind dropdown, above grid)
            titleCreatorDropdownBlocker = UI.CreateChildRT(backgroundBoxGO, "TitleCreatorDropdownBlocker", AnchorPresets.stretchAll);
            {
                var img = UI.AddImage(titleCreatorDropdownBlocker, new Color(0, 0, 0, 0));
            }
            {
                var blockerBtn = titleCreatorDropdownBlocker.AddComponent<Button>();
                blockerBtn.onClick.RemoveAllListeners();
                blockerBtn.onClick.AddListener(() => HideTitleCreatorDropdown());
            }
            titleCreatorDropdownBlocker.SetActive(false);

            // Dropdown root (hidden by default)
            // Below title bar (70px height) so it overlays grid items.
            titleCreatorDropdown = UI.CreateChildRT(backgroundBoxGO, "TitleCreatorDropdown", AnchorPresets.topMiddle, new Vector2(330, 500), new Vector2(-184, -70));
            titleCreatorDropdown.transform.SetAsLastSibling();

            var ddImg = UI.AddImage(titleCreatorDropdown, new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f));

            var ddCg = titleCreatorDropdown.AddComponent<CanvasGroup>();
            ddCg.blocksRaycasts = true;
            ddCg.interactable = true;

            // Rated-only filter chip (right of search) — same job as side-pane sort menu toggle.
            {
                titleCreatorRatedOnlyBtn = UI.CreateUIButton(titleCreatorDropdown, 32f, 32f, "★", 16, 0, 0, AnchorPresets.topRight, null);
                if (titleCreatorRatedOnlyBtn != null)
                {
                    RectTransform rrt = titleCreatorRatedOnlyBtn.GetComponent<RectTransform>();
                    if (rrt != null)
                    {
                        rrt.anchorMin = new Vector2(1f, 1f);
                        rrt.anchorMax = new Vector2(1f, 1f);
                        rrt.pivot = new Vector2(1f, 1f);
                        rrt.anchoredPosition = new Vector2(-8f, -10f);
                        rrt.sizeDelta = new Vector2(32f, 32f);
                    }
                    titleCreatorRatedOnlyBtnBackdrop = titleCreatorRatedOnlyBtn.GetComponent<Image>();
                    titleCreatorRatedOnlyBtnText = titleCreatorRatedOnlyBtn.GetComponentInChildren<Text>(true);
                    Button rb = titleCreatorRatedOnlyBtn.GetComponent<Button>();
                    if (rb != null)
                    {
                        rb.onClick.RemoveAllListeners();
                        rb.onClick.AddListener(ToggleCreatorRatedOnlyFilter);
                    }
                    AddTooltip(titleCreatorRatedOnlyBtn, "gallery.tooltip.creator_rated_only", "Rated creators only, sorted high→low.");
                    SyncTitleCreatorRatedOnlyButton();
                }
            }

            // Search input (top of dropdown)
            {
                var searchGO = CreateSearchInput(titleCreatorDropdown, 270f, (val) =>
                {
                    titleCreatorDropdownFilter = val ?? "";
                    RebuildTitleCreatorVirtView(force: true);
                    UpdateTitleCreatorVirtualVisible();
                }, () =>
                {
                    titleCreatorDropdownFilter = "";
                    try
                    {
                        if (titleCreatorDropdownSearchInput != null)
                            titleCreatorDropdownSearchInput.text = "";
                    }
                    catch { }
                    if (HasCreatorFilter())
                    {
                        ClearCreatorFilters();
                        OnCreatorFilterChanged(refreshFilesAndTabs: true);
                    }
                    else
                    {
                        RebuildTitleCreatorVirtView(force: true);
                        UpdateTitleCreatorVirtualVisible();
                    }
                });
                titleCreatorDropdownSearchInput = searchGO != null ? searchGO.GetComponent<InputField>() : null;
                var srt = searchGO != null ? searchGO.GetComponent<RectTransform>() : null;
                if (srt != null)
                {
                    srt.anchorMin = new Vector2(0.5f, 1f);
                    srt.anchorMax = new Vector2(0.5f, 1f);
                    srt.pivot = new Vector2(0.5f, 1f);
                    srt.anchoredPosition = new Vector2(-18f, -10);
                    srt.sizeDelta = new Vector2(270, 36);
                }
            }

            // Scroll view area
            {
                float scrollBarWidth = 18f;
                GameObject scrollGO = UI.CreateChildRT(titleCreatorDropdown, "Scroll", AnchorPresets.stretchAll);
                RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
                scrollRT.offsetMin = new Vector2(GalleryUiDesignTokens.ControlGapRef, GalleryUiDesignTokens.ControlGapRef);
                scrollRT.offsetMax = new Vector2(-GalleryUiDesignTokens.ControlGapRef, -54);

                var viewportGO = UI.CreateChildRT(scrollGO, "Viewport", AnchorPresets.stretchAll, new Vector2(-scrollBarWidth, 0), new Vector2(-scrollBarWidth / 2f, 0));
                var vpRT = viewportGO.GetComponent<RectTransform>();
                viewportGO.AddComponent<RectMask2D>();

                var contentGO = UI.CreateChildRT(viewportGO, "Content", AnchorPresets.hStretchTop);
                var contentRT = contentGO.GetComponent<RectTransform>();
                _titleCreatorVirtContentRT = contentRT;

                titleCreatorDropdownHolder = UI.CreateChildRT(contentGO, "Holder", AnchorPresets.hStretchTop);
                titleCreatorDropdownHolder.AddComponent<LayoutElement>();

                _titleCreatorVirtScroll = scrollGO.AddComponent<ScrollRect>();
                _titleCreatorVirtScroll.viewport = vpRT;
                _titleCreatorVirtScroll.content = contentRT;
                _titleCreatorVirtScroll.horizontal = false;
                _titleCreatorVirtScroll.vertical = true;
                _titleCreatorVirtScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                _titleCreatorVirtScroll.verticalScrollbar = null;
                _titleCreatorVirtScroll.movementType = ScrollRect.MovementType.Clamped;
                _titleCreatorVirtScroll.onValueChanged.AddListener(_ =>
                {
                    try { UpdateTitleCreatorVirtualVisible(); } catch { }
                });

                // Scrollbar (use same sync behaviour as main lists)
                try
                {
                    GameObject sbGO = UI.CreateScrollBar(scrollGO, scrollBarWidth, 0, Scrollbar.Direction.BottomToTop);
                    Scrollbar sb = sbGO != null ? sbGO.GetComponent<Scrollbar>() : null;
                    ScrollbarSync sync = sbGO != null ? sbGO.AddComponent<ScrollbarSync>() : null;
                    if (sync != null)
                    {
                        sync.scrollRect = _titleCreatorVirtScroll;
                        sync.scrollbar = sb;
                        sync.minSizePixels = 30f;
                    }
                }
                catch { }
            }

            titleCreatorDropdown.SetActive(false);
            try { UpdateTitleCreatorButtonVisual(); } catch { }

            innerPaneScaleActions.Add(s => ApplyTitleCreatorDropdownLayout(s));
        }

        internal void ApplyTitleCreatorDropdownLayout(float s)
        {
            if (titleCreatorDropdown == null) return;
            if (s <= 0f) s = 1f;
            RectTransform ddRT = titleCreatorDropdown.GetComponent<RectTransform>();
            if (ddRT != null)
            {
                float bgW = backgroundBoxGO != null
                    ? (backgroundBoxGO.GetComponent<RectTransform>()?.rect.width ?? 600f)
                    : 600f;
                float ddW = Mathf.Min(
                    GalleryUiDesignTokens.TitleCreatorDropdownWidthRef * s,
                    Mathf.Max(160f * s, bgW - 20f * s));
                float ddH = GalleryUiDesignTokens.TitleCreatorDropdownHeightRef * s;
                ddRT.sizeDelta = new Vector2(ddW, ddH);

                if (titleCreatorBtn != null)
                {
                    RectTransform btnRT = titleCreatorBtn.GetComponent<RectTransform>();
                    if (btnRT != null)
                    {
                        float halfDdW = ddW * 0.5f;
                        float halfBgW = bgW * 0.5f;
                        float clampedX = Mathf.Clamp(btnRT.anchoredPosition.x, -halfBgW + halfDdW + 4f * s, halfBgW - halfDdW - 4f * s);
                        ddRT.anchoredPosition = new Vector2(clampedX, -GalleryUiDesignTokens.TitleBarHeightRef * s);
                    }
                }
            }
            if (titleCreatorRatedOnlyBtn != null)
            {
                RectTransform rrt = titleCreatorRatedOnlyBtn.GetComponent<RectTransform>();
                if (rrt != null)
                {
                    float chip = 32f * s;
                    rrt.anchoredPosition = new Vector2(-8f * s, -10f * s);
                    rrt.sizeDelta = new Vector2(chip, chip);
                }
                if (titleCreatorRatedOnlyBtnText != null)
                    GalleryUiMetrics.ApplyFont(titleCreatorRatedOnlyBtnText, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }
            if (titleCreatorDropdownSearchInput != null)
            {
                RectTransform srt = titleCreatorDropdownSearchInput.GetComponent<RectTransform>();
                if (srt != null)
                {
                    // Keep search narrower than dropdown; leave room for rated-only chip on the right.
                    float ddWNow = ddRT != null ? ddRT.sizeDelta.x : GalleryUiDesignTokens.TitleCreatorDropdownSearchWidthRef * s + 20f * s;
                    float chipGap = 40f * s;
                    float searchW = Mathf.Max(100f * s, ddWNow - 20f * s - chipGap);
                    srt.anchoredPosition = new Vector2(-chipGap * 0.45f, -10f * s);
                    srt.sizeDelta = new Vector2(searchW, GalleryUiDesignTokens.SearchFieldHeightRef * s);
                }
                RescaleSearchInput(titleCreatorDropdownSearchInput, s);
            }
            Transform scrollGO = titleCreatorDropdown.transform.Find("Scroll");
            if (scrollGO != null)
            {
                RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
                if (scrollRT != null)
                {
                    float pad = GalleryUiDesignTokens.SideTabSideMarginRef * s;
                    scrollRT.offsetMin = new Vector2(pad, pad);
                    scrollRT.offsetMax = new Vector2(-pad, -(GalleryUiDesignTokens.SearchFieldHeightRef + 19f) * s);
                }
            }
            for (int i = 0; i < _titleCreatorVirtButtons.Count; i++)
            {
                GameObject btn = _titleCreatorVirtButtons[i];
                if (btn == null) continue;
                RectTransform rt = btn.GetComponent<RectTransform>();
                if (rt != null)
                    rt.sizeDelta = new Vector2(-10f * s, GalleryUiDesignTokens.SearchFieldHeightRef * s);
            }
            try { UpdateTitleCreatorVirtualVisible(); } catch { }
        }

        private void ToggleTitleCreatorDropdown()
        {
            if (titleCreatorDropdown == null) return;
            if (titleCreatorDropdown.activeSelf) HideTitleCreatorDropdown();
            else ShowTitleCreatorDropdown();
        }

        private void ShowTitleCreatorDropdown()
        {
            if (titleCreatorDropdown == null) return;
            if (titleCreatorDropdownBlocker != null)
            {
                try { titleCreatorDropdownBlocker.SetActive(true); } catch { }
                try { titleCreatorDropdownBlocker.transform.SetAsLastSibling(); } catch { }
            }
            try { titleCreatorDropdown.transform.SetAsLastSibling(); } catch { }
            titleCreatorDropdown.SetActive(true);
            RebuildTitleCreatorVirtView(force: false);
            UpdateTitleCreatorVirtualVisible();
            try { if (titleCreatorDropdownSearchInput != null) titleCreatorDropdownSearchInput.Select(); } catch { }
        }

        private void HideTitleCreatorDropdown()
        {
            CreatorRatingRowHandler.CloseAnyOpen();
            if (titleCreatorDropdown == null) return;
            titleCreatorDropdown.SetActive(false);
            try { if (titleCreatorDropdownBlocker != null) titleCreatorDropdownBlocker.SetActive(false); } catch { }
        }

        private float TitleCreatorVirtRowHeight()
        {
            float s = ChromeScale;
            return (35f * s) + (2f * s);
        }

        private void EnsureTitleCreatorVirtPool(Transform parent, int desired)
        {
            if (parent == null) return;
            if (desired < 8) desired = 8;
            for (int i = 0; i < _titleCreatorVirtButtons.Count; i++)
            {
                if (_titleCreatorVirtButtons[i] == null || _titleCreatorVirtButtons[i].transform.parent != parent)
                {
                    _titleCreatorVirtButtons.Clear();
                    break;
                }
            }
            while (_titleCreatorVirtButtons.Count < desired)
            {
                GameObject btnGO = UI.CreateUIButton(parent.gameObject, 240, 35, "", 18, 0, 0, AnchorPresets.middleLeft, null);
                AddHoverDelegate(btnGO);
                btnGO.SetActive(true);

                RectTransform rt = btnGO.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float s = ChromeScale;
                    rt.anchorMin = new Vector2(0, 1);
                    rt.anchorMax = new Vector2(1, 1);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(-10f, 35f * s);
                    rt.offsetMin = new Vector2(5f, rt.offsetMin.y);
                    rt.offsetMax = new Vector2(-5f, rt.offsetMax.y);
                }
                _titleCreatorVirtButtons.Add(btnGO);
            }
        }

        private void BindTitleCreatorVirtButton(GameObject btnGO, CreatorCacheEntry creator)
        {
            if (btnGO == null) return;
            string cName = creator.Name ?? "";
            bool isActive = ActiveFilterContainsCreatorSelection(cName);
            string label = cName + " (" + creator.Count + ")";

            var img = btnGO.GetComponent<Image>();
            if (img != null) img.color = isActive ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;

            var btn = btnGO.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    CreatorRatingRowHandler.CloseAnyOpen();
                    ToggleCreatorFilter(cName);
                    OnCreatorFilterChanged(refreshFilesAndTabs: true);
                    // Keep dropdown open for multi-select.
                    RebuildTitleCreatorVirtView(force: false);
                    UpdateTitleCreatorVirtualVisible();
                });
            }

            var txt = null as Text;
            Transform textTr = btnGO.transform.Find("Text");
            if (textTr != null) txt = textTr.GetComponent<Text>();
            if (txt != null)
            {
                float s = ChromeScale;
                GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                txt.text = label;
                txt.color = UI.PopupText;
            }

            BindCreatorRatingChrome(btnGO, cName);
        }

        private string ComputeTitleCreatorVirtViewSignature()
        {
            SortState st = GetSortState("Creator");
            float scale = ChromeScale;
            return "v1|" + creatorSideTabDataRevision
                + CreatorConsolidationSignatureFragment()
                + "|" + (titleCreatorDropdownFilter ?? "")
                + "|" + (nameFilterLower ?? "")
                + "|" + (VPBConfig.Instance != null ? VPBConfig.NormalizeGallerySearchScope(VPBConfig.Instance.GallerySearchScope) : "PathAndName")
                + "|" + (currentExtension ?? "")
                + "|" + CurrentPathsSignatureFragment()
                + "|" + (int)(st != null ? st.Type : 0)
                + "|" + (int)(st != null ? st.Direction : 0)
                + "|" + scale.ToString("R")
                + "|crR" + CreatorRatingRevisionFragment()
                + "|crF" + (creatorRatedOnlyFilter ? "1" : "0");
        }

        private void RebuildTitleCreatorVirtView(bool force)
        {
            if (!creatorsCached) CacheCreators();
            var displayCreators = GetCreatorsForDisplay();
            if (displayCreators == null) return;

            string sig = ComputeTitleCreatorVirtViewSignature();
            if (!force && string.Equals(_titleCreatorVirtViewSig, sig, StringComparison.Ordinal) && _titleCreatorVirtView.Count > 0) return;
            _titleCreatorVirtViewSig = sig;
            _titleCreatorVirtView.Clear();

            var sortState = GetCreatorListSortState();
            try { GallerySortManager.Instance.SortCreators(displayCreators, sortState); } catch { }

            string filterNow = titleCreatorDropdownFilter ?? "";
            string filterLower = string.IsNullOrEmpty(filterNow) ? "" : filterNow.ToLowerInvariant();
            bool hasFilter = filterLower.Length > 0;

            for (int i = 0; i < displayCreators.Count; i++)
            {
                var c = displayCreators[i];
                if (string.IsNullOrEmpty(c.Name)) continue;
                if (hasFilter && (c.Name == null || !c.Name.ToLowerInvariant().Contains(filterLower))) continue;
                if (!CreatorPassesRatedOnlyFilter(c.Name)) continue;
                _titleCreatorVirtView.Add(c);
            }
        }

        private void UpdateTitleCreatorVirtualVisible()
        {
            if (_titleCreatorVirtView == null) return;
            if (titleCreatorDropdownHolder == null || !_titleCreatorDropdownHolderActive()) return;
            if (_titleCreatorVirtScroll == null) return;

            float rowH = TitleCreatorVirtRowHeight();
            if (rowH <= 1f) rowH = 37f;

            RectTransform viewport = _titleCreatorVirtScroll.viewport != null ? _titleCreatorVirtScroll.viewport : (_titleCreatorVirtScroll.transform as RectTransform);
            float viewportH = viewport != null ? viewport.rect.height : 360f;

            int total = _titleCreatorVirtView.Count;
            var holderLe = titleCreatorDropdownHolder.GetComponent<LayoutElement>();
            if (total == 0)
            {
                for (int i = 0; i < _titleCreatorVirtButtons.Count; i++)
                    if (_titleCreatorVirtButtons[i] != null) _titleCreatorVirtButtons[i].SetActive(false);
                if (holderLe != null) holderLe.preferredHeight = 0f;
                return;
            }

            float contentH = total * rowH;
            if (holderLe != null) holderLe.preferredHeight = contentH;
            if (_titleCreatorVirtContentRT != null) _titleCreatorVirtContentRT.sizeDelta = new Vector2(_titleCreatorVirtContentRT.sizeDelta.x, contentH);

            float scrollRange = Mathf.Max(0f, contentH - viewportH);
            float scrollY = (1f - Mathf.Clamp01(_titleCreatorVirtScroll.verticalNormalizedPosition)) * scrollRange;
            int firstIdx = (rowH > 0f) ? Mathf.FloorToInt(scrollY / rowH) : 0;
            if (firstIdx < 0) firstIdx = 0;
            if (firstIdx > total - 1) firstIdx = Mathf.Max(0, total - 1);

            int visible = Mathf.CeilToInt(viewportH / rowH) + 10;
            EnsureTitleCreatorVirtPool(titleCreatorDropdownHolder.transform, visible);
            _titleCreatorVirtLastFirstIdx = firstIdx;

            for (int i = 0; i < _titleCreatorVirtButtons.Count; i++)
            {
                int idx = firstIdx + i;
                var btnGO = _titleCreatorVirtButtons[i];
                if (btnGO == null) continue;
                if (idx >= 0 && idx < total)
                {
                    btnGO.SetActive(true);
                    BindTitleCreatorVirtButton(btnGO, _titleCreatorVirtView[idx]);
                    var rt = btnGO.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        float s = ChromeScale;
                        rt.sizeDelta = new Vector2(-10f, 35f * s);
                        rt.anchoredPosition = new Vector2(0f, -idx * rowH);
                    }
                }
                else btnGO.SetActive(false);
            }
        }

        private bool _titleCreatorDropdownHolderActive()
        {
            try { return titleCreatorDropdown != null && titleCreatorDropdown.activeInHierarchy; }
            catch { return false; }
        }
    }
}

