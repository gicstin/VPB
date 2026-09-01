namespace VPB
{
    /// <summary>Design-reference pixel sizes at gallery UI scale 1.0 (before host/DPI factors).</summary>
    public static class GalleryUiDesignTokens
    {
        /// <summary>Legacy BepInEx overlay UI Scale default (Settings.UIScale). Not used for gallery HostScale.</summary>
        public const float VamUiScaleDesignBaseline = 1.5f;
        /// <summary>VaM <c>SuperController.monitorUIScale</c> default. Gallery desktop HostScale = monitorUIScale / this.</summary>
        public const float VamMonitorUiScaleDesignBaseline = 1.0f;

        // ── Golden ratio (major splits + decorative aspects only; not spacing/type) ──
        /// <summary>φ ≈ 1.618 — aspect ratios and scale accents.</summary>
        public const float GoldenRatio = 1.618f;
        /// <summary>1/φ ≈ 0.618 — major share of a unit split (e.g. desktop dock width).</summary>
        public const float GoldenRatioMajor = 1f / GoldenRatio;
        /// <summary>1/φ² ≈ 0.382 — minor share of a unit split (e.g. category side sub-pane).</summary>
        public const float GoldenRatioMinor = 1f - GoldenRatioMajor;

        // ── Typography (3-step hierarchy: caption / body / title) ──────────
        /// <summary>Legacy alias for <see cref="FontBodyRef"/> (dense chrome default).</summary>
        public const int FontRef = 16;
        /// <summary>Primary readable prose — buttons, fields, list rows.</summary>
        public const int FontBodyRef = FontRef;
        /// <summary>Section / window titles — larger than body.</summary>
        public const int FontTitleRef = 18;
        /// <summary>Hints, FPS, status secondary — smaller than body.</summary>
        public const int FontCaptionRef = 13;
        /// <summary>Minimum readable fontSize after int clamp (ApplyFont floor).</summary>
        public const int FontMinRef = 10;
        /// <summary>Icon-only text: fontSize ≈ control height × this factor × scale.</summary>
        public const float GlyphFontHeightFactor = 0.55f;
        /// <summary>All interactive buttons: 2× FontBody. Ratio is global and intentional.</summary>
        public const float ButtonSizeRef = FontBodyRef * 2; // 32

        // Dense power-user scale (Johnson proximity / Gestalt). Tight inside a group,
        // one step looser between groups. Never invent 3/5/6/10/14 — snap to this ladder.
        public const float Space1Ref = 2f;
        public const float Space2Ref = 4f;
        public const float Space3Ref = 8f;
        public const float Space4Ref = 12f;
        public const float Space5Ref = 16f;
        public const float Space6Ref = 24f;

        /// <summary>Hairline — icon/title tight, 1px-feel seams, caption stacks.</summary>
        public const float HairGapRef = Space1Ref;
        /// <summary>Inside a control / packed chrome row (sibling buttons, chips).</summary>
        public const float TightGapRef = Space2Ref;
        /// <summary>Between sibling controls in one group (band pad, popup pad).</summary>
        public const float ControlGapRef = Space3Ref;
        /// <summary>Between related groups (indent, menu text pad, zone gap).</summary>
        public const float GroupGapRef = Space4Ref;
        /// <summary>Between regions / dialog inset.</summary>
        public const float RegionGapRef = Space5Ref;
        /// <summary>Section / empty-state / large modal breathe.</summary>
        public const float SectionGapRef = Space6Ref;

        public const float ControlRimThicknessRef = Space1Ref;
        public const float ControlRimGutterRef = ControlRimThicknessRef;
        public const float ControlSlotHeightRef = ButtonSizeRef + ControlRimGutterRef * 2f;
        public const float BandPadRef = ControlGapRef;
        public const float BandPadHRef = BandPadRef;
        /// <summary>Chrome T/B — tight like float bars so title/toolbox/grid stay dense.</summary>
        public const float BandPadVRef = TightGapRef;
        public const float BandContentInsetVRef = BandPadVRef + ControlRimGutterRef;
        public const float ControlRowGapRef = TightGapRef;
        /// <summary>Centered modal / confirm shell inset.</summary>
        public const float DialogPadRef = RegionGapRef;
        /// <summary>Float footer / chrome L/R (matches band).</summary>
        public const float FloatChromePadHRef = ControlGapRef;
        /// <summary>Float footer / chrome T/B — tighter than L/R so bar stays Fitts-short.</summary>
        public const float FloatChromePadVRef = TightGapRef;
        /// <summary>Rounded-corner radius for gallery buttons + their hover border, as a fraction of the
        /// control's shorter side (0..0.5). Default when no user override is stored in VPB.cfg.
        /// Live value comes from <see cref="VPBConfig.GalleryElementCornerRadiusFraction"/> /
        /// <see cref="VPBConfig.EnableGalleryElementRounding"/>.</summary>
        public const float ButtonCornerRadiusFraction = 0.22f;

        // Title bar
        public const float TitleBarHeightRef = ButtonSizeRef + BandPadVRef * 2f;
        public const float TitleBarChipRef = ButtonSizeRef;
        public const float TitleBarCategoryRowHeightRef = 36f;
        public const float TitleBarTitleLeftInsetRef = 60f;
        public const int TitleFontRef = FontTitleRef;
        public const int FpsFontRef = FontCaptionRef;
        public const int StatusBarFontRef = FontCaptionRef;
        /// <summary>
        /// Shared hide grace for gallery info-bar + quick-menu assignable tips.
        /// Instant show / tip→tip; delay clear only so exit→enter on adjacent targets does not blink.
        /// </summary>
        public const float TooltipHideGraceSec = 0.12f;
        public const int CollapseArrowFontRef = FontRef;
        public const int TitleBarChipFontRef = FontRef;
        public const int TitleBarRatingFontRef = FontRef;
        public const int TitleBarRefreshFontRef = FontRef;
        public const int TitleBarHelpFontRef = FontRef;
        public const int TitleBarOverflowFontRef = FontRef;
        public const int TitleBarWindowFontRef = FontRef;
        public const int CategoryQuickArrowFontRef = FontRef;
        public const int GlobalSourceFilterFontRef = FontRef;

        // Search fields
        public const float SearchFieldHeightRef = 35f;
        public const int SearchFieldFontRef = FontRef;
        public const float SearchIconSizeRef = 24f;
        public const float SearchIconLeftPadRef = TightGapRef;
        /// <summary>TextArea left = icon pad + glyph + control gap (clears glass).</summary>
        public const float SearchTextLeftInsetRef = SearchIconLeftPadRef + SearchIconSizeRef + ControlGapRef;
        public const float SearchClearBtnSizeRef = ButtonSizeRef;
        public const float SearchTextRightInsetRef = SearchClearBtnSizeRef;
        public const int SearchClearFontRef = FontRef;
        public const float SearchClearBtnRightInsetRef = 0f;
        public const float SearchIconButtonPadRef = TightGapRef;
        /// <summary>Pad around float chrome search rows (settings / filter presets).</summary>
        public const float FloatSearchRowPadRef = BandPadRef;
        /// <summary>Title-adjacent search row height (field + pad). Shared by settings + presets.</summary>
        public const float FloatSearchRowHeightRef = SearchFieldHeightRef + FloatSearchRowPadRef * 2f;
        /// <summary>Window-type glyph in float title bars (settings gear, tags, filter, import).</summary>
        public const float FloatTitleWindowIconSizeRef = 22f;
        /// <summary>Space after window icon before title label (design px at scale 1).</summary>
        public const float FloatTitleWindowIconGapRef = TightGapRef;
        /// <summary>Float title bar L/R pad — keep icon near left (grip + this).</summary>
        public const float FloatTitleBarPadHRef = TightGapRef;
        /// <summary>Float title bar T/B pad.</summary>
        public const float FloatTitleBarPadVRef = TightGapRef;
        /// <summary>HLG spacing between grip · icon · title · chrome.</summary>
        public const float FloatTitleBarSpacingRef = HairGapRef;
        /// <summary>Drag-grip column width before window icon.</summary>
        public const float FloatTitleGripWidthRef = 12f;
        /// <summary>Pad inside float title/footer chrome icon buttons (collapse / close / resize).</summary>
        public const float FloatChromeIconPadRef = TightGapRef;
        /// <summary>Pad inside tree-row expand chevron wells (Plugins / Strip Keep).</summary>
        public const float TreeRowExpandIconPadRef = TightGapRef;

        // Resize handles — sized/positioned like the corner-most bar button (40px, seated in the bar).
        // Horizontal centre inset = EdgeMargin + half button (mirrors float footer L/R pad).
        // Vertical centre inset = the bar's half-height so the handle lines up with the bar buttons.
        public const float ResizeHandleFixedHitRef = 40f;
        public const float ResizeHandleCornerHitRef = 40f;
        public const float ResizeHandleEdgeMarginRef = ControlGapRef;
        public const float ResizeHandleFooterCenterYRef = FooterBarHeightRef * 0.5f;
        public const float ResizeHandleTitleCenterYRef = TitleBarHeightRef * 0.5f;
        public const float ResizeHandleLegacyHitRef = 30f;

        // Footer perf controls
        public const int FooterPerfToggleFontRef = FontRef;
        public const int FooterPerfStepFontRef = FontRef;

        // Side tab column
        public const float SideTabColumnWidthRef = 220f;
        public const float SideTabSideMarginRef = BandPadRef;
        public const float SideTabOpenGridInsetRef = SideTabColumnWidthRef + BandPadRef * 2f;
        public const float SideTabClosedGridInsetRef = 0f;
        public const float SideTabRowHeightRef = ButtonSizeRef;
        /// <summary>
        /// Creator list ★ badge — inset inside <see cref="SideTabRowHeightRef"/> so hover rim
        /// stays smaller than the row inward outline (badge was full row tall + outward rim).
        /// </summary>
        public const float CreatorRatingBadgeSizeRef = ButtonSizeRef - ControlGapRef;
        public const float SideTabControlGapRef = ControlGapRef;
        public const float SideTabRefreshBtnWidthRef = ButtonSizeRef;
        public const float SideTabMainSearchSortReserveRef =
            BandPadRef + SideTabRowHeightRef + ControlGapRef + BandPadRef;
        public const float SideTabRowSpacingRef = ControlRowGapRef;
        /// <summary>Left inset for facet rows nested under the selected category (accordion).</summary>
        public const float SideTabAccordionIndentRef = RegionGapRef;
        public const float SideTabFilterRowBottomGapRef = 0f;
        /// <summary>Gap below split seam before lower-pane sort+search row.</summary>
        public const float SideTabSubFilterRowTopGapRef = BandPadRef;
        public const float SideTabRowPadRef = BandPadRef;
        public const float SideTabScrollBarWidthRef = 15f;
        public const int TabButtonFontRef = FontRef;
        public const int TabButtonFontMin = FontMinRef;
        public const float TabButtonMinWidthRef = 140f;
        public const float TabButtonPreferredWidthRef = 170f;

        // Footer / filter chrome
        public const float FooterBarHeightRef = ButtonSizeRef + BandPadVRef * 2f;
        /// <summary>Session footer chip — icon + Play/In.</summary>
        public const float FooterSessionBtnWidthRef = 88f;
        public const float FooterInfoRowHeightRef = ControlSlotHeightRef;
        public const float FooterToolboxTopRef = FooterBarHeightRef + FooterInfoRowHeightRef;
        /// <summary>Near-grid sticky mode / apply-semantics banner (below filter chips).</summary>
        public const float ModeSemanticsBannerHeightRef = 34f;
        public const float ModeSemanticsBannerGapRef = TightGapRef;
        /// <summary>
        /// Context Bar: ActiveFilterChipBar hard-caps to this many wrap rows.
        /// Extra filters go to +N overflow (never grow grid inset).
        /// </summary>
        public const int ContextBarMaxFilterChipRows = 1;
        /// <summary>
        /// Drag floor ≈ title + actions + tags + path (no meta). Smaller than old 96 comfort.
        /// </summary>
        public const float FooterDetailStripMinHeightRef = 80f;
        /// <summary>Design max height at scale 1 for user drag / content clamp.</summary>
        public const float FooterDetailStripHeightRef = 400f;
        /// <summary>Design line height used with <see cref="FooterDetailStripHeightRef"/> for row budget.</summary>
        public const float FooterDetailStripLineHeightRef = 18f;
        /// <summary>
        /// Min hit height for detail-strip action links + meta rows (design px at scale 1).
        /// Larger than <see cref="FooterDetailStripLineHeightRef"/>; below full <see cref="ButtonSizeRef"/>.
        /// </summary>
        /// <summary>Hit pad for actions/meta — above line height, below full button (dense strip).</summary>
        public const float FooterDetailStripHitHeightRef = 22f;
        /// <summary>Equal gap between detail-strip text bands (title / meta / actions / flex lines).</summary>
        public const float FooterDetailStripBandGapRef = HairGapRef;
        /// <summary>Detail-strip thumb edge cap (square); must stay ≥ strip max so preview stays flush.</summary>
        public const float FooterDetailStripThumbMaxRef = FooterDetailStripHeightRef;
        /// <summary>Prev/next overlay on thumb — same size as gallery chrome buttons.</summary>
        public const float FooterDetailStripThumbNavBtnRef = ButtonSizeRef;
        /// <summary>Edge inset for thumb nav overlay buttons (design px at scale 1).</summary>
        public const float FooterDetailStripThumbNavInsetRef = TightGapRef;
        /// <summary>Scrub index chip height on thumb (n/N) — match button chrome.</summary>
        public const float FooterDetailStripThumbScrubIndexHRef = ButtonSizeRef;
        /// <summary>Top drag grip hit height (sits above strip content, not over it).</summary>
        public const float FooterDetailStripResizeGripRef = 14f;
        /// <summary>Center grab-handle size inside the resize grip.</summary>
        public const float FooterDetailStripResizePillWRef = 56f;
        public const float FooterDetailStripResizePillHRef = 5f;
        /// <summary>
        /// When strip height reaches this (design px), description + package tags prefer
        /// main-column rows over the wide side column.
        /// </summary>
        public const float FooterDetailStripStackSideMinHeightRef = 220f;
        /// <summary>
        /// Height hysteresis for stack-side vs SideCol. Must cover typical height delta when
        /// desc/package-tags move left (extra wrap rows + gaps) or height hunts forever.
        /// </summary>
        public const float FooterDetailStripStackSideHysteresisRef = 96f;
        /// <summary>Max wrapped description lines in the main column when stacking by height.</summary>
        public const int FooterDetailStripLeftDescMaxLines = 5;
        /// <summary>Min strip width before right info column (desc + native tags) opens.</summary>
        public const float FooterDetailStripSideMinWidthRef = 600f;
        /// <summary>Hysteresis band so side open/close does not flicker at the threshold.</summary>
        public const float FooterDetailStripSideHysteresisRef = 64f;
        /// <summary>Right info column width clamp (design px at scale 1).</summary>
        public const float FooterDetailStripSideMinColWidthRef = 180f;
        public const float FooterDetailStripSideMaxColWidthRef = 340f;
        /// <summary>Left text column must keep at least this width when side is open.</summary>
        public const float FooterDetailStripSideLeftReserveRef = 240f;
        /// <summary>Scrollbar width for detail-strip description scroll viewport.</summary>
        public const float FooterDetailStripSideScrollBarWidthRef = 8f;
        /// <summary>Max wrapped lines for native tags under description scroll.</summary>
        public const int FooterDetailStripSideTagsMaxLines = 2;
        public const float FilterChipRowHeightRef = ButtonSizeRef;
        public const float FilterChipRowMarginRef = TightGapRef;
        public const float FilterChipDismissSizeRef = ButtonSizeRef - TightGapRef;
        public const float FilterChipLabelDismissGapRef = TightGapRef;

        // Side rail buttons (settings, follow, save, etc.)
        public const float SideButtonWidthRef = 120f;
        public const float SideButtonHeightRef = ButtonSizeRef;
        public const float SideButtonSquareRef = ButtonSizeRef;
        public const float SideButtonIconPadRef = SearchIconButtonPadRef;
        public const float SideButtonContainerWidthRef = 130f;
        public const float SideButtonContainerOffsetRef = 140f;
        public const float SideButtonSpacingRef = ButtonSizeRef + TightGapRef;
        /// <summary>Extra gap between Layout / Browse / Tools rail zones when EnableButtonGaps is on.</summary>
        public const float SideButtonGroupGapRef = GroupGapRef;
        /// <summary>gapTier multiplier at zone starts in <c>GetSideButtonsLayout</c>.</summary>
        public const int SideButtonZoneGapTier = 2;
        public const float SideButtonZoneSepHeightRef = 1f;
        public const float SideButtonZoneSepWidthRef = 20f;
        public const float SideButtonSubmenuWidthFactorRef = 1.6f;
        public const float SideButtonEdgeInsetRef = TightGapRef;
        public const int SideButtonFontRef = FontRef;
        public const int SideButtonFontMin = FontMinRef;
        public const int SideButtonSubmenuFontRef = FontRef;
        public const float SideHoverStripWidthRef = 30f;
        public const float SideHoverStripOffsetRef = 35f;

        // Grid thumbnails (overlay scale derives from cell geometry, not user chrome scale)
        public const float GridCellRefSize = 100f;
        public const float GridBadgeSizeRef = 32f;
        /// <summary>Legacy single-line strip height at scale 1 (reference).</summary>
        public const float GridLabelHeightRef = 22f;
        /// <summary>
        /// Unity UI Text line-box vs fontSize for each grid caption row.
        /// Dual strip = (primaryFs + secondaryFs) × this + primaryFs × <see cref="GridLabelStripVPadMul"/>.
        /// Single strip = primaryFs × this + pad (when filtered set has no dual captions).
        /// Absolute pixels — not a fraction of cell width (column zoom must not waste chrome).
        /// </summary>
        public const float GridLabelLineBoxMul = 1.12f;
        /// <summary>Total vertical pad (top+bottom) as fraction of resolved primary font size.</summary>
        public const float GridLabelStripVPadMul = 0.30f;
        /// <summary>
        /// Secondary band maxY / primary minY (0..1 from bottom).
        /// Primary occupies (frac..1) — keep ≥0.55 share so leaf hierarchy matches larger font.
        /// </summary>
        public const float GridLabelPrimaryHeightFrac = 0.40f;
        /// <summary>Hide creator on primary row when caption inner width is below this (px, chrome-scaled).</summary>
        public const float GridLabelCreatorMinInnerW = 96f;
        /// <summary>Max fraction of inner width reserved for creator before leaf truncation.</summary>
        public const float GridLabelCreatorMaxFrac = 0.42f;
        /// <summary>If leaf would keep less than this fraction of inner width with creator, hide creator.</summary>
        public const float GridLabelLeafMinFracWithCreator = 0.45f;
        public const int GridBadgeFontRef = FontRef;
        public const int GridLabelFontRef = FontRef;
        public const int GridLabelSecondaryFontRef = FontCaptionRef;
        public const float GridCellOverlayMin = 0.45f;
        public const float GridCellOverlayMax = 2.5f;

        // Import sidebar (matches side tab column family)
        public const float ImportSidebarWidthRef = 220f;
        public const float ImportSidebarHeaderHeightRef = ControlSlotHeightRef;
        public const float ImportSidebarApplyHeightRef = ButtonSizeRef;
        /// <summary>Pinned reason line above Apply when import is blocked.</summary>
        public const float ImportSidebarApplyReasonHeightRef = 18f;
        public const float ImportSidebarSideMarginRef = BandPadRef;
        public const float ImportSidebarTopRowRef = 65f;
        public const float ImportSidebarScrollBarWidthRef = 10f;
        public const float ImportSidebarInnerPadHRef = SideTabRowPadRef;
        public const float ImportSidebarLabelPadLeftRef = ImportSidebarInnerPadHRef + 8f;
        public const float ImportSidebarLabelPadRightRef = TightGapRef;
        public const float ImportSidebarHeaderGapRef = SideTabRowSpacingRef;
        public const float ImportSidebarRowSpacingRef = SideTabRowSpacingRef;
        public const float ImportSidebarRowHeightRef = ButtonSizeRef;
        public const int ImportSidebarFontRef = FontRef;
        public const int ImportSidebarFontMin = FontMinRef;
        /// <summary>Floating Scene Import window defaults / clamps (design px at scale 1).</summary>
        public const float ImportSidebarFloatDefaultWidthRef = 360f;
        public const float ImportSidebarFloatDefaultHeightRef = 560f;
        public const float ImportSidebarFloatMinWidthRef = 220f;
        public const float ImportSidebarFloatMinHeightRef = 320f;
        /// <summary>Soft floor for max size; live max also grows to float-host rect (low DPI / UI scale).</summary>
        public const float ImportSidebarFloatMaxWidthRef = 900f;
        public const float ImportSidebarFloatMaxHeightRef = 1600f;
        /// <summary>Hard ceiling in design px (corrupt prefs / bad host read).</summary>
        public const float ImportSidebarFloatAbsoluteMaxWidthRef = 4000f;
        public const float ImportSidebarFloatAbsoluteMaxHeightRef = 4000f;
        /// <summary>Inset from float host edges when computing max size.</summary>
        public const float ImportSidebarFloatHostMarginRef = SectionGapRef;

        // In-app help panel
        public const float InAppHelpPanelWidthRef = 460f;
        public const float InAppHelpHeaderHeightRef = 44f;
        public const float InAppHelpSearchHeightRef = 40f;
        public const float InAppHelpNavBtnHeightRef = ButtonSizeRef;
        public const float InAppHelpBodyLineSpacingRef = HairGapRef;
        public const float InAppHelpIconPreviewDockSizeRef = 82f;
        public const float InAppHelpIconPreviewGlyphSizeRef = 64f;
        public const float InAppHelpScaleFloor = 0.85f;
        public const int InAppHelpHeaderFontRef = FontTitleRef;
        public const int InAppHelpNavFontRef = FontBodyRef;
        public const int InAppHelpSearchFontRef = FontBodyRef;
        public const int InAppHelpSectionTitleFontRef = FontTitleRef;
        public const int InAppHelpBodyFontRef = FontBodyRef;
        public const int InAppHelpBodyFontMin = FontMinRef;

        // Popup / dropdown menus
        public const float PopupMenuPaddingRef = BandPadRef;
        public const float PopupMenuRowSpacingRef = ControlRowGapRef;
        public const float PopupMenuRowHeightRef = ControlSlotHeightRef;
        public const float PopupMenuRowHeightCompactRef = ButtonSizeRef;
        public const float PopupMenuRowTextPadXRef = ControlGapRef;
        public const float PopupMenuRowIconSizeRef = 22f;
        public const float PopupMenuRowIconGapRef = ControlGapRef;
        public const int PopupMenuRowFontRef = FontBodyRef;
        public const int PopupMenuRowFontLargeRef = FontTitleRef;
        public const int PopupMenuOverflowFontRef = FontBodyRef;
        public const float PopupMenuAnchorGapRef = HairGapRef;
        public const float PopupMenuPanelWidthRef = 230f;
        /// <summary>Filter-presets dropdown: search + Float chip + sort need wider panel than plain popup rows.</summary>
        public const float QuickFiltersPanelWidthRef = 300f;
        /// <summary>Filter-presets list scrollbar — match dense secondary panels (import sidebar), not fat side-tab track.</summary>
        public const float QuickFiltersScrollBarWidthRef = 10f;
        /// <summary>
        /// VR float move: panel geometric center may travel this × parent half-extent from host origin.
        /// Canvas is ~1200×800 — values near 1.0 stop at the frame; VR needs ~3–4× past that.
        /// </summary>
        public const float FloatVrTravelParentFraction = 3.5f;
        /// <summary>
        /// VR float move: hard cap on travel (host-local px) when parent rect is huge.
        /// </summary>
        public const float FloatVrMaxTravelRef = 10000f;
        /// <summary>
        /// VR float move: fallback travel when parent rect has no usable size.
        /// </summary>
        public const float FloatVrFallbackTravelRef = 4000f;

        /// <summary>Floating filter-presets window defaults / clamps (design px at scale 1).</summary>
        public const float QuickFiltersFloatDefaultHeightRef = 420f;
        /// <summary>
        /// Fits footer icon row (Dock/Undo/Redo/Remove/resize + pad/gaps); soft-delete Undo may appear.
        /// Text footer labels overflowed this and caused HLG squeeze / reopen drift.
        /// </summary>
        public const float QuickFiltersFloatMinWidthRef = 240f;
        /// <summary>Title + search header + footer + one row — room for float chrome.</summary>
        public const float QuickFiltersFloatMinHeightRef = 260f;
        public const float QuickFiltersFloatMaxWidthRef = 640f;
        public const float QuickFiltersFloatMaxHeightRef = 900f;
        /// <summary>Fits <see cref="ButtonSizeRef"/> collapse/close chips + pad (Jakob with gallery chrome).</summary>
        public const float QuickFiltersTitleBarHeightRef = ButtonSizeRef + ControlGapRef;
        /// <summary>Float footer: undo/redo + resize grip row (merge lives on actions row).</summary>
        public const float QuickFiltersFooterHeightRef = ButtonSizeRef + ControlGapRef;
        /// <summary>Max leaf presets selectable when merging into one multi-random preset.</summary>
        public const int QuickFiltersMergeMaxMembers = 6;
        /// <summary>Settings floating window defaults / clamps (design px at scale 1). Wider default fits category sidebar + rows.</summary>
        public const float SettingsFloatDefaultWidthRef = 680f;
        public const float SettingsFloatDefaultHeightRef = 640f;
        public const float SettingsFloatMinWidthRef = 480f;
        public const float SettingsFloatMinHeightRef = 320f;
        public const float SettingsFloatMaxWidthRef = 1100f;
        public const float SettingsFloatMaxHeightRef = 1200f;
        /// <summary>Left category list width (property-sheet nav).</summary>
        public const float SettingsFloatSidebarWidthRef = 168f;
        /// <summary>Section header rows inside a category (SubGroupKey chunks).</summary>
        public const float SettingsFloatSectionHeaderHeightRef = 24f;
        /// <summary>Non-default marker at the left of a setting label.</summary>
        public const float SettingsFloatModifiedDotSizeRef = 6f;
        /// <summary>Fits <see cref="ButtonSizeRef"/> control chips + row pad (was 80 — too tall for float).</summary>
        public const float SettingsFloatRowHeightRef = 48f;
        /// <summary>Multi-line TextArea rows — host prefers 72 + chrome pad.</summary>
        public const float SettingsFloatTextAreaRowHeightRef = 112f;
        /// <summary>Wheel step (UGUI). 25 jumped ~2 viewports on large Win deltas.</summary>
        public const float SettingsFloatScrollSensitivityRef = 8f;
        /// <summary>Creator Strip Scene keep selector float — same chrome family as Settings/Plugins.</summary>
        public const float StripKeepFloatDefaultWidthRef = 560f;
        public const float StripKeepFloatDefaultHeightRef = 420f;
        public const float StripKeepFloatMinWidthRef = 420f;
        public const float StripKeepFloatMinHeightRef = 300f;
        public const float StripKeepFloatMaxWidthRef = 1200f;
        public const float StripKeepFloatMaxHeightRef = 1000f;
        public const float StripKeepFloatFooterCancelBtnWRef = 96f;
        public const float StripKeepFloatFooterConfirmBtnWRef = 120f;
        public const float StripKeepFloatScrollBarWidthRef = 14f;
        /// <summary>Plugins float tree palette (creator→package→cs) — dense power-user browse + drag.</summary>
        public const float PluginsFloatDefaultWidthRef = 460f;
        public const float PluginsFloatDefaultHeightRef = 560f;
        public const float PluginsFloatMinWidthRef = 320f;
        public const float PluginsFloatMinHeightRef = 280f;
        public const float PluginsFloatMaxWidthRef = 800f;
        public const float PluginsFloatMaxHeightRef = 1100f;
        public const float PluginsFloatRowHeightRef = 36f;
        public const float PluginsFloatExpandWidthRef = PluginsFloatRowHeightRef;
        public const float PluginsFloatChildIndentRef = GroupGapRef;
        /// <summary>Fixed version column width (vN) — recognition over recall.</summary>
        public const float PluginsFloatVersionWidthRef = 44f;
        /// <summary>Gap between version label and ★ so digits do not crowd the star.</summary>
        public const float PluginsFloatVersionStarGapRef = ControlGapRef;
        /// <summary>Options strip under search (latest-version filter).</summary>
        /// <summary>Two filter toggles side-by-side under search.</summary>
        public const float PluginsFloatOptionsRowHeightRef = 30f;
        /// <summary>Min scrollbar handle height so huge lists stay grab-able.</summary>
        public const float PluginsFloatScrollbarMinHandleRef = 32f;
        /// <summary>Quick-menu assignable-action palette (HUD). Smaller than Settings — sits beside 4×4 grid.</summary>
        public const float QmAssignFloatDefaultWidthRef = 360f;
        public const float QmAssignFloatDefaultHeightRef = 480f;
        public const float QmAssignFloatMinWidthRef = 280f;
        public const float QmAssignFloatMinHeightRef = 300f;
        public const float QmAssignFloatMaxWidthRef = 560f;
        public const float QmAssignFloatMaxHeightRef = 800f;
        public const float QmAssignFloatRowHeightRef = 36f;
        public const float OverflowMenuPanelWidthRef = 300f;
        public const float FileSortMenuPanelWidthRef = 248f;
        public const float SidePaneSortMenuPanelWidthRef = 228f;
        public const float TitleCreatorDropdownWidthRef = 330f;
        public const float TitleCreatorDropdownHeightRef = 500f;
        public const float TitleCreatorDropdownSearchWidthRef = 310f;

        // Modal scrim
        /// <summary>Opacity of the black click-to-dismiss dim behind centered modal panels.</summary>
        public const float ModalDimAlpha = 0.72f;

        // Spring scroll drag button (on main grid scrollbar)
        public const float SpringScrollBtnWidthFixedRef = 50f;
        /// <summary>Floating/VR: half prior width — dense Fitts hit beside track, not oversized chrome.</summary>
        public const float SpringScrollBtnWidthFloatRef = 50f;
        public const float SpringScrollBtnAspectRef = GoldenRatio;
        public const float SpringScrollBtnIconInsetRef = 24f;
        /// <summary>Floating/VR only: nudge left of scrollbar center so control sits beside track (px at ref scale).</summary>
        public const float SpringScrollBtnOffsetXFloatRef = -16f;

        // Toolbox action buttons — sized to match the side-rail / title-chip family
        // (uniform with TboxPinBtnSizeRef) and scaled proportionally inside the info row.
        public const float TboxActionButtonSizeRef = ButtonSizeRef;

        // In-app help close (legacy name kept; sized with action buttons)
        public const float TboxPinBtnSizeRef = ButtonSizeRef;
        public const float InAppHelpCloseBtnSizeRef = ButtonSizeRef;
        public const float InAppHelpCloseBtnRightInsetRef = TightGapRef;
        public const float InAppHelpCloseBtnLeftInsetRef = 40f;

        // Footer info / hover path tooltip row
        public const int FooterHoverPathFontRef = FontCaptionRef;
        public const int FooterInfoLabelFontRef = FontCaptionRef;
        public const int SettingsListRowNameFontRef = FontBodyRef;
        public const int SettingsListRowDetailFontRef = FontCaptionRef;

        // Layout anchors used throughout chrome math.
        // Content top reserve must equal the title bar height so grid/chips/side tabs clear it
        // exactly (no overlap, no gap) regardless of scale.
        public const float SideTabTopOffsetRef = TitleBarHeightRef;
        public const float SideTabSplitSeamRef = TightGapRef;
        /// <summary>Bottom sub-pane share in category split view (golden minor; top gets major remainder).</summary>
        public const float CategorySideSubPaneHeightFraction = GoldenRatioMinor;
        /// <summary>Min usable height for subcategory/tags pane when InfoBar grows.</summary>
        public const float SideTabSubPaneMinHeightRef = 110f;
        /// <summary>Min usable height for upper category pane when split is raised for tall InfoBar.</summary>
        public const float SideTabMainPaneMinHeightRef = 90f;
        public const float SideTabScrollBottomPadRef = BandPadRef;
        public const float GalleryMainBottomFallbackRef = 120f;
    }
}
