namespace VPB
{
    /// <summary>Design-reference pixel sizes at gallery UI scale 1.0 (before host/DPI factors).</summary>
    public static class GalleryUiDesignTokens
    {
        public const float VamUiScaleDesignBaseline = 1.5f;
        public const float VamMonitorUiScaleDesignBaseline = 1.0f;

        // Golden ratio, for aspect ratios and major splits only.
        public const float GoldenRatio = 1.618f;
        public const float GoldenRatioMajor = 1f / GoldenRatio;
        public const float GoldenRatioMinor = 1f - GoldenRatioMajor;

        public const int FontRef = 16;
        public const int FontBodyRef = FontRef;
        public const int FontTitleRef = 18;
        public const int FontCaptionRef = 13;
        /// <summary>Minimum readable fontSize after int clamp (ApplyFont floor).</summary>
        public const int FontMinRef = 10;
        /// <summary>Icon-only text: fontSize ≈ control height × this factor × scale.</summary>
        public const float GlyphFontHeightFactor = 0.55f;
        public const float ButtonSizeRef = FontBodyRef * 2;

        public const float Space1Ref = 2f;
        public const float Space2Ref = 4f;
        public const float Space3Ref = 8f;
        public const float Space4Ref = 12f;
        public const float Space5Ref = 16f;
        public const float Space6Ref = 24f;

        public const float HairGapRef = Space1Ref;
        public const float TightGapRef = Space2Ref;
        public const float ControlGapRef = Space3Ref;
        public const float GroupGapRef = Space4Ref;
        public const float RegionGapRef = Space5Ref;
        public const float SectionGapRef = Space6Ref;

        public const float ControlRimThicknessRef = Space1Ref;
        public const float ControlRimGutterRef = ControlRimThicknessRef;
        public const float ControlSlotHeightRef = ButtonSizeRef + ControlRimGutterRef * 2f;
        public const float BandPadRef = ControlGapRef;
        public const float BandPadHRef = BandPadRef;
        public const float BandPadVRef = TightGapRef;
        public const float BandContentInsetVRef = BandPadVRef + ControlRimGutterRef;
        public const float ControlRowGapRef = TightGapRef;
        public const float DialogPadRef = RegionGapRef;
        public const float FloatChromePadHRef = ControlGapRef;
        public const float FloatChromePadVRef = TightGapRef;
        /// <summary>Rounded-corner radius for gallery buttons + their hover border, as a fraction of the control's shorter side (0..0.5).</summary>
        public const float ButtonCornerRadiusFraction = 0.22f;

        public const float TitleBarHeightRef = ButtonSizeRef + BandPadVRef * 2f;
        public const float TitleBarChipRef = ButtonSizeRef;
        public const float TitleBarCategoryRowHeightRef = 36f;
        public const float TitleBarTitleLeftInsetRef = 60f;
        public const int TitleFontRef = FontTitleRef;
        public const int FpsFontRef = FontCaptionRef;
        public const int StatusBarFontRef = FontCaptionRef;
        /// <summary>Shared hide grace for gallery info-bar + quick-menu assignable tips.</summary>
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

        public const float SearchFieldHeightRef = 35f;
        public const int SearchFieldFontRef = FontRef;
        public const float SearchIconSizeRef = 24f;
        public const float SearchIconLeftPadRef = TightGapRef;
        public const float SearchTextLeftInsetRef = SearchIconLeftPadRef + SearchIconSizeRef + ControlGapRef;
        public const float SearchClearBtnSizeRef = ButtonSizeRef;
        public const float SearchTextRightInsetRef = SearchClearBtnSizeRef;
        public const int SearchClearFontRef = FontRef;
        public const float SearchClearBtnRightInsetRef = 0f;
        public const float SearchIconButtonPadRef = TightGapRef;
        public const float FloatSearchRowPadRef = BandPadRef;
        public const float FloatSearchRowHeightRef = SearchFieldHeightRef + FloatSearchRowPadRef * 2f;
        public const float FloatTitleWindowIconSizeRef = 22f;
        public const float PersonGenderBadgeRef = ButtonSizeRef;
        /// <summary>Space after window icon before title label (design px at scale 1).</summary>
        public const float FloatTitleWindowIconGapRef = TightGapRef;
        public const float FloatTitleBarPadHRef = TightGapRef;
        public const float FloatTitleBarPadVRef = TightGapRef;
        public const float FloatTitleBarSpacingRef = HairGapRef;
        /// <summary>Drag-grip column width before window icon.</summary>
        public const float FloatTitleGripWidthRef = 12f;
        public const float FloatChromeIconPadRef = TightGapRef;
        public const float TreeRowExpandIconPadRef = TightGapRef;

        public const float ResizeHandleFixedHitRef = 40f;
        public const float ResizeHandleCornerHitRef = 40f;
        public const float ResizeHandleEdgeMarginRef = ControlGapRef;
        public const float ResizeHandleFooterCenterYRef = FooterBarHeightRef * 0.5f;
        public const float ResizeHandleTitleCenterYRef = TitleBarHeightRef * 0.5f;
        public const float ResizeHandleLegacyHitRef = 30f;

        public const int FooterPerfToggleFontRef = FontRef;
        public const int FooterPerfStepFontRef = FontRef;

        public const float SideTabColumnWidthRef = 220f;
        public const float SideTabSideMarginRef = BandPadRef;
        public const float SideTabOpenGridInsetRef = SideTabColumnWidthRef + BandPadRef * 2f;
        public const float SideTabClosedGridInsetRef = 0f;
        public const float SideTabRowHeightRef = ButtonSizeRef;
        public const float CreatorRatingBadgeSizeRef = ButtonSizeRef - ControlGapRef;
        public const float SideTabControlGapRef = ControlGapRef;
        public const float SideTabRefreshBtnWidthRef = ButtonSizeRef;
        public const float SideTabMainSearchSortReserveRef =
            BandPadRef + SideTabRowHeightRef + ControlGapRef + BandPadRef;
        public const float SideTabRowSpacingRef = ControlRowGapRef;
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

        public const float FooterBarHeightRef = ButtonSizeRef + BandPadVRef * 2f;
        public const float FooterInfoRowHeightRef = ControlSlotHeightRef;
        public const float FooterToolboxTopRef = FooterBarHeightRef + FooterInfoRowHeightRef;
        public const float ModeSemanticsBannerHeightRef = 34f;
        public const float ModeSemanticsBannerGapRef = TightGapRef;
        public const int ContextBarMaxFilterChipRows = 1;
        public const float FooterDetailStripMinHeightRef = 80f;
        public const float FooterDetailStripHeightRef = 400f;
        public const float FooterDetailStripLineHeightRef = 18f;
        public const float FooterDetailStripHitHeightRef = 22f;
        public const float FooterDetailStripBandGapRef = HairGapRef;
        /// <summary>Detail-strip thumb edge cap (square); must stay ≥ strip max so preview stays flush.</summary>
        public const float FooterDetailStripThumbMaxRef = FooterDetailStripHeightRef;
        public const float FooterDetailStripThumbNavBtnRef = ButtonSizeRef;
        public const float FooterDetailStripThumbNavInsetRef = TightGapRef;
        public const float FooterDetailStripThumbScrubIndexHRef = ButtonSizeRef;
        public const float FooterDetailStripResizeGripRef = 14f;
        public const float FooterDetailStripResizePillWRef = 56f;
        public const float FooterDetailStripResizePillHRef = 5f;
        public const float FooterDetailStripStackSideMinHeightRef = 220f;
        public const float FooterDetailStripStackSideHysteresisRef = 96f;
        public const int FooterDetailStripLeftDescMaxLines = 5;
        /// <summary>Min strip width before right info column (desc + native tags) opens.</summary>
        public const float FooterDetailStripSideMinWidthRef = 600f;
        public const float FooterDetailStripSideHysteresisRef = 64f;
        public const float FooterDetailStripSideMinColWidthRef = 180f;
        public const float FooterDetailStripSideMaxColWidthRef = 340f;
        /// <summary>Left text column must keep at least this width when side is open.</summary>
        public const float FooterDetailStripSideLeftReserveRef = 240f;
        public const float FooterDetailStripSideScrollBarWidthRef = 8f;
        public const int FooterDetailStripSideTagsMaxLines = 2;
        public const float FilterChipRowHeightRef = ButtonSizeRef;
        public const float FilterChipRowMarginRef = TightGapRef;
        public const float FilterChipDismissSizeRef = ButtonSizeRef - TightGapRef;
        public const float FilterChipLabelDismissGapRef = TightGapRef;

        public const float SideButtonWidthRef = 120f;
        public const float SideButtonHeightRef = ButtonSizeRef;
        public const float SideButtonSquareRef = ButtonSizeRef;
        public const float SideButtonIconPadRef = SearchIconButtonPadRef;
        public const float SideButtonContainerWidthRef = 130f;
        public const float SideButtonContainerOffsetRef = 140f;
        public const float SideButtonSpacingRef = ButtonSizeRef + TightGapRef;
        public const float SideButtonGroupGapRef = GroupGapRef;
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

        public const float GridCellRefSize = 100f;
        public const float GridBadgeSizeRef = 32f;
        public const float GridLabelHeightRef = 22f;
        public const float GridLabelLineBoxMul = 1.12f;
        public const float GridLabelStripVPadMul = 0.30f;
        public const float GridLabelPrimaryHeightFrac = 0.40f;
        public const float GridLabelCreatorMinInnerW = 128f;
        /// <summary>Max fraction of inner width reserved for creator before leaf truncation.</summary>
        public const float GridLabelCreatorMaxFrac = 0.30f;
        public const float GridLabelLeafMinFracWithCreator = 0.58f;
        public const float GridLabelCreatorDominanceFrac = 0.80f;
        public const float GridLabelCreatorDominanceReleaseFrac = 0.60f;
        public const int GridLabelCreatorDominanceMinSample = 6;
        public const int GridBadgeFontRef = FontRef;
        public const int GridLabelFontRef = FontRef;
        public const int GridLabelSecondaryFontRef = FontCaptionRef;
        public const float GridCellOverlayMin = 0.45f;
        public const float GridCellOverlayMax = 2.5f;

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
        public const float ImportSidebarFloatDefaultWidthRef = 360f;
        public const float ImportSidebarFloatDefaultHeightRef = 560f;
        public const float ImportSidebarFloatMinWidthRef = 220f;
        public const float ImportSidebarFloatMinHeightRef = 320f;
        public const float ImportSidebarFloatMaxWidthRef = 900f;
        public const float ImportSidebarFloatMaxHeightRef = 1600f;
        public const float ImportSidebarFloatAbsoluteMaxWidthRef = 4000f;
        public const float ImportSidebarFloatAbsoluteMaxHeightRef = 4000f;
        public const float ImportSidebarFloatHostMarginRef = SectionGapRef;

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
        public const float QuickFiltersPanelWidthRef = 300f;
        public const float QuickFiltersScrollBarWidthRef = 10f;
        public const float FloatVrTravelParentFraction = 3.5f;
        public const float FloatVrMaxTravelRef = 10000f;
        public const float FloatVrFallbackTravelRef = 4000f;

        public const float QuickFiltersFloatDefaultHeightRef = 420f;
        public const float QuickFiltersFloatMinWidthRef = 240f;
        public const float QuickFiltersFloatMinHeightRef = 260f;
        public const float QuickFiltersFloatMaxWidthRef = 640f;
        public const float QuickFiltersFloatMaxHeightRef = 900f;
        public const float QuickFiltersTitleBarHeightRef = ButtonSizeRef + ControlGapRef;
        public const float QuickFiltersFooterHeightRef = ButtonSizeRef + ControlGapRef;
        public const int QuickFiltersMergeMaxMembers = 6;
        public const float SettingsFloatDefaultWidthRef = 680f;
        public const float SettingsFloatDefaultHeightRef = 640f;
        public const float SettingsFloatMinWidthRef = 480f;
        public const float SettingsFloatMinHeightRef = 320f;
        public const float SettingsFloatMaxWidthRef = 1100f;
        public const float SettingsFloatMaxHeightRef = 1200f;
        public const float SettingsFloatSidebarWidthRef = 168f;
        public const float SettingsFloatSectionHeaderHeightRef = 24f;
        public const float SettingsFloatModifiedDotSizeRef = 6f;
        public const float SettingsFloatRowHeightRef = 48f;
        public const float SettingsFloatTextAreaRowHeightRef = 112f;
        public const float SettingsFloatWrapRowHeightRef = 88f;
        public const float SettingsFloatScrollSensitivityRef = 8f;
        public const float StripKeepFloatDefaultWidthRef = 560f;
        public const float StripKeepFloatDefaultHeightRef = 420f;
        public const float StripKeepFloatMinWidthRef = 420f;
        public const float StripKeepFloatMinHeightRef = 300f;
        public const float StripKeepFloatMaxWidthRef = 1200f;
        public const float StripKeepFloatMaxHeightRef = 1000f;
        public const float StripKeepFloatFooterCancelBtnWRef = 96f;
        public const float StripKeepFloatFooterConfirmBtnWRef = 120f;
        public const float StripKeepFloatScrollBarWidthRef = 14f;
        public const float PluginsFloatDefaultWidthRef = 460f;
        public const float PluginsFloatDefaultHeightRef = 560f;
        public const float PluginsFloatMinWidthRef = 320f;
        public const float PluginsFloatMinHeightRef = 280f;
        public const float PluginsFloatMaxWidthRef = 800f;
        public const float PluginsFloatMaxHeightRef = 1100f;
        public const float PluginsFloatRowHeightRef = 36f;
        public const float PluginsFloatExpandWidthRef = PluginsFloatRowHeightRef;
        public const float PluginsFloatChildIndentRef = GroupGapRef;
        public const float PluginsFloatVersionWidthRef = 44f;
        /// <summary>Gap between version label and ★ so digits do not crowd the star.</summary>
        public const float PluginsFloatVersionStarGapRef = ControlGapRef;
        public const float PluginsFloatOptionsRowHeightRef = 30f;
        public const float PluginsFloatScrollbarMinHandleRef = 32f;
        public const float InsightsFloatDefaultWidthRef = 620f;
        public const float InsightsFloatDefaultHeightRef = 640f;
        public const float InsightsFloatMinWidthRef = 420f;
        public const float InsightsFloatMinHeightRef = 300f;
        public const float InsightsFloatMaxWidthRef = 1100f;
        public const float InsightsFloatMaxHeightRef = 1200f;
        public const float InsightsFloatTabRowHeightRef = 34f;
        public const float InsightsFloatProgressRowHeightRef = 30f;
        public const float InsightsFloatRowHeightRef = 34f;
        public const float OutlinerRailWidthRef = 320f;
        public const float OutlinerRailWideWidthRef = 400f;
        public const float OutlinerRailMinWidthRef = 280f;
        public const float OutlinerRailMaxWidthRef = 560f;
        public const float OutlinerRailEdgeHitRef = RegionGapRef;
        public const float OutlinerSliderHandleSizeRef = ButtonSizeRef;
        public const float OutlinerChromeBarHeightRef = QuickFiltersTitleBarHeightRef;
        public const float OutlinerTreeRowGapRef = TightGapRef;
        public const float OutlinerVrRowHeightRef = ControlSlotHeightRef + Space3Ref;
        public const float OutlinerFloatDefaultWidthRef = 980f;
        public const float OutlinerFloatDefaultHeightRef = 720f;
        public const float OutlinerFloatMinWidthRef = 720f;
        public const float OutlinerFloatMinHeightRef = 420f;
        public const float OutlinerFloatMaxWidthRef = 1600f;
        public const float OutlinerFloatMaxHeightRef = 1200f;
        public const float OutlinerSplitTreeShareRef = GoldenRatioMinor;
        public const float OutlinerLookTileWidthRef = 128f;
        public const float OutlinerLookTileCapRef = ButtonSizeRef;
        public const int OutlinerLookTileMax = 12;
        public const int OutlinerPoseRowMax = 12;
        public const float OutlinerPoseValueWidthRef = ButtonSizeRef * 1.5f;
        public const float OutlinerAxisNameWidthRef = ButtonSizeRef * 1.4f;
        public const float OutlinerNudgeButtonWidthRef = ButtonSizeRef;
        public const float OutlinerAxisIconSizeRef = ButtonSizeRef * 0.6f;
        public const float OutlinerSpringTrackMinWidthRef = ButtonSizeRef * 3f;
        public const float OutlinerAxisValueWidthRef = ButtonSizeRef * 2f;
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

        public const float ModalDimAlpha = 0.72f;

        public const float SpringScrollBtnWidthFixedRef = 50f;
        public const float SpringScrollBtnWidthFloatRef = 50f;
        public const float SpringScrollBtnAspectRef = GoldenRatio;
        public const float SpringScrollBtnIconInsetRef = 24f;
        /// <summary>Floating/VR only: nudge left of scrollbar center so control sits beside track (px at ref scale).</summary>
        public const float SpringScrollBtnOffsetXFloatRef = -16f;

        public const float TboxActionButtonSizeRef = ButtonSizeRef;

        public const float TboxPinBtnSizeRef = ButtonSizeRef;
        public const float InAppHelpCloseBtnSizeRef = ButtonSizeRef;
        public const float InAppHelpCloseBtnRightInsetRef = TightGapRef;
        public const float InAppHelpCloseBtnLeftInsetRef = 40f;

        public const int FooterHoverPathFontRef = FontCaptionRef;
        public const int FooterInfoLabelFontRef = FontCaptionRef;
        public const int SettingsListRowNameFontRef = FontBodyRef;
        public const int SettingsListRowDetailFontRef = FontCaptionRef;

        public const float SideTabTopOffsetRef = TitleBarHeightRef;
        public const float SideTabSplitSeamRef = TightGapRef;
        public const float CategorySideSubPaneHeightFraction = GoldenRatioMinor;
        public const float SideTabSubPaneMinHeightRef = 110f;
        public const float SideTabMainPaneMinHeightRef = 90f;
        public const float SideTabScrollBottomPadRef = BandPadRef;
        public const float GalleryMainBottomFallbackRef = 120f;
    }
}
