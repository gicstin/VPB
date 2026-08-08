namespace VPB
{
    /// <summary>Resolved modal prose sizes (title / body / caption hierarchy).</summary>
    public readonly struct GalleryModalTypography
    {
        /// <summary>Legacy alias for <see cref="Body"/>.</summary>
        public readonly int Prose;
        /// <summary>Scaled title — use with <see cref="GalleryUiMetrics.ApplyEmphasisTitle"/> for headers.</summary>
        public readonly int Title;
        public readonly int Body;
        public readonly int Caption;

        public GalleryModalTypography(float chromeScale)
        {
            float s = chromeScale <= 0f ? 1f : chromeScale;
            Title = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontTitleRef, s, GalleryUiDesignTokens.FontMinRef);
            Body = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontBodyRef, s, GalleryUiDesignTokens.FontMinRef);
            Caption = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontCaptionRef, s, GalleryUiDesignTokens.FontMinRef);
            Prose = Body;
        }

        public static GalleryModalTypography FromPanel(GalleryPanel panel)
            => new GalleryModalTypography(GalleryUiMetrics.ForPanel(panel).ChromeScale);
    }
}
