namespace VPB
{
    /// <summary>Design-reference pixel sizes at gallery UI scale 1.0 (before host/DPI factors).</summary>
    public static class GalleryUiDesignTokens
    {
        public const float VamUiScaleDesignBaseline = 1.5f;

        // ── Golden ratio (major splits + decorative aspects only; not spacing/type) ──
        /// <summary>φ ≈ 1.618 — aspect ratios and scale accents.</summary>
        public const float GoldenRatio = 1.618f;
        /// <summary>1/φ ≈ 0.618 — major share of a unit split (e.g. desktop dock width).</summary>
        public const float GoldenRatioMajor = 1f / GoldenRatio;
        /// <summary>1/φ² ≈ 0.382 — minor share of a unit split (e.g. category side sub-pane).</summary>
        public const float GoldenRatioMinor = 1f - GoldenRatioMajor;

        // ── Typography (single prose size + glyph sizing) ─────────────────────
        /// <summary>All gallery chrome prose. Hierarchy = color / alpha, not weight or fontSize (all text is non-bold).</summary>
        public const int FontRef = 16;
        public const int FontBodyRef = FontRef;
        /// <summary>Same as <see cref="FontRef"/> — emphasis comes from color/alpha, not bold weight.</summary>
        public const int FontTitleRef = FontRef;
        /// <summary>Same as <see cref="FontRef"/> — de-emphasis via reduced alpha on the Text color.</summary>
        public const int FontCaptionRef = FontRef;
        /// <summary>Minimum readable fontSize after int clamp (ApplyFont floor).</summary>
        public const int FontMinRef = 10;
        /// <summary>Icon-only text: fontSize ≈ control height × this factor × scale.</summary>
        public const float GlyphFontHeightFactor = 0.55f;
        /// <summary>All interactive buttons: 2× FontRef. Ratio is global and intentional.</summary>
        public const float ButtonSizeRef = FontRef * 2; // 32
        /// <summary>Rounded-corner radius for gallery buttons + their hover border, as a fraction of the
        /// control's shorter side (0..0.5). Default when no user override is stored in VPB.cfg.
        /// Live value comes from <see cref="VPBConfig.GalleryElementCornerRadiusFraction"/> /
        /// <see cref="VPBConfig.EnableGalleryElementRounding"/>.</summary>
        public const float ButtonCornerRadiusFraction = 0.22f;

        // Title bar
        public const float TitleBarHeightRef = 48f;
        public const float TitleBarChipRef = ButtonSizeRef;
        public const float TitleBarCategoryRowHeightRef = 36f;
        public const float TitleBarTitleLeftInsetRef = 60f;
        public const int TitleFontRef = FontRef;
        public const int FpsFontRef = FontRef;
        public const int StatusBarFontRef = FontRef;
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
        public const float SearchIconLeftPadRef = 6f;
        public const float SearchTextLeftInsetRef = 38f;
        public const float SearchClearBtnSizeRef = ButtonSizeRef;
        public const float SearchTextRightInsetRef = SearchClearBtnSizeRef;
        public const int SearchClearFontRef = FontRef;
        public const float SearchClearBtnRightInsetRef = 0f;
        public const float SearchIconButtonPadRef = 4f;

        // Resize handles — sized/positioned like the corner-most bar button (40px, seated in the bar).
        // Horizontal centre inset = EdgeMargin + half button (mirrors the footer's 10px right padding).
        // Vertical centre inset = the bar's half-height so the handle lines up with the bar buttons.
        public const float ResizeHandleFixedHitRef = 40f;
        public const float ResizeHandleCornerHitRef = 40f;
        public const float ResizeHandleEdgeMarginRef = 10f;     // horizontal gap from the panel side
        public const float ResizeHandleFooterCenterYRef = 20f;  // footer bar half-height (bottom corners)
        public const float ResizeHandleTitleCenterYRef = 24f;   // title bar half-height (top corners)
        public const float ResizeHandleLegacyHitRef = 30f;

        // Footer perf controls
        public const int FooterPerfToggleFontRef = FontRef;
        public const int FooterPerfStepFontRef = FontRef;

        // Side tab column
        public const float SideTabColumnWidthRef = 220f;
        public const float SideTabSideMarginRef = 10f;
        public const float SideTabOpenGridInsetRef = 230f;
        public const float SideTabClosedGridInsetRef = 0f;
        public const float SideTabRowHeightRef = ButtonSizeRef;
        public const float SideTabControlGapRef = 5f;
        public const float SideTabRefreshBtnWidthRef = ButtonSizeRef;
        /// <summary>Sort column + left margin reserved from tab width for main side search (10 + 35).</summary>
        public const float SideTabMainSearchSortReserveRef = 45f;
        public const float SideTabRowSpacingRef = 4f;
        /// <summary>Gap below sort+search row before tab list; matches collapse-header gap (4px).</summary>
        public const float SideTabFilterRowBottomGapRef = SideTabRowSpacingRef;
        /// <summary>Gap below split seam before lower-pane sort+search row.</summary>
        public const float SideTabSubFilterRowTopGapRef = 10f;
        public const float SideTabRowPadRef = 5f;
        public const float SideTabScrollBarWidthRef = 15f;
        public const int TabButtonFontRef = FontRef;
        public const int TabButtonFontMin = FontMinRef;
        public const float TabButtonMinWidthRef = 140f;
        public const float TabButtonPreferredWidthRef = 170f;

        // Footer / filter chrome
        public const float FooterBarHeightRef = 44f;
        public const float FooterInfoRowHeightRef = 36f;
        public const float FooterToolboxTopRef = 80f;
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
        public const float FooterDetailStripBandGapRef = 1f;
        /// <summary>Detail-strip thumb edge cap (square); must stay ≥ strip max so preview stays flush.</summary>
        public const float FooterDetailStripThumbMaxRef = FooterDetailStripHeightRef;
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
        public const float FilterChipRowMarginRef = 6f;
        public const float FilterChipDismissSizeRef = ButtonSizeRef - 4f;
        public const float FilterChipLabelDismissGapRef = 6f;

        // Side rail buttons (settings, follow, save, etc.)
        public const float SideButtonWidthRef = 120f;
        public const float SideButtonHeightRef = ButtonSizeRef;
        public const float SideButtonSquareRef = ButtonSizeRef;
        public const float SideButtonContainerWidthRef = 130f;
        public const float SideButtonContainerOffsetRef = 140f;
        public const float SideButtonSpacingRef = 38f;
        /// <summary>Extra gap between Layout / Browse / Tools rail zones when EnableButtonGaps is on.</summary>
        public const float SideButtonGroupGapRef = 12f;
        /// <summary>gapTier multiplier at zone starts in <c>GetSideButtonsLayout</c>.</summary>
        public const int SideButtonZoneGapTier = 2;
        public const float SideButtonZoneSepHeightRef = 1f;
        public const float SideButtonZoneSepWidthRef = 20f;
        public const float SideButtonSubmenuWidthFactorRef = 1.6f;
        public const float SideButtonEdgeInsetRef = 6f;
        public const int SideButtonFontRef = FontRef;
        public const int SideButtonFontMin = FontMinRef;
        public const int SideButtonSubmenuFontRef = FontRef;
        public const float SideHoverStripWidthRef = 30f;
        public const float SideHoverStripOffsetRef = 35f;

        // Grid thumbnails (overlay scale derives from cell geometry, not user chrome scale)
        public const float GridCellRefSize = 100f;
        public const float GridBadgeSizeRef = 32f;
        public const float GridLabelHeightRef = 22f;
        public const int GridBadgeFontRef = FontRef;
        public const int GridLabelFontRef = FontRef;
        public const float GridCellOverlayMin = 0.45f;
        public const float GridCellOverlayMax = 2.5f;

        // Import sidebar (matches side tab column family)
        public const float ImportSidebarWidthRef = 220f;
        public const float ImportSidebarHeaderHeightRef = 32f;
        public const float ImportSidebarApplyHeightRef = ButtonSizeRef;
        public const float ImportSidebarSideMarginRef = 10f;
        public const float ImportSidebarTopRowRef = 65f;
        public const float ImportSidebarScrollBarWidthRef = 10f;
        public const float ImportSidebarInnerPadHRef = SideTabRowPadRef;
        public const float ImportSidebarLabelPadLeftRef = ImportSidebarInnerPadHRef + 8f;
        public const float ImportSidebarLabelPadRightRef = 4f;
        public const float ImportSidebarHeaderGapRef = SideTabRowSpacingRef;
        public const float ImportSidebarRowSpacingRef = SideTabRowSpacingRef;
        public const float ImportSidebarRowHeightRef = ButtonSizeRef;
        public const int ImportSidebarFontRef = FontRef;
        public const int ImportSidebarFontMin = FontMinRef;

        // In-app help panel
        public const float InAppHelpPanelWidthRef = 460f;
        public const float InAppHelpHeaderHeightRef = 44f;
        public const float InAppHelpSearchHeightRef = 40f;
        public const float InAppHelpNavBtnHeightRef = ButtonSizeRef;
        public const float InAppHelpBodyLineSpacingRef = 3f;
        public const float InAppHelpIconPreviewDockSizeRef = 82f;
        public const float InAppHelpIconPreviewGlyphSizeRef = 64f;
        public const float InAppHelpScaleFloor = 0.85f;
        public const int InAppHelpHeaderFontRef = FontRef;
        public const int InAppHelpNavFontRef = FontRef;
        public const int InAppHelpSearchFontRef = FontRef;
        public const int InAppHelpSectionTitleFontRef = FontRef;
        public const int InAppHelpBodyFontRef = FontRef;
        public const int InAppHelpBodyFontMin = FontMinRef;

        // Popup / dropdown menus
        public const float PopupMenuPaddingRef = 6f;
        public const float PopupMenuRowSpacingRef = 4f;
        public const float PopupMenuRowHeightRef = ButtonSizeRef;
        public const float PopupMenuRowHeightCompactRef = ButtonSizeRef;
        public const float PopupMenuRowTextPadXRef = 10f;
        public const float PopupMenuRowIconSizeRef = 22f;
        public const float PopupMenuRowIconGapRef = 8f;
        public const int PopupMenuRowFontRef = FontRef;
        public const int PopupMenuRowFontLargeRef = FontRef;
        public const int PopupMenuOverflowFontRef = FontRef;
        public const float PopupMenuAnchorGapRef = 2f;
        public const float PopupMenuPanelWidthRef = 230f;
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
        public const float SpringScrollBtnWidthFloatRef = 100f;
        public const float SpringScrollBtnAspectRef = GoldenRatio;
        public const float SpringScrollBtnIconInsetRef = 24f;

        // Toolbox action buttons — sized to match the side-rail / title-chip family
        // (uniform with TboxPinBtnSizeRef) and scaled proportionally inside the info row.
        public const float TboxActionButtonSizeRef = ButtonSizeRef;

        // Toolbox pin + in-app help close
        public const float TboxPinBtnSizeRef = ButtonSizeRef;
        public const float InAppHelpCloseBtnSizeRef = ButtonSizeRef;
        public const float InAppHelpCloseBtnRightInsetRef = 6f;
        public const float InAppHelpCloseBtnLeftInsetRef = 40f;

        // Footer info / hover path tooltip row
        public const int FooterHoverPathFontRef = FontRef;
        public const int FooterInfoLabelFontRef = FontRef;
        public const int SettingsListRowNameFontRef = FontRef;
        public const int SettingsListRowDetailFontRef = FontRef;

        // Layout anchors used throughout chrome math.
        // Content top reserve must equal the title bar height so grid/chips/side tabs clear it
        // exactly (no overlap, no gap) regardless of scale.
        public const float SideTabTopOffsetRef = TitleBarHeightRef;
        public const float SideTabSplitSeamRef = 5f;
        /// <summary>Bottom sub-pane share in category split view (golden minor; top gets major remainder).</summary>
        public const float CategorySideSubPaneHeightFraction = GoldenRatioMinor;
        /// <summary>Min usable height for subcategory/tags pane when InfoBar grows.</summary>
        public const float SideTabSubPaneMinHeightRef = 110f;
        /// <summary>Min usable height for upper category pane when split is raised for tall InfoBar.</summary>
        public const float SideTabMainPaneMinHeightRef = 90f;
        public const float SideTabScrollBottomPadRef = 8f;
        public const float GalleryMainBottomFallbackRef = 120f;
    }
}
