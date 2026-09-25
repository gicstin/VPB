namespace VPB
{
    public readonly struct GalleryModalTypography
    {
        public readonly int Prose;
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
