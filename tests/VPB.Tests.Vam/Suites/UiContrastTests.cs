using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class UiContrastTests
    {
        private const double BodyTextMinimum = 4.5;

        private readonly ITestOutputHelper _out;
        public UiContrastTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static double Channel(float c)
        {
            double v = c;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static double Luminance(Color c)
        {
            return 0.2126 * Channel(c.r) + 0.7152 * Channel(c.g) + 0.0722 * Channel(c.b);
        }

        internal static double ContrastRatio(Color a, Color b)
        {
            double la = Luminance(a);
            double lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private void AssertReadable(string role, Color text, IDictionary<string, Color> fills)
        {
            var failing = new List<string>();
            foreach (KeyValuePair<string, Color> fill in fills)
            {
                double ratio = ContrastRatio(text, fill.Value);
                _out.WriteLine(role + " on " + fill.Key + ": " + ratio.ToString("0.00", CultureInfo.InvariantCulture));
                if (ratio < BodyTextMinimum)
                    failing.Add(fill.Key + "  " + ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1");
            }
            Assert.True(failing.Count == 0,
                role + " labels are drawn on these fills at 16 px, below the 4.5:1 WCAG AA minimum for body text." + Environment.NewLine +
                "In the game the selected row, chip or header is the hardest one to read, and worse through a VR lens:" + Environment.NewLine +
                HarmonyPatchTargetTests.Bullets(failing));
        }

        private static Dictionary<string, Color> SelectedFacetFills()
        {
            return new Dictionary<string, Color>
            {
                { "FacetCategory", GalleryUiColorTokens.FacetCategory },
                { "FacetCreator", GalleryUiColorTokens.FacetCreator },
                { "FacetLooksLike", GalleryUiColorTokens.FacetLooksLike },
                { "FacetRating", GalleryUiColorTokens.FacetRating },
                { "FacetLicense", GalleryUiColorTokens.FacetLicense },
                { "FacetSceneImport", GalleryUiColorTokens.FacetSceneImport },
                { "FacetTag", GalleryUiColorTokens.FacetTag },
                { "FacetHistory", GalleryUiColorTokens.FacetHistory },
                { "FacetHistoryAccent", GalleryUiColorTokens.FacetHistoryAccent },
                { "FacetSource", GalleryUiColorTokens.FacetSource },
                { "FacetHubType", GalleryUiColorTokens.FacetHubType },
                { "FacetHub", GalleryUiColorTokens.FacetHub },
                { "FacetSubfilter", GalleryUiColorTokens.FacetSubfilter },
                { "AccentSelected", GalleryUiColorTokens.AccentSelected },
                { "AccentConfirm", GalleryUiColorTokens.AccentConfirm },
                { "AccentDanger", GalleryUiColorTokens.AccentDanger },
                { "AccentDangerStrong", GalleryUiColorTokens.AccentDangerStrong },
                { "AccentNew", GalleryUiColorTokens.AccentNew },
                { "ActiveOn", GalleryUiColorTokens.ActiveOn },
                { "ActiveSelected", GalleryUiColorTokens.ActiveSelected },
                { "ActiveFocus", GalleryUiColorTokens.ActiveFocus },
                { "PopupRowActive", GalleryUiColorTokens.PopupRowActive },
            };
        }

        private static Dictionary<string, Color> ChromeSurfaces()
        {
            return new Dictionary<string, Color>
            {
                { "SurfaceDeep", GalleryUiColorTokens.SurfaceDeep },
                { "SurfaceDarker", GalleryUiColorTokens.SurfaceDarker },
                { "SurfaceDark", GalleryUiColorTokens.SurfaceDark },
                { "SurfacePanel", GalleryUiColorTokens.SurfacePanel },
                { "RowIdle", GalleryUiColorTokens.RowIdle },
                { "PopupRowIdle", GalleryUiColorTokens.PopupRowIdle },
                { "ModalSurface", GalleryUiColorTokens.ModalSurface },
                { "SegmentIdle", GalleryUiColorTokens.SegmentIdle },
            };
        }

        [Fact]
        public void PrimaryTextIsReadableOnEverySelectedFacetFill()
        {
            AssertReadable("TextPrimary", GalleryUiColorTokens.TextPrimary, SelectedFacetFills());
        }

        [Fact]
        public void OnAccentTextIsReadableOnEverySelectedFacetFill()
        {
            AssertReadable("TextOnAccent", GalleryUiColorTokens.TextOnAccent, SelectedFacetFills());
        }

        [Fact]
        public void SecondaryTextIsReadableOnChromeSurfaces()
        {
            AssertReadable("TextMuted", GalleryUiColorTokens.TextMuted, ChromeSurfaces());
            AssertReadable("TextDim", GalleryUiColorTokens.TextDim, ChromeSurfaces());
        }

        [Fact]
        public void PlaceholderTextIsReadableInInputWells()
        {
            AssertReadable("TextPlaceholder", GalleryUiColorTokens.TextPlaceholder, new Dictionary<string, Color>
            {
                { "SurfaceDeep", GalleryUiColorTokens.SurfaceDeep },
                { "SurfaceDarker", GalleryUiColorTokens.SurfaceDarker },
                { "SurfaceDark", GalleryUiColorTokens.SurfaceDark },
                { "ModalSurface", GalleryUiColorTokens.ModalSurface },
            });
        }
    }
}
