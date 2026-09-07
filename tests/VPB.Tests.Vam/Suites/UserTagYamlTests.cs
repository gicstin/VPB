using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class UserTagYamlTests
    {
        private readonly ITestOutputHelper _out;
        public UserTagYamlTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static string Key(string category, string pkgUid, string internalPath)
        {
            return GalleryUserTagYamlBrain.EncodeItemKey(category, pkgUid, internalPath);
        }

        private static Dictionary<string, List<string>> Map(params KeyValuePair<string, List<string>>[] pairs)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> pair in pairs) map[pair.Key] = pair.Value;
            return map;
        }

        private static KeyValuePair<string, List<string>> Entry(string key, params string[] values)
        {
            return new KeyValuePair<string, List<string>>(key, new List<string>(values));
        }

        private static void AssertSameMap(
            IDictionary<string, List<string>> expected, IDictionary<string, List<string>> actual, string what)
        {
            Assert.Equal(expected.Count, actual.Count);
            foreach (KeyValuePair<string, List<string>> pair in expected)
            {
                List<string> got;
                Assert.True(actual.TryGetValue(pair.Key, out got), what + " lost the entry '" + pair.Key + "'.");

                var wanted = new List<string>(pair.Value);
                var found = new List<string>(got);
                wanted.Sort(StringComparer.Ordinal);
                found.Sort(StringComparer.Ordinal);
                Assert.Equal(wanted, found);
            }
        }

        [Theory]
        [InlineData("Scenes", "Creator.Pack.1", "Saves/scene/one.json")]
        [InlineData("Clothing", "Some Creator.Pack Name.2", "Custom/Clothing/Female/a dress/a dress.vam")]
        [InlineData("Hair", "Créateur.Pack.3", "Custom/Hair/Female/é/é.vam")]
        public void ItemKeysRoundTrip(string category, string pkgUid, string internalPath)
        {
            string encoded = Key(category, pkgUid, internalPath);

            string c, p, i;
            Assert.True(GalleryUserTagYamlBrain.TryDecodeItemKey(encoded, out c, out p, out i),
                "Could not decode the key this build just encoded: " + encoded.Replace('\t', '|'));

            Assert.Equal(category, c);
            Assert.Equal(pkgUid, p);
            Assert.Equal(internalPath, i);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("no separators at all")]
        [InlineData("only\tone")]
        [InlineData("too\tmany\tseparators\there")]
        [InlineData("\t\t")]
        [InlineData("Scenes\t\tSaves/scene/one.json")]
        public void MalformedItemKeysAreRejected(string raw)
        {
            string c, p, i;
            Assert.False(GalleryUserTagYamlBrain.TryDecodeItemKey(raw, out c, out p, out i),
                "Accepted a malformed item key. A half-decoded key silently re-tags the wrong row on import.");
        }

        [Fact]
        public void TagToItemsSurvivesAnExportImportRoundTrip()
        {
            var tagToItems = Map(
                Entry("favourite",
                    Key("Scenes", "Creator.Pack.1", "Saves/scene/one.json"),
                    Key("Clothing", "Creator.Dress.2", "Custom/Clothing/Female/d/d.vam")),
                Entry("wip",
                    Key("Scenes", "Creator.Pack.1", "Saves/scene/two.json")),
                Entry("vocabulary only"));

            string yaml = GalleryUserTagYamlBrain.BuildTagToItemsYaml(tagToItems, null);
            _out.WriteLine(yaml);

            Dictionary<string, List<string>> parsedTagToItems;
            Dictionary<string, List<string>> parsedItemToTags;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categories;
            string error;

            Assert.True(GalleryUserTagYamlBrain.TryParseImport(
                yaml, out parsedTagToItems, out parsedItemToTags, out categories, out error),
                "Import rejected this build's own export: " + error);

            AssertSameMap(tagToItems, parsedTagToItems, "tag -> items");
        }

        [Fact]
        public void ItemToTagsSurvivesAnExportImportRoundTrip()
        {
            var itemToTags = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                { Key("Scenes", "Creator.Pack.1", "Saves/scene/one.json"), new List<string> { "favourite", "wip" } },
                { Key("Hair", "Creator.Hair.1", "Custom/Hair/Female/h/h.vam"), new List<string> { "long" } },
            };

            string yaml = GalleryUserTagYamlBrain.BuildItemToTagsYaml(itemToTags, null);
            _out.WriteLine(yaml);

            Dictionary<string, List<string>> parsedTagToItems;
            Dictionary<string, List<string>> parsedItemToTags;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categories;
            string error;

            Assert.True(GalleryUserTagYamlBrain.TryParseImport(
                yaml, out parsedTagToItems, out parsedItemToTags, out categories, out error),
                "Import rejected this build's own export: " + error);

            AssertSameMap(itemToTags, parsedItemToTags, "item -> tags");
        }

        [Fact]
        public void BothLayoutsCarryTheSameInformation()
        {
            string itemKey = Key("Scenes", "Creator.Pack.1", "Saves/scene/one.json");
            var tagToItems = Map(Entry("favourite", itemKey));
            var itemToTags = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                { itemKey, new List<string> { "favourite" } },
            };

            Dictionary<string, List<string>> fromTagFirst, unusedItemMap, unusedTagMap, fromItemFirst;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> catsA, catsB;
            string errorA, errorB;

            Assert.True(GalleryUserTagYamlBrain.TryParseImport(
                GalleryUserTagYamlBrain.BuildTagToItemsYaml(tagToItems, null),
                out fromTagFirst, out unusedItemMap, out catsA, out errorA), errorA);

            Assert.True(GalleryUserTagYamlBrain.TryParseImport(
                GalleryUserTagYamlBrain.BuildItemToTagsYaml(itemToTags, null),
                out unusedTagMap, out fromItemFirst, out catsB, out errorB), errorB);

            Assert.Empty(unusedItemMap);
            Assert.Empty(unusedTagMap);

            AssertSameMap(fromTagFirst, Invert(fromItemFirst),
                "the two export layouts disagree - exporting in one layout and importing the other loses tags");
        }

        private static Dictionary<string, List<string>> Invert(IDictionary<string, List<string>> itemToTags)
        {
            var tagToItems = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<string>> pair in itemToTags)
            {
                foreach (string tag in pair.Value)
                {
                    List<string> items;
                    if (!tagToItems.TryGetValue(tag, out items) || items == null)
                    {
                        items = new List<string>();
                        tagToItems[tag] = items;
                    }
                    items.Add(pair.Key);
                }
            }
            return tagToItems;
        }

        [Fact]
        public void CategoriesWithColoursRoundTrip()
        {
            var categories = new List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml>
            {
                new GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml
                {
                    Name = "Mood", Color = "#ff8800", Tags = new List<string> { "happy", "dark" },
                },
                new GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml
                {
                    Name = "Status", Color = "#0088ff", Tags = new List<string> { "wip" },
                },
            };

            var tagToItems = Map(Entry("happy", Key("Scenes", "Creator.Pack.1", "Saves/scene/one.json")));
            string yaml = GalleryUserTagYamlBrain.BuildTagToItemsYaml(tagToItems, categories);
            _out.WriteLine(yaml);

            Dictionary<string, List<string>> a, b;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> parsed;
            string error;
            Assert.True(GalleryUserTagYamlBrain.TryParseImport(yaml, out a, out b, out parsed, out error), error);

            Assert.Equal(2, parsed.Count);
            Assert.Contains(parsed, c => c.Name == "Mood" && c.Color == "#ff8800");
            Assert.Contains(parsed, c => c.Name == "Status" && c.Color == "#0088ff");

            foreach (GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml c in parsed)
            {
                if (c.Name != "Mood") continue;
                Assert.Contains("happy", c.Tags);
                Assert.Contains("dark", c.Tags);
            }
        }

        [Theory]
        [InlineData("tag: with colon")]
        [InlineData("tag \"with quotes\"")]
        [InlineData("tag #with hash")]
        [InlineData("tag - with dash")]
        [InlineData("  leading and trailing  ")]
        [InlineData("tag\\with\\backslash")]
        [InlineData("é unicode ✓")]
        public void TagNamesNeedingQuotingSurviveTheRoundTrip(string tag)
        {
            string itemKey = Key("Scenes", "Creator.Pack.1", "Saves/scene/one.json");
            var tagToItems = Map(Entry(tag, itemKey));

            string yaml = GalleryUserTagYamlBrain.BuildTagToItemsYaml(tagToItems, null);

            Dictionary<string, List<string>> parsed, ignored;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categories;
            string error;
            Assert.True(GalleryUserTagYamlBrain.TryParseImport(yaml, out parsed, out ignored, out categories, out error),
                "Import failed for tag '" + tag + "': " + error);

            Assert.Single(parsed);
            foreach (KeyValuePair<string, List<string>> pair in parsed)
            {
                Assert.Equal(tag.Trim(), pair.Key.Trim());
                Assert.Contains(itemKey, pair.Value);
            }
        }

        [Fact]
        public void AnEmptyExportStillImports()
        {
            string yaml = GalleryUserTagYamlBrain.BuildTagToItemsYaml(
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase), null);

            Dictionary<string, List<string>> a, b;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categories;
            string error;

            Assert.True(GalleryUserTagYamlBrain.TryParseImport(yaml, out a, out b, out categories, out error),
                "An export with no tags must still be a valid import: " + error);
            Assert.Empty(a);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("just some text that is not yaml at all")]
        [InlineData("key: value\nother: thing")]
        public void UnrecognisedInputIsRejectedWithAReason(string yaml)
        {
            Dictionary<string, List<string>> a, b;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categories;
            string error;

            Assert.False(GalleryUserTagYamlBrain.TryParseImport(yaml, out a, out b, out categories, out error),
                "Accepted input that is not a VPB tag export. Importing an arbitrary file must fail loudly, " +
                "not silently wipe the user's tags with an empty result.");
            Assert.False(string.IsNullOrEmpty(error), "A rejected import must say why.");
        }

        [Fact]
        public void ALeadingByteOrderMarkDoesNotBreakImport()
        {
            var tagToItems = Map(Entry("favourite", Key("Scenes", "Creator.Pack.1", "Saves/scene/one.json")));
            string yaml = "﻿" + GalleryUserTagYamlBrain.BuildTagToItemsYaml(tagToItems, null);

            Dictionary<string, List<string>> parsed, ignored;
            List<GalleryUserTagYamlBrain.GalleryUserTagCategoryYaml> categories;
            string error;

            Assert.True(GalleryUserTagYamlBrain.TryParseImport(yaml, out parsed, out ignored, out categories, out error),
                "A UTF-8 BOM (what Notepad writes) must not break import: " + error);
            Assert.Single(parsed);
        }
    }
}
