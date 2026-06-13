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
            titleCreatorBtn = UI.CreateUIButton(titleBarGO, 40, 40, " ", 16, 0, 0, AnchorPresets.middleCenter, null);
            titleCreatorBtnBackdrop = titleCreatorBtn != null ? titleCreatorBtn.GetComponent<Image>() : null;
            titleCreatorBtnText = titleCreatorBtn != null ? titleCreatorBtn.GetComponentInChildren<Text>(true) : null;
            if (titleCreatorBtnBackdrop != null) titleCreatorBtnBackdrop.color = new Color(0f, 0f, 0f, 0.5f);
            if (titleCreatorBtnText != null) { titleCreatorBtnText.text = " "; titleCreatorBtnText.gameObject.SetActive(false); }
            try
            {
                // galleryCreatorSprite loaded later in Init; load directly so title bar button always has icon.
                Sprite s = galleryCreatorSprite;
                if (s == null) s = UI.LoadIconSprite("vpb_icons/gallery_creator.png", UI.BarIconGlyphTint);
                if (s != null)
                {
                    UI.AddIconToButton(titleCreatorBtn, s);
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

            AddTooltip(titleCreatorBtn, "gallery.tooltip.creator_filter", "Creator filter (multi-select). Left-click open. Right-click clear.");

            // Click-outside blocker (behind dropdown, above grid)
            titleCreatorDropdownBlocker = new GameObject("TitleCreatorDropdownBlocker");
            titleCreatorDropdownBlocker.transform.SetParent(backgroundBoxGO.transform, false);
            {
                var rt = titleCreatorDropdownBlocker.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
            }
            {
                var img = titleCreatorDropdownBlocker.AddComponent<Image>();
                img.color = new Color(0, 0, 0, 0);
                img.raycastTarget = true;
            }
            {
                var blockerBtn = titleCreatorDropdownBlocker.AddComponent<Button>();
                blockerBtn.onClick.RemoveAllListeners();
                blockerBtn.onClick.AddListener(() => HideTitleCreatorDropdown());
            }
            titleCreatorDropdownBlocker.SetActive(false);

            // Dropdown root (hidden by default)
            titleCreatorDropdown = new GameObject("TitleCreatorDropdown");
            titleCreatorDropdown.transform.SetParent(backgroundBoxGO.transform, false);
            titleCreatorDropdown.transform.SetAsLastSibling();
            var ddRT = titleCreatorDropdown.AddComponent<RectTransform>();
            ddRT.anchorMin = new Vector2(0.5f, 1f);
            ddRT.anchorMax = new Vector2(0.5f, 1f);
            ddRT.pivot = new Vector2(0.5f, 1f);
            // Below title bar (70px height) so it overlays grid items.
            ddRT.anchoredPosition = new Vector2(-184, -70);
            ddRT.sizeDelta = new Vector2(330, 500);

            var ddImg = titleCreatorDropdown.AddComponent<Image>();
            ddImg.color = new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f);

            var ddCg = titleCreatorDropdown.AddComponent<CanvasGroup>();
            ddCg.blocksRaycasts = true;
            ddCg.interactable = true;

            // Search input (top of dropdown)
            {
                var searchGO = CreateSearchInput(titleCreatorDropdown, 310f, (val) =>
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
                    srt.anchoredPosition = new Vector2(0, -10);
                    srt.sizeDelta = new Vector2(310, 36);
                }
            }

            // Scroll view area
            {
                GameObject scrollGO = new GameObject("Scroll");
                scrollGO.transform.SetParent(titleCreatorDropdown.transform, false);
                var scrollRT = scrollGO.AddComponent<RectTransform>();
                scrollRT.anchorMin = new Vector2(0, 0);
                scrollRT.anchorMax = new Vector2(1, 1);
                scrollRT.offsetMin = new Vector2(10, 10);
                scrollRT.offsetMax = new Vector2(-10, -54);
                float scrollBarWidth = 18f;

                var viewportGO = new GameObject("Viewport");
                viewportGO.transform.SetParent(scrollGO.transform, false);
                var vpRT = viewportGO.AddComponent<RectTransform>();
                vpRT.anchorMin = Vector2.zero;
                vpRT.anchorMax = Vector2.one;
                vpRT.sizeDelta = new Vector2(-scrollBarWidth, 0);
                vpRT.anchoredPosition = new Vector2(-scrollBarWidth / 2f, 0);
                viewportGO.AddComponent<RectMask2D>();

                var contentGO = new GameObject("Content");
                contentGO.transform.SetParent(viewportGO.transform, false);
                var contentRT = contentGO.AddComponent<RectTransform>();
                contentRT.anchorMin = new Vector2(0, 1);
                contentRT.anchorMax = new Vector2(1, 1);
                contentRT.pivot = new Vector2(0.5f, 1f);
                contentRT.anchoredPosition = Vector2.zero;
                contentRT.sizeDelta = new Vector2(0, 0);
                _titleCreatorVirtContentRT = contentRT;

                titleCreatorDropdownHolder = new GameObject("Holder");
                titleCreatorDropdownHolder.transform.SetParent(contentGO.transform, false);
                var holderRT = titleCreatorDropdownHolder.AddComponent<RectTransform>();
                holderRT.anchorMin = new Vector2(0, 1);
                holderRT.anchorMax = new Vector2(1, 1);
                holderRT.pivot = new Vector2(0.5f, 1f);
                holderRT.anchoredPosition = Vector2.zero;
                holderRT.sizeDelta = new Vector2(0, 0);
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
                ddRT.sizeDelta = new Vector2(
                    GalleryUiDesignTokens.TitleCreatorDropdownWidthRef * s,
                    GalleryUiDesignTokens.TitleCreatorDropdownHeightRef * s);
                if (titleCreatorBtn != null)
                {
                    RectTransform btnRT = titleCreatorBtn.GetComponent<RectTransform>();
                    if (btnRT != null)
                        ddRT.anchoredPosition = new Vector2(btnRT.anchoredPosition.x, -GalleryUiDesignTokens.TitleBarHeightRef * s);
                }
            }
            if (titleCreatorDropdownSearchInput != null)
            {
                RectTransform srt = titleCreatorDropdownSearchInput.GetComponent<RectTransform>();
                if (srt != null)
                {
                    srt.anchoredPosition = new Vector2(0f, -10f * s);
                    srt.sizeDelta = new Vector2(GalleryUiDesignTokens.TitleCreatorDropdownSearchWidthRef * s, GalleryUiDesignTokens.SearchFieldHeightRef * s);
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
            bool isActive = CreatorFilterContains(cName);
            string label = cName + " (" + creator.Count + ")";

            var img = btnGO.GetComponent<Image>();
            if (img != null) img.color = isActive ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop;

            var btn = btnGO.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    ToggleCreatorFilter(cName);
                    OnCreatorFilterChanged(refreshFilesAndTabs: true);
                    // Keep dropdown open for multi-select.
                    RebuildTitleCreatorVirtView(force: false);
                    UpdateTitleCreatorVirtualVisible();
                });
            }

            var txt = btnGO.GetComponentInChildren<Text>(true);
            if (txt != null)
            {
                float s = ChromeScale;
                GalleryUiMetrics.ApplyFont(txt, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                txt.text = label;
                txt.color = UI.PopupText;
            }
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
                + "|" + scale.ToString("R");
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

            var sortState = GetSortState("Creator");
            try { GallerySortManager.Instance.SortCreators(displayCreators, sortState); } catch { }

            string filterNow = titleCreatorDropdownFilter ?? "";
            string filterLower = string.IsNullOrEmpty(filterNow) ? "" : filterNow.ToLowerInvariant();
            bool hasFilter = filterLower.Length > 0;

            for (int i = 0; i < displayCreators.Count; i++)
            {
                var c = displayCreators[i];
                if (string.IsNullOrEmpty(c.Name)) continue;
                if (hasFilter && (c.Name == null || !c.Name.ToLowerInvariant().Contains(filterLower))) continue;
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

