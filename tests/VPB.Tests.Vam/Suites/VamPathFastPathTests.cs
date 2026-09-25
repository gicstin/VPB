using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Xunit;

namespace VPB.Tests
{
    [Collection(VamCollection.Name)]
    public class VamPathFastPathTests
    {
        public VamPathFastPathTests(VamFixture vam) { }

        [Theory]
        [InlineData("Custom/Clothing/Female/Creator/Item/Item.vam")]
        [InlineData("Creator.Pack.3:/Custom/Hair/Female/x.vam")]
        [InlineData("AddonPackages/Creator.Pack.3.var:")]
        [InlineData("noslash.vmi")]
        [InlineData("trailing/")]
        [InlineData("/leading")]
        [InlineData("//")]
        [InlineData("")]
        [InlineData("a\\b/c\\d")]
        [InlineData("first/line\nsecond/line")]
        [InlineData("x/y\nz")]
        [InlineData("tab\t/name\r")]
        public void EntryNamesMatchVamsRegexExactly(string path)
        {
            string expected = Regex.Replace(path, ".*/", string.Empty);
            Assert.True(expected == VamPathFastPaths.StripThroughLastSlash(path),
                "A file or directory entry got a different Name than VaM computes for '" + path.Replace("\n", "\\n")
                + "', so clothing, hair and morph lookups by name miss the entry.");
            Assert.Equal(expected, VamPathFastPaths.RegexReplace(path, ".*/", string.Empty));
        }

        [Fact]
        public void OtherRegexReplacementsStillRunThroughTheRegexEngine()
        {
            Assert.Equal(Regex.Replace("Creator.Pack.12", "\\.[0-9]+$", string.Empty),
                VamPathFastPaths.RegexReplace("Creator.Pack.12", "\\.[0-9]+$", string.Empty));
            Assert.Equal(Regex.Replace("a/b/c", ".*/", "X"), VamPathFastPaths.RegexReplace("a/b/c", ".*/", "X"));
            Assert.Throws<ArgumentNullException>(() => VamPathFastPaths.RegexReplace(null, ".*/", string.Empty));
        }

        [Fact]
        public void EveryVamEntryConstructorStillComputesItsNameThroughARedirectableRegexCall()
        {
            var missing = new List<string>();
            foreach (ConstructorInfo ctor in VamPathFastPaths.VamEntryConstructors())
            {
                if (ctor == null)
                {
                    missing.Add("(constructor not found)");
                    continue;
                }
                int before = VamPathFastPaths.RedirectedRegexCalls;
                List<CodeInstruction> rewritten = VamPathFastPaths
                    .RedirectRegexReplace(PatchProcessor.GetOriginalInstructions(ctor))
                    .ToList();
                if (VamPathFastPaths.RedirectedRegexCalls == before)
                    missing.Add(ctor.DeclaringType.FullName);
                Assert.DoesNotContain(rewritten, i =>
                {
                    var m = i.operand as MethodInfo;
                    return m != null && m.DeclaringType == typeof(Regex) && m.Name == "Replace";
                });
            }

            Assert.True(missing.Count == 0,
                "These VaM entry constructors no longer call Regex.Replace(string, string, string), so every package scan " +
                "pays the regex per zip entry again: " + string.Join(", ", missing.ToArray()));
        }

        [Fact]
        public void APatchedVamEntryConstructorStillProducesVamsName()
        {
            var harmony = new Harmony("vpb.tests.entry-name-fast-path");
            ConstructorInfo ctor = AccessTools.Constructor(typeof(MVR.FileManagement.FileEntry), new[] { typeof(string) });
            try
            {
                harmony.Patch(ctor, transpiler: new HarmonyMethod(typeof(VamPathFastPaths), "RedirectRegexReplace"));

                var nested = new MVR.FileManagement.SystemFileEntry("Custom\\Clothing/Female/Creator/Item.vam");
                var flat = new MVR.FileManagement.SystemFileEntry("Item.vam");

                Assert.True(nested.Name == "Item.vam",
                    "VaM's file entry ctor produced '" + nested.Name + "' after the fast path was patched in, so every file " +
                    "browser and catalog lookup by name breaks.");
                Assert.Equal("Item.vam", flat.Name);
            }
            finally
            {
                harmony.Unpatch(ctor, HarmonyPatchType.Transpiler, harmony.Id);
            }
        }

        [Theory]
        [InlineData("Custom/Atom/Person/Textures/skin.jpg")]
        [InlineData("Saves/scene/My.Scene.json")]
        [InlineData("Creator.Pack.latest/not-a-reference")]
        [InlineData("")]
        public void PathsWithoutAColonNeverMatchVamsPackageReferencePatterns(string path)
        {
            Assert.DoesNotMatch("^(([^\\.]+\\.[^\\.]+)\\.latest):", path);
            Assert.DoesNotMatch("^(([^\\.]+\\.[^\\.]+)\\.min([0-9]+)):", path);
            Assert.DoesNotMatch("^([^\\.]+\\.[^\\.]+\\.[0-9]+):", path);

            string result = null;
            Assert.False(VamPathFastPaths.SkipNormalizeCommonWithoutPackageReference(path, ref result),
                "A colon-free path was handed to VaM's three package-reference regexes although none of them can match it.");
            Assert.Same(path, result);
        }

        [Theory]
        [InlineData("Creator.Pack.latest:/Custom/x.vam")]
        [InlineData("Creator.Pack.12:/Custom/x.vam")]
        [InlineData(null)]
        public void PackageReferencesStillReachVamsNormalizeCommon(string path)
        {
            string result = "untouched";
            Assert.True(VamPathFastPaths.SkipNormalizeCommonWithoutPackageReference(path, ref result),
                "A package reference skipped VaM's version resolution, so '.latest' and missing-version references load the " +
                "wrong package or nothing.");
            Assert.Equal("untouched", result);
        }
    }
}
