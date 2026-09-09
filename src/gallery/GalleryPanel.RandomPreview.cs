using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using MVR.FileManagement;

namespace VPB
{
    /// <summary>
    /// Panel side of random hover preview: sample from the same pool a random button would draw from,
    /// hand out the preview thumbnail, then launch the exact entry the user saw. Sampling is by
    /// rejection so no pool copy happens on the hover path. Also owns the toolbox overlay (dim +
    /// centered card on the file grid).
    /// </summary>
    public partial class GalleryPanel
    {
        /// <summary>True when this panel is already listing <paramref name="categoryName"/> (null/empty = current view).</summary>
        internal bool QuickMenu_IsShowingRandomCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return true;
            string cur = currentCategoryTitle ?? "";
            if (string.Equals(cur, categoryName, StringComparison.OrdinalIgnoreCase)) return true;
            // Same alias the launch path uses: quick-menu "Skin" is category "Person Skin" in some builds.
            if (string.Equals(categoryName, "Skin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cur, "Person Skin", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        internal int QuickMenu_CopyRandomPoolFromCurrentView(List<FileEntry> dest)
        {
            if (dest == null)
            {
                _copiedRandomPoolFilterCaption = null;
                return 0;
            }
            dest.Clear();
            try
            {
                List<FileEntry> pool = GetRandomCandidatePool();
                if (pool != null && pool.Count > 0)
                {
                    if (dest.Capacity < pool.Count) dest.Capacity = pool.Count;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        FileEntry f = pool[i];
                        if (f != null) dest.Add(f);
                    }
                }
            }
            catch { }
            _copiedRandomPoolFilterCaption = FormatRandomPoolFilterCaption(dest.Count);
            return dest.Count;
        }

        private string _copiedRandomPoolFilterCaption;

        /// <summary>Caption from the last <see cref="QuickMenu_CopyRandomPoolFromCurrentView"/> (watch reel). Null if unfiltered.</summary>
        internal string QuickMenu_CopiedRandomPoolFilterCaption
        {
            get { return _copiedRandomPoolFilterCaption; }
        }

        /// <summary>Live caption for the view now showing. Null when random uses the unfiltered category set.</summary>
        internal string QuickMenu_FormatRandomPoolFilterCaption()
        {
            List<FileEntry> pool = GetRandomCandidatePool();
            int n = pool != null ? pool.Count : 0;
            return FormatRandomPoolFilterCaption(n);
        }

        private string FormatRandomPoolFilterCaption(int poolCount)
        {
            bool userFiltered = false;
            try
            {
                userFiltered = HasActiveNameFilter()
                    || HasActiveBrowseFiltersExcludingTitleSearch()
                    || IsFilterActive;
            }
            catch { }
            if (!userFiltered) return null;

            int total = 0;
            try
            {
                string cat = currentCategoryTitle ?? "";
                if (categoryCounts != null && cat.Length > 0)
                    categoryCounts.TryGetValue(cat, out total);
                if (total <= poolCount)
                {
                    if (cat.IndexOf("Clothing", StringComparison.OrdinalIgnoreCase) >= 0
                        && clothingSubfilterCountAll > total)
                        total = clothingSubfilterCountAll;
                    else if (cat.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0
                        && hairSubfilterCountAll > total)
                        total = hairSubfilterCountAll;
                    else if (cat.IndexOf("Appearance", StringComparison.OrdinalIgnoreCase) >= 0
                        && appearanceSubfilterCountAll > total)
                        total = appearanceSubfilterCountAll;
                }
            }
            catch { }

            if (total > poolCount)
            {
                return string.Format(
                    VPBTranslation.T("hook.qmpreview.filtered_xy", "Filtered ({0} out of {1})"),
                    poolCount, total);
            }
            return string.Format(
                VPBTranslation.T("hook.qmpreview.filtered_x", "Filtered ({0})"),
                poolCount);
        }

        internal static Text QuickMenu_CreateFilterHeader(GameObject cardGo, int font, float height)
        {
            if (cardGo == null) return null;
            GameObject strip = UI.CreateChildRT(cardGo, "FilterHeader", AnchorPresets.hStretchTop,
                new Vector2(0f, height), Vector2.zero);
            if (strip == null) return null;
            Image bg = UI.AddImage(strip, new Color(0.12f, 0.09f, 0.03f, 0.94f), false);
            if (bg != null) bg.raycastTarget = false;
            int f = font;
            if (f < GalleryUiDesignTokens.FontTitleRef) f = GalleryUiDesignTokens.FontTitleRef;
            Text t = UI.CreateLabel(strip, "", f, GalleryUiColorTokens.RandomGlyph,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                false, false, AnchorPresets.stretchAll, Vector2.zero, Vector2.zero, "FilterLabel");
            if (t != null)
            {
                t.resizeTextForBestFit = false;
                t.fontStyle = FontStyle.Bold;
            }
            strip.SetActive(false);
            return t;
        }

        internal static void QuickMenu_ApplyFilterStrip(Text label, string caption)
        {
            if (label == null) return;
            bool on = !string.IsNullOrEmpty(caption);
            Transform parent = label.transform.parent;
            if (parent != null)
            {
                GameObject strip = parent.gameObject;
                if (strip.activeSelf != on) strip.SetActive(on);
            }
            if (on) label.text = caption;
        }

        internal int QuickMenu_FillRandomSampleFromCurrentView(List<FileEntry> dest, int max)
        {
            if (dest == null) return 0;
            dest.Clear();
            if (max <= 0) return 0;

            try
            {
                List<FileEntry> pool = GetRandomCandidatePool();
                if (pool == null || pool.Count == 0) return 0;

                string scope = GetRandomHistoryScope();
                int window = VpbRandomHistory.ComputeWindow(pool.Count);

                int want = Mathf.Min(max, pool.Count);
                int attempts = Mathf.Min(pool.Count * 3 + 8, 512);

                for (int pass = 0; pass < 2 && dest.Count < want; pass++)
                {
                    int passWindow = (pass == 0) ? window : 0;
                    for (int a = 0; a < attempts && dest.Count < want; a++)
                    {
                        FileEntry cand = pool[VpbRandom.Next(pool.Count)];
                        if (cand == null) continue;
                        if (passWindow > 0 && VpbRandomHistory.IsRecent(scope, cand, passWindow)) continue;

                        bool dup = false;
                        for (int i = 0; i < dest.Count; i++)
                        {
                            if (ReferenceEquals(dest[i], cand)) { dup = true; break; }
                            string a1 = dest[i].Path ?? dest[i].Uid;
                            string a2 = cand.Path ?? cand.Uid;
                            if (!string.IsNullOrEmpty(a1) && string.Equals(a1, a2, StringComparison.OrdinalIgnoreCase))
                            { dup = true; break; }
                        }
                        if (dup) continue;

                        dest.Add(cand);
                    }
                }
            }
            catch { }
            return dest.Count;
        }

        internal Coroutine QuickMenu_PrepareRandomSampleForCategory(string categoryName, List<FileEntry> dest, Action<int> onDone)
        {
            try { return StartCoroutine(QuickMenu_PrepareRandomSampleRoutine(categoryName, dest, onDone)); }
            catch
            {
                if (onDone != null) { try { onDone(0); } catch { } }
                return null;
            }
        }

        private IEnumerator QuickMenu_PrepareRandomSampleRoutine(string categoryName, List<FileEntry> dest, Action<int> onDone)
        {
            int filled = 0;

            if (QuickMenu_IsShowingRandomCategory(categoryName))
            {
                filled = QuickMenu_CopyRandomPoolFromCurrentView(dest);
                if (onDone != null) { try { onDone(filled); } catch { } }
                yield break;
            }

            string prevTitle = null;
            string prevExt = null;
            string prevPath = null;
            try { prevTitle = currentCategoryTitle; } catch { prevTitle = null; }
            try { prevExt = currentExtension; } catch { prevExt = null; }
            try { prevPath = currentPath; } catch { prevPath = null; }

            string targetUid = null;
            try { targetUid = QuickMenu_GetSelectedTargetPersonUid(); } catch { targetUid = null; }

            Gallery.Category cat;
            if (!QuickMenu_TryResolveCategory(categoryName, out cat))
            {
                if (onDone != null) { try { onDone(0); } catch { } }
                yield break;
            }

            try { Show(cat.name, cat.extension, cat.path); } catch { }

            yield return null;
            int guard = 0;
            while (refreshCoroutine != null && guard < 600)
            {
                guard++;
                yield return null;
            }
            if (guard == 0) yield return null;

            filled = QuickMenu_CopyRandomPoolFromCurrentView(dest);

            // Preview must never leave the panel parked on another category.
            if (!string.IsNullOrEmpty(prevTitle))
            {
                try { Show(prevTitle, prevExt, prevPath); } catch { }
                if (!string.IsNullOrEmpty(targetUid))
                {
                    try { QuickMenu_SetSelectedTargetPersonUid(targetUid); } catch { }
                }
            }

            if (onDone != null) { try { onDone(filled); } catch { } }
        }

        internal bool QuickMenu_TryResolveCategory(string categoryName, out Gallery.Category cat)
        {
            cat = default(Gallery.Category);
            if (string.IsNullOrEmpty(categoryName)) return false;
            try
            {
                if (categories == null) return false;
                bool skinAlias = string.Equals(categoryName, "Skin", StringComparison.OrdinalIgnoreCase);
                for (int pass = 0; pass < (skinAlias ? 2 : 1); pass++)
                {
                    string name = (pass == 0) ? categoryName : "Person Skin";
                    for (int i = 0; i < categories.Count; i++)
                    {
                        var c = categories[i];
                        if (string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            cat = c;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Bind a preview thumbnail to an arbitrary RawImage. Same tier as hover preview / detail strip:
        /// denom 1 + Unity decode. Grid-column denom would hand back the tile-sized decode, so a small
        /// pane made the preview card blurry even though the card itself never shrinks.
        /// </summary>
        internal void QuickMenu_LoadPreviewThumbnail(FileEntry file, RawImage target)
        {
            if (target == null) return;
            if (file == null)
            {
                ClearThumbnailTarget(target);
                return;
            }
            try
            {
                LoadThumbnail(
                    file,
                    target,
                    gridThumbnailContext: false,
                    turboJpegThumbnailDenom: 1,
                    thumbnailUnityDecodeOnly: true);
            }
            catch { ClearThumbnailTarget(target); }
        }

        internal void QuickMenu_ClearPreviewThumbnail(RawImage target)
        {
            ClearThumbnailTarget(target);
        }

        /// <summary>Short display name for the preview caption (file name, no extension, no package prefix).</summary>
        internal static string QuickMenu_GetPreviewLabel(FileEntry file)
        {
            if (file == null) return "";
            string s = null;
            try { s = file.Name; } catch { s = null; }
            if (string.IsNullOrEmpty(s))
            {
                try { s = file.Path ?? file.Uid; } catch { s = null; }
            }
            if (string.IsNullOrEmpty(s)) return "";

            s = s.Replace('\\', '/');
            int slash = s.LastIndexOf('/');
            if (slash >= 0 && slash < s.Length - 1) s = s.Substring(slash + 1);
            int dot = s.LastIndexOf('.');
            if (dot > 0) s = s.Substring(0, dot);
            return s;
        }

        /// <summary>
        /// Two-band preview caption in the grid's own shape: <paramref name="primary"/> is the leaf (or
        /// sole package) name, <paramref name="secondary"/> the muted creator plus package line.
        /// Warm path — runs once per hover draw, not per frame.
        /// </summary>
        internal void QuickMenu_GetPreviewLabelLines(FileEntry file, out string primary, out string secondary)
        {
            primary = "";
            secondary = "";
            if (file == null) return;

            string p;
            string pkg;
            string creator;
            try { GetGridItemLabelLines(file, out p, out pkg, out creator); }
            catch { p = null; pkg = null; creator = null; }

            if (string.IsNullOrEmpty(p)) p = QuickMenu_GetPreviewLabel(file);
            primary = p ?? "";

            if (!string.IsNullOrEmpty(creator)
                && string.Equals(creator, primary, StringComparison.OrdinalIgnoreCase))
                creator = null;
            if (!string.IsNullOrEmpty(pkg)
                && string.Equals(pkg, primary, StringComparison.OrdinalIgnoreCase))
                pkg = null;

            if (!string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(pkg))
                secondary = creator + "  ·  " + pkg;
            else if (!string.IsNullOrEmpty(creator))
                secondary = creator;
            else
                secondary = pkg ?? "";
        }

        /// <summary>Launch one preselected entry from <paramref name="categoryName"/> (hover preview click).</summary>
        internal void QuickMenu_LoadPickedFromCategory(string categoryName, FileEntry file, bool preserveUi, bool preserveTarget)
        {
            try
            {
                if (file == null)
                {
                    QuickMenu_LoadRandomFromCategory(categoryName, preserveUi, preserveTarget);
                    return;
                }
                if (string.IsNullOrEmpty(categoryName))
                {
                    if (!ApplyPickedRandomEntry(file)) LoadRandom();
                    return;
                }
                StartCoroutine(QuickMenu_LoadRandomFromCategoryRoutine(categoryName, preserveUi, preserveTarget, file));
            }
            catch { }
        }

        // ---------------------------------------------------------------- toolbox overlay

        private const float TboxRandPreviewThumb = 380f;
        private const float TboxRandPreviewPad = 14f;
        private const float TboxRandPreviewLabelH = 26f;
        private const float TboxRandPreviewSubH = 20f;
        private const float TboxRandPreviewFilterH = 36f;
        private const int TboxRandPreviewFilterFont = 22;
        /// <summary>Card is clipped by the grid viewport, so shrink to fit rather than overflow a small pane.</summary>
        private const float TboxRandPreviewHostMargin = 16f;
        private const float TboxRandPreviewMinThumb = 140f;
        private const float TboxRandPreviewDimAlpha = 0.55f;
        // Absolute px at ChromeScale 1. Button fraction (0.22) of this card is a lozenge.
        private const float TboxRandPreviewCornerPx = 6f;
        private static readonly Color TboxRandPreviewThumbPlaceholder = new Color(0.25f, 0.25f, 0.25f, 0.55f);

        private GameObject _tboxRandPreviewRoot;
        private RectTransform _tboxRandPreviewRootRt;
        private RectTransform _tboxRandPreviewCardRt;
        private RectTransform _tboxRandPreviewThumbRt;
        private RoundedRect _tboxRandPreviewCardRounded;
        private RawImage _tboxRandPreviewImg;
        private Text _tboxRandPreviewLabel;
        private Text _tboxRandPreviewSub;
        private Text _tboxRandPreviewFilterLabel;
        private RectTransform _tboxRandPreviewFilterStripRt;
        private FileEntry _tboxRandPreviewPick;
        private FileEntry _tboxRandPreviewLastPick;
        private List<FileEntry> _tboxRandPreviewScratch;
        private bool _tboxRandPreviewHover;
        private Action<bool> _tboxRandPreviewHoverHandler;

        internal bool TboxRandomPreviewIsShowing()
        {
            return _tboxRandPreviewRoot != null && _tboxRandPreviewRoot.activeSelf;
        }

        private static bool TboxRandomPreviewEnabled()
        {
            try
            {
                var c = VPBConfig.Instance;
                return c != null && c.QuickMenuRandomHoverPreview;
            }
            catch { return false; }
        }

        private void TboxBindRandomPreviewHover()
        {
            if (tboxLoadRandomBtn == null) return;
            UIHoverDelegate del = tboxLoadRandomBtn.GetComponent<UIHoverDelegate>();
            if (del == null) del = tboxLoadRandomBtn.AddComponent<UIHoverDelegate>();
            if (_tboxRandPreviewHoverHandler != null)
                del.OnHoverChange -= _tboxRandPreviewHoverHandler;
            _tboxRandPreviewHoverHandler = OnTboxRandomPreviewHover;
            del.OnHoverChange += _tboxRandPreviewHoverHandler;

            TboxRandomPreviewScroll wheel = tboxLoadRandomBtn.GetComponent<TboxRandomPreviewScroll>();
            if (wheel == null) wheel = tboxLoadRandomBtn.AddComponent<TboxRandomPreviewScroll>();
            wheel.panel = this;
        }

        private void OnTboxRandomPreviewHover(bool enter)
        {
            if (enter) TboxBeginRandomPreview();
            else TboxEndRandomPreview();
        }

        private void TboxRerollRandomPreviewFromScroll()
        {
            if (!_tboxRandPreviewHover) return;
            TboxBeginRandomPreview();
        }

        private string TboxRandomButtonTooltip()
        {
            string baseTip = VPBTranslation.T("gallery.tooltip.load_random", "Random in current filtered view (not a preset Dice)");
            if (!TboxRandomPreviewEnabled()) return baseTip;
            return baseTip + "\n" + VPBTranslation.T("gallery.tooltip.random_preview_scroll", "Scroll wheel: next preview");
        }

        private void TboxBeginRandomPreview()
        {
            if (!TboxRandomPreviewEnabled())
            {
                TboxHideRandomPreview(true);
                return;
            }

            _tboxRandPreviewHover = true;
            if (_tboxRandPreviewScratch == null) _tboxRandPreviewScratch = new List<FileEntry>(4);
            int n = QuickMenu_FillRandomSampleFromCurrentView(_tboxRandPreviewScratch, 3);
            FileEntry pick = null;
            if (n > 0)
            {
                for (int i = 0; i < _tboxRandPreviewScratch.Count; i++)
                {
                    FileEntry cand = _tboxRandPreviewScratch[i];
                    if (cand == null) continue;
                    if (!TboxPreviewSameEntry(cand, _tboxRandPreviewLastPick))
                    {
                        pick = cand;
                        break;
                    }
                }
                if (pick == null) pick = _tboxRandPreviewScratch[0];
            }
            _tboxRandPreviewPick = pick;
            _tboxRandPreviewLastPick = pick;
            TboxApplyRandomPreviewVisual();
        }

        private void TboxEndRandomPreview()
        {
            _tboxRandPreviewHover = false;
            _tboxRandPreviewPick = null;
            TboxHideRandomPreview(false);
        }

        private void TboxLoadRandomClick()
        {
            FileEntry pick = _tboxRandPreviewPick;
            _tboxRandPreviewPick = null;
            bool applied = false;
            try
            {
                if (pick != null) applied = ApplyPickedRandomEntry(pick);
            }
            catch { applied = false; }
            if (this == null) return;
            if (!applied)
            {
                try { LoadRandom(); } catch { }
            }
            if (this == null) return;
            if (_tboxRandPreviewHover && TboxRandomPreviewEnabled())
                TboxBeginRandomPreview();
            else
                TboxHideRandomPreview(true);
        }

        private static bool TboxPreviewSameEntry(FileEntry a, FileEntry b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            string pa = a.Path ?? a.Uid;
            string pb = b.Path ?? b.Uid;
            return !string.IsNullOrEmpty(pa)
                && string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
        }

        private void TboxEnsureRandomPreviewOverlay()
        {
            if (_tboxRandPreviewRoot != null) return;
            GameObject host = null;
            if (contentScrollRT != null) host = contentScrollRT.gameObject;
            if (host == null) host = backgroundBoxGO;
            if (host == null) return;

            _tboxRandPreviewRoot = UI.CreateChildRT(host, "TboxRandomPreview", AnchorPresets.stretchAll);
            _tboxRandPreviewRootRt = _tboxRandPreviewRoot.GetComponent<RectTransform>();
            Image dim = UI.AddImage(_tboxRandPreviewRoot, UI.Black(TboxRandPreviewDimAlpha), true);
            if (dim != null) dim.raycastTarget = true;

            GameObject card = UI.CreateChildRT(_tboxRandPreviewRoot, "Card", AnchorPresets.middleCenter);
            if (card == null)
            {
                _tboxRandPreviewRoot.SetActive(false);
                return;
            }
            _tboxRandPreviewCardRt = card.GetComponent<RectTransform>();
            _tboxRandPreviewCardRounded = card.AddComponent<RoundedRect>();
            _tboxRandPreviewCardRounded.color = GalleryUiColorTokens.ModalSurface;
            _tboxRandPreviewCardRounded.raycastTarget = false;
            _tboxRandPreviewCardRounded.excludeFromGlobalRadiusSync = true;

            GameObject thumbGo = UI.CreateChildRT(card, "Thumb", AnchorPresets.middleCenter);
            _tboxRandPreviewThumbRt = thumbGo != null ? thumbGo.GetComponent<RectTransform>() : null;
            if (thumbGo != null)
            {
                _tboxRandPreviewImg = thumbGo.AddComponent<RawImage>();
                _tboxRandPreviewImg.raycastTarget = false;
                _tboxRandPreviewImg.color = TboxRandPreviewThumbPlaceholder;
            }
            _tboxRandPreviewFilterLabel = QuickMenu_CreateFilterHeader(card,
                TboxRandPreviewFilterFont, TboxRandPreviewFilterH);
            if (_tboxRandPreviewFilterLabel != null && _tboxRandPreviewFilterLabel.transform.parent != null)
                _tboxRandPreviewFilterStripRt = _tboxRandPreviewFilterLabel.transform.parent as RectTransform;

            _tboxRandPreviewLabel = UI.CreateLabel(card, "", GalleryUiDesignTokens.FontTitleRef, GalleryUiColorTokens.TextPrimary,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.bottomMiddle, Vector2.zero, Vector2.zero, "PreviewLabel");
            if (_tboxRandPreviewLabel != null) _tboxRandPreviewLabel.resizeTextForBestFit = false;

            _tboxRandPreviewSub = UI.CreateLabel(card, "", GalleryUiDesignTokens.FontCaptionRef, GalleryUiColorTokens.TextMuted,
                TextAnchor.MiddleCenter, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate, false, false,
                AnchorPresets.bottomMiddle, Vector2.zero, Vector2.zero, "PreviewSubLabel");
            if (_tboxRandPreviewSub != null) _tboxRandPreviewSub.resizeTextForBestFit = false;

            TboxLayoutRandomPreviewCard();
            _tboxRandPreviewRoot.SetActive(false);
        }

        private void TboxApplyRandomPreviewVisual()
        {
            TboxEnsureRandomPreviewOverlay();
            if (_tboxRandPreviewRoot == null) return;

            try { HideHoverPreview(null); } catch { }

            if (!_tboxRandPreviewRoot.activeSelf) _tboxRandPreviewRoot.SetActive(true);
            try { _tboxRandPreviewRoot.transform.SetAsLastSibling(); } catch { }

            if (_tboxRandPreviewPick == null)
            {
                if (_tboxRandPreviewImg != null) ClearThumbnailTarget(_tboxRandPreviewImg);
                TboxSetPreviewLabel(_tboxRandPreviewLabel, VPBTranslation.T("hook.qmpreview.empty", "No items"));
                TboxSetPreviewLabel(_tboxRandPreviewSub, VPBTranslation.T("hook.qmpreview.empty_sub", "Nothing in this pool"));
                QuickMenu_ApplyFilterStrip(_tboxRandPreviewFilterLabel, QuickMenu_FormatRandomPoolFilterCaption());
                TboxLayoutRandomPreviewCard();
                return;
            }

            try { QuickMenu_LoadPreviewThumbnail(_tboxRandPreviewPick, _tboxRandPreviewImg); } catch { }

            string primary;
            string secondary;
            QuickMenu_GetPreviewLabelLines(_tboxRandPreviewPick, out primary, out secondary);
            TboxSetPreviewLabel(_tboxRandPreviewLabel, primary);
            TboxSetPreviewLabel(_tboxRandPreviewSub, secondary);
            QuickMenu_ApplyFilterStrip(_tboxRandPreviewFilterLabel, QuickMenu_FormatRandomPoolFilterCaption());
            TboxLayoutRandomPreviewCard();
        }

        /// <summary>Live ChromeScale + corner sync. Hide immediately when the settings toggle is off.</summary>
        internal void TboxSyncRandomPreviewLiveScale()
        {
            if (!TboxRandomPreviewEnabled())
            {
                TboxHideRandomPreview(true);
                return;
            }
            if (_tboxRandPreviewHover)
            {
                if (_tboxRandPreviewRoot != null && _tboxRandPreviewRoot.activeSelf)
                    TboxLayoutRandomPreviewCard();
                else
                    TboxBeginRandomPreview();
                return;
            }
            if (_tboxRandPreviewRoot != null)
                TboxLayoutRandomPreviewCard();
        }

        private void TboxLayoutRandomPreviewCard()
        {
            if (_tboxRandPreviewCardRt == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float thumb = TboxRandPreviewThumb * s;
            float pad = TboxRandPreviewPad * s;
            float labelH = TboxRandPreviewLabelH * s;
            float subH = TboxRandPreviewSubH * s;
            bool headerOn = _tboxRandPreviewFilterStripRt != null
                && _tboxRandPreviewFilterStripRt.gameObject.activeSelf;
            float filterH = headerOn ? TboxRandPreviewFilterH * s : 0f;

            // Root stretches the grid viewport, which masks: a card wider than the pane loses its edges.
            RectTransform hostRt = _tboxRandPreviewRootRt;
            // Rect is 0 until the first layout pass — clamping then would lock the card to the floor.
            if (hostRt != null && hostRt.rect.width > 1f && hostRt.rect.height > 1f)
            {
                float margin = TboxRandPreviewHostMargin * s;
                float fitW = hostRt.rect.width - margin * 2f - pad * 2f;
                float fitH = hostRt.rect.height - margin * 2f - pad * 2f - labelH - subH - filterH;
                float fit = Mathf.Min(fitW, fitH);
                if (fit < thumb)
                    thumb = Mathf.Max(fit, TboxRandPreviewMinThumb * s);
            }

            float innerW = thumb;
            _tboxRandPreviewCardRt.sizeDelta = new Vector2(thumb + pad * 2f, thumb + pad * 2f + labelH + subH + filterH);
            if (_tboxRandPreviewThumbRt != null)
            {
                _tboxRandPreviewThumbRt.sizeDelta = new Vector2(thumb, thumb);
                _tboxRandPreviewThumbRt.anchoredPosition = new Vector2(0f, (labelH + subH) * 0.5f - filterH * 0.5f);
            }
            if (_tboxRandPreviewFilterStripRt != null)
                _tboxRandPreviewFilterStripRt.sizeDelta = new Vector2(0f, TboxRandPreviewFilterH * s);
            if (_tboxRandPreviewFilterLabel != null)
            {
                int ff = Mathf.RoundToInt(TboxRandPreviewFilterFont * s);
                if (ff < GalleryUiDesignTokens.FontTitleRef) ff = GalleryUiDesignTokens.FontTitleRef;
                if (_tboxRandPreviewFilterLabel.fontSize != ff) _tboxRandPreviewFilterLabel.fontSize = ff;
            }
            TboxLayoutPreviewLabel(_tboxRandPreviewLabel, innerW, labelH, new Vector2(0f, pad * 0.5f + subH),
                GalleryUiDesignTokens.FontTitleRef, s);
            TboxLayoutPreviewLabel(_tboxRandPreviewSub, innerW, subH, new Vector2(0f, pad * 0.5f),
                GalleryUiDesignTokens.FontCaptionRef, s);
            TboxApplyPreviewCornerRadius(s);
        }

        private static void TboxLayoutPreviewLabel(Text label, float w, float h, Vector2 pos, int fontRef, float s)
        {
            if (label == null) return;
            RectTransform rt = label.rectTransform;
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = pos;
            }
            int font = Mathf.RoundToInt(fontRef * s);
            if (font < GalleryUiDesignTokens.FontMinRef) font = GalleryUiDesignTokens.FontMinRef;
            if (label.fontSize != font) label.fontSize = font;
            Vector3 ls = label.transform.localScale;
            if (ls.x != 1f || ls.y != 1f)
                label.transform.localScale = Vector3.one;
        }

        private void TboxApplyPreviewCornerRadius(float s)
        {
            RoundedRect rr = _tboxRandPreviewCardRounded;
            if (rr == null) return;
            rr.excludeFromGlobalRadiusSync = true;
            rr.cornerRadiusFraction = 0f;
            float frac = UI.ResolveGalleryElementCornerRadiusFraction();
            float px = (frac <= 0f || s <= 0f) ? 0f : TboxRandPreviewCornerPx * s;
            rr.cornerRadius = px;
        }

        private void TboxHideRandomPreview(bool clearPick)
        {
            if (clearPick) _tboxRandPreviewPick = null;
            if (_tboxRandPreviewRoot == null || !_tboxRandPreviewRoot.activeSelf) return;
            if (_tboxRandPreviewImg != null) ClearThumbnailTarget(_tboxRandPreviewImg);
            _tboxRandPreviewRoot.SetActive(false);
        }

        private void TboxDestroyRandomPreview()
        {
            if (tboxLoadRandomBtn != null)
            {
                if (_tboxRandPreviewHoverHandler != null)
                {
                    UIHoverDelegate del = tboxLoadRandomBtn.GetComponent<UIHoverDelegate>();
                    if (del != null) del.OnHoverChange -= _tboxRandPreviewHoverHandler;
                }
                TboxRandomPreviewScroll wheel = tboxLoadRandomBtn.GetComponent<TboxRandomPreviewScroll>();
                if (wheel != null) wheel.panel = null;
            }
            _tboxRandPreviewHoverHandler = null;
            _tboxRandPreviewHover = false;
            _tboxRandPreviewPick = null;
            _tboxRandPreviewLastPick = null;
            if (_tboxRandPreviewScratch != null) _tboxRandPreviewScratch.Clear();
            if (_tboxRandPreviewImg != null) ClearThumbnailTarget(_tboxRandPreviewImg);
            _tboxRandPreviewRoot = null;
            _tboxRandPreviewRootRt = null;
            _tboxRandPreviewCardRt = null;
            _tboxRandPreviewThumbRt = null;
            _tboxRandPreviewCardRounded = null;
            _tboxRandPreviewImg = null;
            _tboxRandPreviewLabel = null;
            _tboxRandPreviewSub = null;
            _tboxRandPreviewFilterLabel = null;
            _tboxRandPreviewFilterStripRt = null;
        }

        private static void TboxSetPreviewLabel(Text label, string s)
        {
            if (label == null) return;
            string next = s ?? "";
            if (!string.Equals(label.text, next, StringComparison.Ordinal)) label.text = next;
        }

        private sealed class TboxRandomPreviewScroll : MonoBehaviour, IScrollHandler
        {
            public GalleryPanel panel;
            private float _notchAccum;

            public void OnScroll(PointerEventData eventData)
            {
                if (panel == null || eventData == null) return;
                int notches = VpbScrollTuning.TakeNotches(ref _notchAccum, eventData.scrollDelta.y);
                if (notches == 0) return;
                try { panel.TboxRerollRandomPreviewFromScroll(); } catch { }
            }
        }
    }
}
