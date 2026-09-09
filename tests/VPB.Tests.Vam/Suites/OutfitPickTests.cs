using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SimpleJSON;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class OutfitPickTests
    {
        private readonly ITestOutputHelper _out;
        public OutfitPickTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static JSONClass Appearance(string clothingEntries, string hairEntries = "", string extraStorables = "")
        {
            string json =
                "{\n" +
                "  \"storables\" : [\n" +
                "    {\n" +
                "      \"id\" : \"geometry\",\n" +
                "      \"character\" : \"Female 1\",\n" +
                "      \"clothing\" : [\n" + clothingEntries + "\n      ],\n" +
                "      \"hair\" : [\n" + hairEntries + "\n      ]\n" +
                "    }" + (string.IsNullOrEmpty(extraStorables) ? "" : ",\n" + extraStorables) + "\n" +
                "  ]\n" +
                "}\n";
            return JSON.Parse(json).AsObject;
        }

        private static string ClothingEntry(string uid, string internalId = null, bool enabled = true)
        {
            return "        { \"id\" : \"" + uid + "\"" +
                   (internalId != null ? ", \"internalId\" : \"" + internalId + "\"" : "") +
                   (enabled ? "" : ", \"enabled\" : \"false\"") + " }";
        }

        private static List<AppearanceOutfitPickItem> Pick(JSONClass preset, bool includeSkinAndHair = false)
        {
            return VpbImport.ListAppearanceOutfitItems(null, preset, includeSkinAndHair);
        }

        private static int ParseClothingItemIndex(string id)
        {
            MethodInfo m = typeof(VpbImport).GetMethod("ParseClothingItemIndex",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.True(m != null,
                "VpbImport.ParseClothingItemIndex is gone. It decodes the ordinal in a 'clothingItem#N' storable id; " +
                "if it was renamed, update this test rather than deleting it.");
            return (int)m.Invoke(null, new object[] { id });
        }

        [Fact]
        public void ClothingEntriesBecomePickItems()
        {
            JSONClass preset = Appearance(
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", "dress") + ",\n" +
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/boots/boots.vam", "boots"));

            List<AppearanceOutfitPickItem> items = Pick(preset);
            foreach (AppearanceOutfitPickItem i in items)
                _out.WriteLine(i.Kind + " " + i.CategoryLabel + " " + i.DisplayName + "  <- " + i.Uid);

            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.DisplayName == "dress");
            Assert.Contains(items, i => i.DisplayName == "boots");
        }

        [Fact]
        public void DisabledEntriesAreSkipped()
        {
            JSONClass preset = Appearance(
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", "dress") + ",\n" +
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/boots/boots.vam", "boots", enabled: false));

            List<AppearanceOutfitPickItem> items = Pick(preset);

            Assert.Single(items);
            Assert.Equal("dress", items[0].DisplayName);
        }

        [Fact]
        public void DuplicateUidsAppearOnce()
        {
            string entry = ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", "dress");
            JSONClass preset = Appearance(entry + ",\n" + entry);

            Assert.Single(Pick(preset));
        }

        [Fact]
        public void DisplayNameFallsBackToTheFileStemWhenInternalIdIsMissing()
        {
            JSONClass preset = Appearance(
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/a dress/a dress.vam"));

            List<AppearanceOutfitPickItem> items = Pick(preset);

            Assert.Single(items);
            Assert.Equal("a dress", items[0].DisplayName);
        }

        [Fact]
        public void HairIsOnlyIncludedWhenAsked()
        {
            JSONClass preset = Appearance(
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", "dress"),
                ClothingEntry("Creator.Hair.1:/Custom/Hair/Female/bob/bob.vam", "bob"));

            Assert.Single(Pick(preset));

            List<AppearanceOutfitPickItem> withHair = Pick(preset, includeSkinAndHair: true);
            Assert.Contains(withHair, i => i.CategoryLabel == "Hair" && i.DisplayName == "bob");
        }

        [Fact]
        public void AnEmptyOrMalformedPresetYieldsNoItems()
        {
            Assert.Empty(VpbImport.ListAppearanceOutfitItems(null, JSON.Parse("{ }").AsObject));
            Assert.Empty(VpbImport.ListAppearanceOutfitItems(null, JSON.Parse("{ \"storables\" : [] }").AsObject));
            Assert.Empty(VpbImport.ListAppearanceOutfitItems(null, Appearance("")));
        }

        [Theory]
        [InlineData("clothingItem#0", 0)]
        [InlineData("clothingItem#3", 3)]
        [InlineData("clothingItem#12", 12)]
        [InlineData("ClothingItem#7", 7)]
        [InlineData("prefix:clothingItem#4", 4)]
        [InlineData("clothingItem#2suffix", 2)]
        public void ClothingItemOrdinalIsParsedFromTheStorableId(string id, int expected)
        {
            Assert.Equal(expected, ParseClothingItemIndex(id));
        }

        [Theory]
        [InlineData("clothingItem#")]
        [InlineData("clothingItem")]
        [InlineData("hairItem#3")]
        [InlineData("geometry")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("clothingItem#x")]
        public void NonOrdinalStorableIdsReportMinusOne(string id)
        {
            Assert.Equal(-1, ParseClothingItemIndex(id));
        }

        [Fact]
        public void TheOrdinalIsNotAnArrayPosition()
        {
            _out.WriteLine("clothingItem#N is VaM's own storable ordinal for the person atom. It is NOT an index");
            _out.WriteLine("into the preset's clothing array: a preset can carry clothingItem#5 while listing two");
            _out.WriteLine("garments. Material storables must be matched by their 'url' against the garment uid,");
            _out.WriteLine("with the ordinal used only as a last-resort fallback.");

            Assert.Equal(5, ParseClothingItemIndex("clothingItem#5"));

            JSONClass preset = Appearance(
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", "dress") + ",\n" +
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/boots/boots.vam", "boots"));

            List<AppearanceOutfitPickItem> items = Pick(preset);
            Assert.Equal(2, items.Count);
            Assert.True(ParseClothingItemIndex("clothingItem#5") >= items.Count,
                "This fixture exists to state the invariant: an ordinal can exceed the number of listed garments, " +
                "so indexing the clothing array with it reads out of range or hits the wrong garment.");
        }

        [Fact]
        public void EveryPickItemCarriesACategoryLabel()
        {
            JSONClass preset = Appearance(
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", "dress") + ",\n" +
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/earrings/earrings.vam", "earrings") + ",\n" +
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/eyeliner/eyeliner.vam", "eyeliner"));

            List<AppearanceOutfitPickItem> items = Pick(preset);
            foreach (AppearanceOutfitPickItem i in items)
                _out.WriteLine(i.DisplayName + " -> " + i.CategoryLabel);

            Assert.Equal(3, items.Count);
            foreach (AppearanceOutfitPickItem i in items)
                Assert.False(string.IsNullOrEmpty(i.CategoryLabel),
                    i.DisplayName + " has no category label; the picker groups on it.");
        }

        [Fact]
        public void EntriesWithoutAnIdAreIgnored()
        {
            JSONClass preset = Appearance(
                "        { \"internalId\" : \"orphan\" },\n" +
                ClothingEntry("Creator.Pack.1:/Custom/Clothing/Female/dress/dress.vam", "dress"));

            List<AppearanceOutfitPickItem> items = Pick(preset);
            Assert.Single(items);
            Assert.Equal("dress", items[0].DisplayName);
        }
    }
}
