using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VPB.src.util;

namespace VPB
{
    public partial class GalleryPanel : MonoBehaviour
    {
        internal const float CategoryQuickHoldOpenSeconds = 0.35f;
        private Transform _categoryQuickMenuParentTr;

        private static RoundedRect AddCategoryQuickRoundedBg(GameObject go, Color color, bool raycastTarget = true)
        {
            RoundedRect rr = go.AddComponent<RoundedRect>();
            rr.color = color;
            rr.raycastTarget = raycastTarget;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
            return rr;
        }

        private static void SyncCategoryQuickRoundedBg(RoundedRect rr)
        {
            if (rr == null) return;
            rr.cornerRadiusFraction = UI.ResolveGalleryElementCornerRadiusFraction();
        }

        /// <summary>Menu top Y on <c>backgroundBoxGO</c> (anchor top-left): flush under category chrome.</summary>
        private static float CategoryQuickMenuTopOffsetY(float paneScale)
        {
            float s = paneScale <= 0f ? 1f : paneScale;
            return -((GalleryUiDesignTokens.TitleBarHeightRef * 0.5f)
                + (GalleryUiDesignTokens.TitleBarCategoryRowHeightRef * 0.5f)
                + GalleryUiDesignTokens.PopupMenuAnchorGapRef) * s;
        }

        private void ApplyCategoryQuickArrowChromeLayout(float paneScale)
        {
            if (paneScale <= 0f) paneScale = 1f;
            float arrowW = GalleryUiDesignTokens.TitleBarChipRef * paneScale;
            if (_categoryQuickArrowLE != null)
            {
                _categoryQuickArrowLE.preferredWidth = arrowW;
                _categoryQuickArrowLE.minWidth = arrowW;
                _categoryQuickArrowLE.preferredHeight = GalleryUiDesignTokens.TitleBarCategoryRowHeightRef * paneScale;
            }
            if (_categoryQuickArrowIconRT != null)
            {
                float pad = GalleryUiDesignTokens.SearchIconButtonPadRef * paneScale;
                _categoryQuickArrowIconRT.offsetMin = new Vector2(pad, pad);
                _categoryQuickArrowIconRT.offsetMax = new Vector2(-pad, -pad);
            }
        }

        /// <summary>Default quick-switch order when <see cref="VPBConfig.GalleryCategoryQuickOrder"/> is empty.</summary>
        internal static readonly string[] s_DefaultGalleryQuickSwitchOrder =
        {
            "ALL VAR", "Scenes", "Appearance", "Clothing", "Pose", "Hair", "Skin", "Plugins", "CUA", "SubScenes"
        };

        private void SetupCategoryQuickSwitch(GameObject titleBarGO, GameObject galleryBackgroundGO, GameObject titleGO)
        {
            if (titleBarGO == null || canvas == null || galleryBackgroundGO == null || titleGO == null) return;
            _categoryQuickMenuParentTr = galleryBackgroundGO.transform;

            var cqRoot = new GameObject("CategoryQuickSwitchChrome");
            cqRoot.transform.SetParent(titleBarGO.transform, false);
            var cqRootRT = cqRoot.AddComponent<RectTransform>();
            cqRootRT.anchorMin = new Vector2(0, 0.5f);
            cqRootRT.anchorMax = new Vector2(0, 0.5f);
            cqRootRT.pivot = new Vector2(0, 0.5f);
            cqRootRT.anchoredPosition = new Vector2(60, 0);
            cqRootRT.sizeDelta = new Vector2(TitleBarCategoryClampMaxRef, 44);
            _categoryQuickChromeRootGO = cqRoot;
            _categoryQuickChromeRootRT = cqRootRT;

            var hitImg = AddCategoryQuickRoundedBg(cqRoot, new Color(0f, 0f, 0f, 0.5f));

            // Regular label-button: click label toggles list underneath.
            var headerBtn = cqRoot.AddComponent<Button>();
            headerBtn.transition = Selectable.Transition.None;
            headerBtn.targetGraphic = hitImg;
            // Tap/hold handled by CategoryQuickSwitchHeaderBehaviour only — onClick here double-toggled on desktop (PointerUp + onClick).

            var headerBehaviour = cqRoot.AddComponent<CategoryQuickSwitchHeaderBehaviour>();
            headerBehaviour.Panel = this;

            var hlg = cqRoot.AddComponent<HorizontalLayoutGroup>();
            // Small left inset so arrow never touches edge.
            hlg.padding = new RectOffset(6, 0, 0, 0);
            hlg.spacing = 6;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlHeight = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childForceExpandWidth = false;

            // Chevron indicator, left of label (header only; not settings rows).
            {
                var arrowGO = new GameObject("CategoryQuickArrow");
                arrowGO.transform.SetParent(cqRoot.transform, false);
                var arrowLE = arrowGO.AddComponent<LayoutElement>();
                _categoryQuickArrowLE = arrowLE;
                arrowLE.preferredWidth = GalleryUiDesignTokens.TitleBarChipRef;
                arrowLE.minWidth = GalleryUiDesignTokens.TitleBarChipRef;
                arrowLE.preferredHeight = GalleryUiDesignTokens.TitleBarCategoryRowHeightRef;

                GameObject iconGO = new GameObject("Icon");
                iconGO.transform.SetParent(arrowGO.transform, false);
                _categoryQuickArrowIconRT = iconGO.AddComponent<RectTransform>();
                _categoryQuickArrowIconRT.anchorMin = Vector2.zero;
                _categoryQuickArrowIconRT.anchorMax = Vector2.one;
                _categoryQuickArrowImage = iconGO.AddComponent<Image>();
                _categoryQuickArrowImage.color = Color.white;
                _categoryQuickArrowImage.preserveAspect = true;
                _categoryQuickArrowImage.raycastTarget = false;
                try
                {
                    Sprite chevron = UI.LoadIconSprite("vpb_icons/chevron_down.png", UI.BarIconGlyphTint);
                    if (chevron != null) _categoryQuickArrowImage.sprite = chevron;
                }
                catch { }
            }

            titleGO.transform.SetParent(cqRoot.transform, false);
            var titleRT = titleGO.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 0f);
            titleRT.anchorMax = new Vector2(1f, 1f);
            titleRT.pivot = new Vector2(0f, 0.5f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;
            var titleLe = titleGO.GetComponent<LayoutElement>();
            if (titleLe == null) titleLe = titleGO.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;
            titleLe.preferredWidth = 168f;
            titleLe.minWidth = 100f;
            titleLe.preferredHeight = 44f;

            // Ensure title text reads like label (no arrow dependency).
            try
            {
                var t = titleGO.GetComponent<Text>();
                if (t != null)
                {
                    t.alignment = TextAnchor.MiddleLeft;
                    t.color = Color.white;
                }
            }
            catch { }

            try
            {
                AddTooltip(cqRoot, "gallery.tooltip.category_quick_switch",
                    "Tap: toggle list. Hold to open, move, release on row to switch. Keys 1\u20139 / 0. Gallery Settings \u2192 Header category menu, or plugin Settings \u2192 Gallery.");
            }
            catch { }

            try
            {
                var chromeHover = cqRoot.AddComponent<UIHoverBorder>();
                chromeHover.ApplyBorderSettings();
            }
            catch { }

            _categoryQuickBlockerGO = new GameObject("CategoryQuickSwitchBlocker");
            _categoryQuickBlockerGO.transform.SetParent(galleryBackgroundGO.transform, false);
            var blkRT = _categoryQuickBlockerGO.AddComponent<RectTransform>();
            blkRT.anchorMin = Vector2.zero;
            blkRT.anchorMax = Vector2.one;
            blkRT.offsetMin = Vector2.zero;
            blkRT.offsetMax = Vector2.zero;
            var blkImg = _categoryQuickBlockerGO.AddComponent<Image>();
            blkImg.color = new Color(0, 0, 0, 0.001f);
            blkImg.raycastTarget = true;
            var blkBtn = _categoryQuickBlockerGO.AddComponent<Button>();
            blkBtn.targetGraphic = blkImg;
            blkBtn.transition = Selectable.Transition.None;
            blkBtn.onClick.AddListener(() => SetCategoryQuickMenuVisible(false));
            _categoryQuickBlockerGO.SetActive(false);

            _categoryQuickMenuOuterGO = new GameObject("CategoryQuickMenu");
            // Keep menu out of titlebar masks/clips.
            _categoryQuickMenuOuterGO.transform.SetParent(galleryBackgroundGO.transform, false);
            var outerRT = _categoryQuickMenuOuterGO.AddComponent<RectTransform>();
            outerRT.anchorMin = new Vector2(0, 1);
            outerRT.anchorMax = new Vector2(0, 1);
            outerRT.pivot = new Vector2(0, 1);
            outerRT.anchoredPosition = new Vector2(60, CategoryQuickMenuTopOffsetY(1f));
            outerRT.sizeDelta = new Vector2(TitleBarCategoryClampMaxRef, 340f);

            _categoryQuickMenuOuterRT = outerRT;
            var outerImg = AddCategoryQuickRoundedBg(_categoryQuickMenuOuterGO, new Color(UI.PopupBackdrop.r, UI.PopupBackdrop.g, UI.PopupBackdrop.b, 0.92f));

            // No child Canvas / overrideSorting / SuperController.AddCanvas. Earlier attempts at all three
            // either left the popup behind gallery rows in VR (overrideSorting unreliable for nested WorldSpace
            // canvases) or broke raycast (z-position offset). Matching TitleCreatorDropdown's pattern: stay in
            // the parent gallery canvas, rely on hierarchy sibling order (SetAsLastSibling on show) to render
            // above rows. Within a single canvas, sibling order is the render order.
            try
            {
                var cg = _categoryQuickMenuOuterGO.AddComponent<CanvasGroup>();
                cg.ignoreParentGroups = true;
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            catch { }

            _categoryQuickMenuScrollGO = UI.CreateVScrollableContent(
                _categoryQuickMenuOuterGO,
                new Color(0, 0, 0, 0),
                AnchorPresets.stretchAll,
                0, 0,
                Vector2.zero,
                14f,
                2f,
                false);
            var scrollRT = _categoryQuickMenuScrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = new Vector2(4, 4);
            scrollRT.offsetMax = new Vector2(-4, -4);

            var sr = _categoryQuickMenuScrollGO.GetComponent<ScrollRect>();
            if (sr != null)
            {
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.elasticity = 0f;
            }
            _categoryQuickMenuContentGO = sr != null ? sr.content.gameObject : null;
            if (_categoryQuickMenuContentGO != null)
            {
                VerticalLayoutGroup menuVlg = _categoryQuickMenuContentGO.GetComponent<VerticalLayoutGroup>();
                if (menuVlg != null)
                {
                    menuVlg.spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef;
                    menuVlg.padding = new RectOffset(6, 6, 6, 6);
                }
            }

            _categoryQuickMenuOuterGO.SetActive(false);

            innerPaneScaleActions.Add(s => ApplyCategoryQuickChromeLayout(s));
        }

        /// <summary>VR: gallery chrome docked to VaM menu strip (flush left when menu visible).</summary>
        private bool CategoryQuickSwitchUsesAnchoredTitleLayout()
        {
            bool vr = XrUtils.IsVrActive();
            if (!vr || VPBConfig.Instance == null) return false;
            if (!VPBConfig.Instance.GalleryAnchorToVamMenu) return false;
            if (GetAnchoredInstance() != this) return false;
            return IsVamMenuVisible();
        }

        /// <summary>True: align category chrome with left window edge. False (floating desktop / floating VR): inset so resize handle stays usable.</summary>
        private bool CategoryQuickSwitchFlushLeftEdge()
        {
            if (CategoryQuickSwitchUsesAnchoredTitleLayout()) return true;
            return isFixedLocally;
        }

        private void ApplyCategoryQuickChromeLayout(float paneScale)
        {
            if (_categoryQuickChromeRootRT == null) return;
            if (paneScale <= 0f) paneScale = 1f;
            bool flushLeft = CategoryQuickSwitchFlushLeftEdge();
            float leftInset = flushLeft ? 0f : GalleryUiDesignTokens.TitleBarTitleLeftInsetRef * paneScale;
            _categoryQuickChromeRootRT.localScale = Vector3.one;
            _categoryQuickChromeRootRT.anchoredPosition = new Vector2(leftInset, 0f);
            _categoryQuickChromeRootRT.sizeDelta = new Vector2(TitleBarCategoryClampMaxRef * paneScale, GalleryUiDesignTokens.TitleBarCategoryRowHeightRef * paneScale);
            SyncCategoryQuickRoundedBg(_categoryQuickChromeRootGO != null ? _categoryQuickChromeRootGO.GetComponent<RoundedRect>() : null);
            if (_categoryQuickMenuOuterRT != null)
            {
                _categoryQuickMenuOuterRT.localScale = Vector3.one;
                _categoryQuickMenuOuterRT.anchoredPosition = new Vector2(
                    leftInset,
                    CategoryQuickMenuTopOffsetY(paneScale));
                _categoryQuickMenuOuterRT.sizeDelta = new Vector2(TitleBarCategoryClampMaxRef * paneScale, 340f * paneScale);
                SyncCategoryQuickRoundedBg(_categoryQuickMenuOuterGO != null ? _categoryQuickMenuOuterGO.GetComponent<RoundedRect>() : null);
            }
            ApplyCategoryQuickArrowChromeLayout(paneScale);
            ApplyCategoryQuickMenuRowsLayout(paneScale);
        }

        /// <summary>Scale category quick-switch dropdown row fonts/heights without full rebuild.</summary>
        private void ApplyCategoryQuickMenuRowsLayout(float s)
        {
            if (_categoryQuickMenuContentGO == null) return;
            if (s <= 0f) s = 1f;
            Transform parent = _categoryQuickMenuContentGO.transform;
            int pad = Mathf.RoundToInt(6f * s);
            VerticalLayoutGroup contentVlg = _categoryQuickMenuContentGO.GetComponent<VerticalLayoutGroup>();
            if (contentVlg != null)
            {
                contentVlg.padding = new RectOffset(pad, pad, pad, pad);
                contentVlg.spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
            }
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightRef * s;
            int padH = Mathf.RoundToInt(10f * s);
            int padV = Mathf.RoundToInt(6f * s);
            int gap = Mathf.RoundToInt(10f * s);
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform row = parent.GetChild(i);
                if (row == null) continue;
                HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
                if (hlg != null)
                {
                    hlg.padding = new RectOffset(padH, padH, padV, padV);
                    hlg.spacing = gap;
                }
                LayoutElement le = row.GetComponent<LayoutElement>();
                if (le != null)
                {
                    le.preferredHeight = rowH;
                    le.minHeight = rowH;
                }
                RoundedRect rowBg = row.GetComponent<RoundedRect>();
                if (rowBg != null)
                    SyncCategoryQuickRoundedBg(rowBg);
                for (int c = 0; c < row.childCount; c++)
                {
                    Transform child = row.GetChild(c);
                    if (child == null) continue;
                    Text t = child.GetComponent<Text>();
                    if (t != null)
                        GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontLargeRef, s, 12);
                    LayoutElement childLe = child.GetComponent<LayoutElement>();
                    if (childLe != null && child.name == "Idx")
                    {
                        childLe.preferredWidth = 34f * s;
                        childLe.minWidth = 34f * s;
                    }
                }
            }
        }

        private void SyncCategoryQuickSwitchChrome()
        {
            if (_categoryQuickChromeRootGO == null) return;
            bool show = !IsFilterActive && !importSidebarActive;
            if (_categoryQuickChromeRootGO.activeSelf != show)
                _categoryQuickChromeRootGO.SetActive(show);
            if (!show && _categoryQuickMenuOpen)
                SetCategoryQuickMenuVisible(false);
            if (!show) HideGlobalSourceFilterDropdownIfOpen();
            ApplyCategoryQuickChromeLayout(ChromeScale);
        }

        private void RefreshCategoryQuickSwitchOnConfigChanged()
        {
            _categoryQuickMenuDirty = true;
            if (_categoryQuickMenuOpen)
                RebuildCategoryQuickSwitchMenuRows();
        }

        internal void OpenCategoryQuickMenuFromHold()
        {
            SetCategoryQuickMenuVisible(true);
        }

        /// <summary>Pointer raised on header after press. Unity sends PointerUp to object that received PointerDown — use raycast at release for hold\u2192release row pick.</summary>
        internal void OnCategoryQuickHeaderPointerUp(PointerEventData eventData, float durationSeconds, bool openedByHoldGesture)
        {
            if (_categoryQuickMenuOpen && openedByHoldGesture)
            {
                if (eventData != null && TryPickCategoryQuickSwitchFromRaycast(eventData, out Gallery.Category cat))
                    QueueDeferredCategoryQuickPick(cat);
                // Released on empty space during hold-browse: leave the menu open so the user can
                // make a deliberate tap selection rather than forcing a full reopen cycle.
                return;
            }
            if (!openedByHoldGesture && durationSeconds < CategoryQuickHoldOpenSeconds)
                ToggleCategoryQuickMenuVisible();
        }

        private bool TryPickCategoryQuickSwitchFromRaycast(PointerEventData eventData, out Gallery.Category cat)
        {
            cat = default(Gallery.Category);
            if (eventData == null || EventSystem.current == null) return false;
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);
            for (int i = 0; i < results.Count; i++)
            {
                Transform t = results[i].gameObject != null ? results[i].gameObject.transform : null;
                while (t != null)
                {
                    var mk = t.GetComponent<CategoryQuickSwitchRowMarker>();
                    if (mk != null && !string.IsNullOrEmpty(mk.CategoryName))
                        return TryResolveCategoryFromQuickMarker(mk, out cat);
                    t = t.parent;
                }
            }
            return false;
        }

        private bool TryResolveCategoryFromQuickMarker(CategoryQuickSwitchRowMarker mk, out Gallery.Category cat)
        {
            cat = default(Gallery.Category);
            if (mk == null || string.IsNullOrEmpty(mk.CategoryName)) return false;
            if (categories == null) return false;
            for (int i = 0; i < categories.Count; i++)
            {
                var x = categories[i];
                if (!string.Equals(x.name, mk.CategoryName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(x.extension ?? "", mk.CategoryExtension ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(x.path ?? "", mk.CategoryPath ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                cat = x;
                return true;
            }
            for (int i = 0; i < categories.Count; i++)
            {
                var x = categories[i];
                if (string.Equals(x.name, mk.CategoryName, StringComparison.OrdinalIgnoreCase))
                {
                    cat = x;
                    return true;
                }
            }
            return false;
        }

        private void QueueDeferredCategoryQuickPick(Gallery.Category c)
        {
            string n = c.name ?? "";
            string ext = c.extension ?? "";
            string p = c.path ?? "";
            if (string.IsNullOrEmpty(n)) return;
            StopCo(ref _categoryQuickApplyCoroutine);
            _categoryQuickApplyCoroutine = StartCoroutine(CoDeferredCategoryQuickPick(n, ext, p));
        }

        private IEnumerator CoDeferredCategoryQuickPick(string name, string extension, string path)
        {
            HideGlobalSourceFilterDropdownIfOpen();
            SetCategoryQuickMenuVisible(false);
            yield return null;
            _categoryQuickApplyCoroutine = null;
            try
            {
                if (string.IsNullOrEmpty(name)) yield break;
                if (_benchPickModeActive && !BenchPickModeAllowsShowRequest(name))
                {
                    ShowTemporaryStatus(VPBTranslation.T("bench.pick.block_nav",
                        "End Scene Load Test selection first (Done or Cancel)."), 2.5f);
                    yield break;
                }
                if (LogGalleryCategoryTypeSwitchTiming)
                    BeginGalleryCategoryTypeNavigationTiming(name);
                Show(name, extension, path);
                if (Settings.Instance != null && Settings.Instance.LastGalleryPage != null)
                    Settings.Instance.LastGalleryPage.Value = name;
                if (VPBConfig.Instance != null)
                {
                    VPBConfig.Instance.LastGalleryCategory = name;
                    try { VPBConfig.Instance.Save(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                try { LogUtil.LogError("[Gallery] Category quick switch failed: " + ex.Message); } catch { }
            }
        }

        private void ToggleCategoryQuickMenuVisible()
        {
            SetCategoryQuickMenuVisible(!_categoryQuickMenuOpen);
        }

        private void SetCategoryQuickMenuVisible(bool visible)
        {
            _categoryQuickMenuOpen = visible;
            if (_categoryQuickBlockerGO != null)
                _categoryQuickBlockerGO.SetActive(visible);
            if (_categoryQuickMenuOuterGO != null)
                _categoryQuickMenuOuterGO.SetActive(visible);
            if (visible)
            {
                bool needsRebuild = _categoryQuickMenuDirty
                    || _categoryQuickMenuContentGO == null
                    || _categoryQuickMenuContentGO.transform.childCount == 0
                    || !categoriesCached
                    || _categoryQuickMenuLastPath != currentPath
                    || _categoryQuickMenuLastExtension != currentExtension;
                if (needsRebuild)
                    RebuildCategoryQuickSwitchMenuRows();
                if (_categoryQuickBlockerGO != null)
                    _categoryQuickBlockerGO.transform.SetAsLastSibling();
                if (_categoryQuickMenuOuterGO != null)
                    _categoryQuickMenuOuterGO.transform.SetAsLastSibling();
            }
        }

        private bool CategoryPassesQuickSwitchVisibility(Gallery.Category c, bool isActive)
        {
            if (string.IsNullOrEmpty(c.name)) return false;
            int count = categoryCounts != null && categoryCounts.ContainsKey(c.name) ? categoryCounts[c.name] : 0;
            bool allowZero = string.Equals(c.name, "Plugins", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.name, "ALL VAR", StringComparison.OrdinalIgnoreCase);
            if (count == 0 && !isActive && !allowZero)
                return false;
            if (VPBConfig.Instance != null && VPBConfig.Instance.IsHiddenCategory(c.name) && !isActive)
                return false;
            return true;
        }

        internal static void ParseQuickSwitchNameTokens(string spec, List<string> dest)
        {
            dest.Clear();
            if (string.IsNullOrEmpty(spec)) return;
            char[] seps = { ',', '\n', '\r', ';' };
            foreach (var raw in spec.Split(seps, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim();
                if (!string.IsNullOrEmpty(t)) dest.Add(t);
            }
        }

        private static void BuildAliasCandidatesForQuickToken(string token, List<string> aliases)
        {
            aliases.Clear();
            if (string.IsNullOrEmpty(token)) return;
            aliases.Add(token);
            if (string.Equals(token, "Skin", StringComparison.OrdinalIgnoreCase))
                aliases.Add("Person Skin");
            if (string.Equals(token, "SubScenes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "SubScene", StringComparison.OrdinalIgnoreCase))
            {
                aliases.Add("SubScenes");
                aliases.Add("SubScene");
            }
        }

        private static bool TryResolveQuickCategoryFromToken(string token, List<Gallery.Category> displayCategories, out Gallery.Category found)
        {
            found = default(Gallery.Category);
            if (string.IsNullOrEmpty(token)) return false;
            var aliases = new List<string>(8);
            BuildAliasCandidatesForQuickToken(token, aliases);
            for (int a = 0; a < aliases.Count; a++)
            {
                string want = aliases[a];
                for (int i = 0; i < displayCategories.Count; i++)
                {
                    var x = displayCategories[i];
                    if (string.Equals(x.name, want, StringComparison.OrdinalIgnoreCase))
                    {
                        found = x;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsQuickSwitchExplicitlyHidden(string categoryName, bool isActive)
        {
            if (string.IsNullOrEmpty(categoryName) || VPBConfig.Instance == null) return false;
            string spec = VPBConfig.Instance.GalleryCategoryQuickSwitchHidden ?? "";
            if (string.IsNullOrEmpty(spec)) return false;
            char[] seps = { ',', '\n', '\r', ';' };
            foreach (var raw in spec.Split(seps, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (string.Equals(t, categoryName, StringComparison.OrdinalIgnoreCase))
                    return !isActive;
            }
            return false;
        }

        /// <summary>Ordered list for dropdown and keyboard; mirrors category tab visibility rules (no side-pane search filter).</summary>
        internal List<Gallery.Category> BuildOrderedCategoriesForQuickSwitch()
        {
            var result = new List<Gallery.Category>();
            if (categories == null || categories.Count == 0) return result;
            if (!categoriesCached)
                CacheCategoryCounts();

            var sortState = GetSortState("Category");
            var displayCategories = new List<Gallery.Category>(categories);
            GallerySortManager.Instance.SortCategories(displayCategories, sortState, categoryCounts);

            string orderSpec = VPBConfig.Instance != null ? (VPBConfig.Instance.GalleryCategoryQuickOrder ?? "").Trim() : "";
            var preferredNames = new List<string>();
            if (!string.IsNullOrEmpty(orderSpec))
                ParseQuickSwitchNameTokens(orderSpec, preferredNames);
            else
            {
                for (int i = 0; i < s_DefaultGalleryQuickSwitchOrder.Length; i++)
                    preferredNames.Add(s_DefaultGalleryQuickSwitchOrder[i]);
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int p = 0; p < preferredNames.Count; p++)
            {
                if (!TryResolveQuickCategoryFromToken(preferredNames[p], displayCategories, out Gallery.Category found))
                    continue;
                if (string.IsNullOrEmpty(found.name)) continue;
                bool active = found.path == currentPath && found.extension == currentExtension;
                if (IsQuickSwitchExplicitlyHidden(found.name, active)) continue;
                if (!CategoryPassesQuickSwitchVisibility(found, active)) continue;
                if (used.Add(found.name))
                    result.Add(found);
            }

            foreach (var c in displayCategories)
            {
                if (used.Contains(c.name)) continue;
                bool active = c.path == currentPath && c.extension == currentExtension;
                if (IsQuickSwitchExplicitlyHidden(c.name, active)) continue;
                if (!CategoryPassesQuickSwitchVisibility(c, active)) continue;
                result.Add(c);
            }

            return result;
        }

        private void RebuildCategoryQuickSwitchMenuRows()
        {
            if (_categoryQuickMenuContentGO == null) return;
            try
            {
                for (int i = _categoryQuickMenuContentGO.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(_categoryQuickMenuContentGO.transform.GetChild(i).gameObject);

                var ordered = BuildOrderedCategoriesForQuickSwitch();

                for (int i = 0; i < ordered.Count; i++)
                {
                    var c = ordered[i];
                    bool isActive = c.path == currentPath && c.extension == currentExtension;
                    int keyNum = (i < 9) ? (i + 1) : ((i == 9) ? 0 : -1);
                    CreateCategoryQuickMenuRow(_categoryQuickMenuContentGO.transform, i, c, i + 1, keyNum, isActive);
                }

                ApplyCategoryQuickMenuRowsLayout(ChromeScale);

                if (_categoryQuickMenuOuterGO != null && ordered.Count == 0)
                {
                    var empty = new GameObject("EmptyHint");
                    empty.transform.SetParent(_categoryQuickMenuContentGO.transform, false);
                    var t = empty.AddComponent<Text>();
                    VPBUiFont.ApplyTo(t);
                    t.fontSize = GalleryUiDesignTokens.FontBodyRef;
                    t.color = new Color(0.7f, 0.7f, 0.75f);
                    t.text = VPBTranslation.T("gallery.category_quick.empty", "No categories available.");
                    var le = empty.AddComponent<LayoutElement>();
                    le.preferredHeight = 28;
                }

                _categoryQuickMenuDirty = false;
                _categoryQuickMenuLastPath = currentPath;
                _categoryQuickMenuLastExtension = currentExtension;
            }
            catch (Exception ex)
            {
                try { LogUtil.LogError("[Gallery] Rebuild category quick menu failed: " + ex.Message); } catch { }
            }
        }

        private void CreateCategoryQuickMenuRow(Transform parent, int rowIndex, Gallery.Category cat, int rowLabelNumber, int keyboardDigitLabel, bool isActive)
        {
            var row = new GameObject("CatRow_" + rowIndex);
            row.transform.SetParent(parent, false);

            var rowMk = row.AddComponent<CategoryQuickSwitchRowMarker>();
            rowMk.CategoryName = cat.name ?? "";
            rowMk.CategoryExtension = cat.extension ?? "";
            rowMk.CategoryPath = cat.path ?? "";

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 6, 6);
            hlg.spacing = 10;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandHeight = false;
            hlg.childForceExpandWidth = false;

            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 36;
            rowLe.minHeight = 36;

            var bg = AddCategoryQuickRoundedBg(row, isActive ? UI.PopupRowActiveBackdrop : UI.PopupRowBackdrop);

            var btn = row.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            var cap = cat;
            btn.onClick.AddListener(() => ApplyCategoryQuickPick(cap));

            string numPrefix = keyboardDigitLabel >= 0 ? (keyboardDigitLabel == 0 ? "0." : keyboardDigitLabel + ".") : rowLabelNumber + ".";

            var numGO = new GameObject("Idx");
            numGO.transform.SetParent(row.transform, false);
            var numT = numGO.AddComponent<Text>();
            VPBUiFont.ApplyTo(numT);
            numT.fontSize = GalleryUiDesignTokens.FontBodyRef;
            numT.fontStyle = FontStyle.Normal;
            numT.color = UI.PopupMutedText;
            numT.alignment = TextAnchor.MiddleLeft;
            numT.text = numPrefix;
            var numLe = numGO.AddComponent<LayoutElement>();
            numLe.preferredWidth = 34;
            numLe.minWidth = 34;

            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(row.transform, false);
            var nameT = nameGO.AddComponent<Text>();
            VPBUiFont.ApplyTo(nameT);
            nameT.fontSize = GalleryUiDesignTokens.FontBodyRef;
            nameT.color = isActive ? UI.PopupText : UI.PopupMutedText;
            nameT.alignment = TextAnchor.MiddleLeft;
            int cnt = categoryCounts != null && categoryCounts.ContainsKey(cat.name ?? "") ? categoryCounts[cat.name] : 0;
            nameT.text = (cat.name ?? "") + " (" + cnt + ")";
            var nameLe = nameGO.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1;
            nameLe.minWidth = 40;
        }

        internal void ApplyCategoryQuickPick(Gallery.Category c)
        {
            if (string.IsNullOrEmpty(c.name)) return;
            QueueDeferredCategoryQuickPick(c);
        }

        private bool TryConsumeCategoryQuickNumberKey()
        {
            if (!IsVisible) return false;

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (ctrl || shift || alt) return false;

            int idx = -1;
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) idx = 0;
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) idx = 1;
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) idx = 2;
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) idx = 3;
            else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) idx = 4;
            else if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) idx = 5;
            else if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) idx = 6;
            else if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) idx = 7;
            else if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)) idx = 8;
            else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) idx = 9;

            if (idx < 0) return false;

            var list = BuildOrderedCategoriesForQuickSwitch();
            if (idx >= list.Count) return false;

            ApplyCategoryQuickPick(list[idx]);
            try { ShowTemporaryStatus("\u2192 " + (list[idx].name ?? ""), 1.2f); } catch { }
            return true;
        }
    }

    internal class CategoryQuickSwitchHeaderBehaviour : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public GalleryPanel Panel;
        private bool _down;
        private float _downTime;
        private Coroutine _holdCo;
        private bool _openedByHold;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _down = true;
            _downTime = Time.unscaledTime;
            if (_holdCo != null)
                StopCoroutine(_holdCo);
            _holdCo = StartCoroutine(HoldRoutine());
        }

        private IEnumerator HoldRoutine()
        {
            yield return new WaitForSecondsRealtime(GalleryPanel.CategoryQuickHoldOpenSeconds);
            if (!_down) yield break;
            _openedByHold = true;
            Panel?.OpenCategoryQuickMenuFromHold();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _down = false;
            if (_holdCo != null)
            {
                StopCoroutine(_holdCo);
                _holdCo = null;
            }
            bool obh = _openedByHold;
            _openedByHold = false;
            float dt = Time.unscaledTime - _downTime;
            Panel?.OnCategoryQuickHeaderPointerUp(eventData, dt, obh);
        }
    }

    /// <summary>Raycast target for hold\u2192release category selection on row.</summary>
    internal class CategoryQuickSwitchRowMarker : MonoBehaviour
    {
        public string CategoryName;
        public string CategoryExtension;
        public string CategoryPath;
    }
}
