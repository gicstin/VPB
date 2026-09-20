using System;
using System.Collections.Generic;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class SearchRescueTests
    {
        public SearchRescueTests(VamFixture vam) { }

        private static int Distance(string a, string b, int max)
        {
            var prev = new int[64];
            var cur = new int[64];
            return GallerySearchRescue.BoundedEditDistance(a, b, max, prev, cur);
        }

        [Theory]
        [InlineData("kanna", "kanna", 0)]
        [InlineData("kanna", "kana", 1)]
        [InlineData("kanna", "kannna", 1)]
        [InlineData("kanna", "kanma", 1)]
        [InlineData("kanna", "anna", 1)]
        [InlineData("hunting", "huntimg", 1)]
        public void SmallTyposStayInsideTheEditBudget(string a, string b, int expected)
        {
            Assert.Equal(expected, Distance(a, b, 2));
        }

        [Theory]
        [InlineData("kanna", "morphs")]
        [InlineData("scene", "clothing")]
        public void UnrelatedWordsExceedTheBudget(string a, string b)
        {
            Assert.True(Distance(a, b, 2) > 2);
        }

        [Fact]
        public void EditBudgetGrowsWithWordLength()
        {
            Assert.Equal(0, GallerySearchRescue.EditBudgetForLength(3));
            Assert.Equal(1, GallerySearchRescue.EditBudgetForLength(4));
            Assert.Equal(1, GallerySearchRescue.EditBudgetForLength(7));
            Assert.Equal(2, GallerySearchRescue.EditBudgetForLength(8));
        }

        [Fact]
        public void TwoBareWordsRelaxToAnOrQuery()
        {
            GallerySearchQuery q = GallerySearchQuery.Parse("kanna school");
            Assert.True(GallerySearchRescue.CanRelaxToAnyWord(q));
            Assert.Equal("kanna OR school", GallerySearchRescue.BuildAnyWordQuery(q));
        }

        [Theory]
        [InlineData("kanna")]
        [InlineData("kanna OR school")]
        [InlineData("kanna #wet")]
        [InlineData("kanna @creatorname")]
        [InlineData("kanna -school")]
        [InlineData("kanna badge:loaded")]
        public void StructuredOrSingleTermQueriesDoNotRelaxToAnyWord(string raw)
        {
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.False(GallerySearchRescue.CanRelaxToAnyWord(q));
            Assert.Null(GallerySearchRescue.BuildAnyWordQuery(q));
        }

        [Fact]
        public void TermReplacementRewritesOnlyTheBareWord()
        {
            const string raw = "kana #wet @somecreator";
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            string rewritten = GallerySearchRescue.BuildTermReplacementQuery(raw, q, "kana", "kanna");
            Assert.Equal("kanna #wet @somecreator", rewritten);
        }

        [Fact]
        public void TermReplacementLeavesTagAndCreatorAtomsAlone()
        {
            const string raw = "#kana @kana";
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            Assert.Null(GallerySearchRescue.BuildTermReplacementQuery(raw, q, "kana", "kanna"));
        }

        [Fact]
        public void BroadTokensAreCollectedWithoutStructuredAtoms()
        {
            const string raw = "kana school #wet @someone -old";
            GallerySearchQuery q = GallerySearchQuery.Parse(raw);
            var tokens = new List<string>();
            GallerySearchRescue.CollectBroadTokens(raw, q, tokens);
            Assert.Equal(2, tokens.Count);
            Assert.Contains("kana", tokens);
            Assert.Contains("school", tokens);
        }

        private static bool Chunks(string term, string original, out int chunks)
        {
            string word;
            int[] segments;
            chunks = 0;
            if (!VpbLocalDatabase.TryBuildVocabularyWord(original, out word, out segments)) return false;
            return GallerySearchRescue.TryChunkMatch(term, word, segments, out chunks);
        }

        [Theory]
        [InlineData("AcidBubbles", "acidbubbles", new[] { 0, 4 })]
        [InlineData("MeshedVR", "meshedvr", new[] { 0, 6 })]
        [InlineData("Hunting-Succubus", "huntingsuccubus", new[] { 0, 7 })]
        [InlineData("VRHair", "vrhair", new[] { 0, 2 })]
        [InlineData("Timeline", "timeline", new[] { 0 })]
        [InlineData("kemenate", "kemenate", new[] { 0 })]
        public void SegmentsFollowCamelCaseAndSeparators(string original, string expectedWord, int[] expectedStarts)
        {
            string word;
            int[] segments;
            Assert.True(VpbLocalDatabase.TryBuildVocabularyWord(original, out word, out segments));
            Assert.Equal(expectedWord, word);
            Assert.Equal(expectedStarts, segments);
        }

        [Theory]
        [InlineData("acibub", "AcidBubbles", 2)]
        [InlineData("hunsucc", "Hunting-Succubus", 2)]
        [InlineData("meshvr", "MeshedVR", 2)]
        [InlineData("acibubbles", "AcidBubbles", 2)]
        public void AbbreviationsMatchAcrossSegments(string term, string original, int expectedChunks)
        {
            int chunks;
            Assert.True(Chunks(term, original, out chunks));
            Assert.Equal(expectedChunks, chunks);
        }

        [Theory]
        [InlineData("acid", "AcidBubbles")]
        [InlineData("bubbles", "AcidBubbles")]
        [InlineData("acidbubbles", "AcidBubbles")]
        [InlineData("acidbub", "AcidBubbles")]
        public void PlainSubstringsAreNotSuggested(string term, string original)
        {
            int chunks;
            Assert.False(Chunks(term, original, out chunks));
        }

        [Theory]
        [InlineData("xyzw", "AcidBubbles")]
        [InlineData("bubaci", "AcidBubbles")]
        [InlineData("abu", "AcidBubbles")]
        [InlineData("acbu", "AcidBubbles")]
        [InlineData("succhun", "Hunting-Succubus")]
        public void NonMatchesAndOutOfOrderChunksAreRejected(string term, string original)
        {
            int chunks;
            Assert.False(Chunks(term, original, out chunks));
        }

        [Fact]
        public void ChunkMatchNeedsAtLeastFourCharacters()
        {
            int chunks;
            Assert.False(Chunks("acb", "AcidBubbles", out chunks));
        }
    }
}
