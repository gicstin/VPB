using System;
using System.Collections.Generic;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class SearchQueryTests
    {
        public SearchQueryTests(VamFixture vam) { }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankInputParsesToAnEmptyQuery(string raw)
        {
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.NotNull(q);
            Assert.Empty(q.Branches);
        }

        [Fact]
        public void BareTermsBecomeBroadTerms()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("alpha beta");
            Assert.Single(q.Branches);
            Assert.Contains("alpha", q.Branches[0].BroadTerms);
            Assert.Contains("beta", q.Branches[0].BroadTerms);
        }

        [Fact]
        public void OrStartsANewBranch()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("alpha or beta");
            Assert.Equal(2, q.Branches.Count);
            Assert.Contains("alpha", q.Branches[0].BroadTerms);
            Assert.Contains("beta", q.Branches[1].BroadTerms);
        }

        [Fact]
        public void AndIsTheDefaultAndIsIgnoredAsAKeyword()
        {
            GallerySearchQuery withKeyword = GallerySearchQuery.Parse("alpha and beta");
            GallerySearchQuery without = GallerySearchQuery.Parse("alpha beta");
            Assert.Equal(without.Branches.Count, withKeyword.Branches.Count);
            Assert.Equal(without.Branches[0].BroadTerms.Count, withKeyword.Branches[0].BroadTerms.Count);
        }

        [Theory]
        [InlineData("tag:wip", "wip")]
        [InlineData("#wip", "wip")]
        public void TagAtomsBecomeTagIncludes(string raw, string expected)
        {
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.Single(q.Branches);
            Assert.Contains(expected, q.Branches[0].TagInclude);
        }

        [Theory]
        [InlineData("-tag:wip", "wip")]
        [InlineData("-#wip", "wip")]
        public void NegatedTagAtomsBecomeTagExcludes(string raw, string expected)
        {
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.Single(q.Branches);
            Assert.Contains(expected, q.Branches[0].TagExclude);
            Assert.DoesNotContain(expected, q.Branches[0].TagInclude);
        }

        [Fact]
        public void CommaSeparatedTagListsExpandToSeparateTags()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("tag:one,two,three");
            List<string> tags = q.Branches[0].TagInclude;
            Assert.Contains("one", tags);
            Assert.Contains("two", tags);
            Assert.Contains("three", tags);
        }

        [Fact]
        public void ParsingIsCaseInsensitiveForKeywords()
        {
            Assert.Equal(2, GallerySearchQuery.Parse("alpha OR beta").Branches.Count);
            Assert.Equal(2, GallerySearchQuery.Parse("alpha Or beta").Branches.Count);
        }

        [Fact]
        public void TrailingOrDoesNotCreateAnEmptyBranch()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("alpha or");
            foreach (GallerySearchBranch b in q.Branches)
                Assert.False(b.IsEmpty, "Parse produced an empty branch, which matches everything.");
        }

        [Fact]
        public void QuotingKeepsAPhraseAsASingleTerm()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("\"two words\"");
            Assert.Single(q.Branches);
            Assert.Single(q.Branches[0].BroadTerms);
            Assert.Contains("two words", q.Branches[0].BroadTerms[0]);
        }

        [Fact]
        public void QuotingSurvivesAlongsideOtherAtoms()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("\"two words\" tag:wip");
            Assert.Single(q.Branches);
            Assert.Single(q.Branches[0].BroadTerms);
            Assert.Contains("wip", q.Branches[0].TagInclude);
        }

        [Fact]
        public void ParseNeverThrowsOnHostileInput()
        {
            string[] hostile =
            {
                "-", "-tag:", "tag:", "#", "-#", "\"", "\"unterminated",
                "or or or", "and", "if", ":", ",,,", "tag:,,", "-", "  -tag:,x  ",
                "creator:", "-creator:", "pack:", "hubcat:", "\\", "()[]{}",
            };

            foreach (string raw in hostile)
            {
                GallerySearchQuery q = GallerySearchQuery.Parse(raw);
                Assert.NotNull(q);
            }
        }

        [Fact]
        public void EmptyQueryFactoryHasNoBranches()
        {
            Assert.Empty(GallerySearchQuery.Empty.Branches);
        }

        [Theory]
        [InlineData("creator:someone", "someone")]
        [InlineData("@someone", "someone")]
        [InlineData("creator:SomeOne", "someone")]
        public void CreatorAtomsBecomeCreatorTerms(string raw, string expected)
        {
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.Single(q.Branches);
            Assert.Contains(expected, q.Branches[0].CreatorTerms);
        }

        [Fact]
        public void CreatorListsSplitOnCommas()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("creator:alpha,beta");
            Assert.Contains("alpha", q.Branches[0].CreatorTerms);
            Assert.Contains("beta", q.Branches[0].CreatorTerms);
        }

        [Theory]
        [InlineData("loaded", "Loaded")]
        [InlineData("installed", "Loaded")]
        [InlineData("unloaded", "Unloaded")]
        [InlineData("starred", "Starred")]
        [InlineData("rated", "Starred")]
        [InlineData("star", "Starred")]
        [InlineData("unrated", "Unrated")]
        [InlineData("not-rated", "Unrated")]
        [InlineData("notrated", "Unrated")]
        [InlineData("unstarred", "Unrated")]
        [InlineData("tagged", "Tagged")]
        [InlineData("untagged", "Untagged")]
        [InlineData("autoinstall", "AutoInstall")]
        [InlineData("auto-install", "AutoInstall")]
        [InlineData("hidden", "Hidden")]
        [InlineData("hide", "Hidden")]
        [InlineData("whitelist", "ScanExcluded")]
        [InlineData("whitelisted", "ScanExcluded")]
        [InlineData("scanexcluded", "ScanExcluded")]
        [InlineData("scan-excluded", "ScanExcluded")]
        public void StatusWordsSetTheirFlagAndAreNotTreatedAsText(string raw, string expectedFlagName)
        {
            var expected = (GallerySearchQuery.StatusFlags)Enum.Parse(
                typeof(GallerySearchQuery.StatusFlags), expectedFlagName);

            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.Single(q.Branches);
            Assert.Equal(expected, q.Branches[0].Status & expected);
            Assert.Empty(q.Branches[0].BroadTerms);
        }

        [Fact]
        public void StatusFlagsAccumulateWithinABranch()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("loaded starred untagged");
            GallerySearchQuery.StatusFlags status = q.Branches[0].Status;

            Assert.True((status & GallerySearchQuery.StatusFlags.Loaded) != 0);
            Assert.True((status & GallerySearchQuery.StatusFlags.Starred) != 0);
            Assert.True((status & GallerySearchQuery.StatusFlags.Untagged) != 0);
        }

        [Fact]
        public void StatusFlagsDoNotLeakAcrossOrBranches()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("loaded or starred");
            Assert.Equal(2, q.Branches.Count);

            Assert.True((q.Branches[0].Status & GallerySearchQuery.StatusFlags.Loaded) != 0);
            Assert.True((q.Branches[0].Status & GallerySearchQuery.StatusFlags.Starred) == 0);
            Assert.True((q.Branches[1].Status & GallerySearchQuery.StatusFlags.Starred) != 0);
            Assert.True((q.Branches[1].Status & GallerySearchQuery.StatusFlags.Loaded) == 0);
        }

        [Theory]
        [InlineData("looks:blonde")]
        [InlineData("hubtag:realistic")]
        [InlineData("hubcat:scenes")]
        [InlineData("lap:anything")]
        public void DataPackAtomsAreRecognisedAndNotTreatedAsText(string raw)
        {
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.Single(q.Branches);
            Assert.True(q.Branches[0].HasDataPackAtoms, raw + " did not register as a data-pack atom.");
            Assert.Empty(q.Branches[0].BroadTerms);
        }

        [Theory]
        [InlineData("looks:blonde")]
        [InlineData("-looks:blonde")]
        public void DataPackAtomListsAreOnlyAllocatedWhenUsed(string raw)
        {
            GallerySearchQuery none = GallerySearchQuery.Parse("plain term");
            Assert.False(none.Branches[0].HasDataPackAtoms);
            Assert.Null(none.Branches[0].PackSubjectInclude);
            Assert.Null(none.Branches[0].PackSubjectExclude);

            GallerySearchQuery used = GallerySearchQuery.Parse(raw);
            Assert.True(used.Branches[0].HasDataPackAtoms);
        }

        [Fact]
        public void NegatedDataPackAtomsGoToTheExcludeSide()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("-hubtag:realistic");
            GallerySearchBranch branch = q.Branches[0];

            Assert.NotNull(branch.PackHubTagExclude);
            Assert.Contains("realistic", branch.PackHubTagExclude);
            Assert.True(branch.PackHubTagInclude == null || branch.PackHubTagInclude.Count == 0);
        }

        [Fact]
        public void DataPackListsSplitOnCommasAndHonourPerItemNegation()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("hubtag:one,-two,three");
            GallerySearchBranch branch = q.Branches[0];

            Assert.Contains("one", branch.PackHubTagInclude);
            Assert.Contains("three", branch.PackHubTagInclude);
            Assert.Contains("two", branch.PackHubTagExclude);
            Assert.DoesNotContain("two", branch.PackHubTagInclude);
        }

        [Fact]
        public void QuotedDataPackValuesBecomeAnExactMatchTerm()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("hubtag:\"two words\"");
            Assert.Contains("=two words", q.Branches[0].PackHubTagInclude);
        }

        [Theory]
        [InlineData("badge:ai")]
        [InlineData("badge:auto")]
        [InlineData("badge:autoinstall")]
        [InlineData("badge:auto-install")]
        [InlineData("badge:a")]
        public void BadgeAliasesMapOntoStatusFlags(string raw)
        {
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.Single(q.Branches);
            Assert.True((q.Branches[0].Status & GallerySearchQuery.StatusFlags.AutoInstall) != 0,
                raw + " did not set the AutoInstall status flag.");
        }

        [Fact]
        public void BareCommaListIsTreatedAsTags()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("wet,shiny,-nsfw");
            GallerySearchBranch branch = q.Branches[0];

            Assert.Contains("wet", branch.TagInclude);
            Assert.Contains("shiny", branch.TagInclude);
            Assert.Contains("nsfw", branch.TagExclude);
        }

        [Fact]
        public void BareMinusTermBecomesABroadExclude()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("-wet");
            Assert.Contains("wet", q.Branches[0].BroadExclude);
            Assert.DoesNotContain("wet", q.Branches[0].TagExclude);
        }

        [Fact]
        public void IfIsAcceptedAsAnOptionalPrefaceAndDroppped()
        {
            GallerySearchQuery withIf = GallerySearchQuery.Parse("if loaded");
            Assert.Single(withIf.Branches);
            Assert.Empty(withIf.Branches[0].BroadTerms);
            Assert.True((withIf.Branches[0].Status & GallerySearchQuery.StatusFlags.Loaded) != 0);
        }

        [Fact]
        public void MixedAtomsAllLandInTheSameBranch()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("dress creator:alpha tag:wip -tag:old hubtag:realistic starred");
            GallerySearchBranch branch = Assert.Single(q.Branches);

            Assert.Contains("dress", branch.BroadTerms);
            Assert.Contains("alpha", branch.CreatorTerms);
            Assert.Contains("wip", branch.TagInclude);
            Assert.Contains("old", branch.TagExclude);
            Assert.Contains("realistic", branch.PackHubTagInclude);
            Assert.True((branch.Status & GallerySearchQuery.StatusFlags.Starred) != 0);
        }

        [Fact]
        public void WithoutBroadTermsKeepsStructuredAtomsOnly()
        {
            GallerySearchQuery full = GallerySearchQuery.Parse("dress creator:alpha tag:wip starred");
            GallerySearchQuery narrowed = full.WithoutBroadTerms();

            GallerySearchBranch branch = Assert.Single(narrowed.Branches);
            Assert.Empty(branch.BroadTerms);
            Assert.Contains("alpha", branch.CreatorTerms);
            Assert.Contains("wip", branch.TagInclude);
            Assert.True((branch.Status & GallerySearchQuery.StatusFlags.Starred) != 0);
        }

        [Fact]
        public void WithoutBroadTermsOnATextOnlyQueryYieldsAnEmptyQuery()
        {
            GallerySearchQuery textOnly = GallerySearchQuery.Parse("just some words");
            Assert.Empty(textOnly.WithoutBroadTerms().Branches);
        }
    }
}
