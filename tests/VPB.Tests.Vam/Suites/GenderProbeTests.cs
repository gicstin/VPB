using System;
using System.IO;
using SimpleJSON;
using VPB.src.util;
using Xunit;
using Xunit.Abstractions;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class GenderProbeTests
    {
        private readonly ITestOutputHelper _out;
        public GenderProbeTests(VamFixture vam, ITestOutputHelper output) { _out = output; }

        private static string VapWithCharacter(string characterName, bool useFemaleMorphsOnMale = false)
        {
            return "{\n" +
                   "  \"setUnlistedParamsToDefault\" : \"true\",\n" +
                   "  \"storables\" : [\n" +
                   "    { \"id\" : \"control\", \"position\" : { \"x\" : \"0\" } },\n" +
                   "    {\n" +
                   "      \"id\" : \"geometry\",\n" +
                   "      \"character\" : \"" + characterName + "\",\n" +
                   "      \"useFemaleMorphsOnMale\" : \"" + (useFemaleMorphsOnMale ? "true" : "false") + "\"\n" +
                   "    }\n" +
                   "  ]\n" +
                   "}\n";
        }

        [Theory]
        [InlineData("Female 1", LooseVapGenderProbe.Gender.Female)]
        [InlineData("Female", LooseVapGenderProbe.Gender.Female)]
        [InlineData("Male 1", LooseVapGenderProbe.Gender.Male)]
        [InlineData("Male", LooseVapGenderProbe.Gender.Male)]
        [InlineData("Futa 1", LooseVapGenderProbe.Gender.Futa)]
        [InlineData("Futa", LooseVapGenderProbe.Gender.Futa)]
        public void CharacterNamePrefixDecidesTheGender(string characterName, LooseVapGenderProbe.Gender expected)
        {
            Assert.Equal(expected, LooseVapGenderProbe.Resolve(characterName, false));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        [InlineData("SomeCustomCharacter")]
        [InlineData("Femalish")]
        [InlineData("Malevolent")]
        [InlineData("Futanari")]
        public void AnUnrecognisedCharacterNameIsUnknownRatherThanGuessed(string characterName)
        {
            LooseVapGenderProbe.Gender g = LooseVapGenderProbe.Resolve(characterName, false);
            Assert.Equal(LooseVapGenderProbe.Gender.Unknown, g);
        }

        [Fact]
        public void PrefixMatchingIsOnWholeTokensNotSubstrings()
        {
            Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.Resolve("Femalebot", false));
            Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.Resolve("Males", false));
            Assert.Equal(LooseVapGenderProbe.Gender.Female, LooseVapGenderProbe.Resolve("Female 2", false));
        }

        [Fact]
        public void UseFemaleMorphsOnMalePromotesMaleToFuta()
        {
            Assert.Equal(LooseVapGenderProbe.Gender.Futa, LooseVapGenderProbe.Resolve("Male 1", true));
        }

        [Fact]
        public void UseFemaleMorphsOnMaleDoesNotTouchFemaleOrUnknown()
        {
            Assert.Equal(LooseVapGenderProbe.Gender.Female, LooseVapGenderProbe.Resolve("Female 1", true));
            Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.Resolve("Custom", true));
            Assert.Equal(LooseVapGenderProbe.Gender.Futa, LooseVapGenderProbe.Resolve("Futa 1", true));
        }

        [Theory]
        [InlineData("Futa 1", true)]
        [InlineData("futa 1", true)]
        [InlineData("Futa", true)]
        [InlineData("Female 1", false)]
        [InlineData("Futanari", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void FutaDetectionMatchesTheWholeToken(string name, bool expected)
        {
            Assert.Equal(expected, LooseVapGenderProbe.IsFutaCharacterName(name));
        }

        [Fact]
        public void LeadingAndTrailingWhitespaceIsIgnored()
        {
            Assert.Equal(LooseVapGenderProbe.Gender.Female, LooseVapGenderProbe.Resolve("  Female 1  ", false));
            Assert.True(LooseVapGenderProbe.IsFutaCharacterName("  Futa 1  "));
        }

        [Theory]
        [InlineData("Female 1", LooseVapGenderProbe.Gender.Female)]
        [InlineData("Male 1", LooseVapGenderProbe.Gender.Male)]
        [InlineData("Futa 1", LooseVapGenderProbe.Gender.Futa)]
        public void StorablesAreClassifiedFromTheGeometryEntry(string characterName, LooseVapGenderProbe.Gender expected)
        {
            JSONNode node = JSON.Parse(VapWithCharacter(characterName));
            Assert.Equal(expected, LooseVapGenderProbe.ClassifyStorables(node));
        }

        [Fact]
        public void StorablesKeepFutaRatherThanFoldingItToMale()
        {
            JSONNode node = JSON.Parse(VapWithCharacter("Male 1", useFemaleMorphsOnMale: true));

            Assert.Equal(LooseVapGenderProbe.Gender.Futa, LooseVapGenderProbe.ClassifyStorables(node));
            _out.WriteLine("ClassifyStorables keeps Futa; Classify(file) folds it to Male for the appearance filter.");
        }

        [Fact]
        public void StorablesWithoutAGeometryEntryAreUnknown()
        {
            JSONNode node = JSON.Parse("{ \"storables\" : [ { \"id\" : \"control\" } ] }");
            Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.ClassifyStorables(node));
        }

        [Theory]
        [InlineData("{ }")]
        [InlineData("{ \"storables\" : [] }")]
        [InlineData("{ \"storables\" : \"not an array\" }")]
        [InlineData("{ \"storables\" : [ null ] }")]
        [InlineData("{ \"storables\" : [ { \"id\" : \"geometry\" } ] }")]
        public void MalformedStorablesAreUnknownRatherThanThrowing(string json)
        {
            LooseVapGenderProbe.Gender g = LooseVapGenderProbe.Gender.Female;
            Exception thrown = Record.Exception(() => g = LooseVapGenderProbe.ClassifyStorables(JSON.Parse(json)));

            Assert.True(thrown == null, "ClassifyStorables threw on malformed input: " +
                                        (thrown == null ? "" : HeadlessVam.DescribeUnwrapped(thrown)));
            Assert.Equal(LooseVapGenderProbe.Gender.Unknown, g);
        }

        [Fact]
        public void NullNodeIsUnknown()
        {
            Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.ClassifyStorables(null));
        }

        [Theory]
        [InlineData("Female 1", LooseVapGenderProbe.Gender.Female)]
        [InlineData("Male 1", LooseVapGenderProbe.Gender.Male)]
        public void ALooseVapFileOnDiskIsClassified(string characterName, LooseVapGenderProbe.Gender expected)
        {
            using (var install = new TempInstall("gender_file"))
            {
                LooseVapGenderProbe.InvalidateMemoryCache();

                string path = Path.Combine(
                    install.EnsureDir("Custom", "Atom", "Person", "Appearance"), "probe.vap");
                File.WriteAllText(path, VapWithCharacter(characterName));

                Assert.Equal(expected, LooseVapGenderProbe.Classify(path));
            }
        }

        [Fact]
        public void ClassifyFoldsFutaToMaleForTheAppearanceFilter()
        {
            using (var install = new TempInstall("gender_fold"))
            {
                LooseVapGenderProbe.InvalidateMemoryCache();

                string path = Path.Combine(
                    install.EnsureDir("Custom", "Atom", "Person", "Appearance"), "futa.vap");
                File.WriteAllText(path, VapWithCharacter("Futa 1"));

                LooseVapGenderProbe.Gender fromFile = LooseVapGenderProbe.Classify(path);
                _out.WriteLine("Classify(file) = " + fromFile);

                Assert.Equal(LooseVapGenderProbe.Gender.Male, fromFile);
                Assert.Equal(LooseVapGenderProbe.Gender.Futa,
                    LooseVapGenderProbe.ClassifyStorables(JSON.Parse(VapWithCharacter("Futa 1"))));
            }
        }

        [Fact]
        public void AMissingOrUnreadableFileIsUnknownRatherThanThrowing()
        {
            using (var install = new TempInstall("gender_missing"))
            {
                LooseVapGenderProbe.InvalidateMemoryCache();

                Assert.Equal(LooseVapGenderProbe.Gender.Unknown,
                    LooseVapGenderProbe.Classify(install.PathTo("Custom", "absent.vap")));
                Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.Classify(null));
                Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.Classify(""));

                string garbage = Path.Combine(install.EnsureDir("Custom"), "garbage.vap");
                File.WriteAllText(garbage, "this is not json {{{");
                Assert.Equal(LooseVapGenderProbe.Gender.Unknown, LooseVapGenderProbe.Classify(garbage));
            }
        }

        [Fact]
        public void GenderCodesMatchTheAppearanceFilterOrdering()
        {
            Assert.Equal(0, (int)LooseVapGenderProbe.Gender.Unknown);
            Assert.Equal(1, (int)LooseVapGenderProbe.Gender.Female);
            Assert.Equal(2, (int)LooseVapGenderProbe.Gender.Male);
            Assert.Equal(3, (int)LooseVapGenderProbe.Gender.Futa);
        }
    }
}
