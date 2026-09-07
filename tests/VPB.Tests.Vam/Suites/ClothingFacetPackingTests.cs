using System;
using System.Collections.Generic;
using VPB;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests.Vam
{
    [Collection(VamCollection.Name)]
    public class ClothingFacetPackingTests
    {
        private readonly ITestOutputHelper _out;
        public ClothingFacetPackingTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static readonly string[] VarListPaths =
        {
            "Creator.Pack.1:/Custom/Clothing/Female/Creator/Dress/dress.vam",
            "Creator.Pack.1:/Custom/Clothing/Male/Creator/Shirt/shirt.vam",
            "Creator.Pack.1:/Custom/Clothing/Neutral/Creator/Cape/cape.vam",
            "Creator.Pack.1:/Custom/Clothing/Female/Creator/Dress/variant.vap",
            "Creator.Pack.1:/Custom/Clothing/Male/Creator/Shirt/variant.vap",
            "Creator.Pack.1:/Custom/Clothing/Female/Creator/Decal/tattoo.vam",
            "Creator.Pack.1:/Custom/Clothing/Female/Creator/Decal/tattoo.vap",
            "Creator.Pack.1:/Custom/Hair/Female/Creator/Long/long.vam",
            "Creator.Pack.1:/Custom/Hair/Male/Creator/Short/short.vap",
            "Creator.Pack.1:/Custom/Atom/Person/Appearance/look.vap",
            "Creator.Pack.1:/Saves/scene/scene.json",
        };

        [Fact]
        public void ThePackedClothingFacetAgreesWithThePathPredicateOnEveryFlagCombination()
        {
            var divergences = new List<string>();
            int compared = 0;
            int combinations = 1 << Enum.GetValues(typeof(GalleryPanel.ClothingSubfilter)).Length;

            for (int bits = 0; bits < combinations; bits++)
            {
                var f = (GalleryPanel.ClothingSubfilter)bits;

                foreach (string listPath in VarListPaths)
                {
                    bool byPath = GalleryPanel.PassesClothingGalleryFiltersForPath(listPath, f, true);
                    bool byPacked = VpbLocalDatabase.ClothingPackedAttrMatchesSubfilter(
                        VpbLocalDatabase.PackClothingGalleryAttrForVarListPath(listPath), f);
                    compared++;

                    if (byPath != byPacked)
                        divergences.Add(listPath + "  flags=" + FlagText(f) +
                                        "  path=" + byPath + "  packed=" + byPacked);
                }
            }

            _out.WriteLine("compared " + compared + " (path, subfilter) pairs");

            Assert.True(divergences.Count == 0,
                "The packed clothing facet disagrees with the path predicate:" + Environment.NewLine +
                Bullets(divergences) + Environment.NewLine + Environment.NewLine +
                "These two are hand-mirrored: PassesClothingGalleryFiltersForPath filters loose entries and " +
                "VAR rows the SQL worker did not precompute, ClothingPackedAttrMatchesSubfilter filters the " +
                "indexed VAR rows. When they drift the same item appears in the grid or not depending on " +
                "whether the index happened to cover it - which reads as a random gallery.");
        }

        [Fact]
        public void ThePackedHairFacetAgreesWithThePathPredicateOnEveryFlagCombination()
        {
            var divergences = new List<string>();
            int hairFlagCount = Enum.GetValues(typeof(GalleryPanel.HairSubfilter)).Length;
            int combinations = 1 << hairFlagCount;

            for (int bits = 0; bits < combinations; bits++)
            {
                var f = (GalleryPanel.HairSubfilter)bits;

                foreach (string listPath in VarListPaths)
                {
                    bool byPath = GalleryPanel.PassesHairGalleryFiltersForPath(listPath, f, true);
                    bool byPacked = VpbLocalDatabase.HairPackedAttrMatchesSubfilter(
                        VpbLocalDatabase.PackClothingGalleryAttrForVarListPath(listPath), f);

                    if (byPath != byPacked)
                        divergences.Add(listPath + "  flags=" + f + "  path=" + byPath + "  packed=" + byPacked);
                }
            }

            Assert.True(divergences.Count == 0,
                "The packed hair facet disagrees with the path predicate:" + Environment.NewLine +
                Bullets(divergences) + Environment.NewLine + Environment.NewLine +
                "Hair shares PackClothingGalleryAttrForVarListPath with clothing, so a change made for one " +
                "silently moves the other.");
        }

        [Fact]
        public void ThePackedFacetCarriesKindGenderPresetAndDecalSeparately()
        {
            int dress = VpbLocalDatabase.PackClothingGalleryAttrForVarListPath(
                "Creator.Pack.1:/Custom/Clothing/Female/Creator/Dress/dress.vam");
            int preset = VpbLocalDatabase.PackClothingGalleryAttrForVarListPath(
                "Creator.Pack.1:/Custom/Clothing/Female/Creator/Dress/dress.vap");

            Assert.NotEqual(0, dress);
            Assert.NotEqual(dress, preset);

            Assert.Equal(dress & 0xF, preset & 0xF);
            Assert.Equal((dress >> 4) & 0xF, (preset >> 4) & 0xF);

            Assert.True((preset & 0x100) != 0, "The .vap must set the preset bit.");
            Assert.True((dress & 0x100) == 0, "The .vam must not set the preset bit.");

            Assert.Equal(0, VpbLocalDatabase.PackClothingGalleryAttrForVarListPath(null));
            Assert.Equal(0, VpbLocalDatabase.PackClothingGalleryAttrForVarListPath(""));
        }

        private static string FlagText(GalleryPanel.ClothingSubfilter f)
        {
            return f == 0 ? "(none)" : f.ToString();
        }

        private static string Bullets(List<string> lines)
        {
            var sb = new System.Text.StringBuilder();
            int shown = Math.Min(lines.Count, 25);
            for (int i = 0; i < shown; i++) sb.AppendLine("  - " + lines[i]);
            if (lines.Count > shown) sb.AppendLine("  ... and " + (lines.Count - shown) + " more");
            return sb.ToString();
        }
    }
}
