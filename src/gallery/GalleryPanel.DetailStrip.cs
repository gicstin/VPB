using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VPB
{
    /// <summary>
    /// Selection-driven detail strip: identity, badges, clickable actions, quick tags, path.
    /// </summary>
    public partial class GalleryPanel
    {
        private const int DetailStripMaxTagsShown = 8;
        /// <summary>Hard cap for inline chips even if width allows more (keeps strip calm).</summary>
        private const int DetailStripMaxTagsInlineHard = 12;
        private const float DetailStripTagFilterPopupWidthRef = 320f;
        private const float DetailStripTagFilterPopupMaxHRef = 320f;
        private const float DetailStripTagFilterPopupPadRef = 10f;
        /// <summary>Meta wrap rows. Was 3 — at high ChromeScale wider glyphs dropped Version/Gender/Flags/dates.</summary>
        private const int DetailStripMetaMaxRows = 6;
        /// <summary>φ ≈ 1.618 — width / height.</summary>
        private const float DetailStripTagMenuGolden = 1.618034f;
        private const float DetailStripTagMenuWidthRef = 610f;
        private const float DetailStripTagMenuHeightRef = DetailStripTagMenuWidthRef / DetailStripTagMenuGolden; // ≈377
        private const float DetailStripTagMenuMinWidthRef = 420f;
        private const float DetailStripTagMenuMinHeightRef = 260f;
        private const float DetailStripTagMenuMaxWidthRef = 1400f;
        private const float DetailStripTagMenuMaxHeightRef = 1000f;
        private const float DetailStripTagMenuScrollHeightRef = 248f;
        private const float DetailStripTagMenuColGapRef = 8f;
        private const float DetailStripTagMenuSectionLabelHRef = 22f;
        private const float DetailStripTagMenuHeaderHRef = 36f;
        /// <summary>Sort / title X square — match gallery <see cref="GalleryUiDesignTokens.ButtonSizeRef"/>.</summary>
        private const float DetailStripTagMenuChromeBtnRef = 32f;
        private const int DetailStripTagMenuMaxRows = 48;
        private const int DetailStripTagMenuRecentMax = 8;
        private const float DetailStripTagMenuFilterDebounceSec = 0.12f;
        /// <summary>Opaque work-surface fill — not shared translucent popup glass.</summary>
        // Match Settings float chrome (neutral greys — not blue title/chips).
        private static readonly Color DetailStripTagMenuPanelBg = GalleryUiColorTokens.SurfaceDeep;
        private static readonly Color DetailStripTagMenuColBg = GalleryUiColorTokens.ModalSurface;
        private static readonly Color DetailStripTagMenuTitleBarBg = GalleryUiColorTokens.SurfaceDark;
        private static readonly Color DetailStripTagMenuTitleBarBgMulti = GalleryUiColorTokens.SurfaceMid;

        // Semantic value colors (status / role — not one generic blue).
        private static readonly Color DetailStripColorAuthor = new Color(0.95f, 0.78f, 0.42f, 1f);      // amber identity
        private static readonly Color DetailStripColorCategory = new Color(0.45f, 0.82f, 0.78f, 1f);    // teal taxonomy
        private static readonly Color DetailStripColorFact = new Color(0.72f, 0.78f, 0.88f, 1f);        // cool fact
        private static readonly Color DetailStripColorDeps = new Color(0.55f, 0.78f, 1f, 1f);           // info blue
        private static readonly Color DetailStripColorMissingOk = new Color(0.45f, 0.78f, 0.52f, 1f);   // green
        private static readonly Color DetailStripColorMissingBad = new Color(0.95f, 0.42f, 0.38f, 1f);  // red
        private static readonly Color DetailStripColorDependents = new Color(0.72f, 0.62f, 0.95f, 1f);  // violet
        private static readonly Color DetailStripColorFlags = new Color(0.92f, 0.62f, 0.38f, 1f);       // orange
        private static readonly Color DetailStripColorTag = new Color(0.70f, 0.72f, 0.98f, 1f);         // lavender
        // Action-link weights (hierarchy): Load = launch primary; manage peers quiet; Delete danger on hover.
        // Meta/status colors above stay semantic — do not rainbow the action row.
        private static readonly Color DetailStripActionPrimary = new Color(0.42f, 0.90f, 0.48f, 1f);   // Load (launch)
        private static readonly Color DetailStripActionSecondary = new Color(0.68f, 0.72f, 0.78f, 1f); // quiet peers
        private static readonly Color DetailStripActionDanger = new Color(0.95f, 0.45f, 0.45f, 1f);     // Delete hover
        private static readonly Color DetailStripColorVersionLatest = new Color(0.50f, 0.85f, 0.58f, 1f);
        private static readonly Color DetailStripColorVersionOlder = new Color(0.95f, 0.70f, 0.40f, 1f);
        private static readonly Color DetailStripColorDesc = new Color(0.70f, 0.72f, 0.76f, 0.95f);

        private struct DetailStripMetaField
        {
            public string Label;      // "Author" (plain; colon added in UI)
            public string Value;      // "CreatorX" (colored; clickable when Enabled)
            public int Group;         // 0 flowable facts; 1 deps cluster (kept together)
            public bool Enabled;
            public Color ValueColor;
            public UnityAction OnClick;
            public string Tip;
            public float MaxValueWidth; // >0 soft-cap / ellipsis (long author)
        }

        private GameObject _detailStripGO;
        private RectTransform _detailStripRT;
        private Image _detailStripBg;
        private RawImage _detailStripThumb;
        private GameObject _detailStripThumbGO;
        private GameObject _detailStripBadgeRowGO;
        private GameObject _detailStripBadgeAuto;
        private GameObject _detailStripBadgeHide;
        private GameObject _detailStripBadgeScan;
        private GameObject _detailStripBadgeTags;
        private GameObject _detailStripTitleRowGO;
        private Text _detailStripTitle;
        /// <summary>Collapse control left of title (expanded strip only).</summary>
        private GameObject _detailStripCollapseBtnGO;
        private Image _detailStripCollapseIconImage;
        /// <summary>Expand control in toolbox label row: icon + label (collapsed strip + selection).</summary>
        private GameObject _detailStripExpandBtnGO;
        private Image _detailStripExpandIconImage;
        private Text _detailStripExpandLabel;
        private LayoutElement _detailStripExpandBtnLE;
        private Sprite _detailStripCollapseSprite;
        private Sprite _detailStripExpandSprite;
        private GameObject _detailStripStarsGO;
        private Image[] _detailStripStarImages;
        private int _detailStripStarRating;
        private int _detailStripStarHover;
        /// <summary>Overlay prev on thumb (absolute; does not shrink preview).</summary>
        private GameObject _detailStripThumbPrevBtnGO;
        private Button _detailStripThumbPrevBtn;
        private Image _detailStripThumbPrevBtnImage;
        /// <summary>Overlay next on thumb (absolute; does not shrink preview).</summary>
        private GameObject _detailStripThumbNextBtnGO;
        private Button _detailStripThumbNextBtn;
        private Image _detailStripThumbNextBtnImage;
        /// <summary>Transient n/N chip on thumb during scrub.</summary>
        private GameObject _detailStripThumbScrubIndexGO;
        private Text _detailStripThumbScrubIndexText;
        private int _detailStripThumbScrubIndexShown = int.MinValue;
        private int _detailStripThumbScrubCountShown = int.MinValue;
        private bool _detailStripThumbScrubIndexVisible;
        private Sprite _detailStripThumbNavPrevSprite;
        private Sprite _detailStripThumbNavNextSprite;
        // Quiet overlay chrome — preview stays primary; ◀▶ secondary (hierarchy / de-emphasize).
        private static readonly Color DetailStripThumbNavBackdrop = new Color(0.04f, 0.04f, 0.04f, 0.40f);
        private static readonly Color DetailStripThumbNavGlyph = new Color(
            GalleryUiColorTokens.TextMuted.r, GalleryUiColorTokens.TextMuted.g, GalleryUiColorTokens.TextMuted.b, 0.70f);
        /// <summary>Inactive ◀▶ CanvasGroup — keep edge recognizable, not competing with live peer.</summary>
        private const float DetailStripThumbNavDisabledAlpha = 0.20f;
        private static readonly Color DetailStripThumbScrubIndexBg = new Color(0.04f, 0.04f, 0.04f, 0.72f);
        private GameObject _detailStripMetaHost;
        private LayoutElement _detailStripMetaHostLE;
        private GameObject[] _detailStripMetaRows;
        private GameObject _detailStripActionsRowGO; // host (VLG) for wrapped action rows
        private GameObject[] _detailStripActionRows;
        private const int DetailStripActionMaxRows = 3;
        private GameObject _detailStripThumbColGO;
        private GameObject _detailStripResizeGripGO;
        private Image _detailStripResizeGripBg;
        private Image _detailStripResizeGripPill;
        private bool _detailStripResizing;
        private float _detailStripResizeStartH;
        private Vector2 _detailStripResizeStartLocal;
        private Text _detailStripLoadLink;
        private Text _detailStripCopyLink;
        private Text _detailStripDeleteLink;
        private GameObject _detailStripToolsRowGO;
        private Text _detailStripHubLink;
        private Text _detailStripCacheLink;
        private Text _detailStripAutoLoadLink;
        private Text _detailStripNoAutoLoadLink;
        private GameObject _detailStripAutoLoadSepGO;
        private Text _detailStripHideLink;
        private Text _detailStripUnhideLink;
        private GameObject _detailStripHideUnhideSepGO;
        private GameObject _detailStripAfterHideSepGO;
        private Text _detailStripTempWlLink;
        private Text _detailStripOldVersLink;
        private GameObject _detailStripBeforeOldVersSepGO;
        private Text _detailStripDesc;
        private Text _detailStripPackageTags;
        private static readonly Color DetailStripLinkColor = DetailStripColorDeps;
        private static readonly Color DetailStripLinkDisabledColor = new Color(0.45f, 0.45f, 0.48f, 0.85f);
        private static readonly Color DetailStripMetaMutedColor = new Color(0.62f, 0.62f, 0.66f, 0.95f);
        // Filled gold on / muted outline off — Jakob rating pattern; solid alpha so fill reads (not washed outline).
        private static readonly Color DetailStripStarOnColor = new Color(0.92f, 0.78f, 0.38f, 0.95f);
        private static readonly Color DetailStripStarOffColor = new Color(0.55f, 0.55f, 0.58f, 0.45f);
        private static readonly Color DetailStripStarPreviewColor = new Color(0.98f, 0.86f, 0.48f, 1f);
        private Text _detailStripTags; // "Set Tags: " action label (opens quick-tag editor)
        private GameObject _detailStripTagsChipsHost;
        private GameObject _detailStripTagClipboardActionsGO;
        private Text _detailStripCopyTagsLink;
        private Text _detailStripPasteTagsLink;
        private Text _detailStripReplaceTagsLink;
        private string _detailStripTagsContentKey = "";
        private List<string> _detailStripBoundTagNames;
        /// <summary>Session display order for quick-tagger Applied column (drag-reorder).</summary>
        private List<string> _detailStripTagMenuAppliedOrder;
        private GameObject _detailStripTagFilterMenuGO;
        private RectTransform _detailStripTagFilterPanelRT;
        private Text _detailStripPath;
        /// <summary>Wide-pane right column: scrollable description + native package tags.</summary>
        private GameObject _detailStripSideColGO;
        private LayoutElement _detailStripSideColLE;
        private GameObject _detailStripSideDescScrollGO;
        private LayoutElement _detailStripSideDescScrollLE;
        private ScrollRect _detailStripSideDescScrollRect;
        private RectTransform _detailStripSideDescContentRT;
        private Text _detailStripSideDesc;
        private LayoutElement _detailStripSideDescLE;
        private Text _detailStripSideNativeTags;
        private string _detailStripCacheKey = "";
        private FileEntry _detailStripThumbFile;
        private FileEntry _detailStripBoundFile;
        private string _detailStripBoundCreator = "";
        private GameObject _detailStripNewTagModalGO;
        private InputField _detailStripNewTagInput;
        private GameObject _detailStripTagMenuRoot;
        private GameObject _detailStripTagMenuPanelGO;
        private RectTransform _detailStripTagMenuPanelRT;
        private GameObject _detailStripTagMenuHeaderGO;
        private Text _detailStripTagMenuSelText;
        private bool _detailStripTagMenuDragged;
        private string _detailStripTagMenuSelectionKey = "";
        /// <summary>Top filter row (Settings float pattern — search above body).</summary>
        private GameObject _detailStripTagMenuFilterRowGO;
        private GameObject _detailStripTagMenuFilterClearGo;
        /// <summary>Footer band (Close + resize). Search lives in FilterRow.</summary>
        private GameObject _detailStripTagMenuSearchRowGO;
        private GameObject _detailStripTagMenuCloseGO;
        private GameObject _detailStripTagMenuCollapseBtn;
        private Image _detailStripTagMenuCollapseIcon;
        private bool _detailStripTagMenuCollapsed;
        private Vector2? _detailStripTagMenuCollapsedTopLeftPos;
        private float? _detailStripTagMenuExpandHeightRef;
        /// <summary>Footer text Close; icon X lives in titlebar.</summary>
        private GameObject _detailStripTagMenuFooterCloseGO;
        private GameObject _detailStripTagMenuColumnsGO;
        private GameObject _detailStripTagMenuAppliedScrollGO;
        private GameObject _detailStripTagMenuAppliedListGO;
        private Text _detailStripTagMenuAppliedLabel;
        private GameObject _detailStripTagMenuAvailableScrollGO;
        private GameObject _detailStripTagMenuAvailableListGO;
        private Text _detailStripTagMenuAvailableLabel;
        private GameObject _detailStripTagMenuAvailSortBtnGO;
        private Image _detailStripTagMenuAvailSortIcon;
        private Text _detailStripTagMenuTipText;
        private const string DetailStripTagMenuAvailSortContext = "DetailStripTagMenu";
        private InputField _detailStripTagMenuSearch;
        private GameObject _detailStripTagMenuCreateGO;
        private Text _detailStripTagMenuCreateText;
        private string _detailStripTagMenuFilter = "";
        private List<string> _detailStripTagMenuVocabCache;
        private HashSet<string> _detailStripTagMenuAppliedCache;
        /// <summary>Session MRU for Add column (pinned tags still win order).</summary>
        private readonly List<string> _detailStripTagMenuRecent = new List<string>(DetailStripTagMenuRecentMax);
        private Vector2? _detailStripTagMenuSavedPos; // panel center (top-left pivot at runtime)
        private Vector2? _detailStripTagMenuSavedSize;
        private Coroutine _detailStripTagMenuPosSaveCo;
        private GameObject _detailStripTagMenuResizeGO;
        private int _detailStripTagMenuLastAppliedCount;
        private int _detailStripTagMenuLastAvailCount;
        private bool _detailStripTagMenuRemoveHintActive;
        private readonly List<DetailStripTagMenuNavRow> _detailStripTagMenuNav = new List<DetailStripTagMenuNavRow>(64);
        private int _detailStripTagMenuFocusIdx = -1;
        private Coroutine _detailStripTagMenuFilterCo;
        private float _detailStripLayoutScale = -1f;
        private float _detailStripMetaAvailWidth = -1f;
        private float _detailStripMeasuredHeight = -1f;
        private bool _detailStripInRefreshGeometry;
        private bool _detailStripWantPath;
        private bool _detailStripWantDesc;
        private bool _detailStripWantTags;
        private bool _detailStripWantNativeTags;
        private bool _detailStripSideVisible;
        /// <summary>
        /// Sticky tall-strip stack mode (desc/package tags as main rows). Paired with height
        /// hysteresis so side↔stack cannot 1 Hz hunt when auto-measure straddles the threshold.
        /// </summary>
        private bool _detailStripStackSideAsRows;
        private bool _detailStripStackSideDecided;
        /// <summary>
        /// Auto-fit height locked to selection identity. Rating/tag paint must not remasure —
        /// remasure only on selection change, scale, user drag, or large width class change.
        /// </summary>
        private float _detailStripAutoHeightLock = -1f;
        private string _detailStripAutoHeightLockKey = "";
        /// <summary>Identity key for last <see cref="DetailStripRefreshSideContent"/> fill (scrub/sameKey skip otherwise).</summary>
        private string _detailStripSideContentKey = "";
        // Thumb-wheel selection scrub: coalesce steps, lite UI while spinning, soft commit on idle.
        private bool _detailStripScrubActive;
        private int _detailStripScrubPendingSteps;
        private int _detailStripScrubIndex = -1;
        private float _detailStripScrubLastInputTime;
        private bool _detailStripScrubHeightLocked;
        private float _detailStripScrubLockedHeight = -1f;
        private const float DetailStripScrubCommitDelaySec = 0.22f;

        /// <summary>True while thumb-scrub session should refuse strip rebuild/hide/populate.</summary>
        private bool DetailStripScrubBlocksRebuild =>
            _detailStripScrubActive || _detailStripScrubHeightLocked;

        /// <summary>Hard floor: min thumb edge. Design 96×s is comfort target via measure, not empty band.</summary>
        private static float DetailStripHardMinHeight(float s)
        {
            if (s <= 0f) s = 1f;
            return 44f * s;
        }

        /// <summary>
        /// User/content max — design ref × scale. Keep ≤ thumb max so preview stays flush
        /// (old lineH×rows formula could exceed ThumbMax and leave a gap under the image).
        /// </summary>
        private static float DetailStripMaxHeight(float s)
        {
            if (s <= 0f) s = 1f;
            return GalleryUiDesignTokens.FooterDetailStripHeightRef * s;
        }

        /// <summary>
        /// True when auto-fit height is locked for this open strip session.
        /// Sticky across selection flips so thumb/nav do not jump per item.
        /// </summary>
        private bool DetailStripHasAutoHeightLock()
        {
            return _detailStripAutoHeightLock > 8f;
        }

        private static bool DetailStripHasUserHeight()
        {
            return VPBConfig.Instance != null && VPBConfig.Instance.GalleryDetailStripHeightRef > 0.5f;
        }

        private static bool DetailStripThumbOnRight()
        {
            return VPBConfig.Instance != null && VPBConfig.Instance.GalleryDetailStripThumbOnRight;
        }

        /// <summary>
        /// User-drag floor: title + 1 action + tags + path (+ pad/gaps). No meta —
        /// HideOverflow drops facts first so path is not clipped at min height.
        /// Token MinHeightRef is soft target; compact path-fit wins when larger.
        /// </summary>
        private static float DetailStripUserMinHeight(float s)
        {
            if (s <= 0f) s = 1f;
            float lineH = DetailStripLineHeight(s);
            float hitH = DetailStripHitHeight(s);
            float gap = DetailStripBandGap(s);
            float vPad = 8f * s; // match TextCol top+bottom pad
            // title + action0 + tags + path (protected bands)
            float compact = vPad + lineH + gap + hitH + gap + lineH + gap + lineH;
            return Mathf.Max(DetailStripHardMinHeight(s), compact);
        }

        private float DetailStripUserHeightScaled(float s)
        {
            if (s <= 0f) s = 1f;
            float minH = DetailStripUserMinHeight(s);
            float maxH = DetailStripMaxHeight(s);
            float refH = VPBConfig.Instance != null
                ? VPBConfig.Instance.GalleryDetailStripHeightRef
                : GalleryUiDesignTokens.FooterDetailStripMinHeightRef;
            return Mathf.Clamp(refH * s, minH, maxH);
        }

        private float DetailStripRowHeight(float s)
        {
            if (s <= 0f) s = 1f;
            float hardMin = DetailStripHardMinHeight(s);
            float maxH = DetailStripMaxHeight(s);
            if (_detailStripScrubHeightLocked && _detailStripScrubLockedHeight > 8f)
                return Mathf.Clamp(_detailStripScrubLockedHeight, hardMin, maxH);
            if (DetailStripHasUserHeight())
                return DetailStripUserHeightScaled(s);
            if (_detailStripMeasuredHeight > 8f)
                return Mathf.Clamp(_detailStripMeasuredHeight, hardMin, maxH);
            return Mathf.Clamp(GalleryUiDesignTokens.FooterDetailStripMinHeightRef * s, hardMin, maxH);
        }

        private static float DetailStripLineHeight(float s)
        {
            if (s <= 0f) s = 1f;
            // Match global chrome font metrics (ScaledFontSize + FontMin); pad for Arial line box.
            int fontPx = GalleryUiMetrics.ScaledFontSize(
                GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            float designLine = GalleryUiDesignTokens.FooterDetailStripLineHeightRef * s;
            return Mathf.Max(designLine, fontPx + 4f * s);
        }

        /// <summary>
        /// Interactive band height for action links + meta rows (≥ line, ≥ hit token).
        /// Title / desc / path stay on <see cref="DetailStripLineHeight"/>.
        /// </summary>
        private static float DetailStripHitHeight(float s)
        {
            if (s <= 0f) s = 1f;
            float lineH = DetailStripLineHeight(s);
            float hit = GalleryUiDesignTokens.FooterDetailStripHitHeightRef * s;
            return Mathf.Max(lineH, hit);
        }

        /// <summary>Equal condensed gap between strip bands (title / meta / actions / flex lines).</summary>
        private static float DetailStripBandGap(float s)
        {
            if (s <= 0f) s = 1f;
            return GalleryUiDesignTokens.FooterDetailStripBandGapRef * s;
        }

        private static void DetailStripDisableVerticalCsf(Component c)
        {
            if (c == null) return;
            ContentSizeFitter csf = c.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        /// <summary>Same font path as rest of gallery chrome (<see cref="GalleryUiMetrics.ApplyFont"/>).</summary>
        private static void DetailStripApplyFont(Text txt, float s, int designPt = GalleryUiDesignTokens.FontBodyRef)
        {
            if (txt == null) return;
            if (s <= 0f) s = 1f;
            if (designPt <= 0) designPt = GalleryUiDesignTokens.FontBodyRef;
            GalleryUiMetrics.ApplyFont(txt, designPt, s, GalleryUiDesignTokens.FontMinRef);
        }

        /// <summary>Count active meta rows (ignore host active — host may be off from a prior empty sync).</summary>
        private int DetailStripActiveMetaRowCount()
        {
            int metaN = 0;
            if (_detailStripMetaRows == null) return 0;
            for (int i = 0; i < _detailStripMetaRows.Length; i++)
            {
                if (_detailStripMetaRows[i] != null && _detailStripMetaRows[i].activeSelf)
                    metaN++;
            }
            return metaN;
        }

        /// <summary>Active meta rows × hit height — never trust a stale LayoutElement height.</summary>
        private float DetailStripMetaHostHeight(float s)
        {
            float hitH = DetailStripHitHeight(s);
            int metaN = DetailStripActiveMetaRowCount();
            if (metaN <= 0) return 0f;
            float gap = DetailStripBandGap(s);
            return hitH * metaN + gap * Mathf.Max(0, metaN - 1);
        }

        private void DetailStripSyncMetaHostHeight(float s)
        {
            if (_detailStripMetaHost == null) return;
            if (s <= 0f) s = 1f;
            if (_detailStripMetaHostLE == null)
                _detailStripMetaHostLE = _detailStripMetaHost.GetComponent<LayoutElement>();
            DetailStripEnsureHostHeightDrivers(_detailStripMetaHost);

            float h = DetailStripMetaHostHeight(s);
            // Keep host active always — SetActive(false) + height check that required host
            // active was a deadlock (meta never came back after scale/empty sync).
            if (!_detailStripMetaHost.activeSelf)
                _detailStripMetaHost.SetActive(true);
            if (_detailStripMetaHostLE == null) return;
            if (h < 0.5f)
            {
                _detailStripMetaHostLE.minHeight = 0f;
                _detailStripMetaHostLE.preferredHeight = 0f;
                _detailStripMetaHostLE.flexibleHeight = 0f;
                return;
            }
            // min == preferred — TextCol VLG must reserve full meta stack (never collapse under actions).
            _detailStripMetaHostLE.minHeight = h;
            _detailStripMetaHostLE.preferredHeight = h;
            _detailStripMetaHostLE.flexibleHeight = 0f;
            _detailStripMetaHostLE.ignoreLayout = false;
        }

        /// <summary>
        /// LayoutElement only (no ContentSizeFitter) — CSF on VLG children fights parent layout
        /// and can leave MetaHost / Actions sharing one rect.
        /// </summary>
        private static void DetailStripEnsureHostHeightDrivers(GameObject host)
        {
            if (host == null) return;
            // Strip leftover CSF from prior overlap fix attempt.
            ContentSizeFitter csf = host.GetComponent<ContentSizeFitter>();
            if (csf != null)
            {
                try { UnityEngine.Object.Destroy(csf); } catch { }
            }
            LayoutElement le = host.GetComponent<LayoutElement>();
            if (le == null) le = host.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
            le.ignoreLayout = false;
        }

        private void DetailStripRebuildTextColLayout()
        {
            if (_detailStripGO == null) return;
            Transform textCol = _detailStripGO.transform.Find("TextCol");
            RectTransform textRT = textCol as RectTransform;
            if (textRT == null) return;
            try { LayoutRebuilder.ForceRebuildLayoutImmediate(textRT); } catch { }
        }

        private static void DetailStripSetRowHeight(GameObject go, float lineH)
        {
            if (go == null) return;
            LayoutElement le = go.GetComponent<LayoutElement>();
            if (le == null) return;
            le.preferredHeight = lineH;
            le.minHeight = lineH;
        }

        private static void DetailStripSetLayoutGroup(GameObject go, float spacing, RectOffset padding)
        {
            if (go == null) return;
            HorizontalLayoutGroup hlg = go.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.spacing = spacing;
                if (padding != null) hlg.padding = padding;
                return;
            }
            VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.spacing = spacing;
                if (padding != null) vlg.padding = padding;
            }
        }

        private bool DetailStripIsExpanded()
        {
            return VPBConfig.Instance == null || VPBConfig.Instance.GalleryDetailStripExpanded;
        }

        private float DetailStripReservedHeight()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return 0f;
            if (!DetailStripIsExpanded()) return 0f;
            if (_detailStripGO == null) return 0f;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            return DetailStripRowHeight(s) + TboxBtnRowGapScaled();
        }

        private void DetailStripEnsure()
        {
            // Rebuild when missing meta-flow, stars, or still on split Tools row / old D-M-Dn layout.
            // NOTE: action links live under ActionRowN, not Actions host — never Transform.Find("Link_*")
            // on _detailStripActionsRowGO (Find is direct-child only; was always-null → destroy loop).
            bool needsRebuild = _detailStripGO != null && (
                _detailStripActionsRowGO == null
                || _detailStripMetaHost == null
                || _detailStripMetaRows == null
                || _detailStripTitleRowGO == null
                || _detailStripStarsGO == null
                || _detailStripStarImages == null
                || _detailStripCollapseBtnGO == null
                || (_detailStripCollapseBtnGO != null && _detailStripTitle != null
                    && _detailStripCollapseBtnGO.transform.GetSiblingIndex()
                        > _detailStripTitle.transform.GetSiblingIndex())
                || _detailStripUnhideLink == null
                || _detailStripDeleteLink == null
                || _detailStripLoadLink == null
                || _detailStripAutoLoadLink == null
                || _detailStripNoAutoLoadLink == null
                || _detailStripOldVersLink == null
                || _detailStripDesc == null
                || _detailStripPackageTags == null
                || _detailStripSideColGO == null
                || _detailStripSideDescScrollGO == null
                || _detailStripSideDesc == null
                || _detailStripSideNativeTags == null
                || (_detailStripSideDescScrollGO != null
                    && _detailStripSideDescScrollGO.GetComponent<UIScrollWheelHandler>() == null)
                || DetailStripTextColMissingMask(_detailStripGO)
                || _detailStripActionRows == null
                || _detailStripBadgeRowGO == null
                || (_detailStripBadgeRowGO != null && _detailStripTitleRowGO != null
                    && _detailStripBadgeRowGO.transform.parent != _detailStripTitleRowGO.transform)
                || (_detailStripPath != null && (_detailStripPath.transform.parent == null
                    || _detailStripPath.transform.parent.name != "PathRow"))
                || _detailStripToolsRowGO != null
                || _detailStripHubLink == null
                || _detailStripThumbColGO == null
                || _detailStripResizeGripGO == null
                || DetailStripResizeGripNeedsRebuild(_detailStripResizeGripGO)
                || DetailStripStripForcesChildHeight(_detailStripGO)
                || DetailStripStripNeedsUpperLeftAlign(_detailStripGO)
                || DetailStripStripHasLegacyOuterPad(_detailStripGO)
                || (_detailStripActionRows != null && _detailStripActionRows.Length < DetailStripActionMaxRows)
                || (_detailStripMetaRows != null && _detailStripMetaRows.Length < DetailStripMetaMaxRows)
                || (_detailStripThumbColGO != null && _detailStripThumbColGO.GetComponent<UIScrollWheelHandler>() == null)
                || (_detailStripThumbColGO != null && _detailStripThumbPrevBtnGO == null)
                || DetailStripActionsHostHasLegacyDirectChild("Link_Tag")
                || DetailStripActionsHostHasLegacyDirectChild("Chip_Deps")
                || DetailStripActionsHostHasLegacyDirectChild("Link_Deps")
                // Hierarchy pass: rebuild strips still on per-action rainbow colors.
                || (_detailStripActionsRowGO != null
                    && _detailStripActionsRowGO.transform.Find("ActionWeightV2") == null)
                // Load-first launch CTA + hard sep before manage cluster.
                || (_detailStripActionsRowGO != null
                    && _detailStripActionsRowGO.transform.Find("ActionLoadV1") == null)
                // Hit-target pad: rebuild strips still on line-height action/meta rows.
                || (_detailStripActionsRowGO != null
                    && _detailStripActionsRowGO.transform.Find("HitPadV1") == null)
                // Band stack: rebuild strips that allowed MetaHost/Actions minHeight collapse.
                || (_detailStripActionsRowGO != null
                    && _detailStripActionsRowGO.transform.Find("BandStackV1") == null)
                // Drop More… overflow — rebuild strips that still hide tools behind it.
                || (_detailStripActionsRowGO != null
                    && (_detailStripActionsRowGO.transform.Find("ActionMoreV1") != null
                        || _detailStripActionsRowGO.transform.Find("OverflowHost") != null)));
            if (needsRebuild && !DetailStripScrubBlocksRebuild)
            {
                try { UnityEngine.Object.Destroy(_detailStripGO); } catch { }
                _detailStripGO = null;
                DetailStripClearUiRefs();
                _detailStripLayoutScale = -1f;
            }

            // Orphan More… popup from reverted IA — destroy if still on chrome.
            if (backgroundBoxGO != null)
            {
                Transform moreMenu = backgroundBoxGO.transform.Find("DetailStripMoreMenu");
                if (moreMenu != null)
                {
                    try { UnityEngine.Object.Destroy(moreMenu.gameObject); } catch { }
                }
            }

            // Expand button is fixed top-left chrome on buttons layer (not flex-packed).
            if (_detailStripExpandBtnGO != null
                && (_detailStripExpandLabel == null
                    || _detailStripExpandBtnGO.GetComponent<UIScrollWheelHandler>() == null
                    || tboxButtonsLayerRT == null
                    || _detailStripExpandBtnGO.transform.parent != tboxButtonsLayerRT))
            {
                try
                {
                    if (tboxBaseWidthSpec != null && _detailStripExpandBtnGO != null)
                        tboxBaseWidthSpec.Remove(_detailStripExpandBtnGO);
                }
                catch { }
                try { UnityEngine.Object.Destroy(_detailStripExpandBtnGO); } catch { }
                _detailStripExpandBtnGO = null;
                _detailStripExpandIconImage = null;
                _detailStripExpandLabel = null;
                _detailStripExpandBtnLE = null;
            }
            DetailStripEnsureExpandButton();

            // Strip-level RectMask2D clips ResizeGrip (pivot hangs above strip top). Remove if present.
            // Desc overflow stays clipped via TextCol mask + budget ellipsis / HideOverflow.
            if (_detailStripGO != null)
            {
                RectMask2D stripMask = _detailStripGO.GetComponent<RectMask2D>();
                if (stripMask != null)
                {
                    try { UnityEngine.Object.Destroy(stripMask); } catch { }
                }
            }

            // Tag menu: rebuild if missing two-column / drag-header / search-row, legacy ARF close, or still parented under pane.
            bool tagMenuCloseHasArf = _detailStripTagMenuCloseGO != null
                && _detailStripTagMenuCloseGO.GetComponent<AspectRatioFitter>() != null;
            bool tagMenuWrongParent = _detailStripTagMenuRoot != null && canvas != null
                && _detailStripTagMenuRoot.transform.parent != canvas.transform;
            bool tagMenuNeedsOpaque = false;
            if (_detailStripTagMenuPanelGO != null)
            {
                Image panelImgProbe = _detailStripTagMenuPanelGO.GetComponent<Image>();
                if (panelImgProbe != null && panelImgProbe.color.a < 0.999f)
                    tagMenuNeedsOpaque = true;
            }
            bool tagMenuMissingKeys = _detailStripTagMenuSearch != null
                && _detailStripTagMenuSearch.GetComponent<DetailStripTagMenuSearchKeys>() == null;
            // Settings-master chrome: left title + collapse + top FilterRow (no legacy centered title / footer search).
            bool tagMenuLegacyChrome = false;
            if (_detailStripTagMenuHeaderGO != null)
            {
                if (_detailStripTagMenuHeaderGO.transform.Find("CollapseBtn") == null)
                    tagMenuLegacyChrome = true;
                if (_detailStripTagMenuHeaderGO.transform.Find("TitleCentered") != null)
                    tagMenuLegacyChrome = true;
            }
            if (_detailStripTagMenuPanelGO != null
                && _detailStripTagMenuPanelGO.transform.Find("FilterRow") == null)
                tagMenuLegacyChrome = true;
            bool tagMenuMissingTitleBar = _detailStripTagMenuHeaderGO != null
                && _detailStripTagMenuHeaderGO.transform.Find("Title") == null
                && _detailStripTagMenuHeaderGO.transform.Find("TitleCentered") == null;
            bool tagMenuMissingTip = _detailStripTagMenuPanelGO != null
                && _detailStripTagMenuPanelGO.transform.Find("Tip") == null;
            bool tagMenuCloseNotInTitle = _detailStripTagMenuCloseGO != null
                && _detailStripTagMenuHeaderGO != null
                && !_detailStripTagMenuCloseGO.transform.IsChildOf(_detailStripTagMenuHeaderGO.transform);
            bool tagMenuMissingAvailRemove = _detailStripTagMenuAvailableScrollGO != null
                && _detailStripTagMenuAvailableScrollGO.GetComponent<UserTagRemoveDropZone>() == null;
            bool tagMenuMissingAppliedApply = _detailStripTagMenuAppliedScrollGO != null
                && _detailStripTagMenuAppliedScrollGO.GetComponent<UserTagApplyDropZone>() == null;
            // Soft-attach column drop zones without hard rebuild.
            if (tagMenuMissingAvailRemove || tagMenuMissingAppliedApply)
            {
                try { DetailStripEnsureTagMenuColumnDropZones(); } catch { }
                tagMenuMissingAvailRemove = _detailStripTagMenuAvailableScrollGO != null
                    && _detailStripTagMenuAvailableScrollGO.GetComponent<UserTagRemoveDropZone>() == null;
            }
            bool tagMenuMissingAvailSort = _detailStripTagMenuAvailableLabel != null
                && _detailStripTagMenuAvailSortBtnGO == null;
            bool tagMenuMissingResize = _detailStripTagMenuResizeGO == null
                || (_detailStripTagMenuSearchRowGO != null
                    && !_detailStripTagMenuResizeGO.transform.IsChildOf(_detailStripTagMenuSearchRowGO.transform));
            // Soft-attach footer drag chrome without hard rebuild.
            try { DetailStripEnsureTagMenuFooterDrag(); } catch { }
            // Prefer attach ModeTabs / DatabaseHost over destroying an open menu (flash-close).
            if (_detailStripTagMenuRoot != null && DetailStripTagMenuNeedsUnifiedRebuild())
            {
                try
                {
                    DetailStripEnsureTagMenuModeTabs();
                    DetailStripEnsureTagMenuDatabasePane();
                }
                catch { }
            }
            bool tagMenuMissingUnified = DetailStripTagMenuNeedsUnifiedRebuild();
            bool tagMenuNeedsHardRebuild = tagMenuCloseHasArf
                || tagMenuWrongParent
                || tagMenuNeedsOpaque
                || tagMenuMissingKeys
                || tagMenuLegacyChrome
                || tagMenuMissingTitleBar
                || tagMenuMissingTip
                || tagMenuCloseNotInTitle
                || tagMenuMissingAvailRemove
                || tagMenuMissingAvailSort
                || tagMenuMissingResize
                || tagMenuMissingUnified
                || _detailStripTagMenuHeaderGO == null
                || _detailStripTagMenuSelText == null
                || _detailStripTagMenuColumnsGO == null
                || _detailStripTagMenuAppliedScrollGO == null
                || _detailStripTagMenuAppliedListGO == null
                || _detailStripTagMenuAvailableScrollGO == null
                || _detailStripTagMenuAvailableListGO == null
                || _detailStripTagMenuFilterRowGO == null
                || _detailStripTagMenuSearchRowGO == null
                || _detailStripTagMenuCloseGO == null
                || _detailStripTagMenuCollapseBtn == null
                || _detailStripTagMenuFooterCloseGO == null
                || _detailStripTagMenuSearch == null
                || _detailStripTagMenuCreateGO == null;
            // Soft-repair unified chrome already attempted above. Never Destroy while open — flash-close.
            if (_detailStripTagMenuRoot != null && tagMenuNeedsHardRebuild
                && !_detailStripTagMenuRoot.activeSelf)
            {
                try { UnityEngine.Object.Destroy(_detailStripTagMenuRoot); } catch { }
                _detailStripTagMenuRoot = null;
                _detailStripTagMenuPanelGO = null;
                _detailStripTagMenuPanelRT = null;
                _detailStripTagMenuHeaderGO = null;
                _detailStripTagMenuSelText = null;
                _detailStripTagMenuDragged = false;
                _detailStripTagMenuSelectionKey = "";
                _detailStripTagMenuColumnsGO = null;
                _detailStripTagMenuAppliedScrollGO = null;
                _detailStripTagMenuAppliedListGO = null;
                _detailStripTagMenuAppliedLabel = null;
                _detailStripTagMenuAvailableScrollGO = null;
                _detailStripTagMenuAvailableListGO = null;
                _detailStripTagMenuAvailableLabel = null;
                _detailStripTagMenuAvailSortBtnGO = null;
                _detailStripTagMenuAvailSortIcon = null;
                _detailStripTagMenuFilterRowGO = null;
                _detailStripTagMenuFilterClearGo = null;
                _detailStripTagMenuSearchRowGO = null;
                _detailStripTagMenuCloseGO = null;
                _detailStripTagMenuCollapseBtn = null;
                _detailStripTagMenuCollapseIcon = null;
                _detailStripTagMenuCollapsed = false;
                _detailStripTagMenuCollapsedTopLeftPos = null;
                _detailStripTagMenuExpandHeightRef = null;
                _detailStripTagMenuFooterCloseGO = null;
                _detailStripTagMenuSearch = null;
                _detailStripTagMenuCreateGO = null;
                _detailStripTagMenuResizeGO = null;
                _detailStripTagMenuCreateText = null;
                _detailStripTagMenuTipText = null;
                _detailStripTagMenuNav.Clear();
                _detailStripTagMenuFocusIdx = -1;
                DetailStripStopTagMenuFilterRebuild();
                DetailStripInvalidateTagMenuCaches();
                DetailStripClearTagMenuDatabaseFieldRefs();
            }

            if (_detailStripGO != null)
            {
                try { DetailStripEnsureApplyDropZone(_detailStripGO); } catch { }
                try { DetailStripSyncThumbInteractions(); } catch { }
                try { DetailStripSyncThumbSide(); } catch { }
                try { DetailStripEnsureResizeGrip(); } catch { }
                return;
            }
            EnsureTboxUI();
            if (tbox == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float rowH = DetailStripRowHeight(s);

            // Full-bleed in InfoBar — thumb sits flush; text owns its own pad.
            GameObject strip = UI.CreateChildRT(
                tbox, "VPB_DetailStrip", AnchorPresets.hStretchTop, new Vector2(0f, rowH));
            _detailStripGO = strip;
            _detailStripRT = strip.GetComponent<RectTransform>();
            // Neutral surface — avoid cool/blue channel drift vs rest of chrome.
            Color stripBg = GalleryUiColorTokens.SurfaceDeep;
            stripBg.a = 0.98f;
            _detailStripBg = UI.AddImage(strip, stripBg, raycastTarget: true);

            // Drop zone: drag tags from quick-tagger / chips onto strip → apply to selection.
            DetailStripEnsureApplyDropZone(strip);

            // No HLG pad — preview uses full strip edge; gap to text via spacing only.
            UI.AddHLG(
                strip,
                spacing: 8f * s,
                padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.UpperLeft,
                childForceExpandWidth: false,
                childForceExpandHeight: false);

            float thumbSize = DetailStripThumbEdge(s, rowH);
            GameObject thumbCol = UI.CreateChildRT(strip, "ThumbCol", AnchorPresets.topLeft, new Vector2(thumbSize, thumbSize));
            _detailStripThumbColGO = thumbCol;
            UI.AddLE(thumbCol, minWidth: thumbSize, preferredWidth: thumbSize,
                minHeight: thumbSize, preferredHeight: thumbSize, flexibleWidth: 0f, flexibleHeight: 0f);

            _detailStripThumbGO = UI.CreateChildRT(thumbCol, "Thumb", AnchorPresets.stretchAll);
            UI.AddImage(_detailStripThumbGO, GalleryUiColorTokens.SurfaceDarker, raycastTarget: true);
            GameObject thumbImgGO = UI.CreateChildRT(_detailStripThumbGO, "Image", AnchorPresets.stretchAll);
            RectTransform thumbImgRT = thumbImgGO.GetComponent<RectTransform>();
            thumbImgRT.offsetMin = Vector2.zero;
            thumbImgRT.offsetMax = Vector2.zero;
            _detailStripThumb = thumbImgGO.AddComponent<RawImage>();
            _detailStripThumb.color = Color.white;
            _detailStripThumb.raycastTarget = true;

            UIScrollWheelHandler thumbScroll = thumbCol.AddComponent<UIScrollWheelHandler>();
            thumbScroll.Sensitivity = 1f;
            thumbScroll.OnScrollValue = DetailStripOnThumbScroll;
            DetailStripEnsureThumbNavOverlay(thumbCol);
            DetailStripSyncThumbInteractions();

            GameObject textCol = UI.CreateChildRT(strip, "TextCol", AnchorPresets.stretchAll);
            UI.AddLE(textCol, flexibleWidth: 1f, minWidth: 80f * s, flexibleHeight: 0f);
            // Clip path/title Overflow so long strings never paint over SideCol.
            if (textCol.GetComponent<RectMask2D>() == null)
                textCol.AddComponent<RectMask2D>();
            // Small left pad so collapse hover rim is not clipped by RectMask2D at thumb seam.
            // Main gap to thumb stays HLG spacing on strip.
            float bandGap = DetailStripBandGap(s);
            UI.AddVLG(textCol, spacing: bandGap, padding: UI.Pad(2, 8, 4, 4, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);

            float lineH = DetailStripLineHeight(s);
            float hitH = DetailStripHitHeight(s);

            // Title left + subtle 5-star rating right.
            _detailStripTitleRowGO = UI.CreateChildRT(textCol, "TitleRow", AnchorPresets.hStretchTop);
            UI.AddHLG(_detailStripTitleRowGO, spacing: 6f * s, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false, childForceExpandHeight: true);
            UI.AddLE(_detailStripTitleRowGO, preferredHeight: lineH, minHeight: lineH, flexibleWidth: 1f, minWidth: 0f);
            DetailStripNormalizeRowRect(_detailStripTitleRowGO);

            // Collapse first — left of item name (reading order / proximity).
            DetailStripCreateCollapseButton(_detailStripTitleRowGO, s, lineH);

            _detailStripTitle = UI.CreateLabel(
                _detailStripTitleRowGO, "", GalleryUiDesignTokens.FontRef, new Color(1f, 1f, 1f, 0.95f),
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: true, richText: true, name: "Title");
            DetailStripApplyFont(_detailStripTitle, s);
            // preferredWidth 0 — same as flex Path/Tags; bare preferred-size drifts left at low scale.
            UI.AddLE(_detailStripTitle.gameObject, preferredHeight: lineH, minHeight: lineH,
                flexibleWidth: 1f, minWidth: 0f, preferredWidth: 0f);
            DetailStripBindClick(_detailStripTitle.gameObject, DetailStripOnTitleClick);
            AddDynamicTooltip(_detailStripTitle.gameObject, () =>
            {
                string name = _detailStripTitle != null ? (_detailStripTitle.text ?? "") : "";
                if (string.IsNullOrEmpty(name)) name = VPBTranslation.T("gallery.detail.tip.title", "Click: copy display name");
                return name + "\n" + VPBTranslation.T("gallery.detail.tip.title", "Click: copy display name");
            });

            // Status badges sit left of stars (not over thumb).
            _detailStripBadgeRowGO = UI.CreateChildRT(_detailStripTitleRowGO, "Badges", AnchorPresets.middleRight, new Vector2(80f * s, lineH));
            UI.AddLE(_detailStripBadgeRowGO, preferredHeight: lineH, minHeight: lineH, flexibleWidth: 0f);
            UI.AddHLG(_detailStripBadgeRowGO, spacing: 3f * s, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleRight, childForceExpandWidth: false, childForceExpandHeight: true);
            ContentSizeFitter badgeFit = _detailStripBadgeRowGO.AddComponent<ContentSizeFitter>();
            badgeFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            badgeFit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            _detailStripBadgeAuto = DetailStripCreateBadge(_detailStripBadgeRowGO, "A", GalleryBadgeLetterAutoInstall, s,
                VPBTranslation.T("gallery.badge.tip.auto_install", "Auto-install: package is in the auto-install list."));
            _detailStripBadgeHide = DetailStripCreateBadge(_detailStripBadgeRowGO, "H", GalleryBadgeLetterHide, s,
                VPBTranslation.T("gallery.badge.tip.hidden", "Hidden: package is marked hidden in the gallery."));
            _detailStripBadgeScan = DetailStripCreateBadge(_detailStripBadgeRowGO, "W", GalleryBadgeLetterScanWlPersistent, s,
                VPBTranslation.T("gallery.badge.tip.scan_wl_persistent", "Scan whitelist: included via whitelisted folder or saved UID override."));
            EnsureScanWlBadgeTempRing(_detailStripBadgeScan);
            _detailStripBadgeTags = DetailStripCreateBadge(_detailStripBadgeRowGO, "T", GalleryBadgeLetterUserTags, s,
                VPBTranslation.T("gallery.detail.tip.badge_tags", "Has user tags. Click Tag badge or Set Tags to manage."));

            DetailStripCreateStars(_detailStripTitleRowGO, s, lineH);

            // Balanced wrapping meta (Author mixed with facts; deps cluster kept together).
            // hStretchTop (not stretchAll) — prevents TextCol VLG bands from stacking on one rect.
            _detailStripMetaHost = UI.CreateChildRT(textCol, "MetaHost", AnchorPresets.hStretchTop);
            UI.AddVLG(_detailStripMetaHost, spacing: bandGap, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);
            _detailStripMetaHostLE = UI.AddLE(_detailStripMetaHost, preferredHeight: hitH * 2f, minHeight: hitH, flexibleWidth: 1f);
            DetailStripEnsureHostHeightDrivers(_detailStripMetaHost);
            DetailStripNormalizeRowRect(_detailStripMetaHost);
            _detailStripMetaRows = new GameObject[DetailStripMetaMaxRows];
            for (int ri = 0; ri < DetailStripMetaMaxRows; ri++)
            {
                GameObject row = UI.CreateChildRT(_detailStripMetaHost, "MetaRow" + ri, AnchorPresets.hStretchTop);
                UI.AddHLG(row, spacing: 8f * s, padding: UI.Pad(0, 0, 0, 0, s),
                    childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false, childForceExpandHeight: true);
                UI.AddLE(row, preferredHeight: hitH, minHeight: hitH, flexibleWidth: 1f);
                DetailStripNormalizeRowRect(row);
                row.SetActive(false);
                _detailStripMetaRows[ri] = row;
            }

            // Actions wrap across 2 rows — avoids clipping when many links visible.
            _detailStripActionsRowGO = UI.CreateChildRT(textCol, "Actions", AnchorPresets.hStretchTop);
            UI.AddVLG(_detailStripActionsRowGO, spacing: bandGap, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);
            UI.AddLE(_detailStripActionsRowGO,
                preferredHeight: hitH * DetailStripActionMaxRows + bandGap * (DetailStripActionMaxRows - 1),
                minHeight: hitH * DetailStripActionMaxRows + bandGap * (DetailStripActionMaxRows - 1),
                flexibleWidth: 1f);
            DetailStripEnsureHostHeightDrivers(_detailStripActionsRowGO);
            DetailStripNormalizeRowRect(_detailStripActionsRowGO);
            // Marker: band stack must keep minHeight == preferredHeight (never one-row min).
            UI.CreateChildRT(_detailStripActionsRowGO, "BandStackV1", AnchorPresets.topLeft, Vector2.zero)
                .SetActive(false);
            _detailStripToolsRowGO = null;
            _detailStripActionRows = new GameObject[DetailStripActionMaxRows];
            for (int ari = 0; ari < DetailStripActionMaxRows; ari++)
            {
                GameObject arow = UI.CreateChildRT(_detailStripActionsRowGO, "ActionRow" + ari, AnchorPresets.hStretchTop);
                UI.AddHLG(arow, spacing: 8f * s, padding: UI.Pad(0, 0, 0, 0, s),
                    childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false, childForceExpandHeight: true);
                UI.AddLE(arow, preferredHeight: hitH, minHeight: hitH, flexibleWidth: 1f);
                DetailStripNormalizeRowRect(arow);
                _detailStripActionRows[ari] = arow;
            }
            GameObject act0 = _detailStripActionRows[0];
            GameObject act1 = _detailStripActionRows[1];

            // Markers force rebuild of pre-hierarchy / pre-hit-pad / Load-CTA strips.
            UI.CreateChildRT(_detailStripActionsRowGO, "ActionWeightV2", AnchorPresets.topLeft, Vector2.zero)
                .SetActive(false);
            UI.CreateChildRT(_detailStripActionsRowGO, "ActionLoadV1", AnchorPresets.topLeft, Vector2.zero)
                .SetActive(false);
            UI.CreateChildRT(_detailStripActionsRowGO, "HitPadV1", AnchorPresets.topLeft, Vector2.zero)
                .SetActive(false);

            // Flat action density (power tool): all verbs visible; wrap across rows.
            // Weights: Load = launch primary; hard | then manage cluster (Hub first); Delete danger on hover.
            _detailStripLoadLink = DetailStripCreateActionLink(act0, "Load", "Load", s,
                DetailStripOnLoadClick, "gallery.detail.tip.load", "Click: load / open / apply selected item",
                DetailStripActionPrimary);
            DetailStripAddLinkSep(act0, s, hard: true);
            _detailStripHubLink = DetailStripCreateActionLink(act0, "Hub", "Hub", s,
                DetailStripOnHubClick, "gallery.detail.tip.hub", "Click: open this item on Hub",
                DetailStripActionSecondary);
            DetailStripAddLinkSep(act0, s);
            _detailStripCopyLink = DetailStripCreateActionLink(act0, "Copy", "Copy", s,
                DetailStripOnCopyClick, "gallery.detail.tip.copy", "Click: copy path(s) to clipboard (one per line)",
                DetailStripActionSecondary);
            DetailStripAddLinkSep(act0, s);
            _detailStripDeleteLink = DetailStripCreateActionLink(act0, "Delete", "Delete", s,
                DetailStripOnDeleteClick, "gallery.detail.tip.delete", "Click: move selection to DeletedPackages / DeletedScenes",
                DetailStripActionSecondary, DetailStripActionDanger);
            DetailStripAddLinkSep(act0, s);
            _detailStripCacheLink = DetailStripCreateActionLink(act0, "Cache", "Cache", s,
                DetailStripOnCacheClick, "gallery.detail.tip.cache", "Click: build zstd texture cache for selection (Ctrl=rewrite, Ctrl+Shift=purge)",
                DetailStripActionSecondary);
            DetailStripAddLinkSep(act0, s);
            _detailStripAutoLoadLink = DetailStripCreateActionLink(act0, "AutoLoad", "AutoLoad", s,
                DetailStripOnAutoLoadClick, "gallery.detail.tip.autoload", "Click: enable auto-install / auto-load for selection",
                DetailStripActionSecondary);
            _detailStripAutoLoadSepGO = DetailStripAddLinkSepGO(act0, s);
            _detailStripNoAutoLoadLink = DetailStripCreateActionLink(act0, "NoAutoLoad", "Clear AutoLoad", s,
                DetailStripOnNoAutoLoadClick, "gallery.detail.tip.no_autoload", "Click: clear auto-install / auto-load for selection",
                DetailStripActionSecondary);

            _detailStripHideLink = DetailStripCreateActionLink(act1, "Hide", "Hide", s,
                DetailStripOnHideClick, "gallery.detail.tip.hide", "Click: hide selected packages in VaM lists",
                DetailStripActionSecondary);
            _detailStripHideUnhideSepGO = DetailStripAddLinkSepGO(act1, s);
            _detailStripUnhideLink = DetailStripCreateActionLink(act1, "Unhide", "Unhide", s,
                DetailStripOnUnhideClick, "gallery.detail.tip.unhide", "Click: unhide selected packages in VaM lists",
                DetailStripActionSecondary);
            _detailStripAfterHideSepGO = DetailStripAddLinkSepGO(act1, s);
            _detailStripTempWlLink = DetailStripCreateActionLink(act1, "TempWL", "Temp whitelist", s,
                DetailStripOnTempWlClick, "gallery.detail.tip.temp_wl", "Click: temporary scan whitelist for selection",
                DetailStripActionSecondary);
            _detailStripBeforeOldVersSepGO = DetailStripAddLinkSepGO(act1, s);
            _detailStripOldVersLink = DetailStripCreateActionLink(act1, "OldVers", "Older versions", s,
                DetailStripOnCleanupOldVersionsClick, "gallery.detail.tip.old_vers", "Click: move older package versions to DeletedPackages/OldVersions",
                DetailStripActionSecondary);

            // User tags + path first (actionable). Desc + package tags last (read-only meta).
            // Set Tags: opens editor; individual chips filter gallery (author-style).
            DetailStripCreateTagsRow(textCol, s, lineH);

            _detailStripPath = DetailStripCreateFlexLine(textCol, "Path", new Color(0.62f, 0.62f, 0.65f, 0.92f), s, true, lineH);
            DetailStripBindClick(_detailStripPath.gameObject, DetailStripOnCopyClick);
            AddDynamicTooltip(_detailStripPath.gameObject, () =>
            {
                string path = _detailStripPath != null ? (_detailStripPath.text ?? "") : "";
                string tip = VPBTranslation.T("gallery.detail.tip.path", "Click: copy path to clipboard");
                if (string.IsNullOrEmpty(path)) return tip;
                return path + "\n" + tip;
            });

            _detailStripDesc = DetailStripCreateFlexLine(textCol, "Desc", DetailStripColorDesc, s, true, lineH);
            DetailStripBindClick(_detailStripDesc.gameObject, DetailStripOnDescriptionClick);
            AddDynamicTooltip(_detailStripDesc.gameObject, () =>
            {
                // Cached only — strip hydrate already ran TryEnsure; hover must not open .var ZIP.
                string full = DetailStripResolveDescription(_detailStripBoundFile, ensureMeta: false);
                if (string.IsNullOrEmpty(full))
                    return VPBTranslation.T("gallery.detail.tip.desc_empty", "No short description in meta.json");
                string tip = full + "\n" + VPBTranslation.T("gallery.detail.tip.desc_click", "Click: copy description");
                if (selectedFiles != null && selectedFiles.Count > 1)
                    tip += "\n" + VPBTranslation.T("gallery.detail.tip.first_item", "Shows first selected item");
                return tip;
            });

            _detailStripPackageTags = DetailStripCreateFlexLine(textCol, "PackageTags", DetailStripColorTag, s, false, lineH);
            AddDynamicTooltip(_detailStripPackageTags.gameObject, () =>
                VPBTranslation.T(
                    "gallery.detail.tip.native_tags",
                    "Native package tags from meta.json (clothing / hair regions)."));

            // Wide-pane right column — scrollable description + native tags (collapses when narrow).
            float sideW = GalleryUiDesignTokens.FooterDetailStripSideMinColWidthRef * s;
            GameObject sideCol = UI.CreateChildRT(strip, "SideCol", AnchorPresets.stretchAll);
            _detailStripSideColGO = sideCol;
            _detailStripSideColLE = UI.AddLE(sideCol,
                minWidth: sideW, preferredWidth: sideW,
                minHeight: 0f, preferredHeight: 0f,
                flexibleWidth: 0f, flexibleHeight: 0f);
            UI.AddVLG(sideCol, spacing: bandGap, padding: UI.Pad(10, 8, 6, 6, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);
            // Clip side desc/tags to SideCol — never paint past strip into toolbox.
            if (sideCol.GetComponent<RectMask2D>() == null)
                sideCol.AddComponent<RectMask2D>();

            DetailStripCreateSideDescScroll(sideCol, s, lineH * 3f);

            _detailStripSideNativeTags = DetailStripCreateSideBlock(
                sideCol, "SideNativeTags", DetailStripColorTag, s, lineH, wrap: true);
            AddDynamicTooltip(_detailStripSideNativeTags.gameObject, () =>
                VPBTranslation.T(
                    "gallery.detail.tip.native_tags",
                    "Native package tags from meta.json (clothing / hair regions)."));

            sideCol.SetActive(false);
            _detailStripSideVisible = false;
            DetailStripResetStackSideDecision();

            DetailStripEnsureResizeGrip();
            DetailStripSyncThumbSide();
            DetailStripApplyTextColPadForThumbSide(s);

            DetailStripEnsureExpandButton();

            // T badge also opens tag menu
            DetailStripBindClick(_detailStripBadgeTags, DetailStripOnTagClick);

            strip.SetActive(false);
        }

        private void DetailStripCreateSideDescScroll(GameObject sideCol, float s, float viewportH)
        {
            if (s <= 0f) s = 1f;
            float sbW = GalleryUiDesignTokens.FooterDetailStripSideScrollBarWidthRef * s;
            float lineH = DetailStripLineHeight(s);

            GameObject scrollGO = UI.CreateChildRT(sideCol, "SideDescScroll", AnchorPresets.stretchAll);
            _detailStripSideDescScrollGO = scrollGO;
            // flexibleHeight fills SideCol after tags; preferredHeight is a floor only.
            _detailStripSideDescScrollLE = UI.AddLE(scrollGO,
                preferredHeight: lineH, minHeight: lineH,
                flexibleWidth: 1f, flexibleHeight: 1f);
            // Raycast target so wheel works over empty padded areas too.
            Image scrollHit = UI.AddImage(scrollGO, new Color(0f, 0f, 0f, 0.01f), raycastTarget: true);
            if (scrollHit != null) scrollHit.raycastTarget = true;

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGO.transform, false);
            RectTransform vpRT = viewport.AddComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.pivot = new Vector2(0.5f, 0.5f);
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = new Vector2(-sbW, 0f);
            Image vpHit = viewport.AddComponent<Image>();
            vpHit.color = new Color(0f, 0f, 0f, 0.01f);
            vpHit.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0f, 0f);
            _detailStripSideDescContentRT = contentRT;
            UI.AddVLG(content, spacing: 0f, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);
            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text t = UI.CreateLabel(
                content, "", GalleryUiDesignTokens.FontRef, DetailStripColorDesc,
                TextAnchor.UpperLeft, HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow,
                raycastTarget: true, richText: true, name: "SideDesc");
            DetailStripApplyFont(t, s);
            _detailStripSideDesc = t;
            _detailStripSideDescLE = UI.AddLE(t.gameObject,
                preferredHeight: lineH, minHeight: lineH,
                flexibleWidth: 1f, minWidth: 0f, preferredWidth: 0f);

            DetailStripBindClick(t.gameObject, DetailStripOnDescriptionClick);
            AddDynamicTooltip(t.gameObject, () =>
            {
                string full = DetailStripResolveDescription(_detailStripBoundFile, ensureMeta: false);
                if (string.IsNullOrEmpty(full))
                    return VPBTranslation.T("gallery.detail.tip.desc_empty", "No short description in meta.json");
                string tip = full + "\n" + VPBTranslation.T("gallery.detail.tip.desc_click", "Click: copy description");
                if (selectedFiles != null && selectedFiles.Count > 1)
                    tip += "\n" + VPBTranslation.T("gallery.detail.tip.first_item", "Shows first selected item");
                return tip;
            });

            ScrollRect sr = scrollGO.AddComponent<ScrollRect>();
            sr.content = contentRT;
            sr.viewport = vpRT;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f * s;
            sr.verticalScrollbar = null;
            _detailStripSideDescScrollRect = sr;

            // Explicit wheel handlers — parent gallery scroll often wins GetEventHandler otherwise.
            UIScrollWheelHandler textWheel = t.gameObject.AddComponent<UIScrollWheelHandler>();
            textWheel.Sensitivity = 1f;
            textWheel.OnScrollValue = DetailStripOnSideDescWheel;
            UIScrollWheelHandler vpWheel = viewport.AddComponent<UIScrollWheelHandler>();
            vpWheel.Sensitivity = 1f;
            vpWheel.OnScrollValue = DetailStripOnSideDescWheel;
            UIScrollWheelHandler wheel = scrollGO.AddComponent<UIScrollWheelHandler>();
            wheel.Sensitivity = 1f;
            wheel.OnScrollValue = DetailStripOnSideDescWheel;

            GameObject sbGO = UI.CreateScrollBar(scrollGO, sbW, viewportH, Scrollbar.Direction.BottomToTop);
            Scrollbar sb = sbGO != null ? sbGO.GetComponent<Scrollbar>() : null;
            if (sb != null)
            {
                ScrollbarSync sync = sbGO.AddComponent<ScrollbarSync>();
                sync.scrollRect = sr;
                sync.scrollbar = sb;
                sync.minSizePixels = 18f * s;
            }
        }

        private void DetailStripOnSideDescWheel(float dy)
        {
            ScrollRect sr = _detailStripSideDescScrollRect;
            if (sr == null || !sr.isActiveAndEnabled) return;
            RectTransform content = sr.content;
            RectTransform viewport = sr.viewport;
            if (content == null || viewport == null) return;
            float overflow = content.rect.height - viewport.rect.height;
            if (overflow <= 1f) return;
            float step = (dy * Mathf.Max(48f, sr.scrollSensitivity)) / overflow;
            sr.verticalNormalizedPosition = Mathf.Clamp01(sr.verticalNormalizedPosition + step);
        }

        private static Text DetailStripCreateSideBlock(
            GameObject parent, string name, Color color, float s, float height, bool wrap)
        {
            Text t = UI.CreateLabel(
                parent, "", GalleryUiDesignTokens.FontRef, color,
                TextAnchor.UpperLeft,
                wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow,
                VerticalWrapMode.Truncate,
                raycastTarget: true, richText: true, name: name);
            DetailStripApplyFont(t, s);
            UI.AddLE(t.gameObject,
                preferredHeight: height, minHeight: DetailStripLineHeight(s),
                flexibleWidth: 1f, minWidth: 0f, preferredWidth: 0f);
            return t;
        }

        private void DetailStripClearUiRefs()
        {
            _detailStripRT = null;
            _detailStripBg = null;
            _detailStripThumb = null;
            _detailStripThumbGO = null;
            _detailStripThumbColGO = null;
            _detailStripResizeGripGO = null;
            _detailStripResizeGripBg = null;
            _detailStripResizeGripPill = null;
            _detailStripBadgeRowGO = null;
            _detailStripBadgeAuto = null;
            _detailStripBadgeHide = null;
            _detailStripBadgeScan = null;
            _detailStripBadgeTags = null;
            _detailStripTitleRowGO = null;
            _detailStripTitle = null;
            _detailStripCollapseBtnGO = null;
            _detailStripCollapseIconImage = null;
            // Expand button is InfoBar chrome — keep alive across strip rebuilds.
            _detailStripStarsGO = null;
            _detailStripStarImages = null;
            _detailStripStarRating = 0;
            _detailStripStarHover = 0;
            _detailStripThumbPrevBtnGO = null;
            _detailStripThumbPrevBtn = null;
            _detailStripThumbPrevBtnImage = null;
            _detailStripThumbNextBtnGO = null;
            _detailStripThumbNextBtn = null;
            _detailStripThumbNextBtnImage = null;
            _detailStripThumbScrubIndexGO = null;
            _detailStripThumbScrubIndexText = null;
            _detailStripThumbScrubIndexShown = int.MinValue;
            _detailStripThumbScrubCountShown = int.MinValue;
            _detailStripThumbScrubIndexVisible = false;
            _detailStripMetaHost = null;
            _detailStripMetaHostLE = null;
            _detailStripMetaRows = null;
            _detailStripActionsRowGO = null;
            _detailStripActionRows = null;
            _detailStripLoadLink = null;
            _detailStripCopyLink = null;
            _detailStripDeleteLink = null;
            _detailStripToolsRowGO = null;
            _detailStripHubLink = null;
            _detailStripCacheLink = null;
            _detailStripAutoLoadLink = null;
            _detailStripNoAutoLoadLink = null;
            _detailStripAutoLoadSepGO = null;
            _detailStripHideLink = null;
            _detailStripUnhideLink = null;
            _detailStripHideUnhideSepGO = null;
            _detailStripAfterHideSepGO = null;
            _detailStripTempWlLink = null;
            _detailStripOldVersLink = null;
            _detailStripBeforeOldVersSepGO = null;
            _detailStripDesc = null;
            _detailStripPackageTags = null;
            _detailStripTags = null;
            _detailStripTagsChipsHost = null;
            _detailStripTagClipboardActionsGO = null;
            _detailStripCopyTagsLink = null;
            _detailStripPasteTagsLink = null;
            _detailStripReplaceTagsLink = null;
            _detailStripTagsContentKey = "";
            _detailStripBoundTagNames = null;
            _detailStripTagMenuAppliedOrder = null;
            try { if (_detailStripTagFilterMenuGO != null) UnityEngine.Object.Destroy(_detailStripTagFilterMenuGO); } catch { }
            _detailStripTagFilterMenuGO = null;
            _detailStripTagFilterPanelRT = null;
            _detailStripPath = null;
            _detailStripSideColGO = null;
            _detailStripSideColLE = null;
            _detailStripSideDescScrollGO = null;
            _detailStripSideDescScrollLE = null;
            _detailStripSideDescScrollRect = null;
            _detailStripSideDescContentRT = null;
            _detailStripSideDesc = null;
            _detailStripSideDescLE = null;
            _detailStripSideNativeTags = null;
            _detailStripCacheKey = "";
            _detailStripThumbFile = null;
            _detailStripBoundFile = null;
            _detailStripBoundCreator = "";
            _detailStripMetaAvailWidth = -1f;
            _detailStripMeasuredHeight = -1f;
            _detailStripWantPath = false;
            _detailStripWantDesc = false;
            _detailStripWantTags = false;
            _detailStripWantNativeTags = false;
            _detailStripSideVisible = false;
            _detailStripSideContentKey = "";
            DetailStripInvalidateAutoHeightLock();
            DetailStripResetStackSideDecision();
        }

        private static Text DetailStripCreateFlexLine(GameObject parent, string name, Color color, float s, bool clickable, float height)
        {
            // Wrapper row + preferredWidth 0 keeps left edge aligned with Title/Meta
            // (bare Text preferred-width was shifting Path/Tags left toward the thumb).
            GameObject row = UI.CreateChildRT(parent, name + "Row", AnchorPresets.hStretchTop);
            UI.AddHLG(row, spacing: 0f, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: true, childForceExpandHeight: true);
            UI.AddLE(row, preferredHeight: height, minHeight: height, flexibleWidth: 1f, minWidth: 0f);

            Text t = UI.CreateLabel(
                row, "", GalleryUiDesignTokens.FontRef, color,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: clickable, richText: true, name: name);
            DetailStripApplyFont(t, s);
            UI.AddLE(t.gameObject, preferredHeight: height, minHeight: height,
                flexibleWidth: 1f, minWidth: 0f, preferredWidth: 0f);
            DetailStripNormalizeRowRect(row);
            return t;
        }

        /// <summary>
        /// Tags row: verb label opens quick-tag editor; chips toggle gallery tag filter (author-style);
        /// trailing Copy Tags / Paste Tags for 1→N stamp via session clipboard.
        /// </summary>
        private void DetailStripCreateTagsRow(GameObject parent, float s, float lineH)
        {
            if (parent == null) return;
            if (s <= 0f) s = 1f;

            GameObject row = UI.CreateChildRT(parent, "TagsRow", AnchorPresets.hStretchTop);
            UI.AddHLG(row, spacing: 4f * s, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false, childForceExpandHeight: true);
            UI.AddLE(row, preferredHeight: lineH, minHeight: lineH, flexibleWidth: 1f, minWidth: 0f);

            string setLabel = VPBTranslation.T("gallery.detail.set_tags", "Set Tags: ");
            _detailStripTags = UI.CreateLabel(
                row, setLabel, GalleryUiDesignTokens.FontRef, DetailStripColorTag,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: true, richText: false, name: "Tags");
            DetailStripApplyFont(_detailStripTags, s);
            ContentSizeFitter setCsf = _detailStripTags.gameObject.AddComponent<ContentSizeFitter>();
            setCsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            setCsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            float hitH = DetailStripHitHeight(s);
            UI.AddLE(_detailStripTags.gameObject, preferredHeight: hitH, minHeight: hitH,
                flexibleWidth: 0f, flexibleHeight: 0f);

            DetailStripBindClick(_detailStripTags.gameObject, DetailStripOnTagClick);
            Color setIdle = DetailStripColorTag;
            Color setHover = DetailStripBrighten(setIdle, 0.16f);
            UIHoverDelegate setHoverDel = _detailStripTags.gameObject.GetComponent<UIHoverDelegate>();
            if (setHoverDel == null) setHoverDel = _detailStripTags.gameObject.AddComponent<UIHoverDelegate>();
            setHoverDel.OnHoverChange += h =>
            {
                if (_detailStripTags == null) return;
                _detailStripTags.color = h ? setHover : setIdle;
            };
            AddDynamicTooltip(_detailStripTags.gameObject, () =>
                VPBTranslation.T("gallery.detail.tip.set_tags", "Click: open quick-tag editor"));

            _detailStripTagsChipsHost = UI.CreateChildRT(row, "TagChips", AnchorPresets.hStretchTop);
            UI.AddHLG(_detailStripTagsChipsHost, spacing: 0f, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: false, childForceExpandHeight: true);
            UI.AddLE(_detailStripTagsChipsHost, preferredHeight: lineH, minHeight: lineH,
                flexibleWidth: 1f, minWidth: 0f, preferredWidth: 0f);
            if (_detailStripTagsChipsHost.GetComponent<RectMask2D>() == null)
                _detailStripTagsChipsHost.AddComponent<RectMask2D>();

            DetailStripCreateTagClipboardActions(row, s, lineH, hitH);

            _detailStripTagsContentKey = "";
            DetailStripNormalizeRowRect(row);
            DetailStripSyncTagClipboardActionChrome();
        }

        private void DetailStripCreateTagClipboardActions(GameObject row, float s, float lineH, float hitH)
        {
            _detailStripTagClipboardActionsGO = UI.CreateChildRT(row, "TagClipboardActions", AnchorPresets.hStretchTop);
            UI.AddHLG(_detailStripTagClipboardActionsGO, spacing: 4f * s, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false, childForceExpandHeight: true);
            UI.AddLE(_detailStripTagClipboardActionsGO, preferredHeight: lineH, minHeight: lineH,
                flexibleWidth: 0f, flexibleHeight: 0f);

            DetailStripAddTagChipSep(_detailStripTagClipboardActionsGO, " · ", s, hitH);

            _detailStripCopyTagsLink = DetailStripCreateTagClipboardActionLink(
                _detailStripTagClipboardActionsGO,
                "CopyTags",
                VPBTranslation.T("gallery.detail.copy_tags", "Copy"),
                s, hitH,
                DetailStripOnCopyTagsClick,
                () => VPBTranslation.T(
                    "gallery.detail.tip.copy_tags",
                    "Click: copy user tags from selection to clipboard (paste onto other items)"));

            DetailStripAddTagChipSep(_detailStripTagClipboardActionsGO, " · ", s, hitH);

            _detailStripPasteTagsLink = DetailStripCreateTagClipboardActionLink(
                _detailStripTagClipboardActionsGO,
                "PasteTags",
                VPBTranslation.T("gallery.detail.paste_tags", "Paste"),
                s, hitH,
                DetailStripOnPasteTagsClick,
                DetailStripGetPasteTagsTooltip);

            DetailStripAddTagChipSep(_detailStripTagClipboardActionsGO, " · ", s, hitH);

            // Quiet secondary — recognition for replace without equal-peer shout (von Restorff).
            _detailStripReplaceTagsLink = DetailStripCreateTagClipboardActionLink(
                _detailStripTagClipboardActionsGO,
                "ReplaceTags",
                VPBTranslation.T("gallery.detail.replace_tags", "Replace"),
                s, hitH,
                DetailStripOnReplaceTagsClick,
                DetailStripGetReplaceTagsTooltip);
        }

        private Text DetailStripCreateTagClipboardActionLink(
            GameObject parent, string name, string label, float s, float hitH,
            UnityAction onClick, Func<string> tipFn)
        {
            Text t = UI.CreateLabel(
                parent, label ?? "", GalleryUiDesignTokens.FontRef, DetailStripActionSecondary,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: true, richText: false, name: "Link_" + name);
            DetailStripApplyFont(t, s);
            ContentSizeFitter csf = t.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            UI.AddLE(t.gameObject, preferredHeight: hitH, minHeight: hitH,
                flexibleWidth: 0f, flexibleHeight: 0f);

            DetailStripBindClick(t.gameObject, onClick);
            if (tipFn != null)
                AddDynamicTooltip(t.gameObject, tipFn);

            Color idle = DetailStripActionSecondary;
            Color hoverCol = DetailStripBrighten(idle, 0.18f);
            UIHoverDelegate hover = t.gameObject.GetComponent<UIHoverDelegate>();
            if (hover == null) hover = t.gameObject.AddComponent<UIHoverDelegate>();
            hover.OnHoverChange += h =>
            {
                if (t == null) return;
                if (!t.raycastTarget)
                {
                    t.color = DetailStripLinkDisabledColor;
                    return;
                }
                Color baseCol = DetailStripActionSecondary;
                if (t == _detailStripPasteTagsLink)
                {
                    baseCol = UserTagClipboardHasTags()
                        ? DetailStripColorTag
                        : DetailStripLinkDisabledColor;
                }
                else if (t == _detailStripReplaceTagsLink)
                {
                    // Quieter than Paste when ready; same disabled cue when empty.
                    baseCol = UserTagClipboardHasTags()
                        ? DetailStripActionSecondary
                        : DetailStripLinkDisabledColor;
                }
                else if (t == _detailStripCopyTagsLink
                    && (selectedFiles == null || selectedFiles.Count == 0))
                {
                    baseCol = DetailStripLinkDisabledColor;
                }
                t.color = h ? DetailStripBrighten(baseCol, 0.18f) : baseCol;
            };
            return t;
        }

        private void DetailStripOnCopyTagsClick()
        {
            UserTagClipboardCopyFromSelection();
        }

        private void DetailStripOnPasteTagsClick()
        {
            // Shift still accelerates replace for experts; visible Replace Tags is the discoverable path.
            bool replace = IsShiftHeld();
            UserTagClipboardPasteToSelection(replace);
        }

        private void DetailStripOnReplaceTagsClick()
        {
            UserTagClipboardPasteToSelection(replace: true);
        }

        private string DetailStripGetPasteTagsTooltip()
        {
            if (!UserTagClipboardHasTags())
            {
                return VPBTranslation.T(
                    "gallery.detail.tip.paste_tags_empty",
                    "Paste Tags: no tags copied yet. Copy Tags first, or put one tag name per line on the clipboard.\nUse Replace Tags (or Shift+click) to overwrite existing tags.");
            }
            if (IsShiftHeld())
            {
                return string.Format(
                    VPBTranslation.T(
                        "gallery.detail.tip.paste_tags_replace_fmt",
                        "Shift+click: replace user tags on selection with {0} copied tag(s). Release Shift to merge."),
                    UserTagClipboardTagCount());
            }
            return string.Format(
                VPBTranslation.T(
                    "gallery.detail.tip.paste_tags_fmt",
                    "Click: add {0} copied tag(s) to selection (merge). Use Replace Tags (or Shift+click) to overwrite."),
                UserTagClipboardTagCount());
        }

        private string DetailStripGetReplaceTagsTooltip()
        {
            if (!UserTagClipboardHasTags())
            {
                return VPBTranslation.T(
                    "gallery.detail.tip.replace_tags_empty",
                    "Replace Tags: no tags copied yet. Copy Tags first, or put one tag name per line on the clipboard.");
            }
            return string.Format(
                VPBTranslation.T(
                    "gallery.detail.tip.replace_tags_fmt",
                    "Click: replace user tags on selection with {0} copied tag(s). Tags not in the clipboard are removed."),
                UserTagClipboardTagCount());
        }

        private void DetailStripSyncTagClipboardActionChrome()
        {
            if (_detailStripCopyTagsLink != null)
            {
                bool canCopy = selectedFiles != null && selectedFiles.Count > 0;
                _detailStripCopyTagsLink.raycastTarget = true; // always clickable → status explains empty
                _detailStripCopyTagsLink.color = canCopy
                    ? DetailStripActionSecondary
                    : DetailStripLinkDisabledColor;
            }

            bool has = UserTagClipboardHasTags();
            if (_detailStripPasteTagsLink != null)
            {
                // Keep clickable so empty buffer explains itself (no silent disable).
                _detailStripPasteTagsLink.raycastTarget = true;
                _detailStripPasteTagsLink.color = has
                    ? DetailStripColorTag
                    : DetailStripLinkDisabledColor;
            }

            if (_detailStripReplaceTagsLink != null)
            {
                _detailStripReplaceTagsLink.raycastTarget = true;
                _detailStripReplaceTagsLink.color = has
                    ? DetailStripActionSecondary
                    : DetailStripLinkDisabledColor;
            }
        }

        private static void DetailStripSetFlexLineActive(Text line, bool active)
        {
            if (line == null) return;
            Transform row = line.transform.parent;
            if (row != null)
            {
                row.gameObject.SetActive(active);
                if (active) DetailStripNormalizeRowRect(row.gameObject);
            }
            else line.gameObject.SetActive(active);
        }

        private void DetailStripCreateStars(GameObject parent, float s, float lineH)
        {
            float starSz = 14f * s;
            float gap = 2f * s;
            float rowW = starSz * 5f + gap * 4f;
            _detailStripStarsGO = UI.CreateChildRT(parent, "Stars", AnchorPresets.middleRight, new Vector2(rowW, lineH));
            UI.AddLE(_detailStripStarsGO, minWidth: rowW, preferredWidth: rowW, preferredHeight: lineH, flexibleWidth: 0f);
            UI.AddHLG(_detailStripStarsGO, spacing: gap, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.MiddleRight, childForceExpandWidth: false, childForceExpandHeight: true);

            Sprite onSpr = ratingStarNormalSprite;
            Sprite offSpr = ratingStarOffSprite;
            if (onSpr == null)
            {
                try { onSpr = UI.LoadIconSprite("vpb_icons/star.png", Color.white); } catch { }
            }
            if (offSpr == null)
            {
                try { offSpr = UI.LoadIconSprite("vpb_icons/star_off.png", Color.white); } catch { }
            }
            if (onSpr == null) onSpr = offSpr;
            if (offSpr == null) offSpr = onSpr;

            _detailStripStarImages = new Image[5];
            _detailStripStarRating = 0;
            _detailStripStarHover = 0;
            for (int i = 0; i < 5; i++)
            {
                int starValue = i + 1;
                GameObject starGO = UI.CreateChildRT(_detailStripStarsGO, "Star" + starValue, AnchorPresets.middleCenter, new Vector2(starSz, starSz));
                UI.AddLE(starGO, minWidth: starSz, preferredWidth: starSz, minHeight: starSz, preferredHeight: starSz);
                Image img = starGO.AddComponent<Image>();
                img.sprite = offSpr != null ? offSpr : onSpr;
                img.color = DetailStripStarOffColor;
                img.raycastTarget = true;
                img.preserveAspect = true;
                _detailStripStarImages[i] = img;

                int captured = starValue;
                DetailStripBindClick(starGO, () => DetailStripOnStarClick(captured));
                UIHoverDelegate hover = starGO.GetComponent<UIHoverDelegate>();
                if (hover == null) hover = starGO.AddComponent<UIHoverDelegate>();
                hover.OnHoverChange += h =>
                {
                    _detailStripStarHover = h ? captured : 0;
                    DetailStripPaintStars();
                };
                AddTooltipPlain(starGO, string.Format(
                    VPBTranslation.T("gallery.detail.tip.star_fmt", "Rate {0}/5 (click again to clear)"), starValue));
            }
        }

        private void DetailStripCreateCollapseButton(GameObject parent, float s, float lineH)
        {
            if (parent == null) return;
            if (s <= 0f) s = 1f;
            float sz = Mathf.Clamp(lineH, 16f * s, 22f * s);
            _detailStripCollapseBtnGO = UI.CreateUIButton(
                parent, sz, sz, "",
                GalleryUiDesignTokens.FontMinRef,
                0f, 0f, AnchorPresets.middleLeft,
                DetailStripToggleExpanded);
            _detailStripCollapseBtnGO.name = "DetailStrip_Collapse";
            _detailStripCollapseBtnGO.transform.SetAsFirstSibling();
            LayoutElement le = _detailStripCollapseBtnGO.GetComponent<LayoutElement>();
            if (le == null) le = _detailStripCollapseBtnGO.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = sz;
            le.minHeight = le.preferredHeight = sz;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;

            try
            {
                if (_detailStripCollapseSprite == null)
                    _detailStripCollapseSprite = UI.LoadIconSprite("vpb_icons/chevron_down.png", new Color(0.78f, 0.78f, 0.80f, 1f));
                if (_detailStripCollapseSprite != null)
                {
                    UI.AddIconToButton(
                        _detailStripCollapseBtnGO, _detailStripCollapseSprite,
                        padding: Mathf.Max(2f, 3f * s),
                        backdropOverride: new Color(0.14f, 0.14f, 0.17f, 0.92f));
                    Transform iconTr = _detailStripCollapseBtnGO.transform.Find("Icon");
                    if (iconTr != null) _detailStripCollapseIconImage = iconTr.GetComponent<Image>();
                }
                else
                {
                    Text t = _detailStripCollapseBtnGO.GetComponentInChildren<Text>(true);
                    if (t != null)
                    {
                        t.gameObject.SetActive(true);
                        t.text = "▾";
                        t.color = new Color(0.78f, 0.78f, 0.80f, 1f);
                    }
                }
            }
            catch { }

            AddTooltip(
                _detailStripCollapseBtnGO,
                "gallery.detail.tip.collapse",
                "Hide details — reclaim grid space. Show again from Details in toolbox.");
        }

        private static float DetailStripExpandButtonWidth(float s)
        {
            if (s <= 0f) s = 1f;
            return 108f * s;
        }

        /// <summary>Left reserve for Details on the top toolbox band only (lower wrap rows stay full-bleed).</summary>
        private float DetailStripExpandLeftReserve(float s)
        {
            if (s <= 0f) s = 1f;
            if (_detailStripExpandBtnGO == null || !_detailStripExpandBtnGO.activeSelf) return 0f;
            return DetailStripExpandButtonWidth(s) + 10f * s;
        }

        private void DetailStripEnsureExpandButton()
        {
            if (_detailStripExpandBtnGO != null) return;
            EnsureTboxUI();
            if (tboxButtonsLayerRT == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float innerH = TboxActionButtonInnerHeight();
            float iconSz = 16f * s;
            float btnW = DetailStripExpandButtonWidth(s);
            string label = VPBTranslation.T("gallery.detail.expand", "Details");

            // Fixed top-left chrome — one action-row tall; wrap rows 2+ use space under it.
            _detailStripExpandBtnGO = new GameObject("DetailStrip_Expand");
            _detailStripExpandBtnGO.transform.SetParent(tboxButtonsLayerRT, false);
            Image bg = UI.AddGalleryElementRoundedBg(_detailStripExpandBtnGO, new Color(0.16f, 0.20f, 0.30f, 0.96f));
            RectTransform rt = _detailStripExpandBtnGO.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(btnW, innerH);
                rt.anchoredPosition = new Vector2(8f * s, -2f * s);
            }

            Button btn = _detailStripExpandBtnGO.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = bg;
            btn.onClick.AddListener(DetailStripToggleExpanded);
            try
            {
                UIHoverBorder hb = _detailStripExpandBtnGO.AddComponent<UIHoverBorder>();
                hb.ApplyBorderSettings();
            }
            catch { }

            UI.AddHLG(
                _detailStripExpandBtnGO,
                spacing: 6f * s,
                padding: UI.Pad(8, 10, 0, 0, s),
                childAlignment: TextAnchor.MiddleCenter,
                childForceExpandWidth: false,
                childForceExpandHeight: true);

            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(_detailStripExpandBtnGO.transform, false);
            _detailStripExpandIconImage = UI.AddImage(iconGO, Color.white, raycastTarget: false);
            _detailStripExpandIconImage.preserveAspect = true;
            UI.AddLE(iconGO, minWidth: iconSz, preferredWidth: iconSz, minHeight: iconSz, preferredHeight: iconSz, flexibleWidth: 0f);

            try
            {
                if (_detailStripExpandSprite == null)
                    _detailStripExpandSprite = UI.LoadIconSprite("vpb_icons/chevron_up.png", new Color(0.85f, 0.88f, 0.95f, 1f));
                if (_detailStripExpandSprite != null)
                    _detailStripExpandIconImage.sprite = _detailStripExpandSprite;
            }
            catch { }

            _detailStripExpandLabel = UI.CreateLabel(
                _detailStripExpandBtnGO, label, GalleryUiDesignTokens.FontBodyRef,
                new Color(0.92f, 0.94f, 0.98f, 1f), TextAnchor.MiddleLeft,
                raycastTarget: false, name: "Label");
            GalleryUiMetrics.ApplyFont(
                _detailStripExpandLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);

            _detailStripExpandBtnLE = UI.AddLE(
                _detailStripExpandBtnGO,
                minWidth: btnW, preferredWidth: btnW,
                minHeight: innerH, preferredHeight: innerH, flexibleWidth: 0f);

            UIScrollWheelHandler wheel = _detailStripExpandBtnGO.AddComponent<UIScrollWheelHandler>();
            wheel.Sensitivity = 1f;
            wheel.OnScrollValue = DetailStripOnThumbScroll;

            _detailStripExpandBtnGO.SetActive(false);

            AddTooltip(
                _detailStripExpandBtnGO,
                "gallery.detail.tip.expand",
                "Show selection details above toolbox. Scroll: previous / next selection.");
            DetailStripSyncExpandButtonChrome(s);
        }

        private void DetailStripToggleExpanded()
        {
            DetailStripSetExpanded(!DetailStripIsExpanded());
        }

        private void DetailStripSetExpanded(bool expanded)
        {
            if (VPBConfig.Instance != null)
            {
                if (VPBConfig.Instance.GalleryDetailStripExpanded == expanded)
                {
                    DetailStripSyncCollapseExpandChrome();
                    return;
                }
                VPBConfig.Instance.GalleryDetailStripExpanded = expanded;
                // Persist across restart.
                try { VPBConfig.Instance.Save(false); } catch { }
            }

            if (!expanded)
            {
                // Tag editor is independent of strip expand — never auto-close on collapse.
                try { DetailStripCloseTagFilterPopup(); } catch { }
                if (_detailStripGO != null) _detailStripGO.SetActive(false);
            }
            else
            {
                // Force populate on re-open (strip may have been soft-hidden).
                _detailStripCacheKey = "";
            }

            try { DetailStripRefresh(); } catch { }
            DetailStripSyncCollapseExpandChrome();
        }

        private void DetailStripSyncCollapseExpandChrome()
        {
            int sel = selectedFiles != null ? selectedFiles.Count : 0;
            bool hasSel = sel > 0;
            bool expanded = DetailStripIsExpanded();
            bool stripUp = expanded
                && _detailStripGO != null
                && _detailStripGO.activeSelf;

            if (_detailStripExpandBtnGO != null)
            {
                bool want = hasSel && !expanded;
                bool changed = _detailStripExpandBtnGO.activeSelf != want;
                if (changed) _detailStripExpandBtnGO.SetActive(want);
                if (changed)
                {
                    if (want)
                        try { DetailStripSyncExpandButtonChrome(ChromeScale); } catch { }
                    else
                        try { DetailStripApplyToolboxFlexLeftInset(ChromeScale); } catch { }
                    try { RefreshTboxFlexButtonLayout(); } catch { }
                }
                if (want)
                    _detailStripExpandBtnGO.transform.SetAsLastSibling();
            }
            if (_detailStripCollapseBtnGO != null)
            {
                _detailStripCollapseBtnGO.SetActive(hasSel && stripUp);
                if (_detailStripCollapseBtnGO.activeSelf)
                    _detailStripCollapseBtnGO.transform.SetAsFirstSibling();
            }
        }

        private void DetailStripSyncCollapseButtonChrome(float s, float lineH)
        {
            if (_detailStripCollapseBtnGO == null) return;
            if (s <= 0f) s = 1f;
            float sz = Mathf.Clamp(lineH, 16f * s, 22f * s);
            LayoutElement le = _detailStripCollapseBtnGO.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = le.preferredWidth = sz;
                le.minHeight = le.preferredHeight = sz;
            }
            RectTransform rt = _detailStripCollapseBtnGO.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(sz, sz);
            Transform iconTr = _detailStripCollapseBtnGO.transform.Find("Icon");
            if (iconTr != null)
            {
                RectTransform iconRT = iconTr as RectTransform;
                if (iconRT != null)
                {
                    float pad = Mathf.Max(2f, 3f * s);
                    iconRT.offsetMin = new Vector2(pad, pad);
                    iconRT.offsetMax = new Vector2(-pad, -pad);
                }
            }
        }

        private void DetailStripSyncExpandButtonChrome(float s)
        {
            if (_detailStripExpandBtnGO == null) return;
            if (s <= 0f) s = 1f;
            float innerH = TboxActionButtonInnerHeight();
            float iconSz = 16f * s;
            float btnW = DetailStripExpandButtonWidth(s);
            float pad = 8f * s;

            if (tboxButtonsLayerRT != null && _detailStripExpandBtnGO.transform.parent != tboxButtonsLayerRT)
                _detailStripExpandBtnGO.transform.SetParent(tboxButtonsLayerRT, false);

            RectTransform rt = _detailStripExpandBtnGO.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = new Vector2(btnW, innerH);
                rt.anchoredPosition = new Vector2(pad, -2f * s);
            }

            HorizontalLayoutGroup hlg = _detailStripExpandBtnGO.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.spacing = 6f * s;
                hlg.padding = UI.Pad(8, 10, 0, 0, s);
            }
            if (_detailStripExpandBtnLE != null)
            {
                _detailStripExpandBtnLE.minWidth = _detailStripExpandBtnLE.preferredWidth = btnW;
                _detailStripExpandBtnLE.minHeight = _detailStripExpandBtnLE.preferredHeight = innerH;
                _detailStripExpandBtnLE.flexibleWidth = 0f;
            }
            if (_detailStripExpandIconImage != null)
            {
                LayoutElement iconLe = _detailStripExpandIconImage.GetComponent<LayoutElement>();
                if (iconLe != null)
                {
                    iconLe.minWidth = iconLe.preferredWidth = iconSz;
                    iconLe.minHeight = iconLe.preferredHeight = iconSz;
                }
            }
            if (_detailStripExpandLabel != null)
            {
                _detailStripExpandLabel.text = VPBTranslation.T("gallery.detail.expand", "Details");
                GalleryUiMetrics.ApplyFont(
                    _detailStripExpandLabel, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            DetailStripApplyToolboxFlexLeftInset(s);
        }

        private static void DetailStripSetHlgLeftPad(HorizontalLayoutGroup hlg, float leftPad, int rightPad)
        {
            if (hlg == null) return;
            RectOffset p = hlg.padding;
            int left = Mathf.Max(0, Mathf.RoundToInt(leftPad));
            hlg.padding = new RectOffset(left, rightPad >= 0 ? rightPad : p.right, p.top, p.bottom);
        }

        /// <summary>
        /// Flex root stays full-bleed. Only the top band beside Details gets a left pad —
        /// wrap rows 2+ reclaim the void under the one-row Details chrome.
        /// </summary>
        private void DetailStripApplyToolboxFlexLeftInset(float s)
        {
            if (tboxButtonsFlexRootRT == null) return;
            if (s <= 0f) s = 1f;
            tboxButtonsFlexRootRT.offsetMin = new Vector2(8f * s, tboxButtonsFlexRootRT.offsetMin.y);
            if (tboxButtonsFlexRootRT.offsetMax.x > -1f)
                tboxButtonsFlexRootRT.offsetMax = new Vector2(-12f * s, tboxButtonsFlexRootRT.offsetMax.y);

            float detailsPad = DetailStripExpandLeftReserve(s);
            bool clothingTop = tboxClothingModeRowGO != null && tboxClothingModeRowGO.activeSelf;
            // Clothing sits above action rows when active — it shares the Details vertical band.
            float clothingLeft = detailsPad > 0f ? detailsPad : 8f * s;
            DetailStripSetHlgLeftPad(tboxClothingModeRowHLG, clothingTop ? clothingLeft : 8f * s, -1);
            DetailStripSetHlgLeftPad(tboxBtnRow0HLG, (!clothingTop && detailsPad > 0f) ? detailsPad : 0f, 0);
            DetailStripSetHlgLeftPad(tboxBtnRow1HLG, 0f, 0);
            DetailStripSetHlgLeftPad(tboxBtnRow2HLG, 0f, 0);
        }

        private void DetailStripRefreshStars()
        {
            if (_detailStripStarsGO == null) return;
            int rating = 0;
            bool enabled = selectedFiles != null && selectedFiles.Count > 0;
            if (enabled)
            {
                try { rating = TboxConsensusRatingDisplay(selectedFiles); }
                catch { rating = 0; }
            }
            _detailStripStarRating = Mathf.Clamp(rating, 0, 5);
            _detailStripStarHover = 0;
            _detailStripStarsGO.SetActive(enabled);
            if (_detailStripStarImages != null)
            {
                for (int i = 0; i < _detailStripStarImages.Length; i++)
                {
                    if (_detailStripStarImages[i] != null)
                        _detailStripStarImages[i].raycastTarget = enabled;
                }
            }
            DetailStripPaintStars();
        }

        private void DetailStripPaintStars()
        {
            if (_detailStripStarImages == null) return;
            int show = _detailStripStarHover > 0 ? _detailStripStarHover : _detailStripStarRating;
            Sprite onSpr = ratingStarNormalSprite;
            Sprite offSpr = ratingStarOffSprite;
            if (onSpr == null) onSpr = offSpr;
            if (offSpr == null) offSpr = onSpr;

            for (int i = 0; i < _detailStripStarImages.Length; i++)
            {
                Image img = _detailStripStarImages[i];
                if (img == null) continue;
                bool on = i < show;
                if (on && onSpr != null) img.sprite = onSpr;
                else if (!on && offSpr != null) img.sprite = offSpr;
                if (_detailStripStarHover > 0 && on)
                    img.color = DetailStripStarPreviewColor;
                else
                    img.color = on ? DetailStripStarOnColor : DetailStripStarOffColor;
            }
        }

        private void DetailStripOnStarClick(int starValue)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            starValue = Mathf.Clamp(starValue, 0, 5);
            // Same star again clears rating (0).
            int next = (starValue > 0 && starValue == _detailStripStarRating) ? 0 : starValue;
            DetailStripApplyStarRating(next);
        }

        private GameObject DetailStripCreateBadge(GameObject parent, string letter, Color letterColor, float s, string tip)
        {
            float sz = 18f * s;
            GameObject go = UI.CreateChildRT(parent, "Badge_" + letter, AnchorPresets.middleLeft, new Vector2(sz, sz));
            UI.AddLE(go, minWidth: sz, preferredWidth: sz, minHeight: sz, preferredHeight: sz);
            AddGalleryBadgeBackground(go);
            Text t = UI.CreateLabel(go, letter, GalleryUiDesignTokens.FontMinRef, letterColor, TextAnchor.MiddleCenter,
                raycastTarget: false, name: "Text");
            DetailStripApplyFont(t, s, GalleryUiDesignTokens.FontMinRef);
            Image img = go.GetComponent<Image>();
            if (img != null) img.raycastTarget = true;
            AddTooltipPlain(go, tip);
            go.SetActive(false);
            return go;
        }

        private Text DetailStripCreateActionLink(
            GameObject parent, string name, string label, float s,
            UnityAction onClick, string tipKey, string tipDefault, Color idleColor,
            Color? hoverColor = null)
        {
            Text t = UI.CreateLabel(
                parent, label, GalleryUiDesignTokens.FontRef, idleColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: true, richText: true, name: "Link_" + name);
            DetailStripApplyFont(t, s);
            ContentSizeFitter csf = t.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Vertical PreferredSize fights row LE at high scale (~1.6) → Truncate clip.
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            float hitH = DetailStripHitHeight(s);
            UI.AddLE(t.gameObject, minHeight: hitH, preferredHeight: hitH, flexibleWidth: 0f, flexibleHeight: 0f);

            AddTooltip(t.gameObject, tipKey, tipDefault);
            DetailStripBindClick(t.gameObject, onClick);

            Color idle = idleColor;
            Color hoverCol = hoverColor ?? DetailStripBrighten(idle, 0.18f);
            UIHoverDelegate hover = t.gameObject.GetComponent<UIHoverDelegate>();
            if (hover == null) hover = t.gameObject.AddComponent<UIHoverDelegate>();
            hover.OnHoverChange += h =>
            {
                if (t == null) return;
                if (!t.raycastTarget)
                {
                    t.color = DetailStripLinkDisabledColor;
                    return;
                }
                if (t.text != null && t.text.IndexOf("<color=", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
                t.color = h ? hoverCol : idle;
            };
            return t;
        }

        private static Color DetailStripBrighten(Color c, float amount)
        {
            return new Color(
                Mathf.Clamp01(c.r + amount),
                Mathf.Clamp01(c.g + amount),
                Mathf.Clamp01(c.b + amount),
                c.a);
        }

        private void DetailStripAddLinkSep(GameObject parent, float s, bool hard = false)
        {
            DetailStripAddLinkSepGO(parent, s, hard);
        }

        private GameObject DetailStripAddLinkSepGO(GameObject parent, float s, bool hard = false)
        {
            // Soft · between manage peers; hard | after Load (use vs manage cluster).
            string glyph = hard ? "|" : "·";
            Color col = hard
                ? new Color(0.62f, 0.64f, 0.70f, 0.95f)
                : new Color(0.50f, 0.50f, 0.54f, 0.90f);
            Text sep = UI.CreateLabel(
                parent, glyph, GalleryUiDesignTokens.FontRef, col,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "Sep");
            DetailStripApplyFont(sep, s);
            ContentSizeFitter csf = sep.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            float hitH = DetailStripHitHeight(s);
            UI.AddLE(sep.gameObject, minHeight: hitH, preferredHeight: hitH, flexibleWidth: 0f, flexibleHeight: 0f);
            return sep.gameObject;
        }

        private void DetailStripBindClick(GameObject go, UnityAction onClick)
        {
            if (go == null || onClick == null) return;
            EventTrigger et = go.GetComponent<EventTrigger>();
            if (et == null) et = go.AddComponent<EventTrigger>();
            et.triggers.Clear();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(data =>
            {
                var ped = data as PointerEventData;
                if (ped != null && ped.button != PointerEventData.InputButton.Left) return;
                onClick();
            });
            et.triggers.Add(entry);
        }

        /// <summary>Tooltip + double-click launch + overlay prev/next on preview thumb.</summary>
        private void DetailStripSyncThumbInteractions()
        {
            GameObject thumbCol = _detailStripThumbColGO;
            if (thumbCol == null && _detailStripGO != null)
            {
                Transform t = _detailStripGO.transform.Find("ThumbCol");
                if (t != null) thumbCol = t.gameObject;
            }
            if (thumbCol == null) return;

            // Status bar is one line tall — keep tip as a single line (newlines truncate).
            AddTooltip(
                thumbCol,
                "gallery.detail.tip.thumb",
                "◀ ▶ / Scroll: prev/next · Double-click: launch / apply");

            // EventTrigger implements IScrollHandler and swallows wheel if placed on the hit
            // target above UIScrollWheelHandler — never put EventTrigger on Thumb/Image children.
            DetailStripStripEventTrigger(_detailStripThumbGO);
            if (_detailStripThumb != null)
                DetailStripStripEventTrigger(_detailStripThumb.gameObject);

            DetailStripEnsureThumbScroll(thumbCol);
            DetailStripEnsureThumbScroll(_detailStripThumbGO);
            if (_detailStripThumb != null)
                DetailStripEnsureThumbScroll(_detailStripThumb.gameObject);

            DetailStripEnsureThumbNavOverlay(thumbCol);
            DetailStripBindThumbInput(thumbCol);
            DetailStripSyncThumbNavChrome();
            DetailStripSyncScrubIndexOverlay();
        }

        private static void DetailStripStripEventTrigger(GameObject go)
        {
            if (go == null) return;
            EventTrigger et = go.GetComponent<EventTrigger>();
            if (et != null)
            {
                try { UnityEngine.Object.Destroy(et); } catch { }
            }
        }

        private void DetailStripEnsureThumbScroll(GameObject go)
        {
            if (go == null) return;
            UIScrollWheelHandler wheel = go.GetComponent<UIScrollWheelHandler>();
            if (wheel == null) wheel = go.AddComponent<UIScrollWheelHandler>();
            wheel.Sensitivity = 1f;
            wheel.OnScrollValue = DetailStripOnThumbScroll;
        }

        /// <summary>Double-click apply on thumb column (no EventTrigger / IScrollHandler).</summary>
        private void DetailStripBindThumbInput(GameObject go)
        {
            if (go == null) return;
            DetailStripThumbClickRelay relay = go.GetComponent<DetailStripThumbClickRelay>();
            if (relay == null) relay = go.AddComponent<DetailStripThumbClickRelay>();
            relay.OnDoubleClick = DetailStripOnThumbDoubleClick;
            DetailStripStripEventTrigger(go);
        }

        private void DetailStripOnThumbDoubleClick()
        {
            DetailStripLaunchBoundOrFirstSelected();
        }

        /// <summary>Load action + thumb double-click — same apply/open path as grid launch.</summary>
        private void DetailStripOnLoadClick()
        {
            DetailStripLaunchBoundOrFirstSelected();
        }

        private void DetailStripLaunchBoundOrFirstSelected()
        {
            if (_benchPickModeActive) return;
            if (_stripKeepSubScenePickActive) return;
            FileEntry file = _detailStripBoundFile;
            if (file == null && selectedFiles != null && selectedFiles.Count > 0)
                file = selectedFiles[0];
            if (file == null) return;
            ApplyFileEntryNow(file);
        }

        private void DetailStripOnThumbPrevClick()
        {
            DetailStripThumbScrubBy(-1);
        }

        private void DetailStripOnThumbNextClick()
        {
            DetailStripThumbScrubBy(1);
        }

        /// <summary>
        /// Overlay ◀▶ on thumb image (absolute anchors). Does not pad/inset RawImage —
        /// preview stays full square; buttons draw on top.
        /// </summary>
        private void DetailStripEnsureThumbNavOverlay(GameObject thumbCol)
        {
            if (thumbCol == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            if (_detailStripThumbNavPrevSprite == null)
            {
                try { _detailStripThumbNavPrevSprite = UI.LoadIconSprite("vpb_icons/chevron_left.png", UI.BarIconGlyphTint); }
                catch { _detailStripThumbNavPrevSprite = null; }
            }
            if (_detailStripThumbNavNextSprite == null)
            {
                try { _detailStripThumbNavNextSprite = UI.LoadIconSprite("vpb_icons/chevron_right.png", UI.BarIconGlyphTint); }
                catch { _detailStripThumbNavNextSprite = null; }
            }

            if (_detailStripThumbPrevBtnGO == null)
            {
                Transform existing = thumbCol.transform.Find("NavPrev");
                if (existing != null) _detailStripThumbPrevBtnGO = existing.gameObject;
            }
            if (_detailStripThumbNextBtnGO == null)
            {
                Transform existing = thumbCol.transform.Find("NavNext");
                if (existing != null) _detailStripThumbNextBtnGO = existing.gameObject;
            }
            if (_detailStripThumbScrubIndexGO == null)
            {
                Transform existing = thumbCol.transform.Find("ScrubIndex");
                if (existing != null) _detailStripThumbScrubIndexGO = existing.gameObject;
            }

            if (_detailStripThumbPrevBtnGO == null)
            {
                _detailStripThumbPrevBtnGO = DetailStripCreateThumbNavButton(
                    thumbCol, "NavPrev", AnchorPresets.middleLeft,
                    _detailStripThumbNavPrevSprite, DetailStripOnThumbPrevClick);
                AddTooltip(_detailStripThumbPrevBtnGO, "gallery.detail.tip.thumb_prev", "Previous item");
            }
            if (_detailStripThumbNextBtnGO == null)
            {
                _detailStripThumbNextBtnGO = DetailStripCreateThumbNavButton(
                    thumbCol, "NavNext", AnchorPresets.middleRight,
                    _detailStripThumbNavNextSprite, DetailStripOnThumbNextClick);
                AddTooltip(_detailStripThumbNextBtnGO, "gallery.detail.tip.thumb_next", "Next item");
            }

            if (_detailStripThumbPrevBtn == null && _detailStripThumbPrevBtnGO != null)
                _detailStripThumbPrevBtn = _detailStripThumbPrevBtnGO.GetComponent<Button>();
            if (_detailStripThumbPrevBtnImage == null && _detailStripThumbPrevBtnGO != null)
                _detailStripThumbPrevBtnImage = _detailStripThumbPrevBtnGO.GetComponent<Image>();
            if (_detailStripThumbNextBtn == null && _detailStripThumbNextBtnGO != null)
                _detailStripThumbNextBtn = _detailStripThumbNextBtnGO.GetComponent<Button>();
            if (_detailStripThumbNextBtnImage == null && _detailStripThumbNextBtnGO != null)
                _detailStripThumbNextBtnImage = _detailStripThumbNextBtnGO.GetComponent<Image>();

            DetailStripEnsureThumbScroll(_detailStripThumbPrevBtnGO);
            DetailStripEnsureThumbScroll(_detailStripThumbNextBtnGO);

            if (_detailStripThumbScrubIndexGO == null)
            {
                float indexH = GalleryUiDesignTokens.ButtonSizeRef * s;
                float indexW = Mathf.Max(72f * s, GalleryUiDesignTokens.ButtonSizeRef * s * 2.4f);
                float insetY = GalleryUiDesignTokens.FooterDetailStripThumbNavInsetRef * s;
                _detailStripThumbScrubIndexGO = UI.AddChildGOImage(
                    thumbCol, DetailStripThumbScrubIndexBg, AnchorPresets.bottomMiddle,
                    indexW, indexH, new Vector2(0f, insetY), rounded: true);
                _detailStripThumbScrubIndexGO.name = "ScrubIndex";
                Image bg = _detailStripThumbScrubIndexGO.GetComponent<Image>();
                if (bg != null) bg.raycastTarget = false;
                _detailStripThumbScrubIndexText = UI.CreateLabel(
                    _detailStripThumbScrubIndexGO, "",
                    GalleryUiDesignTokens.FontBodyRef,
                    new Color(0.92f, 0.94f, 0.98f, 0.95f),
                    TextAnchor.MiddleCenter,
                    raycastTarget: false,
                    name: "Index");
                DetailStripApplyFont(_detailStripThumbScrubIndexText, s, GalleryUiDesignTokens.FontBodyRef);
                _detailStripThumbScrubIndexGO.SetActive(false);
                _detailStripThumbScrubIndexVisible = false;
            }
            else if (_detailStripThumbScrubIndexText == null)
            {
                Transform t = _detailStripThumbScrubIndexGO.transform.Find("Index");
                if (t != null) _detailStripThumbScrubIndexText = t.GetComponent<Text>();
            }

            // Keep overlays above preview image for raycasts / draw order.
            if (_detailStripThumbPrevBtnGO != null) _detailStripThumbPrevBtnGO.transform.SetAsLastSibling();
            if (_detailStripThumbNextBtnGO != null) _detailStripThumbNextBtnGO.transform.SetAsLastSibling();
            if (_detailStripThumbScrubIndexGO != null) _detailStripThumbScrubIndexGO.transform.SetAsLastSibling();

            DetailStripLayoutThumbNavOverlay(s);
        }

        private GameObject DetailStripCreateThumbNavButton(
            GameObject thumbCol, string name, int anchorPreset, Sprite icon, UnityEngine.Events.UnityAction onClick)
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float btnSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float inset = GalleryUiDesignTokens.FooterDetailStripThumbNavInsetRef * s;
            float x = anchorPreset == AnchorPresets.middleLeft ? inset : -inset;

            GameObject go = UI.AddChildGOImage(
                thumbCol, DetailStripThumbNavBackdrop, anchorPreset, btnSz, btnSz, new Vector2(x, 0f), rounded: true);
            go.name = name;
            Button btn = go.AddComponent<Button>();
            UI.ConfigButtonFlat(btn, applyColors: true);
            if (onClick != null) btn.onClick.AddListener(onClick);
            go.AddComponent<UIHoverBorder>();
            if (icon != null)
                UI.AddIconToButton(go, icon, padding: Mathf.Max(3f, 4f * s), backdropOverride: DetailStripThumbNavBackdrop);
            else
            {
                Text label = go.GetComponentInChildren<Text>(true);
                if (label == null)
                    label = UI.CreateLabel(go, name == "NavPrev" ? "◀" : "▶", GalleryUiDesignTokens.FontBodyRef,
                        DetailStripThumbNavGlyph, TextAnchor.MiddleCenter, name: "Text");
                else
                {
                    label.gameObject.SetActive(true);
                    label.text = name == "NavPrev" ? "◀" : "▶";
                    label.color = DetailStripThumbNavGlyph;
                }
                DetailStripApplyFont(label, s, GalleryUiDesignTokens.FontBodyRef);
            }
            DetailStripApplyThumbNavGlyphTint(go);
            return go;
        }

        private static void DetailStripApplyThumbNavGlyphTint(GameObject go)
        {
            if (go == null) return;
            Transform iconTr = go.transform.Find("Icon");
            if (iconTr != null)
            {
                Image iconImg = iconTr.GetComponent<Image>();
                if (iconImg != null) iconImg.color = DetailStripThumbNavGlyph;
            }
            Text label = go.GetComponentInChildren<Text>(true);
            if (label != null && label.gameObject.activeSelf)
                label.color = DetailStripThumbNavGlyph;
        }

        private void DetailStripLayoutThumbNavOverlay(float s)
        {
            if (s <= 0f) s = 1f;
            // Match gallery chrome button size — never shrink with thumb fraction.
            float btnSz = GalleryUiDesignTokens.ButtonSizeRef * s;
            float inset = GalleryUiDesignTokens.FooterDetailStripThumbNavInsetRef * s;

            DetailStripLayoutThumbNavButton(_detailStripThumbPrevBtnGO, AnchorPresets.middleLeft, btnSz, inset);
            DetailStripLayoutThumbNavButton(_detailStripThumbNextBtnGO, AnchorPresets.middleRight, btnSz, -inset);

            if (_detailStripThumbScrubIndexGO != null)
            {
                RectTransform rt = _detailStripThumbScrubIndexGO.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float indexH = GalleryUiDesignTokens.ButtonSizeRef * s;
                    // Wide enough for compact "999K/999K" at body font.
                    float indexW = Mathf.Max(72f * s, btnSz * 2.4f);
                    rt.sizeDelta = new Vector2(indexW, indexH);
                    rt.anchoredPosition = new Vector2(0f, inset);
                }
                if (_detailStripThumbScrubIndexText != null)
                    DetailStripApplyFont(_detailStripThumbScrubIndexText, s, GalleryUiDesignTokens.FontBodyRef);
            }
        }

        private static void DetailStripLayoutThumbNavButton(GameObject go, int anchorPreset, float btnSz, float x)
        {
            if (go == null) return;
            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = AnchorPresets.GetAnchorMin(anchorPreset);
            rt.anchorMax = AnchorPresets.GetAnchorMax(anchorPreset);
            rt.pivot = AnchorPresets.GetPivot(anchorPreset);
            rt.sizeDelta = new Vector2(btnSz, btnSz);
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        /// <summary>Enable/disable ◀▶ at list ends. No alloc.</summary>
        private void DetailStripSyncThumbNavChrome()
        {
            int count = currentFilteredFiles != null ? currentFilteredFiles.Count : 0;
            int idx = _detailStripScrubIndex;
            if (idx < 0 || (count > 0 && idx >= count))
            {
                bool historyBrowse = activeContentType == ContentType.History;
                string navKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse);
                if (string.IsNullOrEmpty(navKey) && selectedFiles != null && selectedFiles.Count > 0)
                    navKey = GetSelectionIdentityKey(selectedFiles[0], historyBrowse);
                if (!string.IsNullOrEmpty(navKey) && currentFilteredFiles != null)
                    idx = FindIndexBySelectionIdentity(currentFilteredFiles, navKey, historyBrowse);
            }

            bool canPrev = count > 1 && idx > 0;
            bool canNext = count > 1 && idx >= 0 && idx < count - 1;
            // Unknown index: allow both so first click resolves via scrub path.
            if (count > 1 && idx < 0)
            {
                canPrev = true;
                canNext = true;
            }

            DetailStripSetThumbNavEnabled(_detailStripThumbPrevBtn, _detailStripThumbPrevBtnImage, canPrev);
            DetailStripSetThumbNavEnabled(_detailStripThumbNextBtn, _detailStripThumbNextBtnImage, canNext);
        }

        private static void DetailStripSetThumbNavEnabled(Button btn, Image img, bool enabled)
        {
            if (btn == null) return;
            if (btn.interactable != enabled)
                btn.interactable = enabled;

            // ColorTint is Transition.None — disabledColor never reaches Icon child.
            // CanvasGroup dims backdrop + glyph + hover rim as one unit.
            CanvasGroup cg = btn.GetComponent<CanvasGroup>();
            if (cg == null) cg = btn.gameObject.AddComponent<CanvasGroup>();
            float a = enabled ? 1f : DetailStripThumbNavDisabledAlpha;
            if (Mathf.Abs(cg.alpha - a) > 0.001f)
                cg.alpha = a;

            if (img != null && img.color != DetailStripThumbNavBackdrop)
                img.color = DetailStripThumbNavBackdrop;
            DetailStripApplyThumbNavGlyphTint(btn.gameObject);

            UIHoverBorder hb = btn.GetComponent<UIHoverBorder>();
            if (hb != null) hb.SyncIndicatorVisibility();
        }

        /// <summary>
        /// Show n/N on thumb while scrubbing. Text rebuild only when index/count changes
        /// (warm path — not per-frame).
        /// </summary>
        private void DetailStripSyncScrubIndexOverlay()
        {
            if (_detailStripThumbScrubIndexGO == null) return;

            bool want = (_detailStripScrubActive || _detailStripScrubHeightLocked)
                && currentFilteredFiles != null
                && currentFilteredFiles.Count > 0
                && DetailStripIsExpanded();

            if (!want)
            {
                if (_detailStripThumbScrubIndexVisible)
                {
                    _detailStripThumbScrubIndexGO.SetActive(false);
                    _detailStripThumbScrubIndexVisible = false;
                }
                _detailStripThumbScrubIndexShown = int.MinValue;
                _detailStripThumbScrubCountShown = int.MinValue;
                return;
            }

            int count = currentFilteredFiles.Count;
            int idx = _detailStripScrubIndex;
            if (idx < 0 || idx >= count) idx = 0;
            int display = idx + 1;

            if (!_detailStripThumbScrubIndexVisible)
            {
                _detailStripThumbScrubIndexGO.SetActive(true);
                _detailStripThumbScrubIndexVisible = true;
            }

            if (display == _detailStripThumbScrubIndexShown && count == _detailStripThumbScrubCountShown)
                return;
            _detailStripThumbScrubIndexShown = display;
            _detailStripThumbScrubCountShown = count;
            if (_detailStripThumbScrubIndexText != null)
                _detailStripThumbScrubIndexText.text =
                    FormatCompactCount(display) + "/" + FormatCompactCount(count);
        }

        private void DetailStripApplyStarRating(int next)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            next = Mathf.Clamp(next, 0, 5);

            int applied = 0;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                try
                {
                    if (RatingsManager.Instance != null)
                    {
                        RatingsManager.Instance.SetRating(f, next);
                        applied++;
                    }
                }
                catch { }
            }
            if (applied == 0) return;

            _detailStripStarRating = next;
            _detailStripStarHover = 0;
            DetailStripPaintStars();
            try
            {
                if (tboxGridRateHandler != null)
                    tboxGridRateHandler.SetDisplayOnly(next);
            }
            catch { }
            try { TboxAfterGridRateChanged(); } catch { }
            // Cache key excludes rating — no remount. Stars already painted.
        }

        private static void DetailStripUnbindClick(GameObject go)
        {
            if (go == null) return;
            EventTrigger et = go.GetComponent<EventTrigger>();
            if (et != null) et.triggers.Clear();
        }

        private void DetailStripLayout()
        {
            if (_detailStripRT == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            bool scaleChanged = Mathf.Abs(_detailStripLayoutScale - s) > 0.001f;
            // Stale absolute px from prior scale → black gutters / clip after UI-scale change.
            if (scaleChanged && !_detailStripScrubHeightLocked)
            {
                _detailStripMeasuredHeight = -1f;
                DetailStripInvalidateAutoHeightLock();
                DetailStripResetStackSideDecision();
            }

            float rowH = DetailStripRowHeight(s);

            float topInset = (tboxExpandT > 0.05f) ? TryOnToolboxReservedHeight() : 0f;
            _detailStripRT.anchorMin = new Vector2(0f, 1f);
            _detailStripRT.anchorMax = new Vector2(1f, 1f);
            _detailStripRT.pivot = new Vector2(0.5f, 1f);
            _detailStripRT.anchoredPosition = new Vector2(0f, -topInset);
            _detailStripRT.sizeDelta = new Vector2(0f, rowH);
            // During scrub lock: never re-drive thumb LE/anchors (HLG fight = vertical bounce).
            if (!_detailStripScrubHeightLocked)
                DetailStripSyncThumbSize(s, rowH);

            if (!scaleChanged) return;
            _detailStripLayoutScale = s;
            DetailStripApplyChromeScale(s);

            // Remeasure after chrome rescale (same selection would otherwise skip Refresh).
            if (!_detailStripInRefreshGeometry
                && _detailStripGO != null && _detailStripGO.activeSelf && !DetailStripScrubBlocksRebuild)
                DetailStripRefreshGeometry();
        }

        /// <summary>Re-apply every scale-dependent strip metric so ChromeScale stays consistent.</summary>
        private void DetailStripApplyChromeScale(float s)
        {
            if (s <= 0f) s = 1f;
            float lineH = DetailStripLineHeight(s);
            float hitH = DetailStripHitHeight(s);
            RectOffset zeroPad = UI.Pad(0, 0, 0, 0, s);

            DetailStripSetLayoutGroup(_detailStripGO, 8f * s, UI.Pad(0, 0, 0, 0, s));

            if (_detailStripThumbGO != null)
            {
                Transform imgTr = _detailStripThumbGO.transform.Find("Image");
                RectTransform imgRT = imgTr as RectTransform;
                if (imgRT != null)
                {
                    imgRT.offsetMin = Vector2.zero;
                    imgRT.offsetMax = Vector2.zero;
                }
            }

            Transform textColTr = _detailStripGO != null ? _detailStripGO.transform.Find("TextCol") : null;
            if (textColTr != null)
            {
                DetailStripApplyTextColPadForThumbSide(s);
                LayoutElement textColLe = textColTr.GetComponent<LayoutElement>();
                if (textColLe != null)
                {
                    textColLe.minWidth = 80f * s;
                    textColLe.flexibleHeight = 0f;
                }
            }

            try { DetailStripSyncResizeGripChrome(s); } catch { }
            try { DetailStripSyncThumbSide(); } catch { }

            if (_detailStripSideColGO != null)
            {
                DetailStripSetLayoutGroup(_detailStripSideColGO, DetailStripBandGap(s), UI.Pad(10, 8, 6, 6, s));
                float sideMin = GalleryUiDesignTokens.FooterDetailStripSideMinColWidthRef * s;
                if (_detailStripSideColLE != null)
                {
                    _detailStripSideColLE.minWidth = sideMin;
                    if (_detailStripSideColLE.preferredWidth < sideMin)
                        _detailStripSideColLE.preferredWidth = sideMin;
                    _detailStripSideColLE.flexibleWidth = 0f;
                    _detailStripSideColLE.flexibleHeight = 0f;
                }
            }
            DetailStripApplyFont(_detailStripSideDesc, s);
            DetailStripApplyFont(_detailStripSideNativeTags, s);
            DetailStripSyncSideBlockChrome(_detailStripSideNativeTags, null, s, lineH, wrapBlock: true);
            if (_detailStripSideDescScrollRect != null)
                _detailStripSideDescScrollRect.scrollSensitivity = 40f * s;
            if (_detailStripSideDescScrollGO != null)
            {
                Transform vpTr = _detailStripSideDescScrollGO.transform.Find("Viewport");
                RectTransform vpRT = vpTr as RectTransform;
                if (vpRT != null)
                {
                    float sbW = GalleryUiDesignTokens.FooterDetailStripSideScrollBarWidthRef * s;
                    vpRT.offsetMax = new Vector2(-sbW, 0f);
                }
                Transform sbTr = _detailStripSideDescScrollGO.transform.Find("Scrollbar");
                RectTransform sbRT = sbTr as RectTransform;
                if (sbRT != null)
                {
                    float sbW = GalleryUiDesignTokens.FooterDetailStripSideScrollBarWidthRef * s;
                    sbRT.sizeDelta = new Vector2(sbW, sbRT.sizeDelta.y);
                }
            }

            DetailStripSetRowHeight(_detailStripTitleRowGO, lineH);
            DetailStripSetLayoutGroup(_detailStripTitleRowGO, 6f * s, zeroPad);
            LayoutElement titleRowLe = _detailStripTitleRowGO != null
                ? _detailStripTitleRowGO.GetComponent<LayoutElement>() : null;
            if (titleRowLe != null) titleRowLe.flexibleHeight = 0f;
            if (_detailStripTitle != null)
            {
                DetailStripApplyFont(_detailStripTitle, s);
                LayoutElement titleLe = _detailStripTitle.GetComponent<LayoutElement>();
                if (titleLe != null)
                {
                    titleLe.minWidth = 0f;
                    titleLe.preferredWidth = 0f;
                    titleLe.flexibleWidth = 1f;
                    titleLe.preferredHeight = lineH;
                    titleLe.minHeight = lineH;
                    titleLe.flexibleHeight = 0f;
                }
            }

            if (_detailStripBadgeRowGO != null)
            {
                DetailStripSetRowHeight(_detailStripBadgeRowGO, lineH);
                DetailStripSetLayoutGroup(_detailStripBadgeRowGO, 3f * s, zeroPad);
                RectTransform badgeRT = _detailStripBadgeRowGO.GetComponent<RectTransform>();
                if (badgeRT != null)
                    badgeRT.sizeDelta = new Vector2(80f * s, lineH);
            }
            DetailStripSyncBadgeChrome(_detailStripBadgeAuto, s);
            DetailStripSyncBadgeChrome(_detailStripBadgeHide, s);
            DetailStripSyncBadgeChrome(_detailStripBadgeScan, s);
            DetailStripSyncBadgeChrome(_detailStripBadgeTags, s);
            DetailStripSyncStarsChrome(s, lineH);
            DetailStripSyncCollapseButtonChrome(s, lineH);
            DetailStripSyncExpandButtonChrome(s);

            DetailStripSetLayoutGroup(_detailStripMetaHost, DetailStripBandGap(s), zeroPad);
            if (_detailStripMetaRows != null)
            {
                for (int ri = 0; ri < _detailStripMetaRows.Length; ri++)
                {
                    GameObject row = _detailStripMetaRows[ri];
                    if (row == null) continue;
                    DetailStripSetRowHeight(row, hitH);
                    DetailStripSetLayoutGroup(row, 8f * s, zeroPad);
                    LayoutElement rowLe = row.GetComponent<LayoutElement>();
                    if (rowLe != null) rowLe.flexibleHeight = 0f;
                    ContentSizeFitter[] csfs = row.GetComponentsInChildren<ContentSizeFitter>(true);
                    for (int ci = 0; ci < csfs.Length; ci++)
                    {
                        if (csfs[ci] != null)
                            csfs[ci].verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                    }
                    Text[] msgs = row.GetComponentsInChildren<Text>(true);
                    for (int ti = 0; ti < msgs.Length; ti++)
                    {
                        Text msg = msgs[ti];
                        if (msg == null) continue;
                        DetailStripApplyFont(msg, s);
                        LayoutElement le = msg.GetComponent<LayoutElement>();
                        if (le == null) continue;
                        le.minHeight = hitH;
                        le.preferredHeight = hitH;
                        le.flexibleHeight = 0f;
                    }
                }
            }
            DetailStripSyncMetaHostHeight(s);

            DetailStripSetLayoutGroup(_detailStripActionsRowGO, DetailStripBandGap(s), zeroPad);
            LayoutElement actionsLe = _detailStripActionsRowGO != null
                ? _detailStripActionsRowGO.GetComponent<LayoutElement>() : null;
            if (actionsLe != null) actionsLe.flexibleHeight = 0f;
            if (_detailStripActionRows != null)
            {
                for (int ari = 0; ari < _detailStripActionRows.Length; ari++)
                {
                    GameObject arow = _detailStripActionRows[ari];
                    if (arow == null) continue;
                    DetailStripSetRowHeight(arow, hitH);
                    DetailStripSetLayoutGroup(arow, 8f * s, zeroPad);
                }
            }

            DetailStripSyncLinkChrome(_detailStripLoadLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripHubLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripCopyLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripDeleteLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripCacheLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripAutoLoadLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripNoAutoLoadLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripHideLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripUnhideLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripTempWlLink, s, hitH);
            DetailStripSyncLinkChrome(_detailStripOldVersLink, s, hitH);
            DetailStripApplyFont(_detailStripDesc, s);
            DetailStripApplyFont(_detailStripPackageTags, s);
            DetailStripApplyFont(_detailStripTags, s);
            DetailStripApplyFont(_detailStripPath, s);

            // Desc height/wrap depends on tall-stack vs side — not a fixed single line.
            try { DetailStripApplyDescPlacement(); } catch { }
            try { DetailStripApplyPackageTagsPlacement(); } catch { }
            DetailStripSyncTagsRowChrome(lineH, s);
            DetailStripSyncFlexLineChrome(_detailStripPath, lineH, s);
            DetailStripSyncFlexLineChrome(_detailStripPackageTags, lineH, s);

            // After pad/spacing/font rescale — clear stale horizontal insets on every band.
            DetailStripNormalizeTextColRows();
            DetailStripSyncActionsHostHeight(s);
            DetailStripRebuildTextColLayout();
        }

        private static void DetailStripSyncSideBlockChrome(
            Text block, LayoutElement le, float s, float lineH, bool wrapBlock)
        {
            if (block == null) return;
            DetailStripApplyFont(block, s);
            if (le == null) le = block.GetComponent<LayoutElement>();
            if (le == null) return;
            le.minHeight = lineH;
            if (wrapBlock)
            {
                int maxLines = GalleryUiDesignTokens.FooterDetailStripSideTagsMaxLines;
                if (maxLines < 1) maxLines = 1;
                if (le.preferredHeight < lineH)
                    le.preferredHeight = lineH * maxLines;
            }
            else
            {
                le.preferredHeight = lineH;
            }
            le.flexibleHeight = 0f;
            le.flexibleWidth = 1f;
            le.minWidth = 0f;
            le.preferredWidth = 0f;
        }

        private static void DetailStripSyncLinkChrome(Text link, float s, float lineH)
        {
            if (link == null) return;
            DetailStripApplyFont(link, s);
            DetailStripDisableVerticalCsf(link);
            LayoutElement le = link.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minHeight = lineH;
                le.preferredHeight = lineH;
                le.flexibleHeight = 0f;
            }
        }

        private static void DetailStripSyncFlexLineChrome(Text line, float lineH, float s)
        {
            if (line == null || line.transform.parent == null) return;
            GameObject row = line.transform.parent.gameObject;
            DetailStripSetRowHeight(row, lineH);
            DetailStripSetLayoutGroup(row, 0f, UI.Pad(0, 0, 0, 0, s));
            LayoutElement rowLe = row.GetComponent<LayoutElement>();
            if (rowLe != null)
            {
                rowLe.flexibleHeight = 0f;
                rowLe.minWidth = 0f;
                rowLe.flexibleWidth = 1f;
            }
            LayoutElement textLe = line.GetComponent<LayoutElement>();
            if (textLe != null)
            {
                textLe.preferredHeight = lineH;
                textLe.minHeight = lineH;
                textLe.flexibleHeight = 0f;
                textLe.minWidth = 0f;
                textLe.preferredWidth = 0f;
                textLe.flexibleWidth = 1f;
            }
            DetailStripNormalizeRowRect(row);
        }

        private void DetailStripSyncTagsRowChrome(float lineH, float s)
        {
            if (_detailStripTags == null || _detailStripTags.transform.parent == null) return;
            if (s <= 0f) s = 1f;
            GameObject row = _detailStripTags.transform.parent.gameObject;
            DetailStripSetRowHeight(row, lineH);
            DetailStripSetLayoutGroup(row, 4f * s, UI.Pad(0, 0, 0, 0, s));
            LayoutElement rowLe = row.GetComponent<LayoutElement>();
            if (rowLe != null)
            {
                rowLe.flexibleHeight = 0f;
                rowLe.minWidth = 0f;
                rowLe.flexibleWidth = 1f;
            }

            float hitH = DetailStripHitHeight(s);
            DetailStripApplyFont(_detailStripTags, s);
            _detailStripTags.text = VPBTranslation.T("gallery.detail.set_tags", "Set Tags: ");
            LayoutElement setLe = _detailStripTags.GetComponent<LayoutElement>();
            if (setLe != null)
            {
                setLe.preferredHeight = hitH;
                setLe.minHeight = hitH;
                setLe.flexibleHeight = 0f;
                setLe.flexibleWidth = 0f;
            }

            if (_detailStripTagsChipsHost != null)
            {
                DetailStripSetLayoutGroup(_detailStripTagsChipsHost, 0f, UI.Pad(0, 0, 0, 0, s));
                LayoutElement hostLe = _detailStripTagsChipsHost.GetComponent<LayoutElement>();
                if (hostLe != null)
                {
                    hostLe.preferredHeight = lineH;
                    hostLe.minHeight = lineH;
                    hostLe.flexibleHeight = 0f;
                    hostLe.flexibleWidth = 1f;
                    hostLe.minWidth = 0f;
                    hostLe.preferredWidth = 0f;
                }
                Text[] chips = _detailStripTagsChipsHost.GetComponentsInChildren<Text>(true);
                for (int i = 0; i < chips.Length; i++)
                {
                    Text chip = chips[i];
                    if (chip == null) continue;
                    DetailStripApplyFont(chip, s);
                    LayoutElement chipLe = chip.GetComponent<LayoutElement>();
                    if (chipLe == null) continue;
                    float pw = Mathf.Max(chip.preferredWidth, 4f * s);
                    chipLe.preferredWidth = pw;
                    chipLe.minWidth = pw;
                    chipLe.preferredHeight = hitH;
                    chipLe.minHeight = hitH;
                    chipLe.flexibleHeight = 0f;
                    chipLe.flexibleWidth = 0f;
                }
            }
            DetailStripNormalizeRowRect(row);
        }

        private void DetailStripSyncBadgeChrome(GameObject badge, float s)
        {
            if (badge == null) return;
            float sz = 18f * s;
            LayoutElement le = badge.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = sz;
                le.preferredWidth = sz;
                le.minHeight = sz;
                le.preferredHeight = sz;
            }
            RectTransform rt = badge.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(sz, sz);
            Text t = badge.GetComponentInChildren<Text>(true);
            DetailStripApplyFont(t, s, GalleryUiDesignTokens.FontMinRef);
        }

        private void DetailStripSyncStarsChrome(float s, float lineH)
        {
            if (_detailStripStarsGO == null) return;
            float starSz = 14f * s;
            float gap = 2f * s;
            float rowW = starSz * 5f + gap * 4f;
            LayoutElement le = _detailStripStarsGO.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = rowW;
                le.preferredWidth = rowW;
                le.preferredHeight = lineH;
                le.minHeight = lineH;
            }
            RectTransform starsRT = _detailStripStarsGO.GetComponent<RectTransform>();
            if (starsRT != null) starsRT.sizeDelta = new Vector2(rowW, lineH);
            DetailStripSetLayoutGroup(_detailStripStarsGO, gap, UI.Pad(0, 0, 0, 0, s));
            if (_detailStripStarImages == null) return;
            for (int i = 0; i < _detailStripStarImages.Length; i++)
            {
                Image img = _detailStripStarImages[i];
                if (img == null) continue;
                GameObject starGO = img.gameObject;
                LayoutElement starLe = starGO.GetComponent<LayoutElement>();
                if (starLe != null)
                {
                    starLe.minWidth = starSz;
                    starLe.preferredWidth = starSz;
                    starLe.minHeight = starSz;
                    starLe.preferredHeight = starSz;
                }
                RectTransform srt = starGO.GetComponent<RectTransform>();
                if (srt != null) srt.sizeDelta = new Vector2(starSz, starSz);
            }
        }

        /// <summary>Called from gallery UI-scale path so strip tracks pane × host at any factor.</summary>
        internal void DetailStripRescaleForUiScale(float s)
        {
            if (s <= 0f) s = 1f;
            try { DetailStripSyncExpandButtonChrome(s); } catch { }
            if (_detailStripGO == null) return;
            _detailStripLayoutScale = -1f;
            if (!_detailStripScrubHeightLocked)
            {
                _detailStripMeasuredHeight = -1f;
                DetailStripInvalidateAutoHeightLock();
                DetailStripResetStackSideDecision();
            }
            DetailStripLayout();
        }

        private static float DetailStripThumbEdge(float s, float stripH)
        {
            if (s <= 0f) s = 1f;
            // Always match strip height — clamping below stripH left a black gap under preview.
            float minEdge = DetailStripHardMinHeight(s);
            if (stripH > 1f) return Mathf.Max(minEdge, stripH);
            return minEdge;
        }

        private static bool DetailStripStripForcesChildHeight(GameObject strip)
        {
            if (strip == null) return false;
            HorizontalLayoutGroup hlg = strip.GetComponent<HorizontalLayoutGroup>();
            return hlg != null && hlg.childForceExpandHeight;
        }

        private static bool DetailStripStripNeedsUpperLeftAlign(GameObject strip)
        {
            if (strip == null) return false;
            HorizontalLayoutGroup hlg = strip.GetComponent<HorizontalLayoutGroup>();
            return hlg != null && hlg.childAlignment != TextAnchor.UpperLeft;
        }

        private static bool DetailStripStripHasLegacyOuterPad(GameObject strip)
        {
            if (strip == null) return false;
            HorizontalLayoutGroup hlg = strip.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) return false;
            RectOffset p = hlg.padding;
            return p != null && (p.left > 0 || p.top > 0 || p.bottom > 0 || p.right > 0);
        }

        private static bool DetailStripTextColMissingMask(GameObject strip)
        {
            if (strip == null) return false;
            Transform t = strip.transform.Find("TextCol");
            return t != null && t.GetComponent<RectMask2D>() == null;
        }

        /// <summary>Direct-child only (legacy flat action row). Do not use for Link_Hub under ActionRow0.</summary>
        private bool DetailStripActionsHostHasLegacyDirectChild(string childName)
        {
            if (_detailStripActionsRowGO == null || string.IsNullOrEmpty(childName)) return false;
            Transform t = _detailStripActionsRowGO.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c != null && c.name == childName) return true;
            }
            return false;
        }

        private void DetailStripSyncThumbSize(float s, float stripH)
        {
            GameObject thumbCol = _detailStripThumbColGO;
            if (thumbCol == null && _detailStripGO != null)
            {
                Transform t = _detailStripGO.transform.Find("ThumbCol");
                if (t != null) thumbCol = t.gameObject;
            }
            if (thumbCol == null) return;
            // Prefer live strip rect so preview stays flush after layout.
            float liveH = stripH;
            try
            {
                if (_detailStripRT != null && _detailStripRT.rect.height > 8f)
                    liveH = _detailStripRT.rect.height;
            }
            catch { }
            float thumbSize = DetailStripThumbEdge(s, liveH);
            LayoutElement le = thumbCol.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minWidth = thumbSize;
                le.preferredWidth = thumbSize;
                le.minHeight = thumbSize;
                le.preferredHeight = thumbSize;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 0f;
            }
            // Keep RectTransform square too — HLG alone can leave stale tall sizeDelta.
            RectTransform rt = thumbCol.GetComponent<RectTransform>();
            if (rt != null)
                rt.sizeDelta = new Vector2(thumbSize, thumbSize);
            DetailStripEnsureThumbNavOverlay(thumbCol);
            DetailStripSyncThumbNavChrome();
        }

        private void DetailStripApplyTextColPadForThumbSide(float s)
        {
            if (s <= 0f) s = 1f;
            Transform textColTr = _detailStripGO != null ? _detailStripGO.transform.Find("TextCol") : null;
            if (textColTr == null) return;
            // Extra pad on the thumb seam so collapse hover rim is not clipped.
            RectOffset pad = DetailStripThumbOnRight()
                ? UI.Pad(8, 2, 4, 4, s)
                : UI.Pad(2, 8, 4, 4, s);
            DetailStripSetLayoutGroup(textColTr.gameObject, DetailStripBandGap(s), pad);
        }

        /// <summary>HLG order: left = Thumb|Text|Side; right = Text|Side|Thumb. Grip ignores layout.</summary>
        private void DetailStripSyncThumbSide()
        {
            if (_detailStripGO == null || _detailStripThumbColGO == null) return;
            Transform strip = _detailStripGO.transform;
            Transform thumb = _detailStripThumbColGO.transform;
            Transform textCol = strip.Find("TextCol");
            Transform sideCol = _detailStripSideColGO != null
                ? _detailStripSideColGO.transform
                : strip.Find("SideCol");

            if (DetailStripThumbOnRight())
            {
                if (textCol != null) textCol.SetSiblingIndex(0);
                if (sideCol != null) sideCol.SetSiblingIndex(1);
                thumb.SetSiblingIndex(strip.childCount - 1);
            }
            else
            {
                thumb.SetSiblingIndex(0);
                if (textCol != null) textCol.SetSiblingIndex(1);
                if (sideCol != null) sideCol.SetSiblingIndex(2);
            }

            // Keep overlay grip last so it stays above HLG children for raycasts.
            if (_detailStripResizeGripGO != null)
                _detailStripResizeGripGO.transform.SetAsLastSibling();

            DetailStripApplyTextColPadForThumbSide(ChromeScale > 0f ? ChromeScale : 1f);
        }

        private static readonly Color DetailStripResizeGripBgNormal = new Color(
            GalleryUiColorTokens.SurfaceDark.r, GalleryUiColorTokens.SurfaceDark.g, GalleryUiColorTokens.SurfaceDark.b, 0.94f);
        private static readonly Color DetailStripResizeGripBgHover = new Color(
            GalleryUiColorTokens.SurfaceMid.r, GalleryUiColorTokens.SurfaceMid.g, GalleryUiColorTokens.SurfaceMid.b, 0.98f);
        private static readonly Color DetailStripResizeGripHandleNormal = new Color(
            GalleryUiColorTokens.TextMuted.r, GalleryUiColorTokens.TextMuted.g, GalleryUiColorTokens.TextMuted.b, 0.92f);
        private static readonly Color DetailStripResizeGripHandleHover = GalleryUiColorTokens.TextPrimary;

        /// <summary>True when grip is legacy (over title / tiny / missing Handle).</summary>
        private static bool DetailStripResizeGripNeedsRebuild(GameObject grip)
        {
            if (grip == null) return true;
            RectTransform rt = grip.GetComponent<RectTransform>();
            if (rt == null) return true;
            if (rt.pivot.y > 0.5f) return true;
            if (grip.transform.Find("Handle") == null) return true;
            return false;
        }

        private void DetailStripEnsureResizeGrip()
        {
            if (_detailStripGO == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            if (_detailStripResizeGripGO == null)
            {
                Transform existing = _detailStripGO.transform.Find("ResizeGrip");
                if (existing != null) _detailStripResizeGripGO = existing.gameObject;
            }

            if (_detailStripResizeGripGO != null && DetailStripResizeGripNeedsRebuild(_detailStripResizeGripGO))
            {
                try { UnityEngine.Object.Destroy(_detailStripResizeGripGO); } catch { }
                _detailStripResizeGripGO = null;
                _detailStripResizeGripBg = null;
                _detailStripResizeGripPill = null;
            }

            if (_detailStripResizeGripGO == null)
            {
                float gripH = GalleryUiDesignTokens.FooterDetailStripResizeGripRef * s;
                GameObject grip = UI.CreateChildRT(
                    _detailStripGO, "ResizeGrip", AnchorPresets.hStretchTop, new Vector2(0f, gripH));
                _detailStripResizeGripGO = grip;
                LayoutElement gripLe = UI.AddLE(grip, preferredHeight: gripH, minHeight: gripH,
                    flexibleWidth: 1f, flexibleHeight: 0f);
                if (gripLe != null) gripLe.ignoreLayout = true;

                // Full-width rail — easy to aim; sits above content (see Sync).
                _detailStripResizeGripBg = UI.AddImage(grip, DetailStripResizeGripBgNormal, raycastTarget: true);

                float handleW = GalleryUiDesignTokens.FooterDetailStripResizePillWRef * s;
                float handleH = GalleryUiDesignTokens.FooterDetailStripResizePillHRef * s;
                GameObject handle = UI.CreateChildRT(grip, "Handle", AnchorPresets.middleCenter, new Vector2(handleW, handleH));
                // Always bright — grip Bg owns raycasts; handle is the visual grab cue.
                _detailStripResizeGripPill = UI.AddImage(handle, DetailStripResizeGripHandleNormal, raycastTarget: false);

                // Twin dashes = classic “grab” cue (readable at a glance).
                float dashW = handleW * 0.55f;
                float dashH = Mathf.Max(1.5f, 1.5f * s);
                float dashGap = 1.5f * s;
                GameObject dashA = UI.CreateChildRT(handle, "DashA", AnchorPresets.middleCenter, new Vector2(dashW, dashH));
                UI.AddImage(dashA, new Color(0.10f, 0.11f, 0.14f, 0.75f), raycastTarget: false);
                RectTransform dashART = dashA.GetComponent<RectTransform>();
                if (dashART != null) dashART.anchoredPosition = new Vector2(0f, dashGap);
                GameObject dashB = UI.CreateChildRT(handle, "DashB", AnchorPresets.middleCenter, new Vector2(dashW, dashH));
                UI.AddImage(dashB, new Color(0.10f, 0.11f, 0.14f, 0.75f), raycastTarget: false);
                RectTransform dashBRT = dashB.GetComponent<RectTransform>();
                if (dashBRT != null) dashBRT.anchoredPosition = new Vector2(0f, -dashGap);

                DetailStripHeightDragRelay drag = grip.AddComponent<DetailStripHeightDragRelay>();
                drag.OnBegin = DetailStripOnResizeBegin;
                drag.OnMove = DetailStripOnResizeDrag;
                drag.OnEnd = DetailStripOnResizeEnd;

                // Full-width rail + handle light up on hover (two hover comps, same GO).
                UIHoverColor railHover = grip.AddComponent<UIHoverColor>();
                railHover.targetImage = _detailStripResizeGripBg;
                railHover.normalColor = DetailStripResizeGripBgNormal;
                railHover.hoverColor = DetailStripResizeGripBgHover;
                UIHoverColor handleHover = grip.AddComponent<UIHoverColor>();
                handleHover.targetImage = _detailStripResizeGripPill;
                handleHover.normalColor = DetailStripResizeGripHandleNormal;
                handleHover.hoverColor = DetailStripResizeGripHandleHover;

                AddDynamicTooltip(grip, () => VPBTranslation.T(
                    "gallery.detail.tip.resize",
                    "Drag up/down to resize detail strip (preview stays square)."));
            }

            DetailStripSyncResizeGripChrome(s);
            _detailStripResizeGripGO.transform.SetAsLastSibling();
        }

        private void DetailStripSyncResizeGripChrome(float s)
        {
            if (s <= 0f) s = 1f;
            if (_detailStripResizeGripGO == null) return;
            float gripH = GalleryUiDesignTokens.FooterDetailStripResizeGripRef * s;
            RectTransform gripRT = _detailStripResizeGripGO.GetComponent<RectTransform>();
            if (gripRT != null)
            {
                // Pivot at bottom edge of grip → bar sits fully above strip top (no title clip).
                gripRT.anchorMin = new Vector2(0f, 1f);
                gripRT.anchorMax = new Vector2(1f, 1f);
                gripRT.pivot = new Vector2(0.5f, 0f);
                gripRT.sizeDelta = new Vector2(0f, gripH);
                gripRT.anchoredPosition = Vector2.zero;
            }
            LayoutElement gripLe = _detailStripResizeGripGO.GetComponent<LayoutElement>();
            if (gripLe != null)
            {
                gripLe.minHeight = gripH;
                gripLe.preferredHeight = gripH;
                gripLe.ignoreLayout = true;
            }
            // Do not reset Image colors here — layout/refresh calls this often and would
            // wipe UIHoverColor highlight while the pointer is still over the grip.
            if (_detailStripResizeGripPill != null)
            {
                RectTransform pillRT = _detailStripResizeGripPill.GetComponent<RectTransform>();
                if (pillRT != null)
                {
                    float handleW = GalleryUiDesignTokens.FooterDetailStripResizePillWRef * s;
                    float handleH = GalleryUiDesignTokens.FooterDetailStripResizePillHRef * s;
                    pillRT.sizeDelta = new Vector2(handleW, handleH);
                    float dashW = handleW * 0.55f;
                    float dashH = Mathf.Max(1.5f, 1.5f * s);
                    float dashGap = 1.5f * s;
                    Transform dashA = pillRT.Find("DashA");
                    Transform dashB = pillRT.Find("DashB");
                    if (dashA != null)
                    {
                        RectTransform dART = dashA as RectTransform;
                        if (dART != null)
                        {
                            dART.sizeDelta = new Vector2(dashW, dashH);
                            dART.anchoredPosition = new Vector2(0f, dashGap);
                        }
                    }
                    if (dashB != null)
                    {
                        RectTransform dBRT = dashB as RectTransform;
                        if (dBRT != null)
                        {
                            dBRT.sizeDelta = new Vector2(dashW, dashH);
                            dBRT.anchoredPosition = new Vector2(0f, -dashGap);
                        }
                    }
                }
            }
        }

        private void DetailStripOnResizeBegin(PointerEventData eventData)
        {
            if (eventData == null || _detailStripRT == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            _detailStripResizing = true;
            _detailStripResizeStartH = DetailStripRowHeight(s);
            RectTransform parent = _detailStripRT.parent as RectTransform;
            if (parent == null) parent = _detailStripRT;
            Camera cam = eventData.pressEventCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, cam, out _detailStripResizeStartLocal))
                _detailStripResizeStartLocal = Vector2.zero;

            // First drag locks auto height into a persisted user height (at least design min).
            if (VPBConfig.Instance != null && !DetailStripHasUserHeight())
            {
                float seed = Mathf.Max(_detailStripResizeStartH, DetailStripUserMinHeight(s));
                _detailStripResizeStartH = seed;
                VPBConfig.Instance.GalleryDetailStripHeightRef = seed / s;
            }
        }

        private void DetailStripOnResizeDrag(PointerEventData eventData)
        {
            if (!_detailStripResizing || eventData == null || _detailStripRT == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            RectTransform parent = _detailStripRT.parent as RectTransform;
            if (parent == null) parent = _detailStripRT;
            Camera cam = eventData.pressEventCamera;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, cam, out local))
                return;

            // Strip grows InfoBar upward into grid — mouse up increases height.
            float dy = local.y - _detailStripResizeStartLocal.y;
            float minH = DetailStripUserMinHeight(s);
            float maxH = DetailStripMaxHeight(s);
            float h = Mathf.Clamp(_detailStripResizeStartH + dy, minH, maxH);
            DetailStripApplyUserHeight(h, persist: true);
        }

        private void DetailStripOnResizeEnd()
        {
            if (!_detailStripResizing) return;
            _detailStripResizing = false;
            try { VPBConfig.Instance?.TriggerChange(); } catch { }
        }

        /// <summary>Set strip height from drag/settings; square thumb + overflow lines follow.</summary>
        private void DetailStripApplyUserHeight(float hScaled, bool persist)
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float minH = DetailStripUserMinHeight(s);
            float maxH = DetailStripMaxHeight(s);
            hScaled = Mathf.Clamp(hScaled, minH, maxH);
            if (persist && VPBConfig.Instance != null)
                VPBConfig.Instance.GalleryDetailStripHeightRef = hScaled / s;
            _detailStripMeasuredHeight = hScaled;
            // User height owns size — keep auto-lock in sync so clearing user height later is stable.
            _detailStripAutoHeightLock = hScaled;
            _detailStripAutoHeightLockKey = DetailStripSelectionLayoutKey() ?? "";
            // Side/stack placement before adapt — tall strip moves desc+package tags into rows.
            try { DetailStripSyncSideColumn(s, allowPlacementChange: true); } catch { }
            DetailStripAdaptContentToHeight(s, hScaled);
            if (!_detailStripScrubHeightLocked)
                DetailStripSyncThumbSize(s, hScaled);
            DetailStripLayout();
            try { DetailStripSyncSideColumn(s, allowPlacementChange: false); } catch { }
        }

        /// <summary>Re-apply preview side + height after settings change (no full rebuild).</summary>
        internal void DetailStripApplyLayoutPrefs()
        {
            if (_detailStripGO == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            try { DetailStripSyncThumbSide(); } catch { }
            if (DetailStripHasUserHeight())
                DetailStripApplyUserHeight(DetailStripUserHeightScaled(s), persist: false);
            else
            {
                _detailStripMeasuredHeight = -1f;
                DetailStripInvalidateAutoHeightLock();
                DetailStripResetStackSideDecision();
                try { DetailStripRefreshGeometry(); } catch { }
            }
        }

        /// <summary>Pack actions by width, measure content, resize strip (shorter when wide / fewer rows).</summary>
        private void DetailStripRefreshGeometry()
        {
            if (_detailStripInRefreshGeometry) return;
            _detailStripInRefreshGeometry = true;
            try
            {
                float s = ChromeScale;
                if (s <= 0f) s = 1f;
                try { DetailStripNormalizeTextColRows(); } catch { }
                // Side column first — packs + meta avail width subtract its column.
                try { DetailStripSyncSideColumn(s, allowPlacementChange: true); } catch { }
                try { DetailStripPackActionRows(s); } catch { }
                try { DetailStripSyncMetaHostHeight(s); } catch { }
                try { DetailStripSyncActionsHostHeight(s); } catch { }
                // Restore optional lines, then hide only if still over budget (never squash row heights).
                float budgetH = DetailStripGeometryBudgetHeight(s);
                DetailStripAdaptContentToHeight(s, budgetH);
                // Adapt may hide meta/action rows — re-pin host min/preferred before TextCol VLG runs.
                try { DetailStripSyncMetaHostHeight(s); } catch { }
                try { DetailStripSyncActionsHostHeight(s); } catch { }
                try { DetailStripRebuildTextColLayout(); } catch { }
                float h;
                if (DetailStripHasUserHeight())
                    h = DetailStripUserHeightScaled(s);
                else if (_detailStripScrubHeightLocked && _detailStripScrubLockedHeight > 8f)
                    h = _detailStripScrubLockedHeight;
                else if (DetailStripHasAutoHeightLock())
                    h = _detailStripAutoHeightLock;
                else
                {
                    h = DetailStripComputeContentHeight(s);
                    _detailStripAutoHeightLock = h;
                }
                // Scrub session: keep outer strip height stable (no tbox jump / layout thrash).
                if (_detailStripScrubHeightLocked && _detailStripScrubLockedHeight > 8f)
                    h = _detailStripScrubLockedHeight;
                _detailStripMeasuredHeight = h;
                try { DetailStripSyncThumbSide(); } catch { }
                DetailStripLayout();
                // Side desc viewport was sized with pre-measure height — refill to final strip edge.
                // Do not flip stack/side here (that reopens the height↔width feedback loop).
                try { DetailStripSyncSideColumn(s, allowPlacementChange: false); } catch { }
                // Pack/side sync can reintroduce horizontal drift — re-align once, then restack.
                try { DetailStripNormalizeTextColRows(); } catch { }
                try { DetailStripRebuildTextColLayout(); } catch { }
                // Side-rail span ignores detail-strip height (stable pane chrome) — no invalidate here.
            }
            finally
            {
                _detailStripInRefreshGeometry = false;
            }
        }

        private float DetailStripGeometryBudgetHeight(float s)
        {
            if (_detailStripScrubHeightLocked && _detailStripScrubLockedHeight > 8f)
                return _detailStripScrubLockedHeight;
            if (DetailStripHasUserHeight())
                return DetailStripUserHeightScaled(s);
            if (DetailStripHasAutoHeightLock())
                return _detailStripAutoHeightLock;
            return DetailStripMaxHeight(s);
        }

        /// <summary>
        /// If content exceeds budget: meta extras → package tags → shrink/ellipsis desc →
        /// trailing actions. Always keep title + action row 0 (Copy/Hub/…). Never squash
        /// protected row heights. User tags + path stay. Read-only prose yields first.
        /// </summary>
        private void DetailStripHideOverflowLines(float s, float maxH)
        {
            try
            {
                if (maxH < 8f) maxH = DetailStripMaxHeight(s);
                // Must use unclamped measure — clamped ComputeContentHeight always ≤ maxH,
                // so overflow never fired and multi-line desc painted over toolbox.
                float h = DetailStripComputeContentHeightRaw(s);
                if (h <= maxH + 0.5f) return;

                // Drop meta beyond the first 2 detail rows, then 2nd, then 1st.
                if (_detailStripMetaRows != null)
                {
                    for (int pass = 0; pass < 3; pass++)
                    {
                        int keep = pass == 0 ? 2 : (pass == 1 ? 1 : 0);
                        for (int r = _detailStripMetaRows.Length - 1; r >= keep; r--)
                        {
                            GameObject row = _detailStripMetaRows[r];
                            if (row == null || !row.activeSelf) continue;
                            row.SetActive(false);
                            DetailStripSyncMetaHostHeight(s);
                            h = DetailStripComputeContentHeightRaw(s);
                            if (h <= maxH + 0.5f) return;
                        }
                    }
                }

                // Drop read-only prose before burying Hide / Temp whitelist / Older versions.
                if (DetailStripFlexLineVisible(_detailStripPackageTags))
                {
                    DetailStripSetFlexLineActive(_detailStripPackageTags, false);
                    h = DetailStripComputeContentHeightRaw(s);
                    if (h <= maxH + 0.5f) return;
                }

                // Shrink desc lines with ellipsis before hiding — keep one useful line when possible.
                if (DetailStripFlexLineVisible(_detailStripDesc))
                {
                    for (int lines = DetailStripLeftDescCurrentLines(s); lines >= 1; lines--)
                    {
                        h = DetailStripComputeContentHeightRaw(s);
                        if (h <= maxH + 0.5f) return;
                        if (lines <= 1) break;
                        try { DetailStripSyncLeftDescContent(s, maxH, lines - 1); } catch { break; }
                    }
                    h = DetailStripComputeContentHeightRaw(s);
                    if (h <= maxH + 0.5f) return;
                    DetailStripSetFlexLineActive(_detailStripDesc, false);
                    h = DetailStripComputeContentHeightRaw(s);
                    if (h <= maxH + 0.5f) return;
                }

                // Drop trailing action rows only (never row 0).
                if (_detailStripActionRows != null)
                {
                    for (int r = _detailStripActionRows.Length - 1; r >= 1; r--)
                    {
                        GameObject row = _detailStripActionRows[r];
                        if (row == null || !row.activeSelf) continue;
                        row.SetActive(false);
                        DetailStripSyncActionsHostHeight(s);
                        h = DetailStripComputeContentHeightRaw(s);
                        if (h <= maxH + 0.5f) return;
                    }
                }

                // Keep Tags + Path when wanted — strip RectMask2D clips remainder.
                // Still over budget: accept clip. Never deactivate action row 0.
            }
            finally
            {
                DetailStripEnsureActionRow0Visible(s);
            }
        }

        /// <summary>Keep primary action band on when it still has visible links.</summary>
        private void DetailStripEnsureActionRow0Visible(float s)
        {
            if (_detailStripActionRows == null || _detailStripActionRows.Length == 0) return;
            GameObject row0 = _detailStripActionRows[0];
            if (row0 == null || !DetailStripRowHasVisibleChild(row0)) return;
            if (row0.activeSelf) return;
            row0.SetActive(true);
            DetailStripSyncActionsHostHeight(s);
        }

        private void DetailStripSyncActionsHostHeight(float s)
        {
            if (_detailStripActionsRowGO == null) return;
            if (s <= 0f) s = 1f;
            DetailStripEnsureHostHeightDrivers(_detailStripActionsRowGO);
            float hitH = DetailStripHitHeight(s);
            int actN = 0;
            if (_detailStripActionRows != null)
            {
                for (int i = 0; i < _detailStripActionRows.Length; i++)
                {
                    if (_detailStripActionRows[i] != null && _detailStripActionRows[i].activeSelf
                        && DetailStripRowHasVisibleChild(_detailStripActionRows[i]))
                        actN++;
                }
            }
            LayoutElement hostLe = _detailStripActionsRowGO.GetComponent<LayoutElement>();
            if (hostLe == null) return;
            if (actN <= 0)
            {
                hostLe.minHeight = 0f;
                hostLe.preferredHeight = 0f;
                hostLe.flexibleHeight = 0f;
                return;
            }
            float gap = DetailStripBandGap(s);
            float h = hitH * actN + gap * Mathf.Max(0, actN - 1);
            hostLe.minHeight = h;
            hostLe.preferredHeight = h;
            hostLe.flexibleHeight = 0f;
            hostLe.ignoreLayout = false;
        }

        /// <summary>Re-show packed action rows that still have links (after a prior height hide).</summary>
        private void DetailStripRestoreActionRowsForHeight(float s)
        {
            if (_detailStripActionRows == null) return;
            for (int r = 0; r < _detailStripActionRows.Length; r++)
            {
                GameObject row = _detailStripActionRows[r];
                if (row == null) continue;
                row.SetActive(DetailStripRowHasVisibleChild(row));
            }
            DetailStripSyncActionsHostHeight(s);
        }

        /// <summary>Re-show meta rows that still have chip children (after a prior height hide).</summary>
        private void DetailStripRestoreMetaRowsForHeight(float s)
        {
            if (_detailStripMetaRows == null) return;
            for (int r = 0; r < _detailStripMetaRows.Length; r++)
            {
                GameObject row = _detailStripMetaRows[r];
                if (row == null) continue;
                row.SetActive(row.transform.childCount > 0);
            }
            DetailStripSyncMetaHostHeight(s);
        }

        /// <summary>Restore optional bands then hide what no longer fits the height budget.</summary>
        private void DetailStripAdaptContentToHeight(float s, float budgetH)
        {
            try
            {
                DetailStripSetFlexLineActive(_detailStripPath, _detailStripWantPath);
                DetailStripSetFlexLineActive(_detailStripTags, _detailStripWantTags);
                DetailStripApplyDescPlacement();
                DetailStripApplyPackageTagsPlacement();
                DetailStripRestoreActionRowsForHeight(s);
                DetailStripRestoreMetaRowsForHeight(s);
                DetailStripHideOverflowLines(s, budgetH);
            }
            catch { }
        }

        private void DetailStripBeginScrubHeightLock()
        {
            if (_detailStripScrubHeightLocked) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float h = _detailStripMeasuredHeight > 8f
                ? _detailStripMeasuredHeight
                : DetailStripRowHeight(s);
            _detailStripScrubLockedHeight = h;
            _detailStripScrubHeightLocked = true;
        }

        private void DetailStripEndScrubHeightLock()
        {
            // Promote scrub height into session auto-lock so release does not remasure/jump.
            if (_detailStripScrubLockedHeight > 8f && !DetailStripHasUserHeight())
            {
                _detailStripAutoHeightLock = _detailStripScrubLockedHeight;
                _detailStripMeasuredHeight = _detailStripScrubLockedHeight;
            }
            _detailStripScrubHeightLocked = false;
            _detailStripScrubLockedHeight = -1f;
            DetailStripSyncScrubIndexOverlay();
            DetailStripSyncThumbNavChrome();
        }

        private float DetailStripComputeContentHeight(float s)
        {
            float hardMin = DetailStripHardMinHeight(s);
            float maxH = DetailStripMaxHeight(s);
            return Mathf.Clamp(DetailStripComputeContentHeightRaw(s), hardMin, maxH);
        }

        /// <summary>
        /// Unclamped TextCol stack height. Used by overflow adapt — clamped measure always
        /// sits ≤ max and would skip hide/ellipsis.
        /// </summary>
        private float DetailStripComputeContentHeightRaw(float s)
        {
            return DetailStripComputeContentHeightCore(s, includeDesc: true, includePackageTags: true);
        }

        private float DetailStripComputeContentHeightCore(float s, bool includeDesc, bool includePackageTags)
        {
            return DetailStripComputeContentHeightCore(s, includeDesc, includePackageTags, applyHardMin: true);
        }

        private float DetailStripComputeContentHeightCore(
            float s, bool includeDesc, bool includePackageTags, bool applyHardMin)
        {
            float lineH = DetailStripLineHeight(s);
            float hitH = DetailStripHitHeight(s);
            float gap = DetailStripBandGap(s);
            // vPad matches TextCol VLG top+bottom pad (4+4).
            float vPad = 8f * s;
            float total = vPad;
            int parts = 0;

            if (_detailStripTitleRowGO != null && _detailStripTitleRowGO.activeSelf)
            {
                if (parts > 0) total += gap;
                total += lineH;
                parts++;
            }

            // Derive from live row count — stale LayoutElement preferredHeight after scale-down
            // left a tall empty meta band (black gap between title and actions).
            float metaH = DetailStripMetaHostHeight(s);
            if (metaH > 0.5f)
            {
                if (parts > 0) total += gap;
                total += metaH;
                parts++;
            }

            int actN = 0;
            if (_detailStripActionRows != null)
            {
                for (int i = 0; i < _detailStripActionRows.Length; i++)
                {
                    if (_detailStripActionRows[i] != null && _detailStripActionRows[i].activeSelf
                        && DetailStripRowHasVisibleChild(_detailStripActionRows[i]))
                        actN++;
                }
            }
            if (actN > 0)
            {
                if (parts > 0) total += gap;
                total += hitH * actN + gap * Mathf.Max(0, actN - 1);
                parts++;
            }

            if (DetailStripFlexLineVisible(_detailStripTags))
            {
                if (parts > 0) total += gap;
                total += DetailStripFlexLineCurrentHeight(_detailStripTags, lineH);
                parts++;
            }
            if (DetailStripFlexLineVisible(_detailStripPath))
            {
                if (parts > 0) total += gap;
                total += lineH;
                parts++;
            }
            if (includeDesc && DetailStripFlexLineVisible(_detailStripDesc))
            {
                if (parts > 0) total += gap;
                total += DetailStripFlexLineCurrentHeight(_detailStripDesc, lineH);
                parts++;
            }
            if (includePackageTags && DetailStripFlexLineVisible(_detailStripPackageTags))
            {
                if (parts > 0) total += gap;
                total += DetailStripFlexLineCurrentHeight(_detailStripPackageTags, lineH);
                parts++;
            }

            // Side column scrolls — never grow strip past left-column content.
            if (!applyHardMin) return total;
            float hardMin = DetailStripHardMinHeight(s);
            return Mathf.Max(total, hardMin);
        }

        private static void DetailStripNormalizeRowRect(GameObject row)
        {
            if (row == null) return;
            RectTransform rt = row.GetComponent<RectTransform>();
            if (rt == null) return;
            // Horizontal drift only. Never rewrite vertical anchors/size — that fights VLG and
            // piles every TextCol band on the parent top (meta + actions overlap).
            float yMin = rt.offsetMin.y;
            float yMax = rt.offsetMax.y;
            if (Mathf.Abs(rt.offsetMin.x) > 0.01f || Mathf.Abs(rt.offsetMax.x) > 0.01f)
            {
                rt.offsetMin = new Vector2(0f, yMin);
                rt.offsetMax = new Vector2(0f, yMax);
            }
            Vector2 ap = rt.anchoredPosition;
            if (Mathf.Abs(ap.x) > 0.01f)
                rt.anchoredPosition = new Vector2(0f, ap.y);
        }

        /// <summary>Re-align every TextCol band to the same left/right edge (scale-safe).</summary>
        private void DetailStripNormalizeTextColRows()
        {
            DetailStripNormalizeRowRect(_detailStripTitleRowGO);
            DetailStripNormalizeRowRect(_detailStripMetaHost);
            if (_detailStripMetaRows != null)
            {
                for (int i = 0; i < _detailStripMetaRows.Length; i++)
                    DetailStripNormalizeRowRect(_detailStripMetaRows[i]);
            }
            DetailStripNormalizeRowRect(_detailStripActionsRowGO);
            if (_detailStripActionRows != null)
            {
                for (int i = 0; i < _detailStripActionRows.Length; i++)
                    DetailStripNormalizeRowRect(_detailStripActionRows[i]);
            }
            if (_detailStripTags != null && _detailStripTags.transform.parent != null)
                DetailStripNormalizeRowRect(_detailStripTags.transform.parent.gameObject);
            if (_detailStripPath != null && _detailStripPath.transform.parent != null)
                DetailStripNormalizeRowRect(_detailStripPath.transform.parent.gameObject);
            if (_detailStripDesc != null && _detailStripDesc.transform.parent != null)
                DetailStripNormalizeRowRect(_detailStripDesc.transform.parent.gameObject);
            if (_detailStripPackageTags != null && _detailStripPackageTags.transform.parent != null)
                DetailStripNormalizeRowRect(_detailStripPackageTags.transform.parent.gameObject);
        }

        private static bool DetailStripFlexLineVisible(Text line)
        {
            if (line == null) return false;
            Transform row = line.transform.parent;
            if (row != null) return row.gameObject.activeSelf;
            return line.gameObject.activeSelf;
        }

        private static bool DetailStripRowHasVisibleChild(GameObject row)
        {
            if (row == null) return false;
            Transform t = row.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c != null && c.gameObject.activeSelf && c.name != null && c.name.StartsWith("Link_"))
                    return true;
            }
            return false;
        }

        private void DetailStripPackActionRows(float s)
        {
            if (_detailStripActionRows == null || _detailStripActionRows.Length < 2) return;

            Text[] links = new Text[]
            {
                _detailStripLoadLink, _detailStripHubLink, _detailStripCopyLink, _detailStripDeleteLink,
                _detailStripCacheLink, _detailStripAutoLoadLink, _detailStripNoAutoLoadLink,
                _detailStripHideLink, _detailStripUnhideLink, _detailStripTempWlLink, _detailStripOldVersLink
            };

            var visible = new List<Text>(links.Length);
            for (int i = 0; i < links.Length; i++)
            {
                Text link = links[i];
                if (link == null || !link.gameObject.activeSelf) continue;
                visible.Add(link);
            }

            for (int r = 0; r < _detailStripActionRows.Length; r++)
                DetailStripStripActionSeps(_detailStripActionRows[r]);

            float avail = DetailStripEstimateMetaAvailWidth();
            float sepW = 14f * s;
            float pad = 8f * s;
            float x = 0f;
            int rowIdx = 0;
            int maxRow = _detailStripActionRows.Length - 1;

            for (int i = 0; i < visible.Count; i++)
            {
                float w = DetailStripEstimateLinkWidth(visible[i], s);
                float need = w + (x > 0.5f ? sepW : 0f);
                if (x > 0.5f && x + need > avail - pad && rowIdx < maxRow)
                {
                    rowIdx++;
                    x = 0f;
                    need = w;
                }
                GameObject parent = _detailStripActionRows[rowIdx];
                if (parent == null) continue;
                if (parent.transform != visible[i].transform.parent)
                    visible[i].transform.SetParent(parent.transform, false);
                visible[i].transform.SetAsLastSibling();
                x += need;
            }

            for (int r = 0; r < _detailStripActionRows.Length; r++)
            {
                GameObject row = _detailStripActionRows[r];
                if (row == null) continue;
                DetailStripRebuildActionSeps(row, s);
                row.SetActive(DetailStripRowHasVisibleChild(row));
            }
            DetailStripSyncActionsHostHeight(s);

            GameObject rowForSeps = DetailStripFirstActiveActionRow();
            _detailStripAutoLoadSepGO = DetailStripFindSepBetween(rowForSeps, _detailStripAutoLoadLink, _detailStripNoAutoLoadLink);
            GameObject hideRow = DetailStripFindActionRowOf(_detailStripHideLink);
            if (hideRow == null) hideRow = DetailStripFindActionRowOf(_detailStripUnhideLink);
            if (hideRow == null) hideRow = rowForSeps;
            _detailStripHideUnhideSepGO = DetailStripFindSepBetween(hideRow, _detailStripHideLink, _detailStripUnhideLink);
            _detailStripAfterHideSepGO = DetailStripFindSepAfter(hideRow, _detailStripHideLink, _detailStripUnhideLink);
            GameObject oldRow = DetailStripFindActionRowOf(_detailStripOldVersLink);
            if (oldRow == null) oldRow = hideRow;
            _detailStripBeforeOldVersSepGO = DetailStripFindSepBefore(oldRow, _detailStripOldVersLink);
        }

        private GameObject DetailStripFirstActiveActionRow()
        {
            if (_detailStripActionRows == null) return null;
            for (int i = 0; i < _detailStripActionRows.Length; i++)
            {
                if (_detailStripActionRows[i] != null && _detailStripActionRows[i].activeSelf)
                    return _detailStripActionRows[i];
            }
            return _detailStripActionRows.Length > 0 ? _detailStripActionRows[0] : null;
        }

        private GameObject DetailStripFindActionRowOf(Text link)
        {
            if (link == null || link.transform.parent == null) return null;
            return link.transform.parent.gameObject;
        }

        private static void DetailStripStripActionSeps(GameObject row)
        {
            if (row == null) return;
            Transform t = row.transform;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform c = t.GetChild(i);
                if (c != null && c.name == "Sep")
                {
                    try { UnityEngine.Object.Destroy(c.gameObject); } catch { }
                }
            }
        }

        private void DetailStripRebuildActionSeps(GameObject row, float s)
        {
            if (row == null) return;
            Transform t = row.transform;
            var links = new List<Transform>(8);
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c != null && c.gameObject.activeSelf && c.name != null && c.name.StartsWith("Link_"))
                    links.Add(c);
            }
            for (int i = 1; i < links.Count; i++)
            {
                bool hard = links[i - 1] != null && links[i - 1].name == "Link_Load";
                GameObject sep = DetailStripAddLinkSepGO(row, s, hard);
                if (sep != null) sep.transform.SetSiblingIndex(links[i].GetSiblingIndex());
            }
        }

        private static GameObject DetailStripFindSepBetween(GameObject row, Text a, Text b)
        {
            if (row == null || a == null || b == null) return null;
            if (!a.gameObject.activeSelf || !b.gameObject.activeSelf) return null;
            int ia = a.transform.GetSiblingIndex();
            int ib = b.transform.GetSiblingIndex();
            if (ia > ib) { int tmp = ia; ia = ib; ib = tmp; }
            Transform t = row.transform;
            for (int i = ia + 1; i < ib && i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c != null && c.name == "Sep") return c.gameObject;
            }
            return null;
        }

        private static GameObject DetailStripFindSepAfter(GameObject row, Text a, Text b)
        {
            if (row == null) return null;
            Text last = null;
            if (a != null && a.gameObject.activeSelf) last = a;
            if (b != null && b.gameObject.activeSelf) last = b;
            if (last == null) return null;
            int idx = last.transform.GetSiblingIndex();
            Transform t = row.transform;
            if (idx + 1 < t.childCount)
            {
                Transform c = t.GetChild(idx + 1);
                if (c != null && c.name == "Sep") return c.gameObject;
            }
            return null;
        }

        private static GameObject DetailStripFindSepBefore(GameObject row, Text link)
        {
            if (row == null || link == null || !link.gameObject.activeSelf) return null;
            int idx = link.transform.GetSiblingIndex();
            if (idx <= 0) return null;
            Transform c = row.transform.GetChild(idx - 1);
            if (c != null && c.name == "Sep") return c.gameObject;
            return null;
        }

        private static float DetailStripEstimateLinkWidth(Text link, float s)
        {
            if (link == null) return 40f * s;
            string txt = link.text ?? "";
            float charW = Mathf.Max(5f, GalleryUiDesignTokens.FontRef * 0.55f * s);
            return Mathf.Max(28f * s, txt.Length * charW);
        }

        private void DetailStripHide()
        {
            if (_detailStripGO != null) _detailStripGO.SetActive(false);
            _detailStripCacheKey = "";
            _detailStripSideContentKey = "";
            _detailStripThumbFile = null;
            _detailStripBoundFile = null;
            _detailStripBoundCreator = "";
            _detailStripMeasuredHeight = -1f;
            DetailStripInvalidateAutoHeightLock();
            DetailStripResetStackSideDecision();
            _detailStripScrubActive = false;
            _detailStripScrubPendingSteps = 0;
            _detailStripScrubIndex = -1;
            DetailStripEndScrubHeightLock();
            if (_detailStripThumb != null)
            {
                _detailStripThumb.texture = null;
                _detailStripThumb.color = new Color(1f, 1f, 1f, 0.15f);
            }
            if (_detailStripExpandBtnGO != null) _detailStripExpandBtnGO.SetActive(false);
            if (_detailStripCollapseBtnGO != null) _detailStripCollapseBtnGO.SetActive(false);
        }

        private void DetailStripRefresh()
        {
            // Settings rows are not packages — never show package detail chrome.
            if (settingsListViewActive)
            {
                try { DetailStripHide(); } catch { }
                return;
            }

            DetailStripEnsure();
            DetailStripEnsureExpandButton();

            // Entire scrub session (active wheel OR height-locked): never hide/populate/reflow.
            // Side meta is filled on scrub commit (see DetailStripCommitScrub).
            if (DetailStripScrubBlocksRebuild)
            {
                // Scrub blocks strip rebuild, but tag popup must still track selection swaps.
                try { DetailStripSyncOpenTagMenuIfSelectionChanged(); } catch { }
                return;
            }

            int sel = selectedFiles != null ? selectedFiles.Count : 0;
            if (sel <= 0)
            {
                // Tag editor stays open (Apply shows empty lists; Database is vocab-only).
                // Only explicit Close / Esc / toggle dismisses it — strip refresh must not.
                DetailStripCloseTagFilterPopup();
                DetailStripHide();
                try { DetailStripSyncOpenTagMenuIfSelectionChanged(); } catch { }
                return;
            }

            // Collapsed: strip height 0; Info expand button stays in toolbox gutter.
            if (!DetailStripIsExpanded())
            {
                // Keep tag editor open across strip collapse (mode switch / Edit must stay stable).
                try { DetailStripCloseTagFilterPopup(); } catch { }
                if (_detailStripGO != null) _detailStripGO.SetActive(false);
                DetailStripSyncCollapseExpandChrome();
                try { DetailStripSyncOpenTagMenuIfSelectionChanged(); } catch { }
                return;
            }

            if (_detailStripGO == null) return;

            string key = BuildDetailStripCacheKey();
            bool same = string.Equals(key, _detailStripCacheKey, StringComparison.Ordinal);
            if (same && _detailStripGO.activeSelf)
            {
                DetailStripSyncCollapseExpandChrome();
                // Scrub/enrich updates cache key without side fill — catch stale side here.
                bool sideRefreshed = false;
                if (DetailStripSideContentNeedsRefresh())
                {
                    try
                    {
                        DetailStripRefreshSideMetaForSelection();
                        sideRefreshed = true;
                    }
                    catch { }
                }
                try { DetailStripSyncOpenTagMenuIfSelectionChanged(); } catch { }
                if (sideRefreshed) return;

                // Meta vanished after scale sync — force rebuild even when cache key matches.
                bool metaMissing = DetailStripActiveMetaRowCount() <= 0
                    || (_detailStripMetaHost != null && !_detailStripMetaHost.activeSelf);
                try
                {
                    if (metaMissing)
                        DetailStripReflowMetaForCurrentSelection();
                    else
                    {
                        float avail = DetailStripEstimateMetaAvailWidth();
                        // Match width-side hysteresis scale: tiny avail drift must not reflow every
                        // SelectionContext tick (0.25s) or height↔thumb↔pack hunts forever.
                        float drift = Mathf.Abs(avail - _detailStripMetaAvailWidth);
                        float reflowEps = 8f;
                        float geomEps = 6f;
                        if (_detailStripMetaAvailWidth < 0f || drift > reflowEps)
                        {
                            // Width-class change: reflow meta only — do not remasure strip height
                            // (height stickiness keeps thumb/nav stable across items).
                            DetailStripReflowMetaForCurrentSelection();
                        }
                        else if (drift > geomEps)
                            DetailStripRefreshGeometry();
                    }
                }
                catch { }
                return;
            }
            _detailStripCacheKey = key;

            // Stack/height lock follows selection identity only — tag edits remount content but keep height.
            DetailStripOnSelectionLayoutKeyChanged();
            DetailStripShowChrome();
            if (sel == 1)
                DetailStripPopulateSingle(selectedFiles[0], reloadThumb: true);
            else
                DetailStripPopulateMulti(sel, reloadThumb: true);
            DetailStripSyncCollapseExpandChrome();
            try { DetailStripSyncOpenTagMenuIfSelectionChanged(force: true); } catch { }
        }

        private string DetailStripSideContentKeyForSelection()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return "";
            bool historyBrowse = activeContentType == ContentType.History;
            FileEntry f = selectedFiles[0];
            if (f == null) return "";
            if (selectedFiles.Count == 1)
                return GetSelectionIdentityKey(f, historyBrowse);
            return "M" + selectedFiles.Count + "|" + GetSelectionIdentityKey(f, historyBrowse);
        }

        private bool DetailStripSideContentNeedsRefresh()
        {
            return !string.Equals(
                DetailStripSideContentKeyForSelection(),
                _detailStripSideContentKey ?? "",
                StringComparison.Ordinal);
        }

        /// <summary>Description + native tags for current selection (hydrate meta.json).</summary>
        private void DetailStripRefreshSideMetaForSelection()
        {
            FileEntry file = null;
            if (selectedFiles != null && selectedFiles.Count > 0)
                file = selectedFiles[0];
            if (file == null) file = _detailStripBoundFile;
            DetailStripRefreshDescription(file);
            DetailStripRefreshSideContent(file);
            DetailStripRefreshTagsLineForPlacement();
            DetailStripRefreshGeometry();
        }

        private void DetailStripShowChrome()
        {
            _detailStripGO.SetActive(true);
            _detailStripGO.transform.SetAsLastSibling();
            if (_tryOnActive && _tryOnBarGO != null)
                _tryOnBarGO.transform.SetAsLastSibling();
            DetailStripLayout();
        }

        private string BuildDetailStripCacheKey()
        {
            var sb = new StringBuilder(160);
            if (selectedFiles == null || selectedFiles.Count == 0) return "";
            bool historyBrowse = activeContentType == ContentType.History;
            if (selectedFiles.Count == 1)
            {
                FileEntry f = selectedFiles[0];
                sb.Append('1').Append('|').Append(GetSelectionIdentityKey(f, historyBrowse));
                // Rating is paint-only — never part of layout cache (remount remasured height).
                sb.Append('|').Append(DetailStripUserTagsFingerprint(f));
                return sb.ToString();
            }

            sb.Append('M').Append(selectedFiles.Count);
            var keys = new List<string>(selectedFiles.Count);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                keys.Add(GetSelectionIdentityKey(f, historyBrowse));
            }
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append('|');
                sb.Append(keys[i]);
            }
            return sb.ToString();
        }

        private void DetailStripPopulateSingle(FileEntry file, bool reloadThumb)
        {
            if (file == null)
            {
                DetailStripHide();
                return;
            }

            _detailStripBoundFile = file;
            _detailStripBoundCreator = DetailStripResolveCreator(file);

            if (_detailStripTitle != null)
                _detailStripTitle.text = GetGalleryListRowDisplayName(file);
            DetailStripRefreshStars();

            int deps = 0, missing = 0, dependents = 0;
            try { deps = GallerySortManager.GetDepsCount(file); } catch { }
            try { missing = GallerySortManager.GetMissingDepsCount(file); } catch { }
            try { dependents = GallerySortManager.GetDependentsCount(file); } catch { }

            DetailStripRebuildMetaFields(DetailStripCollectMetaFields(file, deps, missing, dependents, -1, -1, false));

            DetailStripSetToolLinksEnabled(true);
            DetailStripRefreshDescription(file);
            DetailStripRefreshSideContent(file);
            DetailStripRefreshTagsLineForPlacement();

            if (_detailStripPath != null)
            {
                _detailStripPath.text = DetailStripResolvePathLine(file);
                _detailStripWantPath = true;
                DetailStripSetFlexLineActive(_detailStripPath, true);
            }
            else _detailStripWantPath = false;

            DetailStripRefreshBadgesForSelection();
            DetailStripRefreshGeometry();

            if (_detailStripThumbGO != null) _detailStripThumbGO.SetActive(true);
            if (reloadThumb || !ReferenceEquals(_detailStripThumbFile, file))
                DetailStripLoadThumb(file);
        }

        private void DetailStripPopulateMulti(int sel, bool reloadThumb)
        {
            FileEntry first = selectedFiles != null && selectedFiles.Count > 0 ? selectedFiles[0] : null;
            _detailStripBoundFile = first;
            _detailStripBoundCreator = DetailStripResolveCreator(first);

            if (_detailStripTitle != null)
            {
                _detailStripTitle.text = string.Format(
                    VPBTranslation.T("gallery.detail.selected_many", "{0} selected"), sel);
            }
            DetailStripRefreshStars();

            long totalSize = 0;
            int missingTotal = 0;
            string sharedCreator = null;
            bool creatorMixed = false;

            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                if (f.Size > 0) totalSize += f.Size;
                try { missingTotal += GallerySortManager.GetMissingDepsCount(f); } catch { }

                string c = DetailStripResolveCreator(f);
                if (!creatorMixed)
                {
                    if (sharedCreator == null) sharedCreator = c ?? "";
                    else if (!string.Equals(sharedCreator, c ?? "", StringComparison.OrdinalIgnoreCase))
                        creatorMixed = true;
                }
            }

            if (!creatorMixed) _detailStripBoundCreator = sharedCreator ?? "";
            else _detailStripBoundCreator = "";

            // Multi: deps chips operate on first item; tag/copy still work for whole selection.
            int deps = 0, missing = 0, dependents = 0;
            if (first != null)
            {
                try { deps = GallerySortManager.GetDepsCount(first); } catch { }
                try { missing = GallerySortManager.GetMissingDepsCount(first); } catch { }
                try { dependents = GallerySortManager.GetDependentsCount(first); } catch { }
            }

            int mShow = missingTotal > 0 ? missingTotal : missing;
            DetailStripRebuildMetaFields(DetailStripCollectMetaFields(
                first, deps, mShow, dependents, sel, totalSize, creatorMixed));

            DetailStripSetToolLinksEnabled(first != null);
            // First-item description still useful for multi (same as deps chips / thumb).
            DetailStripRefreshDescription(first);
            DetailStripRefreshSideContent(first);
            DetailStripRefreshTagsLineForPlacement();

            if (_detailStripPath != null)
            {
                // Multi: show first-item path (Copy still dumps all paths).
                if (first != null)
                {
                    string pathLine = DetailStripResolvePathLine(first);
                    string hint = VPBTranslation.T(
                        "gallery.detail.multi_path_hint", "first item — Copy for all paths");
                    _detailStripPath.text = string.IsNullOrEmpty(pathLine)
                        ? hint
                        : (pathLine + " · " + hint);
                    _detailStripWantPath = true;
                    DetailStripSetFlexLineActive(_detailStripPath, true);
                }
                else
                {
                    _detailStripPath.text = "";
                    _detailStripWantPath = false;
                    DetailStripSetFlexLineActive(_detailStripPath, false);
                }
            }
            else _detailStripWantPath = false;

            DetailStripRefreshBadgesForSelection();
            DetailStripRefreshGeometry();

            if (_detailStripThumbGO != null) _detailStripThumbGO.SetActive(first != null);
            if (first != null && (reloadThumb || !ReferenceEquals(_detailStripThumbFile, first)))
                DetailStripLoadThumb(first);
        }

        private static void DetailStripSetLink(
            Text link, string label, bool enabled, Color idleColor, Color? hoverColor = null)
        {
            if (link == null) return;
            link.text = label ?? "";
            link.raycastTarget = enabled;
            bool rich = label != null && label.IndexOf("<color=", StringComparison.OrdinalIgnoreCase) >= 0;
            // Preserve hover tint while pointer still over the link.
            UIHoverDelegate del = link.GetComponent<UIHoverDelegate>();
            bool hovered = del != null && del.IsHovered;
            Color hover = hoverColor ?? DetailStripBrighten(idleColor, 0.18f);
            if (!enabled)
                link.color = DetailStripLinkDisabledColor;
            else if (rich)
                link.color = Color.white; // rich text supplies its own colors
            else
                link.color = hovered ? hover : idleColor;
        }

        private void DetailStripSetToolLinksEnabled(bool enabled)
        {
            // Task chrome: sticky Eraser/Try-On/Cleanup/Bench kill strip actions.
            try { enabled = TaskChromeShouldEnableDetailStripTools(enabled); } catch { }

            DetailStripSetLink(
                _detailStripLoadLink,
                VPBTranslation.T("gallery.detail.chip_load", "Load"),
                enabled,
                DetailStripActionPrimary);

            DetailStripSetLink(
                _detailStripHubLink,
                VPBTranslation.T("gallery.detail.chip_hub", "Hub"),
                enabled,
                DetailStripActionSecondary);

            DetailStripSetLink(
                _detailStripCopyLink,
                VPBTranslation.T("gallery.detail.chip_copy", "Copy"),
                enabled,
                DetailStripActionSecondary);

            bool canDelete = false;
            if (enabled)
            {
                try
                {
                    canDelete = GetTboxDeleteEligiblePackageCount()
                        + GetTboxDeleteEligibleLocalSceneCount()
                        + GetTboxDeleteEligibleLocalPresetCount() > 0;
                }
                catch { canDelete = false; }
            }
            DetailStripSetLink(
                _detailStripDeleteLink,
                VPBTranslation.T("gallery.detail.chip_delete", "Delete"),
                canDelete,
                DetailStripActionSecondary,
                DetailStripActionDanger);
            if (_detailStripDeleteLink != null)
                _detailStripDeleteLink.gameObject.SetActive(enabled);

            DetailStripSetLink(
                _detailStripCacheLink,
                VPBTranslation.T("gallery.detail.chip_cache", "Cache"),
                enabled,
                DetailStripActionSecondary);
            DetailStripRefreshAutoLoadLinks(enabled);
            DetailStripRefreshHideLinks(enabled);
            DetailStripSetLink(
                _detailStripTempWlLink,
                VPBTranslation.T("gallery.detail.chip_temp_wl", "Temp whitelist"),
                enabled,
                DetailStripActionSecondary);
            DetailStripRefreshOldVersLink(enabled);
        }

        private void DetailStripCountAutoLoad(out int enableN, out int disableN)
        {
            enableN = 0;
            disableN = 0;
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            bool scanWlEnabled = false;
            try { scanWlEnabled = ScanWhitelistManager.Instance != null && ScanWhitelistManager.Instance.IsEnabled; }
            catch { scanWlEnabled = false; }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                if (!TryGetTboxResolvablePackageState(f, out string uid, out _, out _, out bool fiAi, out bool uidAl, out bool uidWl))
                    continue;
                if (!seen.Add(uid)) continue;

                bool hasAnyAiFlag = fiAi || uidAl || (scanWlEnabled && uidWl);
                bool missingAnyAiFlag = !fiAi || !uidAl || (scanWlEnabled && !uidWl);
                if (hasAnyAiFlag) disableN++;
                if (missingAnyAiFlag) enableN++;
            }
        }

        private void DetailStripRefreshAutoLoadLinks(bool toolsEnabled)
        {
            int enableN = 0, disableN = 0;
            try { DetailStripCountAutoLoad(out enableN, out disableN); }
            catch { enableN = 0; disableN = 0; }

            bool multi = selectedFiles != null && selectedFiles.Count > 1;
            bool showEnable = toolsEnabled && enableN > 0;
            bool showDisable = toolsEnabled && disableN > 0;

            string enableLabel = multi
                ? VPBTranslation.T("gallery.detail.chip_autoload_all", "AutoLoad All")
                : VPBTranslation.T("gallery.detail.chip_autoload", "AutoLoad");
            string disableLabel = multi
                ? VPBTranslation.T("gallery.detail.chip_no_autoload_all", "Clear AutoLoad All")
                : VPBTranslation.T("gallery.detail.chip_no_autoload", "Clear AutoLoad");

            DetailStripSetLink(_detailStripAutoLoadLink, enableLabel, showEnable, DetailStripActionSecondary);
            DetailStripSetLink(_detailStripNoAutoLoadLink, disableLabel, showDisable, DetailStripActionSecondary);

            if (_detailStripAutoLoadLink != null)
                _detailStripAutoLoadLink.gameObject.SetActive(showEnable);
            if (_detailStripNoAutoLoadLink != null)
                _detailStripNoAutoLoadLink.gameObject.SetActive(showDisable);
            if (_detailStripAutoLoadSepGO != null)
                _detailStripAutoLoadSepGO.SetActive(showEnable && showDisable);
        }

        private void DetailStripRefreshOldVersLink(bool toolsEnabled)
        {
            int olderN = 0;
            if (toolsEnabled)
            {
                try { olderN = DetailStripCollectOlderSiblingUids(null).Count; }
                catch { olderN = 0; }
            }
            bool show = toolsEnabled && olderN > 0;
            string label = olderN > 0
                ? string.Format(VPBTranslation.T("gallery.detail.chip_old_vers_n", "Older versions ({0})"), olderN)
                : VPBTranslation.T("gallery.detail.chip_old_vers", "Older versions");
            DetailStripSetLink(_detailStripOldVersLink, label, show, DetailStripActionSecondary);
            if (_detailStripOldVersLink != null)
                _detailStripOldVersLink.gameObject.SetActive(show);
            if (_detailStripBeforeOldVersSepGO != null)
                _detailStripBeforeOldVersSepGO.SetActive(show);
        }

        private void DetailStripCountHideUnhide(out int hideN, out int unhideN)
        {
            hideN = 0;
            unhideN = 0;
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                if (TryGetTboxResolvablePackageState(f, out string uid, out _, out bool hidden, out _, out _))
                {
                    if (!seen.Add(uid)) continue;
                    if (hidden) unhideN++;
                    else hideN++;
                    continue;
                }
                if (TryGetTboxResolvableLocalPresetHideState(f, out string presetKey, out bool presetHidden))
                {
                    if (!seen.Add(presetKey)) continue;
                    if (presetHidden) unhideN++;
                    else hideN++;
                }
            }
        }

        private void DetailStripRefreshHideLinks(bool toolsEnabled)
        {
            int hideN = 0, unhideN = 0;
            try { DetailStripCountHideUnhide(out hideN, out unhideN); }
            catch { hideN = 0; unhideN = 0; }

            bool multi = selectedFiles != null && selectedFiles.Count > 1;
            bool showHide = toolsEnabled && hideN > 0;
            bool showUnhide = toolsEnabled && unhideN > 0;

            string hideLabel = multi
                ? VPBTranslation.T("gallery.detail.chip_hide_all", "Hide All")
                : VPBTranslation.T("gallery.detail.chip_hide", "Hide");
            string unhideLabel = multi
                ? VPBTranslation.T("gallery.detail.chip_unhide_all", "Unhide All")
                : VPBTranslation.T("gallery.detail.chip_unhide", "Unhide");

            DetailStripSetLink(_detailStripHideLink, hideLabel, showHide, DetailStripActionSecondary);
            DetailStripSetLink(_detailStripUnhideLink, unhideLabel, showUnhide, DetailStripActionSecondary);

            if (_detailStripHideLink != null)
                _detailStripHideLink.gameObject.SetActive(showHide);
            if (_detailStripUnhideLink != null)
                _detailStripUnhideLink.gameObject.SetActive(showUnhide);
            if (_detailStripHideUnhideSepGO != null)
                _detailStripHideUnhideSepGO.SetActive(showHide && showUnhide);
            if (_detailStripAfterHideSepGO != null)
                _detailStripAfterHideSepGO.SetActive(showHide || showUnhide);
        }

        private List<DetailStripMetaField> DetailStripCollectMetaFields(
            FileEntry file, int deps, int missing, int dependents,
            int selectedCount, long totalSizeOverride, bool creatorMixed)
        {
            var fields = new List<DetailStripMetaField>(10);
            bool multi = selectedCount > 1;
            string mixed = VPBTranslation.T("gallery.detail.mixed_short", "Mixed");
            string firstSuffix = multi
                ? VPBTranslation.T("gallery.detail.label_first_suffix", " (1st)")
                : "";
            string itemName = file != null ? GetGalleryListRowDisplayName(file) : "";
            if (string.IsNullOrEmpty(itemName)) itemName = VPBTranslation.T("gallery.detail.this_item", "this item");

            string author = creatorMixed
                ? mixed
                : (!string.IsNullOrEmpty(_detailStripBoundCreator)
                    ? _detailStripBoundCreator
                    : VPBTranslation.T("gallery.detail.author_local", "Local"));
            bool authorClick = !creatorMixed && !string.IsNullOrEmpty(_detailStripBoundCreator);
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float avail = DetailStripEstimateMetaAvailWidth();
            // Cap long creator names so Author mixes with neighbors (no lone stretched row).
            float authorCap = Mathf.Clamp(avail * 0.30f, 88f * s, 150f * s);
            fields.Add(new DetailStripMetaField
            {
                Label = VPBTranslation.T("gallery.detail.label_author", "Author"),
                Value = author,
                Group = 0,
                Enabled = authorClick,
                ValueColor = DetailStripColorAuthor,
                OnClick = DetailStripOnCreatorClick,
                Tip = authorClick
                    ? string.Format(VPBTranslation.T("gallery.detail.tip.creator_fmt", "Filter the gallery by {0}"), author)
                    : (creatorMixed
                        ? VPBTranslation.T("gallery.detail.tip.author_mixed", "Selection has mixed authors")
                        : VPBTranslation.T("gallery.detail.tip.author_local", "Local / non-package item (no author filter)")),
                MaxValueWidth = authorCap
            });

            if (multi)
            {
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_selected", "Selected"),
                    Value = selectedCount.ToString(),
                    Group = 0,
                    Enabled = false,
                    ValueColor = DetailStripColorFact,
                    Tip = VPBTranslation.T("gallery.detail.tip.selected", "Number of selected items")
                });
            }

            string category;
            bool categoryMixed;
            DetailStripResolveSharedOrMixedMeta(
                multi, DetailStripResolveCategory, out category, out categoryMixed);
            if (!string.IsNullOrEmpty(category) || categoryMixed)
            {
                string catValue = categoryMixed ? mixed : category;
                string catSnap = catValue;
                bool catClick = !categoryMixed && !string.IsNullOrEmpty(category);
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_category", "Category"),
                    Value = catValue,
                    Group = 0,
                    Enabled = catClick,
                    ValueColor = DetailStripColorCategory,
                    OnClick = catClick
                        ? (UnityAction)(() => DetailStripCopyMetaValue(catSnap, VPBTranslation.T("gallery.detail.copied_category", "Copied category")))
                        : null,
                    Tip = categoryMixed
                        ? VPBTranslation.T("gallery.detail.tip.category_mixed", "Selection has mixed categories")
                        : string.Format(VPBTranslation.T("gallery.detail.tip.category_fmt", "Copy category \"{0}\""), category)
                });
            }

            long size = totalSizeOverride > 0 ? totalSizeOverride : (file != null ? file.Size : 0);
            if (size > 0)
            {
                string sizeTxt = FormatBytesForList(size);
                fields.Add(new DetailStripMetaField
                {
                    Label = multi
                        ? VPBTranslation.T("gallery.detail.label_size_total", "Size (total)")
                        : VPBTranslation.T("gallery.detail.label_size", "Size"),
                    Value = sizeTxt,
                    Group = 0,
                    Enabled = true,
                    ValueColor = DetailStripColorFact,
                    OnClick = () => DetailStripCopyMetaValue(sizeTxt, VPBTranslation.T("gallery.detail.copied_size", "Copied size")),
                    Tip = multi
                        ? string.Format(VPBTranslation.T("gallery.detail.tip.size_total_fmt", "Total size of selection: {0}"), sizeTxt)
                        : string.Format(VPBTranslation.T("gallery.detail.tip.size_fmt", "Copy size {0}"), sizeTxt)
                });
            }

            DetailStripAppendTimestampFields(fields, file);

            // License from meta.json (same hydrate as description).
            string license;
            bool licenseMixed;
            DetailStripResolveSharedOrMixedMeta(
                multi, DetailStripResolveLicense, out license, out licenseMixed);
            if (!string.IsNullOrEmpty(license) || licenseMixed)
            {
                string licValue = licenseMixed ? mixed : license;
                string licSnap = licValue;
                bool licClick = !licenseMixed && !string.IsNullOrEmpty(license);
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_license", "License"),
                    Value = licValue,
                    Group = 0,
                    Enabled = licClick,
                    ValueColor = new Color(0.88f, 0.86f, 0.55f, 1f),
                    OnClick = licClick
                        ? (UnityAction)(() => DetailStripOnLicenseClick(licSnap))
                        : null,
                    Tip = licenseMixed
                        ? VPBTranslation.T("gallery.detail.tip.license_mixed", "Selection has mixed licenses")
                        : string.Format(VPBTranslation.T("gallery.detail.tip.license_filter_fmt", "Filter by license \"{0}\""), license)
                });
            }

            // Version / gender / flags — Mixed when selection differs; else show shared / first.
            if (file != null || multi)
            {
                string version;
                bool versionMixed;
                DetailStripResolveSharedOrMixedMeta(
                    multi, DetailStripResolveVersion, out version, out versionMixed);
                if (!string.IsNullOrEmpty(version) || versionMixed)
                {
                    string verValue;
                    Color verColor = DetailStripColorFact;
                    if (versionMixed)
                    {
                        verValue = mixed;
                    }
                    else
                    {
                        string status;
                        DetailStripResolveVersionStatus(file, out status, out verColor);
                        verValue = !string.IsNullOrEmpty(status) ? (version + " " + status) : version;
                    }
                    string verSnap = verValue;
                    bool verClick = !versionMixed && !string.IsNullOrEmpty(verValue);
                    fields.Add(new DetailStripMetaField
                    {
                        Label = VPBTranslation.T("gallery.detail.label_version", "Version"),
                        Value = verValue,
                        Group = 0,
                        Enabled = verClick,
                        ValueColor = verColor,
                        OnClick = verClick
                            ? (UnityAction)(() => DetailStripCopyMetaValue(verSnap, VPBTranslation.T("gallery.detail.copied_version", "Copied version")))
                            : null,
                        Tip = versionMixed
                            ? VPBTranslation.T("gallery.detail.tip.version_mixed", "Selection has mixed versions")
                            : string.Format(VPBTranslation.T("gallery.detail.tip.version_fmt", "Copy version {0}"), verValue)
                    });
                }

                string gender;
                bool genderMixed;
                DetailStripResolveSharedOrMixedMeta(
                    multi, DetailStripResolveGender, out gender, out genderMixed);
                if (!string.IsNullOrEmpty(gender) || genderMixed)
                {
                    string gValue = genderMixed ? mixed : gender;
                    string gSnap = gValue;
                    bool gClick = !genderMixed && !string.IsNullOrEmpty(gender);
                    fields.Add(new DetailStripMetaField
                    {
                        Label = VPBTranslation.T("gallery.detail.label_gender", "Gender"),
                        Value = gValue,
                        Group = 0,
                        Enabled = gClick,
                        ValueColor = DetailStripColorFact,
                        OnClick = gClick
                            ? (UnityAction)(() => DetailStripCopyMetaValue(gSnap, VPBTranslation.T("gallery.detail.copied_gender", "Copied gender")))
                            : null,
                        Tip = genderMixed
                            ? VPBTranslation.T("gallery.detail.tip.gender_mixed", "Selection has mixed gender values")
                            : string.Format(VPBTranslation.T("gallery.detail.tip.gender_fmt", "Copy gender {0}"), gValue)
                    });
                }

                string flags;
                bool flagsMixed;
                DetailStripResolveSharedOrMixedMeta(
                    multi, DetailStripResolveFlags, out flags, out flagsMixed);
                if (!string.IsNullOrEmpty(flags) || flagsMixed)
                {
                    fields.Add(new DetailStripMetaField
                    {
                        Label = VPBTranslation.T("gallery.detail.label_flags", "Flags"),
                        Value = flagsMixed ? mixed : flags,
                        Group = 0,
                        Enabled = false,
                        ValueColor = DetailStripColorFlags,
                        Tip = flagsMixed
                            ? VPBTranslation.T("gallery.detail.tip.flags_mixed", "Selection has mixed flags")
                            : flags
                    });
                }
            }

            // Deps cluster — kept together. Deps/Dependents are first-item when multi; Missing is selection total.
            fields.Add(new DetailStripMetaField
            {
                Label = VPBTranslation.T("gallery.detail.label_deps", "Dependencies") + firstSuffix,
                Value = deps.ToString(),
                Group = 1,
                Enabled = deps > 0,
                ValueColor = DetailStripColorDeps,
                OnClick = DetailStripOnDepsClick,
                Tip = multi
                    ? (deps > 0
                        ? string.Format(VPBTranslation.T("gallery.detail.tip.deps_first_fmt", "Filter to dependencies of first item ({0})"), itemName)
                        : VPBTranslation.T("gallery.detail.tip.deps_none", "No dependencies"))
                    : (deps > 0
                        ? string.Format(VPBTranslation.T("gallery.detail.tip.deps_fmt", "Filter the gallery to dependencies of {0}"), itemName)
                        : VPBTranslation.T("gallery.detail.tip.deps_none", "No dependencies"))
            });

            fields.Add(new DetailStripMetaField
            {
                Label = multi
                    ? VPBTranslation.T("gallery.detail.label_missing_total", "Missing (total)")
                    : VPBTranslation.T("gallery.detail.label_missing", "Missing"),
                Value = missing.ToString(),
                Group = 1,
                Enabled = true,
                ValueColor = missing > 0 ? DetailStripColorMissingBad : DetailStripColorMissingOk,
                OnClick = DetailStripOnMissingClick,
                Tip = multi
                    ? (missing > 0
                        ? string.Format(VPBTranslation.T("gallery.detail.tip.missing_total_fmt", "Selection has {0} missing dependency hits — click to filter"), missing)
                        : VPBTranslation.T("gallery.detail.tip.missing_none", "No missing dependencies — click to confirm"))
                    : (missing > 0
                        ? string.Format(VPBTranslation.T("gallery.detail.tip.missing_fmt", "Filter the gallery to missing dependencies of {0}"), itemName)
                        : VPBTranslation.T("gallery.detail.tip.missing_none", "No missing dependencies — click to confirm"))
            });

            fields.Add(new DetailStripMetaField
            {
                Label = VPBTranslation.T("gallery.detail.label_dependents", "Dependents") + firstSuffix,
                Value = dependents.ToString(),
                Group = 1,
                Enabled = dependents > 0,
                ValueColor = DetailStripColorDependents,
                OnClick = DetailStripOnDependentsClick,
                Tip = multi
                    ? (dependents > 0
                        ? string.Format(VPBTranslation.T("gallery.detail.tip.dependents_first_fmt", "Filter to dependents of first item ({0})"), itemName)
                        : VPBTranslation.T("gallery.detail.tip.dependents_none", "No dependents"))
                    : (dependents > 0
                        ? string.Format(VPBTranslation.T("gallery.detail.tip.dependents_fmt", "Filter the gallery to dependents of {0}"), itemName)
                        : VPBTranslation.T("gallery.detail.tip.dependents_none", "No dependents"))
            });

            return fields;
        }

        /// <summary>
        /// When multi-select: shared value if all match, else Mixed. Single-select: value from bound file.
        /// </summary>
        private void DetailStripResolveSharedOrMixedMeta(
            bool multi, System.Func<FileEntry, string> resolve, out string value, out bool mixed)
        {
            value = "";
            mixed = false;
            if (!multi || selectedFiles == null || selectedFiles.Count <= 1)
            {
                value = resolve != null ? (resolve(_detailStripBoundFile) ?? "") : "";
                return;
            }

            string shared = null;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                string v = "";
                try { v = resolve != null ? (resolve(f) ?? "") : ""; } catch { v = ""; }
                if (shared == null)
                {
                    shared = v;
                    continue;
                }
                if (!string.Equals(shared, v, StringComparison.OrdinalIgnoreCase))
                {
                    mixed = true;
                    value = "";
                    return;
                }
            }
            value = shared ?? "";
        }

        private void DetailStripCopyMetaValue(string value, string status)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                GUIUtility.systemCopyBuffer = value;
                if (!string.IsNullOrEmpty(status)) ShowTemporaryStatus(status, 1.2f);
            }
            catch { }
        }

        private bool DetailStripWantSideContent()
        {
            return _detailStripWantDesc || _detailStripWantNativeTags;
        }

        private float DetailStripComputeSideWidth(float s)
        {
            if (s <= 0f) s = 1f;
            float minCol = GalleryUiDesignTokens.FooterDetailStripSideMinColWidthRef * s;
            float maxCol = GalleryUiDesignTokens.FooterDetailStripSideMaxColWidthRef * s;
            float leftReserve = GalleryUiDesignTokens.FooterDetailStripSideLeftReserveRef * s;
            float stripW = 0f;
            try
            {
                if (_detailStripRT != null && _detailStripRT.rect.width > 8f)
                    stripW = _detailStripRT.rect.width;
            }
            catch { }
            if (stripW < 8f) return minCol;

            float stripH = _detailStripRT != null && _detailStripRT.rect.height > 8f
                ? _detailStripRT.rect.height
                : DetailStripRowHeight(s);
            float thumb = DetailStripThumbEdge(s, stripH);
            float gap = 8f * s;
            float textAvail = Mathf.Max(0f, stripW - thumb - gap);
            float ideal = textAvail * GalleryUiDesignTokens.GoldenRatioMinor;
            float sideW = Mathf.Clamp(ideal, minCol, maxCol);
            if (textAvail - sideW < leftReserve)
                sideW = Mathf.Max(minCol, textAvail - leftReserve);
            return Mathf.Clamp(sideW, minCol, maxCol);
        }

        private bool DetailStripCanShowSide(float s)
        {
            if (!DetailStripWantSideContent()) return false;
            try
            {
                if (VPBConfig.Instance != null && !VPBConfig.Instance.GalleryDetailStripSideInfoEnabled)
                    return false;
            }
            catch { }
            if (s <= 0f) s = 1f;
            float stripW = 0f;
            try
            {
                if (_detailStripRT != null && _detailStripRT.rect.width > 8f)
                    stripW = _detailStripRT.rect.width;
            }
            catch { }

            float openMin = GalleryUiDesignTokens.FooterDetailStripSideMinWidthRef * s;
            float hyst = GalleryUiDesignTokens.FooterDetailStripSideHysteresisRef * s;
            if (hyst < 8f) hyst = 8f;
            float closeMin = Mathf.Max(openMin * 0.55f, openMin - hyst);

            float sideW = DetailStripComputeSideWidth(s);
            float stripH = _detailStripRT != null && _detailStripRT.rect.height > 8f
                ? _detailStripRT.rect.height
                : DetailStripRowHeight(s);
            float thumb = DetailStripThumbEdge(s, stripH);
            float gap = 8f * s;
            float leftReserve = GalleryUiDesignTokens.FooterDetailStripSideLeftReserveRef * s;
            float leftRemain = stripW - thumb - gap - sideW - gap;

            // Sticky band: open needs full threshold; stay open until clearly narrower.
            // Prevents 1–2 Hz flicker when pane width sits on the collapse edge.
            if (_detailStripSideVisible)
            {
                if (stripW < closeMin) return false;
                if (leftRemain < leftReserve - hyst) return false;
                return true;
            }

            if (stripW < openMin) return false;
            if (leftRemain < leftReserve) return false;
            return true;
        }

        private float DetailStripEstimateWrappedLines(string text, float width, float s)
        {
            if (string.IsNullOrEmpty(text) || width < 8f) return 1f;
            if (s <= 0f) s = 1f;
            float charW = Mathf.Max(5f, GalleryUiDesignTokens.FontRef * 0.52f * s);
            int charsPerLine = Mathf.Max(8, Mathf.FloorToInt(width / charW));
            int lines = Mathf.CeilToInt(text.Length / (float)charsPerLine);
            // Count hard newlines as extra breaks.
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') lines++;
            }
            return Mathf.Max(1f, lines);
        }

        /// <summary>Ellipsize prose to fit max wrapped lines (warm path; char-width estimate).</summary>
        private string DetailStripEllipsizeToLines(string full, float width, float s, int maxLines)
        {
            if (string.IsNullOrEmpty(full) || maxLines < 1) return "";
            if (s <= 0f) s = 1f;
            if (DetailStripEstimateWrappedLines(full, width, s) <= maxLines + 0.01f)
                return full;

            float charW = Mathf.Max(5f, GalleryUiDesignTokens.FontRef * 0.52f * s);
            int charsPerLine = Mathf.Max(8, Mathf.FloorToInt(Mathf.Max(8f, width) / charW));
            int maxChars = charsPerLine * maxLines;
            if (maxChars < 2) return "…";

            // Prefer cutting on whitespace so last glyph before … is readable.
            int take = Mathf.Min(full.Length, maxChars - 1);
            int cut = take;
            for (int i = take; i >= Mathf.Max(0, take - charsPerLine); i--)
            {
                char c = full[i];
                if (c == ' ' || c == '\n' || c == '\t' || c == ',' || c == ';' || c == '.')
                {
                    cut = i;
                    break;
                }
            }
            if (cut < 1) cut = take;
            string head = full.Substring(0, cut).TrimEnd();
            if (string.IsNullOrEmpty(head)) head = full.Substring(0, take);
            return head + "…";
        }

        private int DetailStripLeftDescCurrentLines(float s)
        {
            if (_detailStripDesc == null) return 0;
            float lineH = DetailStripLineHeight(s);
            if (lineH < 1f) lineH = 1f;
            float h = DetailStripFlexLineCurrentHeight(_detailStripDesc, lineH);
            int lines = Mathf.RoundToInt(h / lineH);
            if (lines < 1) lines = 1;
            return lines;
        }

        /// <summary>How many desc lines fit under budget after other TextCol bands (excl. desc).</summary>
        private int DetailStripLeftDescFitLines(float s, float budgetH, int softCap)
        {
            if (softCap < 1) return 0;
            if (budgetH < 8f) return softCap;
            float lineH = DetailStripLineHeight(s);
            float gap = DetailStripBandGap(s);
            float without = DetailStripComputeContentHeightCore(
                s, includeDesc: false, includePackageTags: true, applyHardMin: false);
            // Reserve band gap for inserting desc under existing rows.
            float remain = budgetH - without - gap;
            int fit = Mathf.FloorToInt((remain + 0.01f) / Mathf.Max(1f, lineH));
            if (fit < 0) fit = 0;
            if (fit > softCap) fit = softCap;
            return fit;
        }

        /// <summary>Fill left Desc text + row height from content only (never pad to strip spare).</summary>
        private void DetailStripSyncLeftDescContent(float s)
        {
            float budget = DetailStripGeometryBudgetHeight(s);
            DetailStripSyncLeftDescContent(s, budget, maxLinesOverride: -1);
        }

        /// <param name="maxLinesOverride">≥0 forces line cap (overflow shrink). −1 = compute.</param>
        private void DetailStripSyncLeftDescContent(float s, float budgetH, int maxLinesOverride)
        {
            if (_detailStripDesc == null || !_detailStripWantDesc) return;
            if (_detailStripSideVisible) return;
            if (s <= 0f) s = 1f;

            string full = DetailStripResolveDescription(_detailStripBoundFile);
            if (string.IsNullOrEmpty(full))
            {
                _detailStripDesc.text = "";
                DetailStripSyncFlexLineChrome(_detailStripDesc, DetailStripLineHeight(s), s);
                return;
            }

            float lineH = DetailStripLineHeight(s);
            bool stack = DetailStripShouldStackSideAsRows(s);
            int softCap = 1;
            if (stack)
            {
                softCap = GalleryUiDesignTokens.FooterDetailStripLeftDescMaxLines;
                if (softCap < 1) softCap = 1;
            }

            int lines;
            if (maxLinesOverride >= 0)
            {
                lines = Mathf.Clamp(maxLinesOverride, 0, softCap);
            }
            else
            {
                float availW = DetailStripEstimateMetaAvailWidth();
                int needed = Mathf.Clamp(
                    Mathf.CeilToInt(DetailStripEstimateWrappedLines(full, availW, s)),
                    1, softCap);
                int fit = DetailStripLeftDescFitLines(s, budgetH, softCap);
                lines = Mathf.Min(needed, fit);
                // Prefer one ellipsized line over blank — HideOverflow / strip mask guard toolbox.
                if (lines < 1) lines = 1;
            }

            if (lines < 1)
            {
                _detailStripDesc.text = "";
                DetailStripSyncFlexLineChrome(_detailStripDesc, lineH, s);
                return;
            }

            float availWFinal = DetailStripEstimateMetaAvailWidth();
            string shown = DetailStripEllipsizeToLines(full, availWFinal, s, lines);

            if (lines <= 1)
            {
                _detailStripDesc.alignment = TextAnchor.MiddleLeft;
                _detailStripDesc.horizontalOverflow = HorizontalWrapMode.Overflow;
                _detailStripDesc.verticalOverflow = VerticalWrapMode.Truncate;
                _detailStripDesc.text = shown;
                DetailStripSyncFlexLineChrome(_detailStripDesc, lineH, s);
            }
            else
            {
                // Upper-left — short last line must not float mid-row (looks like huge gaps).
                _detailStripDesc.alignment = TextAnchor.UpperLeft;
                _detailStripDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
                _detailStripDesc.verticalOverflow = VerticalWrapMode.Truncate;
                _detailStripDesc.text = shown;
                float rowH = lineH * lines;
                DetailStripSyncFlexLineChrome(_detailStripDesc, rowH, s);
                LayoutElement textLe = _detailStripDesc.GetComponent<LayoutElement>();
                if (textLe != null)
                {
                    textLe.minHeight = lineH;
                    textLe.preferredHeight = rowH;
                }
            }
        }

        private void DetailStripApplyDescPlacement()
        {
            // SideCol (wide+short): scroll desc there. Else main-column row under tags/path.
            bool showLeft = _detailStripWantDesc && !_detailStripSideVisible;
            DetailStripSetFlexLineActive(_detailStripDesc, showLeft);
            if (showLeft)
            {
                float s = ChromeScale;
                if (s <= 0f) s = 1f;
                try { DetailStripSyncLeftDescContent(s); } catch { }
            }
            if (_detailStripSideDescScrollGO != null)
                _detailStripSideDescScrollGO.SetActive(_detailStripSideVisible && _detailStripWantDesc);
        }

        /// <summary>
        /// Tall strip: prefer description + package tags as main-column rows instead of SideCol.
        /// Sticky band (same idea as width side hysteresis) — open at minStack, stay until
        /// clearly shorter so auto-height ↔ pack ↔ side cannot oscillate.
        /// </summary>
        private bool DetailStripShouldStackSideAsRows(float s)
        {
            if (!DetailStripWantSideContent())
            {
                _detailStripStackSideAsRows = false;
                _detailStripStackSideDecided = false;
                return false;
            }
            if (s <= 0f) s = 1f;
            float stripH = DetailStripRowHeight(s);
            float openAt = GalleryUiDesignTokens.FooterDetailStripStackSideMinHeightRef * s;
            float hyst = GalleryUiDesignTokens.FooterDetailStripStackSideHysteresisRef * s;
            if (hyst < 24f) hyst = 24f;
            float stayAt = Mathf.Max(DetailStripHardMinHeight(s), openAt - hyst);

            bool stack;
            if (_detailStripStackSideDecided)
                stack = _detailStripStackSideAsRows
                    ? (stripH + 0.5f >= stayAt)
                    : (stripH + 0.5f >= openAt);
            else
                stack = stripH + 0.5f >= openAt;

            _detailStripStackSideAsRows = stack;
            _detailStripStackSideDecided = true;
            return stack;
        }

        /// <summary>Forget sticky stack decision (selection/scale/hide) so next layout re-picks.</summary>
        private void DetailStripResetStackSideDecision()
        {
            _detailStripStackSideDecided = false;
            _detailStripStackSideAsRows = false;
        }

        /// <summary>Drop auto-fit height lock (next geometry remasures content).</summary>
        private void DetailStripInvalidateAutoHeightLock()
        {
            _detailStripAutoHeightLock = -1f;
            _detailStripAutoHeightLockKey = "";
        }

        /// <summary>Selection-only key for height/stack lock (never includes rating).</summary>
        private string DetailStripSelectionLayoutKey()
        {
            return DetailStripSideContentKeyForSelection();
        }

        /// <summary>
        /// Selection identity changed. Keep auto-height + stack sticky so strip/thumb/nav
        /// do not jump per item — content adapts via HideOverflow to locked budget.
        /// Remasure only on scale / hide / user resize / explicit invalidate.
        /// </summary>
        private void DetailStripOnSelectionLayoutKeyChanged()
        {
            // Intentionally no InvalidateAutoHeightLock / ResetStackSideDecision.
        }

        private static float DetailStripFlexLineCurrentHeight(Text line, float fallbackLineH)
        {
            if (line == null || line.transform.parent == null) return fallbackLineH;
            LayoutElement rowLe = line.transform.parent.GetComponent<LayoutElement>();
            if (rowLe != null && rowLe.preferredHeight > 0.5f)
                return rowLe.preferredHeight;
            return fallbackLineH;
        }

        /// <summary>
        /// Package tags (meta.json regions): SideCol when wide+short; else left row under path.
        /// Never merge into user-tags line — user tags stay actionable and above.
        /// </summary>
        private void DetailStripApplyPackageTagsPlacement()
        {
            if (_detailStripPackageTags == null) return;
            bool showLeft = _detailStripWantNativeTags && !_detailStripSideVisible;
            if (showLeft)
            {
                FileEntry file = _detailStripBoundFile;
                if (file == null && selectedFiles != null && selectedFiles.Count > 0)
                    file = selectedFiles[0];
                string nativeFmt = DetailStripFormatTagNames(DetailStripCollectNativeTags(file));
                if (string.IsNullOrEmpty(nativeFmt))
                {
                    showLeft = false;
                }
                else
                {
                    _detailStripPackageTags.text = string.Format(
                        VPBTranslation.T("gallery.detail.native_tags_fmt", "Package tags: {0}"),
                        nativeFmt);
                    float s = ChromeScale;
                    if (s <= 0f) s = 1f;
                    DetailStripSyncFlexLineChrome(_detailStripPackageTags, DetailStripLineHeight(s), s);
                }
            }
            DetailStripSetFlexLineActive(_detailStripPackageTags, showLeft);
        }

        private void DetailStripSyncSideColumn(float s)
        {
            DetailStripSyncSideColumn(s, allowPlacementChange: true);
        }

        /// <param name="allowPlacementChange">
        /// False = only resize SideCol to current strip edge (post-measure). Flipping stack/side
        /// after height apply causes avail-width drift and 0.25s SelectionContext reflow hunt.
        /// </param>
        private void DetailStripSyncSideColumn(float s, bool allowPlacementChange)
        {
            if (_detailStripSideColGO == null) return;
            if (s <= 0f) s = 1f;

            // Wide opens SideCol; tall strip stacks desc+package tags as main rows instead.
            bool show;
            if (allowPlacementChange)
            {
                bool stack = DetailStripShouldStackSideAsRows(s);
                show = DetailStripCanShowSide(s) && !stack;
            }
            else
            {
                // Keep committed placement — do not re-evaluate sticky stack (that mutates state).
                show = _detailStripSideVisible;
            }
            float sideW = show ? DetailStripComputeSideWidth(s) : 0f;
            float stripH = DetailStripRowHeight(s);
            try
            {
                if (_detailStripRT != null && _detailStripRT.rect.height > stripH + 0.5f)
                    stripH = _detailStripRT.rect.height;
            }
            catch { }
            if (_detailStripSideColLE != null && show)
            {
                _detailStripSideColLE.minWidth = GalleryUiDesignTokens.FooterDetailStripSideMinColWidthRef * s;
                _detailStripSideColLE.preferredWidth = sideW;
                _detailStripSideColLE.minHeight = stripH;
                _detailStripSideColLE.preferredHeight = stripH;
                _detailStripSideColLE.flexibleWidth = 0f;
                _detailStripSideColLE.flexibleHeight = 0f;
            }
            RectTransform sideRT = _detailStripSideColGO.GetComponent<RectTransform>();
            if (sideRT != null && show)
                sideRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, stripH);

            if (allowPlacementChange && _detailStripSideVisible != show)
            {
                _detailStripSideVisible = show;
                _detailStripSideColGO.SetActive(show);
                DetailStripRefreshTagsLineForPlacement();
            }
            else if (show && !_detailStripSideColGO.activeSelf)
            {
                _detailStripSideColGO.SetActive(true);
            }
            else if (!show && _detailStripSideColGO.activeSelf)
            {
                _detailStripSideColGO.SetActive(false);
            }
            else if (allowPlacementChange)
            {
                _detailStripSideVisible = show;
            }

            if (allowPlacementChange)
            {
                DetailStripApplyDescPlacement();
                DetailStripApplyPackageTagsPlacement();
            }
            DetailStripApplySideFieldVisibility(show, s, sideW, stripH);

            if (show)
            {
                try
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(sideRT != null ? sideRT : _detailStripSideColGO.GetComponent<RectTransform>());
                }
                catch { }
            }
        }

        /// <summary>Sync side field active/height. Desc scroll uses flexibleHeight to fill SideCol.</summary>
        private void DetailStripApplySideFieldVisibility(bool show, float s, float sideW, float stripH)
        {
            float lineH = DetailStripLineHeight(s);
            float innerW = show
                ? Mathf.Max(40f * s, sideW - 18f * s - GalleryUiDesignTokens.FooterDetailStripSideScrollBarWidthRef * s)
                : 120f * s;

            if (_detailStripSideNativeTags != null)
            {
                bool on = show && _detailStripWantNativeTags && !string.IsNullOrEmpty(_detailStripSideNativeTags.text);
                if (!on)
                {
                    if (!_detailStripWantNativeTags) _detailStripSideNativeTags.text = "";
                    _detailStripSideNativeTags.gameObject.SetActive(false);
                }
                else
                {
                    string tags = _detailStripSideNativeTags.text ?? "";
                    int maxTagLines = GalleryUiDesignTokens.FooterDetailStripSideTagsMaxLines;
                    if (maxTagLines < 1) maxTagLines = 1;
                    float lines = Mathf.Clamp(DetailStripEstimateWrappedLines(tags, innerW, s), 1f, maxTagLines);
                    float tagsH = lineH * lines;
                    LayoutElement tagLe = _detailStripSideNativeTags.GetComponent<LayoutElement>();
                    if (tagLe != null)
                    {
                        tagLe.minHeight = lineH;
                        tagLe.preferredHeight = tagsH;
                        tagLe.flexibleHeight = 0f;
                    }
                    _detailStripSideNativeTags.gameObject.SetActive(true);
                }
            }

            if (_detailStripSideDescScrollGO != null)
            {
                bool on = show && _detailStripWantDesc && _detailStripSideDesc != null
                    && !string.IsNullOrEmpty(_detailStripSideDesc.text);
                if (!on)
                {
                    if (!_detailStripWantDesc && _detailStripSideDesc != null)
                        _detailStripSideDesc.text = "";
                    _detailStripSideDescScrollGO.SetActive(false);
                }
                else
                {
                    // Fill leftover SideCol height (tags keep preferred; scroll takes flex remainder).
                    if (_detailStripSideDescScrollLE != null)
                    {
                        _detailStripSideDescScrollLE.minHeight = lineH;
                        _detailStripSideDescScrollLE.preferredHeight = lineH;
                        _detailStripSideDescScrollLE.flexibleHeight = 1f;
                    }

                    string desc = _detailStripSideDesc.text ?? "";
                    float contentLines = Mathf.Max(1f, DetailStripEstimateWrappedLines(desc, innerW, s));
                    float contentH = lineH * contentLines;
                    if (_detailStripSideDescLE != null)
                    {
                        _detailStripSideDescLE.minHeight = lineH;
                        _detailStripSideDescLE.preferredHeight = contentH;
                    }
                    if (_detailStripSideDescContentRT != null)
                        _detailStripSideDescContentRT.sizeDelta = new Vector2(0f, contentH);

                    _detailStripSideDescScrollGO.SetActive(true);
                    if (_detailStripSideDescScrollRect != null)
                    {
                        _detailStripSideDescScrollRect.scrollSensitivity = 40f * s;
                        _detailStripSideDescScrollRect.verticalNormalizedPosition = 1f;
                    }
                }
            }
        }

        private void DetailStripRefreshSideContent(FileEntry file)
        {
            // Always clear first — prevents previous selection's package tags/desc sticking.
            DetailStripClearSideContentFields();

            VarPackage pkg = null;
            try { pkg = TryResolvePackageForThumbPlaceholder(file); } catch { pkg = null; }
            try { if (pkg != null) pkg.TryEnsureMetaJsonLiteFields(); } catch { }

            string desc = DetailStripResolveDescription(file);
            _detailStripWantDesc = !string.IsNullOrEmpty(desc);

            HashSet<string> native = DetailStripCollectNativeTags(file);
            string nativeFmt = DetailStripFormatTagNames(native);
            _detailStripWantNativeTags = !string.IsNullOrEmpty(nativeFmt);

            if (_detailStripSideDesc != null)
            {
                if (_detailStripWantDesc)
                    _detailStripSideDesc.text = desc;
            }

            if (_detailStripSideNativeTags != null)
            {
                if (_detailStripWantNativeTags)
                {
                    _detailStripSideNativeTags.text = string.Format(
                        VPBTranslation.T("gallery.detail.native_tags_fmt", "Package tags: {0}"),
                        nativeFmt);
                }
            }

            _detailStripSideContentKey = DetailStripSideContentKeyForSelection();
            DetailStripApplyPackageTagsPlacement();
        }

        private void DetailStripClearSideContentFields()
        {
            // Wipe previous selection visuals before refill (flags set by caller after resolve).
            if (_detailStripSideDesc != null)
                _detailStripSideDesc.text = "";
            if (_detailStripSideDescScrollGO != null)
                _detailStripSideDescScrollGO.SetActive(false);
            if (_detailStripSideNativeTags != null)
            {
                _detailStripSideNativeTags.text = "";
                _detailStripSideNativeTags.gameObject.SetActive(false);
            }
        }

        private void DetailStripRefreshTagsLineForPlacement()
        {
            if (_detailStripTags == null) return;
            FileEntry file = _detailStripBoundFile;
            if (file == null && selectedFiles != null && selectedFiles.Count > 0)
                file = selectedFiles[0];

            int sel = selectedFiles != null ? selectedFiles.Count : 0;
            if (sel > 1)
            {
                HashSet<string> shared = null;
                bool mixed = false;
                string firstFp = null;
                for (int i = 0; i < selectedFiles.Count; i++)
                {
                    FileEntry f = selectedFiles[i];
                    if (f == null) continue;
                    HashSet<string> row = DetailStripCollectUserTags(f);
                    if (shared == null) shared = new HashSet<string>(row, StringComparer.OrdinalIgnoreCase);
                    else shared.IntersectWith(row);
                    string fp = DetailStripUserTagsFingerprint(f);
                    if (firstFp == null) firstFp = fp;
                    else if (!string.Equals(firstFp, fp, StringComparison.Ordinal)) mixed = true;
                }

                string note;
                if (mixed)
                {
                    note = (shared != null && shared.Count > 0)
                        ? VPBTranslation.T("gallery.detail.tags_shared_note", "shared · applies to all selected")
                        : VPBTranslation.T("gallery.detail.mixed_tags", "Mixed tags");
                }
                else if (shared != null && shared.Count > 0)
                    note = VPBTranslation.T("gallery.detail.tags_applies_all", "applies to all selected");
                else
                    note = VPBTranslation.T("gallery.detail.no_tags", "none");

                DetailStripRebuildTagChips(shared, note, force: true, showStampFromFirst: mixed);
                _detailStripWantTags = true;
                DetailStripSetFlexLineActive(_detailStripTags, true);
                DetailStripApplyPackageTagsPlacement();
                DetailStripSyncTagClipboardActionChrome();
                return;
            }

            if (file == null)
            {
                _detailStripWantTags = false;
                DetailStripSetFlexLineActive(_detailStripTags, false);
                DetailStripApplyPackageTagsPlacement();
                DetailStripSyncTagClipboardActionChrome();
                return;
            }

            HashSet<string> tags = DetailStripCollectUserTags(file);
            string emptyNote = (tags == null || tags.Count == 0)
                ? VPBTranslation.T("gallery.detail.no_tags", "none")
                : null;
            DetailStripRebuildTagChips(tags, emptyNote, force: true);
            _detailStripWantTags = true;
            DetailStripSetFlexLineActive(_detailStripTags, true);
            DetailStripApplyPackageTagsPlacement();
            DetailStripSyncTagClipboardActionChrome();
        }

        private static HashSet<string> DetailStripCollectNativeTags(FileEntry file)
        {
            var regions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (file == null) return regions;
            try
            {
                VarFileEntry vfe = file as VarFileEntry;
                if (vfe != null)
                {
                    DetailStripAddNativeTagTokens(regions, vfe.ClothingTags);
                    DetailStripAddNativeTagTokens(regions, vfe.HairTags);
                }

                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg != null)
                {
                    try { pkg.TryEnsureMetaJsonLiteFields(); } catch { }
                    DetailStripAddNativeTagTokens(regions, pkg.PackageMetaTags);
                    DetailStripAddNativeTagTokens(regions, pkg.ClothingTags);
                    DetailStripAddNativeTagTokens(regions, pkg.HairTags);
                }
            }
            catch { }
            return regions;
        }

        private static void DetailStripAddNativeTagTokens(HashSet<string> dest, List<string> rawList)
        {
            if (dest == null || rawList == null) return;
            for (int i = 0; i < rawList.Count; i++)
            {
                string raw = rawList[i];
                if (string.IsNullOrEmpty(raw)) continue;
                string[] parts = raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < parts.Length; p++)
                {
                    string t = parts[p] != null ? parts[p].Trim() : "";
                    if (!string.IsNullOrEmpty(t)) dest.Add(t);
                }
            }
        }

        private float DetailStripEstimateMetaAvailWidth()
        {
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float avail = 280f * s;
            try
            {
                if (_detailStripRT != null && _detailStripRT.rect.width > 8f)
                {
                    float stripH = _detailStripRT.rect.height > 8f
                        ? _detailStripRT.rect.height
                        : DetailStripRowHeight(s);
                    float thumb = DetailStripThumbEdge(s, stripH);
                    float gap = 8f * s;
                    float textPad = 8f * s;
                    avail = Mathf.Max(120f * s, _detailStripRT.rect.width - thumb - gap - textPad);
                    if (_detailStripSideVisible && _detailStripSideColLE != null)
                    {
                        float sideW = Mathf.Max(
                            GalleryUiDesignTokens.FooterDetailStripSideMinColWidthRef * s,
                            _detailStripSideColLE.preferredWidth);
                        avail = Mathf.Max(120f * s, avail - sideW - gap);
                    }
                }
            }
            catch { }
            return avail;
        }

        private void DetailStripReflowMetaForCurrentSelection()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            FileEntry file = selectedFiles[0];
            int deps = 0, missing = 0, dependents = 0;
            try { deps = GallerySortManager.GetDepsCount(file); } catch { }
            try { missing = GallerySortManager.GetMissingDepsCount(file); } catch { }
            try { dependents = GallerySortManager.GetDependentsCount(file); } catch { }
            if (selectedFiles.Count == 1)
            {
                DetailStripRebuildMetaFields(DetailStripCollectMetaFields(file, deps, missing, dependents, -1, -1, false));
                DetailStripRefreshGeometry();
                return;
            }
            long totalSize = 0;
            int missingTotal = 0;
            bool creatorMixed = false;
            string sharedCreator = null;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                if (f.Size > 0) totalSize += f.Size;
                try { missingTotal += GallerySortManager.GetMissingDepsCount(f); } catch { }
                string c = DetailStripResolveCreator(f);
                if (!creatorMixed)
                {
                    if (sharedCreator == null) sharedCreator = c ?? "";
                    else if (!string.Equals(sharedCreator, c ?? "", StringComparison.OrdinalIgnoreCase))
                        creatorMixed = true;
                }
            }
            int mShow = missingTotal > 0 ? missingTotal : missing;
            DetailStripRebuildMetaFields(DetailStripCollectMetaFields(
                file, deps, mShow, dependents, selectedFiles.Count, totalSize, creatorMixed));
            DetailStripRefreshGeometry();
        }

        private void DetailStripRebuildMetaFields(List<DetailStripMetaField> fields)
        {
            if (_detailStripMetaRows == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float hitH = DetailStripHitHeight(s);
            float sepW = 10f * s;

            for (int ri = 0; ri < _detailStripMetaRows.Length; ri++)
            {
                if (_detailStripMetaRows[ri] != null)
                {
                    UI.DestroyAllChildren(_detailStripMetaRows[ri].transform);
                    _detailStripMetaRows[ri].SetActive(false);
                }
            }

            if (fields == null || fields.Count == 0)
            {
                DetailStripSyncMetaHostHeight(s);
                return;
            }

            if (_detailStripMetaHost != null && !_detailStripMetaHost.activeSelf)
                _detailStripMetaHost.SetActive(true);

            float avail = DetailStripEstimateMetaAvailWidth();
            _detailStripMetaAvailWidth = avail;

            var flow = new List<DetailStripMetaField>(fields.Count);
            var cluster = new List<DetailStripMetaField>(4);
            for (int i = 0; i < fields.Count; i++)
            {
                DetailStripMetaField f = fields[i];
                if (string.IsNullOrEmpty(f.Label) && string.IsNullOrEmpty(f.Value)) continue;
                if (f.Group == 1) cluster.Add(f);
                else flow.Add(f);
            }

            float[] flowWidths = new float[flow.Count];
            float flowTotal = 0f;
            for (int i = 0; i < flow.Count; i++)
            {
                flowWidths[i] = DetailStripEstimateFieldWidth(flow[i], s);
                flowTotal += flowWidths[i];
                if (i > 0) flowTotal += sepW;
            }

            // Reserve one row for deps cluster when present; use remaining for flow fields.
            int maxFlowRows = cluster.Count > 0 ? DetailStripMetaMaxRows - 1 : DetailStripMetaMaxRows;
            if (maxFlowRows < 1) maxFlowRows = 1;
            int needed = Mathf.Max(1, Mathf.CeilToInt(flowTotal / Mathf.Max(1f, avail * 0.98f)));
            int flowRowCount = Mathf.Clamp(needed, 1, maxFlowRows);
            // Prefer at least 2 rows when many fields so Version/Gender/Flags are not jammed off-screen.
            if (flow.Count >= 4 && maxFlowRows >= 2)
                flowRowCount = Mathf.Max(flowRowCount, 2);
            if (flow.Count >= 6 && maxFlowRows >= 3)
                flowRowCount = Mathf.Max(flowRowCount, 3);

            var packed = DetailStripBalancePackIndices(flowWidths, avail, sepW, flowRowCount);
            int rowIdx = 0;

            for (int r = 0; r < packed.Count && rowIdx < DetailStripMetaMaxRows; r++)
            {
                List<int> idxs = packed[r];
                if (idxs == null || idxs.Count == 0) continue;
                GameObject rowGO = _detailStripMetaRows[rowIdx++];
                if (rowGO == null) continue;
                rowGO.SetActive(true);
                DetailStripNormalizeRowRect(rowGO);
                for (int j = 0; j < idxs.Count; j++)
                {
                    if (j > 0) DetailStripAddMetaSep(rowGO, s);
                    DetailStripCreateMetaField(rowGO, flow[idxs[j]], s, hitH);
                }
            }

            if (cluster.Count > 0 && rowIdx < DetailStripMetaMaxRows)
            {
                // Always own row for deps cluster. Appending "in line" with flow facts at the
                // width fit threshold left MetaHost height short → action links painted over
                // Dependencies / Missing / Dependents.
                GameObject rowGO = _detailStripMetaRows[rowIdx++];
                if (rowGO != null)
                {
                    rowGO.SetActive(true);
                    DetailStripNormalizeRowRect(rowGO);
                    for (int j = 0; j < cluster.Count; j++)
                    {
                        if (j > 0) DetailStripAddMetaSep(rowGO, s);
                        DetailStripCreateMetaField(rowGO, cluster[j], s, hitH);
                    }
                }
            }

            DetailStripSyncMetaHostHeight(s);
            try
            {
                if (_detailStripMetaHost != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_detailStripMetaHost.GetComponent<RectTransform>());
            }
            catch { }
            try { DetailStripRebuildTextColLayout(); } catch { }
        }

        private static float DetailStripEstimateFieldWidth(DetailStripMetaField field, float s)
        {
            float charW = Mathf.Max(5f, GalleryUiDesignTokens.FontRef * 0.52f * s);
            string label = field.Label ?? "";
            string value = field.Value ?? "";
            float labelW = (label.Length + 2) * charW; // "Label: "
            float valueW = value.Length * charW;
            if (field.MaxValueWidth > 8f)
                valueW = Mathf.Min(valueW, field.MaxValueWidth);
            return Mathf.Max(36f * s, labelW + valueW);
        }

        private static float DetailStripEstimateRowWidth(List<int> idxs, float[] widths, float sepW)
        {
            if (idxs == null || idxs.Count == 0 || widths == null) return 0f;
            float w = 0f;
            for (int i = 0; i < idxs.Count; i++)
            {
                int ix = idxs[i];
                if (ix < 0 || ix >= widths.Length) continue;
                if (i > 0) w += sepW;
                w += widths[ix];
            }
            return w;
        }

        /// <summary>Split items into roughly equal-width rows (density balance).</summary>
        private static List<List<int>> DetailStripBalancePackIndices(float[] widths, float avail, float sepW, int rowCount)
        {
            var result = new List<List<int>>();
            if (widths == null || widths.Length == 0) return result;
            rowCount = Mathf.Clamp(rowCount, 1, DetailStripMetaMaxRows);
            // Hard wrap: never jam fields past avail when another meta row is free.
            var cur = new List<int>();
            float x = 0f;
            float total = 0f;
            for (int i = 0; i < widths.Length; i++)
            {
                total += widths[i];
                if (i > 0) total += sepW;
            }
            float target = total / Mathf.Max(1, rowCount);

            for (int i = 0; i < widths.Length; i++)
            {
                float add = widths[i] + (cur.Count > 0 ? sepW : 0f);
                bool underRowCap = result.Count + 1 < DetailStripMetaMaxRows;
                bool underTargetCap = result.Count + 1 < rowCount;
                bool overAvail = cur.Count > 0 && x + add > avail;
                bool overTarget = cur.Count > 0 && x + add > target && x >= target * 0.55f;
                // Always wrap on avail overflow if a row remains; also balance toward rowCount.
                if (underRowCap && (overAvail || (underTargetCap && overTarget)))
                {
                    result.Add(cur);
                    cur = new List<int>();
                    x = 0f;
                    add = widths[i];
                }
                cur.Add(i);
                x += add;
            }
            if (cur.Count > 0) result.Add(cur);
            return result;
        }

        private void DetailStripAddMetaSep(GameObject row, float s)
        {
            Text sep = UI.CreateLabel(
                row, "·", GalleryUiDesignTokens.FontRef, new Color(0.42f, 0.42f, 0.46f, 0.90f),
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "Sep");
            DetailStripApplyFont(sep, s);
            ContentSizeFitter scsf = sep.gameObject.AddComponent<ContentSizeFitter>();
            scsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            float sepHitH = DetailStripHitHeight(s);
            UI.AddLE(sep.gameObject, minHeight: sepHitH, preferredHeight: sepHitH, flexibleWidth: 0f, flexibleHeight: 0f);
        }

        /// <summary>Muted label + colored value. Soft-caps long values (author) without stretching row.</summary>
        /// <param name="rowH">Interactive row/hit height (not prose line height).</param>
        private float DetailStripCreateMetaField(GameObject row, DetailStripMetaField field, float s, float rowH)
        {
            float totalW = 0f;
            float maxValueW = field.MaxValueWidth;

            if (!string.IsNullOrEmpty(field.Label))
            {
                Text label = UI.CreateLabel(
                    row, field.Label + ": ", GalleryUiDesignTokens.FontRef, DetailStripMetaMutedColor,
                    TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                    raycastTarget: false, richText: false, name: "MetaLabel");
                DetailStripApplyFont(label, s);
                ContentSizeFitter lcsf = label.gameObject.AddComponent<ContentSizeFitter>();
                lcsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                lcsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
                UI.AddLE(label.gameObject, flexibleWidth: 0f, flexibleHeight: 0f, minHeight: rowH, preferredHeight: rowH);
                totalW += Mathf.Max(label.preferredWidth, 8f * s);
            }

            string valueText = field.Value ?? "";
            Color valueCol = field.ValueColor;
            if (!field.Enabled)
                valueCol = new Color(valueCol.r, valueCol.g, valueCol.b, 0.72f);

            if (maxValueW > 8f)
                valueText = DetailStripEllipsizeToWidth(valueText, maxValueW, s);

            Text value = UI.CreateLabel(
                row, valueText, GalleryUiDesignTokens.FontRef, valueCol,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: true, richText: false, name: "MetaValue");
            DetailStripApplyFont(value, s);
            ContentSizeFitter vcsf = value.gameObject.AddComponent<ContentSizeFitter>();
            vcsf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            vcsf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            UI.AddLE(value.gameObject, flexibleWidth: 0f, flexibleHeight: 0f, minHeight: rowH, preferredHeight: rowH);
            totalW += Mathf.Max(value.preferredWidth, 8f * s);

            string tip = field.Tip ?? valueText;
            if (maxValueW > 8f && !string.IsNullOrEmpty(field.Value) && field.Value != valueText
                && !string.IsNullOrEmpty(field.Tip))
                tip = field.Tip + "\n" + field.Value;
            AddTooltipPlain(value.gameObject, tip);
            if (field.OnClick != null && field.Enabled)
                DetailStripBindClick(value.gameObject, field.OnClick);

            if (field.Enabled)
            {
                UIHoverDelegate hover = value.gameObject.GetComponent<UIHoverDelegate>();
                if (hover == null) hover = value.gameObject.AddComponent<UIHoverDelegate>();
                Color baseCol = field.ValueColor;
                Color hoverCol = DetailStripBrighten(baseCol, 0.16f);
                hover.OnHoverChange += h =>
                {
                    if (value == null || !value.raycastTarget) return;
                    value.color = h ? hoverCol : baseCol;
                };
            }

            return totalW;
        }

        private static string DetailStripEllipsizeToWidth(string text, float maxW, float s)
        {
            if (string.IsNullOrEmpty(text) || maxW <= 8f) return text ?? "";
            // Approximate glyph width for current font scale.
            float charW = Mathf.Max(5f, GalleryUiDesignTokens.FontRef * 0.52f * s);
            int maxChars = Mathf.Max(4, Mathf.FloorToInt(maxW / charW));
            if (text.Length <= maxChars) return text;
            if (maxChars <= 1) return "…";
            return text.Substring(0, maxChars - 1) + "…";
        }

        private void DetailStripRefreshBadges(FileEntry file)
        {
            bool showAi = false, showHide = false, showScan = false, showTags = false;
            if (file != null)
            {
                try { showAi = file.IsAutoInstall(); } catch { }
                try { showHide = PackageHidePrefs.IsGalleryHideBadgeVisible(file); } catch { }
                try { showTags = IsGalleryUserTagBadgeVisible(file); } catch { }
            }
            if (_detailStripBadgeAuto != null) _detailStripBadgeAuto.SetActive(showAi);
            if (_detailStripBadgeHide != null) _detailStripBadgeHide.SetActive(showHide);
            if (_detailStripBadgeScan != null)
                showScan = ApplyScanWhitelistBadgeVisual(_detailStripBadgeScan, file);
            if (_detailStripBadgeTags != null) _detailStripBadgeTags.SetActive(showTags);
            if (_detailStripBadgeRowGO != null)
                _detailStripBadgeRowGO.SetActive(showAi || showHide || showScan || showTags);
        }

        private void DetailStripRefreshBadgesForSelection()
        {
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                DetailStripRefreshBadges(null);
                return;
            }
            if (selectedFiles.Count == 1)
            {
                DetailStripRefreshBadges(selectedFiles[0]);
                return;
            }

            // Multi: show badge if any selected item has it. Prefer temporary chrome if any temp.
            bool showAi = false, showHide = false, showScan = false, showTags = false;
            FileEntry scanSample = null;
            ScanWhitelistManager.GalleryScanWlBadgeKind scanKind = ScanWhitelistManager.GalleryScanWlBadgeKind.None;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                try { if (!showAi && f.IsAutoInstall()) showAi = true; } catch { }
                try { if (!showHide && PackageHidePrefs.IsGalleryHideBadgeVisible(f)) showHide = true; } catch { }
                try
                {
                    var k = ScanWhitelistManager.GetGalleryScanWhitelistBadgeKind(f);
                    if (k != ScanWhitelistManager.GalleryScanWlBadgeKind.None)
                    {
                        showScan = true;
                        if (scanKind != ScanWhitelistManager.GalleryScanWlBadgeKind.Temporary)
                        {
                            scanKind = k;
                            scanSample = f;
                        }
                    }
                }
                catch { }
                try { if (!showTags && IsGalleryUserTagBadgeVisible(f)) showTags = true; } catch { }
                if (showAi && showHide && showScan && showTags
                    && scanKind == ScanWhitelistManager.GalleryScanWlBadgeKind.Temporary)
                    break;
            }
            if (_detailStripBadgeAuto != null) _detailStripBadgeAuto.SetActive(showAi);
            if (_detailStripBadgeHide != null) _detailStripBadgeHide.SetActive(showHide);
            if (_detailStripBadgeScan != null)
            {
                if (showScan)
                    ApplyScanWhitelistBadgeVisual(_detailStripBadgeScan, scanSample);
                else
                    _detailStripBadgeScan.SetActive(false);
            }
            if (_detailStripBadgeTags != null) _detailStripBadgeTags.SetActive(showTags);
            if (_detailStripBadgeRowGO != null)
                _detailStripBadgeRowGO.SetActive(showAi || showHide || showScan || showTags);
        }

        /// <summary>
        /// Full-res decode tier (same as hover preview). Optional keepCurrentUntilReady avoids
        /// blank flash while upgrading the <em>same</em> file's grid copy to hi-res.
        /// Never keep another item's texture — that mislabels the current selection.
        /// </summary>
        private void DetailStripLoadThumb(FileEntry file, bool keepCurrentUntilReady = false)
        {
            if (_detailStripThumb == null || file == null) return;
            Texture keepTex = null;
            Rect keepUv = new Rect(0f, 0f, 1f, 1f);
            bool sameFile = ReferenceEquals(_detailStripThumbFile, file);
            if (keepCurrentUntilReady && sameFile && _detailStripThumb.texture != null)
            {
                keepTex = _detailStripThumb.texture;
                keepUv = _detailStripThumb.uvRect;
            }
            _detailStripThumbFile = file;
            _detailStripThumb.color = Color.white;
            try
            {
                // Same path as hover preview: not grid context, denom 1, Unity decode tier.
                LoadThumbnail(
                    file,
                    _detailStripThumb,
                    gridThumbnailContext: false,
                    turboJpegThumbnailDenom: 1,
                    thumbnailUnityDecodeOnly: true);
                // LoadThumbnail blanks while queued — restore same-file placeholder until callback.
                if (keepTex != null && _detailStripThumb.texture == null)
                {
                    _detailStripThumb.texture = keepTex;
                    _detailStripThumb.uvRect = keepUv;
                    _detailStripThumb.color = Color.white;
                }
            }
            catch
            {
                if (keepTex != null)
                {
                    _detailStripThumb.texture = keepTex;
                    _detailStripThumb.uvRect = keepUv;
                    _detailStripThumb.color = Color.white;
                }
                else
                    DetailStripClearThumbPreview();
            }
        }

        /// <summary>Empty preview chrome for current item (no leftover prior texture).</summary>
        private void DetailStripClearThumbPreview()
        {
            if (_detailStripThumb == null) return;
            try { ClearThumbnailTarget(_detailStripThumb); }
            catch
            {
                _detailStripThumb.texture = null;
                _detailStripThumb.uvRect = new Rect(0f, 0f, 1f, 1f);
                _detailStripThumb.color = new Color(1f, 1f, 1f, 0.15f);
            }
        }

        /// <summary>Mouse wheel over strip thumb → previous/next item in current filtered list.</summary>
        private void DetailStripOnThumbScroll(float scrollDelta)
        {
            if (Mathf.Abs(scrollDelta) < 0.01f) return;
            // Unity scroll up is positive → previous item (matches typical list feel).
            int step = scrollDelta > 0f ? -1 : 1;
            DetailStripThumbScrubBy(step);
        }

        /// <summary>Shared scrub step for wheel + overlay ◀▶. Warm path; no per-frame alloc.</summary>
        private void DetailStripThumbScrubBy(int step)
        {
            if (step == 0) return;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;
            DetailStripBeginScrubHeightLock();
            _detailStripScrubPendingSteps += step;
            _detailStripScrubLastInputTime = Time.unscaledTime;
            _detailStripScrubActive = true;
            DetailStripProcessScrubPending();
            DetailStripSyncScrubIndexOverlay();
            DetailStripSyncThumbNavChrome();
        }

        /// <summary>Called from Update: soft-fill meta after scrub wheel stops (height stays locked).</summary>
        private void DetailStripScrubTick()
        {
            if (!_detailStripScrubActive && !_detailStripScrubHeightLocked) return;
            if (_detailStripScrubActive)
            {
                DetailStripProcessScrubPending();
                if (_detailStripScrubPendingSteps != 0) return;
                if (Time.unscaledTime - _detailStripScrubLastInputTime < DetailStripScrubCommitDelaySec)
                    return;
                DetailStripCommitScrub();
                DetailStripSyncScrubIndexOverlay();
                return;
            }
            // Idle after scrub: drop height lock so normal refresh can run again (no click needed).
            if (_detailStripScrubHeightLocked
                && Time.unscaledTime - _detailStripScrubLastInputTime > 0.85f)
                DetailStripEndScrubHeightLock();
        }

        private void DetailStripProcessScrubPending()
        {
            int steps = _detailStripScrubPendingSteps;
            if (steps == 0) return;
            _detailStripScrubPendingSteps = 0;
            DetailStripNudgeSelectionFast(steps);
        }

        private void DetailStripNudgeSelectionFast(int step)
        {
            if (step == 0) return;
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;

            bool historyBrowse = activeContentType == ContentType.History;
            int count = currentFilteredFiles.Count;
            int currentIndex = _detailStripScrubIndex;
            if (currentIndex < 0 || currentIndex >= count)
            {
                string navKey = GetCurrentSelectionAnchorIdentityKey(historyBrowse);
                if (string.IsNullOrEmpty(navKey) && selectedFiles != null && selectedFiles.Count > 0)
                    navKey = GetSelectionIdentityKey(selectedFiles[0], historyBrowse);
                if (!string.IsNullOrEmpty(navKey))
                    currentIndex = FindIndexBySelectionIdentity(currentFilteredFiles, navKey, historyBrowse);
                if (currentIndex < 0) currentIndex = 0;
            }

            int newIndex = Mathf.Clamp(currentIndex + step, 0, count - 1);
            _detailStripScrubIndex = newIndex;
            if (newIndex == currentIndex && selectedFiles != null && selectedFiles.Count == 1)
            {
                DetailStripSyncScrubIndexOverlay();
                DetailStripSyncThumbNavChrome();
                return;
            }

            FileEntry newFile = currentFilteredFiles[newIndex];
            if (newFile == null) return;

            selectedFiles.Clear();
            selectedFilePaths.Clear();
            AddFileToSelection(newFile, historyBrowse);
            SetSelectionAnchor(newFile, historyBrowse);
            selectedPath = historyBrowse ? GetSelectionIdentityKey(newFile, true) : newFile.Path;

            // Scroll cell into view first so grid Thumbnail is bound — then copy (no LoadThumbnail blank).
            if (recyclingGrid != null) recyclingGrid.EnsureItemVisible(newIndex);
            DetailStripApplyScrubPreview(newFile, newIndex);
            RefreshSelectionVisualsCore(runHeavySideEffects: false);
            DetailStripSyncScrubIndexOverlay();
            DetailStripSyncThumbNavChrome();
        }

        private void DetailStripApplyScrubPreview(FileEntry file, int listIndex)
        {
            if (file == null) return;
            // Do not DetailStripEnsure() here — needsRebuild destroy blanks whole strip mid-scrub.
            if (_detailStripGO == null)
            {
                DetailStripEnsure();
                if (_detailStripGO == null) return;
            }
            // Collapsed: selection already nudged; keep strip hidden (scrub from Details button).
            if (!DetailStripIsExpanded())
                return;
            if (!_detailStripGO.activeSelf) _detailStripGO.SetActive(true);

            _detailStripBoundFile = file;
            // Fast path: title + thumb only. Never clear other fields; never async-load (blanks texture).
            if (_detailStripTitle != null)
            {
                string name = GetGalleryListRowDisplayName(file);
                if (!string.Equals(_detailStripTitle.text, name, StringComparison.Ordinal))
                    _detailStripTitle.text = name;
            }
            // No grid thumb / empty cell → clear prior item preview (do not mislead).
            if (!DetailStripTryCopyThumbFromVisibleGrid(file, listIndex))
            {
                DetailStripClearThumbPreview();
                _detailStripThumbFile = file;
            }
        }

        private bool DetailStripTryCopyThumbFromVisibleGrid(FileEntry file, int listIndex)
        {
            if (file == null || _detailStripThumb == null || recyclingGrid == null)
                return false;
            try
            {
                int n = recyclingGrid.ActiveItemCount;
                for (int i = 0; i < n; i++)
                {
                    RecyclingGridItem rgv = recyclingGrid.GetActiveItemAt(i);
                    if (rgv == null || rgv.gameObject == null || !rgv.gameObject.activeSelf) continue;
                    if (listIndex >= 0 && rgv.index != listIndex) continue;
                    FileButtonBinder binder = rgv.binder;
                    if (binder == null) binder = FileButtonBinder.GetOrAdd(rgv.gameObject);
                    if (listIndex < 0)
                    {
                        UIDraggableItem diag = binder != null ? binder.draggable : null;
                        if (diag == null || !ReferenceEquals(diag.FileEntry, file)) continue;
                    }
                    RawImage ri = binder != null ? binder.thumbRaw : null;
                    // Cell found but empty → treat as no preview for this item (caller clears).
                    if (ri == null || ri == _detailStripThumb) continue;
                    if (ri.texture == null) return false;
                    _detailStripThumb.texture = ri.texture;
                    _detailStripThumb.uvRect = ri.uvRect;
                    _detailStripThumb.color = Color.white;
                    _detailStripThumbFile = file;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private void DetailStripCommitScrub()
        {
            // Keep height lock; only clear "active wheel" so enrich can run. Full strip rebuild still blocked.
            _detailStripScrubActive = false;
            _detailStripScrubPendingSteps = 0;
            FileEntry file = _detailStripBoundFile;
            if (file == null && selectedFiles != null && selectedFiles.Count > 0)
                file = selectedFiles[0];

            // Collapsed scrub (Details button): selection already moved — skip strip enrich.
            if (!DetailStripIsExpanded())
            {
                try { UpdatePaginationText(); } catch { }
                try { RefreshSelectionVisualsCore(runHeavySideEffects: false); } catch { }
                // Heavy path skipped during scrub spin — refresh side-rail applied/avail now.
                try { RefreshUserTagsSideRailAfterScrubSelection(); } catch { }
                // Same settle point as expanded scrub: import source must track preview scroll.
                try { TryLoadSelectedSceneIntoImportSidebar(); } catch { }
                DetailStripEndScrubHeightLock();
                return;
            }

            // Enrich when idle: patch existing texts in place (no destroy / SetActive).
            try { DetailStripEnrichScrubFields(file); } catch { }
            // Upgrade scrub's grid-copy thumb → hover-quality decode (keep grid tex until ready).
            try { DetailStripLoadThumb(file, keepCurrentUntilReady: true); } catch { }
            // Side meta (desc/native/promo) skipped during scrub spin + sameKey — hydrate now.
            try
            {
                _detailStripSideContentKey = "";
                DetailStripRefreshDescription(file);
                DetailStripRefreshSideContent(file);
                DetailStripRefreshTagsLineForPlacement();
                float s = ChromeScale;
                if (s <= 0f) s = 1f;
                DetailStripSyncSideColumn(s);
            }
            catch { }
            try { UpdatePaginationText(); } catch { }
            try { RefreshSelectionVisualsCore(runHeavySideEffects: false); } catch { }
            // Heavy path skipped during scrub spin — refresh side-rail applied/avail now.
            try { RefreshUserTagsSideRailAfterScrubSelection(); } catch { }
            // Defer LoadSourceScene to scrub settle (warm path) — avoid cancel/reparse per wheel tick.
            try { TryLoadSelectedSceneIntoImportSidebar(); } catch { }
        }

        /// <summary>
        /// Scrub uses runHeavySideEffects:false for scroll perf. On commit, sync UserTags
        /// side-rail applied list (and avail selection chrome) without full DetailStripRefresh.
        /// </summary>
        private void RefreshUserTagsSideRailAfterScrubSelection()
        {
            userTagAppliedRemoveSelection.Clear();
            userTagAppliedRemoveAnchor = null;
            updatePanelForSelection();
            try { DetailStripSyncOpenTagMenuIfSelectionChanged(); } catch { }
        }

        /// <summary>
        /// Smooth scrub enrich: rewrite visible field strings + meta values in place.
        /// Never DestroyAllChildren, never blank thumb, never toggle row active state.
        /// </summary>
        private void DetailStripEnrichScrubFields(FileEntry file)
        {
            if (file == null || _detailStripGO == null) return;
            _detailStripBoundFile = file;
            _detailStripBoundCreator = DetailStripResolveCreator(file);

            string name = GetGalleryListRowDisplayName(file);
            if (_detailStripTitle != null && !string.Equals(_detailStripTitle.text, name, StringComparison.Ordinal))
                _detailStripTitle.text = name;

            // Stars: paint only (no SetActive — avoids one-frame blank).
            try
            {
                int rating = 0;
                try { rating = TboxConsensusRatingDisplay(selectedFiles); } catch { }
                _detailStripStarRating = Mathf.Clamp(rating, 0, 5);
                _detailStripStarHover = 0;
                DetailStripPaintStars();
            }
            catch { }

            // Meta values: patch by label match (no row rebuild).
            try { DetailStripPatchMetaValuesInPlace(file); } catch { }

            if (_detailStripTags != null && DetailStripFlexLineVisible(_detailStripTags))
            {
                HashSet<string> tags = DetailStripCollectUserTags(file);
                string emptyNote = (tags == null || tags.Count == 0)
                    ? VPBTranslation.T("gallery.detail.no_tags", "none")
                    : null;
                DetailStripRebuildTagChips(tags, emptyNote, force: false);
            }
            if (_detailStripPath != null && DetailStripFlexLineVisible(_detailStripPath))
            {
                string path = DetailStripResolvePathLine(file);
                if (!string.IsNullOrEmpty(path)
                    && !string.Equals(_detailStripPath.text, path, StringComparison.Ordinal))
                    _detailStripPath.text = path;
            }
            if (_detailStripPackageTags != null && DetailStripFlexLineVisible(_detailStripPackageTags))
            {
                string nativeFmt = DetailStripFormatTagNames(DetailStripCollectNativeTags(file));
                string shown = !string.IsNullOrEmpty(nativeFmt)
                    ? string.Format(VPBTranslation.T("gallery.detail.native_tags_fmt", "Package tags: {0}"), nativeFmt)
                    : "";
                if (!string.Equals(_detailStripPackageTags.text, shown, StringComparison.Ordinal))
                    _detailStripPackageTags.text = shown;
            }
            if (_detailStripDesc != null && DetailStripFlexLineVisible(_detailStripDesc))
            {
                string desc = DetailStripResolveDescription(file);
                if (!string.IsNullOrEmpty(desc))
                {
                    float s = ChromeScale;
                    if (s <= 0f) s = 1f;
                    int lines = DetailStripLeftDescCurrentLines(s);
                    if (lines < 1) lines = 1;
                    string shown = DetailStripEllipsizeToLines(
                        desc, DetailStripEstimateMetaAvailWidth(), s, lines);
                    if (!string.Equals(_detailStripDesc.text, shown, StringComparison.Ordinal))
                        _detailStripDesc.text = shown;
                }
                else
                    _detailStripDesc.text = "";
            }
            // Side column lite patch during scrub spin; full hydrate on CommitScrub.
            try
            {
                if (_detailStripSideVisible)
                {
                    string desc = DetailStripResolveDescription(file);
                    if (_detailStripSideDesc != null)
                    {
                        if (!string.IsNullOrEmpty(desc))
                        {
                            if (!string.Equals(_detailStripSideDesc.text, desc, StringComparison.Ordinal))
                                _detailStripSideDesc.text = desc;
                            if (_detailStripSideDescScrollGO != null)
                                _detailStripSideDescScrollGO.SetActive(true);
                            _detailStripWantDesc = true;
                        }
                        else
                        {
                            _detailStripSideDesc.text = "";
                            if (_detailStripSideDescScrollGO != null)
                                _detailStripSideDescScrollGO.SetActive(false);
                            _detailStripWantDesc = false;
                        }
                    }
                    if (_detailStripSideNativeTags != null)
                    {
                        string nativeFmt = DetailStripFormatTagNames(DetailStripCollectNativeTags(file));
                        if (!string.IsNullOrEmpty(nativeFmt))
                        {
                            string shown = string.Format(
                                VPBTranslation.T("gallery.detail.native_tags_fmt", "Package tags: {0}"),
                                nativeFmt);
                            if (!string.Equals(_detailStripSideNativeTags.text, shown, StringComparison.Ordinal))
                                _detailStripSideNativeTags.text = shown;
                            _detailStripSideNativeTags.gameObject.SetActive(true);
                            _detailStripWantNativeTags = true;
                        }
                        else
                        {
                            _detailStripSideNativeTags.text = "";
                            _detailStripSideNativeTags.gameObject.SetActive(false);
                            _detailStripWantNativeTags = false;
                        }
                    }
                }
            }
            catch { }

            // Thumb upgrade happens in CommitScrub via DetailStripLoadThumb (hi-res).

            _detailStripCacheKey = BuildDetailStripCacheKey();
            if (_detailStripScrubHeightLocked && _detailStripScrubLockedHeight > 8f)
                _detailStripMeasuredHeight = _detailStripScrubLockedHeight;
        }

        /// <summary>Update MetaValue texts under matching MetaLabel keys — no destroy/rebuild.</summary>
        private void DetailStripPatchMetaValuesInPlace(FileEntry file)
        {
            if (file == null || _detailStripMetaRows == null) return;
            int deps = 0, missing = 0, dependents = 0;
            try { deps = GallerySortManager.GetDepsCount(file); } catch { }
            try { missing = GallerySortManager.GetMissingDepsCount(file); } catch { }
            try { dependents = GallerySortManager.GetDependentsCount(file); } catch { }
            List<DetailStripMetaField> fields = DetailStripCollectMetaFields(
                file, deps, missing, dependents, -1, -1, false);
            if (fields == null || fields.Count == 0) return;

            var byLabel = new Dictionary<string, DetailStripMetaField>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fields.Count; i++)
            {
                DetailStripMetaField f = fields[i];
                if (string.IsNullOrEmpty(f.Label)) continue;
                byLabel[f.Label] = f;
            }

            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            for (int ri = 0; ri < _detailStripMetaRows.Length; ri++)
            {
                GameObject row = _detailStripMetaRows[ri];
                if (row == null || !row.activeSelf) continue;
                Transform t = row.transform;
                for (int ci = 0; ci < t.childCount; ci++)
                {
                    Transform c = t.GetChild(ci);
                    if (c == null || c.name != "MetaLabel") continue;
                    Text labelT = c.GetComponent<Text>();
                    if (labelT == null) continue;
                    string lab = labelT.text ?? "";
                    if (lab.EndsWith(": ", StringComparison.Ordinal)) lab = lab.Substring(0, lab.Length - 2);
                    else if (lab.EndsWith(":", StringComparison.Ordinal)) lab = lab.Substring(0, lab.Length - 1);
                    lab = lab.Trim();
                    DetailStripMetaField field;
                    if (!byLabel.TryGetValue(lab, out field)) continue;

                    Text valueT = null;
                    if (ci + 1 < t.childCount)
                    {
                        Transform vt = t.GetChild(ci + 1);
                        if (vt != null && vt.name == "MetaValue")
                            valueT = vt.GetComponent<Text>();
                    }
                    if (valueT == null) continue;

                    string valueText = field.Value ?? "";
                    if (field.MaxValueWidth > 8f)
                        valueText = DetailStripEllipsizeToWidth(valueText, field.MaxValueWidth, s);
                    Color valueCol = field.ValueColor;
                    if (!field.Enabled)
                        valueCol = new Color(valueCol.r, valueCol.g, valueCol.b, 0.72f);
                    if (!string.Equals(valueT.text, valueText, StringComparison.Ordinal))
                        valueT.text = valueText;
                    valueT.color = valueCol;

                    // Scrub patch used to update text only — tip/click stayed on prior item
                    // (e.g. "No dependents" + no filter while counter shows 2).
                    string tip = field.Tip ?? valueText;
                    if (field.MaxValueWidth > 8f && !string.IsNullOrEmpty(field.Value) && field.Value != valueText
                        && !string.IsNullOrEmpty(field.Tip))
                        tip = field.Tip + "\n" + field.Value;
                    AddTooltipPlain(valueT.gameObject, tip);
                    if (field.OnClick != null && field.Enabled)
                        DetailStripBindClick(valueT.gameObject, field.OnClick);
                    else
                        DetailStripUnbindClick(valueT.gameObject);
                }
            }
        }

        /// <summary>Unlock scrub height so normal click/keyboard selection can resize strip again.</summary>
        private void DetailStripUnlockAfterExternalSelectionChange()
        {
            if (!_detailStripScrubHeightLocked && !_detailStripScrubActive) return;
            _detailStripScrubActive = false;
            _detailStripScrubPendingSteps = 0;
            DetailStripEndScrubHeightLock();
            _detailStripCacheKey = "";
        }

        // ── Clicks ────────────────────────────────────────────────────────────

        private void DetailStripOnTitleClick()
        {
            FileEntry f = _detailStripBoundFile;
            if (f == null && selectedFiles != null && selectedFiles.Count > 0) f = selectedFiles[0];
            if (f == null) return;
            string name = GetGalleryListRowDisplayName(f);
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                GUIUtility.systemCopyBuffer = name;
                ShowTemporaryStatus(VPBTranslation.T("gallery.detail.copied_name", "Copied name"), 1.5f);
            }
            catch { }
        }

        private void DetailStripOnDepsClick()
        {
            FileEntry f = _detailStripBoundFile;
            if (f == null) return;
            try
            {
                if (GallerySortManager.GetDepsCount(f) <= 0)
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.detail.no_deps", "No dependencies"), 1.5f);
                    return;
                }
                ApplyDependenciesFilter(f);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip deps: " + ex.Message); }
        }

        private void DetailStripOnMissingClick()
        {
            FileEntry f = _detailStripBoundFile;
            if (f == null) return;
            try
            {
                if (GallerySortManager.GetMissingDepsCount(f) <= 0)
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.detail.no_missing", "No missing dependencies"), 1.5f);
                    return;
                }
                ApplyMissingDependenciesFilter(f);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip missing: " + ex.Message); }
        }

        private void DetailStripOnDependentsClick()
        {
            FileEntry f = _detailStripBoundFile;
            if (f == null) return;
            try
            {
                if (GallerySortManager.GetDependentsCount(f) <= 0)
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.detail.no_dependents", "No dependents"), 1.5f);
                    return;
                }
                ApplyDependentsFilter(f);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip dependents: " + ex.Message); }
        }

        private void DetailStripOnCreatorClick()
        {
            if (string.IsNullOrEmpty(_detailStripBoundCreator)) return;
            try
            {
                ToggleCreatorFilter(_detailStripBoundCreator);
                OnCreatorFilterChanged(refreshFilesAndTabs: true);
                ShowTemporaryStatus(string.Format(
                    VPBTranslation.T("gallery.detail.creator_filtered", "Creator filter: {0}"),
                    _detailStripBoundCreator), 1.8f);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip creator: " + ex.Message); }
        }

        private void DetailStripOnLicenseClick(string license)
        {
            if (string.IsNullOrEmpty(license)) return;
            try
            {
                bool clearing = HasLicenseFilter()
                    && string.Equals(currentLicenseFilter, license, StringComparison.OrdinalIgnoreCase);
                SetLicenseFilter(license, refresh: true);
                if (clearing)
                {
                    ShowTemporaryStatus(
                        VPBTranslation.T("gallery.detail.license_filter_cleared", "License filter cleared"),
                        1.5f);
                }
                else
                {
                    ShowTemporaryStatus(string.Format(
                        VPBTranslation.T("gallery.detail.license_filtered", "License filter: {0}"),
                        license), 1.8f);
                }
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip license: " + ex.Message); }
        }

        private void DetailStripOnCopyClick()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            var lines = new List<string>(selectedFiles.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                string path = DetailStripResolvePathLine(f);
                if (string.IsNullOrEmpty(path)) path = f.Path ?? f.Uid ?? f.Name ?? "";
                if (string.IsNullOrEmpty(path)) continue;
                if (!seen.Add(path)) continue;
                lines.Add(path);
            }
            if (lines.Count == 0) return;
            try
            {
                GUIUtility.systemCopyBuffer = string.Join("\n", lines.ToArray());
                ShowTemporaryStatus(
                    lines.Count == 1
                        ? VPBTranslation.T("gallery.detail.copied_path", "Copied path")
                        : string.Format(VPBTranslation.T("gallery.detail.copied_paths_n", "Copied {0} paths"), lines.Count),
                    1.5f);
            }
            catch { }
        }

        private void DetailStripOnHubClick()
        {
            try { TboxOpenSelectedItemOnHub(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip hub: " + ex.Message); }
        }

        private void DetailStripOnCacheClick()
        {
            try { TboxCacheTexturesSelected(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip cache: " + ex.Message); }
        }

        private void DetailStripOnDeleteClick()
        {
            try { TboxDeleteSelectedPackages(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip delete: " + ex.Message); }
        }

        private void DetailStripOnAutoLoadClick()
        {
            try { TboxAutoInstallSelectedPackages(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip AutoLoad: " + ex.Message); }
            try
            {
                bool on = selectedFiles != null && selectedFiles.Count > 0;
                DetailStripRefreshAutoLoadLinks(on);
                DetailStripRefreshBadgesForSelection();
            }
            catch { }
        }

        private void DetailStripOnNoAutoLoadClick()
        {
            try { TboxDisableAutoInstallSelectedPackages(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip No AutoLoad: " + ex.Message); }
            try
            {
                bool on = selectedFiles != null && selectedFiles.Count > 0;
                DetailStripRefreshAutoLoadLinks(on);
                DetailStripRefreshBadgesForSelection();
            }
            catch { }
        }

        private void DetailStripOnHideClick()
        {
            try { TboxHideSelectedPackages(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip hide: " + ex.Message); }
            try
            {
                DetailStripRefreshHideLinks(selectedFiles != null && selectedFiles.Count > 0);
                DetailStripRefreshBadgesForSelection();
            }
            catch { }
        }

        private void DetailStripOnUnhideClick()
        {
            try { TboxUnhideSelectedPackages(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip unhide: " + ex.Message); }
            try
            {
                DetailStripRefreshHideLinks(selectedFiles != null && selectedFiles.Count > 0);
                DetailStripRefreshBadgesForSelection();
            }
            catch { }
        }

        private void DetailStripOnTempWlClick()
        {
            try { TboxScanWhitelistTemporaryForSelection(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip temp WL: " + ex.Message); }
        }

        private void DetailStripOnCleanupOldVersionsClick()
        {
            try
            {
                List<string> older = DetailStripCollectOlderSiblingUids(null);
                if (older == null || older.Count == 0)
                {
                    ShowTemporaryStatus(VPBTranslation.T("gallery.detail.old_vers_none", "No older versions in selection groups."), 2f);
                    return;
                }

                string currentScenePkg = null;
                try { currentScenePkg = VamHookPlugin.CurrentScenePackageUid; } catch { }
                HashSet<string> runningSceneDeps = TryGetRunningSceneDependenciesFast();
                var olderSet = new HashSet<string>(older, StringComparer.OrdinalIgnoreCase);
                var blocked = new List<string>();
                var warned = new List<string>();
                var toDelete = new List<string>();
                ClassifyUidsForTboxDelete(olderSet, currentScenePkg, runningSceneDeps, blocked, warned, toDelete);
                if (toDelete.Count == 0)
                {
                    ShowTemporaryStatus(
                        blocked.Count > 0
                            ? VPBTranslation.T("gallery.detail.old_vers_blocked", "Older versions blocked from delete.")
                            : VPBTranslation.T("gallery.detail.old_vers_none", "No older versions in selection groups."),
                        2f);
                    return;
                }

                string msg = string.Format(
                    VPBTranslation.T(
                        "gallery.detail.old_vers_confirm",
                        "Move {0} older package version(s) into DeletedPackages/OldVersions?\n\nNewest version kept."),
                    toDelete.Count);
                if (blocked.Count > 0)
                {
                    var blockedShow = new List<string>();
                    for (int bi = 0; bi < blocked.Count && blockedShow.Count < 8; bi++)
                    {
                        if (!string.IsNullOrEmpty(blocked[bi]) && !blockedShow.Contains(blocked[bi]))
                            blockedShow.Add(blocked[bi]);
                    }
                    msg += "\n\nBlocked: " + string.Join(", ", blockedShow.ToArray());
                }

                DisplayConfirm(
                    VPBTranslation.T("gallery.detail.old_vers_title", "Cleanup old versions"),
                    msg,
                    () =>
                    {
                        string baseDir = Directory.GetCurrentDirectory();
                        string deletedPkgDir = Path.Combine(Path.Combine(baseDir, DeletedPackagesFolderName), "OldVersions");
                        EnsureDeletedPackagesDirectory(deletedPkgDir);
                        int moved, failed;
                        var undoPairs = new List<FileMoveUndoPair>();
                        PerformDeleteMove(toDelete, deletedPkgDir, out moved, out failed, undoPairs);
                        try
                        {
                            PushFileMoveUndo(
                                undoPairs,
                                VPBTranslation.T("gallery.undo.delete_old_versions", "Delete old versions"),
                                "tbox_undo_delete_old_versions");
                        }
                        catch { }
                        ShowTemporaryStatus(
                            failed > 0
                                ? string.Format("Moved {0}, failed {1}.", moved, failed)
                                : string.Format("Moved {0} old version(s).", moved),
                            2.5f);
                        try { DetailStripRefreshOldVersLink(selectedFiles != null && selectedFiles.Count > 0); } catch { }
                        try { if (recyclingGrid != null) recyclingGrid.Refresh(); } catch { }
                    });
            }
            catch (Exception ex)
            {
                LogUtil.LogError("[VPB] DetailStrip old vers: " + ex.Message);
                ShowTemporaryStatus("Cleanup old versions failed. See log.", 2f);
            }
        }

        private void DetailStripOnTagClick()
        {
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            try
            {
                DetailStripCloseTagFilterPopup();
                DetailStripToggleTagMenu();
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip quick tag: " + ex.Message); }
        }

        /// <summary>Toggle gallery user-tag filter (include on/off). Keeps current F/T work mode — filter sets are orthogonal.</summary>
        private void DetailStripOnTagFilterClick(string tagName)
        {
            if (string.IsNullOrEmpty(tagName)) return;
            try
            {
                string norm = tagName.Trim();
                if (string.IsNullOrEmpty(norm)) return;
                norm = VpbLocalDatabase.NormalizeGalleryUserTagName(norm);
                if (string.IsNullOrEmpty(norm)) return;

                // Exit Not-tagged browse if armed; restore prior F/T work mode without forcing Filter mode.
                if (_userTagAvailMode == UserTagAvailMode.FilterUntagged)
                {
                    try { ClearUntaggedTaggedPinKeys(); } catch { }
                    UserTagAvailMode restore = _userTagModeBeforeUntagged == UserTagAvailMode.Tag
                        ? UserTagAvailMode.Tag
                        : UserTagAvailMode.FilterByTags;
                    _userTagAvailMode = restore;
                    try { SyncUserTagFilterModeToggleVisualsEverywhere(); } catch { }
                }

                bool nowOn;
                if (activeUserTags != null && activeUserTags.Contains(norm))
                {
                    activeUserTags.Remove(norm);
                    nowOn = false;
                }
                else
                {
                    if (activeUserTags == null) return;
                    activeUserTags.Add(norm);
                    if (excludedUserTags != null) excludedUserTags.Remove(norm);
                    nowOn = true;
                }

                try { BridgeTitleSearchTagChipFromFilterSet(norm); } catch { }

                try { RefreshFilesAndTabs(); } catch { try { RefreshFiles(true, false, false, "detail_strip_tag_filter"); } catch { } }
                try { SyncBrowseFilterChipChrome(); } catch { }
                try { RefreshUserTagsAvailPaneInPlace(true); } catch { }
                try { RefreshUserTagsAvailPaneInPlace(false); } catch { }
                // Force chip recolor for active filter state.
                _detailStripTagsContentKey = "";
                try { DetailStripRefreshTagsLineForPlacement(); } catch { }

                ShowTemporaryStatus(string.Format(
                    nowOn
                        ? VPBTranslation.T("gallery.detail.tag_filtered", "Tag filter: {0}")
                        : VPBTranslation.T("gallery.detail.tag_filter_cleared", "Tag filter cleared: {0}"),
                    norm), 1.8f);
            }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip tag filter: " + ex.Message); }
        }

        private void DetailStripRebuildTagChips(HashSet<string> tags, string note, bool force, bool showStampFromFirst = false)
        {
            if (_detailStripTags == null) return;
            _detailStripTags.text = VPBTranslation.T("gallery.detail.set_tags", "Set Tags: ");
            _detailStripTags.color = DetailStripColorTag;

            var list = DetailStripOrderTagsWithSession(tags);
            _detailStripBoundTagNames = list;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float hitH = DetailStripHitHeight(s);
            float avail = DetailStripEstimateTagChipsAvailWidth(s);
            float sepW = DetailStripEstimateChipTextWidth(", ", s);
            float noteW = !string.IsNullOrEmpty(note)
                ? DetailStripEstimateChipTextWidth(" · " + note, s)
                : 0f;
            string stampLabel = showStampFromFirst
                ? VPBTranslation.T("gallery.detail.stamp_from_first", "Stamp from first")
                : null;
            float stampW = !string.IsNullOrEmpty(stampLabel)
                ? DetailStripEstimateChipTextWidth(" · " + stampLabel, s)
                : 0f;
            float moreUnit = DetailStripEstimateChipTextWidth("+99", s) + sepW;

            // Progressive disclosure: pack prefix that fits; rest in "+N" filter popup.
            var shown = new List<string>(Math.Min(list.Count, DetailStripMaxTagsInlineHard));
            float used = 0f;
            for (int i = 0; i < list.Count && shown.Count < DetailStripMaxTagsInlineHard; i++)
            {
                string tag = list[i];
                float tw = DetailStripEstimateChipTextWidth(tag, s);
                float chipMax = Mathf.Max(48f * s, avail * 0.38f);
                if (tw > chipMax) tw = chipMax;

                float add = (shown.Count > 0 ? sepW : 0f) + tw;
                int remainIfTake = list.Count - (i + 1);
                float reserve = noteW + stampW + (remainIfTake > 0 || shown.Count + 1 < list.Count ? moreUnit : 0f);
                if (shown.Count > 0 && used + add > avail - reserve)
                    break;

                shown.Add(tag);
                used += add;
            }
            if (shown.Count == 0 && list.Count > 0)
                shown.Add(list[0]);
            int overflow = Math.Max(0, list.Count - shown.Count);

            var keySb = new StringBuilder(64);
            keySb.Append("a:").Append(Mathf.RoundToInt(avail));
            for (int i = 0; i < shown.Count; i++)
            {
                if (i > 0) keySb.Append(',');
                keySb.Append(shown[i]);
                bool active = activeUserTags != null && activeUserTags.Contains(shown[i]);
                keySb.Append(active ? "=1" : "=0");
            }
            if (overflow > 0) keySb.Append("|+").Append(overflow);
            if (!string.IsNullOrEmpty(note)) keySb.Append("|n:").Append(note);
            if (showStampFromFirst) keySb.Append("|stamp");
            if (UserTagClipboardHasTags()) keySb.Append("|cb:").Append(UserTagClipboardTagCount());
            string key = keySb.ToString();
            if (!force && string.Equals(key, _detailStripTagsContentKey, StringComparison.Ordinal))
                return;
            _detailStripTagsContentKey = key;

            if (_detailStripTagsChipsHost == null) return;
            UI.DestroyAllChildren(_detailStripTagsChipsHost.transform);

            float chipMaxW = Mathf.Max(48f * s, avail * 0.38f);
            for (int i = 0; i < shown.Count; i++)
            {
                if (i > 0)
                    DetailStripAddTagChipSep(_detailStripTagsChipsHost, ", ", s, hitH);

                string tagSnap = shown[i];
                bool filterOn = activeUserTags != null && activeUserTags.Contains(tagSnap);
                Color idle = filterOn ? UserTagFilterActiveColor : DetailStripColorTag;
                string display = tagSnap;
                if (DetailStripEstimateChipTextWidth(display, s) > chipMaxW)
                    display = DetailStripEllipsizeToWidth(display, chipMaxW, s);

                Text chip = DetailStripCreateTagChipLabel(
                    _detailStripTagsChipsHost, display, idle, s, hitH, clickable: true, name: "TagChip");
                AddTooltipPlain(chip.gameObject, string.Format(
                    VPBTranslation.T("gallery.detail.tip.tag_filter_fmt", "Filter the gallery by tag {0}"),
                    tagSnap)
                    + "\n"
                    + VPBTranslation.T("gallery.detail.tip.tag_chip_drag", "Drag: apply tag to gallery item"));
                string filterSnap = tagSnap;
                UserTagPickDragSource chipDrag = chip.gameObject.GetComponent<UserTagPickDragSource>();
                if (chipDrag == null) chipDrag = chip.gameObject.AddComponent<UserTagPickDragSource>();
                chipDrag.Panel = this;
                chipDrag.PrimaryTag = tagSnap;
                chipDrag.IsAppliedRowDrag = false;
                DetailStripBindClick(chip.gameObject, () =>
                {
                    if (chipDrag != null && chipDrag.ConsumedByDrag) return;
                    DetailStripOnTagFilterClick(filterSnap);
                });
                Color hoverCol = DetailStripBrighten(idle, 0.16f);
                UIHoverDelegate hover = chip.gameObject.GetComponent<UIHoverDelegate>();
                if (hover == null) hover = chip.gameObject.AddComponent<UIHoverDelegate>();
                Color baseCol = idle;
                hover.OnHoverChange += h =>
                {
                    if (chip == null) return;
                    chip.color = h ? hoverCol : baseCol;
                };
            }

            if (overflow > 0)
            {
                DetailStripAddTagChipSep(_detailStripTagsChipsHost, " ", s, hitH);
                string moreLabel = string.Format(
                    VPBTranslation.T("gallery.detail.tags_more_fmt", "+{0}"), overflow);
                Text more = DetailStripCreateTagChipLabel(
                    _detailStripTagsChipsHost, moreLabel, DetailStripColorTag, s, hitH,
                    clickable: true, name: "TagOverflow");
                AddTooltipPlain(more.gameObject,
                    VPBTranslation.T("gallery.detail.tip.tags_more", "Click: show all tags (filter)"));
                DetailStripBindClick(more.gameObject, DetailStripToggleTagFilterPopup);
                Color moreHover = DetailStripBrighten(DetailStripColorTag, 0.16f);
                UIHoverDelegate moreH = more.gameObject.GetComponent<UIHoverDelegate>();
                if (moreH == null) moreH = more.gameObject.AddComponent<UIHoverDelegate>();
                moreH.OnHoverChange += h =>
                {
                    if (more == null) return;
                    more.color = h ? moreHover : DetailStripColorTag;
                };
            }

            if (!string.IsNullOrEmpty(note))
            {
                if (shown.Count > 0 || overflow > 0)
                    DetailStripAddTagChipSep(_detailStripTagsChipsHost, " · ", s, hitH);
                bool noteOpensEditor = shown.Count == 0 && overflow == 0 && !showStampFromFirst;
                Text noteT = DetailStripCreateTagChipLabel(
                    _detailStripTagsChipsHost, note, DetailStripMetaMutedColor, s, hitH,
                    clickable: noteOpensEditor, name: "TagNote");
                if (noteOpensEditor)
                {
                    AddTooltipPlain(noteT.gameObject,
                        VPBTranslation.T("gallery.detail.tip.set_tags", "Click: open quick-tag editor"));
                    DetailStripBindClick(noteT.gameObject, DetailStripOnTagClick);
                    Color noteHover = DetailStripBrighten(DetailStripMetaMutedColor, 0.16f);
                    UIHoverDelegate noteH = noteT.gameObject.GetComponent<UIHoverDelegate>();
                    if (noteH == null) noteH = noteT.gameObject.AddComponent<UIHoverDelegate>();
                    noteH.OnHoverChange += h =>
                    {
                        if (noteT == null) return;
                        noteT.color = h ? noteHover : DetailStripMetaMutedColor;
                    };
                }
            }

            if (showStampFromFirst && !string.IsNullOrEmpty(stampLabel))
            {
                DetailStripAddTagChipSep(_detailStripTagsChipsHost, " · ", s, hitH);
                Text stamp = DetailStripCreateTagChipLabel(
                    _detailStripTagsChipsHost, stampLabel, DetailStripColorTag, s, hitH,
                    clickable: true, name: "StampFromFirst");
                AddTooltipPlain(stamp.gameObject,
                    VPBTranslation.T(
                        "gallery.detail.tip.stamp_from_first",
                        "Click: copy user tags from the first selected item onto the whole selection (merge)."));
                DetailStripBindClick(stamp.gameObject, UserTagClipboardStampFromFirst);
                Color stampHover = DetailStripBrighten(DetailStripColorTag, 0.16f);
                UIHoverDelegate stampH = stamp.gameObject.GetComponent<UIHoverDelegate>();
                if (stampH == null) stampH = stamp.gameObject.AddComponent<UIHoverDelegate>();
                stampH.OnHoverChange += h =>
                {
                    if (stamp == null) return;
                    stamp.color = h ? stampHover : DetailStripColorTag;
                };
            }

            if (_detailStripTagFilterMenuGO != null && _detailStripTagFilterMenuGO.activeSelf)
                DetailStripRebuildTagFilterPopupRows();

            DetailStripSyncTagClipboardActionChrome();
        }

        private float DetailStripEstimateTagChipsAvailWidth(float s)
        {
            float avail = DetailStripEstimateMetaAvailWidth();
            if (avail < 80f) avail = 240f * s;
            float setW = 0f;
            if (_detailStripTags != null)
            {
                try { setW = _detailStripTags.preferredWidth; } catch { setW = 0f; }
            }
            if (setW < 8f)
                setW = DetailStripEstimateChipTextWidth(
                    VPBTranslation.T("gallery.detail.set_tags", "Set Tags: "), s);
            float actionsW = 0f;
            if (_detailStripTagClipboardActionsGO != null && _detailStripTagClipboardActionsGO.activeInHierarchy)
            {
                try
                {
                    RectTransform art = _detailStripTagClipboardActionsGO.transform as RectTransform;
                    if (art != null) actionsW = art.rect.width;
                }
                catch { actionsW = 0f; }
            }
            if (actionsW < 8f)
            {
                // Fallback estimate before first layout pass.
                actionsW = DetailStripEstimateChipTextWidth(" · ", s)
                    + DetailStripEstimateChipTextWidth(
                        VPBTranslation.T("gallery.detail.copy_tags", "Copy"), s)
                    + DetailStripEstimateChipTextWidth(" · ", s)
                    + DetailStripEstimateChipTextWidth(
                        VPBTranslation.T("gallery.detail.paste_tags", "Paste"), s)
                    + DetailStripEstimateChipTextWidth(" · ", s)
                    + DetailStripEstimateChipTextWidth(
                        VPBTranslation.T("gallery.detail.replace_tags", "Replace"), s);
            }
            float gap = 4f * s;
            float chips = avail - setW - actionsW - gap * 2f;
            return Mathf.Max(64f * s, chips);
        }

        private static float DetailStripEstimateChipTextWidth(string text, float s)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            float charW = Mathf.Max(5f, GalleryUiDesignTokens.FontRef * 0.52f * s);
            return text.Length * charW;
        }

        private Text DetailStripCreateTagChipLabel(
            GameObject host, string text, Color color, float s, float hitH, bool clickable, string name)
        {
            Text t = UI.CreateLabel(
                host, text ?? "", GalleryUiDesignTokens.FontRef, color,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: clickable, richText: false,
                anchorPreset: AnchorPresets.middleLeft, name: name);
            DetailStripApplyFont(t, s);
            float pw = Mathf.Max(t.preferredWidth, 4f * s);
            UI.AddLE(t.gameObject,
                preferredWidth: pw, minWidth: pw,
                preferredHeight: hitH, minHeight: hitH,
                flexibleWidth: 0f, flexibleHeight: 0f);
            RectTransform rt = t.rectTransform;
            if (rt != null)
                rt.sizeDelta = new Vector2(pw, hitH);
            return t;
        }

        private void DetailStripAddTagChipSep(GameObject host, string sep, float s, float hitH)
        {
            DetailStripCreateTagChipLabel(
                host, sep, DetailStripMetaMutedColor, s, hitH, clickable: false, name: "TagSep");
        }

        private void DetailStripToggleTagFilterPopup()
        {
            DetailStripEnsureTagFilterPopup();
            if (_detailStripTagFilterMenuGO == null) return;
            if (_detailStripTagFilterMenuGO.activeSelf)
            {
                DetailStripCloseTagFilterPopup();
                return;
            }
            DetailStripRebuildTagFilterPopupRows();
            DetailStripPositionTagFilterPopup();
            _detailStripTagFilterMenuGO.SetActive(true);
            _detailStripTagFilterMenuGO.transform.SetAsLastSibling();
        }

        private void DetailStripCloseTagFilterPopup()
        {
            if (_detailStripTagFilterMenuGO != null)
                _detailStripTagFilterMenuGO.SetActive(false);
        }

        private void DetailStripEnsureApplyDropZone(GameObject strip)
        {
            if (strip == null) return;
            UserTagApplyDropZone dz = strip.GetComponent<UserTagApplyDropZone>();
            if (dz == null) dz = strip.AddComponent<UserTagApplyDropZone>();
            dz.Panel = this;
        }

        private void DetailStripEnsureTagFilterPopup()
        {
            GameObject host = canvas != null ? canvas.gameObject : backgroundBoxGO;
            if (_detailStripTagFilterMenuGO != null || host == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            _detailStripTagFilterMenuGO = UI.CreatePopupMenuRoot(host, "DetailStripTagFilterMenu", DetailStripCloseTagFilterPopup);
            float pad = DetailStripTagFilterPopupPadRef;
            GameObject panel = UI.CreatePopupMenuPanel(
                _detailStripTagFilterMenuGO, "TagFilterPanel",
                AnchorPresets.middleCenter,
                new Vector2(DetailStripTagFilterPopupWidthRef * s, 80f * s),
                Vector2.zero,
                TextAnchor.UpperLeft,
                vlg =>
                {
                    if (vlg == null) return;
                    vlg.padding = UI.Pad(pad, pad, pad, pad, s);
                    vlg.spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
                    vlg.childForceExpandWidth = true;
                    vlg.childControlWidth = true;
                });
            _detailStripTagFilterPanelRT = panel.GetComponent<RectTransform>();
            // Pivot at bottom — panel grows upward from anchor under the chips row.
            if (_detailStripTagFilterPanelRT != null)
                _detailStripTagFilterPanelRT.pivot = new Vector2(0.5f, 0f);

            ContentSizeFitter csf = panel.GetComponent<ContentSizeFitter>();
            if (csf != null) csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _detailStripTagFilterMenuGO.SetActive(false);
        }

        private void DetailStripPositionTagFilterPopup()
        {
            if (_detailStripTagFilterPanelRT == null || _detailStripTagFilterMenuGO == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            RectTransform overlayRT = _detailStripTagFilterMenuGO.GetComponent<RectTransform>();
            RectTransform anchor = null;
            if (_detailStripTags != null)
                anchor = _detailStripTags.rectTransform;
            if (anchor == null && _detailStripTagsChipsHost != null)
                anchor = _detailStripTagsChipsHost.GetComponent<RectTransform>();

            float w = DetailStripTagFilterPopupWidthRef * s;
            _detailStripTagFilterPanelRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            _detailStripTagFilterPanelRT.anchorMin = _detailStripTagFilterPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
            _detailStripTagFilterPanelRT.pivot = new Vector2(0.5f, 0f);

            float gap = GalleryUiDesignTokens.PopupMenuAnchorGapRef * s;
            if (anchor != null && overlayRT != null)
            {
                // Place above the Set Tags / chips band (open upward).
                Vector3 worldBottom = anchor.TransformPoint(new Vector3(
                    anchor.rect.center.x,
                    anchor.rect.yMin,
                    0f));
                Vector3 local = overlayRT.InverseTransformPoint(worldBottom);
                _detailStripTagFilterPanelRT.anchoredPosition = new Vector2(local.x, local.y + gap);
            }
            else
                _detailStripTagFilterPanelRT.anchoredPosition = Vector2.zero;

            float pad = DetailStripTagFilterPopupPadRef * s;
            UI.ClampPopupMenuPanelX(_detailStripTagFilterPanelRT, overlayRT, pad);
            UI.ClampPopupMenuPanelY(_detailStripTagFilterPanelRT, overlayRT, pad);
        }

        private void DetailStripRebuildTagFilterPopupRows()
        {
            DetailStripEnsureTagFilterPopup();
            if (_detailStripTagFilterPanelRT == null) return;
            GameObject panel = _detailStripTagFilterPanelRT.gameObject;
            UI.DestroyAllChildren(panel.transform);

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float panelW = DetailStripTagFilterPopupWidthRef * s;
            float pad = DetailStripTagFilterPopupPadRef * s;
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;
            float innerW = Mathf.Max(80f * s, panelW - pad * 2f);

            VerticalLayoutGroup panelVlg = panel.GetComponent<VerticalLayoutGroup>();
            if (panelVlg != null)
            {
                panelVlg.padding = UI.Pad(
                    DetailStripTagFilterPopupPadRef, DetailStripTagFilterPopupPadRef,
                    DetailStripTagFilterPopupPadRef, DetailStripTagFilterPopupPadRef, s);
                panelVlg.spacing = GalleryUiDesignTokens.PopupMenuRowSpacingRef * s;
                panelVlg.childForceExpandWidth = true;
                panelVlg.childControlWidth = true;
            }

            Text title = UI.CreateLabel(
                panel,
                VPBTranslation.T("gallery.detail.tag_filter_popup_title", "Filter by tag"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                UI.PopupMutedText,
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "Title");
            DetailStripApplyFont(title, s, GalleryUiDesignTokens.PopupMenuRowFontRef);
            UI.AddLE(title.gameObject, preferredHeight: rowH * 0.85f, minHeight: rowH * 0.75f, flexibleWidth: 1f);

            var tags = _detailStripBoundTagNames;
            if (tags == null || tags.Count == 0)
            {
                Text empty = UI.CreateLabel(
                    panel,
                    VPBTranslation.T("gallery.detail.no_tags", "none"),
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    DetailStripMetaMutedColor,
                    TextAnchor.MiddleLeft,
                    raycastTarget: false,
                    name: "Empty");
                DetailStripApplyFont(empty, s, GalleryUiDesignTokens.PopupMenuRowFontRef);
                UI.AddLE(empty.gameObject, preferredHeight: rowH, minHeight: rowH, flexibleWidth: 1f);
                DetailStripPositionTagFilterPopup();
                return;
            }

            int maxVisible = Mathf.Max(4, Mathf.FloorToInt(DetailStripTagFilterPopupMaxHRef * s / Mathf.Max(1f, rowH)) - 1);
            bool useScroll = tags.Count > maxVisible;
            GameObject listParent = panel;
            if (useScroll)
            {
                float scrollBarW = 12f * s;
                float scrollH = Mathf.Min(
                    DetailStripTagFilterPopupMaxHRef * s,
                    tags.Count * (rowH + GalleryUiDesignTokens.PopupMenuRowSpacingRef * s));
                GameObject scrollGO = UI.CreateVScrollableContent(
                    panel,
                    new Color(0f, 0f, 0f, 0f),
                    AnchorPresets.topMiddle,
                    Mathf.Max(64f * s, innerW - scrollBarW),
                    scrollH,
                    Vector2.zero,
                    scrollBarW,
                    GalleryUiDesignTokens.PopupMenuRowSpacingRef * s,
                    false);
                UI.AddLE(scrollGO,
                    preferredHeight: scrollH, minHeight: Mathf.Min(scrollH, 120f * s),
                    flexibleWidth: 1f, flexibleHeight: 0f, minWidth: 0f);
                ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
                listParent = sr != null && sr.content != null ? sr.content.gameObject : scrollGO;
            }

            for (int i = 0; i < tags.Count; i++)
            {
                string tagSnap = tags[i];
                if (string.IsNullOrEmpty(tagSnap)) continue;
                bool active = activeUserTags != null && activeUserTags.Contains(tagSnap);
                GameObject row = UI.AddStretchPopupMenuRow(
                    listParent.transform, tagSnap,
                    () =>
                    {
                        DetailStripOnTagFilterClick(tagSnap);
                        DetailStripRebuildTagFilterPopupRows();
                    },
                    active,
                    enabled: true,
                    rowHeight: rowH);
                Text rowT = row != null ? row.GetComponentInChildren<Text>(true) : null;
                if (rowT != null)
                    GalleryUiMetrics.ApplyFont(rowT, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            DetailStripPositionTagFilterPopup();
        }

        private void DetailStripEnsureTagMenu()
        {
            // Parent to canvas (sibling of pane) so drag can leave the main pane area freely.
            GameObject host = canvas != null ? canvas.gameObject : backgroundBoxGO;
            if (_detailStripTagMenuRoot != null || host == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            // Root without click-catch backdrop — user must keep selecting grid items while menu stays open.
            _detailStripTagMenuRoot = UI.CreatePopupMenuRoot(host, "DetailStripTagMenu", null);
            DetailStripDisableTagMenuBackdrop(_detailStripTagMenuRoot);
            _detailStripTagMenuPanelGO = UI.CreatePopupMenuPanel(
                _detailStripTagMenuRoot, "DetailStripTagMenuPanel",
                AnchorPresets.middleCenter,
                new Vector2(DetailStripTagMenuWidthRef, DetailStripTagMenuHeightRef),
                Vector2.zero);
            _detailStripTagMenuPanelRT = _detailStripTagMenuPanelGO.GetComponent<RectTransform>();
            if (_detailStripTagMenuPanelRT != null)
            {
                // Top-left pivot — resize grows bottom-right (same as Settings / QuickFilters floats).
                _detailStripTagMenuPanelRT.pivot = new Vector2(0f, 1f);
                DetailStripLoadTagMenuSavedSizeFromConfig();
                Vector2 sizeRef = DetailStripResolveTagMenuSizeRef();
                float sc = s > 0f ? s : 1f;
                _detailStripTagMenuPanelRT.sizeDelta = new Vector2(sizeRef.x * sc, sizeRef.y * sc);
            }
            DetailStripApplyTagMenuPanelChrome();

            // Fixed height panel — do not grow with tag count.
            ContentSizeFitter panelCsf = _detailStripTagMenuPanelGO.GetComponent<ContentSizeFitter>();
            if (panelCsf != null) UnityEngine.Object.Destroy(panelCsf);
            VerticalLayoutGroup panelVlg = _detailStripTagMenuPanelGO.GetComponent<VerticalLayoutGroup>();
            if (panelVlg != null)
            {
                panelVlg.childForceExpandHeight = false;
                panelVlg.childControlHeight = true;
                panelVlg.padding = UI.Pad(8f, 8f, 10f, 8f);
                panelVlg.spacing = 6f;
            }

            // Title bar — Settings float master: grip · left title · collapse · close.
            float headerH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef;
            float chromeSz = GalleryUiDesignTokens.ButtonSizeRef;
            float titleCloseSz = chromeSz;
            GalleryModalTypography type = new GalleryModalTypography(s);
            int font = type.Body;

            _detailStripTagMenuHeaderGO = UI.CreateChildRT(_detailStripTagMenuPanelGO, "Header");
            Image headerImg = UI.AddImage(_detailStripTagMenuHeaderGO, DetailStripTagMenuTitleBarBg);
            if (headerImg != null) headerImg.raycastTarget = true;
            UI.AddHLG(
                _detailStripTagMenuHeaderGO,
                spacing: 4f,
                padding: UI.Pad(6f, 6f, 4f, 4f),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: false);
            UI.AddLE(
                _detailStripTagMenuHeaderGO,
                preferredHeight: headerH,
                minHeight: headerH,
                flexibleWidth: 1f,
                flexibleHeight: 0f);
            if (_detailStripTagMenuHeaderGO.GetComponent<RectMask2D>() == null)
                _detailStripTagMenuHeaderGO.AddComponent<RectMask2D>();

            Text grip = UI.CreateLabel(
                _detailStripTagMenuHeaderGO,
                "\u2807",
                font,
                GalleryUiColorTokens.TextDim,
                TextAnchor.MiddleCenter,
                raycastTarget: false,
                name: "Grip");
            GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            UI.AddLE(grip.gameObject, minWidth: 18f, preferredWidth: 18f);

            UI.CreateFloatTitleWindowIcon(
                _detailStripTagMenuHeaderGO, "vpb_icons/tags.png",
                GalleryUiDesignTokens.FloatTitleWindowIconSizeRef);

            _detailStripTagMenuSelText = UI.CreateEmphasisTitleLabel(
                _detailStripTagMenuHeaderGO,
                VPBTranslation.T("gallery.detail.tag_menu_drag", "Tags"),
                font, Color.white, TextAnchor.MiddleLeft, name: "Title");
            UI.AddLE(_detailStripTagMenuSelText.gameObject, flexibleWidth: 1f, minWidth: 60f);

            _detailStripTagMenuCollapseBtn = SettingsFloatSquareIconButton(
                _detailStripTagMenuHeaderGO.transform, titleCloseSz, "vpb_icons/chevron_up.png",
                GalleryUiColorTokens.ChromeIconWell, DetailStripToggleTagMenuCollapsed);
            if (_detailStripTagMenuCollapseBtn != null)
            {
                _detailStripTagMenuCollapseBtn.name = "CollapseBtn";
                Transform iconTr = _detailStripTagMenuCollapseBtn.transform.Find("Icon");
                _detailStripTagMenuCollapseIcon = iconTr != null ? iconTr.GetComponent<Image>() : null;
            }

            _detailStripTagMenuCloseGO = SettingsFloatSquareIconButton(
                _detailStripTagMenuHeaderGO.transform, titleCloseSz, "vpb_icons/x.png",
                GalleryUiColorTokens.ChromeIconWell, DetailStripCloseTagMenu);
            if (_detailStripTagMenuCloseGO != null)
                _detailStripTagMenuCloseGO.name = "TitleClose";

            // Hairline under titlebar (separates chrome from work surface).
            GameObject headerRule = UI.CreateChildRT(_detailStripTagMenuPanelGO, "HeaderRule");
            Image ruleImg = UI.AddImage(headerRule, GalleryUiColorTokens.SurfaceMid, raycastTarget: false);
            if (ruleImg != null) ruleImg.raycastTarget = false;
            UI.AddLE(headerRule, preferredHeight: 1f, minHeight: 1f, flexibleWidth: 1f, flexibleHeight: 0f);

            // Filter row — top, Settings chrome input + search glyph.
            float filterH = chromeSz + 10f;
            _detailStripTagMenuFilterRowGO = UI.CreateChildRT(_detailStripTagMenuPanelGO, "FilterRow");
            Image filterRowBg = UI.AddImage(_detailStripTagMenuFilterRowGO, GalleryUiColorTokens.SurfaceDarker);
            if (filterRowBg != null) filterRowBg.raycastTarget = true;
            UI.AddHLG(
                _detailStripTagMenuFilterRowGO,
                spacing: 0f,
                padding: UI.Pad(8f, 8f, 4f, 4f),
                childAlignment: TextAnchor.MiddleCenter,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: true,
                childForceExpandHeight: false);
            UI.AddLE(
                _detailStripTagMenuFilterRowGO,
                preferredHeight: filterH,
                minHeight: filterH,
                flexibleWidth: 1f,
                flexibleHeight: 0f);

            _detailStripTagMenuSearch = UI.CreateChromeLayoutInputField(
                _detailStripTagMenuFilterRowGO.transform,
                font,
                filterH - 8f,
                1f,
                8f,
                2f,
                GalleryUiColorTokens.SurfaceDarker,
                UI.InputFieldPlaceholderColor,
                VPBTranslation.T("gallery.detail.tag_search", "Filter Available / create…"),
                "TagMenuFilter");
            if (_detailStripTagMenuSearch != null)
            {
                UI.LayoutChromeSearchIcon(_detailStripTagMenuSearch.gameObject, s);
                float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef;
                Transform textAreaTr = _detailStripTagMenuSearch.transform.Find("TextArea");
                if (textAreaTr != null)
                {
                    RectTransform taRt = textAreaTr as RectTransform;
                    if (taRt != null)
                        taRt.offsetMax = new Vector2(-clearSz, taRt.offsetMax.y);
                }

                _detailStripTagMenuFilterClearGo = UI.CreateUIButton(
                    _detailStripTagMenuSearch.gameObject,
                    clearSz, filterH - 8f, "X", 24, 0, 0, AnchorPresets.middleRight,
                    () =>
                    {
                        DetailStripOnTagMenuFilterCleared();
                        try
                        {
                            if (_detailStripTagMenuSearch != null)
                            {
                                _detailStripTagMenuSearch.text = "";
                                _detailStripTagMenuSearch.ActivateInputField();
                                _detailStripTagMenuSearch.MoveTextEnd(false);
                            }
                        }
                        catch { }
                        DetailStripRefreshTagMenuFilterClearVisible();
                    });
                if (_detailStripTagMenuFilterClearGo != null)
                {
                    _detailStripTagMenuFilterClearGo.name = "FilterClear";
                    RectTransform clearRT = _detailStripTagMenuFilterClearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                    {
                        clearRT.anchorMin = new Vector2(1f, 0f);
                        clearRT.anchorMax = new Vector2(1f, 1f);
                        clearRT.pivot = new Vector2(1f, 0.5f);
                        clearRT.anchoredPosition = Vector2.zero;
                        clearRT.sizeDelta = new Vector2(clearSz, 0f);
                    }
                    Image clearBg = _detailStripTagMenuFilterClearGo.GetComponent<Image>();
                    if (clearBg != null) clearBg.color = new Color(0f, 0f, 0f, 0f);
                    try
                    {
                        Sprite xSpr = UI.LoadIconSprite("vpb_icons/x.png", new Color(0.6f, 0.6f, 0.6f));
                        if (xSpr != null)
                            UI.AddIconToButton(_detailStripTagMenuFilterClearGo, xSpr, 6f, new Color(0f, 0f, 0f, 0f));
                    }
                    catch { }
                    Text clearLabel = _detailStripTagMenuFilterClearGo.GetComponentInChildren<Text>(true);
                    if (clearLabel != null) clearLabel.text = "";
                    var clearHover = _detailStripTagMenuFilterClearGo.AddComponent<UIHoverBorder>();
                    clearHover.hoverColor = new Color(1f, 0.2f, 0.2f, 1f);
                    clearHover.borderSize = 2f;
                    clearHover.inward = true;
                    AddTooltipPlain(_detailStripTagMenuFilterClearGo,
                        VPBTranslation.T("gallery.creator.strip_filter_clear", "Clear filter"));
                }

                _detailStripTagMenuSearch.onValueChanged.AddListener(DetailStripOnTagMenuFilterChanged);
                try
                {
                    SearchInputESCHandler legacyEsc = _detailStripTagMenuSearch.GetComponent<SearchInputESCHandler>();
                    if (legacyEsc != null) UnityEngine.Object.Destroy(legacyEsc);
                }
                catch { }
                try
                {
                    _detailStripTagMenuSearch.gameObject.AddComponent<CtrlBackspaceWordDeleteHandler>()
                        .Initialize(_detailStripTagMenuSearch);
                }
                catch { }
                DetailStripTagMenuSearchKeys keys = _detailStripTagMenuSearch.GetComponent<DetailStripTagMenuSearchKeys>();
                if (keys == null) keys = _detailStripTagMenuSearch.gameObject.AddComponent<DetailStripTagMenuSearchKeys>();
                keys.Panel = this;
                keys.Field = _detailStripTagMenuSearch;
                DetailStripRefreshTagMenuFilterClearVisible();
            }

            // Discoverability tip (same popup row font as rest of menu chrome).
            GameObject tipGO = UI.CreateChildRT(_detailStripTagMenuPanelGO, "Tip");
            _detailStripTagMenuTipText = UI.CreateLabel(
                tipGO,
                VPBTranslation.T(
                    "gallery.detail.tag_menu_tip",
                    "Click toggle · drag reorder · drop on Available to remove"),
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                UI.PopupMutedText,
                TextAnchor.MiddleCenter,
                raycastTarget: false,
                name: "TipText");
            UI.AddLE(tipGO, preferredHeight: DetailStripTagMenuSectionLabelHRef, minHeight: DetailStripTagMenuSectionLabelHRef, flexibleWidth: 1f, flexibleHeight: 0f);
            if (_detailStripTagMenuTipText != null)
            {
                _detailStripTagMenuTipText.alignment = TextAnchor.MiddleCenter;
                RectTransform tipTextRT = _detailStripTagMenuTipText.GetComponent<RectTransform>();
                if (tipTextRT != null)
                {
                    tipTextRT.anchorMin = Vector2.zero;
                    tipTextRT.anchorMax = Vector2.one;
                    tipTextRT.offsetMin = Vector2.zero;
                    tipTextRT.offsetMax = Vector2.zero;
                }
            }

            var headerDrag = _detailStripTagMenuHeaderGO.AddComponent<DetailStripTagMenuDrag>();
            headerDrag.Target = _detailStripTagMenuPanelRT;
            headerDrag.OnMoved = DetailStripOnTagMenuDragged;

            // Two columns: Applied | Available — proximity + recognition for multi-tag.
            _detailStripTagMenuColumnsGO = UI.CreateChildRT(_detailStripTagMenuPanelGO, "Columns");
            UI.AddHLG(
                _detailStripTagMenuColumnsGO,
                spacing: DetailStripTagMenuColGapRef,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.UpperLeft,
                childForceExpandWidth: false,
                childForceExpandHeight: true);
            UI.AddLE(
                _detailStripTagMenuColumnsGO,
                flexibleWidth: 1f,
                flexibleHeight: 1f,
                minHeight: 80f,
                preferredHeight: 80f);

            DetailStripCreateTagMenuColumn(
                _detailStripTagMenuColumnsGO, "Applied",
                VPBTranslation.T("gallery.detail.tag_applied", "Applied"),
                out _detailStripTagMenuAppliedLabel,
                out _detailStripTagMenuAppliedScrollGO,
                out _detailStripTagMenuAppliedListGO,
                withAvailSort: false);
            DetailStripCreateTagMenuColumn(
                _detailStripTagMenuColumnsGO, "Available",
                VPBTranslation.T("gallery.detail.tag_available", "Available"),
                out _detailStripTagMenuAvailableLabel,
                out _detailStripTagMenuAvailableScrollGO,
                out _detailStripTagMenuAvailableListGO,
                withAvailSort: true);
            DetailStripEnsureTagMenuColumnDropZones();
            DetailStripSyncTagMenuAvailSortIcon();

            DetailStripEnsureTagMenuModeTabs();
            DetailStripEnsureTagMenuDatabasePane();

            // Create row — shown only when filter has no exact match.
            _detailStripTagMenuCreateGO = UI.CreateUIButton(
                _detailStripTagMenuPanelGO, DetailStripTagMenuWidthRef - 20f,
                GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                VPBTranslation.T("gallery.detail.tag_new", "New tag…"),
                GalleryUiDesignTokens.PopupMenuRowFontRef, 0f, 0f, AnchorPresets.middleCenter,
                DetailStripOnTagMenuCreateClick);
            Image createImg = _detailStripTagMenuCreateGO.GetComponent<Image>();
            if (createImg != null) createImg.color = UI.PopupRowBackdrop;
            _detailStripTagMenuCreateText = _detailStripTagMenuCreateGO.GetComponentInChildren<Text>(true);
            UI.AddLE(_detailStripTagMenuCreateGO,
                preferredHeight: GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                minHeight: GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                flexibleWidth: 1f);
            _detailStripTagMenuCreateGO.SetActive(false);

            // Footer — Settings master: Close text + Dock text + spacer + resize.
            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef;
            _detailStripTagMenuSearchRowGO = UI.CreateChildRT(_detailStripTagMenuPanelGO, "Footer");
            UI.AddImage(_detailStripTagMenuSearchRowGO, GalleryUiColorTokens.SurfaceDarker);
            UI.AddHLG(
                _detailStripTagMenuSearchRowGO,
                spacing: 6f,
                padding: UI.Pad(8f, 8f, 4f, 4f),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: false);
            UI.AddLE(
                _detailStripTagMenuSearchRowGO,
                preferredHeight: footerH,
                minHeight: footerH,
                flexibleWidth: 1f,
                flexibleHeight: 0f);
            if (_detailStripTagMenuSearchRowGO.GetComponent<RectMask2D>() == null)
                _detailStripTagMenuSearchRowGO.AddComponent<RectMask2D>();

            // Full-footer drag hit (behind Close/Dock/resize) — same job as title bar.
            GameObject footerDragArea = UI.CreateFloatFooterDragArea(_detailStripTagMenuSearchRowGO);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<DetailStripTagMenuDrag>();
                footerDrag.Target = _detailStripTagMenuPanelRT;
                footerDrag.OnMoved = DetailStripOnTagMenuDragged;
            }

            float footerBtnW = 96f;
            _detailStripTagMenuFooterCloseGO = SettingsFloatChromeButton(
                _detailStripTagMenuSearchRowGO.transform, footerBtnW, chromeSz,
                VPBTranslation.T("gallery.detail.tag_menu_close", "Close"), font, 1f,
                GalleryUiColorTokens.SurfaceMid, DetailStripCloseTagMenu);
            if (_detailStripTagMenuFooterCloseGO != null)
                _detailStripTagMenuFooterCloseGO.name = "FooterClose";

            // Text Dock — Import float language; Settings chrome button (dismiss — tag menu has no side dock).
            GameObject footerDock = SettingsFloatChromeButton(
                _detailStripTagMenuSearchRowGO.transform, footerBtnW, chromeSz,
                VPBTranslation.T("gallery.import.dock", "Dock"), font, 1f,
                GalleryUiColorTokens.SurfaceMid, DetailStripCloseTagMenu);
            if (footerDock != null) footerDock.name = "FooterDock";

            GameObject footerSpacer = new GameObject("Spacer");
            footerSpacer.transform.SetParent(_detailStripTagMenuSearchRowGO.transform, false);
            footerSpacer.AddComponent<RectTransform>();
            UI.AddLE(footerSpacer, flexibleWidth: 1f, minWidth: 8f);
            UI.EnsureFloatFooterSpacerDragHit(footerSpacer);
            var spacerDrag = footerSpacer.AddComponent<DetailStripTagMenuDrag>();
            spacerDrag.Target = _detailStripTagMenuPanelRT;
            spacerDrag.OnMoved = DetailStripOnTagMenuDragged;

            float rhSz = GalleryUiDesignTokens.ButtonSizeRef;
            _detailStripTagMenuResizeGO = UI.AddChildGOImage(
                _detailStripTagMenuSearchRowGO, UI.IconButtonBackdrop, AnchorPresets.middleCenter,
                rhSz, rhSz, Vector2.zero, rounded: true);
            _detailStripTagMenuResizeGO.name = "ResizeHandle";
            Image rhImg = _detailStripTagMenuResizeGO.GetComponent<Image>();
            if (rhImg != null) rhImg.raycastTarget = true;
            try
            {
                Sprite rhSpr = UI.LoadIconSprite("vpb_icons/chevrons_down_right.png", UI.BarIconGlyphTint);
                if (rhSpr != null)
                    UI.AddIconToButton(_detailStripTagMenuResizeGO, rhSpr, 5f, UI.IconButtonBackdrop);
            }
            catch { }
            LayoutElement rhLe = _detailStripTagMenuResizeGO.GetComponent<LayoutElement>();
            if (rhLe == null) rhLe = _detailStripTagMenuResizeGO.AddComponent<LayoutElement>();
            rhLe.preferredWidth = rhLe.minWidth = rhSz;
            rhLe.preferredHeight = rhLe.minHeight = rhSz;
            rhLe.flexibleWidth = 0f;
            rhLe.flexibleHeight = 0f;
            var resizer = _detailStripTagMenuResizeGO.AddComponent<DetailStripTagMenuResize>();
            resizer.Target = _detailStripTagMenuPanelRT;
            resizer.GetMinSize = () =>
            {
                float sc = ChromeScale > 0f ? ChromeScale : 1f;
                return new Vector2(DetailStripTagMenuMinWidthRef * sc, DetailStripTagMenuMinHeightRef * sc);
            };
            resizer.GetMaxSize = () =>
            {
                float sc = ChromeScale > 0f ? ChromeScale : 1f;
                return new Vector2(DetailStripTagMenuMaxWidthRef * sc, DetailStripTagMenuMaxHeightRef * sc);
            };
            resizer.OnResized = DetailStripOnTagMenuResized;
            _detailStripTagMenuResizeGO.transform.SetAsLastSibling();

            _detailStripTagMenuCollapsed = false;
            _detailStripTagMenuRoot.SetActive(false);
        }

        private void DetailStripRefreshTagMenuFilterClearVisible()
        {
            if (_detailStripTagMenuFilterClearGo == null) return;
            bool has = _detailStripTagMenuSearch != null
                && !string.IsNullOrEmpty(_detailStripTagMenuSearch.text);
            try { _detailStripTagMenuFilterClearGo.SetActive(has); } catch { }
        }

        private void DetailStripToggleTagMenuCollapsed()
        {
            if (_detailStripTagMenuPanelRT == null) return;
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            float titleH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;

            if (!_detailStripTagMenuCollapsed)
            {
                Vector2 size = _detailStripTagMenuPanelRT.sizeDelta;
                _detailStripTagMenuExpandHeightRef = s > 0f ? size.y / s : size.y;
                _detailStripTagMenuCollapsedTopLeftPos = _detailStripTagMenuPanelRT.anchoredPosition;
                _detailStripTagMenuCollapsed = true;
            }
            else
            {
                _detailStripTagMenuCollapsed = false;
                _detailStripTagMenuCollapsedTopLeftPos = null;
            }

            DetailStripSyncTagMenuCollapseChrome(titleH);
        }

        private void DetailStripSyncTagMenuCollapseChrome(float titleH)
        {
            bool show = !_detailStripTagMenuCollapsed;
            if (_detailStripTagMenuFilterRowGO != null) _detailStripTagMenuFilterRowGO.SetActive(show);
            Transform ruleTr = _detailStripTagMenuPanelGO != null
                ? _detailStripTagMenuPanelGO.transform.Find("HeaderRule") : null;
            if (ruleTr != null) ruleTr.gameObject.SetActive(show);
            Transform tipTr = _detailStripTagMenuPanelGO != null
                ? _detailStripTagMenuPanelGO.transform.Find("Tip") : null;
            if (tipTr != null) tipTr.gameObject.SetActive(show);
            if (_detailStripTagMenuModeTabsGO != null) _detailStripTagMenuModeTabsGO.SetActive(show);
            if (_detailStripTagMenuColumnsGO != null) _detailStripTagMenuColumnsGO.SetActive(show);
            if (_detailStripTagMenuDbHostGO != null)
                _detailStripTagMenuDbHostGO.SetActive(show && _detailStripTagMenuMode == DetailStripTagMenuMode.Database);
            if (_detailStripTagMenuCreateGO != null && !show)
                _detailStripTagMenuCreateGO.SetActive(false);
            else if (_detailStripTagMenuCreateGO != null && show)
                DetailStripUpdateTagMenuCreateButton();
            if (_detailStripTagMenuSearchRowGO != null) _detailStripTagMenuSearchRowGO.SetActive(show);

            if (_detailStripTagMenuCollapseIcon != null)
            {
                string path = _detailStripTagMenuCollapsed ? "vpb_icons/chevron_down.png" : "vpb_icons/chevron_up.png";
                Sprite spr = UI.LoadIconSprite(path, UI.BarIconGlyphTint);
                if (spr != null)
                {
                    _detailStripTagMenuCollapseIcon.sprite = spr;
                    _detailStripTagMenuCollapseIcon.color = Color.white;
                }
            }

            if (_detailStripTagMenuPanelRT == null) return;
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            if (_detailStripTagMenuCollapsed)
            {
                Vector2 size = _detailStripTagMenuPanelRT.sizeDelta;
                size.y = titleH;
                _detailStripTagMenuPanelRT.sizeDelta = size;
                if (_detailStripTagMenuCollapsedTopLeftPos.HasValue)
                    _detailStripTagMenuPanelRT.anchoredPosition = _detailStripTagMenuCollapsedTopLeftPos.Value;
            }
            else if (_detailStripTagMenuExpandHeightRef.HasValue)
            {
                Vector2 size = _detailStripTagMenuPanelRT.sizeDelta;
                size.y = Mathf.Clamp(
                    _detailStripTagMenuExpandHeightRef.Value * s,
                    DetailStripTagMenuMinHeightRef * s,
                    DetailStripTagMenuMaxHeightRef * s);
                _detailStripTagMenuPanelRT.sizeDelta = size;
            }
        }

        private static void DetailStripStyleTagMenuTitleClose(GameObject closeGO, float size)
        {
            // Legacy path — SettingsFloatSquareIconButton owns new chrome.
            if (closeGO == null) return;
            AspectRatioFitter arf = closeGO.GetComponent<AspectRatioFitter>();
            if (arf != null) UnityEngine.Object.Destroy(arf);
            Image closeImg = closeGO.GetComponent<Image>();
            if (closeImg != null) closeImg.color = new Color(0f, 0f, 0f, 0.5f);
            Button closeBtn = closeGO.GetComponent<Button>();
            if (closeBtn != null) closeBtn.transition = Selectable.Transition.None;
            LayoutElement closeLe = closeGO.GetComponent<LayoutElement>();
            if (closeLe == null) closeLe = closeGO.AddComponent<LayoutElement>();
            closeLe.preferredWidth = size;
            closeLe.preferredHeight = size;
            closeLe.minWidth = size;
            closeLe.minHeight = size;
            closeLe.flexibleWidth = 0f;
            closeLe.flexibleHeight = 0f;
            RectTransform closeRT = closeGO.GetComponent<RectTransform>();
            if (closeRT != null)
            {
                closeRT.anchorMin = closeRT.anchorMax = new Vector2(0.5f, 0.5f);
                closeRT.pivot = new Vector2(0.5f, 0.5f);
                closeRT.sizeDelta = new Vector2(size, size);
            }
            Text closeLabel = closeGO.GetComponentInChildren<Text>(true);
            if (closeLabel != null) closeLabel.gameObject.SetActive(false);

            const float iconPad = 6f;
            Transform closeIconTr = closeGO.transform.Find("Icon");
            if (closeIconTr == null)
            {
                Sprite closeSpr = null;
                try { closeSpr = UI.LoadIconSprite("vpb_icons/x.png", UI.BarIconGlyphTint); } catch { }
                if (closeSpr != null)
                    UI.AddIconToButton(closeGO, closeSpr, iconPad, new Color(0f, 0f, 0f, 0.5f));
            }
            else
            {
                RectTransform iconRT = closeIconTr.GetComponent<RectTransform>();
                if (iconRT != null)
                {
                    iconRT.anchorMin = Vector2.zero;
                    iconRT.anchorMax = Vector2.one;
                    iconRT.sizeDelta = new Vector2(-iconPad * 2f, -iconPad * 2f);
                    iconRT.anchoredPosition = Vector2.zero;
                }
            }

            UIHoverBorder closeBorder = closeGO.GetComponent<UIHoverBorder>();
            if (closeBorder != null)
                UnityEngine.Object.Destroy(closeBorder);
            UIHoverColor closeHover = closeGO.GetComponent<UIHoverColor>();
            if (closeHover != null)
                UnityEngine.Object.Destroy(closeHover);
        }

        private void DetailStripApplyTagMenuPanelChrome()
        {
            if (_detailStripTagMenuPanelGO == null) return;
            Image panelImg = _detailStripTagMenuPanelGO.GetComponent<Image>();
            if (panelImg != null)
                panelImg.color = DetailStripTagMenuPanelBg;
            DetailStripApplyTagMenuColumnFill(_detailStripTagMenuAppliedScrollGO);
            DetailStripApplyTagMenuColumnFill(_detailStripTagMenuAvailableScrollGO);
        }

        private static void DetailStripApplyTagMenuColumnFill(GameObject scrollGO)
        {
            if (scrollGO == null) return;
            Image img = scrollGO.GetComponent<Image>();
            if (img != null) img.color = DetailStripTagMenuColBg;
        }

        private void DetailStripEnsureTagMenuColumnDropZones()
        {
            if (_detailStripTagMenuAppliedScrollGO != null)
            {
                UserTagApplyDropZone applyDz = _detailStripTagMenuAppliedScrollGO.GetComponent<UserTagApplyDropZone>();
                if (applyDz == null) applyDz = _detailStripTagMenuAppliedScrollGO.AddComponent<UserTagApplyDropZone>();
                applyDz.Panel = this;
            }
            if (_detailStripTagMenuAvailableScrollGO != null)
            {
                UserTagRemoveDropZone removeDz = _detailStripTagMenuAvailableScrollGO.GetComponent<UserTagRemoveDropZone>();
                if (removeDz == null) removeDz = _detailStripTagMenuAvailableScrollGO.AddComponent<UserTagRemoveDropZone>();
                removeDz.Panel = this;
            }
        }

        /// <summary>Esc while search focused: clear filter, then close. Enter: create when Create row shown.</summary>
        internal void DetailStripTagMenuOnSearchEscape()
        {
            if (_detailStripTagMenuRoot == null || !_detailStripTagMenuRoot.activeSelf) return;
            if (_userTagEditorMergeModalGo != null && _userTagEditorMergeModalGo.activeSelf)
            {
                UserTagEditorCloseMergeDialog();
                return;
            }
            if (_userTagEditorRenameModalGo != null && _userTagEditorRenameModalGo.activeSelf)
            {
                UserTagEditorCloseRenameDialog();
                return;
            }
            if (_tagCategoryModalGo != null)
            {
                CloseTagCategoryEditorModal();
                return;
            }
            string filter = (_detailStripTagMenuFilter ?? "").Trim();
            if (!string.IsNullOrEmpty(filter)
                || (_detailStripTagMenuSearch != null && !string.IsNullOrEmpty(_detailStripTagMenuSearch.text)))
            {
                _detailStripTagMenuFilter = "";
                DetailStripStopTagMenuFilterRebuild();
                if (_detailStripTagMenuSearch != null)
                {
                    try { _detailStripTagMenuSearch.text = ""; } catch { }
                    try
                    {
                        _detailStripTagMenuSearch.ActivateInputField();
                        _detailStripTagMenuSearch.MoveTextEnd(false);
                    }
                    catch { }
                }
                if (DetailStripTagMenuIsDatabaseMode())
                    RebuildUserTagEditorRows();
                else
                {
                    DetailStripUpdateTagMenuCreateButton();
                    DetailStripRebuildTagMenuList(fullLayoutSync: false);
                }
                return;
            }
            DetailStripCloseTagMenu();
        }

        internal void DetailStripTagMenuOnSearchSubmit()
        {
            if (_detailStripTagMenuRoot == null || !_detailStripTagMenuRoot.activeSelf) return;
            if (DetailStripTagMenuIsDatabaseMode())
            {
                // Enter in Database filter: create vocab rows from filter text (one name).
                string filter = (_detailStripTagMenuFilter ?? "").Trim();
                if (string.IsNullOrEmpty(filter)) return;
                if (_userTagEditorNewTagInput != null
                    && string.IsNullOrEmpty((_userTagEditorNewTagInput.text ?? "").Trim()))
                {
                    _userTagEditorNewTagInput.text = filter;
                    UserTagEditorOnCreateTagsClicked();
                }
                return;
            }
            if (_detailStripTagMenuCreateGO != null && _detailStripTagMenuCreateGO.activeSelf)
                DetailStripOnTagMenuCreateClick();
        }

        internal bool DetailStripIsTagMenuOpen()
        {
            return _detailStripTagMenuRoot != null && _detailStripTagMenuRoot.activeSelf;
        }

        private void DetailStripCreateTagMenuColumn(
            GameObject columnsGO,
            string name,
            string title,
            out Text label,
            out GameObject scrollGO,
            out GameObject listGO,
            bool withAvailSort)
        {
            label = null;
            scrollGO = null;
            listGO = null;
            if (columnsGO == null) return;

            GameObject col = UI.CreateChildRT(columnsGO, name + "Col");
            UI.AddVLG(
                col,
                spacing: 2f,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.UpperLeft,
                childForceExpandWidth: true,
                childForceExpandHeight: false);
            UI.AddLE(col, flexibleWidth: 1f, flexibleHeight: 1f, minWidth: 120f);

            // Header: label (+ optional sort on Add). Both cols use chrome btn height so rows align.
            float headerH = DetailStripTagMenuChromeBtnRef;
            GameObject header = UI.CreateChildRT(col, "Header");
            UI.AddHLG(
                header,
                spacing: 4f,
                padding: UI.Pad(0f, 0f, 0f, 0f),
                childAlignment: TextAnchor.MiddleLeft,
                childControlWidth: true,
                childControlHeight: true,
                childForceExpandWidth: false,
                childForceExpandHeight: false);
            UI.AddLE(
                header,
                preferredHeight: headerH,
                minHeight: headerH,
                flexibleWidth: 1f,
                flexibleHeight: 0f);

            label = UI.CreateLabel(
                header,
                title ?? "",
                GalleryUiDesignTokens.PopupMenuRowFontRef,
                UI.PopupMutedText,
                TextAnchor.MiddleLeft,
                raycastTarget: false,
                name: "Label");
            UI.AddLE(
                label.gameObject,
                preferredHeight: headerH,
                minHeight: headerH,
                flexibleWidth: 1f,
                flexibleHeight: 0f);

            if (withAvailSort)
            {
                float sortSq = DetailStripTagMenuChromeBtnRef;
                Color sortBackdropCol = GalleryUiColorTokens.ActiveUtility;
                Sprite sortSpr0 = sceneSourceSortModeSprites != null && sceneSourceSortModeSprites.Length > 0
                    ? sceneSourceSortModeSprites[0]
                    : null;
                // Same edge as side-pane sort chips; pad 5 keeps glyph readable.
                _detailStripTagMenuAvailSortBtnGO = UI.CreateSideTabSquareIconButton(
                    header, sortSq, sortSpr0, DetailStripCycleTagMenuAvailSort, sortBackdropCol, 5f);
                _detailStripTagMenuAvailSortBtnGO.name = "AvailSortBtn";
                Transform sortIconTr = _detailStripTagMenuAvailSortBtnGO.transform.Find("Icon");
                _detailStripTagMenuAvailSortIcon = sortIconTr != null ? sortIconTr.GetComponent<Image>() : null;
                AddTooltipPlain(
                    _detailStripTagMenuAvailSortBtnGO,
                    VPBTranslation.T(
                        "gallery.detail.tag_avail_sort_tip",
                        "Sort Available list: A→Z / Z→A / count 1→9 / 9→1. Remembers choice."));
            }

            float colW = (DetailStripTagMenuWidthRef - 16f - DetailStripTagMenuColGapRef) * 0.5f;
            scrollGO = UI.CreateVScrollableContent(
                col,
                DetailStripTagMenuColBg,
                AnchorPresets.topMiddle,
                colW,
                DetailStripTagMenuScrollHeightRef,
                Vector2.zero,
                12f,
                GalleryUiDesignTokens.PopupMenuRowSpacingRef,
                false);
            UI.AddLE(scrollGO,
                flexibleWidth: 1f,
                flexibleHeight: 1f,
                minHeight: 80f,
                preferredHeight: 80f);
            ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
            listGO = sr != null && sr.content != null ? sr.content.gameObject : null;
            if (sr != null) sr.movementType = ScrollRect.MovementType.Clamped;
        }

        private void DetailStripCycleTagMenuAvailSort()
        {
            SortState st = GetSortState(DetailStripTagMenuAvailSortContext);
            int i = TryGetSidePaneFourModeIndex(st);
            int next = (i < 0) ? 0 : (i + 1) % 4;
            SidePaneFourModeToState(next, out SortType ty, out SortDirection d);
            st.Type = ty;
            st.Direction = d;
            SaveSortState(DetailStripTagMenuAvailSortContext, st);
            DetailStripSyncTagMenuAvailSortIcon();
            DetailStripRebuildTagMenuList(fullLayoutSync: false);
        }

        private void DetailStripSyncTagMenuAvailSortIcon()
        {
            if (_detailStripTagMenuAvailSortIcon == null) return;
            if (sceneSourceSortModeSprites == null || sceneSourceSortModeSprites.Length == 0) return;
            SortState st = GetSortState(DetailStripTagMenuAvailSortContext);
            int idx = TryGetSidePaneFourModeIndex(st);
            int spIdx = idx >= 0 ? idx : 0;
            if (spIdx < 0 || spIdx >= sceneSourceSortModeSprites.Length) return;
            Sprite sp = sceneSourceSortModeSprites[spIdx];
            if (sp == null) return;
            _detailStripTagMenuAvailSortIcon.sprite = sp;
            _detailStripTagMenuAvailSortIcon.enabled = true;
        }

        private void DetailStripOnTagMenuCreateClick()
        {
            string filter = (_detailStripTagMenuFilter ?? "").Trim();
            if (string.IsNullOrEmpty(filter))
            {
                DetailStripCloseTagMenu();
                DetailStripShowNewTagModal();
                return;
            }
            string created = DetailStripCreateAndApplyTag(filter);
            if (!string.IsNullOrEmpty(created))
            {
                DetailStripOptimisticTagMenuSet(created, applied: true);
                DetailStripNoteTagMenuRecent(created);
            }
            _detailStripTagMenuFilter = "";
            if (_detailStripTagMenuSearch != null)
            {
                try { _detailStripTagMenuSearch.text = ""; } catch { }
            }
            DetailStripRebuildTagMenuFromCaches();
        }

        /// <summary>
        /// Apply is async (DB thread). Update menu caches immediately so Applied column
        /// reflects the click before the coroutine finishes.
        /// </summary>
        private void DetailStripOptimisticTagMenuSet(string tag, bool applied)
        {
            if (string.IsNullOrEmpty(tag)) return;
            DetailStripEnsureTagMenuCaches();
            if (_detailStripTagMenuAppliedCache == null)
                _detailStripTagMenuAppliedCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (applied)
            {
                _detailStripTagMenuAppliedCache.Add(tag);
                if (_detailStripTagMenuVocabCache == null)
                    _detailStripTagMenuVocabCache = new List<string>(8);
                bool inVocab = false;
                for (int i = 0; i < _detailStripTagMenuVocabCache.Count; i++)
                {
                    if (string.Equals(_detailStripTagMenuVocabCache[i], tag, StringComparison.OrdinalIgnoreCase))
                    {
                        inVocab = true;
                        break;
                    }
                }
                if (!inVocab) _detailStripTagMenuVocabCache.Add(tag);

                if (_detailStripTagMenuAppliedOrder == null)
                    _detailStripTagMenuAppliedOrder = new List<string>(8);
                bool inOrder = false;
                for (int i = 0; i < _detailStripTagMenuAppliedOrder.Count; i++)
                {
                    if (string.Equals(_detailStripTagMenuAppliedOrder[i], tag, StringComparison.OrdinalIgnoreCase))
                    {
                        inOrder = true;
                        break;
                    }
                }
                if (!inOrder) _detailStripTagMenuAppliedOrder.Add(tag);

                // Keep Mixed→On chrome honest until CacheAppliedUserTagsForSelection reruns.
                if (_userTagSelectionStates != null)
                    _userTagSelectionStates[tag] = UserTagSelectionState.On;
                if (_userTagSelectionRowCount > 0)
                    DetailStripOptimisticAppliedCountSet(tag, _userTagSelectionRowCount);
            }
            else
            {
                _detailStripTagMenuAppliedCache.Remove(tag);
                if (_detailStripTagMenuAppliedOrder != null)
                {
                    for (int i = _detailStripTagMenuAppliedOrder.Count - 1; i >= 0; i--)
                    {
                        if (string.Equals(_detailStripTagMenuAppliedOrder[i], tag, StringComparison.OrdinalIgnoreCase))
                            _detailStripTagMenuAppliedOrder.RemoveAt(i);
                    }
                }
                if (_userTagSelectionStates != null)
                    _userTagSelectionStates.Remove(tag);
                DetailStripOptimisticAppliedCountRemove(tag);
            }
        }

        private void DetailStripOptimisticAppliedCountSet(string tag, int count)
        {
            if (string.IsNullOrEmpty(tag) || cachedAppliedUserTagsSelection == null) return;
            for (int i = 0; i < cachedAppliedUserTagsSelection.Count; i++)
            {
                if (string.Equals(cachedAppliedUserTagsSelection[i].Name, tag, StringComparison.OrdinalIgnoreCase))
                {
                    var e = cachedAppliedUserTagsSelection[i];
                    e.Count = count;
                    cachedAppliedUserTagsSelection[i] = e;
                    return;
                }
            }
            cachedAppliedUserTagsSelection.Add(new UserTagSideTabEntry { Name = tag, Count = count });
        }

        private void DetailStripOptimisticAppliedCountRemove(string tag)
        {
            if (string.IsNullOrEmpty(tag) || cachedAppliedUserTagsSelection == null) return;
            for (int i = cachedAppliedUserTagsSelection.Count - 1; i >= 0; i--)
            {
                if (string.Equals(cachedAppliedUserTagsSelection[i].Name, tag, StringComparison.OrdinalIgnoreCase))
                    cachedAppliedUserTagsSelection.RemoveAt(i);
            }
        }

        /// <summary>Rebuild lists from current caches (no DB re-read). Keeps optimistic state.</summary>
        private void DetailStripRebuildTagMenuFromCaches()
        {
            if (_detailStripTagMenuRoot == null || !_detailStripTagMenuRoot.activeSelf) return;
            DetailStripUpdateTagMenuCreateButton();
            DetailStripRebuildTagMenuList(fullLayoutSync: false);
        }

        private void DetailStripCloseTagMenu()
        {
            DetailStripStopTagMenuFilterRebuild();
            DetailStripClearAppliedReorderHint();
            try { DetailStripSetTagMenuRemoveDragHint(false, false); } catch { }
            try { CloseTagCategoryEditorModal(); } catch { }
            if (_userTagEditorMergeModalGo != null) _userTagEditorMergeModalGo.SetActive(false);
            if (_userTagEditorRenameModalGo != null) _userTagEditorRenameModalGo.SetActive(false);
            _detailStripTagMenuFocusIdx = -1;
            if (_detailStripTagMenuCollapsed)
            {
                _detailStripTagMenuCollapsed = false;
                _detailStripTagMenuCollapsedTopLeftPos = null;
                try
                {
                    float s = ChromeScale > 0f ? ChromeScale : 1f;
                    DetailStripSyncTagMenuCollapseChrome(GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s);
                }
                catch { }
            }
            if (_detailStripTagMenuRoot != null)
                _detailStripTagMenuRoot.SetActive(false);
            _detailStripTagMenuFilter = "";
            _detailStripTagMenuSelectionKey = "";
            // Keep applied order after close so strip chips stay in last rearrange until selection changes.
            if (_detailStripTagMenuSearch != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_detailStripTagMenuSearch.text))
                        _detailStripTagMenuSearch.text = "";
                }
                catch { }
            }
            DetailStripRefreshTagMenuFilterClearVisible();
        }

        private void DetailStripToggleTagMenu()
        {
            DetailStripEnsureTagMenu();
            if (_detailStripTagMenuRoot == null) return;

            if (_detailStripTagMenuRoot.activeSelf)
            {
                DetailStripCloseTagMenu();
                return;
            }

            DetailStripOpenTagMenu(DetailStripTagMenuMode.Apply);
        }

        private static void DetailStripDisableTagMenuBackdrop(GameObject root)
        {
            if (root == null) return;
            Transform backdrop = root.transform.Find("Backdrop");
            if (backdrop == null) return;
            Image img = backdrop.GetComponent<Image>();
            if (img != null) img.raycastTarget = false;
            Button btn = backdrop.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.enabled = false;
            }
        }

        private void DetailStripOnTagMenuDragged()
        {
            _detailStripTagMenuDragged = true;
            if (_detailStripTagMenuPanelRT != null)
            {
                // Persist as center (legacy + Settings/QuickFilters) — panel uses top-left pivot.
                Vector2 center = DetailStripTagMenuTopLeftToCenter(
                    _detailStripTagMenuPanelRT.anchoredPosition, _detailStripTagMenuPanelRT.sizeDelta);
                _detailStripTagMenuSavedPos = center;
                DetailStripPersistTagMenuPos(center);
            }
        }

        /// <summary>
        /// Soft-wire footer drag on existing tag menus built before FooterDragArea existed.
        /// </summary>
        private void DetailStripEnsureTagMenuFooterDrag()
        {
            if (_detailStripTagMenuSearchRowGO == null || _detailStripTagMenuPanelRT == null) return;
            if (_detailStripTagMenuSearchRowGO.transform.Find("FooterDragArea") != null) return;

            GameObject footerDragArea = UI.CreateFloatFooterDragArea(_detailStripTagMenuSearchRowGO);
            if (footerDragArea != null)
            {
                var footerDrag = footerDragArea.AddComponent<DetailStripTagMenuDrag>();
                footerDrag.Target = _detailStripTagMenuPanelRT;
                footerDrag.OnMoved = DetailStripOnTagMenuDragged;
            }

            Transform spacerTr = _detailStripTagMenuSearchRowGO.transform.Find("Spacer");
            if (spacerTr != null)
            {
                GameObject spacer = spacerTr.gameObject;
                if (spacer.GetComponent<DetailStripTagMenuDrag>() == null)
                {
                    UI.EnsureFloatFooterSpacerDragHit(spacer);
                    var spacerDrag = spacer.AddComponent<DetailStripTagMenuDrag>();
                    spacerDrag.Target = _detailStripTagMenuPanelRT;
                    spacerDrag.OnMoved = DetailStripOnTagMenuDragged;
                }
            }
        }

        private void DetailStripOnTagMenuResized()
        {
            if (_detailStripTagMenuPanelRT == null) return;
            float s = ChromeScale > 0f ? ChromeScale : 1f;
            Vector2 sizePx = _detailStripTagMenuPanelRT.sizeDelta;
            Vector2 sizeRef = new Vector2(sizePx.x / s, sizePx.y / s);
            sizeRef.x = Mathf.Clamp(sizeRef.x, DetailStripTagMenuMinWidthRef, DetailStripTagMenuMaxWidthRef);
            sizeRef.y = Mathf.Clamp(sizeRef.y, DetailStripTagMenuMinHeightRef, DetailStripTagMenuMaxHeightRef);
            _detailStripTagMenuSavedSize = sizeRef;
            DetailStripPersistTagMenuSize(sizeRef);
            DetailStripRefreshTagMenuColumnWidths(s);
            if (_detailStripTagMenuDragged)
            {
                Vector2 center = DetailStripTagMenuTopLeftToCenter(
                    _detailStripTagMenuPanelRT.anchoredPosition, _detailStripTagMenuPanelRT.sizeDelta);
                _detailStripTagMenuSavedPos = center;
                DetailStripPersistTagMenuPos(center);
            }
        }

        private void DetailStripRefreshTagMenuColumnWidths(float s)
        {
            if (s <= 0f) s = 1f;
            float panelW = DetailStripTagMenuWidthRef * s;
            if (_detailStripTagMenuPanelRT != null && _detailStripTagMenuPanelRT.sizeDelta.x > 1f)
                panelW = _detailStripTagMenuPanelRT.sizeDelta.x;
            float colW = Mathf.Max(80f * s, (panelW - 16f * s - DetailStripTagMenuColGapRef * s) * 0.5f);
            DetailStripSetTagMenuScrollWidth(_detailStripTagMenuAppliedScrollGO, colW);
            DetailStripSetTagMenuScrollWidth(_detailStripTagMenuAvailableScrollGO, colW);
        }

        private static void DetailStripSetTagMenuScrollWidth(GameObject scrollGO, float colW)
        {
            if (scrollGO == null) return;
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            if (scrollRT == null) return;
            scrollRT.sizeDelta = new Vector2(colW, scrollRT.sizeDelta.y);
        }

        private void DetailStripLoadTagMenuSavedPosFromConfig()
        {
            if (_detailStripTagMenuSavedPos.HasValue) return;
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryDetailStripTagMenuPosSaved)
                    return;
                _detailStripTagMenuSavedPos = new Vector2(
                    VPBConfig.Instance.GalleryDetailStripTagMenuPosX,
                    VPBConfig.Instance.GalleryDetailStripTagMenuPosY);
            }
            catch { }
        }

        private void DetailStripLoadTagMenuSavedSizeFromConfig()
        {
            if (_detailStripTagMenuSavedSize.HasValue) return;
            try
            {
                if (VPBConfig.Instance == null || !VPBConfig.Instance.GalleryDetailStripTagMenuSizeSaved)
                    return;
                float w = VPBConfig.Instance.GalleryDetailStripTagMenuWidthRef;
                float h = VPBConfig.Instance.GalleryDetailStripTagMenuHeightRef;
                if (w < DetailStripTagMenuMinWidthRef || h < DetailStripTagMenuMinHeightRef)
                    return;
                _detailStripTagMenuSavedSize = new Vector2(
                    Mathf.Clamp(w, DetailStripTagMenuMinWidthRef, DetailStripTagMenuMaxWidthRef),
                    Mathf.Clamp(h, DetailStripTagMenuMinHeightRef, DetailStripTagMenuMaxHeightRef));
            }
            catch { }
        }

        private Vector2 DetailStripResolveTagMenuSizeRef()
        {
            DetailStripLoadTagMenuSavedSizeFromConfig();
            if (_detailStripTagMenuSavedSize.HasValue)
                return _detailStripTagMenuSavedSize.Value;
            return new Vector2(DetailStripTagMenuWidthRef, DetailStripTagMenuHeightRef);
        }

        private void DetailStripPersistTagMenuPos(Vector2 pos)
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.GalleryDetailStripTagMenuPosSaved = true;
                VPBConfig.Instance.GalleryDetailStripTagMenuPosX = pos.x;
                VPBConfig.Instance.GalleryDetailStripTagMenuPosY = pos.y;
            }
            catch { return; }
            DetailStripScheduleTagMenuPosSave();
        }

        private void DetailStripPersistTagMenuSize(Vector2 sizeRef)
        {
            try
            {
                if (VPBConfig.Instance == null) return;
                VPBConfig.Instance.GalleryDetailStripTagMenuSizeSaved = true;
                VPBConfig.Instance.GalleryDetailStripTagMenuWidthRef = sizeRef.x;
                VPBConfig.Instance.GalleryDetailStripTagMenuHeightRef = sizeRef.y;
            }
            catch { return; }
            DetailStripScheduleTagMenuPosSave();
        }

        private void DetailStripScheduleTagMenuPosSave()
        {
            if (!isActiveAndEnabled) return;
            try
            {
                if (_detailStripTagMenuPosSaveCo != null)
                    StopCoroutine(_detailStripTagMenuPosSaveCo);
            }
            catch { }
            try { _detailStripTagMenuPosSaveCo = StartCoroutine(DetailStripTagMenuPosSaveCo()); }
            catch { _detailStripTagMenuPosSaveCo = null; }
        }

        private IEnumerator DetailStripTagMenuPosSaveCo()
        {
            yield return new WaitForSecondsRealtime(0.35f);
            _detailStripTagMenuPosSaveCo = null;
            try
            {
                if (VPBConfig.Instance != null)
                    VPBConfig.Instance.Save(false);
            }
            catch { }
        }

        /// <summary>
        /// Free float like Settings/Plugins — no host clamp (user may park off-screen).
        /// Kept as call-site hook so open/layout paths stay stable.
        /// </summary>
        private void DetailStripClampTagMenuPanelInView()
        {
        }

        /// <summary>Saved tag-menu pos is panel center (legacy center-pivot + Settings/QuickFilters).</summary>
        private static Vector2 DetailStripTagMenuCenterToTopLeft(Vector2 center, Vector2 size)
        {
            return new Vector2(center.x - size.x * 0.5f, center.y + size.y * 0.5f);
        }

        private static Vector2 DetailStripTagMenuTopLeftToCenter(Vector2 topLeft, Vector2 size)
        {
            return new Vector2(topLeft.x + size.x * 0.5f, topLeft.y - size.y * 0.5f);
        }

        private void DetailStripUpdateTagMenuColumnLabels(int appliedCount, int availCount)
        {
            _detailStripTagMenuLastAppliedCount = appliedCount;
            _detailStripTagMenuLastAvailCount = availCount;
            if (_detailStripTagMenuRemoveHintActive) return;
            if (_detailStripTagMenuAppliedLabel != null)
            {
                _detailStripTagMenuAppliedLabel.text = string.Format(
                    VPBTranslation.T("gallery.detail.tag_applied_count_fmt", "Applied ({0})"),
                    appliedCount);
                _detailStripTagMenuAppliedLabel.color = UI.PopupMutedText;
            }
            if (_detailStripTagMenuAvailableLabel != null)
            {
                _detailStripTagMenuAvailableLabel.text = string.Format(
                    VPBTranslation.T("gallery.detail.tag_available_count_fmt", "Available ({0})"),
                    availCount);
                _detailStripTagMenuAvailableLabel.color = UI.PopupMutedText;
            }
        }

        /// <summary>While dragging Applied → Available: cue drop-to-remove target.</summary>
        internal void DetailStripSetTagMenuRemoveDragHint(bool dragging, bool overAvailable)
        {
            if (!dragging)
            {
                _detailStripTagMenuRemoveHintActive = false;
                DetailStripUpdateTagMenuColumnLabels(
                    _detailStripTagMenuLastAppliedCount, _detailStripTagMenuLastAvailCount);
                DetailStripApplyTagMenuColumnFill(_detailStripTagMenuAvailableScrollGO);
                return;
            }

            _detailStripTagMenuRemoveHintActive = true;
            if (_detailStripTagMenuAvailableLabel != null)
            {
                _detailStripTagMenuAvailableLabel.text = overAvailable
                    ? VPBTranslation.T("gallery.detail.tag_drop_remove_active", "Drop to remove")
                    : VPBTranslation.T("gallery.detail.tag_drop_remove", "Drop here to remove");
                _detailStripTagMenuAvailableLabel.color = overAvailable
                    ? DetailStripActionDanger
                    : DetailStripColorTag;
            }
            if (_detailStripTagMenuAvailableScrollGO != null)
            {
                Image img = _detailStripTagMenuAvailableScrollGO.GetComponent<Image>();
                if (img != null)
                {
                    img.color = overAvailable
                        ? new Color(0.28f, 0.14f, 0.14f, 1f)
                        : new Color(0.16f, 0.16f, 0.22f, 1f);
                }
            }
        }

        internal bool DetailStripScreenPosOverAvailableList(Vector2 screenPos)
        {
            if (_detailStripTagMenuAvailableScrollGO == null) return false;
            RectTransform rt = _detailStripTagMenuAvailableScrollGO.GetComponent<RectTransform>();
            if (rt == null) rt = _detailStripTagMenuAvailableListGO != null
                ? _detailStripTagMenuAvailableListGO.GetComponent<RectTransform>()
                : null;
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
        }

        private void DetailStripUpdateTagMenuSelectionChrome()
        {
            if (_detailStripTagMenuMode == DetailStripTagMenuMode.Database)
            {
                DetailStripSyncTagMenuModeTipAndTitle();
                return;
            }
            int n = selectedFiles != null ? selectedFiles.Count : 0;
            bool multi = n > 1;
            if (_detailStripTagMenuSelText != null)
            {
                if (n <= 0)
                {
                    _detailStripTagMenuSelText.text = VPBTranslation.T(
                        "gallery.detail.tag_menu_drag", "Tags");
                    _detailStripTagMenuSelText.color = UI.PopupText;
                }
                else if (multi)
                {
                    _detailStripTagMenuSelText.text = string.Format(
                        VPBTranslation.T("gallery.detail.tag_menu_multi_fmt", "Tagging {0} items"), n);
                    _detailStripTagMenuSelText.color = DetailStripColorTag;
                }
                else
                {
                    _detailStripTagMenuSelText.text = VPBTranslation.T(
                        "gallery.detail.tag_menu_one", "Tagging 1 item");
                    _detailStripTagMenuSelText.color = UI.PopupText;
                }
                _detailStripTagMenuSelText.alignment = TextAnchor.MiddleLeft;
                _detailStripTagMenuSelText.fontStyle = FontStyle.Bold;
            }
            if (_detailStripTagMenuHeaderGO != null)
            {
                Image headerImg = _detailStripTagMenuHeaderGO.GetComponent<Image>();
                if (headerImg != null)
                    headerImg.color = multi ? DetailStripTagMenuTitleBarBgMulti : DetailStripTagMenuTitleBarBg;
            }
        }

        /// <summary>Identity + applied-tags fingerprint — changes when selection or tags for selection change.</summary>
        private string BuildDetailStripTagMenuSelectionKey()
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return "";
            bool historyBrowse = activeContentType == ContentType.History;
            var sb = new StringBuilder(192);
            sb.Append(selectedFiles.Count);
            var keys = new List<string>(selectedFiles.Count);
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                keys.Add(GetSelectionIdentityKey(f, historyBrowse) + "#" + DetailStripUserTagsFingerprint(f));
            }
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < keys.Count; i++)
            {
                sb.Append('|');
                sb.Append(keys[i]);
            }
            return sb.ToString();
        }

        /// <summary>Live-sync Applied/Add when selection (or its tags) changes while popup open.</summary>
        private void DetailStripSyncOpenTagMenuIfSelectionChanged(bool force = false)
        {
            if (_detailStripTagMenuRoot == null || !_detailStripTagMenuRoot.activeSelf) return;
            if (_detailStripTagMenuMode == DetailStripTagMenuMode.Database)
            {
                // Database mode is vocab-scoped — still refresh title count if cache dirty.
                DetailStripSyncTagMenuModeTipAndTitle();
                return;
            }
            string key = BuildDetailStripTagMenuSelectionKey();
            if (!force && string.Equals(key, _detailStripTagMenuSelectionKey, StringComparison.Ordinal))
                return;
            bool selectionChanged = !string.Equals(key, _detailStripTagMenuSelectionKey, StringComparison.Ordinal);
            _detailStripTagMenuSelectionKey = key;
            DetailStripInvalidateTagMenuCaches();
            DetailStripEnsureTagMenuCaches();
            if (selectionChanged || _detailStripTagMenuAppliedOrder == null)
                _detailStripTagMenuAppliedOrder = DetailStripOrderTagsWithSession(_detailStripTagMenuAppliedCache);
            DetailStripUpdateTagMenuSelectionChrome();
            DetailStripRebuildTagMenuList(fullLayoutSync: false);
        }

        /// <summary>Re-read applied/vocab after DB mutate (open menu only).</summary>
        private void DetailStripRefreshTagMenuAfterMutation()
        {
            if (_detailStripTagMenuRoot == null || !_detailStripTagMenuRoot.activeSelf) return;
            if (_detailStripTagMenuMode == DetailStripTagMenuMode.Database)
            {
                userTagsCached = false;
                RebuildUserTagEditorRows();
                DetailStripSyncTagMenuModeTipAndTitle();
                return;
            }
            _detailStripTagMenuSelectionKey = "";
            DetailStripSyncOpenTagMenuIfSelectionChanged(force: true);
        }

        private void DetailStripInvalidateTagMenuCaches()
        {
            _detailStripTagMenuVocabCache = null;
            _detailStripTagMenuAppliedCache = null;
        }

        private void DetailStripEnsureTagMenuCaches()
        {
            if (_detailStripTagMenuAppliedCache == null)
                _detailStripTagMenuAppliedCache = DetailStripCollectAppliedTagsForMenu();
            if (_detailStripTagMenuVocabCache == null)
            {
                _detailStripTagMenuVocabCache = new List<string>(64);
                try { VpbLocalDatabase.TryReadAllGalleryUserTagNames(_detailStripTagMenuVocabCache); }
                catch { }
            }
        }

        private HashSet<string> DetailStripCollectAppliedTagsForMenu()
        {
            var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedFiles == null || selectedFiles.Count == 0) return applied;

            // Prefer selection On+Mixed so multi-select partials show in Applied.
            try { CacheAppliedUserTagsForSelection(); } catch { }
            if (_userTagSelectionRowCount > 0 && _userTagSelectionStates != null && _userTagSelectionStates.Count > 0)
            {
                foreach (var kv in _userTagSelectionStates)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    if (kv.Value == UserTagSelectionState.On || kv.Value == UserTagSelectionState.Mixed)
                        applied.Add(kv.Key);
                }
                return applied;
            }

            if (selectedFiles.Count == 1)
                return DetailStripCollectUserTags(selectedFiles[0]);

            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                HashSet<string> row = DetailStripCollectUserTags(f);
                if (i == 0) applied = new HashSet<string>(row, StringComparer.OrdinalIgnoreCase);
                else applied.IntersectWith(row);
            }
            return applied;
        }

        private void DetailStripNoteTagMenuRecent(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            string norm = tag;
            try
            {
                string n = VpbLocalDatabase.NormalizeGalleryUserTagName(tag);
                if (!string.IsNullOrEmpty(n)) norm = n;
            }
            catch { }
            if (string.IsNullOrEmpty(norm)) return;

            for (int i = _detailStripTagMenuRecent.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_detailStripTagMenuRecent[i], norm, StringComparison.OrdinalIgnoreCase))
                    _detailStripTagMenuRecent.RemoveAt(i);
            }
            _detailStripTagMenuRecent.Insert(0, norm);
            while (_detailStripTagMenuRecent.Count > DetailStripTagMenuRecentMax)
                _detailStripTagMenuRecent.RemoveAt(_detailStripTagMenuRecent.Count - 1);
        }

        /// <summary>
        /// Add-column order: pinned first (investment), then user sort (A→Z / Z→A / count).
        /// Name A→Z also floats session-recent (recognition). Filter only applies here —
        /// Applied column stays full for remove/reorder.
        /// </summary>
        private List<string> DetailStripOrderAvailableTagsForMenu(List<string> vocab, HashSet<string> applied, string filter)
        {
            var result = new List<string>(64);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (applied == null) applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            SortState sort = GetSortState(DetailStripTagMenuAvailSortContext);
            bool byCount = sort != null && sort.Type == SortType.Count;
            bool nameAsc = !byCount && (sort == null || sort.Direction == SortDirection.Ascending);
            if (byCount)
            {
                try
                {
                    if (!userTagsCached) CacheUserTagsSideTab();
                }
                catch { }
            }

            try { EnsureUserTagPinOrderRuntimeLoaded(); } catch { }
            if (_userTagPinOrderRuntime != null)
            {
                for (int i = 0; i < _userTagPinOrderRuntime.Count; i++)
                {
                    string pin = _userTagPinOrderRuntime[i];
                    if (string.IsNullOrEmpty(pin) || applied.Contains(pin)) continue;
                    if (!DetailStripTagMatchesFilter(pin, filter)) continue;
                    if (!seen.Add(pin)) continue;
                    result.Add(pin);
                }
            }

            // Default A→Z: keep recent near top (recognition). Other modes: pure sort after pins.
            if (nameAsc)
            {
                for (int i = 0; i < _detailStripTagMenuRecent.Count; i++)
                {
                    string recent = _detailStripTagMenuRecent[i];
                    if (string.IsNullOrEmpty(recent) || applied.Contains(recent)) continue;
                    if (!DetailStripTagMatchesFilter(recent, filter)) continue;
                    if (!seen.Add(recent)) continue;
                    result.Add(recent);
                }
            }

            var rest = new List<string>(vocab != null ? vocab.Count : 8);
            if (vocab != null)
            {
                for (int i = 0; i < vocab.Count; i++)
                {
                    string tag = vocab[i];
                    if (string.IsNullOrEmpty(tag) || applied.Contains(tag)) continue;
                    if (!DetailStripTagMatchesFilter(tag, filter)) continue;
                    if (!seen.Add(tag)) continue;
                    rest.Add(tag);
                }
            }

            if (byCount)
            {
                bool asc = sort.Direction == SortDirection.Ascending;
                rest.Sort((a, b) =>
                {
                    int ca = DetailStripTagMenuUsageCount(a);
                    int cb = DetailStripTagMenuUsageCount(b);
                    int cmp = asc ? ca.CompareTo(cb) : cb.CompareTo(ca);
                    if (cmp != 0) return cmp;
                    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });
            }
            else
            {
                bool asc = sort == null || sort.Direction == SortDirection.Ascending;
                if (asc)
                    rest.Sort(StringComparer.OrdinalIgnoreCase);
                else
                    rest.Sort((a, b) => string.Compare(b, a, StringComparison.OrdinalIgnoreCase));
            }

            result.AddRange(rest);
            return result;
        }

        private int DetailStripTagMenuUsageCount(string tag)
        {
            if (string.IsNullOrEmpty(tag) || cachedUserTagSideTab == null) return 0;
            for (int i = 0; i < cachedUserTagSideTab.Count; i++)
            {
                if (string.Equals(cachedUserTagSideTab[i].Name, tag, StringComparison.OrdinalIgnoreCase))
                    return cachedUserTagSideTab[i].Count;
            }
            return 0;
        }

        private int DetailStripTagMenuSelectionHitCount(string tag)
        {
            if (string.IsNullOrEmpty(tag) || cachedAppliedUserTagsSelection == null) return 0;
            for (int i = 0; i < cachedAppliedUserTagsSelection.Count; i++)
            {
                if (string.Equals(cachedAppliedUserTagsSelection[i].Name, tag, StringComparison.OrdinalIgnoreCase))
                    return cachedAppliedUserTagsSelection[i].Count;
            }
            return 0;
        }

        private string DetailStripFormatTagMenuRowLabel(string tag, UserTagSelectionState state)
        {
            if (string.IsNullOrEmpty(tag)) return "";
            if (state == UserTagSelectionState.On)
                return "\u2713  " + tag;
            if (state == UserTagSelectionState.Mixed)
            {
                int hit = DetailStripTagMenuSelectionHitCount(tag);
                int total = _userTagSelectionRowCount > 0
                    ? _userTagSelectionRowCount
                    : (selectedFiles != null ? selectedFiles.Count : 0);
                if (total > 0 && hit > 0)
                {
                    return "\u25D1  " + string.Format(
                        VPBTranslation.T("gallery.detail.tag_partial_fmt", "{0} ({1}/{2})"),
                        tag, hit, total);
                }
                return "\u25D1  " + tag;
            }
            return tag;
        }

        private void DetailStripStopTagMenuFilterRebuild()
        {
            if (_detailStripTagMenuFilterCo == null) return;
            try { StopCoroutine(_detailStripTagMenuFilterCo); } catch { }
            _detailStripTagMenuFilterCo = null;
        }

        private void DetailStripScheduleTagMenuFilterRebuild()
        {
            DetailStripStopTagMenuFilterRebuild();
            try { _detailStripTagMenuFilterCo = StartCoroutine(DetailStripTagMenuFilterRebuildCo()); }
            catch { DetailStripRebuildTagMenuList(fullLayoutSync: false); }
        }

        private IEnumerator DetailStripTagMenuFilterRebuildCo()
        {
            yield return new WaitForSecondsRealtime(DetailStripTagMenuFilterDebounceSec);
            _detailStripTagMenuFilterCo = null;
            if (DetailStripTagMenuIsDatabaseMode())
                RebuildUserTagEditorRows();
            else
                DetailStripRebuildTagMenuList(fullLayoutSync: false);
        }

        private void DetailStripUpdateTagMenuCreateButton()
        {
            if (_detailStripTagMenuCreateGO == null) return;
            if (_detailStripTagMenuMode == DetailStripTagMenuMode.Database)
            {
                _detailStripTagMenuCreateGO.SetActive(false);
                return;
            }
            string filter = (_detailStripTagMenuFilter ?? "").Trim();
            bool exists = false;
            if (!string.IsNullOrEmpty(filter))
            {
                DetailStripEnsureTagMenuCaches();
                if (_detailStripTagMenuAppliedCache != null
                    && _detailStripTagMenuAppliedCache.Contains(filter))
                    exists = true;
                else if (_detailStripTagMenuVocabCache != null)
                {
                    for (int i = 0; i < _detailStripTagMenuVocabCache.Count; i++)
                    {
                        if (string.Equals(_detailStripTagMenuVocabCache[i], filter, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }
            }

            bool showCreate = !string.IsNullOrEmpty(filter) && !exists;
            _detailStripTagMenuCreateGO.SetActive(showCreate);
            if (showCreate && _detailStripTagMenuCreateText != null)
            {
                _detailStripTagMenuCreateText.text = string.Format(
                    VPBTranslation.T("gallery.detail.tag_create_fmt", "Create \"{0}\""), filter);
            }
        }

        /// <summary>
        /// Pane center in tag-menu root local space. Popup lives on canvas (free drag) but opens
        /// centered on <see cref="backgroundBoxGO"/> — including docked/offset pane positions.
        /// </summary>
        private Vector2 DetailStripTagMenuPaneCenterInRoot()
        {
            RectTransform paneRT = backgroundBoxGO != null ? backgroundBoxGO.GetComponent<RectTransform>() : null;
            RectTransform rootRT = _detailStripTagMenuRoot != null
                ? _detailStripTagMenuRoot.GetComponent<RectTransform>()
                : null;
            if (paneRT == null) return Vector2.zero;
            if (rootRT == null) return paneRT.anchoredPosition;

            Camera cam = null;
            try
            {
                Canvas c = rootRT.GetComponentInParent<Canvas>();
                if (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = c.worldCamera != null ? c.worldCamera : Camera.main;
            }
            catch { }

            Vector3 paneWorld = paneRT.TransformPoint(paneRT.rect.center);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, paneWorld);
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRT, screen, cam, out local))
                return local;

            // Same-canvas fallback: centre-anchored pane offset is already canvas-local.
            return paneRT.anchoredPosition;
        }

        private void DetailStripPositionTagMenu()
        {
            if (_detailStripTagMenuPanelRT == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            Vector2 sizeRef = DetailStripResolveTagMenuSizeRef();
            Vector2 sizePx = new Vector2(sizeRef.x * s, sizeRef.y * s);
            _detailStripTagMenuPanelRT.anchorMin = _detailStripTagMenuPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
            _detailStripTagMenuPanelRT.pivot = new Vector2(0f, 1f);
            _detailStripTagMenuPanelRT.sizeDelta = sizePx;
            if (!_detailStripTagMenuDragged)
            {
                Vector2 center = DetailStripTagMenuPaneCenterInRoot();
                _detailStripTagMenuPanelRT.anchoredPosition =
                    DetailStripTagMenuCenterToTopLeft(center, sizePx);
            }
            DetailStripClampTagMenuPanelInView();
        }

        private void DetailStripSyncTagMenuLayout(float s)
        {
            if (_detailStripTagMenuPanelGO == null) return;
            if (s <= 0f) s = 1f;
            DetailStripApplyTagMenuPanelChrome();
            if (_detailStripTagMenuPanelRT != null)
            {
                Vector2 sizeRef = DetailStripResolveTagMenuSizeRef();
                Vector2 sizePx = new Vector2(sizeRef.x * s, sizeRef.y * s);
                _detailStripTagMenuPanelRT.anchorMin = _detailStripTagMenuPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                _detailStripTagMenuPanelRT.pivot = new Vector2(0f, 1f);
                _detailStripTagMenuPanelRT.sizeDelta = sizePx;
                if (!_detailStripTagMenuDragged)
                {
                    Vector2 center = DetailStripTagMenuPaneCenterInRoot();
                    _detailStripTagMenuPanelRT.anchoredPosition =
                        DetailStripTagMenuCenterToTopLeft(center, sizePx);
                }
                DetailStripClampTagMenuPanelInView();
            }

            VerticalLayoutGroup panelVlg = _detailStripTagMenuPanelGO.GetComponent<VerticalLayoutGroup>();
            if (panelVlg != null)
            {
                panelVlg.padding = UI.Pad(8f, 8f, 10f, 8f, s);
                panelVlg.spacing = 6f * s;
            }

            if (_detailStripTagMenuHeaderGO != null)
            {
                float headerH = GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s;
                LayoutElement headerLe = _detailStripTagMenuHeaderGO.GetComponent<LayoutElement>();
                if (headerLe != null)
                {
                    headerLe.preferredHeight = headerH;
                    headerLe.minHeight = headerH;
                }
                HorizontalLayoutGroup headerHlg = _detailStripTagMenuHeaderGO.GetComponent<HorizontalLayoutGroup>();
                if (headerHlg != null)
                {
                    headerHlg.childForceExpandHeight = false;
                    headerHlg.childForceExpandWidth = false;
                    headerHlg.childControlWidth = true;
                    headerHlg.childControlHeight = true;
                    headerHlg.padding = UI.Pad(6f, 6f, 4f, 4f, s);
                    headerHlg.spacing = 4f * s;
                }
                if (_detailStripTagMenuSelText != null)
                {
                    GalleryUiMetrics.ApplyFont(_detailStripTagMenuSelText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                    _detailStripTagMenuSelText.alignment = TextAnchor.MiddleLeft;
                }
                Transform gripTr = _detailStripTagMenuHeaderGO.transform.Find("Grip");
                Text grip = gripTr != null ? gripTr.GetComponent<Text>() : null;
                if (grip != null)
                    GalleryUiMetrics.ApplyFont(grip, GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
                Transform winIconTr = _detailStripTagMenuHeaderGO.transform.Find("WindowIcon");
                if (winIconTr != null && winIconTr.Find("Icon") == null)
                {
                    try { UnityEngine.Object.Destroy(winIconTr.gameObject); } catch { }
                    winIconTr = null;
                }
                if (winIconTr == null)
                {
                    GameObject winIcon = UI.CreateFloatTitleWindowIcon(
                        _detailStripTagMenuHeaderGO, "vpb_icons/tags.png",
                        GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                    if (winIcon != null && gripTr != null)
                        winIcon.transform.SetSiblingIndex(gripTr.GetSiblingIndex() + 1);
                }
                else
                {
                    UI.LayoutFloatTitleWindowIcon(
                        _detailStripTagMenuHeaderGO,
                        GalleryUiDesignTokens.FloatTitleWindowIconSizeRef * s);
                }
                float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
                if (_detailStripTagMenuCollapseBtn != null)
                {
                    LayoutElement cLe = _detailStripTagMenuCollapseBtn.GetComponent<LayoutElement>();
                    if (cLe != null)
                    {
                        cLe.preferredWidth = cLe.minWidth = chromeSz;
                        cLe.preferredHeight = cLe.minHeight = chromeSz;
                    }
                }
                if (_detailStripTagMenuCloseGO != null)
                    DetailStripStyleTagMenuTitleClose(_detailStripTagMenuCloseGO, chromeSz);
                DetailStripUpdateTagMenuSelectionChrome();
            }

            if (_detailStripTagMenuFilterRowGO != null)
            {
                float chromeSz = GalleryUiDesignTokens.ButtonSizeRef * s;
                float filterH = chromeSz + 10f * s;
                LayoutElement filterLe = _detailStripTagMenuFilterRowGO.GetComponent<LayoutElement>();
                if (filterLe != null)
                {
                    filterLe.preferredHeight = filterH;
                    filterLe.minHeight = filterH;
                }
                HorizontalLayoutGroup filterHlg = _detailStripTagMenuFilterRowGO.GetComponent<HorizontalLayoutGroup>();
                if (filterHlg != null)
                    filterHlg.padding = UI.Pad(8f, 8f, 4f, 4f, s);
            }

            if (_detailStripTagMenuTipText != null)
            {
                GalleryUiMetrics.ApplyFont(
                    _detailStripTagMenuTipText,
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    s,
                    GalleryUiDesignTokens.FontMinRef);
                DetailStripSyncTagMenuModeTipAndTitle();
                Transform tipTr = _detailStripTagMenuTipText.transform.parent;
                if (tipTr != null)
                {
                    LayoutElement tipLe = tipTr.GetComponent<LayoutElement>();
                    if (tipLe != null)
                    {
                        tipLe.preferredHeight = DetailStripTagMenuSectionLabelHRef * s;
                        tipLe.minHeight = DetailStripTagMenuSectionLabelHRef * s;
                    }
                }
            }

            if (_detailStripTagMenuColumnsGO != null)
            {
                LayoutElement colLe = _detailStripTagMenuColumnsGO.GetComponent<LayoutElement>();
                if (colLe != null)
                {
                    colLe.minHeight = 80f * s;
                    // Soft floor — flexibleHeight fills remaining panel after chrome.
                    colLe.preferredHeight = 80f * s;
                    colLe.flexibleHeight = 1f;
                }
                HorizontalLayoutGroup hlg = _detailStripTagMenuColumnsGO.GetComponent<HorizontalLayoutGroup>();
                if (hlg != null) hlg.spacing = DetailStripTagMenuColGapRef * s;
            }

            DetailStripSyncTagMenuColumnChrome(_detailStripTagMenuAppliedLabel, _detailStripTagMenuAppliedScrollGO, _detailStripTagMenuAppliedListGO, s);
            DetailStripSyncTagMenuColumnChrome(_detailStripTagMenuAvailableLabel, _detailStripTagMenuAvailableScrollGO, _detailStripTagMenuAvailableListGO, s);
            DetailStripSyncTagMenuAvailSortChrome(s);
            DetailStripSyncTagMenuUnifiedChrome(s);

            float footerH = GalleryUiDesignTokens.QuickFiltersFooterHeightRef * s;
            float chromeBtn = GalleryUiDesignTokens.ButtonSizeRef * s;
            if (_detailStripTagMenuSearchRowGO != null)
            {
                LayoutElement rowLe = _detailStripTagMenuSearchRowGO.GetComponent<LayoutElement>();
                if (rowLe != null)
                {
                    rowLe.preferredHeight = footerH;
                    rowLe.minHeight = footerH;
                    rowLe.flexibleHeight = 0f;
                }
                HorizontalLayoutGroup rowHlg = _detailStripTagMenuSearchRowGO.GetComponent<HorizontalLayoutGroup>();
                if (rowHlg != null)
                {
                    rowHlg.childForceExpandHeight = false;
                    rowHlg.childControlHeight = true;
                    rowHlg.spacing = 6f * s;
                    rowHlg.padding = UI.Pad(8f, 8f, 4f, 4f, s);
                }
                _detailStripTagMenuSearchRowGO.transform.SetAsLastSibling();
            }

            if (_detailStripTagMenuFooterCloseGO != null)
            {
                float footerCloseW = 96f * s;
                LayoutElement footerLe = _detailStripTagMenuFooterCloseGO.GetComponent<LayoutElement>();
                if (footerLe != null)
                {
                    footerLe.preferredWidth = footerCloseW;
                    footerLe.preferredHeight = chromeBtn;
                    footerLe.minWidth = footerCloseW;
                    footerLe.minHeight = chromeBtn;
                    footerLe.flexibleWidth = 0f;
                    footerLe.flexibleHeight = 0f;
                }
                Text footerT = _detailStripTagMenuFooterCloseGO.GetComponentInChildren<Text>(true);
                if (footerT != null)
                {
                    footerT.text = VPBTranslation.T("gallery.detail.tag_menu_close", "Close");
                    GalleryUiMetrics.ApplyFont(footerT, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                }
            }
            Transform dockTr = _detailStripTagMenuSearchRowGO != null
                ? _detailStripTagMenuSearchRowGO.transform.Find("FooterDock") : null;
            if (dockTr != null)
            {
                float dockW = 96f * s;
                LayoutElement dockLe = dockTr.GetComponent<LayoutElement>();
                if (dockLe != null)
                {
                    dockLe.preferredWidth = dockW;
                    dockLe.preferredHeight = chromeBtn;
                    dockLe.minWidth = dockW;
                    dockLe.minHeight = chromeBtn;
                }
                Text dockT = dockTr.GetComponentInChildren<Text>(true);
                if (dockT != null)
                {
                    dockT.text = VPBTranslation.T("gallery.import.dock", "Dock");
                    GalleryUiMetrics.ApplyFont(dockT, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                }
            }

            if (_detailStripTagMenuCreateGO != null)
            {
                LayoutElement createLe = _detailStripTagMenuCreateGO.GetComponent<LayoutElement>();
                if (createLe != null)
                {
                    createLe.preferredHeight = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;
                    createLe.minHeight = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;
                }
                if (_detailStripTagMenuCreateText != null)
                    GalleryUiMetrics.ApplyFont(_detailStripTagMenuCreateText, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
            }

            if (_detailStripTagMenuSearch != null)
            {
                float filterFieldH = chromeBtn + 2f * s;
                LayoutElement searchLe = _detailStripTagMenuSearch.GetComponent<LayoutElement>();
                if (searchLe == null)
                    searchLe = UI.AddLE(_detailStripTagMenuSearch.gameObject);
                searchLe.minHeight = filterFieldH;
                searchLe.preferredHeight = filterFieldH;
                searchLe.flexibleWidth = 1f;
                searchLe.flexibleHeight = 0f;
                UI.LayoutChromeSearchIcon(_detailStripTagMenuSearch.gameObject, s);
                if (_detailStripTagMenuFilterClearGo != null)
                {
                    float clearSz = GalleryUiDesignTokens.SearchClearBtnSizeRef * s;
                    RectTransform clearRT = _detailStripTagMenuFilterClearGo.GetComponent<RectTransform>();
                    if (clearRT != null)
                        clearRT.sizeDelta = new Vector2(clearSz, 0f);
                    Transform textAreaTr = _detailStripTagMenuSearch.transform.Find("TextArea");
                    if (textAreaTr != null)
                    {
                        RectTransform taRt = textAreaTr as RectTransform;
                        if (taRt != null)
                            taRt.offsetMax = new Vector2(-clearSz, taRt.offsetMax.y);
                    }
                }
                DetailStripSyncTagMenuSearchPlaceholder();
                DetailStripRefreshTagMenuFilterClearVisible();
            }

            if (_detailStripTagMenuResizeGO != null)
            {
                float rhSz = GalleryUiDesignTokens.ButtonSizeRef * s;
                LayoutElement rhLe = _detailStripTagMenuResizeGO.GetComponent<LayoutElement>();
                if (rhLe == null) rhLe = UI.AddLE(_detailStripTagMenuResizeGO);
                rhLe.preferredWidth = rhSz;
                rhLe.preferredHeight = rhSz;
                rhLe.minWidth = rhSz;
                rhLe.minHeight = rhSz;
                rhLe.flexibleWidth = 0f;
                rhLe.flexibleHeight = 0f;
                RectTransform rhRT = _detailStripTagMenuResizeGO.GetComponent<RectTransform>();
                if (rhRT != null)
                    rhRT.sizeDelta = new Vector2(rhSz, rhSz);
                try { UI.ApplyBarIconFromPath(_detailStripTagMenuResizeGO, "vpb_icons/chevrons_down_right.png"); } catch { }
                if (_detailStripTagMenuSearchRowGO != null
                    && _detailStripTagMenuResizeGO.transform.parent != _detailStripTagMenuSearchRowGO.transform)
                    _detailStripTagMenuResizeGO.transform.SetParent(_detailStripTagMenuSearchRowGO.transform, false);
                _detailStripTagMenuResizeGO.transform.SetAsLastSibling();
            }

            if (_detailStripTagMenuCollapsed)
                DetailStripSyncTagMenuCollapseChrome(GalleryUiDesignTokens.QuickFiltersTitleBarHeightRef * s);
        }

        private void DetailStripSyncTagMenuColumnChrome(Text label, GameObject scrollGO, GameObject listGO, float s)
        {
            float colHeaderH = DetailStripTagMenuChromeBtnRef * s;
            if (label != null)
            {
                GalleryUiMetrics.ApplyFont(label, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                LayoutElement labelLe = label.GetComponent<LayoutElement>();
                if (labelLe != null)
                {
                    labelLe.preferredHeight = colHeaderH;
                    labelLe.minHeight = colHeaderH;
                }
                Transform headerTr = label.transform.parent;
                if (headerTr != null && headerTr.name == "Header")
                {
                    LayoutElement headerLe = headerTr.GetComponent<LayoutElement>();
                    if (headerLe != null)
                    {
                        headerLe.preferredHeight = colHeaderH;
                        headerLe.minHeight = colHeaderH;
                    }
                    HorizontalLayoutGroup headerHlg = headerTr.GetComponent<HorizontalLayoutGroup>();
                    if (headerHlg != null)
                    {
                        headerHlg.childForceExpandHeight = false;
                        headerHlg.childForceExpandWidth = false;
                    }
                }
            }

            if (scrollGO != null)
            {
                LayoutElement scrollLe = scrollGO.GetComponent<LayoutElement>();
                if (scrollLe == null) scrollLe = UI.AddLE(scrollGO);
                scrollLe.flexibleHeight = 1f;
                scrollLe.minHeight = 80f * s;
                scrollLe.preferredHeight = 80f * s;
                scrollLe.flexibleWidth = 1f;
                float panelW = DetailStripTagMenuWidthRef * s;
                if (_detailStripTagMenuPanelRT != null && _detailStripTagMenuPanelRT.sizeDelta.x > 1f)
                    panelW = _detailStripTagMenuPanelRT.sizeDelta.x;
                float colW = Mathf.Max(80f * s, (panelW - 16f * s - DetailStripTagMenuColGapRef * s) * 0.5f);
                RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
                if (scrollRT != null)
                    scrollRT.sizeDelta = new Vector2(colW, scrollRT.sizeDelta.y);
            }

            if (listGO != null)
            {
                RectTransform contentRT = listGO.GetComponent<RectTransform>();
                if (contentRT != null)
                    contentRT.sizeDelta = new Vector2(0f, contentRT.sizeDelta.y);

                ScaleVerticalPopupMenuRows(
                    listGO, s,
                    GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0f);

                if (contentRT != null)
                    contentRT.sizeDelta = new Vector2(0f, contentRT.sizeDelta.y);
            }
        }

        private void DetailStripSyncTagMenuAvailSortChrome(float s)
        {
            if (_detailStripTagMenuAvailSortBtnGO == null) return;
            float sortSq = DetailStripTagMenuChromeBtnRef * s;
            LayoutElement le = _detailStripTagMenuAvailSortBtnGO.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = sortSq;
                le.preferredHeight = sortSq;
                le.minWidth = sortSq;
                le.minHeight = sortSq;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 0f;
            }
            RectTransform rt = _detailStripTagMenuAvailSortBtnGO.GetComponent<RectTransform>();
            if (rt != null)
                rt.sizeDelta = new Vector2(sortSq, sortSq);
            DetailStripSyncTagMenuAvailSortIcon();
        }

        private void DetailStripRebuildTagMenuList(bool fullLayoutSync)
        {
            if (_detailStripTagMenuAppliedListGO == null || _detailStripTagMenuAvailableListGO == null) return;
            UI.DestroyAllChildren(_detailStripTagMenuAppliedListGO.transform);
            UI.DestroyAllChildren(_detailStripTagMenuAvailableListGO.transform);
            _detailStripTagMenuNav.Clear();
            int prevFocus = _detailStripTagMenuFocusIdx;
            _detailStripTagMenuFocusIdx = -1;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;
            string filter = (_detailStripTagMenuFilter ?? "").Trim();

            DetailStripEnsureTagMenuCaches();
            HashSet<string> applied = _detailStripTagMenuAppliedCache
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> vocab = _detailStripTagMenuVocabCache ?? new List<string>(0);

            int appliedRows = 0;
            if (applied.Count > 0)
            {
                var appliedList = DetailStripOrderTagsWithSession(applied);
                for (int i = 0; i < appliedList.Count; i++)
                {
                    string tag = appliedList[i];
                    // Filter scopes Add column only — Applied stays visible for remove/reorder.
                    if (appliedRows >= DetailStripTagMenuMaxRows) break;
                    string tagSnap = tag;
                    UserTagSelectionState st = GetUserTagSelectionState(tagSnap);
                    if (st == UserTagSelectionState.Off) st = UserTagSelectionState.On;
                    DetailStripAddTagMenuRow(
                        _detailStripTagMenuAppliedListGO, tagSnap, rowH, s, st,
                        () =>
                        {
                            toggleTagForSelectedItems(tagSnap);
                            bool stillApplied = st != UserTagSelectionState.On;
                            DetailStripOptimisticTagMenuSet(tagSnap, applied: stillApplied);
                            if (stillApplied) DetailStripNoteTagMenuRecent(tagSnap);
                            DetailStripRebuildTagMenuFromCaches();
                        },
                        allowDrag: true,
                        isAppliedColumn: true);
                    appliedRows++;
                }
            }
            if (appliedRows == 0)
            {
                DetailStripAddTagMenuEmptyRow(
                    _detailStripTagMenuAppliedListGO,
                    VPBTranslation.T("gallery.detail.tag_applied_empty", "None yet"),
                    rowH, s);
            }

            List<string> availableOrdered = DetailStripOrderAvailableTagsForMenu(vocab, applied, filter);
            int availTotal = availableOrdered.Count;
            int availRows = 0;
            int skipped = 0;
            for (int i = 0; i < availableOrdered.Count; i++)
            {
                string tag = availableOrdered[i];
                if (string.IsNullOrEmpty(tag)) continue;
                if (availRows >= DetailStripTagMenuMaxRows) { skipped++; continue; }
                string tagSnap = tag;
                DetailStripAddTagMenuRow(
                    _detailStripTagMenuAvailableListGO, tagSnap, rowH, s, UserTagSelectionState.Off,
                    () =>
                    {
                        ApplyUserTagsToFileEntries(new List<string> { tagSnap }, selectedFiles, remove: false);
                        DetailStripOptimisticTagMenuSet(tagSnap, applied: true);
                        DetailStripNoteTagMenuRecent(tagSnap);
                        DetailStripRebuildTagMenuFromCaches();
                    },
                    allowDrag: true,
                    isAppliedColumn: false);
                availRows++;
            }

            bool createWillShow = DetailStripTagMenuFilterWouldCreate(filter);
            if (availRows == 0 && skipped == 0)
            {
                // When Create row will appear, skip redundant "No match" empty (Create is the action).
                if (!(createWillShow && !string.IsNullOrEmpty(filter)))
                {
                    DetailStripAddTagMenuEmptyRow(
                        _detailStripTagMenuAvailableListGO,
                        string.IsNullOrEmpty(filter)
                            ? VPBTranslation.T("gallery.detail.tag_available_empty", "Type to create")
                            : VPBTranslation.T("gallery.detail.tag_available_none_match", "No match"),
                        rowH, s);
                }
            }
            else if (skipped > 0)
            {
                DetailStripAddTagMenuMoreRow(
                    _detailStripTagMenuAvailableListGO,
                    string.Format(VPBTranslation.T("gallery.detail.tag_more_fmt", "… {0} more — refine filter"), skipped),
                    rowH, s);
            }

            DetailStripUpdateTagMenuColumnLabels(appliedRows, availTotal);
            DetailStripUpdateTagMenuCreateButton();
            if (createWillShow && _detailStripTagMenuCreateGO != null)
                _detailStripTagMenuCreateGO.transform.SetSiblingIndex(
                    _detailStripTagMenuColumnsGO != null
                        ? _detailStripTagMenuColumnsGO.transform.GetSiblingIndex() + 1
                        : 0);

            if (prevFocus >= 0 && prevFocus < _detailStripTagMenuNav.Count)
                DetailStripTagMenuSetFocus(prevFocus, scrollIntoView: false);

            if (fullLayoutSync)
                DetailStripSyncTagMenuLayout(s);
            else
            {
                ScaleVerticalPopupMenuRows(
                    _detailStripTagMenuAppliedListGO, s,
                    GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0f);
                ScaleVerticalPopupMenuRows(
                    _detailStripTagMenuAvailableListGO, s,
                    GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0f);
                DetailStripKeepTagMenuListStretchWidth(_detailStripTagMenuAppliedListGO);
                DetailStripKeepTagMenuListStretchWidth(_detailStripTagMenuAvailableListGO);
            }
        }

        private bool DetailStripTagMenuFilterWouldCreate(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return false;
            DetailStripEnsureTagMenuCaches();
            if (_detailStripTagMenuAppliedCache != null && _detailStripTagMenuAppliedCache.Contains(filter))
                return false;
            if (_detailStripTagMenuVocabCache != null)
            {
                for (int i = 0; i < _detailStripTagMenuVocabCache.Count; i++)
                {
                    if (string.Equals(_detailStripTagMenuVocabCache[i], filter, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return true;
        }

        private static void DetailStripKeepTagMenuListStretchWidth(GameObject listGO)
        {
            if (listGO == null) return;
            RectTransform contentRT = listGO.GetComponent<RectTransform>();
            if (contentRT != null)
                contentRT.sizeDelta = new Vector2(0f, contentRT.sizeDelta.y);
        }

        private void DetailStripAddTagMenuRow(
            GameObject listGO, string label, float rowH, float s, UserTagSelectionState state, UnityAction onClick,
            bool allowDrag = false, bool isAppliedColumn = false)
        {
            if (listGO == null) return;
            string display = DetailStripFormatTagMenuRowLabel(label, state);
            bool isActive = state == UserTagSelectionState.On;
            GameObject row = UI.AddStretchPopupMenuRow(
                listGO.transform,
                display,
                onClick,
                isActive: isActive,
                enabled: true,
                rowHeight: rowH);
            if (row == null) return;
            Image rowImg = row.GetComponent<Image>();
            Color baseColor = rowImg != null ? rowImg.color : UI.PopupRowBackdrop;
            Text t = row.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                if (state == UserTagSelectionState.Mixed)
                    t.color = UI.PopupText;
            }
            if (state == UserTagSelectionState.Mixed && rowImg != null)
            {
                rowImg.color = UserTagStateMixedColor;
                baseColor = UserTagStateMixedColor;
            }
            bool pulsing = !string.IsNullOrEmpty(_userTagPulseTag)
                && string.Equals(_userTagPulseTag, label, StringComparison.OrdinalIgnoreCase)
                && Time.unscaledTime < _userTagPulseUntil;
            if (pulsing && rowImg != null)
            {
                rowImg.color = UserTagStatePulseColor;
                baseColor = UserTagStatePulseColor;
            }

            if (allowDrag && !string.IsNullOrEmpty(label))
            {
                UserTagPickDragSource pick = row.GetComponent<UserTagPickDragSource>();
                if (pick == null) pick = row.AddComponent<UserTagPickDragSource>();
                pick.Panel = this;
                pick.PrimaryTag = label;
                pick.IsAppliedRowDrag = false;
                pick.DetailStripAppliedReorder = isAppliedColumn;

                Button btn = row.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        if (pick != null && pick.ConsumedByDrag) return;
                        if (onClick != null) onClick();
                    });
                }
            }

            if (isAppliedColumn && !string.IsNullOrEmpty(label))
            {
                var marker = row.GetComponent<DetailStripAppliedTagRow>();
                if (marker == null) marker = row.AddComponent<DetailStripAppliedTagRow>();
                marker.TagName = label;
            }

            if (!string.IsNullOrEmpty(label) && onClick != null)
            {
                var nav = row.GetComponent<DetailStripTagMenuNavRow>();
                if (nav == null) nav = row.AddComponent<DetailStripTagMenuNavRow>();
                nav.TagName = label;
                nav.IsAppliedColumn = isAppliedColumn;
                nav.Activate = onClick;
                nav.RowImage = rowImg;
                nav.BaseColor = baseColor;
                _detailStripTagMenuNav.Add(nav);
            }
        }

        private void DetailStripAddTagMenuEmptyRow(GameObject listGO, string label, float rowH, float s)
        {
            if (listGO == null) return;
            GameObject row = UI.AddStretchPopupMenuRow(
                listGO.transform,
                label ?? "",
                () => { },
                isActive: false,
                enabled: false,
                rowHeight: rowH);
            if (row == null) return;
            Text t = row.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                t.color = UI.PopupMutedText;
            }
        }

        private void DetailStripAddTagMenuMoreRow(GameObject listGO, string label, float rowH, float s)
        {
            if (listGO == null) return;
            GameObject row = UI.AddStretchPopupMenuRow(
                listGO.transform,
                label ?? "",
                DetailStripFocusTagMenuSearchForRefine,
                isActive: false,
                enabled: true,
                rowHeight: rowH);
            if (row == null) return;
            Text t = row.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
                t.color = DetailStripColorTag;
            }
            Image rowImg = row.GetComponent<Image>();
            if (rowImg != null) rowImg.color = new Color(0.16f, 0.17f, 0.24f, 1f);
            AddTooltipPlain(row, VPBTranslation.T(
                "gallery.detail.tag_more_tip", "Click to focus filter — type to narrow results"));
        }

        private void DetailStripFocusTagMenuSearchForRefine()
        {
            if (_detailStripTagMenuSearch == null) return;
            try
            {
                _detailStripTagMenuSearch.ActivateInputField();
                _detailStripTagMenuSearch.MoveTextEnd(false);
            }
            catch { }
        }

        private void DetailStripTagMenuSetFocus(int index, bool scrollIntoView)
        {
            if (_detailStripTagMenuNav.Count == 0)
            {
                _detailStripTagMenuFocusIdx = -1;
                return;
            }
            index = Mathf.Clamp(index, 0, _detailStripTagMenuNav.Count - 1);
            for (int i = 0; i < _detailStripTagMenuNav.Count; i++)
            {
                DetailStripTagMenuNavRow nav = _detailStripTagMenuNav[i];
                if (nav == null || nav.RowImage == null) continue;
                if (i == index)
                    nav.RowImage.color = Color.Lerp(nav.BaseColor, DetailStripColorTag, 0.45f);
                else
                    nav.RowImage.color = nav.BaseColor;
            }
            _detailStripTagMenuFocusIdx = index;
            if (scrollIntoView)
            {
                DetailStripTagMenuNavRow focused = _detailStripTagMenuNav[index];
                if (focused != null)
                    DetailStripTagMenuScrollRowIntoView(focused);
            }
        }

        private void DetailStripTagMenuScrollRowIntoView(DetailStripTagMenuNavRow nav)
        {
            if (nav == null) return;
            ScrollRect sr = nav.IsAppliedColumn
                ? (_detailStripTagMenuAppliedScrollGO != null ? _detailStripTagMenuAppliedScrollGO.GetComponent<ScrollRect>() : null)
                : (_detailStripTagMenuAvailableScrollGO != null ? _detailStripTagMenuAvailableScrollGO.GetComponent<ScrollRect>() : null);
            if (sr == null || sr.content == null || sr.viewport == null) return;
            RectTransform rowRT = nav.GetComponent<RectTransform>();
            if (rowRT == null) return;
            // Lightweight: nudge normalized position toward row sibling index.
            Transform list = sr.content;
            int idx = rowRT.GetSiblingIndex();
            int n = Mathf.Max(1, list.childCount - 1);
            float t = 1f - (idx / (float)n);
            sr.verticalNormalizedPosition = Mathf.Clamp01(t);
        }

        private void DetailStripTagMenuMoveFocus(int delta)
        {
            if (_detailStripTagMenuNav.Count == 0) return;
            int next = _detailStripTagMenuFocusIdx < 0 ? 0 : _detailStripTagMenuFocusIdx + delta;
            if (next < 0) next = _detailStripTagMenuNav.Count - 1;
            if (next >= _detailStripTagMenuNav.Count) next = 0;
            DetailStripTagMenuSetFocus(next, scrollIntoView: true);
        }

        internal void DetailStripTagMenuActivateFocused()
        {
            if (_detailStripTagMenuFocusIdx < 0 || _detailStripTagMenuFocusIdx >= _detailStripTagMenuNav.Count)
                return;
            DetailStripTagMenuNavRow nav = _detailStripTagMenuNav[_detailStripTagMenuFocusIdx];
            if (nav != null && nav.Activate != null)
                nav.Activate();
        }

        internal bool DetailStripTagMenuHandleListKey(KeyCode key)
        {
            if (!DetailStripIsTagMenuOpen()) return false;
            if (_detailStripTagMenuMode == DetailStripTagMenuMode.Database) return false;
            if (key == KeyCode.UpArrow)
            {
                DetailStripTagMenuMoveFocus(-1);
                return true;
            }
            if (key == KeyCode.DownArrow)
            {
                DetailStripTagMenuMoveFocus(1);
                return true;
            }
            if (key == KeyCode.Space)
            {
                // Don't steal Space while typing a filter.
                string filter = (_detailStripTagMenuFilter ?? "").Trim();
                if (!string.IsNullOrEmpty(filter)
                    && _detailStripTagMenuSearch != null
                    && _detailStripTagMenuSearch.isFocused)
                    return false;
                DetailStripTagMenuActivateFocused();
                return true;
            }
            return false;
        }

        private void DetailStripSyncAppliedOrderFromListGO()
        {
            if (_detailStripTagMenuAppliedListGO == null) return;
            if (_detailStripTagMenuAppliedOrder == null)
                _detailStripTagMenuAppliedOrder = new List<string>(8);
            else
                _detailStripTagMenuAppliedOrder.Clear();

            Transform parent = _detailStripTagMenuAppliedListGO.transform;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform ch = parent.GetChild(i);
                if (ch == null || ch.name == "ReorderInsert") continue;
                string tag = null;
                DetailStripAppliedTagRow marker = ch.GetComponent<DetailStripAppliedTagRow>();
                if (marker != null && !string.IsNullOrEmpty(marker.TagName))
                    tag = marker.TagName;
                if (string.IsNullOrEmpty(tag))
                {
                    UserTagPickDragSource pick = ch.GetComponent<UserTagPickDragSource>();
                    if (pick != null) tag = pick.PrimaryTag;
                }
                if (string.IsNullOrEmpty(tag)) continue;
                _detailStripTagMenuAppliedOrder.Add(tag);
            }

            _detailStripTagsContentKey = "";
            try { DetailStripRefreshTagsLineForPlacement(); } catch { }
        }

        /// <summary>Prefer session reorder list, then A–Z for remainder.</summary>
        private List<string> DetailStripOrderTagsWithSession(HashSet<string> tags)
        {
            var result = new List<string>();
            if (tags == null || tags.Count == 0) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_detailStripTagMenuAppliedOrder != null)
            {
                for (int i = 0; i < _detailStripTagMenuAppliedOrder.Count; i++)
                {
                    string n = _detailStripTagMenuAppliedOrder[i];
                    if (string.IsNullOrEmpty(n) || !tags.Contains(n) || !seen.Add(n)) continue;
                    result.Add(n);
                }
            }

            var rest = new List<string>();
            foreach (string n in tags)
            {
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) continue;
                rest.Add(n);
            }
            rest.Sort(StringComparer.OrdinalIgnoreCase);
            result.AddRange(rest);
            return result;
        }

        // ── Applied-list reorder via existing tag pick-drag (insert line) ──────

        private GameObject _detailStripTagReorderInsertGO;
        private int _detailStripTagReorderInsertIndex = -1;
        private ScrollRect _detailStripTagReorderPausedScroll;

        /// <summary>
        /// While dragging an Applied tag: if pointer is over Applied list, show insert separator.
        /// Returns true when reorder UI is active (caller should skip gallery-apply hover).
        /// </summary>
        internal bool DetailStripUpdateAppliedReorderHint(string draggedTag, Vector2 screenPos)
        {
            if (string.IsNullOrEmpty(draggedTag)
                || _detailStripTagMenuRoot == null
                || !_detailStripTagMenuRoot.activeSelf
                || _detailStripTagMenuAppliedListGO == null)
            {
                DetailStripClearAppliedReorderHint();
                return false;
            }

            if (!DetailStripScreenPosOverAppliedList(screenPos))
            {
                DetailStripClearAppliedReorderHint();
                return false;
            }

            int insertAt = DetailStripComputeAppliedInsertIndex(draggedTag, screenPos);
            DetailStripShowAppliedReorderInsert(insertAt);
            DetailStripPauseAppliedListScroll(true);
            return true;
        }

        /// <summary>Drop on Applied list → reorder. True = handled (skip gallery apply).</summary>
        internal bool DetailStripTryCommitAppliedReorder(string draggedTag, Vector2 screenPos)
        {
            try
            {
                if (string.IsNullOrEmpty(draggedTag)
                    || _detailStripTagMenuAppliedListGO == null
                    || !DetailStripScreenPosOverAppliedList(screenPos))
                    return false;

                int insertAt = DetailStripComputeAppliedInsertIndex(draggedTag, screenPos);
                DetailStripClearAppliedReorderHint();

                Transform list = _detailStripTagMenuAppliedListGO.transform;
                var rows = new List<Transform>(list.childCount);
                var names = new List<string>(list.childCount);
                Transform dragged = null;
                for (int i = 0; i < list.childCount; i++)
                {
                    Transform ch = list.GetChild(i);
                    if (ch == null || ch.name == "ReorderInsert") continue;
                    string name = DetailStripReadAppliedRowTagName(ch);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(name, draggedTag, StringComparison.OrdinalIgnoreCase))
                    {
                        dragged = ch;
                        continue;
                    }
                    rows.Add(ch);
                    names.Add(name);
                }
                if (dragged == null) return true;

                insertAt = Mathf.Clamp(insertAt, 0, rows.Count);
                rows.Insert(insertAt, dragged);
                names.Insert(insertAt, draggedTag);

                for (int i = 0; i < rows.Count; i++)
                    rows[i].SetSiblingIndex(i);

                if (_detailStripTagMenuAppliedOrder == null)
                    _detailStripTagMenuAppliedOrder = new List<string>(names.Count);
                else
                    _detailStripTagMenuAppliedOrder.Clear();
                _detailStripTagMenuAppliedOrder.AddRange(names);

                _detailStripTagsContentKey = "";
                try { DetailStripRefreshTagsLineForPlacement(); } catch { }
                return true;
            }
            finally
            {
                DetailStripClearAppliedReorderHint();
            }
        }

        private static string DetailStripReadAppliedRowTagName(Transform ch)
        {
            if (ch == null) return null;
            DetailStripAppliedTagRow m = ch.GetComponent<DetailStripAppliedTagRow>();
            if (m != null && !string.IsNullOrEmpty(m.TagName)) return m.TagName;
            UserTagPickDragSource p = ch.GetComponent<UserTagPickDragSource>();
            return p != null ? p.PrimaryTag : null;
        }

        internal void DetailStripClearAppliedReorderHint()
        {
            _detailStripTagReorderInsertIndex = -1;
            if (_detailStripTagReorderInsertGO != null)
            {
                try { UnityEngine.Object.Destroy(_detailStripTagReorderInsertGO); } catch { }
                _detailStripTagReorderInsertGO = null;
            }
            DetailStripPauseAppliedListScroll(false);
        }

        private void DetailStripPauseAppliedListScroll(bool pause)
        {
            if (!pause)
            {
                if (_detailStripTagReorderPausedScroll != null)
                {
                    _detailStripTagReorderPausedScroll.enabled = true;
                    _detailStripTagReorderPausedScroll = null;
                }
                return;
            }
            if (_detailStripTagReorderPausedScroll != null) return;
            if (_detailStripTagMenuAppliedScrollGO == null) return;
            ScrollRect sr = _detailStripTagMenuAppliedScrollGO.GetComponent<ScrollRect>();
            if (sr == null || !sr.enabled) return;
            _detailStripTagReorderPausedScroll = sr;
            sr.enabled = false;
        }

        private bool DetailStripScreenPosOverAppliedList(Vector2 screenPos)
        {
            if (_detailStripTagMenuAppliedScrollGO == null) return false;
            RectTransform rt = _detailStripTagMenuAppliedScrollGO.GetComponent<RectTransform>();
            if (rt == null) rt = _detailStripTagMenuAppliedListGO != null
                ? _detailStripTagMenuAppliedListGO.GetComponent<RectTransform>()
                : null;
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
        }

        private int DetailStripComputeAppliedInsertIndex(string draggedTag, Vector2 screenPos)
        {
            Transform list = _detailStripTagMenuAppliedListGO.transform;
            int insertAt = 0;
            for (int i = 0; i < list.childCount; i++)
            {
                Transform ch = list.GetChild(i);
                if (ch == null || ch.name == "ReorderInsert") continue;

                string name = DetailStripReadAppliedRowTagName(ch);
                // Skip the row being dragged — insert relative to neighbors only.
                if (string.Equals(name, draggedTag, StringComparison.OrdinalIgnoreCase))
                    continue;

                RectTransform rt = ch as RectTransform;
                if (rt == null) continue;
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                // corners[0]=bl, [1]=tl — mid Y in screen space
                float midY = (corners[0].y + corners[1].y) * 0.5f;
                Vector2 screenMid = RectTransformUtility.WorldToScreenPoint(null, new Vector3(corners[0].x, midY, 0f));
                if (screenPos.y > screenMid.y)
                    return insertAt;
                insertAt++;
            }
            return insertAt;
        }

        private void DetailStripShowAppliedReorderInsert(int insertAt)
        {
            if (_detailStripTagMenuAppliedListGO == null) return;
            if (insertAt == _detailStripTagReorderInsertIndex && _detailStripTagReorderInsertGO != null)
                return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            float lineH = Mathf.Max(3f, 3f * s);

            if (_detailStripTagReorderInsertGO == null)
            {
                _detailStripTagReorderInsertGO = UI.CreateChildRT(
                    _detailStripTagMenuAppliedListGO, "ReorderInsert", AnchorPresets.hStretchTop);
                UI.AddImage(_detailStripTagReorderInsertGO, DetailStripColorTag, raycastTarget: false);
                UI.AddLE(_detailStripTagReorderInsertGO,
                    preferredHeight: lineH, minHeight: lineH, flexibleWidth: 1f, flexibleHeight: 0f);
            }

            _detailStripTagReorderInsertIndex = insertAt;
            Transform list = _detailStripTagMenuAppliedListGO.transform;
            int real = 0;
            int place = list.childCount - 1;
            for (int i = 0; i < list.childCount; i++)
            {
                Transform ch = list.GetChild(i);
                if (ch == null || ch == _detailStripTagReorderInsertGO.transform) continue;
                if (real == insertAt)
                {
                    place = i;
                    break;
                }
                real++;
                place = i + 1;
            }
            _detailStripTagReorderInsertGO.transform.SetSiblingIndex(Mathf.Clamp(place, 0, list.childCount - 1));
        }

        private static bool DetailStripTagMatchesFilter(string tag, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(tag)) return false;
            return tag.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DetailStripStripRichText(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <returns>Normalized tag name applied, or null on no-op.</returns>
        private string DetailStripCreateAndApplyTag(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return null;
            }
            string norm = raw.Trim();
            try
            {
                if (VpbLocalDatabase.TryEnsureGalleryUserTagInVocabulary(norm, out string n) && !string.IsNullOrEmpty(n))
                    norm = n;
            }
            catch { }
            ApplyUserTagsToFileEntries(new List<string> { norm }, selectedFiles, remove: false);
            return norm;
        }

        private void DetailStripShowNewTagModal()
        {
            DetailStripEnsureNewTagModal();
            if (_detailStripNewTagModalGO == null || _detailStripNewTagInput == null) return;
            _detailStripNewTagInput.text = "";
            _detailStripNewTagModalGO.SetActive(true);
            _detailStripNewTagModalGO.transform.SetAsLastSibling();
            try { _detailStripNewTagInput.ActivateInputField(); } catch { }
        }

        private void DetailStripEnsureNewTagModal()
        {
            if (_detailStripNewTagModalGO != null) return;
            if (backgroundBoxGO == null) return;

            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            UserTagEditorBuildNameDialog(
                backgroundBoxGO.transform,
                "VPB_DetailStripNewTagModal",
                "Panel",
                "TagInput",
                "Buttons",
                VPBTranslation.T("gallery.detail.tag_new_title", "New tag"),
                "Title",
                VPBTranslation.T("gallery.detail.tag_new_placeholder", "Tag name"),
                VPBTranslation.T("gallery.detail.cancel", "Cancel"),
                VPBTranslation.T("gallery.detail.add_tag", "Add"),
                GalleryUiDesignTokens.FontTitleRef,
                GalleryUiDesignTokens.FontBodyRef,
                GalleryUiDesignTokens.FontBodyRef,
                s,
                () =>
                {
                    if (_detailStripNewTagModalGO != null) _detailStripNewTagModalGO.SetActive(false);
                },
                DetailStripConfirmNewTag,
                out _detailStripNewTagModalGO,
                out _,
                out _detailStripNewTagInput);
        }

        private void DetailStripConfirmNewTag()
        {
            string raw = _detailStripNewTagInput != null ? (_detailStripNewTagInput.text ?? "").Trim() : "";
            if (_detailStripNewTagModalGO != null) _detailStripNewTagModalGO.SetActive(false);
            if (string.IsNullOrEmpty(raw))
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.no_tags", "No tags parsed."), 1.5f);
                return;
            }
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }

            string norm = raw;
            try
            {
                if (VpbLocalDatabase.TryEnsureGalleryUserTagInVocabulary(raw, out string n) && !string.IsNullOrEmpty(n))
                    norm = n;
            }
            catch { }

            ApplyUserTagsToFileEntries(new List<string> { norm }, selectedFiles, remove: false);
        }

        // ── Data helpers ──────────────────────────────────────────────────────

        private string DetailStripResolveTagsLine(FileEntry file, bool includeNative = true)
        {
            if (file == null) return "";
            var parts = new List<string>(3);
            string user = DetailStripFormatTagNames(DetailStripCollectUserTags(file));
            if (!string.IsNullOrEmpty(user)) parts.Add(user);
            if (includeNative)
            {
                string regions = DetailStripResolveRegionTags(file);
                if (!string.IsNullOrEmpty(regions)) parts.Add(regions);
            }
            if (parts.Count == 0) return "";
            return string.Join("  ·  ", parts.ToArray());
        }

        private HashSet<string> DetailStripCollectUserTags(FileEntry file)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (file == null) return names;
            try
            {
                if (!TryGetGalleryRowKeysForUserTags(file, out string pkgUid, out string internalPath))
                    return names;

                string cat = currentCategoryTitle ?? "";
                if (string.IsNullOrEmpty(cat) && titleText != null) cat = titleText.text ?? "";

                bool allVar = !string.IsNullOrEmpty(cat) && VpbLocalDatabase.IsGalleryAllVarPseudoCategory(cat);
                if (allVar
                    && string.Equals(internalPath, "meta.json", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(pkgUid))
                {
                    if (!VpbLocalDatabase.TryGetGalleryUserTagsForRow(cat, pkgUid, internalPath, names)
                        || names.Count == 0)
                        VpbLocalDatabase.TryGetGalleryUserTagsForPackageAnyPath(pkgUid, names);
                }
                else if (!string.IsNullOrEmpty(cat))
                    VpbLocalDatabase.TryGetGalleryUserTagsForRow(cat, pkgUid, internalPath, names);
                else
                    VpbLocalDatabase.TryGetGalleryUserTagsForPackageAnyPath(pkgUid, names);
            }
            catch { }
            return names;
        }

        private string DetailStripUserTagsFingerprint(FileEntry file)
        {
            HashSet<string> tags = DetailStripCollectUserTags(file);
            if (tags == null || tags.Count == 0) return "-";
            var list = new List<string>(tags);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", list.ToArray());
        }

        private static string DetailStripFormatTagNames(HashSet<string> names)
        {
            if (names == null || names.Count == 0) return "";
            var list = new List<string>(names.Count);
            foreach (string n in names)
            {
                if (!string.IsNullOrEmpty(n)) list.Add(n);
            }
            if (list.Count == 0) return "";
            list.Sort(StringComparer.OrdinalIgnoreCase);
            int show = Math.Min(DetailStripMaxTagsShown, list.Count);
            var sb = new StringBuilder(64);
            for (int i = 0; i < show; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i]);
            }
            if (list.Count > show) sb.Append(" +").Append(list.Count - show);
            return sb.ToString();
        }

        private static string DetailStripResolveRegionTags(FileEntry file)
        {
            HashSet<string> regions = DetailStripCollectNativeTags(file);
            if (regions == null || regions.Count == 0) return "";
            string joined = DetailStripFormatTagNames(regions);
            if (string.IsNullOrEmpty(joined)) return "";
            return VPBTranslation.T("gallery.detail.regions_prefix", "Regions: ") + joined;
        }

        private string DetailStripResolveGender(FileEntry file)
        {
            try
            {
                AppearanceGender g = GetAppearanceGender(file);
                if (g == AppearanceGender.Female) return VPBTranslation.T("gallery.detail.gender_female", "Female");
                if (g == AppearanceGender.Male) return VPBTranslation.T("gallery.detail.gender_male", "Male");
                if (g == AppearanceGender.Futa) return VPBTranslation.T("gallery.detail.gender_futa", "Futa");
            }
            catch { }
            return "";
        }

        private static string DetailStripResolvePathLine(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                if (file is VarFileEntry vfe)
                {
                    string ip = vfe.InternalPath;
                    if (!string.IsNullOrEmpty(ip))
                    {
                        string pkg = DetailStripResolvePackageUid(file);
                        if (!string.IsNullOrEmpty(pkg)) return pkg + ":/" + ip.Replace('\\', '/');
                        return ip.Replace('\\', '/');
                    }
                    if (!string.IsNullOrEmpty(vfe.Uid)) return vfe.Uid;
                }
                if (file is PackageListEntry ple)
                {
                    string uid = DetailStripResolvePackageUid(file);
                    if (!string.IsNullOrEmpty(uid)) return uid + ".var";
                    if (ple.Package != null && !string.IsNullOrEmpty(ple.Package.Path))
                        return ple.Package.Path.Replace('\\', '/');
                }
            }
            catch { }
            if (!string.IsNullOrEmpty(file.Path)) return file.Path.Replace('\\', '/');
            if (!string.IsNullOrEmpty(file.Uid)) return file.Uid;
            return file.Name ?? "";
        }

        private static string DetailStripResolvePackageUid(FileEntry file)
        {
            try
            {
                string uid = TryGetPackageUidForEntry(file);
                if (!string.IsNullOrEmpty(uid)) return uid;
            }
            catch { }
            return "";
        }

        private static string DetailStripResolveCreator(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg != null && !string.IsNullOrEmpty(pkg.Creator)) return pkg.Creator;
                string pkgUid = TryGetPackageUidForEntry(file);
                string creator, pkgName;
                int ver;
                if (!string.IsNullOrEmpty(pkgUid)
                    && TryParseVarUidParts(pkgUid, out creator, out pkgName, out ver)
                    && !string.IsNullOrEmpty(creator))
                    return creator;
            }
            catch { }
            return "";
        }

        private static string DetailStripResolveVersion(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg != null && pkg.Version > 0) return "v" + pkg.Version;
                string pkgUid = TryGetPackageUidForEntry(file);
                string creator, pkgName;
                int ver;
                if (!string.IsNullOrEmpty(pkgUid)
                    && TryParseVarUidParts(pkgUid, out creator, out pkgName, out ver)
                    && ver > 0)
                    return "v" + ver;
            }
            catch { }
            return "";
        }

        private static string DetailStripResolveLicense(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg == null) return "";
                try { pkg.TryEnsureMetaJsonLiteFields(); } catch { }
                if (!string.IsNullOrEmpty(pkg.LicenseType))
                    return pkg.LicenseType.Trim();
            }
            catch { }
            return "";
        }

        private static string DetailStripResolveFlags(FileEntry file)
        {
            if (file == null) return "";
            var flags = new List<string>(3);
            try { if (file.IsHidden()) flags.Add(VPBTranslation.T("gallery.detail.flag_hidden", "Hidden")); } catch { }
            try { if (file.IsAutoInstall()) flags.Add(VPBTranslation.T("gallery.detail.flag_autoinstall", "Autoinstall")); } catch { }
            try
            {
                if (!file.IsInstalled() && !string.IsNullOrEmpty(TryGetPackageUidForEntry(file)))
                    flags.Add(VPBTranslation.T("gallery.detail.flag_not_installed", "Not installed"));
            }
            catch { }
            return flags.Count > 0 ? string.Join(", ", flags.ToArray()) : "";
        }

        private string DetailStripResolveCategory(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                if (file is MissingPackageListEntry || file is VirtualFileEntry)
                    return VPBTranslation.T("gallery.detail.missing", "Missing");
                if (file is CleanupFileEntry cfe && cfe.Candidate != null)
                {
                    string fl = cfe.Candidate.GetFlagsLabel();
                    if (!string.IsNullOrEmpty(fl)) return fl;
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(currentCategoryTitle)) return currentCategoryTitle;

            try
            {
                VarPackage pkg = null;
                if (file is PackageListEntry ple) pkg = ple.Package;
                else if (file is VarFileEntry vfe) pkg = vfe.Package;
                if (pkg == null) pkg = TryResolvePackageForThumbPlaceholder(file);
                string label = GetBestCategoryLabelForPackage(pkg);
                if (!string.IsNullOrEmpty(label) && !string.Equals(label, "Unknown", StringComparison.OrdinalIgnoreCase))
                    return label;
            }
            catch { }

            try
            {
                string n = file.Name ?? file.Path ?? "";
                int dot = n.LastIndexOf('.');
                if (dot >= 0 && dot < n.Length - 1)
                    return n.Substring(dot + 1).ToUpperInvariant();
            }
            catch { }
            return "";
        }

        private static string DetailStripFormatDate(FileEntry file)
        {
            return DetailStripFormatDateTime(DetailStripResolveAddedDate(file));
        }

        private static string DetailStripFormatDateTime(DateTime dt)
        {
            try
            {
                if (dt.Year < 1980) return "";
                return dt.ToString("yy-MM-dd");
            }
            catch { return ""; }
        }

        private static DateTime DetailStripResolveAddedDate(FileEntry file)
        {
            if (file == null) return DateTime.MinValue;
            try { return GallerySortManager.ResolveDisplayDateForRow(file); }
            catch { return DateTime.MinValue; }
        }

        private static DateTime DetailStripResolveModifiedDate(FileEntry file)
        {
            if (file == null) return DateTime.MinValue;
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg != null) return pkg.LastWriteTime;
                return file.LastWriteTime;
            }
            catch { return DateTime.MinValue; }
        }

        private static DateTime DetailStripResolveCreatedDate(FileEntry file)
        {
            if (file == null) return DateTime.MinValue;
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg != null)
                {
                    try
                    {
                        long ib = pkg.InternalCreationTimeBinary;
                        if (ib != 0L && ib != long.MinValue)
                        {
                            DateTime fromZip = DateTime.FromBinary(ib);
                            if (fromZip.Year >= 1980) return fromZip;
                        }
                    }
                    catch { }
                    try
                    {
                        if (pkg.CreationTime.Year >= 1980) return pkg.CreationTime;
                    }
                    catch { }
                }
            }
            catch { }
            return DateTime.MinValue;
        }

        private void DetailStripAppendTimestampFields(List<DetailStripMetaField> fields, FileEntry file)
        {
            if (fields == null || file == null) return;

            string added = DetailStripFormatDateTime(DetailStripResolveAddedDate(file));
            string modified = DetailStripFormatDateTime(DetailStripResolveModifiedDate(file));
            string created = DetailStripFormatDateTime(DetailStripResolveCreatedDate(file));

            // Prefer distinct stamps; if only one unique day, keep single "Date".
            var unique = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(added)) unique.Add(added);
            if (!string.IsNullOrEmpty(modified)) unique.Add(modified);
            if (!string.IsNullOrEmpty(created)) unique.Add(created);

            if (unique.Count <= 1)
            {
                string one = !string.IsNullOrEmpty(added) ? added
                    : (!string.IsNullOrEmpty(modified) ? modified : created);
                if (string.IsNullOrEmpty(one)) return;
                string snap = one;
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_date", "Date"),
                    Value = one,
                    Group = 0,
                    Enabled = true,
                    ValueColor = DetailStripColorFact,
                    OnClick = () => DetailStripCopyMetaValue(snap, VPBTranslation.T("gallery.detail.copied_date", "Copied date")),
                    Tip = string.Format(VPBTranslation.T("gallery.detail.tip.date_fmt", "Copy date {0}"), one)
                });
                return;
            }

            if (!string.IsNullOrEmpty(added))
            {
                string snap = added;
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_added", "Added"),
                    Value = added,
                    Group = 0,
                    Enabled = true,
                    ValueColor = DetailStripColorFact,
                    OnClick = () => DetailStripCopyMetaValue(snap, VPBTranslation.T("gallery.detail.copied_added", "Copied added date")),
                    Tip = string.Format(VPBTranslation.T("gallery.detail.tip.added_fmt", "Gallery first seen {0}"), added)
                });
            }
            if (!string.IsNullOrEmpty(modified) && !string.Equals(modified, added, StringComparison.Ordinal))
            {
                string snap = modified;
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_modified", "Modified"),
                    Value = modified,
                    Group = 0,
                    Enabled = true,
                    ValueColor = DetailStripColorFact,
                    OnClick = () => DetailStripCopyMetaValue(snap, VPBTranslation.T("gallery.detail.copied_modified", "Copied modified date")),
                    Tip = string.Format(VPBTranslation.T("gallery.detail.tip.modified_fmt", "File modified {0}"), modified)
                });
            }
            if (!string.IsNullOrEmpty(created)
                && !string.Equals(created, added, StringComparison.Ordinal)
                && !string.Equals(created, modified, StringComparison.Ordinal))
            {
                string snap = created;
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_created", "Created"),
                    Value = created,
                    Group = 0,
                    Enabled = true,
                    ValueColor = DetailStripColorFact,
                    OnClick = () => DetailStripCopyMetaValue(snap, VPBTranslation.T("gallery.detail.copied_created", "Copied created date")),
                    Tip = string.Format(VPBTranslation.T("gallery.detail.tip.created_fmt", "Package created {0}"), created)
                });
            }
        }

        private static void DetailStripResolveVersionStatus(FileEntry file, out string status, out Color color)
        {
            status = "";
            color = DetailStripColorFact;
            if (file == null) return;
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg == null) return;
                VarPackageGroup group = pkg.Group;
                if (group == null || group.Packages == null || group.Packages.Count <= 1)
                {
                    status = VPBTranslation.T("gallery.detail.version_latest", "latest");
                    color = DetailStripColorVersionLatest;
                    return;
                }

                int newest = group.NewestVersion;
                if (pkg.Version >= newest || pkg.isNewestVersion)
                {
                    status = VPBTranslation.T("gallery.detail.version_latest", "latest");
                    color = DetailStripColorVersionLatest;
                }
                else
                {
                    status = string.Format(
                        VPBTranslation.T("gallery.detail.version_older", "older (v{0})"),
                        newest);
                    color = DetailStripColorVersionOlder;
                }
            }
            catch { }
        }

        private void DetailStripRefreshDescription(FileEntry file)
        {
            string desc = DetailStripResolveDescription(file);
            if (string.IsNullOrEmpty(desc))
            {
                if (_detailStripDesc != null) _detailStripDesc.text = "";
                _detailStripWantDesc = false;
                DetailStripApplyDescPlacement();
                return;
            }
            _detailStripWantDesc = true;
            // Placement + truncate/wrap decided by side vs tall-stack vs narrow.
            DetailStripApplyDescPlacement();
        }

        /// <param name="ensureMeta">
        /// True (default): may open .var ZIP once via <see cref="VarPackage.TryEnsureMetaJsonLiteFields"/>.
        /// False: hover tips — read cached Description only; never block EventSystem on disk I/O.
        /// </param>
        private static string DetailStripResolveDescription(FileEntry file, bool ensureMeta = true)
        {
            if (file == null) return "";
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg == null) return "";
                if (ensureMeta)
                {
                    try { pkg.TryEnsureMetaJsonLiteFields(); } catch { }
                }
                if (!string.IsNullOrEmpty(pkg.Description))
                    return pkg.Description.Trim();
            }
            catch { }
            return "";
        }

        private void DetailStripOnDescriptionClick()
        {
            string full = DetailStripResolveDescription(_detailStripBoundFile);
            if (string.IsNullOrEmpty(full)) return;
            DetailStripCopyMetaValue(full, VPBTranslation.T("gallery.detail.copied_desc", "Copied description"));
        }

        private List<string> DetailStripCollectOlderSiblingUids(List<FileEntry> files)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<FileEntry> src = files ?? selectedFiles;
            if (src == null || src.Count == 0) return result;

            for (int i = 0; i < src.Count; i++)
            {
                FileEntry f = src[i];
                if (f == null) continue;
                VarPackage pkg = null;
                try { pkg = TryResolvePackageForThumbPlaceholder(f); } catch { pkg = null; }
                if (pkg == null || pkg.Group == null || pkg.Group.Packages == null) continue;

                string groupKey = null;
                try { groupKey = pkg.Group.Name; } catch { }
                if (string.IsNullOrEmpty(groupKey))
                {
                    try { groupKey = pkg.Uid; } catch { groupKey = null; }
                }
                if (!string.IsNullOrEmpty(groupKey) && !seenGroups.Add(groupKey)) continue;

                int newest = 0;
                try { newest = pkg.Group.NewestVersion; } catch { newest = pkg.Version; }
                var packages = pkg.Group.Packages;
                for (int p = 0; p < packages.Count; p++)
                {
                    VarPackage other = packages[p];
                    if (other == null || other.Version >= newest) continue;
                    string uid = other.Uid;
                    if (string.IsNullOrEmpty(uid) || !seen.Add(uid)) continue;
                    result.Add(uid);
                }
            }
            return result;
        }

        /// <summary>Screen-space drag for detail-strip tag popup (Settings float pattern).</summary>
        private sealed class DetailStripTagMenuDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public RectTransform Target;
            public Action OnMoved;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null || eventData == null) return;
                Target.anchoredPosition += eventData.delta;
            }

            public void OnEndDrag(PointerEventData eventData)
            {
                if (OnMoved != null) OnMoved();
            }
        }

        /// <summary>Bottom-right resize for quick-tag popup (dep-whitelist pattern).</summary>
        private sealed class DetailStripTagMenuResize : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public RectTransform Target;
            public Func<Vector2> GetMinSize;
            public Func<Vector2> GetMaxSize;
            public Action OnResized;

            public void OnBeginDrag(PointerEventData eventData) { }

            public void OnDrag(PointerEventData eventData)
            {
                if (Target == null || eventData == null) return;
                Vector2 size = Target.sizeDelta;
                size.x += eventData.delta.x;
                size.y -= eventData.delta.y;
                Vector2 min = GetMinSize != null ? GetMinSize() : new Vector2(420f, 260f);
                Vector2 max = GetMaxSize != null ? GetMaxSize() : new Vector2(1400f, 1000f);
                size.x = Mathf.Clamp(size.x, min.x, max.x);
                size.y = Mathf.Clamp(size.y, min.y, max.y);
                Target.sizeDelta = size;
                if (OnResized != null) OnResized();
            }
        }

        /// <summary>Esc/Enter/arrows/Space while quick-tag search focused.</summary>
        private sealed class DetailStripTagMenuSearchKeys : MonoBehaviour
        {
            public GalleryPanel Panel;
            public InputField Field;

            private void OnGUI()
            {
                if (Panel == null || !Panel.DetailStripIsTagMenuOpen()) return;
                Event e = Event.current;
                if (e == null || e.type != EventType.KeyDown) return;

                // List nav works even when search not focused (panel open).
                bool searchFocused = Field != null && Field.isFocused;
                if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.Space)
                {
                    if (Panel.DetailStripTagMenuHandleListKey(e.keyCode))
                    {
                        e.Use();
                        return;
                    }
                }

                if (!searchFocused) return;

                if (e.keyCode == KeyCode.Escape)
                {
                    e.Use();
                    Panel.DetailStripTagMenuOnSearchEscape();
                    return;
                }
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    Panel.DetailStripTagMenuOnSearchSubmit();
                }
            }
        }

        /// <summary>Keyboard-focusable tag row in quick-tagger.</summary>
        private sealed class DetailStripTagMenuNavRow : MonoBehaviour
        {
            public string TagName;
            public bool IsAppliedColumn;
            public UnityAction Activate;
            public Image RowImage;
            public Color BaseColor;
        }

        /// <summary>Marks Applied-column shell rows so reorder sync can read tag names.</summary>
        private sealed class DetailStripAppliedTagRow : MonoBehaviour
        {
            public string TagName;
        }
    }
}
