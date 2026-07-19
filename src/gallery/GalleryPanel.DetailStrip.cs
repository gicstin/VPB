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
        /// <summary>Meta wrap rows. Was 3 — at high ChromeScale wider glyphs dropped Version/Gender/Flags/dates.</summary>
        private const int DetailStripMetaMaxRows = 6;
        private const float DetailStripTagMenuWidthRef = 300f;
        private const float DetailStripTagMenuHeightRef = 320f;
        private const float DetailStripTagMenuScrollHeightRef = 220f;
        private const int DetailStripTagMenuMaxRows = 48;
        private const float DetailStripTagMenuFilterDebounceSec = 0.12f;

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
        private static readonly Color DetailStripColorCopy = new Color(0.68f, 0.72f, 0.78f, 1f);        // muted
        private static readonly Color DetailStripColorDelete = new Color(0.95f, 0.45f, 0.45f, 1f);      // destructive
        private static readonly Color DetailStripColorHub = new Color(0.40f, 0.72f, 0.95f, 1f);         // hub blue
        private static readonly Color DetailStripColorCache = new Color(0.40f, 0.85f, 0.72f, 1f);       // mint / zstd
        private static readonly Color DetailStripColorAutoLoad = new Color(0.55f, 0.80f, 0.95f, 1f);    // auto-load
        private static readonly Color DetailStripColorHide = new Color(0.90f, 0.58f, 0.42f, 1f);        // warm
        private static readonly Color DetailStripColorTempWl = new Color(0.95f, 0.82f, 0.40f, 1f);      // yellow
        private static readonly Color DetailStripColorOldVers = new Color(0.92f, 0.68f, 0.40f, 1f);     // cleanup old
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
        /// <summary>Right mouse held on preview thumb — wheel adjusts rating instead of selection scrub.</summary>
        private bool _detailStripThumbRightHeld;
        private GameObject _detailStripMetaHost;
        private LayoutElement _detailStripMetaHostLE;
        private GameObject[] _detailStripMetaRows;
        private GameObject _detailStripActionsRowGO; // host (VLG) for wrapped action rows
        private GameObject[] _detailStripActionRows;
        private const int DetailStripActionMaxRows = 3;
        private GameObject _detailStripThumbColGO;
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
        private static readonly Color DetailStripLinkColor = DetailStripColorDeps;
        private static readonly Color DetailStripLinkDisabledColor = new Color(0.45f, 0.45f, 0.48f, 0.85f);
        private static readonly Color DetailStripMetaMutedColor = new Color(0.62f, 0.62f, 0.66f, 0.95f);
        private static readonly Color DetailStripStarOnColor = new Color(0.92f, 0.78f, 0.38f, 0.55f);
        private static readonly Color DetailStripStarOffColor = new Color(0.55f, 0.55f, 0.58f, 0.32f);
        private static readonly Color DetailStripStarPreviewColor = new Color(0.95f, 0.82f, 0.42f, 0.72f);
        private Text _detailStripTags;
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
        private GameObject _detailStripTagMenuScrollGO;
        private InputField _detailStripTagMenuSearch;
        private GameObject _detailStripTagMenuListGO;
        private GameObject _detailStripTagMenuCreateGO;
        private Text _detailStripTagMenuCreateText;
        private string _detailStripTagMenuFilter = "";
        private List<string> _detailStripTagMenuVocabCache;
        private HashSet<string> _detailStripTagMenuAppliedCache;
        private Coroutine _detailStripTagMenuFilterCo;
        private float _detailStripLayoutScale = -1f;
        private float _detailStripMetaAvailWidth = -1f;
        private float _detailStripMeasuredHeight = -1f;
        private bool _detailStripInRefreshGeometry;
        private bool _detailStripWantPath;
        private bool _detailStripWantDesc;
        private bool _detailStripWantNativeTags;
        private bool _detailStripSideVisible;
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
        /// Max strip height preserves design row capacity (300/18 ≈ 16.7 rows). Scaling only
        /// FooterDetailStripHeightRef×s while lineH grew (glyph pad) hid Path/Desc at ~1.6.
        /// </summary>
        private static float DetailStripMaxHeight(float s)
        {
            if (s <= 0f) s = 1f;
            float lineH = DetailStripLineHeight(s);
            float designLine = GalleryUiDesignTokens.FooterDetailStripLineHeightRef;
            if (designLine < 1f) designLine = 18f;
            float designRows = GalleryUiDesignTokens.FooterDetailStripHeightRef / designLine;
            return lineH * designRows + 12f * s;
        }

        private float DetailStripRowHeight(float s)
        {
            if (s <= 0f) s = 1f;
            float hardMin = DetailStripHardMinHeight(s);
            float maxH = DetailStripMaxHeight(s);
            if (_detailStripScrubHeightLocked && _detailStripScrubLockedHeight > 8f)
                return Mathf.Clamp(_detailStripScrubLockedHeight, hardMin, maxH);
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

        /// <summary>Active meta rows × current lineH — never trust a stale LayoutElement height.</summary>
        private float DetailStripMetaHostHeight(float s)
        {
            float lineH = DetailStripLineHeight(s);
            int metaN = DetailStripActiveMetaRowCount();
            if (metaN <= 0) return 0f;
            float gap = 2f * s;
            return lineH * metaN + gap * Mathf.Max(0, metaN - 1);
        }

        private void DetailStripSyncMetaHostHeight(float s)
        {
            if (_detailStripMetaHostLE == null || _detailStripMetaHost == null) return;
            float h = DetailStripMetaHostHeight(s);
            float lineH = DetailStripLineHeight(s);
            // Keep host active always — SetActive(false) + height check that required host
            // active was a deadlock (meta never came back after scale/empty sync).
            if (!_detailStripMetaHost.activeSelf)
                _detailStripMetaHost.SetActive(true);
            if (h < 0.5f)
            {
                _detailStripMetaHostLE.minHeight = 0f;
                _detailStripMetaHostLE.preferredHeight = 0f;
                _detailStripMetaHostLE.flexibleHeight = 0f;
                return;
            }
            _detailStripMetaHostLE.minHeight = lineH;
            _detailStripMetaHostLE.preferredHeight = h;
            _detailStripMetaHostLE.flexibleHeight = 0f;
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
                || _detailStripAutoLoadLink == null
                || _detailStripNoAutoLoadLink == null
                || _detailStripOldVersLink == null
                || _detailStripDesc == null
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
                || DetailStripStripForcesChildHeight(_detailStripGO)
                || DetailStripStripNeedsUpperLeftAlign(_detailStripGO)
                || DetailStripStripHasLegacyOuterPad(_detailStripGO)
                || (_detailStripActionRows != null && _detailStripActionRows.Length < DetailStripActionMaxRows)
                || (_detailStripMetaRows != null && _detailStripMetaRows.Length < DetailStripMetaMaxRows)
                || (_detailStripThumbColGO != null && _detailStripThumbColGO.GetComponent<UIScrollWheelHandler>() == null)
                || DetailStripActionsHostHasLegacyDirectChild("Link_Tag")
                || DetailStripActionsHostHasLegacyDirectChild("Chip_Deps")
                || DetailStripActionsHostHasLegacyDirectChild("Link_Deps"));
            if (needsRebuild && !DetailStripScrubBlocksRebuild)
            {
                try { UnityEngine.Object.Destroy(_detailStripGO); } catch { }
                _detailStripGO = null;
                DetailStripClearUiRefs();
                _detailStripLayoutScale = -1f;
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

            // Tag menu: rebuild if missing scroll / bottom-search layout.
            if (_detailStripTagMenuRoot != null && (
                _detailStripTagMenuScrollGO == null
                || _detailStripTagMenuSearch == null
                || _detailStripTagMenuListGO == null
                || _detailStripTagMenuCreateGO == null))
            {
                try { UnityEngine.Object.Destroy(_detailStripTagMenuRoot); } catch { }
                _detailStripTagMenuRoot = null;
                _detailStripTagMenuPanelGO = null;
                _detailStripTagMenuPanelRT = null;
                _detailStripTagMenuScrollGO = null;
                _detailStripTagMenuSearch = null;
                _detailStripTagMenuListGO = null;
                _detailStripTagMenuCreateGO = null;
                _detailStripTagMenuCreateText = null;
                DetailStripStopTagMenuFilterRebuild();
                DetailStripInvalidateTagMenuCaches();
            }

            if (_detailStripGO != null)
            {
                try { DetailStripSyncThumbInteractions(); } catch { }
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
            _detailStripBg = UI.AddImage(strip, new Color(0.07f, 0.07f, 0.09f, 0.98f), raycastTarget: true);

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
            UI.AddImage(_detailStripThumbGO, new Color(0.12f, 0.12f, 0.14f, 1f), raycastTarget: true);
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
            DetailStripSyncThumbInteractions();

            GameObject textCol = UI.CreateChildRT(strip, "TextCol", AnchorPresets.stretchAll);
            UI.AddLE(textCol, flexibleWidth: 1f, minWidth: 80f * s, flexibleHeight: 0f);
            // Clip path/title Overflow so long strings never paint over SideCol.
            if (textCol.GetComponent<RectMask2D>() == null)
                textCol.AddComponent<RectMask2D>();
            // Small left pad so collapse hover rim is not clipped by RectMask2D at thumb seam.
            // Main gap to thumb stays HLG spacing on strip.
            UI.AddVLG(textCol, spacing: 2f * s, padding: UI.Pad(2, 8, 6, 6, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);

            float lineH = DetailStripLineHeight(s);

            // Title left + subtle 5-star rating right.
            _detailStripTitleRowGO = UI.CreateChildRT(textCol, "TitleRow", AnchorPresets.stretchAll);
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
            _detailStripBadgeScan = DetailStripCreateBadge(_detailStripBadgeRowGO, "W", GalleryBadgeLetterScanExcluded, s,
                VPBTranslation.T("gallery.badge.tip.scan_excluded", "Scan whitelist: package is excluded from VaM's AddonPackages scan."));
            _detailStripBadgeTags = DetailStripCreateBadge(_detailStripBadgeRowGO, "T", GalleryBadgeLetterUserTags, s,
                VPBTranslation.T("gallery.detail.tip.badge_tags", "Has user tags. Click Tag or the tags line to manage."));

            DetailStripCreateStars(_detailStripTitleRowGO, s, lineH);

            // Balanced wrapping meta (Author mixed with facts; deps cluster kept together).
            _detailStripMetaHost = UI.CreateChildRT(textCol, "MetaHost", AnchorPresets.stretchAll);
            UI.AddVLG(_detailStripMetaHost, spacing: 2f * s, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);
            _detailStripMetaHostLE = UI.AddLE(_detailStripMetaHost, preferredHeight: lineH * 2f, minHeight: lineH, flexibleWidth: 1f);
            _detailStripMetaRows = new GameObject[DetailStripMetaMaxRows];
            for (int ri = 0; ri < DetailStripMetaMaxRows; ri++)
            {
                GameObject row = UI.CreateChildRT(_detailStripMetaHost, "MetaRow" + ri, AnchorPresets.stretchAll);
                UI.AddHLG(row, spacing: 8f * s, padding: UI.Pad(0, 0, 0, 0, s),
                    childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false, childForceExpandHeight: true);
                UI.AddLE(row, preferredHeight: lineH, minHeight: lineH, flexibleWidth: 1f);
                row.SetActive(false);
                _detailStripMetaRows[ri] = row;
            }

            // Actions wrap across 2 rows — avoids clipping when many links visible.
            _detailStripActionsRowGO = UI.CreateChildRT(textCol, "Actions", AnchorPresets.stretchAll);
            UI.AddVLG(_detailStripActionsRowGO, spacing: 2f * s, padding: UI.Pad(0, 0, 0, 0, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);
            UI.AddLE(_detailStripActionsRowGO,
                preferredHeight: lineH * DetailStripActionMaxRows + 2f * s * (DetailStripActionMaxRows - 1),
                minHeight: lineH, flexibleWidth: 1f);
            _detailStripToolsRowGO = null;
            _detailStripActionRows = new GameObject[DetailStripActionMaxRows];
            for (int ari = 0; ari < DetailStripActionMaxRows; ari++)
            {
                GameObject arow = UI.CreateChildRT(_detailStripActionsRowGO, "ActionRow" + ari, AnchorPresets.stretchAll);
                UI.AddHLG(arow, spacing: 8f * s, padding: UI.Pad(0, 0, 0, 0, s),
                    childAlignment: TextAnchor.MiddleLeft, childForceExpandWidth: false, childForceExpandHeight: true);
                UI.AddLE(arow, preferredHeight: lineH, minHeight: lineH, flexibleWidth: 1f);
                _detailStripActionRows[ari] = arow;
            }
            GameObject act0 = _detailStripActionRows[0];
            GameObject act1 = _detailStripActionRows[1];

            // Row 0: Copy · Delete · Hub · Cache · AutoLoad
            _detailStripCopyLink = DetailStripCreateActionLink(act0, "Copy", "Copy", s,
                DetailStripOnCopyClick, "gallery.detail.tip.copy", "Click: copy path(s) to clipboard (one per line)",
                DetailStripColorCopy);
            DetailStripAddLinkSep(act0, s);
            _detailStripDeleteLink = DetailStripCreateActionLink(act0, "Delete", "Delete", s,
                DetailStripOnDeleteClick, "gallery.detail.tip.delete", "Click: move selection to DeletedPackages / DeletedScenes",
                DetailStripColorDelete);
            DetailStripAddLinkSep(act0, s);
            _detailStripHubLink = DetailStripCreateActionLink(act0, "Hub", "Hub", s,
                DetailStripOnHubClick, "gallery.detail.tip.hub", "Click: open this item on Hub",
                DetailStripColorHub);
            DetailStripAddLinkSep(act0, s);
            _detailStripCacheLink = DetailStripCreateActionLink(act0, "Cache", "Cache", s,
                DetailStripOnCacheClick, "gallery.detail.tip.cache", "Click: build zstd texture cache for selection (Ctrl=rewrite, Ctrl+Shift=purge)",
                DetailStripColorCache);
            DetailStripAddLinkSep(act0, s);
            _detailStripAutoLoadLink = DetailStripCreateActionLink(act0, "AutoLoad", "AutoLoad", s,
                DetailStripOnAutoLoadClick, "gallery.detail.tip.autoload", "Click: enable auto-install / auto-load for selection",
                DetailStripColorAutoLoad);
            _detailStripAutoLoadSepGO = DetailStripAddLinkSepGO(act0, s);
            _detailStripNoAutoLoadLink = DetailStripCreateActionLink(act0, "NoAutoLoad", "No AutoLoad", s,
                DetailStripOnNoAutoLoadClick, "gallery.detail.tip.no_autoload", "Click: clear auto-install / auto-load for selection",
                DetailStripColorAutoLoad);

            // Row 1: Hide · Temp WL · Old vers
            _detailStripHideLink = DetailStripCreateActionLink(act1, "Hide", "Hide", s,
                DetailStripOnHideClick, "gallery.detail.tip.hide", "Click: hide selected packages in VaM lists",
                DetailStripColorHide);
            _detailStripHideUnhideSepGO = DetailStripAddLinkSepGO(act1, s);
            _detailStripUnhideLink = DetailStripCreateActionLink(act1, "Unhide", "Unhide", s,
                DetailStripOnUnhideClick, "gallery.detail.tip.unhide", "Click: unhide selected packages in VaM lists",
                DetailStripColorHide);
            _detailStripAfterHideSepGO = DetailStripAddLinkSepGO(act1, s);
            _detailStripTempWlLink = DetailStripCreateActionLink(act1, "TempWL", "Temp WL", s,
                DetailStripOnTempWlClick, "gallery.detail.tip.temp_wl", "Click: temporary scan whitelist for selection",
                DetailStripColorTempWl);
            _detailStripBeforeOldVersSepGO = DetailStripAddLinkSepGO(act1, s);
            _detailStripOldVersLink = DetailStripCreateActionLink(act1, "OldVers", "Old vers", s,
                DetailStripOnCleanupOldVersionsClick, "gallery.detail.tip.old_vers", "Click: move older package versions to DeletedPackages/OldVersions",
                DetailStripColorOldVers);

            _detailStripDesc = DetailStripCreateFlexLine(textCol, "Desc", DetailStripColorDesc, s, true, lineH);
            DetailStripBindClick(_detailStripDesc.gameObject, DetailStripOnDescriptionClick);
            AddDynamicTooltip(_detailStripDesc.gameObject, () =>
            {
                string full = DetailStripResolveDescription(_detailStripBoundFile);
                if (string.IsNullOrEmpty(full))
                    return VPBTranslation.T("gallery.detail.tip.desc_empty", "No short description in meta.json");
                return full + "\n" + VPBTranslation.T("gallery.detail.tip.desc_click", "Click: copy description");
            });

            _detailStripTags = DetailStripCreateFlexLine(textCol, "Tags", DetailStripColorTag, s, true, lineH);
            DetailStripBindClick(_detailStripTags.gameObject, DetailStripOnTagClick);
            AddDynamicTooltip(_detailStripTags.gameObject, () =>
            {
                string tags = _detailStripTags != null ? (_detailStripTags.text ?? "") : "";
                string tip = VPBTranslation.T("gallery.detail.tip.tags_line", "Click: quick-tag menu (existing tags or new tag)");
                if (string.IsNullOrEmpty(tags)) return tip;
                return tags + "\n" + tip;
            });

            _detailStripPath = DetailStripCreateFlexLine(textCol, "Path", new Color(0.62f, 0.62f, 0.65f, 0.92f), s, true, lineH);
            DetailStripBindClick(_detailStripPath.gameObject, DetailStripOnCopyClick);
            AddDynamicTooltip(_detailStripPath.gameObject, () =>
            {
                string path = _detailStripPath != null ? (_detailStripPath.text ?? "") : "";
                string tip = VPBTranslation.T("gallery.detail.tip.path", "Click: copy path to clipboard");
                if (string.IsNullOrEmpty(path)) return tip;
                return path + "\n" + tip;
            });

            // Wide-pane right column — scrollable description + native tags (collapses when narrow).
            float sideW = GalleryUiDesignTokens.FooterDetailStripSideMinColWidthRef * s;
            GameObject sideCol = UI.CreateChildRT(strip, "SideCol", AnchorPresets.stretchAll);
            _detailStripSideColGO = sideCol;
            _detailStripSideColLE = UI.AddLE(sideCol,
                minWidth: sideW, preferredWidth: sideW,
                minHeight: 0f, preferredHeight: 0f,
                flexibleWidth: 0f, flexibleHeight: 0f);
            UI.AddVLG(sideCol, spacing: 2f * s, padding: UI.Pad(10, 8, 6, 6, s),
                childAlignment: TextAnchor.UpperLeft,
                childControlWidth: true, childControlHeight: true,
                childForceExpandWidth: true, childForceExpandHeight: false);

            DetailStripCreateSideDescScroll(sideCol, s, lineH * 3f);

            _detailStripSideNativeTags = DetailStripCreateSideBlock(
                sideCol, "SideNativeTags", DetailStripColorTag, s, lineH, wrap: true);
            AddDynamicTooltip(_detailStripSideNativeTags.gameObject, () =>
                VPBTranslation.T(
                    "gallery.detail.tip.native_tags",
                    "Native package tags from meta.json (clothing / hair regions)."));

            sideCol.SetActive(false);
            _detailStripSideVisible = false;

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
                string full = DetailStripResolveDescription(_detailStripBoundFile);
                if (string.IsNullOrEmpty(full))
                    return VPBTranslation.T("gallery.detail.tip.desc_empty", "No short description in meta.json");
                return full + "\n" + VPBTranslation.T("gallery.detail.tip.desc_click", "Click: copy description");
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
            _detailStripThumbRightHeld = false;
            _detailStripMetaHost = null;
            _detailStripMetaHostLE = null;
            _detailStripMetaRows = null;
            _detailStripActionsRowGO = null;
            _detailStripActionRows = null;
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
            _detailStripTags = null;
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
            _detailStripWantNativeTags = false;
            _detailStripSideVisible = false;
            _detailStripSideContentKey = "";
        }

        private static Text DetailStripCreateFlexLine(GameObject parent, string name, Color color, float s, bool clickable, float height)
        {
            // Wrapper row + preferredWidth 0 keeps left edge aligned with Title/Meta
            // (bare Text preferred-width was shifting Path/Tags left toward the thumb).
            GameObject row = UI.CreateChildRT(parent, name + "Row", AnchorPresets.stretchAll);
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
                // Persist across restart (same pattern as toolbox pin).
                try { VPBConfig.Instance.Save(false); } catch { }
            }

            if (!expanded)
            {
                try { DetailStripCloseTagMenu(); } catch { }
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
            UnityAction onClick, string tipKey, string tipDefault, Color linkColor)
        {
            Text t = UI.CreateLabel(
                parent, label, GalleryUiDesignTokens.FontRef, linkColor,
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: true, richText: true, name: "Link_" + name);
            DetailStripApplyFont(t, s);
            ContentSizeFitter csf = t.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Vertical PreferredSize fights row LE at high scale (~1.6) → Truncate clip.
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            float lineH = DetailStripLineHeight(s);
            UI.AddLE(t.gameObject, minHeight: lineH, preferredHeight: lineH, flexibleWidth: 0f, flexibleHeight: 0f);

            AddTooltip(t.gameObject, tipKey, tipDefault);
            DetailStripBindClick(t.gameObject, onClick);

            UIHoverDelegate hover = t.gameObject.GetComponent<UIHoverDelegate>();
            if (hover == null) hover = t.gameObject.AddComponent<UIHoverDelegate>();
            Color baseCol = linkColor;
            Color hoverCol = DetailStripBrighten(baseCol, 0.18f);
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
                t.color = h ? hoverCol : baseCol;
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

        private void DetailStripAddLinkSep(GameObject parent, float s)
        {
            DetailStripAddLinkSepGO(parent, s);
        }

        private GameObject DetailStripAddLinkSepGO(GameObject parent, float s)
        {
            Text sep = UI.CreateLabel(
                parent, "·", GalleryUiDesignTokens.FontRef, new Color(0.50f, 0.50f, 0.54f, 0.90f),
                TextAnchor.MiddleLeft, HorizontalWrapMode.Overflow, VerticalWrapMode.Truncate,
                raycastTarget: false, name: "Sep");
            DetailStripApplyFont(sep, s);
            ContentSizeFitter csf = sep.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            float lineH = DetailStripLineHeight(s);
            UI.AddLE(sep.gameObject, minHeight: lineH, preferredHeight: lineH, flexibleWidth: 0f, flexibleHeight: 0f);
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

        /// <summary>Tooltip + double-click launch on preview thumb (safe to call on existing strip).</summary>
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
                "Scroll: prev/next · Hold right + scroll: rate · Double-click: launch / apply");

            // EventTrigger implements IScrollHandler and swallows wheel if placed on the hit
            // target above UIScrollWheelHandler — never put EventTrigger on Thumb/Image children.
            DetailStripStripEventTrigger(_detailStripThumbGO);
            if (_detailStripThumb != null)
                DetailStripStripEventTrigger(_detailStripThumb.gameObject);

            DetailStripEnsureThumbScroll(thumbCol);
            DetailStripEnsureThumbScroll(_detailStripThumbGO);
            if (_detailStripThumb != null)
                DetailStripEnsureThumbScroll(_detailStripThumb.gameObject);

            DetailStripBindThumbInput(thumbCol);
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

        /// <summary>Double-click + right-hold tracking on thumb column (no EventTrigger / IScrollHandler).</summary>
        private void DetailStripBindThumbInput(GameObject go)
        {
            if (go == null) return;
            DetailStripThumbClickRelay relay = go.GetComponent<DetailStripThumbClickRelay>();
            if (relay == null) relay = go.AddComponent<DetailStripThumbClickRelay>();
            relay.OnDoubleClick = DetailStripOnThumbDoubleClick;
            relay.OnRightHoldChanged = held => _detailStripThumbRightHeld = held;
            _detailStripThumbRightHeld = relay.RightHeld;
            DetailStripStripEventTrigger(go);
        }

        private void DetailStripOnThumbDoubleClick()
        {
            if (IsSettingsPanelOpen()) return;
            if (_benchPickModeActive) return;
            FileEntry file = _detailStripBoundFile;
            if (file == null && selectedFiles != null && selectedFiles.Count > 0)
                file = selectedFiles[0];
            if (file == null) return;
            ApplyFileEntryNow(file);
        }

        private bool DetailStripThumbRateScrollActive()
        {
            if (!_detailStripThumbRightHeld) return false;
            // RMB released outside thumb without PointerUp on relay.
            if (!Input.GetMouseButton(1))
            {
                _detailStripThumbRightHeld = false;
                return false;
            }
            return true;
        }

        /// <summary>Scroll up → +1 star, down → −1 (0–5). Applies to whole selection.</summary>
        private void DetailStripNudgeRatingFromScroll(float scrollDelta)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            int step = scrollDelta > 0f ? 1 : -1;
            int next = Mathf.Clamp(_detailStripStarRating + step, 0, 5);
            if (next == _detailStripStarRating) return;
            DetailStripApplyStarRating(next);
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
            _detailStripCacheKey = BuildDetailStripCacheKey();
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
                _detailStripMeasuredHeight = -1f;

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
                DetailStripSetLayoutGroup(textColTr.gameObject, 2f * s, UI.Pad(2, 8, 6, 6, s));
                LayoutElement textColLe = textColTr.GetComponent<LayoutElement>();
                if (textColLe != null)
                {
                    textColLe.minWidth = 80f * s;
                    textColLe.flexibleHeight = 0f;
                }
            }

            if (_detailStripSideColGO != null)
            {
                DetailStripSetLayoutGroup(_detailStripSideColGO, 2f * s, UI.Pad(10, 8, 6, 6, s));
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

            DetailStripSetLayoutGroup(_detailStripMetaHost, 2f * s, zeroPad);
            if (_detailStripMetaRows != null)
            {
                for (int ri = 0; ri < _detailStripMetaRows.Length; ri++)
                {
                    GameObject row = _detailStripMetaRows[ri];
                    if (row == null) continue;
                    DetailStripSetRowHeight(row, lineH);
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
                        le.minHeight = lineH;
                        le.preferredHeight = lineH;
                        le.flexibleHeight = 0f;
                    }
                }
            }
            DetailStripSyncMetaHostHeight(s);

            DetailStripSetLayoutGroup(_detailStripActionsRowGO, 2f * s, zeroPad);
            LayoutElement actionsLe = _detailStripActionsRowGO != null
                ? _detailStripActionsRowGO.GetComponent<LayoutElement>() : null;
            if (actionsLe != null) actionsLe.flexibleHeight = 0f;
            if (_detailStripActionRows != null)
            {
                for (int ari = 0; ari < _detailStripActionRows.Length; ari++)
                {
                    GameObject arow = _detailStripActionRows[ari];
                    if (arow == null) continue;
                    DetailStripSetRowHeight(arow, lineH);
                    DetailStripSetLayoutGroup(arow, 8f * s, zeroPad);
                }
            }

            DetailStripSyncLinkChrome(_detailStripCopyLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripDeleteLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripHubLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripCacheLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripAutoLoadLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripNoAutoLoadLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripHideLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripUnhideLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripTempWlLink, s, lineH);
            DetailStripSyncLinkChrome(_detailStripOldVersLink, s, lineH);
            DetailStripApplyFont(_detailStripDesc, s);
            DetailStripApplyFont(_detailStripTags, s);
            DetailStripApplyFont(_detailStripPath, s);

            DetailStripSyncFlexLineChrome(_detailStripDesc, lineH, s);
            DetailStripSyncFlexLineChrome(_detailStripTags, lineH, s);
            DetailStripSyncFlexLineChrome(_detailStripPath, lineH, s);

            // After pad/spacing/font rescale — clear stale horizontal insets on every band.
            DetailStripNormalizeTextColRows();
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
                _detailStripMeasuredHeight = -1f;
            DetailStripLayout();
        }

        private static float DetailStripThumbEdge(float s, float stripH)
        {
            if (s <= 0f) s = 1f;
            // Full strip height — preview flush, no pad around image.
            float minEdge = 44f * s;
            float maxEdge = GalleryUiDesignTokens.FooterDetailStripThumbMaxRef * s;
            float edge = stripH > 1f ? stripH : minEdge;
            return Mathf.Clamp(edge, minEdge, maxEdge);
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
            float thumbSize = DetailStripThumbEdge(s, stripH);
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
                try { DetailStripSyncSideColumn(s); } catch { }
                try { DetailStripPackActionRows(s); } catch { }
                try { DetailStripSyncMetaHostHeight(s); } catch { }
                // Restore optional lines, then hide only if still over budget (never squash row heights).
                try
                {
                    DetailStripSetFlexLineActive(_detailStripPath, _detailStripWantPath);
                    DetailStripApplyDescPlacement();
                    DetailStripHideOverflowLines(s, DetailStripGeometryBudgetHeight(s));
                }
                catch { }
                float h = DetailStripComputeContentHeight(s);
                // Scrub session: keep outer strip height stable (no tbox jump / layout thrash).
                if (_detailStripScrubHeightLocked && _detailStripScrubLockedHeight > 8f)
                    h = _detailStripScrubLockedHeight;
                _detailStripMeasuredHeight = h;
                DetailStripLayout();
                // Side desc viewport was sized with pre-measure height — refill to final strip edge.
                try { DetailStripSyncSideColumn(s); } catch { }
                // Pack/side sync can reintroduce stretchAll horizontal drift — re-align once.
                try { DetailStripNormalizeTextColRows(); } catch { }
                // Force side-rail + subcategory list re-fit (tall strip eats bottom chrome).
                if (!_detailStripScrubHeightLocked)
                    _sideRailLastBottomInset = -1f;
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
            return DetailStripMaxHeight(s);
        }

        /// <summary>
        /// If content exceeds budget height, hide Path then Desc (keep tags/actions).
        /// Never shrink row heights — avoids overlapping glyphs.
        /// </summary>
        private void DetailStripHideOverflowLines(float s, float maxH)
        {
            if (maxH < 8f) maxH = DetailStripMaxHeight(s);
            float h = DetailStripComputeContentHeight(s);
            if (h <= maxH + 0.5f) return;

            if (DetailStripFlexLineVisible(_detailStripPath))
            {
                DetailStripSetFlexLineActive(_detailStripPath, false);
                h = DetailStripComputeContentHeight(s);
                if (h <= maxH + 0.5f) return;
            }
            if (DetailStripFlexLineVisible(_detailStripDesc))
            {
                DetailStripSetFlexLineActive(_detailStripDesc, false);
                h = DetailStripComputeContentHeight(s);
                if (h <= maxH + 0.5f) return;
            }
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
            _detailStripScrubHeightLocked = false;
            _detailStripScrubLockedHeight = -1f;
        }

        private float DetailStripComputeContentHeight(float s)
        {
            float lineH = DetailStripLineHeight(s);
            float gap = 2f * s;
            float vPad = 12f * s;
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
                total += lineH * actN + gap * Mathf.Max(0, actN - 1);
                parts++;
            }

            if (DetailStripFlexLineVisible(_detailStripDesc))
            {
                if (parts > 0) total += gap;
                total += lineH;
                parts++;
            }
            if (DetailStripFlexLineVisible(_detailStripTags))
            {
                if (parts > 0) total += gap;
                total += lineH;
                parts++;
            }
            if (DetailStripFlexLineVisible(_detailStripPath))
            {
                if (parts > 0) total += gap;
                total += lineH;
                parts++;
            }

            // Side column scrolls — never grow strip past left-column content.

            float hardMin = DetailStripHardMinHeight(s);
            float maxH = DetailStripMaxHeight(s);
            return Mathf.Clamp(Mathf.Max(total, hardMin), hardMin, maxH);
        }

        private static void DetailStripNormalizeRowRect(GameObject row)
        {
            if (row == null) return;
            RectTransform rt = row.GetComponent<RectTransform>();
            if (rt == null) return;
            // stretchAll children in a VLG can pick up a negative/stale left offset on wrap
            // rows or after UI-scale changes — title vs path then misalign at low scale.
            rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
            rt.pivot = new Vector2(0.5f, rt.pivot.y);
            rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
            rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
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
            if (_detailStripDesc != null && _detailStripDesc.transform.parent != null)
                DetailStripNormalizeRowRect(_detailStripDesc.transform.parent.gameObject);
            if (_detailStripTags != null && _detailStripTags.transform.parent != null)
                DetailStripNormalizeRowRect(_detailStripTags.transform.parent.gameObject);
            if (_detailStripPath != null && _detailStripPath.transform.parent != null)
                DetailStripNormalizeRowRect(_detailStripPath.transform.parent.gameObject);
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
                _detailStripCopyLink, _detailStripDeleteLink, _detailStripHubLink, _detailStripCacheLink,
                _detailStripAutoLoadLink, _detailStripNoAutoLoadLink,
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

            int actN = 0;
            for (int r = 0; r < _detailStripActionRows.Length; r++)
            {
                GameObject row = _detailStripActionRows[r];
                if (row == null) continue;
                DetailStripRebuildActionSeps(row, s);
                bool on = DetailStripRowHasVisibleChild(row);
                row.SetActive(on);
                if (on) actN++;
            }
            if (actN < 1) actN = 1;

            float lineH = DetailStripLineHeight(s);
            LayoutElement hostLe = _detailStripActionsRowGO != null
                ? _detailStripActionsRowGO.GetComponent<LayoutElement>() : null;
            if (hostLe != null)
            {
                float gap = 2f * s;
                hostLe.preferredHeight = lineH * actN + gap * Mathf.Max(0, actN - 1);
                hostLe.minHeight = lineH;
            }

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
                GameObject sep = DetailStripAddLinkSepGO(row, s);
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
            DetailStripEnsure();
            DetailStripEnsureExpandButton();

            // Entire scrub session (active wheel OR height-locked): never hide/populate/reflow.
            // Side meta is filled on scrub commit (see DetailStripCommitScrub).
            if (DetailStripScrubBlocksRebuild)
                return;

            int sel = selectedFiles != null ? selectedFiles.Count : 0;
            if (sel <= 0)
            {
                DetailStripCloseTagMenu();
                DetailStripHide();
                return;
            }

            // Collapsed: strip height 0; Info expand button stays in toolbox gutter.
            if (!DetailStripIsExpanded())
            {
                try { DetailStripCloseTagMenu(); } catch { }
                if (_detailStripGO != null) _detailStripGO.SetActive(false);
                DetailStripSyncCollapseExpandChrome();
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
                        if (_detailStripMetaAvailWidth < 0f || Mathf.Abs(avail - _detailStripMetaAvailWidth) > 8f)
                            DetailStripReflowMetaForCurrentSelection();
                        else if (Mathf.Abs(avail - _detailStripMetaAvailWidth) > 2f)
                            DetailStripRefreshGeometry();
                    }
                }
                catch { }
                return;
            }
            _detailStripCacheKey = key;

            DetailStripShowChrome();
            if (sel == 1)
                DetailStripPopulateSingle(selectedFiles[0], reloadThumb: true);
            else
                DetailStripPopulateMulti(sel, reloadThumb: true);
            DetailStripSyncCollapseExpandChrome();
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
                try
                {
                    int r = RatingsManager.Instance != null ? RatingsManager.Instance.GetRating(f) : 0;
                    sb.Append('|').Append(r);
                }
                catch { }
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

        private static void DetailStripSetLink(Text link, string label, bool enabled, Color activeColor)
        {
            if (link == null) return;
            link.text = label ?? "";
            link.raycastTarget = enabled;
            bool rich = label != null && label.IndexOf("<color=", StringComparison.OrdinalIgnoreCase) >= 0;
            // Preserve hover tint while pointer still over the link.
            UIHoverDelegate del = link.GetComponent<UIHoverDelegate>();
            bool hovered = del != null && del.IsHovered;
            if (!enabled)
                link.color = DetailStripLinkDisabledColor;
            else if (rich)
                link.color = Color.white; // rich text supplies its own colors
            else
                link.color = hovered ? DetailStripBrighten(activeColor, 0.18f) : activeColor;
        }

        private void DetailStripSetToolLinksEnabled(bool enabled)
        {
            DetailStripSetLink(_detailStripCopyLink, VPBTranslation.T("gallery.detail.chip_copy", "Copy"), enabled, DetailStripColorCopy);

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
            DetailStripSetLink(_detailStripDeleteLink, VPBTranslation.T("gallery.detail.chip_delete", "Delete"), canDelete, DetailStripColorDelete);
            if (_detailStripDeleteLink != null)
                _detailStripDeleteLink.gameObject.SetActive(enabled);

            DetailStripSetLink(_detailStripHubLink, VPBTranslation.T("gallery.detail.chip_hub", "Hub"), enabled, DetailStripColorHub);
            DetailStripSetLink(
                _detailStripCacheLink,
                VPBTranslation.T("gallery.detail.chip_cache", "Cache"),
                enabled,
                DetailStripColorCache);
            DetailStripRefreshAutoLoadLinks(enabled);
            DetailStripRefreshHideLinks(enabled);
            DetailStripSetLink(_detailStripTempWlLink, VPBTranslation.T("gallery.detail.chip_temp_wl", "Temp WL"), enabled, DetailStripColorTempWl);
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
                ? VPBTranslation.T("gallery.detail.chip_no_autoload_all", "Clear AL All")
                : VPBTranslation.T("gallery.detail.chip_no_autoload", "No AutoLoad");

            DetailStripSetLink(_detailStripAutoLoadLink, enableLabel, showEnable, DetailStripColorAutoLoad);
            DetailStripSetLink(_detailStripNoAutoLoadLink, disableLabel, showDisable, DetailStripColorAutoLoad);

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
                ? string.Format(VPBTranslation.T("gallery.detail.chip_old_vers_n", "Old vers ({0})"), olderN)
                : VPBTranslation.T("gallery.detail.chip_old_vers", "Old vers");
            DetailStripSetLink(_detailStripOldVersLink, label, show, DetailStripColorOldVers);
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

            DetailStripSetLink(_detailStripHideLink, hideLabel, showHide, DetailStripColorHide);
            DetailStripSetLink(_detailStripUnhideLink, unhideLabel, showUnhide, DetailStripColorHide);

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
            string itemName = file != null ? GetGalleryListRowDisplayName(file) : "";
            if (string.IsNullOrEmpty(itemName)) itemName = VPBTranslation.T("gallery.detail.this_item", "this item");

            string author = creatorMixed
                ? VPBTranslation.T("gallery.detail.mixed_short", "Mixed")
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
                    : VPBTranslation.T("gallery.detail.tip.author_local", "Local / non-package item (no author filter)"),
                MaxValueWidth = authorCap
            });

            if (selectedCount > 1)
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

            string category = DetailStripResolveCategory(file);
            if (!string.IsNullOrEmpty(category))
            {
                string catSnap = category;
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_category", "Category"),
                    Value = category,
                    Group = 0,
                    Enabled = true,
                    ValueColor = DetailStripColorCategory,
                    OnClick = () => DetailStripCopyMetaValue(catSnap, VPBTranslation.T("gallery.detail.copied_category", "Copied category")),
                    Tip = string.Format(VPBTranslation.T("gallery.detail.tip.category_fmt", "Copy category \"{0}\""), category)
                });
            }

            long size = totalSizeOverride > 0 ? totalSizeOverride : (file != null ? file.Size : 0);
            if (size > 0)
            {
                string sizeTxt = FormatBytesForList(size);
                fields.Add(new DetailStripMetaField
                {
                    Label = VPBTranslation.T("gallery.detail.label_size", "Size"),
                    Value = sizeTxt,
                    Group = 0,
                    Enabled = true,
                    ValueColor = DetailStripColorFact,
                    OnClick = () => DetailStripCopyMetaValue(sizeTxt, VPBTranslation.T("gallery.detail.copied_size", "Copied size")),
                    Tip = string.Format(VPBTranslation.T("gallery.detail.tip.size_fmt", "Copy size {0}"), sizeTxt)
                });
            }

            DetailStripAppendTimestampFields(fields, file);

            // Version / gender / flags from bound file (first item when multi) — always offer when present.
            if (file != null)
            {
                string version = DetailStripResolveVersion(file);
                if (!string.IsNullOrEmpty(version))
                {
                    string status;
                    Color verColor;
                    DetailStripResolveVersionStatus(file, out status, out verColor);
                    string verValue = !string.IsNullOrEmpty(status) ? (version + " " + status) : version;
                    string verSnap = verValue;
                    string verTip = selectedCount > 1
                        ? VPBTranslation.T("gallery.detail.tip.version_first", "First selected item version")
                        : string.Format(VPBTranslation.T("gallery.detail.tip.version_fmt", "Copy version {0}"), verValue);
                    fields.Add(new DetailStripMetaField
                    {
                        Label = VPBTranslation.T("gallery.detail.label_version", "Version"),
                        Value = verValue,
                        Group = 0,
                        Enabled = true,
                        ValueColor = verColor,
                        OnClick = () => DetailStripCopyMetaValue(verSnap, VPBTranslation.T("gallery.detail.copied_version", "Copied version")),
                        Tip = verTip
                    });
                }

                string gender = DetailStripResolveGender(file);
                if (!string.IsNullOrEmpty(gender))
                {
                    string gSnap = gender;
                    fields.Add(new DetailStripMetaField
                    {
                        Label = VPBTranslation.T("gallery.detail.label_gender", "Gender"),
                        Value = gender,
                        Group = 0,
                        Enabled = true,
                        ValueColor = DetailStripColorFact,
                        OnClick = () => DetailStripCopyMetaValue(gSnap, VPBTranslation.T("gallery.detail.copied_gender", "Copied gender")),
                        Tip = string.Format(VPBTranslation.T("gallery.detail.tip.gender_fmt", "Copy gender {0}"), gender)
                    });
                }

                string flags = DetailStripResolveFlags(file);
                if (!string.IsNullOrEmpty(flags))
                {
                    fields.Add(new DetailStripMetaField
                    {
                        Label = VPBTranslation.T("gallery.detail.label_flags", "Flags"),
                        Value = flags,
                        Group = 0,
                        Enabled = false,
                        ValueColor = DetailStripColorFlags,
                        Tip = flags
                    });
                }
            }

            // Deps cluster — kept together on one balanced row.
            fields.Add(new DetailStripMetaField
            {
                Label = VPBTranslation.T("gallery.detail.label_deps", "Dependencies"),
                Value = deps.ToString(),
                Group = 1,
                Enabled = deps > 0,
                ValueColor = DetailStripColorDeps,
                OnClick = DetailStripOnDepsClick,
                Tip = deps > 0
                    ? string.Format(VPBTranslation.T("gallery.detail.tip.deps_fmt", "Filter the gallery to dependencies of {0}"), itemName)
                    : VPBTranslation.T("gallery.detail.tip.deps_none", "No dependencies")
            });

            fields.Add(new DetailStripMetaField
            {
                Label = VPBTranslation.T("gallery.detail.label_missing", "Missing"),
                Value = missing.ToString(),
                Group = 1,
                Enabled = true,
                ValueColor = missing > 0 ? DetailStripColorMissingBad : DetailStripColorMissingOk,
                OnClick = DetailStripOnMissingClick,
                Tip = missing > 0
                    ? string.Format(VPBTranslation.T("gallery.detail.tip.missing_fmt", "Filter the gallery to missing dependencies of {0}"), itemName)
                    : VPBTranslation.T("gallery.detail.tip.missing_none", "No missing dependencies — click to confirm")
            });

            fields.Add(new DetailStripMetaField
            {
                Label = VPBTranslation.T("gallery.detail.label_dependents", "Dependents"),
                Value = dependents.ToString(),
                Group = 1,
                Enabled = dependents > 0,
                ValueColor = DetailStripColorDependents,
                OnClick = DetailStripOnDependentsClick,
                Tip = dependents > 0
                    ? string.Format(VPBTranslation.T("gallery.detail.tip.dependents_fmt", "Filter the gallery to dependents of {0}"), itemName)
                    : VPBTranslation.T("gallery.detail.tip.dependents_none", "No dependents")
            });

            return fields;
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

        private void DetailStripApplyDescPlacement()
        {
            // Narrow: single truncated Desc under actions. Wide: scroll viewport in SideCol.
            bool showLeft = _detailStripWantDesc && !_detailStripSideVisible;
            DetailStripSetFlexLineActive(_detailStripDesc, showLeft);
            if (_detailStripSideDescScrollGO != null)
                _detailStripSideDescScrollGO.SetActive(_detailStripSideVisible && _detailStripWantDesc);
        }

        private void DetailStripSyncSideColumn(float s)
        {
            if (_detailStripSideColGO == null) return;
            if (s <= 0f) s = 1f;

            bool show = DetailStripCanShowSide(s);
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

            if (_detailStripSideVisible != show)
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

            DetailStripApplyDescPlacement();
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
                // Multi path already formats shared/mixed — rebuild lightly from user tags only when side open.
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

                string tagsText;
                if (mixed)
                {
                    string sharedFmt = DetailStripFormatTagNames(shared);
                    tagsText = !string.IsNullOrEmpty(sharedFmt)
                        ? string.Format(VPBTranslation.T("gallery.detail.shared_tags", "Tags (shared): {0}"), sharedFmt)
                        : VPBTranslation.T("gallery.detail.mixed_tags", "Mixed tags — click to tag");
                }
                else if (shared != null && shared.Count > 0)
                {
                    tagsText = string.Format(
                        VPBTranslation.T("gallery.detail.tags_fmt", "Tags: {0}"),
                        DetailStripFormatTagNames(shared));
                }
                else
                    tagsText = VPBTranslation.T("gallery.detail.no_tags", "No tags — click to add");

                // When side collapsed, append native tags from first item into line.
                if (!_detailStripSideVisible && file != null)
                {
                    string regions = DetailStripResolveRegionTags(file);
                    if (!string.IsNullOrEmpty(regions))
                        tagsText = tagsText + "  ·  " + regions;
                }

                string applies = VPBTranslation.T("gallery.detail.tags_applies_all", "applies to all selected");
                _detailStripTags.text = tagsText + " · " + applies;
                DetailStripSetFlexLineActive(_detailStripTags, true);
                return;
            }

            if (file == null)
            {
                DetailStripSetFlexLineActive(_detailStripTags, false);
                return;
            }

            bool includeNative = !_detailStripSideVisible;
            string tagsLine = DetailStripResolveTagsLine(file, includeNative);
            _detailStripTags.text = !string.IsNullOrEmpty(tagsLine)
                ? string.Format(VPBTranslation.T("gallery.detail.tags_fmt", "Tags: {0}"), tagsLine)
                : VPBTranslation.T("gallery.detail.no_tags", "No tags — click to add");
            DetailStripSetFlexLineActive(_detailStripTags, true);
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
            float lineH = DetailStripLineHeight(s);
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

            float clusterW = 0f;
            for (int i = 0; i < cluster.Count; i++)
            {
                clusterW += DetailStripEstimateFieldWidth(cluster[i], s);
                if (i > 0) clusterW += sepW;
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
                    DetailStripCreateMetaField(rowGO, flow[idxs[j]], s, lineH);
                }
            }

            if (cluster.Count > 0 && rowIdx < DetailStripMetaMaxRows)
            {
                // Append cluster to last flow row when it fits with padding; else new row.
                bool appended = false;
                if (rowIdx > 0)
                {
                    GameObject last = _detailStripMetaRows[rowIdx - 1];
                    float lastW = DetailStripEstimateRowWidth(packed.Count > 0 ? packed[packed.Count - 1] : null, flowWidths, sepW);
                    if (last != null && lastW + sepW + clusterW <= avail * 0.98f)
                    {
                        for (int j = 0; j < cluster.Count; j++)
                        {
                            DetailStripAddMetaSep(last, s);
                            DetailStripCreateMetaField(last, cluster[j], s, lineH);
                        }
                        appended = true;
                    }
                }
                if (!appended)
                {
                    GameObject rowGO = _detailStripMetaRows[rowIdx++];
                    if (rowGO != null)
                    {
                        rowGO.SetActive(true);
                        DetailStripNormalizeRowRect(rowGO);
                        for (int j = 0; j < cluster.Count; j++)
                        {
                            if (j > 0) DetailStripAddMetaSep(rowGO, s);
                            DetailStripCreateMetaField(rowGO, cluster[j], s, lineH);
                        }
                    }
                }
            }

            DetailStripSyncMetaHostHeight(s);
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
            float sepLineH = DetailStripLineHeight(s);
            UI.AddLE(sep.gameObject, minHeight: sepLineH, preferredHeight: sepLineH, flexibleWidth: 0f, flexibleHeight: 0f);
        }

        /// <summary>Muted label + colored value. Soft-caps long values (author) without stretching row.</summary>
        private float DetailStripCreateMetaField(GameObject row, DetailStripMetaField field, float s, float lineH)
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
                UI.AddLE(label.gameObject, flexibleWidth: 0f, flexibleHeight: 0f, minHeight: lineH, preferredHeight: lineH);
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
            UI.AddLE(value.gameObject, flexibleWidth: 0f, flexibleHeight: 0f, minHeight: lineH, preferredHeight: lineH);
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
                try { showScan = ScanWhitelistManager.IsScanExcludedBadgeVisible(file); } catch { }
                try { showTags = IsGalleryUserTagBadgeVisible(file); } catch { }
            }
            if (_detailStripBadgeAuto != null) _detailStripBadgeAuto.SetActive(showAi);
            if (_detailStripBadgeHide != null) _detailStripBadgeHide.SetActive(showHide);
            if (_detailStripBadgeScan != null) _detailStripBadgeScan.SetActive(showScan);
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

            // Multi: show badge if any selected item has it.
            bool showAi = false, showHide = false, showScan = false, showTags = false;
            for (int i = 0; i < selectedFiles.Count; i++)
            {
                FileEntry f = selectedFiles[i];
                if (f == null) continue;
                try { if (!showAi && f.IsAutoInstall()) showAi = true; } catch { }
                try { if (!showHide && PackageHidePrefs.IsGalleryHideBadgeVisible(f)) showHide = true; } catch { }
                try { if (!showScan && ScanWhitelistManager.IsScanExcludedBadgeVisible(f)) showScan = true; } catch { }
                try { if (!showTags && IsGalleryUserTagBadgeVisible(f)) showTags = true; } catch { }
                if (showAi && showHide && showScan && showTags) break;
            }
            if (_detailStripBadgeAuto != null) _detailStripBadgeAuto.SetActive(showAi);
            if (_detailStripBadgeHide != null) _detailStripBadgeHide.SetActive(showHide);
            if (_detailStripBadgeScan != null) _detailStripBadgeScan.SetActive(showScan);
            if (_detailStripBadgeTags != null) _detailStripBadgeTags.SetActive(showTags);
            if (_detailStripBadgeRowGO != null)
                _detailStripBadgeRowGO.SetActive(showAi || showHide || showScan || showTags);
        }

        private void DetailStripLoadThumb(FileEntry file)
        {
            _detailStripThumbFile = file;
            if (_detailStripThumb == null || file == null) return;
            _detailStripThumb.color = Color.white;
            try
            {
                // Square cell: center-crop like grid (no AspectRatioFitter on this RawImage).
                LoadThumbnail(
                    file,
                    _detailStripThumb,
                    gridThumbnailContext: true,
                    turboJpegThumbnailDenom: 1,
                    thumbnailUnityDecodeOnly: true);
            }
            catch
            {
                _detailStripThumb.texture = null;
                _detailStripThumb.color = new Color(1f, 1f, 1f, 0.15f);
            }
        }

        /// <summary>Mouse wheel over strip thumb → previous/next item in current filtered list.</summary>
        private void DetailStripOnThumbScroll(float scrollDelta)
        {
            if (Mathf.Abs(scrollDelta) < 0.01f) return;
            if (IsSettingsPanelOpen()) return;
            // Hold right on preview + wheel → nudge star rating (does not scrub selection).
            if (DetailStripThumbRateScrollActive())
            {
                DetailStripNudgeRatingFromScroll(scrollDelta);
                return;
            }
            if (currentFilteredFiles == null || currentFilteredFiles.Count == 0) return;
            // Unity scroll up is positive → previous item (matches typical list feel).
            int step = scrollDelta > 0f ? -1 : 1;
            DetailStripBeginScrubHeightLock();
            _detailStripScrubPendingSteps += step;
            _detailStripScrubLastInputTime = Time.unscaledTime;
            _detailStripScrubActive = true;
            // Apply immediately for snappy preview; soft commit waits for idle.
            DetailStripProcessScrubPending();
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
                return;

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
            DetailStripTryCopyThumbFromVisibleGrid(file, listIndex);
        }

        private bool DetailStripTryCopyThumbFromVisibleGrid(FileEntry file, int listIndex)
        {
            if (file == null || _detailStripThumb == null || recyclingGrid == null || recyclingGrid.content == null)
                return false;
            try
            {
                foreach (Transform child in recyclingGrid.content)
                {
                    if (child == null || !child.gameObject.activeSelf) continue;
                    RecyclingGridItem rgv = child.GetComponent<RecyclingGridItem>();
                    if (rgv != null && listIndex >= 0 && rgv.index != listIndex) continue;
                    if (rgv == null)
                    {
                        UIDraggableItem diag = child.GetComponent<UIDraggableItem>();
                        if (diag == null || !ReferenceEquals(diag.FileEntry, file)) continue;
                    }
                    // Grid cell thumb is direct child "Thumbnail".
                    Transform imgT = child.Find("Thumbnail");
                    RawImage ri = imgT != null ? imgT.GetComponent<RawImage>() : null;
                    if (ri == null || ri.texture == null || ri == _detailStripThumb) continue;
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
                DetailStripEndScrubHeightLock();
                return;
            }

            // Enrich when idle: patch existing texts in place (no destroy / SetActive / LoadThumbnail).
            try { DetailStripEnrichScrubFields(file); } catch { }
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
                string tagsLine = DetailStripResolveTagsLine(file, includeNative: !_detailStripSideVisible);
                string tagsText = !string.IsNullOrEmpty(tagsLine)
                    ? string.Format(VPBTranslation.T("gallery.detail.tags_fmt", "Tags: {0}"), tagsLine)
                    : VPBTranslation.T("gallery.detail.no_tags", "No tags — click to add");
                if (!string.Equals(_detailStripTags.text, tagsText, StringComparison.Ordinal))
                    _detailStripTags.text = tagsText;
            }
            if (_detailStripPath != null && DetailStripFlexLineVisible(_detailStripPath))
            {
                string path = DetailStripResolvePathLine(file);
                if (!string.IsNullOrEmpty(path)
                    && !string.Equals(_detailStripPath.text, path, StringComparison.Ordinal))
                    _detailStripPath.text = path;
            }
            if (_detailStripDesc != null && DetailStripFlexLineVisible(_detailStripDesc))
            {
                string desc = DetailStripResolveDescription(file);
                if (!string.IsNullOrEmpty(desc))
                {
                    const int maxChars = 110;
                    string shown = desc.Length > maxChars ? desc.Substring(0, maxChars - 1) + "…" : desc;
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

            // Thumb: retry grid copy after settle (still never LoadThumbnail — that blanks).
            try { DetailStripTryCopyThumbFromVisibleGrid(file, _detailStripScrubIndex); } catch { }

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
                        PerformDeleteMove(toDelete, deletedPkgDir, out moved, out failed);
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

            try { DetailStripToggleTagMenu(); }
            catch (Exception ex) { LogUtil.LogError("[VPB] DetailStrip quick tag: " + ex.Message); }
        }

        private void DetailStripEnsureTagMenu()
        {
            if (_detailStripTagMenuRoot != null || backgroundBoxGO == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            _detailStripTagMenuRoot = UI.CreatePopupMenuRoot(backgroundBoxGO, "DetailStripTagMenu", DetailStripCloseTagMenu);
            _detailStripTagMenuPanelGO = UI.CreatePopupMenuPanel(
                _detailStripTagMenuRoot, "DetailStripTagMenuPanel",
                AnchorPresets.bottomLeft,
                new Vector2(DetailStripTagMenuWidthRef, DetailStripTagMenuHeightRef),
                new Vector2(16f, 130f));
            _detailStripTagMenuPanelRT = _detailStripTagMenuPanelGO.GetComponent<RectTransform>();

            // Fixed height panel — do not grow with tag count.
            ContentSizeFitter panelCsf = _detailStripTagMenuPanelGO.GetComponent<ContentSizeFitter>();
            if (panelCsf != null) UnityEngine.Object.Destroy(panelCsf);
            VerticalLayoutGroup panelVlg = _detailStripTagMenuPanelGO.GetComponent<VerticalLayoutGroup>();
            if (panelVlg != null)
            {
                panelVlg.childForceExpandHeight = false;
                panelVlg.childControlHeight = true;
            }

            _detailStripTagMenuScrollGO = UI.CreateVScrollableContent(
                _detailStripTagMenuPanelGO,
                new Color(0f, 0f, 0f, 0f),
                AnchorPresets.topMiddle,
                DetailStripTagMenuWidthRef - 16f,
                DetailStripTagMenuScrollHeightRef,
                Vector2.zero,
                12f,
                GalleryUiDesignTokens.PopupMenuRowSpacingRef,
                false);
            UI.AddLE(_detailStripTagMenuScrollGO,
                flexibleWidth: 1f,
                flexibleHeight: 1f,
                minHeight: 80f,
                preferredHeight: DetailStripTagMenuScrollHeightRef);
            ScrollRect tagSr = _detailStripTagMenuScrollGO.GetComponent<ScrollRect>();
            _detailStripTagMenuListGO = tagSr != null && tagSr.content != null ? tagSr.content.gameObject : null;
            if (tagSr != null) tagSr.movementType = ScrollRect.MovementType.Clamped;

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

            _detailStripTagMenuSearch = CreateSearchInput(_detailStripTagMenuPanelGO, DetailStripTagMenuWidthRef - 20f, val =>
            {
                _detailStripTagMenuFilter = val ?? "";
                // Create button updates immediately; list rebuild is debounced (avoids SQLite + full UI churn per key).
                DetailStripUpdateTagMenuCreateButton();
                DetailStripScheduleTagMenuFilterRebuild();
            }, () =>
            {
                _detailStripTagMenuFilter = "";
                DetailStripStopTagMenuFilterRebuild();
                DetailStripUpdateTagMenuCreateButton();
                DetailStripRebuildTagMenuList(fullLayoutSync: false);
            });
            if (_detailStripTagMenuSearch != null)
            {
                if (_detailStripTagMenuSearch.placeholder is Text ph)
                    ph.text = VPBTranslation.T("gallery.detail.tag_search", "Filter tags…");
                UI.AddLE(_detailStripTagMenuSearch.gameObject,
                    minHeight: GalleryUiDesignTokens.SearchFieldHeightRef,
                    preferredHeight: GalleryUiDesignTokens.SearchFieldHeightRef,
                    flexibleWidth: 1f);
                // Keep filter at bottom of panel.
                _detailStripTagMenuSearch.transform.SetAsLastSibling();
            }

            _detailStripTagMenuRoot.SetActive(false);
        }

        private void DetailStripOnTagMenuCreateClick()
        {
            string filter = (_detailStripTagMenuFilter ?? "").Trim();
            DetailStripCloseTagMenu();
            if (string.IsNullOrEmpty(filter))
                DetailStripShowNewTagModal();
            else
                DetailStripCreateAndApplyTag(filter);
        }

        private void DetailStripCloseTagMenu()
        {
            DetailStripStopTagMenuFilterRebuild();
            if (_detailStripTagMenuRoot != null)
                _detailStripTagMenuRoot.SetActive(false);
            _detailStripTagMenuFilter = "";
            if (_detailStripTagMenuSearch != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_detailStripTagMenuSearch.text))
                        _detailStripTagMenuSearch.text = "";
                }
                catch { }
            }
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

            _detailStripTagMenuFilter = "";
            if (_detailStripTagMenuSearch != null)
            {
                try { _detailStripTagMenuSearch.text = ""; } catch { }
            }
            DetailStripInvalidateTagMenuCaches();
            DetailStripEnsureTagMenuCaches();
            DetailStripRebuildTagMenuList(fullLayoutSync: true);
            DetailStripPositionTagMenu();
            _detailStripTagMenuRoot.SetActive(true);
            _detailStripTagMenuRoot.transform.SetAsLastSibling();
            try
            {
                if (_detailStripTagMenuSearch != null)
                    _detailStripTagMenuSearch.ActivateInputField();
            }
            catch { }
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
            DetailStripRebuildTagMenuList(fullLayoutSync: false);
        }

        private void DetailStripUpdateTagMenuCreateButton()
        {
            if (_detailStripTagMenuCreateGO == null) return;
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

        private void DetailStripPositionTagMenu()
        {
            if (_detailStripTagMenuPanelRT == null || backgroundBoxGO == null) return;
            float s = ChromeScale;
            if (s <= 0f) s = 1f;

            RectTransform anchor = _detailStripTags != null ? _detailStripTags.rectTransform : null;

            RectTransform parentRT = backgroundBoxGO.GetComponent<RectTransform>();
            if (anchor != null && parentRT != null)
            {
                Camera cam = null;
                try
                {
                    Canvas c = backgroundBoxGO.GetComponentInParent<Canvas>();
                    if (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay)
                        cam = c.worldCamera;
                }
                catch { }

                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, anchor.position);
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRT, screen, cam, out local))
                {
                    _detailStripTagMenuPanelRT.anchorMin = _detailStripTagMenuPanelRT.anchorMax = new Vector2(0.5f, 0.5f);
                    _detailStripTagMenuPanelRT.pivot = new Vector2(0f, 0f);
                    _detailStripTagMenuPanelRT.anchoredPosition = local + new Vector2(0f, 8f * s);
                    _detailStripTagMenuPanelRT.sizeDelta = new Vector2(DetailStripTagMenuWidthRef * s, DetailStripTagMenuHeightRef * s);
                    return;
                }
            }

            _detailStripTagMenuPanelRT.anchorMin = _detailStripTagMenuPanelRT.anchorMax = new Vector2(0f, 0f);
            _detailStripTagMenuPanelRT.pivot = new Vector2(0f, 0f);
            _detailStripTagMenuPanelRT.anchoredPosition = new Vector2(16f * s, DetailStripRowHeight(s) + 16f * s);
            _detailStripTagMenuPanelRT.sizeDelta = new Vector2(DetailStripTagMenuWidthRef * s, DetailStripTagMenuHeightRef * s);
        }

        private void DetailStripSyncTagMenuLayout(float s)
        {
            if (_detailStripTagMenuPanelGO == null) return;
            if (s <= 0f) s = 1f;
            if (_detailStripTagMenuPanelRT != null)
                _detailStripTagMenuPanelRT.sizeDelta = new Vector2(DetailStripTagMenuWidthRef * s, DetailStripTagMenuHeightRef * s);

            if (_detailStripTagMenuScrollGO != null)
            {
                LayoutElement scrollLe = _detailStripTagMenuScrollGO.GetComponent<LayoutElement>();
                if (scrollLe == null) scrollLe = UI.AddLE(_detailStripTagMenuScrollGO);
                scrollLe.flexibleHeight = 1f;
                scrollLe.minHeight = 80f * s;
                scrollLe.preferredHeight = DetailStripTagMenuScrollHeightRef * s;
                scrollLe.flexibleWidth = 1f;
                RectTransform scrollRT = _detailStripTagMenuScrollGO.GetComponent<RectTransform>();
                if (scrollRT != null)
                    scrollRT.sizeDelta = new Vector2((DetailStripTagMenuWidthRef - 16f) * s, DetailStripTagMenuScrollHeightRef * s);
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
                RescaleSearchInput(_detailStripTagMenuSearch, s, GalleryUiDesignTokens.SearchFieldHeightRef);
                LayoutElement searchLe = _detailStripTagMenuSearch.GetComponent<LayoutElement>();
                if (searchLe == null)
                    searchLe = UI.AddLE(_detailStripTagMenuSearch.gameObject);
                searchLe.minHeight = GalleryUiDesignTokens.SearchFieldHeightRef * s;
                searchLe.preferredHeight = GalleryUiDesignTokens.SearchFieldHeightRef * s;
                searchLe.flexibleWidth = 1f;
                _detailStripTagMenuSearch.transform.SetAsLastSibling();
            }

            if (_detailStripTagMenuListGO != null)
            {
                // Never pass panelWidthRef here — content is stretch-width inside ScrollRect;
                // setting sizeDelta.x clips tag text to the trailing edge.
                RectTransform contentRT = _detailStripTagMenuListGO.GetComponent<RectTransform>();
                if (contentRT != null)
                    contentRT.sizeDelta = new Vector2(0f, contentRT.sizeDelta.y);

                ScaleVerticalPopupMenuRows(
                    _detailStripTagMenuListGO, s,
                    GalleryUiDesignTokens.PopupMenuRowHeightCompactRef,
                    GalleryUiDesignTokens.PopupMenuRowFontRef,
                    0f);

                if (contentRT != null)
                    contentRT.sizeDelta = new Vector2(0f, contentRT.sizeDelta.y);
            }
        }

        private void DetailStripRebuildTagMenuList(bool fullLayoutSync)
        {
            if (_detailStripTagMenuListGO == null) return;
            UI.DestroyAllChildren(_detailStripTagMenuListGO.transform);

            float s = ChromeScale;
            if (s <= 0f) s = 1f;
            // Pre-scale row height so filter rebuilds skip ScaleVerticalPopupMenuRows.
            float rowH = GalleryUiDesignTokens.PopupMenuRowHeightCompactRef * s;
            string filter = (_detailStripTagMenuFilter ?? "").Trim();

            DetailStripEnsureTagMenuCaches();
            HashSet<string> applied = _detailStripTagMenuAppliedCache
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> vocab = _detailStripTagMenuVocabCache ?? new List<string>(0);

            int rowsAdded = 0;
            int skipped = 0;

            if (applied.Count > 0)
            {
                var appliedList = new List<string>(applied);
                appliedList.Sort(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < appliedList.Count; i++)
                {
                    string tag = appliedList[i];
                    if (!DetailStripTagMatchesFilter(tag, filter)) continue;
                    if (rowsAdded >= DetailStripTagMenuMaxRows) { skipped++; continue; }
                    string tagSnap = tag;
                    DetailStripAddTagMenuRow(
                        "✓ " + tag, rowH, s, isActive: true,
                        () =>
                        {
                            DetailStripCloseTagMenu();
                            toggleTagForSelectedItems(tagSnap);
                        });
                    rowsAdded++;
                }
            }

            for (int i = 0; i < vocab.Count; i++)
            {
                string tag = vocab[i];
                if (string.IsNullOrEmpty(tag)) continue;
                if (applied.Contains(tag)) continue;
                if (!DetailStripTagMatchesFilter(tag, filter)) continue;
                if (rowsAdded >= DetailStripTagMenuMaxRows) { skipped++; continue; }
                string tagSnap = tag;
                DetailStripAddTagMenuRow(
                    tagSnap, rowH, s, isActive: false,
                    () =>
                    {
                        DetailStripCloseTagMenu();
                        ApplyUserTagsToFileEntries(new List<string> { tagSnap }, selectedFiles, remove: false);
                    });
                rowsAdded++;
            }

            if (skipped > 0)
            {
                UI.AddStretchPopupMenuRow(
                    _detailStripTagMenuListGO.transform,
                    string.Format(VPBTranslation.T("gallery.detail.tag_more_fmt", "… {0} more — refine filter"), skipped),
                    () => { },
                    isActive: false,
                    enabled: false,
                    rowHeight: rowH);
            }

            DetailStripUpdateTagMenuCreateButton();

            if (fullLayoutSync)
                DetailStripSyncTagMenuLayout(s);
            else
            {
                // Keep scroll content stretch-width; avoid full panel rescale while typing.
                RectTransform contentRT = _detailStripTagMenuListGO.GetComponent<RectTransform>();
                if (contentRT != null)
                    contentRT.sizeDelta = new Vector2(0f, contentRT.sizeDelta.y);
            }
        }

        private void DetailStripAddTagMenuRow(string label, float rowH, float s, bool isActive, UnityAction onClick)
        {
            GameObject row = UI.AddStretchPopupMenuRow(
                _detailStripTagMenuListGO.transform,
                label,
                onClick,
                isActive: isActive,
                enabled: true,
                rowHeight: rowH);
            if (row == null) return;
            Text t = row.GetComponentInChildren<Text>(true);
            if (t != null)
                GalleryUiMetrics.ApplyFont(t, GalleryUiDesignTokens.PopupMenuRowFontRef, s, GalleryUiDesignTokens.FontMinRef);
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

        private void DetailStripCreateAndApplyTag(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                ShowTemporaryStatus(VPBTranslation.T("gallery.usertags.none_selected", "Nothing selected."), 1.5f);
                return;
            }
            string norm = raw.Trim();
            try
            {
                if (VpbLocalDatabase.TryEnsureGalleryUserTagInVocabulary(norm, out string n) && !string.IsNullOrEmpty(n))
                    norm = n;
            }
            catch { }
            ApplyUserTagsToFileEntries(new List<string> { norm }, selectedFiles, remove: false);
            DetailStripInvalidateTagMenuCaches();
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
            const int maxChars = 110;
            if (_detailStripDesc != null)
            {
                _detailStripDesc.text = desc.Length > maxChars
                    ? desc.Substring(0, maxChars - 1) + "…"
                    : desc;
            }
            _detailStripWantDesc = true;
            // Visibility decided by side column (wide) vs left flex line (narrow).
            DetailStripApplyDescPlacement();
        }

        private static string DetailStripResolveDescription(FileEntry file)
        {
            if (file == null) return "";
            try
            {
                VarPackage pkg = TryResolvePackageForThumbPlaceholder(file);
                if (pkg == null) return "";
                try { pkg.TryEnsureMetaJsonLiteFields(); } catch { }
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
    }
}
