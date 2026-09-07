using System;
using System.Collections.Generic;
using SimpleJSON;
using VPB.src.util;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class PresetPathFixupTests
    {
        private const string SourcePath = "Creator.Pack.1:/Custom/Atom/Person/Appearance/look.vap";

        private readonly ITestOutputHelper _out;
        public PresetPathFixupTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static JSONClass Preset(string clothingUrl)
        {
            string json =
                "{\n" +
                "  \"storables\" : [\n" +
                "    {\n" +
                "      \"id\" : \"geometry\",\n" +
                "      \"clothing\" : [ { \"id\" : \"" + clothingUrl + "\" } ]\n" +
                "    }\n" +
                "  ]\n" +
                "}\n";
            return JSON.Parse(json).AsObject;
        }

        private static string ClothingId(JSONClass preset)
        {
            return preset["storables"].AsArray[0].AsObject["clothing"].AsArray[0].AsObject["id"].Value;
        }

        private static List<string> AllStrings(JSONNode node)
        {
            var values = new List<string>();
            Walk(node, values);
            return values;
        }

        private static void Walk(JSONNode node, List<string> values)
        {
            if (node == null) return;
            JSONArray array = node as JSONArray;
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++) Walk(array[i], values);
                return;
            }
            JSONClass obj = node as JSONClass;
            if (obj != null)
            {
                foreach (string key in obj.Keys) Walk(obj[key], values);
                return;
            }
            if (!string.IsNullOrEmpty(node.Value)) values.Add(node.Value);
        }

        [Fact]
        public void SelfPrefixIsReplacedWithTheOwningPackageUid()
        {
            JSONClass preset = Preset("SELF:/Custom/Clothing/Female/dress/dress.vam");

            VarPresetPathFixups.Apply(preset, SourcePath);

            string id = ClothingId(preset);
            _out.WriteLine("SELF:/... -> " + id);

            Assert.StartsWith("Creator.Pack.1:", id, StringComparison.Ordinal);
            Assert.DoesNotContain("SELF:", id, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SelfPrefixIsReplacedEverywhereNotJustTheFirstHit()
        {
            string json =
                "{\n" +
                "  \"storables\" : [\n" +
                "    { \"id\" : \"geometry\",\n" +
                "      \"clothing\" : [\n" +
                "        { \"id\" : \"SELF:/Custom/Clothing/Female/a/a.vam\" },\n" +
                "        { \"id\" : \"SELF:/Custom/Clothing/Female/b/b.vam\" }\n" +
                "      ],\n" +
                "      \"hair\" : [ { \"id\" : \"SELF:/Custom/Hair/Female/h/h.vam\" } ] }\n" +
                "  ]\n" +
                "}\n";
            JSONClass preset = JSON.Parse(json).AsObject;

            VarPresetPathFixups.Apply(preset, SourcePath);

            foreach (string value in AllStrings(preset))
                Assert.DoesNotContain("SELF:", value, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AnAlreadyQualifiedReferenceToAnotherPackageIsLeftAlone()
        {
            const string other = "Other.Pack.2:/Custom/Clothing/Female/dress/dress.vam";
            JSONClass preset = Preset(other);

            VarPresetPathFixups.Apply(preset, SourcePath);

            Assert.Equal(other, ClothingId(preset));
        }

        [Fact]
        public void ALooseSourcePathLeavesThePresetUntouched()
        {
            const string original = "SELF:/Custom/Clothing/Female/dress/dress.vam";
            JSONClass preset = Preset(original);

            VarPresetPathFixups.Apply(preset, "Custom/Atom/Person/Appearance/look.vap");

            Assert.Equal(original, ClothingId(preset));
            _out.WriteLine("A loose (non-package) preset has no owning package, so SELF: cannot be resolved " +
                           "and must be left for the caller rather than rewritten to something wrong.");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void AMissingSourcePathIsANoOp(string sourcePath)
        {
            const string original = "SELF:/Custom/Clothing/Female/dress/dress.vam";
            JSONClass preset = Preset(original);

            Exception thrown = Record.Exception(() => VarPresetPathFixups.Apply(preset, sourcePath));

            Assert.True(thrown == null,
                "Apply must tolerate a missing source path: " +
                (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
            Assert.Equal(original, ClothingId(preset));
        }

        [Fact]
        public void ANullPresetIsANoOp()
        {
            Exception thrown = Record.Exception(() => VarPresetPathFixups.Apply(null, SourcePath));
            Assert.True(thrown == null,
                "Apply must tolerate a null preset: " + (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
        }

        [Fact]
        public void AnEmptyPresetIsANoOp()
        {
            JSONClass preset = JSON.Parse("{ }").AsObject;
            Exception thrown = Record.Exception(() => VarPresetPathFixups.Apply(preset, SourcePath));
            Assert.True(thrown == null,
                "Apply must tolerate an empty preset: " + (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
        }

        [Fact]
        public void ApplyIsIdempotent()
        {
            JSONClass preset = Preset("SELF:/Custom/Clothing/Female/dress/dress.vam");

            VarPresetPathFixups.Apply(preset, SourcePath);
            string afterFirst = ClothingId(preset);

            VarPresetPathFixups.Apply(preset, SourcePath);
            string afterSecond = ClothingId(preset);

            Assert.Equal(afterFirst, afterSecond);
            _out.WriteLine("Running the fixups twice must not double-prefix: " + afterSecond);
        }

        [Fact]
        public void SpacedPackageNamesSurviveTheRewrite()
        {
            JSONClass preset = Preset("SELF:/Custom/Clothing/Female/dress/dress.vam");

            VarPresetPathFixups.Apply(preset, "Some Creator.Pack Name.2:/Custom/Atom/Person/Appearance/look.vap");

            string id = ClothingId(preset);
            _out.WriteLine("spaced -> " + id);

            Assert.StartsWith("Some Creator.Pack Name.2:", id, StringComparison.Ordinal);
        }

        [Fact]
        public void ResolveOwnerlessMorphPathsHandlesAPresetWithNoMorphs()
        {
            JSONClass preset = Preset("SELF:/Custom/Clothing/Female/dress/dress.vam");

            List<string> owners = null;
            Exception thrown = Record.Exception(() => owners = VarPresetPathFixups.ResolveOwnerlessMorphPaths(preset));

            Assert.True(thrown == null,
                "ResolveOwnerlessMorphPaths threw on a preset with no morph references: " +
                (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
            Assert.NotNull(owners);
            Assert.Empty(owners);
        }
    }
}
