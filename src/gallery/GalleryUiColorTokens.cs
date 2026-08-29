using UnityEngine;

namespace VPB
{
    /// <summary>
    /// Gallery color system — dark homogeneous shell + selective high-contrast state.
    /// Neutral greys for chrome; status accents sparingly (Johnson edges; von Restorff
    /// one salient focus). Dark mode = this palette, not invert (<c>ui-ux-design</c>).
    /// </summary>
    public static class GalleryUiColorTokens
    {
        // ── Surfaces (quiet ladder; neutral grey — no cool/blue bias) ────────
        /// <summary>Deepest fill — modal / title search idle / strip footers.</summary>
        public static readonly Color SurfaceDeep = new Color(0.07f, 0.07f, 0.07f, 1f);
        /// <summary>Input wells, darkest chrome band.</summary>
        public static readonly Color SurfaceDarker = new Color(0.10f, 0.10f, 0.10f, 1f);
        /// <summary>Primary panel / title-adjacent chrome.</summary>
        public static readonly Color SurfaceDark = new Color(0.14f, 0.14f, 0.14f, 1f);
        /// <summary>Default button / row resting fill.</summary>
        public static readonly Color SurfacePanel = new Color(0.18f, 0.18f, 0.18f, 1f);
        /// <summary>Raised / mid chrome (idle toggles, cancel rows).</summary>
        public static readonly Color SurfaceMid = new Color(0.28f, 0.28f, 0.28f, 1f);
        /// <summary>Icon-button resting backdrop.</summary>
        public static readonly Color SurfaceIconBtn = new Color(0.24f, 0.24f, 0.24f, 1f);
        /// <summary>Side-tab / list row idle (was ColorInactiveRow).</summary>
        public static readonly Color RowIdle = new Color(0.22f, 0.22f, 0.22f, 1f);
        /// <summary>Zero-count / disabled path row fill.</summary>
        public static readonly Color RowZero = new Color(0.15f, 0.15f, 0.15f, 1f);
        /// <summary>Popup menu panel fill — opaque dark shell (same ladder as SurfaceDeep).</summary>
        public static readonly Color PopupSurface = SurfaceDeep;
        /// <summary>Popup row resting.</summary>
        public static readonly Color PopupRowIdle = new Color(0.18f, 0.18f, 0.18f, 1f);
        /// <summary>Popup row active / hovered.</summary>
        public static readonly Color PopupRowActive = new Color(0.34f, 0.34f, 0.34f, 1f);
        /// <summary>Centered modal panel body.</summary>
        public static readonly Color ModalSurface = new Color(0.06f, 0.06f, 0.06f, 1f);

        // ── Text (lifted secondary for dark-on-dark edges) ───────────────────
        public static readonly Color TextPrimary = new Color(0.94f, 0.94f, 0.94f, 1f);
        public static readonly Color TextMuted = new Color(0.78f, 0.78f, 0.78f, 1f);
        /// <summary>Secondary chrome — was ~0.55; too weak on SurfaceDark.</summary>
        public static readonly Color TextDim = new Color(0.68f, 0.68f, 0.68f, 1f);
        public static readonly Color TextZeroCount = new Color(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Color TextPlaceholder = new Color(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Color TextOnAccent = Color.white;
        public static readonly Color TextShadow = new Color(0f, 0f, 0f, 0.75f);

        // ── Universal active / chrome (floats + shared interactive) ─────────
        // Role split so Scene Import / presets stop painting every control the same green.
        // Selected vs idle must clear ~3:1 luminance (Johnson / WCAG 1.4.11), not a 0.12 grey step.
        /// <summary>Selected list row / armed choice — grey lift, still muted (not CTA).</summary>
        public static readonly Color ActiveSelected = new Color(0.36f, 0.36f, 0.36f, 1f);
        /// <summary>Toggle ON / multi-select armed — muted sage, distinct from Apply.</summary>
        public static readonly Color ActiveOn = new Color(0.28f, 0.38f, 0.32f, 1f);
        /// <summary>Keyboard / focus highlight — neutral lift above selected fill.</summary>
        public static readonly Color ActiveFocus = new Color(0.42f, 0.42f, 0.42f, 1f);
        /// <summary>Secondary bulk (Select All) — mid chrome, not neon.</summary>
        public static readonly Color ActiveSecondary = new Color(0.30f, 0.30f, 0.30f, 1f);
        /// <summary>Selected-but-blocked / paused — amber lift, not grey (change blindness vs idle).</summary>
        public static readonly Color ActivePaused = new Color(0.36f, 0.30f, 0.16f, 1f);
        /// <summary>Dirty / warn mark.</summary>
        public static readonly Color ActiveWarn = new Color(0.72f, 0.58f, 0.28f, 1f);
        /// <summary>Detach / Float chip.</summary>
        public static readonly Color ActiveFloatChip = new Color(0.26f, 0.26f, 0.26f, 1f);
        /// <summary>Sort / utility icon-button backdrop.</summary>
        public static readonly Color ActiveUtility = new Color(0.30f, 0.30f, 0.30f, 1f);
        /// <summary>Title-bar collapse/close well.</summary>
        public static readonly Color ChromeIconWell = new Color(0f, 0f, 0f, 0.5f);
        /// <summary>Search-field clear (X) glyph — shared by all float filters.</summary>
        public static readonly Color SearchClearIconTint = new Color(0.6f, 0.6f, 0.6f, 1f);
        /// <summary>Tree-row expand chevron (Plugins / Strip Keep).</summary>
        public static readonly Color TreeExpandIconTint = new Color(0.82f, 0.84f, 0.88f, 1f);
        /// <summary>Tree-row expand well when row can expand.</summary>
        public static readonly Color TreeExpandWell = new Color(0.16f, 0.17f, 0.20f, 1f);
        /// <summary>Tree-row expand well when leaf / empty (opaque placeholder).</summary>
        public static readonly Color TreeExpandWellEmpty = new Color(0.10f, 0.10f, 0.12f, 1f);
        /// <summary>Tree-row expand well when leaf / empty (no fill).</summary>
        public static readonly Color TreeExpandWellEmptyClear = new Color(0.10f, 0.10f, 0.12f, 0f);
        /// <summary>Magnifying-glass glyph in search fields.</summary>
        public static readonly Color SearchIconTint = new Color(0.55f, 0.55f, 0.55f, 1f);
        /// <summary>Window-type icon in float title bars.</summary>
        public static readonly Color TitleWindowIconTint = new Color(0.80f, 0.80f, 0.80f, 1f);
        /// <summary>Apply-reason / locked banner fill (quiet warn surface).</summary>
        public static readonly Color ActiveWarnSurface = new Color(0.16f, 0.14f, 0.10f, 0.95f);
        /// <summary>Apply-reason / locked banner header.</summary>
        public static readonly Color ActiveWarnHeader = new Color(0.28f, 0.24f, 0.16f, 1f);
        /// <summary>Apply-reason status text.</summary>
        public static readonly Color ActiveWarnText = new Color(0.90f, 0.78f, 0.48f, 1f);

        // ── Interactive accents (selected / confirm / destroy) ───────────────
        /// <summary>Gallery chip selected — muted cool-grey (not saturated blue).</summary>
        public static readonly Color AccentSelected = new Color(0.34f, 0.40f, 0.46f, 1f);
        /// <summary>Primary confirm CTA (Save / Apply) — reserved; do not reuse on rows.</summary>
        public static readonly Color AccentConfirm = new Color(0.26f, 0.46f, 0.32f, 1f);
        /// <summary>Session/ready status dot — warm bright green, not the muted confirm fill.</summary>
        public static readonly Color AccentLive = new Color(0.52f, 0.88f, 0.38f, 1f);
        /// <summary>Destructive fill — avoid dark-red-on-dark (Johnson).</summary>
        public static readonly Color AccentDanger = new Color(0.62f, 0.28f, 0.28f, 1f);
        public static readonly Color AccentDangerStrong = new Color(0.72f, 0.26f, 0.24f, 1f);
        public static readonly Color AccentNew = new Color(0.26f, 0.40f, 0.34f, 1f);
        public static readonly Color AccentFacetGeneric = new Color(0.36f, 0.36f, 0.40f, 1f);

        // Session rule fills. Muted: blocked is the default, not an error.
        public static readonly Color RuleBlocked = new Color(0.36f, 0.20f, 0.20f, 1f);
        public static readonly Color RuleAsk = new Color(0.36f, 0.30f, 0.16f, 1f);
        public static readonly Color RuleAllowed = new Color(0.22f, 0.36f, 0.28f, 1f);
        public static readonly Color RuleBlockedText = new Color(0.94f, 0.80f, 0.80f, 1f);
        public static readonly Color RuleAskText = new Color(0.95f, 0.87f, 0.66f, 1f);
        public static readonly Color RuleAllowedText = new Color(0.78f, 0.94f, 0.84f, 1f);

        // ── Sticky mode ambient (stronger than idle chrome — change blindness) ─
        public static readonly Color ModeToolBanner = new Color(0.20f, 0.28f, 0.34f, 0.97f);
        public static readonly Color ModeApplyStripe = new Color(0.90f, 0.72f, 0.40f, 1f);
        public static readonly Color ModeStatusTextTool = new Color(0.78f, 0.84f, 0.88f, 1f);
        public static readonly Color ModeStatusTextApply = new Color(0.92f, 0.82f, 0.55f, 1f);

        // ── Content-type / filter facet fills (idle accents on dark shell) ───
        // Slightly higher luminance than legacy so peers separate under squint test.
        public static readonly Color FacetCategory = new Color(0.62f, 0.28f, 0.28f, 1f);
        public static readonly Color FacetCreator = new Color(0.64f, 0.50f, 0.22f, 1f);
        public static readonly Color FacetTag = new Color(0.52f, 0.30f, 0.54f, 1f);
        public static readonly Color FacetRating = new Color(0.68f, 0.58f, 0.26f, 1f);
        public static readonly Color FacetUnrated = new Color(0.34f, 0.40f, 0.46f, 1f);
        public static readonly Color FacetUnratedLabel = new Color(0.72f, 0.78f, 0.84f, 1f);
        public static readonly Color FacetUnratedIcon = new Color(0.74f, 0.80f, 0.86f, 1f);
        public static readonly Color FacetSource = new Color(0.30f, 0.40f, 0.52f, 1f);
        public static readonly Color FacetSubfilter = new Color(0.36f, 0.36f, 0.42f, 1f);
        public static readonly Color FacetUserTag = new Color(0.54f, 0.32f, 0.54f, 1f);
        public static readonly Color FacetTitleSearchActive = new Color(0.32f, 0.36f, 0.40f, 1f);
        public static readonly Color FacetPath = new Color(0.28f, 0.30f, 0.40f, 1f);
        public static readonly Color FacetHistory = new Color(0.48f, 0.28f, 0.48f, 1f);
        public static readonly Color FacetHistoryAccent = new Color(0.54f, 0.34f, 0.54f, 1f);
        public static readonly Color FacetSceneImport = new Color(0.64f, 0.52f, 0.24f, 1f);
        public static readonly Color FacetHub = new Color(0.28f, 0.46f, 0.36f, 1f);
        /// <summary>MP session toggle — cyan so it does not hide in grey footer chrome.</summary>
        public static readonly Color FacetMultiplayer = new Color(0.16f, 0.40f, 0.48f, 1f);
        /// <summary>Session window visible — brighter cyan than idle.</summary>
        public static readonly Color FacetMultiplayerOn = new Color(0.20f, 0.52f, 0.60f, 1f);
        public static readonly Color FacetLicense = new Color(0.46f, 0.46f, 0.28f, 1f);
        public static readonly Color FacetUserTagsSide = new Color(0.24f, 0.40f, 0.42f, 1f);

        // ── User-tag editor state ───────────────────────────────────────────
        public static readonly Color UserTagOn = new Color(0.24f, 0.40f, 0.42f, 1f);
        public static readonly Color UserTagMixed = new Color(0.38f, 0.38f, 0.28f, 1f);
        public static readonly Color UserTagPulse = new Color(0.30f, 0.48f, 0.50f, 1f);
        public static readonly Color UserTagFilterOn = FacetTitleSearchActive;
        public static readonly Color UserTagFilterExclude = new Color(0.62f, 0.28f, 0.28f, 1f);
        public static readonly Color UserTagDropGlow = new Color(0.40f, 0.72f, 0.50f, 1f);

        // ── Title-search popup cue ──────────────────────────────────────────
        public static readonly Color TitleSearchPopupIdle = SurfacePanel;
        public static readonly Color TitleSearchPopupCue = new Color(0.36f, 0.36f, 0.38f, 1f);

        // ── Button ColorBlock multipliers (Unity Selectable) ────────────────
        public static readonly Color ButtonHighlight = new Color(1.2f, 1.2f, 1.2f, 1f);
        public static readonly Color ButtonPressed = new Color(0.8f, 0.8f, 0.8f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        // ── Segmented / clothing-mode idle (selected uses Accent*) ──────────
        public static readonly Color SegmentIdle = new Color(0.16f, 0.16f, 0.16f, 1f);
        public static readonly Color SegmentIdleText = TextDim;
        /// <summary>Default hover rim when facet not selected (yellow; high periphery pop).</summary>
        public static readonly Color HoverRimDefault = new Color(1f, 1f, 0f, 1f);
        /// <summary>Idle control edge — quiet luminance, not a white halo (Johnson contrast).</summary>
        public static readonly Color RimIdle = new Color(0.48f, 0.48f, 0.48f, 0.32f);
        /// <summary>Armed/selected edge — muted cool, not white-on-grey.</summary>
        public static readonly Color RimSelected = new Color(0.58f, 0.60f, 0.64f, 0.85f);
    }
}
