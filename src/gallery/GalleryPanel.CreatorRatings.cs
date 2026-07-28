using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Creator-row rating badge + 0–5 picker (package <see cref="RatingHandler"/> pattern).
    /// Digit always shown (not color-only). Warm path: bind / toggle only.
    /// </summary>
    public class CreatorRatingRowHandler : MonoBehaviour
    {
        private static CreatorRatingRowHandler _openHandler;

        private string creatorName = "";
        private Text digitText;
        private Image starImage;
        private GalleryPanel panel;
        private GameObject selectorGO;
        private CanvasGroup selectorCG;
        private Image[] optionImages;
        private Text[] optionTexts;
        private GameObject[] borderGOs;
        private int currentRating;
        private Transform _selectorHomeParent;
        private int _selectorHomeSibling;
        private bool _suppressDisableClose;

        public GameObject SelectorGO { get { return selectorGO; } }

        public static CreatorRatingRowHandler OpenHandler { get { return _openHandler; } }

        public static void CloseAnyOpen()
        {
            if (_openHandler != null)
                _openHandler.CloseSelector();
        }

        public bool IsSelectorOpen
        {
            get
            {
                if (selectorGO == null || !selectorGO) return false;
                if (selectorCG == null) selectorCG = selectorGO.GetComponent<CanvasGroup>();
                return selectorCG != null && selectorCG.alpha > 0.01f;
            }
        }

        public void Bind(GalleryPanel host, string name, Text digit, Image starImg, GameObject selector)
        {
            bool sameCreator = string.Equals(creatorName, name, StringComparison.OrdinalIgnoreCase);
            bool wasOpen = IsSelectorOpen;

            panel = host;
            creatorName = name ?? "";
            digitText = digit;
            starImage = starImg;
            if (selector != null)
                selectorGO = selector;
            if (selectorGO != null && selectorCG == null)
                selectorCG = selectorGO.GetComponent<CanvasGroup>();

            // Only close when this pooled row is rebound to a different creator while open.
            if (wasOpen && !sameCreator)
                CloseSelector();

            try { currentRating = CreatorRatingsManager.Instance.GetRating(creatorName); }
            catch { currentRating = 0; }
            RefreshDisplay();
        }

        public void AttachSelector(GameObject selector, Image[] images, Text[] texts, GameObject[] borders)
        {
            selectorGO = selector;
            if (selectorGO != null)
            {
                selectorCG = selectorGO.GetComponent<CanvasGroup>();
                if (selectorCG == null) selectorCG = selectorGO.AddComponent<CanvasGroup>();
            }
            optionImages = images;
            optionTexts = texts;
            borderGOs = borders;
            RefreshDisplay();
        }

        public void OnStarClicked()
        {
            ToggleSelector();
        }

        public void ToggleSelector()
        {
            if (selectorGO == null || !selectorGO) return;
            if (!selectorGO.activeSelf)
                selectorGO.SetActive(true);
            if (selectorCG == null) selectorCG = selectorGO.GetComponent<CanvasGroup>();

            bool opening = selectorCG == null || selectorCG.alpha <= 0.01f;
            if (opening)
                OpenSelector();
            else
                CloseSelector();
        }

        public void OpenSelector()
        {
            if (selectorGO == null || !selectorGO) return;
            if (_openHandler != null && _openHandler != this)
                _openHandler.CloseSelector();

            _openHandler = this;
            _suppressDisableClose = true;
            SetSelectorVisible(true);
            if (panel != null)
                panel.BeginCreatorRatingPickerDismissGuard();
            _suppressDisableClose = false;
        }

        public void CloseSelector()
        {
            if (_openHandler == this)
                _openHandler = null;
            SetSelectorVisible(false);
            if (panel != null)
                panel.EndCreatorRatingPickerDismissGuard();
        }

        public void SetRating(int rating)
        {
            if (string.IsNullOrEmpty(creatorName)) return;
            rating = Mathf.Clamp(rating, 0, 5);
            if (rating > 0 && rating == currentRating)
                rating = 0;

            try { CreatorRatingsManager.Instance.SetRating(creatorName, rating); }
            catch { return; }

            currentRating = rating;
            RefreshDisplay();
            CloseSelector();
            if (panel != null)
                panel.NotifyCreatorRatingChanged();
        }

        public void ClearRating()
        {
            if (string.IsNullOrEmpty(creatorName)) return;
            try { CreatorRatingsManager.Instance.SetRating(creatorName, 0); }
            catch { return; }
            currentRating = 0;
            RefreshDisplay();
            CloseSelector();
            if (panel != null)
                panel.NotifyCreatorRatingChanged();
        }

        private void SetSelectorVisible(bool visible)
        {
            if (selectorGO == null || !selectorGO) return;
            if (visible && !selectorGO.activeSelf)
                selectorGO.SetActive(true);
            if (selectorCG == null) selectorCG = selectorGO.GetComponent<CanvasGroup>();
            if (selectorCG == null) selectorCG = selectorGO.AddComponent<CanvasGroup>();

            if (visible)
            {
                TryReparentSelectorOutsideScroll();
                // Explicit star-relative place — worldPositionStays alone is unreliable across
                // ScrollRect / dropdown / pane size, and layout refresh rewrites anchors.
                PositionSelectorNearStar();
            }
            else
                RestoreSelectorHomeParent();

            selectorCG.ignoreParentGroups = visible;
            selectorCG.alpha = visible ? 1f : 0f;
            selectorCG.interactable = visible;
            selectorCG.blocksRaycasts = visible;
            if (visible)
                selectorGO.transform.SetAsLastSibling();
        }

        private void TryReparentSelectorOutsideScroll()
        {
            if (selectorGO == null || panel == null) return;
            GameObject host = panel.backgroundBoxGO;
            if (host == null) return;
            Transform home = selectorGO.transform.parent;
            if (home == host.transform) return;
            _selectorHomeParent = home;
            _selectorHomeSibling = selectorGO.transform.GetSiblingIndex();
            // Pose rewritten by PositionSelectorNearStar; keep false so anchors are clean.
            selectorGO.transform.SetParent(host.transform, false);
        }

        /// <summary>
        /// Place open picker under the star, in host (backgroundBox) local space.
        /// </summary>
        internal void PositionSelectorNearStar()
        {
            if (selectorGO == null || !selectorGO || panel == null) return;
            GameObject host = panel.backgroundBoxGO;
            if (host == null) return;
            RectTransform hostRT = host.GetComponent<RectTransform>();
            RectTransform selRT = selectorGO.GetComponent<RectTransform>();
            RectTransform starRT = transform as RectTransform;
            if (hostRT == null || selRT == null || starRT == null) return;

            float s = 1f;
            try { s = panel.ChromeScale; } catch { s = 1f; }
            if (s <= 0f) s = 1f;
            float gap = 2f * s;

            Bounds starB = RectTransformUtility.CalculateRelativeRectTransformBounds(hostRT, starRT);
            float selW = selRT.rect.width;
            float selH = selRT.rect.height;
            if (selW < 1f) selW = selRT.sizeDelta.x;
            if (selH < 1f) selH = selRT.sizeDelta.y;

            // Right-align with star; drop just below. Pivot top-right.
            selRT.anchorMin = selRT.anchorMax = new Vector2(0.5f, 0.5f);
            selRT.pivot = new Vector2(1f, 1f);
            float x = starB.max.x;
            float y = starB.min.y - gap;

            // Clamp inside host so picker never lands at pane origin / off-screen.
            Rect hr = hostRT.rect;
            float pad = 8f * s;
            float minX = hr.xMin + pad + selW;
            float maxX = hr.xMax - pad;
            float minY = hr.yMin + pad + selH;
            float maxY = hr.yMax - pad;
            if (minX <= maxX) x = Mathf.Clamp(x, minX, maxX);
            if (minY <= maxY) y = Mathf.Clamp(y, minY, maxY);

            selRT.anchoredPosition = new Vector2(x, y);
        }

        private void RestoreSelectorHomeParent()
        {
            if (selectorGO == null || _selectorHomeParent == null) return;
            try
            {
                // false: home layout owns anchors; next EnsureCreatorRatingChrome rewrites pose.
                selectorGO.transform.SetParent(_selectorHomeParent, false);
                int max = Mathf.Max(0, _selectorHomeParent.childCount - 1);
                selectorGO.transform.SetSiblingIndex(Mathf.Clamp(_selectorHomeSibling, 0, max));
            }
            catch { }
            _selectorHomeParent = null;
            _selectorHomeSibling = 0;
        }

        private void RefreshDisplay()
        {
            int r = Mathf.Clamp(currentRating, 0, 5);
            Color c = RatingHandler.RatingColors[r];
            if (digitText != null)
            {
                digitText.text = r.ToString();
                digitText.color = c;
            }
            if (starImage != null)
                starImage.color = r > 0 ? c : new Color(1f, 1f, 1f, 0.25f);

            if (optionImages == null) return;
            for (int i = 0; i < optionImages.Length && i < 6; i++)
            {
                if (optionImages[i] != null)
                    optionImages[i].color = RatingHandler.RatingColors[i];
                if (optionTexts != null && i < optionTexts.Length && optionTexts[i] != null)
                    optionTexts[i].color = i == 0 ? Color.red : Color.black;
                if (borderGOs != null && i < borderGOs.Length && borderGOs[i] != null)
                    borderGOs[i].SetActive(i == r);
            }
        }

        private void OnDisable()
        {
            // Row pool recycle — close if still open. Skip during open reparent (same-frame noise).
            if (_suppressDisableClose) return;
            if (_openHandler == this)
                CloseSelector();
        }
    }

    public partial class GalleryPanel
    {
        private bool creatorRatedOnlyFilter;
        private bool _creatorSortSnapshotValid;
        private SortType _creatorSortSnapshotType = SortType.Name;
        private SortDirection _creatorSortSnapshotDir = SortDirection.Ascending;

        private GameObject _creatorRatingPickerBlocker;
        private Coroutine _creatorRatingPickerBlockerCo;
        private bool _creatorRatingPickerDismissArmed;

        private const string CreatorRatingChildName = "CreatorRating";
        private const string CreatorRatingSelectorName = "CreatorRatingSelector";

        internal void NotifyCreatorRatingChanged()
        {
            // Rating change while sorted/filtered by rating needs list rebuild.
            SortState st = GetSortState("Creator");
            bool needRebuild = creatorRatedOnlyFilter
                || (st != null && st.Type == SortType.Rating);
            if (!needRebuild) return;

            _creatorVirtViewSig = null;
            _titleCreatorVirtViewSig = null;
            try { UpdateTabs(); } catch { }
            try
            {
                if (titleCreatorDropdown != null && titleCreatorDropdown.activeSelf)
                {
                    RebuildTitleCreatorVirtView(force: true);
                    UpdateTitleCreatorVirtualVisible();
                }
            }
            catch { }
        }

        /// <summary>
        /// Arm click-outside dismiss next frame so the opening click cannot hit the blocker.
        /// </summary>
        internal void BeginCreatorRatingPickerDismissGuard()
        {
            if (_creatorRatingPickerBlockerCo != null)
            {
                try { StopCoroutine(_creatorRatingPickerBlockerCo); } catch { }
                _creatorRatingPickerBlockerCo = null;
            }
            _creatorRatingPickerDismissArmed = false;
            EnsureCreatorRatingPickerBlocker();
            if (_creatorRatingPickerBlocker != null)
            {
                _creatorRatingPickerBlocker.SetActive(false);
                _creatorRatingPickerBlocker.transform.SetAsLastSibling();
            }
            _creatorRatingPickerBlockerCo = StartCoroutine(ArmCreatorRatingPickerBlockerNextFrame());
        }

        internal void EndCreatorRatingPickerDismissGuard()
        {
            if (_creatorRatingPickerBlockerCo != null)
            {
                try { StopCoroutine(_creatorRatingPickerBlockerCo); } catch { }
                _creatorRatingPickerBlockerCo = null;
            }
            _creatorRatingPickerDismissArmed = false;
            if (_creatorRatingPickerBlocker != null)
                _creatorRatingPickerBlocker.SetActive(false);
        }

        private IEnumerator ArmCreatorRatingPickerBlockerNextFrame()
        {
            yield return null;
            _creatorRatingPickerBlockerCo = null;
            if (CreatorRatingRowHandler.OpenHandler == null) yield break;

            EnsureCreatorRatingPickerBlocker();
            if (_creatorRatingPickerBlocker == null) yield break;

            _creatorRatingPickerBlocker.SetActive(true);
            _creatorRatingPickerBlocker.transform.SetAsLastSibling();
            // Keep picker above blocker.
            GameObject sel = CreatorRatingRowHandler.OpenHandler.SelectorGO;
            if (sel != null)
                sel.transform.SetAsLastSibling();
            _creatorRatingPickerDismissArmed = true;
        }

        private void EnsureCreatorRatingPickerBlocker()
        {
            if (backgroundBoxGO == null) return;
            if (_creatorRatingPickerBlocker != null) return;

            _creatorRatingPickerBlocker = UI.CreateChildRT(backgroundBoxGO, "CreatorRatingPickerBlocker", AnchorPresets.stretchAll);
            Image img = UI.AddImage(_creatorRatingPickerBlocker, new Color(0f, 0f, 0f, 0f));
            if (img != null) img.raycastTarget = true;
            Button btn = _creatorRatingPickerBlocker.AddComponent<Button>();
            UI.ConfigButtonFlat(btn, applyColors: false);
            btn.targetGraphic = img;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnCreatorRatingPickerBlockerClicked);
            _creatorRatingPickerBlocker.SetActive(false);
        }

        private void OnCreatorRatingPickerBlockerClicked()
        {
            if (!_creatorRatingPickerDismissArmed) return;
            CreatorRatingRowHandler.CloseAnyOpen();
        }

        /// <summary>
        /// Display sort only. Rated-only forces Rating type without mutating saved Creator sort;
        /// keeps Rating direction if user already chose Rating asc/desc.
        /// </summary>
        private SortState GetCreatorListSortState()
        {
            if (creatorRatedOnlyFilter)
            {
                SortState st = GetSortState("Creator");
                SortDirection dir = SortDirection.Descending;
                if (st != null && st.Type == SortType.Rating)
                    dir = st.Direction;
                return new SortState(SortType.Rating, dir);
            }
            return GetSortState("Creator");
        }

        private void ToggleCreatorRatedOnlyFilter()
        {
            CreatorRatingRowHandler.CloseAnyOpen();

            if (!creatorRatedOnlyFilter)
            {
                // Turning ON — snapshot current sort; do not persist display override.
                SortState st = GetSortState("Creator");
                if (st != null)
                {
                    _creatorSortSnapshotType = st.Type;
                    _creatorSortSnapshotDir = st.Direction;
                    _creatorSortSnapshotValid = true;
                }
                creatorRatedOnlyFilter = true;
            }
            else
            {
                // Turning OFF — restore alphabetical (or prior snapshot).
                creatorRatedOnlyFilter = false;
                SortState st = GetSortState("Creator");
                if (st != null)
                {
                    if (_creatorSortSnapshotValid)
                    {
                        st.Type = _creatorSortSnapshotType;
                        st.Direction = _creatorSortSnapshotDir;
                    }
                    else
                    {
                        st.Type = SortType.Name;
                        st.Direction = SortDirection.Ascending;
                    }
                    SaveSortState("Creator", st);
                }
                _creatorSortSnapshotValid = false;
            }

            _creatorVirtViewSig = null;
            _titleCreatorVirtViewSig = null;
            try { SyncTitleCreatorRatedOnlyButton(); } catch { }
            try { SyncSidePaneTopSortButtonVisuals(); } catch { }
            try { UpdateTabs(); } catch { }
            try
            {
                if (titleCreatorDropdown != null && titleCreatorDropdown.activeSelf)
                {
                    RebuildTitleCreatorVirtView(force: true);
                    UpdateTitleCreatorVirtualVisible();
                }
            }
            catch { }
            try
            {
                ShowTemporaryStatus(
                    creatorRatedOnlyFilter
                        ? VPBTranslation.T("gallery.creator.rated_only_on", "Creators: rated only (high→low)")
                        : VPBTranslation.T("gallery.creator.rated_only_off", "Creators: show all (restored sort)"),
                    1.4f);
            }
            catch { }
        }

        private void SyncTitleCreatorRatedOnlyButton()
        {
            if (titleCreatorRatedOnlyBtnText != null)
                titleCreatorRatedOnlyBtnText.color = creatorRatedOnlyFilter ? Color.green : Color.white;
            if (titleCreatorRatedOnlyBtnBackdrop != null)
            {
                titleCreatorRatedOnlyBtnBackdrop.color = creatorRatedOnlyFilter
                    ? ColorHistoryAccent
                    : new Color(0f, 0f, 0f, 0.45f);
            }
        }

        private static int CreatorRatingRevisionFragment()
        {
            try { return CreatorRatingsManager.Instance.DataRevision; }
            catch { return 0; }
        }

        private bool CreatorPassesRatedOnlyFilter(string creatorName)
        {
            if (!creatorRatedOnlyFilter) return true;
            if (string.IsNullOrEmpty(creatorName)) return false;
            try { return CreatorRatingsManager.Instance.GetRating(creatorName) > 0; }
            catch { return false; }
        }

        private GameObject ResolveCreatorRatingSelector(GameObject btnGO, CreatorRatingRowHandler handler)
        {
            if (btnGO != null)
            {
                Transform t = btnGO.transform.Find(CreatorRatingSelectorName);
                if (t != null) return t.gameObject;
            }
            if (handler != null && handler.SelectorGO != null && handler.SelectorGO)
                return handler.SelectorGO;
            return null;
        }

        private void EnsureCreatorRatingChrome(GameObject btnGO)
        {
            if (btnGO == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float edge = GalleryUiDesignTokens.ButtonSizeRef * s;
            float cell = GalleryUiDesignTokens.ButtonSizeRef * s;
            float gap = GalleryUiDesignTokens.SideTabRowSpacingRef * 0.5f * s;
            float pad = 1f * s;
            int optFont = GalleryUiDesignTokens.FontBodyRef;

            CreatorRatingRowHandler handler;
            GameObject starGO;
            Transform existing = btnGO.transform.Find(CreatorRatingChildName);
            if (existing != null)
            {
                starGO = existing.gameObject;
                handler = starGO.GetComponent<CreatorRatingRowHandler>();
                if (handler == null) handler = starGO.AddComponent<CreatorRatingRowHandler>();
            }
            else
            {
                starGO = CreateCreatorRatingBadge(btnGO);
                handler = starGO.GetComponent<CreatorRatingRowHandler>();
            }

            GameObject selectorGO = ResolveCreatorRatingSelector(btnGO, handler);
            if (selectorGO == null)
                selectorGO = CreateCreatorRatingSelector(btnGO, handler, cell, optFont, s);

            // Closed picker must live under the row so Find works after reparent-dismiss.
            if (selectorGO != null
                && selectorGO.transform.parent != btnGO.transform
                && handler != null
                && !handler.IsSelectorOpen)
            {
                try { selectorGO.transform.SetParent(btnGO.transform, false); } catch { }
            }

            ApplyCreatorRatingChromeLayout(btnGO, starGO, selectorGO, edge, cell, gap, pad, optFont, s);
        }

        private GameObject CreateCreatorRatingBadge(GameObject btnGO)
        {
            GameObject starGO = new GameObject(CreatorRatingChildName);
            starGO.transform.SetParent(btnGO.transform, false);

            Image bg = starGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = true;

            RectTransform rt = starGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);

            Button starBtn = starGO.AddComponent<Button>();
            UI.ConfigButtonFlat(starBtn, applyColors: true);
            starBtn.targetGraphic = bg;
            starBtn.navigation = new Navigation { mode = Navigation.Mode.None };

            GameObject labelGO = new GameObject("Digit");
            labelGO.transform.SetParent(starGO.transform, false);
            Text digit = labelGO.AddComponent<Text>();
            digit.alignment = TextAnchor.MiddleCenter;
            digit.raycastTarget = false;
            digit.text = "0";
            VPBUiFont.ApplyTo(digit);
            RectTransform digitRT = labelGO.GetComponent<RectTransform>();
            digitRT.anchorMin = Vector2.zero;
            digitRT.anchorMax = Vector2.one;
            digitRT.offsetMin = Vector2.zero;
            digitRT.offsetMax = Vector2.zero;

            Sprite onSpr = ratingStarNormalSprite;
            if (onSpr == null)
            {
                try { onSpr = UI.LoadIconSprite("vpb_icons/star.png", Color.white); } catch { }
            }
            if (onSpr != null)
            {
                GameObject iconGO = new GameObject("StarIcon");
                iconGO.transform.SetParent(starGO.transform, false);
                Image iconImg = iconGO.AddComponent<Image>();
                iconImg.sprite = onSpr;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
                iconImg.color = new Color(1f, 1f, 1f, 0.12f);
                RectTransform irt = iconGO.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.12f, 0.12f);
                irt.anchorMax = new Vector2(0.88f, 0.88f);
                irt.offsetMin = Vector2.zero;
                irt.offsetMax = Vector2.zero;
                iconGO.transform.SetAsFirstSibling();
            }

            starGO.AddComponent<CreatorRatingRowHandler>();
            AddTooltip(starGO, "gallery.tooltip.creator_rate",
                "Rate creator: click → pick 0–5. Digit shows value. Same digit again clears. Click outside to close.");
            return starGO;
        }

        private GameObject CreateCreatorRatingSelector(GameObject btnGO, CreatorRatingRowHandler handler, float cell, int optFont, float s)
        {
            GameObject selectorGO = new GameObject(CreatorRatingSelectorName);
            selectorGO.transform.SetParent(btnGO.transform, false);
            RectTransform selectorRT = selectorGO.AddComponent<RectTransform>();
            selectorRT.anchorMin = new Vector2(1f, 0.5f);
            selectorRT.anchorMax = new Vector2(1f, 0.5f);
            selectorRT.pivot = new Vector2(1f, 1f);

            CanvasGroup selectorCG = selectorGO.AddComponent<CanvasGroup>();
            selectorCG.alpha = 0f;
            selectorCG.interactable = false;
            selectorCG.blocksRaycasts = false;

            UI.AddImage(selectorGO, new Color(0.05f, 0.05f, 0.05f, 0.96f));

            GridLayoutGroup selectorGrid = selectorGO.AddComponent<GridLayoutGroup>();
            selectorGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            selectorGrid.constraintCount = 2;
            selectorGrid.childAlignment = TextAnchor.UpperLeft;

            Image[] optImages = new Image[6];
            Text[] optTexts = new Text[6];
            GameObject[] optBorders = new GameObject[6];
            for (int i = 0; i <= 5; i++)
            {
                int ratingValue = i;
                string label = i == 0 ? "X" : i.ToString();
                CreatorRatingRowHandler captured = handler;
                GameObject optBtnGO = UI.CreateUIButton(selectorGO, cell, cell, label, optFont, 0, 0, AnchorPresets.middleCenter,
                    () => captured.SetRating(ratingValue));
                optBtnGO.GetComponent<Button>().navigation = new Navigation { mode = Navigation.Mode.None };
                optImages[i] = optBtnGO.GetComponent<Image>();
                optImages[i].color = RatingHandler.RatingColors[i];
                optTexts[i] = optBtnGO.GetComponentInChildren<Text>();
                if (optTexts[i] != null)
                {
                    optTexts[i].color = i == 0 ? Color.red : Color.black;
                    GalleryUiMetrics.ApplyFont(optTexts[i], optFont, s, GalleryUiDesignTokens.FontMinRef);
                }

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
                optBorders[i] = borderGO;
            }

            handler.AttachSelector(selectorGO, optImages, optTexts, optBorders);
            selectorGO.SetActive(true);
            return selectorGO;
        }

        private void ApplyCreatorRatingChromeLayout(
            GameObject btnGO, GameObject starGO, GameObject selectorGO,
            float edge, float cell, float gap, float pad, int optFont, float s)
        {
            RectTransform starRT = starGO != null ? starGO.GetComponent<RectTransform>() : null;
            if (starRT != null)
            {
                starRT.sizeDelta = new Vector2(edge, edge);
                starRT.anchoredPosition = new Vector2(-2f * s, 0f);
            }

            Text digitLabel = null;
            if (starGO != null)
            {
                Transform digitTr = starGO.transform.Find("Digit");
                if (digitTr != null) digitLabel = digitTr.GetComponent<Text>();
            }
            if (digitLabel != null)
                GalleryUiMetrics.ApplyFont(digitLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);

            if (selectorGO != null)
            {
                CreatorRatingRowHandler handler = starGO != null
                    ? starGO.GetComponent<CreatorRatingRowHandler>()
                    : null;
                bool pickerOpen = handler != null && handler.IsSelectorOpen;
                RectTransform srt = selectorGO.GetComponent<RectTransform>();
                if (srt != null)
                {
                    float selW = pad + cell + gap + cell + pad;
                    float selH = pad + (cell + gap) * 3f - gap + pad;
                    srt.sizeDelta = new Vector2(selW, selH);
                    if (!pickerOpen)
                    {
                        // Closed: parked under row for Find() + ScrollRect recycle.
                        srt.anchorMin = new Vector2(1f, 0.5f);
                        srt.anchorMax = new Vector2(1f, 0.5f);
                        srt.pivot = new Vector2(1f, 1f);
                        srt.anchoredPosition = new Vector2(-2f * s, -(edge * 0.5f + 2f * s));
                    }
                }
                GridLayoutGroup glg = selectorGO.GetComponent<GridLayoutGroup>();
                if (glg != null)
                {
                    glg.cellSize = new Vector2(cell, cell);
                    glg.spacing = new Vector2(gap, gap);
                    glg.padding = new RectOffset(
                        Mathf.RoundToInt(pad), Mathf.RoundToInt(pad),
                        Mathf.RoundToInt(pad), Mathf.RoundToInt(pad));
                }
                for (int ci = 0; ci < selectorGO.transform.childCount; ci++)
                {
                    Transform child = selectorGO.transform.GetChild(ci);
                    if (child == null) continue;
                    Text ot = child.GetComponentInChildren<Text>(true);
                    if (ot != null)
                        GalleryUiMetrics.ApplyFont(ot, optFont, s, GalleryUiDesignTokens.FontMinRef);
                    RectTransform crt = child as RectTransform;
                    if (crt != null)
                        crt.sizeDelta = new Vector2(cell, cell);
                }
                // Virt rebind / scale refresh while open must not leave picker at pane corner.
                if (pickerOpen)
                    handler.PositionSelectorNearStar();
            }

            Transform textTr = btnGO.transform.Find("Text");
            Text rowTxt = textTr != null ? textTr.GetComponent<Text>() : null;
            if (rowTxt != null && rowTxt.transform.parent == btnGO.transform)
            {
                RectTransform txtRT = rowTxt.rectTransform;
                if (txtRT != null)
                    txtRT.offsetMax = new Vector2(-(edge + 6f * s), txtRT.offsetMax.y);
            }
        }

        private void BindCreatorRatingChrome(GameObject btnGO, string creatorName)
        {
            if (btnGO == null) return;
            EnsureCreatorRatingChrome(btnGO);

            Transform starTr = btnGO.transform.Find(CreatorRatingChildName);
            if (starTr == null) return;
            GameObject starGO = starTr.gameObject;

            Text digit = null;
            Transform digitTr = starTr.Find("Digit");
            if (digitTr != null) digit = digitTr.GetComponent<Text>();

            Image starImg = null;
            Transform iconTr = starTr.Find("StarIcon");
            if (iconTr != null) starImg = iconTr.GetComponent<Image>();

            CreatorRatingRowHandler handler = starGO.GetComponent<CreatorRatingRowHandler>();
            if (handler == null) handler = starGO.AddComponent<CreatorRatingRowHandler>();

            GameObject selectorGO = ResolveCreatorRatingSelector(btnGO, handler);
            handler.Bind(this, creatorName, digit, starImg, selectorGO);

            Button starBtn = starGO.GetComponent<Button>();
            if (starBtn != null)
            {
                starBtn.onClick.RemoveAllListeners();
                CreatorRatingRowHandler captured = handler;
                starBtn.onClick.AddListener(captured.OnStarClicked);
            }

            UIRightClickDelegate starRc = starGO.GetComponent<UIRightClickDelegate>();
            if (starRc == null) starRc = starGO.AddComponent<UIRightClickDelegate>();
            starRc.OnRightClick = () => handler.ClearRating();
        }

    }
}
