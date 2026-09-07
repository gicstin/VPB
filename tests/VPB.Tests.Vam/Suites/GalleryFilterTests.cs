using System;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class GalleryFilterTests
    {
        private const string VarClothingItem = "Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam";
        private const string VarClothingPreset = "Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vap";
        private const string VarHairItem = "Creator.Hair.1:/Custom/Hair/Female/bob/bob.vam";
        private const string VarHairPreset = "Creator.Hair.1:/Custom/Hair/Female/bob/bob.vap";
        private const string LooseClothingItem = "Custom/Clothing/Female/dress/dress.vam";
        private const string LooseClothingPreset = "Custom/Atom/Person/Clothing/mypreset.vap";

        private readonly ITestOutputHelper _out;
        public GalleryFilterTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static bool Clothing(string path, GalleryPanel.ClothingSubfilter filter, bool inVar)
        {
            return GalleryPanel.PassesClothingGalleryFiltersForPath(path, filter, inVar);
        }

        private static bool Hair(string path, GalleryPanel.HairSubfilter filter, bool inVar)
        {
            return GalleryPanel.PassesHairGalleryFiltersForPath(path, filter, inVar);
        }

        [Fact]
        public void TheDefaultClothingViewShowsItemsAndHidesPresets()
        {
            Assert.True(Clothing(VarClothingItem, 0, true));
            Assert.False(Clothing(VarClothingPreset, 0, true),
                "The default clothing view must hide .vap presets. Showing them doubles every garment " +
                "in the list and the duplicate does nothing useful when clicked.");
        }

        [Fact]
        public void TheDefaultHairViewShowsItemsAndHidesPresets()
        {
            Assert.True(Hair(VarHairItem, 0, true));
            Assert.False(Hair(VarHairPreset, 0, true));
        }

        [Fact]
        public void ClothingAndHairNeverLeakIntoEachOther()
        {
            Assert.False(Clothing(VarHairItem, 0, true),
                "A hair item passed the clothing filter. Clothing and hair share the .vam extension, so " +
                "the only thing keeping them apart is the path classification.");
            Assert.False(Hair(VarClothingItem, 0, true));
        }

        [Fact]
        public void ThePresetsToggleShowsPresetsAndHidesBaseItems()
        {
            Assert.True(Clothing(VarClothingPreset, GalleryPanel.ClothingSubfilter.Presets, true));
            Assert.False(Clothing(VarClothingItem, GalleryPanel.ClothingSubfilter.Presets, true),
                "With Presets selected the view must not still list base items - the toggle would do nothing " +
                "visible and the user would think it is broken.");
        }

        [Fact]
        public void TheItemsToggleHidesPresets()
        {
            Assert.True(Clothing(VarClothingItem, GalleryPanel.ClothingSubfilter.Items, true));
            Assert.False(Clothing(VarClothingPreset, GalleryPanel.ClothingSubfilter.Items, true));
        }

        [Fact]
        public void APackagedPresetIsNotTreatedAsACustomPreset()
        {
            Assert.False(Clothing(VarClothingPreset, GalleryPanel.ClothingSubfilter.CustomPreset, true),
                "A .vap inside a VAR is not the user's own custom preset. Counting it as one puts other " +
                "creators' presets into the Custom Preset view.");

            Assert.True(Clothing(LooseClothingPreset, GalleryPanel.ClothingSubfilter.CustomPreset, inVar: false));
        }

        [Fact]
        public void ACustomToggleWantsLooseItemsOnly()
        {
            Assert.False(Clothing(VarClothingItem, GalleryPanel.ClothingSubfilter.Custom, true),
                "A packaged garment is not a custom item.");
            Assert.True(Clothing(LooseClothingItem, GalleryPanel.ClothingSubfilter.Custom, inVar: false));
        }

        [Theory]
        [InlineData("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", true)]
        [InlineData("Creator.Pack.1:/Custom/Clothing/Male/suit/suit.vam", false)]
        public void TheFemaleToggleDropsMaleFolderContent(string path, bool expected)
        {
            bool passes = Clothing(path, GalleryPanel.ClothingSubfilter.Female, true);
            _out.WriteLine(path + " -> " + passes);

            Assert.Equal(expected, passes);
        }

        [Theory]
        [InlineData("Creator.Pack.1:/Custom/Clothing/Male/suit/suit.vam", true)]
        [InlineData("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", false)]
        public void TheMaleToggleDropsFemaleFolderContent(string path, bool expected)
        {
            Assert.Equal(expected, Clothing(path, GalleryPanel.ClothingSubfilter.Male, true));
        }

        [Fact]
        public void AnUngenderedItemSurvivesEitherGenderToggle()
        {
            const string ungendered = "Creator.Pack.1:/Custom/Clothing/dress/dress.vam";

            _out.WriteLine("male   -> " + Clothing(ungendered, GalleryPanel.ClothingSubfilter.Male, true));
            _out.WriteLine("female -> " + Clothing(ungendered, GalleryPanel.ClothingSubfilter.Female, true));

            Assert.True(Clothing(ungendered, GalleryPanel.ClothingSubfilter.Male, true),
                "Content that is not in a gendered folder must stay visible under either toggle. Hiding it " +
                "silently loses a large share of older VaM content from both views.");
            Assert.True(Clothing(ungendered, GalleryPanel.ClothingSubfilter.Female, true));
        }

        [Fact]
        public void NonClothingPathsAreRejectedOutright()
        {
            Assert.False(Clothing("Creator.Pack.1:/Saves/scene/one.json", 0, true));
            Assert.False(Clothing("Creator.Pack.1:/Custom/Atom/Person/Appearance/look.vap", 0, true));
            Assert.False(Clothing("", 0, true));
            Assert.False(Clothing(null, 0, true));
        }

        [Fact]
        public void NonHairPathsAreRejectedOutright()
        {
            Assert.False(Hair("Creator.Pack.1:/Saves/scene/one.json", 0, true));
            Assert.False(Hair("", 0, true));
            Assert.False(Hair(null, 0, true));
        }

        [Fact]
        public void BackslashPathsFilterTheSameAsForwardSlashOnes()
        {
            const string forward = "Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam";
            string backward = forward.Replace('/', '\\');

            Assert.Equal(Clothing(forward, 0, true), Clothing(backward, 0, true));
            Assert.Equal(
                Clothing(forward, GalleryPanel.ClothingSubfilter.Female, true),
                Clothing(backward, GalleryPanel.ClothingSubfilter.Female, true));
        }

        [Theory]
        [InlineData(LooseVapGenderProbeGender.Female, "Female", true)]
        [InlineData(LooseVapGenderProbeGender.Male, "Female", false)]
        [InlineData(LooseVapGenderProbeGender.Male, "Male", true)]
        [InlineData(LooseVapGenderProbeGender.Female, "Male", false)]
        [InlineData(LooseVapGenderProbeGender.Unknown, "Female", false)]
        [InlineData(LooseVapGenderProbeGender.Unknown, "Unknown", true)]
        [InlineData(LooseVapGenderProbeGender.Futa, "Futa", true)]
        [InlineData(LooseVapGenderProbeGender.Futa, "Female", false)]
        [InlineData(LooseVapGenderProbeGender.Futa, "Male", false)]
        [InlineData(LooseVapGenderProbeGender.Female, "Futa", false)]
        [InlineData(LooseVapGenderProbeGender.Male, "Futa", false)]
        public void AppearanceGenderSubfilterMatchesTheClassifiedGender(
            int genderCode, string filterName, bool expected)
        {
            var filter = (GalleryPanel.AppearanceSubfilter)Enum.Parse(
                typeof(GalleryPanel.AppearanceSubfilter), filterName);

            bool passes = AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(genderCode, filter);
            _out.WriteLine("gender " + genderCode + " under " + filter + " -> " + passes);

            Assert.Equal(expected, passes);
        }

        [Fact]
        public void FutaIsItsOwnChipAndIsNotFoldedIntoEitherGender()
        {
            var futa = (GalleryPanel.AppearanceSubfilter)Enum.Parse(typeof(GalleryPanel.AppearanceSubfilter), "Futa");
            var female = (GalleryPanel.AppearanceSubfilter)Enum.Parse(typeof(GalleryPanel.AppearanceSubfilter), "Female");
            var male = (GalleryPanel.AppearanceSubfilter)Enum.Parse(typeof(GalleryPanel.AppearanceSubfilter), "Male");

            Assert.True(AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(LooseVapGenderProbeGender.Futa, futa));

            Assert.False(AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(LooseVapGenderProbeGender.Futa, female),
                "Futa looks must not appear under the Female chip alone. The appearance filter keeps Futa as a " +
                "separate gender - LooseVapGenderProbe.Classify only folds Futa to Male for the file-level probe.");
            Assert.False(AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(LooseVapGenderProbeGender.Futa, male),
                "Nor under Male alone.");

            Assert.True(AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(LooseVapGenderProbeGender.Futa, futa | female),
                "With both chips on, a Futa look must be visible.");
        }

        [Fact]
        public void AnAppearanceFilterOfNoneKeepsEverything()
        {
            for (int gender = 0; gender <= 3; gender++)
                Assert.True(AppearanceGenderClassifier.PassesAppearanceGenderSubfilter(gender, 0),
                    "With no gender toggle selected, gender " + gender + " was hidden. An unset filter must " +
                    "not narrow the list.");
        }

        private static class LooseVapGenderProbeGender
        {
            public const int Unknown = 0;
            public const int Female = 1;
            public const int Male = 2;
            public const int Futa = 3;
        }
    }
}
