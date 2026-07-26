namespace VPB
{
    /// <summary>Resolved modal prose size (single gallery font tier).</summary>
    public readonly struct GalleryModalTypography
    {
        /// <summary>Scaled prose fontSize — use with <see cref="GalleryUiMetrics.ApplyEmphasisTitle"/> for headers.</summary>
        public readonly int Prose;
        public readonly int Title;
        public readonly int Body;
        public readonly int Caption;

        public GalleryModalTypography(float chromeScale)
        {
            float s = chromeScale <= 0f ? 1f : chromeScale;
            Prose = GalleryUiMetrics.ScaledFontSize(GalleryUiDesignTokens.FontRef, s, GalleryUiDesignTokens.FontMinRef);
            Title = Prose;
            Body = Prose;
            Caption = Prose;
        }

        public static GalleryModalTypography FromPanel(GalleryPanel panel)
            => new GalleryModalTypography(GalleryUiMetrics.ForPanel(panel).ChromeScale);
    }
}
